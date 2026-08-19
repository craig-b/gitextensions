namespace GitCommands;

/// <summary>
///  Host seam for the portable-installation decision (does this installation keep its settings
///  next to the executable?). The WinForms hosts leave this null and the answer comes from the
///  entry executable's app.config via ConfigurationManager - a reflection-based mechanism that a
///  trimmed client cannot carry (plan §19.1a). Non-WinForms hosts set this BEFORE the first
///  AppSettings access (its static constructor consumes the answer), and ConfigurationManager is
///  then never touched.
/// </summary>
public static class HostPortability
{
    public static bool? IsPortableOverride { get; set; }
}
