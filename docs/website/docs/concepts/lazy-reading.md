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

## What it deliberately does not do

Nothing is parsed until something asks for it, and stream data is not even read from disk. A
`PdfStream` holds the offset and length of its bytes; the bytes arrive when you call `Decode()`, and not
before. This is asserted by a test that counts how many bytes are read from the file while opening a
500 KB document: opening reads a small fraction of it.

Copying a stream between documents — merging, assembling, stamping — moves the **encoded** bytes as they
are, with no decompress/recompress cycle in between.

## The cost of the trade

Objects are cached once parsed, in a bounded cache, so hot objects such as the page tree are not reparsed.
Beyond that bound, an object read again is read from the file again. That is the deliberate trade: memory
stays predictable, and the pathological case is slower rather than fatal.

```csharp
using var document = PdfDocument.Open("catalogue.pdf", new PdfReaderOptions
{
    ObjectCacheCapacity = 32_768,   // more memory, fewer re-reads
    DiagnosticCapacity = 5_000,
});
```

## Measured

| Operation | Document | Time | Allocated |
|---|---|---|---|
| Indexing | 1000 pages, ~4 MB | 229 µs | 393 KB |
| Indexing, then reading every page | 1000 pages, ~4 MB | 6.2 ms | 5.9 MB |
| Indexing and walking the page tree | real 1000-page file | — | 2.4 MB |

The last row is enforced as a budget in CI: an allocation regression fails the build.
