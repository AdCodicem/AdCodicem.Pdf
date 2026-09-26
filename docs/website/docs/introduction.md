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

**A preview carries no guarantee at all.** Its API, its behaviour and any of its features may change or
disappear in the next preview, without notice and without a deprecation period; compatibility promises hold
between stable releases only. If you depend on a preview, pin its exact version.

:::

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
| `AdCodicem.Pdf` | Object model, lazy reader, validation, streaming writer, pages, fonts, logical structure | Published — the object model, the reader and the first validation rules so far |
| `AdCodicem.Pdf.Conformance` | PDF/A and PDF/UA profiles for the validation engine | Planned, M12 |
| `AdCodicem.Pdf.Html` | HTML parsing, CSS engine, layout, painting to PDF | Planned, M7 |
| `AdCodicem.Pdf.AspNetCore` | Dependency-injection and `IResult` integration | Planned, M7 |
| `AdCodicem.Pdf.FacturX` | Factur-X and ZUGFeRD | Planned, M12 |
| `AdCodicem.Pdf.Rendering` | Rasterisation | Planned, M14 |
| `AdCodicem.Pdf.Signing` | PAdES signing | Planned, M14 |

## Reading a document today

What the library can do right now:

```csharp
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;

using var document = PdfDocument.Open("invoice.pdf");

Console.WriteLine(document.Version);      // 1.7
Console.WriteLine(document.ObjectCount);  // how many objects the file defines
Console.WriteLine(document.WasRepaired);  // did the index have to be rebuilt?

var pages = document.Catalog.GetDictionary(PdfName.Pages);
Console.WriteLine(pages.GetInteger(PdfName.Count));

foreach (var entry in document.Diagnostics)
{
    Console.WriteLine(entry);             // what the reader noticed, and where
}
```

Nothing above reads page content. It is read when, and only when, you ask for it.
