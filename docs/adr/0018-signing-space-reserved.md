# 18. Signing: space reserved

Date: 2026-09-12

## Status

Accepted

## Context

Business documents are signed, often by several parties in turn, and a signature covers byte ranges of the
file: any rewrite that shifts bytes breaks it. Signing itself — PAdES, time stamps, validation data — is too
large to hold up the foundations.

## Decision

We will reserve the space for signing in the writer from the start: the writer must be able to produce
incremental updates and to leave an existing signature intact. PAdES follows, behind an `IPdfSigner`
abstraction so signing can be delegated to an HSM or a qualified provider.

## Consequences

Recorded so the choice is not silently revisited.

---

Recorded as `D17` before this project adopted Architecture Decision Records; the identifier still
appears in commit messages and in `CLAUDE.md`.
