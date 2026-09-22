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

## Amendment, 2026-09-22

The site moved to `docs/website` with the standard layout, and the move broke it without failing a
build: the project documents' plugin pointed at `docs/`, which now contained the site, and Docusaurus
compiles everything under a plugin's directory with that plugin's loader. Every user-facing page was
compiled twice and published as its own JavaScript, printed as text, for as long as the site had existed.

The decision stands; how it is carried out changed. The project documents are **copied** into the site
before each build (`docs/website/scripts/sync-project-docs.mjs`) and published from the copy, still
unchanged, with edit links to the originals. And a building site is no longer taken as a working one:
every build ends by reading its own pages for compiled MDX and escaped markup
(`docs/website/scripts/check-site.mjs`), and fails if it finds any.

---

Recorded as `D27` before this project adopted Architecture Decision Records; the identifier still
appears in commit messages and in `CLAUDE.md`.
