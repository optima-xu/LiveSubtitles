using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LiveSubtitles;

// Real Ollama requests through the application's streaming scheduler; no audio capture or playback.
var settings = args.Length > 0
    ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(args[0])) ?? throw new ArgumentException("Invalid settings")
    : new Settings();
if (args.Length > 1) settings.OllamaUrl = args[1];
using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) })
{
    using var warm = await http.PostAsync(settings.OllamaUrl.TrimEnd('/') + "/api/chat",
        new StringContent(JsonSerializer.Serialize(TranslationPrompt.Request(settings, "Hello.", stream: false)), Encoding.UTF8, "application/json"));
    warm.EnsureSuccessStatusCode();
}
int passed = 0, total = 0;
async Task Run(TranslationService translator, string name, string source, Func<string, bool> verify)
{
    long segment = ++total;
    var completion = new TaskCompletionSource<TranslationUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
    var watch = Stopwatch.StartNew();
    int updates = 0;
    double first = -1;
    void OnUpdate(TranslationUpdate update)
    {
        if (update.Job.Segment != segment) return;
        if (!update.Done)
        {
            updates++;
            if (first < 0) first = watch.Elapsed.TotalSeconds;
        }
        else completion.TrySetResult(update);
    }
    translator.Updated += OnUpdate;
    try
    {
        translator.Submit(new("translation-probe", segment, 1, source, true, 0, 1));
        var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(25));
        bool ok = result.Error.Length == 0 && updates > 0 && verify(result.Text);
        if (ok) passed++;
        Console.WriteLine(JsonSerializer.Serialize(new { name, source, target = settings.TargetLanguage, text = result.Text, result.Error, updates, firstSeconds = first, totalSeconds = watch.Elapsed.TotalSeconds, passed = ok }));
    }
    finally { translator.Updated -= OnUpdate; }
}
settings.TargetLanguage = "简体中文";
await using (var translator = new TranslationService(settings))
{
    await Run(translator, "numbers-negation", "The total is 120 dollars, not 20 dollars.",
        text => text.Contains("120") && Regex.IsMatch(text, @"(?<!\d)20(?!\d)") && text.Contains('不'));
    await Run(translator, "context-first", "Alice is preparing the report.", text => text.Contains("报告"));
    await Run(translator, "context-followup", "She will send it tomorrow.", text => text.Contains("明天") && !text.Contains("准备"));
    settings.Glossary = "gradient descent = 梯度下降\nlearning rate = 学习率";
    await Run(translator, "glossary", "We use gradient descent with a learning rate of 0.01.", text => text.Contains("梯度下降") && text.Contains("学习率") && text.Contains("0.01"));
}
settings.Glossary = "";
settings.TargetLanguage = "繁體中文";
await using (var translator = new TranslationService(settings))
    await Run(translator, "traditional-chinese", "This software supports video translation.", text => text.Contains("軟") && text.Contains("譯") && !text.Contains("软"));
settings.TargetLanguage = "English";
await using (var translator = new TranslationService(settings))
    await Run(translator, "english", "请不要删除这两个文件。", text => text.Contains("not", StringComparison.OrdinalIgnoreCase) && text.Contains("file", StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(text, @"[\u4e00-\u9fff]"));
Console.WriteLine(JsonSerializer.Serialize(new { summary = true, model = settings.TranslationModel, passed, total }));
return passed == total ? 0 : 1;
