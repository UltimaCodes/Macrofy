using System.IO;
using Macrofy.Core;

namespace Macrofy.App;

// Tracks one-time UI hints. Each flag is a tiny marker file in the data folder so it
// survives restarts; best-effort (a failed read/write just means the hint may show again).
public static class OnboardingState
{
    public static bool HasSeen(string flag)
    {
        try { return File.Exists(MarkerPath(flag)); }
        catch { return false; }
    }

    public static void MarkSeen(string flag)
    {
        try { File.WriteAllText(MarkerPath(flag), DateTime.UtcNow.ToString("o")); }
        catch { /* best effort */ }
    }

    private static string MarkerPath(string flag) => AppPaths.PathFor($"{flag}.seen");
}
