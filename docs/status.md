# Project status

A living file, updated **at the end of every session**. It describes the real state, not intentions.
Keep it short: summarise the journal once it passes a dozen entries — the detailed history is in git, not
here.

## At a glance

- **Current milestone**: M2 — Document validation (`docs/milestones/M2.md`), not started
- **Last milestone closed**: **M1 — Object model and tolerant reading**
- **Builds**: yes, with no warnings — **Tests**: 620 unit (5 skipped by design: two corpus
  documents recorded as unsupported until M2, and the negative control of `readerLimits`, which has no
  document without the remote corpus) + 302 integration (skipped without Docker) + 23 for the remote
  corpus's fetcher (Python, against a local server); with the remote corpus fetched (240 of its 242
  documents from here on 2026-09-26, all 242 on the runner), 1,281 unit here — 1,266 on the runner, before
  the coverage tests — (61 skipped by design, on documents recorded as unsupported until M2 or until T24
  or T25 is fixed) + 668
  integration, run 6 of `Remote corpus`, dispatched on the ADR 34 branch on 2026-09-26 — **CI**:
  green on `main`, `OpenSSF Scorecard` included
- **Corpus**: 168 committed documents, 23.0 MB — 19 generated here, 3 from Word and PDF24 on Windows, 146
  third-party files under attribution-only licences (56 of them from the Open Preservation Foundation's
  format-corpus, added on 2026-09-25; see `docs/corpus-sources.md`). Beside it, a **remote corpus** of 242
  documents we may use but not redistribute — never committed, fetched at a pinned SHA-256 and size, 88 of
  them copied out of their authors' archive (the iPRES 2017 hand-built set, ADR 33), and tested every night
  by the `Remote corpus` workflow (ADR 32). All 410 are described in one file,
  `tests/corpus/manifest.json`. Pull request [#25](https://github.com/AdCodicem/AdCodicem.Pdf/pull/25),
  which brought the remote corpus, was merged on 2026-09-24. Its first nightly run, on `main` on
  2026-09-25, passed every test (538 unit, 361 integration) but failed its last step: the Internet
  Archive's copy of the Massachusetts COVID-19 dashboard timed out three times, so it ran without that
  one document. The same file downloads without trouble from here; one slow night is not yet a trend.
  Run 2, dispatched by hand on `claude/format-corpus-integration-udjoo6` the same day, fetched all 153
  documents and passed 806 unit
  tests (35 skipped by design) and 563 integration tests
- **Supply-chain score**: **6.6/10** as published on `6bacbb2`, up from 5.5. The branch below is measured
  to take it to **7.1**; everything above that needs repository settings or people, not code — see T15,
  T18 and T19
- **Pull requests**: versioned documentation (ADR 31) is [#23](https://github.com/AdCodicem/AdCodicem.Pdf/pull/23),
  merged once green: after it, every merge redeploys the site.
  [#22](https://github.com/AdCodicem/AdCodicem.Pdf/pull/22), the site repair, is merged and deployed.
  [#11](https://github.com/AdCodicem/AdCodicem.Pdf/pull/11), [#1](https://github.com/AdCodicem/AdCodicem.Pdf/pull/1)
  and Dependabot's action bumps are merged too; the preview/stable release split (ADR 30), the baseline fix
  and the Scorecard action ref are all on `main`
- **`main` has a ruleset since 2026-09-22** ("Default": pull request with one code-owner approval, linear
  history, CodeQL, coverage). Only repository admins bypass it — and the stable release pushes its
  `chore(release)` commit to `main` as GitHub Actions. **The first stable release will fail at that push**
  until the ruleset grants GitHub Actions a bypass, as T15 said it would have to. Nothing is published
  when it fails: the push comes before the packages do
- **Tagged**: `v0.1.0` on `2808d2f` — the starting point semantic-release continues from. No package exists
  for it, by design.
- **Nothing on `main` is red any more.** The publishing-identity guard that used to stop `Release` (T11)
  went with the merge of #11, and `Documentation` deployed successfully on 2026-09-19 (run 2).
- **The site at <https://adcodicem.github.io/AdCodicem.Pdf/> works**, for the first time: until 2026-09-22
  every user-facing page showed its own compiled JavaScript as text. Repaired by #22 and redeployed by
  hand the same evening; the site check passes on all 74 published pages. **In flight**: versioned
  documentation — stable lines behind a selector, the latest preview behind a button, redeployed with
  every preview (ADR 31).
- **`OpenSSF Scorecard` is fixed and proven**: the `v2.4.4` pin merged, run 13 went green, and
  `api.scorecard.dev` now serves a report — so the README badge finally has a number behind it.
- **Published**: [`AdCodicem.Pdf`](https://www.nuget.org/packages/AdCodicem.Pdf) is on nuget.org —
  `0.1.1-preview.10` through `0.1.1-preview.20` at least (2026-09-22), previews from `main`, the first packages this project has
  ever shipped. Trusted publishing works end to end; nothing long-lived is stored anywhere.
  `dotnet add package AdCodicem.Pdf --prerelease`
- **No stable release yet**: `v0.1.0` is a tag with nothing behind it, by design, and the stable path has
  still never run. Every merge into `main` now publishes a preview on its own.
- **Coverage**: 82.61% on Codecov, linked and uploading without a token. Its GitHub App is not installed,
  which Codecov warns about on every pull request (T14).
- **Branches**: five merged pull requests left their head branches on the remote.
  `claude/dependabot-prs-review-p3mx79`, `claude/scorecard-pipeline-47mgrj` and
  `claude/scorecard-improvement-4ckfxd` hold nothing `main` does not, and are deletable as they stand.
  `claude/nuget-pdf-html-dotnet-msyz8z` and `claude/package-preview-deployment-h0lakt` each kept commits
  pushed after their merge; what was still true on them is in this change, so they are deletable too once
  it lands

### Current measurements (BenchmarkDotNet, ShortRun)

| Operation | Document | Time | Allocated |
|---|---|---|---|
| Indexing | synthetic, 1000 pages, ~4 MB | 229 µs | 393 KB |
| Indexing, then reading every page | synthetic, 1000 pages, ~4 MB | 6.2 ms | 5.9 MB |
| Indexing and walking the page tree | real ReportLab document, 1000 pages | — | 2.4 MB |

The gap between the first two rows is the library's promise: opening a document does not read its content.
Indexing costs roughly 200 bytes per object, whatever the objects weigh. The third row is asserted as a
budget in CI (`CorpusReadingTests`), so an allocation regression fails the build.

## Next concrete step

M2 — document validation (`docs/milestones/M2.md`). Its first slice is the findings, the report and the
rule engine end to end with a single trivial rule; the acceptance to keep in view is that no well-formed
corpus document from any of the four producers earns an error-severity finding, and that a PDF/A-invalid
file earns no *structural* one. T21 and T23 are fixed, so an object's diagnostics no longer carry syntax
errors its window invented (the rebuild's trailer scan still reads through a fixed window, T30). T25, which
M2's exit criteria name, and T27, which its acceptance conditions name, remain, and belong with its
cross-reference and object-graph slices. T31 is fixed, and ADR 34 made the reader's bounds options
(`PdfReaderLimits`), each reported under its own `limit.*` code: M2's stream rules must read such a code as
the reader's limit, not a fault of the file. **T32** belongs with M2's stream rules at the latest: a Flate stream
whose tail was lost decodes to what is left, without a word.

**The milestones were renumbered** when validation and repair were inserted: validation is now M2 (right
after reading) and repair M4 (right after writing). Numbers in commits older than 2026-09-13 refer to the
previous ordering, where M2 was writing and M3 assembly.

## Journal

### 2026-09-26 — Every line of #28's change is covered
- **The report.** Codecov found ten lines of the pull request's change that no test reached (97.3 % of the
  patch). Measured the same way here — the unit suite with CI's coverage command, without the remote
  corpus, intersected with the lines the change adds — it gave the same ten.
- **One was unreachable, and went**: a cut trailer's re-read tested an offset that is inside the file by
  construction, for any source whose length holds.
- **The rest were behaviour without a test**, and have one: an LZW stream stopping at a code it has not
  defined; an empty stream with a predictor, which must not be taken for rows too long for their data; the
  predictor transform left alone without a predictor; a stream cut with nowhere to report; a guard at the
  most the reader can hold; a chain of lengths running too deep — into an object with no offset (free,
  unlisted, compressed), into one with an offset, bytes before the header included, and twice, reported
  once —; and a keyword the section's probe saw as `xref` that is `xrefs`. Each test fails when its branch
  is mutated (thirteen mutations). The LZW case is silent corruption, like a Flate stream's lost tail, and
  is recorded under T32.
- **On review.** Three reviewers in their own worktrees, each finding checked by two more. The first
  version also dropped the pipeline's own check for a stream without a predictor, as a duplicate of the
  transform's — and it was not one: without it, `/Colors`, `/BitsPerComponent` and `/Columns` were read,
  and resolving a reference there could rebuild the index, throw a guard or change what other objects read
  as. The check is back, with a test that a parameter nothing reads is not resolved; the transform's own
  guard is tested directly. Also from the review: nothing asserted where a too-deep object with an offset
  is reported, and the probe's length is now a named constant the test is built from. Found on the way,
  older than the pull request: **T34**.

### 2026-09-26 — ADR 34: every valid PDF is readable, and the reader's guards are options
- **The rule.** Asked whether the 256 MB decoding bound came from the specification — it does not —, the
  maintainer set one: every PDF valid under the specification must be readable; guards may protect against
  the exceptional cases it allows, and options must be able to lift them. ADR 34 records it, and makes it
  invariant 12. ADR 35, accepted alongside, allows unsafe code where a measurement asks for it; no code uses
  it yet, and `AllowUnsafeBlocks` stays off.
- **What implements it.** `PdfReaderLimits`, an immutable record on `PdfReaderOptions.Limits`, holds the
  five bounds a valid file can exceed: what a stream decodes to (256 MB), an object's length (16 MB), a
  classic cross-reference section's length (64 MB), the number of sections (1,024), a trailer's length
  (64 KB). `Default` keeps them, `Unbounded` takes them to `Array.MaxLength` and `int.MaxValue`; zero or
  less is refused, more than an array holds is taken as that. Reaching one keeps what fits and warns under
  its own code — `limit.decoded-stream`, `limit.object`, `limit.xref-section-length`,
  `limit.xref-section-count`, `limit.trailer`, replacing T31's `filter.limit-exceeded` before any release —
  whose message ends "Raise PdfReaderLimits.<property> to read past it." `PdfReaderOptions.ThrowOnLimit`
  makes it a `PdfLimitExceededException` instead, carrying the code, the property, its value and the
  offset. A stream read from a document carries that document's guard, so it decodes under its limits
  however long after opening, and reports to the document's diagnostics when its caller passed none.
- **Details that decide behaviour.** What the parser met where a guard cut an object is the reader's, not
  the file's, and is dropped for the guard's own report. An object, a section or a trailer is reported once,
  however often it is read again; with `ThrowOnLimit` it throws each time. A rebuild that reaches a guard
  finishes its index before throwing, since a rebuild is never run twice. `PdfDocument.Open` releases a
  source it was given to own when it throws — which it failed to do before on an empty input.
- **Bounds that were not bounds.** A cross-reference stream was parsed through one 64 KB window that never
  grew: a dictionary past it — a long `/Index` — sent the file to a rebuild without a word; it now grows up
  to `MaxTrailerLength`, a cross-reference stream's dictionary being its trailer. The section count was
  reported as `xref.chain-cycle`, a loop it is not. A classic table cut at its maximum was reported not at
  all, and one whose `trailer` keyword the maximum cut was taken for a malformed table. A trailer cut by a
  table at its maximum was kept cut, where a smaller window would have re-read it through its own.
- **The corpus.** A manifest entry may give the limits it is opened with, in `readerLimits`, beside
  `expect` — a setting, not an observation —, modelled with unknown keys refused. The USGS topographic map
  is opened with `maxDecodedStreamLength` at 512 MB and reads whole and clean: object 155 decodes to
  328,608,000 bytes. It loses its unsupported mark, and meets the laziness test for the first time: opening
  reads 83 KB of its 63.1 MB. A negative control on the remote corpus opens every such entry
  with the defaults — each limit reached must be one its entry raises — and with `ThrowOnLimit`, which must
  throw from `Decode`, after `Open` succeeded. Fuzzing seeds leave such entries out, their budgets holding
  under the defaults.
- **Tests.** `ReaderLimitsTests`, 40 cases: each guard reached (what is kept, the code, the
  message, the offset, and nothing else reported where the parser would have), raised (read whole, nothing
  reported) and thrown (from `Open` or from the later operation, with the exception's four properties);
  a stream decoded without diagnostics; an object read again after the cache let it go; a rebuild that
  reaches a guard while expanding object streams, and while looking for the catalogue; an object read no
  further than its bound; the table's keywords and its trailer at the bound; the presets, the refusals and
  the clamp, and an FsCheck property over every `int`. Thirty-five mutations — each report, bound, preset,
  refusal, deferral and fallback — each fail a test; two needed a test of their own (a bound that falls
  where the parser would report a cut, a length stated in kilobytes), and five were rewritten to compile.
  The catalogue search caught a defect
  of its own before any commit: `reached ??= FindCatalog()` skipped the search whenever the expansion had
  already reached a guard.
- **The remote corpus.** Run 6 of `Remote corpus`, dispatched on the branch on 2026-09-26, fetched all 242
  documents and passed 1,266 unit tests (61 skipped by design) and 668 integration tests: the map reads
  whole on the runner too, and the negative control holds there. It is the first run to include T21, T23,
  T29 and T31's fixes as well.
- **Measured.** The USGS map under 512 MB: opening reads 83 KB of 63.1 MB; decoding every stream takes
  1.8 s and allocates 1,785 MiB, its image's 328,608,000 bytes passing through an output that doubles to the
  bound and is copied out — memory for M13's budgets (T28, T33). The reader's benchmarks allocate what they
  did (392.86 KB to index 1,000 pages, 5,964.54 KB to read them); their times, on a short run, stay within
  its spread.
- **Documentation.** ADR 34 and 35, and the ADR index; `CLAUDE.md` (invariant 12, invariants 4 and 5
  qualified, a convention on unsafe code); `ARCHITECTURE.md`, `docs/architecture.md`, `SECURITY.md` (what
  "without bound" means once limits can be raised); the site's new *Reader limits* page, *Diagnostics*,
  *Lazy reading* and the introduction; `docs/corpus.md` (whose example still showed fields the model never
  had), `docs/corpus-contributions.md`, `docs/corpus-sources.md`, `tests/corpus/README.md`; the M2 stream
  rule and M13's deliverables in `docs/roadmap.md`; T28, T30, T31 and T33 below.

### 2026-09-26 — T31: every filter keeps the bound, and says when it reached it
- **The defect.** `PdfFilterLimits.MaxDecodedLength`, 256 MB, was kept by two filters of five. RunLength
  had no bound: two bytes in decode to 128 out, so 4.6 MB decoded to 294 MB, and a Flate stream feeding it
  multiplied that by 64. ASCII85 had none either (a `z` is four bytes). LZW stopped at the bound without a
  word. Flate reported the bound as damage — "a Flate stream was truncated" — which is what made the
  USGS map, a sound file, look broken (T28). And each decoder sized its first buffer as a multiple of its
  input, a length the file chose, whatever the bound.
- **The fix.** Every filter keeps exactly the first 256 MB and says whether it had more. The four that can
  expand their input — Flate, LZW, RunLength, ASCII85 — write into one bounded output, which grows to the
  bound and no further and hands back a full buffer without copying it; ASCIIHex, whose output is at most
  half its input, fills a fixed array capped at the bound. The pipeline reports reaching the bound under a
  new code, `filter.limit-exceeded`, a warning worded as the reader's limit — decoding stopped there —;
  `filter.failed` is left to data that is corrupt. The bound is a parameter inside the library, so the
  tests run at a kilobyte rather than decoding 256 MB each time.
- **Tests.** Each filter swept from a one-byte bound to past its whole output — Flate through its zlib, raw
  and white-space paths, empty inputs of every filter —: the first bytes kept, exactly, the bound reported
  once, only when met, beside what the data earns unbounded; a chain of Flate and RunLength that cannot
  multiply; a corrupt Flate stream told apart from one reaching the bound; each expanding filter's first
  buffer measured against a megabyte of input, and its whole allocation against a guess that lands 4 bytes
  short of a 4 MB bound; a predictor keeping whole rows of a bounded output; three predictors whose rows
  overflow; the bounded output's own rules; and T31's own file, 4.6 MB of RunLength decoded to exactly
  256 MB at the real bound, with the message a user reads. Twenty-five mutations — eleven of the first
  version, fourteen of the reviewed one, each filter's bound, the report, the bounded output's growth, the
  predictor's checks — each fail a test.
- **The map.** Object 155 of the USGS map now decodes to exactly 268,435,456 bytes with one
  `filter.limit-exceeded`, where it gave 268,429,626 and a truncated-stream warning. It stays unsupported
  for T28, whose remaining half — decoding such a stream a piece at a time — is M13's.
- **On review.** Three reviewers went over the commit; of their twelve findings, each checked by two
  others, eleven held outright and the twelfth — the doubled buffer — was judged a cost rather than a
  defect by one of its checkers, and is fixed all the same. The bound
  capped a decode's output, not its memory: the framework's buffer doubled past the bound, and a write of
  nothing into a full one doubled it again — T31's own file allocated 809 MiB for 256 MB, a 1 MB Flate
  stream 1,276 MiB. The bounded output allocates 528 and 508 MiB for them (measured), in arrays that double
  up to the bound, go straight to it once within a quarter of it, and are handed back without a copy. Older than T31 and reachable from it: the predictor that
  follows Flate and LZW took its row length from /Columns unchecked, so a 413-byte PDF hung
  `PdfDocument.Open` for ever through its cross-reference stream (a row length that overflowed to zero),
  another threw `OverflowException`, and a 12-byte stream allocated 512 MiB. The row length is now
  computed without overflow and weighed against the data first; parameters describing rows the data
  cannot hold leave it as decoded, with `filter.failed`. The report's wording, "the first 256 MB were
  kept", was wrong after a predictor, which keeps whole rows; it now says decoding stopped there.
- **Found on the way.** **T32**: a Flate stream whose tail was lost decodes to what is left, without a
  diagnostic. Measured: a zlib stream cut in half decodes 27,939 of its 58,890 bytes and reports nothing,
  because .NET's inflater returns the end of its input as the end of the data; a complete but wrong
  Adler-32 trailer is caught, a missing one is not. **T33**, from the review: every decoded object stream
  stays in the reader's cache for the life of the document, so four object streams that each decode to
  the bound hold 1 GB once `Open` returns.

### 2026-09-26 — T21 and T23: what a window too small for its object saw is dropped with it
- **The defect.** The reader parses an object through an 8 KB window, and parses it again through one eight
  times larger when the object runs past the edge. The second attempt read the object right; what the
  first had reported stayed in the document's diagnostics — a truncated stream (T21), a truncated object or
  unexpected tokens where the window had cut a token (T23) — and any real anomaly in an object's first
  8 KB was reported once per attempt. Reading the code for the fix showed worse: some cuts were never
  noticed, and the object was kept cut without a word — a top-level string or name longer than the window,
  a reference cut after `12 0`, a `stream` keyword or the CR LF after it across the edge (the stream came
  back a dictionary, or its data one byte early). And a classic table's window never grew for what ended
  it: a trailer across the edge of the table's 64 KB window lost its `/Root` or `/Prev`, and a `trailer`
  keyword or subsection header cut there made the whole table unreadable, so a sound file was rebuilt.
- **The fix.**
  - The parser reports into a pending buffer (a `PdfDiagnostics` with internal marks), and only the
    attempt that is kept reaches the document's diagnostics. Nested loads — an indirect `/Length` resolved
    mid-parse — still report straight to the document, so a repair they make is never lost with the
    attempt that asked for it. The buffer is bounded like the report, suppressed counts included, and costs
    no allocation per object.
  - The parser says when the end of its buffer may have cut something short: a value that touches it, a
    reference whose look-ahead ran out, a value followed by the end or by a token that touches it (`stre`
    of `stream`), a buffer that ends on `stream` or on the CR of its CR LF.
  - A stream whose data ends inside the window, but whose `endstream` may lie past it, is confirmed by
    asking the file for the 13 bytes after the data — not through a window eight times larger.
  - A classic table's window grows when a token reaches its edge, and a trailer the edge cut is parsed
    again where it starts, through a window of its own that stops at 64 KB.
  - An object header the window's edge cut — past more than 8 KB of white space — is read again in a
    larger window, not searched for nearby and then rebuilt.
  - A stream whose declared length the file cannot hold is no longer quietly shortened: the parser asks for
    a window that reaches the end of the file and reports what it finds there — `stream.truncated` when the
    file ends inside the data, `stream.length-invalid` when `endstream` comes first.
- **Tests.** `WindowEdgeTests` slides nine objects across the window's edge one byte at a time — a
  dictionary and an array holding every construct, streams with each end-of-line form, strings, a name, a
  reference — and compares each read with what the parser makes of the whole object: the same object, the
  same diagnostics. Beside it: a stream ending 0 to 10 bytes before the edge, confirmed with at most 13
  bytes read past the window (T21's shape; the hospital-bed guidance's object 2053 ends exactly on the
  edge); the table's 64 KB edge slid over its last rows, a subsection header, the `trailer` keyword and the
  dictionary, 125 positions, with a sound trailer and with one holding a key that is not a name, reported
  once wherever the edge falls; an anomaly inside a long object reported once; an object the file really
  cuts short still reported, once; a repair by a nested load surviving the attempt that is dropped;
  headers past more than 8 KB of white space; a cross-reference stream whose `/Length` is wrong; streams
  whose length the file cannot hold, cut or lying; the pending buffer's rules, and its capacity following
  `DiagnosticCapacity` through the reader; a string longer than the 16 MB window bound; and two chains of a
  hundred sections whose trailers, or whose cross-reference streams, never close. Every read in the
  window tests goes through a source that refuses to be asked past its end. A
  property in `PropertyTests` draws the objects instead — nested, escaped, alone or in a dictionary, an
  array or a stream — and lets the edge fall anywhere; 21,000 cases over three seeds found nothing.
- **Checked that they have teeth.** Each piece of the fix was disabled in turn, ten mutations, and each
  one fails at least one test. That is how the first version of the table test was caught passing on
  nothing: its rows were 21 bytes, not 20, so the edge never reached the region it was meant to sweep. It
  now counts the positions it exercised.
- **On review.** Four independent reviewers — correctness, the repository's invariants, the tests, the
  documentation — went over the first commit, and each of their fifteen findings was checked against the
  code and a test before it was acted on; none was wrong. The first version asked the source for 13 bytes after a stream's data even when fewer
  were left, and a third-party `PdfFileSource` may refuse that: opening a truncated file threw. It grew a
  classic table's window up to 64 MB for a trailer that never closes, once per section of a chain — about
  10 GB of parsing for a crafted 5 MB file —, and parsed cross-reference streams from 8 KB, which left their
  `/Length` unchecked whenever their data ran past 8 KB: a short one dropped rows and forced a rebuild. It
  let a header after more than 8 KB of white space fail instead of growing the window, and it claimed on
  the site that every object the file cuts short is reported, which was not so. All are fixed above, with
  their tests; the cross-reference stream keeps its fixed 64 KB window. Four mutations the suite did not
  catch — a look-ahead of 10 bytes, a trailer's anomaly dropped, the 16 MB bound removed, the pending
  buffer at its default capacity — are caught now. The same reviews found **T31**, older than this work:
  RunLength streams decode without any bound, 64 bytes out for 2 in, and LZW stops at the bound in
  silence.
- **The corpus.** The five remote documents recorded as unsupported for T21 and T23 — the hospital-bed
  guidance, the 25-signature sheet, the 2015 BOE law, the VA Kernel guide and the poster — pass every test
  that reads them, the laziness test included. The topographic map's T21 report is gone as well, but the
  map stays unsupported for a new reason, **T28**: its 9,600 × 11,410 RGB image decodes to 313 MB, past
  the 256 MB bound every filter keeps against decompression bombs, and the Flate filter reports that as a
  truncated stream. That was the "Flate failure not yet explained".
- **Measured.** The reader's benchmarks on the 1000-page document, default job, two runs before the
  change and three after: indexing 246–247 µs before, 238–250 µs after; indexing and reading every page
  7.5–7.8 ms before, 7.6–8.2 ms after. The runs overlap, and this machine's spread from one run to the next,
  about 5 %, is larger than any difference. Allocation grows by the pending buffer, created once per
  document — 392.71 KB to 392.73–392.79 KB — and by nothing per object.
- **Found on the way.** **T29**: a chain of streams each taking its `/Length` from the next nests object
  loads as deep as the chain, and 20,000 of them overflow the stack and kill the process — older than this
  change, reproduced on it, and fixed in the commit after it: loads nest at most 64 deep, the next one reads
  as null, uncached, and the document reports it once as `syntax.depth-exceeded`. A 50,000-level chain now
  reads in milliseconds; with the bound removed, the same test brings the test host down. **T30**: a rebuild
  parses a 64 KB window at every `trailer` keyword in the file; 80,000 of them open in 0.3 s, so the cost is
  linear, but it is 8,000 bytes read per byte of file. And a top-level string that never ends now grows its
  window up to 16 MB, as an unterminated container already did: bounded in memory, not in the number of such
  objects. Last, a stream whose data runs past the window is still taken at its declared length unchecked;
  checking it now costs 13 bytes, which M2's stream rules ("declared length matches reality") can use.

### 2026-09-25 — The nightly fuzzing campaign starts from one document per reader structure
- **The question**: this branch, rebased on `main` after its fix for the fuzzing runner's disk, brings the
  seed documents from 68 to 104, and the nightly campaign ran 20,000 mutations on every one of them, so
  its cost grew with the corpus. The maintainer asked for a representative selection instead.
- **What "representative" means here**: not the manifest's features — 428 of them among the 104 seeds, 310
  held by a single document, so covering them all would still take 71 — but what the reader meets before
  any content: the form of the index, object streams, linearization, the number of revisions, bytes before
  the header, line endings, the filters it decodes itself, predictors, and damage. The 104 seeds have 32
  such structures; one of them groups 43 documents.
- **The selection** (`FuzzingSeeds`): each night fuzzes the smallest document of every structure, and 16 of
  the others chosen by the run number, 48 documents today; every seed document is reached within five
  nights. The core is computed from the files at each run, so a document bringing a new structure joins it
  without anyone choosing it. Every commit still fuzzes every seed document, 60 mutations each, and a
  nightly failure replays without `ADCODICEM_FUZZ_ROTATION` from the document and seed it names. Four tests
  hold these rules; `fuzz.yml` passes the run number, and its `rotation` input takes `all` for a full
  campaign by hand.
- **Measured on the runner**, 20,000 mutations per seed document: `Fuzzing` run 12, on `e16645a` with all
  104, passed in 12 min 06 s; run 13, on `066b024` with 48, passed in 5 min 15 s. The same night therefore
  costs 43 % of what it did, and will stay near that as the corpus grows. The 17 min 30 s the fuzzing entry below
  gives for 68 documents was measured elsewhere: no `Fuzzing` run came between run 11 and run 12.
- **What it gives up**: a structure is not a syntax. Two files alike in all of the above can still differ in
  their producer's spacing, comments or dictionary layout, which mutations exploit too; the rotating share
  keeps those documents in play every few nights rather than every night. Choosing seeds by the reader
  branches they reach, as `afl-cmin` does, would be more exact and needs a coverage run per document: a
  later step if the signature proves too coarse.

### 2026-09-25 — ADR 33 accepted: the iPRES 2017 set and a fact sheet screened in part join the remote corpus
- **The request**: the maintainer accepted ADR 33 — a remote document may be a member of a pinned archive —
  and admitted the SAMHSA fact sheet to the remote corpus, the exception to the personal-data screen
  documented.
- **The fetcher**: `source.archive` pins the archive (SHA-256, size, member) while `source.sha256` and
  `source.bytes` still pin the document; an archive is downloaded at most once a run, only when a member is
  missing or stale, and its outcome, failure included, holds for every member; only an uncompressed tar
  whose hash matched is opened, and the member is copied into its entry's file, never extracted by its own
  path. Every remote entry now pins its size. Its first tests — against a local server, now run by the
  main CI job — found a defect older than ADR 33: a source serving more bytes than pinned was reported
  unavailable, not refused. Fixed: a file that grew changed, it did not disappear.
- **The 88 iPRES files** came out of one download of RADAR's tar. They hold no metadata and one line of
  text. Expectations from qpdf 11.9.1 in the referee container and from pdftotext; titles in our own words,
  from each file's difference with the base page; the spreadsheet's case number, category and JHOVE
  verdict as features, its text never copied. The reader met 69 as written. 19 are recorded as unsupported,
  all until M2, which now names them: seven page trees it counts differently from qpdf, four faults it
  reads without a word, six recoveries that differ from qpdf's — in four it rebuilds a sound index to find
  a catalogue the trailer no longer leads to —, and two references to an object the file lacks, after
  which it rebuilds its whole index where qpdf takes null (**T27**, new). One file holds no catalogue at
  all; a new expectation, `catalogRecoverable: false`, asks the reader to open it and hand back none.
- **The SAMHSA fact sheet**: a text-mode transfer turned each of its CR and LF bytes into CR LF — 1,904
  pairs, not one lone CR or LF left — and dropped every 0x1A byte, which broke every stream's `/Length`,
  every offset and every Flate stream. The reader meets qpdf's expectations: three pages, the index
  rebuilt. The exception is written into its entry (feature `partially-screened`) and into
  `docs/corpus-sources.md`; the entry publishes nothing from the document beyond its publication name.
- **Documentation**: ADR 33 accepted, ADR 32 amended, `docs/corpus.md`, `tests/corpus/README.md`,
  `docs/corpus-contributions.md` and `docs/corpus-sources.md` brought in line; M2 has a new acceptance row;
  T26 is closed.
- **`Remote corpus` runs 3 and 4**, dispatched on `13c5861` and, after the review below, on `8513ff8`: all
  242 documents fetched on the runner, the 88 out of one download of RADAR's tar; 1,145 unit tests (68, then
  70 skipped by design) and 668 integration tests, all green both times, as was the pull request's CI.
- **Review**: two independent checkers went over the code, the 89 entries and the documentation, and were
  right on each point kept. The fetcher ended the whole run on a pinned archive with a damaged header past
  the first — it now reads every header before copying any member, so a damaged archive refuses only its
  own members —, and its tests wrote into the CI job's summary. Seven tests were added, for 23. Both
  validators now accept exactly the same member names, and a pin that is not text is an invalid manifest.
  The acceptance test judged a clean file's diagnostics before walking its page tree, which hid T27's two
  files; it now walks the tree, each page's contents and resources resolved, first — no other document in
  the corpus changed. The 69 and 19 the first commit gave were miscounted (it was 71 and 17); with T27's two
  files they are right now. The SAMHSA file had lost its 0x1A bytes too: with them put back, more of its
  text reads — the opening of each page and its references —, and is clean; the rest still cannot be
  recovered with certainty, and the exception now says exactly that.
- **Tests**: without the remote corpus, 482 unit and 302 integration; with 240 of its 242 documents — the two
  GitHub issue attachments still answer 403 to this session —, 1,143 unit (70 skipped by design) and 665
  integration, all green; the fetcher's 23 tests pass.

### 2026-09-25 — The OPF format-corpus, file by file: 123 more documents, 56 of them committed
- **The question**: the Open Preservation Foundation's format-corpus holds 293 PDFs; why had only nine
  entered? Because the passes of 2026-09-24 hunted for the W lines and took from it only what filled one;
  the other 284 had never been examined, turned down or listed. The survey row calling the corpus usable
  was also incomplete, and is corrected. **The request**: take in all that can be, committed where
  possible, remote otherwise — the diversity of producers is the point.
- **The screen**: all 284, by script and by eye — every page's text, metadata, annotations, form and XFA
  values, attachments (spreadsheets, Word files, videos), signatures, the strings of every object and
  every revision, and page images where there is no text layer, which caught an e-mail address printed
  inside a scan. One independent verifier then re-screened the 131 files admitted, OCR included, told to
  reject on doubt: it turned down six — a nurse's extension split over two lines, an e-mail with a space
  after the `@`, contact details printed only in two scans, an essay on its author's own health, a
  classroom-only licence —, moved a Census section with copyrighted tables to the remote corpus, and
  flagged two more that were excluded on doubt: an iPhone video carrying where and when its identifiable
  author filmed it, and a fact sheet too mangled to be read whole.
- **56 committed** (6.2 MB): the Cabinet of Horrors' 16 remaining files and Acrobat 11's three Image
  Conversion pages, the Save As corpus (OpenOffice.org 3.2 and 3.3, LibreOffice 3.5, RC4-128 with and
  without an open password), Pages '09, iBooks Author, calibre through pyPdf and PoDoFo, an InDesign CS
  flyer, and 15 GovDocs1 files that are federal staff's work (VA, Census, USGS, Congress, the Law Library).
- **67 remote** (216 MB): two Cabinet files, 20 GovDocs1 files too large or not shown to be federal staff's
  work, and 45 files filed against JHOVE, each under the error it raised.
- **161 refused**: 68 for personal data — authors' e-mail addresses in 49 JHOVE files, named staff's
  direct lines in GovDocs1 ones —, two for a restriction of purpose, one that could not be read whole, one
  AppleDouble fork that is not a PDF, and **all 89 OPF copies of the iPRES 2017 hand-built set**: git stripped five carriage returns from
  87 of its 88 test files and from the page they derive from, as the authors' archive on RADAR (CC BY-SA
  4.0) shows byte for byte; the 89th, `minimal_test.pdf`, is intact but holds only a header. The originals
  are the best external test suite M2 could have, but RADAR serves them only as one tar; T26 and ADR 33
  record what fetching them takes.
- **What the reader made of them**: none of the 131 first admitted crashed or hung it; of the 123 kept, 113
  open as their entries say, and ten are
  recorded as unsupported. **T25** (new): a trailer's `/Prev` 12 bytes past the older section's `xref`
  keyword makes the reader drop that section — 4,106 entries of IBM's QMF manual — without a diagnostic,
  where qpdf reports it and rebuilds. T21, T23 and T24 each gained a document: T24 from the other side, a
  273 KB table read again from its start each time its window grows. Five files wait for M2, now named in
  its acceptance conditions: two catalogues without `/Type`, a null kid in a page tree, and two trees whose
  kids point at missing objects — qpdf, poppler and PDFium count those as blank pages, pikepdf and the
  reader skip them.
- **Expectations**: pages by `qpdf --show-npages` (pikepdf, used before, disagrees with it on five damaged
  files), verdicts by qpdf 11.9.1 in the integration container, text by pdftotext, PDF/A by veraPDF 1.30.2.
  qpdf says nothing about a MacBinary header or a `data:` URI before `%PDF`; those two files expect the
  offset adjustment anyway. Two files expect the rebuild only after a full read, as qpdf checks every
  offset at opening and the reader does not.
- **Tests**: 481 unit and 302 integration without the remote corpus; with 151 of its 153 documents, 804
  and 560, all green; with all 153, on the runner (`Remote corpus` run 2), 806 and 563, all green.
  Fetching the remote corpus from this session got 151 of 153: the two GitHub issue attachments of
  the third pass answer 403 to a cloud session, whose GitHub proxy lets it reach only its own repositories.
  The runner is not bound that way: the first nightly run fetched both.
- **Afterwards**: the first two `Remote corpus` runs, recorded under *At a glance*. T25 checked against
  `PdfFileReader.TryReadXRefChain`, which returns success when a later section fails as long as an earlier
  one was read; its row now says the drop is silent only when the `/Prev` lands inside the file. The RADAR
  lead became **T26** and a proposed **ADR 33**: the tar is the 2017 deposit, whose MD5 RADAR publishes; it
  holds 88 test files — one more than the OPF's copy, `T04_019`, a trailer pointing at the wrong
  cross-reference offset — and neither reference file; its files hold more than their authors' deviation
  (stale offsets in 43 of 59 header and body files). Two independent checkers reviewed the first draft
  against the tar, the code and the corpus rules; their corrections are in. M2 names the set as the input
  its structural profile is still missing.

### 2026-09-25 — The nightly fuzzing campaign filled its runner's disk
- **Symptom**: run 11 of `Fuzzing` failed after 33 minutes with its step still "in progress" and no
  log to download: the runner was lost, not a test.
- **Cause**: `FuzzingTests.Survives` built its assertion messages with `Save(input, what)` inside the
  interpolated reason. That string is built before the assertion runs, so **every** mutated input was
  written to the temp directory, passing or not. The file name carries the seed, so nothing was
  overwritten. With the corpus going from 27 documents to 112, 20,000 mutations per seed wrote tens of
  gigabytes. Reproduced locally: 30 GB in `/tmp` before the disk filled, and the tests then failed on
  I/O.
- **Fix**: the input is saved only once a budget is exceeded. Measured again at 20,000 iterations over the
  112-document corpus: 137 of 137 passed in 17 min 30 s, with no file written. That leaves room under
  the workflow's 60-minute limit.

### 2026-09-24 — The leads held back by their licence: 82 more documents, 76 of them remote
- **The request**: now that ADR 32 gives a place to files we may not redistribute, follow the leads
  `corpus-sources.md` had held back for their licence, and reconsider the five files once refused for size
  alone. Eleven research groups: W05 signatures (pdfcpu's test data, EU DSS's test resources, Foxit's files
  in `pdfium_tests`, node-signpdf, the BOE's sealed gazette, a hunt for commercial e-signatures), W04 real
  invoices and statements, W07 vendors' Factur-X and ZUGFeRD samples, government leads, W06 files that
  broke pdf.js, PDFBox, PDFium and pdfplumber, and ShareAlike sets. They examined 113 candidates and
  proposed 80, each then adversarially verified — bytes downloaded again and hashed, licence, personal
  data, expectations against qpdf, poppler and veraPDF —; 79 survived, and 158 further leads were set
  aside, each with its reason.
- **Six committed**, each under an attribution-only licence and under 2 MB, after a final independent
  review of bytes, licence and personal data: node-signpdf's own two files (MIT), a signature whose
  `/Reason` contains the keyword `trailer` and an unsigned placeholder; the ZUGFeRD corpus maintainer's
  factur-x Python output with a false PDF/A-3b claim (Apache-2.0); the Federal Reserve Board's SR 01-15
  attachment, FineReader 8 OCR under CCITT pages (the Board's public-domain notice); the UK OZEV sample
  invoice (OGL v3); and a GSA Standard Form 30 completed in DocuSign (US federal, public domain) — **the
  first commercial e-signature the public corpus holds**.
- **76 remote, 86 in all**: a Konica copier's untouched scan with its own OCR, the file W03 lacked; Adobe
  Sign, Yousign, two more DocuSign files and Universign timestamps; PAdES
  B-B to B-LTA, qualified seals, 24 signatures in 48 updates; real invoices and statements from SAP,
  Axapta, Scoro, PDFlib on z/OS and a bank's AFP batch processor; eight vendor samples of Factur-X,
  ZUGFeRD and Order-X; files that broke pdf.js, PDFBox, PDFium, pdfplumber, PdfPig and OCRmyPDF; and USGS
  Professional Paper 1 (147 MB), so all three W11 references are now fetched and tested every night. The
  committed corpus is 112 files, 16.8 MB: 19 generated, 93 from elsewhere.
- **The five refused for size alone, reconsidered** under the new rule: the Open Preservation Foundation's
  signed 3D portfolio (CC0, 4.7 MB) went remote, as did PDF/UA Reference Suite 2-09, a tagged scan of
  10.6 MB, and 2-08, a textbook chapter of 2.3 MB whose content is a publisher's that the suite's CC BY
  cannot be shown to cover. The NIST request for quotation scanned on a Canon SC1011 was refused: it prints
  two named staff members' direct phone lines and own e-mail addresses, and Acrobat had re-saved it anyway.
  Suite member 2-01, a Danish magazine of 12.9 MB, was refused: it gives health conditions of named people,
  children among them. And 2-06, a 1.65 MB brochure first held back for naming a photographed person, went
  remote: its cover photograph carries a third party's copyright the suite licence cannot cover.
- **The maintainer's rulings on borderline cases**, now rules in `corpus-contributions.md`: a software
  library author's own e-mail inside that library's copyright string compiled into the file is a library
  credit, and acceptable; an anonymous photograph whose caption describes a health condition is
  anonymised, and acceptable; images of named people's handwritten signatures are treated like names,
  acceptable even in a committed file — which moved the SF 30 from remote to vendored; fabricated test
  e-mail constants at real domains are fictitious data. Remote use is acceptable under terms that restrict
  reuse — conditions beyond attribution, non-commercial reproduction, fair use only, no modification or
  commercial use — but not "educational use only": California Geological Survey Note 17 was excluded for
  it.
- **Excluded, notably**: the Mustang library's Oracle Reports invoice, whose XML
  gives an employee's direct line and e-mail; Adobe's well-known signed sample, whose signer certificate
  carries an employee's own e-mail. Most of the EU DSS files set aside failed the same way: personal
  signing certificates carry e-mail addresses, phone numbers and national identity numbers.
- **What the reader made of them** — the library is not changed on this branch; gaps are recorded, as
  ever. **T23** (new): an indirect object longer than the reader's 8 KB window is cut at the window's edge
  — a DSS `/VRI` dictionary of 10,112 bytes in the file with 24 signatures, 36 structure arrays of about
  8.7 KB in the 2015 BOE law — where qpdf reads them whole. Both entries are unsupported until it is
  fixed, before M2 closes. **T24** (new): each cross-reference section is read through a window of up to
  64 KB whatever its size, so opening a 218 KB signed web capture with three sections reads 117 KB —
  bounded, but proportional to the number of sections; that entry is unsupported, for M13.
- **Recorded without a new debt row**: two files where the reader is silent while qpdf reports damage — an
  `/Info` without `endobj`, names containing `#00` — are unsupported until M2, like the two hand-written
  ones before them. PDFBOX-3947, a zeroed block that destroyed one object stream: the cross-reference
  reads as written, and the reader rebuilds the index when it meets the destroyed stream, and reports it —
  so the entry expects no rebuild at opening and the rebuild diagnostic after a full read. Two truncated
  PDFBox files record no page count: qpdf 11.9.1 finds no `/Root` in one; on the other, qpdf 11.9.1,
  qpdf 12 and PDFium disagree (11, 4 and 8 pages).
- **The laziness test** now honours `unsupported`, like the other acceptance tests, and skips an encrypted
  document visibly until M11 brings decryption. It no longer applies to documents whose index must be
  rebuilt: a rebuild scans the file by definition. With the remote corpus fetched, 539 unit and
  363 integration tests pass; without it, 346 and 208.

### 2026-09-24 — Size is a recommendation; over 2 MB a document goes remote
- **The maintainer's rule**: the corpus search refused files over 2 MB, and so lost five that would have
  filled real gaps — a PDF portfolio, a Canon scan with its own OCR, a tagged scan, two PDF/UA files. Size
  no longer turns a document down. Keeping committed files small is a recommendation; a document over
  2 MB is not committed, whatever its licence, and goes to the remote corpus, fetched from its public URL.
  The private corpus stays for confidential documents only, never for heavy ones.
- **Where it is written**: `corpus.md`, `corpus-contributions.md`, `tests/corpus/README.md`, `CLAUDE.md`,
  and a second dated amendment to ADR 32 ("too large to commit" means over 2 MB). `corpus-sources.md`
  keeps the rule the search applied, with the revision beside it. `corpus.md`'s vendor row still said
  "no real person's name anywhere", from before the personal-data rule was relaxed; corrected.
- **Enforced**: `A_committed_document_weighs_at_most_2_MB` fails on a committed file over the threshold.
  The largest today is the generated 1,000-page journal, 1.56 MB.

### 2026-09-24 — One corpus manifest instead of four files
- **Why**: 87 of the manifest's 106 entries were copies of `vendor.json` and `contributed.json` — two
  records of the same truth, every addition a double diff, and nothing to stop a correction landing in the
  copy. `remote.json` held a tenth of the corpus in the same format. The maintainer chose one file.
- **What changed**: `tests/corpus/manifest.json` now describes all 116 documents; the three listings are
  gone, their entries moved without a change (checked field by field). `build_corpus.py` marks the entries
  it writes `"builtBy": "build_corpus.py"` and replaces only those — `origin` could not tell it, since the
  Microsoft print-driver file is `derived` but comes from `build_word.ps1` — and gives every other entry
  nothing but the referee's verdict; `--committed-only` and `--remote` now only refresh verdicts in place,
  and both left the manifest byte-identical in the container. `fetch_remote.py` reads the entries of origin
  `remote`, and `Corpus` drops those whose file was not fetched. `private.json` stays apart: git ignores it.
- **A new check in the main job**: `Remote_documents_are_pinned_and_kept_where_git_ignores_them` fails on a
  remote entry outside `remote/`, a file under `remote/` not marked remote, or a missing URL or SHA-256 —
  the nightly job used to be the first to read those entries. 333 and 196 tests pass without the remote
  files, 355 and 216 with them. ADR 32 carries a dated amendment: the decision is unchanged, only the file.

### 2026-09-24 — ADR 32 accepted and implemented: the remote corpus
- **The decision**: documents we may use but not redistribute — bug-report attachments, vendors' samples,
  ShareAlike sets, files too large to commit — are described in `tests/corpus/remote.json`, fetched on
  demand at a pinned SHA-256, and never committed, nor anything derived from them. Accepted by the
  maintainer the day it was proposed.
- **What implements it**: `build/fetch_remote.py` (standard library only; refuses a changed file instead
  of trusting it; tells an unavailable document from a refused one), `build_corpus.py --remote` (the
  referee's verdict, as for every other entry), `Corpus` merging the remote entries whose file is present,
  `tests/corpus/remote/` ignored by git, and a `Remote corpus` workflow — nightly and on dispatch — that
  runs both suites over what it fetched and reports a missing document as such, not as a failing test.
- **The first ten documents**, from the leads the search held back: the Ricoh copier scan and the SAP
  NetWeaver statement kept by pdf.js, the FNFE-MPE's and intarsys's Factur-X samples, three ShareAlike
  files (pdfTeX, Google Docs, WeasyPrint in Arabic), the Census Bureau's Hebrew guide, and two of W11's
  heavy references. Expectations established by qpdf, poppler and veraPDF like everyone else's.
- **Observed, not assumed**: all ten fetched from their real sources and matched their hashes; a pinned
  hash changed on purpose was refused and the file removed; a second run downloaded nothing. With them,
  354 unit and 216 integration tests pass (the topographic map skipped with its T21 reason); without them,
  the suite is exactly as before, 332 and 196. The US Code's 9,302 pages open clean in 0.8 s.
- **Not yet observed**: the workflow itself, which only runs from `main`'s schedule or a dispatch.

### 2026-09-24 — The wanted documents, searched for on the internet
- **The request**: find in public sources the documents `corpus-contributions.md` asks for (W01–W12),
  free of rights and free of real people's data, and see what other PDF libraries keep. The rules were
  settled with the maintainer before anything was downloaded, then twice more along the way: ADR 23
  licences only; no real person's name anywhere, superseded revisions included, with typeface-designer
  credits, public figures and historical figures tolerated; publisher's bytes only; specimens allowed.
- **38 third-party documents entered `tests/corpus/vendor`**, 6.6 MB, from 69 downloaded candidates and
  47 adversarially verified ones. Every line of the wanted list now has something; what public sources
  cannot give — a real Java-stack invoice, a commercial e-signature, a Factur-X from an ERP, a copier's
  own OCR — stays wanted, and says so in `corpus-contributions.md`. Provenance (URL, date, SHA-256) is in
  `vendor.json`, attribution in `NOTICE`, the whole story in `docs/corpus-sources.md`.
- **W01 was also produced here**: `build/build_word.ps1` writes the corpus invoice through Word's Save as
  PDF, the Microsoft Print to PDF driver and the PDF24 printer. Both printers stamp the Windows account
  that printed: the Microsoft driver's display name in `/Author` (overwritten in place, same length, file
  recorded as derived), Ghostscript's `%%For` in `/Author` and the XMP (set in the PostScript before PDF24
  converts it). **The PDF24 file draws a box for every capital E** of the regular face: the driver's
  PostScript lacks the glyph while ToUnicode still says E — a genuine W06 file from an office desktop.
- **Most other libraries' corpora cannot be reused**: bug-report attachments under a code licence
  (pdf.js, PDFium, PDFBox, PdfPig, pypdf…), copyleft (Poppler, MuPDF, iText, and `py-pdf/sample-files` under
  CC BY-SA). pikepdf's per-file `REUSE.toml` is the one complete provenance record found.
- **The corpus found three things of ours.** `build_corpus.py` deleted the whole of `documents/` before
  regenerating, so a contributed document would have vanished with its entry: `contributed.json` now lists
  what the script must keep, and `--committed-only` refreshes the manifest with nothing but qpdf. The
  integration test read qpdf's page count from standard output and standard error together, and failed on
  any file qpdf warns about. And the reader falsely reports a truncated stream at a window boundary (T21).
- **Recorded, not fixed**: two hand-written files on which the reader is silent while qpdf reports damage
  (a trailer without `/Size`, a page given a stream body) are `unsupported` until M2 — a manifest field
  that did not exist, and now skips the test with the reason. `M2.md` gains the corresponding acceptance
  conditions and the anomalies of the vendored files (stale hint tables, a `/Size` off by one, an xref
  stream without its own entry, undefined references, malformed font XMP).
- **Referees**: pikepdf for pages, qpdf 11.9.1 in the integration container for the verdict, poppler's
  `pdftotext` 24.02 for text — it disagrees with xpdf on one Arabic ligature —, veraPDF 1.30.2 for PDF/A:
  thirteen of fifteen new claims upheld, two rejected.
- **The rule on names was then relaxed**, at the maintainer's request: a person's name alone, a Windows user
  ID or a photograph no longer disqualifies a file; a person's phone number, postal address or own e-mail
  still does, as does health, bank or identity data; fabricated and specimen data is always fine. Every
  file turned down for a name alone was examined again, and a critic went through the 117 earlier
  rejections for the ones missed. **38 more documents entered**, 6.1 MB, filling what the first pass had
  left open: copiers with their own OCR, JasperReports and other Java writers, PDFMaker 5 to 25, PDFWriter,
  a Mac Distiller, Print to PDF from Word, weclapp and Dynamics 365 Factur-X, DILA's Dictao signature, a
  qualified seal renewed by timestamps, XFA forms the reader can open today. What stays wanted is what
  only an inbox holds: a real supplier or bank document, a commercial e-signature.
- **For what cannot be redistributed** — ShareAlike sets, bug-report attachments, vendors' samples, W11's
  heavy files — the maintainer chose fetching on demand: [ADR 32](adr/0032-documents-that-cannot-be-redistributed-are-fetched-on-demand.md),
  proposed, not implemented. A manifest of URLs and hashes, a separate job, nothing committed.

### 2026-09-22 — Documentation for every release, and for the preview
- **The request**: a preview package should come with its documentation; the site should open on the
  stable version, with a button to the latest preview; every stable release should keep its documentation,
  behind a selector. Settled in ADR 31, with the choices it made along the way.
- **Docusaurus's own versioning**, not a site per release: one build serves everything, and a frozen
  version stays correctable. The stable release freezes the user documentation and the generated API
  reference in its release commit (`scripts/version-docs.mjs`, run from `.releaserc.json`'s prepare step).
- **One entry per line**: per minor below 1.0, per major from 1.0 — 1.0 opening the `1` line. A later
  release on a line replaces its copy, deletions included (the script unstages the old copy from git, since
  the release commit only adds what exists), and the selector shows the line's latest release. All lines
  are kept.
- **The preview has a button, not a selector entry**, and only while a preview is newer than the latest
  stable release. Right after a release, the newest preview on nuget.org is *older* than it; a button to it
  would lead backwards, so it disappears until the next merge. Before the first release the preview is the
  whole site. The rules are in `scripts/versions.mjs` and pinned by `npm test` (12 cases, run in CI).
- **Deployment**: `release.yml` calls `docs.yml` after every preview from `main`, with the preview's
  version — nuget.org can take minutes to list a new package, so it is not asked — and after every stable
  release, from the release commit, which the run did not start from. A manual run asks nuget.org.
- **Verified by simulation**, since no stable release exists: freezing `0.1.1`, then `0.2.0`, then `0.2.1`
  over it (a stale page and a local edit both gone, as they should be), then refusing `0.2.1` again and a
  preview. Built each way — two lines and a preview, no preview after a release, no stable at all — with
  the site check passing on every page, and the selector, the button in both directions, the banners and
  the namespace sidebar of a frozen API reference looked at in a browser.
- **Caught by looking, then made checkable**: the preview's banner appeared on every project document too —
  their unversioned plugin's only version is also named `current`. Fixed, and the site check now fails on
  a version banner under `/project/` (it reported 41 pages before the fix, none after).
- **Found on the way**: `main` gained a ruleset today, and GitHub Actions is not among its bypass actors —
  the first stable release will fail when it pushes its commit (at a glance, above).

### 2026-09-22 — The site had never shown a single user-facing page
- **What a visitor saw**: the introduction, both concept pages and all thirty API pages printed as
  `export const frontMatter = …` followed by a screen of `_jsx(…)` calls. Only the project documents
  rendered. It had been that way since the first deployment on 2026-09-19.
- **Why**: moving the site into `docs/website` left the project documents' plugin pointed at `docs/` —
  which now contained the site. Docusaurus scopes each docs plugin's MDX loader to its whole directory;
  `include` and `exclude` choose pages, not what the loader compiles. So every user page went through two
  loaders, and the second rendered the first one's output as Markdown. The page itself said so: it imported
  metadata from `…/docusaurus-plugin-content-docs/project/`, the wrong plugin.
- **Fix**: the project documents are copied into `docs/website/project` (ignored by git) before every build
  and published from there; edit links still point at `docs/`. ADR 28 carries the amendment.
- **Why nobody noticed**: the build passed, every link resolved, every URL returned 200 — and T12 below was
  closed on "the site serves 44 pages", which counted responses and never looked at one. Every build now
  ends with `check-site.mjs`, which reads the built pages for compiled MDX, HTML printed as text and
  unresolved DocFX references, and fails the build on any. Run against a saved copy of the published site,
  it reports 64 problems in 74 pages; against the repaired build, none.
- **The API reference, tidied while there**: DocFX's inline `<a id>` anchors had been the title, sidebar
  label and table-of-contents text of every page — they are explicit heading ids now; six `<see cref>`
  DocFX could not resolve had left empty elements, and now link to what they name; cross-links carrying an
  anchor were not being rewritten; the sidebar groups types under their namespace; generated pages no
  longer offer an edit link to a file that exists only during the build; and analyser suppressions are
  filtered out of declarations. Printing them exposed two `Justification` strings in `PdfDictionary` and
  `PdfStream` mangled by an old search-and-replace ("calls this a public sealed class pdfdictionary :
  pdfobject"), now rewritten.
- **Stale content**: the introduction still said nothing was on nuget.org; it now gives the `--prerelease`
  command and says which package exists and which milestone brings each of the others. The diagnostics
  page claimed seventy-five thousand mutations on every release; a normal test run does about three
  thousand, and the page now says so.

### 2026-09-19 — What two abandoned branches still knew

- Five merged pull requests left their branches behind. Three carried nothing `main` does not have and
  were identified for deletion; two did not, and this is what they held.
- **`main` was wrong about its own release.** It said no package had ever been published. nuget.org's flat
  container says otherwise: `0.1.1-preview.10` through `.13`. T11 called trusted publishing *untested*
  when four runs had already been through the OIDC exchange. The status file is the one document that
  must never be optimistic *or* pessimistic, and it had drifted the second way.
- **The coverage upload has never found the file it was given.** Run 84's log: `Some files were not found`
  for the `bin/Release/net10.0/TestResults/` path, then `Found 1 coverage files to report` pointing at
  `TestResults/` in the working directory. Codecov's CLI searches when the file it was handed is missing,
  and `fail_ci_if_error: false` means the day it stops searching, nothing turns red — the badge just
  freezes. Fixed by naming `--results-directory` and pointing the upload there.
- **T14 was stale in the other direction**, but only partly, and the branch overstated it. Codecov is
  linked, the upload runs tokenless (`Token length: 0`) and the badge reads 82.61% — so "not linked, badge
  empty" is done. The branch went further and called the Codecov **GitHub App** installed; it is not. The
  bot comments as `codecov-commenter` rather than `codecov[bot]`, and says so itself on every pull
  request. T14 is narrowed to that, not struck out — a distinction worth the correction, since it is the
  difference between "coverage is handled" and "coverage happens to work".
- **One claim on those branches was checked and rejected**: the `NUGET_USER` secret still had to be
  added. `main` had already replaced that whole design with `NUGET_ACCOUNT` and no secret at all, so it
  was not carried over.
- **A second was rejected, then turned out to be right for the wrong reason.** The branch said Pages was
  enabled; the site returned 404, so I kept T12 as written. T12 blamed the settings — "GitHub Pages is
  not enabled" — and that was the stale part. Pages *was* enabled. What had never happened was a
  deployment: `docs.yml` runs only with a stable release or its own manual trigger, run 1 had failed
  back when the settings really were missing, and nothing re-attempted it since. Dispatching it
  succeeded first time and the site is live. The lesson is the ordinary one: a 404 confirms the symptom,
  not the diagnosis.
- The prefix reservation is now real debt rather than a future chore: packages exist under `AdCodicem.`
  and nothing stops someone publishing beside them. T20, and the procedure is an email, not a button.

### 2026-09-19 — Fuzzing read 0 because of what it looks for, and I had read the check wrong
- **The correction first.** The entry below states that Scorecard detects no .NET fuzzer and that the 0
  is simply wrong. That is false, and reading `checks/raw/fuzzing.go` rather than recalling it says so:
  there is a `clients.CSharp` entry, and it greps `*.cs` for `using FsCheck;`, `using FsCheck.Xunit;`,
  `using FsCheck.NUnit;` or `using Expecto.ExpectoFsCheck;`. The check is narrower than "is this project
  fuzzed" — it asks whether the project does **property-based testing with FsCheck** — but it is not
  blind to this stack, and the 0 was earned.
- **So the fix is a test technique this codebase was missing, not a token.** `PropertyTests` holds six
  properties over generated data: the lexer terminates on any bytes and keeps every token offset inside
  the buffer; the parser answers for any bytes without letting its position escape the input; a text
  string survives `FromText` and `ToText`; the hand-written integer and real parsers agree with the
  framework everywhere both will answer; and the seeding itself replays.
- **They complement the mutation campaign rather than repeat it.** Mutation fuzzing starts from real
  documents and damages them, so it explores the neighbourhood of files that exist. A generator starts
  from nothing and reaches an empty buffer, a file of nothing but delimiters, a number carrying forty
  signs. Different instruments, different defects.
- **Checked that they have teeth before trusting them.** Three were deliberately falsified — the integer
  parser compared against `value + 1`, the round-trip against `value + "x"`, the lexer bound inverted —
  and all three failed, shrank to a minimal counter-example and printed a replay pair. A property that
  cannot fail is decoration.
- **Deterministic in the suite, exploratory at night.** A fixed seed means a commit can never be failed
  by luck; `ADCODICEM_PROPERTY_SEED` and `ADCODICEM_PROPERTY_TESTS` let the nightly campaign run 50 000
  cases per property from a different seed each time, which `fuzz.yml` now does alongside the mutations.
  FsCheck rejects an even gamma outright, so that half of the random state stays a constant.
- **FsCheck without `FsCheck.Xunit`**: the runner integration still pins `xunit.extensibility.execution`
  below 3.0.0 and would drag xUnit v2 into a v3 suite. The library itself is runner-agnostic, so a
  property runs inside an ordinary fact. No advisory against `FsCheck` 3.4.0 or `FSharp.Core` 5.0.2, so
  Vulnerabilities stays at 10.
- **Measured effect: 6.6 → 7.1**, Fuzzing 0 → 10 at weight 5. Confirmed by the detection rules rather
  than assumed: C# is 84% of what GitHub reports for this repository, far above the check's
  `average / 4` prominence threshold; `*.cs` matches a nested path because the matcher falls back to the
  file name; and `tests/` is not one of the `testdata/` or `src/test/` prefixes the walker skips.

### 2026-09-19 — The badge had a number at last, and it read 5.5
- The first Scorecard run that ever started (run 13, on `1bfde6d`) published a report: **5.5 out of 10**.
  Before anything was touched, its own arithmetic was reproduced from the published per-check scores and
  the documented risk weights — Critical 10, High 7.5, Medium 5, Low 2.5 — and it lands on 5.46, which is
  what `api.scorecard.dev` rounds to 5.5. That is what makes the rest of this entry a measurement rather
  than a hope: every fix below is worth a known number of points.
- **Pinned-Dependencies, 1/10 — the whole of it.** Not one of the 39 action references in this repository
  was pinned: 33 GitHub-owned and 6 third-party, every one on a floating tag. A tag is a name its owner
  can repoint at other code, and every run after that picks the new code up in silence. All 39 are now
  commit hashes, each carrying the version as a trailing comment — which is what Dependabot reads to know
  what the hash stands for, and what it rewrites alongside the hash when it bumps one. Each hash was
  resolved from `git ls-remote` and **checked against the annotated tag's dereferenced commit**, since a
  tag object's own SHA is not the commit and pinning it would not resolve.
- **Vulnerabilities, 5/10 — all five in the documentation site.** Two in `qs`, two in `serialize-javascript`
  and one in `uuid`, every one a transitive dependency of Docusaurus that nothing here asks for by name.
  npm's own advice was to take `@docusaurus/core` *down* to 3.5.2; three `overrides` clear all five without
  moving Docusaurus at all. `npm audit` now reports nothing, and the site still builds.
- **Security-Policy, 4/10 — and the missing 6 points were one link.** Scorecard gives 1 point for text,
  3 for saying something about disclosure and timelines, and 6 for *linked* content. `SECURITY.md` was a
  page of careful prose containing no URL whatsoever: it told the reader to find the Security tab. It now
  links the advisory form itself, which is better writing before it is a better score.
- **Measured effect: 5.5 → 6.6.** Pinned-Dependencies 1 → 9, Vulnerabilities 5 → 10, Security-Policy
  4 → 10. Unproven until the next run on `main` publishes, for the same reason as last time.
- **The last point on Pinned-Dependencies was refused, with a reason.** It is `dotnet restore` without
  `--locked-mode`, which means committing `packages.lock.json`. Worth exactly 10 of the check's 144
  weighted units — 0.05 of the displayed score, which rounds away entirely — and
  `RestorePackagesWithLockFile` set in `Directory.Build.props` breaks restore outright (`NETSDK1013`, an
  empty `TargetFramework`), though it works set per project. Central package management with transitive
  pinning already gives most of what a lock file is for. Recorded as T17 rather than forced through.
- **What is left is not code.** Branch-Protection and Code-Review are 0 and weigh 7.5 each: together
  they are worth about 1.5 points, and both need the `main` ruleset of T15 plus pull requests someone
  approves. Maintained is 0 because the repository is younger than 90 days, which only time fixes.
  Contributors is 0 because there is one of us. CII-Best-Practices needs a registration (T18) and
  Signed-Releases is unscored only because no release exists yet (T19). ~~Fuzzing reads 0 and will stay
  there: Scorecard detects no .NET fuzzer, so the number stays wrong.~~ **Wrong, and corrected the same
  day** — the check does cover C#, and the entry below says how.

### 2026-09-19 — The supply-chain badge had never been earned
- `OpenSSF Scorecard` was **red on every run it has ever had** — twelve of them, back to the day the
  workflow was added — and nothing said so, because the failure is not a check that fails but a workflow
  that never starts: `ossf/scorecard-action@v2` does not resolve. The action tags releases (`v2.4.4` is the
  current one) and publishes no floating major, so GitHub stops at *Prepare all required actions*. Six
  seconds, no step run, no SARIF, nothing uploaded to code scanning and nothing published to
  `api.scorecard.dev` — which is why the README badge has been empty since it was added.
- **Dependabot could not have caught it either**: the `github-actions` ecosystem bumped six other actions
  in this repository while leaving this one alone, because an unresolvable ref gives it no version to
  compare. An exact tag puts the action back under Dependabot's eye, which is the part that keeps the fix
  from decaying.
- Read against the action's own source at `v2.4.4` rather than assumed: `repo_token` defaults to
  `${{ github.token }}`, so the four job permissions already granted are what it needs; publication is
  refused only for a private repository or a ref other than the default branch. `workflow_dispatch` was
  added on the strength of that second rule — a report can now be asked for, provided the run is started
  on `main`.
- **Unproven until it runs.** The workflow only triggers on `main`, so merging is the first real attempt.
  The thing to check afterwards is not the green tick but the badge: a green run that publishes nothing
  looks exactly like a green run that does.

### 2026-09-19 — A preview was asked for, and the guard held
- The preview deployment was launched by re-running `Release` on the tip of `main` (`2798fb2`, run 9,
  attempt 2). Build and the whole suite pass in 25 seconds; the run then stops at **Check the publishing
  identity is configured**, because `NUGET_USER` is not set on the `nuget` environment. Nothing was packed,
  nothing was pushed to nuget.org, no OIDC key was even requested.
- **The secret was the wrong shape for what it held.** The trusted publishing policy was already configured
  on nuget.org; what was missing was the answer to *which account*, which `NuGet/login` requires as `user`
  because OIDC proves a run is authorised without saying who receives the key. That answer is `AdCodicem` —
  the owner of this repository, the prefix of every package, public on every page nuget.org will serve.
  Keeping a public name in a secret hid nothing and bought a setup step that fails silently much later.
  It is now `NUGET_ACCOUNT` in `release.yml`, stated once at the top, and **both guards are gone** with the
  thing they guarded against.
- **A preview had no manual trigger**, which is why the attempt above had to be a re-run — and a re-run
  keeps its run number, so it republishes the same `-preview.<n>` version rather than producing a new one.
  `workflow_dispatch` now asks *what to publish*, and defaults to `preview`. The stable path is the one
  that tags, writes to `main` and cannot be withdrawn, so it is the one you have to select. ADR 30 is
  extended rather than reopened: publishing a preview should not have required a merge, since taking that
  pressure off the merge is what the record was for.

### 2026-09-16 — Dependabot's six action bumps merged
- Six **major** GitHub Actions bumps, which `dependabot-auto-merge.yml` deliberately leaves for a human.
  Each was read against its own release notes; what that reading settled is on the squash commits, where
  it belongs. The three Pages bumps were reasoned rather than observed at the time; the deployment of
  2026-09-19 has since exercised all three.
- `Conventional commits` was red on all six: Dependabot wrote "Bump" with a capital and `subject-case`
  refuses it. **Corrected at the squash, not relaxed in the configuration** — which fixed the source too,
  since Dependabot copies the style of recent commits and its last rebase came back lowercase on its own.

### 2026-09-15 — The starting tag, and the trap it would have sprung
- `v0.1.0` tagged on `2808d2f`, the tip of `main`, so the first stable release continues in `0.x` instead of
  being declared `1.0.0`. No workflow runs on a tag, so pushing it changed nothing on its own.
- **Checked before relying on it, and it would have failed.** Both release paths passed the last tag as
  the baseline for package validation, which downloads that version from nuget.org to compare the public
  API — and nothing was ever published as `0.1.0`. `dotnet pack` with that baseline fails with `NU1101`,
  reproduced locally. The first preview and the first stable release would both have died at packing, the
  moment the publishing identity was configured.
- `.github/scripts/published-baseline.sh` now decides the baseline: the last release's version if nuget.org
  has a package for it, nothing if nothing has been published, and a failed run if nuget.org cannot be
  asked. Every branch exercised; the stable release's exact prepare command, rendered by lodash as
  semantic-release renders it, now packs `0.2.0` cleanly against the real tag.

### 2026-09-15 — Publishing and releasing are no longer the same event
- A merge into `main` now publishes a **preview** package and nothing else: no tag, no changelog, no
  GitHub Release, no redeployed site. The **stable release is a manual run** of the same workflow, and it
  alone versions, tags, writes `CHANGELOG.md`, publishes and deploys the documentation. ADR
  [30](adr/0030-previews-on-every-merge-stable-releases-on-demand.md).
- Both paths stay in `release.yml` on purpose: a nuget.org trusted-publishing policy is pinned to a
  workflow **file name**, so one file is one policy to register rather than two to keep in step.
- A preview is `<last release, patch bumped>-preview.<run number>` — after `v0.1.0`, `0.1.1-preview.12`.
  It says where the preview sits rather than predicting the next release: if a `feat:` takes the release
  to `0.2.0`, every `0.1.1-preview.n` still sorts between the two. Working the exact number out would
  mean running semantic-release on every merge to answer a question only the release asks.
- The site is deployed by the release rather than by the tip of `main`, so what is documented online is
  what is installable. `docs.yml` keeps a manual trigger for a documentation fix that cannot wait.
- The `-alpha` suffix now hangs on whether a build was handed a version at all, rather than on
  `GITHUB_REF_TYPE` — a check that meant something when releases came from tag builds and nothing since.

### 2026-09-14 — The repository brought to the standard toolchain
- Merged into `main` at the end of the session. The first `Release` run then proved its own safety net:
  with no previous tag semantic-release would have called this 1.0.0, and the workflow refused to
  reach it — **tag `v0.1.0` before configuring the publishing identity**, or the first automated
  release leaves 0.x on its own.
- Community, security and supply-chain files; dependency automation, coverage, a formatting gate and API
  compatibility validation; versions and releases derived from the commit history; a generated API
  reference, a runnable sample, and the standard layout (`benchmarks/`, `docs/website`).
- The decision log became **twenty-nine records** in `docs/adr/`, each with its context, its rejected
  alternatives and what would reopen it. The index maps every record back to the `Dnn` identifier that
  older commits cite, so nothing written before this stops resolving.
- The conventional-commits check had been failing on every push for a reason it did not name: version 6
  of the action refuses a `.js` configuration file and wants `.mjs`, and reports it as "you have commit
  messages with errors". Since semantic-release computes the version from those same messages, a check
  that cannot run is not cosmetic. Once it ran it refused one commit from 2026-09-13, whose subject
  started with a capital; the message was corrected and the branch re-pushed, so
  `config-conventional` stays enforced in full rather than relaxed to accommodate it.
- **CodeQL's first run paid for itself**: a buffering stream never disposed, a dead assignment in the
  cross-reference reader, and `GetWindow` testing its own type instead of asking the source whether it
  can serve bytes without copying. All three fixed; the third left the design better than it found it.
  Four quality queries are excluded in `.github/codeql/codeql-config.yml`, each with its reason — one of
  them flags every call to `Path.Combine` whatever its arguments, so the single place a path arrives from
  outside the repository is guarded in code instead.

### 2026-09-14 — M1 closed, and what fuzzing found on its first run
- Mutation fuzzing of the reader and the parser, seeded from the corpus: bit flips, corrupted digits,
  truncation, spliced bytes and broken keywords, each input asserted to end in a result or a typed
  exception within a time and an allocation budget. Seeds are deterministic, so a failure replays from
  the number printed in the message, and the offending bytes are written out.
- **It found a process-killing defect within a minute.** A mutated invoice drove `LoadRegularObject` and
  `RelocateAndLoad` into mutual recursion — the index named one offset, the neighbourhood search answered
  with another that failed to parse the same way, and the two called each other 3 978 times until the
  stack ran out. A file that kills the process is exactly what invariant 4 forbids, and no hand-written
  test had thought to try it. Relocation is now three counted attempts with no path back into loading.
- A campaign of 75 000 mutated inputs then ran clean in 76 seconds. A nightly workflow runs 20 000
  mutations per seed document.
- **M1 is closed**: every exit criterion ticked, corpus acceptance green in CI, both test levels in place,
  documentation published.

### 2026-09-13 — The build keeps no warnings
- `TreatWarningsAsErrors` on, analysis at `latest-recommended`, code style enforced in the build, XML
  documentation required on the public API (D28). T01 and T02 closed.
- Four real defects in production code, all worth the trouble: an override that did not chain to
  `base.Dispose`, and two return types wider than what the method can return. Ten more in the tests:
  culture-dependent parsing and formatting, a `JsonSerializerOptions` rebuilt on every call, a type owning
  an undisposed stream, a constant array allocated per call.
- Three suppressions, each at the symbol that triggers it and each with a reason: `PdfDictionary` and
  `PdfStream` keep the specification's vocabulary, and an xUnit collection definition is named after its
  collection. One scoped exception in `.editorconfig`: test names carry underscores because they are
  sentences.

### 2026-09-13 — Two test levels and a documentation site
- Test stack settled (D25): xUnit v3, **AwesomeAssertions** in place of Shouldly, **NSubstitute** where an
  interaction is what needs asserting — the first such test pins that the parser resolves an indirect
  `/Length` exactly once, which is a statement about a call and not about a value.
- Shared fixtures extracted into `tests/AdCodicem.Pdf.TestSupport`, so both suites read one manifest.
- `tests/AdCodicem.Pdf.IntegrationTests` runs the independent referees in **containers** (D26). qpdf now
  cross-checks every corpus document: its page count against the manifest, and its own verdict on which
  documents are damaged. Where no Docker daemon exists the 47 tests skip with the reason attached rather
  than failing, which is what makes the suite usable in a sandbox.
- A **Docusaurus site** in `website/` (D27) publishes the user-facing documentation and `docs/` unchanged,
  deployed to GitHub Pages. Its build runs in CI, so a project document that does not build is caught
  before the default branch. Docusaurus 3.9 had to be taken to 3.10: the older release pairs with a
  webpack whose progress-plugin schema it violates, and the build fails on a validation error that says
  nothing about the cause.
- The **definition of done** now has six points in `docs/roadmap.md`, and every milestone carries a
  Documentation section: unit tests, integration tests and documentation are conditions of closing a
  milestone, not follow-up work (invariant 11).
- The referee earned its keep on its first CI run by contradicting an assumption of mine: I had asserted
  that every damaged document makes qpdf complain, and **junk before the header does not** — qpdf shifts
  every offset silently and reports a sound file. The manifest now records `refereeCheckSucceeds` per
  document, filled in by running the very command the container runs, so the expectation is observed
  rather than assumed. A first attempt to author it through pikepdf was worse than useless: `Pdf.check()`
  does not exist in pikepdf 10, and a broad `except` turned that into "the referee rejects everything".

### 2026-09-13 — Packaging settled, and the wanted-documents specification
- Package identifiers confirmed and checked as unclaimed: `AdCodicem.Pdf` plus `.Validation`, `.Html`,
  `.AspNetCore`, `.FacturX`, `.Rendering`, `.Signing` (D23). The `AdCodicem.` prefix is to be reserved on
  nuget.org with the first publish.
- Publication switched to **trusted publishing** (D24): the release workflow exchanges a GitHub OIDC token
  for a nuget.org key valid one hour and usable once, so no long-lived secret exists. `docs/releasing.md`
  records the exact policy fields — a mismatch on the workflow file name or the environment is what breaks
  this setup, and it breaks it silently until someone reads the error.
- `docs/corpus-contributions.md` specifies the twelve document types still wanted (W01 to W12), with the
  milestone each unblocks, what makes a sample usable, an anonymisation checklist, and the difference
  between the public and private corpora.

### 2026-09-13 — Validation and repair milestones, and a defect the corpus found
- Two milestones inserted at the user's request: **M2 document validation**, right after reading, and
  **M4 repair**, right after writing — repair produces a sound file, so it needs the writer. Everything
  after them shifted by two; the roadmap now runs to M14.
- M2 is specified as a rule engine with stable finding identifiers, of which PDF/A and PDF/UA become
  profiles in M12 (D20). M4 is specified as findings-driven and conservative by default, writing an
  incremental update so signed bytes survive (D21).
- Eight conformance fixtures from the veraPDF corpus vendored under CC BY 4.0 with a NOTICE (D22), taking
  the corpus to 27 documents. `git clone` of public repositories works through the sandbox proxy, so
  public corpora need no manual help.
- **Those third-party files immediately found a real defect**: a validly compressed *empty* stream — an
  empty content stream, an empty appearance, both commonplace — decodes to zero bytes, which the Flate
  filter was reading as failure and answering with the compressed bytes plus a spurious warning. Success
  is now reported explicitly rather than inferred from the length of the output, and filter diagnostics
  carry the offset of the stream they concern, because "a Flate stream could not be decoded" with no
  location is not actionable.

### 2026-09-13 — The corpus of real documents
- 19 documents built by four real producers — Chromium (Skia backend), LibreOffice, ReportLab, qpdf —
  covering invoices, a multi-page report, a contract, an interactive form, a scanned page, a PDF/A-2b
  export, a linearised file, an object-stream rewrite, an AES-256 encrypted file, a 1000-page document,
  and five copies damaged on purpose. 2.7 MB, committed, reproducible via `tests/corpus/build`.
- Expectations in the manifest are established by an **independent tool**, never by our own reader,
  including what qpdf can still recover from each damaged file.
- `CorpusReadingTests` turns the M1 acceptance conditions into 45 executable tests: every document opens
  as described, damaged ones report their damage, well-formed ones produce no repair and no warning, page
  counts match across all four producers, opening never reads content, and indexing the 1000-page document
  holds inside a measured 4 MB budget.
- Three real defects in the expectations surfaced immediately, and were worth the exercise: a wrong
  `/Length` is only noticed when the stream is actually read (the lazy reader working as designed, so the
  test now reads everything before judging), shifting every offset breaks `startxref` itself so the whole
  index is rebuilt rather than relocated object by object, and a process-wide allocation counter is
  meaningless in a parallel suite.

### 2026-09-13 — English throughout, and corpus-based acceptance
- All project documentation rewritten in English (D19). The convention is now: everything in English, code
  and documentation alike.
- Every milestone now carries **acceptance conditions** expressed against real documents, and `docs/corpus.md`
  defines where those documents come from, how they are catalogued and what closing a milestone requires
  (D18, invariant 10).
- The corpus can be produced in-container and committed: Chromium's Skia backend, LibreOffice, and Python
  producers from pypi give genuinely different cross-reference shapes, font handling and object stream use.

### 2026-09-13 — M1, slices 1 to 7
- COS object model, tolerant lexer and parser, decoding filters, all four index shapes (classic table,
  cross-reference stream, object stream, `/Prev` chain, hybrid files), lazy resolution with a bounded
  cache, repair by scanning, structured diagnostics.
- Hardening: every allocation a file could dictate is bounded (a stream length clamped to the real file
  size, an object stream's object count clamped to what its header could hold), and cycles — references,
  `/Prev`, an object stream containing itself — all terminate.
- A file with no usable object is refused with a typed exception rather than opened empty.
- 73 tests, including a class devoted to hostile input with a per-test time budget.
- Tests build their PDFs byte by byte with exact offsets, then damage them on purpose: no network
  dependency and no binaries in the repository. Real documents come next, as the corpus.
- Tooling: the .NET 10 SDK installs from the Ubuntu archive; `dotnet test` now requires
  Microsoft.Testing.Platform (opted into via `global.json`) and the `--solution` form.

### 2026-09-12 — Framing and foundations
- Scope settled in two steps: HTML → PDF generation first, then extended to full manipulation of existing
  documents. Nineteen decisions recorded in `docs/adr/`.
- Documentation frame established: `CLAUDE.md` (session frame), `architecture.md`, `decisions.md`
  (since converted into `docs/adr/`),
  `roadmap.md` (M0 to M12), `corpus.md`, `milestones/` (per-milestone specification), this file.
- Solution skeleton: core, HTML engine, ASP.NET Core integration, tests, benchmarks; `net10.0` target,
  central package management.
- Environment: the .NET SDK is absent from web sessions and Microsoft's servers are blocked by the network
  policy. Worked around through the Ubuntu archive (`dotnet-sdk-10.0`), automated by the `SessionStart` hook.

## Debt and open points

| # | Subject | Decision expected |
|---|---------|-------------------|
| ~~T01~~ | ~~`TreatWarningsAsErrors` is off while the foundations settle~~ | Done: on across the solution, analysis at `latest-recommended` |
| ~~T02~~ | ~~XML documentation (`CS1591`) is not enforced on the public API~~ | Done: required, and the public API already satisfied it |
| ~~T03~~ | ~~The real-document corpus is not built yet~~ | Done: `tests/corpus`, 27 documents, four producers plus vendored fixtures; 68 since 2026-09-24 |
| T10 | **Narrowed on 2026-09-24**: Word, PDFMaker, Acrobat, InDesign, LiveCycle, PDFWriter, copier scans with their own OCR, Java writers, ERP Factur-X samples, PDF 1.2 archives, signatures — DocuSign's among them since the third pass — and other producers' PDF/A are now in the corpus, found in public sources (`docs/corpus-sources.md`). Still missing from what may be committed is what only an inbox holds: a real invoice or statement from a supplier or bank (real ones are in the remote corpus only), a Yousign, Universign or Adobe Sign signature, a copier file untouched since the copier wrote it, Hebrew | Contributions, per the "still wanted" column of `docs/corpus-contributions.md`; the remote corpus (ADR 32) for files that can be used but not redistributed |
| ~~T21~~ | ~~**The reader reports a truncated stream that is not.** When a stream's data ends inside the parser's 8 KB window but its `endstream` falls past the window's end, `PdfObjectParser.ReadStream` finds no `endstream` in the window and reports `stream.truncated`, cutting the stream at the window. Found on object 49 of the USGS Washington West topographic map (W11 reference, `docs/corpus-sources.md`): data from 68 to 8,185 in a 8,192-byte window; qpdf reads it cleanly. The same file also earns a `filter.failed` on its 14.9 MB Flate image, not yet explained. The map is now in the remote corpus, recorded as unsupported with this reason, so the fix is checked against it every night. Since 2026-09-25 also the FDA hospital-bed guidance from GovDocs1 (remote): objects 604 and 2053, streams of about 8.1 KB whose `/Length` is right~~ | Done on 2026-09-26: the attempt through a window too small for its stream is dropped with what it reported, and a stream whose `endstream` may lie past the window is confirmed by 13 bytes read from the file. The hospital-bed guidance is supported; the map is unsupported for T28 instead |
| T22 | W11 has no committed document, by decision. All three of its references — 9,302 pages, one 63 MB page, and since the third pass of 2026-09-24 the heavy scan (USGS Professional Paper 1, 147 MB of JPEG 2000) — are in the remote corpus (ADR 32) and tested every night, but not in the main CI job | M13: state its memory budgets against the remote documents, and close only on a green `Remote corpus` run |
| ~~T23~~ | ~~**The reader cuts an indirect object longer than its 8 KB window at the window's edge.** Found on two remote documents: object 458 of the EU DSS file with 24 signatures and a document timestamp, a DSS `/VRI` dictionary of 10,112 bytes, reported as a truncated object exactly 8 KB in; and object 14 of the BOE's 2015 law, a structure array of 8,694 bytes, reported as unexpected tokens at the same point, with 35 arrays like it. qpdf reads all of them whole. `PdfFileReader.TryParseObjectAt` does grow its window when the parser says an object ran out, but the parser has warned into the document's diagnostics by then. The same window as T21, met by an object rather than a stream. Both entries are recorded as unsupported with this reason, so the fix is checked against them every night. Since 2026-09-25 also the VA Kernel guide (object 10913, 14,188 bytes) and a JHOVE poster (object 2307, 8,248 bytes), both remote~~ | Done on 2026-09-26: only the attempt that is kept reports, a cut at the buffer's end is noticed wherever it falls, a classic table's window grows for a cut keyword and a cut trailer is parsed again in a window of its own, up to 64 KB. The four documents are supported. The rebuild's trailer scan keeps its fixed window (T30) |
| T24 | **Opening reads each cross-reference section through a window of up to 64 KB, whatever the section's size.** Bounded, but proportional to the number of sections rather than to their size: opening the 218 KB signed Web Capture file from pdfcpu's test data, which has three sections, reads 117 KB — more than the quarter of the file the laziness test allows. The entry is recorded as unsupported with this reason | M13, with the other budgets: start a section's window small and grow it, as object windows already do — and keep what was read when it grows: the VHA coding handbook from GovDocs1 (2026-09-25) has one 273 KB table, read at 64 KB, then 256 KB, then to its end, 683 KB in all for a 2.2 MB file; also recorded as unsupported |
| T25 | **A `/Prev` that misses its section drops it in silence.** `PdfFileReader.TryReadXRefChain` returns success as soon as one section was read, so when a later `/Prev` does not land on `xref` or on a cross-reference stream the older section is simply left out, with no rebuild — and with no diagnostic either when the offset falls inside the file; one past its end earns `xref.entry-out-of-range`. Found on IBM's QMF manual from GovDocs1 (remote): `/Prev 1569328` falls 12 bytes past the keyword, and the 4,106 entries of the main table are lost; qpdf reports `xref not found` and rebuilds. Recorded as unsupported with this reason | **Before M2 closes** — a validator cannot report what the reader hides: a synthetic regression test (a `/Prev` a few bytes off, and one pointing nowhere), then report the failed section and search near it or rebuild, as the reader already does for an object a few bytes off |
| ~~T26~~ | ~~The remote corpus cannot take a file out of an archive, so the one external test suite for M2's structural profile stays out of reach~~ | Done on 2026-09-25: [ADR 33](adr/0033-a-remote-document-may-be-a-member-of-a-pinned-archive.md) accepted and implemented; the 88 files are in the remote corpus, 19 of them recorded as unsupported until M2 and named in its acceptance conditions |
| T27 | **A reference to an object the file lacks makes the reader rebuild its whole index.** The specification says such a reference is null, and qpdf takes it so; the reader instead scans the file for the missing object — the lazy rebuild meant for an index that lost entries — and reports a repair on a file qpdf calls clean. Found on two iPRES 2017 files (remote): a catalogue whose `/Pages` and a page whose `/Contents` point at object 9, which does not exist. Both are recorded as unsupported with this reason. The acceptance test saw it only once it walked each page's contents and resources before judging a clean file's diagnostics; across the whole corpus, no other document was affected | Before M2 closes, since its acceptance conditions name both files: a synthetic regression test (a sound file with a reference past /Size, and one to a free entry), then rebuild only when the index gives reason to doubt it, and otherwise take the reference as null — a finding for M2, not a repair |
| T28 | **A stream that decodes past about 2 GB cannot be read whole, whatever the options.** A decoded stream is returned as `ReadOnlyMemory<byte>`, which holds at most `Array.MaxLength` bytes. Below that the bound is an option since ADR 34, `PdfReaderLimits.MaxDecodedStreamLength`, 256 MB by default: the USGS topographic map (remote), whose 9,600 × 11,410 RGB image decodes to 328,608,000 bytes, reads whole with `readerLimits` at 512 MB, and the defaults keep its first 256 MB and report `limit.decoded-stream`. Past the ceiling, even `PdfReaderLimits.Unbounded` keeps the first 2 GB and reports the same code. No corpus document reaches it | M13, with the memory budgets: decode such a stream a piece at a time rather than into one array, in native memory if a measurement asks for it (ADR 35) |
| ~~T29~~ | ~~**A chain of `/Length` references nests object loads as deep as the chain.** Resolving an indirect `/Length` loads that object while the first is being parsed, and a stream whose `/Length` points at a stream whose `/Length` points at another goes one level deeper each time. The cycle guard stops a loop, not a chain: 20,000 such objects overflow the stack and kill the process, which invariant 4 forbids. Older than T23's fix, reproduced on it~~ | Done on 2026-09-26: object loads nest at most 64 deep; the next one reads as null, is not cached, and is reported once as `syntax.depth-exceeded` with its offset. `HostileInputTests` reads a 50,000-level chain |
| T30 | **A rebuild reads a 64 KB window at every `trailer` keyword.** `ScanForTrailers` parses each occurrence through its own fixed window, so a damaged file made of the keyword costs about 8,000 bytes read per byte of file: 80,000 occurrences (625 KB) open in 0.3 s from a file — linear, but an amplification the file controls It also parses each through that fixed window straight into the document's diagnostics, so a sound trailer longer than 64 KB earns a syntax error the file does not have when a rebuild scans for it. The scan keeps its fixed window whatever `PdfReaderLimits.MaxTrailerLength` says, so raising the option adds no amplification; on opening, a trailer past 64 KB is now reported as `limit.trailer` and read whole under a raised `MaxTrailerLength` (ADR 34) | M13, with the budgets: a small window grown on demand, as objects have, and occurrences inside a stream's data skipped |
| ~~T31~~ | ~~**Two filters keep their bound badly.** `RunLengthDecode` has none: every two bytes in can decode to 128 out, so a 4.6 MB stream decodes to 294 MB, past the 256 MB `PdfFilterLimits.MaxDecodedLength`, with no diagnostic, and a Flate stream feeding it multiplies that by 64 — memory a hostile file chooses. `LZWDecode` stops at the bound in silence. Found by the review of T23's fix (a claim that every filter keeps the bound); older than it~~ | Done on 2026-09-26: every filter keeps exactly the first 256 MB and says whether it had more; the pipeline reports it as `filter.limit-exceeded`, the reader's limit (renamed `limit.decoded-stream` by ADR 34, before any release); first buffers are capped by the bound |
| T32 | **A Flate stream whose tail was lost decodes to what is left, in silence.** .NET's `ZLibStream` treats the end of its input as the end of the data, so no exception reaches `FlateFilter`, whose handling of a lost tail was written for one: a zlib stream cut in half decodes 27,939 of its 58,890 bytes with no diagnostic (measured). zlib does check a complete Adler-32 trailer — a wrong one throws, and is reported — but not a missing one, and the trailer cannot be found by position, since a stream's `/Length` often takes in the end-of-line after it. .NET 10 exposes no inflater that says whether it reached the final block. LZW has the same silence: a stream that uses a code it never defined stops there and keeps what came before, without a word (pinned by `FilterTests.Stops_an_lzw_stream_at_a_code_it_has_not_defined`, found while covering #28's patch) | Before M2's stream rules ("filters decodable"): read the input through a stream that notices the inflater asking for bytes past the end of the data, which a complete stream never does, and report that as a truncated stream; and have the LZW decoder report the code it could not read |
| T33 | **Every decoded object stream stays cached for the life of the document.** `_objectStreams` in `PdfFileReader` has no bound and no eviction, and a rebuild decodes every object stream up front to index its objects. A damaged file of four object streams that each decode to the 256 MB bound (1.2 MB) holds 1 GB once `Open` returns (measured by the review of T31), and raising `PdfReaderLimits.MaxDecodedStreamLength` (ADR 34) multiplies that; a large sound document holds its object streams' decoded bytes however few objects are read, against invariant 2 | M13, with the memory budgets, and sooner for the hostile case if a file of the kind turns up: a budget on the decoded bytes the cache holds, evicting the oldest — an evicted stream is decoded again when one of its objects is asked for |
| T34 | **An object stream whose `/DecodeParms` names an object stored in that same stream reads that object as null, in silence, for good.** Decoding the stream resolves the parameter while the stream is being loaded; `GetObjectStream` marks it as unavailable meanwhile, so the object reads as null, and `GetObject` caches the null with no diagnostic. Any parameter the pipeline reads can do it: `/Predictor`, and `/EarlyChange` for LZW, always; `/Colors`, `/BitsPerComponent` and `/Columns` once there is a predictor above 1. Found by the review of #28's coverage; older than it | M2's object-graph rules: report the self-reference, and do not cache a null the reader produced while an object stream was still being loaded |
| ~~T11~~ | ~~Publishing is configured but untested~~ | Done, and **observed**: four previews are on nuget.org, pushed through the OIDC exchange. No secret is involved — the account is `NUGET_ACCOUNT` in `release.yml` |
| ~~T12~~ | ~~GitHub Pages is not enabled, so the site builds but does not publish~~ | Done, and the diagnosis was wrong: Pages was enabled; no deployment had ever been *run*. Dispatched `Documentation` on 2026-09-19, it went green first time, and the site served 44 pages plus the API reference — **served, not rendered**: every user-facing page was broken, which only a look at one would have shown (2026-09-22). The three Pages action bumps of 2026-09-16 are now observed rather than reasoned |
| T13 | The integration suite has one referee (qpdf); veraPDF, pdftotext and a rasteriser join it as their milestones arrive | M10, M12, M14 |
| T14 | Codecov is linked and the badge reads 82.61%, uploaded tokenless (`Token length: 0` in run 84) — so the original entry, "not linked, badge stays empty", is closed. What is left is narrower: the Codecov **GitHub App** is not installed, so it comments as `codecov-commenter` rather than `codecov[bot]` and warns on every pull request that uploads and comments are not reliably processed | Install the Codecov GitHub App on the repository |
| T15 | **Half done, and the missing half now blocks the stable release**: the "Default" ruleset on `main` exists since 2026-09-22 (pull request, one code-owner approval, linear history, CodeQL, coverage), bypassed by repository admins only. GitHub Actions is not a bypass actor, so the stable release's push of its `chore(release)` commit will be refused. The original entry, for the record: auto-merge **is** allowed on the repository; what was missing is a ruleset on `main`, so it still accepted direct pushes and the Dependabot auto-merge workflow had no required check to wait for. **Measured cost**: Scorecard's `Branch-Protection` is 0/10 at weight 7.5, and `Code-Review` is 0/10 at the same weight because nothing here has ever been approved — together about **1.5 points** of the overall score, the largest block left | A branch ruleset on `main` requiring the five pull-request checks, non-strict, **with a bypass for GitHub Actions** — `@semantic-release/git` pushes the `chore(release)` commit straight to `main`, and a ruleset without that bypass fails the stable release in `prepare` |
| T16 | The API baseline is one version for the whole solution, checked against `AdCodicem.Pdf` only. A satellite first shipped in a later release — `AdCodicem.Pdf.Validation` in M2 — has no package at that version, and its pack fails with `NU1101` exactly as `v0.1.0` would have | In M2, before `AdCodicem.Pdf.Validation` is packable: make the baseline per package |
| T17 | One dependency in CI is still unpinned: `dotnet restore` in `ci.yml` has no `--locked-mode`, because no `packages.lock.json` is committed. Measured at 10 of Pinned-Dependencies' 144 weighted units — 0.05 of the displayed score — and `RestorePackagesWithLockFile` in `Directory.Build.props` fails the restore with `NETSDK1013` | When it buys something beyond the check: set the property **per project**, where it works, commit the six lock files, and add `--locked-mode` to `ci.yml` |
| T18 | No OpenSSF Best Practices badge, so `CII-Best-Practices` is 0/10 at weight 2.5 — about 0.26 of the overall score | Register the project at [bestpractices.dev](https://www.bestpractices.dev), answer the questionnaire, put the badge in `README.md` |
| T19 | `Signed-Releases` is unscored (-1) only because no release exists. The moment one does it becomes a scored High check, and nothing in `release.yml` attaches a signature or a provenance bundle to the GitHub Release | Before the first stable release: attest the packages and upload the bundle as a release asset, so the check has a `.intoto.jsonl` to find |
| T20 | The `AdCodicem.` prefix is not reserved on nuget.org. Now that packages exist under it, anyone else can publish `AdCodicem.Anything`, and ours are not marked as coming from a verified owner | Email account@nuget.org with the owner display name and the glob `AdCodicem.*` — there is no self-service button; `docs/releasing.md` has the criteria |
| T04 | An OFL font set must be embedded for default rendering | During M6 |
| T05 | A public API test (a baseline of exported signatures) | Put in place at the start of M7 |
| T06 | `PdfString.ToText` reads Latin-1 rather than full PDFDocEncoding (the 32 positions 0x80-0x9F differ) | Before the first public release |
| T07 | The object cache evicts FIFO rather than LRU; names are interned through an intermediate string | M13, with measurements |
| ~~T08~~ | ~~Fuzzing of the lexer and parser is not set up~~ | Done: in the suite per commit, and a nightly campaign |
| T09 | A memory budget is now enforced in CI; a throughput budget is not | Throughput budget in M13 |
