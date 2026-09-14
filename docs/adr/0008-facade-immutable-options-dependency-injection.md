# 8. Facade, immutable options, dependency injection

Date: 2026-09-12

## Status

Accepted

## Context

A thread-safe `IPdfRenderer` singleton, options
as records, `AddAdCodicemPdf()`.

## Decision

We will a thread-safe `IPdfRenderer` singleton, options
as records, `AddAdCodicemPdf()`.

## Consequences

- **Rejected** — a fluent builder as the primary API — it can be layered on top later; the reverse is not true.

---

Recorded as `D07` before this project adopted Architecture Decision Records; the identifier still
appears in commit messages and in `CLAUDE.md`.
