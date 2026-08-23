using GitExtensions.Extensibility.Git;

namespace GitCommands.Settings.Pages;

/// <summary>
///  Presentation model for the general settings page. Carries the page's storage quirks:
///  the pull-action choice list (with the stored <see cref="GitPullAction.Default"/>
///  normalizing to "open pull dialog"), the commits-limit gate over a 0-means-unlimited
///  int, and the submodule-status value being conditional on either git-status option.
/// </summary>
public sealed class GeneralPageModel : SettingsPageModel
{
    /// <summary>The canonical pull-action order; every view presents the choice in this order.</summary>
    public static IReadOnlyList<GitPullAction> PullActionValues { get; } =
    [
        GitPullAction.None,
        GitPullAction.Merge,
        GitPullAction.Rebase,
        GitPullAction.Fetch,
        GitPullAction.FetchAll,
        GitPullAction.FetchPruneAll,
    ];

    private static readonly string[] _pullActionCaptions =
    [
        "Open pull dialog",
        "Pull - merge",
        "Pull - rebase",
        "Fetch",
        "Fetch all",
        "Fetch and prune all",
    ];

    public GeneralPageModel()
        : base("General")
    {
        Groups =
        [
            new SettingsGroup("Performance",
                ShowGitStatusInToolbar = new BoolSettingsEntry("Show number of changed files on commit button",
                    () => AppSettings.ShowGitStatusInBrowseToolbar, value => AppSettings.ShowGitStatusInBrowseToolbar = value),
                ShowGitStatusForArtificialCommits = new BoolSettingsEntry("Show number of changed files for artificial commits",
                    () => AppSettings.ShowGitStatusForArtificialCommits, value => AppSettings.ShowGitStatusForArtificialCommits = value),
                ShowSubmoduleStatus = new BoolSettingsEntry("Show submodule status in browse menu",
                    () => AppSettings.ShowSubmoduleStatus,
                    value => AppSettings.ShowSubmoduleStatus =
                        value && (ShowGitStatusInToolbar.Value || ShowGitStatusForArtificialCommits.Value)),
                ShowStashCount = new BoolSettingsEntry("Show stash count on status bar in browse window",
                    () => AppSettings.ShowStashCount, value => AppSettings.ShowStashCount = value),
                ShowAheadBehindData = new BoolSettingsEntry("Show ahead and behind information on status bar in browse window",
                    () => AppSettings.ShowAheadBehindData, value => AppSettings.ShowAheadBehindData = value),
                CheckForUncommittedChanges = new BoolSettingsEntry("Check for uncommitted changes in checkout branch dialog",
                    () => AppSettings.CheckForUncommittedChangesInCheckoutBranch, value => AppSettings.CheckForUncommittedChangesInCheckoutBranch = value),
                CommitsLimit = new OptionalNumberSettingsEntry("Limit number of commits to be loaded",
                    minimum: 0, maximum: 1_000_000, increment: 10_000,
                    () => (AppSettings.MaxRevisionGraphCommits != 0, AppSettings.MaxRevisionGraphCommits),
                    (enabled, number) => AppSettings.MaxRevisionGraphCommits = enabled ? number : 0)),

            new SettingsGroup("Behaviour",
                CloseProcessDialog = new BoolSettingsEntry("Close Process dialog when process succeeds",
                    () => AppSettings.CloseProcessDialog, value => AppSettings.CloseProcessDialog = value),
                ShowGitCommandLine = new BoolSettingsEntry("Show console window when executing git process",
                    () => AppSettings.ShowGitCommandLine, value => AppSettings.ShowGitCommandLine = value),
                UseHistogramDiffAlgorithm = new BoolSettingsEntry("Use histogram diff algorithm",
                    () => AppSettings.UseHistogramDiffAlgorithm, value => AppSettings.UseHistogramDiffAlgorithm = value),
                IncludeUntrackedFilesInAutoStash = new BoolSettingsEntry("Include untracked files in autostash",
                    () => AppSettings.IncludeUntrackedFilesInAutoStash, value => AppSettings.IncludeUntrackedFilesInAutoStash = value),
                UpdateSubmodulesOnCheckout = new TriStateSettingsEntry("Update submodules on checkout",
                    () => AppSettings.UpdateSubmodulesOnCheckout, value => AppSettings.UpdateSubmodulesOnCheckout = value),
                FollowRenamesInFileHistory = new BoolSettingsEntry("Follow renames in file history",
                    () => AppSettings.FollowRenamesInFileHistory, value => AppSettings.FollowRenamesInFileHistory = value),
                FollowRenamesExactOnly = new BoolSettingsEntry("Follow exact renames and copies only",
                    () => AppSettings.FollowRenamesInFileHistoryExactOnly, value => AppSettings.FollowRenamesInFileHistoryExactOnly = value),
                StartWithRecentWorkingDir = new BoolSettingsEntry("Open last working directory on startup",
                    () => AppSettings.StartWithRecentWorkingDir, value => AppSettings.StartWithRecentWorkingDir = value),
                DefaultCloneDestination = new StringSettingsEntry("Default clone destination",
                    () => AppSettings.DefaultCloneDestinationPath, value => AppSettings.DefaultCloneDestinationPath = value),
                DefaultPullAction = new ChoiceSettingsEntry("Default pull action", _pullActionCaptions,
                    () =>
                    {
                        GitPullAction stored = AppSettings.DefaultPullAction;
                        if (stored == GitPullAction.Default)
                        {
                            stored = GitPullAction.None;
                        }

                        int index = 0;
                        for (int i = 0; i < PullActionValues.Count; i++)
                        {
                            if (PullActionValues[i] == stored)
                            {
                                index = i;
                                break;
                            }
                        }

                        return index;
                    },
                    index => AppSettings.DefaultPullAction = PullActionValues[index]),
                QuickSearchTimeout = new NumberSettingsEntry("Revision grid quick search timeout [ms]",
                    minimum: 100, maximum: 1_000_000, increment: 100,
                    () => AppSettings.RevisionGridQuickSearchTimeout, value => AppSettings.RevisionGridQuickSearchTimeout = value)),

            new SettingsGroup("Telemetry",
                Telemetry = new BoolSettingsEntry("Yes, I allow telemetry!",
                    () => AppSettings.TelemetryEnabled ?? false, value => AppSettings.TelemetryEnabled = value)),
        ];
    }

    public BoolSettingsEntry ShowGitStatusInToolbar { get; }
    public BoolSettingsEntry ShowGitStatusForArtificialCommits { get; }
    public BoolSettingsEntry ShowSubmoduleStatus { get; }
    public BoolSettingsEntry ShowStashCount { get; }
    public BoolSettingsEntry ShowAheadBehindData { get; }
    public BoolSettingsEntry CheckForUncommittedChanges { get; }
    public OptionalNumberSettingsEntry CommitsLimit { get; }

    public BoolSettingsEntry CloseProcessDialog { get; }
    public BoolSettingsEntry ShowGitCommandLine { get; }
    public BoolSettingsEntry UseHistogramDiffAlgorithm { get; }
    public BoolSettingsEntry IncludeUntrackedFilesInAutoStash { get; }
    public TriStateSettingsEntry UpdateSubmodulesOnCheckout { get; }
    public BoolSettingsEntry FollowRenamesInFileHistory { get; }
    public BoolSettingsEntry FollowRenamesExactOnly { get; }
    public BoolSettingsEntry StartWithRecentWorkingDir { get; }
    public StringSettingsEntry DefaultCloneDestination { get; }
    public ChoiceSettingsEntry DefaultPullAction { get; }
    public NumberSettingsEntry QuickSearchTimeout { get; }

    public BoolSettingsEntry Telemetry { get; }

    public override IReadOnlyList<SettingsGroup> Groups { get; }
}
