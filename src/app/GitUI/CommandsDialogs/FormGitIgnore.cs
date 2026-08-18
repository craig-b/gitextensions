using System.Diagnostics;
using GitCommands;
using GitCommands.Editing;
using GitExtensions.Extensibility.Git;
using GitUI.CommandsDialogs.GitIgnoreDialog;
using ResourceManager;
using UICmd = GitExtensions.Extensibility.Git.UICommands;

namespace GitUI.CommandsDialogs;

public sealed partial class FormGitIgnore : GitModuleForm
{
    private readonly TranslationString _gitignoreOnlyInWorkingDirSupportedCaption =
        new("No working directory");

    private readonly TranslationString _saveFileQuestionCaption =
        new("Save changes?");

    private readonly bool _localExclude;

    private readonly IGitIgnoreDialogModel _dialogModel;
    private readonly RepoDotFileEditor _editor;

    public FormGitIgnore(IGitUICommands commands, bool localExclude)
        : base(commands)
    {
        _localExclude = localExclude;
        InitializeComponent();
        InitializeComplete();

        _dialogModel = CreateGitIgnoreDialogModel(localExclude);
        _editor = RepoDotFileEditor.ForGitIgnore(Module, localExclude);

        Text = _dialogModel.FormCaption;
    }

    private IGitIgnoreDialogModel CreateGitIgnoreDialogModel(bool localExclude)
    {
        if (localExclude)
        {
            return new GitLocalExcludeModel(Module);
        }

        return new GitIgnoreModel(Module);
    }

    private string? ExcludeFile => _editor.FilePath;

    protected override void OnRuntimeLoad(EventArgs e)
    {
        base.OnRuntimeLoad(e);
        LoadGitIgnore();
        _NO_TRANSLATE_GitIgnoreEdit.TextLoaded += GitIgnoreFileLoaded;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void LoadGitIgnore()
    {
        try
        {
            if (_editor.FileExists)
            {
                _NO_TRANSLATE_GitIgnoreEdit.ViewFileAsync(ExcludeFile!);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex.Message);
        }
    }

    private void SaveClick(object sender, EventArgs e)
    {
        SaveGitIgnore();
        Close();
    }

    private bool SaveGitIgnore()
    {
        if (!HasUnsavedChanges() || ExcludeFile is null)
        {
            return false;
        }

        try
        {
            _editor.Save(_NO_TRANSLATE_GitIgnoreEdit.GetText());
            return true;
        }
        catch (Exception ex)
        {
            MessageBoxes.Show(this, _dialogModel.CannotAccessFile + Environment.NewLine + ex.Message,
                _dialogModel.CannotAccessFileCaption, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void FormGitIgnoreFormClosing(object sender, FormClosingEventArgs e)
    {
        if (HasUnsavedChanges())
        {
            switch (MessageBoxes.Show(this, _dialogModel.SaveFileQuestion, _saveFileQuestionCaption.Text,
                                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
            {
                case DialogResult.Yes:
                    if (!SaveGitIgnore())
                    {
                        e.Cancel = true;
                    }

                    break;
                case DialogResult.Cancel:
                    e.Cancel = true;
                    break;
            }
        }
    }

    private void FormGitIgnoreLoad(object sender, EventArgs e)
    {
        if (_editor.IsSupported)
        {
            return;
        }

        MessageBoxes.Show(this, _dialogModel.FileOnlyInWorkingDirSupported, _gitignoreOnlyInWorkingDirSupportedCaption.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        Close();
    }

    private void AddDefaultClick(object sender, EventArgs e)
    {
        string currentFileContent = _NO_TRANSLATE_GitIgnoreEdit.GetText();
        string[] patternsToAdd = GitIgnoreDefaultPatterns.GetPatternsToAdd(currentFileContent, GitIgnoreDefaultPatterns.GetEffectivePatterns());
        if (patternsToAdd.Length == 0)
        {
            return;
        }

        // workaround to prevent GitIgnoreFileLoaded event handling (it causes wrong _originalGitIgnoreFileContent update)
        // TODO: implement in FileViewer separate events for loading text from file and for setting text directly via ViewText
        _NO_TRANSLATE_GitIgnoreEdit.InvokeAndForget(async () =>
            {
                _NO_TRANSLATE_GitIgnoreEdit.TextLoaded -= GitIgnoreFileLoaded;
                await _NO_TRANSLATE_GitIgnoreEdit.ViewTextAsync(
                    ExcludeFile,
                    GitIgnoreDefaultPatterns.Append(currentFileContent, patternsToAdd));
                _NO_TRANSLATE_GitIgnoreEdit.TextLoaded += GitIgnoreFileLoaded;
            });
    }

    private void AddPattern_Click(object sender, EventArgs e)
    {
        SaveGitIgnore();
        UICommands.Execute(new UICmd.AddToGitIgnore(_localExclude, ["*.dll"]), this);
        LoadGitIgnore();
    }

    private bool HasUnsavedChanges() => _editor.HasUnsavedChanges(_NO_TRANSLATE_GitIgnoreEdit.GetText());

    private void GitIgnoreFileLoaded(object? sender, EventArgs e) => _editor.NotifyContentLoaded(_NO_TRANSLATE_GitIgnoreEdit.GetText());

    private void lnkGitIgnorePatterns_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
        OsShellUtil.OpenUrlInDefaultBrowser(@"https://github.com/github/gitignore");
    }

    private void lnkGitIgnoreGenerate_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
        OsShellUtil.OpenUrlInDefaultBrowser(@"https://www.gitignore.io/");
    }

    private void btnCancel_Click(object sender, EventArgs e)
    {
        Close();
    }
}
