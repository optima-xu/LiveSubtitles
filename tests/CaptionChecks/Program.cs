using LiveSubtitles;
int checks=0;
void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
Check(!CaptionRules.AcceptTranslation("new","old",4,"I do not agree",2,3,"I agree"),"Old session must be rejected after pause/restart");
Check(!CaptionRules.AcceptTranslation("s","s",4,"I do not agree",2,3,"I agree"),"Revised negation must invalidate older translation");
Check(!CaptionRules.AcceptTranslation("s","s",4,"The model works",4,3,"The model"),"Late older result must not replace newer display");
Check(CaptionRules.AcceptTranslation("s","s",4,"The model works",2,3,"The model"),"Compatible prefix remains visible until replacement");
Check(CaptionRules.AcceptTranslation("s","s",4,"The model works",3,4,"The model works"),"Current final result should render");
Check(!CaptionRules.AcceptTranslation("","s",4,"The model works",3,4,"The model works"),"Stopped session must reject all late results");
var final=new Caption{Start=.125,End=2.375,Source="Do not delete it.",Translation="不要删除。",SourceFinal=true,TranslationFinal=true};
var srt=Exporter.Srt(new[]{final});
Check(srt.Contains("00:00:00,125 --> 00:00:02,375"),"SRT must preserve source time, including milliseconds");
Check(srt.Contains("不要删除。")&&srt.Contains("Do not delete it.")&&!srt.Contains("[草稿]"),"Final bilingual captions must export cleanly");
Check(Exporter.Srt(new[]{new Caption{Source="unfinished",Translation="未完成"}}).Contains("[未定稿] unfinished"),"Unfinished recognition must remain visibly marked in export");
Check(Exporter.Srt(new[]{new Caption{Source="missing",Gap=true}}).Contains("[音频缺失] missing"),"Audio loss must remain visible in export");
var mtSettings = new Settings { TargetLanguage = "English", Glossary = "gradient descent = 梯度下降" };
var mt = System.Text.Json.JsonSerializer.SerializeToElement(TranslationPrompt.Request(mtSettings, "请不要删除这两个文件。", "原文：An earlier sentence.\n译文：前一句。"));
Check(mt.GetProperty("stream").GetBoolean(), "Translation must continue to stream");
Check(!mt.TryGetProperty("think", out _), "HY-MT2 must not receive Qwen-specific thinking controls");
Check(mt.GetProperty("messages").GetArrayLength() == 1 && mt.GetProperty("messages")[0].GetProperty("role").GetString() == "user", "HY-MT2 requires a single user translation instruction");
var prompt = mt.GetProperty("messages")[0].GetProperty("content").GetString()!;
Check(prompt.Contains("翻译为英语") && prompt.EndsWith("请不要删除这两个文件。"), "Target language must use a full name and preserve source text");
Check(prompt.Contains("gradient descent 翻译成 梯度下降") && prompt.Contains("〖背景信息〗"), "Glossary and context must survive model migration");
var warm = System.Text.Json.JsonSerializer.SerializeToElement(TranslationPrompt.Request(mtSettings, "Hello.", stream: false, maxTokens: 16));
Check(!warm.GetProperty("stream").GetBoolean() && warm.GetProperty("options").GetProperty("num_predict").GetInt32() == 16, "Warm-up must use the same adapter with bounded output");
mtSettings.TranslationModel = "qwen3.5:4b-q4_K_M";
var legacy = System.Text.Json.JsonSerializer.SerializeToElement(TranslationPrompt.Request(mtSettings, "Hello."));
Check(legacy.GetProperty("messages").GetArrayLength() == 2 && !legacy.GetProperty("think").GetBoolean(), "Previously configured Qwen models must remain usable");
var savedLanguages = System.Text.Json.JsonSerializer.Serialize(new Settings { SourceLanguage = "Japanese", TargetLanguage = "Korean" });
var restoredLanguages = System.Text.Json.JsonSerializer.Deserialize<Settings>(savedLanguages)!;
Check(LanguageCatalog.Sources.Single(x => x.Value == restoredLanguages.SourceLanguage).Name == "日语" && LanguageCatalog.Targets.Single(x => x.Value == restoredLanguages.TargetLanguage).Name == "韩语", "Saved language values must resolve to selectable entries after restart");
var multilingual = System.Text.Json.JsonSerializer.SerializeToElement(TranslationPrompt.Request(restoredLanguages, "明日の会議は9時です。"));
var multilingualPrompt = multilingual.GetProperty("messages")[0].GetProperty("content").GetString()!;
Check(multilingualPrompt.Contains("翻译为韩语") && multilingualPrompt.EndsWith("明日の会議は9時です。"), "A foreign-language request must map the target identifier to its full language name without changing the source");
var wav = AsrProtocol.Wave(new float[] { -1, 0, 1 });
Check(wav.Length == 50 && System.Text.Encoding.ASCII.GetString(wav, 0, 4) == "RIFF" && BitConverter.ToInt32(wav, 24) == 16000 && BitConverter.ToInt16(wav, 44) == -32768 && BitConverter.ToInt16(wav, 48) == 32767, "Ollama audio must be a valid mono 16 kHz PCM16 WAV, including clipping and header lengths");
Check(AsrProtocol.Text("language English<asr_text>Hello.<|im_end|>") == "Hello." && AsrProtocol.Text("language None<asr_text>") == "", "ASR language markers and non-speech must never become subtitles");
Check(AsrProtocol.Text("不要删除文件。") == "不要删除文件。", "Language-prefilled output must survive without an ASR marker");
Check(AsrProtocol.RemoveOverlap("We need to test the model.", "the model works well.") == "works well.", "Rolling English overlap must be removed");
Check(AsrProtocol.RemoveOverlap("今天学习机器翻译", "机器翻译的原理。") == "的原理。", "Rolling Chinese overlap must be removed by characters");
Check(AsrProtocol.RemoveOverlap("I said yes", "yes yes this is intended") == "yes yes this is intended", "A single repeated word must not be deleted without reliable overlap");
var nativeRequest = System.Text.Json.JsonSerializer.SerializeToElement(AsrProtocol.Request(new Settings { SourceLanguage = "Japanese" }, new float[16000]));
Check(nativeRequest.GetProperty("messages")[0].GetProperty("content").GetString() == "Japanese" && nativeRequest.GetProperty("messages")[1].GetProperty("images").GetArrayLength() == 1, "Source language must prefill the ASR template and audio must use Ollama's audio-capable images transport");
var automatic = System.Text.Json.JsonSerializer.SerializeToElement(AsrProtocol.Request(new Settings { SourceLanguage = "Auto" }, new float[512]));
Check(automatic.GetProperty("messages").GetArrayLength() == 1, "Automatic language recognition must not force an English prefill");
var segmenter = new AsrSegmenter();
var windows = new List<AsrWindow>();
segmenter.Window += windows.Add;
double time = 0;
void Feed(int frames, bool speech) { for (int i=0; i<frames; i++) { segmenter.Push(new float[512], time, speech); time+=0.032; } }
Feed(100, false);
Check(windows.Count == 0, "Silence must not schedule model requests");
Feed(80, true);
Check(windows.Count > 0 && windows.All(x=>!x.Final), "A speaking segment must emit drafts before the speaker pauses");
Feed(25, false);
Check(windows.Last().Final && windows.Select(x=>x.Segment).Distinct().Count()==1 && windows.Zip(windows.Skip(1)).All(x=>x.First.Revision<x.Second.Revision), "A pause must finalize the same segment with monotonic revisions");
long oldSegment=windows.Last().Segment;
windows.Clear();
Feed(800, true);
Check(windows.Count(x=>x.Final)>=2 && windows.All(x=>x.Audio.Length<=12*16000+512), "Continuous speech must roll over without unbounded audio growth");
Check(windows.Any(x=>x.OverlapSegment>oldSegment) && windows.Where(x=>x.Final).All(x=>x.End>=x.Start), "Rollover must carry overlap identity and valid timestamps");
long beforeReset=windows.Last().Segment;
segmenter.Reset();windows.Clear();Feed(100,false);Feed(40,true);
Check(windows.Count>0 && windows.All(x=>x.Segment>beforeReset && x.OverlapSegment==0), "An audio discontinuity must invalidate old acoustic state and segment identity");
var work=new AsrWorkQueue(2);
AsrWindow Job(long id,int rev,bool done=false)=>new(id,rev,new float[512],done,0,1);
Check(work.Add(Job(1,1))&&work.Add(Job(1,2))&&work.Count==1, "Queued drafts must coalesce");
work.Add(Job(1,3,true));work.Add(Job(1,4));work.Add(Job(2,1));
Check(!work.Add(Job(3,1)), "Backlog overflow must be visible, not silently drop finals");
Check(work.Take() is { Segment:1, Revision:3, Final:true } && work.Take() is { Segment:2 } && work.Take()==null, "Finals must survive later drafts and preserve segment order");
work.Add(Job(3,1));work.Clear();Check(work.Count==0 && work.Take()==null, "Session reset must discard pending recognition");
Console.WriteLine($"Passed {checks} subtitle, translation, native audio and streaming-state checks.");

namespace LiveSubtitles
{
    public static class Paths
    {
        public static string Root => Path.GetTempPath();
        public static string Data => Path.GetTempPath();
    }
}
