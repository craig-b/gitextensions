using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using GitCommands.Actions;
using GitCommands.LeftPanel;
using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;

namespace GitExtensions.Avalonia;

/// <summary>
///  The action-registry projections: the commit-row context menu, the ref context
///  menu, and the command palette all render from GridMenuRegistry via MenuProjector.
///  The client's menus show the registry ∩ the handlers implemented here - the menu grows
///  as session operations land, with no menu code changes.
/// </summary>
public partial class MainWindow
{
    private static readonly MenuProfile _menuProfile = MenuProfile.Normal;

    private GridCommitMenuContext CommitMenuContext(GitRevision revision) => new(
        IsArtificial: revision.IsArtificial,
        IsStash: revision.ReflogSelector?.StartsWith("stash@") is true,
        InBisect: _session.InBisect,
        IsBareRepository: _session.IsBareRepository,
        SelectedCount: 1,
        HasCurrentBranch: !_session.IsDetachedHead);

    private Dictionary<string, Func<GitRevision, Task>> CommitActionHandlers => new()
    {
        ["commit.createBranch"] = async revision =>
        {
            string? name = await ConfirmDialog.InputAsync(this, "Create branch", $"Branch name (created at {revision.ObjectId.ToShortString()}):", "feature/my-branch");
            if (name is not null)
            {
                await RunOperationAsync($"Create branch {name}", () => _session.CreateBranchAtAsync(name, revision.ObjectId, checkout: true));
            }
        },
        ["commit.cherryPick"] = revision => RunOperationAsync($"Cherry-pick {revision.ObjectId.ToShortString()}", () => _session.CherryPickAsync(revision.ObjectId)),
        ["commit.revert"] = revision => RunOperationAsync($"Revert {revision.ObjectId.ToShortString()}", () => _session.RevertAsync(revision.ObjectId)),
        ["commit.mergeIntoCurrent"] = revision => RunOperationAsync($"Merge {revision.ObjectId.ToShortString()}", () => _session.MergeAsync(revision.ObjectId.ToString())),
        ["copy.hash"] = revision => CopyToClipboardAsync(revision.ObjectId.ToString()),
        ["copy.message"] = revision => CopyToClipboardAsync(revision.Body ?? revision.Subject),
        ["copy.author"] = revision => CopyToClipboardAsync($"{revision.Author} <{revision.AuthorEmail}>"),
        ["copy.date"] = revision => CopyToClipboardAsync(revision.CommitDate.ToString("yyyy-MM-dd HH:mm:ss")),
        ["artificial.commit"] = async _ =>
        {
            CommitWindow commitWindow = new(_session);
            await commitWindow.ShowDialog(this);
            if (commitWindow.Committed)
            {
                await ReloadLogAsync();
            }
        },
        ["stash.apply"] = revision => RunOperationAsync("Apply stash", () => _session.StashApplyAsync(revision.ReflogSelector!)),
        ["stash.pop"] = revision => RunOperationAsync("Pop stash", () => _session.StashPopAsync()),
        ["stash.drop"] = async revision =>
        {
            if (await ConfirmDialog.ConfirmAsync(this, "Drop stash", $"Drop {revision.ReflogSelector}?"))
            {
                await RunOperationAsync("Drop stash", () => _session.StashDropAsync(revision.ReflogSelector!));
            }
        },
    };

    private Dictionary<string, Func<RefTreeNode, Task>> RefActionHandlers => new()
    {
        ["ref.checkout"] = node => RunOperationAsync($"Checkout {node.FullPath}", () => _session.CheckoutBranchAsync(node.FullPath)),
        ["ref.mergeIntoCurrent"] = node => RunOperationAsync($"Merge {node.FullPath}", () => _session.MergeAsync(node.FullPath)),
        ["ref.delete"] = async node =>
        {
            if (!await ConfirmDialog.ConfirmAsync(this, "Delete", $"Delete {node.FullPath}?"))
            {
                return;
            }

            if (node.Kind is RefTreeNodeKind.Tag)
            {
                await RunOperationAsync($"Delete tag {node.FullPath}", () => _session.DeleteTagAsync(node.FullPath));
            }
            else
            {
                await RunOperationAsync($"Delete branch {node.FullPath}", () => _session.DeleteBranchAsync(node.FullPath));
            }
        },
        ["ref.copyName"] = node => CopyToClipboardAsync(node.FullPath),
    };

    private async Task CopyToClipboardAsync(string text)
    {
        if (GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private void OnLogContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_selectedRevision is not GitRevision revision)
        {
            return;
        }

        e.Handled = true;
        GridCommitMenuContext context = CommitMenuContext(revision);
        Dictionary<string, Func<GitRevision, Task>> handlers = CommitActionHandlers;

        ContextMenu menu = BuildMenu(
            GridMenuRegistry.CommitMenuFor(context),
            action => handlers.ContainsKey(action.Id),
            action => GridMenuRegistry.IsApplicable(action, context),
            action => handlers[action.Id](revision));

        if (menu.Items.Count > 0)
        {
            menu.Open(LogControl);
        }
    }

    private void OnRefTreeContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        RefMenuKind? kind = (RefTree.SelectedItem as RefTreeNode)?.Kind switch
        {
            RefTreeNodeKind.LocalBranch => RefMenuKind.LocalBranch,
            RefTreeNodeKind.RemoteBranch => RefMenuKind.RemoteBranch,
            RefTreeNodeKind.Tag => RefMenuKind.Tag,
            _ => null,
        };

        if (kind is null || RefTree.SelectedItem is not RefTreeNode node)
        {
            return;
        }

        e.Handled = true;
        RefMenuContext context = new(kind.Value, node.IsCurrent);
        Dictionary<string, Func<RefTreeNode, Task>> handlers = RefActionHandlers;

        ContextMenu menu = BuildMenu(
            GridMenuRegistry.RefActions,
            action => handlers.ContainsKey(action.Id),
            action => GridMenuRegistry.IsApplicable(action, context),
            action => handlers[action.Id](node));

        if (menu.Items.Count > 0)
        {
            menu.Open(RefTree);
        }
    }

    private static ContextMenu BuildMenu(
        IReadOnlyList<ActionDescriptor> actions,
        Func<ActionDescriptor, bool> isImplemented,
        Func<ActionDescriptor, bool> isApplicable,
        Func<ActionDescriptor, Task> execute)
    {
        var groups = MenuProjector.Project(
            [.. actions.Where(isImplemented)],
            _menuProfile,
            isApplicable);

        ContextMenu menu = new();
        bool first = true;
        foreach (IReadOnlyList<ProjectedMenuItem> group in groups)
        {
            if (!first)
            {
                menu.Items.Add(new Separator());
            }

            first = false;
            foreach (ProjectedMenuItem item in group)
            {
                MenuItem menuItem = new()
                {
                    Header = item.Action.Caption,
                    IsEnabled = item.Enabled,
                };
                ActionDescriptor action = item.Action;
                menuItem.Click += (_, _) => _ = execute(action);
                menu.Items.Add(menuItem);
            }
        }

        return menu;
    }

    /// <summary>Verification harness (GE_SPIKE_MENUTEST): print the projected menus and palette size.</summary>
    internal async Task RunMenuHarnessAsync()
    {
        await Task.Delay(4000);
        LogControl.SelectRow(0);
        await Task.Delay(300);

        if (_selectedRevision is GitRevision revision)
        {
            GridCommitMenuContext context = CommitMenuContext(revision);
            var groups = MenuProjector.Project(
                [.. GridMenuRegistry.CommitMenuFor(context).Where(action => CommitActionHandlers.ContainsKey(action.Id))],
                _menuProfile,
                action => GridMenuRegistry.IsApplicable(action, context));
            Console.Error.WriteLine($"[menu] commit menu: {string.Join(" | ", groups.Select(group => string.Join(", ", group.Select(item => item.Enabled ? item.Action.Caption : $"({item.Action.Caption})"))))}");
        }

        RefMenuContext refContext = new(RefMenuKind.LocalBranch, IsCurrent: false);
        var refGroups = MenuProjector.Project(
            [.. GridMenuRegistry.RefActions.Where(action => RefActionHandlers.ContainsKey(action.Id))],
            _menuProfile,
            action => GridMenuRegistry.IsApplicable(action, refContext));
        Console.Error.WriteLine($"[menu] ref menu (local, not current): {string.Join(" | ", refGroups.Select(group => string.Join(", ", group.Select(item => item.Action.Caption))))}");

        var (branches, remotes, tags) = await Task.Run(_session.GetRefPanel);
        Console.Error.WriteLine($"[menu] palette would list commands + {branches.Count + remotes.Count + tags.Count}+ ref sections for jumps");

        Environment.Exit(0);
    }

    /// <summary>The command palette: applicable commands for the selection, plus jump-to-ref navigation.</summary>
    private async Task ShowCommandPaletteAsync()
    {
        List<(string Label, Func<Task> Execute)> entries = [];

        if (_selectedRevision is GitRevision revision)
        {
            GridCommitMenuContext context = CommitMenuContext(revision);
            Dictionary<string, Func<GitRevision, Task>> handlers = CommitActionHandlers;
            foreach (ActionDescriptor action in GridMenuRegistry.CommitMenuFor(context))
            {
                if (handlers.TryGetValue(action.Id, out Func<GitRevision, Task>? handler) && GridMenuRegistry.IsApplicable(action, context))
                {
                    entries.Add(($"{action.Caption}", () => handler(revision)));
                }
            }
        }

        var (branches, remotes, tags) = await Task.Run(_session.GetRefPanel);
        foreach (RefTreeNode leaf in Flatten(branches).Concat(Flatten(remotes)).Concat(Flatten(tags)))
        {
            if (leaf.ObjectId is ObjectId objectId)
            {
                entries.Add(($"Go to: {leaf.FullPath}", () =>
                {
                    LogControl.TryJumpTo(objectId);
                    return Task.CompletedTask;
                }));
            }
        }

        await CommandPalette.ShowAsync(this, entries);

        static IEnumerable<RefTreeNode> Flatten(IReadOnlyList<RefTreeNode> nodes)
            => nodes.SelectMany(node => node.Children.Count == 0 ? [node] : Flatten(node.Children));
    }
}
