# 33. A remote document may be a member of a pinned archive

Date: 2026-09-25

## Status

Proposed on 2026-09-25, not implemented; tracked as T26 in `docs/status.md`. It extends
[32](0032-documents-that-cannot-be-redistributed-are-fetched-on-demand.md), which fetches one file per URL,
and leaves the rest of it unchanged. Accepting it amends ADR 32, `docs/corpus.md`, `tests/corpus/README.md`
and `fetch_remote.py`'s docstring, as listed under Consequences.

## Context

ADR 32 describes a remote document by the URL it is fetched from and the SHA-256 of what that URL serves,
and asks for immutable URLs: a repository commit, an Internet Archive `id_` copy, a permanent publisher URI.
Some documents are published only inside an archive, and none of those rules reaches them.

The one that matters is the **iPRES 2017 hand-built set** — Michelle Lindlar, Yvonne Tunnat and Carl Wilson
(RADAR records the last as "Carl, Wilson", given and family names swapped), [doi:10.22000/53](https://doi.org/10.22000/53),
CC BY-SA 4.0, so remote at best (ADR 23):

- **88 test files**, each derived from the same one-page document with one deviation from ISO 32000-1's
  structure, in eight categories: header 7, catalogue 7, page tree 9, page object 12, page resources 6,
  content stream 18, cross-reference table 10, trailer 19. A spreadsheet gives each file's category, a
  one-line description of the deviation, JHOVE 1.16.5's verdict and message, and whether Acrobat XI Pro
  opens it. It cites no ISO clause: mapping a deviation to a requirement is ours to do. It is the only test
  suite written by others for M2's structural profile.
- **The files hold more than their authors' deviation.** In 43 of the 59 header and body files the edit left
  the cross-reference offsets stale, so qpdf rebuilds the table; 15 of the 18 content-stream files start with
  a space before `%PDF`. `qpdf --check` passes 14 of the 88. `qpdf --show-npages` finds one page in 73, none
  in four, three and nine in two page-tree files, and fails on nine. Two files have no catalogue to recover
  (`T02-01_001`, `T04_011`: qpdf finds no `/Root`).
- **The OPF's copies are unusable** (`docs/corpus-sources.md`): git removed the five carriage returns of every
  test file and of the base document, `hello_world.pdf`, and the copy predates one test file, `T04_019`. The
  authors' own `Test_Corpus.md5`, inside the archive, lists exactly the OPF's 89 files and not `T04_019`.
- **RADAR serves the originals only as a BagIt tar**:
  `https://www.radar-service.eu/radar-backend/archives/JtlOdwQquZWDqQdq/versions/1/content`, 613,888 bytes,
  SHA-256 `33b2b4510f70aae3f4c606ca087292fc506759bb917e3b9bf9663d74ffca4f43`. Its MD5,
  `b19d4d5668bc8cb3cc8a1a3b0280e246`, is the archive checksum RADAR publishes on the dataset page, archived
  on 2017-11-05, the bag's own date: the tar is the deposit, not a package built per request, and two
  downloads on 2026-09-25 were identical. The URL is RADAR's backend, not one of ADR 32's three kinds; the
  persistent identifier is the DOI.
- **RADAR's terms for data users** (July 2019), which its web interface shows before a download, charge
  nothing, ask that the dataset's licence be honoured, and reserve RADAR's right to restrict or end the
  service. They say nothing of automated download, and the backend URL serves the tar with no login and no
  prompt, so ADR 32's condition — nothing behind a login, nothing whose terms forbid automated download —
  holds.
- **The tar's names are not the corpus's.** Members carry the bag directory
  (`10.22000-53/data/dataset/Test_Corpus/…`) and GNU long-name headers, and their names have capitals and
  underscores that `fetch_remote.py`'s file pattern refuses; lower-cased, with `_` as `-`, they stay unique.
- **`fetch_remote.py` reads `source.bytes` as the download's ceiling and exact size**, and retries each entry
  on its own. Pointed at an archive unchanged, it would refuse the archive for being larger than one
  member, and 88 members would each retry an outage — up to hours, in a job with 60 minutes.

## Decision

We will let a remote document be a member of an archive, pinned twice: the archive by its SHA-256 and size,
the member by its own.

- **The manifest.** `source.url` serves the archive. A new `source.archive` gives its `sha256`, its `bytes`
  and the `member`, the exact name `tarfile` reports, bag directory included. `source.sha256` and
  `source.bytes` keep pinning the document itself — the file at `file` — so the check of a file already
  present, `--list` and `Remote_documents_are_pinned_and_kept_where_git_ignores_them` keep their meaning.
  `source.landingPage` carries the persistent identifier (`https://doi.org/10.22000/53`).
- **When it applies.** Only to an archive shown to be immutable — a deposit whose checksum its repository
  publishes, or a URL of one of ADR 32's three kinds. Any other archive is not a remote source.
- **`fetch_remote.py`** reads an archive only when one of its members is missing or stale. It downloads it at
  most once per run into a temporary directory outside `remote/`, deleted at the end, with
  `source.archive.bytes` as ceiling and exact size and `source.archive.sha256` as pin, and remembers the
  outcome, failure included, for every member of that archive. After the hash matched it opens the archive
  with `tarfile` in mode `r:` — uncompressed only, standard library only — and, for each entry, refuses a
  member that is absent, is not a regular file, or whose size differs from `source.bytes`, before reading
  it; it copies at most `source.bytes` into the entry's `.part` file — never `extract`, never `extractall`,
  never a path read from the archive — then checks the member's SHA-256 as today. A refusal names the
  archive or the member it concerns, and concerns that entry only.
- **Validation.** `load_entries` and the C# test require `source.bytes` on every remote entry (all 153 carry
  it today). With `source.archive` they also require a 64-hex `sha256`, a positive `bytes` and a relative
  `member` without `..` or a leading `/`; every entry naming the same `source.url` carries the same archive
  pin, and no member is named twice.
- **Nothing extracted is committed**, and nothing else in the archive is fetched. The spreadsheet is read by
  hand when the entries are written; titles and expectations are in our own words and cite the test-case
  number, never its text, since ShareAlike reaches whatever is copied.

## Consequences

- **The set enters as 88 remote entries**, `remote/ipres2017/<name lower-cased, _ as ->.pdf`, after a
  personal-data screen, each member checked once against the bag's `manifest-md5.txt`. Expectations come
  from the independent tools, as for any document: pages from `qpdf --show-npages`, the verdict from
  `build_corpus.py --remote`, `clean` from qpdf — even where qpdf passes a file built broken, since M2 then
  asks no error-severity finding of it, which is its rule: an error means readers disagree. The test-case
  number, the category and JHOVE's 2017 verdict are features, as for the JHOVE issue files: a second
  opinion, not a referee.
- **Each gap names the milestone that will close it.** Entries the reader cannot yet meet are marked
  unsupported: M2 for the 75 file, cross-reference, catalogue, page-tree, page-object, resource and
  stream-object cases, M10 for the 13 content-operator cases (`BT`, `ET`, `Tf`, `Tj`, their operands and
  parentheses), which need the content interpreter. The two files with no catalogue get a new expectation
  that says so, with its test, rather than a marker no milestone would lift.
- **M2 then owes them**: its row for documents the manifest calls not clean covers the set, and the slice
  that names a rule adds the expected finding to each entry, or the reason the profile stays silent.
- **Amended on acceptance**: ADR 32's URL rule and its description of `source`; the `source` bullet of
  `docs/corpus.md`; *Remote documents* in `tests/corpus/README.md`; `fetch_remote.py`'s docstring.
- **Costs**: one more path through `fetch_remote.py`, 613 KB a night, and the archive pin repeated in 88
  entries. A top-level map of archives that entries reference would state it once, at the price of a
  second place every script and test must read; the choice is left to the implementation.
- **What would reopen it**: RADAR replacing the deposit (a new version has a new URL, and is a new
  document), or terms that forbid automated download.
