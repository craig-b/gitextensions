using System.Diagnostics;
using System.Reflection;

namespace GitExtensions.Extensibility;

/// <summary>
///  Application identity and per-user path values that on Windows come from
///  <c>System.Windows.Forms.Application</c>, which platform-neutral code cannot reference.
/// </summary>
/// <remarks>
///  The WinForms hosts assign these from <c>Application</c> at startup (see GitExtensions.Program),
///  before anything touches <c>AppSettings</c> — its static constructor latches the paths on first
///  touch. The defaults replicate the WinForms computation (dotnet/winforms Application.cs), so
///  tests and non-UI hosts work without configuration.
/// </remarks>
public static class AppPaths
{
    /// <summary>
    ///  The application's product version string. WinForms takes the entry assembly's
    ///  <see cref="AssemblyInformationalVersionAttribute"/> verbatim — including any semver
    ///  build metadata — then falls back to the Win32 ProductVersion resource, then "1.0.0.0".
    /// </summary>
    public static string ProductVersion { get; set; } = GetDefaultProductVersion();

    /// <summary>
    ///  Full path of the executable that started the process. <see cref="Environment.ProcessPath"/>
    ///  and WinForms' <c>Application.ExecutablePath</c> are the same Win32 call
    ///  (<c>GetModuleFileName(null)</c>), so the default is already byte-identical on Windows.
    /// </summary>
    public static string ApplicationExecutablePath { get; set; } = Environment.ProcessPath ?? string.Empty;

    /// <summary>
    ///  Returns the per-user roaming data directory: base path + CompanyName + ProductName +
    ///  ProductVersion, creating it if missing.
    /// </summary>
    /// <remarks>
    ///  A delegate rather than a value because evaluation creates the directory, and portable
    ///  installations must never evaluate it — they store settings beside the executable.
    /// </remarks>
    public static Func<string> GetUserAppDataPath { get; set; } = GetDefaultUserAppDataPath;

    private static string GetDefaultProductVersion()
    {
        Assembly? entryAssembly = Assembly.GetEntryAssembly();
        string? version = entryAssembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (string.IsNullOrEmpty(version))
        {
            version = GetAppFileVersionInfo()?.ProductVersion?.Trim();
        }

        return string.IsNullOrEmpty(version) ? "1.0.0.0" : version;
    }

    private static string GetDefaultUserAppDataPath()
    {
        string path = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            GetDefaultCompanyName(),
            GetDefaultProductName(),
            ProductVersion);

        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        return path;
    }

    private static string? GetDefaultCompanyName()
    {
        string? company = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;

        if (string.IsNullOrEmpty(company))
        {
            company = GetAppFileVersionInfo()?.CompanyName?.Trim();
        }

        if (string.IsNullOrEmpty(company))
        {
            string? ns = GetAppMainType()?.Namespace;
            if (!string.IsNullOrEmpty(ns))
            {
                int firstDot = ns.IndexOf('.');
                company = firstDot >= 0 ? ns[..firstDot] : ns;
            }
            else
            {
                company = GetDefaultProductName();
            }
        }

        return company;
    }

    private static string? GetDefaultProductName()
    {
        string? product = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyProductAttribute>()?.Product;

        if (string.IsNullOrEmpty(product))
        {
            product = GetAppFileVersionInfo()?.ProductName?.Trim();
        }

        if (string.IsNullOrEmpty(product))
        {
            Type? mainType = GetAppMainType();
            string? ns = mainType?.Namespace;
            if (!string.IsNullOrEmpty(ns))
            {
                int lastDot = ns.LastIndexOf('.');
                product = lastDot >= 0 && lastDot < ns.Length - 1 ? ns[(lastDot + 1)..] : ns;
            }
            else
            {
                product = mainType?.Name;
            }
        }

        return product;
    }

    private static FileVersionInfo? GetAppFileVersionInfo()
    {
        // WinForms reads the version resource of the entry point's module, falling back to the
        // process executable for single-file publishes.
        string? file = GetAppMainType()?.Module.FullyQualifiedName;
        if (string.IsNullOrEmpty(file) || file == "<Unknown>" || !File.Exists(file))
        {
            file = Environment.ProcessPath;
        }

        return string.IsNullOrEmpty(file) ? null : FileVersionInfo.GetVersionInfo(file);
    }

    private static Type? GetAppMainType() => Assembly.GetEntryAssembly()?.EntryPoint?.ReflectedType;
}
