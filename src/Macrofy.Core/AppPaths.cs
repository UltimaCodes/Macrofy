using System.IO;

namespace Macrofy.Core;

// Where Macrofy keeps settings, profiles and logs: %AppData%\Macrofy. Setting the
// MACROFY_DATA_DIR environment variable points it somewhere else, so tests and screenshots
// can run against a scratch folder without touching a real setup.
public static class AppPaths
{
    public static string DataDir { get; } = Resolve();

    public static string PathFor(string fileName) => Path.Combine(DataDir, fileName);

    private static string Resolve()
    {
        string? custom = Environment.GetEnvironmentVariable("MACROFY_DATA_DIR");
        string dir = !string.IsNullOrWhiteSpace(custom)
            ? Path.GetFullPath(custom)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Macrofy");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
