#!/usr/bin/env bash
# Pushes to nuget.org whatever of this version it does not have yet, dependencies
# first, and optionally waits until nuget.org serves all of it.
#
# Usage: push-packages.sh <packages-dir> <version>
# Needs NUGET_API_KEY (the short-lived key NuGet/login exchanged for the job's
# OIDC token).
#   NUGET_PUSH_ATTEMPTS   passes over the set before giving up (default 3)
#   NUGET_PUSH_BACKOFF    seconds before the second pass, doubled for each later one (default 60)
#   NUGET_LIST_TIMEOUT    seconds to wait, once everything is pushed, until nuget.org
#                         lists every package and serves every symbol package; 0, the
#                         default, does not wait. preview.yml and release.yml wait, so
#                         that the next run decides from a nuget.org that has caught up.
#   NUGET_SOURCE, NUGET_FLAT_CONTAINER, NUGET_SYMBOL_PACKAGES override the endpoints.
#
# nuget.org has no transaction: a push of several packages is several requests, and a run that stops
# halfway leaves some of them published. So the set is pushed in dependency order -- AdCodicem.Pdf before
# every satellite that depends on it -- and a pass stops at the first package that fails, so that whatever a
# partial push leaves on nuget.org can be restored: no package is ever published before one it depends on.
# Each pass asks nuget.org which packages already carry the version and pushes only the others, so the same
# script, run again in the same job, by "Re-run failed jobs", or by preview.yml's next run (repair), completes
# the set at the same version. release.yml runs it as semantic-release's publish step, where a re-run finds
# the release already tagged: a stable set left partial is completed by running it again by hand on the
# release's packages (docs/releasing.md).
#
# Each .nupkg is pushed with --no-symbols, and its .snupkg by a push of its own
# right after it. Pushed together, a .nupkg answering 409 would take its symbols
# down with it: --skip-duplicate never sends the symbols of a package it skips,
# and a .nupkg that nuget.org holds but does not list yet answers 409, after an
# earlier pass or after the client's own retry of a request that timed out or
# answered 5xx. A package nuget.org already lists, whose symbols it does not
# serve, gets its .snupkg pushed on its own. A .snupkg still validating answers
# 409, which is skipped. A symbol package that fails does not stop the pass,
# since no package needs the symbols of another, but fails it once every package
# is pushed. preview-gate.sh only completes missing .nupkg files, so a symbol
# package still missing after the wait fails the job, and "Re-run failed jobs"
# pushes it.
#
# Every lookup is a plain statement whose failure returns from the pass
# explicitly: inside a function called from a condition, bash suspends set -e,
# and a failed lookup would otherwise read as "missing" and push.
set -euo pipefail

packages_dir="${1:?packages directory required}"
version="${2:?version required}"
: "${NUGET_API_KEY:?NUGET_API_KEY required}"
source_url="${NUGET_SOURCE:-https://api.nuget.org/v3/index.json}"
flat_container="${NUGET_FLAT_CONTAINER:-https://api.nuget.org/v3-flatcontainer}"
symbol_packages="${NUGET_SYMBOL_PACKAGES:-https://www.nuget.org/api/v2/symbolpackage}"
attempts="${NUGET_PUSH_ATTEMPTS:-3}"
backoff="${NUGET_PUSH_BACKOFF:-60}"
list_timeout="${NUGET_LIST_TIMEOUT:-0}"
push_args=(--api-key "$NUGET_API_KEY" --source "$source_url" --skip-duplicate)
[[ "$source_url" == http://* ]] && push_args+=(--allow-insecure-connections)
lower_version="${version,,}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

shopt -s nullglob
packages=("$packages_dir"/*."$version".nupkg)
(( ${#packages[@]} > 0 )) || { echo "::error::No package of $version in $packages_dir." >&2; exit 1; }

# --- order: every package after the packages of this set it depends on --------------------------
# A dependency is looked up in an array keyed by the lowercased ID, since NuGet IDs ignore case, and
# its edge names the package as its file does, so that tsort sees one node per package. Not a
# `printf | grep -q` per dependency: under pipefail, grep leaving at its first match can fail printf
# with SIGPIPE, and the edge would be dropped without a word.
ids=()
declare -A in_set=()
for package in "${packages[@]}"; do
  id="$(basename "$package" ".$version.nupkg")"
  ids+=("$id")
  in_set[${id,,}]=$id
done
for id in "${ids[@]}"; do
  echo "$id $id"
  unzip -p "$packages_dir/$id.$version.nupkg" '*.nuspec' >"$work/nuspec"
  # grep finds nothing in a package with no dependency, which is not an error.
  { grep -oP '<dependency\b[^>]*\bid="\K[^"]+' "$work/nuspec" || true; } | sort -u >"$work/dependencies"
  while read -r dependency; do
    if [[ -v in_set[${dependency,,}] ]]; then echo "${in_set[${dependency,,}]} $id"; fi
  done <"$work/dependencies"
done >"$work/edges"
mapfile -t order < <(tsort "$work/edges")
(( ${#order[@]} == ${#ids[@]} )) || { echo "::error::Could not order the packages by dependency." >&2; exit 1; }
echo "Push order: ${order[*]}"

# --- nuget.org lookups: 200 or 404 is an answer; anything else fails the pass -------------------
# Sets $status; returns 1, after an ::error::, on anything but 200 or 404.
status=""
lookup() {
  status="$(curl --silent --location --output "$work/body" --write-out '%{http_code}' --retry 3 "$1")" || status=000
  if [[ "$status" != 200 && "$status" != 404 ]]; then
    echo "::error::$1 answered HTTP $status." >&2
    return 1
  fi
}

# Sets $listed to true when the flat container lists the version for $1.
listed=false
lookup_listed() {
  listed=false
  lookup "$flat_container/${1,,}/index.json" || return 1
  [[ "$status" == 200 ]] || return 0
  if jq -e --arg v "$lower_version" '.versions | index($v) != null' "$work/body" >/dev/null; then listed=true; fi
}

# Sets $served to true when the symbol package of $1 is available.
served=false
lookup_served() {
  served=false
  lookup "$symbol_packages/$1/$version" || return 1
  [[ "$status" != 200 ]] || served=true
}

# One pass over the set, in order. A package that fails, or a lookup, ends the pass and sets
# $stopped_at, so that nothing is pushed after it. A symbol package that fails is added to
# $symbols_failed and the pass goes on, then fails after the last package.
pushed=0 completed=0 present=0 stopped_at="" symbols_failed=()
push_pass() {
  local id package symbols
  pushed=0 completed=0 present=0 stopped_at="" symbols_failed=()
  for id in "${order[@]}"; do
    package="$packages_dir/$id.$version.nupkg"
    symbols="$packages_dir/$id.$version.snupkg"
    lookup_listed "$id" || { stopped_at="$id"; return 1; }
    if ! $listed; then
      echo "::group::Push $id $version"
      dotnet nuget push "$package" --no-symbols "${push_args[@]}" || { echo "::endgroup::"; stopped_at="$id"; return 1; }
      pushed=$(( pushed + 1 ))
      if [[ -f "$symbols" ]] && ! dotnet nuget push "$symbols" "${push_args[@]}"; then symbols_failed+=("$id"); fi
      echo "::endgroup::"
      continue
    fi
    if [[ -f "$symbols" ]]; then
      lookup_served "$id" || { stopped_at="$id"; return 1; }
      if ! $served; then
        echo "::group::Push the symbols of $id $version"
        if dotnet nuget push "$symbols" "${push_args[@]}"; then completed=$(( completed + 1 )); else symbols_failed+=("$id"); fi
        echo "::endgroup::"
        continue
      fi
    fi
    echo "$id $version is already on nuget.org."
    present=$(( present + 1 ))
  done
  (( ${#symbols_failed[@]} == 0 )) || return 1
}

attempt=1 delay="$backoff"
until push_pass; do
  if (( attempt >= attempts )); then
    if [[ -n "$stopped_at" ]]; then
      echo "::error::The push of $version stopped after $attempt passes, the last at $stopped_at; whatever is on nuget.org is a dependency-complete prefix of the set, which this script, run again on the same packages, completes at the same version." >&2
    else
      echo "::error::Every package of $version is pushed, but the symbols of ${symbols_failed[*]} failed in each of $attempt passes. This script, run again on the same packages, pushes only what nuget.org still lacks." >&2
    fi
    exit 1
  fi
  echo "Pass $attempt failed; another in $delay seconds." >&2
  sleep "$delay"
  attempt=$(( attempt + 1 )) delay=$(( delay * 2 ))
done
echo "Pushed $pushed packages, completed the symbols of $completed, found $present already published."

# --- wait until nuget.org serves what was pushed ------------------------------------------------
(( list_timeout > 0 )) || exit 0
deadline=$(( SECONDS + list_timeout ))
while :; do
  waiting=()
  for id in "${order[@]}"; do
    lookup_listed "$id" || exit 1
    if ! $listed; then waiting+=("$id"); continue; fi
    if [[ -f "$packages_dir/$id.$version.snupkg" ]]; then
      lookup_served "$id" || exit 1
      $served || waiting+=("$id (symbols)")
    fi
  done
  if (( ${#waiting[@]} == 0 )); then
    echo "nuget.org lists $version for every package and serves every symbol package."
    exit 0
  fi
  if (( SECONDS >= deadline )); then
    echo "::error::After $list_timeout seconds nuget.org still does not serve $version of: ${waiting[*]}. Re-run the failed job once nuget.org has caught up: it pushes only what is still missing." >&2
    exit 1
  fi
  echo "Waiting for nuget.org: ${waiting[*]}"
  sleep 30
done
