---
title: Object model
description: The PDF objects a document hands out, the value each holds, and what each says of itself through ToString.
---

# Object model

The types of `AdCodicem.Pdf.Objects` that a document hands out, through `GetObject`, `Trailer`, `Catalog` and the
references they hold. Every one derives from `PdfObject`. How a document reads them only when asked is explained in
[Lazy reading](../concepts/lazy-reading.md); their members are in the [API reference](api/AdCodicem.Pdf.Objects.md).

## The types

| Type | What it holds | Its value | `ToString()` |
|---|---|---|---|
| `PdfNull` | The null object, and what a reference to an object the file lacks resolves to | — | `null` |
| `PdfBoolean` | `true` or `false` | `Value` | `true`, `false` |
| `PdfInteger` | A whole number within a `long`; one a file writes past it is read as a `PdfReal` | `Value` | Its digits: `-17` |
| `PdfReal` | A real number, as the `double` nearest to what the file wrote | `Value` | The shortest decimal that reads back to `Value`, with a decimal point: `0.0000001`, `4.0` |
| `PdfName` | A name, such as `/Type` | `Value`, without its solidus | The name as a writer writes it: `/Pa#0Age` |
| `PdfString` | A string's bytes, text or binary | `Bytes`; `ToText()` for its text | Its text between double quotes, escaped: `"Annual report"` |
| `PdfArray` | An ordered list of objects | Its items, by index | `[array of 3]` |
| `PdfDictionary` | Entries, each keyed by a name | Its entries, by key | `<</Type /Page ... 5 entries>>`, or `<<dictionary of 5>>` when its `/Type` is no name |
| `PdfStream` | A dictionary and its encoded data, which a stream read from a document [reads each time it is asked for](#a-streams-data) | `Dictionary`, `GetRawBytes()`; `Decode()` for the decoded data | `<</Image stream of 4096 bytes>>`, by its `/Subtype`, else its `/Type` |
| `PdfReference` | A reference to an object, such as `12 0 R` | `Id`; `Resolve()` for the object | `12 0 R` |

## What `ToString()` gives

`ToString()` describes an object for a debugger, a log line or a test's failure message: on one line, in printable
ASCII, whatever the file wrote, and bounded however long what it describes — fewer than 500 characters for a name, a
dictionary or a stream, about 810 at most for a string, 327 for a real. A name holding a line feed or an escape
sequence reaches no terminal or log as one. It is not the value: read `Value`,
`Bytes`, `ToText()` or `GetRawBytes()` for that. Numbers are written in the invariant culture, whatever
`CultureInfo.CurrentCulture` is.

### Names, dictionaries and streams

A name is quoted as the library's messages quote one, under the rules of
[How a message quotes the file](diagnostics.md#how-a-message-quotes-the-file): every byte a writer does not write as
itself — white space, a control character, the number sign, a delimiter — as `#` and two hexadecimal digits, and, past
127 bytes, a cut that says how long the whole is.

| The name holds | `ToString()` |
|---|---|
| `Type` | `/Type` |
| `Esc`, an escape, `[31mred`, a line feed, `line` | `/Esc#1B#5B31mred#0Aline` |
| `A B#` | `/A#20B#23` |
| `café`, read from a file as Latin-1 | `/caf#E9` |
| 4,194,304 times `A` | `/` and its first 127 `A`, then ` (the first 127 of 4,194,304 bytes)` |

A dictionary is described by its `/Type`, quoted the same way, and its count of entries: `1 entry`, `2 entries`. A
stream is described by its `/Subtype`, or else its `/Type`, and the count of its encoded bytes: `1 byte`, `4096 bytes`.
Neither describes its entries, and a stream's data is not read.

### Strings

A string's text, as `ToText()` reads it — UTF-16 after a byte order mark, Latin-1 otherwise —, between double quotes,
every character outside printable ASCII escaped as C# writes it in a literal:

| Character | Written |
|---|---|
| A backslash, a double quote | `\\`, `\"` |
| Null, tab, line feed, carriage return | `\0`, `\t`, `\n`, `\r` |
| Any other character outside U+0020 to U+007E — an escape, `é`, U+202E, half of a surrogate pair | `\u` and four hexadecimal digits: `\u001B`, `\u00E9`, `\u202E`, `\uD83D` |

Past 127 characters the text is cut, and the description says how many the whole holds: a string of 4,194,304 `A` is
described as a double quote, its first 127 `A`, a double quote, and ` (the first 127 of 4,194,304 characters)`. Only
what is shown is decoded.

### Reals

A real is written as PDF writes one: the shortest decimal that reads back to the same `double`, never with an exponent,
always with a decimal point, so that it reads as a real and not as an integer. Nothing is rounded. An integer a file
writes past what a `long` holds is read as a real, and so described with its point: `92233720368547760000.0`.

| `Value` | `ToString()` |
|---|---|
| 4 | `4.0` |
| 1E-07 | `0.0000001` |
| 1E+20 | `100000000000000000000.0` |
| 2 / 3 | `0.6666666666666666` |
| 0.1 + 0.2, computed | `0.30000000000000004` |
| `NaN`, `Infinity`, which only a caller can build | `NaN`, `Infinity`, `-Infinity` |

The longest description is 327 characters, that of -4.9E-324, the negative of the smallest positive double.

## A stream's data

A `PdfStream` holds its dictionary and a `PdfStreamData`. For a stream read from a document, that says where the
encoded bytes lie and how many there are, not what they are: they are read each time something asks for them, and kept
by nothing — neither the stream nor the document's object cache —, so that a stream costs its data only while it is
read or decoded, and for as long as the caller keeps what that gave.

| Member | What it gives | What it costs |
|---|---|---|
| `Dictionary` | The stream's dictionary | Nothing: it was read with the stream |
| `RawLength`, `Data.Length` | The count of the stream's encoded bytes in the file: its `/Length` where an `endstream` confirms it, otherwise as far as the reader takes the data to run ([Where a stream ends](../concepts/diagnostics.md#where-a-stream-ends)). Data longer than `MaxStreamLength` is counted whole | Nothing: found when the stream is parsed |
| `Data.Position` | The offset in the file the data starts at | Nothing |
| `GetRawBytes()`, `Data.GetBytes()` | The encoded bytes, as the file holds them, up to `MaxStreamLength`: past it, the bytes within the bound, and `limit.stream-length` reported once for the stream, in the document's diagnostics | A read, each call: from a document opened from bytes or from a stream, the bytes lent where they lie, without a copy — a view of that memory, which keeps it alive for as long as you hold it, and changes as it does; from one opened from a path, read from the file into an array of their length |
| `Decode()` | The data the stream's filters decode, as far as they go: decoding stops at an image filter, at a filter the library does not know, and where the [reader limits](reader-limits.md) end it — a `/Filter`, a `/DecodeParms` or a parameter that leads to an object a limit cut or left out of the index among them. Data cut at `MaxStreamLength` is decoded as far as it was read, and `limit.stream-length` reported in the diagnostics you pass, each time | A read, as `GetRawBytes()` makes, then what each filter produces; data no filter decodes comes back as it was read |
| `HasImageFilter()` | Whether `Decode()` hands back an encoded image: whether `/Filter` names an image filter before anything that ends decoding whatever the data holds — a filter the library does not know, an element that names no filter, a filter past `MaxFilterChainLength`, a value or parameter of the filters' that leads to an object a limit cut or left out of the index | None of the data: what decoding reads before the image filter — the filters' names, and the parameters of a Flate or LZW step before it —, which can load an object, and decode the object stream that holds it, as decoding would |

Opened with `ThrowOnLimit`, a document's streams throw a `PdfLimitExceededException` from `GetRawBytes()` and
`Decode()` instead, each time their data runs past `MaxStreamLength`; `Decode()` throws at the other limits it reaches
too, and `HasImageFilter()` when loading a value the filters read reaches one. No call's result is kept for the next: a caller that uses the data twice keeps what one call gave, rather than call
again ([Tune the reader's memory](../guides/tune-reader-memory.md#read-a-streams-data-once)).

Data built with `PdfStreamData.FromMemory` is held by the stream, whatever its length, and handed back as it was given;
its `Position` is -1.

## Objects from a disposed document

An object keeps what it holds once its document is disposed: a dictionary's direct values, an array's items, a stream's
`RawLength`, every object's `ToString()`. What reaches back into the document throws `ObjectDisposedException`, however
the document was opened and whether or not it was read before: `Resolve()` on a reference, and every accessor that
resolves one, such as `GetDictionary`; `GetRawBytes()` and `Decode()` on a stream read from the file, and
`HasImageFilter()` when it has a value to load. Raw bytes taken before from a document held in memory stay a view of
that memory: they keep it alive for as long as they are held.
