# Batch 19 — adversarial review brief

Repository /home/user/AdCodicem.Pdf, branch ccr-6d6c0ee6-dqm4lm-188. Batch 19 of M02's step 4 started from main at
21a0298 and pays #188, #197, #156, #207, #208, #210, #221, #222, #241, #261, #264, #281 and #317 (read the issues
and their comments through the GitHub tools: ToolSearch "select:mcp__github__issue_read", owner AdCodicem, repo
AdCodicem.Pdf). `git log 21a0298..origin/ccr-6d6c0ee6-dqm4lm-188` gives the commits and their messages.

Read first, in full:
- /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/decisions.md — every
  maintainer decision (B19-1 to B19-8, Q9 to Q12, R7-Q1 to R7-Q3, R8-Q1 to R8-Q3), the points decided without a
  question, the debts filed (#320 to #332) and the comments made (#171, #260, #292, #320, #330, #52).
- /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/residuals-impl.md —
  the residuals the implementation stages reported and left unfiled. For each one your lens covers, judge it: a
  defect to fix in this batch, a debt to file, or acceptable as is (say why). Do not report a residual already filed
  as #320 to #332 or as an older open debt (#289, #295, #52 among them) unless the code or the docs misstate it.

What to look for: defects with a reproducer (a hand-built file, its exact output on the branch, and on main 21a0298
where it differs) — wrong behavior, a report that is wrong, missing, doubled or depends on read order, a crash, an
unbounded cost or allocation on hostile input (invariant 4), an anomaly not reported (invariant 5), a report that
depends on what was read before (invariant 6), a guard's cut blamed on the file or a valid file refused (invariant
12, ADR 34), a severity against ADR 45, a test that does not hold what its name says or would pass without the fix,
a test that depends on load or machine, a member not covered, documentation (XML docs, remarks, docs/, the site)
that misstates the code, a measurement key or review key written in src/ tests/ docs/ (S1..S10 as shape names,
B19-, P1b, P2, b2t, c9b, A-prime, N-DUP, N-TND, R1-...), British spelling, a commit message rule broken. Also judge
whether each decision was implemented as decided.

Rules:
- Work in your own git worktree (the Workflow gave you one). `git fetch origin ccr-6d6c0ee6-dqm4lm-188` then
  `git checkout --detach origin/ccr-6d6c0ee6-dqm4lm-188`. Do not modify /home/user/AdCodicem.Pdf itself; do not
  commit or push; do not comment on GitHub.
- Build Release (`dotnet build AdCodicem.Pdf.slnx -c Release -m:2`; other agents build beside you). Probes are
  uncommitted test files Zz*.cs in tests/AdCodicem.Pdf.Tests, run with `dotnet test --project
  tests/AdCodicem.Pdf.Tests/AdCodicem.Pdf.Tests.csproj -c Release --no-build -- --filter-method "*ZzName*"`. For
  main's output, `git worktree add <scratch dir> 21a0298` and build there; remove it when done.
- Never run pkill or pgrep -f with a pattern that appears in the same command line; kill by PID.
- Severity: blocker (crash, hang, data loss, a valid file refused), major (a wrong or missing report, a wrong object
  served, an invariant broken, a decision not implemented), minor (a misleading text, a test that does not hold its
  claim, an uncovered member, a cost that is bounded but avoidable), nit (wording, style).
- Return findings as structured output only (no report files). Every finding: a short key, its severity, file:line
  on the branch head, the claim, the evidence (the reproducer's exact output), and the fix you propose.
- RESOURCE RULE (the container restarted twice when several dotnet processes ran at once and memory ran out; up to
  four agents work beside you). Run at most ONE dotnet build, test or run process at a time, never two in parallel
  (no `&`, no parallel shells, no background dotnet). Start every test or probe run with `setsid timeout -k 15 900`
  so a hang ends, and if one still hangs kill its whole process group by its PGID. For probe and mutant runs (not
  for the timing, budget, allocation or memory tests) export `DOTNET_GCHeapHardLimit=0xC0000000` so a runaway test
  host fails alone instead of taking the container down. Never run `dotnet build-server shutdown` and never kill a
  process you did not start: the other agents' builds share the MSBuild nodes and the compiler server.
