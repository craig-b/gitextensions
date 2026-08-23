using GitCommands;
using GitExtensions.Extensibility.Git;
using UICmd = GitExtensions.Extensibility.Git.UICommands;

namespace GitUI.CommandsDialogs;

public static class MergeConflictHandler
{
    public static bool HandleMergeConflicts(IGitUICommands commands, IWin32Window? owner, bool offerCommit = true, bool offerUpdateSubmodules = true)
    {
        if (commands.Module.InTheMiddleOfConflictedMerge())
        {
            if (AppSettings.DontConfirmResolveConflicts || MessageBoxes.ConfirmResolveMergeConflicts(owner))
            {
                SolveMergeConflicts(commands, owner, offerCommit);
            }

            return true;
        }

        if (offerUpdateSubmodules)
        {
            commands.Execute(new UICmd.UpdateSubmodules(), owner);
        }

        return false;
    }

    private static void SolveMergeConflicts(IGitUICommands commands, IWin32Window? owner, bool offerCommit)
    {
        if (commands.Module.InTheMiddleOfConflictedMerge())
        {
            commands.Execute(new UICmd.ResolveConflicts(offerCommit), owner);
        }

        if (commands.Module.InTheMiddleOfPatch())
        {
            if (MessageBoxes.MiddleOfPatchApply(owner))
            {
                commands.Execute(new UICmd.ApplyPatch(), owner);
            }
        }
        else if (commands.Module.InTheMiddleOfRebase())
        {
            if (MessageBoxes.MiddleOfRebase(owner))
            {
                commands.Execute(new UICmd.ContinueRebase(), owner);
            }
        }
    }
}
