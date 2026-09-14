# 11. Fonts: an explicit registry, an embedded OFL set, optional web fonts

Date: 2026-09-12

## Status

Accepted

## Context

A container has no fonts installed, and depending on system fonts makes rendering irreproducible.
The embedded set guarantees that the first attempt works. Fetching remote `@font-face` resources is
possible but **off by default**: a network call during rendering is neither deterministic nor safe.

## Decision



## Consequences

Recorded so the choice is not silently revisited.

---

Recorded as `D10` before this project adopted Architecture Decision Records; the identifier still
appears in commit messages and in `CLAUDE.md`.
