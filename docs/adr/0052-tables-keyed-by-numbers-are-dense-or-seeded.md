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
struct's own hash is a fixed sum of its fields. So a file can choose numbers a multiple of that prime apart and put them
all in one bucket, or give two offsets one hash outright, and every lookup walks them all. Measured on `main` at
`e3e2034`, each against the same file numbered in order:

| Shape | In order | Chosen to collide |
|---|---|---|
| 34,170 one-row subsections, opened | 0.04 s | 1.9 s |
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
  for, and 1,024 more. A number past them goes to a map hashed through the process's seed, and moves into a slot once
  the slots reach it. The room made counts toward the slots so that a chain whose newest sections number the highest
  objects, as updates do, does not fill the map first; room is made only for rows the file holds, so the slots follow
  its bytes whatever its `/Size` claims.
- **Every other hash table keyed by numbers hashes them through the process's seed.** `SeededComparer` combines a
  number, or both halves of an offset apart, or the items of the four tuples the reader keys by, through
  `System.HashCode`, which .NET seeds once a process as it does strings. The reader's cache, its object streams, its
  sets and memos, the copies it marks and restores, and the validator's walks take it.
- **The public `PdfObjectId` and `PdfDiagnostic` hash through the same seed**, overriding the record struct's hash;
  equality stays the record's. A caller's table keyed by them is spread alike.
- **A test reads the sources** and refuses any `Dictionary` or `HashSet` keyed by an `int`, a `long` or a tuple holding
  one that is built without the comparer — a declaration given `[]` or `new()`, a copy, a later assignment —, and any
  LINQ operator that hashes; the rule covers every numeric key, whatever it numbers and wherever it comes from, so that
  none needs arguing. A second test checks the comparer of each such table the reader holds once a document is opened,
  rebuilt and validated.
- **No bound is added**, and nothing is classified under ADR 34: the slots' slack is a property of the representation,
  not a limit on what a file may hold.

## Consequences

- The five shapes above take about the time of the same files numbered in order (`ReaderTimingTests`).
- The index is lighter and no slower: 40 bytes an object numbered in order against 52 to 63, so that opening 300,000
  objects allocates 15.0 MB against 16.9 for a classic table and 25.5 against 29.5 for a cross-reference stream, and a
  chain of a thousand sections 25.7 MB against 30.6.
- The seeded tables cost a hash and an interface call a lookup, and scatter keys a table numbered in order would keep
  together: measured on a prototype, validating 1,000 pages costs 5 to 8 % more.
- A hash differs from one process to the next. Tables enumerate in the order they were filled, never in an order their
  hashes give, so nothing the library reports or writes changes with it (invariant 6). A caller must not store a
  `PdfObjectId`'s or a `PdfDiagnostic`'s hash.
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
