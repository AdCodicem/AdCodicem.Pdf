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
| `PdfInteger` | A whole number within a `long` | `Value` | Its digits: `-17` |
| `PdfReal` | A real number, as the `double` nearest to what the file wrote | `Value` | The shortest decimal that reads back to `Value`, with a decimal point: `0.0000001`, `4.0` |
| `PdfName` | A name, such as `/Type` | `Value`, without its solidus | The name as a writer writes it: `/Pa#0Age` |
| `PdfString` | A string's bytes, text or binary | `Bytes`; `ToText()` for its text | Its text between double quotes, escaped: `"Annual report"` |
| `PdfArray` | An ordered list of objects | Its items, by index | `[array of 3]` |
| `PdfDictionary` | Entries, each keyed by a name | Its entries, by key | `<</Type /Page ... 5 entries>>`, or `<<dictionary of 5>>` when its `/Type` is no name |
| `PdfStream` | A dictionary and its encoded data | `Dictionary`, `GetRawBytes()`; `Decode()` for the decoded data | `<</Image stream of 4096 bytes>>`, by its `/Subtype`, else its `/Type` |
| `PdfReference` | A reference to an object, such as `12 0 R` | `Id`; `Resolve()` for the object | `12 0 R` |

## What `ToString()` gives

`ToString()` describes an object for a debugger, a log line or a test's failure message: on one line, in printable
ASCII, whatever the file wrote, and in a few hundred characters at most, however long what it describes. A name
holding a line feed or an escape sequence reaches no terminal or log as one. It is not the value: read `Value`,
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
| Any other character outside U+0020 to U+007E — an escape, `é`, U+202E, half of a surrogate pair | `\u` and four hexadecimal digits: `\u001B`, `é`, `‮`, `\uD83D` |

Past 127 characters the text is cut, and the description says how many the whole holds: a string of 4,194,304 `A` is
described as a double quote, its first 127 `A`, a double quote, and ` (the first 127 of 4,194,304 characters)`. Only
what is shown is decoded.

### Reals

A real is written as PDF writes one: the shortest decimal that reads back to the same `double`, never with an exponent,
always with a decimal point, so that it reads as a real and not as an integer. Nothing is rounded.

| `Value` | `ToString()` |
|---|---|
| 4 | `4.0` |
| 1E-07 | `0.0000001` |
| 1E+20 | `100000000000000000000.0` |
| 2 / 3 | `0.6666666666666666` |
| 0.1 + 0.2, computed | `0.30000000000000004` |
| `NaN`, `Infinity`, which only a caller can build | `NaN`, `Infinity`, `-Infinity` |

The longest is 327 characters, for the smallest double there is.

## Objects from a disposed document

An object keeps what it holds once its document is disposed: a dictionary's direct values, an array's items, a stream's
`RawLength`, every object's `ToString()`. What reaches back into the document throws `ObjectDisposedException`, however
the document was opened and whether or not it was read before: `Resolve()` on a reference, and every accessor that
resolves one, such as `GetDictionary`; `GetRawBytes()` and `Decode()` on a stream read from the file.
