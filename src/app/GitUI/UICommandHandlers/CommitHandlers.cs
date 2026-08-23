using System.Diagnostics;
using GitExtensions.Extensibility.Git;
using GitUI.CommandsDialogs;
using GitUI.HelperDialogs;
using GitUIPluginInterfaces;
using Intent = GitExtensions.Extensibility.Git.UICommands;

namespace GitUI.UICommandHandlers;

internal sealed class CommitHandler(GitUICommands commands) : IUICommandHandler<Intent.Commit>
{
    public bool Execute(Intent.Commit command, IWin32Window? owner)
    {
        if (commands.Module.IsBareRepository())
        {
            return false;
        }

        // Commit dialog can be opened on its own without the main form
        // If it is opened by itself, we need to ensure plugins are loaded because some of them
        // may have hooks into the commit flow
        bool werePluginsRegistered = PluginRegistry.PluginsRegistered;

        try
        {
            // Load plugins synchronously
            // if the commit dialog is opened from the main form, all plugins are already loaded and we return instantly,
            // if the dialog is loaded on its own, plugins need to be loaded before we load the form
            if (!werePluginsRegistered)
            {
                PluginRegistry.InitializeForCommitForm();
                PluginRegistry.Register(commands);
            }
        }
        catch (Exception exception)
        {
            // Nothing: we don't want plugin loading to crash the application here
            Trace.WriteLine(exception);
        }

        bool Action()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            using FormCommit form = new(commands, commitMessage: command.CommitMessage);
            if (command.ShowOnlyWhenChanges)
            {
                form.ShowDialogWhenChanges(owner);
            }
            else
            {
                form.ShowDialog(owner);
            }

            return true;
        }

        try
        {
            return commands.DoActionOnRepo(owner, Action, changesRepo: false, preEvent: commands.PreCommitEvent, postEvent: commands.PostCommitEvent);
        }
        finally
        {
            try
            {
                if (!werePluginsRegistered)
                {
                    PluginRegistry.Unregister(commands);
                }
            }
            catch (Exception exception)
            {
                // Nothing: we don't want plugin loading to crash the application here
                Trace.WriteLine(exception);
            }
        }
    }
}

internal sealed class AmendCommitHandler(GitUICommands commands) : IUICommandHandler<Intent.AmendCommit>
{
    public bool Execute(Intent.AmendCommit command, IWin32Window? owner)
    {
        bool Action()
        {
            using FormCommit form = new(commands, CommitKind.Amend, command.Revision);
            form.ShowDialog(owner);
            return true;
        }

        return commands.DoActionOnRepo(Action);
    }
}

internal sealed class FixupCommitHandler(GitUICommands commands) : IUICommandHandler<Intent.FixupCommit>
{
    public bool Execute(Intent.FixupCommit command, IWin32Window? owner)
    {
        bool Action()
        {
            using FormCommit form = new(commands, CommitKind.Fixup, command.Revision);
            form.ShowDialog(owner);
            return true;
        }

        return commands.DoActionOnRepo(Action);
    }
}

internal sealed class SquashCommitHandler(GitUICommands commands) : IUICommandHandler<Intent.SquashCommit>
{
    public bool Execute(Intent.SquashCommit command, IWin32Window? owner)
    {
        bool Action()
        {
            using FormCommit form = new(commands, CommitKind.Squash, command.Revision);
            form.ShowDialog(owner);
            return true;
        }

        return commands.DoActionOnRepo(Action);
    }
}

internal sealed class RevertCommitHandler(GitUICommands commands) : IUICommandHandler<Intent.RevertCommit>
{
    public bool Execute(Intent.RevertCommit command, IWin32Window? owner)
    {
        bool Action()
        {
            using FormRevertCommit form = new(commands, command.Revision);
            return form.ShowDialog(owner) == DialogResult.OK;
        }

        return commands.DoActionOnRepo(owner, Action);
    }
}

internal sealed class CherryPickHandler(GitUICommands commands) : IUICommandHandler<Intent.CherryPick>
{
    public bool Execute(Intent.CherryPick command, IWin32Window? owner)
    {
        if (command.Revisions is not { Count: > 1 })
        {
            GitRevision? revision = command.Revisions is [GitRevision single] ? single : null;

            bool SingleAction()
            {
                using FormCherryPick form = new(commands, revision);
                return form.ShowDialog(owner) == DialogResult.OK;
            }

            return commands.DoActionOnRepo(owner, SingleAction);
        }

        bool Action()
        {
            FormCherryPick? prevForm = null;

            try
            {
                bool repoChanged = false;

                foreach (GitRevision r in command.Revisions)
                {
                    FormCherryPick frm = new(commands, r);
                    if (prevForm is not null)
                    {
                        frm.CopyOptions(prevForm);
                        prevForm.Dispose();
                    }

                    prevForm = frm;
                    if (frm.ShowDialog(owner) == DialogResult.OK)
                    {
                        repoChanged = true;
                    }
                    else
                    {
                        return repoChanged;
                    }
                }

                return repoChanged;
            }
            finally
            {
                prevForm?.Dispose();
            }
        }

        return commands.DoActionOnRepo(owner, Action);
    }
}

internal sealed class CommitDiffHandler(GitUICommands commands) : IUICommandHandler<Intent.CommitDiff>
{
    public bool Execute(Intent.CommitDiff command, IWin32Window? owner)
    {
        bool Action()
        {
            using FormCommitDiff viewPatch = new(commands, command.ObjectId);
            viewPatch.ShowDialog(null);
            return true;
        }

        return commands.DoActionOnRepo(null, Action, requiresValidWorkingDir: false, changesRepo: false);
    }
}

internal sealed class CreateTagHandler(GitUICommands commands) : IUICommandHandler<Intent.CreateTag>
{
    public bool Execute(Intent.CreateTag command, IWin32Window? owner)
    {
        if (command.Revision?.IsArtificial is true)
        {
            return false;
        }

        bool Action()
        {
            using FormCreateTag form = new(commands, command.Revision?.ObjectId ?? default);
            return form.ShowDialog(owner) == DialogResult.OK;
        }

        return commands.DoActionOnRepo(owner, Action);
    }
}

internal sealed class DeleteTagHandler(GitUICommands commands) : IUICommandHandler<Intent.DeleteTag>
{
    public bool Execute(Intent.DeleteTag command, IWin32Window? owner)
    {
        bool Action()
        {
            using FormDeleteTag form = new(commands, command.Tag);
            return form.ShowDialog(owner) == DialogResult.OK;
        }

        return commands.DoActionOnRepo(owner, Action);
    }
}
