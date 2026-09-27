# 8. Facade, immutable options, dependency injection

Date: 2026-09-12

## Status

Accepted

## Context

The library is called from applications, most of them ASP.NET Core services, that render documents
concurrently and configure the library once, at start-up.

## Decision

We will expose a facade: a thread-safe `IPdfRenderer` singleton, options as immutable records, and an
`AddAdCodicemPdf()` extension that registers it for dependency injection.

## Consequences

- **Rejected** — a fluent builder as the primary API — it can be layered on top later; the reverse is not true.

---

Recorded as `D07` before this project adopted Architecture Decision Records; the identifier still
appears in commit messages and in `CLAUDE.md`.
