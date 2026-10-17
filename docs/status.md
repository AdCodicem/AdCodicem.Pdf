# Project status

A living file, updated **at the end of every session**. It describes the real state, not intentions.
Keep it short: summarize the journal once it passes a dozen entries — the detailed history is in git, not
here. What is tracked item by item lives on GitHub since 2026-09-27: each milestone's slices and the known
debt are issues, filed under the [milestones](https://github.com/AdCodicem/AdCodicem.Pdf/milestones) the
Tracking workflow mirrors from `docs/roadmap.md` (*Debt and open points*, below).

## At a glance

- **Current milestone**: M02 — Document validation (`docs/milestones/M02.md`), in progress. Slices 1 to 3 are done: the
  engine and the report (ADR 36), twenty file and cross-reference rules under ADR 45's severities, and the object, page
  tree and Arlington object-shape rules (ADR 44). What remains is *Next concrete step*: step 4's thirty-two batches of
  reader and validation debts, settled with the maintainer on 2026-10-01, the first four of which, [#117], [#187],
  [#159] and [#193], merged with [#203] and [#211] that day and [#213] and [#214] on 2026-10-02, the fifth, [#157] and
  [#186], with [#217] that day, and the sixth, [#182] with [#215] and [#216], with [#225] on 2026-10-03, the seventh,
  [#118], with [#226] that day, the eighth, [#119] and [#172], with [#231] on 2026-10-04, the ninth, [#125] and [#126],
  with [#238] that day, and the tenth, [#190] and [#212], with [#248] on 2026-10-14; the eleventh, [#164] and [#183],
  with [#249] that day; the twelfth, [#165] and [#166], filter chains, with [#199], [#209], [#219] and [#247], with
  [#252] on 2026-10-15. Slice 4's two blockers, [#141] and [#144] — batch 23 and half of batch 24 —, brought forward at
  the maintainer's request on 2026-10-15, merged with [#254] on 2026-10-16; the thirteenth, [#128] and [#237], with
  [#253], is in [#258], under review. Then the other batches, slices 4 to 6 ([#60] to [#62]); then the milestone's
  adversarial review, slice 7 ([#137], ADR 46), once every other issue filed under M02 is closed. The debts come from
  the reader's own work, the
  threat model's first version ([#135], `docs/threat-model.md`), M01's review after the fact ([#136],
  `docs/reviews/M01.md`), the planning of step 4 ([#199] to [#202]), its second batch ([#207] to [#210]), its third
  ([#212]), its fifth ([#215], [#216]), its sixth ([#218] to [#224]), its eighth ([#229], [#230]), its ninth ([#237]),
  its tenth ([#241], [#247]), its twelfth ([#250]), slice 4's blockers ([#253]) and its thirteenth ([#255] to [#257]);
  [#123] moved to M20.
- **User documentation**: organized along Diátaxis since 2026-09-30 ([ADR 47](adr/0047-the-user-documentation-follows-diataxis.md),
  [#148]), on `main`: a tutorial held to its sample by a test, four how-to guides, four reference
  pages — the validation rules among them, moved from the project documents — with the API reference under them, and
  four explanations. Outside any milestone.
- **Last milestone closed**: **M01 — Object model and tolerant reading**
- **Tests**: 4,253 unit on [#258]'s branch (19 skipped: 2 by design, the theory over the remote corpus's streams whose
  length is wrong, the fifteen remote documents of the theory over the corpus's filter chains, which have no document
  without them, and the test held to PDFBox's remote report) + 1,495 integration (skipped without Docker) + 23 for the
  remote corpus's fetcher + 43 for the roadmap's mirror on GitHub. With the remote documents fetched, on [#258]'s
  branch: 6,624 unit with the 233 this container holds, 3 skipped — the laziness test on the two documents recorded as
  unsupported until [#47], which every other test holds to their expectations, and the private manifest this container
  lacks —; 3,688 integration against qpdf in its container, 3,682 passed and 6 skipped where qpdf cannot walk a damaged
  document's pages; and, with all 242, `Remote corpus` run 60's 6,699 acceptance tests and 3,760 referee checks, on the
  code [#258] ends with.
- **Coverage**: on the committed corpus, as Codecov counts it (a line with an untaken branch is partial, the generated
  Arlington tables left out), 99.54 % of `src/` — 6,543 of 6,573 lines on [#258]'s branch, every one of its patch's
  211 measurable lines among them, every branch taken. The 30 left are those the rule of 2026-09-29 leaves (`CLAUDE.md`,
  *Coverage*): `main`'s 28 since [#252], at the line numbers [#258] gives them, less
  `ObjectStreamDependencies.Location`'s arm, which [#258] removes, and three more. They are members that are private, or
  of a private type, which no input reaches —
  eight lines of `ArlingtonWalk` (194, 919, 1026, 1036, 1037, 1057, 1059, 1147) and three of a defensive branch of
  `PdfLexer` (161, 164, 165) —, a `?.` on an index never null where it is read and a switch's default arm
  (`CrossReferenceProbe` 86 and 231, `PageTreePageOrphanedRule` 51, `RootInvalidRule` 98 and 107), a line the compiler
  puts after a call that never returns (`PdfFileReader` 1775), three branches the compiler adds that no input takes
  (`FileQuote` 145, an interpolation's buffer too small; `PdfObjectParser` 796, a concatenation's null check;
  `PdfObjectParser` 716, the switch over a `/Length`'s presence, whose every arm is taken); `EndStreamAfter`'s two
  defensive arms (`PdfFileReader` 1743, 1747, journal of batch 6), the `HasValue` checks the compiler adds on a `long?`
  (3259, batch 5) and a defensive arm of the real's expansion (`PdfReal` 66, batch 5), one of the six ways out of a
  cross-reference stream row's `switch` (3355) and `Repair`'s guard against a second rebuild (4431, 4433). Batch 13 adds
  three defensive lines no test or document reaches any longer: `KeepObjectStream`'s release of the contents it
  replaces (4386, 4388), a rebuild now letting go of every object stream kept before any is decoded again; and
  `PageTreePageOrphanedRule`'s check of a section's state (91), settling having rebuilt the index of a damaged section,
  which the check before it answers.
  `codecov.yml` asks 95 % of each patch, and lets the project drop by half a point at most; the aim is 100 %.
- **CI**: green on `main` at `8385c81` (CI run 575), [#254]'s last commit. `Remote corpus` passed on every document in
  the nightly run 18, on `main` at `8c54490`, and in runs 19 and 20, on [#203]'s and [#211]'s branches, each rerun once
  after web.archive.org refused a document ([#204]), in run 21, on [#213]'s branch, at its first attempt, and in run 22,
  on [#214]'s branch, rerun once for the same refusal. The nightly run 23, on `main`, failed only because
  web.archive.org refused the download of ECan's scan (`Connection refused`, [#204]): every test over the 241 it fetched
  passed. Run 24, on [#217]'s branch, lost the same document at its first attempt and passed whole at its one rerun; run
  25, on [#225]'s branch, passed whole at its first attempt; run 26, on [#226]'s branch, lost ECan's scan again at its
  first attempt and passed whole at its one rerun. The nightly run 27, on `main` at `2f26244`, lost the CFIA's form,
  web.archive.org refusing the connection ([#204]), and passed every test over the 241 others. Runs 28 and 29, on
  [#231]'s branch, passed whole at their first attempt, the second on the code the branch ends with; CI run 473 is green
  on that code. The nightly run 30, on `main` at `78ff13b`, passed whole at its first attempt, and so did runs 31 and
  32, on [#238]'s branch, the second on the batch's last code; run 33, on that code rebased onto `main`, lost ECan's
  scan at its first attempt, web.archive.org refusing the connection ([#204]), and passed whole at its one rerun. CI run
  509 is green on that code. The nightly runs 34 to 36 and 39 to 41, on `main` at `83d15a1`, passed whole at their first
  attempt; runs 37 and 38 lost ECan's scan, web.archive.org refusing the connection, and run 42 the 88 documents of the
  ipres2017 archive, its host timing out ([#204]), every test over the others passing. Run 43, on [#248]'s branch, and
  run 44, on its last code, lost the ipres2017 archive the same way at their first attempt, every other test passing;
  run 44 passed whole at its one rerun. CI run 534 is green on that code. The nightly run 45, on `main` at `a930544`,
  passed whole at its first attempt, and so did run 46, on [#249]'s branch, on the batch's last code. Runs 47 and 48, on
  [#252]'s branch, each failed on a test the branch then fixed — the corpus manifest's schema, which the new guard's
  commit had left out, then a test that took a cut cross-reference stream's rows as read —, every other test over the
  242 documents passing; runs 49 to 51 passed whole, run 51 on the batch's last code. The nightly run 53, on `main` at
  `2d7d5dc`, lost ECan's scan, web.archive.org refusing the connection, and failed one test, an allocation window that a
  collection elsewhere counted 8 KB over ([#253]), every other test passing. On [#254]'s branch, run 52, on its first
  code, and run 54, on the code its second review changed, passed whole at their first attempt; run 55, on its last
  code, lost ECan's scan, web.archive.org refusing the connection, and the 88 documents of the ipres2017 archive, its
  host timing out ([#204]), every test over the others passing; at its one rerun it lost the ipres2017 archive alone,
  every test over the 154 others passing, and the 88 it lost pass on that code in this container, whose unit and
  integration runs hold them. The nightly run 56, on `main` at `2d7d5dc`, and run 57, on [#254]'s last code, lost
  documents their hosts refused or timed out on ([#204]) — run 57 ECan's scan and the ipres2017 archive —, every test
  over the others passing, run 57's 5,216 acceptance tests and 2,575 referee checks over 153 documents. The nightly run
  58, on `main` at `8385c81`, passed whole at its first attempt, and so did runs 59 and 60, on [#258]'s branch, run 60
  on the code it ends with.
- **Corpus**: 168 committed documents, 23.0 MB — 19 generated here, 3 from Word and PDF24 on Windows, 146
  third-party files under attribution-only licenses (`docs/corpus-sources.md`). Beside them, a **remote
  corpus** of 242 documents we may use but not redistribute, fetched at a pinned SHA-256 and size (ADR 32),
  88 of them out of their authors' archive (ADR 33), 7 out of web.archive.org, and tested every night by `Remote
  corpus`, last green in the nightly run 58 on 2026-10-17. All 410 are described in `tests/corpus/manifest.json`.
- **Published**: [`AdCodicem.Pdf`](https://www.nuget.org/packages/AdCodicem.Pdf) `0.1.1-preview.10` to
  `0.1.1-preview.57`, one per push to `main` until [#227], through trusted publishing, 1,141 downloads on 2026-10-02.
  The `AdCodicem.*` prefix is reserved: nuget.org marks the package as verified. With [#227] merged, previews come
  from `preview.yml`, weekly and on dispatch, only when a package input changed, numbered after the next release:
  the first will be `0.2.0-preview.<N>` (ADR 49).
- **A preview carries no guarantee** (ADR 30, 2026-09-26): an API no stable release has shipped may change or
  go with the next preview.
- **No stable release yet.** `v0.1.0` is a tag with no package behind it, by design, and the stable path has
  never run. With [#227] it packs and tests without credentials, publishes from `nuget-stable` behind a reviewer,
  pushes through a release App and attests; it waits on settings only the maintainer can make (journal of 2026-10-03,
  *Left to the maintainer*), and stops at its first step until they are made.
- **The site**, <https://adcodicem.github.io/AdCodicem.Pdf/>, is versioned (ADR 31). With [#227] it is redeployed by
  every preview run, and by a push to `main` that changed no package input since the version on nuget.org (ADR 49);
  before the first stable release the preview is the whole documentation. Since 2026-10-04 ([#235]) it
  wears the AdCodicem design system, serves the documentation
  under `/docs` behind a homepage, redirects every former address, and has a local search
  (`docs/website/README.md`, *Look and feel*).
- **Supply chain**: OpenSSF Scorecard **7.6** on `74ce382`. What Scorecard still marks down is settings and
  people: Code-Review 0 (nothing has ever been approved by a second person), Branch-Protection 5 (T15), Maintained 0
  (the repository is younger than 90 days), Contributors 3, CII-Best-Practices 0 (T18), Signed-Releases
  unscored until a release exists (T19). What it does not measure, the threat model found in code: the release jobs
  built and tested with the right to publish ([#177]), which [#227] moves into jobs without credentials; nothing ties
  publishing to `main` but a setting of the environments ([#178]); and only the packed nuspec is checked for a
  dependency of the core since [#227], not the assembly's references ([#179]).
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
| Indexing | synthetic, 1000 pages, ~4 MB | 280 µs | 122 KB |
| Indexing, then reading every page | synthetic, 1000 pages, ~4 MB | 4.8 ms | 5.7 MB |
| Indexing and walking the page tree | real ReportLab document, 1000 pages | — | 3.2 MB |
| Indexing a classic table (xUnit, after a first reading) | generated, 300,000 objects | — | 16.9 MB |
| Indexing a cross-reference stream whose rows Flate stores (xUnit, after a first reading) | generated, 300,000 objects | — | 29.5 MB |
| Indexing a chain of a thousand classic sections (xUnit, after a first reading) | generated, 300,000 objects | — | 30.6 MB |
| Validating under the structural profile, the document already open and read | synthetic, 1000 pages | 3.5 ms | 620 KB |
| Typing and checking the objects the trailer reaches against the Arlington model, alone | synthetic, 1000 pages | 2.3 ms | 312 KB |
| Opening and validating under the structural profile | synthetic, 1000 pages | 9.4 ms | 2.4 MB |
| Settling the index of a sound document, after opening it | synthetic, 1000 pages | 0.27 ms | 50 B |
| Walking the object streams' dependencies | generated, 1000 object streams sharing a 16 KB `/DecodeParms` | 4.2 ms | 2.3 MB |
| Decoding a whole Flate content stream | 4 MB decoded, about 330 KB encoded | 8.3 ms | 13.1 MB |
| Decoding the same stream without its checksum, or without its last five bytes | 4 MB decoded | 11.7 ms | 13.1 MB |
| Decoding the same stream under a wrong checksum, kept whole | 4 MB decoded | 12.1 ms | 13.1 MB |
| Decoding a stream that turns corrupt halfway, the 2 MB before the fault kept | 4 MB of content | 8.1 ms | 6.6 MB |
| Decoding a chain of ASCII85 and Flate | 4 MB decoded | 10.4 ms | 13.7 MB |
| Indexing, then parsing every page's content stream, each past the reader's first window | synthetic, 1000 pages of 16 KB | 3.9 ms from memory, 7.1 ms from a file | 2.0 MB |
| The same, each stream declared 2 bytes too long and searched for its `endstream` | synthetic, 1000 pages of 16 KB | 7.3 ms from memory, 12.5 ms from a file | 2.8 MB |
| Parsing one object, a medium job | Word's page dictionary | 6.0 µs | 4.6 KB |

The gap between the first two rows is the library's promise: opening a document does not read its content.
The validation rows are slice 3's thirty-eight rules. The first measures the rules alone on a document whose objects
the reader has cached: each entry of the index probed, the page tree walked, every object the trailer reaches met
once and typed and checked against the Arlington model, every object of the index looked at for a page the tree
leaves out — the allocation is the sets of object numbers met, each page's index, and the Arlington walk's queue and
tallies. The second is that walk alone, which the four rules generated from the model share. The third is what a
caller validating a file it has not read pays: every object parsed for the first time, about 2.7 KB a page, no
stream's data read. Slice 3's first thirty-four rules took 716 µs and 308 KB on the first row, slice 2's twenty-one
214 µs and 8.9 KB, slice 1's one rule 86 ns and 232 B; the rows grow with each slice of M02. Since batch 13 ([#258])
the validator settles the index first: on a sound document, that is reading the header each regular entry places, 0.25
to 0.28 ms and about 50 bytes for a thousand pages, which the third row includes; the first, whose document is settled
already, pays nothing more. The three validation rows allocate what they did on `main` before the batch, to 40 bytes;
under a medium job, twice, alternated, the first took 3.0 and 3.7 ms there, 3.4 and 3.6 ms on the branch, and the
third 8.4 and 9.3 ms there, 9.2 and 9.7 ms on the branch, settling within a gap the runs' spread covers. The walk of
the object streams' dependencies reads once an object their decoding keys share: a thousand streams sharing a 16 KB
`/DecodeParms` took 118 to 128 ms and 125.6 MB on `main`, each stream parsing it again, and take 4.1 to 4.3 ms and
2.3 MB (`ObjectStreamDependencyBenchmarks`, ShortRun, twice).
Indexing costs the map, 52 to 63 bytes an object, whatever the objects weigh, since a section makes room for its rows
before reading them ([#164]); a map a rebuild grows costs 100 to 210, by where the count falls against its growth steps.
A cross-reference stream adds 7 to about 42 bytes an object for its rows, and the object stream holding the catalog,
when one does, what it holds. The first two rows are batch 11's (ShortRun, twice): 394 KB and 6.1 MB on `main` in the
same runs, 304 to 324 µs and 5.5 to 6.5 ms. The third row is asserted as a budget in CI
(`CorpusReadingTests`), so an allocation regression fails the build; it was 2.4 MB when M01 closed, and M01's review
measured 3.2 MB on 2026-10-01, under the 4 MB budget. The three generated rows are held to 17.7 MB, 31.0 MB and 32.1 MB, 5 %
over their figures (`DocumentReaderTests`, [#193], [#164]); growing the map allocated 31.1 MB, 43.1 MB and 33.2 MB; MB here are 2^20 bytes. A stream that ran out is
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
The last row is `ParserBenchmarks`' ([#119], [#172]), the means of two medium runs of each alternated, before the
checks of the syntax and with them. What a sound object allocates is what it was, to 10 bytes: 4.6 KB for Word's page
dictionary, 1.37 MB for a thousand dictionaries of strings, 1.56 MB for a thousand of escaped names, 1.44 MB for ten
thousand distinct keys. The checks cost 5.7 → 6.0 µs for the page dictionary, about 9 ns for each of its 34 names,
1.71 → 1.77 ms for the names, 1.47 → 1.49 ms for the strings and 2.74 → 2.76 ms for the keys. A hostile object pays
for its reports, formatted until the diagnostics hold 1,000 and counted past them: a thousand hexadecimal strings with
stray bytes take 81 → 214 µs and 118 → 448 KB, a thousand names whose `#` is no escape 76 → 201 µs and 55 → 432 KB,
ten thousand entries that give ten keys again and again 1.22 → 1.53 ms and 524 → 799 KB; ten thousand distinct keys
given null, which a set now holds to tell a repeat that follows, 1.03 → 2.18 ms and 313 → 970 KB; an object the end
of the data leaves open, 19.6 → 21.0 µs.

## Next concrete step

M02 — document validation (`docs/milestones/M02.md`), slices 1 to 3 done. Its progress is the
[M02 milestone](https://github.com/AdCodicem/AdCodicem.Pdf/milestone/3) on GitHub, which closes when every issue
filed under it has; the maintainer settled on 2026-09-29 that each is paid in M02 rather than moved, [#123] aside.
One pull request per batch, each design question put to the maintainer after measuring, in this order:

1. The review of 2026-09-29 recorded, the specification brought up to date, the two [#47] documents held to every
   test but the laziness one, and the damaged trees' page counts held to qpdf — done, merged with [#133].
2. [#55] and [#120], one path in `PdfObjectParser.ReadStream` — done, merged with [#140] on 2026-09-30: a stream's
   `/Length` checked past the parser's window, and a `/Length` that gives no length said as the file wrote it.
3. [#56], with [#134] — done, merged with [#147] on 2026-09-30 (*Earlier, in brief*): what a damaged Flate stream
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
   2. [#187], the reader's codes and positions — done, merged with [#211] on 2026-10-01, `Remote corpus` run 20 green
      on its branch (journal of 2026-10-01).
   3. [#159], what a message quotes of the file, bounded and escaped — done, merged with [#213] on 2026-10-02,
      `Remote corpus` run 21 green on its branch (journal of 2026-10-01).
   4. [#193], the memory and time budgets the index changes are measured against — done, merged with [#214] on
      2026-10-02, `Remote corpus` run 22 green on its branch (journal of 2026-10-02).
   5. [#157] and [#186], numbers read from the file; [#186] blocks M03's slice 1 — done, merged with [#217] on
      2026-10-02, `Remote corpus` run 24 green on its branch (journal of 2026-10-02).
   6. [#182], a cross-reference stream's dictionary read as written, with [#215] and [#216], which batch 5 filed —
      done, merged with [#225] on 2026-10-03, `Remote corpus` run 25 green on its branch (journal of 2026-10-03).
   7. [#118], a rebuilt index's generations — done, merged with [#226] on 2026-10-03, `Remote corpus` run 26 green on
      its branch (journal of 2026-10-03).
   8. [#119] and [#172], what the parser read in silence — done, merged with [#231] on 2026-10-04, `Remote corpus`
      run 28 green on its branch (journal of 2026-10-03).
   9. [#125] and [#126], offsets past what a long holds and where a trailer's finding goes — done, merged with [#238]
      on 2026-10-04, `Remote corpus` run 31 green on its branch (journal of 2026-10-04).
   10. [#190] and [#212], the reader's public surface — done, merged with [#248] on 2026-10-14, `Remote corpus` run
       44 green on its branch (journal of 2026-10-13).
   11. [#164] and [#183], the index's size, the first new guard — done, merged with [#249] on 2026-10-14, `Remote
       corpus` run 46 green on its branch (journal of 2026-10-14).
   12. [#165] and [#166], filter chains, with [#199], [#209], [#219] and [#247] — done, merged with [#252] on
       2026-10-15, `Remote corpus` run 51 green on its branch (journal of 2026-10-15).
   13. [#128], with [#237] and [#253], a report that does not depend on what was read before it — done, in [#258],
       `Remote corpus` runs 59 and 60 green on its branch (journal of 2026-10-17).
   14. [#129] and [#175].
   15. [#160] and [#161], object-stream members, with [#200], [#202], [#255] and [#257].
   16. [#174] and [#170], stream data, with [#220], [#224] and [#230].
   17. [#155].
   18. [#167] and [#168], with [#201].
   19. [#188], [#197] and [#156], with [#207], [#208], [#210], [#221], [#222], [#241] and [#256].
   20. [#189].
   21. [#154] and [#171].
   22. [#132], with [#218] and [#229].
   23. [#141] — done in [#254], brought forward with [#144] at the maintainer's request on 2026-10-15 (journal of
       2026-10-15).
   24. [#169]; [#144], its batch's other half, done in [#254] with [#141].
   25. [#181].
   26. [#162], with [#250].
   27. [#163], `MaxDepth` and `MaxNestedLoads` made guards, as the maintainer decided.
   28. [#185].
   29. [#173].
   30. [#192].
   31. [#107] and [#111], each a new public rule whose name and severity the maintainer gives.
   32. What remains of [#158], with [#223].

   Slice 4's blockers, [#141] and [#144], are paid in [#254]; nothing else filed blocks it. The four defects the
   planning found outside every issue, [#199] to [#202], and the four batch 2 found, [#207] to [#210], are filed under
   M02 and paid in the batches above (journal of 2026-10-01). [#212], which batch 3 filed, is paid in batch 10, with
   [#190], as the maintainer placed it on 2026-10-02. The seven batch 6 filed are placed above: [#218] with [#132], as
   the maintainer placed it on 2026-10-02, the others with the batch whose code they share. The two batch 8 filed are
   placed likewise: [#229] with [#132], [#230] with [#174]; the one batch 9 filed, [#237], with [#128]; of the three
   batch 10 filed, [#241] with [#188], [#247] with [#165], and [#240] under M03, whose full rewrite it concerns; and of
   the two batch 12 filed, [#250] with [#162], whose code it shares, and [#251] under M23, which sizes the decoding it
   concerns. Of the three batch 13 filed, [#255] and [#257] go with [#160] and [#161], the reads of object streams they
   concern, and [#256] with [#197], whose search near a row it shares.
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

### 2026-10-17 — Batch 13: a report that does not depend on what was read before it ([#128], [#237], [#253])
- **The question.** [#237]: a read could make the reader rebuild its index halfway through a validation — the index
  incomplete through damage, or an entry placing its object where a load finds neither it nor its header nearby —, and
  a rebuild merges the trailers it finds, serves the last definition of each number and may take another catalog: the
  rules before it read the document as the chain gave it, those after it as the rebuild did. [#128]: the walk of the
  object streams' dependencies read an index such a rebuild changes. Measured on 8385c81 over 96 crafted files — 90
  shapes and 6 controls — and the 401 documents of the corpus: a fresh validation, a second one and one after a read
  disagreed in 72 of the 90 shapes, `Validate` crashed on two (`ObjectGraph.Describe`), and a caller resolving the
  trailer's values inside a `foreach` over it threw. Three designs were prototyped: settling the index before the first
  rule; validating the document as it stood at opening (two corpus reports changed, 23 % more time, over three times the
  memory, a guard only validation reaches left unreported); fixing the two issues alone (38 shapes still disagreed with
  themselves). [#253]: an allocation window of `HostileInputTests` that a collection paused counted 9,000 bytes for
  4,096. Ten questions followed.
- **Settled with the maintainer**, each as recommended but one (Q9, the maintainer's own):
  - settle before the rules: `PdfValidator.Validate` calls `PdfFileReader.SettleIndex`, which rebuilds once when a later
    read still could; a rebuild forgets what the reader noted of the definitions it replaces; `FileStructure` is the
    document as it opened (Q1); an object whose own load set off a rebuild is not cached, for every caller (Q2);
  - a limit that rebuild reaches throws, under `ThrowOnLimit`, before any rule (Q3); `file.root-invalid` is not judged
    when a guard hid `/Root` as the document opened, whatever a rebuild found since (Q4);
  - the rows are judged as the file wrote them, an indirect value a row's object needs read through the settled index
    (Q5); the exceptions named — a document changed between two validations, a limit under `ThrowOnLimit` — and the
    lazy-reading wording qualified (Q6); `WasRepaired` changes as on any rebuild (Q7);
  - the object graph described each object from the value it walked (Q8, replaced by R2 below);
  - tests per mechanism, a theory over the crafted shapes in the unit suite, a corpus check that no read rebuilds after
    a validation, and a corpus sweep — fresh, second, after reading everything — in the integration suite, the one test
    there that runs no independent tool (Q9);
  - [#253]: every allocation window under 16 KB measured as the least of unpaused windows (Q10).
- **Reviewed**, twice, and settled again each time with the maintainer, each as recommended.
  - The first review, over five lenses, gave 36 findings, 2 blocking; each was put to a refuter and to a reproducer: 31
    held by both, the 5 others weighed by hand — 3 fixed all the same, 1 moot once the convergence theory landed, 1 a
    note with nothing to do. Blocking: an object stream on the load stack was not expanded by the rebuild its load set
    off, and its members were lost — the catalog read as null right after opening —; the settling check took an `obj`
    keyword its window cut for a header. Then: the object graph's walked values held every object along a chain of
    references (41 MB for two hundred objects of 200 KB); the dependency walk parsed a shared object once per stream (93
    MB for 400 streams); `file.root-invalid` named a catalog the rebuilt index no longer served; settling rebuilt where
    a read reports the object guard and goes on; documentation that promised more than the code; mutants no test killed.
    Decided: R1, a rebuild a load sets off is finished from its scan once no load is in flight, the object read again;
    R2, the object graph reads an object again to describe it, and says what it counted without where when that read
    does not find the fault; R3, settling takes a header past the object guard as a read does.
  - The second, over the same five lenses aimed at the fixes, gave 25 findings, 3 blocking, each put to two refuters:
    all 25 held by both. Blocking: under `ThrowOnLimit`, rows leading into one run of blanks longer than the object
    guard had settling read each to the guard (8.6 GB for 400 rows of a 17 MB file); the dependency walk's memo grew
    with the file (80 MB for a file of 60 MB); a generation per stream got around it (93 MB again). Then: the expansion
    R1 made for the loads in flight read every object twice, and its reports outlived the finish; the catch that
    finished a rebuild before a guard's exception left the read was decided in its filter, before the loads it was part
    of had let go, so that a guard reached a load deep left the rebuild unfinished and the next read threw it; a finish
    the source failed lost the rebuild for good; the scan's guard was swallowed when the read threw its own; eleven
    mutants no test killed; three texts. Decided: R4, the rebuild expands once, when finished; R5, settling reads at
    most twice the file's length in windows past the first two of each header, then rebuilds; R6, the memo is keyed by
    number and holds what the reader's cache holds; R7, the outermost load finishes the rebuild for any exception, the
    scan's guard thrown first, a failed finish left to the next read.
- **Done**, in [#258], 25 commits:
  - [#253]: every window under 16 KB measured as the least of unpaused ones;
  - [#128]: the walk judges each object stream, and each object written directly in the file its decoding keys lead
    through, where the chain's index places it — read once a validation under whatever generation, as many kept as the
    reader's cache holds —; `ObjectStreamDependencyBenchmarks` measures it;
  - [#237]: `SettleIndex`, before the first rule — the header each regular entry places read as a load reads it, in a
    window of 64 bytes then grown as a load grows its own, or looked for nearby, a header past the object guard taken as
    a read takes it, at most twice the file's length read past the first two windows of each —; a rebuild forgets what
    was noted of the definitions it replaces; `FileStructure` as the document opened; nothing a load read through an
    index its own rebuild replaced is cached; a rebuild a load sets off is finished, once, from its scan, when no load
    is in flight or before an exception leaves the read, the scan's guard first, a failed finish left to the next read;
    no catalog a rebuild no longer serves named as the reader's; the object graph reads an object again to describe it.

  Docs: ADR 50 (*Validation settles the reader before its rules*), amending ADR 36; `concepts/validation.md`,
  `concepts/lazy-reading.md`, `reference/validation.md`, `reference/validation-rules.md`, `reference/reader-limits.md`;
  the threat model's rows on settling, on a rebuild run again, on a report that depends on what was read, on the walk's
  reads and on the graph's memory, and the validator's own reads; M02; `ARCHITECTURE.md`; a wanted document in
  `corpus-contributions.md`, the corpus holding none whose index a read rebuilds after opening.
- **Tests**: 335 new unit tests, 3,918 to 4,253, and 168 integration, 1,327 to 1,495: `ReadOrderTests`, the
  convergence theory over the 90 shapes, `CorpusValidationTests.No_read_rebuilds_the_index_after_a_validation`, and
  `ReadOrderSweepTests` over the corpus in the integration suite. 40 mutants of the fixes, each run against the classes
  that hold the reader and the validator, and the four an earlier round left alive again against the whole unit suite:
  39 failed a test, several once a test was added for them, and one by never finishing, a window that grows past the
  guard; one lived on, the clearing, at a rebuild's finish, of what read as null while the loads were in flight, two
  files built to reach it reading alike with and without it ([#257]).
- **Tracking.** Filed: [#256], under M02, placed with batch 19 beside [#197], whose search near a row it shares: the
  cross-reference probe takes for a header what a read refuses — an `obj` its 64-byte read cuts, or one that runs on —,
  reproduced twice; [#257], under M02, placed with batch 15, the one mutant no test kills, with the two probes that did
  not reach it. [#255], which the batch's measurement filed on 2026-10-16, under M02, is placed with batch 15 too, the
  reads of object streams it concerns. [#168] was told what the batch measured of the walk's per-stream pass and of the
  graph's retention.
- **Checked**:
  - the solution builds with no warning, and `dotnet format` finds nothing;
  - each of the 25 commits builds with no warning and passes the full unit suite alone, with the remote documents this
    container holds: 6,056 to 6,624 tests, 3 skipped;
  - the patch's 211 measurable lines of `src/` covered, every branch taken; 99.54 % of `src/` in all, 6,543 of
    6,573 lines, on the committed corpus alone;
  - the corpus's 401 documents, each run twice on `main` and twice on the branch, alike each time: `main`'s 1,489
    findings, line for line, and the same again after every object was read first; three Quartz files of the remote
    corpus rebuilt when a validation starts, their damage one no rule reaches; one document's `xref.rebuilt` earlier in
    its list; every other diagnostic as on `main`;
  - the integration suite against qpdf in its container, twice, with the remote documents this container holds: 3,688,
    3,682 passed and 6 skipped where qpdf cannot walk a damaged document's pages;
  - `ObjectStreamDependencyBenchmarks`, `ValidationBenchmarks` and `ReaderBenchmarks` (ShortRun), twice, `main` and the
    branch alternated, and the validation rows again under a medium job: the walk of a thousand object streams sharing a
    16 KB `/DecodeParms` took 118 to 128 ms and 125.6 MB on `main`, and takes 4.1 to 4.3 ms and 2.3 MB (a hundred, 12.7
    to 15.3 ms and 12.6 MB, then 1.4 to 1.5 ms and 521 KB); settling a sound document's index, a thousand pages, takes
    0.25 to 0.28 ms and about 50 bytes, beside the 0.30 to 0.35 ms of opening it; validating allocates what it did on
    `main`, to 40 bytes, and took 3.0 and 3.7 ms there, 3.4 and 3.6 ms on the branch, opening and validating 8.4 and 9.3
    ms, then 9.2 and 9.7 ms, settling within a gap the runs' spread covers; opening allocates about 20 bytes more, the
    reader's new fields (5.48 → 5.50 KB), its times within the runs' spread;
  - the site builds, 263 pages;
  - `Remote corpus` runs 59 and 60 passed whole at their first attempt, run 59 on the branch's code but its last two
    tests, run 60 on the code it ends with: 242 documents, 6,699 acceptance tests and 3,760 referee checks;
  - commitlint finds no problem.
- **Next**: batch 14, [#129] and [#175].

### 2026-10-15 — Slice 4's blockers, brought forward: [#141] and [#144]
- **The question.** The maintainer asked for the issues of M02 that block slice 4 ([#60]) to be paid first, one commit
  each, in one pull request: [#141] and [#144], batch 23 and half of batch 24. [#141]: ASCII85, ASCIIHex and
  RunLength passed over what their format does not allow — a byte outside the alphabet, a `z` inside a group, a group
  past 2^32 − 1, a `~` without its `>`, a missing end marker — in silence, and LZW took data without its end-of-data
  code as whole. [#144]: a document's diagnostics held a stream's filter fault once for each decoding — the reader's,
  a rebuild's, the validator's and a caller's —: PDFBox's object stream 417's fault three times after a full read and
  four after a validation; with `MaxDecodedStreamLength` at 1 KB, 56 corpus documents held a guard's report two or
  three times. Measured first, each claim twice, the ASCII85 classes against pikepdf as well: the corpus's 1,627
  ASCII85 streams hold one anomaly, Distiller 1.0.2's thumbnail, object 1363, without its `~>` and ending on a single
  digit; it holds no ASCIIHex or RunLength stream, and its 837 LZW streams all end on their end-of-data code. Five
  questions followed.
- **Settled with the maintainer**, each as recommended:
  - every error ISO 32000 names in these formats is reported, with a missing end marker and a `<~` introducer;
    nothing after the end marker (Q1);
  - a missing end marker is a repair after a whole group, pair or run, a warning inside one; bytes passed over and
    broken markers are warnings (Q2); LZW's missing end-of-data code follows the same rule, in [#141]'s commit (Q3);
  - the document's own list holds each report a decoding makes once, whoever decodes the stream — the reader, the
    validator, a caller passing that list or none —; a list a caller passes of its own gets every decoding's (Q4);
  - a guard's report is held once too, the guard still counted each time it is reached, and `ThrowOnLimit` throwing
    at each decoding; ADR 34 amended (Q5).
- **Done**, in [#254], one commit per issue:
  - [#141]: the four filters decode such data as other readers do and report it as `filter.failed`, once for each
    kind and step. A byte that encodes nothing is passed over, a warning at the byte of the filter's data where the
    first lies, where qpdf and MuPDF stop; an ASCII85 group past four bytes keeps its low-order bytes, a warning at its
    byte; a `~` that something other than `>` follows ends the data, and a last group of one digit is dropped,
    warnings; a `<~` introducer, or a last `~` with no `>`, is a repair. Data without its end marker decodes to its
    end: a repair after a whole group, pair, run or code, a warning inside one. Nothing is reported for what follows
    an end marker, nor for the end of data the reader cut or a filter stopped at the bound, and a count of what a
    filter stopped at the bound passed over says so;
  - [#144]: a decoding reports into a list the document's diagnostics keep (`PdfDiagnostics.Decodings`), whose
    reports are then moved in only past the copies of each the document holds: a stream decoded again adds nothing,
    two filters of one decoding that report alike are both held, a report the capacity left out is counted again, as
    any is, and a guard is counted each time it is reached — the move made in a `finally`, so that a decoding a guard
    ends by throwing holds what it met before. A list a caller passes of its own is reported to as before.

  Docs: ADR 34 amended (the guard's report held once); `reference/diagnostics.md` (each filter's faults and their
  severities), `reference/reader-limits.md`, `concepts/diagnostics.md`; the threat model's rows on a filter's data
  read in silence and on a stream decoded again and again, its known gaps without [#141] and [#144];
  `PdfDiagnosticCodes.FilterFailed` and `Decode`'s documentation; M02.
- **Reviewed**, twice.
  - The first review, over five lenses, gave 29 findings, each put to two refuters: 20 kept, 3 refuted, and 4 of the
    6 left without a verdict weighed by hand and kept — 16 distinct, all fixed. ASCII85 reported a missing `>` on
    data the reader cut; LZW data cut inside a code was a repair; two filters of one decoding that reported alike were
    held as one; a repeat within one decoding took room under the capacity, so that a new report was dropped while
    there was room; "a run of 1 bytes"; what a filter stopped at the bound passed over was counted as the stream's;
    five texts; three defenses no test held, which mutants found; a test's name and a test's comment.
  - The second, over five lenses aimed at the fixes, gave 9 findings: 6 put to a refuter, 4 kept, 2 refuted, and the
    3 minor ones weighed by hand and kept — all fixed. `FilterFailed`'s documentation listed a last `~` among the
    warnings and promised positions some messages do not give; no test held that the document marks as held only the
    reports it kept; of two alike reports of one decoding, the copy the capacity dropped was not counted again when
    the stream was decoded again; two comments; three of the four texts that say decoding stopped at the bound held by
    no test.
- **Tests**: 231 new unit tests, 3,687 to 3,918, and 21 integration, 1,306 to 1,327: `TextFilterRefereeTests`
  holds each case to qpdf — every stream qpdf cannot decode past is reported, every stream it decodes to its end
  decodes to the same bytes —, and a theory over the corpus holds every document to each report once however often
  its streams are decoded. 31 mutants of the batch's defenses, each run against the suite: all failed a test, one once
  a test held the count of what one decoding's capacity left out, and one rewritten to compile.
- **Tracking.** Filed: [#253], under M02, placed with batch 13: the nightly run 53 failed on `main` on one test, a
  window of `HostileInputTests` that holds 4 KB and counted 9,000 bytes — #247's excess of one allocation quantum, in a
  window #247's fix did not reach. Alone, in 40 runs, the test never failed; beside two threads that allocate as
  parallel tests do, 109 to 222 of 400 windows went over, each paused by a collection, in both runs. The one other
  defect met outside the two issues, the Flate message's "the 1 bytes decoded", is [#241]'s.
- **Checked**:
  - the solution builds with no warning, and `dotnet format` finds nothing;
  - each of the two commits builds with no warning and passes the full unit suite alone, with the remote documents
    this container holds: 5,644 then 6,056 tests, 3 skipped;
  - the unit tests, 3,918, 19 skipped; 6,056 with the 233 remote documents this container holds, 3 skipped;
  - the patch's 166 measurable lines of `src/` covered, every branch taken; 99.56 % of `src/` in all, 6,383 of 6,411
    lines, on the committed corpus alone;
  - the corpus's 401 documents, each run twice and alike: `main`'s 1,489 findings, line for line, and `main`'s
    diagnostics but for what the batch changes. Validation holds one report fewer, PDFBox's repeat. Each stream decoded
    twice into a list of its own reports as on `main`, but for two reports on object 86 of the copyright office's RC4
    summary, ciphertext the reader cannot decrypt before M16, as the measurement foresaw. Decoded twice into the
    document's own list, the streams leave 2,311 reports where `main` left 3,391: none lost, the two new ones held once,
    1,082 repeats gone in 43 documents, none held twice;
  - the integration suite against qpdf in its container, twice: 3,287, 3,281 passed and 6 skipped where qpdf cannot
    walk a damaged document's pages;
  - `FilterBenchmarks` and `ReaderBenchmarks` (ShortRun), twice, `main` and the branch alternated, the times within the
    runs' spread: a decode allocates 16 bytes more, the two fields each list of diagnostics gains (86.54 → 86.55 KB for
    a Flate stream of 64 KB, 97.62 → 97.63 KB for a chain of ASCII85 and Flate); indexing a document 30 bytes more (5.45
    → 5.48 KB), and reading its pages 130 bytes more, the list its decodings report into (62.45 → 62.58 KB for ten,
    5,818.80 → 5,818.93 KB for a thousand). On a Flate stream that turns corrupt, BenchmarkDotNet counts 180 bytes more
    (112.62 → 112.80 KB), where a count over a thousand warm decodings, taken twice on each side, finds 152 fewer
    (115,248 → 115,096 bytes): the batch keeps nothing more on that path, whose cost is the exception the inflater
    throws;
  - the site builds, 262 pages;
  - `Remote corpus`: run 52, on the branch's first code, and run 54, on the code its second review changed, passed whole
    at their first attempt, run 54 with 6,116 acceptance tests and 3,350 referee checks; run 55, on its last code, lost
    ECan's scan and the ipres2017 archive at its first attempt, and the archive alone at its one rerun ([#204]), every
    test over the 154 others passing, 5,223 acceptance tests and 2,583 referee checks; the 88 it lost pass on that code
    in this container's unit and integration runs;
  - commitlint finds no problem.
- **Next**: batch 13, [#128], with [#237] and [#253], measured on 2026-10-15 but [#253], its questions to put.

### 2026-10-15 — Batch 12: filter chains ([#165], [#166], [#199], [#209], [#219], [#247])
- **The question.** [#165]: a filter's parameters can name an object an object stream holds, and decoding one stream
  decoded another while holding its own output, 64 deep: about 250 MB live for a chain of 4 MB object streams, where
  `MaxNestedLoads`' reason said no level held decoded data. [#166]: `MaxDecodedStreamLength` bounded each filter of a
  chain alone, so that a 513-byte cross-reference stream naming `/RunLengthDecode` sixteen times allocated 3.3 GB as
  it opened, and a sound chain of stored Flate layers 14.9 GB, nothing reported. [#199]: what a filter before the bound
  kept was handed on as whole data, and the filters after it reported a lost tail, a missing checksum, a digit without
  its pair — the guard's cut as the file's damage. [#209]: an element of `/Filter` that names no filter was skipped,
  and the filters after it read data they could not. [#219]: the Arlington walk typed `/DecodeParms` by nothing, and
  tied. [#247]: the allocation windows of four tests paused for a collection now and then, and failed. Measured first,
  each claim twice; thirteen questions followed, and two more after the review.
- **Settled with the maintainer**, each as recommended:
  - after a cut, the filters after it decode what was kept, as data the reader cut, under one report (Q1);
  - the filters before the last share one `MaxDecodedStreamLength` for what they pass along together, and the last
    keeps its own: a stream of one or two filters decodes as before; no new property (Q2);
  - a Flate step that decodes nothing ends the chain (Q3); an element that names no filter ends it, reported with its
    place and kind, and a `/Filter` that resolves to null is one the stream does not have (Q4);
  - parameters paired as qpdf pairs them (Q5); every value a step reads resolved before any data, and nothing more
    (Q6); a hex digit or ASCII85 group a cut parted is dropped, a predictor over cut data undoes the part of its last
    row (Q7);
  - Arlington types `/DecodeParms` by the filter at the same place (Q8);
  - the tests and the docs: GC sampling for the nested chain, the measured factor of a Flate decode, real shapes,
    hostile ones and a property, three pause-free windows (Q9 to Q12); `HasImageFilter` through the same resolver,
    abbreviated names filed as debt (Q13);
  - after the review: a seventh guard, `MaxFilterChainLength`, 64 by default, `limit.filter-chain` (Q14); `/Crypt`
    neither charged nor the last (Q15).
- **Done**, in [#252]:
  - a stream resolves its filters and every value its steps read before any of its data (`FilterChain`): the chain of
    64 object streams of 4 MB, each needing the next's parameters, holds 32 MB and three bounds at most, where it held
    about 250 MB; only what a step reads is resolved — LZW's `/EarlyChange`, a Flate or LZW step's predictor
    values —, and a parameter past an int is the int nearest it, not its low bits;
  - the filters before the last share `MaxDecodedStreamLength` for what they pass along together, the last keeping
    its own; the 513-byte cross-reference stream of sixteen `/RunLengthDecode` opens in a few bounds' worth, the same
    with sixty-four. `/Crypt`, which passes its data along as it is, is neither charged nor the last;
  - a Flate step that decodes nothing ends the chain, read once rather than once an entry: a hundred thousand
    `/FlateDecode` over bad data took 424 MB and two seconds;
  - after a cut, the filters decode what was kept as data the reader cut, under one report; a fault or a checksum in
    what was read stays the file's. A hex digit or ASCII85 group the cut parted is dropped, one its end marker closes
    completed; a predictor over cut data undoes the part of its last row it holds;
  - an element of `/Filter` that names no filter ends decoding, `filter.unsupported` with its place and kind;
    `HasImageFilter` answers from the same chain;
  - `PdfReaderLimits.MaxFilterChainLength`, 64, `limit.filter-chain`, `int.MaxValue` under `Unbounded`: decoding,
    `HasImageFilter` and the cross-reference stream's check read nothing past it, and a cross-reference stream or an
    object stream it ends is read as cut before its data. Forty million `/Crypt` in a 350 KB file cost 1.8 s a decode;
  - the chain keeps nothing for a name its `/Filter` array holds, and decoding stops at data a filter decoded to
    nothing: that array ran `Decode` and `HasImageFilter` out of memory under 2 GB, the chain keeping 32 bytes a name,
    and a hundred thousand LZW steps over nothing allocated 4 GB;
  - Arlington types `/DecodeParms` by the filter at its place, and `/FDecodeParms` by `/FFilter`: the corpus's ties
    fall from 4,497 to 3,364, its findings and diagnostics unchanged;
  - the four allocation tests [#247] names measure the least of three windows no collection paused.

  Docs: ADR 34 amended twice, the second time for the seventh guard and `/Crypt`; `reference/reader-limits.md`,
  `reference/diagnostics.md`, `reference/validation-rules.md`, `concepts/reader-limits.md` (*What decoding a stream may
  hold*); the threat model's rows on filter chains, nested loads and cuts; `architecture.md`, `corpus.md`,
  `corpus-contributions.md` (a real chain of three filters wanted), M23.
- **Reviewed**, twice.
  - The first review, over five lenses, gave 19 findings, each put to two refuters: 12 kept, all fixed. The chain
    kept 32 bytes for every element of `/Filter` on every decode and every `HasImageFilter`, and forty million
    `/Crypt` in an object stream's member ran both out of memory; `/Crypt` was charged to the shared bound; a filter
    with no data to decode still set up its work; four texts described the chain as it was before; four choices no
    test held. The figures it brought — a chain's cost linear in its length, seconds for an array an object stream
    holds — were put to the maintainer, who chose the seventh guard and `/Crypt` out of the bound (Q14, Q15).
  - The second, over five lenses aimed at the fixes, gave 12 findings, 11 kept, all fixed: the corpus manifest's
    schema left without the new guard, two unit tests red from the guard's commit, which was rewritten so that every
    commit passes alone; a cross-reference stream or object stream the guard ended read as decoded, so that a sound
    file was rebuilt, or its object stream judged unreadable, an Error; `/EarlyChange` 2^32 read as 0; three
    allocation windows that measured the first assertion of a process; a dictionary serving a Flate and an LZW step
    no test held; and three texts.
- **Tests**: 144 new, 3,543 to 3,687; each case the fixes exist for fails on the code before them. 55 mutants of the
  batch's defenses, each run against the suite: 53 failed a test, one more once a test held `/Columns` past an int;
  the last, which makes the filters before the last share a bound already at `Array.MaxLength`, differs only once
  they pass along more than about 2 GB together, which no test can hold here.
- **Tracking.** Filed: [#250], abbreviated filter names in a stream's `/Filter`, under M02, placed with [#162]; [#251],
  a Flate decode allocating about three times its output, under M23. Commented: [#162] (an uncut ragged row left as the
  predictor left it, and what of its reports this batch paid).
- **Checked**:
  - the solution builds with no warning, and `dotnet format` finds nothing;
  - each of the batch's 21 commits builds with no warning and passes the full unit suite alone;
  - the unit tests, 3,687, 18 skipped; 5,592 with the 233 remote documents this container holds, 3 skipped;
  - the patch's 316 measurable lines of `src/` covered, every branch taken; 99.55 % of `src/` in all, 6,236 of 6,264
    lines, on the committed corpus alone;
  - the corpus's 401 documents give `main`'s findings and diagnostics, line for line;
  - the integration suite against qpdf in its container, twice: 3,266, 3,260 passed and 6 skipped where qpdf cannot
    walk a damaged document's pages;
  - `FilterBenchmarks` (ShortRun), twice, `main` and the branch alternated: a decode allocates 112 bytes more, the
    resolved chain — 86.43 → 86.54 KB for a Flate stream of 64 KB, 97.51 → 97.62 KB for a chain of ASCII85 and Flate —,
    and takes as long, within the runs' spread: 86 to 88 µs on `main` and 88 to 104 µs on the branch for the 64 KB
    stream, 9.6 to 10.2 ms and 10.4 to 10.6 ms for the 4 MB chain;
  - the site builds;
  - `Remote corpus`: run 51, on the batch's last code, passed whole at its first attempt — all 242 documents, 5,649
    acceptance tests and 3,329 referee checks;
  - commitlint finds no problem.
- **Next**: batch 13, [#128], with [#237].

### 2026-10-14 — Batch 11: the index's size, the first new guard ([#164], [#183])
- **The question.** [#164]: nothing bounded how many entries the index held. A cross-reference stream of one-byte
  rows, `/W [0 1 0]` under two Flate filters, decodes a few hundred bytes to the 256 MB bound and claimed an entry a
  row — 268 million, 20 GB and more — until the process ran out of memory, with nothing reported; and the map grew
  by doubling, allocating about 1.93 times its final size on the way. [#183]: a rebuild counted every header it
  read and stopped in silence after two million, so that 2,200,000 copies of `1 0 obj` hid the document after them;
  and a sound file whose table or chain a guard cut was rebuilt as damaged — `xref.rebuilt`, an error under ADR 45
  —, losing what the chain had read past the two millionth header. Measured first, each claim twice, which found
  more: a classic table `MaxXRefSectionLength` cut lost its trailer, its `/Encrypt` with it, so that an encrypted
  file opened as plain; and neither `MaxSubsectionEntries` nor `MaxRepairObjects` bounded what ADR 34 said, nor was
  reached by any test. Thirteen questions followed.
- **Settled with the maintainer**, each as recommended:
  - a guard's cut is no damage: what lies past it reads as null, unindexed, until the caller lifts the property the
    report names, and the index is not rebuilt for it; a rebuild that damage forces anyway keeps what the cut chain
    read; a chain a guard leaves with no section read is rebuilt, as before (Q1, Q13);
  - `PdfReaderLimits.MaxIndexEntries`, `limit.index-entries`, 8,388,607 by default, the most Annex C advises,
    `int.MaxValue` under `Unbounded`, a fixed count: the distinct object numbers the index holds, free and refused
    ones included, through one gate — the chain's rows, a refused row, the rebuild's scan and the members a rebuild
    takes in. `MaxSubsectionEntries` and `MaxRepairObjects` fold into it (Q2 to Q6);
  - a full index reads a number it lacks as null, rebuilding nothing, reported once a document where it filled;
    under `ThrowOnLimit` a rebuild finishes, then throws, and each later lookup past the bound throws (Q7, Q9);
  - the index makes room for a section's rows before reading them, bounded by what the section holds (Q8);
  - the validator says once, `xref.checked-in-part`, where the index filled, and takes an object a guard hid as
    unknown in every rule (Q10);
  - a classic table `MaxXRefSectionLength` cuts keeps its trailer (Q11).
- **Done**, in [#249]:
  - `MaxIndexEntries` bounds every number the index takes; reaching it stops the chain at the section that filled
    the index, a stream's rows past it unread. The one-byte rows decoding to the bound open in 675 MB under a guard of
    a million entries, most of it the decoding;
  - the rebuild scans to the file's end, counting distinct numbers: a header for a number it holds replaces the entry,
    past the bound too, so that the newest definition wins;
  - a subsection's count is bounded by what it numbers, `first + count − 1` compared by subtraction;
  - a table a guard cuts, or whose rows run past a full index, keeps its trailer, found past the rows left unread a
    megabyte at a time, from the start of the line the cut fell in: a comment is skipped, anything else no row holds
    is the section's fault, and the search goes on to the keyword;
  - what a guard leaves out of the index reads as null, `ObjectPresence.Unindexed`, rebuilding nothing; the chain
    ends at any cut a guard makes — a table's, a stream's rows, a section or a trailer cut wherever the cut falls —,
    and a section a guard cut is not looked for elsewhere. A cut is known by the guards the diagnostics count, kept
    or dropped, not by their reports;
  - the validator takes a catalog, an object stream, a kid or a `/Length` a guard hid as unknown, judging the chain's
    index as a guard stopped it; a row refused before a cut is the file's fault;
  - the index makes room for a section's rows once, before reading them: as many as a stream's data holds, or a
    table's lines up to the first byte no row holds, at 19 bytes at least; within the newest `/Size`, or the highest
    number the sections give when it is stale, and `MaxIndexEntries`; once a doubling comes within a doubling of the
    `/Size`, room is made for that size. `IndexBenchmarks` (ShortRun, twice): opening 300,000 objects allocates
    16.09 MB from one table, where it allocated 31.06 MB; 21.46 MB from one stream, where 36.42 MB; 18.26 MB for an
    update over a table, where 31.06 MB; 21.46 MB for a Word hybrid file, where 36.42 MB; 29.15 MB for a chain of a
    thousand sections, where 31.66 MB;
  - System.Security.Cryptography.Pkcs pinned to 8.0.2, past the 8.0.1 iText asks for: two advisories published on
    2026-10-14 made a fresh restore of the comparison benchmarks fail, `main` included, reproduced twice; Dependabot's
    [#246] proposes the same pin.

  Docs: ADR 34 amended; `reference/reader-limits.md`, `reference/diagnostics.md`, `reference/validation-rules.md`,
  `guides/handle-reader-limits.md`, `concepts/lazy-reading.md` and the concepts on limits; the threat model's guards
  table gains the sixth guard, the gaps [#164] and [#183] named become defenses, and the indexing rows say how the
  trailer is found past a cut and when the chain ends; `corpus.md`, M02 and M23.
- **Reviewed**, twice.
  - The first review, over five lenses, gave 27 findings, each put to two refuters: 25 kept, 2 refuted by both,
    room made for a sparse `/Size` and for a lying count, which the decisions chose — the measures behind them
    still reshaped the presizing. Fixed among them: a count past `long` wrapping the numbering check once
    `MaxSubsectionEntries` went; a trailer past a cut lost to a comment or a damaged row, and with it `/Encrypt`;
    `file.root-invalid` and `xref.object-stream-broken` blaming the file for what a guard hid; a catalog packed past
    a cut rebuilding the index as the document opened; a hybrid stream's cut rows letting an older revision stand.
  - The second, over four lenses aimed at the fixes, gave 20 findings, each kept: the search for the trailer read a
    megabyte again for each comment or stray candidate — a file a little past the default bound took close to an
    hour opened from a path —; a section a guard cut was relocated to the nearest one, which stood for it; a trailer
    cut past its `/Prev` let the chain go on; an array or a typed object at a `/Prev` was taken for a cut stream; a
    cut taken for damage once the diagnostics were full; and the presizing overshooting where rows are 19 bytes long,
    a `/Size` stale, or the rows read again. All fixed.
- **Tests**: 104 new, 3,439 to 3,543; each case the fixes exist for fails on the code before them. 80 mutants of the
  batch's defenses, 53 after the first review and 27 after the second, each run against the suite: 77 fail a test,
  eleven of them once the runs had shown that no test held them, two rewritten to compile. One went with the code it
  mutated, a clamp the cap made redundant; two change nothing a test can see: a negative `/Size` narrowed, which the
  highest number read now bounds at zero, and a line end skipped after a comment, which is never significant.
- **Tracking.** Nothing filed. Commented: [#158] (its `MaxRepairObjects` item paid, under `MaxIndexEntries`),
  [#169] (its first item paid on the reader's side: a guard reached is counted whatever the capacity).
- **Checked**:
  - the solution builds with no warning, and `dotnet format` finds nothing;
  - each of the batch's commits builds with no warning and passes alone;
  - the unit tests, 3,543, 3 skipped; 5,448 with the 233 remote documents this container holds;
  - the patch's 278 measurable lines of `src/` covered, every branch taken but one the compiler adds on a `long?`,
    partial before the batch (3040); 99.5 % of `src/` in all, 6,051 of 6,080 lines, on the committed corpus alone;
  - the integration suite against qpdf in its container, twice: 3,266, 3,260 passed and 6 skipped where qpdf cannot
    walk a damaged document's pages;
  - `IndexBenchmarks` and `ReaderBenchmarks` (ShortRun), twice each, `main` and the branch alternated;
  - the site builds, 262 pages;
  - `Remote corpus`: run 46, on the batch's last code, passed whole at its first attempt — all 242 documents, 5,505
    acceptance tests and 3,329 referee checks. The branch's commits were reordered after it, the pin first so that
    each restores, the code unchanged;
  - commitlint finds nothing.
- **Next**: batch 12, [#165] and [#166], filter chains, with [#199], [#209], [#219] and [#247].

### 2026-10-13 — Batch 10: the reader's public surface ([#190], [#212])
- **The question.** [#190]: `PdfDocument.Version` said 1.4 of a file that declares no version, `Open(Stream)` read a
  `MemoryStream` from its origin or from its position depending on how it was built, and a disposed document kept
  answering through every member but `GetObject`; [#212]: the objects' public `ToString` wrote what the file wrote, raw
  and whole, and `PdfReal`'s rounded to six decimals. Measured first, each claim twice, by seven agents and a critic,
  who found more than the issues named: `Version` ignored the catalog's `/Version`, which ISO 32000-1 lets raise it —
  nine Quartz documents of the corpus declare 1.4 or 1.6 over a 1.3 header —, and read `%PDF-1.7abc` as 1.7 in
  silence; a seekable stream a caller had read the first bytes of, to tell its type, and not rewound, opened as a
  damaged file, with an untrue `header.missing`, objects lost and, past 1,024 bytes, the catalog; once a caller reused
  its buffer after `Dispose`, a held reference read another file's objects beside the first's cached ones, with nothing
  reported; and `((IDisposable)reference.Source).Dispose()` closed the file of a document still in use. The referees
  disagree on a missing version: qpdf 1.2, MuPDF 1.7, PDFBox and PdfPig 1.4, poppler 0.0. Ten questions followed.
- **Settled with the maintainer**, each as recommended:
  - after `Dispose`, every member but `Diagnostics` throws `ObjectDisposedException`, and so does whatever reaches
    back from an object taken before, cached or not, whatever the opening; disposal lets go of what the reader kept;
  - `Version` is null when the header names no version, and is the version the document declares: the header's, or
    the catalog's `/Version` when that is later, worked out as it opens; a header without a version declares none,
    whatever its catalog says;
  - a header names a version as one digit, a period and one digit, then white space, a comment or the end of the file;
  - `Open(Stream)` reads a seekable stream from its start, one that cannot seek from where it stands, and only a
    `MemoryStream` itself in its buffer;
  - a name, a dictionary and a stream describe themselves as the messages quote a name, through `FileQuote`; a string
    its text, escaped as C# writes it and cut past 127 characters; a real the shortest decimal that reads back, with a
    point, which `page-tree.mediabox-invalid` uses too;
  - the reader stops being disposable, in this batch.
- **Done**, in [#248]:
  - `PdfReal.ToString` writes the shortest decimal that reads back, never an exponent, always a point; the reader's
    messages quote reals through it, and `page-tree.mediabox-invalid` quotes each corner as the file wrote it;
  - `PdfName`, `PdfDictionary` and `PdfStream` describe themselves through `FileQuote`, "1 entry" and "1 byte" in ASCII;
    `PdfString` between double quotes, escaped, cut past 127 characters, counting a UTF-16 text without decoding it;
  - `Version` is `string?`, the declared version, worked out once at opening; what follows `%PDF-` is recorded up to
    the white space or comment that ends it, so that `file.header-version-invalid` judges `1.7abc` whole and quotes it
    bounded, "at least" when the 4,096-byte search cuts it; `ArlingtonWalk` takes the declared version from `Version`;
  - `FromStream` reads a seekable stream from its start, into an array of its length; only an exact `MemoryStream` is
    read in its buffer;
  - the reader closes as its document is disposed: `GetObject`, a stream's data — one an object stream holds included
    — and an enumeration of `ObjectNumbers` refuse, whatever was read before, and the cache, the decoded object
    streams, the index, what was noted of each object and the source are let go; the reader is no longer disposable.

  Docs: `reference/object-model.md`, new; `reference/validation-rules.md`, `concepts/validation.md`,
  `guides/tune-reader-memory.md`; the threat model's *Messages* boundary takes in the objects' `ToString`, the rows
  for use after dispose and for the caller's stream are rewritten, [#190]'s gap goes; `M03.md` says what an input that
  declares no version is written as; the "7.2.2" two places cited for the header is 7.5.2.
- **Reviewed.** An adversarial review over five lenses — the reader, hostile inputs, the tests, the documents, and the
  corpus before and after — gave 25 findings, each put to two refuters: 22 kept, 20 by both, 3 refuted.
  - Kept and fixed, among them:
    - a stream an object stream holds, as no file should, still gave its data after `Dispose`;
    - closing kept what the reader noted of each object: one held reference kept 41.7 MB after disposing of a file of
      300,000 streams whose `/Length` was wrong;
    - `PdfString.ToString` counted a UTF-16 text by decoding it, a quarter of a gigabyte for a 15 MB string of lone
      surrogates;
    - a header's text the search cut was quoted with what the window held as its whole;
    - a seekable stream copied by doubling kept up to twice the file, a path derived `MemoryStream`s now took:
      `StreamOpenBenchmarks` (ShortRun) gives 16.26 MB and 6.4 ms before, 4.5 MB and 2.0 ms after;
    - the object model's page planted a raw right-to-left override where it meant its escape; bounds and examples
      wrong; tests that could not fail.
  - Refuted: a catalog `/Version` naming a version the model does not describe turns version-aware rules off — the
    decision, as for a header naming one; a stream's data keeping its reader alive — it is the document's, until
    disposed; a stream that claims to seek and refuses its position — the caller's contract, broken.
- **Tests**: 126 new, 3,313 to 3,439 (5,344 with the remote documents this container holds); each case the fixes
  exist for fails on the code before them. 52 mutants of the batch's defenses, each run against the suite: 51 fail a
  test once the four the first run left were held and four rewritten to compile. One changes nothing a test can see
  and stays as a defense: `GetObject` refusing before its cache, which the closing empties anyway.
- **Tracking.** Filed [#241] — messages say "1 bytes into the file" and "found -1 bytes from where the index said" —
  reproduced twice, placed with [#188] in batch 19; [#247] — `FilterDamageTests` holds sound data's allocation within
  1 KB of a measure the runtime can count 8 KB over, in a window that allocates on the large-object heap — reproduced
  twice on the branch and three times on `main`, placed with [#165] in batch 12; and [#240] under M03 — its full
  rewrite would keep a catalog `/Version` lower than the header, which MuPDF, pdf.js and PDFium read as the
  document's version — reproduced twice.
  Commented: [#204] (the nightly runs 37, 38 and 42), [#149] (the position rule, the documentation now true, and what
  a copy past 2 GB throws), [#191] (references refuse, and the reader is no longer disposable), [#161] (a stream an
  object stream holds refuses once disposed).
- **Checked**:
  - the solution builds with no warning, and `dotnet format` finds nothing;
  - each of the batch's commits builds with no warning and passes alone, but for one test once: the run over the
    final series failed `FilterDamageTests`' allocation budget for sound data on a commit of documentation alone,
    whose code passed beside it and in the run before. Run again, 20 times alone and 20 times in the suite at the
    head, it passed; it is [#247], older than the batch;
  - the unit tests, 3,439, 3 skipped; 5,344 with the 233 remote documents this container holds, 3 skipped;
  - the patch's 168 measurable lines of `src/` covered, every branch taken but two: one the compiler adds
    (`FileQuote` 145) and `Expanded`'s defensive arm, moved into `PdfReal` (66), both partial before the batch; 99.5 %
    of `src/` in all, on the committed corpus alone;
  - the integration suite against qpdf in its container, twice: 3,266, 3,260 passed and 6 skipped where qpdf cannot
    walk a damaged document's pages;
  - the corpus read, validated and described whole, twice, before the batch and with it: 14 versions change as the
    decisions say — nine Quartz documents, four headerless and `%PDF-14` —, and no diagnostic, finding or message;
  - the site builds;
  - `Remote corpus`: runs 43, before the branch's last two commits, and 44, on its last code, lost the 88 documents
    of the ipres2017 archive at their first attempt, its host timing out ([#204]), every other test passing; run 44
    passed whole at its one rerun, 5,401 acceptance tests and 3,329 referee checks over all 242 documents;
  - commitlint finds nothing; four messages were reworded, three before the pull request and one, a body line too
    long, just after it opened, the code of every commit unchanged.
- **Next**: batch 11, [#164] and [#183], the index's size, the first new guard.

### 2026-10-04 — Batch 9: offsets past what a long holds, and where a trailer's finding goes ([#125], [#126])
- **The question.** [#125]: the reader added the header's offset to every offset the file gives unchecked, so five
  bytes before the header turned a `startxref`, a `/Prev` or a row of 9223372036854775807 into a negative position;
  [#126]: a finding on a value the trailer holds was located at the first section's trailer, past the end of the file
  when `startxref` named an offset past it. Measured first, each claim twice, on files built independently by five
  agents and a critic. The wrap reached far more than [#125] named: the loop and the cut read as none, so
  `xref.chain-loop` and `xref.checked-in-part` went unsaid while `file.size-wrong` and `page-tree.page-orphaned` judged
  a cut chain; a section named that far read as named by no offset; rows of classic tables, which the lexer reads at any
  width, and of cross-reference streams wrapped too, and with 513 bytes or more before the header the object was looked
  for in the file's first kilobyte; `Validate` threw where an object stream's finding was located, at a wrapped row or
  at a negative one with no wrap at all ([#167]'s); `stream.self-reference` and `syntax.depth-exceeded` were placed
  outside the file; and a negative row behind bytes before the header landed in them and was judged shifted. Trailer
  findings went past the end, to an offset holding no trailer, or to the file's length when the first table ran to it.
  The corpus — 401 documents, 18 with bytes before their header, offsets up to 146,707,130 — reaches none of it: its
  records, read and validated whole, are the same before and after. The referees: qpdf refuses the shift, says so and
  rebuilds; poppler and pdfium check the sum; MuPDF refuses to open a file whose `/Prev` is the largest long; pypdf and
  pdfminer.six throw. Four questions followed.
- **Settled with the maintainer**, each as recommended:
  - a message names an offset whose shift overflows exactly, on 64 unsigned bits; the records keep a position
    saturated at `long.MaxValue`, past the end of any file, and the offset as written beside it, which the three rules
    that name it read;
  - a finding on a trailer value is located at the newest trailer the reader merged: the first section's when the
    chain read it, otherwise the newest a rebuild found, otherwise the document;
  - one test in the header's frame at every shift site, the validator's two copies included, negative offsets
    included: a negative offset lies before the file's start;
  - two neighbors paid: `xref.chain-loop`'s wording for an offset outside the file, and `PdfDiagnostic.Position`
    documenting that the file's length designates its end; the rest filed or commented.
- **Done**, in [#238]:
  - `PdfFileReader.PositionOf` tests an offset before adding the header's: negative, kept as given; past what a long
    holds, `long.MaxValue`; `DescribeOffset` writes it exactly. Every shift site uses them: the chain's sections, loop
    and cut, the entries, the relocation of objects and sections, the read while the chain is read, the reports on an
    object, and the validator's probe and dependency walk, which locate a finding at the object alone when its row lies
    outside the file. A cut outside the file is placed at the section that names it, the offset in its message, as a
    missing section is (batch 2); the search near an entry reaches nothing more than 512 bytes before the file's start;
  - `XRefEntry.FoundByReader`: an offset the reader found, rebuilding or searching near a row, is a position, before
    the header though it be, and a section relocated there is read; the index leaves a row's negative offset out of
    the bounds of the search for an `endstream`;
  - `PdfFileReader.TrailerLocation`, the rebuild recording where its newest trailer starts, read by both walks;
  - a loop through an offset where the chain read no section says it only named it, in the file as outside it.

  Docs: `reference/diagnostics.md`, `reference/validation-rules.md`, `reference/validation.md` (where a trailer's
  finding goes), `reference/reader-limits.md`; the threat model's gap for [#125] made a defense row, with the rows for
  loops, cuts, sections, objects not where their entry says, object streams and the validator's locations, and
  [#167]'s gap without the negative entry.
- **Reviewed.** An adversarial review over five lenses — the reader, the validator, hostile files, the tests, the
  documents — gave 28 findings, each put to two refuters: 27 kept, 25 by both, 1 refuted.
  - Kept and fixed, among them:
    - a regression of the batch's own: a section found near its offset but before the header came back as a negative
      offset, outside the file, and the next candidate, the table naming it, made a loop — a section is now read at a
      position;
    - a negative row still bounded the search for an `endstream` in the bytes before the header;
    - a loop through an offset the chain named and never read said it had read a section there, beside
      `xref.section-not-found` saying it holds none;
    - `limit.xref-section-count` for a cut outside the file named no offset, where the wrapped position had carried it;
    - seven arms no test held, mutants the suite let live among them; documents that said more than the code.
  - Commented, not this batch's: [#188] (a relocation landing on the section that names it), [#197] (the nearby
    search's window near the file's start).
  - Refuted: that the guard before the file's start assumed a radius the window does not have — [#197]'s, and the
    guard narrows it.
- **Tests**: 57 new, 3,256 to 3,313 (5,218 with the remote documents this container holds). Each case the fixes exist
  for fails on the code before them. 40 mutants of the batch's defenses, each run against the suite: 37 fail a test
  once the two the first run left were held. Three change nothing a test can see, and stay as defenses: a section's
  relocation from an offset whose sum wraps, which stays outside the file; reading again at a rebuilt entry, every one
  of which the reader found; and a found entry bounding the search for an `endstream`, which stops at that entry's
  header anyway.
- **Tracking.** Filed [#237] — a rebuild the object graph's walk triggers merges trailer keys the walk has passed, so
  the first validation misses what the second finds — reproduced twice on two files; placed with [#128] in batch 13.
  Commented: [#167] (the negative entry paid), [#171] (a `trailer` keyword in a comment merged by the rebuild), [#154]
  (a cross-reference stream's dictionary the rebuild never merges, and where its trailer's findings would then go),
  [#188], [#197].
- **Checked**:
  - the solution builds with no warning, and `dotnet format` finds nothing;
  - each of the batch's commits builds with no warning and passes alone;
  - the unit tests, 3,313, 3 skipped; 5,218 with the 233 remote documents this container holds, 3 skipped;
  - the patch's 109 measurable lines of `src/` covered, every branch taken, on the committed corpus alone; 99.5 % of
    `src/` in all, the 28 lines left those left before the batch;
  - the integration suite against qpdf in its container, twice: 3,266, 3,260 passed and 6 skipped;
  - the corpus read and validated whole, twice, before the batch and with it: every record the same;
  - the site builds, before the rebase onto `main`'s redesign and after it;
  - `Remote corpus` runs 31 and 32 on the branch, green at their first attempt with all 242 documents, the second on
    the batch's last code; run 33, on it rebased onto `main`'s site work, which touches none of it, lost ECan's scan at
    its first attempt, web.archive.org refusing the connection ([#204]), and passed whole at its one rerun;
  - commitlint finds nothing; five messages were reworded before the first push, the code of every commit unchanged;
    the branch rebased once onto `main`, each commit re-signed and checked alone again, the patch's coverage the same,
    and the site built again.
- **Next**: batch 10, [#190] and [#212], the reader's public surface, its questions put when it starts.

### 2026-10-04 — The design system on the site, the README and the package, outside any milestone ([#235])
- **The question.** Apply the AdCodicem design system, as AdCodicem.ValueObjects did, to the site, the README and the
  package icon; scope and choices settled with the maintainer one question at a time.
- **In the design system first**: the compact Pdf lockups (`pdf-logo-horizontal-compact`, `-white`), which the navbar's
  34px calls for, and the AdCodicem lockups with their name outlined, for the footer. Their text is outlined the way
  the design system's Pdf lockup was, which it reproduces byte for byte (Archivo 600, no kerning, 0.47 H).
- **Done**: the tokens, fonts, syntax palette and four color schemes, with theme and contrast toggles; the Pdf lockup
  and favicons; the documentation under `/docs`, behind a homepage whose example is frozen with each stable line and
  compiles against the current API, with every former address redirected (checked on a locally frozen line); a 404
  page; a local search covering the user documentation, the API reference in an index of its own (232 KB instead of
  611 KB once its summaries and tables are left out); the README's banner; the package's icon, the Pdf tile.
- **Measured, and decided on it**: indexing the project documents would take the search index from 0.7 MB to 12.7 MB,
  fetched by every reader's first search; they are left out.
- **Left**: the startup hook failed to install the SDK, the Ubuntu archive's index being stale ([#234]);
  fixed the same day: the hook refreshes the index and retries when the install fails.

### 2026-10-03 — Batch 8: what the parser read in silence ([#119], [#172])
- **The question.** [#119]: an array or a dictionary left open until the end of the data was read in silence; [#172]:
  so were a string left open, a hexadecimal string's stray bytes, a name's `#` that is no escape, and a key given
  twice — a later null even kept the earlier value, where every other reader that keeps the last drops the key or
  reads it as null. Measured first, each run twice: 134 small files over eleven shapes in seven readers (ours at
  `c07e489`, qpdf 11.9.1, libqpdf 12, pypdf, PyMuPDF, PDFBox 3.0.5, pdf.js), ours the only one silent on every shape;
  and the 401 corpus documents, every parse the reader keeps instrumented — 1.75 million dictionaries —, where one
  document leaves a string, an array and a dictionary open at the end of the file (Foxit's garbage-`/BBox` signature
  file) and one a trailer (iPRES `t04-010`), and none holds a stray hexadecimal byte, a bad escape or a repeated key.
  The reader drops 552 end-of-input events, all at the edge of a window it grows, in 38 documents. The measurement
  found three more defects on the same paths: a fault of the syntax reported again whenever its object is parsed
  again; the rebuild's trailer scan reporting at its fixed 64 KB edge a fault the file does not have ([#49]'s); and a
  window that ends exactly where the file does, at `MaxObjectLength`, taken for the guard's cut. Eleven questions
  followed.
- **Settled with the maintainer**, each as recommended:
  - what the end of the data leaves open is `syntax.truncated-object`, one report per cut, for the innermost
    construct, the others around it counted, placed where it opens — the missing value's report moving to its
    dictionary's opening; inside an object stream's member, at the data's start, the member and the byte in the
    message;
  - the parser is told whether its buffer ends the data, and reports what the end leaves open only then: the rebuild's
    false report at its 64 KB edge goes, and a window that ends where the file does is no guard's cut;
  - a key given again keeps its last value, a null given last removing it, every repeat reported — written beside the
    null-drop decision in `docs/adr/README.md`;
  - three codes, `syntax.hex-string-invalid`, `syntax.name-escape-invalid` and `syntax.key-repeated`; `#00` stays
    the validator's;
  - every new `syntax.*` code met in a trailer or a cross-reference stream's dictionary makes it malformed;
  - bounding a member at the next member's offset stays [#160]'s; the validator gains no rule, its line rewritten;
  - four neighbors paid here: no message formatted once the document's diagnostics are full; a fault of the syntax
    reported once however often its object is parsed again; what precedes a guard's cut kept (ADR 34), nothing said
    of the token the edge touches; an `endobj` where a value or a key should be ends every container open, reported
    once — the keyword alone, not an object header.
- **Done**, in [#231]:
  - `PdfObjectParser` takes `endsData`; reports a cut once, where the innermost construct opens (`ReportCut`); tells a
    token the window's edge may have cut from one it cannot, a whole `>>` reported wherever it lies (`AtWindowEdge`),
    and a stream whose `/Length` the file cannot hold from an edge it met (`_lengthUnsettled`); ends the containers at
    an `endobj`; reports stray hexadecimal bytes and bad escapes, which `PdfStringDecoder` counts in the loops it
    already ran, and repeated keys (`Give`: one lookup an entry, the first key given null held alone, a set made for a
    second);
  - `PdfFileReader` tells each of its five parse sites whether its window ends the data, and keeps a `syntax.*`
    report once (`_syntaxReported`); an attempt a guard cut reports the guard first, then what it met before the cut;
    the rebuild's trailer scan parses through the pending diagnostics; `PdfDiagnostics.KeptIn` leaves a message
    unformatted only when the pending buffer or the document is full;
  - `ParserBenchmarks`, nine shapes, sound and hostile.

  Docs: `reference/diagnostics.md` (the three codes, and a section on the faults of the syntax),
  `reference/validation-rules.md`, `concepts/diagnostics.md`, the ADR index, the threat model's object syntax rows,
  its gaps for [#119] and [#172] removed and those for [#229] and [#230] added; the two corpus documents'
  `requiredDiagnostics`, and the manifest schema's codes.
- **Reviewed.** An adversarial review over five lenses — the parser, the reader, the tests, the documents, the
  decisions — gave 33 findings, each put to a refuter, and a completeness critic 2 more: 29 kept whole, 3 in part,
  1 refuted; several are one defect seen from more than one lens.
  - Kept and fixed, among them:
    - parsed again near the capacity, a fault was kept twice, the second time with the short message (four
      findings): a message is now left unbuilt only when the pending buffer or the document is full by itself;
    - a `>>` ending exactly at a window's edge silenced "a key had no value", and nothing read the trailer again: it
      was no longer judged malformed — a regression;
    - a key given again went unreported when its own value ran into a guard's cut, and a keyword the cut left short
      read as a null that removed the key's earlier value;
    - the guard's report, the one naming the property to raise, could be crowded out by what its object met first;
    - a trailer read through a source whose reads stop short of its length took its short window for one the file
      goes on past;
    - a string left open where a key should be was not named as the innermost construct; "the 1 bytes"; every
      dictionary that gave a null made a set, now made only for a second key given null;
    - tests for what a window that is not the file's end reports, a member parsed again, and each new report left
      unformatted once the diagnostics are full;
    - documents that said more than the code: which readers read `<< /B 3 /B null >>` as 3 (pypdf and Ghostscript
      do), what other readers make of a stray hexadecimal byte, an `endobj` taking nothing of the objects after it.
  - Filed: [#230] — an object that lost both its closing delimiter and its own `endobj` takes the next object up to
    its `endobj`, and `object.endobj-missing` is no longer reported for it; the object header the `endobj` decision
    left out would hold it.
  - Left: with a capacity of 0, a cross-reference stream whose dictionary is sound is judged malformed for its
    stream's length fault, which is [#169]'s.
  - Refuted: that the batch made [#49]'s and [#192]'s bodies false — their comments say what changed.
  - Found by mutation, after the review: a stream whose `/Length` the file cannot hold marked the parse as having met
    the window's edge, so a key given again after it, read whole, went unreported where the bound stopped the reader
    short of the file's end ([#174]'s case). The parser now asks for the larger window without taking the stream for
    an edge it met.
- **Tests**: 153 new, 3,103 to 3,256; 5,161 with the remote corpus. Each case the fixes exist for fails on the code
  before them. 39 mutants of the batch's defenses, each run against the suite: 37 fail a test. Two change nothing a
  test can see: the trailer's first reading told its window does not end the data, which keeps only a parse that met
  no edge; and the guard that leaves a key at a window's edge unreported as given again — which pointed at the defect
  above, and once it was fixed can no longer act, a key whose value follows never touching the edge. Both stay, as
  defenses.
- **Tracking.** Filed [#229] — a catalog whose own load rebuilds the index is tested by neither search for it,
  reproduced twice on `c07e489` and on the branch; placed with [#132] in batch 22 — and [#230], placed with [#174] in
  batch 16. Commented: [#49] (the false report paid; the keys past 64 KB lost, which its body does not name,
  reproduced twice on four files), [#192] (`ParserBenchmarks` holds the string case it left open), [#37] (the
  transient string a name token makes, 26 % of what parsing Word's page dictionary allocates), [#174] (a stream whose
  `/Length` the file cannot hold no longer silences what follows it; its own case stands).
- **Checked**:
  - the solution builds with no warning, and `dotnet format` finds nothing;
  - each of the batch's commits builds with no warning and passes alone;
  - 3,256 unit tests, 3 skipped, and 5,161 with the remote corpus;
  - the patch's 218 measurable lines of `src/` covered on the committed corpus alone, every branch taken; three lines
    of the validator the fuzzing reached by chance, which the parser's new reading of its mutated documents no longer
    reaches, are held by two tests; 99.5 % of `src/` in all, as before the batch;
  - the integration suite against qpdf in its container, with the remote documents this container holds, 3,266
    tests: 3,260 passed and 6 skipped where qpdf cannot walk a damaged document's pages, in 13 minutes;
  - the corpus read and validated whole, twice, against `c07e489`: only the two documents above change, each as its
    manifest now requires;
  - `ParserBenchmarks`, two medium runs of each alternated, before the batch and with it: a sound object allocates
    what it did, and parses 1 to 5 % slower, 5.7 → 6.0 µs for Word's page dictionary; a hostile one pays for its
    reports (*Current measurements*);
  - the site builds, 196 pages;
  - `Remote corpus` runs 28 and 29, on the branch, each at its first attempt: all 242 documents; run 29's 5,218
    acceptance tests with 3 skipped and 3,329 referee checks with 6 skipped, on the code the branch ends with;
  - commitlint, which runs on the pull request alone, found sixteen commit messages wider than 100 columns: they were
    rewrapped, the code of every commit unchanged, and the runs above stand for it.
- **Next**: batch 9, [#125] and [#126], a `startxref` near the largest long and a finding located past the end of the
  file, its questions put when it starts.

### 2026-10-03 — Weekly previews and versioning, outside any milestone ([#227])
- **The question.** Apply AdCodicem.ValueObjects' preview and versioning pattern (its ADR-0009 and ADR-0010), each
  part justified by a measurement on this repository, not copied.
- **Measured.** 48 previews in 15 days, one per push; 20 of the 47 after the first had the same package inputs as the
  one before, and their assemblies differ only in their version strings and module identifier (`preview.14` and
  `.15`, 154 bytes). `0.1.1-preview.57` while the commits make the next release `0.2.0`; the number was the run's. No
  attestation. The publishing job restored 71 packages, 610 MB, while holding `id-token: write`; `src/` restores one,
  2.7 MB. The stable path, never run, would have pushed to `main` with `GITHUB_TOKEN`, which the ruleset refuses, and
  with no preset the commit analyzer answers `null` on `feat!:`. On .NET 11 RC1, the unit suite rolled forward passes
  3,100 of 3,103 with the same three skipped, and a trimmed `net11.0` application of the packed package reads the 168
  committed documents exactly as their manifest says.
- **Settled with the maintainer**, each as recommended: `preview.yml` (`nuget`) and `release.yml` (`nuget-stable`,
  with a reviewer); weekly previews, and a push redeploying the site when nothing that ships changed; a breaking
  change releases a minor while the major is 0; a release App past the ruleset; scripts for several packages from
  now on; a compatibility island of a consumer application and the rolled-forward suite; `Build and unit tests`,
  `Integration tests`, `Conventional commits` and `workflows` as required checks; this issue to track it.
- **Done**: ADR 48 (one version, independent of .NET, `conventionalcommits`, the 0.x rule, Dependabot's lines, the
  island) and ADR 49 (weekly gated previews, publish/repair/none, the pack without credentials, attestation, the
  site), which supersedes ADR 30's preview track and amends ADR 31. Release tooling pinned in a root `package.json`.
  Pack of `src/` only, the set checked before every push, pushes in dependency order with symbols on their own, and
  attestation of every package and assembly. `docs.yml` called only, checking the commit it builds. actionlint and
  shellcheck on every pull request. Dependabot: a week of cooldown, auto-merge by allow-list, and its subjects let
  through commitlint (#196). `CLAUDE.md`: questions to the maintainer go through `AskUserQuestion`.
- **Checked**: `next-version.mjs` against semantic-release's core, in dry run on a local remote, over the 292 commits
  since `v0.1.0` and a made-up history of 14 — every one agrees; `preview-gate.sh` replayed over the 47 previews
  after the first against a fake flat container — 27 *publish*, 20 *none*, as the inputs changed —, and 34 scenarios
  over a synthetic repository of two packages (repair, recheck, report, a release, `Directory.Packages.props`, a
  lookup that fails); `push-packages.sh` against a fake feed answering 409, 500 and listing late — 11 scenarios,
  the trap it avoids among them: a `.nupkg` answered 409 under `--skip-duplicate` never sends its `.snupkg`; and
  `dotnet nuget push` tries a 5xx three times. Both sets now run on every pull request (`release scripts`, in
  `ci.yml`). actionlint and shellcheck on every commit (the first two keep the two style findings `main` already
  had, in code the third removes); CodeQL 2.27.1's `actions-code-scanning` suite, 0 results; build, format, the unit
  suite (3,103, 3 skipped), the integration suite skipped without Docker, the pack, the island and the site.
- **Reviewed.** An adversarial review of the diff by a fresh agent found no defect that would fail CI, publish a
  wrong version or leak a credential. Fixed from it: `docs.yml`'s jobs carry a status function of their own, since
  the implicit `success()` may look past the caller to the jobs `preview.yml` skips on purpose; `release.yml`'s
  uploads overwrite on a re-run; the pack steps compute their outputs before writing them; the gate restores and reads
  the projects of the solution filter rather than every project under `src/`; the threat model's entry for [#179];
  the local command of the island, which a package folder of its own keeps from testing a stale `0.1.0-alpha`; the
  harnesses, committed rather than run once; two stale comments.
- **Left to the maintainer**, before or right after the merge: the `preview.yml` Trusted Publishing policy (before),
  the `nuget-stable` environment and its reviewer, both environments limited to `main` ([#178]), the release App and
  its bypass ([#41]), the required checks ([#41], [#196]); then dispatch `preview.yml` once, check the first preview's
  attestation, and delete the old `release.yml` policy.

### 2026-10-03 — Batch 7: the generation a rebuilt or relocated entry records ([#118])
- **The question.** [#118]: the rebuild's scan read a header's generation to know it from the `obj` of an `endobj`,
  and recorded every object at generation 0, so a finding located `5 0` an object the file writes `5 1 obj`. Measured
  first, each run of our reader twice: 18 shapes against our reader and six referees (qpdf 11.9.1, libqpdf 12, pypdf,
  PyMuPDF, PDFBox 3.0.5, pdf.js), which all record the header's generation; and all 401 corpus documents, with 11
  headers of a non-zero generation in 3 of them, 2 rebuilt — groff, committed, and nureg, remote —: 6 entries move, and
  no finding, diagnostic or manifest expectation. The measurement found two more sites that make a generation up:
  a relocation recorded the generation of whichever reference asked first, and a catalog found by its type was put in
  the trailer as `N 0 R`. Seven questions followed.
- **Settled with the maintainer**, each as recommended:
  - the scan, the relocation and the recovered `/Root` record the header's generation, or the entry's; [#185]'s
    relocation bullet is paid here;
  - the live index gives the entry's: the row's for a chain entry, the header's once rebuilt or relocated; only a row
    whose header contradicts it at the offset it gives, followed by a rebuild set off elsewhere, names an object two
    ways, which `xref.generation-mismatch` reports;
  - the last definition of a number wins, under its own generation; which one a rebuild keeps stays [#189]'s;
  - an in-use generation of 65,535 is kept;
  - [#185] stays in batch 28; `object.redefined` unchanged, noted on [#189]; [#158]'s `MaxRepairObjects` test is
    written with [#183] in batch 11.
- **Done**, in [#226]:
  - `TryReadHeaderBackwards` returns the generation it reads; `ScanForObjects`, the relocation in `LoadRegularObject`
    (through `TryFindObjectHeader`) and `FindCatalog` record it; the `Index` and `ObjectGraph.IdOf` remarks say which
    generation an entry gives;
  - the validator's probe judges a relocated header with the generation the search read, and the object stream walk
    locates a finding through the chain's index, read again — [#128]'s rule for a lookup, not its fix.

  Docs: `lazy-reading.md` (which definition a rebuilt map keeps, and which rules name an object from the map, the row
  or the reference), `architecture.md`, and the threat model's rows on the rebuild's scan and on an object not where
  its entry says, its gap for [#118] removed.
- **Reviewed.** An adversarial review over five lenses — the reader, the validator, the tests, the documents, the
  neighboring issues — and a completeness critic gave 26 findings, each put to a refuter: 17 kept, some the same
  defect seen twice, and 9 refuted.
  - Kept and fixed, among them:
    - the probe read a relocated header again through its 64 bytes, so a longer one — zeros leading its generation —
      earned no `xref.generation-mismatch` while the reader recorded its generation;
    - the object stream walk located a stream through the index it took before loading it, which the load then
      relocated or rebuilt: the stream was named by its header's generation beside the cross-reference rules' row, and
      named otherwise once read before validating — a regression of this batch;
    - the first `lazy-reading.md` paragraph said every finding names an object as its header does, and that a rebuild
      keeps the last definition of each number; the `Index` remark dropped the decision's "at the offset it gives";
      `FindCatalog`'s comment named the header where the entry decides;
    - tests: the catalog found after a rebuild and after a relocation of its own load, the naming after a rebuild that
      follows a chain read, a row of 2 that contradicts its header, the object stream walk fresh and read first;
    - the comment on [#185] left its relocation reproduction unmentioned, now false: an addendum says so.
  - Left as is: the body of the docs commit `9790f09` calls both threat-model rows "Indexing rows", one being under
    *Resolving objects*; pushed history is not rewritten.
  - Refuted: the `/Root` recovered from the chain's index and a later rebuild, a reference of another generation
    naming the object two ways (#185's), the corpus test's qpdf claim, the deferral of #158's test, and five others.
- **Tests**: 31 new, 3,072 to 3,103; 5,008 with the remote corpus. Each case the fix exists for fails on the code
  before it, and eight mutants each fail a test: the scan recording 0 again; relocation keeping a non-zero row's
  generation; `IdOf` reading the chain's index first; the object stream walk naming a stream at generation 0, or
  locating it through the index it took; `FindCatalog` reading the generation from the chain's index, or before the
  load; the probe reading a relocated header again through its 64 bytes.
- **Tracking.** No new debt: what the batch found outside it is covered. Commented: [#189] (a number reused under a new
  generation, `object.redefined`; a last header whose value reads as null winning over a readable one 48 bytes
  before it, reproduced twice; `lazy-reading.md` added to its documents), [#185] (its groff sentence, its relocation
  bullet and its relocation reproduction, now paid or false), [#183] (#158's test placed with it), [#128] (relocations
  move the walk's index too; `Location` reads the chain's again, its arms without an entry now defensive, its test's
  positions kept), [#175] (the probe's relocated header now read whole; the exact offset still its own).
- **Checked**:
  - the solution builds with no warning, and `dotnet format` finds nothing;
  - each of the batch's commits builds with no warning and passes alone;
  - 3,103 unit tests, 3 skipped, and 5,008 with the remote corpus;
  - the patch's 21 measurable lines of `src/` covered on the committed corpus alone, 20 with every branch taken: the
    one left is `ObjectStreamDependencies.Location`'s arm without a Regular entry, defensive since the batch;
  - the integration suite against qpdf in its container, with the remote documents this container holds, 3,266
    tests: 3,260 passed and 6 skipped where qpdf cannot walk a damaged document's pages, in 13 minutes;
  - `Remote corpus` run 26, on the branch at `79de092`: its first attempt could not fetch ECan's scan, web.archive.org
    refusing the connection ([#204]), and passed every test over the 241 others, 5,059 acceptance tests with 3 skipped
    and 3,321 referee checks with 6 skipped; the one rerun fetched all 242 and passed, 5,065 acceptance tests with 3
    skipped and 3,329 referee checks with 6 skipped.
- **Next**: batch 8, [#119] and [#172], what the parser reads in silence, its questions put when it starts.

### 2026-10-03 — Batch 6: what the chain refers to while it is read ([#182], [#215], [#216])
- **The question.** [#182], from the threat model: a cross-reference stream's `/W`, `/Size`, `/Index`, `/Filter`,
  `/DecodeParms` and `/Length` were resolved while the chain was read, and a rebuild, a corrected entry or a cached
  null that load caused was then served, and judged, as the index the file wrote. With it [#215], integral reals read
  in silence where an integer is required, and [#216], `/W`'s bound of 8, both filed by batch 5. Measured first: 60
  shapes through our reader and five referees (qpdf 11.9.1, libqpdf 12, pypdf, MuPDF, PDFBox), and all 401 corpus
  documents — 233 cross-reference streams in 95, none with an indirect value but `/Root`, `/Info` and `/Encrypt`, no
  integral real where the reader reads an integer, no field wider than 4 bytes: no option moves a manifest entry.
  Eleven questions followed.
- **Settled with the maintainer**, each as recommended:
  - while the chain is read, nothing loads: a lookup with no side effect reads an object where a section already read
    places it — its type-1 row, the exact offset, the same number and generation, within `MaxObjectLength`, not a
    stream —, and nothing is corrected, rebuilt, cached, recorded or reported;
  - a value the rows need written as a reference is read through it and reported, a Warning; one it cannot read makes
    the section malformed, the fault naming the reference; `/Prev` and `/XRefStm` alike;
  - a `/Length` it cannot read: the data up to its `endstream`, the length read and checked once the chain is, the
    rows not read again;
  - `file.trailer-value-wrong`, a new Warning, judges each section's trailer as written; `/Size` stays
    `file.size-wrong`'s;
  - [#215]: read as written and reported; `AsInteger` unchanged; `xref.object-stream-value-wrong`, a new Warning, for
    an object stream's `/N` and `/First`, its `/Length` left to `object.value-type-wrong`; the `/DecodeParms` of
    ordinary streams a debt;
  - [#216]: fields wider than 8 bytes read when their excess is zero; a nonzero excess refuses the row alone, and in
    the type field gives a reserved type;
  - a lookup a guard cuts is unresolved, reports nothing and marks nothing;
  - the empty chain the measurement found (E1) a debt, placed in batch 22 with [#132]; [#208] stays in batch 19.
- **Done**, in [#225]:
  - `PdfFileReader.ReadWithoutLoading`, which `GetObject` answers through while the chain is read, with a memo scoped
    to the chain; `UnreadValue`, which follows the values the rows are read with, each through as many references as
    `PdfReference.Resolve` follows; `/Prev` and `/XRefStm` through the same lookup;
  - a deferred `/Length` (`ObjectPresence.Deferred`, `StreamLengthForm.Deferred`): the data up to its `endstream`, a
    carriage return ending it kept, then `CheckDeferredLengths` once the chain is read, behind the copy of the index;
  - relocation candidates that are no section dropped whole, their guard included; at a named offset the guard reached;
  - `/W` widths as `long`, narrowed against the data; rows read field by field past 8 bytes;
  - the two rules, `file.size-wrong`'s message, the profile and the manifest's schema.
    `xref.object-stream-value-wrong` takes an object stream's `/Length` as well: the Arlington walk that
    `object.value-type-wrong` reports from never reaches an object stream, so the decision would have left it
    unreported — one finding, under one rule, either way;
  - the commit that adds the rules is a `feat:`.

  Docs: `validation-rules.md` (the two rules, `xref.section-malformed`, `file.size-wrong`), `diagnostics.md`
  (`xref.section-unreadable`, `stream.length-invalid`), `lazy-reading.md`, `concepts/validation.md`, and the threat
  model's *Indexing* rows, its gaps replaced by the debts below.
- **Reviewed.** An adversarial review over five lenses — the reader, the rows, the rules, hostile inputs, the tests —
  and a completeness critic gave 33 findings, each put to a refuter: 26 kept and fixed, 7 refuted.
  - Kept and fixed, among them:
    - the chain refused a section over a `/DecodeParms` entry its rows never read, a regression on valid 1.x files,
      and followed a reference three levels deep, past which a value its rows needed read as null;
    - the candidate's keep test ran before the guard at a named offset too, silencing `limit.trailer` and
      `ThrowOnLimit`;
    - the deferred length reparsed its stream, reporting its syntax faults twice, was checked for streams the chain
      never read as sections, reported a length its `endstream` confirms, with a false message when it gave fewer
      bytes, and lost a last byte that is a carriage return;
    - `file.trailer-value-wrong` gave two findings for one value, said "the reader read it" of values it could not
      read, and cited Table 17 where 7.5.8.2 applies; `RowFault` said "more than 64 bits" of 2⁶⁴ − 1;
    - the documents: a fractional `/Length` of an object stream, a reserved type "ignored as ISO asks" ([#221]), two
      stale comments; tests: a renamed test that passed on `main`, the lookup's bounds held by no test, the
      validator's arrays, a real `/XRefStm` and an object stream's real `/Length`.
  - Refuted: one already fixed in the tree; `ReadField`'s inlining, which tiered compilation restores; three not this
    change's — [#188]'s duplicate records among them —; two within the decisions.
  - Found after it: a reference bound of 3 left every test green (a boundary test now holds 31), and patch coverage
    left a deferred length that gives no length, and a reference leading to a real, unreached.
- **Tests**: 129 new, 2,943 to 3,072; 4,977 with the remote corpus. Eight mutants of the defenses the review named —
  the keep test, the window's growth, the trailer guard, the deferred check's confirmation, the carriage return kept,
  the filters followed, the reference bound, the widths' clamp — each fail a test.
- **Tracking.** Filed under M02, each reproduced twice: [#218] (a chain read whole that gives no row, rebuilt with no
  finding, batch 22), [#219] (an ordinary stream's `/DecodeParms` never typed, batch 12), [#220] (a self-indexed
  `/Length` past 64 KB, and rows that spell `endstream`, batch 16), [#221] (a reserved row type skipped where ISO reads
  null, batch 19), [#222] (a classic section with no subsection, batch 19), [#223] (five manifest entries describing a
  structure their files lack, batch 32) and [#224] (a guard-cut `/Length` said to hold null, batch 16). Commented:
  [#132] (`file.root-invalid`'s false message on one free row), [#169] (the diagnostics' capacity making a trailer
  malformed), [#61] (an indirect `/ID` beside `/Encrypt`), [#210] ("1 row" paid), [#188] (the relocation window near
  the file's start, which made the test files' spacer 1,200 bytes), [#158] (`/W`'s hostile test re-aimed, its mutant
  shown), [#220] and [#224] (their shapes rechecked after the review).
- **Checked**:
  - the solution builds with no warning, and `dotnet format` finds nothing;
  - 3,072 unit tests, 3 skipped, and 4,977 with the remote corpus;
  - the patch's 338 measurable lines of `src/` covered on the committed corpus alone, 336 with every branch taken: the
    two left are `EndStreamAfter`'s defensive arms, a position outside the file and no `endstream` within reach, which
    the chain's own search rules out; one `??=` no input reached the other side of became an assignment;
  - each of the batch's commits builds with no warning and passes alone;
  - the integration suite against qpdf in its container, with the remote documents this container holds, 3,266
    tests: 3,260 passed and 6 skipped where qpdf cannot walk a damaged document's pages, in 12 minutes;
  - the site builds, on the pull request;
  - `Remote corpus` run 25, on the branch at `2de24cd`, fetched all 242 documents at its first attempt and passed:
    5,027 acceptance tests with 3 skipped and 3,329 referee checks with 6 skipped.
- **Next**: batch 7, [#118], a rebuilt index's generations, its questions put when it starts.

### 2026-10-02 — Batch 5: the numbers read from the file ([#157], [#186])
- **The question.** [#157], from the threat model: a 19-digit integer wrapped to a value the file chose —
  `92233720368547758082 0 R` led to object 2, and the header written with that number was taken for object 2's, with
  nothing reported —, a 309-digit real read as an infinity, and the index's and object streams' numbers were narrowed
  to `int` before their checks. [#186], from M01's review: six in ten reals below 1 read an ulp or more off, which M03's
  slice 1, whose rewrite must not move a value it read, cannot accept. Measured first: two parse routes prototyped and
  held against the framework over three million cases and the corpus's 24 million reals, their cost, 135 hostile
  shapes through our reader and four referees, and every narrowing site. Twelve questions followed.
- **Settled with the maintainer**, each as recommended:
  - the exact fast path, `mantissa / 10^k` when both are exact doubles, the framework's parse for the rest;
  - an integer past a `long` reads as the nearest real, with no report of its own; `-9223372036854775808` stays the
    integer `long.MinValue`;
  - a number beyond what a double holds reads as null, reported as `syntax.number-out-of-range`, a Warning; an
    underflow is not reported;
  - object numbers 1 to 2,147,483,647 and generations 0 to 65,535 are internal constants (ADR 34); `/W`'s bound of
    8 becomes a debt;
  - a fault in a cross-reference stream's numbering makes the section malformed; one in a row refuses that row and
    reads on, the reader's report left to [#210]; free rows are exempt;
  - an object stream's header ends at a pair no member can have;
  - `AsInteger`'s bound corrected, integral reals still admitted;
  - the properties and `/W`'s hostile test paid from [#158]; the benchmark committed before the change;
  - [#212] commented, `DescribeValue` corrected here, the integral reals filed.
- **Done**, in [#217]:
  - `PdfNumberParser` checks the shape, reads a `long` exactly, divides when both operands are exact, and hands the
    rest to `double.TryParse` on the span; an infinity is read as null by `PdfObjectParser`, the number quoted only in
    a report the diagnostics keep;
  - `PdfObjectId.MaxNumber` and `MaxGeneration`, with their reasons, wherever a header, a reference, a row or a
    member is read; a classic subsection past the last number malformed; a cross-reference stream's numbering
    checked, `/Index` resolved once, before any row is read; rows read as unsigned fields and refused alone, no older
    section's row then standing for the object; an object stream's header ended at a pair no member can have, in the
    reader, the rebuild and the validator; headers judged by value in the rebuild's backward scan and the probe;
  - `AsInteger` below 2⁶³; `DescribeValue` with a real expanded and a null;
  - the commit that adds the code is a `feat:`, as every earlier code's was.

  Docs: M03, M07 and M08; the threat model's rows, the gap removed; `diagnostics.md`, `validation-rules.md`,
  `reader-limits.md` (three bounds that cannot be lifted) and `lazy-reading.md`; the XML documentation of the code, of
  `PdfToken`, `PdfReal`, `PdfObjectId`, `AsInteger` and `xref.section-malformed`.
- **Measured.**
  - `LexerBenchmarks`, the parser before [#186] then this one, one after the other, 0 B allocated in every case: the
    Word invoice page's 2,888 numbers alone 33.9 to 45.2 µs, the page lexed 141.3 to 153.8 µs (×1.09); a million
    synthetic numbers 8.26 to 10.00 ms, lexed in 26.3 ms either way.
  - A first version, one loop over a number's whole part and fraction, cost ×1.61 on the page's numbers in one process
    against ×1.33 for the prototype; two loops, as the prototype had, ×1.21. The `perf:` commit says so.
  - The corpus: no manifest entry moves. 4,580 object reals in 124 documents, 62 of them remote, now read 1 to 3 ulp
    from where they did, none across a threshold a rule judges; five documents give the free list's head 65,536, read
    as before.
- **Reviewed.** An adversarial review over four lenses — the parser, the narrowing sites, the reports and documents,
  the tests — and a completeness critic gave 34 findings, each put to a refuter: 31 kept and fixed, 3 refuted.
  - Kept and fixed, among them:
    - a refused row let the row of an older section stand for its object, which served a superseded revision in
      silence — a regression of this batch, where the narrowed row had served the current one;
    - `/Index` checked on one resolution and read on a second, which a rebuild between them could change;
    - the backward scan refusing a generation of more than ten digits, zeros leading, which the parser reads;
    - an `/Index` that is not an array read as absent;
    - a refused row's fault naming itself for a section unreadable for another reason;
    - every number beyond a real quoted, about 1.1 KB each, even once the diagnostics are full: 24 to 31 MB for twenty
      thousand, now 4 to 8 MB;
    - the new code under `fix:`;
    - messages: an exponent in a real, a reference said "null", "1 rows", the sign of a number beyond a real;
    - the documents: lazy reading's list of rebuilds, a test the threat model named under an old name, M08's
      inline images split by a new sentence;
    - tests: the accepted side of every bound, the rows read after a refused one, the object then rebuilt, integral
      reals admitted, a trailer holding a number beyond a real.
  - Refuted: the benchmark's figures missing before they could be measured; a lower bound in `reader-limits.md`,
    rewritten anyway; a trailer test said missing, added anyway.
  - One more, found while measuring the coverage: `An_index_a_rebuild_left_empty_while_the_chain_was_read_is_not_rebuilt_again`
    still passed, but no longer rebuilt anything while the chain was read, `/Index` being checked first; it now reaches
    the second rebuild it is named for again.
- **Tests**: 142 new, 2,801 to 2,943; 4,848 with the remote corpus. Every defense was mutated and each mutant fails a
  test — 31 of the reader's checks, 12 off-by-one ones the review named, the parser's bounds, the two review fixes —,
  but one the parser's `fits &&` makes equivalent.
- **Tracking.** Filed under M02, each reproduced twice, both paid with [#182] in batch 6: [#215] (integral reals read
  in silence where an integer is required) and [#216] (`/W`'s bound of 8, unclassified under ADR 34). [#137] waits on
  them. Commented: [#158] (the number properties and `/W`'s hostile test paid), [#202] (its negative start paid),
  [#210] (its `/Index` count paid, the refused rows added), [#212] (`PdfReal.ToString` quoted more often now). The
  false `file.root-invalid` the measurement saw on three shapes no longer reproduces: those sections are now malformed.
- **Checked**:
  - the solution builds with no warning, and `dotnet format` finds nothing;
  - each of the batch's commits builds and passes alone;
  - 2,943 unit tests, 3 skipped, and 4,848 with the remote corpus;
  - the patch's 183 measurable lines of `src/` covered on the committed corpus alone, 181 with every branch taken.
    Codecov found four lines partial, which a local script blind to branches had counted whole: a dead fallback was
    dropped and two tests were added; two stay partial, as `CLAUDE.md` allows — the `HasValue` checks the compiler
    adds to arithmetic on a `long?` known to hold a value, and a defensive arm of `Expanded` no runtime takes today;
  - the integration suite against qpdf in its container, with the remote corpus, 3,266 tests: 3,260 passed and 6
    skipped where qpdf cannot walk a damaged document's pages, in 13 minutes; CI green on the pull request;
  - the site builds, 196 pages;
  - `Remote corpus` run 24, on the branch at `3ab20e7`: its first attempt could not fetch ECan's scan, web.archive.org's
    TLS handshake timing out, as the nightly run 23 had found it refusing connections that morning ([#204]), and
    passed every test over the 241 others; the one rerun fetched all 242 and passed, 4,903 acceptance tests with 3
    skipped and 3,329 referee checks with 6 skipped.
- **Next**: batch 6, [#182], a cross-reference stream's dictionary read as written, with [#215] and [#216].

### 2026-10-02 — Batch 4: the budgets the index changes are measured against ([#193])
- **The question.** [#193], from M01's review: M01 asks for a file of several hundred thousand objects opened within a
  stated budget, and for every corpus document read within its time budget. No test held either, no budget was
  stated, and no CI job set a timeout. Measured first: opening indexes of 100,000 to 1,000,000 objects in ten shapes,
  each operation on the 401 corpus documents this container holds on a quiet machine, and thirty runs of each CI job.
  Eleven questions followed.
- **Settled with the maintainer**, each as recommended:
  - 300,000 objects, through a classic table and a cross-reference stream, written by `TestPdfBuilder` with one page;
  - the stream's rows stored by Flate, not compressed: compressed, they allocate 0.65 % more under Microsoft's
    zlib-ng, which CI runs, than under Ubuntu's zlib 1.3, which sessions run;
  - `Open` from a `byte[]`, after a first opening, measured per thread;
  - the measured figure plus 5 %;
  - in `DocumentReaderTests`, each measured `Open` also held to 10 s;
  - 20 s for each operation on a corpus document;
  - 15 minutes on `ci.yml`'s jobs, `release.yml`'s two test jobs and the documentation's deployment;
  - a CTRF report of the remote corpus's acceptance tests, kept as an artifact;
  - the journal's budget measured after a first reading, and M23's account of it corrected;
  - the documentation's bytes per object rewritten as measured;
  - [#212] placed in batch 10, with [#190].
- **Done**, in [#214]:
  - the large-index theory, with `TestPdfBuilder.BuildWithXRefStream` taking a Flate level;
  - one helper in `CorpusReadingTests` holding each operation to 20 s: opening, reading every object, walking the
    pages, validating a damaged document, refusing an encrypted one;
  - the file read outside every stopwatch;
  - the timeouts and the report in four workflows.
  
  Docs: M01.md's *Tests required*, acceptance conditions and checklist; M23.md; `lazy-reading.md`'s per-object
  figures and *Measured* table; `architecture.md`; `PdfXRefTable`'s remarks; the threat model's budgets, with the
  gap removed; a wanted document in `corpus-contributions.md`.
- **Measured.**
  - Opening allocates a step function of the index map's capacity. The map holds 52 bytes a slot and grows through
    156,437, 324,449 and 672,827 slots, so a count costs 100.4 bytes an object at a growth step and 208.2 just past it.
  - The figure is the same to the byte in a console, alone in xUnit, under `--coverage` and in the parallel suite.
    An open document keeps 52 to 108 bytes an object.
  - At 300,000 objects: 32,571,368 bytes through the table and 45,174,464 through the stored stream. A copy of the
    index would add 52 %, and an entry 8 bytes wider 15 %.
  - The journal measures 3,347,320 bytes after a first reading.
  - The slowest full read is us-topo's, 1.6 to 1.9 s alone and 2.8 s in the suite. Cairo's validation takes 1.4 s.
    No committed document takes over 96 ms for any operation.
  - CI's longest runs: build 2:43, integration 2:14, documentation 1:43, preview 2:48, deployment 1:59.
- **Reviewed.** An adversarial review over four lenses — the large-index test, the corpus's timing, the workflows,
  the documentation — put each finding to a refuter.
  - Kept and fixed:
    - a commit subject of 106 characters, which commitlint's 100 would have refused; the branch's history was
      rewritten before its pull request opened;
    - the journal's first assertion inside its measured span: 3,561,744 bytes alone and 3,348,152 in its class, now
      3,347,320 either way;
    - "18 to 42 bytes" for a stream's rows, when unencoded rows add 7;
    - "whatever the objects weigh", untrue when the catalog sits in a large object stream;
    - MB in two units;
    - "mid-way" for 85 % of the way to the next growth step;
    - the index copy's size, the journal row's date, and the last preview, 53.
  - Refuted:
    - the corpus operations the settled set leaves untimed;
    - a CTRF upload that an artifact-service failure could turn red before the referee checks.
- **Tests**: 2 new, 2,799 to 2,801; the corpus theories now time five operations. Each budget fails when its defense
  goes: 24 more bytes an index entry fails both rows, and a one-tick budget fails every timed operation, each one
  reached — 510 openings, 510 reads, 380 page walks, 129 validations, 20 refusals.
- **Tracking.** Commented, with the measurements:
  - [#180]: a valid table of a million objects opened from a path leaves 55.9 MB pooled;
  - [#181]: cairo's validation is mostly the object cache letting objects go;
  - [#164]: the map grows with no capacity hint;
  - [#62]: the validation budget is measured cold;
  - [#37]: a name table per document would move both budgets.
- **Checked**:
  - the solution builds with no warning, and `dotnet format` finds nothing;
  - 2,801 unit tests, 3 skipped, and 4,706 with the remote corpus;
  - the CTRF report is written where the workflow uploads it;
  - the site builds, 196 pages;
  - the integration suite is left to CI: nothing here changes what the reader returns;
  - `Remote corpus` run 22, on the branch at `e6e41ef`: its first attempt fetched 241 of the 242 remote documents —
    web.archive.org refused the IRCC form — and passed every test over them; the one rerun fetched all 242 and passed,
    4,763 acceptance tests with 3 skipped and 3,329 referee checks with 6 skipped;
  - its CTRF reports give the first durations measured on GitHub's runners: over the two attempts, the slowest whole
    test of a corpus document took 1.2 to 2.8 s, the slowest of a damaged one 1.8 to 3.3 s, both cairo's, and the
    large index's slower row 1.8 to 2.2 s — each operation at least six times under its 20 s.
  
  Merged with [#214] on 2026-10-02; CI run 428 green on `main` at `ed1e9ed`.
- **Next**: batch 5, [#157] and [#186], numbers read from the file, its questions put when it starts.

### 2026-10-01 — Batch 3: what a message quotes of the file ([#159])
- **The question.** [#159], from the threat model: messages quoted the file's names and keywords whole and unescaped,
  and numbers under the host's culture. A name of megabytes was copied into every report, and kept for the document's
  life in a stream's length fault. A line feed, an escape sequence or a C1 control reached the host's logs as itself.
  The corpus, the reader, the validator and hostile shapes were measured first, and ten questions followed.
- **Settled with the maintainer**, each as recommended:
  - a quote is written as a PDF writer writes a name (ISO 32000-1, 7.3.5), keywords by the same rule;
  - it is cut past 127 bytes, an internal constant, with ADR 34 amended to say so;
  - the cut note gives the whole's length;
  - a stream's length fault keeps what `/Length` held, not words about it;
  - decoding stops at the first unknown filter;
  - strings are never quoted;
  - every number of `src/` is formatted with the invariant culture, held by a corpus test under a Persian culture;
  - one helper in `AdCodicem.Pdf.IO`, which the validator shares;
  - a key path keeps its first and last four steps;
  - the public `ToString`s are left to a debt ([#212]).
- **Done**, in [#213]:
  - `FileQuote` writes through a buffer on the stack: a 64 MB name costs its 127-byte quote and no copy;
  - every reader message and validator finding that quoted a name now goes through it, and `RuleText.Name` is gone;
  - `StreamLengthFault` keeps `Value`, and its `Kind` is a fixed literal;
  - `filter.unsupported` stops the chain, "decoding stopped there";
  - `PdfName.IsBytes`, known when a name is interned, spares a cut quote reading a file's name whole to count it.
  
  Docs: the diagnostics reference's *How a message quotes the file*, the validation reference, *Reader limits*, the
  threat model and ADR 34's amendment of 2026-10-01.
- **Measured.** Each hostile shape is held to an allocation budget:
  - four streams taking a 4M-character name for their `/Length`: under 2 MB;
  - fifty sharing a 1M-character name: under 4 MB;
  - a filter of a million characters decoded a hundred times: under 1 MB;
  - a path through ten keys of a megabyte: 126 MB copying the keys, under 4 MB now.
  
  No message of the 401 documents changes under the invariant culture, and none earned `filter.unsupported`.
- **Reviewed.** An adversarial review over five lenses (the quote, its sites, the paths, the filters, the culture
  and the documentation), each finding put to a refuter.
  - Kept and fixed: a caller's name with characters past U+00FF was cut and counted in characters, a surrogate pair
    astride the cut written as U+FFFD, and its characters up to U+00FF counted as two bytes in the whole; then
    `FileQuote.Keyword`'s summary, and *Reader limits*, which stated ADR 34 unamended.
  - Refuted as this batch's: a `/Filter` element that is not a name, skipped in silence, which predates it
    ([#162], [#209]; commented).
- **Tests**: 246 new, 2,553 to 2,799, 168 of them the corpus theory under fa-IR. Each defense — the escapes, the cut,
  the byte count, the path cap, each call site, the stop at an unknown filter — fails a test when removed. The batch
  rewrote the loop over a chain's filters, so it pays [#158]'s intermediate step cut at the bound.
- **Tracking.** Filed under M02, reproduced twice: [#212] (the public `ToString`s of `PdfName`, `PdfDictionary`,
  `PdfStream` and `PdfString` raw and unbounded), which [#137] waits on. Commented, each reproduced twice: [#161] (a
  stream in an object-stream member reports its length fault at every parse), [#175] (`file.root-invalid` judges a
  catalog a guard cut, "/Type /Catalo"), [#162] (the non-name `/Filter` element now differs from the stop).
- **Checked**:
  - the solution builds with no warning, and `dotnet format` finds nothing;
  - 2,799 unit tests, 3 skipped, and 4,704 with the remote corpus;
  - the integration suite against qpdf, with the remote corpus, at `1b14d05`: 3,266 tests, 3,260 passed and 6
    skipped, as on `main`;
  - on the committed corpus alone, 149 of the patch's 151 measurable lines covered, the two left partial on a branch
    the compiler adds that no input takes (`FileQuote` 138, `PdfObjectParser` 647); the project at 5,145 of 5,165,
    the same 18 lines left besides;
  - the site builds, 196 pages;
  - `Remote corpus` run 21, on the branch at `1475ef8`, fetched all 242 remote documents at its first attempt and
    passed every test over them: 4,761 acceptance tests with 3 skipped, and 3,329 referee checks with 6 skipped.
  
  Merged with [#213] on 2026-10-02; CI run 420 green on `main` at `cf39b4c`.
- **Next**: batch 4, [#193], the memory and time budgets the index changes are measured against, its questions put
  when it starts.

### 2026-10-01 — Batch 2: the reader's codes and positions ([#187])
- **The question.** [#187], from M01's review: five situations raised under codes whose meaning does not cover them, a
  documented code nothing raised, and positions that are no offset in the file. The corpus was measured first, over
  the 401 documents this container holds, and eight questions followed.
- **Settled with the maintainer**, each as recommended:
  - a file with no `%PDF-` in its first 4,096 bytes is the new `header.missing`, a Repair at its first byte (-1 for an
    empty source), no longer `xref.rebuilt`;
  - a section `/Prev` or `/XRefStm` names outside the file is one `xref.section-missing`; a `startxref` outside it is
    the rebuild alone; `xref.entry-out-of-range` is an object's entry only, with no position;
  - an offset outside the file is placed at what named it, the offset in the message; `xref.chain-cycle` adds the
    header's offset;
  - a section that is there and cannot be read is the new `xref.section-unreadable`, a Warning at the section, its
    fault in the message; relocation is left to [#188];
  - a family for object streams, `object-stream.member-moved` (Repair) and `object-stream.unreadable` (Warning), placed
    where the stream's data starts, as is a fault met inside a member, the stream, the object and the byte of decoded
    data in the message;
  - each reported once: a misplaced member per stream and number, a stream's own fault per stream;
  - `object.redefined` raised once per rebuild, as Information with no position.
- **Done**, in [#211]. The parser has a mode for an object stream member, whose reports go where the stream's data
  starts. `/N` and `/First` are read as `long` and checked before any narrowing, and an explicit `/N 0` is an empty
  stream. A section a guard cut, and what the parser met where a guard cut a stream's data, are the guard's to report
  (ADR 34). The rebuild counts what it redefines in a fixed-size record — a `long` and the first ten numbers —, and its
  scan reads each header once: a window after the first starts where the one before could no longer hold a whole
  `obj`. The codes' XML, the reference's table and a section on object streams with real lines, `PdfDiagnostic.Position`,
  `lazy-reading.md`, `architecture.md`, the threat model's rows and the schema's enum follow.
- **Measured.** `header.missing` on iPRES's four T01 files, which keep `xref.rebuilt` where they rebuild;
  `object-stream.member-moved` on cairo, 6 reports where `xref.offset-adjusted` gave 23; `object-stream.unreadable` on
  pdfbox3947 and pdfbox3949; `object.redefined` on nine documents, groff-distiller405 the committed one — exactly the
  ones the measurement named. No corpus document has a section that is there and cannot be read past the first.
- **Reviewed.** An adversarial review over four lenses — correctness, the public contract, the tests, the invariants —
  gave 31 findings, each put to a refuter: 26 kept, 5 refuted. Kept and fixed: a cross-reference stream whose
  dictionary a guard cut, blamed on the file; a redefinition in the scan's overlap counted three times; the numbers
  listed under the current culture, and counted in an `int` a hostile rebuild can wrap; a parser fault at a guard's
  cut reported as the file's; a located message built for every fault of a member past the thousand kept, about
  280 MB for a million; `xref.chain-cycle` saying "already read" of an offset outside the file; eight documentation
  sentences; and eight missing tests. Refuted, and filed or commented where they stand as defects: a table whose last
  row is no row, read in silence ([#210]); member numbers wrapped by the cast ([#157]'s); a header straddling a
  window's start (fixed with the overlap, what is left commented on [#171]).
- **Tests**: 49 new, 2,504 to 2,553. Each test of a guard, a once-only report, the scan's overlap, the culture and the
  allocation bound fails when the defense it names is removed.
- **Tracking.** Filed under M02, each reproduced twice: [#207] (a table the end of the file cuts before its trailer,
  read in silence, and an object only older sections index read as null), [#208] (the chain stops at an unreadable
  cross-reference stream whose `/Prev` is known), [#209] (`filter.unsupported` with no position), [#210] (a section read
  only in part, reported nowhere). [#137] waits on them. Commented: [#125] (`limit.xref-section-count` is the one
  reader report left outside the file), [#157] (`/N` and `/First` are now checked as `long`; member numbers are not),
  [#171] (the straddling header the overlap fix stops). M02's checklist ticks M01's review, merged with [#198].
- **Checked**: the solution builds with no warning, and `dotnet format` finds nothing; 2,553 unit tests, 3 skipped,
  4,225 with the remote corpus; the integration suite against qpdf, with the remote corpus, on the branch at `86ad974`
  and again at `a6c90c8`, after the review's fixes, 3,266 tests each time, 3,260 passed and 6 skipped where qpdf cannot
  walk a damaged document's pages; the patch's 186 measurable lines of `src/` covered, every branch taken, on the
  committed corpus alone, and the project at 5,036 of 5,054, the same 18 lines left; the site builds, 196 pages.
  `Remote corpus` run 20, on the branch at `a6c90c8`: its first attempt fetched 241 of the 242 remote documents —
  web.archive.org refused the CFIA form — and passed every test over them; the one rerun fetched all 242 and passed,
  4,273 acceptance tests with 3 skipped and 3,329 referee checks with 6 skipped. Merged with [#211].
- **Next**: batch 3, [#159], what a message quotes of the file, its questions put when it starts.

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
- **Left**: the journal had passed a dozen entries; it was summarized after this pull request merged, the entries of
  2026-09-26 to 2026-09-30 folded into *Earlier, in brief*, which found [#50]'s closing recorded nowhere and M23 still
  planning it ([#205], under M23). Asked by the maintainer how hard the remote job leans on web.archive.org — 7 of the 242 remote documents, about
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
- **2026-09-26 and 27 — the roadmap revised from a feature survey.** Six areas surveyed, each challenged by a second
  pass: 307 features kept (`docs/research/2026-09-feature-survey.md`). The roadmap now has 32 milestones, numbered in
  the order they are worked and written with two digits (M00 to M31), the old numbers mapped in `docs/roadmap.md`; the
  case file comes before the HTML engine (M06 to M11), and new milestones run from revisions (M04) to DOCX to HTML
  (M31). ADRs 37 to 44 are new, and ten early ADRs regained a lost Context or Decision. Each milestone file got
  slices, acceptance on named documents and corpus gaps, gathered in `docs/corpus-contributions.md`. Their writers'
  173 open points are settled in `docs/research/2026-09-milestone-open-points.md`, sixteen by the maintainer's
  thirteen decisions, D1 to D13 — American spelling everywhere among them (ADR 20, amended). `features.json` feeds the
  README's table and the *Features and comparison* page; `Comparison benchmarks` was added, its first run left open.
- **2026-09-27 — T27, and M02's second slice.** T27 (#31): a reference to an object the file lacks reads as null,
  silently, the index rebuilt for it only when it may have lost entries; `/Prev` and `/XRefStm` are direct integers
  only, so a `/Prev` written as a reference is `xref.section-missing`. Slice 2 ([#58]): twenty `file.*` and `xref.*`
  rules, held both ways to qpdf on all 401 documents by `ValidationRefereeTests`; the reader records the file's own
  structure and finds a catalog among the indexed objects before any rebuild (`trailer.root-recovered`). ADR 45, the
  maintainer's: an error says the reader cannot vouch it reads the file as written, a warning that it was read as
  evidently meant; no `Critical` before M05 ([#109]); left silent: [#107], [#108] (M23), [#111]. Ten iPRES files
  supported, eight by the slice, two by T27; `Remote corpus` run 9 confirmed the remote findings. Covering its patch
  set the maintainer's first rule on what coverage work may remove; the rest of `src/` went to [#115].
- **2026-09-27 — tracking on GitHub, and a disclaimer.** Settled with the maintainer: `docs/roadmap.md` stays the
  reference, and the `Tracking` workflow mirrors its rows as GitHub milestones 1 (M00) to 32 (M31), never dated. A
  milestone's slices become `slice` issues when it starts, M02's #57 to [#62]; the debt table's open rows but T10
  became `debt` issues [#34] to [#56] (*Former identifiers* below), `maintainer` where a setting or an account closes
  them; the corpus's wanted rows `help wanted` issues [#63] to [#89]; the roadmap's open questions discussions #91 to
  #104. `stale.yml` spares `debt`, `slice`, `help wanted` and every issue under a milestone. With the Codecov App
  installed, [#40] (T14) was the first debt closed as an issue. At the maintainer's request the README, and so
  nuget.org, gained a `Disclaimer` — as is, no liability, no legal, tax or compliance advice, each document the user's
  to check —, the site a shorter one and a footer line; the roadmap is called an intention.
- **2026-09-28 — the project's coverage, and Codecov's rules.** [#115], merged with #116: `src/` from 93.6 % to 99.0 %
  as Codecov counts it on the committed corpus, with 67 new cases — the object model as a caller uses it, the PNG
  predictors held to libpng's filters, the reader, lexer, parser and filters on shapes no corpus file had. Settled
  with the maintainer: `codecov.yml` lets the project lose half a point at most and asks 95 % of each patch, `tests/`
  unmeasured; in `CLAUDE.md` and `CONTRIBUTING.md`, code goes only when no input can reach it, never because no corpus
  file does, a defensive branch stays, and a public member nothing calls is asked about, not removed. The internal
  `PdfLexer.Peek`, `PdfLexer.Length` and `PdfCharacters.IsDelimiter`, which nothing called, were removed.
- **2026-09-28 — four reader fixes on `main`, beside slice 3.** An object is read once whatever generation names it
  (`15af060`); the parser and the reader ask whether the stack has room before going deeper, and report
  `syntax.depth-exceeded` when it has not (`8b58870`); decoded object streams are kept to 32 MB in all, the oldest let
  go and decoded again when asked for (`9a99b95`, T33, [#50], closed with [#122]); a member its entry misplaces in an
  object stream is found by lookup, not by a search per object (`3d8d148`).
- **2026-09-28 — M02 slice 3: the object, page tree and Arlington rules.** Settled with the maintainer after
  measuring: one finding per fault, a hand-written rule owning what a generated one also sees; a reference to a
  missing object and the page tree's faults warnings, but a loop an error and a page left out information; pages
  counted as qpdf's walk counts them. [#122] fixed [#51] (`stream.self-reference`) and added thirteen rules:
  `xref.object-stream-circular`, three `object.*`, nine `page-tree.*`. [#124] added four `object.*` rules generated
  from the Arlington model at `c48b363` by `tools/AdCodicem.Pdf.Arlington`, into tables a test regenerates (ADR 44,
  amended), `NOTICE` packed and the license still MIT, overridden only where ISO 32000-1 is looser; a key newer than
  the declared version went to [#123]. No document waits for M02 any more; the thousand-page journal validates within
  its 6 MB budget (3.9 MB measured). Filed: [#117] to [#120], [#121] (M23).
- **2026-09-29 — slice 3 merged, and the rest of `src/` covered.** [#122] and [#124] merged, closing [#59], [#51] and
  [#50]. The maintainer set the coverage rule (`CLAUDE.md`, *Coverage*): 100 % of each patch is the aim and
  `codecov.yml`'s 95 % the floor; every member that is not private is covered, directly when no document reaches it,
  and neither a private member nor a branch the compiler adds is tested for its own sake. `src/` went from 98.9 % to
  99.6 %, a second agent trying to refute each line the first called unreachable: one claim fell; the 18 lines left
  are the rule's to leave. Fixed on the way: the Arlington walk threw on an object a rebuild had left no dictionary; a
  chain loop no section names, and a `/Root` written as a stream, were misworded; a timing test and an allocation test
  that wavered under coverage now measure a ratio and the least of several runs. Filed: [#125], [#126], [#128],
  [#129].
- **2026-09-29 — what remains of M02, and how work waits and is reviewed.** M02's review of that day, by the
  maintainer and the slice's session one question at a time, merged with [#133]: every issue under M02 is paid in it,
  [#123] moved to M20, the Arlington overrides confirmed (ADR 44, *Reviewed on 2026-09-29*); the order [#55] with
  [#120], [#56], then ten debts — [#117] to [#119], [#125], [#126], [#128], [#129], [#132] (filed then), [#107],
  [#111] —, then slices 4 to 6, a pull request per batch. The two [#47] documents skip only the laziness test
  (`unsupportedTests`); `QpdfRefereeTests` counts damaged trees' pages too. An issue's body declares what it waits on
  (`Blocked by:`, `Blocks:`), which `Tracking` mirrors; a `help wanted` issue goes under the first milestone it
  unblocks that has not started. ADR 46: every milestone ends with an adversarial review by a fresh session, its last
  slice; M02's is [#137], after the threat model ([#135]) and M01's review ([#136]).
- **2026-09-29 — a stream's length past the window ([#55]), said as the file wrote it ([#120]).** Merged with [#140]
  on 2026-09-30. Past the 8 KB window, the file is asked whether `endstream` follows the `/Length`; if not, the first
  `endstream` is taken, searched no further than the nearer of the next object the index as written places and the
  first object header the file's bytes hold — bounds no read changes, as the maintainer settled once reviews found
  lengths that depended on what was read first. No guard (ADR 34); no search starts once a document's searches have
  read four times the file (`EndStreamSearchPasses`); a header with a comment between its tokens is not taken, as in
  the rebuild's scan (maintainer, 2026-09-30). `stream.length-invalid` went from 81 to 96 reports, each stream
  reported once; 15 of the 16 wrong lengths read to their `endstream`, held to qpdf by `StreamLengthRefereeTests`;
  crafted files still order-dependent are [#138] (M23). SAMHSA's object 27, now read whole, became [#56]'s case.
- **2026-09-30 — a damaged Flate stream keeps what it decoded ([#56], T40).** Of the corpus's Flate streams, 272 are
  damaged, 223 of which lost what the read meeting the fault decoded. The maintainer settled: data that faults is read
  again, after the fault only — a plain zlib header's body as raw deflate, kept whole when only the checksum
  disagrees, else again to the 8 KB of input the fault lies in, then a byte at a time through them; a managed
  inflater, and smaller reads everywhere, turned down. A new warning, `filter.checksum-mismatch`, says whole data's
  checksum is wrong, in the `filter.*` family M21 reads; corrupt data stays `filter.failed`, what decoded before the
  fault kept; no new bound (ADR 34). Merged with [#147]: the 272 keep exactly what libz keeps, qpdf referees both
  codes over every report ([#134]), and the integration suite first ran in a session (`dockerd`, an `alpine:3.21`
  trusting the proxy's CA). Filed: [#141], [#144] (M02, after [#132]), [#142], [#143] (M05), [#145], [#146] (M23).
- **2026-09-30 — the user documentation along Diátaxis ([#148], ADR 47).** Each choice put to the maintainer:
  `docs/website/docs` in `tutorials/`, `guides/`, `reference/` and `concepts/`, one mode per page, the four *Concepts*
  pages split; a tutorial whose code is `samples/FirstSteps`, held to it by `FirstStepsTutorialTests`; four how-to
  guides; the rules table moved into the reference, versioned with each release, the API under `reference/api`, the
  old addresses redirected. `CLAUDE.md`, `CONTRIBUTING.md`, the milestone template, the definition of done and M02 to
  M31's *Documentation* sections follow; `codecov.yml` leaves `samples/**` out. Filed: `PdfDocument.Open(Stream)`
  copies a seekable stream into memory, unlike what it documents ([#149]).

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
| T33 | Fixed on 2026-09-28 by `9a99b95`, closed with [#122]: decoded object streams kept to 32 MB in all ([#50]) |
| T34 | Fixed on 2026-09-29 by [#122]: an object stream that needs itself is reported, its null not cached ([#51]) |
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
[#198]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/198
[#199]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/199
[#200]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/200
[#201]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/201
[#202]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/202
[#203]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/203
[#204]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/204
[#205]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/205
[#207]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/207
[#208]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/208
[#209]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/209
[#210]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/210
[#211]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/211
[#212]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/212
[#213]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/213
[#214]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/214
[#215]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/215
[#216]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/216
[#217]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/217
[#218]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/218
[#219]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/219
[#220]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/220
[#221]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/221
[#222]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/222
[#223]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/223
[#224]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/224
[#225]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/225
[#226]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/226
[#227]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/227
[#229]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/229
[#230]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/230
[#231]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/231
[#234]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/234
[#235]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/235
[#237]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/237
[#238]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/238
[#240]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/240
[#241]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/241
[#246]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/246
[#247]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/247
[#248]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/248
[#249]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/249
[#250]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/250
[#251]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/251
[#252]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/252
[#253]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/253
[#254]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/254
[#255]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/255
[#256]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/256
[#257]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/257
[#258]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/258
