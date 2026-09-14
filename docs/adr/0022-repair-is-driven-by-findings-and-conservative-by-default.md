# 22. Repair is driven by findings, and conservative by default

Date: 2026-09-13

## Status

Accepted

## Context

Repair is where a library quietly destroys data. A change nobody asked for is a corruption, and
rewriting a file wholesale invalidates signatures and any external byte-range reference.

## Decision

Every change is justified by a
validation finding, recorded in a report, and applied by preference as an incremental update that leaves
the original bytes in place.

## Consequences

- **Rejected** — repairing as a side effect of reading (the caller must choose), and a single "fix everything"
mode with no account of what it did.

---

Recorded as `D21` before this project adopted Architecture Decision Records; the identifier still
appears in commit messages and in `CLAUDE.md`.
