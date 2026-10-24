---
title: Reader limits
description: The bounds on what a file may make the reader hold — their defaults, codes and what is kept — and the bounds that cannot be lifted.
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
| `MaxObjectLength` | 16 MB | An object that long, the data of a stream aside, written in the file or held by an object stream: an array of a million references | The object as far as the bound | `limit.object` |
| `MaxXRefSectionLength` | 64 MB | A classic cross-reference table of more than about 3.3 million entries | The entries within the bound | `limit.xref-section-length` |
| `MaxXRefSectionCount` | 1,024 | A document saved incrementally more than a thousand times | The newest sections | `limit.xref-section-count` |
| `MaxTrailerLength` | 64 KB | A trailer that long, or a cross-reference stream's dictionary — its `/Index` grows with every scattered update —, never that stream's data | The trailer as far as the bound | `limit.trailer` |
| `MaxIndexEntries` | 8,388,607 | A document of more objects than ISO 32000-1's Annex C advises; or a cross-reference stream of one-byte rows, of which 256 MB decode from a few hundred bytes | The object numbers indexed first: the newest sections', a rebuild's first | `limit.index-entries` |
| `MaxFilterChainLength` | 64 | A `/Filter` array naming more filters, `/Crypt` among them: real streams name one to three, and a hostile array of millions costs decoding a few microseconds a filter, and some filters a table of tens of kilobytes, whatever their data | The data as the filters within the bound decoded it; the elements past it are not read | `limit.filter-chain` |

| Member | Value |
|---|---|
| `PdfReaderLimits.Default` | Every limit at the default above; what `PdfReaderOptions.Limits` holds unless you set it |
| `PdfReaderLimits.Unbounded` | Every length at `Array.MaxLength`, about 2 GB, and `MaxXRefSectionCount`, `MaxIndexEntries` and `MaxFilterChainLength` at `int.MaxValue` |

`MaxIndexEntries` bounds what a document costs after opening too. The index holds 52 to 63 bytes an object, up to 108
when a rebuild grew it, so 436 MB or more at the default bound; and validating a document costs about 3 µs and 350
bytes allocated an object, so a document near the bound takes about 24 s and 3 GB to validate. An application that
validates uploads can lower it to the documents it accepts.

A value of zero or less is refused with an `ArgumentOutOfRangeException`. A length above `Array.MaxLength`
is taken as `Array.MaxLength`: a decoded stream, and a stream's data read at once, are held in one piece, and no
runtime allocates a larger one.
`PdfReaderOptions.Limits` refuses null with an `ArgumentNullException`.

## When a limit is reached

The reader keeps what fits and reports a warning under the limit's own code, whose message names the property to
raise:

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
  missing. When the trailer's `/Root` names an object so left out, the catalog reads as null too: the reader does not
  look for another among the objects it indexed, which would be another object than the one `/Root` names. A classic table cut by its length, or whose rows run past a full index, keeps its trailer: the document's
  catalog, encryption and identity. The chain ends at such a cut, and at a section whose trailer a limit cut: a `/Prev`,
  an `/XRefStm` or a `/Root` past the cut is unknown, and no older section stands for it. A section a limit cut is
  where it was named, and is not looked for near there, as a section that is not there is.
- An object cut at `MaxObjectLength` is kept as far as the bound, and the catalog among them: the reader takes the
  object `/Root` names for the catalog as it read it, rather than look for another or rebuild the index, until you raise
  the limit ([#175](https://github.com/AdCodicem/AdCodicem.Pdf/issues/175)). The validator judges nothing of what an
  object so cut lacks or holds, and takes a page tree node so cut to give what its pages inherit. An object header that
  runs past the bound, where a cross-reference entry places it, is where a read reaches the limit, and the validator says
  that entry was not checked.
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
  data is — a Flate filter within it that decodes nothing ends decoding first, reported as `filter.failed` alone:

  ```text
  Warning limit.filter-chain at 1904: The /Filter entry names more than 64 filters; decoding stopped after the first
  64. Raise PdfReaderLimits.MaxFilterChainLength to read past it.
  ```

  What comes back is still encoded by the filters past the bound, and is no part of what the stream decodes to. A
  cross-reference stream or an object stream the reader meets so is read as one a guard cut before its data: its rows,
  or its objects, are unknown until you raise the limit, as what any limit leaves out is.

- A value a stream is read with that leads to an object a guard cut, or left out of the index — its `/Length`,
  `/Filter` or `/DecodeParms`, a value of a filter's parameters, directly or through a chain of references — is the
  guard's, and nothing is reported of it but the guard. The data of a stream whose `/Length` is so named is found by
  the search for its `endstream`, and its length is not judged: an integer the cut left, which may be the first digits
  of a longer one, is only a place to look for `endstream` at. No `endstream` after the data of a stream whose
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
  a Flate or LZW filter that comes before it included, and says false when such a value ends the chain there; under
  `ThrowOnLimit`, loading one throws where decoding would. An object stream the reader meets so
  is read as one a guard cut before its data: its objects are unknown until you raise the limit, and when the catalog
  is among them the reader does not look for another. While the chain is read, a `/Prev` or an `/XRefStm` the reader
  follows that leads to an object `MaxObjectLength` cuts — not a hybrid file's cross-reference stream's own `/Prev`,
  nor the links of a table an `/XRefStm` names in a stream's place, which it leaves unread —, a cross-reference stream
  whose `/W`, `/Index`, `/Filter` or `/DecodeParms` does, or whose `/Size` does with no `/Index`, and one whose
  `/Length` does when no `endstream` ends its data, are the guard's, as a section it cut is: the chain ends there, the
  sections it names are not read — the older ones, or a hybrid file's cross-reference stream —, the index is not
  rebuilt, and the guard is reported once the chain is read, or thrown by `Open`. So does a section found near the
  offset it is named at, which such a value keeps from being read, or which declared itself one — a table's `xref`
  keyword, `/Type /XRef` — before a guard cut it; a place there that declares another type — a CIDFont, whose `/W` gives
  widths —, or whose dictionary a guard cut before it said what it is, is not taken for the section, and the next is
  tried. A guard that cut, where the chain names a section, what declares another type cut no section; and a section
  that numbers its rows wrongly in what it writes direct — neither `/Size` nor `/Index` — is the file's fault, whatever a
  value a guard cut hides. The rows a cross-reference stream's data lacks,
  where its `/Length` so leads and its data was taken to the first `endstream`, are the guard's too, unless what follows
  that `endstream` proves it the stream's own, as above: they are then the file's to lack, and the index is rebuilt. An
  object the chain read through a reference the cut left whole is loaded once the chain is read only for a value it
  keeps, or for what the offset a section is named at holds, which is read as a load reads it, its reports kept: not
  for a place near there it guessed the section was moved to and dropped as none.
- A stream read from a document decodes under **that document's** limits, however long after opening you decode it.
  A stream you build in memory decodes under `PdfReaderLimits.Default`.
- A classic trailer that ends within the window its cross-reference table was read through is read whole, whatever
  its length. `MaxTrailerLength` bounds how far the reader follows one past that window, and a cross-reference stream's
  dictionary, not its data: the data of one whose `/Length` the reader cannot read while it reads the chain is searched
  for its `endstream` in the file, up to the next object the sections read so far place, or the end of the file. Data
  no `endstream` ends before there makes its section malformed, a fault of the file and not of a guard — unless its
  `/Length` leads to an object `MaxObjectLength` cuts, which makes the section the guard's, as above.
- Until the reader decodes streams a piece at a time, a stream that decodes past about 2 GB is cut there, and
  reported, even under `Unbounded`. A stream's data is read in one piece, and data past about 2 GB is read up to
  there, and reported under `limit.stream-length`, even under `Unbounded`.
- The reader keeps the object streams it decoded within a budget of 32 MB — their decoded data, and their headers'
  tables —, and always keeps the one it decoded last, whatever its size. Raising `MaxDecodedStreamLength` raises how
  large that one can be.

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
| `PdfDocument.Open` | While it indexes the file and reads its catalog |
| `GetObject`, or resolving a reference | When an object is too long, or the object stream that holds it runs past `MaxStreamLength`, decodes too far or names too many filters; when the read rebuilds the index, at any limit the rebuild reaches, once the rebuild is finished — the first reached: a limit the rebuild's scan reaches before the read's own, the read's own before one finishing the rebuild reaches; and each time it looks up an object a full index left out |
| `GetRawBytes` | When the stream's data runs past `MaxStreamLength`, each time it is read |
| `Decode` | When a stream's data runs past `MaxStreamLength`, the stream decodes too far, its decoding reaches the filter past `MaxFilterChainLength`, or loading a value the filters read — the `/Filter` entry, a filter's parameters — reaches a limit, as resolving a reference does |
| `HasImageFilter` | When loading a value decoding reads before the image filter — the `/Filter` entry, a filter's parameters — reaches a limit, as resolving a reference does |
| `PdfValidator.Validate` | As it reads, like any other caller; and before its first rule, at any limit the rebuild it may run then reaches, once that rebuild is finished |

A document whose index had to be rebuilt finishes the rebuild before throwing, so it stays usable afterwards.

## What cannot be lifted

Some bounds protect the reader itself — its stack, the chains of references it follows — and are not reached by
the documents producers write. They are not options:

| Bound | Value | Why it is not an option |
|---|---|---|
| Nesting of arrays and dictionaries | 128 levels | Real documents nest a handful of levels; the bound keeps a hostile file from exhausting the stack |
| A reference that resolves to another reference | followed 32 times | Producers write the object itself; the bound breaks a chain that loops |
| Searches for a stream's `endstream` | four times the file's length, for a document's searches together | A valid stream's `endstream` follows its declared length: the file is read past the bytes an object is parsed through only for the data of a cross-reference stream whose `/Length` the chain cannot read, for the data of a stream whose `/Length` a guard cut or left out of the index, and for white space and comments after a declared length that run past a first read — about twice the file at most —, and the proof that an `endstream` found under a hidden `/Length` is the stream's own reads 48 or 64 bytes past each, once, about the file once more. Past the bound, a stream whose `endstream` the bytes read with its object do not show is not searched for further, and its report says so — or, where a guard hid its `/Length`, its data up to the next object is the guard's, `CutByGuard`, nothing reported |
| Objects loaded while loading another | 64 | An indirect `/Length` leads to an integer, and a stream resolves its filters and the parameters they read before decoding any of its data: no level holds decoded data while the next loads |
| Object numbers | up to 2,147,483,647 | An object's number is an `int`, and ISO 32000-1's Annex C advises at most 8,388,607 indirect objects. A number past it is refused where it is read — a header, a reference, a subsection, a row, an object stream's header —, never narrowed to one that would name another object. An object is numbered from 1; 0 names the head of the free list, where a reference, a subsection or a row may give it |
| An object stream's members | each read to the next one's start, the last to the end of the decoded data | ISO 32000-1 (7.5.7) has a header list its objects by increasing offset, so that each lies before the next one starts: a pair whose offset is not past the one before it, or whose object would start past the data, ends the header there, as a stray token does, and the objects listed before it are read. A member past `MaxObjectLength` is that limit's |
| An object stream's header | at most a quarter of its `/First` bytes, and one, of pairs | No limit but a count: each pair takes four bytes at least, so a header holds no more, whatever its `/N` claims. The header is read as far as it goes, and what it lists grows as it is read |
| Generations | 0 to 65,535 | No object in use has a higher one (ISO 32000-1, 7.5.4). A free row of a table is read whatever its generation: producers give the free list's head 65,536 |
| Reals | within ±1.8 × 10³⁰⁸ | A real is a `double`, and ISO 32000-1's Annex C advises reals within ±3.403 × 10³⁸. A number past it reads as null, reported as `syntax.number-out-of-range` |
