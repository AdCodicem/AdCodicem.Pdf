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
planned feature is an intention of the roadmap, not a promise that it will ever ship: its milestone may be
delayed, reshaped or dropped, and until a release has shipped it, it is not something you can use. Choose the
library for what it does today. **A preview carries no guarantee at all**: its API may change or disappear in
the next preview.

:::

## Where it stands out

- **No browser, no native PDF engine, no external process.** The core package has no dependency at all,
  NuGet or native, and the HTML engine is our own, written in C#: there is no headless browser to ship, start
  and patch. It will use SkiaSharp and HarfBuzzSharp for images and text shaping; everything that needs a
  dependency lives in a satellite package you add knowingly.
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
- **MIT.** No copyleft to comply with, no revenue threshold, no per-developer or per-server license.

## Features

### Reading and validation

| Feature | State |
|---|---|
| Opens any PDF without loading it: memory follows what is read, not the size of the file | ✅ Available |
| Opens damaged files — rebuilds a broken index, relocates misplaced objects — and reports every repair as a structured diagnostic | ✅ Available |
| Guards against hostile files, on by default, each lifted by an option so that every valid PDF stays readable | ✅ Available |
| Every standard filter and index form: Flate with predictors, LZW, ASCII85, ASCIIHex, RunLength, cross-reference and object streams, hybrid files | ✅ Available |
| Structural validation with stable, public rule identifiers, a severity, a location and a remedy for each finding | 🚧 In progress — M02 |
| Object-shape rules generated from the PDF Association's Arlington model, version-aware | 📅 Planned — M02 |
| Revision history, what each signature covers, and changes made after signing, without cryptography | 📅 Planned — M04 |
| Repair, conservative (incremental) or full rebuild, with a report of what was changed and what was lost | 📅 Planned — M05 |

### Writing

| Feature | State |
|---|---|
| Forward-only streaming writer: full rewrite or incremental update, object and cross-reference streams | 📅 Planned — M03 |
| PDF 1.7 or PDF 2.0 output, the caller's choice, the version raised and reported when a merge needs it | 📅 Planned — M03 |
| Deterministic output: the same inputs give the same bytes | 📅 Planned — M03 |
| Cancellation and progress for every long operation | 📅 Planned — M03 |

### Assembly and case files

| Feature | State |
|---|---|
| Insert, remove, reorder, rotate and extract pages, inherited attributes kept | 📅 Planned — M06 |
| Merge that keeps bookmarks, links, forms, layers, named destinations, attachments and page labels | 📅 Planned — M06 |
| Attachments and associated files, at document, page and annotation level | 📅 Planned — M06 |
| Split by bookmark, size, page count or separator page; images to pages without re-encoding | 📅 Planned — M07 |
| Watermarks, stamps, headers, footers, overlay, N-up and imposition on existing documents | 📅 Planned — M09 |
| Exhibit stamps ("Pièce n° 12") and Bates numbering, tagged as artifacts so accessible files stay so | 📅 Planned — M09 |
| Annotations with generated appearances, selective flattening, and optional content (layers) | 📅 Planned — M11 |
| Legal case files: a piece model, the inventory of exhibits, court-portal presets, e-mail to PDF | 📅 Planned — M18 |

### HTML to PDF

| Feature | State |
|---|---|
| HTML and CSS to PDF with a fully managed engine: no browser, no native PDF engine, no external process | 📅 Planned — M12 |
| Paged media: margin boxes, running headers, page groups, cross-references, tables of contents, footnotes, columns | 📅 Planned — M12 |
| Contract-grade typography: Unicode line breaking, bidirectional text, hyphenation, OpenType features, per-character font fallback | 📅 Planned — M08, M12 |
| Batch generation of thousands of documents with compiled templates and shared caches | 📅 Planned — M12 |
| Resources loaded deny-by-default: no request a template did not earn, no file outside its root | 📅 Planned — M12 |
| Font subsetting and embedding, WOFF2, an embedded OFL font set | 📅 Planned — M08 |
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
| PDF/UA-2, Well-Tagged PDF and PDF/A-4: generation and validation profiles | 📅 Planned — M28 |

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
| True redaction, search-and-redact for personal data, and sanitization of hidden content | 📅 Planned — M19 |
| PAdES signing (B-B, B-T), time stamps, certification, remote signing through the CSC API | 📅 Planned — M26 |
| Long-term signatures (B-LT, B-LTA) and validation of received signatures against the EU trusted lists | 📅 Planned — M27 |

### Scans, images and output

| Feature | State |
|---|---|
| Managed JBIG2, JPEG 2000 and CCITT decoding; an OCR text layer from any engine | 📅 Planned — M22 |
| Deduplication, recompression, downsampling to a target size, linearization | 📅 Planned — M23 |
| Pages rendered to images | 📅 Planned — M25 |
| Print production: PDF/X-4, PDF/VT, CMYK and spot colors | 📅 Planned — M29 |
| Vertical writing, ruby and MathML | 📅 Planned — M30 |

### Platform

| Feature | State |
|---|---|
| A core with no dependency at all, NuGet or native, and a satellite package for everything that needs one | ✅ Available |
| Native AOT, trimming and browser WebAssembly, verified on every corpus document | 📅 Planned — M23 |
| A command-line tool, as a dotnet tool and a Native AOT binary | 📅 Planned — M06 |
| MIT license, no revenue threshold, no per-developer fee | ✅ Available |

## The market at a glance

The products a .NET team usually compares, and a few references from other ecosystems. Pricing models only:
prices change too often to be worth copying here.

| Product | Stack | License | Pricing model | Runs on | HTML to PDF | Status |
|---|---|---|---|---|---|---|
| [AdCodicem.Pdf](https://github.com/AdCodicem/AdCodicem.Pdf) | .NET 10 | MIT | Free | Fully managed; no native binary, browser or external process | Own managed engine (planned, M12) | Active; previews only, before 1.0 |
| [PDFsharp and MigraDoc](https://www.pdfsharp.com/) | .NET | MIT, or paid attribution-free license | Free (MIT); paid support plans, attribution-free license and PsX add-ons on request | Fully managed (Core build); GDI+/WPF builds Windows-only | none | Active (6.2.4 Jan 2026; 7.0 preview Mar 2026) |
| [QuestPDF](https://www.questpdf.com/) | .NET | Source-available Community or commercial | Free under revenue threshold; else perpetual per-entity license with annual update renewal | Native Skia and qpdf binaries per platform, via P/Invoke | none | Active (2026.9.1, Sep 2026) |
| [PdfPig](https://github.com/UglyToad/PdfPig) | .NET | Apache-2.0 | Free, open source | Fully managed; Skia rendering is a separate native add-on | none | Active (0.1.16, Aug 2026; pre-1.0) |
| [iText 9](https://itextpdf.com/) | .NET, Java | AGPL-3.0 or commercial | Free under AGPL; otherwise an annual subscription by volume, or OEM; some add-ons commercial only | Managed; the OCR add-on is native, rendering an external CLI | Own managed engine (add-on pdfHTML) | Active (9.7.0, Jul 2026) |
| [Aspose.PDF for .NET](https://products.aspose.com/pdf/net/) | .NET | Proprietary (Aspose EULA) | Perpetual per developer or site with a year of updates; or metered pay-per-use | Managed; System.Drawing.Common, or the .Drawing variant | Own engine (no browser) | Active (26.9.0, Sep 2026) |
| [Syncfusion PDF Library](https://www.syncfusion.com/document-sdk/net-pdf-library) | .NET | Proprietary EULA; free Community License | Per developer per year + annual document-volume tier; free Community License | Managed core; bundled Chromium for HTML; native add-ons | Bundled Blink (Chromium 147), separate packages | Active (34.2.9, Sep 2026) |
| [IronPDF](https://ironpdf.com/) | .NET | Proprietary commercial (Iron EULA) | Perpetual tiers by devs/locations/projects, 1 yr updates; monthly and enterprise options | Native CEF + PDFium/qpdf binaries, or IronPdfEngine Docker | Chromium (bundled CEF 109; CEF 131 option) | Active (2026.9.2, Sep 2026) |
| [Apryse SDK](https://apryse.com/) | .NET over a native core | Proprietary (commercial license key) | Custom quote; modular base package plus add-ons, by volume and deployment; free trial | Native PDFNetC core per platform behind a .NET wrapper | Chromium (separate HTML2PDF module) | Active (12.1.0, Aug 2026) |
| [Nutrient .NET SDK (GdPicture.NET)](https://www.nutrient.io/sdk/dotnet/) | .NET | Proprietary (GdPicture.NET 14 EULA) | Annual or multiyear subscription, quoted by component and use case; 30-day trial | Managed .NET plus native runtimes; Chrome for HTML | Chromium (external headless Chrome) | Active (14.4.9, Sep 2026) |
| [Docotic.Pdf](https://bitmiracle.com/pdf-library/) | .NET | Proprietary (commercial) | Perpetual per app, per server or unbound; unlimited developers; 1 year of updates | Managed .NET; HTML add-on drives headless Chrome | Chromium (free HtmlToPdf add-on) | Active (9.9 May 2026; dev builds Sep 2026) |
| [GemBox.Pdf](https://www.gemboxsoftware.com/pdf) | .NET | Proprietary (GemBox EULA) | Perpetual per-developer tiers, royalty-free; 1 year of updates; free mode (2 pages) | .NET plus SkiaSharp/HarfBuzz native; Chromium for HTML | Chromium (bundled GemBox.Pdf.Html packages) | Active (2026.9.116, Sep 2026) |
| [Chromium through PuppeteerSharp or Playwright](https://www.puppeteersharp.com/) | .NET client, Chromium | MIT clients; BSD-3-Clause Chromium | Free, open source | External Chromium process; Playwright adds a Node.js driver | Chromium (headless Page.printToPDF) | Active (PuppeteerSharp 25.12.0, Sep 2026) |
| [wkhtmltopdf (DinkToPdf, Rotativa)](https://wkhtmltopdf.org/) | C++ with .NET wrappers | LGPL-3.0 (.NET wrappers MIT) | Free (open source) | Native wkhtmltopdf binary or libwkhtmltox per platform | QtWebKit (WebKit of about 2012) | Archived (Jan 2023) |
| [Gotenberg](https://gotenberg.dev/) | Go service in Docker | MIT | Free (open source), sponsor-funded; no paid tier | Docker container (Chromium, LibreOffice) over HTTP | Chromium (bundled headless) | Active (8.37.0, Sep 2026) |
| [Apache PDFBox](https://pdfbox.apache.org/) | Java | Apache-2.0 | Free (open source) | JVM (Java 8+), pure Java | none | Active (3.0.8, Jul 2026) |
| [qpdf](https://qpdf.readthedocs.io/) | C++ | Apache-2.0 | Free (open source) | Native C++ library and CLI (zlib, libjpeg) | none | Active (12.4.1, Aug 2026) |
| [MuPDF and PyMuPDF](https://mupdf.com/) | C, with Python and .NET bindings | AGPL-3.0 or commercial (.NET: NC/comm.) | Free under AGPL; commercial license quoted (OEM per copy, subscription or custom) | Native C library per platform (Python, .NET bindings) | Own engine (CSS2 subset, Story API) | Active (MuPDF 1.28.5, Sep 2026) |
| [pdfcpu](https://pdfcpu.io/) | Go | Apache-2.0 | Free (open source) | Pure Go (no cgo); Go library or static CLI binary | none | Active (v0.15.0, Aug 2026) |
| [WeasyPrint](https://weasyprint.org/) | Python | BSD-3-Clause | Free (open source); optional paid professional support | CPython 3.10+ with native Pango, HarfBuzz, Fontconfig | Own engine (Python, no JavaScript) | Active (70.0, Sep 2026) |
| [Prince](https://www.princexml.com/) | Native binary | Proprietary (YesLogic EULA) | Annual site license by document type/volume; one-time server/desktop; OEM; free non-comm. | Native binary per platform, run as an external process | Own native engine (optional JavaScript) | Active (Prince 17, Sep 2026) |

## Capabilities of .NET libraries

✅ yes · ◐ partly · ➕ through a paid or separately installed add-on · — no · ? not established from public
sources · 📅 or 🚧 planned or in progress in the milestone shown. The notes and sources behind every cell are at
the end of the page.

| Capability | AdCodicem.Pdf | iText | Aspose | Syncfusion | PDFsharp | QuestPDF | PdfPig | IronPDF | Chromium |
|---|---|---|---|---|---|---|---|---|---|
| Managed core: no native binary, browser or external process | ✅ | ✅ | ✅ | ✅ | ✅ | — | ✅ | — | — |
| Native AOT, documented | 📅 M23 | ◐ | ? | ? | ◐ | ✅ | ✅ | — | ◐ |
| Opens damaged files by rebuilding the index | ✅ | ✅ | ✅ | ◐ | — | ◐ | ✅ | — | — |
| HTML and CSS to PDF | 📅 M12 | ➕ | ✅ | ✅ | — | — | — | ✅ | ✅ |
| CSS paged media beyond page size and margins | 📅 M12 | ➕ | ? | ? | — | n/a | — | ◐ | ◐ |
| Merge, split and reorder pages | 📅 M06 | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | — |
| Stamps and watermarks on existing pages | 📅 M09 | ✅ | ✅ | ✅ | ✅ | ✅ | ◐ | ✅ | — |
| Text extraction | 📅 M15 | ✅ | ✅ | ✅ | ➕ | — | ✅ | ✅ | — |
| Fill and flatten AcroForms | 📅 M16 | ✅ | ✅ | ✅ | ➕ | — | — | ✅ | — |
| Password encryption, AES-256 | 📅 M16 | ✅ | ✅ | ✅ | ✅ | ✅ | — | ✅ | — |
| Digital signatures | 📅 M26 | ✅ | ◐ | ✅ | ◐ | — | — | ◐ | — |
| Validation of received signatures | 📅 M27 | ✅ | ✅ | ✅ | — | — | — | ✅ | — |
| PDF/A generation | 📅 M14, M20 | ✅ | ✅ | ✅ | ◐ | ✅ | ✅ | ✅ | — |
| PDF/A validation of any document | 📅 M20 | ◐ | ✅ | — | — | — | — | — | — |
| Tagged, accessible PDF (PDF/UA) | 📅 M13 | ✅ | ✅ | ✅ | ✅ | ✅ | — | ✅ | ◐ |
| Factur-X and ZUGFeRD | 📅 M14 | ◐ | ✅ | ✅ | ➕ | ✅ | — | ✅ | — |
| True redaction | 📅 M19 | ➕ | ✅ | ➕ | — | — | — | ✅ | — |
| OCR of scanned pages | 📅 M22 | ➕ | ➕ | ➕ | — | — | — | ➕ | — |
| Pages rendered to images | 📅 M25 | ➕ | ✅ | ➕ | — | ◐ | ➕ | ✅ | — |
| Barcodes and QR codes | 📅 M10 | ✅ | ◐ | ✅ | ✅ | — | — | ✅ | — |

On AdCodicem.Pdf's column:

- Managed core: no native binary, browser or external process: The core has no dependency at all. The HTML engine (M12) will use SkiaSharp and HarfBuzzSharp, native libraries, for images and text shaping, and the rasterizer (M25) Skia (ADR 43).
- Native AOT, documented: The core is written for Native AOT and trimming (invariant 1); verifying it on every corpus document is M23's.
- Opens damaged files by rebuilding the index: Every repair is reported as a structured diagnostic.
- Validation of received signatures: M04, earlier and without cryptography, will report what each signature covers and what changed after it.
- OCR of scanned pages: M22 writes the text layer from any engine's output; the engine itself is the caller's (ADR 42).

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
  here; PDFsharp and PdfPig do so under permissive licenses.
- **You need a vendor's support contract.** The commercial libraries sell one; an open-source project does
  not.
- **You need to run form scripts, render dynamic XFA, or convert PDF to Word.** None of it is planned here
  (ADR 37); Acrobat and the commercial SDKs do it.
- **You prefer to lay out documents in C# rather than in HTML.** QuestPDF and MigraDoc are built for that; a
  code-first API is only an open question on the roadmap.
- **You need a command-line tool now.** qpdf, pdfcpu and MuPDF are mature; this library's own starts with
  milestone M06.

## How this page is kept honest

It is generated from `docs/features/features.json` and `docs/features/comparison.json`. A test holds every
state to the roadmap, requires every milestone to list what it delivers, and refuses a claim about another
product without a source. The facts about other products were gathered on 2026-09-26 and checked by a second,
independent pass; products change, and a claim that has become wrong is a bug — open an issue with the
source that shows it.

## Sources

**PDFsharp and MigraDoc** — Free MIT library for code-built PDFs (drawing API, MigraDoc layout) and for merging, splitting and editing PDFs. Sources: [1](https://www.nuget.org/packages/PDFsharp), [2](https://api.nuget.org/v3-flatcontainer/pdfsharp/index.json), [3](https://docs.pdfsharp.net/General/Overview/Whats-New.html), [4](https://docs.pdfsharp.net/PDFsharp/Overview/Features.html).

- Managed core: no native binary, browser or external process: ✅ Core build is pure managed C# (net8/9/10, netstandard2.0); GDI+ and WPF builds are Windows-only ([source](https://www.nuget.org/packages/PDFsharp))
- Native AOT, documented: ◐ No documented AOT support; 6.2.4 net10.0 PdfSharp.dll declares IsAotCompatible (net8.0 only IsTrimmable) ([source](https://github.com/empira/PDFsharp/pull/171))
- Opens damaged files by rebuilding the index: — Tolerates misnumbered xref entries but never rebuilds the xref; a missing startxref or bad offset throws ([source](https://github.com/empira/PDFsharp/blob/master/src/foundation/src/PDFsharp/src/PdfSharp/Pdf.IO/Parser.cs))
- HTML and CSS to PDF: — FAQ: no HTML-to-PDF converter and none planned; only the third-party HtmlRenderer.PdfSharp ([source](https://docs.pdfsharp.net/PDFsharp/Overview/FAQ.html))
- CSS paged media beyond page size and margins: — No HTML/CSS engine; MigraDoc does headers, footers, page fields and TOC through its own object model ([source](https://docs.pdfsharp.net/PDFsharp/Overview/FAQ.html))
- Merge, split and reorder pages: ✅ Modify, merge and split existing PDF files: import, insert, move and remove pages ([source](https://docs.pdfsharp.net/PDFsharp/Overview/Features.html))
- Stamps and watermarks on existing pages: ✅ XGraphics.FromPdfPage with Append/Prepend draws text or images on existing pages; vendor Watermark sample ([source](https://github.com/empira/PDFsharp-samples-1.5/blob/master/samples/core/Watermark/Program.cs))
- Text extraction: ➕ Core gives only raw content-stream characters via CLexer; PsX Text Extractor (separate license) does extraction ([source](https://www.pdfsharp.com/Offers))
- Fill and flatten AcroForms: ➕ Core AcroForm support is limited, with no flatten API; PsX Forms (separate license) creates and manages forms ([source](https://www.pdfsharp.com/Offers))
- Password encryption, AES-256: ✅ RC4 40/128, AES-128 (default) and AES-256 (PDF 2.0); passwords and permissions ([source](https://docs.pdfsharp.net/PDFsharp/Topics/PDF-Features/Encryption.html))
- Digital signatures: ◐ Since 6.2.0: adbe.pkcs7.detached CMS signatures with optional RFC 3161 timestamp; no PAdES, no DSS/LTV ([source](https://github.com/empira/PDFsharp/blob/master/src/foundation/src/PDFsharp/src/PdfSharp/Pdf.Signatures/DigitalSignatureHandler.cs))
- Validation of received signatures: — Signature API only signs (IDigitalSigner, DigitalSignatureHandler); no integrity, chain or revocation checks ([source](https://docs.pdfsharp.net/PDFsharp/Topics/PDF-Features/Signatures.html))
- PDF/A generation: ◐ 'Very early state': 6.2.4 SetPdfA writes PDF/A-1a for new tagged documents only; 7.0 preview adds PdfAManager ([source](https://docs.pdfsharp.net/PDFsharp/Topics/PDF-Features/Archiving.html))
- PDF/A validation of any document: — Docs: 'PDFsharp cannot check whether an existing file is PDF/A-conforming'; vendor uses veraPDF ([source](https://docs.pdfsharp.net/PDFsharp/Topics/PDF-Features/Archiving.html))
- Tagged, accessible PDF (PDF/UA): ✅ PDF/UA-1 via UAManager and StructureBuilder, tagging by hand around XGraphics drawing ([source](https://docs.pdfsharp.net/PDFsharp/Topics/PDF-Features/Accessibility.html))
- Factur-X and ZUGFeRD: ➕ PsX Factur-X (separate license, delivered as source): Factur-X/ZUGFeRD e-invoices ([source](https://www.pdfsharp.com/Offers))
- True redaction: — FAQ: no content-manipulation API beyond low-level CLexer; 7.0 preview only adds a Redact annotation type ([source](https://docs.pdfsharp.net/PDFsharp/Overview/FAQ.html))
- OCR of scanned pages: — Not in the feature list or the PsX add-ons; the library cannot render pages ([source](https://docs.pdfsharp.net/PDFsharp/Overview/Features.html))
- Pages rendered to images: — FAQ: 'cannot render PDF files'; the vendor points to GhostScript for page images ([source](https://docs.pdfsharp.net/PDFsharp/Overview/FAQ.html))
- Barcodes and QR codes: ✅ PdfSharp.BarCodes ships in the package: Code 3 of 9, Interleaved 2 of 5, OMR; DataMatrix is a stub; no QR ([source](https://github.com/empira/PDFsharp/blob/master/src/foundation/src/PDFsharp/src/PdfSharp.BarCodes/Drawing.BarCodes/CodeDataMatrix.cs))

**QuestPDF** — Code-first fluent C# layout of business documents to PDF, including PDF/A-2/3, PDF/UA-1 and ZUGFeRD invoices. Sources: [1](https://www.nuget.org/packages/QuestPDF/), [2](https://github.com/QuestPDF/QuestPDF/releases), [3](https://github.com/QuestPDF/QuestPDF/releases/tag/2026.9.1), [4](https://github.com/QuestPDF/QuestPDF/releases/tag/2026.7.0).

- Managed core: no native binary, browser or external process: — P/Invokes bundled native libraries per platform: a custom Skia build (m154) and qpdf (12.4.1). ([source](https://github.com/QuestPDF/QuestPDF/releases/tag/2026.9.1))
- Native AOT, documented: ✅ 2026.7.0 added full Native AOT and trimming support (IsAotCompatible on net8.0+); native Skia/qpdf still ship. ([source](https://github.com/QuestPDF/QuestPDF/releases/tag/2026.7.0))
- Opens damaged files by rebuilding the index: ◐ Undocumented: DocumentOperation runs qpdf with its default xref recovery; warnings don't throw; no report. ([source](https://github.com/QuestPDF/QuestPDF.Native.Qpdf/blob/main/manual/cli.rst))
- HTML and CSS to PDF: — Code-first C# API only; full HTML/CSS not planned, an HTML subset sits in the backlog; community add-ons only. ([source](https://github.com/QuestPDF/QuestPDF/discussions/995))
- CSS paged media beyond page size and margins: n/a No HTML/CSS input; the C# API has repeating headers/footers, page numbers, page references and TOC. ([source](https://github.com/QuestPDF/QuestPDF/discussions/995))
- Merge, split and reorder pages: ✅ DocumentOperation (qpdf) MergeFile and TakePages: ranges, any order, reverse, even/odd; split by saving selections. ([source](https://www.questpdf.com/concepts/document-operations.html))
- Stamps and watermarks on existing pages: ✅ OverlayFile/UnderlayFile put pages of another PDF over/under chosen pages (watermark example); no direct text call. ([source](https://www.questpdf.com/concepts/document-operations.html))
- Text extraction: — Reading existing PDFs, including text extraction, is only a Future item on the vendor roadmap. ([source](https://www.questpdf.com/roadmap.html))
- Fill and flatten AcroForms: — Basic AcroForm support (create fields, read values) is only a Future roadmap item; no form operations exist. ([source](https://www.questpdf.com/roadmap.html))
- Password encryption, AES-256: ✅ DocumentOperation.Encrypt with 40-, 128- or 256-bit (AES-256) keys and permission flags; Decrypt too. ([source](https://www.questpdf.com/concepts/document-operations.html))
- Digital signatures: — X.509 signing is only a Future roadmap item; no PAdES or LTV. ([source](https://www.questpdf.com/roadmap.html))
- Validation of received signatures: — Cannot inspect existing PDFs; signing and content inspection are only Future roadmap items. ([source](https://www.questpdf.com/roadmap.html))
- PDF/A generation: ✅ PDF/A-2a/2b/2u and 3a/3b/3u; PDF/A-1 is commented out of the enum; no PDF/A-4. ([source](https://github.com/QuestPDF/QuestPDF/blob/main/src/dotnet/library/QuestPDF/Infrastructure/DocumentSettings.cs))
- PDF/A validation of any document: — Docs send users to external veraPDF; built-in veraPDF validation is only a Future roadmap item. ([source](https://www.questpdf.com/roadmap.html))
- Tagged, accessible PDF (PDF/UA): ✅ PDF/UA-1 via semantic tagging, with automatic table tagging; PDF/UA-2 is on the roadmap. ([source](https://www.questpdf.com/concepts/accessibility.html))
- Factur-X and ZUGFeRD: ✅ Generate PDF/A-3b, then AddAttachment (XML) and ExtendMetadata (XMP); caller supplies both. Docs: ZUGFeRD 2.1. ([source](https://www.questpdf.com/examples/zugferd.html))
- True redaction: — The full DocumentOperation list has no redaction, and the product cannot inspect page content. ([source](https://www.questpdf.com/concepts/document-operations.html))
- OCR of scanned pages: — No OCR anywhere; reading content of existing PDFs is not supported at all. ([source](https://www.questpdf.com/concepts/document-operations.html))
- Pages rendered to images: ◐ Renders its own documents to PNG, JPEG or WEBP (and SVG) at a set DPI; cannot render existing PDFs. ([source](https://www.questpdf.com/concepts/generating-output.html))
- Barcodes and QR codes: — Not built in: docs show drawing SVG from third-party ZXing.Net, installed separately. ([source](https://www.questpdf.com/api-reference/barcodes.html))

**PdfPig** — Extracting positioned letters and words, with layout analysis and reading order, from existing PDFs in pure C#. Sources: [1](https://github.com/UglyToad/PdfPig), [2](https://github.com/UglyToad/PdfPig/wiki), [3](https://github.com/UglyToad/PdfPig/wiki/FAQ), [4](https://github.com/UglyToad/PdfPig/wiki/Forms-(AcroForms)).

- Managed core: no native binary, browser or external process: ✅ Pure C#, no native code; only the optional Skia rendering add-on is native (filter add-ons are managed). ([source](https://www.nuget.org/packages/PdfPig))
- Native AOT, documented: ✅ IsAotCompatible on net8.0+, IsTrimmable on net6.0+ (v0.1.10, #939); text exporters need unreferenced code. ([source](https://github.com/UglyToad/PdfPig/releases/tag/v0.1.10))
- Opens damaged files by rebuilding the index: ✅ Lenient parsing by default; if the xref cannot be read it brute-forces objects and xref sections. ([source](https://github.com/UglyToad/PdfPig/blob/master/src/UglyToad.PdfPig/Parser/FileStructure/XrefBruteForcer.cs))
- HTML and CSS to PDF: — The wiki lists 'Converting HTML or other document formats to PDF' under things you can't do. ([source](https://github.com/UglyToad/PdfPig/wiki))
- CSS paged media beyond page size and margins: — No HTML/CSS engine and no layout engine at all. ([source](https://github.com/UglyToad/PdfPig/wiki))
- Merge, split and reorder pages: ✅ PdfMerger merges files with per-file page selection; AddPage(document, n) copies pages to split or reorder. ([source](https://github.com/UglyToad/PdfPig/blob/master/src/UglyToad.PdfPig/Writer/PdfMerger.cs))
- Stamps and watermarks on existing pages: ◐ Can draw text, shapes and JPEG/PNG over copied pages; no stamp API or opacity; README: edits 'very limited'. ([source](https://github.com/UglyToad/PdfPig))
- Text extraction: ✅ Core purpose: positioned letters, word extractors, page segmenters, reading-order detection. ([source](https://github.com/UglyToad/PdfPig))
- Fill and flatten AcroForms: — AcroForm fields are read-only; the wiki lists populating AcroForms under things you can't do (#797). ([source](https://github.com/UglyToad/PdfPig/wiki))
- Password encryption, AES-256: — Only reads encrypted files given a password (RC4/AES); the writer has no encryption. ([source](https://github.com/UglyToad/PdfPig/wiki))
- Digital signatures: — No signing; signature fields can only be read, as AcroSignatureField. ([source](https://github.com/UglyToad/PdfPig/wiki))
- Validation of received signatures: — Signature fields are exposed as form fields only; no integrity, certificate or revocation checks. ([source](https://github.com/UglyToad/PdfPig/wiki))
- PDF/A generation: ✅ ArchiveStandard writes PDF/A-1b, 2b, 3b; the 1a/2a/3a options only add an empty StructTreeRoot. ([source](https://github.com/UglyToad/PdfPig/blob/master/src/UglyToad.PdfPig/Writer/PdfAStandard.cs))
- PDF/A validation of any document: — No validator; features cover reading, extraction and basic creation only. ([source](https://github.com/UglyToad/PdfPig/wiki))
- Tagged, accessible PDF (PDF/UA): — No tagging API; the A-level PDF/A option writes an empty structure tree plus MarkInfo. ([source](https://github.com/UglyToad/PdfPig/blob/master/src/UglyToad.PdfPig/Writer/PdfA1ARuleBuilder.cs))
- Factur-X and ZUGFeRD: — Embedded files are read-only; the writer cannot attach files, so no Factur-X despite PDF/A-3b output. ([source](https://github.com/UglyToad/PdfPig/wiki))
- True redaction: — PdfTextRemover strips text for OCR preprocessing; its docs say it 'should not be used to redact content'. ([source](https://github.com/UglyToad/PdfPig/blob/master/src/UglyToad.PdfPig/Writer/PdfTextRemover.cs))
- OCR of scanned pages: — No OCR engine; it only exports existing text to hOCR, ALTO or PageXML. ([source](https://github.com/UglyToad/PdfPig/wiki))
- Pages rendered to images: ➕ Core cannot render; PdfPig.Rendering.Skia is a separate package by co-maintainer BobLd, using native SkiaSharp. ([source](https://www.nuget.org/packages/PdfPig.Rendering.Skia/))
- Barcodes and QR codes: — No barcode or QR generation; creation covers text, paths, images and links only. ([source](https://github.com/UglyToad/PdfPig/wiki))

**iText 9** — Mature library, strongest at PAdES signing and validation, PDF/A-1 to 4, PDF/UA-1 and 2, and forms; add-ons for HTML, redaction and OCR. Sources: [1](https://www.nuget.org/packages/itext), [2](https://www.nuget.org/packages/itext.pdfsweep), [3](https://www.nuget.org/packages/itext.pdfocr.tesseract4), [4](https://www.nuget.org/packages/itext.pdfocr.onnx.abstract).

- Managed core: no native binary, browser or external process: ✅ Core ships as managed DLLs (io, kernel, layout, forms, pdfa, pdfua, sign, svg, barcodes, styledxmlparser), and pdfHTML is also managed. The exceptions are pdfOCR, which needs native OCR runtimes, and pdfRender, which is an external CLI ([source](https://www.nuget.org/packages/itext))
- Native AOT, documented: ◐ The 9.2.0 release notes claim Native AOT support for iText Core, but only in .NET MAUI (iOS/macOS), and 9.3.0 improved MAUI compatibility. The netstandard2.0/net461 assemblies carry no trimming or AOT-compatibility markings. Nothing is documented for server or console Native AOT, or for the add-ons ([source](https://kb.itextpdf.com/itext/release-itext-core-9-2-0))
- Opens damaged files by rebuilding the index: ✅ PdfReader rebuilds the xref when reading it throws (HasRebuiltXref) and fixes object offsets (HasFixedXref). StrictnessLevel controls how lenient it is ([source](https://api.itextpdf.com/iText/dotnet/9.3.0/classi_text_1_1_kernel_1_1_pdf_1_1_pdf_reader.html))
- HTML and CSS to PDF: ➕ The pdfHTML add-on (itext.pdfhtml) is 'not based on any browser engine implementation'. It can output tagged PDF, PDF/A or PDF/UA ([source](https://github.com/itext/itext-pdfhtml-dotnet))
- CSS paged media beyond page size and margins: ➕ Through pdfHTML: @page rules with margin boxes (e.g. @bottom-right) and counter(page)/counter(pages), running elements (CssRunningManager in the pdfHTML source), and target-counter/target-counters since pdfHTML 3.0.3 ([source](https://kb.itextpdf.com/itext/chapter-2-defining-styles-with-css))
- Merge, split and reorder pages: ✅ PdfMerger and CopyPagesTo merge documents. PdfSplitter splits by page numbers, size or outlines. PdfDocument.MovePage reorders pages. Tiling and N-up are done through PdfCanvas transforms ([source](https://kb.itextpdf.com/itext/chapter-6-reusing-existing-pdf-documents-net))
- Stamps and watermarks on existing pages: ✅ Text or image watermarks with transparency (PdfExtGState) are added to existing pages through PdfCanvas in stamping mode ([source](https://kb.itextpdf.com/itext/how-to-watermark-pdfs-using-text-or-images))
- Text extraction: ✅ PdfTextExtractor with location-based and simple strategies. The commercial pdfCalligraph add-on advertises extraction for writing systems with compound characters ([source](https://api.itextpdf.com/iText/dotnet/9.3.0/classi_text_1_1_kernel_1_1_pdf_1_1_canvas_1_1_parser_1_1_pdf_text_extractor.html))
- Fill and flatten AcroForms: ✅ PdfAcroForm fills AcroForm fields and flattens them fully or partially (FlattenFields, PartialFormFlattening). XFDF is supported (XfdfObjectFactory). Core can fill XFA data (XfaForm), but flattening dynamic XFA needs the commercial pdfXFA add-on ([source](https://api.itextpdf.com/iText/dotnet/9.3.0/classi_text_1_1_forms_1_1_pdf_acro_form.html))
- Password encryption, AES-256: ✅ Password and certificate encryption through WriterProperties, from RC4 up to AES-256. AES-GCM (ISO/TS 32003) and MAC integrity protection (ISO/TS 32004) since 9.0 ([source](https://api.itextpdf.com/iText/dotnet/9.3.0/classi_text_1_1_kernel_1_1_pdf_1_1_writer_properties.html))
- Digital signatures: ✅ PdfPadesSigner covers PAdES B-B, B-T, B-LT and B-LTA, and ProlongSignatures adds LTV data. It needs the BouncyCastle adapter package ([source](https://api.itextpdf.com/iText/dotnet/9.3.0/classi_text_1_1_signatures_1_1_pdf_pades_signer.html))
- Validation of received signatures: ✅ SignatureValidator arrived in 8.0.5 as experimental and was finalized in 9.0. It checks integrity, the certificate chain up to trust anchors, OCSP/CRL revocation, timestamps and document revisions (MDP). EU trusted lists (LOTL) arrived in 9.3 and non-EU lists in 9.6 ([source](https://api.itextpdf.com/iText/dotnet/9.3.0/namespacei_text_1_1_signatures_1_1_validation.html))
- PDF/A generation: ✅ PDF/A-1a/1b, 2a/2b/2u, 3a/3b/3u, 4, 4e and 4f, through PdfADocument ([source](https://api.itextpdf.com/iText/dotnet/9.3.0/classi_text_1_1_kernel_1_1_pdf_1_1_pdf_a_conformance.html))
- PDF/A validation of any document: ◐ Conformance is enforced only on documents written through PdfADocument (created or stamped), which throws PdfAConformanceException on a violation. The vendor says client code is still responsible for full compliance. The docs describe no standalone validator that reports on arbitrary files, including the 9.6 conformance-checking rework ([source](https://api.itextpdf.com/iText/dotnet/9.3.0/classi_text_1_1_pdfa_1_1_pdf_a_document.html))
- Tagged, accessible PDF (PDF/UA): ✅ PDF/UA-1 and PDF/UA-2, with automated conformance checks at creation (UA-1 since 8.0.4, UA-2 since 9.2). WellTaggedPdfDocument (WTPDF) and color-contrast checks were added in 9.6 ([source](https://kb.itextpdf.com/itext/release-itext-core-9-2-0))
- Factur-X and ZUGFeRD: ◐ Core gives PDF/A-3 with associated files, but the developer must embed factur-x.xml and add the ZUGFeRD XMP schema by hand, as the vendor article shows. There is no current dedicated add-on: the old pdfInvoice add-on covered ZUGFeRD 1.0 and was last released in 2018, for iText 7.1 ([source](https://itextpdf.com/blog/technical-notes/creating-zugferd-itext))
- True redaction: ➕ The pdfSweep add-on (AGPL or commercial) removes the underlying text, image and vector content in a region. Regex-based cleanup is also available (RegexBasedCleanupStrategy) ([source](https://itextpdf.com/products/pdf-redaction-pdfsweep))
- OCR of scanned pages: ➕ The pdfOCR add-on (AGPL or commercial) runs Tesseract 4 (itext.pdfocr.tesseract4: a library that targets net461 only, or an external executable) or ONNX models (PaddleOCR, EasyOCR, docTR) on ONNX Runtime. Output is PDF or PDF/A-3u ([source](https://kb.itextpdf.com/itext/installing-itext-pdfocr-for-net-developers))
- Pages rendered to images: ➕ The pdfRender add-on is commercial only. There is no native C# version: from .NET it is called as a CLI executable in an external process ([source](https://kb.itextpdf.com/itext/how-do-i-use-pdfrender-in-a-net-environment))
- Barcodes and QR codes: ✅ itext.barcodes ships in the core package: Code 128, EAN/UPC, Code 39, Codabar, Interleaved 2 of 5, MSI, POSTNET, PDF417, DataMatrix and QR (the QR encoder is based on zxing code) ([source](https://api.itextpdf.com/iText/dotnet/9.3.0/namespacei_text_1_1_barcodes.html))

**Aspose.PDF for .NET** — Broad commercial SDK: creation, conversion to and from Office formats, forms, signatures, redaction, PDF/A, PDF/UA and ZUGFeRD. Sources: [1](https://www.nuget.org/packages/Aspose.PDF), [2](https://www.nuget.org/packages/Aspose.PDF.Drawing), [3](https://releases.aspose.com/pdf/net/), [4](https://releases.aspose.com/pdf/net/release-notes/2026/).

- Managed core: no native binary, browser or external process: ✅ Managed assemblies only (no native runtimes in the NuGet packages), no browser or external process. On non-Windows .NET 8+ use the Aspose.PDF.Drawing variant, because the main package relies on System.Drawing.Common. Optional OCR brings in Aspose.OCR, which uses native ONNX Runtime ([source](https://docs.aspose.com/pdf/net/drawing/))
- Native AOT, documented: ? The system requirements, release notes and docs say nothing about Native AOT or trimming. In a 2023 vendor-forum thread, a user found that PublishTrimmed broke Aspose.PDF 22.12 with a TypeLoadException until trimming was turned off. No vendor statement for Aspose.PDF was found (a 2023 'unsupported' answer was about Aspose.Cells) ([source](https://forum.aspose.com/t/aspose-pdf-library-throwing-unhandled-exception-system-typeloadexception-cannot-load-type-system-void-mscorlib-while-running-in-docker-container/260545))
- Opens damaged files by rebuilding the index: ✅ Document.Repair(RepairOptions); PdfFileSanitization.RebuildXrefAndTrailer added in 22.8 (PDFNET-50528 'Add method for rebuild xref table') ([source](https://releases.aspose.com/pdf/net/release-notes/2022/aspose-pdf-for-net-22-8-release-notes/))
- HTML and CSS to PDF: ✅ Built-in HTML/CSS engine via HtmlLoadOptions (print/screen media, SVG, font embedding, logical structure via CreateLogicalStructure since 25.12). No Chromium or external process ([source](https://docs.aspose.com/pdf/net/convert-html-to-pdf/))
- CSS paged media beyond page size and margins: ? CSS @page rules can override PageInfo (HtmlLoadOptions.IsPriorityCssPageRule), so page size and margins can come from CSS. Margin boxes, running headers, page counters, cross-references and footnotes are not documented for Aspose.PDF. break-inside: avoid was unsupported in 2022 (enhancement PDFNET-51824), still unresolved in July 2023 ([source](https://reference.aspose.com/pdf/net/aspose.pdf/htmlloadoptions/))
- Merge, split and reorder pages: ✅ Merge, split, move/reorder, insert, delete and extract pages through PageCollection or the PdfFileEditor facade ([source](https://docs.aspose.com/pdf/net/move-pages/))
- Stamps and watermarks on existing pages: ✅ TextStamp, ImageStamp and PdfPageStamp via Page.AddStamp on existing pages; WatermarkArtifact; PdfFileStamp facade ([source](https://docs.aspose.com/pdf/net/text-stamps-in-the-pdf-file/))
- Text extraction: ✅ TextAbsorber, TextFragmentAbsorber and ParagraphAbsorber (paragraph extraction) ([source](https://docs.aspose.com/pdf/net/extract-text-from-all-pdf/))
- Fill and flatten AcroForms: ✅ Fill AcroForm fields through document.Form; flatten with Field.Flatten, Form.Flatten or Document.Flatten. XFA fields can be filled, or the form converted to AcroForm ([source](https://reference.aspose.com/pdf/net/aspose.pdf.forms/form/flatten/))
- Password encryption, AES-256: ✅ Document.Encrypt with CryptoAlgorithm.AESx256 (also RC4-40, RC4-128 and AES-128); public-certificate encryption added in 25.7 ([source](https://reference.aspose.com/pdf/net/aspose.pdf/cryptoalgorithm/))
- Digital signatures: ◐ Creates PKCS#1, PKCS#7 and PKCS#7-detached signatures with RSA, DSA or ECDSA keys. PKCS#1 and PKCS#7 are always SHA-1; SHA-2 (and SHA-3 for ECDSA) apply only to detached signatures. Also supports external/HSM signing and TSA timestamps. PAdES and ETSI.CAdES.detached are not documented; staff said in February 2022 that Aspose.PDF 'does not meet PADES standards' (PDFNET-42596). A Signature.UseLtv flag exists. A 2024 report of non-compliant PAdES LTV signatures (PDFNET-58018) was answered in January 2025 only by adding SHA-256 for ExternalSignature. B-LT/B-LTA are not documented ([source](https://docs.aspose.com/pdf/net/digitally-sign-pdf-file/))
- Validation of received signatures: ✅ VerifySignature, plus TryVerifySignature (26.7), check integrity and compromise. ValidationOptions add certificate-chain and OCSP/CRL revocation checks (25.1/25.4). No documented LTV/DSS-based long-term validation ([source](https://docs.aspose.com/pdf/net/digitally-sign-pdf-file/))
- PDF/A generation: ✅ Converts to PDF/A-1a/1b, 2a/2b/2u, 3a/3b/3u, 4, 4e and 4f ([source](https://docs.aspose.com/pdf/net/convert-pdf-to-pdfa/))
- PDF/A validation of any document: ✅ Document.Validate(log, PdfFormat) on any document writes an XML log of problems. PDF/A-1b to PDF/A-4 conversion and validation were improved in 26.8 ([source](https://docs.aspose.com/pdf/net/convert-pdf-to-pdfa/))
- Tagged, accessible PDF (PDF/UA): ✅ PDF/UA-1 only: tagged-PDF API, auto-tagging, and validation with PdfFormat.PDF_UA_1. PdfFormat has no PDF/UA-2 member ([source](https://docs.aspose.com/pdf/net/create-tagged-pdf/))
- Factur-X and ZUGFeRD: ✅ Built in: attach factur-x.xml via FileSpecification with AFRelationship.Alternative, then convert with PdfFormat.ZUGFeRD to PDF/A-3B. You supply the invoice XML yourself ([source](https://docs.aspose.com/pdf/net/attach-zugferd/))
- True redaction: ✅ RedactionAnnotation.Redact() flattens the annotation and removes the text and images under it; HiddenDataSanitizer (25.11) removes hidden data ([source](https://reference.aspose.com/pdf/net/aspose.pdf.annotations/redactionannotation/methods/redact))
- OCR of scanned pages: ➕ OcrTextAbsorber (Aspose.Pdf.Ocr, since 26.6) returns recognized plain text. Since 26.7 it needs the separately installed Aspose.OCR NuGet package, which is based on ONNX Runtime. Alternatively, Document.Convert(CallBackGetHocr) adds hOCR text from an OCR engine you supply ([source](https://releases.aspose.com/pdf/net/release-notes/2026/aspose-pdf-for-net-26-7-release-notes/))
- Pages rendered to images: ✅ PngDevice, JpegDevice, TiffDevice, BmpDevice, GifDevice and EmfDevice; SVG via SvgSaveOptions ([source](https://docs.aspose.com/pdf/net/convert-pdf-to-images-format/))
- Barcodes and QR codes: ◐ Built in only as BarcodeField.AddBarcode, which draws a Code 128 barcode into a barcode form field and makes it read-only. For other symbologies (QR and the like), the vendor uses the separately licensed Aspose.BarCode to generate an image and insert it ([source](https://reference.aspose.com/pdf/net/aspose.pdf.forms/barcodefield/))

**Syncfusion PDF Library** — Broad commercial SDK: edit, forms, PAdES B-LTA signing and validation, PDF/A-1 to 4, Factur-X, Blink HTML Sources: [1](https://www.nuget.org/packages/Syncfusion.Pdf.Net.Core), [2](https://api.nuget.org/v3-flatcontainer/syncfusion.pdf.net.core/index.json), [3](https://www.nuget.org/packages/Syncfusion.Pdf.NET), [4](https://www.nuget.org/packages/Syncfusion.HtmlToPdfConverter.Net.Linux).

- Managed core: no native binary, browser or external process: ✅ Core package is managed; HTML needs bundled Blink; raster (PDFium), OCR (Tesseract), Imaging (SkiaSharp) are native ([source](https://www.nuget.org/packages/Syncfusion.Pdf.Net.Core))
- Native AOT, documented: ? No Native AOT or trimming statement in docs or release notes; 34.2.9 assembly has no IsAotCompatible metadata ([source](https://www.nuget.org/packages/Syncfusion.Pdf.Net.Core))
- Opens damaged files by rebuilding the index: ◐ OpenAndRepair overload fixes basic cross-reference offset errors; docs say it cannot repair complex corruption ([source](https://help.syncfusion.com/document-processing/pdf/pdf-library/net/open-pdf-file))
- HTML and CSS to PDF: ✅ Bundled Blink (Chromium 147) packages; URL, HTML string and SVG input; TOC, bookmarks, forms, headers/footers ([source](https://help.syncfusion.com/document-processing/pdf/conversions/html-to-pdf/net/features))
- CSS paged media beyond page size and margins: ? Docs cover page size, margins, print media type and API headers/footers; no @page or margin-box support documented ([source](https://help.syncfusion.com/document-processing/pdf/conversions/html-to-pdf/net/features))
- Merge, split and reorder pages: ✅ Merge, split, import, reorder, remove and rotate pages ([source](https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-pages))
- Stamps and watermarks on existing pages: ✅ Text and image watermarks drawn on existing pages, plus watermark annotations ([source](https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-watermarks))
- Text extraction: ✅ Plain and layout-preserving extraction; lines, words and glyphs with bounds, font and color ([source](https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-text-extraction))
- Fill and flatten AcroForms: ✅ Create, fill and flatten AcroForm fields; fills XFA through the AcroForm API (EnableXfaFormFill) ([source](https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-forms))
- Password encryption, AES-256: ✅ RC4 40/128, AES 128/256, and AES-GCM 256 (PDF 2.0 only); passwords and permissions ([source](https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-security))
- Digital signatures: ✅ CAdES/PAdES signatures, timestamps, LTV (DSS) and PAdES B-LTA archive timestamps; external/HSM signing ([source](https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-digitalsignature))
- Validation of received signatures: ✅ ValidateSignature: modification, certificate chain, timestamp and OCSP/CRL revocation, custom trusted list ([source](https://www.syncfusion.com/blogs/post/sign-verify-pdf-signatures-in-csharp))
- PDF/A generation: ✅ Creates PDF/A-1a/b, 2a/b/u, 3a/b/u, 4/4e/4f; ConvertToPDFA converts existing files (Imaging package on .NET Core) ([source](https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-pdf-conformance))
- PDF/A validation of any document: — Only reads the declared Conformance level; 2021 validation feature request still undelivered ([source](https://www.syncfusion.com/feedback/22470/support-for-validate-the-pdf-standard-conformance-documents))
- Tagged, accessible PDF (PDF/UA): ✅ Tagged PDF stated PDF/UA-1 compliant, plus PDF/UA-2 and WTPDF; auto-tagging (alt text still manual) ([source](https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-tagged-pdf))
- Factur-X and ZUGFeRD: ✅ ZUGFeRD 1.0, 2.0 and Factur-X profiles (to EN16931, XRechnung) on PDF/A-3b; caller supplies the XML ([source](https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-zugferd-invoice))
- True redaction: ➕ True removal of text and graphics; existing PDFs on .NET Core need Syncfusion.Pdf.Imaging.Net.Core (same license) ([source](https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-redaction))
- OCR of scanned pages: ➕ Syncfusion.PDF.OCR.Net.Core: Tesseract and Leptonica native binaries, same Document SDK license ([source](https://www.nuget.org/packages/Syncfusion.PDF.OCR.Net.Core))
- Pages rendered to images: ➕ Syncfusion.PdfToImageConverter.Net.Core: native PDFium plus SkiaSharp, same license ([source](https://www.nuget.org/packages/Syncfusion.PdfToImageConverter.Net.Core))
- Barcodes and QR codes: ✅ Built in: Code 11/32/39/93/128, Codabar, EAN-8/13, UPC, QR, Data Matrix, PDF417 ([source](https://help.syncfusion.com/document-processing/pdf/pdf-library/net/working-with-barcode))

**IronPDF** — Pixel-faithful Chromium HTML/Razor to PDF from .NET, with a broad commercial API for editing, signing, PDF/A and PDF/UA. Sources: [1](https://www.nuget.org/packages/IronPdf), [2](https://ironpdf.com/licensing/), [3](https://ironpdf.com/features/), [4](https://ironpdf.com/product-updates/changelog/).

- Managed core: no native binary, browser or external process: — Needs native packages (IronPdf.Native.Chrome.*, IronSoftware.Native.PdfModel) or the remote IronPdfEngine container ([source](https://www.nuget.org/packages/IronPdf))
- Native AOT, documented: — Vendor's .NET 8 article lists IronPDF as not Native AOT compatible because it embeds the Chromium runtime ([source](https://ironsoftware.com/suite/blog/comparison/dotnet-8-pdf-generation-library/))
- Opens damaged files by rebuilding the index: — No repair API; vendor troubleshooting says IronPDF cannot parse a corrupted PDF ([source](https://ironpdf.com/troubleshooting/ironpdf-can-not-open-parse-a-specific-pdf-file/))
- HTML and CSS to PDF: ✅ Bundled Chromium (CEF 109; CEF 131 in IronPdf.UpdatedChrome): HTML, files, URLs, Razor/CSHTML, JavaScript ([source](https://ironpdf.com/troubleshooting/updated-chrome-rendering/))
- CSS paged media beyond page size and margins: ◐ @page size/margins honored; running headers and page numbers via HtmlHeaderFooter API; margin boxes undocumented ([source](https://ironpdf.com/troubleshooting/override-css-page-rules-renderingoptions/))
- Merge, split and reorder pages: ✅ Merge, AppendPdf/PrependPdf/InsertPdf, CopyPages, RemovePages and split ([source](https://ironpdf.com/how-to/merge-or-split-pdfs/))
- Stamps and watermarks on existing pages: ✅ TextStamper, ImageStamper, HtmlStamper, BarcodeStamper and ApplyWatermark on existing pages ([source](https://ironpdf.com/how-to/stamp-text-image/))
- Text extraction: ✅ ExtractAllText and ExtractTextFromPage(s); images too; JSON export since 2025.12.2 ([source](https://ironpdf.com/how-to/extract-text-and-images/))
- Fill and flatten AcroForms: ✅ Fill AcroForm text, checkbox, combo and radio fields via Form.FindFormField; flatten with PdfDocument.Flatten ([source](https://ironpdf.com/how-to/edit-forms/))
- Password encryption, AES-256: ✅ User/owner passwords and permissions; RC4-128 by default, AES-128/AES-256 via EncryptionType since 2026.8.1 ([source](https://ironpdf.com/how-to/pdf-permissions-passwords/))
- Digital signatures: ◐ PFX/X509Certificate2 or HSM (PKCS#11), ECDSA, RFC 3161 timestamps; no documented PAdES baseline or LTV (DSS) ([source](https://ironpdf.com/how-to/signing/))
- Validation of received signatures: ✅ VerifyPdfSignatures: integrity, trust chain, revocation, algorithm strength (2026.3.1); no LTV validation documented ([source](https://ironpdf.com/product-updates/changelog/))
- PDF/A generation: ✅ SaveAsPdfA/ConvertToPdfA from HTML or existing PDFs: PDF/A-1a/1b, 2a/2b, 3a/3b, 4/4e/4f (default 3b) ([source](https://ironpdf.com/object-reference/api/IronPdf.PdfAVersions.html))
- PDF/A validation of any document: — No validation API in PdfDocument; the vendor's PDF/A tutorial says to validate with veraPDF or Acrobat Preflight ([source](https://ironpdf.com/tutorials/pdfa-archiving-csharp/))
- Tagged, accessible PDF (PDF/UA): ✅ PDF/UA-1 and PDF/UA-2 (2025.12.2); SaveAsPdfUA tags existing PDFs, RenderHtmlAsPdfUA recommended for full tagging ([source](https://ironpdf.com/how-to/pdfua/))
- Factur-X and ZUGFeRD: ✅ Since v2024.10: caller's XML embedded in PDF/A-3 with ZUGFeRD/Factur-X XMP via EmbedFileConfiguration ([source](https://ironpdf.com/how-to/pdfa/))
- True redaction: ✅ RedactTextOnAllPages removes matched text; RedactRegions* black out rectangles (image/path removal not detailed) ([source](https://ironpdf.com/how-to/redact-text/))
- OCR of scanned pages: ➕ PdfDocument.PerformOcr() requires the separately licensed IronOcr package ([source](https://ironpdf.com/object-reference/api/IronPdf.PdfDocument.html))
- Pages rendered to images: ✅ RasterizeToImageFiles and ToBitmap/PageToBitmap ([source](https://ironpdf.com/how-to/rasterize-pdf-to-images/))
- Barcodes and QR codes: ✅ Built-in BarcodeStamper for QR Code, Code128 and Code39; other symbologies need the separate IronBarcode ([source](https://ironpdf.com/object-reference/api/IronPdf.Editing.BarcodeStamper.html))

**Apryse SDK** — Broad commercial toolkit: rendering, editing, conversion, forms, signatures, PDF/A, redaction and OCR from one native core. Sources: [1](https://apryse.com/pricing), [2](https://www.nuget.org/packages/PDFTron.NET.x64), [3](https://docs.apryse.com/core/changelogs/version-12/12-0-0).

**Nutrient .NET SDK (GdPicture.NET)** — Wide .NET document-imaging suite: PDF editing, OCR, barcodes, PDF/A and PDF/UA conversion, signatures and e-invoice embedding. Sources: [1](https://www.nutrient.io/sdk/pricing/), [2](https://www.nuget.org/packages/GdPicture.API/14.4.9), [3](https://www.nutrient.io/guides/dotnet/conversion/html-to-pdf/).

**Docotic.Pdf** — Managed .NET library for editing, text extraction, forms, signatures and PDF/A, with a free Chrome-based HTML add-on. Sources: [1](https://bitmiracle.com/pdf-library/licenses/), [2](https://www.nuget.org/packages/BitMiracle.Docotic.Pdf), [3](https://bitmiracle.com/pdf-library/html-pdf/).

**GemBox.Pdf** — Simple .NET API to read, edit, sign and redact PDFs, with a free two-page mode that may be used commercially. Sources: [1](https://www.gemboxsoftware.com/pdf/pricelist), [2](https://www.nuget.org/packages/GemBox.Pdf), [3](https://www.gemboxsoftware.com/pdf/examples/c-sharp-vb-net-convert-html-to-pdf/212).

**Chromium through PuppeteerSharp or Playwright** — Browser-grade HTML, CSS and JavaScript to PDF from .NET via headless Chromium; cannot open or modify existing PDFs. Sources: [1](https://www.nuget.org/packages/PuppeteerSharp), [2](https://www.nuget.org/packages/Microsoft.Playwright), [3](https://www.nuget.org/packages/PuppeteerSharp.Cdp), [4](https://github.com/hardkoded/puppeteer-sharp/releases).

- Managed core: no native binary, browser or external process: — Drives an external Chromium process from downloaded browser binaries; Playwright .NET also runs a Node.js driver ([source](https://playwright.dev/dotnet/docs/browsers))
- Native AOT, documented: ◐ PuppeteerSharp states AOT support (PuppeteerSharp.Cdp for Chrome-only); Playwright .NET AOT issue #2714 still open ([source](https://github.com/hardkoded/puppeteer-sharp/blob/master/README.md))
- Opens damaged files by rebuilding the index: — Generates PDFs from web pages only; no API opens or rebuilds existing PDFs ([source](https://www.puppeteersharp.com/api/PuppeteerSharp.IPage.html))
- HTML and CSS to PDF: ✅ page.pdf()/PdfAsync prints the page in headless Chromium (Blink layout, JavaScript executed) ([source](https://playwright.dev/dotnet/docs/api/class-page))
- CSS paged media beyond page size and margins: ◐ @page margin boxes with page/pages counters since Chrome 131; string-set, target-counter, float: footnote unsupported ([source](https://developer.chrome.com/blog/print-margins))
- Merge, split and reorder pages: — pageRanges only selects pages of the PDF being generated; no merge/split/reorder of existing PDFs ([source](https://github.com/ChromeDevTools/devtools-protocol/blob/master/pdl/domains/Page.pdl))
- Stamps and watermarks on existing pages: — Watermarks only via HTML/CSS or header/footer templates while generating; cannot open existing PDF pages ([source](https://www.puppeteersharp.com/api/PuppeteerSharp.IPage.html))
- Text extraction: — No API reads existing PDFs; headless mode cannot navigate to a PDF document ([source](https://www.puppeteersharp.com/api/PuppeteerSharp.IPage.html))
- Fill and flatten AcroForms: — HTML form automation only; no API to fill or flatten AcroForm fields in a PDF ([source](https://www.puppeteersharp.com/api/PuppeteerSharp.IPage.html))
- Password encryption, AES-256: — Page.printToPDF has no password, permission or encryption parameter ([source](https://github.com/ChromeDevTools/devtools-protocol/blob/master/pdl/domains/Page.pdl))
- Digital signatures: — No signing parameter in Page.printToPDF and no PDF manipulation API ([source](https://github.com/ChromeDevTools/devtools-protocol/blob/master/pdl/domains/Page.pdl))
- Validation of received signatures: — Cannot open existing PDFs; headless mode cannot navigate to a PDF document ([source](https://www.puppeteersharp.com/api/PuppeteerSharp.IPage.html))
- PDF/A generation: — Page.printToPDF offers no PDF/A conformance option ([source](https://github.com/ChromeDevTools/devtools-protocol/blob/master/pdl/domains/Page.pdl))
- PDF/A validation of any document: — Generator only; cannot open or validate existing PDFs ([source](https://www.puppeteersharp.com/api/PuppeteerSharp.IPage.html))
- Tagged, accessible PDF (PDF/UA): ◐ Tagged PDF (Chrome 85+, Tagged/generateTaggedPDF option); no PDF/UA conformance claim; tags depend on the HTML ([source](https://blog.chromium.org/2020/07/using-chrome-to-generate-more.html))
- Factur-X and ZUGFeRD: — Page.printToPDF has no PDF/A-3, file attachment or XMP option ([source](https://github.com/ChromeDevTools/devtools-protocol/blob/master/pdl/domains/Page.pdl))
- True redaction: — Cannot open or modify existing PDFs ([source](https://www.puppeteersharp.com/api/PuppeteerSharp.IPage.html))
- OCR of scanned pages: — No PDF input and no OCR component ([source](https://www.puppeteersharp.com/api/PuppeteerSharp.IPage.html))
- Pages rendered to images: — Screenshots render web pages, not PDF pages; headless mode cannot navigate to a PDF document ([source](https://www.puppeteersharp.com/api/PuppeteerSharp.IPage.html))
- Barcodes and QR codes: — No barcode feature; only via third-party JS/SVG placed in the HTML ([source](https://github.com/hardkoded/puppeteer-sharp/blob/master/README.md))

**wkhtmltopdf (DinkToPdf, Rotativa)** — Long the default free HTML to PDF command-line tool; now archived, with SSRF flaw CVE-2022-35583 left unpatched. Sources: [1](https://github.com/wkhtmltopdf/wkhtmltopdf), [2](https://github.com/wkhtmltopdf/packaging), [3](https://wkhtmltopdf.org/status.html).

**Gotenberg** — Self-hosted HTTP API bundling Chromium and LibreOffice for HTML and Office to PDF, plus merge, split, PDF/A and encryption. Sources: [1](https://gotenberg.dev/), [2](https://github.com/gotenberg/gotenberg/releases), [3](https://pkg.go.dev/github.com/gotenberg/gotenberg/v8).

**Apache PDFBox** — Mature open-source Java library to read, edit, sign and render PDFs, with Preflight for PDF/A-1b validation. Sources: [1](https://pdfbox.apache.org/), [2](https://repo1.maven.org/maven2/org/apache/pdfbox/pdfbox/maven-metadata.xml).

**qpdf** — Structural PDF transformation: linearization, encryption, merge and split, repair, and JSON inspection of PDF internals. Sources: [1](https://qpdf.readthedocs.io/en/stable/release-notes.html), [2](https://qpdf.readthedocs.io/en/stable/license.html), [3](https://qpdf.readthedocs.io/en/stable/installation.html).

**MuPDF and PyMuPDF** — Fast rendering and text extraction, with a light HTML/CSS layout engine and bindings for Python and .NET. Sources: [1](https://artifex.com/licensing), [2](https://mupdf.com/releases/history), [3](https://www.nuget.org/packages/MuPDF.NET).

**pdfcpu** — Pure-Go PDF processing: validate, merge, split, encrypt, stamp and optimize, as a library or a static CLI. Sources: [1](https://github.com/pdfcpu/pdfcpu), [2](https://pkg.go.dev/github.com/pdfcpu/pdfcpu).

**WeasyPrint** — Browser-free HTML/CSS paged-media to PDF engine in Python, with PDF/A and PDF/UA output variants. Sources: [1](https://pypi.org/project/weasyprint/), [2](https://doc.courtbouillon.org/weasyprint/stable/changelog.html), [3](https://github.com/Kozea/WeasyPrint/blob/main/LICENSE).

**Prince** — High-fidelity CSS paged-media typesetting of HTML and XML, used for books, reports and business documents. Sources: [1](https://www.princexml.com/releases/17/), [2](https://www.princexml.com/purchase/), [3](https://www.princexml.com/purchase/license_faq/).
