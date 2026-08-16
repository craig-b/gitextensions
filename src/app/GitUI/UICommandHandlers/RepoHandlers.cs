using GitExtensions.Extensibility;
using GitUI.CommandsDialogs;
using GitUI.HelperDialogs;
using Intent = GitExtensions.Extensibility.Git.UICommands;

namespace GitUI.UICommandHandlers;

internal sealed class BrowseHandler(GitUICommands commands) : IUICommandHandler<Intent.Browse>
{
    public bool Execute(Intent.Browse command, IWin32Window? owner)
    {
        FormBrowse form = new(commands, command.Args ?? new BrowseArguments());

        if (Application.MessageLoop)
        {
            form.Show(owner);
        }
        else
        {
            Application.Run(form);
        }

        return true;
    }
}

internal sealed class CloneHandler(GitUICommands commands) : IUICommandHandler<Intent.Clone>
{
    public bool Execute(Intent.Clone command, IWin32Window? owner)
    {
        bool Action()
        {
            using FormClone form = new(commands, command.Url, command.OpenedFromProtocolHandler, command.GitModuleChanged);
            form.ShowDialog(owner);
            return true;
        }

        return commands.DoActionOnRepo(owner, Action, requiresValidWorkingDir: false, changesRepo: false);
    }
}

internal sealed class InitializeRepositoryHandler(GitUICommands commands) : IUICommandHandler<Intent.InitializeRepository>
{
    public bool Execute(Intent.InitializeRepository command, IWin32Window? owner)
    {
        bool Action()
        {
            string dir = command.Directory ?? (commands.Module.IsValidGitWorkingDir() ? commands.Module.WorkingDir : string.Empty);

            using FormInit frm = new(commands, dir, command.GitModuleChanged);
            frm.ShowDialog(owner);
            return true;
        }

        return commands.DoActionOnRepo(owner, Action, requiresValidWorkingDir: false, changesRepo: false);
    }
}

internal sealed class CleanupRepositoryHandler(GitUICommands commands) : IUICommandHandler<Intent.CleanupRepository>
{
    public bool Execute(Intent.CleanupRepository command, IWin32Window? owner)
    {
        using FormCleanupRepository form = new(commands);
        form.SetPathArgument(command.Path);
        form.ShowDialog(owner);

        return true;
    }
}

internal sealed class CompareRevisionsHandler(GitUICommands commands) : IUICommandHandler<Intent.CompareRevisions>
{
    public bool Execute(Intent.CompareRevisions command, IWin32Window? owner)
    {
        bool Action()
        {
            using FormLog form = new(commands);
            return form.ShowDialog(owner) == DialogResult.OK;
        }

        return commands.DoActionOnRepo(owner, Action);
    }
}

internal sealed class VerifyDatabaseHandler(GitUICommands commands) : IUICommandHandler<Intent.VerifyDatabase>
{
    public bool Execute(Intent.VerifyDatabase command, IWin32Window? owner)
    {
        bool Action()
        {
            using FormVerify form = new(commands);
            form.ShowDialog(owner);
            return true;
        }

        // TODO: move Notify to FormVerify and friends
        return commands.DoActionOnRepo(owner, Action);
    }
}

internal sealed class RemotesHandler(GitUICommands commands) : IUICommandHandler<Intent.Remotes>
{
    public bool Execute(Intent.Remotes command, IWin32Window? owner)
    {
        bool Action()
        {
            using FormRemotes form = new(commands)
            {
                PreselectRemoteOnLoad = command.PreselectRemote,
                PreselectLocalOnLoad = command.PreselectLocal
            };
            form.ShowDialog(owner);
            return true;
        }

        return commands.DoActionOnRepo(owner, Action);
    }
}

internal sealed class ResolveConflictsHandler(GitUICommands commands) : IUICommandHandler<Intent.ResolveConflicts>
{
    public bool Execute(Intent.ResolveConflicts command, IWin32Window? owner)
    {
        bool Action()
        {
            using FormResolveConflicts form = new(commands, command.OfferCommit);
            form.ShowDialog(owner);
            return true;
        }

        return commands.DoActionOnRepo(owner, Action);
    }
}

internal sealed class ResetChangesHandler(GitUICommands commands) : IUICommandHandler<Intent.ResetChanges>
{
    public bool Execute(Intent.ResetChanges command, IWin32Window? owner)
    {
        // Show a form asking the user if they want to reset the changes.
        FormResetChanges.ActionEnum resetType = FormResetChanges.ShowResetDialog(owner, hasExistingFiles: command.WorkTreeFiles.Any(item => !item.IsNew), hasNewFiles: command.WorkTreeFiles.Any(item => item.IsNew));

        if (resetType == FormResetChanges.ActionEnum.Cancel)
        {
            return false;
        }

        return commands.DoActionOnRepo(owner, Action);

        bool Action()
        {
            return commands.Module.ResetAllChanges(clean: resetType == FormResetChanges.ActionEnum.ResetAndDelete, command.OnlyWorkTree);
        }
    }
}

internal sealed class ArchiveHandler(GitUICommands commands) : IUICommandHandler<Intent.Archive>
{
    public bool Execute(Intent.Archive command, IWin32Window? owner)
    {
        return commands.DoActionOnRepo(owner, action: () =>
            {
                using FormArchive form = new(commands)
                {
                    SelectedRevision = command.Revision,
                };
                form.SetDiffSelectedRevision(command.Revision2);
                form.SetPathArgument(command.Path);
                form.ShowDialog(owner);

                return true;
            }, changesRepo: false);
    }
}

internal sealed class FormatPatchHandler(GitUICommands commands) : IUICommandHandler<Intent.FormatPatch>
{
    public bool Execute(Intent.FormatPatch command, IWin32Window? owner)
    {
        bool Action()
        {
            using FormFormatPatch form = new(commands);
            form.ShowDialog(owner);
            return true;
        }

        return commands.DoActionOnRepo(owner, Action, changesRepo: false);
    }
}

internal sealed class ApplyPatchHandler(GitUICommands commands) : IUICommandHandler<Intent.ApplyPatch>
{
    public bool Execute(Intent.ApplyPatch command, IWin32Window? owner)
    {
        return commands.DoActionOnRepo(owner, action: () =>
            {
                using FormApplyPatch form = new(commands);
                if (Directory.Exists(command.PatchFile!))
                {
                    form.SetPatchDir(command.PatchFile!);
                }
                else
                {
                    form.SetPatchFile(command.PatchFile ?? "");
                }

                form.ShowDialog(owner);

                return true;
            }, changesRepo: false);
    }
}

internal sealed class ViewPatchHandler(GitUICommands commands) : IUICommandHandler<Intent.ViewPatch>
{
    public bool Execute(Intent.ViewPatch command, IWin32Window? owner)
    {
        bool Action()
        {
            using FormViewPatch viewPatch = new(commands);
            if (!string.IsNullOrEmpty(command.PatchFile))
            {
                viewPatch.LoadPatch(command.PatchFile);
            }

            viewPatch.ShowDialog(owner);

            return true;
        }

        return commands.DoActionOnRepo(owner, Action, requiresValidWorkingDir: false, changesRepo: false);
    }
}

internal sealed class AddFilesHandler(GitUICommands commands) : IUICommandHandler<Intent.AddFiles>
{
    public bool Execute(Intent.AddFiles command, IWin32Window? owner)
    {
        return commands.DoActionOnRepo(owner, action: () =>
        {
            using FormAddFiles form = new(commands, command.Files);
            form.ShowDialog(owner);
            return true;
        });
    }
}

internal sealed class AddToGitIgnoreHandler(GitUICommands commands) : IUICommandHandler<Intent.AddToGitIgnore>
{
    public bool Execute(Intent.AddToGitIgnore command, IWin32Window? owner)
    {
        bool Action()
        {
            using FormAddToGitIgnore frm = new(commands, command.LocalExclude, [.. command.FilePatterns]);
            frm.ShowDialog(owner);
            return true;
        }

        return commands.DoActionOnRepo(owner, Action, changesRepo: false, postEvent: commands.PostEditGitIgnoreEvent);
    }
}

internal sealed class EditGitIgnoreHandler(GitUICommands commands) : IUICommandHandler<Intent.EditGitIgnore>
{
    public bool Execute(Intent.EditGitIgnore command, IWin32Window? owner)
    {
        bool Action()
        {
            using FormGitIgnore form = new(commands, command.LocalExcludes);
            form.ShowDialog(owner);
            return true;
        }

        return commands.DoActionOnRepo(owner, Action, changesRepo: false, postEvent: commands.PostEditGitIgnoreEvent);
    }
}

internal sealed class EditGitAttributesHandler(GitUICommands commands) : IUICommandHandler<Intent.EditGitAttributes>
{
    public bool Execute(Intent.EditGitAttributes command, IWin32Window? owner)
    {
        bool Action()
        {
            using FormGitAttributes form = new(commands);
            form.ShowDialog(owner);
            return true;
        }

        return commands.DoActionOnRepo(owner, Action, changesRepo: false);
    }
}

internal sealed class MailMapHandler(GitUICommands commands) : IUICommandHandler<Intent.MailMap>
{
    public bool Execute(Intent.MailMap command, IWin32Window? owner)
    {
        bool Action()
        {
            using FormMailMap form = new(commands);
            form.ShowDialog(owner);
            return true;
        }

        return commands.DoActionOnRepo(owner, Action, changesRepo: false);
    }
}

internal sealed class SparseWorkingCopyHandler(GitUICommands commands) : IUICommandHandler<Intent.SparseWorkingCopy>
{
    public bool Execute(Intent.SparseWorkingCopy command, IWin32Window? owner)
    {
        bool Action()
        {
            using FormSparseWorkingCopy form = new(commands);
            form.ShowDialog(owner);
            return true;
        }

        return commands.DoActionOnRepo(owner, Action, changesRepo: false);
    }
}
