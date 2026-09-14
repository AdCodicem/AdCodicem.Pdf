# 26. Test stack: xUnit v3, AwesomeAssertions, NSubstitute

Date: 2026-09-13

## Status

Accepted

## Context

The seams that exist are real —
`IPdfObjectSource`, and later the font resolver, the signer and the web-font fetcher — and some
behaviour is only observable as an interaction. "The parser resolves an indirect length exactly once" is
a statement about a call, not about a value.

## Decision

Assertions read
`value.Should().Be(…)`; AwesomeAssertions is the MIT-licensed continuation of that style.

## Consequences

- **Guard** — substitutes are for interaction assertions and for seams that would otherwise need a network, a
clock or a container. A value-oriented library tested through mocks tests its own mocks.

---

Recorded as `D25` before this project adopted Architecture Decision Records; the identifier still
appears in commit messages and in `CLAUDE.md`.
