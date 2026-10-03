#!/usr/bin/env bash
# Holds push-packages.sh to what ADR 49 says of it, against fake_feed.py: three packages packed here, one depending
# on the next, pushed through answers nuget.org gives (201, 409, 500, a listing that comes late or never).
#
# Usage: push-scenarios.sh        (needs dotnet, python3, jq, unzip; ci.yml's release scripts job runs it)
set -uo pipefail

scripts="$(cd "$(dirname "$0")/.." && pwd)"
work="$(mktemp -d)"
port="${FAKE_FEED_PORT:-8770}"
server=""
trap '[[ -z "$server" ]] || kill "$server" 2>/dev/null; rm -rf "$work"' EXIT
version=0.2.0-preview.9

# Three packages whose file order is not their dependency order: AdCodicem.Base, first by name, depends on
# AdCodicem.Pdf.Tool, which depends on AdCodicem.Pdf. Packed outside the repository, away from its props.
mkdir -p "$work/src/Core" "$work/src/Tool" "$work/src/Base" "$work/packages"
echo '<Project />' >"$work/src/Directory.Build.props"
echo '<Project />' >"$work/src/Directory.Packages.props"
project() { # <dir> <id> [reference]
  local reference=""
  [[ -z "${3:-}" ]] || reference="<ItemGroup><ProjectReference Include=\"../$3/$3.csproj\" /></ItemGroup>"
  cat >"$work/src/$1/$1.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><PackageId>$2</PackageId><IncludeSymbols>true</IncludeSymbols><SymbolPackageFormat>snupkg</SymbolPackageFormat></PropertyGroup>$reference</Project>
EOF
  echo "public class $1Type {}" >"$work/src/$1/$1.cs"
}
project Core AdCodicem.Pdf
project Tool AdCodicem.Pdf.Tool Core
project Base AdCodicem.Base Tool
for name in Core Tool Base; do
  dotnet pack "$work/src/$name" -c Release -p:Version="$version" -o "$work/packages" --nologo -v q >/dev/null ||
    { echo "::error::Could not pack the $name test package." >&2; exit 1; }
done

state="$work/feed"
start() { # <script.json>
  [[ -z "$server" ]] || kill "$server" 2>/dev/null
  rm -rf "$state"; mkdir -p "$state"; echo "$1" >"$state/script.json"; : >"$state/log.txt"
  python3 "$scripts/tests/fake_feed.py" "$state" "$port" & server=$!
  local tries=0
  until curl --silent --fail "http://127.0.0.1:$port/v3/index.json" >/dev/null; do
    (( ++tries < 50 )) || { echo "::error::The fake feed did not start." >&2; exit 1; }
    sleep 0.2
  done
}
out="" code=0
push() { # [list timeout]
  out="$(NUGET_API_KEY=key NUGET_SOURCE="http://127.0.0.1:$port/v3/index.json" \
    NUGET_FLAT_CONTAINER="http://127.0.0.1:$port/flat" NUGET_SYMBOL_PACKAGES="http://127.0.0.1:$port/sym" \
    NUGET_PUSH_BACKOFF=1 NUGET_PUSH_ATTEMPTS=3 NUGET_LIST_TIMEOUT="${1:-0}" \
    "$scripts/push-packages.sh" "$work/packages" "$version" 2>&1)"
  code=$?
}
pass=0 fail=0
check() { # <name> <exit> <output regex or -> [log regex]... ; a log regex prefixed with ! must not match
  local name="$1" want="$2" pattern="$3" ok=true re
  shift 3
  [[ "$code" == "$want" ]] || ok=false
  [[ "$pattern" == - ]] || grep -qE -- "$pattern" <<<"$out" || ok=false
  for re in "$@"; do
    if [[ "$re" == '!'* ]]; then
      ! grep -qE -- "${re#!}" "$state/log.txt" || ok=false
    else
      grep -qE -- "$re" "$state/log.txt" || ok=false
    fi
  done
  if $ok; then
    pass=$(( pass + 1 )); echo "PASS $name"
  else
    fail=$(( fail + 1 )); echo "FAIL $name (exit $code, expected $want)"
    tail -15 <<<"$out" | sed 's/^/    out: /'; sed 's/^/    log: /' "$state/log.txt"
  fi
}
many() { printf '500, %.0s' $(seq 1 "$1") | sed 's/, $//'; }

start '{}'; push
order="$(grep '^nupkg' "$state/log.txt" | cut -d' ' -f2 | paste -sd' ' -)"
[[ "$order" == "adcodicem.pdf adcodicem.pdf.tool adcodicem.base" ]] || code=99
check "every package after the ones it depends on, whatever the file order ($order)" 0 'Pushed 3 packages' \
  '^snupkg adcodicem.pdf '"$version"' 201' '^snupkg adcodicem.base '"$version"' 201'

# dotnet nuget push tries a 5xx three times itself: thirty outlast three passes.
start '{"fail": {"adcodicem.pdf.tool.nupkg": ['"$(many 30)"']}}'; push
check "a package that keeps failing stops every pass before what depends on it" 1 \
  'stopped after 3 passes, the last at AdCodicem.Pdf.Tool' '!^nupkg adcodicem.base'

start '{"fail": {"adcodicem.pdf.tool.nupkg": ['"$(many 4)"']}}'; push
check "a failure that clears is completed by a later pass, at the same version" 0 'Pass 1 failed' \
  '^nupkg adcodicem.base '"$version"' 201'

start '{"fail": {"adcodicem.pdf.nupkg": [409]}}'; push
check "a package nuget.org holds unlisted (409) still gets its symbols" 0 - \
  '^nupkg adcodicem.pdf '"$version"' 409' '^snupkg adcodicem.pdf '"$version"' 201'

start '{"fail": {"adcodicem.pdf.snupkg": ['"$(many 12)"']}}'; push
check "a symbol package that keeps failing fails the job once every package is pushed" 1 \
  'symbols of AdCodicem.Pdf failed' '^nupkg adcodicem.base '"$version"' 201'

start '{"fail": {"adcodicem.pdf.snupkg": ['"$(many 3)"']}}'; push
check "a symbol package that failed a pass is pushed on its own by the next" 0 'completed the symbols of 1'

start '{}'; push; push
check "run again, it pushes nothing nuget.org has" 0 'Pushed 0 packages, completed the symbols of 0, found 3 already published'

start '{"list_after": 2}'; push 90
check "it waits until nuget.org lists every package and serves every symbol package" 0 "lists $version for every package"

start '{"list_after": 1000}'; push 5
check "a listing that never comes fails the job" 1 'still does not serve'

start '{"flat_status": 500}'; push
check "a lookup that fails pushes nothing" 1 'answered HTTP 500' '!^nupkg'

# The trap the script exists for, measured rather than assumed: a .nupkg nuget.org answers 409, pushed with its
# symbols beside it under --skip-duplicate, never sends the symbols.
start '{"fail": {"adcodicem.pdf.nupkg": [409]}}'
out="$(dotnet nuget push "$work/packages/AdCodicem.Pdf.$version.nupkg" --api-key key --skip-duplicate \
  --source "http://127.0.0.1:$port/v3/index.json" --allow-insecure-connections 2>&1)"; code=$?
check "the trap: --skip-duplicate on a 409 leaves the symbols behind" 0 - '^nupkg adcodicem.pdf' '!^snupkg'

echo "push scenarios: $pass passed, $fail failed"
(( fail == 0 ))
