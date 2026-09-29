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

The report is a returned value, not a log: it can be inspected, serialized, asserted on in your own tests,
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
| `xref.offset-adjusted` | An object, or a cross-reference section the chain names, was not where the file said, and was found nearby |
| `xref.section-missing` | A cross-reference section `/Prev` or `/XRefStm` names is neither there nor nearby; what only it indexed is found by rebuilding the index when it is asked for |
| `xref.chain-cycle` | The chain of previous sections looped |
| `xref.entry-out-of-range` | An entry pointed outside the file |
| `stream.length-invalid` | A stream's `/Length` is not where its data ends: its `endstream` lies elsewhere and ends the data, or the `/Length` is no length — absent, not a non-negative integer, or naming an object the file lacks or that could not be read — and the `endstream` ends the data, or no `endstream` follows the declared length — none before the next object or the end of the file, or none looked for once the document's searches read as much as they may —, and that length is kept |
| `stream.truncated` | A stream has no `endstream` before the end of the file, or before the `endobj` that follows its data, and its data runs to the end of the file |
| `stream.self-reference` | An object stream's dictionary names an object the stream holds — as its `/Length`, `/N`, `/First`, a filter or a parameter —: that object reads as null while the stream is decoded, and the stream is decoded without it |
| `syntax.unexpected-token` | A token was found where a value was expected |
| `syntax.truncated-object` | The file ended in the middle of an object |
| `syntax.depth-exceeded` | Nesting went deeper than the reader will follow |
| `object.redefined` | An object was defined more than once; the last definition won |
| `filter.failed` | A filter's data is damaged: left encoded when nothing could be decoded, kept as far as it decoded otherwise, and a repair when nothing was lost |
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

A stream's declared length is checked against the `endstream` that must follow it. When the data runs past
the window, the reader asks the file for the few bytes after the declared length rather than reading the
data, and they tell it whether `endobj` follows too. When no `endstream` is there, the first one after the
start of the data ends it, and where it is looked for depends on where the declared length ends:

- **Inside the window, or nowhere** — a `/Length` that gives no length —, the first `endstream` after the
  data is taken wherever it lies, past the header of the next object too: the reader looks in the window,
  and in a larger one while none is found, as far as `MaxObjectLength` allows.
- **Past the window**, the reader searches the file from the start of the data up to the next object the
  file's index places as the file wrote it — as the reader rebuilt it when the document opened, if the file
  wrote none —, or up to the end of the file.
  Stopping at the next object keeps a later object's `endstream` from ending this stream, which is what a
  stretch of zeros that erased the end of one and the objects after it would otherwise do; when none lies
  before it, the declared length is kept, and the report says so.

The search is not one of the reader's limits: a valid file's `endstream` follows its length, so only a
damaged file is searched, and once for each stream, with the same result whatever was read before it: an
index the reader rebuilds or corrects later does not move where the search stops. What the searches of one
document read together is bounded at a few times the file's length, which only a file whose objects overlap
reaches, one's header inside another's dictionary; past it, a stream keeps its declared length without a
search, and the report says so. Each stream is reported once, however often it is parsed again — after the
cache let it go, or the index was rebuilt.

```text
Warning stream.length-invalid at 5501: The stream declared 19954 bytes but ended after 19952.
Warning stream.length-invalid at 19901349: The stream declared 202154 bytes, and no endstream follows them before the next object, at 20103524; the declared length is kept.
Warning stream.length-invalid at 316: The stream's /Length is a real number, 61.5, not a non-negative integer; its data ends after 68 bytes.
Warning stream.length-invalid at 542577: The stream's /Length names object 18 0, which holds a stream, not a non-negative integer; its data ends after 5555 bytes.
Warning stream.truncated at 315: The stream has no endstream before the endobj that follows its data; the 310 bytes to the end of the file are taken as its data.
```

Damaged stream data decodes as far as it goes, and what decoded is kept, with a report. A Flate stream whose
tail was lost, as a file cut short or a producer that stopped writing leaves it, is a warning: what decoded
before the end is all there is. A Flate stream that lost only the zlib checksum after its last block decoded
whole, unchecked, and is a repair, as a stream with no zlib header at all is. A Flate stream that turns
corrupt is a warning too; decoding stops at the fault, and the last stretch decoded before it, up to 64 KB, is
lost with it. An LZW stream that uses a code it has not defined is a warning naming the code: decoding stops
there, since what follows cannot be read reliably. An LZW stream without its end-of-data code is taken as
complete, as other readers take it. A stream the reader itself cut at one of its limits is reported as that
limit, not as a lost tail.

```text
Warning filter.failed at 59534: A Flate stream ends before its data does; what decoded before the end was kept.
Repair filter.failed at 63982: A Flate stream ends before its checksum does; its data decoded whole, unchecked.
```

These reports go to the diagnostics you pass to `Decode`. A stream read from a document and decoded without
any reports to the document's own `Diagnostics`, so that a document's stream never decodes in silence; a
stream you built in memory and decode without any has nowhere to report, and reports nothing.

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
