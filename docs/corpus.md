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
| Remote documents we may use but not redistribute (`remote.json`, ADR 32) | What only a bug report, a vendor's sample, a ShareAlike set or a file too large to commit can give | Never committed, nor anything derived from them; fetched at a pinned SHA-256 from an immutable URL; tested by a separate job |

Documents are **committed**, not generated at test time: producer output changes with producer version, and
a test suite that shifts under you is worse than no test suite. Regeneration is an explicit act, reviewed
like any other change. It also keeps CI free of any network dependency.

The one exception is the **remote corpus** (ADR 32): documents whose licence forbids redistribution, or whose
size forbids committing them. `remote.json` describes them in the manifest's format, with a mandatory
`source.url` and `source.sha256`; `build/fetch_remote.py` downloads them into `remote/`, which git ignores,
and refuses any file whose hash differs — a changed file is a new document, reviewed as one. The test suite
merges the entries whose file is present, so the main CI job fetches nothing, finds nothing, and tests
exactly what is committed. The `Remote corpus` workflow fetches them every night and runs both suites over
them; a download failure is reported as such, never as a test failure. The manifest itself is public, so its
titles and `textContains` strings carry no personal data, and it lists only files anyone can download.

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
  remote.json            entries for documents fetched on demand, never committed (ADR 32)
  remote/                where build/fetch_remote.py puts them; ignored by git
```

`build_corpus.py --committed-only` merges `vendor.json` and `contributed.json` into the manifest without
regenerating anything, with the referee's verdict on each file; `build_corpus.py --remote` records that
verdict in `remote.json` for each fetched remote document. `tests/corpus/README.md` gives the container
commands that make those verdicts the integration tests' own.

## Manifest

One entry per document. The manifest is the contract: tests read it, and a document nobody asserts
anything about is not part of the corpus.

```jsonc
{
  "file": "documents/invoice/chromium-invoice-fr.pdf",
  "title": "French invoice with VAT breakdown",
  "useCase": "invoice",                  // invoice | report | contract | form | scan | archival | mixed
  "producer": "Chromium 147 (Skia PDF backend)",
  "origin": "generated",                 // generated | contributed | derived | remote
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
  as published, so provenance is checkable without trusting the repository. For a remote document it is
  mandatory, and it is what `fetch_remote.py` downloads and verifies.

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

A milestone may name **remote documents** in its acceptance conditions (ADR 32). The main CI job never sees
them, so such a milestone is closed only on a green run of the `Remote corpus` workflow, recorded in
`docs/status.md` with its date.
