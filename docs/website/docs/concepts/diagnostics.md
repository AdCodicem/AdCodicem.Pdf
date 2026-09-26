---
title: Diagnostics
sidebar_position: 2
---

# Diagnostics

Real PDFs are frequently not conforming. A reader that refuses them fails where every reader on the
market succeeds; a reader that silently patches them leaves you guessing. This library does the third
thing: it repairs what it can and **returns an account of what it did**.

```csharp
using var document = PdfDocument.Open("received-from-supplier.pdf");

if (document.Diagnostics.HasRepairs)
{
    foreach (var entry in document.Diagnostics)
    {
        Console.WriteLine($"{entry.Severity} {entry.Code} at {entry.Position}: {entry.Message}");
    }
}
```

The report is a returned value, not a log: it can be inspected, serialised, asserted on in your own tests,
and used to decide whether to accept a file into your system.

Diagnostics say what the reader did to read a file. To know what is wrong with the file itself — whether or
not the reader could work around it —, [validate it](validation.md): findings are a separate verdict, with
their own severities and rule identifiers that never reuse a diagnostic code.

## Severities

| Severity | Meaning |
|---|---|
| `Information` | Worth knowing, changes nothing |
| `Repair` | The file was not conforming and the reader worked around it |
| `Warning` | Something is wrong and the result may not be what you expect |
| `ConformanceLoss` | A guarantee such as PDF/A or PDF/UA was lost by an operation |

## Codes

Codes are stable: they are part of the public contract, because callers filter on them.

| Code | Raised when |
|---|---|
| `xref.rebuilt` | The index was rebuilt by scanning the whole file |
| `xref.offset-adjusted` | An object was not where the index said, and was found nearby |
| `xref.chain-cycle` | The chain of previous sections looped |
| `xref.entry-out-of-range` | An entry pointed outside the file |
| `stream.length-invalid` | A stream's declared length did not match where its data ended |
| `stream.truncated` | A stream ran past the end of the file |
| `syntax.unexpected-token` | A token was found where a value was expected |
| `syntax.truncated-object` | The file ended in the middle of an object |
| `syntax.depth-exceeded` | Nesting went deeper than the reader will follow |
| `object.redefined` | An object was defined more than once; the last definition won |
| `filter.failed` | A filter could not be applied, and the data was left encoded |
| `filter.unsupported` | The file names a filter the library does not implement |
| `limit.decoded-stream` | A stream decodes to more than `MaxDecodedStreamLength`; the part within it was kept |
| `limit.object` | An object is longer than `MaxObjectLength`, its stream data aside; the part within it was parsed |
| `limit.xref-section-length` | A classic cross-reference table is longer than `MaxXRefSectionLength`; the entries within it were read |
| `limit.xref-section-count` | The chain of cross-reference sections is longer than `MaxXRefSectionCount`; the newest were read |
| `limit.trailer` | A trailer, or a cross-reference stream's dictionary, is longer than `MaxTrailerLength`; the part within it was parsed |

The reader parses an object through a window of the file — 8 KB to start with — and reads it again
through a larger one when it runs past the edge. What the smaller window saw there, such as a string
without its end or a stream without its `endstream`, is dropped with that attempt, so the report says what
the object holds, not where a window happened to end. A stream whose declared length the file cannot hold
is reported: as `stream.truncated` when the file ends inside its data, as `stream.length-invalid` when its
`endstream` comes first.

Growing windows, and decoding, are bounded. The five `limit.*` codes report the reader's own limits, not
faults of the file: a valid document can reach them — a large-format scan decodes past the 256 MB a stream
may decode to by default — and each message names the property of `PdfReaderLimits` that lifts it. What
fits within the limit is kept. See [Reader limits](reader-limits.md).

## Exceptions, by contrast

Exceptions are reserved for what makes the operation impossible: the input is not a PDF
(`PdfFormatException`), or it is encrypted and cannot be opened with the credentials given
(`PdfEncryptedException`). Anything the reader can work around is a diagnostic, never an exception —
unless you ask for one: with `PdfReaderOptions.ThrowOnLimit`, reaching a reader limit throws
`PdfLimitExceededException` from whichever operation reached it.

## Hostile input

A PDF arrives from outside your system, so the reader treats every value in it as an attempt. No
allocation is sized by a number read from the file without a checked bound, no recursion is unbounded,
and no loop exits on an offset that came from the file. The bounds a valid document can reach are the
[reader limits](reader-limits.md), on by default: raise them for a document you know, and keep
`PdfReaderLimits.Unbounded` for documents you trust.

That claim is tested rather than asserted. Besides the hand-written cases — a cross-reference chain that
loops, a stream claiming two gigabytes, an object stream declaring a billion objects, containers nested
twenty thousand deep, fifty thousand streams each taking its length from the next, a few megabytes of
RunLength data that would decode to hundreds, predictor rows whose length overflows — a mutation campaign
runs against the test corpus: bit flips, corrupted digits, truncations, spliced bytes and broken keywords,
each input required to end either in a usable document or in a typed exception, inside a time and an
allocation budget. A few thousand mutated documents go through
the reader on every test run — every pull request, and every package before it is published — and a
nightly campaign runs twenty thousand mutations per seed document.

The first campaign found a real defect within a minute: a mutated invoice made the reader's index lookup
and its relocation search call each other until the stack ran out. Relocation is now bounded to three
counted attempts. That is the kind of failure this exists to catch — no hand-written test had thought of
it, and a file that kills the process is the worst outcome a document reader can have.
