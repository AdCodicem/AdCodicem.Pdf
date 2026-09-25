# 33. A remote document may be a member of a pinned archive

Date: 2026-09-25

## Status

Proposed on 2026-09-25 as T26 (`docs/status.md`), accepted the same day. It extends
[32](0032-documents-that-cannot-be-redistributed-are-fetched-on-demand.md), which fetches one file per URL,
and leaves the rest of it unchanged; ADR 32, `docs/corpus.md`, `tests/corpus/README.md` and
`fetch_remote.py`'s docstring are amended accordingly.

Implemented by `source.archive` in `tests/corpus/manifest.json`, the `Archives` class and `copy_member` in
`tests/corpus/build/fetch_remote.py`, `tests/corpus/build/test_fetch_remote.py` — run by the main CI job
against a local server —, `CorpusReadingTests.A_document_taken_from_an_archive_pins_the_archive_and_itself`,
and the 88 entries under `remote/ipres2017/`. The first run fetched all 88 from one download of the tar.

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
- **The files hold more than their authors' deviation.** In 45 of the 59 header and body files the edit left
  a table that no longer leads to its objects: qpdf rebuilds it in 43, and adjusts to a leading space in the
  other two; 15 of the 18 content-stream files start with
  a space before `%PDF`. `qpdf --check` passes 14 of the 88. `qpdf --show-npages` finds one page in 73, none
  in four, three and nine in two page-tree files, and fails on nine. In one file (`T02-01_001`) no object is
  a catalogue at all; in five more the catalogue is there but the trailer does not lead to it, and qpdf gives
  up (`unable to find /Root dictionary`), and in three qpdf finds no trailer at all. The reader recovers the
  catalogue in all eight.
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
- **Validation.** `load_entries` and the C# test require `source.bytes` on every remote entry (all 242 carry
  it). With `source.archive` they also require a 64-hex `sha256`, a positive `bytes` and a relative
  `member` without `..` or a leading `/`; every entry naming the same `source.url` carries the same archive
  pin, and no member is named twice.
- **Nothing extracted is committed**, and nothing else in the archive is fetched. The spreadsheet is read by
  hand when the entries are written; titles and expectations are in our own words and cite the test-case
  number, never its text, since ShareAlike reaches whatever is copied.

## Consequences

- **The set enters as 88 remote entries**, `remote/ipres2017/<name lower-cased, _ as ->.pdf`, after a
  personal-data screen — the files hold no metadata, and their only text is "Hello PDF-world!" —, each
  member checked once against the bag's `manifest-md5.txt`. Expectations come from the independent tools,
  as for any document: pages from `qpdf --show-npages`, the verdict from `build_corpus.py --remote`, `clean`
  from qpdf — even where qpdf passes a file built broken, since M2 then asks no error-severity finding of
  it, which is its rule: an error means readers disagree. The 15 files with a space before `%PDF` expect the
  offset adjustment qpdf makes without a word, as the corpus's other prefixed files do. The test-case
  number, the category and JHOVE's 2017 verdict are features, as for the JHOVE issue files: a second
  opinion, not a referee.
- **Each gap names the milestone that will close it.** On implementation the reader met 69 entries as
  written and 19 were marked unsupported, all until M2: seven page trees that qpdf and the reader count
  differently, four faults the reader reads without a word (a root typed `/Pagez`, a page typed `/Font`,
  generation 10000 in the table, a trailer without `/Size`), six recoveries that differ from qpdf's —
  among them four where the trailer's `/Root` is missing or broken and the reader rebuilds a sound index to
  find the catalogue, rather than looking among the indexed objects first —, and two references to an
  object the file lacks, which qpdf takes as null, as the specification says, and after which the reader
  rebuilds its whole index and reports a repair (T27). Those two came to light in review: the acceptance
  test judged a clean file's diagnostics before walking its page tree, and now walks it — each page's
  contents and resources resolved — first; no other document in the corpus changed. None waits for M10:
  the edits to the 13 content-operator cases also broke their lengths or offsets, which the reader meets;
  the operator faults themselves (`BT`, `ET`, `Tf`, `Tj`, `cm`, their operands and parentheses) become
  findings when M10's interpreter exists.
- **One file has no catalogue to recover**: the manifest's new `catalogRecoverable: false` says so, and the
  acceptance tests then require the reader to open it, report the rebuild, and hand back no catalogue
  rather than invent one.
- **M2 then owes them**: its row for documents the manifest calls not clean covers 72 of them, its row for
  well-formed documents the 16 that qpdf passes or only warns about — so those may earn warnings, never
  errors —, the 19 are named in its acceptance conditions, and the slice that names a rule adds the
  expected finding to each entry, or the reason the profile stays silent.
- **A source serving more bytes than pinned is refused, not reported unavailable** — found while writing the
  fetcher's tests: a file that grew changed, it did not disappear. Review found one more: a pinned archive
  with a damaged header past the first ended the whole run; every header is now read before any member is
  copied, and a damaged archive refuses its own members only.
- **Amended on acceptance**: ADR 32's URL rule and its description of `source`; the `source` bullet of
  `docs/corpus.md`; *Remote documents* in `tests/corpus/README.md`; `fetch_remote.py`'s docstring. Every
  remote entry now pins its size, which all of them already did.
- **Costs**: one more path through `fetch_remote.py`, 613 KB a night, and the archive pin repeated in 88
  entries. A top-level map of archives that entries reference would state it once, at the price of a
  second place every script and test must read; the implementation keeps the pin in each entry, and both
  validators require every entry naming an archive to pin it the same way.
- **What would reopen it**: RADAR replacing the deposit (a new version has a new URL, and is a new
  document), or terms that forbid automated download.
