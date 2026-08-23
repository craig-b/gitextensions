namespace GitCommands.FileStatus;

public enum DiffListGrouping
{
    FilePath,
    FileExtension,
    FileStatus,
}

/// <summary>
///  DiffListSortType is a (grouping, flat) pair - three view
///  sites decoded it independently, one by matching the enum name's "Flat" suffix.
/// </summary>
public static class DiffListSortLayout
{
    public static (DiffListGrouping Grouping, bool Flat) Decompose(DiffListSortType sortType)
        => sortType switch
        {
            DiffListSortType.FilePath => (DiffListGrouping.FilePath, false),
            DiffListSortType.FilePathFlat => (DiffListGrouping.FilePath, true),
            DiffListSortType.FileExtension => (DiffListGrouping.FileExtension, false),
            DiffListSortType.FileExtensionFlat => (DiffListGrouping.FileExtension, true),
            DiffListSortType.FileStatus => (DiffListGrouping.FileStatus, false),
            DiffListSortType.FileStatusFlat => (DiffListGrouping.FileStatus, true),
            _ => throw new NotSupportedException($"{sortType} is not a supported sorting method."),
        };

    public static DiffListSortType Compose(DiffListGrouping grouping, bool flat)
        => (grouping, flat) switch
        {
            (DiffListGrouping.FilePath, false) => DiffListSortType.FilePath,
            (DiffListGrouping.FilePath, true) => DiffListSortType.FilePathFlat,
            (DiffListGrouping.FileExtension, false) => DiffListSortType.FileExtension,
            (DiffListGrouping.FileExtension, true) => DiffListSortType.FileExtensionFlat,
            (DiffListGrouping.FileStatus, false) => DiffListSortType.FileStatus,
            (DiffListGrouping.FileStatus, true) => DiffListSortType.FileStatusFlat,
            _ => throw new NotSupportedException(),
        };
}
