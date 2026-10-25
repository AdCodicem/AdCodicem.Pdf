---
title: Reader limits
sidebar_position: 3
description: How the reader keeps every valid PDF readable while treating every file as hostile, and why its guards are on by default.
---

# Reader limits

Every PDF that is valid under the specification can be read. That promise has to live alongside another:
a file arriving from outside your system is treated as hostile, and a few kilobytes of it must not be able
to make the reader hold gigabytes. The two meet in the **reader limits**: bounds on what a file may make the
reader hold, on by default, each of which you can raise. The [reference](../reference/reader-limits.md) lists them;
[Read or refuse a document that reaches a limit](../guides/handle-reader-limits.md) says what to do when one is
reached.

## Hostile input

A PDF arrives from outside your system, so the reader treats every value in it as an attempt. No
allocation is sized by a number read from the file without a checked bound, no recursion is unbounded,
and no loop exits on an offset that came from the file.

That claim is tested rather than asserted. Besides the hand-written cases — a cross-reference chain that
loops, a stream claiming two gigabytes, an object stream declaring a billion objects, containers nested
twenty thousand deep, fifty thousand streams each taking its length from the next, a few megabytes of
RunLength data that would decode to hundreds, predictor rows whose length overflows — a mutation campaign
runs against the test corpus: bit flips, corrupted digits, truncations, spliced bytes and broken keywords,
each input required to end either in a usable document or in a typed exception, inside a time and an
allocation budget. A few thousand mutated documents go through
the reader on every test run — every pull request, and every package before it is published — and a
nightly campaign runs twenty thousand mutations per seed document.

The first campaign found a real defect within a minute: a mutated invoice made the reader's index lookup
and its relocation search call each other until the stack ran out. Relocation is now bounded to three
counted attempts. That is the kind of failure this exists to catch — no hand-written test had thought of
it, and a file that kills the process is the worst outcome a document reader can have.

## Guards a valid file can reach

Some of those bounds are reachable by a sound document: a large-format scan decodes past the 256 MB a stream may
decode to by default, and stored without a filter its data would run past the 256 MB the reader reads of one stream at
once; a file saved incrementally a thousand times has a thousand cross-reference sections; and nothing
in PDF 2.0 forbids more objects than the 8,388,607 the index holds by default. A bound like that is a **guard**. It is on by default, so an application that opens files it did not produce is
protected without configuring anything; it is reported under its own `limit.*` code when a document reaches it; and
its message names the option that lifts it, so that the document stays readable by whoever needs to read it.

When a guard is reached, the reader keeps what fits and warns rather than fails. The limit is the reader's, not a
fault of the file, and it replaces what the parser met where it stopped: you are not told a stream was truncated
when it was the reader that stopped reading it. The bounds live on the options rather than on a static setting, so
two documents opened side by side can be read under different ones, and a stream decodes under the limits of the
document it came from, however long after opening.

A value read through a reference to an object a guard cut, or left out of the index, is the guard's too. What the cut
left of it may be the start of what the file holds — `1` of `15`, `/Flate` of `/FlateDecode` —, so it is never judged
as a value the file gave; a whole reference it left, though, leads on to the object it names, as read whole. The data
of a stream whose `/Length` is so named is found by its `endstream`, and its length is not judged, though an `endstream`
the data lacks is still the file's to lack — unless an object header the data spells stops the search, or an
`endstream` it spells, which only the hidden length could tell from the end of the data: the data is then the guard's
too. An `endstream` is taken for the stream's own only where what follows its object's end is the next object the
index places, a cross-reference stream the chain read, or the end of the file, which data that goes on is never followed
by, or a cross-reference section's keyword, `xref` or `startxref`, which the data may spell as well: data that spells
`endstream`, then one of them, and goes on is taken to end there, which only the hidden length could tell. The end is
then the file's, though white space or comments before the keyword, which only the length says are the data's or not,
leave where it ends to the guard as the data decodes: a filter that runs out there says nothing of the file. Decoding stops at a filter so named, or one whose parameters are, and leaves
the rest of the data encoded, as past the most filters the reader runs; and a `/Prev` or an `/XRefStm` the reader
follows that is so named, or a cross-reference stream whose values lead to an object a guard cut, ends the chain as a
section a guard cut does, wherever near its named offset the section is found — unless what the values read say makes
the section unreadable whatever the cut hides, or data whose length the file gives, its filters written direct, lacks
the rows they declare whatever it hides: a fault of the file, as read whole, such a section read as read whole, lacking
rows. The rows such data may still hold are read with the value the guard cut, and are the guard's: the chain ends at
that section, as where a guard cut its rows, since an older section's rows would stand for them.

What a guard leaves out of the cross-reference index — the oldest sections of a long chain, the rows past a long
table's bound, the objects past the most the index holds — is no damage either. Those objects read as null, unknown
rather than missing, until you raise the limit; the reader does not rebuild the index for them, as it would for a file
whose index is broken, since a rebuild would report a sound file as damaged and could lose what the chain had read.

## What decoding a stream may hold

`MaxDecodedStreamLength` bounds what a stream decodes to, and a stream's data may pass through several filters, each
decoding what the one before it gave. The filters before the last share one bound for what they pass along together,
and the last has its own, so that a stream of one or two filters decodes as each filter alone would, and a chain however
long holds, and fills, two bounds at most. A `/Crypt` filter passes its data along as it is: it holds nothing more, and
is neither charged nor the last, so that an encrypted stream, whose first filter ISO 32000 makes `/Crypt`, decodes as
its twin in the clear does. When the filters before the last reach theirs, the stream comes back as what the last
decoded of what they kept, and the report says decoding stopped.

ISO 32000 sets no limit on how many filters a stream names, and real streams name one to three. Each filter costs
decoding something whatever its data — a few microseconds, and for LZW a table of tens of kilobytes —, and a filter
whose data decodes to itself never runs out of it: a `/Filter` array of millions, which an object stream's member can
hold in a file of a few hundred kilobytes, cost seconds on every decode, more for filters that never run out of data.
`MaxFilterChainLength` bounds how many filters a stream's data passes through, 64 by default: decoding stops before the
first past it, as at a filter the library does not know, and nothing past it is read. Data a filter decodes to nothing
ends decoding too, since every filter after it would decode nothing to nothing. Of the filters it read, decoding keeps
less than the `/Filter` array holds: nothing for a name the array holds itself.

A filter's parameters can name an object, and that object can sit in an object stream that has to be decoded first,
whose own parameters can name another. A stream therefore reads its filters and every parameter they need before it
reads any of its data, and decodes once every object it needs is loaded: however deep such a chain of loads goes, no
stream on it holds decoded data while the next one decodes. What decoding holds at once is the object streams the
reader keeps, 32 MB together, the one it decoded last, whatever its size, and the decoding in progress. That one costs
more than its output: a Flate filter allocates up to about five times what it decodes to, and holds two to three times
that while it decodes.

Before its first filter runs, a stream's encoded data is read, and that read is bounded too. ISO 32000 sets no limit on
a stream's length, and data no filter decodes — none named, an image filter, one the library does not know, one that
could not decode it — comes back as it is, out of reach of `MaxDecodedStreamLength`, which bounds what filters produce.
`MaxStreamLength` bounds what is read of one stream's data at once, 256 MB by default; past it the data is cut, and its
filters decode what was read as data the reader cut. No stream keeps its data: it is read each time it is asked for,
lent where it lies when the document is held in memory, read into an array of its length from a file. A stream costs
its data only while it is read or decoded, and for as long as you keep what that gave you, and streams whose data share
a stretch of the file do not each hold a copy of it.

`PdfReaderLimits.Unbounded` takes every limit to the most the implementation can hold. It is for documents your own
application produced, never for uploads: under it, a file can make the reader hold whatever it asks for, up to those
maxima.

## Bounds only a damaged file reaches

Other bounds protect the reader itself — its stack, the chains of references it follows — and are not reached by the
documents producers write: real documents nest a handful of levels, not 128, and producers write an object rather
than a reference to a reference. An object stream's header lists its objects by increasing offset, so that a sound
one's objects each lie before the next one starts: the reader reads each no further, and a header that breaks that
order ends where it breaks it. A sound stream's `endstream` follows its declared length; where it does not, or the
`/Length` gives none, the reader searches the file for it past the bytes it parsed the object through, rather than parse
the object through a larger window, so that a stream's data — and the end-of-line after its `stream` keyword — never
counts toward its object's length. A document's
searches read four times the file at most: a valid file is read past those bytes only for the data of a cross-reference
stream whose `/Length` the chain cannot read yet, for the data of a stream whose `/Length` a guard cut or left out of the
index, and for white space and comments after a declared length, about twice the file — three times over the white
space a cross-reference stream's search takes as data that its length, once read, is read on through —, and for the
proof that an `endstream` found under a hidden `/Length` is the stream's own, about three times the file in all. Those stay internal
constants, since lifting them would let nothing more be read, and
each says where it is declared why no valid file reaches it.

A new bound is classified when it is added ([ADR 34](/project/adr/every-valid-pdf-is-readable-and-the-readers-guards-are)):
if a valid file can reach it, it becomes a limit, with its code and its test.

One kind stays an internal constant though a valid file reaches it: a bound on what the library repeats of what it
read, not on what it reads. A message quotes at most 127 bytes of a name, which PDF 2.0 lets be longer, and the whole
name stays in the document; there is nothing more to read by lifting it. Its declaration says so instead ([How a
message quotes the file](../reference/diagnostics.md#how-a-message-quotes-the-file)).

## Refusing instead

Some applications would rather refuse a document than read part of it, and `ThrowOnLimit` turns every limit into an
exception. Reading is lazy, so that exception can come from any operation that reads — opening the document,
resolving an object, decoding a stream, validating —, long after `Open` returned. That is the price of reading only
what is asked for: a limit is met where the data is, and the data is read when you ask for it.
