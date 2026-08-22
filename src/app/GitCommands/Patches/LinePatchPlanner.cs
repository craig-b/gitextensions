using System.Text;
using GitExtensions.Extensibility;
using GitExtUtils;

namespace GitCommands.Patches;

/// <summary>The line-patch verbs of the diff-pane menu, with the honest committed-diff pair.</summary>
public enum LinePatchVerb
{
    /// <summary>Stage the selected worktree lines into the index.</summary>
    Stage,

    /// <summary>Unstage the selected index lines back to the worktree.</summary>
    Unstage,

    /// <summary>Discard the selected worktree lines.</summary>
    ResetWorkTree,

    /// <summary>Reset the selected staged lines out of the index and worktree.</summary>
    ResetIndex,

    /// <summary>Apply the selected lines of a committed diff to the working tree (line-level cherry-pick).</summary>
    Apply,

    /// <summary>Revert the selected lines of a committed diff out of the working tree.</summary>
    Revert,
}

/// <summary>A ready-to-run line patch: the patch bytes plus the exact git-apply invocation.</summary>
public sealed record LinePatchPlan(byte[] Patch, ArgumentString ApplyArguments);

/// <summary>
///  The portable half of FileViewer's line-patch dispatch: verb + selection offsets into the
///  rendered patch text → the right <see cref="PatchManager"/> call and the right git-apply
///  flags (--cached/--reverse/--3way), exactly as the WinForms viewer wires them. Tracked
///  files only - the new-file path (FilePreamble + blob id plumbing) stays host-side for now.
/// </summary>
public static class LinePatchPlanner
{
    public static LinePatchPlan? Plan(
        LinePatchVerb verb,
        string text,
        int selectionStart,
        int selectionLength,
        Encoding encoding,
        bool isNewFile = false,
        bool isRenamed = false)
    {
        byte[]? patch = verb switch
        {
            LinePatchVerb.Stage => PatchManager.GetSelectedLinesAsPatch(text, selectionStart, selectionLength, isIndex: false, encoding, reset: false, isNewFile, isRenamed),
            LinePatchVerb.Unstage => PatchManager.GetSelectedLinesAsPatch(text, selectionStart, selectionLength, isIndex: true, encoding, reset: false, isNewFile, isRenamed),
            LinePatchVerb.ResetIndex => PatchManager.GetSelectedLinesAsPatch(text, selectionStart, selectionLength, isIndex: true, encoding, reset: true, isNewFile, isRenamed),
            LinePatchVerb.Apply => PatchManager.GetSelectedLinesAsPatch(text, selectionStart, selectionLength, isIndex: false, encoding, reset: false, isNewFile, isRenamed),
            _ => PatchManager.GetResetWorkTreeLinesAsPatch(text, selectionStart, selectionLength, encoding),
        };

        return patch is not { Length: > 0 } ? null : new LinePatchPlan(patch, ApplyArguments(verb));
    }

    /// <summary>The WinForms flag sets verbatim: stage/unstage hit the index, resets reverse, committed lines go --3way.</summary>
    public static ArgumentString ApplyArguments(LinePatchVerb verb)
        => verb switch
        {
            LinePatchVerb.Stage => new GitArgumentBuilder("apply") { "--cached", "--index", "--whitespace=nowarn" },
            LinePatchVerb.Unstage => new GitArgumentBuilder("apply") { "--cached", "--index", "--whitespace=nowarn", "--reverse" },
            LinePatchVerb.ResetIndex => new GitArgumentBuilder("apply") { "--whitespace=nowarn", "--reverse --index" },
            LinePatchVerb.ResetWorkTree => new GitArgumentBuilder("apply") { "--whitespace=nowarn" },
            _ => new GitArgumentBuilder("apply") { "--3way", "--index", "--whitespace=nowarn" },
        };
}
