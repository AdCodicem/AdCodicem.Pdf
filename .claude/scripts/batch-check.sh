#!/usr/bin/env bash
# The check a batch's fix workflow ends with (ADR 54, the `batch` skill), run as a script rather than by an agent.
#
# Usage: bash .claude/scripts/batch-check.sh [base]    base defaults to origin/main
#
# It builds the solution in Release (warnings are errors), verifies the code style, runs the whole unit suite as CI
# does, and checks the subjects of the commits since the base against the conventional-commit rules CI applies. The
# suite's timing tests run last in their own collection; run the script while no benchmark or other heavy process
# loads the machine, or a timing test may fail for the load alone (#342). It prints one line per step and exits
# non-zero on the first failure, so a workflow reads its result without reading its logs, which are in
# $TMPDIR/batch-check (or /tmp/batch-check).
set -uo pipefail

base="${1:-origin/main}"
logs="${TMPDIR:-/tmp}/batch-check"
mkdir -p "$logs"

step() {
  local name="$1"; shift
  local log="$logs/$name.log"
  local start=$SECONDS
  if "$@" >"$log" 2>&1; then
    printf 'ok    %-8s %4ds\n' "$name" $((SECONDS - start))
  else
    printf 'FAIL  %-8s %4ds  see %s\n' "$name" $((SECONDS - start)) "$log"
    tail -n 30 "$log"
    exit 1
  fi
}

# Subjects only: a conventional type, an optional scope, an optional '!', at most 100 characters, no issue number.
check_subjects() {
  local bad=0
  while IFS= read -r subject; do
    if ! [[ "$subject" =~ ^(feat|fix|perf|docs|test|refactor|build|ci|chore|style|revert)(\([a-z0-9-]+\))?!?:\ .+ ]] \
      || (( ${#subject} > 100 )) || [[ "$subject" =~ \#[0-9]+ ]]; then
      echo "refused: $subject"
      bad=1
    fi
  done < <(git log --format=%s "$base"..HEAD)
  return $bad
}

step build dotnet build AdCodicem.Pdf.slnx -c Release
step format dotnet format AdCodicem.Pdf.slnx --verify-no-changes --no-restore
step unit dotnet test --project tests/AdCodicem.Pdf.Tests/AdCodicem.Pdf.Tests.csproj -c Release --no-build
step commits check_subjects
echo "batch check passed at $(git rev-parse --short HEAD)"
