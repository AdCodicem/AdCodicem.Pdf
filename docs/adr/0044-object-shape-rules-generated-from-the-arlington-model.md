# 44. Object-shape rules generated from the Arlington model

Date: 2026-09-26

## Status

Accepted on 2026-09-26, by the maintainer, for M2's third slice. It adds a source of rules to the structural
profile of [36](0036-validation-lives-in-the-core-conformance-in-a-satellite.md) without changing its engine,
its identifiers' grammar or its severities.

## Context

M2's object-graph family must check that each dictionary carries the entries its `/Type` requires, with the
types the specification gives them, and that a version does not use keys it deprecates. ISO 32000-2 defines
several hundred dictionary types; writing those checks by hand would take many sessions, drift from the
specification, and leave most types unchecked.

The PDF Association publishes the **Arlington PDF Model**: a machine-readable description of every object
in ISO 32000-2 — each key, its types, whether it is required, the versions that introduce or deprecate it,
and the conditions between keys — as TSV files under the Apache License 2.0. veraPDF, PDFix and BFO already
derive checks from it.

M2's first rule of judgement also applies: a validator that calls a widely read file broken is wrong, not
strict. A large share of real files carry keys the model says a type must not have, or lack keys it says a
type requires, and every reader opens them.

## Decision

We will generate the object-shape rules from the Arlington model at build time.

- A generator reads the model's TSV files, pinned to one commit of the model's repository, and emits the
  rule tables as static data in the core — no file is read at run time, and nothing is interpreted by
  reflection, so the core stays trimming- and AOT-compatible.
- The rules are **version-aware**: a key is checked against the version the file declares.
- Every generated finding is a **warning** at most, under the object family's identifiers; a condition an
  error would need is written by hand, with its own justification.
- The model's licence is Apache-2.0: its notice travels with the generated data, and `NOTICE` names it.
- A model rule that disagrees with qpdf and with the corpus's well-formed documents is overridden by name,
  with the reason, rather than dropped in silence.

## Consequences

- The structural profile covers every dictionary type the specification defines, from one pinned input.
- Updating to a new commit of the model is a reviewed change: the corpus tests show every finding it adds or
  removes.
- The generator and the pinned model add a build step that CI must run and test.
- **Rejected** — hand-written rules for every type (slow, and never complete); reading the TSV files at run
  time (a file dependency and reflection in the core); leaving shape rules to M20's conformance profiles
  (they are structural, and ADR 36 puts structural rules in the core).
- **What would reopen it** — the model's licence changing, or its maintenance stopping, after which the
  generated tables would be frozen and maintained by hand.
