---
title: Diagnostics
sidebar_position: 2
description: Why the reader repairs what it can and returns an account of what it did, and how it reasons about damaged streams.
---

# Diagnostics

Real PDFs are frequently not conforming. A reader that refuses them fails where every reader on the
market succeeds; a reader that silently patches them leaves you guessing. This library does the third
thing: it repairs what it can and **returns an account of what it did**.

The report is a returned value, not a log: it can be inspected, serialized, asserted on in your own tests,
and used to decide whether to accept a file into your system. [Find out what the reader
repaired](../guides/find-what-the-reader-repaired.md) shows how; [Diagnostics](../reference/diagnostics.md), in the
reference, lists every severity and code.

## What the reader did, not what is wrong

Diagnostics say what the reader did to read a file. To know what is wrong with the file itself — whether or
not the reader could work around it —, [validate it](validation.md): findings are a separate verdict, with
their own severities and rule identifiers that never reuse a diagnostic code. A `Repair` says what the reader did,
not how wrong the file is.

## Where a stream ends

The reader parses an object through a window of the file — 8 KB to start with — and reads it again
through a larger one when it runs past the edge. What the smaller window saw there, such as a string
without its end or a stream without its `endstream`, is dropped with that attempt, so the report says what
the object holds, not where a window happened to end.

A stream's declared length is checked against the `endstream` that must follow it. When the data runs past
the window, the reader asks the file for the few bytes after the declared length rather than reading the
data, and they tell it whether `endobj` follows too. When no `endstream` is there, the first one after the
start of the data ends it, and where it is looked for depends on where the declared length ends:

- **Inside the window, or nowhere** — a `/Length` that gives no length —, the first `endstream` after the
  data is taken wherever it lies, past the header of the next object too: the reader looks in the window,
  and in a larger one while none is found, as far as `MaxObjectLength` allows.
- **Past the window**, the reader searches the file from the start of the data up to the next object —
  the one the file's index places as the file wrote it (as the reader rebuilt it when the document opened, if
  the file wrote none), or the first object header, `N G obj`, the file's bytes hold, whichever comes first —,
  or up to the end of the file.
  Stopping at the next object keeps a later object's `endstream` from ending this stream, which is what a
  stretch of zeros that erased the end of one and the objects after it would otherwise do, or an object whose
  entry the index lost; when none lies before it, the declared length is kept, and the report says so. Text
  in a damaged stream's data that reads as an object header stops the search as well, and the declared
  length is kept: an object number and a generation the parser takes, however many zeros lead them, white
  space between the three tokens, however much, and white space, a delimiter, or the start or the end of
  the search around them. A comment between the tokens makes no header here, as it makes none for the scan
  that rebuilds a damaged index; nor does a regular character glued before the number, though that scan
  takes the digits after it for one.

The search is not one of the reader's limits: a valid file's `endstream` follows its length, so only a
damaged file is searched, and once for each stream. Where it stops depends on the file's bytes and on its
index as the file wrote it, which the reader rebuilding or correcting its own index later does not change:
in the 401 documents of the project's test corpus measured, each stream takes the same length whatever was
read before it — though a damaged index can still serve another copy of an object, at another offset,
according to what was read first. A file crafted for it can still make what a stream holds depend on which
object was asked for first. What the searches of one document read together is bounded: none starts once
they have read four times the file's length. They read twice the file at most — 0.35 of it at most in the
test corpus — unless their stretches overlap: one object's header inside another's dictionary, or a header
the search does not take — a regular character glued before its number — that the index as the file wrote
it does not place. Past the bound, a stream keeps its declared length without a search, and the report says
so.

## Damaged stream data

Damaged stream data decodes as far as it goes, and what decoded is kept, with a report. A Flate stream whose
tail was lost, as a file cut short or a producer that stopped writing leaves it, is a warning: what decoded
before the end is all there is. A Flate stream that lost only the zlib checksum after its last block decoded
whole, unchecked, and is a repair, as a stream with no zlib header at all is. A Flate stream that turns
corrupt is a warning too: decoding stops at the fault, and what decoded before the byte the fault lies in is
kept; what that one byte decoded ahead of the fault can be lost with it, though no stream in the project's test
corpus lost any. The report says at which byte of the encoded data the fault was found and how many bytes were
kept, and does not vouch for them: the damage may lie before the point where decoding found it, since damaged
data can go on decoding, wrongly, for a while. A stream in which nothing decodes before that byte is left
encoded, and reported as one that could not be decoded. Finding what decoded before a fault reads the stream's
data again, the last few kilobytes before the fault a byte at a time; a sound stream is read once.

A Flate stream that decodes to its end, but whose zlib checksum disagrees with what it decoded to, is a
warning of its own, `filter.checksum-mismatch`: all of its data is kept, as other readers keep it, and some of
it may be wrong — the checksum does not say where. In the project's test corpus, none of the 86 such streams
could be shown intact, and 39 were shown damaged, 28 of them by a change of line endings that, undone, makes
the checksum agree.

An LZW stream that uses a code it has not defined stops there, since what follows cannot be read reliably. An LZW
stream without its end-of-data code is taken as complete, as other readers take it.

Decoding is lazy like the rest of reading, so a stream's damage is reported when the stream is decoded, not when the
document opens — and a stream you built in memory and decode without a `PdfDiagnostics` of your own has nowhere to
report, and reports nothing. A document's stream never decodes in silence.

## The reader's limits are not faults

Growing windows, and decoding, are bounded. The `limit.*` codes report the reader's own limits, not
faults of the file: a valid document can reach them — a large-format scan decodes past the 256 MB a stream
may decode to by default. What fits within the limit is kept, and the limit replaces what the parser met where it
stopped. [Reader limits](reader-limits.md) explains why they exist and why they are on by default.

## Exceptions, by contrast

Exceptions are reserved for what makes the operation impossible: the input is not a PDF, or it is encrypted and
cannot be opened. Anything the reader can work around is a diagnostic, never an exception — unless you ask for one:
with `PdfReaderOptions.ThrowOnLimit`, reaching a reader limit throws from whichever operation reached it, for an
application that would rather refuse a document than read part of it. The
[reference](../reference/diagnostics.md#exceptions) lists them.
