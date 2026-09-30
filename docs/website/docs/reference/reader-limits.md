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
| `MaxDecodedStreamLength` | 256 MB | A decompression bomb, or a large-format scan: a 9,600 × 11,410 RGB map decodes to 313 MB | The first bytes, up to the bound | `limit.decoded-stream` |
| `MaxObjectLength` | 16 MB | An object that long, the data of a stream aside: an array of a million references | The object as far as the bound | `limit.object` |
| `MaxXRefSectionLength` | 64 MB | A classic cross-reference table of more than about 3.3 million entries | The entries within the bound | `limit.xref-section-length` |
| `MaxXRefSectionCount` | 1,024 | A document saved incrementally more than a thousand times | The newest sections | `limit.xref-section-count` |
| `MaxTrailerLength` | 64 KB | A trailer that long, or a cross-reference stream's dictionary: its `/Index` grows with every scattered update | The trailer as far as the bound | `limit.trailer` |

| Member | Value |
|---|---|
| `PdfReaderLimits.Default` | Every limit at the default above; what `PdfReaderOptions.Limits` holds unless you set it |
| `PdfReaderLimits.Unbounded` | Every length at `Array.MaxLength`, about 2 GB, and `MaxXRefSectionCount` at `int.MaxValue` |

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

- An object, a cross-reference section or a trailer is reported once, however often it is read again.
- A stream is reported each time it is decoded past the bound — in the diagnostics you pass to `Decode`, or in the
  document's own when you pass none.
- A stream read from a document decodes under **that document's** limits, however long after opening you decode it.
  A stream you build in memory decodes under `PdfReaderLimits.Default`.
- A classic trailer that ends within the window its cross-reference table was read through is read whole, whatever
  its length. `MaxTrailerLength` bounds how far the reader follows one past that window.
- Until the reader decodes streams a piece at a time, a stream that decodes past about 2 GB is cut there, and
  reported, even under `Unbounded`.
- Raising `MaxDecodedStreamLength` also raises what the reader may hold for object streams it keeps decoded, which
  are not yet bounded as a whole.

## `ThrowOnLimit`

With `PdfReaderOptions.ThrowOnLimit` set — it is false by default —, reaching a limit throws a
`PdfLimitExceededException` instead of warning:

| Member of `PdfLimitExceededException` | What it is |
|---|---|
| `Code` | The limit's code, such as `limit.decoded-stream` |
| `LimitName` | The property of `PdfReaderLimits` that lifts it, such as `MaxDecodedStreamLength` |
| `Limit` | The bound in force, such as `268435456` |
| `Position` | The byte offset where the limit was reached, or -1 |

The exception comes from whichever operation reaches the limit:

| Operation | When |
|---|---|
| `PdfDocument.Open` | While it indexes the file |
| `GetObject`, or resolving a reference | When an object is too long |
| `Decode` | When a stream decodes too far |
| `PdfValidator.Validate` | As it reads, like any other caller |

A document whose index had to be rebuilt finishes the rebuild before throwing, so it stays usable afterwards.

## What cannot be lifted

Some bounds protect the reader itself — its stack, its rebuild of a damaged index — and are not reached by
the documents producers write. They are not options:

| Bound | Value | Why it is not an option |
|---|---|---|
| Nesting of arrays and dictionaries | 128 levels | Real documents nest a handful of levels; the bound keeps a hostile file from exhausting the stack |
| A reference that resolves to another reference | followed 32 times | Producers write the object itself; the bound breaks a chain that loops |
| Objects loaded while loading another | 64 | An indirect `/Length` leads to an integer, which loads nothing further |
| Objects indexed by a rebuild | 2,000,000 | A rebuild only runs on a damaged file |
| Entries one subsection claims | 50,000,000 | A table's rows are bounded by `MaxXRefSectionLength` already; a count past that describes rows that are not there |
