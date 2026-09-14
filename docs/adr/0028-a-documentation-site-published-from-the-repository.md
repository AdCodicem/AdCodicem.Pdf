# 28. A documentation site, published from the repository

Date: 2026-09-13

## Status

Accepted

## Context

For a young library, the roadmap, the decisions and their
rejected alternatives are the most useful thing a reader has when deciding whether to depend on it.

## Decision

Docusaurus in `website/`, deployed to
GitHub Pages, publishing both the user-facing documentation and `docs/` unchanged.

## Consequences

- **Consequence** — `docs/` is written for two audiences at once, and the site build runs in CI so a document
that does not build is caught before it reaches the default branch.

---

Recorded as `D27` before this project adopted Architecture Decision Records; the identifier still
appears in commit messages and in `CLAUDE.md`.
