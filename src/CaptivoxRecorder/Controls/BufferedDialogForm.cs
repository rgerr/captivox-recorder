using System.Windows.Forms;

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
