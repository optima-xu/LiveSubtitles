using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LiveSubtitles;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1) return 2;
        Directory.CreateDirectory(args[0]);
        // Render only the component tree; no desktop windows, tray icons or models are opened.
        foreach (var (name, width, font, bilingual) in new[] { ("standard", 920d, 26d, true), ("large-type", 360d, 42d, true), ("translation-only", 700d, 26d, false) })
        {
            var window = new OverlayWindow(new Settings { OverlayWidth = width, FontSize = font, Bilingual = bilingual });
            window.SetCaption("Please do not delete the file. The total is 120 dollars, not 20 dollars.", "请不要删除文件。总额是一百二十美元，不是二十美元。", true);
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(window.Width, window.Height));
            root.Arrange(new Rect(0, 0, window.Width, window.Height));
            root.UpdateLayout();
            var text = (TextBlock)window.FindName("Translated");
            if (text.ActualHeight <= 0 || text.DesiredSize.Height > text.ActualHeight + 1) throw new Exception("Subtitle text is clipped: " + name);
            var image = new RenderTargetBitmap((int)Math.Ceiling(window.Width), (int)Math.Ceiling(window.Height), 96, 96, PixelFormats.Pbgra32);
            image.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = File.Create(Path.Combine(args[0], name + ".png"));
            encoder.Save(stream);
            window.Exit();
            Console.WriteLine("PASS " + name);
        }
        return 0;
    }
}
