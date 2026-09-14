# 5. SkiaSharp and HarfBuzzSharp allowed, in `.Html` only

Date: 2026-09-12

## Status

Accepted

## Context

Writing a quality shaper takes years, and the result would be typographically worse. Confining
these to the HTML package keeps the core dependency-free.

## Decision

HarfBuzz for shaping (ligatures, kerning,
complex scripts, bidirectional text), Skia for image decoding and SVG fallback rendering.

## Consequences

- **Rejected** — everything managed (disproportionate cost), Skia everywhere (contaminates the core).

---

Recorded as `D04` before this project adopted Architecture Decision Records; the identifier still
appears in commit messages and in `CLAUDE.md`.
