using System.Diagnostics;
using GitCommands.Submodules;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtUtils;

namespace GitCommands.LeftPanel;

/// <summary>A node of the left panel's submodule hierarchy: the top project, a submodule, or a grouping folder.</summary>
public sealed class SubmoduleTreeNode
{
    /// <summary>The display name: the submodule name, or the (possibly compacted "a/b/c") folder name.</summary>
    public required string Name { get; set; }

    public bool IsFolder { get; init; }

    /// <summary>The submodule info; null on folders.</summary>
    public SubmoduleInfo? Info { get; init; }

    public bool IsCurrent { get; init; }

    public IReadOnlyList<GitItemStatus>? GitStatus { get; init; }

    public string LocalPath { get; init; } = "";

    public string SuperPath { get; init; } = "";

    /// <summary>The trailing " [branch]" decoration parsed from the info text; empty when absent.</summary>
    public string BranchText { get; init; } = "";

    public List<SubmoduleTreeNode> Children { get; set; } = [];
}

public sealed class SubmoduleTreeBuildResult
{
    /// <summary>The single root: the top project with everything beneath it.</summary>
    public required SubmoduleTreeNode Root { get; init; }

    /// <summary>Submodules whose super-project directory is missing on disk; views surface these.</summary>
    public required IReadOnlyList<(string? SuperPath, string SubmodulePath, string SubmoduleText)> SkippedMissingSuperPaths { get; init; }
}

/// <summary>
///  Builds the submodule hierarchy the left panel shows: everything roots
///  from the top project, each submodule's super project is the nearest containing module path,
///  intermediate directory segments become folders, and chains of single-child folders compact
///  into one "a/b/c" folder.
/// </summary>
public static class SubmoduleTreeBuilder
{
    public static SubmoduleTreeBuildResult Build(SubmoduleInfoResult result)
    {
        IGitModule threadModule = result.Module ?? throw new ArgumentException("The result must carry its module.", nameof(result));
        SubmoduleInfo topProject = result.TopProject ?? throw new ArgumentException("The result must carry the top project.", nameof(result));

        // result.AllSubmodules is a recursive list of submodules without super-project info;
        // deduce it by substring matching against an ordered list of all module paths.
        List<string> modulePaths = [.. result.AllSubmodules.Select(info => info.Path)];

        IGitModule? parentModule = threadModule;
        while (parentModule is not null)
        {
            modulePaths.Add(parentModule.WorkingDir);
            parentModule = parentModule.SuperprojectModule;
        }

        // Sort descending so we find the nearest outer folder first
        modulePaths = [.. modulePaths.OrderByDescending(path => path)];

        List<SubmoduleTreeNode> submoduleNodes = [];
        List<(string? SuperPath, string SubmodulePath, string SubmoduleText)> skipped = [];

        foreach (SubmoduleInfo submoduleInfo in result.AllSubmodules)
        {
            string? superPath = modulePaths.Find(path => submoduleInfo.Path != path && submoduleInfo.Path.Contains(path));

            if (!Directory.Exists(superPath))
            {
                skipped.Add((superPath, submoduleInfo.Path, submoduleInfo.Text));
                continue;
            }

            string? localPath = Path.GetDirectoryName(submoduleInfo.Path[superPath.Length..]).ToPosixPath();
            bool isCurrent = submoduleInfo.Bold;

            submoduleNodes.Add(CreateSubmoduleNode(
                submoduleInfo,
                isCurrent,
                isCurrent ? result.CurrentSubmoduleStatus : null,
                localPath!,
                superPath));
        }

        IGitModule topModule = threadModule.GetTopModule();

        // Build a mapping of top-module-relative path to node,
        // then create the missing folder nodes for intermediate directory segments.
        Dictionary<string, SubmoduleTreeNode> pathToNodes = [];
        foreach (SubmoduleTreeNode node in submoduleNodes)
        {
            pathToNodes[GetNodeRelativePath(topModule, node)] = node;
        }

        foreach (SubmoduleTreeNode node in submoduleNodes)
        {
            string[] parts = GetNodeRelativePath(topModule, node).Split(Delimiters.ForwardSlash);

            for (int i = 0; i < parts.Length - 1; ++i)
            {
                string path = string.Join("/", parts.Take(i + 1));

                if (!pathToNodes.ContainsKey(path))
                {
                    pathToNodes[path] = new SubmoduleTreeNode { Name = parts[i], IsFolder = true };
                }
            }
        }

        SubmoduleTreeNode topModuleNode = CreateSubmoduleNode(
            topProject,
            topProject.Bold,
            topProject.Bold ? result.CurrentSubmoduleStatus : null,
            "",
            topProject.Path);

        HashSet<SubmoduleTreeNode> nodesInTree = [];
        foreach (SubmoduleTreeNode node in submoduleNodes)
        {
            SubmoduleTreeNode parentNode = topModuleNode;
            string[] parts = GetNodeRelativePath(topModule, node).Split(Delimiters.ForwardSlash);

            for (int i = 0; i < parts.Length; ++i)
            {
                string path = string.Join("/", parts.Take(i + 1));
                SubmoduleTreeNode nodeToAdd = pathToNodes[path];

                if (nodesInTree.Add(nodeToAdd))
                {
                    parentNode.Children.Add(nodeToAdd);
                }

                parentNode = nodeToAdd;
            }
        }

        CompactSingleChildFolderChains(topModuleNode.Children);

        return new SubmoduleTreeBuildResult { Root = topModuleNode, SkippedMissingSuperPaths = skipped };
    }

    private static SubmoduleTreeNode CreateSubmoduleNode(
        SubmoduleInfo info, bool isCurrent, IReadOnlyList<GitItemStatus>? gitStatus, string localPath, string superPath)
    {
        // Extract submodule name and branch
        // e.g. info.Text = "Externals/conemu-inside [no branch]"
        // Note that the branch portion won't be there if the user hasn't yet init'd + updated the submodule.
        string[] pathAndBranch = info.Text.Split(Delimiters.Space, 2);
        Trace.Assert(pathAndBranch.Length >= 1);

        return new SubmoduleTreeNode
        {
            Name = pathAndBranch[0].SubstringAfterLast('/'),
            Info = info,
            IsCurrent = isCurrent,
            GitStatus = gitStatus,
            LocalPath = localPath,
            SuperPath = superPath,
            BranchText = pathAndBranch.Length == 2 ? " " + pathAndBranch[1] : "",
        };
    }

    private static string GetNodeRelativePath(IGitModule topModule, SubmoduleTreeNode node)
        => node.SuperPath.SubstringAfter(topModule.WorkingDir).ToPosixPath() + node.LocalPath;

    /// <summary>
    ///  Compacts chains of single-child folder nodes by merging their names with "/" separators:
    ///  "extension" → "src" → "assets" becomes one folder named "extension/src/assets".
    /// </summary>
    internal static void CompactSingleChildFolderChains(List<SubmoduleTreeNode> nodes)
    {
        foreach (SubmoduleTreeNode node in nodes)
        {
            if (node.IsFolder)
            {
                while (node.Children is [{ IsFolder: true } childFolder])
                {
                    node.Name += "/" + childFolder.Name;
                    node.Children = childFolder.Children;
                }
            }

            CompactSingleChildFolderChains(node.Children);
        }
    }
}
