# 50. Validation settles the reader before its rules

Date: 2026-10-16

## Status

Accepted on 2026-10-16, by the maintainer, during M02's fourth step, settling
[#237](https://github.com/AdCodicem/AdCodicem.Pdf/issues/237) and
[#128](https://github.com/AdCodicem/AdCodicem.Pdf/issues/128). Implemented by `PdfFileReader.SettleIndex`, which
`PdfValidator.Validate` calls before its first rule, and documented in `docs/website/docs/concepts/validation.md`.

## Context

The validator reads through the document it is given, lazily, like any other caller (ADR 36). A read can make the
reader rebuild its index: when the index is incomplete because the file is damaged, or when an entry places its object
neither at its offset nor within 512 bytes of it. A rebuild changes what the reader serves: it merges the trailers it
finds into the trailer, serves the last definition of each number, and may take another catalog.

When one of the rules' walks set off that rebuild, the rules before it had read the document as the chain gave it and
the rules after it as the rebuild did. Two validations of one document gave different reports, and what the caller had
read before validating decided which. #237 showed it with a trailer key only a rebuild merges; measuring it found 96
crafted files in which a fresh validation, a second one and one after a read disagreed in 72 cases, a crash in
`ObjectGraph` among them, and #128 the same through the walk of the object streams' dependencies. The documentation
promised the opposite: two validations give the same report, and reading objects between them changes nothing.

Three designs were measured on the 96 crafted files and the 401 documents of the corpus:

- **Settle before the rules.** Before its first rule, the validator has the reader rebuild the index once when some read
  still could, found by reading the header each entry places, and nothing more. The reader forgets, on a rebuild, what it
  noted of the definitions it replaces; the file's structure is the one the document opened with; an object whose own
  load set off the rebuild is not cached.
- **Validate the document as it stood at opening.** Each validation reads through a view of the reader frozen when
  opening ended: its own loader, cache and records, and a copy of the opening trailer, never rebuilding.
- **Fix the issues alone.** #128's walk reads the chain's index again for each lookup, and the validator rebuilds first
  when the index is known to be incomplete, as #237 proposed.

## Decision

We settle the reader before the validator's first rule. `Validate` calls `PdfFileReader.SettleIndex`, which rebuilds the
index, once per document, when the index is incomplete through damage or an entry in use places its object where a load
would find neither it nor its header nearby. Whether a load would is decided as a load decides it, the header read alone
— after white space and comments, in a window grown while the header runs past it —, without parsing the object or
recording anything.

With it: a rebuild forgets what the reader noted of each object as it read it; `FileStructure` is filled while the
document opens and never changes afterwards, a later rebuild keeping what it finds to the reader; an object or object
stream whose own load set off a rebuild is not cached; the object graph describes each object from the value it walked;
the walk of the object streams' dependencies reads the chain's index, and each stream where that index places it.

The rules judge the rows of the index as the file wrote them. A value written elsewhere that a row's object needs is read
as any read reads it, through the index the reader settled on.

## Consequences

- A document gives the same report whatever was read before validating it: the 96 crafted files and the 401 documents
  of the corpus do, which `ReadOrderConvergenceTests` and the integration suite's `ReadOrderSweepTests` hold. No read
  rebuilds the index once a validation has settled it, which `CorpusValidationTests` holds over the corpus.
- The corpus's reports did not change: 0 of 401 documents. Three documents whose damage no rule reaches — Quartz files
  of the remote corpus — are now rebuilt when a validation starts, a few milliseconds each.
- Settling costs a header read per entry in use: under a microsecond an entry, about 2 % of a validation, and no
  allocation an entry. On a hostile file it reads at most what loading every object would read before their bodies, bounded by
  `MaxIndexEntries`, and stops at the first entry a load would rebuild for.
- `WasRepaired`, `ObjectCount`, `ObjectNumbers`, the merged `Trailer` and `IsEncrypted` can change when a validation
  starts, on a damaged file, as they could on any read.
- Two exceptions remain, named in the documentation: a document opened with `ThrowOnLimit` whose settling rebuild reaches
  a guard throws in its first validation only, the index rebuilt whole before; and a document the caller changed between
  two validations is validated as changed.
- The settling check replicates what a load accepts. Were the load to change and the check not, rebuilds in the middle
  of a validation would come back silently; the corpus check above, and the crafted files, would see it.
- Validating the document as it stood at opening was rejected: it changed the reports of two corpus documents, did not
  report a guard only validation reached (invariant 12), held a second cache — 23 % more time and up to three times the
  memory after a full read — and needed a second loader kept in step with the reader's. Fixing the issues alone was
  rejected: 38 crafted files still disagreed with themselves, and `Validate` still crashed on two.
- What stays order-dependent is known and filed: the reader's memo of where streams end
  ([#138](https://github.com/AdCodicem/AdCodicem.Pdf/issues/138)).
