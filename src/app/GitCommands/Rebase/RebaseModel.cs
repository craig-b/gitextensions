using System.Text.RegularExpressions;

namespace GitCommands.Rebase;

/// <summary>
///  The rebase target: a specific from..to range rebased onto a base, or a plain rebase of
///  the chosen branch. An incomplete range silently degrades to the plain form - the
///  dialog's historical rule, now explicit.
/// </summary>
public sealed record RebaseTarget(string? OnTo, string? From, string BranchName)
{
    public static RebaseTarget Resolve(string rebaseOnText, bool specificRange, string fromText, string toText)
        => specificRange && !string.IsNullOrWhiteSpace(fromText) && !string.IsNullOrWhiteSpace(toText)
            ? new RebaseTarget(OnTo: rebaseOnText, From: fromText, BranchName: toText)
            : new RebaseTarget(OnTo: null, From: null, BranchName: rebaseOnText);
}

/// <summary>What the rebase dialog's options allow, given each other and the repo.</summary>
public readonly record struct RebaseOptionAvailability(
    bool InteractiveAllowed,
    bool PreserveMergesAllowed,
    bool AutoSquashAllowed,
    bool IgnoreDateAllowed,
    bool CommitterDateAllowed,
    bool AutoStashAllowed,
    bool UpdateRefsVisible)
{
    public static RebaseOptionAvailability Evaluate(
        bool interactive,
        bool ignoreDate,
        bool committerDateIsAuthorDate,
        bool isDirtyWorkingDir,
        bool supportsUpdateRefs)
    {
        bool anyDateOption = ignoreDate || committerDateIsAuthorDate;
        return new RebaseOptionAvailability(
            InteractiveAllowed: !anyDateOption,
            PreserveMergesAllowed: !anyDateOption,
            AutoSquashAllowed: interactive && !anyDateOption,
            IgnoreDateAllowed: !committerDateIsAuthorDate,
            CommitterDateAllowed: !ignoreDate,
            AutoStashAllowed: isDirtyWorkingDir,
            UpdateRefsVisible: supportsUpdateRefs);
    }
}

public static class RebasePreflight
{
    /// <summary>
    ///  --update-refs/--no-update-refs is passed only when the choice differs from the
    ///  effective rebase.updaterefs config; otherwise git decides.
    /// </summary>
    public static bool? ResolveUpdateRefsChoice(bool supported, bool? configuredValue, bool checkboxValue)
        => supported && configuredValue != checkboxValue ? checkboxValue : null;

    /// <summary>The dialog closes only once nothing is in flight.</summary>
    public static bool ShouldCloseAfterAction(bool inTheMiddleOfAction, bool inTheMiddleOfPatch)
        => !inTheMiddleOfAction && !inTheMiddleOfPatch;
}

/// <summary>Recognizes rebase process output the dialog reacts to.</summary>
public static partial class RebaseOutputAnalyzer
{
    // The dialog historically compared against the literal "Current branch a is up to date."
    // - i.e. it only ever matched a branch actually named "a". Matching any branch name is
    // a deliberate fix.
    [GeneratedRegex(@"^Current branch .+ is up to date\.$", RegexOptions.ExplicitCapture)]
    private static partial Regex UpToDateRegex { get; }

    public static bool IsBranchUpToDate(string processOutput) => UpToDateRegex.IsMatch(processOutput.Trim());
}
