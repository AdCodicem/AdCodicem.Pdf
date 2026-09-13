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

Each slice is vertical, testable, and ends on a green commit.

## Tests required

The behaviours to cover, degenerate and hostile cases included.

## Acceptance conditions

The real documents this milestone must handle, what handling them means, and the test that proves it.
Written in the form *"these corpus documents, this behaviour, verified by this test"*. See
`docs/corpus.md`. A milestone with no acceptance conditions is not specified.

## Traps

What has already bitten, or what is known to be treacherous in the specification.

## Exit criteria

Checkboxes verifiable by tests, including the acceptance conditions above. The milestone closes when they
are all ticked and CI is green.
