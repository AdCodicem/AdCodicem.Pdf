# 19. Milestones are accepted on real documents

Date: 2026-09-13

## Status

Accepted

## Context

Hand-built test files prove the code handles what we imagined. Producers emit what they emit.

## Decision

Every milestone states acceptance conditions verified
against `tests/corpus`, a set of documents produced by real generators and contributed from the field,
rather than only on files we wrote by hand.

## Consequences

- **Consequence** — the corpus and its manifest are part of the library's contract; see `docs/corpus.md`.

---

Recorded as `D18` before this project adopted Architecture Decision Records; the identifier still
appears in commit messages and in `CLAUDE.md`.
