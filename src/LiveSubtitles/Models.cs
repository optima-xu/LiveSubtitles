using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace LiveSubtitles;
public sealed class Settings
{
    public string DeviceId { get; set; } = "";
    public string SourceLanguage { get; set; } = "English";
    public string TargetLanguage { get; set; } = "简体中文";
    public string AsrUrl { get; set; } = "ws://127.0.0.1:17861/asr";
    public string OllamaUrl { get; set; } = "http://127.0.0.1:11436";
    public string TranslationModel { get; set; } = "qwen3.5:4b-q4_K_M";
    public string AsrModel { get; set; } = "Qwen/Qwen3-ASR-0.6B";
    public string WslDistribution { get; set; } = "Ubuntu-24.04";
    public string WslRuntimeDirectory { get; set; } = "/opt/live-subtitles";
    public string RuntimeDirectory { get; set; } = "";
    public string Glossary { get; set; } = "";
    public double FontSize { get; set; } = 26;
    public double BackgroundOpacity { get; set; } = 0.78;
    public bool Bilingual { get; set; } = true;
    public bool Locked
    {
        get; set;
    }
    public double OverlayLeft { get; set; } = -1;
    public double OverlayTop { get; set; } = -1;
    public double OverlayWidth { get; set; } = 920;
    public int StartHotkey { get; set; } = 0x20;
    public int LockHotkey { get; set; } = 0x4C;
    public int ShowHotkey { get; set; } = 0x48;
    public static string HotkeyText(int key) => "Ctrl+Alt+" + (key == 0x20 ? "Space" : ((char)key).ToString());
    public static Settings Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(Paths.Data, "settings.json")));
            if (s != null && !string.IsNullOrWhiteSpace(s.RuntimeDirectory))
                return s;
        }
        catch { }
        var config = new Settings();
        var runtimeFile = Path.Combine(Paths.Root, "runtime-path.txt");
        config.RuntimeDirectory = File.Exists(runtimeFile) ? File.ReadAllText(runtimeFile).Trim() : Path.Combine(Paths.Root, ".runtime");
        return config;
    }
    public void Save()
    {
        Directory.CreateDirectory(Paths.Data);
        File.WriteAllText(Path.Combine(Paths.Data, "settings.json"), JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
    }
}
public sealed class Caption : INotifyPropertyChanged
{
    public string Session { get; init; } = "";
    public long Id
    {
        get; init;
    }
    public double Start
    {
        get; set;
    }
    public double End
    {
        get; set;
    }
    public int Revision
    {
        get; set;
    }
    public bool SourceFinal
    {
        get; set;
    }
    public int TranslationRevision { get; set; } = -1;
    public bool TranslationFinal
    {
        get; set;
    }
    public bool Gap
    {
        get; set;
    }
    private string source = "", translation = "", state = "识别中";
    public string Source
    {
        get => source; set
        {
            source = value;
            Changed();
        }
    }
    public string Translation
    {
        get => translation; set
        {
            translation = value;
            Changed();
        }
    }
    public string State
    {
        get => state; set
        {
            state = value;
            Changed();
        }
    }
    public string Time => TimeSpan.FromSeconds(Math.Max(0, Start)).ToString(@"hh\:mm\:ss");
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
public sealed record AsrEvent(string Type, string Session, long Segment, int Revision, string Text, bool Final, double Start, double End, string Message);
public sealed record AudioDevice(string Id, string Name)
{
    public override string ToString() => Name;
}
public static class Exporter
{
    public static string Text(IEnumerable<Caption> captions)
    {
        var sb = new StringBuilder();
        foreach (var c in captions)
            sb.AppendLine($"[{c.Time}] [{c.State}] {c.Source}").AppendLine(c.Translation).AppendLine();
        return sb.ToString();
    }
    public static string Srt(IEnumerable<Caption> captions)
    {
        var sb = new StringBuilder();
        int i = 0;
        foreach (var c in captions)
        {
            if (string.IsNullOrWhiteSpace(c.Source))
                continue;
            sb.AppendLine((++i).ToString()).AppendLine($"{Stamp(c.Start)} --> {Stamp(Math.Max(c.Start + 0.3, c.End))}");
            if (c.Gap)
                sb.AppendLine("[音频缺失] " + c.Source);
            else
            {
                if (!string.IsNullOrWhiteSpace(c.Translation))
                    sb.AppendLine((c.TranslationFinal ? "" : "[草稿] ") + c.Translation);
                sb.AppendLine((c.SourceFinal ? "" : "[未定稿] ") + c.Source);
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
    private static string Stamp(double seconds) => TimeSpan.FromMilliseconds(Math.Round(Math.Max(0, seconds) * 1000)).ToString(@"hh\:mm\:ss\,fff");
}
