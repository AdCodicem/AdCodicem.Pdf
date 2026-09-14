# 15. Full text extraction, tagged structure preferred

Date: 2026-09-12

## Status

Accepted

## Context

Positioned glyphs, grouping into lines and
paragraphs, table detection, and, when the document is tagged, following its structure tree rather than
heuristics.

## Decision

We will positioned glyphs, grouping into lines and
paragraphs, table detection, and, when the document is tagged, following its structure tree rather than
heuristics.

## Consequences

- **Note** — table detection is heuristic by nature; the API must expose a confidence score rather than imply an
exact result.

---

Recorded as `D14` before this project adopted Architecture Decision Records; the identifier still
appears in commit messages and in `CLAUDE.md`.
