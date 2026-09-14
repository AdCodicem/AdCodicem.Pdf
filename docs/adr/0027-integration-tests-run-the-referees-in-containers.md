# 27. Integration tests run the referees in containers

Date: 2026-09-13

## Status

Accepted

## Context

The corpus promises that our claims are checked by tools we did not write. Running them in
containers makes that reproducible: the same versions answer on a laptop and in CI, and nobody installs
anything. It also removes the last "please install this on the runner" from the build.

## Decision

`tests/AdCodicem.Pdf.IntegrationTests` uses
Testcontainers to run qpdf — and later veraPDF, pdftotext, mutool — against the corpus.

## Consequences

- **Consequence** — where no Docker daemon exists the tests skip with the reason attached, visible in the run.
CI has Docker, so skipping there would be a failure of the runner, not of the suite.

---

Recorded as `D26` before this project adopted Architecture Decision Records; the identifier still
appears in commit messages and in `CLAUDE.md`.
