using GitExtensions.Extensibility.Git;

namespace GitCommands.Reset;

/// <summary>The reset-current-branch dialog's decisions.</summary>
public static class ResetCurrentBranchPolicy
{
    /// <summary>Only a hard reset destroys work and needs confirming.</summary>
    public static bool RequiresConfirmation(ResetMode mode) => mode is ResetMode.Hard;

    /// <summary>Callers preselect Soft when the working directory is dirty (nothing gets lost), else Hard.</summary>
    public static ResetMode ResolveDefaultMode(bool isDirtyWorkingDir)
        => isDirtyWorkingDir ? ResetMode.Soft : ResetMode.Hard;

    /// <summary>Submodules update after a reset that moves HEAD in a repo that has them, when the setting asks for it.</summary>
    public static bool ShouldUpdateSubmodules(
        bool? updateSubmodulesOnCheckoutSetting,
        bool hasSubmodules,
        ObjectId targetCommit,
        ObjectId currentCheckout)
        => updateSubmodulesOnCheckoutSetting is true
            && hasSubmodules
            && targetCommit != currentCheckout;

    /// <summary>The manual anchor for each mode's help link.</summary>
    public static string HelpAnchor(ResetMode mode)
        => mode switch
        {
            ResetMode.Soft => "--soft",
            ResetMode.Mixed => "--mixed",
            ResetMode.Keep => "--keep",
            ResetMode.Merge => "--merge",
            ResetMode.Hard => "--hard",
            _ => "",
        };
}
