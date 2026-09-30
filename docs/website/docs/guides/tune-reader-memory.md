---
title: Tune the reader's memory
description: Trade memory for fewer re-reads with the object cache, keep more diagnostics, and open documents without copying them.
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

Measure before and after: the cache only pays off when the same objects are read again.

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
| A `MemoryStream` | Its buffer, used as it is |
| Any other `Stream` — a `FileStream`, a network response | The whole stream, copied into memory first |
| `ReadOnlyMemory<byte>` | The bytes you already hold |

Open from a path whenever the document is a file. Dispose each document when you are done with it: opened from a
path, it holds the file open until then.

## See also

- [Lazy reading](../concepts/lazy-reading.md): what opening does, what it costs, and what was measured.
- [Reader limits](../reference/reader-limits.md): the bounds on what one file may make the reader hold.
