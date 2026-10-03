using System.Windows.Controls;

namespace Macrofy.App;

// The Macrofy logo: a keycap with an M on it. Reused in the sidebar, the
// welcome window and the About page. The exe and tray icon come from Assets\macrofy.ico,
// which is rendered from the same drawing.
public partial class LogoMark : UserControl
{
    public LogoMark() => InitializeComponent();
}
