using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace LiveSubtitles;
public partial class MainWindow : Window
{
    private readonly Settings settings = Settings.Load();
    private readonly ObservableCollection<Caption> history = new();
    private readonly Dictionary<long, Caption> rows = new();
    private readonly ServiceManager services = new();
    private readonly Stopwatch timeline = new();
    private readonly Forms.NotifyIcon tray = new();
    private readonly DispatcherTimer monitor = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly OverlayWindow overlay;
    private AudioCapture? audio;
    private AsrClient? asr;
    private TranslationService? translator;
    private CancellationTokenSource? operation;
    private string session = "";
    private double sessionBase;
    private long activeSegment = -1;
    private bool running, starting, stopping, closing, loaded, reconnect;
    private IntPtr hwnd;
    private int controlEpoch;

    public MainWindow()
    {
        InitializeComponent();
        overlay = new(settings);
        HistoryGrid.ItemsSource = history;
        LoadControls();
        loaded = true;
        services.Status += s => Ui(() => SetStatus(s));
        SourceInitialized += (_, _) => RegisterKeys();
        Closing += async (_, e) => { if (closing) return; e.Cancel = true; await ExitAsync(); };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized) { Hide(); tray.Visible = true; } };
        tray.Text = "实时翻译字幕";
        tray.Icon = System.Drawing.SystemIcons.Application;
        tray.Visible = true;
        tray.DoubleClick += (_, _) => Ui(() => { Show(); WindowState = WindowState.Normal; Activate(); });
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示设置", null, (_, _) => Ui(() => { Show(); WindowState = WindowState.Normal; Activate(); }));
        menu.Items.Add("开始 / 暂停", null, (_, _) => Ui(async () => { if (running) await StopAsync("已暂停"); else await StartAsync(); }));
        menu.Items.Add("显示 / 隐藏字幕", null, (_, _) => Ui(() => ToggleOverlay(this, new RoutedEventArgs())));
        menu.Items.Add("锁定 / 解锁字幕", null, (_, _) => Ui(ToggleLock));
        menu.Items.Add("退出", null, (_, _) => Ui(async () => await ExitAsync()));
        tray.ContextMenuStrip = menu;
        monitor.Tick += async (_, _) => { if (reconnect && running && !starting && !stopping) { reconnect = false; AddGap("识别服务中断，重新连接", timeline.Elapsed.TotalSeconds, timeline.Elapsed.TotalSeconds); await StopAsync("正在重新连接"); int expected = controlEpoch; await Task.Delay(1000); if (!closing && controlEpoch == expected) await StartAsync(); } overlay.EnsureVisible(); };
        monitor.Start();
        SystemEvents.PowerModeChanged += PowerChanged;
        Loaded += (_, _) => SetStatus("就绪。先播放电脑音频，再点击开始。");
    }
    private void LoadControls()
    {
        RefreshDevices(this, new RoutedEventArgs());
        SourceBox.SelectedIndex = settings.SourceLanguage == "Chinese" ? 1 : settings.SourceLanguage == "Auto" ? 2 : 0;
        TargetBox.SelectedIndex = settings.TargetLanguage == "English" ? 2 : settings.TargetLanguage == "繁體中文" ? 1 : 0;
        FontSlider.Value = settings.FontSize;
        OpacitySlider.Value = settings.BackgroundOpacity;
        BilingualBox.IsChecked = settings.Bilingual;
        LockBox.IsChecked = settings.Locked;
        KeyHint.Text = $"{Settings.HotkeyText(settings.StartHotkey)} 开始/暂停 · {Settings.HotkeyText(settings.LockHotkey)} 锁定";
        KeyHint.ToolTip = $"{Settings.HotkeyText(settings.ShowHotkey)} 显示/隐藏字幕";
    }
    private void ReadControls()
    {
        settings.DeviceId = (DeviceBox.SelectedItem as AudioDevice)?.Id ?? "";
        settings.SourceLanguage = (SourceBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "English";
        settings.TargetLanguage = (TargetBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "简体中文";
        settings.Save();
    }
    private void Ui(Action action)
    {
        if (!Dispatcher.HasShutdownStarted)
            Dispatcher.BeginInvoke(action);
    }
    private void SetStatus(string s)
    {
        StatusText.Text = s;
        StatusText.ToolTip = s;
    }
    private void Buttons()
    {
        StartButton.IsEnabled = !running && !starting && !stopping;
        PauseButton.IsEnabled = running;
        StopButton.IsEnabled = running || starting;
        InputPanel.IsEnabled = !running && !starting;
        SourceBox.IsEnabled = TargetBox.IsEnabled = !running && !starting;
    }
    private async Task StartAsync()
    {
        if (starting || running || stopping || closing)
            return;
        controlEpoch++;
        starting = true;
        reconnect = false;
        ReadControls();
        operation = new();
        Buttons();
        try
        {
            await services.EnsureAsync(settings, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            session = Guid.NewGuid().ToString("N");
            rows.Clear();
            activeSegment = -1;
            translator = new(settings);
            translator.Updated += u => Ui(() => ApplyTranslation(u));
            asr = new();
            asr.Received += e => Ui(() => ApplyAsr(e));
            asr.Error += s => Ui(() => { SetStatus(s); if (running) reconnect = true; });
            await asr.ConnectAsync(settings, session, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            audio = new();
            audio.Packet += (pcm, t) => asr?.Push(pcm, t);
            audio.Status += s => Ui(() => SetStatus(s));
            audio.Gap += (a, b, m) => { asr?.MarkGap(); Ui(() => AddGap(m, sessionBase + a, sessionBase + b)); };
            sessionBase = timeline.Elapsed.TotalSeconds;
            timeline.Start();
            audio.Start(settings.DeviceId);
            running = true;
            if (!overlay.IsVisible)
                overlay.Show();
            overlay.SetCaption("", "等待电脑声音…", false);
            SetStatus("正在运行");
        }
        catch (OperationCanceledException) { SetStatus("已取消启动"); await Cleanup(); }
        catch (Exception e) { Log.Write(e.ToString()); SetStatus("启动失败：" + e.Message); System.Windows.MessageBox.Show(this, e.Message, "无法开始", MessageBoxButton.OK, MessageBoxImage.Information); await Cleanup(); }
        finally { starting = false; Buttons(); }
    }
    private void ApplyAsr(AsrEvent e)
    {
        if (e.Session != session || string.IsNullOrEmpty(session))
            return;
        if (e.Type == "ready")
            return;
        if (e.Type == "error")
        {
            SetStatus(e.Message);
            reconnect = true;
            return;
        }
        if (e.Type == "gap")
        {
            AddGap(e.Message, sessionBase + e.Start, sessionBase + e.End);
            return;
        }
        if (e.Type != "asr")
            return;
        if (!rows.TryGetValue(e.Segment, out var row))
        {
            row = new()
            {
                Session = session,
                Id = e.Segment,
                Start = sessionBase + e.Start
            };
            rows[e.Segment] = row;
            history.Add(row);
            TrimHistory();
        }
        if (e.Revision <= row.Revision)
            return;
        row.Revision = e.Revision;
        row.Source = e.Text;
        row.End = sessionBase + e.End;
        row.SourceFinal = e.Final;
        row.TranslationFinal = false;
        row.State = e.Final ? "翻译中" : "草稿";
        activeSegment = Math.Max(activeSegment, e.Segment);
        if (e.Segment == activeSegment)
            overlay.SetCaption(row.Source, row.Translation.Length > 0 ? row.Translation : "正在翻译…", false);
        HistoryGrid.ScrollIntoView(row);
        if (e.Final || e.Text.Length >= 8)
            translator?.Submit(new(session, e.Segment, e.Revision, e.Text, e.Final, row.Start, row.End));
        Log.Write($"metric asr segment={e.Segment} revision={e.Revision} final={e.Final} lag_ms={(timeline.Elapsed.TotalSeconds - row.End) * 1000:F0}");
    }
    private void ApplyTranslation(TranslationUpdate u)
    {
        if (!rows.TryGetValue(u.Job.Segment, out var row) || !CaptionRules.AcceptTranslation(session, u.Job.Session, row.Revision, row.Source, row.TranslationRevision, u.Job.Revision, u.Job.Source))
            return;
        if (u.Error.Length > 0)
        {
            row.State = u.Error;
            SetStatus(u.Error);
            return;
        }
        row.TranslationRevision = u.Job.Revision;
        row.Translation = u.Text;
        row.TranslationFinal = u.Done && u.Job.Final && u.Job.Revision == row.Revision;
        row.State = row.TranslationFinal ? "定稿" : "草稿";
        if (row.Id == activeSegment)
            overlay.SetCaption(row.Source, row.Translation, row.TranslationFinal);
        if (u.Done)
            Log.Write($"metric translation segment={row.Id} final={row.TranslationFinal} lag_ms={(timeline.Elapsed.TotalSeconds - row.End) * 1000:F0}");
    }
    private void TrimHistory()
    {
        while (history.Count > 5000)
        {
            var old = history[0];
            history.RemoveAt(0);
            if (rows.TryGetValue(old.Id, out var tracked) && ReferenceEquals(old, tracked))
                rows.Remove(old.Id);
        }
    }
    private void AddGap(string message, double start, double end)
    {
        history.Add(new()
        {
            Session = session,
            Id = -1,
            Start = start,
            End = end,
            Source = "[缺失区间] " + message,
            Gap = true,
            State = "缺失"
        });
        TrimHistory();
        SetStatus(message);
    }
    private async Task Cleanup()
    {
        session = "";
        timeline.Stop();
        var oldAudio = audio;
        audio = null;
        var oldAsr = asr;
        asr = null;
        var oldTranslator = translator;
        translator = null;
        running = false;
        if (oldAudio != null)
        {
            await oldAudio.StopAsync();
            oldAudio.Dispose();
        }
        if (oldAsr != null)
        {
            await oldAsr.DisposeAsync();
        }
        if (oldTranslator != null)
        {
            await oldTranslator.DisposeAsync();
        }
        running = false;
    }
    private async Task StopAsync(string status)
    {
        controlEpoch++;
        operation?.Cancel();
        if (stopping)
            return;
        stopping = true;
        reconnect = false;
        Buttons();
        await Cleanup();
        foreach (var c in rows.Values)
            if (!c.TranslationFinal)
                c.State = "未定稿";
        SetStatus(status);
        stopping = false;
        Buttons();
        settings.Save();
    }
    private async void StartClick(object s, RoutedEventArgs e) => await StartAsync();
    private async void PauseClick(object s, RoutedEventArgs e) => await StopAsync("已暂停");
    private async void StopClick(object s, RoutedEventArgs e)
    {
        await StopAsync("正在停止模型…");
        stopping = true;
        Buttons();
        try
        {
            await services.StopOwnedAsync();
            SetStatus("已停止");
        }
        finally { stopping = false; Buttons(); }
    }
    private async void ClearClick(object s, RoutedEventArgs e)
    {
        bool resume = running;
        await StopAsync("已清空");
        history.Clear();
        rows.Clear();
        timeline.Reset();
        overlay.SetCaption("", "", true);
        if (resume)
            await StartAsync();
    }
    private void ToggleOverlay(object s, RoutedEventArgs e)
    {
        if (overlay.IsVisible)
            overlay.Hide();
        else
        {
            overlay.EnsureVisible();
            overlay.Show();
        }
    }
    private void ToggleLock()
    {
        settings.Locked = !settings.Locked;
        LockBox.IsChecked = settings.Locked;
        overlay.ApplySettings();
        settings.Save();
    }
    private void LockChanged(object s, RoutedEventArgs e)
    {
        settings.Locked = LockBox.IsChecked == true;
        overlay.ApplySettings();
        settings.Save();
    }
    private void StyleChanged(object s, RoutedEventArgs e)
    {
        if (!loaded)
            return;
        settings.FontSize = FontSlider.Value;
        settings.BackgroundOpacity = OpacitySlider.Value;
        settings.Bilingual = BilingualBox.IsChecked == true;
        overlay.ApplySettings();
        settings.Save();
    }
    private void RefreshDevices(object s, RoutedEventArgs e)
    {
        try
        {
            var id = (DeviceBox.SelectedItem as AudioDevice)?.Id ?? settings.DeviceId;
            DeviceBox.ItemsSource = AudioCapture.Devices();
            DeviceBox.SelectedValue = id;
            if (DeviceBox.SelectedIndex < 0)
                DeviceBox.SelectedIndex = 0;
        }
        catch (Exception ex) { SetStatus("无法读取音频设备：" + ex.Message); }
    }
    private void CopyClick(object s, RoutedEventArgs e)
    {
        var selected = HistoryGrid.SelectedItems.Cast<Caption>().ToList();
        Clipboard.SetText(Exporter.Text(selected.Count > 0 ? selected : history));
    }
    private void ExportText(object s, RoutedEventArgs e) => Export(false);
    private void ExportSrt(object s, RoutedEventArgs e) => Export(true);
    private void Export(bool srt)
    {
        var dlg = new SaveFileDialog { Filter = srt ? "字幕文件|*.srt" : "文本文件|*.txt", FileName = "字幕_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + (srt ? ".srt" : ".txt") };
        if (dlg.ShowDialog(this) == true)
            File.WriteAllText(dlg.FileName, srt ? Exporter.Srt(history) : Exporter.Text(history), new UTF8Encoding(true));
    }
    private void AdvancedClick(object s, RoutedEventArgs e)
    {
        if (running || starting)
        {
            SetStatus("请先停止，再修改模型和连接设置。");
            return;
        }
        new SettingsWindow(settings) { Owner = this }.ShowDialog();
    }
    private void HelpClick(object s, RoutedEventArgs e) => System.Windows.MessageBox.Show(this, "1. 首次使用先运行项目的 Install.ps1。\n2. 播放电脑声音，选择输出设备，点击开始。\n3. Ctrl+Alt+Space 开始/暂停，Ctrl+Alt+L 锁定/解锁，Ctrl+Alt+H 显示/隐藏字幕。\n4. 字幕草稿会随上下文修正；历史可以复制或导出。\n\n模型与运行目录可在高级设置中调整。缺失、超时和未定稿内容会在记录中标明。", "使用说明");
    private void RegisterKeys()
    {
        hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        var keys = new[] { settings.StartHotkey, settings.LockHotkey, settings.ShowHotkey };
        for (int i = 0; i < keys.Length; i++)
            if (!RegisterHotKey(hwnd, 910 + i, 0x4003, (uint)keys[i]))
                SetStatus("部分快捷键被占用，可从高级设置修改后重启。");
    }
    private IntPtr WndProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == 0x0312)
        {
            handled = true;
            switch (w.ToInt32())
            {
                case 910:
                    Ui(async () => { if (running) await StopAsync("已暂停"); else await StartAsync(); });
                    break;
                case 911:
                    ToggleLock();
                    break;
                case 912:
                    ToggleOverlay(this, new RoutedEventArgs());
                    break;
            }
        }
        return IntPtr.Zero;
    }
    private void PowerChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
            Ui(async () => await StopAsync("休眠已暂停；恢复后可点击开始"));
    }
    private async void ExitClick(object s, RoutedEventArgs e) => await ExitAsync();
    private async Task ExitAsync()
    {
        if (closing)
            return;
        closing = true;
        monitor.Stop();
        await StopAsync("正在退出");
        for (int i = 0; i < 3; i++)
            UnregisterHotKey(hwnd, 910 + i);
        SystemEvents.PowerModeChanged -= PowerChanged;
        settings.Save();
        overlay.Exit();
        tray.Dispose();
        await services.DisposeAsync();
        Application.Current.Shutdown();
    }
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);
}
