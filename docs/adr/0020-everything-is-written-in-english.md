# 20. Everything is written in English

Date: 2026-09-13

## Status

Accepted. Amended on 2026-09-27: the English is American.

## Context

The package is public, and a mixed-language repository forces every contributor to switch
languages between a file and its documentation.

## Decision

Code, public API, XML documentation, project documentation, commit messages, diagnostics and exception
messages are all written in English.

The spelling is American throughout: type and member names, package identifiers, command-line verbs,
diagnostic codes, corpus manifest keys, documentation and prose alike — color, catalog, license, optimize,
behavior, gray. It is the spelling of the .NET base class library and of PDF's own keys (`/ColorSpace`), so
an identifier and the key it names agree. Proper names keep their own spelling (the UK's Open Government
License, Etalab's Licence Ouverte, the Xerox WorkCentre), as do text quoted from a document and HTML's
`aria-labelledby`.

## Consequences

Recorded so the choice is not silently revisited.

The repository was written in British English until 2026-09-27, when the maintainer chose American English
and it was converted in one pass, before any stable release: the identifiers the milestone files plan
(`PdfColorSpace`, `PdfOptimizer`, the verbs `optimize`, `normalize` and `sanitize`, the `optimize.*` codes),
the corpus manifest's `license` key, the file names that carried a British spelling, and the prose. No
shipped public API carried one; had it, ADR 30 would have allowed the rename in a preview.

---

Recorded as `D19` before this project adopted Architecture Decision Records; the identifier still
appears in commit messages and in `CLAUDE.md`.
