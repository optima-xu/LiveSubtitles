using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace LiveSubtitles;

public sealed class AsrClient : IAsyncDisposable
{
    private sealed record Packet(byte[] Pcm, double Start);
    private readonly Channel<Packet> incoming = Channel.CreateBounded<Packet>(new BoundedChannelOptions(30) { FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource cancel = new();
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(25) };
    private readonly object gate = new();
    private readonly AsrWorkQueue queue = new();
    private readonly SemaphoreSlim wake = new(0, 1);
    private readonly AsrSegmenter segmenter = new();
    private SileroVad? vad;
    private Settings settings = new();
    private string session = "";
    private Task? segmentTask, inferenceTask;
    private int resetPending, overflowPending, generation;
    private volatile bool stopping;
    public event Action<AsrEvent>? Received;
    public event Action<string>? Error;

    public async Task ConnectAsync(Settings settings, string session, CancellationToken token)
    {
        this.settings = settings;
        this.session = session;
        string model = Path.Combine(settings.RuntimeDirectory, "vad", "silero_vad.onnx");
        if (!File.Exists(model))
            throw new FileNotFoundException("尚未安装语音分段模型，请运行 Install.cmd。", model);
        vad = await Task.Run(() => new SileroVad(model), token);
        segmenter.Window += Enqueue;
        segmentTask = Task.Run(SegmentLoop);
        inferenceTask = Task.Run(InferenceLoop);
        Received?.Invoke(new("ready", session, 0, 0, "", false, 0, 0, ""));
    }

    public void Push(byte[] pcm, double start)
    {
        if (stopping)
            return;
        if (!incoming.Writer.TryWrite(new(pcm, start)))
        {
            while (incoming.Reader.TryRead(out _)) { }
            Interlocked.Exchange(ref overflowPending, 1);
            MarkGap();
            incoming.Writer.TryWrite(new(pcm, start));
        }
    }
    public void MarkGap() => Interlocked.Exchange(ref resetPending, 1);

    private void Reset()
    {
        segmenter.Reset();
        vad!.Reset();
        lock (gate)
        {
            generation++;
            queue.Clear();
        }
    }
    private async Task SegmentLoop()
    {
        try
        {
            float[] frame = new float[AsrSegmenter.FrameSamples];
            int filled = 0;
            double frameStart = 0, lastEnd = -1;
            await foreach (var packet in incoming.Reader.ReadAllAsync(cancel.Token))
            {
                bool discontinuity = lastEnd >= 0 && Math.Abs(packet.Start - lastEnd) > 0.15;
                if (Interlocked.Exchange(ref resetPending, 0) != 0 || discontinuity)
                {
                    Reset();
                    filled = 0;
                    frame = new float[AsrSegmenter.FrameSamples];
                }
                if (Interlocked.Exchange(ref overflowPending, 0) != 0 || discontinuity)
                    Received?.Invoke(new("gap", session, 0, 0, "", false, Math.Max(0, lastEnd), packet.Start, "音频处理积压或中断，已跳过缺失区间"));
                for (int i = 0; i + 1 < packet.Pcm.Length; i += 2)
                {
                    if (filled == 0)
                        frameStart = packet.Start + i / 32000.0;
                    frame[filled++] = BitConverter.ToInt16(packet.Pcm, i) / 32768.0f;
                    if (filled == frame.Length)
                    {
                        segmenter.Push(frame, frameStart, vad!.IsSpeech(frame));
                        frame = new float[AsrSegmenter.FrameSamples];
                        filled = 0;
                    }
                }
                lastEnd = packet.Start + packet.Pcm.Length / 32000.0;
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (Exception ex) { if (!stopping) Error?.Invoke("音频分段失败：" + ex.Message); }
    }

    private void Enqueue(AsrWindow window)
    {
        lock (gate)
        {
            if (!queue.Add(window))
            {
                generation++;
                queue.Clear();
                queue.Add(window);
                Received?.Invoke(new("gap", session, 0, 0, "", false, window.Start, window.End, "识别速度落后于音频，已跳过积压字幕"));
            }
            if (wake.CurrentCount == 0)
                wake.Release();
        }
    }

    private async Task InferenceLoop()
    {
        long previousSegment = 0, lastSegment = 0;
        string previousFinal = "", lastText = "";
        int lastGeneration = -1;
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                await wake.WaitAsync(cancel.Token);
                while (true)
                {
                    AsrWindow? window;
                    int epoch;
                    lock (gate) { window = queue.Take(); epoch = generation; }
                    if (window == null)
                        break;
                    if (epoch != lastGeneration)
                    {
                        previousFinal = lastText = "";
                        previousSegment = lastSegment = 0;
                        lastGeneration = epoch;
                    }
                    string text = await RecognizeAsync(http, settings, window.Audio, cancel.Token);
                    if (stopping)
                        return;
                    lock (gate)
                    {
                        if (epoch != generation)
                            continue;
                        if (window.OverlapSegment == previousSegment && previousSegment > 0)
                            text = AsrProtocol.RemoveOverlap(previousFinal, text);
                        if (text.Length > 0 && (window.Final || lastSegment != window.Segment || text != lastText))
                        {
                            Received?.Invoke(new("asr", session, window.Segment, window.Revision, text, window.Final, window.Start, window.End, ""));
                            lastText = text;
                            lastSegment = window.Segment;
                        }
                        else if (window.Final && lastSegment == window.Segment && lastText.Length > 0)
                        {
                            text = lastText;
                            Received?.Invoke(new("asr", session, window.Segment, window.Revision, text, true, window.Start, window.End, ""));
                        }
                        if (window.Final)
                        {
                            previousFinal = text;
                            previousSegment = window.Segment;
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (Exception ex) { if (!stopping) Error?.Invoke("Ollama 识别失败：" + ex.Message); }
    }

    public static async Task<string> RecognizeAsync(HttpClient client, Settings settings, float[] audio, CancellationToken token, int maxTokens = 256)
    {
        using var content = new StringContent(JsonSerializer.Serialize(AsrProtocol.Request(settings, audio, maxTokens)), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(settings.OllamaUrl.TrimEnd('/') + "/api/chat", content, token);
        string body = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
            throw new IOException($"{(int)response.StatusCode}: {body[..Math.Min(body.Length, 500)]}");
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out var error))
            throw new IOException(error.GetString());
        if (!root.TryGetProperty("done", out var done) || !done.GetBoolean())
            throw new IOException("识别响应未完整结束");
        if (root.TryGetProperty("done_reason", out var reason) && reason.GetString() == "length" && maxTokens > 16)
            throw new IOException("识别输出超过长度限制，请缩短音频窗口或检查识别模型");
        return AsrProtocol.Text(root.GetProperty("message").GetProperty("content").GetString() ?? "");
    }

    public async ValueTask DisposeAsync()
    {
        stopping = true;
        incoming.Writer.TryComplete();
        cancel.Cancel();
        if (segmentTask != null)
            await segmentTask;
        if (inferenceTask != null)
            await inferenceTask;
        vad?.Dispose();
        http.Dispose();
        wake.Dispose();
        cancel.Dispose();
    }
}
