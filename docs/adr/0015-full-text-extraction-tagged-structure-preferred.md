# 15. Full text extraction, tagged structure preferred

Date: 2026-09-12

## Status

Accepted

## Context

A caller who reads a document wants its text as a reader meets it — words, lines and paragraphs in reading
order, tables as tables —, while a content stream holds glyphs painted in whatever order its producer chose.
A tagged document records that order in its structure tree; an untagged one leaves it to be inferred from
positions, which no heuristic gets right every time.

## Decision

We will extract text fully: positioned glyphs, grouped into lines and paragraphs, with table detection; and,
when the document is tagged, we will follow its structure tree rather than heuristics.

## Consequences

- **Note** — table detection is heuristic by nature; the API must expose a confidence score rather than imply an
exact result.

---

Recorded as `D14` before this project adopted Architecture Decision Records; the identifier still
appears in commit messages and in `CLAUDE.md`.
