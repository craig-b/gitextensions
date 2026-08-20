using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using GitCommands;
using GitCommands.Settings.Pages;

namespace GitExtensions.Avalonia;

/// <summary>
///  The settings dialog, rendered generically from the portable page models: every
///  <see cref="SettingsEntry"/> kind maps to an editor (checkbox, numeric, text, combo).
///  Apply pushes values through the models and flushes <see cref="AppSettings"/> to disk.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly List<(string Label, SettingsPageModel Page, Action? AfterSave)> _pages;

    private readonly Dictionary<SettingsEntry, Control> _editors = [];
    private readonly Dictionary<SettingsPageModel, Control> _builtPages = [];
    private readonly Dictionary<SettingsPageModel, List<Action>> _pullValueActions = [];

    public SettingsWindow(SliceSession session)
    {
        InitializeComponent();

        // the git-config pages write at the global level; the WinForms level-switch
        // header (effective/local/global/system) is a client follow-on
        GitCommands.Settings.GitConfigSettings globalGitConfig = new(session.Module.GitExecutable, GitExtensions.Extensibility.Git.GitSettingLevel.Global);
        GitExtensions.Extensibility.Settings.SettingsSource gitConfigSource =
            new GitCommands.Settings.SettingsSource<GitExtensions.Extensibility.Configurations.IPersistentConfigValueStore>(globalGitConfig);
        Action saveGitConfig = globalGitConfig.Save;

        _pages =
        [
            ("General", new GeneralPageModel(), null),
            ("Commit dialog", new CommitDialogPageModel(), null),
            ("Confirmations", new ConfirmationsPageModel(), null),
            ("Appearance", new AppearancePageModel(), null),
            ("Advanced", new AdvancedPageModel(), null),
            ("Detailed", new DetailedPageModel(() => AppSettings.SettingsContainer), null),
            ("Diff viewer", new DiffViewerPageModel(), null),
            ("Blame viewer", new BlameViewerPageModel(), null),
            ("Sorting", new SortingPageModel(), null),
            ("Browse repository window", new BrowseRepoPageModel(), null),
            ("Menus", new MenusPageModel(), null),
            ("Git: Paths", new GitPathsPageModel(), null),
            ("Git: Config", new GitConfigPageModel(() => gitConfigSource, () => true), saveGitConfig),
            ("Git: Advanced", new GitConfigAdvancedPageModel(() => gitConfigSource), saveGitConfig),
        ];

        _visiblePages = _pages;
        PageList.ItemsSource = _pages.Select(page => page.Label).ToList();
        PageList.SelectedIndex = 0;

        ImportButton.IsEnabled = System.IO.File.Exists(LegacyXmlSettingsFilePath);
    }

    /// <summary>
    ///  The XML settings file this app used before the INI store (and, on installs where the
    ///  directories coincide, the WinForms app's file) - the import source.
    /// </summary>
    private static string LegacyXmlSettingsFilePath
        => System.IO.Path.Join(AppSettings.ApplicationDataPath.Value, AppSettings.ApplicationId + ".settings");

    /// <summary>
    ///  One-shot key-for-key copy of the legacy XML settings into this client's store
    ///  (both stores share the same setting keys and string encodings).
    /// </summary>
    private void OnImportClick(object? sender, RoutedEventArgs e)
    {
        if (!System.IO.File.Exists(LegacyXmlSettingsFilePath))
        {
            ImportButton.IsEnabled = false;
            return;
        }

        IReadOnlyList<KeyValuePair<string, string>> pairs;
        using (GitCommands.Settings.GitExtSettingsCache xmlCache = new(LegacyXmlSettingsFilePath, autoSave: false))
        {
            pairs = xmlCache.GetAllValues();
        }

        foreach ((string key, string value) in pairs)
        {
            AppSettings.SettingsContainer.SettingsCache.SetValue(key, value);
        }

        AppSettings.SaveSettings();

        // rebuild every shown page so the dialog reflects the imported values
        SettingsPageModel? current = PageList.SelectedIndex >= 0 && PageList.SelectedIndex < _visiblePages.Count
            ? _visiblePages[PageList.SelectedIndex].Page
            : null;
        _builtPages.Clear();
        _pullValueActions.Clear();
        _editors.Clear();
        if (current is not null)
        {
            ShowPage(current);
        }

        ImportButton.Content = $"Imported {pairs.Count} settings";
        ImportButton.IsEnabled = false;
    }

    private List<(string Label, SettingsPageModel Page, Action? AfterSave)> _visiblePages;

    private void OnFindTextChanged(object? sender, TextChangedEventArgs e)
    {
        string searchText = FindBox.Text ?? string.Empty;
        SettingsPageModel? selected = PageList.SelectedIndex >= 0 && PageList.SelectedIndex < _visiblePages.Count
            ? _visiblePages[PageList.SelectedIndex].Page
            : null;

        _visiblePages = string.IsNullOrWhiteSpace(searchText)
            ? _pages
            : _pages.Where(entry => SettingsPageSearch.Matches(searchText, entry.Page.Title, entry.Page.GetSearchKeywords())).ToList();

        PageList.ItemsSource = _visiblePages.Select(page => page.Label).ToList();

        int keepIndex = _visiblePages.FindIndex(entry => entry.Page == selected);
        PageList.SelectedIndex = keepIndex >= 0 ? keepIndex : (_visiblePages.Count > 0 ? 0 : -1);
    }

    private void OnPageSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        int index = PageList.SelectedIndex;
        if (index >= 0 && index < _visiblePages.Count)
        {
            ShowPage(_visiblePages[index].Page);
        }
    }

    private void ShowPage(SettingsPageModel page)
    {
        if (!_builtPages.TryGetValue(page, out Control? content))
        {
            page.Load();
            content = BuildPage(page);
            _builtPages[page] = content;
        }

        PagePanel.Children.Clear();
        PagePanel.Children.Add(content);
    }

    private StackPanel BuildPage(SettingsPageModel page)
    {
        List<Action> pullValues = _pullValueActions[page] = [];
        StackPanel panel = new() { Spacing = 6 };

        foreach (SettingsGroup group in page.Groups)
        {
            panel.Children.Add(new TextBlock
            {
                Text = group.Caption,
                FontWeight = FontWeight.Bold,
                Margin = new global::Avalonia.Thickness(0, panel.Children.Count == 0 ? 0 : 10, 0, 2),
            });

            foreach (SettingsEntry entry in group.Entries)
            {
                panel.Children.Add(BuildEditor(entry, pullValues));
            }
        }

        return panel;
    }

    private Control BuildEditor(SettingsEntry entry, List<Action> pullValues)
    {
        switch (entry)
        {
            case BoolSettingsEntry boolEntry:
            {
                CheckBox checkBox = MakeCheckBox(entry.Caption, boolEntry.Value, isThreeState: false);
                pullValues.Add(() => boolEntry.Value = checkBox.IsChecked ?? false);
                _editors[entry] = checkBox;
                return Indent(checkBox);
            }

            case TriStateSettingsEntry triStateEntry:
            {
                CheckBox checkBox = MakeCheckBox(entry.Caption, triStateEntry.Value, isThreeState: true);
                pullValues.Add(() => triStateEntry.Value = checkBox.IsChecked);
                _editors[entry] = checkBox;
                return Indent(checkBox);
            }

            case NumberSettingsEntry numberEntry:
            {
                NumericUpDown numeric = MakeNumeric(numberEntry.Minimum, numberEntry.Maximum, numberEntry.Increment, numberEntry.Value);
                pullValues.Add(() => numberEntry.Value = (int)(numeric.Value ?? numberEntry.Value));
                _editors[entry] = numeric;
                return CaptionedRow(entry.Caption, numeric);
            }

            case OptionalNumberSettingsEntry optionalNumberEntry:
            {
                CheckBox gate = MakeCheckBox(entry.Caption, optionalNumberEntry.Enabled, isThreeState: false);
                NumericUpDown numeric = MakeNumeric(optionalNumberEntry.Minimum, optionalNumberEntry.Maximum, optionalNumberEntry.Increment, optionalNumberEntry.Number);
                numeric.IsEnabled = optionalNumberEntry.Enabled;
                gate.IsCheckedChanged += (_, _) => numeric.IsEnabled = gate.IsChecked == true;
                pullValues.Add(() =>
                {
                    optionalNumberEntry.Enabled = gate.IsChecked == true;
                    optionalNumberEntry.Number = (int)(numeric.Value ?? optionalNumberEntry.Number);
                });
                _editors[entry] = gate;
                return Indent(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { gate, numeric },
                });
            }

            case StringSettingsEntry stringEntry:
            {
                TextBox textBox = new() { Text = stringEntry.Value, MinWidth = 280 };
                pullValues.Add(() => stringEntry.Value = textBox.Text ?? string.Empty);
                _editors[entry] = textBox;
                return CaptionedRow(entry.Caption, textBox);
            }

            case ChoiceSettingsEntry choiceEntry:
            {
                ComboBox comboBox = new()
                {
                    ItemsSource = choiceEntry.Choices,
                    SelectedIndex = choiceEntry.SelectedIndex,
                    MinWidth = 200,
                };
                pullValues.Add(() =>
                {
                    if (comboBox.SelectedIndex >= 0)
                    {
                        choiceEntry.SelectedIndex = comboBox.SelectedIndex;
                    }
                });
                _editors[entry] = comboBox;
                return CaptionedRow(entry.Caption, comboBox);
            }

            default:
                return Indent(new TextBlock { Text = entry.Caption });
        }
    }

    private static CheckBox MakeCheckBox(string caption, bool? isChecked, bool isThreeState)
        => new()
        {
            Content = caption,
            IsChecked = isChecked,
            IsThreeState = isThreeState,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

    private static NumericUpDown MakeNumeric(int minimum, int maximum, int increment, int value)
        => new()
        {
            Minimum = minimum,
            Maximum = maximum,
            Increment = increment,
            Value = value,
            FormatString = "0",
            MinWidth = 130,
        };

    private static Control Indent(Control control)
    {
        control.Margin = new global::Avalonia.Thickness(12, 0, 0, 0);
        return control;
    }

    private static Control CaptionedRow(string caption, Control editor)
        => Indent(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = caption, VerticalAlignment = VerticalAlignment.Center },
                editor,
            },
        });

    /// <summary>Pushes every shown page's editor states through its model and persists.</summary>
    private void Apply()
    {
        foreach ((_, SettingsPageModel page, Action? afterSave) in _pages)
        {
            if (!_pullValueActions.TryGetValue(page, out List<Action>? pullValues))
            {
                continue;
            }

            foreach (Action pullValue in pullValues)
            {
                pullValue();
            }

            page.Save();
            afterSave?.Invoke();
        }

        AppSettings.SaveSettings();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        Apply();
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();

    private void OnApplyClick(object? sender, RoutedEventArgs e) => Apply();

    /// <summary>
    ///  Verification harness (GE_SPIKE_SETTINGSTEST): snapshot the General page, switch to
    ///  Confirmations, toggle "Amend last commit", apply, prove the storage flip and the
    ///  file flush, restore, snapshot, close.
    /// </summary>
    internal async Task RunHarnessAsync(string snapshotDirectory)
    {
        await Task.Delay(800);
        Snapshot(System.IO.Path.Combine(snapshotDirectory, "settings_general.png"));

        ConfirmationsPageModel page = _pages.Select(entry => entry.Page).OfType<ConfirmationsPageModel>().Single();
        PageList.SelectedIndex = _pages.FindIndex(entry => entry.Page == page);
        await Task.Delay(400);
        Snapshot(System.IO.Path.Combine(snapshotDirectory, "settings_confirmations.png"));

        CheckBox amend = (CheckBox)_editors[page.AmendLastCommit];
        bool? original = amend.IsChecked;

        amend.IsChecked = !(original ?? false);
        Apply();
        Console.Error.WriteLine($"[settings] after toggle: DontConfirmAmend={AppSettings.DontConfirmAmend}");

        ConfirmationsPageModel reread = new();
        reread.Load();
        Console.Error.WriteLine($"[settings] fresh model sees AmendLastCommit={reread.AmendLastCommit.Value}");

        amend.IsChecked = original;
        Apply();
        Console.Error.WriteLine($"[settings] after restore: DontConfirmAmend={AppSettings.DontConfirmAmend}");

        PageList.SelectedIndex = _pages.FindIndex(entry => entry.Page is GitConfigPageModel);
        await Task.Delay(400);
        Snapshot(System.IO.Path.Combine(snapshotDirectory, "settings_gitconfig.png"));

        GitConfigPageModel gitConfig = (GitConfigPageModel)_pages.Single(entry => entry.Page is GitConfigPageModel).Page;
        Console.Error.WriteLine($"[settings] git config global user.name loads as: '{gitConfig.UserName.Value}'");

        Console.Error.WriteLine($"[settings] store: {AppSettings.SettingsFilePath} (exists: {System.IO.File.Exists(AppSettings.SettingsFilePath)})");
        Console.Error.WriteLine($"[settings] import button enabled: {ImportButton.IsEnabled}");

        FindBox.Text = "stash";
        await Task.Delay(300);
        Console.Error.WriteLine($"[settings] search 'stash' shows: {string.Join(", ", _visiblePages.Select(entry => entry.Label))}");
        Snapshot(System.IO.Path.Combine(snapshotDirectory, "settings_search.png"));
        FindBox.Text = "";

        Close();

        void Snapshot(string path)
        {
            global::Avalonia.PixelSize size = new((int)Bounds.Width, (int)Bounds.Height);
            using global::Avalonia.Media.Imaging.RenderTargetBitmap bitmap = new(size);
            bitmap.Render(this);
            bitmap.Save(path);
        }
    }
}
