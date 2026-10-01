using System.IO;
using Macrofy.Core;

namespace Macrofy.App;

// Appends unhandled exceptions to <data>/log.txt so field crashes leave a trace.
public static class CrashLog
{
    public static string FilePath { get; } = AppPaths.PathFor("log.txt");

    public static void Write(Exception? ex)
    {
        if (ex is null)
            return;
        try
        {
            File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* logging must never throw */ }
    }
}
