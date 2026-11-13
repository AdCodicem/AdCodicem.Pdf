# Project status

A living file, updated **at the end of every session**. It describes the real state, not intentions.
Keep it short: the history is [`journal.md`](journal.md), and the detail of each pull request is in its body. What
is tracked item by item lives on GitHub since 2026-09-27: each milestone's slices and the known
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
  [#253], with [#258] on 2026-10-17; the fourteenth, [#129] and [#175], with [#256] and [#259], with [#265] on
  2026-10-21; the fifteenth, [#160] and [#161], object stream members, with [#200], [#202], [#255], [#257], [#273],
  [#274] and [#275], with [#278] on 2026-10-22; the sixteenth, [#174] and [#170], stream data, with [#220], [#224],
  [#230] and [#263], with [#297] on 2026-10-25; the seventeenth, [#155], tables keyed by numbers dense or seeded (ADR
  52), with [#308] on 2026-10-30; the eighteenth, [#167] and [#168], what a rebuild leaves a caller and what the
  validator's analyses cost, with [#201], [#277], [#307] and [#312], with [#318] on 2026-11-03; the nineteenth, [#188],
  [#197] and [#156], where the chain's sections lie and what it reports of one it reads in part, with [#207], [#208],
  [#210], [#221], [#222], [#241], [#261], [#264], [#281] and [#317], in [#344]; the twentieth, [#189], [#276] and [#320],
  which definition a rebuild keeps and what a place the search near a section drops leaves, in [#347]; the twenty-first,
  [#154] and [#171] with [#327], [#348] and [#350], in two pull requests by area, the rebuild in [#352], the searches
  near an offset and a row next, from the same branch. Then the
  other batches, slices 4 to 6 ([#60] to [#62]); then the milestone's adversarial review, slice 7 ([#137], ADR 46), once
  every other issue filed under M02 is closed. The debts come from the reader's own work, the threat model's first
  version ([#135], `docs/threat-model.md`), M01's review after the fact ([#136], `docs/reviews/M01.md`), the planning of
  step 4 ([#199] to [#202]), its second batch ([#207] to [#210]), its third ([#212]), its fifth ([#215], [#216]), its
  sixth ([#218] to [#224]), its eighth ([#229], [#230]), its ninth ([#237]), its tenth ([#241], [#247]), its twelfth
  ([#250]), slice 4's blockers ([#253]), its thirteenth ([#255] to [#257]), its fourteenth ([#259] to [#264]), its
  fifteenth ([#273] to [#277]), its sixteenth ([#279] to [#283], [#287] to [#290], [#292] to [#296]), its seventeenth
  ([#307]), its eighteenth ([#309], [#312] to [#315], [#317]), its nineteenth ([#320] to [#330], [#332], [#333],
  [#336], [#342]), its twentieth ([#348] to [#351]) and its twenty-first ([#353] to [#359]); [#123] moved to M20.
  The seventeenth filed [#305] and [#306] under M23, the eighteenth [#310] under
  M03 and [#311] under M23, the nineteenth [#331] under M03 and [#343] under M23.
- **User documentation**: organized along Diátaxis since 2026-09-30 ([ADR 47](adr/0047-the-user-documentation-follows-diataxis.md),
  [#148]), on `main`: a tutorial held to its sample by a test, four how-to guides, four reference
  pages — the validation rules among them, moved from the project documents — with the API reference under them, and
  four explanations. Outside any milestone.
- **Last milestone closed**: **M01 — Object model and tolerant reading**
- **Tests**: 8,497 unit on [#347]'s branch, on the committed corpus; with 235 of the 242 remote documents fetched — the
  container reached neither web.archive.org nor a GitHub release for the other seven —, 11,120 at `0423a7f`, 11,117
  passed and 3 skipped; the integration suite green in CI, and `Remote corpus` run 111's acceptance tests and referee
  checks over all 242, on the library code [#347] ends with. Before it, 8,466 unit on [#344]'s branch (20 skipped: 2 by design, the theory over the
  remote corpus's streams whose length is wrong, the fifteen remote documents of the theory over the corpus's filter
  chains, which have no document without them, the test held to PDFBox's remote report, and iPRES's stream that lost its
  `endstream`) + 1,495 integration (skipped without Docker) + 48 for the remote corpus's fetcher + 43 for the roadmap's
  mirror on GitHub. With all 242 remote documents fetched, on [#344]'s branch:
  11,155 unit on the maintainer's machine at `2c7cf01` — 11,052 at `19a2296` —, 11,152 passed and 3 skipped — the
  laziness test on the two documents recorded as unsupported until [#47], which every other
  test holds to their expectations, and the private manifest the machine lacks —; 3,760 integration against qpdf in its
  container at `26ce787`, run class by class, 3,754 passed and 6 skipped where qpdf cannot walk a damaged document's
  pages; and `Remote corpus` run 109's 11,155 acceptance tests and 3,760 referee checks, on the library code
  [#344] ends with.
- **Coverage**: on [#347]'s branch at `0423a7f`, as Codecov reports it, 99.71 % of the 19,833 lines it counts, against
  99.73 % on `main`; of the patch's 151 coverable lines 149 whole and 2 partial, both defensive branches no input takes —
  the rank of a member whose stream the expansion has not recorded (`ExpandEachObjectStream`), and a served stream whose
  header the probe cannot read (`CheckMembersARebuildServesElsewhere`). Before it, on the committed corpus, as Codecov
  counts it (a line with an untaken branch is partial, the generated Arlington tables left out), 99.48 % of `src/`, 10,005 of 10,057 lines, of the patch's 1,461 coverable lines 1,460 run
  and 1,456 whole, and 1,307 of their 1,313 branches, at `2c7cf01` on [#344]'s
  branch — at `dfb03fc`, of its patch's 1,462 coverable lines 1,460 run and 1,455 whole, the seven not whole a defensive
  branch of the parse of an object no section records (two lines), the line test's guard against a place before the
  bytes it read (two), two lines of `SizeWrongRule` (a branch the compiler adds, 71, and the arms of `Counted` that give
  null, 162) and the defensive arm of the walk past a header's signs, none of which an input takes but the line test's
  guard, which a test written since in the mutation step takes —; on [#318]'s, 99.44 %, 8,859 of 8,909 lines, of its
  patch's 1,123 coverable lines 1,122 run and 1,120 whole, and 933 of their 935 branches. Codecov's patch report leaves
  `ObjectStreamDependencies.cs` out, so [#318]'s figures were counted from the unit suite's own report by Codecov's
  rule, which gives Codecov's totals for `663cb80` to the line. The branch's figures are counted the same way, from the
  suite's report at `2c7cf01`, and leave 52 lines. Five are the batch's, in its patch, none of which an input takes: the
  defensive check of the parse of an object no section records, whose one caller asks only places the search found
  inside the file (`PdfFileReader` 4075, 4077); the defensive arm of the walk past a header's signs, a place the search
  finds lying whole within the bytes searched, its number or `xref` with it (5567); and two lines of `SizeWrongRule`
  that only a section named again that is no hybrid stream would take, every such record leaving a chain the rule does
  not judge — the arms of `Counted` that give null for it (162), and the branch the compiler adds for the `?.` through
  which the loop reads that null (71). The other 47 are the 50 left on [#318]'s branch but three, given below at the
  line numbers `2c7cf01` gives them, the batch having moved `PdfFileReader`'s and a few of `PdfObjectParser`'s and
  `PdfFilterPipeline`'s. The 50 left on [#318]'s branch are those the rule of 2026-09-29 leaves (`CLAUDE.md`,
  *Coverage*), and five that only faults filed reach. Forty-seven are [#297]'s; three are batch 18's, in
  `ObjectStreamDependencies`: a defensive arm of a chain's walk, which the walk enters only while it is not done (924,
  926), and a `??=` on a list the walk of a standing made as it began (1353). [#297]'s are members that are private, or
  of a private type, which no input reaches — eight lines of `ArlingtonWalk` (194, 925, 1029, 1039, 1040, 1060, 1062,
  1152) and three of a defensive branch of `PdfLexer` (161, 164, 165) —, a `?.` on an index never null where it is read
  and a switch's default arm (`CrossReferenceProbe` 125 and 311, `PageTreePageOrphanedRule` 50, `RootInvalidRule` 111
  and 120), a line the compiler puts after a call that never returns (`PdfFileReader` 2955), two branches the compiler
  adds that no input takes (`FileQuote` 145, an interpolation's buffer too small; `PdfObjectParser` 1292, a
  concatenation's null check); a defensive arm of the real's expansion (`PdfReal` 66, batch 5), one of the six ways out
  of a cross-reference stream row's `switch` (8294) and `Repair`'s guard against a second rebuild (9738, 9740);
  `KeepObjectStream`'s release of the contents it replaces (9693, 9695, batch 13). Batch 14's five: the switch over a
  header's place (`CrossReferenceProbe` 184), whose target past the enumeration's values the compiler adds and no input
  takes; the one over a kid's presence (`PageTreeWalk` 382), whose default the batch's arm for a cut object leaves to
  `Deferred`, which `GetPresence` never answers; the test that a judged catalog's `/Pages` is a dictionary or a stream
  (`ObjectGraph` 312), which the walk judged a root there, so is one; and two defensive lines of a rebuild:
  `FinishRebuild`'s `??=` when reading `/Root` reaches a guard (`PdfFileReader` 9904), which always finds one held
  already — a `/Root` a guard cuts is a regular object, or a member of an object stream, that the rebuild read as it
  took in object streams, reaching the guard first —, and `KeepMembersHiddenByGuard`'s position of a stream the rebuilt
  index lacks (10184), when every stream it marks hidden was read from that index. Batch 15's two, in the probe's
  judging of the members only a rebuild places: the arms that pass over an object stream the rebuilt index does not hold
  as a regular object, or whose header the reader cannot read as one (`CrossReferenceProbe` 478), when the rebuild
  placed the members there for having found that stream and read its header; and a `?.` on the chain's index (492),
  which a chain a guard stopped has read. Batch 16 takes three off — `EndStreamAfter`'s two defensive arms, which a
  document whose searches are spent now takes, and the switch over a `/Length`'s presence —, brings a fourth into its
  patch, the `HasValue` checks the compiler adds on a `long?` (`PdfFileReader` 8106), and leaves fifteen. In its patch,
  nine no input takes: a `?.` on the set of what the chain read, never null where it is forgotten (2892), and on a
  cross-reference stream's `/W` element, `/Size` and `/Index` pair, which the parser never leaves null there (7548,
  7587, 7603); those `HasValue` checks, on values already tested (8106); a deferred length's search, always memoized by
  the parse before (7297), and its value, never null (7474), defensive arms of private members; the operand of
  `ChecksumDisagrees` its one caller's precondition makes true (`PdfFilterPipeline` 369); and a header's generation
  neither cut nor an integer, which the tests before it rule out (`PdfObjectParser` 1647). Outside it, the dispatch the
  compiler builds over `SkipContainer`'s token kinds, to which the batch's arm for a header adds a leaf no kind reaches
  (`PdfObjectParser` 1370); and five lines that inputs still reach, but only to show a fault filed rather than pinned: a
  classic table's first token the probe's bound cut (`PdfFileReader` 5962, 5964, 5965, [#295]), and `MarkCutByGuard`,
  which a `stream` a guard's edge parts reaches (9045, 9047, [#294]). Batch 19 takes three of batch 16's off, which the
  branch runs whole: that generation's test and `MarkCutByGuard`'s two lines. `codecov.yml` asks 95 % of each patch, and
  lets the project drop by half a point at most; the aim is 100 %.
- **Mutation testing**: since 2026-10-30, a finite step of every pull request that changes `src/`, apart from its own
  review (ADR 53): a fixed, first-order catalog over the lines it changes, and over the conditions it adds to or changes
  in a test that reads the library's code; each survivor killed by a test, classed equivalent or, in such a test,
  widening, each with its reason, or filed as debt. Batch 17's review, whose mutation sweeps had found no fixed point in
  seven rounds, set it ([#308]). Since 2026-11-01, at the maintainer's choice, a mutant that changes only what the code
  costs is killed where a deterministic measure tells it apart — allocation, an exact count —, and is otherwise classed
  equivalent in what is reported, its reason the bound of its extra cost, a benchmark holding the constant factor
  (invariant 9). Batch 18 ran the step on its implementation and on the fixes of each round that changed code in `src/`
  (those of rounds 7, 8 and 10 changed none), and over its whole patch at its end: 1,675 distinct mutants run, 1,593
  killed by a test, 57 classed equivalent, 8 equivalent in what is reported and 17 superseded by a later rewrite or
  removal of their line, none filed; one more, which did not compile as written, classed equivalent by reading without a
  run ([#318]). Batch 19 ran it at each stage of its implementation and of its fixes that changed code in `src/`, and
  over its whole patch at its end, 3,751 mutants, of which 15 a stage record still covered: of the 3,736 run at
  `19a2296`, 3,193 were killed, 224 did not compile, and the 319 that survived were settled — 113 killed by tests
  written since, 206 classed equivalent, 29 of them in what is reported —, none filed; every first verdict of
  equivalence was checked a second time, against an input built to tell the two apart, which overturned 39 of the 245
  ([#344]). Since 2026-11-11 (ADR 53 amended, ADR 54) the step runs once, after the review loop stops, over the
  defenses among the changed lines only — bounds, guards, depth and count limits, visited sets, the exits of loops and
  recursions that read the file —, and a mutant no document and no public call tells apart is equivalent: about 6,000
  mutants since batch 5 had found one defect of the library. Batch 20, the first under it, built 51 mutants over the
  defenses among its changed lines — the windows of the scan and of both searches near an offset and the byte past them,
  the cache's eviction, the object streams' weight, the tentative index's rollback, the probe's room — and left the rest
  to the review: 27 were killed by the suite as it stood, 4 of them once compiled without warnings as errors; 12 by tests
  the step wrote, 3 of them after the second opinion overturned their first verdict; 12 were classed equivalent, one in
  cost only, each verdict checked by a second agent; none was filed ([#347]). Batch 21's first pull request built 92
  over its scan, walk, sweep, delimiting, stream confirmation and read budget, expansion and encryption at opening: 50
  killed by the suite, 7 of them by a hang, 2 by tests outside the runner's class filter; 34 by tests the step wrote,
  each re-run on the final commit; 6 classed equivalent, each checked by a second agent; 1 filed, told apart only by a
  file of `Array.MaxLength` bytes ([#353]); and 1 gone with the code it mutated, a second resolution of `/Encrypt`,
  once the second agent overturned its equivalence and the maintainer chose to remove it ([#352]). The step also found
  two defects, filed in [#356] and [#358].
- **Review rounds**: since 2026-11-11 (ADR 54) a finding's severity follows its effect and whether real files have its
  input; a pull request's rounds stop at the first with no blocker and no major, at the eighth at the latest, and a
  batch pays at most six issues. Batch 19's 13 rounds, measured, are in
  [Where a batch's time goes](research/2026-11-session-speed.md). Batch 20 stopped at its first round: seven lenses, 21
  findings, the one major refuted as [#171]'s, 5 minors and 15 nits, whose texts and tests the final pass fixed and the
  rest filed as [#348] to [#350] ([#347]). Batch 21's first pull request stopped at its fifth: round 1, six lenses,
  29 findings — 11 blockers, all the new scan's cost, 1 major, an unbalanced parenthesis the maintainer settled —;
  rounds 2 to 4, one or two blockers each in the code the round before wrote; round 5, 3 minors and a nit; the rest
  filed as [#353] to [#358] ([#352]).
- **CI**: green on [#347]'s branch at `0423a7f`, the integration suite against qpdf, the .NET 11 island and the site
  included; `Remote corpus` run 111 green on it at `3dfdf9d`, whose library code the branch keeps. Before it, green on
  `main` at `21a0298` (CI run 927), and on [#344]'s branch at `2c7cf01`
  (CI run 1055). `Remote
  corpus` passed on every document in the nightly run 18, on `main` at `8c54490`, and in runs 19 and 20, on [#203]'s and
  [#211]'s branches, each rerun once after web.archive.org refused a document ([#204]), in run 21, on [#213]'s branch,
  at its first attempt, and in run 22, on [#214]'s branch, rerun once for the same refusal. The nightly run 23, on
  `main`, failed only because web.archive.org refused the download of ECan's scan (`Connection refused`, [#204]): every
  test over the 241 it fetched passed. Run 24, on [#217]'s branch, lost the same document at its first attempt and
  passed whole at its one rerun; run 25, on [#225]'s branch, passed whole at its first attempt; run 26, on [#226]'s
  branch, lost ECan's scan again at its first attempt and passed whole at its one rerun. The nightly run 27, on `main`
  at `2f26244`, lost the CFIA's form, web.archive.org refusing the connection ([#204]), and passed every test over the
  241 others. Runs 28 and 29, on [#231]'s branch, passed whole at their first attempt, the second on the code the branch
  ends with; CI run 473 is green on that code. The nightly run 30, on `main` at `78ff13b`, passed whole at its first
  attempt, and so did runs 31 and 32, on [#238]'s branch, the second on the batch's last code; run 33, on that code
  rebased onto `main`, lost ECan's scan at its first attempt, web.archive.org refusing the connection ([#204]), and
  passed whole at its one rerun. CI run 509 is green on that code. The nightly runs 34 to 36 and 39 to 41, on `main` at
  `83d15a1`, passed whole at their first attempt; runs 37 and 38 lost ECan's scan, web.archive.org refusing the
  connection, and run 42 the 88 documents of the ipres2017 archive, its host timing out ([#204]), every test over the
  others passing. Run 43, on [#248]'s branch, and run 44, on its last code, lost the ipres2017 archive the same way at
  their first attempt, every other test passing; run 44 passed whole at its one rerun. CI run 534 is green on that code.
  The nightly run 45, on `main` at `a930544`, passed whole at its first attempt, and so did run 46, on [#249]'s branch,
  on the batch's last code. Runs 47 and 48, on [#252]'s branch, each failed on a test the branch then fixed — the corpus
  manifest's schema, which the new guard's commit had left out, then a test that took a cut cross-reference stream's
  rows as read —, every other test over the 242 documents passing; runs 49 to 51 passed whole, run 51 on the batch's
  last code. The nightly run 53, on `main` at `2d7d5dc`, lost ECan's scan, web.archive.org refusing the connection, and
  failed one test, an allocation window that a collection elsewhere counted 8 KB over ([#253]), every other test
  passing. On [#254]'s branch, run 52, on its first code, and run 54, on the code its second review changed, passed
  whole at their first attempt; run 55, on its last code, lost ECan's scan, web.archive.org refusing the connection, and
  the 88 documents of the ipres2017 archive, its host timing out ([#204]), every test over the others passing; at its
  one rerun it lost the ipres2017 archive alone, every test over the 154 others passing, and the 88 it lost pass on that
  code in this container, whose unit and integration runs hold them. The nightly run 56, on `main` at `2d7d5dc`, and run
  57, on [#254]'s last code, lost documents their hosts refused or timed out on ([#204]) — run 57 ECan's scan and the
  ipres2017 archive —, every test over the others passing, run 57's 5,216 acceptance tests and 2,575 referee checks over
  153 documents. The nightly run 58, on `main` at `8385c81`, passed whole at its first attempt, and so did runs 59 and
  60, on [#258]'s branch, run 60 on the code it ends with. The nightly run 61, on `main` at `55ea05f`, lost ECan's scan,
  its host timing out in the TLS handshake ([#204]), every test over the 241 others passing: 6,691 acceptance tests and
  3,751 referee checks. Run 62, on [#265]'s branch at `d3c0239`, lost the ipres2017 archive, its host timing out
  ([#204]), every test over the 154 others passing: 6,024 acceptance tests and 2,905 referee checks. Runs 63 to 65, on
  its next commits, passed whole at their first attempt, run 65 on the code it ends with. The nightly runs 66 to 68, on
  `main` at `55ea05f`, and 69, at `6a5aad3`, passed whole at their first attempt. Run 70, on [#278]'s branch at
  `f3a0d32`, failed one test: the manifest still required `object.redefined` of PDFBox's `pdfbox3947`, whose broken
  object stream header the batch ends before the pairs the rebuild had taken for eleven definitions of one number, and
  the branch corrected the expectation. Run 71, on `48c7172`, passed whole at its first attempt, and so did run 72, on
  `e69756b`. Run 73, on `0de8727`, the same tree under a corrected commit message, lost ECan's scan at its first
  attempt, web.archive.org refusing the connection ([#204]), every test over the 241 others passing, and passed whole at
  its one rerun. Run 74, on the code [#278] ends with, passed whole at its first attempt: 7,135 acceptance tests and
  3,760 referee checks. The nightly run 75, on `main` at `a76c9da`, passed whole; run 77 lost ECan's scan, its host
  timing out in the TLS handshake, and run 79 the 88 documents of the ipres2017 archive, its host timing out ([#204]),
  every test over the others passing. On [#297]'s branch, run 76, on `8f6a085`, passed whole; run 78, on `241d701`, lost
  the ipres2017 archive the same way and failed one test, whose bounds the zlib of this container set and the zlib-ng of
  the runtime CI installs does not keep, which `d468352` corrected; run 80, on that commit, passed whole at its first
  attempt: 7,742 acceptance tests and 3,760 referee checks. Run 81, on `b9df537`, the first form of the record [#297]
  ends with, lost two documents at its first attempt, web.archive.org refusing the connection or timing out ([#204]),
  every test over the 240 others passing, and passed whole at its one rerun; CI run 780 is green on that code. The
  nightly run 83, on `main` at `ce30948`, [#297] squashed, passed whole at its first attempt, and so did runs 84 to 86
  ([#300]) and the nightly run 88, on `main` at `e3e2034`. On [#308]'s branch, runs 87, on `5b19cbc`, the first form of
  the batch's record, 89, on `1b8edde`, and 90, on `3109cb5`, whose library code the branch ends with, passed whole at
  their first attempt: run 90's 7,885 acceptance tests and 3,760 referee checks. Run 93, on `b932b9b`, with ADR 53 and
  the last samples, passed whole at its first attempt: 8,020 acceptance tests and 3,760 referee checks. The nightly runs
  94 to 97, on `main` at `75999e4`, [#308] merged, passed whole at their first attempt. On [#318]'s branch, run 98,
  on `1355cd8`, passed whole at its first attempt, from the cache alone: 9,346 acceptance tests and 3,760 referee
  checks; run 99, on `755750b`, whose library code the branch ends with, failed one test, [#317]'s, on .NET 10, every
  other passing, and passed whole at its one rerun. The nightly runs 100 to 108, on `main` at `21a0298`, [#318] merged,
  passed whole at their first attempt, run 108 from the cache alone: 9,346 acceptance tests and 3,760 referee checks. On
  [#344]'s branch, run 109, on `9a21ac7`, whose library code the branch ends with, passed
  whole at its first attempt, from the cache alone: 11,155 acceptance tests and 3,760 referee checks.
- **Corpus**: 168 committed documents, 23.0 MB — 19 generated here, 3 from Word and PDF24 on Windows, 146
  third-party files under attribution-only licenses (`docs/corpus-sources.md`). Beside them, a **remote
  corpus** of 242 documents we may use but not redistribute, fetched at a pinned SHA-256 and size (ADR 32),
  88 of them out of their authors' archive (ADR 33), 5 out of web.archive.org, and tested every night by `Remote
  corpus`, last green in run 108 on `main` on 2026-11-11, from the cache alone. Since [#204] and [#291], 104 of them — the iPRES archive and
  two Government of Canada forms among them — come from the project's own copy in
  [AdCodicem/AdCodicem.Pdf.Corpus](https://github.com/AdCodicem/AdCodicem.Pdf.Corpus) (ADR 51), and the job keeps
  what it fetched in an encrypted Actions cache. All 410 are described in `tests/corpus/manifest.json`.
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
| Indexing | synthetic, 1000 pages, ~4 MB | 200 to 222 µs | 83 KB |
| Indexing, then reading every page | synthetic, 1000 pages, ~4 MB | 3.7 ms | 1.7 MB |
| Indexing and walking the page tree | real ReportLab document, 1000 pages | — | 3.2 MB |
| Indexing a classic table (xUnit, after a first reading) | generated, 300,000 objects | — | 12.0 MB |
| Indexing a cross-reference stream whose rows Flate stores (xUnit, after a first reading) | generated, 300,000 objects | — | 22.5 MB |
| Indexing a chain of a thousand classic sections (xUnit, after a first reading) | generated, 300,000 objects | — | 24.2 MB |
| Indexing a classic table of numbers three apart, which the index's map serves | generated, 300,000 objects | 64 ms | 20.9 MB |
| Validating under the structural profile, the document already open and read, a medium job | synthetic, 1000 pages | 2.8 ms | 623 KB |
| Typing and checking the objects the trailer reaches against the Arlington model, alone, a medium job | synthetic, 1000 pages | 1.7 ms | 312 KB |
| Opening and validating under the structural profile, a medium job | synthetic, 1000 pages | 6.2 to 6.5 ms | 2.3 MB |
| Settling the index of a sound document, after opening it | synthetic, 1000 pages | 0.2 ms | 400 B |
| Probing the cross-reference entries, the document open and settled | synthetic, 800 pages | 0.25 ms | 7.2 KB |
| Probing rows that all lead into one run of 2 MB of null bytes | generated, 800 rows | 0.52 ms | 428 KB |
| Walking the object streams' dependencies | generated, 1000 object streams sharing a 16 KB `/DecodeParms` | 2.9 to 3.3 ms | 1.1 MB |
| Walking the object streams' dependencies through nests of streams by `/Length` | generated, 1000 object streams leading through 16,128 | 360 to 364 ms | 16.9 MB |
| Rebuilding an index past 12,000 `trailer` keywords that share long comments, a medium job | generated, two comment lines | 211 to 220 ms | 642 KB |
| Indexing, then reading every member of one object stream, a medium job | generated, 100,000 members | 136 ms | 64.2 MB |
| Decoding a whole Flate content stream | 4 MB decoded, about 330 KB encoded | 8.3 ms | 13.1 MB |
| Decoding the same stream without its checksum, or without its last five bytes | 4 MB decoded | 11.7 ms | 13.1 MB |
| Decoding the same stream under a wrong checksum, kept whole | 4 MB decoded | 12.1 ms | 13.1 MB |
| Decoding a stream that turns corrupt halfway, the 2 MB before the fault kept | 4 MB of content | 8.1 ms | 6.6 MB |
| Decoding a chain of ASCII85 and Flate | 4 MB decoded | 10.4 ms | 13.7 MB |
| Indexing, then parsing every page's content stream, each past the reader's first window | synthetic, 1000 pages of 16 KB | 4.0 to 4.6 ms from memory, 7.2 to 7.4 ms from a file | 1.75 MB |
| The same, each stream declared 2 bytes too long and searched for its `endstream` | synthetic, 1000 pages of 16 KB | 10.6 to 11.3 ms from memory, 14.9 to 16.4 ms from a file | 2.71 MB |
| Searching a damaged stream's data for its `endstream`: text lines, then nested strings | generated, 4 MB | 399 to 403 µs, then 35.0 to 36.1 ms | 7.2 KB |
| Rebuilding an index by scanning for object headers, each with a comment between its tokens | generated, 4 MB, 100,000 headers | 44.8 to 45.1 ms | 17.8 MB |
| Parsing one object, a medium job | Word's page dictionary | 6.0 µs | 4.6 KB |

The gap between the first two rows is the library's promise: opening a document does not read its content. The
validation rows are slice 3's thirty-eight rules. The first measures the rules alone on a document whose objects the
reader has cached: each entry of the index probed, the page tree walked, every object the trailer reaches met once and
typed and checked against the Arlington model, every object of the index looked at for a page the tree leaves out — the
allocation is the sets of object numbers met, each page's index, and the Arlington walk's queue and tallies. The second
is that walk alone, which the four rules generated from the model share. The third is what a caller validating a file it
has not read pays: every object parsed for the first time, about 2.7 KB a page, no stream's data read. Slice 3's first
thirty-four rules took 716 µs and 308 KB on the first row, slice 2's twenty-one 214 µs and 8.9 KB, slice 1's one rule 86
ns and 232 B; the rows grow with each slice of M02. Since batch 13 ([#258]) the validator settles the index first: on a
sound document, that is reading the header each regular entry places, 0.25 to 0.28 ms and about 50 bytes for a thousand
pages, which the third row includes; the first, whose document is settled already, pays nothing more. The three
validation rows allocate what they did on `main` before the batch, to 40 bytes; under a medium job, twice, alternated,
the first took 3.0 and 3.7 ms there, 3.4 and 3.6 ms on the branch, and the third 8.4 and 9.3 ms there, 9.2 and 9.7 ms on
the branch, settling within a gap the runs' spread covers. Since batch 14 ([#265]) settling and the probe read each
header as a load does, in the reader's `PlaceHeader`: on a sound document the first 64 bytes decide each, as before, and
settling allocates about 400 bytes, the record of the runs of blanks a reading crossed; under a medium job, twice,
alternated, opening and settling took 0.40 and 0.42 ms on `main`, 0.47 ms on the branch, and opening and validating 6.6
and 6.8 ms there, 6.9 ms on the branch. The probe rows are `CrossReferenceProbeBenchmarks`' (ShortRun, twice): a sound
document of 800 pages, 1,602 entries, took 0.18 to 0.20 ms and 6.7 KB on `main`; rows that lead into one run of blanks,
which `main` read 64 bytes of each, were crossed once, from the first row to its end, in 0.43 ms, against 0.40 to 0.41
ms; the 412 KB, on both, were the 800 findings. The walk of the object streams' dependencies reads once an object their
decoding keys share: a thousand streams sharing a 16 KB `/DecodeParms` took 118 to 128 ms and 125.6 MB on `main`, each
stream parsing it again, and took 4.1 to 4.3 ms and 2.3 MB (`ObjectStreamDependencyBenchmarks`, ShortRun, twice). Since
batch 15 ([#278]) the walk follows a key's chain apart from a filter's walk: 4.2 to 4.3 ms and 1.9 MB, against 4.1 to
4.9 ms and 2.3 MB on `main` in the same runs. A member of an object stream is read to the next one's start, and the
header's tables grow with its pairs rather than being allocated from `/N` (`ObjectStreamMemberBenchmarks`, ShortRun
twice and a medium job, `main` and the branch alternated): indexing and reading the 100,000 members of one stream takes
136 ms under the medium job, against 176 ms on `main`, within the runs' spread, and allocates 64.2 MB against 63.2 MB,
the tables' growth; a thousand take 1.2 ms and 728 KB on both. With each entry giving the next member's index, so that
every member is found through the stream's lookup by number, 198 ms against 188 ms, 96.8 MB against 94.8 MB. The probe
allocates 16 KB more for the 800 rows that lead into blanks, 428 KB against 412 KB, in 0.52 ms against 0.45 to 0.60 ms,
and 20 bytes more for a sound document of 800 pages, 0.24 to 0.27 ms against 0.22 to 0.32 ms. Opening allocates 140
bytes more, the reader's new fields; the parser and the validation rows allocate what they did to 160 bytes, their times
within the runs' spread. Indexing costs the index, 40 bytes an object numbered in order, whatever the objects weigh,
since a section makes room for its rows before reading them ([#164]); an index a rebuild grows allocates 80 to 160, and
numbers spread far apart, which the seeded map past the slots holds, keep 60 to 110 ([#155], ADR 52), the fourth
generated row's 20.9 MB for 300,000 numbers three apart (`IndexBenchmarks`, ShortRun, twice). A cross-reference stream
adds 7 to about 35 bytes an object for its rows, and the object stream holding the catalog, when one does, what it
holds. Batch 17 ([#155]) made the hash table the index was arrays, and hashed every other table keyed by numbers through
the process's seed: under a medium job, twice, alternated, indexing a thousand pages took 200 to 222 µs and 83 KB
against 230 to 242 µs and 123 KB on `main`, an index of 300,000 objects 8 to 13 % less time, and the validation rows 2.9
to 3.2 ms against 2.6 to 2.7, the Arlington walk 2 to 6 % more, opening and validating 7.0 to 7.3 ms against 6.7 to 6.8
— the seeded hash the validator's walks now pay, about 15 % on the rules, is filed ([#305]). The first row is batch 11's
(ShortRun, twice): 394 KB on `main` in the same runs, 304 to 324 µs. The second is batch 16's ([#297], ShortRun, twice,
alternated): no stream keeps its raw bytes since, each read from its source when it is asked for, so reading every page
takes 4.1 to 4.3 ms and 1.79 MB, against 6.3 to 7.1 ms and 5.82 MB on `main`. The third row is asserted as a budget in
CI (`CorpusReadingTests`), so an allocation regression fails the build; it was 2.4 MB when M01 closed, and M01's review
measured 3.2 MB on 2026-10-01, under the 4 MB budget. The three generated rows are held to 12.6 MB, 23.6 MB and 25.5 MB,
5 % over their figures (`DocumentReaderTests`, [#193], [#164], [#155]), where the hash table the index was allocated
16.9 MB, 27.4 MB and 30.6 MB sized so, and 32.6 MB, 43.1 MB and 33.2 MB left to grow; those rows' MB are 10^6 bytes. A
stream that ran out is read twice to tell a lost checksum from lost data (T32), which costs time on damaged streams
only; the second reading keeps nothing. A stream whose checksum is wrong, or that turns corrupt, is read again after the
fault ([#56]): its body as raw deflate, then, when that faults too, once more a byte at a time through the 8 KB piece of
input the fault was met in. A sound stream is read once, as it was, and allocates 8 bytes more for it (88,480 → 88,488 B
at 64 KB); a medium job measured 93.5 → 88.8 µs at 64 KB and 10.0 → 8.85 ms at 4 MB, within noise, and the 25,157 sound
streams of the corpus decode to the same bytes in a time within the spread of four alternating runs. Damaged streams pay
for the replay: the corrupt 64 KB stream took 54 µs to be left encoded and takes 671 µs to keep its 32 KB; the
byte-at-a-time part costs about 0.3 µs an input byte, 8 KB at most per reading of a form of the data, so an 8,013-byte
stream that faults at its end takes 1.8 to 2.5 ms instead of 64 µs, and a file made of nothing but small corrupt streams
reads about 40 times slower than before — still in time linear in its size. The damaged streams of the corpus take 43 →
60 ms in all. What a 4 MB decode allocates is the output doubling towards its size, T28's and M23's business. The last
two rows are [#55]'s (`StreamLengthBenchmarks`, medium job, before the check and with it). A stream past the 8 KB window
is checked by asking the file for the 64 bytes after its declared length. From memory that is a copy, and costs a valid
document nothing measurable: 4.4 ms before, 3.9 ms with it, 2.02 MB both. From a file, the way the library recommends
opening a large document, it is one read more for each stream: 6.4 → 7.1 ms for the thousand, about 0.7 µs a stream,
allocating nothing more. A length Distiller 3 wrote two bytes too long sends the reader searching the 8 KB of data past
the window for each stream — 4.1 → 6.4 ms from memory, 6.8 → 10.3 ms from a file, 2.02 → 2.82 MB for a report and a
record each; looking for an object header in what it reads as well costs about 0.6 µs more a stream from memory and 1.4
µs from a file — 6.7 → 7.3 ms and 11.2 → 12.5 ms, the means of two medium runs of each alternated, the same allocation;
carrying what a read cuts of a header into the next, and the first read sized from what the parser holds, cost nothing
measurable beside it, 7.1 → 7.0 ms and 11.4 → 11.1 ms measured the same way. Data made of near-headers costs the search
more than data that holds none: 16 MB of ` obj` over and over take about 63 ms to search to the end, of `0 0 obj ` 44
ms, of letters 10 ms — some 4 ms a megabyte searched at worst, and, the budget letting a document's searches read about
five times the file, some 20 ms a megabyte of file. Since batch 16 ([#297], medium job, twice, alternated) a sound
stream costs what it did, and a wrong length about 2 µs and 120 to 300 bytes more: one search inside the window or past
it, memoized with what follows the `endstream`, up to the next object's header or the next object the index places; the
last two rows were 8.5 to 9.1 ms and 12.4 to 13.6 ms, 2.59 MB, on `main` in the same runs ([#296]). Inside the window
the check is what it was, but a wrong length now costs its record and its mark of one report: about 440 bytes more
allocated per stream (the 4 KB streams, 2.20 → 2.64 MB), and about 180 bytes kept for the life of the document — 50,000
such streams, read with a cache of 64 objects, keep 9.1 MB more. A sound stream allocates and keeps nothing more. The 4
KB streams take 3.3 → 3.5 ms from memory and 5.2 → 5.3 ms from a file, 3.7 → 3.9 and 6.0 → 6.0 ms when each length is
wrong, within the measurement's error. The last row is `ParserBenchmarks`' ([#119], [#172]), the means of two medium
runs of each alternated, before the checks of the syntax and with them. What a sound object allocates is what it was, to
10 bytes: 4.6 KB for Word's page dictionary, 1.37 MB for a thousand dictionaries of strings, 1.56 MB for a thousand of
escaped names, 1.44 MB for ten thousand distinct keys. The checks cost 5.7 → 6.0 µs for the page dictionary, about 9 ns
for each of its 34 names, 1.71 → 1.77 ms for the names, 1.47 → 1.49 ms for the strings and 2.74 → 2.76 ms for the keys.
A hostile object pays for its reports, formatted until the diagnostics hold 1,000 and counted past them: a thousand
hexadecimal strings with stray bytes take 81 → 214 µs and 118 → 448 KB, a thousand names whose `#` is no escape 76 → 201
µs and 55 → 432 KB, ten thousand entries that give ten keys again and again 1.22 → 1.53 ms and 524 → 799 KB; ten
thousand distinct keys given null, which a set now holds to tell a repeat that follows, 1.03 → 2.18 ms and 313 → 970 KB;
an object the end of the data leaves open, 19.6 → 21.0 µs.

Batch 18 ([#318]) left indexing, reading every page and the probe as they were, to 160 bytes, and the validation rows
within the runs' spread, 1.1 KB more allocated (`ValidationBenchmarks`, a medium job, twice, alternated: the rules 2.79
to 2.81 ms against 2.63 to 2.75 on `main`, opening and validating 6.2 to 6.5 ms against 6.3). The walk of the object
streams' dependencies now follows what each stream's decoding reads, through the streams and the other object streams'
objects a chain meets, and so walks nests `main` did not: a thousand streams leading through 16,128 streams by `/Length`
take 360 to 364 ms and 16.9 MB, where `main` took 4.8 to 5.1 ms and 5.5 MB and found none of their loops. The shapes
`main` walked cost less: a thousand streams sharing a `/DecodeParms` 2.9 to 3.3 ms and 1.1 MB, against 3.8 ms and 2.2
MB; in a ring 4.2 to 5.4 ms and 3.5 MB, against 41 to 43 ms and 60 MB; whose standings wait 54 to 77 ms and 3.3 MB,
against 640 to 713 ms and 108 MB (`ObjectStreamDependencyBenchmarks`, ShortRun, twice, alternated, the branch's
benchmark run on `main`). The rebuild's scan for trailers past 12,000 keywords that share long comments takes 211 to 220
ms over a reference or a null and 408 to 447 ms over a dictionary, against 258 to 288 ms and 473 to 475 ms on `main`
(`TrailerScanBenchmarks`, a medium job, twice, alternated).

Batch 19 ([#344]) leaves indexing, reading every page, the probe, validating, the
stream-length rows and the rebuild's scan for trailers within the runs' spread, each 0.96 to 1.09 times what it takes on
`main`, the error bars of the branch's runs and of `main`'s overlapping on every row (`ReaderBenchmarks`,
`IndexBenchmarks`, `CrossReferenceProbeBenchmarks`, `ValidationBenchmarks`, `StreamLengthBenchmarks` and
`TrailerScanBenchmarks`, ShortRun, each class twice on `2c7cf01` and twice on `main`, alternated, one process at a
time): indexing a thousand pages takes 172 to 173 µs against 167 to 169 µs, reading every page 2.86 to 2.93 ms against
2.75 to 2.84, the rules 2.51 to 2.55 ms against 2.56 to 2.65, opening and validating 5.67 to 5.84 ms against 5.59 to
5.61. Opening allocates about 770 bytes more, whatever the document (5.47 → 6.23 KB for ten pages, 82.84 → 83.59 KB for
a thousand); a chain of a thousand sections about 280 bytes more a section (23.11 → 23.38 MB for 300,000 objects); a
stream whose `/Length` is wrong 20 to 30 bytes more (2.67 → 2.69 and 2.70 MB for a thousand); the rules about 140 bytes
more, and the scan for trailers about 220; the rest allocates what it did, to the last digit its table gives. The two
classes the batch adds, which `main` lacks, ran twice on the branch: 4 MB searched for a lost `endstream` in 399 to 403
µs over text lines, 35.0 to 36.1 ms over nested strings and 40.9 to 41.3 ms over one-byte tokens, the costliest shape in
these runs, allocating 7.2 KB, 7.6 KB where a keyword in a comment waits on what follows its line; 100,000 headers with
a comment between their tokens scanned in 44.8 to 45.1 ms and 17.8 MB, as many plain headers in 38.3 to 38.4 ms.
Measured as the batch went, on a shared machine: the search for a damaged stream's `endstream`, which now lexes from its
start a line that holds a `%`, costs what it did on text and on binary data, about 0.5 ms for 4 MB, and allocates 6.9 KB
against 6.4 KB, whatever the data (`StreamEndSearchBenchmarks`, ShortRun, twice, alternated with `main`, in the review's
first round); data built to cross such a line at every byte or two costs more, in time linear in its size — 4 MB of
one-byte tokens 36 to 39 ms, of comment lines 27 ms, of nested strings 44 ms, each about 0.5 ms on `main`, which took no
header with a comment —, and a timing test holds it so, 4 MB taking 3.99 times what 1 MB takes; the review's fourth
round halved what a keyword waiting on comment lines costs, 87.3 to 43.5 ms, and measured nested strings, the worst
shape, at 58 ms, some 14 ns a byte. The rebuild's scan for object headers costs what it did on sound files, 3.0 to 3.2
ms for 4 MB, and on plain headers, 32 to 34 ms against 34 to 36; lines that hold a `%` 6.9 to 7.5 ms against 1.3,
strings that hold one 12.7 to 13.7 ms against 2.8, and 100,000 headers with a comment between their tokens 40 to 43 ms
and 18 MB, indexed where `main` found none (`ObjectScanBenchmarks`, the same runs). The chain pays for the numbers it
refuses while it is read, about 60 bytes a number: 6.0 MB for 100,000 refused rows, and 1.01 GB for a stream whose every
row is refused at the default bound on the index's entries; 1,024 chained streams that cannot be read open in about four
times what 256 take, 197 to 201 ms against 47 to 53 under a `MaxTrailerLength` of 8 KB. A thousand tables naming one
stream of 100,000 rows read it once, 168 bytes more a naming. The white space before a named section is crossed once a
position, its probes reading less than ten times the bytes to the first token's end — 9.14 times at worst, 2,044 spaces
before `xref` —, and 84,288 bytes at most under the default `MaxTrailerLength`, 1.29 times the 65,568 they reach; a link
naming the start of a run of 65,530 spaces before a table reads 215,497 bytes where the file ends past the table, and
411,968 where 8 MB follow it, the table's windows reading the run again (measured at the batch's end, [#295]).

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
   code, and it closes last. Since 2026-11-11 a batch follows the `batch` skill and ADR 54: one that would pay more
   than six issues or change more than about 1,000 lines of code in `src/` is split by code area when it opens.
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
   13. [#128], with [#237] and [#253], a report that does not depend on what was read before it — done, merged with
       [#258] on 2026-10-17, `Remote corpus` runs 59 and 60 green on its branch (journal of 2026-10-17).
   14. [#129] and [#175], with [#256] and [#259], page tree findings that say where and nothing judged that a limit
       cut — done, merged with [#265] on 2026-10-21, `Remote corpus` runs 63 to 65 green on its branch (journal of
       2026-10-18).
   15. [#160] and [#161], object-stream members, with [#200], [#202], [#255] and [#257], and [#273] to [#275], which the
       batch filed — done, merged with [#278] on 2026-10-22, `Remote corpus` runs 71 to 74 green on its branch, run 73
       at its one rerun (journal of 2026-10-22).
   16. [#174] and [#170], stream data, with [#220], [#224], [#230] and [#263] — done, in [#297], `Remote corpus` runs
       76 and 80 green on its branch (journal of 2026-10-25).
   17. [#155], numbers a file chooses to share a bucket, tables keyed by numbers dense or seeded (ADR 52) — done, merged
       with [#308] on 2026-10-30, `Remote corpus` runs 87, 89, 90 and 93 green on its branch (journal of 2026-10-28).
   18. [#167] and [#168], what a rebuild leaves a caller and what the validator's analyses cost, with [#201], [#277] and
       [#307], and [#312], which the batch filed — done, merged with [#318] on 2026-11-03, `Remote corpus` runs 98 and
       99, the second at its one rerun, green on its branch (journal of 2026-10-30).
   19. [#188], [#197] and [#156], where the chain's sections lie and what it reports of one it reads in part, with
       [#207], [#208], [#210], [#221], [#222], [#241], [#261] and [#264], and [#281] and [#317], placed with it — done,
       in [#344], `Remote corpus` run 109 green on its branch (journal of 2026-11-03).
   20. [#189], with [#276] and [#320] — done, in [#347], `Remote corpus` run 111 green on its branch (journal of
       2026-11-12).
   21. [#154] and [#171], with [#348] and [#350], which batch 20 filed, and [#327] — in two pull requests by area, as
       the maintainer chose on 2026-11-13: the rebuild in [#352], open, which closes all but [#171]; the searches near
       an offset and a row next, from the same branch once [#352] merges, which closes [#171].
   22. [#132], with [#218], [#229] and [#260].
   23. [#141] — done in [#254], brought forward with [#144] at the maintainer's request on 2026-10-15 (journal of
       2026-10-15).
   24. [#169]; [#144], its batch's other half, done in [#254] with [#141].
   25. [#181].
   26. [#162], with [#250].
   27. [#163], `MaxDepth` and `MaxNestedLoads` made guards, as the maintainer decided, with [#262].
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
   concern, and [#256], which the maintainer brought into batch 14 on 2026-10-17, its probe being the batch's. Of the
   six batch 14 filed, [#259] was paid in the batch at the maintainer's choice; [#260] goes with [#132], whose rule it
   words; [#261] with [#197], whose search near a row it shares; [#262] with [#163], the parser at its guards; [#263]
   with [#174], [#170] and [#224], the stream data it concerns; [#264] with [#188] and [#197], the chain's reading of
   where its sections lie. Of the five batch 15 filed, [#273] to [#275] were paid in the batch at the maintainer's
   choice; [#276] goes with [#189], whose choice of the definition a rebuild serves it depends on; [#277] with [#168],
   whose walk of the object streams' dependencies it concerns. Of the fourteen batch 16 filed, [#279], [#282], [#287],
   [#289], [#290] and [#292] to [#295] go with [#163], the reader at its guards; [#280] and [#296] with [#181], the work
   one document asks; [#281] with [#188], the chain's reading of where its sections lie; [#283] and [#288] with [#162], the
   filter reports they reword. Of the three batch 17 filed, [#305], the seeded hash the validator's walks pay, and
   [#306], the source reading's survivors, go under M23, which budgets what the library costs and hardens it; [#307],
   the test of a disposed document's memory that failed once on .NET 11, with [#168], the next batch, whose object
   streams it decodes, since the job it failed runs on every pull request. Of the eight batch 18 filed, [#312] was paid
   in the batch at the maintainer's choice; [#310], the objects the reader hands out, which a caller can change, goes
   under M03, blocking its slice 5, which plans objects read from a file to be read-only; [#311], the `/Kids` arrays a
   deep path of wide nodes holds, under M23, which budgets what the library costs and hardens it. Of the five left under
   M02, four go with the batch whose code they share: [#314], what [#312]'s closing leaves, and [#315], a waiting
   stream's walk made again, with [#181], the work one document asks, whose nests the same walk parses again; [#313],
   the probe's verdicts that depend on what was read before, with [#163], whose cut of nested loads also makes a report
   depend on it; and [#309], the Arlington walk's queue, with [#173], whose walk it shares; [#317], a timing test's
   flake on .NET 11, goes with [#188], the next batch, since the job it fails runs on every pull request, as [#307] went
   with [#168]. Batch 19 filed seventeen, fifteen under M02, one under M03 and one under M23: [#331], the search for a
   classic table's trailer that a source serving short reads sends round one window without end, goes under M03, with
   [#52], where the source's contract is written; [#343], the clamp of a section probe's reach to an array's greatest
   length, which only white space past 2 GB tells apart, under M23, which budgets what the library costs and hardens it.
   Of the fifteen under M02, twelve go with the batch whose code they share: [#320], the rows a relocation
   candidate leaves when it is dropped, with [#189], the next batch, whose fix settles which of a number's definitions
   the index keeps; [#327], a header whose number or generation carries a sign, with [#171], whose fix reads the byte
   before a header's number in the same method; [#322], a table whose `trailer` keyword no dictionary follows, [#323], a
   first section that cannot be read reported only as a rebuild, and [#325], `xref.section-malformed` silent where the
   trailer is at fault too, with [#132], [#218] and [#229], whose rebuilds at opening, search for the catalog and rule
   over the same records decide them; [#326], counts grouped in some messages and not others, with [#162], whose filter
   reports it rewords; [#324], a classic table mid-chain whose bad row ends the chain, [#328], a `/Prev` in a trailer
   `MaxTrailerLength` cut, and [#329], the `/Prev` of a stream at which a value's cut ends the chain, with [#163], the
   reader at its guards; [#321], an `/XRefStm` copied two revisions late, [#330], a sound `/Prev` reported as a loop
   where a newer `/XRefStm` read its stream first, and [#332], blocked by [#330], a `/Prev` a few bytes off a stream a
   newer `/XRefStm` read, with [#107] and [#111], whose rules the maintainer names. The maintainer placed the other
   three on 2026-11-11, their bodies naming no batch: [#333], an older revision's stream taken near a hybrid table's
   `/Prev` before its own revision's table, in batch 31, with [#332], [#330] and [#321], the other side of the same
   search; [#336], the test of the seeded comparer that fails under coverage on Windows, and [#342], the tests bound by
   the stack or a stopwatch that fail on a loaded machine, in batch 32, with what remains of [#158], the tests' own
   debt. Batch 20 filed four, all under M02: [#348], what a rebuild's expansion keeps of a definition a later object
   stream replaces, and [#350], the scan's window rented at twice its size, with [#171], the next batch, which decides
   which object streams the expansion takes in and reads the scan's pieces at their edges; [#349], the reasons and cases
   the probe's check of where a rebuild serves a member leaves, with [#162], whose findings' texts it rewords; and [#351],
   a last definition that reads as nothing, which the maintainer filed on 2026-11-12, with [#163], the reader at its
   guards and cuts. Batch 21's first pull request filed seven, all under M02, which the maintainer places: [#353],
   headers the walk refuses that a load reads; [#354], encryption's edges at opening; [#355], a cross-reference stream's
   dictionary read in a fixed window; [#356], the expansion's order; [#357], a string a cut leaves open closing on an
   update; [#358], where a piece starts still changing what the scan takes; and [#359], an encrypted document's
   references into its object streams called missing after a rebuild.
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

The history of every session and pull request is [`journal.md`](journal.md), newest first. An entry is short, about
thirty lines; the detail is in the pull request's body ([ADR 54](adr/0054-review-and-documentation-effort-follow-what-protects-the-library.md)).

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
[#60]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/60
[#61]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/61
[#62]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/62
[#107]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/107
[#111]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/111
[#117]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/117
[#118]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/118
[#119]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/119
[#120]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/120
[#123]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/123
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
[#140]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/140
[#141]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/141
[#144]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/144
[#147]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/147
[#148]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/148
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
[#177]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/177
[#178]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/178
[#179]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/179
[#181]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/181
[#182]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/182
[#183]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/183
[#185]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/185
[#186]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/186
[#187]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/187
[#188]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/188
[#189]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/189
[#190]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/190
[#192]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/192
[#193]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/193
[#197]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/197
[#199]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/199
[#200]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/200
[#201]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/201
[#202]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/202
[#203]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/203
[#204]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/204
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
[#235]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/235
[#237]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/237
[#238]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/238
[#240]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/240
[#241]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/241
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
[#259]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/259
[#260]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/260
[#261]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/261
[#262]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/262
[#263]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/263
[#264]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/264
[#265]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/265
[#273]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/273
[#274]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/274
[#275]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/275
[#276]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/276
[#277]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/277
[#278]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/278
[#279]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/279
[#280]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/280
[#281]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/281
[#282]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/282
[#283]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/283
[#287]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/287
[#288]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/288
[#289]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/289
[#290]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/290
[#291]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/291
[#292]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/292
[#294]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/294
[#295]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/295
[#296]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/296
[#297]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/297
[#300]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/300
[#305]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/305
[#306]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/306
[#307]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/307
[#308]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/308
[#309]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/309
[#310]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/310
[#311]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/311
[#312]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/312
[#313]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/313
[#314]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/314
[#315]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/315
[#317]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/317
[#318]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/318
[#320]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/320
[#321]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/321
[#322]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/322
[#323]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/323
[#324]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/324
[#325]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/325
[#326]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/326
[#327]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/327
[#328]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/328
[#329]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/329
[#330]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/330
[#331]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/331
[#332]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/332
[#333]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/333
[#336]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/336
[#342]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/342
[#343]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/343
[#344]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/344
[#347]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/347
[#348]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/348
[#349]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/349
[#350]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/350
[#351]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/351
[#352]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/352
[#353]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/353
[#354]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/354
[#355]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/355
[#356]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/356
[#357]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/357
[#358]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/358
[#359]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/359
