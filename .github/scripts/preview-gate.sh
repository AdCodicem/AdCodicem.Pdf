#!/usr/bin/env bash
# Decides what preview.yml publishes, from what nuget.org already has. The packages move together, so the
# decision is one for the whole set: all of them at one version, or none (ADR 49).
#
# Usage: NEXT_VERSION=<x.y.z-preview.n> RELEASE_TYPE=<major|minor|patch|> preview-gate.sh
#                                   what preview.yml does on its schedule and on dispatch: mode=publish,
#                                   repair or none. NEXT_VERSION and RELEASE_TYPE are what next-version.mjs
#                                   computed for HEAD; a publish checks the first against the second.
#        preview-gate.sh --site     what preview.yml does on a push to main: the same decision, without a
#                                   version, so that the site is redeployed only when nothing a package is
#                                   built from changed since the version nuget.org has (mode=none).
#        preview-gate.sh --report   for release.yml: warns when HEAD carries package inputs that no version on
#                                   nuget.org carries, so no preview did, and when the version it compares with
#                                   is on some packages only
#        GATE_REF=<commit> PACKAGE_IDS=<ids> EXPECT_BASE=<version> EXPECT_VERSION=<version> \
#          preview-gate.sh --recheck
#                                   for preview.yml's publish job, just before it logs in: fails unless the
#                                   base nuget.org gives GATE_REF is still the one the plan saw, or the version
#                                   being published (a push of this very run that stopped midway). A re-run of
#                                   an old run, after a newer commit was published, stops there. Needs no
#                                   restore: the IDs come from PACKAGE_IDS.
#
# The base is a version on nuget.org and the commit it was packed from, read off its nuspec
# (<repository commit="..."/>, which the SDK's SourceLink writes): of the versions from the last stable tag
# merged into HEAD on, across every package ID, the one packed from the commit nearest to HEAD. Not the
# previous run of the workflow: a run can stop after part of its push, a stable release publishes too, and
# nuget.org is the one record that survives both. Not the highest in SemVer order: a feature reverted after
# its preview shipped leaves that preview above every later version, and every later commit would differ
# from it.
#
#   repair   a package the base commit packed lacks the base version: a push stopped midway. nuget.org has
#            no transaction, so the missing IDs are completed at that version, from that commit, before
#            anything newer is published.
#   publish  a package input changed between the base commit and HEAD.
#   none     nothing a package is built from changed.
#
# Outputs, to $GITHUB_OUTPUT (stdout outside Actions):
#   mode              publish | repair | none
#   ref               the commit to test, pack and publish: HEAD, or the base commit for a repair
#   version           the version to pack: NEXT_VERSION for a publish, the base version for a repair, empty
#                     for none and with --site
#   base-version      the version on nuget.org packed from the commit nearest to HEAD, from the last stable
#                     tag on; never empty: a tag nuget.org has nothing from fails
#   base-commit       the commit it was packed from
#   last-stable       the last stable release merged into HEAD, read off the tags
#   docs-ref          the commit /preview/ is built from: HEAD, unless a repair leaves HEAD with package
#                     changes nuget.org does not have yet
#   docs-version      the label of /preview/: the version published, or the base version
#   pending           for a repair, whether HEAD has package changes beyond the repaired commit
#   reason            one line, for the job summary
#
# A lookup that fails fails the run rather than guessing. A guess that publishes adds a version to nuget.org
# for good; a guess that skips hides a change until someone notices. A failed run publishes nothing, shows in
# the Actions tab, and the next run decides again. With --report the same failure is a warning: release.yml
# runs this step with continue-on-error.
#
# NUGET_FLAT_CONTAINER overrides the feed, to replay a decision against a recorded state.
set -euo pipefail

report=false recheck=false site=false
case "${1:-}" in
  --report) report=true ;;
  --recheck) recheck=true ;;
  --site) site=true ;;
  "") ;;
  *) echo "::error::Unknown argument '$1'." >&2; exit 1 ;;
esac

root="$(cd "$(dirname "$0")/../.." && pwd)"
scripts="$root/.github/scripts"
feed="${NUGET_FLAT_CONTAINER:-https://api.nuget.org/v3-flatcontainer}"
output="${GITHUB_OUTPUT:-/dev/stdout}"
summary="${GITHUB_STEP_SUMMARY:-/dev/stderr}"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
cd "$root"

fail() {
  if $report; then
    echo "::warning title=Not checked::Could not tell whether this release was previewed: $*" >&2
  else
    echo "::error::$*" >&2
  fi
  exit 1
}

# What can change the bytes or the metadata of a package, apart from Directory.Packages.props, which is
# filtered below. src/ holds the code, the generated Arlington tables and the solution filter the pack goes
# through. README.md, assets/icon.png, NOTICE and the Arlington model's LICENSE are packed into the core
# (Directory.Build.props, src/AdCodicem.Pdf/AdCodicem.Pdf.csproj). release-pack.sh holds the pack command
# line. Not docs/ or the site, which no package carries; not tests/, samples/, benchmarks/ or the rest of
# tools/, which the pack never builds (the Arlington tables it generates are committed under src/); not
# LICENSE, which PackageLicenseExpression replaces; not .editorconfig, which changes diagnostics only; not the
# solution, which selects projects and changes nothing in them; not the workflows. The SDK is not an input
# either: the workflows install the latest 10.0 patch, and a new one publishes nothing by itself. A file that
# comes to change what a package carries has to join this list.
inputs=(
  src
  README.md
  assets/icon.png
  NOTICE
  tools/AdCodicem.Pdf.Arlington/model/LICENSE
  Directory.Build.props
  Directory.Build.targets
  Directory.Build.rsp
  global.json
  ':(icase)nuget.config'
  .gitattributes
  .github/scripts/release-pack.sh
)

# --- nuget.org ----------------------------------------------------------------------------------------

# One package's versions, lowercased, one per line, and nothing on a 404: nuget.org has never had it. The
# flat container lists unlisted versions too, which is wanted: an unlisted version can still be installed by
# number.
versions_of() {
  local code
  code="$(curl --silent --show-error --location --retry 3 --output "$work/index.json" \
    --write-out '%{http_code}' "$feed/${1,,}/index.json")" || fail "nuget.org did not answer for $1."
  case "$code" in
    200) jq -r '.versions[] | ascii_downcase' "$work/index.json" 2>/dev/null ||
      fail "nuget.org answered for $1 with something that is not a list of versions." ;;
    404) ;;
    *) fail "nuget.org answered HTTP $code for $1." ;;
  esac
}

# The newest of the versions on stdin, one per line, in SemVer order -- not in the order the feed lists them.
newest() {
  jq -Rrs -L "$scripts" 'include "semver"; split("\n") | map(select(length > 0)) | max_by(semver_key) // empty'
}

# $work/published holds one "<id> <version> <version>..." line per package ID looked up.
holder_of() { awk -v v="$1" '{ for (i = 2; i <= NF; i++) if ($i == v) { print $1; exit } }' "$work/published"; }
has_version() { awk -v id="$1" -v v="$2" '$1 == id { for (i = 2; i <= NF; i++) if ($i == v) found = 1 } END { exit !found }' "$work/published"; }

commit_of() {
  local commit
  commit="$(curl --fail --silent --show-error --location --retry 3 "$feed/${1,,}/$2/${1,,}.nuspec" |
    grep -oP '<repository\b[^>]*\bcommit="\K[0-9a-f]{40}')" || fail "The nuspec of $1 $2 names no commit."
  git cat-file -e "$commit^{commit}" 2>/dev/null ||
    fail "$1 $2 was packed from $commit, which this clone does not have (fetch-depth: 0?)."
  git merge-base --is-ancestor "$commit" "$head" ||
    fail "$1 $2 was packed from $commit, which is not an ancestor of ${head:0:7}: a newer commit was published since, or the history was rewritten."
  echo "$commit"
}

record() { # <id>...: appends "<id> <versions...>" to $work/published for each ID not there yet
  local id versions
  for id in "$@"; do
    [[ -n "$id" ]] || continue
    if [[ -f "$work/published" ]] && awk -v id="$id" '$1 == id { found = 1 } END { exit !found }' "$work/published"; then
      continue
    fi
    versions="$(versions_of "$id")"
    echo "$id $(paste -sd ' ' <<<"$versions")" >>"$work/published"
  done
}

# --- package inputs -----------------------------------------------------------------------------------

# The package IDs a commit packs, evaluated from its own tree: a package added since has no reason to carry
# that commit's version.
ids_at() {
  if [[ "$1" == "$(git rev-parse HEAD)" ]]; then
    "$scripts/package-ids.sh"
  else
    local paths=()
    mapfile -t paths < <(git ls-tree --name-only "$1" -- src Directory.Build.props Directory.Build.targets \
      Directory.Packages.props global.json)
    rm -rf "$work/tree" && mkdir -p "$work/tree"
    git archive "$1" -- "${paths[@]}" | tar -x -C "$work/tree"
    "$scripts/package-ids.sh" "$work/tree"
  fi
}

# Every package in the restore graph of the projects under src/, lowercased: what they reference, what that
# pulls in, and what the SDK adds (Microsoft.NET.ILLink.Tasks, for trimming). Restored only when needed.
shipped=""
load_shipped() {
  [[ -z "$shipped" ]] || return 0
  dotnet restore src/AdCodicem.Pdf.Packages.slnf --verbosity quiet >&2 ||
    fail "Could not restore the packable projects."
  # The projects the solution filter restores, not every project under src/: one the filter leaves out, an
  # analyzer that ships nothing say, has no restore graph here.
  local assets=() project projects
  projects="$(jq -r '.solution.projects[]' src/AdCodicem.Pdf.Packages.slnf)" || fail "Could not read the solution filter."
  while read -r project; do
    [[ -n "$project" ]] || continue
    project="$(dirname "$project")/obj/project.assets.json"
    [[ -f "$project" ]] || fail "No restore graph at $project."
    assets+=("$project")
  done <<<"$projects"
  (( ${#assets[@]} > 0 )) || fail "The solution filter lists no project."
  shipped="$(jq -r '.libraries | to_entries[] | select(.value.type == "package")
    | .key | split("/")[0] | ascii_downcase' "${assets[@]}" | LC_ALL=C sort -u)"
}

# Directory.Packages.props also pins the test, benchmark and comparison packages. Comments are dropped; a
# change to anything but a PackageVersion element counts, and so does the PackageVersion of a package in the
# graph above.
strip_comments() { perl -0777 -pe 's/<!--.*?-->//gs'; }
package_versions() {
  strip_comments | perl -0777 -ne 'while (/<PackageVersion\b([^>]*?)\/>/gs) {
    my $a = $1; $a =~ s/\s+/ /g; my ($id) = $a =~ /Include="([^"]+)"/; print lc($id), "\t", $a, "\n" }' |
    LC_ALL=C sort
}
other_settings() { strip_comments | perl -0777 -pe 's/<PackageVersion\b[^>]*?\/>//gs; s/\s+//g'; }

# Sets $changed to what changed between two commits, one entry per line; empty when nothing did. Called as a
# plain statement, never in a condition or a $( ), so that set -e still holds inside.
changed=""
package_inputs_changed() {
  local before after versions hits
  changed="$(git diff --name-only "$1" "$2" -- "${inputs[@]}")"
  before="$(git show "$1:Directory.Packages.props")"
  after="$(git show "$2:Directory.Packages.props")"
  [[ "$before" != "$after" ]] || return 0
  if [[ "$(other_settings <<<"$before")" != "$(other_settings <<<"$after")" ]]; then
    changed="$(printf '%s\n' "$changed" "Directory.Packages.props (outside the PackageVersion entries)" | sed '/^$/d')"
    return 0
  fi
  versions="$(LC_ALL=C comm -3 <(package_versions <<<"$before") <(package_versions <<<"$after") |
    sed 's/^\t//' | cut -f1 | LC_ALL=C sort -u)"
  [[ -n "$versions" ]] || return 0
  load_shipped
  hits="$(LC_ALL=C comm -12 <(echo "$versions") <(echo "$shipped") | sed 's/^/Directory.Packages.props: /')"
  [[ -z "$hits" ]] || changed+=$'\n'"$hits"
  changed="$(sed '/^$/d' <<<"$changed")"
}

# --- decision -----------------------------------------------------------------------------------------

# The commit decided about: HEAD, or for --recheck the commit the plan packed, which must be HEAD or one of
# its ancestors (a repair packs the base commit).
if $recheck; then
  : "${GATE_REF:?GATE_REF required with --recheck}" "${PACKAGE_IDS:?PACKAGE_IDS required with --recheck}"
  : "${EXPECT_BASE:?EXPECT_BASE required with --recheck}" "${EXPECT_VERSION:?EXPECT_VERSION required with --recheck}"
  head="$(git rev-parse --verify --quiet "$GATE_REF^{commit}")" || fail "GATE_REF $GATE_REF is not a commit of this clone."
  git merge-base --is-ancestor "$head" HEAD || fail "GATE_REF $GATE_REF is not HEAD or one of its ancestors."
  head_ids="$(tr -s ' \n' '\n' <<<"$PACKAGE_IDS" | sed '/^$/d')"
else
  head="$(git rev-parse HEAD)"
  head_ids="$(ids_at "$head")"
fi
[[ -n "$head_ids" ]] || fail "No packable project under src/."
mapfile -t ids <<<"$head_ids"
record "${ids[@]}"

# The last stable release merged into the commit, read off the tags as next-version.mjs reads them.
tags="$(git tag --merged "$head" --list 'v*')"
last_stable="$(sed -n 's/^v\([0-9]\{1,\}\.[0-9]\{1,\}\.[0-9]\{1,\}\)$/\1/p' <<<"$tags" | newest)"
[[ -n "$last_stable" ]] || fail "No stable tag v<major>.<minor>.<patch> is merged into HEAD."

# The base: of the versions nuget.org has from the last stable release on, the one packed from the commit
# nearest to HEAD. A preview and the stable release cut from the same commit tie, and the release, the higher,
# wins: the base is then the newest version of any kind, which is what release.yml asks about too.
newest_version="$(cut -d' ' -f2- "$work/published" | tr ' ' '\n' | newest)"
candidates="$(cut -d' ' -f2- "$work/published" | tr ' ' '\n' | sed '/^$/d' | LC_ALL=C sort -u |
  jq -Rrs -L "$scripts" --arg floor "$last_stable" 'include "semver";
    split("\n") | map(select(length > 0) | select(semver_key >= ($floor | semver_key)))
    | sort_by(semver_key) | reverse | .[]')"
base_version="" base="" best=""
if [[ -n "$candidates" ]]; then
  mapfile -t candidates <<<"$candidates"
  for candidate in "${candidates[@]}"; do
    commit="$(commit_of "$(holder_of "$candidate")" "$candidate")"
    distance="$(git rev-list --count "$commit..$head")"
    if [[ -z "$best" ]] || (( distance < best )); then
      best="$distance" base="$commit" base_version="$candidate"
    fi
  done
fi

# v<last_stable> is tagged, so nuget.org must have something from it on: the release itself at least, or for
# v0.1.0, the baseline tagged by hand with no package behind it, the previews published since. Nothing means the release's push failed
# (semantic-release tags before it publishes) or nuget.org has not listed it yet. Publishing on top would be a
# guess, so stop instead.
[[ -n "$base_version" ]] ||
  fail "v$last_stable is tagged, but nuget.org has no version of these packages from $last_stable on: the release's push failed, or nuget.org has not listed it yet. Complete the release, or wait, then run again."

# preview.yml's publish job asks whether nuget.org still gives the base the plan saw.
if $recheck; then
  expected_base="${EXPECT_BASE,,}" expected_version="${EXPECT_VERSION,,}"
  if [[ "$base_version" == "$expected_version" ]]; then
    [[ "$base" == "$head" ]] ||
      fail "$EXPECT_VERSION is on nuget.org, packed from ${base:0:7}, not from ${head:0:7}, which this run packed."
    echo "$EXPECT_VERSION is partly or wholly on nuget.org already, from ${head:0:7}: this run completes it." >&2
  elif [[ "$base_version" == "$expected_base" ]]; then
    echo "nuget.org still gives $base_version as the base of ${head:0:7}, as when this run decided." >&2
  else
    fail "nuget.org now gives $base_version (${base:0:7}) as the base of ${head:0:7}, where this run decided from $EXPECT_BASE: something was published since. Dispatch preview.yml again rather than re-running this run."
  fi
  exit 0
fi

# What the base commit packed: it may hold an ID that HEAD no longer does, and lack one HEAD adds.
base_ids="$(ids_at "$base")"
mapfile -t base_ids <<<"$base_ids"
record "${base_ids[@]}"
missing=()
for id in "${base_ids[@]}"; do
  has_version "$id" "$base_version" || missing+=("$id")
done

# release.yml asks whether HEAD ships anything that no version on nuget.org carries: what changed since the
# base, and the packages the base version never reached, which carry none of it.
if $report; then
  package_inputs_changed "$base" "$head"
  if (( ${#missing[@]} > 0 )); then
    hint="Dispatching preview.yml first completes it."
    [[ "$base_version" == *-* ]] || hint="Complete that release by hand first (docs/releasing.md)."
    {
      echo "### Partly published"
      echo
      echo "$base_version, the version on nuget.org built from the nearest commit, \`${base:0:7}\`, is missing for ${#missing[@]} of its packages, which therefore carry none of what changed up to that commit:"
      echo
      # shellcheck disable=SC2016 # backticks for Markdown
      printf -- '- `%s`\n' "${missing[@]}"
      echo
      echo "$hint"
    } >>"$summary"
    echo "::warning title=Partly published::$base_version is missing from nuget.org for ${missing[*]}." >&2
  fi
  if [[ -z "$changed" ]]; then
    (( ${#missing[@]} > 0 )) ||
      echo "Every package input of this release is already on nuget.org, in $base_version (\`${base:0:7}\`)." >>"$summary"
    exit 0
  fi
  kind=preview
  [[ "$base_version" == *-* ]] || kind=release
  {
    echo "### Not previewed"
    echo
    echo "This release ships package inputs that no preview carried. They changed since $base_version, the $kind on nuget.org built from the nearest commit, \`${base:0:7}\`:"
    echo
    # shellcheck disable=SC2016 # backticks for Markdown
    sed 's/^/- `/; s/$/`/' <<<"$changed"
    echo
    echo "Dispatching preview.yml first publishes them as a preview."
  } >>"$summary"
  echo "::warning title=Not previewed::This release ships package inputs that no preview carried, changed since $base_version: $(paste -sd ' ' <<<"$changed")" >&2
  exit 0
fi

mode="" ref="$head" version="" pending="" docs_ref="$head"

package_inputs_changed "$base" "$head"
if (( ${#missing[@]} > 0 )); then
  [[ "$base_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+-preview\.[0-9]+$ ]] ||
    fail "$base_version is missing from nuget.org for ${missing[*]}, but it is not a preview: a stable release left partial is completed by hand (docs/releasing.md)."
  mode=repair
  ref="$base"
  version="$base_version"
  reason="$base_version is missing from nuget.org for ${missing[*]}: completing it from ${base:0:7}."
  if [[ -n "$changed" ]]; then
    pending=true
    docs_ref="$base"
    reason+=" HEAD has further package changes, for the next run."
  else
    pending=false
  fi
elif [[ -n "$changed" ]]; then
  mode=publish
  reason="Package inputs changed since $base_version (${base:0:7}): $(paste -sd ' ' <<<"$changed")"
else
  mode=none
  reason="No package input changed since $base_version (${base:0:7})."
fi

# A publish takes the version next-version.mjs computed for HEAD. That job runs third-party JavaScript, so
# only its release type is taken from it: the version is computed again here, from the last stable tag and
# the commits since, and must be the same. The commit analyzer cannot answer major while .releaserc.json
# rates a breaking change a minor, so a major is refused then. The version must also be new: one that
# nuget.org already has was packed from another commit, and pushing would skip every package as a duplicate.
# With --site nothing is published, and no version is asked for.
if [[ "$mode" == publish ]] && ! $site; then
  version="${NEXT_VERSION:-}"
  [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+-preview\.[0-9]+$ ]] ||
    fail "NEXT_VERSION '$version' is not of the X.Y.Z-preview.N shape that next-version.mjs computes."
  release_type="${RELEASE_TYPE:-}"
  [[ "$release_type" =~ ^(major|minor|patch)?$ ]] ||
    fail "RELEASE_TYPE '$release_type' is not major, minor, patch or empty."
  if [[ "$release_type" == major ]] && git show "$head:.releaserc.json" | jq -e '[.plugins[]
    | select(type == "array" and .[0] == "@semantic-release/commit-analyzer") | .[1].releaseRules // [] | .[]]
    | any(.breaking == true and .release == "minor") and all(.release != "major")' >/dev/null; then
    fail "RELEASE_TYPE is major, which the commit analyzer cannot answer while .releaserc.json rates a breaking change a minor."
  fi
  IFS=. read -r major minor patch <<<"$last_stable"
  case "${release_type:-patch}" in
    major) expected="$((10#$major + 1)).0.0" ;;
    minor) expected="$major.$((10#$minor + 1)).0" ;;
    patch) expected="$major.$minor.$((10#$patch + 1))" ;;
  esac
  expected+="-preview.$(git rev-list --count "v$last_stable..$head")"
  [[ "$version" == "$expected" ]] ||
    fail "NEXT_VERSION $version is not $expected, the ${release_type:-patch} after $last_stable at this commit's height."
  holder="$(holder_of "${version,,}")"
  [[ -z "$holder" ]] ||
    fail "$version is already on nuget.org ($holder), packed from another commit. The preview number counts commits since the last stable tag, so a preview left above a lower release (a feature reverted after its preview shipped) can hold it: the next commit on main moves the number."
  if [[ -n "$newest_version" ]] && ! jq -en -L "$scripts" --arg new "$version" --arg old "$newest_version" \
    'include "semver"; ($new | semver_key) > ($old | semver_key)' >/dev/null; then
    echo "::warning title=Below an earlier version::$version sorts below $newest_version, which nuget.org already has: NuGet offers $newest_version as the latest prerelease until a higher version ships." >&2
  fi
fi
docs_version="$base_version"
if [[ "$mode" == publish ]] && ! $site; then docs_version="$version"; fi

echo "$reason" >&2
{
  echo "mode=$mode"
  echo "ref=$ref"
  echo "version=$version"
  echo "base-version=$base_version"
  echo "base-commit=$base"
  echo "last-stable=$last_stable"
  echo "docs-ref=$docs_ref"
  echo "docs-version=$docs_version"
  echo "pending=$pending"
  echo "reason=$reason"
} >>"$output"
{
  if $site; then
    echo "### Site: $( [[ "$mode" == none ]] && echo "redeployed" || echo "left as it is" )"
    echo
    echo "$reason"
    [[ "$mode" == none ]] ||
      echo "The site waits for the next preview, which publishes these changes: Monday's, or a dispatch of preview.yml."
  else
    echo "### Preview: $mode"
    echo
    echo "$reason"
  fi
  if [[ -n "$changed" ]]; then
    echo
    echo "Package inputs changed since $base_version:"
    echo
    # shellcheck disable=SC2016 # backticks for Markdown
    sed 's/^/- `/; s/$/`/' <<<"$changed"
  fi
} >>"$summary"
