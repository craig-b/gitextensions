namespace GitCommands.Clone;

/// <summary>
///  The protocol-handler argument table: plain git/http(s) URLs clone as-is, and the GitHub
///  desktop-app "openRepo" schemes are stripped to their payload URL.
/// </summary>
public static class ProtocolHandlerUrl
{
    public static string? TryParseCloneUrl(string argument)
    {
        if (argument.StartsWith("git://") || argument.StartsWith("http://") || argument.StartsWith("https://"))
        {
            return argument;
        }

        if (argument.StartsWith("github-windows://openRepo/"))
        {
            return argument.Replace("github-windows://openRepo/", "");
        }

        if (argument.StartsWith("github-mac://openRepo/"))
        {
            return argument.Replace("github-mac://openRepo/", "");
        }

        return null;
    }
}
