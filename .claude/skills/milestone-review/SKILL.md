---
name: milestone-review
description: Run a milestone's adversarial review (ADR 46) as a fresh session — two passes, blind then informed, five axes, every finding put to a refuter, triage shown to the maintainer, report in docs/reviews/. Use only when the maintainer types /milestone-review Mxx, or asks for a milestone's review.
---

# Milestone review

You are the review session for the milestone named in the arguments (`M02`, `M01`, a sub-milestone). You
worked on none of it; keep it that way — do not ask for, or read, a summary of the sessions that did.

1. **Read `docs/milestone-review.md` in full, now.** It is your reading list and your procedure; it replaces
   `CLAUDE.md`'s *How to approach a session*. `CLAUDE.md` remains the frame you check the milestone against.
2. Follow it in order: *Before starting* (stop and tell the maintainer if an issue other than the review's is
   still open), *Pass 1 — blind* (candidates written to the scratchpad before anything justifying them is
   read), *Pass 2 — informed*, the five axes, *Refutation* (refuters are sub-agents that see the finding and
   its evidence, not your reasoning), *Triage*.
3. Show the maintainer the triage before filing or fixing anything; put each finding that would reopen a
   settled choice to them one question at a time, in their language.
4. Produce what *What the review produces* lists: `docs/reviews/<milestone>.md` in the form *The report*
   gives, its row in `docs/reviews/README.md`, the trivial fixes as `docs:`/`refactor:`/`test:` commits, the
   issues, `docs/status.md`, and one pull request that closes the review's issue.

If no milestone was given, ask which one; do not guess from `docs/status.md`.
