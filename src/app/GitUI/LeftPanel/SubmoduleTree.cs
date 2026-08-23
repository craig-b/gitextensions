using System.Diagnostics;
using GitCommands;
using GitCommands.LeftPanel;
using GitCommands.Submodules;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtUtils;
using GitUI.CommandsDialogs;
using Microsoft;
using Microsoft.VisualStudio.Threading;
using UICmd = GitExtensions.Extensibility.Git.UICommands;

namespace GitUI.LeftPanel;

internal sealed class SubmoduleTree : Tree
{
    private readonly ISubmoduleStatusProvider _submoduleStatusProvider;
    private SubmoduleStatusEventArgs? _currentSubmoduleInfo;
    private Nodes? _currentNodes = null;

    public SubmoduleTree(TreeNode treeNode, IGitUICommandsSource commandsSource)
        : base(treeNode, commandsSource)
    {
        _submoduleStatusProvider = UICommands.GetRequiredService<ISubmoduleStatusProvider>();
        _submoduleStatusProvider.StatusUpdating += Provider_StatusUpdating;
        _submoduleStatusProvider.StatusUpdated += Provider_StatusUpdated;
    }

    public override void Dispose()
    {
        base.Dispose();

        _submoduleStatusProvider.StatusUpdating -= Provider_StatusUpdating;
        _submoduleStatusProvider.StatusUpdated -= Provider_StatusUpdated;
    }

    private void Provider_StatusUpdating(object? sender, EventArgs e)
    {
        _currentNodes = null;
    }

    private void Provider_StatusUpdated(object? sender, SubmoduleStatusEventArgs e)
    {
        _currentSubmoduleInfo = e;

        if (IsAttached)
        {
            OnStatusUpdated(e);
        }
    }

    private void OnStatusUpdated(SubmoduleStatusEventArgs e)
    {
        TreeViewNode.TreeView!.InvokeAndForget(async () =>
        {
            CancellationTokenSource? cts = null;
            Task<Nodes>? loadNodesTask = null;

            if (e.StructureUpdated)
            {
                _currentNodes = null;
            }

            if (_currentNodes is not null)
            {
                // Structure is up-to-date, update status
                Dictionary<string, SubmoduleInfo> infos = e.Info.AllSubmodules.ToDictionary(info => info.Path, info => info);
                Validates.NotNull(e.Info.TopProject);
                infos[e.Info.TopProject.Path] = e.Info.TopProject;
                List<SubmoduleNode> nodes = [.. _currentNodes.DepthEnumerator<SubmoduleNode>()];

                foreach (SubmoduleNode node in nodes)
                {
                    if (infos.TryGetValue(node.Info.Path, out SubmoduleInfo? info))
                    {
                        node.Info = info;
                        infos.Remove(node.Info.Path);
                    }
                    else
                    {
                        // structure no longer matching
                        DebugHelpers.Assert(true, $"Status info with {1 + e.Info.AllSubmodules.Count} records do not match current nodes ({nodes.Count})");
                        _currentNodes = null;
                        break;
                    }
                }

                if (infos.Count > 0)
                {
                    // This normally occurs with illegal paths
                    Trace.WriteLine($"{infos.Count} status info records remains after matching current nodes, structure seem to mismatch ({nodes.Count}/{e.Info.AllSubmodules.Count}: {string.Join(",", infos.Keys.ToList())})");

                    _currentNodes = null;
                }
            }

            if (_currentNodes is null)
            {
                // Load the nodes in the tree
                // Module.GetRefs() is not used for submodules
                JoinableTask joinableTask = ReloadNodesDetached((_, token) =>
                    {
                        cts = CancellationTokenSource.CreateLinkedTokenSource(e.Token, token);
                        loadNodesTask = LoadNodesAsync(e.Info, cts.Token);
                        return loadNodesTask;
                    },
                    getRefs: null!);
                await joinableTask.JoinAsync(e.Token);
            }

            if (cts is not null && loadNodesTask is not null)
            {
                _currentNodes = await loadNodesTask;
            }

            if (_currentNodes is not null)
            {
                CancellationToken token = cts?.Token ?? e.Token;
                try
                {
                    await LoadNodeDetailsAsync(_currentNodes, token).ConfigureAwaitRunInline();
                    LoadNodeToolTips(_currentNodes, token);
                }
                catch (Exception) when (token.IsCancellationRequested)
                {
                }
            }

            Interlocked.CompareExchange(ref _currentSubmoduleInfo, null, e);
        });
    }

    private async Task<Nodes> LoadNodesAsync(SubmoduleInfoResult info, CancellationToken token)
    {
        await TaskScheduler.Default;
        token.ThrowIfCancellationRequested();

        return FillSubmoduleTree(info);
    }

    private async Task LoadNodeDetailsAsync(Nodes loadedNodes, CancellationToken token)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);

        if (TreeViewNode.TreeView is not null)
        {
            TreeViewNode.TreeView.BeginUpdate();

            try
            {
                loadedNodes.DepthEnumerator<SubmoduleNode>().ForEach(node => node.RefreshDetails());
            }
            finally
            {
                TreeViewNode.TreeView.EndUpdate();
            }
        }
    }

    private void LoadNodeToolTips(Nodes loadedNodes, CancellationToken token)
    {
        if (TreeViewNode.TreeView is null)
        {
            return;
        }

        loadedNodes.DepthEnumerator<SubmoduleNode>()
#pragma warning disable VSTHRD101 // Avoid unsupported async delegates
                .ForEach(async node =>
                {
                    try
                    {
                        await node.SetStatusToolTipAsync(token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    //// Comment out to debug BugReporter
                    ////catch (GitExtUtils.ExternalOperationException)
                    ////{
                    ////}
                });
#pragma warning restore VSTHRD101 // Avoid unsupported async delegates
    }

    protected override void PostFillTreeViewNode(bool firstTime)
    {
        if (firstTime)
        {
            TreeViewNode.ExpandAll();
        }
    }

    private Nodes FillSubmoduleTree(SubmoduleInfoResult result)
    {
        Validates.NotNull(result.TopProject);
        Validates.NotNull(result.Module);

        SubmoduleTreeBuildResult built = SubmoduleTreeBuilder.Build(result);

        foreach ((string? superPath, string submodulePath, string submoduleText) in built.SkippedMissingSuperPaths)
        {
            MessageBoxes.SubmoduleDirectoryDoesNotExist(owner: null, superPath ?? submodulePath, submoduleText);
        }

        Nodes nodes = new(this);
        nodes.AddNode(Convert(built.Root));
        return nodes;

        Node Convert(SubmoduleTreeNode node)
        {
            Node converted = node.IsFolder
                ? new SubmoduleFolderNode(this, node.Name)
                : new SubmoduleNode(this, node.Info!, node.IsCurrent, node.GitStatus, node.LocalPath, node.SuperPath);

            foreach (SubmoduleTreeNode child in node.Children)
            {
                converted.Nodes.AddNode(Convert(child));
            }

            return converted;
        }
    }

    public void UpdateSubmodule(IWin32Window owner, SubmoduleNode node)
    {
        UICommands.Execute(new UICmd.UpdateSubmodule(node.LocalPath, node.SuperPath), owner);
    }

    public void OpenSubmodule(SubmoduleNode node)
    {
        node.Open();
    }

    public void OpenSubmoduleInGitExtensions(SubmoduleNode node)
    {
        node.LaunchGitExtensions();
    }

    public void ManageSubmodules(IWin32Window owner)
    {
        UICommands.Execute(new UICmd.Submodules(), owner);
    }

    public void SynchronizeSubmodules(IWin32Window owner)
    {
        UICommands.Execute(new UICmd.SyncSubmodules(), owner);
    }

    public void ResetSubmodule(IWin32Window owner, SubmoduleNode node)
    {
        FormResetChanges.ActionEnum resetType = FormResetChanges.ShowResetDialog(owner, true, true);

        if (resetType == FormResetChanges.ActionEnum.Cancel)
        {
            return;
        }

        GitModule module = new(UICommands.GetRequiredService<IGitExecutorProvider>(), node.Info.Path);
        module.ResetAllChanges(clean: resetType == FormResetChanges.ActionEnum.ResetAndDelete);
    }

    public void StashSubmodule(IWin32Window owner, SubmoduleNode node)
    {
        IGitUICommands uiCmds = UICommands.WithWorkingDirectory(node.Info.Path);
        uiCmds.Execute(new UICmd.StashSave(AppSettings.IncludeUntrackedFilesInManualStash), owner);
    }

    public void CommitSubmodule(IWin32Window owner, SubmoduleNode node)
    {
        IGitUICommands submodulCommands = UICommands.WithWorkingDirectory(node.Info.Path.EnsureTrailingPathSeparator());
        submodulCommands.Execute(new UICmd.Commit(), owner);
    }
}
