namespace GitExtensions.Extensibility.Extensions;

/// <summary>
///  WinForms-typed control helpers. Platform-neutral (M6): <see cref="FormatBodyAndNotes"/> needs no
///  WinForms types, so it lives in the portable half of this partial class, UIExtensions.FormatBodyAndNotes.cs.
/// </summary>
public static partial class UIExtensions
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
