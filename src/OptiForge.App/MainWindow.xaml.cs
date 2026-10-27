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

        RootPathText.Text = "Root: " + _paths.Root;
        SettingsPathText.Text = "Settings: " + _paths.SettingsPath;
        DryRunCheck.IsChecked = _settings.DryRunCleanByDefault;
        AdminStatusText.Text = RamTrimmer.IsRunningAsAdministrator()
            ? "?ang ch?y v?i quy?n Administrator."
            : "Kh?ng ph?i admin ? standby list s? b? b? qua (working-set trim v?n ch?y).";

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

    private async void StartAi_Click(object sender, RoutedEventArgs e)
    {
        StartAiBtn.IsEnabled = false;
        AiStatusText.Text = "Starting llama-server (c? th? m?t 30?90s tr?n HDD)?";
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
            AiMetaText.Text = fromCache ? "Cache hit ? kh?ng g?i model." : "Fresh generation";
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

    private async void ScanClean_Click(object sender, RoutedEventArgs e)
        => await RunCleanJobAsync(dryRunForce: true);

    private async void RunClean_Click(object sender, RoutedEventArgs e)
    {
        var dryRun = DryRunCheck.IsChecked == true;
        if (!dryRun)
        {
            var confirm = MessageBox.Show(
                "X?a file trong c?c category ?? ch?n?\n(File ?ang b? kh?a s? b? b? qua.)",
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
