using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
namespace LiveSubtitles;
public partial class OverlayWindow : Window
{
    private readonly Settings settings;
    private bool exit;
    private string currentOriginal = "", currentTranslation = "";
    public OverlayWindow(Settings s)
    {
        settings = s;
        InitializeComponent();
        Width = Math.Clamp(s.OverlayWidth, 340, 1800);
        var area = SystemParameters.WorkArea;
        Left = s.OverlayLeft < 0 ? area.Left + (area.Width - Width) / 2 : s.OverlayLeft;
        Top = s.OverlayTop < 0 ? area.Bottom - Height - 60 : s.OverlayTop;
        EnsureVisible();
        SourceInitialized += (_, _) => ApplyStyle();
        LocationChanged += (_, _) => Remember();
        SizeChanged += (_, _) => { Remember(); RenderText(); };
        Closing += (_, e) => { if (!exit) { e.Cancel = true; Hide(); } };
        ApplySettings();
    }
    public void EnsureVisible()
    {
        var v = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!v.IntersectsWith(new Rect(Left, Top, Width, Height)))
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + 40;
            Top = area.Bottom - Height - 40;
        }
    }
    public void ApplySettings()
    {
        Translated.FontSize = Math.Clamp(settings.FontSize, 16, 42);
        Original.Visibility = settings.Bilingual ? Visibility.Visible : Visibility.Collapsed;
        SourceRow.Height = new GridLength(settings.Bilingual ? 28 : 0);
        MinHeight = 40 + (settings.Bilingual ? 28 : 0) + Translated.FontSize * 2.8;
        Height = Math.Max(Height, MinHeight);
        Surface.Background = new SolidColorBrush(Color.FromArgb((byte)(Math.Clamp(settings.BackgroundOpacity, .15, 1) * 255), 0, 0, 0));
        RenderText();
        ApplyStyle();
    }
    private void ApplyStyle()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        long style = GetWindowLongPtr(hwnd, -20).ToInt64() | 0x08000000L | 0x80L;
        style = settings.Locked ? style | 0x20L : style & ~0x20L;
        SetWindowLongPtr(hwnd, -20, new IntPtr(style));
        Surface.BorderBrush = settings.Locked ? Brushes.Transparent : Brushes.Gray;
        ResizeMode = settings.Locked ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
        Hint.Text = settings.Locked ? Settings.HotkeyText(settings.LockHotkey) + " 解锁字幕" : "拖动可移动 · " + Settings.HotkeyText(settings.LockHotkey) + " 锁定 / 解锁";
    }
    public void SetCaption(string original, string translated, bool final, string status = "")
    {
        currentOriginal = original;
        currentTranslation = translated;
        RenderText();
        Translated.Foreground = final ? Brushes.White : new SolidColorBrush(Color.FromRgb(220, 220, 220));
        Hint.Text = string.IsNullOrEmpty(status) ? (final ? "" : "正在更新…") : status;
    }
    private void RenderText()
    {
        Original.Text = FitTail(currentOriginal, 17, 1);
        Translated.Text = FitTail(currentTranslation, Translated.FontSize, 2);
    }
    private string FitTail(string text, double size, int lines)
    {
        if (text.Length == 0)
            return text;
        var positions = StringInfo.ParseCombiningCharacters(text);
        double width = Math.Max(260, ActualWidth > 0 ? ActualWidth - 40 : Width - 40);
        bool Fits(string candidate)
        {
            var measured = new FormattedText(candidate, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(Translated.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip) { MaxTextWidth = width };
            return measured.Height <= size * 1.4 * lines;
        }
        if (Fits(text))
            return text;
        int lo = 0, hi = positions.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            string suffix = "…" + text[positions[mid]..];
            if (Fits(suffix))
                hi = mid;
            else
                lo = mid + 1;
        }
        return lo < positions.Length ? "…" + text[positions[lo]..] : "…";
    }
    private void DragCaption(object sender, MouseButtonEventArgs e)
    {
        if (!settings.Locked && e.LeftButton == MouseButtonState.Pressed)
            try
            {
                DragMove();
            }
            catch (InvalidOperationException) { }
    }
    private void Remember()
    {
        settings.OverlayLeft = Left;
        settings.OverlayTop = Top;
        settings.OverlayWidth = Width;
    }
    public void Exit()
    {
        exit = true;
        Close();
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int n);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr h, int n, IntPtr v);
}
