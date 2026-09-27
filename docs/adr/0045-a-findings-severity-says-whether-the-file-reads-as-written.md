# 45. A finding's severity says whether the file reads as written

Date: 2026-09-27

## Status

Accepted on 2026-09-27, by the maintainer, during M02's second slice, before any of its rules shipped. It
amends [36](0036-validation-lives-in-the-core-conformance-in-a-satellite.md), whose bar for `Error` — readers
will disagree about the document — it replaces. Implemented by `PdfValidationSeverity` and the severities of the
structural profile's rules, listed in `docs/validation-rules.md`.

## Context

ADR 36 gave findings a scale of three — `Information`, `Warning`, `Error` — and set the bar for `Error` at "readers
will disagree about the document", taken from M02: a validator that calls sound files broken trains its users to
ignore it.

Applying that bar to the file and cross-reference rules of M02's second slice showed what it costs. It asks the
validator to predict how readers it does not run will behave, and the predictions did not hold. Two severities
proposed from it were contradicted by measurement the same day:

- An entry giving generation 10000 for an object written `2 0 obj` (iPRES `t03-010`) had been called a warning,
  as a fault every reader fixes; qpdf does not — it reads the reference as null and finds no page.
- A `/Size` of 4 for a file whose catalog is object 5 (`t04-016`) had been called a warning too; ISO 32000-1
  (Table 15) makes a conforming reader ignore an object numbered above `/Size`, which would lose the catalog. Made
  an error on that ground, it contradicted M02's own statement that the iPRES files qpdf only warns about earn
  "warnings for those, never errors".

A bar the project cannot check against its own reader or its corpus decides severities by argument. The
maintainer proposed one that can be checked: a warning is a file that was not conforming but was read; an error
is a file that cannot be read correctly.

"Correctly" needs care. When the reader rebuilds a file's index by scanning it, it often recovers exactly what the
author wrote, and cannot know that it did: in a file updated incrementally, a scan may bring back an object an
update deleted, or keep the wrong copy of one an update replaced.

A fourth level was considered, `Critical`, to tell a file nothing can be recovered from from one a repair can
restore. A file with nothing to read — empty, without a single object — does not open, so no report would carry
it; among files that open, it would today apply to one case, a file where no object is a catalog. What "repairable"
means is `PdfRepair`'s to define (M05).

## Decision

We will set a finding's severity by whether the reader can vouch that it reads the file as it was written.

- **`Error`**: the reader cannot vouch for what it read. It rebuilt the index by scanning the file, lost part of
  what the file holds — a section, an object stream, an object that is nowhere —, or chose what the file does not
  designate: a catalog the trailer's `/Root` does not name, a trailer salvaged from broken syntax.
- **`Warning`**: the file breaks the specification, and is read all the same as it was evidently meant, the rest of
  the file confirming that reading — white space before what an offset names, an object a few bytes from its
  offset whose own header confirms it, a `/Size` that miscounts, a generation that the references and the object's
  header agree against. The reader reads it; a stricter reader may not.
- **`Information`**: unchanged — nothing is wrong, or something was not checked.

One rule keeps one severity (ADR 36): where a fault is sometimes readable and sometimes not, it is two rules.

The scale stays the findings' own. The reader's diagnostics keep theirs (`PdfDiagnosticSeverity`), which says what
the reader did rather than how wrong the file is, as ADR 36 decided.

There is no `Critical` level; whether one is needed is M05's question, when repair gives "repairable" a meaning
([#109](https://github.com/AdCodicem/AdCodicem.Pdf/issues/109)).

## Consequences

- Three rules of the second slice move from `Error` to `Warning`: `file.header-missing` (Acrobat refuses such a
  file, the reader reads it), `xref.generation-mismatch` (qpdf reads the object as missing, the reader reads it as
  the references name it), `xref.object-past-size` (a reader applying Table 15 loses the object, the reader reads
  it). M02's statement about the iPRES files qpdf only warns about holds without exception.
- A severity is checked against the reader and the corpus, not argued about other readers: an `Error` rule is one
  whose finding goes with a rebuild, a loss or a choice the reader made, and the corpus tests hold each document to
  its findings.
- A report no longer says through its errors how portable a file is. That a stricter reader may refuse it is a
  warning; conformance to a strict profile is the business of the PDF/A and PDF/UA profiles (M20).
- A reader that improves — one that finds the section `startxref` names a few bytes off, as it already finds a
  `/Prev` section — does not change a rule's severity: the rule is defined by the fault, and a finding that no
  longer goes with a rebuild belongs to a warning rule of its own, as `xref.section-shifted` is to
  `xref.section-not-found`.
- **Rejected** — keeping "readers will disagree": it depends on readers the project does not run, and was wrong
  twice in one slice.
- **Rejected** — counting a rebuild that recovers the catalog and the pages as a correct reading: the reader cannot
  tell a scan that recovered the file from one that brought back what an update deleted.
- **Rejected** — a `Critical` level now: it would apply to one case, and its definition belongs to repair.
- **Amended on acceptance**: ADR 36 (a note), `PdfValidationSeverity`, `docs/milestones/M02.md`,
  `docs/validation-rules.md`, and the site's validation page.
- **What would reopen it**: a rule whose faults cannot be told apart by what the reader did, or M05 finding that
  repair needs a level the three do not give.
