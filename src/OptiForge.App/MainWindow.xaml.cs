using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using OptiForge.AI;
using OptiForge.Core;
using OptiForge.Core.Models;
using OptiForge.SysMon;
using OptiForge.Update;

namespace OptiForge.App;

public partial class MainWindow : Window
{
    private readonly PortablePaths _paths;
    private readonly AppSettings _settings;
    private readonly SystemMonitor _monitor = new();
    private readonly DiskCleaner _cleaner = new();
    private readonly LocalAiService _ai;
    private readonly UpdateChecker _updates = new();
    private readonly AutoUpdater _autoUpdater;
    private readonly AiBootstrapper _aiSetup;
    private readonly NetworkThrottle _net = new();
    private readonly StartupManager _startup = new();
    private readonly DispatcherTimer _timer;
    private readonly ObservableCollection<CleanRow> _cleanRows = [];
    private readonly ObservableCollection<Playbook> _playbooks = [];
    private bool _cleanBusy;

    public MainWindow()
    {
        InitializeComponent();

        _paths = new PortablePaths();
        _settings = AppSettings.Load(_paths);
        _ai = new LocalAiService(_paths, _settings);
        _autoUpdater = new AutoUpdater(_paths);
        _aiSetup = new AiBootstrapper(_paths);

        RootPathText.Text = "Root: " + _paths.Root;
        SettingsPathText.Text = "Settings: " + _paths.SettingsPath;
        DryRunCheck.IsChecked = _settings.DryRunCleanByDefault;
        AdminStatusText.Text = RamTrimmer.IsRunningAsAdministrator()
            ? "Running as Administrator."
            : "Not admin ? standby/QoS limited; working-set trim still works.";

        foreach (var def in _cleaner.Categories)
        {
            _cleanRows.Add(new CleanRow
            {
                Id = def.Id,
                DisplayName = def.DisplayName,
                Selected = def.SelectedByDefault,
                Note = def.Description
            });
        }
        CleanList.ItemsSource = _cleanRows;

        foreach (var pb in new PlaybookStore(_paths).LoadAll())
            _playbooks.Add(pb);
        PlaybookList.ItemsSource = _playbooks;
        if (_playbooks.Count > 0)
            PlaybookList.SelectedIndex = 0;

        VersionText.Text = "Version: " + _updates.CurrentVersion;
        RefreshStartupList();

        _ = RefreshAiStatusAsync();
        RefreshMonitor();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.MonitorIntervalMs, 500, 5000))
        };
        _timer.Tick += (_, _) => RefreshMonitor();
        _timer.Start();

        Closed += async (_, _) =>
        {
            _timer.Stop();
            _monitor.Dispose();
            await _ai.DisposeAsync();
        };
    }

    private void RefreshMonitor()
    {
        var s = _monitor.GetSnapshot();
        CpuText.Text = $"{s.CpuPercent:0}%";
        RamText.Text = $"{s.RamUsedPercent:0}%";
        RamDetailText.Text = $"{s.RamUsedGb:0.0} / {s.RamTotalGb:0.0} GB";
        var diskUsedPct = s.DiskTotalGb > 0
            ? (1 - s.DiskFreeGb / s.DiskTotalGb) * 100
            : 0;
        DiskText.Text = $"{diskUsedPct:0}% used";
        DiskDetailText.Text = $"{s.SystemDrive}  free {s.DiskFreeGb:0.0} / {s.DiskTotalGb:0.0} GB";
        StatusText.Text = s.Timestamp.ToString("HH:mm:ss");
    }

    private async Task RefreshAiStatusAsync()
    {
        var st = await _ai.GetStatusAsync();
        AiStatusText.Text = st.Message;
        if (!string.IsNullOrWhiteSpace(st.ActiveModelPath))
            AiMetaText.Text = "Model: " + Path.GetFileName(st.ActiveModelPath);
    }

    private async void RefreshAi_Click(object sender, RoutedEventArgs e)
        => await RefreshAiStatusAsync();

    private async void SetupAi_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Download llama-server + Qwen2.5 3B GGUF (~2GB) into this portable drive?\nNeeds internet. May take a while.",
            "OptiForge AI setup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        SetupAiBtn.IsEnabled = false;
        AiSetupProgress.Visibility = Visibility.Visible;
        AiSetupProgress.Value = 0;
        try
        {
            var progress = new Progress<BootstrapProgress>(p =>
            {
                AiSetupProgress.Value = p.Percent;
                AiSetupDetail.Text = p.Detail;
            });
            var (ok, msg) = await _aiSetup.EnsureAllAsync(progress);
            AiStatusText.Text = msg;
            AiSetupDetail.Text = ok ? "Setup done." : msg;
            if (!ok)
                MessageBox.Show(msg, "OptiForge AI", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            AiSetupDetail.Text = ex.Message;
            MessageBox.Show(ex.Message, "OptiForge AI", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetupAiBtn.IsEnabled = true;
            await RefreshAiStatusAsync();
        }
    }

    private async void StartAi_Click(object sender, RoutedEventArgs e)
    {
        StartAiBtn.IsEnabled = false;
        AiStatusText.Text = "Starting llama-server?";
        try
        {
            var (ok, msg) = await _ai.StartServerAsync();
            AiStatusText.Text = msg;
            if (!ok)
                MessageBox.Show(msg, "OptiForge AI", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            AiStatusText.Text = ex.Message;
        }
        finally
        {
            StartAiBtn.IsEnabled = true;
            await RefreshAiStatusAsync();
        }
    }

    private async void StopAi_Click(object sender, RoutedEventArgs e)
    {
        await _ai.StopServerAsync();
        AiStatusText.Text = "Server stopped.";
        await RefreshAiStatusAsync();
    }

    private async void SendAi_Click(object sender, RoutedEventArgs e)
    {
        var prompt = AiPromptBox.Text?.Trim() ?? "";
        if (prompt.Length == 0) return;

        SendAiBtn.IsEnabled = false;
        AiResponseBox.Text = "Thinking?";
        AiMetaText.Text = "";
        try
        {
            var useCache = UseCacheCheck.IsChecked == true;
            var (fromCache, response) = await _ai.ChatAsync(prompt, useCache);
            AiResponseBox.Text = response;
            AiMetaText.Text = fromCache ? "Cache hit" : "Fresh generation";
        }
        catch (Exception ex)
        {
            AiResponseBox.Text = ex.Message;
        }
        finally
        {
            SendAiBtn.IsEnabled = true;
        }
    }

    private async void TrimRam_Click(object sender, RoutedEventArgs e)
    {
        TrimRamBtn.IsEnabled = false;
        RamTrimSummaryText.Text = "Trimming?";
        var clearStandby = ClearStandbyCheck.IsChecked == true;
        try
        {
            var report = await Task.Run(() => new RamTrimmer().Trim(clearStandby));
            RamTrimSummaryText.Text = report.Summary;
            RefreshMonitor();
            StatusText.Text = "RAM trim done";
        }
        catch (Exception ex)
        {
            RamTrimSummaryText.Text = "Error: " + ex.Message;
        }
        finally
        {
            TrimRamBtn.IsEnabled = true;
        }
    }

    private void NetApply_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(NetKbpsBox.Text?.Trim(), out var kbps))
        {
            NetStatusText.Text = "Kbps must be a number.";
            return;
        }

        var result = _net.Apply(new NetworkThrottleRule
        {
            ProcessName = NetProcessBox.Text?.Trim() ?? "chrome.exe",
            Kbps = kbps
        });
        NetStatusText.Text = result.Message;
    }

    private void NetRemove_Click(object sender, RoutedEventArgs e)
    {
        var result = _net.Apply(new NetworkThrottleRule
        {
            ProcessName = NetProcessBox.Text?.Trim() ?? "chrome.exe",
            Kbps = 0
        });
        NetStatusText.Text = result.Message;
    }

    private void RefreshStartupList()
    {
        StartupList.ItemsSource = _startup.ListUserStartup().ToList();
    }

    private void StartupRefresh_Click(object sender, RoutedEventArgs e) => RefreshStartupList();

    private void StartupDisable_Click(object sender, RoutedEventArgs e)
    {
        if (StartupList.SelectedItem is not StartupEntry entry)
        {
            StartupStatusText.Text = "Select an entry.";
            return;
        }

        var dry = StartupDryRunCheck.IsChecked == true;
        var (ok, msg) = _startup.Disable(entry.Name, dry);
        StartupStatusText.Text = msg;
        if (ok && !dry) RefreshStartupList();
    }

    private void StartupEnable_Click(object sender, RoutedEventArgs e)
    {
        if (StartupList.SelectedItem is not StartupEntry entry)
        {
            StartupStatusText.Text = "Select an entry.";
            return;
        }

        var dry = StartupDryRunCheck.IsChecked == true;
        var (ok, msg) = _startup.Enable(entry.Name, dry);
        StartupStatusText.Text = msg;
        if (ok && !dry) RefreshStartupList();
    }

    private async void ScanClean_Click(object sender, RoutedEventArgs e)
        => await RunCleanJobAsync(dryRunForce: true);

    private async void RunClean_Click(object sender, RoutedEventArgs e)
    {
        var dryRun = DryRunCheck.IsChecked == true;
        if (!dryRun)
        {
            var confirm = MessageBox.Show(
                "Delete files in selected categories?\n(Locked files will be skipped.)",
                "OptiForge Clean",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
                return;
        }

        await RunCleanJobAsync(dryRunForce: dryRun);
    }

    private async Task RunCleanJobAsync(bool dryRunForce)
    {
        if (_cleanBusy) return;
        _cleanBusy = true;
        ScanBtn.IsEnabled = false;
        CleanBtn.IsEnabled = false;
        CleanSummaryText.Text = dryRunForce ? "Scanning?" : "Cleaning?";

        var ids = _cleanRows.Where(r => r.Selected).Select(r => r.Id).ToList();
        try
        {
            var report = await Task.Run(() => _cleaner.Clean(ids, dryRunForce));
            ApplyCleanReport(report);
            CleanSummaryText.Text = report.Summary;
            StatusText.Text = dryRunForce ? "Scan done" : "Clean done";
        }
        catch (Exception ex)
        {
            CleanSummaryText.Text = "Error: " + ex.Message;
        }
        finally
        {
            _cleanBusy = false;
            ScanBtn.IsEnabled = true;
            CleanBtn.IsEnabled = true;
        }
    }

    private void ApplyCleanReport(CleanReport report)
    {
        foreach (var cat in report.Categories)
        {
            var row = _cleanRows.FirstOrDefault(r => r.Id == cat.Id);
            if (row is null) continue;
            row.BytesFound = cat.BytesFound;
            row.FileCount = cat.FileCount;
            row.Note = string.IsNullOrWhiteSpace(cat.Note)
                ? (report.DryRun
                    ? $"~{FormatBytes(cat.BytesFound)} recoverable"
                    : $"deleted {FormatBytes(cat.BytesDeleted)}, fail {cat.FilesFailed}")
                : cat.Note;
            row.NotifySize();
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.0} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):0.0} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):0.00} GB";
    }

    private void PlaybookList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (PlaybookList.SelectedItem is not Playbook pb)
        {
            PlaybookTitleText.Text = "";
            PlaybookStepsText.Text = "";
            return;
        }

        PlaybookTitleText.Text = pb.Title;
        var i = 1;
        PlaybookStepsText.Text = string.Join("\n\n", pb.Steps.Select(s => $"{i++}. {s}"));
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings.DryRunCleanByDefault = DryRunCheck.IsChecked == true;
        _settings.Save(_paths);
        StatusText.Text = "Settings saved";
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        UpdateText.Text = "Checking?";
        var (_, message) = await _updates.CheckAsync();
        UpdateText.Text = message;
    }

    private async void ApplyUpdate_Click(object sender, RoutedEventArgs e)
    {
        UpdateText.Text = "Downloading update if available?";
        try
        {
            var progress = new Progress<string>(s => UpdateText.Text = s);
            var result = await _autoUpdater.CheckAndDownloadAsync(applyAndRestart: true, progress);
            UpdateText.Text = result.Message;
            if (result.Applied)
            {
                Application.Current.Shutdown();
            }
        }
        catch (Exception ex)
        {
            UpdateText.Text = ex.Message;
        }
    }
}

public sealed class CleanRow : INotifyPropertyChanged
{
    private bool _selected;
    private long _bytesFound;
    private int _fileCount;
    private string? _note;

    public required string Id { get; init; }
    public required string DisplayName { get; init; }

    public bool Selected
    {
        get => _selected;
        set { _selected = value; OnPropertyChanged(); }
    }

    public long BytesFound
    {
        get => _bytesFound;
        set { _bytesFound = value; OnPropertyChanged(); OnPropertyChanged(nameof(SizeText)); }
    }

    public int FileCount
    {
        get => _fileCount;
        set { _fileCount = value; OnPropertyChanged(); }
    }

    public string? Note
    {
        get => _note;
        set { _note = value; OnPropertyChanged(); }
    }

    public string SizeText =>
        BytesFound <= 0 ? "?" :
        BytesFound < 1024 * 1024 ? $"{BytesFound / 1024.0:0.0} KB" :
        BytesFound < 1024L * 1024 * 1024 ? $"{BytesFound / (1024.0 * 1024):0.0} MB" :
        $"{BytesFound / (1024.0 * 1024 * 1024):0.00} GB";

    public void NotifySize() => OnPropertyChanged(nameof(SizeText));

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
