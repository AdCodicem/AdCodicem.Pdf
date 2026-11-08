---
title: Lazy reading
sidebar_position: 1
description: What opening a document does and deliberately does not do, what that costs, and what it measured.
---

# Lazy reading

Opening a document builds an index, finds the catalog, and stops there.

## What opening actually does

1. Reads the tail of the file to find `startxref`.
2. Follows the chain of cross-reference sections, each of which may be a classic table, a cross-reference
   stream, or a hybrid of both, and may point at object streams.
3. Builds a map from object number to a location: either a byte offset, or a position inside an object
   stream. Nothing is loaded meanwhile: a value a section refers to — a cross-reference stream's `/W` or
   `/Length` written as a reference, a `/Prev` — is read only where a section already read places it, so that
   the map the chain gives is the file's own. A `/Length` no section read before places, or that leads through
   references to such an object, is read once the chain is, and checked against the data the chain took up to the
   first `endstream` after the data's start, searched for in the file up to the next object the sections read so far
   place, or its end — unless `MaxStreamLength` cut that data, which is the guard's; one that leads to an object `MaxObjectLength` cuts waits for the chain the same way, and is the
   guard's: the guard is reached once the chain is read, and the length is not checked. Either way, when the section's
   own row for that `/Length` — the row the reading of its rows keeps — lies among the rows the data holds, those
   rows spelled `endstream`: the object is read where the row places it, without being kept, and the data is taken
   again to the length it gives, or to the integer the guard's cut left of it, when an `endstream` confirms it.
4. Resolves the trailer's `/Root`, which reads the document catalog, and decodes the object stream that holds
   it when one does. When `/Root` leads to no catalog, it loads the objects the map holds, one after the
   other, until one is a catalog (`trailer.root-recovered`), and rebuilds the map when none is.

On a file whose index and `/Root` are sound, that is all: no page is read, and no stream's data but that of the
cross-reference streams and of the object stream holding the catalog. The search for a lost catalog parses
objects, and a rebuild scans the whole file, but neither decodes a page's content, an image or a font.

Indexing allocates 40 bytes per object, whatever the objects weigh — a scanned page holding a 3 MB image
costs the same to index as an empty one. The map holds 36 bytes an entry, and 4 a slot that finds an object number's
entry while numbers run in order; a number past twice the entries held or made room for, and 1,024 more, is found
through a hash table instead, 20 to about 42 bytes more, by where its count falls against the table's growth steps, its
hashes seeded so that a file cannot choose numbers that make each lookup slow. The map is sized before it is filled: each cross-reference section makes room for the rows it holds before it reads them, and
for no more objects than the newest section's `/Size` numbers, since an older section's rows for numbers a newer one
gave add nothing; a `/Size` the sections number past stands for one past the highest number they gave, until an older
section numbers past the newer ones, and bounds nothing from then on. A count the file declares is its claim, so room is made for no more rows than a stream's data holds,
or than a classic table's bytes hold at 20 bytes a row up to the first byte no row holds, which the table is read in a
window large enough to reach. A chain of many like sections makes room section after section,
doubling the map at least each time it must grow, and for the whole `/Size` once a doubling comes within a doubling of
it. A table's rows are counted by their lines, so that rows written 19 bytes long, their end of line a single byte,
take no more room than rows of 20. A map left to grow — by a rebuild, or rows written shorter still — doubles as it
grows, and allocates 80 to 160 bytes an object numbered in order, by where the count falls against its steps, and about
110 to 220 for numbers spread far apart.
A cross-reference stream adds the decoding of its rows: 7 bytes an object when they are written as they are, up to
about 35 when Flate stores them without compressing, compressed rows in between. Opening also decodes the object
stream that holds the catalog, when one does, and that costs what the stream holds: a producer that packs every object
into one stream makes opening pay their decoded weight: 864 bytes an object on one such file, damaged, of 65,564. An open
document keeps the map, 40 bytes an object, about 80 at most when it grew: on the corpus's thousand-page journal, 40.
Room made for the whole `/Size` holds 40 bytes for each object it numbers, so a `/Size` that numbers more objects than
the sections give, up to four times as many, keeps up to 160 bytes an object they give; a table of one-row subsections,
whose headers make room as rows do, keeps about 1.5 times these figures. Numbers spread far apart keep about 60 to 85
bytes an object in subsections of ten rows or more, up to about 96 in subsections of two, and about 80 to 110 in a table
of one-row subsections, the more the wider the numbers, and up to 4 bytes more where the slots stretch to twice the room.
When the reader relocates an object or rebuilds the map once the chain is read, it keeps a copy of the map the chain
gave as well, so that validation still judges the file's own.

The map holds at most 8,388,607 object numbers by default, the most ISO 32000-1's Annex C advises a document to have,
and `PdfReaderLimits.MaxIndexEntries` raises it ([Reader limits](reader-limits.md)): at the default, the map alone
can take 335 MB.

An object the map does not hold is one the file does not define, and a reference to it reads as null, as the
specification says — or one a limit left out of the map, which reads as null too, until the limit is raised: the
sections past `MaxXRefSectionCount`, the rows past `MaxXRefSectionLength` or past what `MaxStreamLength` reads or
`MaxDecodedStreamLength` decodes of a cross-reference stream, a cross-reference stream whose `/W`, `/Index`, `/Filter`
or `/DecodeParms`, or `/Size` with no `/Index`, leads to an object `MaxObjectLength` cuts and the sections older than it — so too one whose
`/Length` leads to such an object, when no `endstream` ends its data before the next object or the end of the file, or
the rows past an `endstream` its rows spell, which only that length could tell from the end of its data —, the sections
a `/Prev` or an `/XRefStm` the reader follows that leads to such an object names — the older ones, or the cross-reference stream of a hybrid
file's revision, with the sections older than it, the chain ending with it —, the numbers past `MaxIndexEntries`. An
object the guard cut after a whole reference it holds hides none of them: the chain reads through it. That is no
damage, and the map is not rebuilt for it. What a section whose rows a limit stopped names is judged all the same, as
far as the limit lets it be, and nothing past the limit is read: a `/Prev` or an `/XRefStm` there that can name no
section whatever the limit hid — no offset, an offset outside the file, the section itself, another the chain reached —
is the file's damage, reported as in the file read whole, and leaves the map to be rebuilt as that damage does
([#264](https://github.com/AdCodicem/AdCodicem.Pdf/issues/264)). The `/Prev` of a cross-reference stream at which a
limit's cut of a value its rows are read with ends the chain, and a `/Prev` in a trailer a limit cut, are not judged
yet ([#329](https://github.com/AdCodicem/AdCodicem.Pdf/issues/329),
[#328](https://github.com/AdCodicem/AdCodicem.Pdf/issues/328)).

A row of a cross-reference stream of a reserved type — a type past 2, or a type field wider than 8 bytes whose leading
bytes are not all zero, which ISO 32000-1 leaves for later versions to define and reads as a reference to the null
object — is read so: the map holds its number as a free one's, no older section's row stands for its object, which
reads as null, and nothing is rebuilt for it (`xref.entry-reserved`,
[#221](https://github.com/AdCodicem/AdCodicem.Pdf/issues/221)) — until the map is rebuilt for another fault, at
any of the moments below: at opening, by a read after it, or as a validation settles it. The rebuild reads the object
as the scan finds it, and `xref.entry-type-reserved` then says the reader read the row so only until then. A row of a
reserved type whose number was refused before it was read — by a newer section, below, or by a row of the stream's own
for the same number — is not read so: it stands for nothing, as every row whose number was refused does, its object is
looked for by rebuilding the map, and both reports say so; a stream a table's `/XRefStm` read and that table's `/Prev`
reads again is judged as its first reading read it, what it refused itself no newer section's refusal
([#156](https://github.com/AdCodicem/AdCodicem.Pdf/issues/156)). A classic table that holds no subsection, its `trailer`
keyword right after its `xref` keyword, reads as one whose one subsection is `0 0`: it indexes nothing, and nothing is
rebuilt for it (`xref.section-empty`, [#222](https://github.com/AdCodicem/AdCodicem.Pdf/issues/222)) — unless no
section of the chain indexes anything, below.

The map is rebuilt by scanning the whole file, once at most, and never while the chain is read:

- at opening, when `startxref` cannot be found or read, when the section it names cannot be read, when the
  chain indexes nothing, or when `/Root` leads to no catalog and no object of the map is one. A section `startxref`
  names that was read only in part, below, is kept: the map is rebuilt for it only as the next case says, when an
  object it lost is asked for;
- when an object the map lacks is asked for, by opening or after it, while the map may have lost entries: a
  cross-reference section the chain names cannot be found, or a `/Prev` or `/XRefStm` is not an offset — one written as
  a reference no section read before it places included — (`xref.section-missing`); one is found and cannot be read
  (`xref.section-unreadable`), a value its rows need written as such a reference, or a `/Length` so written, or that
  leads through references to an object no section read before places, whose data no `endstream` ends before the next
  object or the end of the file, or data a stream's filters decode nothing of — left at a filter the library does not
  know, at an element of `/Filter` that names none or at a `/Filter` that is neither a name nor an array, or by Flate
  data corrupt before its first byte decodes —, which holds no row whatever its bytes would read as
  ([#221](https://github.com/AdCodicem/AdCodicem.Pdf/issues/221)), among the causes; a section is read only in part, and
  kept (`xref.section-unreadable` too, its message saying it `was read only in part`): a row of a classic table cannot
  be read, which ends its subsection, a row gives an object in use what no entry can hold — a generation past 65,535, an
  offset of 2⁶³ or more, an object stream that is no object number, an index past 2,147,483,647, a field wider than 8
  bytes whose leading bytes are not all zero —, which refuses that row alone, a cross-reference stream whose data no
  guard cut — decoded to its end, or as far as a filter decoded it before a fault — holds fewer rows than it declares,
  or the end of the file cuts a table before its trailer, or within its `trailer` keyword, once it read a row
  ([#207](https://github.com/AdCodicem/AdCodicem.Pdf/issues/207),
  [#210](https://github.com/AdCodicem/AdCodicem.Pdf/issues/210)); a `/Prev` loops back on the chain
  (`xref.chain-cycle`); or a hybrid file's table names through `/XRefStm` no cross-reference stream of its revision — a
  classic table, one that cannot be read among them, a section the chain reads through `/Prev` or `startxref`, or what
  the table's own `/Prev` reaches too, whatever offset each writes: the same, one in the white space before that
  section, or one a few bytes off where the search near it comes to that section and the `/Prev` keeps its place there,
  as its own reading does, from the object's first token — the `xref` keyword, a stream of `/Type /XRef`, or an object
  that gives a `/W` and reads there as a section, whatever its type, not a font, a string, or an object that gives a
  `/W` and does not read as one; a `/Prev` that names what the chain has reached, which ends the chain in a loop, keeps
  no place, nor does one that names white space running on past `MaxTrailerLength` —, or what the `/XRefStm` read in the
  stream's place where the `/Prev`'s own search comes to it, which that `/Prev` then reads whole: a classic table where
  the search comes to it, or a stream where the search takes it, in the order below — (`xref.hybrid-stream-lost`,
  [#156](https://github.com/AdCodicem/AdCodicem.Pdf/issues/156)); the report on what the `/Prev` reaches too is made
  before that `/Prev` is read, and says only that the chain looks for it there, or that the offset lies outside the
  file, and a stream the `/Prev` read again is reported once it has, its own `/Prev`, where it names no older section,
  reported as the chain finds it. Where the search near the offset the `/XRefStm` names stopped at what the `/Prev`
  names only because one of the reader's limits cut it before it said what it is, or cut a value its rows are read with,
  the naming is the limit's: nothing is reported of it, and the map is not rebuilt for it
  ([#264](https://github.com/AdCodicem/AdCodicem.Pdf/issues/264)). A stream an older table names again, as a writer that
  copies `/XRefStm` into the next trailer leaves it, is no such fault, and is read once, whatever offset in the white
  space before its header each table writes;
- when an object is asked for, by opening or after it, that is neither where the map says nor within 512 bytes
  of it;
- before a validation's first rule, when one of the two cases above could still happen — the map may have lost
  entries, or one of them places its object neither where it says nor near —: the validator has the map rebuilt
  then, as that read would, rather than let one of its rules' reads rebuild it halfway through. It finds out by
  reading the header each entry written directly in the file places, or looking for it nearby as a read would, and
  nothing of any object's body.

A section read only in part does not end the chain: the chain goes on from its trailer, where it has one. Nor does a
cross-reference stream whose dictionary gives `/Type /XRef` and was read whole, though its rows cannot be — data its
filters decode nothing of holds none ([#221](https://github.com/AdCodicem/AdCodicem.Pdf/issues/221)) —: it hands on the
`/Prev` that dictionary gives, so that the older sections are read and judged rather than lost with it
([#208](https://github.com/AdCodicem/AdCodicem.Pdf/issues/208)), its report saying so where that `/Prev` names an offset
or leads to what a limit hides — one that is no offset is reported missing after it, and the chain ends there —; but
where `startxref` names it, the map is rebuilt at opening, as for any first section that cannot be read. Either way, an
object such a section numbered and lost — the rest of each subsection a table's fault ended, a refused row's, every
number a stream declares, through its `/Index` or below its `/Size` — is not taken from an older section's row, which
would serve what an update replaced: it is looked for by rebuilding the map, which keeps the last definition the file
writes. So is an object that a hybrid file's stream of `/Type /XRef`, its dictionary read whole and its rows not,
declares; and one that a classic table an `/XRefStm` names lost, when it cannot be read or the end of the file cuts it,
the chain going on past it through the table that names it: the rest of the subsection its fault ended, and, where what
follows the fault cannot be known — a subsection header that is no header, a stray token, the end of the file —, every
number its rows do not give ([#156](https://github.com/AdCodicem/AdCodicem.Pdf/issues/156)). Where what was lost cannot
be told — a stream whose numbering cannot be read, or is written with references, which ISO 32000-1 makes direct, or
lost numbers that make more than 4,096 ranges, counted once those of the sections read so far that meet are joined, a
table's own each joined as it is lost to the last one lost where the two meet, one that meets only an earlier one kept
apart —, no older section's row stands for any number that no section read before it gives, and the report on the
section that refused them says so, a section read only in part included, which is reported once it refused what it lost
— but for a table `startxref` or a `/Prev` names whose rest cannot be known, at which the chain ends: its report says,
however many ranges it lost, that what only the rest of the table, or the sections its trailer would name, index is
looked for, the same objects, no section past it being read, unless a newer section refused every number before the
chain came to it, which its report says instead. A section the chain reads after such a refusal keeps none of its rows
whose numbers were refused, and none at all where every number was; where it was read only in part, or cannot be read,
its report says which of its rows stand, rather than that they were kept — not those it refused itself, though only its
first fault is named, and, of a stream that holds one row and refused it, that the row was refused. A section that gives
one number twice and refused one of the two rows is not said to have had the other refused by a newer section: the
refusal is its own, and its object is left to the rebuild only where no row read before the refused one indexes it. Any
other section a `/Prev` names that cannot be read ends the chain there.

An object, or a section a `/Prev` or a `/XRefStm` names, that lies a few bytes from where it is said to be is found
nearby (`xref.offset-adjusted`), and the map is not rebuilt for it: within 512 bytes either side, however near the
file's start that falls ([#197](https://github.com/AdCodicem/AdCodicem.Pdf/issues/197)), comments between its header's
tokens included, as a load reads them ([#261](https://github.com/AdCodicem/AdCodicem.Pdf/issues/261)). A section an
offset names short of its first token, white space and comments between, is read where it is named — a cross-reference
stream from its object's header, a classic table through a window that opens at the offset, so that the white space
before its `xref` keyword counts toward `MaxXRefSectionLength` as well
([#289](https://github.com/AdCodicem/AdCodicem.Pdf/issues/289)) —, as long as they run for no more than
`MaxTrailerLength`: white space and comments that run on past it reach that guard, the section is not read from that
offset, and it is looked for nearby as one that is not there, where a search may find it past them, the map rebuilt for
the one `startxref` names ([Reader limits](reader-limits.md)). A section is never found where a section the chain
reached starts — one it read, or one there that it could not read though it declares itself a section —, which is not
the one looked for ([#188](https://github.com/AdCodicem/AdCodicem.Pdf/issues/188)), but for what a table's `/XRefStm`
read in the stream's place, which that table's `/Prev` takes, reading it whole. Where it read a stream, or names again
one a newer table's `/XRefStm` read, which is never taken in its place
([#330](https://github.com/AdCodicem/AdCodicem.Pdf/issues/330),
[#332](https://github.com/AdCodicem/AdCodicem.Pdf/issues/332)), a stream near the offset may be the table's own, which
nothing names, but only one that starts no sooner than that stream — told by its data, whatever byte of its header the
`/XRefStm` names, so that where it names a place inside the stream's object number, the header that starts before that
place is the stream itself, and where it names the `+` the number is written with, so is the header found at the digit
past it —: under updates appended to the file, a revision's own stream lies past every older revision's sections, and a
stream before it — a first revision's, which gives no `/Prev`, or an older revision's — is tried where it comes
nearest, whatever its `/Prev`. Of the streams that start there or past it, one whose dictionary, read whole, gives no
`/Prev`, or the one the table's names — read as the chain reads it, a real with no fractional part as its integer, a
reference through what a section already read places there, one that gives no offset in the file as another offset —,
is tried once every other place was, nearest first, but never the stream the `/XRefStm` read where it gives none; a
dictionary one of the reader's limits cut, whose `/Prev` the cut may hide, is tried where it comes nearest, the section
found moved and the limit's where it declared itself one before the cut. Such a stream passed over for another section,
which the chain does not reach, at its header or inside its object number, is reported once the chain is read
(`xref.stream-passed-over`), the map left as it is: if it is the table's own, the map the chain read lacks what only it
indexes, found only by a rebuild for another cause. It is reported only where it declares itself a cross-reference
stream — its dictionary gives `/Type /XRef`, and its header is a token a load reads from the start of its line —, as a
place a fault of the file keeps from being read is taken only where it does: not an object that only gives a `/W`, nor
a header a comment or a string on its line holds, whether it reads or not. A stream the chain reaches — a section names
it, or a later search finds it moved —, at its header or inside its object number, from which a reading reads the same
stream, is reported as the chain finds it, read or not. Where one of the reader's limits left part of the chain unread
— the number of its sections, a section it cut before the section said what it is, rows, a trailer, an object a value
the rows or a link are read with leads to, or the map it filled —, which may hide the link that names the stream, or
where white space and comments at an offset the chain names run past `MaxTrailerLength`, which turns the reading of
the section there into the search near the offset ([#289](https://github.com/AdCodicem/AdCodicem.Pdf/issues/289)), the
limit's report stands for what it left unread, or for what that search passed over or did not find, and nothing is said
of the stream; a cut that leaves nothing unread — data past a stream's last row, an object past a whole reference it
holds — leaves the report made. Four cases fall short: an older revision's stream before the one the `/XRefStm` read, nearer than its own table,
is taken, and where it gives no `/Prev` the chain ends there, what only the sections it skips index reading as null, or
as the older revision the stream gives, nothing reported of it but the section found moved — unless the catalog is among
it: the map is then rebuilt as the document opens, which finds it, and the validator says the file holds no catalog
([#260](https://github.com/AdCodicem/AdCodicem.Pdf/issues/260)) —; where it gives a `/Prev`, the chain goes on through
it, its own revision's table unread, what only that table indexes reading as null, nothing reported of it but the
section found moved, and nothing rebuilt ([#333](https://github.com/AdCodicem/AdCodicem.Pdf/issues/333)); where the
stream the `/XRefStm` read gives no `/Prev` and every stream past it gives none, or the table's, a first revision's
stream may be taken farther off, the revisions between lost, nothing reported of them but the section found moved and
the streams passed over ([#333](https://github.com/AdCodicem/AdCodicem.Pdf/issues/333)); that stream, where its `/Prev`
gives no offset in the file, is taken, that `/Prev` then found missing and the `/XRefStm` said to name what the `/Prev`
read ([#156](https://github.com/AdCodicem/AdCodicem.Pdf/issues/156)); and a `/Prev` a few bytes off a stream a newer
table's `/XRefStm` read never takes it: the table's own stream past it is taken where there is one, the chain ending
there, and otherwise the section is reported missing a few bytes from that stream
([#332](https://github.com/AdCodicem/AdCodicem.Pdf/issues/332)). Nor is a section looked for nearby when what is there
declares itself one — an `xref` keyword, or a stream whose dictionary gives `/Type /XRef` — and cannot be read: it is
reported where it is, any section near it being another ([#281](https://github.com/AdCodicem/AdCodicem.Pdf/issues/281)).
Where the offset holds no section, the search near it, nearest first, ends at a place that so declares itself and cannot
be read — an `xref` keyword, or the header of a stream of `/Type /XRef`, a token a load reads from the start of its
line, not the word or such a header in a comment or a string there, nor digits a regular character runs into —, as at
one it reads: that is the section found moved, reported where it was found, rather than passed over for an older section
farther off whose rows would stand for its lost ones in silence. The line is lexed from its start, read again as far as
4,096 bytes before the bytes searched where it starts before them: a place whose line starts farther back declares
nothing, as does one that the bytes a source gives end at or before
([#52](https://github.com/AdCodicem/AdCodicem.Pdf/issues/52)), and the search goes on past it, as past any place that
holds no section. Stream data, or a string, that an earlier line opened is not seen: the word `xref`, or such a header,
is taken all the same on a line that starts inside a string, and on a line of stream data wherever that line, lexed
alone, reads it as a token, its middle included, for the section found moved, the real one farther off left unread; and
a section there that reads, whole or in part — an attached file's own table, a cross-reference stream the data quotes —,
is taken whatever its line is, the chain going on through its trailer, while near the offset a hybrid table's `/Prev`
names, a quoted cross-reference stream that starts no sooner than the stream the table's `/XRefStm` read, gives
`/Type /XRef`, its header a token its line, lexed alone, reads, and gives no `/Prev`, or the table's, may be passed over
as the table's own and reported as `xref.stream-passed-over`, at a place that holds no section. Nor is the line of a
place that reads lexed: a table whose `xref` keyword a comment or a string on its line holds, which declares nothing, is
taken where it reads, whole or as far as `MaxXRefSectionLength` lets it — near an `/XRefStm`, read in the stream's
place, the stream farther off left unread —, though, where the guard cut its rows, a whole read may find them
unreadable and drop it ([#171](https://github.com/AdCodicem/AdCodicem.Pdf/issues/171)). An object there that gives a
`/W` and another `/Type` and does not read as a section — a CIDFont —, with no section near it, is no section, and is
reported missing; where `startxref` names one, it holds no section either, and its dictionary is no trailer. The
section `startxref` names is not looked for nearby: when it is not where `startxref` says, the map is rebuilt.

A rebuilt map keeps, of each number, the definition `object.redefined` names — the last written directly in the file,
or, for a number written only inside object streams, the first listed in the object stream read first —, a direct
definition under the generation its header gives and a member of an object stream under generation 0. An object
found nearby takes the generation of the header found there, whatever its row gave; an entry of the chain otherwise
keeps its row's. The rules that name an object from the map — `object.reference-missing`,
`object.name-null-character`, `object.endobj-missing` — name it so, `5 1` for `5 1 obj`; the cross-reference rules
name it as the file's row does, and the page tree and object-shape rules as the reference that reached it does.

What the reader noted of an object as it read it — that no `endobj` followed it, that a limit cut it, that its
stream's `/Length` was wrong — is forgotten by a rebuild, and noted again as the rebuilt map's definitions are read.
A rebuild a read sets off while that read is still under way — an object's `/Length` or filter names an object the
map misplaces — scans the file, and is finished once no read is under way, or once the read throws, before its exception
leaves: the object is then read again from the rebuilt map, and what was read meanwhile, with the read unfinished, is
let go, so that it reads, and reports, as it would had the map been rebuilt first, at the same cost.

If the source fails one of the rebuild's own reads, whether a read or the start of a validation set the rebuild off,
the failure leaves, and the next read or validation runs the rebuild again. A rebuild the source fails in its scan of
the file, or in a finish run with no read under way, withdraws what it reported since its scan began, and runs again
from the scan. A rebuild whose finish waited for the read under way withdraws only what that read and the finish
reported since the scan ended: the scan's reports stand, and are not made again, as the finish runs again from the
copy of the scan's map kept for it. `xref.rebuilt`, reported as the rebuild begins, stays either way. Running a
failed finish again from the scan costs a sound rebuild no copy of the map, which would take 40 bytes an entry. A
validation whose first checks of the map the source fails checks again at the next one.

A rebuild finds each object by its header, comments between the header's tokens included, as a load reads them
([#261](https://github.com/AdCodicem/AdCodicem.Pdf/issues/261)). It reads the file to its end, and holds no more
numbers than the map may: a header for a number it holds already
takes no room, so that a million copies of one object hide nothing behind them, and the first number past
`MaxIndexEntries` is reported once (`limit.index-entries`). A rebuild of a chain a limit had cut keeps the entries the
chain read for the numbers the scan did not find, rather than losing them. Whether it runs as the document opens or
after, a rebuild keeps the trailer the chain read too: it adds to it only the keys it lacks, from the trailers the scan
finds, and replaces only a `/Root` that leads to no catalog, by the catalog its search finds (`trailer.root-recovered`,
which a rebuild after opening that finds the same object again does not report again).

## What a rebuild after opening changes

A rebuild after opening — which a read sets off, or the start of a validation — changes what the document answers next,
not what it handed out before, so that a loop over what the document gives goes on whatever its body reads:

- `WasRepaired` turns true, `ObjectCount` counts the rebuilt map's entries, and `Diagnostics` holds what the rebuild
  reported, at once.
- The next enumeration of `ObjectNumbers` gives the rebuilt map's numbers; one under way goes on over those it began
  with. Nothing is copied: a rebuild that empties the map while an enumeration is under way records the new numbers in a
  new array, 4 bytes for each entry the map has room for — 1.2 MB at 300,000 objects, beside the 25.7 MB the rebuild
  allocates there.
- An enumeration of `Diagnostics` under way gives the entries recorded when it began, and the next one the rebuild's
  too: between two calls you make, entries are only added after those the record held. Once it has ended, it stays
  ended, whatever is recorded after.
- The next `Trailer` gives the trailer the rebuild merged, and `Catalog` and `IsEncrypted` read that one: the trailer
  the document held, with the keys it lacks taken from the trailers the rebuild finds, and a `/Root` the rebuild set.
  A trailer taken before is left as it was, without an older trailer's `/Info` or `/Encrypt`, or a `/Root` the
  rebuild set, and one taken after is never changed: read `Trailer` again after reading the document rather than keep
  it. The new trailer costs a copy of the old, a few hundred bytes.
- An object read before is left as it was; the next read of its number reads it through the rebuilt map.

Only a `PdfFileSource` of your own, reading the document from inside the read that rebuilds, sees the rebuild under
way. A trailer it takes is the new dictionary, which may still gain the trailers and the `/Root` the rebuild merges and
sets after. An enumeration of `Diagnostics` it steps may meet a finish that withdraws what the rebuild reported and
reports again: it reads, up to the count it began with, the entries held at those places, which may be those reported
again. Its `Current` stays the entry it advanced to, whatever is withdrawn or reported at that place after.

If the source fails a rebuild, `Trailer`, `Catalog`, `IsEncrypted`, `ObjectCount` and `ObjectNumbers` describe the
unfinished rebuild until the next read or validation runs it again: the map lacks the entries the scan or the finish
had not taken in — every one when the source failed the scan's first read —, and the trailer lacks the trailers the scan
had not merged and a `/Root` the finish would search for. Asking them finishes nothing, except that `Catalog` reads the
`/Root` that trailer names — the file's own `/Root`, not the catalog a search found as the document opened —, and that read
finishes the rebuild first. `Catalog` so gives the object that `/Root` names, which need not be a catalog, and null when
it names a free number or the trailer names none, though opening found a catalog; the next `Catalog` gives the finished
rebuild's.

## What it deliberately does not do

Past the index and the catalog, nothing is parsed until something asks for it, and no stream's data is read for that
stream before it is asked for: a `PdfStream` holds the offset and length of its bytes; the bytes arrive when you call
`GetRawBytes()` or `Decode()`, and not before. Reads made for something else take in whatever of a stream's data lies
within their reach, and keep none of it: the searches for the header and for `startxref`, the window an object or a
section is parsed through, the search near an offset an object, a `/Prev` or an `/XRefStm` names — 512 bytes either
side, and, for a section, as far as 4,096 bytes back to a line's start, above —, a rebuild's scan of the whole file, and
the search for where a damaged stream's data ends, below. A test holds this on a document of about 500 KB, nearly all of
it one page's content: opening it reads less than 200,000 bytes of the file, and reading that content's bytes afterwards
reads at least its 500,000 more.

Nor are the bytes kept once they have arrived: neither the stream nor the object cache holds them, and each call reads
them again. A document held in memory — opened from bytes or from a stream — lends them where they lie, without a copy:
what a call hands back is a view of that memory, which keeps it alive for as long as it is held, after the document is
disposed too, and changes as it does. One opened from a path reads them from the file into an array of their length.
Either is the caller's to keep or let go, so that memory follows the streams being read or decoded, not those read
before, and streams whose data share a stretch of the file — objects that overlap — do not each hold a copy of it. What
one call reads is bounded by `PdfReaderLimits.MaxStreamLength`, 256 MB by default: past it the data is cut, its filters
decode what was read as data the reader cut, and `limit.stream-length` is reported; `RawLength` stays the length the
file holds.

Finding where a stream's data ends reads little of it. The reader parses an object through a window of the file, and
confirms the declared `/Length` by the `endstream` that must follow it, after white space and comments, however many —
a comment only once white space comes before it, since a `%` right where the declared length ends is the data's —:
past the window, it asks the file for the bytes after the declared length rather than reading the data, and reads on
as far as white space and comments run. A length nothing confirms, or a `/Length` that gives none, sends the reader on
one search for the first `endstream` after the data's start: in the bytes the parser holds, then in the file, up to the
nearer of the first object header the bytes hold and the next object the index as the file wrote it places, or the end
of the file. With no `endstream` before that stop, the object's `endobj` plays its part: the search follows the last one
through its own reads, the comments after it taken for white space, rather than read the bytes before the stop again
backward. The search is made once for each stream, however often the stream is parsed again, and whether the object's
`endobj` follows the declared length is read again only when a rebuild changes that length; no window grows to find an
`endstream`, so that a stream's data never counts toward `MaxObjectLength`; and the searches of a document read four
times the file at most — a valid file's, about three times, for the white space after a declared length, the
cross-reference streams whose `/Length` the chain cannot read yet, and the streams whose `/Length` a guard hid, with the
proof that their `endstream` is their own. What each case keeps, and reports, is in [Diagnostics](diagnostics.md#where-a-stream-ends).

When it is read or decoded, a stream works under the [limits](reader-limits.md) of the document it came from,
however long after opening — so with `ThrowOnLimit` set, `GetRawBytes()` and `Decode()` can throw long after `Open`
returned. What decoding meets — a stream that lost its tail, a limit its filters reached — is reported when it is met,
in the diagnostics you pass to `Decode`, or in the document's own when you pass none; data cut at `MaxStreamLength` is
reported in the document's own once for each stream, as the reader reads it, and in those you pass to `Decode` each time
it decodes the cut data. A damaged stream nobody decodes is never reported, since nothing reads it.

Holding a stream as an offset and a length is what will let a copy between documents — merging, assembling,
stamping — move its **encoded** bytes as they are, with no decompress/recompress cycle in between, since nothing
has to decode a stream to copy it. No API copies one yet: merging and assembling come with M06 on the
[roadmap](/project/roadmap), stamping with M09.

## The cost of the trade

Objects are cached once parsed, in a bounded cache, so hot objects such as the page tree are not reparsed.
Beyond that bound, an object read again is read from the file again. That is the deliberate trade: memory
stays predictable, and the pathological case is slower rather than fatal. The bound is an option, and
[Tune the reader's memory](../guides/tune-reader-memory.md) says when to move it.

A stream's data makes the same trade without a cache: no stream keeps it, cached or not, so that data asked for twice
is read twice, and, through `Decode()`, decoded twice. A caller that needs it more than once keeps what one call gave,
as [the same guide](../guides/tune-reader-memory.md#read-a-streams-data-once) shows.

The same options carry the [reader limits](reader-limits.md), which bound what one file may make the reader
hold.

## Measured

| Operation | Document | Time | Allocated |
|---|---|---|---|
| Indexing | 1000 pages, ~4 MB | 200 to 222 µs | 83 KB |
| Indexing, then reading every page | 1000 pages, ~4 MB | 3.7 ms | 1.7 MB |
| Indexing and walking the page tree | real 1000-page file | — | 3.2 MB |
| Indexing a classic table | generated, 300,000 objects | — | 12.0 MB |
| Indexing a cross-reference stream whose rows Flate stores | generated, 300,000 objects | — | 22.5 MB |
| Indexing a chain of a thousand classic sections | generated, 300,000 objects | — | 24.2 MB |

The first two rows are BenchmarkDotNet's, measured on 2026-10-28 (ShortRun, twice). The last four are measured in the test suite, in Release, after a first
reading — the journal's on 2026-10-02, the generated indexes' on 2026-10-28 —, and enforced as budgets in CI, so that
an allocation regression past one fails the build: 4 MB for the journal, 12.6 MB, 23.6 MB and 25.5 MB for the generated
indexes, 5 % over their figures. The map is sized before it is filled, the chain's doubling from section to section
until it comes within a doubling of the newest `/Size`; the hash table the map was before 2026-10-28 allocated 16.9 MB,
27.4 MB and 30.6 MB sized so, and 32.6 MB and 43.1 MB left to grow, measured on the same day, and 33.2 MB for the
chain left to grow on 2026-10-14. The stream's rows are stored rather than compressed so that no runtime's zlib moves its figure.
