using System.IO;
using System.Security.Cryptography;

namespace Macrofy.Core.Input;

// Picks the MacrofyHook.dll to load. The copy embedded in this assembly always matches this
// build, so a DLL beside the exe is used only when it's byte-for-byte that copy; otherwise
// (missing, or a stale one from an older download) the embedded copy is written out.
//
// Elevated, the hook gets loaded into admin apps too, so it must come from a folder only
// administrators can write to. A copy in Downloads or AppData could be swapped by any program
// running as you, which would turn Macrofy into a way to run that code as admin.
internal static class HookDllLocator
{
    public const string FileName = "MacrofyHook.dll";

    public static string? Resolve(out string? problem)
    {
        problem = null;
        byte[]? embedded = ReadEmbedded();
        string beside = Path.Combine(AppContext.BaseDirectory, FileName);

        if (ProcessElevation.IsElevated)
        {
            if (InstallLocation.IsProtected && File.Exists(beside)
                && (embedded is null || SameContent(beside, embedded)))
                return beside;
            if (embedded is not null)
            {
                string safe = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Macrofy", "hook", FileName);
                if (WriteIfDifferent(safe, embedded))
                    return safe;
            }
            problem = "Macrofy couldn't put its keyboard hook in a protected folder, so it won't load it while running as administrator.";
            return null;
        }

        if (File.Exists(beside) && (embedded is null || SameContent(beside, embedded)))
            return beside;
        if (embedded is not null)
        {
            string target = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Macrofy", FileName);
            if (WriteIfDifferent(target, embedded))
                return target;
        }
        problem = "MacrofyHook.dll is missing. Your antivirus may have removed it. Restore it from quarantine or download Macrofy again.";
        return null;
    }

    private static byte[]? ReadEmbedded()
    {
        try
        {
            using var stream = typeof(HookDllLocator).Assembly.GetManifestResourceStream(FileName);
            if (stream is null)
                return null;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
        catch { return null; }
    }

    private static bool SameContent(string path, byte[] expected)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length != expected.Length)
                return false;
            return SHA256.HashData(File.ReadAllBytes(path)).AsSpan().SequenceEqual(SHA256.HashData(expected));
        }
        catch { return false; }
    }

    // Writes the DLL unless an identical copy is already there. A copy from a previous run can
    // still be mapped into other processes (Windows unloads hook DLLs lazily), which blocks
    // overwriting it but not renaming it, so the old one is moved aside first.
    private static bool WriteIfDifferent(string target, byte[] content)
    {
        try
        {
            string dir = Path.GetDirectoryName(target)!;
            Directory.CreateDirectory(dir);
            foreach (var stale in Directory.GetFiles(dir, "MacrofyHook.old-*.dll"))
            {
                try { File.Delete(stale); } catch { /* still loaded somewhere; next time */ }
            }
            if (File.Exists(target) && SameContent(target, content))
                return true;
            if (File.Exists(target))
                File.Move(target, Path.Combine(dir, $"MacrofyHook.old-{Guid.NewGuid():N}.dll"));
            string tmp = target + ".tmp";
            File.WriteAllBytes(tmp, content);
            File.Move(tmp, target, overwrite: true);
            return true;
        }
        catch
        {
            return File.Exists(target) && SameContent(target, content);
        }
    }
}
