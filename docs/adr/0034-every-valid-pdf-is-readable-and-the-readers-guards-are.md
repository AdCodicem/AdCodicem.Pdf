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
`PdfLimitExceededException`, the five `limit.*` codes of `PdfDiagnosticCodes`, the `readerLimits` field of
`tests/corpus/manifest.json`, and `ReaderLimitsTests`.

## Context

Invariant 4 treats everything read from a file as hostile: no allocation, loop or recursion is sized by a
value the file chose without a checked bound. The reader has a dozen such bounds. They are of two kinds, and
until now nothing told them apart.

**Bounds only an invalid or hostile file reaches.** A chain of objects loaded while loading another is
bounded at 64 (T29): an indirect `/Length` must lead to an integer, which loads nothing further. Containers
nest at most 128 deep, and references to references are followed 32 times. A rebuild indexes at most two
million objects, and a subsection may not claim more than fifty million entries. These bounds never refuse
a document the specification allows in practice, and a caller has nothing to gain from lifting them.

**Bounds a valid file can exceed**, because ISO 32000 sets no limit where the reader set one:

| Bound | Value | What a valid file can do |
|---|---|---|
| A stream's decoded length | 256 MB | The USGS topographic map in the remote corpus holds one image, 9,600 × 11,410 RGB, that decodes to 313 MB; qpdf reads it whole |
| An object's length, streams' data aside | 16 MB | A direct array or dictionary of that size: a page-tree node with a million kids |
| A classic cross-reference section | 64 MB | About 3.3 million entries; the informative annex C of ISO 32000-1 gives Acrobat's own limit as 8,388,607 objects |
| The number of cross-reference sections | 1,024 | A document saved incrementally more than a thousand times |
| A trailer | 64 KB | A trailer is a few hundred bytes in practice; nothing forbids more. A cross-reference stream's dictionary is its trailer: its `/Index` holds two numbers per subsection, and a document updated in many scattered places has thousands |

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

- **The options.** `PdfReaderOptions.Limits` is a `PdfReaderLimits`, an immutable record with the five bounds
  of the table: `MaxDecodedStreamLength`, `MaxObjectLength`, `MaxXRefSectionLength`, `MaxXRefSectionCount`
  and `MaxTrailerLength`. `PdfReaderLimits.Default` keeps the values above; `PdfReaderLimits.Unbounded`
  takes the implementation's maximums — `Array.MaxLength` for a length, `int.MaxValue` for the count. A
  value of zero or less is refused; a value above the implementation's maximum is taken as that maximum.
- **The defaults stay on.** An application that opens files it did not produce is protected without
  configuring anything; one that reads a sound but exceptional file asks for it, by raising one bound or by
  choosing `Unbounded`.
- **Reaching a guard** keeps what fits within it, as the reader does today, and reports it as a warning
  under a code of its own — `limit.decoded-stream`, `limit.object`, `limit.xref-section-length`,
  `limit.xref-section-count`, `limit.trailer` — whose message names the property that lifts it. What the
  parser met at the cut is the reader's, not the file's, and is dropped in favour of the guard. An object,
  a section or a trailer is reported once, however often it is read again; a stream, each time it is decoded
  past the bound, and in its document's diagnostics when the caller supplied none. The code
  `filter.limit-exceeded`, added by T31 and never released, becomes `limit.decoded-stream`.
- **Or it throws.** `PdfReaderOptions.ThrowOnLimit`, false by default, turns every guard reached into a
  `PdfLimitExceededException`, a `PdfException` carrying the code and the name of the property: from
  `PdfDocument.Open` when it is reached while opening, from `GetObject` or `Decode` when it is reached later.
  A rebuild of the index that reaches a guard finishes first and throws after, since a rebuild is never run
  twice; `Open` releases a source it was given to own when it throws.
- **A stream decodes under its document's settings.** A stream read from a document decodes under that
  document's limits and `ThrowOnLimit`, however long after opening; a stream built in memory decodes under
  the defaults.
- **Bounds only an invalid or hostile file reaches stay internal constants**, each with the reason it cannot
  refuse a valid document. A new bound is classified when it is added: if a valid file can reach it, it
  becomes an option, a code and a test.
- **The corpus proves it.** A corpus entry may give the limits it is opened with, in `readerLimits`; the USGS
  map is opened with a decoded-stream bound of 512 MB and must read whole. Every other document is tested
  with the defaults.
- **Past the implementation's ceiling the rule is not yet met.** A stream that decodes past about 2 GB needs
  a decode that yields its output a piece at a time; that is T28, for M13, and until then
  `limit.decoded-stream` reports it even under `Unbounded`.

## Consequences

- A caller holding a valid but exceptional file can read it, and learns from the report which property to
  raise; a caller holding untrusted input keeps every protection without knowing it has one.
- Every bound now needs a decision, recorded where it is declared: an option with its code and its test, or
  an internal constant with the reason no valid file reaches it. The option surface grows with the reader.
- The defaults are judgements about what is exceptional, and one is already known to be crossed by a real
  document: a large-format scan decodes past 256 MB. They can be revised without breaking a caller, who may
  always set them.
- With `ThrowOnLimit`, any lazy operation — resolving an object, decoding a stream — may throw after `Open`
  returned; a caller who sets it must be ready for that wherever the document is used.
- `Unbounded` is for documents the application trusts. Raised guards let a file make the reader hold what
  it asks for, up to the new bounds; and until M13 bounds the cache of decoded object streams (T33), raising
  `MaxDecodedStreamLength` multiplies what that cache can hold.
- A classic trailer that ends within the window its table was read through is read whole whatever its
  length: the guard bounds how far the reader follows one past that window, which is where the cost is.
- **Rejected** — reading everything by default and making the guards opt-in: every service that accepts
  uploaded PDFs would be exposed to decompression bombs until someone read this record.
- **Rejected** — flat properties on `PdfReaderOptions`: five related bounds and their presets belong
  together, and the options record would grow with every bound.
- **Rejected** — one code for every guard: a caller could not tell, without parsing a message, which bound to
  raise before trying again.
- **Rejected** — exceptions only: a document whose one huge image is cut would become a document that cannot
  be opened at all.
- **Amended on acceptance**: invariants 4 and 5 of `CLAUDE.md`, and invariant 12 added; `ARCHITECTURE.md`,
  `docs/architecture.md` and `SECURITY.md` on bounds; `docs/corpus.md`, `docs/corpus-contributions.md` and
  `tests/corpus/README.md` for `readerLimits`; the site's *Diagnostics* and *Lazy reading* pages, and a new
  page, *Reader limits*; the M2 stream rule and M13's deliverables.
- **What would reopen it**: a streaming decode (M13), after which `MaxDecodedStreamLength` would bound memory
  held at once rather than the length of a stream; or evidence that most callers read trusted input, which
  would argue for `Unbounded` as the default.
