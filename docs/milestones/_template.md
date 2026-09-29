# M<n> — <title>

> State: to do | in progress | done — Depends on: M<n-1>

## Goal

One sentence: what the library will be able to do at the end that it cannot do today.

## Scope

What is in. What is explicitly **out**, and which milestone it is deferred to.

## Design

Main types, responsibilities, boundaries. Precise enough that a session can start without reinventing;
loose enough not to freeze what will be discovered while writing.

## Slices

Each slice is vertical, testable, and ends on a green commit. When the milestone starts, each becomes an issue
labeled `slice`, filed under its GitHub milestone (`docs/roadmap.md`, *Tracking on GitHub*), with a
`Blocked by:` line naming the slice before it when the order matters; an open issue whose `Blocks:` line names
the slice in words has that line rewritten to the slice's number.

The last slice is always the milestone's **adversarial review** (ADR 46, `docs/milestone-review.md`): an issue
labeled `slice` and `review`, opened with the others, whose `Blocked by:` line names every other issue filed under
the milestone. An XL milestone lists one review per sub-milestone, and a last one over their seams.

## Tests required

**Unit** — the behaviors to cover, degenerate and hostile cases included.

**Integration** — what an independent tool must confirm about the documents this milestone produces or
reads, and which tool confirms it.

## Acceptance conditions

The real documents this milestone must handle, what handling them means, and the test that proves it.
Written in the form *"these corpus documents, this behavior, verified by this test"*. See
`docs/corpus.md`. A milestone with no acceptance conditions is not specified.

## Traps

What has already bitten, or what is known to be treacherous in the specification.

## Documentation

Which pages of `docs/website/docs` this milestone adds or changes, and which project documents it touches.
A milestone that adds public API without documenting it is not finished.

## Exit criteria

Checkboxes verifiable by tests, including the acceptance conditions above and the seven points of the
definition of done in `docs/roadmap.md`. The last is always:

- [ ] The milestone's review is recorded in `docs/reviews/M<n>.md`, and every issue it filed under the milestone is
  closed.

The milestone closes when they are all ticked and CI is green.
