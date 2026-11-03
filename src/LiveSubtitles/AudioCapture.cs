using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace LiveSubtitles;
public sealed class AudioCapture : IDisposable
{
    private readonly MMDeviceEnumerator enumerator = new();
    private WasapiLoopbackCapture? capture;
    private MMDevice? device;
    private BufferedWaveProvider? buffer;
    private ISampleProvider? resampler;
    private readonly object gate = new();
    private readonly Stopwatch watch = new();
    private DateTime lastInput = DateTime.UtcNow;
    private CancellationTokenSource? cts;
    private Task? pump;
    private double lastEnd;
    private string activeId = "", requestedId = "";
    public event Action<byte[], double>? Packet;
    public event Action<string>? Status;
    public event Action<double, double, string>? Gap;
    public double Level
    {
        get; private set;
    }
    public static List<AudioDevice> Devices()
    {
        using var e = new MMDeviceEnumerator();
        var list = new List<AudioDevice> { new("", "系统默认输出（自动跟随）") };
        foreach (var d in e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (d)
                list.Add(new(d.ID, d.FriendlyName));
        }
        return list;
    }
    private string DesiredId()
    {
        if (!string.IsNullOrEmpty(requestedId))
            return requestedId;
        using var d = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        return d.ID;
    }
    public void Start(string id)
    {
        requestedId = id;
        watch.Restart();
        lastEnd = 0;
        OpenDevice();
        cts = new();
        pump = Task.Run(() => Pump(cts.Token));
    }
    private void OpenDevice()
    {
        CloseDevice();
        activeId = DesiredId();
        device = enumerator.GetDevice(activeId);
        capture = new WasapiLoopbackCapture(device);
        buffer = new BufferedWaveProvider(capture.WaveFormat) { BufferDuration = TimeSpan.FromSeconds(2), DiscardOnBufferOverflow = false, ReadFully = false };
        ISampleProvider source = buffer.ToSampleProvider();
        if (source.WaveFormat.Channels > 1)
            source = new MonoMix(source);
        resampler = new WdlResamplingSampleProvider(source, 16000);
        capture.DataAvailable += (_, e) => { lock (gate) { if (buffer == null) return; try { buffer.AddSamples(e.Buffer, 0, e.BytesRecorded); } catch { buffer.ClearBuffer(); Gap?.Invoke(lastEnd, watch.Elapsed.TotalSeconds, "采集缓冲区溢出"); } lastInput = DateTime.UtcNow; } };
        capture.RecordingStopped += (_, e) => { if (e.Exception != null) { Log.Write("Capture: " + e.Exception.Message); Status?.Invoke("音频设备中断，正在重新连接"); activeId = ""; } };
        lastInput = DateTime.UtcNow;
        capture.StartRecording();
        Status?.Invoke("正在接收：" + device.FriendlyName);
    }
    private async Task Pump(CancellationToken token)
    {
        var samples = new float[1600];
        var pcm = new List<byte>(3200);
        var nextDeviceCheck = DateTime.MinValue;
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (DateTime.UtcNow >= nextDeviceCheck)
                {
                    nextDeviceCheck = DateTime.UtcNow.AddSeconds(1);
                    if (DesiredId() != activeId || device?.State != DeviceState.Active)
                    {
                        var start = lastEnd;
                        OpenDevice();
                        pcm.Clear();
                        Gap?.Invoke(start, watch.Elapsed.TotalSeconds, "音频设备重新连接");
                    }
                }
                int count = 0;
                lock (gate)
                {
                    if (buffer != null && buffer.BufferedBytes > 0 && resampler != null)
                        count = resampler.Read(samples, 0, samples.Length);
                }
                if (count > 0)
                {
                    float peak = 0;
                    for (int i = 0; i < count; i++)
                    {
                        peak = Math.Max(peak, Math.Abs(samples[i]));
                        short v = (short)Math.Clamp(samples[i] * 32767, -32768, 32767);
                        pcm.Add((byte)v);
                        pcm.Add((byte)(v >> 8));
                    }
                    Level = peak;
                    while (pcm.Count >= 3200)
                    {
                        var bytes = pcm.GetRange(0, 3200).ToArray();
                        pcm.RemoveRange(0, 3200);
                        var start = Math.Max(lastEnd, watch.Elapsed.TotalSeconds - 0.1 - pcm.Count / 32000.0);
                        Packet?.Invoke(bytes, start);
                        lastEnd = start + 0.1;
                    }
                }
                else if ((DateTime.UtcNow - lastInput).TotalMilliseconds > 220 && watch.Elapsed.TotalSeconds - lastEnd >= 0.1)
                {
                    if (pcm.Count > 0)
                    {
                        var bytes = pcm.ToArray();
                        pcm.Clear();
                        var tailStart = Math.Max(lastEnd, watch.Elapsed.TotalSeconds - 0.1 - bytes.Length / 32000.0);
                        Packet?.Invoke(bytes, tailStart);
                        lastEnd = tailStart + bytes.Length / 32000.0;
                    }
                    var start = Math.Max(lastEnd, watch.Elapsed.TotalSeconds - 0.1);
                    Packet?.Invoke(new byte[3200], start);
                    lastEnd = start + 0.1;
                    Level = 0;
                }
            }
            catch (Exception ex) { Log.Write("Audio: " + ex.Message); Status?.Invoke("音频设备不可用，正在重试"); try { await Task.Delay(1000, token); } catch (OperationCanceledException) { } }
            try
            {
                await Task.Delay(10, token);
            }
            catch (OperationCanceledException) { break; }
        }
    }
    private void CloseDevice()
    {
        var old = capture;
        capture = null;
        try
        {
            old?.StopRecording();
        }
        catch { }
        old?.Dispose();
        device?.Dispose();
        device = null;
        lock (gate)
        {
            buffer = null;
            resampler = null;
        }
    }
    public async Task StopAsync()
    {
        cts?.Cancel();
        if (pump != null)
            await pump;
        CloseDevice();
        cts?.Dispose();
        cts = null;
        pump = null;
    }
    public void Dispose()
    {
        cts?.Cancel();
        CloseDevice();
        enumerator.Dispose();
    }
    private sealed class MonoMix(ISampleProvider input) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(input.WaveFormat.SampleRate, 1);
        private float[] tmp = Array.Empty<float>();
        public int Read(float[] dest, int offset, int count)
        {
            int channels = input.WaveFormat.Channels;
            if (tmp.Length < count * channels)
                tmp = new float[count * channels];
            int n = input.Read(tmp, 0, count * channels) / channels;
            for (int i = 0; i < n; i++)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++)
                    sum += tmp[i * channels + c];
                dest[offset + i] = sum / channels;
            }
            return n;
        }
    }
}
