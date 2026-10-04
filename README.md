<!-- The design system's GithubPdfBanner, rendered at 2x. An absolute address, so that nuget.org shows it too. -->
![AdCodicem.Pdf — .NET 10, fully managed, MIT](https://raw.githubusercontent.com/AdCodicem/AdCodicem.Pdf/main/docs/assets/readme-banner.png)

# AdCodicem.Pdf

[![CI](https://github.com/AdCodicem/AdCodicem.Pdf/actions/workflows/ci.yml/badge.svg)](https://github.com/AdCodicem/AdCodicem.Pdf/actions/workflows/ci.yml)
[![Coverage](https://codecov.io/gh/AdCodicem/AdCodicem.Pdf/branch/main/graph/badge.svg)](https://codecov.io/gh/AdCodicem/AdCodicem.Pdf)
[![NuGet](https://img.shields.io/nuget/v/AdCodicem.Pdf.svg)](https://www.nuget.org/packages/AdCodicem.Pdf)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/AdCodicem/AdCodicem.Pdf/badge)](https://scorecard.dev/viewer/?uri=github.com/AdCodicem/AdCodicem.Pdf)
[![Documentation](https://img.shields.io/badge/docs-adcodicem.github.io-blue)](https://adcodicem.github.io/AdCodicem.Pdf/)

Managed PDF toolkit for .NET 10: generate PDF documents from HTML, and read, assemble and transform
existing ones — with no headless browser, no native PDF engine and no external process.

**[Read the documentation](https://adcodicem.github.io/AdCodicem.Pdf/)** — a tutorial, how-to guides, the
reference (the generated API reference among it) and explanations, versioned alongside each release.

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

## Features

<!-- features:start -->
| What it does | State |
|---|---|
| Opens any PDF without loading it: memory follows what is read, not the size of the file | ✅ Available |
| Opens damaged files — rebuilds a broken index, relocates misplaced objects — and reports every repair as a structured diagnostic | ✅ Available |
| Structural validation with stable, public rule identifiers, a severity, a location and a remedy for each finding | 🚧 In progress — M02 |
| Merge that keeps bookmarks, links, forms, layers, named destinations, attachments and page labels | 📅 Planned — M06 |
| Exhibit stamps ("Pièce n° 12") and Bates numbering, tagged as artifacts so accessible files stay so | 📅 Planned — M09 |
| HTML and CSS to PDF with a fully managed engine: no browser, no native PDF engine, no external process | 📅 Planned — M12 |
| Tagged, accessible PDF: PDF/UA-1 | 📅 Planned — M13 |
| PDF/A-3 and Factur-X / ZUGFeRD: profile-aware embedding, EN 16931 and French rules, a typed invoice model | 📅 Planned — M14 |
| True redaction, search-and-redact for personal data, and sanitization of hidden content | 📅 Planned — M19 |
| A core with no dependency at all, NuGet or native, and a satellite package for everything that needs one | ✅ Available |
| MIT license, no revenue threshold, no per-developer fee | ✅ Available |

Every feature, planned ones included, and how the library compares with other PDF libraries: [Features and comparison](https://adcodicem.github.io/AdCodicem.Pdf/docs/features), as of 2026-09-28.
<!-- features:end -->

## Packages

One package exists today, and it holds the object model, the tolerant lazy reader and the document
validator. Everything else is planned, in the order `docs/roadmap.md` gives.

| Package | Contents | State |
|---|---|---|
| `AdCodicem.Pdf` | Object model, lazy reader, validation, streaming writer, revisions, pages, fonts, logical structure, security, extraction, redaction | Published as previews |
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

A preview of `main` is published to nuget.org every week when something that ships has changed, so the
current state of the library is always installable:

```bash
dotnet add package AdCodicem.Pdf --prerelease
```

**A preview carries no guarantee.** Its API, its behavior and any of its features may change or disappear
in the next preview, without notice. Compatibility promises hold between stable releases only; if you
depend on a preview, pin its exact version.

### Supported frameworks

Every package carries one version, which follows its own API and never the major of .NET
([ADR 48](docs/adr/0048-one-version-for-every-package-independent-of-dotnet.md)). A package targets `net10.0`, so it
installs on .NET 10 and on any later version.

| Package | Built for | Tested on | Next major |
|---|---|---|---|
| `AdCodicem.Pdf` | `net10.0` | .NET 10 | .NET 11 release candidate: the unit suite on its runtime, and the package in a trimmed `net11.0` application, on every change |

## Building

Requires the .NET 10 SDK.

```bash
dotnet build AdCodicem.Pdf.slnx -c Release
dotnet test --solution AdCodicem.Pdf.slnx -c Release
```

## Contributing

The most useful contribution is a document: this library reads files produced by software that cannot be
run here, and one real file that breaks something is worth more than a patch. See
[docs/corpus-contributions.md](docs/corpus-contributions.md), and [CONTRIBUTING.md](CONTRIBUTING.md) for
everything else.

A malformed document that crashes, hangs or exhausts memory is a security issue, not an ordinary bug —
report it privately, as [SECURITY.md](SECURITY.md) explains.

## Disclaimer

AdCodicem.Pdf is provided **"as is"**, without warranty of any kind, express or implied — including, without
limitation, the warranties of merchantability, fitness for a particular purpose and non-infringement. To the
maximum extent permitted by applicable law, the authors, contributors and copyright holders are not liable for
any claim, damage or loss, direct or indirect, arising from the use of the library, of its documentation or of
the documents it reads, produces or modifies. The [MIT license](LICENSE) is the governing text; this section
restates it in plain words and does not replace it.

The library is meant for documents that matter — contracts, invoices, case files — so what stays yours to
check is worth saying plainly:

- **Every document it produces or modifies** is yours to verify before you rely on it, file it, sign it or
  send it.
- **Conformance** — PDF/A, PDF/UA, Factur-X, EN 16931 — is an aim, and the validator reports what it finds;
  neither is a certification. A conformance claim is confirmed by the tools and the authorities your use
  requires.
- **Redaction and sanitization**: check that the content you removed is actually gone before a document leaves
  your hands.
- **Signatures**: whether a signature is legally valid depends on your jurisdiction, your certificates and your
  trust services, not on this library.
- **Nothing in this project** — code, documentation or roadmap — is legal, tax or compliance advice.

## License

MIT. The core package also carries tables derived from the PDF Association's
[Arlington PDF Model](https://github.com/pdf-association/arlington-pdf-model), under the Apache License 2.0: its
notice is in [NOTICE](NOTICE), which the package carries with the license's text. Everything in this repository is
written in English.
