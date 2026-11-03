using System;
using System.IO;
using System.Text;
using System.Windows;
namespace LiveSubtitles;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Directory.CreateDirectory(Paths.Data);
        DispatcherUnhandledException += (_, a) => { Log.Write(a.Exception.ToString()); MessageBox.Show("操作未能完成：" + a.Exception.Message, "实时翻译字幕"); a.Handled = true; };
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
public static class Paths
{
    public static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".."));
    public static string Data => Path.Combine(Root, "data");
}
public static class Log
{
    private static readonly object Gate = new();
    public static void Write(string text)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Paths.Data);
                var p = Path.Combine(Paths.Data, "diagnostics.log");
                if (File.Exists(p) && new FileInfo(p).Length > 2_000_000)
                    File.Move(p, p + ".previous", true);
                File.AppendAllText(p, $"{DateTimeOffset.Now:O} {text}\n", Encoding.UTF8);
            }
            catch { }
        }
    }
}
