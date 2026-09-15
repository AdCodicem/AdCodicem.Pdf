#!/bin/sh
# Prints the version to compare the public API against, or nothing when there is none to compare with.
#
# Usage: published-baseline.sh <last release version>
#
# Package validation downloads its baseline from nuget.org, and fails the build when that version does
# not exist there (NU1101). The last tag is not proof of a published package: v0.1.0 was tagged by hand as
# the starting point, and nothing was ever published under it. So the candidate is returned only if
# nuget.org actually has it.
#
# A package nuget.org has never heard of answers 404, which means "nothing to compare with yet". Anything
# else that is not a clean answer fails the run rather than skipping the check: a compatibility check that
# silently turns itself off on a network hiccup is not a check.
set -eu

candidate="${1:-}"
if [ -z "$candidate" ]; then
  exit 0
fi

index="$(mktemp)"
trap 'rm -f "$index"' EXIT

status="$(curl --silent --show-error --location --max-time 30 \
  --output "$index" --write-out '%{http_code}' \
  https://api.nuget.org/v3-flatcontainer/adcodicem.pdf/index.json)"

case "$status" in
  200)
    # The flat container lists versions lower-cased and normalised, one quoted string each.
    normalised="$(printf '%s' "$candidate" | tr '[:upper:]' '[:lower:]')"
    if grep -q "\"$normalised\"" "$index"; then
      printf '%s' "$candidate"
    else
      echo "No published package for $candidate; the API is not compared with it." >&2
    fi
    ;;
  404)
    echo "Nothing published on nuget.org yet; there is no API to compare with." >&2
    ;;
  *)
    echo "nuget.org answered HTTP $status for the package index; refusing to guess the baseline." >&2
    exit 1
    ;;
esac
