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
///  The settings dialog, rendered generically from the portable page models: each
///  <see cref="SettingsPageModel"/> becomes a page of group captions and checkboxes.
///  Apply pushes values through the models and flushes <see cref="AppSettings"/> to disk.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly List<SettingsPageModel> _pages = [new ConfirmationsPageModel()];
    private readonly Dictionary<SettingsEntry, CheckBox> _checkBoxes = [];
    private readonly Dictionary<SettingsPageModel, Control> _builtPages = [];

    public SettingsWindow()
    {
        InitializeComponent();

        PageList.ItemsSource = _pages.Select(page => page.Title).ToList();
        PageList.SelectedIndex = 0;
    }

    private void OnPageSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        int index = PageList.SelectedIndex;
        if (index >= 0 && index < _pages.Count)
        {
            ShowPage(_pages[index]);
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
                CheckBox checkBox = new()
                {
                    Content = entry.Caption,
                    IsThreeState = entry is TriStateSettingsEntry,
                    IsChecked = entry switch
                    {
                        BoolSettingsEntry boolEntry => boolEntry.Value,
                        TriStateSettingsEntry triStateEntry => triStateEntry.Value,
                        _ => false,
                    },
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new global::Avalonia.Thickness(12, 0, 0, 0),
                };

                _checkBoxes[entry] = checkBox;
                panel.Children.Add(checkBox);
            }
        }

        return panel;
    }

    /// <summary>Pushes every shown page's checkbox states through its model and persists.</summary>
    private void Apply()
    {
        foreach (SettingsPageModel page in _pages.Where(_builtPages.ContainsKey))
        {
            foreach (SettingsEntry entry in page.Entries)
            {
                if (!_checkBoxes.TryGetValue(entry, out CheckBox? checkBox))
                {
                    continue;
                }

                switch (entry)
                {
                    case BoolSettingsEntry boolEntry:
                        boolEntry.Value = checkBox.IsChecked ?? false;
                        break;
                    case TriStateSettingsEntry triStateEntry:
                        triStateEntry.Value = checkBox.IsChecked;
                        break;
                }
            }

            page.Save();
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
    ///  Verification harness (GE_SPIKE_SETTINGSTEST): snapshot the dialog, toggle
    ///  "Amend last commit" off, apply, prove the storage flip and the file flush,
    ///  toggle it back, snapshot, close.
    /// </summary>
    internal async Task RunHarnessAsync(string snapshotDirectory)
    {
        await Task.Delay(800);
        Snapshot(System.IO.Path.Combine(snapshotDirectory, "settings_before.png"));

        ConfirmationsPageModel page = (ConfirmationsPageModel)_pages[0];
        CheckBox amend = _checkBoxes[page.AmendLastCommit];
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

        await Task.Delay(300);
        Snapshot(System.IO.Path.Combine(snapshotDirectory, "settings_after.png"));

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
