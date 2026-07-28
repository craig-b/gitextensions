using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace GitExtensions.Extensibility;

/// <summary>
///  Round-trips a <see cref="FontDescriptor"/> to and from the persisted settings string.
/// </summary>
/// <remarks>
///  The wire format is unchanged from when this operated on <c>System.Drawing.Font</c>
///  (<c>family;size;_IC_;bold;italic</c>), so settings written by earlier versions still parse.
/// </remarks>
public static class FontParser
{
    private const string InvariantCultureId = "_IC_";

    public static string AsString(this FontDescriptor value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return string.Format(CultureInfo.InvariantCulture,
            "{0};{1};{2};{3};{4}", value.FamilyName, value.Size, InvariantCultureId, value.Bold ? 1 : 0, value.Italic ? 1 : 0);
    }

    [return: NotNullIfNotNull(nameof(defaultValue))]
    public static FontDescriptor? Parse(this string? value, FontDescriptor? defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        string[] parts = value.Split(';');
        if (parts.Length < 2)
        {
            return defaultValue;
        }

        try
        {
            string fontSize;
            if (parts.Length == 3 && parts[2] == InvariantCultureId)
            {
                fontSize = parts[1];
            }
            else
            {
                fontSize = parts[1].Replace(",", CultureInfo.InvariantCulture.NumberFormat.NumberDecimalSeparator);
                fontSize = fontSize.Replace(".", CultureInfo.InvariantCulture.NumberFormat.NumberDecimalSeparator);
            }

            bool bold = parts.Length > 3 && parts[3] == "1";
            bool italic = parts.Length > 4 && parts[4] == "1";

            return new FontDescriptor(parts[0], float.Parse(fontSize, CultureInfo.InvariantCulture), bold, italic);
        }
        catch
        {
            return defaultValue;
        }
    }
}
