# Review round 10 (wf_90e4d823-0f8): findings and refuter verdicts

## 1. check-decode-drops-stream-length-guard — major → confirmed major (tests)
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:6549 (TryReadXRefStream, checkOnly branch: `_diagnostics.RollBack(decodingChecked);`, from 092b772); its comment at :6534-6537 ('drops what the decoding reports, which a reading of the stream meets again')

Claim: This is open point (a), confirmed and wider than the stage measured. KeepsItsPlace's check decodes an untyped cross-reference stream with no value cut. It then rolls the decoding's reports back with plain RollBack, so the limit.stream-length that MaxStreamLength's cut makes is dropped. No later reading reports it again, because _limitsReached is keyed by position. The guard is reached without a report (invariant 12, ADR 34), yet under ThrowOnLimit the same file throws MaxStreamLength. This happens with no filter at all, not only with /Foo. main 21a0298 reports the guard in every shape, so this is a regression from this batch. No test pins it: the e157359 guard theory covers only the value-cut path.

Evidence: Writer: W/r10wip/tests/ZzR10WriteFilesTests.cs.txt writes HybridFiles.PrevNamingAStreamBesideTheOldestTable("", padding: 100[, filter: "/Foo"]) and its typed twin into W/r10wip/tests/files. Reader: W/r10wip/tests/ZzR10FileProbeTests.cs.txt, public API only, run on c2a2aaa and on 21a0298. It opens each file with MaxStreamLength 50, then with ThrowOnLimit too. Outputs are in W/r10wip/probe-out/files-head.txt and files-main.txt. Branch c2a2aaa: a_untyped_ms50 gives [xref.offset-adjusted @1459, xref.hybrid-stream-lost @1647, filter.unsupported @1765, xref.offset-adjusted @1459] with no limit.stream-length, but 'ThrowOnLimit: throws MaxStreamLength'. a_untyped_nofilter_ms50 gives [xref.hybrid-stream-lost @1647] alone, again with 'throws MaxStreamLength'. a_typed_ms50 keeps limit.stream-length @1777. main 21a0298: a_untyped_ms50 gives [limit.stream-length @1765, filter.unsupported, offset-adjusted]; a_untyped_nofilter_ms50 gives [limit.stream-length @1752, offset-adjusted]. The same shape with a cut /W and MaxObjectLength 1024 keeps the guard on the branch (e157359).

Fix: Use `_diagnostics.RollBackBesideLimits(decodingChecked)` at :6549, as e157359 did for the value-cut path. Reword the comment at :6534-6537: what the decoding reports is dropped, but a guard it reaches stands, because no later reading reports it. Then KeepsItsPlace's remark can drop 'beside a value a guard cut' (the stage's own note). Add test rows with no value cut, for example a `cut: null` row in GuardCutValueTests.A_guard_reached_decoding_a_stream_beside_a_value_a_guard_cut_is_reported_once_whatever_the_look_at_the_prev_s_place_drops, or a sibling theory: ("", no filter) and ("", /Foo) under MaxStreamLength 50 must give limit.stream-length exactly once. Today those rows fail on c2a2aaa.

Verdict: The finding holds as stated. It is open point (a), and it is wider than the R9c stage measured, since it happens with no filter at all.

How the report is lost:
- KeepsItsPlace (PdfFileReader.cs:3884) is asked when a hybrid table's /XRefStm search reaches the place the table's /Prev names. It calls TryReadXRefStream with checkOnly true.
- For a stream with no /Type and no value cut, that check decodes the data, then calls plain `_diagnostics.RollBack(decodingChecked)` at :6549.
- The decoding reaches MaxStreamLength through ReachStreamLength (:8667-8677). That records (StreamLength, position) in the reader's own `_limitsReached` set, and the report goes into the diagnostics.
- RollBack (PdfDiagnostics.cs:127) then removes the entry and resets the guard count, but `_limitsReached` keeps the key.
- The /Prev's own reading of the same stream meets the guard again at the same position and stays silent. So the guard is reached and never reported. That breaks invariant 12 and ADR 34. Under ThrowOnLimit the same file throws MaxStreamLength, from the check itself.

Where it comes from:
- 092b772 introduced it (2026-11-07). That is a commit of this batch: main 21a0298 has no checkOnly path at all.
- e157359 fixed only the value-cut path (:6436) by switching to RollBackBesideLimits.

None of the accepted items covers it:
- The round 8 accepted item is the check's throw at MaxDecodedStreamLength under ThrowOnLimit. This finding is a missing report when nothing throws.
- No issue from #320 to #333 holds it.

The texts misstate the code:
- The comment at :6534-6537 says the check drops what the decoding reports 'which a reading of the stream meets again'. That is false for this guard.
- The remarks at :3864 and :6225, and threat-model.md:432, describe the guard as standing only 'beside a value a guard cut'. So they describe the defect as if it were intended.

Not needed from the maintainer: invariant 12 and ADR 34 decide this, and e157359 already made the same choice for the value-cut path.

The shape is narrow: a hybrid file whose /XRefStm misses, whose /Prev names an untyped stream, with data longer than MaxStreamLength. The brief's scale still makes a missing guard report and a broken invariant major.

Verdict evidence: I rebuilt this on a fresh worktree at c2a2aaa and a fresh one at 21a0298 (Release, 0 warnings).

Probes: V = W/r10wip/verify-check-decode-drops-stream-length-guard.
- V/ZzVcdWriteTests.cs.txt writes the files with HybridFiles.PrevNamingAStreamBesideTheOldestTable into V/files.
- V/ZzVcdReadTests.cs.txt uses the public API only. It opens each file with default limits, then under the limits its name gives, then with ThrowOnLimit.
- Outputs: V/out/read-head.txt and V/out/read-main.txt.

Branch c2a2aaa, under MaxStreamLength 50 unless the name says otherwise:
- u_foo_ms50 ("", padding 100, /Foo): [Repair xref.offset-adjusted @1459, xref.hybrid-stream-lost @1647, filter.unsupported @1765, xref.offset-adjusted @1459]. No limit.stream-length, yet 'ThrowOnLimit: throws MaxStreamLength'.
- u_none_ms50 (no filter): [xref.hybrid-stream-lost @1647] alone, and it throws MaxStreamLength.
- u_none_p1000_ms50 and u_none_p1000_ms500 (padding 1000): the same, [xref.hybrid-stream-lost @1647] alone, and both throw.
- t_foo_ms50 and t_none_ms50 (typed): they keep limit.stream-length @1777 and @1764.
- u_foo_cutW_ms50_mo1024 and u_none_cutW_ms50_mo1024 (/W cut, the e157359 path): they keep limit.stream-length @1763 and @1750.

Main 21a0298:
- u_foo_ms50: [limit.stream-length @1765, filter.unsupported @1765, xref.offset-adjusted @1675].
- u_none_ms50: [limit.stream-length @1752, xref.offset-adjusted @1675].
- u_none_p1000_ms50 and u_none_p1000_ms500: limit.stream-length @1753 in both.

The proposed test rows (V/ZzVcdRowTests.cs.txt) require exactly one limit.stream-length for ("", 100), ("/Foo", 100) and ("", 1000) under MaxStreamLength 50.
- On c2a2aaa all 3 fail with 'Expected ... to be 1 ... but found 0'.

With the one-word fix (V/fix.diff: `RollBackBesideLimits(decodingChecked)` at :6549):
- All 3 rows pass.
- V/out/read-fixed.txt differs from read-head.txt only by the four added limit.stream-length lines (@1765, @1752, @1753, @1753). Nothing else moves, and no report is doubled.
- 14 neighboring test classes still pass, 3,987 tests: CrossReferenceChain, CrossReferenceRule, DocumentReader, FileRule, FilterDamage, GuardCutValue, HeaderOffset, HostileInput, ObjectRule, PageTreeRule, ReaderLimits, StreamLength, SyntaxFault and Validator, with 0 failed and 2 skipped. So no test pins this report either way, which confirms 'no test pins it'.

The worktree is restored, and the 21a0298 worktree is removed (it had no remote corpus copy).
fix: In the code:
- Replace `_diagnostics.RollBack(decodingChecked);` at PdfFileReader.cs:6549 with `_diagnostics.RollBackBesideLimits(decodingChecked);`. This is the same change e157359 made at :6436.

In the texts:
- Reword the comment at :6534-6537: the check drops what the decoding reports, but a guard it reaches stands, because the guard is reported once for the stream and no later reading reports it.
- In the remarks at :3864 (KeepsItsPlace) and :6225 (TryReadXRefStream), and in threat-model.md:432's cell ('but for a guard its decoding beside a value a guard cut reached'), drop 'beside a value a guard cut'. The guard now stands on both decode paths.

In the tests, add rows with no value cut:
- Either a sibling of GuardCutValueTests.A_guard_reached_decoding_a_stream_beside_a_value_a_guard_cut_is_reported_once_whatever_the_look_at_the_prev_s_place_drops, or a `cut: null` arm in it.
- Use HybridFiles.PrevNamingAStreamBesideTheOldestTable("", padding: 100[, filter: "/Foo"]) under MaxStreamLength 50.
- Assert the exact code list, with limit.stream-length first and once: [LimitStreamLength, XRefHybridStreamLost] with no filter, and [LimitStreamLength, XRefOffsetAdjusted, XRefHybridStreamLost, FilterUnsupported, XRefOffsetAdjusted] with /Foo.
- These rows kill the RollBack/RollBackBesideLimits mutant (ADR 53).

In the records:
- Add 092b772 to status.md's 'Commit bodies not rewritten'. Its body says the check of a /Prev's place drops the decoding's reports, and that is false for a guard the decoding reaches, until this fix.
needs_maintainer: False

## 2. long-object-builder-comment-size — nit → confirmed nit (tests)
Location: tests/AdCodicem.Pdf.Tests/HybridFiles.cs:1526

Claim: PrevFallingShortOfAStreamWhosePrevLeadsThroughALongObject's summary says object 20 holds '21 0 R and a comment of 1,100 bytes'. The code (:1547) writes '%' followed by 1,100 '=', which is a comment of 1,101 bytes. Elsewhere in the file, comments are counted by their dashes ('a comment of 600 dashes'), or exactly in bytes ('a comment of 700 bytes' for the '%' plus 698 dashes plus newline). MaxObjectLength 1,024 cuts either way.

Evidence: Dump: W/r10wip/tests/dump/long_True_the_oldest_table.pdf, made by W/r10wip/tests/ZzR10DumpTests.cs.txt and examined with W/r10wip/tests/anat.py. Object 20 at 476 has a 1,108-byte body: '21 0 R' (6) + '\n' (1) + '%' (1) + 1,100 '=' = 1,108. anat.py reports 'comment 1101 bytes' at 492.

Fix: Say 'a comment of 1,100 equals signs', or 'a comment of 1,101 bytes'.

Verdict: The finding holds. The summary of PrevFallingShortOfAStreamWhosePrevLeadsThroughALongObject (tests/AdCodicem.Pdf.Tests/HybridFiles.cs:1526, at c2a2aaa) says object 20 holds a comment of 1,100 bytes. Line 1547 builds that body as "21 0 R\n%" plus 1,100 '=' characters, and HandWrittenFile.Object (HandWrittenFile.cs:35) writes the body followed by an end of line and then endobj. So the comment is 1,101 bytes without its end of line, or 1,102 with it.

Elsewhere in the file a comment is counted one of two ways:
- by the characters after the '%', for example 'a comment of 600 dashes' is '%' plus 600 dashes;
- by its bytes including the end of line: the 'comment of 700 bytes' in the same summary (line 1524, built at line 1542) is '%' plus 698 dashes plus '\n'.

Neither count gives 1,100 for object 20. It is the only such wording in tests/ ('1,100 bytes' appears elsewhere only for MaxDecodedStreamLength in DocumentReaderTests). Commit 9823992 introduced both the wording and the builder, in this batch (R9b, listed among the new builders in r9-fix-result.json). No earlier round reported it, and no decision settles it.

The consequence is wording only. MaxObjectLength 1,024 still cuts the object after its whole '21 0 R' reference, which is what the summary goes on to say, so the test's claim and outcome are not affected.

Verdict evidence: Code: HybridFiles.cs:1547 `var twenty = "21 0 R\n%" + new string('=', 1100);` and HandWrittenFile.cs:35 appends `{body}{_eol}endobj{_eol}`. Reviewer's dump C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r10wip/tests/dump/long_True_the_oldest_table.pdf, measured with my own script C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r10wip/verify-long-object-builder-comment-size/count.py: `object 20 at 476 body bytes 1108 comment at 492 comment bytes without EOL 1101 with EOL 1102 equals signs 1100`. For comparison, line 1542 writes `"%" + new string('-', 698) + "\n"`, which the same summary calls 'a comment of 700 bytes', counting the '%' and the end of line. `git log -S"a comment of 1,100 bytes" 21a0298..c2a2aaa` names only 9823992.
fix: In HybridFiles.cs:1526, change 'a comment of 1,100 bytes' to 'a comment of 1,100 equals signs', which matches the file's 'a comment of N dashes' convention. Saying 'a comment of 1,102 bytes' would match line 1524's convention, which counts the '%' and the end of line. Either works; the code needs no change.
needs_maintainer: False

## 3. passed-over-reached-inside-its-number — major → narrowed major (src-texts)
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3725-3743 (ReportStreamsPassedOver's reached set, by Offset+Padding), message at :3749; src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:120; remark at PdfFileReader.cs:3700-3702 and :3709

Claim: Several texts say a stream the chain reaches is never reported: XRefStreamPassedOver ('the chain did not reach the stream'), the ReportStreamsPassedOver remark ('a link of an older section that reaches the stream, where it is named ... reads it ... no link the chain read reaches it') and the message itself ('the chain did not read the stream ... the index the chain read lacks the objects only it indexes'). All are false when an older section's /XRefStm or /Prev names the passed-over stream one byte into its object number. The chain reads that stream whole and indexes its rows, and the Warning is still made. The reached check is positional (Offset+Padding), while R9-Q1 tells S by its data. This is the same contrivance, applied to the report. The code is unchanged since 9a07bb9: da2b06f gives the same output, and 8b31ebf rewrote the check and the texts without covering it.

Evidence: Probe ZzS01 in W/r10wip/src-texts/ZzSrcTextsProbes.cs.txt (W = the session scratchpad's cloud/ folder). Builder OwnBesideStream, a copy of HybridFiles.PrevFallingShortOfTheStreamBeforeTheTablesOwn("nothing", shortOfOwn: true, oldestNames: "/XRefStm"), with the oldest trailer's '/XRefStm 0000001892' changed to 0000001893. Output on c2a2aaa (out/head/ZzS01.txt): 'section /XRefStm named=1893 state=Read kind=Stream offset=1893' / 'Warning xref.stream-passed-over @1892: The cross-reference stream at offset 1892, near offset 1885, ... and the chain did not read the stream. If it is that table's own stream, the index the chain read lacks the objects only it indexes, which only a rebuild of the index for another cause finds.' / 'obj 10 = "ten"' / 'WasRepaired(after ask)=False'. Object 10, indexed only by stream 11, is served from the chain's index. The /Prev form (oldest '/Prev 0000001890' for own 1889) gives the same: Read at 1890, Warning at 1889, obj 10 "ten". The controls that name 1892 (or 1889) exactly give no Warning. da2b06f (out/r8f/ZzS01.txt) is identical.

Fix: While the search's window is open, record with each typed place passed over where its object number's digits end. Read this as NumberRunsInto does (span[..].IndexOfAnyExceptInRange('0','9')), with no read. In ReportStreamsPassedOver, count as reached a Read, Relocated or Malformed record whose Offset+Padding lies in [at, digitsEnd). Pin the /XRefStm and /Prev rows at own+1 beside the exact controls, with object 10 served and no Warning. Rewording the texts alone would leave the message false.

Verdict: The first half holds and is this batch's own defect. ReportStreamsPassedOver (PdfFileReader.cs:3728-3743 on c2a2aaa) decides the chain reached a stream by start byte only: `told.Add(section.Offset + section.Padding)`, then `told.Add(at)`. Sometimes a link names a byte inside a stream's object number, and the chain reads the stream from there. The record then starts at header+k, while the place the search passed over is the header. So the Warning is still made, and it says 'the chain did not read the stream' and that 'the index the chain read lacks the objects only it indexes'. In fact the chain read that stream's dictionary and data, and object 10, which only it indexes, is served. The same false Warning shows up through three links: an older table's /XRefStm at own+1, an older table's /Prev at own+1, and a newer table's /XRefStm read at own+1 before the search. A three-digit object number named at own+2 gives it too. With the exact naming there is no Warning. These texts are false for this shape:
- diagnostics.md:116 ('A stream the chain reached — an older section names it ... — is reported as the chain finds it');
- threat-model.md:432 (the same clause);
- the remark at PdfFileReader.cs:3700-3702;
- the XRefStreamPassedOver summary ('the chain did not reach the stream').
The Warning code is new in this batch (9a07bb9). 8b31ebf's record check did not change the result: the reviewer's da2b06f run gives the same output. main has no such code. On main, shape A takes stream 11 at its header, rebuilds, and gives Error file.root-invalid, so head reads better there except for this Warning. None of the accepted items or open issues holds it. #330, #332 and #333 cover other shapes. The 'not marking the index incomplete' item and R8-Q3's kagain Warning are about a stream that is truly never read. Round 9's [1] was the same kind of defect: a passed-over Warning at a stream the chain reached, a wrong report with nothing served wrong. It was settled as major, and so is this. The cause is the one R9-Q1 fixed for S: the stream is told by the byte its reading started at, not by its data. No new maintainer choice is needed, because the fix applies R9-Q1's principle and makes the existing texts true.
The second half is narrowed away. The search's byte-based reached test (:5118) takes the stream again only in the newer-/XRefStm shape, and only where the stream's /Prev names another offset. That behaves exactly as main does: main takes 877 whether it was named at 877 or 878. It is also #332's open question: whether the search may take a stream a newer table's /XRefStm read. The brief lists that consequence as accepted and tracked there. With the exact naming the batch passes the stream over as reached, which is #332's subject; one byte in, it takes it, as main does. That is worth a comment on #332, not a defect or a new debt for this batch.

Verdict evidence: I rebuilt the reproducer independently with a self-contained builder (my own copy of the PrevFallingShortOfTheStreamBeforeTheTablesOwn('nothing', shortOfOwn: true, oldestNames) shape, plus a newer-/XRefStm shape) in W/r10wip/verify-passed-over-reached-inside-its-number/ZzRefInsideNumberTests.cs.txt. I ran it on c2a2aaa and on main 21a0298 in my worktree; the probe has since been removed and the worktree is back at c2a2aaa, clean. The outputs are in W/r10wip/verify-passed-over-reached-inside-its-number/mine-head.txt and mine-main.txt. W = C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud.

On c2a2aaa:
- **Oldest /XRefStm at own+1 (own=1892):** sections end in 'S /XRefStm named=1893 Read Stream at=1893+0'. Diagnostics: 'D Repair xref.offset-adjusted @1703', 'D Warning xref.hybrid-stream-lost @1703', 'D Warning xref.stream-passed-over @1892'. Objects: 4="four" 5="five" 10="ten", repaired False before and after the reads. Message: '...the search near that offset took another section first, and the chain did not read the stream. If it is that table's own stream, the index the chain read lacks the objects only it indexes...'. The findings are the same as with the exact naming: [Warning:xref.section-shifted, Error:xref.hybrid-stream-wrong, Warning:page-tree.kid-invalid].
- **Exact naming (own+0):** 'Read Stream at=1892+0', diagnostics offset-adjusted and hybrid-stream-lost only, 10="ten".
- **Oldest /Prev at own+1 (own=1889):** 'Read Stream at=1890+0' and 'Warning xref.stream-passed-over @1889'. At own+0 there is no Warning.
- **Stream renamed 111, oldest /XRefStm at own+1 and at own+2:** 'Read Stream at=1893' or '1894', and 'Warning xref.stream-passed-over @1892' both times.
- **Newer table's /XRefStm at 878 = own+1, stream 11 giving no /Prev:** 'S /XRefStm named=878 Read Stream at=878+0', '... /Prev named=870 Relocated Stream at=721+0', and 'D Warning xref.stream-passed-over @877'. Objects 10="ten" 12="twelve", repaired False. At 877 there is no Warning.
- **Same newer shape, stream 11 giving /Prev to the oldest table:** at into=1, 'S /Prev named=870 Relocated Stream at=877+0', diagnostics [offset-adjusted@877], findings [file.size-wrong x2, xref.section-shifted]. At into=0: [offset-adjusted@721, hybrid-stream-lost@721], findings including Error:xref.hybrid-stream-wrong.

On main 21a0298:
- No stream-passed-over anywhere.
- Shape A at own+0 and own+1 alike: 'S /Prev named=1885 Relocated Stream at=1892+0', then 'xref.offset-adjusted @1892', 'xref.rebuilt', 'object.redefined', repaired True, findings [Error:file.root-invalid, ...].
- Newer shape with /Prev oldest, at 877 and 878 alike: 'S /Prev named=870 Relocated Stream at=877+0', [offset-adjusted@877], findings [file.size-wrong x2, section-shifted]. Head's into=1 matches main here, so the second half is main's behavior.

The reviewer's da2b06f run (W/r10wip/own-stream/p3-da2.txt:64-86) gives the same Warning at 1892, which places the defect at 9a07bb9. Code on c2a2aaa: :3734 `told.Add(section.Offset + section.Padding);`, :3740 `if (!told.Add(at))`, :5118 `_sectionsReadAt.Contains(candidate)`. NumberRunsInto (:5281) is applied only to streamStart. No open issue names a link landing inside the object number of a stream that was passed over (searched #171, #188, #289, #295, #320, #321, #323, #330, #331, #332, #333, #52, and a body search).
fix: 1. Tell the passed-over stream by its data, as R9-Q1 does for S. Where TryRelocateXRefSection records a place in _streamsPassedOver (:5221), the window is still open. Also record where the header's object-number digits end: `passed + span[(int)(passed - (start - lead))..].IndexOfAnyExceptInRange((byte)'0', (byte)'9')`, the scan NumberRunsInto makes. It costs no read and no allocation beyond the tuple field.
2. In ReportStreamsPassedOver, skip a place where a record in state Read, Relocated or Malformed has its Offset + Padding in [at, numberEnd). Keep the exact-start set for reporting each place once. Both lists are small: sections are bounded by MaxXRefSectionCount, places by twice MaxRelocationCandidates. Alternatively, look up each position at..numberEnd-1 in `told`.
3. Add test rows beside A_stream_passed_over_near_a_hybrid_table_s_prev_that_an_older_section_reaches_is_reported_as_the_chain_finds_it_not_as_passed_over. Each row expects no xref.stream-passed-over and object 10 "ten":
   - the oldest table's /XRefStm at own+1;
   - the oldest table's /Prev at own+1;
   - a three-digit stream number named at own+2;
   - a newer table's /XRefStm at own+1 read before the search, stream 11 giving no /Prev.
   Keep the exact-naming rows as controls.
4. Optionally add 'at its header or inside its object number' to the remark at :3700-3702, to threat-model 432's 'A stream the chain reached' clause, and to diagnostics.md:116. Once the code is fixed, those texts hold as written.
5. Run ADR 53 over the new lines: drop the range check, and shift the digit-run end by one.
6. The search's byte-based reached test (:5118): not a batch defect. It is main's behavior and #332's question. Add a comment on #332 with the newer-/XRefStm reproducer: named at the header, the stream is passed over as reached and the Error hybrid-stream-wrong is made; named one byte in, it is taken again as main takes it. #332's decision can then cover both namings. Not a maintainer question for this batch.
needs_maintainer: False

## 4. check-main-path-drops-stream-length-guard — major → confirmed major (src-texts)
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:6549 (_diagnostics.RollBack(decodingChecked)); texts at :3864-3865 (KeepsItsPlace remark), :6224-6226 (check paragraph), :6534-6538 (comment 'drops what the decoding reports, which a reading of the stream meets again')

Claim: This is open point (a), confirmed against main. For an untyped stream, the check's decoding outside the cut-value path rolls back with RollBack. The limit.stream-length that ReachStreamLength made is dropped, and _limitsReached keeps the later /Prev reading from making it again, so a guard is reached and never reported (invariant 12, ADR 34). main 21a0298 reports it. So does the typed control, whose data the check does not decode. Under ThrowOnLimit the open throws, so the guard really is reached. The round-9 texts carve the exception as 'beside a value a guard cut' only. The comment at :6419-6424 gives the reason why a guard must stand ('reported once for the stream ... and no later reading reports it'), and that reason holds on the main path too. The comment at :6536 ('which a reading of the stream meets again') is false for this guard. This is a regression of this batch (092b772).

Evidence: Probe ZzS04: BesideOldest, a copy of HybridFiles.PrevNamingAStreamBesideTheOldestTable(type, padding: 100, filter), under MaxStreamLength 50. The outputs are in out/head, out/r8f and out/main/ZzS04.txt. On c2a2aaa and da2b06f, type='' filter='/Foo' gives codes [offset-adjusted, hybrid-stream-lost, filter.unsupported, offset-adjusted] and no limit.stream-length. type='' filter='' gives [hybrid-stream-lost] only, obj 7 "new seven" read from the cut data. type='/Type /XRef ' filter='' gives 'Warning limit.stream-length @1764'. On main 21a0298 every row reports it: '/Foo' 'Warning limit.stream-length @1765', '' '@1752', typed '@1764' and '@1777'. With ThrowOnLimit, every row on every head throws PdfLimitExceededException ('A stream's data runs past 50 bytes').

Fix: At :6549, use _diagnostics.RollBackBesideLimits(decodingChecked), as e157359 did beside a cut value. Texts: the KeepsItsPlace remark becomes '... nothing of the reading is kept or reported, but for a guard its decoding reached (TryReadXRefStream's check alone)'. The check paragraph becomes 'what the decoding reports dropped — but for a guard it reached, which stands, as no later reading reports it —'. The :6536 comment becomes 'drops what the decoding reports, which a reading of the stream meets again, but for a guard it reached, which stands'. Test: the two untyped rows above under MaxStreamLength 50 report limit.stream-length once, at the data's position, as on main. Mutate the call (RollBack against RollBackBesideLimits).

Verdict: The finding holds as stated. I rebuilt the reproducer myself on c2a2aaa, da2b06f and main 21a0298.

**What goes wrong.** KeepsItsPlace's check decodes an untyped stream in TryReadXRefStream's main path (PdfFileReader.cs:6546-6549). That decoding goes through FileStreamData.GetBytes, which calls ReachStreamLength. ReachStreamLength makes the limit.stream-length report and adds (StreamLength, position) to _limitsReached. The check then calls plain `_diagnostics.RollBack(decodingChecked)`, which drops that entry. _limitsReached is not rolled back on this path, so no later reading ever reports the guard, though every later reading meets the cut again: the search, the /Prev's own reading, a later GetBytes.

**Scope.** It happens for a readable stream as well as an undecodable one. Under MaxStreamLength 5 the cut empties the rows, the index is rebuilt, and xref.rebuilt and object.redefined are reported with no limit.* at all. That is a guard's cut blamed on the file, which breaks invariant 12 (ADR 34).

**Origin and the decision.** The line comes from 092b772, a commit of this batch: `git show 092b772` adds `_diagnostics.RollBack(decodingChecked)`. Main reports the guard in every shape I tried. decisions.md's open point (a) says "a fix here unless main loses the report too", so this is not a maintainer choice. It is not filed: no open issue covers it, and #283 is a different defect.

**Texts.**
- The comment at :6534-6538 ("drops what the decoding reports, which a reading of the stream meets again") is false for this guard: a later reading meets the cut but does not report it.
- The KeepsItsPlace remark (:3864-3865), the TryReadXRefStream remark (:6224-6226) and threat-model.md:432 limit the guard that stands to one reached "beside a value a guard cut". They give the reason as "no later reading reports it", which holds just as much for the main path. They describe the loss accurately, but as if it were intended.

**Reach.** The default MaxStreamLength is int.MaxValue, so the loss needs a lowered limit or data over 2 GB. It also needs the check itself: an /XRefStm that misses, with a /Prev naming an untyped stream. That reach is narrow, but by the brief's scale a missing guard report that breaks an invariant is major. The fix stage treated the same loss in the cut-value branch (e157359) as an invariant 12 regression.

**Secondary claim.** I reproduced it too: the one-word swap alone makes SuppressedCount 1 for [/FlateDecode /Foo] under MaxDecodedStreamLength 1000. The cause is that MoveDecodingReportsTo counts a guard a repeated decoding reaches again in _limits without adding an entry, and RollBackBesideLimits then counts it as left out by the capacity. The reviewer's out-head.txt:313 (b4 mol_mdsl, suppressed 1) shows the same phantom already happens on head in the cut-value branch.

ThrowOnLimit is unchanged by the fix: the check throws at open, as on da2b06f.

Verdict evidence: My own probes are in W/r10wip/verify-check-main-path-drops-stream-length-guard/wt19/:
- ZzR19Gen.cs.txt writes the files to files/, from HybridFiles.PrevNamingAStreamBesideTheOldestTable with padding 100:
  - u_foo: untyped, /Foo
  - u_plain: untyped, no filter, 107 bytes readable
  - t_foo, t_plain: the /Type /XRef twins
  - u_plain0: padding 0
  - u_flate, u_flatefoo: Flate-encoded rows plus 4,000 zero bytes, filter Flate or [/FlateDecode /Foo]
- ZzR19Read.cs.txt reads the files on any tree.
- Outputs: out-main.txt, out-da2.txt, out-head-full.txt, out-fix1.txt, out-fix2.txt, neighbors-fix2.txt.

Diagnostic codes, main 21a0298 vs c2a2aaa (da2b06f is identical to c2a2aaa in every row):

| File, limit | main 21a0298 | c2a2aaa |
|---|---|---|
| u_foo, MaxStreamLength 50 | [limit.stream-length, filter.unsupported, xref.offset-adjusted] | [xref.offset-adjusted, xref.hybrid-stream-lost, filter.unsupported, xref.offset-adjusted] |
| u_plain, MaxStreamLength 50 | [limit.stream-length, xref.offset-adjusted] | [xref.hybrid-stream-lost] |
| u_plain, MaxStreamLength 5 | [limit.stream-length, xref.offset-adjusted] | [xref.hybrid-stream-lost, xref.rebuilt, object.redefined] |
| u_plain0, MaxStreamLength 5 | [limit.stream-length, xref.offset-adjusted] | [xref.hybrid-stream-lost, xref.rebuilt, object.redefined] |
| u_flate, MaxStreamLength 5 | — | [xref.hybrid-stream-lost, xref.rebuilt, object.redefined] |

The typed twins keep the guard on head:
- t_plain, MaxStreamLength 5: [xref.hybrid-stream-lost, limit.stream-length, xref.rebuilt, object.redefined]
- t_foo, MaxStreamLength 50: [xref.hybrid-stream-lost, limit.stream-length, filter.unsupported, xref.section-unreadable]

The other guards are kept on head:
- u_flate, MaxDecodedStreamLength 1000: [xref.hybrid-stream-lost, limit.decoded-stream]
- u_flatefoo, MaxDecodedStreamLength 1000: limit.decoded-stream reported once

ThrowOnLimit at 50: u_foo and u_plain throw PdfLimitExceededException at ReachStreamLength < GetBytes < TryReadXRefStream:6547 < KeepsItsPlace:3884. Main throws from TryReadXRefSection.

**Prototype fix 1 (fix1.diff): RollBackBesideLimits at :6549.**
- u_foo at 50: [limit.stream-length, xref.offset-adjusted, xref.hybrid-stream-lost, filter.unsupported, xref.offset-adjusted]
- u_plain at 50: [limit.stream-length, xref.hybrid-stream-lost]
- u_plain at 5: [limit.stream-length, xref.hybrid-stream-lost, xref.rebuilt, object.redefined]
- u_plain0, u_flate and u_flatefoo at 5 likewise; each guard is reported once.
- u_flate at MaxDecodedStreamLength 1000: limit.decoded-stream moves ahead of hybrid-stream-lost, still once.
- u_flatefoo at MaxDecodedStreamLength 1000: suppressed=1, against 0 on head.

**Fix 1 plus the clamp at PdfDiagnostics.cs:169 (fix2.diff, the same lines as the reviewer's fix3.diff).**
- u_flatefoo at MaxDecodedStreamLength 1000: suppressed=0. No other row changes from fix 1.
- 2,479 tests of CrossReferenceChain, CrossReferenceRule, Diagnostics, FilterDamage, Filter, GuardCutValue, HostileInput, ReaderLimits and XRefTable pass, 0 failed. The reviewer's whole suite gave 10,565 total, 0 failed. No test pins the loss.

The worktree is restored to c2a2aaa, clean.
fix: 1. **The fix.** PdfFileReader.cs:6549: `_diagnostics.RollBackBesideLimits(decodingChecked);`, as e157359 did in the cut-value branch.
2. **The suppressed count.** In PdfDiagnostics.RollBackBesideLimits (:169), count as left out only what the capacity left out. A guard a repeated decoding reaches again is counted in _limits without an entry (MoveDecodingReportsTo), so it must not be counted. Either:
   - clamp: `Math.Clamp(_limits - mark.Limits - (kept - mark.Count), 0, _suppressed - mark.Suppressed)`; or
   - more exact: count the limit.* entries the capacity dropped since the mark.

   This also removes the phantom already on head in the cut-value branch. Without it, the swap extends the phantom to the main path.
3. **Texts.** Drop "beside a value a guard cut", so that a guard any decoding of the check reached stands, since no later reading reports it:
   - KeepsItsPlace remark :3864-3865
   - TryReadXRefStream remark :6224-6226
   - threat-model.md:432
   - the comment at :6534-6538: say the decoding's reports are dropped but for a guard it reached, which stands, because ReachStreamLength reports the cut once for the stream and no later reading reports it.
4. **Tests.**
   - Exact-code rows for the untyped no-filter shape at MaxStreamLength 50 and 5, and the untyped /Foo shape at 50. They could be no-cut rows beside GuardCutValueTests.A_guard_reached_decoding_a_stream_beside_a_value_a_guard_cut_is_reported_once_..., with that test renamed so it no longer says "beside a value a guard cut".
   - A SuppressedCount-is-0 row for [/FlateDecode /Foo] under MaxDecodedStreamLength 1000.
   - The ADR 53 mutants (RollBackBesideLimits back to RollBack, the clamp dropped) are killed by these rows.

No maintainer decision is needed: decisions.md's open point (a) already rules on it, since main reports the guard.
needs_maintainer: False

## 5. passed-over-guard-sentence-overstated — minor → narrowed minor (src-texts)
Location: src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:122-123; src/AdCodicem.Pdf/IO/PdfFileReader.cs:626-628 (_streamsPassedOver), :3695-3697 (ReportStreamsPassedOver summary), :3718-3719 (comment), :4987-4988 (TryRelocateXRefSection remark); the pages say the same: diagnostics.md:116, lazy-reading.md:213-215, reader-limits.md:60-61, threat-model.md:432

Claim: The new sentence 'Nothing is reported where one of the reader's limits stopped the chain, or cut what it reached' fails in both directions. The code's rule is ChainStoppedByGuard: a limit that left part of the chain unread. (1) Open point (b): white space past MaxTrailerLength where a /Prev names a section stops the chain (limit.trailer, the section not read there), but PaddingAt sets no guard flag. The Warning is then made for a stream whose naming link the guard hid (#289's class, misstated by the new text). (2) A limit that cuts what the chain reached but hides nothing also leaves the Warning in place: MaxStreamLength cutting padding past every row, or MaxObjectLength cutting an object whose whole reference S's /Prev reads through. That outcome is right, but the text says otherwise. The public summary also leaves out the 'chain indexed nothing' exception that diagnostics.md:116 states.

Evidence: ZzS02 (out/head/ZzS02.txt): stream 6's /Prev names 1,501 bytes of white space before the oldest table, whose /XRefStm names stream 11. With default limits the oldest table is read (pad=1501) and its /XRefStm reads stream 11 at 3300: codes [offset-adjusted, hybrid-stream-lost], no Warning. With MaxTrailerLength=1024: 'Warning limit.trailer @229 ... the section was not read from there', 'Warning xref.section-missing @229', 'Warning xref.stream-passed-over @3300 ...', 'Repair xref.rebuilt'. ZzS17: MaxStreamLength=60 gives 'Warning limit.stream-length @2875' plus 'Warning xref.stream-passed-over @3043'. MaxObjectLength=1024 gives 'Warning xref.stream-passed-over @2945' plus 'Warning limit.object @1621' (object 20, which S's '/Prev 20 0 R' reads through). da2b06f gives the same codes (out/r8f), so only the text is new.

Fix: State the code's rule: 'Nothing is reported where one of the reader's limits left part of the chain unread — ended it, cut rows or a trailer, cut a value a link is read through, or filled the index —, nor where the chain indexed nothing; white space past MaxTrailerLength where a section is named is reported as a section missing (#289), and the stream with it.' Apply it in the five src places and the four pages. Comment on #289 with ZzS02. Alternatively, set the chain's guard flag where PaddingAt's limit.trailer stops a named section, as part of #289's fix.

Verdict: The finding holds in both directions, so the new sentence misstates the code. The rule the code applies is ChainStoppedByGuard (_chainFilledIndex || _cutByGuard, PdfFileReader.cs:1105): the Warning is withheld only where a limit left part of the chain unread. Two cases fall outside what the text says.

(1) White space past MaxTrailerLength where a /Prev names a section. PaddingAt returns -1 (:5461-5476), and the reader reports limit.trailer, 'the section was not read from there'. The section stays NotFound with no flag set, so it is treated as missing: section-missing, then a rebuild. That is #289's misclassification, and the Warning is still made. The texts say nothing is reported where a limit stopped the chain. threat-model.md:432 column 1 goes further and lists 'a guard hid what names it' as fixed.

(2) A limit cut something the chain reached but hid nothing. Two shapes keep the Warning: MaxStreamLength cutting S's data past its last row (S's record shows CutByLimit=True), and MaxObjectLength cutting object 20 after the whole reference S's /Prev reads through. Keeping the Warning is correct: threat-model.md:439 says such cuts let 'the chain going on' and 'hide nothing'. But the sentence 'or cut what it reached' says the Warning is withheld there. The comment at :3718-3719 is plainly false: it says a limit that 'cut a section it reached... each marks the chain cut by a guard', and the cut past S's rows marks nothing.

The behavior is the same on da2b06f (the reviewer's out/r8f). Only the sentence is new, from R9a (8b31ebf/ee33070; `git diff da2b06f c2a2aaa -- PdfDiagnosticCodes.cs`). That makes it the 'a text misstates it' exception to the known #289 item and open point (b).

Narrowed in three ways:
- reference/reader-limits.md:55-61 does not misstate. Its 'nor is ... reported' is tied to 'what a limit leaves out of the index': sections past the count, rows past the length, stream or decoded bounds, a full index. All of these set the flag.
- The ReportStreamsPassedOver remark (:3700-3706) lists the stopping kinds correctly ('the number of its sections, rows it cut or an index it filled, a trailer or a value it cut'). PaddingAt's case is the only one it misses.
- The public summary leaving out the 'chain indexed nothing' exception is an omission, not a misstatement (nit, optional).

On open point (b): the Warning in the white-space case is true (the chain did not read the stream). It follows #289's classification, and the rebuild happens anyway. #289's planned fix (the guard's section, CutByLimit, the chain ending there) would withhold it through ReportLostSection's CutByLimit path (:4510-4512). It is not a separate defect. Setting the flag inside PaddingAt alone would be wrong where the search finds the section past the white space and the chain goes on.

Verdict evidence: Probe: C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r10wip/verify-passed-over-guard-sentence-overstated/ZzVpgsProbes.cs.txt. It rebuilds the reviewer's ZzS02/ZzS17 shapes and adds a control, and prints document.Reader.ChainStoppedByGuard. Output is in out/head/pad.txt and out/head/cuts.txt in that folder. Run on c2a2aaa (origin/ccr-6d6c0ee6-dqm4lm-188), Release, 2/2 passed. The probe file was removed afterwards and the worktree is clean.

pad (run=229 oldest=1730 stream=3143 own=3300 newest=4017):
- Defaults: ChainStoppedByGuard=False, '/Prev named=229 state=Read pad=1501', '/XRefStm named=3300 state=Read'. Codes: offset-adjusted, hybrid-stream-lost. No Warning.
- MaxTrailerLength=1024: ChainStoppedByGuard=False, '/Prev named=229 state=NotFound cut=False'. Diagnostics, in order:
  - 'Warning limit.trailer @229: White space and comments run past 1 KB where a cross-reference section is named; the section was not read from there.'
  - 'Warning xref.section-missing @229'
  - 'Warning xref.stream-passed-over @3300'
  - 'Repair xref.rebuilt @-1'

cuts (oldest=229):
- 'padding limited' (MaxStreamLength=60): ChainStoppedByGuard=False, '/Prev named=3036 state=Relocated kind=Stream offset=2766 cut=True'. Diagnostics: 'Warning limit.stream-length @2875', then 'Warning xref.stream-passed-over @3043'. The oldest table is read and obj 5 = "old five".
- 'through-whole limited' (MaxObjectLength=1024; object 20 = '21 0 R' then 1,100 bytes of comment): ChainStoppedByGuard=False. Diagnostics: 'Warning xref.stream-passed-over @2945', then 'Warning limit.object @1621: Object 20 runs past 1 KB'.
- Control 'through-hidden limited' (the reference placed past the cut): ChainStoppedByGuard=True, no xref.stream-passed-over, limit.object @1621.

So the code withholds the Warning only where the cut left part of the chain unread.

Texts:
- PdfDiagnosticCodes.cs:122-123 'Nothing is reported where one of the reader's limits stopped the chain, or cut what it reached'.
- PdfFileReader.cs:626-628, :3695-3697 and :4987-4988 say the same.
- :3718-3719 'A limit that stopped the chain, or cut a section it reached, ... each marks the chain cut by a guard'.
- diagnostics.md:116 and lazy-reading.md:215-217 say 'stopped the chain or cut what it reached, which may hide the link'.
- threat-model.md:432: column 1 has 'a guard hid what names it'; column 2 has 'where none of the reader's limits stopped the chain or cut what it reached' and 'cut a section, a trailer or a value may hide the link'.
- threat-model.md:439 says the opposite of these for (2): 'a cross-reference stream whose data a guard cut past its last row, the chain going on' and 'An object the guard cut after a whole reference it holds hides nothing'. For (1) it says white space past the bound is 'looked for nearby as one that is not there'.

The code also leaves the chain going on at :6460 ('Data that holds none leaves the chain going on'): a value cut whose data holds no row sets no flag.

reader-limits.md:55-61 is accurate as written. The reviewer's out/r8f/ZzS02.txt and ZzS17.txt show the same codes on da2b06f; out/main/ZzS02.txt has no such Warning code.
fix: Rewrite the sentence as the code's rule, in the five src places and in diagnostics.md:116, lazy-reading.md:215-217 and threat-model.md:432 (column 2). Leave reference/reader-limits.md:55-61 as it is.

Suggested text: 'Nothing is reported where one of the reader's limits left part of the chain unread — the section count, a section it cut before it said what it is, rows, a trailer, or a value the rows or a link are read with cut short of them, or a full index —: the limit's report stands for what it left unread. A cut that hides nothing, data past a stream's last row or an object past a whole reference it holds, leaves the report made. White space past MaxTrailerLength where a section is named is read for now as a section missing (#289), and the stream is reported with it.'

Correct the comment at :3718-3719 to the same rule. It should no longer say that a limit that cut a section it reached marks the chain cut.

In threat-model.md:432 column 1, qualify 'a guard hid what names it' with 'but white space past a trailer's window, a section missing until #289'.

Optional nit: add 'nor where the chain indexed nothing, the whole file then rebuilt' to the PdfDiagnosticCodes summary, as diagnostics.md says.

Tracking: comment on #289 with the pad shape (limit.trailer + section-missing + xref.stream-passed-over + rebuilt under MaxTrailerLength=1024; read whole at defaults). Note there that its fix (the guard's section, CutByLimit) withholds the Warning through ReportLostSection, and add that row to its closing tests.

No code change in this batch. Setting the flag inside PaddingAt alone would wrongly mark the chain stopped where the search finds the section past the white space and goes on. This is text plus a debt comment, not a maintainer choice.
needs_maintainer: False

## 6. line-test-reads-back — minor → confirmed minor (src-texts)
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3706-3707 (ReportStreamsPassedOver remark 'nothing is read to report it ... and its line lexed there'), :5209-5211 (loop comment 'which the window, still open, tells'); threat-model.md:432 ('nothing is read to report it')

Claim: R9-Q2 was decided as 'no extra read', and R9a's deviation says 'IsTokenOnItsLine while the window is open, no extra read'. That fails when the typed passed-over stream's line starts before the bytes searched: IsTokenOnItsLine then reads back up to LineStartSearchLength bytes, plus the bytes searched again, only to decide the report. IsTokenOnItsLine's own remark admits 'one window more read at most for each'. The two texts above do not. The cost is bounded and avoidable.

Evidence: ZzS09 (out/head/ZzS09.txt): a typed stream 11, no /Prev, passed over; the search takes the classic table 115 bytes past it. Its line starts 720 bytes earlier, at '12 0 obj (700 x) endobj ', before the window start at 1817. c2a2aaa: newline=False gives 'bytesReadAtOpen=18896' and newline=True (an end of line before the header) gives 'bytesReadAtOpen=16055'. Both report 'Warning xref.stream-passed-over @2336'. da2b06f (out/r8f/ZzS09.txt) gives 16055 for both. The extra 2,841 bytes are 1,817 back to the file's start plus the 1,024 searched.

Fix: Reword :3706-3707 as '... and its line lexed when the search took another section, read again at most LineStartSearchLength bytes before the bytes searched where it starts before them (IsTokenOnItsLine)'. Reword :5210-5211 as '... which the window tells, read again before it where the line starts there'. Correct threat-model.md:432's 'nothing is read to report it'. Record in status.md that R9-Q2's 'no extra read' holds only where the line starts within the bytes searched.

Verdict: The finding holds, and only as a text misstatement. Behavior is right and the cost is bounded.

Since 8b31ebf (R9a), the loop that records a stream passed over (PdfFileReader.cs:5214-5222) asks IsTokenOnItsLine of each /Type /XRef place it puts back. When that place's line starts before the bytes searched, IsTokenOnItsLine (:5314-5339) opens a second window. It runs from max(0, spanStart - LineStartSearchLength) to the end of the bytes searched, so it reads the search's window again. This read exists only to decide whether the Warning is recorded. da2b06f recorded on the dictionary alone and made no such read.

The bound holds:
- at most LineStartSearchLength plus the bytes searched for each such place;
- the places are counted by the document-wide _relocationCandidatesTried budget (MaxRelocationCandidates);
- IsTokenOnItsLine's own remark states this second use and 'one window more read at most for each'.

Three texts claim no read:
- ReportStreamsPassedOver's remark (:3705-3707): 'nothing is read to report it ... and its line lexed there [in the search's window]';
- the loop comment (:5209-5211): 'which the window, still open, tells';
- threat-model.md:432: 'nothing is read to report it'. This contradicts row 434, which documents the read-back of up to 4,096 bytes for the line test.

The LineStartSearchLength summary (:141-147) also names only 'a place that cannot be read' as its use, not the stream passed over.

R9-Q2's premise 'no extra read' (decisions.md:619, RESUME.md) contrasted with reading the stream, the alternative round 9's refuter offered. The decided rule is implemented: no stream is read. But the premise does not hold to the byte where the line starts before the bytes searched.

None of this is in the accepted list or an earlier round's settlement. Round 4 settled the read-back for places that cannot be read, not for this new use. The case needs a line running from before the window to a typed header in it, so it is rare and mostly hand-built. Severity: minor (misleading text, including in the threat model, about what a hostile input costs). It is not major: the decided behavior is implemented.

One small correction to the reviewer's evidence: the extra read is [0, windowEnd). That is the bytes back to the file's start plus the 1,025-byte window (named-513 to named+512), not '1,817 + 1,024'. The total is right.

Verdict evidence: Own probe: C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r10wip/verify-line-test-reads-back/ZzVerifyLineReadBackTests.cs. It wraps the file in a source that logs every Read(offset, length).

The shape:
- objects 1 to 3 and 5;
- a 1300-byte comment;
- object 4;
- S = stream 6 (/Type /XRef, /Prev to the oldest table);
- object 10;
- the line '12 0 obj (y x filler) endobj', followed by ' ' (sameLine) or '\n';
- stream 11, with or without /Type /XRef, no /Prev;
- the oldest classic table, a 640-byte comment, then the newest table with /Prev own-7 /XRefStm S.

Outputs are out-c2a2aaa.txt and out-da2b06f.txt in the same folder. da2b06f ran in a temporary worktree, since removed.

c2a2aaa:
- typed=True filler=700 sameLine=False (line starts at the header, 2436): bytesReadAtOpen=16635, 15 reads.
- typed=True filler=700 sameLine=True (line starts at 1717, before the window [1916,2941)): bytesReadAtOpen=19576, 16 reads. The extra read is 'read [0,2941) len=2941'. It comes right after the relocated table's reads [2551,2583) and [2551,3472), so in the recording loop, and it re-reads the window [1916,2941) the search had just read.
- Both rows give 'Repair xref.offset-adjusted @2551' and 'Warning xref.stream-passed-over @2436'.
- filler=100 (line starts inside the window): 15422 for both line forms.
- typed=False: 16563 for both line forms, no Warning (R9-Q2), no extra read.

da2b06f:
- typed=True filler=700: 16635 for both line forms, Warning @2436 in both, no [0,2941) read.

The reviewer's ZzS09 output (r10wip/src-texts/out/head and out/r8f) agrees: 18896 vs 16055 on c2a2aaa, 16055 for both on da2b06f.

Texts checked on c2a2aaa: PdfFileReader.cs:3705-3707, :5209-5211, :141-147 (LineStartSearchLength), :5292-5309 (IsTokenOnItsLine remark, accurate: 'one window more read at most for each'). threat-model.md:432 ('nothing is read to report it') against :434 ('read again as far as 4,096 bytes before the bytes searched where it starts before them'). decisions.md:619 and :646, and the r9-fix-result.json R9a deviation ('No stream is read for it. Its remark now names this second use and states the bound').
fix: Texts only. No code change, and no maintainer decision needed.

1. PdfFileReader.cs:3705-3707. Replace '...: nothing is read to report it, the stream's dictionary having been parsed in the search's window when it was passed over, and its line lexed there when the search took another section.' with: '...: the stream's dictionary was parsed in the search's window when it was passed over, and its line lexed when the search took another section, in that window, or, where the line starts before the bytes searched, in those bytes read again with up to LineStartSearchLength bytes before them (IsTokenOnItsLine), one window more for each such stream; nothing is read once the chain is read.'

2. PdfFileReader.cs:5209-5211. Replace 'which the window, still open, tells' with 'which the window tells, read again from before it where the line starts there'.

3. LineStartSearchLength summary (:141-147). Add the second use: '...and a stream of /Type /XRef passed over near a hybrid table's /Prev, which is reported only where its header is such a token'.

4. threat-model.md:432. Replace 'nothing is read to report it' with words saying what deciding it costs, for example: 'nothing is read to report it but, where the stream's line starts before the bytes searched, those bytes read again from as far as 4,096 bytes before them, once for each stream so passed over'.

5. status.md batch 19 entry. Record that R9-Q2's 'no extra read' means that no stream is read: the line test reads one window more where the stream's line starts before the bytes searched, bounded as IsTokenOnItsLine states.

Optional: a HostileInputTests row pinning the extra read's bound. For example, a typed passed-over stream on a line 700 bytes long reads at most LineStartSearchLength plus the bytes searched more than the same file with an end of line before the header.

Avoiding the read in code would change behavior in either direction:
- report without the line test where the line starts before the window: a commented header gets the Warning;
- never report there: a typed header on a long line loses it.

Neither is worth it for a bounded, rare read.
needs_maintainer: False

## 7. taken-only-where-it-declares-analogy — nit → confirmed nit (src-texts)
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:4985-4987 ('as a place that cannot be read is taken only where it does'), :5208-5209 ('as a place the search takes though it cannot be read does'); diagnostics.md:116, lazy-reading.md:211-213, threat-model.md:432

Claim: The analogy used to justify the typed-only report says a place that cannot be read is taken only where it declares itself. A stream that gives only a /W, with a value a guard cut, is taken as well (#263, CutByValue), though it declares nothing. The same remark says so a few lines earlier.

Evidence: ZzS15 (out/head/ZzS15.txt): stream 11 gives '/W 12 0 R' and no /Type and no /Prev, passed over near the hybrid /Prev with no other section within reach. Under MaxObjectLength=1024: 'section /Prev named=1761 state=Malformed kind=Stream offset=1768 pad=0 cut=True typed=False', 'Repair xref.offset-adjusted @1768', 'Warning limit.object @2472'. The place is taken.

Fix: Write 'as a place that cannot be read is taken only where it does, or where a guard cut a value it is read with' in the two src places and the three pages, or drop the analogy.

Verdict: The finding holds, and it is wider than the reviewer listed by one place. The analogy ("as a place that cannot be read is taken only where it does") came in with round 9's fixes: 8b31ebf in src, b55738a in the pages. It says a place that cannot be read is taken only where it declares itself. That is false for one class: a stream that cannot be read because a guard cut a value it is read with. The acceptance test at PdfFileReader.cs:5193-5197 is `Malformed && (CutByValue || ((TypedXRef || Table) && IsTokenOnItsLine(...)))`, so a stream with no /Type that gives only a /W whose value a guard cut is taken without declaring anything. The same method's own texts say this. Its remarks at :4934-4939 say "One that cannot be read is the section all the same ... where it declared itself one ... or where a value it is read with led to an object a guard cut (#263)". Its comment at :5176-5178 says the same. reference/reader-limits.md:171-173 states the take as well. So the "only" at :4986 contradicts the remarks block it sits in, and :5209 contradicts the comment above it. A test comment, CrossReferenceChainTests.cs:6548-6551, has the same overstatement: a /W-only stream is one "the search drops where it cannot read them, and where it takes them too only once they read". The behavior itself is intended and documented; it was main's too (r3-findings:678 calls gating CutByValue optional, since main took it already). The report rule itself (R9-Q2) is stated exactly in every place: /Type /XRef, and the header a token on its line. No report, no object served and no decision changes. Only the clause given as its reason overstates, so the severity stays nit. One argument against was checked: in the pages, "cannot be read" could be taken to mean only the file's fault. In src it cannot, because the source's own wording counts the guard's value cut as "one that cannot be read". That reading does not save the pages either, since they give no qualifier.

Verdict evidence: I rebuilt the probe on c2a2aaa (origin/ccr-6d6c0ee6-dqm4lm-188), with a Release build of the test project: 0 warnings. Probe: C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r10wip/verify-taken-only-where-it-declares-analogy/ZzVerifyTakenAnalogy.cs.txt. Output: .../verify-taken-only-where-it-declares-analogy/head.txt. Stream 11 is `<< /W 12 0 R /Size 13 /Index [10 1] /Root 1 0 R /Length 7 >>`, with no /Type and no /Prev. Object 12 is `[1 4 2]` followed by 1,100 spaces. The /Prev names stream 11's offset minus 7.

(A) Hybrid, the reviewer's shape: the /XRefStm reads typed stream 6 at 1621, and there is no other section within reach.
- Under MaxObjectLength=1024: `section /Prev named=1761 state=Malformed kind=Stream offset=1768 pad=0 cut=True typed=False`, `Repair xref.offset-adjusted @1768: ... found 7 bytes after it.`, `Warning limit.object @2472: Object 12 runs past 1 KB ...`, WasRepaired False, object 5 = PdfNull. The place is taken.
- Control, the same stream unreadable through the file's fault (`/W [1 -4 2]`): `section /Prev named=1761 state=NotFound`, `Warning xref.section-missing @1761`, `xref.rebuilt`. The place is dropped.

(C) Plain /Prev, no hybrid table, an older table 183 bytes off.
- Under MaxObjectLength=1024: `section /Prev named=412 state=Malformed kind=Stream offset=419 cut=True typed=False`, `Repair xref.offset-adjusted @419: ... found 7 bytes after it.`, `Warning limit.object @1123`, object 5 = PdfNull. The stream is taken over the older table.
- Control `/W [1 -4 2]`: `section /Prev named=412 state=Relocated kind=Table offset=229`, `xref.offset-adjusted @229: ... found 183 bytes before it.`, object 5 = "old five". The stream is dropped and the older table is taken.

Texts:
- PdfFileReader.cs:4985-4987 says "as a place that cannot be read is taken only where it does".
- :5208-5209 says "as a place the search takes though it cannot be read does".
- Both contradict :4934-4939 and :5176-5178 in the same method.
- diagnostics.md:116, lazy-reading.md:213-214 and threat-model.md:432 carry the same clause.
- CrossReferenceChainTests.cs:6548-6551 also carries it; the reviewer did not list this one.

git blame dates the src lines to 8b31ebf, this batch's round 9 fix. The probe was removed from the worktree afterward; nothing is committed.
fix: Qualify the clause in all six places: the two src places, the three pages and the CrossReferenceChainTests.cs:6548-6551 comment. Suggested wording: "as a place that cannot be read is taken only where it does, but for one a guard's cut of a value it is read with keeps from being read, which is the guard's whatever it declares (#263)". A shorter alternative is "as a place a fault of the file keeps from being read is taken only where it does", which matches reference/reader-limits.md:173's own wording. Or drop the analogy and keep only the rule: /Type /XRef, and the header a token on its line. In the test comment, write "places the search drops where the file keeps them from being read". This is a text-only change: no code and no test assertions change, and no maintainer decision is needed.
needs_maintainer: False

## 8. passed-over-reached-inside-number — major → confirmed major (docs)
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3728-3736 (ReportStreamsPassedOver's told set); stated false by docs/threat-model.md:432, docs/website/docs/reference/diagnostics.md:116, docs/website/docs/concepts/lazy-reading.md:209-216, src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:117-127

Claim: xref.stream-passed-over is made for a stream the chain did read, when an older section reads it from inside its object number. The report skips only the Offset+Padding of each Read/Relocated/Malformed record. A stream read from inside its number (`13 0 obj` named at the `3`) has its record at header+1, while the search recorded the passed-over place at the header. R9-Q1 tells S by its data in the search, but the report does not tell it that way. The pages say a stream the chain reached, an older section's /Prev or /XRefStm naming it, is reported as the chain finds it and never as passed over.

Evidence: Generator W/r10wip/docs/genx.py writes gx/x-1.pdf: T2 /XRefStm S2 with /Prev 20 bytes short of X (stream 13, /Type /XRef, no /Prev, past S2). The search passes X over and takes T1, a classic table whose /XRefStm names X+1. The control gx/x-0.pdf names X at its header. Probe W/r10wip/docs/ZzR10DocsProbe.cs.txt run by run.sh, output out-x-head.txt. On c2a2aaa, x-1 gives `S /XRefStm 1247 1247 Stream Read`, then `D Warning xref.stream-passed-over at 1246: The cross-reference stream at offset 1246, near offset 1226, which the /Prev of the cross-reference table at offset 1460 names, gives no /Prev ... the search near that offset took another section first, and the chain did not read the stream. ...`, then `O 5 "new five"`, a row that X gives. The control x-0 gives the same sections (`S /XRefStm 1246 1246 Stream Read`) and no Warning. main 21a0298 has no such code: it takes X 20 bytes off, rebuilds and gives a false `Error file.root-invalid` (out-x-main.txt). The cited test A_stream_passed_over_..._reported_as_the_chain_finds_it_not_as_passed_over covers naming at the header and 1 byte short through the end of line, never inside the number.

Fix: Tell a reached stream by its data in ReportStreamsPassedOver, as the search does (R9-Q1). Option 1: keep with each passed-over place the end of its header's object-number digits, already lexed in the open window by the same code as NumberRunsInto/IsTokenOnItsLine, and skip the place when a Read/Relocated/Malformed stream record starts inside [at, digitsEnd). Option 2: when a stream is read from inside its number, also record its header start. Add rows to the reached-stream theory: the oldest table names stream 11 through /XRefStm, and through /Prev, 1 byte into its object number.

Verdict: The finding reproduces on c2a2aaa with my own generator, through /XRefStm and through /Prev, 1 and 2 bytes into the number. It is a little wider than claimed: where the stream cannot be read, the same gap brings back round 9's [1] pair at one stream. That pair is xref.section-unreadable at header+k plus xref.stream-passed-over at the header, the contradiction R9a was meant to remove.

Why it happens: ReportStreamsPassedOver's told set (PdfFileReader.cs:3728-3736) holds only Offset+Padding of each Read, Relocated or Malformed record. A link that names the stream inside its object number reads `3 0 obj`: the same dictionary and data, recorded at header+k with Padding 0. The search recorded the passed-over place at the header (:5219-5221). The two never match. The record's Offset+Padding is header+k, not header, so a reading from inside the number falls outside the told set (the test at :6363 asserts Offset+Padding == own).

Not covered by anything settled or filed:
- R9-Q1 only tells S apart in the search (NumberRunsInto, :5107), not in the report.
- R9-Q2's consequences, #171, #320, #330, #332 and #333 do not hold it.
- No round 1-9 finding or open point (a)-(e) names it, and r9-fix-result.json does not mention it.
- The harness and the corpus have no such shape.

The report is new in this batch: git grep finds no XRefStreamPassedOver at 21a0298, so main makes no such report. The texts state the rule this breaks:
- threat-model.md:432 lists 'a stream reported as passed over though the chain reached it' as a threat. It says such a stream 'an older section's /Prev or /XRefStm naming it ... is reported as the chain finds it'.
- diagnostics.md:116 and lazy-reading.md (around :215-217) say the same.
- The XRefStreamPassedOver summary (PdfDiagnosticCodes.cs:117-127) says 'the chain did not reach the stream'.
- The theory at CrossReferenceChainTests.cs:6363 ends 'never as a stream passed over'. It covers the header, 1 byte short and 5 bytes short, but not inside the number.

The message is false on both counts. The chain read the stream, and the index holds its rows: objects 4 and 5 read from it. Severity is major, as for round 9's [1], the same report at a stream the chain reached: a wrong report, contradicting the chain's own records, and doubled and contradictory where the stream is unreadable. Objects are served correctly, and the shape needs two faulty offsets.

Verdict evidence: Generator: W/r10wip/verify-passed-over-reached-inside-number/gen.py, written independently of the reviewer's genx.py. Files are in files/ and the spec is files/spec.txt.

Layout of each file:
- T0, the oldest table;
- S, stream 14 at 1149, which T2's /XRefStm reads;
- X, stream 13 or 113 at 1265, /Type /XRef, no /Prev;
- T1, either hybrid with /Prev T0 and /XRefStm X+k (kind x), or plain, indexing 1-3, with /Prev X+k (kind p);
- T2, with /Prev X-20.

Probe: ZzR10RefInsideNumber.cs.txt in the same folder, run as an uncommitted test in my worktree at c2a2aaa (Release, built clean, 0 warnings) and removed afterwards. Outputs: out-head.txt and out-head2.txt.

Readable X:
- x-n13-k0 (control): `S /XRefStm named=1265 at=1265 pad=0 Stream Read`, only `D Repair xref.offset-adjusted at 1392`.
- x-n13-k1: `S /XRefStm named=1266 at=1266 pad=0 Stream Read`, then `D Warning xref.stream-passed-over at 1265: ... the search near that offset took another section first, and the chain did not read the stream...`. Objects read `O 4 "four"`, `O 5 "new five"`, and `repaired-after=False`.
- x-n113-k1 ('13 0 obj') and x-n113-k2 ('3 0 obj') give the same.
- p-n13-k1, p-n113-k1, p-n113-k2: `S /Prev named=1266|1267 ... Stream Read` and the same Warning at 1265. The p-*-k0 controls give no Warning.

Unreadable X (/W [1 -4 2]):
- x-n13-k1-negw: `S /XRefStm named=1266 ... Stream Malformed`, `D Warning xref.section-unreadable at 1266: ... is there but cannot be read: its /W gives a field a negative width...`, AND `D Warning xref.stream-passed-over at 1265 ...`.
- p-n13-k1-negw gives the same pair (section-unreadable at 1266 and passed-over at 1265).
- The k0-negw controls give section-unreadable at 1265 only.

The reviewer's run (W/r10wip/docs/out-x-head.txt) shows the same at 1246/1247. A sibling refuter's run (W/r10wip/verify-passed-over-reached-inside-its-number/out-head.txt) shows it on the test builder's shape (own+1, 'Warning xref.stream-passed-over @1892').

Main: `git grep XRefStreamPassedOver 21a0298 -- src` finds nothing; the code arrived in 9a07bb9, and the told set was rewritten in 8b31ebf.
fix: Make the code match the texts. This needs no maintainer decision: R9-Q1 already settled that a reading started inside a stream's object number is that stream, and the pages and the theory at :6363 state the rule for the report.

1. Code: in TryRelocateXRefSection's pass-over loop (PdfFileReader.cs:5214-5222), compute the end of each recorded place's object-number digit run while the window is open. That is `passed + span[(int)(passed - spanStart)..].IndexOfAnyExceptInRange((byte)'0', (byte)'9')`, the same look as NumberRunsInto: no read, no allocation. Store it with the place: (At, DigitsEnd, Named, Table).
2. In ReportStreamsPassedOver, count a place as reached where a Read, Relocated or Malformed record's Offset+Padding lies in [At, DigitsEnd). For example, probe the told set for each offset of that run, which the window bounds, or keep the record starts sorted and search them. Keep the once-per-stream rule by adding At after the check.
3. Tests: add rows to A_stream_passed_over_near_a_hybrid_table_s_prev_that_an_older_section_reaches_is_reported_as_the_chain_finds_it_not_as_passed_over. The oldest table names stream 11 through /XRefStm and through /Prev, 1 byte into its object number, both with /W [1 4 2] (Read at own+1, no passed-over) and with /W [1 -4 2] (one section-unreadable at own+1, no passed-over). The test's record assertion then needs `section.Offset + section.Padding` to equal own+1 for those rows, or a test of its own.
4. Texts: optionally add to the theory's comment and the ReportStreamsPassedOver remark that a link reaching the stream from inside its object number reaches that stream, told by its data as the search tells S. Then run ADR 53 over the new lines.

Only if the code is not changed would the four texts need to say that a stream an older link reads from inside its object number is reported as passed over too, and that is the weaker option.
needs_maintainer: False

## 9. passed-over-under-trailer-padding-guard — minor → confirmed minor (docs)
Location: docs/threat-model.md:432; docs/website/docs/reference/diagnostics.md:116; docs/website/docs/concepts/lazy-reading.md:216-218; src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:122-123; code: src/AdCodicem.Pdf/IO/PdfFileReader.cs:5463-5476 (PaddingAt's limit.trailer leaves _cutByGuard unset), :3720

Claim: Open point (b) reproduced. Four texts say xref.stream-passed-over is never made where one of the reader's limits stopped the chain or cut what it reached. White space past MaxTrailerLength before a named section stops the chain there, yet the Warning is still made, blaming the file for a link only the guard hid.

Evidence: Generator W/r10wip/docs/genb.py writes gb/b.pdf. T0's /Prev names 700 spaces before Tm, a hybrid table whose /XRefStm names stream 9, the stream T1's /Prev search passes over. Output out-b-head.txt. With default limits: `S /Prev 186 186 Table Read`, `S /XRefStm 2158 2158 Stream Read`, and no stream-passed-over. Under MaxTrailerLength=256: `D Warning limit.trailer at 186: White space and comments run past 256 bytes where a cross-reference section is named; the section was not read from there. ...`, then `D Warning xref.section-missing at 186`, then `D Warning xref.stream-passed-over at 2158: ... the chain did not read the stream. ...`. Under ThrowOnLimit it throws that limit.trailer message. main (out-b-main.txt) has no such code and takes stream 9 instead.

Fix: Preferred, as round 9's [2]/[11] decided for the Warning: mark the chain cut by a guard where PaddingAt's guard leaves a named section unread and the search finds nothing in its place. The flag must not affect the rebuild #289 keeps. Otherwise, qualify the four texts: "but for white space before a named section that MaxTrailerLength cuts (#289)". In that case, comment on #289 with gb/b.pdf.

Verdict: The finding holds, and it is a little wider than reported. At c2a2aaa the white-space guard in TryReadXRefSection does not mark the chain as cut by a guard. That guard is the padding<0 branch at PdfFileReader.cs:5463-5476: PaddingAt's limit.trailer, raised where a section is named. Because of this, ReportStreamsPassedOver's ChainStoppedByGuard test at :3720 does not stop the Warning. The texts state the rule without exception: threat-model.md:432, diagnostics.md:116, lazy-reading.md:216-218 and PdfDiagnosticCodes.cs:121-123. So do the src comments: ReportStreamsPassedOver's summary at :3696-3698, the comment at :3717-3718 ("each marks the chain cut by a guard"), and the _streamsPassedOver doc at :627. They all say nothing is reported where one of the reader's limits stopped the chain or cut what it reached. Here limit.trailer is reported, the named section is not read, the chain ends there, and the Warning is still made. ThrowOnLimit throws, which shows the stop is the guard's. A second shape has the same root cause and also contradicts the texts' 'a stream the chain reached, an older section's /XRefStm naming it, is reported as the chain finds it, a guard's cut among it': there an older table's /XRefStm names the white space before the very stream that was passed over. The count guard stops the chain at the same link and does withhold the Warning, so two limits that hide the same link get different treatment. This is not the #289 behavior already known (limit.trailer, section-missing, rebuild, section-not-found Error, all accepted). It is that the batch's new Warning contradicts its own texts, so it qualifies under the brief's 'a text misstates it'. Main has no such code. It relocates T1's /Prev to stream 9, never reaches the guard, and ends in file.root-invalid. Severity is minor, not major. The behavior belongs to #289's class, where the white-space guard is taken for a missing section and section-missing, a rebuild and section-not-found are already charged to the file. Round 9's [2]/[11] triage did not list this guard, the fix stage flagged it, and decisions.md left it as open point (b).

Verdict evidence: The reproducer was rebuilt at c2a2aaa in a Release build in this worktree, and on main 21a0298 in a worktree of 21a0298 that has since been removed. Probe: W/r10wip/verify-passed-over-under-trailer-padding-guard/ZzR10VerPadProbe.cs.txt, the reviewer's dump probe renamed. Drivers: run.sh and mkspec.py. Generators: gen.py (pad/dict/str/none, each with or without Tm's /XRefStm) and genx.py (the /XRefStm shape). Files are in g/, with the reviewer's b.pdf and b-control.pdf copied there. Outputs: out-head-1.txt, out-head2.txt, out-head3.txt and out-main.txt.

(1) g/pad-x.pdf (= b.pdf), default limits: sections startxref T1 Read, /XRefStm 2022 Read, /Prev 2138 to 1866 Table Relocated, /Prev 186 Table Read, /XRefStm 2158 Stream Read. Only xref.offset-adjusted is reported; no stream-passed-over.
Under MaxTrailerLength=256: /Prev 186 is Unknown NotFound, and the dump reports, in order:
- `D Warning limit.trailer at 186: White space and comments run past 256 bytes where a cross-reference section is named; the section was not read from there. ...`
- `D Warning xref.section-missing at 186`
- `D Warning xref.stream-passed-over at 2158: ... the chain did not read the stream. ...`
Then repaired-after=True with xref.rebuilt, and the validator gives `Error xref.section-not-found at offset 186`. Under ThrowOnLimit the read throws PdfLimitExceededException with that limit.trailer message.

(2) The same file under MaxXRefSectionCount=2, which stops the chain at the same link (186): limit.xref-section-count is reported and there is no stream-passed-over. So does g/none.pdf under count 2, though it makes the Warning under default limits.

(3) g/pad.pdf and b-control.pdf, where Tm has no /XRefStm: default limits make the Warning at 2144, as a whole read does. Under MaxTrailerLength=256 the output is limit.trailer, section-missing, then the Warning at 2144 again, made although a limit stopped the chain.

(4) g/xpad700.pdf: the older table Tm's /XRefStm names the first of 700 spaces before stream 9. T1's /Prev lies 20 bytes into stream 9, and the search takes T0 first.
- Default limits: /XRefStm 1330 Stream Read, no Warning.
- MaxTrailerLength=256: `/XRefStm 1330 Unknown NotFound`, then `limit.trailer at 1330`, `xref.section-missing at 1330`, `xref.stream-passed-over at 2030`, a rebuild, and `Error xref.section-not-found at offset 1330`.
- g/xpad400.pdf (400 spaces, inside the 512-byte search): the stream is found moved past the white space, with no Warning. That case is correct.

(5) main 21a0298 on pad.pdf, pad-x.pdf and b.pdf, under every limit: /Prev 2138 relocated to stream 9 at 2158, xref.rebuilt at opening, then `Error file.root-invalid`. No guard is reached, and the code xref.stream-passed-over does not exist on main (grep finds 0 in PdfDiagnosticCodes.cs).

Code: in the padding<0 branch, ReachLimit is followed only by Fault "holds neither the xref keyword nor an object" and State NotFound. No flag is set. ChainStoppedByGuard (:1105) = _chainFilledIndex || _cutByGuard. r9-fix-result.json (R9a, carried by R9c and R9f) records this as 'not handled'.
fix: Preferred: (A) code. This applies round 9's [2]/[11] rule (ADR 34, #264: the Warning is not made where a guard ended the chain or may hide a link) to one more guard, so it needs no new decision.
- In TryReadXRefSection's padding<0 branch, for a chain-named offset, record that a guard hid the section in a new field. Candidates never reach that branch.
- ReportStreamsPassedOver returns early where such a link ended NotFound, meaning the search found nothing in its place.
- Do not use _cutByGuard. It drives IndexStoppedByGuard, Rebuild(_cutByGuard ? _chainIndex : null) and the validator's ChainStoppedByGuard (CrossReferenceProbe.cs:315/492), so #289's section-missing, rebuild and section-not-found stay as they are.
- Rows: pad-x.pdf and xpad700.pdf under MaxTrailerLength 256 give no xref.stream-passed-over, with limit.trailer and section-missing kept. pad.pdf under default limits keeps the Warning (control). xpad400.pdf is unchanged.
- Mutate the new set and the new check.
- Bring the remark at :3703-3708 and the comment at :3717-3718 in line by naming the white space before a named section among the limits.

Fallback: (B) texts. Use this if the fix stage leaves all of #289's class to #289 (batch 27).
- Qualify all seven texts: the four the finding names, plus ReportStreamsPassedOver's summary at :3696-3698, the comment at :3717-3718 and the _streamsPassedOver doc at :627. The wording would add "but where white space and comments past MaxTrailerLength before a section the chain names leave it unread, which the chain takes for a section not there (#289)".
- Fix the threat model's 'A stream the chain reached ... /XRefStm naming it' clause the same way, for xpad700.pdf.
- Comment on #289 with pad-x.pdf and xpad700.pdf as one more report charged to the file there.

Either way, b55738a's body ('none of the reader's limits stopped the chain or cut what it reached') is false for this case until (A) lands. Record it under 'Commit bodies not rewritten' in status.md.
needs_maintainer: False

## 10. trailer-value-row48-hybrid-prev-read — minor → narrowed minor (docs)
Location: docs/website/docs/reference/validation-rules.md:48; src/AdCodicem.Pdf/Validation/Rules/TrailerValueWrongRule.cs:35-38

Claim: Open point (d). Row 48 lists the hybrid stream's own /Prev under "A value the reader does not read ..., which would read what the reading never did". Since R8/R9 the reader does read it, through references, to tell S's place near the table's /Prev, and the outcome depends on it. threat-model.md:439 says it "is read only to tell that stream's place", and reference/reader-limits.md:213-214 says the search "reads [it] to tell that stream's place". The validator still judges it as written, which is right since the read-through is forgotten (9823992). The pages disagree. R9d updated diagnostics.md:111 and reader-limits.md:164 to "which it does not follow" but missed row 48.

Evidence: Generator W/r10wip/docs/genS.py writes gS/. Outputs out-S-head.txt and out-S2-head.txt. S gives `/Prev 20 0 R`, and S's rows index object 20. When object 20 holds 394, the table's /Prev (s-ref-table.pdf): `S /Prev 394 212 Table Relocated`, no rebuild. When object 20 holds 212, the oldest table (s-ref-oldest.pdf): `S /Prev 394 401 Stream Relocated`, `D Warning xref.hybrid-stream-lost at 401`, `F Error xref.hybrid-stream-wrong`, rebuilt once asked. The reader read the value through the reference. When object 20 holds 394.0 (s-ref-table-real.pdf), the rule says only `writes its /Prev as the reference 20 0 R, where ISO 32000-1 makes it direct (7.5.8.2).`, so it is judged as written. When S is taken and the chain follows that /Prev (s-ref-oldest-real.pdf, 212.0), the rule says `... to a real number: ... and Table 17 of ISO 32000-1 an integer.`

Fix: Reword row 48: "A value the reader does not follow is judged as the file wrote it, a reference it holds not followed: a hybrid file's cross-reference stream's own /Prev, which the chain does not follow, going on through the table's. That /Prev is read only to tell that stream's place near the offset the table's /Prev names, as the chain reads a /Prev, and what it read through is forgotten unless the search takes the stream (#156); ...". Give TrailerValueWrongRule's remarks (:35-38) the same reading.

Verdict: The finding holds for row 48. It names one file it should not, and it misses one that has the same misstatement.

Row 48 (docs/website/docs/reference/validation-rules.md:48) has this text, unchanged from main: "A value the reader does not read is judged as the file wrote it, a reference it holds not followed, which would read what the reading never did: a hybrid file's cross-reference stream's own `/Prev`, which the chain does not follow, ...". The colon makes S's /Prev an example of a value the reader does not read. That has been false since this batch:
- 3a1ac79 (round 7's fixes) made the search near a hybrid table's /Prev tell S by its own /Prev.
- 9a07bb9 and 7ff4f39 (round 8) made it read that /Prev through references, through StreamPrevious and ReadWithoutLoading, up to MaxChainLength (PdfFileReader.cs:7418-7432).
- Main 21a0298 has none of MayBeTablesOwn, StreamPrevious or readInStreamsPlace (git grep finds 0 matches; c2a2aaa has 30).

So the clause "which would read what the reading never did" is false for this item too, and the pages disagree. threat-model.md:439 says that /Prev "is read only to tell that stream's place". reference/reader-limits.md:213-214 says the search "reads [it] to tell that stream's place". b55738a (R9d) changed diagnostics.md:111 and reader-limits.md:164-165 to "which it does not follow" and left row 48 as it was.

The validator's choice is still right, and the finding says so. Where S is not taken, the rule judges S's /Prev as written and does not follow the reference. Where the search takes S, S is a section the /Prev reached, and its /Prev is followed.

The finding is not settled by any decision or issue. decisions.md lists it as open point (d) for this round to judge. The R9d and R9f stages left it unchanged, suggesting the words "which the chain does not follow", which row 48 already contains. No round 1 to 9 finding mentions row 48, and none of the listed issues holds it.

Narrowed: TrailerValueWrongRule.cs:35-38 does not misstate. It says "So are a hybrid file's stream's own /Prev, which the reader does not follow", which is true.

Widened: the public XML doc of PdfValidationRuleIds.FileTrailerValueWrong (src/AdCodicem.Pdf/Validation/PdfValidationRuleIds.cs:76-77, unchanged from main) says the same thing more strongly: "The reader reads such a value all the same, or does not read it at all — a hybrid file's stream's own /Prev, a /W element past the third —". This text reaches the generated API reference.

Severity is minor: two texts misstate what the reader does, one of them on the public API. Behavior is correct.

Verdict evidence: Rebuilt on c2a2aaa, at the detached head of origin/ccr-6d6c0ee6-dqm4lm-188:
- Generator: W/r10wip/verify-trailer-value-row48-hybrid-prev-read/genS.py, a copy of the reviewer's, writing into g/.
- Probe: ZzR10Row48Probe.cs.txt (run as ZzR10Row48Probe, then deleted).
- Spec: spec.txt. Output: out-head.txt.
- Release build: 0 warnings, 0 errors.

The files differ only in object 20, which S's /Prev 20 0 R names and which S's own rows index:

s-ref-table.pdf (20 holds 394, the table's /Prev):
  S /XRefStm 401 401 Stream Read
  S /Prev 394 212 Table Relocated
  no rebuild
  F Warning file.trailer-value-wrong at offset 401: ... writes its /Prev as the reference 20 0 R, where ISO 32000-1 makes it direct (7.5.8.2).

s-ref-oldest.pdf (20 holds 212):
  S /XRefStm 401 401 Unknown NamedAgain
  S /Prev 394 401 Stream Relocated
  S /Prev 212 212 Table Read
  D Warning xref.hybrid-stream-lost at 401
  repaired-after=True
  F Error xref.hybrid-stream-wrong at offset 1175

So the reader reads the value through the reference, and the outcome depends on it.

s-ref-table-real.pdf (20 holds 394.0, S not taken): only "writes its /Prev as the reference 20 0 R, where ISO 32000-1 makes it direct (7.5.8.2)." The value is judged as written.

s-ref-oldest-real.pdf (212.0, S taken as the /Prev's section): "writes its /Prev as the reference 20 0 R, to a real number: ISO 32000-1 makes it direct (7.5.8.2), and Table 17 of ISO 32000-1 an integer."

This matches the reviewer's out-S-head.txt and out-S2-head.txt.

Texts:
- validation-rules.md:48 has the same clause as main 21a0298. The batch added only the #208 sentence.
- PdfValidationRuleIds.cs:76-77 is identical on main and on the head.
- b55738a's diff adds "which it does not follow" to diagnostics.md and reader-limits.md only.
- `git log -S StreamPrevious 21a0298..c2a2aaa` gives 9a07bb9.
- 3a1ac79's message: "A stream near that /Prev is now told by its own /Prev".
fix: Docs only, no design choice.

1. validation-rules.md:48: keep "A value the reader does not read ..." for the values it really does not read (the /Prev and /XRefStm of a table an /XRefStm names in a stream's place, the /W elements past the third, what decoding does not read). Give S's /Prev a sentence of its own, for example: "A hybrid file's cross-reference stream's own `/Prev`, which the chain does not follow, going on through the table's, is judged as written too, a reference it holds not followed: it is read only to tell that stream's place near the offset the table's `/Prev` names, nothing loaded or reported, and what it read through a guard's cut is forgotten unless the search takes the stream, which is then a section that `/Prev` reached, its own `/Prev` judged as the chain read it ([#156])."

2. PdfValidationRuleIds.cs:76-77: change the summary to "The reader reads such a value all the same, reads it only to tell a place — a hybrid file's stream's own <c>/Prev</c> —, or does not read it at all — a <c>/W</c> element past the third —, which is then judged as written."

3. TrailerValueWrongRule.cs:35-38 is accurate as it stands. It may gain the same reason ("read only to tell that stream's place, what it read through forgotten"), but this is optional.

A docs: commit, Refs #156.
needs_maintainer: False

## 11. check-drops-stream-length-guard-docs — major → narrowed major (docs)
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:6535-6549 (the check's RollBack and its comment "which a reading of the stream meets again"); docs/threat-model.md:432 ("reporting and recording nothing — but for a guard its decoding beside a value a guard cut reached"), contradicted by docs/threat-model.md:434 and :440

Claim: Open point (a) reproduced, with main as control. The check's main-path decode rolls back a limit.stream-length report that no later reading makes. Under ThrowOnLimit the same guard throws, but by default it is never reported, which breaks invariant 12 and regresses from main. The comment says a later reading meets it again, which is false. threat-model 432 describes the gap as the rule. 440 says an object read at the offset the chain names keeps "the guards it reached", and 434 says a guard the decoding reached "stands": both are false for this shape.

Evidence: Shape: HybridFiles.PrevNamingAStreamBesideTheOldestTable("", padding: 100, filter: "/Foo"), dumped to W/r10wip/docs/shapes/beside-untyped.pdf by ZzR10DocsShapes.cs.txt. Output out-shapes-head.txt. On c2a2aaa under MaxStreamLength=50, the diagnostics are `xref.offset-adjusted, xref.hybrid-stream-lost, filter.unsupported at 1765, xref.offset-adjusted`, with no limit.stream-length. Under MaxStreamLength=50,ThrowOnLimit it gives `THROW PdfLimitExceededException: A stream's data runs past 50 bytes; ...`. main 21a0298 on the same file (out-beside-main.txt): `D Warning limit.stream-length at 1765: A stream's data runs past 50 bytes; ...`. The typed variant keeps its report on c2a2aaa.

Fix: Use RollBackBesideLimits at :6549, as e157359 did beside a cut value, and pin the shape: untyped, MaxStreamLength 50, limit.stream-length once. Then drop "beside a value a guard cut" from threat-model 432's clause and from the KeepsItsPlace and check remarks, and correct the :6536 comment.

Verdict: Open point (a) holds and is the batch's own regression. The check's main-path decode (PdfFileReader.cs:6546-6549, from 092b772, which is not in main) uses plain RollBack. That drops the limit.stream-length entry, but the reader's _limitsReached set still holds (StreamLength, position). ReachStreamLength (:8668-8676) reports only a position it has not seen, so no later reading of that stream reports the guard again. Other decoding reports are different: filter.unsupported comes back through the _decodingReports mark, which is why only the guard goes missing. The code already says this at :6420-6424, in the beside-cut branch that e157359 moved to RollBackBesideLimits: "reported once for the stream, where its data is first read (ReachStreamLength), and no later reading reports it". The main path kept RollBack.

The result is a guard that throws under ThrowOnLimit but is never reported by default, which breaks invariant 12 and ADR 34. It is also worse than the finding says: after the index is rebuilt, object 4 is served as a PdfStream, and GetRawBytes returns 50 of its 107 bytes with no limit.stream-length at any point. PdfStream.GetRawBytes promises that report "once for the stream". main 21a0298 reports the guard on the same file.

Texts:
- The :6535-6538 comment ("drops what the decoding reports, which a reading of the stream meets again") is false for the guard and true for the other reports.
- threat-model 432, the KeepsItsPlace remark at :3864-3865 and the checkOnly paragraph of TryReadXRefStream at :6225-6226 match the code. But they limit the standing guard to "beside a value a guard cut", so they describe the gap as the rule. Each needs that qualifier dropped once the code is fixed.
- Narrowed: threat-model 434 (a dropped candidate's guard "stands") and 440 (a reading at the named offset keeps "the guards it reached") describe their own paths correctly. Those paths keep guards. They fail on this file only because the check, run earlier, swallowed the report. With the one-word code fix both hold without any edit, so they need none.

This is not a design choice. e157359 already settled the same question for the beside-cut branch, and invariant 12 settles it here.

Verdict evidence: Probes are in W/r10wip/verify-check-drops-stream-length-guard-docs/ (W = C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud). ZzVerifyCdslGen.cs writes HybridFiles.PrevNamingAStreamBesideTheOldestTable(type, padding: 100, filter: "/Foo") to files/untyped.pdf and files/typed.pdf (oldest=1459, four=1675, named=1647). ZzVerifyCdslRead.cs opens each file under default limits, MaxStreamLength=50, and MaxStreamLength=50 with ThrowOnLimit, then loads object 4 and reads its raw bytes.

On c2a2aaa (out-head.txt), untyped.pdf with MaxStreamLength=50:
- Sections: S /XRefStm 1647 1647 Unknown NamedAgain; S /Prev 1675 1459 Table Relocated.
- Diagnostics at open: xref.offset-adjusted at 1459, xref.hybrid-stream-lost at 1647, filter.unsupported at 1765, xref.offset-adjusted at 1459. No limit.stream-length.
- Reading object 4: 'O 4 PdfStream', 'O 4 declared length 107, raw bytes read 50'. The only diagnostics added after open are xref.rebuilt and object.redefined. No guard report at any point.
- With ThrowOnLimit: 'THROW PdfLimitExceededException: A stream's data runs past 50 bytes; ...'.
- The typed control keeps 'D Warning limit.stream-length at 1777'.

On main 21a0298, in my own worktree (out-main.txt), the same untyped.pdf with MaxStreamLength=50: S /XRefStm 1647 1675 Stream Relocated, then 'D Warning limit.stream-length at 1765: A stream's data runs past 50 bytes; only what lies within it was read. Raise PdfReaderLimits.MaxStreamLength to read past it.', filter.unsupported, xref.offset-adjusted.

`git merge-base --is-ancestor 092b772 21a0298` returns false; `git log -S decodingChecked` points only at 092b772.

I then changed only :6549 to `_diagnostics.RollBackBesideLimits(decodingChecked);` (fix.diff). Untyped with MaxStreamLength=50 then gives exactly limit.stream-length at 1765, offset-adjusted, hybrid-stream-lost, filter.unsupported, offset-adjusted (out-fixed.txt). That is the order GuardCutValueTests already pins for the cut-/W row. Object 4 still reads 50 bytes and the report is not doubled. The whole AdCodicem.Pdf.Tests project passes with the change (suite-fixed.log: total 7878, failed 0, skipped 20, remote corpus not linked), so no test pins the dropped guard. The worktree is restored, the probes are deleted and the main worktree is removed.
fix: 1. PdfFileReader.cs:6549: change `_diagnostics.RollBack(decodingChecked)` to `_diagnostics.RollBackBesideLimits(decodingChecked)`, as e157359 did at :6436.
2. Rewrite the :6535-6538 comment. It should say the check drops what the decoding reports, which a reading of the stream meets again, except a guard it reached, which stands. The reason: the MaxStreamLength cut is reported once for the stream, where its data is first read (ReachStreamLength), and no later reading reports it.
3. Drop the 'beside a value a guard cut' qualifier from the KeepsItsPlace remark (:3864-3865), from the checkOnly paragraph of TryReadXRefStream (:6225-6226) and from threat-model 432's clause. The clause becomes 'but for a guard its decoding reached, which no later reading reports'.
4. Pin the shape. Either add a cut: null row to GuardCutValueTests.A_guard_reached_decoding_a_stream_beside_a_value_a_guard_cut_is_reported_once_whatever_the_look_at_the_prev_s_place_drops (renamed, since there is then no cut value), or write a sibling test. Untyped, MaxStreamLength 50: exactly [limit.stream-length, offset-adjusted, hybrid-stream-lost, filter.unsupported, offset-adjusted]. Typed: [hybrid-stream-lost, limit.stream-length, filter.unsupported, section-unreadable]. Optionally assert that reading object 4's raw bytes afterwards adds no second limit.stream-length. The row kills the RollBack/RollBackBesideLimits mutant.
5. threat-model 434 and 440 need no edit: they hold once the code is fixed.
6. For the status.md record: 092b772's check dropped this guard until the fix.
needs_maintainer: False

## 12. commit-9823992-british-spelling — nit → confirmed nit (docs)
Location: commit 9823992 body, line 'the judgement read through is forgotten where the stream is put back,'

Claim: 9823992's body uses the British "judgement". The convention requires American spelling in commit messages ("judgment"). Nothing else in the 13 messages or the added doc lines is British.

Evidence: `git log --format=%B da2b06f..c2a2aaa | grep -o -i -E '\b\w*(our|ise|ised|yse|ogue|lled|tre|judgement|whilst|defence|licence)\b'` gives `judgement` once, plus filled/rolled/otherwise, which are not British forms. check_msgs.py passes all 13 messages: subject <=100, footer `Refs #n[, #m]`, blank line, Co-Authored-By.

Fix: None in history: pushed bodies are not rewritten. Record it under status.md's 'Commit bodies not rewritten' if the batch lists style slips; otherwise nothing.

Verdict: The finding holds. Line 22 of 9823992's body reads "the judgement read through is forgotten where the stream is put back,". "Judgement" is the British form; the American form is "judgment". CLAUDE.md's Conventions and ADR 20 (amended 2026-09-27) require American spelling in commit messages. No other British form appears in the 13 messages (da2b06f..c2a2aaa) or in any line the batch adds to src/, tests/ or docs/. All 13 messages pass check_msgs.py. Commit bodies already pushed are not rewritten, so nothing in history can change. The status.md section 'Commit bodies not rewritten' has so far listed only bodies that are incomplete or inexact (status.md:814, and every entry in decisions.md). This body is accurate; only its spelling is off. Recording it there is therefore optional, not owed. Not a maintainer question.

Verdict evidence: Worktree detached at origin/ccr-6d6c0ee6-dqm4lm-188 = c2a2aaa. `git log --format=%B -1 9823992`, line 22: "the judgement read through is forgotten where the stream is put back,".

Probe: C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r10wip/verify-commit-9823992-british-spelling/brit.py. It is a wide British-form regex: -our, -ise/-isation, -yse, -ogue, -lled/-lling, -tre, whilst, amongst, licence, defence, grey, programme, judgement, acknowledgement, learnt, ageing, and more. It ran over:
- msgs.txt (all 13 bodies): one hit only, "(236, 'judgement', 'the judgement read through is forgotten where the stream is put back,')".
- the '+' lines of diff.txt (git diff da2b06f c2a2aaa, 2,390 lines): no hits.

C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/lot19/tools/check_msgs.py on msgs0.txt prints OK for all 13. Subject lengths: c2a2aaa 91, b55738a 91, e157359 97, e3d0c99 74, 44db26a 93, 68a55fa 94, ee0c231 89, f8550c7 93, df6b1fb 84, 9823992 94, ee33070 87, 74a7668 97, 8b31ebf 100. check_msgs.py does not check spelling, and the repo has no CI spelling check, which is how the slip got through.

Side observation, outside the batch: the same form is on main in docs/adr/0046-every-milestone-ends-with-an-adversarial-review.md:103 ("keeps the maintainer's judgement"). It was added by 34a6f2e on 2026-09-29, after the American conversion. Elsewhere the repo writes "judgment" (ADR 34:276, ADR 44:37, ADR 53:98 and :114, corpus-contributions.md:216).
fix: Leave history as it is; pushed bodies are not rewritten. Optionally, at the batch's end, add one clause beside the batch's entries under status.md's 'Commit bodies not rewritten': "9823992 writes the British 'judgement'". That section has so far recorded only inexact bodies, so this is not owed. If the fix stage makes a docs commit anyway, it can also correct the pre-existing "judgement" in docs/adr/0046-every-milestone-ends-with-an-adversarial-review.md:103 to "judgment". That is optional and comes from main, not from this batch.
needs_maintainer: False

## 13. check-main-path-drops-stream-length-guard — major → confirmed major (undecoded: 68a55fa and e157359)
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:6549 (texts at :3864-3865, :6224-6226, :6534-6538, docs/threat-model.md:432)

Claim: Open point (a) holds, and it reaches further than stated. KeepsItsPlace looks at a hybrid table's /Prev place. Where the stream there gives no /Type, its data is decoded in TryReadXRefStream's main path, and the decoding is then dropped with a plain `_diagnostics.RollBack(decodingChecked)`. That rollback also drops limit.stream-length. ReachStreamLength has already keyed the report in _limitsReached, so the /Prev's own reading, the candidate search and any later read meet the cut but never report it again. MaxStreamLength's cut is therefore never reported, which breaks invariant 12. This happens for readable untyped streams as well as undecodable ones. Main 21a0298 reports the guard. The loss comes from 092b772, a commit of this batch, so the decision's rule ('a fix here unless main loses the report too') says fix it here. The source and threat-model texts describe this loss as intended: 'nothing ... reported, but for a guard its decoding beside a value a guard cut reached'. The comment at :6534-6538, 'drops what the decoding reports, which a reading of the stream meets again', is false for this guard: a later reading meets the cut again but does not report it.

Evidence: Generator W/r10wip/undecoded/ZzUndGen.cs; files W/r10wip/undecoded/files/a1_untyped_foo.pdf, a3_untyped_plain.pdf, a7_typed_plain.pdf; outputs out-main.txt, out-da2.txt, out-head.txt.
(1) a1 = HybridFiles.PrevNamingAStreamBesideTheOldestTable("", padding: 100, filter: "/Foo"), MaxStreamLength 50:
- main: [limit.stream-length, filter.unsupported, xref.offset-adjusted]
- da2b06f and c2a2aaa: [xref.offset-adjusted, xref.hybrid-stream-lost, filter.unsupported, xref.offset-adjusted]. No limit.stream-length.
(2) a3 = PrevNamingAStreamBesideTheOldestTable("", padding: 100), no filter, readable data of 107 bytes:
- MaxStreamLength 50: main [limit.stream-length, xref.offset-adjusted]; da2b06f and c2a2aaa [xref.hybrid-stream-lost]. The /Prev's section is read from data cut at 50 bytes, and no guard is reported.
- MaxStreamLength 5: c2a2aaa [xref.hybrid-stream-lost, xref.rebuilt, object.redefined]. Its typed twin a7 gives [xref.hybrid-stream-lost, limit.stream-length, xref.rebuilt, object.redefined].
(3) Prototype fix, RollBackBesideLimits(decodingChecked) at :6549 (out-fix1.txt, out-fix3.txt):
- a1 MaxStreamLength 50: [limit.stream-length, xref.offset-adjusted, xref.hybrid-stream-lost, filter.unsupported, xref.offset-adjusted]
- a3 MaxStreamLength 50: [limit.stream-length, xref.hybrid-stream-lost]
- a3 MaxStreamLength 5: [limit.stream-length, xref.hybrid-stream-lost, xref.rebuilt, object.redefined]
- Each guard is reported once. In a4 (Flate past MaxDecodedStreamLength 1000) and a5 (MaxFilterChainLength 1), the single limit report moves ahead of hybrid-stream-lost and is still made once.
- Without the SuppressedCount fix below, a6 = [/FlateDecode /Foo] under MaxDecodedStreamLength 1000 gets SuppressedCount 1 (out-fix1.txt).
- With fix3.diff (both fixes): 2,922 tests of the 10 neighboring classes pass, and the whole unit suite with the remote corpus hard-linked gives 10,565 total, 0 failed, 3 skipped (suite-fix3.txt). No existing test pins the loss.
(4) ThrowOnLimit, unchanged: a1 and a3 throw PdfLimitExceededException limit.stream-length from KeepsItsPlace during Open, as on da2b06f. Main threw from TryRelocateXRefSection.

Fix: At :6549, `_diagnostics.RollBackBesideLimits(decodingChecked)`, together with the SuppressedCount fix below; without it a re-decoding counts a phantom suppressed entry.
Texts:
- KeepsItsPlace remark :3864-3865: 'but for a guard its decoding reached', without 'beside a value a guard cut'.
- TryReadXRefStream remark :6224-6226: the same change.
- Comment :6534-6538: say the guard the decoding reaches stands, since no later reading reports it.
- The comment beside :6419-6426: drop 'as at a place dropped' and restrict nothing to the cut.
- threat-model.md:432: the same change to its clause.
Tests: rows with exact codes for the a1 shape and the a3 shape (MaxStreamLength 50 and 5). They could be cut: null rows of GuardCutValueTests.A_guard_reached_decoding_a_stream_beside_a_value_a_guard_cut_is_reported_once_whatever_the_look_at_the_prev_s_place_drops, with that test renamed.

Verdict: The finding holds as stated. I rebuilt the reproducer myself on c2a2aaa, da2b06f and main 21a0298.

**What goes wrong.** KeepsItsPlace's check decodes an untyped stream in TryReadXRefStream's main path (PdfFileReader.cs:6546-6549). That decoding goes through FileStreamData.GetBytes, which calls ReachStreamLength. ReachStreamLength makes the limit.stream-length report and adds (StreamLength, position) to _limitsReached. The check then calls plain `_diagnostics.RollBack(decodingChecked)`, which drops that entry. _limitsReached is not rolled back on this path, so no later reading ever reports the guard, though every later reading meets the cut again: the search, the /Prev's own reading, a later GetBytes.

**Scope.** It happens for a readable stream as well as an undecodable one. Under MaxStreamLength 5 the cut empties the rows, the index is rebuilt, and xref.rebuilt and object.redefined are reported with no limit.* at all. That is a guard's cut blamed on the file, which breaks invariant 12 (ADR 34).

**Origin and the decision.** The line comes from 092b772, a commit of this batch: `git show 092b772` adds `_diagnostics.RollBack(decodingChecked)`. Main reports the guard in every shape I tried. decisions.md's open point (a) says "a fix here unless main loses the report too", so this is not a maintainer choice. It is not filed: no open issue covers it, and #283 is a different defect.

**Texts.**
- The comment at :6534-6538 ("drops what the decoding reports, which a reading of the stream meets again") is false for this guard: a later reading meets the cut but does not report it.
- The KeepsItsPlace remark (:3864-3865), the TryReadXRefStream remark (:6224-6226) and threat-model.md:432 limit the guard that stands to one reached "beside a value a guard cut". They give the reason as "no later reading reports it", which holds just as much for the main path. They describe the loss accurately, but as if it were intended.

**Reach.** The default MaxStreamLength is int.MaxValue, so the loss needs a lowered limit or data over 2 GB. It also needs the check itself: an /XRefStm that misses, with a /Prev naming an untyped stream. That reach is narrow, but by the brief's scale a missing guard report that breaks an invariant is major. The fix stage treated the same loss in the cut-value branch (e157359) as an invariant 12 regression.

**Secondary claim.** I reproduced it too: the one-word swap alone makes SuppressedCount 1 for [/FlateDecode /Foo] under MaxDecodedStreamLength 1000. The cause is that MoveDecodingReportsTo counts a guard a repeated decoding reaches again in _limits without adding an entry, and RollBackBesideLimits then counts it as left out by the capacity. The reviewer's out-head.txt:313 (b4 mol_mdsl, suppressed 1) shows the same phantom already happens on head in the cut-value branch.

ThrowOnLimit is unchanged by the fix: the check throws at open, as on da2b06f.

Verdict evidence: My own probes are in W/r10wip/verify-check-main-path-drops-stream-length-guard/wt19/:
- ZzR19Gen.cs.txt writes the files to files/, from HybridFiles.PrevNamingAStreamBesideTheOldestTable with padding 100:
  - u_foo: untyped, /Foo
  - u_plain: untyped, no filter, 107 bytes readable
  - t_foo, t_plain: the /Type /XRef twins
  - u_plain0: padding 0
  - u_flate, u_flatefoo: Flate-encoded rows plus 4,000 zero bytes, filter Flate or [/FlateDecode /Foo]
- ZzR19Read.cs.txt reads the files on any tree.
- Outputs: out-main.txt, out-da2.txt, out-head-full.txt, out-fix1.txt, out-fix2.txt, neighbors-fix2.txt.

Diagnostic codes, main 21a0298 vs c2a2aaa (da2b06f is identical to c2a2aaa in every row):

| File, limit | main 21a0298 | c2a2aaa |
|---|---|---|
| u_foo, MaxStreamLength 50 | [limit.stream-length, filter.unsupported, xref.offset-adjusted] | [xref.offset-adjusted, xref.hybrid-stream-lost, filter.unsupported, xref.offset-adjusted] |
| u_plain, MaxStreamLength 50 | [limit.stream-length, xref.offset-adjusted] | [xref.hybrid-stream-lost] |
| u_plain, MaxStreamLength 5 | [limit.stream-length, xref.offset-adjusted] | [xref.hybrid-stream-lost, xref.rebuilt, object.redefined] |
| u_plain0, MaxStreamLength 5 | [limit.stream-length, xref.offset-adjusted] | [xref.hybrid-stream-lost, xref.rebuilt, object.redefined] |
| u_flate, MaxStreamLength 5 | — | [xref.hybrid-stream-lost, xref.rebuilt, object.redefined] |

The typed twins keep the guard on head:
- t_plain, MaxStreamLength 5: [xref.hybrid-stream-lost, limit.stream-length, xref.rebuilt, object.redefined]
- t_foo, MaxStreamLength 50: [xref.hybrid-stream-lost, limit.stream-length, filter.unsupported, xref.section-unreadable]

The other guards are kept on head:
- u_flate, MaxDecodedStreamLength 1000: [xref.hybrid-stream-lost, limit.decoded-stream]
- u_flatefoo, MaxDecodedStreamLength 1000: limit.decoded-stream reported once

ThrowOnLimit at 50: u_foo and u_plain throw PdfLimitExceededException at ReachStreamLength < GetBytes < TryReadXRefStream:6547 < KeepsItsPlace:3884. Main throws from TryReadXRefSection.

**Prototype fix 1 (fix1.diff): RollBackBesideLimits at :6549.**
- u_foo at 50: [limit.stream-length, xref.offset-adjusted, xref.hybrid-stream-lost, filter.unsupported, xref.offset-adjusted]
- u_plain at 50: [limit.stream-length, xref.hybrid-stream-lost]
- u_plain at 5: [limit.stream-length, xref.hybrid-stream-lost, xref.rebuilt, object.redefined]
- u_plain0, u_flate and u_flatefoo at 5 likewise; each guard is reported once.
- u_flate at MaxDecodedStreamLength 1000: limit.decoded-stream moves ahead of hybrid-stream-lost, still once.
- u_flatefoo at MaxDecodedStreamLength 1000: suppressed=1, against 0 on head.

**Fix 1 plus the clamp at PdfDiagnostics.cs:169 (fix2.diff, the same lines as the reviewer's fix3.diff).**
- u_flatefoo at MaxDecodedStreamLength 1000: suppressed=0. No other row changes from fix 1.
- 2,479 tests of CrossReferenceChain, CrossReferenceRule, Diagnostics, FilterDamage, Filter, GuardCutValue, HostileInput, ReaderLimits and XRefTable pass, 0 failed. The reviewer's whole suite gave 10,565 total, 0 failed. No test pins the loss.

The worktree is restored to c2a2aaa, clean.
fix: 1. **The fix.** PdfFileReader.cs:6549: `_diagnostics.RollBackBesideLimits(decodingChecked);`, as e157359 did in the cut-value branch.
2. **The suppressed count.** In PdfDiagnostics.RollBackBesideLimits (:169), count as left out only what the capacity left out. A guard a repeated decoding reaches again is counted in _limits without an entry (MoveDecodingReportsTo), so it must not be counted. Either:
   - clamp: `Math.Clamp(_limits - mark.Limits - (kept - mark.Count), 0, _suppressed - mark.Suppressed)`; or
   - more exact: count the limit.* entries the capacity dropped since the mark.

   This also removes the phantom already on head in the cut-value branch. Without it, the swap extends the phantom to the main path.
3. **Texts.** Drop "beside a value a guard cut", so that a guard any decoding of the check reached stands, since no later reading reports it:
   - KeepsItsPlace remark :3864-3865
   - TryReadXRefStream remark :6224-6226
   - threat-model.md:432
   - the comment at :6534-6538: say the decoding's reports are dropped but for a guard it reached, which stands, because ReachStreamLength reports the cut once for the stream and no later reading reports it.
4. **Tests.**
   - Exact-code rows for the untyped no-filter shape at MaxStreamLength 50 and 5, and the untyped /Foo shape at 50. They could be no-cut rows beside GuardCutValueTests.A_guard_reached_decoding_a_stream_beside_a_value_a_guard_cut_is_reported_once_..., with that test renamed so it no longer says "beside a value a guard cut".
   - A SuppressedCount-is-0 row for [/FlateDecode /Foo] under MaxDecodedStreamLength 1000.
   - The ADR 53 mutants (RollBackBesideLimits back to RollBack, the clamp dropped) are killed by these rows.

No maintainer decision is needed: decisions.md's open point (a) already rules on it, since main reports the guard.
needs_maintainer: False

## 14. rollback-beside-limits-phantom-suppressed — major → confirmed major (undecoded: 68a55fa and e157359)
Location: src/AdCodicem.Pdf/Diagnostics/PdfDiagnostics.cs:169, reached through src/AdCodicem.Pdf/IO/PdfFileReader.cs:6436 (e157359) and :6445/:5242 (68a55fa)

Claim: RollBackBesideLimits assumes that every guard counted in _limits since the mark has a report, either kept or dropped by the capacity. MoveDecodingReportsTo breaks that assumption on purpose (#144, 'A guard reached stays counted ... its report moved or not'): when a decoding reaches a guard again, it counts the guard in _limits but skips the held report.
How the phantom arises:
- Since e157359, the look keeps limit.decoded-stream beside a cut value (RollBackBesideLimits at :6436).
- The search then tries the same place as a candidate. The candidate's FaultBesideCut decoding reaches the guard again; the report is held and skipped, but counted.
- The place is dropped, and 68a55fa's mark rolls it back beside the limits (:5242). limitsLeftOut then comes to 1.
Result: SuppressedCount reads 1 with 5 entries and a capacity of 1,000. That is false against its contract, 'How many entries were dropped because Capacity was reached' (reference/diagnostics.md:73; PdfDiagnostics.SuppressedCount docs). It is a regression of this round: da2b06f and main report 0. The fix for open point (a) widens it to shapes with no cut value (a6).

Evidence: File W/r10wip/undecoded/files/b4_untyped_cutSize_flatefoo.pdf (ZzUndGen's Hyb("", "/Filter [/FlateDecode /Foo]", Flate(row + 4,000 NULs), cut: "/Size")), opened with MaxObjectLength 1024 and MaxDecodedStreamLength 1000:
- c2a2aaa: [limit.decoded-stream, xref.offset-adjusted, xref.hybrid-stream-lost, filter.unsupported, xref.offset-adjusted], 'suppressed 1' (out-head.txt, out2-head.txt)
- da2b06f: [limit.decoded-stream, filter.unsupported, xref.offset-adjusted, xref.hybrid-stream-lost, xref.offset-adjusted], SuppressedCount 0
- main: [xref.offset-adjusted, limit.object], SuppressedCount 0
With only RollBackBesideLimits at :6549, a6_untyped_flatefoo (no cut, MaxDecodedStreamLength 1000) also shows 'suppressed 1' (out-fix1.txt).
Two prototypes:
- Changing MoveDecodingReportsTo to stop counting repeats fails 3 tests that pin the counting: DiagnosticsTests.Rolling_back_beside_the_guards_holds_again_as_moved_in_only_the_guard_reports_a_decoding_moved_in, DiagnosticsTests.A_report_a_decoding_moved_in_before_takes_no_room_once_the_diagnostics_are_full_and_a_guard_stays_counted, and HostileInputTests.A_guard_an_object_stream_reaches_is_held_once_by_its_document_and_counted_each_time.
- Clamping in RollBackBesideLimits (fix3.diff) gives SuppressedCount 0 for b4 and a6, entries unchanged, and the whole suite passes (10,565, 0 failed).

Fix: In RollBackBesideLimits, count as left out only guards the capacity dropped since the mark: `var limitsLeftOut = Math.Max(0, Math.Min(_limits - mark.Limits - (kept - mark.Count), _suppressed - mark.Suppressed));`. The prototype used Math.Clamp, which throws if the upper bound were ever negative.
Correct the comment 'Every guard reached since the mark was reported once' and the summary.
Tests:
- A DiagnosticsTests unit test: a guard kept by a rollback beside the limits, the same decoding reached again and rolled back beside the limits, SuppressedCount unchanged.
- The b4 row asserting SuppressedCount 0, beside its exact codes.

Verdict: The finding reproduces on c2a2aaa, at the file level and in a unit test of PdfDiagnostics alone. b4 opened with MaxObjectLength 1024 and MaxDecodedStreamLength 1000 gives Count 5, SuppressedCount 1, Capacity 1000. da2b06f gives 0 on the same file, and main does not decode the stream at all. So it is a regression of this round.

The mechanism is as claimed, and it takes both commits:
- The look at the hybrid /Prev's place (e157359, PdfFileReader.cs:6431-6436) now keeps limit.decoded-stream through RollBackBesideLimits and holds it as moved in.
- KeepsItsPlace answers None, so the same place is tried as a candidate and decoded again. MoveDecodingReportsTo skips the held repeat but still adds 1 to _limits.
- The place is dropped, and the besideCut mark 68a55fa records (:6445) is rolled back at :5242. limitsLeftOut is then (1 - 0) = 1.
- With 68a55fa alone the look used RollBack, so the second decoding reported the guard afresh: no phantom. Only the combination makes it.

The phantom reaches no reader decision: SyntaxFaultSince reads _pending, not the document's record. What is wrong is the public SuppressedCount, against its documented contract ('How many entries were dropped because Capacity was reached', PdfDiagnostics.cs:47 and reference/diagnostics.md:73). By the brief's scale that is a wrong report, so major. It is narrow: a hostile hybrid file, a value a guard cut, and an untyped stream whose filters reach MaxDecodedStreamLength and then decode nothing.

None of the listed issues, findings or decisions holds this case: #169 is about guards lost at capacity, not a count with no drop behind it.

The reviewer's proposed clamp fixes b4, but it is not exact once the record is full.

Verdict evidence: Probes are under W/r10wip/verify-rollback-beside-limits-phantom-suppressed/, where W = C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud. Files: ZzVerPhantom.cs, ZzVerPhantomUnit.cs, b4_untyped_cutSize_flatefoo.pdf and a6_untyped_flatefoo.pdf (copied from r10wip/undecoded/files).

File probe (ZzVerPhantom), b4 under mol_mdsl:
- c2a2aaa (out-head.txt): 'Count 5 Suppressed 1 Limits 3 Capacity 1000', entries [limit.decoded-stream, xref.offset-adjusted, xref.hybrid-stream-lost, filter.unsupported, xref.offset-adjusted]. After reading objects 1 to 11: Suppressed 1, Limits 4.
- da2b06f (out-da2.txt, built in W/base-da2b06f, probe deleted after): 'Count 5 Suppressed 0 Limits 3', entries [limit.decoded-stream, filter.unsupported, xref.offset-adjusted, xref.hybrid-stream-lost, xref.offset-adjusted].
- main 21a0298 (r10wip/undecoded/out-main.txt): [xref.offset-adjusted, limit.object], with no 'suppressed' line, so 0.
- a6 under mdsl1000: Suppressed 0 on both c2a2aaa and da2b06f. As the reviewer says, a6 only shows the phantom once open point (a)'s RollBackBesideLimits is applied at :6549.

Unit probe (ZzVerPhantomUnit, out-unit-head.txt), no file involved:
- Steps: a decoding reaches the guard and reports beside it, and the result is rolled back beside the limits. A second decoding meets the same two reports and is rolled back beside the limits to a mark taken before it.
- Result: 'Count 1 Suppressed 1 Limits 2 Capacity 1000'.

The reviewer's clamp, Math.Max(0, Math.Min(..., _suppressed - mark.Suppressed)), on c2a2aaa (out-unit-fix.txt, out-fix.txt):
- b4 gives 0 and the unit case with room gives 0.
- A full record (Capacity 2) still gives 1 where 0 is exact. There the guard repeat is held and skipped, while a filter report beside it is dropped by the capacity and then rolled back: the clamp counts that drop as a guard. Output: 'full/after candidate dropped: Count 2 Suppressed 1 Limits 2'.

Exact variant (exact.diff):
- Change: a count of guard reports the capacity dropped, incremented in Add's full branch for limit.*, carried by the mark, restored by RollBack, carried over by both MoveTo overloads and MoveDecodingReportsTo, and used as limitsLeftOut.
- b4 gives 0, and both unit cases give 0 (out-unit-exact.txt, out-exact.txt).
- 2,117 tests pass (0 failed): DiagnosticsTests, HostileInputTests, GuardCutValueTests, *CrossReference*, *Hybrid*, *Limit*, *Relocat*.
- Worktree restored and clean.

Not reproduced, a possible wider case: the main-path candidate mark (:6615, from an earlier round of the batch) reaches the same :5242 rollback wherever a kept decoding of the same stream already holds the guard. The fix in PdfDiagnostics covers every call site either way.
fix: Correct it in PdfDiagnostics rather than at the call sites. RollBackBesideLimits should count as left out only the guard reports the capacity actually dropped since the mark.

Preferred, exact (exact.diff):
- Add a private _limitsSuppressed, incremented in Add's full branch when the code starts with 'limit.'.
- Carry it in PdfDiagnosticsMark (a LimitsSuppressed field), restore it in RollBack, and add the source's delta to the target in both MoveTo overloads and in MoveDecodingReportsTo.
- In RollBackBesideLimits: `_suppressed = mark.Suppressed + (_limitsSuppressed - mark.LimitsSuppressed)`, keeping _limitsSuppressed as it stands.

Minimal alternative: the reviewer's clamp `Math.Max(0, Math.Min(_limits - mark.Limits - (kept - mark.Count), _suppressed - mark.Suppressed))`, not Math.Clamp. It is exact while the record has room. Once full, it still over-counts by up to the number of held guard repeats that have non-guard reports beside them which the capacity drops. If chosen, say so in its comment.

Either way, rewrite the comment at PdfDiagnostics.cs:168 ('Every guard reached since the mark was reported once…'), which MoveDecodingReportsTo makes false on purpose, and the method's summary.

Tests:
- A DiagnosticsTests unit test: a guard kept by a rollback beside the limits, the same decoding met again and rolled back beside the limits, SuppressedCount still 0. Add a full-capacity case: a held guard met again beside a report the capacity drops, SuppressedCount unchanged after the rollback.
- The b4 shape as a GuardCutValueTests or HybridFiles row, asserting its exact codes and SuppressedCount 0.

Both fixes are mechanical; neither needs a design decision from the maintainer.
needs_maintainer: False

## 15. m03-not-equivalent — minor → confirmed minor (undecoded: 68a55fa and e157359)
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:6427

Claim: R9c classed M03 (dropping `!xrefStream.Data.CutByGuard` from `judged`) as equivalent. Its reason was that the parser marks data cut only for a /Length a guard cut or left out of the index, which `deferred is null` excludes. That reason is false. TryParseNumberedAt also marks the data cut by MaxTrailerLength's window (MarkCutByGuard, :8441). This happens where the window ends right after a `stream` keyword followed by a regular byte: DataStart returns -1 and the parse is truncated. A direct /Length leaves deferred null. A test can build this input, so the mutant is a test gap, not an equivalent.

Evidence: File W/r10wip/undecoded/files/m03_streamx_cutW_foo.pdf (ZzUndGen). It is ChainFiles.UnderAnUpdate with object 6 '<< /Type /XRef /W 5 0 R /Size 8 /Filter /Foo /Length 7 >>stream' + 'xxxxxxx', the newer table's /Prev naming object 6. Opened with MaxTrailerLength 71 (exactly '6 0 obj\n' + dictionary + 'stream') and MaxObjectLength 1024:
- c2a2aaa: [limit.trailer, limit.object]
- M03 mutant (W/r10wip/undecoded/out-m03.txt): [limit.trailer, filter.unsupported]. The section is judged undecoded, the file's fault, and the cut object is never loaded, so its guard goes unreported.
The other three option sets of that file give the same output on both.

Fix: Add a test row for this shape pinning [limit.trailer, limit.object] and section Malformed with CutByValue. Correct R9c's ADR 53 record: M03 is killed by the new row, and 4 equivalents remain (M20, M26, M27, M05).

Verdict: The finding holds. R9c gave this reason for classing M03 (dropping `!xrefStream.Data.CutByGuard` from `judged`, PdfFileReader.cs:6427) as equivalent: "the parser marks data cut ... only for a /Length a guard cut or left out of the index, and such a /Length is deferred". That reason is false.

How the data gets marked cut with a direct /Length:
- TryParseNumberedAt itself marks the data cut (MarkCutByGuard, :8441 and :8631). It does so when the window is at its maximum and the parse is truncated.
- The parse is truncated with a direct /Length when the window ends on a `stream` keyword followed by a regular byte. PdfObjectParser.ReadStream (:825-834) asks DataStart, which returns -1, so `_truncated = true`. Finish then places the data at the window's end, so `Position + Length >= windowEnd`.
- A direct /Length leaves `deferred` null.

Why an unknown or failing filter tells the mutant apart:
- RowsHeldAtMost (:7264) returns null for data that is cut, so with no filter the mutant changes nothing. That is why the existing theory, A_stream_whose_data_the_trailer_guard_cuts_beside_a_width..., does not kill it.
- PdfFilterPipeline sets `undecoded` whatever the cut (:299, :274). So with /Foo, or Flate on bytes that are not deflate data, FaultBesideCut returns DataUndecoded. That fault is the file's, and it replaces CutByValue.

M03 also survived the 2,547 tests of the eight reader classes I ran it against, which matches R9c's whole-suite run. So the survivor is a test gap, not an equivalent. Rounds 7 and 8 settled the same kind of finding (M09, then H3) as minor.

Narrowing: I did not run M05. Part of its reason rests on the same premise. EndHidden is set only by the parser's hidden-/Length path (PdfObjectParser.cs:908), and ReadThroughWithoutLoading defers such a /Length, so M05 is likely still equivalent. Its one-line reason must still be rewritten not to cite the false premise.

Severity: minor. No behavior is wrong at c2a2aaa. The record is false and a test is missing.

Verdict evidence: Reproduced on c2a2aaa in my worktree (Release build). Probes and outputs are in W/r10wip/verify-m03-not-equivalent/.

**Probe ZzM03V.cs** builds the reviewer's shape independently:
- the file is ChainFiles.UnderAnUpdate;
- object 6 is `<< /Type /XRef /W 5 0 R /Size 8 [filter] /Length 7 >>stream` + `xxxxxxx`;
- the newer table's /Prev names object 6 at 1326;
- MaxTrailerLength is set to the window (71 for /Foo, 79 for Flate);
- the files are written as m03v_*.pdf.

Output on c2a2aaa (out-head.txt, and out-head2.txt after the revert, identical):
- foo_x, mtl_mol: open gives `limit.trailer@1326, limit.object@203`. Section `/Prev Malformed @1326 cutByValue=True`, fault "its /W is the reference 5 0 R, which one of the reader's limits cut". Object 6 is PdfNull.
- flate_x, mtl_mol: same result.

Output with M03 (out-m03.txt):
- foo_x, mtl_mol: `limit.trailer@1326, filter.unsupported@1397`. Section `/Prev Malformed cutByValue=False`, fault "its data could not be decoded".
- flate_x, mtl_mol: `limit.trailer@1326, filter.failed@1405`, the same section verdict.

Every other row is identical on c2a2aaa and M03: def, mtl and mol, the end-of-line control (foo_eol), and no filter.

**Probe ZzM03W.cs** takes the existing theory's own shape, HybridFiles.NamingAStreamWhoseKeywordEndsAt(2048, "X"), and writes `/Filter /Foo ` (or `/Filter /FlateDecode `) over 13 bytes of its padding, so every offset is kept. Limits are MaxObjectLength 1024 and MaxTrailerLength 2048.
- c2a2aaa (outW-head.txt): foo and flate both give `limit.trailer@2087, limit.object@5383` and `/Prev Malformed @2087 cutByValue=True`, as the theory's "X" row asserts.
- M03 (outW-m03.txt): foo gives `limit.trailer@2087, filter.unsupported@4135`; flate gives `filter.failed@4135`. Both give `/Prev Malformed cutByValue=False` with "its data could not be decoded".

**Survival:** M03 passed all 2,547 tests of CrossReferenceChainTests, GuardCutValueTests, CrossReferenceRuleTests, ReaderLimitsTests, DocumentReaderTests, StreamLengthTests, XRefTableTests and UndecodedStream* (2 skipped).

The mutant is reverted, the probes are removed and the worktree is clean.
fix: 1. Kill M03 with a row on the existing theory:
   - Give HybridFiles.NamingAStreamWhoseKeywordEndsAt a `filter` parameter, written into object 4's dictionary in place of as many padding bytes, so the keyword still ends at keywordEnd.
   - Add rows ("X", false, "/Filter /Foo ") and ("X", false, "/Filter /FlateDecode ") to A_stream_whose_data_the_trailer_guard_cuts_beside_a_width_a_guard_cut_leaves_the_prev_s_place_to_the_guard. Its existing assertions already pin head's behavior: [limit.trailer, limit.object], /Prev Malformed with CutByValue, object 7 null, no rebuild.
   - Extend the theory's comment: data a guard's window cut is not decoded to judge the section, even under a filter that decodes nothing of it.
   - The reviewer's ChainFiles.UnderAnUpdate shape works as well.
2. Correct R9c's ADR 53 record for the batch's docs/status.md entry: 31 mutants, 26 killed (M03 by the new rows), 4 equivalent (M20, M26, M27, M05), 1 not viable.
3. Rewrite M05's one-line reason without the false premise: EndHidden is set only for a /Length a guard cut or left out of the index (PdfObjectParser.cs:908), which ReadThroughWithoutLoading defers. Or run M05 against such a shape to confirm it.

Nothing here needs the maintainer.
needs_maintainer: False

## 16. reader-limits-names-one-guard-beside-cut — minor → narrowed minor (undecoded: 68a55fa and e157359)
Location: docs/website/docs/reference/reader-limits.md:198-200

Claim: The page says that, to tell whether the filters decode anything, the data is decoded beside the cut and 'MaxDecodedStreamLength may be reached there, reported before the guard that cut the value ... and thrown by Open in its place under ThrowOnLimit'. MaxStreamLength and MaxFilterChainLength are reached there too, in shapes where main reaches neither. Under ThrowOnLimit, Open then throws them in place of the cut value's limit.object. The page names only one of the three guards.

Evidence: W/r10wip/undecoded/files2, outputs out2-head.txt and out2-main.txt, all with MaxObjectLength 1024.
- b6_untyped_cutSize_plain (an untyped stream with /Size 9 0 R, no filter, 107 bytes), MaxStreamLength 50: c2a2aaa [limit.stream-length, limit.object]; main [xref.offset-adjusted, limit.object]. Under ThrowOnLimit, c2a2aaa throws limit.stream-length from FaultBesideCut < UndecodedFault < RowsHeldAtMost; main throws limit.object from GetObject.
- b5_untyped_cutSize_chain ([/ASCIIHexDecode /ASCIIHexDecode]), MaxFilterChainLength 1: c2a2aaa [limit.filter-chain, limit.object]; main [xref.offset-adjusted, limit.object]. Under ThrowOnLimit, c2a2aaa throws limit.filter-chain from the same frames.

Fix: Say that any guard the decoding meets may be reached there: MaxStreamLength, MaxDecodedStreamLength, MaxFilterChainLength. Name the same in status.md's record of the accepted consequence.

Verdict: The finding holds. I rebuilt it on c2a2aaa, with the files hand-built by my own generator and the reviewer's hybrid shape, and compared against main 21a0298. Where a guard cut a stream's /Size or its whole /Index, UndecodedFault -> RowsHeldAtMost -> PdfFilterPipeline.Decode now runs at opening. That decoding reaches MaxStreamLength (when GetRawBytes reads the data) and MaxFilterChainLength (at ReportEnd), as well as MaxDecodedStreamLength. Each one is reported before the limit.object of the cut value, and under ThrowOnLimit Open throws it instead of limit.object. Main reaches none of the three in these shapes, because its FaultBesideCut returns null before decoding when /Size or /Index is cut. This happens with a typed stream and an untyped one, named directly through /Prev or through the hybrid /XRefStm. reader-limits.md:198-200 names only MaxDecodedStreamLength. The accepted consequence (decisions.md, R8 open point and R9 fix result) was also recorded with that one guard only, so the wider behavior was never written down. The text does not misstate MaxDecodedStreamLength, but it leaves out two guards that behave the same way, which a ThrowOnLimit caller will meet. Two points narrow the finding. (1) The consequence is the same kind for all three guards and no worse. Each report is true: a whole read reaches the same guard. (2) The sentence says 'whatever value the guard cut', so it also covers a cut /W with the counts read. There, main already decoded beside the cut and already reached all three guards, throwing them in place of limit.object, but main's page said nothing about it. The batch spread this to /Size and /Index cuts, and its page now names one guard of three. The decoding beside the cut also reports what it meets of the file's own faults at opening, for example a Repair filter.failed for ASCIIHex with no '>'. A whole read reports that too, and the UndecodedFault summary says so. This is a side note, not a separate finding.

Verdict evidence: Probes: W/r10wip/verify-reader-limits-names-one-guard-beside-cut/ZzVgbcRead.cs (generator ZzVgbcGen and reader ZzVgbcReadAll; it compiles on main too) and ZzVgbcGen2.cs (the reviewer's HybridFiles.PrevNamingAStreamBesideTheOldestTable("", padding: 100, cut: "/Size"), branch only). Files are in files/, outputs in out-head.txt (c2a2aaa) and out-main.txt (21a0298), and a side-by-side view in summary.txt (made by summ.py).

Shape: an oldest table, then stream object 4 (/Size 9 0 R, or /Index 9 0 R, or /W 9 0 R; /W [1 4 2]; /Prev oldest). The newest classic table indexes objects 6 and 9 (object 9 is 1,100 bytes long) and names object 4 exactly through /Prev. Every run uses MaxObjectLength 1024.

- cutSize_untyped_plain107 (no filter, 107 bytes of data), MaxStreamLength 50.
  - c2a2aaa: 'Warning limit.stream-length @1694 ... Raise PdfReaderLimits.MaxStreamLength', then 'Warning limit.object @3042'.
  - main: limit.object only.
  - Under ThrowOnLimit, c2a2aaa throws 'code limit.stream-length limit MaxStreamLength @1694' from ReachStreamLength < FileStreamData.GetBytes < PdfStream.GetRawBytes < PdfFilterPipeline.Decode < RowsHeldAtMost < UndecodedFault < FaultBesideCut. Main throws limit.object (MaxObjectLength @3042).
- cutSize_untyped_hexhex ([/ASCIIHexDecode /ASCIIHexDecode]), MaxFilterChainLength 1.
  - c2a2aaa: [limit.filter-chain @1735, limit.object]. main: [limit.object].
  - Under ThrowOnLimit, c2a2aaa throws limit.filter-chain from ReportEnd < DecodeChain < Decode. Main throws limit.object.
- The same pairs hold for the typed versions (/Type /XRef) and for every cutIndex_* file.
- cutSize_*_flate4000 with MaxDecodedStreamLength 1000 is the documented case: c2a2aaa [limit.decoded-stream, limit.object] and throws limit.decoded-stream; main [limit.object].
- Reviewer's hybrid shape, MaxStreamLength 50: c2a2aaa [limit.stream-length, limit.object] and throws limit.stream-length. main [xref.offset-adjusted, limit.object] and throws limit.object. This matches the reviewer's out2-*.txt.
- Narrowing control, cutW_untyped_*: main already gives [limit.stream-length, limit.object], [limit.filter-chain, limit.object] and [limit.decoded-stream, limit.object], and throws each one under ThrowOnLimit, the same as c2a2aaa.

Code: main's FaultBesideCut returns null before RowsHeldAtMost on an unread /Index or /Size. c2a2aaa calls UndecodedFault(stream) there (PdfFileReader.cs:7165-7175), which decodes.

Text: reader-limits.md:198-200 reads 'MaxDecodedStreamLength may be reached there, reported before the guard that cut the value ... and thrown by Open in its place under ThrowOnLimit'. It is the only page that names a guard here; threat-model.md:432 says 'a guard its decoding ... reached' without naming one.

The record of the accepted consequence also names only MaxDecodedStreamLength (decisions.md:584 and :676).
fix: Reword reader-limits.md:198-200 so that it names every guard the decoding meets, not one. For example: 'To tell whether they do, the data is read and decoded beside the cut, whatever value the guard cut: a guard that reading or decoding it meets — `MaxStreamLength`, `MaxFilterChainLength`, `MaxDecodedStreamLength` — may be reached there, reported before the guard that cut the value, which is reported once the chain is read, and thrown by `Open` in its place under `ThrowOnLimit`.' The batch-end record in docs/status.md, which is not written yet, should name the same three guards, and so should the accepted-consequence line in decisions.md. No code change is needed and no maintainer decision is needed. The decoding beside the cut is the accepted mechanism, and these two guards follow from it in the same way as MaxDecodedStreamLength. A test pinning ThrowOnLimit with MaxStreamLength or MaxFilterChainLength beside a cut /Size could go next to the existing MaxDecodedStreamLength rows, but it is optional.
needs_maintainer: False

## 17. passed-over-reached-inside-its-number — major → narrowed major (own-stream (8b31ebf, ee33070, )
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3734 and :3740 (ReportStreamsPassedOver's records check); also :5118 (the search's reached test)

Claim: The [1]/[10] fix decides that the chain reached a stream by comparing start bytes: a record's Offset + Padding against the first byte of the passed-over header. Sometimes the chain reaches the same stream through a link that names a place inside its object number: an older table's /XRefStm or /Prev naming header+1, or a newer table's /XRefStm read before the search. That record then starts at header+1. The Warning is still made and says 'the chain did not read the stream'. It adds that the index lacks the objects only that stream indexes, yet the chain read the stream and serves those objects. The cause is the one R9-Q1 fixed for S: the stream is told by a byte, not by its data. The search's own reached test (:5118, `_sectionsReadAt.Contains(candidate)`) is also by byte. So where that stream gives a /Prev to another offset, the search takes it a second time and misses hybrid-stream-lost and the Error hybrid-stream-wrong; that half behaves the same on main.

Evidence: Probe W/r10wip/own-stream/ZzR10OwnTests.cs. ZzG1 uses OwnShape, a copy of HybridFiles.PrevFallingShortOfTheStreamBeforeTheTablesOwn('nothing', shortOfOwn: true, oldestNames: '/XRefStm'), with the oldest /XRefStm rewritten from own to own+1. Output on c2a2aaa, in p3-head.txt: sections [startxref Read Table 2609, /XRefStm NamedAgain 1703, /Prev Relocated Stream 1703, /Prev Read Table 292, /XRefStm Read Stream 1893]. Codes [xref.offset-adjusted@1703, xref.hybrid-stream-lost@1703, xref.stream-passed-over@1892 'The cross-reference stream at offset 1892, near offset 1885, ... the chain did not read the stream. ...']. GetObject(10) = "ten", WasRepaired false. With the exact naming (own+0), the same sections end in /XRefStm Read Stream 1892 and there is no Warning. With /Prev at own+1: Read Stream 1890 and the Warning at 1889. ZzG1d: the newest table's /XRefStm reads stream 11 at 878 before the search near the middle table's /Prev; result: stream-passed-over@877. When stream 11's /Prev names the oldest table, the search takes 877 again: '/XRefStm Read Stream 878 ... /Prev Relocated Stream 877', codes [offset-adjusted@877], no Error. The exact naming gives [offset-adjusted@721, hybrid-stream-lost@721] and the Error hybrid-stream-wrong. da2b06f gives the same output (the bug dates from 9a07bb9). main 21a0298 has no such Warning; in G1 it rebuilds with file.root-invalid. Seeded differential, seed 202611081414, reachedcheck.py over gen-head.txt: 64 of 2,000 head blocks (23 of 400 files) put the Warning at a header whose number a Read, Relocated or Malformed section starts inside; da2b06f has 93.

Fix: When a place is recorded as passed over, also record where its number's digit run ends. The window is open then, and it is the same scan NumberRunsInto does. In ReportStreamsPassedOver, skip the place where a reached record starts within [at, at + digits). Add test rows for the oldest table's /XRefStm and /Prev at own+1 and the newest table's /XRefStm at own+1 (G1, G1d). For the search's byte-based reached test at :5118 (main's class), either apply the same digit-run test against _sectionsReadAt or file it as debt beside #330/#332, and name it among threat-model 432's known gaps. Until then, diagnostics.md:116 is false for this shape where it says 'A stream the chain reached — an older section names it ... — is reported as the chain finds it'.

Verdict: The first half holds and is this batch's own defect. ReportStreamsPassedOver (PdfFileReader.cs:3728-3743 on c2a2aaa) decides the chain reached a stream by start byte only: `told.Add(section.Offset + section.Padding)`, then `told.Add(at)`. Sometimes a link names a byte inside a stream's object number, and the chain reads the stream from there. The record then starts at header+k, while the place the search passed over is the header. So the Warning is still made, and it says 'the chain did not read the stream' and that 'the index the chain read lacks the objects only it indexes'. In fact the chain read that stream's dictionary and data, and object 10, which only it indexes, is served. The same false Warning shows up through three links: an older table's /XRefStm at own+1, an older table's /Prev at own+1, and a newer table's /XRefStm read at own+1 before the search. A three-digit object number named at own+2 gives it too. With the exact naming there is no Warning. These texts are false for this shape:
- diagnostics.md:116 ('A stream the chain reached — an older section names it ... — is reported as the chain finds it');
- threat-model.md:432 (the same clause);
- the remark at PdfFileReader.cs:3700-3702;
- the XRefStreamPassedOver summary ('the chain did not reach the stream').
The Warning code is new in this batch (9a07bb9). 8b31ebf's record check did not change the result: the reviewer's da2b06f run gives the same output. main has no such code. On main, shape A takes stream 11 at its header, rebuilds, and gives Error file.root-invalid, so head reads better there except for this Warning. None of the accepted items or open issues holds it. #330, #332 and #333 cover other shapes. The 'not marking the index incomplete' item and R8-Q3's kagain Warning are about a stream that is truly never read. Round 9's [1] was the same kind of defect: a passed-over Warning at a stream the chain reached, a wrong report with nothing served wrong. It was settled as major, and so is this. The cause is the one R9-Q1 fixed for S: the stream is told by the byte its reading started at, not by its data. No new maintainer choice is needed, because the fix applies R9-Q1's principle and makes the existing texts true.
The second half is narrowed away. The search's byte-based reached test (:5118) takes the stream again only in the newer-/XRefStm shape, and only where the stream's /Prev names another offset. That behaves exactly as main does: main takes 877 whether it was named at 877 or 878. It is also #332's open question: whether the search may take a stream a newer table's /XRefStm read. The brief lists that consequence as accepted and tracked there. With the exact naming the batch passes the stream over as reached, which is #332's subject; one byte in, it takes it, as main does. That is worth a comment on #332, not a defect or a new debt for this batch.

Verdict evidence: I rebuilt the reproducer independently with a self-contained builder (my own copy of the PrevFallingShortOfTheStreamBeforeTheTablesOwn('nothing', shortOfOwn: true, oldestNames) shape, plus a newer-/XRefStm shape) in W/r10wip/verify-passed-over-reached-inside-its-number/ZzRefInsideNumberTests.cs.txt. I ran it on c2a2aaa and on main 21a0298 in my worktree; the probe has since been removed and the worktree is back at c2a2aaa, clean. The outputs are in W/r10wip/verify-passed-over-reached-inside-its-number/mine-head.txt and mine-main.txt. W = C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud.

On c2a2aaa:
- **Oldest /XRefStm at own+1 (own=1892):** sections end in 'S /XRefStm named=1893 Read Stream at=1893+0'. Diagnostics: 'D Repair xref.offset-adjusted @1703', 'D Warning xref.hybrid-stream-lost @1703', 'D Warning xref.stream-passed-over @1892'. Objects: 4="four" 5="five" 10="ten", repaired False before and after the reads. Message: '...the search near that offset took another section first, and the chain did not read the stream. If it is that table's own stream, the index the chain read lacks the objects only it indexes...'. The findings are the same as with the exact naming: [Warning:xref.section-shifted, Error:xref.hybrid-stream-wrong, Warning:page-tree.kid-invalid].
- **Exact naming (own+0):** 'Read Stream at=1892+0', diagnostics offset-adjusted and hybrid-stream-lost only, 10="ten".
- **Oldest /Prev at own+1 (own=1889):** 'Read Stream at=1890+0' and 'Warning xref.stream-passed-over @1889'. At own+0 there is no Warning.
- **Stream renamed 111, oldest /XRefStm at own+1 and at own+2:** 'Read Stream at=1893' or '1894', and 'Warning xref.stream-passed-over @1892' both times.
- **Newer table's /XRefStm at 878 = own+1, stream 11 giving no /Prev:** 'S /XRefStm named=878 Read Stream at=878+0', '... /Prev named=870 Relocated Stream at=721+0', and 'D Warning xref.stream-passed-over @877'. Objects 10="ten" 12="twelve", repaired False. At 877 there is no Warning.
- **Same newer shape, stream 11 giving /Prev to the oldest table:** at into=1, 'S /Prev named=870 Relocated Stream at=877+0', diagnostics [offset-adjusted@877], findings [file.size-wrong x2, xref.section-shifted]. At into=0: [offset-adjusted@721, hybrid-stream-lost@721], findings including Error:xref.hybrid-stream-wrong.

On main 21a0298:
- No stream-passed-over anywhere.
- Shape A at own+0 and own+1 alike: 'S /Prev named=1885 Relocated Stream at=1892+0', then 'xref.offset-adjusted @1892', 'xref.rebuilt', 'object.redefined', repaired True, findings [Error:file.root-invalid, ...].
- Newer shape with /Prev oldest, at 877 and 878 alike: 'S /Prev named=870 Relocated Stream at=877+0', [offset-adjusted@877], findings [file.size-wrong x2, section-shifted]. Head's into=1 matches main here, so the second half is main's behavior.

The reviewer's da2b06f run (W/r10wip/own-stream/p3-da2.txt:64-86) gives the same Warning at 1892, which places the defect at 9a07bb9. Code on c2a2aaa: :3734 `told.Add(section.Offset + section.Padding);`, :3740 `if (!told.Add(at))`, :5118 `_sectionsReadAt.Contains(candidate)`. NumberRunsInto (:5281) is applied only to streamStart. No open issue names a link landing inside the object number of a stream that was passed over (searched #171, #188, #289, #295, #320, #321, #323, #330, #331, #332, #333, #52, and a body search).
fix: 1. Tell the passed-over stream by its data, as R9-Q1 does for S. Where TryRelocateXRefSection records a place in _streamsPassedOver (:5221), the window is still open. Also record where the header's object-number digits end: `passed + span[(int)(passed - (start - lead))..].IndexOfAnyExceptInRange((byte)'0', (byte)'9')`, the scan NumberRunsInto makes. It costs no read and no allocation beyond the tuple field.
2. In ReportStreamsPassedOver, skip a place where a record in state Read, Relocated or Malformed has its Offset + Padding in [at, numberEnd). Keep the exact-start set for reporting each place once. Both lists are small: sections are bounded by MaxXRefSectionCount, places by twice MaxRelocationCandidates. Alternatively, look up each position at..numberEnd-1 in `told`.
3. Add test rows beside A_stream_passed_over_near_a_hybrid_table_s_prev_that_an_older_section_reaches_is_reported_as_the_chain_finds_it_not_as_passed_over. Each row expects no xref.stream-passed-over and object 10 "ten":
   - the oldest table's /XRefStm at own+1;
   - the oldest table's /Prev at own+1;
   - a three-digit stream number named at own+2;
   - a newer table's /XRefStm at own+1 read before the search, stream 11 giving no /Prev.
   Keep the exact-naming rows as controls.
4. Optionally add 'at its header or inside its object number' to the remark at :3700-3702, to threat-model 432's 'A stream the chain reached' clause, and to diagnostics.md:116. Once the code is fixed, those texts hold as written.
5. Run ADR 53 over the new lines: drop the range check, and shift the digit-run end by one.
6. The search's byte-based reached test (:5118): not a batch defect. It is main's behavior and #332's question. Add a comment on #332 with the newer-/XRefStm reproducer: named at the header, the stream is passed over as reached and the Error hybrid-stream-wrong is made; named one byte in, it is taken again as main takes it. #332's decision can then cover both namings. Not a maintainer question for this batch.
needs_maintainer: False

## 18. adr53-record-line-test-span-start — minor → narrowed nit (own-stream (8b31ebf, ee33070, )
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:5219

Claim: The stages missed an ADR 53 survivor. In the record loop's `IsTokenOnItsLine(window.Memory.Span, start - lead, (int)(passed - (start - lead)))`, shifting the spanStart argument by one (`start - lead + 1`) survives. B6/B7 shifted only the position argument. No test pins a typed passed-over header whose line starts before the search's window, which is the case where IsTokenOnItsLine reads its line again.

Evidence: Mutant built in a separate worktree at c2a2aaa, DOTNET_GCHeapHardLimit set. Every class that builds chain or hybrid shapes passes: CrossReferenceChainTests, HostileInputTests, GuardCutValueTests, WindowEdgeTests, CrossReferenceRuleTests, ReaderLimitsTests (2,206 passed, 0 failed); DocumentReaderTests, FileRuleTests, FuzzingSeeds, HeaderOffsetTests, ObjectRuleTests, PageTreeRuleTests, SyntaxFaultTests, ValidatorTests and the probes (2,046 passed, 0 failed). Probe ZzG6 writes the header '9 0 obj << /Type /XRef /W [1 1 1] /Length 3 >> stream' after '8 0 obj (600 x) endobj ' on the same line, with the newest /Prev 20 bytes past it; the oldest table, 123 bytes after the /Prev's offset, is taken. Head (p3-head.txt): [xref.offset-adjusted@1101, xref.stream-passed-over@958]. Mutant (W/r10wip/own-stream/mut-sorted.txt): [xref.offset-adjusted@1101], the Warning lost. With the line broken before the header (breakLine=true), both keep the Warning.

Fix: Add the G6 row and its line-broken control to A_place_passed_over_near_a_hybrid_table_s_prev_is_reported_only_where_it_declares_itself_a_cross_reference_stream, or to a sibling test. The row expects the Warning at the header where its line starts before the bytes searched, and it kills the mutant.

Verdict: The facts reproduce. The mutant at src/AdCodicem.Pdf/IO/PdfFileReader.cs:5219 changes the record loop's spanStart argument from `start - lead` to `start - lead + 1`. No committed test kills it, and it is not equivalent. In IsTokenOnItsLine's read-back path (lineStart == 0 and spanStart != 0), the mutant reads the bytes one further on, or with `before` one larger where `from` clamps to 0. StartsToken is then asked about the byte after the header's first digit, and that byte never starts a token. So the mutant drops the Warning wherever a typed passed-over header's line starts before the bytes searched. The direct path (lineStart > 0) is unchanged. No committed test holds that case: in every row of A_place_passed_over_..._declares_itself_a_cross_reference_stream, the line starts inside the window.

The finding is narrowed in three ways.
(1) The mutant is outside ADR 53's catalog as written. Item 2, 'shift a bound by one', means `<`/`<=`, `>`/`>=`, or a constant bound n to n ± 1. An argument offset like `start - lead` is neither. B6/B7, which shift the `at` argument, were the stage's own wide reading. Round 6 [9] ruled that such a reading does not widen the catalog (R5b's `_guard.Bound(...)` to 0). So 'the stages missed an ADR 53 survivor' overstates it: what is missing is a test.
(2) Even inside the catalog, ADR 53 says a survivor 'does not count as a review finding'. Round 8's triage classed [12] the same way ('an ADR 53 survivor, not a review minor'), so the severity is nit, not minor.
(3) There is no defect. The head reports the Warning exactly where R9-Q2 and the IsTokenOnItsLine remark say it should: a /Type /XRef header that is a token on its line, read back up to LineStartSearchLength before the window. No Warning is made past that bound. Line 5219 is covered, and the read-back branch is covered through the candidate call at :5197. None of #171, #289, #295, #320 to #333 or #52 holds this. Nothing for the maintainer to decide.

Side observation, not part of this verdict: in exactly this case the record's line test reads one window more. The reviewer's LoggingSource shows 18 windows against 17, with an extra 0:1403. The comment at :5209-5210 ('which the window, still open, tells') and ReportStreamsPassedOver's remark at :3706-3707 ('its line lexed there') do not say so. IsTokenOnItsLine's own remark at :5307-5309 states the bound correctly. Decisions.md's 'no extra read' refers to [30]'s alternative of reading the place again, so no decision is broken.

Verdict evidence: Rebuilt independently at c2a2aaa (origin/ccr-6d6c0ee6-dqm4lm-188, Release, 0 warnings). The probe is C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r10wip/verify-adr53-record-line-test-span-start/ZzVerifySpanStartTests.cs, G6's layout parameterized. Before the header comes `8 0 obj (<N x>) endobj` followed by a space or a newline, then `9 0 obj << /Type /XRef /W [1 1 1] /Length 3 >> stream`. The newest table gives /Prev at+20 and /XRefStm 225. The oldest table comes after the place, within reach.

Head (head.txt):
- N=600, no break (line=340, spanStart=465, header at 958): sections startxref Read 1270, /XRefStm Read Stream 225, /Prev Relocated Table 1101; diagnostics [Repair xref.offset-adjusted @1101, Warning xref.stream-passed-over @958].
- N=600 with the line broken: same.
- N=400, line inside the window: Warning @758.
- N=5000, line start more than 4,096 bytes before the window: only xref.offset-adjusted @5501, as the remark states.

Mutant, line 5219 set to `IsTokenOnItsLine(window.Memory.Span, start - lead + 1, (int)(passed - (start - lead)))` (mut.txt):
- N=600, no break: [Repair xref.offset-adjusted @1101] only. The Warning is lost.
- The broken-line and N=400 rows keep the Warning.
- N=5000 is unchanged.

The whole AdCodicem.Pdf.Tests project on the mutant, with the 242 remote documents hard-linked and DOTNET_GCHeapHardLimit set (mut-suite.txt): total 10,569, failed 2, succeeded 10,564, skipped 3. The only failures are my probe's N=600-no-break row, which the mutant kills, and its N=5000 row, which also fails on head because it expects a Warning the bound withholds by design. Every one of the 10,565 committed tests passes on the mutant.

The stage's script (r9wip/mutate_r9a.py:41-42) shifts only the `at` argument (B6/B7). ADR 53's item 2 lists only comparison operators and constant bounds. Precedents: r6-findings [9] (a stage's wide reading does not widen the catalog; narrowed/nit) and r8-findings [12] (a survivor is not a review minor; narrowed/nit).

The worktree is reverted, the probe deleted and the remote corpus links removed.
fix: Settle it in the ADR 53 record as a survivor killed by a new test. No src change.

The test cannot be a row in the existing theory. In HybridFiles.PrevFallingShortOfAPlacePastTheStream, a 600-byte line before the place pushes stream 21 and the oldest table out of the search's reach, and the search then takes the place. Write instead a sibling theory with its own builder in G6's layout:
- objects 1-4, then stream 21, with no /Prev;
- the line `8 0 obj (600 bytes) endobj 9 0 obj << /Type /XRef /W [1 1 1] /Length 3 >> stream`, holding three bytes of data;
- a comment, then object 5 (old five), then the oldest table, within 512 bytes past the place;
- a newest table with /Prev at+20 and /XRefStm naming stream 21.

Rows:
(a) the line starting before the search's window: sections [startxref Read, /XRefStm Read Stream, /Prev Relocated Table oldest], diagnostics [XRefOffsetAdjusted, XRefStreamPassedOver], the Warning at the header. This row kills the mutant.
(b) the same with the line broken before the header: the same output, as a control.
(c) optionally, the line starting more than LineStartSearchLength before the window: [XRefOffsetAdjusted] only, which pins the bound.

Optionally, with the same change, reword the comment at :5209-5210 and ReportStreamsPassedOver's remark at :3706-3707 to say that where the header's line starts before the bytes searched, the line test reads up to LineStartSearchLength bytes back, one window more for that place, as IsTokenOnItsLine's remark already says. Not a design choice.
needs_maintainer: False

## 19. passed-over-line-test-reads-a-window — minor → narrowed nit (own-stream (8b31ebf, ee33070, )
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3705-3707 (ReportStreamsPassedOver remarks)

Claim: The remark says nothing is read to report a passed-over stream, 'its line lexed there' (in the search's window), and R9-Q2 asked for no extra read. Where the header's line starts before the bytes searched, the record's IsTokenOnItsLine reads one window more: up to LineStartSearchLength (4,096) + 1,025 bytes for each typed place recorded. The cost stays bounded by MaxRelocationCandidates. IsTokenOnItsLine's own remark says this correctly ('one window more read at most for each'); this remark and the R9-Q2 record do not.

Evidence: ZzG6, counting windows with a logging source (p3-head.txt). Line starting before the window: 18 windows, 9,558 bytes. Header on its own line: 17 windows, 8,155 bytes. The extra window, 0:1403, is read right after the oldest table's (1101:302), so it comes from the record loop. Both give xref.stream-passed-over@958.

Fix: Remark: 'its line lexed there or, where it starts before the bytes searched, read again from as far as 4,096 bytes before them: one window more at most for each such place (IsTokenOnItsLine)'. Record in status.md that R9-Q2's 'no extra read' holds except in that case.

Verdict: The facts reproduce. R9a's record loop, PdfFileReader.cs:5216-5223 (8b31ebf), calls IsTokenOnItsLine on each typed place passed over. Where the header's line starts before the bytes searched, that call reads one more window: from up to LineStartSearchLength (4,096) bytes before them, through their end, so up to 4,096 + 1,025 bytes. da2b06f has no such call (the reviewer's p3-da2.txt reads 17 windows for both shapes).

Three texts say no read happens:
- the ReportStreamsPassedOver remark at :3705-3707: 'nothing is read to report it ... its line lexed there';
- the comment in the loop at :5212-5213: 'which the window, still open, tells' (the finding misses this one);
- docs/threat-model.md:432: 'nothing is read to report it' (the finding misses this one too).

I narrow it on three points:
(1) The cost is not new. It is the read-back that round 3 [1] adopted and round 4 [5] accepted as a nit: once per place, bounded by MaxRelocationCandidates. IsTokenOnItsLine's remark states it, and so does threat-model.md:434 ('read again as far as 4,096 bytes before the bytes searched'). It is also smaller than that bound suggests. A stream header needs an end of line after 'stream', so at most one typed place per search can have its line start before the window. Memory sources lend the window without a copy.
(2) R9-Q2 is implemented. Its 'no extra read' answered round 9 [30]'s refuter, whose rejected option read each recorded stream ('one read per recorded place ... the remark nothing is read to report it then changes'). No stream is read. R9a's own report says only 'No stream is read for it'. Only the wording of the record is loose, and status.md has no batch 19 entry yet.
(3) The cost is bounded and the mechanism was accepted, so only the sentences on the report side overclaim. Earlier rounds set cost remarks that overclaim at nit (r5 [3], r5 [5], r5 [13], r6 [3], r6 [4]), so this is a nit, not a minor.

A side consequence the passed-over texts do not name: a typed header whose line starts more than 4,096 bytes before the bytes searched still costs the 5,035-byte window and is not reported. IsTokenOnItsLine's remark and threat-model 434 hold that consequence, and the brief accepts it under R9-Q2 / LineStartSearchLength. It does not raise the severity.

Verdict evidence: Own probe, run on c2a2aaa (detached origin/ccr-6d6c0ee6-dqm4lm-188, Release, 0 warnings). It is ZzVerifyPassedOverWindowTests.cs in C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r10wip/verify-passed-over-line-test-reads-a-window/, with its output in out-c2a2aaa.txt there.

The file has the reviewer's ZzG6 shape, rebuilt. A logging PdfFileSource flags each Read whose stack contains IsTokenOnItsLine with '*'.

- lead=600, header on the same line as the string before it (its line starts at 340; the bytes searched start at 465):
  - sections: '/Prev Relocated Table at=1101', 'xref.offset-adjusted @1101', 'Warning xref.stream-passed-over @958';
  - 'windows=18 bytes=9558 lineTestWindows=1 lineTestBytes=1403';
  - order: ... '1101:32 1101:302 0:1403* 9:1394'. The read-back comes right after the oldest table is taken, so it is the record loop's call, not the candidate test at :5197, which a Read state short-circuits.
- lead=600, end of line before the header: same reports, 'windows=17 bytes=8155 lineTestWindows=0'.
- lead=3000: 'lineTestBytes=3804' ('0:3804*'), Warning @3358.
- lead=5000 (the line starts more than 4,096 bytes back): '769:5035*' is read, and no xref.stream-passed-over is made.

Texts on c2a2aaa:
- PdfFileReader.cs:3706-3707: 'nothing is read to report it, the stream's dictionary having been parsed in the search's window when it was passed over, and its line lexed there when the search took another section';
- PdfFileReader.cs:5212-5213: 'which the window, still open, tells';
- docs/threat-model.md:432: 'nothing is read to report it';
- IsTokenOnItsLine remark :5307-5309, correct: 'one window more read at most for each';
- threat-model.md:434, correct for the line test in general.

Precedent:
- r4-findings.md [5] (line-readback-repeated-per-place-in-one-window): refuter narrowed it to a nit and accepted it as round 3 [1]'s per-place cost;
- r9-findings.md [30], verdict fix: the option R9-Q2 rejected read each recorded stream;
- r9-fix-result.json, R9a: 'No stream is read for it'.
fix: Text only. The behavior and the bound stay as they are.

(1) PdfFileReader.cs:3705-3707. Replace 'nothing is read to report it, the stream's dictionary having been parsed in the search's window when it was passed over, and its line lexed there when the search took another section' with: 'no stream is read to report it: its dictionary was parsed in the search's window when it was passed over, and its line was lexed there when the search took another section — or, where the line starts before the bytes searched, read again from as far as LineStartSearchLength bytes before them, one window more at most (IsTokenOnItsLine), a line that starts farther back declaring nothing and the stream then not reported'.

(2) PdfFileReader.cs:5212-5213. Change 'which the window, still open, tells' to 'which the window, still open, tells, or a window read back before it where the line starts sooner (IsTokenOnItsLine)'.

(3) docs/threat-model.md:432. Change 'nothing is read to report it' to 'no stream is read to report it, its line read back as for a place that cannot be read (below) where it starts before the bytes searched'.

(4) At the batch's end, in status.md's batch 19 record of R9-Q2, say 'no stream read; the line test reads one window back where the header's line starts before the bytes searched (the accepted read-back cost of the line test)'.

Optional, and a separate change that is not needed for this finding: open the read-back window at most once per search (r4 [5]'s option b).

No maintainer question: R9-Q2 is implemented as decided.
needs_maintainer: False

## 20. s-read-from-a-sign-not-told — minor → confirmed minor (own-stream (8b31ebf, ee33070, )
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:5268-5271 (the mapping) and :5273-5275 (NumberRunsInto remarks)

Claim: S is told by its data only from a header that starts before S. Sometimes the /XRefStm names the sign of a header written '+17 0 obj', which the object parser reads there. TryReadHeaderBackwards does not look at the byte before the number, so the search also finds the header '17 0 obj' one byte past S. That is the same stream, and the search tries it as another stream past S. Where S's /Prev names the oldest table, it is taken: the stream is read twice, and hybrid-stream-lost and the Error hybrid-stream-wrong are missed, though the exact naming reports both. The remark's premise 'a header counts only where its number starts a token' is false: headers written '+17 0 obj' and 'endobj17 0 obj' are found. Its conclusion, that no header found starts inside a run of digits, holds, so M07 and M11 stay equivalent.

Evidence: ZzG4, header '+17 0 obj', into=0, S's /Prev the oldest table. Same output on c2a2aaa, da2b06f and main (l-head.txt, l-da2.txt, l-main.txt): sections [startxref Read Table 1158, /XRefStm Read Stream 418, /Prev Relocated Stream 419, /Prev Read Table 229], codes [xref.offset-adjusted@419], findings [W:xref.section-shifted]. Control, header '17 0 obj' into=0: [/XRefStm NamedAgain 418, /Prev Relocated Stream 418, /Prev Read Table 229], codes [offset-adjusted@418, hybrid-stream-lost@418], findings [W:section-shifted, E:hybrid-stream-wrong]. On da2b06f, the row 'endobj17 0 obj' into=1 took a section at 424, the header right after 'endobj'. This predates the batch (main reads it the same way) and the shape is exotic.

Fix: Correct the remark: the walk takes the whole digit run, and the byte before it is not looked at. Then file debt beside R9-Q1 (a header whose digits start one byte past a sign that S starts at is S), or let NumberRunsInto also map candidate == streamStart + 1 where span[streamStart] is '+' or '-'.

Verdict: The finding reproduces exactly on c2a2aaa and on main 21a0298.

1. Behavior. The /XRefStm names the '+' of a '+17 0 obj' header, so S is the '+'. The digit walk (TryReadHeaderBackwards) stops at the first byte that is not a digit and never looks at it. The search therefore finds the header at S+1. NumberRunsInto only accepts candidate < streamStart, so S+1 is not mapped to S. It is tried as a separate stream with passOver. Where S's /Prev names another offset, it is taken: the stream is read twice, and hybrid-stream-lost and the Error hybrid-stream-wrong are missed. The exact naming, into=1 and into=2 all report both. Every object is still served. Main gives the same output, so this is not a regression. No issue holds it: #171, #330, #332 and #333 do not mention a signed header.

2. Text. The remark at :5273-5275 is new in 9823992. Its premise, 'a header counts only where its number starts a token', is false for the digit walk: headers written '+17', 'x17' and 'endobj17' are all found, at the first digit. One precision on the finding: on the comment path (#261, TakeLine) the premise does hold, because the header starts at the lexer's token start, sign included. The conclusion the remark draws holds: no candidate starts inside a run of digits. M07 stays equivalent. M11 stays equivalent too, but its recorded reason in r9-fix-result.json, 'S ... is always the first digit of the stream's number', is false where S is a '+'. lazy-reading.md:200 (b55738a) says S is told 'whatever byte of its header the /XRefStm names'; that is false for the sign byte.

3. Wider than claimed, and worse than main. The comment path breaks the mapping in the other direction. In '+17 0 %c\nobj' the header the search finds starts at the '+'. NumberRunsInto scans digits from that '+', finds none, and never maps it. So where the /XRefStm names the first or second digit and S gives no /Prev, the table's own stream is taken as 'a stream before S'. The chain ends there: index rebuilt, validator Error file.root-invalid. Main reads the same file whole, because its walk never found that header. This is R9-Q1's case ('a candidate before S whose stream data starts where S's does is S itself') left unimplemented once a sign leads the number. It needs a '+'-signed object number with a comment inside the header, plus a /Prev that names no section; every object is still served after the rebuild. Hence minor, but it should be fixed in this batch, not only filed.

Verdict evidence: Probe: C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r10wip/verify-s-read-from-a-sign-not-told/ZzVerifySignTests.cs (main copy: ZzVerifySignTestsMain.cs, without the walk theory because TryReadHeaderBackwards is private on main). Outputs in the same folder: out.txt and out2-head.txt (c2a2aaa), out-main.txt and out2-main.txt (21a0298, built in a temporary worktree, since removed). Shape: the oldest table (1, 2, 3, 5) at 229; object 4; S at 418 indexing 4 and 17; the newest table's /XRefStm names 418+into; its /Prev names 411, where no section is.

Walk on c2a2aaa (TryReadHeaderBackwards):
- 'x17 0 obj' gives headerStart=1, byte before 'x'.
- 'endobj17 0 obj' gives headerStart=6, byte before 'j'.
- '+17 0 obj' gives headerStart=1, byte before '+'.
- '+17 0 %c\nobj' gives headerStart=0, the '+'.

Reviewer's shape, '+17 0 obj', into=0, S's /Prev the oldest table. c2a2aaa and main give the same output:
- Sections: startxref Read Table 1158; /XRefStm Read Stream 418; /Prev Relocated Stream 419 (named 411); /Prev Read Table 229.
- Diagnostics: only 'Repair xref.offset-adjusted @419'.
- WasRepaired False; objects 3, 4 and 5 ('old five') served.
- Findings: [Warning:xref.section-shifted].

Control on c2a2aaa, '17 0 obj', into=0:
- Sections: /XRefStm NamedAgain 418; /Prev Relocated Stream 418; /Prev Read Table 229.
- Diagnostics: offset-adjusted @418 and 'Warning xref.hybrid-stream-lost @418'.
- Findings: [Warning:xref.section-shifted, Error:xref.hybrid-stream-wrong].
- '+17 0 obj' at into=1 and into=2 reads the same, with hybrid-stream-lost @419 and @420. On main the control reads S twice and reports no hybrid-stream-lost.

Wider variant, '+17 0 %c\nobj', S giving no /Prev, into=1 or into=2:
- c2a2aaa: /XRefStm Read Stream 419 (420); /Prev Relocated Stream 418 (named 411); 'Repair xref.offset-adjusted @418' and 'Repair xref.rebuilt @-1'; repaired=True; findings [Error:file.root-invalid, Warning:xref.section-shifted].
- main: /Prev Relocated Table 229; offset-adjusted @229 only; repaired=False; findings [Warning:xref.section-shifted].
- Controls on c2a2aaa: '17 0 %c\nobj' at into=1 (mapped) and '+17 0 obj' at into=1 both read Relocated Table 229, with no rebuild.
- With S's /Prev the oldest table, the variant reads Relocated Stream 418 then Read Table 229 on c2a2aaa (S read twice, no hybrid-stream-lost); main reads Relocated Table 229.

Code (c2a2aaa): NumberRunsInto at :5281-5283 is `candidate < streamStart && candidate + span[...].IndexOfAnyExceptInRange('0','9') > streamStart`. It scans digits from the candidate's first byte, which is the '+' on the comment path (TakeLine's token.Start). The non-comment walk at :8964-9045 takes the digit run and does not check the byte before it. Blame: the remark lines :5265-5280 come from 9823992; lazy-reading.md:199-202 comes from b55738a.
fix: Fix this batch's code under R9-Q1, with no new decision. Make two changes in NumberRunsInto, both in the window already open, with no read:
- (a) Start the digit scan past one leading '+' at the candidate. A header the comment path finds starts at its number token, sign included.
- (b) Also map candidate == streamStart + 1 where streamStart >= 0 and the byte at streamStart is '+'. This is the digit walk's header one byte past the sign the reading started at.

Pin both with rows:
- '+17 0 obj', into=0, S's /Prev the oldest table: NamedAgain 418, hybrid-stream-lost@418, Error hybrid-stream-wrong, as for '17 0 obj'.
- '+17 0 %c\nobj', into=1 and into=2, S giving no /Prev: /Prev Relocated Table 229, no rebuild, no Error, as main.

Run ADR 53's mutants over the new conditions, and re-judge M07/M11. Correct M11's recorded reason before the status.md batch entry copies it: S can be a '+'.

Correct the texts:
- The remark at :5272-5275: the digit walk takes the whole run back to the first byte that is not a digit and does not look at that byte ('+', the end of 'endobj', any regular byte); the comment path takes the lexer's token start, a sign included. A candidate therefore never starts inside a run of digits.
- lazy-reading.md:200 ('whatever byte of its header the /XRefStm names') holds once (b) lands; otherwise it must name the sign as an exception.

If the fix is not taken now: correct the remark and the page, and file debt beside R9-Q1 for both directions, noting that the comment-path direction is worse than main, a consequence of #261 together with R9-Q1.
needs_maintainer: False

## 21. passed-over-warning-only-under-white-space-guard — minor → narrowed minor (batch (21a0298..c2a2aaa agains)
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:5463 (TryReadXRefSection, padding < 0: limit.trailer, State NotFound, no guard flag) and :3720 (ReportStreamsPassedOver tests ChainStoppedByGuard only); texts src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:122-123, PdfFileReader.cs:3695-3697, docs/website/docs/reference/diagnostics.md:116, docs/website/docs/reference/reader-limits.md:60-61

Claim: Open point (b), reproduced. Where a hybrid table's /Prev names white space longer than MaxTrailerLength before the older table, the guard turns the reading into NotFound, the search near the offset runs, passes over a typed stream that may be the table's own, takes the table past the run, and xref.stream-passed-over is made. Unbounded, the run is crossed, the table read where named, and no Warning is made. The Warning therefore exists only because of the guard, while the code's summary, ReportStreamsPassedOver's summary and both pages say nothing is reported where one of the reader's limits stopped the chain or cut what it reached. Not worse than main (main takes the stream, rebuilds and gives Error file.root-invalid); #289 as written names NotFound, the relocation and the Errors, not this Warning. Same on da2b06f.

Evidence: Generator C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r10wip/batch/tools/hand_b.py; files and outputs in .../r10wip/batch/hb/ (out-branch.txt, out-main.txt, out-r8f.txt; probe .../r10wip/batch/probe-src/Probe.cs, driver .../r10wip/batch/run.py). b1_n12.pdf: stream S at 246 (what /XRefStm reads, no /Prev), typed stream P at 361 (no /Prev), 300 spaces at 476, older table at 776, newest table at 900 with /Prev 476 /XRefStm 246. c2a2aaa default limits: no diagnostic, only 'Warning xref.offset-imprecise at offset 476 ... 300 bytes before the xref keyword'. c2a2aaa MaxTrailerLength 256: 'Warning limit.trailer at 476: White space and comments run past 256 bytes where a cross-reference section is named; the section was not read from there.', 'Repair xref.offset-adjusted at 776: ... found 300 bytes after it.', 'Warning xref.stream-passed-over at 361: The cross-reference stream at offset 361, near offset 476, which the /Prev of the cross-reference table at offset 900 names, ...'. da2b06f identical. b4_n12.pdf (440 spaces): same pair at T256, nothing at default. main T256: 'limit.trailer at 476: The dictionary of the object read as a cross-reference stream runs past 256 bytes', 'xref.offset-adjusted at 361 ... -115 bytes', 'xref.rebuilt', 'Error file.root-invalid'. b2_n12.pdf (no run, exact /Prev) control: no Warning. In the 1,500-file differential (seed 202611081357), 3 of the 11 runs that make the Warning beside a limit report at opening are this shape (f00355, f00871, f01343 at T256); f00355 makes the same Warning at default limits, so only shapes like b1 show the guard dependence.

Fix: In TryReadXRefSection's padding < 0 branch set a reader flag (white space cut by the trailer guard where a section is named) that ReportStreamsPassedOver tests beside ChainStoppedByGuard; rows: b1 and b4 at MaxTrailerLength 256 give no xref.stream-passed-over, default limits unchanged, b2 control. If it is left with #289 instead, name the exception in PdfDiagnosticCodes.XRefStreamPassedOver, the ReportStreamsPassedOver summary, diagnostics.md:116 and reader-limits.md:60-61, and add the b1 shape to the end-of-batch #289 comment.

Verdict: The behavior reproduces exactly as claimed. In b1 and b4 at MaxTrailerLength 256, the white-space guard's NotFound sends the chain into the search near the offset, the search passes P over, and xref.stream-passed-over is made. At default limits (and at 512 for b1 and b4) the table is read where named and no Warning is made. The root cause is #289's class: the white-space guard's cut is charged to the file. The batch did not make the shape worse than main, which takes P, rebuilds and reports Error file.root-invalid, and da2b06f behaves the same.

It is not refuted as known, because this batch's own texts state the exception for every limit, and the white-space guard (MaxTrailerLength reached in white space where a section is named) does not honor it. The fix stage judged that 'pages describe that case as a section looked for nearby, so they stay consistent'. That reading is loose for b1 and fails outright for a variant I built, c_run1000_n160_t300. There the same guard ends the chain and hides the very link that names the stream:
- unbounded: the oldest table T0 is read through 1,000 spaces, its /XRefStm reads P, and no Warning is made;
- guarded: limit.trailer ('the section was not read from there'), then xref.section-missing, and the Warning is still made.
That case contradicts 'Nothing is reported where one of the reader's limits stopped the chain' and threat-model 432's 'a limit that ended the chain ... may hide the link that names the stream, and the limit's report stands for it'. It also falls under round 9's [2]+[11] rule: the Warning is not made where a guard ended the chain or may hide a link.

The finding is narrowed in two ways:
- reader-limits.md:60-61 is not misstated. Its paragraph covers only what the index limits leave out (MaxXRefSectionCount, MaxXRefSectionLength, MaxStreamLength, MaxDecodedStreamLength, MaxIndexEntries), and the white-space guard leaves nothing out of the index in b1.
- The texts that do misstate it are:
  - src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:121-123
  - src/AdCodicem.Pdf/IO/PdfFileReader.cs:626-628 (the _streamsPassedOver doc)
  - src/AdCodicem.Pdf/IO/PdfFileReader.cs:3695-3697 and :3702-3704 (ReportStreamsPassedOver's summary and remark)
  - docs/website/docs/reference/diagnostics.md:116
  - docs/website/docs/concepts/lazy-reading.md:216-218
  - docs/threat-model.md:432 (the rule and its 'a limit that ended the chain' clause)

Severity is minor: the texts mislead, and the effect is a single hedged Warning in a shape where #289 already reports Errors. It is reachable only with a lowered MaxTrailerLength, or with a run of white space over 64 KB at the default.

Verdict evidence: All probes ran on c2a2aaa (origin/ccr-6d6c0ee6-dqm4lm-188), built Release with 0 warnings, and on main 21a0298 in a temporary worktree, now removed.
- Probe source: W/r10wip/verify-passed-over-warning-only-under-white-space-guard/ZzVerifyWhiteSpaceGuardTests.cs, run with ZZ_ONLY and ZZ_OUT.
- Generators: hand_b.py (the reviewer's, copied) and hand_b2.py (mine).
- Files: gen/.
- Outputs: out.txt (b1-b4 on the branch), out-main.txt, out-c2-branch.txt, out-c2-main.txt.
W = C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud.

b1_n12.pdf on the branch:
- Default: no diagnostic. The /Prev section reads at 476 with padding 300, and the validator gives xref.offset-imprecise.
- MaxTrailerLength 512: the same as default.
- MaxTrailerLength 256: the /Prev section is Relocated to 776, and the diagnostics are:
  - 'Warning limit.trailer @476: White space and comments run past 256 bytes where a cross-reference section is named; the section was not read from there. ...'
  - 'Repair xref.offset-adjusted @776: ... found 300 bytes after it.'
  - 'Warning xref.stream-passed-over @361: The cross-reference stream at offset 361, near offset 476, which the /Prev of the cross-reference table at offset 900 names, ...'

b4_n12.pdf: the same pattern at 256 (offset-adjusted @916, passed-over @361); nothing at default or 512.

b2_n12.pdf (control): no Warning at any limit.

Main, MaxTrailerLength 256, b1 and b4: the /Prev section is Relocated as a Stream at 361, with limit.trailer 'The dictionary of the object read as a cross-reference stream runs past 256 bytes', xref.offset-adjusted -115, xref.rebuilt, and Error file.root-invalid. So the branch is not worse than main. The reviewer's out-r8f.txt shows da2b06f identical to the branch.

My hid-the-link variant, c_run1000_n160_t300.pdf (hand_b2.py gen 1000 160 300). Layout:
- S at 236, what H's /XRefStm reads;
- P at 351, typed, no /Prev;
- H's /Prev names 511, inside a comment;
- T1 at 811, a classic table whose /Prev names 946;
- 1,000 spaces;
- T0 at 1946, a hybrid table with /XRefStm 351.

Its outputs on the branch:
- Default: sections ... /Prev Relocated 811, /Prev Read 946 (padding 1000), /XRefStm Read 351. Only 'Repair xref.offset-adjusted @811'.
- MaxTrailerLength 512: '/Prev NotFound 946', then:
  - 'Repair xref.offset-adjusted @811'
  - 'Warning limit.trailer @946: White space and comments run past 512 bytes where a cross-reference section is named; the section was not read from there.'
  - 'Warning xref.section-missing @946'
  - 'Warning xref.stream-passed-over @351: The cross-reference stream at offset 351, near offset 511, which the /Prev of the cross-reference table at offset 2035 names, ...'
  - repaired=True, and the validator gives Error xref.section-not-found.
- MaxTrailerLength 256: the same, plus the dash-comment 'limit.trailer @511' (the known #295-type message).
Main on this file, at every limit: it takes P at -160, rebuilds, and reports Error file.root-invalid.

Controls c_run0_n160_t300 and c_run200_n160_t300: no Warning at any limit.

Code checked: PdfFileReader.cs:5463-5476 (padding < 0: ReachLimit, then NotFound with no flag; the comment there says 'looked for as one not there is: near the offset'); :1105 (ChainStoppedByGuard => _chainFilledIndex || _cutByGuard); :3719 (the only guard test in ReportStreamsPassedOver). decisions.md and r9-fix-result.json open_points record that R9a left this case unhandled.
fix: Preferred: change the code, which applies round 9's [2]+[11] rule (the Warning is not made where a guard ended the chain or may hide a link), so that the texts hold as written.
- In TryReadXRefSection's padding < 0 branch (PdfFileReader.cs:5463), set a dedicated reader flag, such as a 'white space past the trailer guard where a section is named' flag.
- Have ReportStreamsPassedOver return when the flag is set, beside ChainStoppedByGuard.
- Do not use _cutByGuard: it ends the chain and drives checked-in-part and the rebuild decision, and would reshape #289's documented NotFound, relocation and section-missing behavior. Those stay #289's.
- The branch is reached only by the chain's own reads (the main path at :3360 and the /XRefStm reading at :3562); a search's candidate never gets there.
- Rows:
  - b1 and b4 at MaxTrailerLength 256 give [limit.trailer, xref.offset-adjusted] with no xref.stream-passed-over.
  - c_run1000_n160_t300 at 512 gives [xref.offset-adjusted, limit.trailer, xref.section-missing] with no passed-over Warning. Use 512 for a clean row; 256 adds the dash-comment limit.trailer at 511.
  - Default limits are unchanged: b1 gives nothing; c_run1000_n160_t300 gives xref.offset-adjusted only.
  - Controls: b2, c_run0_n160_t300 and c_run200_n160_t300.
- Mutants: drop the assignment, and drop the test in ReportStreamsPassedOver.

Alternative, if the orchestrator keeps the case whole with #289: change no behavior.
- Name the exception in the texts listed in the reason (PdfDiagnosticCodes.cs:121-123, PdfFileReader.cs:626-628, :3695-3697 and :3702-3704, diagnostics.md:116, lazy-reading.md:216-218, threat-model.md:432): white space and comments past MaxTrailerLength where a section is named are looked for as a section that is not there, the Warning still made (#289). Leave reader-limits.md:60-61 as it is.
- Add both shapes, b1 at 256 and c_run1000_n160_t300 at 512, to the end-of-batch #289 comment, since #289's closing (white space not counted, or the section left to the guard with CutByLimit) removes the Warning with the relocation reports.

The code fix follows from an existing decision, so it does not need the maintainer.
needs_maintainer: False

## 22. guard-cut-commented-table-taken — major → narrowed minor (batch (21a0298..c2a2aaa agains)
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:5799-5801 (a table MaxXRefSectionLength cut returns true, 'its keyword declared it a section before the cut'), :5193-5197 (a Read candidate is taken before any line test), against the remark at :4934-4939; consequence at :3642-3646 (ReportNoStream for a table read in the stream's place)

Claim: A candidate of the search near an offset whose xref keyword lies in a comment on its line (so per the batch's own line rule it declares nothing) is taken as the section found moved when MaxXRefSectionLength cuts its rows: the cut table returns Read, and Read skips IsTokenOnItsLine. Unbounded, the same place fails at its rows' end, is Malformed, fails the line test and is dropped. The remark at :4934-4939 says a place that cannot be read is the section only where it declared itself 'before a fault or a guard's cut, a token a load reads from the start of its line', and the comment at :5799-5800 justifies keeping it by 'its keyword declared it a section before the cut', which is false for a keyword in a comment. At an /XRefStm the batch then reports xref.hybrid-stream-lost and Error xref.hybrid-stream-wrong ('a cross-reference table, whose rows were read in the stream's place') and rebuilds, where the unbounded read finds the stream 232 bytes off with a Warning: a guard's cut becomes an Error on the file (invariant 12, ADR 34). At a /Prev the chain ends at the comment (objects of the older revision null under the guard), as on main. This is also the only route I found for open point (c): no candidate is dropped after setting _cutByGuard, because every path that sets it during a candidate's reading (:5759, :6469, :6779, :7791) returns Read and the candidate is taken. The example the R9a stage gave for (c) is taken, not dropped.

Evidence: Generator C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r10wip/batch/tools/hand_c.py; files and outputs in .../r10wip/batch/hc/. c1_n12.pdf: older table at 246 (objects 1-4), object 4 rewritten, the table's stream 11 at 423, a decoy at 538 ('% xref', '100 40', 40 free rows), the hybrid table at 1352 with /Prev 246 /XRefStm 655 (inside the decoy's rows). c2a2aaa default limits: 'Repair xref.offset-adjusted at 423: The cross-reference section /XRefStm names at offset 655 was found 232 bytes before it.', 'Warning xref.section-shifted at offset 423', objects 1-4 right. c2a2aaa MaxXRefSectionLength 650: 'Warning limit.xref-section-length at 540', 'Repair xref.offset-adjusted at 540: ... found 115 bytes before it.', 'Warning xref.hybrid-stream-lost at 655: The /XRefStm of the cross-reference table at offset 1352 names offset 655, a cross-reference table, whose rows were read in the stream's place ...', 'Repair xref.rebuilt', 'Error xref.hybrid-stream-wrong at offset 1352 ...', 'Information xref.checked-in-part at offset 540'. da2b06f identical. main at 650: limit at 540, offset-adjusted at 540, objects 1-4 null, no Error. c3_n12.pdf (/Prev 690 inside a decoy at 653; P at 538; older table at 246): default limits: 'xref.offset-adjusted at 246 ... found 444 bytes before it', 'Warning xref.stream-passed-over at 538', objects right; at 650, branch = main: 'limit.xref-section-length at 655', 'xref.offset-adjusted at 655 ... found 35 bytes before it', objects 1-3 null, no Warning. Controls c2/c4 (10 decoy rows) read at 650 as at default limits.

Fix: In TryRelocateXRefSection take a candidate a guard cut (CutByLimit, read or not) only where it declares itself, as an unreadable one: a table whose keyword, or a stream of /Type /XRef whose header, IsTokenOnItsLine finds a token on its line; drop any other, its guard report standing as for a dropped place. Save _cutByGuard (and TrailersHiddenByGuard) before each attempt and restore them when the place is dropped, or the dropped candidate ends the chain at the /XRefStm path (:3663, _cutByGuard != cut) and withholds the Warning, which is what (c) describes. Correct the comment at :5799-5800. Rows: c1 and c3 at MaxXRefSectionLength 650 read as at default limits (no hybrid-stream-wrong in c1; the older table read in c3, P reported); c2 and c4 controls; a guard-cut table whose keyword is on its own line still taken as the guard's.

Verdict: The finding reproduces on c2a2aaa and on da2b06f. Under MaxXRefSectionLength 650, a run that starts with "% xref" near an /XRefStm is taken, while at default limits it is dropped. Its 40 free rows reach the guard, so TryReadXRefSection returns Read with CutByLimit (:5799-5801), and a Read candidate is accepted before any line test (:5191-5195). At the /XRefStm the branch then reports hybrid-stream-lost, rebuilds, and the validator gives Error xref.hybrid-stream-wrong. With no guard, the stream 232 bytes off is read and the only finding is a Warning, section-shifted. I narrow the claim on four points:

(1) The take is not new and not undocumented. Main takes the same run under the same guard. The texts say "One that reads, whole or in part, is taken whatever its line is" (:4932-4933, :5187-5189; lazy-reading.md: the search ends "as at one it reads"). In this batch's terms, a table a guard cut is read in part: "whose rows were read in the stream's place", xref.checked-in-part. The maintainer accepted the header analog with R9-Q2: "a readable commented header [is] taken". The remark at :4934-4939 that the finding quotes is about places that cannot be read, so it does not contradict the code.

(2) The guard is not what causes the false Error. A commented table whose rows read whole, at DEFAULT limits, gets the same hybrid-stream-lost, rebuild and Error hybrid-stream-wrong on the branch. Main takes it too, silently, and serves object 4 from the older revision. The batch's #156 report produces the Error for any non-section taken in the stream's place under the documented rule. The guard only adds the runs that would be malformed past the cut. Still, those are the runs where a guard turns a Warning into an Error, against the direction ADR 34 sets.

(3) Compared with main, the outcome is mixed, not purely worse. Main ends the chain under the guard, with objects 1-4 null. The branch serves them right through the rebuild, but adds a false Error.

(4) The trigger is contrived. At the default MaxXRefSectionLength (64 MB), it takes more than 64 MB of row-shaped lines after a commented xref, within 512 bytes of an /XRefStm that misses its stream. Otherwise it takes a limit the caller lowered, and the guard is reported (limit.xref-section-length, xref.checked-in-part).

One text does misstate the code: the comment at :5799-5800, "its keyword declared it a section before the cut". By the batch's own definition (IsTokenOnItsLine), that is false for a candidate whose keyword lies in a comment or in a string opened on its line. The /Prev consequence (c3) is the same on main, as the finding says.

On open point (c): R9a's example, a table cut by MaxXRefSectionLength whose keyword sits in a comment, is taken, not dropped (section Relocated, cut=True). So (c) as R9a stated it does not hold for that example. The paths at :5759, :6469 and :6779 all return Read after setting _cutByGuard. I did not prove that no other route drops a candidate.

Verdict evidence: Generator: C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r10wip/verify-guard-cut-commented-table-taken/gen.py. Its cm40.pdf and cm10.pdf are byte-identical to the reviewer's c1 and c2 (cmp). It adds three variants:
- own40: the keyword on its own line;
- cmT40: "% xref", 40 rows, then "trailer << /Size 140 >>";
- str40: "(xref" on the keyword's line.

The probe is ZzRefuteCutTable.cs.txt in the same folder. It runs on c2a2aaa (my worktree, Release). Outputs are out-branch.txt, out-main-probe.txt (the reviewer's main build) and out-r8f-probe.txt (da2b06f).

c2a2aaa, cm40 (c1):
- D: "section /XRefStm named=655 state=Relocated kind=Stream offset=423", "Repair xref.offset-adjusted @423 ... found 232 bytes before it.", "F Warning xref.section-shifted at offset 423", objects 1-4 right.
- X650: "section /XRefStm named=655 state=Relocated kind=Table offset=540 cut=True", "Warning limit.xref-section-length @540", "Repair xref.offset-adjusted @540 ... found 115 bytes before it.", "Warning xref.hybrid-stream-lost @655: ... a cross-reference table, whose rows were read in the stream's place ...", "Repair xref.rebuilt", "F Error xref.hybrid-stream-wrong at offset 1352 ...", "F Information xref.checked-in-part at offset 540". WasRepaired(open)=True, objects 1-4 right.

cmT40 at D (no guard), c2a2aaa:
- "section /XRefStm named=655 state=Relocated kind=Table offset=540 cut=False";
- "Warning xref.hybrid-stream-lost @655 ... whose rows were read in the stream's place";
- "F Error xref.hybrid-stream-wrong at offset 1376";
- obj 4 = Rev 0 until validation rebuilds.

Main on cmT40 at D: "offset-adjusted at 540 ... -115 bytes from there", O 4 Rev 0, no Error.

Main on cm40 at X650: limit at 540, "offset-adjusted at 540 ... -115 bytes from there", O 1-4 null, no Error. So main already takes the commented run.

str40 at X650 behaves as cm40: taken, cut=True, Error. At D the stream is found 231 bytes off.

own40 gives Error hybrid-stream-wrong both at D (Malformed, declared) and at X650.

c3 at X650, branch and main alike: "/Prev ... Relocated kind=Table offset=655 cut=True", objects 1-3 null, no passed-over Warning.

da2b06f gives output identical to c2a2aaa on cm40 and cmT40.

The texts: PdfFileReader.cs:4932-4933 says "One that reads, whole or in part, is taken whatever its line is"; :5187-5189 says the same; :5799-5800 says "its keyword declared it a section before the cut".

The R9-Q2 accepted consequence, from the brief and decisions.md: "a readable commented header [is] taken".
fix: Make a guard-cut table candidate pass the line test before it is taken. In TryRelocateXRefSection, accept a table that is Read with CutByLimit only where IsTokenOnItsLine(window, start - lead, candidate - (start - lead)) holds. The window is still open there, so no read is added. Drop any other such candidate as a place that holds no section: ForgetReadThrough and the decoding rollback. Its limit.xref-section-length report stands, as a guard a dropped place reached does.

Save _cutByGuard before each attempt and restore it when the attempt is dropped. Otherwise the /XRefStm path's `_cutByGuard != cut` ends the chain at a dropped place, which is what open point (c) describes.

Texts to change:
- the comment at :5799-5800, which should say the keyword is the table's own at the offset named, and that a place the search tries counts as the guard's only where its keyword is a token on its line;
- the remarks at :4932-4933 and :5187-5189, lazy-reading.md and threat-model #171: "whole or in part" becomes "whole, or in part where no guard cut it; a table a guard cut is taken only where its keyword is a token on its line".

Test rows:
- cm40 at MaxXRefSectionLength 650 reads as at default limits (stream at 423, no hybrid-stream-lost, no Error, the limit report kept);
- str40 likewise;
- own40 at 650 is still taken as the guard's;
- cm10 as control.

This follows ADR 34 (a guard's cut does not make the reader take what an unbounded read drops). It does not touch R9-Q2, which concerns places that read with no guard, so no maintainer question is needed.

If the fix stage prefers no code change this late, the alternative is: correct the :5799-5800 comment, file the case as debt (or comment on #171), and widen #171's closing to "the line test asked of every place the search takes, one that reads or a guard cut included". #171's closing names only stream data and strings an earlier line opened, so as written it would close neither this case nor the cmT40 default-limit case.

Also record in decisions.md that R9a's example for open point (c) is taken, not dropped.
needs_maintainer: False
