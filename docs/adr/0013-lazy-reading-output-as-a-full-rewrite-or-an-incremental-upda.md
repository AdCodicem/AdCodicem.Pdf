# 13. Lazy reading; output as a full rewrite or an incremental update

Date: 2026-09-12

## Status

Accepted

## Context

It is what allows a document of several hundred megabytes to be manipulated in a few megabytes of
memory, and an existing signature not to be invalidated.

## Decision



## Consequences

- **Rejected** — loading everything into memory (what most .NET libraries do, and contrary to the goal); a pure
streaming pipeline (rules out reordering, global deduplication and forms).

---

Recorded as `D12` before this project adopted Architecture Decision Records; the identifier still
appears in commit messages and in `CLAUDE.md`.
