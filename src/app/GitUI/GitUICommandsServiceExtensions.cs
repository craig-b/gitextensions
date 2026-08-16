using GitExtensions.Extensibility.Git;
using GitExtUtils;

namespace GitUI;

/// <summary>
///  Host-side service access for <see cref="IGitUICommands"/>.
/// </summary>
/// <remarks>
///  M3.3 removed <see cref="IServiceProvider"/> from the public plugin contract; the host still
///  resolves its own services through the concrete <see cref="GitUICommands"/>. This internal
///  extension keeps host call sites unchanged without re-advertising the container to plugins.
/// </remarks>
internal static class GitUICommandsServiceExtensions
{
    public static T GetRequiredService<T>(this IGitUICommands commands) where T : notnull
        => ((IServiceProvider)commands).GetRequiredService<T>();
}
