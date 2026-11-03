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
    private readonly string applicationRoot;
    public ServiceManager(string? applicationRoot = null) => this.applicationRoot = applicationRoot ?? Paths.Root;
    public event Action<string>? Status;
    private string ownedAsrHealth = "", controlToken = "";
    private async Task<JsonDocument?> Get(string url, CancellationToken token)
    {
        try
        {
            return JsonDocument.Parse(await http.GetStringAsync(url, token));
        }
        catch (HttpRequestException) { return null; }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
    }
    public async Task EnsureAsync(Settings s, CancellationToken token)
    {
        Directory.CreateDirectory(s.RuntimeDirectory);
        using var version = await Get(s.OllamaUrl.TrimEnd('/') + "/api/version", token);
        if (version == null)
        {
            var uri = new Uri(s.OllamaUrl);
            if (!uri.IsLoopback)
                throw new InvalidOperationException("无法连接翻译服务，请检查地址。");
            var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe");
            if (!File.Exists(exe))
                exe = "ollama.exe";
            var p = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            p.ArgumentList.Add("serve");
            p.Environment["OLLAMA_HOST"] = $"127.0.0.1:{uri.Port}";
            p.Environment["OLLAMA_MODELS"] = Path.Combine(s.RuntimeDirectory, "ollama-models");
            p.Environment["OLLAMA_NUM_PARALLEL"] = "1";
            Launch(p, "Ollama");
            for (int i = 0; i < 30; i++)
            {
                token.ThrowIfCancellationRequested();
                using var v = await Get(s.OllamaUrl.TrimEnd('/') + "/api/version", token);
                if (v != null)
                    break;
                await Task.Delay(1000, token);
            }
        }
        Status?.Invoke("检查翻译模型…");
        using (var tags = await Get(s.OllamaUrl.TrimEnd('/') + "/api/tags", token))
        {
            bool found = false;
            if (tags != null)
                foreach (var model in tags.RootElement.GetProperty("models").EnumerateArray())
                    if (model.GetProperty("name").GetString() == s.TranslationModel)
                        found = true;
            if (!found)
                throw new InvalidOperationException("翻译模型尚未下载。请先运行项目的 Install.ps1，或在高级设置中选择已经安装的模型。");
        }
        var asrUri = new Uri(s.AsrUrl);
        var health = new UriBuilder(asrUri) { Scheme = asrUri.Scheme == "wss" ? "https" : "http", Path = "/health" }.Uri.ToString();
        Status?.Invoke("正在预热翻译模型…");
        using (var warmClient = new HttpClient { Timeout = TimeSpan.FromMinutes(3) })
        {
            var body = new
            {
                model = s.TranslationModel,
                stream = false,
                think = false,
                keep_alive = "30m",
                messages = new[] { new { role = "user", content = "Translate into Chinese: Hello." } },
                options = new
                {
                    num_ctx = 2048,
                    num_predict = 16
                }
            };
            using var warm = await warmClient.PostAsync(s.OllamaUrl.TrimEnd('/') + "/api/chat", new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"), token);
            warm.EnsureSuccessStatusCode();
        }
        using var existing = await Get(health, token);
        if (existing == null)
        {
            if (!asrUri.IsLoopback)
                throw new InvalidOperationException("无法连接所设置的识别服务，请检查地址。");
            var script = Path.Combine(applicationRoot, "server", "launch.sh");
            if (!File.Exists(script))
                throw new FileNotFoundException("没有找到识别服务，请使用完整发布目录启动。", script);
            var p = new ProcessStartInfo("wsl.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            controlToken = Guid.NewGuid().ToString("N");
            ownedAsrHealth = health;
            foreach (var a in new[] { "-d", s.WslDistribution, "--exec", "bash", LinuxPath(script), LinuxPath(s.RuntimeDirectory), s.AsrModel, s.WslRuntimeDirectory, asrUri.Port.ToString(), controlToken })
                p.ArgumentList.Add(a);
            Launch(p, "ASR");
        }
        for (int i = 0; i < 600; i++)
        {
            token.ThrowIfCancellationRequested();
            using var h = await Get(health, token);
            if (h != null)
            {
                var root = h.RootElement;
                var status = root.GetProperty("status").GetString();
                if (status == "ready")
                {
                    Status?.Invoke("模型已就绪：" + root.GetProperty("model").GetString());
                    return;
                }
                if (status == "error")
                    throw new InvalidOperationException("语音模型加载失败：" + root.GetProperty("message").GetString());
            }
            if (i % 5 == 0)
                Status?.Invoke("正在准备语音模型，首次加载需要一些时间…");
            foreach (var p in owned)
                if (p.HasExited && p.ExitCode != 0)
                    throw new InvalidOperationException("本地服务启动失败，请先运行 Install.ps1 并查看诊断日志。");
            await Task.Delay(1000, token);
        }
        throw new TimeoutException("语音模型未能就绪，请查看 data/diagnostics.log。");
    }
    private void Launch(ProcessStartInfo info, string name)
    {
        var p = new Process { StartInfo = info };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) Log.Write(name + ": " + e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) Log.Write(name + ": " + e.Data); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        owned.Add(p);
    }
    public static string LinuxPath(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.Length < 3 || full[1] != ':')
            throw new ArgumentException("运行目录需要位于本地磁盘。");
        return "/mnt/" + char.ToLowerInvariant(full[0]) + full[2..].Replace('\\', '/');
    }
    public async Task StopOwnedAsync()
    {
        if (ownedAsrHealth.Length > 0)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, new UriBuilder(ownedAsrHealth) { Path = "/shutdown" }.Uri);
                request.Headers.Add("X-Control-Token", controlToken);
                using var reply = await http.SendAsync(request);
                if (reply.IsSuccessStatusCode)
                    await Task.Delay(700);
            }
            catch { }
        }
        foreach (var p in owned)
        {
            try
            {
                if (!p.HasExited)
                    p.Kill(true);
            }
            catch { }
            p.Dispose();
        }
        owned.Clear();
        ownedAsrHealth = "";
        controlToken = "";
    }
    public async ValueTask DisposeAsync()
    {
        await StopOwnedAsync();
        http.Dispose();
    }
}
