# Test corpus and acceptance rules

Read this before closing any milestone.

## Why a corpus

Synthetic files prove that the code handles what we imagined. Real documents prove that it handles what
producers actually emit — and those two sets differ far more than anyone expects. Word puts its
cross-reference table in one shape, Chromium in another, a scanner in a third; each of them writes fonts,
images, metadata and object streams in its own way, and each of them gets some detail slightly wrong.

Hence invariant 10: **no milestone closes on synthetic files alone**. Every milestone states which real
documents it must handle and what handling them means, and those statements are executable tests.

**Contributing a document**: `docs/corpus-contributions.md` says what is wanted, what to check before
handing anything over, and where it goes.

## Where the documents come from

| Origin | What it gives | Rule |
|---|---|---|
| Generated in-container by real producers — Chromium (Skia backend), LibreOffice, Python producers from pypi | Genuine producer quirks, reproducible, no licensing question | The build script and the producer version are recorded in the manifest |
| Generated on Windows from our own content — Word's Save as PDF, the Microsoft Print to PDF driver, the PDF24 printer (`build/build_word.ps1`) | The office desktop's writers, which no container runs | Same content as the generated invoice; nothing the script cannot account for, including what a driver stamps on its own |
| Third-party documents found in public sources (`vendor/`) | Producers we will never run — Acrobat, InDesign, LiveCycle, copier firmware, a qualified-seal service — and damage from the wild | Attribution-only licence (ADR 23), no real person's name anywhere, byte-identical to the publisher's copy; source URL and SHA-256 recorded; see `docs/corpus-sources.md` |
| Contributed real documents | Everything the generators never do: legacy tooling, scanners, foreign-language typography, damaged files from the wild | No confidential content; origin and licence recorded; anonymised before committing |
| Derived variants | Encryption, linearisation, object-stream rewrites, and deliberate damage | Derived by a recorded, repeatable transformation from a document already in the corpus |

Documents are **committed**, not generated at test time: producer output changes with producer version, and
a test suite that shifts under you is worse than no test suite. Regeneration is an explicit act, reviewed
like any other change. It also keeps CI free of any network dependency.

Keep each document small — a few hundred kilobytes at most, except the one deliberate large-document case.

## Layout

```
tests/corpus/
  manifest.json          the index: one entry per document, written by build/build_corpus.py
  sources/               the inputs documents are generated from (HTML, ODT, scripts)
  build/                 the generation scripts and their recorded producer versions
  documents/             the committed PDF files, by category
  contributed.json       entries for files under documents/ the build script cannot produce
  vendor/                third-party files, by source, with NOTICE and LICENSES/ for their terms
  vendor.json            entries for the files under vendor/
```

`build_corpus.py --committed-only` merges `vendor.json` and `contributed.json` into the manifest without
regenerating anything, with the referee's verdict on each file; `tests/corpus/README.md` gives the
container command that makes that verdict the integration tests' own.

## Manifest

One entry per document. The manifest is the contract: tests read it, and a document nobody asserts
anything about is not part of the corpus.

```jsonc
{
  "file": "documents/invoice/chromium-invoice-fr.pdf",
  "title": "French invoice with VAT breakdown",
  "useCase": "invoice",                  // invoice | report | contract | form | scan | archival | mixed
  "producer": "Chromium 147 (Skia PDF backend)",
  "origin": "generated",                 // generated | contributed | derived
  "licence": "MIT (generated from our own source)",
  "features": ["xref-stream", "object-streams", "type0-subset", "utf16-metadata"],
  "expect": {
    "pages": 2,
    "opensWithoutRepair": true,
    "textContains": ["Facture", "TVA 20", "Total TTC"],
    "diagnostics": []
  }
}
```

`expect` grows as milestones land: page count and repair status from M1, validation findings from M2,
round-trip fidelity from M3, repaired-equals-original from M4, merge invariants from M5, extracted text
from M10, conformance verdicts from M12. An expectation is
never weakened to make a test pass — either the library is fixed, or the expectation is corrected with the
reason recorded in the commit message.

Three fields serve that rule:

- `unsupported` — the reason the library cannot yet meet the entry's expectations, and the milestone that
  will. The acceptance tests skip the document with that reason in their output; the expectations stay as
  the independent tool established them.
- `conformanceValid` — veraPDF's verdict on the PDF/A level the document claims (`claimsConformance`),
  for M12 to agree with.
- `source` — for a third-party file, the URL it was retrieved from, the date and the SHA-256 of the bytes
  as published, so provenance is checkable without trusting the repository.

## Use-case categories

The corpus is organised by what the document *is*, not by which feature it exercises, so that coverage
gaps are visible in business terms.

| Category | Represents | Must eventually include |
|---|---|---|
| `invoice` | The highest-volume use case | Line items and totals, logo, accented text, a Factur-X invoice with its embedded XML, a PDF/A-3 invoice |
| `report` | Multi-page generated documents | Table of contents with real page numbers, repeated table headers, page numbering, bookmarks, charts as vector graphics |
| `contract` | Documents assembled from parts | Appendices from third-party files, an existing signature, file attachments, mixed page sizes and orientations |
| `form` | Interactive documents | AcroForm fields, filled and flattened states, field appearances |
| `scan` | Documents that are images | Image-only pages, CCITT and JPEG encodings, a mixed text-and-scan file |
| `archival` | Regulated output | PDF/A-2b and PDF/A-3b, tagged PDF/UA, embedded ICC profile, XMP metadata |
| `damaged` | What the world actually sends | Missing cross-reference table, offsets off by a few bytes, truncated tail, junk before the header, several incremental updates, a lying `/Length` |
| `stress` | Scale | A document of several thousand pages, and one with a very large embedded image |

## How a milestone is accepted

Each milestone file lists its acceptance conditions in the form *"these documents, this behaviour,
verified by this test"*. A milestone is closed when, and only when:

1. its functional tests pass;
2. its acceptance conditions pass **on the corpus**, in CI, with no document skipped;
3. its performance budgets hold on the `stress` documents;
4. `docs/status.md` records the measurements rather than promising them.

A document that cannot be handled and is not going to be handled in this milestone is not deleted: its
expectation is recorded as unsupported (`expect.unsupported`), with the milestone that will address it.
Silence is not an option; a known gap must be written down where the next session will read it. A
milestone is not closed while an entry still names it as the one that will.
