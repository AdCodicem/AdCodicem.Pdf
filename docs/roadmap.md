# Roadmap

An index of milestones. It places a piece of work in the whole — it is **not** the daily working document:
the detailed specification of the current milestone lives in `docs/milestones/`, and the real state in
`docs/status.md`.

A milestone is not a session: the large ones span several, and a milestone closes only when its exit
criteria and its **acceptance conditions on the corpus** are verified by tests — not when the code exists.
The acceptance rules are in `docs/corpus.md`.

Indicative size: **S** ≈ one session, **M** ≈ two or three, **L** ≈ a handful, **XL** ≈ a programme of work
to be split into sub-milestones.

| # | Milestone | Size | Depends on | State |
|---|-----------|------|------------|-------|
| M0 | Repository foundations | S | — | done |
| M1 | Object model and tolerant reading | L | M0 | in progress |
| M2 | Document validation | M | M1 | to do |
| M3 | Writing and round-trip fidelity | M | M1 | to do |
| M4 | Repair | M | M2, M3 | to do |
| M5 | Pages and case-file assembly | M | M3 | to do |
| M6 | Fonts, text and content streams | L | M3 | to do |
| M7 | HTML → PDF engine | XL | M6 | to do |
| M8 | Tagged structure and accessibility | M | M7 | to do |
| M9 | Content on existing documents | M | M5, M6 | to do |
| M10 | Extraction and analysis | L | M5 | to do |
| M11 | Security and forms | L | M3 | to do |
| M12 | PDF/A-3, Factur-X and conformance profiles | L | M2, M8, M10 | to do |
| M13 | Optimisation, performance, hardening | M | M7, M10 | to do |
| M14 | Satellites: rasterisation and signing | XL | M10, M11 | to do |

Validation comes immediately after reading, because a verdict on a document needs nothing more than the
ability to read it — and because repair, conformance and every later guarantee are expressed in terms of
its findings. Repair comes immediately after writing, since producing a sound file is what repair means.

Every acceptance condition below is an executable test over `tests/corpus`. "External referee" means an
independent tool run in CI — qpdf, pikepdf, pypdf, veraPDF — used to check our claims against something
that was not written by us.

---

## M0 — Repository foundations

**Goal**: any session can build, test and publish without discovering anything.
**Deliverables**: solution and project layout, central package management, GitHub Actions CI (build, test,
benchmarks on demand, publish on tag), a `SessionStart` hook that installs the SDK, and the documentation
frame (`CLAUDE.md`, `architecture.md`, `decisions.md`, `roadmap.md`, `corpus.md`, `status.md`).
**Acceptance**: CI is green on a clean clone; a NuGet package builds.

## M1 — Object model and tolerant reading

**Goal**: open any PDF, imperfect ones included, without loading it into memory.
**Deliverables**: the COS object model; a lexer and parser over spans; Flate (with PNG and TIFF
predictors), LZW, ASCII85, ASCIIHex and RunLength filters; classic tables, cross-reference streams, object
streams, the `/Prev` chain and hybrid-reference files; lazy resolution with a bounded cache; rebuilding by
scanning when the index is wrong or absent; `PdfDiagnostics`; the corpus harness itself.

**Acceptance**
- Every document in the corpus opens, and its page count and catalogue match the manifest.
- Each `damaged` document opens with exactly the diagnostics its manifest declares — no more, no fewer.
- No document, hostile ones included, produces an untyped exception, unbounded recursion, or an allocation
  the file chose; each stays inside a per-document time budget.
- Opening a document does not read its content: on the `scan` and `stress` documents, bytes read at open
  are a small fraction of file size, verified by counting reads.
- Indexing the `stress` document holds within a stated memory budget recorded in `status.md`.

## M2 — Document validation

**Goal**: given any document the reader can open, produce a structured, machine-readable verdict on what
is wrong with it — separately from whether it could be read at all.

**Deliverables**: the `AdCodicem.Pdf.Validation` package; a rule engine (`IValidationRule`,
`ValidationProfile`, `PdfValidator`) whose findings carry a stable rule identifier, a severity, the object
they concern and a remedy hint; a `PdfValidationReport` that serialises; and the **structural profile** —
file structure, object graph integrity, page tree consistency, stream integrity, font embedding, resource
resolution, metadata coherence, annotation and destination targets.

Rule identifiers are part of the public contract from the day they ship: repair consumes them (M4),
conformance profiles extend them (M12), and callers filter on them.

**Acceptance**
- Every well-formed corpus document, from all four producers, validates with **no error-severity finding**.
  A validator that calls Chromium's or LibreOffice's output broken is a wrong validator, not a strict one.
- Each `damaged/*` document produces the findings its manifest declares, with stable rule identifiers.
- The vendored conformance fixtures discriminate: a file that is structurally sound but PDF/A-invalid
  produces **no** structural error. Conformance verdicts must not leak into the structural profile — they
  belong to M12.
- Two runs over the same document produce the same findings in the same order.
- Validating the 1000-page document holds within a stated memory budget, and does not read content it does
  not need to inspect.

## M3 — Writing and round-trip fidelity

**Goal**: rewrite what was read, byte for byte in semantic terms.
**Deliverables**: a forward-only `PdfWriter` (object numbers reserved ahead, indirect `/Length`,
compression on the fly), classic tables and cross-reference streams, object streams on write, full rewrite
and incremental update, a deterministic `/ID`, and preservation of an existing signature.

**Acceptance**
- Every corpus document survives open → save → reopen with an identical object graph, compared
  semantically rather than textually.
- An external referee opens every rewritten document without complaint.
- Saving the same document twice produces identical bytes.
- An incremental update on the signed `contract` document leaves the original bytes untouched, and the
  existing signature still covers its byte range.
- Rewriting the `stress` document holds memory flat and stays within the stated throughput budget.

## M4 — Repair

**Goal**: turn a damaged document into a sound one, and state precisely what was changed and what was lost.

**Deliverables**: `PdfRepair` in the core, driven by the reader's diagnostics and the M2 findings, with
each remedy attached to the finding that justified it: rebuild the index, recompute stream lengths, drop
or reconstruct unparseable objects, re-derive an inconsistent page tree, re-link orphaned pages, remove
references that point nowhere, normalise the trailer. Two modes: **conservative**, which changes only what
is broken and writes an incremental update, and **rebuild**, which normalises the whole file. A
`PdfRepairReport` says what was found, what was done, and what could not be saved.

Conformance remediation — embedding missing fonts, adding metadata — is **not** repair; it belongs to M12.

**Acceptance**
- Every `damaged/*` document repairs into a file that opens with no repair needed, validates with no error
  finding, and is accepted by an external referee.
- The repaired file matches the **undamaged original it was derived from**: same page count, same extracted
  text. The corpus keeps those originals precisely so this can be asserted rather than asserted about.
- Repairing a sound document in conservative mode changes nothing: byte-identical output.
- When content is genuinely gone, the report says what was lost. A silently shorter document fails the test.
- Repairing the 1000-page document holds memory bounded.

## M5 — Pages and case-file assembly

**Goal**: the first business priority — compose a case file from generated pages and third-party PDFs.
**Deliverables**: the page tree with inherited attributes; `PdfPageCollection` (insert, remove, reorder,
rotate, extract); cross-document deep copy with resource deduplication; merge preserving bookmarks, links,
annotations and attachments; a high-level assembly API.

**Acceptance**
- Merging the `contract` appendices with generated pages yields a document an external referee accepts,
  in which every internal link and bookmark still resolves to the page it named.
- The merged file is no larger than the sum of its inputs minus deduplicated resources, measured.
- Extraction, reordering and rotation preserve inherited attributes: a page taken out of a document keeps
  the media box and resources it inherited from its ancestors.
- Assembling one hundred corpus documents holds memory proportional to the largest single page.

## M6 — Fonts, text and content streams

**Goal**: write text that is correct, embedded, extractable and accessible.
**Deliverables**: a TrueType and OpenType parser (metrics, `cmap`, `hmtx`, `glyf`/`loca`, `CFF`);
subsetting; Type0/CIDFontType2 embedding with `ToUnicode`; a font registry with family resolution;
content stream operators; an embedded OFL font set.

**Acceptance**
- A generated document containing accented French text, typographic ligatures and a CJK sample extracts
  back to exactly the input text through an external extractor.
- Every font in a generated document is embedded and subsetted; an external referee confirms it, and the
  subset contains only the glyphs used.
- Advance widths match a reference renderer within a stated tolerance across the corpus fonts.

## M7 — HTML → PDF engine

**Goal**: the original promise. **XL — split into sub-milestones:**

- **M7.1** — CSS engine: tokeniser, selectors, cascade, inheritance, typed computed values, default stylesheet.
- **M7.2** — Block and inline layout, line breaking, alignment, `@page` pagination, margins, headers and footers, page counters.
- **M7.3** — Tables (automatic and fixed layout, spanning cells, repeated headers).
- **M7.4** — Flexbox and simple grid.
- **M7.5** — Images (JPEG passed through, PNG, transparency), inline SVG as vectors, borders and backgrounds.
- **M7.6** — Links, bookmarks, a table of contents with real page numbers, `@font-face`, the public API and DI integration.

**Acceptance**
- The reference business documents — invoice, multi-page report, contract — render within an agreed visual
  difference threshold against approved reference images, page by page.
- Their generated versions enter the corpus and satisfy every earlier milestone's acceptance conditions in
  turn: what we produce must be as readable as what we consume.
- Generating a one-thousand-page report holds memory constant as page count grows, measured at 10, 100 and
  1000 pages.
- Unsupported CSS never fails a render: it degrades and says so in the diagnostics.

## M8 — Tagged structure and accessibility

**Goal**: produce PDFs that are genuinely accessible, not merely labelled as such.
**Deliverables**: the full logical structure tree, marked content and the parent tree, alternative text,
language, reading order, artifacts for decorative elements, tagged tables.

**Acceptance**
- The reference documents pass PDF/UA validation by an external validator with no error.
- Reading order extracted from the structure tree matches the visual order, including in the two-column
  report and across page breaks.
- Every image carries alternative text or is marked as an artifact; no exceptions, verified by a test.

## M9 — Content on existing documents

**Goal**: act on a received PDF without regenerating it.
**Deliverables**: watermarks and stamps (text or a rendered HTML fragment), numbering, overlay and
underlay, headers and footers added after the fact, N-up and imposition, resource dictionary merging
without name collisions.

**Acceptance**
- Stamping every corpus document leaves every untouched object byte-identical, verified object by object.
- Text extracted from a stamped document is the original text plus the stamp, and nothing else.
- Stamping a PDF/A document either preserves conformance, confirmed by the validator, or reports the loss
  in the diagnostics. Silence fails the test.

## M10 — Extraction and analysis

**Goal**: read what a PDF contains.
**Deliverables**: a content stream interpreter (graphics state, text, positions); positioned glyphs with
font and size; grouping into words, lines, blocks and columns; table detection with a confidence score;
the tagged structure preferred where present; extraction of images, metadata, bookmarks and attachments.

**Acceptance**
- Text extracted from each corpus document matches the manifest expectations, including the two-column
  report, where reading order must be correct.
- A `scan` document reports that it has no extractable text rather than returning noise.
- The Factur-X invoice yields its embedded XML byte-identical to the source.
- Table detection on the invoice returns the line items with their columns, and states its confidence.
- Extracting from the `stress` document holds memory bounded and independent of document length.

## M11 — Security and forms

**Goal**: open protected documents, produce protected documents, handle forms.
**Deliverables**: RC4 40/128 and AES-128/256 decryption, encryption and permissions; AcroForms — reading,
filling, flattening, field appearances.

**Acceptance**
- Every encrypted corpus document opens with its recorded password, and its content matches the
  unencrypted twin it was derived from.
- Documents we encrypt open in an external referee with the same password and permissions.
- The `form` document round-trips: filled, saved, reopened, and the values read back are the values
  written; after flattening the values are still visible and the fields are gone.

## M12 — PDF/A-3, Factur-X and conformance profiles

**Goal**: regulatory conformance, guaranteed and checkable.
**Deliverables**: PDF/A-2b and PDF/A-3b generation (ICC profile, XMP, rendering constraints); Factur-X and
ZUGFeRD embedding and extraction; conformance actively preserved when merging; PDF/A and PDF/UA profiles for the
M2 rule engine, delivered in stages.

**Acceptance**
- Documents we generate pass veraPDF for the claimed conformance level, with no error.
- A Factur-X invoice we generate is accepted by an independent Factur-X validator, and its XML matches the
  input.
- Merging two PDF/A documents yields a document that still passes veraPDF; merging a conforming one with a
  non-conforming one reports the loss precisely.
- The conformance profiles agree with veraPDF on every corpus document; each disagreement is either
  fixed or recorded in the manifest with its reason.

## M13 — Optimisation, performance, hardening

**Goal**: deliver the frugality the library promises, with numbers.
**Deliverables**: global resource deduplication, recompression, subsetting of inherited fonts,
linearisation; a benchmark campaign with performance budgets enforced in CI; Native AOT and trimming
validation; fuzzing of the lexer and parser.

**Acceptance**
- Published budgets for throughput and allocation hold on the `stress` documents, and CI fails when a
  budget is exceeded — an allocation regression is a regression.
- Optimising a corpus document reduces its size without changing what an external referee extracts from it.
- A Native AOT executable opens, transforms and writes every corpus document.
- A fuzzing campaign over the lexer and parser, seeded with the `damaged` documents, finds no untyped
  exception, hang or unbounded allocation.

## M14 — Satellites: rasterisation and signing

**Goal**: the extensions that presuppose everything else.
**Deliverables**: `AdCodicem.Pdf.Rendering` (Skia rasterisation reusing the M10 interpreter);
`AdCodicem.Pdf.Signing` (the `IPdfSigner` abstraction, a local implementation, a path to an HSM).

**Acceptance**
- Rasterised pages of the reference documents match approved reference images within the agreed threshold.
- A signature we produce validates in an external reader, and the document still opens in every earlier
  acceptance test.
- Signing does not invalidate an existing signature on the `contract` document.

---

## Working a milestone

1. Read `CLAUDE.md`, `docs/status.md`, then `docs/milestones/<milestone>.md`.
2. Work in vertical, testable slices, never a whole horizontal layer.
3. A delivered feature is code plus tests plus an entry in the `status.md` journal.
4. What is discovered on the way and falls outside the milestone goes into the debt table, not into the code.

## Definition of done

Every milestone, without exception, closes only when all six hold:

| | Requirement |
|---|---|
| 1 | Its **exit criteria** are met |
| 2 | Its **acceptance conditions on the corpus** are green in CI, with no document skipped |
| 3 | **Unit tests** cover the behaviour, its degenerate cases and its hostile ones — xUnit v3, AwesomeAssertions, NSubstitute where an interaction is the thing being asserted |
| 4 | **Integration tests** confirm, through an independent tool running in a container, anything the milestone claims about a document: that it is valid, that it round-trips, that its text is what we say it is |
| 5 | The **documentation site** matches what now exists: the user-facing pages under `website/docs` for anything a consumer can call, and the project documents for anything a contributor needs |
| 6 | **`docs/status.md`** records the measurements rather than promising them |

Points 3 to 5 are not paperwork after the fact. An untested behaviour is a guess; a claim no independent
tool has checked is an opinion; and a feature nobody can find in the documentation does not exist for
anyone outside this repository.

## Adding a milestone

Create `docs/milestones/<number>.md` from `docs/milestones/_template.md`, add its row above, and specify
only what is decided — a distant milestone stays deliberately coarse. Its acceptance conditions, however,
are written when the milestone is written: they are what the work is for.
