using GitCommands.Git;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitUI.CommandsDialogs;

namespace GitCommands.Rewrite;

public enum RewriteTodoAction
{
    Edit,
    Reword,
}

/// <summary>
///  The history-rewrite verbs: edit/reword run an interactive rebase onto the
///  commit's parent with a sequence editor that flips the first "pick"; fixup/squash/amend
///  commits carry the autosquash prefixes and are folded in by an autosquash rebase whose
///  todo is accepted verbatim.
/// </summary>
public static class HistoryRewrite
{
    public const string SequenceEditorVariable = "GIT_SEQUENCE_EDITOR";
    public const string EditorVariable = "GIT_EDITOR";

    /// <summary>Accepts the generated todo unchanged (the autosquash fold needs no interaction).</summary>
    public const string AcceptTodoEditor = "true";

    /// <summary>The grid's historical sed program: rewrite the first "pick" to the chosen action.</summary>
    public static string ReplaceFirstPickEditor(RewriteTodoAction action)
        => string.Format("sed -i -re '0,/pick/s//{0}/'", action is RewriteTodoAction.Edit ? "e" : "r");

    /// <summary>"fixup! subject" / "squash! subject" / "amend! subject".</summary>
    public static string PrefixedSubject(CommitKind kind, string subject) => $"{kind.GetPrefix()} {subject}";

    /// <summary>
    ///  The interactive rebase onto the commit's actual first parent (null for a root
    ///  commit rebases the whole history), autostash on - the grid's LaunchRebase shape.
    /// </summary>
    public static ArgumentString InteractiveRebaseOntoParent(ObjectId? firstParentId, bool supportRebaseMerges, bool autoSquash = false)
        => Commands.Rebase(new Commands.RebaseOptions
        {
            BranchName = firstParentId is { IsZero: false } parentId ? parentId.ToString() : null,
            Interactive = true,
            AutoSquash = autoSquash,
            AutoStash = true,
            SupportRebaseMerges = supportRebaseMerges,
        });
}
