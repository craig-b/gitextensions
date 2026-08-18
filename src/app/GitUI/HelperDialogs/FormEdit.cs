using GitExtensions.Extensibility.Git;

namespace GitUI.HelperDialogs;

public partial class FormEdit : GitModuleForm
{
    public FormEdit(IGitUICommands commands, string text, string filename = "")
        : base(commands)
    {
        InitializeComponent();
        InitializeComplete();
        Viewer.InvokeAndForget(() => Viewer.ViewTextAsync(filename, text));
    }

    public bool IsReadOnly
    {
        get => Viewer.ReadOnly;
        set => Viewer.ReadOnly = value;
    }
}
