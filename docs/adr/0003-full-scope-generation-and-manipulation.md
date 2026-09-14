# 3. Full scope: generation and manipulation

Date: 2026-09-12

## Status

Accepted

## Context

Decided after an initial framing of "generation only".

## Decision

We will decided after an initial framing of "generation only".

## Consequences

- **Consequence** — manipulation requires a complete parser and a read/write object model. That is not a module
bolted onto the writer, it is the lower half of the library, and both paths share one object model.

---

Recorded as `D02` before this project adopted Architecture Decision Records; the identifier still
appears in commit messages and in `CLAUDE.md`.
