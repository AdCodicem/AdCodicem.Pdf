# 54. Review and documentation effort follow what protects the library

Date: 2026-11-11

## Status

Accepted on 2026-11-11, on the maintainer's decisions of that day, put to them in two sets of questions after the
measurements of [Where a batch's time goes](../research/2026-11-session-speed.md). It makes permanent the stopping rule
the maintainer set for batch 19 from its thirteenth round, with a cap of eight rounds instead of fourteen. It amends
[53](0053-mutation-testing-runs-a-fixed-catalog-over-the-changed-code.md) the same day. A milestone's review
([46](0046-every-milestone-ends-with-an-adversarial-review.md)) is unchanged; this record governs the review rounds
of each pull request.

## Context

Since batch 5 of M02's step 4, each pull request has been reviewed in rounds: reviewers by lens, a refuter per
finding, a fix workflow, then the next round. Batches 5 to 13 ran one or two rounds each, a day or two a batch, and
no defect of the library has failed a run after their merge. From batch 14 the rule became "until a round finds no
minor", and the rounds grew to 13, 5, 13, 32, 10 and 13. Batch 19 took eight days.

The 314 findings of batch 19 that their refuters kept, classed one by one:

- 71 % changed nothing a caller can observe: texts disagreeing with each other or with the code. The same rule was
  written in code comments, XML documentation, the diagnostics reference, a concepts page, the threat model, test
  comments and commit bodies; 54 % of the lines the batch added to `src/` were comments.
- Of the 91 that changed behavior, 83 needed a hand-made file — signed object numbers, comments between a header's
  tokens, a guard reached at an exact byte, faults stacked. None concerned a valid file.
- 64 of those 91 were regressions of the batch's own fixes: a fix for a hand-made shape added a branch the next round
  found hand-made shapes for. Round 5 had no blocker and no major; majors came back in round 6 and the loop did not
  converge.
- Had the loop stopped at round 5, six defects the batch caused would have shipped, two of them on shapes real files
  have — both damaged files, both found in round 7.

A round cost about 1.3 h of review with 28 to 32 agents, and 4 to 5 h of sequential fixes. The model, not the tools,
took 63 to 71 % of agent time, with a median context of 232,000 tokens: briefs sent every reviewer to the whole record
of the batch and every earlier round. `status.md` and `threat-model.md` weighed 366 KB each; the threat model cited
1,258 test methods by name.

The maintainer's frame: everything is a trade-off, and a rule that protects only against very exotic cases can be
revised.

## Decision

**A finding's weight is set by what it would do to a caller and by whether real files have its input. The review
loop stops at its first round with no blocker and no major, at the eighth at the latest. A rule of behavior is
written once, beside the code.**

### The scale

A finding is classed by its **effect**:

- E1: a crash, a hang, an unbounded use of memory, time or the stack (invariant 4);
- E2: wrong objects served for a file valid under ISO 32000 (invariant 12);
- E3: wrong objects served for a damaged file;
- E4: a diagnostic missing or wrong while the objects served are right;
- E5: nothing a caller can observe — a message's wording, a comment, a document, a test, a record.

And by its **input**. A **real-file shape** is one the corpus has, a known producer writes, or ordinary damage makes:
a truncation at any point, offsets shifted by a few bytes, corrupt filter data, a stale `/Prev` or `/XRefStm` after
an incremental update, a writer's known quirk. A **hand-made shape** is any other: what only a file written to
exercise the reader has, and several faults stacked unless a real file shows them together.

| Severity | Findings |
|---|---|
| Blocker | E1 on any input, a caller's source that honors its contract included; E2 |
| Major | E3 on a real-file shape; a regression against `main` on a real-file shape |
| Minor | E3 on a hand-made shape; E4 on a real-file shape |
| Nit | E4 on a hand-made shape; E5 |

Invariant 4 holds for every input: a hand-made file that crashes, hangs or exhausts a resource is a blocker. What
drops to best effort, on hand-made shapes only, is the precision of the objects served and of the diagnostics.

### The loop

- **Round 1** runs every lens over the whole patch, one of them on real damage: the corpus, and the damage listed
  above applied to the shapes the patch reads.
- **Each later round** reviews the previous round's fixes and the code they call or that calls them, not the whole
  patch again.
- **A refuter** checks each blocker and major. Minors and nits go to triage as stated; the agent that fixes one checks
  it first.
- **Briefs are short**: the batch's specification, the diff under review, the consequences already accepted, and the
  issues that already hold known gaps. Not the earlier rounds' findings, not the batch's record.
- **The loop stops** at the first round whose kept findings hold no blocker and no major. Its minors and nits, and any
  left from earlier rounds, get one final pass that is not reviewed again: texts and tests are fixed there; a minor
  whose fix would change behavior is filed as `debt` under the milestone that will pay it, so no behavior changes
  unreviewed.
- **The eighth round is the last.** A blocker or major still standing then goes to the maintainer: fix it, file it,
  or run one more round.

### The batch

A batch pays at most six issues and changes at most about 1,000 lines of code in `src/`, comments not counted. A
planned batch above that is split by code area when it opens; the order of the batches is kept.

### The texts

- A behavior is written once: in the XML documentation of the diagnostic code, the rule or the member that has it.
- The user documentation says what a caller can rely on and links the reference; it does not enumerate hand-made
  shapes.
- The threat model says, for each defense, what it stops, where it lives (type and member), how its bounds are
  classified, which test class holds it, and its known gaps. It cites no test method and enumerates no shape.
- A pull request's journal entry, in [`journal.md`](../journal.md), is short — about thirty lines; the detail is in
  the pull request's body.
- A code comment says why, not which shapes take which branch.

The procedure a batch follows is the `batch` skill (`.claude/skills/batch/SKILL.md`).

## Consequences

- Batch 19 under these rules would have stopped at round 5 at the latest, saving about eight rounds; the two
  real-file defects round 7 found are what the real-damage lens of round 1 is for.
- Fixes for hand-made shapes stop restarting the loop, and so do the regressions those fixes cause, which are
  themselves hand-made shapes. Majors come from real files.
- A hand-made shape may now ship with objects served or a diagnostic less precise than it could be; a crash or a hang
  on it stays a blocker. A real file that shows such a shape turns it into a major and a debt.
- Fewer texts restate a rule, so fewer findings are texts disagreeing; the reviewers' contexts shrink with the briefs
  and the documents.
- More batches: 13 remain in M02's step 4 and some will split. Each pull request carries about 5 h of fixed
  verification (integration suite, benchmarks, corpus).
- A refuter no longer checks minors and nits. A wrong minor costs a final-pass edit, not a round.
- The cap is a judgment, not a proof. Eight rounds is wider than any batch needed before batch 14, and narrower than
  batches 17 and 19 took.

### Alternatives rejected

- **The rule of batches 17 and 18, until a round finds no minor.** Minors stayed between 5 and 19 a round in batch 19;
  the loop never met it.
- **No cap.** Batch 17 ran 32 rounds; a cap puts the choice back in the maintainer's hands.
- **A cap of five rounds.** Narrower; the maintainer preferred room for larger batches.
- **Hand-made findings filed as debt, never fixed in the batch.** Faster still, but it leaves defects the batch caused
  open even where their fix is cheap and reviewed.
- **Severity left to each reviewer's judgment.** That is what let hand-made shapes drive the loop.
- **Every lens over the whole patch each round, refuters for all.** Batch 19's form: about 30 agents a round, most of
  them refuting minors and nits.
- **Batches of at most four issues.** Smaller, but the fixed cost of each pull request comes back more often.
- **The texts rule for new texts only.** The documents that weigh most would keep weighing; they were compacted with
  this record.

## What would reopen it

A defect on a hand-made shape that a real file then shows, reaching a caller; a blocker or a major found after a merge
that a round stopped by this rule would have found; or batches that still take more than three days.
