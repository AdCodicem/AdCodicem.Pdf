# 52. Tables keyed by numbers are dense or seeded

Date: 2026-10-28

## Status

Accepted on 2026-10-28, on the maintainer's four decisions of that day, settling
[#155](https://github.com/AdCodicem/AdCodicem.Pdf/issues/155). Implemented by `PdfXRefTable`, `SeededComparer`, the
`GetHashCode` of `PdfObjectId` and `PdfDiagnostic`, and held by `SeededComparerTests` and the hash cases of
`ReaderTimingTests`.

## Context

The reader and the validator key many hash tables by numbers a file chooses: object numbers, generations, the offsets
its rows and `/Prev` give, where its streams lie. .NET hashes an `int` as itself and a `long` as its two halves
exclusive-ored, and puts a key in the bucket its hash leaves modulo a prime from a list anyone can read. A record
struct's own hash is a fixed linear combination of its fields — `Number × −1521134295 + Generation` for `PdfObjectId` —,
which a file inverts. So a file can choose numbers a multiple of that prime apart and put them all in one bucket, or give
two offsets or two identifiers one hash outright, and every lookup walks them all. Measured on `main` at `e3e2034`, each
a single run in a process of its own, its compilation included, against the same file numbered in order:

| Shape | In order | Chosen to collide |
|---|---|---|
| 34,167 one-row subsections, opened | 0.04 s | 1.9 s |
| The same, validated | 0.09 s | 4.0 s |
| 36,350 objects and no table, rebuilt as the document opens | 0.07 s | 8.0 s |
| 1,000,000 references to those subsections' numbers, resolved | 0.73 s | 56 s |
| The same references, reached from the catalog, validated | 0.68 s | 10.6 s |

The work a call does no longer follows the bytes it reads (invariant 4). No guard bounds it: each shape stays far under
every limit, and the total grows with the references a file holds.

The index, keyed by object number, is the reader's hottest table: every object read looks a number up in it, and
opening a file records each of its rows. Most files number their objects from 0 up with few gaps.

## Decision

- **The index keeps its entries in arrays.** `PdfXRefTable` holds the numbers and their entries in two arrays, in the
  order it first recorded them, and finds a number's entry through a slot by number while numbers run in order: the
  slots stretch to a number only while they stay no longer than twice the entries the index holds or has made room
  for, and 1,024 more, and to the room made at once. A number past them goes to a map hashed through the process's seed,
  and moves into a slot once the slots reach it; the map gives back the room of the numbers that left it once they are
  most of it. The room made counts toward the slots so that a chain whose newest sections number the highest objects, as
  updates do, does not fill the map first. Room follows the rows the sections hold, but for the last step, which batch
  11 takes to the newest `/Size` once a doubling comes within a doubling of it: the slots follow it there, four times
  what the index held at most. A `/Size` the sections number past stands for one past the highest number they gave,
  until an older section numbers past the newer ones; from then on it bounds nothing, so that the index doubles where it
  would otherwise grow by one entry at each such section.
- **Every other hash table keyed by numbers hashes them through the process's seed.** `SeededComparer` combines a
  number, or both halves of an offset apart, or the items of the four tuples the reader keys by, through
  `System.HashCode`, which .NET seeds once a process as it does strings. The reader's cache, its object streams, its
  sets and memos, the copies it marks and restores, and the validator's walks take it.
- **The public `PdfObjectId` and `PdfDiagnostic` hash through the same seed**, overriding the record struct's hash;
  equality stays the record's. A caller's table keyed by them is spread alike.
- **Tests read the code.** One reads the compiled library, whatever the source wrote, and refuses every hash collection
  built over a key a file can steer — a number wider than a byte, nullable or not, a tuple or an anonymous type holding
  one, a type of the library hashed as the compiler writes a record's hash — unless the comparer of its key, taken last,
  is `SeededComparer.Instance`, handed to it straight on every path that reaches it: a `Dictionary`, `HashSet`,
  `OrderedDictionary` or `KeyedCollection` built or derived from, or a LINQ operator that hashes, any of them as a
  method group. It refuses always a table keyed by any object, a boxed number hashing as itself — a `Hashtable` or a
  type derived from one, a `HybridDictionary`, the non-generic `OrderedDictionary`, the hash tables `CollectionsUtil`
  builds, a key typed `object` or `ValueType` or an interface numbers implement, a reference comparer given or not: an
  identity table is keyed by a reference type of the library, as the validator's is —; a collection that enumerates by
  hash — a `ConcurrentDictionary`, a frozen or immutable one, its factories, its builder and an immutable one's `Empty`,
  a `StringDictionary`, however built — whatever its key and comparer, since a hash that goes through a seed, now or
  after a later change, would order what it gives, and no key is argued one by one: the one named and let be is
  `PdfName`'s interning table, keyed by strings, which its static constructor builds once, straight into a static field,
  and which the library reaches, through its type or a generic interface of it, only to find or add one name, a member
  that takes it, gives it, views it or names its pairs refused, the table not followed through `object` or a non-generic
  interface; and a table built for a type argument by `new()`, `Activator`, `Lazy` or `LazyInitializer`. The rule covers
  every numeric key, whatever it numbers and wherever it comes from, so that none needs arguing. The library builds no
  hash collection over a type parameter, whatever its comparer: its callers choose its key and the comparer they hand
  it, and no reading follows them. How the hash of a type the library hashes by hand spreads is held by its own tests,
  as `PdfObjectId`'s and `PdfDiagnostic`'s are; an enumeration's values are the library's own; a collection built
  through reflection otherwise, or one a framework method returns under another type, is not read. The reading takes out
  the probes code coverage puts, by their shape alone, so that a probe written otherwise refuses the table it stands
  before. The other reads the sources, their comments and literals blanked, and names the file and line of the shapes it
  recognizes over a numeric key: a declaration given `[]` or `new()`, a `new` without the comparer among its own
  arguments or in its initializer, a name declared with no table and given one later, a LINQ operator that hashes
  without naming a comparer, the tables an array's initializer builds, and a concurrent, frozen or immutable one whose
  type it writes and that it sees built with `[]` or `new`, there or later, seeded or not; one an operator or a factory
  builds is the compiled reading's.
- **No bound is added**, and nothing is classified under ADR 34: the slots' slack is a property of the representation,
  not a limit on what a file may hold.

## Consequences

- `ReaderTimingTests` holds the first three shapes at the sizes above, the first shape's numbers aimed at the index's
  own map as well, the object cache at 200,000 references to 20,000 objects numbered its size apart, and validation at
  100,000 references to 30,000 free numbers numbered a set's size apart: each takes about the time of the same file
  numbered in order. The fourth and fifth shapes take that time too, measured on the batch's prototype.
- For numbers in order the index is lighter and no slower: 40 bytes an object, allocated and kept, against 52 to 63, so
  that opening 300,000 objects allocates 12.0 MB against 16.9 for a classic table and 22.5 against 27.4 for a
  cross-reference stream, and a chain of a thousand sections 24.2 MB against 30.6, measured on `e3e2034` the same day.
- An object numbered apart from the rest costs more than it did: its number in the map keeps 20 to about 42 bytes beside
  its entry and slot, by where the map's count falls against .NET's prime steps, so that it keeps about 60 to 85 bytes
  in subsections of ten rows or more, up to about 96 in subsections of two and 80 to 110 in a table of one-row
  subsections, whose headers make room as rows do, and a rebuild over such numbers allocates about 110 to 220; it costs
  a hash and a lookup more; opening 500,000 subsections whose every other number lies far from the rest took about 1.2
  times as long as before, and with every number far from the rest about 1.4 times.
- Arrays sized exactly leave no slack: one row past the room made for the newest section sets off batch 11's last step
  to the newest `/Size`, which the map's primes once absorbed, and room made for a `/Size` that numbers up to four times
  the objects the sections give keeps up to 160 bytes an object they give, where the map kept about 60. The step saves
  a copy of the index to every chain whose `/Size` is honest, and a valid file only reaches four times its objects by
  numbering them sparsely.
- A `/Size` the sections number past bounds nothing once an older section has numbered past the newer ones, so that the
  room doubles where it would grow by one entry a section: exact arrays, unlike the map's primes, would otherwise copy
  the index at each one. Until then one past the highest number read stands for it, so that an update chain that kept
  its original `/Size` still lands on its count.
- The seeded tables cost a hash and an interface call a lookup, and scatter keys a table numbered in order would keep
  together. Measured on the batch's last code against `e3e2034`, a medium job of each twice, alternated: validating
  1,000 pages already read takes 2.9 to 3.2 ms against 2.6 to 2.7, about 15 % more, where the prototype had measured 5
  to 8 %; opening and validating them 7.0 to 7.3 ms against 6.7 to 6.8, about 5 % more, the lighter index paying back
  part of it; the Arlington walk alone 2 to 6 % more. Opening an index of 300,000 objects takes 8 to 13 % less, a chain
  of a thousand sections about as long, and indexing 1,000 pages 200 to 222 µs against 230 to 242 (ShortRun, twice).
  What the validator's walks pay is filed under M23
  ([#305](https://github.com/AdCodicem/AdCodicem.Pdf/issues/305)), a set indexed by the index's slots the way to measure
  first.
- A hash differs from one process to the next. Tables enumerate in an order their adds and removes set, never in an
  order their hashes give — the reading of the compiled library refuses one that would, whatever its key, but
  `PdfName`'s interning table, which nothing enumerates —, so nothing the library reports or writes changes with it
  (invariant 6). A caller must not store a `PdfObjectId`'s or a `PdfDiagnostic`'s hash.
- One seed serves the whole process, as for strings: an attacker who could learn it, by timing a long-running server's
  lookups, could aim at it for the life of that process.

### Alternatives rejected

- **The seed everywhere, the index included.** The least code. Opening an index of 300,000 objects cost 45 to 70 % more
  (30–40 ms to 45–62 ms), and reading or validating 1,000 pages 4 to 12 % more: random hashes scatter the keys an
  index numbered in order keeps in step with its entries.
- **Numbers below 65,536 hashed as themselves, the seed above.** As little code, 1,000 pages as fast as before, and
  chains a file can still build, bounded to 256 entries since a table never holds more entries than buckets: measured
  at 1.8 times the same references numbered in order. An index of 300,000 objects cost 30 to 60 % more.
- **The dense index, and numbers below 65,536 hashed as themselves elsewhere.** The fastest everywhere, the validator's
  sets keeping chains of up to 256 entries against a hostile file instead of none.
- **A sorted index searched by halves.** No seed at all, but a logarithm of comparisons a lookup, and the validator's
  sets would have needed trees of their own.
- **A seed drawn for each document.** An attacker who learned one process's seed could not reuse it on the next
  document, at the cost of a comparer to carry to every table, the validator's included.
- **A fixed mixing function.** Its inverse is public: a file inverts it as it inverts the identity.

## What would reopen it

A measurement where the seeded tables, not the index, dominate a real workload; a runtime that lets a table switch to
a seeded hash only once a chain grows long, as .NET does for strings; or evidence that a process's seed can be
recovered from outside it.
