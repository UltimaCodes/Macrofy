using System.IO;
using System.Text.Json;

namespace Macrofy.App;

// Small persisted app preferences (separate from device names / macro profiles).
// Stored at %AppData%/Macrofy/settings.json; best-effort load/save.
public sealed class AppSettings
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Macrofy");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    // When true, closing the window hides Macrofy to the tray; when false, closing quits.
    public bool MinimizeToTrayOnClose { get; set; } = true;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { /* fall back to defaults on a corrupt/missing file */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* best effort */ }
    }
}
