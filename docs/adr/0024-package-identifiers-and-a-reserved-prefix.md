# 24. Package identifiers, and a reserved prefix

Date: 2026-09-13

## Status

Accepted

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
