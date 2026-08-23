using System.Text;

namespace GitExtensions.Extensibility.Extensions;

/// <summary>
///  Platform-neutral text helpers. The WinForms-typed control helpers that shared this class
///  moved to Extensibility.WinForms' UIWinFormsExtensions (same namespace), clearing the last
///  Extensibility probe debt.
/// </summary>
public static class UIExtensions
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
