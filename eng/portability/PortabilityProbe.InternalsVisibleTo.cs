// The probe merges four projects into one assembly named PortabilityProbe (see the header comment
// in PortabilityProbe.csproj), so tests that rely on internal members visible to their normal
// per-project test assembly (e.g. CommitMessageManager's internal constructor, granted to
// "GitCommands.Tests" by src/app/GitCommands/Properties/AssemblyInfo.cs) need an equivalent grant
// to the test host here.
//
// This can't use the SDK's <InternalsVisibleTo> item convention: that requires
// $(GenerateAssemblyInfo) == true, but the root Directory.Build.props sets it false repo-wide and
// unconditionally injects CommonAssemblyInfo.cs instead (see its "GenerateAssemblyInfo false" and
// the Compile Include a few lines below it). So this file follows the repo's other convention
// instead - a plain attribute in a source file - matching src/app/*/Properties/AssemblyInfo.cs.
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("PortabilityProbe.Tests")]
