#!/usr/bin/env bash
# Holds preview-gate.sh to what ADR 49 says of it, over a synthetic repository of two packages and a fake flat
# container: publish, repair, none, --site, --recheck and --report, a stable release on the feed, and the lookups
# that must fail rather than guess.
#
# Usage: gate-scenarios.sh        (needs dotnet, python3, jq, git; ci.yml's release scripts job runs it)
set -uo pipefail
scripts="$(cd "$(dirname "$0")/.." && pwd)"
S="$(mktemp -d)"
port="${FAKE_FLAT_PORT:-8767}"
R="$S/repository"; F="$S/feed"
mkdir -p "$R/src/Core" "$R/src/Tool" "$R/.github/scripts" "$F"
cd "$R" || exit 1
git init -q -b main; git config user.email a@b; git config user.name a
cp "$scripts"/{preview-gate.sh,package-ids.sh,semver.jq,release-pack.sh} .github/scripts/
cp "$scripts/../../.releaserc.json" .
cat > Directory.Build.props <<'X'
<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><IsPackable>false</IsPackable></PropertyGroup></Project>
X
cat > Directory.Packages.props <<'X'
<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup>
<ItemGroup><PackageVersion Include="xunit.v3" Version="1.0.0" /></ItemGroup></Project>
X
echo '{ "sdk": { "version": "10.0.100", "rollForward": "latestMinor" } }' > global.json
cat > src/Core/Core.csproj <<'X'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><IsPackable>true</IsPackable><PackageId>AdCodicem.Pdf</PackageId></PropertyGroup></Project>
X
cat > src/Tool/Tool.csproj <<'X'
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><IsPackable>true</IsPackable><PackageId>AdCodicem.Pdf.Tool</PackageId></PropertyGroup></Project>
X
printf '{"solution":{"path":"../x.slnx","projects":["src/Core/Core.csproj","src/Tool/Tool.csproj"]}}\n' > src/AdCodicem.Pdf.Packages.slnf
printf '<Solution>\n  <Project Path="src/Core/Core.csproj" />\n  <Project Path="src/Tool/Tool.csproj" />\n</Solution>\n' > x.slnx
echo "class A {}" > src/Core/A.cs; echo "class B {}" > src/Tool/B.cs; mkdir -p docs; echo doc > docs/x.md
git add -A; git commit -qm "chore: initial"; git tag v0.1.0
c() { echo "$2" >> "$1"; git add -A; git commit -qm "$3"; git rev-parse HEAD; }
C1=$(c src/Core/A.cs "// 1" "fix: one")          # height 1
C2=$(c docs/x.md "more" "docs: two")              # height 2, no package input
C3=$(c src/Tool/B.cs "// 3" "fix: three")        # height 3
C4=$(c docs/x.md "more" "docs: four")             # height 4

python3 -m http.server "$port" --bind 127.0.0.1 --directory "$F" >/dev/null 2>&1 &
server=$!
trap 'kill "$server" 2>/dev/null; rm -rf "$S"' EXIT
until curl --silent --output /dev/null "http://127.0.0.1:$port/"; do sleep 0.2; done
feed() { # feed "<id>:<version>:<commit>" ...
  rm -rf "${F:?}"/*; local spec id v commit
  declare -A lists=()
  for spec in "$@"; do
    IFS=: read -r id v commit <<<"$spec"; local lid="${id,,}"
    mkdir -p "$F/$lid/$v"
    if [[ "$commit" == none ]]; then echo "<package><metadata><id>$id</id></metadata></package>" > "$F/$lid/$v/$lid.nuspec"
    else echo "<package><metadata><id>$id</id><repository type=\"git\" commit=\"$commit\" /></metadata></package>" > "$F/$lid/$v/$lid.nuspec"; fi
    lists[$lid]+="\"$v\","
  done
  for id in "${!lists[@]}"; do echo "{\"versions\":[${lists[$id]%,}]}" > "$F/$id/index.json"; done
}
pass=0 fail=0
run() { # run <name> <expect-exit> <expect-grep|-> <head> [args...] (env via ENVS)
  local name="$1" want="$2" grep_for="$3" head="$4"; shift 4
  git checkout -q --detach "$head"
  local out code
  local envs=()
  read -ra envs <<<"$ENVS"
  out="$(env "${envs[@]}" NUGET_FLAT_CONTAINER="http://127.0.0.1:$port" GITHUB_OUTPUT=/dev/stdout GITHUB_STEP_SUMMARY=/dev/stdout .github/scripts/preview-gate.sh "$@" 2>&1)"; code=$?
  if [[ "$code" == "$want" ]] && { [[ "$grep_for" == - ]] || grep -qE -- "$grep_for" <<<"$out"; }; then
    pass=$((pass+1)); echo "PASS $name"
  else
    fail=$((fail+1)); echo "FAIL $name (exit $code, wanted $want, looking for: $grep_for)"; echo "$out" | sed 's/^/    /' | head -20
  fi
  git checkout -q main
}
ENVS=""
feed "AdCodicem.Pdf:0.1.1-preview.1:$C1" "AdCodicem.Pdf.Tool:0.1.1-preview.1:$C1"
run "docs-only change since the base: none" 0 '^mode=none' "$C2" --site
run "package change since the base: publish (site mode)" 0 '^mode=publish' "$C3" --site
ENVS="NEXT_VERSION=0.1.1-preview.3 RELEASE_TYPE=patch"
run "publish with the right version" 0 '^version=0\.1\.1-preview\.3$' "$C3"
ENVS="NEXT_VERSION=0.1.1-preview.4 RELEASE_TYPE=patch"
run "publish refuses a version that is not the height" 1 'is not 0\.1\.1-preview\.3' "$C3"
ENVS="NEXT_VERSION=0.2.0-preview.3 RELEASE_TYPE=patch"
run "publish refuses a version that is not the release type" 1 'is not 0\.1\.1-preview\.3' "$C3"
ENVS="NEXT_VERSION=1.0.0-preview.3 RELEASE_TYPE=major"
run "publish refuses major while breaking is a minor" 1 'cannot answer while' "$C3"
ENVS="NEXT_VERSION= RELEASE_TYPE=patch"
run "publish refuses an empty version" 1 'not of the X\.Y\.Z-preview\.N shape' "$C3"
ENVS="NEXT_VERSION=0.1.1-preview.3 RELEASE_TYPE=bogus"
run "publish refuses an unknown release type" 1 'is not major, minor, patch' "$C3"

# A push that stopped midway: the core has preview.3, the tool lacks it.
feed "AdCodicem.Pdf:0.1.1-preview.1:$C1" "AdCodicem.Pdf.Tool:0.1.1-preview.1:$C1" "AdCodicem.Pdf:0.1.1-preview.3:$C3"
ENVS="NEXT_VERSION=0.1.1-preview.4 RELEASE_TYPE=patch"
run "partial set: repair, nothing pending" 0 '^mode=repair' "$C4"
run "repair completes the base version from the base commit" 0 "^ref=$C3" "$C4"
run "repair names the missing package" 0 'missing from nuget.org for AdCodicem\.Pdf\.Tool' "$C4"
run "repair with nothing pending documents HEAD" 0 "^docs-ref=$(git rev-parse "$C4")" "$C4"
C5=$(c src/Core/A.cs "// 5" "fix: five")
run "repair with package changes pending documents the base" 0 "^docs-ref=$C3" "$C5"
run "repair with package changes pending says so" 0 '^pending=true' "$C5"

# Recheck, as the publish job runs it.
feed "AdCodicem.Pdf:0.1.1-preview.1:$C1" "AdCodicem.Pdf.Tool:0.1.1-preview.1:$C1"
ENVS="GATE_REF=$C3 EXPECT_BASE=0.1.1-preview.1 EXPECT_VERSION=0.1.1-preview.3"
export PACKAGE_IDS="AdCodicem.Pdf AdCodicem.Pdf.Tool"
run "recheck: nuget.org still gives the plan's base" 0 'still gives 0\.1\.1-preview\.1' "$C5" --recheck
feed "AdCodicem.Pdf:0.1.1-preview.1:$C1" "AdCodicem.Pdf.Tool:0.1.1-preview.1:$C1" "AdCodicem.Pdf:0.1.1-preview.3:$C3"
run "recheck: a push of this very run that stopped midway passes" 0 'completes it' "$C5" --recheck
feed "AdCodicem.Pdf:0.1.1-preview.1:$C1" "AdCodicem.Pdf.Tool:0.1.1-preview.1:$C1" "AdCodicem.Pdf:0.1.1-preview.5:$C5" "AdCodicem.Pdf.Tool:0.1.1-preview.5:$C5"
run "recheck: a newer commit was published since" 1 'a newer commit was published since' "$C5" --recheck
ENVS="GATE_REF=$C5 EXPECT_BASE=0.1.1-preview.1 EXPECT_VERSION=0.1.1-preview.5"
feed "AdCodicem.Pdf:0.1.1-preview.1:$C1" "AdCodicem.Pdf.Tool:0.1.1-preview.1:$C1" "AdCodicem.Pdf:0.1.1-preview.4:$C4" "AdCodicem.Pdf.Tool:0.1.1-preview.4:$C4"
run "recheck: another base was published since" 1 'something was published since' "$C5" --recheck
unset PACKAGE_IDS

# Report, as release.yml runs it.
ENVS=""
feed "AdCodicem.Pdf:0.1.1-preview.1:$C1" "AdCodicem.Pdf.Tool:0.1.1-preview.1:$C1"
run "report: not previewed" 0 'Not previewed' "$C5" --report
feed "AdCodicem.Pdf:0.1.1-preview.1:$C1" "AdCodicem.Pdf.Tool:0.1.1-preview.1:$C1" "AdCodicem.Pdf:0.1.1-preview.3:$C3"
run "report: partly published" 0 'Partly published' "$C4" --report
feed "AdCodicem.Pdf:0.1.1-preview.5:$C5" "AdCodicem.Pdf.Tool:0.1.1-preview.5:$C5"
run "report: everything previewed" 0 'already on nuget\.org' "$C5" --report

# Failures that must not guess.
feed "AdCodicem.Pdf:0.1.1-preview.1:none" "AdCodicem.Pdf.Tool:0.1.1-preview.1:$C1"
run "a nuspec without a commit fails" 1 'names no commit' "$C3" --site
feed "AdCodicem.Pdf:0.1.1-preview.1:$C1" "AdCodicem.Pdf.Tool:0.1.1-preview.1:$C1" "AdCodicem.Pdf.Tool:0.1.1-preview.2:0123456789abcdef0123456789abcdef01234567"
run "a commit this clone lacks fails" 1 'does not have' "$C3" --site
feed "AdCodicem.Pdf:0.1.1-preview.5:$C5" "AdCodicem.Pdf.Tool:0.1.1-preview.5:$C5"
run "a version from a commit that is not an ancestor fails" 1 'not an ancestor' "$C3" --site
rm -rf "${F:?}"/*; mkdir -p "$F/adcodicem.pdf"; echo 'oops' > "$F/adcodicem.pdf/index.json"
run "a feed that answers garbage fails" 1 'not a list of versions' "$C3" --site
rm -rf "${F:?}"/*
run "nothing on nuget.org since the stable tag fails" 1 'has no version of these packages' "$C3" --site

# A version already taken, and one below the newest.
feed "AdCodicem.Pdf:0.1.1-preview.1:$C1" "AdCodicem.Pdf.Tool:0.1.1-preview.1:$C1" "AdCodicem.Pdf:0.1.1-preview.3:$C1" "AdCodicem.Pdf.Tool:0.1.1-preview.3:$C1"
ENVS="NEXT_VERSION=0.1.1-preview.3 RELEASE_TYPE=patch"
run "a version nuget.org already has fails" 1 'already on nuget\.org' "$C3"
feed "AdCodicem.Pdf:0.1.1-preview.1:$C1" "AdCodicem.Pdf.Tool:0.1.1-preview.1:$C1" "AdCodicem.Pdf:0.1.1-preview.10:$C1" "AdCodicem.Pdf.Tool:0.1.1-preview.10:$C1"
run "a version below the newest, sorted in SemVer, warns" 0 'sorts below 0\.1\.1-preview\.10' "$C3"

# A stable release on the feed: the release and a preview from the same commit tie, the release wins.
git checkout -q main; git tag v0.1.1 "$C3"
feed "AdCodicem.Pdf:0.1.1:$C3" "AdCodicem.Pdf.Tool:0.1.1:$C3" "AdCodicem.Pdf:0.1.1-preview.3:$C3" "AdCodicem.Pdf.Tool:0.1.1-preview.3:$C3"
ENVS=""
run "after a release, the base is the release" 0 '^base-version=0\.1\.1$' "$C4" --site
run "after a release, docs-only: none" 0 '^mode=none' "$C4" --site
ENVS="NEXT_VERSION=0.1.2-preview.2 RELEASE_TYPE=patch"
run "after a release, the height counts from its tag" 0 '^version=0\.1\.2-preview\.2$' "$C5"
feed "AdCodicem.Pdf:0.1.1:$C3"
ENVS=""
run "a release left partial is completed by hand" 1 'not a preview' "$C4" --site
# Directory.Packages.props: a test package's version publishes nothing, a setting outside the versions does.
git checkout -q main
sed -i 's/Version="1.0.0"/Version="1.0.1"/' Directory.Packages.props; git commit -qam "build(deps): bump xunit.v3"
C7=$(git rev-parse HEAD)
feed "AdCodicem.Pdf:0.1.2-preview.2:$C5" "AdCodicem.Pdf.Tool:0.1.2-preview.2:$C5"
ENVS=""
run "a test package's version is not a package input" 0 '^mode=none' "$C7" --site
sed -i 's|<ManagePackageVersionsCentrally>|<CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled><ManagePackageVersionsCentrally>|' Directory.Packages.props
git commit -qam "build: pin transitively"
run "a setting outside the versions is a package input" 0 'outside the PackageVersion entries' "$(git rev-parse HEAD)" --site
echo "gate scenarios: $pass passed, $fail failed"
(( fail == 0 ))
