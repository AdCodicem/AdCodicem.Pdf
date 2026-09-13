# Project status

A living file, updated **at the end of every session**. It describes the real state, not intentions.
Keep it short: summarise the journal once it passes a dozen entries — the detailed history is in git, not
here.

## At a glance

- **Current milestone**: M1 — Object model and tolerant reading (`docs/milestones/M1.md`), slices 1 to 8 written
- **Last milestone closed**: M0 — Repository foundations
- **Builds**: yes — **Tests**: 111, all green — **CI**: green
- **Branch**: `claude/nuget-pdf-html-dotnet-msyz8z`

### Current measurements (BenchmarkDotNet, ShortRun)

| Operation | Document | Time | Allocated |
|---|---|---|---|
| Indexing | synthetic, 1000 pages, ~4 MB | 229 µs | 393 KB |
| Indexing, then reading every page | synthetic, 1000 pages, ~4 MB | 6.2 ms | 5.9 MB |
| Indexing and walking the page tree | real ReportLab document, 1000 pages | — | 2.4 MB |

The gap between the first two rows is the library's promise: opening a document does not read its content.
Indexing costs roughly 200 bytes per object, whatever the objects weigh. The third row is asserted as a
budget in CI (`CorpusReadingTests`), so an allocation regression fails the build.

## Next concrete step

Close M1: fuzz the lexer and parser, seeded with the damaged corpus (T08). Then M2 — document validation
(`docs/milestones/M2.md`), whose acceptance is that no well-formed corpus document from any of the four
producers earns an error-severity finding, and that a PDF/A-invalid file earns no *structural* one.

**The milestones were renumbered** when validation and repair were inserted: validation is now M2 (right
after reading) and repair M4 (right after writing). Numbers in commits older than 2026-09-13 refer to the
previous ordering, where M2 was writing and M3 assembly.

## Journal

### 2026-09-13 — Packaging settled, and the wanted-documents specification
- Package identifiers confirmed and checked as unclaimed: `AdCodicem.Pdf` plus `.Validation`, `.Html`,
  `.AspNetCore`, `.FacturX`, `.Rendering`, `.Signing` (D23). The `AdCodicem.` prefix is to be reserved on
  nuget.org with the first publish.
- Publication switched to **trusted publishing** (D24): the release workflow exchanges a GitHub OIDC token
  for a nuget.org key valid one hour and usable once, so no long-lived secret exists. `docs/releasing.md`
  records the exact policy fields — a mismatch on the workflow file name or the environment is what breaks
  this setup, and it breaks it silently until someone reads the error.
- `docs/corpus-contributions.md` specifies the twelve document types still wanted (W01 to W12), with the
  milestone each unblocks, what makes a sample usable, an anonymisation checklist, and the difference
  between the public and private corpora.

### 2026-09-13 — Validation and repair milestones, and a defect the corpus found
- Two milestones inserted at the user's request: **M2 document validation**, right after reading, and
  **M4 repair**, right after writing — repair produces a sound file, so it needs the writer. Everything
  after them shifted by two; the roadmap now runs to M14.
- M2 is specified as a rule engine with stable finding identifiers, of which PDF/A and PDF/UA become
  profiles in M12 (D20). M4 is specified as findings-driven and conservative by default, writing an
  incremental update so signed bytes survive (D21).
- Eight conformance fixtures from the veraPDF corpus vendored under CC BY 4.0 with a NOTICE (D22), taking
  the corpus to 27 documents. `git clone` of public repositories works through the sandbox proxy, so
  public corpora need no manual help.
- **Those third-party files immediately found a real defect**: a validly compressed *empty* stream — an
  empty content stream, an empty appearance, both commonplace — decodes to zero bytes, which the Flate
  filter was reading as failure and answering with the compressed bytes plus a spurious warning. Success
  is now reported explicitly rather than inferred from the length of the output, and filter diagnostics
  carry the offset of the stream they concern, because "a Flate stream could not be decoded" with no
  location is not actionable.

### 2026-09-13 — The corpus of real documents
- 19 documents built by four real producers — Chromium (Skia backend), LibreOffice, ReportLab, qpdf —
  covering invoices, a multi-page report, a contract, an interactive form, a scanned page, a PDF/A-2b
  export, a linearised file, an object-stream rewrite, an AES-256 encrypted file, a 1000-page document,
  and five copies damaged on purpose. 2.7 MB, committed, reproducible via `tests/corpus/build`.
- Expectations in the manifest are established by an **independent tool**, never by our own reader,
  including what qpdf can still recover from each damaged file.
- `CorpusReadingTests` turns the M1 acceptance conditions into 45 executable tests: every document opens
  as described, damaged ones report their damage, well-formed ones produce no repair and no warning, page
  counts match across all four producers, opening never reads content, and indexing the 1000-page document
  holds inside a measured 4 MB budget.
- Three real defects in the expectations surfaced immediately, and were worth the exercise: a wrong
  `/Length` is only noticed when the stream is actually read (the lazy reader working as designed, so the
  test now reads everything before judging), shifting every offset breaks `startxref` itself so the whole
  index is rebuilt rather than relocated object by object, and a process-wide allocation counter is
  meaningless in a parallel suite.

### 2026-09-13 — English throughout, and corpus-based acceptance
- All project documentation rewritten in English (D19). The convention is now: everything in English, code
  and documentation alike.
- Every milestone now carries **acceptance conditions** expressed against real documents, and `docs/corpus.md`
  defines where those documents come from, how they are catalogued and what closing a milestone requires
  (D18, invariant 10).
- The corpus can be produced in-container and committed: Chromium's Skia backend, LibreOffice, and Python
  producers from pypi give genuinely different cross-reference shapes, font handling and object stream use.

### 2026-09-13 — M1, slices 1 to 7
- COS object model, tolerant lexer and parser, decoding filters, all four index shapes (classic table,
  cross-reference stream, object stream, `/Prev` chain, hybrid files), lazy resolution with a bounded
  cache, repair by scanning, structured diagnostics.
- Hardening: every allocation a file could dictate is bounded (a stream length clamped to the real file
  size, an object stream's object count clamped to what its header could hold), and cycles — references,
  `/Prev`, an object stream containing itself — all terminate.
- A file with no usable object is refused with a typed exception rather than opened empty.
- 73 tests, including a class devoted to hostile input with a per-test time budget.
- Tests build their PDFs byte by byte with exact offsets, then damage them on purpose: no network
  dependency and no binaries in the repository. Real documents come next, as the corpus.
- Tooling: the .NET 10 SDK installs from the Ubuntu archive; `dotnet test` now requires
  Microsoft.Testing.Platform (opted into via `global.json`) and the `--solution` form.

### 2026-09-12 — Framing and foundations
- Scope settled in two steps: HTML → PDF generation first, then extended to full manipulation of existing
  documents. Nineteen decisions recorded in `docs/decisions.md`.
- Documentation frame established: `CLAUDE.md` (session frame), `architecture.md`, `decisions.md`,
  `roadmap.md` (M0 to M12), `corpus.md`, `milestones/` (per-milestone specification), this file.
- Solution skeleton: core, HTML engine, ASP.NET Core integration, tests, benchmarks; `net10.0` target,
  central package management.
- Environment: the .NET SDK is absent from web sessions and Microsoft's servers are blocked by the network
  policy. Worked around through the Ubuntu archive (`dotnet-sdk-10.0`), automated by the `SessionStart` hook.

## Debt and open points

| # | Subject | Decision expected |
|---|---------|-------------------|
| T01 | `TreatWarningsAsErrors` is off while the foundations settle | Turn on when M3 closes |
| T02 | XML documentation (`CS1591`) is not enforced on the public API | Enforce when the public API freezes (M7.6) |
| ~~T03~~ | ~~The real-document corpus is not built yet~~ | Done: `tests/corpus`, 27 documents, four producers plus vendored fixtures |
| T10 | The corpus has no document from Word, Acrobat, InDesign, a real scanner or a Java stack — the producers we cannot run here | Specified as W01 to W12 in `docs/corpus-contributions.md`; waiting on documents from the field |
| T11 | The `nuget` GitHub environment and its `NUGET_USER` secret, and the nuget.org trusted publishing policy, are not configured yet | Before the first release; steps in `docs/releasing.md` |
| T04 | An OFL font set must be embedded for default rendering | During M6 |
| T05 | A public API test (a baseline of exported signatures) | Put in place at the start of M7 |
| T06 | `PdfString.ToText` reads Latin-1 rather than full PDFDocEncoding (the 32 positions 0x80-0x9F differ) | Before the first public release |
| T07 | The object cache evicts FIFO rather than LRU; names are interned through an intermediate string | M13, with measurements |
| T08 | Fuzzing of the lexer and parser is not set up | Closing M1 |
| T09 | A memory budget is now enforced in CI; a throughput budget is not | Throughput budget in M13 |
