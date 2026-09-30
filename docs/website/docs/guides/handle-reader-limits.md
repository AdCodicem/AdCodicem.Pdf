---
title: Read or refuse a document that reaches a limit
description: Raise the limit a report names, read trusted documents without limits, or refuse a document rather than read part of it.
---

# Read or refuse a document that reaches a limit

The reader bounds what a file may make it hold, and a large but sound document — a large-format scan, a file saved
incrementally a thousand times — can reach one of those bounds. This guide shows how to tell, and then how to read the
whole document, or refuse it instead.

## Tell whether a document reached a limit

A document that reached a limit was read in part, and says so in its diagnostics, under a code that starts with
`limit.`. The message names the property that lifts it:

```text
Warning limit.decoded-stream at 48213: The /FlateDecode data decodes to more than 256 MB; decoding stopped
there. Raise PdfReaderLimits.MaxDecodedStreamLength to read past it.
```

```csharp
foreach (var entry in document.Diagnostics)
{
    if (entry.Code.StartsWith("limit.", StringComparison.Ordinal))
    {
        Console.WriteLine(entry);
    }
}
```

Reading is lazy: a stream reaches `MaxDecodedStreamLength` when it is decoded, so look after decoding, not only after
opening.

## Raise the limit the report names

The limits are an immutable record on `PdfReaderOptions`. Raise the one the report names, and open the document again:

```csharp
using AdCodicem.Pdf.Documents;

var options = new PdfReaderOptions
{
    Limits = PdfReaderLimits.Default with { MaxDecodedStreamLength = 512 * 1024 * 1024 },
};

using var document = PdfDocument.Open("topographic-map.pdf", options);
```

Every stream of that document decodes under its limits, however long after opening you decode it.

## Read documents you trust without limits

For documents your own application produced, lift every limit at once:

```csharp
var trusted = new PdfReaderOptions { Limits = PdfReaderLimits.Unbounded };
```

Never use it for uploads: under it, a file can make the reader hold whatever it asks for, up to about 2 GB per
stream or object.

## Refuse the document instead

To refuse a document rather than read part of it, set `ThrowOnLimit`, and reaching a limit throws a
`PdfLimitExceededException` instead of warning:

```csharp
using AdCodicem.Pdf.Diagnostics;

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

Put the `try` around everything that uses the document, not only around `Open`: the exception comes from whichever
operation reaches the limit — `Open` while it indexes the file, `GetObject` or resolving a reference when an object is
too long, `Decode` when a stream decodes too far, `Validate` as it reads.

## See also

- [Reader limits](../reference/reader-limits.md), in the reference: every limit, its default and its code, and the
  bounds that cannot be lifted.
- [Reader limits](../concepts/reader-limits.md), explained: why the guards are on by default.
