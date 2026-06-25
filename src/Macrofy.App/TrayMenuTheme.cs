using System.Drawing;
using System.Windows.Forms;

namespace Macrofy.App;

// Dark color table for the tray context menu so it matches Macrofy instead of the stock
// light Win32 menu. Paired with ToolStripProfessionalRenderer in MainWindow.InitTray.
internal sealed class DarkMenuColors : ProfessionalColorTable
{
    public static readonly Color Background = Color.FromArgb(0x1C, 0x1D, 0x22);
    public static readonly Color Text = Color.FromArgb(0xE6, 0xE7, 0xEA);
    private static readonly Color Hover = Color.FromArgb(0x2E, 0x30, 0x36);
    private static readonly Color BorderC = Color.FromArgb(0x3A, 0x3C, 0x42);
    private static readonly Color CheckBg = Color.FromArgb(0x21, 0x3A, 0x35);

    public DarkMenuColors() => UseSystemColors = false;

    public override Color ToolStripDropDownBackground => Background;
    public override Color ImageMarginGradientBegin => Background;
    public override Color ImageMarginGradientMiddle => Background;
    public override Color ImageMarginGradientEnd => Background;

    public override Color MenuItemSelected => Hover;
    public override Color MenuItemSelectedGradientBegin => Hover;
    public override Color MenuItemSelectedGradientEnd => Hover;
    public override Color MenuItemPressedGradientBegin => Hover;
    public override Color MenuItemPressedGradientEnd => Hover;
    public override Color MenuItemBorder => Hover;

    public override Color MenuBorder => BorderC;
    public override Color ToolStripBorder => BorderC;
    public override Color SeparatorDark => BorderC;
    public override Color SeparatorLight => BorderC;

    public override Color CheckBackground => CheckBg;
    public override Color CheckSelectedBackground => CheckBg;
    public override Color CheckPressedBackground => CheckBg;
}
