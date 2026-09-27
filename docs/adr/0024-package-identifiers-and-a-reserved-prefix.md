# 24. Package identifiers, and a reserved prefix

Date: 2026-09-13

## Status

Accepted. Amended by [36](0036-validation-lives-in-the-core-conformance-in-a-satellite.md) on 2026-09-26:
`.Validation` becomes `.Conformance`, for the PDF/A and PDF/UA profiles (M20), since the validation engine
itself lives in the core. The prefix was reserved on nuget.org by 2026-09-26: the search API marks
`AdCodicem.Pdf` as verified.

## Context

The packages need identifiers on nuget.org before their first publish, and a published identifier cannot be
renamed.

## Decision

We will publish `AdCodicem.Pdf` for the core, then `.Validation`, `.Html`, `.AspNetCore`, `.FacturX`,
`.Rendering` and `.Signing` as satellites under the same prefix. All seven were unclaimed when checked on
2026-09-13. (ADR 36 renamed `.Validation` to `.Conformance` before it was ever published.)

## Consequences

- **Consequence** — the `AdCodicem.` prefix is to be reserved on nuget.org with the first publish, so nobody
else can publish under the name and consumers see a verified owner.
- **Consequence** — the satellites named after 2026-09-13 — `.Tool` (M6), `.Fonts` (M8), `.Barcodes` (M10),
`.CaseFile` (M18), `.Imaging` (M22), `.Compare` (M24), `.Docx` (M31) — take identifiers of the same form under
the reserved prefix; `docs/architecture.md` §2 is their register, and this record is not amended for each.

---

Recorded as `D23` before this project adopted Architecture Decision Records; the identifier still
appears in commit messages and in `CLAUDE.md`.
