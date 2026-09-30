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
| Generated on Windows from our own content — Word's Save as PDF, the Microsoft Print to PDF driver, the PDF24 printer (`build/build_word.ps1`) | The office desktop's writers, which no container runs | Our own fictitious content — the generated invoice today, M31's contract, letter, report, review and equations fixtures after it; nothing the script cannot account for, including what a driver stamps on its own; a DOCX kept beside its PDFs is checked for personal data in every part (`cp:lastModifiedBy`, `docProps/app.xml`, `w:docVars`) |
| Third-party documents found in public sources (`vendor/`) | Producers we will never run — Acrobat, InDesign, LiveCycle, copier firmware, a qualified-seal service — and damage from the wild | Attribution-only license (ADR 23), no real person's contact or identity data anywhere, byte-identical to the publisher's copy, at most 2 MB; source URL and SHA-256 recorded; see `docs/corpus-sources.md` |
| Contributed real documents | Everything the generators never do: legacy tooling, scanners, foreign-language typography, damaged files from the wild | No confidential content; origin and license recorded; anonymized before committing |
| Derived variants | Encryption, linearization, object-stream rewrites, and deliberate damage | Derived by a recorded, repeatable transformation from a document already in the corpus, named in the entry's `derivedFrom` from M04 on |
| Remote documents we may use but not redistribute (origin `remote`, ADR 32) | What only a bug report, a vendor's sample, a ShareAlike set or a file over 2 MB can give | Never committed, nor anything derived from them; fetched at a pinned SHA-256 and size from an immutable URL, or copied out of a pinned archive (ADR 33); tested by a separate job |

Documents are **committed**, not generated at test time: producer output changes with producer version, and
a test suite that shifts under you is worse than no test suite. Regeneration is an explicit act, reviewed
like any other change. It also keeps CI free of any network dependency.

The first exception is the **remote corpus** (ADR 32): documents whose license forbids redistribution, or that
weigh more than 2 MB. The manifest describes them like any other document, with origin `remote`
and a mandatory `source.url`, `source.sha256` and `source.bytes`; `build/fetch_remote.py` downloads them into
`remote/`, which git ignores, and refuses any file whose hash or size differs — a changed file is a new
document, reviewed as one. A document published only inside an archive is copied out of it (ADR 33): the
archive is pinned too, downloaded at most once a run, and never extracted by its members' own paths. The test suite leaves out the remote entries whose file is absent, so the main CI job fetches nothing,
finds nothing, and tests exactly what is committed. The `Remote corpus` workflow fetches them every night and runs both suites over
them; a download failure is reported as such, never as a test failure. The manifest itself is public, so its
titles and `textContains` strings carry no personal data, and it lists only files anyone can download.

The second exception is an input the test support **synthesizes** at test time because committing it would be
pointless: M23's stream that decodes past 2 GB, about 2 MB of nested Flate over zeros, made the same way on every
run. It is not a corpus document and has no manifest entry.

Keep committed documents small — a few hundred kilobytes is the aim, since everyone who clones the repository
downloads them. That is a recommendation, never a reason to turn a document down: a document over 2 MB is
not committed, whatever its license, and joins the remote corpus instead, fetched from its public URL. The
private corpus is for confidential documents, never for heavy ones. `CorpusReadingTests` fails on a
committed document over 2 MB.

## Layout

```
tests/corpus/
  manifest.json          the index: one entry per document, whoever produced it
  manifest.schema.json   the JSON schema the manifest is held to
  sources/               the inputs documents are generated from (HTML, ODT, scripts)
    third-party/         inputs written by others — stylesheets, HTML templates, test suites, ICC profiles —
                         one folder each, with its license and SOURCE
    ocr/                 OCR sidecars (hOCR, ALTO, TSV) of committed scans, with the engine's version (M22)
  build/                 the generation scripts and their recorded producer versions
  documents/             the committed PDF files, by category
  vendor/                third-party files, by source, with NOTICE and LICENSES/ for their terms
  remote/                documents fetched on demand by build/fetch_remote.py (ADR 32); ignored by git
  referee/               reports of a referee that cannot run in CI, each bound to the SHA-256 it judged (M29)
```

**Inputs written by others.** A stylesheet, an HTML template, a conformance suite or an ICC profile that someone
else wrote — Bootstrap, normalize.css, web-platform-tests, css-parsing-tests, the ICC's sRGB and CMYK profiles (M21,
M29) — is an input, not a corpus document: it lives in `sources/third-party/<name>/`, pinned to a release or a
commit, beside its license file and a `SOURCE` file giving the URL, the version and the SHA-256 of what was
retrieved, and is credited in `tests/corpus/NOTICE`. It follows the rule of vendored documents — an attribution-only
license or a public-domain dedication (ADR 23), and nothing personal —; one too large to commit, or not ours to
redistribute, is fetched at its pinned commit by the job that runs it, as the remote corpus is (ADR 32). Its
expectations come from the suite itself or from an independent tool, never from the library. A file of a format the
manifest describes — an XML invoice of KoSIT's suite, an image of PngSuite — is a corpus document instead, under
`vendor/<source>/` with its entry.

One manifest describes every document, so each is described exactly once. `build/build_corpus.py` writes
the entries of the documents it generates, marks them `"builtBy": "build_corpus.py"`, and replaces only
those; every other entry is written by hand, and the script adds nothing to it but the referee's verdict.
`build_corpus.py --committed-only` refreshes that verdict on the committed documents without regenerating
anything, and `build_corpus.py --remote` on each fetched remote document. `tests/corpus/README.md` gives the
container commands that make those verdicts the integration tests' own.

## Manifest

One entry per document. The manifest is the contract: tests read it, and a document nobody asserts
anything about is not part of the corpus.

Its shape is written down in `tests/corpus/manifest.schema.json` (JSON Schema, draft 2020-12), which the
manifest names in its `"$schema"` key, so that an editor completes, describes and checks every field while
it is typed. `CorpusManifestSchemaTests` holds the manifest — and `private.json`, where there is one — to it
in CI: every key at every level must be one the schema knows; a use case, an origin, a diagnostic code or a
rule identifier must be one that exists; a remote document must carry its pinned source and live under
`remote/<source>/`, as `fetch_remote.py` requires; no path has a `.`, `..` or empty segment; a raised reader
limit must raise its default, and replaces a skip rather than sitting beside one; every committed and remote
entry carries the referee's verdict, which only a private entry — no script writes one for it — may leave out.
The patterns end with `$(?!\n)` and name their characters, so that the editor, .NET and Python agree. The same tests hold the schema to what reads the manifest — the model in
`tests/AdCodicem.Pdf.TestSupport/Corpus.cs`, `PdfDiagnosticCodes`, `PdfValidationRuleIds` and
`PdfReaderLimits.Default` —, so the three cannot drift apart. What a schema cannot see across entries — a
file listed twice, an archive pinned two ways — stays with `fetch_remote.py` and `CorpusReadingTests`.

```jsonc
{
  "file": "documents/invoice/chromium-invoice-fr.pdf",
  "builtBy": "build_corpus.py",          // the script that writes the file, if one does
  "title": "French invoice with VAT breakdown",
  "useCase": "invoice",                  // invoice | report | contract | form | scan | archival | damaged | stress
  "producer": "Chromium 147 (Skia PDF backend)",
  "origin": "generated",                 // generated | contributed | derived | remote
  "license": "MIT (generated from our own source)",
  "features": ["xref-stream", "object-streams", "type0-subset", "utf16-metadata"],
  "expect": {
    "pages": 2,
    "clean": true,                       // no repair and no warning
    "indexRebuilt": false,
    "requiredDiagnostics": [],
    "refereeCheckSucceeds": true,        // qpdf --check's verdict, written by build_corpus.py
    "textContains": ["Facture", "TVA 20", "Total TTC"]
  }
}
```

`expect` grows as milestones land, each adding its fields and their schema in the slice that first needs them: page
count and repair status from M01, validation findings from M02, round-trip fidelity from M03, revisions and signatures
— pyHanko's coverage and modification levels — and `derivedFrom` from M04, repaired-equals-original from M05,
attachments, form fields, page labels and merge invariants from M06, the formats beyond PDF from M07 (below),
annotations and layers from M11, the PDF/UA claim beside the PDF/A one from M13, Factur-X facts from M14, extracted
text — reading order, tables, images, hidden text, textless pages — from M15, encryption from M16, detections and
disclosure findings from M19, conformance verdicts from M20 — one list of every claim a document makes, PDF/A and
PDF/UA, then PDF/X, PDF/VT and Well-Tagged PDF, each with its referee's verdict and failed clauses —, the expected
conversion outcome from M21, decoded-sample hashes, blank pages, orientation and OCR sidecars from M22, optimization
counts from M23, EU DSS's verdicts from M27, and the print referee's from M29. Each comes from an independent
referee — qpdf, pikepdf, poppler, MuPDF, veraPDF, pyHanko, EU DSS, KoSIT's validator, the PDF/X referee M29 chooses
— or from the file itself, never from the library. An expectation is never weakened to make a test pass — either the
library is fixed, or the expectation is corrected with the reason recorded in the commit message.

**Formats other than PDF.** Image files (M07), XML invoices (M14), FDF and XFDF (M16), messages (M18) and DOCX (M31)
are corpus inputs too, and one manifest describes them all (the maintainer's decision of 2026-09-27): M07 adds a
`format` field — `pdf` when absent —, file patterns keyed on it and one expectation block per format, and every PDF
acceptance test filters on it. A non-PDF file follows the provenance, license, personal-data and size rules a PDF
does, lives where a PDF of its origin would, and when remote is fetched and verified by the same `fetch_remote.py`.
What is written from a standard rather than received — M10's barcode payload set — is test data in
`tests/AdCodicem.Pdf.TestSupport`, not a corpus document.

Five fields serve that rule:

- `unsupported` — the reason the library cannot yet meet the entry's expectations, and the milestone or the
  issue that will. The acceptance tests skip the document with that reason in their output; the expectations stay as
  the independent tool established them. When the reason concerns some tests only, `unsupportedTests` names them by
  method name, `["Opening_does_not_read_the_content_of"]`, and only they skip it: every other test holds the
  document to its expectations, so that a gap in one respect keeps it out of no other.
- `readerLimits` — beside `expect`, not in it, since it is a setting chosen rather than an observation:
  the reader limits the document is opened with, when it is valid but exceeds a default one (ADR 34), such
  as `"readerLimits": { "maxDecodedStreamLength": 536870912 }`. Its keys are the properties of
  `PdfReaderLimits` in camel case, in bytes or sections, and each must raise its default; a misspelled one
  fails loading. The document is then read, not skipped, and a test run on the remote corpus checks that the
  defaults still cut it, so that a raise outlives no reason. Every other document is opened with the defaults.
- `conformanceValid` — veraPDF's verdict on the PDF/A level the document claims (`claimsConformance`),
  for M20 to agree with. M13 adds the same pair for a PDF/UA claim (`claimsUa`, `uaConformanceValid`) — a dual
  claim is two claims, not one —, and M20 replaces both pairs with one list of claims.
- `findings` — the validation rules that report on the document under the default profile (M02), by
  identifier: exactly these, no more and no fewer, and none when the field is left out. A sound document
  that earns a warning nobody declared fails as surely as a damaged one that earns nothing. Like every
  expectation it comes from the file, not from the validator: for `file.eof-missing`, a search of the
  file's last 1,024 bytes; for the rules that follow, the referee's report or the damage the document was
  made with; for the object-shape rules generated from the Arlington PDF Model, an independent walk of the model over
  the document's objects, and, for a sound document, the words of ISO 32000-1 the object breaks, read in the file
  itself — a finding the text does not support is an override, not an expectation (`docs/website/docs/reference/validation-rules.md`).
  `build_corpus.py` writes it for the documents it damages. Updating the model's pinned commit is held to the same
  lists: the corpus tests show every finding it adds or removes.
- `source` — for a third-party file, the URL it was retrieved from, the date and the SHA-256 of the bytes
  as published, so provenance is checkable without trusting the repository. For a remote document it is
  mandatory, with the document's size in `bytes`, and it is what `fetch_remote.py` downloads and verifies.
  When the document is published only inside an archive (ADR 33), `url` serves the archive and
  `source.archive` pins it — its `sha256`, its `bytes`, and the `member` to copy out —, while `sha256` and
  `bytes` still pin the document: provenance is then checked by hashing the download against the archive's
  pin, then the member against the document's. `landingPage` gives the persistent identifier when there is
  one, such as a DOI.

## Use-case categories

The corpus is organized by what the document *is*, not by which feature it exercises, so that coverage
gaps are visible in business terms.

| Category | Represents | Must eventually include |
|---|---|---|
| `invoice` | The highest-volume use case | Line items and totals, logo, accented text, a Factur-X invoice with its embedded XML, a PDF/A-3 invoice |
| `report` | Multi-page generated documents | Table of contents with real page numbers, repeated table headers, page numbering, bookmarks, charts as vector graphics |
| `contract` | Documents assembled from parts | Appendices from third-party files, an existing signature, file attachments, mixed page sizes and orientations |
| `form` | Interactive documents | AcroForm fields, filled and flattened states, field appearances |
| `scan` | Documents that are images | Image-only pages, CCITT and JPEG encodings, a mixed text-and-scan file |
| `archival` | Regulated output | PDF/A-2b and PDF/A-3b, PDF/A-4, tagged PDF/UA-1 and PDF/UA-2, a Well-Tagged PDF declaration, embedded ICC profile, XMP metadata |
| `damaged` | What the world actually sends | Missing cross-reference table, offsets off by a few bytes, truncated tail, junk before the header, several incremental updates, a lying `/Length` |
| `stress` | Scale | A document of several thousand pages, and one with a very large embedded image |

Two categories join with their first documents, each added to the schema's enum by the milestone that brings
them: `message` (M18) — e-mails as clients export them, EML and MSG, and a message printed to PDF — and `print`
(M29) — PDF/X-4 and PDF/X-4p, PDF/VT, spot colors and overprint, a statement as a print provider receives it.

## Test data beside the corpus

Some inputs are not corpus documents — nobody receives them, and no manifest entry describes them — but they are
held to the same discipline: our own or under a license as free as ADR 23 asks, nothing personal, at most 2 MB a
file, their license texts beside them and their origin in a `NOTICE` of their own directory:

- `tests/fonts/` — test faces (M08, M30);
- `tests/pki/` — the fictitious test PKI, as a Certomancer configuration (M26);
- `tests/trusted-lists/` — the pinned trusted-list snapshot, if its reuse terms and size allow (M27);
- `tests/visual/` — approved reference images, M12's and, under `rendering/`, M25's.

**Test fonts.** M08's test fonts live in `tests/fonts/`, each under the SIL Open Font License or a license as free,
with its license text beside it. A CJK face is subsetted by fontTools to the ideographs the tests use and a margin,
the command recorded; a subset of a face whose license reserves a name is renamed as the license asks. Their WOFF
and WOFF2 forms are made by fontTools and Google's `woff2_compress`, and hostile fonts by recorded mutations. A face
that cannot be committed — a license that forbids redistribution, a size past 2 MB — is fetched on demand at a
pinned SHA-256, as ADR 32 fetches documents.

## How a milestone is accepted

Each milestone file lists its acceptance conditions in the form *"these documents, this behavior,
verified by this test"*. A milestone is closed when, and only when:

1. its functional tests pass;
2. its acceptance conditions pass **on the corpus**, in CI, with no document skipped;
3. its performance budgets hold on the `stress` documents;
4. `docs/status.md` records the measurements rather than promising them.

A document that cannot be handled and is not going to be handled in this milestone is not deleted: its
expectation is recorded as unsupported (`expect.unsupported`), with the milestone that will address it. A
document that is valid but exceeds a default reader limit is not such a document: it is opened under the
limit its `readerLimits` raises, and counts as read.
Silence is not an option; a known gap must be written down where the next session will read it. A
milestone is not closed while an entry still names it as the one that will.

A milestone may name **remote documents** in its acceptance conditions (ADR 32). The main CI job never sees
them, so such a milestone is closed only on a green run of the `Remote corpus` workflow, recorded in
`docs/status.md` with its date.
