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
without its end, is dropped with that attempt, so the report says what the object holds, not where a window
happened to end. When `MaxObjectLength` allows no larger window, the object is kept as far as it was read and the
guard is reported: what the parse met before the edge — a stray token, a key that is not a name — is reported beside
it, and nothing of the token the edge cut, nor of what that token leaves open, nor of the next object's header the edge
cut short inside an array or a dictionary — `6 0` or `6 0 ob`, whose number and generation would otherwise read as keys
that are not names, or, for `6 0 ob`, which no reference can be, the number as a value —, is. A stream's data is no part of what a window must hold: where it
ends is found in the file, whatever window its dictionary was read through, and it never counts toward
`MaxObjectLength`.

A stream's declared length is confirmed by the `endstream` that must follow it, after any white space and comments,
however many — a comment only once white space comes before it: ISO 32000-1 (7.2.3) counts a comment as white space
outside a stream's data, and a `%` right where the declared length ends is the data's, the length too short. Past the
window, the reader asks the file for the bytes after the declared length rather than reading the data, reading on as
far as white space and comments run, and they tell it whether `endobj` follows too. Any other
stream — its length unconfirmed, or a `/Length` that gives none — is searched once for the first `endstream` after the
start of its data, which ends it: in the bytes the parser holds, then in the file, up to the next object — the one the
file's index places as the file wrote it (as the reader rebuilt it when the document opened, if the file wrote none),
or the first object header, `N G obj`, the file's bytes hold, whichever comes first —, or up to the end of the file.

Stopping at the next object keeps a later object's `endstream` from ending this stream, which is what a stretch of
zeros that erased the end of one and the objects after it would otherwise do, or an object whose entry the index lost.
Text in a damaged stream's data that reads as an object header stops the search as well: an object number and a
generation the parser takes, however many zeros lead them, white space between the three tokens, however much, and
white space, a delimiter, or the start or the end of the search around them. A comment between the tokens makes no
header here, as it makes none for the scan that rebuilds a damaged index; nor does a regular character glued before
the number, though that scan takes the digits after it for one. Neither `xref` nor `trailer` stops a search: the
stream of a classic file's last object, which no object follows, is searched through the table and the trailer to the
end of the file.

With no `endstream` before the stop, the object's `endobj` plays its part. A declared length that ends before the stop,
and that the object's `endobj` follows as an `endstream` would, is kept: the stream lost only its `endstream`. Otherwise,
the object's own `endobj`, when only white space and comments separate it from the stop, ends the data, the end-of-line
before it left out. Both are reported as `stream.truncated`. Without one, a declared length that ends before the stop is
kept, and whether the object ends with its `endobj` is not judged; and the data otherwise runs to the stop, a declared
length past it cut there, the object judged to lack its `endobj` unless what is taken holds one. Both are reported as
`stream.length-invalid`, but data that runs to the end of the file as `stream.truncated`.

A `/Length` that names an object one of the reader's limits cut is no length the file gave: what the cut left of it —
null, or `1` of a `15` it parted — is the guard's. An integer read before the cut is only a place to look for
`endstream` at, and the data is otherwise found by its `endstream`, as for a stream with no length; nothing is said of
the length, while a missing `endstream` is still the file's, and reported. One that names an object a guard left out of
the index is not judged either: its data is found the same way, and a missing `endstream` reported as the file's. Such a
stream may be valid, its length only hidden, and its data may spell an object header, as an embedded PDF file's does —
and the `endstream` of a stream the embedded file holds, which no search can tell from the stream's own. A header the
data spells that stops the search, with no `endobj` of the object's own before it, leaves the data up to it to the
guard, and so does an `endstream` the data spells — inside a string, say, an `endobj` after it or not —: the data is
marked cut by the guard, whose report the document holds, whether the object ends with `endobj` is not judged, and
nothing is said of the file. Only what follows the object's end — its `endobj`, or the `endstream` where none follows —
can prove an `endstream` the stream's own: the next object the index places or the end of the file, which data that
goes on is never followed by, or the `xref` or `startxref` keyword, which the data may spell as well — data that spells
`endstream`, then one of them, and goes on is taken to end there, which only the hidden length could tell. The end is
then the file's, and so is an `endobj` the object lacks. A whole reference the cut left, `6 0 R` before the window's end, is no length
it hid: it leads on to the object it names, as read whole.

A cross-reference stream whose `/Length` the chain cannot read yet, which a valid file may hold, is searched as well, up
to the next object the sections read so far place, or the end of the file, and its length read once the chain is. Its
rows may spell an object header and end in a carriage return: no header stops its search, and a carriage return before
the line feed that precedes `endstream` is kept. Data with no `endstream` before that stop ends nowhere the section
says, and its rows are not read — the file's fault, unless the `/Length` leads to an object one of the reader's limits
cut: the length that would have said where the data ends is then the guard's, and so is the section, the chain ending
there. Where such a `/Length` leaves the data at the first `endstream`, the rows it lacks are the guard's too, unless
what follows that `endstream` proves it the stream's own, as above: they are then the file's to lack.

The search is not one of the reader's limits: a valid file's `endstream` follows its length, so only a damaged stream is
searched — and a cross-reference stream whose `/Length` the chain cannot read yet, and a stream whose `/Length` a guard
hid —, each once. Where it stops depends
on the file's bytes and on its index as the file wrote it, which the reader rebuilding or correcting its own index later
does not change: in the 401 documents of the project's test corpus measured, each stream takes the same length whatever
was read before it — though a damaged index can still serve another copy of an object, at another offset, according to
what was read first. A file crafted for it can still make what a stream holds depend on which object was asked for
first. What the searches of one document read together is bounded: none starts once they have read four times the file's
length. For a valid file, what they read — its cross-reference streams searched, its streams whose length a guard hid,
the white space after a declared length read on — comes to about twice the file at most, and the proofs that an `endstream`
found under a hidden length is the stream's own, 48 or 64 bytes past each, once, to about the file once more; the searches of damaged
streams read twice the file at most — 0.35
of it at most, all searches together, in the test corpus — unless their stretches overlap: one object's header inside
another's dictionary, or a header the search does not take — a regular character glued before its number — that the
index as the file wrote it does not place. Past the bound, only the bytes the parser holds are searched: a declared
length that ends before the next object is kept, the data otherwise taken up to it or to the end of the file, and the
report says so — but for a stream whose length a guard hid, whose data up to there is the guard's, as an `endstream`
nothing proves is.

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

An LZW stream that uses a code it has not defined stops there, since what follows cannot be read reliably.

ASCII85, ASCIIHex and RunLength data that breaks its format is decoded as other readers decode it, and reported. A byte
that encodes nothing where it stands — outside the alphabet, or a `z` inside an ASCII85 group — is passed over and the
rest decoded: a warning, since what the rest decodes to may not be what was encoded, where qpdf and MuPDF stop at that
byte. An ASCII85 group whose value does not fit in four bytes decodes to its four low-order bytes, as every reader
measured decodes it, and is a warning too; a `~` that something other than `>` follows ends the data, what follows lost.
The `<~` PostScript writes before ASCII85 data is passed over, and the data read as meant: a repair, where qpdf and MuPDF
read nothing.

Data whose end marker is missing — ASCII85's `~>`, ASCIIHex's `>`, RunLength's end-of-data byte, LZW's end-of-data
code — is decoded to its end, as every reader measured decodes it, and reported, since the format asks for the marker.
Data that ends after a whole group, pair or run lost nothing anyone can tell: a repair. Data that ends inside one was
likely cut short: the unit is completed as a last one is, or kept as far as it goes, or, for a single ASCII85 digit,
which encodes no byte, and for an LZW code the data holds only part of, dropped — a warning. An LZW stream is padded to
the byte with zero bits after its last code: a whole byte past it, or a bit that is set, is part of a code. What follows an end marker is no part of the data, and is not reported. Of
data the reader cut, the end is the reader's, and no marker is missing from it.

Decoding is lazy like the rest of reading, so a stream's damage is reported when the stream is decoded, not when the
document opens, and once in the document's diagnostics, however often it is decoded: the reader decodes an object stream
again once it let it go, and the validator decodes it again. Diagnostics you pass to `Decode` of your own hold each
decoding's account — and a stream you built in memory and decode without a `PdfDiagnostics` of your own has nowhere to
report, and reports nothing. A document's stream never decodes in silence.

## The reader's limits are not faults

Growing windows, reading a stream's data, and decoding are bounded. The `limit.*` codes report the reader's own limits,
not faults of the file: a valid document can reach them — a large-format scan decodes past the 256 MB a stream
may decode to by default. What fits within the limit is kept, and the limit replaces what the parser met where it
stopped. [Reader limits](reader-limits.md) explains why they exist and why they are on by default.

## Exceptions, by contrast

Exceptions are reserved for what makes the operation impossible: the input is not a PDF, or it is encrypted and
cannot be opened. Anything the reader can work around is a diagnostic, never an exception — unless you ask for one:
with `PdfReaderOptions.ThrowOnLimit`, reaching a reader limit throws from whichever operation reached it, for an
application that would rather refuse a document than read part of it. The
[reference](../reference/diagnostics.md#exceptions) lists them.
