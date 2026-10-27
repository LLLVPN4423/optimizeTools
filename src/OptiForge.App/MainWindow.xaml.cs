using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    private readonly HostCleanupService _hostCleanup;
    private readonly ProjectAgent _agent;
    private readonly ConversationStore _conversations;
    private readonly ResourceGovernor _governor = new();
    private readonly DispatcherTimer _timer;
    private readonly ObservableCollection<CleanRow> _cleanRows = [];
    private readonly ObservableCollection<Playbook> _playbooks = [];
    private readonly ObservableCollection<ConversationIndexEntry> _convItems = [];
    private bool _cleanBusy;
    private bool _exitCleanupDone;
    private readonly List<(string role, string content)> _aiHistory = [];
    private AiAssistMode _aiMode = AiAssistModes.Default;
    private AgentPlan? _lastAgentPlan;
    private Conversation? _activeConversation;

    public MainWindow()
    {
        InitializeComponent();

        _paths = new PortablePaths();
        _settings = AppSettings.Load(_paths);
        _ai = new LocalAiService(_paths, _settings);
        _autoUpdater = new AutoUpdater(_paths);
        _aiSetup = new AiBootstrapper(_paths);
        _hostCleanup = new HostCleanupService(_paths);
        _agent = new ProjectAgent(_paths);
        _conversations = new ConversationStore(_paths);
        if (Enum.TryParse<ResourceProfile>(_settings.ResourceProfile, true, out var rp))
            _governor.Profile = rp;

        RootPathText.Text = "Root: " + _paths.Root;
        SettingsPathText.Text = "Settings: " + _paths.SettingsPath;
        DryRunCheck.IsChecked = _settings.DryRunCleanByDefault;
        AutoCleanupCheck.IsChecked = _settings.AutoCleanupHostOnExit;
        RestoreStartupsCheck.IsChecked = _settings.RestoreStartupsOnExit;
        LmStudioUrlBox.Text = _settings.LmStudioUrl;
        SelectBackendCombo(_settings.AiBackend);
        SelectResourceProfileCombo(_settings.ResourceProfile);
        ConvList.ItemsSource = _convItems;
        RefreshConversationList();
        EnsureActiveConversation();
        NavigateTo("AI");
        UpdateBackendBadge();
        AdminStatusText.Text = RamTrimmer.IsRunningAsAdministrator()
            ? "Running as Administrator."
            : "Not admin - standby/QoS limited; working-set trim still works.";

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
        InitAiUi();
        RefreshQuotaUi();

        _ = RefreshAiStatusAsync();
        RefreshMonitor();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Clamp(_settings.MonitorIntervalMs, 500, 5000))
        };
        _timer.Tick += (_, _) => RefreshMonitor();
        _timer.Start();

        Closing += OnWindowClosing;
        Closed += async (_, _) =>
        {
            _timer.Stop();
            _monitor.Dispose();
            await _ai.DisposeAsync();
        };
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_exitCleanupDone) return;
        _exitCleanupDone = true;
        _timer.Stop();

        try { PersistActiveConversation(); } catch { /* ignore */ }

        try
        {
            // Always stop AI we own; then optional host-side cleanup.
            _ai.StopServerAsync().GetAwaiter().GetResult();
        }
        catch { /* ignore */ }

        if (_settings.AutoCleanupHostOnExit)
        {
            try
            {
                _hostCleanup.CleanupForEject(
                    removeQos: true,
                    restoreStartups: _settings.RestoreStartupsOnExit,
                    clearPortableTemp: true,
                    killLlamaFromOurRuntime: true);
            }
            catch { /* best-effort */ }
        }
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
        var freePct = s.RamTotalGb > 0 ? (1 - s.RamUsedGb / s.RamTotalGb) * 100 : 0;
        var advice = _governor.Advise(freePct);
        ResourceAdviceText.Text = advice.Summary;
    }

    private void NavigateTo(string tag)
    {
        for (var i = 0; i < MainTabs.Items.Count; i++)
        {
            if (MainTabs.Items[i] is TabItem { Tag: string t } &&
                string.Equals(t, tag, StringComparison.OrdinalIgnoreCase))
            {
                MainTabs.SelectedIndex = i;
                if (tag != "AI" && ChatTitleBox is not null && _activeConversation is null)
                    ChatTitleBox.Text = tag;
                return;
            }
        }
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag })
            NavigateTo(tag);
    }

    private void UpdateBackendBadge()
    {
        BackendBadgeText.Text = $"{_settings.AiBackend} ? {_governor.Profile}";
    }

    private bool _suppressConvSelection;

    private void RefreshConversationList(string? search = null)
    {
        var keepId = _activeConversation?.Id;
        _suppressConvSelection = true;
        try
        {
            _convItems.Clear();
            var items = string.IsNullOrWhiteSpace(search)
                ? _conversations.List()
                : _conversations.Search(search);
            foreach (var e in items)
                _convItems.Add(e);
            if (keepId is not null)
                SelectConversationInList(keepId);
        }
        finally
        {
            _suppressConvSelection = false;
        }
    }

    private void EnsureActiveConversation()
    {
        if (_activeConversation is not null) return;
        var first = _conversations.List().FirstOrDefault();
        _activeConversation = first is null
            ? _conversations.Create()
            : _conversations.Get(first.Id) ?? _conversations.Create();
        RefreshConversationList();
        SelectConversationInList(_activeConversation.Id);
        LoadConversationIntoUi(_activeConversation);
    }

    private void SelectConversationInList(string id)
    {
        var item = _convItems.FirstOrDefault(c => c.Id == id);
        if (item is not null)
            ConvList.SelectedItem = item;
    }

    private void LoadConversationIntoUi(Conversation c)
    {
        _aiHistory.Clear();
        var sb = new StringBuilder();
        foreach (var m in c.Messages)
        {
            var who = m.Role == "user" ? "You" : m.Role == "assistant" ? "AI" : "System";
            sb.AppendLine($"[{who}] {m.Content}");
            sb.AppendLine();
            if (m.Role is "user" or "assistant")
                _aiHistory.Add((m.Role, m.Content));
        }
        AiChatBox.Text = sb.Length == 0
            ? "New conversation. Pick a mode, Start server (or LM Studio), then send.\n" + AiAssistModes.CapabilityBlurb
            : sb.ToString().Trim();
        ChatTitleBox.Text = c.Title;
    }

    private void PersistActiveConversation()
    {
        if (_activeConversation is null) return;
        // Don't wipe a saved chat if UI history was cleared accidentally while on another page
        if (_aiHistory.Count == 0 && _activeConversation.Messages.Count > 0 &&
            (AiChatBox.Text?.StartsWith("New conversation", StringComparison.Ordinal) != true) &&
            string.IsNullOrWhiteSpace(AiChatBox.Text))
            return;

        _activeConversation.Messages = _aiHistory.Select(h => new ChatMessage
        {
            Role = h.role,
            Content = h.content
        }).ToList();
        _activeConversation.ModelHint = _settings.AiBackend;
        _conversations.Save(_activeConversation);
        RefreshConversationList(ConvSearchBox.Text);
        SelectConversationInList(_activeConversation.Id);
        ChatTitleBox.Text = _activeConversation.Title;
    }

    private void NewChat_Click(object sender, RoutedEventArgs e)
    {
        PersistActiveConversation();
        _activeConversation = _conversations.Create();
        _aiHistory.Clear();
        AiChatBox.Text = "New conversation ready. Type below and send.";
        AiPromptBox.Clear();
        ChatTitleBox.Text = _activeConversation.Title;
        RefreshConversationList();
        SelectConversationInList(_activeConversation.Id);
        NavigateTo("AI");
        AiPromptBox.Focus();
    }

    private void ConvList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressConvSelection) return;
        if (ConvList.SelectedItem is not ConversationIndexEntry entry) return;
        if (_activeConversation?.Id == entry.Id) return;
        PersistActiveConversation();
        _activeConversation = _conversations.Get(entry.Id);
        if (_activeConversation is null) return;
        LoadConversationIntoUi(_activeConversation);
        NavigateTo("AI");
    }

    private void ConvSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        => RefreshConversationList(ConvSearchBox.Text);

    private void ConvList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => RenameChat_Click(sender, e);

    private void ConvList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2)
        {
            e.Handled = true;
            RenameChat_Click(sender, e);
        }
        else if (e.Key == Key.Delete)
        {
            e.Handled = true;
            DeleteChat_Click(sender, e);
        }
    }

    private void RenameChat_Click(object sender, RoutedEventArgs e)
    {
        if (_activeConversation is null) return;
        var current = _activeConversation.Title;
        var input = PromptDialog.Ask("Rename chat", "New title:", current);
        if (input is null) return;
        if (!_conversations.Rename(_activeConversation.Id, input))
        {
            MessageBox.Show("Title cannot be empty.", "OptiForge", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _activeConversation = _conversations.Get(_activeConversation.Id);
        ChatTitleBox.Text = _activeConversation?.Title ?? input;
        RefreshConversationList(ConvSearchBox.Text);
        StatusText.Text = "Renamed";
    }

    private void ChatTitleBox_LostFocus(object sender, RoutedEventArgs e)
        => CommitTitleFromBox();

    private void ChatTitleBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            CommitTitleFromBox();
            AiPromptBox.Focus();
        }
    }

    private void CommitTitleFromBox()
    {
        if (_activeConversation is null) return;
        var t = ChatTitleBox.Text?.Trim() ?? "";
        if (t.Length == 0 || t == _activeConversation.Title) return;
        if (_conversations.Rename(_activeConversation.Id, t))
        {
            _activeConversation = _conversations.Get(_activeConversation.Id);
            RefreshConversationList(ConvSearchBox.Text);
            StatusText.Text = "Title saved";
        }
    }

    private void PinChat_Click(object sender, RoutedEventArgs e)
    {
        if (_activeConversation is null) return;
        var next = !_activeConversation.Pinned;
        _conversations.SetPinned(_activeConversation.Id, next);
        _activeConversation = _conversations.Get(_activeConversation.Id);
        RefreshConversationList(ConvSearchBox.Text);
        StatusText.Text = next ? "Pinned" : "Unpinned";
    }

    private void DeleteChat_Click(object sender, RoutedEventArgs e)
    {
        if (_activeConversation is null) return;
        var title = _activeConversation.Title;
        var id = _activeConversation.Id;
        if (MessageBox.Show(
                $"Delete chat \"{title}\"?\nThis cannot be undone.",
                "OptiForge",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _conversations.Delete(id);
        _activeConversation = null;
        _aiHistory.Clear();
        EnsureActiveConversation();
        NavigateTo("AI");
        StatusText.Text = "Chat deleted";
    }

    private void SelectResourceProfileCombo(string profile)
    {
        foreach (ComboBoxItem item in ResourceProfileCombo.Items)
        {
            if (string.Equals(item.Content?.ToString(), profile, StringComparison.OrdinalIgnoreCase))
            {
                ResourceProfileCombo.SelectedItem = item;
                return;
            }
        }
    }

    private void InitAiUi()
    {
        AiCapabilityText.Text = AiAssistModes.CapabilityBlurb;
        AiModeCombo.ItemsSource = AiAssistModes.All;
        AiModeCombo.SelectedItem = AiAssistModes.Default;
        ApplyAiMode(AiAssistModes.Default);

        AiQuickPanel.Children.Clear();
        foreach (var mode in AiAssistModes.All)
        {
            var btn = new Button
            {
                Content = mode.Title,
                Tag = mode.Id,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(10, 4, 10, 4),
                ToolTip = mode.Description
            };
            btn.Click += AiQuickMode_Click;
            AiQuickPanel.Children.Add(btn);
        }

        if (string.IsNullOrWhiteSpace(AiChatBox.Text))
        {
            AiChatBox.Text =
                "Chon che do ben tren -> Start server -> go noi dung -> Gui (Ctrl+Enter).\n" +
                "Bam chip che do de nap prompt mau.";
        }
    }

    private void ApplyAiMode(AiAssistMode mode)
    {
        _aiMode = mode;
        AiModeHintText.Text = mode.Description;
        AiPromptBox.ToolTip = mode.Placeholder;
        if (string.IsNullOrWhiteSpace(AiPromptBox.Text) && !string.IsNullOrWhiteSpace(mode.ExamplePrompt))
            AiPromptBox.Text = mode.ExamplePrompt;
    }

    private void AiModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AiModeCombo.SelectedItem is AiAssistMode mode)
            ApplyAiMode(mode);
    }

    private void AiQuickMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var mode = AiAssistModes.Find(id);
        if (mode is null) return;
        AiModeCombo.SelectedItem = mode;
        if (!string.IsNullOrWhiteSpace(mode.ExamplePrompt))
            AiPromptBox.Text = mode.ExamplePrompt;
        AiPromptBox.Focus();
        AiPromptBox.CaretIndex = AiPromptBox.Text.Length;
    }

    private async Task RefreshAiStatusAsync()
    {
        var st = await _ai.GetStatusAsync();
        AiStatusText.Text = st.Message;
        var ready = st.ServerReachable;
        AiReadyBadgeText.Text = ready ? "Ready" : (st.RuntimePresent && st.ModelPresent ? "Not started" : "Need setup");
        AiReadyBadge.Background = ready
            ? new SolidColorBrush(Color.FromRgb(0x1E, 0x3A, 0x2F))
            : new SolidColorBrush(Color.FromRgb(0x2A, 0x32, 0x3E));
        if (!string.IsNullOrWhiteSpace(st.ActiveModelPath))
            AiMetaText.Text = "Model: " + Path.GetFileName(st.ActiveModelPath) + "  |  Mode: " + _aiMode.Title;
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
        AiStatusText.Text = "Starting llama-server...";
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

    private void AiPromptBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            e.Handled = true;
            SendAi_Click(sender, e);
        }
    }

    private async void SendAi_Click(object sender, RoutedEventArgs e)
    {
        var prompt = AiPromptBox.Text?.Trim() ?? "";
        if (prompt.Length == 0) return;

        if (AiModeCombo.SelectedItem is AiAssistMode selected)
            _aiMode = selected;

        SendAiBtn.IsEnabled = false;
        AppendAiChat("You", prompt);
        AiPromptBox.Clear();
        AiMetaText.Text = "Streaming...";
        BeginAiStreamSlot();
        AiChatBox.ScrollToEnd();

        try
        {
            var useCache = UseCacheCheck.IsChecked == true;
            var multi = MultiTurnCheck.IsChecked == true;
            IReadOnlyList<(string role, string content)>? history = multi && _aiHistory.Count > 1
                ? _aiHistory.Take(_aiHistory.Count - 1).ToList()
                : null;

            var (fromCache, response) = await _ai.ChatStreamingAsync(
                prompt,
                delta => Dispatcher.Invoke(() => AppendAiStreamDelta(delta)),
                useCache,
                _aiMode,
                history);

            FinalizeAiStream(response);
            PersistActiveConversation();
            AiMetaText.Text = (fromCache ? "Cache | " : "Stream | ") + _aiMode.Title +
                              " | saved ? Cloud brief available";
            AiChatBox.ScrollToEnd();
            RefreshQuotaUi();
            UpdateBackendBadge();
        }
        catch (Exception ex)
        {
            AppendAiChat("Error", ex.Message);
            AiMetaText.Text = ex.Message;
        }
        finally
        {
            SendAiBtn.IsEnabled = true;
            await RefreshAiStatusAsync();
        }
    }

    private int _aiStreamStart = -1;

    private void BeginAiStreamSlot()
    {
        if (AiChatBox.Text.StartsWith("Chon che do", StringComparison.Ordinal) ||
            AiChatBox.Text.StartsWith("Chat cleared", StringComparison.Ordinal))
            AiChatBox.Clear();

        if (!string.IsNullOrWhiteSpace(AiChatBox.Text))
            AiChatBox.AppendText(Environment.NewLine + Environment.NewLine);
        AiChatBox.AppendText("[AI] ");
        _aiStreamStart = AiChatBox.Text.Length;
    }

    private void AppendAiStreamDelta(string delta)
    {
        AiChatBox.AppendText(delta);
        AiChatBox.ScrollToEnd();
    }

    private void FinalizeAiStream(string full)
    {
        if (_aiStreamStart >= 0 && _aiStreamStart <= AiChatBox.Text.Length)
        {
            // Ensure final text matches (cache path may dump all at once)
            var prefix = AiChatBox.Text[.._aiStreamStart];
            AiChatBox.Text = prefix + full.Trim();
        }
        else
        {
            AppendAiChat("AI", full);
            return;
        }

        _aiHistory.Add(("assistant", full.Trim()));
        while (_aiHistory.Count > 8)
            _aiHistory.RemoveAt(0);
        _aiStreamStart = -1;
    }

    private void AppendAiChat(string who, string text)
    {
        if (AiChatBox.Text.StartsWith("Chon che do", StringComparison.Ordinal) ||
            AiChatBox.Text.StartsWith("Chat cleared", StringComparison.Ordinal))
            AiChatBox.Clear();

        var role = who switch
        {
            "You" => "user",
            "AI" => "assistant",
            _ => "system"
        };
        if (role is "user" or "assistant")
            _aiHistory.Add((role, text));

        while (_aiHistory.Count > 8)
            _aiHistory.RemoveAt(0);

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(AiChatBox.Text))
            sb.AppendLine(AiChatBox.Text.TrimEnd());
        sb.AppendLine();
        sb.Append('[').Append(who).Append("] ");
        sb.AppendLine(text.Trim());
        AiChatBox.Text = sb.ToString().TrimStart();
    }

    private void RefreshQuotaUi()
    {
        if (QuotaStatsText is null) return;
        var s = _ai.Quota.GetStats();
        QuotaStatsText.Text = s.Summary;
        if (QuotaTipsText is not null)
            QuotaTipsText.Text = string.Join("\n", QuotaSaver.FreeTierTips.Select((t, i) => $"{i + 1}. {t}"));
    }

    private void QuotaRefresh_Click(object sender, RoutedEventArgs e) => RefreshQuotaUi();

    private void CopyCloudBrief_Click(object sender, RoutedEventArgs e)
    {
        var lastUser = _aiHistory.LastOrDefault(x => x.role == "user").content ?? "";
        var lastAi = _aiHistory.LastOrDefault(x => x.role == "assistant").content ?? "";
        if (string.IsNullOrWhiteSpace(lastUser) && string.IsNullOrWhiteSpace(lastAi))
        {
            MessageBox.Show("Hay chat/draft o tab AI truoc, roi Copy cloud brief.", "Quota Saver",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var brief = _ai.Quota.BuildCloudPolishBrief(lastUser, _aiHistory, lastAi);
        try
        {
            Clipboard.SetText(brief);
            _ai.Quota.RecordCloudBrief();
            RefreshQuotaUi();
            AiMetaText.Text = "Cloud brief copied ? paste vao ChatGPT/Gemini/Claude.";
            StatusText.Text = "Cloud brief ready";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Quota Saver", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CopyBatchTemplate_Click(object sender, RoutedEventArgs e)
    {
        var lines = (QuotaBatchBox.Text ?? "")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var text = _ai.Quota.BuildBatchPrompt(lines);
        try
        {
            Clipboard.SetText(text);
            StatusText.Text = "Batch prompt copied";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Quota Saver", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenChatGpt_Click(object sender, RoutedEventArgs e) => QuotaSaver.OpenUrl("https://chatgpt.com/");
    private void OpenGemini_Click(object sender, RoutedEventArgs e) => QuotaSaver.OpenUrl("https://gemini.google.com/app");
    private void OpenClaude_Click(object sender, RoutedEventArgs e) => QuotaSaver.OpenUrl("https://claude.ai/new");
    private void OpenPerplexity_Click(object sender, RoutedEventArgs e) => QuotaSaver.OpenUrl("https://www.perplexity.ai/");

    private void ClearAiChat_Click(object sender, RoutedEventArgs e)
    {
        _aiHistory.Clear();
        AiChatBox.Text =
            "Chat cleared. Pick a mode -> type -> Send.\n" +
            AiAssistModes.CapabilityBlurb;
        AiMetaText.Text = "Chat cleared";
    }

    private void CopyAi_Click(object sender, RoutedEventArgs e)
    {
        var last = _aiHistory.LastOrDefault(x => x.role == "assistant").content;
        var text = !string.IsNullOrWhiteSpace(last) ? last : AiChatBox.Text;
        if (string.IsNullOrWhiteSpace(text)) return;
        try
        {
            Clipboard.SetText(text);
            AiMetaText.Text = "Copied last AI reply.";
        }
        catch (Exception ex)
        {
            AiMetaText.Text = "Copy failed: " + ex.Message;
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

    private void NetRemoveAll_Click(object sender, RoutedEventArgs e)
    {
        var (_, msg) = _net.RemoveAllOptiForgePolicies();
        NetStatusText.Text = msg;
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

    private void StartupRestoreAll_Click(object sender, RoutedEventArgs e)
    {
        var dry = StartupDryRunCheck.IsChecked == true;
        var (_, msg) = _startup.RestoreAllFromBackup(dry);
        if (!dry)
            _startup.TryRemoveEmptyOptiForgeKey();
        StartupStatusText.Text = msg;
        RefreshStartupList();
    }

    private void ScanHostImpact_Click(object sender, RoutedEventArgs e)
    {
        var report = _hostCleanup.AnalyzeCurrentImpact();
        HostImpactText.Text = FormatHostReport(report);
        StatusText.Text = "Host impact scanned";
    }

    private async void SafeEject_Click(object sender, RoutedEventArgs e)
    {
        var restore = RestoreStartupsCheck.IsChecked == true;
        var confirm = MessageBox.Show(
            "Safe eject cleanup:\n" +
            "- Stop llama from this drive\n" +
            "- Remove all OptiForge_* QoS policies\n" +
            "- Clear update temp on portable data\n" +
            (restore ? "- Restore disabled startups\n" : "- Keep startup changes (not restoring)\n") +
            "\nContinue?",
            "OptiForge Safe eject",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            await _ai.StopServerAsync();
        }
        catch { /* ignore */ }

        var report = await Task.Run(() => _hostCleanup.CleanupForEject(
            removeQos: true,
            restoreStartups: restore,
            clearPortableTemp: true,
            killLlamaFromOurRuntime: true));

        HostImpactText.Text = FormatHostReport(report);
        StatusText.Text = "Safe eject cleanup done";
        MessageBox.Show(report.Summary, "OptiForge Safe eject", MessageBoxButton.OK, MessageBoxImage.Information);
        RefreshStartupList();
        await RefreshAiStatusAsync();
    }

    private static string FormatHostReport(HostImpactReport report)
    {
        var lines = new List<string> { report.Summary };
        lines.AddRange(report.Actions);
        lines.AddRange(report.Warnings.Select(w => "! " + w));
        return string.Join("\n", lines);
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
        _settings.AutoCleanupHostOnExit = AutoCleanupCheck.IsChecked == true;
        _settings.RestoreStartupsOnExit = RestoreStartupsCheck.IsChecked == true;
        _settings.AiBackend = GetSelectedBackend();
        _settings.LmStudioUrl = LmStudioUrlBox.Text?.Trim() ?? _settings.LmStudioUrl;
        _settings.ResourceProfile = (ResourceProfileCombo.SelectedItem as ComboBoxItem)?.Content?.ToString()
                                    ?? "Balanced";
        if (Enum.TryParse<ResourceProfile>(_settings.ResourceProfile, true, out var rp))
            _governor.Profile = rp;
        _settings.Save(_paths);
        UpdateBackendBadge();
        StatusText.Text = "Settings saved";
    }

    private void SelectBackendCombo(string backend)
    {
        foreach (ComboBoxItem item in AgentBackendCombo.Items)
        {
            if (string.Equals(item.Tag?.ToString(), backend, StringComparison.OrdinalIgnoreCase))
            {
                AgentBackendCombo.SelectedItem = item;
                return;
            }
        }
    }

    private string GetSelectedBackend()
    {
        if (AgentBackendCombo.SelectedItem is ComboBoxItem { Tag: string tag })
            return tag;
        return "builtin";
    }

    private string GetSelectedTemplate()
    {
        if (AgentTemplateCombo.SelectedItem is ComboBoxItem { Tag: string tag })
            return tag;
        return "auto";
    }

    private void AgentBackendCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // no-op; Probe/Plan read selection live
    }

    private async void AgentProbe_Click(object sender, RoutedEventArgs e)
    {
        AgentBackendStatusText.Text = "Probing...";
        var backend = GetSelectedBackend();
        var url = LmStudioUrlBox.Text?.Trim() ?? _settings.LmStudioUrl;
        var (ok, msg) = await _agent.ProbeBackendAsync(backend, url, _settings.AiPort);
        AgentBackendStatusText.Text = msg;
        StatusText.Text = ok ? "Backend OK" : "Backend down";
    }

    private async void AgentPlan_Click(object sender, RoutedEventArgs e)
    {
        var prompt = AgentPromptBox.Text?.Trim() ?? "";
        if (prompt.Length == 0) return;

        AgentLogBox.Text = "Planning with " + GetSelectedBackend() + "...\n";
        AgentMetaText.Text = "Working...";
        try
        {
            _lastAgentPlan = await Task.Run(async () =>
                await _agent.PlanAsync(
                    prompt,
                    GetSelectedBackend(),
                    LmStudioUrlBox.Text?.Trim() ?? _settings.LmStudioUrl,
                    _settings.AiPort,
                    GetSelectedTemplate()));

            var sb = new StringBuilder();
            sb.AppendLine("Backend: " + _lastAgentPlan.BackendUsed);
            sb.AppendLine("Project: " + _lastAgentPlan.ProjectName);
            sb.AppendLine("Template: " + _lastAgentPlan.Template);
            sb.AppendLine("Summary: " + _lastAgentPlan.Summary);
            sb.AppendLine();
            sb.AppendLine("Files:");
            foreach (var f in _lastAgentPlan.Files)
                sb.AppendLine($"- {f.RelativePath} ({f.Content?.Length ?? 0} chars)");
            if (_lastAgentPlan.NextSteps.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Next:");
                foreach (var s in _lastAgentPlan.NextSteps)
                    sb.AppendLine("? " + s);
            }
            AgentLogBox.Text = sb.ToString();
            AgentMetaText.Text = "Plan ready ? Create project de ghi vao Projects/";
        }
        catch (Exception ex)
        {
            AgentLogBox.Text = ex.Message;
            AgentMetaText.Text = "Plan failed";
        }
    }

    private void AgentCreate_Click(object sender, RoutedEventArgs e)
    {
        if (_lastAgentPlan is null)
        {
            MessageBox.Show("Bam Plan truoc.", "Local Agent", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dry = AgentDryRunCheck.IsChecked == true;
        var result = _agent.Execute(_lastAgentPlan, dry);
        AgentMetaText.Text = result.Message;
        AgentLogBox.AppendText(Environment.NewLine + Environment.NewLine + result.Message);
        if (result.Ok && !dry && result.ProjectPath is not null)
        {
            var open = MessageBox.Show(
                result.Message + "\n\nOpen folder?",
                "Local Agent",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (open == MessageBoxResult.Yes)
                QuotaSaver.OpenUrl(result.ProjectPath);
        }
    }

    private void AgentOpenProjects_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_agent.ProjectsRoot);
        QuotaSaver.OpenUrl(_agent.ProjectsRoot);
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
