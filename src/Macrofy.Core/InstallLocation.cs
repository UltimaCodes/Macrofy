using System.IO;

namespace Macrofy.Core;

// Whether Macrofy's files sit where only administrators can change them (Program Files).
// Anything that runs elevated without a prompt must come from such a folder; otherwise any
// program running as the user could swap the file and get admin rights through Macrofy.
public static class InstallLocation
{
    public static bool IsProtected => IsUnderProgramFiles(AppContext.BaseDirectory);

    public static bool IsUnderProgramFiles(string dir)
    {
        string full = Path.GetFullPath(dir);
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            string root = Environment.GetFolderPath(folder);
            if (root.Length > 0 && full.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
