# Milestone review

How a milestone's adversarial review is run ([ADR 46](adr/0046-every-milestone-ends-with-an-adversarial-review.md)).
Every milestone ends with one. It is the milestone's last slice, run by a session that worked on none of it, and
its report is kept in [`docs/reviews/`](reviews/README.md).

**This document is the review session's reading list.** It replaces the one in `CLAUDE.md`. That file still
applies as the frame being checked, but its reading order does not. The review session starts with
`/milestone-review Mxx`, and that command sends it here.

## Why

Each session of a milestone decides with the context it has, and records its decisions where it works: the
specification, an ADR, the journal in `docs/status.md`, an issue, a commit message. No session reads everything
the others decided. CI, the referees, coverage and the review of each pull request all look at one change at a
time. So no one checks that the decisions, taken together, still agree. The review does. It is run by a session
that did not take those decisions, and every finding it makes has to survive an attempt to refute it.

## When, and who starts it

- **The review is the milestone's last slice.** It is an issue labeled `slice` and `review`, opened with the
  other slices. Its `Blocked by:` line names every other issue filed under the milestone, and it gains the
  number of each issue filed there later.
- **The maintainer starts it** by opening a new session and typing `/milestone-review Mxx`. Two conditions
  apply: the review is the only open issue left under the milestone, and `docs/status.md`'s *Next concrete
  step* says so.
- **The session must not have worked on the milestone.** It must not be a session that continues one that
  did, and must not carry over a summary of one.
- **XL milestones** are split into sub-milestones, and each sub-milestone gets its own review. The milestone's
  final review then covers only the seams between the sub-milestones — the decisions one of them took that
  another depends on — and whatever the sub-milestone reviews left to it. A milestone of size L or smaller
  has a single review.
- **A milestone that closed before ADR 46** is reviewed after the fact when an issue asks for it, as #136
  does for M01. The code reviewed is the code as it stands on `main`. A finding that a later change already
  fixed is recorded as fixed, not filed.

## Before starting

1. Check that every issue filed under the milestone is closed except the review's own. If one is still open,
   stop and tell the maintainer which one. A review of an unfinished milestone would review the wrong thing.
2. Work out the **milestone's extent**, which is the range of commits the review covers. Fetch the whole
   history first (`git fetch --unshallow origin main`), because a session's clone is shallow.
   - Its **pull requests** are those that closed an issue filed under the milestone's GitHub milestone (each
     issue's `closed_by_pull_requests`), together with those whose commits say `Refs #n` for such an issue.
   - It **starts** at the base of the earliest of those pull requests, and **ends** at the head of `main`.
     After the fact, it ends at the last pull request the milestone merged.
   - For M01, whose work predates tracking on GitHub, the extent runs from the repository's first commit to
     the commit that marked M01 *done* in `docs/roadmap.md` (`git log -S'| M01 |' -- docs/roadmap.md`).
   - A sub-milestone's extent is the pull requests of its own slices.

   At this stage, read the pull request titles and commit **subjects** only
   (`git log --format='%h %s' <start>..<end>`). Their bodies and threads are for the second pass.
3. List what the extent touched:
   - `git diff --stat <start>..<end> -- src/ tests/ docs/ .github/ Directory.*.props *.slnx`;
   - the public API it added or changed: the `public` and `protected` members added or changed in `src/`
     over the extent, until a public API baseline records them (#35).

## The two passes

### Pass 1 — blind

Read what the milestone **produced**, not yet what was said about it:

- `CLAUDE.md`, and the ADRs **that were accepted before the milestone started**. These are the frame.
- The milestone's specification, `docs/milestones/Mxx.md`, as it stands.
- The code and the tests in the extent, the corpus manifest entries the milestone added or changed, and the
  benchmarks.
- The published user documentation (`docs/website/docs`) and the project documents the milestone changed,
  `docs/threat-model.md` among them.
- The specifications of the milestones already closed, in the places the milestone touched their behavior.

Do not read yet: the journal in `docs/status.md`, the issues and their comments, the pull requests and their
review threads, commit bodies, or ADRs accepted or amended during the milestone.

Write every **candidate finding** to a file in the scratchpad before pass 2 begins. Pass 2 can drop a
candidate, but it cannot erase one: a dropped candidate stays in the report, with the reason it was dropped.

### Pass 2 — informed

Now read the justifications: the journal entries in the milestone's time span, every issue filed under the
milestone (closed ones too) and its comments, the pull requests that closed them and their review threads, the
commit bodies in the extent, and the ADRs accepted or amended during the milestone.

For each candidate from pass 1, decide:

- **Justified.** A decision written somewhere accounts for it. The candidate is dropped, with the citation. If
  that justification lives only in a journal entry, a pull request thread or a commit body, and not beside the
  code or in the specification, a finding remains: *the reason is not where the next reader will look*.
- **Stands.** No justification was found, or the one found does not hold.

Then look for what only the justifications can reveal. This is where most findings of the consistency axis
come from: two sessions that settled the same question differently; a decision the specification records
while the code does something else; a *Blocks:* or *Blocked by:* line that no longer matches what was built;
a debt closed while its *What closes it* was only partly done.

## The five axes

Each axis is a list of checks, run over the extent. An axis that finds nothing says so in the report, together
with what it examined, so that an absence of findings means something.

### 1. Consistency of decisions

- Within the milestone, sessions do not contradict each other. Look at naming (rule identifiers, diagnostic
  codes, options, types), at the same kind of question being settled by the same bar, and at each settled
  choice still holding in every slice that came after it.
- Against the milestones already closed: behavior the milestone inherited and changed is still described
  correctly by their specifications, the user documentation and `docs/architecture.md`.
- Against `CLAUDE.md` and the settled ADRs: nothing reverses a decision in silence. An ADR amended during the
  milestone says so in its *Status*, and whatever cited the old wording was updated.
- `docs/status.md`, the milestone's exit criteria and the issues tell the same story. A box is ticked only
  where a test proves it.

### 2. Code against specification

- Every acceptance condition of the specification exists as the test it names, and that test checks what the
  condition says, on the documents it names.
- Every behavior of the specification's *Design* section is either in the code, or explicitly deferred with
  an issue.
- Where the code rightly diverged from the specification, the specification was updated. Where the code
  diverged without reason, that is a finding.
- The *Traps* section: each trap is guarded by a test.

### 3. Architecture invariants

Invariants 1 to 12 of `CLAUDE.md`, over every file the extent changed in `src/`. The ones that slip most often
between sessions:

- **2 and 3**: nothing loads a whole document; no allocation, LINQ, closure or boxing on a hot path.
- **4 and 12**: every bound is classified, when it is added, as a guard (a `PdfReaderLimits` property plus a
  `limit.*` code) or as an internal constant with its reason written where it is declared (ADR 34). Every loop
  whose exit depends on the file terminates.
- **5**: anomalies are recorded as diagnostics, not as exceptions or log lines.
- **6**: nothing depends on the clock, on a random source, on culture, or on the iteration order of a hash set.
- **8**: public types are immutable and there is no mutable static state.
- **9 to 11**: every feature has its tests, every optimization has its benchmark, the corpus is used, and both
  test levels plus the documentation are present.

### 4. Public API and documentation

- The public API the milestone added or changed follows `CLAUDE.md`'s conventions: `sealed` by default,
  immutable options, naming consistent with what already exists, the `Pdf` prefix where it belongs, and no
  type public that could be internal.
- Every public member has XML documentation that says what it does, not just what it is named.
- `docs/website/docs` documents what now exists and nothing that does not. `docs/features/features.json`
  records the milestone's features in their real state.
- Rule identifiers, diagnostic codes and limit codes are listed where the documentation says they are, for
  example `docs/website/docs/reference/validation-rules.md`.

### 5. Security

- **Hostile input and denial of service.** Take every new path that reads a byte from outside the caller's
  code. Allocation is bounded by a checked bound, recursion is bounded, and each loop ends. Decompression
  bounds its output. A fuzz target reaches the new parser or decoder, or the report says why none does.
- **Attack surface added.** Look at resources the library now fetches (ADR 38), XML it parses (external
  entities, entity expansion), file paths it builds, cryptography or signatures it handles, and anything it
  executes or interprets.
- **Supply chain.** For each dependency the milestone added, check its license, whether it is maintained and
  its known vulnerabilities, and that it is pinned. Workflows the milestone changed keep least-privilege
  `permissions:`, actions pinned by SHA, and no secret exposed to a pull request from a fork.
- **Threat model.** `docs/threat-model.md` covers every surface the milestone opened or widened, and each
  defense it names exists in the code, with the test or fuzz target it names.

The specifications of milestones still to come are **not** reviewed. Each one will be reviewed in its turn.

## Refutation

Every finding that stands after pass 2 is handed to a **refuter**: a sub-agent started with the Agent tool.
It receives the finding's statement and evidence, with the critic's own reasoning left out, and has
read-only access to the repository and the issues. Its task is to show that the finding is wrong, already
handled, or justified somewhere the critic missed, and it must cite what it relies on. It returns one of:

- **Refuted**, with the citation;
- **Stands**;
- **Narrowed**, when part of the finding holds, with what remains.

Run the refuters in parallel, in batches of related findings. A finding and its refutation go into the report
together, whatever the verdict. A refuter that accepts every finding, or rejects every finding, is itself
something to report: the counts in the report's summary make it visible.

## Triage

Every finding that survives gets exactly one outcome:

| The finding is… | Outcome |
|---|---|
| An inconsistency (between decisions, or between the specification and the code), or a broken invariant, including a security defect — **and trivial** | Fixed in the review's own pull request |
| The same, **not trivial** | An issue labeled `debt` and `review`, filed **under the milestone**, added to the review issue's `Blocked by:` line. The milestone does not close before it does |
| A gap that is neither: an improvement, or a piece a later milestone plans | An issue labeled `debt` and `review`, filed under the milestone that will pay it |
| A challenge to a settled choice (an ADR, or a decision the maintainer took) | Put to the maintainer, **one question at a time**, with the evidence. The answer is recorded: the ADR amended by a later change, or the finding dropped with the maintainer's reason |

**Trivial** means that nothing a caller or a file can observe changes: documentation, a comment, the
name of something internal, a specification brought in line with code that is right, a stale link. A public
name is not trivial, even in a preview.

**The maintainer sees the triage before anything is filed or fixed.** Show the findings that survived,
grouped by outcome, and let the maintainer move any of them.

For a review done after the fact (M01, under M02), the milestone that can no longer be reopened is replaced
by the one the review is filed under.

## What the review produces

- **The report**, `docs/reviews/Mxx.md`, in the form below, linked from [`docs/reviews/README.md`](reviews/README.md).
- **The trivial fixes**, as commits in the same pull request. Their types are `docs:`, `refactor:` or `test:`,
  never `fix:` or `feat:`, because a change a caller could observe is not trivial.
- **The issues**, each saying in its body *Found by the review of Mxx (`docs/reviews/Mxx.md`, R-07)*, and
  written like any other debt: what is wrong, the evidence, and *What closes it*.
- **`docs/status.md`**: one line under *At a glance* and a journal entry giving the counts, the issues filed,
  and what the milestone now waits on.
- **One pull request**, `docs: record the review of Mxx`, which says `Closes #n` for the review's issue.

The review's issue closes with that pull request. The milestone closes once the issues the review filed under
it have closed as well.

## The report

```markdown
# Review of Mxx — <title>

Date: YYYY-MM-DD. Extent: `<start>`..`<end>` (<n> commits, <n> pull requests).
Issue: #n. Session: <link>.

## Summary

| | Count |
|---|---|
| Candidates from pass 1 | n |
| Dropped in pass 2, justified | n |
| Raised in pass 2 | n |
| Refuted | n |
| Narrowed | n |
| Kept | n |
| — fixed here | n |
| — filed under Mxx | n |
| — filed under a later milestone | n |
| — put to the maintainer | n |

## What each axis examined

One paragraph per axis: what was read, what was checked, and what held.

## Findings

### R-01 — <one-line statement>

- **Axis**: consistency | specification | invariant n | API and documentation | security
- **Evidence**: `path:line` at `<sha>`, the specification's section, the ADR, the issue.
- **Pass**: 1, kept in pass 2 | 1, dropped in pass 2 (justified by …) | 2
- **Refutation**: what the refuter argued and cited.
- **Verdict**: stands | narrowed to … | refuted
- **Outcome**: fixed in `<sha>` | #n under Mxx | #n under Myy | the maintainer decided … on YYYY-MM-DD

## Dropped candidates

The candidates pass 2 justified, one line each with the citation that justified them.
```
