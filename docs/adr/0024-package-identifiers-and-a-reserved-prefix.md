# 24. Package identifiers, and a reserved prefix

Date: 2026-09-13

## Status

Accepted. Amended by [36](0036-validation-lives-in-the-core-conformance-in-a-satellite.md) on 2026-09-26:
`.Validation` becomes `.Conformance`, for the PDF/A and PDF/UA profiles (M12), since the validation engine
itself lives in the core. The prefix was reserved on nuget.org by 2026-09-26: the search API marks
`AdCodicem.Pdf` as verified.

## Context

`AdCodicem.Pdf` for the core, then
`.Validation`, `.Html`, `.AspNetCore`, `.FacturX`, `.Rendering` and `.Signing`. All seven were unclaimed
when checked on 2026-09-13.

## Decision

We will `AdCodicem.Pdf` for the core, then
`.Validation`, `.Html`, `.AspNetCore`, `.FacturX`, `.Rendering` and `.Signing`. All seven were unclaimed
when checked on 2026-09-13.

## Consequences

- **Consequence** — the `AdCodicem.` prefix is to be reserved on nuget.org with the first publish, so nobody
else can publish under the name and consumers see a verified owner.

---

Recorded as `D23` before this project adopted Architecture Decision Records; the identifier still
appears in commit messages and in `CLAUDE.md`.
