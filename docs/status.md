# Project status

A living file, updated **at the end of every session**. It describes the real state, not intentions.
Keep it short: summarise the journal once it passes a dozen entries — the detailed history is in git, not
here.

## At a glance

- **Current milestone**: M2 — Document validation (`docs/milestones/M2.md`), in progress. Where it lives and
  what its public API is was settled by ADR 36 before the first type was written; slice 1 — the engine, the
  report and `file.eof-missing` — is done, in pull request [#29](https://github.com/AdCodicem/AdCodicem.Pdf/pull/29).
- **Last milestone closed**: **M1 — Object model and tolerant reading**
- **Tests**: 1,327 unit (7 skipped by design) + 302 integration (skipped without Docker) + 23 for the remote
  corpus's fetcher. With the remote corpus: `Remote corpus` run 7, on #29's branch at `4aa6816` with all 242
  documents, passed 2,844 unit (97 skipped by design, on documents recorded as unsupported until M2, T24, T25
  or T27) and 668 integration tests; the review's four tests came after it. Here, with 233 of the 242 —
  seven hosts reset this session's connections and the two GitHub attachments answer 403 —, 2,802 unit.
- **CI**: green on `main` at `74ce382` (CI run 198). Release run 27 published `0.1.1-preview.27` and
  redeployed the preview's documentation.
- **Corpus**: 168 committed documents, 23.0 MB — 19 generated here, 3 from Word and PDF24 on Windows, 146
  third-party files under attribution-only licences (`docs/corpus-sources.md`). Beside them, a **remote
  corpus** of 242 documents we may use but not redistribute, fetched at a pinned SHA-256 and size (ADR 32),
  88 of them out of their authors' archive (ADR 33), and tested every night by `Remote corpus`: run 5, on
  `main` on 2026-09-26, and run 6, on the ADR 34 branch, both green. All 410 are described in
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
- **Coverage**: 89.13 % on Codecov for `74ce382`, uploaded without a token; the Codecov app is not installed
  (T14).
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
| Validating under the structural profile, the document already open | synthetic, 1000 pages | 86 ns | 232 B |

The gap between the first two rows is the library's promise: opening a document does not read its content.
The validation row is one rule reading the file's last 1,024 bytes, whatever its size; it grows with each
slice of M2.
Indexing costs roughly 200 bytes per object, whatever the objects weigh. The third row is asserted as a
budget in CI (`CorpusReadingTests`), so an allocation regression fails the build.

## Next concrete step

M2 — document validation (`docs/milestones/M2.md`), slice 1 done (#29). In the order its debts impose:

1. **T32** as its own change: a Flate stream that lost its tail, or an LZW stream that stops at a code it
   never defined, decodes without a word — through `PdfStream.Decode`, public API the previews already ship.
2. **T25**, then **T27**, before slice 2 (file and cross-reference rules) is baselined: a validator cannot
   report what the reader hides, and T27's fix wants T25's report of a failed section as its reason to
   doubt the index.
3. Slice 2, which also answers for iPRES `t04-007` (a premature `%%EOF` before the trailer); slice 3 with
   **T34** in the object-graph rules; slices 4 to 6. Each slice adds to the manifest's `findings` what its
   rules report, and every document is held to exactly its list.

A stream, object or section the reader cut at one of its limits (`limit.*`, ADR 34) is the reader's limit,
not a fault of the file: the rules on it report at most, as information, that it was not checked whole.

## Journal

### 2026-09-26 — CodeQL left to GitHub's default setup, and the merged branches
- **T35, the maintainer's choice**: GitHub's default setup stays; `.github/workflows/codeql.yml`, disabled
  since 2026-09-22, and `.github/codeql/codeql-config.yml` are deleted, and the ADR index says what runs now.
  Nothing that depends on CodeQL changes: the default setup posts the check runs Scorecard's SAST reads — 10/10
  on `74ce382`, already under the default setup — and meets the ruleset's code-scanning rule, as #29's checks
  show. The four queries the configuration excluded may now raise alerts; the index says to dismiss them with
  the reason the deleted file gave, which git keeps.
- **The five merged branches** stay on the remote: asked to delete them, this session was refused by its git
  proxy (HTTP 403), which lets it push only to the branches it created. Their tips are recorded under
  *At a glance*.

### 2026-09-26 — ADR 36 and M2's first slice: validation in the core, and `file.eof-missing`
- **The question, then the decision.** Asked what came next, the answer was M2's first slice — but not before
  settling where its public types live, since every merge publishes them: the roadmap put the engine in an
  `AdCodicem.Pdf.Validation` satellite and `PdfRepair` in the core, driven by findings, which invariant 1 rules
  out. The maintainer accepted ADR 36 — the engine and the structural profile in the core, the PDF/A and
  PDF/UA profiles in `AdCodicem.Pdf.Conformance`, an identifier never published — and answered what it left
  open: identifiers `family.name`, never a reader code; an instance with immutable options as the entry
  point; the `PdfLimitExceededException` a caller asked for passes through; the engine internal until M12;
  `file.eof-missing` read over the last 1,024 bytes; every corpus document held to exactly its findings.
- **Previews carry no guarantee**, at the maintainer's request: an addition to ADR 30, said wherever a preview
  is offered — the releasing guide, the README the package ships, the site's introduction and banners,
  CONTRIBUTING, `CLAUDE.md` (which also dropped the fixed development branch it still named).
- **What was built.** `PdfValidator` (stateless) and `PdfValidatorOptions`; `ValidationProfile.Structural`,
  version 1; `PdfValidationReport`, which keeps at most `FindingCapacity` findings in the order found and
  counts every one; `PdfValidationFinding`, `PdfValidationSeverity`, `PdfValidationLocation`,
  `PdfValidationRuleIds`. One rule: `file.eof-missing`, a warning when no `%%EOF` lies in the file's last
  1,024 bytes — the tolerance readers extend, not a reader guard, since a valid file ends with the marker —,
  reading those bytes and nothing else. `docs/validation-rules.md` lists it, and a test holds the table to the
  code.
- **The corpus.** A manifest entry gains `expect.findings`, established from the file: eleven documents lack
  the marker and declare it — the truncated invoice, five PDFBox and pdf.js files cut short or with junk
  appended, two JHOVE files, and iPRES `t04-002` to `t04-004`, which qpdf accepts, so a warning is right there.
  The other iPRES end-of-file cases stay silent, each for a reason M2 records; `t04-007` only because the file
  is shorter than the window. `CorpusExpectation` refuses unknown keys; `build_corpus.py` writes the finding
  for the tail it cuts.
- **Tests.** The engine's order, capacity, counts, determinism and refusals; the rule on every shape of end of
  file, the 1,024-byte edge one byte at a time, and the bytes it reads; a property over generated trailing
  bytes; the identifiers' grammar; `CorpusValidationTests` over every document — reported on without a throw,
  unsupported ones included, exactly the declared findings, no error on a well-formed file nor on a PDF/A
  failure, the same report twice.
- **On review.** Four reviewers — correctness, the repository's rules, the tests by mutation, the
  documentation — and two skeptics on each of their 22 findings. Upheld and fixed: ten behaviours no test
  held (six mutations checked killed afterwards), M2's account of three iPRES end-of-file cases and of the
  acceptance row the strict one replaced, a stale comment, and this file. Refuted: a roadmap state, two
  tests said to be vacuous, the skipping of unsupported entries. One was real and older than the change:
  a `PdfFileSource` whose `Read` returns short counts misleads the whole reader, and now the rule — **T36**.
- **Housekeeping.** This file's "At a glance", a week stale, re-checked against GitHub, nuget.org, Scorecard
  and Codecov, and the journal summarised; T20 closed, T35 opened; the package's description, which promised
  a writer, now says what it holds.
- **Measured.** `ValidationBenchmarks`, ShortRun: 94 ns and 232 B for 10 pages, 86 ns and 232 B for 1,000.
  The reader's benchmarks are unchanged: 231 µs and 392.92 KB to index 1,000 pages.

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

### Before 2026-09-26, in brief

The detail is in git and in the pull requests; what still matters is in the records and in this file.

- **2026-09-12 and 13 — foundations and M1.** Scope, the first nineteen decisions (now ADRs), the solution
  skeleton, the .NET SDK installed from the Ubuntu archive. M1 in seven slices: the object model, a tolerant
  lexer and parser, the filters, all four shapes of index, lazy resolution with a bounded cache, repair by
  scanning, structured diagnostics, every file-sized allocation bounded. English throughout; acceptance on
  real documents (19 from four producers, expectations from independent tools, never from our reader); two
  test levels, the second running qpdf in a container; a Docusaurus site; warnings as errors. Validation (M2)
  and repair (M4) were inserted then, so milestone numbers in commits older than 2026-09-13 follow the
  previous order.
- **2026-09-14 — M1 closed.** Mutation fuzzing found a mutual recursion that killed the process within a
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
- **2026-09-24 — the corpus from public sources.** Documents screened for licence and personal data, the
  rules on names relaxed with the maintainer; a remote corpus for what may be used but not redistributed
  (ADR 32), one manifest for every document, and over 2 MB a document goes remote. T21, T23 and T24 found.
- **2026-09-25 — more corpus, and fuzzing.** The OPF format-corpus screened file by file: 56 committed, 67
  remote, 161 refused. The iPRES 2017 hand-built set fetched out of its authors' archive (ADR 33). The
  nightly fuzzing campaign, which had filled its runner's disk, now starts from one document per reader
  structure plus a rotating share, for 43 % of the cost. T25, T26 and T27 found.

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
| T14 | Codecov is linked and reads 89.13 % on `74ce382`, uploaded tokenless. The Codecov **GitHub App** is not installed, so it comments as `codecov-commenter` rather than `codecov[bot]` and warns on every pull request that uploads and comments are not reliably processed | Install the Codecov GitHub App on the repository |
| T15 | **The ruleset exists; its missing bypass blocks the stable release.** The "Default" ruleset on `main` (since 2026-09-22) asks for a pull request with one code-owner approval and resolved threads, linear history, CodeQL and 61 % coverage, and is bypassed by administrators only. GitHub Actions is not a bypass actor, so the stable release's push of its `chore(release)` commit will be refused. It names no required status check, so a pull request can merge with CI red if a reviewer approves. Scorecard's Branch-Protection reads 5/10 and Code-Review 0/10, at weight 7.5 each | Before the first stable release: add GitHub Actions as a bypass actor (or have the release open a pull request), and require the CI checks by name |
| T16 | The API baseline is one version for the whole solution, and `published-baseline.sh` asks nuget.org about `AdCodicem.Pdf` only. A satellite first shipped after a stable release has no package at that version, and its pack — so the whole solution's, previews included — fails with `NU1101` on every run until the logic changes. Harmless while no stable release exists (the baseline is empty). ADR 36 took it out of M2, which ships no package | Before the first satellite ships after a stable release (`.Html` in M7 at the latest): a baseline per package, one pack script shared by both release paths, and CI running it so the failure shows on the pull request |
| T17 | One dependency in CI is still unpinned: `dotnet restore` in `ci.yml` has no `--locked-mode`, because no `packages.lock.json` is committed. Measured at 10 of Pinned-Dependencies' 144 weighted units — 0.05 of the displayed score — and `RestorePackagesWithLockFile` in `Directory.Build.props` fails the restore with `NETSDK1013` | When it buys something beyond the check: set the property **per project**, where it works, commit the six lock files, and add `--locked-mode` to `ci.yml` |
| T18 | No OpenSSF Best Practices badge, so `CII-Best-Practices` is 0/10 at weight 2.5 — about 0.26 of the overall score | Register the project at [bestpractices.dev](https://www.bestpractices.dev), answer the questionnaire, put the badge in `README.md` |
| T19 | `Signed-Releases` is unscored (-1) only because no release exists. The moment one does it becomes a scored High check, and nothing in `release.yml` attaches a signature or a provenance bundle to the GitHub Release | Before the first stable release: attest the packages and upload the bundle as a release asset, so the check has a `.intoto.jsonl` to find |
| ~~T20~~ | ~~The `AdCodicem.` prefix is not reserved on nuget.org~~ | Done by 2026-09-26: nuget.org's search API marks `AdCodicem.Pdf` as verified, and its page says the prefix is reserved |
| T04 | An OFL font set must be embedded for default rendering | During M6 |
| T05 | A public API test (a baseline of exported signatures) | Put in place at the start of M7 |
| T06 | `PdfString.ToText` reads Latin-1 rather than full PDFDocEncoding (the 32 positions 0x80-0x9F differ) | Before the first public release |
| T07 | The object cache evicts FIFO rather than LRU; names are interned through an intermediate string | M13, with measurements |
| ~~T08~~ | ~~Fuzzing of the lexer and parser is not set up~~ | Done: in the suite per commit, and a nightly campaign |
| T09 | A memory budget is now enforced in CI; a throughput budget is not | Throughput budget in M13 |
| T36 | **A `PdfFileSource` whose `Read` returns fewer bytes than asked is taken as the end of the data.** `GetWindow` calls `Read` once, and every read built on it — the header and `startxref` searches, the cross-reference probes, object windows, `FileStreamData.GetBytes`, and now `file.eof-missing` — takes a short count as the end. The two built-in sources fill the buffer (the file one loops), so only a caller's own source can do it; then a sound file is rebuilt (`xref.rebuilt`), streams come back cut (`stream.truncated`) and the validator reports `file.eof-missing` on a file that ends with its marker — measured by the review of M2's first slice with a source serving at most 1,000 bytes a read. The public documentation of `Read` does not say it must fill the buffer | Make `GetWindow` and the direct reads loop until the buffer is full or `Read` returns 0, as `FileSource.Read` does, with a test through a source that returns short reads; say on `Read` what the reader expects |
| ~~T35~~ | ~~CodeQL runs as GitHub's default setup since 2026-09-22, while `.github/workflows/codeql.yml`, disabled, and its configuration stayed in the repository~~ | Done on 2026-09-26, the maintainer's choice: the default setup stays, and the workflow, its configuration and the ADR index's line about them are gone; the index now says what runs, and what to do if one of the four queries once excluded raises an alert |
