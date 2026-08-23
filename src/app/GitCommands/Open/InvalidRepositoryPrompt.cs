namespace GitCommands.Open;

/// <summary>
///  What the "this directory is not a valid repository" dialog may offer:
///  removing the selected entry is always available; removing all invalid entries is offered
///  only when more than one recent entry is invalid.
/// </summary>
public readonly record struct InvalidRepositoryPromptOptions(int InvalidCount)
{
    public bool OfferRemoveAll => InvalidCount > 1;

    public static InvalidRepositoryPromptOptions Evaluate(IEnumerable<string> recentRepositoryPaths, Func<string, bool> isValidGitWorkingDir)
        => new(recentRepositoryPaths.Count(path => !isValidGitWorkingDir(path)));
}
