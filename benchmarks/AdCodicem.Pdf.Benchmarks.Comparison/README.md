# Comparison benchmarks

AdCodicem.Pdf measured against other .NET PDF libraries on the same documents, so that the comparison the
documentation publishes rests on numbers anyone can reproduce rather than on claims.

## What is measured

| Benchmark | What each library does |
|---|---|
| `OpenBenchmarks` | Opens the document from memory and returns its page count |
| `ContentBenchmarks` | Opens the document and decodes the content streams of every page |

Each runs on five corpus documents, from a one-page Chromium invoice to ReportLab's thousand-page journal,
through an Acrobat form and a tagged InDesign chapter with three incremental updates (`ComparisonDocuments`).
Every library reads the same bytes from memory, so none pays for the disk.

| Library | Package | License |
|---|---|---|
| AdCodicem.Pdf | this repository | MIT |
| PdfPig | `PdfPig` | Apache-2.0 |
| PDFsharp | `PDFsharp` | MIT |
| iText | `itext` | AGPL-3.0 (or commercial) |

## What the numbers do not say

- **Like for like, and where not.** AdCodicem.Pdf, PDFsharp and iText return each page's decoded content and
  nothing more. PdfPig has no call that stops there: its pages come parsed into operations and letters, so
  its `ContentBenchmarks` row measures more work, and is labeled so.
- **Opening is where the designs differ.** AdCodicem.Pdf reads the cross-reference index and nothing else
  until asked. The `Allocated` column shows what each of the others holds by the time the page count is
  known, and is the point of the comparison as much as the time.
- **Features are not measured here.** A library that does more when it opens a document — building the
  structures it edits with, or preparing text extraction — may be doing work a caller wants.
- **Allocated memory is managed memory only**, as BenchmarkDotNet's `MemoryDiagnoser` reports it.

## HTML to PDF, later

When the HTML engine exists (M12), a third benchmark will render the same templates with AdCodicem.Pdf.Html
and with headless Chromium through PuppeteerSharp, measuring time, allocated memory and the resident memory
of the process — the comparison the library was designed to win. The slot is reserved here; nothing is
measured until there is something of ours to measure.

## Running

On demand, like every benchmark in this repository:

```bash
dotnet run -c Release --project benchmarks/AdCodicem.Pdf.Benchmarks.Comparison -- --filter '*' --memory
```

or through the `Comparison benchmarks` workflow (Actions tab → Comparison benchmarks → Run workflow), whose
results are the ones the documentation publishes, cited by run number, runner, date and package versions.
