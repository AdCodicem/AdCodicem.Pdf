# AdCodicem.Pdf

Managed PDF toolkit for .NET 10: generate PDF documents from HTML, and read, assemble and transform
existing ones — with no headless browser, no native PDF engine and no external process.

> **Status: early development.** The public API is not stable yet. See `docs/roadmap.md`.

## Why

Most .NET options force a trade-off. Browser-based converters give perfect fidelity but cost hundreds of
megabytes of RAM per instance and a browser to deploy. In-memory object models are simple but load an
entire document into the heap. AdCodicem.Pdf targets the middle ground that business documents actually
need: a fully managed engine whose memory use follows the complexity of a page, not the size of a file.

## Design goals

- **Managed all the way down.** The core package has no dependencies at all — Native AOT and trimming friendly.
- **Streaming by default.** Documents are read lazily and written forward-only; a 10 000-page report costs
  what a 10-page one costs.
- **Business documents, not the web.** Full CSS box model, tables, paged media, flexbox and simple grid,
  inline SVG. No JavaScript.
- **Conformance as a constraint, not a feature.** Tagged PDF for accessibility, PDF/A-3 and Factur-X for
  archiving and e-invoicing, designed in from the start.
- **Honest about damaged input.** Real-world PDFs are frequently malformed; the reader repairs what it can
  and reports precisely what it did.

## Packages

| Package | Contents |
|---|---|
| `AdCodicem.Pdf` | Object model, lazy reader, streaming writer, pages, fonts, logical structure |
| `AdCodicem.Pdf.Html` | HTML parsing, CSS engine, layout, painting to PDF |
| `AdCodicem.Pdf.AspNetCore` | Dependency-injection and `IResult` integration |

Further packages (validation, Factur-X, rasterisation, signing) are planned — see `docs/roadmap.md`.

## Building

Requires the .NET 10 SDK.

```bash
dotnet build AdCodicem.Pdf.slnx -c Release
dotnet test  AdCodicem.Pdf.slnx -c Release
```

## Licence

MIT. Project documentation is written in French; code, public API and this README are in English.
