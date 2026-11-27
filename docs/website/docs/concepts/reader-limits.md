---
title: Reader limits
sidebar_position: 3
description: How the reader keeps every valid PDF readable while treating every file as hostile, and why its guards are on by default.
---

# Reader limits

Every PDF that is valid under the specification can be read. That promise has to live alongside another:
a file arriving from outside your system is treated as hostile, and a few kilobytes of it must not be able
to make the reader hold gigabytes. The two meet in the **reader limits**: bounds on what a file may make the
reader hold or read, on by default, each of which you can raise. The [reference](../reference/reader-limits.md) lists them;
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
once; a file saved incrementally a thousand times has a thousand cross-reference sections; nothing
in PDF 2.0 forbids more objects than the 8,388,607 the index holds by default; a rebuild of a file whose attached
data spells streams can read past their lengths, to confirm them, many times the file; and a page tree whose pages lie in
turn in two large object streams has a validation decode them again for each page. A bound like that is a **guard**. It is on by default, so an application that opens files it did not produce is
protected without configuring anything; it is reported under its own `limit.*` code when a document reaches it; and
its message names the option that lifts it, so that the document stays readable by whoever needs to read it.

When a guard is reached, the reader keeps what fits and warns rather than fails. The limit is the reader's, not a
fault of the file, and it replaces what the parser met where it stopped: you are not told a stream was truncated
when it was the reader that stopped reading it. The bounds live on the options rather than on a static setting, so
two documents opened side by side can be read under different ones, and a stream decodes under the limits of the
document it came from, however long after opening.

A value a stream is read with — its `/Length`, its filters or their parameters — read through a reference to an object
a guard cut, or left out of the index, is the guard's too, never judged as a value the file gave, though the cut keeps
no token it parts, nor an integer that may start a reference past it
([Tokens a bound parts](../reference/reader-limits.md#tokens-a-bound-parts)); a whole reference it left leads on to
the object it names, as read whole. The data
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
rows, or unreadable where its filters decode nothing of its data. The rows such data may still hold are read with the
value the guard cut, and are the guard's: the chain ends at that section, as where a guard cut its rows, since an older
section's rows would stand for them.

What a guard leaves out of the cross-reference index — the oldest sections of a long chain, the rows past a long
table's bound, the objects past the most the index holds, the objects whose headers a rebuild's scan may have left in
data it no longer reads once it has read past streams' direct lengths, to confirm them, as much as `MaxRebuildReadPasses`
lets it — is no damage
either. Those objects read as null, unknown rather than missing, until you raise the limit; the reader does not rebuild
the index for them, as it would for a file whose index is broken, since a rebuild would report a sound file as damaged
and could lose what the chain had read.

The chain of cross-reference sections ends at the section a guard stopped, since an older section's rows would stand for
those the guard left unread. Where the guard stopped its rows — cut them, or filled the index at them —, cut the trailer
of a classic table ([#328](https://github.com/AdCodicem/AdCodicem.Pdf/issues/328)), or cut a value a cross-reference
stream's rows are read with ([#329](https://github.com/AdCodicem/AdCodicem.Pdf/issues/329)), what that section names is
still the file's to answer for, though, where it can name no section whatever the guard hid: a `/Prev` that is no offset
or lies outside the file, one that names the section itself or another the chain has reached, an `/XRefStm` that names
no cross-reference stream. The reader judges such a value as a read of the whole file judges it, and reads nothing past
the guard to do so: such a guard hides no damage that can be seen without reading past it, and what the lost sections
index is found by rebuilding the index, as in the file read whole. A value that may name a sound section — an offset in
the file the chain has not reached, a reference to a row the guard left unread — stays the guard's, the older sections
with it ([#264](https://github.com/AdCodicem/AdCodicem.Pdf/issues/264)). In a trailer the guard cut, a value written
whole before the cut is the trailer's, and the `/XRefStm` it gives over rows read whole is followed, its stream holding
the table's own revision's rows ([#328](https://github.com/AdCodicem/AdCodicem.Pdf/issues/328)). A hybrid table's
`/XRefStm` whose offset holds no section, and whose search nearby stops at what the table's `/Prev` names because a
guard cut it before it said what it is, or cut a value its rows are read with — the `/Prev` may read a section there,
which the guard hides —, is the guard's too: nothing is reported of it, and the index is not rebuilt for it. Wherever
the chain names a section, a table or a stream, `MaxTrailerLength` bounds apart the white space and comments the offset
falls short by, and the section is measured from its first token — a stream's dictionary from its object's header, a
classic table's rows from its `xref` keyword, toward `MaxXRefSectionLength` alone
([#289](https://github.com/AdCodicem/AdCodicem.Pdf/issues/289),
[#295](https://github.com/AdCodicem/AdCodicem.Pdf/issues/295)). Where `MaxTrailerLength` cuts what lies at that offset
before it says whether it is a section — the white space running on past the bound, the object's header, or its value
before the value says what it is —, the section is the guard's: cut where it is named and not looked for nearby, where a
search would read past the guard and may take another section, the chain ending there as at any section a guard cuts;
for the one `startxref` names, which leaves the chain no section, the index is rebuilt, and the validator says it
checked the chain in part. What the reading tells before the cut stays the file's: a value that starts as no dictionary
is no cross-reference stream however it goes on. Where the reading does not take what the cut left for a section — a
dictionary whose `/Type`, written whole before the cut, names another type, or a token no header starts with —, no
cross-reference stream passed over near a hybrid table's `/Prev` is reported anywhere in the document: the cut may have
hidden a link the chain would follow, which the reader cannot tell without reading past the guard
([#279](https://github.com/AdCodicem/AdCodicem.Pdf/issues/279)). The reference says what each cut is taken for:
[`MaxTrailerLength`](../reference/api/AdCodicem.Pdf.Documents.PdfReaderLimits.md#AdCodicem_Pdf_Documents_PdfReaderLimits_MaxTrailerLength)
and
[`limit.trailer`](../reference/api/AdCodicem.Pdf.Diagnostics.PdfDiagnosticCodes.md#AdCodicem_Pdf_Diagnostics_PdfDiagnosticCodes_LimitTrailer).
Where the guard cuts the trailer of a classic table an `/XRefStm` names, whose links the chain never follows
([#263](https://github.com/AdCodicem/AdCodicem.Pdf/issues/263)), the cut hides no link the chain follows and ends
nothing, the chain going on through the table that names it, and the report is made. An `/XRefStm` that names what the
`/Prev` names, at that offset or in the white space before it, names no stream of the table's revision whatever the
guard hid, and is reported, the validator saying the guard hides what is there.

## What decoding a stream may hold

`MaxDecodedStreamLength` bounds what a stream decodes to, and a stream's data may pass through several filters, each
decoding what the one before it gave. The filters before the last share one bound for what they pass along together,
and the last has its own, so that a stream of one or two filters decodes as each filter alone would, and a chain however
long holds, and fills, two bounds at most. A `/Crypt` filter passes its data along as it is: it holds nothing more, and
is neither charged nor the last, so that an encrypted stream, whose first filter ISO 32000 makes `/Crypt`, decodes as
its twin in the clear does. When the filters before the last reach theirs, the stream comes back as what the last
decoded of what they kept, and the report says decoding stopped.

ISO 32000 sets no limit on how many filters a stream names, and real streams name one to three. Each filter costs
decoding something whatever its data — a few microseconds, and for LZW a table it rents —, and a filter
whose data decodes to itself never runs out of it: a `/Filter` array of millions, which an object stream's member can
hold in a file of a few hundred kilobytes, cost seconds on every decode, more for filters that never run out of data.
`MaxFilterChainLength` bounds how many filters a stream's data passes through, 64 by default: decoding stops before the
first past it, as at a filter the library does not know, and nothing past it is read. Data a filter decodes to nothing
ends decoding too, since every filter after it would decode nothing to nothing; the filters after it are read all the
same, and one the library does not know, or an element that names none, is still reported as `filter.unsupported`. Of
the filters it read, decoding keeps less than the `/Filter` array holds: nothing for a name the array holds itself.

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

## What the library's own walks decode again

Each limit above bounds one step: one stream's decoding, one object's parsing. Steps repeat, though. The reader keeps the
object streams it decoded within its 32 MB budget, and decodes again a stream it let go when one of its objects is read
again — the price of memory that follows the heaviest stream rather than their number. Two streams that each decode past
16 MB cannot be kept together, so objects read from them in turn decode a whole stream each: a valid page tree whose
pages alternate between them, or a 45 KB file whose cross-reference stream places 100,000 numbers in them in turn, which
the search for a catalog reads one by one, decoding about two terabytes before the document opens
([#181](https://github.com/AdCodicem/AdCodicem.Pdf/issues/181)).

The reads you make are yours to order, and are never cut for this. The library's own walks of a document — the search for
a catalog a `/Root` that leads to none sets off, and a validation's rules — weigh what they decode again:
`MaxObjectStreamDecodePasses`, 4 by default, lets one walk decode again, together, the streams it let go up to four times
what its first decodings of them weighed. Past it, the walk leaves the rest unread as a guard's cut, goes on, and
reports `limit.object-stream-decodes`. A valid file reaches it — the page tree above —, so it is a guard, and what it
leaves unread is the reader's limit: the validator judges nothing of it, and finds no fault where the file has none.
The search for a catalog does not leave it at that when it finds none among what it read: rebuilding the index
would scan the whole file, and could take a catalog an update superseded, while reading again what the guard cut, those
of each object stream together, decodes each stream once more at most; so it does that first, and takes the first of
them, in the index's order, that reads as a catalog — read whole this time, or cut again, as the first search takes
one —; where it finds none — a candidate's `/Type` lying in a stream it may not decode again —, the index is rebuilt as
before. Nothing the walk left unread is kept. An object whose load read through it is kept until the walk ends, and no
longer, so that the walk reads such an object once, however many candidates lead to it, as long as the cache holds it:
one the cache let go first is read again. With `ThrowOnLimit`, where such a load throws instead, the walk reads the
object once all the same ([`ThrowOnLimit`](../reference/reader-limits.md#throwonlimit)). An object stream whose decoding
met the cut is not kept at all, but counted as decoded, so that the walk does not decode it again for each of its
objects. Your own reads once the walk is done read every object whole, and the next walk starts its budget again. The
rules that read every object of the index read those of each object stream together, so that a sound file's validation
decodes each stream once, whatever its numbers' order.

## Bounds that stay internal

Other bounds protect the reader itself — its stack, the chains of references it follows — and are not reached by the
documents producers write: real documents nest a handful of levels, not 128, and producers write an object rather
than a reference to a reference. An object stream's header lists its objects by increasing offset, so that a sound
one's objects each lie before the next one starts: the reader reads each no further, and a header that breaks that
order ends where it breaks it. A sound stream's `endstream` follows its declared length; where it does not, or the
`/Length` gives none, the reader searches the file for it past the bytes it parsed the object through, rather than parse
the object through a larger window, so that a stream's data — and the end-of-line after its `stream` keyword — never
counts toward its object's length. None of a
document's searches starts once they have read four times the file: a valid file is read past those bytes only for
the data of a cross-reference stream whose `/Length` the chain cannot read yet, for the data of a stream whose `/Length` a guard cut or left out of the
index, and for white space and comments after a declared length, about twice the file — three times over the white
space a cross-reference stream's search takes as data that its length, once read, is read on through —, and for the
proof that an `endstream` found under a hidden `/Length` is the stream's own, about three times the file in all. The
numbers that cross-reference sections the reader could not read whole lost, which no older section's row may stand for,
are kept as 4,096 ranges at most — those of the sections read so far that meet joined, and those a table loses each
joined as it is lost to the last one lost where the two meet —, past which every number the newer sections do not give
is looked for by rebuilding the index: a valid file's sections lose none ([#208](https://github.com/AdCodicem/AdCodicem.Pdf/issues/208),
[#210](https://github.com/AdCodicem/AdCodicem.Pdf/issues/210)). Those stay internal constants, since lifting them would
let nothing more be read, and each says where it is declared why no valid file reaches it. The scan that rebuilds the
index counts what it reads apart, and three ways. The reads that confirm a stream's direct `/Length` decide which
streams it confirms: a valid file's own streams spend about twice the file of them, but the streams an attached file's
bytes spell inside a stream's data the scan does not confirm count too, and can spend four times the file, so they are
a guard, `MaxRebuildReadPasses`; once it is reached, the scan reads the data of each stream it cannot confirm up to its
first `endstream`, rather than as objects. The reads that confirm an indirect `/Length` look for its object in the
file's bytes, a few objects before the stream or the first object after it, where writers put it, and have a budget
of their own: an internal constant, as are the few objects and the bytes they look through, since a valid file reaches
that search only through a rebuild a reported guard sent it to, which raising that guard spares, and past those bounds
a stream is read as before the search existed, nothing reported
([What cannot be lifted](../reference/reader-limits.md#what-cannot-be-lifted),
[#363](https://github.com/AdCodicem/AdCodicem.Pdf/issues/363)). The reads that tell only how the searches near a
row or an offset rank a header found in such data ([Lazy reading](lazy-reading.md#what-opening-actually-does)) have four
times the file of their own, an internal constant: what a rebuild serves does not hang on them, a rebuild's own scan
makes none of them, and those searches run only where a row or an offset is off
([#361](https://github.com/AdCodicem/AdCodicem.Pdf/issues/361)). And a stream whose length the reader takes without
reading where its data ends — one an `endstream` confirms, one taken without a search once the searches have read four
times the file, or what a search made for another object whose data starts at the same place found, as each number of
one body that many headers name but the one read first takes it — keeps that length while the lengths so taken, each
object and place counted once at its longest, total at most four times the file: streams whose data share no byte, whatever row a damaged file left stale inside them, total at
most the file, and none of them is ended, while streams that share a stretch of the file, each holding the next one's
header, or one body read under many numbers, would otherwise read that stretch again for each read of each one, the file
in its square. Past that, a stream's
data ends at an object either index places inside it, as long as what the streams so ended keep stays within four times
the file more, or reads empty: what such streams serve stays within eight times the file once the reader counts, besides
what the searches read for the objects they were made for — four times the file, and the stretch of the search under way
when they reach it, which reads on to its stop: about five times the file at most —, whatever the order they are read in
and whatever the indexes place, and it counts only once they were parsed four times the file over: a valid file is spared
the count until its streams have been parsed that often, and none of them is ever ended, since, counted once each, they
total less than the file
([What cannot be lifted](../reference/reader-limits.md#what-cannot-be-lifted),
[#280](https://github.com/AdCodicem/AdCodicem.Pdf/issues/280)).

A new bound is classified when it is added ([ADR 34](/project/adr/every-valid-pdf-is-readable-and-the-readers-guards-are)):
if a valid file can reach it, it becomes a limit, with its code and its test.

Two kinds stay internal constants though a valid file reaches them. One is a bound on what the library
repeats of what it read, not on what it reads. A message quotes at most 127 bytes of a name, which PDF 2.0 lets be
longer, and the whole name stays in the document; there is nothing more to read by lifting it. Its declaration says
so instead ([How a message quotes the file](../reference/diagnostics.md#how-a-message-quotes-the-file)). The other is
a threshold that changes only when the library reads an object, never what it reads nor what it reports: validating a
document whose objects many others name before the validator reaches them, it reads those objects sooner rather than
keep a count of each reference, and the report is the same.

## Refusing instead

Some applications would rather refuse a document than read part of it, and `ThrowOnLimit` turns every limit into an
exception. Reading is lazy, so that exception can come from any operation that reads — opening the document,
resolving an object, decoding a stream, validating —, long after `Open` returned. That is the price of reading only
what is asked for: a limit is met where the data is, and the data is read when you ask for it.
