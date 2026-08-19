using GitCommands;
using GitExtensions.Extensibility.Translations;
using GitUI;

namespace ResourceManager;

internal sealed class GitExtensionsControlInitialiser
{
    static GitExtensionsControlInitialiser()
    {
        // Every WinForms control/form translation runs through this initialiser, so this is the
        // choke point for installing the WinForms special cases of the translation walk (M7).
        GitExtensions.Extensibility.WinForms.Translations.WinFormsTranslationSpecialCases.Install();
    }

    private static bool? _isDesignMode;
    private readonly ITranslate _translate = null!;

    // Indicates whether the initialisation has been signalled as complete.
    private bool _initialiseCompleteCalled;

    public GitExtensionsControlInitialiser(GitExtensionsFormBase form)
    {
        if (IsDesignMode)
        {
            return;
        }

        ThreadHelper.ThrowIfNotOnUIThread();
        form.Load += LoadHandler;
        _translate = form;
    }

    public GitExtensionsControlInitialiser(TranslatedControl control)
    {
        if (IsDesignMode)
        {
            return;
        }

        ThreadHelper.ThrowIfNotOnUIThread();
        control.Load += LoadHandler;
        _translate = control;
    }

    /// <summary>
    /// Indicates whether code is running as part of an IDE designer, such as the WinForms designer.
    /// </summary>
    public bool IsDesignMode
    {
        get
        {
            if (_isDesignMode is null)
            {
                string processName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);
                _isDesignMode = processName.Contains("devenv", StringComparison.OrdinalIgnoreCase) || processName.Contains("designtoolsserver", StringComparison.OrdinalIgnoreCase);
            }

            return _isDesignMode.Value;
        }
    }

    public void InitializeComplete()
    {
        if (IsDesignMode)
        {
            return;
        }

        if (_initialiseCompleteCalled)
        {
            throw new InvalidOperationException($"{nameof(InitializeComplete)} already called.");
        }

        _initialiseCompleteCalled = true;

        ((Control)_translate).Font = AppFonts.App;
        Translator.Translate(_translate, AppSettings.CurrentTranslation);
    }

    private void LoadHandler(object? control, EventArgs e)
    {
        if (!_initialiseCompleteCalled)
        {
            throw new Exception($"{control?.GetType().Name} must call {nameof(InitializeComplete)} in its constructor, ideally as the final statement.");
        }
    }
}
