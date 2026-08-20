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
        => new(
            GitCommands.AppSettings.MenuProfileMode,
            GitCommands.AppSettings.MenuInapplicableItemPolicy,
            MenuCustomOrder.Parse(GitCommands.AppSettings.MenuCustomOrder));

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
    private IReadOnlyList<GitCommands.Scripts.ScriptDefinition> _scripts = [];

    /// <summary>The stored script definitions, reloaded when the editor or settings close.</summary>
    private void ReloadScripts()
        => _scripts = GitCommands.Scripts.ScriptStorage.Load(key => GitCommands.AppSettings.GetString(key, null));

    /// <summary>The effective gesture per action id: registry defaults overridden by the Hotkeys page.</summary>
    private void RebuildHotkeyMap()
    {
        ReloadScripts();
        _hotkeyMap = HotkeyResolution.ResolveAll(
            [
                .. GridMenuRegistry.CommitActions,
                .. GridMenuRegistry.RefActions,
                .. GitCommands.Scripts.ScriptActions.ToDescriptors(_scripts, GitCommands.Scripts.ScriptSurfaces.CommitMenu),
            ],
            actionId => GitCommands.AppSettings.GetString(HotkeyResolution.SettingKey(actionId), null));
    }

    /// <summary>Runs a script: prompts pre-collected, tokens expanded, interpreter resolved.</summary>
    private async Task RunScriptAsync(GitCommands.Scripts.ScriptDefinition script, GitRevision? revision, string? refName)
    {
        if (script.Destructive
            && !await ConfirmDialog.ConfirmAsync(this, script.Caption, $"Run \"{script.Caption}\"?"))
        {
            return;
        }

        Dictionary<string, string> answers = [];
        foreach (string question in GitCommands.Scripts.ScriptTokenSubstitution.ExtractPrompts(script.Command))
        {
            if (answers.ContainsKey(question))
            {
                continue;
            }

            string? answer = await ConfirmDialog.InputAsync(this, script.Caption, question);
            if (answer is null)
            {
                return;
            }

            answers[question] = answer;
        }

        IGitRef? selectedLocal = revision?.Refs?.FirstOrDefault(r => !r.IsRemote && !r.IsTag);
        IGitRef? selectedRemote = revision?.Refs?.FirstOrDefault(r => r.IsRemote);
        GitCommands.Scripts.ScriptTokenContext context = new(
            SelectedHash: revision?.ObjectId.ToString(),
            SelectedHashes: string.Join(" ", LogControl.SelectedRevisions.Select(r => r.ObjectId.ToString())),
            SelectedSubject: revision?.Subject,
            SelectedMessage: revision?.Body ?? revision?.Subject,
            SelectedAuthor: revision?.Author,
            SelectedBranch: selectedLocal?.Name,
            SelectedTag: revision?.Refs?.FirstOrDefault(r => r.IsTag)?.Name,
            SelectedRemoteBranch: selectedRemote?.Name,
            SelectedRemote: selectedRemote?.Name.Split('/').FirstOrDefault(),
            CurrentBranch: _session.SelectedBranch,
            CurrentHash: _session.CurrentCheckout.ToString(),
            CurrentRemote: _session.GetPushDefaults().Remote,
            RepoName: System.IO.Path.GetFileName(_session.WorkingDir.TrimEnd('/', '\\')),
            RepoDir: _session.WorkingDir,
            RefName: refName);

        // Token values are repo content (subjects, branch names) - they reach the process
        // as environment variables, never as shell-parsed command text (injection-safe).
        GitCommands.Scripts.ScriptInterpreterKind kind = GitCommands.Scripts.ScriptInterpreter.Classify(script.Interpreter);
        GitCommands.Scripts.ExpandedScript expanded = GitCommands.Scripts.ScriptTokenSubstitution.ExpandSafe(script.Command, context, answers, kind);
        (string fileName, string arguments) = GitCommands.Scripts.ScriptInterpreter.Resolve(script.Interpreter, expanded.Command, OperatingSystem.IsWindows());

        if (script.RunInBackground)
        {
            _ = _session.RunProcessAsync(fileName, arguments, expanded.Environment);
            return;
        }

        await RunOperationAsync(script.Caption, () => _session.RunProcessAsync(fileName, arguments, expanded.Environment));
    }

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
            if (!actionGesture.Equals(gesture, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (_scripts.FirstOrDefault(script => script.ActionId == actionId) is GitCommands.Scripts.ScriptDefinition hotkeyScript)
            {
                keyArgs.Handled = true;
                _ = RunScriptAsync(hotkeyScript, revision, refName: null);
                return;
            }

            if (!CommitActionHandlers.TryGetValue(actionId, out Func<GitRevision, Task>? handler))
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
        // Right-clicking a branch/tag chip opens the ref menu for that ref.
        if (e.TryGetPosition(LogControl, out global::Avalonia.Point position)
            && LogControl.HitTestRefChip(position) is IGitRef chipRef)
        {
            e.Handled = true;
            OpenRefMenuForChip(chipRef);
            return;
        }

        if (_selectedRevision is not GitRevision revision)
        {
            return;
        }

        e.Handled = true;
        GridCommitMenuContext context = CommitMenuContext(revision);
        Dictionary<string, Func<GitRevision, Task>> handlers = CommitActionHandlers;
        foreach (GitCommands.Scripts.ScriptDefinition script in _scripts)
        {
            GitCommands.Scripts.ScriptDefinition captured = script;
            handlers[script.ActionId] = rev => RunScriptAsync(captured, rev, refName: null);
        }

        ContextMenu menu = BuildMenu(
            [.. GridMenuRegistry.CommitMenuFor(context), .. GitCommands.Scripts.ScriptActions.ToDescriptors(_scripts, GitCommands.Scripts.ScriptSurfaces.CommitMenu)],
            action => handlers.ContainsKey(action.Id),
            action => GridMenuRegistry.IsApplicable(action, context),
            action => handlers[action.Id](revision));

        if (menu.Items.Count > 0)
        {
            menu.Open(LogControl);
        }
    }

    /// <summary>The ref menu for a clicked chip - the same registry projection the sidebar uses.</summary>
    private void OpenRefMenuForChip(IGitRef chipRef)
    {
        RefMenuKind kind = chipRef.IsTag ? RefMenuKind.Tag
            : chipRef.IsRemote ? RefMenuKind.RemoteBranch
            : RefMenuKind.LocalBranch;
        RefTreeNode node = new()
        {
            Name = chipRef.LocalName,
            FullPath = chipRef.IsTag || chipRef.IsRemote ? chipRef.Name : chipRef.LocalName,
            Kind = chipRef.IsTag ? RefTreeNodeKind.Tag : chipRef.IsRemote ? RefTreeNodeKind.RemoteBranch : RefTreeNodeKind.LocalBranch,
        };

        RefMenuContext context = new(kind, IsCurrent: !chipRef.IsRemote && !chipRef.IsTag && chipRef.LocalName == _session.SelectedBranch);
        Dictionary<string, Func<RefTreeNode, Task>> handlers = RefActionHandlers;

        ContextMenu menu = BuildMenu(
            GridMenuRegistry.RefActions,
            action => handlers.ContainsKey(action.Id),
            action => GridMenuRegistry.IsApplicable(action, context),
            action => handlers[action.Id](node));

        if (menu.Items.Count > 0)
        {
            menu.Open(LogControl);
        }
    }

    private void OnRefTreeContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (RefTree.SelectedItem is RefTreeNode { Kind: RefTreeNodeKind.Worktree } worktreeNode)
        {
            e.Handled = true;
            OpenWorktreeMenu(worktreeNode);
            return;
        }

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
        foreach (GitCommands.Scripts.ScriptDefinition script in _scripts)
        {
            GitCommands.Scripts.ScriptDefinition captured = script;
            handlers[script.ActionId] = refNode => RunScriptAsync(captured, _selectedRevision, refNode.FullPath);
        }

        ContextMenu menu = BuildMenu(
            [.. GridMenuRegistry.RefActions, .. GitCommands.Scripts.ScriptActions.ToDescriptors(_scripts, GitCommands.Scripts.ScriptSurfaces.RefMenu)],
            action => handlers.ContainsKey(action.Id),
            action => GridMenuRegistry.IsApplicable(action, context),
            action => handlers[action.Id](node));

        if (menu.Items.Count > 0)
        {
            menu.Open(RefTree);
        }
    }

    /// <summary>The worktree node menu (not registry-driven yet - the worktree surface has no registry).</summary>
    private void OpenWorktreeMenu(RefTreeNode node)
    {
        ContextMenu menu = new();
        AddItem("Open in new window", () =>
        {
            new MainWindow(node.FullPath).Show();
            return Task.CompletedTask;
        });
        AddItem("Create worktree...", async () =>
        {
            IReadOnlyList<string> branches = await Task.Run(_session.GetLocalBranchNames);
            var choice = await CreateWorktreeDialog.ShowAsync(this, _session.WorkingDir.TrimEnd('/', '\\'), branches, _session.SelectedBranch);
            if (choice is var (directory, newBranchOption) && choice is not null)
            {
                await RunOperationAsync("Create worktree", () => _session.CreateWorktreeAsync(directory, newBranchOption));
                await LoadRefPanelAsync();
            }
        });
        AddItem("Delete worktree...", async () =>
        {
            if (await ConfirmDialog.ConfirmAsync(this, "Delete worktree", $"Remove the worktree at {node.FullPath}?\nThis cannot be undone."))
            {
                await RunOperationAsync("Delete worktree", () => _session.RemoveWorktreeAsync(node.FullPath, force: true));
                await LoadRefPanelAsync();
            }
        });
        AddItem("Prune worktrees", async () =>
        {
            await RunOperationAsync("Prune worktrees", _session.PruneWorktreesAsync);
            await LoadRefPanelAsync();
        });
        menu.Open(RefTree);

        void AddItem(string caption, Func<Task> execute)
        {
            MenuItem item = new() { Header = caption };
            item.Click += (_, _) => _ = execute();
            menu.Items.Add(item);
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
                    Header = Loc.T(item.Action.Caption),
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

        // Scan the visible rows for a rendered ref chip.
        IGitRef? chip = null;
        for (double y = 8; y < LogControl.Bounds.Height && chip is null; y += 8)
        {
            for (double x = 0; x < LogControl.Bounds.Width && chip is null; x += 8)
            {
                chip = LogControl.HitTestRefChip(new global::Avalonia.Point(x, y));
            }
        }

        Console.Error.WriteLine($"[menu] chip hit-test: {(chip is null ? "none found" : $"{chip.Name}")}");

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
                    string hint = _hotkeyMap.TryGetValue(action.Id, out string? gesture) ? $"  [{gesture}]" : "";
                    entries.Add(($"{Loc.T(action.Caption)}{hint}", () => handler(revision)));
                }
            }
        }

        foreach (GitCommands.Scripts.ScriptDefinition script in _scripts.Where(s => s.Enabled))
        {
            GitCommands.Scripts.ScriptDefinition captured = script;
            entries.Add(($"Script: {script.Caption}", () => RunScriptAsync(captured, _selectedRevision, refName: null)));
        }

        entries.Add(("Scripts: edit...", async () =>
        {
            ScriptsWindow scriptsWindow = new();
            await scriptsWindow.ShowDialog(this);
            RebuildHotkeyMap();
        }));

        foreach ((string aliasName, string expansion) in await _session.GetGitAliasesAsync())
        {
            string capturedName = aliasName;
            string hint = expansion.Length > 60 ? expansion[..60] + "…" : expansion;
            entries.Add(($"git: {aliasName}  —  {hint}", () => RunOperationAsync($"git {capturedName}", () => _session.RunProcessAsync(
                "git", capturedName))));
        }

        entries.Add(("Add submodule...", async () =>
        {
            string? url = await ConfirmDialog.InputAsync(this, "Add submodule", "Remote path or URL:");
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            string? localPath = await ConfirmDialog.InputAsync(this, "Add submodule", "Local path:", initialText: GitCommands.PathUtil.GetRepositoryName(url));
            if (!GitCommands.Worktree.SubmoduleAddModel.IsValid(url, localPath))
            {
                return;
            }

            await RunOperationAsync($"Add submodule {localPath}", () => _session.AddSubmoduleAsync(url, localPath!, branch: "", force: false));
        }));
        entries.Add(("Update all submodules", () => RunOperationAsync("Update submodules", _session.UpdateSubmodulesAsync)));

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
