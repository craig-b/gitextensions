namespace GitUI;

/// <summary>
///  Modal folder selection, previously part of <c>OsShellUtil</c> in the git engine.
/// </summary>
public static class FolderPicker
{
    /// <summary>
    ///  Prompts the user to select a directory.
    /// </summary>
    /// <param name="ownerWindow">The owner window.</param>
    /// <param name="selectedPath">The initially selected path.</param>
    /// <returns>The path selected by the user, or <see langword="null"/> if the user cancels the dialog.</returns>
    public static string? PickFolder(IWin32Window ownerWindow, string? selectedPath = null)
    {
        using FolderBrowserDialog dialog = new();
        if (selectedPath is not null)
        {
            dialog.SelectedPath = selectedPath;
        }

        DialogResult result = dialog.ShowDialog(ownerWindow);
        if (result == DialogResult.OK)
        {
            return dialog.SelectedPath;
        }

        // return null if the user cancelled
        return null;
    }
}
