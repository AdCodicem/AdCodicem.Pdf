# AdCodicem.Pdf — working frame

A **.NET 10 / C# 14** NuGet library (MIT) that **generates PDF from HTML** and **manipulates existing
PDF documents**, under a standing requirement of frugal CPU and memory use.

## How to approach a session

Read these, in this order, and nothing else:

1. **this file** — the invariant frame;
2. **`docs/status.md`** — where the project stands, what is in flight, known debt;
3. **the current milestone file** in `docs/milestones/` — the detailed specification of the work.

Load `docs/architecture.md` only when touching a boundary between layers, and `docs/decisions.md` only
when considering reversing a settled choice. `docs/roadmap.md` places a milestone in the whole; it is not
a daily working document. `docs/corpus.md` is required reading before closing any milestone; `docs/corpus-contributions.md` says
what documents are still wanted and how they arrive; `docs/releasing.md` covers packaging and publishing.

At the end of every session: update `docs/status.md` (actual state, not intentions), tick the milestone
checklist, commit, push.

## What the library is — and is not

- A **fully managed** HTML → PDF engine: no Chromium, no wkhtmltopdf, no external process.
- A **reader/writer pair** able to open imperfect third-party files without loading them into memory.
- It does **not** chase browser fidelity: no JavaScript, no animation, no rendering of arbitrary web
  pages. It targets **business documents** — invoices, reports, contracts, case files.

## Settled decisions — do not relitigate

| # | Decision |
|---|----------|
| D01 | Fully managed rendering: AngleSharp (HTML5 parsing) → our own CSS engine and layout → our own PDF writer |
| D02 | Full scope: generation **and** manipulation (assembly, content, extraction, forms, security, optimisation) |
| D03 | Target content: business documents plus a chosen subset of modern CSS (flex, simple grid, SVG, paged media) |
| D04 | SkiaSharp and HarfBuzzSharp allowed — in `AdCodicem.Pdf.Html` only, never in the core |
| D05 | We write the PDF writer ourselves: full control over compression, conformance and streaming |
| D06 | Designed in from the start: headers/footers/numbering/links/bookmarks, PDF/A-3 and Factur-X, PDF/UA (tagged) |
| D07 | API: facade plus immutable options plus ASP.NET Core dependency injection |
| D08 | Packaging: a dependency-free core plus satellite packages |
| D09 | Single target `net10.0`, C# 14 |
| D10 | Fonts: explicit registry, an embedded OFL set, and CSS web fonts fetched on demand (off by default) |
| D11 | CI on GitHub Actions, published to nuget.org |
| D12 | Lazy reading; output either as a full rewrite or as an incremental update |
| D13 | A **tolerant** reader for non-conforming files, with a structured diagnostic report |
| D14 | Full text extraction: positioned glyphs → lines and paragraphs → tables, preferring the tagged structure where it exists |
| D15 | PDF → image rasterisation: a satellite package, after the foundations |
| D16 | Conformance actively preserved through manipulation, plus a built-in PDF/A and PDF/UA validator |
| D17 | Signing: space reserved in the writer (incremental update, existing signatures preserved); PAdES later |

First business priority after the foundations: **assembling case files** (generated pages plus
third-party PDFs, table of contents, bookmarks, continuous pagination).

## Architecture invariants — not negotiable

1. **The core `AdCodicem.Pdf` has no dependencies**, neither NuGet nor native, and stays compatible with
   Native AOT and trimming.
2. **Nothing loads a whole document into memory.** Objects are read lazily, output is written forward-only,
   and memory follows the heaviest page rather than the size of the file.
3. **No allocation in hot loops** — parsing, layout, writing: `Span<T>`, `ArrayPool<T>`, reused buffers.
   No LINQ, no `string.Split`, no closures, no boxing on those paths. Elsewhere, readability wins.
4. **Everything read from a third-party file is hostile.** No allocation sized by a value from the file
   without a checked bound, no unbounded recursion, no loop whose exit depends on an offset that was read.
   A malformed PDF produces a diagnostic, never a crash and never a denial of service.
5. **Anomalies go into `PdfDiagnostics`**, not into a logger and not into an exception, as long as reading
   can continue. Exceptions are for what makes the operation impossible.
6. **Determinism**: same inputs, same bytes out. The only permitted sources of variation are supplied
   explicitly by the caller (creation date, document identifier).
7. **Conformance**: PDF/A and the tagged structure are preserved, or their loss is reported explicitly.
   Never a silent break.
8. **Public type safety**: the public API is immutable by default, with no mutable static state.
   A `PdfDocument` is not thread-safe; a rendering engine is.
9. Every feature ships **with its tests**. Every optimisation ships **with its benchmark**.
10. **No milestone closes on synthetic files alone.** Each one is accepted against real documents from
    `tests/corpus`, under the rules in `docs/corpus.md`.
11. **No milestone closes without both test levels and updated documentation.** Unit tests for the
    behaviour, integration tests for anything an independent tool must confirm, and the documentation
    site brought in line with what now exists. Code without either is unfinished, not ahead of schedule.

## Development environment

Claude Code web sessions: the .NET SDK is not preinstalled and Microsoft's distribution hosts are blocked
by the network policy. The `SessionStart` hook (`.claude/scripts/setup-dotnet.sh`) installs
`dotnet-sdk-10.0` from the Ubuntu archive. nuget.org is reachable and `dotnet restore` works.
If `dotnet` is missing: `apt-get install -y --no-install-recommends dotnet-sdk-10.0`.

```bash
dotnet build AdCodicem.Pdf.slnx -c Release
dotnet test --solution AdCodicem.Pdf.slnx -c Release          # unit + integration (the latter skip without Docker)
dotnet test --project tests/AdCodicem.Pdf.Tests/AdCodicem.Pdf.Tests.csproj -c Release
dotnet run -c Release --project bench/AdCodicem.Pdf.Benchmarks -- --filter '*'

cd website && npm ci && npm run build                          # the documentation site
```

Corpus documents are produced by real generators available in the container — Chromium (Skia backend),
LibreOffice, and Python producers from pypi — then committed, so tests never depend on the network.
See `docs/corpus.md`.

## Conventions

- **Everything is written in English**: code, public API, XML documentation, project documentation,
  commit messages, diagnostics and exception messages.
- One public type per file. `sealed` by default. `internal` until an API is deliberately made public.
- PDF object model types carry the `Pdf` prefix; types internal to the HTML engine do not.
- Tests: **xUnit v3**, **AwesomeAssertions** (`value.Should().Be(…)`), **NSubstitute** for the few real
  seams, **Testcontainers** for integration. A test name states a behaviour, not a method.
- Two suites, and the difference is not speed: `tests/AdCodicem.Pdf.Tests` asserts our own behaviour;
  `tests/AdCodicem.Pdf.IntegrationTests` asserts what an independent tool says about it, running that
  tool in a container. Shared fixtures live in `tests/AdCodicem.Pdf.TestSupport`.
- Integration tests **skip** when Docker is absent rather than failing, so a sandbox without a daemon
  still gives a usable run. They are not optional in CI.
- The documentation site is `website/` (Docusaurus). It publishes the user-facing documentation *and*
  `docs/` as they are, so a project document that does not build breaks CI.
- Conventional commits (`feat:`, `fix:`, `perf:`, `docs:`, `test:`, `refactor:`, `build:`).
- Development branch: `claude/nuget-pdf-html-dotnet-msyz8z`.

## Known traps

- PDF is a format of **absolute offsets**: any write that shifts bytes invalidates the cross-reference
  table. Only `PdfWriter` knows positions; no layer above it computes an offset.
- PDF real numbers **admit no exponent notation**: format with an invariant `"0.####"`.
- A non-ASCII PDF text string must be written as **UTF-16BE with a byte order mark**, or accented text
  breaks in every reader.
- A stream `/Length` may be an **indirect reference**; that is what makes streaming output possible.
- Page attributes (`Resources`, `MediaBox`, `Rotate`) are **inherited** through the page tree: always go
  through inherited resolution, never read the page dictionary directly.
