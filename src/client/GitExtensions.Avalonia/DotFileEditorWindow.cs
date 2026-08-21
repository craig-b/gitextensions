using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using GitCommands.Editing;

namespace GitExtensions.Avalonia;

/// <summary>The repository dot files the editor window handles.</summary>
internal enum RepoDotFile
{
    GitIgnore,
    LocalExclude,
    GitAttributes,
    MailMap,
}

/// <summary>
///  The dot-file editor (.gitignore, .git/info/exclude, .gitattributes, .mailmap) over the
///  portable RepoDotFileEditor model: same path resolution, dirty tracking, and save semantics
///  as the WinForms dialogs.
/// </summary>
internal sealed class DotFileEditorWindow : Window
{
    private readonly RepoDotFileEditor _editor;
    private readonly TextBox _text;

    public static async Task ShowAsync(Window owner, SliceSession session, RepoDotFile kind)
    {
        (RepoDotFileEditor editor, string title) = kind switch
        {
            RepoDotFile.GitIgnore => (RepoDotFileEditor.ForGitIgnore(session.Module, localExclude: false), Loc.T("Edit .gitignore")),
            RepoDotFile.LocalExclude => (RepoDotFileEditor.ForLocalExclude(session.Module), Loc.T("Edit .git/info/exclude")),
            RepoDotFile.GitAttributes => (RepoDotFileEditor.ForWorkTreeFile(session.Module, ".gitattributes"), Loc.T("Edit .gitattributes")),
            RepoDotFile.MailMap => (RepoDotFileEditor.ForWorkTreeFile(session.Module, ".mailmap"), Loc.T("Edit .mailmap")),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        if (!editor.IsSupported)
        {
            await ConfirmDialog.ErrorAsync(owner, title, Loc.T("Editing this file is not supported in a bare repository."));
            return;
        }

        DotFileEditorWindow window = new(editor, title, showDefaultIgnores: kind is RepoDotFile.GitIgnore or RepoDotFile.LocalExclude);
        await window.ShowDialog(owner);
    }

    private DotFileEditorWindow(RepoDotFileEditor editor, string title, bool showDefaultIgnores)
    {
        _editor = editor;

        Title = title;
        Width = 640;
        Height = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        string content = editor.FileExists ? File.ReadAllText(editor.FilePath!) : string.Empty;
        editor.NotifyContentLoaded(content);

        _text = new TextBox
        {
            Text = content,
            AcceptsReturn = true,
            FontFamily = new global::Avalonia.Media.FontFamily("monospace"),
            TextWrapping = global::Avalonia.Media.TextWrapping.NoWrap,
            VerticalContentAlignment = VerticalAlignment.Top,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(_text, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(_text, ScrollBarVisibility.Auto);

        Button save = new() { Content = Loc.T("Save"), MinWidth = 90 };
        save.Click += async (_, _) => await SaveAsync();

        Button cancel = new() { Content = Loc.T("Cancel"), MinWidth = 90, IsCancel = true };
        cancel.Click += async (_, _) => await CancelAsync();

        StackPanel leftButtons = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (showDefaultIgnores)
        {
            Button addDefaults = new() { Content = Loc.T("Add default ignores") };
            addDefaults.Click += (_, _) => AddDefaultIgnores();
            leftButtons.Children.Add(addDefaults);
        }

        Content = new Grid
        {
            Margin = new global::Avalonia.Thickness(16),
            RowDefinitions = new RowDefinitions("Auto,8,*,12,Auto"),
            Children =
            {
                At(new TextBlock
                {
                    Text = editor.FilePath ?? "",
                    FontSize = 12,
                    Foreground = global::Avalonia.Media.Brushes.Gray,
                    TextWrapping = global::Avalonia.Media.TextWrapping.Wrap,
                }, row: 0),
                At(_text, row: 2),
                At(new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    Children =
                    {
                        leftButtons,
                        AtColumn(new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Spacing = 8,
                            Children = { save, cancel },
                        }, column: 1),
                    },
                }, row: 4),
            },
        };
    }

    private static Control At(Control control, int row)
    {
        Grid.SetRow(control, row);
        return control;
    }

    private static Control AtColumn(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private void AddDefaultIgnores()
    {
        string current = _text.Text ?? "";
        string[] patternsToAdd = GitIgnoreDefaultPatterns.GetPatternsToAdd(current, GitIgnoreDefaultPatterns.GetEffectivePatterns());
        if (patternsToAdd.Length > 0)
        {
            _text.Text = GitIgnoreDefaultPatterns.Append(current, patternsToAdd);
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            _editor.Save(_text.Text ?? "");
            Close();
        }
        catch (Exception exception)
        {
            await ConfirmDialog.ErrorAsync(this, Title ?? "", exception.Message);
        }
    }

    private async Task CancelAsync()
    {
        if (_editor.HasUnsavedChanges(_text.Text ?? "")
            && !await ConfirmDialog.ConfirmAsync(this, Title ?? "", Loc.T("Discard unsaved changes?")))
        {
            return;
        }

        Close();
    }
}
