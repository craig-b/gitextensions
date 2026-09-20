#:property TargetFramework=net10.0
#:property UseWindowsForms=false
#:property EnableStyleCopAnalyzers=false
#:property EnableVisualStudioThreading=false
#:property GenerateDocumentationFile=false
#:property NuGetAudit=false

// Downloads the latest GitExtensions.PluginManager release and unpacks it under <root>/Output, once.
// Usage: dotnet run Download-PluginManager.cs -- <root>

using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: dotnet run Download-PluginManager.cs -- <extract root>");
    return 1;
}

string root = args[0];
using HttpClient http = new();
http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GitExtensions-build", "1"));

// the anonymous API allows 60 requests an hour per address; CI and gh users have a token
if ((Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? Environment.GetEnvironmentVariable("GH_TOKEN")) is { Length: > 0 } token)
{
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
}

using JsonDocument releases = JsonDocument.Parse(await http.GetStringAsync("https://api.github.com/repos/gitextensions/gitextensions.pluginmanager/releases"));
(string Name, string Url)? asset = releases.RootElement.EnumerateArray()
    .Take(1)
    .SelectMany(release => release.GetProperty("assets").EnumerateArray())
    .Select(a => (Name: a.GetProperty("name").GetString()!, Url: a.GetProperty("browser_download_url").GetString()!))
    .FirstOrDefault(a => a.Name.StartsWith("GitExtensions.PluginManager", StringComparison.Ordinal) && a.Name.EndsWith(".zip", StringComparison.Ordinal));

if (asset is not { } found)
{
    Console.Error.WriteLine("PluginManager release not found.");
    return 1;
}

string zipPath = Path.Combine(root, found.Name);
string extractPath = Path.Combine(root, "Output");
if (File.Exists(zipPath))
{
    Console.WriteLine($"Download '{found.Name}' already exists.");
    return 0;
}

Directory.CreateDirectory(extractPath);
Console.WriteLine($"Downloading '{found.Name}'.");
await File.WriteAllBytesAsync(zipPath, await http.GetByteArrayAsync(found.Url));
ZipFile.ExtractToDirectory(zipPath, extractPath, overwriteFiles: true);
return 0;
