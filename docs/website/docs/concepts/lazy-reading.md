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
   stream.
4. Resolves the trailer's `/Root`, which reads the document catalog, and decodes the object stream that holds
   it when one does. When `/Root` leads to no catalog, it loads the objects the map holds, one after the
   other, until one is a catalog (`trailer.root-recovered`), and rebuilds the map when none is.

On a file whose index and `/Root` are sound, that is all: no page is read, and no stream's data but that of the
cross-reference streams and of the object stream holding the catalog. The search for a lost catalog parses
objects, and a rebuild scans the whole file, but neither decodes a page's content, an image or a font.

Opening allocates roughly 200 bytes per object, whatever the objects weigh — a scanned page holding a 3 MB image
costs the same to index as an empty one. An open document keeps about half of that: on the corpus's
thousand-page journal, about 107 bytes per object, of which the map takes about 48 bytes an entry. When the
reader relocates an object or rebuilds the map once the chain is read, it keeps a copy of the map the chain gave
as well, so that validation still judges the file's own.

An object the map does not hold is one the file does not define, and a reference to it reads as null, as the
specification says. The map is rebuilt by scanning the whole file, once at most:

- at opening, when `startxref` cannot be found or read, when the section it names cannot be read, when the
  chain indexes nothing, or when `/Root` leads to no catalog and no object of the map is one;
- when an object the map lacks is asked for, by opening or after it, while the map may have lost entries: a
  cross-reference section the chain names cannot be found, or a `/Prev` or `/XRefStm` is not an offset
  (`xref.section-missing`); one is found and cannot be read (`xref.section-unreadable`); the chain loops back on itself (`xref.chain-cycle`); a limit stopped the chain or a
  table before its end; a row of a classic table cannot be read, which ends its subsection; or a
  cross-reference stream holds fewer rows than it declares, or gives a subsection a count of rows out of range;
- when an object is asked for, by opening or after it, that is neither where the map says nor within 512 bytes
  of it.

An object, or a section a `/Prev` or a `/XRefStm` names, that lies a few bytes from where it is said to be is
found nearby (`xref.offset-adjusted`), and the map is not rebuilt for it. The section `startxref` names is not
looked for nearby: when it is not where `startxref` says, the map is rebuilt.

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

The last row was measured on 2026-10-01, in Release, and is enforced as a budget of 4 MB in CI: an allocation
regression past it fails the build.
