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
| `MaxObjectLength` | 16 MB | An object that long, the data of a stream aside: an array of a million references | The object as far as the bound | `limit.object` |
| `MaxXRefSectionLength` | 64 MB | A classic cross-reference table of more than about 3.3 million entries | The entries within the bound | `limit.xref-section-length` |
| `MaxXRefSectionCount` | 1,024 | A document saved incrementally more than a thousand times | The newest sections | `limit.xref-section-count` |
| `MaxTrailerLength` | 64 KB | A trailer that long, or a cross-reference stream's dictionary: its `/Index` grows with every scattered update | The trailer as far as the bound | `limit.trailer` |
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
is taken as `Array.MaxLength`: a decoded stream is held in one piece, and no runtime allocates a larger one.
`PdfReaderOptions.Limits` refuses null with an `ArgumentNullException`.

## When a limit is reached

The reader keeps what fits and reports a warning under the limit's own code, whose message names the property to
raise:

```text
Warning limit.decoded-stream at 48213: The /FlateDecode data decodes to more than 256 MB; decoding stopped
there. Raise PdfReaderLimits.MaxDecodedStreamLength to read past it.
```

- An object, a cross-reference section or a trailer is reported once, however often it is read again; a full index,
  once a document.
- What a limit leaves out of the index — the sections past `MaxXRefSectionCount`, the rows past
  `MaxXRefSectionLength` or past what `MaxDecodedStreamLength` decodes of a cross-reference stream, the object
  numbers past `MaxIndexEntries` — reads as null until you raise the limit. The reader does not rebuild the index for
  it, since the file may be sound, and the validator reports it as unknown, under `xref.checked-in-part`, rather than
  missing. A classic table cut by its length, or whose rows run past a full index, keeps its trailer: the document's
  catalog, encryption and identity. The chain ends at such a cut, and at a section whose trailer a limit cut: a `/Prev`,
  an `/XRefStm` or a `/Root` past the cut is unknown, and no older section stands for it. A section a limit cut is
  where it was named, and is not looked for near there, as a section that is not there is.
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

- A stream read from a document decodes under **that document's** limits, however long after opening you decode it.
  A stream you build in memory decodes under `PdfReaderLimits.Default`.
- A classic trailer that ends within the window its cross-reference table was read through is read whole, whatever
  its length. `MaxTrailerLength` bounds how far the reader follows one past that window.
- Until the reader decodes streams a piece at a time, a stream that decodes past about 2 GB is cut there, and
  reported, even under `Unbounded`.
- The reader keeps the object streams it decoded within a budget of 32 MB, and always keeps the one it decoded last,
  whatever its size. Raising `MaxDecodedStreamLength` raises how large that one can be.

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
| `GetObject`, or resolving a reference | When an object is too long, or the object stream that holds it decodes too far or names too many filters; when the read rebuilds the index, at any limit the rebuild reaches, once the rebuild is finished; and each time it looks up an object a full index left out |
| `Decode` | When a stream decodes too far, or its decoding reaches the filter past `MaxFilterChainLength` |
| `PdfValidator.Validate` | As it reads, like any other caller |

A document whose index had to be rebuilt finishes the rebuild before throwing, so it stays usable afterwards.

## What cannot be lifted

Some bounds protect the reader itself — its stack, the chains of references it follows — and are not reached by
the documents producers write. They are not options:

| Bound | Value | Why it is not an option |
|---|---|---|
| Nesting of arrays and dictionaries | 128 levels | Real documents nest a handful of levels; the bound keeps a hostile file from exhausting the stack |
| A reference that resolves to another reference | followed 32 times | Producers write the object itself; the bound breaks a chain that loops |
| Objects loaded while loading another | 64 | An indirect `/Length` leads to an integer, and a stream resolves its filters and the parameters they read before decoding any of its data: no level holds decoded data while the next loads |
| Object numbers | up to 2,147,483,647 | An object's number is an `int`, and ISO 32000-1's Annex C advises at most 8,388,607 indirect objects. A number past it is refused where it is read — a header, a reference, a subsection, a row, an object stream's header —, never narrowed to one that would name another object. An object is numbered from 1; 0 names the head of the free list, where a reference, a subsection or a row may give it |
| Generations | 0 to 65,535 | No object in use has a higher one (ISO 32000-1, 7.5.4). A free row of a table is read whatever its generation: producers give the free list's head 65,536 |
| Reals | within ±1.8 × 10³⁰⁸ | A real is a `double`, and ISO 32000-1's Annex C advises reals within ±3.403 × 10³⁸. A number past it reads as null, reported as `syntax.number-out-of-range` |
