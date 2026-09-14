# 1. Record architecture decisions

Date: 2026-09-12

## Status

Accepted

## Context

Decisions about this project's structure, dependencies, and trade-offs tend
to get made once, in a PR discussion or a chat, and then forgotten — so
later on nobody knows *why* something is the way it is, only that it is.

## Decision

We will record significant, hard-to-reverse architectural decisions as
Architecture Decision Records (ADRs) in `docs/adr/`, one file per decision,
following the format in `adr-template.md` — a format
originally proposed by Michael Nygard.

Not every decision needs an ADR — only ones that are costly to reverse or
that a future contributor (including future-you) would otherwise have to
re-derive from git blame.

## Consequences

Slightly more upfront friction for decisions that qualify. In exchange,
the reasoning behind the current shape of the project stays discoverable
instead of living only in closed PR threads.
