using System;
namespace LiveSubtitles;
public static class CaptionRules
{
    public static bool AcceptTranslation(string activeSession, string jobSession, int latestSourceRevision, string latestSource,
        int displayedTranslationRevision, int jobRevision, string jobSource)
    {
        if (string.IsNullOrEmpty(activeSession) || activeSession != jobSession)
            return false;
        if (displayedTranslationRevision > jobRevision)
            return false;
        return jobRevision >= latestSourceRevision || latestSource.StartsWith(jobSource, StringComparison.Ordinal);
    }
}
