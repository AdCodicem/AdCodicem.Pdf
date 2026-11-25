---
title: Reader limits
description: The bounds on what a file may make the reader hold or read — their defaults, codes and what is kept — and the bounds that cannot be lifted.
---

# Reader limits

The properties of `PdfReaderLimits`, the record `PdfReaderOptions.Limits` carries, in `AdCodicem.Pdf.Documents`.
Why the reader has them is explained in [Reader limits](../concepts/reader-limits.md); how to raise one, or refuse a
document instead, in [Read or refuse a document that reaches a limit](../guides/handle-reader-limits.md).

## The limits

| Property of `PdfReaderLimits` | Default | Reached by | What is kept | Code |
|---|---|---|---|---|
| `MaxDecodedStreamLength` | 256 MB | A decompression bomb, or a large-format scan: a 9,600 × 11,410 RGB map decodes to 313 MB. Of a chain of filters, the filters before the last share the bound for what they pass along together, and the last has its own; a `/Crypt` filter, which passes its data along as it is, is neither charged nor the last | The first bytes, up to the bound | `limit.decoded-stream` |
| `MaxStreamLength` | 256 MB | A stream read from a file whose encoded data is that long, read in one piece each time `GetRawBytes` or decoding asks for it: that map's image stored without a filter would be 313 MB. It bounds what is read, where `MaxDecodedStreamLength` bounds what filters produce: data decoding hands back as it is — no filter, an image filter, a filter the library does not know, one that could not decode it — is bounded by it alone | The data's first bytes, up to the bound; `Data.Length` and `RawLength` stay the length the file holds | `limit.stream-length` |
| `MaxObjectLength` | 16 MB | An object that long, the data of a stream aside, written in the file or held by an object stream: an array of a million references | The object as far as the bound, but for a token the bound parts, which is not kept ([Tokens a bound parts](#tokens-a-bound-parts)) | `limit.object` |
| `MaxXRefSectionLength` | 64 MB | A classic cross-reference table of more than about 3.3 million entries, measured from its `xref` keyword, the white space and comments an offset that names it falls short by counted toward `MaxTrailerLength` instead ([#295](https://github.com/AdCodicem/AdCodicem.Pdf/issues/295)) | The entries within the bound, if any | `limit.xref-section-length` |
| `MaxXRefSectionCount` | 1,024 | A document saved incrementally more than a thousand times | The newest sections | `limit.xref-section-count` |
| `MaxTrailerLength` | 64 KB | A trailer that long, or a cross-reference stream's dictionary — its `/Index` grows with every scattered update —, never that stream's data, read from the stream's object header; and, counted apart, as much white space and comments as an offset that names a cross-reference section falls short of it by | The trailer as far as the bound, the chain ending there — near the offset a hybrid table's `/Prev` names too, where a stream whose dictionary it cut keeps its place in the search, whatever `/Prev` the cut may hide ([#156](https://github.com/AdCodicem/AdCodicem.Pdf/issues/156)) —, but for the trailer of a classic table an `/XRefStm` names, whose links the chain never follows ([#263](https://github.com/AdCodicem/AdCodicem.Pdf/issues/263)), the chain going on through the table that names it; past white space and comments that run on past it, or where it cuts the object at an offset the chain names before the object says what it is, the section is the guard's, cut where it is named and not looked for near there, the chain ending there, the index rebuilt for the one `startxref` names, as [the property's reference](api/AdCodicem.Pdf.Documents.PdfReaderLimits.md#AdCodicem_Pdf_Documents_PdfReaderLimits_MaxTrailerLength) details ([#289](https://github.com/AdCodicem/AdCodicem.Pdf/issues/289)); no cross-reference stream passed over anywhere in the document is reported as one the chain did not reach (`xref.stream-passed-over`) wherever it cuts the reading at an offset the chain names and the reading does not take what it cut for a section — a dictionary whose `/Type`, written whole before the cut, names another type, or a token where no header starts —: the cut may have hidden a link the chain would follow, or what would end it there ([#263](https://github.com/AdCodicem/AdCodicem.Pdf/issues/263), [#264](https://github.com/AdCodicem/AdCodicem.Pdf/issues/264)). A dictionary whose `/Type` is no name, or a name the bound parts, cut before its `/W`, is a section it cut, the chain ending there ([#279](https://github.com/AdCodicem/AdCodicem.Pdf/issues/279)). A `stream` the bound ends on that a regular byte follows, `streamX`, is no stream keyword: the dictionary is followed by no stream data, as read whole ([#294](https://github.com/AdCodicem/AdCodicem.Pdf/issues/294)). The trailer keeps no token the bound parts ([Tokens a bound parts](#tokens-a-bound-parts)) | `limit.trailer` |
| `MaxIndexEntries` | 8,388,607 | A document of more objects than ISO 32000-1's Annex C advises; or a cross-reference stream of one-byte rows, of which 256 MB decode from a few hundred bytes | The object numbers indexed first: the newest sections', a rebuild's first; of the scan the searches near an offset ask for, the first object headers and keywords it takes | `limit.index-entries` |
| `MaxFilterChainLength` | 64 | A `/Filter` array naming more filters, `/Crypt` among them: real streams name one to three, and a hostile array of millions costs decoding a few microseconds a filter, and some filters a table of tens of kilobytes, whatever their data | The data as the filters within the bound decoded it; the elements past it are not read | `limit.filter-chain` |
| `MaxRebuildReadPasses` | 4 | A rebuild of the index whose scan reads past streams' direct lengths, to confirm them, more times the length of the file: a valid file's own streams take about twice the file, but the streams an attached file's bytes spell, inside the data of a stream whose `/Length` the scan does not confirm, count too, and their lengths may all end in one long run of white space that each read crosses ([#361](https://github.com/AdCodicem/AdCodicem.Pdf/issues/361)). The search for the objects of indirect lengths has bounds of its own ([What cannot be lifted](#what-cannot-be-lifted)) | The streams confirmed until then; past it, the data of each stream the scan cannot confirm is read up to its first `endstream`, the object headers there not taken | `limit.rebuild-reads` |
| `MaxObjectStreamDecodePasses` | 4 | One of the library's own walks of a document — the search for a catalog a `/Root` that leads to none sets off, as the document opens or as a rebuild finishes, and a validation's rules — that decodes again the object streams the reader let go more times what its first decodings of them weighed: two object streams that each decode past 16 MB, half the budget the reader keeps them in, whose objects the walk reads in turn — the pages of a valid page tree among them — decode a whole stream for each object ([#181](https://github.com/AdCodicem/AdCodicem.Pdf/issues/181)) | The objects of the streams the walk decoded within the bound, and those of any stream it has not decoded yet; until the walk ends, an object a stream it decoded and the reader let go holds reads as null, a guard's cut, and is read whole by any other read | `limit.object-stream-decodes` |
| `MaxNestingDepth` | 128 | An object that nests its arrays and dictionaries deeper: ISO 32000-1 nests them to any depth, and a chain of actions through `/Next` written in one object is as deep as it is long | The object up to the bound, the container past it read as null, the object one a guard cut | `limit.nesting-depth` |
| `MaxNestedLoads` | 64 | An object loaded while more others are being loaded: an object stream may take its `/Filter` or `/DecodeParms` from an object another object stream holds, to any depth | The load past the bound reads as null there, and the object reads whole from a shallower place, whatever was read before it; within one read past the bound, a load made again from where the read made it before, the cache having let go of what it gave, reads as null too, so that the read's cost stays in proportion to its objects and the bound | `limit.nested-loads` |

| Member | Value |
|---|---|
| `PdfReaderLimits.Default` | Every limit at the default above; what `PdfReaderOptions.Limits` holds unless you set it |
| `PdfReaderLimits.Unbounded` | Every length at `Array.MaxLength`, about 2 GB, and `MaxXRefSectionCount`, `MaxIndexEntries`, `MaxFilterChainLength`, `MaxRebuildReadPasses`, `MaxObjectStreamDecodePasses`, `MaxNestingDepth` and `MaxNestedLoads` at `int.MaxValue`: the thread's stack then bounds the nesting alone |

`MaxIndexEntries` bounds what a document costs after opening too. The index holds 40 bytes for each entry it is sized
for — an object, or one the newest `/Size` numbers —, up to about 80 when a rebuild grew it and about 96 for numbers
spread far apart, so 335 MB or more at the default bound. While the chain is read, the numbers whose rows it refused,
which the bound counts with the index, are held apart as well: about 60 bytes a number allocated as that set grows, let
go once the chain is read. Where the rows the chain read and did not take place objects is kept as well, 8 bytes each,
up to the same bound apart from the index. A rebuild keeps nothing of its scan of the file but the index. The scan of the file that the
searches near an entry's offset and near an offset the chain names ask for, once a document
([Lazy reading](../concepts/lazy-reading.md#the-cost-of-the-trade)), keeps, within the same bound, each object header
and keyword it takes, each definition of a number counted where the index counts the number once: under 25 bytes a
place, about 201 MB at most at the default, and from two to five times what it keeps allocated while it grows
([`MaxIndexEntries`](api/AdCodicem.Pdf.Documents.PdfReaderLimits.md#AdCodicem_Pdf_Documents_PdfReaderLimits_MaxIndexEntries)
says what each place costs). Validating a document costs about 3 µs and 350 bytes allocated an object, so a document
near the bound takes about 24 s and 3 GB to validate. An application that validates uploads can lower it to the
documents it accepts.

A value of zero or less is refused with an `ArgumentOutOfRangeException`. A length above `Array.MaxLength`
is taken as `Array.MaxLength`: a decoded stream, and a stream's data read at once, are held in one piece, and no
runtime allocates a larger one.
`PdfReaderOptions.Limits` refuses null with an `ArgumentNullException`.

## When a limit is reached

The reader keeps what fits and reports a warning under the limit's own code, whose message names the property to
raise. Once the diagnostics are full, the report is counted rather than kept, and `Diagnostics.Contains` and
`HasWarnings` still answer for it ([`PdfDiagnostics`](diagnostics.md#pdfdiagnostics)):

```text
Warning limit.decoded-stream at 48213: The /FlateDecode data decodes to more than 256 MB; decoding stopped
there. Raise PdfReaderLimits.MaxDecodedStreamLength to read past it.
```

- An object, a cross-reference section or a trailer is reported once, however often it is read again — an object an
  object stream holds, once for each object and each definition of its object stream —; a full index, once a document.
- What a limit leaves out of the index — the sections past `MaxXRefSectionCount`, the rows past
  `MaxXRefSectionLength`, or past what `MaxStreamLength` reads or `MaxDecodedStreamLength` decodes of a
  cross-reference stream, the object
  numbers past `MaxIndexEntries` — reads as null until you raise the limit. The reader does not rebuild the index for
  it, since the file may be sound, and the validator reports it as unknown, under `xref.checked-in-part`, rather than
  missing; nor is a cross-reference stream the search near a hybrid table's `/Prev` passed over reported as one the
  chain did not reach (`xref.stream-passed-over`): the link that names it may lie past the limit — white space and
  comments past `MaxTrailerLength` where the chain names a section, an object's header it cuts there, or an object it
  cuts there before its value says what it is, among them, which make the section the guard's
  ([#289](https://github.com/AdCodicem/AdCodicem.Pdf/issues/289)). None is reported anywhere in the document either
  where `MaxTrailerLength` may have hidden such a link, though what it cut is read as
  the file's: wherever it cuts the reading at an offset the chain names, whatever it cuts, that the reading does not take
  for a section it cut — a dictionary whose `/Type`, written whole before the cut, names another type, the object read
  as one of another type ([#279](https://github.com/AdCodicem/AdCodicem.Pdf/issues/279)), or a token where no header
  starts, no object read —: where a `/Prev` or `startxref` names it, any `/W` makes a stream of any `/Type` the section
  ([#263](https://github.com/AdCodicem/AdCodicem.Pdf/issues/263)), and the cut may have hidden one — a stream of another
  `/Type` that gives a `/W`, or a `/Type` given twice —, its `/Prev` not followed, wherever it lies; where an `/XRefStm` names it, the cut is what would end the chain there
  ([#264](https://github.com/AdCodicem/AdCodicem.Pdf/issues/264)). Which, the reader cannot tell without reading past
  the guard, and it withholds the report even where the cut hid nothing, a dictionary of another type that gives no
  `/W`, or a token no header starts, among them. When the trailer's
  `/Root` names an object so left out, the catalog reads as null too: the reader does not
  look for another among the objects it indexed, which would be another object than the one `/Root` names. When its
  `/Encrypt` names an object so left out, the document is taken for encrypted, as the file says
  ([#154](https://github.com/AdCodicem/AdCodicem.Pdf/issues/154)). A classic table cut by its length, or whose rows run past a full index, keeps its trailer: the document's
  catalog, encryption and identity. The chain ends at such a cut, and at a section whose trailer a limit cut — but for a
  classic table an `/XRefStm` names, whose links the chain never follows
  ([#263](https://github.com/AdCodicem/AdCodicem.Pdf/issues/263)), the chain going on through the table that names it —:
  a `/Prev`, an `/XRefStm` or a `/Root` past the cut is unknown, and no older section stands for it; one written whole
  before the cut is the trailer's, though a repeat of it past the cut would win in a whole read. What the section the
  chain ends at names is still judged, as far as the cut lets it be, and nothing past the cut is read: a `/Prev` or an
  `/XRefStm` — of a section whose rows a limit cut or filled the index at, or of a hybrid table whose stream a limit so
  stopped; a `/Prev` written whole before a limit cut a trailer
  ([#328](https://github.com/AdCodicem/AdCodicem.Pdf/issues/328)); the `/Prev` of a cross-reference stream at which a
  limit's cut of a value its rows are read with ends the chain, below — that can name no section whatever the cut hides
  is reported as a whole read reports it, under
  `xref.section-missing`, `xref.chain-cycle` or `xref.hybrid-stream-lost`, and what only the sections it should have named
  index is found by rebuilding the index when you ask for it
  ([#264](https://github.com/AdCodicem/AdCodicem.Pdf/issues/264)). Such a value is no offset — a reference to a free
  row the reader read among them, or to a number a newer section that lost rows refused, which no row past the cut can
  place —, an offset outside the file, a section the chain has reached, or, for an `/XRefStm`, what the section's
  `/Prev` names too, at that offset or through the white space before that section. A reference the reader follows
  through rows it read is judged as the value it leads to. One that may name a sound section — an offset in the file
  the chain has not reached, for an `/XRefStm` one the section's `/Prev` does not name too, a reference to a row past
  the cut, or to a number the section itself lost, which a row past the cut may place — is the limit's, and so are the trailers past it. A
  classic table `MaxXRefSectionLength` cuts before its first row is the limit's whatever the index holds, its `xref`
  keyword having declared it a section, and what its trailer names is judged so too; where the chain then gave no row,
  the index is rebuilt at opening, as for any chain a limit leaves no row. The `/XRefStm` that the cut trailer of a
  table read whole gives is followed, as the file wrote it, its stream holding the table's own revision's rows: the cut
  keeps none it parts, nor one that may start a reference past it
  ([Tokens a bound parts](#tokens-a-bound-parts)); one that names what the `/Prev` names is reported as naming the
  section past the limit that cut the trailer. Past rows a limit stopped, a cut trailer's `/XRefStm` is judged as the
  `/Prev` is, and not followed. A section a limit cut is where it was named, and is not looked for near there, as a
  section that is not there is.
- Past `MaxIndexEntries` places, the scan the searches near an offset ask for keeps no more, and answers only before
  the first it left out — or before the data of a stream whose length's object it then awaited, which that object could
  still show to end sooner. A search near an entry's offset that finds a header of the object past there, or a search
  near an offset the chain names that tries a place past there — among the 32 places it may try, below —, goes without
  it, and takes a header or a section it finds there as the bytes give it, which the validator reports as
  `xref.entry-unconfirmed` or `xref.section-unconfirmed`. It reports `limit.index-entries` once a document, where the
  first place left out starts — apart from the index's own report, which may come beside it at another position —, for
  a place short of where the end of a read that a source answered with fewer bytes than asked left what the scan keeps
  whole: past there, raising the bound would not let the scan answer. A rebuild never reports it: where that scan
  does not answer, the rebuild scans the file again, and serves and reports what it would have.
- Apart from the index, the reader keeps where the rows the chain reads and does not take — rows a newer section
  superseded, or that the chain refused — place objects, up to `MaxIndexEntries` of them: an older definition there
  proves the `endstream` of the stream before it its own, where a limit cut or left out the stream's `/Length`. Past
  the bound it keeps no more, reports `limit.index-entries` once, and an `endstream` only those rows would prove leaves
  the stream's data the limit's.
- A rebuild whose scan reaches `MaxRebuildReadPasses` reports it once, where the data starts of the first stream it then
  reads up to its first `endstream`, and finishes the index before `ThrowOnLimit` throws it. Such data of an attached
  file that holds an `endstream` of its own before the stream's has the object headers past that keyword taken, and a
  comment there may hide the objects after the stream: raising the limit reads it whole
  ([`MaxRebuildReadPasses`](api/AdCodicem.Pdf.Documents.PdfReaderLimits.md#AdCodicem_Pdf_Documents_PdfReaderLimits_MaxRebuildReadPasses)
  says what the scan counts). Where the data it so leaves unread holds an object header the scan would have taken, an
  object the rebuilt index lacks reads as null and is unknown rather than missing, whatever the file's own index says of
  it: the validator says the index was checked in part (`xref.checked-in-part`) from the first such stream, rather than
  report a reference to it as `object.reference-missing` or its entry as `xref.entry-broken`, and `ThrowOnLimit` throws
  at each read of one, where the guard was reported. An `obj` that no header precedes, a word in a comment or a string,
  holds none.
  The scan the searches near an offset ask for reaches it as the rebuild's does, and reports nothing: a rebuild after it
  scans the file again, and reports it then.
- An object cut at `MaxObjectLength` is kept as far as the bound, and the catalog among them: the reader takes the
  object `/Root` names for the catalog as it read it, rather than look for another or rebuild the index, until you raise
  the limit ([#175](https://github.com/AdCodicem/AdCodicem.Pdf/issues/175)). The validator judges nothing of what an
  object so cut lacks or holds, and takes a page tree node so cut to give what its pages inherit. An object header that
  runs past the bound, where a cross-reference entry places it, is where a read reaches the limit, and the validator says
  that entry was not checked. What the bound parts of the object's last token is not kept
  ([Tokens a bound parts](#tokens-a-bound-parts)).
- A stream whose data runs past `MaxStreamLength` is read up to the bound each time its data is asked for, by
  `GetRawBytes` or by decoding, and its filters decode what was read as data the reader cut: that it runs out is not
  reported as damage. Data that decoding hands back as it is comes back as far as the bound. The guard is reported at the
  offset the stream's data starts at: once in the document's own diagnostics, as the reader reads the data, and, as the
  bound on decoded data is, in the diagnostics you pass to `Decode` each time it decodes the stream. `GetRawBytes` takes
  no diagnostics: the document's hold its report. With `ThrowOnLimit` each read throws. In an object stream so cut, an
  object that lies past the cut is the guard's, unknown until you raise the limit, as what any limit leaves out is:

  ```text
  Warning limit.stream-length at 1904: A stream's data runs past 256 MB; only what lies within it was read. Raise
  PdfReaderLimits.MaxStreamLength to read past it.
  ```

- A stream decoded past the bound is reported by the first of its filters that stopped there: each time it is decoded,
  in the diagnostics you pass to `Decode`; once, in the document's own — when you pass none, or the document's —, however
  often you, the reader or the validator decode it. The guard is counted each time all the same, and with `ThrowOnLimit`
  each decoding throws. The filters after it decode
  what it kept as data the reader cut: that it runs out is not reported as damage.
- A stream of one or two filters decodes as each filter alone would. Of a longer chain, once the filters before the
  last have passed along the bound together, the next decodes nothing more, and the stream comes back as what the last
  decoded of what they kept, possibly nothing. The report says so:

  ```text
  Warning limit.decoded-stream at 1904: The filters before the last decode to more than 256 MB together; decoding
  stopped in the /RunLengthDecode data. Raise PdfReaderLimits.MaxDecodedStreamLength to read past it.
  ```

- A stream whose `/Filter` array names more filters than `MaxFilterChainLength` decodes through the first of them only,
  and comes back as they decoded it, as at a filter the library does not know; the elements past the bound are not
  read, so an image filter there is not seen, and `HasImageFilter` says false. It is reported as the bound on decoded
  data is — a filter within it the library does not know, an element within it that names none, or a Flate filter within
  it whose data is corrupt before its first byte decodes, ends decoding first, reported as `filter.unsupported` or
  `filter.failed` alone:

  ```text
  Warning limit.filter-chain at 1904: The /Filter entry names more than 64 filters; decoding stopped after the first
  64. Raise PdfReaderLimits.MaxFilterChainLength to read past it.
  ```

  What comes back is still encoded by the filters past the bound, and is no part of what the stream decodes to. A
  cross-reference stream or an object stream the reader meets so is read as one a guard cut before its data: its rows,
  or its objects, are unknown until you raise the limit, as what any limit leaves out is. But a cross-reference stream
  whose filters within the bound decode nothing of its data — a filter the library does not know or an element that
  names none, or Flate data corrupt before its first byte decodes, ending decoding there — cannot be read whatever the
  limit, the file's fault rather than the guard's ([#221](https://github.com/AdCodicem/AdCodicem.Pdf/issues/221)).

- A value a stream is read with that leads to an object a guard cut, or left out of the index — its `/Length`,
  `/Filter` or `/DecodeParms`, a value of a filter's parameters, directly or through a chain of references — is the
  guard's, and nothing is reported of it but the guard. The data of a stream whose `/Length` is so named is found by
  the search for its `endstream`, and its length is not judged: an integer the cut left whole, which starts no
  reference, is only a place to look for `endstream` at — one it parted, or that may start a reference, is not kept
  ([Tokens a bound parts](#tokens-a-bound-parts)), and gives none —, but where an `endstream` follows it and the index
  places an object inside the data it gives, which is weighed as a declared length is
  ([Streams whose length is wrong](diagnostics.md#streams-whose-length-is-wrong)). No `endstream` after the data of a stream whose
  `/Length` names an object a guard cut, or left out of the index, is still the file's fault, and reported; the report
  names the object at the end of the chain of references. The search may stop short of it, though, at an object header
  the data spells, as an embedded PDF file's does, or at an `endstream` the data spells, as a string may, an `endobj`
  after it or not: the length that tells those from the end of the data is the guard's. An `endstream` ends data that
  is the file's only where what follows its object's end — its `endobj`, or the `endstream` itself where none follows —
  proves it the stream's own: the next object the index places, a cross-reference stream the chain read, or the end of
  the file, which no data that goes on is followed by, or the `xref` or `startxref` keyword — which data may spell too: data that spells `endstream`, then
  `xref` or `startxref`, and goes on is taken to end there as the file's, which only the hidden length could tell; white
  space before the keyword, which only the length says is the data's or not, leaves the data's end to the guard as it
  decodes, so that a filter that runs out there says nothing of the file. Elsewhere the data up to there comes back cut by the guard,
  `CutByGuard`, whether the object ends with `endobj` is not judged, and nothing is said of the file. A reference the cut
  left whole, the cut falling after `6 0 R`, hides nothing: it is read through to the
  object it names, as it is read whole, while the chain is read as afterward. Decoding stops at a filter so named, or
  at the filter whose
  parameters are: the filters before it run, and the stream comes back as they decoded it, still encoded by the rest,
  as past `MaxFilterChainLength`. `HasImageFilter` reads what decoding reads before the image filter, the parameters of
  a Flate, LZW or `/Crypt` filter that comes before it included, and says false when such a value ends the chain there; under
  `ThrowOnLimit`, loading one throws where decoding would. An object stream the reader meets so
  is read as one a guard cut before its data: its objects are unknown until you raise the limit, and when the catalog
  is among them the reader does not look for another. While the chain is read, a `/Prev` or an `/XRefStm` the reader
  follows that leads to an object `MaxObjectLength` cuts — not a hybrid file's cross-reference stream's own `/Prev`,
  which it does not follow, nor the links of a table an `/XRefStm` names in a stream's place, which it leaves unread —,
  a cross-reference stream whose `/W`, `/Index`, `/Filter` or `/DecodeParms` does, or whose `/Size` does with no
  `/Index`, and one whose `/Length` does when no `endstream` ends its data, are the guard's, as a section it cut is: the
  chain ends there, the sections it names are not read — the older ones, or a hybrid file's cross-reference stream —,
  though a stream's own `/Prev`, its dictionary read whole, is judged as past a limit that stopped its rows, a
  reference to a row it declares and the limit left unread staying the limit's
  ([#329](https://github.com/AdCodicem/AdCodicem.Pdf/issues/329)), the index is not rebuilt for the guard, and the
  guard is reported once the chain is read, or thrown by `Open`: where it cuts the object's header, the load that reports
  it looks for nothing near the object's entry and rebuilds nothing, as where it cuts the value; a later read of that
  object, or a validation, reads it as any read reads a header past the guard, which may rebuild the index
  ([#292](https://github.com/AdCodicem/AdCodicem.Pdf/issues/292)). So does a section found near the offset it is named at, which
  such a value keeps from being read, or which declared itself one — a table's `xref` keyword, `/Type /XRef` — before a
  guard cut it, as the nearest such place a fault of the file keeps from being read is taken for the section too
  ([#281](https://github.com/AdCodicem/AdCodicem.Pdf/issues/281)); a place there that declares another type — a CIDFont, whose `/W` gives
  widths —, or whose dictionary a guard cut before it said what it is, is not taken for the section, and the next is
  tried — but for what a hybrid table's `/Prev` names, where the search near the offset its `/XRefStm` names comes to
  it: a guard that cut it there, before it said what it is or in a value its rows are read with, may hide a section the
  `/Prev` reads, and the search stops there, the `/XRefStm` the guard's, nothing reported of it and the index not
  rebuilt for it; an `/XRefStm` that names that offset itself, or the white space before it, names no stream of the
  table's revision whatever the guard hid, and is reported
  ([#156](https://github.com/AdCodicem/AdCodicem.Pdf/issues/156),
  [#264](https://github.com/AdCodicem/AdCodicem.Pdf/issues/264)). Near the offset a hybrid table's `/Prev` names, a
  cross-reference stream that may be the table's own, told by its own `/Prev`, is tried last, if at all, but not one
  whose dictionary a guard cut, which may hide that `/Prev`: it keeps its nearest-first place, the section found moved
  and the guard's where it declared itself one before the cut, the guard reported at its header, or thrown by `Open`
  ([#156](https://github.com/AdCodicem/AdCodicem.Pdf/issues/156)). A guard that cut, where the chain names a section,
  the dictionary of what declares another type cut no section — a CIDFont's, which read whole no stream data follows —,
  while one that cut a value of a stream there leaves it the section the guard cut, whatever type it gives, as read
  whole it is the section. What makes a section unreadable whatever a value a guard cut holds is the file's fault, as
  read whole: a value past the cut that no section read so far places, a `/W` that gives no three widths or none past 0,
  rows numbered wrongly — an `/Index` that holds no pairs, a subsection whose elements are no integers, whose first
  number or count is below 0, or that numbers past the object numbers, by both its elements or by the one read whatever
  the cut one holds, a `/Size` with no `/Index` that gives no count —, as far as the values read, written direct or
  through references, say. Data whose length the file gives that its filters, written direct, decode nothing of — a
  filter the library does not know, an element of `/Filter` that names none, a `/Filter` that is neither a name nor an
  array, Flate data corrupt before its first byte decodes — makes the section unreadable too, whatever `/W`, `/Index` or
  `/Size` the guard cut: it holds no row whatever the counts declare, and the fault is the file's, as read whole
  ([#221](https://github.com/AdCodicem/AdCodicem.Pdf/issues/221)). To tell whether they do, the data is read and
  decoded beside the cut, whatever value the guard cut: a guard that reading or decoding it meets — `MaxStreamLength`,
  `MaxFilterChainLength`, `MaxDecodedStreamLength` — may be reached there, reported before the guard that cut the
  value, which is reported once the chain is read, and thrown by `Open` in its place under `ThrowOnLimit`. The look at
  what a hybrid table's `/Prev` names, made for its `/XRefStm`, decodes no stream of `/Type /XRef` to tell, such a
  stream keeping its place whatever its data holds: that `/Prev`'s own reading decodes it. It decodes any other stream —
  of no type, or of another type that gives a `/W` and so reads there as a section —, beside a cut value or with every
  value read, and drops what that decoding reports but a guard it reaches, which
  is reported there, once, where the search comes to that place — after what reading a nearer place it tried and
  dropped reported, which stands, ahead of the `/XRefStm`'s own report and of what the search reports past that place,
  and not again by the `/Prev`'s own reading of the stream, nor by a later read of its data — and thrown by `Open`
  there under `ThrowOnLimit`. Data whose length
  the file gives, its filters written direct, decoded whole, that holds fewer rows than the counts read declare however
  few bytes the widths read leave a row lacks them whatever the cut hides: the section is read as read whole, lacking
  rows, its trailer merged and the index rebuilt, and the object the guard cut is loaded once the chain is read, which
  reaches the guard. The rows the data may hold at the fewest bytes a row may take, read with the cut value, are the
  guard's: the chain ends at the section, as where rows a guard cut, and the validator says it unchecked; the chain goes
  on only past data that holds none. The rows a cross-reference stream's data lacks, where its `/Length` so leads and
  its data was taken to the first `endstream`, are the guard's too, unless what follows that `endstream` proves it the
  stream's own, as above: they are then the file's to lack, and the index is rebuilt. An object the chain read through a
  reference the cut left whole is loaded once the chain is read only for a value it keeps, or for what the offset a
  section is named at holds, which is read as a load reads it, its reports kept: not for a place near there it guessed
  the section was moved to and dropped as none, nor for the `/Prev` of the stream a hybrid table's `/XRefStm` read,
  which the search near the offset that table's `/Prev` names reads to tell that stream's place, where it does not take
  the stream.
- A stream read from a document decodes under **that document's** limits, however long after opening you decode it.
  A stream you build in memory decodes under `PdfReaderLimits.Default`.
- A classic trailer that ends within the window its cross-reference table was read through is read whole, whatever
  its length. `MaxTrailerLength` bounds how far the reader follows one past that window, and a cross-reference stream's
  dictionary, read from its object's header, not its data: the data of one whose `/Length` the reader cannot read while
  it reads the chain is searched for its `endstream` in the file, up to the next object the sections read so far place,
  or the end of the file. Data no `endstream` ends before there makes its section malformed, a fault of the file and not
  of a guard — unless its `/Length` leads to an object `MaxObjectLength` cuts, which makes the section the guard's, as
  above. It bounds, apart, the white space and comments an offset that names a section falls short by, so that a
  dictionary is cut only where it alone runs past the bound, and a classic table's rows are bounded by
  `MaxXRefSectionLength` alone, from its `xref` keyword ([#295](https://github.com/AdCodicem/AdCodicem.Pdf/issues/295)):
  white space and comments that run on past it reach the guard, and so does a cut of the object there before it says
  what it is — within its header, or past it before its value says what it is —, all of which a valid file may hold.
  The section is then the guard's: cut where it is named and not looked for near there, where a search would read past
  the guard and may take another section, the chain ending there, and, for the one `startxref` names, which leaves the
  chain no section, the index rebuilt ([#289](https://github.com/AdCodicem/AdCodicem.Pdf/issues/289)). A value that
  starts before the cut as no dictionary, or a first token no header starts with — a `-`, a letter —, is no section's,
  whatever the cut hid: the section is looked for as one that is not there. No
  cross-reference stream passed over near a hybrid table's `/Prev` anywhere in the document is reported as one the
  chain did not reach wherever the guard cuts the reading at an offset the chain names and the reading does not take
  what it cut for a section: a dictionary whose `/Type`, written whole before the cut, names another type, which the
  parser keeps as far as it read it, the object read as one of another type
  ([#279](https://github.com/AdCodicem/AdCodicem.Pdf/issues/279)), or such a token, which leaves no object read. Where a
  `/Prev` or `startxref` names the offset, any `/W` makes a stream of any `/Type` the section
  ([#263](https://github.com/AdCodicem/AdCodicem.Pdf/issues/263)), and the cut may have hidden one — a stream of another
  `/Type` that gives a `/W`, or a `/Type` given twice whose later value is `/XRef` —, its `/Prev` not followed, wherever
  it lies, before the cut or past it; where an `/XRefStm` names it, whose stream's `/Prev` the chain never follows, the cut
  is what would end the chain there, as its cut of that stream does
  ([#264](https://github.com/AdCodicem/AdCodicem.Pdf/issues/264)). Which, the reader cannot tell without reading past
  the guard, and it withholds the report even where the cut hid nothing, a dictionary of another type that gives no
  `/W`, or a token no header starts, among them. A dictionary whose `/Type` is no name, which says nothing of what it
  is, or a name the cut parts, which the parser drops, declares a section only through a `/W`: cut before it, it is a
  section the guard cut, as one cut before its `/Type` is, the chain ending there and nothing reported missing.
- Until the reader decodes streams a piece at a time, a stream that decodes past about 2 GB is cut there, and
  reported, even under `Unbounded`. A stream's data is read in one piece, and data past about 2 GB is read up to
  there, and reported under `limit.stream-length`, even under `Unbounded`.
- The reader keeps the object streams it decoded within a budget of 32 MB — their decoded data, and their headers'
  tables —, and always keeps the one it decoded last, whatever its size. Raising `MaxDecodedStreamLength` raises how
  large that one can be. A stream it let go is decoded again when one of its objects is read again: your own reads are
  never cut for it, while the library's own walks — the search for a catalog, a validation's rules — decode again what
  they let go within `MaxObjectStreamDecodePasses`. Past it, every object a stream the walk decoded and let go holds
  reads as null until the walk ends, as one a guard cut: the validator judges nothing of what it lacks or holds, and
  the search for a catalog takes no such object — but where it finds no catalog among the objects it read, it
  reads those the guard cut again, stream by stream, each stream decoded once more at most, and takes the first of them,
  in the index's order, that reads as a catalog — read whole this time, or cut again, as the first search takes one —,
  before it would rebuild the index for want of one; where this reading finds none, the index is rebuilt as it would have
  been without it. It does not read them again within another walk, nor once a load rebuilt the index on the way, whose
  own search for a catalog takes over. What so reads as null is not kept. An object whose load read through it is kept
  until the walk ends only, and an object stream whose decoding did is not kept at all, nor decoded again before the walk
  ends, so that your own reads, once the walk is done, read the objects whole; and the budget starts again with the next
  walk, so that which objects a walk leaves unread depends on what the reader kept before it began
  ([`MaxObjectStreamDecodePasses`](api/AdCodicem.Pdf.Documents.PdfReaderLimits.md#AdCodicem_Pdf_Documents_PdfReaderLimits_MaxObjectStreamDecodePasses)
  says what a walk counts). It is reported once a document, where the object stream the walk first left undecoded lies:

  ```text
  Warning limit.object-stream-decodes at 18191: The validation decoded again the object streams the reader let go as
  much as 4 times what its first decodings of them weighed, 35652544 bytes: object stream 11 was not decoded again,
  and the objects it holds read as null until the validation ends. Raise PdfReaderLimits.MaxObjectStreamDecodePasses
  to read past it.
  ```

  With `ThrowOnLimit`, each read the walk so leaves undecoded throws: a validation stops there, and the search for a
  catalog goes on, reads again what the guard cut where it found no catalog, and throws the first once it ends, the
  index rebuilt where it found none even so. The walk reads an object whose read threw so once, however many of its
  reads lead to it.

### Tokens a bound parts

Where `MaxObjectLength` or `MaxTrailerLength` ends what is read short of the end of the file, or of an object stream's
decoded data, and where an object stream member runs to the end of data `MaxDecodedStreamLength` or `MaxStreamLength`
cut, you can rely on this
([`MaxObjectLength`](api/AdCodicem.Pdf.Documents.PdfReaderLimits.md#AdCodicem_Pdf_Documents_PdfReaderLimits_MaxObjectLength)
says which tokens are kept, and how the byte past the bound decides):

- No value you read holds a token the bound parts, nor an integer the bound reaches or that may start a reference past
  it, whole as it may be: such a token is left out of its array, leaves its key out of its dictionary, and reads as
  null alone ([#262](https://github.com/AdCodicem/AdCodicem.Pdf/issues/262),
  [#279](https://github.com/AdCodicem/AdCodicem.Pdf/issues/279)).
- Any other token reads as written — but at the end of data a guard cut, where no byte says a name, a real or a keyword
  ends, one that reaches it is taken as parted —: an object stream member that ends at `MaxObjectLength` reads as under
  a larger bound, not cut and nothing reported, unless it ends with an integer, or with a dictionary a `stream` keyword
  may follow past the bound ([#282](https://github.com/AdCodicem/AdCodicem.Pdf/issues/282)); and a keyword longer than
  `stream` is never read as one ([#294](https://github.com/AdCodicem/AdCodicem.Pdf/issues/294)).
- A string, an array or a dictionary the bound leaves open is kept as far as it was read; what holds it is one the
  guard cut, reported under its code.

## `ThrowOnLimit`

With `PdfReaderOptions.ThrowOnLimit` set — it is false by default —, reaching a limit throws a
`PdfLimitExceededException` instead of warning:

| Member of `PdfLimitExceededException` | What it is |
|---|---|
| `Code` | The limit's code, such as `limit.decoded-stream` |
| `LimitName` | The property of `PdfReaderLimits` that lifts it, such as `MaxDecodedStreamLength` |
| `Limit` | The bound in force, such as `268435456` |
| `Position` | The byte offset where the limit was reached, or -1; a chain cut where it goes on outside the file gives the section whose `/Prev` names where |

The exception comes from whichever operation reaches the limit:

| Operation | When |
|---|---|
| `PdfDocument.Open` | While it indexes the file and reads its catalog, the search for one included: once that search ends, at the first limit it reached — `MaxObjectStreamDecodePasses` among them —, the index rebuilt first where it found none |
| `GetObject`, or resolving a reference | When an object is too long, or the object stream that holds it runs past `MaxStreamLength`, decodes too far or names too many filters; when the read rebuilds the index, at any limit the rebuild reaches, once the rebuild is finished — the first reached: a limit the rebuild's scan reaches before the read's own, the read's own before one finishing the rebuild reaches; and each time it looks up an object a full index left out; and when the search near its entry's offset reaches past the places the scan it asks for keeps |
| `GetRawBytes` | When the stream's data runs past `MaxStreamLength`, each time it is read |
| `Decode` | When a stream's data runs past `MaxStreamLength`, the stream decodes too far, its decoding reaches the filter past `MaxFilterChainLength`, or loading a value the filters read — the `/Filter` entry, a filter's parameters — reaches a limit, as resolving a reference does |
| `HasImageFilter` | When loading a value decoding reads before the image filter — the `/Filter` entry, a filter's parameters — reaches a limit, as resolving a reference does |
| `PdfValidator.Validate` | As it reads, like any other caller, and when its rules decode again past `MaxObjectStreamDecodePasses` the object streams the reader let go; and before its first rule, at any limit the rebuild it may run then reaches, once that rebuild is finished |

A document whose index had to be rebuilt finishes the rebuild before throwing, so it stays usable afterwards. An object
whose load threw — or whose load another load threw through —, or an object stream whose decoding did, throws the same
exception again each time it is read from as deep, without reading the file again, until a rebuild of the index lets go
of what was read: a search for a catalog whose candidates all lead to one object a limit cut reads it once
([#181](https://github.com/AdCodicem/AdCodicem.Pdf/issues/181), [#397](https://github.com/AdCodicem/AdCodicem.Pdf/issues/397)).
One `MaxNestedLoads` cut throws again only from as many loads in flight as it was read from, and one the thread's stack cut
not past the read in progress. The document keeps as many such exceptions for loads, and as many for decodings, as it
caches objects (`ObjectCacheCapacity`), the oldest let go first: an object or a stream whose exception was let go is read
again up to the limit, and throws a new one. None is kept for a load, or a decoding, that rebuilt the index on its way —
read again, the object is read as the rebuilt index defines it —, nor for a load that met, on its way, an object read as
null for the time being — one being loaded already —, which may read whole once what was in flight is done. An object
stream's decoding keeps its exception through such a null, as it keeps the objects it gave, every later
decoding reading an object the stream holds, or one being loaded, as null again; but not through an object the walks'
guard read as null, nor — though the objects it gives are kept then — while a rebuild takes the object streams in, where
an object the index holds no entry for yet reads as null for the time being. The walks' guard's is not kept past the
walk: within it, the walk reads an object whose read threw so once, and a read of it once the walk is done reads it
whole. None is kept for a lookup of an object a guard left out of the index, which reads nothing and throws a new
exception each time.

## What cannot be lifted

Some bounds protect the reader itself — the chains of references it follows, the reads of its searches — and are not reached by
the documents producers write; a document producers write reaches the search for the object of a stream's indirect
`/Length` only through a rebuild a reported guard sent it to, and past its bounds nothing is read worse than before the
search existed.
They are not options:

| Bound | Value | Why it is not an option |
|---|---|---|
| A reference that resolves to another reference | followed 32 times | Producers write the object itself; the bound breaks a chain that loops |
| Searches for a stream's `endstream` | none starts once a document's searches together have read four times the file's length | A valid stream's `endstream` follows its declared length: the file is read past the bytes an object is parsed through only for the data of a cross-reference stream whose `/Length` the chain cannot read, for the data of a stream whose `/Length` a guard cut or left out of the index, and for white space and comments after a declared length that run past a first read — about twice the file, three times over the white space a cross-reference stream's search takes as data that its length, once read, is read on through —, and the proof that an `endstream` found under a hidden `/Length` is the stream's own reads 48 or 64 bytes past each, once, and the white space it crosses once more at most, about three times the file in all. Past the bound, a stream whose `endstream` the bytes read with its object do not show is not searched for further, and its report says so — or, where a guard hid its `/Length`, its data up to the next object is the guard's, `CutByGuard`, nothing reported —; either counts toward the next row's bound, past which it is ended short or reads empty, and is reported. The scan that rebuilds the index counts its own reads apart, against `MaxRebuildReadPasses` and the two bounds below |
| Lengths taken without reading where a stream's data ends | counted once the lengths so taken — each confirmed by an `endstream`, taken without a search, or found by a search made for another object whose data starts at the same place — were parsed four times the file's length over, each object and place its data starts at once, at the longest length taken for it; the data kept whole while they total at most four times the file's length; past it, the data ends at the first of the places the index, as the file wrote it or as the reader rebuilt or corrected it, gives inside it, of the first 64, at which an object header starts, else at the first, as long as what the streams so ended keep fits four times the file's length more, and reads empty otherwise | ISO 32000-1 has each object begin after the one before ends: a valid file's streams lie apart, and parsed once each they total less than the file: it is spared the count until its streams were parsed four times over, the cache having let them go, and, counted once each, none of them is ever ended. A row a damaged file left stale in a stream's data, or a copy of an object the file superseded, ends nothing: streams whose data share no byte total at most the file. Streams that share a stretch of the file — each holding the next one's header, every length confirmed by one `endstream` far on or taken without a search, or one body that many headers name, each number but the one searched for taking what the search found — would read that stretch again for each read of each one's data, from a source not in memory, the file in its square; counted, the data they serve stays within eight times the file once the count begins, and four times it before, besides what the searches read for the objects they were made for — four times the file, and the stretch of the search under way when they reach it, which reads on to its stop: about five times the file at most —, whatever the order they are read in and whatever the index places, but which of them are ended, and where, depends on that order, each reported as `stream.length-invalid` ([Streams whose length is wrong](diagnostics.md#streams-whose-length-is-wrong), [#280](https://github.com/AdCodicem/AdCodicem.Pdf/issues/280)) |
| Reads of the scan that rebuilds the index that tell only how it took a place in a stream's data | none starts once they have read four times the file's length | The searches near a cross-reference entry's offset and near an offset the chain names read how the scan took each place — in a stream's data the scan could not confirm, or between objects — to rank what they find; they run only where a row or an offset is off, and no valid file's is. What the scan takes, and so what a rebuild serves, does not hang on those reads, which have a budget of their own apart from `MaxRebuildReadPasses`, and which a rebuild's own scan does not make ([#361](https://github.com/AdCodicem/AdCodicem.Pdf/issues/361)). Past the bound, the places in such data are held as taken there |
| The search of the scan that rebuilds the index for the object of a stream's indirect `/Length` | the last eight objects other than streams before the stream, within its revision; past the data's first `endstream`, short of the first revision's end past it, the first object header, or, in an attached file's data, the first header of the length's number whose object holds an integer, within 1 MB of that `endstream`; a non-negative integer and `endobj` within 64 bytes of the header; each read repeating 32 bytes of the one before; none starts once the searches have read four times the file's length | A writer puts a stream's length object a few objects before the stream or right after it — after an attached file, past the file's own objects, found where it lies within 1 MB of the data's first `endstream`, an uncompressed attached PDF's own first stream's —; one in an object stream has no header in the file, and the search stops at the first header past the data. A revision ends at its end-of-file marker, `%%EOF` — a linearized file's first-page marker aside —, past which a later revision's objects lie; an attached file's own marker, right before its stream's `endstream`, ends none, but one no `endstream` follows within 64 bytes — the attached file updated incrementally, linearized with a first-page cross-reference stream, with bytes or long padding after its last marker, or holding a marker in its text — ends the search, and the stream's data is read as before the search. A valid file reaches the search only through a rebuild that a reported guard sent it to, and raising that guard reads it through its index. Past any of these bounds, a stream whose length the search did not confirm has its data read as the bytes give it, up to its first `endstream`, as before the search existed, and nothing is reported ([#363](https://github.com/AdCodicem/AdCodicem.Pdf/issues/363), [#391](https://github.com/AdCodicem/AdCodicem.Pdf/issues/391)) |
| Places tried near a cross-reference section that is not where the chain names it | 32 a document, within 512 bytes either side of the offset, each counted once | A valid file's sections are where its chain names them, and a file that misses one misses by a few bytes, the section the nearest place; each place is read as a section is, through a window of 64 KB first, and a hostile file may write a header every few bytes near each offset its chain names. Only the places the scan that rebuilds the index takes are tried and counted, as far as what that scan keeps reaches ([Lazy reading](../concepts/lazy-reading.md#the-cost-of-the-trade)): the word `xref` or a header inside another object, or in a comment or a string between objects — but for one that scan takes in a stream's data past where a load ends it —, is neither read nor counted there ([#171](https://github.com/AdCodicem/AdCodicem.Pdf/issues/171)). A cross-reference stream read and passed over as possibly a hybrid table's own, tried again once every other place was, is not counted again, and is tried even once the 32 are spent, so that places are tried 64 times a document at most; the stream the table's `/XRefStm` read, put back without being read, is counted where it is read, and the bound may stop it. Once the 32 are spent, no new place is tried — but where the place a hybrid table's `/Prev` names is the nearest not yet read, the search near its `/XRefStm` still asks whether that `/Prev` keeps its place there, read as that `/Prev` reads it, once for each hybrid table, and leaves the naming to that `/Prev` where it does, as with places to spare —: a section not found by then is reported missing, and what only it indexes is found by rebuilding the index when it is asked for |
| Object numbers | up to 2,147,483,647 | An object's number is an `int`, and ISO 32000-1's Annex C advises at most 8,388,607 indirect objects. A number past it is refused where it is read — a header, a reference, a subsection, a row, an object stream's header —, never narrowed to one that would name another object. An object is numbered from 1; 0 names the head of the free list, where a reference, a subsection or a row may give it |
| An object stream's members | each read to the next one's start, the last to the end of the decoded data | ISO 32000-1 (7.5.7) has a header list its objects by increasing offset, so that each lies before the next one starts: a pair whose offset is not past the one before it, or whose object would start past the data, ends the header there, as a stray token does, and the objects listed before it are read. A member past `MaxObjectLength` is that limit's |
| An object stream's header | at most a quarter of its `/First` bytes, and one, of pairs | No limit but a count: each pair takes four bytes at least, so a header holds no more, whatever its `/N` claims. The header is read as far as it goes, and what it lists grows as it is read |
| Generations | 0 to 65,535 | No object in use has a higher one (ISO 32000-1, 7.5.4). A free row of a table is read whatever its generation: producers give the free list's head 65,536 |
| Reals | within ±1.8 × 10³⁰⁸ | A real is a `double`, and ISO 32000-1's Annex C advises reals within ±3.403 × 10³⁸. A number past it reads as null, reported as `syntax.number-out-of-range` |
