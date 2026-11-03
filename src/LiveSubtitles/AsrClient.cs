using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
namespace LiveSubtitles;
public sealed class AsrClient : IAsyncDisposable
{
    private readonly ClientWebSocket socket = new();
    private readonly Channel<byte[]> outgoing = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(20) { FullMode = BoundedChannelFullMode.Wait, SingleReader = false });
    private readonly TaskCompletionSource<bool> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int gapPending;
    private readonly CancellationTokenSource cancel = new();
    private Task? sender, receiver;
    private ulong sequence, samples;
    private bool stopping;
    public event Action<AsrEvent>? Received;
    public event Action<string>? Error;
    public async Task ConnectAsync(Settings settings, string session, CancellationToken token)
    {
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
        await socket.ConnectAsync(new Uri(settings.AsrUrl), token);
        var msg = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "start",
            session_id = session,
            language = settings.SourceLanguage == "Auto" ? null : settings.SourceLanguage,
            chunk_seconds = 1.0
        });
        await socket.SendAsync(msg, WebSocketMessageType.Text, true, token);
        receiver = ReceiveLoop();
        sender = SendLoop();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
    }
    public void Push(byte[] pcm, double start)
    {
        if (Interlocked.Exchange(ref gapPending, 0) != 0)
            sequence++;
        var frame = new byte[24 + pcm.Length];
        BitConverter.GetBytes(sequence++).CopyTo(frame, 0);
        BitConverter.GetBytes(samples).CopyTo(frame, 8);
        BitConverter.GetBytes(start).CopyTo(frame, 16);
        samples += (ulong)(pcm.Length / 2);
        pcm.CopyTo(frame, 24);
        if (!outgoing.Writer.TryWrite(frame))
        {
            Error?.Invoke("音频发送积压：部分音频缺失，正在恢复");
            while (outgoing.Reader.TryRead(out _))
            {
            }
            outgoing.Writer.TryWrite(frame);
        }
    }
    public void MarkGap() => Interlocked.Exchange(ref gapPending, 1);
    private async Task SendLoop()
    {
        try
        {
            await foreach (var packet in outgoing.Reader.ReadAllAsync(cancel.Token))
                await socket.SendAsync(packet, WebSocketMessageType.Binary, true, cancel.Token);
            if (socket.State == WebSocketState.Open)
                await socket.SendAsync(Encoding.UTF8.GetBytes("{\"type\":\"stop\"}"), WebSocketMessageType.Text, true, cancel.Token);
        }
        catch (Exception e) when (e is OperationCanceledException || e is WebSocketException) { if (!stopping) Error?.Invoke("识别连接中断：" + e.Message); }
    }
    private async Task ReceiveLoop()
    {
        var chunk = new byte[16384];
        try
        {
            while (socket.State == WebSocketState.Open && !cancel.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult read;
                do
                {
                    read = await socket.ReceiveAsync(chunk, cancel.Token);
                    if (read.MessageType == WebSocketMessageType.Close)
                    {
                        ready.TrySetException(new IOException("识别服务已断开"));
                        if (!stopping)
                            Error?.Invoke("识别服务已断开");
                        return;
                    }
                    ms.Write(chunk, 0, read.Count);
                    if (ms.Length > 1_000_000)
                        throw new IOException("识别响应过大");
                } while (!read.EndOfMessage);
                using var doc = JsonDocument.Parse(ms.ToArray());
                var r = doc.RootElement;
                string Str(string n) => r.TryGetProperty(n, out var p) ? p.GetString() ?? "" : "";
                long Int(string n) => r.TryGetProperty(n, out var p) ? p.GetInt64() : 0;
                double Num(string n) => r.TryGetProperty(n, out var p) ? p.GetDouble() : 0;
                bool final = r.TryGetProperty("final", out var f) && f.GetBoolean();
                if (Str("type") == "ready")
                    ready.TrySetResult(true);
                if (Str("type") == "error")
                {
                    ready.TrySetException(new IOException(Str("message")));
                    Error?.Invoke(Str("message"));
                }
                Received?.Invoke(new(Str("type"), Str("session_id"), Int("segment_id"), (int)Int("revision"), Str("text"), final, Num("start"), Num("end"), Str("message")));
            }
        }
        catch (Exception e) { ready.TrySetException(e); if (!stopping) Error?.Invoke("识别连接异常：" + e.Message); }
    }
    public async ValueTask DisposeAsync()
    {
        stopping = true;
        outgoing.Writer.TryComplete();
        if (sender != null)
            await Task.WhenAny(sender, Task.Delay(400));
        cancel.Cancel();
        socket.Abort();
        if (receiver != null)
            await Task.WhenAny(receiver, Task.Delay(400));
        socket.Dispose();
        cancel.Dispose();
    }
}
