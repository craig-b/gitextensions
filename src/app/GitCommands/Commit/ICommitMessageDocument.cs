namespace GitCommands.Commit;

/// <summary>
///  The highlight the formatter assigns to a run of message text; the view maps each to its
///  colors (WinForms: WindowText / ControlText / red adapted to the editor background).
/// </summary>
public enum CommitMessageHighlight
{
    /// <summary>Within limits.</summary>
    Normal,

    /// <summary>Whole-line reset before re-formatting (historically ControlText).</summary>
    Reset,

    /// <summary>Beyond the configured line-length limit.</summary>
    Overlimit,
}

/// <summary>
///  The line-oriented editing surface <see cref="CommitMessageFormatter"/> drives - the same
///  member shapes as the WinForms spell-check editor, so the adapter there is 1:1, and any
///  other client implements it over its own text box.
/// </summary>
public interface ICommitMessageDocument
{
    int LineCount();

    string Line(int line);

    int LineLength(int line);

    void ReplaceLine(int line, string withText);

    /// <summary>
    ///  Inserts an empty line before line <paramref name="afterLine"/>'s content when that line
    ///  is non-empty, optionally prefixing the pushed-down content with a bullet.
    /// </summary>
    void EnsureEmptyLine(bool addBullet, int afterLine);

    void SetLineHighlight(int line, int offset, int length, CommitMessageHighlight highlight);
}
