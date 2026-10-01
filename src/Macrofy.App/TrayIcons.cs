using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Macrofy.App;

// The tray icon in three states: the plain logo when idle, a teal dot while capturing, and an
// amber dot when capture can't work (the hook didn't load). Drawn once at startup.
internal sealed class TrayIcons : IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint hIcon);

    public TrayIcons(Icon logo)
    {
        Idle = logo;
        Capturing = WithDot(logo, Color.FromArgb(0x2D, 0xD4, 0xBF));
        Problem = WithDot(logo, Color.FromArgb(0xF0, 0xB4, 0x4A));
    }

    public Icon Idle { get; }
    public Icon Capturing { get; }
    public Icon Problem { get; }

    private static Icon WithDot(Icon logo, Color dot)
    {
        using var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawIcon(logo, new Rectangle(0, 0, 32, 32));
            using var ring = new SolidBrush(Color.FromArgb(0x12, 0x13, 0x16));
            g.FillEllipse(ring, 16, 16, 16, 16);
            using var fill = new SolidBrush(dot);
            g.FillEllipse(fill, 18.5f, 18.5f, 11, 11);
        }
        nint handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone(); // the clone owns its own handle
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    public void Dispose()
    {
        Capturing.Dispose();
        Problem.Dispose();
        Idle.Dispose();
    }
}
