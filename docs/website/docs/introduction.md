---
slug: /
title: Introduction
sidebar_position: 1
---

# AdCodicem.Pdf

A managed PDF toolkit for .NET 10: generate documents from HTML, and read, assemble and transform
existing ones — with no headless browser, no native PDF engine and no external process.

```bash
dotnet add package AdCodicem.Pdf                 # the latest stable release
dotnet add package AdCodicem.Pdf --prerelease    # the latest preview, built from main
```

This documentation follows the version selected at the top of the page: each stable line has its own,
and whenever `main` has moved past the latest release, the **Preview** button shows what is on it.

:::warning Before 1.0

The reader is built and tested, and validation is being built; writing, assembly and the HTML engine are
ahead, and any minor version may still change the API. The [roadmap](/project/roadmap) says what exists and
what does not, and [status](/project/status) says where the work actually stands today.

**A preview carries no guarantee at all.** Its API, its behavior and any of its features may change or
disappear in the next preview, without notice and without a deprecation period; compatibility promises hold
between stable releases only. If you depend on a preview, pin its exact version.

:::

:::info Disclaimer

AdCodicem.Pdf is provided **"as is"**, without warranty of any kind, express or implied. To the maximum extent
permitted by applicable law, the authors, contributors and copyright holders are not liable for any claim,
damage or loss arising from the use of the library, of this documentation or of the documents it reads,
produces or modifies. The [MIT license](https://github.com/AdCodicem/AdCodicem.Pdf/blob/main/LICENSE) is the
governing text.

Every document the library produces or modifies is yours to verify before you rely on it, file it, sign it or
send it. Conformance to PDF/A, PDF/UA or Factur-X is an aim, and the validator's report is not a
certification; check that redacted content is actually gone; the legal validity of a signature depends on
your jurisdiction and your trust services. Nothing in this project is legal, tax or compliance advice.

:::

## How this documentation is organized

It follows [Diátaxis](https://diataxis.fr/): each page is written for one need, and sits in the section for that need.

| Section | For when you want to | Start with |
|---|---|---|
| [Tutorials](/tutorials) | Learn the library by using it, one step at a time | [Open, inspect and validate a PDF](tutorials/first-steps.md) |
| [How-to guides](/guides) | Get a task done that you already have in mind | [Validate a document before accepting it](guides/validate-a-received-document.md) |
| [Reference](/reference) | Look up a code, a rule, a limit or a type while you work | [Diagnostics](reference/diagnostics.md), [Validation rules](reference/validation-rules.md), the API reference |
| [Explanation](/concepts) | Understand why the library works the way it does | [Lazy reading](concepts/lazy-reading.md) |

New to the library? The [tutorial](tutorials/first-steps.md) opens, repairs and validates a document in about fifteen
minutes, with nothing to download but the package.

## Why another PDF library

Most .NET options force a trade-off. Browser-based converters give perfect fidelity but cost hundreds of
megabytes of memory per instance and a browser to deploy. In-memory object models are simple but load an
entire document into the heap, so a 500 MB file needs 500 MB of RAM before you have done anything with it.

This library takes the middle ground business documents actually need: a fully managed engine whose memory
use follows the complexity of a page rather than the size of a file.

Indexing a thousand-page document costs **229 µs and 393 KB**. Reading every page of it afterwards costs
6.2 ms and 5.9 MB. The gap between those two numbers is the whole design: opening a document does not read
its content.

## Design commitments

- **Managed all the way down.** The core package has no dependencies at all, and stays Native AOT and
  trimming friendly.
- **Streaming by default.** Documents are read lazily and written forward-only.
- **Business documents, not the web.** Full CSS box model, tables, paged media, flexbox and simple grid,
  inline SVG. No JavaScript.
- **Conformance as a constraint.** Tagged PDF for accessibility, PDF/A-3 and Factur-X for archiving and
  e-invoicing, designed in from the start rather than bolted on.
- **Honest about damaged input.** Real files are frequently malformed; the reader repairs what it can and
  reports precisely what it did.
- **Every valid PDF is readable.** The guards against hostile files are on by default, reported when a
  document reaches one, and lifted by an option.

## Packages

One package exists today; the others are planned, and the milestone that brings each one is on the
[roadmap](/project/roadmap). Validation is part of the core package, not a satellite of its own; the
satellite brings the PDF/A and PDF/UA profiles.

| Package | Contents | Available |
|---|---|---|
| `AdCodicem.Pdf` | Object model, lazy reader, validation, streaming writer, revisions, pages, fonts, logical structure, security, extraction, redaction | Published — the object model, the reader and the first validation rules so far |
| `AdCodicem.Pdf.Tool` | The command-line tool | Planned, M06 |
| `AdCodicem.Pdf.Fonts` | The OFL font set — Liberation Sans, Serif and Mono — as WOFF2 | Planned, M08 |
| `AdCodicem.Pdf.Barcodes` | Vector barcodes and payment codes | Planned, M10 |
| `AdCodicem.Pdf.Html` | HTML parsing, CSS engine, layout, painting to PDF | Planned, M12 |
| `AdCodicem.Pdf.AspNetCore` | Dependency-injection and `IResult` integration | Planned, M12 |
| `AdCodicem.Pdf.FacturX` | Factur-X and ZUGFeRD | Planned, M14 |
| `AdCodicem.Pdf.CaseFile` | Legal case files: pieces, inventories, court-portal presets | Planned, M18 |
| `AdCodicem.Pdf.Conformance` | PDF/A and PDF/UA profiles for the validation engine | Planned, M20 |
| `AdCodicem.Pdf.Imaging` | Image decoders and lossless encoders for scans | Planned, M22 |
| `AdCodicem.Pdf.Compare` | Comparison and templates | Planned, M24 |
| `AdCodicem.Pdf.Rendering` | Rasterization | Planned, M25 |
| `AdCodicem.Pdf.Signing` | PAdES signing, long-term signatures, signature validation | Planned, M26 |
| `AdCodicem.Pdf.Docx` | DOCX to HTML | Planned, M31 |
