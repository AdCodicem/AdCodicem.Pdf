# 23. Third-party corpus documents are vendored only under attribution-only licences

Date: 2026-09-13

## Status

Accepted

## Context

Test data ships inside an MIT repository, so its licence must not reach back into the software.

## Decision

A curated subset
of the veraPDF corpus (CC BY 4.0) is committed with a NOTICE; ShareAlike collections are not vendored, and
large corpora are fetched on demand for local investigation rather than committed.

## Consequences

Recorded so the choice is not silently revisited.

---

Recorded as `D22` before this project adopted Architecture Decision Records; the identifier still
appears in commit messages and in `CLAUDE.md`.
