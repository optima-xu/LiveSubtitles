using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LiveSubtitles;

public static class AsrProtocol
{
    public const string DefaultModel = "qwen3-asr:0.6b-q8_0";
    public const int SampleRate = 16000;

    public static object Request(Settings settings, float[] audio, int maxTokens = 256)
    {
        var messages = new List<object>();
        // Our pinned ASR template uses System only as the language prefill.
        // An empty System lets Qwen identify the language itself.
        if (settings.SourceLanguage != "Auto")
            messages.Add(new { role = "system", content = settings.SourceLanguage });
        messages.Add(new { role = "user", content = "", images = new[] { Convert.ToBase64String(Wave(audio)) } });
        return new
        {
            model = settings.AsrModel,
            messages,
            stream = false,
            keep_alive = "30m",
            options = new { temperature = 0, num_ctx = 2048, num_predict = maxTokens, repeat_penalty = 1.0 }
        };
    }

    public static byte[] Wave(float[] samples)
    {
        using var stream = new MemoryStream(44 + samples.Length * 2);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + samples.Length * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(samples.Length * 2);
        foreach (float value in samples)
            writer.Write((short)Math.Clamp(value * 32768.0f, -32768, 32767));
        return stream.ToArray();
    }

    public static string Text(string response)
    {
        int marker = response.IndexOf("<asr_text>", StringComparison.Ordinal);
        if (marker >= 0)
            response = response[(marker + 10)..];
        else if (response.TrimStart().StartsWith("language ", StringComparison.OrdinalIgnoreCase))
            return "";
        return Regex.Replace(response, @"<\|[^>]*\|>|</?asr_text>", "").Trim();
    }

    public static string RemoveOverlap(string previous, string current)
    {
        const string pattern = @"[\p{IsCJKUnifiedIdeographs}\p{IsHiragana}\p{IsKatakana}\p{IsHangulSyllables}]|[\p{L}\p{N}]+";
        var left = Regex.Matches(previous, pattern).Cast<Match>().ToArray();
        var right = Regex.Matches(current, pattern).Cast<Match>().ToArray();
        for (int count = Math.Min(16, Math.Min(left.Length, right.Length)); count >= 2; count--)
        {
            bool equal = true;
            for (int i = 0; i < count; i++)
                equal &= string.Equals(left[left.Length - count + i].Value, right[i].Value, StringComparison.OrdinalIgnoreCase);
            if (equal)
                return current[(right[count - 1].Index + right[count - 1].Length)..].TrimStart(' ', ',', '.', '，', '。', '!', '?', '！', '？', ':', '：', ';', '；');
        }
        return current;
    }
}
