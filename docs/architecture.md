# Architecture

A reference document. Load it when touching a boundary between layers or introducing a component. The
day-to-day frame is `CLAUDE.md`.

## 1. Overview

Two paths cross the library, and they share one object model and one writer:

```
HTML ─▶ parse ─▶ CSS cascade ─▶ box tree ─▶ layout ─▶ pagination ─┐
                                                                  ├─▶ PDF object model ─▶ writer ─▶ bytes
existing PDF ─▶ lexer ─▶ xref ─▶ lazy objects ─▶ manipulation ────┘
```

The meeting point is deliberate: "add an HTML page to an existing PDF" is not a special case, it is the
ordinary composition of the two paths.

## 2. Package split

| Package | Role | Dependencies |
|---|---|---|
| `AdCodicem.Pdf` | Object model, reader, writer, pages, fonts, logical structure, security, diagnostics | **none** |
| `AdCodicem.Pdf.Html` | HTML parsing, CSS engine, layout, painting to PDF | AngleSharp, HarfBuzzSharp, SkiaSharp |
| `AdCodicem.Pdf.AspNetCore` | DI registration, `IResult`, MVC integration | `AdCodicem.Pdf.Html` |
| `AdCodicem.Pdf.Validation` | PDF/A and PDF/UA validator | `AdCodicem.Pdf` |
| `AdCodicem.Pdf.FacturX` | Factur-X and ZUGFeRD: embedding, extraction, validation | `AdCodicem.Pdf` |
| `AdCodicem.Pdf.Rendering` | PDF → image rasterisation (late satellite) | SkiaSharp |
| `AdCodicem.Pdf.Signing` | PAdES (late satellite) | `AdCodicem.Pdf` |

The rule: **the core depends on nothing**. That is what guarantees its Native AOT compatibility, its
memory profile and its reuse on a server as much as in a serverless function. Any temptation to let Skia,
AngleSharp or a native dependency into it is a sign the split is wrong.

## 3. The core, `AdCodicem.Pdf`

```
Objects/      COS object model: PdfName, PdfNumber, PdfString, PdfArray, PdfDictionary, PdfStream,
              PdfReference. Immutable where it can be, interned for names.
IO/           PdfLexer (tokens), PdfObjectParser (objects), Filters/ (Flate with predictors, LZW,
              ASCII85, ASCIIHex, RunLength, DCT and JPX passed through), XRef/ (classic tables,
              cross-reference streams, object streams, the /Prev chain, rebuilding),
              PdfWriter (forward-only output).
Documents/    PdfDocument (open, save), PdfReaderOptions and PdfReaderLimits, PdfPage,
              PdfPageCollection, attribute inheritance, cross-document deep copy, assembly.
Fonts/        TrueType and OpenType parsing, metrics, subsetting, Type0/CIDFontType2 embedding,
              the ToUnicode CMap, the font registry and family resolution.
Content/      Content stream writing; the content stream interpreter used by extraction (M8).
Structure/    The logical structure tree (tagged PDF), marked content, the parent tree.
Security/     RC4 and AES decryption and encryption, permissions.
Diagnostics/  PdfDiagnostics: anomalies, repairs, guards reached, conformance losses; PdfException
              and its typed subclasses.
```

### 3.1 The read path

1. **Indexing** — read the tail of the file (`startxref`), follow the `/Prev` chain, and build a map from
   object number to either a byte offset or a position inside an object stream. Only that map lives in
   memory: a few dozen bytes per object, whatever the objects weigh.
2. **Repair** — if `startxref` is wrong, the table missing, or an offset does not point at the object it
   claims, fall back to scanning the whole file for `N G obj` headers, keeping the last definition of each
   number. Every repair is recorded in the diagnostics.
3. **Lazy resolution** — `PdfReference.Resolve()` reads and parses the object on demand. A bounded cache
   avoids reparsing hot objects (the page tree, shared resources) without ever retaining the whole document.
4. **Streams** — stream data is neither read nor decoded until the caller asks, and then decodes under the
   limits of the document it came from; a stream built in memory decodes under the defaults. A stream copied
   from one document to another travels **encoded**, with no decompress/recompress cycle.
5. **Guards** — every read sized by the file is bounded. A bound a valid file can exceed — a stream's
   decoded length, an object's length, a cross-reference section's length and their number, a trailer's
   length — is a `PdfReaderLimits` option, on by default and reported under its own `limit.*` code when
   reached; a bound only an invalid file reaches is an internal constant (ADR 34).

### 3.2 The write path

The writer only ever moves forward: it alone knows byte offsets.

- Object numbers are **reserved ahead of time** and bodies written later, so an object can refer to one
  that does not exist yet (the page tree, shared resources).
- Streams are written with an **indirect `/Length`**: compression runs straight to the output and the
  length object follows. No content stream is ever buffered whole.
- Two save modes: **full rewrite** (compact file, objects reordered, duplicates removed) or **incremental
  update** (appended at the end, original bytes untouched — required not to invalidate a signature).
- Output is deterministic: stable write order, and an `/ID` derived from content or supplied by the caller.

### 3.3 Fonts

A PDF embeds glyphs, not characters. The chain is therefore: text → shaping (HarfBuzz, in `.Html`) → glyph
identifiers → Identity-H encoding → an embedded subset. The core does no shaping: it receives resolved
glyphs and handles metrics, subsetting, embedding, and the `ToUnicode` table without which text is neither
copyable nor accessible.

## 4. The HTML engine, `AdCodicem.Pdf.Html`

```
Parsing/   AngleSharp: an HTML5-conforming DOM. Nothing else of AngleSharp is used.
Css/       A level 3 CSS tokeniser, a selector parser, the default stylesheet, the cascade,
           inheritance, and **typed** computed values (structs, not strings).
Layout/    Box tree, block layout, inline layout (line breaking, alignment), tables, flex, grid,
           pagination (@page, breaks, headers and footers, counters).
Rendering/ Painting the box tree into content operators, links, bookmarks, and emission of the
           tagged logical structure.
Fonts/     CSS family resolution, @font-face, the font cache, HarfBuzz shaping.
```

**Why our own CSS engine rather than AngleSharp.Css**: its computed values are strings that must be
reparsed on every access, which is disqualifying inside a layout loop, and the package has been in
permanent prerelease. HTML5 parsing, on the other hand, is thankless, normative and thoroughly solved by
AngleSharp: reuse it without hesitation.

**Layout and painting are separate**: layout knows nothing about PDF, and painting recomputes nothing.
That boundary is what will later make a rasterisation backend, or an SVG export, possible at all.

**DOM to PDF traceability**: every box keeps a reference to its source element. That is the precondition
for emitting the logical structure (PDF/UA) and for pointing at the offending line of HTML when something
goes wrong. No intermediate layer may drop it.

## 5. Memory and CPU strategy

- **Memory budget**: consumption follows the complexity of the **page** being processed, never the size of
  the document. A ten-thousand-page report must generate in the footprint of a ten-page one. One decoded
  stream is still held whole, up to `PdfReaderLimits.MaxDecodedStreamLength` and at most `Array.MaxLength`,
  until M13 decodes a piece at a time (T28), in native memory if a measurement asks for it (ADR 35).
- **Pooling**: write buffers, glyph arrays and layout boxes come from `ArrayPool<T>` or dedicated pools.
  What is rented is returned, exceptions included.
- **Structs and spans**: computed CSS values, metrics, rectangles and positions are structs. Parsing works
  on `ReadOnlySpan<byte>` without materialising strings.
- **Strings**: PDF names are interned once; the rest of parsing avoids `string` wherever it can.
- **Asynchrony**: the public API is asynchronous at its I/O edges; computation (layout, writing) stays
  synchronous, because parallelising across documents buys more than making one document await.
- **Parallelism**: never implicit. One document is generated on one thread; the caller runs several
  documents in parallel, and the API must make that safe and obvious.

## 6. Conformance

Conformance is not a box ticked at the end of the pipeline; it is a constraint that reaches back into
layout:

- **PDF/A** requires every font embedded, an output ICC profile, XMP metadata consistent with the
  information dictionary, and the absence of certain constructs.
- **PDF/UA** requires a complete logical structure tree, an explicit reading order, alternative texts and a
  declared language. Hence the DOM → box → marked content traceability, which must exist from day one even
  though full emission arrives later.
- **In manipulation**, merging two conforming documents must produce a conforming document: structure trees,
  `OutputIntents`, metadata and fonts are recombined, not concatenated. Whatever cannot be preserved is
  reported in the diagnostics.

## 7. Errors and diagnostics

| Situation | Response |
|---|---|
| The input is not a PDF, or is unreadable even after repair | `PdfException` |
| The caller asks for the impossible (a page that does not exist, a wrong password) | A typed exception |
| The file is imperfect but usable | An entry in `PdfDiagnostics`, processing continues |
| A reader guard is reached — by a valid but exceptional file, or a hostile one | A `limit.*` warning naming the `PdfReaderLimits` property that lifts it, and what fits is kept; `PdfLimitExceededException` instead, from whichever operation reached it, when the caller set `ThrowOnLimit` |
| A CSS feature is unsupported | An entry in the diagnostics, degraded rendering, never a failure |
| An operation breaks a conformance guarantee | An entry in the diagnostics, with the precise cause |

The diagnostic report is a returned value, not a side effect: it can be inspected, serialised and asserted
on in tests.

## 8. Testing

- **Unit**: every component, with its degenerate cases. The lexer and parser are tested on malformed input
  as much as on valid input.
- **Corpus**: real documents from real producers, and documents damaged on purpose. No milestone closes
  without passing on it — see `docs/corpus.md`.
- **Round-trip**: open → save → reopen → compare semantically. The central invariant of the foundations.
- **External referees**: qpdf, pikepdf, pypdf and veraPDF run in CI to check our claims against tools we
  did not write.
- **Fingerprints**: generated documents are compared byte for byte against a reference, which determinism
  makes possible. An intentional difference is approved by regenerating the reference in the same commit.
- **Visual**: page images compared against approved references within a threshold.
- **Benchmarks**: BenchmarkDotNet with `MemoryDiagnoser` throughout. An allocation regression is a
  regression.
- **Security**: fuzzing of the lexer and parser seeded with the corpus; no input may produce an untyped
  exception, an infinite loop or an unbounded allocation under the default limits. Every commit mutates every small seed document a
  little; each night mutates, far more deeply, the smallest document of each reader structure and a
  rotating share of the others (`FuzzingSeeds`).

## 9. Compatibility and versioning

Strict SemVer. While the major version is 0 the API may move, but every break is recorded. A public API
test — a checked-in baseline of exported signatures — makes any break visible in review rather than after
publication.
