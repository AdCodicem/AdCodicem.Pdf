# 2. Fully managed rendering

Date: 2026-09-12

## Status

Accepted

## Context

It is the only option compatible with the frugality goal. A Chromium instance costs 150 to 300 MB
and hundreds of milliseconds to start; a managed engine renders an invoice in a few megabytes and a few
milliseconds, and deploys into a distroless container.

## Decision

AngleSharp for HTML5 parsing; the CSS engine, the layout engine and the
PDF writer are ours.

## Consequences

- **Rejected** — wrapping Chromium or Playwright (footprint and deployment complexity), binding a native engine
such as PDFium (per-RID dependencies, incompatible with AOT).
- **Would reopen it** — a demonstrated need to render arbitrary web pages with JavaScript — which would be met
by adding a satellite backend, not by changing the core.

---

Recorded as `D01` before this project adopted Architecture Decision Records; the identifier still
appears in commit messages and in `CLAUDE.md`.
