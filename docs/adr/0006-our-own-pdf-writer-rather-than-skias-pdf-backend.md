# 6. Our own PDF writer rather than Skia's PDF backend

Date: 2026-09-12

## Status

Accepted

## Context

Skia produces an opaque PDF — no PDF/A, no tagged structure, no control over compression, no
streaming output. Conformance and streaming are the heart of the promise.

## Decision



## Consequences

- **Rejected** — `SKDocument.CreatePdf` (quick to ship, a dead end afterwards).

---

Recorded as `D05` before this project adopted Architecture Decision Records; the identifier still
appears in commit messages and in `CLAUDE.md`.
