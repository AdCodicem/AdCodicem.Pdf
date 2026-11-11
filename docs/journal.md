# Journal

What each session and pull request did and decided, newest first. It moved here from [`status.md`](status.md) on
2026-11-11 ([ADR 54](adr/0054-review-and-documentation-effort-follow-what-protects-the-library.md)), so that a
session's first reading is the project's state and not its history, and its entries before that day were summarized
to about thirty lines each; the unabridged text is the
[journal of `e38ebbc`](https://github.com/AdCodicem/AdCodicem.Pdf/blob/e38ebbc471b86779c3f6ac5b320ca1ecd531b2c1/docs/status.md#journal). A session reads an entry when it needs the
reason for a past decision. A new entry is short, about thirty lines; the detail is in the pull request's body.

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
[#53]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/53
[#55]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/55
[#56]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/56
[#58]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/58
[#60]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/60
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
