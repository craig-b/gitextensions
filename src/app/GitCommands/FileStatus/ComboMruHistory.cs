namespace GitCommands.FileStatus;

public enum MruDedupe
{
    /// <summary>An existing entry keeps its position (the filter combo's historical rule).</summary>
    KeepPosition,

    /// <summary>An existing entry is promoted to the front (the grep combo's historical rule).</summary>
    MoveToFront,
}

/// <summary>The result of an MRU insertion; unchanged lists report Changed = false.</summary>
public readonly record struct MruUpdate(IReadOnlyList<string> Items, bool Changed);

/// <summary>
///  The file-status combos MRU maintenance - the two combos historically hand-rolled divergent algorithms;
///  the divergence is now an explicit <see cref="MruDedupe"/> choice.
/// </summary>
public static class ComboMruHistory
{
    public static MruUpdate Add(IReadOnlyList<string> items, string entry, int maxLength, MruDedupe dedupe)
    {
        int existingIndex = -1;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] == entry)
            {
                existingIndex = i;
                break;
            }
        }

        if (existingIndex == 0 || (existingIndex > 0 && dedupe is MruDedupe.KeepPosition))
        {
            return new MruUpdate(items, Changed: false);
        }

        List<string> updated = [.. items];
        if (existingIndex > 0)
        {
            updated.RemoveAt(existingIndex);
        }
        else if (updated.Count >= maxLength)
        {
            updated.RemoveAt(maxLength - 1);
        }

        updated.Insert(0, entry);
        return new MruUpdate(updated, Changed: true);
    }
}
