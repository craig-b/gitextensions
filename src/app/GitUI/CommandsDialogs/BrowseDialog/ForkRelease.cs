using System.Diagnostics.CodeAnalysis;
using System.Net;
using GitCommands;

namespace GitUI.CommandsDialogs.BrowseDialog;

internal enum UpdateState
{
    /// <summary>The latest release could not be determined.</summary>
    CheckFailed,

    /// <summary>The newest release is already installed.</summary>
    UpToDate,

    /// <summary>A newer release exists.</summary>
    UpdateAvailable,

    /// <summary>This copy was not installed from a release, so there is nothing to compare against.</summary>
    NotFromRelease
}

/// <summary>
/// Finds the newest release of the fork this build came from.
/// </summary>
/// <remarks>
/// Releases are resolved by following the redirect from <c>/releases/latest</c>, which is what
/// <c>eng/wine/install.sh</c> does. The GitHub API would be the obvious alternative and is the wrong
/// one: the check it replaced spent five unauthenticated calls against a limit of sixty an hour, and
/// when that limit was reached it left the dialog searching forever. A redirect costs one request and
/// cannot be throttled.
///
/// A tag is <c>wine-v&lt;version&gt;</c> and states the whole four-part version the release is stamped
/// with, which the release workflow checks against the upstream base rather than trusting. Comparing
/// versions rather than a bare counter means a release is ordered correctly after the upstream base
/// moves, so the build number can start again from one on a new base: 7.4.0.1 follows 7.3.0.9.
/// </remarks>
internal static class ForkRelease
{
    private const string DefaultRepo = "craig-b/gitextensions";
    private const string TagPrefix = "wine-v";

    private static readonly Lazy<HttpClient> _client = new(() =>
    {
        // Proxy handling as GitUI.Avatars.AvatarDownloader sets it up; redirects are followed by hand
        // because the redirect target is the answer.
        HttpClient client = new(new HttpClientHandler
        {
            UseProxy = true,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
            AllowAutoRedirect = false
        })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        return client;
    });

    /// <summary>The <c>owner/repository</c> the releases are taken from.</summary>
    public static string Repo
    {
        get
        {
            string? fromEnvironment = Environment.GetEnvironmentVariable("GITEXT_UPDATE_REPO");
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
            {
                return fromEnvironment;
            }

            string configured = AppSettings.UpdateRepository;
            return string.IsNullOrWhiteSpace(configured) ? DefaultRepo : configured;
        }
    }

    public static string ReleasesUrl => $"https://github.com/{Repo}/releases";

    public static string TagUrl(string tag) => $"{ReleasesUrl}/tag/{tag}";

    /// <summary>The tag of the newest release, or <see langword="null"/> if it could not be read.</summary>
    public static async Task<string?> GetLatestTagAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Head, $"{ReleasesUrl}/latest");
            using HttpResponseMessage response = await _client.Value.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            string? target = response.Headers.Location?.ToString();
            if (string.IsNullOrEmpty(target))
            {
                return null;
            }

            string tag = target[(target.LastIndexOf('/') + 1)..];
            return IsReleaseTag(tag) ? tag : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return null;
        }
    }

    public static bool IsReleaseTag(string? tag) => TryGetVersion(tag, out _);

    /// <summary>Reads the version out of a <c>wine-v&lt;version&gt;</c> tag.</summary>
    /// <remarks>
    /// All four components are required, which is also what rejects the scheme used before the tag
    /// stated the version: <c>wine-v7.2.1.7-2</c> is not a version and must not be read as one.
    /// </remarks>
    public static bool TryGetVersion(string? tag, [NotNullWhen(returnValue: true)] out Version? version)
    {
        version = null;
        return tag is not null
            && tag.StartsWith(TagPrefix, StringComparison.Ordinal)
            && Version.TryParse(tag[TagPrefix.Length..], out version)
            && version.Revision >= 0;
    }

    /// <summary>
    /// Compares the tag recorded by the installer with the newest released one.
    /// </summary>
    /// <param name="installedTag">
    /// The contents of the install's VERSION file, or <see langword="null"/> when there is none —
    /// a build overlaid by eng/wine/refresh.sh, or any copy not installed from a release.
    /// </param>
    public static UpdateState Decide(string? installedTag, string? latestTag)
    {
        if (!TryGetVersion(latestTag, out Version? latest))
        {
            return UpdateState.CheckFailed;
        }

        if (!TryGetVersion(installedTag, out Version? installed))
        {
            return UpdateState.NotFromRelease;
        }

        return latest > installed ? UpdateState.UpdateAvailable : UpdateState.UpToDate;
    }
}
