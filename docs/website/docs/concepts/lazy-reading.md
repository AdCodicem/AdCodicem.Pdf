---
title: Lazy reading
sidebar_position: 1
description: What opening a document does and deliberately does not do, what that costs, and what it measured.
---

# Lazy reading

Opening a document builds an index, finds the catalog, and stops there.

## What opening actually does

1. Reads the tail of the file to find `startxref`.
2. Follows the chain of cross-reference sections, each of which may be a classic table, a cross-reference
   stream, or a hybrid of both, and may point at object streams.
3. Builds a map from object number to a location: either a byte offset, or a position inside an object
   stream. Nothing is loaded meanwhile: a value a section refers to — a cross-reference stream's `/W` or
   `/Length` written as a reference, a `/Prev` — is read only where a section already read places it, so that
   the map the chain gives is the file's own. A `/Length` no section read before places, or that leads through
   references to such an object, is read once the chain is, and checked against the data the chain took up to the
   first `endstream` after the data's start, searched for in the file up to the next object the sections read so far
   place, or its end; one that leads to an object `MaxObjectLength` cuts waits for the chain the same way, and is the
   guard's: the guard is reached once the chain is read, and the length is not checked. Either way, when the section's
   own row for that `/Length` — the row the reading of its rows keeps — lies among the rows the data holds, those
   rows spelled `endstream`: the object is read where the row places it, without being kept, and the data is taken
   again to the length it gives, or to the integer the guard's cut left of it, when an `endstream` confirms it.
4. Resolves the trailer's `/Root`, which reads the document catalog, and decodes the object stream that holds
   it when one does. When `/Root` leads to no catalog, it loads the objects the map holds, one after the
   other, until one is a catalog (`trailer.root-recovered`), and rebuilds the map when none is.

On a file whose index and `/Root` are sound, that is all: no page is read, and no stream's data but that of the
cross-reference streams and of the object stream holding the catalog. The search for a lost catalog parses
objects, and a rebuild scans the whole file, but neither decodes a page's content, an image or a font.

Indexing allocates 52 to 63 bytes per object, whatever the objects weigh — a scanned page holding a 3 MB image
costs the same to index as an empty one. The map holds 52 bytes a slot, and is sized before it is filled: each
cross-reference section makes room for the rows it holds before it reads them, up to the next prime of the runtime's
table past the count, and for no more objects than the newest section's `/Size` numbers, since an older section's rows
for numbers a newer one gave add nothing. A count the file declares is its claim, so room is made for no more rows than
a stream's data holds, or than a classic table's bytes hold at 20 bytes a row up to the first byte no row holds, which
the table is read in a window large enough to reach. A chain of many like sections makes room section after section,
doubling the map at least each time it must grow, and for the whole `/Size` once a doubling comes within a doubling of
it. A table's rows are counted by their lines, so that rows written 19 bytes long, their end of line a single byte,
take no more room than rows of 20. A map left to grow — by a rebuild, or rows written shorter still — doubles as it
grows, and allocates 100 to 210 bytes an object, by where the count falls against its steps.
A cross-reference stream adds the decoding of its rows: 7 bytes an object when they are written as they are, up to
about 42 when Flate stores them without compressing, compressed rows in between. Opening also decodes the object
stream that holds the catalog, when one does, and that costs what the stream holds: a producer that packs every object
into one stream makes opening pay their decoded weight: 864 bytes an object on one such file, damaged, of 65,564. An open
document keeps the map, 52 to 63 bytes an object, 108 at most when it grew: on the corpus's thousand-page journal, about
61, where it kept 105 when the map grew by doubling. When the reader relocates an object or rebuilds the map once the
chain is read, it keeps a copy of the map the chain gave as well, so that validation still judges the file's own.

The map holds at most 8,388,607 object numbers by default, the most ISO 32000-1's Annex C advises a document to have,
and `PdfReaderLimits.MaxIndexEntries` raises it ([Reader limits](reader-limits.md)): at the default, the map alone
can take 436 MB.

An object the map does not hold is one the file does not define, and a reference to it reads as null, as the
specification says — or one a limit left out of the map, which reads as null too, until the limit is raised: the
sections past `MaxXRefSectionCount`, the rows past `MaxXRefSectionLength` or past what `MaxStreamLength` reads or
`MaxDecodedStreamLength` decodes of a cross-reference stream, a cross-reference stream whose `/W`, `/Index`, `/Size`,
`/Filter` or `/DecodeParms` leads to an object `MaxObjectLength` cuts and the sections older than it — so too one whose
`/Length` leads to such an object, when no `endstream` ends its data before the next object or the end of the file, or
the rows past an `endstream` its rows spell, which only that length could tell from the end of its data —, the sections
older than a `/Prev` or an `/XRefStm` that leads to such an object, the numbers past `MaxIndexEntries`. That is no
damage, and the map is not rebuilt for it. The map is rebuilt by scanning the whole file, once at most, and never while
the chain is read:

- at opening, when `startxref` cannot be found or read, when the section it names cannot be read, when the
  chain indexes nothing, or when `/Root` leads to no catalog and no object of the map is one;
- when an object the map lacks is asked for, by opening or after it, while the map may have lost entries: a
  cross-reference section the chain names cannot be found, or a `/Prev` or `/XRefStm` is not an offset — one
  written as a reference no section read before it places included — (`xref.section-missing`); one is found and
  cannot be read (`xref.section-unreadable`), a value its rows need written as such a reference, or a `/Length` so written whose data no `endstream` ends before the next object or the end of the file, among the causes; the chain loops back on itself (`xref.chain-cycle`); a row of a classic table cannot be read, which ends its subsection; a row gives an object
  in use what no entry can hold — a generation past 65,535, an offset of 2⁶³ or more, an object stream that is no
  object number, an index past 2,147,483,647, a field wider than 8 bytes whose leading bytes are not all zero —,
  which refuses that row alone, no older section's row standing for
  the object; or a cross-reference stream whose data the reader decoded whole holds fewer rows than it declares;
- when an object is asked for, by opening or after it, that is neither where the map says nor within 512 bytes
  of it;
- before a validation's first rule, when one of the two cases above could still happen — the map may have lost
  entries, or one of them places its object neither where it says nor near —: the validator has the map rebuilt
  then, as that read would, rather than let one of its rules' reads rebuild it halfway through. It finds out by
  reading the header each entry written directly in the file places, or looking for it nearby as a read would, and
  nothing of any object's body.

An object, or a section a `/Prev` or a `/XRefStm` names, that lies a few bytes from where it is said to be is
found nearby (`xref.offset-adjusted`), and the map is not rebuilt for it. The section `startxref` names is not
looked for nearby: when it is not where `startxref` says, the map is rebuilt.

A rebuilt map keeps, of each number, the definition `object.redefined` names — the last written directly in the file,
or, for a number written only inside object streams, the first listed in the object stream read first —, a direct
definition under the generation its header gives and a member of an object stream under generation 0. An object
found nearby takes the generation of the header found there, whatever its row gave; an entry of the chain otherwise
keeps its row's. The rules that name an object from the map — `object.reference-missing`,
`object.name-null-character`, `object.endobj-missing` — name it so, `5 1` for `5 1 obj`; the cross-reference rules
name it as the file's row does, and the page tree and object-shape rules as the reference that reached it does.

What the reader noted of an object as it read it — that no `endobj` followed it, that a limit cut it, that its
stream's `/Length` was wrong — is forgotten by a rebuild, and noted again as the rebuilt map's definitions are read.
A rebuild a read sets off while that read is still under way — an object's `/Length` or filter names an object the
map misplaces — scans the file, and is finished once no read is under way, or once the read throws, before its exception
leaves: the object is then read again from the rebuilt map, and what was read meanwhile, with the read unfinished, is
let go, so that it reads, and reports, as it would had the map been rebuilt first, at the same cost. If the source fails
while the rebuild is being finished, the next read finishes it.

A rebuild reads the file to its end, and holds no more numbers than the map may: a header for a number it holds already
takes no room, so that a million copies of one object hide nothing behind them, and the first number past
`MaxIndexEntries` is reported once (`limit.index-entries`). A rebuild of a chain a limit had cut keeps the entries the
chain read for the numbers the scan did not find, rather than losing them.

## What it deliberately does not do

Past the index and the catalog, nothing is parsed until something asks for it, and — short of a rebuild, which
scans the whole file, and of the search for where a damaged stream's data ends, below — stream data is not even read
from disk. A `PdfStream` holds the offset and length of its bytes; the bytes arrive when you call `GetRawBytes()` or
`Decode()`, and not before. A test holds this on a document of about 500 KB, nearly all of it one page's content:
opening it reads less than 200,000 bytes of the file, and reading that content's bytes afterwards reads at least its
500,000 more.

Nor are the bytes kept once they have arrived: neither the stream nor the object cache holds them, and each call reads
them again. A document held in memory — opened from bytes or from a stream — lends them where they lie, without a copy:
what a call hands back is a view of that memory, which keeps it alive for as long as it is held, after the document is
disposed too, and changes as it does. One opened from a path reads them from the file into an array of their length.
Either is the caller's to keep or let go, so that memory follows the streams being read or decoded, not those read
before, and streams whose data share a stretch of the file — objects that overlap — do not each hold a copy of it. What
one call reads is bounded by `PdfReaderLimits.MaxStreamLength`, 256 MB by default: past it the data is cut, its filters
decode what was read as data the reader cut, and `limit.stream-length` is reported; `RawLength` stays the length the
file holds.

Finding where a stream's data ends reads little of it. The reader parses an object through a window of the file, and
confirms the declared `/Length` by the `endstream` that must follow it, after white space and comments, however many —
a comment only once white space comes before it, since a `%` right where the declared length ends is the data's —:
past the window, it asks the file for the bytes after the declared length rather than reading the data, and reads on
as far as white space and comments run. A length nothing confirms, or a `/Length` that gives none, sends the reader on
one search for the first `endstream` after the data's start: in the bytes the parser holds, then in the file, up to the
nearer of the first object header the bytes hold and the next object the index as the file wrote it places, or the end
of the file. With no `endstream` before that stop, the object's `endobj` plays its part. The search is made once for
each stream, however often the stream is parsed again; no window grows to find an `endstream`, so that a stream's data
never counts toward `MaxObjectLength`; and the searches of a document read four times the file at most — a valid
file's, about twice, for the white space after a declared length and the cross-reference streams whose `/Length` the
chain cannot read yet. What each case keeps, and reports, is in [Diagnostics](diagnostics.md#where-a-stream-ends).

When it is read or decoded, a stream works under the [limits](reader-limits.md) of the document it came from,
however long after opening — so with `ThrowOnLimit` set, `GetRawBytes()` and `Decode()` can throw long after `Open`
returned. What decoding meets — a stream that lost its tail, a limit its filters reached — is reported when it is met,
in the diagnostics you pass to `Decode`, or in the document's own when you pass none; data cut at `MaxStreamLength` is
reported in the document's own once for each stream, as the reader reads it, and in those you pass to `Decode` each time
it decodes the cut data. A damaged stream nobody decodes is never reported, since nothing reads it.

Holding a stream as an offset and a length is what will let a copy between documents — merging, assembling,
stamping — move its **encoded** bytes as they are, with no decompress/recompress cycle in between, since nothing
has to decode a stream to copy it. No API copies one yet: merging and assembling come with M06 on the
[roadmap](/project/roadmap), stamping with M09.

## The cost of the trade

Objects are cached once parsed, in a bounded cache, so hot objects such as the page tree are not reparsed.
Beyond that bound, an object read again is read from the file again. That is the deliberate trade: memory
stays predictable, and the pathological case is slower rather than fatal. The bound is an option, and
[Tune the reader's memory](../guides/tune-reader-memory.md) says when to move it.

A stream's data makes the same trade without a cache: no stream keeps it, cached or not, so that data asked for twice
is read twice, and, through `Decode()`, decoded twice. A caller that needs it more than once keeps what one call gave,
as [the same guide](../guides/tune-reader-memory.md#read-a-streams-data-once) shows.

The same options carry the [reader limits](reader-limits.md), which bound what one file may make the reader
hold.

## Measured

| Operation | Document | Time | Allocated |
|---|---|---|---|
| Indexing | 1000 pages, ~4 MB | 229 µs | 393 KB |
| Indexing, then reading every page | 1000 pages, ~4 MB | 6.2 ms | 5.9 MB |
| Indexing and walking the page tree | real 1000-page file | — | 3.2 MB |
| Indexing a classic table | generated, 300,000 objects | — | 16.9 MB |
| Indexing a cross-reference stream whose rows Flate stores | generated, 300,000 objects | — | 29.5 MB |
| Indexing a chain of a thousand classic sections | generated, 300,000 objects | — | 30.6 MB |

The first two rows are BenchmarkDotNet's. The last four are measured in the test suite, in Release, after a first
reading — the journal's on 2026-10-02, the generated indexes' on 2026-10-14 —, and enforced as budgets in CI, so that
an allocation regression past one fails the build: 4 MB for the journal, 17.7 MB, 31.0 MB and 32.1 MB for the generated
indexes, 5 % over their figures. Growing the map allocated 31.1 MB, 43.1 MB and 33.2 MB: sizing it before it is
filled allocates about half that for the table, two thirds for the stream, and a tenth less for the chain, whose map
still doubles from section to section until it comes within a doubling of the newest `/Size`. The stream's rows are stored rather than compressed so that no runtime's zlib moves its figure.
