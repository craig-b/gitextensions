using GitCommands;
using GitExtensions.Extensibility.Git;
using GitExtensions.Extensibility.Translations;
using ResourceManager;

namespace GitUI.CommandsDialogs.GitIgnoreDialog;

public class GitIgnoreModel : Translate, IGitIgnoreDialogModel
{
    private readonly TranslationString _editGitignoreTitle =
        new("Edit .gitignore");

    private readonly TranslationString _gitignoreOnlyInWorkingDirSupported =
        new(".gitignore is only supported when there is a working directory.");

    private readonly TranslationString _cannotAccessGitignore =
        new("Failed to save .gitignore." + Environment.NewLine + "Check if file is accessible.");

    private readonly TranslationString _cannotAccessGitignoreCaption =
        new("Failed to save .gitignore");

    private readonly TranslationString _saveFileQuestion =
        new("Save changes to .gitignore?");

    public GitIgnoreModel(IGitModule module)
    {
        Translator.Translate(this, AppSettings.CurrentTranslation);
    }

    public string FormCaption => _editGitignoreTitle.Text;

    public string FileOnlyInWorkingDirSupported => _gitignoreOnlyInWorkingDirSupported.Text;

    public string CannotAccessFile => _cannotAccessGitignore.Text;

    public string CannotAccessFileCaption => _cannotAccessGitignoreCaption.Text;

    public string SaveFileQuestion => _saveFileQuestion.Text;
}
