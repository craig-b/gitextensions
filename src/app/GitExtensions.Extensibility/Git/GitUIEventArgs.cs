using System.ComponentModel;

namespace GitExtensions.Extensibility.Git;

public class GitUIEventArgs : CancelEventArgs
{
    private readonly IFilteredGitRefsProvider _getRefs;

    public GitUIEventArgs(object? ownerForm, IGitUICommands gitUICommands, Lazy<IReadOnlyList<IGitRef>>? getRefs = null)
        : base(cancel: false)
    {
        OwnerForm = ownerForm;
        GitUICommands = gitUICommands;
        if (getRefs is null)
        {
            _getRefs = new FilteredGitRefsProvider(GitModule);
        }
        else
        {
            _getRefs = new FilteredGitRefsProvider(getRefs);
        }
    }

    public IGitUICommands GitUICommands { get; }

    /// <summary>
    ///  The host-specific owner window for dialogs raised by this event (an <c>IWin32Window</c> in the WinForms host).
    /// </summary>
    public object? OwnerForm { get; }

    public IGitModule GitModule => GitUICommands.Module;

    public IReadOnlyList<IGitRef> GetRefs(RefsFilter filter) => _getRefs.GetRefs(filter);
}
