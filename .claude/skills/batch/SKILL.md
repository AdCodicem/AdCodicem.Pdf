---
name: batch
description: Run or resume one batch of M02's step 4 — one pull request paying a few debt issues — under ADR 54 (severity by effect and real-file input, review rounds stopped at the first with no blocker or major, eight at most) and ADR 53 as amended (mutation of defenses only, once). Use when the maintainer asks to start, resume or plan a batch.
---

# A batch of M02's step 4

A batch is one pull request that pays a few `debt` issues. `docs/status.md`, *Next concrete step*, names the next one.
The frame is `CLAUDE.md`; this is the procedure. The rules it applies are ADR 54 and ADR 53 (amended 2026-11-11): read
both before the first batch you run. Speak French to the maintainer; write code, documents, commits and issues in
American English. Questions go through `AskUserQuestion`, the recommended option first.

A batch aims at three days at most. A phase that runs well past what is written below is a reason to stop and ask.

## 1. Open

- Read each issue with its comments. Count: at most six issues and about 1,000 changed lines of code in `src/`
  (ADR 54). A batch planned above that is split by code area: propose the split to the maintainer before starting.
- Branch from `origin/main`. Keep working notes outside the repository — the scratchpad, or a `-wip` branch that is
  never merged: decisions, triage, the review keys (finding numbers, mutant ids, probe names). **No key goes into
  `src/`, `tests/` or `docs/`**; name behaviors in plain words there.

## 2. Measure, then ask

- Reproduce each issue's shapes on `main`, twice; say whether the issue's text still holds. Prototype the fix and any
  better alternative; run the unit suite and, where what the reader or validator reports changes, the corpus
  differential (a dump of every corpus document's diagnostics on `main` and on the prototype, compared).
- What the measurement settles is decided without a question, with its evidence. A real design choice goes to the
  maintainer: options, what each implies (behavior, codes, severities under ADR 45, cost, tests, docs), the one
  recommended.

## 3. Implement

- One stage per issue or code area, each in its own worktree. Stages that touch different files run in parallel.
- Each stage brings its tests and its texts. A behavior is written once, in the XML documentation of the diagnostic
  code, rule or member; the user documentation says what a caller can rely on; the threat model gets one entry per
  new defense — what it stops, where it lives, its bounds, the test class that holds it. A code comment says why.
- Tests: a theory row per behavior. A hand-made shape is a row, not a test with a paragraph of prose. Assert what a
  caller observes — objects, diagnostics, limits — and an internal position only where it is the contract.

## 4. Review rounds (ADR 54)

- **Round 1**: one reviewer per lens over the whole patch, one lens on real damage — the corpus, and truncation,
  shifted offsets, corrupt filter data, stale links after an incremental update, applied to what the patch reads.
- **Brief** (short): the batch's specification and accepted decisions, the diff under review, the consequences
  already accepted, the issues that already hold known gaps, and ADR 54's scale. Not the earlier rounds' findings
  and not the notes.
- Each finding carries its effect (E1 to E5) and its input (real-file or hand-made); the severity follows from them.
  **A refuter** checks each blocker and major only.
- **Fixes**: code stages first, parallel where files allow, then one documentation stage. The check is
  `bash .claude/scripts/batch-check.sh` (build, format, unit suite, commit subjects), not an agent. Issue comments and
  new debt issues are collected in the notes and posted once, at the batch's end.
- **Each later round** reviews the previous round's fixes and the code they call or that calls them.
- **Stop** at the first round with no blocker and no major. Its minors and nits, and any left, get one final pass that
  is not reviewed again: texts and tests are fixed; a minor whose fix would change behavior is filed as `debt`.
- **Round 8 is the last.** A blocker or major still standing goes to the maintainer: fix, file, or one more round.

## 5. Mutation (ADR 53, amended 2026-11-11)

- Once, after the loop stops. List the defenses among the changed lines — bounds before an allocation, a read or a
  copy; guards and their reports; depth and count limits; visited sets; exits of loops and recursions that read the
  file — and record the lines taken and the lines left, with why.
- Run the catalog over them in parallel worktrees (on the maintainer's PC, about 8 workers). Settle each survivor: a
  test; an equivalent, with its reason, when no document and no public call tells it apart, checked by a second agent;
  a widening in an oracle; or a debt.

## 6. Verify

Build with no warning; format; the whole unit suite; the patch's coverage as Codecov counts it; the integration suite
(Docker); the corpus differential against `main` if what the reader or validator reports changed; the `Remote corpus`
workflow on the branch if a remote document could move; benchmarks against `main`, alone, if a hot path changed; the
timing, budget, allocation and memory tests, alone; the site (`cd docs/website && npm ci && npm run build`).

## 7. Close

- `docs/status.md`: the actual state. `docs/journal.md`: an entry of about thirty lines — what was paid, the counts
  (rounds, findings by severity, mutants), what was filed, what changed for callers; the detail goes in the pull
  request's body.
- Post the collected issue comments; file the debts under the milestones that will pay them.
- Open the pull request: a `fix!:` or `fix:` title, its body naming one `Closes #n` per line. Never merge it.

## Resources

On the maintainer's PC (16 cores, 32 GB) dotnet builds and tests run in parallel, never two in the same worktree.
Benchmarks and the timing, budget, allocation and memory tests run alone. Kill a stuck process by PID, never with a
pattern that appears on its own command line.
