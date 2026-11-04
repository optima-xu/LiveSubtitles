using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LiveSubtitles;
public sealed record TranslationJob(string Session, long Segment, int Revision, string Source, bool Final, double Start, double End);
public sealed record TranslationUpdate(TranslationJob Job, string Text, bool Done, string Error);
public sealed class TranslationService : IAsyncDisposable
{
    private readonly Settings settings;
    private readonly HttpClient http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly object gate = new();
    private readonly LinkedList<TranslationJob> pending = new();
    private readonly SemaphoreSlim wake = new(0);
    private readonly CancellationTokenSource stop = new();
    private readonly List<(long Id, string Source, string Target)> context = new();
    private readonly Task worker;
    private CancellationTokenSource? active;
    private TranslationJob? current;
    public event Action<TranslationUpdate>? Updated;
    public TranslationService(Settings settings)
    {
        this.settings = settings;
        worker = Task.Run(Loop);
    }
    public void Submit(TranslationJob job)
    {
        if (string.IsNullOrWhiteSpace(job.Source))
            return;
        lock (gate)
        {
            for (var n = pending.First; n != null;)
            {
                var next = n.Next;
                if (n.Value.Segment == job.Segment)
                    pending.Remove(n);
                n = next;
            }
            if (current != null && current.Segment == job.Segment && !job.Source.StartsWith(current.Source, StringComparison.Ordinal))
                active?.Cancel();
            if (!job.Final && pending.Count >= 4)
                return;
            if (pending.Count >= 10)
            {
                var old = pending.First!.Value;
                pending.RemoveFirst();
                Updated?.Invoke(new(old, "", true, "翻译积压，未完成此句翻译"));
            }
            pending.AddLast(job);
            if (wake.CurrentCount == 0)
                wake.Release();
        }
    }
    private async Task Loop()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await wake.WaitAsync(stop.Token);
                TranslationJob? job;
                lock (gate)
                {
                    job = pending.First?.Value;
                    if (job != null)
                        pending.RemoveFirst();
                    if (pending.Count > 0 && wake.CurrentCount == 0)
                        wake.Release();
                    current = job;
                }
                if (job == null)
                    continue;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                lock (gate)
                    active = timeout;
                try
                {
                    await Translate(job, timeout.Token);
                }
                catch (OperationCanceledException) { if (!stop.IsCancellationRequested && job.Final) Updated?.Invoke(new(job, "", true, "翻译已取消或超时")); }
                catch (Exception e) { Log.Write("Translation: " + e.Message); Updated?.Invoke(new(job, "", true, "翻译不可用：" + e.Message)); }
                finally { lock (gate) { active = null; current = null; } }
            }
            catch (OperationCanceledException) { break; }
        }
    }
    private async Task Translate(TranslationJob job, CancellationToken token)
    {
        var previous = string.Join("\n", context.Where(x => x.Id != job.Segment).TakeLast(3).Select(x => $"原文：{x.Source}\n译文：{x.Target}"));
        var body = TranslationPrompt.Request(settings, job.Source, previous);
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.OllamaUrl.TrimEnd('/') + "/api/chat") { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(token));
        var text = new StringBuilder();
        bool done = false;
        var lastSend = DateTime.MinValue;
        while (await reader.ReadLineAsync(token) is { } line)
        {
            if (line.Length == 0)
                continue;
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            if (r.TryGetProperty("error", out var error))
                throw new IOException(error.GetString());
            if (r.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content))
                text.Append(content.GetString());
            if ((DateTime.UtcNow - lastSend).TotalMilliseconds >= 80 && text.Length > 0)
            {
                Updated?.Invoke(new(job, text.ToString(), false, ""));
                lastSend = DateTime.UtcNow;
            }
            if (r.TryGetProperty("done", out var d) && d.GetBoolean())
            {
                done = true;
                var reason = r.TryGetProperty("done_reason", out var q) ? q.GetString() : "stop";
                if (reason == "length")
                {
                    Updated?.Invoke(new(job, text.ToString(), true, "译文过长，结果已截断"));
                    return;
                }
                break;
            }
        }
        if (!done)
            throw new IOException("翻译流意外结束");
        var result = text.ToString().Trim();
        if (result.Length == 0)
            throw new IOException("模型没有返回译文");
        if (job.Final)
        {
            context.RemoveAll(x => x.Id == job.Segment);
            context.Add((job.Segment, job.Source, result));
            if (context.Count > 3)
                context.RemoveAt(0);
        }
        Updated?.Invoke(new(job, result, true, ""));
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        lock (gate)
            active?.Cancel();
        await Task.WhenAny(worker, Task.Delay(500));
        http.Dispose();
    }
}
