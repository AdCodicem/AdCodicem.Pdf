# AdCodicem.Pdf

[![CI](https://github.com/AdCodicem/AdCodicem.Pdf/actions/workflows/ci.yml/badge.svg)](https://github.com/AdCodicem/AdCodicem.Pdf/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/AdCodicem.Pdf.svg)](https://www.nuget.org/packages/AdCodicem.Pdf)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/AdCodicem/AdCodicem.Pdf/badge)](https://scorecard.dev/viewer/?uri=github.com/AdCodicem/AdCodicem.Pdf)
[![Documentation](https://img.shields.io/badge/docs-adcodicem.github.io-blue)](https://adcodicem.github.io/AdCodicem.Pdf/)

Managed PDF toolkit for .NET 10: generate PDF documents from HTML, and read, assemble and transform
existing ones — with no headless browser, no native PDF engine and no external process.

**[Read the documentation](https://adcodicem.github.io/AdCodicem.Pdf/)** — concepts, guides and the
generated API reference, versioned alongside each release.

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

Every merge into `main` publishes a preview to nuget.org, so the current state of the library is always
installable:

```bash
dotnet add package AdCodicem.Pdf --prerelease
```

## Building

Requires the .NET 10 SDK.

```bash
dotnet build AdCodicem.Pdf.slnx -c Release
dotnet test  AdCodicem.Pdf.slnx -c Release
```

## Contributing

The most useful contribution is a document: this library reads files produced by software that cannot be
run here, and one real file that breaks something is worth more than a patch. See
[docs/corpus-contributions.md](docs/corpus-contributions.md), and [CONTRIBUTING.md](CONTRIBUTING.md) for
everything else.

A malformed document that crashes, hangs or exhausts memory is a security issue, not an ordinary bug —
report it privately, as [SECURITY.md](SECURITY.md) explains.

## Licence

MIT. Everything in this repository is written in English.
