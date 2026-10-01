# Project status

A living file, updated **at the end of every session**. It describes the real state, not intentions.
Keep it short: summarize the journal once it passes a dozen entries — the detailed history is in git, not
here. What is tracked item by item lives on GitHub since 2026-09-27: each milestone's slices and the known
debt are issues, filed under the [milestones](https://github.com/AdCodicem/AdCodicem.Pdf/milestones) the
Tracking workflow mirrors from `docs/roadmap.md` (*Debt and open points*, below).

## At a glance

- **Current milestone**: M02 — Document validation (`docs/milestones/M02.md`), in progress. Slices 1 to 3 are done:
  the engine and the report (ADR 36), twenty file and cross-reference rules under ADR 45's severities, and the object,
  page tree and Arlington object-shape rules (ADR 44). What remains is *Next concrete step*: step 4's thirty-two batches
  of reader and validation debts, settled with the maintainer on 2026-10-01, the first of which, [#117], merged with
  [#203] that day; then slices 4 to 6 ([#60] to [#62]); then the milestone's adversarial review, slice 7 ([#137],
  ADR 46), once every other issue filed under M02 is closed. The debts come from the reader's own work, the threat
  model's first version ([#135], `docs/threat-model.md`), M01's review after the fact ([#136], `docs/reviews/M01.md`)
  and the planning of step 4 ([#199] to [#202]); [#123] moved to M20.
- **User documentation**: organized along Diátaxis since 2026-09-30 ([ADR 47](adr/0047-the-user-documentation-follows-diataxis.md),
  [#148]), on `main`: a tutorial held to its sample by a test, four how-to guides, four reference
  pages — the validation rules among them, moved from the project documents — with the API reference under them, and
  four explanations. Outside any milestone.
- **Last milestone closed**: **M01 — Object model and tolerant reading**
- **Tests**: 2,504 unit on `main` since [#203] (3 skipped: 2 by design, and the theory over the remote corpus's streams
  whose length is wrong, which has no document without it) + 1,306 integration (skipped without Docker) + 23 for the
  remote corpus's fetcher + 43 for the roadmap's mirror on GitHub. With the remote documents fetched, on [#203]'s branch:
  4,176 unit with 233 of them, 3 skipped — the laziness test on the two documents recorded as unsupported until [#47],
  which every other test holds to their expectations, and the private manifest this container lacks —; 3,266
  integration against qpdf in its container, 3,260 passed and 6 skipped where qpdf cannot walk a damaged document's
  pages; and, with all 242, `Remote corpus` run 19's 4,224 acceptance tests and 3,329 referee checks.
- **Coverage**: on the committed corpus, as Codecov counts it (a line with an untaken branch is partial, the generated
  Arlington tables left out), 99.6 % of `src/` — 4,889 of 4,907 lines on `main` since [#203], all 21 of its patch among
  them. The 18 left are those the rule of 2026-09-29 leaves (`CLAUDE.md`, *Coverage*): members that are private, or of
  a private type, which no input reaches — nine lines of `ArlingtonWalk` (193, 849, 956, 966, 967, 987, 989, 1061,
  1077) and three of a defensive branch of `PdfLexer` (161, 164, 165) —, a `?.` on an index never null where it is read
  and a switch's default arm (`CrossReferenceProbe` 86 and 225, `PageTreePageOrphanedRule` 51, `RootInvalidRule` 86
  and 95), and a line the compiler puts after a call that never returns (`PdfFileReader` 1094). `codecov.yml` asks 95 %
  of each patch, and lets the project drop by half a point at most; the aim is 100 %.
- **CI**: green on `main` at `a27e07d` (CI run 399), [#203]'s merge. `Remote corpus` passed on every document in the
  nightly run 18, on `main` at `8c54490`, and in run 19, on [#203]'s branch, rerun once after web.archive.org refused a
  document ([#204]).
- **Corpus**: 168 committed documents, 23.0 MB — 19 generated here, 3 from Word and PDF24 on Windows, 146
  third-party files under attribution-only licenses (`docs/corpus-sources.md`). Beside them, a **remote
  corpus** of 242 documents we may use but not redistribute, fetched at a pinned SHA-256 and size (ADR 32),
  88 of them out of their authors' archive (ADR 33), 7 out of web.archive.org, and tested every night by `Remote
  corpus`, last green in run 19 on 2026-10-01. All 410 are described in `tests/corpus/manifest.json`.
- **Published**: [`AdCodicem.Pdf`](https://www.nuget.org/packages/AdCodicem.Pdf) `0.1.1-preview.10` to
  `0.1.1-preview.49`, previews from `main` through trusted publishing, 1,141 downloads on 2026-10-01. The
  `AdCodicem.*` prefix is reserved: nuget.org marks the package as verified.
- **A preview carries no guarantee** (ADR 30, 2026-09-26): an API no stable release has shipped may change or
  go with the next merge.
- **No stable release yet.** `v0.1.0` is a tag with no package behind it, by design, and the stable path has
  never run. When it does, it will fail at its push of the `chore(release)` commit to `main` until the
  ruleset lets GitHub Actions bypass it (T15); nothing is published when it fails.
- **The site**, <https://adcodicem.github.io/AdCodicem.Pdf/>, is versioned (ADR 31) and redeployed by every
  preview; before the first stable release the preview is the whole site.
- **Supply chain**: OpenSSF Scorecard **7.6** on `74ce382`. What Scorecard still marks down is settings and
  people: Code-Review 0 (nothing has ever been approved by a second person), Branch-Protection 5 (T15), Maintained 0
  (the repository is younger than 90 days), Contributors 3, CII-Best-Practices 0 (T18), Signed-Releases
  unscored until a release exists (T19). What it does not measure, the threat model found in code: the release jobs
  build and test with the right to publish ([#177]), nothing ties publishing to `main` ([#178]), and nothing but
  review keeps the core free of dependencies ([#179]).
- **Codecov**: uploaded without a token; the Codecov GitHub App is installed since 2026-09-27, and reports on each
  pull request as `codecov[bot]` (#40, formerly T14).
- **Repository settings**: the "Default" ruleset on `main` asks for a pull request with one code-owner
  approval and every review thread resolved, linear history, CodeQL and coverage of at least 61 %; it
  requires no status check by name, and only administrators bypass it. CodeQL runs as GitHub's default setup
  since 2026-09-22; the repository's own workflow and configuration, disabled since then, were deleted on
  2026-09-26 at the maintainer's choice (T35).
- **Branches**: five merged branches are still on the remote and hold nothing `main` needs —
  `claude/dependabot-prs-review-p3mx79`, `claude/nuget-pdf-html-dotnet-msyz8z`,
  `claude/package-preview-deployment-h0lakt`, `claude/scorecard-improvement-4ckfxd`,
  `claude/scorecard-pipeline-47mgrj`. The maintainer asked for their deletion on 2026-09-26, but this
  session's git proxy refuses to delete a branch the session did not create (HTTP 403), so it is the
  maintainer's to do; their tips are `c5b0a19`, `d597294`, `e0c5aba`, `cd31f84` and `637772e`, should one be
  wanted back.

### Current measurements (BenchmarkDotNet, ShortRun)

| Operation | Document | Time | Allocated |
|---|---|---|---|
| Indexing | synthetic, 1000 pages, ~4 MB | 229 µs | 393 KB |
| Indexing, then reading every page | synthetic, 1000 pages, ~4 MB | 6.2 ms | 5.9 MB |
| Indexing and walking the page tree | real ReportLab document, 1000 pages | — | 3.2 MB |
| Validating under the structural profile, the document already open and read | synthetic, 1000 pages | 2.5 ms | 588 KB |
| Typing and checking the objects the trailer reaches against the Arlington model, alone | synthetic, 1000 pages | 1.7 ms | 280 KB |
| Opening and validating under the structural profile | synthetic, 1000 pages | 6.2 ms | 2.6 MB |
| Decoding a whole Flate content stream | 4 MB decoded, about 330 KB encoded | 8.3 ms | 13.1 MB |
| Decoding the same stream without its checksum, or without its last five bytes | 4 MB decoded | 11.7 ms | 13.1 MB |
| Decoding the same stream under a wrong checksum, kept whole | 4 MB decoded | 12.1 ms | 13.1 MB |
| Decoding a stream that turns corrupt halfway, the 2 MB before the fault kept | 4 MB of content | 8.1 ms | 6.6 MB |
| Indexing, then parsing every page's content stream, each past the reader's first window | synthetic, 1000 pages of 16 KB | 3.9 ms from memory, 7.1 ms from a file | 2.0 MB |
| The same, each stream declared 2 bytes too long and searched for its `endstream` | synthetic, 1000 pages of 16 KB | 7.3 ms from memory, 12.5 ms from a file | 2.8 MB |

The gap between the first two rows is the library's promise: opening a document does not read its content.
The validation rows are slice 3's thirty-eight rules. The first measures the rules alone on a document whose objects
the reader has cached: each entry of the index probed, the page tree walked, every object the trailer reaches met
once and typed and checked against the Arlington model, every object of the index looked at for a page the tree
leaves out — the allocation is the sets of object numbers met, each page's index, and the Arlington walk's queue and
tallies. The second is that walk alone, which the four rules generated from the model share. The third is what a
caller validating a file it has not read pays: every object parsed for the first time, about 2.7 KB a page, no
stream's data read. Slice 3's first thirty-four rules took 716 µs and 308 KB on the first row, slice 2's twenty-one
214 µs and 8.9 KB, slice 1's one rule 86 ns and 232 B; the rows grow with each slice of M02.
Indexing costs roughly 200 bytes per object, whatever the objects weigh. The third row is asserted as a
budget in CI (`CorpusReadingTests`), so an allocation regression fails the build; it was 2.4 MB when M01 closed, and
M01's review measured 3.2 MB on 2026-10-01, under the 4 MB budget. A stream that ran out is
read twice to tell a lost checksum from lost data (T32), which costs time on damaged streams only; the
second reading keeps nothing. A stream whose checksum is wrong, or that turns corrupt, is read again after the fault
([#56]): its body as raw deflate, then, when that faults too, once more a byte at a time through the 8 KB piece of
input the fault was met in. A sound stream is read once, as it was, and allocates 8 bytes more for it (88,480 →
88,488 B at 64 KB); a medium job measured 93.5 → 88.8 µs at 64 KB and 10.0 → 8.85 ms at 4 MB, within noise, and the
25,157 sound streams of the corpus decode to the same bytes in a time within the spread of four alternating runs.
Damaged streams pay for the replay: the corrupt 64 KB stream took 54 µs to be left encoded and takes 671 µs to keep
its 32 KB; the byte-at-a-time part costs about 0.3 µs an input byte, 8 KB at most per reading of a form of the data,
so an 8,013-byte stream that faults at its end takes 1.8 to 2.5 ms instead of 64 µs, and a file made of nothing but
small corrupt streams reads about 40 times slower than before — still in time linear in its size. The damaged streams
of the corpus take 43 → 60 ms in all. What a 4 MB decode allocates is the output doubling towards its size, T28's and
M23's business. The last two rows are [#55]'s (`StreamLengthBenchmarks`, medium job, before the check and with
it). A stream past the 8 KB window is checked by asking the file for the 64 bytes after its declared length.
From memory that is a copy, and costs a valid document nothing measurable: 4.4 ms before, 3.9 ms with it,
2.02 MB both. From a file, the way the library recommends opening a large document, it is one read more for each
stream: 6.4 → 7.1 ms for the thousand, about 0.7 µs a stream, allocating nothing more. A length Distiller 3
wrote two bytes too long sends the reader searching the 8 KB of data past the window for each stream —
4.1 → 6.4 ms from memory, 6.8 → 10.3 ms from a file, 2.02 → 2.82 MB for a report and a record each; looking for an
object header in what it reads as well costs about 0.6 µs more a stream from memory and 1.4 µs from a file — 6.7 →
7.3 ms and 11.2 → 12.5 ms, the means of two medium runs of each alternated, the same allocation; carrying what a
read cuts of a header into the next, and the first read sized from what the parser holds, cost nothing measurable
beside it, 7.1 → 7.0 ms and 11.4 → 11.1 ms measured the same way. Data made of near-headers costs the search more
than data that holds none: 16 MB of ` obj` over and over take about 63 ms to search to the end, of `0 0 obj ` 44 ms,
of letters 10 ms — some 4 ms a megabyte searched at worst, and, the budget letting a document's searches read about
five times the file, some 20 ms a megabyte of file. Inside the window the check is what it was, but a wrong length
now costs its record and its mark of one report: about 440 bytes more allocated per stream (the 4 KB streams,
2.20 → 2.64 MB), and about 180 bytes kept for the life of the document — 50,000 such streams, read with a cache of
64 objects, keep 9.1 MB more. A sound stream allocates and keeps nothing more. The 4 KB streams take 3.3 → 3.5 ms
from memory and 5.2 → 5.3 ms from a file, 3.7 → 3.9 and 6.0 → 6.0 ms when each length is wrong, within the
measurement's error.

## Next concrete step

M02 — document validation (`docs/milestones/M02.md`), slices 1 to 3 done. Its progress is the
[M02 milestone](https://github.com/AdCodicem/AdCodicem.Pdf/milestone/3) on GitHub, which closes when every issue
filed under it has; the maintainer settled on 2026-09-29 that each is paid in M02 rather than moved, [#123] aside.
One pull request per batch, each design question put to the maintainer after measuring, in this order:

1. The review of 2026-09-29 recorded, the specification brought up to date, the two [#47] documents held to every
   test but the laziness one, and the damaged trees' page counts held to qpdf — done, merged with [#133].
2. [#55] and [#120], one path in `PdfObjectParser.ReadStream` — done, merged with [#140] on 2026-09-30: a stream's
   `/Length` checked past the parser's window, and a `/Length` that gives no length said as the file wrote it.
3. [#56], with [#134] — done, merged with [#147] on 2026-09-30 (journal of 2026-09-30): what a damaged Flate stream
   decoded is kept, and a wrong checksum over whole data reported as `filter.checksum-mismatch`. The `Remote corpus`
   run [#134] asks for on the branch, run 17, was green; it closed with the merge.
4. The reader and validation debts, in thirty-two batches settled with the maintainer on 2026-10-01 (journal of
   2026-10-01). The ten agreed on 2026-09-29, with [#141] and [#144] placed on 2026-09-30, keep their order; the
   twenty-five the threat model filed under M02 and the nine of M01's review join them where they share code or where
   their dependencies let them, the denial-of-service debts brought forward. A batch is one pull request; its design
   questions are put to the maintainer when it starts, after measuring; one that can move what a remote document gives
   runs `Remote corpus` on its branch before it merges; [#158]'s tests are written in the batch that changes their
   code, and it closes last.
   1. [#117] — done, merged with [#203] on 2026-10-01, `Remote corpus` run 19 green on its branch (journal of
      2026-10-01).
   2. [#187], the reader's codes and positions, so that the batches after it report under their final codes.
   3. [#159], what a message quotes of the file, bounded and escaped, before any batch adds a message that quotes it.
   4. [#193], the memory and time budgets the index changes are measured against.
   5. [#157] and [#186], numbers read from the file; [#186] blocks M03's slice 1.
   6. [#182], a cross-reference stream's dictionary read as written.
   7. [#118].
   8. [#119] and [#172].
   9. [#125] and [#126].
   10. [#190].
   11. [#164] and [#183], the index's size, the first new guard.
   12. [#165] and [#166], filter chains, with [#199].
   13. [#128].
   14. [#129] and [#175].
   15. [#160] and [#161], object-stream members, with [#200] and [#202].
   16. [#174] and [#170], stream data.
   17. [#155].
   18. [#167] and [#168], with [#201].
   19. [#188], [#197] and [#156].
   20. [#189].
   21. [#154] and [#171].
   22. [#132].
   23. [#141].
   24. [#144] and [#169].
   25. [#181].
   26. [#162].
   27. [#163], `MaxDepth` and `MaxNestedLoads` made guards, as the maintainer decided.
   28. [#185].
   29. [#173].
   30. [#192].
   31. [#107] and [#111], each a new public rule whose name and severity the maintainer gives.
   32. What remains of [#158].

   Slice 4 waits on [#141] and [#144]. The four defects the planning found outside every issue, [#199] to [#202], are
   filed under M02 and paid in the batches above (journal of 2026-10-01).
5. Slice 4 ([#60]) in two pull requests, streams then fonts, after the decisions it waits on: the severity of a font
   that is not embedded, the standard 14's aliases, where text is "meant to be extractable", how the rules that need
   content are left out and shown so, and the tools that referee both families.
6. Slice 5 ([#61]) in three: security and the trailer's `/ID`, annotations and destinations, metadata — with
   PDFDocEncoding ([#36]) and an XMP reader, or the `/Info`–XMP check moved, to settle first.
7. Slice 6 ([#62]): the report's JSON, its schema and documentation, the budgets restated.
8. The threat model's first version, `docs/threat-model.md`, for the reader and the validator ([#135]) — done on
   2026-10-01 (journal of 2026-10-01).
9. M01's review after the fact ([#136]) — done on 2026-10-01 (journal of 2026-10-01, `docs/reviews/M01.md`). The
   nine issues it filed under M02, [#185] to [#190], [#192], [#193] and [#197], are among step 4's batches; [#186]
   comes before M03's slice 1, which it blocks.
10. M02's review, slice 7 ([#137]), once every other issue filed under M02 is closed: a fresh session,
   `/milestone-review M02`; then whatever it files under M02.
11. The closing: a green `Remote corpus` run recorded here, M02.md's exit criteria ticked, the roadmap and
   `features.json` set to done.

A stream, object or section the reader cut at one of its limits (`limit.*`, ADR 34) is the reader's limit,
not a fault of the file: the rules on it report at most, as information, that it was not checked whole.

## Journal

### 2026-10-01 — Step 4's order, and its first batch: a reference to object 0 ([#117])
- **The question.** The maintainer asked for the next step to begin and for every ambiguity to be put to them. Seven
  analysts read the 46 debts filed under M02 against the code at `8c54490`, a planner grouped them, and a critic
  checked the grouping; none of the 46 was stale or already fixed. The critic moved three batches before the work
  that rests on them, [#187], [#159] and [#193], and [#182] before the index changes. It also recommended putting
  each batch's questions when that batch starts, after measuring, rather than all 113 at once.
- **Settled with the maintainer**, each as recommended:
  - the thirty-four debts filed since 2026-09-29 join the twelve already in step 4, interleaved where they share code
    or where their dependencies let them, the denial-of-service debts brought forward: thirty-two batches, listed in
    *Next concrete step*;
  - batches by code path, one pull request each; this session pays the first alone;
  - a batch's design questions are put when it starts, after measuring;
  - a batch that can move what a remote document gives runs `Remote corpus` on its branch before it merges;
  - [#158]'s tests are written, as `Refs #158`, in the batch that changes their code, and [#158] closes last;
  - a defect found outside every issue is reproduced, then filed as debt;
  - for [#117], both of the validator's "no reference" sentinels are replaced.
- **[#117].** The parser made a reference only of an object number above 0, so `0 0 R` read as two integers and a
  stray `R`: an array gained values, a dictionary's keys shifted, and `syntax.unexpected-token` was reported. It is
  now a reference to object 0, which heads the free list and is never in use (ISO 32000-1, 7.5.4), and reads as null
  (7.3.10). The reader holds object 0 missing whatever the table's first row says. `ObjectGraph`'s search for the
  first missing reference says whether it found one apart from what it names, the page tree walk tells a kid by its
  being a reference, and the object stream dependency walk skips object 0, which no stream can need. A `/Length 0 0 R`
  reads as "names object 0 0, which the file lacks", and `/Root 0 0 R` as a reference to it, the trailer whole.
- **Measured.** Over the 401 corpus documents this container holds — 168 committed, 233 of the 242 remote ones
  fetched —, the baseline and the change give the same findings and diagnostics but on two of JHOVE's error files,
  `jhove-hul-2` and `jhove-hul-28`: an `object.value-type-wrong` becomes the `object.reference-missing` at `/AP/N` and
  `/Contents[4]`, and their `syntax.unexpected-token` reports go. Their rule-id sets are unchanged; hul-28 is tagged
  `reference-to-object-zero`, as hul-2 was. qpdf, too, ignores hul-28's `/Contents[4]` as no stream. No committed
  document writes a reference to object 0.
- **Reviewed.** An adversarial review over four lenses gave fifteen findings, each put to a refuter; none was refuted.
  They came to seven: a false `xref.object-stream-circular` when a decoding key names `0 0 R` and the table places
  object 0 in that stream, fixed; no test of the lower bound against a negative number that narrows to a real one,
  `-4294967291 0 R` to 5, added; `/Root 0 0 R`'s message and location, now held; and four in this file — the batch
  list's numbering, this entry, the `Remote corpus` run, and the date of the twelve debts' order —, corrected.
- **Tests**: sixteen new, each failing when the defense it names is removed — the parser's bounds on both sides and the
  generation's, the presence of object 0, the two sentinels, the dependency walk's skip. #158's four reference forms,
  with the two negative ones, are among them.
- **Tracking.** [#144] blocks slice 4 ([#60]), as M02 says; [#137] waits on every issue under M02, the threat model's
  twenty-five, [#141], [#144] and [#199] to [#202] added; [#107]'s body and two documents say iPRES `t03-008`'s last
  row is two digits short, eighteen bytes, where they said one. Of the six defects the planning found outside every
  issue, four were reproduced twice and filed under M02, each with its batch: [#199] (a step after one the guard cut
  reports a false `filter.failed`), [#200] (object-stream members one byte apart, quadratic: 844 KB read in 59 s),
  [#201] (the dependency walk again for every stream, the page tree walk's frames past the cache), [#202] (a member the
  reader gives up on, reported by no rule). The fifth, a cut and a loop that wrap negative, is what [#125]'s closing
  asks, and is commented there; the sixth, `2^63` read as `long.MinValue`, is [#157]'s.
- **Checked**: the solution builds with no warning, and `dotnet format` finds nothing; 2,504 unit tests, 3 skipped,
  4,176 with the remote corpus; the integration suite ran against qpdf in its container with the remote corpus,
  3,266 tests, 3,260 passed and 6 skipped where qpdf cannot walk a damaged document's pages, in 19 minutes; the
  patch's 21 measurable lines of `src/` are covered, every branch taken, on the committed corpus alone; the site
  builds, 196 pages. `Remote corpus` run 19, on the branch at `b7fb2c9`: its first attempt fetched 241 of the 242
  remote documents — web.archive.org refused ECan's Konica scan — and passed every test over them; the one rerun
  fetched all 242 and passed, 4,224 acceptance tests with 3 skipped and 3,329 referee checks with 6 skipped.
- **Left**: the journal passes a dozen entries, and `docs/status.md` asks that it be summarized; no session has done so
  yet. Asked by the maintainer how hard the remote job leans on web.archive.org — 7 of the 242 remote documents, about
  140 requests in a week, no 429 logged —, filed [#204], under no milestone: the fetcher ignores `Retry-After`, logs no
  failed attempt, and the job caches nothing between runs, a cache being the maintainer's call under ADR 32.
- **Next**: batch 2, [#187], its questions put when it starts.

### 2026-10-01 — M01's review after the fact ([#136])
- **What it is.** The adversarial review ADR 46 asks of every milestone, run on M01 after the fact, under M02, by a
  session that worked on none of it, as `docs/milestone-review.md` describes. It covers M01's fifteen commits,
  `c9d751f` to `1f90e8f`, and the reader as it stands on `main` at `2183466`. The report is
  [`docs/reviews/M01.md`](reviews/M01.md), indexed in `docs/reviews/README.md`.
- **Counts.** Pass 1, blind, gave 107 candidates. Pass 2 dropped 37 as justified or already filed, most of them by the
  threat model's debts of the day before, and merged the rest into 20 findings. Refutation refuted none and narrowed
  13 of the 20, and two findings were split so each part has one outcome, giving 22, 14 of them narrowed. A
  twenty-third surfaced after the triage, while an issue was checked. Of the 23: 9 fixed here, 11 filed under M02 in nine issues, 2 under later milestones,
  1 under none, none put to the maintainer.
- **Fixed in the review's pull request**:
  - each public type of the object model and the diagnostics in its own file (R-08);
  - analysis back at `latest-recommended`, as ADR 29 and `CLAUDE.md` say: six test findings fixed, and CA1720
    suppressed where it fires, with its reason (R-09);
  - the core's `InternalsVisibleTo` grant to the HTML assemblies removed, as ADR 36 reasons (R-10);
  - the tests M01 requires and lacked: deep dictionaries, exotic white space, `IsHexadecimal` and the `ToString`
    forms, the `damaged` use case, and the manifest's completeness over the whole corpus folder (R-16);
  - `M01.md`, the XML documentation, the site and the project documents brought in line with the code (R-14, R-18
    to R-20). The memory row above now reads 3.2 MB, as measured;
  - `docs.yml`'s checkout no longer keeps its token beside `pages: write` and `id-token: write` (R-21).

  The fixes are `refactor:`, `test:` and `docs:` commits, with `build:` for R-09 and R-10 and `ci:` for R-21. None of
  them starts a release.
- **Filed under M02**, which now waits on them:
  - [#185], a reference's generation is compared with nothing, so `5 1 R` reads `5 0 obj` with no report;
  - [#186], reals are not read as the nearest `double`. It blocks M03's slice 1;
  - [#187], codes raised outside their meaning, `object.redefined` never raised, positions that are not file offsets;
  - [#188], a relocated section that lands on one already read;
  - [#189], a rebuild keeping an older direct definition, and taking `10 0 objx` for a header;
  - [#190], `PdfDocument`'s version, stream position and behavior after `Dispose`;
  - [#192], LZW decoding allocating per code;
  - [#193], no memory budget for a large index, and no time budget for a corpus document's full read;
  - [#197], the nearby search reaching 1,024 bytes past an offset near the start of the file, found after the triage
    and filed as R-06's was.
- **Filed elsewhere**: [#191] under M03, before [#35] records the API baseline; [#194], the nightly fuzzing's
  repeated seeds, under M23; [#195], the corpus build's unhashed Python pins, under none.
- **Noticed outside the extent**: Dependabot's auto-merge fails open on an update it cannot classify, and no CI check
  gates the merge. It is filed as [#196], under none, at the maintainer's choice. It is not counted as a finding.
- **Decided by the maintainer**: the triage as proposed; [#186] and [#192] under M02 rather than M03 and M23;
  `M01.md`'s first acceptance row aligned with the manifest's floor rather than the test tightened.
- **Updated**: [#137]'s `Blocked by:` line names the nine issues filed under M02. The threat model's *Known gaps*
  name every issue the review filed but [#186] and [#187], which concern correctness and the diagnostics' contract,
  not a defense.
- **Checked**: the solution builds with no warning at `latest-recommended`; 2,488 unit tests, 2,485 passed and
  3 skipped; the site builds.
- **Next**: the reader and validation debts of step 4, with the nine this review added, then slices 4 to 6.

### 2026-10-01 — The threat model's first version ([#135])
- **What it is.** `docs/threat-model.md`, written against `13e06bb`. It covers:
  - assets, attackers and trust boundaries;
  - the defenses every surface shares;
  - a section for each surface the reader and the validator open. Each defense there names where it lives in the
    code, the test that fails if it goes, and whether the fuzzing reaches it;
  - what holds the defenses;
  - the package and its supply chain;
  - the twenty-nine surfaces still to come, each with the milestone that opens it and what its specification leaves
    open;
  - what is out of scope.

  It is in the project documents' sidebar.
- **How it was checked.** The surfaces were mapped by reading the code, and every test the document names was read.
  Every gap suspected was then reproduced against the Release build. The exceptions were traced in the code: those
  that need GitHub's runners, and those that need a file past 2 GB. Each gap was compared with the open and closed
  issues. What survived went into thirty issues, each with its shape, its measurement and what closes it. None is
  fixed here: #135 asks for them to be filed, not paid.
- **What it found**, the worst first:
  - **Files of a few hundred bytes that make the reader hold gigabytes**:
    - a `/Filter` array of RunLength steps, where 513 bytes allocate 3.3 GB as the file opens ([#166]);
    - a cross-reference stream of one-byte rows ([#164]);
    - decodes nested through filter parameters ([#165]);
    - overlapping streams, each keeping its own copy of the raw data ([#170]);
    - an object-stream member with no object bound ([#160]).
  - **Work beyond the file**:
    - object numbers chosen to collide ([#155]);
    - object streams decoded again for every member ([#181]);
    - the validator's quadratic probe and walk ([#168]).
  - **Wrong answers**:
    - encryption lost when the index is rebuilt ([#154]);
    - numbers that wrap to a value the file chose ([#157]);
    - a rebuild serving a header found inside stream data ([#171]);
    - filter parameters decoded wrong, in silence ([#162]);
    - a guard's report dropped once the diagnostics are full ([#169]).
  - **Untyped exceptions** from `Validate` and from enumerating `ObjectNumbers` ([#167]).
  - **Tests**: some hold less than their names claim, and the fuzzing never reaches a guard ([#158], [#176]).
  - **Supply chain**:
    - release jobs that build and test while holding the right to publish ([#177]);
    - nothing tying publishing to `main` ([#178]);
    - a dependency-free core held by review alone ([#179]).
- **Fixed in passing**:
  - Four internal constants now give, where they are declared, the reason ADR 34 asks for: `HeaderSearchLength`,
    `TailSearchLength`, `NearbySearchRadius` and `LzwFilter.MaxCodes`.
  - `SECURITY.md` no longer calls the dependency versions locked; [#43] is open.
  - The reader-limits reference no longer says decoded object streams are unbounded.
  - The security bullet of `docs/architecture.md` states its rule as an aim.
- **Decided by the maintainer**:
  - The reader's and the validator's debts go under M02, not M23.
  - `MaxDepth` and `MaxNestedLoads` become guards, since a valid file can exceed both ([#163]).
  - Proposed and accepted:
    - the fuzzing gaps go under M23, beside its coverage-guided fuzzing ([#176]);
    - the pooled buffers go under M16, where decryption brings secrets into them ([#180]);
    - the three supply-chain issues go under no milestone.
- **Comments**:
  - On [#37]: a cached object weighs about thirty times its syntax (a 15 MB array retained 483 MB), and a cached
    stream keeps its raw data.
  - On [#53]: a length between `Array.MaxLength` and `int.MaxValue` throws an `OutOfMemoryException`.
- **Next**: M01's review after the fact ([#136]), in a fresh session, with `/milestone-review M01`. The review
  checks the code against this document.

### 2026-09-30 — The user documentation organized along Diátaxis ([#148])
- **The question.** The maintainer asked for the documentation to follow [Diátaxis](https://diataxis.fr/), and to be
  asked whatever it took to leave nothing ambiguous. Each choice below was put to them and settled, and recorded as
  [ADR 47](adr/0047-the-user-documentation-follows-diataxis.md).
  - **Scope**: the user documentation, `docs/website/docs`. The project documents stay as they are.
  - **Sections**: `tutorials/`, `guides/`, `reference/` and `concepts/` — the names the milestones already used —,
    shown as *Tutorials*, *How-to guides*, *Reference* and *Explanation*. The introduction and the feature comparison
    stay first, outside them, and the introduction's code goes to the tutorial.
  - **One mode per page**: the four *Concepts* pages were split, not moved; the parts link to each other.
  - **A tutorial held to its output**: it writes a one-page PDF, opens it, cuts it short and validates both. Its code
    is `samples/FirstSteps`, and `FirstStepsTutorialTests` requires every C# block of the page in the sample and every
    output it shows to be what the sample prints. Other pages' snippets are not compiled.
  - **Four how-to guides**: what the reader repaired, validating a received document, limits (raise or refuse), the
    reader's memory.
  - **The rules table moved** into the reference, so that it is versioned with each release;
    `ValidationRuleIdTests` reads it there, and every document that cited it cites its new path. The API reference
    moved under `reference/api`. `@docusaurus/plugin-client-redirects` keeps `/api/*` and `/project/validation-rules`
    working in the built site.
  - **The framework follows**: `CLAUDE.md`, `CONTRIBUTING.md`, the milestone template, point 5 of the definition of
    done, and the *Documentation* sections of M03 to M31 remapped to the sections — codes to
    `reference/diagnostics.md`, the tool's pages to `reference/tool/` and a guide to install it, the planned pages
    that mixed modes split, a tutorial planned for M06 and M12 — with an exit criterion in each. M02's section names
    the pages it now has.
  - **Tracking**: [#148], outside any milestone, closed by this pull request.
- **Found on the way.** `PdfDocument.Open(Stream)` copies every stream but a `MemoryStream` into memory, a seekable one
  included, while its XML documentation says only a non-seekable one is. The memory guide says what the code does;
  filed as [#149], its milestone the maintainer's to choose. The tutorial's first minimal PDF had a page without
  `/Resources`, which `page-tree.resources-missing` reported: the validator caught the documentation's own mistake.
- **Checked**: the solution builds with no warning; 2,474 unit tests pass; the site builds, 193 pages, with no broken
  link and nothing `check-site.mjs` refuses, and the redirect pages point at the new addresses. Code coverage runs the
  sample too, so `codecov.yml` leaves `samples/**` out, as it leaves `tests/**`: neither is shipped.

### 2026-09-30 — What a damaged Flate stream decoded is kept ([#56]), and the Flate referee reads every diagnostic ([#134])
- **The question.** The framework's inflater throws from the read that meets a fault, and loses what that read decoded
  from the last 8 KB of input it was handed — not "up to 64 KB", as the message said. Of the corpus's 26,578 Flate
  streams, 272 are not whole: 86 decode to their end under a wrong Adler-32, 2 lose part of their checksum, 47 lose
  their tail, and 137 turn corrupt partway. 72 of the first and 135 of the last were left encoded, as "could not be
  decoded", and 16 others lost a prefix: 1,441,772 bytes in all. Three independent measurements — every Flate stream
  against Python's zlib and libz through ctypes, prototypes of four ways to keep what decoded, qpdf's output — were
  reconciled; where they disagreed, the bytes settled it: feeding the inflater one *input* byte per read keeps exactly
  what libz keeps, asking it for one *output* byte per read loses one and costs 61 to 67 times as much on hostile data.
- **Settled with the maintainer**, one question at a time after the measurement:
  - Data that faults is read again, only after the fault. A zlib body behind a plain header (CM 8, CINFO ≤ 7, FCHECK,
    no FDICT) is read as raw deflate at full speed; when that reads to its end, only the checksum disagreed, and all
    of it is kept. Otherwise, and for raw deflate that faults, it is read once more, at full speed up to the 8 KB
    piece of input the fault was met in, then one input byte at a time through that piece; each reading adds only
    what the ones before did not keep. A managed inflater (450 to 600 lines, a second decoder of hostile data in the
    core) and smaller reads everywhere (2.5 to 3.9 times the cost of every sound stream) were turned down.
  - A new public code, `filter.checksum-mismatch`, a warning: "A Flate stream's checksum disagrees with the {L} bytes
    its data decoded to; all were kept, and some may be wrong." It keeps the `filter.*` family, which M21's safeguard
    reads before re-encoding a stream. The data is kept whole, as qpdf keeps it; none of the 86 could be shown
    intact, and 39 were shown damaged, 28 of them by a change of line endings that, undone, makes the checksum agree.
  - Corrupt data stays `filter.failed`, a warning: "A Flate stream is corrupt at byte {N} of its {E}; the {M} bytes
    decoded before the fault was found were kept." N counts from 1 in the data the Flate filter was given, M before any
    predictor. Data in which nothing decodes before its fault is still left encoded, as one that could not be decoded.
  - [#134] went with it; qpdf referees both reports; a header that asks for a preset dictionary is not read again —
    none in the corpus, and qpdf fails on it too.
- **No new bound** (ADR 34): at most three readings per form of the data, all into one output under
  `MaxDecodedStreamLength`, and at most one piece read a byte at a time, `FlateInput.ChunkLength` — the 8 KB the
  framework's inflater asks for at once, now also the most `FlateInput` hands over, so that the piece stays one piece
  whatever a later runtime asks. The reasoning is written where the code is.
- **Found on the way.** Raw deflate that starts with a byte PDF counts as white space — a line feed starts a block of
  fixed codes — decodes a byte or so from the white space and a zlib header after it before it faults. The old code
  lost that byte with the read, which let zlib after the white space be tried; kept now, it would have won. Zlib after
  the white space is now tried over a corrupt raw reading when a plain zlib header follows the white space, and taken
  when it reads at least as far; raw deflate that goes further is kept, a case built from real bytes in which the zlib
  reading faults after four bytes. What raw deflate kept is let go while zlib is read, and raw deflate read again when
  it went further, so that a stream holds one output at a time: at most eight readings, three pieces a byte at a time.
  Two older flaws seen then were filed rather than fixed ([#145], [#146], M23), and a third that slice 4 would meet:
  a damaged object stream reports its fault each time the reader decodes it ([#144]).
- **Measured on the whole corpus**, 381 documents, every Flate-first stream, against libz: the 272 damaged streams
  keep exactly what libz keeps. The 86 whose checksum is wrong keep all their data, 3,361,116 bytes where 2,073,060
  were kept; 98 corrupt streams keep 342,784 bytes where 189,068 were, reported at the byte libz meets the fault in,
  98 of 98; the 39 in which nothing decodes stay encoded; the 49 others are unchanged, and the 25,157 sound streams
  decode to the same bytes. 19 documents, 7 of them committed, now require `filter.checksum-mismatch`: the
  expectation comes from zlib's "incorrect data check" on their streams, not from the library.
- **Held to qpdf.** A new theory of `FlateRefereeTests` asks qpdf for each such stream's data
  (`--show-object=N,G --filtered-stream-data --decode-level=specialized`): the 86 equal ours, qpdf warning of nothing
  but the file's structure beside them; of the 98 corrupt ones qpdf serves a prefix of ours — nothing for 96, 65,536
  bytes for two — and reports an error decoding each. `QpdfIndex` picks the entry placing the copy the reader read,
  since a rebuilt index lists groff's object 304, and NUREG's 284 and 285, under two generations;
  `StreamLengthRefereeTests` uses it too. qpdf keeps nothing of the three committed documents' corrupt streams, so
  `CorpusReadingTests` holds those three to libz's figures. [#134]: the cut-short theory reads every diagnostic; the
  default capacity of 1,000 dropped the later reports of IBM's QMF manual. The integration suite ran in this container
  for the first time: `dockerd` starts, and the Alpine image the referee uses trusts the session proxy's certificate
  once the proxy's CA bundle is appended to `/etc/ssl/certs/ca-certificates.crt` — a local image tagged
  `alpine:3.21`, built in the session, which Testcontainers then uses; nothing of it is committed.
- **Cost**, under *Current measurements*: a sound stream is read once, as before; a damaged one pays for the replay,
  a file of nothing but small corrupt streams about 40 times what it did, linear in its size — a point for the threat
  model ([#135]).
- **Tests**: 4,144 unit with the remote corpus, 2,472 without — seeded FsCheck properties over data, levels, forms and
  fault positions; sound data read once; only the piece the fault was met in read a byte at a time; allocation, bound
  and throw-on-limit for each damage; counts before a predictor; a fault in the part a guard kept — and every changed
  line of `src/` covered. An adversarial review over five lenses found 23 points; 21 survived their refuters and were
  fixed, one of them — the order in which the forms are tried — by keeping the order and recording M23's constraint.
- **Left, filed**: [#141] (ASCII85, ASCIIHex and RunLength skip in silence; M02, blocks [#60]), [#142] (a repair the
  checksum guides; M05), [#143] (a checksum cut short is not checked against its bytes; M05), [#144] (M02), [#145] and
  [#146] (M23).

### 2026-09-29 — A stream's length checked past the window ([#55]), and said as the file wrote it ([#120])
- **The question.** A stream whose data ran past the 8 KB window the reader parses an object through had its `/Length`
  taken as declared, and a wrong one cut the data short or took in what followed, in silence; a `/Length` that gave no
  length was reported as "-1 bytes". Measured first on the whole corpus by three independent views, then settled with
  the maintainer: ask the file whether `endstream` follows the declared length; when it does not, take the first
  `endstream` after the data's start, searched no further than the nearer of the next object the index as the file
  wrote it places — the one rebuilt as the document opened when the file wrote none — and the first object header the
  file's bytes hold (settled after the review, below), or the end of the file, and keep the declared length when there
  is none; inside the window, what is read does not change. The bound is no guard (ADR 34): a valid file's `endstream`
  follows its length, as it does for the 35,873 confirmed streams of the corpus, so only a damaged file is searched.
- **The reader.** `PdfObjectParser` asks its provider for the 64 bytes after a declared length past the window — which
  also say whether `endobj` follows the `endstream`, so that such an object's end is seen at last —, and for a search
  when they hold no `endstream`: the window's part first, then the file through windows the source lends, always
  advancing, allocating nothing per call. Each index keeps its offsets sorted once a search asks — sorted whole, in
  place, the first time, then in runs merged as a binary counter carries —, so that a lookup costs a logarithm of the
  index and one it grows between lookups costs what it adds (`SortedOffsets`). The stream's own entry is no next
  object: an entry that missed its object may place it inside its own data. The search runs once per stream — its
  result is kept by where the data starts —, and a stream is reported once however often it is parsed again, after the
  cache let it go or a rebuild: the mark is set when a reading is kept, so a report a dropped reading made is made
  again by the next. Inside the window too: PDFBox's zeroed object stream was reported once more each time it was
  parsed. Nothing a read changes bounds the search. The index as written does not change once the document has opened
  — the chain's is copied before the reader corrects or rebuilds its own, and one rebuilt as the document opened loads
  every object it places as it opens, so any correction is made then —, nor do the file's bytes, where the search
  stops at the first object header too: `N G obj` at a token boundary, a number and a generation whose values the
  parser takes, however many zeros lead them, and white space between the tokens, however much. It is looked for in
  the same reads as the `endstream`, before it; each read repeats 10 bytes of the one before and carries into it what
  that one cut of a header — the runs of white space and digits it ends with, as a few bytes that decide every header
  as they would —, and the reads grow from twice what the parser holds of the data, 16 KB under the default window, to
  64 KB, so that a search stopped by a header it could not know of reads at most about twice what its object spans,
  whatever window the object was parsed through. No search starts once the searches of a document have read four
  times the file (`EndStreamSearchPasses`, no guard under ADR 34): those of objects whose headers follow one another
  read disjoint stretches, twice the file at most, and only objects that overlap — one's header inside another's
  dictionary —, or a header the search does not take — a regular character glued before its number — that the index
  as written does not place, share a stretch each of their searches would read again; past the bound, a stream keeps
  its declared length without a search, and says so. The `/Length` is read for its form — absent, not an integer, out of range, a reference to an object the file lacks or that could not
  be read (a cycle, an object stream being decoded), or to a non-integer —, and each form has its message; a stream
  `endobj` follows without an `endstream` no longer says it ran past the end of the file. What the reader found of
  each stream whose length is not confirmed is recorded per object, beside `endobj` and the limits
  (`TryGetStreamLengthFault`), for slice 4's length rule; a sound file records nothing.
- **Measured on the whole corpus** (410 documents, the 233 remote ones fetched), before and after, every object read,
  then the validator: `stream.length-invalid` 81 → 96 reports, seven documents changed. The count before depended on
  how often a stream was parsed — PDFBox's zeroed object stream was reported two to five times in the readings the
  reviews made —, where each stream is now reported once. Of the sixteen streams of six documents whose length is
  wrong past the window, fifteen of five documents now read to their `endstream`: DEA-CFR's objects 10, 13 and 64,
  IBM's QMF manual's 93 and 169, the ORNL PowerPoint's 144, SAMHSA's 27, 30, 35, 45, 50, 55, 60 and 72, and the Atypon
  article's 49, 1,647 bytes where it declares 62,065. The sixteenth, PDFium's object 695, keeps its 202,154 declared
  bytes and says no `endstream` follows them before object 696, which the index as written places 21 bytes past them.
  The twelve reports of [#120] — iPRES's `61.5` and missing `/Length`, the tiff2pdf scans' `/Length` naming an image or
  the linearization dictionary — now say what the file wrote. `stream.truncated` is unchanged in number; iPRES's
  missing `endstream` says what it takes. No finding changed: no `object.endobj-missing` appeared with what the reader
  now sees past the window. The review's fixes changed none of this: the survey after them is identical, bytes read too.
- **Held to qpdf.** `StreamLengthRefereeTests` compares the length taken for every stream reported with qpdf's
  `--show-object --raw-stream-data`, one end-of-line off qpdf's, every diagnostic kept — IBM's manual reports 4,013
  relocated objects before two of its streams, which the default capacity of 1,000 dropped. Run here with qpdf 11.9.0
  outside a container, as the test would: 85 streams compared, all agreeing but PDFium's 695, named with its reason —
  qpdf takes object 778's `endstream`, 3.3 MB on. The Atypon article's 49 is not compared, qpdf reading another copy
  of it; PDFBox's 417 agrees where both take object 441's `endstream`, the search inside the window being unbounded by
  the maintainer's choice. `FlateRefereeTests` loses its one known exception, SAMHSA's object 27. Two documents now
  require `stream.length-invalid` in the manifest: the Atypon article and PDFium's report, whose only length fault is
  past the window.
- **Cost**, open and every object read from a file, counted: the 1000-page journal reads the same 16,547,278 bytes in
  the same 2,011 reads, no stream of it running past the window; DEA-CFR 38,505 bytes more (992,128 → 1,030,633) in 9
  more reads (95 → 104), IBM's manual 168,962 more of 73.0 MB in 16 more (12,399 → 12,415), SAMHSA 63,114 more in 24
  more (96 → 120), PDFium's report 215,586 more of 61.8 MB in 337 more (1,467 → 1,804). The times are within this
  shared machine's noise: least of 40 runs over four alternating series, the journal took 6.6 to 7.6 ms before and 6.0
  to 7.0 after, IBM's manual 20.1 to 21.9 and 21.0 to 21.5; SAMHSA's and PDFium's moved either way from one series to
  the next. `StreamLengthBenchmarks` is in *Current measurements*, from memory and from a file.
- **The review.** Four reviews of the branch — the corpus, the invariants against hostile files, the cost, the tests
  and documents — found the reader right on the corpus, and these, fixed before it merges:
  - The searches were bounded one by one, not in total: streams whose objects overlap each searched the same stretch
    to the end of the file — 50 nested streams in a 20 MB file read 1.0 GB, 200 of them 4.0 GB. Now 100 MB, and
    102 MB, in 0.1 s: the bound above.
  - A stream relocated from an entry inside its own data was bounded by that entry, and reported "no `endstream`
    before the next object" of itself; its own entries are now stepped over.
  - The first search on a large index sorted its offsets through a list and two copies: a million entries allocated
    32.8 MB and kept 16.4 MB more. Now it allocates and keeps 8.0 MB: the offsets once, sorted in place.
  - The benchmark opened its document from memory only, where the new read is a copy; from a file it is a read of its
    own, which costs a valid document about 0.7 µs a stream, not nothing.
  - The next object bounds only a declared length past the window; the user documentation said it bounded a `/Length`
    that gives none, or a declared length inside the window, which take the first `endstream` wherever it lies, as
    before. It now says what the code does.
  - The header's offset in the bound, the edge between two reads of the search, an `endstream` glued to what follows,
    and what follows an `endstream` the search found had no test that failed when they were broken; they have.
  - The bound took the nearer of the index as written and the index the reader reads with, which a late rebuild or a
    corrected entry changes: a stream searched before and one searched after were bounded differently, and a test
    pinned a stream that kept 24,976 bytes read after the rebuild and 24,620 read before. Measured with the index as
    written alone — or, when the file wrote none, the one rebuilt as it opened —, every object read in seven orders
    (by number, reversed, three seeded shuffles, the first two again with a cache of one object) and, for the eight
    documents whose index a read rebuilds, two more (the object that rebuilds it first, and last): every stream of the
    corpus takes the same length in every order, before the change as after it, and the survey — lengths, `stream.*`
    reports, findings — is identical. What changed is where IBM's QMF manual bounds two searches: at the next object
    as its entry places it, one to three bytes into the header, rather than at the offset reading that object
    corrects when it happened to be read first; both find their `endstream` before either. The Atypon article serves
    another copy of seven objects, its 49 among them, when they are read before its index is rebuilt — the chain's —
    than after — the rebuild's last definition —, as before; each copy takes the same length in every order. No index
    rebuilt as a document opened changed after it opened, in any order, nor searched a stream after: 84 documents.
  - An adversarial review of that bound found it let a stream only a rebuilt index places run into the objects after
    it, where the branch's first bound, the index the reader reads with, had stopped it at the next object the rebuild
    found. On its file C2 — 29 streams that lost their `endstream`, which the index as written marks free and only a
    rebuild places, one a later read makes, then a stream the index as written places after them, whose `/Length` is
    100 bytes too long —, each of the 29 ran to the first `endstream` after them all, the first taking 713,981 bytes;
    the searches overlapped and spent the document's budget, and whether the last stream was searched — whether it
    held 30,000 bytes or 30,100 — depended on whether it was read before the rebuild. The maintainer settled it on
    2026-09-29: keep the index as written, and stop the search at the first object header the file's bytes hold as
    well, which no read changes. Found on the way: reading the file 64 KB at a time, a search stopped by a header read
    the whole 64 KB for each stream, and C2 with streams of 10,000 bytes spent the budget again; the reads now grow
    from twice what the parser holds of the data.
  - Measured against the stable bound alone, on the 401 documents present, every object read, then the validator:
    lengths taken, `stream.*` reports and their messages, what the reader records per stream, and findings are
    identical, and the sixteen streams keep their lengths — PDFium's 695 its 202,154 bytes, before the object at
    20,103,524 whose header a block of zeros erased. The 2,823 runs of the nine orders are identical too, and no
    stream of the corpus takes two lengths. The searches read 0.35 of a file at most (SAMHSA's fact sheet, 62,100
    bytes of 180,381, measured with the second review's fixes below). C2 and four variants of it —
    400,000 bytes after, `obj<<` and CR LF, every other `endstream` kept, streams of 10,000 bytes — hold the same data
    in all nine orders, each stream stopping at the next header, and their searches read the file less than twice over
    (1.5 to 1.9 times), where four of them read it more than four times over and gave up. A searched stream pays for
    the header it looks for: about 0.6 µs more from memory, 1.4 µs from a file (*Current measurements*).
  - The same review found the documents saying more than holds: that a stream holds the same data whatever was read
    before it, and that only objects that overlap reach the budget. Crafted files still make what a search keeps
    depend on the order. An entry that points into a stream's own data makes the number the stream is first read under
    step over a different entry (the review's file D), and the rebuild's scan for trailers parses a stream under
    number 0 (file F): both predate the stable bound, and are [#138], filed under M23. Its files A and B, whose entry
    one byte into `169 0 obj` reads it as `69 0 obj`, now hold the same data in every order, the header the bytes hold
    stopping both numbers' searches first. The budget is reached by objects that overlap, and by headers the search
    does not take that no index as written places: C2 with a regular character glued before each header reads the file
    4.4 times, and which streams go unsearched depends on the order, recorded on [#138] too. The code's remarks,
    `architecture.md`, M02 and the user documentation now say what holds.
  - A second review found the header stop narrower than the parser. It took at most 16 bytes of white space between
    the tokens and a generation of five digits, where the rebuild's scan and the parser take any: C2 with 17 spaces,
    17 NULs or `000000` in each header ran each stream to the last `endstream` again, the searches read the file 4.4
    times over, and object 50 held 30,000 bytes or 30,100 according to the order, where the branch's first bound had
    kept all three stable. The rule now takes a number and a generation by their values, however many zeros lead them,
    and white space however long: each read of the search carries into the next what it cut of a header, as the few
    bytes that stand for the runs of white space and digits it ends with — one space for a run of white space, the
    digits of its value for a run of digits —, and the reads repeat only the 10 bytes an `endstream` and its
    end-of-line need. The three files hold the same data in both orders, their searches reading 1.9 times the file,
    and 30 or 200 streams whose 17-space headers only a rebuild places, read three times over with a cache of one
    object, are each searched once, reading 1.9 and 2.0 times the file. The review also found the first read 16 KB
    whatever window the object was parsed through: with `MaxObjectLength` at 1 KB, C2 with streams of 1,500 bytes
    read the file four times over and depended on the order. The first read is now twice what the parser holds of the
    data, and the same file reads 1.1 times the file, the same data in both orders. The rule still leaves out a
    comment between the tokens, which the parser takes and the rebuild's scan does not, so that no stream only a
    rebuilt index places has one: confirmed by the maintainer on 2026-09-30, the two rules to move together. Measured again, the survey of the 401 documents present
    and the 2,823 runs of the nine orders are identical to the header stop's, the searches reading 0.344 of a file at
    most; the review's crafted files are as they were — A, B, C, C2 and its variants stable; D, F and C2 with a regular
    character glued before each header still depending on the order ([#138]). The documents said more than was
    measured, and now say what was: 401 documents of 410, each stream the same length in every order, though a
    damaged index serves the Atypon article's 49 from another copy according to what was read first; and the budget
    as a third way a crafted file makes a search depend on the order.
- **Tests**: 141 unit tests on synthetic files, the theory over the remote streams counted as the row it skips without
  them — every case above, the window's edge, a stream holding an embedded PDF's `endstream`, the written index
  against the rebuilt one, the end of the file as the bound, junk before the header, the edge between two reads of the
  search, each form of `/Length`, the record, one report and one search after the cache let the stream go and after a
  rebuild, the same length whatever was read first — before or after a rebuild, a corrected entry, an entry corrected
  while the chain was read —, the first object header the bytes hold before the next object the index as written
  places, 31 shapes of text that are a header or only look like one, the start and the end of the search as a header's
  boundaries, headers and near-headers across the edges of the window and of the first two reads, headers whose runs
  of white space or digits span whole reads, C2 in both orders with streams of 24 KB and of 10,000 bytes, with headers
  of 17 spaces, 17 NULs or a generation of six digits, and with streams of 1,500 bytes read through a window of 1 KB,
  an index rebuilt as the document opened that nothing read after changes, a report a dropped reading made, and
  hostile values: an offset past the file, before the data or inside the stream's own data, a million entries, 16 MB
  of data without `endstream` searched with nothing allocated for it, streams that share a stretch, a source that
  shrinks —, DEA-CFR's three streams pinned, and a theory over the remote streams. The test pinned in both orders now
  keeps the declared length in both, object 7's header stopping the search, and the search's reads are counted as one
  search however many it makes. Every line the patch changes in `src/` is covered on the committed corpus, 354 of
  them, as Codecov counts it.
- **Found on the way**: SAMHSA's object 27, now read whole, has a wrong Adler-32 checksum; the reader takes that for
  corruption and loses up to 64 KB decoded before it — 126,219 bytes kept where the data holds 174,803, and 171,005 were
  kept while it read 26 bytes short. [#56], next, keeps them; the case is named on it.
### 2026-09-29 — Every milestone ends with an adversarial review
- **The question.** The maintainer asked for a final step to every milestone, one that checks the choices its
  sessions made are consistent with each other. Each choice below was put to them and settled.
- **Settled, and recorded as [ADR 46](adr/0046-every-milestone-ends-with-an-adversarial-review.md).**
  - The review is the milestone's last slice, labeled `slice` and `review`. It is blocked by every other issue under
    the milestone, and is point 7 of the definition of done.
  - It is run by a fresh session, which the maintainer starts with `/milestone-review Mxx`.
  - It reads in two passes: blind first (code, tests, specification), then informed (journal, issues, pull requests,
    commits).
  - Five axes: the consistency of decisions, within the milestone and with those already closed; the code against
    the specification; invariants 1 to 12; the public API and its documentation; and security (hostile input,
    attack surface, supply chain, threat model). The specifications of milestones to come are not reviewed.
  - Every finding is put to a sub-agent that tries to refute it.
  - Triage: an inconsistency or a broken invariant is fixed before the milestone closes — by the review itself when
    trivial, otherwise filed under the milestone. Anything else is debt under the milestone that pays it. A settled
    choice goes to the maintainer. The maintainer sees the triage before anything is filed.
  - The report goes in `docs/reviews/Mxx.md`.
  - An XL milestone is reviewed once per sub-milestone, then once over the seams between them.
  - The threat model is a single document, `docs/threat-model.md`, completed by each milestone that opens a surface.
- **What changed.**
  - New: `docs/milestone-review.md`, the procedure and the report's form; `docs/reviews/`, published with the
    project documents; the `milestone-review` skill.
  - Point 7 of the definition of done, in `docs/roadmap.md`, and the review in the milestone template.
  - M02's slice 7 and its exit criteria.
  - `CLAUDE.md`: the review session's reading list, the threat model's, and the `review` label. That label and
    `area: security` were added to `.github/labels.json`.
- **Filed under M02.**
  - [#135]: the threat model's first version, for the reader and the validator, to be written by a session that
    reads the code.
  - [#136]: M01's review after the fact. The maintainer chose it so that M02's review finds a reader that has been
    through one.
  - [#137]: M02's review, slice 7, blocked by every other issue under M02, [#135] and [#136] among them.

### 2026-09-29 — What remains of M02, settled with the maintainer
- **The question.** What M02 still needs to be complete. The answer is an inventory of its open issues, exit
  criteria, acceptance rows and tests required. Eight readers checked it against the code, the corpus and CI, one
  area each, and a ninth tried to refute what they found and looked for what they missed. What remains is in *Next
  concrete step*.
- **Settled with the maintainer, one question at a time.**
  - Every issue filed under the milestone closes before it does, and each is paid in M02: [#107], [#111], [#125]
    and [#126] too, though no plan had named them.
  - [#123], a key newer than the version a file declares, moves to M20. A newer key still conforms, and it is a
    claim that bounds the version.
  - The [#47] skip is narrowed.
  - The Arlington overrides slice 3 made beyond the seven settled beforehand are confirmed, one by one against ISO
    32000-1's text, and so are the two candidates the text did not support (ADR 44, *Reviewed on 2026-09-29*).
  - The work goes one pull request per batch. Each design question goes to the maintainer after measuring.
- **The two [#47] documents.** Opening them reads more than a quarter of the file, which fails the laziness test and
  nothing else: with the marker lifted, every other acceptance test passes on them. Until now the marker made the
  five validation theories skip them too. An entry's new `unsupportedTests` names the tests its reason concerns,
  and only they skip the document. The schema requires the reason beside it, and a test fails on a name no test
  has. Both now name the laziness test alone.
- **What M02's rows name is now what the tests check.**
  - `Documents_waiting_for_M02_are_now_diagnosed` missed eleven of the documents the acceptance rows name: the two
    catalogs without `/Type`, and the iPRES file, cross-reference and object-shape cases. Each now earns its rule's
    finding by its tag.
  - `QpdfRefereeTests` counted pages on clean documents only; it now holds damaged ones too. It skips a tree qpdf
    cannot walk, and a catalog the reader chose (`file.root-invalid`), which qpdf does not look for. Run here with
    qpdf 11.9.0 on the damaged documents: 112 agree, five are trees qpdf cannot walk, and one is the Aspose file,
    whose stale trailer names `/Info` as `/Root`.
- **M02.md.**
  - Slice 3 is marked done.
  - The rule identifiers it gives as examples now exist.
  - Its acceptance rows no longer speak of markers that are gone.
  - The iPRES cases no slice had named have their verdicts: `t02-01-002` and `t02-03-010` here, and `t02-01-001`,
    which has no catalog at all and earns nothing for it, in [#132].
- **Found on the way**, filed as debt under M02: a document with no catalog earns no finding when its index was
  rebuilt at opening ([#132]).

### 2026-09-29 — The rest of `src/` covered, two messages a direct test showed wrong, two tests that wavered
- **Coverage.** `src/` goes from 98.9 % to 99.6 % as Codecov counts it (4,493 of 4,511 lines): 39 lines of the
  reader, its source and parser, the cross-reference probe, the object graph, the page tree walk and eight rules, each
  covered through a document where one reaches it, otherwise by a call with what no file produces — an empty source
  handed to the reader, a structure set by hand, a window outside the source, a catalog edited in memory. Each group
  was reviewed by a second agent that tried to refute what the first called unreachable; one claim fell (an object
  stream decoded again inside its own decoding, short both times, now tested), and one test was made to catch what its
  name says (a null character in the trailer, past the keys that name sections).
- **What stays uncovered**, 18 lines: nine in private members of `ArlingtonWalk` (settled with [#124]); a branch of
  the lexer's `ReadRegularRun` for a byte the switch before it did not claim, which none can be — 65,536 two-byte
  prefixes and 20,000 random buffers never took it —, kept as a guard against a later edit of the switch or of the
  delimiters, since without it the lexer would stop advancing; the `?.` on `ChainIndex` in
  `CrossReferenceProbe` (two lines) and `PageTreePageOrphanedRule`, never null once the chain is read, which is
  checked before; `RootInvalidRule.Kind`'s default arm, which nothing reaches now that a stream has its own; and the
  line the compiler puts after `ExceptionDispatchInfo.Throw` in `PdfFileReader.RecoverCatalog`.
- **Two messages.** A chain loop whose naming section is unknown said "the section at offset -1"; it now says which
  key names the loop, and no offset. A `/Root` written as a stream in the trailer was "not an object"; it is now "a
  stream written in the trailer", as a dictionary is. Neither case comes from a file the reader produces a record for,
  the second only from a trailer holding a stream.
- **Two tests that wavered.** The allocation test of the Arlington lookups found 3,352 bytes on one CI run under
  coverage and none on another run of the same commit: it now runs the lookups once, then measures the least of up to
  five runs, which a lookup that allocates still fails at 48,000 bytes a run. The linear-time test of a mis-indexed
  object stream failed once here in the full suite under coverage, at a ratio of 9.5 where it measures 3 to 6 alone:
  it now runs in a collection of its own, after the others, the best of five runs of each size.
- **Found on the way**, filed as debt: the object stream dependency walk reads an index a rebuild changes under it
  ([#128]), and the page tree findings misword a kid written directly in its parent's `/Kids` ([#129]).

### 2026-09-29 — Dependencies between issues, declared in their bodies
- **The question.** Some issues wait on others — slice 4 ([#60]) on [#55] and [#56] — and GitHub did not know it.
  Settled with the maintainer: GitHub's own *blocked by* relationships, the body of an issue as their reference,
  every open issue reviewed, the convention written down (`CLAUDE.md`, *Conventions*; `docs/roadmap.md`,
  *Tracking on GitHub*).
- **The syntax.** A line `Blocked by: #55, #56` on the issue that waits, or `Blocks: #60` on the one waited on; a
  target that is not an issue yet stays in words (`Blocks: M03 slice 1`) until the slice is opened. A milestone
  closes when all its issues have, so what only its closing waits on — [#117] to [#120] for M02 — is filed under
  it and needs no relationship.
- **The workflow.** `sync_tracking.py dependencies` reads every body and makes GitHub's relationships match, adding
  and removing; only the owner's, members' and collaborators' bodies count, and a relationship with another
  repository is left alone. `Tracking` runs it on every issue they open or edit and on each of its runs from `main`;
  every run rebuilds the whole set, so one its concurrency group drops loses nothing, and the listing's dependency
  summary keeps it to a few requests. Seventeen new tests; 43 in all. This session cannot write GitHub's
  relationships itself, so the first ones appear when the job first runs, on the merge.
- **The issues.** Declared: [#60] blocked by [#55], [#56] and [#120] (the same `/Length` path); [#61] by [#60] and
  [#62] by [#61]; [#126] by [#125]. In words, until their slices exist: [#35] blocks M03 slice 1 (it is slice 0),
  [#52] M03 slice 6, [#36] M03 slice 8 and M06 slice 5, [#42] M10 slice 1, [#109] M05 slice 1. Left in prose: the
  M23 debts done together ([#47], [#48], [#49], [#53], [#121]), a grouping rather than an order, and [#54],
  [#41] and [#45], which wait on a stable release, not an issue. A simulation against the live issues, with the new
  bodies, gives exactly those six relationships and misreads no other body.
- **The help-wanted issues**, W03 to W30 ([#63] to [#89]), under no milestone until now, are each filed under the
  first milestone of their *Unblocks* column that has not started: W05 ([#65]) under M03, W06 ([#66]) under M05, W10
  and W22 under M06, W03, W13 and W29 under M07, and so on to W21 under M25. Such a milestone does not close before
  its contribution arrives.

### 2026-09-29 — Slice 3's second pull request: covered, and a cast the walk no longer makes
- **Coverage, by a rule the maintainer set today** (`CLAUDE.md`, *Coverage*): 100 % of each patch is the aim and
  `codecov.yml`'s 95 % the floor; every member that is not private is covered, directly when no document reaches it,
  and neither a private member nor a branch the compiler adds is tested for its own sake. The branch's patch went from
  96.7 % to 99.1 % as Codecov counts it (971 of 980 lines): tests of the messages written directly, of handles of the
  model's tables, of trees, discriminators, inherited keys and trailers the corpus does not hold. The nine lines left
  are in private members of `ArlingtonWalk`: a guard for a model that would link the cross-reference stream, branches
  the pinned model and overrides never take, and a switch's default arm.
- **A cast the walk could not make.** An object the walk met as a dictionary and read again after a rebuild of the
  reader's index — as a tree node, a candidate to type, or an ancestor to inherit from — could be a number by then,
  and the walk threw `InvalidCastException`. Validation never met it, as the object graph resolves every object, and
  so meets the rebuild, before the walk; the walk run alone did. It now skips what is no longer a dictionary.
- **A test that timed out under coverage.** The linear lookup of a mis-indexed object stream (on `main` since
  2026-09-28) was held to a fixed 10 s for a million objects, which coverage instrumentation, as CI runs it,
  exceeded here. It now requires four times the objects to cost less than eight times the time, which the lookup
  meets at about four and the search it replaced fails at more than eleven.
- **Found on the way**, filed as debt: a `startxref` near the largest `long` wraps to a negative offset once the
  header's offset is added ([#125]), and a finding on a trailer value is located past the end of the file when
  `startxref` points beyond it ([#126]).

### 2026-09-28 — M02 slice 3, second half: the Arlington object-shape rules
- **Settled with the maintainer before the first line**, from a study of the model and a prototype of the subset
  measured on the 408 documents here: a generator in C# under `tools/`, its output committed and regenerated by a unit
  test rather than produced at build time (ADR 44, amended); the model's `tsv/latest` vendored at commit `c48b363`,
  byte for byte, under a lock of SHA-256; a root `NOTICE` packed with the core, the license expression staying MIT;
  four rules, the fifth — a key newer than the declared version — left to a debt issue; an override only where ISO
  32000-1 does not require what the model says, its citation checked against the text, a genuine violation staying a
  declared finding; a fault a hand-written rule reports silenced in the generated ones by a named "covered by" row.
- **The generator.** `tools/AdCodicem.Pdf.Arlington`, a project of the solution held to its build rules: `generate`,
  `verify`, `update --from <clone>`. It reduces the model's 613 objects and 3,983 rows to static tables — ten-byte
  rows, names as ASCII bytes compared with `PdfName.Value`, the links of each type, the plain values, and for each of
  the 78 lists of several candidates the plan that tells them apart —, one line per row of the model, in a file of 660
  KB; it refuses what it cannot encode, and an override the model no longer needs. The encoding is one file both the
  core and the tool compile. Dumped and compared with the prototype's compiled model, the tables differ only in the
  rows the overrides edit.
- **The walk.** Breadth-first from the trailer along the model's links, with a queue: candidates kept by kind, then
  the plan, then the study's scores, a tie leaving the object unchecked; `/Parent`, a structure element's or an
  annotation's `/P` and an outline item's `/Prev` checked but not followed; each indirect node or array of a name or
  number tree expanded once, however its `/Kids` loop or share arrays; a reference to an object the file lacks or the
  reader could not produce present and unchecked; an object a reader limit cut not judged, nor what is written inside
  it, and an ancestor so cut taken to give an inherited key — the reader now records such objects, an object stream's
  members that run into data `MaxDecodedStreamLength` cut among them. Of the trailer, only `/Root`, `/Info` and
  `/Encrypt` are followed, and nothing it holds of its own is checked. What is wrong is tallied per rule and row of
  the model, counting objects rather than occurrences, and written at the end. The version is the header's, or the
  catalog's `/Version` when later, never rounded, and none without a header that names one.
- **The rules.** `object.key-missing`, `object.value-type-wrong`, `object.type-value-wrong` (warnings) and
  `object.key-deprecated` (information), after `object.name-null-character`: one finding per type and key, at the
  first object, with how many; a row that stands for every key a type does not name gathers them into one finding.
- **Overrides.** The seven the prototype proposed, each citation checked against ISO 32000-1's text and quoted — one
  of them in part: an empty `/Order` sub-array is legal, and a sub-array nested in another, which Table 101 does not
  describe, stays Esri's declared finding. Two more the corpus review found, where ISO 32000-1 is looser than the
  model: a form XObject's `/FormType` and `/Matrix` optional and its `/Name` required in PDF 1.0 only, an
  optional-content creator's `/SubType` — the model's row for a misspelled `/Subtype` — a name or a string. A third, a
  Type 3 font's `/Encoding` as a name, was dropped on review: Table 112 types it "name or dictionary", but its text
  requires "An encoding dictionary whose Differences array shall specify the complete character encoding", so the AFP
  Batch Processor's fonts keep a declared finding. Eight "covered by" rows leave the page tree's faults to its
  rules, and hold only where the page tree's walk judged the object: a page the tree does not list, which only a
  destination names, is the generated rules' — the unlisted page of pikepdf's cyclic outline now earns
  `object.key-missing` for its `/Resources`. The page tree rules were made to cover what the rows claim: a `null`
  `/Kids`, `/Count`, `/MediaBox` or `/Resources`, written so or through a reference, is absent, a `/Kids` naming an
  object that is no array is reported, and a missing `/Count` is reported on every node, above a loop or without
  `/Kids` too. Running both on the corpus and on the first half's fixtures found no other fault reported twice. A
  catalog without `/Pages` is the generated rules': no other reports it.
- **The corpus.** 48 entries change, their findings all, four of them no longer unsupported. 21 of the 279 documents
  the manifest calls clean earn a generated finding, each checked in the file against ISO 32000-1 — structure elements
  without `/P`, FieldMDP references without `/Data`, PDFMaker's border styles written as strings, an action typed
  `/A`, a `/ToUnicode` given a name, a destination whose first element is a name, a boolean in the document
  information dictionary, an IRS form from Distiller 3 whose form XObjects lack `/Subtype`, the AFP Batch Processor's
  Type 3 fonts whose `/Encoding` is a name —, one of them a deliberate iPRES file; 27 of the 129 others do. The last
  four documents recorded as unsupported until M02 — `jhove-hul-142` ×2, iPRES `t02-02-009` and `t02-03-002` — are
  diagnosed: none waits for M02 any more. The prototype's walk, its findings filtered by the same overrides, agrees on
  403 of the 408 documents at the grain of rule, type and row; the five others are its limits or settled refinements —
  in the two tiff2pdf scans, a thumbnail's `/Length` naming an object that is no integer, which the prototype saw as
  dangling; a dangling reference counted as present; an object in a zeroed object stream left unjudged; and pikepdf's
  cyclic outline's unlisted page, where the prototype reports the missing `/Resources` too and the check's filter
  silences it without the page tree's condition. `ValidationRefereeTests`: the catalog's `/Type` is reported where,
  and only where, `qpdf --check` finds it missing or invalid, on all 408. Two remote documents GitHub refused here
  (HTTP 403) keep entries nothing here has checked.
- **Tests.** `ArlingtonGeneratorTests`: the tables regenerated byte for byte, the lock, stale overrides, a refusal for
  each thing the generator cannot encode. `ArlingtonModelTests`: every row decoded as the TSV gives it, an FsCheck
  property over generated rows, 2,000 lookups that allocate nothing. `ArlingtonRuleTests`: each rule on a broken file,
  a sound one and an unusual legal one, each override, ties, repeating groups, inheritance, the interactive form's
  `/DA`, dangling and unproduced references, nulls, versions, a 20,000-long outline chain, a `/Parent` cycle among
  fields, a looping name tree, a tree node written in the `/Kids` array it names and nodes sharing one `/Kids` or
  `/Names` array, each expanded once, a tree node, an ancestor and an object stream's member cut at a limit, the page
  tree's silences where its walk judged and not elsewhere, determinism. `PageTreeRuleTests`: `null` and referenced `/Kids`, `/Count`, `/MediaBox`, `/Resources`, and a missing
  `/Count` where the pages below cannot be counted. The integration suite ran here against qpdf 11.9.1 and passed
  whole: 1,124 tests on the committed corpus, 2,683 with the remote one.
- **Measured.** `ValidationBenchmarks`, ShortRun, 1,000 synthetic pages: the walk alone 1.7 ms and 280 KB; the profile
  2.5 ms and 588 KB on a document already read, 6.2 ms and 2.6 MB to open and validate one — the same, within
  ShortRun's error, after the review's fixes, which keep only the exceptions of the page tree's judged parents so that
  nothing grows with the pages. The journal validates within its 6 MB budget, 3.9 MB measured, 0.5 MB of it the walk,
  which checks its 6,008 objects and values, as the prototype did. The core's assembly grows from 178,176 to 307,712
  bytes, the tables' data 87,546 of them, and its build by about 0.8 s. On the largest walk, pdf.js's
  `cairo-firefox-objstm-index-overflow-bug1978317.pdf`, 163,866 values checked, validating goes from 0.42 s to 1.0 s
  and from 275 MB to 467 MB allocated: almost all of the difference is the reader reading again the objects its cache
  of 8,192 had dropped. The walk's queue holds object numbers, not objects: holding the objects halved that
  allocation, and kept 98,307 of them alive at once.
- **Found on the way.** Two JHOVE files, `jhove-hul-2` and `jhove-hul-28`, earn `object.value-type-wrong` where a
  reference written `0 0 R` is read as two integers ([#117]); once the reader reads it as a reference, the finding
  becomes `object.reference-missing`'s.

### 2026-09-28 — M02 slice 3, first half: the object and page tree rules, and #51
- **Settled with the maintainer before the rules were written**, from measurements: two pull requests, the
  Arlington rules the second; one finding per fault, a hand-written rule owning what a generated one would also see;
  generic identifiers for the generated rules; the object syntax faults in this slice; a reference to an object the
  file lacks reported once per referring object, as a warning; a kid null or missing, a wrong `/Count` or `/Parent`,
  a page without a media box or resources, all warnings — 7.3.10 makes the reference null, qpdf only warns, and M02
  holds the iPRES files qpdf only warns about to no error —; a loop in the tree an error, as `xref.chain-loop` is; a
  page the tree leaves out information; pages counted as qpdf's walk counts them; `PdfValidationLocation.PageIndex`,
  from 0, written from 1; for the second pull request, a C# generator and a committed table checked by a test, the
  model's `tsv/latest` vendored under `tools/`, a root `NOTICE` packed with the package, overrides only where ISO
  32000-1 does not require what the model says.
- **Measured before designing.** Independent walks of every page tree and of every object graph of the 408
  documents here, from qpdf's JSON and the raw bytes, beside the reader's view; the Arlington model's own checker
  and a prototype of the subset for the second pull request. The page tree walks agree with qpdf 11.9's on 380 of
  381 documents once null and missing kids count as pages; five clean documents refer to objects they lack; one
  forbidden cycle exists in the corpus; two real files leave pages out of their tree, and DocuSign types
  marked-content property lists `/Page`.
- **The reader.** [#51]: an object an object stream needs to be read — through its `/Length`, `/Filter`,
  `/DecodeParms`, `/N` or `/First` — reads as null while the stream is decoded, is reported under a new code,
  `stream.self-reference`, and is read again once the stream is; the null is no longer cached, nor one met while a
  rebuilt index takes in its object streams. The reader says whether an object the index holds could be produced,
  and records an object whose value `endobj` does not follow — a stream without `endstream` leaving it unjudged.
- **The rules.** Thirteen: `xref.object-stream-circular` (error; `xref.object-stream-broken` silent on such a
  stream), `object.reference-missing`, `object.endobj-missing`, `object.name-null-character`, `page-tree.cycle`
  (error), `page-tree.node-repeated`, `page-tree.kids-missing`, `page-tree.kid-invalid`, `page-tree.count-mismatch`,
  `page-tree.parent-wrong`, `page-tree.mediabox-invalid`, `page-tree.resources-missing` (warnings) and
  `page-tree.page-orphaned` (information, judged only where the index is sound and whole). The page tree and the
  objects the trailer reaches are walked once per validation, iteratively, each object by its number; a tree
  20,000 levels deep and nodes listed twice at forty levels are walked in bounded time.
- **The corpus.** 34 documents declare new findings, seven of them clean — warnings only; fourteen are no longer
  unsupported, and five page counts are qpdf's walk rather than the root's `/Count`. An independent computation from
  the files' bytes, without the rule code, gave the same rule identifiers and page counts on all 408 documents, and
  found one false `object.endobj-missing` — a stream without `endstream` — which is fixed. `QpdfRefereeTests` counts
  pages with qpdf's walk; `ValidationRefereeTests` holds every error of any family to a document qpdf finds fault
  with, and every document whose page tree qpdf repairs to a finding. M02's three missing acceptance tests exist:
  the damage the corpus tags diagnosed, the field anomalies no error, the thousand-page journal validated within
  6 MB (3.9 MB measured).
- **Tests.** `PageTreeRuleTests` and `ObjectRuleTests`, each rule on a broken file, a sound one and an unusual legal
  one, and #51 in each order; the validator fuzzed on mutated corpus documents. Every line the change adds to `src/`
  is covered on the committed corpus. The integration suite ran here against qpdf 11.9.1 and passed whole: 956
  tests on the committed corpus, 2,275 with the remote one.
- **Measured.** `ValidationBenchmarks`, ShortRun, 1,000 synthetic pages: 716 µs and 308 KB on a document already
  read, 4.1 ms and 2.4 MB to open and validate one.
- **Found on the way**, filed as debt: a reference written `0 0 R` is read as two integers and a stray `R` — the
  reader says so, and no finding does — ([#117]), a rebuild records every object at generation 0 ([#118]), a
  container left open to the end of an uncut window is not reported ([#119]), `stream.length-invalid` says
  "declared -1" for a `/Length` that could not be resolved ([#120]), and each object is read through its own 8 KB
  window ([#121], M23).

### 2026-09-28 — The project's coverage, and Codecov's rules
- **Settled with the maintainer.** `codecov.yml`: the project may lose half a point of coverage at most against
  its base, each patch is 95 % covered, `tests/` is not measured. The rule for coverage work, now in `CLAUDE.md`
  and `CONTRIBUTING.md`: code goes only when no input can reach it, never because no file of the corpus does; a
  defensive branch stays, covered or not; 100 % is not the goal; a public member nothing calls is asked about.
- **From 93.6 % to 99.0 % of `src/`**, as Codecov counts it on the committed corpus, with 67 new cases.
  - The object model as a caller uses it: arrays and dictionaries edited in place, typed accessors, a loop of
    references, UTF-16LE text, names, shared values, streams and exceptions (`ObjectModelTests`).
  - The PNG predictors against libpng: an 8 by 6 RGB image written by ImageMagick 6.9.12 with libpng's adaptive
    filtering — Sub, Up, Average and Paeth — and with none, decoded through `/FlateDecode` and `/Predictor 15`
    to its exact pixels; the TIFF predictor at widths other than a byte.
  - The reader on shapes no corpus file had: a stream of any kind, a cross-reference stream without a type
    field, object stream headers wrong in four ways, a rebuild past the first megabyte, an entry past the end
    of the file, object headers numbered 0 or beyond an `int`; the lexer, parser and filters at their edges.
  - Dead code removed: `PdfLexer.Peek`, `PdfLexer.Length` and `PdfCharacters.IsDelimiter`, which nothing called.
- **Left uncovered, on purpose.** The reader's fallbacks — an empty source (`Open` refuses it first), an offset
  outside the file (its callers check), a rebuild asked for twice, a stream shorter on read than it said, a window
  outside the source; the lexer's guard against a delimiter its switch does not claim, which keeps it from
  looping should the switch change; the rules' fallbacks for values the reader never records; the parser's
  end-of-stream check past its buffer; the `}` after `reached?.Throw()`, which the tool counts apart; and the
  branches the compiler adds to a switch over strings or types. Every public member nothing called is kept and
  now tested.

### 2026-09-27 — M02 slice 2: the file and cross-reference rules, and ADR 45
- **Settled with the maintainer, one question at a time, before the rules were written.** Severity (first "an
  error is an index unusable as written", then ADR 45), how far entries are probed (each one, headers only, each
  object stream once without keeping it), the reader's catalog recovery in the same pull request, the rule
  catalogue submitted after measurement, the remote corpus confirmed by a `Remote corpus` run on the branch, a qpdf
  integration test from this slice, linearization left out ([#108], M23).
- **Measured before designing.** An analysis of the raw bytes of every document here, independent of the reader —
  header, `startxref`, each section and trailer, each entry probed, each object stream decoded —, beside qpdf's
  `--check` and the reader's diagnostics. It found what each candidate rule would say, and it later checked what the
  rules do say: they agreed on all 401 documents, the three differences being the analysis's approximations.
- **Corrections owed to the maintainer.** Three situations first called "legal but unusual" were not all legal: an
  offset naming the white space before an object or before `xref` breaks ISO 32000-1 (7.5.4, 7.5.5), every reader
  reading past it; the "decreasing /Size" was a hybrid file's stream against its table, not an update; a trailer
  without `/Root` is the main table of a linearized file, which F.3.11 wants reduced to `/Size`. The first became
  `xref.offset-imprecise`, once per file; the second `file.size-wrong`, extended to each section, Table 17's two
  readings of a stream's `/Size` both accepted; the third stays silent. Two severities first argued from other
  readers were contradicted by measurement — qpdf reads t03-010's object as null, Table 15 makes t04-016's catalog
  missing —, which led the maintainer to ADR 45.
- **ADR 45.** A finding's severity says whether the reader can vouch that it reads the file as written: an error
  goes with a rebuild, a loss or a choice the file does not make; a warning is a file read as it was evidently
  meant. `file.header-missing`, `xref.generation-mismatch` and `xref.object-past-size` became warnings;
  `xref.chain-loop` an error, a loop losing the section the chain should have gone on to. No `Critical` level
  before M05 ([#109]). The reader's diagnostics keep their own scale.
- **The reader.** It records the file's own structure as it opens it (`FileStructure`: the header, `startxref`,
  each section's state, fault and trailer as written, `/Root` and `/Size` before anything was done about them), and
  copies the chain's index before a relocation or a rebuild changes it, so that reading objects between two
  validations changes nothing. A `/Root` that leads to no catalog no longer discards a sound index: the catalog is
  looked for among the indexed objects first (new diagnostic `trailer.root-recovered`). A negative `/Prev`, a
  malformed row and a looping chain mark the index incomplete, as a missing section does.
- **The rules.** Twenty join `file.eof-missing`: `file.header-missing`, `file.header-offset`,
  `file.header-version-invalid`, `file.startxref-missing`, `file.startxref-wrong`, `file.trailer-missing`,
  `file.trailer-malformed`, `file.root-invalid`, `file.size-wrong`, `xref.section-malformed`,
  `xref.section-not-found`, `xref.section-shifted`, `xref.chain-loop`, `xref.entry-broken`, `xref.entry-shifted`,
  `xref.generation-mismatch`, `xref.object-stream-broken`, `xref.offset-imprecise`, `xref.object-past-size`,
  `xref.checked-in-part`. `docs/website/docs/reference/validation-rules.md` gives each its severity, meaning and reference, and what the
  profile leaves silent on purpose — a row not twenty bytes long ([#107]), linearization ([#108]), a catalog
  written in the trailer rather than referred to ([#111]).
- **The corpus.** Every entry declares its findings: 65 documents earn `file.startxref-wrong`, 11
  `file.startxref-missing`, 18 `file.header-offset`, 18 clean ones a `file.size-wrong` (Word's and Excel's hybrid
  streams one short, `/Size` off by one), 8 clean ones `xref.offset-imprecise` (Microsoft Print to PDF), 9 encrypted
  ones `xref.checked-in-part`; no clean document earns an error. Eight iPRES files are supported (t03-007, t03-010,
  t04-010 to t04-015), their expectations saying what the reader does where they had copied qpdf's rebuild; the
  corpus's reading test counts a finding as damage made known, for what the reader reads without a word. Nine
  remote documents could not be fetched here; their findings were predicted, and `Remote corpus` run 9, on the
  branch with all 242 documents, confirmed them.
- **Tests.** `FileRuleTests` and `CrossReferenceRuleTests`, 116 cases on files written from a template whose offsets
  are placeholders: each rule on a file that breaks it, a sound one and an unusual legal one, and the report
  unchanged by reads and rebuilds between validations. `ValidationRefereeTests` (integration): every document qpdf
  rebuilds the index of earns a `file.*` or `xref.*` finding, and every such error is a document qpdf finds fault
  with — on all 401 documents here, without an exception to name.
- **Measured.** `ValidationBenchmarks`, ShortRun, 1,000 synthetic pages: 214 µs and 8.9 KB, against 86 ns and
  232 B for slice 1's one rule.
- **Coverage, with the maintainer.** Codecov's patch check failed at 89.2 %. The maintainer asked for the patch
  first and the rest of the project in a pull request of its own, and set the rule: code is removed only when no
  input can reach it — conditions that contradict each other, a dead branch —, not because no file of the corpus
  does; a defensive branch stays, covered or not, and 100 % is not the goal. The patch is at 99.0 %: tests for each
  reachable case — a hybrid file's stream, a cross-reference or object stream compressed and cut by a limit or
  corrupt, a table the file ends in, a rebuild or a guard met during the catalog search —, the fallbacks for values
  the reader never records left as they were. Covering it found three faults: a `/Root null` reported as "not an
  object", an unreadable object stream reported in no sentence, and an object stream whose dictionary
  `MaxObjectLength` cut reported broken — an error — where the reader's limit, not the file, kept it from being
  checked. The maintainer also settled `codecov.yml`, left to the project's coverage work ([#115]).

### 2026-09-27 — Milestones, slices and debt tracked on GitHub
- **The question.** Whether GitHub's issues and milestones suit the milestones and the debt. Settled with the
  maintainer one question at a time: the specifications stay in `docs/milestones/`, and `docs/roadmap.md` stays
  the reference for the milestones and their state — `FeatureTablesTests` reads it, and CI never asks GitHub —;
  GitHub mirrors it, and carries what is tracked item by item.
- **Milestones.** The `Tracking` workflow runs `.github/scripts/sync_tracking.py` on every change to the
  roadmap, a specification or `.github/labels.json` on `main`: one GitHub milestone per row of the roadmap's
  index, described by its goal, closed when the row says done, never given a due date — the roadmap is an
  intention —, and the labels the issues use. What it reads and plans is held by 26 tests in CI. Its first
  run, on the merge of #90, created the 32 milestones, numbered 1 (M00) to 32 (M31), and #34 to #62 were
  filed under theirs — the debt under the milestone its row named, the five settings and accounts under
  none.
- **Slices.** A milestone's slices become issues labeled `slice` when it starts; the exit criteria stay
  checkboxes in its file. M02's six are #57 to #62, the first closed as done with #29.
- **Debt.** The 24 open rows but T10 are 23 issues, #34 to #56, labeled `debt` and an area, typed Bug or Task, and
  `maintainer` where only a setting or an account closes them (#40, #41, #44). T10 was the list that
  `docs/corpus-contributions.md` now keeps. This file's table gives way to a link and the former identifiers.
  Code, tests, the manifest, the milestone specifications, the roadmap, `architecture.md`, `corpus.md`,
  `corpus-contributions.md` and `releasing.md` name the issue now, and an `unsupported` reason may start with
  `#47:` as it does with a milestone; the journal, the ADRs, `docs/research` and `docs/corpus-sources.md`,
  which record what happened, keep T01 to T40.
- **The corpus's wanted documents.** Each row of *What is wanted* with something still wanted — W03 to W10 and
  W12 to W30 — has a `help wanted` issue, #63 to #89, whose first lines say an issue is public and a
  confidential file never goes in one. The file stays the reference and links each row to its issue; a
  *Document contribution* issue form asks for the same checks.
- **Open questions.** Each row of the roadmap's *Open questions* has a discussion under Ideas, #91 to #104,
  opened by the workflow's manual dispatch and linked from the table's third column, which the script does
  not read: it matches a question to its discussion by the subject.
- **Conventions.** A commit names the issue it advances in its footer (`Refs #58`), and the pull request
  closes what it completes (`Closes #58`); sessions open, label and comment on issues freely, and close one
  only through a merge. `stale.yml` exempts `debt`, `slice`, `help wanted` and every issue under a milestone.
- **Afterwards.** Codecov's comments on #90 came from `codecov[bot]`, and the maintainer confirmed its GitHub
  App installed: #40 is the first debt closed as an issue, by the pull request that records it.

### 2026-09-27 — A disclaimer, and the roadmap called an intention
- **At the maintainer's request.** The README (and so the package on nuget.org) gains a `Disclaimer` section:
  provided as is, no liability to the extent the law permits, the MIT license governing; and what stays the
  user's to check — every document produced, conformance claims, redaction, the legal validity of signatures —
  with no legal, tax or compliance advice. The site's introduction carries a shorter version, and its footer
  says "provided as is, without warranty" on every page. `LICENSE` is untouched.
- **The roadmap is an intention, not a promise**: said at the top of `docs/roadmap.md`, and on the features
  page, whose template called a planned feature "a commitment of the roadmap" and no longer does.

### 2026-09-27 — T27: a reference to an object the file lacks is null; #31 rebased on the roadmap revision
- **The defect.** A reference to an object the index did not hold made the reader rebuild its whole index,
  scanning the file for the object — the lazy rebuild meant for an index that lost entries — and report a
  repair on files qpdf calls clean.
- **Measured before designing.** Every one of the 401 documents readable here, each object resolved and each
  reference inside it followed: eight such rebuilds on an intact chain, and none found anything — every
  object sought lay at or past `/Size`, and none is in its file.
- **The fix.** The specification makes such a reference null, and so does the reader, silently: the finding is
  M02's third slice. The index is rebuilt for a missing object only when it may have lost entries — a section
  the chain names could not be found (T25), `MaxXRefSectionCount` or `MaxXRefSectionLength` stopped the chain
  or a table, a cross-reference stream holds fewer rows than it declares —, and then as it was, once, when
  such an object is asked for.
- **What it exposed.** `/Prev` and `/XRefStm` are read as direct integers only, as the specification makes
  them. The two JHOVE tiff2pdf scans write `/Prev 576066 0 R`: resolving that reference had rebuilt their
  index by chance, and T27 would have dropped their main table in silence. It is now `xref.section-missing`,
  and they read their 6 and 4 pages, as qpdf counts them, on demand; their manifest entries expect the
  report and no rebuild at opening. Two limit tests that reached a rebuild through a missing object now give
  the index a `/Prev` that names no section.
- **The corpus.** The two iPRES files T27 was recorded against — a catalog's `/Pages` and a page's
  `/Contents` naming object 9 — are supported: no rebuild, nothing reported. Twenty-six remote entries stay
  unsupported, 17 of them from the iPRES set.
- **Tests.** In `CrossReferenceChainTests`: a reference past `/Size` and one to a free entry, each object
  written after the file's end where only a rebuild would find it, read as null with nothing reported; a chain
  and a table a guard stopped, and a stream short of its rows, each rebuilt when the object is asked for; a `/Prev`
  written as a reference, reported and read on demand. Each fails when its rule is mutated, but resolving
  the reference, which reads as null and is reported all the same.
- **Rebased on the roadmap revision.** The revision merged first opened its own T37 and T38; the two debts
  T32 found became **T39** and **T40**, in the commits as in the documents, which follow its American
  spelling and its two-digit milestone numbers: the memory budgets of the old M13 are M23's. One remote
  document renamed there, `jhove-hul-79-quartz-word-program-evaluation-report.pdf`, is fetched under its new
  name. A last test covers what `FlateInput` refuses, seven lines Codecov found uncovered.
- **Every line of #31's change is covered.** Codecov then found four lines and branches of the patch no test
  reached (97.96 %), and the unit suite measured as CI measures it gave the same four. Two were the branch for
  a host that sets `UseStrictValidation`, which the framework reads once per process: the loop now leaves on a
  fault and looks at a request past the end first, so that the host's exception and the framework's silent end
  take one path — checked in a separate process, the switch on and off: the same bytes and the same reports.
  The others were behaviour without a test, and have one: a guard's cut that falls in the checksum, or after
  whole data, reports only the guard; and only the inflater's complaints about its data count as faults. All
  197 lines the change adds are covered, branches included.

### 2026-09-26 and 27 — The roadmap revised from a feature survey
- **The question.** Asked which PDF features the milestones did not plan, and for a feature table and a
  comparison that show what the library is for, the answer was a survey: six areas — .NET libraries, other
  ecosystems, ISO 32000-2, the standards around it, HTML to PDF engines, business and legal workflows — each
  read and then challenged by a second pass that dropped what the roadmap already planned or what was not
  true. 307 features survived, in sixteen themes (`docs/research/2026-09-feature-survey.md`). The maintainer
  answered one question per decision.
- **What was decided.** The roadmap now has 32 milestones, numbered in the order they are worked, every
  reference in the repository renumbered and the old numbers mapped at the end of `docs/roadmap.md`. The case
  file comes before the HTML engine (M06 to M11), Factur-X right after tagging (M14); new milestones for
  revisions and signature coverage (M04), barcodes (M10), annotations and layers (M11), HTML forms (M17),
  legal case files (M18), redaction and sanitization (M19), PDF/A conversion (M21), imaging and OCR (M22),
  comparison (M24), signing and long-term validation (M26, M27), PDF 2.0 conformance (M28), print production
  (M29), advanced typesetting (M30) and DOCX to HTML (M31). ADRs 37 to 44 record what is out of scope,
  resource loading deny-by-default, forward references, the output version, cryptography in satellites,
  image codecs and scans, Skia in rendering with text analysis ours (amending ADR 5), and structural rules
  generated from the Arlington model. T06 now covers PDF 2.0 UTF-8 strings; CLAUDE.md adds `CropBox` to the
  inherited attributes.
- **Every milestone specified.** Each file in `docs/milestones/` has its slices, its acceptance against named
  corpus documents, and a "Corpus" section saying what the corpus lacks — identified, none added
  (`docs/corpus-contributions.md` gathers them for M01 to M31). The session's usage limit first stopped the run
  before its last writers; M23 and M29 to M31 were written without agents, then deepened, and every file was
  reviewed against the roadmap, the corpus and the other files.
- **Open points settled.** The writers raised 173 open points (`docs/research/2026-09-milestone-open-points.md`),
  each now followed by its outcome: 93 already resolved or fixed in the milestone files, the rest applied to the
  roadmap (dependency columns completed, 37 corrections), the ADRs (15 and 17 repaired, eight amended), the debt
  table (T05 to the start of M03, T13 merged, T37 and T38 new), CLAUDE.md and the corpus manifest. Thirteen
  maintainer decisions settled the rest — among them: an input keeps its PDF version (ADR 40); M03's read-only
  objects, signed-rewrite refusal and cancellation convention; one manifest for non-PDF corpus inputs; an
  `AdCodicem.Pdf.Fonts` satellite with one face in the core; managed MD5, RC4 and AES in the core (ADR 41);
  the `adpdf` command; the PDF/X referee left to M29's first slice. Ten early ADRs whose Context or Decision
  was lost in their conversion are restored.
- **American English.** The maintainer chose American spelling for everything (ADR 20, amended): identifiers,
  verbs, codes, the corpus manifest's `license` key, six file names — ADR 16 and five corpus documents — and
  the prose. Proper names and quoted text keep theirs (the Open Government Licence, Licence Ouverte).
- **Milestone numbers padded.** Numbers below ten take a leading zero (M00 to M09), in references and in file
  names, so that they sort; `docs/roadmap.md`'s renumbering table keeps the numbers as they were before.
- **Feature tables and comparison.** `docs/features/features.json` ties each feature to its milestones and a
  state; the README's table and a new site page, "Features and comparison", are generated from it, and
  `FeatureTablesTests` holds them to it and to the roadmap. `comparison.json` describes twenty products —
  license, pricing model, runtime, HTML engine, maintenance — and answers twenty capabilities for the eight
  .NET products most often compared, each cell with its source, each product checked by a second pass.
- **Comparison benchmarks.** `benchmarks/AdCodicem.Pdf.Benchmarks.Comparison` opens five corpus documents with
  this library, PdfPig, PDFsharp and iText; the on-demand `Comparison benchmarks` workflow produces the figures
  the site will publish, none yet. A dry run here showed the allocation gap the design promises — about 0.5 MB
  to open the thousand-page journal against 6 to 9 MB for the others — and is not a published figure.
- **Still open.** The first run of the comparison workflow, and its figures on the site; the corpus gaps,
  milestone by milestone as each begins; the PDF/X referee and whether PDF/X-4 is capped at PDF 1.6, both left
  to M29's first slice.
### Earlier, in brief

The detail is in git and in the pull requests; what still matters is in the records and in this file.

- **2026-09-12 and 13 — foundations and M01.** Scope, the first nineteen decisions (now ADRs), the solution
  skeleton, the .NET SDK installed from the Ubuntu archive. M01 in seven slices: the object model, a tolerant
  lexer and parser, the filters, all four shapes of index, lazy resolution with a bounded cache, repair by
  scanning, structured diagnostics, every file-sized allocation bounded. English throughout; acceptance on
  real documents (19 from four producers, expectations from independent tools, never from our reader); two
  test levels, the second running qpdf in a container; a Docusaurus site; warnings as errors. Validation (M02)
  and repair (then M04) were inserted then, so milestone numbers in commits older than 2026-09-13 follow the
  previous order; they were renumbered again on 2026-09-26 (the mapping is in `docs/roadmap.md`).
- **2026-09-14 — M01 closed.** Mutation fuzzing found a mutual recursion that killed the process within a
  minute; relocation became three counted attempts. The repository was brought to the standard toolchain,
  and CodeQL's first run found three defects.
- **2026-09-15 and 16 — publishing.** Previews on every merge, stable releases by hand (ADR 30). `v0.1.0`
  tagged as the starting point, with `published-baseline.sh` so that packing never asks nuget.org for a
  version it does not have. Six major action bumps merged.
- **2026-09-19 — first packages and the supply chain.** The first previews reached nuget.org through trusted
  publishing. OpenSSF Scorecard, red since it was added (an action ref that did not resolve), published 5.5,
  then 6.6 and 7.1: actions pinned by hash, npm overrides, a linked security policy, FsCheck properties.
  Pages deployed for the first time; the coverage upload was pointed at the file it was meant to read.
- **2026-09-22 — the site.** It had never rendered a user page: two MDX loaders compiled each one twice.
  Repaired, with a check of every built page. Versioned documentation (ADR 31). `main` got its ruleset.
- **2026-09-24 — the corpus from public sources.** Documents screened for license and personal data, the
  rules on names relaxed with the maintainer; a remote corpus for what may be used but not redistributed
  (ADR 32), one manifest for every document, and over 2 MB a document goes remote. T21, T23 and T24 found.
- **2026-09-25 — more corpus, and fuzzing.** The OPF format-corpus screened file by file: 56 committed, 67
  remote, 161 refused. The iPRES 2017 hand-built set fetched out of its authors' archive (ADR 33). The
  nightly fuzzing campaign, which had filled its runner's disk, now starts from one document per reader
  structure plus a rotating share, for 43 % of the cost. T25, T26 and T27 found.
- **2026-09-26 — T21 and T23.** A window too small for its object no longer leaves its reports behind: only
  the attempt that is kept reports, a cut at the buffer's end is noticed wherever it falls, a stream whose
  end may lie past the window is confirmed by 13 bytes read from the file, and a classic table's window grows
  for what ends it. `WindowEdgeTests` slides nine objects across the edge a byte at a time, and ten mutations
  of the fix each fail a test. The five remote documents recorded for T21 and T23 are supported. Found on
  the way: T29, fixed in the next commit, T30 and T31.
- **2026-09-26 — the reader's guards, validation's first rule, and three reader debts.** T31: every filter keeps
  the 256 MB bound and says when it reached it; a predictor's row length can no longer overflow. ADR 34 and invariant
  12: every valid PDF is readable, the reader's five guards are `PdfReaderLimits` a caller may raise, each reported
  under its own `limit.*` code; ADR 35 allows measured unsafe code. ADR 36 and M02's first slice: validation in the
  core, `PdfValidator`, the structural profile and `file.eof-missing`; previews carry no guarantee (ADR 30). The
  manifest gained a JSON schema. T32: a Flate stream that lost its tail, and an LZW code never defined, are
  reported, held to qpdf by `FlateRefereeTests`. T25: a section `/Prev` misses is found within 512 bytes or reported
  as `xref.section-missing`. CodeQL left to GitHub's default setup. Every line of #28's change covered.

## Debt and open points

Known debt is tracked as issues since 2026-09-27: the [open debt](https://github.com/AdCodicem/AdCodicem.Pdf/issues?q=is%3Aissue%20state%3Aopen%20label%3Adebt), each issue filed under the
milestone that will pay it, and among it [what only the maintainer can do](https://github.com/AdCodicem/AdCodicem.Pdf/issues?q=is%3Aissue%20state%3Aopen%20label%3Amaintainer) — a repository
setting or an account, not a change a pull request can make. What a session finds and leaves becomes an issue
labeled `debt` there and then, not a line here. What the corpus still wants stays in
`docs/corpus-contributions.md`, each row with a [`help wanted`](https://github.com/AdCodicem/AdCodicem.Pdf/issues?q=is%3Aissue%20state%3Aopen%20label%3A%22help%20wanted%22) issue.

### Former identifiers

Until 2026-09-27 this section was a table whose rows were numbered T01 to T40; the journal, the ADRs,
`docs/research`, `docs/corpus-sources.md` and the commits cite them so.

| Row | Now |
|---|---|
| T01, T02 | Fixed on 2026-09-13 by `7e47804`: warnings as errors, XML documentation required on the public API |
| T03 | Fixed on 2026-09-13 by `4154e11`: the corpus of real documents |
| T04 | [#34] |
| T05 | [#35] |
| T06 | [#36] |
| T07 | [#37] |
| T08 | Fixed on 2026-09-14 by `1f90e8f`: fuzzing of the lexer and the parser, in the suite and nightly |
| T09 | [#38] |
| T10 | The "still wanted" column of `docs/corpus-contributions.md`, each row with its `help wanted` issue |
| T11 | Done on 2026-09-19 (`d3a748d`): publishing observed, previews on nuget.org through trusted publishing |
| T12 | Done on 2026-09-19 (`a20570a`): the site published, and rendering since 2026-09-22 |
| T13 | [#39] |
| T14 | Done on 2026-09-27 ([#40]): the Codecov GitHub App is installed |
| T15 | [#41] |
| T16 | [#42] |
| T17 | [#43] |
| T18 | [#44] |
| T19 | [#45] |
| T20 | Done by 2026-09-26 (`35b232e`): the `AdCodicem.` prefix is reserved on nuget.org |
| T21, T23 | Fixed on 2026-09-26 by `11556f3`: a stream or an object longer than the parser's window |
| T22 | [#46] |
| T24 | [#47] |
| T25 | Fixed on 2026-09-26 by `3efb5e6` (#31): a cross-reference section `/Prev` misses is found nearby, or reported |
| T26 | Fixed on 2026-09-25 by `ebb35bd`: a remote document may be a member of a pinned archive (ADR 33) |
| T27 | Fixed on 2026-09-27 by `772b69d` (#31): a reference to an object the file lacks is null |
| T28 | [#48] |
| T29 | Fixed on 2026-09-26 by `d1bb4cb`: object loads nest at most 64 deep |
| T30 | [#49] |
| T31 | Fixed on 2026-09-26 by `2574f0f`: every filter keeps its bound, and says when it reached it |
| T32 | Fixed on 2026-09-26 by `8051bf6` (#31): a Flate stream that lost its tail, an LZW code never defined |
| T33 | [#50] |
| T34 | [#51] |
| T35 | Done on 2026-09-26 by `beb0a4d`: CodeQL left to GitHub's default setup |
| T36 | [#52] |
| T37 | [#53] |
| T38 | [#54] |
| T39 | Fixed on 2026-09-30 by [#140]: a stream's length checked past the parser's window ([#55]) |
| T40 | Fixed on 2026-09-30 by [#147]: what a damaged Flate stream decoded is kept, and a wrong checksum reported ([#56]) |

[#34]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/34
[#35]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/35
[#36]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/36
[#37]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/37
[#38]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/38
[#39]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/39
[#40]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/40
[#41]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/41
[#42]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/42
[#43]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/43
[#44]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/44
[#45]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/45
[#46]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/46
[#47]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/47
[#48]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/48
[#49]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/49
[#50]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/50
[#51]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/51
[#52]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/52
[#53]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/53
[#54]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/54
[#55]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/55
[#56]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/56
[#58]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/58
[#59]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/59
[#60]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/60
[#61]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/61
[#62]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/62
[#63]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/63
[#65]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/65
[#66]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/66
[#89]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/89
[#107]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/107
[#108]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/108
[#109]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/109
[#111]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/111
[#115]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/115
[#117]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/117
[#118]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/118
[#119]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/119
[#120]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/120
[#121]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/121
[#122]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/122
[#123]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/123
[#124]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/124
[#125]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/125
[#126]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/126
[#128]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/128
[#129]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/129
[#132]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/132
[#133]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/133
[#134]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/134
[#135]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/135
[#136]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/136
[#137]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/137
[#138]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/138
[#140]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/140
[#141]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/141
[#142]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/142
[#143]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/143
[#144]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/144
[#145]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/145
[#146]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/146
[#147]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/147
[#148]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/148
[#149]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/149
[#154]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/154
[#155]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/155
[#156]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/156
[#157]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/157
[#158]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/158
[#159]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/159
[#160]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/160
[#161]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/161
[#162]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/162
[#163]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/163
[#164]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/164
[#165]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/165
[#166]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/166
[#167]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/167
[#168]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/168
[#169]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/169
[#170]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/170
[#171]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/171
[#172]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/172
[#173]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/173
[#174]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/174
[#175]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/175
[#176]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/176
[#177]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/177
[#178]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/178
[#179]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/179
[#180]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/180
[#181]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/181
[#182]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/182
[#183]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/183
[#185]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/185
[#186]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/186
[#187]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/187
[#188]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/188
[#189]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/189
[#190]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/190
[#191]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/191
[#192]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/192
[#193]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/193
[#194]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/194
[#195]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/195
[#196]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/196
[#197]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/197
[#199]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/199
[#200]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/200
[#201]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/201
[#202]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/202
[#203]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/203
[#204]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/204
