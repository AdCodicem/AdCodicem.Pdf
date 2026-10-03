#!/usr/bin/env bash
# Checks that a directory holds exactly one complete set of packages, all at one version, each with its
# symbol package, before any of them is pushed.
#
# Usage: verify-packages.sh <packages-dir> <version> <expected-ids>
#   <expected-ids>  the space- or newline-separated package IDs package-ids.sh prints
#   The version must be X.Y.Z or X.Y.Z-preview.N, the two shapes the workflows publish. ci.yml, which checks
#   the set a plain build packs (0.1.0-alpha) and publishes nothing, sets ALLOW_ANY_VERSION=true.
#
# nuget.org takes no package back, so the set is checked as a whole: one ID missing, one too many, one at
# another version, or one whose nuspec says another version than its file name, stops the run before the
# first push. So does a version left empty on the way, which packs 0.1.0-alpha instead of failing.
set -euo pipefail

packages_dir="${1:?packages directory required}"
version="${2:?version required}"
expected_ids="${3:?expected package IDs required}"

if [[ "${ALLOW_ANY_VERSION:-false}" != true && ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-preview\.[0-9]+)?$ ]]; then
  echo "::error::'$version' is neither X.Y.Z nor X.Y.Z-preview.N." >&2
  exit 1
fi

shopt -s nullglob
expected="$(tr -s ' \n' '\n' <<<"$expected_ids" | sed '/^$/d' | LC_ALL=C sort -u)"
status=0

# Every file must be a package of this version: anything else would be pushed with it.
files=("$packages_dir"/*)
(( ${#files[@]} > 0 )) || { echo "::error::$packages_dir is empty." >&2; exit 1; }
for file in "${files[@]}"; do
  name="$(basename "$file")"
  if [[ "$name" != *."$version".nupkg && "$name" != *."$version".snupkg ]]; then
    echo "::error::$name is not a package of $version." >&2
    status=1
  fi
done

packed="$(for package in "$packages_dir"/*."$version".nupkg; do
  basename "$package" ".$version.nupkg"
done | LC_ALL=C sort -u)"
if [[ "$expected" != "$packed" ]]; then
  echo "::error::The packages do not match the packable projects under src/. A project missing from src/AdCodicem.Pdf.Packages.slnf is not packed." >&2
  diff <(echo "$expected") <(echo "$packed") >&2 || true
  status=1
fi

# The nuspec is what nuget.org reads, so the version is checked there too, not only in the file name; and
# every package carries its symbols (IncludeSymbols, Directory.Build.props), which are pushed after it.
for package in "$packages_dir"/*."$version".nupkg; do
  declared="$(unzip -p "$package" '*.nuspec' | sed -n 's:.*<version>\(.*\)</version>.*:\1:p' | head -n 1)"
  if [[ "$declared" != "$version" ]]; then
    echo "::error::$(basename "$package") declares version '$declared'." >&2
    status=1
  fi
  if [[ ! -f "${package%.nupkg}.snupkg" ]]; then
    echo "::error::$(basename "$package") has no symbol package." >&2
    status=1
  fi
done

if (( status == 0 )); then echo "$(wc -l <<<"$packed") packages at $version, each with its symbols, matching the packable projects."; fi
exit "$status"
