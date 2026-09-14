# 21. Validation is a rule engine, and conformance is a profile of it

Date: 2026-09-13

## Status

Accepted

## Context

Repair, conformance and every later guarantee are expressed in terms of findings. Two validators
with two vocabularies would mean two answers to "is this document sound?".

## Decision

Validation lands immediately
after reading (M2), as the structural profile of a rule engine whose findings carry stable identifiers.
PDF/A and PDF/UA arrive later (M12) as further profiles, not as a separate validator.

## Consequences

- **Consequence** — rule identifiers are public API from the day they ship, and `docs/validation-rules.md`
documents them.

---

Recorded as `D20` before this project adopted Architecture Decision Records; the identifier still
appears in commit messages and in `CLAUDE.md`.
