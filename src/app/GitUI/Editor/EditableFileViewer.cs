namespace GitUI.Editor;

/// <summary>
///  The editable variant of <see cref="FileViewer"/> - and the only way to get one: the base
///  viewer is read-only by construction (M5.3). Embedded by the six editing dialogs:
///  FormEditor/FormEdit and the four plain-text config dialogs
///  (.gitignore/.gitattributes/.mailmap/sparse working copy).
/// </summary>
public class EditableFileViewer : FileViewer
{
    public EditableFileViewer()
    {
        SetEditable(true);
    }

    /// <summary>
    ///  Lets a host lock an otherwise editable viewer again - FormVerify shows FormEdit as a
    ///  plain viewer for lost objects. Locking hides the replace UI, like the base construction
    ///  state does.
    /// </summary>
    public bool ReadOnly
    {
        get => IsReadOnly;
        set => SetEditable(!value);
    }
}
