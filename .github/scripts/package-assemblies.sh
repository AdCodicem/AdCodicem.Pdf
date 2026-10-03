#!/usr/bin/env bash
# Extracts every assembly the packages ship, so that the provenance attestation can name them as subjects
# next to the .nupkg and .snupkg files.
#
# Usage: package-assemblies.sh <packages-dir> <output-dir>
#
# nuget.org adds its repository signature (.signature.p7s) to every .nupkg it accepts, so the copy a user
# restores no longer has the digest that was attested. The entries inside are left byte for byte as packed,
# and NuGet extracts them unchanged into the global packages folder: attesting the assemblies is what makes a
# restored DLL verifiable with
#   gh attestation verify ~/.nuget/packages/adcodicem.pdf/<version>/lib/net10.0/AdCodicem.Pdf.dll \
#     --repo AdCodicem/AdCodicem.Pdf
# Each package is extracted into a directory of its own, so that two packages shipping an assembly of the
# same name never overwrite each other.
set -euo pipefail

packages_dir="${1:?packages directory required}"
output_dir="${2:?output directory required}"

shopt -s nullglob
packages=("$packages_dir"/*.nupkg)
(( ${#packages[@]} > 0 )) || { echo "::error::No package in $packages_dir." >&2; exit 1; }

rm -rf "$output_dir"
mkdir -p "$output_dir"
total=0
for package in "${packages[@]}"; do
  name="$(basename "$package" .nupkg)"
  mapfile -t entries < <(unzip -Z1 "$package" | grep -iE '\.(dll|exe)$' || true)
  # Every package of this repository ships an assembly; one that does not means the pack went wrong.
  if (( ${#entries[@]} == 0 )); then
    echo "::error::$name ships no assembly." >&2
    exit 1
  fi
  unzip -q "$package" "${entries[@]}" -d "$output_dir/$name"
  printf '%s\n' "${entries[@]/#/$name: }"
  total=$(( total + ${#entries[@]} ))
done
echo "Extracted $total assemblies from ${#packages[@]} packages."
