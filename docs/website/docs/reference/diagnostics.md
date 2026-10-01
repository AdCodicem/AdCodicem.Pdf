---
title: Diagnostics
description: The entries the reader writes in a document's diagnostics, their severities and codes, and the exceptions that end an operation instead.
---

# Diagnostics

What the reader records in `PdfDocument.Diagnostics` when it works around a file, and the exceptions it throws
when it cannot. Why the reader reports rather than refuses is explained in [Diagnostics](../concepts/diagnostics.md);
the verdict on the file itself is the validator's, in [Validation](validation.md).

## `PdfDiagnostic`

One entry, a record struct in `AdCodicem.Pdf.Diagnostics`.

| Member | What it is |
|---|---|
| `Severity` | A `PdfDiagnosticSeverity`, below |
| `Code` | The code, such as `xref.rebuilt`: stable, and what you filter on — one of the constants of `PdfDiagnosticCodes` |
| `Message` | What happened, in English, for a person to read |
| `Position` | The byte offset in the file the entry relates to, or -1 when it relates to none |

`ToString()` gives `Severity Code at Position: Message`, or `Severity Code: Message` when there is no position:

```text
Repair xref.rebuilt: The cross-reference index was rebuilt by scanning the file.
Warning stream.length-invalid at 5501: The stream declared 19954 bytes but ended after 19952.
```

## `PdfDiagnostics`

The list `PdfDocument.Diagnostics` holds, in the order the entries were recorded. It is an
`IReadOnlyList<PdfDiagnostic>`.

| Member | What it is |
|---|---|
| `Count`, the indexer, enumeration | The entries kept |
| `Capacity` | The most entries kept: `PdfReaderOptions.DiagnosticCapacity` for a document, 1,000 by default |
| `SuppressedCount` | How many entries were dropped because `Capacity` was reached; they are counted, not kept |
| `HasRepairs` | Whether any entry is a `Repair` |
| `HasWarnings` | Whether any entry is a `Warning` |
| `HasConformanceLoss` | Whether any entry is a `ConformanceLoss` |
| `Contains(code)` | Whether any entry carries the code, compared ordinally |
| `Add`, `Repair`, `Warn` | Record an entry; the library's own, public so that a caller can collect its own |

`new PdfDiagnostics()` collects what decoding one stream reports, when it is passed to `Decode`:

| `Decode(stream, diagnostics)` | Where the reports go |
|---|---|
| `diagnostics` given | Into it, and nowhere else |
| none, a stream read from a document | The document's own `Diagnostics` |
| none, a stream built in memory | Nowhere: nothing is reported |

A stream that is never decoded is never reported: nothing reads it.

## Severities

`PdfDiagnosticSeverity`:

| Severity | Meaning |
|---|---|
| `Information` | Worth knowing, changes nothing |
| `Repair` | The file was not conforming and the reader worked around it |
| `Warning` | Something is wrong and the result may not be what you expect |
| `ConformanceLoss` | A guarantee such as PDF/A or PDF/UA was lost by an operation |

## Codes

Codes are stable: they are part of the public contract, because callers filter on them. No code is ever a
[validation rule identifier](validation-rules.md).

| Code | Raised when |
|---|---|
| `header.missing` | The file has no `%PDF-` header in its first 4,096 bytes; it was read all the same, its offsets counted from its first byte |
| `xref.rebuilt` | The index was rebuilt by scanning the whole file |
| `xref.offset-adjusted` | An object, or a cross-reference section the chain names, was not where the file said, and was found nearby |
| `xref.section-missing` | A cross-reference section `/Prev` or `/XRefStm` names is neither there nor nearby, lies outside the file, or is named by a value that is not an offset; what only it indexed is found by rebuilding the index when it is asked for. Reported where the section was named, or, when that lies outside the file, at the section whose trailer named it |
| `xref.chain-cycle` | The chain of previous sections looped; reported at the section it looped back to, or, when that offset lies outside the file, at the section whose trailer named it |
| `xref.entry-out-of-range` | An object's entry places it outside the file; reported with no position, the offset in the message. A section named outside the file is `xref.section-missing`'s, or the rebuild's when `startxref` names it |
| `stream.length-invalid` | A stream's `/Length` is not where its data ends: its `endstream` lies elsewhere and ends the data, or the `/Length` is no length — absent, not a non-negative integer, or naming an object the file lacks or that could not be read — and the `endstream` ends the data, or no `endstream` follows the declared length — none before the next object or the end of the file, or none looked for once the document's searches read as much as they may —, and that length is kept |
| `stream.truncated` | A stream has no `endstream` before the end of the file, or before the `endobj` that follows its data, and its data runs to the end of the file |
| `stream.self-reference` | An object stream's dictionary names an object the stream holds — as its `/Length`, `/N`, `/First`, a filter or a parameter —: that object reads as null while the stream is decoded, and the stream is decoded without it |
| `syntax.unexpected-token` | A token stood where the syntax does not allow it: where a value or a dictionary key was expected, after a key that has no value, closing an array with a dictionary's end, or in an object stream's header; it was read as null, skipped, or it ended what it stood in |
| `syntax.truncated-object` | The file ended in the middle of an object |
| `syntax.depth-exceeded` | Nesting went deeper than the reader will follow |
| `object.redefined` | An object was defined more than once; the last definition won |
| `trailer.root-recovered` | The trailer's `/Root` does not lead to a document catalog, and the catalog was found among the file's objects — those its index holds when the index is sound, a rebuilt index's otherwise |
| `filter.failed` | A filter's data is damaged: left encoded when nothing decoded before the byte the fault lies in, kept up to that byte or to the end otherwise, and a repair when nothing was lost |
| `filter.checksum-mismatch` | A Flate stream decoded to its end, but its zlib checksum disagrees with what it decoded to: all of it was kept, and some of it may be wrong |
| `filter.unsupported` | The file names a filter the library does not implement |
| `limit.decoded-stream` | A stream decodes to more than `MaxDecodedStreamLength`; the part within it was kept |
| `limit.object` | An object is longer than `MaxObjectLength`, its stream data aside; the part within it was parsed |
| `limit.xref-section-length` | A classic cross-reference table is longer than `MaxXRefSectionLength`; the entries within it were read |
| `limit.xref-section-count` | The chain of cross-reference sections is longer than `MaxXRefSectionCount`; the newest were read |
| `limit.trailer` | A trailer, or a cross-reference stream's dictionary, is longer than `MaxTrailerLength`; the part within it was parsed |

The five `limit.*` codes report the reader's own limits, not faults of the file; each message names the property of
`PdfReaderLimits` that lifts it. They are listed with their defaults in [Reader limits](reader-limits.md).

### Streams whose length is wrong

A stream whose declared length the file cannot hold is reported as `stream.truncated` when the file ends inside its
data, and as `stream.length-invalid` when its `endstream` comes first. Each stream is reported once, however often it
is parsed again — after the cache let it go, or the index was rebuilt. How the reader finds where such a stream ends
is explained in [Diagnostics](../concepts/diagnostics.md#where-a-stream-ends).

```text
Warning stream.length-invalid at 5501: The stream declared 19954 bytes but ended after 19952.
Warning stream.length-invalid at 19901349: The stream declared 202154 bytes, and no endstream follows them before the next object, at 20103524; the declared length is kept.
Warning stream.length-invalid at 316: The stream's /Length is a real number, 61.5, not a non-negative integer; its data ends after 68 bytes.
Warning stream.length-invalid at 542577: The stream's /Length names object 18 0, which holds a stream, not a non-negative integer; its data ends after 5555 bytes.
Warning stream.truncated at 315: The stream has no endstream before the endobj that follows its data; the 310 bytes to the end of the file are taken as its data.
```

### Damaged stream data

| Stream | Code and severity | What `Decode` returns |
|---|---|---|
| Flate, lost its tail — a file cut short, a producer that stopped writing | `filter.failed`, Warning | What decoded before the end |
| Flate, lost only the zlib checksum after its last block | `filter.failed`, Repair | All of it, unchecked |
| Flate, no zlib header at all | `filter.failed`, Repair | All of it |
| Flate, turns corrupt | `filter.failed`, Warning | What decoded before the byte the fault lies in |
| Flate, checksum disagrees with what decoded to its end | `filter.checksum-mismatch`, Warning | All of it; some may be wrong |
| LZW, uses a code it has not defined | `filter.failed`, Warning, naming the code | What decoded before the code |
| LZW, no end-of-data code | — | All of it, taken as complete |
| Flate or LZW, a predictor whose parameters describe rows longer than the decoded data | `filter.failed`, Warning | The data as decoded, the predictor not undone |
| Nothing decodes before the fault | `filter.failed`, Warning | The data, still encoded |
| Decodes past `MaxDecodedStreamLength` | `limit.decoded-stream`, Warning | The data up to the bound |

The byte counts in these reports are the Flate filter's own. The bytes kept are counted before any predictor
(`/DecodeParms` `/Predictor`) is applied, so `Decode` may return another length — a PNG predictor takes a byte off
each row. The byte where the fault was found counts from the start of the data the Flate filter was given, and the
length the report gives is that data's: the stream's data when Flate is its only filter, what the filter before it
decoded to otherwise. A stream the reader itself cut at one of its limits is reported as that limit, not as a lost
tail; a fault in the part it did read is the file's, and is reported.

```text
Warning filter.failed at 59534: A Flate stream ends before its data does; what decoded before the end was kept.
Repair filter.failed at 63982: A Flate stream ends before its checksum does; its data decoded whole, unchecked.
Warning filter.failed at 2102: A Flate stream is corrupt at byte 889 of its 20624; the 847 bytes decoded before the fault was found were kept.
Warning filter.checksum-mismatch at 3278: A Flate stream's checksum disagrees with the 174803 bytes its data decoded to; all were kept, and some may be wrong.
```

## Exceptions

Anything the reader can work around is a diagnostic. These end the operation instead; all derive from
`PdfException`, in `AdCodicem.Pdf.Diagnostics`.

| Exception | Thrown when |
|---|---|
| `PdfFormatException` | The input is not a PDF file, or is damaged beyond repair: it is empty, or no PDF object can be found in it |
| `PdfEncryptedException` | The document is encrypted and cannot be opened with the credentials given. Decryption is not supported yet, so `PdfDocument.Open` throws it for any encrypted document while `PdfReaderOptions.ThrowOnEncrypted` is true, its default; set to false, the document opens without being decrypted |
| `PdfLimitExceededException` | A reader limit is reached and the document was opened with `PdfReaderOptions.ThrowOnLimit`; see [Reader limits](reader-limits.md#throwonlimit) |
