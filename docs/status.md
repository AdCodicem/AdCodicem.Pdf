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
  Slices 2 to 6 are issues [#58](https://github.com/AdCodicem/AdCodicem.Pdf/issues/58) to
  [#62](https://github.com/AdCodicem/AdCodicem.Pdf/issues/62).
- **Last milestone closed**: **M01 — Object model and tolerant reading**
- **Tests**: 1,504 unit (8 skipped by design) + 452 integration (skipped without Docker) + 23 for the remote
  corpus's fetcher + 26 for the roadmap's mirror on GitHub. With the remote corpus: `Remote corpus` run 7, on
  #29's branch at `4aa6816` with all 242 documents, passed 2,844 unit (97 skipped by design, on documents
  recorded as unsupported until M02, T24, T25 or T27) and 668 integration tests. Here, on #31's branch with 233
  of the 242 — seven hosts reset this session's connections and the two GitHub attachments answer 403 —, 2,978
  unit (89 skipped by design, on documents recorded as unsupported until M02 or T24) and 1,034 integration, in a
  local referee container.
- **CI**: green on `main` at `74ce382` (CI run 198). Release run 27 published `0.1.1-preview.27` and
  redeployed the preview's documentation.
- **Corpus**: 168 committed documents, 23.0 MB — 19 generated here, 3 from Word and PDF24 on Windows, 146
  third-party files under attribution-only licenses (`docs/corpus-sources.md`). Beside them, a **remote
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
| Decoding a whole Flate content stream | 4 MB decoded, about 330 KB encoded | 8.3 ms | 13.1 MB |
| Decoding the same stream without its checksum, or without its last five bytes | 4 MB decoded | 11.7 ms | 13.1 MB |

The gap between the first two rows is the library's promise: opening a document does not read its content.
The validation row is one rule reading the file's last 1,024 bytes, whatever its size; it grows with each
slice of M02.
Indexing costs roughly 200 bytes per object, whatever the objects weigh. The third row is asserted as a
budget in CI (`CorpusReadingTests`), so an allocation regression fails the build. A stream that ran out is
read twice to tell a lost checksum from lost data (T32), which costs time on damaged streams only; the
second reading keeps nothing. What a 4 MB decode allocates is the output doubling towards its size, T28's
and M23's business.

## Next concrete step

M02 — document validation (`docs/milestones/M02.md`), slice 1 done (#29); T32, T25 and T27, the reader debts
before its cross-reference and object-graph rules, merged with #31. Its progress is the
[M02 milestone](https://github.com/AdCodicem/AdCodicem.Pdf/milestone/3) on GitHub. In the order its debts impose:

1. Slice 2, file and cross-reference rules ([#58]), which also answers for iPRES `t04-007` (a premature
   `%%EOF` before the trailer) and reports what T25 now tells the reader: a section found near where it was
   named, one found nowhere.
2. Slice 3 ([#59]) with [#51] in the object-graph rules, and the finding for a reference to an object the
   file lacks, which T27 left to it; [#55] and [#56] before slice 4 ([#60]), whose stream rules check
   declared lengths and whether filters decode; slices 5 and 6 ([#61], [#62]). Each slice adds to the
   manifest's `findings` what its rules report, and every document is held to exactly its list.

A stream, object or section the reader cut at one of its limits (`limit.*`, ADR 34) is the reader's limit,
not a fault of the file: the rules on it report at most, as information, that it was not checked whole.

## Journal

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
### 2026-09-26 — T25: a cross-reference section `/Prev` misses is found nearby, or reported
- **The defect.** The chain of sections stopped at the first `/Prev` or `/XRefStm` that did not lead to a
  section, and kept what it had read, without a word unless the offset lay past the file's end. IBM's QMF
  manual lost the 4,106 entries of its main table so, its `/Prev` 12 bytes past the keyword.
- **Measured before designing.** Every one of the 401 documents readable here, each object resolved and each
  reference inside it followed: one chain breaks in the whole corpus, the QMF manual's.
- **The fix.** A section the chain names that is not where it is named is looked for within 512 bytes either
  side, as an object is: an `xref` keyword standing on its own — not the end of `startxref` —, or an object
  header that reads as a cross-reference stream, nearest first; found, it is read, and reported as
  `xref.offset-adjusted`. One that is nowhere near is reported under a new code, `xref.section-missing`, a
  warning naming the offset: the chain stops at a missing `/Prev` and goes on past a missing `/XRefStm`, and
  what only the missing section indexed is found by the lazy rebuild when it is asked for — nothing is
  rebuilt at opening that nobody asks for. The places tried are capped at 32 per document, each read through
  a window of up to 64 KB: a file chooses how many headers lie near a section and how many sections miss.
- **Why not rebuild at once, as qpdf does.** The first version did, and the hostile test of a hundred hybrid
  sections whose streams never close read 169 MB instead of a bounded amount: a rebuild parses each of those
  objects to the end of the file. Reporting the section and rebuilding only on demand keeps opening bounded,
  and is the design T27 was recorded to build on.
- **The corpus.** The QMF manual is supported: its section is found 12 bytes before where `/Prev` names it,
  the index holds 4,366 objects without a rebuild, and it counts 429 pages, as qpdf does; its manifest entry
  requires `xref.offset-adjusted` where it expected a rebuild. Twenty-eight remote entries stay unsupported.
- **Tests.** `CrossReferenceChainTests`: a `/Prev` 7 and 2 bytes before its section and 1, 3 and 12 bytes
  after it, read with one repair at the section's real offset; a `/Prev` naming the first object, reported,
  with the rebuild left for the page it alone indexed; a `/Prev` past the end of the file; a hybrid file's
  `/XRefStm` naming nothing, reported, the chain reaching the original section through `/Prev`; and one naming
  its stream 4 bytes before or 6 after, read. Each fails when the search or the report is removed.

### 2026-09-26 — T32: a Flate stream that lost its tail, and an LZW code never defined, are reported
- **The defect.** .NET's inflater takes the end of its input for the end of the data: a Flate stream cut
  short decoded to what was left, and nothing reached `FlateFilter`'s handling of a lost tail, written for an
  exception that never came. An LZW stream stopped in silence at a code it had not defined — and a code past
  the next one to define was worse than silent: decoded as if it were that code, it made up data.
- **Measured before designing.** The framework's inflater, on some 166,000 cases — streams of thirteen
  sizes, four compression levels and three kinds of data, read whole through five buffer sizes and cut at
  every byte, or at 1,500 places for the longer ones —, never asks its source for bytes past the end of a
  complete stream, zlib or raw, whatever follows it, and always does, or throws, for a cut one. .NET 10 has an `AppContext` switch, `System.IO.Compression.UseStrictValidation`,
  that makes it throw instead; it is process-wide and the host's to set, not the library's, and a host that
  sets it gets the same reports. qpdf, the referee, warns "input stream is complete but output may still be
  valid" for a lost tail and for a lost checksum alike, errs on an undefined LZW code, accepts an LZW stream
  without its end-of-data code — and does not check zlib's checksum at all.
- **The fix.** The encoded bytes are read through `FlateInput`, which records a request past their end. A
  zlib stream that ran out has its body read again as raw deflate, keeping nothing, to say which it lost: its
  checksum only — a repair, the data decoded whole — or its tail — a warning, what decoded before the end was
  kept. Corrupt data keeps its own report, reworded: "is corrupt; decoding stopped at the fault, losing up to
  the last 64 KB decoded before it", no longer "was truncated". A stream a guard of the reader cut is
  marked so, and its lost tail is left to the guard's report. What a Flate
  stream decodes to is unchanged. The LZW decoder decodes a code up to the next one to define, stops at any
  other, keeps what came before and names the code; a stream without its end-of-data code is still taken as
  complete. And `Decode()` given no diagnostics now reports what it met to the document's own, as a reached
  limit already did (ADR 34): through the public API the previews ship, a damaged stream is never silent.
- **The corpus.** Ten of the 381 documents readable here without a password report a Flate stream that ran
  out — 50 streams, 40 of them in NIDA's *Heads Up* —, all of them declared damaged, none of them silent before. Against qpdf,
  in a new integration test over every document readable without a password (`FlateRefereeTests`, qpdf at its
  specialized decode level, its offsets counted from the header): every stream qpdf finds cut short, the
  reader reports at the same offset; the six only the reader reports are streams whose length qpdf had to
  recover, so that the two read different bytes — four cut by the end of the file, which qpdf treats as
  empty; the Census abstract's object 66, whose checksum lost its last byte, a carriage return, to an
  end-of-line conversion, where qpdf takes in the line feed after it and ignores the checksum it then misreads;
  and SAMHSA's object 27, whose `/Length` is 26 bytes short, which qpdf notices and the reader does not
  (**T39**). The test excuses a stream only the reader reports when the reader itself said its bytes were not
  the stream's — cut by the file, a wrong length, raw deflate —, and names SAMHSA's with T39, so that it fails
  when T39 is fixed. The truncated invoice now requires `stream.truncated` and `filter.failed`, the cut falling in
  its last Flate stream's data.
- **Tests.** `FilterDamageTests`: four payloads cut at every byte — at 2,000 places and every byte of both ends
  for the longer ones —, each cut reported as
  a lost tail or a lost checksum, the bytes kept always the start of the data; whole streams followed by
  whatever a `/Length` takes in, without a word; a lost checksum told from lost data at three compression
  levels — the last byte of the body may hold only the end-of-block code, so data may be unfinished without
  a byte of output lost —; raw deflate and white space before the header; a bound reached before the cut,
  which says nothing of the cut; the second reading's allocation; `IsWholeDeflate` and `FlateInput`
  directly; LZW codes on either side of the next to define, a first code with nothing before it, codes past
  nine bits, a missing end-of-data code; and a document's stream reported where its data starts, to the
  caller or to the document. Two tests that let the silence pass now assert the report.
- **Measured.** `FilterBenchmarks`, ShortRun: a whole stream decodes as before — 81.6 µs and 86.41 KB for
  64 KB, 8.3 ms and 13,413.59 KB for 4 MB, against 81.9 µs and 8.3 ms with the same allocations before the
  change. A stream that ran out costs 1.4 to 1.6 times that, and 0.4 KB more.
- **Found on the way.** **T39**: a stream whose data runs past the parser's window has its `/Length` taken as
  it is, so a wrong one goes unnoticed — sixteen in the SAMHSA fact sheet by qpdf's count, eight by the
  reader's. **T40**: a Flate stream that turns corrupt throws from the read that meets the fault, and what that
  read decoded — up to 64 KB, or all of a small stream, which is then left encoded — is lost with it; for a
  checksum that disagrees with whole data, qpdf, which does not check the checksum, keeps it all.
- **On review.** Five reviewers — correctness against the pre-change library on 32,000 differential cases, the
  tests' teeth by mutation on the Flate side and on the LZW side and the route, the rules and every claim,
  the integration test and the corpus — and two skeptics on each finding above a nit. Upheld and fixed: a zlib
  header asking for a preset dictionary raised an `IOException`, not an `InvalidDataException`, which escaped
  the filter and made `PdfDocument.Open` throw on an object stream or a cross-reference stream that began so —
  older than T32, in the code it rewrote, now a filter failure as qpdf has it; a stream cut by
  `MaxObjectLength` was reported as a lost tail; the corrupt report claimed more than was kept; the
  integration test excused any stream whose length qpdf recovered, hid T39, compared nothing where qpdf gives
  up, and counted offsets from the file's start where qpdf counts from the header; and seven gaps in the
  tests — LZW after a clear, a longer sequence extended by its first byte, `/EarlyChange 0`, the guard on raw
  deflate, a stream decoded with nowhere to report, the second reading's allocation measured on data that
  does not compress, and the position of each report. Each new test fails on its mutant. Refuted: duplicate
  reports from a stream decoded twice — as a reached limit already is, each decode reports what it met —, and
  the code widths past ten bits. Left as they are: the branch for hosts that set `UseStrictValidation`, which
  cannot be tested in-process since the framework reads the switch once, and the cuts enumerated over four
  payloads rather than drawn by FsCheck.

### 2026-09-26 — The corpus manifest has a JSON schema
- **Why.** Asked whether a schema was worth having, the answer was yes, in a pull request of its own: the
  manifest is written by hand for every contributed or remote document, and M02's slices now edit its
  `findings` across dozens of entries; the model refused unknown keys inside `expect` and `readerLimits` only,
  so a misspelled `feature` on an entry dropped the document out of every selection by feature in silence.
- **What.** `tests/corpus/manifest.schema.json`, draft 2020-12, named by the manifest's `"$schema"` so that an
  editor applies it as it is typed: no unknown key at any level; the eight use-case categories and four
  origins; the reader's seventeen diagnostic codes and the validation rules as enumerations; a remote
  document with its pinned source and size, under `remote/`, and nothing else there; pins as lower-case hex;
  archive members relative with no `..`; a raised reader limit above its default; `catalogRecoverable` only
  as false; `conformanceValid` only beside a claim, `password` only beside `encrypted`; an `unsupported`
  reason naming its milestone or debt row. `build_corpus.py` keeps the key when it rewrites the manifest,
  byte for byte.
- **What it found at once.** A feature listed twice on one entry (removed), and three committed files taken
  out of the PDF/UA reference zip whose `source.url` names the member after the URL — allowed, in exactly
  that form, for committed files only: `fetch_remote.py` downloads a remote URL as it stands.
- **Tests.** `CorpusManifestSchemaTests`: the manifest and any `private.json` follow the schema; twelve sound
  entries accepted; sixty refused, each one mistake away from a sound one and each required to fail on the
  keyword that names its mistake, three whole manifests refused, and every required key left out once; and
  the schema held to what reads the manifest — the model's properties, every `PdfReaderLimits` guard and its
  default, `PdfDiagnosticCodes`, `PdfValidationRuleIds` —, so that neither can change without the other.
  Eleven mutations of the schema, one per rule the review found unpinned, each fail a test.
- **On review.** Two reviewers — the schema's correctness and portability, the tests' teeth — and two
  skeptics on each of their eleven findings. Taken: a referee's verdict no script writes for a private entry
  is no longer required of one; a remote path follows `fetch_remote.py`'s `remote/<source>/<name>.pdf`, and no
  path may pass through `..`; the patterns end with `$(?!\n)` and use explicit ASCII classes, since .NET and
  Python let `$` match before a final line feed and the three engines disagree on `\S`; a raised limit may not
  sit beside a skip, nor a page count beside an unrecoverable catalog; `docs/corpus.md`'s example gained
  the verdict the schema asks for; and a refused case that failed for a reason other than its own —
  renaming `features` also made a required key go missing — now adds its unknown key beside it. Validation is `JsonSchema.Net` 9.4.0, MIT,
  a dependency of the unit tests only; the framework exports schemas but does not validate against one.
  Python's reference validator agreed with it on the manifest.
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

### 2026-09-26 — ADR 36 and M02's first slice: validation in the core, and `file.eof-missing`
- **The question, then the decision.** Asked what came next, the answer was M02's first slice — but not before
  settling where its public types live, since every merge publishes them: the roadmap put the engine in an
  `AdCodicem.Pdf.Validation` satellite and `PdfRepair` in the core, driven by findings, which invariant 1 rules
  out. The maintainer accepted ADR 36 — the engine and the structural profile in the core, the PDF/A and
  PDF/UA profiles in `AdCodicem.Pdf.Conformance`, an identifier never published — and answered what it left
  open: identifiers `family.name`, never a reader code; an instance with immutable options as the entry
  point; the `PdfLimitExceededException` a caller asked for passes through; the engine internal until M20;
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
  The other iPRES end-of-file cases stay silent, each for a reason M02 records; `t04-007` only because the file
  is shorter than the window. `CorpusExpectation` refuses unknown keys; `build_corpus.py` writes the finding
  for the tail it cuts.
- **Tests.** The engine's order, capacity, counts, determinism and refusals; the rule on every shape of end of
  file, the 1,024-byte edge one byte at a time, and the bytes it reads; a property over generated trailing
  bytes; the identifiers' grammar; `CorpusValidationTests` over every document — reported on without a throw,
  unsupported ones included, exactly the declared findings, no error on a well-formed file nor on a PDF/A
  failure, the same report twice.
- **On review.** Four reviewers — correctness, the repository's rules, the tests by mutation, the
  documentation — and two skeptics on each of their 22 findings. Upheld and fixed: ten behaviors no test
  held (six mutations checked killed afterwards), M02's account of three iPRES end-of-file cases and of the
  acceptance row the strict one replaced, a stale comment, and this file. Refuted: a roadmap state, two
  tests said to be vacuous, the skipping of unsupported entries. One was real and older than the change:
  a `PdfFileSource` whose `Read` returns short counts misleads the whole reader, and now the rule — **T36**.
- **Housekeeping.** This file's "At a glance", a week stale, re-checked against GitHub, nuget.org, Scorecard
  and Codecov, and the journal summarized; T20 closed, T35 opened; the package's description, which promised
  a writer, now says what it holds.
- **Measured.** `ValidationBenchmarks`, ShortRun: 94 ns and 232 B for 10 pages, 86 ns and 232 B for 1,000.
  The reader's benchmarks are unchanged: 231 µs and 392.92 KB to index 1,000 pages.

### 2026-09-26 — Every line of #28's change is covered
- **The report.** Codecov found ten lines of the pull request's change that no test reached (97.3 % of the
  patch). Measured the same way here — the unit suite with CI's coverage command, without the remote
  corpus, intersected with the lines the change adds — it gave the same ten.
- **One was unreachable, and went**: a cut trailer's re-read tested an offset that is inside the file by
  construction, for any source whose length holds.
- **The rest were behavior without a test**, and have one: an LZW stream stopping at a code it has not
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
- **Details that decide behavior.** What the parser met where a guard cut an object is the reader's, not
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
  `expect` — a setting, not an observation —, modeled with unknown keys refused. The USGS topographic map
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
  reaches a guard while expanding object streams, and while looking for the catalog; an object read no
  further than its bound; the table's keywords and its trailer at the bound; the presets, the refusals and
  the clamp, and an FsCheck property over every `int`. Thirty-five mutations — each report, bound, preset,
  refusal, deferral and fallback — each fail a test; two needed a test of their own (a bound that falls
  where the parser would report a cut, a length stated in kilobytes), and five were rewritten to compile.
  The catalog search caught a defect
  of its own before any commit: `reached ??= FindCatalog()` skipped the search whenever the expansion had
  already reached a guard.
- **The remote corpus.** Run 6 of `Remote corpus`, dispatched on the branch on 2026-09-26, fetched all 242
  documents and passed 1,266 unit tests (61 skipped by design) and 668 integration tests: the map reads
  whole on the runner too, and the negative control holds there. It is the first run to include T21, T23,
  T29 and T31's fixes as well.
- **Measured.** The USGS map under 512 MB: opening reads 83 KB of 63.1 MB; decoding every stream takes
  1.8 s and allocates 1,785 MiB, its image's 328,608,000 bytes passing through an output that doubles to the
  bound and is copied out — memory for M23's budgets (T28, T33). The reader's benchmarks allocate what they
  did (392.86 KB to index 1,000 pages, 5,964.54 KB to read them); their times, on a short run, stay within
  its spread.
- **Documentation.** ADR 34 and 35, and the ADR index; `CLAUDE.md` (invariant 12, invariants 4 and 5
  qualified, a convention on unsafe code); `ARCHITECTURE.md`, `docs/architecture.md`, `SECURITY.md` (what
  "without bound" means once limits can be raised); the site's new *Reader limits* page, *Diagnostics*,
  *Lazy reading* and the introduction; `docs/corpus.md` (whose example still showed fields the model never
  had), `docs/corpus-contributions.md`, `docs/corpus-sources.md`, `tests/corpus/README.md`; the M02 stream
  rule and M23's deliverables in `docs/roadmap.md`; T28, T30, T31 and T33 below.

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
  for T28, whose remaining half — decoding such a stream a piece at a time — is M23's.
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
| T14 | [#40] |
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
