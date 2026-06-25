using System.IO;

namespace Macrofy.App;

// Tracks one-time UI hints. Each flag is a tiny marker file in AppData so it survives
// restarts; best-effort (a failed read/write just means the hint may show again).
public static class OnboardingState
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Macrofy");

    public static bool HasSeen(string flag)
    {
        try { return File.Exists(MarkerPath(flag)); }
        catch { return false; }
    }

    public static void MarkSeen(string flag)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(MarkerPath(flag), DateTime.UtcNow.ToString("o"));
        }
        catch { /* best effort */ }
    }

    private static string MarkerPath(string flag) => Path.Combine(Dir, $"{flag}.seen");
}
