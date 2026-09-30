# Threat model

What the library defends against, how, where each defense lives in the code, and which test fails if it goes.
It is one document for the whole library ([ADR 46](adr/0046-every-milestone-ends-with-an-adversarial-review.md)).
A milestone that opens or widens a surface a hostile input reaches completes its section **in the same change**.
Every milestone's review checks the code against it (`docs/milestone-review.md`, *Security*). Read it before you
touch a parser, a decoder or a resource loader (`CLAUDE.md`).

This first version was written on 2026-09-30 against `13e06bb` ([#135]). It covers what exists there: the reader
(M01) and the validator (M02, slices 1 to 3). Each defense below was checked against the code as it stands, and
each test it names was read. A defense found missing is not written as if it held. It is listed under the
*Known gaps* of its surface, with the issue that pays it.

## How to read it

- A **threat** is what a file can make the library do: exhaust memory, the stack or time, read out of bounds,
  throw something untyped, return a wrong answer, or reach the host.
- A **defense** is the code that stops it: a bound, a visited set, a check, an iterative walk.
- **Held by** names the test that fails if the defense goes. **Fuzzed** says whether the mutation campaign
  reaches the code at all (*What holds the defenses*, below).
- A bound is classified as [ADR 34](adr/0034-every-valid-pdf-is-readable-and-the-readers-guards-are.md) requires.
  A **guard** is a `PdfReaderLimits` property, on by default and reported under a `limit.*` code: a valid file
  may reach it. An **internal constant** is a bound that only an invalid file reaches, and the reason is written
  where the constant is declared.

## Assets

What a caller entrusts to the library when it opens a file it did not produce:

- **The process's memory.** Memory follows the heaviest object the caller touches, not the size of the file
  (invariant 2). No allocation is sized by a value from the file without a checked bound (invariant 4). The five
  guards cap what a valid but exceptional file may make the reader hold.
- **The process's time, and the calling thread.** Every call does work in proportion to the bytes it reads, under
  the guards. No call can be cancelled yet ([M03] slice 9, [M23] slice 11). A .NET thread cannot be aborted, so a
  slow call holds its thread until it returns.
- **The stack.** A `StackOverflowException` cannot be caught in .NET: it ends the process. Every recursion whose
  depth a file chooses is bounded, and the reader also asks the runtime whether stack is left
  (`RuntimeHelpers.TryEnsureSufficientExecutionStack`) before it goes one level deeper.
- **The caller's resources.**
  - `Open(string)` opens a read-only handle (`FileShare.Read`) and holds it until `Dispose`.
  - `Open(ReadOnlyMemory<byte>)` reads the caller's memory where it lies, without copying it.
  - `Open(Stream)` reads the caller's stream and leaves it open.
  - `Open(PdfFileSource, …)` releases the source when it was given to own it, on failure too.
- **The truth of what the library returns.** A caller decides on the objects, the decoded data, the diagnostics
  and the validation report. Should an upload be accepted? Is the document encrypted? Did the reader cut
  something? A file that makes the report lie defeats the caller as surely as a crash does. That covers hiding its
  encryption, hiding a cut, or getting the reader's own limit blamed on the file.
- **The host's logs and terminals.** Diagnostic and finding messages quote names and keywords from the file, and
  hosts log and print them.

The library holds more later: the files it writes ([M03]), the original bytes and existing signatures an
incremental update preserves ([M03], [M04]), passwords and file keys ([M16]), signing keys and certificates
([M26]), access to the network ([M12]'s resource loader, [M26] and [M27]'s clients), native code in the process
([M12], [M25]), and caches shared across documents and renders ([M08], [M12]). *Surfaces still to come* lists
each with the milestone that brings it.

## Attackers

- **Whoever wrote the file's bytes.** That is the author of a PDF the caller opens: an upload, an e-mail
  attachment, a document received for a case file. They control every byte. They can iterate offline against the
  same package and the same public source. They want to crash, hang or exhaust the host. They want the library to
  return something other than what the file holds, or a report that hides what the file is. Later, they will want
  to reach the host: its files, its network, its secrets.
- **Not the caller.** The caller's code is trusted, and so are its options, the path it names, the stream or
  memory it passes, a `PdfFileSource` it writes, and the process's environment. A caller that raises a guard, or
  chooses `PdfReaderLimits.Unbounded`, accepts what the raised bound allows ([SECURITY.md](https://github.com/AdCodicem/AdCodicem.Pdf/blob/main/SECURITY.md)).
- **Whoever could alter the package** between this repository and nuget.org, or a dependency on the way in. See
  *The package and its supply chain*.

## Trust boundaries

Where a byte from outside the caller's code enters today:

| Boundary | Where | What crosses |
|---|---|---|
| The file source | `PdfDocument.Open(string)`, `Open(ReadOnlyMemory<byte>)`, `Open(Stream)`, `Open(PdfFileSource, PdfReaderOptions?, bool)` | Every byte of the file, read lazily through windows |
| What the reader derives | `GetObject`, `PdfReference.Resolve`, the `PdfObjectExtensions` accessors, `Trailer`, `Catalog`, `ObjectNumbers`, `PdfStream.GetRawBytes`, `Decode` | Values the file chose, handed to the caller's code |
| Messages | `PdfDiagnostic.Message` and `ToString`, `PdfValidationFinding.Message` | Text that quotes names and keywords from the file |
| The validator | `PdfValidator.Validate` | The same document, plus bytes the validator reads itself: the file's tail, 64 bytes at each entry's offset, object stream headers |

Every surface still to come adds a boundary of its own: HTML, CSS and template data, the resource loader, font
programs, image files, content streams, XML, encryption, signatures and the network, DOCX packages and e-mail.
*Surfaces still to come* lists each one.

## Defenses every surface shares

### The guards

| `PdfReaderLimits` | Default | Code | Where it applies |
|---|---|---|---|
| `MaxDecodedStreamLength` | 256 MB | `limit.decoded-stream` | Every filter's output, each step of a chain separately (`PdfFilterPipeline`) |
| `MaxObjectLength` | 16 MB | `limit.object` | The window a regular object is parsed through, grown ×8 from 8 KB (`PdfFileReader.TryParseNumberedAt`) |
| `MaxXRefSectionLength` | 64 MB | `limit.xref-section-length` | The window a classic cross-reference table is read through |
| `MaxXRefSectionCount` | 1,024 | `limit.xref-section-count` | The `/Prev` links the chain follows |
| `MaxTrailerLength` | 64 KB | `limit.trailer` | A trailer the table's window cut, and a cross-reference stream's dictionary |

Reaching a guard keeps what fits and reports its code (`PdfLimitGuard.Reach`): once per position for an object, a
section or a trailer, and each time a stream is decoded past the bound. When the document was opened with
`ThrowOnLimit`, reaching a guard throws `PdfLimitExceededException` instead. A stream read from a document
decodes under that document's guards, however long after opening. `PdfReaderLimits` refuses a bound of zero or
less and takes a length past `Array.MaxLength` as that length. Held by `ReaderLimitsTests` throughout; the
mutation campaign opens with the defaults and never with `ThrowOnLimit`.

### Iteration, visited sets, and the stack

- **The parser recurses**, one frame per container level. Two things bound it: `PdfObjectParser.MaxDepth` (128)
  and a stack probe at each container. Past either, the container is skipped by counting brackets, with no
  recursion, and reported as `syntax.depth-exceeded`.
- **Loads nest.** An indirect `/Length`, an object stream's dictionary or a filter parameter loads another object
  while one is being parsed. `PdfFileReader.MaxNestedLoads` (64) and a stack probe before each load bound the
  chain; `_loading` turns a load that needs itself into null.
- **Every walk the validator makes across objects is iterative**, with a visited set by object number: the
  object graph, the page tree, the Arlington walk, name and number trees, and the dependencies between object
  streams. The searches inside one object recurse no deeper than the parser nested it.
- **Chains are followed once.** The `/Prev` chain keeps the offsets it has read. `PdfReference.Resolve` follows
  at most `MaxChainLength` (32) references. The index is rebuilt at most once per document (`_repaired`).
- **Reports are made once.** A guard is reported once per `(limit, position)`, a stream's length fault once per
  data start, and a search for a lost `endstream` runs once per data start.

### Everything read is bounded by the source

`PdfFileSource.GetWindow` returns an empty window for a range outside the source and clamps the rest.
`FileSource.Read` loops until the buffer is full or the file ends. A pooled window goes back to its pool at the
end of its `using`, and parsed values are copied out of it, so nothing a caller holds points into a rented buffer.
Held by `DocumentReaderTests.A_source_reads_nothing_outside_itself`,
`A_source_gives_an_empty_window_for_a_range_outside_itself_or_of_no_length`, and every test that reads through
`StrictCountingSource`, which throws on a read past its end.

### Reports

`PdfDiagnostics` keeps `PdfReaderOptions.DiagnosticCapacity` entries (1,000) and counts the rest in
`SuppressedCount`. `PdfValidationReport` keeps `PdfValidatorOptions.FindingCapacity` findings (1,000) and counts
the rest by severity and rule. Both bound the **number** of entries, not their size: see the gaps of *Opening*
and of *The validator*.

### What may be thrown

From a file, only a `PdfException`: `PdfFormatException` for an input that is empty or holds no object,
`PdfEncryptedException`, and `PdfLimitExceededException` when the caller asked for it. Everything else that
escapes is the caller's: an argument it passed, the I/O of its own file or stream, a document it disposed. Any
other exception a file can cause is a defect, and the ones found are in the gaps below.

### What the library never does with a file's content

It opens no path, URL or process that a file names. It loads no type and writes no log. The only host access in
the core is `File.OpenHandle` on the path the caller gives, read-only. A search of `src/` finds no `HttpClient`,
`Process`, `Assembly.Load`, `Type.GetType`, `Activator`, P/Invoke, `Console`, `Trace` or logger.

The only shared mutable state is `PdfName`'s interned table. It is a `ConcurrentDictionary`, so it is safe to
share across threads, but it grows for the life of the process ([#37]). Every other static value is an immutable
record or singleton. A `PdfDocument` is not thread-safe, and nothing detects two threads using one.

<!-- READER -->

<!-- VALIDATOR -->

<!-- HOLDS -->

<!-- SUPPLY -->

## Surfaces still to come

Each surface below is left for the milestone that opens it to write, in the form of the sections above, in the
same change. The table says which milestone opens each surface first and what the specifications decide today.
The last column also names the questions they leave open, which that milestone settles before it writes code.

| Surface | Opens in | Decided by | What the specifications decide, and what they leave open |
|---|---|---|---|
| XMP packets: the first XML the library parses | [M02] slice 5 ([#61]) | ADR 34 | [M03] and [M14] give the parser's settings: DTDs prohibited, no resolver, iterative, size under the stream's guard, a billion-laughs and an external-entity packet read as malformed. Slice 5 comes first and must adopt them. |
| The writer: values the reader recovered, the incremental copy, the files a save writes | [M03] | ADR 6, 13, 18 | Nesting written without recursion past the parser's depth. A lying `/Length` not propagated. A save writes a temporary file beside its target, and saving over the source is refused. `OpenAsync(Stream)` buffers a non-seekable stream whole, with no bound stated. |
| Revisions and signature coverage: `/ByteRange`, `/Contents` | [M04] | ADR 18 | Byte ranges checked against the file before use, none sizing an allocation. A revision opened on a slice that ends at its end. Iterative walks. |
| Repair | [M05] | ADR 22 | Hostile documents end in a report or a typed exception within time and allocation budgets. No bounds table yet. |
| Copying objects between documents, catalog structures, embedded files and portfolios | [M06], [M07] | ADR 17, 37 | Copies never follow `/Parent`, `/P` or outline links, with an explicit stack. Attachments are streamed under the source's guards. Open: how a file name from the PDF becomes an output path in the tool's `attachments … extract`, and the depth bound on nested portfolios, whose value is not given. |
| Image files as pages: JPEG, JPEG 2000, TIFF, PNG | [M07] | ADR 42 | Container parsers in the core, bounded and fuzzed. Open: `PdfImagePageOptions` carries no limits record for a file read outside any document. |
| Content streams: tokenizing, rewriting, interpreting | [M07], [M08], [M15] | ADR 15, 34 | Guards on operations, glyphs and graphics state depth arrive with the interpreter ([M15]). Open: received content rewritten through `PdfContentBuilder`, whose state machine throws `InvalidOperationException` on sequences damaged pages hold. |
| Font programs: sfnt, CFF charstrings, WOFF, WOFF2, Type 1, CMaps | [M08] | ADR 11, 34, 43 | `MaxFontLength` (64 MB) and `MaxCharstringOperations` (65,536) join `PdfReaderLimits` (ADR 34, amended). Composite glyphs are walked iteratively, and the WOFF2 sizes are checked before allocation. Fonts are fuzzed nightly. |
| Barcode payloads, and `barcode:` URLs from template data | [M10], [M12] | ADR 38 | Payloads end in the typed exception or a parse error within a time and allocation budget. |
| Annotations, rich text (`/RC`) and optional content | [M11] | ADR 34, 37 | `/RC` is parsed with `XmlReader`, DTDs prohibited. Walks are iterative with visited sets. |
| HTML and CSS | [M12] | ADR 2, 4, 37, 38 | Scripting disabled. Guards on box depth, pages, imports, custom property expansion and selector cost. Open: the length of a `@counter-style` representation, and the source's length when it is given as one string. |
| The resource loader and web fonts | [M12] | ADR 11, 38 | Deny by default. Addresses are checked after name resolution, redirects are kept within an allowed origin, and `file:` is confined to a root. Resource guards apply. Open: how `CachingResourceResolver`, keyed by URL, composes with each render's policy, and which reader options a PDF fetched as a page image opens under. |
| Native code in `AdCodicem.Pdf.Html`: HarfBuzz reading fonts, Skia decoding images | [M12] | ADR 5, 43 | Images are checked against `MaxImagePixels` by our own header parser before Skia sees them. Open: the tables HarfBuzz reads and M08's parser does not (GSUB, GPOS, morx) are neither validated nor bounded. [M25] keeps every font program away from native parsers. |
| SVG | [M12] | ADR 37, 38 | `MaxSvgElements`, DTDs prohibited in referenced files. Open: group nesting depth, and cycles among `clipPath`, `mask`, `pattern` and `marker`. |
| Factur-X, CII, UBL and XRechnung XML | [M14] | ADR 9, 37 | XMP's reader settings, schemas from embedded copies only. Open: the third-party model's own XML settings, and escaping in the HTML rendition. |
| Markup the library generates from file text: Markdown, JSON, HTML renditions; regular-expression search | [M15] | ADR 15, 38 | Text escaped so it cannot make structure; `NonBacktracking` by default. Open: [M31] does not yet state escaping for the values it places into CSS. |
| ICC profiles | [M15], [M29] | ADR 34, 42 | Bounded header and tag-table readers, CLUT sizes computed in 64 bits before allocation, and fuzzing over fourteen nights. |
| Encryption and passwords | [M16] | ADR 41 | `/O`, `/U`, `/OE`, `/UE`, `/Perms` and `/Length` are checked before use, and decryption runs block by block through pooled buffers. Open: whether passwords and file keys are cleared from memory and kept out of exceptions and diagnostics. The managed MD5, RC4 and AES are not constant-time. |
| Forms, scripts and XFA; FDF and XFDF | [M16] | ADR 34, 37 | Scripts are recognized by pattern and never run. XFA and XFDF are parsed with `XmlReader`, DTDs prohibited. Field trees are walked iteratively. |
| E-mail: EML and MSG | [M18] | ADR 34, 38 | `EmailLimits` guards. CFB chains cut at the sector count, LZFu sizes checked before allocation, every remote reference refused. |
| Redaction and sanitization | [M19] | ADR 22, 37 | Always a full rewrite. A stream cut at a guard fails verification rather than passing it. |
| Conformance profiles over hostile documents | [M20], [M28] | ADR 36, 45 | Bounded header readers; the engine proved total by FsCheck. |
| Received documents converted to PDF/A | [M21] | ADR 22, 41 | `MaxAttachmentDepth` (3), with the attached streams' hashes. |
| Image codecs: CCITT, JBIG2, JPEG, JPEG 2000; color spaces and functions | [M22] | ADR 34, 35, 42 | `MaxImageWorkingSet`, `MaxJpegScans`, JBIG2 counts in 64 bits, a bounded PostScript stack, every decoder fuzzed. Open: the cost of a type 4 function evaluated per sample, and the size of the lookup table for an n-input `DeviceN`. |
| OCR text layers: hOCR, ALTO, TSV | [M22] | ADR 42 | `XmlReader` with DTDs prohibited; `MaxInputLength` 64 MB, typed, since it is the caller's input. |
| Optimization, the piecewise decode, hardening | [M23] | ADR 34, 35 | Cancellation within 100 ms on hostile input, the cache weighed in bytes, coverage-guided fuzzing, the container profile. |
| Comparison and templates | [M24] | ADR 15 | Bounded edits per window, `NonBacktracking`. |
| Rasterization | [M25] | ADR 16, 42, 43 | `MaxRasterPixels`, band and layer bytes, bounded mesh subdivision. No font program or codec reaches Skia. |
| Signing: TSA and remote-signing responses, deferred requests | [M26] | ADR 18, 41 | Network off by default, responses read up to a bound, the prepared file checked again before the second step. |
| Signature validation: CMS, X.509, OCSP, CRL, trusted lists, and the URLs they name | [M27] | ADR 41 | OCSP and CRL parsed by our own `AsnReader` code under explicit bounds, fuzzed from the day they are written. Path building uses a visited set. Open: no address, scheme or redirect policy is stated for the URLs a signed file names (caIssuers, OCSP, CRL). ADR 38's rules cover only the HTML engine. |
| DOCX packages | [M31] | ADR 37, 38 | ZIP limits checked before reading, part names validated, DTDs refused in every part, external relationships refused by default. |
| JSON inputs: plans, models, policies, templates, requests | [M06] onward | ADR 8 | `Utf8JsonReader` and source generation, no reflection. Open: several specifications state no depth or size bound. |

## Out of scope

- **Active content** ([ADR 37](adr/0037-out-of-scope-active-content-and-pdf-to-office.md)). JavaScript is never
  executed, dynamic XFA never rendered, 3D and rich media never authored. A file that carries them is read,
  validated and manipulated like any other, and what cannot be kept is reported.
- **The caller's own code and choices.** A `PdfFileSource`, a resolver, a codec, an OCR engine, a key source or an
  `HttpClient` the caller supplies is trusted. A caller that raises a guard, or chooses `Unbounded`, lets a file
  use what those bounds allow. That is by design, not a vulnerability.
- **A file changed under an open document.** `Open(string)` shares the file for reading, which Unix treats as
  advisory, and `Open(ReadOnlyMemory<byte>)` reads the caller's memory in place. Bytes that change after opening
  are read as they are then. A caller who cannot keep them still copies them first.
- **Concurrent use of one `PdfDocument`.** It is not thread-safe, as its documentation says, and nothing detects
  two threads using it.
- **Hard limits on a process.** The library does not sandbox itself. Until [M23]'s container profile, a host
  that must bound time or memory absolutely runs the library in a process or container it can stop.
- **What a page shows as opposed to what the file says.** The reader returns what the file holds. Whether a
  page's appearance misleads (an overlay, a field over signed text, white text) is for the milestones that
  render, compare or judge signatures to report.
- **Timing side channels.** None exists today. [M16] records that its managed MD5, RC4 and AES are not
  constant-time.

## Keeping this document

- A change that opens or widens a surface completes its section in the same pull request (`CLAUDE.md`). A new
  bound is classified when it is added (ADR 34). A defense that is missing is filed as a `debt` issue and named
  under *Known gaps*, never written as if it held.
- A defense named here is one that was read in the code. A test named here is one that was read.
- A milestone's review checks the code against this document (`docs/milestone-review.md`, axis 5). Any
  difference it finds is a finding.

[#61]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/61
[M02]: milestones/M02.md
[M05]: milestones/M05.md
[M06]: milestones/M06.md
[M07]: milestones/M07.md
[M10]: milestones/M10.md
[M11]: milestones/M11.md
[M14]: milestones/M14.md
[M15]: milestones/M15.md
[M18]: milestones/M18.md
[M19]: milestones/M19.md
[M20]: milestones/M20.md
[M21]: milestones/M21.md
[M22]: milestones/M22.md
[M24]: milestones/M24.md
[M28]: milestones/M28.md
[M29]: milestones/M29.md
[M31]: milestones/M31.md

[#37]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/37
[#135]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/135
[M03]: milestones/M03.md
[M04]: milestones/M04.md
[M08]: milestones/M08.md
[M12]: milestones/M12.md
[M16]: milestones/M16.md
[M23]: milestones/M23.md
[M25]: milestones/M25.md
[M26]: milestones/M26.md
[M27]: milestones/M27.md
