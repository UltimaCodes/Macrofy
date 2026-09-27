using System.Diagnostics;
using static Macrofy.Core.Input.Interop.NativeMethods;

namespace Macrofy.Core;

// What kind of app a process is, for explaining why captured keys weren't blocked in it.
public enum AppKind { Normal, Elevated, StoreApp, Unknown }

public static class ForegroundApp
{
    public static (string Name, AppKind Kind) Describe(uint pid)
    {
        string name;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            name = p.ProcessName + ".exe";
        }
        catch { name = $"process {pid}"; }

        // Store apps' windows belong to this frame host; the app itself runs in a sandbox.
        if (name.Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
            return ("a Microsoft Store app", AppKind.StoreApp);

        nint process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == nint.Zero)
            return (name, AppKind.Unknown);
        try
        {
            // A non-elevated Macrofy can't open an elevated app's token at all.
            if (!OpenProcessToken(process, TOKEN_QUERY, out nint token))
                return (name, ProcessElevation.IsElevated ? AppKind.Unknown : AppKind.Elevated);
            try
            {
                if (GetTokenInformation(token, TokenIsAppContainer, out int appContainer, sizeof(int), out _) && appContainer != 0)
                    return (name, AppKind.StoreApp);
                if (GetTokenInformation(token, TokenElevation, out int elevated, sizeof(int), out _) && elevated != 0
                    && !ProcessElevation.IsElevated)
                    return (name, AppKind.Elevated);
                return (name, AppKind.Normal);
            }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }
}
