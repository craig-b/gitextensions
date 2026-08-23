namespace GitExtensions.Extensibility.Extensions;

/// <summary>
///  The WinForms-typed control helpers that used to live in UIExtensions (Extensibility) - moved
///  here to clear the last Extensibility probe debt. Namespace kept so the extension methods
///  resolve unchanged at every call site.
/// </summary>
public static class UIWinFormsExtensions
{
    public static bool? GetNullableChecked(this CheckBox chx)
    {
        if (chx.CheckState == CheckState.Indeterminate)
        {
            return null;
        }
        else
        {
            return chx.Checked;
        }
    }

    public static void SetNullableChecked(this CheckBox chx, bool? @checked)
    {
        if (@checked.HasValue)
        {
            chx.CheckState = @checked.Value ? CheckState.Checked : CheckState.Unchecked;
        }
        else
        {
            chx.CheckState = CheckState.Indeterminate;
        }
    }

    public static bool IsFixedWidth(this Font ft, Graphics g)
    {
        ReadOnlySpan<char> charSizes = ['i', 'a', 'Z', '%', '#', 'a', 'B', 'l', 'm', ',', '.'];
        float charWidth = g.MeasureString("I", ft).Width;

        foreach (char c in charSizes)
        {
            if (Math.Abs(g.MeasureString(c.ToString(), ft).Width - charWidth) > float.Epsilon)
            {
                return false;
            }
        }

        return true;
    }
}
