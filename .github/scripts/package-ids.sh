#!/usr/bin/env bash
# Prints the PackageId of every packable project under src/, one per line, sorted.
#
# Usage: package-ids.sh [root]
#
# The list is read from MSBuild rather than written down, so that a package added under src/ is packed,
# checked, pushed and linked from the GitHub Release without anyone remembering to add it anywhere.
# release-pack.sh and verify-packages.sh check it against what was actually packed before anything is
# pushed. preview-gate.sh also passes the root of an older commit's tree, to learn what that commit packed.
#
# Evaluation only, no restore: IsPackable comes from Directory.Build.props, which sets it false, and the
# project that sets it true.
set -euo pipefail

cd "${1:-$(dirname "$0")/../..}"

shopt -s nullglob
projects=(src/*/*.csproj)
(( ${#projects[@]} > 0 )) || { echo "::error::No project under src/." >&2; exit 1; }
for project in "${projects[@]}"; do
  dotnet msbuild "$project" -nologo -getProperty:IsPackable -getProperty:PackageId |
    jq -r 'select(.Properties.IsPackable == "true") | .Properties.PackageId'
done | LC_ALL=C sort -u
