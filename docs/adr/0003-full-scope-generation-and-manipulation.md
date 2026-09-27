# 3. Full scope: generation and manipulation

Date: 2026-09-12

## Status

Accepted

## Context

The project was first framed as generation only: HTML in, PDF out. The documents it targets rarely stop
there — an invoice is archived, a report is merged with its appendices, a contract is signed, a case file
assembles third-party PDFs — and each of those steps reads and changes a PDF the library did not write.

## Decision

We will cover generation and manipulation alike — assembly, content, extraction, forms, security and
optimisation —, a scope decided after an initial framing of "generation only".

## Consequences

- **Consequence** — manipulation requires a complete parser and a read/write object model. That is not a module
bolted onto the writer, it is the lower half of the library, and both paths share one object model.

---

Recorded as `D02` before this project adopted Architecture Decision Records; the identifier still
appears in commit messages and in `CLAUDE.md`.
