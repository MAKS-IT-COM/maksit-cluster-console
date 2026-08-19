#!/bin/sh
set -eu

: "${ARTIFACTS_DIR:=/artifacts}"
mkdir -p "$ARTIFACTS_DIR" /testResults

# Customize test/pack .csproj paths (see templates/Dockerfile.ci.dotnet).
# Requires src/global.json (solution root) with test.runner = Microsoft.Testing.Platform and coverlet.MTP on the test project.
dotnet test path/to/YourProject.Tests/YourProject.Tests.csproj -c Release \
  --coverlet --coverlet-output-format cobertura --coverlet-include "[MaksIT.*]*" \
  --results-directory /testResults
dotnet pack path/to/YourProject/YourProject.csproj -c Release -o "$ARTIFACTS_DIR" --nologo \
  -p:IncludeSymbols=true -p:SymbolPackageFormat=snupkg
