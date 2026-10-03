#!/usr/bin/env bash
# Packs every packable project under src/ at the version it is given, and checks that the packages are the
# ones package-ids.sh lists, at that version, with their symbols.
#
# Usage: release-pack.sh <version> <release-type> [last-version]
#   version       what next-version.mjs computed: the stable version in release.yml, which semantic-release
#                 then refuses to publish under any other, or <next>-preview.<height> in preview.yml
#   release-type  major, minor or patch, as the commit analyzer answered (patch when it answered nothing);
#                 or repair, for preview.yml completing a version nuget.org already partly has, which was
#                 held to its baseline when it was first packed
#   last-version  the last stable release, the API baseline of a patch, and of a minor from 1.0 on
#
# Only the packable projects are restored, built and packed, through the solution filter: no test,
# benchmark, sample or tool project, and none of their packages, in the job whose output is published.
#
# The version goes in as -p:Version, which overrides VersionPrefix and VersionSuffix alike. A build given
# no version is 0.1.0-alpha (Directory.Build.props), so an empty or lost version would not fail the pack: it
# would pack the wrong number. The packed file names and every nuspec are checked against it instead.
set -euo pipefail

version="${1:?version required}"
release_type="${2:?release type required}"
last_version="${3:-}"
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-preview\.[0-9]+)?$ ]] ||
  { echo "::error::'$version' is neither X.Y.Z nor X.Y.Z-preview.N." >&2; exit 1; }
[[ "$release_type" =~ ^(major|minor|patch|repair)$ ]] ||
  { echo "::error::'$release_type' is not major, minor, patch or repair." >&2; exit 1; }

root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$root"
args=(src/AdCodicem.Pdf.Packages.slnf --configuration Release --output artifacts/packages "-p:Version=$version")
feed="${NUGET_FLAT_CONTAINER:-https://api.nuget.org/v3-flatcontainer}"

ids="$("$root/.github/scripts/package-ids.sh")"
[[ -n "$ids" ]] || { echo "::error::No packable project under src/." >&2; exit 1; }

# Package validation compares each package's API with a published baseline. That holds a patch, and a minor
# from 1.0 on, to the last release; a major says in its number that it breaks, and below 1.0 so does a minor
# (ADR 48), so neither is held to it. A repair completes a version that was held to its baseline when it was
# first packed.
if [[ "$release_type" == repair ]]; then
  echo "Completing $version, partly published already: no API baseline."
elif [[ -z "$last_version" ]]; then
  echo "No previous release: no API baseline."
elif [[ "$release_type" == major ]]; then
  echo "Major release: no API baseline (it breaks on purpose)."
elif [[ "$version" == 0.* && "$release_type" == minor ]]; then
  echo "Minor below 1.0: no API baseline (a minor is where 0.x breaks)."
else
  # Only a published package can serve as a baseline, and each only for itself: v0.1.0 is a tag with no
  # package behind it, and a package added since the last release has nothing to compare with. Restoring a
  # baseline nuget.org lacks fails the pack (NU1101), so each package is looked up, and the projects
  # nuget.org lacks at that version are packed without one, through the hook in Directory.Build.props. A
  # lookup that fails stops the pack rather than switching the check off.
  validated=() without=()
  index="$(mktemp)"
  trap 'rm -f "$index"' EXIT
  for project in src/*/*.csproj; do
    properties="$(dotnet msbuild "$project" -nologo \
      -getProperty:IsPackable -getProperty:PackageId -getProperty:MSBuildProjectName)"
    [[ "$(jq -r .Properties.IsPackable <<<"$properties")" == true ]] || continue
    id="$(jq -r .Properties.PackageId <<<"$properties")"
    code="$(curl --silent --show-error --location --retry 3 --output "$index" --write-out '%{http_code}' \
      "$feed/${id,,}/index.json")" || code=000
    if [[ "$code" == 200 ]] && jq -e --arg v "${last_version,,}" '.versions | index($v) != null' "$index" >/dev/null; then
      validated+=("$id")
    elif [[ "$code" == 200 || "$code" == 404 ]]; then
      echo "$id $last_version is not on nuget.org: packing it without a baseline."
      without+=("$(jq -r .Properties.MSBuildProjectName <<<"$properties")")
    else
      echo "::error::nuget.org answered HTTP $code for $id: cannot tell whether $last_version is a baseline." >&2
      exit 1
    fi
  done
  if (( ${#validated[@]} == 0 )); then
    echo "$last_version was never published to nuget.org: no API baseline."
  else
    echo "Validating the API of ${validated[*]} against $last_version."
    args+=("-p:ReleaseBaselineVersion=$last_version")
    (( ${#without[@]} == 0 )) || args+=("-p:ProjectsWithoutBaseline=${without[*]}")
  fi
fi

rm -rf artifacts/packages
echo "Packing $version ($release_type)."
dotnet pack "${args[@]}"

"$root/.github/scripts/verify-packages.sh" artifacts/packages "$version" "$ids"
