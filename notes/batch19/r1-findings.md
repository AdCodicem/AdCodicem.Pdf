######## [0] chain:reread-through-leading-white-space — reviewer major; refuter confirmed major maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3349 (ReachedBefore), :3171-3177 (hand-on continues without RecordSectionRead)
CLAIM: The chain does not recognize an offset that falls in the white space before a section it already read or handed on. ReachedBefore tests PositionOf(offset), but _sectionsReadAt holds Offset + Padding. A handed-on stream's start is not recorded at all. The section is read a second time, so every per-section report this batch added is doubled, along with its validator finding. A lost stream is also handed on twice, which is a regression of #208. The threat model's row 414 says 'the section read once', which is false for these files.
EVIDENCE: Probes are in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/chainlens/probes (ZzChainProbes*.cs). Outputs are in chainlens/*.txt (branch) and chainlens-main/out/*.txt (main 21a0298).
(p1) T1 -> lost stream S at 1550, /W [1 -4 2], whose /Prev 1549 names the end-of-line before its own header.
Branch:
  xref.section-unreadable @1550 '...The chain goes on through its /Prev...'
  xref.section-unreadable @1549 (same text)
  xref.chain-cycle @1549 'the /Prev of the section at offset 1549 names offset 1549, which the chain has already named.'
  Validator: xref.section-malformed [Error] for 1550 and again for 1549.
  Same result with 22 bytes before the header (1572/1571).
Main: one section-unreadable and one section-malformed.
(p1b) T0's /XRefStm names S-1 after T1 -> S (hand-on) -> T0.
  Branch: section-unreadable @1570 '/Prev names' and @1569 '/XRefStm names', and two section-malformed Errors.
  With the exact offset (p1c): one report.
(p5) Empty table (#222) at 110 whose /Prev 109 names its own end-of-line.
  Branch: xref.section-empty @110 and @109, and xref.subsection-missing twice.
  Main read the table twice but reported nothing per section.
(q2) Stream with a type-7 row whose /Prev names its own end-of-line.
  Branch: xref.entry-reserved @1310 and @1309, and xref.entry-type-reserved twice.
(q3-p1) Default limits; Tg's /Prev names the end-of-line before the relocated Tmid (same on main).
  Tmid is read twice.
  Spurious 'file.size-wrong: The trailer of the cross-reference table at offset 3435 gives /Size 40, ... makes it 5'.
  xref.chain-loop is blamed on the sound link '/Prev of the section at offset 3435 names offset 1344'.
FIX (reviewer): Judge 'already reached' by the section's first token, not by the written offset. Either skip white space and comments at PositionOf(offset), as TryReadXRefSection's probe does for Padding, before testing _sectionsReadAt. Or have TryReadXRefSection return a 'reached' state as soon as its first token's start is in _sectionsReadAt, before it indexes any row. Record a handed-on stream's Offset + Padding as reached. A /Prev that reaches it is then a loop, and an /XRefStm that reaches it is NamedAgain. Add the p1, p1b, p5 and q2 shapes as tests asserting one report each, and amend threat-model row 414.
--- VERDICT REASON: The finding holds. Its stronger part is new in this batch.

1. Code. `ReachedBefore` (PdfFileReader.cs:3349) tests `named.Contains(offset) || _sectionsReadAt.Contains(PositionOf(offset))`, but `RecordSectionRead` (:3358-3361) stores `Offset + Padding`. The hand-on branch (:3171-3177) does `continue` before `RecordSectionRead` (:3190), so a lost stream's start is never recorded.

2. New in this batch: the hand-on gap is wider than the finding says. A lost stream first named short of its header (T2 /Prev = S-1), whose own /Prev names its exact header S, is read and handed on twice. Q9 decided that the header offset counts toward "already read", and the same shape with a readable stream is caught at once on the branch. #208's closing criteria are not met for these shapes: "reported once as xref.section-unreadable", and "followed under the same checks as any other". The threat-model #208 row says "read as a whole read reads it", which is false for this shape. Calling it a "regression of #208" is loose: main never handed on, so it is #208's criteria that fail.

3. Pre-existing, made visible by this batch. A readable section re-read through a white-space offset other than its first naming happens on main too. It already doubled file.size-wrong and blamed the loop on the wrong link (q3-p1 is identical on main). The per-section reports this batch adds now double with it: section-empty/subsection-missing, entry-reserved/entry-type-reserved, and section-unreadable/section-malformed for lost streams.

4. Row 414 is literally true, since it speaks of "an offset naming one of them", meaning a recorded position. But "the section read once" misleads for an offset in the white space before it.

5. Related cost, also pre-existing. The #156 claim "a stream a thousand tables name is read once" holds only when the tables name the same offset. Each distinct white-space offset reads the stream again: the cost is the number of tables times the stream's size, bounded by MaxXRefSectionCount and MaxTrailerLength, so there is no hang.

Objects are served correctly in every shape. The damage is doubled reports, Errors among them, and a loop blamed on a phantom second record of the same section. These are wrong reports, so the severity is major.
VERDICT EVIDENCE: The probe is my own and independent of the reviewer's kit: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute7/ZzRefuteWs.cs. Outputs are in refute7/branch/*.txt and refute7/main/*.txt under the same scratchpad.

File: T0 (classic), comment, obj 4, stream S (obj 5, /Type /XRef, /Index [4 2], /W [1 -4 2] when lost), comment, T2 /Prev S.

(A) Lost S, its /Prev = S-1 (1506).
Branch:
  xref.section-unreadable @1507, then again @1506 (both '...The chain goes on through its /Prev...')
  xref.chain-cycle @1506 'the /Prev of the section at offset 1506 names offset 1506, which the chain has already named.'
  Validator: xref.section-malformed [Error] for 1507 and for 1506; xref.chain-loop for 1506.
Main: one section-unreadable @1507 and one section-malformed.

(A2) New shape: T2 /Prev = S-1, lost S's /Prev = S, its exact header.
Branch:
  section-unreadable @1506 and @1507
  chain-cycle '...section at offset 1507 names offset 1507, which the chain has already named.'
  two section-malformed [Error]
Main: one of each.

(C2) The A2 shape with a readable stream.
Branch: one read; chain-cycle '/Prev of the section at offset 1506 names offset 1507, which the chain has already read.' The batch's position set works for read sections but not for handed-on ones.

(C) Readable S, its /Prev = S-1.
Branch and main identical: S read twice; file.size-wrong for 1507 and again for 1506. Pre-existing.

(D) Readable S with a type-7 row, its /Prev = S-1.
Branch: xref.entry-reserved @1507 and @1506; xref.entry-type-reserved twice.
Main: no reserved report; file.size-wrong twice.

(E) Control: lost S /Prev = T0-1, sound. One report.

Cost: 500 tables, each /XRefStm naming a 20,000-row stream.
Branch: same offset gives 1 stream read and 16,903,594 bytes read; distinct white-space offsets (stream-k) give 499 reads and 119,930,154 bytes.
Main: 1 read; 500 reads and 120,135,786 bytes.

The reviewer's q3-p1 output is identical on main and on the branch.
FIX (refuter): 1. In the hand-on branch (PdfFileReader.cs:3171), call `RecordSectionRead(section, sectionsAt)`, or at least `_sectionsReadAt.Add(section.Offset + section.Padding)`, before `continue`. A /Prev that names the stream's header then ends the chain in a loop, and an /XRefStm that names it is NamedAgain. This alone fixes A2.

2. Judge "reached" by the section's first token, not by the written offset. Two ways, the second cheaper:
   - In `ReachedBefore`, when the plain checks fail and `PositionOf(offset)` is in the file, lex past white space and comments within `XRefProbeLength`, growing to the trailer bound as `TryReadXRefSection`'s probe does, and test that start against `_sectionsReadAt`.
   - Have `TryReadXRefSection` return a "reached" outcome when `absolute + Padding` is in `_sectionsReadAt`, before it indexes any row or reports anything. The chain treats it as a loop on /Prev, placed at the section's start with "already read", and as NamedAgain on /XRefStm. The record must then not be counted as a second read section.

3. Add tests asserting one report each: A, A2, the reviewer's p1b and p5 shapes, D, and C (one file.size-wrong). Also add a HostileInputTests variant where the tables name one stream through distinct white-space offsets and it is read once.

4. Amend threat-model row 414 and the #208 and #156 rows, so that an offset in the white space before a reached section counts as reaching it.

This implements Q9's "one notion of already read" consistently, so it is not a new design choice.

######## [1] chain:xrefstm-reaches-prev-section-by-another-offset — reviewer major; refuter confirmed major maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3235 (branch (c) compares written offsets) against :3087/:3349 (loop check by position)
CLAIM: Branch (c) of B19-5 compares the written offsets (hybrid == previous), but the loop check compares positions. An /XRefStm can reach the section its table's /Prev names through another offset: the white space before it, or a relocation. The /XRefStm then reads that table first, in the stream's place, with its links unread. The sound /Prev is then reported as xref.chain-cycle and the Error xref.chain-loop, and every older section is lost and left to a rebuild. Main read these files whole. This is the exact symptom the test comment of An_xrefstm_naming_what_its_table_s_prev_names_is_left_to_the_prev... describes as fixed, and threat-model row 417 claims as mitigated.
EVIDENCE: (q1) Told (alone indexes object 3) <- T0 at 1469 <- T1 hybrid with /Prev 1469 and /XRefStm 1468.
Branch:
  xref.hybrid-stream-lost @1468 '... a cross-reference table, whose rows were read in the stream's place'
  xref.chain-cycle @1469 'the /Prev of the section at offset 2789 names offset 1469, which the chain has already read.'
  Told is never read; object 3 is found only after xref.rebuilt.
  Validator: xref.chain-loop [Error].
  Same with a 5-byte prefix (1473/1474).
Main: all 4 sections read, object 3 read without a rebuild, only xref.offset-imprecise.
(q4) /XRefStm 1462, 7 bytes before T0, relocated onto it.
  Branch: xref.offset-adjusted, hybrid-stream-lost, chain-cycle, and chain-loop [Error]; Told lost.
  Main: no loop, Told read.
With equal written offsets (q1 with offset 0), branch (c) works: only hybrid-stream-lost, and Told is read.
The existing row An_xrefstm_naming_offset_zero_is_judged_as_one_naming_any_other_offset("", "/Prev {xref:1} /XRefStm 0", ...) produces, on its own fixture:
  xref.offset-adjusted
  xref.hybrid-stream-lost
  'xref.chain-cycle @203 ... the /Prev of the section at offset 373 names offset 203, which the chain has already read.'
  validator xref.chain-loop [Error]
The test asserts only the hybrid-stream-lost message, so it passes over the false loop.
FIX (reviewer): Decide branch (c) by the section both links reach, not by their written values. When the /XRefStm would read or relocate onto the start that the table's /Prev reaches, do not read it there: record the naming as 'the section its /Prev names too' and let the /Prev read it whole. Alternatively, let a /Prev that reaches a table read only in a stream's place, its links unread, read those links instead of reporting a loop. Add the q1 and q4 shapes as tests. Assert 'no chain-cycle' in the offset-zero row. Amend threat-model row 417 and the lazy-reading page.
--- VERDICT REASON: The finding holds as stated. Branch (c), at src/AdCodicem.Pdf/IO/PdfFileReader.cs:3235, tests `hybrid == previous` on the offsets as the file writes them. Since Q9, the loop check (ReachedBefore at :3087 and :3349) tests positions through _sectionsReadAt. Sometimes the /XRefStm reaches the section its table's /Prev names but through a different written offset. It can name the white space before the xref keyword, with or without a header prefix, or be relocated onto that section, as with /XRefStm 0 or 7 bytes short. In those cases branch (c) is not taken. The else-branch reads the section in the stream's place, takes its rows, leaves its links unread and records its start in _sectionsReadAt. The sound /Prev is then reported as xref.chain-cycle, and the validator raises an xref.chain-loop Error. The index is left incomplete, every older section is lost, and the document is rebuilt at opening when the lost section held the catalog. Main 21a0298 read all four sections of the same files without a rebuild. So this is a regression the batch introduced: the position-based loop check came in without a matching change to branch (c). B19-5 chose branch (c) so that a right /Prev is not blamed for a loop and the older chain is not lost to a rebuild. The rejected alternative in measure/xrefstm/summary.md:128 is described as "keeps a false loop and loses T00's chain to a rebuild". Q9 settles one notion of 'already read', by position, for #156's NamedAgain/firstAt, so the decisions already cover the shifted shapes. Threat-model row 417 claims the threat "the right /Prev blamed for a loop" is mitigated, and cites An_xrefstm_naming_offset_zero_is_judged_as_one_naming_any_other_offset, whose third row shows the threat: that test checks only that the xref.hybrid-stream-lost diagnostic is single. No open debt issue covers this; #321 and the stage 5 and 6 residuals are other shapes. Severity is major: a wrong report (a false loop, Error in the validator), sections lost to a rebuild, a regression against main, and B19-5 not implemented as decided for these shapes. It is not a blocker: there is no crash, the file is already invalid because its /XRefStm names a table, and the objects are still found, by a rebuild.
VERDICT EVIDENCE: Probe, branch head 071f829. The fixture is HybridFiles.NamingThePreviousTable copied inline: the oldest table Told at 203 alone indexes objects 1 to 3, T0 at 368 has /Prev 203, and T1 at 507 has /Prev 368 and /XRefStm X.
- q1 (X=367, the newline before T0's xref): sections startxref Read 507; /XRefStm Read Table offset=367. Diagnostics: 'xref.hybrid-stream-lost @367 ... names offset 367, a cross-reference table, whose rows were read in the stream's place'; 'xref.chain-cycle @368: ... the /Prev of the section at offset 507 names offset 368, which the chain has already read.'; xref.rebuilt @-1. WasRepaired=True before any load. Findings: Error xref.chain-loop, Error xref.hybrid-stream-wrong, Warning xref.offset-imprecise.
- q1 with a 5-byte prefix: the same, at positions 372 and 373, plus xref.offset-adjusted @5.
- q4 (X=361, relocated 7 bytes onto T0): xref.offset-adjusted @368, hybrid-stream-lost @361, chain-cycle @368, xref.rebuilt. Findings: Error xref.chain-loop and Error xref.hybrid-stream-wrong.
- Equal offsets (X=368): sections Read 507, NamedAgain 368, Read 368, Read 203. Only hybrid-stream-lost, WasRepaired=False. Branch (c) works here.
- The offset-zero test's third row (SoundUpdated with '/Prev {xref:1} /XRefStm 0'): xref.offset-adjusted @203, hybrid-stream-lost @0, 'xref.chain-cycle @203: ... the /Prev of the section at offset 373 names offset 203, which the chain has already read.' Findings: Error xref.chain-loop, Error xref.hybrid-stream-wrong. The test passes.
Main 21a0298, same probe:
- q1: sections /XRefStm Read 367, /Prev Read 368, /Prev Read 203. No chain-cycle, WasRepaired=False, only Warning xref.offset-imprecise.
- q1 with the prefix: the same, all four read.
- q4: all four read, only xref.offset-adjusted and xref.section-shifted.
- Offset-zero row: /XRefStm Relocated 203, /Prev Read 203. No loop.
- Equal offsets: main had the false loop (chain-cycle, rebuilt, chain-loop Error), which the branch fixes.
FIX (refuter): Decide branch (c) by the section both links reach, not by the values the file writes, as Q9's single, position-based notion of 'already read' requires. The simplest form works after the read. When the /XRefStm's record is read or relocated (else-branch, :3263-3291), check whether its start (stream.Offset + stream.Padding) is where the table's /Prev reaches, that is PositionOf(previous) past its white space or the section a relocation of it would find. If it is, keep no record of the read at that start: remove it from _sectionsReadAt and keep it out of sectionsAt. Turn the record into the branch (c) naming, `namedWithPrevious` with ReadAs set when the /Prev's record is made, reported as 'the section its /Prev names too, which the chain reads through that /Prev'. The /Prev then reads the section whole, with its links. The rows the /XRefStm already took are the same section's rows at the same place in the chain, so taking them twice is harmless (the index keeps the first). The other form is equivalent: a /Prev that reaches a section read only in a stream's place, its links unread, reads those links instead of reporting a loop. Either form only implements B19-5 and Q9 as decided, so no maintainer question is needed. Tests to add: q1 (white space before the keyword, with and without a header prefix) and q4 (relocated onto the /Prev's section), each asserting no xref.chain-cycle, no xref.chain-loop, the oldest section read, and WasRepaired false. In the third row of An_xrefstm_naming_offset_zero_is_judged_as_one_naming_any_other_offset, assert the whole diagnostic list, with no chain-cycle; its expected text then becomes 'the section its /Prev names too'. Amend threat-model row 417 and the lazy-reading page so that 'names what its own table's /Prev names' says 'reaches the same section, whatever offset it writes'.

######## [2] chain:hybrid-in-part-refuses-its-own-stream — reviewer major; refuter refuted none maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3191 (ReportSectionReadInPart -> RefuseRowsLost, :3870) before the hybrid branch at :3276
CLAIM: A hybrid table read in part refuses its lost numbers before its own /XRefStm stream is read. That stream belongs to the same revision, and ISO 32000-1 7.5.8.4 reads its rows before the /Prev's. Its rows for those numbers are refused, the object is left to a rebuild, and the rebuild keeps an older direct definition: an older revision is served. This is a regression from main. Stage 7 flagged it as residual [7-partial] and left it unfiled.
EVIDENCE: (q9) Told indexes object 5 written directly as '(five old)'. T is a hybrid table named by startxref. Its subsection '5 1' holds the row '0000000000 00000 x'. Its /XRefStm names stream 8, which indexes object 5 in object stream 7 as '(five new)'.
Branch:
  xref.section-unreadable @810 'The cross-reference table startxref names at offset 810 was read only in part: the row for object 5, at offset 843, is not an offset, a generation and n or f; ...'
  GetObject(5) = '(five old)' after xref.rebuilt and 'object.redefined ... object 5'.
Main 21a0298: GetObject(5) = '(five new)'.
FIX (reviewer): Refuse a hybrid table's lost rows (RefuseRowsLost) only once its /XRefStm stream has been read, or exempt that stream from its own table's refusals. Report the section in part as now. Add the q9 shape as a test: object 5 reads '(five new)'.
--- VERDICT REASON: I rebuilt q9 and three variants of it. The branch's output matches the evidence, but neither the "regression" nor the "older revision served" holds up.

(1) In a hybrid file the table's rows come before its /XRefStm stream's (ISO 32000-1 7.5.8.4: table, then XRefStm, then /Prev). This reader applies that order on both main and the branch, and a free row counts: when T writes object 5's row as 'f', GetObject(5) is null and the stream's row never stands. So any row T could have written for 5 ('f' or 'n') would have hidden the stream's row. Refusing that row once T lost its own is the reader's own precedence, which is what #210 decided ("no section read after it stands for a number whose row it lost"). It is not a bad ordering of refusals. The docs say the same (diagnostics.md:112, validation-rules.md:54).

(2) Main already applies this rule to a refused row in the same place. If T's row for 5 has a generation out of range, main also refuses object 5 to T's own /XRefStm stream and serves '(five old)' by rebuilding. The branch only brings a lost row into line with that.

(3) Main's '(five new)' on q9 is #210's silent loss, which this batch fixes, and it was right only by chance. If T's lost row points at a newer direct definition ('{direct} 00000 x'), main serves the object stream's copy, which is the wrong object, with no diagnostic. The branch serves the right one. The proposed fix (exempt the stream from its table's refusals) would bring that wrong object back.

(4) The older revision comes from the rebuild's rule, open debt #189 (milestone M02): "A rebuilt index serves an older direct definition over a newer copy in a later object stream". The object.redefined message says so ("the definition kept is the last written directly in the file"). Once #189 is fixed, q9 serves '(five new)' through the rebuild.

Stage 7's residual is therefore acceptable as it stands, and the "older revision" half is already filed as #189.

A side note, not this finding's claim: the read-in-part message ends "rather than taken from an older section's rows". The table's own /XRefStm stream is a section read after it, not an older one. That is wording only, a nit at most.
VERDICT EVIDENCE: Probe ZzHybridPart, saved at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/ZzHybridPartProbe.cs.txt. Told indexes 1, 2 and 5 '(five old)' directly. Object stream 7 holds 5 '(five new)'. XRef stream 8 has /Index [5 1 7 2]. T is the hybrid table startxref names: 0 1, then 5 1 <row>, with /Prev Told and /XRefStm 8. Output on branch 071f829 and on main 21a0298 (worktree built from 21a0298, since removed):

q9 row '0000000000 00000 x':
  branch: GetObject(5)='five old', after xref.section-unreadable @495 'read only in part: the row for object 5, at offset 528, is not an offset...', then xref.rebuilt, then object.redefined ('...the definition kept is the last written directly in the file...').
  main: chain index has 5 as Compressed in 7, GetObject(5)='five new', 0 diagnostics.

T row '0000000000 65535 f ' (free) and '0000000000 00000 f ':
  branch and main: chain index has 5 as Free, GetObject(5)=null, no rebuild. T's free row hides the stream's row.

T row '0000000000 99999 n ' (refused row, generation out of range):
  main: GetObject(5)='five old', repaired, xref.rebuilt and object.redefined. The same refusal of 5 to the table's own stream already happens on main.
  branch: the same object, plus xref.section-unreadable 'read only in part ... that row alone was refused'.

Object 5 also written directly after stream 7 as '(five direct newest)', T row '{direct} 00000 x':
  main: GetObject(5)='five new', 0 diagnostics. That is the wrong object, since T's row placed the direct one.
  branch: GetObject(5)='five direct newest', after xref.section-unreadable, xref.rebuilt and object.redefined.

T row '{direct} 00000 n ': both give 'five direct newest'. No 5 row in T: both give 'five new'.

Code: ReadSubsection, PdfFileReader.cs:4772-4777, sends both 'f' and 'n' rows through AddFromChain, and PdfXRefTable.TryAdd keeps the first entry it sees for a number. Issue #189, open, labeled debt and review, milestone M02: "A rebuilt index serves an older direct definition over a newer copy in a later object stream".
FIX (refuter): No change to RefuseRowsLost's ordering. Exempting the stream would serve the stream's object where T's lost row placed a newer direct definition (the {direct} variant above). The older revision in q9 is paid by #189, the rebuild ranking an object stream's members at the stream's place. Optionally, a comment on #189 that adds the q9 shape (a hybrid table's lost row) to its tests: object 5 reads '(five new)' once the rebuild ranks by position. Optional nit: the read-in-part message's "rather than taken from an older section's rows" could say "from the rows of a section read after it", which covers the table's own /XRefStm stream.

######## [3] chain:relocation-passes-over-declared-unreadable-candidate — reviewer major; refuter narrowed major maintainer=True
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:4071-4073 (candidate acceptance in TryRelocateXRefSection)
CLAIM: A relocation drops a candidate that declares itself a section (an xref keyword, or /Type /XRef) but cannot be read, and takes a farther, older section instead. The lost section is never reported (invariant 5), its declared numbers are not refused, and the index is not marked incomplete, so an older revision is served in silence. With nothing else near, the report says 'not there, nor near it' although a cross-reference stream lies 7 bytes away. This is the same on main and is not #320: #320 is about rows a dropped candidate leaves, and this candidate leaves none. It contradicts #281's rationale ('any section near it is ... an older one whose rows would stand for the lost ones').
EVIDENCE: (p2-near) T0 at 171 indexes 1, 2, 3 and 7 '(old seven)'. Before S: object 4 '(four only in S)' and object 7 '(new seven in S)'. S is at 382: /Type /XRef /W [1 -4 2] /Index [4 2 7 1] /Prev T0. T1's /Prev is 375, 7 bytes before S.
Branch:
  Only 'xref.offset-adjusted [Repair] @171: The cross-reference section /Prev names at offset 375 was found 204 bytes before it.'
  indexIncomplete=False, WasRepaired=False.
  GetObject(4) = null, GetObject(7) = '(old seven)'.
  Findings: only xref.section-shifted.
Main: identical (message 'found -204 bytes from there').
(p2-far) Same file with 1,200 bytes between T0 and S.
  'xref.section-missing @1575 ... is not there, nor near it'
  'xref.section-not-found [Error] ... nor within 512 bytes of it'
FIX (reviewer): End the search at a Malformed candidate that DeclaresItselfASection. Either take it as the section found moved, as a guard-cut declared candidate already is: report offset-adjusted and section-unreadable at it, refuse its declared numbers, and hand on its /Prev when it gives /Type /XRef. Or at least report it and mark the index incomplete. Taking it changes #320's expected outcome, so if the maintainer does not settle it in this batch, file it as debt beside #320 with the p2 shapes.
--- VERDICT REASON: The finding holds on the branch. The acceptance test at PdfFileReader.cs:4071-4073 takes a candidate only when it is Read, or when it is Malformed and was cut by a guard while it declared itself a section (#263). A Malformed candidate that declares itself a section (an xref keyword, or /Type /XRef) and was not cut by a guard is dropped through ForgetReadThrough at :4092. The search then goes on to the next candidate. When that one is an older section, the newer, unreadable section is never reported. Its declared numbers are not refused and the index is not marked incomplete, so the older revision's rows are served with no report. This breaks invariant 5. The DeclaresItselfASection doc at :3410-3420 rules out the same outcome ("an older one whose rows would stand for the lost ones (#281)"), and B19-1 now prevents it when such a section sits at the named offset. It is not #320: here the dropped candidate leaves no rows. It is not in residuals-impl.md or in any open debt. Relocation measurement S8 met only a dropped candidate that the chain reached again later through a /Prev, so it was reported then.

I narrow one part of the claim. "Identical on main" holds only when no section the chain has already read lies within 512 bytes of the named offset. When the newer table that names the section is within that radius, main relocated onto it: it reported a false loop, rebuilt the index and served the newest objects. The branch's #188 skip of already-read positions passes that table over, and the older revision is then served with no report. So this batch makes the fault reachable in more files; it did not only leave it as it was.

The "far" half also holds: the reader says "not there, nor near it" and the validator says "nor within 512 bytes of it", while an unreadable /Type /XRef stream lies 7 bytes away. Taken alone, that is a misleading text (minor), because the index is marked incomplete there and the rebuild serves the right objects.

No decision covers a declared candidate found near the named offset. B19-1 speaks of the section at the named offset only. So the rule is a design choice for the maintainer, and it changes #320's expected outcome.
VERDICT EVIDENCE: Probe (uncommitted, removed; kept at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute-f42-10/ZzRefuteDeclaredCandidate.cs.txt):
- T0 at 160 indexes 1, 2, 3 and 7 '(old seven)'.
- Then object 4 '(four only in S)' and object 7 '(new seven in S)'.
- S at 447: 9 0 obj << /Type /XRef /Size 10 /W [1 -4 2] /Index [4 2 7 1] /Prev 160 >> (or /W [1 4 /]).
- T1 indexes object 6, with /Prev 440, the 'endobj\n' 7 bytes before S.

Branch 071f829, near-endobj (T1 197 bytes past 440):
  D xref.offset-adjusted [Repair] @160: The cross-reference section /Prev names at offset 440 was found 280 bytes before it.
  indexIncomplete=False WasRepaired=False
  GetObject(4) = null; GetObject(7) = "old seven"; still WasRepaired=False
  F file.size-wrong @690 (false: /Size 10 judged to be 8); F xref.section-shifted @160
  Sections: 637 Read, 160 Relocated (S appears nowhere)
The /W [1 4 /] variant gives the same. The near-endobj-T1far variant (1,200 bytes before T1) gives the same.

Main 21a0298, near-endobj (T1 in radius):
  xref.offset-adjusted @637 'found 197 bytes from there', xref.chain-cycle, xref.rebuilt
  GetObject(4) = "four only in S"; GetObject(7) = "new seven in S"
  F xref.chain-loop, size-wrong x2
Main 21a0298, near-endobj-T1far: identical to the branch ('found -280 bytes from there'; 4 null, 7 "old seven").

Control: /Prev naming S itself (447), on the branch.
  xref.section-unreadable 'its /W gives a field a negative width. The chain goes on through its /Prev...'
  indexIncomplete=True; GetObject(4) and GetObject(7) are the new definitions after a rebuild; F xref.section-malformed @447.
Main relocated this one onto T0 too, serving "old seven".

far-endobj (1,200 bytes between T0 and S), branch and main alike:
  'xref.section-missing @1642 ... is not there, nor near it'
  F 'xref.section-not-found [Error] ... nor within 512 bytes of it'
  The rebuild serves the right objects.
FIX (refuter): The rule is the maintainer's to settle. I recommend ending the search at the nearest Malformed candidate that DeclaresItselfASection (and is nearer than any candidate that reads), as the search already ends at a guard-cut declared candidate:
- take it as the section found moved (section.RelocateTo(attempt), xref.offset-adjusted at it), then return it as Malformed so that TryReadXRefChain reports it;
- for a /Type /XRef stream whose dictionary was read whole, go through PreviousToHandOn/HandOnPrevious: report xref.section-unreadable, run RefuseDeclared, and follow its /Prev;
- for a table, ReportLostSection, with the index incomplete;
- the validator then gives xref.section-shifted plus xref.section-malformed at the candidate.

Smaller alternative: when such a candidate is dropped, report it (xref.section-unreadable at the candidate) and mark the index incomplete, so that older rows do not stand in silence and the "nor near it" text is not said.

Either way, #320's expected outcome changes: its oldest table at 224, 30 bytes from the named 254, would be taken, not the table at 395. If this batch does not settle it, file it as debt beside #320 under M02 with both shapes, the newer table inside and outside the radius, since the branch's #188 skip makes the first reachable. Tests in CrossReferenceChainTests: GetObject(4) = "four only in S" and GetObject(7) = "new seven in S", S reported where it is, no chain-loop. The same in CrossReferenceRuleTests: xref.section-malformed at S, and no false file.size-wrong.

######## [4] chain:guard-prev-reference-to-declared-number-unjudged — reviewer major; refuter confirmed major maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:2875 (LeadsPastIndex)
CLAIM: LeadsPastIndex consults _refusedNumbers only, not the ranges a lost stream refused (RefusedToOlderRows). A section a guard stopped may give a /Prev written as a reference to a number a newer lost stream declared. That reference is taken as pointing past the guard, though no row past the guard can place that number, since every older row for it is refused. The /Prev fault a whole read reports is therefore silent under the guard, so #264's rule ('can name no section whatever the guard hid ... reported as a whole read reports it') does not hold for this shape. This is the LeadsPastIndex half of residual [6-stream-prev].
EVIDENCE: (q8) Told <- T0 (40 rows; /Prev 9 0 R; object 9 holds Told's offset) <- S (lost; /Index [9 1]; /Prev T0) <- T1.
MaxXRefSectionLength = 300:
  Diagnostics: xref.section-unreadable (S) and limit.xref-section-length only.
  Findings: file.trailer-value-wrong, xref.section-malformed, xref.checked-in-part. No finding on the /Prev.
Default limits:
  'xref.section-missing @2695: The /Prev of the cross-reference section at offset 2695 is not an offset'
  'xref.section-not-found [Error]: ... is the reference 9 0 R, which no section read before it places where it can be read.'
Same result with number 35, whose row lies past the cut.
FIX (reviewer): Have LeadsPastIndex return false when RefusedToOlderRows(number) is true, as it does for _refusedNumbers. Add the q8 shape to ReaderLimitsTests beside its whole read.
--- VERDICT REASON: The finding holds on origin/ccr-6d6c0ee6-dqm4lm-188 (071f829). I rebuilt q8 by hand and got the same result. LeadsPastIndex (src/AdCodicem.Pdf/IO/PdfFileReader.cs:2875) checks only _refusedNumbers. RefuseDeclared/RefuseRanges refuse a number to the rows of every section the chain reads afterwards, and that includes the guard-stopped section's own rows past the guard. So no row past the guard can place a number a newer lost stream declared, which is the same reason the code's own comment gives for _refusedNumbers. Under MaxXRefSectionLength or MaxIndexEntries the reference is still taken as the guard's. The xref.section-missing / xref.section-not-found that a whole read reports is then missing, TrailersHiddenByGuard is wrongly true, and #264's closing rule ('judged as far as it can without reading past the guard ... reported as a whole read reports it') fails for this shape. The shape is new to the batch, since main has no hand-on and no refused ranges, so there is nothing to compare on main. Severity stays major because a report is missing and #264 is not fully implemented. One narrowing: the shape needs a lost stream that declares exactly the number the older section's /Prev reference names. The fix the finding proposes is incomplete. With it alone the whole unit suite still passes (7,196, 0 failed), but it causes a regression the suite does not catch, and the evidence below shows it.
VERDICT EVIDENCE: Probe (uncommitted, since removed): %PDF-1.5, catalog 1, pages 2. Told is a classic table with /Root. Object N holds Told's offset. T0 is 'xref 0 40' whose row N places object N, trailer << /Size 40 /Prev N 0 R >>. S is '50 0 obj << /Type /XRef /Size 51 /Index [N 1] /W [1 -4 2] /Prev T0 /Length 0 >>'. T1 is 'xref 0 1', trailer << /Size 51 /Root 1 0 R /Prev S >>.
Branch head, N=9 (row read before the cut) and N=35 (row past the cut), same result for both:
  default: xref.section-unreadable @S; xref.section-missing @T0 'The /Prev of the cross-reference section at offset 252 is not an offset'; xref.rebuilt; validator xref.section-not-found '... is the reference 9 0 R, which no section read before it places where it can be read.' TrailersHiddenByGuard=False.
  MaxXRefSectionLength=300: xref.section-unreadable, limit.xref-section-length, xref.rebuilt; validator file.trailer-value-wrong, xref.section-malformed, xref.checked-in-part. No section-missing or section-not-found. TrailersHiddenByGuard=True.
  MaxIndexEntries=5: same as the line above, with limit.index-entries instead. Silent on the /Prev, TrailersHiddenByGuard=True.
  Control (S declares 45, /Prev 35 0 R): the whole read follows the /Prev without fault, and the guarded read stays the guard's. This is correct.
Proposed fix alone ('&& !RefusedToOlderRows(number)' beside _refusedNumbers): q8 is fixed under both guards. It regresses this file: the newest section is stream 60 with /Index [0 50] /W [1 4 2] /Prev 20 0 R and no /Root, data of 30 rows (read in part, rows short), row 20 placing object 20 = Told's offset. Read under MaxIndexEntries=5:
  branch head: limit.index-entries, xref.section-unreadable, trailer.root-recovered; TrailersHiddenByGuard=True; no file.root-invalid. A whole read is clean apart from section-malformed and trailer-value-wrong.
  with the fix alone: TrailersHiddenByGuard=False and 'F file.root-invalid [Error]: The trailer has no /Root.' The guard's cut is blamed on the file (ADR 34). Cause: at :3206, NamesOlderSection runs after ReportSectionReadInPart has called RefuseRowsLost(section). The section's own refusal of its declared numbers then counts against its own rows past the full index.
Fix plus moving RefuseRowsLost after the TrailersHiddenByGuard block (and after ReportSectionReadInPart on the /XRefStm path): q8 matches the whole read plus the guard under both guards. The short-stream edge keeps TrailersHiddenByGuard=True with no root-invalid. Unit suite 7,196 total, 0 failed.
FIX (refuter): In LeadsPastIndex, consult the refused ranges only in the branch about rows left unread: '(_cutWhileChainIsRead?.ContainsKey(reference.Id) == true || (!_xref.TryGet(number, out _) && !RefusedToOlderRows(number) && MayBeLeftUnread(number)))'. A number placed by a newer row and cut by MaxObjectLength stays the guard's that way. Extend the comment so it covers numbers a lost stream declared. Only refusals made by sections newer than the one judged may count. Today SectionOffset at :4594/:5261 runs before the section's own refusal, but NamesOlderSection at :3206 runs after it. So take RefuseRowsLost out of ReportSectionReadInPart and call it after the TrailersHiddenByGuard block in the chain loop, and right after ReportSectionReadInPart on the /XRefStm path. Alternatively, snapshot the refusal before the section's own refusal. Tests for ReaderLimitsTests, each beside its whole read: (1) q8 with N inside and past the cut, under the rows guard and the index guard: diagnostics equal the whole read's plus the guard, and TrailersHiddenByGuard is false; (2) a stream read in part (rows short) that fills MaxIndexEntries, whose /Prev reference names one of its own rows past the full point, with no /Root in it: TrailersHiddenByGuard stays true and there is no file.root-invalid. Test (2) is what tells the full fix from the naive one; the suite as it stands does not. This is not a design choice for the maintainer: the code's own principle for _refusedNumbers decides it.

######## [5] chain:hand-on-message-claims-chain-goes-on — reviewer minor; refuter confirmed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3478 (HandOnPrevious reports before SectionOffset) / :3817-3819
CLAIM: A handed-on stream is reported with 'The chain goes on through its /Prev' even when that /Prev names no offset. The next diagnostic then says the /Prev is not an offset, so the first message is false for this file (residual [6-stream-prev], unfiled).
EVIDENCE: (p8) The lost stream's /Prev is /Nowhere.
  'xref.section-unreadable @1413: ... The chain goes on through its /Prev; the objects it numbers ...'
  then 'xref.section-missing @1413: The /Prev of the cross-reference section at offset 1413 is not an offset; ...'
  Findings: xref.section-malformed and xref.section-not-found [Error] '... is the name /Nowhere, not an offset.'
FIX (reviewer): Test whether the /Prev reads as an offset (SectionOffset's value check, without its report) before calling ReportLostSection, and say 'its /Prev names no section' otherwise. Add a test for the /Prev /Nowhere shape.
--- VERDICT REASON: The finding holds on origin/ccr-6d6c0ee6-dqm4lm-188 (071f829). The new hand-on message comes from 8c33e4b (#208), so main 21a0298 has nothing to compare it with. The defect is new in this batch and is not old debt. None of #320 to #328 covers it, and residuals-impl.md lists it as unfiled ([6-stream-prev]).

What happens: PreviousToHandOn (PdfFileReader.cs:3433-3434) only checks that the dictionary has a /Prev. It does not check that the /Prev reads as an offset. HandOnPrevious (:3473-3481) then calls ReportLostSection first, and that message hard-codes "The chain goes on through its /Prev" (:3818-3819). Only after that does SectionOffset (:6041) find that the /Prev is not an offset. It reports xref.section-missing, and the chain ends.

Three shapes are affected: a name, a negative integer, and a reference no section places. For each, the first diagnostic states something false, and the next one, at the same position, contradicts it. The docs carry the same claim: reference/diagnostics.md:112 says such a stream "hands that /Prev on, and the chain goes on to the older sections, as the message says".

Narrowing: an offset outside the file, or one that loops, is acceptable as is. The chain does go on to that offset, and section-missing or chain-cycle then reports it the same way it would for a section that was read. A /Prev reference to an object that MaxObjectLength cut also ends the chain. That stop belongs to the guard, though, and the /Prev may still name a real section, so "goes on" is defensible there.

Severity is minor, not major. The diagnostic codes, positions, index state, refused numbers and validator findings are all correct, and the very next diagnostic gives the real fault. Only the text is misleading. No existing test asserts the first message for a /Prev that is not an offset. CrossReferenceRuleTests' theory covers "/X" and "99 0 R" for the validator only.
VERDICT EVIDENCE: Probe on 071f829 with LostStreamFiles.ThreeRevisions(prev: ...): oldest=200, stream=1650, default limits.

prev=/Nowhere:
  xref.section-unreadable [Warning] @1650: The cross-reference stream /Prev names at offset 1650 is there but cannot be read: its /W gives a field a negative width. The chain goes on through its /Prev; the objects it numbers that no section read before it indexes are looked for by rebuilding the index, rather than taken from an older section's rows.
  xref.section-missing [Warning] @1650: The /Prev of the cross-reference section at offset 1650 is not an offset; the sections it names are looked for by rebuilding the index.
  xref.rebuilt [Repair]; object.redefined [Information]
  sections: startxref@3054:Read, /Prev@1650:Malformed, /Prev@-1:NotFound
  findings: xref.section-malformed [Error] '...cannot be read: its /W gives a field a negative width.'; xref.section-not-found [Error] 'The /Prev of the cross-reference section at offset 1650 is the name /Nowhere, not an offset.'

prev=-5 and prev=99 0 R: the same "The chain goes on through its /Prev" message, followed by the same xref.section-missing "is not an offset". The findings say "is the integer -5, not an offset" and "is the reference 99 0 R, which no section read before it places where it can be read".

Acceptable shapes, for comparison:
- prev=99999999: "goes on", then section-missing "...at offset 99999999 lies outside the file, which is 3,173 bytes long".
- prev={stream}: "goes on", then chain-cycle "...names offset 1650, which the chain has already named".
- prev=9 0 R with MaxObjectLength 1024: "goes on", then limit.object, with sections startxref@5072:Read, /Prev@1650:Malformed.

The reviewer's offset @1413 differs only by the gap; the text reproduces word for word.
FIX (refuter): Fix it in this batch, since the text is new in it.

In HandOnPrevious, decide whether the /Prev reads as an offset before reporting. Use SectionOffset's own value read without its reports: `var value = dictionary.GetRaw(PdfName.Prev); var read = value is PdfReference r ? ReadWithoutLoading(r.Id) : value; var goesOn = read?.AsInteger() is >= 0;`. ReadWithoutLoading reports nothing and caches its result, so the later SectionOffset call changes no output, and the diagnostic order stays unreadable-then-missing. Pass goesOn to ReportLostSection, or keep it on the record. Say "The chain goes on through its /Prev;" only when goesOn is true. Otherwise use the existing refused-numbers sentence without that clause ("The objects it numbers that no section read before it indexes are looked for by rebuilding the index, rather than taken from an older section's rows.", or its every-number form), and let the xref.section-missing that follows say why the chain ends.

I would not use the proposed "its /Prev names no section" wording for the guard-cut reference. There the /Prev may name a section that the guard hides, so keep "goes on" for that case, as today. Keep PreviousHandedOn true in every case: TrailerValueWrongRule needs it to judge the /Prev's form. Reword that property's internal XML doc to "the chain read the /Prev its dictionary gives". Add "where it names an offset" to reference/diagnostics.md:112 ("hands that /Prev on, and the chain goes on ... as the message says").

Add a theory over ThreeRevisions(prev: "/Nowhere" | "-5" | "99 0 R") asserting the exact first message, without the "goes on" clause, and xref.section-missing second. The mutation catalog requires a row for the goesOn condition, so this theory also kills its negation. No maintainer decision is needed: the wording reuses an existing message.

######## [6] chain:xrefstm-prev-outside-file-called-a-section — reviewer nit; refuter confirmed nit maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3245
CLAIM: When /XRefStm and /Prev give the same offset outside the file, xref.hybrid-stream-lost calls that offset 'the section its /Prev names too, which the chain reads through that /Prev'. No section is there; the next report says the offset lies outside the file.
EVIDENCE: (p10) /Prev 99999999 /XRefStm 99999999.
  'xref.hybrid-stream-lost @110: ... names offset 99999999, the section its /Prev names too, which the chain reads through that /Prev: ...'
  then 'xref.section-missing @110: ... lies outside the file, which is 273 bytes long'.
  The validator repeats the wording in xref.hybrid-stream-wrong.
FIX (reviewer): Use 'an offset outside the file, which its /Prev names too' when !IsInFile(namedWithPrevious.NamedOffset), as the past-guard variant already words its own case.
--- VERDICT REASON: The finding holds, and it is a wording nit only. On the branch head 071f829, when a table's /Prev and /XRefStm both name the same offset outside the file, xref.hybrid-stream-lost says the offset holds "the section its /Prev names too, which the chain reads through that /Prev". The very next report, xref.section-missing, says that offset lies outside the file. HybridStreamWrongRule takes the same text through XRefSectionRecord.NamesNoStream, so the validator repeats it. The text comes from 7d35896 (#156); `git grep` finds no trace of it at 21a0298, so main has nothing to compare.

The behavior itself is as decided: branch (c) of B19-5 does not follow the /XRefStm, and CrossReferenceRuleTests:880-881 expects xref.hybrid-stream-wrong for this shape. Only the words are wrong.

"The section its /Prev names" alone matches the house style (xref.section-missing also says "The cross-reference section /Prev names at offset 99999999 lies outside the file"). The part that overstates is "which the chain reads through that /Prev": the chain reads nothing there. The reader already knows the offset is outside the file through IsInFile when it writes the message.

The chain-loop message right above this code (PdfFileReader.cs:3109, ChainLoopRule.cs:43) already has its own variant for this case: "outside the file, which the chain has already named". That loop message is the precedent to follow.

One correction to the finding's own fix: the past-guard variant does not word an outside-the-file case. Its condition is `pastGuard && IsInFile(...)`, so a past-guard offset outside the file falls through to the same "reads through that /Prev" text. ReaderLimitsTests:2454-2455 and CrossReferenceRuleTests:880-881 assert that the guarded and whole reads give equal output in that case. So the outside-the-file wording must be tested before pastGuard, and it then covers both reads.

Not a maintainer design choice.
VERDICT EVIDENCE: Branch head 071f829. Probe test (uncommitted, since removed): PdfTemplate.SoundWith("/Size 4 /Root 1 0 R", "/Size 4 /Root 1 0 R " + links), opened, then validated.

links = "/Prev 99999999 /XRefStm 99999999" (file is 379 bytes):
  xref.hybrid-stream-lost @203: The /XRefStm of the cross-reference table at offset 203 names offset 99999999, the section its /Prev names too, which the chain reads through that /Prev: ISO 32000-1 has it name the cross-reference stream of the table's revision (Table 19). The objects only that stream indexes are looked for by rebuilding the index.
  xref.section-missing @203: The cross-reference section /Prev names at offset 99999999 lies outside the file, which is 379 bytes long; the objects only it indexes are looked for by rebuilding the index.
  RULE xref.section-not-found Error @offset 203: ... the offset lies outside the file.
  RULE xref.hybrid-stream-wrong Error @offset 203: The /XRefStm of the cross-reference table at offset 203 names offset 99999999, the section its /Prev names too, which the chain reads through that /Prev, where ISO 32000-1 names the cross-reference stream of the table's revision (Table 19): what only that stream indexes is lost.

links = "/Prev 9999999999 /XRefStm 9999999999": same wording.
links = "/XRefStm 99999999" alone: only xref.section-missing ("The cross-reference section /XRefStm names at offset 99999999 lies outside the file").

Code, PdfFileReader.cs:3241-3245:
  pastGuard && IsInFile(namedWithPrevious.NamedOffset) ? "the section its /Prev names too, past the guard that stopped the table" : "the section its /Prev names too, which the chain reads through that /Prev"
A past-guard offset outside the file therefore gets the second text too; the tests at ReaderLimitsTests:2454-2455 and CrossReferenceRuleTests:880-881 require the guarded and whole reads to be equal. The precedent for a separate outside-the-file text is PdfFileReader.cs:3109: "outside the file, which the chain has already named".
FIX (refuter): In PdfFileReader.cs:3241-3245, choose the text three ways, testing the outside-the-file case first:
  !IsInFile(namedWithPrevious.NamedOffset) ? "outside the file, which its /Prev names too"
  : pastGuard ? "the section its /Prev names too, past the guard that stopped the table"
  : "the section its /Prev names too, which the chain reads through that /Prev"
The message then reads "names offset 99999999, outside the file, which its /Prev names too: ISO 32000-1 ...", worded like the chain-loop message, and the validator inherits it through NamesNoStream.

Add a reader test and a rule test that assert this text for "/Prev 99999999 /XRefStm 99999999", read whole and past a guard. The existing equality tests stay as they are.

Optionally, mention the outside-the-file case in the ReportNoStream / NamesNoStream XML docs.

######## [7] sections:guard-cut-table-decided-by-index-count — reviewer major; refuter narrowed major maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:4429 (TryReadClassicTable, guard branch `return _xref.Count > 0;`), with :3331 (chain end `return _xref.Count > 0;`)
CLAIM: #207 changed the end-of-file branch so that what decides is the table's own rows (HighestNumber), not the index's count. The MaxXRefSectionLength branch two lines below still decides by `_xref.Count > 0`. So a table the guard cuts before its first row is kept, as the guard's, only when a newer section indexed something. Otherwise it is Malformed and the chain indexes nothing. Two consequences. (1) The whole file is rebuilt at opening and read past the guard, with xref.rebuilt (Repair) on a sound file, while a looser guard reads less. (2) The #264 judgment of the cut table's /Prev depends on what another section indexed.
EVIDENCE: Probe ZzP21 runs the sound PdfTemplate.Sound. With MaxXRefSectionLength=12: `limit.xref-section-length @203`, then `[Repair] xref.rebuilt`, section `state=Malformed cut=True`, catalog read, object 3 read, repaired=True. With MaxXRefSectionLength=40: the limit alone, `state=Read cut=True`, object 3 = null, repaired=False. Probe ZzP12 uses a table with 300 spaces after `xref` and `/Prev 999999`, cut at 150. Under a newer `0 0` table: `limit.xref-section-length`, `xref.rebuilt`, section Malformed, and no xref.section-missing. Under a newer table indexing object 4: `limit…`, `xref.section-missing @133: …/Prev names at offset 999999 lies outside the file…`, and rule xref.section-not-found. Without the guard both report xref.section-missing. On main 21a0298 the rebuild happens the same way; there the /Prev was judged in neither shape.
FIX (reviewer): In the guard branch, keep the table as the guard's whatever the index holds (`return true`): its keyword declares it, CutByLimit is set, and ReadTrailerPastRows already ran JudgeLinksPastGuard. At the chain's end, do not rebuild at opening a chain that indexes nothing because a guard cut it (`_xref.Count > 0 || _cutByGuard`). Its objects then read as null as the guard's (ADR 34). If the second half needs a decision, file it beside #218.
--- VERDICT REASON: The finding has two parts. Part 1 is refuted and part 2 holds, in a wider set of shapes than the reviewer showed.

Part 1 is the rebuild at opening (xref.rebuilt, WasRepaired) when a guard cuts a table before its first row and the index is empty. The code does what ADR 34 decided: "A rebuild that damage forces on such a chain keeps the entries the chain read; one the chain cannot do without — a guard that leaves it no section — runs as before." Main 21a0298 behaves the same way. The batch's own test CrossReferenceChainTests.Reports_a_table_that_prev_names_and_the_guard_cuts_before_any_row_under_the_guard_alone asserts [limit.xref-section-length, xref.rebuilt]. #218's closing criterion also exempts "where a reader limit caused the rebuild". The fact that a tighter guard reads more comes from that decided rule, so it is not a defect. Reversing it (`_xref.Count > 0 || _cutByGuard` at the chain's end) would reopen ADR 34. It is not part of the fix.

Part 2 holds. The guard branch at PdfFileReader.cs:4429 still returns `_xref.Count > 0`. When the index is empty, a table that MaxXRefSectionLength cut before any row becomes Malformed. In that case #264's judgment of its /Prev never runs:
- For startxref's own table, the chain returns false at once.
- For an older table, the chain breaks through ReportLostSection.

As a result, an offset outside the file, or a /Prev naming the table itself, goes unreported. The same table under a newer section that indexes a single free row has its /Prev judged. A whole read reports both.

#264's closing criterion and the TryReadXRefChain remarks ("an offset outside the file, a section the chain has reached … is reported as a whole read reports it") are therefore not met in this shape. That is a missing Error-level report, and whether it appears depends on what another section indexed.

Main judged the /Prev under no guard at all, so this is a gap left by #264's fix, not a regression against main. The bug is reachable only when the guard's window ends before the first row:
- with a limit the caller set very low;
- or, under the 64 MB default, with a hostile table that holds more than 64 MB before its first row.

The implementer saw part of this and left it unfiled as residual [9-guards]. The fix is one line and the whole unit suite passes with it, so it should be fixed in this batch rather than filed.
VERDICT EVIDENCE: I wrote my own probes (ZzRefuteGuardCut, uncommitted, now removed). All files use objects 1–3 and a table with 300 spaces after `xref`.

Branch 071f829, startxref's table, guard 150:
- With /Prev 999999 (outside the file): `limit.xref-section-length @133`, `[Repair] xref.rebuilt`, `section startxref@133 state=Malformed cut=True`. The validator gives only `xref.checked-in-part`.
- With /Prev naming its own offset: the same output, and no `xref.chain-cycle` or `xref.chain-loop`.
- The same files with no guard give `xref.section-missing @133 … offset 999999 lies outside the file` plus the Error rule `xref.section-not-found`, or `xref.chain-cycle` plus the Error rule `xref.chain-loop`.

Branch, the same table named by /Prev from a newer table:
- Newer table `0 0`, guard 150: `limit…`, `xref.rebuilt`, `/Prev@133 state=Malformed`. No section-missing and no section-not-found.
- Newer table indexing only the free row 0 (`0 1` plus a free row), guard 150: `limit…`, `xref.section-missing @133`, `xref.rebuilt`, and the rule `xref.section-not-found [Error]`.

Sound template, branch and main alike:
- Guard 12: `limit…`, `xref.rebuilt`, `state=Malformed`, objects read, repaired=True.
- Guards 28, 29 and 40: limit only, `state=Read cut=True`, 1=null, 3=null, repaired=False.

Main 21a0298: identical rebuild behavior. No guarded shape judges the /Prev (newer table indexing a free row, guard 150: limit only).

Experiment: guard branch changed to return true (`windowFull || _xref.Count > 0`):
- The whole unit project passes: 7,211 tests, 0 failed.
- startxref's table with /Prev outside: `limit`, `xref.section-missing`, `xref.rebuilt`, rule `xref.section-not-found [Error]`.
- startxref's table with /Prev naming itself: `limit`, `xref.chain-cycle`, `xref.rebuilt`, rule `xref.chain-loop [Error]`.
- Newer `0 0` table: section-missing and section-not-found are now reported, and the rebuild at opening is unchanged.
FIX (refuter): In TryReadClassicTable's `if (windowFull)` branch, return true whatever the index holds. The table's keyword declares it, CutByLimit is set, and ADR 34 makes a section that declared itself before a guard cut it the guard's. Replace the comment "Keep whatever was indexed" with that reason, citing #264.

Leave the chain's final `return _xref.Count > 0` alone. ADR 34's "a guard that leaves it no section — runs as before" keeps the rebuild at opening when the index is empty, and the experiment shows the fix does not touch that rebuild.

Add tests in ReaderLimitsTests or CrossReferenceRuleTests, each beside the same file read whole, for a table MaxXRefSectionLength cuts before its first row:
- startxref's table with /Prev outside the file (expect xref.section-missing and xref.section-not-found);
- startxref's table with /Prev naming itself (expect xref.chain-cycle and xref.chain-loop);
- an older table under a `0 0` update with /Prev outside the file.

Correct the comment in Reports_a_table_that_prev_names_and_the_guard_cuts_before_any_row_under_the_guard_alone. The rebuild comes from the empty index (ADR 34), not from "the catalog only the original indexes is needed at opening": under a newer section indexing a free row, that catalog reads as null with no rebuild.

Run ADR 53's mutation catalog on the changed line.

######## [8] sections:eof-cut-inside-trailer-keyword — reviewer major; refuter narrowed major maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:4334
CLAIM: #207's rule says a table the end of the file cuts before its trailer is kept, read only in part, once it read a row. It is not applied when the cut falls inside the trailer keyword. The partial keyword (`t`, `trai`) is taken as a stray token, so the table is Malformed. When startxref names it, the whole file is rebuilt at opening. When /Prev names it, it is reported as holding `the keyword trai` rather than as cut by the end of the file. One byte earlier, the same table is kept read in part.
EVIDENCE: Probe ZzP20 on PartialFiles.CutFirst, every row read. Tail '': `xref.section-unreadable @309: … startxref names at offset 309 was read only in part: the file ends before its trailer; …`, then trailer.root-recovered, repaired=False. Tail 't' or 'trai': only `[Repair] xref.rebuilt`, section `state=Malformed fault=it holds the keyword trai at offset 398, where a subsection or the trailer should start`, repaired=True. On CutUpdate (/Prev) with 'trai': `xref.section-unreadable @346: … is there but cannot be read: it holds the keyword trai at offset 375, where a subsection or the trailer should start. The rows read before the fault…`.
FIX (reviewer): In the non-integer token branch, take a keyword token that runs to the end of the file (`!windowFull && token.End == span.Length`) and is a proper prefix of `trailer` as the end of the file (`break`). The end-of-file branch then keeps the table read in part by HighestNumber, as #207 decided. Add the 't' and 'trai' rows to the #207 tests.
--- VERDICT REASON: Confirmed on origin/ccr-6d6c0ee6-dqm4lm-188 at 071f829. In TryReadClassicTable (src/AdCodicem.Pdf/IO/PdfFileReader.cs:4325-4339), a keyword that runs to the end of the file falls into the stray-token branch. So a table whose file ends with a proper prefix of `trailer` (t, tr, trai, traile) is Malformed. One byte earlier it is kept, read in part, under #207's rule.

The partial-token-at-end-of-file policy is applied everywhere else in the table: a cut row (ReadSubsection :4741-4749), a lone subsection-header integer (`4`) and a header with a cut row (`4 1\n00000`) all take the end-of-file branch. The reader's own window-edge comment (:4300) already treats `trai` as a cut `trailer`. #207's closing text says "A table whose trailer the end of the file cuts is reported … The first section, which startxref names, is judged the same way". The batch decided that "startxref's section read in part [is] not rebuilt at opening". Neither holds for these 6 cut positions, and no #207 test has a keyword-prefix row.

Narrowed: the two namings do not have the same weight.
- **/Prev naming:** only the section's state (Malformed instead of Read+Incomplete), the reader's wording and the validator's rule differ (xref.section-malformed instead of file.trailer-missing, both Errors). The rows are kept, objects 3 and 4 are served the same, and it is reported once. Alone, this part would be minor.
- **startxref naming:** this is where it is major. It rebuilds the whole index at opening and reports only xref.rebuilt; that missing report is #323's, already filed. The rows the cut table read are also set aside, so an object the newest revision frees is served from its older definition (shown below).

Not part of this finding: the cut at or past the whole keyword. `trailer` or `trailer\n` at the end of the file follows #322's path (no dictionary). A trailer dictionary cut by the end of the file (`trailer\n<< /Size 5`) reads as Read, not incomplete, and object 4 as null with no rebuild — #207's own symptom. #322's wording ("followed by no dictionary") does not name that case, and no open issue I found does.
VERDICT EVIDENCE: Probe ZzRefuteTrai (kept at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/ZzRefuteTrai.cs.txt), on the branch, Release build.

**PartialFiles.CutFirst + tail:**
- **Tail ''** — `Warning xref.section-unreadable @309: The cross-reference table startxref names at offset 309 was read only in part: the file ends before its trailer; …`, then trailer.root-recovered. repaired=False; section Read, incomplete=True, Missing. Object 3 read with no rebuild; object 4 read through a rebuild. Validator: `Error file.trailer-missing @398`.
- **Tails '4' and '4 1\n00000'** — identical to '' (read in part, file.trailer-missing).
- **Tails 't', 'tr', 'trai', 'traile' (and 'x')** — only `Repair xref.rebuilt @-1`. repaired=True at opening; section `state=Malformed fault=it holds the keyword trai at offset 398, where a subsection or the trailer should start`. Validator: `Error xref.section-malformed @309: … cannot be read: it holds the keyword trai at offset 398 …`.
- **Wrong object served** — CutFirst with the cut table as `0 5`, its last row freeing object 4 (`0000000000 00001 f`):
  - tail '': `GetObject(4)` gives null, repaired=False.
  - tail 'trai': `xref.rebuilt` at opening, and `GetObject(4)` gives "object 4", the definition the newest revision deleted.

**PartialFiles.CutUpdate("3 1\n{row:3}\n" + tail):**
- **Tail ''** — `Warning xref.section-unreadable @346: … /Prev names at offset 346 was read only in part: the file ends before its trailer; …`; section Read, incomplete=True.
- **Tails 't', 'trai', 'traile'** — `Warning xref.section-unreadable @346: … is there but cannot be read: it holds the keyword trai at offset 375, where a subsection or the trailer should start. The rows read before the fault, if any, were kept; …`; section Malformed. Object 3 is read with no rebuild and object 4 through a rebuild, as with ''. Validator: `Error xref.section-malformed @346` (instead of file.trailer-missing).

**Neighbor, outside this finding:**
- **CutUpdate + 'trailer', 'trailer\n' or 'trailer\n<< /Size 5'** — only `Warning syntax.truncated-object`. Section Read, incomplete=False, trailerFault NotADictionary or Malformed. `obj 4: null repaired=False`. Validator: false `file.size-wrong @282` (/Size 5 makes it 4); with '<< /Size 5' a second one @375.
- **CutFirst + 'trailer\n<< /Size 5 /Ro'** — likewise object 4 is null, plus file.root-invalid and file.size-wrong.

Existing #207 tests (CrossReferenceChainTests.cs:1766-1860, 2120-2140) have no row for a cut inside the keyword. Main was not probed: the finding claims no difference from it.
FIX (refuter): In TryReadClassicTable's non-integer branch (PdfFileReader.cs:4325), before the stray-token fault: when `!windowFull && token.End == span.Length` and the token is a keyword that is a proper prefix of TrailerKeyword, `break`. The end-of-file branch (:4410-4425) then keeps the table read in part when HighestNumber >= 0 (unreadable "the file ends before its trailer" when it read no row), as #207 decided. `!windowFull` already means the span ends at the file's end, so this cannot misread a window edge.

Optionally, set TrailerPosition to the prefix's start rather than _source.Length. The same judgment exists in ReadTrailerPastRows' stray branch (:4529-4535), reached only past a MaxXRefSectionLength cut. It was not probed and should get the same prefix rule if a test confirms it.

Tests:
- Add 't' and 'trai' rows to the #207 CutUpdate theories (read in part, file.trailer-missing in CrossReferenceRuleTests).
- Add a CutFirst row asserting no rebuild at opening.
- Add a test where the cut table frees an object, which must read as null.

No maintainer choice is needed for this: it applies #207's decided rule. The neighbor — the end of the file cutting the trailer at or past the whole keyword, especially inside the dictionary, which #322's wording does not name — needs a debt or a comment widening #322. #322 already leaves its first-section case to the maintainer.

######## [9] sections:whitespace-offset-rereads-section-doubling-reports — reviewer major; refuter narrowed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3349 (ReachedBefore), :3358 (RecordSectionRead), :3157-3172 (hand-on records no position)
CLAIM: A /Prev that names a byte of the white space before a section the chain already reached is not seen as reached. `_sectionsReadAt` holds only the keyword or header position, and a lost stream that hands /Prev on is not recorded at all. So the section is read again, and every report this batch adds is emitted twice: xref.section-unreadable for read in part or hand-on, xref.entry-reserved, and the validator's xref.entry-type-reserved. The loop is reported one step late, naming the wrong pair. Main re-read such sections too (residual 5-relocation), but in silence.
EVIDENCE: Probe ZzP13, in which the oldest table's /Prev names the section's offset minus 1. table-in-part: `xref.section-unreadable @136: … was read only in part: the row for object 3…` and again `@135` with the same text, then `xref.chain-cycle @275: … the /Prev of the section at offset 135 names offset 275`. stream-lost: `xref.section-unreadable @136 … is there but cannot be read: its /W gives a field a negative width. The chain goes on through its /Prev…`, twice (@136, @135). stream-reserved: `xref.entry-reserved @136` and `@135`, and rules xref.entry-type-reserved ×2 and file.size-wrong ×2 for one stream. With shift 0, each is reported once and the cycle is at @136. On main, the same files give no doubled reader report.
FIX (reviewer): Count a section as reached by where its first token starts. Probe the padded start before reading rows, which TryReadXRefSection already computes as Padding, and check `offset + padding` against `_sectionsReadAt`. Record the header position of a lost stream that hands /Prev on as well (Q9's one notion of already read). Add a test with a /Prev into the white space before a read-in-part table and before a lost stream.
--- VERDICT REASON: The finding reproduces on 071f829. If a /Prev names a byte of the white space before a section the chain has already reached, the chain reads that section a second time. Each per-section report this batch adds then comes out twice: xref.section-unreadable for a table read in part, xref.section-unreadable for a lost stream that hands its /Prev on, xref.entry-reserved and xref.section-empty, plus the rules xref.entry-type-reserved and xref.subsection-missing. The loop is caught one step late.

The cause is what the finding says. RecordSectionRead stores Offset + Padding (the first token), but ReachedBefore looks up PositionOf(offset), which is where the offset points, before the padding. Also, the hand-on branch at :3157-3172 does `continue` before it reaches RecordSectionRead, so a lost stream is never stored. Stages 5 and 6 reported both gaps as residuals and left them unfiled.

I narrow the finding on three points:
1. The re-reading predates the batch. Main 21a0298 reads the same files twice too, and it already doubles the validator's findings: xref.section-malformed x2 and file.size-wrong x2 for the table read in part. Main also catches the loop just as late.
2. "On main, no doubled reader report" is wrong for the lost-stream shape. Main relocates that stream (#188) and reports xref.offset-adjusted twice.
3. Nothing except the reports changes. Objects 1-3, WasRepaired and the null of a reserved row are identical with shift 0 and shift 1. The only other difference is one extra section record.

The chain-cycle message stays true of the pair it names: the second read at 136 does name 276, which was read. It just comes one step late. The shape needs a chain that already loops, which the file is reported for (xref.chain-cycle, plus xref.chain-loop as an Error). Each doubled message is also true of the offset it names. So this is a doubled report on a rare hostile shape, not a wrong fact or a wrong object. That makes it minor, not major.

It does fall short of the spirit of Q9 ("one notion of already read", by positions). The validator's xref.offset-imprecise already says that 136 designates the keyword at 137, yet the chain treats 136 as a new place.
VERDICT EVIDENCE: My own probe (not the reviewer's ZzP13; it was an uncommitted Zz test and is deleted, its output saved): objects 1-3, then three blank-ish bytes, then section M at 137, an oldest table, and a newest table at startxref. The chain runs newest -> M -> oldest, and the oldest table's /Prev names 137 - shift. Output: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute16/branch.txt (branch 071f829) and /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute16/main.txt (main 21a0298).

On the branch:

**Table read in part (last row of type 'x'), shift 1:**
- "[Warning] xref.section-unreadable @137: The cross-reference table /Prev names at offset 137 was read only in part: the row for object 3, at offset 206, …"
- the same text again @136 ("/Prev names at offset 136")
- "[Warning] xref.chain-cycle @276: … the /Prev of the section at offset 136 names offset 276, which the chain has already read."
- rules: xref.section-malformed at 137 and again at 136; xref.chain-loop "section at offset 136 names offset 276".
- Shift 2 gives the same pair, @137 and @135.
- Shift 0 gives one section-unreadable @137, and the cycle @137 "the /Prev of the section at offset 276 names offset 137".

**Stream with /W [1 -4 2], shift 1:** xref.section-unreadable "@137 … is there but cannot be read: its /W gives a field a negative width. The chain goes on through its /Prev; …" and again @136. Then chain-cycle @267. Rules: section-malformed x2.

**Stream with a type-9 row, shift 1:** xref.entry-reserved @137 and @136. Rules: xref.entry-type-reserved for 137 and for 136. file.size-wrong for 288 changes from "0, makes it 1" (shift 0) to "6, makes it 7".

**Empty table, shift 1:** xref.section-empty @137 and @136. Rules: xref.subsection-missing x2.

**Whole table, shift 1:** no doubled report, but the cycle is reported @277 "section at offset 136 names offset 277".

Objects at shift 0 and shift 1 are identical in every shape. For example, in-part: 3="three" with repaired=True in both. Reserved: 3=null with repaired=False in both. Only the section count changes, 3 -> 4.

On main, with the same files:
- In part, shift 1: no reader section report, but the rules give section-malformed @137 and @136, and file.size-wrong for 137 and 136. The cycle is @276 "section at offset 136 names offset 276", the same late loop.
- Lost stream, shift 1: "[Repair] xref.offset-adjusted @267: … /Prev names at offset 137 was found 130 bytes from there." and the same @136, so the reader does double here. The rules give xref.section-shifted x2.
- Reserved, shift 1: no doubled report; the late cycle @288; file.size-wrong for 288 reads "6, makes it 7".

In the code (PdfFileReader.cs):
- :3349-3350: `named.Contains(offset) || _sectionsReadAt.Contains(PositionOf(offset))`
- :3358-3361: `var start = section.Offset + section.Padding; _sectionsReadAt.Add(start);`
- :3157-3172: the hand-on branch does `continue` before RecordSectionRead at :3190.
- TryReadXRefSection :4208 computes Padding as the first token's start before any row is read.

This is the unfiled residual of stage 5 (relocation) and stage 6 (stream-prev) in residuals-impl.md.
FIX (refuter): Make ReachedBefore look up where the offset's first token starts, not where the offset points.

1. **Shared helper.** Add one, e.g. FirstTokenAt(offset). It lexes the first token in a window of XRefProbeLength at PositionOf(offset), as TryReadXRefSection does at :4196-4208 (comments and white space skipped, the same widening up to the trailer bound), and returns position + first.Start. ReachedBefore then tests `named.Contains(offset) || _sectionsReadAt.Contains(PositionOf(offset)) || _sectionsReadAt.Contains(FirstTokenAt(offset))`. This costs one bounded window per section the chain names, which the section count guard already bounds. It also covers an /XRefStm, the pastGuard check and NamesOlderSection, since they share ReachedBefore.

2. **Lost streams that hand /Prev on.** Store the start of such a stream as reached: HandOnPrevious, or the hand-on branch, calls RecordSectionRead. One catch: CrossReferenceChainTests.The_prev_a_lost_stream_hands_on_ends_the_chain_in_a_loop_where_it_names_what_the_chain_reached(false, …) asserts "which the chain has already named" for a stream that names itself. LoopOffsetRead reads `_sectionsReadAt`, so storing the stream there would flip that text to "already read". So either keep a separate set of positions reached but not read for ReachedBefore, or change the wording. Choosing between those is a small wording choice for the maintainer; the rest of the fix is mechanical and follows Q9.

3. **Tests.** Add a CrossReferenceChainTests theory over a table read in part, a lost stream that hands /Prev on, and a stream with a reserved row, each with the oldest /Prev naming 1 and 2 bytes before the section. Each asserts:
   - the section's report appears once;
   - the chain-cycle is at the section, "the /Prev of the section at offset {oldest} names offset {section - shift}, which the chain has already read";
   - three section records.
   
   Add a CrossReferenceRuleTests case asserting one xref.section-malformed or one xref.entry-type-reserved, and one xref.chain-loop naming the oldest table.

If this is not fixed in batch 19, file it as debt under M02 with the reproducer above. The mechanism predates the batch and serves no wrong object.

######## [10] sections:xrefstm-malformed-table-older-rows-stand — reviewer major; refuter narrowed major maintainer=True
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3281-3297 (/XRefStm branch: only a /Type /XRef stream refuses; ReportLostSection for a table)
CLAIM: A classic table that an /XRefStm names and that cannot be read (Malformed) refuses nothing, yet the chain goes on past it through the hybrid table's /Prev. An older section's row then stands for a number whose row it is known to have lost: old seven is served over new seven. The same table read only in part refuses those numbers (#210), and is reported as xref.hybrid-stream-lost and xref.hybrid-stream-wrong. The Malformed one gets neither, so more damage brings less protection. A table that the /XRefStm names and that the end of the file cuts likewise lets older rows stand for the subsections past the cut, which cannot be told. The test A_table_an_xrefstm_names_that_the_end_of_the_file_cuts_inside_a_subsection_… (CrossReferenceChainTests.cs:2143) asserts `7 = "old seven"`. The docs' rule (lazy-reading.md: 'Where what was lost cannot be told … no older section's row stands') does not reach it. This is residuals 7-partial and 8-xrefstm, and a neighbor of #324, which covers only /Prev-named tables, where the chain stops.
EVIDENCE: Probe ZzP1, in which the /XRefStm-named table is `6 2`, the row of new six, then `junk junk junk\nmore`. `xref.section-unreadable @407: … is there but cannot be read: the row for object 7, at offset 436, is not an offset… The rows read before the fault, if any, were kept…`, and no hybrid-stream-lost. Chain index `5=Regular@110 6=Regular@355 7=Regular@161`, 7=("old seven"), repaired=False, rules: xref.section-malformed only. The variant read in part (bad last row): xref.hybrid-stream-lost plus xref.section-unreadable 'read only in part', index `6=- 7=-`, 7=("new seven") via a rebuild, and rule xref.hybrid-stream-wrong. On main the Malformed variant was relocated onto the startxref table and also served old seven.
FIX (reviewer): When a classic table that an /XRefStm names is lost (Malformed, not cut by a guard), or kept with its trailer cut by the end of the file, refuse every number to the sections read after it. What follows its fault cannot be told, as for a stream whose numbering cannot be read. Report it as xref.hybrid-stream-lost too, as the table read in its place is. Or file it with #324, whose remedy (refuse what the unread rows may hold) is the same, and correct the test's comment.
--- VERDICT REASON: The finding reproduces, but only part of it is a defect. Take a classic table that an /XRefStm names and that ends Malformed after a bad row inside a subsection it declares. The number of that row is known to be lost, because LoseRows recorded it in _rowsLost. The table is still never refused: RefuseRowsLost runs only from ReportSectionReadInPart, which runs only for a section that was Read. DeclaredByLostStream also returns null for a table. Then the chain goes on through the hybrid table's /Prev, and the older row stands. Old seven is served with no rebuild. The same bad row followed by a trailer is refused, and new seven is served. So the batch leaves more damage less protected. This asymmetry is new: on main 21a0298, every variant served old seven. It is not covered by B19-2 (which names streams only), by experiment R, or by #324 (only /Prev-named tables, where the chain stops and no older row can stand). What stays a design choice for the maintainer is what lies past the fault: a bad subsection header (A4), or a table the end of the file cuts after a whole subsection (C, which test :2143 asserts on purpose). Whether to refuse every number there is the open choice #324 already puts to the maintainer. The test's comment is accurate to the code, and the docs' "cannot be told" list is closed and does not claim these shapes. The missing xref.hybrid-stream-lost / xref.hybrid-stream-wrong follows the decided list, which names tables read. The loss is still reported, as xref.section-unreadable (index marked incomplete) and xref.section-malformed (Error), so that part is not a defect. One doc remark is wrong: ReportSectionReadInPart's XML remark at PdfFileReader.cs:3854 says of a table the end of the file cuts that "the chain ends there". For an /XRefStm-named one, the chain goes on.
VERDICT EVIDENCE: Probe ZzProbeXrefStmMalformed (kept at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/ZzRefuteXrefStmMalformed.cs.txt). The oldest table at 224 indexes 5, 6 and 7 (old seven at 197). After it the file writes new six at 411 and new seven at 436. The hybrid table at 463 has /Prev 224 and /XRefStm 576, and the /XRefStm table at 576 is 'xref\n6 2\n<row 411>' + variant.

Branch 071f829:
- A (variant 'junk junk junk\nmore'): `Warning xref.section-unreadable @576: The cross-reference table /XRefStm names at offset 576 is there but cannot be read: the row for object 7, at offset 605, is not an offset, a generation and n or f. The rows read before the fault, if any, were kept; the objects only the rest indexes are looked for by rebuilding the index.` Index `5=Regular@146 6=Regular@411 7=Regular@197`. Section /XRefStm @576 Malformed, /Prev @224 Read. `7 = old seven repaired=False`. Rule: only `Error xref.section-malformed at offset 576`.
- A2 (stray /Bad, then a trailer): same result, 7 = old seven.
- A4 (bad header '7 x'): same, 7 = old seven.
- B (same bad row, then 'trailer'): xref.hybrid-stream-lost @576, then `xref.section-unreadable ... was read only in part: the row for object 7, at offset 605, ...`. Index `7=-`, `7 = new seven repaired=True`. Rules: xref.section-malformed and xref.hybrid-stream-wrong.
- C2 (end of file inside 6 2): 7=- and new seven, through a rebuild.
- C (end of file after '6 1' whole): 7=Regular@197, old seven.
- D (sound): new seven.

Main 21a0298: A, A2 and A4 relocated onto the hybrid table at 463 (`xref.offset-adjusted ... found -113 bytes`, rule xref.section-shifted) and served old seven. B, C and C2 were Read and served old seven too.

Code: PdfFileReader.cs:3281-3297. A table ends DeclaresItselfASection, DeclaredByLostStream is null for it (XML doc: "a table, whose subsections are unknown past a fault among them"), and ReportLostSection is called without RefuseRowsLost. ReadSubsection :4757-4761 has already called LoseRows(7, 8).
FIX (refuter): In this batch, in the /XRefStm branch's lost path (PdfFileReader.cs:3281-3297): when the section is a classic table that is Malformed and not CutByLimit, refuse what it is known to have lost. Call RefuseRowsLost(stream), which uses _rowsLost/_rowsLostPast, still those of this table since a table is never relocated, before ReportLostSection. Flag it so that ReportLostSection words it like the read-in-part case ("the objects the rest of that subsection numbers are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows"). Add tests: a CrossReferenceChainTests shape A/A2 with 7 read as new seven, and a validator test; then a mutation pass. Correct the remark at :3854 so it says the chain ends there only for a table that startxref or /Prev names.

The maintainer decides what lies past the fault: a bad subsection header, a stray token, or a cut after a whole subsection (test :2143 and its comment). The choice is to refuse every number (RefuseEveryNumber, whose message already exists), or to leave it as now. Put that choice to the maintainer together with #324's (refuse below /Size, or parse the rest for headers), by commenting on #324 that the /XRefStm-named table is the one shape where the chain goes on today. Whether to add xref.hybrid-stream-lost/-wrong for a Malformed table is optional: the loss is already reported.

######## [11] sections:leadspastindex-ignores-refused-ranges — reviewer major; refuter narrowed major maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:2875
CLAIM: LeadsPastIndex treats a number in `_refusedNumbers` as one no row past the guard can place, but not a number in the refused ranges (RefusedToOlderRows). Take a /Prev written as a reference to a number a lost stream declared, in a section a guard stopped. It is taken as possibly naming a section past the guard, so it is the guard's. Read whole, that number can never be placed and the /Prev is reported. #264's 'judged as a whole read judges it' then fails: xref.section-missing and the rule xref.section-not-found go missing. This is residual 6-stream-prev.
EVIDENCE: Probe ZzP7. The newest table refuses row 8 (generation 65536). A lost stream (/W [1 -4 2] /Index [9 1]) hands /Prev to a middle table whose trailer gives `/Prev 9 0 R` and which is long enough to cut. No guard: `xref.section-missing @299: The /Prev of the cross-reference section at offset 299 is not an offset…`, and rule `xref.section-not-found [Error]: …is the reference 9 0 R, which no section read before it places…`. MaxXRefSectionLength=150: `limit.xref-section-length @299` and no xref.section-missing. Rules: file.trailer-value-wrong and xref.checked-in-part, and no xref.section-not-found. With `/Prev 8 0 R` (individually refused), xref.section-missing is reported both with and without the guard.
FIX (reviewer): `return number > 0 && _refusedNumbers?.Contains(number) != true && !RefusedToOlderRows(number) && (…)`, plus a row of the #264 tests with a /Prev reference to a number a lost stream declared.
--- VERDICT REASON: The defect is real. LeadsPastIndex (src/AdCodicem.Pdf/IO/PdfFileReader.cs:2875) skips numbers in _refusedNumbers but does not call RefusedToOlderRows. That misses the refused ranges (#208 lost streams, #210 sections read in part) and _everyNumberRefused. A row past the guard belongs to a section older than the refusing one, and AddFromChain refuses that row before indexing it (line 6200), so no row past the guard can place such a number. The in-code comment even says "a number the chain refused is never placed".

Because of this, a /Prev written as a reference to such a number is treated as "the guard's". #264 decided that a section a guard stops is judged as a whole read judges it, and the #264 tests hold that the guarded diagnostics equal the whole read's after the guard's report. Here that fails: xref.section-missing goes missing, the rule xref.section-not-found becomes file.trailer-value-wrong, and TrailersHiddenByGuard is wrongly True. This is a missing report and a decision implemented only in part, so it is major on the brief's scale. It is narrow in practice: it needs a lost or partly read newer section, a guard reached on the next section, and a non-conforming reference /Prev.

I narrowed the finding because its proposed fix is too wide and causes a worse regression. LeadsPastIndex is also called from NamesOlderSection (line 3206). That call runs after ReportSectionReadInPart, which already ran RefuseRowsLost for the stopped section's own lost rows. Those rows are not refused within that same section on a whole read. So with the one-line fix, a section that is both cut by a guard and read in part loses its hidden-trailer judgment: the reader rebuilds and reports file.root-invalid against the file. The full suite stays green with either fix, so no existing test covers either case. Not checked on main 21a0298, since #208 and the rows-guard judgment of #264 are new on this branch. This is not a regression from main.
VERDICT EVIDENCE: Rebuilt independently (probe ZzLpiProbe, copy at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/ZzLpiProbeTests.cs).
- Layout: the newest table indexes 1 and 2, refuses row 8 when asked, and names lost stream 5 (`<< /Type /XRef /Size 30 /W [1 -4 2] /Index [9 1] /Prev 110 /Length 0 >>`, empty data). The stream hands /Prev on to a table at 110 with rows `0 1`, `10 20` (20 free rows) and trailer `<< /Size 30 /Prev 9 0 R >>`.

Branch head 071f829, /Prev 9 0 R:
- Whole read:
  - Diagnostic `xref.section-missing at 110: The /Prev of the cross-reference section at offset 110 is not an offset; ...`
  - Rule `Error xref.section-not-found at offset 110: The /Prev ... is the reference 9 0 R, which no section read before it places where it can be read.`
  - TrailersHiddenByGuard=False.
- MaxXRefSectionLength=150:
  - Diagnostic `limit.xref-section-length at 110` and no xref.section-missing.
  - Rules `Warning file.trailer-value-wrong at offset 545: ... writes its /Prev as the reference 9 0 R ...` and `Information xref.checked-in-part at offset 110`, no xref.section-not-found.
  - TrailersHiddenByGuard=True.

Same result for these variants:
- With no refused row 8.
- With `/Prev 15 0 R` and `/Index [9 1 15 1]`.
- With `/Index [9 -1]`, which refuses every number.

Controls:
- `/Prev 8 0 R`, row 8 refused individually: xref.section-missing is reported with and without the guard.
- `/Prev 7 0 R` (not refused): the guard's, as designed.

With the proposed one-line fix (`&& !RefusedToOlderRows(number)`), every refused case under the guard gives `limit.xref-section-length` then `xref.section-missing`, and the rules xref.section-not-found plus xref.checked-in-part, with TrailersHiddenByGuard=False. That matches the whole read.

The one-line fix regresses another case (probe ZzLpiEdge, /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/ZzLpiEdgeTests.cs):
- Layout: an older table gives /Root. Object 10 holds that table's offset. The newest table has a bad row `abcdefghij 00000 n` in subsection `10 1`, then `11 20` free rows, then `10 1` placing object 10, and trailer `<< /Size 31 /Prev 10 0 R >>`.
- Whole read: TrailersHiddenByGuard=False, WasRepaired=False, catalog found.
- Branch head under 150: TrailersHiddenByGuard=True, RootHiddenByGuard=True, WasRepaired=False. This is correct: the cut hid the row for 10.
- One-line fix under 150: diagnostic `Repair xref.rebuilt`, TrailersHiddenByGuard=False, WasRepaired=True, and rule `Error file.root-invalid at offset 738: The trailer has no /Root. No object of the file is a catalog.`

The narrowed fix below passes both probes. The full unit project passes with it: 7,196 total, 0 failed, 20 skipped.
FIX (refuter): Saved as /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refuter-lpi-narrowed-fix.diff.
1. In LeadsPastIndex, make the check `number > 0 && _refusedNumbers?.Contains(number) != true && !RefusedToOlderRows(number) && (...)`. Update the comment and the XML doc to name the refused ranges.
2. In the chain loop, take `RefuseRowsLost(section)` out of ReportSectionReadInPart. Call it under `if (section.Incomplete)` just after the TrailersHiddenByGuard block and before `if (section.TrailerCutByLimit)`. In the hybrid /XRefStm branch, call it before `ReportSectionReadInPart(stream, "/XRefStm")`. That way the hidden-trailer judgment sees only refusals made by newer sections, and the order of diagnostics does not change.
3. Tests:
   - Add rows to the #264 tests (`document.Diagnostics.Skip(1).Should().Equal(whole.Diagnostics)`) where a section a guard stops (table rows, and table index) gives a /Prev reference to a number a newer lost stream declares. Add one more whose lost stream has an unreadable /Index, which refuses every number.
   - Add a test that a guard-cut table read in part, whose /Prev reference names a number from its own lost range re-placed past the cut, stays the guard's: TrailersHiddenByGuard true, no rebuild, no file.root-invalid.

Not a design choice for the maintainer. The ordering in step 2 is an implementation detail.

######## [12] sections:refused-row-message-claims-rows-after-read — reviewer minor; refuter narrowed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3879 (message), :6222-6239 (Refuse)
CLAIM: In a section a guard stopped, the read-in-part report for a refused row says 'that row alone was refused, the rows after it read'. That is false when the guard stopped the rows after it: MaxXRefSectionLength cut later rows, or MaxIndexEntries filled at that very row. In the second case the row was not even refused, since Refuse found no room and reached the bound. Refuse also sets `_indexIncomplete` there, so an object the index guard left out is looked for by a rebuild. That part predates the batch.
EVIDENCE: Probe ZzP16 (refused row 3, then 20 free rows, MaxXRefSectionLength=150): `limit.xref-section-length @133: … only the entries within it were read`, then `xref.section-unreadable @133: … read only in part: the row for object 3 … gives it generation 65536…; that row alone was refused, the rows after it read, …`. Probe ZzP11 (table-refused, MaxIndexEntries=3): `limit.index-entries @155: The index reached 3 entries…`, then the same 'the rows after it read' text, count=3. Reading object 4 then gives `[Repair] xref.rebuilt`. The stream-refused shape behaves the same. On main there was no read-in-part message, and the rebuild was the same.
FIX (reviewer): Record RefusedRow, and mark the index incomplete, only when Refuse took the row, as AddFromChain leaves a sound row at the fill point to the guard. Where section.CutByLimit, word the tail as 'the rows after it read up to where one of the reader's limits stopped the section'.
--- VERDICT REASON: Holds for the wording only. Commit 54d56c6 (#210) added the reader's read-in-part report for a refused row. It always says "that row alone was refused, the rows after it read". When a guard stopped the rows after the refused row, that is false. MaxXRefSectionLength can cut the table, or MaxIndexEntries can fill, at the refused row or at any later row of the section, and the guard's own report at the same offset says the opposite. On main 21a0298 there was no such report.

The other two sub-claims do not hold as batch defects:
(1) "At the fill point the row was not even refused" is internal only. The number does not enter _refusedNumbers, but the index is full, so no older row can stand for it either. The row's fault is the file's and the reader did read it. Reporting it follows #210's decision ("first fault, whatever a guard also did"). Dropping RefusedRow there, as proposed, would hide real damage.
(2) Refuse setting _indexIncomplete so that an object the guard left out is found by a rebuild is the same on main. It is not tied to the fill point: a row refused with room left, then the index filling at the next row, rebuilds for object 4 on both branches. The proposed "only when Refuse took the row" would not cover that case. This is the global damage flag's existing design (damage anywhere means any miss rebuilds), outside this batch.
VERDICT EVIDENCE: Rebuilt on the branch head 071f829 with my own probes, then the same probes on main 21a0298 (source from git archive).

Shape A: Spread with row 3 = "{off:3} 65536 n", table "0 25" (20 free rows added), MaxXRefSectionLength=150.
Branch:
- `limit.xref-section-length @1421: The cross-reference table runs past 150 bytes; only the entries within it were read. ...`
- `xref.section-unreadable @1421: ... was read only in part: the row for object 3, at offset 1491, gives it generation 65536, outside 0 to 65535; that row alone was refused, the rows after it read, and its object is looked for by rebuilding ...`
Main: only the limit report.

Shape B: Spread with the same bad row 3, MaxIndexEntries=3 (fills at row 3).
Branch:
- `limit.index-entries @1421: The index reached 3 entries; ...`
- the same "that row alone was refused, the rows after it read" text.
- Row 4 was never read. Then obj 4: PdfString repaired=True; obj 3: PdfNull.
Main: the limit report only, then the same obj 4 PdfString repaired=True and obj 3 PdfNull. The rebuild is unchanged.

Shape C: Spread with the same bad row 3, MaxIndexEntries=4. The row was refused with room left, and the index fills at row 4.
Branch: limit.index-entries @1421, then the same false "rows after it read" text. obj 4: PdfString repaired=True.
Main: the same rebuild, so the rebuild is not tied to the fill point.

Shape D: stream from the existing test (row 3 = type 2 in stream 0), MaxIndexEntries=3.
Branch: `limit.index-entries @212`, then `xref.section-unreadable @212: ... places it in object stream 0, which is no object number; that row alone was refused, the rows after it read, ...`
Main: the limit report only.

Control: Spread, sound rows, MaxIndexEntries=3. Only limit.index-entries; obj 3 and obj 4 are PdfNull with no rebuild, on both branches.

Code:
- PdfFileReader.cs:3877-3880 picks the RefusedRow branch whatever section.CutByLimit holds.
- The section-length cut sets CutByLimit at :4390.
- An index fill sets CutByLimit at :6213 (AddFromChain) and :6238 (Refuse).
FIX (refuter): In ReportSectionReadInPart's RefusedRow branch (PdfFileReader.cs:3877-3880), word the tail by section.CutByLimit. Without it, keep today's text. With it, say "that row alone was refused, the rows after it read as far as one of the reader's limits let the section be read, and its object is looked for by rebuilding …". The guard's own report names the property. This wording also stays true when only MaxTrailerLength cut the trailer, which sets CutByLimit too.

Add a test beside A_row_refused_alone_is_reported_once_at_its_section_and_the_rows_after_it_read, for both a table and a stream, with a guard stopping the section past the refused row: MaxIndexEntries filling at or after it, and MaxXRefSectionLength for the table.

Keep RefusedRow recorded and Refuse's _indexIncomplete as they are. The bad row is the file's fault (#210: "whatever a guard also did"). The rebuild that finds a guard-omitted object once damage was met predates the batch and is not specific to this path.

This is not a design choice for the maintainer: it rewords a message to match what the reader did.

######## [13] sections:fault-beside-cut-rows-said-kept — reviewer minor; refuter confirmed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3872
CLAIM: A stream whose value a guard cut and whose data holds fewer rows than it declares (the FaultBesideCut path with rows > 0) is now reported 'read only in part: …; the rows it holds were kept'. In that path no row is read at all: the rows the data may hold are left to the guard, with CutByLimit set and the chain ending there.
EVIDENCE: Probe ZzP15, the GuardCutValueTests shape `<< /Type /XRef /W 5 0 R /Size 8 /Length 3 >>` under MaxObjectLength=1024: `xref.section-unreadable @1326: … was read only in part: its data, 3 bytes decoded, holds at most 3 of the 8 rows its /Index and /Size declare; the rows it holds were kept, and the objects whose rows it lacks are looked for by rebuilding the index…`, then `limit.object @203`, and the section shows `incomplete=True cut=True`.
FIX (reviewer): In ReportSectionReadInPart, for a stream that is both CutByLimit and Incomplete, say that the rows its data may hold were left unread, a value they are read with having been cut by one of the reader's limits. Pin the message in A_section_whose_data_holds_fewer_rows_than_it_declares_whatever_a_value_a_guard_cut_holds_is_the_file_s_to_lack.
--- VERDICT REASON: The finding holds. Batch 19 adds ReportSectionReadInPart in commit 54d56c6, and its text is new: main 21a0298 has no "read only in part" message. For any stream section marked Incomplete, the method (PdfFileReader.cs:3872) appends "; the rows it holds were kept". But in TryReadXRefStream's FaultBesideCut branch with `rows > 0` (PdfFileReader.cs:4947-4972), no row of the section is ever read. The branch sets Incomplete, CutByLimit and _cutByGuard, merges the trailer and returns Read, with `previous` still at -1, so the chain ends there. The rows the data may hold are left to the guard, so the message says rows were kept when none were.

I confirmed this with a variant where the data really holds rows. A whole read keeps those rows. The guard read keeps none, yet prints the same "were kept" sentence.

The test the finding cites (GuardCutValueTests.cs:3650) checks only diagnostic codes, not the message. So nothing pins this text, and the test would pass with any wording.

Severity is minor, as the brief defines it: the text is misleading. The codes are right (section-unreadable, limit.object, xref.checked-in-part), and so is the behavior: a rebuild finds the objects, and the catalog is served.

One related point, already on main before this batch and not part of this finding: reference/diagnostics.md also says such a section is read "the chain going on". That is false when `rows > 0`, where the chain ends.

The rows==0 whole read prints "it holds 0 rows ...; the rows it holds were kept". That sentence is only vacuous, which is a nit.
VERDICT EVIDENCE: I rebuilt the reproducer on origin/ccr-6d6c0ee6-dqm4lm-188 (071f829), Release build. The probe used ChainFiles.UnderAnUpdate in the GuardCutValueTests shape, with object 5 padded by 1,100 spaces, /Prev rewritten to object 6, and ObjectGuard set to MaxObjectLength=1024.

(1) six = `<< /Type /XRef /W 5 0 R /Size 8 /Length 3 >>\nstream\nabc\nendstream`, five = `[1 4 2]`. The guard read prints:
`Warning xref.section-unreadable at 1326: The cross-reference stream /Prev names at offset 1326 was read only in part: its data, 3 bytes decoded, holds at most 3 of the 8 rows its /Index and /Size declare; the rows it holds were kept, and the objects whose rows it lacks are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows.`
It then prints `limit.object at 203` and `xref.rebuilt`. The section shows `state=Read incomplete=True cut=True highest=-1`.

(2) My own variant has rows that are really there. six = `<< /Type /XRef /W 5 0 R /Size 8 /Length 6 >>` with 6 bytes of data, and five = `[1 1 1]`. The data holds 2 real rows: object 0 free, and object 1 at offset 9.
- Whole read: `... was read only in part: it holds 2 rows where its /Index and /Size declare 8; the rows it holds were kept, ...`, with `highest=1` and chain index `1: Regular 9 0`. The sentence is true here.
- Guard read: `... was read only in part: its data, 6 bytes decoded, holds at most 6 of the 8 rows its /Index and /Size declare; the rows it holds were kept, ...`, but `highest=-1`, `cut=True`, and chain index `1: absent`. The two rows the data holds were not read.

Code: the `rows > 0` branch at PdfFileReader.cs:4965-4970 sets `_cutByGuard = true; section.CutByLimit = true; return XRefSectionState.Read;` without reading a row. `previous` was set to -1 at the method's entry, so the chain ends. The message comes from PdfFileReader.cs:3871-3872.

`git log -S"the rows it holds were kept" 21a0298..HEAD` returns only 54d56c6. GuardCutValueTests.cs:3650 asserts codes only. The one test that pins this text, CrossReferenceChainTests.cs:1903, pins the whole-read case only.
FIX (refuter): In ReportSectionReadInPart, give a stream whose rows were left to a guard its own clause. For example: "...; the rows its data may hold were not read, a value they are read with having been cut by one of the reader's limits, and the objects it numbers are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows".

Do not detect this case with the bare `CutByLimit && Incomplete` test the finding proposes. Mark it where it happens instead. One way: a record flag set in the FaultBesideCut `rows > 0` branch, such as RowsLeftToGuard. Another: test `section.CutByLimit && section.HighestNumber < 0`. A cut dictionary, or a later rows path, could otherwise set both bits on a section that did keep rows.

Pin the message in A_section_whose_data_holds_fewer_rows_than_it_declares_whatever_a_value_a_guard_cut_holds_is_the_file_s_to_lack for the hidesRows rows. Add one row whose data holds real rows (/W [1 1 1] behind the reference), so the guard text and the whole-read "were kept" text are shown to differ.

Optionally, on the vacuous rows==0 whole read, drop "the rows it holds were kept" when the stream holds none. Separately, the pre-existing "the chain going on" in reference/diagnostics.md's xref.section-unreadable row could say that data which may hold rows ends the chain at the guard. That is a wording fix, not a design choice.

######## [14] sections:relocated-section-message-names-found-offset — reviewer minor; refuter confirmed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3901, :3931, :3889
CLAIM: For a section found moved, the new reports say '/Prev names at offset X', where X is where the section was found, not the offset /Prev names. This holds for read only in part, xref.entry-reserved and xref.section-empty, and contradicts the xref.offset-adjusted report just before them.
EVIDENCE: Probe ZzP19 (/Prev names 162, the section is at 182). Table: `xref.offset-adjusted @182: The cross-reference section /Prev names at offset 162 was found 20 bytes after it.`, then `xref.section-unreadable @182: The cross-reference table /Prev names at offset 182 was read only in part: …`. Stream: the same, plus `xref.entry-reserved @182: The cross-reference stream /Prev names at offset 182 gives 1 row a reserved type…`.
FIX (reviewer): When section.WasRelocated, write 'that /Prev names, found at offset X' (or name both offsets) in ReportSectionReadInPart, in its xref.section-empty message, and in ReportReservedRows.
--- VERDICT REASON: The finding holds. I rebuilt it myself on origin/ccr-6d6c0ee6-dqm4lm-188 (071f829).

All three new messages are written as `{naming} names at offset {section.Offset}`:
- ReportSectionReadInPart, PdfFileReader.cs:3901 (xref.section-unreadable, "read only in part")
- its xref.section-empty branch, :3889
- ReportReservedRows, :3931 (xref.entry-reserved)

RelocateTo (XRefSectionRecord.cs:213) sets Offset to the place the section was found, so for a section found moved these texts give that place as the offset its naming gives. The xref.offset-adjusted report just before them gives the named offset correctly, so the two reports contradict each other.

It is wider than the claim in one respect. It applies to a section named by /XRefStm as well as by /Prev, because both callers (:3191 and :3307) pass the naming through.

It is narrower than "every report on a relocated section":
- The older "is there but cannot be read" text (:3832, also on main) never fires for a relocated section. TryRelocateXRefSection keeps a Malformed attempt only when a guard cut it, and ReportLostSection returns before that message when CutByLimit is set.
- The validator rules SubsectionMissingRule and ReservedTypeRule say "at offset X", with no "names", so they are correct.

All three messages are new in this batch. Main 21a0298 has none of them, so this is a defect of the batch, not an older one. The Position field (@182) is right in every case; only the text is wrong. That makes it a misleading text, which the brief classes as minor. No existing test covers a relocated section with these messages: every expected text in CrossReferenceChainTests uses a section read where it was named.
VERDICT EVIDENCE: The probe was a HandWrittenFile with version 1.5. Objects 1, 2, 3 and 9 come first. The section under test starts at 182. The newest table's trailer names it 20 bytes early, at 162, through /Prev or /XRefStm. Exact output on branch 071f829:

table, bad last row (`{three:D10} 00000 x`), named by /Prev:
  [Repair] xref.offset-adjusted @182: The cross-reference section /Prev names at offset 162 was found 20 bytes after it.
  [Warning] xref.section-unreadable @182: The cross-reference table /Prev names at offset 182 was read only in part: the row for object 3, at offset 251, is not an offset, a generation and n or f; ...

table holding no subsection (`xref\ntrailer\n<< /Size 10 /Root 1 0 R >>`), named by /Prev:
  [Repair] xref.offset-adjusted @182: The cross-reference section /Prev names at offset 162 was found 20 bytes after it.
  [Repair] xref.section-empty @182: The cross-reference table /Prev names at offset 182 holds no subsection: its trailer keyword follows its xref keyword. It was read as indexing nothing.

/Type /XRef stream, /W [1 4 2], /Index [0 5], 4 rows, the last of type 7, named by /Prev:
  [Repair] xref.offset-adjusted @182: The cross-reference section /Prev names at offset 162 was found 20 bytes after it.
  [Warning] xref.section-unreadable @182: The cross-reference stream /Prev names at offset 182 was read only in part: it holds 4 rows where its /Index and /Size declare 5; ...
  [Warning] xref.entry-reserved @182: The cross-reference stream /Prev names at offset 182 gives 1 row a reserved type: the row for object 3 gives it type 7. ...

the same stream named by /XRefStm 162 in the newest table's trailer:
  [Repair] xref.offset-adjusted @182: The cross-reference section /XRefStm names at offset 162 was found 20 bytes after it.
  [Warning] xref.section-unreadable @182: The cross-reference stream /XRefStm names at offset 182 was read only in part: ...
  [Warning] xref.entry-reserved @182: The cross-reference stream /XRefStm names at offset 182 gives 1 row a reserved type: ...

Code: on main 21a0298, `git show 21a0298:src/AdCodicem.Pdf/IO/PdfFileReader.cs` has no "read only in part", "holds no subsection" or "a reserved type" message. The validator's SubsectionMissingRule.cs:40 and ReservedTypeRule.cs:46 say "The cross-reference table/stream at offset {section.Offset}". The probe file was removed afterwards and the worktree is clean.
FIX (refuter): Word the subject of the three messages from section.WasRelocated, through one small helper that ReportSectionReadInPart (:3889 and :3901) and ReportReservedRows (:3931) share:
- read where named: keep "The cross-reference table/stream {naming} names at offset {section.Offset}";
- found moved: "The cross-reference table/stream {naming} names at offset {section.NamedOffset}, found at offset {section.Offset}," (or "The cross-reference stream found at offset {section.Offset}"), as SectionShiftedRule names both offsets.

Add one test per message on a section found moved. Each asserts the xref.offset-adjusted report and the following report, the shape the probe above builds. Kill the WasRelocated condition under ADR 53. This is wording only: no decision of the maintainer's is needed.

######## [15] sections:table-lost-ranges-counted-unjoined — reviewer minor; refuter narrowed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3963 (LoseRows)
CLAIM: A classic table's lost ranges are counted against MaxRefusedRanges as written, never joined. A stream's are joined first (RefuseRanges), as the constant's doc says ('once ranges that meet are joined'). A table that loses 4,097 adjacent one-row subsections, one range once joined, therefore refuses every number, while a stream declaring the same numbers keeps the older rows. The avoidable cost is the whole file rebuilt at opening when no other row is left.
EVIDENCE: Probe ZzP14, with lost subsections at 100..4196, each `N 1` and row `0 0 x`, over an oldest table indexing objects 0-3. Table, lost=4096: chain read, index has 3=True, diags `xref.section-unreadable`. Table, lost=4097: chain read=False, diags `xref.section-unreadable,xref.rebuilt`, repaired=True. Lost stream (/W [1 -4 2]) with /Index of the same 4,097 pairs: chain read=True, index has 3=True, no rebuild.
FIX (reviewer): In LoseRows, extend the last range when the new one meets it (`first <= last.End`), the subsections usually being ascending, before counting against MaxRefusedRanges.
--- VERDICT REASON: The behavior reproduces exactly on origin/ccr-6d6c0ee6-dqm4lm-188 (071f829). LoseRows (src/AdCodicem.Pdf/IO/PdfFileReader.cs:3963) appends one range for each fault and checks the 4,096 cap before any join. RefuseRanges joins meeting ranges only afterwards. Two tables that lose the same refused set, the numbers 100..4196, therefore behave differently. One that loses them as 4,097 adjacent one-row subsections goes past the bound and refuses every number. A stream that declares the same 4,097 pairs keeps them as one range. The result is a whole-file rebuild at opening that the stream path avoids. Only an invalid file reaches it, and the cost is bounded but avoidable: minor under the brief.

Narrowed in two ways.
(1) The constant's doc does not misstate the code. It says "once ranges that meet are joined" of the refused ranges, then "So many ranges a classic table's faults lose are kept at most, too". That sentence caps the table's list without promising a join. The defect is an inconsistency with the decided bound (B19-2: at most 4,096 refused ranges, those that meet joined), not a doc that is wrong.
(2) The proposed condition `first <= last.End` alone is wrong. A range lost below the last one would be absorbed into it and dropped: last (200,201), new (100,101). The join also needs `first >= last.First`.

The difference is not a design choice: the fix only makes the table's cap count what the stream's counts. No existing test covers the table's adjacent case. A_table_keeps_the_numbers_its_faults_lost_apart_up_to_4096_ranges uses ranges one number apart, and the stream has Subsections_that_meet_are_one_range.

Related, outside this finding: past the bound, the table's xref.section-unreadable message still reads "the objects the rest of that subsection numbers are looked for by rebuilding the index". In fact every number was refused. The table branch of ReportSectionReadInPart (around line 3874) has no every-number wording, while the stream's ReportUnreadable branches on _everyNumberRefused. Check whether another finding covers it.
VERDICT EVIDENCE: The probe, my own and since removed, built the following:
- Objects 1-3 and the oldest table, `xref 0 4`, trailer `/Size 4 /Root 1 0 R`.
- A newest table whose subsections are `{100+step*i} 1\n0 0 x\n` for i < lost, with trailer `/Size 30000 /Root 1 0 R /Prev oldest`.
- For the stream side, LostStreamFiles.Chain(1, rows, [], _ => 4,097 pairs (100+i, 1)).

Output on the branch head 071f829:
- table adjacent 4096: chainRead=True, has3=True, repaired=False, diags=xref.section-unreadable
- table adjacent 4097: chainRead=False, has3=(null), repaired=True, diags=xref.section-unreadable,xref.rebuilt
- table apart 4096: chainRead=True, has3=True, repaired=False
- table apart 4097: chainRead=False, repaired=True, diags=xref.section-unreadable,xref.rebuilt (the decided behavior)
- stream adjacent 4097 (rows 4 and rows 5000): chainRead=True, has3=True, repaired=False, diags=xref.section-unreadable
- stream apart 4097: chainRead=True, has3=False, repaired=True, diags=xref.section-unreadable,xref.rebuilt

The xref.section-unreadable message for "table adjacent 4097" reads "...the rows read were kept, and the objects the rest of that subsection numbers are looked for by rebuilding the index where no section read before it indexes them...", though every number was refused.

Trial fix in LoseRows, reverted afterwards:

    if (lost.Count > 0 && first >= lost[^1].First && first <= lost[^1].End) lost[^1] = (lost[^1].First, Math.Max(lost[^1].End, end)); else if (lost.Count < MaxRefusedRanges) ...

With it:
- table adjacent 4097 changes to chainRead=True, has3=True, repaired=False, diags=xref.section-unreadable.
- table apart 4097 is unchanged (rebuilt).
- CrossReferenceChainTests, ReadOrderTests and HostileInputTests all pass: 572 passed, 0 failed. This includes both 4,096-bound theories and Subsections_that_meet_are_one_range.

Not checked on main 21a0298: the finding claims no difference there, and LoseRows is new in this batch.
FIX (refuter): In LoseRows (PdfFileReader.cs:3963), join a range lost just after the last one before checking the cap:

    if (lost.Count > 0 && first >= lost[^1].First && first <= lost[^1].End) { lost[^1] = (lost[^1].First, Math.Max(lost[^1].End, end)); }
    else if (lost.Count < MaxRefusedRanges) { lost.Add((first, end)); }
    else { _rowsLostPast = true; }

The `first >= last.First` half is required. The reviewer's bare `first <= last.End` would absorb a range lost below the last one and stop refusing it.

Then:
- Extend the MaxRefusedRanges doc sentence to "So many ranges a classic table's faults lose are kept at most, too, those that meet joined as they are lost".
- Add the table counterpart of Subsections_that_meet_are_one_range: 4,097 or 5,000 adjacent one-row subsections, each ending in `0 0 x`. The oldest table's row for object 3 stands, 100 stays refused, and the file is not repaired.

Run ADR 53's mutation catalog on the new condition. Separately, give the table branch of ReportSectionReadInPart every-number wording when _rowsLostPast or _everyNumberRefused holds, unless another finding already covers that.

######## [16] headers:hdr-endobj-in-header-comment-split — reviewer major; refuter narrowed major maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:2482 (with :2441-2442, :2483, :2487); src/AdCodicem.Pdf/IO/EndObjTail.cs:14-19
CLAIM: B19-7 lets a comment sit between a header's tokens, and that comment can hold `endobj`. The search's reads are not all the same: one that comes before the read finding the header cannot yet know where the header starts. In such a read, `StreamEnds.HoldsEndObj(span, span.Length)` and `EndObjTail.Read(..., position + span.Length)` take in the bytes past the header's start, including that comment's `endobj`. Later, once the header's start lies in an earlier read, `HoldsEndObj(span, header - position)` and `tail.Read(..., until: header)` cannot take those bytes back. Two results then change: `EndObjTail` overwrites `_keywordEnd` with the comment's keyword, so `DataEnd(header)` returns -1 and the object's own `endobj` no longer ends the data; and `endObjSeen` turns true. So the data served for a stream, its report, and whether its `endobj` is judged missing all depend on where the reads split the data. That split moves with the file's layout against the 8 KB window, and with a `MaxObjectLength` the object never reaches. EndObjTail's remark is now false: "What lies between is the header's runs of digits and white space, which make no keyword". So is the threat model's "where the reads start changes nothing". On main the comment made no header, and the outcome did not depend on the window.
EVIDENCE: Probe file: objects 1 and 2; `5 0 obj << /Length 999999 >> stream` + F bytes of 'A' + `\nendobj\n` + `12 % endobj\n0 obj\n(twelve)\nendobj\n` + `6 0 obj (six) endobj`. The xref places 1, 2, 5 and 6, not 12. Each case calls GetObject(5).
Branch 071f829, default limits, F swept from 100 to 19,999:
- F=100..8130: `data F`; `Warning stream.truncated @146: The stream declared 999999 bytes, and no endstream follows its data before the next object, at F+154: its object's endobj ends it, after F bytes.`
- F=8131..8137: `data F+8`; `Warning stream.length-invalid @146: ... before the next object, at 8285; the 8139 bytes up to that object are taken as its data.` (at F=8131)
- F>=8138: stream.truncated again, as for F=100.
Branch, one file (F=6000), MaxObjectLength swept, no limit diagnostic in any run:
- default or MaxObjectLength >= 6062: `data 6000`, `stream.truncated ... its object's endobj ends it, after 6000 bytes.`
- MaxObjectLength 6055..6061: `data 6008`, `stream.length-invalid ... at 6154; the 6008 bytes up to that object are taken as its data.`
Variant without the stream's own `endobj` (F=6000):
- default: `data 6001; endobj missing True at 110`.
- MaxObjectLength 6048..6054: `data 6001; endobj missing False at 0` (Reader.IsEndObjMissing(5)).
Main 21a0298: one outcome for every MaxObjectLength from 5,900 to 6,299: `data 6034`, `stream.truncated ... after 6034 bytes`, and for the variant `data 6027; endobj missing False`.
FIX (reviewer): Do not let the bytes from a possible header's start reach EndObjTail or HoldsEndObj until a read decides whether that header is one. Option 1: HeaderText exposes where the farthest number it carries starts (its runs or lead). SearchEndStream snapshots the tail and endObjSeen at that position before reading on. When a later read finds a header that starts before it, the search goes back to the snapshot, then takes in the bytes between the snapshot and the header, which hold only digits, white space and comment lines. Option 2: EndObjTail keeps, beside the last keyword, the record as it stood at the first break after which only a header's bytes followed, and DataEnd(stop) uses the record whose keyword ends at or before stop. Either way, add a WindowEdgeTests/StreamLengthTests row, run with a sweep of MaxObjectLength and of the filler length: `12 % endobj\n0 obj` after a stream that lost its endstream gives the same data, report and endobj judgement wherever the reads split. Correct EndObjTail's remark and the threat-model row (#174/#261) to match.
--- VERDICT REASON: The finding holds. Batch 19 adds comments between a header's tokens (B19-7). In SearchEndStream (src/AdCodicem.Pdf/IO/PdfFileReader.cs:2441-2442, 2482-2483, 2487), a read that ends after the comment's `endobj` but before the header's `obj` has been decided passes the whole read to StreamEnds.HoldsEndObj and to EndObjTail.Read. The next read finds the header and caps both at its start, but it cannot take back what was already read. So the tail's `_keywordEnd` now points into the header, `DataEnd(header)` returns -1, and `endObjSeen` turns true. All three outcomes change with where the reads split: the data served, the diagnostic code, and the endobj judgement (on the reader and in the validator's object.endobj-missing rule). A MaxObjectLength the object never reaches moves that split, with no limit diagnostic. Main 21a0298 gives one outcome for every split.

I narrow the claim on one point. The threat model's "where the reads start changes nothing" refers to HeaderText's finding of the header, and that still holds: NextObject is the same offset in every run. The text that has become false is EndObjTail's remark at src/AdCodicem.Pdf/IO/EndObjTail.cs:15-19 ("What lies between is the header's runs of digits and white space, which make no keyword"). The threat-model row's own statement of the endobj rule ("the object's own endobj, with only white space and comments between it and the stop, ends the data") is correct; it is the code that breaks it inside the split band.

The band is 7 bytes per read edge: the read must end after the comment's `endobj` and no later than the header's `obj`, before the byte after it. The input has to be built for it: a stream that lost its endstream, then a header whose comment holds `endobj`. Still, by the brief's scale this is wrong data served and a wrong, split-dependent report, introduced by this batch, so it is major. No open debt covers it: #320-#328 and the open `debt` list do not, and the residuals do not mention it.
VERDICT EVIDENCE: I rebuilt the reproducer on origin/ccr-6d6c0ee6-dqm4lm-188 (071f829, Release, 0 warnings) as the uncommitted probe tests/AdCodicem.Pdf.Tests/ZzHdrSplitProbe.cs, since removed. The file is TestPdfBuilder objects 1 and 2, then object 5 = `<< /Length 999999 >>\nstream\n` + F x 'A' + `\nendobj\n12 % endobj\n0 obj\n(twelve)` (the builder adds `\nendobj\n`), then object 6 `(six)`. The xref places 1, 2, 5 and 6. Each run calls GetObject(5).

Branch, default limits, F swept from 100 to 19,999; the shape changes only at these points:
- F=100: `data 100; endobj missing False; Warning stream.truncated @152: ... before the next object, at 260: its object's endobj ends it, after 100 bytes.`
- F=8131 (through 8137): `data 8139; Warning stream.length-invalid @152: ... before the next object, at 8291; the 8139 bytes up to that object are taken as its data.`
- F=8138 onward: `data 8138; stream.truncated ... its object's endobj ends it, after 8138 bytes.`

Branch, F=6000, MaxObjectLength swept from 5,900 to 6,299, no limit diagnostic in any run:
- default, 5,900 to 6,054, and 6,062 or more: `data 6000; stream.truncated ... at 6160: its object's endobj ends it, after 6000 bytes.`
- 6,055 to 6,061: `data 6008; stream.length-invalid ... at 6160; the 6008 bytes up to that object are taken as its data.`

Variant without the stream's own endobj; the catalog names 5 as /Metadata so the validator reaches it:
- default, 5,900 to 6,047, and 6,055 or more: `data 6001; endobj missing True at 132; rule(object.endobj-missing) True`.
- 6,048 to 6,054: `data 6001; endobj missing False at 0; rule False`.

Control `12 0 obj` (no comment): `data 6000, stream.truncated` at every MaxObjectLength.

Main 21a0298, same probe, built from a `git archive` export: one shape for F=100 to 19,999 (`data F+34, stream.truncated`, because the comment makes no header there); `data 6034, stream.truncated` at every MaxObjectLength from 5,900 to 6,299; the variant gives `data 6027, endobj missing False` at every value. In every branch run the header offset (NextObject) is identical, so HeaderText's "where the reads start changes nothing" holds; only EndObjTail and HoldsEndObj depend on the split.
FIX (refuter): Stop EndObjTail and endObjSeen from depending on bytes past the start of a header that is not yet decided. The simplest bounded way follows from where a header can start: a header that a later read finds always starts at a number HeaderText carries out of an earlier read (its lead number, or a later one as in `5 12 0 obj`). So:
1. HeaderText exposes the starts of the numbers it carries at the end of a read (a few fields).
2. SearchEndStream reads the tail and HoldsEndObj in segments split at those starts. At each start it keeps a copy of the EndObjTail struct and of endObjSeen (at most as many copies as carried numbers), and drops a copy once its candidate is dropped.
3. When a read finds a header that starts before where the tail has read to, SearchEndStream restores the copy taken at that header's start, then calls DataEnd(header) and uses that copy's endObjSeen.
No byte is read twice, and nothing allocates. A record kept beside the last keyword inside EndObjTail (the reviewer's option 2) would also work, but it still needs the header's start to choose between records.

Tests: add a StreamLengthTests or WindowEdgeTests row with `\nendobj\n12 % endobj\n0 obj` after a stream that lost its endstream. Sweep the filler across the window edge and the first read edge, and MaxObjectLength across the band. Assert the same data, code, message and IsEndObjMissing / object.endobj-missing everywhere, with and without the stream's own endobj.

Rewrite EndObjTail's remark (lines 15-19): the bytes between a header's start and its `obj` may now hold comment lines, and the tail is taken as it stood at the header's start. The threat-model clause needs no change once the code is fixed. The target behavior is already decided by #174's rule and B19-7 (the default-window outcome), so this is an implementation fix, not a maintainer choice.

######## [17] headers:hdr-stream-end-lexing-cost — reviewer minor; refuter narrowed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/HeaderText.cs:112 (CarryTo lexes the line each read ends in); src/AdCodicem.Pdf/IO/LineTokens.cs:86
CLAIM: CarryTo lexes the end of every read's last line through LineTokens, whether or not that line holds a '%'. On a long line of short tokens (names, brackets, parentheses, `<<>>`, escapes), LineTokens.Pass passes nothing and Step runs once per byte. The search for a lost endstream then costs 11 to 15 ns per byte, about 60 to 85 times main's on the same bytes. The cost stays linear and allocates nothing, and EndStreamSearchPasses = 4 bounds it at four passes over the file; real content streams and binary data are unchanged. The implementation residual puts the slowdown at '~1.6-4x on pathological lines', which understates it. The scan alone is the shape the residual describes: about 4x on lines lexed at each obj. The stage-4 benchmark (ZzStreamEndBenchmarks) was not committed. No benchmark in benchmarks/ and no ReaderTimingTests case covers the stream-end search's lexing or the scan's comment walk, though the threat model claims linear cost and the commit sells the pass-by-search as a speed gain (invariant 9).
EVIDENCE: Timing probe: best of 5 runs, Stopwatch, data in a stream whose /Length runs past it, GetObject(3). Times at 2 / 4 / 8 MB, branch vs main.
- `/` x N then `\n0 obj`: branch 26.7 / 52.8 / 104.3 ms; main 0.26 / 0.54 / 1.34 ms.
- `()` x N: branch 25.4 / 51.6 / 98.5; main 0.27 / 0.54 / 1.19.
- `(` x N then `%\n0 obj`: branch 28.3 / 58.3 / 117.3; main 0.25 / 0.54 / 1.39.
- `(` + `\\` x N: branch 23.0 / 45.1 / 91.9; main 0.25 / 0.54 / 1.10.
- `<<>>` x N: branch 15.6 / 31.8 / 65.0; main 0.25 / 0.51 / 1.06.
- `%\n` x N: branch 17.4 / 34.3 / 67.7; main 0.26 / 0.54 / 1.05.
- `0 obj %\n` x N: branch 21.7 / 44.7 / 85.7; main 4.4 / 8.8 / 17.9.
- Content-stream text lines: branch 6.35 ms, main 6.50 ms at 8 MB.
- Random binary: branch 1.23 ms, main 1.15 ms at 8 MB.
Allocation: 6-7 KB in every case on both.
Scan alone (rebuild, no stream) at 8 MB, branch vs main: `0 obj %\n` 89.0 vs 22.0 ms; `12 (x) % c\n0 obj\n` 30.3 vs 7.7 ms.
Committed benchmarks, ShortRun, branch vs main: StreamLengthBenchmarks rows within noise (e.g. 4096/2/False 6.26 vs 5.68 ms, error ±12.7). TrailerScanBenchmarks 446/249/256 ms vs 486/265/243 ms.
FIX (reviewer): Commit the stage's stream-end benchmark to benchmarks/ (StreamLengthBenchmarks or a new class), with the shapes above beside text and binary. Add a ReaderTimingTests case: four times as many bytes of delimiter-only line cost about four times the search. Write the measured worst factor in the threat-model row. Optionally add fast paths in LineTokens: a run of '(' in Literal mode via IndexOfAnyExcept, and runs of one-byte tokens in Space mode, whose last two tokens are the run's last two bytes. Alternatively, lex a carried line only once it holds a '%', reading its start once more from the file.
--- VERDICT REASON: The measurements reproduce. The finding claims more than they show in two places.

What reproduces: `CarryTo` (HeaderText.cs:112, :125) lexes the line each read ends in through `LineTokens.Read` without checking for a '%'. On a long line of one-byte tokens, `Pass` returns 0 and `Step` runs once per token. The search for a lost endstream then costs about 11 to 15 ns per byte, against about 0.13 to 0.6 ns on main. That is a factor of about 20 to 110. The cost stays linear and allocates nothing more. `EndStreamSearchPasses` = 4 bounds it at four passes over the file. Text lines and binary data cost what they did. The rebuild's scan costs 4 to 6 times main's on lines that are lexed at each obj. No benchmark or `ReaderTimingTests` case in the repository covers the stream-end search's lexing, and the batch changed nothing under benchmarks/. Stage 4's `ZzStreamEndBenchmarks` was left in the scratchpad. Commit b09f981 says the line lexer passes tokens "with one search each rather than a byte at a time, so a long line costs what searching it costs". That holds for a long token, not for a long line of short tokens.

First narrowing: the cost is not avoidable in the worst case. Per byte, `LineTokens` costs about what `PdfLexer` costs on the same bytes, from 0.6 to 2.7 times. B19-7 decided to lex a line as a load lexes it, and that is the price. Lexing a carried line only once it holds a '%' leaves the worst case where it is: the file puts the '%' at the line's end, as the shape of N '(' then "%\n0 obj" already does (121 ms at 8 MB). Fast paths for runs of '(' or of one-byte tokens only send the hostile file to another shape: "1 " costs 108 ms and "/a " 59 ms. The `%\n` shape's cost comes from `Walk` passing comment-only lines, which "passes a line that holds only a comment" requires, not from `_line.Read`.

Second narrowing: the "~1.6-4x" figure appears only in the workflow notes (decisions.md, residuals-impl.md), not in the repository. The committed texts are accurate. `LineTokens`' remarks say "a line of one long token costs what searching it costs". `HeaderText`'s remarks say "what the search lexes is bounded by what it reads". The threat-model row (threat-model.md:514) says "bounded by the search's own reads". None of them claims a constant factor.

What remains is a gap in measurement and testing. Nothing guards the stream-end lexing's linearity: if `CarryTo` re-lexed a spanning line from its start at each read, the search would turn quadratic and every test would still pass. The pass-by-search, which the commit offers as what keeps a long line cheap, ships without its benchmark (invariant 9). This is minor: the cost is bounded and adds nothing for real documents.
VERDICT EVIDENCE: Rebuilt independently in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute-hdr-lex: console probes referencing the branch library (071f829) and main's (21a0298). Each run opens the file with PdfDocument.Open and calls GetObject(3) on a stream whose /Length is the data's length + 1000. Times are the best of 7 runs.

Search from memory, 2 / 4 / 8 MB, branch vs main:
- `()` x N: branch 22.0 / 50.3 / 103.4 ms; main 1.14 / 2.22 / 4.38 ms.
- `/` x N then `\n0 obj`: branch 22.9 / 45.6 / 94.4; main 1.10 / 2.13 / 4.81.
- `(` x N then `%\n0 obj`: branch 30.9 / 60.8 / 121.0; main 1.17 / 2.20 / 2.57.
- `(` + `\\` x N: branch 17.7 / 33.9 / 73.1; main 0.31 / 0.59 / 1.17.
- `<<>>` x N: branch 18.4 / 36.0 / 72.8; main 0.29 / 0.60 / 1.20.
- `[]` x N: branch 24.3 / 45.2 / 89.4; main 0.28 / 0.59 / 1.23.
- `%\n` x N: branch 20.7 / 41.1 / 84.4; main 0.33 / 0.61 / 1.16.
- `0 obj %\n` x N: branch 29.3 / 51.8 / 104.9; main 37.8 / 9.25 / 18.9.
- `1 ` x N: branch 30.5 / 63.0 / 126.2; main 0.26 / 0.56 / 1.14.
- `/a ` x N: branch 14.8 / 30.8 / 63.0; main 0.28 / 0.54 / 1.20.
- Content-stream text lines: branch 0.27 / 0.53 / 1.13; main 0.28 / 0.52 / 1.08.
- Random binary: branch 0.26 / 0.55 / 1.51; main 0.26 / 0.51 / 1.05.
- One long token (`x` x N): branch 0.57 / 1.03 / 2.12; main 0.26 / 0.53 / 1.09.
- Allocation: 6 KB in every case on both.

Search from a file, 8 MB, branch vs main: `()` 111.9 vs 5.0 ms; `1 ` 107.9 vs 1.6; `/` 102.7 vs 1.8; text 1.58 vs 1.52. Allocation 7 KB vs 6 KB.

Rebuild scan alone (startxref broken), 8 MB, branch vs main: `0 obj %\n` 88.6 vs 15.6 ms (4 MB: 44.6 vs 19.7); `12 (x) % c\n0 obj\n` 30.7 vs 8.0.

Same 8 MB on one line, `PdfLexer` vs `LineTokens.Read`:
- `()`: 38.5 vs 103.0 ms.
- `1 `: 93.7 vs 108.6.
- `/`: 95.2 vs 96.1.
- `/a `: 38.8 vs 59.5.
- `<<>>`: 70.2 vs 70.8.
- `[]`: 141.8 vs 88.9.
- Text tokens: 45.2 vs 45.4.

`git diff --stat 21a0298 HEAD -- benchmarks/` is empty. `ReaderTimingTests` has no case on the stream-end search. Stage 4's own benchmark (scratchpad stage4/ZzStreamEndBenchmarks.cs.txt, uncommitted) measured only shapes of long tokens or few lines: 1.6x to 4x against the parent.

The reviewer's numbers agree with these within machine load. The load average was about 4 during the runs.
FIX (refuter): 1. Commit a stream-end search benchmark to benchmarks/: a new `StreamEndSearchBenchmarks`, or a Shape parameter on `StreamLengthBenchmarks`. Shapes:
   - text lines and binary, as the baseline;
   - one long token;
   - a long line of one-byte tokens (`()` or `1 `);
   - a string opened and never closed, ending in `%\n0 obj`;
   - `%\n` lines.

2. Add a `ReaderTimingTests` case holding linearity: a lost endstream whose data is one line of one-byte tokens ending in a '%' costs about four times as much at four times the size. Bound it as the neighboring ratio tests are bounded. This catches a `CarryTo` that re-lexes a line spanning reads from its start.

3. In the threat-model row (threat-model.md:514), say what the cost is: about one lexer pass over the bytes searched, four passes of the file at most (`EndStreamSearchPasses`). Cite the new test.

Fast paths are optional and should not be required. A `Literal`-mode run of '(' through IndexOfAnyExcept, or runs of one-byte delimiters in Space mode, narrow a few shapes and leave the worst case where it is. So does lexing a carried line only once it holds a '%', since the file can put the '%' at the line's end. The per-byte cost is what B19-7's rule costs. Lowering it below a lexer's cost would mean revisiting that decision, which is the maintainer's call and not this fix's.

The commit message's "a long line costs what searching it costs" cannot be amended without rewriting history. The PR description should say "a long token" instead.

######## [18] headers:hdr-status-b19-7-text — reviewer minor; refuter confirmed minor maintainer=False
LOCATION: docs/status.md:2900
CLAIM: B19-7 named four texts that must now say the stream-end rule follows the scan. One of them, status.md's record of 2026-09-30, still says the opposite: "a header with a comment between its tokens is not taken, as in the rebuild's scan (maintainer, 2026-09-30)". The branch does not touch status.md (git diff 21a0298..HEAD -- docs/status.md is empty). The other three texts were updated.
EVIDENCE: `sed -n 2900p docs/status.md` at 071f829: "read four times the file (`EndStreamSearchPasses`); a header with a comment between its tokens is not taken, as in". `git diff 21a0298 HEAD --stat -- docs/status.md` prints nothing.
FIX (reviewer): In the batch's status update, amend the 2026-09-30 record. Say that batch 19 (#261, B19-7) made the stream-end rule read comments between a header's tokens as the scan now does, by maintainer decision on 2026-11-03.
--- VERDICT REASON: The claim holds as stated. B19-7 in decisions.md lists four texts that must now say the stream-end rule follows the scan: HeaderText.cs:131-133, concepts/diagnostics.md:52-53, reference/diagnostics.md:116 and status.md's record of 2026-09-30. The branch head rewrote three of them. HeaderText.cs remarks at lines 141-144 now say "comments between them, as the scan that rebuilds an index reads them (#261)". concepts/diagnostics.md:52-54 now says "Comments may lie between the tokens, as a load reads them ... as the scan that rebuilds a damaged index reads it". reference/diagnostics.md:125 now says "white space and comments between them, as a load reads them". The fourth text, docs/status.md:2900-2901, still says the opposite: "a header with a comment between its tokens is not taken, as in the rebuild's scan (maintainer, 2026-09-30)". Since e67fef9 and b09f981 that is false for both the scan and the stream-end rule. One narrowing: the branch has not run its status.md update yet. The session's own plan (task #26, "Batch-wide ADR 53, verification, status.md, PR") schedules it after the review rounds, and decisions.md keeps other items "to record in status.md at the end". So this is a decided edit that is still pending, not one the docs stage got wrong. It still has to land before the PR, because B19-7 is not fully carried out until it does. Severity is minor: a misleading text in the project journal, with no effect on code or the user site.
VERDICT EVIDENCE: On origin/ccr-6d6c0ee6-dqm4lm-188 at 071f829:
- `git diff 21a0298 HEAD --stat -- docs/status.md` prints nothing.
- `grep -n "comment between its tokens" docs/status.md` gives `2900:  read four times the file (EndStreamSearchPasses); a header with a comment between its tokens is not taken, as in`, and line 2901 continues `the rebuild's scan (maintainer, 2026-09-30).`
- At the head, src/AdCodicem.Pdf/IO/HeaderText.cs:141-144 reads "white space before each of the generation and obj, as much as the file holds, and comments between them, as the scan that rebuilds an index reads them (#261)". At main 21a0298 the same remarks read "nor tokens a comment separates. The scan that rebuilds an index refuses ... tokens a comment separates too".
- docs/website/docs/concepts/diagnostics.md:52-53 reads "Comments may lie between the tokens, as a load reads them: the line a comment ends is read from its start, as the scan that rebuilds a damaged index reads it."
- docs/website/docs/reference/diagnostics.md:125 reads "white space and comments between them, as a load reads them".
- Commit b09f981's message says "That scan now reads such headers as a load does, so the rule follows it."
- The session's task list has #26 "Batch-wide ADR 53, verification, status.md, PR" still pending.
FIX (refuter): In the batch's status.md update (task #26), amend the record of 2026-09-29/30 at docs/status.md:2900-2901 so it stays true as history and names the change, for example: "a header with a comment between its tokens was not taken, as in the rebuild's scan (maintainer, 2026-09-30); since batch 19 ([#261], maintainer, 2026-11-03) the scan and this rule both take one, as a load reads it." Also check line 1394 (#261's entry in the debt or placement list) when the batch's journal entry closes #261.

######## [19] headers:hdr-property-scan-equivalence — reviewer nit; refuter confirmed nit maintainer=False
LOCATION: tests/AdCodicem.Pdf.Tests/PropertyTests.cs:418
CLAIM: B19-7 and the threat model say the stream-end rule reads headers as the scan does. No generated-input test holds that claim. The property checks HeaderText only against itself, split reads against one read, so a lexing error both make alike would pass; only example rows tie it to the scan. LineTokens against PdfLexer (:384) is adequately strong: its pieces cover strings, escapes, hex and dictionary delimiters, names and comments, with cuts anywhere.
EVIDENCE: Probe (removed): for each `obj`, TryReadHeaderBackwards was taken through a delegate, filtered by the documented differences (a regular byte before the number, a regular byte after obj). Its first header was compared with HeaderText's first header in one read and in reads of 11-40 bytes, over 1.1 million generated texts. Pieces covered digits, \0 \t \f, CR, LF, CRLF, `%`, comments, ( ) \\ < << > >> /A [ ] { } x . obj endobj objx R and whole headers. Result: 0 differences. With '+' pieces added, it found only #327's documented case, e.g. `4\0+7   % c\nobj`: scan=118, streamEnd=-1.
FIX (reviewer): Add a property that compares HeaderText's first header with the scan's walk (TryReadHeaderBackwards made internal, or a thin internal wrapper) over HeaderLikeTexts without signs. Filter the two differences the remarks document.
--- VERDICT REASON: The gap is real, but it is only a test gap. B19-7, the HeaderText remarks (src/AdCodicem.Pdf/IO/HeaderText.cs:142-149) and threat-model.md:514 say the stream-end rule reads comments between a header's tokens "as the scan that rebuilds an index reads them". Two separate implementations are meant to agree: HeaderText/LineTokens, and PdfFileReader.TryReadHeaderBackwards (:7216). No generated-input test compares them.

The two properties at PropertyTests.cs:384 and :418 hold what their names say. :418 compares HeaderText's split reads with its own single read. :384 compares LineTokens with PdfLexer. Neither would catch a walk error that the single read and the split reads share, such as a wrong decision on a run of lines between the generation and obj.

The only ties are example tables, and each side has its own. On the rule's side: StreamLengthTests.Text_that_follows_a_lost_endstream...:483-527 and HeaderTextTests:16-44. On the scan's side: DocumentReaderTests.HeadersHoldingComments:333-349 and Tokens_a_load_takes_for_no_header...:397-405. They use different texts ("12 ..." against "4 ..."). So the reviewer's "example rows tie it to the scan" is looser than the evidence: each side is held by its own table, and no row runs both rules.

The equivalence itself holds; I could not make it fail. That is why this is a nit and not a minor: the documentation is true, B19-7 asked only for the four texts (which were written), and no behavior is wrong.
VERDICT EVIDENCE: I rebuilt the probe on origin/ccr-6d6c0ee6-dqm4lm-188 (071f829), Release build with 0 warnings, as tests/AdCodicem.Pdf.Tests/ZzScanEquivalenceProbe.cs (since removed).

How the probe works:
- It reaches TryReadHeaderBackwards through Delegate.CreateDelegate on the private method.
- The scan's first header is taken over each obj in order. It is filtered by the two documented differences: a regular byte before the number, and a regular byte after obj.
- The rule's answer is computed the way PropertyTests.FirstHeaderReadPartly does it: one read, and reads of 11-40 bytes.
- Texts are up to 16 pieces drawn from HeaderPieces (plus \f, 7, 1) and HeaderLike shapes with extra separators (\f, \0, " % (a\) %\n") and extra after-obj bytes "(" and "/".

Results:
- Without signs, seeds 1 and 2: 400,000 texts each, 86,771 and 86,680 with a header, diffs=0 (scan = one read = split reads).
- With '+'/'-' pieces and signed HeaderLike numbers, seeds 3 and 4: 16,855 differences before a sign filter. Every one is #327's documented case, for example "+007%\n00\0obj(<<" gives scan=100 one=-1 split=-1, and "12\n% c\n-0 % (a\) %\nobj\n.)obj" gives scan=100 one=-1.
- A first filter (any '+'/'-' between headerStart and obj) left 180 apparent differences, all false: the sign sat inside a comment, for example "65536% c-4\r00\fobj{", where both rules rightly take 65536 00 obj.
- Filtering only header tokens that start with a sign (lexed with PdfLexer): seeds 3 and 4, 400,000 texts each, diffs=0.

The reviewer's result (0 differences without signs, only #327 with them) reproduces.

Greps:
- No test outside HeaderTextTests and PropertyTests uses HeaderText or FindHeader.
- PropertyTests holds no comparison with the scan or a rebuild.
- TryReadHeaderBackwards is called only from src (PdfFileReader.cs:4148, :6998, :7940).
FIX (refuter): 1. Make TryReadHeaderBackwards internal, or add a thin internal wrapper (for example `internal static long FirstScanHeader(ReadOnlySpan<byte> data)`).
2. Add a property beside :418, for example "The_rule_for_object_headers_takes_the_header_the_scan_that_rebuilds_an_index_takes".
   - Generator: HeaderTexts and HeaderLikeTexts with "+" and "-" removed from HeaderPieces, and "+4" and "+0" removed from HeaderLike, so #327 stays out.
   - Assertion: FirstHeaderReadPartly(data, sizes) equals the scan's first header. The scan walks back from each obj in order and skips any header where (a) a regular byte precedes the number (#171), or (b) a regular byte follows obj. These are the two differences the HeaderText remarks list.
   - A separate negative example row can pin #327 until that debt is paid.

The probe above runs 400k cases in about 2 s, so Settings.WithMaxTest(10 * Cases) is affordable. No maintainer decision is needed: this is a test addition plus one visibility change.

######## [20] headers:hdr-validation-rules-wording — reviewer nit; refuter confirmed nit maintainer=False
LOCATION: docs/website/docs/reference/validation-rules.md:323-324
CLAIM: "that object's header is one the header rule does not take, a sign before its number say:" lacks the commas around 'say' and reads as a broken clause. "A comment between a header's tokens no longer hides it" describes history on a reference page, not what the code does.
EVIDENCE: Lines 322-324 at 071f829: "... when the object lost its own and that object's header is one / the header rule does not take, a sign before its number say: the container then runs on to that `endobj`. A comment / between a header's tokens no longer hides it, since the rule reads comments as a load does ([#261])."
FIX (reviewer): Write "... is one the header rule does not take — a sign before its number, say —: ..." and "Comments between a header's tokens do not hide it: the rule reads them as a load does ([#261])."
--- VERDICT REASON: The finding holds, and it is only about wording. Commit b09f981 (#261) rewrote lines 322-324 of docs/website/docs/reference/validation-rules.md at 071f829. The new text writes "a sign before its number say:" with no comma before "say". Everywhere else in the docs, "say" in the sense of "for example" is set off with commas, so this reads as a broken clause, or as if "say" were a verb. The new sentence "A comment between a header's tokens no longer hides it" is written against the previous text of the page, which said a comment hid the header. Someone reading this reference page has never seen that old behavior, so "no longer" describes a change instead of what the code does now. The only other "no longer" on the page (line 247) is about a condition at run time, not about the library's history. The content itself is correct. HeaderText.cs:146-149 says the rule does not take a sign before a number, and that it reads comments as a load does. No decision in decisions.md (B19-7 covers #261) sets this wording. The reviewer cited lines 322-324; the two phrases are on lines 323-324.
VERDICT EVIDENCE: At 071f829, `sed -n 322,324p docs/website/docs/reference/validation-rules.md` gives: "that `endobj` being the object's — or the next object's, when the object lost its own and that object's header is one / the header rule does not take, a sign before its number say: the container then runs on to that `endobj`. A comment / between a header's tokens no longer hides it, since the rule reads comments as a load does ([#261])." At 21a0298 the same passage read "...that object's header holds a comment, which the header rule does not take for one: the container then runs on to that `endobj`." So "no longer" is measured against the old text. How the docs write "say" elsewhere: concepts/diagnostics.md:74 "inside a string, say, an `endobj` after it or not —"; threat-model.md:514 "— a string's, say, an `endobj` after it or not —"; reference/reader-limits.md:163 "written direct or through references, say."; IPdfFileEdge.cs:10 "allows, say —". Each has a comma before "say". `grep -rn 'no longer' docs/website/docs` finds only concepts/validation.md:148, validation-rules.md:247 (a runtime condition) and validation-rules.md:324 (this line). HeaderText.cs:146-149 confirms both statements are correct: "nor a sign before a number ... The scan that rebuilds an index ... takes ... numbers a sign precedes".
FIX (refuter): In docs/website/docs/reference/validation-rules.md:323, write "...that object's header is one the header rule does not take — a sign before its number, say —: the container then runs on to that `endobj`." The smallest fix is to add the comma: "a sign before its number, say:". On lines 323-324, replace "A comment between a header's tokens no longer hides it, since the rule reads comments as a load does ([#261])." with "Comments between a header's tokens do not hide it: the rule reads them as a load does ([#261])." This is a wording fix and needs no decision from the maintainer.

######## [21] validator:xrefstm-whitespace-double-read — reviewer major; refuter confirmed major maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3247 (ReachedBefore at :3349)
CLAIM: Two tables can name one hybrid stream, one at its header and one in the white space just before it (a different written offset). When the newer table names the header and the older one names the white space, the stream is read a second time and gets a second Read record. Every report about that stream then appears twice, and whether it does depends on which of the two tables names the header. Doubled: the reader's xref.entry-reserved, xref.entry-type-reserved, and file.size-wrong at both 391 and 389. A lost stream doubles xref.section-unreadable and xref.section-malformed. This breaks ReservedTypeRule's remark ("each section is reported once"), SizeWrongRule's ("its own /Size is judged once, where the oldest of them names it") and Q9's single notion of "already read". It extends stage 5's unfiled residual (white space before a section already read, which /Prev reads again) and stage 6's (a lost stream's position is not counted as read) to /XRefStm. On /XRefStm nothing catches the repeat a step later, so the reports stay doubled.
EVIDENCE: Probe ZzSharedShortOlder / ZzSharedShortOlderSize / ZzSharedShortLost (source saved at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/vallens/ZzValLens.cs.txt). Shape: stream 5 at 391 after two spaces, /Index [6 1 8 1] (row for 8 of type 3). Older table /XRefStm 389, newest /XRefStm 391. Branch output: "Warning xref.entry-reserved at 391: ..." and "Warning xref.entry-reserved at 389: ...". Findings: "Warning file.size-wrong at offset 391: The cross-reference stream at offset 391 gives /Size 12 ...", "Warning file.size-wrong at offset 389: The cross-reference stream at offset 389 gives /Size 12 ...", "Warning xref.entry-type-reserved at offset 391 ...", "Warning xref.entry-type-reserved at offset 389 ...". Records: "/XRefStm named 391 offset 391 pad 0 state Read reserved 1" and "/XRefStm named 389 offset 389 pad 2 state Read reserved 1". With the namings swapped (newer 389, older 391), each report appears once and the second record is NamedAgain. Lost variant (/W [1 -4 2]): "Error xref.section-malformed at offset 391 ..." and "Error xref.section-malformed at offset 389 ...", plus xref.section-unreadable at 391 and at 389. On main 21a0298, file.size-wrong is doubled in both orders.
FIX (reviewer): Make ReachedBefore look past the white space and comments at the named position, as TryReadXRefSection's probe already does, and test the first token's start against _sectionsReadAt, so the second naming is recorded NamedAgain with ReadAs taken from sectionsAt. Also record the start of a lost /Type /XRef stream (its header) as reached, so that naming it again does not read it again. Add tests for SharedStream with one naming two bytes short, in either order and for a lost stream, asserting one read record and one report of each kind.
--- VERDICT REASON: I rebuilt the reproducer and it holds on origin/ccr-6d6c0ee6-dqm4lm-188 (071f829). One part is wider than the claim says.

**Why it happens.** `ReachedBefore` (src/AdCodicem.Pdf/IO/PdfFileReader.cs:3349) checks two things: the written offset in `named`, and `PositionOf(offset)` in `_sectionsReadAt`. `RecordSectionRead` (:3360) puts only the header start (`Offset + Padding`) in that set. So it depends on which naming comes first:
- **White space named first, header named later:** the later naming matches, and its record is NamedAgain. This is the only order the batch tests (`CrossReferenceChainTests.A_stream_named_short_of_its_header_and_named_at_it_again_is_read_once`).
- **Header named first, white space named later:** the later naming misses. The /XRefStm branch at :3265 reads the stream a second time and writes a second Read record. Nothing later catches it, because no /Prev follows an /XRefStm stream.

**Effect on reports.** `ReservedTypeRule`'s remark ("each section ... reported once") and `SizeWrongRule`'s ("judged once, where the oldest of them names it"; `judgedAt` keys on the record itself) no longer hold. #156's closing criterion ("many tables naming one stream still read it once") holds only when every table writes the same offset.

**Wider than the claim.**
- A lost stream (`/W [1 -4 2]`) is doubled in both orders, not just the one probed. Nothing puts its header in `_sectionsReadAt`, and `sectionsAt` holds it only under its written offset.
- Any naming of the white space that is not the header itself is read again. With N such namings, the stream is read N times. Default limits allow this (MaxXRefSectionCount 1,024, MaxXRefSectionLength 64 MB), so the cost grows as tables × stream size. That is the 700 MB that `HostileInputTests.A_thousand_tables_naming_one_stream_read_it_once` says it prevents.

**Main.** On main 21a0298 the stream named twice is read twice in both orders: file.size-wrong appears twice either way, the 1,000-table read amplification is the same, and a lost stream was relocated onto the table instead. So the double read is older than the branch. What the branch adds:
- it now depends on which table names the header;
- the two new reports, xref.entry-reserved and xref.entry-type-reserved, come out twice;
- the lost-stream reports come out twice.

**Severity: major.** Reports come out doubled and their count depends on which table names the header. Q9's single notion of "already read" and #156's "read once" are not fully implemented. Hostile input can multiply the reads about 1,000 times under the default limits. The shape is rare in real files: a writer that copies /XRefStm into the next trailer copies the same offset.

**Filed?** No open debt covers this. Stage 5 (the /Prev case) and stage 6 (a lost stream's position not counted as read) left their residuals unfiled.
VERDICT EVIDENCE: Probe: a file of the claim's shape, built by HandWrittenFile (the uncommitted probe files have been deleted). The stream header is at 414 with two spaces before it, `/Index [6 1 8 1]` with a type-3 row for object 8, and the tables name 414 and 412. Outputs are in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute-xrefstm-ws/branch and .../main.

**Branch: older table names 412, newer names 414.**
- Reader: "Warning xref.entry-reserved at 414: ..." and "Warning xref.entry-reserved at 412: ...".
- Findings: "Warning xref.entry-type-reserved at offset 414 ..." and "... at offset 412 ...".
- Records: "/XRefStm named 414 offset 414 pad 0 state Read" and "/XRefStm named 412 offset 412 pad 2 state Read".
- With /Size 12, also "Warning file.size-wrong at offset 414: The cross-reference stream at offset 414 gives /Size 12, where ... makes it 9." and "Warning file.size-wrong at offset 412: ...".
- The same doubling happens with no reserved row (type-1 row).

**Branch: swapped (newer 412, older 414).**
- One report of each kind.
- The second record reads "/XRefStm named 414 offset 414 pad 0 state NamedAgain readAs 412".

**Branch: lost stream (`/W [1 -4 2]`), in both orders.**
- Reader: "Warning xref.section-unreadable at 414: The cross-reference stream /XRefStm names at offset 414 is there but cannot be read: its /W gives a field a negative width. ..." and the same at 412.
- Findings: "Error xref.section-malformed at offset 414 ..." and "... at offset 412 ...".
- Records: two Malformed records.

**Branch: both tables name 414.** One file.size-wrong, and a NamedAgain record with readAs 414.

**Main 21a0298.**
- The Read/Read records and the doubled file.size-wrong appear in both orders.
- Lost variant: "/XRefStm named 412 offset 527 pad 0 state Relocated" with xref.section-shifted (the #188 bug).

**Cost probe.** 1,000 tables chained by /Prev name a 100,000-row (700 KB) stream. In the shifted file, table k names `stream - k`, inside 1,000 spaces before the stream.

| Run | Bytes read | /XRefStm Read records | Other | Time |
|---|---|---|---|---|
| Branch, every table names the header | 50,486,636 | 1 | 999 NamedAgain | 141 ms |
| Branch, shifted | 816,242,576 | 999 | 1 | 1,822 ms |
| Main, shifted | 817,008,208 | 1,000 | 0 | 1,651 ms |

No run reported any diagnostic. Line numbers on the branch head: the NamedAgain branch is at :3247, the re-read at :3265–3294, `ReachedBefore` at :3349, `RecordSectionRead` at :3360.
FIX (refuter): 1. **Check where a naming lands, not only where it points.** Factor the first-token probe of `TryReadXRefSection` (:4194–4208) into a helper, `StartOf(position)`. It returns `position + first.Start`, growing the window the way the probe does, up to the trailer bound. Use it in `ReachedBefore`: when `named.Contains(offset)` and `_sectionsReadAt.Contains(PositionOf(offset))` both miss and the position is in the file, test `_sectionsReadAt.Contains(StartOf(PositionOf(offset)))`. In the /XRefStm branch, look up `ReadAs` with `sectionsAt.GetValueOrDefault(that start)`. A naming of the white space then records NamedAgain in either order. This also fixes stage 5's /Prev residual: the loop is caught at the first repeat. Cost: one small extra window for each naming that misses.

2. **Count a lost stream's position as reached.** For a lost /XRefStm or /Prev stream that declares itself a section (`DeclaresItselfASection`, `Padding` known), add `Offset + Padding` to `_sectionsReadAt` and `sectionsAt`. A naming at another offset then becomes NamedAgain with `ReadAs` set to the Malformed record, as a naming at the same offset already does. Relocation passing over that position is harmless, since nothing there can be read. This does not need a maintainer decision: Q9 already decided on one notion of "already read", and `ReachedBefore`'s own doc already counts an offset named whether or not a section was read there.

3. **Tests.**
   - In `HybridFiles`, add the mirror of `SharedStreamNamedShort`: the newest table names the header, the update names two bytes short. Assert one Read record, NamedAgain `ReadAs` the first, and one entry-reserved, one entry-type-reserved and one file.size-wrong.
   - The lost variant in both orders: one section-unreadable and one section-malformed.
   - A `HostileInputTests` variant of `A_thousand_tables_naming_one_stream_read_it_once` where table k names `stream - k` inside white space: bytes read equal to the same-offset file, 999 NamedAgain records.
   - Correct that test's comment, which claims the guarantee for every naming.

######## [22] validator:reserved-rule-as-the-reader-does — reviewer minor; refuter narrowed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/Validation/Rules/ReservedTypeRule.cs:46
CLAIM: The xref.entry-type-reserved message ends "reads any other as a reference to the null object (7.5.8.3), as the reader does". That is false once the index has been rebuilt. The rebuild can come at opening, or from the validator's own SettleIndex, which rebuilds whenever the index is incomplete. After it, the object reads as whatever the scan finds, which is the older revision's definition when the file writes no newer one. So the finding contradicts what the reader serves.
EVIDENCE: ReservedRowFiles.Updated(type: 3, more: 2, missing: 1). Before validation, object 3 is null. Validation reports "Warning xref.entry-type-reserved at offset 381: ... reads any other as a reference to the null object (7.5.8.3), as the reader does." and the diagnostics add "Repair xref.rebuilt". Probe output: "object 3 before validation (other doc) = null; after = \"three\"". ReservedRowFiles.Updated(type: 3, newer: true, prevShift: 700) (the /Prev lies outside the file, so it is rebuilt at opening) gives the same finding text, with "object 3 = \"new three\"". On main there is no such finding.
FIX (reviewer): Write "as the reader does" only when !context.Document.WasRepaired. Otherwise say the reader read the row so until it rebuilt the index, which finds the object by scanning. Or drop the clause. Add a test with an incomplete index (rows short) that asserts the text.
--- VERDICT REASON: The finding holds. I rebuilt it on origin/ccr-6d6c0ee6-dqm4lm-188 (071f829). Validation runs PdfFileReader.SettleIndex before the first rule. SettleIndex calls Repair whenever `_indexIncomplete` is set (PdfFileReader.cs:1110), and a stream read in part sets it. Repair goes through Rebuild, which runs `_xref.Clear()` and scans the file, and it keeps the chain's index only when a guard cut the chain (`Rebuild(_cutByGuard ? _chainIndex : null)`). So a reserved row's null entry does not survive a rebuild. The same happens when the rebuild runs at opening, for example when the /Prev lies outside the file. After either rebuild, ReservedTypeRule.cs:46 still ends "reads any other as a reference to the null object (7.5.8.3), as the reader does", but the reader now serves the scanned definition. The narrowing: the contradiction only shows for objects whose definition the scan finds. Objects 7 and up have no definition anywhere and still read as null after the rebuild, so for them the clause stays true. A section read whole with no rebuild (the plain and hybrid shapes) gives a true message. Severity is minor because the report itself is right (the row is reserved, the code, severity and position are correct) and only its closing clause about the reader is wrong for a repaired document. This rule is new in this batch (01426c1, #221), so main 21a0298 has no such finding. That makes the defect this batch's, and residuals-impl.md does not list it. The existing test A_section_read_in_part_reports_its_rows_of_a_reserved_type_after_what_it_lost uses this exact fixture. It checks that object 3 is null before validation and checks only the middle of the validator message with `Contains`, so it passes with the wrong clause in place.
VERDICT EVIDENCE: Probe on the branch head (uncommitted Zz test, since removed). Each file is opened once without validation and once with validation.

`ReservedRowFiles.Updated(type: 3, more: 2, missing: 1)`:
[missing1] other doc: object 3 without validation = null; WasRepaired=False
[missing1]   diag before: Warning xref.section-unreadable @381: ... was read only in part: it holds 4 rows where its /Index and /Size declare 5; ...
[missing1]   diag before: Warning xref.entry-reserved @381: ... gives 3 rows a reserved type: ...: the rows were read so, and no older section's row stands for their objects.
[missing1]   finding: Warning xref.entry-type-reserved @381: The cross-reference stream at offset 381 gives 3 rows a reserved type: the row for object 3 gives it type 3, the first of them. ISO 32000-1 defines types 0, 1 and 2, and reads any other as a reference to the null object (7.5.8.3), as the reader does.
[missing1]   diag after: Repair xref.rebuilt @-1: The cross-reference index was rebuilt by scanning the file.
[missing1] after validation: WasRepaired=True; object 3 = "three"
[missing1] object 7 = null

`Updated(type: 3, more: 1, missing: 2)` (the fixture of the existing test) gives the same text, then xref.rebuilt, then object 3 = "three".

`Updated(type: 3, newer: true, prevShift: 700)`: rebuilt at opening (xref.section-missing at offset 926, outside a 562-byte file, then xref.rebuilt and object.redefined). The finding text is the same ("... reads any other as a reference to the null object (7.5.8.3), as the reader does."), and object 3 = "new three" both before and after validation.

`Updated(type: 3, prevShift: 700)`: same finding text, object 3 = "three".

Controls that hold the claim to rebuilt documents only: `Updated(type: 3, more: 2)` (plain) and `Hybrid()` have WasRepaired=False and object 3 = null after validation, so the text is true there. `git show 21a0298:src/AdCodicem.Pdf/Validation/Rules/ReservedTypeRule.cs` reports the path does not exist in 21a0298: the rule is new in 01426c1.
FIX (refuter): In ReservedTypeRule.Check, choose the closing clause from `context.Document.WasRepaired`. It is final once SettleIndex has run before the first rule, so the text does not depend on read order. The current clause stays when no rebuild ran. Rebuilt case, for example: "... and reads any other as a reference to the null object (7.5.8.3): the reader read the rows so, then rebuilt the index by scanning the file, which reads each of their objects as the scan finds it." The simpler alternative is to drop ", as the reader does". Either is a wording choice, not a design decision. In A_section_read_in_part_reports_its_rows_of_a_reserved_type_after_what_it_lost, assert the full validator message, then assert that object 3 reads "three" after validation. Add one opening-rebuild case, `Updated(type: 3, newer: true, prevShift: 700)`, that asserts the rebuilt wording. Optionally, have the `xref.entry-type-reserved` row of docs/website/docs/reference/validation-rules.md:55 ("the reader reads the row so, its object indexed as a free one's is and read as null") add "until a rebuild, which reads the object as the scan finds it", and adjust the rule's XML remarks the same way.

######## [23] validator:read-in-part-rows-held-count — reviewer minor; refuter narrowed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3872
CLAIM: The xref.section-unreadable message added by #210 for a stream read only in part does not follow #241's rule (new messages agree verb and pronoun with their count). With one row held it says "it holds 1 row ...; the rows it holds were kept". With none held it says the rows it holds "were kept", though nothing was kept.
EVIDENCE: PartialFiles.StreamOverOlderRows(rows: 1): "Warning xref.section-unreadable at 403: The cross-reference stream startxref names at offset 403 was read only in part: it holds 1 row where its /Index and /Size declare 3; the rows it holds were kept, and the objects whose rows it lacks are looked for ...". rows: 0: "... it holds 0 rows where its /Index and /Size declare 3; the rows it holds were kept, ...". The /XRefStm-named variants (hybrid: true) give the same text.
FIX (reviewer): Keep the number of rows held on the record, or pass it, and write "the row it holds was kept" for 1. For 0, drop the clause, or say "it holds none". Add count-of-one and count-of-zero tests next to the existing #210 message tests.
--- VERDICT REASON: The count-of-one part holds. The suffix added by #210 (54d56c6, PdfFileReader.cs:3872) follows the fault's "it holds 1 row" with "the rows it holds were kept". That breaks the rule this batch decided for #241 ("verb/pronoun agree ... new messages use it"). Commit 954cef7 states the same rule ("A verb or a pronoun that follows a count agrees with it too"), and so does docs/website/docs/reference/diagnostics.md:38-41, whose example is "the 1 byte decoded before the fault was found was kept". The same batch applies the rule to the parallel RunLength message ("of which it holds 1; it was kept"), to the Flate messages and to the reserved-row message ("the row was read so ... its object"). This new message misses it, so the reference page states a rule the code does not hold at this site.

The count-of-zero part is narrowed out. #241's rule makes the noun "singular for one alone", so "0 rows ... the rows it holds were kept" agrees. It is vacuous, not false, and the project's own Flate message writes "the {0 bytes} decoded before the fault was found were kept" the same way. At most it is a wording nit, not a breach of the rule.

Main 21a0298 gives no report at all for this section, since the message is new with #210, so there is no difference from main to judge. No test holds the message with one row. DocumentReaderTests.cs:2424 checks only the record's Fault, and CrossReferenceChainTests.cs:1903 uses rows: 2. Severity stays minor rather than nit because the decided rule and the reference documentation are not followed in a message this batch adds. The meaning is not misleading.
VERDICT EVIDENCE: I rebuilt the reproducer on origin/ccr-6d6c0ee6-dqm4lm-188 (071f829) with an uncommitted probe calling PartialFiles.StreamOverOlderRows(rows: n, hybrid: h) and PdfDocument.Open, Release build:
rows=1 hybrid=False: Warning xref.section-unreadable at 403: The cross-reference stream startxref names at offset 403 was read only in part: it holds 1 row where its /Index and /Size declare 3; the rows it holds were kept, and the objects whose rows it lacks are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows.
rows=1 hybrid=True: the same text with "/XRefStm names".
rows=0 hybrid=False/True: "... it holds 0 rows where its /Index and /Size declare 3; the rows it holds were kept, ..." (ChainIndex.TryGet(4) is False: nothing was kept).
rows=2: "... it holds 2 rows ...; the rows it holds were kept ..." (the existing test's text).
Code: the suffix at src/AdCodicem.Pdf/IO/PdfFileReader.cs:3872 is a fixed plural, while the count is formatted separately at :5223 (`{held:N0} {(held == 1 ? "row" : "rows")}`) into section.Fault, a string. ReportSectionReadInPart cannot see held. The rule is at docs/website/docs/reference/diagnostics.md:40-41. The precedents are PdfFilterPipeline.cs:678 and :694, and the RunLength test text "of which it holds 1; it was kept".
FIX (refuter): Make the clause after the count agree with it. There are two equivalent fixes, neither a maintainer decision:

(a) Keep the held count on XRefSectionRecord (for example `RowsHeld`, set beside section.Fault at PdfFileReader.cs:5220-5223). In ReportSectionReadInPart, write "the row it holds was kept" when it is 1 and "the rows it holds were kept" otherwise.

(b) Reword the stream branch so it agrees with any count without new state, for example "; each row it holds was kept, and the objects whose rows it lacks are looked for ...".

Optionally, for 0 rows, drop the kept clause or write "it holds none". This is a nit and consistent with the Flate precedent, so it is not required.

Add a count-of-one test beside CrossReferenceChainTests' #210 theory, StreamOverOlderRows(rows: 1, hybrid) for both namings, that asserts the full message. Update the rows: 2 expected text if (b) is chosen.

######## [24] validator:hybrid-same-offset-nothing-there — reviewer minor; refuter confirmed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3245 (text carried into HybridStreamWrongRule.cs:43)
CLAIM: Branch (c) words what an /XRefStm names when its table's /Prev names the same offset, and it does so before the /Prev is read. It always says "the section its /Prev names too, which the chain reads through that /Prev". When that offset is outside the file, or holds nothing, there is no section and the chain reads nothing there. The next diagnostic, for the same offset, says so (xref.section-missing / xref.section-not-found).
EVIDENCE: StoppedSectionFiles.Stopped("/Prev 99999999 /XRefStm 99999999"), read whole: "Warning xref.hybrid-stream-lost at 483: The /XRefStm of the cross-reference table at offset 483 names offset 99999999, the section its /Prev names too, which the chain reads through that /Prev: ..." then "Warning xref.section-missing at 483: The cross-reference section /Prev names at offset 99999999 lies outside the file, which is 1,462 bytes long ...". Findings: "Error xref.section-not-found at offset 483: ... the offset lies outside the file." and "Error xref.hybrid-stream-wrong at offset 483: ... names offset 99999999, the section its /Prev names too, which the chain reads through that /Prev, where ISO 32000-1 names ...". The table-rows guard gives the same two texts.
FIX (reviewer): Word it neutrally ("what its /Prev names too, which the chain looks for through that /Prev"), or word it by what the /Prev's record became. ReadAs is set when that record is made, so the rule can read ReadAs.State, and the reader can write the text once the /Prev's section has been tried ("an offset its /Prev names too, where no section is" or "outside the file").
--- VERDICT REASON: The finding holds. I rebuilt it on origin/ccr-6d6c0ee6-dqm4lm-188 (071f829). Branch (c) at src/AdCodicem.Pdf/IO/PdfFileReader.cs:3238-3246 picks its text before the /Prev's section is tried. Outside a guard, it always writes "the section its /Prev names too, which the chain reads through that /Prev". The record's ReadAs is filled only later (line 3153). The same text goes into NamesNoStream, and HybridStreamWrongRule.cs:43 repeats it. So when the shared offset is outside the file, or is a place in the file that holds no section, both reports say there is a section and that the chain reads it. The next report, on the same offset, says there is nothing there.

Exact scope: the false text appears whenever no guard stopped the table and the offset holds no section, whether outside the file or a hole in it. Past a guard it appears only when the offset is outside the file. When the offset is in the file past a guard, the text is the guard's own ("past the guard that stopped the table"), which is acceptable because the guard hides what is there.

The project's own wording rule backs the finding. The guard variant exists because "reads" was false where the /Prev reads nothing (CrossReferenceRuleTests: "which says the /Prev reads that table, where past the guard it reads nothing"). The test HeaderOffsetTests.A_table_naming_one_offset_outside_the_file_through_prev_and_xrefstm_is_reported_at_the_table says in its comment "the /Prev names no section", yet it asserts the message "the section its /Prev names too". NamesNoStream's XML doc (XRefSectionRecord.cs:186-190) also says it is null for "what the chain could not read", which this case contradicts.

The classification is not in question: hybrid-stream-lost and hybrid-stream-wrong are deliberate for this shape (B19-5, the CrossReferenceRuleTests rows "/Prev 99999999 /XRefStm 99999999"). Only the text is wrong, so the severity is minor (misleading text). The finding claims no difference from main, so no main build was needed. The text is new in 7d35896.
VERDICT EVIDENCE: The probe was StoppedSectionFiles.Stopped(links, newer, twentyOne: "(" + 1500 x + ")"), read whole and validated. It was uncommitted and has been removed.

1) links "/Prev 99999999 /XRefStm 99999999", no newer section, no guard:
D Warning xref.hybrid-stream-lost at 2002: The /XRefStm of the cross-reference table at offset 2002 names offset 99999999, the section its /Prev names too, which the chain reads through that /Prev: ISO 32000-1 has it name the cross-reference stream of the table's revision (Table 19). ...
D Warning xref.section-missing at 2002: The cross-reference section /Prev names at offset 99999999 lies outside the file, which is 2,982 bytes long; ...
S /XRefStm 99999999 from 2002 state NamedAgain readAs NotFound
S /Prev 99999999 from 2002 state NotFound
F Error xref.section-not-found at offset 2002: The cross-reference section /Prev names at offset 99999999 is not there, nor within 512 bytes of it: the offset lies outside the file.
F Error xref.hybrid-stream-wrong at offset 2002: ... names offset 99999999, the section its /Prev names too, which the chain reads through that /Prev, where ISO 32000-1 names ...

2) The same file with a newer /Prev section, under "table rows" and under "table index": the same two texts (the out-of-file case takes the read-whole text, because IsInFile is false).

3) An offset in the file that holds nothing (1183, inside a 1,500-byte string, more than 512 bytes from any header), no guard:
D Warning xref.hybrid-stream-lost at 1183: ... names offset 1183, the section its /Prev names too, which the chain reads through that /Prev: ...
D Warning xref.section-missing at 1183: The cross-reference section /Prev names at offset 1183 is not there, nor near it; ...
F Error xref.section-not-found at offset 1183: ... the offset holds neither the xref keyword nor an object.
F Error xref.hybrid-stream-wrong at offset 2002: ... names offset 1183, the section its /Prev names too, which the chain reads through that /Prev, ...

4) The same hole past "table rows": "..., the section its /Prev names too, past the guard that stopped the table". ReadAs is null and nothing is read, so the guard's text is used.

5) Control, "/Prev {older} /XRefStm {older}": readAs Read, and the text is true.
FIX (refuter): Choose the text by what is known, and stop asserting a section that may not exist.

In the reader, branch (c) (PdfFileReader.cs:3241-3245):
- Offset outside the file: this is known at report time, as the guard text already checks IsInFile. Write e.g. "an offset outside the file, which its /Prev names too".
- Otherwise: write a neutral text, e.g. "what its /Prev names too, which the chain looks for through that /Prev". The diagnostic order stays as it is.

In HybridStreamWrongRule, word the finding from the record's ReadAs, which is filled once the /Prev's record is made:
- ReadAs.State Read or Relocated: "the section its /Prev names too, which the chain read through that /Prev".
- NotFound or any other state: "what its /Prev names too, where the chain found no section", or "outside the file".
- ReadAs null (past a guard): keep the guard text.

Then update:
- NamesNoStream's XML doc (XRefSectionRecord.cs:186-190), which says it is null for what the chain could not read.
- reference/diagnostics.md:115 and validation-rules.md:60 ("which the chain reads through that /Prev").
- The expected texts in HeaderOffsetTests:141, CrossReferenceChainTests:2300/2450, CrossReferenceRuleTests:915/989 and ReaderLimitsTests:2494-2495.
- Add an assertion on the out-of-file text and the hole text, which no test holds today.

This is wording within the decision already taken (B19-5), not a design choice.

######## [25] validator:xrefstm-read-before-prev-loop — reviewer minor; refuter narrowed minor maintainer=True
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3074 (loop check) and src/AdCodicem.Pdf/Validation/Rules/SizeWrongRule.cs:169 (IsWhole)
CLAIM: #156's rule covers an /XRefStm that names a stream the chain reads through /Prev only when the /Prev comes first. In the mirror order the newest table T2's /XRefStm names a whole-revision stream S, and the older table T1's /Prev names the same S. S is then read as T2's hybrid stream. T1's /Prev is called a loop (an Error placed at T1), and T1's correct /Size gets a false file.size-wrong, because IsWhole ignores a loop. xref.hybrid-stream-wrong never fires for T2, whose /XRefStm is the actual fault. Until validation, object 4 is served from S, the older revision. Main 21a0298 gives the same output. This is not #321, which is about a hybrid stream copied late.
EVIDENCE: Probe ZzStreamNamedByLaterPrev. Branch: "Warning xref.chain-cycle at 229: ... the /Prev of the section at offset 410 names offset 229, which the chain has already read." Findings: "Warning file.size-wrong at offset 463: The trailer of the cross-reference table at offset 410 gives /Size 6, where the highest object number it and the sections it updates use, 4, makes it 5." and "Error xref.chain-loop at offset 410: ...". "object 4 at open = \"old four\"", and after validation "new four". Main: the same three lines.
FIX (reviewer): File as debt beside #321. When a /Prev reaches a position where the chain read a section through an /XRefStm (an IsHybridStream record), report that /XRefStm (xref.hybrid-stream-lost / -wrong, naming the /Prev that names the stream) rather than a loop. In SizeWrongRule.IsWhole, return false when structure.LoopOffset >= 0, since a loop leaves the section the /Prev should have named unknown, as ChainLoopRule's remarks say.
--- VERDICT REASON: The reviewer's output reproduces byte for byte, and on main 21a0298 too. The newest table T2's /XRefStm names S, a cross-reference stream for a whole revision, and the older table T1's /Prev also names S. The chain reads S as T2's hybrid stream. It then reports T1's correct /Prev as a loop: Warning xref.chain-cycle and Error xref.chain-loop at T1. The fix text, "Point the /Prev at the section before it", is wrong advice here. Nothing reports T2's /XRefStm, which is the real fault. S lies before T1 and T1's /Prev names it, so S cannot be a stream of T2's revision. This is the same misplaced blame that branch (c) of B19-5 removed for the one-table version (shape C2). The cross-table version was never measured.

It is not a regression and not a decision left unimplemented. It is documented behavior: diagnostics.md:114 says a /Prev naming an offset the chain reached "through a /Prev or an /XRefStm" is xref.chain-cycle. Q9 settled one meaning of "already read" for the loop check. The batch deliberately kept the same two-table pattern as a loop when the offset holds no section (CrossReferenceRuleTests.A_loop_through_an_offset_the_chain_only_named_says_no_section_was_read_there). The pattern is related to #321 but distinct: #321's detection follows /XRefStm namings recorded as NamedAgain, while here the second naming is a /Prev and goes through the loop branch.

Two points of the claim are narrowed.

(1) The served object is not a separate defect. "old four" at open is the reading ISO 32000-1 7.5.8.4's order gives: T2's /XRefStm rows are read before T1. A hybrid-aware reader gives the same, and #321 accepts it for its sibling shape. The proposed fix does not change it. The switch to "new four" after validation is the validator rebuilding an incomplete index, which happens for every loop.

(2) The false file.size-wrong is wider than the mirror order. Any /Prev loop gets it when the looping table's /Size counts objects of the section the chain should have gone on to. My plain two-table loop gets the same false Warning, on the branch and on main. It contradicts three things: SizeWrongRule's own remarks (not judged when the chain lost a section), ChainLoopRule's remarks (that section is unknown), and PageTreePageOrphanedRule, which skips when LoopOffset >= 0.

Severity is minor: the shape is hand-built, no corpus document has it, it predates the batch, and the file is still flagged with an Error, only on the wrong key.
VERDICT EVIDENCE: The probe is /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refxrefstm/ZzRefXRefStmPrev.cs.txt, the reviewer's file rebuilt. S at 229 is /Type /XRef /Size 6 for objects 0-5, with object 4 "(old four)". T1 at 410 indexes 4 "(new four)", /Size 6 /Prev 229. T2 indexes 6, /Size 7 /Prev 410 /XRefStm 229.

Branch 071f829, output before validation, the findings, then the diagnostics after validation:
```
object 4 at open (fresh doc) = "old four"
Warning xref.chain-cycle at 229: The cross-reference chain loops back on itself: the /Prev of the section at offset 410 names offset 229, which the chain has already read.
Warning file.size-wrong at offset 463: The trailer of the cross-reference table at offset 410 gives /Size 6, where the highest object number it and the sections it updates use, 4, makes it 5.
Error xref.chain-loop at offset 410: The /Prev of the cross-reference section at offset 410 names offset 229, a section the chain has already read: the chain loops.
Repair xref.rebuilt: ...
Information object.redefined: ...
object 4 after validation = "new four"
```
Main 21a0298 (scratch worktree) gives the identical lines (scratchpad/refxrefstm/main/mirror.txt).

Two controls:
- Without T2's /XRefStm: no diagnostic, no finding, object 4 = "new four".
- The forward order (HybridFiles.NamingThePreviousStream / NamingANewerStream) correctly gets xref.hybrid-stream-lost and an Error xref.hybrid-stream-wrong at the naming table.

The size-wrong part is general. In a plain /Prev loop, T0 indexes objects 1-5 and T1 (460) updates object 4 with /Size 6, but T1's /Prev names T2 (605) instead of T0. Branch and main both give:
```
Warning file.size-wrong at offset 513: The trailer of the cross-reference table at offset 460 gives /Size 6, where the highest object number it and the sections it updates use, 4, makes it 5.
Error xref.chain-loop at offset 460: ...
```

I tried adding `structure.LoopOffset >= 0` to SizeWrongRule.cs:46. That removes the false Warning in both probes. The whole unit project then ran 7,195 tests: 0 failed, 20 skipped (remote corpus). I reverted the trial afterwards.

Code locations: the loop check is PdfFileReader.cs:3087, `if (ReachedBefore(offset, visited))`, not 3074. IsWhole is at SizeWrongRule.cs:169, and it never consults LoopOffset.
FIX (refuter): There are two parts, and only the first needs no decision.

(a) Pay it in this batch: SizeWrongRule has no design choice. Add `|| structure.LoopOffset >= 0` to the guard at SizeWrongRule.cs:46, or have IsWhole take the structure. Extend the remark: "or that ends in a loop, which leaves the section the last /Prev should have named unknown". Add a FileRuleTests or CrossReferenceRuleTests case: a plain two-table /Prev loop whose looping table's correct /Size counts the unread section's objects gets no file.size-wrong. Update validation-rules.md:47 to match. This is #156's own area (file.size-wrong judging a table without what it updates), and no existing test changes.

(b) File as debt under M02, beside #321 and linked to it; the maintainer must decide. The question is what happens when a /Prev names a position where the chain read a section only through another table's /XRefStm (a record with IsHybridStream set). One option is to report that /XRefStm as xref.hybrid-stream-lost / xref.hybrid-stream-wrong, saying a /Prev names the stream too, and mark the index incomplete, with no loop. The other is to keep it a loop, as Q9 and diagnostics.md:114 say now. Choosing the first refines Q9's single notion of "already read". It must keep the offset-only-named two-table loop test as a loop. It also leaves object 4 at open as the stream's row, as #321 accepts for its sibling shape. The debt should carry the reproduction above and the forward-order controls.

######## [26] validator:xrefstm-names-malformed-table — reviewer minor; refuter confirmed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3294
CLAIM: When an /XRefStm names a classic table that cannot be read, only that table's own fault is reported. The /XRefStm names a table where ISO 32000-1 Table 19 asks for a stream, whatever that table holds, and nothing reports it. The only finding's remedy, "Rewrite the cross-reference section", would leave the /XRefStm wrong. When the same table reads, it gets hybrid-stream-wrong. This is the unfiled residual from stage 8.
EVIDENCE: Probe ZzXRefStmMalformedTable: the table at 390 ("10 1\n(garbage)\ntrailer ...") is named only by the newest table's /XRefStm. Branch: "Warning xref.section-unreadable at 390: The cross-reference table /XRefStm names at offset 390 is there but cannot be read: ...". The only finding is "Error xref.section-malformed at offset 390: ...". There is no xref.hybrid-stream-lost or xref.hybrid-stream-wrong. HybridFiles.NamingATableNeverRead(), where that table reads, gives both.
FIX (reviewer): In the lost branch, when stream.Kind == XRefSectionKind.Table, call ReportNoStream(stream, "a cross-reference table"), so it is reported beside the section's own fault. Or file this as debt with that design.
--- VERDICT REASON: The finding holds on origin/ccr-6d6c0ee6-dqm4lm-188 (071f829). The table is named only by the newest table's /XRefStm, starts with its xref keyword and cannot be read (DeclaresItselfASection, so it is not relocated). It goes to ReportLostSection at PdfFileReader.cs:3294 and gets xref.section-unreadable in the reader and xref.section-malformed in the validator. ReportNoStream runs only in the branch where the table reads (:3304), so the second fault, an /XRefStm that names a table where ISO 32000-1 Table 19 asks for a stream, is never reported.

The finding is a little narrower than the gap. The named-again branch (:3254) also accepts only ReadAs in state Read or Relocated, so a second /XRefStm naming of the same unreadable table is silent too. A readable table is reported once per naming (A_table_read_in_the_stream_s_place_and_named_again_is_reported_for_each_table_that_names_it).

This is deliberate, not an oversight. XRefSectionRecord.NamesNoStream:189 says "null where it names a stream, or what the chain could not read". The rule's remarks and the validation-rules.md row list only tables "the reader reads". Stage 8 reported it as an open residual. So no decision is broken: B19-5 and the decided list name tables that read. But the rule's own summary ("each hybrid file's table names through /XRefStm the cross-reference stream of its revision") covers this shape. The reader knows the section is a table (Kind Table, its keyword seen), and the control shows that fixing the reported fault brings up a new Error.

Severity is minor, not major. No object is served differently: the index is incomplete either way and object 10 is found by a rebuild. The validation verdict is already Error at the same section. The missing finding is a second fault hidden behind the first, the same class as #325, which was filed as debt. No open debt covers it: the open debt issues were searched and #156's comments were checked.
VERDICT EVIDENCE: Rebuilt independently (probe ZzXRefStmMalformedTableRefute, since deleted). It uses the shape of HybridFiles.NamingATableNeverRead with the named table's body replaced. Release build has 0 warnings.

Control (`10 1\n{row:10}`), named at 390, naming table at 443:
- Records: /XRefStm at 390, Table, state Read, NamesNoStream 'a cross-reference table, whose rows were read in the stream's place'.
- Reader: "Warning xref.hybrid-stream-lost at 390: The /XRefStm of the cross-reference table at offset 443 names offset 390, a cross-reference table, whose rows were read in the stream's place: ..."
- Validator: "Error xref.hybrid-stream-wrong at 443 ..."

garbage-row (`10 1\n(garbage)`), named at 390, naming at 433:
- Records: /XRefStm at 390, Table, state Malformed, NamesNoStream ''.
- Diagnostics, all of them: "Warning xref.section-unreadable at 390: The cross-reference table /XRefStm names at offset 390 is there but cannot be read: the row for object 10, at offset 400, is not an offset, a generation and n or f. The rows read before the fault, if any, were kept; ..."
- Findings, all of them: "Error xref.section-malformed at 390: The cross-reference table at offset 390 cannot be read: the row for object 10, at offset 400, is not an offset, a generation and n or f. | remedy: Rewrite the cross-reference section; a rewrite of the whole file rebuilds it from the objects."
- No hybrid-stream-lost or hybrid-stream-wrong. obj10 "ten" (rebuilt), WasRepaired True.

garbage-header (`(garbage)\n{row:10}`): the same output, fault 'it holds a string at offset 395, where a subsection or the trailer should start'.

garbage-row-named-twice (the oldest table also gives /XRefStm to the same table, at 403):
- Records: Read, Malformed, Read, and a NamedAgain at 403.
- Still only section-unreadable and section-malformed. Neither naming is reported, whereas the readable twin gets two hybrid-stream-lost diagnostics.
FIX (refuter): The fix has two sites in src/AdCodicem.Pdf/IO/PdfFileReader.cs:

1. In the lost branch at :3294, beside ReportLostSection, add: when `stream.Kind == XRefSectionKind.Table && !stream.CutByLimit`, call `ReportNoStream(stream, "a cross-reference table, which cannot be read")`. Its keyword was seen, so the naming is the file's fault whatever the rows hold.
2. At :3254, widen the named-again pattern so that a ReadAs of `{ Kind: Table, State: Malformed, CutByLimit: false }` also gets ReportNoStream, worded "the cross-reference table at offset X, which cannot be read". Each naming of an unreadable table is then reported, as each naming of a readable one is.

_indexIncomplete is already set, and SizeWrongRule.IsWhole already rejects Malformed, so neither changes.

Docs and tests to update:
- The XRefSectionRecord.NamesNoStream XML doc (drop "or what the chain could not read" for tables).
- HybridStreamWrongRule's remarks, the xref.hybrid-stream-wrong row in validation-rules.md, the xref.hybrid-stream-lost row in diagnostics.md, and the threat-model row.
- Add two tests: a malformed table named once, which gives section-unreadable plus hybrid-stream-lost and section-malformed plus hybrid-stream-wrong; and one named twice, which gives one per naming.

This extends B19-5's rule within its stated purpose, so it is not a new design. If the batch is closed to it, file it as debt under M02 with this reproduction and the two sites, next to #325, which is the same class.

######## [27] validator:startxref-names-cidfont — reviewer minor; refuter narrowed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/Validation/Rules/SectionMalformedRule.cs:40 (record left Malformed at PdfFileReader.cs:3162)
CLAIM: When startxref names an object that gives a /W and another /Type (a CIDFont), the validator calls the font a cross-reference stream that cannot be read, and gives no file.startxref-wrong. Batch 19's conversion to NotFound covers only sections a /Prev or /XRefStm names (stage 5 residual, a neighbor of #323).
EVIDENCE: Probe ZzStartxrefFont, file "startxref-font". Diagnostics: only "Repair xref.rebuilt". Finding: "Error xref.section-malformed at offset 110: The cross-reference stream at offset 110 cannot be read: its dictionary is not followed by stream data." Object 110 is "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /F /W [1 [500]] >>". The same font named by a /Prev gives "Error xref.section-not-found ... the offset holds object 4, which is not a cross-reference stream."
FIX (reviewer): Comment on #323 so its fix applies the TypedOther conversion to startxref's section, which turns it into file.startxref-wrong. Do not fix it here.
--- VERDICT REASON: The finding reproduces exactly on branch 071f829. But main 21a0298 gives the same output for the startxref case, byte for byte, so the batch did not cause it. What the batch did was create an asymmetry. Commit d5b4d48 (#281) added the TypedOther conversion to NotFound only in ReportLostSection (PdfFileReader.cs:3778). The chain reaches that method only for a section after the first. For the first section, TryReadXRefChain returns false at :3162-3165 before the conversion, so the record stays Malformed. SectionMalformedRule.cs:40 then counts it as unreadable and calls the font "the cross-reference stream at offset 110". StartXRefWrongRule.cs:29 needs Sections[0].State == NotFound, so file.startxref-wrong does not fire. The branch docs scope the CIDFont sentence ("holds none ([#281])") to the section-not-found row, which is about /Prev and /XRefStm, so they do not misstate the startxref case. The validator rules did not change in this batch. Both rules are Errors, so the file's verdict is right; only the rule and its text are wrong. Issue #323 (startxref section unreadable reported only as xref.rebuilt, batch 22) does not mention this case. A naive fix of #323 would make the reader say a font "is there but cannot be read", so its fix has to apply the same conversion. A dictionary giving only /W (no /Type) at startxref also gets section-malformed. That is the decided "guess" (B19-1) and matches the /Prev case with nothing near it, so it is not part of the defect. The finding is narrowed to: a pre-existing defect that is not a regression, now inconsistent with the /Prev path; an unfiled residual from stages 5 and 11, which the brief asks to judge. Disposition: file it as debt, not fix it here.
VERDICT EVIDENCE: Probe ZzRefuteSxFont (deleted after the runs). It builds the reviewer's file plus variants and calls PdfDocument.Open then PdfValidator.Validate. The pre-existing claim rests on main's output for startxref-font, which matches the branch's. The other variants were run on both; the two stream variants on the branch only.

On origin/ccr-6d6c0ee6-dqm4lm-188 (071f829):
- startxref-font (startxref 110 -> "4 0 obj << /Type /Font /Subtype /CIDFontType2 /BaseFont /F /W [1 [500]] >>"): diagnostics only "Repair xref.rebuilt: The cross-reference index was rebuilt by scanning the file."; finding "Error xref.section-malformed at offset 110: The cross-reference stream at offset 110 cannot be read: its dictionary is not followed by stream data."; no file.startxref-wrong.
- Same font written as a stream with /Length 2 (branch only): "Error xref.section-malformed at offset 110: The cross-reference stream at offset 110 cannot be read: its /W does not give the widths of three fields."
- Same font without /W: "Error file.startxref-wrong at offset 282: startxref gives offset 110, which holds object 4, which is not a cross-reference stream."
- Array at startxref: "Error file.startxref-wrong at offset 236: ... holds object 4, which is not a cross-reference stream."
- /Type /XRef dictionary with no stream data (branch only): section-malformed "its dictionary is not followed by stream data". This one is correct, being a section that is there.
- prev-font (/Prev 1435 -> the same CIDFont, 1,200-byte comment between): "Warning xref.section-missing at 1435 ..." and "Error xref.section-not-found at offset 1435: ... the offset holds object 4, which is not a cross-reference stream."

On main 21a0298:
- startxref-font gives the same output: only xref.rebuilt, and the same section-malformed text.
- prev-font differs: xref.offset-adjusted (found 82 bytes from there), then xref.chain-cycle, xref.section-shifted and xref.chain-loop.

Code: PdfFileReader.cs:3162-3165 (`if (sections == 1) { return false; }`) runs before ReportLostSection, whose :3778 `if (section.TypedOther && !section.CutByLimit)` is what sets NotFound. StartXRefWrongRule.cs:29 requires Sections[0].State == NotFound. #323 has no comments and does not mention a TypedOther object at startxref.
FIX (refuter): Do not fix it in this batch; it is pre-existing and the startxref section's reporting belongs to #323 (batch 22). Comment on #323 with the reproducer: "startxref 110 -> << /Type /Font /Subtype /CIDFontType2 /W [1 [500]] >>" gives on main and on the branch only xref.rebuilt plus "Error xref.section-malformed ... The cross-reference stream at offset 110 cannot be read", while the same object named by /Prev gives xref.section-not-found. The fix #323 should carry: move the TypedOther block at the top of ReportLostSection (PdfFileReader.cs:3778-3786) into a helper and call it on the first section before `return false` at :3164. The record then becomes NotFound, its Fault becomes "holds object 4, which is not a cross-reference stream" and its Trailer is cleared. The validator then gives file.startxref-wrong ("startxref gives offset 110, which holds object 4, which is not a cross-reference stream"), and #323's new reader report must not call that section unreadable. Add tests in CrossReferenceRuleTests for the dictionary and the stream forms. This needs no maintainer decision: B19-1 already says only the xref keyword or /Type /XRef makes a section "there". If the orchestrator prefers to fix it in this batch, it is the same helper call and the same tests.

######## [28] validator:flate-checksum-zero-bytes — reviewer nit; refuter confirmed nit maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/Filters/PdfFilterPipeline.cs:678
CLAIM: #241 added a branch for a count of one but left zero. A Flate stream that decodes to nothing but has a wrong checksum says "all were kept, and some may be wrong" of 0 bytes. Main says the same.
EVIDENCE: Probe ZzFlateZero, data 78 DA 03 00 00 00 00 02: "Warning filter.checksum-mismatch: A Flate stream's checksum disagrees with the 0 bytes its data decoded to; all were kept, and some may be wrong."
FIX (reviewer): For decoded == 0, write "A Flate stream's checksum disagrees with its data, which decoded to nothing."
--- VERDICT REASON: The finding holds as stated, and it does not reach further than that. On the branch head (071f829), the ChecksumMismatch arm of PdfFilterPipeline.ReportEnding (src/AdCodicem.Pdf/IO/Filters/PdfFilterPipeline.cs:678) now has a branch for decoded == 1 but none for decoded == 0. A zlib stream whose only block is an empty final block, followed by a wrong Adler-32, therefore says "all were kept, and some may be wrong" of 0 bytes. That says nothing true, because there is nothing that could be wrong. It is not a regression: on main 21a0298 the message was `{decoded} bytes ...; all were kept, and some may be wrong`, and the Flate decode path is unchanged between main and the branch (the only diff in src/AdCodicem.Pdf/IO/Filters is the message lines). Main therefore gives the same text. #241's closing list covers a count of one and a signed distance, not zero, and "0 bytes" is grammatical, so this is not a defect #241 left open. It is wording on a line the batch has just edited. Only this arm reaches zero. The Corrupt arm never sees decoded == 0, because data nothing decoded from is left encoded and reported as not decoded. The neighboring RunLength message already has its own UnitHeld == 0 arm, which shows the codebase already gives zero its own wording. No data is lost, and the severity (Warning) and code are correct, so this is a nit.
VERDICT EVIDENCE: Uncommitted probe ZzFlateZeroRefuter.ZzFlateZero, built Release on the branch head 071f829. Each input was decoded through new PdfStream({/Filter /FlateDecode}, data).Decode(diagnostics):
- in=78DA030000000002 out.len=0 :: Warning filter.checksum-mismatch: A Flate stream's checksum disagrees with the 0 bytes its data decoded to; all were kept, and some may be wrong.
- in=789C0300FFFFFFFF out.len=0 :: (same text)
- in=789C010000FFFF00000002 out.len=0 :: (same text; a stored empty block)
- in=78DA030000000001 out.len=0 :: (no diagnostic; the correct checksum of empty data, which confirms the empty decode itself is accepted)
Main 21a0298, src/AdCodicem.Pdf/IO/Filters/PdfFilterPipeline.cs:676: `$"A Flate stream's checksum disagrees with the {decoded} bytes its data decoded to; all were kept, and some may be wrong."`. With `git diff 21a0298 HEAD -- src/AdCodicem.Pdf/IO/Filters/` only the message lines change, so main prints the identical text for these inputs. No existing test covers decoded == 0. FilterDamageTests.A_flate_stream_that_keeps_one_byte_says_so_in_the_singular (:336) covers 1, and the ChecksumMismatch(int) helper (:1745) covers the plural. The probe was removed afterwards and the worktree is clean.
FIX (refuter): In ReportEnding's ChecksumMismatch arm, give decoded == 0 its own text, for example "A Flate stream's checksum disagrees with its data, which decoded to nothing." The 1 and plural arms stay as they are. Add a zero case beside A_flate_stream_that_keeps_one_byte_says_so_in_the_singular (or a sibling test that names the behavior, such as "A_flate_stream_that_decodes_to_nothing_against_a_wrong_checksum_says_it_decoded_to_nothing"). It should feed 78 DA 03 00 00 00 00 02, assert an empty result, and assert the exact Warning filter.checksum-mismatch text. Nothing in docs/website quotes the zero case, so no documentation changes. This is the implementer's wording choice, in line with the decided "verb/pronoun agree", not a maintainer decision. If the batch is closed to new wording, filing it as debt under #326's neighborhood is acceptable instead.

######## [29] validator:rules-doc-guard-exception — reviewer nit; refuter confirmed minor maintainer=False
LOCATION: docs/website/docs/reference/validation-rules.md:60
CLAIM: The xref.hybrid-stream-wrong row says a table a limit stopped is judged "but for an /XRefStm that names an offset in the file the chain did not reach, which is the limit's". An /XRefStm equal to the table's /Prev, at an offset in the file the chain has not reached, is reported all the same. The code, the test An_xrefstm_naming_what_the_prev_of_a_table_a_guard_stops_names_is_wrong_as_read_whole and diagnostics.md's xref.hybrid-stream-lost row ("one that names ... what the table's /Prev names too, is reported") all say so.
EVIDENCE: StoppedSectionFiles.Stopped("/Prev {older} /XRefStm {older}") with the table-rows guard: "Error xref.hybrid-stream-wrong at offset 483: ... names offset 199, the section its /Prev names too, past the guard that stopped the table, ...". Offset 199 is in the file, and the chain did not reach it.
FIX (reviewer): Change the clause to "but for an /XRefStm that names an offset in the file the chain did not reach, other than the one the table's /Prev names, which is the limit's".
--- VERDICT REASON: The finding holds. In docs/website/docs/reference/validation-rules.md:60, the guard clause makes one exception: an /XRefStm "that names an offset in the file the chain did not reach" is the limit's. That wording also covers an /XRefStm naming the same in-file offset as the table's /Prev. When a guard stops the table, the chain reads nothing past it, so it never reaches that offset either. The code does report this case. In PdfFileReader.cs:3236-3245, the branch `hybrid >= 0 && hybrid == previous && !ReachedBefore(...)` runs before the `!(pastGuard && IsInFile(...))` exception. Under a guard it calls ReportNoStream with "past the guard that stopped the table", and HybridStreamWrongRule turns that into an Error.

Other texts agree with the code: the test CrossReferenceRuleTests.cs:901, the diagnostics.md:115 row, and threat-model.md:416, which lists "what the table's /Prev names too" as reported before giving the in-file exception. Only an /XRefStm alone at an unreached in-file offset is the limit's.

The same gap is in docs/website/docs/reference/reader-limits.md:65-69. Its list of values reported despite the cut leaves out the /Prev-shared offset, and its list of what is the limit's ("an offset in the file the chain has not reached") includes it. The XML docs on PdfDiagnosticCodes.XRefHybridStreamLost and PdfValidationRuleIds.XRefHybridStreamWrong do not cover the guard case, so they are not wrong. Both doc rows are new in this batch: main 21a0298 has no xref.hybrid-stream-wrong row.

I rate it minor, not nit, because the text is not just badly worded: it says an Error the validator emits is not emitted. The impact is narrow: it needs a guard plus an /XRefStm equal to the /Prev, and the finding's own message says "past the guard that stopped the table".
VERDICT EVIDENCE: Probe on origin/ccr-6d6c0ee6-dqm4lm-188 (071f829), built in Release with 0 warnings. It used StoppedSectionFiles.Stopped(links) (section 483), validated with PdfValidator, and the probe was removed afterward.

links="/Prev {older} /XRefStm {older}", guard "table rows":
  diag Warning limit.xref-section-length at 483: The cross-reference table runs past 128 bytes; ...
  diag Warning xref.hybrid-stream-lost at 199: The /XRefStm of the cross-reference table at offset 483 names offset 199, the section its /Prev names too, past the guard that stopped the table: ISO 32000-1 has it name the cross-reference stream of the table's revision (Table 19). ...
  rule Error xref.hybrid-stream-wrong at 483: The /XRefStm of the cross-reference table at offset 483 names offset 199, the section its /Prev names too, past the guard that stopped the table, where ISO 32000-1 names the cross-reference stream of the table's revision (Table 19): what only that stream indexes is lost.
  rule Information xref.checked-in-part at 483
Guard "table index": same hybrid-stream-lost warning and same hybrid-stream-wrong Error, next to limit.index-entries.

Control, links="/XRefStm {older}" (no /Prev):
  guard "table rows": only limit.xref-section-length and xref.checked-in-part (the limit's, as the doc says).
  no guard: Error xref.hybrid-stream-wrong ("a cross-reference table, whose rows were read in the stream's place").

So under a guard, offset 199 (in the file, not reached) is the limit's when only /XRefStm names it, and is reported as an Error when /Prev names it too. validation-rules.md:60 makes no such distinction.
FIX (refuter): In docs/website/docs/reference/validation-rules.md:60, change the clause to: "...is judged so too, as in the file read whole, but for an `/XRefStm` that names an offset in the file the chain did not reach, other than the one the table's `/Prev` names, which is the limit's ([#264])."

Make the same change in docs/website/docs/reference/reader-limits.md:66-69:
- add "or, for an `/XRefStm`, what the section's `/Prev` names too" to the "Such a value is ..." list;
- change the limit's list to read "an offset in the file the chain has not reached and no `/Prev` of the section names too".

This is documentation only; the code and tests stay as they are. No design choice is needed: the behavior was decided under B19-5 (branch (c)) and #264, and the code, the test and diagnostics.md already follow it.

######## [30] validator:xrefstm-table-cut-trailer-wording — reviewer nit; refuter confirmed nit maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3875
CLAIM: A table the end of the file cuts is said to lose "what only the rest of the table, or the sections its trailer would name, index". When an /XRefStm names that table, its trailer's links are never followed (#263), so it loses no sections through them.
EVIDENCE: PartialFiles.CutTableAnXRefStmNames(): "Warning xref.section-unreadable at 549: The cross-reference table /XRefStm names at offset 549 was read only in part: the file ends before its trailer; the rows read were kept, and what only the rest of the table, or the sections its trailer would name, index is looked for by rebuilding the index."
FIX (reviewer): Drop ", or the sections its trailer would name," when section.NamedBy == "/XRefStm".
--- VERDICT REASON: The finding holds. When a table is named by /XRefStm, the reader never reads its trailer's /Prev or /XRefStm: line 4308 of PdfFileReader.cs guards them with `section.NamedBy != "/XRefStm"`, and RetakeTrailerPastGuard does the same at line 4589 (#263). Cutting such a table's trailer therefore loses no sections through it, and the chain goes on to the oldest table through the hybrid table's /Prev. Even so, the read-in-part message at line 3875 says "or the sections its trailer would name". This text is new in this batch: `git grep` finds no "or the sections its trailer would name" on main 21a0298. Nothing is mis-read. The clause is empty rather than false, since any section such a trailer names is reached through the hybrid table's /Prev or not at all. So this is a wording problem, kept at nit. The XML remark on ReportSectionReadInPart, at lines 3853-3854, has the same flaw and should be fixed along with it: "A table the end of the file cuts loses its trailer too, and with it the older sections that trailer would name: the chain ends there." For a table named by /XRefStm, the chain does not end there. The general text in diagnostics.md and validation-rules.md ("the older sections a lost trailer would name") still reads correctly for tables named by /Prev or startxref and can stay.
VERDICT EVIDENCE: I wrote a probe (since removed) that opens PartialFiles.CutTableAnXRefStmNames(rows) for both InlineData rows of the existing theory, on origin/ccr-6d6c0ee6-dqm4lm-188 at 071f829, Release build. Diagnostics, identical for both rows:
"Warning xref.hybrid-stream-lost at 549: The /XRefStm of the cross-reference table at offset 436 names offset 549, a cross-reference table, whose rows were read in the stream's place: ..."
"Warning xref.section-unreadable at 549: The cross-reference table /XRefStm names at offset 549 was read only in part: the file ends before its trailer; the rows read were kept, and what only the rest of the table, or the sections its trailer would name, index is looked for by rebuilding the index."
Section records, which show the chain going past the cut table:
"startxref from 529 at 436 Table Read incomplete=False"
"/XRefStm from 436 at 549 Table Read incomplete=True"
"/Prev from 436 at 224 Table Read incomplete=False"
In the code, src/AdCodicem.Pdf/IO/PdfFileReader.cs:4305-4308 says "A table an /XRefStm names ... stands in its place: as a hybrid file's stream's own /Prev, what its trailer names is not read", inside `if (trailer is not null && section.NamedBy != "/XRefStm")`. Line 3875 builds the message with no test of `naming`.
On main, `git grep "or the sections its trailer would name" 21a0298 -- src docs` finds nothing, so this wording was introduced by this batch.
The existing test A_table_an_xrefstm_names_that_the_end_of_the_file_cuts_inside_a_subsection_leaves_no_older_row_standing_for_the_rest_of_it checks the message only with StartsWith, up to "the file ends before its trailer;", so the clause is not pinned for the /XRefStm case.
FIX (refuter): In ReportSectionReadInPart (PdfFileReader.cs:3875), when the cut table is named by /XRefStm (`naming == "/XRefStm"`, or `section.NamedBy == "/XRefStm"`), write "the file ends before its trailer; the rows read were kept, and what only the rest of the table indexes is looked for by rebuilding the index". Keep the current text for startxref and /Prev. Also fix the remark at lines 3853-3854: "A table the end of the file cuts loses its trailer too, and with it the older sections that trailer would name: the chain ends there — but for one an /XRefStm names, whose trailer's links are not read whole either (#263): the chain goes on through the table that names it." Then make CrossReferenceChainTests.A_table_an_xrefstm_names_that_the_end_of_the_file_cuts_inside_a_subsection_... assert the full message, and keep the /Prev test at line 1784 asserting the longer wording, so that a mutant dropping the new condition is killed. This is a wording fix only, with no design choice for the maintainer.

######## [31] tests:T317-still-flaky-under-load — reviewer minor; refuter narrowed minor maintainer=True
LOCATION: tests/AdCodicem.Pdf.Tests/ReaderTimingTests.cs:162 (The_validator_judges_an_object_stream_s_members_in_linear_time, bound smallTime * 8; comment at :134-142)
CLAIM: #317's test still fails under four busy loops. The fix (room of 1,000 findings, five runs, bound 8) removed the collections, but the small probe takes about 5 ms, less than a scheduler slice. Under contention, all five large runs can be slowed while the small one is not. The comment's claim of '2.1 to 5.1 under four busy loops' does not hold. The neighbor test hit the same failure profile at bound 8 (8 failures in 300 under four busy loops) and moved to 12.
EVIDENCE: Release build at 071f829. Under four `while :; do :; done` loops (load 4.4), run 3 of 5 with the filter on the batch's timing and allocation tests failed: `failed ...The_validator_judges_an_object_stream_s_members_in_linear_time(shape: "a header broken before its last member")`, message `Expected largeTime to be less than 40ms and 712.8µs because four times the members must cost about four times the time (00:00:00.0050891 for 50,000, 00:00:00.0454299 for 200,000), but found 45ms and 429.9µs.`, a ratio of 8.93. Over all real runs of this test under four busy loops (5 runs of the combined filter, 10 filtered alone, 6 of the whole ReaderTimingTests class), it failed 1 time in 21. Alone it failed 0 times in 10. A probe that copies the test's measure (same Packed files, five runs, GC.Collect before each) gave ratios of 2.63-5.62 alone over 30 rows, and 2.75-6.01 under busy loops over 45 rows. The first row, run first in a fresh process under busy loops, timed its small probe at 27-33 ms (4.3-8.5 ms warm).
FIX (reviewer): Make each timed size span several scheduler slices. For example, time k back-to-back probes of each document, or measure through BestOfEach(..., collect: true), which alternates the two until neither improves. Alternatively raise the bound to 12, as the neighbor did; the comment's measure of a scan of the header for each member (13.0-22.5) still clears 12, but only narrowly, so prefer longer measures. Then measure again under four busy loops (the neighbor used 300 runs), and correct the comment's '2.1 to 5.1 under four busy loops'.
--- VERDICT REASON: The finding holds in substance. On branch head 071f829, The_validator_judges_an_object_stream_s_members_in_linear_time still fails its bound of 8 under heavy CPU contention, and the comment's "2.1 to 5.1 under four busy loops" understates the range. I narrow three parts of it.

(1) Where it fails. The failures need the combined filter, which runs the batch's timing and allocation tests in the same process, plus heavy load. Filtered alone to the method, it passed 30 of 30 runs under four busy loops (the reviewer had 10 of 10).

(2) The mechanism. "The small probe is under a scheduler slice" is too narrow. One failure here had a 17.5 ms small probe. The pattern is that the probe four times shorter finds a quiet window in at least one of its five turns, while the long one rarely does. The ratio then reaches about twice its true value of about 4.

(3) The comment. It does not state a false bound. It faithfully records stage 1's 15 rows. But it is narrower than what the batch's own measurement lens recorded for this same change (streams up to 6.13, broken row up to 6.84), so it misleads.

This is not a regression. On main the measurement had the test failing 5 of 10 runs under four busy loops, and the fix removed the systematic garbage-collection cost.

By the project's own precedent the residual flake still counts. The neighbor test moved off bound 8 at 8 failures in 300 under four busy loops (ReaderTimingTests.cs:322-325). This test fails at a comparable or worse rate in that condition.

Why the maintainer must decide: B19-8 was consented to on the measurement's claim that no run reached 8 "in every condition including four busy loops". That premise does not hold, and every remedy changes B19-8's decided shape (five turns, bound 8).
VERDICT EVIDENCE: Release build of 071f829 in my worktree. The only change was an uncommitted hook, gated by an environment variable, that appends each row's best small time, best large time and ratio to a file after timing and before the unchanged assertion. Each run was a fresh process: `dotnet AdCodicem.Pdf.Tests.dll --filter-method ...`. The busy loops were 4x `while :; do :; done`, killed by PID afterwards. Other agents were building beside me, so the load average was 3 to 10 on 4 CPUs. The hook was reverted afterwards.

Filtered alone, 3 runs without busy loops: all passed. Ratios 1.55-4.18 (wrong-index 1.55 was the first row, cold JIT).

Filtered to the method alone under four busy loops, 30 runs: 0 failed. Per row over 30:
- wrong-index: 0.86-6.60, median 3.97
- streams: 2.51-6.69, median 4.04
- broken: 2.23-5.22, median 4.25

Under four busy loops, combined filter (`--filter-method *The_validator_judges_an_object_stream_s_members_in_linear_time* --filter-method *Four_times_as_many_lost_streams_declaring_numbers_of_their_own* --filter-method *Older_rows_looked_for_among_4096_refused_ranges*`), 20 runs: this test failed in 2.
- Run 2: `failed ...The_validator_judges_an_object_stream_s_members_in_linear_time(shape: "a header broken before its last member")`, message `Expected largeTime to be less than 45ms and 658.4µs because four times the members must cost about four times the time (00:00:00.0057073 for 50,000, 00:00:00.0535121 for 200,000), but found 53ms and 512.1µs.` Ratio 9.38.
- Run 7: `(shape: "members written as streams")`, message `Expected largeTime to be less than 139ms and 633.6µs ... (00:00:00.0174542 for 50,000, 00:00:00.1523107 for 200,000), but found 152ms and 310.7µs.` Ratio 8.73.

Combined-filter ranges over 20 runs: wrong-index 1.02-5.72, streams 3.50-8.73, broken 2.56-9.38. The failing rows had normal small probes (broken row's warm small is about 5 ms; streams row's is about 13-17 ms) and large probes 2.3-2.7x their normal value (broken 22 to 53.5 ms, streams 55 to 152 ms).

Pooled with the reviewer's runs, that is 3 failures in 71 under four busy loops. Data: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute-t317/data/{busy,combined}.{runs,ratios}.

Separately from this finding, the batch's new Four_times_as_many_lost_streams_declaring_numbers_of_their_own_cost_about_four_times_the_time also failed once in those 20 combined runs: `(00:00:00.0463554 for 256, 00:00:00.4264992 for 1,024)`, ratio 9.2.

The comment's range comes from stage 1's 5 runs under busy loops (impl-reports-1.json: 2.14-5.14). Measure/flaky's prototype D, the same change, recorded streams up to 6.13 and broken up to 6.84 over 81 runs.
FIX (refuter): Two parts, the first needing no decision and the second a maintainer's choice.

1. Correct the comment at ReaderTimingTests.cs:139-142, whatever else is chosen. Give the measured tail instead of 2.1 to 5.1: up to about 6.7 filtered alone under four busy loops, and an occasional 8.7 to 9.4 under heavier contention. Say how often it failed there.

2. Choose a remedy (a maintainer's choice, since B19-8 decided five turns and bound 8):
   - (a) Recommended: keep a room of 1,000 and the bound of 8, but time several back-to-back probes per turn, e.g. 4. The small measure then spans tens of milliseconds and cannot catch a quiet window the large one never gets. Better still, compare four small probes against one large, so both spans are equally long and contention hits them alike. That means a bound near 2 against a scan's about 4.
   - (b) Measure the thread's CPU time instead of wall time, which preemption cannot inflate.
   - (c) Bound 12, as the neighbor did. Weakest: the scan mutant measured 13.0-22.5 at room 1,000, a margin of about 1.

   Whichever is chosen, rerun under four busy loops with the combined filter, as many runs as the neighbor's 300, and with the scan mutant (FirstIndexOf returning Array.IndexOf) to show it is still caught.

   If the maintainer keeps D as it is, the PR should not present #317 as closed under four busy loops. Either file the residual contention flake as debt, or state the measured rate.

######## [32] tests:uncovered-refuse-ranges-after-every-number — reviewer minor; refuter confirmed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3560-3562 (RefuseRanges: `if (_everyNumberRefused) { return; }`)
CLAIM: The only line of the batch's src patch that no test covers can be reached by input. RefuseRowsLost calls RefuseRanges for a classic table kept though read only in part, and does not check _everyNumberRefused first, as RefuseDeclared does. So a table cut after a row, older than a lost stream that refused every number, reaches the early return. No test builds that chain. Patch coverage is 686 of 687 measured lines.
EVIDENCE: The whole unit suite was run with `--coverage --coverage-output-format cobertura` (6,857 passed, 20 skipped). The changed lines of src/ were then matched against the cobertura report: `patch lines measured 687, covered 686, 99.85%`, `UNCOVERED src/AdCodicem.Pdf/IO/PdfFileReader.cs 3562 return;`, `PARTIAL ... 3560 50% (1/2) if (_everyNumberRefused)`. The reproducer chain is: a newest table with /Prev naming stream 5 `<< /Type /XRef /Size 9 /Index /X /W [1 -4 2] /Prev <oldest> /Length 0 >>`, whose /Prev names an oldest table `xref\n0 6\n<free row>\n<row of 1>\n` that ends the file. With the return replaced by a throw in a scratch copy, the probe failed with `InvalidOperationException : ZZ RefuseRanges reached after every number was refused`. At the head it reports `xref.section-unreadable: The cross-reference stream /Prev names at offset 110 is there but cannot be read: its /W gives a field a negative width. The chain goes on through its /Prev; every object no section read before it indexes is looked for...` and then `xref.section-unreadable: The cross-reference table /Prev names at offset 347 was read only in part: the file ends before its trailer; ...`.
FIX (reviewer): Add a CrossReferenceChainTests case with this shape: a lost stream whose /Index cannot be read hands /Prev on to an older table the end of the file cuts after a row. Assert both reports, and assert that object 1's row in the cut table does not stand (no older row stands once every number is refused). If the early return is kept for its cost alone, add a one-line reason in the code.
--- VERDICT REASON: The claim holds. RefuseRanges is new in this batch; main 21a0298 has no RefuseRanges and no _everyNumberRefused. It has two callers. RefuseDeclared checks _everyNumberRefused before it calls RefuseRanges. RefuseRowsLost's classic-table branch (PdfFileReader.cs:3953-3955) calls RefuseRanges(_rowsLost) without that check. So the early return at :3560-3563 is reached only when a classic table is kept though read only in part and an earlier lost stream has already refused every number. A file can build that chain, and no test in the suite does.

One narrowing. Taking the return out changes nothing anyone can observe. RefusedToOlderRows (:3635) checks _everyNumberRefused before it looks at any range, and the ranges are only handed back to the pool when the chain has been read. So the return only saves one rent from the pool and one merge. This makes it a coverage gap, not a wrong behavior. A test can cover the line but cannot kill the "drop the condition" mutant. Stage 7's mutation runs show that mutant (G1) surviving in both passes (`G1: SURVIVED (total 1885)`, then `(total 1566)`), and it must be classed equivalent, with that reason, under ADR 53. CLAUDE.md asks for 100 % of the patch when an input can reach a line. A private member that callers can reach through a document is not exempt. So the gap is minor (an uncovered line that input reaches), not a defect.
VERDICT EVIDENCE: On origin/ccr-6d6c0ee6-dqm4lm-188 (071f829), in a scratch copy, I left the return in place and added a line before it that appends the stack trace to a scratchpad file. I built Release with 0 warnings.

The probe file:
- header;
- objects 1 (catalog), 2 (pages) and 3;
- stream 5 `<< /Type /XRef /Size 9 /Index /X /W [1 -4 2] /Prev <oldest> /Length 0 >>` with empty data;
- a newest table `0 3` (rows for 0, 1 and 2) whose trailer is `/Prev <stream>`, then startxref and %%EOF;
- after those, the oldest table `xref\n0 6\n0000000000 65535 f \n<row of 1>`, which ends the file.

Output of the probe:
- `xref.section-unreadable @146: The cross-reference stream /Prev names at offset 146 is there but cannot be read: its /W gives a field a negative width. The chain goes on through its /Prev; every object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows.`
- `xref.section-unreadable @387: The cross-reference table /Prev names at offset 387 was read only in part: the file ends before its trailer; the rows read were kept, and what only the rest of the table, or the sections its trailer would name, index is looked for by rebuilding the index.`
- Sections: 254 Read; 146 Malformed; 387 Read with incomplete=True.

The hook fired once, with stack `RefuseRanges <- RefuseRowsLost (line 3958) <- ReportSectionReadInPart (3873) <- ZzRefuseRangesProbe`.

A variant where the oldest table sits mid-file with a malformed row does not reach the line. It ends as Malformed, `is there but cannot be read: the row for object 2 ... is not an offset, a generation and n or f`, so only the shape the end of the file cuts does.

I then ran the whole unit suite with the hook in place: total 7196, 7174 passed, 20 skipped, 2 failed. The 2 failures were my probe's own rows, which call Assert.Fail on purpose to show their output. The hook file held exactly one hit, from ZzRefuseRangesProbe. No other test reaches the line.

Stage 7's mutation output (lot19/stage7/mutants-pass1.out:21 and mutants-pass2.out:3) records G1, which removes this early return, as SURVIVED.

The scratch changes were reverted, and the worktree is clean.
FIX (refuter): Add one CrossReferenceChainTests case (a [Fact]), built with the same writer as LostStreamFiles.Chain:
- the newest table indexes the catalog and the pages and names, through /Prev, a lost stream of `/Type /XRef /W [1 -4 2] /Index /X`;
- that stream's /Prev names an oldest classic table `xref\n0 6` that holds the free row and object 3's row (an object the newest table does not index), and the file ends there.

The test should assert:
- the diagnostics start with the two xref.section-unreadable reports, at the stream offset with the "every object no section read before it indexes" ending, and at the cut table with "was read only in part: the file ends before its trailer;";
- the cut table's section is Read with Incomplete true;
- ChainIndex.TryGet(3) is false (no older row stands once every number is refused);
- Text(3) reads "only in the oldest" after a rebuild, and WasRepaired is true.

The finding's suggested assertion on object 1 should not be used if the newest table indexes object 1: it would hold whatever the cut table gives. The test covers :3562 but cannot kill the dropped-return mutant. Class that mutant equivalent in the batch-wide ADR 53 record, with a one-line reason: every number is refused already, RefusedToOlderRows checks that first, and the return only spares a rent and a merge. Optionally, add that same reason as a comment on the return. No maintainer choice is needed.

######## [33] tests:stream-end-row-pins-comment-header-residual — reviewer minor; refuter narrowed minor maintainer=False
LOCATION: tests/AdCodicem.Pdf.Tests/StreamLengthTests.cs:522 (`[InlineData("% 12 0 obj", 2)]`) and the comment at :530-535
CLAIM: The test is named '..._stops_the_search_when_it_is_an_object_header_and_only_then', and its comment says headers are read 'as a load reads it (#261)'. The batch added a row that asserts the stream-end search stops at `12 0 obj` inside a comment (`% 12 0 obj`), which a load reads as a comment, not a header. The row pins stage 4's unfiled residual (the obj keyword's own line is read by its digits) as correct behavior. Unlike DocumentReaderTests.The_line_of_an_obj_keyword_is_read_by_its_digits_whatever_else_it_holds, it neither says so nor cites an issue.
EVIDENCE: The theory passes at 071f829 with the row expecting a header 2 bytes into `% 12 0 obj`: the stream's data is cut inside the comment, `stream.Data.Length == data.Length + 1 + 2` and `EndStreamState.MissingBeforeNextObject`. Its neighbor row `[InlineData("% a note 12\n0 obj", null)]` expects no header for the same digits once the comment line ends before `0 obj`. Both are a comment to PdfLexer, which the property test LastTwoAsTheLexerReads uses as the reference.
FIX (reviewer): File stage 4's residual as debt, or add it to #171, whose subject is headers inside comments, strings and data. Cite it on this row, and add one sentence to the test's comment saying the obj keyword's own line is read by its digits, not lexed, so this row is the known difference from a load. Flip the row when the debt is paid.
--- VERDICT REASON: The finding holds on its main point. Commit b09f981 (#261, B19-7) added the row `[InlineData("% 12 0 obj", 2)]` at tests/AdCodicem.Pdf.Tests/StreamLengthTests.cs:522. The row asserts that the stream-end search stops at a header that a comment on obj's own line holds, although PdfLexer reads that text as a comment and nothing else. This is the stage 4 residual that residuals-impl.md lists twice ('% 1 0 obj' on obj's own line is taken by the scan and the stream-end rule, though a load reads a comment). It is not among the debts filed for the batch (#320 to #328). The batch's comment on #171 (2026-11-03) lists what batch 19 leaves open there: the chunk edge, headers in stream data and strings, the nearby search taking the first match, and trailers in data. It does not list headers in comments. The neighbouring scan test, DocumentReaderTests.The_line_of_an_obj_keyword_is_read_by_its_digits_whatever_else_it_holds (:432), states the same difference and cites #171. The StreamLengthTests row does neither, so a later fix to the rule would look like a regression to anyone who flips it.

Narrowed in two ways. (1) This is not a regression. main 21a0298 cuts the data at the same place, 2 bytes into the text with MissingBeforeNextObject, so the batch pins behavior that already existed. That behavior is also as decided: B19-7 says the stream-end rule follows the scan, and the scan takes `% 4 0 obj`. HeaderText.FindHeader's XML remarks (src/AdCodicem.Pdf/IO/HeaderText.cs) describe the rule exactly: a line is lexed only where the white space crossed holds an end of line. (2) The test comment is not strictly false. It defines a header 'as the bytes hold it', and its 'as a load reads it' refers to comments that lie between a header's tokens. It does not refer to tokens that a comment holds. So the test holds its own definition. What is missing is the sentence that says this row is the known difference from a load, and an issue where that difference is recorded. Minor rather than nit, because a residual found and left was not filed (CLAUDE.md requires a debt issue) and a row pins it silently.
VERDICT EVIDENCE: I added a probe (an uncommitted Zz test, since removed) with the theory's own shape: classic file, stream 5 declaring data.Length+400, the data, then "\n" + text + "\nendstream", and object 6 after it. It reports how far into the text the data is cut, the EndStream state, and the PdfLexer tokens of the text.

Branch 071f829:
PROBE [% 12 0 obj] data cut 2 bytes into the text (text length 10); EndStream=MissingBeforeNextObject; lexer tokens: []
PROBE [% a note 12\n0 obj] data cut 17 bytes into the text (text length 17); EndStream=Found; lexer tokens: [Integer Keyword ]
PROBE [12 %\n0 obj] data cut 0 bytes into the text; EndStream=MissingBeforeNextObject; lexer tokens: [Integer Integer Keyword ]

main 21a0298 (same probe):
PROBE [% 12 0 obj] data cut 2 bytes into the text (text length 10); EndStream=MissingBeforeNextObject; lexer tokens: []
PROBE [% a note 12\n0 obj] data cut 9 bytes into the text; EndStream=MissingBeforeNextObject
PROBE [12 %\n0 obj] data cut 10 bytes into the text; EndStream=Found

The existing theory Text_that_follows_a_lost_endstream_stops_the_search_when_it_is_an_object_header_and_only_then passes on the branch with 44 of 44 rows. git diff 21a0298..HEAD shows the `% 12 0 obj` row is new in b09f981. The theory's comment (:530-535) does not mention the row's shape and cites no issue for it. DocumentReaderTests.cs:434-436 says: "the keyword's own line is read by the digits before it, as it was, and a header a comment holds is taken for one, as a header in a string is (#171). Lexed from its start, the line would be a comment." The open #171 and its five comments never name headers inside comments.
FIX (refuter): Record the difference and cite it. Comment on #171: the scan, the search near a row and the stream-end rule read the obj keyword's own line by its digits. So `% N G obj` is taken for a header there, although a load reads a comment. The scan's form is held by DocumentReaderTests.The_line_of_an_obj_keyword_is_read_by_its_digits_whatever_else_it_holds, and the stream-end form by StreamLengthTests' `% 12 0 obj` row. Since B19-7 makes the rule follow the scan, whatever #171 decides for the scan applies to both. A separate `debt` issue under M02 would do as well if the maintainer prefers one per issue. Then add one sentence to the theory's comment at StreamLengthTests.cs:530-535, for example: "obj's own line is read by its digits, not lexed, so a header a comment holds on that line stops the search, as the scan takes one (#171); lexed from its start, the line would be a comment." Change no behavior in this batch, and flip the row when #171 is paid. The concepts/diagnostics.md:52-53 sentence ('Comments may lie between the tokens, as a load reads them') could take the same half-sentence, but it is not wrong as written.

######## [34] tests:lost-streams-timing-near-bound — reviewer nit; refuter narrowed minor maintainer=False
LOCATION: tests/AdCodicem.Pdf.Tests/ReaderTimingTests.cs:566 (Four_times_as_many_lost_streams_declaring_numbers_of_their_own_cost_about_four_times_the_time, bound smallTime * 8)
CLAIM: #208's timing test passed every run but came within 4% of its bound under four busy loops. Its BestOfEach runs without collect, and the large opening allocates far more (1,024 dictionaries of 1,400 integers each).
EVIDENCE: Probe copying the test (same files, options and BestOfEach): alone 3.90-4.46 over 10 runs. Under four busy loops 3.50-6.28 over 10 filtered runs, and 3.93, 4.21, 4.18, 7.69, 4.50 in the 5 combined runs. The 7.69 run, at load 5.67, had small 60.9 ms and large 468.0 ms. The real test passed 25 of 25 under busy loops and 10 of 10 alone.
FIX (reviewer): Pass collect: true to BestOfEach, as the allocation-heavy measures elsewhere do. Or widen the bound: removing the 4,096 cap also fails the test's functional assertion (`TryGet(3)` is no longer false), so the timing bound can afford a wider margin.
--- VERDICT REASON: The finding is right that the test is sensitive to load. Under load I measured worse than the reviewer did: the probe crossed the bound of 8, once in each mode. The stated cause is wrong, though. The test runs without collect, but no garbage collection fell inside any best run, with or without collect. The large opening allocates 4.0 times what the small one does, which is what four times the streams should allocate. And collect: true also crossed the bound, at 10.33, so the proposed fix does not help.

The excursions follow CPU contention. Every large opening in a trial is slowed, to 400-580 ms against about 210-250 ms, while the small ones stay near 50 ms. That is the cause b2d5e9b recorded for the circular-streams neighbor ("all five walks of the larger file are slowed now and then"), which it fixed by widening that bound to 12. b2d5e9b also found that a collected heap and taking more turns did not help.

Widening is safe here. Removing the 4,096 cap already fails the test's functional assertion before any timing is taken. Rated minor rather than nit because a crossing was reproduced. The rate is comparable to the one that led to b2d5e9b (8 in 300 under four busy loops) and to #317 (2 in 309 in CI).
VERDICT EVIDENCE: Branch origin/ccr-6d6c0ee6-dqm4lm-188 (071f829), Release build. 4 vCPUs. Other agents' builds were running beside the probes, so the load average was 4 to 10.

Probe ZzLostTiming: a copy of the test (same LostStreamFiles.OwnNumbers(256 / 1024, each: 700, rows: 16), same options, same BestOfEach stop rule). It also counts gen0 collections inside each best run.
- Alone, no collect (5 trials): ratios 4.06, 4.10, 4.13, 4.19, 4.41. Small about 43-48 ms, large about 187-196 ms.
- Alone, collect: true (5 trials): 4.10 to 4.22.
- Under four busy loops, no collect, 28 trials:
  - Most ratios were 3.90 to 5.91.
  - Three were 7.37, 7.47 and 10.03. The 10.03 trial, at load 6.60, had small 49.1 ms and large 492.8 ms.
  - One trial had a gen0 collection inside the best large run. It was the first, at ratio 4.33, not one of the high ratios.
- Under four busy loops, collect: true, 28 trials:
  - Most ratios were 2.71 to 6.59.
  - One was 10.33, at load 5.39: small 56.3 ms, large 581.1 ms, gcInBestLarge=0.
- In every trial with a high ratio, gcInBestSmall=0 and gcInBestLarge=0.

Allocation per opening, on the test thread: small 13.3 MB, large 53.2 MB (4.0x). File sizes are 1,586,403 and 6,727,640 bytes.

The real test, called 20 times in-process under four busy loops at load 6.4-8.75: 20 of 20 passed.

Mutant MaxRefusedRanges = int.MaxValue (cap removed):
- The real test fails on its functional assertion: "Expected document.Reader.ChainIndex!.TryGet(3, out _) to be False because past 4,096 ranges, every number is refused, but found True."
- With that assertion skipped, the mutant's timing ratio was 10.87-11.20, over 6 trials alone. The test comment gives 11.7.

Precedent: b2d5e9b ("let the circular streams' timing ratio reach twelve before it fails") gives the same cause. That test failed 8 times in 300 under four busy loops, every walk from a collected heap. Alternating the walks until neither improves failed about as often.
FIX (refuter): Do not pass collect: true: it does not touch the cause, and it crossed the bound too. Widen the bound to smallTime * 12 at ReaderTimingTests.cs:566, as b2d5e9b did for Four_times_as_many_object_streams_that_need_one_another.

Reword the comment to match. An uncapped merge, which reads about 11 to 12 times, is held by the TryGet(3) assertion, not by the time. The time tells a chain whose cost grows with the number of streams (about 4) from one whose cost grows with its square (about 16). The bound of 12 allows for every large opening being slowed by contention on a busy machine.

This is a test-only change with a direct precedent, so it is not a design choice for the maintainer.

######## [35] tests:name-garbled-deferred-length — reviewer nit; refuter narrowed nit maintainer=False
LOCATION: tests/AdCodicem.Pdf.Tests/DocumentReaderTests.cs:2343
CLAIM: The test name `A_deferred_length_is_taken_again_only_by_the_row_the_section_keeps_for_it_which_one_of_a_reserved_type_is` does not read as a sentence, and it hides the behavior it holds: the data is not taken again, because the row kept is of a reserved type.
EVIDENCE: The body asserts `XRefSectionUnreadable, XRefEntryReserved, StreamLengthInvalid`, `section.Incomplete` true, `ReservedRow == "the row for object 4 gives it type 3"` and `WasRepaired` true, so the length is not retaken.
FIX (reviewer): Rename it to, for example, `A_deferred_length_is_not_taken_again_when_the_first_row_numbering_it_is_of_a_reserved_type`.
--- VERDICT REASON: The finding holds, but more narrowly than stated, and it is a wording issue only. The name does parse as a sentence. "which one of a reserved type is" puts the complement of "is" first: it means "which a row of a reserved type is", so it says the kept row is the reserved one. Other names in the suite use the same verb-final style, for example A_deferred_length_whose_reference_leads_to_an_object_only_its_own_section_places_is_read_once_the_chain_is and Rows_past_a_full_index_are_not_read_and_the_trailer_past_them_is. So the claim that it "does not read as a sentence" goes too far. The part that holds is that the name states a rule ("taken again only by the row the section keeps") and leaves the reader to work out the outcome. Read plainly ("taken again ... by the kept row, which is of a reserved type"), it suggests the reserved row takes the data again. In fact no row does: the data stays as the chain took it, the section is incomplete, and a rebuild finds the page. Read literally, the name is not false, because "only" restricts and does not assert. The body and its comment state the behavior clearly, so this is a nit, not a misleading text. The rename came in 01426c1 (#221). On main 21a0298 the test was A_deferred_length_is_taken_again_by_the_row_the_section_keeps_for_it and asserted the opposite outcome: no diagnostics and a complete section.
VERDICT EVIDENCE: Branch head 071f829 (origin/ccr-6d6c0ee6-dqm4lm-188), Release build. The test is at tests/AdCodicem.Pdf.Tests/DocumentReaderTests.cs:2343 and passes (filter "*A_deferred_length_is_taken_again*": 4 passed, 0 failed). A probe (removed afterwards) opened ChainFiles.AloneWithIndirectLengthBeforeSpelledRows(reservedRowFirst: true) and printed the following.
- xref.section-unreadable: "...was read only in part: it holds 6 rows where its /Index and /Size declare 9; ..."
- xref.entry-reserved: "...the row for object 4 gives it type 3. ISO 32000-1 defines types 0, 1 and 2, and reads any other as a reference to the null object (7.5.8.3)..."
- stream.length-invalid: "The /Length of cross-reference stream 5 names object 4 0, which the file lacks; the chain took its data up to its endstream, after 42 bytes."
- Incomplete=True

The test then asserts WasRepaired true after GetObject(3). So no row took the length again, and the name's plain reading suggests otherwise. `git show 01426c1` shows the old name, A_deferred_length_is_taken_again_by_the_row_the_section_keeps_for_it, whose assertions were Diagnostics empty, Incomplete false and no XRefSectionMalformed.
FIX (refuter): Rename the test so that it states the outcome and keeps the reason, for example A_deferred_length_is_not_taken_again_when_the_row_the_section_keeps_for_it_is_of_a_reserved_type. The reviewer's A_deferred_length_is_not_taken_again_when_the_first_row_numbering_it_is_of_a_reserved_type also works. The body and comment stay as they are. This is a test-only rename and not a maintainer design choice.

######## [36] tests:name-this-rule — reviewer nit; refuter narrowed nit maintainer=False
LOCATION: tests/AdCodicem.Pdf.Tests/CrossReferenceRuleTests.cs:1034
CLAIM: `A_stream_two_tables_name_is_no_fault_of_this_rule` does not say which rule. The class covers every cross-reference rule, and the body judges two: all findings, then xref.hybrid-stream-wrong.
EVIDENCE: The body is `Validate(HybridFiles.SharedStream(olderSize: 7).File).Findings.Should().BeEmpty(); Validate(HybridFiles.SharedStreamNamedShort().File).Contains(PdfValidationRuleIds.XRefHybridStreamWrong).Should().BeFalse();`.
FIX (reviewer): Rename it to, for example, `A_stream_two_tables_name_is_no_xrefstm_naming_a_wrong_stream`.
--- VERDICT REASON: The finding holds, with one narrowing. The test is new in this batch (commit 7d35896, "fix: record each table's naming of a hybrid stream and report one that names no stream"). Its class covers every rule of the xref family, as its summary says ("each rule on a file that breaks it..."). "this rule" only means xref.hybrid-stream-wrong because of the two tests just above it. In a runner's output, or under a filter, the name does not say which rule. Its first assertion is also stronger than the name: that file has no finding of any rule. The narrowing: the "not this rule's" naming already existed on main 21a0298 in classes that also cover several rules: FileRuleTests:653 `A_catalog_without_type_that_holds_the_page_tree_is_not_this_rule_s` and PageTreeRuleTests:776 `A_count_that_is_no_integer_is_not_this_rule_s`. So the test follows existing practice and breaks no written convention. It is wording only, not a test that fails to hold its claim. Severity stays nit. The proposed rename (`..._is_no_xrefstm_naming_a_wrong_stream`) reads awkwardly. A name matching its siblings' "An_xrefstm_..._is_an_error" pattern is clearer.
VERDICT EVIDENCE: Branch head 071f829 (origin/ccr-6d6c0ee6-dqm4lm-188), tests/AdCodicem.Pdf.Tests/CrossReferenceRuleTests.cs:1034-1039:
  public void A_stream_two_tables_name_is_no_fault_of_this_rule()
  { Validate(HybridFiles.SharedStream(olderSize: 7).File).Findings.Should().BeEmpty();
    Validate(HybridFiles.SharedStreamNamedShort().File).Contains(PdfValidationRuleIds.XRefHybridStreamWrong).Should().BeFalse(); }
The two tests before it (lines 990 and 1020) are the xref.hybrid-stream-wrong tests. The class summary (lines 13-16) says the class covers the whole xref family.
Probe ZzNameProbe (uncommitted, now removed) ran PdfValidator on both fixtures. Output:
  --- file            (SharedStream(olderSize: 7): no finding)
  --- file
  xref.offset-imprecise Warning An offset names the white space before what it designates rather than its first byte: /XRefStm gives offset 389, 2 bytes before the header of the cross-reference stream.
So the first assertion covers every rule, and the second is narrowed to the one rule because the file legitimately has another finding.
The test passes on the branch (2/2 passed together with the probe).
`git show 21a0298:...FileRuleTests.cs` line 638 and `...PageTreeRuleTests.cs` line 775 already use the `_is_not_this_rule_s` naming in multi-rule classes. The new test is in `git log 21a0298..HEAD -S` only through 7d35896.
FIX (refuter): Rename the test after its siblings so it names the rule's subject and stands on its own, for example `An_xrefstm_copied_into_the_next_trailer_is_no_error` or `An_xrefstm_the_next_table_copies_names_no_wrong_stream`. Optionally add a `because` to the second assertion naming the copy's xref.offset-imprecise, which is why only xref.hybrid-stream-wrong is checked there. The two older "_is_not_this_rule_s" names on main can stay as they are, or be renamed in the same way in a style pass. Either way, this is not something the maintainer has to decide.

######## [37] tests:process-reference-batch-16 — reviewer nit; refuter refuted none maintainer=False
LOCATION: tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:2389
CLAIM: A new test comment cites a work batch rather than an issue: 'as batch 16 settled (#263)'. It is not one of the measurement or review keys, and main has the same habit in two places (HostileInputTests:3807, PageTreeRuleTests:414), but the issue number alone carries the reference.
EVIDENCE: `// The update's /XRefStm names a classic table no /Prev names: its rows are read in the stream's place, as batch 16 settled (#263), and the naming, ...`
FIX (reviewer): Write it as '..., as #263 settled, ...'.
--- VERDICT REASON: The facts are right, but they are not a defect. On origin/ccr-6d6c0ee6-dqm4lm-188 (071f829), tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:2389-2390 does say "as batch 16 settled (#263)". It is the only new mention of a batch number in src/ or tests/. Main 21a0298 has the same kind of comment at HostileInputTests.cs:3798 ("(batch 13)") and PageTreeRuleTests.cs:413 ("Batch 13's shape (#129)"). Still, "batch N" is not a review key or a measurement key. The brief bans B19-, S1..S10 used as shape names, P1b, P2, b2t, c9b, A-prime, N-DUP, N-TND and R1-. "batch N" is the project's own term, defined in committed ADR 53 at line 14: "the batches are the pull requests in which M02 pays the debt filed under it". docs/status.md maps each batch to its issues and pull request; its journal entry "2026-10-25 — Batch 16: ... ([#170], [#174], [#220], [#224], [#230], [#263])" names #263 among them. Committed docs on main cite batches as provenance the same way: corpus-contributions.md:310 "(batch 12, [#165], [#166])", and many lines of status.md and ADR 52/53. The branch's new corpus-contributions row follows that pattern with "(batch 19)". Neither CLAUDE.md nor the brief has a rule against naming a batch in a comment, and the finding admits it is not one of the banned keys. The citation is also accurate. "A table an /XRefStm names in a stream's place" was settled under #263, as on main: threat-model.md:419 and validation-rules.md:48 tag it [#263], and the test GuardCutValueTests.A_table_an_xrefstm_names_in_a_stream_s_place_has_its_own_links_left_unread holds it. The comment follows a documented, traceable project convention. Its issue number is already present, so nothing is lost or misleading.
VERDICT EVIDENCE: Branch head 071f829, CrossReferenceChainTests.cs:2389: "// The update's /XRefStm names a classic table no /Prev names: its rows are read in the stream's place, as batch 16 / settled (#263), ...". On main 21a0298, `git grep -i '\bbatch\b' -- src tests` finds HostileInputTests.cs:3798 "(batch 13)" and PageTreeRuleTests.cs:413 "Batch 13's shape (#129)". The term is defined at docs/adr/0053-...md:14: "Since batch 5 — the batches are the pull requests in which M02 pays the debt filed under it —". docs/status.md:1003: "### 2026-10-25 — Batch 16: where a stream's data ends, and what a guard's cut leaves ([#170], [#174], [#220], [#224], [#230], [#263])". docs/corpus-contributions.md:310 on main: "(batch 12, [#165], [#166])". docs/threat-model.md:419 on main: "nor are the /Prev and /XRefStm of a table an /XRefStm names in a stream's place ... ([#263])". None of the review-brief's banned key patterns matches "batch N".
FIX (refuter): None needed. If the maintainer wants code comments to cite issues only, that would be a new project-wide convention. It would also cover main's two comments (HostileInputTests.cs:3798, PageTreeRuleTests.cs:413) and belongs in a separate change, not in this batch.

######## [38] docs:guard-judging-overclaim — reviewer minor; refuter confirmed minor maintainer=False
LOCATION: docs/adr/0034-every-valid-pdf-is-readable-and-the-readers-guards-are.md:226-228; docs/website/docs/concepts/reader-limits.md:77-84; docs/milestones/M02.md:112-113
CLAIM: Three texts say the links of any section a guard stopped are judged. ADR 34's Decision bullet attaches "what the section it ends at names is judged all the same, as far as the guard lets it be" to every cut it lists, and that list includes a cross-reference stream whose /W, /Index, /Filter or /DecodeParms leads to an object MaxObjectLength cut. The concepts page says "a guard never hides damage that can be seen without reading past it", right after a paragraph about exactly those value-cut streams. M02 says "what a section a guard stopped names is judged". The code judges the links only under the three rows guards (#264). On a CutByValue section (and on FaultBesideCut with rows), a /Prev that lies whole in a dictionary read whole is never judged. This is residual [9-guards], which is unfiled.
EVIDENCE: Probe ZzCutByValuePrev (copy at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/ZzDocsProbe.cs.txt). File: ChainFiles.UnderAnUpdate("/Type /XRef /Size 8 /W 5 0 R /Prev 99999999 /Length {length}", "[1 4 2]" + 3000 spaces, placed: true). Read whole: `Warning xref.section-missing at 3226: The cross-reference section /Prev names at offset 99999999 lies outside the file, which is 3,507 bytes long; ...`, `Repair xref.rebuilt`, finding `Error xref.section-not-found at offset 3226`. With MaxObjectLength = 1024: only `Warning limit.object at 203: Object 5 runs past 1 KB ...` and finding `Information xref.checked-in-part at offset 3226`. WasRepaired False. Nothing reports the /Prev outside the file.
FIX (reviewer): Scope the three texts to what #264 implements: a section whose rows MaxXRefSectionLength or MaxDecodedStreamLength cut, or MaxIndexEntries filled at them, as the threat model's #264 row, reference/reader-limits.md and the amendment paragraph at ADR 34:139 already do. File a debt under M02 for the CutByValue and FaultBesideCut paths, whose links stay unjudged.
--- VERDICT REASON: The finding holds. I rebuilt it on origin/ccr-6d6c0ee6-dqm4lm-188 (071f829) with my own probe.

The code judges a guard-stopped section's /Prev and /XRefStm only on the paths #264 named:
- ReadTrailerPastRows calls JudgeLinksPastGuard;
- the stream reading sets _previousPastGuard under `rowsCut || _indexFullAt >= 0`;
- the /XRefStm branch sets pastGuard for a hybrid stream a guard stopped.

Two paths return before any SectionOffset runs, so their direct /Prev is never judged:
- CutByValue (PdfFileReader.cs:5490, reached from :4924 and :4980), which returns Malformed;
- FaultBesideCut when the data holds rows (PdfFileReader.cs:4962-4967), which returns Read.

The trailer dictionary of both is read whole. A /Prev that is no offset, lies outside the file, or names the section itself needs nothing past the guard to judge, but it goes unreported.

Three texts, all added by this batch (294d550 and 071f829; absent from main 21a0298), say otherwise:
- **ADR 34 Decision bullet, lines 222-228.** "what the section it ends at names is judged all the same, as far as the guard lets it be" is attached to every cut in its list. That list includes a stream whose /W, /Index, /Filter or /DecodeParms leads to an object MaxObjectLength cut, and a trailer cut. The ADR's own amendment paragraph at lines 133-140 scopes the change to rows guards and says the trailer-cut case is #328, so the ADR also contradicts itself.
- **concepts/reader-limits.md, lines 77-84.** It speaks of "the section a guard stopped", right after a paragraph about value-cut streams that end the chain "as a section a guard cut does". It then states, without exception, "a guard never hides damage that can be seen without reading past it". The CutByValue and FaultBesideCut paths contradict that, and so does the filed #328, which the page does not mention.
- **M02.md, lines 112-113.** "what a section a guard stopped names is judged" is the weakest of the three: a one-line summary that links #264, which names the three rows guards. Still, it reads as general.

Texts that are correctly scoped to "a section whose rows a limit cut, or filled the index at":
- reference/reader-limits.md:62-67;
- reference/diagnostics.md, the xref.section-missing, xref.chain-cycle and xref.hybrid-stream-lost rows;
- reference/validation-rules.md, the xref.section-not-found, xref.chain-loop and xref.hybrid-stream-wrong rows;
- the threat model's #264 row.

Supporting facts:
- Issue #264's "What closes it" names only the three rows guards, and decisions.md records "#264 P1 ... under rows guards".
- The gap itself is older than this batch, out of #264's scope, and not a regression. The batch's fault is a misleading text plus a residual nobody filed: [9-guards] says so in residuals-impl.md and is not among #320-#328.

Severity is minor: documentation that misstates the code.
VERDICT EVIDENCE: Probe ZzGjoProbe (uncommitted, since removed). Each file is built with `ChainFiles.UnderAnUpdate(entries, five, compressed: false, predicted: false, placed: true)`. Each is opened with default limits, then with `MaxObjectLength = 1024`, and validated.

**CutByValue: entries "/Type /XRef /Size 8 /W 5 0 R /Prev 99999999 /Length {length}", five "[1 4 2]" + 3000 spaces.**
- Whole read:
  - `Warning xref.section-missing at 3226: The cross-reference section /Prev names at offset 99999999 lies outside the file, which is 3,507 bytes long; ...`
  - `Repair xref.rebuilt`
  - finding `Error xref.section-not-found at offset 3226 ... the offset lies outside the file.`
- Guarded (WasRepaired False):
  - `Warning limit.object at 203: Object 5 runs past 1 KB, ...`
  - finding `Information xref.checked-in-part at offset 3226`
  - Nothing else.

**Same file with "/Prev (x)".**
- Whole read: `xref.section-missing ... is not an offset`, plus `Error xref.section-not-found ... a value of type string, not an offset`.
- Guarded: only `limit.object` and `xref.checked-in-part`.

**Same file with /Prev naming the stream itself (3226).**
- Whole read: `Warning xref.chain-cycle at 3226 ... names offset 3226, which the chain has already read`, plus `Error xref.chain-loop`.
- Guarded: only `limit.object` and `xref.checked-in-part`.

**Control, "/Prev 0" (in the file, not reached).** Guarded: only the guard's reports. This is correct, since an offset in the file the chain has not reached stays the guard's.

**FaultBesideCut with rows: entries "/Type /XRef /Size 100 /W [1 5 0 R 2] /Prev 99999999 /Length {length}", five "4" + 3000 spaces.**
- Whole read:
  - `xref.section-unreadable ... holds 8 rows where its /Index and /Size declare 100`
  - `Warning xref.section-missing at 3220: The cross-reference section /Prev names at offset 99999999 lies outside the file ...`
  - finding `Error xref.section-not-found ... outside the file`
- Guarded:
  - `xref.section-unreadable ... holds at most 18 of the 100 rows`
  - `limit.object`
  - `xref.rebuilt`
  - findings `xref.section-malformed` and `xref.checked-in-part`
  - No xref.section-missing and no xref.section-not-found.

The texts at the branch head:
- ADR 34:227: "what the section it ends at names is judged all the same, as far as the guard lets it be, without reading past it (amended on 2026-11-04)", inside the bullet that lists the /W, /Index, /Filter and /DecodeParms value cuts and the trailer cut.
- concepts/reader-limits.md:77-84: "a guard never hides damage that can be seen without reading past it".
- M02.md:112-113: "what a section a guard stopped names is judged as far as the guard lets it be ([#264])".
FIX (refuter): Scope the three texts to what #264 implements, in the words the amendment paragraph (ADR 34:133-140) and reference/reader-limits.md already use.

1. **ADR 34 Decision bullet (lines 226-228).** Replace "what the section it ends at names is judged all the same, as far as the guard lets it be, without reading past it (amended on 2026-11-04)" with: "where a guard stopped its rows — MaxXRefSectionLength or MaxDecodedStreamLength cut them, or MaxIndexEntries filled at them, a hybrid table's stream among them —, what the section it ends at names is judged all the same, as far as the guard lets it be, without reading past it (amended on 2026-11-04); the links of a section a value's cut ends the chain at, and of a trailer MaxTrailerLength cut (#328), are not judged yet (#new)".

2. **concepts/reader-limits.md:77-84.**
   - Say "ends at the section whose rows a guard stopped".
   - Drop the unqualified "a guard never hides damage that can be seen without reading past it", or scope it to such a section.
   - Add one sentence saying that a section a value's cut ends the chain at, and a trailer a guard cut, have their links left to the guard for now, linking #328 and the new debt.

3. **M02.md:112-113.** Write "and what a section whose rows a guard stopped names is judged as far as the guard lets it be ([#264])".

4. **File a debt labeled `debt` under M02.** A cross-reference stream that ends the chain through CutByValue, or through FaultBesideCut with rows > 0, never has its /Prev or /XRefStm judged. Include the reproducers above: /Prev 99999999, /Prev (x), and /Prev naming itself, each under MaxObjectLength 1024, against the whole read's xref.section-missing/section-not-found and chain-cycle/chain-loop. Then cite it from the threat model's "known debt" list beside #328.

None of this is a design choice. It follows the decided #264 scope and the session's rule to file what is found and left. Extending the judging to those two paths in this batch would be a new scope, and that choice is the maintainer's.

######## [39] docs:first-fault-not-first — reviewer minor; refuter narrowed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:54-55; docs/website/docs/reference/diagnostics.md:112; docs/threat-model.md:421; commit 54d56c6 body
CLAIM: Four texts say the read-in-part report names the section's first fault: the XML doc ("the fault in its message — the first, where the section has several"), the reference ("its first fault"), the threat model's #210 row ("its first fault in the message") and the 54d56c6 body. The decision on #210 says "first fault" too. The code instead applies a priority (ReportSectionReadInPart's remarks: "one that lost rows, else the end of the file before its trailer, else the first row it refused"). When a refused row comes before a row that is no row, the message names the later fault and never mentions the refused object.
EVIDENCE: Probe ZzFirstFault: PartialFiles.RowEndingInX(false) with {row:3} changed to {row:3:70000}, so row 3 is refused (generation past 65,535) before row 4 ends in x. Output at opening: `Warning xref.section-unreadable at 162: The cross-reference table startxref names at offset 162 was read only in part: the row for object 4, at offset 251, is not an offset, a generation and n or f; the rows read were kept, and the objects the rest of that subsection numbers are looked for by rebuilding the index ...`. Object 3's refused row is not named.
FIX (reviewer): Either name the first fault in file order, as decided, keeping the first fault met, refused or not. Or make the four texts state the priority the code applies: a fault that lost rows first, then the end of the file, then the first refused row.
--- VERDICT REASON: The finding holds, though it is narrower than stated, and only the texts are wrong. On the branch head, ReportSectionReadInPart (src/AdCodicem.Pdf/IO/PdfFileReader.cs:3864-3902) checks section.Incomplete before section.RefusedRow. So when a section also lost rows, or the end of the file cut it, the message never names a row it refused. That holds even when the refused row comes first in the file.

Four texts say "first" with no qualifier:
- the public XML doc at PdfDiagnosticCodes.cs:54-55 ("the fault in its message — the first, where the section has several")
- reference/diagnostics.md:112 ("its first fault")
- threat-model.md:421 ("its first fault in the message")
- the body of commit 54d56c6

Only the private remarks at PdfFileReader.cs:3845-3846 state the priority. Even they call it "its first fault".

How the finding narrows:
- Faults that lose rows keep file order among themselves (`section.Fault ??=`, lines 4334/4352/4361/4420/4762). A row that is no row before the end of the file is named first. So the gap is only the refused row, and it is wider than the finding's one case: a refused row before the end of the file is not named either.
- The decision is not broken. It was "decided without a question", and the measured prototype (measure/partial/proto-ap-read-in-part.diff:64-96) already had this exact Incomplete-then-RefusedRow order and also called it "the first fault in the message". The code does what was measured; only the word "first" is wrong.
- Behavior is right. Object 3's refused row is still refused (Refuse at line 4782), and object 3 is found by a rebuild in every case below. The only cost is that the message does not mention it.
- Commit 54d56c6's body is pushed history and is left out of the fix.
VERDICT EVIDENCE: Probe ZzFirstFault on origin/ccr-6d6c0ee6-dqm4lm-188 (071f829), Release, run as an uncommitted test and then removed.

(1) PartialFiles.RowEndingInX(false) with {row:3} changed to {row:3:70000}:
`Warning xref.section-unreadable at 162: The cross-reference table startxref names at offset 162 was read only in part: the row for object 4, at offset 251, is not an offset, a generation and n or f; the rows read were kept, and the objects the rest of that subsection numbers are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows.`
Object 3 reads "object 3", and then xref.rebuilt is reported. RowEndingInX(true) gives the same text with "/Prev".

(2) The same file with row 4 made sound, so the refused row is the only fault:
`... was read only in part: the row for object 3, at offset 231, gives it generation 70000, outside 0 to 65535; that row alone was refused, ...`
This shows the refused row is named only when it is alone.

(3) PartialFiles.CutUpdate("3 2\n{row:3:70000}\n{row:4}\n"), a refused row before the end of the file:
`Warning xref.section-unreadable at 346: The cross-reference table /Prev names at offset 346 was read only in part: the file ends before its trailer; the rows read were kept, and what only the rest of the table, or the sections its trailer would name, index is looked for by rebuilding the index.`
The refused row is not named. This text is the same as for the cut table with no refused row.

Code: PdfFileReader.cs:3868 `if (section.Incomplete)` comes before :3877 `else if (section.RefusedRow is { } refused)`. Its remarks at :3845-3846 say "its first fault in the message: one that lost rows, else the end of the file before its trailer, else the first row it refused".
FIX (refuter): Keep the code and make the texts state the priority it applies. This is the order the decision was measured on, and the fault that lost rows is the one the message's tail (what is looked for by rebuilding) describes.

- PdfDiagnosticCodes.cs:54-55: replace "the fault in its message — the first, where the section has several" with something like "one fault in its message: the first that lost rows, else the end of the file before its trailer, else the first row it refused — a row refused is not named beside either of the others".
- reference/diagnostics.md:112: replace "its first fault" with the same priority.
- threat-model.md:421: replace "its first fault in the message" with the same priority.
- PdfFileReader.cs:3845 (optional): replace "its first fault" with "one fault", so the private remarks do not call a priority "first".

Leave the commit 54d56c6 body as it is. No maintainer question is needed: the "first fault" wording came from the measurement, not from the maintainer.

Naming the first fault in file order, or adding "and a row before it was refused" to the message, would be a change to the message. It is not needed to make the texts true.

######## [40] docs:tm-323-gap-says-read-whole — reviewer minor; refuter confirmed minor maintainer=False
LOCATION: docs/threat-model.md:469-470
CLAIM: The new known gap for #323 says "When startxref names a section that is there and cannot be read whole, the index is rebuilt as the document opens and only xref.rebuilt is reported". In this batch's own wording, "cannot be read whole" covers a section read only in part (the XRefSectionUnreadable XML doc). Such a section, when startxref names it, is kept, reported and not rebuilt at opening, as lazy-reading.md and #323's own body say. The gap is the section that cannot be read at all.
EVIDENCE: Probe ZzCutFirst / ZzFirstFault, at opening: PartialFiles.CutFirst gives `Warning xref.section-unreadable at 309: The cross-reference table startxref names at offset 309 was read only in part: the file ends before its trailer; ...`. The refused-row shape gives `WasRepaired False` with the single `Warning xref.section-unreadable at 162 ... was read only in part`. No xref.rebuilt appears at opening.
FIX (reviewer): Write "a section that is there and cannot be read" (or "cannot be read at all"), matching #323's title and its "A first section that cannot be read at all is the case left".
--- VERDICT REASON: On the branch head (071f829), docs/threat-model.md:469-470 says the #323 gap covers a startxref section "that is there and cannot be read whole". In this batch's own vocabulary, "cannot be read whole" is the umbrella for two cases. The XRefSectionUnreadable XML doc (PdfDiagnosticCodes.cs:36) and the diagnostics.md row both start "A cross-reference section the chain names cannot be read whole", and then cover both "cannot be read" and "was read only in part". The code treats the second case the other way. When startxref names a section read only in part, the section is kept and reported as xref.section-unreadable "was read only in part", and nothing is rebuilt at opening. That follows decision #210 ("startxref's section read in part not rebuilt at opening") and B19-3, and the same file says so at line 421 ("the one `startxref` names among them ... nothing is rebuilt at opening for it"). So the gap line states the wrong behavior for the read-in-part case and contradicts line 421 of the same document. The narrow wording is used everywhere else: lazy-reading.md:127-128 ("as for any first section that cannot be read"), the diagnostics.md row ("The section `startxref` names that cannot be read hands nothing on ... (#323)") and the title of #323 ("cannot be read"). #323's body also says "A first section that cannot be read at all is the case left". Its first "What closes it" bullet does say "cannot be read whole", but that bullet describes the future fix and does not describe today's behavior. This is misleading text that misstates the code, which the brief grades minor. It is not a nit, because a reader of the threat model would expect a read-in-part first section to be rebuilt with no report, and the opposite happens.
VERDICT EVIDENCE: Probe on origin/ccr-6d6c0ee6-dqm4lm-188 (071f829), Release build: PdfDocument.Open, then WasRepaired and Diagnostics printed at opening.
== CutFirst: WasRepaired False
  Warning xref.section-unreadable at 309: The cross-reference table startxref names at offset 309 was read only in part: the file ends before its trailer; the rows read were kept, and what only the rest of the table, or the sections its trailer would name, index is looked for by rebuilding the index.
  Repair trailer.root-recovered at -1: ...
== RowEndingInX(false): WasRepaired False
  Warning xref.section-unreadable at 162: The cross-reference table startxref names at offset 162 was read only in part: the row for object 4, at offset 251, is not an offset, a generation and n or f; ...
== StreamOverOlderRows rows 2: WasRepaired False
  Warning xref.section-unreadable at 403: The cross-reference stream startxref names at offset 403 was read only in part: it holds 2 rows where its /Index and /Size declare 3; ...
== StreamOverOlderRows /W [1 -4 2] (section that cannot be read at all): WasRepaired True
  Repair xref.rebuilt at -1: The cross-reference index was rebuilt by scanning the file.
  Information object.redefined at -1: ...
Only the last shape, a section that cannot be read at all, matches the gap text. The branch's own tests hold the read-in-part behavior and pass: CrossReferenceChainTests.The_table_startxref_names_that_the_end_of_the_file_cuts_after_a_row_is_kept_and_nothing_rebuilt_at_opening, and A_table_whose_last_row_is_no_row_is_reported_read_in_part_once_whether_prev_or_startxref_names_it(false), which asserts a single xref.section-unreadable and WasRepaired false at opening. The finding is about docs added in this batch only (git diff 21a0298..HEAD adds line 469), so main gives no different output to compare.
FIX (refuter): In docs/threat-model.md:469-470, write: "- [#323] When `startxref` names a section that is there and cannot be read at all, the index is rebuilt as the document opens and only `xref.rebuilt` is reported, not why: the section's fault reaches the validator alone." Optionally add "(a section read only in part is kept and reported, [#210])" so that line 421 and the gap read as one. If the closing bullet of #323 is touched later, it could say "cannot be read" as well, but that is not needed for this batch.

######## [41] docs:reference-to-a-row-read — reviewer minor; refuter narrowed minor maintainer=False
LOCATION: docs/website/docs/reference/diagnostics.md:111; docs/website/docs/reference/reader-limits.md:67-68; docs/adr/0034-every-valid-pdf-is-readable-and-the-readers-guards-are.md:143-144; docs/website/docs/reference/validation-rules.md:56
CLAIM: Each text lists "a reference to a row the reader read" among the values past a rows guard that "can name no section whatever the guard hid" and are reported as read whole. A reference whose row was read and whose object holds an offset in the file that the chain has not reached is the guard's, and nothing is reported. Only a reference to a free row is reported, which is how the 294d550 body words it ("a reference to a free row the reader had read").
EVIDENCE: Probe ZzReferenceToARowRead, StoppedSectionFiles.Stopped("/Prev 21 0 R", twentyOne: ...). Object 21 = {older} under "table rows" gives only `Warning limit.xref-section-length at 510 ...`, WasRepaired False. Under "table index" it gives only `Warning limit.index-entries at 510`. With a free row 21 it gives `Warning limit.xref-section-length at 483` and `Warning xref.section-missing at 483: The /Prev of the cross-reference section at offset 483 is not an offset; ...`. With object 21 = 99999999 it gives `xref.section-missing ... lies outside the file`.
FIX (reviewer): Write "a reference to a free row the reader read", or "a reference that leads, through rows the reader read, to no offset or to one outside the file", in the four places.
--- VERDICT REASON: The finding holds. Four texts added in 294d550 list "a reference to a row the reader read" among the values past a rows guard that can name no section and that a whole read would report. The code does not treat such a reference as a category of its own. SectionOffset (src/AdCodicem.Pdf/IO/PdfFileReader.cs:6041) resolves the reference through the rows that were read, and TryReadXRefChain then judges the value it gets as it would judge a direct value. If object 21 holds an offset inside the file that the chain has not reached, the reference stays the guard's: only the limit is reported, nothing is rebuilt and TrailersHiddenByGuard is true. That is the right behaviour, since a whole read follows the same reference to a sound older table and reports nothing. Only the docs are wrong. The claim needs one correction: it is not only a reference to a free row that is reported. A reference through a row that was read is reported whenever the value it leads to is not an offset (a free row, /Garbage), lies outside the file, or names a section the chain has already reached (a loop). The reviewer's second wording leaves the loop case out. Three other texts already say it correctly: the 294d550 commit body ("a reference to a free row the reader had read"), threat-model.md ("a free row") and the comment in ReaderLimitsTests.A_section_a_guard_stops_reports_the_faults_of_its_prev_a_whole_read_reports ("a reference to a free row the guard let the reader read"). concepts/reader-limits.md does not mention references at all. All four texts are new in this batch (294d550), so main has nothing to compare against. Locations on the branch head: docs/website/docs/reference/diagnostics.md:111, docs/website/docs/reference/reader-limits.md:67-68, docs/adr/0034-every-valid-pdf-is-readable-and-the-readers-guards-are.md:143-144, docs/website/docs/reference/validation-rules.md:56 ("a reference to a row read").
VERDICT EVIDENCE: Reproduced on origin/ccr-6d6c0ee6-dqm4lm-188 (071f829) with a throwaway probe (now deleted). The probe called StoppedSectionFiles.Stopped(links, stream, twentyOne: ...) and opened each file under StoppedSectionFiles.Guard(guard).

Object 21 = {older} (an offset in the file the chain has not reached):
- "/Prev 21 0 R", guard "table rows". Whole read: no diagnostics, WasRepaired False. Guarded: only `Warning limit.xref-section-length at 510: The cross-reference table runs past 128 bytes; ...`; WasRepaired False, TrailersHiddenByGuard True. Rules: only file.trailer-value-wrong (reference form) and xref.checked-in-part, no xref.section-not-found.
- "table index": only `Warning limit.index-entries at 510`.
- "stream rows": only `Warning limit.decoded-stream at 645`.
- "stream index": only `limit.index-entries at 510`.
- "/XRefStm 21 0 R", guards "table rows" and "table index": only the limit. The whole read reports xref.hybrid-stream-lost at 199.

The reported cases:
- Free row 21, "table rows" or "table index": the limit, then `Warning xref.section-missing at 483: The /Prev of the cross-reference section at offset 483 is not an offset; ...`, then xref.rebuilt.
- Object 21 = 99999999, "table rows": `xref.section-missing at 508: ... at offset 99999999 lies outside the file`.
- Object 21 = /Garbage, "table rows": `xref.section-missing at 508: The /Prev ... is not an offset`.
- Object 21 = 0000000510 (the section's own offset), guards "table rows", "table index" and "stream rows": `Warning xref.chain-cycle at 510: ... names offset 510, which the chain has already read`, then xref.rebuilt, plus rule xref.chain-loop.

So a reference whose row was read is judged by the value it leads to, not reported as a reference.
FIX (refuter): In each of the four places, classify the reference by the value it leads to, not by whether its row was read. Suggested wording for the list of values that can name no section: "a value that is no offset — a reference to a free row the reader read among them —, an offset outside the file, or a section the chain has reached, written directly or as a reference the reader follows through rows it read". Keep the guard's side as it is: "an offset in the file the chain has not reached, a reference to a row past the cut", and say that a reference leading to such an offset is the guard's too. validation-rules.md:56 needs "a reference to a row read" changed the same way. In ADR 34's amendment (lines 143-144), "a reference to a row the reader read" becomes "a reference to a free row the reader read", and the sentence says that a reference through rows that were read is judged as the value it leads to. Optionally, add a row with object 21 = {older} to A_link_a_section_a_guard_stops_gives_that_may_be_sound_stays_the_guard_s, so a test holds the corrected text. This is a docs-only fix and needs no design choice.

######## [42] docs:b19-7-status-record — reviewer minor; refuter narrowed minor maintainer=False
LOCATION: docs/status.md:2900
CLAIM: Decision B19-7 names four texts that should say the stream-end rule now follows the scan. Three were updated: HeaderText remarks, concepts/diagnostics.md and the syntax.truncated-object row of reference/diagnostics.md. The fourth, status.md's record of 2026-09-30, still states the old rule.
EVIDENCE: `git diff --stat 21a0298..origin/ccr-6d6c0ee6-dqm4lm-188 -- docs/status.md` is empty. docs/status.md:2900 still reads "a header with a comment between its tokens is not taken, as in the rebuild's scan (maintainer, 2026-09-30)".
FIX (reviewer): In the batch's status.md pass (task #26), add to that record that batch 19 (#261, B19-7) made the rule and the scan read comments between a header's tokens. Do not leave it for later.
--- VERDICT REASON: The claim is accurate. B19-7 names four texts that must say the stream-end rule now follows the scan. Three of them changed on the branch: the FindHeader remarks in HeaderText.cs, concepts/diagnostics.md, and the syntax.truncated-object row of reference/diagnostics.md (now at line 125, not 116). reference/validation-rules.md, threat-model.md and M02.md changed too. docs/status.md was not touched by the batch, and line 2900 still states the old rule, which is now false. The flipped StreamLengthTests rows prove it: "12 0 %\nobj" and "12 %\n0 obj" go from null to 0. The finding is narrower than it reads, though. This is not something the implementation stages missed. Every stage leaves status.md to the end-of-batch step by design, and the stage-4 report already hands this exact line to that step. That step has not run yet. The text misleads on the branch head as it stands, and the risk is that the final pass adds a batch-19 record without amending the 2026-09-30 one. If the branch merged that way, maintainer decision B19-7 would go unimplemented.
VERDICT EVIDENCE: On origin/ccr-6d6c0ee6-dqm4lm-188 (071f829), `git diff --stat 21a0298..HEAD -- docs/status.md` prints nothing. Line 2900 of docs/status.md reads "...a header with a comment between its tokens is not taken, as in the rebuild's scan (maintainer, 2026-09-30)." A grep of docs/ for "comment(s) between" finds that line as the only remaining statement of the old rule. Every other text now says comments are read between the tokens: threat-model.md:377, :432, :491 and :514; M02.md:106; lazy-reading.md:139 and :173; reference/diagnostics.md:125. In tests/AdCodicem.Pdf.Tests/StreamLengthTests.cs the batch's diff changes `[InlineData("12 0 %\nobj", null)]` to `0` and `[InlineData("12 %\n0 obj", null)]` to `0`. In the scratchpad, impl-result.json:358 (stage 4's open_points) says: "docs/status.md is the final stage's to update: record #261 and the stream-end rule extension. status.md:2900 still says a header with a comment between its tokens is not taken." decisions.md B19-7 lists "status.md's record of 2026-09-30" as one of the four texts.
FIX (refuter): In the end-of-batch status.md pass, amend the 2026-09-30 record in place; adding a new batch-19 entry is not enough. Replace "a header with a comment between its tokens is not taken, as in the rebuild's scan (maintainer, 2026-09-30)" with wording such as "a header is read as the rebuild's scan reads it (maintainer, 2026-09-30); since batch 19 ([#261]) both read comments between its tokens as a load does". In the batch-19 record, also note the flipped rows ('12 0 %\nobj', '12 %\n0 obj') and the difference that remains: '% 1 0 obj' on the obj keyword's own line is still taken. Add the [#261] link definition if status.md lacks one. B19-7 already settles this, so the maintainer has nothing to decide.

######## [43] docs:cidfont-at-startxref — reviewer minor; refuter narrowed minor maintainer=False
LOCATION: docs/website/docs/reference/validation-rules.md:43,56; src/AdCodicem.Pdf/Validation/Rules/SectionMalformedRule.cs:40
CLAIM: The rules reference says the section startxref names is file.startxref-wrong's, and that an object giving a /W and another /Type (a CIDFont) holds no section (#281). When startxref names such an object, the validator reports xref.section-malformed and calls the font "The cross-reference stream at offset X". file.startxref-wrong is not reported. #281 converted the record only for /Prev and /XRefStm. This is residual [5-relocation]'s neighbor of #323, which is unfiled. It is the same on main.
EVIDENCE: Probe ZzStartxrefNamesCidFont: a one-page HandWrittenFile whose startxref names object 6. With `<< /Type /Font /Subtype /CIDFontType2 /W [0 [500]] /BaseFont /F >>`, branch and main give the same output: `Repair xref.rebuilt`, finding `Error xref.section-malformed at offset 203: The cross-reference stream at offset 203 cannot be read: its dictionary is not followed by stream data.` A /Type /Font stream with /W [1 -4 2] gives `... cannot be read: its /W gives a field a negative width.`
FIX (reviewer): File a debt under M02, or add a comment to #323 since its fix rewrites that path: startxref naming an object of another /Type should take the TypedOther conversion (file.startxref-wrong, "holds object 6, which is not a cross-reference stream"). Until then, make the file.startxref-wrong row say what is reported.
--- VERDICT REASON: The behavior reproduces exactly, and it is the same on main 21a0298. It is narrower than claimed in one respect. No sentence of validation-rules.md states the startxref case outright. The #281 sentence at :56 belongs to the section-not-found row, which covers an offset a /Prev or /XRefStm names. diagnostics.md:111 and PdfDiagnosticCodes.cs:24-30 have the same scope.

What is misleading is the combination of three texts:
- the docs define a CIDFont that does not read as a section as holding none;
- the file.startxref-wrong row (:43) covers 'the offset startxref gives holds no cross-reference section';
- the section-malformed row (:54) covers a section that is 'an xref keyword, or a cross-reference stream'.
Read together, they lead to file.startxref-wrong. The code gives xref.section-malformed and calls the font 'The cross-reference stream at offset 203'.

The internal XML doc of XRefSectionRecord.TypedOther (XRefSectionRecord.cs:114-120) says in general that such an object 'is the section only where the chain names it and it reads as one ... otherwise it holds no section (#281)'. startxref's record contradicts it: TypedOther=True, State=Malformed, Kind=Stream.

Cause: TryReadXRefChain returns false for the first section at PdfFileReader.cs:3162-3165, before ReportLostSection, whose block at :3778-3786 does the TypedOther conversion. So the conversion runs only for /Prev and /XRefStm.

The batch did not regress the output. Main already gave the same wrong report for /Prev and startxref alike. What the batch did was fix /Prev and /XRefStm and document that fix, which leaves startxref asymmetric. The critic's summary itself calls this message false ('keeps a false message about a cross-reference stream').

It is not covered by #323. #323 is about the reader reporting only xref.rebuilt for an unreadable startxref section, and #323 has no comments. This residual ([5-relocation], [11-docs]) is unfiled.

The issue is wider than claimed: a font dictionary that gives a key twice is also judged as a trailer at startxref. The /Prev side was cured of exactly that by #281 ('its dictionary is no trailer to judge'). Minor rather than major: same severity (Error) under either rule, no regression, a known residual.
VERDICT EVIDENCE: Probe ZzStartxrefNamesCidFont, now removed. It is a HandWrittenFile, version 1.5, with objects 1-3 (catalog, pages, page), object 6 at offset 203, and then `startxref\n203\n%%EOF`. Branch 071f829 and main 21a0298 give identical output except where noted.

- cidfont `<< /Type /Font /Subtype /CIDFontType2 /W [0 [500]] /BaseFont /F >>`:
  - diag `Repair xref.rebuilt`, then `Repair trailer.root-recovered`;
  - section `startxref 203 Malformed Stream` (TypedOther=True on the branch), Fault 'its dictionary is not followed by stream data';
  - finding `Error xref.section-malformed @203: The cross-reference stream at offset 203 cannot be read: its dictionary is not followed by stream data.`
- Same font, with a sound table also written before startxref: identical finding.
- fontstream `<< /Type /Font /W [1 -4 2] /Size 8 /Length 3 >>` + stream: `Error xref.section-malformed @203: The cross-reference stream at offset 203 cannot be read: its /W gives a field a negative width.`
- fontstream with `/Size 8 /Size 8`:
  - diag `Warning syntax.key-repeated`, then `Repair xref.rebuilt`;
  - finding `Error file.trailer-malformed @203: The dictionary of the cross-reference stream at offset 203 is not well formed: the reader read it despite syntax errors.` (the font judged as a trailer, the same on main).
- Control `<< /Type /Foo /Bar 1 >>` (no /W): section `NotFound`, finding `Error file.startxref-wrong @242: startxref gives offset 203, which holds object 6, which is not a cross-reference stream.`
- Untyped `<< /W [0 [500]] /BaseFont /F >>`: xref.section-malformed, as documented for untyped objects.

The /Prev equivalent, An_object_of_another_type_the_prev_names_with_no_section_near_is_not_found (CrossReferenceRuleTests.cs:707-721), gives a single xref.section-not-found 'the offset holds object 6, which is not a cross-reference stream'.
FIX (refuter): Preferred: fix it in this batch. It is about 5 lines, follows the rule already decided for /Prev and /XRefStm, and needs no new decision.

1. Move ReportLostSection's TypedOther block (PdfFileReader.cs:3778-3786) into a small helper. That block sets State=NotFound, sets Fault 'holds object N, which is not a cross-reference stream', clears Trailer and resets TrailerFault=None. Call the helper from ReportLostSection, and also in TryReadXRefChain's `sections == 1` branch (:3162) before `return false`, when `section.TypedOther && !section.CutByLimit`.
2. StartXRefWrongRule then reports `startxref gives offset 203, which holds object 6, which is not a cross-reference stream.` No trailer is judged. The reader's report stays xref.rebuilt, which is #323's to change.
3. Add theory rows beside CrossReferenceRuleTests.cs:707 for startxref: the CIDFont dictionary and the font stream with /W [1 -4 2] and a key given twice. Each expects a single file.startxref-wrong finding.
4. Add a sentence to the file.startxref-wrong row (validation-rules.md:43) saying that an object giving a /W and another /Type holds none there either.
5. Run the corpus diff.

If it is not fixed here, add a comment to #323. Its fix, reporting xref.section-unreadable for startxref's section, would otherwise call the font an unreadable section on the reader side too, so the conversion must come first or with it. Until then, the TypedOther XML doc and the file.startxref-wrong row should say that startxref's section is not converted.

######## [44] docs:comment-header-on-obj-line — reviewer minor; refuter confirmed minor maintainer=False
LOCATION: docs/threat-model.md:442-443 (#171 gap); docs/website/docs/concepts/lazy-reading.md:173-174
CLAIM: This batch removed #261's known gap and says a rebuild finds headers "comments between the header's tokens included, as a load reads them". The scan still reads the obj keyword's own line by its digits, so a header inside a comment on that line (`% 4 0 obj`) is taken as a definition and served. The #171 gap lists headers "lying in stream data or strings", not comments. The only trace is a cited test name and the TryReadHeaderBackwards remarks. This is residual [4-comment-head], which is unfiled; the behavior predates the batch.
EVIDENCE: Probe ZzHeaderInsideComment: Sound with `4 0 obj\n(real)\nendobj\n% 4 0 obj\n(fake)\nendobj` and object 4's row 900 bytes off. Branch and main give the same: `Warning xref.entry-out-of-range ... object 4 ... offset 1027`, `Repair xref.rebuilt`, `Information object.redefined ... a second definition of object 4`, and GetObject(4) = "fake".
FIX (reviewer): Extend the #171 gap to "lying in stream data, strings, or a comment on its obj keyword's line", or file it as a debt. The lazy-reading sentence can stay, since it speaks of comments between tokens.
--- VERDICT REASON: The finding holds, and the gap is a little wider than it says: the search near a row takes the same false header, not only the rebuild. The behavior is older than this batch and is the same on main 21a0298, so this is a gap in the docs, not a regression.

How the code does it: TryReadHeaderBackwards (src/AdCodicem.Pdf/IO/PdfFileReader.cs:7216) lexes a line only when its backward walk crosses an end of line. The obj keyword's own line is therefore always read by its digits, and `% 4 0 obj` gives number 4, generation 0. A load would lex that whole line as a comment.

This is how P261-B was decided (measure/window/summary.md:323-334, critic :338), so no decision was broken. The batch's own test says the same thing in a code comment: The_line_of_an_obj_keyword_is_read_by_its_digits_whatever_else_it_holds, DocumentReaderTests.cs:432, which says "a header a comment holds is taken for one, as a header in a string is (#171)".

The threat model does not say it:
- The batch removed the #261 Known gap.
- The scan row (threat-model.md:432) now says comments are lexed "as a load reads it".
- The #171 gap (threat-model.md:442-444) still names only headers "lying in stream data or strings", even though this batch edited that same paragraph.
- The only traces are the cited test name and the remarks on TryReadHeaderBackwards ("as is the line of obj itself").

None of #320-#328 files it. Issue #171's body names stream data and strings. Its batch-9 comment asks that comments get the same treatment, but only for trailer keywords. Stage 4 and stage 11 reported this residual and left it unfiled.

The lazy-reading.md:173 sentence is accurate, since it is about comments between a header's tokens, and the user docs say nothing about #171. Severity is minor: a Known gaps list that leaves out one way another object is served under a number misleads, but no code is wrong against what was decided.
VERDICT EVIDENCE: Probe file (uncommitted, now deleted): catalog 1, pages 2, page 3, then `4 0 obj\n(real)\nendobj\n% 4 0 obj\n(fake)\nendobj\n`, with object 4's row 900 bytes past the real header.

Branch 071f829 and main 21a0298 give the same output:
```
GetObject(4) = "fake"; WasRepaired=True
Warning xref.entry-out-of-range: The entry of object 4 places it at offset 1103, outside the file.
Repair xref.rebuilt: The cross-reference index was rebuilt by scanning the file.
Information object.redefined: Rebuilding the index met a second definition of object 4. ...
```

Variant `% note\n%4 0 obj`: same result ("fake") on both.

Variant `% a 4\n0 obj`: main gives "fake", the branch gives "real". This is the part #261 paid.

Search near a row: object 4 written only as `% 4 0 obj\n(four)\nendobj`, its row at the `%`.
- Branch: `GetObject(4) = "four"; WasRepaired=False; Repair xref.offset-adjusted @205: Object 4 was found 2 bytes after where the index said.`
- Main: the same, worded "2 bytes from where".

Docs on the branch:
- threat-model.md:442-443: "A rebuild takes headers cut at a chunk's edge, or lying in stream data or strings, and serves another object under their number".
- The #261 gap is removed (diff 21a0298..HEAD).
- The scan row :432 cites The_line_of_an_obj_keyword_is_read_by_its_digits_whatever_else_it_holds but says nothing of what it means.
FIX (refuter): Edit docs/threat-model.md, the #171 Known gap (line 442), to name comments and the search near a row, for example: "A rebuild, and the search near a row, take headers cut at a chunk's edge, or lying in stream data, strings, or a comment on their `obj` keyword's line (`% 4 0 obj`, which a load reads as a comment), and serve another object under their number; ...".

In the same change, add a sentence to #171 saying this batch leaves a header a comment holds on its obj line in that issue's class, as the batch's test already says. A separate debt is not needed, since #171 already covers false headers in data and its batch-9 comment extends the choice to comments.

Leave lazy-reading.md:173 as it is. Optionally add "the line of `obj` itself is read by its digits" to the scan row at :432, so the defense column states the limit it cites.

######## [45] docs:trailer-cut-scope — reviewer nit; refuter confirmed nit maintainer=False
LOCATION: docs/adr/0034-every-valid-pdf-is-readable-and-the-readers-guards-are.md:149-150; docs/threat-model.md:416
CLAIM: Both texts say "A /Prev or an /XRefStm in a trailer MaxTrailerLength cut is not judged" without saying which trailer. That is true of the trailer found past rows a guard stopped. In a table read whole, an /XRefStm in a cut trailer is followed and blamed on the file (#328), as the 071f829 correction to reference/reader-limits.md now says. The ADR and the threat model kept the unscoped sentence.
EVIDENCE: Probe ZzTrailerCutXRefStmWhole: Sound trailer `/XRefStm 99999999 /Pad (70,000 x)` gives `Warning limit.trailer at 299`, then `Warning xref.section-missing at 203: The cross-reference section /XRefStm names at offset 99999999 lies outside the file ...`. Probe ZzTrailerCutXRefStmPastRows (Stopped("/XRefStm 99999999 /Pad (400 x)"), MaxTrailerLength 300) gives only `limit.xref-section-length` (or `limit.index-entries`) and `limit.trailer`.
FIX (reviewer): Use the reference page's wording: "A /Prev in a trailer a limit cut is not judged, nor an /XRefStm in the trailer found past rows a limit stopped; the /XRefStm of a table read whole whose trailer was cut is followed as if whole (#328)."
--- VERDICT REASON: The finding holds. Commit 294d550 added the same sentence to ADR 34 (lines 149-150) and to threat-model.md line 416: "A /Prev or an /XRefStm in a trailer MaxTrailerLength cut is not judged (yet) (#328)". Neither sentence is on main 21a0298. It does not say which trailer. That is true of the trailer found past rows a guard stopped. In a table read whole whose trailer MaxTrailerLength cuts, the /XRefStm is followed, and what it names is reported as the file's fault: the reader gives xref.section-missing and the validator gives Error xref.section-not-found. A /Prev cut the same way is not judged. Commit 071f829 says it will "correct what a cut trailer's /XRefStm does", and it rewrote this exact sentence on reference/reader-limits.md:70-72. It did not touch the twin sentences in ADR 34 and the threat model. Inside threat-model.md, line 416 now contradicts its own known-gaps entry for #328 at line 471, which says the /XRefStm "is followed as if whole". It stays a nit. In both places the paragraph around the sentence is about sections a guard stopped, so the sentence can be read as scoped. The reference page and the known-gaps list, both correct, are the texts a user reads. #328's closing criteria already require ADR 34 to say what a cut trailer's values are. The threat-model row is the more clearly scoped of the two, because its left column limits it to sections stopped by a rows or index guard. The ADR paragraph ends on the sentence as a general statement.
VERDICT EVIDENCE: I rebuilt the probe on origin/ccr-6d6c0ee6-dqm4lm-188 (071f829), in Release. The probe was uncommitted and has been removed.

(1) Table read whole: a one-table file (HandWrittenFile, objects 1-3), trailer "/Size 4 /Root 1 0 R /XRefStm 99999999 /Pad (70,000 x)", default limits:
  Warning limit.trailer at 295: A trailer runs past 64 KB; only what lies within it was read. ...
  Warning xref.section-missing at 199: The cross-reference section /XRefStm names at offset 99999999 lies outside the file, which is 70,368 bytes long; the objects only it indexes are looked for by rebuilding the index.
  rule Error xref.section-not-found: The cross-reference section /XRefStm names at offset 99999999 is not there, nor within 512 bytes of it: the offset lies outside the file.
  rule Information xref.checked-in-part
The same file with "/Prev 99999999" gives only limit.trailer at 295 and xref.checked-in-part, so the /Prev is not judged. My offsets are 4 bytes below the finding's 203/299 because the file's header differs.

(2) Trailer past rows a guard stopped: StoppedSectionFiles.Stopped("/XRefStm 99999999 /Pad (400 x)"), guard "table rows" or "table index", MaxTrailerLength = 300:
  Warning limit.xref-section-length at 483 (or limit.index-entries at 483)
  Warning limit.trailer at 1393
  rule Information xref.checked-in-part
Nothing else is reported. A whole read of the same file gives xref.section-missing and Error xref.section-not-found.

Text: `git log -S'in a trailer \`MaxTrailerLength\` cut' 21a0298..HEAD` finds only 294d550. In 071f829, reference/reader-limits.md:70-72 changed to "A /Prev in a trailer a limit cut is not judged, nor an /XRefStm in one found past rows a limit stopped; but the /XRefStm that the cut trailer of a table read whole gives is followed as if whole, though the cut may have parted its digits (#328)". threat-model.md:471 (#328 gap) says "an /XRefStm there is followed as if whole ... and what it then names is blamed on the file". threat-model.md:416 still says "so is a /Prev or an /XRefStm in a trailer MaxTrailerLength cut, which is not judged ([#328])".
FIX (refuter): Use the reference page's wording in both places.

ADR 34, docs/adr/0034-every-valid-pdf-is-readable-and-the-readers-guards-are.md:149-150, replace the last sentence of the 2026-11-04 amendment with: "A `/Prev` in a trailer `MaxTrailerLength` cut is not judged yet, nor an `/XRefStm` in the trailer found past rows a guard stopped; the `/XRefStm` that the cut trailer of a table read whole gives is followed as if whole, though the cut may have parted its digits ([#328](https://github.com/AdCodicem/AdCodicem.Pdf/issues/328))."

docs/threat-model.md:416, replace "so is a `/Prev` or an `/XRefStm` in a trailer `MaxTrailerLength` cut, which is not judged ([#328])" with "so is a `/Prev` in a trailer `MaxTrailerLength` cut, and an `/XRefStm` in the trailer found past the stopped rows, neither judged ([#328]); the `/XRefStm` of a table read whole whose trailer the guard cut is followed as if whole (the same gap)".

This is a wording fix only, with no code change. It is not a design choice.

######## [46] docs:bench-claim-uncommitted — reviewer nit; refuter narrowed minor maintainer=False
LOCATION: commit e67fef9 body ("On the rebuild benchmarks ... about 12 ms for 4 MB"); commit b09f981 body
CLAIM: e67fef9 cites "the rebuild benchmarks" for the cost of lines holding a percent sign, and b09f981 describes an optimization of the line lexer (one search per token rather than a byte at a time). The benchmarks that measured those shapes were Zz probes and are not in the diff. The only committed rebuild benchmark is TrailerScanBenchmarks. Invariant 9 asks that an optimization ship with its benchmark.
EVIDENCE: `git diff --stat 21a0298..origin/ccr-6d6c0ee6-dqm4lm-188 -- benchmarks` is empty. The measurements are in scratchpad lot19/stage4/bench/benchmarks/AdCodicem.Pdf.Benchmarks/ZzScanBenchmarks.cs and ZzStreamEndBenchmarks.cs (for example `comment-headers-not 19,165.8 μs`, `comment-lines 3,713.2 μs`).
FIX (reviewer): Commit the scan and stream-end comment-line shapes as benchmarks, which also satisfies invariant 9. Otherwise word the bodies as a measurement made for the change, not as existing benchmarks; the PR body can carry the numbers.
--- VERDICT REASON: The finding holds, with one part narrower than claimed. Nothing under benchmarks/ changed in the batch. In e67fef9's body, "the rebuild benchmarks" points at shapes that exist only in an uncommitted probe. b09f981's search-based line lexer has no committed benchmark for most of its arms.

The narrowing: b09f981 is not measured only by Zz probes. The committed StreamLengthBenchmarks (16384 bytes, LengthError 2) caught the byte-stepping lexer: 40.5 to 43.6 ms with it, 10.5 ms once Pass searched. So that benchmark covers the arm for runs of regular characters (mutant PS8).

The severity goes up from nit to minor, for two reasons:
- **Classing the batch's own mutants needs a benchmark.** Its ADR 53 record classes LineTokens mutants PS2, PS8, PS10 and PS11 (each removes one Pass arm) and TD6 as equivalent because "only speed changes". The maintainer's rule of 2026-11-01 (docs/status.md around line 99) classes such a mutant only with "a benchmark holding the constant factor (invariant 9)". No committed benchmark covers PS2 (white space), PS10 (literal strings), PS11 (hex strings) or TD6 (digit runs).
- **Precedent.** Batch 18's review listed "f2f5e6c, a perf: commit, had shipped no benchmark" among its minors (status.md line 625).

Commit bodies are not rewritten once pushed (status.md, "Commit bodies not rewritten"). So the remedy is to commit the benchmarks and record the bodies, not to reword them.
VERDICT EVIDENCE: On origin/ccr-6d6c0ee6-dqm4lm-188 (071f829):
- `git diff --stat 21a0298..HEAD -- benchmarks` is empty, and `git ls-tree -r HEAD benchmarks` holds no Zz file.
- e67fef9's body: "On the rebuild benchmarks, sound files and plain headers cost what they did; files built to cross a line holding a percent sign at every obj cost about 12 ms for 4 MB."
- b09f981's body: "The line lexer passes white space, strings, names and runs of digits with one search each rather than a byte at a time". It names no benchmark.

The committed rebuild benchmark is TrailerScanBenchmarks: two obj keywords and 12,000 trailer keywords. The measurement report (measure.json) says: "No committed benchmark measures the object scan alone; TrailerScanBenchmarks is dominated by the trailer parses."

The sound, plain-headers, comment-lines and obj-lines shapes exist only in scratchpad lot19/stage4/bench/.../ZzScanBenchmarks.cs:

| Shape | Parent (ms) | Fix (ms) |
|---|---|---|
| comment-lines | 1.31 / 1.21 | 11.36 / 10.08 |
| obj-lines | 0.45 / 0.49 | 8.93 / 11.47 |

The stream-end shapes exist only in stage4/ZzStreamEndBenchmarks.cs.txt (fix-4: comment-lines 3,713.2 μs, comment-headers-not 19,165.8 μs, open-string 1,150.4 μs).

The committed StreamLengthBenchmarks (benchout2):

| Run | 16384 bytes / LengthError 2 / memory |
|---|---|
| parent | 9.26 to 10.30 ms |
| fix-1 / fix-2 (byte-stepping lexer) | 43.577 / 40.505 ms |
| fix-3 / fix-4 (search-based) | 10.555 / 10.450 ms |

That benchmark's content is 16 KB of 'A' (SyntheticDocument.cs:31), so it only drives the regular-run arm (LineTokens.cs:139, IndexOfAny(RunEnds)). No committed benchmark puts long white space, literal strings, hex strings or comment lines in front of the stream-end search: grep over benchmarks/*.cs finds no such data.

impl-result.json: "Commit 2, LineTokens equivalents (6): PS2, PS8, PS10 and PS11 each remove one Pass arm ... so only speed changes. ... TD6 removes the early return at Beyond; ... only speed changes." No benchmark is named for them.
FIX (refuter): 1. **Commit the two probes as benchmarks.** Write them without the `#pragma warning disable`, with XML docs and no review keys:
   - An object-scan rebuild benchmark: a file with no valid startxref, with shapes sound, plain headers, comment headers, comment lines and percent-in-string lines.
   - A stream-end search benchmark: 4 MB of text lines, one line, comment lines, an open string and comment-headers-not, plus a long white-space run and a hex-string line, so that PS2 and PS11 have a shape.
2. **Name the benchmarks in the ADR 53 record.** For PS2, PS8 (StreamLengthBenchmarks already holds it), PS10, PS11 and TD6, say which benchmark holds the constant factor, as the 2026-11-01 rule requires.
3. **Record the bodies in docs/status.md** under "Commit bodies not rewritten":
   - e67fef9's "the rebuild benchmarks" meant an uncommitted probe, and the body gives no figure from before the fix (comment lines 1.3 ms to about 11 ms for 4 MB).
   - b09f981 shipped its lexer's search arms without a benchmark.
   - Name the new benchmarks there, and put the before and after figures in *Current measurements* and the PR body.

Not a maintainer choice: the 2026-11-01 rule and invariant 9 already settle it.

######## [47] docs:m04-spec-change-unrecorded — reviewer nit; refuter narrowed nit maintainer=False
LOCATION: docs/milestones/M04.md:72-73; commit 7d35896 body
CLAIM: 7d35896 adds a design rule to a milestone not yet started: "A stream several tables name ... belongs to the oldest one's revision". Its body does not mention it, and no maintainer decision in decisions.md covers M04.
EVIDENCE: `git show --stat 7d35896` lists docs/milestones/M04.md (10 lines). The body's last paragraphs speak of loop tests and pdfmaker7 only. decisions.md has no M04 entry; P1b covers the validator's /Size judgment only.
FIX (reviewer): Name it in the PR as a proposed M04 rule for the maintainer to confirm, or reword it as an open point of M04 ("which revision owns a stream several tables name is decided with M04; the validator judges its /Size at the oldest naming").
--- VERDICT REASON: The facts hold, but the finding overstates them. 7d35896 does change docs/milestones/M04.md (State: to do; roadmap row "M04 | ... | to do"). Its body never names that file, and decisions.md has no M04 entry, so no maintainer answer covers the sentence.

What does not hold:
(1) "Unrecorded" is wrong. The batch planned this edit. The xrefstm lens's docs_to_update lists it (measure/xrefstm/summary.md:181), and so does the critic's (measure/critic/summary.md:500). The rationale is in P1b's description: "the oldest table that names a stream is the revision that wrote it" (measure/xrefstm/summary.md:78). Issue #156's own body raised the tension with M04: "The record also ties S1 to T2's revision alone, where docs/milestones/M04.md takes a table and its stream as one section."
(2) It is not really a new rule. It applies M04's existing principle, "A revision is what one save wrote", to a shape the reader now records twice: the save that wrote the stream is the oldest naming table's.
(3) The commit body follows the project's usual practice. Bodies describe behavior, not files: the same body does not name its threat-model.md, diagnostics.md or validation-rules.md edits either, and it does state the substance ("its own /Size is judged where the oldest of them names it, the revision that wrote it").

What remains is a process nit. A future milestone's specification now states a settled resolution that the maintainer only saw as a line in the critic's docs list, not as an answer. Separately, the clause "as the validator already judges it" is looser than the code. SizeWrongRule counts the shared stream's rows "as part of each revision whose table names it" (SizeWrongRule.cs:55) and judges only its own /Size at the oldest naming (SizeWrongRule.cs:20-21, :56). The validator does not assign the stream wholly to the oldest revision.
VERDICT EVIDENCE: On origin/ccr-6d6c0ee6-dqm4lm-188 (071f829):

- `git show 7d35896 -- docs/milestones/M04.md` adds this at M04.md:72-73: "A stream several tables name — a writer that copies `/XRefStm` into the next trailer — belongs to the oldest one's revision, the one that wrote it, as the validator already judges it."
- Within 21a0298..HEAD, `git log -- docs/milestones/` lists only 7d35896 and 071f829. 071f829 touches M02.md only.
- decisions.md has no "M04" string. Its #156 line under "Decided without a question" reads "its /Size judged at its oldest naming (P1b)".
- measure/xrefstm/summary.md:181 and measure/critic/summary.md:500 both read "docs/milestones/M04.md:71: a stream several tables name belongs to the oldest one's revision". The edit was planned, not improvised.
- The #156 issue body (GitHub) contains "where `docs/milestones/M04.md` takes a table and its stream as one section". Its comments are empty.
- docs/status.md:11 shows M02 as the current milestone, and the roadmap shows M04 as "to do".
- SizeWrongRule.cs:55-57 reads: "is counted for each, as part of each revision whose table names it, and its own /Size is judged once, where the oldest of them names it".
- The PR is not opened yet (task 26 pending), so it can still carry the note.
FIX (refuter): In the batch's PR description, name the M04.md sentence as a clarification of M04's "A revision is what one save wrote", made because the reader now records every /XRefStm naming, for the maintainer to confirm or reverse. Optionally tighten the clause to match the code, for example: "belongs to the oldest one's revision, the one that wrote it, where the validator judges its own /Size". No code change is needed. The rule follows an existing M04 principle, so this is a note, not a question to settle before merge.

######## [48] docs:validation-rules-wording — reviewer nit; refuter confirmed nit maintainer=False
LOCATION: docs/website/docs/reference/validation-rules.md:322-324
CLAIM: Two wording problems in the structural profile's gaps bullet. "that object's header is one the header rule does not take, a sign before its number say:" lacks the commas that make "say" read as "for example". "A comment between a header's tokens no longer hides it" narrates a change in a reference page (Diátaxis).
EVIDENCE: Text at :322-324: "or the next object's, when the object lost its own and that object's header is one the header rule does not take, a sign before its number say: the container then runs on to that `endobj`. A comment between a header's tokens no longer hides it, since the rule reads comments as a load does ([#261])."
FIX (reviewer): "... that object's header is one the header rule does not take — a sign before its number, say —: the container then runs on to that endobj. Comments between a header's tokens do not hide it: the rule reads them as a load does ([#261])."
--- VERDICT REASON: The finding holds as stated, and both phrases are new in this batch. On main 21a0298 the bullet (line 320) ended "...a comment, which the header rule does not take for one: the container then runs on to that `endobj`." The branch replaced that example with "a sign before its number say:" and added "A comment between a header's tokens no longer hides it".

(1) When "say" means "for example", the house style sets it off with commas. docs/website/docs/concepts/diagnostics.md:74 has "inside a string, say, an `endobj`" and src/AdCodicem.Pdf/IO/IPdfFileEdge.cs:10 has "allows, say —". Without the commas, "a sign before its number say:" reads as a verb.

(2) "no longer hides it" tells the reader what used to happen. That is change narration in a page under reference/, which ADR 47 (Diátaxis, one mode per page) keeps out. The only other "no longer" in reference/ (validation-rules.md:247, "an override that no longer changes the pinned model") describes a present condition, not the library's history, so line 324 is the only case.

The content is accurate, so only the wording is at fault. PdfObjectParser.IsHeader/MayBeHeaderBytes (src/AdCodicem.Pdf/IO/PdfObjectParser.cs:1634-1661) accept digits only, with no sign, and the lexer reads past comments between the tokens "as a load does (#261)".
VERDICT EVIDENCE: Branch origin/ccr-6d6c0ee6-dqm4lm-188 (071f829), docs/website/docs/reference/validation-rules.md:322-324: "...when the object lost its own and that object's header is one / the header rule does not take, a sign before its number say: the container then runs on to that `endobj`. A comment / between a header's tokens no longer hides it, since the rule reads comments as a load does ([#261])."

`git show 21a0298:docs/website/docs/reference/validation-rules.md` line 320: "a comment, which the header rule does not take for one: the container then runs on to that `endobj`." (neither phrase exists on main).

House style for "say": concepts/diagnostics.md:74 "— inside a string, say, an `endobj` after it or not —"; IPdfFileEdge.cs:10 "allows, say —".

Code agrees with the content: PdfObjectParser.cs:1635-1639 "digits alone ... — no sign —, white space and comments between them, which the lexer reads past as a load does (#261)".
FIX (refuter): Reword lines 322-324: "...when the object lost its own and that object's header is one the header rule does not take — a sign before its number, say —: the container then runs on to that `endobj`. A comment between a header's tokens does not hide it: the rule reads comments as a load does ([#261])." The em-dash pair followed by a colon matches the page's own usage at line 317 ("— pikepdf's ... holds one —:"). A plain ", a sign before its number, say:" would also do. This is a wording fix, not a design choice.
