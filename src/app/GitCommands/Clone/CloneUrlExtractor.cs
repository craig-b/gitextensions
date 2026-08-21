namespace GitCommands.Clone;

public static class CloneUrlExtractor
{
    /// <summary>
    ///  Check whether the given string contains one or more valid git URLs and extract the first
    ///  URL that exists, if any (e.g. from a pasted "git clone &lt;url&gt;" command line).
    /// </summary>
    /// <remarks>
    ///  <see cref="PathUtil.CanBeGitURL"/> is used as the standard way to detect a git URL. If
    ///  <paramref name="contents"/> contains more than one URL, subsequent URLs are not extracted.
    /// </remarks>
    public static bool TryExtractUrl(string? contents, out string url)
    {
        url = "";

        if (string.IsNullOrEmpty(contents))
        {
            return false;
        }

        string[] parts = contents.Split(' ');
        foreach (string part in parts)
        {
            if (PathUtil.CanBeGitURL(part))
            {
                url = part;
                break;
            }
        }

        return !string.IsNullOrEmpty(url);
    }
}
