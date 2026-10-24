using System.Windows.Forms;
using LocalCaptureAgent.Services;

namespace LocalCaptureAgent.Controls;

internal sealed class BufferedDialogForm : Form
{
    public BufferedDialogForm()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;
    }

    // De teksten in de taal van de interface, voordat het venster te zien is.
    protected override void OnLoad(EventArgs e)
    {
        UiTextBinder.Attach(this);
        base.OnLoad(e);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            if (!SystemInformation.TerminalServerSession)
            {
                cp.ExStyle |= 0x02000000;
            }

            return cp;
        }
    }
}
