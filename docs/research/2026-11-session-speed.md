# Where a batch's time goes, November 2026

Batch 19 of M02's step 4 ([#344](https://github.com/AdCodicem/AdCodicem.Pdf/pull/344), merged 2026-11-11) paid 13
debt issues in 219 commits over eight days of calendar: 13 review rounds, 13 fix workflows, then a mutation step
over the whole patch. Before the merge, the maintainer asked whether that time is justified, whether the project writes
too many unit tests, whether mutation testing ([ADR 53](../adr/0053-mutation-testing-runs-a-fixed-catalog-over-the-changed-code.md))
is worth it, and what takes so much time. This page answers from measurements. The decisions it led to are
[ADR 54](../adr/0054-review-and-documentation-effort-follow-what-protects-the-library.md) and the amendment of ADR 53
of 2026-11-11.

## Sources

- The transcripts of the 255 workflow agents batch 19 ran on the maintainer's machine (review rounds 10 to 13, the
  fixes of rounds 9 to 13, the mutation step's settling and the end of the batch): wall clock, agent time, tokens
  and tool time, by agent. Rounds 1 to 9 ran in a cloud container whose transcripts were not kept; their counts come
  from the batch's record.
- Every finding of the 13 rounds that its refuter did not refute, 314 in all, classed one by one by what it would
  have done to a caller (below) and by whether its input occurs in real files.
- The mutation step's ledger: one line per surviving mutant, with its fate and the test that killed it.
- The journal of batches 5 to 19 ([`journal.md`](../journal.md)), for the trend.

## Where the time goes

Measured over the part of batch 19 that ran on the maintainer's machine:

| Phase | Wall clock | Agent time | Output tokens |
|---|---|---|---|
| 4 review rounds (10 to 13), 28 to 32 agents each | 5.1 h | 30.6 h | 13.4 M |
| 9 fix workflows (rounds 9 to 13, and the final pass) | 22.9 h | 23.8 h | 10.2 M |
| Mutation: the run itself (3,736 mutants, 8 workers, no agent) | 7.5 h | — | — |
| Mutation: settling 319 survivors (5 groups, refuter included) | 9.8 h | 21.6 h | 8.1 M |
| End: status, comments, verification, pull request | 4.6 h | 8.2 h | 4.0 M |

- **A round costs about 1.3 h of review and 4 to 5 h of fixes.** The fixes are sequential stages, each in its own
  worktree on the same 11,000-line `PdfFileReader.cs`, followed by a documentation stage, a tracking stage (issue
  comments) and a check stage, each an agent of 0.4 to 0.5 h.
- **The model, not the tools, takes the time.** Tools account for 29 % of agent time in reviews, 37 % in fixes and
  42 % in settling; the rest is the model reading and writing. The median model call carries about 232,000 tokens of
  context (the orchestrating session's: 197,000, and 458,000 at the 90th percentile). A reviewer's brief sends it to
  the batch's whole record, every earlier round's findings and the fix reports; `status.md` and `threat-model.md`
  weigh 366 KB each.
- **What the agents read**, by volume of text returned to them: the batch's own working notes 28 %, other working
  files (reports, scripts, probes) 26 %, `PdfFileReader.cs` 20 %, tests 10 %, the threat model 8 % (read by 131 of
  the 255 agents), the rest of `src/` 3 %, `status.md` 2 %, the ADRs 1.4 % (50 reads, most of them ADR 53 and ADR 34).
- **Builds and tests are not the bottleneck.** The whole unit suite, 11,155 tests, runs in 4 minutes; builds and test
  runs were about 3 of the 42 agent-hours of rounds 9 to 12.

## What the review rounds found

Each finding is classed by its **effect**: a crash, a hang or an unbounded use of memory or time (E1); wrong objects
served for a file valid under ISO 32000 (E2); wrong objects served for a damaged file (E3); a diagnostic missing or
wrong while the objects are right (E4); nothing a caller can observe — a message's wording, a comment, a document, a
test (E5). And by its **input**: a shape that occurs in real files — the corpus, a known producer, ordinary damage
such as truncation, shifted offsets, corrupt filter data or a stale link after an incremental update — or a shape
only a hand-made file has: signed object numbers, comments between a header's tokens, a guard reached at an exact
byte, several faults stacked.

| Round | Kept | Blocker / major / minor / nit | E1 | E2 | E3 | E4 | E5 | Behavior on a real-file shape | Regressions of the batch's own fixes |
|---|---|---|---|---|---|---|---|---|---|
| 1 | 47 | 0 / 10 / 27 / 10 | 0 | 0 | 3 | 13 | 31 | 1 | 0 |
| 2 | 23 | 0 / 3 / 10 / 10 | 0 | 0 | 3 | 3 | 17 | 0 | 16 |
| 3 | 18 | 0 / 4 / 9 / 5 | 0 | 0 | 4 | 1 | 13 | 0 | 13 |
| 4 | 26 | 1 / 2 / 10 / 13 | 2 | 0 | 3 | 1 | 20 | 0 | 21 |
| 5 | 17 | 0 / 0 / 5 / 12 | 0 | 0 | 0 | 1 | 16 | 0 | 12 |
| 6 | 14 | 0 / 1 / 1 / 12 | 0 | 0 | 1 | 0 | 13 | 0 | 9 |
| 7 | 21 | 0 / 2 / 4 / 15 | 0 | 0 | 3 | 0 | 18 | 2 | 16 |
| 8 | 29 | 0 / 9 / 4 / 16 | 0 | 0 | 7 | 3 | 19 | 3 | 26 |
| 9 | 30 | 0 / 3 / 19 / 8 | 0 | 0 | 2 | 10 | 18 | 2 | 26 |
| 10 | 22 | 0 / 8 / 9 / 5 | 0 | 0 | 1 | 12 | 9 | 0 | 22 |
| 11 | 23 | 0 / 1 / 16 / 6 | 0 | 0 | 3 | 8 | 12 | 0 | 21 |
| 12 | 24 | 0 / 1 / 11 / 12 | 0 | 0 | 3 | 4 | 17 | 0 | 22 |
| 13 | 20 | 0 / 0 / 13 / 7 | 0 | 0 | 0 | 0 | 20 | 0 | 19 |
| **All** | **314** | **1 / 44 / 138 / 131** | **2** | **0** | **33** | **56** | **223** | **8** | **223** |

- **71 % of what the rounds found changes nothing a caller can observe** (E5): texts that disagree with each other or
  with the code, test comments, records. The same rule is written in the code's comments, its XML documentation, the
  diagnostics reference, a concepts page, the threat model, test comments and commit bodies; each change of behavior
  must be carried to all of them, and each round finds where it was not. 54 % of the lines batch 19 added to `src/`
  are comments.
- **Of the 91 findings that change behavior, 83 need a hand-made file.** Eight concern shapes that occur in real
  files, all of them damaged files (E3, E4). None concerns a valid file (E2).
- **70 % of the behavior findings (64 of 91) are regressions the batch's own fixes caused**, and so are 31 of the 45
  blockers and majors. Fixing a hand-made shape adds a branch to the most entangled function of the reader; the next
  round finds the hand-made shapes that branch mishandles. That is why majors came back after round 5, which had
  none, and why the loop did not converge.
- **The two E1 findings** — a caller's source that returns short reads, which its contract allows, made `Open` throw
  or loop — were themselves regressions of round 3's fixes, found by round 4.
- **Had the review stopped at round 5**, its first round with no blocker and no major, 12 behavior defects found later
  would have shipped. Six were already on `main` before the batch. Of the six the batch caused, two concern real-file
  shapes: an undecodable cross-reference stream whose encoded bytes were read as rows, and a hybrid file whose
  `/Prev` lies a few bytes off. Both are damaged files; both were found in round 7. Rounds 9 to 13 alone, measured,
  cost 28 h of wall clock and 54 agent-hours; rounds 6 to 8 ran in the cloud, unmeasured.

The trend across batches says the same. Batches 5 to 13 ran one or two review rounds each, a day or two a batch. No
CI or corpus run after a merge has failed on a defect of the library: the four post-merge failures the journal records
are all tests. Later batches' reviews do find older defects — 12 of batch 19's behavior findings were already on
`main` — and 10 of those 12 need a hand-made file. From batch 14, rounds grew to 13, 5, 13, 32, 10 and 13, and
findings per batch from 25–35 to 191–328.

## What mutation testing found

Across batches 5 to 19, about 6,000 mutants of the library were run. **One survivor revealed a defect in the
library** (batch 8: a stream whose `/Length` the file cannot hold marked the parse as having met the window's edge).
Every other survivor was a gap in the tests, an equivalent mutant, or dead code.

Batch 19's step, over the whole patch at its end: 3,751 mutants for 1,746 changed lines of code, 3,736 run, 3,193
killed, 224 not viable, 319 surviving. The survivors ended as 112 tests written for them, 1 killed by a later test,
206 equivalent, 0 debt, and **no change to `src/`**. The refuter overturned 39 of the 245 first "equivalent"
verdicts; 33 of those 39 were told apart only by calling an internal member directly (a helper's positions, an
internal interface called with an argument the reader never passes, a record's fields), 6 through a document (the
order of two diagnostics, a message, the number of reads). Of the 112 killing tests, 78 assert positions or offsets.
The step cost 7.5 h of machine time and 21.6 agent-hours of settling, about 11 h of wall clock with the overlap, on
top of the mutants each fix stage ran over its own lines.

## The tests

The suite is not slow: 11,155 tests in 4 minutes. What costs is writing and keeping them. Batch 19 added 475 test
methods and 1,636 data rows: 14,745 lines of test code and 4,204 lines of test comments, for 2,373 lines of library
code. `threat-model.md` cites 1,258 test methods by name, 53 % of all of them, so a test renamed or repurposed is a
document to edit and a citation check to run. The tests that cost most are the ones that pin a hand-made shape or an
internal position, each with prose restating the rule.

## Answers

- **Is the time justified?** Partly. The protection that matters — no crash, no denial of service, valid files read
  right, real damage handled — came from the first rounds, plus two real-file defects in round 7. About three quarters
  of the time went to hand-made shapes, to texts agreeing with each other, and to fixing the loop's own regressions.
- **Too many unit tests?** Not too many to run. Too many pinned to hand-made shapes and internal positions, each with
  prose that must agree with six or seven other texts.
- **Is mutation testing worth it?** Not at its batch 19 scope: one library defect in about 6,000 mutants, none in
  batch 19's 3,736, at about 11 h a batch. It keeps a use where a missing test would let a later change remove a
  defense in silence — a bound, a guard, a depth limit — which is a small part of a patch.
- **What takes so much time?** The number of rounds, driven by findings on hand-made shapes and by the regressions
  their fixes cause; sequential fix stages with three fixed stages each; the model reading very large contexts; and
  the whole-patch mutation step.
