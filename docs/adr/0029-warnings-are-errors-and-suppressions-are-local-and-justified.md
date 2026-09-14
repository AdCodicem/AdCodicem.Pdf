# 29. Warnings are errors, and suppressions are local and justified

Date: 2026-09-14

## Status

Accepted

## Context

A warning nobody has to fix is a warning nobody reads, and a build that prints forty of them
hides the one that matters. The two that CI caught in the first week — a `stackalloc` inside a loop, and
an obsolete API — were both worth acting on and would both have been lost in noise.

## Decision

`TreatWarningsAsErrors` is on
across the solution, analysis runs at `latest-recommended`, code style is enforced in the build, and XML
documentation is required on the public API.

## Consequences

- **How a rule that is wrong here is handled** — suppressed at the symbol that triggers it, with a
`Justification` a reader can weigh. `PdfDictionary` and `PdfStream` keep the names the specification uses;
renaming them so an analyzer stops objecting would make every reader of the specification translate. The
single exception is scoped in `.editorconfig`: test method names carry underscores because they are
sentences, and CA1707 objects only there.
- **Rejected** — a global `NoWarn` list, which is how a codebase quietly stops enforcing anything.

---

Recorded as `D28` before this project adopted Architecture Decision Records; the identifier still
appears in commit messages and in `CLAUDE.md`.
