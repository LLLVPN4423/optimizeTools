using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private bool _cleanBusy;

    public MainWindow()
    {
        InitializeComponent();

        _paths = new PortablePaths();
        _settings = AppSettings.Load(_paths);
        _ai = new LocalAiService(_paths);

        RootPathText.Text = "Root: " + _paths.Root;
        SettingsPathText.Text = "Settings: " + _paths.SettingsPath;
        DryRunCheck.IsChecked = _settings.DryRunCleanByDefault;

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

        RefreshAiStatus();
        RefreshMonitor();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.MonitorIntervalMs, 500, 5000))
        };
        _timer.Tick += (_, _) => RefreshMonitor();
        _timer.Start();

        Closed += (_, _) =>
        {
            _timer.Stop();
            _monitor.Dispose();
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

    private void RefreshAiStatus()
    {
        var st = _ai.GetStatus();
        AiStatusText.Text = st.Message;
    }

    private void RefreshAi_Click(object sender, RoutedEventArgs e) => RefreshAiStatus();

    private async void ScanClean_Click(object sender, RoutedEventArgs e)
        => await RunCleanJobAsync(dryRunForce: true);

    private async void RunClean_Click(object sender, RoutedEventArgs e)
    {
        var dryRun = DryRunCheck.IsChecked == true;
        if (!dryRun)
        {
            var confirm = MessageBox.Show(
                "Xóa file trong các category ?ã ch?n?\n(File ?ang b? khóa s? b? b? qua.)",
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
