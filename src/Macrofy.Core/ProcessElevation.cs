using System.Security.Principal;

namespace Macrofy.Core;

// Whether Macrofy is running elevated. Elevation lets the hook block keys in apps that run as
// administrator, but it also means anything Macrofy launches or loads runs with admin rights.
public static class ProcessElevation
{
    public static bool IsElevated { get; } = Check();

    private static bool Check()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }
}
