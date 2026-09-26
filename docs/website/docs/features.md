---
title: Features and comparison
sidebar_position: 2
---

# Features and comparison

What AdCodicem.Pdf does today, what it will do and in which milestone, and how it compares with the PDF
libraries a .NET team usually weighs against it. Every state below comes from the
[roadmap](/project/roadmap); every claim about another product comes from that product's own public
documentation, as found on 2026-09-26, and cites it.

:::warning A young library

Reading and the first validation rules exist today; everything else is planned, milestone by milestone. A
planned feature is a commitment of the roadmap, not something you can use yet. **A preview carries no
guarantee at all**: its API may change or disappear in the next preview.

:::

## Where it stands out

- **Nothing but managed code.** The core package has no dependency at all, NuGet or native, and the HTML
  engine is our own: no headless browser to ship, start and patch, no native PDF engine to deploy per
  platform, no external process. Everything that needs a dependency lives in a satellite package you add
  knowingly.
- **Memory follows what you read, not the size of the file.** Opening a document reads its index and
  nothing else: indexing a thousand-page document costs 229 µs and 393 KB, and reading every page of it
  afterwards 6.2 ms and 5.9 MB (BenchmarkDotNet, `docs/status.md`). The writer only moves forward.
- **Honest about damaged input.** Real files are often malformed. The reader rebuilds what it can and
  reports every repair as a structured diagnostic; the validator gives a verdict with stable, public rule
  identifiers you can filter on.
- **Deterministic.** The same inputs give the same bytes, so a generated document can be tested like any
  other output.
- **Conformance designed in.** PDF/UA, PDF/A-3 and Factur-X are milestones of their own, and a
  manipulation that would break a conformance claim or a signature says so rather than breaking it in
  silence.
- **Made for business documents** — invoices, reports, contracts — and for the case files European
  lawyers assemble: exhibit stamps, Bates numbers, inventories of exhibits, court-portal presets.
- **MIT.** No copyleft to comply with, no revenue threshold, no per-developer or per-server licence.

## Features

### Reading and validation

| Feature | State |
|---|---|
| Opens any PDF without loading it: memory follows what is read, not the size of the file | ✅ Available |
| Opens damaged files — rebuilds a broken index, relocates misplaced objects — and reports every repair as a structured diagnostic | ✅ Available |
| Guards against hostile files, on by default, each lifted by an option so that every valid PDF stays readable | ✅ Available |
| Every standard filter and index form: Flate with predictors, LZW, ASCII85, ASCIIHex, RunLength, cross-reference and object streams, hybrid files | ✅ Available |
| Structural validation with stable, public rule identifiers, a severity, a location and a remedy for each finding | 🚧 In progress — M2 |
| Object-shape rules generated from the PDF Association's Arlington model, version-aware | 📅 Planned — M2 |
| Revision history, what each signature covers, and changes made after signing, without cryptography | 📅 Planned — M4 |
| Repair, conservative (incremental) or full rebuild, with a report of what was changed and what was lost | 📅 Planned — M5 |

### Writing

| Feature | State |
|---|---|
| Forward-only streaming writer: full rewrite or incremental update, object and cross-reference streams | 📅 Planned — M3 |
| PDF 1.7 or PDF 2.0 output, the caller's choice, the version raised and reported when a merge needs it | 📅 Planned — M3 |
| Deterministic output: the same inputs give the same bytes | 📅 Planned — M3 |
| Cancellation and progress for every long operation | 📅 Planned — M3 |

### Assembly and case files

| Feature | State |
|---|---|
| Insert, remove, reorder, rotate and extract pages, inherited attributes kept | 📅 Planned — M6 |
| Merge that keeps bookmarks, links, forms, layers, named destinations, attachments and page labels | 📅 Planned — M6 |
| Attachments and associated files, at document, page and annotation level | 📅 Planned — M6 |
| Split by bookmark, size, page count or separator page; images to pages without re-encoding | 📅 Planned — M7 |
| Watermarks, stamps, headers, footers, overlay, N-up and imposition on existing documents | 📅 Planned — M9 |
| Exhibit stamps ("Pièce n° 12") and Bates numbering, tagged as artifacts so accessible files stay so | 📅 Planned — M9 |
| Annotations with generated appearances, selective flattening, and optional content (layers) | 📅 Planned — M11 |
| Legal case files: a piece model, the inventory of exhibits, court-portal presets, e-mail to PDF | 📅 Planned — M18 |

### HTML to PDF

| Feature | State |
|---|---|
| HTML and CSS to PDF with a fully managed engine: no browser, no native PDF engine, no external process | 📅 Planned — M12 |
| Paged media: margin boxes, running headers, page groups, cross-references, tables of contents, footnotes, columns | 📅 Planned — M12 |
| Contract-grade typography: Unicode line breaking, bidirectional text, hyphenation, OpenType features, per-character font fallback | 📅 Planned — M8, M12 |
| Batch generation of thousands of documents with compiled templates and shared caches | 📅 Planned — M12 |
| Resources loaded deny-by-default: no request a template did not earn, no file outside its root | 📅 Planned — M12 |
| Font subsetting and embedding, WOFF2, an embedded OFL font set | 📅 Planned — M8 |
| Vector barcodes and payment codes: QR, Data Matrix, GS1-128, PDF417, EAN, SEPA QR, Swiss QR-bill | 📅 Planned — M10 |
| Fillable forms generated from HTML inputs | 📅 Planned — M17 |
| Word documents (DOCX) to PDF through the HTML engine | 📅 Planned — M31 |

### Accessibility, archiving and e-invoicing

| Feature | State |
|---|---|
| Tagged, accessible PDF: PDF/UA-1 | 📅 Planned — M13 |
| PDF/A-3 and Factur-X / ZUGFeRD: profile-aware embedding, EN 16931 and French rules, a typed invoice model | 📅 Planned — M14 |
| PDF/A-2 generation; PDF/A-1 to 4 and PDF/UA-1 validation profiles | 📅 Planned — M20 |
| Conversion of received documents to PDF/A, with a report of what could not be converted | 📅 Planned — M21 |
| PDF/UA-2, Well-Tagged PDF and PDF/A-4 | 📅 Planned — M28 |

### Extraction and analysis

| Feature | State |
|---|---|
| Text with positions and reading order, tables with a confidence score, images, metadata, attachments | 📅 Planned — M15 |
| Search with positions; Markdown, JSON, ALTO and hOCR export anchored to pages, for AI pipelines | 📅 Planned — M15 |
| Comparison of two versions of a document, and field extraction by template | 📅 Planned — M24 |

### Security and signatures

| Feature | State |
|---|---|
| Password encryption and decryption, RC4 to AES-256, permissions; AES-GCM read | 📅 Planned — M16 |
| AcroForms: fill, flatten, create fields; FDF, XFDF and JSON exchange; batch filling | 📅 Planned — M16 |
| True redaction, search-and-redact for personal data, and sanitisation of hidden content | 📅 Planned — M19 |
| PAdES signing (B-B, B-T), time stamps, certification, remote signing through the CSC API | 📅 Planned — M26 |
| Long-term signatures (B-LT, B-LTA) and validation of received signatures against the EU trusted lists | 📅 Planned — M27 |

### Scans, images and output

| Feature | State |
|---|---|
| Managed JBIG2, JPEG 2000 and CCITT decoding; an OCR text layer from any engine | 📅 Planned — M22 |
| Deduplication, recompression, downsampling to a target size, linearisation | 📅 Planned — M23 |
| Pages rendered to images | 📅 Planned — M25 |
| Print production: PDF/X-4, PDF/VT, CMYK and spot colours | 📅 Planned — M29 |
| Vertical writing, ruby and MathML | 📅 Planned — M30 |

### Platform

| Feature | State |
|---|---|
| A core with no dependency at all, NuGet or native, and a satellite package for everything that needs one | ✅ Available |
| Native AOT, trimming and browser WebAssembly, verified on every corpus document | 📅 Planned — M23 |
| A command-line tool, as a dotnet tool and a Native AOT binary | 📅 Planned — M6 |
| MIT licence, no revenue threshold, no per-developer fee | ✅ Available |

## The market at a glance

The products a .NET team usually compares, and a few references from other ecosystems. Pricing models only:
prices change too often to be worth copying here.

| Product | Stack | Licence | Pricing model | Runs on | HTML to PDF | Status |
|---|---|---|---|---|---|---|
| [AdCodicem.Pdf](https://github.com/AdCodicem/AdCodicem.Pdf) | .NET 10 | MIT | Free | Fully managed; no native binary, browser or external process | Own managed engine (planned, M12) | Active; previews only, before 1.0 |

## Capabilities of .NET libraries

✅ yes · ◐ partly · ➕ through a paid or separately installed add-on · — no · ? not established from public
sources · 📅 or 🚧 planned or in progress in the milestone shown. The notes and sources behind every cell are at
the end of the page.

| Capability | AdCodicem.Pdf |
|---|---|

## Performance

The [comparison benchmarks](https://github.com/AdCodicem/AdCodicem.Pdf/tree/main/benchmarks/AdCodicem.Pdf.Benchmarks.Comparison)
open five corpus documents — a one-page invoice to a thousand-page report — with AdCodicem.Pdf, PdfPig,
PDFsharp and iText, and measure the time and the memory each takes to open a document and to decode every
page. They run on demand in the `Comparison benchmarks` workflow, so that anyone can reproduce them; the
figures will be published here from its first run, cited by run number, runner, date and package versions.
When the HTML engine exists, the same benchmarks will measure it against headless Chromium.

## When to choose something else

- **You need a web page rendered exactly as Chrome renders it, scripts included.** A headless Chromium —
  through PuppeteerSharp, Playwright or Gotenberg — is the only faithful renderer of the web. This library
  targets business documents, and never runs JavaScript
  ([ADR 37](/project/adr/out-of-scope-active-content-and-pdf-to-office)).
- **You need the features today.** iText, Aspose.PDF, Syncfusion and the others ship now what is planned
  here; PDFsharp and PdfPig do so under permissive licences.
- **You need a vendor's support contract.** The commercial libraries sell one; an open-source project does
  not.
- **You need to run form scripts, render dynamic XFA, or convert PDF to Word.** None of it is planned here
  (ADR 37); Acrobat and the commercial SDKs do it.
- **You prefer to lay out documents in C# rather than in HTML.** QuestPDF and MigraDoc are built for that; a
  code-first API is only an open question on the roadmap.
- **You need a command-line tool now.** qpdf, pdfcpu and MuPDF are mature; this library's own starts with
  milestone M6.

## How this page is kept honest

It is generated from `docs/features/features.json` and `docs/features/comparison.json`. A test holds every
state to the roadmap, requires every milestone to list what it delivers, and refuses a claim about another
product without a source. The facts about other products were gathered on 2026-09-26 and checked by a second,
independent pass; products change, and a claim that has become wrong is a bug — open an issue with the
source that shows it.

## Sources


