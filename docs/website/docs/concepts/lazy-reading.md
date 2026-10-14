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
   the map the chain gives is the file's own. A `/Length` no section read before places is read once the chain
   is, and checked against the data the chain took up to its `endstream`.
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
the table is read in a window large enough to reach. A map left to grow — by a rebuild, rows written shorter than that,
or a chain of many like sections, each making room in turn — doubles as it grows, and allocates 100 to 210 bytes an
object, by where the count falls against its steps.
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
sections past `MaxXRefSectionCount`, the rows past `MaxXRefSectionLength` or past what `MaxDecodedStreamLength`
decodes of a cross-reference stream, the numbers past `MaxIndexEntries`. That is no damage, and the map is not rebuilt
for it. The map is rebuilt by scanning the whole file, once at most, and never while the chain is read:

- at opening, when `startxref` cannot be found or read, when the section it names cannot be read, when the
  chain indexes nothing, or when `/Root` leads to no catalog and no object of the map is one;
- when an object the map lacks is asked for, by opening or after it, while the map may have lost entries: a
  cross-reference section the chain names cannot be found, or a `/Prev` or `/XRefStm` is not an offset — one
  written as a reference no section read before it places included — (`xref.section-missing`); one is found and
  cannot be read (`xref.section-unreadable`), a value its rows need written as such a reference among the causes; the chain loops back on itself (`xref.chain-cycle`); a row of a classic table cannot be read, which ends its subsection; a row gives an object
  in use what no entry can hold — a generation past 65,535, an offset of 2⁶³ or more, an object stream that is no
  object number, an index past 2,147,483,647, a field wider than 8 bytes whose leading bytes are not all zero —,
  which refuses that row alone, no older section's row standing for
  the object; or a cross-reference stream whose data the reader decoded whole holds fewer rows than it declares;
- when an object is asked for, by opening or after it, that is neither where the map says nor within 512 bytes
  of it.

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

A rebuild reads the file to its end, and holds no more numbers than the map may: a header for a number it holds already
takes no room, so that a million copies of one object hide nothing behind them, and the first number past
`MaxIndexEntries` is reported once (`limit.index-entries`). A rebuild of a chain a limit had cut keeps the entries the
chain read for the numbers the scan did not find, rather than losing them.

## What it deliberately does not do

Past the index and the catalog, nothing is parsed until something asks for it, and — short of a rebuild, which
scans the whole file — stream data is not even read from disk. A `PdfStream` holds the offset and length of its
bytes; the bytes arrive when you call `Decode()`, and not before. A test holds this on a document of about
500 KB, nearly all of it one page's content: opening it reads less than 200,000 bytes of the file, and reading
that content's bytes afterwards reads at least its 500,000 more.

When it is decoded, a stream decodes under the [limits](reader-limits.md) of the document it came from,
however long after opening — so with `ThrowOnLimit` set, `Decode()` can throw long after `Open` returned.
What decoding meets — a stream that lost its tail, a limit reached — is reported when it is met, in the
diagnostics you pass to `Decode`, or in the document's own when you pass none: a damaged stream nobody
decodes is never reported, since nothing reads it.

Holding a stream as an offset and a length is what will let a copy between documents — merging, assembling,
stamping — move its **encoded** bytes as they are, with no decompress/recompress cycle in between, since nothing
has to decode a stream to copy it. No API copies one yet: merging and assembling come with M06 on the
[roadmap](/project/roadmap), stamping with M09.

## The cost of the trade

Objects are cached once parsed, in a bounded cache, so hot objects such as the page tree are not reparsed.
Beyond that bound, an object read again is read from the file again. That is the deliberate trade: memory
stays predictable, and the pathological case is slower rather than fatal. The bound is an option, and
[Tune the reader's memory](../guides/tune-reader-memory.md) says when to move it.

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
| Indexing a chain of a thousand classic sections | generated, 300,000 objects | — | 44.6 MB |

The first two rows are BenchmarkDotNet's. The last four are measured in the test suite, in Release, after a first
reading — the journal's on 2026-10-02, the generated indexes' on 2026-10-14 —, and enforced as budgets in CI, so that
an allocation regression past one fails the build: 4 MB for the journal, 17.7 MB, 31.0 MB and 46.9 MB for the generated
indexes, 5 % over their figures. A map sized before it is filled allocates half what growing it did, 31.1 MB and
43.1 MB; a chain of many like sections lands where doubling lands, as growth landed elsewhere, at 33.2 MB here. The
stream's rows are stored rather than compressed so that no runtime's zlib moves its figure.
