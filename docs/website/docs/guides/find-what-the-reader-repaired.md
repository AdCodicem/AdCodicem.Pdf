---
title: Find out what the reader repaired
description: Check whether a document needed repairs, list what the reader did, filter by severity or code, and keep the account.
---

# Find out what the reader repaired

This guide shows how to find out what the reader had to work around to read a document you received, and how to
act on it. It assumes you can open a document; if not, start with the
[tutorial](../tutorials/first-steps.md).

## Check whether anything happened

```csharp
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;

using var document = PdfDocument.Open("received-from-supplier.pdf");

if (document.WasRepaired)
{
    // The index was rebuilt by scanning the file: the reader found the objects itself.
}

if (document.Diagnostics.HasRepairs || document.Diagnostics.HasWarnings)
{
    // The file was not conforming, or something may not read as you expect.
}
```

`WasRepaired` is true only when the whole index had to be rebuilt. `Diagnostics` holds everything else the reader
worked around. Both say what the reader did so far: a read after `Open` can still rebuild the index, and add to
the diagnostics.

## List what the reader did

Each entry writes itself out with its severity, its code, where in the file it applies and what happened:

```csharp
foreach (var entry in document.Diagnostics)
{
    Console.WriteLine(entry);   // Repair xref.offset-adjusted at 1874: …
}
```

To format it your own way, use its members:

```csharp
foreach (var entry in document.Diagnostics)
{
    Console.WriteLine($"{entry.Severity} {entry.Code} at {entry.Position}: {entry.Message}");
}
```

A loop lists the entries recorded when it began. One whose body reads the document — `GetObject`, a validation — goes on
over those, and what its reads record is listed by the next loop, and counted by `Count` at once.

## Filter on what matters to you

Filter on the code, never on the message: codes are stable from one release to the next, messages are not.
`PdfDiagnosticCodes` holds every code as a constant.

```csharp
if (document.Diagnostics.Contains(PdfDiagnosticCodes.XRefRebuilt))
{
    // Every object was found by scanning; no offset in the file was trusted.
}

foreach (var entry in document.Diagnostics)
{
    if (entry.Severity == PdfDiagnosticSeverity.Warning)
    {
        Console.WriteLine(entry);
    }
}
```

## Catch what decoding reports

Opening reads the index and the catalog and decodes no page's content, image or font, so a damaged stream is
reported when it is decoded, into the same `Diagnostics`. To keep the reports of one stream apart — to know which
image of a page lost its tail —, pass a list of your own:

```csharp
using AdCodicem.Pdf.Objects;

if (document.GetObject(new PdfObjectId(12)) is PdfStream stream)
{
    var reports = new PdfDiagnostics();
    var data = stream.Decode(reports);

    if (reports.Contains(PdfDiagnosticCodes.FilterFailed))
    {
        // What came back is what decoded before the damage.
    }
}
```

The list also receives, each time you decode the stream, `limit.stream-length` when its data runs past what the reader
reads of one stream; the document's `Diagnostics` hold that report too, once, since the reader makes it as it reads the
data.

## Decide whether to accept the file

Diagnostics say what the reader did, not whether the file is sound. To decide whether to accept it, validate it too:
[Validate a document before accepting it](validate-a-received-document.md). A policy that refuses a file whose
index was lost, and accepts the others with their account, might read:

```csharp
if (document.WasRepaired)
{
    Reject(path, "The file's index was lost; ask the sender for a sound copy.");
}
else
{
    Store(path, document.Diagnostics);   // keep the account beside the file
}
```

Apply it once you have read what you need from the document, or once you have validated it — not straight after
`Open`. The index can be rebuilt after `Open` returned, when a read asks for an object the index lacks while the
index may have lost entries, or for one that is neither where the index says nor near it
([Lazy reading](../concepts/lazy-reading.md) lists when): `WasRepaired` turns true then, and a policy applied
before that read accepts a file it would have refused after it.

## Read the trailer after reading the document

A rebuild merges the trailers it finds — an older revision's `/Info`, or `/Encrypt` — and the catalog it finds into a new
trailer, which `Trailer`, `Catalog` and `IsEncrypted` give from then on; a trailer you took before keeps none of it. Take
the trailer once you have read what you need, rather than keep the one you took at opening:

```csharp
using AdCodicem.Pdf.Objects;

foreach (var number in document.ObjectNumbers)
{
    _ = document.GetObject(new PdfObjectId(number));   // may rebuild the index
}

var info = document.Trailer.GetDictionary(PdfName.Info);   // the rebuilt trailer's, if a rebuild ran
```

The loop itself is safe: it goes on over the numbers the index held when it began, whatever a read in it rebuilds.

## Keep the account

The diagnostics are a returned value, not a log: store them beside the file, serialize them, or assert on them in
your tests. A document keeps 1,000 entries at most by default; `SuppressedCount` says how many it dropped past that,
and `PdfReaderOptions.DiagnosticCapacity` raises the bound
([Tune the reader's memory](tune-reader-memory.md#keep-more-diagnostics)).

## See also

- [Diagnostics](../reference/diagnostics.md), in the reference: every severity and code.
- [Diagnostics](../concepts/diagnostics.md), explained: why the reader repairs and reports rather than refuses.
