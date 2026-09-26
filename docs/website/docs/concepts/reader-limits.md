---
title: Reader limits
sidebar_position: 3
---

# Reader limits

Every PDF that is valid under the specification can be read. That promise has to live alongside another:
a file arriving from outside your system is treated as hostile, and a few kilobytes of it must not be able
to make the reader hold gigabytes. The two meet in the **reader limits**: bounds on what a file may make the
reader hold, on by default, each of which you can raise.

## The limits

| Property of `PdfReaderLimits` | Default | Reached by | What is kept | Code |
|---|---|---|---|---|
| `MaxDecodedStreamLength` | 256 MB | A decompression bomb, or a large-format scan: a 9,600 × 11,410 RGB map decodes to 313 MB | The first bytes, up to the bound | `limit.decoded-stream` |
| `MaxObjectLength` | 16 MB | An object that long, the data of a stream aside: an array of a million references | The object as far as the bound | `limit.object` |
| `MaxXRefSectionLength` | 64 MB | A classic cross-reference table of more than about 3.3 million entries | The entries within the bound | `limit.xref-section-length` |
| `MaxXRefSectionCount` | 1,024 | A document saved incrementally more than a thousand times | The newest sections | `limit.xref-section-count` |
| `MaxTrailerLength` | 64 KB | A trailer that long, or a cross-reference stream's dictionary: its `/Index` grows with every scattered update | The trailer as far as the bound | `limit.trailer` |

When a limit is reached, the reader keeps what fits and reports a warning under the limit's own code, whose
message names the property to raise:

```text
Warning limit.decoded-stream at 48213: The /FlateDecode data decodes to more than 256 MB; decoding stopped
there. Raise PdfReaderLimits.MaxDecodedStreamLength to read past it.
```

The limit is the reader's, not a fault of the file, and it replaces what the parser met where it stopped:
you are not told a stream was truncated when it was the reader that stopped reading it. An object, a
cross-reference section or a trailer is reported once, however often it is read again; a stream is reported
each time it is decoded past the bound — in the diagnostics you pass to `Decode`, or in the document's own
when you pass none.

A classic trailer that ends within the window its cross-reference table was read through is read whole,
whatever its length. `MaxTrailerLength` bounds how far the reader follows one past that window, which is
where the cost lies.

## Raising a limit

The limits are an immutable record on `PdfReaderOptions`. Raise the one the report names:

```csharp
var options = new PdfReaderOptions
{
    Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 512 * 1024 * 1024 },
};

using var document = PdfDocument.Open("topographic-map.pdf", options);
```

A stream read from a document decodes under **that document's** limits, however long after opening you
decode it. A stream you build in memory decodes under `PdfReaderLimits.Default`.

A value of zero or less is refused with an `ArgumentOutOfRangeException`. A length above `Array.MaxLength`,
about 2 GB, is taken as `Array.MaxLength`: a decoded stream is held in one piece, and no runtime allocates
a larger one.

### Documents you trust

`PdfReaderLimits.Unbounded` takes every limit to the most the implementation can hold. Use it for documents
your own application produced, never for uploads: under it, a file can make the reader hold whatever it
asks for, up to those maxima. Until the reader decodes streams a piece at a time, a stream that decodes past
about 2 GB is still cut there, and still reported, even under `Unbounded`.

Raising `MaxDecodedStreamLength` also raises what the reader may hold for object streams it keeps decoded,
which are not yet bounded as a whole.

## Throwing instead

Some applications would rather refuse a document than read part of it. Set `ThrowOnLimit`, and reaching a
limit throws a `PdfLimitExceededException` instead of warning:

```csharp
var options = new PdfReaderOptions { ThrowOnLimit = true };

try
{
    using var document = PdfDocument.Open(path, options);
    Process(document);
}
catch (PdfLimitExceededException exception)
{
    // exception.Code      "limit.decoded-stream"
    // exception.LimitName "MaxDecodedStreamLength"
    // exception.Limit     268435456
    // exception.Position  the byte offset where the limit was reached
    Reject(path, exception.Message);
}
```

Reading is lazy, so the exception comes from whichever operation reaches the limit: `PdfDocument.Open`
while it indexes the file, `GetObject` or resolving a reference when an object is too long, `Decode` when a
stream decodes too far. With `ThrowOnLimit` set, be ready for it wherever the document is used, not only
around `Open`. A document whose index had to be rebuilt finishes the rebuild before throwing, so it stays
usable afterwards.

## What cannot be lifted

Some bounds protect the reader itself — its stack, its rebuild of a damaged index — and are not reached by
the documents producers write. They are not options:

| Bound | Value | Why it is not an option |
|---|---|---|
| Nesting of arrays and dictionaries | 128 levels | Real documents nest a handful of levels; the bound keeps a hostile file from exhausting the stack |
| A reference that resolves to another reference | followed 32 times | Producers write the object itself; the bound breaks a chain that loops |
| Objects loaded while loading another | 64 | An indirect `/Length` leads to an integer, which loads nothing further |
| Objects indexed by a rebuild | 2,000,000 | A rebuild only runs on a damaged file |
| Entries one subsection claims | 50,000,000 | A table's rows are bounded by `MaxXRefSectionLength` already; a count past that describes rows that are not there |

A new bound is classified when it is added: if a valid file can reach it, it becomes a limit, with its code
and its test.
