using System.Text;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Configurations;
using GitExtensions.Extensibility.Git;
using GitExtUtils;
using Microsoft;

namespace GitCommands.Commit;

/// <summary>
///  Builds the "list of changes in submodules" commit message the commit dialog offers when
///  submodule updates are staged (extracted from FormCommit): a summary
///  line naming the updated submodules, then per submodule the log between the staged
///  from/to revisions (or the bare revision change when the range yields no commits).
/// </summary>
public static class SubmoduleUpdateMessageBuilder
{
    /// <param name="createSubmoduleModule">Creates an <see cref="IGitModule"/> for a submodule's resolved working directory.</param>
    /// <returns>The generated message, or null when no staged item is a registered submodule.</returns>
    public static string? Build(
        IGitModule module,
        ISubmodulesConfigFile configFile,
        IFullPathResolver fullPathResolver,
        Func<string, IGitModule> createSubmoduleModule,
        IEnumerable<GitItemStatus> stagedFiles)
    {
        Dictionary<string, string> modules = stagedFiles
            .Where(item => item.IsSubmodule
                           && Directory.Exists(fullPathResolver.Resolve(item.Name))
                           && configFile.ConfigSections.FirstOrDefault(section => section.GetValue("path").Trim() == item.Name)?.SubSection is not null)
            .Select(item => item.Name)
            .ToDictionary(localPath =>
            {
                IConfigSection? submodule = configFile.ConfigSections.FirstOrDefault(section => section.GetValue("path").Trim() == localPath);
                Validates.NotNull(submodule?.SubSection);
                return submodule.SubSection.Trim();
            });

        if (modules.Count == 0)
        {
            return null;
        }

        StringBuilder sb = new();
        sb.AppendLine("Submodule" + (modules.Count == 1 ? " " : "s ") +
            string.Join(", ", modules.Keys) + " updated");
        sb.AppendLine();

        foreach ((string path, string name) in modules)
        {
            GitArgumentBuilder args = new("diff")
            {
                "--no-ext-diff",
                "--cached",
                "-z",
                "--",
                name.QuoteNE()
            };
            string diff = module.GitExecutable.GetOutput(args);
            string[] lines = diff.Split(Delimiters.LineFeed, StringSplitOptions.RemoveEmptyEntries);
            const string subprojectCommit = "Subproject commit ";
            string from = lines.Single(s => s.StartsWith("-" + subprojectCommit))[(subprojectCommit.Length + 1)..];
            string to = lines.Single(s => s.StartsWith("+" + subprojectCommit))[(subprojectCommit.Length + 1)..];
            if (!string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to))
            {
                sb.AppendLine("Submodule " + path + ":");
                IGitModule submoduleModule = createSubmoduleModule(fullPathResolver.Resolve(name.EnsureTrailingPathSeparator())!);
                args = new GitArgumentBuilder("log")
                {
                    "--pretty=format:\"    %m %h - %s\"",
                    "--no-merges",
                    $"{from}...{to}".Quote()
                };

                string log = submoduleModule.GitExecutable.GetOutput(args);

                if (log.Length != 0)
                {
                    sb.AppendLine(log);
                }
                else
                {
                    sb.AppendLine("    * Revision changed to " + to[..7]);
                }

                sb.AppendLine();
            }
        }

        return sb.ToString().TrimEnd();
    }
}
