# Architecture

Where to start reading. The reasoning behind the shape is in [`docs/adr/`](docs/adr/); the detailed
design is in [`docs/architecture.md`](docs/architecture.md); the working frame every contributor should
read first is [`CLAUDE.md`](CLAUDE.md).

## Layout

```
src/
  AdCodicem.Pdf/            the core: object model, reader, writer, pages, fonts. No dependencies at all.
tests/
  AdCodicem.Pdf.Tests/              unit tests — our own behaviour
  AdCodicem.Pdf.IntegrationTests/   integration tests — what independent tools say about it, in containers
  AdCodicem.Pdf.TestSupport/        shared fixtures, the corpus manifest reader
  corpus/                           27 real documents from four producers, plus copies damaged on purpose
benchmarks/                 BenchmarkDotNet; run on demand, never on every push
samples/                    runnable examples referencing src/ directly
docs/                       project documents, and the Docusaurus site under docs/website
```

## The two paths

Reading and writing share one object model, on purpose: adding a generated page to an existing document
is then the ordinary composition of the two, not a special case.

```
HTML ─▶ parse ─▶ CSS cascade ─▶ box tree ─▶ layout ─▶ pagination ─┐
                                                                  ├─▶ object model ─▶ writer ─▶ bytes
existing PDF ─▶ lexer ─▶ xref ─▶ lazy objects ─▶ manipulation ────┘
```

Today the lower path is built: the reader indexes a file, resolves objects on demand, repairs what is
wrong and reports it. The upper path arrives with the milestones in [`docs/roadmap.md`](docs/roadmap.md).

## The three rules that shape everything else

1. **The core depends on nothing** — no NuGet package, no native library. It is what keeps it AOT-friendly
   and deployable anywhere.
2. **Nothing loads a whole document.** Memory follows the heaviest page, not the size of the file.
   Indexing a thousand-page document costs a few hundred kilobytes.
3. **Every byte from a third-party file is hostile.** No allocation sized by a value read from the file
   without a checked bound, no unbounded recursion, no loop whose exit depends on an offset. A malformed
   PDF produces a diagnostic, never a crash.

The rest — the layering, the conformance strategy, the threading model — follows from those, and is
written out in [`docs/architecture.md`](docs/architecture.md).
