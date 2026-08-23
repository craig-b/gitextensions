namespace GitCommands.Remotes;

/// <summary>The remote editor's field state.</summary>
public sealed record RemoteEditorFields(
    string Name,
    string Url,
    string PushUrl,
    bool SeparatePushUrl,
    string PuttySshKey,
    string? Color,
    bool IsNew,
    bool IsDisabled)
{
    public bool CanSave => Name.Trim().Length > 0;

    /// <summary>Edit mode from a remote (a separate push URL shows only when one is set); null = "new remote" mode.</summary>
    public static RemoteEditorFields FromRemote(ConfigFileRemote? remote)
        => remote is null
            ? new RemoteEditorFields("", "", "", SeparatePushUrl: false, "", Color: null, IsNew: true, IsDisabled: false)
            : new RemoteEditorFields(
                remote.Name ?? "",
                remote.Url ?? "",
                remote.PushUrl ?? "",
                SeparatePushUrl: !string.IsNullOrEmpty(remote.PushUrl),
                remote.PuttySshKey ?? "",
                string.IsNullOrWhiteSpace(remote.Color) ? null : remote.Color,
                IsNew: false,
                IsDisabled: remote.Disabled);
}

/// <summary>What actually gets saved after the dialog's normalization rules.</summary>
public sealed record RemoteSaveRequest(string Name, string Url, string? PushUrl, bool SeparatePushUrl)
{
    /// <summary>
    ///  The historical save-time rules: trim everything, and collapse the separate push URL
    ///  when it is empty or equals the fetch URL (case-insensitive). The push URL is null
    ///  unless the separate-URL choice survives.
    /// </summary>
    public static RemoteSaveRequest Normalize(string name, string url, string pushUrl, bool separatePushUrl)
    {
        name = name.Trim();
        url = url.Trim();
        pushUrl = pushUrl.Trim();

        if ((string.IsNullOrEmpty(pushUrl) && separatePushUrl)
            || (!string.IsNullOrEmpty(pushUrl) && pushUrl.Equals(url, StringComparison.OrdinalIgnoreCase)))
        {
            separatePushUrl = false;
        }

        return new RemoteSaveRequest(name, url, separatePushUrl ? pushUrl : null, separatePushUrl);
    }
}

public enum RemoteNameConflict
{
    None,
    EnabledExists,
    DisabledExists,
}

public static class RemoteNameValidator
{
    /// <summary>The duplicate check runs only for new remotes; renames rely on git's own error.</summary>
    public static RemoteNameConflict Check(string name, bool isNew, Func<string, bool> enabledExists, Func<string, bool> disabledExists)
    {
        if (!isNew)
        {
            return RemoteNameConflict.None;
        }

        if (enabledExists(name))
        {
            return RemoteNameConflict.EnabledExists;
        }

        return disabledExists(name) ? RemoteNameConflict.DisabledExists : RemoteNameConflict.None;
    }
}

/// <summary>How the dialog reacts to a save result.</summary>
public sealed record RemoteSaveReaction(
    bool ShowMessage,
    string? Message,
    bool UpdateUrlHistory,
    bool UpdatePushUrlHistory,
    bool OfferConfigureAndFetch)
{
    /// <summary>
    ///  A non-empty user message stops everything else (it doubles as git's error channel);
    ///  otherwise the URL history updates (push URL only with a separate one), and the
    ///  configure-and-fetch prompt requires both the manager's flag and a non-empty URL.
    /// </summary>
    public static RemoteSaveReaction Evaluate(ConfigFileRemoteSaveResult result, string remoteUrl, bool separatePushUrl)
    {
        if (!string.IsNullOrEmpty(result.UserMessage))
        {
            return new RemoteSaveReaction(
                ShowMessage: true,
                result.UserMessage,
                UpdateUrlHistory: false,
                UpdatePushUrlHistory: false,
                OfferConfigureAndFetch: false);
        }

        return new RemoteSaveReaction(
            ShowMessage: false,
            Message: null,
            UpdateUrlHistory: true,
            UpdatePushUrlHistory: separatePushUrl,
            OfferConfigureAndFetch: result.ShouldUpdateRemote && !string.IsNullOrEmpty(remoteUrl));
    }
}

/// <summary>Which list row the dialog selects on (re)load, and what the empty state disables.</summary>
public sealed record RemoteListSelection(int SelectedIndex, bool DeleteEnabled, bool ToggleEnabled, bool FocusNameBox)
{
    /// <summary>
    ///  The preselected remote by name when present, else the first enabled remote, else the
    ///  first disabled one; an empty list disables delete/toggle and focuses the name box.
    /// </summary>
    public static RemoteListSelection Resolve(IReadOnlyList<ConfigFileRemote> remotes, string? preselectRemote)
    {
        if (remotes.Count == 0)
        {
            return new RemoteListSelection(SelectedIndex: -1, DeleteEnabled: false, ToggleEnabled: false, FocusNameBox: true);
        }

        int index = -1;
        if (!string.IsNullOrEmpty(preselectRemote))
        {
            index = IndexOf(remote => remote.Name == preselectRemote);
        }

        if (index < 0)
        {
            index = IndexOf(remote => !remote.Disabled);
        }

        if (index < 0)
        {
            index = 0;
        }

        return new RemoteListSelection(index, DeleteEnabled: true, ToggleEnabled: true, FocusNameBox: false);

        int IndexOf(Func<ConfigFileRemote, bool> predicate)
        {
            for (int i = 0; i < remotes.Count; i++)
            {
                if (predicate(remotes[i]))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}

public static class RemoteUrlSuggestions
{
    /// <summary>The dialog's built-in generic names, merged with the user's custom list.</summary>
    public static IReadOnlyCollection<string> GenericRemoteNames(IEnumerable<string> customGenericNames)
        => ["origin", "upstream", "fork", "remote", "internal", .. customGenericNames];

    /// <summary>
    ///  URL candidates for a new remote, derived from the existing remotes: the typed name
    ///  spliced over each remote's own name and over the git-hosting owner segment. A blank
    ///  or generic typed name yields placeholder candidates that must not autofill.
    /// </summary>
    public static (IReadOnlyList<string> Candidates, bool FillEmptyUrl) GenerateCandidates(
        IReadOnlyList<ConfigFileRemote> remotes,
        Func<ConfigFileRemote, string?> urlGetter,
        string typedName,
        IReadOnlyCollection<string> genericNames)
    {
        bool fillEmptyUrl = true;
        string remoteName = typedName;
        if (string.IsNullOrWhiteSpace(typedName) || genericNames.Contains(typedName))
        {
            remoteName = "TO_REPLACE";
            fillEmptyUrl = false;
        }

        HashSet<string> candidates = [];
        GitHostingRemoteParser gitHostingRemoteParser = new();
        foreach (ConfigFileRemote remote in remotes)
        {
            string? url = urlGetter(remote);
            if (string.IsNullOrEmpty(url))
            {
                continue;
            }

            // Simple replace tentative
            if (remote.Name is not null && url.Contains(remote.Name))
            {
                candidates.Add(url.Replace($"{remote.Name}/", $"{remoteName}/"));
            }

            // Extract from "known" git hosting pattern
            if (gitHostingRemoteParser.TryExtractGitHostingDataFromRemoteUrl(remote.Url!, out _, out string? owner, out _))
            {
                candidates.Add(url.Replace($"{owner}/", $"{remoteName}/"));
            }
        }

        return ([.. candidates], fillEmptyUrl);
    }

    /// <summary>A remote-name guess from a hosting URL's owner segment.</summary>
    public static string? TryInferNameFromUrl(string url)
        => new GitHostingRemoteParser().TryExtractGitHostingDataFromRemoteUrl(url, out _, out string? owner, out _)
            ? owner
            : null;
}
