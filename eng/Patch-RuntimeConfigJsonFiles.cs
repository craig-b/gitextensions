#:property TargetFramework=net10.0
#:property UseWindowsForms=false
#:property EnableStyleCopAnalyzers=false
#:property EnableVisualStudioThreading=false
#:property GenerateDocumentationFile=false
#:property NuGetAudit=false

// Retains only the Microsoft.WindowsDesktop.App reference in the given *.runtimeconfig.json files
// (semicolon-separated), see https://github.com/gitextensions/gitextensions/issues/10337.
// Usage: dotnet run Patch-RuntimeConfigJsonFiles.cs -- "a.runtimeconfig.json;b.runtimeconfig.json"

using System.Text.Json;
using System.Text.Json.Nodes;

foreach (string file in args.SelectMany(arg => arg.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
{
    JsonNode root = JsonNode.Parse(File.ReadAllText(file)) ?? throw new InvalidOperationException($"{file}: not JSON");
    if (root["runtimeOptions"]?["frameworks"] is not JsonArray frameworks)
    {
        continue;
    }

    foreach (JsonNode? framework in frameworks.Where(f => f?["name"]?.GetValue<string>() != "Microsoft.WindowsDesktop.App").ToList())
    {
        frameworks.Remove(framework);
    }

    File.WriteAllText(file, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
}

return 0;
