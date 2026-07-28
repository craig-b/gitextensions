#!/usr/bin/env bash
# Runs once after the container is created.
set -euo pipefail

echo "==> Initialising submodules"
# GitUI will not compile without these: ICSharpCode.TextEditor, conemu-inside and Git.hub are
# referenced directly by GitUI.csproj.
#
# Note these use *relative* URLs in .gitmodules (../../gitextensions/<name>.git), which git
# resolves against the 'origin' remote. If the clone has no origin, this step fails -- add one
# or clone with --recurse-submodules.
git submodule update --init --depth 1

echo "==> Warming the NuGet cache for the portable core"
# Restoring these three covers most of the package graph and makes the first real build quick.
# Failure here is not fatal: it is a cache warm-up, not a correctness check.
dotnet restore eng/portability/PortabilityProbe.csproj || true
dotnet restore src/app/GitCommands/GitCommands.csproj || true

echo
echo "Ready. Notes for this container:"
echo "  - EnableWindowsTargeting=true is set, so net10.0-windows projects compile here."
echo "  - They cannot RUN: that needs the Windows Desktop runtime. Unit tests must run on Windows."
echo "  - Portability probe:  dotnet build eng/portability/PortabilityProbe.csproj"
echo "  - See .devcontainer/README.md for what does and does not work on Linux."
