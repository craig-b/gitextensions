using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using GitCommands.Remotes;

namespace GitExtensions.Avalonia;

/// <summary>
///  Remote management over the portable ConfigFileRemoteSettingsManager and the remotes
///  dialog models - the same save normalization, name validation, and save-reaction rules
///  FormRemotes binds.
/// </summary>
public sealed class RemotesWindow : Window
{
    private readonly SliceSession _session;
    private readonly IConfigFileRemoteSettingsManager _manager;
    private readonly ListBox _list = new() { MinWidth = 200 };
    private readonly TextBox _name = new() { Watermark = "Name" };
    private readonly TextBox _url = new() { Watermark = "Url" };
    private readonly CheckBox _separatePushUrl = new() { Content = "Separate push URL" };
    private readonly TextBox _pushUrl = new() { Watermark = "Push URL", IsEnabled = false };
    private readonly Button _toggle = new() { Content = "Deactivate" };
    private List<ConfigFileRemote> _remotes = [];
    private ConfigFileRemote? _selected;

    /// <summary>Harness hook: skip modal follow-up prompts.</summary>
    internal bool SuppressPrompts { get; set; }

    public RemotesWindow(SliceSession session)
    {
        _session = session;
        _manager = session.CreateRemotesManager();

        Title = $"Remotes - {session.WorkingDir}";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _list.SelectionChanged += (_, _) => BindSelection(_list.SelectedIndex >= 0 && _list.SelectedIndex < _remotes.Count ? _remotes[_list.SelectedIndex] : null);
        _separatePushUrl.IsCheckedChanged += (_, _) => _pushUrl.IsEnabled = _separatePushUrl.IsChecked is true;
        _url.LostFocus += (_, _) =>
        {
            if (string.IsNullOrEmpty(_name.Text) && !string.IsNullOrEmpty(_url.Text)
                && RemoteUrlSuggestions.TryInferNameFromUrl(_url.Text) is string owner)
            {
                _name.Text = owner;
            }
        };

        Button newButton = new() { Content = "New" };
        newButton.Click += (_, _) =>
        {
            _list.SelectedIndex = -1;
            BindSelection(null);
            _name.Focus();
        };

        Button deleteButton = new() { Content = "Delete..." };
        deleteButton.Click += async (_, _) => await DeleteSelectedAsync();

        _toggle.Click += async (_, _) => await ToggleSelectedAsync();

        Button save = new() { Content = "Save", MinWidth = 90, IsDefault = true };
        save.Click += async (_, _) => await SaveAsync();
        Button close = new() { Content = "Close", MinWidth = 90, IsCancel = true };
        close.Click += (_, _) => Close();

        Content = new Grid
        {
            Margin = new global::Avalonia.Thickness(16),
            ColumnDefinitions = new ColumnDefinitions("Auto,16,360"),
            Children =
            {
                WithColumn(new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        _list,
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { newButton, deleteButton, _toggle } },
                    },
                }, 0),
                WithColumn(new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = "Remote details", FontWeight = global::Avalonia.Media.FontWeight.Bold },
                        _name,
                        _url,
                        _separatePushUrl,
                        _pushUrl,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Spacing = 8,
                            Children = { save, close },
                        },
                    },
                }, 2),
            },
        };

        Reload(preselect: null);
    }

    private static Control WithColumn(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private void Reload(string? preselect)
    {
        _remotes = [.. _manager.LoadRemotes(loadDisabled: true)];
        _list.ItemsSource = _remotes.Select(remote => remote.Disabled ? $"{remote.Name} (inactive)" : remote.Name).ToList();

        RemoteListSelection selection = RemoteListSelection.Resolve(_remotes, preselect);
        _list.SelectedIndex = selection.SelectedIndex;
        if (selection.FocusNameBox)
        {
            BindSelection(null);
            _name.Focus();
        }
    }

    private void BindSelection(ConfigFileRemote? remote)
    {
        _selected = remote;
        RemoteEditorFields fields = RemoteEditorFields.FromRemote(remote);
        _name.Text = fields.Name;
        _url.Text = fields.Url;
        _pushUrl.Text = fields.PushUrl;
        _separatePushUrl.IsChecked = fields.SeparatePushUrl;
        _toggle.Content = fields.IsDisabled ? "Activate" : "Deactivate";
        _toggle.IsEnabled = !fields.IsNew;
    }

    private async Task SaveAsync()
    {
        RemoteSaveRequest request = RemoteSaveRequest.Normalize(_name.Text ?? "", _url.Text ?? "", _pushUrl.Text ?? "", _separatePushUrl.IsChecked is true);
        if (request.Name.Length == 0)
        {
            return;
        }

        switch (RemoteNameValidator.Check(request.Name, isNew: _selected is null, _manager.EnabledRemoteExists, _manager.DisabledRemoteExists))
        {
            case RemoteNameConflict.EnabledExists:
                await ConfirmDialog.ErrorAsync(this, "Remotes", $"An active remote named \"{request.Name}\" already exists.");
                return;
            case RemoteNameConflict.DisabledExists:
                await ConfirmDialog.ErrorAsync(this, "Remotes", $"An inactive remote named \"{request.Name}\" already exists.");
                return;
        }

        ConfigFileRemoteSaveResult result = await Task.Run(() => _manager.SaveRemote(
            _selected, request.Name, request.Url, request.PushUrl, remotePuttySshKey: "", remoteColor: null, remotePrefix: null));

        RemoteSaveReaction reaction = RemoteSaveReaction.Evaluate(result, request.Url, request.SeparatePushUrl);
        if (reaction.ShowMessage)
        {
            await ConfirmDialog.ErrorAsync(this, "Remotes", reaction.Message!);
            Reload(request.Name);
            return;
        }

        if (reaction.OfferConfigureAndFetch
            && !SuppressPrompts
            && await ConfirmDialog.ConfirmAsync(this, "New remote", "Configure the default pull behavior for this remote and fetch it?"))
        {
            await Task.Run(() => _manager.ConfigureRemotes(request.Name));
            await _session.PullWithOptionsAsync(new GitCommands.Pull.PullOptions(
                GitCommands.Pull.PullActionKind.Fetch, GitCommands.Pull.PullSourceKind.Remote, request.Name, RemoteBranch: null, LocalBranch: null));
        }

        Reload(request.Name);
    }

    private async Task DeleteSelectedAsync()
    {
        if (_selected is null || !await ConfirmDialog.ConfirmAsync(this, "Delete", $"Are you sure you want to delete remote {_selected.Name}?"))
        {
            return;
        }

        string output = await Task.Run(() => _manager.RemoveRemote(_selected));
        if (!string.IsNullOrEmpty(output))
        {
            await ConfirmDialog.ErrorAsync(this, "Remotes", output);
        }

        Reload(preselect: null);
    }

    /// <summary>Verification harness (GE_SPIKE_REMOTESTEST=1): create, toggle, and delete a remote.</summary>
    internal async Task RunHarnessAsync()
    {
        SuppressPrompts = true;
        await Task.Delay(800);
        Console.Error.WriteLine($"[remotes] loaded: {Describe()}");

        _list.SelectedIndex = -1;
        BindSelection(null);
        _name.Text = "harness-remote";
        _url.Text = "../opsremote.git";
        await SaveAsync();
        Console.Error.WriteLine($"[remotes] after save: {Describe()}");

        SelectByName("harness-remote");
        await ToggleSelectedAsync();
        Console.Error.WriteLine($"[remotes] after deactivate: {Describe()}");

        SelectByName("harness-remote");
        await ToggleSelectedAsync();
        SelectByName("harness-remote");
        string output = await Task.Run(() => _manager.RemoveRemote(_selected!));
        Reload(preselect: null);
        Console.Error.WriteLine($"[remotes] after delete: {Describe()}{(output.Length > 0 ? $" | {output}" : "")}");
        Environment.Exit(0);

        string Describe() => string.Join(", ", _remotes.Select(remote => remote.Disabled ? $"{remote.Name}(off)" : remote.Name));
        void SelectByName(string name) => _list.SelectedIndex = _remotes.FindIndex(remote => remote.Name == name);
    }

    private async Task ToggleSelectedAsync()
    {
        if (_selected is null)
        {
            return;
        }

        bool newDisabled = !_selected.Disabled;
        string name = _selected.Name!;
        await Task.Run(() => _manager.ToggleRemoteState(name, newDisabled));
        Reload(name);
    }
}
