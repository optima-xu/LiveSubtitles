using System.Collections.Generic;
using System.Linq;

namespace LiveSubtitles;

public sealed record LanguageOption(string Value, string Name);

public static class LanguageCatalog
{
    // Audio choices are the intersection of the official Qwen3-ASR and HY-MT2 language lists.
    // Qwen: https://github.com/QwenLM/Qwen3-ASR#released-models-description-and-download
    public static IReadOnlyList<LanguageOption> Sources { get; } = new LanguageOption[]
    {
        new("English", "英语"), new("Chinese", "中文"), new("Auto", "自动识别"),
        new("Japanese", "日语"), new("Korean", "韩语"), new("Cantonese", "粤语"),
        new("French", "法语"), new("German", "德语"), new("Spanish", "西班牙语"),
        new("Portuguese", "葡萄牙语"), new("Italian", "意大利语"), new("Russian", "俄语"),
        new("Arabic", "阿拉伯语"), new("Thai", "泰语"), new("Vietnamese", "越南语"),
        new("Indonesian", "印尼语"), new("Malay", "马来语"), new("Filipino", "菲律宾语"),
        new("Hindi", "印地语"), new("Turkish", "土耳其语"), new("Dutch", "荷兰语"),
        new("Polish", "波兰语"), new("Czech", "捷克语"), new("Persian", "波斯语")
    };

    // Preserve the three existing setting values; display labels must not become protocol identifiers.
    // HY-MT2: https://huggingface.co/tencent/Hy-MT2-1.8B-GGUF#supported-languages
    public static IReadOnlyList<LanguageOption> Targets { get; } = new LanguageOption[]
    {
        new("简体中文", "简体中文"), new("繁體中文", "繁體中文"), new("English", "英语"),
        new("Japanese", "日语"), new("Korean", "韩语"), new("Cantonese", "粤语"),
        new("French", "法语"), new("German", "德语"), new("Spanish", "西班牙语"),
        new("Portuguese", "葡萄牙语"), new("Italian", "意大利语"), new("Russian", "俄语"),
        new("Arabic", "阿拉伯语"), new("Thai", "泰语"), new("Vietnamese", "越南语"),
        new("Indonesian", "印尼语"), new("Malay", "马来语"), new("Filipino", "菲律宾语"),
        new("Hindi", "印地语"), new("Turkish", "土耳其语"), new("Dutch", "荷兰语"),
        new("Polish", "波兰语"), new("Czech", "捷克语"), new("Persian", "波斯语"),
        new("Khmer", "高棉语"), new("Burmese", "缅甸语"), new("Gujarati", "古吉拉特语"),
        new("Urdu", "乌尔都语"), new("Telugu", "泰卢固语"), new("Marathi", "马拉地语"),
        new("Hebrew", "希伯来语"), new("Bengali", "孟加拉语"), new("Tamil", "泰米尔语"),
        new("Ukrainian", "乌克兰语"), new("Tibetan", "藏语"), new("Kazakh", "哈萨克语"),
        new("Mongolian", "蒙古语"), new("Uyghur", "维吾尔语")
    };

    public static string TargetName(string value) => Targets.FirstOrDefault(x => x.Value == value)?.Name ?? value;
}
