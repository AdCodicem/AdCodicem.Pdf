# Project status

A living file, updated **at the end of every session**. It describes the real state, not intentions.
Keep it short: summarize the journal once it passes a dozen entries — the detailed history is in git, not
here. What is tracked item by item lives on GitHub since 2026-09-27: each milestone's slices and the known
debt are issues, filed under the [milestones](https://github.com/AdCodicem/AdCodicem.Pdf/milestones) the
Tracking workflow mirrors from `docs/roadmap.md` (*Debt and open points*, below).

## At a glance

- **Current milestone**: M02 — Document validation (`docs/milestones/M02.md`), in progress. Where it lives and
  what its public API is was settled by ADR 36 before the first type was written; slice 1 — the engine, the
  report and `file.eof-missing` — is done, merged with [#29](https://github.com/AdCodicem/AdCodicem.Pdf/pull/29) on
  2026-09-26 ([#57](https://github.com/AdCodicem/AdCodicem.Pdf/issues/57), closed). The three reader debts it
  waited on — T32, T25 and T27 — are fixed, merged with [#31](https://github.com/AdCodicem/AdCodicem.Pdf/pull/31).
  Slice 2 — the file and cross-reference rules, twenty of them — is done, merged with
  [#110](https://github.com/AdCodicem/AdCodicem.Pdf/pull/110) on 2026-09-28
  ([#58](https://github.com/AdCodicem/AdCodicem.Pdf/issues/58), closed); severities follow
  [ADR 45](adr/0045-a-findings-severity-says-whether-the-file-reads-as-written.md), accepted with it. The
  project's coverage and Codecov's rules ([#115]) merged with #116. Slice 3 — [#51], the object and page tree
  rules, the page in a finding's location, pages counted as qpdf's walk counts them, and the object-shape rules
  generated from the Arlington PDF Model (ADR 44, amended on 2026-09-28) — is done, merged with [#122] and [#124] on
  2026-09-29 ([#59](https://github.com/AdCodicem/AdCodicem.Pdf/issues/59), closed), and the rest of `src/` covered
  after it. What remains was settled with the maintainer on 2026-09-29 (*Next concrete step*): every issue filed under
  the milestone closes before it does — the reader debts [#55], [#56], [#117] to [#120], [#125] and [#126], the
  validation debts [#107], [#111], [#128], [#129] and [#132], and slices 4 to 6, [#60] to [#62], in that order —;
  [#123] moved to M20. Since 2026-09-29 every milestone ends with an adversarial review by a session that worked on
  none of it ([ADR 46](adr/0046-every-milestone-ends-with-an-adversarial-review.md), `docs/milestone-review.md`):
  M02's is its slice 7, [#137], after the threat model's first version ([#135]) and M01's review after the fact
  ([#136]).
- **Last milestone closed**: **M01 — Object model and tolerant reading**
- **Tests**: 2,279 unit (2 skipped by design) + 1,124 integration (skipped without Docker) + 23 for the remote
  corpus's fetcher + 43 for the roadmap's mirror on GitHub, on `claude/m02-docs-catch-up-e19dlq`. With the 233 remote
  documents fetched here: 3,927 unit, 3 skipped — the laziness test on the two documents recorded as unsupported
  until [#47], which every other test now holds to their expectations, and the private manifest this container
  lacks. The integration suite did not run here (no Docker); CI and `Remote corpus` run it.
- **Coverage**: on the committed corpus, as Codecov counts it (a line with an untaken branch is partial, the
  generated Arlington tables left out), 99.6 % of `src/` — 4,493 of 4,511 lines —, up from 98.9 % on `main`. The 18
  left are those the rule of 2026-09-29 leaves (`CLAUDE.md`, *Coverage*): members that are private, or of a private
  type, which no input reaches — nine in `ArlingtonWalk`, a defensive branch of the lexer —, a `?.` on an index
  never null where it is read, a switch's default arm, and a line the compiler puts after a call that never returns;
  the journal of 2026-09-29 lists them. `codecov.yml` asks 95 % of each patch, and lets the project drop by half a
  point at most; the aim is 100 %.
- **CI**: green on `main` at `d03a864` (CI run 352), and Release run 42 on it. `Remote corpus` run 12, on
  2026-09-29, on slice 3 with the flaky allocation test fixed (the commit `main` then took as `faef79c`), fetched all
  242 remote documents and passed its acceptance tests and referee checks: the two GitHub had refused a session
  here are confirmed.
- **Corpus**: 168 committed documents, 23.0 MB — 19 generated here, 3 from Word and PDF24 on Windows, 146
  third-party files under attribution-only licenses (`docs/corpus-sources.md`). Beside them, a **remote
  corpus** of 242 documents we may use but not redistribute, fetched at a pinned SHA-256 and size (ADR 32),
  88 of them out of their authors' archive (ADR 33), and tested every night by `Remote corpus`, last green in run
  12 on 2026-09-29. All 410 are described in
  `tests/corpus/manifest.json`.
- **Published**: [`AdCodicem.Pdf`](https://www.nuget.org/packages/AdCodicem.Pdf) `0.1.1-preview.10` to
  `0.1.1-preview.27`, previews from `main` through trusted publishing, 671 downloads on 2026-09-26. The
  `AdCodicem.*` prefix is reserved: nuget.org marks the package as verified.
- **A preview carries no guarantee** (ADR 30, 2026-09-26): an API no stable release has shipped may change or
  go with the next merge.
- **No stable release yet.** `v0.1.0` is a tag with no package behind it, by design, and the stable path has
  never run. When it does, it will fail at its push of the `chore(release)` commit to `main` until the
  ruleset lets GitHub Actions bypass it (T15); nothing is published when it fails.
- **The site**, <https://adcodicem.github.io/AdCodicem.Pdf/>, is versioned (ADR 31) and redeployed by every
  preview; before the first stable release the preview is the whole site.
- **Supply chain**: OpenSSF Scorecard **7.6** on `74ce382`. What remains is settings and people, not code:
  Code-Review 0 (nothing has ever been approved by a second person), Branch-Protection 5 (T15), Maintained 0
  (the repository is younger than 90 days), Contributors 3, CII-Best-Practices 0 (T18), Signed-Releases
  unscored until a release exists (T19).
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
| Indexing and walking the page tree | real ReportLab document, 1000 pages | — | 2.4 MB |
| Validating under the structural profile, the document already open and read | synthetic, 1000 pages | 2.5 ms | 588 KB |
| Typing and checking the objects the trailer reaches against the Arlington model, alone | synthetic, 1000 pages | 1.7 ms | 280 KB |
| Opening and validating under the structural profile | synthetic, 1000 pages | 6.2 ms | 2.6 MB |
| Decoding a whole Flate content stream | 4 MB decoded, about 330 KB encoded | 8.3 ms | 13.1 MB |
| Decoding the same stream without its checksum, or without its last five bytes | 4 MB decoded | 11.7 ms | 13.1 MB |

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
budget in CI (`CorpusReadingTests`), so an allocation regression fails the build. A stream that ran out is
read twice to tell a lost checksum from lost data (T32), which costs time on damaged streams only; the
second reading keeps nothing. What a 4 MB decode allocates is the output doubling towards its size, T28's
and M23's business.

## Next concrete step

M02 — document validation (`docs/milestones/M02.md`), slices 1 to 3 done. Its progress is the
[M02 milestone](https://github.com/AdCodicem/AdCodicem.Pdf/milestone/3) on GitHub, which closes when every issue
filed under it has; the maintainer settled on 2026-09-29 that each is paid in M02 rather than moved, [#123] aside.
One pull request per batch, each design question put to the maintainer after measuring, in this order:

1. The review of 2026-09-29 recorded, the specification brought up to date, the two [#47] documents held to every
   test but the laziness one, and the damaged trees' page counts held to qpdf: `claude/m02-docs-catch-up-e19dlq`.
2. [#55] and [#120], one path in `PdfObjectParser.ReadStream`: a stream's `/Length` checked past the parser's window,
   and the form of a `/Length` that could not be read said as it is. Measured on the whole corpus first; then the
   bound of the search for `endstream` (ADR 34) and what the reader records per stream, for slice 4, go to the
   maintainer.
3. [#56]: what a corrupt Flate stream decoded is kept, and a wrong checksum over whole data reported.
4. The reader and validation debts: [#117], [#118], [#119], [#125] then [#126], [#128], [#129], [#132]; then [#107]
   and [#111], each a new public rule whose name and severity the maintainer gives.
5. Slice 4 ([#60]) in two pull requests, streams then fonts, after the decisions it waits on: the severity of a font
   that is not embedded, the standard 14's aliases, where text is "meant to be extractable", how the rules that need
   content are left out and shown so, and the tools that referee both families.
6. Slice 5 ([#61]) in three: security and the trailer's `/ID`, annotations and destinations, metadata — with
   PDFDocEncoding ([#36]) and an XMP reader, or the `/Info`–XMP check moved, to settle first.
7. Slice 6 ([#62]): the report's JSON, its schema and documentation, the budgets restated.
8. The threat model's first version, `docs/threat-model.md`, for the reader and the validator ([#135]): a session
   that reads the code. It can come at any point before 9.
9. M01's review after the fact ([#136]): the maintainer opens a fresh session and types `/milestone-review M01`.
10. M02's review, slice 7 ([#137]), once every other issue filed under M02 is closed: a fresh session,
   `/milestone-review M02`; then whatever it files under M02.
11. The closing: a green `Remote corpus` run recorded here, M02.md's exit criteria ticked, the roadmap and
   `features.json` set to done.

A stream, object or section the reader cut at one of its limits (`limit.*`, ADR 34) is the reader's limit,
not a fault of the file: the rules on it report at most, as information, that it was not checked whole.

## Journal

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
  `xref.checked-in-part`. `docs/validation-rules.md` gives each its severity, meaning and reference, and what the
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
| T39 | [#55] |
| T40 | [#56] |

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
[#135]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/135
[#136]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/136
[#137]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/137
