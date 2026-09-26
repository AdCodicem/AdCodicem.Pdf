# 40. The caller chooses PDF 1.7 or PDF 2.0 output

Date: 2026-09-26

## Status

Accepted on 2026-09-26, by the maintainer, before the writer (M3) exists. It sets the version policy M3
implements, and the PDF/UA part M13 targets.

## Context

Two families of standards now pull the writer apart:

- **PDF 1.7-based** — PDF/A-2 and PDF/A-3, and therefore Factur-X and ZUGFeRD, which require PDF/A-3;
  PDF/UA-1, which is what an accessible Factur-X invoice can claim beside PDF/A-3a.
- **PDF 2.0-based** — PDF/UA-2, Well-Tagged PDF, PDF/A-4, the PDF 2.0 structure namespace, the AES-256
  security handler revision 6 as the only one PDF 2.0 keeps, and the ISO/TS 32001 to 32005 extensions.

The structure tree M13 emits differs between PDF/UA-1 and PDF/UA-2 (namespaces, new structure types,
structure destinations), and the writer's handling of metadata differs between 1.7 and 2.0 (the document
information dictionary is deprecated in 2.0 in favour of XMP). Choosing one family now would either keep
Factur-X from being accessible or defer PDF/UA-2 indefinitely; deciding nothing would leave M3 to guess,
and M13 to be rewritten.

## Decision

We will let the caller choose the output version, and target PDF/UA-1 first.

- **The writer** writes PDF 1.7 or PDF 2.0, as the caller's options say. It computes the minimum version the
  document's features need, raises the output to it when a merge or a feature requires it, and reports the
  raise as a diagnostic — never silently. `/Extensions` entries of every input are unioned and written.
- **In 2.0 output**, XMP is the authoritative metadata, the deprecated information-dictionary entries are not
  written, and text strings may be UTF-8. In 1.7 output both metadata forms are written and kept in step.
- **M13 targets PDF/UA-1**, which works with PDF/A-3 and Factur-X; **PDF/UA-2, Well-Tagged PDF and PDF/A-4
  come in M28**, on the 2.0 writer, without changing what M13 emits for 1.7 output.

## Consequences

- M3 carries a small cost now — a version option, the minimum-version computation, `/Extensions` — that
  spares a retrofit of the writer later.
- An accessible Factur-X invoice (PDF/A-3a and PDF/UA-1) is possible from M14.
- Every milestone that adds a feature states the minimum version it needs, so the computation stays whole.
- **Rejected** — PDF 1.7 only (PDF/UA-2, WTPDF and PDF/A-4 deferred indefinitely, while the European
  Accessibility Act raises demand for them); PDF 2.0 and PDF/UA-2 first (Factur-X would still need a 1.7
  path, so both would be built at once).
- **What would reopen it** — Factur-X accepting a PDF 2.0 base (PDF/A-4f), which would let M13 target
  PDF/UA-2 directly.
