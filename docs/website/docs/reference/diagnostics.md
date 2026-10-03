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
| `Position` | The byte offset in the file the entry relates to, or -1 when it relates to none. An offset a section's trailer or an object's entry names outside the file is no position: the entry is placed at the section that named it, or has none, the offset in the message. The `object-stream.*` entries, and a fault met inside an object an object stream holds, are placed where the stream's data starts, the byte of decoded data in the message |

`ToString()` gives `Severity Code at Position: Message`, or `Severity Code: Message` when there is no position:

```text
Repair xref.rebuilt: The cross-reference index was rebuilt by scanning the file.
Warning stream.length-invalid at 5501: The stream declared 19954 bytes but ended after 19952.
```

## How a message quotes the file

A message, of a diagnostic or of a [validation finding](validation.md#pdfvalidationfinding), is written for a person and
read where a host prints it — a console, a log, a web page. What the file wrote reaches it under these rules:

- **Printable ASCII, on one line.** Every character of a message lies between U+0020 and U+007E, and numbers are written
  in the invariant culture, whatever `CultureInfo.CurrentCulture` is: `-7 bytes`, `1,048,576 bytes`, never a culture's
  own minus sign or separators. The same holds for `ToString()`.
- **A name or a keyword is quoted as a PDF writer writes a name** (ISO 32000-1, 7.3.5): a byte of printable ASCII as
  itself, except the number sign and the delimiters `( ) < > [ ] { } / %`; every other byte, white space and control
  characters included, as `#` and two uppercase hexadecimal digits. A name keeps its solidus: `/Pa#0Age#1B` is the name
  the file wrote as the bytes `Pa`, a line feed, `ge`, an escape. In a keyword, which has no solidus, `#xx` stands for a
  byte all the same: `tra#1Biler`.
- **Past 127 bytes, a quote is cut**, and says how long the whole is: a name of a megabyte of `A` is quoted as a solidus,
  its first 127 `A`, and ` (the first 127 of 1,048,576 bytes)`. 127 bytes is the longest name PDF/A allows, so a name
  such a file holds is quoted whole. The cut never splits a `#xx`, and
  the space before the note is one no quote holds. The whole name stays in the document: `PdfName.Value` keeps every
  byte.
- **A path of keys** — `/Resources/Font/F1` in a finding — writes each key as a name. Past eight steps it keeps its
  first four and its last four, and says how many lie between: `/A/B/C/D (3 steps) /H/I/J/K`.
- **A string is never quoted.** A string is the document's content, not its syntax: a message says where it lies, not
  what it holds.

These rules are the library's own messages'. A name a caller builds can hold characters past U+00FF, which no file can:
a quote writes their UTF-8 bytes as `#xx`, counts those bytes against the 127 and in the whole, and stops before a
character it cannot write whole.

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
| `xref.section-missing` | A cross-reference section `/Prev` or `/XRefStm` names holds nothing that reads as a section, nor does any place nearby, lies outside the file, or is named by a value that is not an offset; what only it indexed is found by rebuilding the index when it is asked for. Reported at the offset the section was named at, or, when that offset lies outside the file or the value is not an offset, at the section whose trailer named it |
| `xref.section-unreadable` | A cross-reference section `/Prev` or `/XRefStm` names is there and cannot be read — a stray token among a table's rows or in place of its trailer, a subsection numbering objects past 2,147,483,647, a stream whose `/W`, `/Index`, `/Size` or data cannot give rows, or whose `/W`, `/Index`, `/Size`, `/Filter` or `/DecodeParms` is a reference no section read before it places where it can be read without loading it — and nothing nearby can be read in its place; the rows read before the fault are kept, and what only the rest indexed is found by rebuilding the index when it is asked for. Reported at the section, the fault in the message |
| `xref.chain-cycle` | The chain of previous sections looped: a `/Prev` or `/XRefStm` names an offset the chain has already reached, read or, outside the file, named. Reported at the section it looped back to, or, when that offset lies outside the file, at the section whose trailer named it |
| `xref.entry-out-of-range` | An object's entry places it outside the file; reported with no position, the offset in the message. A section named outside the file is `xref.section-missing`'s, or the rebuild's when `startxref` names it |
| `stream.length-invalid` | A stream's `/Length` is not where its data ends: its `endstream` lies elsewhere and ends the data, or the `/Length` is no length — absent, not a non-negative integer, or naming an object the file lacks or that could not be read — and the `endstream` ends the data, or no `endstream` follows the declared length — none before the next object or the end of the file, or none looked for once the document's searches read as much as they may —, and that length is kept. A cross-reference stream whose `/Length` the chain could not read, its data taken up to its first `endstream`, has it read once the chain is: a `/Length` that is no length, or that no `endstream` confirms, is reported as any stream's, and one an `endstream` past the one the chain stopped at confirms is reported with the rows the chain left unread |
| `stream.truncated` | A stream has no `endstream` before the end of the file, or before the `endobj` that follows its data, and its data runs to the end of the file |
| `stream.self-reference` | An object stream's dictionary names an object the stream holds — as its `/Length`, `/N`, `/First`, a filter or a parameter —: that object reads as null while the stream is decoded, and the stream is decoded without it |
| `object-stream.member-moved` | An object is in the object stream its entry names, at another index than the entry gives, and was read where the stream's header lists it. Reported where the stream's data starts, the object, the stream and both indexes in the message |
| `object-stream.unreadable` | What an object stream — a stream an entry of the index names as one — says of itself cannot be believed: its `/N` or `/First` is absent or no non-negative integer, its `/N` declares more objects than its header can list, its `/First` lies past its decoded data, or its header ends or breaks before listing as many objects as `/N` declares — a pair that is not two integers, or that gives a number no object can have or an offset no member can start at. The objects listed before the fault are read, the others cannot be read from it. Reported where the stream's data starts, the stream and its fault in the message |
| `syntax.unexpected-token` | A token stood where the syntax does not allow it: where a value or a dictionary key was expected, after a key that has no value, or closing an array with a dictionary's end; it was read as null, skipped, or it ended what it stood in |
| `syntax.truncated-object` | An object ended before it was whole: the file, or an object stream's decoded data, ended in the middle of it — a value missing, or an array, a dictionary or a string it opened never closed —, or an `endobj` stood where a value or a key should be, inside an array or a dictionary it opened. What was read is kept: a string takes the bytes to the end of the data, and an `endobj` ends every container it finds open, which takes nothing of the objects after it. Reported once, for the innermost construct left open, where it opens — where the value would start, when none is open —, the constructs around it counted in the message; inside an object an object stream holds, where the stream's data starts, the member and the byte in the message. The edge of a window the reader grows is no end of the data, and a cut a guard made is the guard's |
| `syntax.depth-exceeded` | Nesting went deeper than the reader will follow |
| `syntax.number-out-of-range` | A number is beyond what a real can hold — its magnitude rounds past the largest a double holds, about 1.8 × 10³⁰⁸ —: it was read as null, so a dictionary holds no entry for its key, and an array holds a null in its place. Reported where the number starts — where its object stream's data starts, for a number inside an object one holds —, quoted in the message. An integer past the range of a `long` is no fault of its own: it reads as the real nearest to it |
| `object.redefined` | Rebuilding the index met more than one definition of an object number. Raised once for each rebuild, as `Information` with no position: how many definitions met a number already found, the first ten of those numbers, and which definition was kept — the last written directly in the file, or, for a number written only inside object streams, the first listed in the object stream read first |
| `trailer.root-recovered` | The trailer's `/Root` does not lead to a document catalog, and the catalog was found among the file's objects — those its index holds when the index is sound, a rebuilt index's otherwise |
| `filter.failed` | A filter's data is damaged: left encoded when nothing decoded before the byte the fault lies in, kept up to that byte or to the end otherwise, and a repair when nothing was lost |
| `filter.checksum-mismatch` | A Flate stream decoded to its end, but its zlib checksum disagrees with what it decoded to: all of it was kept, and some of it may be wrong |
| `filter.unsupported` | The file names a filter the library does not implement; decoding stopped there, with what the filters before it decoded |
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

### Object streams

The `object-stream.*` entries, and the faults met inside the objects an object stream holds, are placed where the
stream's data starts in the file: a byte of its decoded data is no offset in the file, so the message gives it, with
the object stream's number and, for a fault inside an object, that object's. Each object stream's own fault is
reported once, and each object found at another index than its entry gives once, however often the stream is decoded
or the object parsed again.

```text
Repair object-stream.member-moved at 274206: Object 2 is at index 65540 of object stream 65547, not at index 4, where the cross-reference index places it.
Warning object-stream.unreadable at 1037994: Object stream 6396 gives in /First an offset, 29,927, past the end of its decoded data, which is 21,501 bytes long; none of its objects can be read from it.
Warning syntax.unexpected-token at 125: A token was found where a value was expected. It was met in object 3, at byte 112 of object stream 4's decoded data.
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
