using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LiveSubtitles;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

// Optional hardware smoke test. Does not open windows or play sound.
await using var services = args.Length >= 4 && args[0] == "--managed" ? new ServiceManager(Path.GetFullPath(args[1])) : null;
Settings? managedSettings = null;
if (services != null)
{
    managedSettings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(args[2])) ?? throw new ArgumentException("Invalid settings file.");
    string? lastStatus = null;
    services.Status += status => { if (status != lastStatus) Console.WriteLine("SERVICE " + status); lastStatus = status; };
    await services.EnsureAsync(managedSettings, CancellationToken.None);
    args = args[3..];
}
if (args.Length == 1 && args[0] == "--capture")
{
    using var capture = new AudioCapture();
    int packets = 0, bytes = 0, gaps = 0;
    double peak = 0, lastEnd = 0;
    bool ordered = true;
    capture.Packet += (pcm, time) =>
    {
        packets++;
        bytes += pcm.Length;
        ordered &= time >= lastEnd - 0.001;
        lastEnd = time + pcm.Length / 32000.0;
        for (int i = 0; i < pcm.Length; i += 2)
            peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(pcm, i)) / 32768.0);
    };
    capture.Gap += (_, _, _) => gaps++;
    capture.Start("");
    await Task.Delay(5000);
    await capture.StopAsync();
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        capture = true,
        packets,
        bytes,
        gaps,
        peak,
        ordered,
        lastEnd
    }));
    return packets > 20 && ordered ? 0 : 1;
}
if (args.Length < 1) { Console.WriteLine("Usage: IntegrationProbe audio.wav [repeats] [asrUrl] [ollamaUrl], or --capture"); return 2; }
using var input = new AudioFileReader(args[0]);
ISampleProvider source = input;
if (source.WaveFormat.Channels == 2) source = new StereoToMonoSampleProvider(source);
if (source.WaveFormat.Channels != 1) throw new ArgumentException("Use mono or stereo WAV.");
source = new WdlResamplingSampleProvider(source, 16000);
var all = new List<byte>();
var floats = new float[1600];
int count;
while ((count = source.Read(floats, 0, floats.Length)) > 0)
    for (int i = 0; i < count; i++)
    {
        short v = (short)Math.Clamp(floats[i] * 32767, -32768, 32767);
        all.Add((byte)v);
        all.Add((byte)(v >> 8));
    }
int repeats = args.Length > 1 ? int.Parse(args[1]) : 1;
var settings = managedSettings ?? new Settings { AsrUrl = args.Length > 2 ? args[2] : "ws://127.0.0.1:17861/asr", OllamaUrl = args.Length > 3 ? args[3] : "http://127.0.0.1:11436" };
var gate = new object();
var rows = new Dictionary<long, AsrEvent>();
var translated = new HashSet<long>();
var errors = new List<string>();
var watch = new Stopwatch();
int partials = 0, finals = 0, streamingTranslations = 0, gapsFound = 0;
double firstPartial = -1, firstTranslation = -1;
await using var translator = new TranslationService(settings);
await using var asr = new AsrClient();
string session = Guid.NewGuid().ToString("N");
asr.Error += error => { lock (gate) errors.Add(error); Console.WriteLine("ERROR " + error); };
asr.Received += e =>
{
    lock (gate)
    {
        if (e.Type == "gap")
        {
            gapsFound++;
            Console.WriteLine("GAP " + e.Message);
        }
        if (e.Type != "asr")
            return;
        rows[e.Segment] = e;
        if (e.Final)
            finals++;
        else
        {
            partials++;
            if (firstPartial < 0)
                firstPartial = watch.Elapsed.TotalSeconds;
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            kind = "asr",
            at = watch.Elapsed.TotalSeconds,
            lag = watch.Elapsed.TotalSeconds - e.End,
            e.Segment,
            e.Revision,
            e.Final,
            e.Text
        }));
        if (e.Final || e.Text.Length >= 8)
            translator.Submit(new(session, e.Segment, e.Revision, e.Text, e.Final, e.Start, e.End));
    }
};
translator.Updated += u =>
{
    lock (gate)
    {
        if (u.Error.Length > 0)
        {
            errors.Add(u.Error);
            Console.WriteLine("ERROR " + u.Error);
            return;
        }
        if (!rows.TryGetValue(u.Job.Segment, out var row) || !CaptionRules.AcceptTranslation(session, u.Job.Session, row.Revision, row.Text, -1, u.Job.Revision, u.Job.Source))
            return;
        if (firstTranslation < 0)
            firstTranslation = watch.Elapsed.TotalSeconds;
        if (!u.Done)
            streamingTranslations++;
        if (u.Done && u.Job.Final && u.Job.Revision == row.Revision)
            translated.Add(u.Job.Segment);
        if (u.Done)
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                kind = "translation",
                at = watch.Elapsed.TotalSeconds,
                lag = watch.Elapsed.TotalSeconds - u.Job.End,
                u.Job.Segment,
                u.Job.Final,
                u.Text
            }));
    }
};
await asr.ConnectAsync(settings, session, CancellationToken.None);
watch.Start();
long sampleIndex = 0;
async Task Send(byte[] packet)
{
    var end = (sampleIndex + packet.Length / 2) / 16000.0;
    var wait = end - watch.Elapsed.TotalSeconds;
    if (wait > 0)
        await Task.Delay(TimeSpan.FromSeconds(wait));
    asr.Push(packet, sampleIndex / 16000.0);
    sampleIndex += packet.Length / 2;
}
for (int repeat = 0; repeat < repeats; repeat++)
{
    for (int i = 0; i < all.Count; i += 3200)
        await Send(all.GetRange(i, Math.Min(3200, all.Count - i)).ToArray());
    for (int i = 0; i < 15; i++)
        await Send(new byte[3200]);
}
for (int i = 0; i < 150; i++) { lock (gate) { if (finals > 0 && translated.Count == rows.Count) break; } await Task.Delay(100); }
lock (gate)
{
    bool passed = partials > 0 && finals > 0 && translated.Count == rows.Count && streamingTranslations > 0 && errors.Count == 0 && gapsFound == 0;
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        summary = true,
        passed,
        sampleSeconds = all.Count / 32000.0,
        repeats,
        partials,
        finals,
        translated = translated.Count,
        streamingTranslations,
        firstPartial,
        firstTranslation,
        errors = errors.Count,
        gapsFound,
        elapsed = watch.Elapsed.TotalSeconds
    }));
    return passed ? 0 : 1;
}
