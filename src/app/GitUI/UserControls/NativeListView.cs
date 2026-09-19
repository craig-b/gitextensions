using static System.NativeMethods;

namespace GitUI.UserControls;

public class NativeListView : ListView
{
    internal static event EventHandler? BeginCreateHandle;
    internal static event EventHandler? EndCreateHandle;
    internal event ScrollEventHandler? Scroll;

    private const int WM_REFLECT_NOTIFY = 0x2000 + 0x004E;
    private const int NM_CUSTOMDRAW = -12;
    private const int CDDS_ITEMPREPAINT = 0x00010001;
    private const int CDRF_SKIPDEFAULT = 0x00000004;

    /// <summary>
    /// When set, the control answers the item pre-paint custom-draw stage with CDRF_SKIPDEFAULT
    /// after <see cref="ListView.DrawItem"/> ran, so the native control paints nothing of its own.
    /// Wine's comctl32 ignores the sub-item handshake WinForms uses for owner-drawn details/tile
    /// views and paints the plain item text over the owner drawing; this keeps it out.
    /// </summary>
    internal bool SkipNativeItemPainting { get; set; }

    public NativeListView()
    {
        DoubleBuffered = true;
    }

    protected override void CreateHandle()
    {
        BeginCreateHandle?.Invoke(this, EventArgs.Empty);
        base.CreateHandle();

        if (!Application.IsDarkModeEnabled)
        {
            // explorer style selection painting in left panel
            // Not needed in dark mode, this is the same for "DarkMode_Explorer"
            NativeMethods.SetWindowTheme(Handle, "explorer", null);
        }

        EndCreateHandle?.Invoke(this, EventArgs.Empty);
    }

    protected override void WndProc(ref Message m)
    {
        Message message = m;
        switch (m.Msg)
        {
            case WM_REFLECT_NOTIFY when SkipNativeItemPainting && OwnerDraw:
                base.WndProc(ref m);
                if (IsItemPrePaintNotification(m.LParam))
                {
                    m.Result = CDRF_SKIPDEFAULT;
                }

                break;

            default:
                HandleScroll(m);
                base.WndProc(ref m);
                break;
        }

        static bool IsItemPrePaintNotification(IntPtr nmhdr)
        {
            // NMHDR { HWND hwndFrom; UINT_PTR idFrom; UINT code; } followed by NMCUSTOMDRAW.dwDrawStage
            int code = System.Runtime.InteropServices.Marshal.ReadInt32(nmhdr, 2 * IntPtr.Size);
            if (code != NM_CUSTOMDRAW)
            {
                return false;
            }

            int nmhdrSize = IntPtr.Size == 8 ? 24 : 12;
            int drawStage = System.Runtime.InteropServices.Marshal.ReadInt32(nmhdr, nmhdrSize);
            return drawStage == CDDS_ITEMPREPAINT;
        }

        void HandleScroll(Message msg)
        {
            ScrollEventType type;
            int? newValue = null;

            switch (msg.Msg)
            {
                case WM_VSCROLL:
                    type = (ScrollEventType)LowWord(msg.WParam.ToInt64());
                    newValue = HighWord(msg.WParam.ToInt64());
                    break;

                case WM_MOUSEWHEEL:
                    type = HighWord(msg.WParam.ToInt64()) > 0
                        ? ScrollEventType.SmallDecrement
                        : ScrollEventType.SmallIncrement;
                    break;

                case WM_KEYDOWN:
                    switch ((Keys)(int)(long)msg.WParam)
                    {
                        case Keys.Up:
                            type = ScrollEventType.SmallDecrement;
                            break;
                        case Keys.Down:
                            type = ScrollEventType.SmallIncrement;
                            break;
                        case Keys.PageUp:
                            type = ScrollEventType.LargeDecrement;
                            break;
                        case Keys.PageDown:
                            type = ScrollEventType.LargeIncrement;
                            break;
                        case Keys.Home:
                            type = ScrollEventType.First;
                            break;
                        case Keys.End:
                            type = ScrollEventType.Last;
                            break;
                        default:
                            return;
                    }

                    break;

                default:
                    return;
            }

            newValue ??= GetScrollPos(Handle, SB.VERT);
            Scroll?.Invoke(this, new ScrollEventArgs(type, newValue.Value));

            short LowWord(long number) =>
                unchecked((short)(number & 0x0000ffff));

            short HighWord(long number) =>
                unchecked((short)(number >> 16));
        }
    }
}
