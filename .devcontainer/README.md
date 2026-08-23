# Dev container

Runs the toolchain — and optionally Claude Code — inside Linux, against the repo bind-mounted from
your host. Set up per <https://code.claude.com/docs/en/devcontainer>.

## What works on Linux

| | |
|---|---|
| Compile `GitCommands`, `GitExtUtils`, `GitExtensions.Extensibility`, `GitUI`, `BugReporter` | yes |
| Portability probe (`eng/portability/PortabilityProbe.csproj`) | yes — this is its native environment |
| Compile the test projects | yes |
| **Run** the unit or integration tests | **no** |
| Run the app | no |

`EnableWindowsTargeting=true` is set in `containerEnv`, which is what makes the first row possible:
it lets the SDK restore Windows *reference* packs on a non-Windows host. It does **not** provide the
Windows Desktop *runtime*, so anything that needs to execute WinForms code cannot run here:

```
You must install .NET to run this application.
Framework: 'Microsoft.WindowsDesktop.App', version '10.0.0'
```

**Tests must be run on Windows before merging.** Compilation in this container is not a substitute —
it will not catch a behavioural regression.

## Why a container is useful for this repo anyway

The portability work (see the M0/M1 milestones) is specifically about making the layers below `GitUI`
build without WinForms. Linux is the environment that proves it, because on Windows an accidental
Windows-only dependency simply compiles.

The container also surfaces case-sensitivity bugs that Windows hides — the `.resx` files referenced
12 icons whose casing did not match the files on disk, which was invisible on Windows and broke the
build here.

## Gotchas

**Submodules.** `.gitmodules` uses relative URLs, which git resolves against the `origin` remote. A
clone with no `origin` cannot init submodules, and `GitUI` will not compile without them.
`post-create.sh` runs `git submodule update --init` for you.

**SourceLink.** If the clone has no remote, builds fail with
`The value passed to task parameter RepositoryUrl is not a valid URI: ''`. Either add a remote or
pass `-p:EnableSourceControlManagerQueries=false`.

**Building a single project in isolation.** `dotnet build src/app/GitExtensions/GitExtensions.csproj`
fails with `MSB4184` on a missing `Plugins` directory, because a post-build target enumerates plugin
output that only exists after a fuller build. This is pre-existing and unrelated to the container.

## Claude Code specifics

Authentication and session history persist across rebuilds via a named volume mounted at
`~/.claude`, scoped per devcontainer so this repo does not share credentials with other projects.

Two things deliberately **not** configured here, both from the guidance in the docs:

- **No egress firewall.** The reference container includes one, requiring `NET_ADMIN`/`NET_RAW` via
  `runArgs`. It is optional, and an untested allowlist that silently blocks nuget.org is worse than
  none. Add it deliberately if you want it, and allow the NuGet and GitHub domains this repo needs.
- **No host credentials mounted.** Do not add `~/.ssh` or cloud credential files to `mounts`. The
  container is protection against a mistake inside it, and mounting host secrets removes that.

`--dangerously-skip-permissions` is possible here because `remoteUser` is non-root, but note that
the workspace is bind-mounted: anything written still lands in your real working tree.
