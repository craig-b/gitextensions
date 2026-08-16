using GitExtensions.Extensibility;

namespace GitUI;

/// <summary>
///  <see cref="IUserInteraction"/> bound to a WinForms control: switches to the UI thread and
///  shows message boxes owned by that control.
/// </summary>
public sealed class ControlUserInteraction(Control owner) : IUserInteraction
{
    public async Task ShowErrorAsync(string text, string caption, CancellationToken cancellationToken = default)
    {
        await owner.SwitchToMainThreadAsync(cancellationToken: cancellationToken);
        MessageBoxes.Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
    }
}
