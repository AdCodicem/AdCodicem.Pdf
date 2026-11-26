# Journal

What each session and pull request did and decided, newest first. It moved here from [`status.md`](status.md) on
2026-11-11 ([ADR 54](adr/0054-review-and-documentation-effort-follow-what-protects-the-library.md)), so that a
session's first reading is the project's state and not its history, and its entries before that day were summarized
to about thirty lines each; the unabridged text is the
[journal of `e38ebbc`](https://github.com/AdCodicem/AdCodicem.Pdf/blob/e38ebbc471b86779c3f6ac5b320ca1ecd531b2c1/docs/status.md#journal). A session reads an entry when it needs the
reason for a past decision. A new entry is short, about thirty lines; the detail is in the pull request's body.

## 2026-11-26 — Batch 38: the rebuild's scan — values a window cuts among placed headers, and a header's verdict ([#454])

- **The question.** Measured on `main` at `1d4f0a5`. A value a window cuts, with headers the indexes place inside it
  in comment lines or strings, regrew every load's window from 8 KB up past them: a table placing a header on each of
  1,000 lines of 70 spaces read 137 MB, 2,000 lines 487 MB (#387); 5,000 lines of `k 0 obj(`, opened and validated,
  2.8 s and 981 MB, 20,000 lines 47 s and 16.8 GB. [#389] did not reproduce: a header past a stream's first
  `endstream` is missing, not unindexed, the reads spent or not. A rebuild took `7 5 obj -3 0 obj` for a definition
  that holds no value (#438.1), asked a member's verdict again for each listing (#438.2), and the memo's scan lexed
  every header's (#438.3).
- **Settled with the maintainer** on 2026-11-26, each as recommended: an internal bound, not a guard (ADR 34),
  `MaxPlacedHeadersInValue` = 64; past it, the value's data ends at the first placed header, reported by
  `syntax.truncated-object`.
- **What changed.** A load counts the rows both indexes place inside a value it reads on, its own stepped over; past
  64 the data ends at the first. A header that more than 64 rows part is none at its place; a whole number naming
  another object is read no further. What a load learns of a cut header — its number, the largest window that cut it,
  the smallest that found none — is kept per reader and answers the loads after it as reading again would. A pair out
  of a header's range is no header (#438.1); a member's verdict is asked once (#438.2); a value is told from its first
  byte where it decides (#438.3). [#389] is held by a test row.
- **Review** (ADR 54): six rounds. Five blockers, all E1 on hand-made shapes, all fixed: one comment line holding a
  header's tokens (round 1), then the family of many rows at one long header, as on `main` (rounds 2 to 5). No major;
  ten minors, fixed or filed; twelve nits. The real-damage lens found no object changed over 24,000 to 27,000 variants
  a round; round 6 checked 15,366 answers of the memo against reading again, none differing.
- **Mutation** (ADR 53 amended): 63 mutants over the bound, the refusal, the memo and the member's verdict. 55 killed,
  by the suite and by tests the step added; 1 equivalent, confirmed by a second agent, which refuted another claim,
  then killed; 7 filed in [#449], [#452] and [#453].
- **The issues.** Closes [#387], [#389], [#438]. Filed under M02: [#449] to [#453], placed in batches 42, 43 and a
  new 46.
- **Checks.** The batch check at `115dc48`; 13,639 unit tests at `cbaed88`, with 235 of the 242 remote documents;
  the 69 timing and memory tests and the 623 of `HostileInputTests` and `WindowEdgeTests` alone; the corpus
  differential against `main` identical over 802 reads, the forced rebuilds included; the integration suite green in
  CI; `Remote corpus` run 144 green; the site. Patch coverage 98.10 %: two partial arms in `NoteNoHeader`, the memo's
  creation, which no input takes, and a smaller window's verdict, [#453]'s input. Benchmarks against
  `main`, alone, alternating: the memo's scan 6 to 9 % faster (median), 4 % on `percent-strings`; 100,000 listings
  of one member read 1.81 MB against 7.41. The shapes of #387 read linearly: 37.7 MB at 2,000 lines against 487;
  20,000 lines of `k 0 obj(`, 1.3 s and 188 MB.
- **For callers.** No public API change. A damaged value with more than 64 placed headers inside it reads as ending at
  the first; a header that many rows part is none at its place.

## 2026-11-25 — Dependabot's security updates grouped, and its bypass of the approval ([#439], [#440])

- **The question.** Dependabot pull requests queued for auto-merge never merged: the ruleset on `main` waits for a
  code-owner approval (#439). npm security updates under `/docs/website` arrived one pull request each, as
  version-update groups do not apply to security updates (#440).
- **Settled with the maintainer**, on 2026-11-25: Dependabot goes on the ruleset's bypass list rather than the
  approval being dropped for everyone; CodeQL stays required.
- **What changed.** Every entry of `.github/dependabot.yml` has a second group, `applies-to: security-updates`, for
  minor and patch updates; a major security update still arrives alone. Validated against the schema store's
  Dependabot schema. `docs/releasing.md`'s settings table names the bypass; the setting itself is the maintainer's to
  make, as no session can edit a ruleset.
- **Left open.** GitHub's documentation lists Dependabot among the actors a ruleset can let bypass, but says neither
  which actor a merge by auto-merge counts as (the workflow's token enabled it) nor whether a bypass skips the
  required checks too. #439 stays open until a Dependabot pull request is seen merging on green checks alone, and a
  red one not merging.

## 2026-11-25 — Dependabot auto-merge moved off `pull_request_target`

- **The question.** From 2026-12-02, GitHub Actions blocks `pull_request_target` by default in public repositories, and
  `dependabot-auto-merge.yml` ran on nothing else: it would no longer run at all.
- **What changed.** The workflow runs on `pull_request` (`opened`, `synchronize`, `reopened`). A run Dependabot
  triggers gets a read-only `GITHUB_TOKEN`. GitHub's *Automating Dependabot with GitHub Actions* page says a
  `permissions:` key raises it, and its own auto-merge example does exactly that, so the job keeps `contents: write`
  and `pull-requests: write`. Every guard stays: the author and the actor are `dependabot[bot]`, the head repository is
  this one, `fetch-metadata` is pinned by SHA, the patch-and-minor allow-list (#196) holds, the merge is
  `gh pr merge --auto --squash`, and no code is checked out. The header comment states the new threat model: the
  workflow file now comes from the pull request's head, and the guards hold the job to Dependabot's own branches of
  this repository. `docs/threat-model.md` says no workflow runs on `pull_request_target` any more.
- **Checks.** actionlint 1.7.12, the binary and digest CI's `workflows` job pins: clean. zizmor 1.33.1: the
  `dangerous-triggers` finding is gone. `bot-conditions` on the `github.actor` check was there before and stays, as
  the guard is kept on purpose beside the author check, which cannot be spoofed.
- **Found and left.** None of the twenty or so open Dependabot pull requests merges, even with green checks: the
  ruleset waits for a code-owner approval, and CodeQL's `neutral` may block too ([#439], the maintainer's to settle).
  npm security updates under `/docs/website` arrive one at a time ([#440]).

## 2026-11-25 — The thirty-three debts no batch held, placed in batches 32 to 45

- **The question.** After batch 27 merged, thirty-three debts filed under M02 held no batch: [#387] to [#389] and
  [#391] to [#393], which batch 33 filed; [#401] to [#403] and [#405], batch 26; [#410] to [#414] and [#416], [#420] to
  [#426], [#428] to [#435], [#437] and [#438], batch 27's four pull requests.
- **Settled with the maintainer**, on 2026-11-25, each as recommended: four join a batch whose code they share —
  [#426], the stack-cut test's flake, batch 32, the tests' own debt; [#392], the memo's kinds near a lost `endstream`,
  batch 34, the search near a row; [#388] and [#432], what the edges of the scan's pieces and a load's window take,
  batch 35. The twenty-nine others make eight new batches by code area, at most six issues each: 38, the rebuild's scan
  ([#387], [#389], [#438]); 39, a rebuild's indirect lengths and object streams ([#391], [#393], [#405]); 40, object
  stream headers and stream values a cut object holds ([#411], [#424], [#425], [#416]); 41, the nesting guards under
  `ThrowOnLimit` and the validator's walk ([#412], [#413], [#410], [#414]); 42, the parser at a guard's edge ([#420] to
  [#423]); 43, the chain and the loads at guards ([#428] to [#431], [#435]); 44, faulted tables and the rows the chain
  does not take ([#433], [#434], [#437]); 45, filter reports ([#401] to [#403]).
- **The order.** Batch 38 opens next, before batch 28: [#387] is a denial of service, a rebuild or a validation in
  time the square of the lines, and such debts have always come first. Batches 39 to 45 follow batch 37.
- **Where it is written.** `docs/status.md`, *Next concrete step*, step 4's list and its placements; no issue changes.
- **The rule from now on**, the maintainer's of 2026-11-25: a debt filed under M02 is placed in a batch when it is
  filed — an existing one whose code it shares, within six issues, or a new one —, recorded in step 4's list and named
  in its body (`CLAUDE.md`, *Tracking*; the `batch` skill's closing step).

## 2026-11-25 — Batch 27, fourth pull request: a hidden end proven by the rows not taken, and a rebuild's readable definition ([#436])

- **The question.** Measured on `main` at `98db003`. A stream whose `/Length` a guard hid was proven by the object the
  merged index places after it, so that after an update redefining that object, the older definition sitting right
  there proved nothing: under `MaxObjectLength` 1024, the stream read as cut (#287). Once the searches for `endstream`
  had read their budget, a deferred length counting the end-of-line before the chain's `endstream` was reported past
  it (#293: 2,296,059 bytes searched against 2,103,660). Under a hidden end, Flate decoded the data three times where
  a run of white space followed, raw deflate included (#290, point 2). A rebuild kept a last definition that holds no
  value — `7 5 obj` then `)`, `trailer` or the end of the file — over the readable one before it (#351).
- **Settled with the maintainer**, on 2026-11-25, each as recommended: a definition met again that holds no value
  leaves the one before it kept, a value read in part still winning, and `object.redefined` says so (#351); R12-2
  kept, a section lacking rows beside a cut value read as read whole whatever the value holds, written in ADR 34
  (#290, point 1). Settled by the measurement: the chain's `endstream` is read where it stopped, without a search
  (#293); every in-file row the chain reads and does not take is kept, bounded by `MaxIndexEntries` apart from the
  index (#287).
- **What changed.** The rebuild's scan judges a number defined again by the 64 bytes past its `obj` (`NoValueReach`,
  ADR 34), its memo noting the verdict per header; an object stream member replaces a direct definition with no value
  after it. The chain notes the places of the rows it supersedes or refuses, given back when a table is read again or
  a place a search tried is dropped; a proof counts them where a header starts there. `EndStreamAfter` reads the
  keyword past a lone LF. `ChecksumDisagrees` runs only on a checksum mismatch, with a benchmark.
- **Review** (ADR 54): two rounds. Round 1, four lenses — the rebuild's scan, the chain's proofs, texts and tests,
  real damage over about 32,300 variants a side: no blocker; two majors, confirmed by refuters and fixed — an update
  cut after the header of a number an object stream held served null, and a classic table read again in a larger
  window noted its rows twice, a false `limit.index-entries` under a lowered bound —; three minors fixed; about twelve
  nits. Round 2, two lenses over the fixes: no blocker, no major — two minors and six nits; its behavior fixes, made
  before the loop stopped, taken back and filed, its tests kept.
- **Mutation** (ADR 53 amended): 69 mutants. 40 killed by the suite, 21 by new tests; 5 equivalent, confirmed by a
  second agent, which refuted two more claims, then killed; 3 filed in [#437] (the give-backs, the sorting while a
  place is read). The final pass's speed fix had ten more: 6 killed, 1 of them by a new row, 4 equivalent, confirmed.
- **The issues.** Closes [#287], [#290], [#293], [#351]. Filed under M02: [#437], [#438].
- **Checks.** The batch check at `91c3fde`, 10,703 unit tests; the 69 timing and memory tests, the 359 of
  `HostileInputTests` and the 252 of `FilterTests` alone; the corpus differential against `main` identical; `Remote
  corpus` run 142 green at `91c3fde`. Benchmarks against `main`, alone, twice in opposite orders: Flate under a hidden
  end 23 to 53 % faster on raw deflate and a short checksum, allocating a third less; opening an index within the
  noise, `Update` allocating 0.5 MB more for the 30,000 rows it supersedes. They caught the scan for the memo 16 to 45
  % slower on files of 100,000 small objects, a rebuild redefining one number 200,000 times 36 to 51 %: the final pass
  lexes the verdict from past `obj` (11 to 20 %, and 3 to 15 %, left in [#438]). Patch coverage 97.95 %, three arms no
  input takes.
- **For callers.** No public API change. A stream after a redefined object is proven by the definition the file still
  holds; a rebuild serves the readable definition before one that holds no value, and says so; `limit.index-entries`
  also bounds the rows the chain does not take.

## 2026-11-25 — Batch 27, third pull request: the chain at its guards ([#427])

- **The question.** Measured on `main` at `ba64b1f`. White space past `MaxTrailerLength` before a section `startxref` or
  a `/Prev` names was charged to the file: `file.startxref-wrong` or `xref.section-not-found`, Errors, and the
  section searched for nearby (#289; #295's two files had been settled by batch 19). A `/Prev` written whole before a
  trailer guard's cut, or in a stream whose value a guard cut, was neither judged nor followed (#328, #329). The chain's
  own load of a `/Prev` whose header `MaxObjectLength` cut searched near the entry and rebuilt the index (#292). A table
  mid-chain with a bad row, header or stray token ended the chain: older sections unread, everything they index rebuilt
  (#324).
- **Settled with the maintainer**, on 2026-11-24, each as recommended: that white space makes the guard's section, as
  does a header cut inside its tokens (ADR 34 amended), and white space before an object's header keeps its behavior,
  filed ([#428]); the chain's own loads stop at the guard, a later read following the load rule; a faulted table named
  by a `/Prev` is read on to its own trailer and its `/Prev` handed on, every number below its `/Size` refused to older
  rows. Settled by the measurement: a `/Prev` whole before a cut is judged, not followed, as #264 judges one past a rows
  guard; a whole `/XRefStm` before a trailer cut over rows read whole is still followed.
- **What changed.** A classic table's windows open at its keyword. A stream whose header is whole but whose value a
  trailer guard cut before it said what it is makes the guard's section (`PdfObjectParser.ValueKindCut`). Faulted tables
  read on from their first fault, the search for their trailer in growing pieces, ending at a `startxref` or `xref`;
  a `/Size` the rows read contradict, or too many lost ranges, refuse every number. The chain's stop is marked apart
  (`_cutAtStop`) and dropped by the next load. `TrailerValueWrongRule` judges a table's `/Prev`.
- **Review** (ADR 54): two rounds. Round 1, four lenses — loading and faulted tables, the chain at guards, texts and
  tests, real damage over 168 corpus documents in about 128,000 runs: one blocker, a stream's value cut before it starts
  taken for a shifted section and an older revision served under the default limits, the same on `main`, confirmed by a
  refuter and fixed; three minors fixed (the refusal against `/Size`, the read-on's start and its search crossing into a
  newer table's trailer, seen on a PDFMaker file damaged by one byte); about thirteen nits. Round 2, three lenses over
  the fixes: no blocker, no major — one minor and four nits, fixed in the final pass or filed.
- **Mutation** (ADR 53 amended): 162 mutants over 21 defenses. 148 killed by the suite, two of them by hanging the
  search past a table's rows; 7 by new tests — where the read-on starts when the misreading runs over the trailer
  keyword, what a `/Size` of 0 or 1 refuses, the chain's stop past a rebuild it set off and a stopped object a rebuild
  finds in an object stream —; 7 equivalent, confirmed by a second agent, which refuted two more claims.
- **The issues.** Closes [#289], [#295], [#328], [#329], [#292], [#324]. Filed under M02: [#428] to [#435].
- **Checks.** The batch check at `8e75b8e`, 10,627 unit tests; the 69 timing and memory tests and the 359 of
  `HostileInputTests` alone; the corpus differential against `main` identical; `Remote corpus` run 138 green on its
  first attempt; benchmarks against `main`, alone, twice in opposite orders: allocation identical but for a field of
  the reader, time within the noise — a few cases faster or slower by up to 7 % in both orders, as the probe, whose
  code is untouched, is 4 to 8 % slower. Patch coverage 98.64 %, three partial lines no input takes.
- **For callers.** No public API change. Under a trailer guard, a section the guard hid is reported as the guard's
  (`xref.checked-in-part`) instead of an Error of the file, and a `/Prev` written whole before the cut is judged; a
  faulted table mid-chain no longer costs a rebuild of what older sections index.

## 2026-11-24 — Batch 27, second pull request: the parser at its guards ([#419])

- **The question.** Measured on `main` at `5780810`. At the largest window `MaxObjectLength` or `MaxTrailerLength`
  allows, the parser kept what the window's edge parted as if the file had written it: a reference cut after its number
  read as integers (#262: `/Kids [3 0 R 4 0 R]` cut at 49 bytes read `[3 0 R, 4, 0]`); a name, a real or an integer cut
  mid-way read shorter (#279: `/Fl`, `1` of `1.5`); an object stream member whose closed string or name ended at the
  bound was taken for cut, `limit.object` reported and thrown under `ThrowOnLimit` (#282); `streamX` cut after `stream`
  read as a stream keyword, its data `X…` decoded and failing (#294).
- **Settled with the maintainer**, on 2026-11-24: at a guard's cut, keep no token the edge parts, nor the one or two
  integers a reference it cuts may start; an integer at the edge stays cut, even followed by white space, since it may
  start `n 0 R` (#282 amended); a string the edge leaves open is a separate debt ([#420]); [#415] closed here, its rule
  written where `/N` and `/First` are judged.
- **What changed.** The parser is told when its window is a guard's (`guardEdge`): there, a name, a real or a keyword
  that reaches the edge is asked of the file, or of the decoded data for a member, as `endobj` and `R` already were, and
  dropped where a regular byte follows; an integer whose look-ahead for `G R` meets the edge is dropped with its
  generation. An array leaves a dropped value out, a dictionary the key it was for, a value alone reads as null.
  `stream` is a stream keyword only whole, at any window's edge. The validator's member reads ask the same edge. A
  `/Type`, `/XRefStm` or `/Root` the cut parts is no longer read as another value: the guard's report stands for it.
- **Review** (ADR 54): one round, four lenses — real damage over 168 corpus documents in 9,692 guard variants, parser,
  reader, texts and tests. No blocker, no major. One minor, the delimiters an edge leaves undecided, filed with the open
  strings ([#420]); eleven nits: eight texts and tests fixed in the final pass, three filed ([#421], [#423]). The final
  pass also held [#415]'s rule by a test per key and shape, and the validator's member edge.
- **Mutation** (ADR 53 amended): 68 mutants over the drop, the resets, the stream gates, `EndsAtGuardEdge`, the edge and
  the call sites. 57 killed by the suite, 8 by new tests — nested containers, a parser parsing twice, a growable
  window's edge, the chain's check-only read, a stray delimiter ending a member or a trailer at its guard ([#420]
  widened) —, 3 equivalent, confirmed by a second agent; the final pass's 4 on [#415]'s conditions killed by its theory.
- **The issues.** Closes [#262], [#279], [#282], [#294], [#415]; settles [#328]'s `/XRefStm` half. Filed under M02:
  [#420] to [#426].
- **Checks.** The batch check at `69c11ec`, 10,410 unit tests; the 69 timing and memory tests alone; the corpus
  differential identical; `Remote corpus` run 137 green on its second attempt — its first ran short of stack in a test
  of nesting, filed ([#426]): this branch takes 3.5 % more stack per nested dictionary at tier 0. Benchmarks against
  `main`, alone, twice in opposite orders: the parser within a few percent, allocation identical. Patch coverage 100 %.
- **For callers.** No public API change. Under a guard's cut, fewer values are read than before, none of them one the
  file did not write; a member whose string, name, real or keyword ends at `MaxObjectLength` is read whole and not cut.

## 2026-11-24 — Batch 27, first pull request: the nesting guards, and what a read keeps by its depth ([#409])

- **The question.** Measured on `main` at `ad21642`, twice: a container nested past 128 levels, or a load nested past 64,
  read as null under a constant, reported `syntax.depth-exceeded`, no guard; what a read the nesting cut gave was kept and
  served at any depth, so that an object read otherwise after another (#163: of 64 streams each taking its `/Filter` from
  the next, 5 read null after 3, `"five"` fresh); under `ThrowOnLimit`, 25, 50 and 100 candidates through a 4 MB stream
  over a bomb read 222, 437 and 866 MB ([#397]); a header value a guard cut was the file's fault ([#395]); the probe's
  header reads depended on what was read before through streams on a loop ([#313]).
- **Settled with the maintainer.** On 2026-11-23: the batch's nineteen issues in four pull requests by area; the guards
  `MaxNestingDepth` (128) and `MaxNestedLoads` (64), defaults unchanged, `syntax.depth-exceeded` withdrawn; then, once
  keeping cut reads by depth proved either memory-hungry or exponential under a small cache, a bound per read. On
  2026-11-24: the store's mutants held by its own tests, ten intricate survivors filed rather than tested, a departure
  from ADR 53's letter.
- **What changed.** The two guards. What a read keeps — an object, an object stream's contents, a kept exception —
  carries its depth and serves only reads as deep, or, cut, from the same base; cut results are kept by base in
  `BoundedStore`, objects one each once the read ends; within one read, an object loaded again from where the cache let it
  go reads as null. A guard's exception is kept in exception filters: no stack overflow under `ThrowOnLimit` (main's at
  50 nested decodings on 1.5 MB too). [#395]'s and [#313]'s header reads; the walk parses again what the reader does.
- **Review** (ADR 54): two rounds. Round 1, four lenses: three blockers — exponential re-reads within a read, a stack
  overflow under `ThrowOnLimit`, a trailer's nesting cut ending the chain — and a slow leak; round 2, three lenses: no
  blocker or major, nits fixed or filed. CI on .NET 11 then showed the walk 4 % past its timing bound: four million table
  writes a validation, deferred into a run of integers.
- **Mutation** (ADR 53 amended): 420 mutants. Parser and validator 135: 92 killed by the suite, 27 by new tests, 16
  equivalent. Reader 277: 171 by the suite (6 by time-out), 48 by new tests, 17 more by the store's tests, 31
  equivalent, 10 filed ([#414], [#415]). The walk's notes 8: 7 killed, 1 equivalent. The second opinion confirmed 41 of
  the 48 equivalents and held the 7 others by tests.
- **The issues.** Closes [#163], [#313], [#395], [#396], [#397]. Filed under M02: [#410] to [#416].
- **Checks.** The batch check at `ee4927c`, 10,256 unit tests; the 69 timing and memory tests alone; the corpus
  differential identical; `Remote corpus` run 135 green. Benchmarks against `main`, alone: time within noise, 7 % more
  allocated reading every page, each cached object carrying its depth. Patch coverage 99.75 % by Codecov's rule: one
  rethrow in a filter that catches nothing.
- **For callers.** New: `PdfReaderLimits.MaxNestingDepth`, `MaxNestedLoads`, codes `limit.nesting-depth` and
  `limit.nested-loads`; withdrawn: `PdfDiagnosticCodes.SyntaxDepthExceeded`. An object reads the same whatever was read
  before; past the nesting, a stream is decoded about once for each depth a read reaches it from.

## 2026-11-22 — Batch 26, second pull request: counts in plain digits, and why a rebuild loses an object ([#407])

- **The question.** Measured on `main` at `2e07dca`, twice: a message grouped the digits of some counts and not of
  others, one message both, and an object stream's header fault was written twice, by the reader and by the validator,
  in other words and other digits ([#326]). The check of objects the chain places soundly in an object stream a rebuild
  serves otherwise said only that the rebuilt index lacked one, or nothing, where the definition it serves did not list
  it, could not be read or was no object stream, and compared its served definition with the row, so that a row a few
  bytes early made every sound member a candidate ([#349]).
- **Settled with the maintainer** (2026-11-22, each as recommended): no count grouped, a whole-megabyte or -kilobyte
  bound keeping its unit; the reader's predicates the single text of a header fault, which the finding quotes; each
  reason a rebuild loses an object given; after the mutation step, [#406], this pull request's own regression, fixed
  here. Settled by the measurement: `xref.entry-broken`, Error, no new rule; the served definition told by where a load
  found its header.
- **What changed.** Every count of a message is written in plain digits. `ObjectStreamHeader` writes a header's fault
  once. The probe judges each candidate once per served stream: lacking it, it says why — the definition does not list
  it, can be read only in part or not at all, is no object stream, is itself a member, or is not held —; serving it
  elsewhere, where it is written as a stream or not listed; a stream a reader limit left out is skipped.
- **Review** (ADR 54): one round, three lenses — the wording, the probe's paths, real damage (the corpus at default and
  lowered limits, under `ThrowOnLimit`, about 18,000 damaged variants a worktree). No blocker or major; one minor, E4 on
  real files (the bound-in-units rule worded wider than the code), and six nits, one this pull request's own regression
  against `main` (a full index read as a loss), fixed in the final pass with the texts; one nit, an extra header read
  for a signed header, left as cost only.
- **Mutation** (ADR 53 amended): 132 mutants over 53 of 161 changed code lines; 101 killed by the suite, 1 stillborn;
  tests kill 21 of the 30 survivors. The second opinion overturned 4 of 12 equivalents: 3 now held by tests, the fourth
  told apart only by a false finding — an object the reader serves said lost where the rebuild keeps the chain's own row
  of its stream outside the file, a regression against `main` filed as [#406], which the maintainer chose to fix here;
  confirmed equivalent again on the fixed code. 9 equivalents stand.
- **The issues.** [#326], [#349], [#406]. Filed under M02: [#405], a rebuild that takes an object stream's members in
  through a value of its dictionary that a stream it expands later replaces, which the second opinion found. Considered
  and not filed: a member its stream does not list read as null with no report of the reader, in every case alike.
- **Checks.** The batch check at `5fb2ae2`, 12,997 unit tests; the 69 timing and memory tests alone; `Remote corpus` run
  133 green. The corpus differential moves the counts, the header faults and [#349]'s reasons only. No benchmark: no hot
  path changed. Patch coverage 96.40 % by Codecov's rule: five lines, defensive arms no input takes.
- **For callers.** No new code, rule or severity. Messages read their counts in plain digits; a lost object's finding
  says why.

## 2026-11-22 — Batch 26, first pull request: predictors, abbreviated filter names, and filter reports of cut data ([#404])

- **The question.** Measured on `main` at `2e07dca`, twice: a TIFF predictor at 1, 2, 4 or 16 bits a component left its
  data predicted, a `/Colors` past 32 was clamped, and a PNG pixel's bytes rounded down, so `/Colors 3
  /BitsPerComponent 4` decoded otherwise than qpdf, MuPDF and PDFium — all in silence, as was every parameter value the
  reader replaced ([#162]). A filter named by an inline image's abbreviation (`/Fl`, `/DCT`, …) stopped decoding; a
  cross-reference stream so named rebuilt the file ([#250]). A filter report on data a guard ended quoted the cut as the
  data's length, so a deferred stream's fault was held twice ([#283]); a deferred cross-reference stream kept the
  carriage return its length leaves out, completing a checksum or a row with it ([#288]).
- **Settled with the maintainer** (2026-11-22, each as recommended): the batch in two pull requests by area, decoding
  here, the messages and the probe ([#326], [#349]) next; a new `filter.parameters-invalid`, Warning, once for each value
  replaced; an invalid `/Predictor` split as PDFium splits it; a crypt filter other than `/Identity` in a document that
  gives no `/Encrypt` passed along, reported; abbreviations read, under a new `filter.name-abbreviated`, Repair; a
  report on cut data with no length, counting "the part of its data read"; the carriage return dropped on both paths.
  [#395] to [#397] placed in batch 27.
- **What changed.** Predictors are undone at every width, in place, the row length weighed in 64 bits before any
  allocation; a partial last row is kept and undone as far as it goes. Abbreviations are read by decoding, the
  cross-reference stream check, `HasImageFilter` and the validator alike. Filter reports of cut data read the same
  whatever the cut, so the document holds them once. A deferred stream's data is taken again, or decoded again, without
  the carriage return, its first reports withdrawn.
- **Review** (ADR 54): one round, three lenses — predictors against independent references (25,000 random cases), the
  reader's paths, real damage over the corpus and about 10,000 damaged variants. No blocker or major; one minor, E4 on
  real files (an unknown PNG filter type counted against the rows of the part read), and seven nits. The final pass
  fixed the texts; the behavior left is filed.
- **Mutation** (ADR 53 amended): 313 mutants over 112 of 278 changed code lines; 251 killed by the suite, 1 hung; tests
  kill 29 of the 61 survivors. The second opinion overturned 3 of 32 equivalents, told apart by the reads a caller's own
  source counts, now held by tests; 29 stay. No debt.
- **The issues.** [#162], [#250], [#283], [#288]. Filed under M02: [#401], two filter reports that still depend on how
  much was decoded; [#402], three edges of the deferred stream's retake; [#403], an object stream whose filters decode
  nothing reported as a header fault.
- **Checks.** The batch check at `9950958`, 12,959 unit tests; the 69 timing and memory tests alone; `Remote corpus`
  run 131 green. The corpus differential is identical at default limits. Benchmarks against `main`, alone and twice:
  the same allocations, times within the runs' spread. Patch coverage 99.57 % by Codecov's rule: one line, a defensive
  check no input takes.
- **For callers.** Two new codes. Data that decoded wrongly or stayed predicted now decodes as other readers decode it;
  abbreviated filters decode; a filter report on cut data no longer depends on where the cut fell.

## 2026-11-21 — Batch 25, second pull request: what the validator's walk of object streams follows again ([#400])

- **The question.** Measured on `main` at `b8f6f7d`: each walk of a waiting object stream — one whose standing waits on
  streams not known yet — followed every value again from the start: ten streams of ten holders the cache let go took
  1.35 GB and 4.4 s, thirty-one of thirty-two 13.9 GB and 44 s ([#315]). The walk kept its nests by the stream heading
  them, so a nest met from another head was parsed again, up to 14.5 to 29.4 MB a stream ([#181], its walk's part).
  [#314] named four classes of objects the walk leaves to the reader.
- **Settled with the maintainer** (2026-11-20 and 21, each as recommended): [#314] closed by its documentation, its four
  classes pinned by their tests; links kept for `ObjectCacheCapacity × 62` streams, about 20 MB at the default cache;
  flattened nests let go first in, first out. After round 1, a stream's parameters in a store of their own,
  `ObjectCacheCapacity` entries of numbers and references only; past it, a stream is read again as on `main`.
- **What changed.** The walk keeps, per stream, the first object its `/Length` names and where its chain leads; a
  flattened run per nest; the parameters decoding reads; and, across the walks of one waiting stream, each value's
  outcome, so that a walk made again follows only the values that read as null an object of a stream not known yet.
  Each store is a cache (ADR 34): past it, the walk follows the file again, to the same verdict. Findings, their order
  and messages are `main`'s on every input tried; the reader's diagnostics during and after a validation can differ on
  hand-made files, where the walk no longer loads through the reader what it kept.
- **Review** (ADR 54): two rounds. Round 1 found one blocker, E1 on a hand-made file and a regression — a link kept its
  stream's parameters whole, 113 MiB live at a cache of 64 against 4.7 on `main` — fixed by the store above; round 2
  only nits. Differentials over 41,000 runs of generated files with every parameter type and rebuilt indexes, 403
  corpus documents and 7,177 damaged variants under a dozen settings: no finding differs.
- **Mutation** (ADR 53 amended): 267 mutants over 148 of 245 changed code lines; 169 killed by the suite, none hung;
  tests kill 89 of the 98 survivors. The second opinion overturned 3 of 12 equivalents — one a finding on a hand-made
  shape, the same on `main`, now held by a test —; 9 stay. No debt.
- **The issues.** [#181], [#314], [#315]. Filed under M23: [#399], the walk about a fifth slower over a thousand streams
  in nests, a nit on a hand-made shape.
- **Checks.** The batch check at `2f8cc89`; the 69 timing and memory tests alone; CI green, integration, the site and the .NET 11
  island included; `Remote corpus` run 130 green on the code.
  Benchmarks against `main`, alone, alternated: a thousand streams hiding their holders behind their `/Length` walked in
  56 to 59 ms and 17.8 MB against 4.62 to 4.78 s and 1.50 GB; nests at a thousand streams 326 to 381 ms against 288 to
  328 ([#399]), a megabyte more; the rest and validation within the runs' spread. Patch coverage 98.64 % by Codecov's
  rule: three lines of `Describe`'s defensive check of the index, which no input takes.
- **For callers.** No API change. Validating a file whose object streams depend on each other no longer walks again
  what it met; reader diagnostics read during a validation of a hand-made file may differ, its findings do not.

## 2026-11-21 — Batch 25, first pull request: what the library's walks decode again, and overlapping stream data ([#398])

- **The question.** Measured on `main` at `b8f6f7d`: the library's walks decoded again, for each member they read, the
  object streams the reader had let go — a hundred rows 99 decodings, 6 s and 9.9 GB; a valid file of 200 pages
  alternating between two 20 MB object streams 200 decodings in its page tree's walk ([#181]). Under `ThrowOnLimit` a
  broken `/Root`'s search read a bomb again for each candidate. Two hundred streams whose data share a stretch read it
  199 times from a file ([#280]); a wrong `/Length` read 64 bytes it held ([#296]); the pending note of dropped codes
  grew with nested loads ([#385]).
- **Settled with the maintainer** (2026-11-20, each as recommended): two pull requests by area, the reader's first; a
  guard scoped to the library's walks, `MaxObjectStreamDecodePasses`, 4, `limit.object-stream-decodes`; [#280] counted
  lazily; [#181]'s grown windows moved to [#387]; [#314] closed by its documentation, in the second. After round 1, the
  catalog's candidates the guard cut read again grouped by stream; after round 2, one total of the lengths taken unread.
- **What changed.** Within a walk, past four times what the first decodings gave, a stream is not decoded again and its
  members read null for that walk; a caller's own reads are never cut. The orphaned-page rule reads grouped by stream.
  Under `ThrowOnLimit` a load's and a decoding's exception are kept as their values are and thrown again. Every length
  taken unread counts once per data start and number into one total: past four times the file, data ends at the first of
  64 placed starts inside it, within a second allowance, or reads empty, reported. What follows a found `endstream` is
  read from the bytes held; a mark gives back its note's window. No corpus document changes.
- **Review** (ADR 54): five rounds: 4, 3, 3, 1 and no blocker, each E1 on a hand-made file; three were regressions
  against `main` — a walk reading an object again for each candidate, through a decoding that gave nothing, a load that
  met the cut, the guard's own exception — each now kept for the walk. Three minors, 29 nits. Real damage, up to 180,000
  reads a round, the count and the guards forced low: nothing but the guards' documented effects.
- **Mutation** (ADR 53 amended): 439 mutants over 249 of 525 changed lines; 260 killed by the suite, none hung; tests
  kill 128 of the 177 survivors, three through a 2 GB source generated as it is read. The second opinion overturned 8 of
  57 equivalents, a third agent one more; 49 stay. No debt.
- **The issues.** [#280], [#296], [#385]; [#181] advanced. Filed under M02: [#395], [#396], [#397] — the last E1 by the
  letter on a hand-made file, as on `main` and slower there.
- **Checks.** The batch check at `d95e0f0`; the 61 timing and memory tests alone; CI green, integration and the site
  included; `Remote corpus` run 128 green on the code. Benchmarks against `main`, alone, alternated: a wrong `/Length`
  read from a file about 7 % faster at 4 KB, allocations equal; the rest within the runs' spread, opening about 200
  bytes more. Patch coverage 98.21 % by Codecov's rule, eight partial lines no input takes.
- **For callers.** `PdfReaderLimits.MaxObjectStreamDecodePasses` and `limit.object-stream-decodes`; overlapping data
  past the bound reads cut or empty, reported `stream.length-invalid`; under `ThrowOnLimit` a load or decoding that
  reached a guard throws its first exception again. Validating first reports in another order, the same set.

## 2026-11-19 — Batch 33, second pull request: the indirect lengths a rebuild's scan confirms, and its memo's kinds ([#394])

- **The question.** Measured on `main` at `bcb3653`: after a reported guard sent a valid file into a rebuild, the scan
  read data whose indirect `/Length` it could not confirm up to its first `endstream`, taking an attached file's headers
  and a trailer in a comment: wrong objects served ([#363]). The memo's kinds held Clean what a comment or string hides
  past a lost `endstream` ([#365]) and an attached file's objects ([#366]), and Raw what follows where the scan could
  not learn a load's end ([#372]).
- **Settled with the maintainer** (2026-11-17 to 19, each as recommended): confirm an indirect length from its object,
  the search's bounds internal constants with a written reason; after round 1, its budget internal and silent,
  `MaxRebuildReadPasses` back to direct lengths, only the first header past the data deciding unless it is an attached
  file; a lone hidden header near a shifted row now an Error where [#360] pinned Clean; the later residuals filed.
- **What changed.** The object is one of the last eight values since the last trailer, the first holding an integer
  deciding, else the first header past the first `endstream` (for an attached file, the first of its number within a
  mebibyte); the search stops at a revision's `%%EOF`, under four times the file. The memo sweeps for what a comment or
  string hides, holds an attached file's places Raw, and ends a load's data at a line-start `xref` or `trailer`. No
  corpus document changes, by default or after a forced rebuild.
- **Review** (ADR 54): three rounds. Round 1: 1 blocker (an older revision's length object), 2 majors (Ghostscript's
  object streams reaching the guard; length objects before their streams spending it), 3 minors, 8 nits. Round 2: 1
  blocker (an attachment's search crossing a revision), 3 minors, 7 nits. Round 3: 1 blocker by the letter, equal to
  `main`, folded into an accepted residual, 5 minors, 8 nits. Real damage, up to 180,000 reads a round: no change.
- **Mutation** (ADR 53 amended): 314 mutants over 165 of 354 changed lines; 198 killed by the suite, 6 hung; tests kill
  the hangs and 85 survivors. The second opinion overturned 10 of 32 equivalents and found an empty read at a file's end
  that a strict source refuses (#167), fixed; 25 equivalents stay.
- **The issues.** [#363], [#365], [#366], [#372]. Filed under M02: [#391], [#392], [#393].
- **Checks.** 12,589 unit tests at `797ca09`, 12,586 passed and 3 skipped; the 60 timing and memory tests alone; CI
  green, Codecov's patch whole; `Remote corpus` run 125 green; the corpus differential: no object or finding changes.
  Benchmarks against `main`, alone, within noise once one statement's temporaries, which had taken the scan's loop past
  the JIT's threshold of tracked locals and cost 4 MB of strings holding a `%` a seventh, were written out ([#374]).
- **For callers.** A rebuild serves a valid file's own objects where its streams' lengths are indirect.
  `MaxRebuildReadPasses` counts direct lengths only; `xref.entry-unconfirmed` and `xref.checked-in-part` are reworded.

## 2026-11-18 — Batch 33, first pull request: a load's look past a whole value, and the rebuild's read bound ([#386])

- **The question.** Measured on `main` at `445d2e5`: a file of comment lines holding headers the rebuild's scan takes
  opened in time in the square of its lines, 0.55 s at 2,500 and 6 s at 10,000 ([#362]). The scan was linear; the
  expansion's loads were not, each one's look at the token past its value crossing every line after it, its window
  regrown eight times over. Past four times the file's length of confirming reads, a valid file's stream data was read as
  the bytes give it, a wrong object possibly served, and nothing reported the bound ([#361]).
- **Settled with the maintainer** (2026-11-17, each as recommended): two pull requests by area, [#362] and [#361] first;
  the bound made a guard, `PdfReaderLimits.MaxRebuildReadPasses`, 4, reported as `limit.rebuild-reads`. On 2026-11-18,
  after round 3: the simple cap, no value ever cut.
- **What changed.** A load keeps a value read whole as read, its window not regrown, where only what follows it reached
  the edge and the first row past it, of either index, lexes forward to a header that starts at the row. Past the guard,
  the scan reads each stream it cannot confirm as intact up to its first `endstream` and reports once; the index stops
  short only where that data hides a header; the memo's kind reads have their own internal bound. No corpus document
  changes, by default or after a forced rebuild.
- **Review** (ADR 54): four rounds. Rounds 1 to 3 found 3, 2 and 2 blockers, each widening of a rule that ended an
  object's data at a placed header bringing new ones on valid files — a string or a content stream cut after a forced
  rebuild, a window grown to the guard. The maintainer chose the round-1 cap with its own gaps paid, values cut inside
  them filed: [#387]. Round 4: no blocker or major, one minor, two nits; the minor and a residual whose fixes change
  behavior are [#388] and [#389]. Real damage, about 21,000 reads a round: no change; the cap fired on no real file.
- **Mutation** (ADR 53 amended): 302 mutants over 89 of the 242 changed lines; 180 killed by the suite, 9 hung. Tests
  kill the hangs and 68 survivors, 4 of them written when the second opinion overturned 7 of 49 equivalents; 42 stay
  equivalent; one differs only through [#388]'s defect, two only on a source near 2^63 bytes: [#390], under M23.
- **The issues.** [#362], [#361]. Filed: [#387], [#388] and [#389] under M02, [#390] under M23.
- **Checks.** 12,363 unit tests at `c935daf`, 12,360 passed and 3 skipped, with 235 of the 242 remote documents; the
  timing and memory tests alone; CI green with the integration suite and the site; 99.53 % of the patch, the one partial
  line a branch no input takes; `Remote corpus` run 123 green; the corpus differential over 403 documents, by default
  and after a forced rebuild: no object or finding changes. The benchmarks against `main`, alone, within noise but the
  search past a comment holding `endstream` over 4 MB of white space, a quarter slower: the JIT held its byte loop's
  state in memory once the search's bound became a parameter, and the loop, moved into a method of its own, is back
  within noise.
- **For callers.** `PdfReaderLimits.MaxRebuildReadPasses` (4; `Unbounded` lifts it; zero or less refused) and the code
  `limit.rebuild-reads`; past it, an object the index left out is unknown, and reading one throws under `ThrowOnLimit`.

## 2026-11-17 — Batch 24: what a full diagnostics record still answers for ([#384])

- **The question.** Measured on `main` at `edd3021`, twice, identical: batch 11 had paid [#169]'s reader side — a
  cross-reference stream, object stream header or member a guard cut stays the guard's at any capacity —, but past the
  capacity a caller could not see a guard: `HasWarnings` and `Contains` read the entries kept only. With no room, a
  cross-reference stream whose `/Length` the chain reported earned `file.trailer-malformed`, an Error, the reader taking
  any dropped entry for a syntax fault. The capacity options took negative values, the cache's floor of 64 unsaid.
- **Settled with the maintainer** (2026-11-17, each as recommended): `Contains` and the `Has` properties answer for every
  entry, kept or dropped, as a validation report does (ADR 36), with no new member; a diagnostic capacity of zero keeps
  nothing and a negative capacity is refused; the cache's floor is documented.
- **What changed.** Each `PdfDiagnostics` notes the severity and code of what its capacity drops — once on a document's
  record —, through its marks, rollbacks and moves; a dropped entry is a syntax fault only under a `syntax.` code. A
  section record boxes its rare mark, so the mark's new field costs the others nothing. No object served and no finding
  changes on a valid file.
- **Review** (ADR 54): one round, three lenses — the bookkeeping, with a model-based probe of 4,000 random sequences;
  the invariants; real damage, about 77,000 damaged variants against a full record and room to spare, and the texts:
  no blocker, major or minor, ten nits, three found twice. The final pass fixed texts and tests; the nit whose fix
  changes code — the pending record's note growing with nested loads and kept at its peak — is filed: [#385].
- **Mutation** (ADR 53 amended): 26 mutants over the defenses among the changed lines — the capacities' refusals, the
  cache's floor, the note's dedupe and its window; 18 killed by the suite, 5 by tests the step wrote, 3 equivalent, a
  second agent agreeing; two of those are named in [#385], whose fix's test kills them.
- **The issues.** [#169]; batch 24 is paid, [#144] having gone with [#141] in [#254].
- **Checks.** 12,214 unit tests, 12,211 passed and 3 skipped, with 235 of the 242 remote documents; the timing and
  memory tests alone; CI green with the integration suite; the patch's coverable lines whole; `Remote corpus` run 122 green;
  the parser and reader benchmarks against `main`, within noise; the corpus differential over 403 documents at
  capacities 1,000, 0 and 7: no finding or kept entry changes, and at 0 and 7 `Contains` and the `Has` properties answer
  as at 1,000; IBM's QMF manual, which passes 1,000, now answers for the `filter.*` codes it dropped.
- **For callers.** `Contains`, `HasRepairs`, `HasWarnings` and `HasConformanceLoss` answer for entries the capacity
  dropped; `DiagnosticCapacity`, `ObjectCacheCapacity` and `PdfDiagnostics.Capacity` throw `ArgumentOutOfRangeException`
  for a negative value.

## 2026-11-17 — The debts batches 21 and 22 filed, placed

- **The question.** Twenty-six debts filed under M02 by batch 21's two pull requests and batch 22's ([#353] to
  [#359], [#361] to [#363], [#365] to [#379], [#381]) were in no batch.
- **Settled with the maintainer** (2026-11-17, each as recommended), by the code each shares, read from the issues'
  bodies: five new batches, 33 to 37 — the headers the rebuild's scan takes in stream data it cannot confirm, with the
  denial of service [#362]; the searches near a row and an offset and a data end without `endstream`; the header walk,
  the scan's pieces and the expansion's order; a rebuild's trailer merge and encryption at opening; the chain's tables
  and trailers since [#322]. Batch 33 runs right after batch 24. [#381] joins batch 31 with [#111], which then opens in
  two pull requests; [#374] joins batch 30 with [#192]; [#359] goes with slice 5 ([#61]), as its body says; [#367]
  moves to M03 with [#52], as [#331] did, and [#373] to M23, as [#343] did.
- **For callers.** Nothing changes; `docs/status.md`, *Next concrete step*, lists the batches.

## 2026-11-17 — Batch 22, second pull request: what file.root-invalid says of the catalog after a rebuild ([#382])

- **The question.** Measured on `main` at `a2f82d9`, twice, identical: a document rebuilt as it opens with no catalog
  at all earned no finding — only `t02-01-001` among the 81 corpus documents whose chain is not read ([#132]); a
  catalog whose own load rebuilds the index was found, the rebuild running its own search once the load returns since
  [#167] — [#229]'s text no longer held, a test closes it —; and `file.root-invalid` ended "No object of the file is a
  catalog" where a guard cut a candidate, a rebuild made `/Root` lead to the catalog, or an older trailer gave it
  ([#260]).
- **Settled with the maintainer** (2026-11-17, each as recommended): the trailer the rebuild merged judged only where the
  reader found no catalog at all; after rounds 1 and 2, fixing their minors — wording on real-file shapes — in a further
  round, the second with a neutral clause for a `/Root` that led to nothing.
- **What changed.** Where the chain gave no index and no catalog is found, the rebuilt trailer is judged, unless a guard
  cut `/Root` or left objects out of the rebuilt index. The finding ends with what the reader knows: the catalog it
  took, the one a rebuild made `/Root` lead to, the object a guard cut or left out there, none among what it read whole
  where a guard cut some, or none among what it could read. A `/Root` that led to nothing read as null through the
  chain, or reads as null, guessing no cause. No object the reader serves changes.
- **Review** (ADR 54): three rounds. Round 1, three lenses: no blocker or major, 3 minors — wording on real-file
  shapes — and a nit regression. Round 2, two lenses: no blocker or major, 3 minors. Round 3, one lens: no blocker or
  major, a minor the final pass fixed. Filed: [#381].
- **Mutation** (ADR 53 amended): 24 mutants over the defenses among the changed lines; 18 killed by the suite, 5 by
  tests the step wrote. The one claimed equivalent a second agent told apart through a caller that removes `/Root`, the
  original wrong there: the search now notes every candidate a guard cut, and the suite kills that line's 2 mutants.
- **The issues.** [#132], [#229] and [#260]; batch 22 is paid.
- **Checks.** `Remote corpus` runs 119 and 120 green; 12,231 unit tests, 12,228 passed and 3 skipped; the timing and
  memory tests alone; the patch's 40 coverable lines whole; the site; the corpus differential over 408 documents:
  `t02-01-001` gains `file.root-invalid`, `t02-01-002`'s finding is worded anew, no object changes.
- **For callers.** A document rebuilt as it opens with no catalog now earns `file.root-invalid`; its message, and that of
  every `file.root-invalid`, ends with what the reader knows of the catalog rather than a claim about the file.

## 2026-11-16 — Batch 22, first pull request: why the chain leaves the index to a rebuild at opening ([#380])

- **The question.** Measured on `main` at `7e8dd24`, twice, identical: a chain each of whose sections reads whole and
  indexes nothing was rebuilt at opening with no finding ([#218]); a table whose `trailer` keyword no dictionary
  follows was read as sound, what older sections index reading as null ([#322]); the section `startxref` names, there
  and unreadable or missing, left only `xref.rebuilt` ([#323]); `xref.section-malformed` was silent where the trailer
  was at fault too ([#325]).
- **Settled with the maintainer** (2026-11-16, each as recommended): the batch's seven issues in two pull requests by
  area, the chain first, the catalog after a rebuild ([#132], [#229], [#260]) next; a new Error rule,
  `xref.chain-empty`; a table whose trailer keyword no dictionary follows read in part, the first section included;
  a dictionary the rows run into with no keyword read as the trailer; `xref.section-missing` for a `startxref` that
  names no section; after round 1, fixing its three minors — regressions on hand-made shapes — in a second round.
- **What changed.** Such a keyword-less dictionary is the table's trailer — its `/Prev` followed, its `/Root`,
  `/Encrypt` and `/ID` kept, which a rebuild lost — and the table is read in part, no older row standing for a number
  it does not give, rows having perhaps been lost with the keyword. The first section's loss is reported before the
  rebuild. `xref.section-malformed` names a section's own fault whatever its trailer's.
- **Review** (ADR 54): two rounds. Round 1, five lenses: one major claimed, a refuter downgrading it to a nit, 3
  minors, about twenty nits. Round 2, three lenses over the fixes: no blocker, major or minor; the final pass fixed the
  texts. Filed: [#375] to [#379].
- **Mutation** (ADR 53 amended): 23 mutants over the defenses among the changed lines; 18 killed by the suite, 4 by
  tests the step wrote, 1 equivalent, checked by a second agent.
- **The issues.** [#218], [#322], [#323] and [#325]; the catalog's three, [#132], [#229] and [#260], in the next.
- **Checks.** CI green, the integration suite and the .NET 11 island included; `Remote corpus` run 118 green; 12,206
  unit tests, 12,203 passed and 3 skipped; the timing and memory tests alone; the patch's 114 coverable lines whole;
  the site; the corpus differential over 408 documents: no finding nor object changes, 65 gain
  `xref.section-missing` at opening, 5 `xref.section-unreadable`, iPRES `t04-008` and `t04-009` read in part.
- **For callers.** A rebuild at opening now says why in the diagnostics; an encrypted file whose `trailer` keyword is
  lost keeps its `/Encrypt`, refused rather than read as ciphertext; a new Error rule judges an empty chain.

## 2026-11-16 — Batch 21, second pull request: what lies inside another object, told in the searches near a row and near an offset ([#171])

- **The question.** Measured on `main`: a header or a cross-reference section inside a string, a stream's data or a
  comment stood for the file's own in the search near an entry's row and in the search near an offset the chain names,
  and a window's start could cut a header's number ([#171]). The rebuild's half came in [#352].
- **What it does.** The rebuild's own scan, made once per document and only when a search needs it, is kept as a memo of
  the headers, `xref` keywords and trailers it takes, each held as taken between objects or in another object's bytes,
  16 bytes a place, bounded by `MaxIndexEntries` as a guard; a rebuild after a search replays it. The search near a row
  ranks clean candidates first, then the one its neighboring rows support, then the nearest; the search near an offset
  skips a place the scan does not take, reading nothing there. A choice the reader cannot vouch for is the validator's
  Error, under two new rules, `xref.entry-unconfirmed` and `xref.section-unconfirmed`; the object served is the same
  either way. A `trailer` keyword the scan's piece cut, once skipped, is merged.
- **Settled with the maintainer.** The memo from one scan; the nearest supported candidate; an Error under a rule of its
  own; every round 4 finding fixed, the batch passing its 1,000 lines of `src/` (about 1,340 at the end); the read past
  a length taken from the bytes bounded, after the final pass reopened the loop; [#361] filed rather than an eighth fix
  of the read budget; today, the overflow below fixed in the pull request.
- **Review** (ADR 54): eight rounds — 18 findings in round 1 (1 blocker, 1 major), 10 in round 2 (1 major), 17 in round
  3 (3 majors), 12 in round 4 (4 blockers, 2 majors), 9 in round 5, which stopped the loop until the final pass found a
  blocker on a valid hand-made file; 6 in round 6 (2 blockers, 1 major), 9 in round 7 (5 blockers, one cause: a length
  read from the bytes that cut a stream's data), 8 in round 8 (1 minor, 7 nits). Round 8's rule: a length the scan
  cannot confirm changes only how the memo counts places. Filed: [#361] to [#363], [#365] to [#372]; [#353], [#358],
  [#361] and [#372] commented.
- **Mutation** (ADR 53 amended): 396 mutants over the defenses among the changed lines. 247 killed by the suite, 6 of
  them by a hang, each timing-only kill re-run alone (one survived it); 115 by tests the step wrote, 2 of them timing
  rows, each re-run on the final commit; 32 equivalent, each checked by a second agent, which overturned 6 first
  verdicts; 2 filed, told apart only past 2^30 slots ([#373]). The step found a defect `main` had not: a length past the
  end of a source that says it holds `long.MaxValue` bytes overflowed, and opening threw; fixed, its 7 mutants run, 1
  equivalent.
- **Checks.** `Remote corpus` run 117 green; CI green; 11,688 unit tests, the timing ones alone; the rebuild's
  benchmarks within `main`'s spread, allocations equal; the JIT's lost inlining of the lexer in the scan filed as
  [#374]; the patch's coverage 99.5 % as Codecov counts it, its three lines a guard no call reaches and an arm a value
  returned before never takes.
- **For callers.** A search near a row or an offset no longer takes a header or a section another object holds for the
  file's own; two new Error rules say where the reader could not vouch for its choice. A document whose searches ask for
  the memo pays one scan of the file; a sound one, nothing.

## 2026-11-13 — Batch 21, first pull request: what a rebuild takes, and encryption decided at opening ([#154], [#171], [#327], [#348], [#350])

- **The question.** Measured on `main` at `7bccd5b`, twice, identical: a rebuild took any header the bytes spelled — in
  an attachment's stream data, a string, a comment — and the newest won, so an injected page tree replaced the file's
  own ([#171]); a rebuild lost an `/Encrypt` kept in a cross-reference stream, and one resolving to null refused the
  document ([#154]); `4 +0 obj` was no header to the scan ([#327]); the expansion followed the order numbers were met
  ([#348]); the scan rented 2 MiB from a file source ([#350]).
- **Settled with the maintainer** (2026-11-13, each as recommended): all of [#171] in this batch, as two pull requests
  by area — this one the rebuild, the next the searches near an offset and a row, which closes it; what the file
  delimits hides the headers inside; signs read as the lexer reads them; a non-dictionary `/Encrypt` leaves the document
  unencrypted, reported; encryption decided at opening; a string that holds `endobj`, or that the end of the data leaves
  open, delimits nothing (round 1's one major); the second resolution of `/Encrypt` removed (the mutation step).
- **What changed.** The scan delimits each object it takes as a load reads it, in pieces of a megabyte, each byte
  searched, swept, lexed and walked back a bounded number of times. The rebuild merges cross-reference streams' trailer
  keys newest first. Object streams are taken in from the last written. `IsEncrypted` is fixed as the document opens.
- **Review** (ADR 54): five rounds. Round 1: six lenses, 29 findings — 11 blockers, all the new scan's cost, 1 major, 17
  minors and nits. Rounds 2 to 4: one or two blockers each, in the code the round before had written. Round 5: no
  blocker and no major, 3 minors and a nit.
- **Mutation** (ADR 53 amended): 92 mutants over the defenses among the changed lines. 50 killed by the suite, 34 by
  tests the step wrote and re-run on the final commit, 6 equivalent, each checked by a second agent, 1 filed ([#353]), 1
  gone with its code: the second agent overturned its equivalence, and the maintainer removed the redundant resolution.
  The step also found two defects, filed in [#356] and [#358].
- **Checks.** Forced rebuild of 403 corpus documents: four change objects, each for the better; 50 lose a false
  `trailer.root-recovered`. CI green at `7a91f02`; `Remote corpus` run 113 green; the patch's coverage 99.52 % as
  Codecov counts it, its three partial lines branches no input takes. A rebuild's scan costs 1.2 to 3 times what it did,
  allocating what it did; 12,000 `trailer` keywords that comments hold take 0.2 ms against 250 to 470.
- **Filed**: [#353] to [#358] under M02, from the review and the step; [#359], from the corpus differential.
- **For callers** (`fix!`). A rebuilt index no longer serves a header another object holds, and may serve another
  definition than before: an object stream's newest. `IsEncrypted` is decided as the document opens;
  `trailer.encrypt-invalid` and `trailer.encrypt-late` are new. `4 +0 obj` is object 4.

## 2026-11-12 — Batch 20: which definition a rebuild keeps, and what a dropped place leaves ([#189], [#276], [#320])

- **The question.** Measured on `main` at `86e8d9c`, twice, identical: a rebuild kept a direct definition over a newer
  copy in a later object stream, and of two object streams the one it happened to take first ([#189]); `10 0 objx` was a
  header to the scan and to both searches near an offset, not to a load; an object a rebuild serves from another
  definition of its object stream read as null with no finding ([#276]); a place the search near a missing section tried
  and dropped left its rows, its refused numbers and the incomplete mark ([#320]).
- **Settled by measurement.** The definition the file writes last wins, a member counting where its stream lies: of 403
  corpus documents only two truncated PDFBox files change, each now serving its newest revision — the `/Info` with the
  newest `/ModDate`, the struct tree of the last revision, which adds five true `object.reference-missing`. A dropped
  place's reading is a tentative one of the index, whose offsets bound no search until kept, so that dropping it sorts
  nothing; a guard it reached stands ([#221]).
- **Settled with the maintainer** (2026-11-12, three questions, each as recommended): [#276] by the probe, an
  `xref.entry-broken` finding, not a new rule nor the reader; a last definition that reads as nothing, filed as [#351];
  `object.redefined` keeps counting a number reused under a new generation.
- **Review** (ADR 54, its first batch): one round, seven lenses, 21 findings — the one major, a real portfolio whose
  attachment's uncompressed object stream outranks the host's objects once damaged, refuted as [#171]'s; 5 minors, 15
  nits. The final pass fixed texts and tests; [#348], [#349] and [#350] hold the rest, whose fixes change behavior.
- **Mutation** (ADR 53 amended): 51 mutants over the defenses among the changed lines; 27 killed by the suite, 12 by
  tests written in the step — 3 after a second opinion overturned their verdict —, 12 equivalent, each checked twice.
- **Checks.** `Remote corpus` run 111 green; CI green; the patch's coverage 98.7 % as Codecov counts it, its two partial
  lines defensive branches no input takes.
- **For callers.** A rebuilt index may serve another definition of an object than before: the one the file writes last
  (`fix!`). `10 0 objx` is no header anywhere. A rebuild's lost member is an `xref.entry-broken` finding. A file whose
  relocation dropped a decoy reads as one without it.

## 2026-11-11 — Where a batch's time goes, and what the review protects

- **The question.** Batch 19 took eight days: 13 review rounds, 13 fix workflows, a mutation step over its whole
  patch. Before its merge the maintainer asked whether that time is justified, whether there are too many unit tests,
  whether mutation testing is worth it, and what takes the time. Answered from the transcripts of the batch's 255
  workflow agents, its 314 kept findings classed one by one, and the mutation step's ledger:
  [Where a batch's time goes](research/2026-11-session-speed.md).
- **What it found.** 71 % of the findings changed nothing a caller can observe; 83 of the 91 that changed behavior
  needed a hand-made file, and 64 of the 91 were regressions of the batch's own fixes. Round 5 had no blocker and no
  major. About 6,000 mutants since batch 5 found one defect of the library; batch 19's 3,736 found none. The suite runs
  in 4 minutes; the time is the model reading contexts of about 232,000 tokens, round after round.
- **Settled with the maintainer** (2026-11-11, eight questions, six as recommended; the maintainer chose a cap of
  eight rounds over five, and compacting the documents now over a rule for new texts only):
  [ADR 54](adr/0054-review-and-documentation-effort-follow-what-protects-the-library.md) — severity by effect and by
  whether real files have the input; rounds stop at the first with no blocker and no major, eight at most; round 1
  on the whole patch with a real-damage lens, later rounds on the previous fixes; refuters for blockers and majors
  only; batches of at most six issues and about 1,000 lines of code; a behavior written once, beside the code.
  [ADR 53](adr/0053-mutation-testing-runs-a-fixed-catalog-over-the-changed-code.md) amended: defenses only, once per
  pull request, equivalence through documents and the public API.
- **Done.** The threat model compacted to what each defense stops, where it lives and the test class that holds it;
  this journal moved out of `status.md` and its older entries summarized (303 KB to 82 KB); the `batch` skill and
  `.claude/scripts/batch-check.sh`; `CLAUDE.md` brought in line. In its own pull request, merged first ([#345]):
  `PdfFileReader.cs` split into ten partial files by concern, a pure move.

## 2026-11-03 — Batch 19: where the chain's sections lie, and what it reports of one it reads in part ([#156], [#188], [#197], [#207], [#208], [#210], [#221], [#222], [#241], [#261], [#264], [#281], [#317])
- **The question.** Measured on `main`: a section the chain names that cannot be read was sought nearby and landed on
  one already read, reporting a false loop or reading an object as null in silence ([#188], [#281]).
- **Settled with the maintainer** (2026-11-03, 11 of 12 as recommended): a section is there where its offset holds
  `xref` or a `/Type /XRef` stream, one that cannot be read reported there, never sought nearby, such a stream's `/Prev`
  followed; no older row stands for a number a section that lost rows declares, refused ranges kept to 4,096 (ADR 34); a
  section read in part is `xref.section-unreadable`; an empty table indexes nothing, `xref.section-empty`, a repair,
  rule `xref.subsection-missing`, a warning; a reserved-type row reads as null, `xref.entry-reserved` and rule
  `xref.entry-type-reserved`, warnings, ISO 32000-1 defining types 0 to 2; an `/XRefStm` naming no stream of its
  revision is `xref.hybrid-stream-lost`, a warning, rule `xref.hybrid-stream-wrong`, an error, `xref.chain-loop` kept to
  `/Prev`; [#317]'s test probe given 1,000 findings' room; one notion of what the chain read; `trailer.root-recovered`
  once a document; against the recommended debt, the `endstream` search's stop follows the rebuild's scan.
- **Settled in the reviews** (twenty questions, each as recommended): an unreadable place that declares itself a section
  ends a relocation where nearest, changing [#320]; what an unreadable classic table an `/XRefStm` names lost is
  refused; `file.size-wrong` judges no chain ending in a loop; a linked section read from its first token, its white
  space bounded by `MaxTrailerLength`, past it `limit.trailer`; `xref` in stream data left to [#171]; a stream whose
  filters decode nothing holds no rows and hands its `/Prev` on; a hybrid table's own stream is sought only at or after
  the one its `/XRefStm` read, its costs filed ([#333], [#332]); `xref.stream-passed-over`, a warning, names only a
  place that declares itself, withheld once a trailer guard cut a named offset; a header reached at a sign is told by
  its digits, never read twice. On 2026-11-10 the loop was made to stop at a round with no blocker or major surviving.
- **Reviewed** thirteen times, 328 findings, 14 refuted: one blocker, in round 4, and majors by round 10, 3, 4, 2, 0, 1,
  2, 9, 3, 8, 1, 1 and 2, the last two refuted; all fixed but [#171]'s stream-data reading, [#332] and [#333].
- **Done** in [#344], closing the heading's issues: 512 bytes searched either side of an offset ([#197]); headers with
  comments read as a load does ([#261]); a guard-stopped section judged as a whole read judges it (ADR 34, amended on
  2026-11-04); a stream whose `/Size` or `/Index` a guard cut has its data decoded, so `MaxStreamLength`,
  `MaxFilterChainLength` or `MaxDecodedStreamLength` may be met first; counts worded through `CountText` ([#241]).
- **Tests and mutation.** 1,809 new unit tests, 6,657 to 8,466; patch coverage 1,460 of 1,461 lines and 1,307 of 1,313
  branches, `src/` 99.48 %. Of the 3,736 mutants run at the end, 3,193 killed, 224 did not build, 319 survived: 113
  killed by new tests, 206 equivalent, 39 of 245 verdicts of equivalence overturned; none left to a debt.
- **Filed** under M02: [#320], [#321], [#322], [#323], [#324], [#325], [#326], [#327], [#328], [#329], [#330], [#332],
  [#333]; under M03, [#331]; under M23, [#343]; [#336] and [#342], tests failing on Windows coverage or load.

## 2026-10-30 — Batch 18: what a rebuild leaves a caller, and what the validator's analyses cost ([#167], [#168], [#201], [#277], [#307], [#312])
- **The question.** Measured on `main`: a rebuild made a caller's enumeration of `ObjectNumbers` or `Diagnostics` throw
  and merged trailers into the `Trailer` it had handed out ([#167]); analyses kept every fault before `FindingCapacity`
  applied, 2.4 GB for a report of 1,000 ([#168]); object streams walked again what their decoding reads and page tree
  frames kept each node ([#201]); the walk followed `/Filter` whole, missing loops through streams ([#277]).
- **Settled with the maintainer** (2026-10-30, all four as recommended): enumerations go on over what was there when
  they began, through a public `PdfDiagnostics.Enumerator`, a binary break typed `fix!` (ADR 48), and a trailer taken
  stays as it was, `Count`, `Catalog` and `IsEncrypted` staying live; each analysis writes what the report has room for
  and counts the rest; a page tree frame keeps its node's `/Count` and `/Kids`, not the node, [#311] filed; the
  Arlington walk's queue left to [#309].
- **Settled in the reviews** (fifteen questions, all but one as recommended): another stream's object followed only
  where its decoding is the same in every read order, nests kept by the stream heading them; against the recommendation,
  [#312] closed in the batch, followed where the holder's closure is acyclic; a rebuild the source fails run again by
  the next read, what it reported withdrawn; nests past the cache documented under [#181]; [#314] and [#315] filed; a
  cost-only mutant killed by a deterministic measure, else equivalent, its cost bounded; `trailer` taken anywhere left
  to [#171] and [#49]; the trailer scan loads nothing, so it merges the same trailers whatever is in flight, noting a
  nested stream or a reference under `syntax.unexpected-token`; two timing tests held by allocation; the walks probe the
  stack before each level, the cut reported once a document under `syntax.depth-exceeded`.
- **Reviewed** ten times, 191 findings, until a round had no minor: eleven majors in rounds 1 to 9 (2, 5, 2, 0, 0, 0, 1,
  0, 1), ten fixed, among them a stack overflow that ended the process, and nests past the cache documented under
  [#181]; of the tenth's two, one refuted, one [#163]'s residual.
- **Done** in [#318], 116 commits, paying the heading's six issues: no read asks the source for no byte; circular object
  streams found by Tarjan's algorithm in linear time; `ObjectGraph` keeps of an object still to read its number, never
  what it holds; the dependency walk follows each object stream as the reader decodes it, `MaxNestedLoads` made
  internal; the suite's threads run on `Stacks`, 256 KB or 1.5 MB; ADR 50, on what a validation changes for a caller.
- **Tests and mutation.** 1,084 new unit tests, 5,573 to 6,657; patch coverage 1,122 of 1,123 lines, 933 of 935
  branches. 1,675 distinct mutants: 1,593 killed, 57 equivalent and one more by reading, 8 equivalent in what is
  reported, their cost bounded, 17 superseded; none filed.
- **Filed** under M02: [#309] the Arlington walk's queue, [#312], paid in the batch, [#313] verdicts that depend on read
  order, older, [#314], [#315] a waiting stream's walk per holder, [#317] a .NET 11 timing flake; under M03, blocking
  its slice 5, [#310], objects a caller can change; under M23, [#311].

## 2026-10-28 — Batch 17: numbers a file chooses to share a bucket ([#155])
- **The question.** .NET puts an `int` in the bucket its value leaves modulo a prime anyone can read, so object numbers
  that prime apart share one bucket: on `main`, 34,167 one-row subsections opened in 1.9 s against 0.04 s, a million
  references resolved in 56 s against 0.73 s, under every guard. Four ways were prototyped and measured.
- **Settled with the maintainer** (2026-10-28, all four as recommended): the index dense in arrays, a seeded map past
  its slots, and every other table keyed by numbers hashed through the seed (ADR 52); the seed `System.HashCode`'s,
  drawn once as .NET does for strings; `PdfObjectId` and `PdfDiagnostic` hashed through it, their equality the record's;
  a test that reads the code, covering every numeric key.
- **Settled later.** On 2026-10-29: no more check of each commit built and tested alone, for every batch, since CI runs
  on each push and a batch is squashed; the library builds no type parameter it is given, generic code taking a factory,
  since the twenty-second round found `new()` reached through a dispatch the reading does not follow. On 2026-10-30, as
  the review's mutation sweeps had gone a level deeper each round for seven rounds, they were bounded, then made a rule
  for every pull request that changes `src/` (ADR 53): a survivor is a gap in the tests, never a review finding.
- **Reviewed** thirty-two times, 286 findings. Three majors, all in round 1: a stale `/Size` making room for one entry
  more at each older section, the index copied whole each time, 3.6 GB for a thousand sections, found twice; and lazy
  reading's measured section left with the hash table's figures. Later rounds hardened the test's two readings of the
  code, and from round 25 swept the compiled reading with mutants, until ADR 53.
- **Done** in [#308], 64 commits: `PdfObjectId` and `PdfDiagnostic` hash through `System.HashCode`; `PdfXRefTable` keeps
  numbers and entries in two arrays, finding a number in a slot while numbers run in order, past them in a seeded map,
  40 bytes an object against 52 to 63; a stale `/Size` bounds nothing once an older section numbers past the newer ones,
  so that the index doubles rather than grows by one; `SeededComparer` for every other table keyed by numbers;
  `SeededComparerTests` refuses a hash collection over a key a file can steer not handed `SeededComparer.Instance`, and
  any that enumerates by hash but `PdfName`'s interning table. ADR 34's row for `MaxIndexEntries` amended on 2026-10-28.
- **Tests and mutation.** 230 new unit tests, 5,343 to 5,573; the patch's 191 measurable lines all covered. Each
  condition the rounds added failed a sample written for it or was judged equivalent; eight sweeps of the compiled
  reading, rounds 25 to 32, ran 3,676 mutants, the last 687: 545 failing a test, 64 survivors held by samples, 78
  classed.
- **Filed** under M23: [#305], the seeded hash the validator's walks pay, about 15 % on the rules; [#306], the source
  reading's survivors. Under M02: [#307], a disposed document's memory test that failed once in CI's .NET 11 job. Found
  in the batch: coverage probes broke the compiled reading in CI runs 813 to 819, read through since.

## 2026-10-27 — The remote corpus: a patient fetcher, an encrypted cache, and the project's own copies ([#204], [#291])
- **The question.** RADAR timed out on the ipres2017 archive again in the nightly run 82 (2026-10-26), web.archive.org
  on the USDA's fact sheet; run 83 passed whole. The maintainer's decision of 2026-10-25 had three parts: a cache no
  pull request's run can read, a fetcher that waits as hosts ask and says what failed, and a repository of the project's
  own for the documents a license lets it copy.
- **The fetcher** (`tests/corpus/build/fetch_remote.py`): four attempts, 15 s, 1 min and 2 min apart; a 429 or a 503
  carrying `Retry-After` retried after the delay it asks, capped at 2 min; a host that wore out every attempt on a
  document gets one attempt for each later one, so that an outage costs minutes, not the job's hour; every failed
  attempt printed, and counted by host in the job summary; `--pins` and `--files`. 25 new tests, 48 in all.
- **The cache** (`.github/workflows/remote-corpus.yml`): keyed by the SHA-256 of every remote document's pin, restored
  from the nearest older key, saved only when every document was fetched, and encrypted with `REMOTE_CORPUS_CACHE_KEY`,
  a secret set on 2026-10-27, since GitHub lets a pull request's run, a fork's included, restore its base branch's
  cache. A run without the secret fetches everything.
- **The copies** (ADR 51): [AdCodicem/AdCodicem.Pdf.Corpus](https://github.com/AdCodicem/AdCodicem.Pdf.Corpus), public,
  created on 2026-10-27, with immutable releases: 15 files, 102 documents, about 257 MB, and USGS Professional Paper 1
  (146.7 MB) as a release's asset. Each license was read again for that very file, recorded in `source.copy`;
  `source.publisher` keeps the publisher's URL, which the fetcher falls back to when the copy is unavailable, never when
  it is refused. Seven of the 109 documents a license was announced to cover stayed at their publisher. The schema holds
  `source.copy` and `source.publisher` together, and the copy's URL to a commit or a release of that repository.
- **Checked**: the 102 copies fetched by the fetcher, every pin matching; its 48 tests; 9 new cases of the manifest's
  schema tests.
- **Merged** with [#300]: `Remote corpus` run 84 passed all 242 documents on the branch, run 85 on `main` filled the
  cache, and run 86 restored and decrypted it, nothing downloaded.
- **Two Government of Canada forms copied too**, IRCC's IMM 1344 and the CFIA's form 5704, the maintainer letting
  non-commercial terms count since the copy repository sells nothing (ADR 51, amended). Massachusetts's dashboard, with
  no copy beyond fair use, and the USDA's Hebrew fact sheet, its translator unknown, are not copied.
- **Note for this machine**: Python's TLS refused www.radar-service.eu's chain, which curl accepted, most likely an
  interception on the workstation; CI never met it, and the archive no longer comes from RADAR.

## 2026-10-25 — Batch 16: where a stream's data ends, and what a guard's cut leaves ([#170], [#174], [#220], [#224], [#230], [#263])
- **The question.** [#174], [#220]: an unconfirmed `/Length` was searched in windows charged to `MaxObjectLength` or the
  trailer's bound; [#170]: streams kept their raw bytes, unfiltered data came back whole; [#224], [#263]: values a guard
  cut read as the file's; [#230]: an unclosed object took the next's entries.
- **Settled with the maintainer**, all eleven questions as recommended (2026-10-22, 23): an `endstream` past white space
  and comments confirms a `/Length`, `MaxEndStreamGap` removed (Q1); one memoized search per unconfirmed length, to the
  next header or indexed object, stream data never charged to `MaxObjectLength` (Q2); else the `endobj` ends the data,
  `stream.truncated` or `stream.length-invalid` (Q3); the next header closes open containers, `syntax.truncated-object`
  (Q4); no raw data kept (Q5); a guard, `MaxStreamLength`, 256 MB, `limit.stream-length` (Q6); a deferred
  cross-reference stream read past the window, `MaxTrailerLength` bounding its dictionary alone (Q7, Q8); a value
  leading to what a guard cut is the guard's, ending the chain, nothing rebuilt (Q9, Q10); `LimitSince` gone (Q11).
- **Reviewed** thirteen times, 325 findings, each put to a refuter, every major fixed; settled on the way, as
  recommended: a comment is white space after a length only past white space, and after `endobj` (F5, F6); a missing
  `endstream` under a length unindexed or cut the guard's (F8, G3, X1); `limit.stream-length` reported on each decode
  (G4); `HasImageFilter` reads what `Decode` reads (G2); data a spelled header stops under a hidden length the guard's
  (R4-1); a reference the guard parts after `N G ` no missing `endobj` (R4-23); a hybrid stream's `/Prev` judged as
  written (R4-15); an `endstream` under a hidden length the file's only where what follows proves it (R6-1, R6-2), data
  whose end only it says decoded as hidden (R8-1); a moved section the guard's only if declared before the cut (R8-3);
  hidden-end cases fixed as they stand (R12-1); a section lacking rows read as whole (R12-2); a last round (R12-3).
- **Done** in [#297], squashed at the maintainer's choice, merged on 2026-10-25; ADR 34 amended (2026-10-23 to 25):
  `MaxStreamLength` a guard, `EndStreamSearchPasses` an internal constant. 643 new unit tests, 4,689 to 5,332; every
  round's mutants killed or shown equivalent; of the patch's 39 partial lines, 30 held by tests, 9 no input takes.
- **Filed** under M02: [#279], [#282], [#287], [#289], [#290], [#292] to [#295] with batch 27; [#280], [#296] with batch
  25; [#283], [#288] with batch 26; [#281] with batch 19. [#264]'s first gap paid here.
- **Remote corpus** ([#204], decided 2026-10-25): documents a license lets the project copy kept, unmodified, in a
  repository of their own ([#291]), `tests/corpus/remote/` cached between runs, the fetcher waiting as hosts ask.

## 2026-10-22 — Batch 15: object stream members ([#160], [#161], [#200], [#202], [#255], [#257], [#273], [#274], [#275])
- **The question.** [#160]: a member was parsed over the whole decoded data, past `MaxObjectLength`, its header's tables
  allocated from `/N`; [#161]: a stream inside an object stream decoded under the default guards; [#200]: members one
  byte apart each parsed to the data's end; [#202]: a member past the data or written as a stream earned no finding;
  [#255]: a chain-place read noted faults of a definition no longer served; [#257]: no test held that a finished rebuild
  forgets what read as null during the loads in flight.
- **Settled with the maintainer**, eight questions, each as recommended but Q7: a member parsed to the next one's start,
  the last to the data's end, as qpdf and pdf.js read it, no further than `MaxObjectLength`, `limit.object` (Q1); a pair
  not past the one before, or whose member would start at or past the data's end, ends the header,
  `object-stream.unreadable` and `xref.object-stream-broken` (Q2); a `/N` past what `/First` bytes can list,
  ⌊(`/First`+1)/4⌋, caps the tables alone (Q8); a member written as a stream reads as null, nothing kept,
  `object-stream.member-stream` once, its entry `xref.entry-broken`'s (Q3, Q5, Q6); a chain-place read records nothing
  when the index serves another definition (Q4); [#273] to [#275], which the measurements filed, all paid here, the
  maintainer's choice where [#275] alone was recommended (Q7).
- **Reviewed** five times, each finding put to a reproducer: 48 findings (44 confirmed, 13 major), 29 (26, 7 major), 16
  (13, 4 major), 14 (10, 1 major), 6 (2). Settled, as recommended: a header breaking after listing every object the
  index places is no finding, and a member written as a stream only a rebuilt index places judged, closing [#202] (R1);
  a guard a catalog candidate's `/Type` reaches held until the search after a rebuild ends, a `/Type` naming an object
  its stream holds circular (R2); the decoding keys followed as the reader reads them, a filter and its parameters
  through containers, `/Type`, `/Length`, `/N`, `/First` through references alone (R3); the filter walk filed (R4).
- **Done** in [#278], 37 commits: a stream not of `/Type /ObjStm` serves no member, to the reader, the rebuild and the
  validator; reports of loads in flight withdrawn once their rebuild finishes; ADR 34's classification. PDFBox's
  `pdfbox3947` reads 81 of its 100 objects, the manifest no longer requiring `object.redefined` of it.
- **Tests**: 109 new unit tests, 4,580 to 4,689; 44 mutants in four rounds, all killed, three by tests written for them.
- **Filed** under M02: [#276] (batch 20), an object served from another definition of its stream lost unreported; [#277]
  (batch 18), the filter walk. [#181] told of the catalog search's re-reads, [#37] of the members' cache weight.

## 2026-10-18 — Batch 14: page tree findings that say where, and nothing judged that a limit cut ([#129], [#175], [#256], [#259])
- **The question.** [#129]: the page tree rules worded a kid with no number of its own badly and located it at the
  document or at "object 0 0"; [#175]: they judged what a reader limit cut — missing kids, count mismatches, orphaned
  pages, invalid boxes —, and a catalog cut before `/Type` had the index rebuilt and `file.root-invalid` say none
  existed; [#256]: the cross-reference probe took for a header what a load refuses.
- **Settled with the maintainer**, four questions, each as recommended: a kid with no number named by its path from the
  object it is written in, as the Arlington walk and the object graph name a value, and located there (Q1); as the
  Arlington walk does, nothing written in a cut object judged, a cut node's `/Count` unjudged, no page called orphaned
  since the walk is partial, the references a cut `/Kids` lists still walked (Q2); the object `/Root` names taken as
  read when a limit cut it, no search, no rebuild, `file.root-invalid` not judging it (Q3); the probe and settling share
  one decision, a header read as a load reads it, up to `MaxObjectLength`, its reads bounded at twice the file per
  validation, an internal constant (ADR 34), past which, or past `MaxObjectLength`, `xref.checked-in-part` (Q4).
- **Reviewed** thirteen times; the first gave 26 findings, all confirmed, one blocking: an older kids walk that never
  ended. Settled, as recommended: a header's reading charges the bytes it crossed, windows grown eightfold, a valid file
  never reaching twice its length, replacing batch 13's R5 (R1); the parser rules out a header at the first token none
  begins with, no `limit.object` for junk (R2); an array of kids with a number of its own walked as a node,
  `page-tree.cycle`, `page-tree.node-repeated` (R3); the object graph reports a reference to nothing in a cut `/Kids`,
  the probe looking near an entry outside the file (R4). At the maintainer's choice, [#259] (a catalog in a cut object
  stream) and P1-02 (one in a cut trailer) paid here; after the final check, a `/Root` naming an object a guard left
  unindexed taken as read, null until the guard is lifted, not `trailer.root-recovered`, one the hidden trailers lack
  still looked for.
- **Done** in [#265], 61 commits: `FileStructure.RootCutByGuard` and `TrailersHiddenByGuard`. Docs: ADR 50, M02.
- **Tests**: 327 new unit tests, 4,253 to 4,580; 152 mutants, 113 killed as the tests stood, 32 by tests written for
  them, the final fix's 4 by its tests, 3 equivalent; the header's reading fuzzed over a million rows, no mismatch.
- **Filed** under M02: [#260] (batch 22), [#261] and [#264] (batch 19), [#263] (batch 16), [#262] (batch 27). [#197]
  told that the probe reaches its window too.

## 2026-10-17 — Batch 13: a report that does not depend on what was read before it ([#128], [#237], [#253])
- **The question.** [#237]: a read could make the reader rebuild its index halfway through a validation, so the rules
  before it and after it read different documents; [#128]: the walk of the object streams' dependencies read an index
  such a rebuild changes. Over 90 crafted shapes, a fresh validation, a second one and one after a read disagreed in 72,
  and `Validate` crashed on two. Three designs were prototyped. [#253]: an allocation window a collection paused counted
  9,000 bytes for 4,096. Ten questions followed.
- **Settled with the maintainer**, each as recommended but Q9: `PdfValidator.Validate` settles the index first,
  `PdfFileReader.SettleIndex` rebuilding once when a later read still could, a rebuild forgetting what was noted of the
  definitions it replaces, `FileStructure` the document as it opened (Q1); an object whose own load set off a rebuild
  not cached (Q2); a limit that rebuild reaches throws under `ThrowOnLimit` before any rule (Q3); `file.root-invalid`
  not judged when a guard hid `/Root` at opening (Q4); rows judged as the file wrote them (Q5); the exceptions named and
  the lazy-reading wording qualified (Q6); `WasRepaired` changes as on any rebuild (Q7); Q8 replaced by R2; tests per
  mechanism and a corpus sweep in the integration suite, the one test there with no independent tool, the maintainer's
  own (Q9); allocation windows under 16 KB measured as the least of unpaused ones (Q10).
- **Reviewed** twice, each settled as recommended: 36 findings, 2 blocking, 31 confirmed; then 25, 3 blocking, all held.
  A rebuild a load sets off finished from its scan once no load is in flight (R1); the object graph reads an object
  again to describe it (R2); settling takes a header past the object guard as a read does (R3); the rebuild expands
  once, when finished (R4); settling reads at most twice the file's length past each header's first two windows, then
  rebuilds (R5); the walk's memo keyed by number, holding what the reader's cache holds (R6); the outermost load
  finishes the rebuild for any exception, the scan's guard thrown first, a failed finish left to the next read (R7).
- **Done** in [#258], 25 commits. Docs: ADR 50 (*Validation settles the reader before its rules*), amending ADR 36; a
  document whose index a read rebuilds after opening wanted in `corpus-contributions.md`.
- **Tests**: 335 new unit tests, 3,918 to 4,253, and 168 integration, 1,327 to 1,495; of the fixes' mutants, 39 failed a
  test and one never finished, one lived on, filed as [#257]; the patch's 211 lines covered, every branch taken.
- **Filed** under M02: [#256] with batch 19 beside [#197], the probe taking for a header what a read refuses; [#257] and
  [#255] with batch 15. [#168] told of the walk's per-stream pass and the graph's retention.

## 2026-10-15 — Slice 4's blockers, brought forward: [#141] and [#144]
- **The question.** The maintainer asked for the M02 issues that block slice 4 ([#60]) to be paid first, one commit
  each, in one pull request. [#141]: ASCII85, ASCIIHex and RunLength passed over what their format does not allow in
  silence, and LZW took data without its end-of-data code as whole. [#144]: a document's diagnostics held a stream's
  filter fault once for each decoding — the reader's, a rebuild's, the validator's, a caller's. Five questions followed.
- **Settled with the maintainer**, each as recommended: every error ISO 32000 names in these formats reported, a missing
  end marker and a `<~` introducer included, nothing after the end marker (Q1); a missing end marker a repair after a
  whole group, pair or run, a warning inside one, bytes passed over and broken markers warnings (Q2), LZW's missing
  end-of-data code likewise (Q3); the document's list holds each report a decoding makes once, whoever decodes, a
  caller's own list getting every decoding's (Q4); a guard's report held once too, the guard still counted and
  `ThrowOnLimit` throwing at each decoding, ADR 34 amended (Q5).
- **Done** in [#254], one commit per issue: the four filters decode such data as other readers do and report it as
  `filter.failed`, once for each kind and step; decodings report into `PdfDiagnostics.Decodings`, moved into the
  document's list only past the copies it holds.
- **Reviewed** twice: 29 findings, 20 kept and 4 more weighed by hand, 16 distinct, all fixed; then 9, 7 kept, all
  fixed.
- **Tests**: 231 new unit tests, 3,687 to 3,918, and 21 integration, 1,306 to 1,327, holding each case to qpdf; 31
  mutants, all killed; the patch's 166 lines covered, every branch taken. Across the corpus, 1,082 repeats gone.
- **Filed** under M02: [#253], with batch 13, an allocation window of `HostileInputTests` a collection pauses. The Flate
  message's "the 1 bytes decoded" is [#241]'s.

## 2026-10-15 — Batch 12: filter chains ([#165], [#166], [#199], [#209], [#219], [#247])
- **The question.** [#165]: decoding one stream decoded another its filter's parameters name, while holding its own
  output, 64 deep, about 250 MB live; [#166]: `MaxDecodedStreamLength` bounded each filter of a chain alone, so a
  513-byte stream naming `/RunLengthDecode` sixteen times allocated 3.3 GB, nothing reported; [#199]: what a filter
  before the bound kept was handed on as whole, the guard's cut reported as damage; [#209]: a `/Filter` element naming
  no filter was skipped; [#219]: Arlington typed `/DecodeParms` by nothing; [#247]: allocation windows a collection
  paused failed.
- **Settled with the maintainer**, fifteen questions, each as recommended: after a cut, the filters after it decode what
  was kept as cut data, under one report (Q1); the filters before the last share one `MaxDecodedStreamLength`, the last
  keeping its own, no new property (Q2); a Flate step that decodes nothing ends the chain (Q3), and so does an element
  naming no filter, a `/Filter` resolving to null none at all (Q4); parameters paired as qpdf pairs them (Q5); every
  value a step reads resolved before any data, nothing more (Q6); a hex digit or ASCII85 group a cut parted dropped, a
  predictor undoing its last row's part (Q7); Arlington types `/DecodeParms` by the filter at its place (Q8); GC
  sampling, real and hostile shapes, a property and pause-free windows for the tests (Q9 to Q12); `HasImageFilter`
  through the same resolver, abbreviated names filed (Q13); after the review, a seventh guard (Q14) and `/Crypt` neither
  charged nor the last (Q15).
- **Done** in [#252]: a stream resolves its filters and their values before any data (`FilterChain`), the 64-deep chain
  holding 32 MB; `filter.unsupported` with the element's place and kind; `PdfReaderLimits.MaxFilterChainLength`, 64,
  `limit.filter-chain`, `int.MaxValue` under `Unbounded`, a cross-reference or object stream it ends read as cut;
  Arlington's ties fall from 4,497 to 3,364. ADR 34 amended twice, the second time for the guard and `/Crypt`.
- **Reviewed** twice: 19 findings, 12 kept, then 12, 11 kept, all fixed.
- **Tests**: 144 new, 3,543 to 3,687; 55 mutants, 54 killed, the last differing only past about 2 GB, which no test can
  hold here; the patch's 316 lines covered, every branch taken.
- **Filed**: [#250], abbreviated filter names, under M02 with [#162]; [#251], a Flate decode allocating about three
  times its output, under M23.

## 2026-10-14 — Batch 11: the index's size, the first new guard ([#164], [#183])
- **The question.** [#164]: nothing bounded how many entries the index held: one-byte rows under two Flate filters
  claimed 268 million entries until the process ran out of memory, nothing reported. [#183]: a rebuild stopped in
  silence after two million headers, and a sound file whose table or chain a guard cut was rebuilt as damaged,
  `xref.rebuilt`. Measured, more: a classic table `MaxXRefSectionLength` cut lost its trailer and `/Encrypt`, opening an
  encrypted file as plain, and `MaxSubsectionEntries` and `MaxRepairObjects` did not bound what ADR 34 said.
- **Settled with the maintainer**, thirteen questions, each as recommended: a guard's cut is no damage, what lies past
  it reading as null, unindexed, until the caller lifts the property, nothing rebuilt for it, a rebuild damage forces
  keeping what the cut chain read, a chain left with no section read rebuilt as before (Q1, Q13);
  `PdfReaderLimits.MaxIndexEntries`, `limit.index-entries`, 8,388,607 by default, the most Annex C advises,
  `int.MaxValue` under `Unbounded`, counting distinct object numbers through one gate, `MaxSubsectionEntries` and
  `MaxRepairObjects` folded into it (Q2 to Q6); a full index reads a number it lacks as null, reported once, and under
  `ThrowOnLimit` a rebuild finishes, then throws (Q7, Q9); room made for a section's rows before reading them, bounded
  by what it holds (Q8); `xref.checked-in-part` once where the index filled, an object a guard hid unknown in every rule
  (Q10); a table `MaxXRefSectionLength` cuts keeps its trailer (Q11).
- **Done** in [#249]: `ObjectPresence.Unindexed`; the rebuild scans to the file's end, the newest definition winning;
  the chain ends at any cut a guard makes; the index makes room for a section's rows once.
  System.Security.Cryptography.Pkcs pinned to 8.0.2 after two advisories of 2026-10-14. ADR 34 amended.
- **Reviewed** twice: 27 findings, 25 kept, 2 refuted; then 20, all kept, all fixed.
- **Tests**: 104 new, 3,439 to 3,543; 80 mutants, 77 killed, one gone with the code it mutated, two equivalent; the
  patch's 278 lines covered, every branch taken but one the compiler adds.
- **Filed**: nothing. [#158] told its `MaxRepairObjects` item is paid, [#169] its first item on the reader's side.

## 2026-10-13 — Batch 10: the reader's public surface ([#190], [#212])
- **The question.** [#190]: `PdfDocument.Version` said 1.4 of a file that declares no version, `Open(Stream)` read a
  `MemoryStream` from its origin or its position depending on how it was built, and a disposed document kept answering;
  [#212]: the objects' public `ToString` wrote what the file wrote, raw and whole, and `PdfReal`'s rounded to six
  decimals. A critic found more: `Version` ignored the catalog's `/Version`, a stream not rewound opened as a damaged
  file, and a reused buffer after `Dispose` served another file's objects. Ten questions followed.
- **Settled with the maintainer**, each as recommended: after `Dispose`, every member but `Diagnostics` throws
  `ObjectDisposedException`, so does whatever reaches back from an object taken before, and the reader lets go of what
  it kept; `Version` is null when the header names no version, otherwise the header's or the catalog's later `/Version`,
  a header without one declaring none whatever the catalog says; a header's version is a digit, a period and a digit,
  then white space, a comment or the end of the file; `Open(Stream)` reads a seekable stream from its start, one that
  cannot seek from where it stands, only an exact `MemoryStream` in its buffer; names, dictionaries and streams describe
  themselves as messages quote them, a string as escaped C# text cut past 127 characters, a real as the shortest decimal
  that reads back, with a point; the reader stops being disposable.
- **Done** in [#248]: `Version` is `string?`, worked out once at opening; `file.header-version-invalid` judges `1.7abc`
  whole; `page-tree.mediabox-invalid` quotes each corner as the file wrote it. Docs: `reference/object-model.md`, new;
  `M03.md` says what an input that declares no version is written as.
- **Reviewed** once, over five lenses: 25 findings, each put to two refuters, 22 kept and fixed, 3 refuted — among them
  that a catalog `/Version` the model does not describe turns version-aware rules off, which is the decision.
- **Tests**: 126 new, 3,313 to 3,439; 52 mutants, 51 killed, one changing nothing a test can see, kept as a defense; the
  patch's 168 lines covered, every branch taken but two, partial before. 14 corpus versions change as decided, nothing
  else.
- **Filed**: [#241], the messages' "1 bytes", with batch 19; [#247], an allocation test's flake, with batch 12; [#240]
  under M03, a full rewrite keeping a catalog `/Version` lower than the header.

## 2026-10-04 — Batch 9: offsets past what a long holds, and where a trailer's finding goes ([#125], [#126])

- **The question.** [#125]: the header's offset was added to every offset the file gives unchecked, so with bytes before
  the header an offset near the largest long wrapped negative: loops and cuts went unsaid, sections and rows were
  misread, findings fell outside the file and `Validate` threw ([#167]'s). [#126]: a trailer value's finding was located
  at the first section's trailer, past the file's end when `startxref` named an offset past it.
- **Settled with the maintainer**, each as recommended: a message names an offset whose shift overflows exactly, on 64
  unsigned bits, while the records keep a position saturated at `long.MaxValue`, past the end of any file, and the
  offset as written beside it; a trailer finding is located at the newest trailer the reader merged — the first
  section's when the chain read it, otherwise the newest a rebuild found, otherwise the document; one test in the
  header's frame at every shift site, the validator's two copies and negative offsets included, a negative offset lying
  before the file's start; two neighbors paid, `xref.chain-loop`'s wording for an offset outside the file and
  `PdfDiagnostic.Position` documenting that the file's length designates its end.
- **Done**, in [#238]: every shift site, the validator's included, tests an offset before adding the header's; a cut
  outside the file is placed at the section that names it, and `limit.xref-section-count` names its offset; an offset
  the reader found is a position, even before the header. The corpus reaches none of it; its records are unchanged.
- **Reviewed.** One review, five lenses: 28 findings, each put to two refuters, 27 kept, 1 refuted; among them a
  regression of the batch's own, a section found before the header read as a negative offset that made a loop.
- **Tests**: 57 new, 3,256 to 3,313; 40 mutants of the defenses, 37 failing a test, 3 equivalent and kept as defenses.
- **Tracking.** Filed [#237] (a rebuild the object graph's walk triggers merges keys the walk has passed, so a first
  validation misses what a second finds), with [#128] in batch 13. Commented: [#167], [#171], [#154], [#188], [#197].

## 2026-10-04 — The design system on the site, the README and the package, outside any milestone ([#235])

- **The question.** Apply the AdCodicem design system, as AdCodicem.ValueObjects did, to the site, the README and the
  package icon; scope and choices settled with the maintainer one question at a time.
- **In the design system first**: the compact Pdf lockups the navbar's 34px calls for, and the AdCodicem lockups with
  their name outlined, for the footer.
- **Done**: the tokens, fonts, syntax palette and four color schemes, with theme and contrast toggles; the Pdf lockup
  and favicons; the documentation under `/docs`, behind a homepage whose example is frozen with each stable line and
  compiles against the current API, every former address redirected; a 404 page; a local search over the user
  documentation, the API reference in an index of its own; the README's banner; the package's icon, the Pdf tile.
- **Decided on a measurement**: the project documents are left out of the search index, which they would take from
  0.7 MB to 12.7 MB, fetched by every reader's first search.
- **Left**: the startup hook failed to install the SDK, the Ubuntu archive's index being stale ([#234]); fixed the same
  day: the hook refreshes the index and retries when the install fails.

## 2026-10-03 — Batch 8: what the parser read in silence ([#119], [#172])

- **The question.** [#119]: an array or a dictionary left open at the end of the data was read in silence; [#172]: so
  were an open string, stray hexadecimal bytes, a name's `#` that is no escape, and a repeated key, a later null keeping
  the earlier value.
- **Settled with the maintainer**, each as recommended: `syntax.truncated-object` for what the end of the data leaves
  open, once per cut, for the innermost construct, where it opens; the parser, told whether its buffer ends the data,
  reports the end only then, which removes a false report at the rebuild's 64 KB edge ([#49]'s) and a false guard cut; a
  key given again keeps its last value, a null given last removing it, every repeat reported, as the ADR index records;
  new codes `syntax.hex-string-invalid`, `syntax.name-escape-invalid` and `syntax.key-repeated`, `#00` staying the
  validator's; a new `syntax.*` code in a trailer or a cross-reference stream's dictionary makes it malformed; bounding
  a member at the next member's offset stays [#160]'s, the validator gaining no rule; four neighbors paid: no message
  formatted once the diagnostics are full, a syntax fault reported once however often it is reparsed, what precedes a
  guard's cut kept (ADR 34), and an `endobj` where a value or a key should be ending every open container.
- **Done**, in [#231]. **Tests**: 153 new, 3,103 to 3,256; of 39 mutants, 37 fail a test, 2 are equivalent.
- **Reviewed.** One review, five lenses: 33 findings, 29 kept whole, 3 in part, 1 refuted, and 2 from a completeness
  critic; fixed among them a regression. Found by mutation after it, and fixed: a stream whose `/Length` the file cannot
  hold silenced a later repeated key.
- **Tracking.** Filed [#229] (a catalog whose own load rebuilds the index is tested by neither search), with [#132] in
  batch 22, and [#230] (an object that lost its closing delimiter and its `endobj` takes the next one,
  `object.endobj-missing` unsaid), with [#174] in batch 16. Left: [#169]'s capacity-0 case. Commented: [#49].

## 2026-10-03 — Weekly previews and versioning, outside any milestone ([#227])

- **The question.** Apply AdCodicem.ValueObjects' preview and versioning pattern (its ADR-0009 and ADR-0010), each part
  justified by a measurement here, not copied. Measured: one preview per push, 20 of 47 with the inputs of the one
  before; numbers taken from the run; no attestation; 610 MB restored while holding `id-token: write`; a stable path,
  never run, that would push to `main` with `GITHUB_TOKEN`, which the ruleset refuses.
- **Settled with the maintainer**, each as recommended: `preview.yml` (`nuget`) and `release.yml` (`nuget-stable`, with
  a reviewer); weekly previews, and a push redeploying the site when nothing that ships changed; a breaking change
  releases a minor while the major is 0; a release App past the ruleset; scripts for several packages from now on; a
  compatibility island of a consumer application and the rolled-forward suite; `Build and unit tests`,
  `Integration tests`, `Conventional commits` and `workflows` as required checks; this issue to track it.
- **Done**: ADR 48 (one version, independent of .NET, `conventionalcommits`, the 0.x rule, Dependabot's lines, the
  island) and ADR 49 (weekly gated previews, publish/repair/none, the pack without credentials, attestation, the site),
  which supersedes ADR 30's preview track and amends ADR 31. Pack of `src/` only, every package attested; the release
  scripts, actionlint and shellcheck on every pull request; Dependabot with a week of cooldown and auto-merge by
  allow-list; `CLAUDE.md`: questions to the maintainer go through `AskUserQuestion`.
- **Reviewed** adversarially by a fresh agent: no defect failing CI, publishing a wrong version or leaking a credential.
- **Left to the maintainer**, before or right after the merge: the `preview.yml` Trusted Publishing policy (before), the
  `nuget-stable` environment and its reviewer, both environments limited to `main` ([#178]), the release App and its
  bypass ([#41]), the required checks ([#41], [#196]); then dispatch `preview.yml` once, check the first preview's
  attestation, and delete the old `release.yml` policy.

## 2026-10-03 — Batch 7: the generation a rebuilt or relocated entry records ([#118])

- **The question.** [#118]: the rebuild's scan recorded every object at generation 0, so a finding located `5 0` an
  object the file writes `5 1 obj`. Two more sites made a generation up: a relocation took the generation of whichever
  reference asked first, and a catalog found by its type went into the trailer as `N 0 R`.
- **Settled with the maintainer**, each as recommended: the scan, the relocation and the recovered `/Root` record the
  header's generation, or the entry's, which pays [#185]'s relocation bullet; the live index gives the entry's — the
  row's for a chain entry, the header's once rebuilt or relocated —, and only a row whose header contradicts it at the
  offset it gives, followed by a rebuild set off elsewhere, names an object two ways, which `xref.generation-mismatch`
  reports; the last definition of a number wins, under its own generation, which one a rebuild keeps staying [#189]'s;
  an in-use generation of 65,535 is kept; [#185] stays in batch 28, `object.redefined` unchanged, noted on [#189];
  [#158]'s `MaxRepairObjects` test is written with [#183] in batch 11.
- **Done**, in [#226]. In the corpus, 6 entries move and no finding, diagnostic or expectation. `lazy-reading.md` says
  which definition a rebuilt map keeps, and which rules name an object from the map, the row or the reference.
- **Reviewed.** One review, five lenses and a completeness critic: 26 findings, each put to a refuter, 17 kept, 9
  refuted. Among the fixes: the probe read a relocated header through 64 bytes only, so a longer one earned no
  `xref.generation-mismatch`; the object stream walk named a stream two ways, a regression of this batch.
- **Tests**: 31 new, 3,072 to 3,103; eight mutants, each failing a test.
- **Tracking.** No new debt. Commented: [#189] (a number reused under a new generation; a last header reading as null
  winning over a readable one), [#185] (its relocation parts paid or now false), [#183], [#128], [#175].

## 2026-10-03 — Batch 6: what the chain refers to while it is read ([#182], [#215], [#216])

- **The question.** [#182]: a cross-reference stream's `/W`, `/Size`, `/Index`, `/Filter`, `/DecodeParms` and `/Length`
  were resolved while the chain was read, and what that load rebuilt, corrected or cached was served as the file's
  index; with it [#215], integral reals read in silence where an integer is required, and [#216], `/W`'s bound of 8.
- **Settled with the maintainer**, each as recommended: while the chain is read nothing loads, a lookup with no side
  effect reading an object only where a section already read places it (its type-1 row, at the exact offset, under the
  same number and generation, within `MaxObjectLength`, not a stream); a value the rows need written as a reference is
  read through it and reported, a Warning, one it cannot read making the section malformed, `/Prev` and `/XRefStm`
  alike; a `/Length` it cannot read takes the data up to its `endstream`, checked once the chain is read;
  `file.trailer-value-wrong`, a new Warning, judges each section's trailer as written, `/Size` staying
  `file.size-wrong`'s; [#215] read as written and reported, `AsInteger` unchanged, a new Warning,
  `xref.object-stream-value-wrong`, for an object stream's `/N` and `/First`, ordinary streams' `/DecodeParms` a debt;
  [#216]: fields wider than 8 bytes read when their excess is zero, a nonzero excess refusing the row alone; a lookup a
  guard cuts is unresolved and reports nothing; the empty chain found a debt ([#218]), with [#132] in batch 22.
- **Done**, in [#225]; `xref.object-stream-value-wrong` takes an object stream's `/Length` too, which the Arlington walk
  never reaches. **Reviewed.** One review, five lenses and a completeness critic: 33 findings, 26 kept and fixed, 7
  refuted. **Tests**: 129 new, 2,943 to 3,072; eight mutants, each failing a test.
- **Tracking.** Filed: [#218], [#219] (ordinary streams' `/DecodeParms` untyped), [#220] (a self-indexed `/Length` past
  64 KB), [#221] (a reserved row type skipped, not read as null), [#222] (a classic section with no subsection), [#223]
  (five manifest entries naming structure their files lack), [#224] (a guard-cut `/Length` called null).

## 2026-10-02 — Batch 5: the numbers read from the file ([#157], [#186])

- **The question.** [#157]: a 19-digit integer wrapped to a value the file chose, a 309-digit real read as an infinity,
  and numbers were narrowed to `int` before their checks. [#186], from M01's review: six in ten reals below 1 read an
  ulp or more off, which M03's slice 1, whose rewrite must not move a value it read, cannot accept.
- **Settled with the maintainer**, each as recommended: the exact fast path, `mantissa / 10^k` when both are exact
  doubles, the framework's parse for the rest; an integer past a `long` reads as the nearest real, with no report,
  `-9223372036854775808` staying `long.MinValue`; a number beyond a double reads as null, reported as
  `syntax.number-out-of-range`, a Warning, an underflow unreported; object numbers 1 to 2,147,483,647 and generations 0
  to 65,535 are internal constants (ADR 34), `/W`'s bound of 8 a debt; a fault in a cross-reference stream's numbering
  makes the section malformed, one in a row refuses that row and reads on, the report left to [#210], free rows exempt;
  an object stream's header ends at a pair no member can have; `AsInteger`'s bound corrected, integral reals still
  admitted; [#158]'s number properties and `/W`'s hostile test paid, the benchmark committed before the change; [#212]
  commented, `DescribeValue` corrected here, the integral reals filed.
- **Done**, in [#217], the code's commit a `feat:`. A refused row stands alone, no older section's row serving the
  object; `reader-limits.md` lists three bounds that cannot be lifted. No manifest entry moves.
- **Reviewed.** One review, four lenses and a completeness critic: 34 findings, 31 kept and fixed, 3 refuted, among them
  a regression of this batch, a refused row letting an older section's row serve a superseded revision.
- **Tests**: 142 new, 2,801 to 2,943; every defense mutated, each mutant failing a test but one equivalent.
- **Tracking.** Filed under M02, paid in batch 6 with [#182]: [#215] (integral reals read in silence as integers) and
  [#216] (`/W`'s bound of 8, unclassified under ADR 34). Commented: [#158], [#202], [#210].

## 2026-10-02 — Batch 4: the budgets the index changes are measured against ([#193])

- **The question.** [#193], from M01's review: no test held M01's budget for a file of several hundred thousand objects,
  nor a time budget for every corpus document; none was stated, and no CI job set a timeout.
- **Settled with the maintainer**, each as recommended: 300,000 objects, through a classic table and a cross-reference
  stream, written by `TestPdfBuilder` with one page; the stream's rows stored by Flate, not compressed, since compressed
  they allocate 0.65 % more under CI's zlib-ng than under the sessions' zlib; `Open` from a `byte[]`, after a first
  opening, measured per thread; the measured figure plus 5 %; each measured `Open` also held to 10 s; 20 s for each
  operation on a corpus document; 15 minutes on `ci.yml`'s jobs, `release.yml`'s two test jobs and the documentation's
  deployment; a CTRF report of the remote corpus's acceptance tests, kept as an artifact; the journal's budget measured
  after a first reading, and M23's account of it corrected; the documentation's bytes per object rewritten as measured;
  [#212] placed in batch 10, with [#190].
- **Done**, in [#214], merged on 2026-10-02: each of five operations on a corpus document held to 20 s; the timeouts and
  the report in four workflows. At 300,000 objects, opening allocates 32.6 MB through the table and 45.2 MB through the
  stored stream; on GitHub's runners each operation stays at least six times under its 20 s.
- **Reviewed.** One review, four lenses, each finding put to a refuter: fixed among others a commit subject over
  commitlint's 100 characters and the journal's first assertion inside its measured span; 2 refuted.
- **Tests**: 2 new, 2,799 to 2,801; the corpus theories now time five operations, each budget failing without its
  defense.
- **Tracking.** Commented, with the measurements: [#180] (a valid table of a million objects opened from a path leaves
  55.9 MB pooled), [#181], [#164], [#62], [#37].

## 2026-10-01 — Batch 3: what a message quotes of the file ([#159])

- **The question.** [#159], from the threat model: messages quoted the file's names and keywords whole and unescaped,
  and numbers under the host's culture. A name of megabytes was copied into every report and kept for the document's
  life; a line feed, an escape sequence or a C1 control reached the host's logs as itself.
- **Settled with the maintainer**, each as recommended: a quote is written as a PDF writer writes a name (ISO 32000-1,
  7.3.5), keywords by the same rule; it is cut past 127 bytes, an internal constant, with ADR 34 amended to say so; the
  cut note gives the whole's length; a stream's length fault keeps what `/Length` held, not words about it; decoding
  stops at the first unknown filter; strings are never quoted; every number of `src/` is formatted with the invariant
  culture, held by a corpus test under a Persian culture; one helper in `AdCodicem.Pdf.IO`, which the validator shares;
  a key path keeps its first and last four steps; the public `ToString`s are left to a debt ([#212]).
- **Done**, in [#213], merged on 2026-10-02: `FileQuote`, a 64 MB name costing its 127-byte quote and no copy, behind
  every message that quotes a name; `filter.unsupported` stops the chain, "decoding stopped there". Each hostile shape
  is held to an allocation budget; no message of the 401 corpus documents changes under the invariant culture.
- **Reviewed.** One review, five lenses, each finding put to a refuter. Fixed: a caller's name past U+00FF cut and
  counted in characters. Refuted as this batch's: a non-name `/Filter` element skipped in silence ([#162], [#209]).
- **Tests**: 246 new, 2,553 to 2,799, 168 of them the corpus theory under fa-IR; each defense fails a test when removed.
  The loop over a chain's filters, rewritten, pays [#158]'s intermediate step cut at the bound.
- **Tracking.** Filed [#212] (the public `ToString`s of `PdfName`, `PdfDictionary`, `PdfStream` and `PdfString` raw and
  unbounded), which [#137] waits on. Commented: [#161], [#175], [#162].

## 2026-10-01 — Batch 2: the reader's codes and positions ([#187])

- **The question.** [#187], from M01's review: five situations raised under codes whose meaning does not cover them, a
  documented code nothing raised, and positions that are no offset in the file.
- **Settled with the maintainer**, each as recommended: a file with no `%PDF-` in its first 4,096 bytes is the new
  `header.missing`, a Repair at its first byte (-1 for an empty source), no longer `xref.rebuilt`; a section `/Prev` or
  `/XRefStm` names outside the file is one `xref.section-missing`, a `startxref` outside it the rebuild alone, and
  `xref.entry-out-of-range` an object's entry only, with no position; an offset outside the file is placed at what named
  it, the offset in the message, `xref.chain-cycle` adding the header's offset; a section that is there and cannot be
  read is the new `xref.section-unreadable`, a Warning at the section, its fault in the message, relocation left to
  [#188]; a family for object streams, `object-stream.member-moved` (Repair) and `object-stream.unreadable` (Warning),
  placed where the stream's data starts, as is a fault met inside a member, the stream, the object and the byte of
  decoded data in the message; each reported once, a misplaced member per stream and number, a stream's own fault per
  stream; `object.redefined` raised once per rebuild, as Information with no position.
- **Done**, in [#211]: `/N` and `/First` checked as `long` before any narrowing, an explicit `/N 0` an empty stream; a
  section a guard cut, and what the parser met at a guard's cut, are the guard's to report (ADR 34).
- **Reviewed.** One review, four lenses: 31 findings, 26 kept, 5 refuted. **Tests**: 49 new, 2,504 to 2,553.
- **Tracking.** Filed under M02: [#207] (a table the end of the file cuts before its trailer, read in silence), [#208]
  (the chain stops at an unreadable cross-reference stream whose `/Prev` is known), [#209] (`filter.unsupported` with no
  position), [#210] (a section read only in part, reported nowhere); [#137] waits on them. Commented: [#125], [#157],
  [#171]. M02's checklist ticks M01's review, merged with [#198].

## 2026-10-01 — Step 4's order, and its first batch: a reference to object 0 ([#117])

- **The question.** The maintainer asked for the next step to begin and every ambiguity to be put to them. Analysts read
  the 46 debts under M02, none stale; a critic moved [#187], [#159], [#193] and [#182] before what rests on them.
- **Settled with the maintainer**, each as recommended: the thirty-four debts filed since 2026-09-29 join the twelve
  already in step 4, interleaved by shared code and dependencies, the denial-of-service debts brought forward, in
  thirty-two batches; batches by code path, one pull request each, this session paying the first alone; a batch's design
  questions put when it starts, after measuring, not all 113 at once; a batch that can move what a remote document gives
  runs `Remote corpus` on its branch before it merges; [#158]'s tests written, as `Refs #158`, in the batch that changes
  their code, [#158] closing last; a defect found outside every issue reproduced, then filed as debt; for [#117], both
  of the validator's "no reference" sentinels replaced.
- **[#117].** `0 0 R` read as two integers and a stray `R`. It is now a reference to object 0, which heads the free list
  and is never in use (ISO 32000-1, 7.5.4), and reads as null; object 0 is held missing whatever the table says.
- **Reviewed.** One review, four lenses: fifteen findings, none refuted, coming to seven, among them a false
  `xref.object-stream-circular`. **Tests**: sixteen new, each failing when its defense is removed.
- **Tracking.** [#144] blocks slice 4 ([#60]); [#137] waits on every issue under M02. Filed under M02: [#199] (a false
  `filter.failed` after a step the guard cut), [#200] (object-stream members one byte apart, quadratic), [#201] (the
  dependency walk again for every stream), [#202] (a member the reader gives up on, reported by no rule).
- **Left**: the journal summarized after the merge, which found [#50]'s closing recorded nowhere and M23 still planning
  it ([#205], under M23). Filed [#204], under no milestone: the remote fetcher ignores `Retry-After`, logs no failed
  attempt and caches nothing between runs, a cache being the maintainer's call under ADR 32.

## 2026-10-01 — M01's review after the fact ([#136])

- **What it is.** The adversarial review ADR 46 asks of every milestone, run on M01 after the fact, under M02, by a
  session that worked on none of it. The report is [`docs/reviews/M01.md`](reviews/M01.md).
- **Counts.** Pass 1, blind, gave 107 candidates; pass 2 dropped 37 and merged the rest into 20; refutation refuted
  none, and two splits and one finding after the triage made 23: 9 fixed here, 11 filed under M02 in nine issues, 2
  under later milestones, 1 under none, none put to the maintainer.
- **Fixed in the review's pull request**: one public type per file (R-08); analysis back at `latest-recommended` (R-09);
  the core's `InternalsVisibleTo` grant to the HTML assemblies removed (ADR 36, R-10); the tests M01 lacked (R-16); the
  documents brought in line (R-14, R-18 to R-20); `docs.yml`'s checkout drops its token beside `pages: write` (R-21).
- **Filed under M02**, which now waits on them: [#185] (a reference's generation compared with nothing), [#186] (reals
  not read as the nearest `double`; it blocks M03's slice 1), [#187] (codes outside their meaning), [#188] (a relocated
  section landing on one already read), [#189] (a rebuild keeping an older direct definition), [#190] (`PdfDocument`'s
  version, stream position and behavior after `Dispose`), [#192] (LZW decoding allocating per code), [#193] (no budget
  for a large index or a full read), [#197] (the nearby search near the file's start).
- **Filed elsewhere**: [#191] under M03, before [#35] records the API baseline; [#194] (the fuzzing's repeated seeds)
  under M23; [#195] (unhashed Python pins) under none; [#196] (Dependabot's auto-merge fails open), outside the extent
  and not counted, under none at the maintainer's choice. The threat model's gaps name every issue the review filed but
  [#186] and [#187], which are no defense.
- **Decided by the maintainer**: the triage as proposed; [#186] and [#192] under M02 rather than M03 and M23; `M01.md`'s
  first acceptance row aligned with the manifest's floor rather than the test tightened.

## 2026-10-01 — The threat model's first version ([#135])

- **What it is.** `docs/threat-model.md`: assets, attackers and trust boundaries; shared defenses; a section for each
  surface the reader and the validator open, each defense naming where it lives, the test that fails if it goes and
  whether the fuzzing reaches it; the supply chain; the twenty-nine surfaces still to come; what is out of scope.
- **How it was checked.** Every gap suspected was reproduced against the Release build, or traced in the code, and
  compared with the issues; what survived went into thirty issues, none fixed here, as #135 asks.
- **What it found**, the worst first. Few-hundred-byte files making the reader hold gigabytes: [#166], [#164], [#165],
  [#170], [#160]. Work beyond the file: [#155], [#181], [#168]. Wrong answers: encryption lost on a rebuild ([#154]),
  numbers that wrap ([#157]), a header found inside stream data ([#171]), filter parameters decoded wrong ([#162]), a
  guard's report lost once the diagnostics are full ([#169]). Untyped exceptions ([#167]). Tests weaker than their names
  and fuzzing that reaches no guard ([#158], [#176]). Supply chain: release jobs building with publish rights ([#177]),
  nothing tying publishing to `main` ([#178]), a dependency-free core held by review alone ([#179]).
- **Fixed in passing**: four internal constants give their ADR 34 reason where they are declared; `SECURITY.md` no
  longer calls the dependency versions locked ([#43] is open); two documents corrected.
- **Decided by the maintainer**: the reader's and the validator's debts go under M02, not M23; `MaxDepth` and
  `MaxNestedLoads` become guards, since a valid file can exceed both ([#163]). Proposed and accepted: the fuzzing gaps
  under M23, beside its coverage-guided fuzzing ([#176]); the pooled buffers under M16, where decryption brings secrets
  into them ([#180]); the three supply-chain issues under no milestone.
- **Comments**: on [#37], a cached object weighs about thirty times its syntax; on [#53], a length between
  `Array.MaxLength` and `int.MaxValue` throws an `OutOfMemoryException`.

## Earlier, in brief

The detail is in git and in the pull requests; what still matters is in the records and in this file.

- **2026-09-12 and 13 — foundations and M01.** Nineteen decisions (now ADRs); M01 in seven slices; English throughout;
  acceptance on real documents, expectations from independent tools, never from our reader; two test levels, the second
  running qpdf in a container; a Docusaurus site; warnings as errors. Milestones renumbered since (`docs/roadmap.md`).
- **2026-09-14 — M01 closed.** Fuzzing found a process-killing recursion; relocation became three counted attempts.
- **2026-09-15 and 16 — publishing.** Previews on every merge, stable releases by hand (ADR 30); `v0.1.0` the baseline.
- **2026-09-19 — first packages and the supply chain.** The first previews reach nuget.org through trusted publishing.
- **2026-09-22 — the site.** Repaired, each page checked; versioned docs (ADR 31); a ruleset on `main`.
- **2026-09-24 — the corpus from public sources.** Rules on names relaxed with the maintainer; a remote corpus for what
  may be used but not redistributed, or over 2 MB (ADR 32); one manifest. T21, T23 and T24 found.
- **2026-09-25 — more corpus, and fuzzing.** The OPF format-corpus screened; the iPRES 2017 set (ADR 33); nightly
  fuzzing from one document per reader structure plus a rotating share. T25 to T27 found.
- **2026-09-26 — T21 and T23.** A too-small window no longer leaves reports behind. T29 fixed; T30, T31 found.
- **2026-09-26 — the reader's guards, validation's first rule, and three reader debts.** ADR 34 and invariant 12: every
  valid PDF is readable, the five guards are `PdfReaderLimits` a caller may raise, each under its own `limit.*` code;
  ADR 35: measured unsafe code; ADR 36: validation in the core, `PdfValidator`, `file.eof-missing`; previews carry no
  guarantee (ADR 30); a manifest JSON schema; CodeQL left to GitHub's default setup. T31, T32, T25: a filter's 256 MB
  bound, a lost Flate tail and an undefined LZW code reported; a section `/Prev` misses found within 512 bytes.
- **2026-09-26 and 27 — the roadmap revised from a feature survey.** 32 milestones, M00 to M31, numbered in working
  order, the case file before the HTML engine; ADRs 37 to 44; 173 open points settled, sixteen by the maintainer's
  decisions D1 to D13, American spelling among them (ADR 20, amended); `Comparison benchmarks`' first run left open.
- **2026-09-27 — T27, and M02's second slice.** A reference to an object the file lacks reads as null, silently; `/Prev`
  and `/XRefStm` are direct integers only. Slice 2 ([#58]): twenty `file.*` and `xref.*` rules held to qpdf. ADR 45, the
  maintainer's: an error says the reader cannot vouch it reads the file as written, a warning that it read it as
  evidently meant; no `Critical` before M05 ([#109]); left silent: [#107], [#108] (M23), [#111].
- **2026-09-27 — tracking on GitHub, and a disclaimer.** `docs/roadmap.md` stays the reference, `Tracking` mirroring it
  as milestones, never dated; slices become `slice` issues when a milestone starts, debts `debt` issues ([#34] to [#56])
  and wanted documents `help wanted` ones, which `stale.yml` spares; open questions discussions. At the maintainer's
  request, a `Disclaimer` in the README and on the site; the roadmap called an intention.
- **2026-09-28 — the project's coverage, and Codecov's rules.** Settled with the maintainer: the project may lose half a
  point at most, each patch 95 %, `tests/` unmeasured; code goes only when no input can reach it, never because no
  corpus file does; a defensive branch stays; a public member nothing calls is asked about.
- **2026-09-28 — four reader fixes on `main`, beside slice 3.** An object read once whatever generation names it;
  `syntax.depth-exceeded`; decoded object streams kept to 32 MB in all (T33, [#50]); a misplaced member found by lookup.
- **2026-09-28 — M02 slice 3: the object, page tree and Arlington rules.** Settled after measuring: one finding per
  fault, hand-written over generated rules; missing references and page-tree faults warnings, a loop an error, a page
  left out information; qpdf's page count. Arlington rules generated, MIT kept (ADR 44, amended), overridden where
  ISO 32000-1 is looser; a key newer than the file's version to [#123]. Filed [#117] to [#120], [#121] (M23).
- **2026-09-29 — slice 3 merged, and the rest of `src/` covered.** The maintainer's coverage rule: 100 % of each patch
  the aim, 95 % the floor, every non-private member covered, no private member or compiler branch tested for its own
  sake. Filed [#125], [#126], [#128], [#129].
- **2026-09-29 — what remains of M02, and how work waits and is reviewed.** Settled in M02's review with the maintainer
  ([#133]): every issue under M02 paid in it, [#123] moved to M20, the Arlington overrides confirmed (ADR 44); [#55]
  with [#120], [#56], ten debts, then slices 4 to 6, a pull request per batch; the two [#47] documents skip only the
  laziness test; `QpdfRefereeTests` counts damaged trees' pages too; an issue's body declares what it waits on, which
  `Tracking` mirrors; a `help wanted` issue goes under the first milestone it unblocks that has not started. ADR 46:
  every milestone ends with an adversarial review by a fresh session, its last slice ([#137] for M02).
- **2026-09-29 — a stream's length past the window ([#55]), said as the file wrote it ([#120]).** Merged with [#140] on
  2026-09-30. A wrong `/Length` reads to the first `endstream`, within bounds no read changes, as the maintainer settled
  once reviews found order-dependent lengths; no guard (ADR 34); a header with a comment between its tokens not taken
  (maintainer, 2026-09-30), taken since batch 19 ([#261]). Order-dependent crafted files: [#138] (M23).
- **2026-09-30 — a damaged Flate stream keeps what it decoded ([#56], T40).** The maintainer settled: data that faults
  is read again after the fault only, whole when only the checksum disagrees; a managed inflater and smaller reads
  turned down; a new warning, `filter.checksum-mismatch`; corrupt data stays `filter.failed`; no new bound (ADR 34).
  Merged with [#147]. Filed [#141] to [#146].
- **2026-09-30 — the user documentation along Diátaxis ([#148], ADR 47).** Each choice put to the maintainer: four
  sections, one mode per page; a tutorial's code a tested sample; the rules table in the reference, versioned; the API
  under `reference/api`, old addresses redirected. Filed [#149]: `Open(Stream)` copies a seekable stream into memory.

[#34]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/34
[#35]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/35
[#37]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/37
[#41]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/41
[#43]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/43
[#47]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/47
[#49]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/49
[#50]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/50
[#52]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/52
[#53]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/53
[#55]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/55
[#56]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/56
[#58]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/58
[#60]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/60
[#61]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/61
[#62]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/62
[#107]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/107
[#108]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/108
[#109]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/109
[#111]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/111
[#117]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/117
[#118]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/118
[#119]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/119
[#120]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/120
[#121]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/121
[#123]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/123
[#125]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/125
[#126]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/126
[#128]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/128
[#129]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/129
[#132]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/132
[#133]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/133
[#135]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/135
[#136]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/136
[#137]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/137
[#138]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/138
[#140]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/140
[#141]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/141
[#144]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/144
[#146]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/146
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
[#204]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/204
[#205]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/205
[#207]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/207
[#208]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/208
[#209]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/209
[#210]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/210
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
[#240]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/240
[#241]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/241
[#247]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/247
[#250]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/250
[#251]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/251
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
[#345]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/345
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
[#360]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/360
[#361]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/361
[#362]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/362
[#363]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/363
[#365]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/365
[#366]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/366
[#367]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/367
[#368]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/368
[#369]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/369
[#370]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/370
[#371]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/371
[#372]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/372
[#373]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/373
[#374]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/374
[#375]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/375
[#376]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/376
[#377]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/377
[#378]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/378
[#379]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/379
[#380]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/380
[#381]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/381
[#382]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/382
[#384]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/384
[#385]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/385
[#386]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/386
[#387]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/387
[#388]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/388
[#389]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/389
[#390]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/390
[#391]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/391
[#392]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/392
[#393]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/393
[#394]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/394
[#395]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/395
[#396]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/396
[#397]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/397
[#398]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/398
[#399]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/399
[#400]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/400
[#401]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/401
[#402]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/402
[#403]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/403
[#404]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/404
[#405]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/405
[#406]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/406
[#407]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/407
[#409]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/409
[#410]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/410
[#411]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/411
[#412]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/412
[#413]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/413
[#414]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/414
[#415]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/415
[#416]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/416
[#419]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/419
[#420]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/420
[#421]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/421
[#423]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/423
[#425]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/425
[#426]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/426
[#427]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/427
[#428]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/428
[#429]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/429
[#430]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/430
[#431]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/431
[#432]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/432
[#433]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/433
[#434]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/434
[#435]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/435
[#293]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/293
[#436]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/436
[#437]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/437
[#438]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/438
[#439]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/439
[#440]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/440
[#449]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/449
[#450]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/450
[#451]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/451
[#452]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/452
[#453]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/453
[#454]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/454
[#422]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/422
[#424]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/424
