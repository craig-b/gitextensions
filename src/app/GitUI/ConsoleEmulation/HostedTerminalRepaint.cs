using GitCommands.Utils;

namespace GitUI.ConsoleEmulation;

/// <summary>
///  Keeps a terminal window owned by another process visible inside its WinForms host under Wine.
/// </summary>
/// <remarks>
///  Wine gives child windows no back buffer of their own: the terminal process paints straight into the
///  top-level X window, while the host control paints nothing, so the app's own back buffer still holds
///  whatever was drawn there before the terminal (e.g. the previous tab). Whenever the app flushes a
///  dirty area whose bounds span the terminal, typically the repaint on window activation, that stale
///  content lands on top of the terminal until something makes the terminal process paint again.
///  The cure is to ask it to paint after the host has finished its own painting.
/// </remarks>
internal static class HostedTerminalRepaint
{
    private static readonly int[] _delaysMs = [50, 250, 700];

    /// <summary>
    ///  Repaints the terminal window hosted in <paramref name="host"/> a few times over the next second,
    ///  after the host's own pending repaints have been flushed.
    /// </summary>
    public static void Soon(Control host)
    {
        if (!EnvUtils.RunningUnderWine || !host.IsHandleCreated)
        {
            return;
        }

        foreach (int delay in _delaysMs)
        {
            System.Windows.Forms.Timer timer = new() { Interval = delay };
            timer.Tick += (sender, e) =>
            {
                timer.Dispose();
                if (!host.IsDisposed && host.Visible)
                {
                    Repaint(host);
                }
            };
            timer.Start();
        }
    }

    private static void Repaint(Control host)
    {
        IntPtr hosted = NativeMethods.GetFirstChildWindow(host.Handle);
        if (hosted != IntPtr.Zero)
        {
            NativeMethods.RedrawWindow(hosted, IntPtr.Zero, IntPtr.Zero,
                NativeMethods.RDW.INVALIDATE | NativeMethods.RDW.ERASE | NativeMethods.RDW.ALLCHILDREN | NativeMethods.RDW.UPDATENOW);
        }
    }
}
