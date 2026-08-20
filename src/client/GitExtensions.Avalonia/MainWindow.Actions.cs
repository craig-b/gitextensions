using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using GitCommands.Actions;
using GitCommands.Branch;
using GitCommands.Git;
using GitCommands.LeftPanel;
using GitCommands.Rebase;
using GitCommands.Reset;
using GitCommands.Rewrite;
using GitExtensions.Extensibility.Git;
using GitUI.CommandsDialogs;
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
    private static MenuProfile _menuProfile
        => new(GitCommands.AppSettings.MenuProfileMode, GitCommands.AppSettings.MenuInapplicableItemPolicy);

    private GitRevision? _compareBaseRevision;

    private GridCommitMenuContext CommitMenuContext(GitRevision revision) => new(
        IsArtificial: revision.IsArtificial,
        IsStash: revision.ReflogSelector?.StartsWith("stash@") is true,
        InBisect: _session.InBisect,
        IsBareRepository: _session.IsBareRepository,
        SelectedCount: Math.Max(1, LogControl.SelectedRevisions.Count),
        HasCurrentBranch: !_session.IsDetachedHead,
        HasBaseToCompare: _compareBaseRevision is not null);

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
        ["commit.mergeIntoCurrent"] = revision => MergeWithDialogAsync(revision.ObjectId.ToShortString(), revision.ObjectId.ToString()),
        ["commit.rebaseCurrentOnto"] = revision => RebaseWithConfirmAsync(revision.ObjectId.ToShortString(), revision.ObjectId.ToString()),
        ["commit.resetCurrentToHere"] = async revision =>
        {
            bool isDirty = await _session.IsDirtyAsync();
            ResetMode? mode = await ResetDialog.ShowAsync(this, revision.ObjectId.ToShortString(), isDirty);
            if (mode is null)
            {
                return;
            }

            if (ResetCurrentBranchPolicy.RequiresConfirmation(mode.Value)
                && !await ConfirmDialog.ConfirmAsync(this, "Hard reset", "A hard reset discards ALL local changes. Continue?"))
            {
                return;
            }

            await RunOperationAsync($"Reset ({mode.Value}) to {revision.ObjectId.ToShortString()}", () => _session.ResetAsync(mode.Value, revision.ObjectId));
        },
        ["commit.createTag"] = async revision =>
        {
            if (await CreateTagDialog.ShowAsync(this, revision.ObjectId, _session.ResolveTagPushRemote()) is not (var args, var pushIt))
            {
                return;
            }

            await RunOperationAsync($"Create tag {args.TagName}", () => _session.CreateTagAsync(args));
            if (pushIt)
            {
                await RunOperationAsync($"Push tag {args.TagName}", () => _session.PushTagAsync(_session.ResolveTagPushRemote(), args.TagName));
            }
        },
        ["copy.hash"] = revision => CopyToClipboardAsync(revision.ObjectId.ToString()),
        ["copy.message"] = revision => CopyToClipboardAsync(revision.Body ?? revision.Subject),
        ["copy.author"] = revision => CopyToClipboardAsync($"{revision.Author} <{revision.AuthorEmail}>"),
        ["copy.date"] = revision => CopyToClipboardAsync(revision.CommitDate.ToString("yyyy-MM-dd HH:mm:ss")),
        ["copy.refNames"] = revision => CopyToClipboardAsync(string.Join("\n", (revision.Refs ?? []).Select(r => r.Name))),
        ["artificial.commit"] = async _ =>
        {
            CommitWindow commitWindow = new(_session);
            await commitWindow.ShowDialog(this);
            if (commitWindow.Committed)
            {
                await ReloadLogAsync();
            }
        },
        ["commit.resetAnotherToHere"] = async revision =>
        {
            var (candidates, defaultName) = await Task.Run(() => _session.GetResetAnotherBranchCandidates(revision));
            if (candidates.Count == 0)
            {
                await ConfirmDialog.ErrorAsync(this, "Reset another branch", "No other local branch can be reset to this commit.");
                return;
            }

            List<(string Label, Func<Task> Execute)> entries = [.. candidates.Select(candidate =>
                ($"{(candidate.Name == defaultName ? "★ " : "")}Reset {candidate.Name} to {revision.ObjectId.ToShortString()}",
                 (Func<Task>)(() => RunOperationAsync($"Reset {candidate.Name}", () => _session.UpdateRefAsync(candidate.CompleteName, revision.ObjectId)))))];
            await CommandPalette.ShowAsync(this, entries);
        },
        ["rewrite.fixup"] = revision => StartPrefixedCommitAsync(GitUI.CommandsDialogs.CommitKind.Fixup, revision),
        ["rewrite.squash"] = revision => StartPrefixedCommitAsync(GitUI.CommandsDialogs.CommitKind.Squash, revision),
        ["rewrite.amend"] = revision => StartPrefixedCommitAsync(GitUI.CommandsDialogs.CommitKind.Amend, revision),
        ["rewrite.reword"] = async revision =>
        {
            string? message = await ConfirmDialog.InputAsync(
                this, "Reword commit", $"New message for {revision.ObjectId.ToShortString()}:",
                initialText: string.IsNullOrEmpty(revision.Body) ? revision.Subject : revision.Body,
                multiline: true);
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            await RunOperationAsync($"Reword {revision.ObjectId.ToShortString()}",
                () => _session.RewriteCommitAsync(revision, RewriteTodoAction.Reword, message));
            UpdateRebaseBar();
        },
        ["rewrite.edit"] = async revision =>
        {
            if (!await ConfirmDialog.ConfirmAsync(this, "Edit commit",
                $"The rebase will stop at {revision.ObjectId.ToShortString()} for amending.\nCommit your changes, then use \"Rebase: continue\" in the toolbar."))
            {
                return;
            }

            await RunOperationAsync($"Edit {revision.ObjectId.ToShortString()}",
                async () =>
                {
                    (bool _, string output) = await _session.RewriteCommitAsync(revision, RewriteTodoAction.Edit, rewordMessage: null);

                    // A stop for editing is the intended outcome, not a failure.
                    return (true, output);
                });
            UpdateRebaseBar();
        },
        ["compare.toCurrentBranch"] = revision =>
        {
            new CompareWindow(_session, revision.ObjectId, revision.Subject, _session.CurrentCheckout, _session.SelectedBranch).Show(this);
            return Task.CompletedTask;
        },
        ["compare.toWorkingDir"] = async revision =>
        {
            if (!GitCommands.Compare.CompareRevisions.CanCompareToWorkingDirectory(revision.ObjectId))
            {
                await ConfirmDialog.ErrorAsync(this, "Compare", "Cannot diff the working directory to itself.");
                return;
            }

            new CompareWindow(_session, revision.ObjectId, revision.Subject, ObjectId.WorkTreeId, "Working directory").Show(this);
        },
        ["compare.toBranch"] = async revision =>
        {
            var (branches, remotes, _) = await Task.Run(_session.GetRefPanel);
            List<(string Label, Func<Task> Execute)> entries = [.. Flatten(branches).Concat(Flatten(remotes))
                .Where(leaf => leaf.ObjectId is not null)
                .Select(leaf => ($"Compare to: {leaf.FullPath}", (Func<Task>)(() =>
                {
                    new CompareWindow(_session, revision.ObjectId, revision.Subject, leaf.ObjectId!.Value, leaf.FullPath).Show(this);
                    return Task.CompletedTask;
                })))];
            await CommandPalette.ShowAsync(this, entries);

            static IEnumerable<RefTreeNode> Flatten(IReadOnlyList<RefTreeNode> nodes)
                => nodes.SelectMany(node => node.Children.Count == 0 ? [node] : Flatten(node.Children));
        },
        ["compare.selected"] = async revision =>
        {
            IReadOnlyList<GitRevision> selected = LogControl.SelectedRevisions;
            if (selected.Count < 2)
            {
                await ConfirmDialog.ErrorAsync(this, "Compare", "Select two commits to compare (Ctrl+click).");
                return;
            }

            // Rows sort newest-first; the older commit is the BASE.
            new CompareWindow(_session, selected[^1].ObjectId, selected[^1].Subject, selected[0].ObjectId, selected[0].Subject).Show(this);
        },
        ["compare.selectBase"] = revision =>
        {
            _compareBaseRevision = revision;
            return Task.CompletedTask;
        },
        ["compare.toBase"] = async revision =>
        {
            if (_compareBaseRevision is not GitRevision baseRevision)
            {
                await ConfirmDialog.ErrorAsync(this, "Compare", "Select a BASE commit first.");
                return;
            }

            new CompareWindow(_session, baseRevision.ObjectId, baseRevision.Subject, revision.ObjectId, revision.Subject).Show(this);
        },
        ["commit.archive"] = async revision =>
        {
            if (GetTopLevel(this)?.StorageProvider is not { } storage)
            {
                return;
            }

            string suggestion = GitCommands.Archive.ArchiveModel.SuggestFileName(
                new System.IO.DirectoryInfo(_session.WorkingDir).Name, revision.ObjectId.ToShortString(), []);
            var file = await storage.SaveFilePickerAsync(new global::Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Save archive as",
                SuggestedFileName = suggestion + ".zip",
                FileTypeChoices =
                [
                    new global::Avalonia.Platform.Storage.FilePickerFileType("Zip archive") { Patterns = ["*.zip"] },
                    new global::Avalonia.Platform.Storage.FilePickerFileType("Tar archive") { Patterns = ["*.tar"] },
                ],
            });

            if (file?.TryGetLocalPath() is not string path)
            {
                return;
            }

            GitCommands.Archive.ArchiveFormat format = path.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)
                ? GitCommands.Archive.ArchiveFormat.Tar
                : GitCommands.Archive.ArchiveFormat.Zip;
            await RunOperationAsync($"Archive {revision.ObjectId.ToShortString()}", () => _session.ArchiveAsync(format, revision.ObjectId.ToString(), path));
        },
        ["bisect.good"] = _ => RunOperationAsync("Bisect: mark good", () => _session.ContinueBisectAsync(GitBisectOption.Good)),
        ["bisect.bad"] = _ => RunOperationAsync("Bisect: mark bad", () => _session.ContinueBisectAsync(GitBisectOption.Bad)),
        ["bisect.skip"] = _ => RunOperationAsync("Bisect: skip", () => _session.ContinueBisectAsync(GitBisectOption.Skip)),
        ["bisect.stop"] = _ => RunOperationAsync("Bisect: stop", () => _session.StopBisectAsync()),
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
        ["ref.mergeIntoCurrent"] = node => MergeWithDialogAsync(node.FullPath, node.FullPath),
        ["ref.rebaseCurrentOnto"] = node => RebaseWithConfirmAsync(node.FullPath, node.FullPath),
        ["ref.delete"] = async node =>
        {
            if (node.Kind is RefTreeNodeKind.Tag)
            {
                if (await ConfirmDialog.ConfirmAsync(this, "Delete", $"Delete tag {node.FullPath}?"))
                {
                    await RunOperationAsync($"Delete tag {node.FullPath}", () => _session.DeleteTagAsync(node.FullPath));
                }

                return;
            }

            if (node.Kind is RefTreeNodeKind.RemoteBranch)
            {
                if (await ConfirmDialog.ConfirmAsync(this, "Delete", $"Delete remote branch {node.FullPath}? (local tracking ref only)"))
                {
                    await RunOperationAsync($"Delete {node.FullPath}", () => _session.DeleteBranchAsync(node.FullPath));
                }

                return;
            }

            MergedBranchScan scan = await _session.GetMergedBranchScanAsync();
            bool unmerged = DeleteBranchPreflight.IsUnmerged(scan.CurrentBranch, node.FullPath, scan.MergedBranches);
            string question = unmerged
                ? $"The branch {node.FullPath} has NOT been merged into HEAD.\nDelete anyway? (reflog can restore deleted branches)"
                : $"Delete branch {node.FullPath}?";
            if (await ConfirmDialog.ConfirmAsync(this, "Delete branch", question))
            {
                await RunOperationAsync($"Delete branch {node.FullPath}", () => _session.DeleteBranchAsync(node.FullPath, force: unmerged));
            }
        },
        ["ref.copyName"] = node => CopyToClipboardAsync(node.FullPath),
        ["ref.rename"] = async node =>
        {
            string? newName = await ConfirmDialog.InputAsync(this, "Rename branch", $"New name for {node.FullPath}:", node.FullPath);
            if (!string.IsNullOrWhiteSpace(newName) && newName != node.FullPath)
            {
                await RunOperationAsync($"Rename {node.FullPath}", () => _session.RenameBranchAsync(node.FullPath, newName.Trim()));
            }
        },
    };

    /// <summary>Fixup/squash/amend: a prefixed commit from staged changes, then the optional autosquash fold.</summary>
    private async Task StartPrefixedCommitAsync(GitUI.CommandsDialogs.CommitKind kind, GitRevision targetRevision)
    {
        CommitWindow commitWindow = new(_session, GitCommands.Rewrite.HistoryRewrite.PrefixedSubject(kind, targetRevision.Subject));
        await commitWindow.ShowDialog(this);
        if (!commitWindow.Committed)
        {
            return;
        }

        await ReloadLogAsync();
        if (await ConfirmDialog.ConfirmAsync(this, "Autosquash",
            $"Fold the {kind.GetPrefix()} commit into {targetRevision.ObjectId.ToShortString()} now (autosquash rebase)?"))
        {
            await RunOperationAsync("Autosquash fold", () => _session.AutosquashFoldAsync(targetRevision));
            UpdateRebaseBar();
        }
    }

    /// <summary>Merge via the options dialog (MergeBranchOptions).</summary>
    private async Task MergeWithDialogAsync(string displayName, string mergeRef)
    {
        GitCommands.Merge.MergeBranchOptions? options = await MergeDialog.ShowAsync(this, mergeRef, _session.SelectedBranch);
        if (options is not null)
        {
            await RunOperationAsync($"Merge {displayName}", () => _session.MergeWithOptionsAsync(options));
        }
    }

    /// <summary>Plain rebase of the current branch onto a ref, with the up-to-date output recognized.</summary>
    private async Task RebaseWithConfirmAsync(string displayName, string onto)
    {
        if (!await ConfirmDialog.ConfirmAsync(this, "Rebase", $"Rebase {_session.SelectedBranch} onto {displayName}?"))
        {
            return;
        }

        await RunOperationAsync($"Rebase onto {displayName}", async () =>
        {
            (bool success, string output) = await _session.RebaseAsync(onto);
            return success && RebaseOutputAnalyzer.IsBranchUpToDate(output)
                ? (true, "Already up to date.")
                : (success, output);
        });
    }

    private IReadOnlyDictionary<string, string> _hotkeyMap = new Dictionary<string, string>();

    /// <summary>The effective gesture per action id: registry defaults overridden by the Hotkeys page.</summary>
    private void RebuildHotkeyMap()
        => _hotkeyMap = HotkeyResolution.ResolveAll(
            [.. GridMenuRegistry.CommitActions, .. GridMenuRegistry.RefActions],
            actionId => GitCommands.AppSettings.GetString(HotkeyResolution.SettingKey(actionId), null));

    /// <summary>Dispatches a registry hotkey against the current selection (text inputs keep their keys).</summary>
    private void HandleActionHotkey(KeyEventArgs keyArgs)
    {
        if (keyArgs.Source is TextBox || _selectedRevision is not GitRevision revision)
        {
            return;
        }

        string keyName = keyArgs.Key switch
        {
            Key.Delete => "Del",
            _ => keyArgs.Key.ToString(),
        };
        string gesture = HotkeyResolution.Normalize(
            keyArgs.KeyModifiers.HasFlag(KeyModifiers.Control),
            keyArgs.KeyModifiers.HasFlag(KeyModifiers.Shift),
            keyArgs.KeyModifiers.HasFlag(KeyModifiers.Alt),
            keyName);

        GridCommitMenuContext context = CommitMenuContext(revision);
        foreach ((string actionId, string actionGesture) in _hotkeyMap)
        {
            if (!actionGesture.Equals(gesture, StringComparison.OrdinalIgnoreCase)
                || !CommitActionHandlers.TryGetValue(actionId, out Func<GitRevision, Task>? handler))
            {
                continue;
            }

            ActionDescriptor? action = GridMenuRegistry.CommitActions.FirstOrDefault(a => a.Id == actionId);
            if (action is not null && !GridMenuRegistry.IsApplicable(action, context))
            {
                continue;
            }

            keyArgs.Handled = true;
            _ = handler(revision);
            return;
        }
    }

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
