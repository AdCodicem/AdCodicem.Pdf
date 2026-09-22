---
slug: /
title: Introduction
sidebar_position: 1
---

# AdCodicem.Pdf

A managed PDF toolkit for .NET 10: generate documents from HTML, and read, assemble and transform
existing ones — with no headless browser, no native PDF engine and no external process.

:::warning Pre-release

Only previews are on nuget.org so far, and no stable release exists yet:

```bash
dotnet add package AdCodicem.Pdf --prerelease
```

The reader is built and tested; writing, assembly and the HTML engine are ahead. The
[roadmap](/project/roadmap) says what exists and what does not, and [status](/project/status) says where
the work actually stands today.

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

## Packages

One package exists today; the others are planned, and the milestone that brings each one is on the
[roadmap](/project/roadmap).

| Package | Contents | Available |
|---|---|---|
| `AdCodicem.Pdf` | Object model, lazy reader, streaming writer, pages, fonts, logical structure | Preview — the object model and the reader |
| `AdCodicem.Pdf.Validation` | The validation rule engine and its profiles | Planned, M2 |
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
