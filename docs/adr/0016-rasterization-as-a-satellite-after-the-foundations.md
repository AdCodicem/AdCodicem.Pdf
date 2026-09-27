# 16. Rasterization as a satellite, after the foundations

Date: 2026-09-12

## Status

Accepted

## Context

Rendering pages to images — thumbnails, previews, visual tests — needs a content stream interpreter and a
graphics backend, which the foundations do not provide and most callers never need.

## Decision

We will ship rasterization as a satellite package, after the foundations. It will reuse the content stream
interpreter written for extraction (M15). Until then, visual tests rely on an external tool in CI.

## Consequences

Recorded so the choice is not silently revisited.

---

Recorded as `D15` before this project adopted Architecture Decision Records; the identifier still
appears in commit messages and in `CLAUDE.md`.
