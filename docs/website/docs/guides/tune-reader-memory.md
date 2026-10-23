---
title: Tune the reader's memory
description: Trade memory for fewer re-reads with the object cache, read a stream's data once, keep more diagnostics, and open documents without copying them.
---

# Tune the reader's memory

The reader's memory follows what you read, not the size of the file. This guide shows the few settings that move it,
and when to move them.

## Size the object cache

Parsed objects are kept in a bounded cache, 8,192 objects by default. Beyond it, an object read again is read from the
file again. Raise the bound when you walk a large document more than once — a catalog of thousands of pages whose
objects you revisit —, and lower it when many documents are open at once and each is read through once:

```csharp
using AdCodicem.Pdf.Documents;

var options = new PdfReaderOptions
{
    ObjectCacheCapacity = 32_768,   // more memory, fewer re-reads
};

using var document = PdfDocument.Open("catalog.pdf", options);
```

Measure before and after: the cache only pays off when the same objects are read again. A stream in the cache holds
its dictionary and where its data lies, not the data: its size does not weigh on the cache.

## Read a stream's data once

A stream keeps none of its data: each call to `GetRawBytes` or `Decode` reads it again. Opened from a path, each call
reads the data from the file into an array of its length; opened from a stream or from bytes, which the document holds
in memory, `GetRawBytes` lends the bytes where they lie, without a copy. When you use the same data more than once,
keep what one call gave you rather than call again:

```csharp
using AdCodicem.Pdf.Objects;

if (document.GetObject(new PdfObjectId(12)) is PdfStream stream)
{
    var data = stream.Decode();   // read and decoded once

    Index(data);
    Render(data);
}
```

What one call reads is bounded by `PdfReaderLimits.MaxStreamLength`, 256 MB by default: past it the data is cut, and
`limit.stream-length` reported. [Read or refuse a document that reaches a limit](handle-reader-limits.md) shows how to
raise it.

## Keep more diagnostics

A document keeps 1,000 diagnostic entries by default, and counts the rest in `Diagnostics.SuppressedCount`. When you
need every entry of a badly damaged file, raise the bound:

```csharp
var options = new PdfReaderOptions { DiagnosticCapacity = 5_000 };
```

When a verdict is all you need, validate rather than keep more entries:
[Validate a document before accepting it](validate-a-received-document.md).

## Open without copying the file

How you open a document decides what it holds in memory:

| You pass | Held in memory |
|---|---|
| A path | Only what you read: the file is read where it lies |
| A `MemoryStream` that exposes its buffer — written to after `new MemoryStream()` or `new MemoryStream(capacity)`, or built with `publiclyVisible: true` | Its buffer, used as it is |
| Any other `Stream` — a `FileStream`, a network response, a `MemoryStream` built over an array without `publiclyVisible`, a type derived from `MemoryStream` | The whole stream, copied into memory first: into an array of its length when it can seek |
| `ReadOnlyMemory<byte>` | The bytes you already hold |

A stream is read as the whole file: from its start when it can seek, whatever you read of it before, and from where it
stands when it cannot. Until the document is disposed the stream is the document's: do not write to it, move it, or
dispose of it, and do not change or grow a buffer the document reads in place.

Open from a path whenever the document is a file. Dispose each document when you are done with it: opened from a
path, it holds the file open until then.

## See also

- [Lazy reading](../concepts/lazy-reading.md): what opening does, what it costs, and what was measured.
- [Reader limits](../reference/reader-limits.md): the bounds on what one file may make the reader hold.
