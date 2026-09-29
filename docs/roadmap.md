# Roadmap

An index of milestones. It places a piece of work in the whole — it is **not** the daily working document:
the detailed specification of each milestone lives in `docs/milestones/`, and the real state in
`docs/status.md`.

> **An intention, not a promise.** This roadmap says what the project means to build and in what order. It
> is not a commitment: a milestone may be delayed, reshaped, split, reordered or dropped, and a planned
> feature may never ship. Nothing here is a contract, a delivery date or a warranty; the only features that
> exist are those a release has shipped and documented. Do not base a decision you cannot easily reverse on
> a feature that is not available yet.

A milestone is not a session: the large ones span several, and a milestone closes only when its exit
criteria and its **acceptance conditions on the corpus** are verified by tests — not when the code exists.
The acceptance rules are in `docs/corpus.md`.

Indicative size: **S** ≈ one session, **M** ≈ two or three, **L** ≈ a handful, **XL** ≈ a program of work
to be split into sub-milestones.

Milestones are **numbered in the order they are worked**, since the revision of 2026-09-26 that followed the
feature survey (`docs/research/2026-09-feature-survey.md`); the mapping from the numbers older commits and
pull requests use is at the end of this file. What the library deliberately does not do is recorded in
[ADR 37](adr/0037-out-of-scope-active-content-and-pdf-to-office.md).

| # | Milestone | Size | Depends on | State |
|---|-----------|------|------------|-------|
| M00 | Repository foundations | S | — | done |
| M01 | Object model and tolerant reading | L | M00 | done |
| M02 | Document validation | M | M01 | in progress |
| M03 | Writing and round-trip fidelity | M | M01 | to do |
| M04 | Revisions and signature coverage | M | M03 | to do |
| M05 | Repair | M | M02, M03, M04 | to do |
| M06 | Pages and case-file assembly | L | M03, M04 | to do |
| M07 | Case-file completion | M | M06 | to do |
| M08 | Fonts, text and content streams | L | M03 | to do |
| M09 | Content on existing documents | L | M06, M07, M08 | to do |
| M10 | Barcodes | M | M08, M09 | to do |
| M11 | Annotations and optional content | M | M09 | to do |
| M12 | HTML → PDF engine | XL | M06, M07, M08, M09, M10, M11 | to do |
| M13 | Tagged structure and accessibility | M | M12 | to do |
| M14 | PDF/A-3 and Factur-X | L | M12, M13 | to do |
| M15 | Extraction and analysis | L | M06, M08, M11, M14 | to do |
| M16 | Security and forms | L | M03, M11, M15 | to do |
| M17 | HTML forms | S | M12, M13, M14, M16 | to do |
| M18 | Legal case files | L | M07, M09, M12, M14, M15, M16 | to do |
| M19 | Redaction and sanitization | L | M14, M15, M16 | to do |
| M20 | PDF/A levels and conformance profiles | L | M02, M13, M14, M15 | to do |
| M21 | Converting received documents to PDF/A | L | M05, M11, M19, M20 | to do |
| M22 | Imaging and OCR | XL | M08, M15 | to do |
| M23 | Optimization, performance, hardening | L | M12, M15, M22 | to do |
| M24 | Comparison and templates | M | M14, M15, M16, M19, M22 | to do |
| M25 | Rasterization | L | M15, M22 | to do |
| M26 | Signing | L | M04, M16 | to do |
| M27 | Long-term signatures and signature validation | XL | M26 | to do |
| M28 | PDF 2.0 conformance: PDF/UA-2, WTPDF, PDF/A-4 | L | M03, M13, M17, M20 | to do |
| M29 | Print production | XL | M19, M20, M22, M23, M25 | to do |
| M30 | Advanced typesetting | L | M12, M28 | to do |
| M31 | DOCX to HTML | L | M12, M30 | to do |

A milestone's *Depends on* names the milestones whose deliverables it builds on; it need not repeat one reached
through another listed there, and the milestone's specification names every earlier milestone it uses.

Validation comes immediately after reading, because a verdict on a document needs nothing more than the
ability to read it — and because repair, conformance and every later guarantee are expressed in terms of
its findings. Repair comes after writing, since producing a sound file is what repair means, and after the
revision history, since repairing a signed file must say which revision it touched.

**The case file comes before the HTML engine.** Assembling case files is the first business priority, and
everything it needs but a rendered index — pages, merge fidelity, page labels, split, image pages, exhibit
stamps and Bates numbers — rests on the object model and the fonts, not on layout. So M06, M07, M09, M10 and
M11 come before M12, and the case-file satellite (M18) follows the HTML engine that renders its index.

**Factur-X comes right after tagging.** Receiving electronic invoices is mandatory in France since
1 September 2026, and issuing them follows in 2027; a PDF/A-3 invoice with its structured XML is the first
thing a business user asks of an HTML engine, so M14 sits right behind M12 and M13, before extraction.

Every acceptance condition below is an executable test over `tests/corpus`. "External referee" means an
independent tool run in CI — qpdf, pikepdf, pypdf, veraPDF, a Factur-X validator, EU DSS — used to check
our claims against something that was not written by us.

A **command-line tool**, `AdCodicem.Pdf.Tool` (a `dotnet tool` and a Native AOT binary), is seeded by M06 and
grows with every milestone after it: a milestone that adds an operation adds its verb once the tool exists.
It is a track, not a milestone of its own.

---

## M00 — Repository foundations

**Goal**: any session can build, test and publish without discovering anything.
**Deliverables**: solution and project layout, central package management, GitHub Actions CI (build, test,
benchmarks on demand, publish on tag), a `SessionStart` hook that installs the SDK, and the documentation
frame (`CLAUDE.md`, `architecture.md`, `decisions.md`, `roadmap.md`, `corpus.md`, `status.md`).
**Acceptance**: CI is green on a clean clone; a NuGet package builds.

## M01 — Object model and tolerant reading

**Goal**: open any PDF, imperfect ones included, without loading it into memory.
**Deliverables**: the COS object model; a lexer and parser over spans; Flate (with PNG and TIFF
predictors), LZW, ASCII85, ASCIIHex and RunLength filters; classic tables, cross-reference streams, object
streams, the `/Prev` chain and hybrid-reference files; lazy resolution with a bounded cache; rebuilding by
scanning when the index is wrong or absent; `PdfDiagnostics`; the corpus harness itself.

**Acceptance**
- Every document in the corpus opens, and its page count and catalog match the manifest.
- Each `damaged` document opens with exactly the diagnostics its manifest declares — no more, no fewer.
- No document, hostile ones included, produces an untyped exception, unbounded recursion, or an allocation
  the file chose; each stays inside a per-document time budget.
- Opening a document does not read its content: on the `scan` and `stress` documents, bytes read at open
  are a small fraction of file size, verified by counting reads.
- Indexing the `stress` document holds within a stated memory budget recorded in `status.md`.

## M02 — Document validation

**Goal**: given any document the reader can open, produce a structured, machine-readable verdict on what
is wrong with it — separately from whether it could be read at all.

**Deliverables**: in the core, under `AdCodicem.Pdf.Validation` (ADR 36), a rule engine (`IValidationRule`,
`ValidationProfile`, `PdfValidator`) whose findings carry a stable rule identifier, a severity, the object
they concern and a remedy hint; a `PdfValidationReport` that serializes; and the **structural profile** —
file structure, object graph integrity, page tree consistency, stream integrity, font embedding, resource
resolution, metadata coherence, annotation and destination targets. The object-shape rules — the keys each
dictionary type requires, their types, the keys a version deprecates — are generated from the PDF Association's
Arlington PDF Model by a tool, into tables committed and checked by a test, at warning severity at most (ADR 44).

Rule identifiers are part of the public contract from the day they ship: repair consumes them (M05),
conformance profiles extend them (M20), and callers filter on them.

**Acceptance**
- Every well-formed corpus document, from all four producers, validates with **no error-severity finding**.
  A validator that calls Chromium's or LibreOffice's output broken is a wrong validator, not a strict one.
- Every document produces exactly the findings its manifest declares, with stable rule identifiers — a
  damaged one its damage, a sound one nothing it did not declare.
- The vendored conformance fixtures discriminate: a file that is structurally sound but PDF/A-invalid
  produces **no** structural error. Conformance verdicts must not leak into the structural profile — they
  belong to M20.
- Two runs over the same document produce the same findings in the same order.
- Validating the 1000-page document holds within a stated memory budget, and does not read content it does
  not need to inspect.

## M03 — Writing and round-trip fidelity

**Goal**: rewrite what was read, byte for byte in semantic terms.
**Deliverables**: the public API baseline ([#35]), in place before the writer adds any public API; a
forward-only `PdfWriter` (object numbers reserved ahead, indirect `/Length`, compression on the fly), classic
tables and cross-reference streams, object streams on write, full rewrite and incremental update, a
deterministic `/ID`, and preservation of an existing signature. An **output version policy** (ADR 40): the
caller chooses PDF 1.7 or PDF 2.0 — with no choice, a document read from a file keeps its declared version and
a new one is written as 1.7 —, the writer computes the minimum version a document needs, raises it when a
feature or a merge requires it, and unions `/Extensions`. Three conventions for every later milestone, decided
here before the first stable release freezes the API and each recorded as an ADR when implemented: objects
read from a file are **read-only**, changed through an explicit change set (`SetObject`, `AddObject`,
`RemoveObject`); a **full rewrite of a signed document** is refused with `PdfSignatureInvalidationException`
unless the caller sets `AllowInvalidatingSignatures`, and then each signature it breaks is reported;
**cancellation and progress** for every long operation — a `CancellationToken` last, checked without
allocating, an `IProgress<PdfProgress>` just before it, never in options —, and `SaveAsync` and `OpenAsync`
never do synchronous I/O.

**Acceptance**
- Every corpus document survives open → save → reopen with an identical object graph, compared
  semantically rather than textually; an encrypted one is refused, typed, until M16 gives the writer its key.
- An external referee opens every rewritten document without complaint, in 1.7 and in 2.0 output.
- Saving the same document twice produces identical bytes.
- An incremental update on each signed corpus document leaves the original bytes untouched, and every
  existing signature still covers its byte range. The corpus's own contract is unsigned; its signed twin joins
  when M04 adds it.
- Rewriting the `stress` document holds memory flat and stays within the stated throughput budget.

## M04 — Revisions and signature coverage

**Goal**: say what a document looked like at each of its revisions, and what every signature in it covers —
without any cryptography.
**Deliverables**: in the core, the revision history — each incremental update enumerated, revision *n*
opened read-only as a lazy index cut at its own `startxref`, objects compared between two revisions; each
signature's `/ByteRange` checked against the revision it closes; every change made after a signature
classified (form fill, annotation, new signature, DSS, content replacement) and checked against its
`DocMDP` and `FieldMDP` permissions, so that shadow attacks are reported; a `signature.*` rule family in the
structural profile. Debt T25 — a `/Prev` that misses its section — is fixed by M02, before its cross-reference
rules, as `docs/status.md` plans; should it still be open, it is M04's first slice.

**Acceptance**
- Every signed corpus document has the revisions pyHanko counts — qpdf and pikepdf merge the chain and list
  none —, each revision's copy opens in qpdf and pikepdf as we describe it, and each signature covers exactly
  the revision pyHanko, run in a container, says it covers.
- A document modified after signing reports each later change with its class, and a change that its
  certification forbids as a finding, on crafted shadow-attack fixtures and on the signed corpus documents.
- Opening a revision reads no more than that revision's index.

## M05 — Repair

**Goal**: turn a damaged document into a sound one, and state precisely what was changed and what was lost.

**Deliverables**: `PdfRepair` in the core, driven by the reader's diagnostics and the M02 findings, with
each remedy attached to the finding that justified it: rebuild the index, recompute stream lengths, drop
or reconstruct unparseable objects, re-derive an inconsistent page tree, re-link orphaned pages, remove
references that point nowhere, normalize the trailer. Two modes: **conservative**, which changes only what
is broken and writes an incremental update, and **rebuild**, which normalizes the whole file. A
`PdfRepairReport` says what was found, what was done, and what could not be saved.

Conformance remediation — embedding missing fonts, adding metadata — is **not** repair; it belongs to M21.

**Acceptance**
- Every `damaged/*` document repairs into a file that opens with no repair needed, validates with no error
  finding, and is accepted by an external referee.
- The repaired file matches the **undamaged original it was derived from**: same page count, same extracted
  text. The corpus keeps those originals precisely so this can be asserted rather than asserted about.
- Repairing a sound document in conservative mode changes nothing: byte-identical output.
- When content is genuinely gone, the report says what was lost. A silently shorter document fails the test.
- Repairing a signed document says which revision each remedy lands in, and never touches a signed one in
  conservative mode.
- Repairing the 1000-page document holds memory bounded.

## M06 — Pages and case-file assembly

**Goal**: the first business priority — compose a case file from generated pages and third-party PDFs,
losing nothing on the way.
**Deliverables**: the page tree with inherited attributes (`Resources`, `MediaBox`, `CropBox`, `Rotate`);
`PdfPageCollection` (insert, remove, reorder, rotate, extract); cross-document deep copy with resource
deduplication; a merge that preserves bookmarks, links, annotations and attachments **and** every
catalog structure a merge can lose: page labels (read, written, remapped when pages move), the name
trees and `/Dests` with key renaming, `/AcroForm` fields with collision-safe renaming of fully qualified
names, `/OCProperties`, `/Extensions`, output intents, viewer preferences, the open action, and the logical
structure tree, recombined as ADR 17 decided; an attachments and associated-files API (document, page and
annotation level, `AFRelationship`, streamed); typed viewer preferences (page mode, `DisplayDocTitle`, print
scaling); a high-level assembly API; a merge or edit that would void a certification signature refused, or
reported when the caller insists (M04). The command-line tool starts here: `info`, `validate` (M02), `repair`
(M05, which the execution order places first; the verb lands with M05 should it close later), `merge`, `pages`.

**Acceptance**
- Merging the `contract` document with third-party appendices and generated pages yields a document an
  external referee accepts, in which every internal link and bookmark of every part still resolves to the page
  it named. The corpus's contract has no appendix yet; until it has, the condition runs on a set of real
  documents with links and outlines.
- Merging two forms that both define a field of the same name keeps both values; merging documents with
  page labels, layers or named destinations keeps each working, verified by an external referee.
- The merged file is no larger than the sum of its inputs minus deduplicated resources, measured.
- Extraction, reordering and rotation preserve inherited attributes: a page taken out of a document keeps
  the media box, crop box and resources it inherited from its ancestors.
- Continuous pagination: a merged case file shows the page labels the caller asked for in every viewer
  pdf.js and qpdf agree on.
- Assembling one hundred corpus documents holds memory proportional to the largest single page.

## M07 — Case-file completion

**Goal**: everything else a case file is made of, before a line of layout exists.
**Deliverables**: split strategies — by top-level bookmark (one file per exhibit, each keeping its outline
subtree), by maximum size, by page count, by page-label range, by separator page — with a page-selection
grammar and collate or interleave for duplex scans; images to pages **without lossy re-encoding** — JPEG
and JPEG 2000 passed through; TIFF strips in CCITT, LZW, Deflate or PackBits passed through, several strips
stacked; PNG's compressed data passed through, or, interlaced or with alpha, re-deflated losslessly and
reported — one page per TIFF frame, at their true resolution and EXIF orientation; unreferenced resources
pruned on extract and split; outline editing, import and export as JSON; links between pieces rewritten from
`GoToR` to internal `GoTo` after assembly, `GoToE`, and PDF open parameters (`#page=`, `#nameddest=`); a
received portfolio unpacked into one paginated, bookmarked volume, a certified member carried unsigned with
its original attached (`AFRelationship` `/Source`) and reported. The corpus manifest admits inputs other than
PDF, once, for every milestone that needs them — the images here, the CII, UBL and XRechnung XML of M14, the
FDF and XFDF of M16, the EML and MSG of M18, the DOCX of M31: a `format` field, `pdf` by default, on which the
PDF acceptance tests filter, and an expectation block per format; one provenance and license rule, and one
remote fetcher, for all.

**Acceptance**
- Splitting the corpus's bookmarked documents by top-level bookmark yields parts whose page counts, outline
  subtrees and text an external referee confirms, and whose sizes hold no resource the part does not use.
- Every image file in the corpus becomes a page whose image stream is byte-identical to the file's compressed
  data — or, where the format cannot be carried as it is (an interlaced or alpha PNG, uncompressed TIFF
  samples), whose decoded samples are identical and whose rewrite is reported — at the size its resolution
  states.
- The portfolio in the corpus unpacks into one volume with one bookmark per member.

## M08 — Fonts, text and content streams

**Goal**: write text that is correct, embedded, extractable and accessible.
**Deliverables**: a TrueType and OpenType parser (metrics, `cmap`, `hmtx`, `glyf`/`loca`, `CFF`);
subsetting; Type0/CIDFontType2 embedding with `ToUnicode`; a font registry with family resolution and
**per-character fallback** by script coverage, down to a visible `.notdef` and a diagnostic naming the code
points no font covers; WOFF and WOFF2 decoding, bounded (ADR 34); the metrics of the standard 14 fonts;
content stream operators; the OFL font set ([#34]) — Liberation Sans, Serif and Mono as WOFF2 in the data-only
`AdCodicem.Pdf.Fonts` satellite, and Liberation Sans regular in the core, so that the core alone can stamp a
PDF/A document — confirmed by an ADR with the measured sizes and the license checks.

**Acceptance**
- A generated document containing accented French text, typographic ligatures, a CJK sample and a Cyrillic
  name absent from the primary font extracts back to exactly the input text through an external extractor.
- Every font in a generated document is embedded and subsetted; an external referee confirms it, and the
  subset contains only the glyphs used.
- Advance widths match a reference renderer within a stated tolerance across the corpus fonts.

## M09 — Content on existing documents

**Goal**: act on a received PDF without regenerating it — the stamps a case file carries included.
**Deliverables**: watermarks and stamps, numbering, overlay and underlay, headers and footers added after the
fact, N-up and imposition, resource dictionary merging without name collisions; the **exhibit stamp** (firm,
"Pièce n° 2.1", date; hierarchical numbering, per-piece page numbering, separator pages); **Bates
numbering** across a set, with a document-to-range map and idempotent update or removal of our own stamps;
every stamp written as an `/Artifact` of type `/Pagination` — subtype `Header`, `Footer` or `Watermark` in 1.7
output, `PageNum` and `Bates` added in 2.0 — so that a tagged or PDF/UA input stays so; page normalization —
fit to A4, set the page boxes, crop, flatten `/Rotate` with annotations and links transformed alike. The
command-line tool gains `stamp`, `bates`, `normalize`.

**Acceptance**
- Stamping every corpus document changes only the stamped pages: in an incremental update the original bytes
  are a prefix of the output and the appended revision redefines only those pages; in a full rewrite every
  other object is equal in the object graph and every original content stream keeps its encoded bytes.
- Text extracted from a stamped document is the original text plus the stamp, and nothing else.
- Stamping a PDF/A document either preserves conformance, confirmed by the validator, or reports the loss
  in the diagnostics. Silence fails the test.
- Stamping a PDF/UA document keeps it valid for an external PDF/UA validator.
- Stamping a certified document is refused, or reported when the caller insists; it never voids the
  certification in silence.
- Bates numbers run continuously across a set of corpus documents, and removing them restores each page's
  `/Contents` and `/Resources` — the original content streams, never touched, byte for byte.

## M10 — Barcodes

**Goal**: the codes invoices, labels and cover sheets carry, as vectors, with no dependency.
**Deliverables**: the `AdCodicem.Pdf.Barcodes` satellite — managed, AOT-compatible, deterministic encoders
for QR, Data Matrix, Code 128 and GS1-128, PDF417, EAN and UPC, painted as form XObjects; payload builders for
the EPC SEPA credit-transfer QR and the Swiss QR-bill payment part and receipt; a hook for M09's stamps, and
one for the HTML engine (M12.5); alternative text carrying the payload once tagging exists (M13).

**Acceptance**
- Every code generated from a reference set of payloads is read back to the same payload by an independent
  decoder (ZXing, in a container) from the rasterized page.
- A Swiss QR-bill passes SwissQRBill's validator in CI, and the variant set passes SIX's validation portal,
  submitted by hand once per guideline version and recorded in `status.md`; an EPC QR is read with the exact
  payload by two independent decoders (zxing-cpp and zbar) at 150 and 300 dpi, equal byte for byte to the
  payload segno builds for the same data.
- Generating the same code twice produces identical bytes.

## M11 — Annotations and optional content

**Goal**: review, freeze and layer a received document.
**Deliverables**: markup annotation authoring — highlight, underline, strike-out and squiggly from quads,
notes with replies, free text, shapes, ink, stamps ("RECEIVED", "PAID"), caret — and removal by subtype or
author; appearance streams generated for every subtype; selective flattening that honors `/F` flags and
`/OC`; link annotations on existing pages; optional content read, created, merged (M06), flattened and
removed, and print-only stamps ("COPY").

**Acceptance**
- Every annotation we create displays identically in pdf.js and in the rasterizer referee, and survives a
  round trip through qpdf.
- Flattening the annotated corpus documents keeps their appearance, verified by rasterized comparison, and
  removes the annotations.
- A print-only layer is visible when printing and hidden on screen in an external renderer.

## M12 — HTML → PDF engine

**Goal**: the original promise. **XL — split into sub-milestones:**

- **M12.1** — CSS engine: tokenizer, selectors, cascade, inheritance, typed computed values, default
  stylesheet, and a **declared CSS level** — custom properties, `calc()` and its relatives, cascade layers,
  nesting, `:is()`, `:where()`, `:has()`, `@media print`, `@supports`, modern units — with a diagnostic for
  anything outside it.
- **M12.2** — Block and inline layout; line breaking by UAX #14, bidirectional text by UAX #9 (ours, ADR 43),
  OpenType features (`tabular-nums`, small caps, superscripts), hyphenation with vetted patterns (an
  optional satellite), justification; `@page` pagination with margin boxes, page selectors, named pages,
  page orientation and page groups; running elements, named strings, counters and counter styles;
  fragmentation (`break-*`, widows, orphans, `box-decoration-break`); floats and positioning; forward
  references by the strategy of ADR 39.
- **M12.3** — Tables: automatic and fixed layout, spanning cells, repeated headers **and footers**, captions,
  rows kept whole, spans across page breaks, carried-forward subtotals, shrink-to-fit.
- **M12.4** — Flexbox and a defined grid level (template areas, named lines, auto-placement), both
  fragmented across pages.
- **M12.5** — Images (JPEG passed through with its EXIF orientation, PNG, GIF, WebP, TIFF and JPEG 2000 passed
  through), inline and referenced SVG as vectors with real text, borders and backgrounds, rounded corners,
  shadows, opacity and blend modes, gradients as native shadings, 2D transforms, CSS Color 4 and 5, barcodes
  (M10); resources loaded under the deny-by-default policy of ADR 38.
- **M12.6** — Links, bookmarks (`bookmark-level`, `-label`, `-state`), named destinations from ids, a table
  of contents with real page numbers and leaders, `target-counter()`, `@font-face` (WOFF2, `unicode-range`),
  metadata from `<title>`, `<meta>` and `lang`, attachments declared in HTML, PDF pages as images and
  letterheads, stamps rendered from an HTML fragment (M09), the public API and DI integration, batch
  generation with compiled templates and shared caches, and the options of Puppeteer's `page.pdf()` mapped
  for those who migrate. The command-line tool gains `html2pdf`.
- **M12.7** — Footnotes and multi-column layout.

**Acceptance**
- The reference business documents — invoice, multi-page report, contract, and the two-column report (the
  report's annex, set in two columns) — render within an agreed visual difference threshold against approved
  reference images, page by page.
- Their generated versions enter the corpus and satisfy every earlier milestone's acceptance conditions in
  turn: what we produce must be as readable as what we consume.
- Generating a one-thousand-page report from a streamed source holds memory constant as page count grows,
  and from a single string holds its retained memory constant once the parsed document is counted, measured
  at 10, 100 and 1000 pages; generating a thousand invoices in one batch meets a throughput budget enforced
  in CI.
- Unsupported CSS never fails a render: it degrades and says so in the diagnostics.
- A template that references a private address, a cloud metadata endpoint or a file outside its root loads
  nothing and reports each refusal.

## M13 — Tagged structure and accessibility

**Goal**: produce PDFs that are genuinely accessible, not merely labeled as such.
**Deliverables**: the full logical structure tree, marked content and the parent tree, alternative text,
language, reading order, artifacts for decorative elements, tagged tables, notes and generated content;
PDF/UA-1 as the target (ADR 40 keeps PDF/UA-2 for M28); author overrides from CSS — tag type, role map,
artifact marking.

**Acceptance**
- The reference documents pass PDF/UA-1 validation by an external validator with no error.
- Reading order extracted from the structure tree matches the visual order, including in the two-column
  report and across page breaks.
- Every image carries alternative text or is marked as an artifact; no exceptions, verified by a test.

## M14 — PDF/A-3 and Factur-X

**Goal**: an electronic invoice that a French or German platform accepts, generated from HTML and a model.
**Deliverables**: PDF/A-3b, 3u and 3a generation (output intent, XMP, rendering constraints), and the dual
PDF/A-3a and PDF/UA-1 claim; a public XMP model with custom and extension schemas; the
`AdCodicem.Pdf.FacturX` satellite — embedding and extraction aware of every Factur-X and ZUGFeRD profile and
version (file name, `AFRelationship`, the `fx:` extension schema), validation of the XML against its schema,
the EN 16931 business rules and the French and German national rules (ported to C#, since the official
Schematron needs XSLT 2.0), a typed EN 16931 invoice model producing CII and UBL (composing an existing
open-source model rather than rewriting one), and a readable PDF rendition of a received CII, UBL or
XRechnung invoice.

**Acceptance**
- Documents we generate pass veraPDF for PDF/A-3b, 3u and 3a with no error, and PDF/UA-1 for the dual claim.
- A Factur-X invoice we generate is accepted by an independent Factur-X validator, and its XML matches the
  input byte for byte.
- The Factur-X invoices in the corpus yield their embedded XML byte-identical, and their profile and version.
- An invoice built from the model produces CII and UBL that the KoSIT validator accepts under EN 16931.

## M15 — Extraction and analysis

**Goal**: read what a PDF contains.
**Deliverables**: a content stream interpreter (graphics state, text, positions); positioned glyphs with
font and size, read from TrueType, OpenType, Type 1, bare CFF and Type 3 fonts; grouping into words, lines,
blocks and columns; table detection with a confidence score; the tagged structure preferred where present;
hidden layers honored; extraction of images, metadata, bookmarks and attachments; **text search** with
positions (literal, regular expression under a timeout, folding case, diacritics and ligatures, across
hyphenated line breaks); **exports** to Markdown, JSON, ALTO and hOCR, chunked with page, page-label and
Bates anchors; a feature inventory of a document (fonts, images and their resolution, color spaces,
annotations, forms, signatures, attachments, layers, conformance claims, active content, space by
category); in `AdCodicem.Pdf.FacturX`, the check that a received Factur-X or ZUGFeRD hybrid's visible invoice
number, dates and totals match its embedded XML, M14's visible-consistency matcher run over this milestone's
search. The command-line tool gains `text`, `markdown`, `search`, `inventory`.

**Acceptance**
- Text extracted from each corpus document the reader opens matches the manifest expectations — the
  encrypted ones join when M16 decrypts them —, including the two-column report, where reading order must be
  correct.
- A `scan` document reports that it has no extractable text rather than returning noise.
- The Factur-X invoice yields its embedded XML byte-identical to the source.
- Table detection on the invoice returns the line items with their columns, and states its confidence.
- Searching the corpus for its expected text finds every occurrence at the position an external tool
  (PyMuPDF, in a container) reports, within a stated tolerance.
- Extracting from the `stress` document holds memory bounded and independent of document length.

## M16 — Security and forms

**Goal**: open protected documents, produce protected documents, handle forms.
**Deliverables**: RC4 40/128 and AES-128/256 decryption, encryption and permissions, on managed MD5, RC4 and
AES wherever the platform lacks them (ADR 41); AES-GCM and the integrity MAC read (ISO/TS 32003 and 32004);
crypt-filter options on write (attachments only, clear metadata); the public-key security handler detected
and reported under a stable code, its decryption in the signing satellite (ADR 41); unencrypted wrapper
documents recognized; AcroForms — reading, filling, flattening, field appearances, a **field creation API**
and signature field placeholders; XFA detected (static, hybrid, dynamic), its datasets read, removed or kept
in step on fill; usage rights (`UR3`) detected and removed, with a diagnostic, when a save would break them;
standard Acrobat formats (`AFNumber`, `AFDate`, `AFPercent`, simple sums) recognized without executing any
script, and any other script reported; **FDF, XFDF and JSON** import and export of field values, and of
annotations through M11's model, and batch filling.

**Acceptance**
- Every encrypted corpus document opens with its recorded password, and its content matches its unencrypted
  twin — the document it was derived from, a sibling with the same content, or its decryption by an
  independent tool; one whose password nobody recorded is refused, never guessed; a public-key-encrypted one
  is reported, never misread.
- Every encrypted corpus document with a recorded password round-trips, by a full rewrite and by an
  incremental update, under its own key, and an external referee opens the result with the same password.
- Documents we encrypt open in an external referee with the same password and permissions.
- The `form` document round-trips: filled, saved, reopened, and the values read back are the values
  written; after flattening the values are still visible and the fields are gone.
- Filling a hybrid XFA form, or a Reader-extended one, either keeps it coherent or reports what the save
  lost. Silence fails the test.
- XFDF exported from a filled corpus form imports into the blank form and reproduces its values, verified
  by an external referee.

## M17 — HTML forms

**Goal**: fillable contracts and onboarding packs generated from templates.
**Deliverables**: opt-in mapping of `<input>`, `<select>`, `<textarea>`, check boxes, radio buttons and
buttons to AcroForm fields with appearances, tagged under `Form` with their tooltip; signature placeholders
placed from the layout.

**Acceptance**
- A form template renders to a PDF whose fields an external referee lists with their names, types and
  values, and that passes PDF/UA-1.

## M18 — Legal case files

**Goal**: the case file as a product — numbered pieces, their inventory, and what French courts ask for.
**Deliverables**: the `AdCodicem.Pdf.CaseFile` satellite, driven by data: a case-file model (piece number,
title, date, source hash) from which stamps (M09), bookmarks, page labels and a hyperlinked inventory
(*bordereau de communication de pièces*, rendered by M12) all follow, so they cannot disagree, and a JSON
export of it; court-portal presets (Télérecours, e-Barreau and RPVA, PLEX) — signet naming, one file per
piece, size caps — kept as data and verified against the current official guides before each is encoded;
an integrity manifest — the SHA-256 of each piece and of each file produced, as an associated file summarized
in XMP, and the volume's own SHA-256 in the XMP of an update appended to it and in a sidecar, since a file
cannot hold its own digest —; e-mail (EML, MSG optionally) to a PDF piece, its attachments as associated
files or sub-pieces; references such as "pièce n° 12" linked to the piece's first page. The command-line tool
gains `casefile`.

**Acceptance**
- A case file assembled from a manifest of corpus documents carries stamps, bookmarks, page labels and an
  inventory that agree with the manifest, verified by extraction, and whose links an external referee
  resolves.
- A case file shaped by a portal preset satisfies every rule the preset encodes, checked by a test per rule.
- The corpus e-mails convert to pieces whose text, headers and attachments extraction finds intact.

## M19 — Redaction and sanitization

**Goal**: anonymize a piece, and strip what should not leave the office, verifiably.
**Deliverables**: a content-stream editing pipeline (read, filter, rewrite), grown from M11's marked-content
filter and shared by every later content change; true redaction — regions marked with `/Redact`, reviewed,
then applied by removing glyphs, paths and (once M22 exists) image pixels, the same text scrubbed from
annotations, form values, bookmarks, metadata, alternative and actual text, an overlay drawn, and the result
verified by extracting again — always as a full rewrite, since an incremental update keeps the removed bytes;
search-and-redact with detectors for IBAN, French NIR, SIREN and SIRET, e-mail, telephone and dates of birth,
producing annotations to review before they apply; inspect-then-sanitize — report, then remove by category,
JavaScript and additional actions, launch and submit actions, embedded files, XFA, rich media, hidden layers,
invisible text, metadata, piece information, thumbnails, comments, form data, other tools' watermarks and
stamps, and earlier revisions — with the loss of PDF/A, PDF/UA or signatures reported; the `action.*` and
`hidden.*` rule families. The command-line tool gains `redact` and `sanitize`.

**Acceptance**
- Redacted text is absent from every extraction an external tool performs (pdftotext, PyMuPDF), from the
  file's bytes, and from every earlier revision, on the corpus documents it applies to.
- Sanitizing the corpus's documents with active content leaves none that veraPDF's feature report or an
  external inspector finds, and the report lists everything removed.
- A redacted PDF/UA document stays valid for an external PDF/UA validator.

## M20 — PDF/A levels and conformance profiles

**Goal**: regulatory conformance, guaranteed and checkable.
**Deliverables**: PDF/A-2b, 2u and 2a generation beside M14's part 3; conformance actively preserved when
merging, and when writing to a PDF/A-1 file; PDF/A and PDF/UA profiles for the M02 rule engine, delivered in
stages in the `AdCodicem.Pdf.Conformance` satellite (ADR 36) — PDF/A-1, 2, 3 and 4 at every level, PDF/UA-1
mapped to its clauses and to the Matterhorn Protocol, with an outcome for the conditions only a person can
judge; the public rule API ADR 36 deferred, and caller-defined policies in a namespace of their own.

**Acceptance**
- Documents we generate pass veraPDF for the claimed conformance level, with no error.
- Merging two PDF/A documents yields a document that still passes veraPDF; merging a conforming one with a
  non-conforming one reports the loss precisely.
- The conformance profiles agree with veraPDF on every corpus document; each disagreement is either
  fixed or recorded in the manifest with its reason.

## M21 — Converting received documents to PDF/A

**Goal**: a case file of third-party exhibits archived as PDF/A, with an honest account of each.
**Deliverables**: a `PdfAConverter` driven by M20's findings, as repair is by M02's (ADR 22): fonts embedded
or substituted from the registry (reported), an output intent added, XMP rebuilt, JavaScript, encryption and
forbidden actions removed, missing appearances generated (M11), attachments fixed (MIME type, `AF`), and a
report of whatever could not be converted.

**Acceptance**
- The non-conforming corpus documents convert to PDF/A-2b or 3b that veraPDF accepts, or report precisely
  why they cannot; extracted text is unchanged by conversion.

## M22 — Imaging and OCR

**Goal**: the pixels of a scan, and the text a scan does not have.
**Deliverables**: in the core, CCITT G3 and G4 decoding (ADR 42); the `AdCodicem.Pdf.Imaging` satellite —
managed, dependency-free, bounded and fuzzed from the start — JBIG2 (with global segments), JPEG 2000 and
JPEG (CMYK and YCCK included) decoders, and lossless CCITT G4 and JBIG2 generic encoders; the pixels of every
codec edited under M19's redaction marks — CCITT and JBIG2 re-encoded losslessly, JPEG by a wipe of the marked
blocks' coefficients, JPEG 2000 re-encoded as Flate; color spaces and functions evaluated for image export;
an invisible, positioned text layer written from hOCR, ALTO or TSV, optionally tagged, so that a scan becomes
PDF/A-2u; an `IOcrEngine` interface for the engines callers bring; blank-page detection on pixels; page
orientation from the text layer, applied as `/Rotate`.

**Acceptance**
- Every image in the corpus decodes to the pixels an external decoder (MuPDF, in a container) produces.
- A corpus scan given its hOCR becomes searchable: an external extractor finds the recognized words at
  their positions.
- A redaction mark over a scan in each codec leaves every sample under it uniform and every other as it was,
  verified by an external decoder.
- A fuzzing campaign over the decoders finds no untyped exception, hang or unbounded allocation.

## M23 — Optimization, performance, hardening

**Goal**: deliver the frugality the library promises, with numbers.
**Deliverables**: global resource deduplication, consolidation of duplicate font subsets, recompression,
subsetting of inherited fonts, linearization; image downsampling and recompression with a target-size mode,
every lossy step opt-in and reported, JBIG2 lossless only (ADR 42); a benchmark campaign with performance
budgets enforced in CI; Native AOT, trimming and browser WebAssembly validation of the core; a tested
container profile (no fontconfig, read-only file system, a 512 MB cap) measured against a Chromium
baseline; fuzzing of the lexer and parser. A decode that yields a stream a piece at a time, so that a stream
past `Array.MaxLength` can be read and the memory held follows a window, while `MaxDecodedStreamLength`, made
a `long`, still bounds what a stream may decode to — a streamed bomb still costs its time ([#48]; an amendment
of ADR 34 written with it; in native memory if a measurement asks for it, ADR 35); a 64-bit length on stream
data ([#53]); an object cache weighted by bytes, and names interned per document beyond a frozen table ([#37]); a
budget on the cache of decoded object streams ([#50]); cross-reference sections and the rebuild's trailer scan
read through windows grown on demand ([#47], [#49]). The command-line tool gains `optimize`.

**Acceptance**
- Published budgets for throughput and allocation hold on the `stress` documents, and CI fails when a
  budget is exceeded — an allocation regression is a regression.
- Optimizing a corpus document reduces its size without changing what an external referee extracts from it.
- A Native AOT executable opens, transforms and writes every corpus document; the core runs the same
  operations in browser WebAssembly, encrypted documents included but those of revision 7 (AES-GCM), which
  the browser has no primitive for and which are refused, typed (ADR 41).
- A fuzzing campaign over the lexer and parser, seeded with the `damaged` documents, finds no untyped
  exception, hang or unbounded allocation.
- Under `PdfReaderLimits.Unbounded`, a stream that decodes past 2 GB is read whole.

## M24 — Comparison and templates

**Goal**: what changed between two versions of a contract, and the fields of an invoice that is not
Factur-X.
**Deliverables**: a satellite for a page-windowed word diff with move detection, written as an annotated PDF
or as JSON; a visual diff of two rasters behind the core's page-raster seam, proven on an external renderer's
rasters and run in-process once M25 exists; zone and anchor templates that extract fields from every document
with a given layout, each value with its confidence (ADR 15).

**Acceptance**
- Comparing each corpus document with an edited copy reports exactly the edits made, and nothing else.
- A template defined on one supplier's invoice extracts the same fields from that supplier's other
  invoices in the corpus, with the confidence it states.

## M25 — Rasterization

**Goal**: pages as images, for previews, thumbnails and visual tests.
**Deliverables**: `AdCodicem.Pdf.Rendering`, Skia rasterization reusing the M15 interpreter and the M22
decoders, honoring hidden layers and annotation appearances; the core's page-raster seam implemented, so that
M24's visual diff runs in-process.

**Acceptance**
- Rasterized pages of the reference documents match approved reference images within the agreed threshold.
- Rasterized scans in JBIG2, CCITT and JPEG 2000 match an external renderer within the same threshold.

## M26 — Signing

**Goal**: sign contracts the way French and European professionals do.
**Deliverables**: `AdCodicem.Pdf.Signing` — an algorithm-neutral `IPdfSigner`, a local implementation and a
path to an HSM; PAdES B-B and B-T; an injectable RFC 3161 time-stamp client and document time stamps on
unsigned files; signature fields, visible appearances that keep PDF/A and PDF/UA, and sequential signers;
certification (`DocMDP`) and field locks (`FieldMDP`); deferred two-step signing and a CSC API client for
remote qualified signatures; ECDSA and RSASSA-PSS; the public-key security handler (ADR 41). Network access
off by default, the signing time supplied by the caller. The command-line tool gains `sign`.

**Acceptance**
- A signature we produce validates in an external validator (EU DSS, in a container) at the level claimed,
  and the document still opens in every earlier acceptance test.
- Signing does not invalidate an existing signature on the signed corpus documents, the contract's signed
  twin (M04) among them.
- Documents encrypted for a certificate in the corpus decrypt with it, and match their twins.

## M27 — Long-term signatures and signature validation

**Goal**: signatures that stay verifiable for years, and a verdict on the ones we receive.
**Deliverables**: PAdES B-LT and B-LTA — the document security store, revocation data, renewed document time
stamps — and long-term material added to third-party signatures before archiving; validation of every
signature in a document — the digest over its byte range, the chain against the caller's trust anchors or
the EU trusted lists, revocation from the security store first and online only when allowed, time stamps,
the PAdES level reached, legacy `adbe.pkcs7.*` signatures, SHA-3 and EdDSA — in a report in the form of ETSI
TS 119 102-2, offline by default. The command-line tool gains `verify`.

**Acceptance**
- Our verdict agrees with EU DSS on every signed corpus document; each disagreement is fixed or recorded
  in the manifest with its reason.
- A B-LTA signature we produce stays valid in EU DSS after its signing certificate's expiry is simulated.

## M28 — PDF 2.0 conformance: PDF/UA-2, WTPDF, PDF/A-4

**Goal**: the accessibility and archiving standards built on PDF 2.0.
**Deliverables**: PDF/UA-2 and Well-Tagged PDF output on the PDF 2.0 writer (ADR 40) — the PDF 2.0 structure
namespace, role maps across namespaces (ISO/TS 32005), structure destinations, PDF Declarations; PDF/A-4,
4f and 4e generation; the PDF/UA-2 and Well-Tagged PDF validation profiles on M20's public rule engine, with
review items for what only a person can judge.

**Acceptance**
- The reference documents pass veraPDF for PDF/UA-2, WTPDF and PDF/A-4 with no error.
- Our PDF/UA-2 and WTPDF profiles agree with veraPDF on every output and on every corpus document that claims
  either; each disagreement is fixed or recorded in the manifest with its reason.

## M29 — Print production

**Goal**: invoices and statements printed at scale by an outsourced print shop.
**Deliverables**: PDF/X-4 and PDF/X-4p output (bleed, marks, trim boxes, output intents); PDF/VT-1, and
PDF/VT-2 over PDF/X-4p, for variable-data runs, built on M12's batch generation (document parts, reused
XObjects); color conversion through a managed color-management engine in a satellite, behind the core's
transform seam and M25's converter seam; CMYK and spot colors from CSS; overprint; soft proofs, separation
previews and overprint simulation through M25's rasterizer; PDF/X and PDF/VT validation profiles. PDF/X-5,
PDF/VT-2 over PDF/X-5 and PDF/VT-2s wait for a print provider who asks; mixed raster content compression stays
an open question.

**Acceptance**
- The reference documents pass an independent PDF/X-4 and PDF/VT check with no error. veraPDF validates
  PDF/A, PDF/UA and WTPDF only, and no open-source PDF/X validator is known: the maintainer chooses the referee
  at the start of the milestone, in its first slice — and decides there whether ISO 15930-7 caps PDF/X-4 at
  PDF 1.6, and so whether ADR 40 needs a 1.6 output for generated documents.

## M30 — Advanced typesetting

**Goal**: the typesetting niches the business target does not need first.
**Deliverables**: vertical writing modes, ruby annotations, MathML Core layout carried into the tagged
structure (PDF/UA-2 associated files).

**Acceptance**
- Reference documents in vertical Japanese and with MathML formulae render within the agreed threshold and
  pass PDF/UA-2.

## M31 — DOCX to HTML

**Goal**: Word documents in a case file, without an office suite.
**Deliverables**: the `AdCodicem.Pdf.Docx` satellite on the Open XML SDK, converting paragraphs, styles,
numbering, tables, images, headers, footers and sections to HTML and CSS for M12, with an approximate
fidelity that is documented rather than hidden, and a diagnostic for what it could not map.

**Acceptance**
- The Word documents in the corpus convert to PDFs whose text, headings and tables an external extractor
  finds in order, within a stated visual threshold of Word's own PDF.

---

## Open questions

Neither planned nor excluded; each would enter a milestone when what triggers it happens. Each has its
discussion under [Ideas](https://github.com/AdCodicem/AdCodicem.Pdf/discussions/categories/ideas), where a
need for it can be said — real use is what triggers most of them. A row added here gets its discussion from
the `Tracking` workflow's manual dispatch, matched by the subject, which is therefore not renamed lightly.

| Subject | What would trigger it | Discussion |
|---|---|---|
| Variable fonts, instanced before subsetting | A brand font delivered only as a variable font | [#91] |
| Color fonts and emoji | Chat or e-mail transcripts as case-file pieces | [#92] |
| Mixed raster content compression | Color scans too large for a portal cap after M23's recompression | [#93] |
| Pixel deskew and despeckle of scans | Copier output that M22's orientation cannot correct | [#94] |
| XLSX, PPTX and ODT to PDF | M31 proving the DOCX route worth extending | [#95] |
| A code-first layout API beside HTML (ADR 8) | Callers asking for one over templates | [#96] |
| A first-party OCR engine satellite | Callers asking for one over `IOcrEngine` | [#97] |
| Signed French 2D-Doc codes | An issuer approved by ANTS asking for them | [#98] |
| Heuristic tagging of untagged received documents | Accessibility obligations on documents a caller only receives | [#99] |
| A lossless JSON dump and update of the object graph, as qpdf's | Support cases, or corpus fixtures that need to be readable | [#100] |
| Barcode and patch-code recognition on scanned pages | Scan batches split on separator sheets that carry a barcode or a patch code (M07's separator predicate) | [#101] |
| EMF, WMF and EMF+ pictures converted to SVG | The share of DOCX pieces that carry them, which M31's report counts | [#102] |
| Office charts drawn from their XML rather than their fallback picture | Case-file pieces whose charts have no usable fallback | [#103] |
| Word's legacy form fields and content controls as AcroForm fields (M17) | Callers converting Word forms that must stay fillable | [#104] |

## Renumbering of 2026-09-26

Commits, pull requests and journal entries older than this revision use the previous numbers. The
milestones that kept their content map as follows; everything else is new.

| Before | After |
|---|---|
| M0 to M3 | M00 to M03 |
| M4 — Repair | M05 |
| M5 — Pages and case-file assembly | M06 |
| M6 — Fonts, text and content streams | M08 |
| M7 — HTML → PDF engine, M7.1 to M7.6 | M12, M12.1 to M12.6 |
| M8 — Tagged structure and accessibility | M13 |
| M9 — Content on existing documents | M09 |
| M10 — Extraction and analysis | M15 |
| M11 — Security and forms | M16 |
| M12 — PDF/A-3, Factur-X and conformance profiles | M14 (PDF/A-3 and Factur-X) and M20 (profiles and the other levels) |
| M13 — Optimization, performance, hardening | M23 |
| M14 — Satellites: rasterization and signing | M25 (rasterization) and M26 (signing) |

Since 2026-09-27 the numbers below ten carry a leading zero (M01 rather than M1), so that milestones
sort in order as text; older commits write them without it.

Commits older than 2026-09-13 follow an order older still, from before validation and repair were
inserted.

---

## Working a milestone

1. Read `CLAUDE.md`, `docs/status.md`, then `docs/milestones/<milestone>.md`, and the milestone's open issues.
2. Work in vertical, testable slices, never a whole horizontal layer. When a milestone starts, each slice its
   specification lists becomes an issue labeled `slice`, filed under the milestone; the pull request that
   completes a slice closes its issue.
3. A delivered feature is code plus tests plus an entry in the `status.md` journal.
4. What is discovered on the way and falls outside the milestone becomes an issue labeled `debt`, filed under
   the milestone that will pay it — not a change to the code.

## Tracking on GitHub

This file is the reference for what the milestones are and where each stands; GitHub mirrors it. The
`Tracking` workflow (`.github/scripts/sync_tracking.py`) keeps one
[GitHub milestone](https://github.com/AdCodicem/AdCodicem.Pdf/milestones) per row of the table above,
described by the goal its specification states, closed when the row says *done*, and never given a due
date: this roadmap is an intention, not a promise. It runs on every change to this file, a specification or
`.github/labels.json` on `main`, and never deletes a milestone the table stops naming.

A GitHub milestone's progress counts its issues: the slices of its specification, opened when it starts,
and the debt filed under it. Its exit criteria stay checkboxes in its specification, reviewed with the code
that ticks them. The debt table `docs/status.md` held until 2026-09-27 is now its issues labeled `debt`, and
its former identifiers, T01 to T40, are mapped there. A commit names the issue it advances in its footer
(`Refs #58`), never in its subject; the pull request closes what it completes (`Closes #58`), and an issue
closes only that way. The documents the corpus still wants are issues labeled `help wanted`, one per row of
`docs/corpus-contributions.md`, which stays their reference, each filed under the first milestone it
unblocks that has not started; the open questions below are discussions under
[Ideas](https://github.com/AdCodicem/AdCodicem.Pdf/discussions/categories/ideas).

A milestone closes when all its issues have, so an order between them is written where it holds: a line
`Blocked by: #55, #56` in the body of the issue that waits, or `Blocks: #60` in the body of the one waited on.
A target that is not an issue yet is named in words — `Blocks: M03 slice 1` — until the slice is opened and
its number takes that place. The bodies are the reference: whenever the owner or a collaborator opens or edits
an issue, and on each of its runs from `main`, the same workflow adds the *blocked by* relationships they
declare and removes those none declares any more; an issue opened by anyone else declares nothing. Being
filed under a milestone is enough for what only its closing waits on.

## Definition of done

Every milestone, without exception, closes only when all six hold:

| | Requirement |
|---|---|
| 1 | Its **exit criteria** are met |
| 2 | Its **acceptance conditions on the corpus** are green in CI, with no document skipped |
| 3 | **Unit tests** cover the behavior, its degenerate cases and its hostile ones — xUnit v3, AwesomeAssertions, NSubstitute where an interaction is the thing being asserted |
| 4 | **Integration tests** confirm, through an independent tool running in a container, anything the milestone claims about a document: that it is valid, that it round-trips, that its text is what we say it is |
| 5 | The **documentation site** matches what now exists: the user-facing pages under `docs/website/docs` for anything a consumer can call, the project documents for anything a contributor needs, and the state of the milestone's features in `docs/features/features.json`, from which the README's and the site's feature tables are generated |
| 6 | **`docs/status.md`** records the measurements rather than promising them |

Points 3 to 5 are not paperwork after the fact. An untested behavior is a guess; a claim no independent
tool has checked is an opinion; and a feature nobody can find in the documentation does not exist for
anyone outside this repository.

## Adding a milestone

Create `docs/milestones/<number>.md` from `docs/milestones/_template.md`, add its row above — the `Tracking`
workflow opens its GitHub milestone once that reaches `main` —, and specify only what is decided — a distant
milestone stays deliberately coarse. Its acceptance conditions, however, are written when the milestone is
written: they are what the work is for.


[#34]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/34
[#35]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/35
[#37]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/37
[#47]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/47
[#48]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/48
[#49]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/49
[#50]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/50
[#53]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/53
[#91]: https://github.com/AdCodicem/AdCodicem.Pdf/discussions/91
[#92]: https://github.com/AdCodicem/AdCodicem.Pdf/discussions/92
[#93]: https://github.com/AdCodicem/AdCodicem.Pdf/discussions/93
[#94]: https://github.com/AdCodicem/AdCodicem.Pdf/discussions/94
[#95]: https://github.com/AdCodicem/AdCodicem.Pdf/discussions/95
[#96]: https://github.com/AdCodicem/AdCodicem.Pdf/discussions/96
[#97]: https://github.com/AdCodicem/AdCodicem.Pdf/discussions/97
[#98]: https://github.com/AdCodicem/AdCodicem.Pdf/discussions/98
[#99]: https://github.com/AdCodicem/AdCodicem.Pdf/discussions/99
[#100]: https://github.com/AdCodicem/AdCodicem.Pdf/discussions/100
[#101]: https://github.com/AdCodicem/AdCodicem.Pdf/discussions/101
[#102]: https://github.com/AdCodicem/AdCodicem.Pdf/discussions/102
[#103]: https://github.com/AdCodicem/AdCodicem.Pdf/discussions/103
[#104]: https://github.com/AdCodicem/AdCodicem.Pdf/discussions/104
