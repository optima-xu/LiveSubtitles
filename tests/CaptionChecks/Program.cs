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
Console.WriteLine($"Passed {checks} subtitle ordering/export checks.");

namespace LiveSubtitles
{
    public static class Paths
    {
        public static string Root => Path.GetTempPath();
        public static string Data => Path.GetTempPath();
    }
}
