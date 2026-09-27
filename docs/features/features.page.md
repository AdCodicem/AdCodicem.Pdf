---
title: Features and comparison
sidebar_position: 2
---

# Features and comparison

What AdCodicem.Pdf does today, what it will do and in which milestone, and how it compares with the PDF
libraries a .NET team usually weighs against it. Every state below comes from the
[roadmap](/project/roadmap); every claim about another product comes from that product's own public
documentation, as found on {{asOf}}, and cites it.

:::warning A young library

Reading and the first validation rules exist today; everything else is planned, milestone by milestone. A
planned feature is a commitment of the roadmap, not something you can use yet. **A preview carries no
guarantee at all**: its API may change or disappear in the next preview.

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

{{features}}

## The market at a glance

The products a .NET team usually compares, and a few references from other ecosystems. Pricing models only:
prices change too often to be worth copying here.

{{glance}}

## Capabilities of .NET libraries

✅ yes · ◐ partly · ➕ through a paid or separately installed add-on · — no · ? not established from public
sources · 📅 or 🚧 planned or in progress in the milestone shown. The notes and sources behind every cell are at
the end of the page.

{{matrix}}

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
product without a source. The facts about other products were gathered on {{asOf}} and checked by a second,
independent pass; products change, and a claim that has become wrong is a bug — open an issue with the
source that shows it.

## Sources

{{sources}}
