using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using GitCommands.Actions;
using GitUI.Editor.Diff;

namespace GitExtensions.Avalonia;

/// <summary>
///  The shared diff-pane context menu (context-menu redesign, surface 4): one attach point
///  for every pane rendering a unified diff (browse, commit window, file history, compare).
///  Per the pointer rule only selection verbs live here; the copy transforms run through the
///  portable <see cref="DiffCopyModel"/>. Line-patch verbs join via extra handlers when the
///  host supports them.
/// </summary>
internal static class DiffPaneMenu
{
    /// <summary>The client renders single-parent unified diffs; combined-diff prefixes arrive with that feature.</summary>
    private static readonly string[] PlainDiffPrefixes = ["+", "-", " "];

    /// <summary>Strips diff markers from the pane's current selection (the Copy transform), for reuse by hosts.</summary>
    public static string? StrippedSelection(SelectableTextBlock pane, string? diffText)
    {
        (int start, string selected) = Selection(pane, diffText);
        return diffText is null ? null : DiffCopyModel.CopySelection(diffText, start, selected, PlainDiffPrefixes);
    }

    public static void Attach(
        SelectableTextBlock pane,
        Func<string?> getDiffText,
        bool isCommitWindow = false,
        Action<string>? addSelectionToCommitMessage = null)
    {
        pane.ContextRequested += (_, e) =>
        {
            string? text = getDiffText();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            (int start, string selected) = Selection(pane, text);
            DiffMenuContext context = new(
                HasSelection: selected.Length > 0,
                IsPatchView: true,
                IsCommitWindow: isCommitWindow);

            Dictionary<string, Func<Task>> handlers = new()
            {
                ["diff.copy"] = () => CopyAsync(pane, DiffCopyModel.CopySelection(text, start, selected, PlainDiffPrefixes)),
                ["diff.copyPatch"] = () => CopyAsync(pane, selected.Length > 0 ? selected : null),
                ["diff.copyNewVersion"] = () => CopyAsync(pane, DiffCopyModel.CopyNewVersion(text, start, selected)),
                ["diff.copyOldVersion"] = () => CopyAsync(pane, DiffCopyModel.CopyOldVersion(text, start, selected)),
            };

            if (addSelectionToCommitMessage is not null)
            {
                handlers["diff.addToCommitMessage"] = () =>
                {
                    if (DiffCopyModel.CopySelection(text, start, selected, PlainDiffPrefixes) is string stripped)
                    {
                        addSelectionToCommitMessage(stripped);
                    }

                    return Task.CompletedTask;
                };
            }

            ContextMenu menu = MainWindow.BuildMenu(
                DiffMenuRegistry.DiffMenuFor(context),
                action => handlers.ContainsKey(action.Id),
                action => DiffMenuRegistry.IsApplicable(action, context),
                action => handlers[action.Id]());

            if (menu.Items.Count > 0)
            {
                e.Handled = true;
                menu.Open(pane);
            }
        };
    }

    private static (int Start, string Selected) Selection(SelectableTextBlock pane, string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (0, "");
        }

        int start = Math.Clamp(Math.Min(pane.SelectionStart, pane.SelectionEnd), 0, text.Length);
        int end = Math.Clamp(Math.Max(pane.SelectionStart, pane.SelectionEnd), 0, text.Length);
        return (start, text[start..end]);
    }

    private static async Task CopyAsync(Control owner, string? text)
    {
        if (text is not null && TopLevel.GetTopLevel(owner)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }
}
