using System.Text;

namespace GitExtensions.Extensibility.Extensions;

/// <summary>
///  Platform-neutral half of <see cref="UIExtensions"/> (M6): no WinForms types are needed to format
///  a commit body with its notes, so it is split out from the CheckBox/Font/Graphics helpers in
///  UIExtensions.cs, which stay WinForms-only.
/// </summary>
public static partial class UIExtensions
{
    /// <summary>
    /// bodyOrSubject
    /// Notes:
    ///     notes
    /// </summary>
    public static string FormatBodyAndNotes(string bodyOrSubject, string? notes)
    {
        if (string.IsNullOrEmpty(notes))
        {
            return bodyOrSubject;
        }

        const string notesPrefix = "Notes:";
        const string indent = "    ";

        // trying to avoid buffer re-allocation during Append()
        StringBuilder sb = new(bodyOrSubject.Length + 4 + notesPrefix.Length + 2 + indent.Length + notes.Length + 1);
        if (bodyOrSubject.Length > 0)
        {
            sb.AppendLine(bodyOrSubject);
        }

        sb.AppendLine().AppendLine(notesPrefix);

        ReadOnlySpan<char> notesAsSpan = notes.AsSpan();
        foreach (Range range in notesAsSpan.Split('\n'))
        {
            sb.Append(indent).Append(notesAsSpan[range]).Append('\n');
        }

        --sb.Length; // removing the last artificially appended \n
        return sb.ToString();
    }
}
