---
title: Lazy reading
sidebar_position: 1
---

# Lazy reading

Opening a document builds an index and stops there.

## What opening actually does

1. Reads the tail of the file to find `startxref`.
2. Follows the chain of cross-reference sections, each of which may be a classic table, a cross-reference
   stream, or a hybrid of both, and may point at object streams.
3. Builds a map from object number to a location: either a byte offset, or a position inside an object
   stream.

That map is the only thing kept. It costs roughly 200 bytes per object, whatever the objects weigh — a
scanned page holding a 3 MB image costs the same to index as an empty one.

An object the map does not hold is one the file does not define, and a reference to it reads as null, as the
specification says. The map is rebuilt by scanning the file only when it may have lost entries — a
cross-reference section the chain names cannot be found (`xref.section-missing`), a limit stopped the chain
or a table before its end, or a cross-reference stream holds fewer rows than it declares — and then only when
such an object is asked for. A section named a few bytes from where it lies is found nearby
(`xref.offset-adjusted`).

## What it deliberately does not do

Nothing is parsed until something asks for it, and stream data is not even read from disk. A
`PdfStream` holds the offset and length of its bytes; the bytes arrive when you call `Decode()`, and not
before. This is asserted by a test that counts how many bytes are read from the file while opening a
500 KB document: opening reads a small fraction of it.

When it is decoded, a stream decodes under the [limits](reader-limits.md) of the document it came from,
however long after opening — so with `ThrowOnLimit` set, `Decode()` can throw long after `Open` returned.
What decoding meets — a stream that lost its tail, a limit reached — is reported when it is met, in the
diagnostics you pass to `Decode`, or in the document's own when you pass none: a damaged stream nobody
decodes is never reported, since nothing reads it.

Copying a stream between documents — merging, assembling, stamping — moves the **encoded** bytes as they
are, with no decompress/recompress cycle in between.

## The cost of the trade

Objects are cached once parsed, in a bounded cache, so hot objects such as the page tree are not reparsed.
Beyond that bound, an object read again is read from the file again. That is the deliberate trade: memory
stays predictable, and the pathological case is slower rather than fatal.

```csharp
using var document = PdfDocument.Open("catalog.pdf", new PdfReaderOptions
{
    ObjectCacheCapacity = 32_768,   // more memory, fewer re-reads
    DiagnosticCapacity = 5_000,
});
```

The same options carry the [reader limits](reader-limits.md), which bound what one file may make the reader
hold.

## Measured

| Operation | Document | Time | Allocated |
|---|---|---|---|
| Indexing | 1000 pages, ~4 MB | 229 µs | 393 KB |
| Indexing, then reading every page | 1000 pages, ~4 MB | 6.2 ms | 5.9 MB |
| Indexing and walking the page tree | real 1000-page file | — | 2.4 MB |

The last row is enforced as a budget in CI: an allocation regression fails the build.
