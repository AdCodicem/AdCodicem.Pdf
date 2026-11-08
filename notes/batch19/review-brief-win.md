# Batch 19 — adversarial review brief (Windows)

Repository C:/Users/clemm/repos/AdCodicem.Pdf (the maintainer's checkout: never edit, build or run anything there),
branch ccr-6d6c0ee6-dqm4lm-188. Batch 19 of M02's step 4 started from main at 21a0298 and pays #188, #197, #156, #207,
#208, #210, #221, #222, #241, #261, #264, #281 and #317. Read the issues and their comments through the gh CLI, always
with `-R AdCodicem/AdCodicem.Pdf`: `gh issue view N --comments`, `gh issue view N --json body,comments`.
`git log 21a0298..origin/ccr-6d6c0ee6-dqm4lm-188` gives the commits and their messages.

Let W = C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud.
It mirrors the cloud scratchpad /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad,
same layout: read any cloud path a note, a finding or a script names under W. The cloud's /home/user/AdCodicem.Pdf is
the maintainer's checkout above.

Read first, in full:
- W/lot19/decisions.md — every maintainer decision (B19-1 to B19-8, Q9 to Q12, R1-Q1 to R9-Q2), the points decided
  without a question, the debts filed (#320 to #333), the comments made, and each round's triage and fix result.
- W/lot19/residuals-impl.md — the residuals the implementation stages reported and left unfiled. For each one your
  lens covers, judge it: a defect to fix in this batch, a debt to file, or acceptable as is (say why). Do not report a
  residual already filed as #320 to #333 or as an older open debt (#289, #295, #52 among them) unless the code or the
  docs misstate it.

What to look for: defects with a reproducer (a hand-built file, its exact output on the branch, and on main 21a0298
where it differs) — wrong behavior, a report that is wrong, missing, doubled or depends on read order, a crash, an
unbounded cost or allocation on hostile input (invariant 4), an anomaly not reported (invariant 5), a report that
depends on what was read before (invariant 6), a guard's cut blamed on the file or a valid file refused (invariant 12,
ADR 34), a severity against ADR 45, a test that does not hold what its name says or would pass without the fix, a test
that depends on load or machine, a member not covered, documentation (XML docs, remarks, docs/, the site) that
misstates the code, a measurement key or review key written in src/ tests/ docs/ (S1..S10 as shape names, B19-, P1b,
P2, b2t, c9b, A-prime, N-DUP, N-TND, R1-..., bracketed finding numbers, mutant ids, probe or generator names, 'decoy',
'reviewer', 'refuter', 'prototype', 'variant', 'judge'), British spelling, a commit message rule broken. Also judge
whether each decision was implemented as decided.

Environment: Windows 11, 16 cores, 32 GB. Your shell tool is Git Bash (POSIX); PowerShell is also available. `python3`
is a Store stub: use `python` (`python -I` for scripts reading generated files). Run `export DOTNET_CLI_UI_LANGUAGE=en-US`
in each shell so build output reads 'Warning(s)'. Git Bash has no setsid. If the harness refuses `bash <script>` or a
`timeout -k` construct, use the Python drivers in W/lot19/tools: corpus_run.py (OUTDIR MAINTREE BRANCHTREE [RUNS]),
diff_run.py (WORKTREE NAME [--no-build]: the own-stream differential, W/r8proto), common.py, tdo.ps1, timing.ps1; read
each one's head first. A Release build of da2b06f (round 8's head) is at W/base-da2b06f: copy a probe into its
tests/AdCodicem.Pdf.Tests, build and run there, delete the probe after, never commit there (one agent at a time may
build it: if its obj folder is locked, add your own worktree at da2b06f instead).

Rules:
- Work in your own git worktree (the Workflow gave you one). `git fetch origin ccr-6d6c0ee6-dqm4lm-188` then
  `git checkout --detach origin/ccr-6d6c0ee6-dqm4lm-188`. Do not commit or push; do not comment on GitHub; do not
  modify the maintainer's checkout.
- Remote corpus: give a worktree the 242 remote documents as HARD LINKS only: `cp -al
  C:/Users/clemm/repos/AdCodicem.Pdf/tests/corpus/remote tests/corpus/remote`. NEVER a junction, symlink or mklink:
  `git worktree remove` follows a junction and deletes the maintainer's files. `rm -rf tests/corpus/remote` before you
  finish and before removing any worktree you added.
- Build Release (`dotnet build AdCodicem.Pdf.slnx -c Release -m:4`). Probes are uncommitted test files Zz*.cs in
  tests/AdCodicem.Pdf.Tests, run with `dotnet test --project tests/AdCodicem.Pdf.Tests/AdCodicem.Pdf.Tests.csproj -c
  Release --no-build -- --filter-method "*ZzName*"`. For main's output, `git worktree add <dir under W/r10wip> 21a0298`
  and build there; remove it when done (its remote corpus copy first).
- RESOURCE RULE (the maintainer's, on this PC): dotnet builds and tests may run in parallel, across agents and within
  yours (mutants in separate worktrees, a probe on main beside one on the branch), up to two dotnet processes of yours
  at once; never two builds or test runs in the same worktree at once. The exception: a benchmark run
  (BenchmarkDotNet) runs alone. Timing, budget, allocation and memory tests run under whatever load the other agents
  make, on purpose: a failure there is a finding only if it reproduces, and you say under what load. Wrap each long
  dotnet command in `timeout 900 <command>` (longer for the whole suite or the corpus). To stop one, find its PID and
  run `taskkill //PID <pid> //T //F`; never kill by name or pattern, never a process you did not start, never
  `dotnet build-server shutdown`. For probe and mutant runs (not the timing, budget, allocation or memory tests) set
  DOTNET_GCHeapHardLimit=0xC0000000.
- Severity: blocker (crash, hang, data loss, a valid file refused), major (a wrong or missing report, a wrong object
  served, an invariant broken, a decision not implemented), minor (a misleading text, a test that does not hold its
  claim, an uncovered member, a cost that is bounded but avoidable), nit (wording, style).
- Return findings as structured output only (no report files). Every finding: a short key, its severity, file:line on
  the branch head, the claim, the evidence (the reproducer's exact output), and the fix you propose. Keep your probes
  and generators under W/r10wip/<your label>/ so the fix stage can reuse them, and name their paths in the evidence.
