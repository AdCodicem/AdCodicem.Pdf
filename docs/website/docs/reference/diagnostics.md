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
  in the invariant culture, whatever `CultureInfo.CurrentCulture` is: `object -5`, `1,048,576 bytes`, never a culture's
  own minus sign or separators. The same holds for `ToString()`.
- **A count agrees with its noun, and a distance says which way it runs**
  ([#241](https://github.com/AdCodicem/AdCodicem.Pdf/issues/241)). A count is written in digits, and its noun is
  singular for one alone: `1 byte`, `2 bytes`, `the 1 object its /N declares`. A verb or a pronoun that follows it
  agrees with it too: `the 1 byte decoded before the fault was found was kept`. A distance is never negative: an
  object found near where the index placed it `was found 3 bytes before where the index said`, and a section found
  near where a `/Prev` named it `was found 12 bytes before it`.
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
`IReadOnlyList<PdfDiagnostic>`, which grows as the document is read, after opening too, and as a validation starts:
between two calls you make, entries are only added, after those it held.

| Member | What it is |
|---|---|
| `Count`, the indexer, enumeration | The entries kept. An enumeration gives those recorded when it began: what a read or a validation records meanwhile, one the loop makes included, `Count` counts at once and the next enumeration gives, and the enumeration never throws for it. Once it has ended, it stays ended, whatever is recorded after. A `PdfFileSource` of your own, stepping an enumeration from inside a read whose rebuild withdraws what it reported and reports again, reads, up to the count the enumeration began with, the entries held at those places, which may be those reported again; its `Current` stays the entry it advanced to, whatever is withdrawn or reported at that place after. A rebuild the source fails withdraws what it reported past the point it runs again from — its scan's start, or, for a finish a read under way waited for, the scan's end — before the failure leaves the call that ran it, and reports that again when the next read or validation runs it again; `xref.rebuilt`, reported as the rebuild begins, stays, as `WasRepaired` does, and is not reported again |
| `Capacity` | The most entries kept: `PdfReaderOptions.DiagnosticCapacity` for a document, 1,000 by default |
| `SuppressedCount` | How many entries were dropped because `Capacity` was reached; they are counted, not kept |
| `HasRepairs` | Whether any entry is a `Repair` |
| `HasWarnings` | Whether any entry is a `Warning` |
| `HasConformanceLoss` | Whether any entry is a `ConformanceLoss` |
| `Contains(code)` | Whether any entry carries the code, compared ordinally |
| `Add`, `Repair`, `Warn` | Record an entry; the library's own, public so that a caller can collect its own. Nothing stops them yet from writing into a document's own record ([#310](https://github.com/AdCodicem/AdCodicem.Pdf/issues/310)) |

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
| `xref.rebuilt` | The index was rebuilt by scanning the whole file; reported as the rebuild begins, it stays when the source fails the rebuild |
| `xref.offset-adjusted` | An object, or a cross-reference section the chain names, was not where the file said, and was found nearby. A section is never found where the chain read one, nor near a section that is there and cannot be read ([#188](https://github.com/AdCodicem/AdCodicem.Pdf/issues/188), [#281](https://github.com/AdCodicem/AdCodicem.Pdf/issues/281)). Where the nearest place to an offset that holds no section declares itself one — a table's `xref` keyword, a stream of `/Type /XRef` — and cannot be read, it is the section found moved, not passed over for an older section farther off: `xref.section-unreadable` follows, at it, and a stream whose dictionary was read whole hands its `/Prev` on ([#281](https://github.com/AdCodicem/AdCodicem.Pdf/issues/281), [#208](https://github.com/AdCodicem/AdCodicem.Pdf/issues/208)). Each report after it on a section found moved names the offset that names it and where it was found |
| `xref.section-missing` | A cross-reference section `/Prev` or `/XRefStm` names holds nothing that reads as a section, nor does any place nearby — a place where a section the chain reached starts is not tried ([#188](https://github.com/AdCodicem/AdCodicem.Pdf/issues/188)), and the nearest place that declares itself one and cannot be read is the section found moved, `xref.offset-adjusted`'s and `xref.section-unreadable`'s ([#281](https://github.com/AdCodicem/AdCodicem.Pdf/issues/281)) —, lies outside the file, or is named by a value that is not an offset; an object there that gives a `/W` and another `/Type`, a CIDFont, which does not read as a section, holds none ([#281](https://github.com/AdCodicem/AdCodicem.Pdf/issues/281)). What only it indexed is found by rebuilding the index when it is asked for. Reported at the offset the section was named at, or, when that offset lies outside the file or the value is not an offset, at the section whose trailer named it. A `/Prev` or `/XRefStm` the reader follows — not a hybrid file's cross-reference stream's own `/Prev`, nor the links of a table an `/XRefStm` names, which it leaves unread —, written as a reference that leads to an object one of the reader's limits cut, names what the guard hides: no section is reported missing, nothing is rebuilt, the chain ends there, and the guard is reported once the chain is read. A section whose rows a limit cut, or filled the index at, ends the chain, but what it names is judged as read whole without reading past the limit: a value that is no offset — a reference to a free row the reader read, or to a number a newer section that lost rows refused, among them —, or an offset outside the file, written directly or as a reference the reader follows through rows it read, is reported here, beside the limit, where one that may name a sound section — an offset in the file the chain has not reached, a reference to a row past the cut — is the limit's ([#264](https://github.com/AdCodicem/AdCodicem.Pdf/issues/264)). The `/Prev` of a cross-reference stream at which a limit's cut of a value its rows are read with ends the chain is not judged yet ([#329](https://github.com/AdCodicem/AdCodicem.Pdf/issues/329)) |
| `xref.section-unreadable` | A cross-reference section the chain names cannot be read whole. Named by `/Prev` or `/XRefStm`, it is there and cannot be read — a stray token among a table's rows or in place of its trailer, a subsection numbering objects past 2,147,483,647, a table the end of the file cuts before its first row, within its `trailer` keyword too ([#207](https://github.com/AdCodicem/AdCodicem.Pdf/issues/207)), a stream whose `/W`, `/Index`, data or — with no `/Index` — `/Size` cannot give rows, or whose `/W`, `/Index`, `/Filter` or `/DecodeParms`, or `/Size` with no `/Index`, is a reference no section read before it places where it can be read without loading it, or whose `/Length` is such a reference, or leads to one through others, and no `endstream` follows its data before the next object the sections read so far place, or before the end of the file, or none was looked for once the document's searches for `endstream` read as much of the file as they may. A table's `xref` keyword, or a stream whose dictionary gives `/Type /XRef`, is the section, reported where it is, and nothing nearby is taken for it ([#281](https://github.com/AdCodicem/AdCodicem.Pdf/issues/281)); one that is the nearest place to an offset holding no section is the section found moved, reported where it was found, after `xref.offset-adjusted`, as where it is named; a stream that only gives a `/W` may be another object, and is reported so only when nothing nearby can be read in its place. The rows read before the fault are kept, and what only the rest indexed is found by rebuilding the index when it is asked for. A classic table an `/XRefStm` names, past which the chain goes on through the table that names it, leaves no older section's row standing for the rest of the subsection its fault ended, nor, where what follows the fault cannot be known — a subsection header that is no header, a stray token, the end of the file —, for any number its rows read before the fault do not give: such an object is found by rebuilding the index, as the message says ([#156](https://github.com/AdCodicem/AdCodicem.Pdf/issues/156)). A stream of `/Type /XRef` whose dictionary was read whole and gives a `/Prev` — its fault in its rows or in a value they are read with — hands that `/Prev` on, and the chain goes on to the older sections, as the message says where the `/Prev` names an offset, or leads to what one of the reader's limits hides; one that is no offset is reported after it as `xref.section-missing`, the chain ending there ([#208](https://github.com/AdCodicem/AdCodicem.Pdf/issues/208)): the `/Prev` is read as a whole read reads it, under the same checks, and no older section's row stands for an object the stream numbers — through its `/Index`, or from 0 to its `/Size` less one —, which is found by rebuilding the index; where that numbering cannot be read, or is written with references, or the streams lost so declare more than 4,096 ranges of numbers, no older row stands for any object the sections read before do not index. The stream's dictionary is not merged into the document's trailer; nor does a hybrid file's stream of `/Type /XRef` that cannot be read, its dictionary read whole, leave an older section's row standing for an object it numbers ([#210](https://github.com/AdCodicem/AdCodicem.Pdf/issues/210)). The section `startxref` names that cannot be read hands nothing on: the whole file is rebuilt, which is all the reader reports ([#323](https://github.com/AdCodicem/AdCodicem.Pdf/issues/323)). Or the section — the one `startxref` names among them — was read only in part, and kept, the chain going on from it where its trailer was read ([#207](https://github.com/AdCodicem/AdCodicem.Pdf/issues/207), [#210](https://github.com/AdCodicem/AdCodicem.Pdf/issues/210)): a row that is no row ends a subsection of a table, the trailer or the next subsection's header following it; a cross-reference stream holds fewer rows than it declares; the end of the file cuts a table before its trailer, or within its `trailer` keyword, once it read a row; or a row that gives an object in use what no entry can hold is refused alone. The message says the section `was read only in part`, one fault — the first that lost rows, else the end of the file before its trailer, else the first row it refused, which is not named beside either of the others —, and what was lost. The rows read are kept, nothing is rebuilt at opening for them, and no section read after it stands for a number whose row it lost — the rest of each subsection a table's fault ended, kept as ranges joined where they meet and every number past 4,096 of them, every number a stream declares, a refused row's, and every number its rows do not give for a table an `/XRefStm` names that the end of the file cuts, what the rest of it held being unknown ([#156](https://github.com/AdCodicem/AdCodicem.Pdf/issues/156)) —: such an object, or one only the older sections a lost trailer would name index, is found by rebuilding the index when it is asked for. A guard that also cut the section leaves what the file lost the file's, reported here, the message saying how far the guard let the rows after a row refused alone be read, and that a stream whose rows a guard left unread, its data holding too few beside a value the guard cut, kept none; a section a guard cut that lost no row is the guard's alone. The message agrees with the count of rows a stream holds: the row it holds was kept, and of none nothing is said kept. Reported at the section, the fault in the message, once, whatever white space before the section the offsets that name it fall in ([#188](https://github.com/AdCodicem/AdCodicem.Pdf/issues/188)). A value that names an object one of the reader's limits cut, directly or through a chain of references — a `/Length` whose data no `endstream` ends before that stop among them —, is the guard's, not the file's: the section is not reported, nor looked for nearby, the chain ends there, nothing is rebuilt, what only that section and the older ones index reads as null, and the guard is reported once the chain is read — but for what makes the section unreadable whatever the value holds, which is the file's and reported here, as read whole: a value past it no section read so far places, a `/W` that gives no three widths or none past 0, or rows numbered wrongly as far as the values read say — a subsection wrong by the element read whatever the cut one holds among them —; data whose length the file gives, its filters written direct, decoded whole, that holds fewer rows than the counts read declare however few bytes the widths read leave a row is read as read whole instead, lacking rows, its trailer merged and the chain going on, where it was moved to too |
| `xref.section-empty` | A classic cross-reference table the chain names holds no subsection: its `trailer` keyword follows its `xref` keyword, where ISO 32000-1 has one or more subsections follow it. It was read as indexing nothing, as a table whose one subsection is `0 0` is: nothing is rebuilt for it, and the index is left whole ([#222](https://github.com/AdCodicem/AdCodicem.Pdf/issues/222)) — though a chain none of whose sections indexes anything, such a table alone among them, is rebuilt as the document opens, `xref.rebuilt` following. Reported at the table |
| `xref.chain-cycle` | The chain of previous sections looped: a `/Prev` names an offset the chain has already named — through a `/Prev` or an `/XRefStm` —, whether it read a section there or not, or the place where a section it reached starts, or the white space and comments before it: one it read, where it was named or found moved, past the white space its offset fell short by, or one that is there and could not be read, a cross-reference stream whose `/Prev` the chain went on through among them ([#188](https://github.com/AdCodicem/AdCodicem.Pdf/issues/188), [#208](https://github.com/AdCodicem/AdCodicem.Pdf/issues/208)). The message says whether the chain read the section, or only named it. An `/XRefStm` ends no chain: one that names what the chain has reached names a hybrid file's stream again, or is `xref.hybrid-stream-lost`'s ([#156](https://github.com/AdCodicem/AdCodicem.Pdf/issues/156)). Reported at the offset the `/Prev` names, or where the section it reaches past the white space there starts, or, when the offset lies outside the file, at the section whose trailer named it. A `/Prev` that names a stream the chain read as a newer table's own, through that table's `/XRefStm`, loops all the same, though the fault is the `/XRefStm`'s, which is not reported yet ([#330](https://github.com/AdCodicem/AdCodicem.Pdf/issues/330)). The `/Prev` of a section whose rows a limit cut, or filled the index at, is judged so too, without reading past the limit ([#264](https://github.com/AdCodicem/AdCodicem.Pdf/issues/264)); that of a cross-reference stream at which a limit's cut of a value its rows are read with ends the chain is not judged yet ([#329](https://github.com/AdCodicem/AdCodicem.Pdf/issues/329)) |
| `xref.hybrid-stream-lost` | A hybrid file's table names through `/XRefStm` no cross-reference stream of its revision: a classic table — the table itself, another the chain read, one it reads in the stream's place, its rows taken and its own links left unread, or one that cannot be read, reported beside its own `xref.section-unreadable` and at each table that names it —, a cross-reference stream the chain read through `/Prev` or `startxref`, or what the table's own `/Prev` reaches too — at the same offset, through the white space before it, or as the nearest section to an offset that holds none where that `/Prev` keeps its place, a section there and no font or string, which the reader does not read for the `/XRefStm` —, which the chain looks for through that `/Prev`, and reads whole, rather than in the stream's place ([#156](https://github.com/AdCodicem/AdCodicem.Pdf/issues/156)): reported before that `/Prev` is read, the message says no more of it than that, or that the offset lies outside the file; what only the table's stream indexed is found by rebuilding the index when it is asked for. So is what the `/XRefStm` read in the stream's place where the `/Prev`'s own search near the offset it names comes to it — a classic table, or a cross-reference stream whose own `/Prev` names an older section with no other section near —, which that `/Prev` reads again, whole: the table was reported as one whose rows were read in the stream's place, the stream is reported once the `/Prev` read it, as the section its `/Prev` names too, and what the first reading reported of the section is not reported again. A cross-reference stream more than one table names, as a writer that copies `/XRefStm` into the next trailer leaves it, is read once, where it is first named, whatever white space before its header each offset falls in, and reported by none. A table whose rows a limit cut, or filled the index at, has its `/XRefStm` judged so too, without reading past the limit: one that names what the chain has reached, or what the table's `/Prev` names too — at that offset or through the white space before that section —, is reported, though that `/Prev`, past the limit, reads nothing; one that names any other offset in the file the chain has not reached is the limit's ([#264](https://github.com/AdCodicem/AdCodicem.Pdf/issues/264)). Reported at the offset the `/XRefStm` names, or, when it lies outside the file, at the table |
| `xref.entry-out-of-range` | An object's entry places it outside the file — past its end, before its start, or past what a `long` holds once the header's offset is added —; reported with no position, the offset in the message, exactly. A section named outside the file is `xref.section-missing`'s, or the rebuild's when `startxref` names it |
| `xref.entry-reserved` | A cross-reference stream the chain reads gives rows of a reserved type: a type past 2, or a type field wider than 8 bytes whose leading bytes are not all zero. ISO 32000-1 (7.5.8.3) defines types 0, 1 and 2, and reads any other as a reference to the null object, so that later versions may define new types: each such row is read so, its object indexed as a free one's is and read as null, and no older section's row stands for it, nor is anything rebuilt for it ([#221](https://github.com/AdCodicem/AdCodicem.Pdf/issues/221)) — until the index is rebuilt for another fault, at opening or as a validation settles it, which reads the object as the scan finds it. Such rows take entries as free rows do, which `MaxIndexEntries` bounds. Reported once for each section, at the section, after what it reports of a section read only in part: the first such row in the message, the others counted |
| `stream.length-invalid` | A stream's `/Length` is not where its data ends: its `endstream` lies elsewhere and ends the data; or the `/Length` is no length — absent, not a non-negative integer, or naming an object the file lacks or that could not be read — and the `endstream` ends the data; or no `endstream` lies before the next object or the end of the file, no `endobj` of its object ends the data, and the data is the declared length, when it ends before that stop, or runs up to the next object; or the document's searches for `endstream` have read as much of the file as they may, and the data is taken without a search — but where a guard hid the `/Length`, whose data is then the guard's. A `/Length` that names an object a guard cut, or left out of the index, is not judged, though a missing `endstream` is reported all the same. [Streams whose length is wrong](#streams-whose-length-is-wrong), below, gives each case. A cross-reference stream whose `/Length` the chain could not read, its data taken up to its first `endstream`, has it read once the chain is, unless a guard cut that data before every row the section declares — data that holds them all is checked whatever a guard did —: a `/Length` that is no length, or that no `endstream` confirms, is reported as any stream's, and one an `endstream` past the one the chain stopped at confirms is reported with the rows the chain left unread. Where the section's own row for that `/Length` lies among the rows the data holds — in a predicted row the spelled `endstream` parts too, the data decoded once more knowing its end hidden —, and gives a length an `endstream` confirms, the data is taken again to it before the rows are read, and nothing is reported; where that decoding reaches `MaxDecodedStreamLength` short of the row, the rows past the bound are the guard's, reported under `limit.decoded-stream`; nor is anything of a `/Length` a guard cut or left out of the index |
| `stream.truncated` | A stream has no `endstream` before the next object or the end of the file, and its object's `endobj` ends its data — following the declared length, which is kept, or ending the object before that stop —; or neither, no declared length ends before the end of the file, and its data runs to it. Data taken without a search, the document's searches having read as much of the file as they may, is `stream.length-invalid`'s, to the end of the file too. [Streams whose length is wrong](#streams-whose-length-is-wrong), below, gives each case |
| `stream.self-reference` | An object stream's dictionary names an object the stream holds — as its `/Type`, `/Length`, `/N`, `/First`, a filter or a parameter —: that object reads as null while the stream is decoded, and the stream is decoded without it |
| `object-stream.member-moved` | An object is in the object stream its entry names, at another index than the entry gives, and was read where the stream's header lists it. Reported where the stream's data starts, the object, the stream and both indexes in the message |
| `object-stream.unreadable` | What an object stream — a stream an entry of the index names as one — says of itself cannot be believed: it is not of `/Type /ObjStm`, its `/N` is absent or no non-negative integer, or, when it declares objects, its `/First` is, or lies past its decoded data, or its header ends or breaks before listing as many objects as `/N` declares — a pair that is not two integers, that gives a number no object can have or an offset no member can start at, whose offset is not past the one before it, or whose member would start at or past the end of the decoded data, unless a guard cut it. The header is read as far as it goes, whatever its `/N` claims: the objects listed before the fault are read, the others cannot be read from it. Reported where the stream's data starts, the stream and its fault in the message. An object stream whose `/Filter`, `/DecodeParms` or a value of a step's parameters names an object a guard cut, or left out of the index, is left encoded and not reported: its objects are the guard's, unknown until it is lifted |
| `object-stream.member-stream` | An object an object stream holds is written as a stream, or holds one, which ISO 32000-1 (7.5.7) forbids: it reads as null, and nothing of it is decoded. Reported once for each object and each definition of its object stream, where the stream's data starts, the object, the stream and the byte of its decoded data where the object starts in the message |
| `syntax.unexpected-token` | A token stood where the syntax does not allow it: where a value or a dictionary key was expected, after a key that has no value, or closing an array with a dictionary's end; it was read as null, skipped, or it ended what it stood in |
| `syntax.truncated-object` | An object ended before it was whole: the file, or an object stream's decoded data, ended in the middle of it — a value missing, or an array, a dictionary or a string it opened never closed —, or an `endobj`, or the next object's header, stood where an element, a key or a value should be, inside an array or a dictionary it opened. What was read is kept: a string takes the bytes to the end of the data, and an `endobj` ends every container it finds open, as does the next object's header, which an object that lost its `endobj` too meets first, and which leaves the object without its `endobj`. A header is `N G obj` as the search for a stream's `endstream` stops at one: digits alone, N from 1 to 2,147,483,647 and G from 0 to 65,535, white space and comments between them, as a load reads them, and `obj` a whole keyword; tokens that read as one inside a container that closes after them end it there all the same, and the object's `endobj` is judged missing. An object stream's member, which no header follows, and a value standing alone, which is no container, never end at one. A trailer, which is no object, ends at one all the same, and the message says the header came inside what it left open: `An object header came inside a dictionary, which was never closed.` Reported once, for the innermost construct left open, where it opens — where the value would start, when none is open; past the depth the reader follows, the container it skipped, which holds what it did not follow —, the constructs around it counted in the message; inside an object an object stream holds, where the stream's data starts, the member and the byte in the message. A member is read to the next member's start, the last to the end of the decoded data: running into either, it is cut short there, and the message says which. The edge of a window the reader grows is no end of the data, and a cut a guard made is the guard's — a member read past `MaxObjectLength` is `limit.object`'s |
| `syntax.depth-exceeded` | Nesting went deeper than the reader will follow: a container nested past the parser's bound, or with too little stack left, is skipped; an object loaded past the reader's bound on nested loads, or with too little stack left, reads as null, reported once a document. Validation reports it too, once a document, for an object its walk of what an object stream's decoding reads had too little stack left to load, which it reads as null: `Object 1002 is reached through more nested objects than the thread's stack lets the validator follow: the validator reads it as null.` Where the walk was working out whether another object stream decodes alike in every read order, the message adds that it takes that stream for one that does not: `…, and takes object stream 9 for one that does not decode alike in every read order.` The validation report can then differ from one made on a thread with more stack |
| `syntax.number-out-of-range` | A number is beyond what a real can hold — its magnitude rounds past the largest a double holds, about 1.8 × 10³⁰⁸ —: it was read as null, so a dictionary holds no entry for its key, and an array holds a null in its place. Reported where the number starts — where its object stream's data starts, for a number inside an object one holds —, quoted in the message. An integer past the range of a `long` is no fault of its own: it reads as the real nearest to it |
| `syntax.hex-string-invalid` | A hexadecimal string holds bytes that are neither hexadecimal digits nor white space, the only bytes ISO 32000-1 (7.3.4.3) lets it hold — a `%` among them, which is no comment there —: they were skipped, as pdf.js and pdfium skip them, where PDFBox stops at the first, MuPDF and poppler read each as the digit 0, and qpdf and pypdf lose the string. Reported once for each string, where its first such byte lies, how many there are in the message; inside an object an object stream holds, where the stream's data starts, the member and the byte in the message |
| `syntax.name-escape-invalid` | A name holds a number sign that two hexadecimal digits do not follow: it was kept as the byte it is, as PDF 1.1 read it and most readers still do, where ISO 32000-1 (7.3.5) has a writer write `#23`. Reported once for each name, where its first such number sign lies, the name read quoted in the message; inside an object an object stream holds, where the stream's data starts, the member and the byte in the message. A `#00` is no such fault: it is an escape, of a byte no name may hold, which `object.name-null-character` reports |
| `syntax.key-repeated` | A dictionary gives a key more than once, which ISO 32000-1 (7.3.7) forbids: keys compare as they read, so a key written with `#xx` escapes repeats the one written without. The last value given is kept, and a null given last removes the key, as qpdf, pdf.js, PDFBox, MuPDF and pdfium read it; a key given with no value, before `>>` or `endobj`, is one given null, and a value a guard's cut leaves short removes nothing. Reported for each key given again, where it is given, unless a window's edge reaches the key itself, the key quoted in the message; inside an object an object stream holds, where the stream's data starts, the member and the byte in the message |
| `object.redefined` | Rebuilding the index met more than one definition of an object number. Raised once for each rebuild, as `Information` with no position: how many definitions met a number already found, the first ten of those numbers, and which definition was kept — the last written directly in the file, or, for a number written only inside object streams, the first listed in the object stream read first |
| `trailer.root-recovered` | The trailer's `/Root` does not lead to a document catalog, and the catalog was found among the file's objects — those its index holds when the index is sound, a rebuilt index's otherwise. A rebuild after opening that takes the same object again says nothing more; one that takes another object reports it |
| `filter.failed` | A filter's data is damaged: Flate data of which nothing decoded before the byte the fault lies in is left as the filters before it decoded it, and the filters after it are not tried; otherwise what decoded is kept, up to that byte or to the end — nothing, for LZW data whose first code is undefined, and the filters after it decode that —, and a repair when nothing was lost. ASCII85, ASCIIHex, RunLength and LZW data that breaks its format — a byte that encodes nothing, an end marker missing or out of place — is decoded as other readers decode it (*Damaged stream data*, below) |
| `filter.checksum-mismatch` | A Flate stream decoded to its end, but its zlib checksum disagrees with what it decoded to: all of it was kept, and some of it may be wrong. Data that decoded to nothing is said to have, and nothing more |
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

A rebuild's scan for trailers parses the dictionary after each `trailer` keyword alone, and reads no stream. A stream
nested in that dictionary, which no file may write there, ends what the rebuild merges of it: the key that holds it and
the keys after it are not merged. This is said of the rebuild's merge alone: a trailer the chain read keeps those keys,
whether the rebuild runs as the document opens or after, since the rebuild's merge adds to the trailer only the keys it
lacks, and the rebuild replaces only a `/Root` that leads to no catalog (`trailer.root-recovered`), wherever that key
is written ([Lazy reading](../concepts/lazy-reading.md)). A stream nested deeper than the reader follows — 128 levels,
or fewer on a thread with little stack, where the reader's stack probe cuts sooner — ends nothing: the container too
deep is skipped, under `syntax.depth-exceeded`, a `stream` keyword left after it is a stray token to the container
around it, and what follows it is read on as the keys or elements of that container. A reference written in the
dictionary's place is not followed, and nothing is merged for it. Each is noted once, as a warning under
`syntax.unexpected-token`, at the stream's keyword or where the reference starts; a stream after the dictionary itself
loses no key, and is not noted.

```text
Warning syntax.truncated-object at 328: The file ended inside a literal string, which takes the 6,575 bytes from where it opens to that end; the 2 arrays or dictionaries around it were never closed either.
Warning syntax.truncated-object at 381: An endobj ended the object inside an array, which was never closed.
Warning syntax.truncated-object at 507: An object header ended the object inside a dictionary, which was never closed, before the value of its last key.
Warning syntax.hex-string-invalid at 1204: A hexadecimal string holds 5 bytes that are neither hexadecimal digits nor white space, the first here; they were skipped.
Warning syntax.name-escape-invalid at 96: A name holds a number sign that two hexadecimal digits do not follow, kept as the byte it is: the name reads as /A#23zz.
Warning syntax.key-repeated at 412: The dictionary gives the key /Filter more than once, written here with #xx escapes; the last value given is kept.
Warning syntax.unexpected-token at 152: A stream inside the dictionary after the trailer keyword at offset 110 ends what the rebuild merges of it: the key that holds it and the keys after it are not merged.
Warning syntax.unexpected-token at 164: The trailer keyword at offset 156 is followed by a reference to object 5, not by a dictionary; the rebuild does not follow it.
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
chain read, the end of the file, or the `xref` or `startxref` keyword. White space or comments before the keyword, an
end-of-line above all, which only the hidden length says are the data's or not, leave where the data ends to the guard
as it decodes, as [Damaged stream data](#damaged-stream-data) says.

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
Nor is the end of data whose end a guard hides — its `endstream` proven its own past white space or comments that only
a `/Length` a guard cut or left out of the index says are the data's or not — reported. Its first filter decodes it up
to where that run starts — a comment past white space within the data, but in ASCII85 data, where a `%` is a digit —:
what it meets before is the file's and reported, its running out there — a lost tail, a checksum short, a missing end
marker — is not, and where its output may lack what the run holds — Flate that lost its tail, LZW or RunLength data
without its end marker — the output's end is cut, as above, for the predictor, the filters after it and an object
stream's members. Flate data whose deflate body ends in the data as taken decodes whole, what it reports as read whole
but a checksum the run cuts short, or that disagrees only where it reaches into the run. The filters after a first whose output is whole
decode, and report, as read whole.

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
