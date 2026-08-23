using System.Text.RegularExpressions;

namespace GitCommands.FileStatus;

public enum FileFilterValidity
{
    Empty,
    Valid,
    Invalid,
}

/// <summary>The parsed file-name filter: the regex when valid, and the error message when not.</summary>
public readonly record struct FileFilterResult(Regex? Filter, FileFilterValidity Validity, string? ErrorMessage);

/// <summary>
///  Parses the file list's file-name filter text: a case-insensitive
///  regex; views map the validity to their input colors and error tooltips.
/// </summary>
public static class FileFilterParser
{
    public static FileFilterResult Parse(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return new FileFilterResult(Filter: null, FileFilterValidity.Empty, ErrorMessage: null);
        }

        try
        {
            return new FileFilterResult(new Regex(value, RegexOptions.IgnoreCase), FileFilterValidity.Valid, ErrorMessage: null);
        }
        catch (ArgumentException exception)
        {
            return new FileFilterResult(Filter: null, FileFilterValidity.Invalid, exception.Message);
        }
    }
}
