using System.IO;
using System.Text;

namespace Macrofy.Core.IO;

// Writes a file so a crash or power cut mid-save leaves either the old contents or the new
// ones, never a half-written file: write a temp file next to it, flush it to disk, then
// swap it in with a single rename.
public static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        string tmp = path + ".tmp";
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(contents);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(flushToDisk: true);
        }
        File.Move(tmp, path, overwrite: true);
    }
}
