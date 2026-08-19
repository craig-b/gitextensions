using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using GitExtensions.Avalonia.Rendering;
using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;
using GitUI.Editor.Diff;

namespace GitExtensions.Avalonia;

public partial class MainWindow : Window
{
    private readonly SliceSession _session;
    private CancellationTokenSource? _selectionCts;

    public MainWindow(string repositoryPath)
    {
        InitializeComponent();

        _session = new SliceSession(repositoryPath);
        Title = $"Git Extensions - {_session.WorkingDir}";

        Loaded += async (_, _) => await LoadLogAsync();
    }

    private sealed record RevisionListItem(GitRevision Revision)
    {
        public override string ToString()
            => $"{Revision.ObjectId.ToShortString()}  {Revision.Subject}\n           {Revision.Author}, {Revision.AuthorDate:yyyy-MM-dd HH:mm}";
    }

    private async Task LoadLogAsync()
    {
        if (!_session.IsValidRepository)
        {
            CommitBody.Text = $"Not a git repository: {_session.WorkingDir}";
            return;
        }

        IReadOnlyList<GitRevision> revisions = await Task.Run(() => _session.GetLog(maxCount: 200, CancellationToken.None));

        RevisionList.ItemsSource = revisions.Select(revision => new RevisionListItem(revision)).ToList();
        if (revisions.Count > 0)
        {
            RevisionList.SelectedIndex = 0;
        }
    }

    private async void OnRevisionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (RevisionList.SelectedItem is not RevisionListItem item)
        {
            return;
        }

        _selectionCts?.Cancel();
        _selectionCts = new CancellationTokenSource();
        CancellationToken cancellationToken = _selectionCts.Token;

        GitRevision revision = item.Revision;

        try
        {
            var (header, body) = await Task.Run(() => _session.GetCommitInfo(revision), cancellationToken);
            var (diffText, spans) = await Task.Run(() => _session.GetDiff(revision), cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                CommitHeader.Inlines!.Clear();
                CommitHeader.Inlines.AddRange(InlineRendering.ToInlines(header));

                CommitBody.Inlines!.Clear();
                CommitBody.Inlines.AddRange(InlineRendering.ToInlines(body));

                DiffText.Inlines!.Clear();
                DiffText.Inlines.AddRange(InlineRendering.ToInlines(diffText, spans));
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            CommitBody.Text = ex.ToString();
        }
    }
}
