using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LiveSubtitles;

public sealed class ServiceManager : IAsyncDisposable
{
    private readonly List<Process> owned = new();
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(5) };
    public ServiceManager(string? applicationRoot = null) { }
    public event Action<string>? Status;

    private async Task<JsonDocument?> Get(string url, CancellationToken token)
    {
        try { return JsonDocument.Parse(await http.GetStringAsync(url, token)); }
        catch (HttpRequestException) { return null; }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
    }

    public async Task EnsureAsync(Settings s, CancellationToken token)
    {
        Directory.CreateDirectory(s.RuntimeDirectory);
        string endpoint = s.OllamaUrl.TrimEnd('/');
        using var initial = await Get(endpoint + "/api/version", token);
        if (initial == null)
        {
            var uri = new Uri(s.OllamaUrl);
            if (!uri.IsLoopback)
                throw new InvalidOperationException("无法连接 Ollama，请检查地址。");
            string exe = s.OllamaExecutable;
            if (!File.Exists(exe))
                exe = Path.Combine(s.RuntimeDirectory, "ollama", "ollama.exe");
            if (!File.Exists(exe))
                exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe");
            if (!File.Exists(exe))
                exe = "ollama.exe";
            var info = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            info.ArgumentList.Add("serve");
            info.Environment["OLLAMA_HOST"] = $"127.0.0.1:{uri.Port}";
            info.Environment["OLLAMA_MODELS"] = Path.Combine(s.RuntimeDirectory, "ollama-models");
            info.Environment["OLLAMA_NUM_PARALLEL"] = "1";
            info.Environment["OLLAMA_MAX_LOADED_MODELS"] = "2";
            Status?.Invoke("正在启动 Ollama…");
            Launch(info);
            bool ready = false;
            for (int i = 0; i < 45; i++)
            {
                token.ThrowIfCancellationRequested();
                using var v = await Get(endpoint + "/api/version", token);
                if (v != null) { ready = true; break; }
                foreach (var process in owned)
                    if (process.HasExited)
                        throw new InvalidOperationException("Ollama 启动失败，请运行 Install.cmd 并查看诊断日志。");
                await Task.Delay(1000, token);
            }
            if (!ready)
                throw new TimeoutException("Ollama 未能启动，请查看诊断日志。");
        }
        using var version = await Get(endpoint + "/api/version", token);
        string value = version?.RootElement.GetProperty("version").GetString() ?? "";
        if (!Version.TryParse(value.Split('-')[0], out var parsed) || parsed < new Version(0, 32, 0))
            throw new InvalidOperationException("音频识别需要 Ollama 0.32.0 或更新版本，请运行 Install.cmd 更新。");
        using (var tags = await Get(endpoint + "/api/tags", token))
        {
            var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (tags != null)
                foreach (var model in tags.RootElement.GetProperty("models").EnumerateArray())
                    installed.Add(model.GetProperty("name").GetString() ?? "");
            foreach (string name in new[] { s.AsrModel, s.TranslationModel })
                if (!installed.Contains(name))
                    throw new InvalidOperationException($"尚未安装模型 {name}，请先运行 Install.cmd。");
        }
        if (!File.Exists(Path.Combine(s.RuntimeDirectory, "vad", "silero_vad.onnx")))
            throw new InvalidOperationException("尚未安装语音分段模型，请先运行 Install.cmd。");
        using var warm = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        Status?.Invoke("正在预热语音识别模型…");
        await AsrClient.RecognizeAsync(warm, s, new float[AsrProtocol.SampleRate], token, maxTokens: 16);
        Status?.Invoke("正在预热翻译模型…");
        using var body = new StringContent(JsonSerializer.Serialize(TranslationPrompt.Request(s, "Hello.", stream: false, maxTokens: 16)), Encoding.UTF8, "application/json");
        using var reply = await warm.PostAsync(endpoint + "/api/chat", body, token);
        reply.EnsureSuccessStatusCode();
        Status?.Invoke("识别和翻译模型已就绪");
    }

    private void Launch(ProcessStartInfo info)
    {
        var process = new Process { StartInfo = info };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) Log.Write("Ollama: " + e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) Log.Write("Ollama: " + e.Data); };
        process.Start();
        owned.Add(process);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    public Task StopOwnedAsync()
    {
        foreach (var process in owned)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception ex) { Log.Write("无法停止 Ollama：" + ex.Message); }
            process.Dispose();
        }
        owned.Clear();
        return Task.CompletedTask;
    }
    public async ValueTask DisposeAsync()
    {
        await StopOwnedAsync();
        http.Dispose();
    }
}
