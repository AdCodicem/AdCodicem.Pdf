# 34. Every valid PDF is readable, and the reader's guards are options

Date: 2026-09-26

## Status

Accepted on 2026-09-26. The rule is the maintainer's, stated when the 256 MB decoding bound turned out to be
the library's own choice rather than the format's: *every PDF that is valid under the specification must be
readable; guards may protect against exceptional cases the specification allows but that could be
dangerous, and options must be able to lift them.* It is invariant 12 in `CLAUDE.md`. It extends
[14](0014-a-tolerant-reader-with-a-diagnostic-report.md), whose tolerant reader bounded what it read without
saying which bounds a sound file could reach, and follows [8](0008-facade-immutable-options-dependency-injection.md)
in making them options on an immutable record.

Implemented by `PdfReaderLimits`, `PdfReaderOptions.Limits` and `PdfReaderOptions.ThrowOnLimit`,
`PdfLimitExceededException`, the ten `limit.*` codes of `PdfDiagnosticCodes`, the `readerLimits` field of
`tests/corpus/manifest.json`, and `ReaderLimitsTests`.

Amended on 2026-09-27, by the maintainer, while M08 was specified: one record bounds every hostile read, the
font programs a caller registers included (*One record for every hostile read*, below); M08 implements it.

Amended on 2026-10-01, by the maintainer, while [#159](https://github.com/AdCodicem/AdCodicem.Pdf/issues/159) was
fixed: a bound on what the library repeats of a value it read, rather than on what it reads, changes nothing a file
can make it read, and is an internal constant even where a valid file reaches it. A message quotes at most 127 bytes
of a name (`FileQuote.MaxBytes`), though PDF 2.0 allows a longer one, and at most the first and last four steps of a
key path (`RuleText.PathEnds`); the whole value stays in the document. Such a constant's declaration says so, as
`No guard (ADR 34): it bounds what a message repeats, not what is read`, in place of the reason no valid file
reaches it. `PdfObjectParser.EndObjLookahead`, which bounded what the reader saw of an object's end, was the first; the
2026-10-23 amendment made it the size of a first read, and no bound.

Amended on 2026-10-14, by the maintainer, while [#164](https://github.com/AdCodicem/AdCodicem.Pdf/issues/164) and
[#183](https://github.com/AdCodicem/AdCodicem.Pdf/issues/183) were fixed: a sixth guard, `MaxIndexEntries`
(8,388,607, `limit.index-entries`), bounds the object numbers the index holds, whatever gives them — a section's rows,
a rebuild's headers, an object stream a rebuild expands. Nothing bounded them before: a cross-reference stream of
one-byte rows decoding to 256 MB from a few hundred bytes asked for 268 million entries, and two internal constants
said to bound the rest did not — a rebuild counted headers rather than objects, and stopped silently behind two million
copies of one; a subsection's count bounded none of the rows an object stream adds. Both constants are folded into
the guard, and the sentence of the context that classified them is struck. And what a guard leaves out of the index
is no longer rebuilt as damage: it reads as null, unknown rather than missing, until the property is raised
(*Reaching a guard*, below).

Amended again on 2026-10-14, by the maintainer, while [#165](https://github.com/AdCodicem/AdCodicem.Pdf/issues/165),
[#166](https://github.com/AdCodicem/AdCodicem.Pdf/issues/166) and [#199](https://github.com/AdCodicem/AdCodicem.Pdf/issues/199)
were fixed: `MaxDecodedStreamLength` bounded each filter of a chain alone, so that a stream cost what its `/Filter` array
asked — a 513-byte cross-reference stream naming `/RunLengthDecode` sixteen times allocated 3.3 GB as it opened, and a
sound chain of stored Flate layers 14.9 GB with nothing reported. The filters before the last now share one bound for what
they pass along together, and the last keeps its own: a stream of one or two filters decodes as before, and no count of
filters becomes a bound, since ISO 32000 sets none. What follows a filter that stopped at the bound is data the reader cut,
reported once as the guard, never as damage. And the reason given for `MaxNestedLoads` was false: a filter's parameters
can name an object an object stream holds, and decoding one stream could decode another while holding its own output, 64
deep. A stream now resolves its filters and every parameter they read before any of its data, so that no level of a load
chain holds decoded data while the next loads; that is what keeps the bound an internal constant.

Amended on 2026-10-15, by the maintainer, during the review of the same change: no count of filters was a bound, yet
each filter costs decoding something whatever its data — a few microseconds, and for LZW a table of 42 KB —, and a
filter whose data decodes to itself never runs out of it. A `/Filter` array of forty million names, which an object
stream's member holds in a file of 350 KB, cost 1.8 s on every decode, and filters that never run out of data could cost
minutes. A seventh guard, `MaxFilterChainLength` (64, `limit.filter-chain`), bounds the filters a stream's data passes
through, counted as its `/Filter` array names them: decoding stops before the first past it, as at a filter the library
does not know, and nothing past it is read. Real streams name one to three. And a `/Crypt` filter, which passes its data
along as it is, is neither charged to the bound the filters before the last share nor the last of them: an encrypted
stream, whose first filter ISO 32000 makes `/Crypt`, decodes as its twin in the clear does. M16 takes it up again when
`/Crypt` decrypts.

Amended again on 2026-10-15, by the maintainer, while [#144](https://github.com/AdCodicem/AdCodicem.Pdf/issues/144) was
fixed: a stream was reported each time it was decoded past the bound, and the reader decodes an object stream again once
its budget let it go, a rebuild once more, and the validator once more: with `MaxDecodedStreamLength` at 1 KB, 56 corpus
documents held a guard's report twice or three times, as a damaged object stream held its filter's fault. A document's
own diagnostics now hold a stream's report once, however often the reader, the validator or a caller decodes it, as they
hold an object's; diagnostics a caller passes to `Decode` hold every decoding's. The guard is still counted each time it is
reached, so that what it cut is never taken for damage, and a document opened to throw throws each time.

Amended on 2026-10-21, by the maintainer, while [#160](https://github.com/AdCodicem/AdCodicem.Pdf/issues/160) and
[#200](https://github.com/AdCodicem/AdCodicem.Pdf/issues/200) were fixed: `MaxObjectLength` bounds an object stream's
member as it bounds an object written in the file — a member of millions of nested arrays was parsed whole, whatever the
guard —, and is reported once for each member it cuts. What an object stream's header says is bounded by internal
constants, each because ISO 32000-1 (7.5.7) asks it of every valid file: a member is read no further than the next
member's start, the last no further than the end of the data; a pair whose offset is not past the one before it, or whose
member would start past the data, ends the header as a stray token does. And a header lists at most a quarter of its
`/First` bytes and one, whatever its `/N` claims: a count of what its bytes can hold, not a bound, its tables growing with
the pairs read. The consequence that raising `MaxDecodedStreamLength` multiplies what the cache of decoded object streams
holds until M23 bounds it (T33) was stale: `ObjectStreamBudget` bounds it since 2026-09-28.

Amended on 2026-10-23, by the maintainer, while [#170](https://github.com/AdCodicem/AdCodicem.Pdf/issues/170),
[#174](https://github.com/AdCodicem/AdCodicem.Pdf/issues/174), [#220](https://github.com/AdCodicem/AdCodicem.Pdf/issues/220),
[#224](https://github.com/AdCodicem/AdCodicem.Pdf/issues/224), [#230](https://github.com/AdCodicem/AdCodicem.Pdf/issues/230)
and [#263](https://github.com/AdCodicem/AdCodicem.Pdf/issues/263) were fixed: an eighth guard, `MaxStreamLength` (256 MB,
`limit.stream-length`), bounds what is read of one stream's encoded data at once. The file alone bounded it: decoding
handed back data no filter decodes — none named, an image filter, one the library does not know, Flate over data that is
none — whole, past `MaxDecodedStreamLength` and unreported, and a `/Length` past what an array holds threw an
`OutOfMemoryException` each time the data was read. The data is now kept by nothing and read each time it is asked for,
so the guard bounds each read: past it the data is cut, what the filters lose past the cut is no damage, the stream is
reported once in its document's diagnostics and, as `limit.decoded-stream` is, with each decoding into diagnostics a
caller passes to `Decode`, and its length stays the one the file holds. `MaxDecodedStreamLength` keeps bounding what the
filters produce, so an uncompressed image past the default needs the new guard raised alone. Nor does a stream's data
count toward `MaxObjectLength`: a `/Length` the file cannot hold grew the window an object is parsed through up to that
bound, reading 21.5 MB to find five bytes and reporting `limit.object`; no window grows to find an `endstream` now, and
one search past the window does. What that search reads, with the white space read on after a declared length, is
bounded by `EndStreamSearchPasses`, four times the file for a document's searches together, which stays an internal
constant with its reason rewritten: a valid file is read past the parser's window only by the search for the data of a
cross-reference stream whose `/Length` the chain cannot read, by the search for the data of a stream whose `/Length` a
guard cut or left out of the index, and by white space and comments after a declared length that run past a first
read — about twice the file, three times over the white space a cross-reference stream's search takes as data that its
length, once read, is read on through; the proof that an `endstream` found under a hidden `/Length` is the stream's
own reads 48 or 64 bytes past each, once, and the white space it crosses once more at most — the run before an
`endobj` taken from the read-on that read it, a run a row names before the next header read once from the row —, about
three times the file in all (amended on 2026-10-24).
`MaxEndStreamGap`, the four bytes of white space accepted
between a stream's data and its `endstream`, is removed: more, or a comment, made a right `/Length` a wrong one, and
white space and comments are read on as far as they run, counted with the searches — a comment only once white space
comes before it, since a `%` right where the length ends is the data's. Comments after the `endobj` that ends a stream
with no `endstream` are white space too, read through the search's own reads: neither rule adds a bound.
`EndObjLookahead` is the size of that first read, no longer a bound. `MaxTrailerLength` bounds a cross-reference
stream's dictionary alone: the data of one whose `/Length` the chain cannot read is searched for its `endstream` in the
file rather than through a window the guard bounded — a stream of 70,000 bytes of rows reads as its twin with a direct
`/Length`, with no `limit.trailer`, and `ThrowOnLimit` no longer throws for it —, and data no `endstream` follows before
the next object the sections read so far place, or the end of the file, makes its section malformed: the file's fault,
and no guard's, unless that `/Length` leads to an object `MaxObjectLength` cut (below). A value read through a reference
to an object a guard cut or left out of the index — a stream's `/Length`, `/Filter` or `/DecodeParms`, a value of a
filter's parameters, a cross-reference stream's `/W`, `/Index` or `/Size`, a trailer's `/Prev` or `/XRefStm` — is the
guard's, not the file's: an integer the cut parted, `1` of `15`, is only a place to look for `endstream` at, never a
declared length, though a stream that lacks its `endstream` is still the file's fault, unless a header its data spells,
or an `endstream` nothing after it proves the stream's own — the next object the index places, a cross-reference
stream the chain read, the end of the file, `xref` or `startxref`, after the object's `endobj` or in its place (amended
on 2026-10-24) —, stops the search, the
data up to it then the guard's; a whole
reference the cut left leads on, as read whole; a chain of filters ends there,
its data left encoded; and a cross-reference section whose values lead to an object `MaxObjectLength` cut ends the
chain, nothing rebuilt — unless what the values read say makes it unreadable whatever the cut hides, or data whose length the file gives, its filters written direct, lacks the rows they declare whatever it hides — the file's fault, such a section read as read whole, lacking rows
(amended on 2026-10-24), the chain going on where the data holds no row: rows it may hold, read with the cut value, are
the guard's (amended on 2026-10-25) —, the older sections and their trailers the guard's to hide, and the guard reached once the chain
is read. So does a cross-reference stream's `/Length` the chain could not read that leads to such an object, wherever
only that length could say how far the data runs: no `endstream` comes before the next object or the end of the file, or
the rows spell one and none confirms what the cut left of the length, the rows past it then the guard's. A guard that
loading a value of a stream's filters reaches is that value's, not a cut of the stream's own data, whose faults it hid.
No bound is added for those values, nor for the next object's header that ends an object's open containers: no valid
object holds `obj` where a value is expected.

Amended on 2026-11-04, while [#264](https://github.com/AdCodicem/AdCodicem.Pdf/issues/264) was fixed: the chain still
ends at a section whose rows a guard stopped — `MaxXRefSectionLength` or `MaxDecodedStreamLength` cut them, or
`MaxIndexEntries` filled at them —, and at a hybrid table whose stream a guard so stopped, since an older section's rows
would stand for those the guard left unread; but what that section names is judged as far as the guard lets it be, and
nothing past the guard is read. A `/Prev` or an `/XRefStm` that can name no section whatever the guard hid — no offset, a
reference to a free row the reader read or to a number a newer section that lost rows refused among them, an offset
outside the file, a section the chain has reached, for an `/XRefStm` what the table's `/Prev` names too, at that offset
or through the white space before that section — is the file's, reported as a whole read reports it, and leaves the
index incomplete as it does read whole, so that what only the sections it should have named index is found by a rebuild
when it is asked for; one that may name a sound section — an offset in the file the chain has not reached, a reference
to a row past the cut — stays the guard's, and so do the trailers past it. A reference the reader follows through rows
it read is judged as the value it leads to. A guarded read said nothing of such a value, and served what only the older
sections index as null, the guard blamed for the file's damage. A classic table `MaxXRefSectionLength` cuts before its
first row is the guard's whatever the index holds, its `xref` keyword having declared it a section, and what its trailer
names is judged so too. A `/Prev` in a trailer `MaxTrailerLength` cut is not judged yet, nor an `/XRefStm` in such a
trailer found past rows a guard stopped; the `/XRefStm` that the cut trailer of a table read whole gives is followed as
if whole, though the cut may have parted its digits ([#328](https://github.com/AdCodicem/AdCodicem.Pdf/issues/328)).
Nor is the `/Prev` of a cross-reference stream judged where a guard's cut of a value its rows are read with ends the
chain at it ([#329](https://github.com/AdCodicem/AdCodicem.Pdf/issues/329)).

Amended on 2026-11-15, by the maintainer, while [#171](https://github.com/AdCodicem/AdCodicem.Pdf/issues/171) was fixed:
the rebuild's scan counts its own reads past streams' declared lengths apart from the loads' searches, against the same
`EndStreamSearchPasses` multiple, and reads the value of a stream's indirect `/Length` from the file's bytes, never
through an index. A valid file can lead that read astray: the objects before a stream may hold an older revision's
object of the length's number, and its data an attached file's, whose integer ends the data anywhere in the file — many
streams' in one run of white space, which reading on past each length would cross once for each. So the scan always
leaves the data of a stream whose length it cannot confirm — such an indirect one, or a direct one no `endstream`
follows — as the bytes give it, up to its first `endstream`, whatever the length says; the length sets only how the
scan's memo counts the places it takes there, as found in the data or between objects. Past an indirect length read
from the file's bytes the scan reads the 64 bytes of a first read, uncounted, and no further; where they, or the reads
made past a direct length to confirm it, show that no `endstream` follows, the search for where a load with that length
ends the data follows, from the data's start, its reads counted; and, for an indirect length the scan reads while it
still reads that data, at the header or keyword where it reads it — among the last values it passed, or in the data —,
or never reads, the reads that tell that data from an intact stream's — whether a `stream` keyword comes before its
first `endstream`, and, where the scan read the length, an `endobj` after it — follow, counted past the bytes the scan
holds; a length's object the scan meets past the data, where a writer usually puts it, has no such reads. The memo
counts what the scan takes past where that load ends the data as found between objects, unless those reads show the data
intact. The constant's reason covers the scan's counted reads of a valid file's own streams, about twice the file; but
the streams an attached file's bytes spell in data the scan does not confirm, which it reads as objects, count too, and
can reach it on a valid file, after which a later stream whose direct length a first read cannot confirm is left
unconfirmed and has the headers its data spells taken: the one way the reads made for the memo can change what the scan
takes. By the maintainer's choice, the constant stays internal all the same: a known exception to the rule of the
Decision that a bound a valid file can reach becomes a guard, tracked by
[#361](https://github.com/AdCodicem/AdCodicem.Pdf/issues/361), which says what would pay it — a guard alone would not,
since one reached would still leave that later stream unconfirmed.

Amended on 2026-11-17, by the maintainer, while [#361](https://github.com/AdCodicem/AdCodicem.Pdf/issues/361) was fixed:
the exception the amendment before this one recorded is closed. A ninth guard, `MaxRebuildReadPasses` (4,
`limit.rebuild-reads`), bounds the rebuild's scan's reads that confirm streams' direct lengths, as a multiple of the
file's length, the product held at the most a position can name. Past it, the scan reads the data of each stream it
cannot confirm up to its first `endstream`, nothing taken there, rather than as objects, so that the streams an attached
file's bytes spell there no longer spend it, and a later stream's data spells nothing the scan takes; what the guard
still changes is the data of an attached file that holds an `endstream` of its own before the stream's, past which the
scan takes what follows. It is reported once a rebuild, where the data starts of the first stream it left so, and a
rebuild that reaches it finishes the index before it throws. The reads that decide only how the scan's memo counts the
places it takes have a budget of their own, four times the file, an internal constant: what the scan takes does not hang
on them, and only the searches near a row or an offset that is off — no valid file's — read those kinds. The loads'
searches stay bounded by `EndStreamSearchPasses`, an internal constant whose reason covers every read they count.

Amended on 2026-11-18, by the maintainer, while [#363](https://github.com/AdCodicem/AdCodicem.Pdf/issues/363) was fixed:
the rebuild's scan now confirms a stream's indirect `/Length` where the file's bytes give its object and an `endstream`
follows the length it holds: one of the last eight values the scan passed since the last trailer it met, the first that
holds an integer deciding; where none does, the first object header past the data's first `endstream`, when it is the
length's object, or, for an attached file's data, the first header of the length's number whose object holds an integer
within a mebibyte past it — the search stopping, either way, at the end of the revision, the `%%EOF` no `endstream`
follows, as the values stop at its trailer. `MaxRebuildReadPasses` still bounds only the reads that confirm direct
lengths. The search's own reads have a budget of their own, four times the file, spent silently, and its other bounds —
the eight values, the mebibyte, the 64 bytes a length's object is read within, and what each of its reads repeats of the
one before — are internal constants too, by the maintainer's choice: a valid file reaches this search only through a
rebuild that a reported guard sent it to, and raising that guard reads it through its index; past one of these bounds
the scan reads the stream as it did before the search existed. A first version counted the search against the guard,
apart from the direct lengths; a damaged file whose lengths sit in an object stream, as one writer puts them, then
reached it on a few pages, and was read worse than before.

Amended on 2026-11-20, by the maintainer, while [#181](https://github.com/AdCodicem/AdCodicem.Pdf/issues/181) was fixed:
a tenth guard, `MaxObjectStreamDecodePasses` (4, `limit.object-stream-decodes`), bounds what the library's own walks of a
document decode again of the object streams the reader let go, a remark on `ObjectStreamBudget` having claimed that no
file was read differently for that budget. Two streams that each decode past half of it evict each other, so a walk that
reads their objects in turn decodes a whole stream for each: the search for a catalog through a 45 KB file whose
cross-reference stream places 100,000 numbers in two streams of 20 MB, and a validation of a valid page tree whose pages
alternate between them. The guard is scoped to the walks the library makes of its own accord — the search for a catalog,
as the document opens or as a rebuild finishes, and a validation's rules —, each counted from its start, a nested one with
it: during one, the streams it decoded and let go may be decoded again up to that many times what its first decodings of
them weighed; past it, an object such a stream holds reads as null until the walk ends, as one a guard cut, reported once
a document. What so reads as null is not kept. An object whose load read through it is kept until the walk ends only, and
so, under `ThrowOnLimit`, is the guard's exception for the loads it passed through, so that the walk reads each such
object once; an object stream whose decoding read through it is not kept at all, but counted as decoded, so that the walk
does not decode it again. So a caller's own reads are never cut, and the budget starts again with the next walk; a
rebuild's expansion of the object streams, which makes the index the reader keeps, is not counted. A valid file reaches
it, and the validator takes what it leaves unread for the reader's limit, judging nothing of it. A guard on every read,
for the document's life, was weighed and set aside: it would cut a caller reading a valid file's pages in turn, which the
caller can order otherwise, as the one rule that reads every object of the index now does, stream by stream.

Amended on 2026-11-23, by the maintainer, while [#163](https://github.com/AdCodicem/AdCodicem.Pdf/issues/163) was fixed: the
parser's nesting bound (128) and the chain of nested loads (64) had been filed as bounds only an invalid file reaches,
on reasons that were empirical. ISO 32000-1 nests arrays to any depth (7.3.6) — a chain of actions through `/Next` written
in one object is as deep as it is long — and lets an object stream take its `/Filter` or `/DecodeParms` from an object
another object stream holds (7.5.7), to any depth. Both become guards, `MaxNestingDepth` (128, `limit.nesting-depth`) and
`MaxNestedLoads` (64, `limit.nested-loads`), their defaults unchanged, honored by `ThrowOnLimit`. The thread's stack stays
the backstop beside each — the parser and the reader are recursive — and is reported under the guard's code, its message
naming a larger stack as the remedy; `syntax.depth-exceeded` is withdrawn. The nested-loads cut depends on where a read
is made from, not on the file: what a read keeps — an object, an object stream's contents, an exception — now carries how
deep its loads went, and serves a later read only where that read would go as deep without the bound, or, for one the
bound cut, from as many loads in flight, so that an object reads the same whatever was read before it; what the stack cut
is kept for the read in progress alone. Keeping nothing a cut reached was measured and set aside: a chain of 64 object
streams each taking its filter from the next was decoded again past every level, without end in practice.

Amended on 2026-11-24, by the maintainer, while [#289](https://github.com/AdCodicem/AdCodicem.Pdf/issues/289) and
[#295](https://github.com/AdCodicem/AdCodicem.Pdf/issues/295) were fixed: `MaxTrailerLength` bounds, apart from the
section, the white space and comments an offset that names a cross-reference section falls short by, and the bound's
reach over them was never classified. Strictly, no valid file reaches it: ISO 32000-1 has `startxref`, `/Prev` and
`/XRefStm` name the section's first byte. But the reader reads white space before a section as a Warning (ADR 45),
nothing bounding how much, and `limit.trailer` already names the property that reads past it: the reach stays the
guard's. Where that white space runs on past the bound, or where the bound cuts the header of the object at the offset —
its number, its generation or its `obj` keyword running on past it, white space or a comment between its tokens, all
valid syntax —, the section is a section the guard cut: where it was named, not looked for near there, the chain ending
at it; for the one `startxref` names, the index is rebuilt, as for any guard that leaves the chain no section. The
search near the offset read up to 512 bytes past it, past a bound set below that, and could take a section other than
the one past the run — an older table, which then served an object the update's stream frees —, the file charged with
Errors. A first token no header starts with — a `-`, a letter — is no section's whatever the cut hid, and stays the
file's. A header read whole whose value the bound cuts before the value says what it is — white space or a comment after
`obj` running on past it, or a `<` its last byte, which may open the dictionary — is the guard's section too, while a
value that starts before the cut with anything else, a number, `null`, a string, is no cross-reference stream however it
goes on, and stays the file's. A classic table's windows open at its `xref` keyword, as a stream's at its header, so
that `MaxXRefSectionLength` bounds its rows alone and the white space is read once. The white space before an object's
header, where its row names it, still counts toward `MaxObjectLength`, and a load reads near the row past the guard:
left as debt ([#428](https://github.com/AdCodicem/AdCodicem.Pdf/issues/428)), since bounding it apart is a change to the
reader's most read path.

The same day, while [#328](https://github.com/AdCodicem/AdCodicem.Pdf/issues/328) and
[#329](https://github.com/AdCodicem/AdCodicem.Pdf/issues/329) were fixed, the maintainer closed what the 2026-11-04
amendment left unjudged. A `/Prev` written whole before a guard's cut — in a classic trailer `MaxTrailerLength` cut, its
table's rows read whole or found past rows a guard stopped, or in the dictionary of a cross-reference stream a guard's cut
of a value its rows are read with keeps from being read, a fault beside that cut whose data may hold rows among them — is
judged as one past a rows guard is, and never read: the chain still ends at the section, since an older trailer would
stand for keys the cut hid, or older rows for those the value hides. A key written whole before a trailer's cut is the
trailer's, though a repeat of it past the cut would win in a whole read: only an invalid file repeats a key, and the
parser keeps no value the cut parts ([#279](https://github.com/AdCodicem/AdCodicem.Pdf/issues/279)). The `/XRefStm`
such a trailer gives over rows read whole stays followed: its stream holds the table's own revision's rows, and no row of
the table was lost for an older one to stand for; past rows a guard stopped, both links are judged and neither is
followed, as before the trailer was cut. An `/XRefStm` that names what the `/Prev` names under a trailer cut is reported
as naming the section past the guard that cut the trailer. A reference to a row the stream may hold but the guard left
unread stays the guard's, the rows counted through what the stream's `/Index` or `/Size` declares, up to what its data
can hold, where no guard cut them, any number otherwise.

The same day, while [#292](https://github.com/AdCodicem/AdCodicem.Pdf/issues/292) was fixed, the maintainer settled where
the chain's end at a guard meets the load rule ADR 50 keeps for a header past `MaxObjectLength`. The loads the chain makes
of its own values once it is read — the object a `/Prev` or a stream's values led to through a guard's cut, a
cross-reference stream's `/Length` — stop where the guard cuts the object's header, as where it cuts its value: the guard
reported, or thrown, nothing looked for near the entry, the index not rebuilt, so that the chain's end costs no rebuild
and no `xref.rebuilt` on a valid file. A later read of that object, or a validation, follows the load rule, which may
look near the entry and rebuild. And while [#324](https://github.com/AdCodicem/AdCodicem.Pdf/issues/324) was fixed, a
classic table a `/Prev` names whose fault lies among its rows came to be read on past that fault to its trailer: a
`MaxTrailerLength` cut of that trailer leaves the table's fault, met before the cut, the file's — the table reported as
one that cannot be read, `xref.section-malformed` beside `xref.checked-in-part` —, and its `/Prev`, written whole before
the cut, is judged, never followed. Its fault, not a guard, stopped its rows: that `/Prev` is judged as a whole read of
the table judges it, a reference not taken for one to a row a guard left unread, and its `/XRefStm`, which a whole read
of such a table does not follow, is not judged.

Amended on 2026-11-25, by the maintainer, while [#290](https://github.com/AdCodicem/AdCodicem.Pdf/issues/290) was fixed:
the 2026-10-24 amendment reads a cross-reference stream whose data holds no row, beside a value a guard cut, as read
whole, lacking rows, the chain going on. A hidden value of the wrong type or range — a string where `/Index` wants a
count, a count that overflows the object numbers — makes the whole read find the section unreadable rather than
incomplete. The verdict stands: data that holds no row lacks the rows whatever the value holds, the section is the
file's fault either way, and no row is read either way, so the older sections are read and judged as a whole read
reads them, and the guard is reached once the chain is read. Yielding the section to the guard wherever a value could
make it unreadable — every value a guard cut can — would end the chain there and leave the older sections unread.

## Context

Invariant 4 treats everything read from a file as hostile: no allocation, loop or recursion is sized by a
value the file chose without a checked bound. The reader has a dozen such bounds. They are of two kinds, and
until now nothing told them apart.

**Bounds only an invalid or hostile file reaches.** A chain of objects loaded while loading another is
bounded at 64 (T29): an indirect `/Length` must lead to an integer, and the filters of a stream and the parameters they
read resolve before any of its data is decoded, so that no level holds decoded data while the next loads (the second
2026-10-14 amendment). Containers
nest at most 128 deep, and references to references are followed 32 times. These bounds never refuse
a document the specification allows in practice, and a caller has nothing to gain from lifting them. (The
first version of this record listed two more here, on the objects a rebuild indexes and the entries a subsection
claims; neither bounded what it was said to, and the 2026-10-14 amendment made them one guard.)

**Bounds a valid file can exceed**, because ISO 32000 sets no limit where the reader set one:

| Bound | Value | What a valid file can do |
|---|---|---|
| A stream's decoded length, its filters before the last sharing one bound for what they pass along together, a `/Crypt` filter neither charged nor the last | 256 MB | The USGS topographic map in the remote corpus holds one image, 9,600 × 11,410 RGB, that decodes to 313 MB; qpdf reads it whole. A chain of filters whose outputs together pass the bound is valid too, and qpdf reads three thousand of them |
| A stream's encoded data, read at once | 256 MB | ISO 32000 sets no limit on a stream's length, and decoding hands back data no filter decodes as it is: the USGS map's image, stored without a filter, would be 313 MB of data |
| An object's length, streams' data aside | 16 MB | A direct array or dictionary of that size: a page-tree node with a million kids |
| A classic cross-reference section | 64 MB | About 3.3 million entries; the informative annex C of ISO 32000-1 gives Acrobat's own limit as 8,388,607 objects |
| The number of cross-reference sections | 1,024 | A document saved incrementally more than a thousand times |
| A trailer | 64 KB | A trailer is a few hundred bytes in practice; nothing forbids more. A cross-reference stream's dictionary is its trailer: its `/Index` holds two numbers per subsection, and a document updated in many scattered places has thousands. The bound applies apart to the white space and comments an offset that names a section falls short by, which the reader reads as a Warning (amended on 2026-11-24, [#289](https://github.com/AdCodicem/AdCodicem.Pdf/issues/289)) |
| The entries of the index | 8,388,607 | The informative annex C of ISO 32000-1 gives Acrobat's limit as 8,388,607 objects, and PDF/A-1 to 3 make it normative; PDF 2.0 sets none, and PDFium raised its own past it for documents of about six million. Each entry the index holds costs 40 bytes numbered in order, up to about 80 when a rebuild grew it and about 96 for numbers spread far apart, so the default holds 335 MB of index or more (amended on 2026-10-28, [#155](https://github.com/AdCodicem/AdCodicem.Pdf/issues/155)) |
| The filters a stream's data passes through | 64 | ISO 32000 sets no limit on how many filters a `/Filter` array names; real streams name one to three, `/Crypt` among them, and qpdf reads three thousand |
| A rebuild's reads past streams' declared lengths, to confirm them | 4 times the file | A valid file's own streams take about twice the file; but the data of a stream whose length the scan cannot confirm, which it reads as objects, may hold an attached file's bytes that spell streams, whose lengths may all end in one run of white space that each read past them crosses (amended on 2026-11-17, [#361](https://github.com/AdCodicem/AdCodicem.Pdf/issues/361)) |
| What one of the library's walks decodes again of the object streams the reader let go | 4 times what its first decodings weighed | ISO 32000 sets no limit on an object stream's decoded length, nor on how a writer spreads objects among streams: a page tree whose pages alternate between two streams that each decode past half the reader's 32 MB budget has a walk of it decode a whole stream for each page (amended on 2026-11-20, [#181](https://github.com/AdCodicem/AdCodicem.Pdf/issues/181)) |

None of these was an option. Until T21, T23 and T31 some were not even reported, or were reported as damage
in the file: the map's image came out as "a Flate stream was truncated".

The trailer's bound was not even one for a cross-reference stream: its dictionary was parsed through one
window of 64 KB that never grew, so a dictionary longer than that left the index to a rebuild, with no word
of why.

One ceiling is the implementation's rather than a choice: a decoded stream is returned as
`ReadOnlyMemory<byte>`, which holds at most `Array.MaxLength` bytes, about 2 GB. Past it only a decode that
hands its output out a piece at a time can read the stream (T28).

## Decision

We will read every PDF that is valid under ISO 32000, and treat each bound a valid file can exceed as a
guard: active by default, reported when it is reached, and lifted by an option.

- **The options.** `PdfReaderOptions.Limits` is a `PdfReaderLimits`, an immutable record with the ten bounds
  of the table: `MaxDecodedStreamLength`, `MaxStreamLength`, `MaxObjectLength`, `MaxXRefSectionLength`, `MaxXRefSectionCount`,
  `MaxTrailerLength`, `MaxIndexEntries`, `MaxFilterChainLength`, `MaxRebuildReadPasses` and `MaxObjectStreamDecodePasses`.
  `PdfReaderLimits.Default` keeps the values above;
  `PdfReaderLimits.Unbounded` takes the implementation's maximums — `Array.MaxLength` for a length,
  `int.MaxValue` for a count. A value of zero or less is refused; a value above the implementation's maximum is
  taken as that maximum.
- **The defaults stay on.** An application that opens files it did not produce is protected without
  configuring anything; one that reads a sound but exceptional file asks for it, by raising one bound or by
  choosing `Unbounded`.
- **Reaching a guard** keeps what fits within it, as the reader does today, and reports it as a warning
  under a code of its own — `limit.decoded-stream`, `limit.stream-length`, `limit.object`, `limit.xref-section-length`,
  `limit.xref-section-count`, `limit.trailer`, `limit.index-entries`, `limit.filter-chain`, `limit.rebuild-reads` — whose message names the property that
  lifts it. What the parser met at the cut is the reader's, not the file's, and is dropped in favor of the guard; so is a
  value that leads to an object a guard cut or left out of the index (amended on 2026-10-23). An object, a section or a
  trailer is reported once, however often it is read again; a stream, by the first of its filters that stopped there,
  each time it is decoded past the bound into diagnostics a caller supplies, and once in its document's own, however
  often it is decoded (amended on 2026-10-15); a stream's data `MaxStreamLength` cut, with each decoding into
  diagnostics a caller supplies, and once in its document's own, as the reader reads it (amended on 2026-10-23); a full
  index, once a document. The filters after one a guard stopped, and those of data `MaxStreamLength` cut, are given data
  the reader cut: that it runs out — a lost tail, a missing checksum, a digit without its pair, a row the predictor
  cannot finish — is not reported, and a fault in what was read is. The code `filter.limit-exceeded`, added by T31 and
  never released, becomes `limit.decoded-stream`.
- **What a guard leaves out of the index is not damage** (amended on 2026-10-14). The objects only a section past
  `MaxXRefSectionCount`, rows past `MaxXRefSectionLength`, `MaxStreamLength` or `MaxDecodedStreamLength`, or numbers past
  `MaxIndexEntries` index read as null, and the validator treats them as unknown rather than missing, until the
  property is raised: the index is not rebuilt for them, which turned a sound file into an `xref.rebuilt` error
  (ADR 45) and could lose what the chain had read. A classic table cut by its length keeps its trailer. The chain ends
  at a cut — a table's, a stream's rows, a section or a trailer cut wherever the cut falls, a cross-reference stream
  whose `/W`, `/Index`, `/Filter` or `/DecodeParms`, or `/Size` with no `/Index`, leads to an object `MaxObjectLength`
  cut, or whose `/Length` does where only it could say how far the data runs, a `/Prev` or an `/XRefStm` the reader
  follows that does (amended on 2026-10-23 and 2026-10-24) —, since an older section, or its trailer, would stand for
  what the cut left out; where a guard stopped its rows — `MaxXRefSectionLength` or `MaxDecodedStreamLength` cut them,
  or `MaxIndexEntries` filled at them, a hybrid table's stream among them —, what the section it ends at names is judged
  all the same, as far as the guard lets it be, without reading past it (amended on 2026-11-04), and so is a `/Prev`
  written whole before a trailer `MaxTrailerLength` cut, or in a cross-reference stream a value's cut ends the chain at
  (amended on 2026-11-24, [#328](https://github.com/AdCodicem/AdCodicem.Pdf/issues/328),
  [#329](https://github.com/AdCodicem/AdCodicem.Pdf/issues/329)); and a section a guard cut is where it was named, not
  looked for near there — white space past `MaxTrailerLength` before it, or a header that bound cuts, among such cuts
  (amended on 2026-11-24). One found near where it is named, whose values a guard keeps
  from being read, or that declared itself a section — the `xref` keyword, `/Type /XRef` — before a guard cut it, is the
  guard's there too (amended on 2026-10-24). A rebuild that damage forces on such a chain keeps the entries
  the chain read; one the chain cannot do without — a guard that leaves it no section — runs as before.
- **Or it throws.** `PdfReaderOptions.ThrowOnLimit`, false by default, turns every guard reached into a
  `PdfLimitExceededException`, a `PdfException` carrying the code and the name of the property: from
  `PdfDocument.Open` when it is reached while opening, from `GetObject`, `GetRawBytes` or `Decode` when it is reached later.
  A rebuild of the index that reaches a guard finishes first and throws after, since a rebuild is never run
  twice; each later lookup of an object a full index left out throws again. `Open` releases a source it was given
  to own when it throws.
- **A stream decodes under its document's settings.** A stream read from a document decodes under that
  document's limits and `ThrowOnLimit`, however long after opening; a stream built in memory decodes under
  the defaults.
- **One record for every hostile read.** A bound on bytes the core parses on a caller's behalf joins
  `PdfReaderLimits` under the same rules, whatever the bytes arrive in: M08 adds `MaxFontLength` (64 MB,
  `limit.font-length`) and `MaxCharstringOperations` (65,536, `limit.charstring-operations`), which govern a
  font program read from a PDF under its document's limits and one a caller registers under the limits the
  registry is given. A record of its own (`PdfFontLimits`) is rejected: a font inside a PDF would answer to
  two, and a caller would raise two records to read one file.
- **Bounds only an invalid or hostile file reaches stay internal constants**, each with the reason it cannot
  refuse a valid document. A new bound is classified when it is added: if a valid file can reach it, it
  becomes an option, a code and a test — the exception made on 2026-11-15 for the rebuild's scan's share of
  `EndStreamSearchPasses` closed on 2026-11-17 ([#361](https://github.com/AdCodicem/AdCodicem.Pdf/issues/361)).
- **The corpus proves it.** A corpus entry may give the limits it is opened with, in `readerLimits`; the USGS
  map is opened with a decoded-stream bound of 512 MB and must read whole. Every other document is tested
  with the defaults.
- **Past the implementation's ceiling the rule is not yet met.** A stream that decodes past about 2 GB needs
  a decode that yields its output a piece at a time; that is T28, for M23, and until then
  `limit.decoded-stream` reports it even under `Unbounded`. A stream's encoded data, read in one piece, has the
  same ceiling: `limit.stream-length` reports data past it even under `Unbounded` (amended on 2026-10-23).

## Consequences

- A caller holding a valid but exceptional file can read it, and learns from the report which property to
  raise; a caller holding untrusted input keeps every protection without knowing it has one.
- Every bound now needs a decision, recorded where it is declared: an option with its code and its test, or
  an internal constant with the reason no valid file reaches it — the exception made on 2026-11-15 closed on 2026-11-17
  ([#361](https://github.com/AdCodicem/AdCodicem.Pdf/issues/361)). The option surface grows with the reader.
- The defaults are judgments about what is exceptional, and one is already known to be crossed by a real
  document: a large-format scan decodes past 256 MB. They can be revised without breaking a caller, who may
  always set them.
- With `ThrowOnLimit`, any lazy operation — resolving an object, reading or decoding a stream — may throw after `Open`
  returned; a caller who sets it must be ready for that wherever the document is used.
- `Unbounded` is for documents the application trusts. Raised guards let a file make the reader hold what
  it asks for, up to the new bounds; the cache of decoded object streams keeps within `ObjectStreamBudget` but always
  holds the stream decoded last, so raising `MaxDecodedStreamLength` raises what that one can hold.
- A classic trailer that ends within the window its table was read through is read whole whatever its
  length: the guard bounds how far the reader follows one past that window, which is where the cost is.
- **Rejected** — reading everything by default and making the guards opt-in: every service that accepts
  uploaded PDFs would be exposed to decompression bombs until someone read this record.
- **Rejected** — flat properties on `PdfReaderOptions`: six related bounds and their presets belong
  together, and the options record would grow with every bound.
- **Rejected** — one code for every guard: a caller could not tell, without parsing a message, which bound to
  raise before trying again.
- **Rejected** — exceptions only: a document whose one huge image is cut would become a document that cannot
  be opened at all.
- **Amended on acceptance**: invariants 4 and 5 of `CLAUDE.md`, and invariant 12 added; `ARCHITECTURE.md`,
  `docs/architecture.md` and `SECURITY.md` on bounds; `docs/corpus.md`, `docs/corpus-contributions.md` and
  `tests/corpus/README.md` for `readerLimits`; the site's *Diagnostics* and *Lazy reading* pages, and a new
  page, *Reader limits*; the M02 stream rule and M23's deliverables.
- **What would reopen it**: a streaming decode (M23), after which `MaxDecodedStreamLength` would bound memory
  held at once rather than the length of a stream; or evidence that most callers read trusted input, which
  would argue for `Unbounded` as the default.
