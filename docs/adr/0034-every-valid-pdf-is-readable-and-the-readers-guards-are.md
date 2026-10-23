# 34. Every valid PDF is readable, and the reader's guards are options

Date: 2026-09-26

## Status

Accepted on 2026-09-26. The rule is the maintainer's, stated when the 256 MB decoding bound turned out to be
the library's own choice rather than the format's: *every PDF that is valid under the specification must be
readable; guards may protect against exceptional cases the specification allows but that could be
dangerous, and options must be able to lift them.* It is invariant 12 in `CLAUDE.md`. It extends
[14](0014-a-tolerant-reader-with-a-diagnostic-report.md), whose tolerant reader bounded what it read without
saying which bounds a sound file could reach, and follows [8](0008-facade-immutable-options-dependency-injection.md)
in making them options on an immutable record.

Implemented by `PdfReaderLimits`, `PdfReaderOptions.Limits` and `PdfReaderOptions.ThrowOnLimit`,
`PdfLimitExceededException`, the eight `limit.*` codes of `PdfDiagnosticCodes`, the `readerLimits` field of
`tests/corpus/manifest.json`, and `ReaderLimitsTests`.

Amended on 2026-09-27, by the maintainer, while M08 was specified: one record bounds every hostile read, the
font programs a caller registers included (*One record for every hostile read*, below); M08 implements it.

Amended on 2026-10-01, by the maintainer, while [#159](https://github.com/AdCodicem/AdCodicem.Pdf/issues/159) was
fixed: a bound on what the library repeats of a value it read, rather than on what it reads, changes nothing a file
can make it read, and is an internal constant even where a valid file reaches it. A message quotes at most 127 bytes
of a name (`FileQuote.MaxBytes`), though PDF 2.0 allows a longer one, and at most the first and last four steps of a
key path (`RuleText.PathEnds`); the whole value stays in the document. Such a constant's declaration says so, as
`No guard (ADR 34): it bounds what a message repeats, not what is read`, in place of the reason no valid file
reaches it. `PdfObjectParser.EndObjLookahead`, which bounded what the reader saw of an object's end, was the first; the
2026-10-23 amendment made it the size of a first read, and no bound.

Amended on 2026-10-14, by the maintainer, while [#164](https://github.com/AdCodicem/AdCodicem.Pdf/issues/164) and
[#183](https://github.com/AdCodicem/AdCodicem.Pdf/issues/183) were fixed: a sixth guard, `MaxIndexEntries`
(8,388,607, `limit.index-entries`), bounds the object numbers the index holds, whatever gives them — a section's rows,
a rebuild's headers, an object stream a rebuild expands. Nothing bounded them before: a cross-reference stream of
one-byte rows decoding to 256 MB from a few hundred bytes asked for 268 million entries, and two internal constants
said to bound the rest did not — a rebuild counted headers rather than objects, and stopped silently behind two million
copies of one; a subsection's count bounded none of the rows an object stream adds. Both constants are folded into
the guard, and the sentence of the context that classified them is struck. And what a guard leaves out of the index
is no longer rebuilt as damage: it reads as null, unknown rather than missing, until the property is raised
(*Reaching a guard*, below).

Amended again on 2026-10-14, by the maintainer, while [#165](https://github.com/AdCodicem/AdCodicem.Pdf/issues/165),
[#166](https://github.com/AdCodicem/AdCodicem.Pdf/issues/166) and [#199](https://github.com/AdCodicem/AdCodicem.Pdf/issues/199)
were fixed: `MaxDecodedStreamLength` bounded each filter of a chain alone, so that a stream cost what its `/Filter` array
asked — a 513-byte cross-reference stream naming `/RunLengthDecode` sixteen times allocated 3.3 GB as it opened, and a
sound chain of stored Flate layers 14.9 GB with nothing reported. The filters before the last now share one bound for what
they pass along together, and the last keeps its own: a stream of one or two filters decodes as before, and no count of
filters becomes a bound, since ISO 32000 sets none. What follows a filter that stopped at the bound is data the reader cut,
reported once as the guard, never as damage. And the reason given for `MaxNestedLoads` was false: a filter's parameters
can name an object an object stream holds, and decoding one stream could decode another while holding its own output, 64
deep. A stream now resolves its filters and every parameter they read before any of its data, so that no level of a load
chain holds decoded data while the next loads; that is what keeps the bound an internal constant.

Amended on 2026-10-15, by the maintainer, during the review of the same change: no count of filters was a bound, yet
each filter costs decoding something whatever its data — a few microseconds, and for LZW a table of 42 KB —, and a
filter whose data decodes to itself never runs out of it. A `/Filter` array of forty million names, which an object
stream's member holds in a file of 350 KB, cost 1.8 s on every decode, and filters that never run out of data could cost
minutes. A seventh guard, `MaxFilterChainLength` (64, `limit.filter-chain`), bounds the filters a stream's data passes
through, counted as its `/Filter` array names them: decoding stops before the first past it, as at a filter the library
does not know, and nothing past it is read. Real streams name one to three. And a `/Crypt` filter, which passes its data
along as it is, is neither charged to the bound the filters before the last share nor the last of them: an encrypted
stream, whose first filter ISO 32000 makes `/Crypt`, decodes as its twin in the clear does. M16 takes it up again when
`/Crypt` decrypts.

Amended again on 2026-10-15, by the maintainer, while [#144](https://github.com/AdCodicem/AdCodicem.Pdf/issues/144) was
fixed: a stream was reported each time it was decoded past the bound, and the reader decodes an object stream again once
its budget let it go, a rebuild once more, and the validator once more: with `MaxDecodedStreamLength` at 1 KB, 56 corpus
documents held a guard's report twice or three times, as a damaged object stream held its filter's fault. A document's
own diagnostics now hold a stream's report once, however often the reader, the validator or a caller decodes it, as they
hold an object's; diagnostics a caller passes to `Decode` hold every decoding's. The guard is still counted each time it is
reached, so that what it cut is never taken for damage, and a document opened to throw throws each time.

Amended on 2026-10-21, by the maintainer, while [#160](https://github.com/AdCodicem/AdCodicem.Pdf/issues/160) and
[#200](https://github.com/AdCodicem/AdCodicem.Pdf/issues/200) were fixed: `MaxObjectLength` bounds an object stream's
member as it bounds an object written in the file — a member of millions of nested arrays was parsed whole, whatever the
guard —, and is reported once for each member it cuts. What an object stream's header says is bounded by internal
constants, each because ISO 32000-1 (7.5.7) asks it of every valid file: a member is read no further than the next
member's start, the last no further than the end of the data; a pair whose offset is not past the one before it, or whose
member would start past the data, ends the header as a stray token does. And a header lists at most a quarter of its
`/First` bytes and one, whatever its `/N` claims: a count of what its bytes can hold, not a bound, its tables growing with
the pairs read. The consequence that raising `MaxDecodedStreamLength` multiplies what the cache of decoded object streams
holds until M23 bounds it (T33) was stale: `ObjectStreamBudget` bounds it since 2026-09-28.

Amended on 2026-10-23, by the maintainer, while [#170](https://github.com/AdCodicem/AdCodicem.Pdf/issues/170),
[#174](https://github.com/AdCodicem/AdCodicem.Pdf/issues/174), [#220](https://github.com/AdCodicem/AdCodicem.Pdf/issues/220),
[#224](https://github.com/AdCodicem/AdCodicem.Pdf/issues/224), [#230](https://github.com/AdCodicem/AdCodicem.Pdf/issues/230)
and [#263](https://github.com/AdCodicem/AdCodicem.Pdf/issues/263) were fixed: an eighth guard, `MaxStreamLength` (256 MB,
`limit.stream-length`), bounds what is read of one stream's encoded data at once. The file alone bounded it: decoding
handed back data no filter decodes — none named, an image filter, one the library does not know, Flate over data that is
none — whole, past `MaxDecodedStreamLength` and unreported, and a `/Length` past what an array holds threw an
`OutOfMemoryException` each time the data was read. The data is now kept by nothing and read each time it is asked for,
so the guard bounds each read: past it the data is cut, what the filters lose past the cut is no damage, the stream is
reported once in its document's diagnostics and, as `limit.decoded-stream` is, with each decoding into diagnostics a
caller passes to `Decode`, and its length stays the one the file holds. `MaxDecodedStreamLength` keeps bounding what the
filters produce, so an uncompressed image past the default needs the new guard raised alone. Nor does a stream's data
count toward `MaxObjectLength`: a `/Length` the file cannot hold grew the window an object is parsed through up to that
bound, reading 21.5 MB to find five bytes and reporting `limit.object`; no window grows to find an `endstream` now, and
one search past the window does. What that search reads, with the white space read on after a declared length, is
bounded by `EndStreamSearchPasses`, four times the file for a document's searches together, which stays an internal
constant with its reason rewritten: a valid file is read past the parser's window only by the search for the data of a
cross-reference stream whose `/Length` the chain cannot read, by the search for the data of a stream whose `/Length` a
guard cut or left out of the index, and by white space and comments after a declared length that run past a first
read — about twice the file at most. `MaxEndStreamGap`, the four bytes of white space accepted
between a stream's data and its `endstream`, is removed: more, or a comment, made a right `/Length` a wrong one, and
white space and comments are read on as far as they run, counted with the searches — a comment only once white space
comes before it, since a `%` right where the length ends is the data's. Comments after the `endobj` that ends a stream
with no `endstream` are white space too, read through the search's own reads: neither rule adds a bound.
`EndObjLookahead` is the size of that first read, no longer a bound. `MaxTrailerLength` bounds a cross-reference
stream's dictionary alone: the data of one whose `/Length` the chain cannot read is searched for its `endstream` in the
file rather than through a window the guard bounded — a stream of 70,000 bytes of rows reads as its twin with a direct
`/Length`, with no `limit.trailer`, and `ThrowOnLimit` no longer throws for it —, and data no `endstream` follows before
the next object the sections read so far place, or the end of the file, makes its section malformed: the file's fault,
and no guard's, unless that `/Length` leads to an object `MaxObjectLength` cut (below). A value read through a reference
to an object a guard cut or left out of the index — a stream's `/Length`, `/Filter` or `/DecodeParms`, a value of a
filter's parameters, a cross-reference stream's `/W`, `/Index` or `/Size`, a trailer's `/Prev` or `/XRefStm` — is the
guard's, not the file's: an integer the cut parted, `1` of `15`, is only a place to look for `endstream` at, never a
declared length, though a stream that lacks its `endstream` is still the file's fault; a chain of filters ends there,
its data left encoded; and a cross-reference section whose values lead to an object `MaxObjectLength` cut ends the
chain, nothing rebuilt, the older sections and their trailers the guard's to hide, and the guard reached once the chain
is read. So does a cross-reference stream's `/Length` the chain could not read that leads to such an object, wherever
only that length could say how far the data runs: no `endstream` comes before the next object or the end of the file, or
the rows spell one and none confirms what the cut left of the length, the rows past it then the guard's. A guard that
loading a value of a stream's filters reaches is that value's, not a cut of the stream's own data, whose faults it hid.
No bound is added for those values, nor for the next object's header that ends an object's open containers: no valid
object holds `obj` where a value is expected.

## Context

Invariant 4 treats everything read from a file as hostile: no allocation, loop or recursion is sized by a
value the file chose without a checked bound. The reader has a dozen such bounds. They are of two kinds, and
until now nothing told them apart.

**Bounds only an invalid or hostile file reaches.** A chain of objects loaded while loading another is
bounded at 64 (T29): an indirect `/Length` must lead to an integer, and the filters of a stream and the parameters they
read resolve before any of its data is decoded, so that no level holds decoded data while the next loads (the second
2026-10-14 amendment). Containers
nest at most 128 deep, and references to references are followed 32 times. These bounds never refuse
a document the specification allows in practice, and a caller has nothing to gain from lifting them. (The
first version of this record listed two more here, on the objects a rebuild indexes and the entries a subsection
claims; neither bounded what it was said to, and the 2026-10-14 amendment made them one guard.)

**Bounds a valid file can exceed**, because ISO 32000 sets no limit where the reader set one:

| Bound | Value | What a valid file can do |
|---|---|---|
| A stream's decoded length, its filters before the last sharing one bound for what they pass along together, a `/Crypt` filter neither charged nor the last | 256 MB | The USGS topographic map in the remote corpus holds one image, 9,600 × 11,410 RGB, that decodes to 313 MB; qpdf reads it whole. A chain of filters whose outputs together pass the bound is valid too, and qpdf reads three thousand of them |
| A stream's encoded data, read at once | 256 MB | ISO 32000 sets no limit on a stream's length, and decoding hands back data no filter decodes as it is: the USGS map's image, stored without a filter, would be 313 MB of data |
| An object's length, streams' data aside | 16 MB | A direct array or dictionary of that size: a page-tree node with a million kids |
| A classic cross-reference section | 64 MB | About 3.3 million entries; the informative annex C of ISO 32000-1 gives Acrobat's own limit as 8,388,607 objects |
| The number of cross-reference sections | 1,024 | A document saved incrementally more than a thousand times |
| A trailer | 64 KB | A trailer is a few hundred bytes in practice; nothing forbids more. A cross-reference stream's dictionary is its trailer: its `/Index` holds two numbers per subsection, and a document updated in many scattered places has thousands |
| The entries of the index | 8,388,607 | The informative annex C of ISO 32000-1 gives Acrobat's limit as 8,388,607 objects, and PDF/A-1 to 3 make it normative; PDF 2.0 sets none, and PDFium raised its own past it for documents of about six million. Each entry the index holds costs 52 to 108 bytes, so the default holds 436 to 906 MB of index |
| The filters a stream's data passes through | 64 | ISO 32000 sets no limit on how many filters a `/Filter` array names; real streams name one to three, `/Crypt` among them, and qpdf reads three thousand |

None of these was an option. Until T21, T23 and T31 some were not even reported, or were reported as damage
in the file: the map's image came out as "a Flate stream was truncated".

The trailer's bound was not even one for a cross-reference stream: its dictionary was parsed through one
window of 64 KB that never grew, so a dictionary longer than that left the index to a rebuild, with no word
of why.

One ceiling is the implementation's rather than a choice: a decoded stream is returned as
`ReadOnlyMemory<byte>`, which holds at most `Array.MaxLength` bytes, about 2 GB. Past it only a decode that
hands its output out a piece at a time can read the stream (T28).

## Decision

We will read every PDF that is valid under ISO 32000, and treat each bound a valid file can exceed as a
guard: active by default, reported when it is reached, and lifted by an option.

- **The options.** `PdfReaderOptions.Limits` is a `PdfReaderLimits`, an immutable record with the eight bounds
  of the table: `MaxDecodedStreamLength`, `MaxStreamLength`, `MaxObjectLength`, `MaxXRefSectionLength`, `MaxXRefSectionCount`,
  `MaxTrailerLength`, `MaxIndexEntries` and `MaxFilterChainLength`. `PdfReaderLimits.Default` keeps the values above;
  `PdfReaderLimits.Unbounded` takes the implementation's maximums — `Array.MaxLength` for a length,
  `int.MaxValue` for a count. A value of zero or less is refused; a value above the implementation's maximum is
  taken as that maximum.
- **The defaults stay on.** An application that opens files it did not produce is protected without
  configuring anything; one that reads a sound but exceptional file asks for it, by raising one bound or by
  choosing `Unbounded`.
- **Reaching a guard** keeps what fits within it, as the reader does today, and reports it as a warning
  under a code of its own — `limit.decoded-stream`, `limit.stream-length`, `limit.object`, `limit.xref-section-length`,
  `limit.xref-section-count`, `limit.trailer`, `limit.index-entries`, `limit.filter-chain` — whose message names the property that
  lifts it. What the parser met at the cut is the reader's, not the file's, and is dropped in favor of the guard; so is a
  value that leads to an object a guard cut or left out of the index (amended on 2026-10-23). An object, a section or a
  trailer is reported once, however often it is read again; a stream, by the first of its filters that stopped there,
  each time it is decoded past the bound into diagnostics a caller supplies, and once in its document's own, however
  often it is decoded (amended on 2026-10-15); a stream's data `MaxStreamLength` cut, with each decoding into
  diagnostics a caller supplies, and once in its document's own, as the reader reads it (amended on 2026-10-23); a full
  index, once a document. The filters after one a guard stopped, and those of data `MaxStreamLength` cut, are given data
  the reader cut: that it runs out — a lost tail, a missing checksum, a digit without its pair, a row the predictor
  cannot finish — is not reported, and a fault in what was read is. The code `filter.limit-exceeded`, added by T31 and
  never released, becomes `limit.decoded-stream`.
- **What a guard leaves out of the index is not damage** (amended on 2026-10-14). The objects only a section past
  `MaxXRefSectionCount`, rows past `MaxXRefSectionLength`, `MaxStreamLength` or `MaxDecodedStreamLength`, or numbers past
  `MaxIndexEntries` index read as null, and the validator treats them as unknown rather than missing, until the
  property is raised: the index is not rebuilt for them, which turned a sound file into an `xref.rebuilt` error
  (ADR 45) and could lose what the chain had read. A classic table cut by its length keeps its trailer. The chain ends
  at a cut — a table's, a stream's rows, a section or a trailer cut wherever the cut falls, a cross-reference stream
  whose `/W`, `/Index`, `/Size`, `/Filter` or `/DecodeParms` leads to an object `MaxObjectLength` cut, or whose
  `/Length` does where only it could say how far the data runs, a `/Prev` or an `/XRefStm` that does (amended on
  2026-10-23) —, since an older section, or its trailer, would stand for what the cut left out; and a section a guard
  cut is where it was named, not looked for near there. A rebuild that damage forces on such a chain keeps the entries
  the chain read; one the chain cannot do without — a guard that leaves it no section — runs as before.
- **Or it throws.** `PdfReaderOptions.ThrowOnLimit`, false by default, turns every guard reached into a
  `PdfLimitExceededException`, a `PdfException` carrying the code and the name of the property: from
  `PdfDocument.Open` when it is reached while opening, from `GetObject`, `GetRawBytes` or `Decode` when it is reached later.
  A rebuild of the index that reaches a guard finishes first and throws after, since a rebuild is never run
  twice; each later lookup of an object a full index left out throws again. `Open` releases a source it was given
  to own when it throws.
- **A stream decodes under its document's settings.** A stream read from a document decodes under that
  document's limits and `ThrowOnLimit`, however long after opening; a stream built in memory decodes under
  the defaults.
- **One record for every hostile read.** A bound on bytes the core parses on a caller's behalf joins
  `PdfReaderLimits` under the same rules, whatever the bytes arrive in: M08 adds `MaxFontLength` (64 MB,
  `limit.font-length`) and `MaxCharstringOperations` (65,536, `limit.charstring-operations`), which govern a
  font program read from a PDF under its document's limits and one a caller registers under the limits the
  registry is given. A record of its own (`PdfFontLimits`) is rejected: a font inside a PDF would answer to
  two, and a caller would raise two records to read one file.
- **Bounds only an invalid or hostile file reaches stay internal constants**, each with the reason it cannot
  refuse a valid document. A new bound is classified when it is added: if a valid file can reach it, it
  becomes an option, a code and a test.
- **The corpus proves it.** A corpus entry may give the limits it is opened with, in `readerLimits`; the USGS
  map is opened with a decoded-stream bound of 512 MB and must read whole. Every other document is tested
  with the defaults.
- **Past the implementation's ceiling the rule is not yet met.** A stream that decodes past about 2 GB needs
  a decode that yields its output a piece at a time; that is T28, for M23, and until then
  `limit.decoded-stream` reports it even under `Unbounded`. A stream's encoded data, read in one piece, has the
  same ceiling: `limit.stream-length` reports data past it even under `Unbounded` (amended on 2026-10-23).

## Consequences

- A caller holding a valid but exceptional file can read it, and learns from the report which property to
  raise; a caller holding untrusted input keeps every protection without knowing it has one.
- Every bound now needs a decision, recorded where it is declared: an option with its code and its test, or
  an internal constant with the reason no valid file reaches it. The option surface grows with the reader.
- The defaults are judgments about what is exceptional, and one is already known to be crossed by a real
  document: a large-format scan decodes past 256 MB. They can be revised without breaking a caller, who may
  always set them.
- With `ThrowOnLimit`, any lazy operation — resolving an object, reading or decoding a stream — may throw after `Open`
  returned; a caller who sets it must be ready for that wherever the document is used.
- `Unbounded` is for documents the application trusts. Raised guards let a file make the reader hold what
  it asks for, up to the new bounds; the cache of decoded object streams keeps within `ObjectStreamBudget` but always
  holds the stream decoded last, so raising `MaxDecodedStreamLength` raises what that one can hold.
- A classic trailer that ends within the window its table was read through is read whole whatever its
  length: the guard bounds how far the reader follows one past that window, which is where the cost is.
- **Rejected** — reading everything by default and making the guards opt-in: every service that accepts
  uploaded PDFs would be exposed to decompression bombs until someone read this record.
- **Rejected** — flat properties on `PdfReaderOptions`: six related bounds and their presets belong
  together, and the options record would grow with every bound.
- **Rejected** — one code for every guard: a caller could not tell, without parsing a message, which bound to
  raise before trying again.
- **Rejected** — exceptions only: a document whose one huge image is cut would become a document that cannot
  be opened at all.
- **Amended on acceptance**: invariants 4 and 5 of `CLAUDE.md`, and invariant 12 added; `ARCHITECTURE.md`,
  `docs/architecture.md` and `SECURITY.md` on bounds; `docs/corpus.md`, `docs/corpus-contributions.md` and
  `tests/corpus/README.md` for `readerLimits`; the site's *Diagnostics* and *Lazy reading* pages, and a new
  page, *Reader limits*; the M02 stream rule and M23's deliverables.
- **What would reopen it**: a streaming decode (M23), after which `MaxDecodedStreamLength` would bound memory
  held at once rather than the length of a stream; or evidence that most callers read trusted input, which
  would argue for `Unbounded` as the default.
