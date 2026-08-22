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
        ["range.compareSelected"] = async revision =>
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
        ["range.copyHashes"] = _ => CopyToClipboardAsync(string.Join("\n", LogControl.SelectedRevisions.Select(revision => revision.ObjectId.ToString()))),
        ["range.cherryPick"] = async _ =>
        {
            IReadOnlyList<GitRevision> selected = LogControl.SelectedRevisions;
            if (selected.Count < 2 || !await ConfirmDialog.ConfirmAsync(this, "Cherry-pick",
                $"Cherry-pick {selected.Count} commits onto the current branch, oldest first?"))
            {
                return;
            }

            await RunOperationAsync($"Cherry-pick {selected.Count} commits", async () =>
            {
                foreach (GitRevision revision in selected.Reverse())
                {
                    (bool success, string output) = await _session.CherryPickAsync(revision.ObjectId);
                    if (!success)
                    {
                        return (false, $"Stopped at {revision.ObjectId.ToShortString()}:\n{output}");
                    }
                }

                return (true, "");
            });
        },
        ["range.squash"] = async _ =>
        {
            IReadOnlyList<GitRevision> selected = LogControl.SelectedRevisions;
            if (SquashSelection.Validate(selected, _session.CurrentCheckout) is string error)
            {
                await ConfirmDialog.ErrorAsync(this, "Squash", error);
                return;
            }

            if (!await ConfirmDialog.ConfirmAsync(this, "Squash",
                $"Squash {selected.Count} commits into one?\nThis soft-resets to {SquashSelection.ResetTarget(selected).ToShortString()} and opens the commit window with all messages."))
            {
                return;
            }

            await RunOperationAsync("Squash: soft reset", () => _session.ResetAsync(ResetMode.Soft, SquashSelection.ResetTarget(selected)));
            CommitWindow commitWindow = new(_session, SquashSelection.CombinedMessage(selected));
            await commitWindow.ShowDialog(this);
            await ReloadLogAsync();
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
        ["commit.checkoutBranch"] = async revision =>
        {
            // Checkout a local branch pointing at this commit; several -> pick, none -> explain.
            List<string> branchesHere = [.. (revision.Refs ?? []).Where(gitRef => !gitRef.IsRemote && !gitRef.IsTag).Select(gitRef => gitRef.LocalName)];
            switch (branchesHere.Count)
            {
                case 0:
                    await ConfirmDialog.ErrorAsync(this, "Checkout branch", "No local branch points at this commit.");
                    return;

                case 1:
                    await CheckoutBranchInteractiveAsync(branchesHere[0]);
                    return;

                default:
                    await CommandPalette.ShowAsync(this,
                        [.. branchesHere.Select(branch => ((string Label, Func<Task> Execute))($"Checkout: {branch}", () => CheckoutBranchInteractiveAsync(branch)))]);
                    return;
            }
        },
        ["commit.checkoutDetached"] = async revision =>
        {
            if (await ConfirmDialog.ConfirmAsync(this, "Checkout commit",
                    $"Checkout {revision.ObjectId.ToShortString()} detached? HEAD will not be on any branch."))
            {
                await RunOperationAsync($"Checkout {revision.ObjectId.ToShortString()}", () => _session.CheckoutRevisionAsync(revision.ObjectId));
            }
        },
        ["commit.openDifftool"] = revision =>
        {
            // The difftool blocks until its window closes - run detached, report only failures.
            _ = Task.Run(async () =>
            {
                (bool success, string output) = await _session.OpenDifftoolAsync(revision.ObjectId);
                if (!success)
                {
                    await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ConfirmDialog.ErrorAsync(this, "Difftool", output));
                }
            });
            return Task.CompletedTask;
        },
        ["artificial.resetChanges"] = _ => ResetChangesFlowAsync(),
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
        ["ref.createBranchFrom"] = async node =>
        {
            if ((node.ObjectId ?? _session.ResolveRef(node.FullPath)) is not ObjectId target)
            {
                await ConfirmDialog.ErrorAsync(this, "Create branch", $"Cannot resolve {node.FullPath}.");
                return;
            }

            string? name = await ConfirmDialog.InputAsync(this, "Create branch", $"Branch name (from {node.FullPath}, without checking it out):", "feature/my-branch");
            if (name is not null)
            {
                await RunOperationAsync($"Create branch {name}", () => _session.CreateBranchAtAsync(name, target, checkout: false));
            }
        },
        ["ref.pushTag"] = node => RunOperationAsync($"Push tag {node.FullPath}", () => _session.PushTagAsync(_session.ResolveTagPushRemote(), node.FullPath)),
        ["ref.push"] = node => RunOperationAsync($"Push {node.FullPath}", () => _session.PushBranchAsync(node.FullPath)),
        ["ref.pull"] = node =>
        {
            // A remote-branch node's FullPath is "remote/branch".
            int slash = node.FullPath.IndexOf('/');
            if (slash <= 0)
            {
                return ConfirmDialog.ErrorAsync(this, "Pull", $"Cannot determine the remote of {node.FullPath}.");
            }

            string remote = node.FullPath[..slash];
            string branch = node.FullPath[(slash + 1)..];
            return RunOperationAsync($"Pull {node.FullPath}", () => _session.PullBranchAsync(remote, branch));
        },
        ["ref.fetch"] = node => WithRemoteBranch(node, (remote, branch) =>
            WithPanelRefresh(RunOperationAsync($"Fetch {node.FullPath}", () => _session.FetchBranchAsync(remote, branch)))),
        ["ref.fetchCheckout"] = node => FetchThenAsync(node, "ref.checkout"),
        ["ref.fetchMerge"] = node => FetchThenAsync(node, "ref.mergeIntoCurrent"),
        ["ref.fetchRebase"] = node => FetchThenAsync(node, "ref.rebaseCurrentOnto"),
        ["ref.fetchCreateBranch"] = node => FetchThenAsync(node, "ref.createBranchFrom"),
        ["ref.compareToCurrent"] = async node =>
        {
            if ((node.ObjectId ?? _session.ResolveRef(node.FullPath)) is not ObjectId target)
            {
                await ConfirmDialog.ErrorAsync(this, "Compare", $"Cannot resolve {node.FullPath}.");
                return;
            }

            new CompareWindow(_session, _session.CurrentCheckout, "HEAD", target, node.FullPath).Show(this);
        },
        ["ref.selectInLeftPanel"] = node =>
        {
            SelectRefInSidebar(node.FullPath);
            return Task.CompletedTask;
        },
        ["ref.filterForSelected"] = node =>
        {
            _filterBar.ApplyBranchFilter(node.FullPath);
            return Task.CompletedTask;
        },
        ["ref.rename"] = async node =>
        {
            string? newName = await ConfirmDialog.InputAsync(this, "Rename branch", $"New name for {node.FullPath}:", node.FullPath);
            if (!string.IsNullOrWhiteSpace(newName) && newName != node.FullPath)
            {
                await RunOperationAsync($"Rename {node.FullPath}", () => _session.RenameBranchAsync(node.FullPath, newName.Trim()));
            }
        },
    };

    /// <summary>Splits a remote-branch node's "remote/branch" path and runs the action, erroring on odd shapes.</summary>
    private Task WithRemoteBranch(RefTreeNode node, Func<string, string, Task> action)
    {
        int slash = node.FullPath.IndexOf('/');
        return slash <= 0
            ? ConfirmDialog.ErrorAsync(this, "Fetch", $"Cannot determine the remote of {node.FullPath}.")
            : action(node.FullPath[..slash], node.FullPath[(slash + 1)..]);
    }

    /// <summary>The remote-branch fetch combos: fetch the branch, then run the follow-up ref action on the fresh node.</summary>
    private Task FetchThenAsync(RefTreeNode node, string followUpActionId)
        => WithRemoteBranch(node, async (remote, branch) =>
        {
            await RunOperationAsync($"Fetch {node.FullPath}", () => _session.FetchBranchAsync(remote, branch));
            await LoadRefPanelAsync();
            await RefActionHandlers[followUpActionId](node);
        });

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
            action => handlers[action.Id](revision),
            GridMenuRegistry.CommitSubmenuGroups);

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
            action => handlers[action.Id](node),
            GridMenuRegistry.RefSubmenuGroups);

        if (menu.Items.Count > 0)
        {
            menu.Open(LogControl);
        }
    }

    private void OnRefTreeContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (RefTree.SelectedItem is not RefTreeNode node)
        {
            return;
        }

        // 2+ selected ref nodes swap in the panel's range menu (the grid's range-menu pattern).
        IReadOnlyList<RefTreeNode> selectedRefs = SelectedRefNodes();
        if (selectedRefs.Count >= 2)
        {
            e.Handled = true;
            OpenRefRangeMenu(selectedRefs);
            return;
        }

        switch (node.Kind)
        {
            case RefTreeNodeKind.LocalBranch or RefTreeNodeKind.RemoteBranch or RefTreeNodeKind.Tag:
                e.Handled = true;
                OpenSidebarRefMenu(node);
                return;

            case RefTreeNodeKind.Folder when IsInSection(node, RefTreeNodeKind.BranchesSection):
                e.Handled = true;
                OpenLeftPanelMenu(node, LeftPanelMenuRegistry.BranchFolderActions, _ => true);
                return;

            case RefTreeNodeKind.RemoteRepo:
                e.Handled = true;
                OpenLeftPanelMenu(node, LeftPanelMenuRegistry.RemoteRepoMenuFor(
                    new LeftPanelRemoteContext(node.Enabled, HasHttpUrl: node.Remote?.FetchUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) is true)), _ => true);
                return;

            case RefTreeNodeKind.Stash:
                e.Handled = true;
                LeftPanelStashContext stashContext = new(_session.IsBareRepository);
                OpenLeftPanelMenu(node, LeftPanelMenuRegistry.StashNodeActions,
                    action => LeftPanelMenuRegistry.IsApplicable(action, stashContext));
                return;

            case RefTreeNodeKind.Submodule:
                e.Handled = true;
                OpenLeftPanelMenu(node, LeftPanelMenuRegistry.SubmoduleMenuFor(
                    new LeftPanelSubmoduleContext(IsCurrent: false, _session.IsBareRepository)), _ => true);
                return;

            case RefTreeNodeKind.Worktree:
                e.Handled = true;
                LeftPanelWorktreeContext worktreeContext = new(
                    node.IsCurrent,
                    IsDeleted: !System.IO.Directory.Exists(node.FullPath),
                    DirectoryExists: System.IO.Directory.Exists(node.FullPath));
                OpenLeftPanelMenu(node, LeftPanelMenuRegistry.WorktreeNodeActions,
                    action => LeftPanelMenuRegistry.IsApplicable(action, worktreeContext));
                return;

            case RefTreeNodeKind.RemotesSection:
                e.Handled = true;
                OpenLeftPanelMenu(node, LeftPanelMenuRegistry.RemotesSectionActions, _ => true);
                return;

            case RefTreeNodeKind.StashesSection:
                e.Handled = true;
                OpenLeftPanelMenu(node, LeftPanelMenuRegistry.StashesSectionActions, _ => true);
                return;

            case RefTreeNodeKind.SubmodulesSection:
                e.Handled = true;
                OpenLeftPanelMenu(node, LeftPanelMenuRegistry.SubmodulesSectionActions, _ => true);
                return;

            case RefTreeNodeKind.WorktreesSection:
                e.Handled = true;
                OpenLeftPanelMenu(node, LeftPanelMenuRegistry.WorktreesSectionActions, _ => true);
                return;
        }
    }

    /// <summary>The registry ref menu for a sidebar branch/tag node (same projection as the grid's chips).</summary>
    private void OpenSidebarRefMenu(RefTreeNode node)
    {
        RefMenuKind kind = node.Kind switch
        {
            RefTreeNodeKind.RemoteBranch => RefMenuKind.RemoteBranch,
            RefTreeNodeKind.Tag => RefMenuKind.Tag,
            _ => RefMenuKind.LocalBranch,
        };

        RefMenuContext context = new(kind, node.IsCurrent, FromLeftPanel: true);
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
            action => handlers[action.Id](node),
            GridMenuRegistry.RefSubmenuGroups);

        if (menu.Items.Count > 0)
        {
            menu.Open(RefTree);
        }
    }

    /// <summary>Opens a panel-unique node menu (remote repo, stash, submodule, worktree, sections, folders).</summary>
    private void OpenLeftPanelMenu(RefTreeNode node, IReadOnlyList<ActionDescriptor> actions, Func<ActionDescriptor, bool> isApplicable)
    {
        Dictionary<string, Func<RefTreeNode, Task>> handlers = LeftPanelActionHandlers;
        ContextMenu menu = BuildMenu(
            actions,
            action => handlers.ContainsKey(action.Id),
            isApplicable,
            action => handlers[action.Id](node));

        if (menu.Items.Count > 0)
        {
            menu.Open(RefTree);
        }
    }

    /// <summary>The panel's range menu for a multi-selection of ref nodes.</summary>
    private void OpenRefRangeMenu(IReadOnlyList<RefTreeNode> selectedRefs)
    {
        LeftPanelRangeContext context = new(selectedRefs.Count);
        Dictionary<string, Func<IReadOnlyList<RefTreeNode>, Task>> handlers = RefRangeHandlers;
        ContextMenu menu = BuildMenu(
            LeftPanelMenuRegistry.RefRangeActions,
            action => handlers.ContainsKey(action.Id),
            action => LeftPanelMenuRegistry.IsApplicable(action, context),
            action => handlers[action.Id](selectedRefs));

        if (menu.Items.Count > 0)
        {
            menu.Open(RefTree);
        }
    }

    /// <summary>The branch/tag nodes of the sidebar's current multi-selection.</summary>
    private IReadOnlyList<RefTreeNode> SelectedRefNodes()
        => RefTree.SelectedItems is null
            ? []
            : [.. RefTree.SelectedItems.OfType<RefTreeNode>()
                .Where(item => item.Kind is RefTreeNodeKind.LocalBranch or RefTreeNodeKind.RemoteBranch or RefTreeNodeKind.Tag)];

    /// <summary>Whether the node sits under the given section header of the sidebar.</summary>
    private bool IsInSection(RefTreeNode node, RefTreeNodeKind sectionKind)
    {
        if (RefTree.ItemsSource is not IEnumerable<RefTreeNode> sections)
        {
            return false;
        }

        return sections.Any(section => section.Kind == sectionKind && Contains(section));

        bool Contains(RefTreeNode candidate)
            => candidate == node || candidate.Children.Any(Contains);
    }

    private Dictionary<string, Func<IReadOnlyList<RefTreeNode>, Task>> RefRangeHandlers => new()
    {
        ["refs.operateOn"] = nodes => OpenRefOperationsAsync(nodes, checkAll: true),
        ["ref.filterForSelected"] = nodes =>
        {
            _filterBar.ApplyBranchFilter(string.Join(" ", nodes.Select(node => node.FullPath)));
            return Task.CompletedTask;
        },
        ["refs.compareSelected"] = nodes =>
        {
            RefTreeNode first = nodes[0];
            RefTreeNode second = nodes[1];
            if ((first.ObjectId ?? _session.ResolveRef(first.FullPath)) is not ObjectId firstId
                || (second.ObjectId ?? _session.ResolveRef(second.FullPath)) is not ObjectId secondId)
            {
                return ConfirmDialog.ErrorAsync(this, "Compare", "Cannot resolve the selected refs.");
            }

            new CompareWindow(_session, firstId, first.FullPath, secondId, second.FullPath).Show(this);
            return Task.CompletedTask;
        },
    };

    private Dictionary<string, Func<RefTreeNode, Task>> LeftPanelActionHandlers => new()
    {
        // remote repo node
        ["remote.fetch"] = node => WithPanelRefresh(RunOperationAsync($"Fetch {node.Name}", () => _session.FetchRemoteAsync(node.Name, prune: false))),
        ["remote.fetchPrune"] = node => WithPanelRefresh(RunOperationAsync($"Fetch & prune {node.Name}", () => _session.FetchRemoteAsync(node.Name, prune: true))),
        ["remote.activate"] = node => SetRemoteStateAsync(node.Name, disabled: false, fetchAfter: false),
        ["remote.activateFetch"] = node => SetRemoteStateAsync(node.Name, disabled: false, fetchAfter: true),
        ["remote.deactivate"] = node => SetRemoteStateAsync(node.Name, disabled: true, fetchAfter: false),
        ["remote.openUrl"] = node =>
        {
            if (node.Remote?.FetchUrl is string url)
            {
                GitCommands.OsShellUtil.OpenUrlInDefaultBrowser(url);
            }

            return Task.CompletedTask;
        },
        ["remote.manage"] = _ => OpenRemotesWindowAsync(),

        // remotes section
        ["remotes.fetchAll"] = _ => WithPanelRefresh(RunOperationAsync("Fetch all remotes", () => _session.FetchAllAsync(prune: false))),
        ["remotes.fetchPruneAll"] = _ => WithPanelRefresh(RunOperationAsync("Fetch & prune all remotes", () => _session.FetchAllAsync(prune: true))),
        ["remotes.manage"] = _ => OpenRemotesWindowAsync(),

        // stash node
        ["stash.show"] = node => OpenStashManagerAsync(node.FullPath),
        ["stash.apply"] = node => WithPanelRefresh(RunOperationAsync($"Apply {node.FullPath}", () => _session.StashApplyAsync(node.FullPath))),
        ["stash.pop"] = node => WithPanelRefresh(RunOperationAsync($"Pop {node.FullPath}", () => _session.StashPopAsync(node.FullPath))),
        ["stash.drop"] = async node =>
        {
            if (await ConfirmDialog.ConfirmAsync(this, "Drop stash", $"Drop {node.Name}?\nThis cannot be undone."))
            {
                await WithPanelRefresh(RunOperationAsync($"Drop {node.FullPath}", () => _session.StashDropAsync(node.FullPath)));
            }
        },
        ["stash.copyHash"] = node => node.ObjectId is ObjectId stashId ? CopyToClipboardAsync(stashId.ToString()) : Task.CompletedTask,

        // stashes section
        ["stashes.save"] = _ => WithPanelRefresh(RunOperationAsync("Stash changes", () => _session.StashSaveAsync())),
        ["stashes.saveStaged"] = _ => WithPanelRefresh(RunOperationAsync("Stash staged", _session.StashStagedAsync)),
        ["stashes.manage"] = _ => OpenStashManagerAsync(initialSelector: null),

        // submodule node (reset/stash/commit run a session inside the submodule, like the file menu)
        ["submodule.switchTo"] = node => SwitchRepositoryAsync(node.FullPath),
        ["submodule.open"] = node =>
        {
            new MainWindow(node.FullPath).Show();
            return Task.CompletedTask;
        },
        ["submodule.update"] = node => WithPanelRefresh(RunOperationAsync($"Update {node.Name}", () => _session.UpdateSubmoduleAsync(node.Name))),
        ["submodule.reset"] = async node =>
        {
            if (await ConfirmDialog.ConfirmAsync(this, "Reset submodule", $"Reset ALL changes in {node.Name}? Untracked files are kept."))
            {
                await new SliceSession(node.FullPath).ResetAllChangesAsync(clean: false);
                await ReloadLogAsync();
            }
        },
        ["submodule.stash"] = async node =>
        {
            await new SliceSession(node.FullPath).StashSaveAsync();
            await ReloadLogAsync();
        },
        ["submodule.commit"] = async node =>
        {
            CommitWindow submoduleCommit = new(new SliceSession(node.FullPath));
            await submoduleCommit.ShowDialog(this);
            await ReloadLogAsync();
        },

        // submodules section
        ["submodules.updateAll"] = _ => WithPanelRefresh(RunOperationAsync("Update submodules", _session.UpdateSubmodulesAsync)),
        ["submodules.syncAll"] = _ => WithPanelRefresh(RunOperationAsync("Synchronize submodules", _session.SyncSubmodulesAsync)),

        // worktree node
        ["worktree.open"] = node => SwitchRepositoryAsync(node.FullPath),
        ["worktree.copyPath"] = node => CopyToClipboardAsync(node.FullPath),
        ["worktree.showInFolder"] = node =>
        {
            GitCommands.OsShellUtil.Open(node.FullPath);
            return Task.CompletedTask;
        },
        ["worktree.delete"] = async node =>
        {
            if (await ConfirmDialog.ConfirmAsync(this, "Delete worktree", $"Remove the worktree at {node.FullPath}?\nThis cannot be undone."))
            {
                await WithPanelRefresh(RunOperationAsync("Delete worktree", () => _session.RemoveWorktreeAsync(node.FullPath, force: true)));
            }
        },

        // worktrees section
        ["worktrees.create"] = async _ =>
        {
            IReadOnlyList<string> branches = await Task.Run(_session.GetLocalBranchNames);
            var choice = await CreateWorktreeDialog.ShowAsync(this, _session.WorkingDir.TrimEnd('/', '\\'), branches, _session.SelectedBranch);
            if (choice is var (directory, newBranchOption) && choice is not null)
            {
                await WithPanelRefresh(RunOperationAsync("Create worktree", () => _session.CreateWorktreeAsync(directory, newBranchOption)));
            }
        },
        ["worktrees.prune"] = _ => WithPanelRefresh(RunOperationAsync("Prune worktrees", _session.PruneWorktreesAsync)),

        // branch folder
        ["folder.operateOnBranches"] = node => OpenRefOperationsAsync(BranchLeavesUnder(node), checkAll: true),
        ["folder.createBranch"] = async node =>
        {
            string? name = await ConfirmDialog.InputAsync(this, "Create branch", "Branch name:", $"{node.FullPath}/");
            if (!string.IsNullOrWhiteSpace(name) && name.Trim() != node.FullPath)
            {
                await WithPanelRefresh(RunOperationAsync($"Create branch {name}", () => _session.CreateBranchAsync(name.Trim(), checkout: false)));
            }
        },
    };

    /// <summary>All local-branch leaves under a branch folder node, in panel order.</summary>
    private static IReadOnlyList<RefTreeNode> BranchLeavesUnder(RefTreeNode folder)
    {
        List<RefTreeNode> leaves = [];
        Walk(folder);
        return leaves;

        void Walk(RefTreeNode node)
        {
            if (node.Kind is RefTreeNodeKind.LocalBranch)
            {
                leaves.Add(node);
            }

            foreach (RefTreeNode child in node.Children)
            {
                Walk(child);
            }
        }
    }

    /// <summary>Opens the batch-ref operations dialog seeded with the given ref nodes.</summary>
    internal async Task OpenRefOperationsAsync(IReadOnlyList<RefTreeNode> nodes, bool checkAll)
    {
        List<(string Name, GitCommands.Refs.BatchRefKind Kind)> seeds = [.. nodes
            .Where(node => node.Kind is RefTreeNodeKind.LocalBranch or RefTreeNodeKind.RemoteBranch or RefTreeNodeKind.Tag)
            .Select(node => (node.FullPath, node.Kind switch
            {
                RefTreeNodeKind.RemoteBranch => GitCommands.Refs.BatchRefKind.RemoteBranch,
                RefTreeNodeKind.Tag => GitCommands.Refs.BatchRefKind.Tag,
                _ => GitCommands.Refs.BatchRefKind.LocalBranch,
            }))];

        if (seeds.Count == 0)
        {
            return;
        }

        IReadOnlyList<GitCommands.Refs.BatchRefRow> rows = await _session.GetBatchRefRowsAsync(seeds);
        RefOperationsWindow window = new(
            _session,
            rows,
            checkAll ? rows.Select(row => row.Name).ToHashSet() : []);
        await window.ShowDialog(this);
        if (window.RefsChanged)
        {
            await ReloadLogAsync();
            await LoadRefPanelAsync();
        }
    }

    /// <summary>Awaits an operation, then refreshes the sidebar (refs/stashes/worktrees may have changed).</summary>
    private async Task WithPanelRefresh(Task operation)
    {
        await operation;
        await LoadRefPanelAsync();
    }

    private async Task SetRemoteStateAsync(string remoteName, bool disabled, bool fetchAfter)
    {
        GitCommands.Remotes.IConfigFileRemoteSettingsManager manager = _session.CreateRemotesManager();
        await Task.Run(() => manager.ToggleRemoteState(remoteName, disabled));
        if (fetchAfter)
        {
            await RunOperationAsync($"Fetch {remoteName}", () => _session.FetchRemoteAsync(remoteName, prune: false));
        }

        await LoadRefPanelAsync();
    }

    private async Task OpenRemotesWindowAsync()
    {
        RemotesWindow remotesWindow = new(_session);
        await remotesWindow.ShowDialog(this);
        await LoadRefPanelAsync();
    }

    private async Task OpenStashManagerAsync(string? initialSelector)
    {
        StashWindow stashWindow = new(_session, initialSelector);
        await stashWindow.ShowDialog(this);
        if (stashWindow.StashesChanged)
        {
            await ReloadLogAsync();
            await LoadRefPanelAsync();
        }
    }

    /// <summary>Selects the sidebar node whose FullPath matches, if it is present in the current panel.</summary>
    private void SelectRefInSidebar(string fullPath)
    {
        if (RefTree.ItemsSource is not System.Collections.Generic.IEnumerable<RefTreeNode> sections)
        {
            return;
        }

        foreach (RefTreeNode section in sections)
        {
            if (Find(section) is RefTreeNode match)
            {
                RefTree.SelectedItem = match;
                return;
            }
        }

        RefTreeNode? Find(RefTreeNode node)
            => node.FullPath == fullPath ? node : node.Children.Select(Find).FirstOrDefault(found => found is not null);
    }

    internal static ContextMenu BuildMenu(
        IReadOnlyList<ActionDescriptor> actions,
        Func<ActionDescriptor, bool> isImplemented,
        Func<ActionDescriptor, bool> isApplicable,
        Func<ActionDescriptor, Task> execute,
        IReadOnlyDictionary<string, string>? submenuGroups = null)
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
            if (submenuGroups?.TryGetValue(group[0].Action.Group, out string? submenuCaption) is true)
            {
                MenuItem parent = new()
                {
                    Header = Loc.T(submenuCaption!),
                    IsEnabled = group.Any(item => item.Enabled),
                };
                foreach (ProjectedMenuItem item in group)
                {
                    parent.Items.Add(MakeItem(item));
                }

                menu.Items.Add(parent);
                continue;
            }

            foreach (ProjectedMenuItem item in group)
            {
                menu.Items.Add(MakeItem(item));
            }
        }

        return menu;

        MenuItem MakeItem(ProjectedMenuItem item)
        {
            MenuItem menuItem = new()
            {
                Header = Loc.T(item.Action.Caption),
                IsEnabled = item.Enabled,
            };
            ActionDescriptor action = item.Action;
            menuItem.Click += (_, _) => _ = execute(action);
            return menuItem;
        }
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

            GridCommitMenuContext rangeContext = context with { SelectedCount = 2 };
            var rangeGroups = MenuProjector.Project(
                [.. GridMenuRegistry.CommitMenuFor(rangeContext).Where(action => CommitActionHandlers.ContainsKey(action.Id))],
                _menuProfile,
                action => GridMenuRegistry.IsApplicable(action, rangeContext));
            Console.Error.WriteLine($"[menu] range menu (2 selected): {string.Join(" | ", rangeGroups.Select(group => string.Join(", ", group.Select(item => item.Enabled ? item.Action.Caption : $"({item.Action.Caption})"))))}");
        }

        RefMenuContext refContext = new(RefMenuKind.LocalBranch, IsCurrent: false);
        var refGroups = MenuProjector.Project(
            [.. GridMenuRegistry.RefActions.Where(action => RefActionHandlers.ContainsKey(action.Id))],
            _menuProfile,
            action => GridMenuRegistry.IsApplicable(action, refContext));
        Console.Error.WriteLine($"[menu] ref menu (local, not current): {string.Join(" | ", refGroups.Select(group => string.Join(", ", group.Select(item => item.Action.Caption))))}");

        RefMenuContext panelRemoteBranch = new(RefMenuKind.RemoteBranch, FromLeftPanel: true);
        var panelRefGroups = MenuProjector.Project(
            [.. GridMenuRegistry.RefActions.Where(action => RefActionHandlers.ContainsKey(action.Id))],
            _menuProfile,
            action => GridMenuRegistry.IsApplicable(action, panelRemoteBranch));
        Console.Error.WriteLine($"[menu] ref menu (remote branch, left panel): {string.Join(" | ", panelRefGroups.Select(group => string.Join(", ", group.Select(item => item.Enabled ? item.Action.Caption : $"({item.Action.Caption})"))))}");

        foreach ((string label, DiffMenuContext diffContext) in new (string, DiffMenuContext)[]
        {
            ("worktree diff", new DiffMenuContext(HasSelection: true, IsPatchView: true, SupportsLinePatching: true, Target: DiffLineTarget.WorkTree, IsCommitWindow: true)),
            ("committed diff", new DiffMenuContext(HasSelection: true, IsPatchView: true, SupportsLinePatching: true, Target: DiffLineTarget.Committed)),
            ("no selection", new DiffMenuContext(IsPatchView: true)),
        })
        {
            var diffGroups = MenuProjector.Project(
                DiffMenuRegistry.DiffMenuFor(diffContext),
                _menuProfile,
                action => DiffMenuRegistry.IsApplicable(action, diffContext));
            Console.Error.WriteLine($"[menu] diff pane ({label}): {string.Join(" | ", diffGroups.Select(group => string.Join(", ", group.Select(item => item.Enabled ? item.Action.Caption : $"({item.Action.Caption})"))))}");
        }

        Console.Error.WriteLine($"[menu] blame gutter: {string.Join(", ", DiffMenuRegistry.BlameGutterActions.Select(action => action.Caption))}");

        Dictionary<string, Func<RefTreeNode, Task>> panelHandlers = LeftPanelActionHandlers;
        foreach ((string label, IReadOnlyList<ActionDescriptor> actions) in new (string, IReadOnlyList<ActionDescriptor>)[]
        {
            ("remote repo (enabled)", LeftPanelMenuRegistry.RemoteRepoMenuFor(new LeftPanelRemoteContext(RemoteEnabled: true, HasHttpUrl: true))),
            ("remote repo (disabled)", LeftPanelMenuRegistry.RemoteRepoMenuFor(new LeftPanelRemoteContext(RemoteEnabled: false))),
            ("stash node", LeftPanelMenuRegistry.StashNodeActions),
            ("stashes section", LeftPanelMenuRegistry.StashesSectionActions),
            ("submodule node", LeftPanelMenuRegistry.SubmoduleMenuFor(new LeftPanelSubmoduleContext())),
            ("submodules section", LeftPanelMenuRegistry.SubmodulesSectionActions),
            ("worktree node", LeftPanelMenuRegistry.WorktreeNodeActions),
            ("worktrees section", LeftPanelMenuRegistry.WorktreesSectionActions),
            ("branch folder", LeftPanelMenuRegistry.BranchFolderActions),
            ("ref range", LeftPanelMenuRegistry.RefRangeActions),
        })
        {
            var panelGroups = MenuProjector.Project(
                [.. actions.Where(action => panelHandlers.ContainsKey(action.Id) || RefRangeHandlers.ContainsKey(action.Id))],
                _menuProfile,
                _ => true);
            Console.Error.WriteLine($"[menu] left panel {label}: {string.Join(" | ", panelGroups.Select(group => string.Join(", ", group.Select(item => item.Action.Caption))))}");
        }

        // The file tree fills asynchronously after the row selection; wait for the first leaf.
        GitItemStatus? firstFile = null;
        for (int i = 0; i < 20 && firstFile is null; i++)
        {
            await Task.Delay(200);
            firstFile = (FileTree.ItemsSource?.OfType<StatusNode>() ?? []).SelectMany(node => node.DescendantStatuses()).FirstOrDefault();
        }

        if (firstFile is not null && _selectedRevision is GitRevision fileRevision)
        {
            FileMenuContext fileContext = FileMenuContextFor(firstFile, fileRevision);
            Dictionary<string, Func<GitItemStatus, Task>> fileHandlers = FileMenuHandlers(firstFile, fileRevision);
            var fileGroups = MenuProjector.Project(
                [.. FileMenuRegistry.FileMenuFor(fileContext).Where(action => fileHandlers.ContainsKey(action.Id))],
                _menuProfile,
                action => FileMenuRegistry.IsApplicable(action, fileContext));
            Console.Error.WriteLine($"[menu] file menu ({firstFile.Name}): {string.Join(" | ", fileGroups.Select(group => string.Join(", ", group.Select(item => item.Enabled ? item.Action.Caption : $"({item.Action.Caption})"))))}");
        }

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

        System.Collections.Generic.IList<GitCommands.UserRepositoryHistory.Repository> recentRepos =
            await GitCommands.UserRepositoryHistory.RepositoryHistoryManager.Locals.LoadRecentHistoryAsync();
        GitCommands.UserRepositoryHistory.RecentRepositoriesMenuModel recentModel =
            GitCommands.UserRepositoryHistory.RecentRepositoryMenu.BuildRecent(
                recentRepos,
                GitCommands.UserRepositoryHistory.RecentRepoSplitterOptions.FromAppSettings(),
                _recentBranchNames.GetCachedBranchName);
        foreach (GitCommands.UserRepositoryHistory.RepoMenuEntry recentEntry in recentModel.Pinned.Concat(recentModel.Recent))
        {
            string repoPath = recentEntry.Repo.Path;
            entries.Add(($"Open recent: {recentEntry.Caption}", () => OpenRecentAsync(repoPath)));
        }

        entries.Add(("Clone repository...", CloneRepositoryAsync));
        entries.Add(("Create new repository...", InitRepositoryAsync));

        var (branches, remotes, tags) = await Task.Run(_session.GetRefPanel);

        entries.Add(("Operate on refs...", () => OpenRefOperationsAsync(
            [.. Flatten(branches)], checkAll: false)));

        if (!string.IsNullOrEmpty(_browseDiffText))
        {
            // The pane menu's Copy patch is selection-only (reviewed); the whole-document
            // copy is pane-global, so it lives here per the pointer rule.
            entries.Add(("Copy whole patch", () => CopyToClipboardAsync(_browseDiffText!)));
        }
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
