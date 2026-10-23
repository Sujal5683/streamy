using System.Drawing;
using System.Windows.Forms;

namespace EnterpriseRemote.Controller.UI.Components;

/// <summary>
/// A smooth scrollable panel that prevents Windows Forms from automatically jumping
/// or violently scrolling the viewport whenever child controls (e.g. textboxes, buttons)
/// receive focus.
/// </summary>
public class CyberScrollPanel : Panel
{
    public CyberScrollPanel()
    {
        DoubleBuffered = true;
        AutoScroll = true;
    }

    protected override Point ScrollToControl(Control activeControl)
    {
        // Return current DisplayRectangle location to prevent WinForms from jumping
        // scroll position when child controls receive focus
        return DisplayRectangle.Location;
    }
}
