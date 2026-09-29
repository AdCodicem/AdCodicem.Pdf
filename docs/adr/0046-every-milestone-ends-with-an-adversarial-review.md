# 46. Every milestone ends with an adversarial review by a fresh session

Date: 2026-09-29

## Status

Accepted on 2026-09-29, by the maintainer, during M02, each choice below put to them and settled. It adds a
seventh point to the definition of done in `docs/roadmap.md`. The procedure is `docs/milestone-review.md`; the
reports are in `docs/reviews/`. M02 is the first milestone to carry the review as a slice (#137); M01, closed
before, is reviewed after the fact under M02 (#136).

## Context

A milestone is not a session: M01 took a handful, M02 is past six, and an XL milestone is a program of work.
Each session reads `CLAUDE.md`, `docs/status.md` and the milestone's specification, works on a slice, and
records what it decided — in the specification, an ADR, the journal, an issue, a commit message. No session
reads the whole of what the others decided. Nothing checks that the decisions still agree once they are
all made.

The project already has checks, and each one covers a single change:

- CI, coverage and the referees check the code a pull request brings;
- `Claude Code Review` and the code owner's approval review one pull request's diff;
- the definition of done checks tests, documentation and measurements, not consistency;
- a second agent was used once, on 2026-09-29, to refute what a first called unreachable during the coverage
  pass (`docs/status.md`). One claim fell.

M02's review of 2026-09-29 was done by the maintainer and the session that had written slice 3, one question
at a time. It confirmed four Arlington overrides and dropped two. It also showed the limit of such a review:
the session judging the decisions was the session that had made them.

Problems that span sessions have no check. A rule gets named two ways in two slices. A severity is decided on
one bar and then applied on another. The specification says one thing while the code and its tests say
another. A bound is added without being classified (invariant 12). Sometimes a later slice undoes, in
silence, what an earlier one settled. None of these fails a build, and none shows in any single diff.

## Decision

**Every milestone ends with an adversarial review, run by a session that worked on none of it, before the
milestone closes.**

- **When.** The review is the milestone's last slice: an issue labeled `slice` and `review`, opened with the
  other slices. Its `Blocked by:` line names every other issue filed under the milestone. An XL milestone,
  which is split into sub-milestones, has one review per sub-milestone, and then a lighter final review of the
  seams between them. A milestone of size L or smaller has one review.
- **Who.** The maintainer starts the session by hand, with `/milestone-review Mxx`, once the review is the
  only open issue. The session has not worked on the milestone. Two agents work in it. One, the critic, states
  findings. The other, the refuter, tries to refute each one with a citation. Only what survives is shown to
  the maintainer.
- **How.** The review is read in two passes. The first is blind: the code, the tests, the specification and
  the published documentation, with `CLAUDE.md` and the ADRs the milestone started from. The second is
  informed: the journal, issues, pull requests, commit messages and the ADRs accepted or amended during the
  milestone. The first pass forms a view before the reasoning already written down can shape it.
- **What.** Five axes, each looked at over the milestone's extent:
  1. the **consistency of decisions**, between the milestone's own sessions and with the milestones already
     closed, `CLAUDE.md` and the settled ADRs;
  2. the **code against the specification**, and the specification brought up to date where the code
     rightly diverged;
  3. the **architecture invariants 1 to 12**;
  4. the **public API and its documentation**;
  5. **security**: hostile input and denial of service, the attack surface the milestone adds, the supply
     chain it changes, and the threat model, `docs/threat-model.md`, completed for every surface the
     milestone opens.

  The specifications of milestones still to come are not reviewed. Their own reviews will be.
- **What follows.** A finding that survives is triaged:
  - an inconsistency or a broken invariant is fixed before the milestone closes. The review session fixes it
    in its own pull request when the fix is trivial: documentation, naming, nothing a caller or a file can
    observe. Otherwise it files an issue under the milestone;
  - anything else is filed as `debt` under the milestone that will pay it;
  - a finding that would reopen a settled choice goes to the maintainer, one question at a time.

  The maintainer sees the triage before any issue is opened.
- **Where.** A report per review, `docs/reviews/Mxx.md`, published with the project documents. It lists every
  finding, its refutation, its verdict and its outcome, plus what each axis examined and found sound.

## Consequences

- A milestone closes one session later than its last slice, and waits on whatever its review files. This is
  the intended cost: a milestone with open inconsistencies was never done.
- The review session starts from nothing. It spends its first hours reading what the milestone's sessions
  already knew. That time buys the fresh view: it cannot inherit their blind spots through their context.
- The blind first pass can raise findings the informed pass then drops, because the reason was written down
  somewhere else. The report keeps them, with where the reason was found. A reason that lives only in a
  journal entry, and not beside the code it justifies, is itself worth a finding.
- The review fixes only trivial findings. That keeps it independent of what it judges, and keeps its pull
  request small enough to review. The cost is that a real fix waits for another session.
- A settled choice can be questioned again. The review brings the evidence to the maintainer and does not
  decide. The "do not relitigate" rule of `CLAUDE.md` still holds for every session that is not a review.
- The threat model becomes a project document that each milestone maintains. Its first version is owed
  under M02 (#135), before M01's review can check the reader against it.
- The procedure is only as adversarial as the refuter. A refuter that accepts every finding, or rejects every
  finding, shows in the report's counts, and the maintainer reads them.

### Rejected alternatives

- **The review in the closing session**, with sub-agents playing critic and refuter. It is cheaper, but the
  sub-agents would inherit the context of the session whose decisions they judge.
- **A multi-agent workflow**, with one agent per axis and a verifier per finding. It is more thorough, but
  it costs an order of magnitude more for a gain the two-role review has not yet been shown to leave on
  the table. It can be reconsidered if a review's report shows an axis looked at too thinly.
- **A review by the maintainer alone, with Claude preparing the file.** This was the form of M02's review
  of 2026-09-29. It keeps the maintainer's judgement, but it asks the maintainer to find the problems as
  well as to arbitrate them.
- **Every finding blocking the milestone.** A gap a later milestone already plans would hold up a milestone
  that has nothing left to do about it.
- **The report as a comment on the review's issue.** It would not be versioned beside the code it judges, and
  would not be published with the project documents.
- **The review fixing everything it keeps.** Its pull request would grow to the size of a slice, and the
  reviewer would be reviewing its own fixes.

### What would reopen it

Two reviews in a row that find nothing a later session did not find anyway. Or a review whose cost, in
sessions, stops being small next to the milestone it reviews.
