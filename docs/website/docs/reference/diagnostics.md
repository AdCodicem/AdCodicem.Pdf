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
| `Position` | The byte offset in the file the entry relates to, or -1 when it relates to none. An offset equal to the file's length designates its end, where the file stops short of what it should hold. An offset a section's trailer or an object's entry names outside the file — past its end, before its start, or past what a `long` holds once the header's offset is added — is no position: the entry is placed at the section that named it, or has none, the offset in the message. The `object-stream.*` entries, and a fault met inside an object an object stream holds, are placed where the stream's data starts, the byte of decoded data in the message |

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
| `diagnostics` given | Into it, each time the stream is decoded; the document's own `Diagnostics` also hold, once, the `limit.stream-length` the reader reports as it reads data `MaxStreamLength` cuts. What loading a value the filters read meets — the `/Filter` entry, a filter's parameters — is the document's alone, a guard that cut it included, which ends decoding there |
| none, a stream read from a document | The document's own `Diagnostics` |
| none, a stream built in memory | Nowhere: nothing is reported |

A stream that is never decoded reports no fault of its decoding: nothing decodes it.

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
| `xref.section-missing` | A cross-reference section `/Prev` or `/XRefStm` names holds nothing that reads as a section, nor does any place nearby, lies outside the file, or is named by a value that is not an offset; what only it indexed is found by rebuilding the index when it is asked for. Reported at the offset the section was named at, or, when that offset lies outside the file or the value is not an offset, at the section whose trailer named it. A `/Prev` or `/XRefStm` the reader follows — not a hybrid file's cross-reference stream's own `/Prev`, nor the links of a table an `/XRefStm` names, which it leaves unread —, written as a reference that leads to an object one of the reader's limits cut, names what the guard hides: no section is reported missing, nothing is rebuilt, the chain ends there, and the guard is reported once the chain is read |
| `xref.section-unreadable` | A cross-reference section `/Prev` or `/XRefStm` names is there and cannot be read — a stray token among a table's rows or in place of its trailer, a subsection numbering objects past 2,147,483,647, a stream whose `/W`, `/Index`, data or — with no `/Index` — `/Size` cannot give rows, or whose `/W`, `/Index`, `/Filter` or `/DecodeParms`, or `/Size` with no `/Index`, is a reference no section read before it places where it can be read without loading it, or whose `/Length` is such a reference, or leads to one through others, and no `endstream` follows its data before the next object the sections read so far place, or before the end of the file, or none was looked for once the document's searches for `endstream` read as much of the file as they may — and nothing nearby can be read in its place; the rows read before the fault are kept, and what only the rest indexed is found by rebuilding the index when it is asked for. Reported at the section, the fault in the message. A value that names an object one of the reader's limits cut, directly or through a chain of references — a `/Length` whose data no `endstream` ends before that stop among them —, is the guard's, not the file's: the section is not reported, nor looked for nearby, the chain ends there, nothing is rebuilt, what only that section and the older ones index reads as null, and the guard is reported once the chain is read — but for what makes the section unreadable whatever the value holds, which is the file's and reported here, as read whole: a value past it no section read so far places, a `/W` that gives no three widths or none past 0, or rows numbered wrongly as far as the values read say — a subsection wrong by the element read whatever the cut one holds among them |
| `xref.chain-cycle` | The chain of previous sections looped: a `/Prev` or `/XRefStm` names an offset the chain has already named, whether it read a section there or not. Reported at that offset, or, when it lies outside the file, at the section whose trailer named it |
| `xref.entry-out-of-range` | An object's entry places it outside the file — past its end, before its start, or past what a `long` holds once the header's offset is added —; reported with no position, the offset in the message, exactly. A section named outside the file is `xref.section-missing`'s, or the rebuild's when `startxref` names it |
| `stream.length-invalid` | A stream's `/Length` is not where its data ends: its `endstream` lies elsewhere and ends the data; or the `/Length` is no length — absent, not a non-negative integer, or naming an object the file lacks or that could not be read — and the `endstream` ends the data; or no `endstream` lies before the next object or the end of the file, no `endobj` of its object ends the data, and the data is the declared length, when it ends before that stop, or runs up to the next object; or the document's searches for `endstream` have read as much of the file as they may, and the data is taken without a search — but where a guard hid the `/Length`, whose data is then the guard's. A `/Length` that names an object a guard cut, or left out of the index, is not judged, though a missing `endstream` is reported all the same. [Streams whose length is wrong](#streams-whose-length-is-wrong), below, gives each case. A cross-reference stream whose `/Length` the chain could not read, its data taken up to its first `endstream`, has it read once the chain is, unless a guard cut that data before every row the section declares — data that holds them all is checked whatever a guard did —: a `/Length` that is no length, or that no `endstream` confirms, is reported as any stream's, and one an `endstream` past the one the chain stopped at confirms is reported with the rows the chain left unread. Where the section's own row for that `/Length` lies among the rows the data holds, and gives a length an `endstream` confirms, the data is taken again to it before the rows are read, and nothing is reported; nor is anything of a `/Length` a guard cut or left out of the index |
| `stream.truncated` | A stream has no `endstream` before the next object or the end of the file, and its object's `endobj` ends its data — following the declared length, which is kept, or ending the object before that stop —; or neither, no declared length ends before the end of the file, and its data runs to it. Data taken without a search, the document's searches having read as much of the file as they may, is `stream.length-invalid`'s, to the end of the file too. [Streams whose length is wrong](#streams-whose-length-is-wrong), below, gives each case |
| `stream.self-reference` | An object stream's dictionary names an object the stream holds — as its `/Type`, `/Length`, `/N`, `/First`, a filter or a parameter —: that object reads as null while the stream is decoded, and the stream is decoded without it |
| `object-stream.member-moved` | An object is in the object stream its entry names, at another index than the entry gives, and was read where the stream's header lists it. Reported where the stream's data starts, the object, the stream and both indexes in the message |
| `object-stream.unreadable` | What an object stream — a stream an entry of the index names as one — says of itself cannot be believed: it is not of `/Type /ObjStm`, its `/N` is absent or no non-negative integer, or, when it declares objects, its `/First` is, or lies past its decoded data, or its header ends or breaks before listing as many objects as `/N` declares — a pair that is not two integers, that gives a number no object can have or an offset no member can start at, whose offset is not past the one before it, or whose member would start at or past the end of the decoded data, unless a guard cut it. The header is read as far as it goes, whatever its `/N` claims: the objects listed before the fault are read, the others cannot be read from it. Reported where the stream's data starts, the stream and its fault in the message. An object stream whose `/Filter`, `/DecodeParms` or a value of a step's parameters names an object a guard cut, or left out of the index, is left encoded and not reported: its objects are the guard's, unknown until it is lifted |
| `object-stream.member-stream` | An object an object stream holds is written as a stream, or holds one, which ISO 32000-1 (7.5.7) forbids: it reads as null, and nothing of it is decoded. Reported once for each object and each definition of its object stream, where the stream's data starts, the object, the stream and the byte of its decoded data where the object starts in the message |
| `syntax.unexpected-token` | A token stood where the syntax does not allow it: where a value or a dictionary key was expected, after a key that has no value, or closing an array with a dictionary's end; it was read as null, skipped, or it ended what it stood in |
| `syntax.truncated-object` | An object ended before it was whole: the file, or an object stream's decoded data, ended in the middle of it — a value missing, or an array, a dictionary or a string it opened never closed —, or an `endobj`, or the next object's header, stood where an element, a key or a value should be, inside an array or a dictionary it opened. What was read is kept: a string takes the bytes to the end of the data, and an `endobj` ends every container it finds open, as does the next object's header, which an object that lost its `endobj` too meets first, and which leaves the object without its `endobj`. A header is `N G obj` as the search for a stream's `endstream` stops at one: digits alone, N from 1 to 2,147,483,647 and G from 0 to 65,535, white space alone between them — no comment —, and `obj` a whole keyword; tokens that read as one inside a container that closes after them end it there all the same, and the object's `endobj` is judged missing. An object stream's member, which no header follows, and a value standing alone, which is no container, never end at one. A trailer, which is no object, ends at one all the same, and the message says the header came inside what it left open: `An object header came inside a dictionary, which was never closed.` Reported once, for the innermost construct left open, where it opens — where the value would start, when none is open; past the depth the reader follows, the container it skipped, which holds what it did not follow —, the constructs around it counted in the message; inside an object an object stream holds, where the stream's data starts, the member and the byte in the message. A member is read to the next member's start, the last to the end of the decoded data: running into either, it is cut short there, and the message says which. The edge of a window the reader grows is no end of the data, and a cut a guard made is the guard's — a member read past `MaxObjectLength` is `limit.object`'s |
| `syntax.depth-exceeded` | Nesting went deeper than the reader will follow |
| `syntax.number-out-of-range` | A number is beyond what a real can hold — its magnitude rounds past the largest a double holds, about 1.8 × 10³⁰⁸ —: it was read as null, so a dictionary holds no entry for its key, and an array holds a null in its place. Reported where the number starts — where its object stream's data starts, for a number inside an object one holds —, quoted in the message. An integer past the range of a `long` is no fault of its own: it reads as the real nearest to it |
| `syntax.hex-string-invalid` | A hexadecimal string holds bytes that are neither hexadecimal digits nor white space, the only bytes ISO 32000-1 (7.3.4.3) lets it hold — a `%` among them, which is no comment there —: they were skipped, as pdf.js and pdfium skip them, where PDFBox stops at the first, MuPDF and poppler read each as the digit 0, and qpdf and pypdf lose the string. Reported once for each string, where its first such byte lies, how many there are in the message; inside an object an object stream holds, where the stream's data starts, the member and the byte in the message |
| `syntax.name-escape-invalid` | A name holds a number sign that two hexadecimal digits do not follow: it was kept as the byte it is, as PDF 1.1 read it and most readers still do, where ISO 32000-1 (7.3.5) has a writer write `#23`. Reported once for each name, where its first such number sign lies, the name read quoted in the message; inside an object an object stream holds, where the stream's data starts, the member and the byte in the message. A `#00` is no such fault: it is an escape, of a byte no name may hold, which `object.name-null-character` reports |
| `syntax.key-repeated` | A dictionary gives a key more than once, which ISO 32000-1 (7.3.7) forbids: keys compare as they read, so a key written with `#xx` escapes repeats the one written without. The last value given is kept, and a null given last removes the key, as qpdf, pdf.js, PDFBox, MuPDF and pdfium read it; a key given with no value, before `>>` or `endobj`, is one given null, and a value a guard's cut leaves short removes nothing. Reported for each key given again, where it is given, unless a window's edge reaches the key itself, the key quoted in the message; inside an object an object stream holds, where the stream's data starts, the member and the byte in the message |
| `object.redefined` | Rebuilding the index met more than one definition of an object number. Raised once for each rebuild, as `Information` with no position: how many definitions met a number already found, the first ten of those numbers, and which definition was kept — the last written directly in the file, or, for a number written only inside object streams, the first listed in the object stream read first |
| `trailer.root-recovered` | The trailer's `/Root` does not lead to a document catalog, and the catalog was found among the file's objects — those its index holds when the index is sound, a rebuilt index's otherwise |
| `filter.failed` | A filter's data is damaged: Flate data of which nothing decoded before the byte the fault lies in is left as the filters before it decoded it, and the filters after it are not tried; otherwise what decoded is kept, up to that byte or to the end — nothing, for LZW data whose first code is undefined, and the filters after it decode that —, and a repair when nothing was lost. ASCII85, ASCIIHex, RunLength and LZW data that breaks its format — a byte that encodes nothing, an end marker missing or out of place — is decoded as other readers decode it (*Damaged stream data*, below) |
| `filter.checksum-mismatch` | A Flate stream decoded to its end, but its zlib checksum disagrees with what it decoded to: all of it was kept, and some of it may be wrong |
| `filter.unsupported` | The file names a filter the library does not implement, or an element of `/Filter` names no filter at all, or `/Filter` is neither a name nor an array; decoding stopped there, with what the filters before it decoded. A `/Filter` that is null is one the stream does not have, and is not reported. Nor is a `/Filter`, an element of it, a `/DecodeParms` or a value of a step's parameters that names an object a guard cut, or left out of the index — directly or through a chain of references —: what the cut left of it, such as a name it parted, is the guard's, and decoding ends there, as it does at the element past `MaxFilterChainLength`, with what the filters before it decoded; nothing more is reported, the guard having been reported where it was reached |
| `limit.decoded-stream` | A filter decodes to more than `MaxDecodedStreamLength`, or the filters before the last pass along more than it together; decoding stopped there, and the stream comes back as the filters after the cut decode what was kept within the bound |
| `limit.stream-length` | A stream's encoded data, as the file holds it, is longer than `MaxStreamLength`, which bounds what is read of it at once: the data within the bound was read, decoded as data the reader cut — what the filters lose past the cut is no damage — or handed back as it is where decoding hands data back undecoded, as with no filter, an image filter or a filter the library does not know. `Data.Length` and `RawLength` stay the length the file holds. Reported where the stream's data starts: once in the document's diagnostics, when its data is first read — by `GetRawBytes` or by decoding —, and in diagnostics of your own passed to `Decode` with each decoding, as `limit.decoded-stream` is |
| `limit.object` | An object is longer than `MaxObjectLength`, its stream data aside; the part within it was parsed. An object an object stream holds is bounded alike, and reported once for each definition of its object stream, where that stream's data starts, the object, the stream and the byte of its decoded data where the object starts in the message |
| `limit.xref-section-length` | A classic cross-reference table is longer than `MaxXRefSectionLength`; the entries within it were read, and its trailer past them; the objects the rows past them place read as null, and the chain is not followed further |
| `limit.xref-section-count` | The chain of cross-reference sections is longer than `MaxXRefSectionCount`; the newest were read, and what only the older ones index reads as null. Reported where the chain goes on, or, when that lies outside the file, at the section whose `/Prev` names it, the offset in the message |
| `limit.trailer` | A trailer, or a cross-reference stream's dictionary — never that stream's data —, is longer than `MaxTrailerLength`; the part within it was parsed, and the chain ends there: an older section, an `/XRefStm` or a `/Root` past the cut is unknown, what only the older sections index reading as null. Where the chain names a section and what lies there declares another type before the cut — a CIDFont, whose `/W` gives widths —, the guard cut no section: it is reported all the same, as the dictionary of the object read as a cross-reference stream, and the section is looked for nearby, the chain going on, as for a CIDFont's dictionary read whole, which no stream data follows |
| `limit.index-entries` | The index holds `MaxIndexEntries` object numbers — those in use, free or refused — and met another: the numbers it holds were indexed, and an object past them reads as null. Reported once a document, where the first number it had no room for was met: the section that gave it, the object header or object stream a rebuild read, or the chain's newest section for an entry the chain gave that a rebuild could not keep |
| `limit.filter-chain` | A stream's `/Filter` array names more filters than `MaxFilterChainLength`, `/Crypt` among them; decoding stopped before the first past them, with what the filters within the bound decoded, and the elements past them were not read. A cross-reference stream or an object stream it ends is read as cut before its data: its rows or its objects are unknown |

The eight `limit.*` codes report the reader's own limits, not faults of the file; each message names the property of
`PdfReaderLimits` that lifts it. They are listed with their defaults in [Reader limits](reader-limits.md).

### Faults of the syntax

A `syntax.*` entry says where the file breaks PDF's syntax, and what the reader read in its place. Each is reported
once, however often its object is parsed again — after the cache let it go, the index was rebuilt, or the validator
read it, a trailer the chain read and a rebuild read again included —, as long as the diagnostics have room for it;
once they are full, each reading counts it among the suppressed. A fault inside an object an object stream holds names
that object and the byte of the stream's decoded data: each object is read in its own window, up to the next one's
start, so no fault is two objects'.

```text
Warning syntax.truncated-object at 328: The file ended inside a literal string, which takes the 6,575 bytes from where it opens to that end; the 2 arrays or dictionaries around it were never closed either.
Warning syntax.truncated-object at 381: An endobj ended the object inside an array, which was never closed.
Warning syntax.truncated-object at 507: An object header ended the object inside a dictionary, which was never closed, before the value of its last key.
Warning syntax.hex-string-invalid at 1204: A hexadecimal string holds 5 bytes that are neither hexadecimal digits nor white space, the first here; they were skipped.
Warning syntax.name-escape-invalid at 96: A name holds a number sign that two hexadecimal digits do not follow, kept as the byte it is: the name reads as /A#23zz.
Warning syntax.key-repeated at 412: The dictionary gives the key /Filter more than once, written here with #xx escapes; the last value given is kept.
```

### Streams whose length is wrong

A declared length is confirmed by an `endstream` after it, white space and comments between, however many — a comment
only once white space comes before it: a `%` right where the declared length ends is the data's, and leaves the length
unconfirmed. Any other stream — its length unconfirmed, or none to confirm — is searched once for the first `endstream`
after the start of its data, up to the next object: the first object header the file's bytes hold after that start, or
the next object the file's index places, whichever is nearer. Each case is reported where the data starts, and each
stream once, however often it is parsed again — after the cache let it go, or the index was rebuilt. How the reader
finds where such a stream ends, and why, is explained in [Diagnostics](../concepts/diagnostics.md#where-a-stream-ends).

| What follows the data | Code | The data taken |
|---|---|---|
| An `endstream` after the declared length | None | The declared length |
| No such `endstream`, or no length to confirm; an `endstream` before the next object or the end of the file | `stream.length-invalid` | Up to that `endstream`, the end-of-line before it left out |
| No `endstream` before that stop; the object's `endobj` after the declared length, which ends before the stop, white space and comments between, as before an `endstream` | `stream.truncated` | The declared length |
| No `endstream` before the stop; the object's own `endobj` ends the object there, only white space and comments between them | `stream.truncated` | Up to that `endobj`, the end-of-line before it left out |
| None of the above; a declared length that ends before the stop | `stream.length-invalid` | The declared length; whether the object ends with its `endobj` is not judged |
| None of the above; no declared length that ends before the stop | `stream.length-invalid` at the next object, `stream.truncated` at the end of the file | Up to the stop; the object lacks its `endobj`, unless what is taken holds one |
| Not searched: the document's searches for `endstream` have read as much of the file as they may, and the bytes read with the object hold neither an `endstream` nor an object header | `stream.length-invalid`, the message saying so | The declared length, when it ends before the stop; otherwise up to the stop, the file past those bytes not searched for a header |

A file that ends on the `stream` keyword, or on the carriage return after it, holds a stream whose data starts where the
file ends and holds nothing: its missing `endstream` is reported as after a line feed, as the table gives it.

A `/Length` that names an object one of the reader's limits cut — directly or through a chain of references — gives no
length the file wrote: what the cut left of it, null or an integer it parted, is the guard's, which reported it. An
integer read before the cut is only a place to look for `endstream` at — the data ends there when one follows it —,
never kept as a length; the data is otherwise found by its `endstream`, as for a stream with no length, and nothing is
said of the length. A missing `endstream` is still the file's, and is reported, the message naming the object the guard
cut. A `/Length` that names an object a guard left out of the index, directly or through a chain of references, is not
judged either: the data is found by its `endstream` the same way, and a missing one is the file's, the message naming the
object the guard left out — `The stream's /Length names object 21 0, which one of the reader's limits left out of the
index, and no endstream follows its data`, then where the search stopped and what was taken. For either, an object
header the data spells, as an embedded PDF file's does, that stops the search — no `endobj` of the object's own before
it —, or an `endstream` the data spells, an `endobj` after it or not, leaves the data up to it to the guard: nothing is
reported of the stream, whose data is cut by the guard, and whether its object ends with `endobj` is not judged. An
`endstream` is taken for the stream's own, its end the file's, only where what follows its object's end — its
`endobj`, or the `endstream` where none follows — is the next object the index places, a cross-reference stream the
chain read, the end of the file, or the `xref` or `startxref` keyword. White space before the keyword, an end-of-line
above all, which only the hidden length says is the data's or not, leaves where the data ends to the guard as it
decodes: a lost tail, a missing checksum or end marker there is not reported, though a whole read reports it.

```text
Warning stream.length-invalid at 5501: The stream declared 19954 bytes but ended after 19952.
Warning stream.length-invalid at 19901349: The stream declared 202154 bytes, and no endstream follows them before the next object, at 20103524; the declared length is kept.
Warning stream.length-invalid at 316: The stream's /Length is a real number, 61.5, not a non-negative integer; its data ends after 68 bytes.
Warning stream.length-invalid at 542577: The stream's /Length names object 18 0, which holds a stream, not a non-negative integer; its data ends after 5555 bytes.
Warning stream.truncated at 315: The stream has no endstream: its object's endobj follows the 68 bytes it declared, which are taken as its data.
```

### Object streams

The `object-stream.*` entries, and the faults met inside the objects an object stream holds, are placed where the
stream's data starts in the file: a byte of its decoded data is no offset in the file, so the message gives it, with
the object stream's number and, for a fault inside an object, that object's. Each object stream's own fault is
reported once, each object found at another index than its entry gives once, and each object written there as a stream
once, however often the stream is decoded or the object parsed again.

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
| LZW, no end-of-data code, after a whole code | `filter.failed`, Repair | All of it, decoded to its end |
| LZW, no end-of-data code, inside a code — a whole byte no code completes, or a bit a padding leaves zero | `filter.failed`, Warning | All of it but the code's bits |
| ASCII85 or ASCIIHex, a byte that encodes nothing — outside the alphabet, or a `z` inside an ASCII85 group —, at the byte where the first lies | `filter.failed`, Warning | All of it, the byte passed over |
| ASCII85, a group whose value does not fit in four bytes | `filter.failed`, Warning | All of it, the group as its four low-order bytes |
| ASCII85, a `~` that something other than `>` follows | `filter.failed`, Warning | What decoded before the `~` |
| ASCII85, ends on a `~` with no `>`, or starts with the `<~` PostScript writes | `filter.failed`, Repair | All of it |
| ASCII85, a last group of a single digit before the `~` that closes the data, which encodes no byte | `filter.failed`, Warning | All of it but the digit |
| ASCII85, ASCIIHex or RunLength, no end marker after a whole group, pair or run | `filter.failed`, Repair | All of it, decoded to its end |
| ASCII85, ASCIIHex or RunLength, no end marker, inside a group, a pair or a run | `filter.failed`, Warning | All of it: the group completed with `u`, the digit with `0`, as a last one is; the run's bytes it holds; a lone ASCII85 digit dropped |
| Flate or LZW, a predictor whose parameters describe rows longer than the decoded data | `filter.failed`, Warning | The data as decoded, the predictor not undone |
| Flate, nothing decodes before the fault | `filter.failed`, Warning | The data as the filters before it left it — still encoded, when it is the first —, and the filters after it are not tried |
| A filter decodes past `MaxDecodedStreamLength`, or the filters before the last pass along more than it together | `limit.decoded-stream`, Warning | What the filters after the cut decode of the data up to the bound |
| Names more filters than `MaxFilterChainLength` | `limit.filter-chain`, Warning | The data as the filters within the bound decoded it |
| Data longer than `MaxStreamLength` | `limit.stream-length`, Warning | What the filters decode of the data up to the bound, as data the reader cut; that data as it is, when no filter decodes it |
| A `/Filter`, an element of it, a `/DecodeParms` or a value of a step's parameters names an object a guard cut, or left out of the index | None of its own: the guard's, reported where it was reached | The data as the filters before that value decoded it |

The byte counts in these reports are the reporting filter's own. The bytes kept are counted before any predictor
(`/DecodeParms` `/Predictor`) is applied, so `Decode` may return another length — a PNG predictor takes a byte off each
row. The byte where the fault was found counts from 1 at the start of the data the filter was given, and the length the
report gives is that data's: the stream's data when the filter is its only one, what the filter before it decoded to
otherwise. Each kind of fault is reported once for each filter of the stream, however often its data repeats it; a
filter stopped at `MaxDecodedStreamLength` counts the faults of the part it read, and says so. The document's
diagnostics hold what decoding a stream met once, however often the stream is decoded — by you, passing no diagnostics
or the document's, by the reader as it serves an object stream's objects again after its budget let them go or as it
rebuilds the index, or by the validator —, as long as they have room for it; diagnostics you pass of your own hold every
decoding's reports. A stream the reader itself cut at one of its limits is reported as that limit, not as a lost tail; a
fault in the part it did read is the file's, and is reported. That holds whoever cut it: the reader, reading the stream's
data within `MaxStreamLength`, or its object within `MaxObjectLength`, or a filter before this one that stopped at
`MaxDecodedStreamLength`. Of data the reader cut, no lost tail, missing checksum or missing end marker is reported, a
hexadecimal digit without its pair or an ASCII85 group short of five is dropped rather than completed, and a predictor
undoes the part of its last row the data holds rather than report rows the parameters describe as longer than the data.
Nor is any reported of data whose end a guard hides — its `endstream` proven its own past white space that only a
`/Length` a guard cut or left out of the index says is the data's or not —, which decodes as read whole.

```text
Warning filter.failed at 59534: A Flate stream ends before its data does; what decoded before the end was kept.
Repair filter.failed at 63982: A Flate stream ends before its checksum does; its data decoded whole, unchecked.
Warning filter.failed at 2102: A Flate stream is corrupt at byte 889 of its 20624; the 847 bytes decoded before the fault was found were kept.
Warning filter.checksum-mismatch at 3278: A Flate stream's checksum disagrees with the 174803 bytes its data decoded to; all were kept, and some may be wrong.
Warning filter.failed at 4336336: An ASCII85 stream ends without its ~> end marker, after a group of a single digit, which encodes no byte; it was dropped, and data may have been lost.
Warning filter.failed at 812: An ASCIIHex stream holds a byte that encodes nothing, at byte 5 of its 12; it was passed over.
Warning limit.stream-length at 9318: A stream's data runs past 256 MB; only what lies within it was read. Raise PdfReaderLimits.MaxStreamLength to read past it.
```

## Exceptions

Anything the reader can work around is a diagnostic. These end the operation instead; all derive from
`PdfException`, in `AdCodicem.Pdf.Diagnostics`.

| Exception | Thrown when |
|---|---|
| `PdfFormatException` | The input is not a PDF file, or is damaged beyond repair: it is empty, or no PDF object can be found in it |
| `PdfEncryptedException` | The document is encrypted and cannot be opened with the credentials given. Decryption is not supported yet, so `PdfDocument.Open` throws it for any encrypted document while `PdfReaderOptions.ThrowOnEncrypted` is true, its default; set to false, the document opens without being decrypted |
| `PdfLimitExceededException` | A reader limit is reached and the document was opened with `PdfReaderOptions.ThrowOnLimit`; see [Reader limits](reader-limits.md#throwonlimit) |
