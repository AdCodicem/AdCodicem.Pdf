######## [0] lens chain-keep — keep-check-reads-from-the-header-not-where-the-prev-reads
reviewer severity: major; refuter: narrowed / major; needs_maintainer: True
location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3711-3713 (KeepsItsPlace calls TryReadXRefStream(start, ...)); :3420-3422 (previousStart = StartOfNamed, and branch (c) by positions); :4827-4831 (candidate == leaveAt)

CLAIM:
The check judges the /Prev at previousStart, the header past the white space. It reads the object through windows that start at that header. The /Prev's own reading (TryReadXRefSection at PositionOf(previous)) reads through windows that start at the offset named, white space included, under the same MaxTrailerLength. So where white space fills part of that window, the check sees bytes the /Prev never reads. Three shapes follow.
(a) The check reads the object whole and answers Section, but the /Prev's own window cuts it before it says what it is. The guard's cut is then reported as the file's fault: xref.hybrid-stream-lost, index incomplete, rebuild at opening, and an Error. That is round 3's [10] left undone.
(b) The white space fills the /Prev's whole window, so its own reading finds no object there (limit.trailer) and relocates to another section. The check still reads the object there as a section. The /XRefStm is left to a /Prev that reads elsewhere, the table's own stream is never read, and an older revision is served, with a false Warning and a false Error. This happens under default limits.
(c) Branch (c) by positions (hybridStart == previousStart) takes the same unreachable start with no check at all.
KeepsItsPlace's remark says the object 'is read as the chain reads one it names as a cross-reference stream', and 2b26d18's body says it is told 'by reading the object as the chain would'. Neither holds here. This is not new in 2b26d18: 19122f0 gives the same output, except the Error's wording. Against main 21a0298, (b) and (c) are regressions.

EVIDENCE:
Probe /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r4keep/ZzKeepProbeTests.cs.txt (ZzPaddingWindow, ZzPaddingWhole). Outputs are in the same folder: padding.txt, whole.txt, and the main- and b19122f0- copies. I ran it on b4ab948, 19122f0 and 21a0298.

(a) File: the newest table's /Prev 2087 names 300 spaces before object 4, at 2387, and its /XRefStm 2069 lies inside object 8's string. Object 4 is an untyped stream '<< /Pad (...) /W [1 4 2] /Size 10 /Index [6 1] /Prev 1481 /Length 7 >>' whose text through 'stream\n' is 65,386 bytes. The table's stream is at 1649. Default limits.
b4ab948:
- S /XRefStm NamedAgain nwpr=True nns='what its /Prev names too, which the chain looks for through that /Prev'
- S /Prev Malformed off=2087 pad=300 cut=True fault='holds object 4, whose dictionary one of the reader's limits cut before it declared a cross-reference stream'
- D xref.hybrid-stream-lost @2069
- D limit.trailer @2087
- D xref.rebuilt; WasRepaired at opening True
- F xref.hybrid-stream-wrong [Error] '...what its /Prev names too, past the limit that cut it there...'
Control, the same object 250 bytes longer so both windows cut it: /XRefStm NamedAgain nwpr=False nns=-, D limit.trailer only, no rebuild, object 7 null, no Error. That is the decided outcome.
19122f0: as on b4ab948, with the Error worded '...which holds no section the chain could read...'.

(b) File: /Prev 1649 names the first of 65,536 spaces. Object 4 is an untyped /W stream that reads: its /Index [7 1] places 'new seven' and its /Prev 1481 names the oldest table, which lies just before the run. It sits at 67185 = 1649 + 65536. /XRefStm 67305 lies inside object 8's string after object 4. The table's /Type /XRef stream 9, which indexes 5 and 7 new, is at 67523, 218 bytes off. Default limits.
b4ab948:
- S /XRefStm NamedAgain readAs=/Prev@1481 nwpr=True
- S /Prev Relocated Table off=1481 named=1649
- D xref.hybrid-stream-lost @67305 '...what its /Prev names too, which the chain looks for through that /Prev...'
- D limit.trailer @1649 '...runs past 64 KB...'
- D xref.offset-adjusted @1481 'found 168 bytes before it'
- GetObject(7) = 'old seven', WasRepaired False
- F xref.hybrid-stream-wrong [Error] 'names offset 67305, the section its /Prev names too, which the chain read through that /Prev'. It read the oldest table, 65 KB away.
The same holds with MaxTrailerLength 1024 and a run of 1,024 or 1,023 spaces, the header straddling the window's end: 'old seven'.
Control, MaxTrailerLength 1 MB: /Prev Read Stream at 1649 with pad=1024, 'new seven'.
19122f0: identical to b4ab948.
Main: /XRefStm Relocated Stream at 67185, 'new seven', no hybrid-stream-* reports.

(c) Same build with MaxTrailerLength 1024, a 1,024-space run, and /XRefStm 2663, 10 bytes before object 4 (2673), inside the run.
b4ab948: S /XRefStm NamedAgain readAs=/Prev@1481; /Prev Relocated Table 1481; D xref.hybrid-stream-lost @2663; D limit.trailer @1649; object 7 = 'old seven'; F xref.hybrid-stream-wrong 'the section its /Prev names too, which the chain read through that /Prev'.
Main: /XRefStm Read Stream off=2663 pad=10, 'new seven', no hybrid-stream-* reports.

REVIEWER FIX:
Judge the /Prev where its own reading reads.
1. Pass KeepsItsPlace the /Prev's position, PositionOf(previous), beside leaveAt. Have it call TryReadXRefStream(position, check, ..., checkOnly: true), with the record at that position, so TryParseUnrecordedAt grows the /Prev's own windows. In (a) it then answers Guard. In (b) the window holds no object, so it answers None and the search goes on to stream 9.
2. Decide branch (c) by positions only where that same check answers Section or Guard. Equivalently, StartOfNamed gives -1 where the /Prev's window from its position cannot read the object at that start.
3. Add rows to An_xrefstm_stops_short_of_its_stream_only_where_its_table_s_prev_keeps_its_place, or a new theory, for the three shapes:
   - (a): the guard's outcome, as the 'both cut' control gives it.
   - (b): /XRefStm Relocated to stream 9, 'new seven', no hybrid-stream-* reports.
   - (c): the /XRefStm read where it names, or at least no report that the /Prev read what the /XRefStm names.
4. Once the code does what the remark says, keep the remark. For the status.md record of commit bodies not rewritten, add 2b26d18: 'told by reading the object as the chain would' was false for these shapes.

REFUTER REASON:
The finding holds. I reproduced all three shapes with my own builder on b4ab948, under default limits. KeepsItsPlace reads from StartOfNamed, the header found past the white space, and opens a fresh MaxTrailerLength window there (PdfFileReader.cs:3711-3713). The /Prev's own reading opens one window at PositionOf(previous) that covers the white space and the object together (:3302 -> TryReadXRefSection -> TryReadXRefStream -> TryParseNumberedAt(absolute)). The two disagree in a band at the limit's edge. The remark's claim, "read as the chain reads one it names", is false there, and so is 2b26d18's body. 19122f0 gives the same results except the Error's wording; main differs. I narrow it in four ways.

1. The band is thin. The white space before the object plus the object's text up to 'stream' must straddle MaxTrailerLength. For (b) and (c) the run must be within about 8 bytes of exactly 64 KB. Only a hostile or broken file gets there. Round 3's [10], just as contrived, was still rated major.

2. In (a) the Warning and the Error are true of the file. With MaxTrailerLength at 1 MB, the same file gives the same hybrid-stream-lost and the same hybrid-stream-wrong Error. So this is not a false accusation. It is the decided rule ([0] mirror, [10] guard's naming) not followed: the check judges past the /Prev's window, and the index is rebuilt at opening, which a whole read does not do. Against main it is a reporting regression: main gives only offset-adjusted and limit.trailer.

3. (b) and (c) serve an older revision under default limits, where main and the whole read serve the newer one. That is major. limit.trailer is reported at the /Prev, but the false hybrid-stream-lost and the Error, which says 'the section its /Prev names too, which the chain read through that /Prev', come with it. (b) breaks [0] directly. (c) follows R1's [1] rule (positions) to the letter, but takes its position from the same PaddingAt start the /Prev's window cannot reach.

4. The proposed fix is incomplete, and partly inaccurate:
   - Steps 1 and 2 alone (my prototype) turn (b) and (c) into a false xref.chain-cycle plus a rebuild at opening. ReachedAt, the loop check behind Q9's 'reached', also places the /Prev at the PaddingAt start.
   - In (b) the search would take object 4, which is nearer (as main does), not stream 9.
   - The two forms of step 2 are not equivalent. Both cut into decided branch (c) for a token that is no object (the endobj test).

Which notion wins is a design choice. One option touches Q9 and R1 [1]; the other changes what MaxTrailerLength bounds compared with main.

REFUTER EVIDENCE:
My probe: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute-r4keep/ZzRefuteKeepTests.cs.txt.
Outputs, in the same folder:
- b4ab948: head-mine.txt, head-mine-cut.txt
- 19122f0: wt-1912-mine*.txt
- main 21a0298: wt-main-mine*.txt
- prototypes: proto1.diff, proto2.diff, proto-suite.txt, proto2-suite.txt, proto2-mine*.txt
The reviewer's own probe (r4keep) reruns byte-identical on b4ab948.

Shared layout, default limits:
- T0 at 1181, indexing 7 '(old seven)'.
- The /Prev names 1328, the start of a run of newlines.
- Object 4 is an untyped stream '<< /Size 10 /W [1 4 2] /Index [7 1] /Prev 1181 /Length 7 >>' whose row places 7 as 'new seven'.
- Object 8 is a string, inside which the /XRefStm names.
- Stream 9 (/Type /XRef) indexes 5 and 7.

(b) Run of 65,536 newlines; object 4 at 66864; /XRefStm 66989; stream 9 at 67162.
- b4ab948:
  - S /XRefStm NamedAgain readAs=/Prev@1181
  - S /Prev Relocated Table off=1181 named=1328
  - D xref.hybrid-stream-lost @66989; D limit.trailer @1328; D xref.offset-adjusted @1181
  - obj7 = old seven
  - F xref.hybrid-stream-wrong [Error] '...the section its /Prev names too, which the chain read through that /Prev...'
- A run of 65,530 (the header straddles the window's end) gives the same.
- 19122f0: identical.
- main: /XRefStm Relocated Stream 66864, obj7 = new seven, no hybrid-stream-*.
- b4ab948 at MaxTrailerLength 1 MB: /Prev Read Stream 1328 pad=65536, new seven.

(c) Same, with /XRefStm 66857, 7 bytes before object 4.
- b4ab948: /XRefStm NamedAgain pad=7 readAs=/Prev@1181; obj7 = old seven; the same Warning and Error.
- main: /XRefStm Read Stream 66857 pad=7, new seven.
- A run of 65,530 gives the same.

(a) Stream 9 at 1328, /XRefStm 1916, /Prev 1939 names 200 newlines before object 4 at 2139. Object 4's text through 'stream' is about 65,432 bytes.
- b4ab948:
  - S /XRefStm NamedAgain readAs=/Prev@1939
  - S /Prev Malformed cut=True
  - D hybrid-stream-lost @1916; D limit.trailer @1939; D xref.rebuilt; D object.redefined
  - WasRepaired at opening True; obj7 = new seven, obj5 = five
  - F hybrid-stream-wrong [Error] '...past the limit that cut it there...'
- Control, the dictionary 300 bytes longer so both windows cut it: limit.trailer only, obj7 and obj5 PdfNull, no Error. This is [10]'s decided outcome.
- MaxTrailerLength 1 MB: /Prev Read Stream, T0 read, hybrid-stream-lost plus the Error, no rebuild.
- main: /XRefStm Relocated Stream 2139, offset-adjusted, limit.trailer, new seven, no Error.
- 19122f0: as b4ab948, the Error worded '...which holds no section the chain could read...'.

Prototype 1 (steps 1 and 2: the check reads from PositionOf(previous); branch (c) only where the /Prev's window finds an object at that start):
- (a) gives the guard outcome.
- (b) gives '/XRefStm Relocated Stream 66864', then 'D xref.chain-cycle @66864' and xref.rebuilt at opening.
- (c) gives '/XRefStm Read Stream 66857', then xref.chain-cycle and a rebuild.
- With the same window test added to ReachedAt, (b) and (c) match main. Two tests then fail:
  - HostileInputTests.A_thousand_tables_naming_one_stream_through_the_white_space_before_it_read_it_once, on cost: 67 MB more read, because my probe parsed the object at every naming.
  - CrossReferenceChainTests.An_xrefstm_falling_short_of_where_its_table_s_prev_names_a_section_found_moved_names_no_section_short: an endobj both links reach, which branch (c) decides by positions though no object is there.

Prototype 2 (one line, proto2.diff: TryParseNumberedAt(absolute + section.Padding) in TryReadXRefStream):
- (a), (b), (c) and the 65,530 variants all give the 1 MB outcome.
- Whole unit suite: 7,587 passed, 3 failed. The failures are the three rows of CrossReferenceRuleTests.A_startxref_naming_more_white_space_than_a_trailer_s_window_before_a_classic_table_reaches_the_guard: white space longer than the window no longer reaches the guard.

REFUTER FIX:
The maintainer chooses which notion of where the /Prev's section starts wins. Then:

(1) The finding's direction: everything follows the /Prev's window from the offset it names.
- KeepsItsPlace reads the check from PositionOf(previous).
- StartOfNamed (branch (c), leaveAt) and ReachedAt (the loop check) count the start only where that window holds the first token there whole, the object header included.
- Do it without a parse at each naming, so the thousand-namings test still holds.
- Keep branch (c) for a token that is no object (the endobj test). Neither form in the finding's step 2 does.
- Expected:
  - (a): limit.trailer only.
  - (b): /XRefStm Relocated to object 4, the nearer place (as main), new seven, no hybrid-stream-*. Not stream 9.
  - (c): the /XRefStm read where it names, new seven.

(2) Recommended: start the object window of a named section where PaddingAt found its first token: TryParseNumberedAt(absolute + section.Padding).
- White space and dictionary are then each bounded by MaxTrailerLength. That is what the check, StartOfNamed, branch (c) and ReachedAt (Q9) already assume, so the check and its remark hold as written.
- PaddingAt's padding-whole case must then report limit.trailer itself, which keeps the three startxref rows.
- (a), (b) and (c) give the whole read's outcome.
- limit.trailer's 'The dictionary ... runs past 64 KB' becomes true in (a); today the dictionary there is under 64 KB.
- This changes what MaxTrailerLength bounds compared with main, so restate reader-limits.md's row and the PaddingAt and KeepsItsPlace remarks.

Either way:
- Add rows to An_xrefstm_stops_short_of_its_stream_only_where_its_table_s_prev_keeps_its_place for (a), (b) at run = limit and at limit - 6, and (c). Each must fail on b4ab948.
- Add 2b26d18 to status.md's 'Commit bodies not rewritten': 'told by reading the object as the chain would' is false in this band.

######## [1] lens chain-keep — keep-check-says-section-where-the-prev-loops
reviewer severity: minor; refuter: confirmed / minor; needs_maintainer: False
location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3420 (previousStart computed without the loop test the chain applies at :3176) and :3702-3718 (KeepsItsPlace)

CLAIM:
The check answers Section at a place the /Prev's own reading never reads. That happens where the /Prev ends the chain in a loop: a newer section named that offset, as written, and found its section moved elsewhere. The check reads with the index as it now stands, so an object whose /W value only a section read since places now reads. The /XRefStm is then left to a /Prev that reads nothing. Its table's own /Type /XRef stream is never read, and two false reports follow: xref.hybrid-stream-lost 'which the chain looks for through that /Prev', and an xref.hybrid-stream-wrong Error. For an object of another /Type this is a regression of 2b26d18 against 19122f0, which read the table's stream. It is narrow: it needs a /W written as a reference, against 7.5.8.2, and a /Prev loop.

EVIDENCE:
Probe ZzLoop, in the same file. Outputs: loop.txt, b19122f0-loop.txt and main-loop.txt.
File:
- object 12 = '4';
- S2, a /Type /XRef stream that indexes 5, at 1445;
- object 8, a string;
- Q = object 4 at 1784, '<< /Type /XRefStream /Size 13 /W [1 12 0 R 2] /Index [6 1] /Length 7 >> stream';
- T2 at 1896, indexing 1-3 and 12, with /Prev 1784 and /XRefStm 1766. 1766 lies inside object 8's string, 18 bytes before Q and 321 after S2;
- T3, the newest, with /Prev 1784.
b4ab948:
- S /Prev Relocated Table off=1896 named=1784
- S /XRefStm NamedAgain off=1766 readAs=- nwpr=True
- D xref.hybrid-stream-lost @1766 '...what its /Prev names too, which the chain looks for through that /Prev...'
- D xref.chain-cycle @1784 '...names offset 1784, which the chain has already read.'
- object 5 is 'five' only after a rebuild
- F xref.chain-loop [Error]
- F xref.hybrid-stream-wrong [Error] '...what its /Prev names too, which the chain looks for through that /Prev...'
19122f0: S /XRefStm Relocated Stream off=1445 'found 321 bytes before it', and no hybrid-stream-* reports.
Untyped object 4: the same output on b4ab948 and on 19122f0.
Main 21a0298: no hybrid-stream-* reports either.

REVIEWER FIX:
Ask the check only where the /Prev's own reading will come to the place. Where ReachedBefore(previous, visited) holds, the /Prev ends in a loop and keeps no place. That is the test the chain applies to it next, and it answers from the memo. So set previousStart = -1 there, or have the check answer None. Add the /Type /XRefStream row: /XRefStm Relocated to S2, no hybrid-stream-* reports, the chain-cycle kept.

REFUTER REASON:
The finding holds, and so does its scope. I rebuilt the reproducer with my own builder and ran it on b4ab948, 19122f0 and main 21a0298.

How it happens:
- The newest table N names Q through /Prev. Q is object 4, whose dictionary has /W [1 12 0 R 2]. When N is read, no section has placed object 12, so Q does not read as a section and the /Prev finds T nearby.
- T places object 12. T's /Prev names Q again, and its /XRefStm names a byte inside object 8's string, 23 bytes before Q.
- previousStart (PdfFileReader.cs:3420) is computed without the loop test that the top of the loop applies (:3176, ReachedAt over visited). So the /XRefStm's search asks KeepsItsPlace at Q.
- KeepsItsPlace reads Q with the index as it now stands. 12 0 R resolves to 4, Q reads, and the check answers Section.
- The /XRefStm is left to the /Prev (LeaveToPrevious). But that /Prev ends in a chain-cycle and reads nothing.
- This breaks LeaveToPrevious's own premise: "The /Prev reads it next, whole". It also breaks round 3's [0] decision that the search stops only where the /Prev keeps its place as its own reading does.
- T's own /Type /XRef stream S is never read. Object 5 is found only through a rebuild. Two reports follow whose stated cause is false: xref.hybrid-stream-lost "which the chain looks for through that /Prev", and an xref.hybrid-stream-wrong Error.

Comparison with the other commits:
- Against 19122f0 this is a regression of 2b26d18 for an object of another /Type (/XRefStream, /Font). At 19122f0, S is found 276 bytes before, object 5 needs no rebuild, and there are no hybrid-stream-* reports.
- Untyped Q gives the same output on both commits.
- Against main, the object outcome is the same: main read T itself in the stream's place, silently, and object 5 also needed a rebuild there. What b4ab948 adds against main is the two reports with a false cause.

Narrowness: it needs a /W written as a reference (against 7.5.8.2), a /Prev that two tables name, and an /XRefStm that is off by a few bytes. So it is minor rather than major.

Related cases on the same path (not regressions, but the fix should cover them):
- The untyped case has been there since 19122f0.
- So has the first branch, where the /XRefStm names the white space before what a looping /Prev names (LeaveToPrevious with shortBy).

Not covered by any open debt: #330 is the mirror order, #320 is dropped relocation rows.

REFUTER EVIDENCE:
Probe ZzLoopRefute, my own builder. Source: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute-loop/ZzLoopRefuteTests.cs.txt. Outputs head.txt, head2.txt, b1912.txt, b1912-2.txt, main.txt, fixed.txt and suite-fixed.txt are in the same folder.

Layout:
- S (/Type /XRef, /Index [5 1]) at 1128;
- the /XRefStm names 1404, inside object 8's string;
- Q = object 4 at 1427, '<< /Type /XRefStream /Size 13 /W [1 12 0 R 2] /Index [6 1] /Length 7 >> stream';
- T at 1539 indexes 1-3 and 12 (= 4), with /Prev 1427 and /XRefStm 1404;
- N, the newest, at 2657 with /Prev 1427.

Case A (other /Type, loop) on b4ab948:
- S /Prev Relocated Table off=1539 named=1427
- S /XRefStm NamedAgain off=1404 nwpr=True
- D xref.hybrid-stream-lost @1404 '...names offset 1404, what its /Prev names too, which the chain looks for through that /Prev...'
- D xref.chain-cycle @1427
- obj5=five, repaired=True right after that lookup
- F xref.chain-loop [Error]
- F xref.hybrid-stream-wrong [Error] '...what its /Prev names too, which the chain looks for through that /Prev...: what only that stream indexes is lost.'

Case A on 19122f0:
- S /XRefStm Relocated Stream off=1128
- D offset-adjusted 'found 276 bytes before it'
- D chain-cycle
- obj5=five, repaired=False
- no hybrid-stream-*

Case A on main 21a0298:
- /XRefStm Relocated Table off=1539 (T itself)
- obj5 found through a rebuild
- no hybrid-stream-*

Other cases:
- D (/Type /Font) gives the same as A on all three commits.
- B (untyped Q): b4ab948 and 19122f0 are identical (NamedAgain plus both false reports). Main reads Q as the /XRefStm's stream.
- F and G (the /XRefStm names the white space before Q, loop): identical false reports on b4ab948 and 19122f0, through the first branch.
- C (no loop, N's /Prev names T): the /Prev reads Q and the search stops there, as round 3's [0] decided. That control is unchanged by the fix.

Fix tried (fix.diff in the same folder): previousStart = -1 where previous >= 0 && ReachedBefore(previous, visited).
- A and D are back to 19122f0's output: S found, obj5 with no rebuild, no hybrid-stream-*, chain-cycle kept.
- B, F and G: the /XRefStm reads Q where it names it or finds it moved, with no false hybrid-stream-*, and the chain-cycle is kept.
- C and H are unchanged.
- Whole unit suite with the fix: 7,601 tests, 0 failed, 20 skipped.
- Reverted afterwards; the worktrees were removed.

REFUTER FIX:
In TryReadXRefChain (PdfFileReader.cs:3420), compute previousStart only where the /Prev can come to the place, using the test the loop applies next:

var previousStart = hybrid >= 0 && hybridReached < 0 && !(previous >= 0 && ReachedBefore(previous, visited)) ? StartOfNamed(previous, out previousTable) : -1;

Why this is safe:
- visited and _sectionsReadAt only grow, so a /Prev that ReachedBefore marks here always loops at the top of the next iteration.
- ReachedAt's memo answers the probe.

Prefer this to "have the check answer None". It also covers the first branch, where the /XRefStm names the white space before what a looping /Prev names, which LeaveToPrevious reports with the same false cause today.

Add rows to An_xrefstm_stops_short_of_its_stream_only_where_its_table_s_prev_keeps_its_place, or a sibling test:
- /Type /XRefStream Q with /W through a reference, its /Prev named by a newer table: /XRefStm Relocated to S, object 5 with no rebuild, no hybrid-stream-*, the chain-cycle kept. This row fails on b4ab948.
- The untyped and white-space variants: no hybrid-stream-*, the chain-cycle kept.

Then correct the summary of KeepsItsPlace and the leaveAt param doc to say that a /Prev that loops keeps no place.

This is not a maintainer question: it implements round 3's [0] as decided.

######## [2] lens chain-keep — markcutbyguard-in-the-check-has-a-killing-input
reviewer severity: minor; refuter: confirmed / minor; needs_maintainer: False
location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3790-3795 (TryParseUnrecordedAt, MarkCutByGuard); no test covers it

CLAIM:
M40 (the condition negated) and M41 (the call removed) were left unsettled, as if no input could tell them apart. An input does tell them apart; this is a test gap (ADR 53), not a defect. Two ingredients are needed together:
- the trailer guard's window ends exactly at the end of an untyped /W stream's 'stream' keyword, with a regular byte after it, so the parse keeps a stream whose data starts at the window's edge;
- one /W width is a reference to an object MaxObjectLength cuts.
UnreadValue then finds a value a guard cut, and FaultBesideCut is handed the stream only where its data is not marked cut.
- Marked (HEAD): no file fault, CutByValue, KeptPlace.Guard. The naming is the guard's, as the /Prev's own reading, which marks it, then finds.
- Unmarked (M41; or M40, which marks only reads no guard cut): FaultBesideCut decodes the 6 bytes past the edge and finds 'holds at most 2 of the 10 rows' with holds = 2. The check returns Read, the answer is Section, and hybrid-stream-lost is reported.
The stage's own attempt had the edge but no cut value, so FaultBesideCut was never reached.

EVIDENCE:
Probe ZzCutStreamMarked and the assertion test ZzMarkedKiller. Outputs: cutstream-orig.txt, cutstream-m41.txt and cutstream-m40.txt.
Limits: MaxObjectLength 1024, MaxTrailerLength 2048.
Object 4 at 2087: '4 0 obj\n<< /W [1 12 0 R 2] /Size 10 /Length 6 /Pad (--...) >>\nstream', exactly 2,048 bytes, followed by 'X' and 6 bytes.
The newest table indexes object 12 = '%' + 3,000 dashes + '\n4'. Its /Prev is 2087 and its /XRefStm names inside object 8's string.
HEAD:
- S /XRefStm NamedAgain nwpr=False nns=-
- S /Prev Malformed cut=True cbv=True fault='its /W holds the reference 12 0 R, which one of the reader's limits cut'
- D limit.trailer, D limit.object
- object 7 null, no rebuild
M41: nwpr=True, plus D xref.hybrid-stream-lost @2069, D xref.rebuilt, object 7 'new seven'.
M40: the same as M41.
ZzMarkedKiller passes on HEAD and fails under both mutants: 'Expected sections[1].NamesWhatPreviousReads to be False, but found True.'
The default-limits variant (the 64 KB edge, MaxObjectLength 1024) and MaxTrailerLength 100,000 tell the mutants apart too. With a line feed after the keyword, M40 alone changes the answer to Guard.

REVIEWER FIX:
Add a test, for example in GuardCutValueTests: 'A_stream_whose_data_the_trailer_guard_cuts_and_a_width_a_guard_cut_leaves_the_prev_s_place_to_the_guard'.
Builder: as in the probe's BuildCutStream(prefix: 2048, after: "X"). That is HybridFiles.NamingAnObjectBesideTheStream's layout, with object 4 written raw so its 'stream' keyword ends 2,048 bytes after its header, plus object 12 indexed by the newest table.
Limits: MaxObjectLength = 1024, MaxTrailerLength = 2048.
Assert:
- sections Skip(1) equal (/XRefStm, NamedAgain, named) and (/Prev, Malformed, four);
- sections[2].CutByValue is true;
- sections[1].NamesWhatPreviousReads is false and NamesNoStream is null;
- the diagnostics equal [limit.trailer, limit.object];
- object 7 is PdfNull and WasRepaired is false.
Do not file M40/M41 as debt.

REFUTER REASON:
The finding holds as stated. On b4ab948, M40 and M41 at src/AdCodicem.Pdf/IO/PdfFileReader.cs:3792-3795 (TryParseUnrecordedAt) are left open. r3-fix-result.json says "Not settled, to file", and decisions.md says round 4 should build an input or else file them as debt.

I wrote my own builder and confirmed byte for byte that the 2,048-byte trailer window ends exactly on 'stream', with 'X' at the edge. That input tells both mutants apart from HEAD. The new test fails under each mutant, and the whole unit suite under each mutant fails only on that test, so no existing test kills either one.

HEAD is right: the check and the /Prev's own reading (TryParseNumberedAt, which marks the data too) agree on Malformed/CutByValue, so KeptPlace.Guard. Under either mutant the check takes the unmarked stream to FaultBesideCut and answers Section, while the /Prev's own reading still says Malformed with cbv=True. That breaks round 3's [0] rule that the /XRefStm stops short only where the /Prev keeps its place as its own reading does. The result is a spurious xref.hybrid-stream-lost, a rebuild, and object 7 served from the rebuild.

So this is a test gap under ADR 53, not a defect. It is not debt either, because a test can build the input. Minor, because the batch's mutation step stays open until these two survivors are killed.

The reviewer's account of the stage's own attempt also holds. With an edge but no cut value (my case C, object 12 whole), HEAD, M40 and M41 all give nwpr=True, so FaultBesideCut never runs.

REFUTER EVIDENCE:
My own probe, worktree at b4ab948; files saved in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute-mark/ (ZzRefuteMarkTests.cs.txt, head.txt, m41.txt, m40.txt, suite-m40.log, suite-m41.log).

The file is HybridFiles.NamingAnObjectBesideTheStream's layout with object 4 at 2087 written raw: '4 0 obj\n<< /W [1 12 0 R 2] /Size 10 /Length 6 /Pad (--...) >>\nstream'. That is 2,048 bytes, and the bytes at the edge read 'streamX'. Object 12 is '%' + 3,000 dashes + '\n4' and is indexed by the newest table. The /Prev is 2087 and the /XRefStm is 2069. Limits: MaxObjectLength 1024, MaxTrailerLength 2048.

HEAD:
- S /XRefStm NamedAgain off=2069 nwpr=False nns=-
- S /Prev Malformed off=2087 cut=True cbv=True fault=its /W holds the reference 12 0 R, which one of the reader's limits cut
- D limit.trailer @2087, D limit.object @5383
- repairedOpen=False, obj7=PdfNull

M41 (call removed):
- S /XRefStm NamedAgain nwpr=True nns=what its /Prev names too, which the chain looks for through that /Prev
- S /Prev is unchanged: Malformed cbv=True
- D xref.hybrid-stream-lost @2069, limit.trailer, limit.object, xref.rebuilt, limit.object @2087, object.redefined
- repairedOpen=True, obj7=new seven
- The validator adds F xref.hybrid-stream-wrong '... past the limit that cut it there ...'

M40 (if (!cut)): the same as M41 for this file.

Case D, the 64 KB default MaxTrailerLength with the edge at 64 KB, gives the same split: HEAD nwpr=False, M40 and M41 nwpr=True.

Case B, a line feed after the keyword:
- HEAD and M41: nwpr=True, and the /Prev is Read with 'holds at most 2 of the 10 rows'. The two agree.
- M40: nwpr=False, which is Guard. So that case kills M40 alone.

Assertion test, with the reviewer's assertions:
- It passes on HEAD.
- It fails under both M41 and M40 with 'Expected sections[1].NamesWhatPreviousReads to be False, but found True.'
- Whole unit suite (7,602 tests, 20 skipped): under M40 and under M41, 1 failed, and the only failure is that test.

The source was restored, and the worktree is clean.

REFUTER FIX:
Add a test that kills both survivors, and record M40 and M41 as killed, not filed as debt.

Placement: beside round 3's KeepsItsPlace tests in CrossReferenceChainTests (around line 4040, the theory over HybridFiles.NamingAnObjectBesideTheStream), or in GuardCutValueTests as the reviewer proposes.

Suggested name: A_stream_whose_data_the_trailer_guard_cuts_beside_a_width_a_guard_cut_leaves_the_prevs_place_to_the_guard.

Build:
- the layout of HybridFiles.NamingAnObjectBesideTheStream, with object 4 written raw so that 'stream' ends 2,048 bytes after its header, followed by 'X', 6 data bytes, then '\nendstream\nendobj';
- object 4's /W is [1 12 0 R 2], with /Size 10 and /Length 6;
- object 12 is '%' + 3,000 dashes + '\n4', indexed by the newest table.

Limits: MaxObjectLength = 1024, MaxTrailerLength = 2048.

Assert:
- Skip(1) of the (NamedBy, State, Offset) triples equals [(/XRefStm, NamedAgain, named), (/Prev, Malformed, four)];
- sections[2].CutByValue is true;
- sections[1].NamesWhatPreviousReads is false and NamesNoStream is null;
- the diagnostic codes equal [LimitTrailer, LimitObject];
- object 7 is PdfNull, and WasRepaired is false.

Optional: a second case with '\n' after the keyword, asserting NamesWhatPreviousReads true and the /Prev Read. It holds the Section answer and kills M40 on its own.

No code change, and no maintainer decision is needed.

######## [3] lens chain-line — line-readback-short-window-hangs-or-throws
reviewer severity: blocker; refuter: confirmed / blocker; needs_maintainer: False
location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:4917-4921 (IsTokenOnItsLine read-back) and :4934 (StartsToken loop)

CLAIM:
2b26d18's read-back assumes the window holds every byte it asked for: `before + span.Length`. A source may return fewer. PdfFileSource.Read returns 'the count read', GetWindow keeps a short window, and FileSource returns a short count when the file shrinks while open, which is exactly what CutShortSource models. When the window is shorter than `before`, `bytes[..before]` throws ArgumentOutOfRangeException out of PdfDocument.Open. When it is long enough to hold the end of line but ends at or before the place, StartsToken's `while (token.End <= at)` gets EndOfInput, whose End is bytes.Length, which is <= at, over and over: an infinite loop that never returns. Neither 19122f0 nor main reads there, and neither hangs. A custom source capped at 4,096 bytes per Read hangs on an ordinary damaged file (wrong /Prev offset, a comment line longer than the search window); main opens the same file correctly. Invariant 4: a crash and a denial of service.

EVIDENCE:
Probe ZzLineReadBackTests, saved at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r4-chainline/ZzLineReadBackTests.cs.txt, with outputs beside it.

The file: T0 indexes 1-4 (4 = '(old four)'). Then a 6,000-byte comment, then the line '%' + 700 'a' + ' xref see the table below', then object 6. The newest table's /Prev names 2 bytes into 6's endobj (7067). The update table is at 7132.

The source cuts the file when the reader first reads at the read-back offset 2458 (spanStart 6554 - 4096).
- Head, cut to 2558: `threw ArgumentOutOfRangeException ... at PdfFileReader.IsTokenOnItsLine ... PdfFileReader.cs:line 4919`.
- Head, cut to the place minus 10: `finished=False cut=True: still running after 20 s` (the thread still spins).
- 19122f0 and main, same rows: `cut=False` (they never read there) and open. Main: '/Prev Relocated 7132 ... found 65 bytes from there', four=null.

A source that returns at most 4,096 bytes a Read, on the uncut file:
- head: `chunk 4096 letters 700: finished=False: still running after 20 s`;
- main: `opened: repaired=False four=null | /Prev Relocated 7132 ... found 65 bytes from there`;
- 19122f0: opens (with round 3 [1]'s bug);
- head with a 100-letter line, or with a 65,536-byte cap: opens.

Prototype fix, applied and then reverted: `if (bytes.Length <= before + at) return false;` after reading the window.
- Hang and crash rows: they open, with 'xref.section-missing ... xref.rebuilt' (the file is cut there).
- 4,096-cap row: 'Relocated 7132, found 65 bytes after it', four=null, as on main.
- CrossReferenceChainTests, CrossReferenceRuleTests, HostileInputTests and GuardCutValueTests: 1,509 passed, 0 failed.

REVIEWER FIX:
1. In IsTokenOnItsLine, after reading the window, return false (the place declares nothing, as past the bound) when `bytes.Length <= before + at`.
2. Make StartsToken's loop stop at EndOfInput too (`while (token.End <= at && token.Kind != PdfTokenKind.EndOfInput)`), so no input length can make it spin.
3. Add two tests, both opening without throwing and in bounded time:
   - a CutShortSource row (CutWhenReadAt at spanStart - 4096, cut below `before` and between `before` and the place);
   - a source capped at 4,096 bytes a read, on the 700-letter file, which reads as main does (found 65 bytes after, object 4 null).
4. Remove the remark's unqualified 'read again with them'.

REFUTER REASON:
The finding holds, and its claim says no more than the evidence shows. 2b26d18's read-back in IsTokenOnItsLine (PdfFileReader.cs:4915-4921) asks for `before + span.Length` bytes and then uses the window as if it held all of them. GetWindow keeps whatever one Read returns, and PdfFileSource.Read's public contract is only 'returning the count read'. FileSource returns a short count when the file shrinks while it is open; CutShortSource models exactly that, and the suite already tests it.

There are two failure paths:
- **Crash.** When the window is shorter than `before`, `bytes[..before]` throws ArgumentOutOfRangeException out of PdfDocument.Open.
- **Infinite loop.** When the window ends at or before the place, StartsToken's `while (token.End <= at)` reads EndOfInput forever. PdfLexer.Read returns `new PdfToken(EndOfInput, _position, _position)` with `_position == data.Length <= at`.

19122f0's IsTokenOnItsLine lexed only inside the window it searched, where the place always lies below span.Length, so the regression is new in round 3's fix.

What it takes to reach it: FromMemory, FromStream, and FromFile over a file that does not change never come up short here. The read-back ends where the window it searches ends, inside Length, and GetWindow clamps nothing. Only two cases reach it:
- a caller's PdfFileSource whose Read returns fewer bytes than asked, which the contract allows;
- a file cut short while it is open.

With such a source, though, an ordinary damaged file reliably crashes or spins a thread forever: a /Prev a few bytes off, next to a comment line longer than the 512-byte radius. Main and 19122f0 open the same file. Under the brief that is a hang and a crash, a blocker, and it breaks invariant 4.

REFUTER EVIDENCE:
I rebuilt the reviewer's probe (ZzLineReadBackTests, same file: the word at 7026, the /Prev naming 7067, the update table at 7132, the bytes searched starting at 6554, the read-back from 2458) at b4ab948 in my own worktree, Release build.

Head b4ab948:
- crash row (the source cuts the file to 2558 when first read at 2458): `finished=True cut=True: threw ArgumentOutOfRangeException ... at PdfFileReader.IsTokenOnItsLine(...) in .../PdfFileReader.cs:line 4919`
- hang row (cut to the word minus 10): `finished=False cut=True: still running after 20 s`
- a source capped at 4,096 bytes a Read, uncut, 700 letters: `finished=False cut=False: still running after 20 s`
- the same with 100 letters, or a 65,536-byte cap: they open (`/Prev Relocated 7132 ... 65 bytes after it`, four=null).

19122f0, the same probe in a worktree built there:
- every row finishes, and `cut=False` on the crash and hang rows: the reader never reads at 2458.
- They read with round 3 [1]'s bug, which is not this finding: `/Prev Malformed 7026 ... xref.section-unreadable ... xref.rebuilt`, four="old four".
- I did not rebuild main 21a0298. It has no read-back either (`git show 21a0298` has no IsTokenOnItsLine/LineStartSearchLength). The reviewer's saved main output shows the 4,096 cap opening with `Relocated 7132 ... found 65 bytes from there`, four=null.

The code:
- PdfLexer.cs:62-64 returns EndOfInput with Start = End = data.Length.
- StartsToken (PdfFileReader.cs:4934) loops `while (token.End <= at)`, so the loop cannot exit once data.Length <= at.
- The 4,096-cap path: before = 4096, the read returns 4096 bytes, at = 472, so 4096 <= 4568 and the loop never exits.

The prototype fix (`if (bytes.Length <= before + at) return false;`, placed before `bytes[..before]`) was applied, rebuilt and run:
- crash row: `opened ... /Prev NotFound 7067 | xref.section-missing ... | xref.rebuilt`
- hang row: the same.
- 4,096 cap: `/Prev Relocated 7132; /Prev Read 159 | ... found 65 bytes after it`, four=null, as main reads it.
- Then reverted.

decisions.md [1] (line 204) decided a read-back 'through an internal-constant window' and says nothing about a short read.

REFUTER FIX:
1. In IsTokenOnItsLine, right after `var bytes = window.Memory.Span;` and before `bytes[..before]`, add `if (bytes.Length <= before + at) return false;`.
   - The source ended before the place, so the place declares nothing and is dropped as any other.
   - A token starts at `at` exactly when the byte there is present, since lexing depends only on the bytes before it. So this is the only short-window case that has to be refused.
2. Defense in StartsToken: stop at EndOfInput, and do not count EndOfInput as starting there:
   ```
   while (token.End <= at && token.Kind != PdfTokenKind.EndOfInput) { ... }
   return token.Kind != PdfTokenKind.EndOfInput && token.Start == at;
   ```
   The reviewer's version keeps `return token.Start == at`, which would answer true when the input ends exactly at `at`.
3. Tests, each opening without throwing and inside a bounded wait (run Open on a background task with a join timeout, so the unfixed code fails rather than stalls the run):
   - a CutShortSource row: CutWhenReadAt(spanStart - 4096), cut once below `before` and once between `before` and the place;
   - a source capped at 4,096 bytes a Read, on the 700-letter file. It must read as main does: `/Prev` Relocated 7132, found 65 bytes after it, object 4 null.
   - Both hang or throw on b4ab948.
4. Qualify the remark's 'read again with them': as far as the source returns them; where it returns fewer than reach the place, the place declares nothing.

A design option for the maintainer, not needed for this fix: GetWindow could loop on Read until the count or a 0 read, so a source capped per Read serves whole windows everywhere. The alternative is for the contract to say that Read fills the buffer unless the source ends. As it stands, a short-reading source turns any window short, which the reader treats as the file ending there.

######## [4] lens chain-line — line-test-cr-line-ends-untested
reviewer severity: minor; refuter: narrowed / minor; needs_maintainer: False
location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:4906 and :4919 (LastIndexOfAny((byte)'\r', (byte)'\n') in IsTokenOnItsLine)

CLAIM:
No test pins a line that a lone CR ends, in either half of the line test. The ADR 53 mutant 'remove one element of a set', with '\r' dropped, survives at both lines changed in this batch: the in-window line start and the read-back end of line. An input tells each mutant apart. The R3a mutant list does not settle these two.

EVIDENCE:
Mutant :4919 `bytes[..before].LastIndexOf((byte)'\n')`, then mutant :4906 `span[..at].LastIndexOf((byte)'\n') + 1`, each alone.
- The 11 chain classes (CrossReferenceChainTests, CrossReferenceRuleTests, HostileInputTests, GuardCutValueTests, ReaderLimitsTests, HybridCrossReferenceTests, WindowEdgeTests, DocumentReaderTests, PropertyTests, ReadOrderTests, StreamLengthTests): 2,801 total, 0 failed under each mutant.

Killing inputs, from the probe in the file above:
- (a) ZzKeywordAfterLongLineEndedBy('\r'): a 5,000-byte comment, then '4 0 obj [400 zeros] endobj xref 0 1 bad', lines ended by a lone CR, the /Prev naming 50 bytes before the keyword.
  - Head: '/Prev Malformed 6053 | xref.offset-adjusted ... found 50 bytes after it | xref.section-unreadable'.
  - Read-back mutant: '/Prev NotFound 6003 | xref.section-missing'.
  - The LF row reads Malformed under both.
- (b) ZzKeywordOnShortLineAfterLongComment('\r'): a 5,000-byte comment ended by CR, then 'endobj xref 0 1 bad'.
  - Head: 'Malformed 5242, found 50 bytes after it'.
  - In-window mutant: 'NotFound 5192, xref.section-missing'.

REVIEWER FIX:
Add CR rows to An_xref_keyword_on_a_line_that_starts_before_the_bytes_searched_is_lexed_from_the_line_s_start: the file's lines ended by a lone CR, no LF within 4,096 bytes before the bytes searched. Add one where the CR lies inside the bytes searched (shape b). Both kill the two mutants; confirm the run.

REFUTER REASON:
The finding holds. I rebuilt both mutants on b4ab948 (origin/ccr-6d6c0ee6-dqm4lm-188). Each one alone survives the whole AdCodicem.Pdf.Tests project, not just the 11 chain classes the finding ran. A hand-built CR file tells each mutant apart from head. Both lines belong to this batch: main 21a0298 has no IsTokenOnItsLine. Line 4906 was already at 19122f0, as the argument to the PdfLexer constructor, and 2b26d18 moved it out. Line 4919 is new in 2b26d18. R3a's 83 mutants and R2a's list include only line-start shifts and dropped conditions (M61, M64, M71, M76, M78, token-line-plus0). Neither list has ADR 53's item 5, 'remove one element of a set', at these two LastIndexOfAny calls. The code at head handles a lone CR correctly. A regression that drops '\r' would go unnoticed, and classic Mac files end their lines with a lone CR. So this is a test gap and an incomplete ADR 53 step, not wrong behavior: minor. One narrowing: the two proposed rows do not each kill both mutants. Shape (a) kills only the read-back mutant (:4919), and shape (b) kills only the in-window mutant (:4906). Together they kill both. The finding is right to require no LF within 4,096 bytes before the bytes searched. With the existing test's 1,200-byte comment, the read-back mutant finds an LF farther back and lexes the same tokens. That LF is in the first table's rows, which HandWrittenFile ends with "\r\n" even when eol is "\r". So such a CR row would not kill it.

REFUTER EVIDENCE:
The probe is tests/AdCodicem.Pdf.Tests/ZzCrLineTests.cs. It was uncommitted and has been removed since. It uses HandWrittenFile(eol) and a /Prev that names 50 bytes before the keyword.

Shape (a), ZzKeywordAfterLongLineEndedBy: a 5,000-byte comment, then '4 0 obj [400 zeros] endobj xref 0 1 bad', every line ended by eol.
- Head, eol "\r", "\n" and "\r\n": 'sec1 Malformed 6053 | xref.offset-adjusted: ... at offset 6003 was found 50 bytes after it. | xref.section-unreadable: ...'. The CRLF row reads Malformed 6068.
- Read-back mutant (:4919 `bytes[..before].LastIndexOf((byte)'\n')`), eol "\r": 'sec1 NotFound 6003 | xref.section-missing: The cross-reference section /Prev names at offset 6003 is not there, nor near it; ...'. The "\n" and "\r\n" rows still read Malformed.
- In-window mutant (:4906 `span[..at].LastIndexOf((byte)'\n') + 1`), eol "\r": Malformed 6053, the same as head, so shape (a) does not kill it.

Shape (b), ZzKeywordOnShortLineAfterLongComment: a 5,000-byte comment, then 'endobj xref 0 1 bad'.
- Head, eol "\r" and "\n": 'sec1 Malformed 5242 | ... at offset 5192 was found 50 bytes after it. | xref.section-unreadable'.
- In-window mutant, eol "\r": 'sec1 NotFound 5192 | xref.section-missing ...'. The "\n" row still reads Malformed.
- Read-back mutant, eol "\r": Malformed 5242, the same as head, so shape (b) does not kill it.

Whole unit project, `dotnet test --project tests/AdCodicem.Pdf.Tests/AdCodicem.Pdf.Tests.csproj -c Release --no-build`, with no filter:
- Read-back mutant: 'total: 7605, failed: 0, succeeded: 7585, skipped: 20'.
- In-window mutant: 'total: 7605, failed: 0, succeeded: 7585, skipped: 20'.
- The skipped tests are remote corpus documents this machine did not fetch.

The R3a mutants in r3-fix-result.json reports[0] list no set-element removal at either LastIndexOfAny.

Existing CR coverage does not reach these lines. The row "7\r% one\r0 % two\robj" in A_cross_reference_stream_whose_header_holds_comments_is_found_near_the_offset_prev_names has an LF before the header's number, so neither mutant changes its result.

REFUTER FIX:
Add two CR rows to CrossReferenceChainTests, for example by turning An_xref_keyword_on_a_line_that_starts_before_the_bytes_searched_is_lexed_from_the_line_s_start into a theory over the end of line, or by adding a sibling theory.

- Row 1 kills the read-back mutant at :4919. The file is written with HandWrittenFile(eol: "\r"). A comment of at least about 4,700 bytes, ended by a lone CR, comes right before the long '4 0 obj [...] endobj xref 0 1 bad' line, so that no LF lies within 4,096 bytes before the bytes searched. Expected: sections[1] Malformed at the keyword, xref.offset-adjusted then xref.section-unreadable.
- Row 2 kills the in-window mutant at :4906. The same 5,000-byte comment ended by a lone CR is followed by the short line 'endobj xref 0 1 bad', so the CR lies inside the bytes searched. Expected: the same Malformed result at the keyword.

Keep the LF rows as controls. Run each mutant alone against the new rows and confirm the kill: row 1 fails under :4919 with NotFound and xref.section-missing, and row 2 fails under :4906 with NotFound. Record both in the batch's ADR 53 log. No maintainer decision is needed.

######## [5] lens chain-line — line-readback-repeated-per-place-in-one-window
reviewer severity: minor; refuter: narrowed / nit; needs_maintainer: False
location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:4904-4921 (IsTokenOnItsLine, called per place from :4859)

CLAIM:
The read-back runs once for every place that cannot be read, yet every place in one search shares the same line start before the bytes searched: the last end of line before spanStart. One search therefore reads the same 4,096 + 1,025 bytes up to 32 times. That cost is bounded (MaxRelocationCandidates, about 164 KB a document) but avoidable, and it doubles what main reads on a file built for it.

EVIDENCE:
ZzManyWordsOnOneLongLine: a 6,000-byte comment, then a comment line of 300 x 'aaaaa xref ', with the newest table's /Prev naming the middle of that line. Bytes read (counting source):
- head 330,010;
- main 166,137;
- 19122f0 72,508 (it took the first word, round 3 [1]'s bug).

Head minus main is 163,873, which is 32 x 5,121 + 1. The diagnostics are the same on head and main ('/Prev NotFound 7885 | xref.section-missing ... | xref.rebuilt').

REVIEWER FIX:
Read back at most once a search. On the first place whose line starts before the bytes searched, find the end of line before spanStart, or none within the bound. Keep that window, or its absolute position, open for the rest of TryRelocateXRefSection, and lex later places from it. Say 'one window more at most for each search' in the remark. Alternatively, if the per-place cost is kept, record it as accepted; it is within the bound decision [1] took.

REFUTER REASON:
The facts hold, and I rebuilt them myself. In IsTokenOnItsLine (PdfFileReader.cs:4904-4921), the read-back runs only for a place with no end of line in span[..at], so every such place in one search has the same line start, the last CR or LF before spanStart. `from` and the window length are identical too. Each such place re-reads the same up-to-5,121-byte window, at most 32 times a document (the cap is per document, checked before each attempt), about 164 KB in all. Round 3's [1] fix added this and main has none of it.

Three things narrow it:
1. **It is the cost round 3 chose.** Round 3's [1] refuter's fix, which decision [1] adopted, says "The cost stays bounded: one extra window per unreadable place, under MaxRelocationCandidates". The remark at :4898-4899 states exactly that: "once for each place that cannot be read ... and one window more at most for each". So no doc misstates it, and no decision is broken. It is an optimization inside a bound that was accepted and is written down.
2. **The proposed fix removes less than half of what head adds.** Keeping the window open, as the fix proposes, cuts the bytes back to main's plus one window. On a file source it recovers about 58 of the roughly 136 µs head adds per open. The rest is the lexing each place does from its line's start: about 2.2 KB byte by byte, since a comment is skipped to the end of the input. That lexing stays under the proposed fix.
3. **Memory sources pay nothing extra in reads.** GetWindow on a memory source hands back a slice without copying (TryGetMemory). The doubled bytes read applies only to file, stream and custom sources.

The absolute cost is tiny: about 0.1 ms and 164 KB of page-cache reads, on a hand-built hostile file. Main already lets each of the 32 attempts read up to 64 KB, about 2 MB in all, and this adds at most about 8% of that. I set nit rather than minor.

REFUTER EVIDENCE:
**Probe.** /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r4-refute-readback/ZzReadBackRefute.cs.txt. The file:
- table T0;
- a 6,000-byte comment;
- the comment line '%' + N x word;
- 1,200 bytes of comment;
- the newest table, whose /Prev names the middle of the long line.

Bytes are counted with StrictCountingSource. Times are the median of 5 x 4,000 PdfDocument.Open(path) calls. Outputs are head.txt, main.txt and proto.txt in the same folder.

**300 x 'aaaaa xref ' (3,300-byte line).** All three report the same thing: 'xref.section-missing @7885 | S Read 10739 | S NotFound 7885', repaired=False.

| Build | Bytes read | Open by path |
|---|---|---|
| main 21a0298 | 117,254 | 51.2 µs |
| head b4ab948 | 281,127 | 186.7 µs |
| proto (window kept once a search) | 122,376 | 128.7 µs |

- Head minus proto is 158,751 = 31 x 5,121.
- Proto minus main is 5,122, one window.
- Proto's diff, since reverted: proto-once-a-search.diff in the same folder.

**300 x (51 'a' + ' xref ').** This line starts more than 4,096 bytes back, so it is read back but never lexed.

| Build | Bytes read | Open by path |
|---|---|---|
| main | 196,515 | 47.0 µs |
| head | 283,573 | 56.9 µs |
| proto | 201,637 | 50.7 µs |

**30 x 'aaaaa xref ' (330-byte line inside the window).** No read-back: head 64,850 bytes, main 64,849.

**Where it is written:**
- r3-findings.md [1], refuter's FIX: "The cost stays bounded: one extra window per unreadable place, under MaxRelocationCandidates."
- PdfFileReader.cs:4898-4899: "once for each place that cannot be read, which MaxRelocationCandidates bounds, and one window more at most for each".
- PdfFileSource.GetWindow:121-137 lends memory without a copy where TryGetMemory succeeds.

REFUTER FIX:
Optional; either way is acceptable.

**(a) Accept it.** Record it in decisions.md under round 4 as the per-place cost round 3's [1] named, within MaxRelocationCandidates. Nothing in the code or docs changes.

**(b) Fix it properly.**
- In TryRelocateXRefSection, open the read-back window lazily, on the first place that needs it. Every such place shares the same `from` and length.
- Keep it open for the rest of the loop and dispose it in a finally at the search's end.
- Better still, lex the line once from its start through that window and keep the token starts it finds. Each later place is then a lookup, and not a fresh lex of up to 5 KB, which is the larger part of head's extra time.
- Change the remark at :4898-4899 to "one window more at most for each search".
- Pin it with a test that counts bytes read through StrictCountingSource on a many-words line: at most main's count plus one window.

Keeping the window but lexing per place, as the finding proposes, saves only the re-reads.

This is not a maintainer question.

######## [6] lens sections — sections:mirror-reread-counts-own-declared-refusal-as-reserved-refused
reviewer severity: minor; refuter: narrowed / minor; needs_maintainer: False
location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:7228 (AddFromChain's RefusedToOlderRows arm, with no refuser check), :7201 (ReservedRowsRefused++), :3349-3361 (readAgain block), :3386-3389 (RefuseRowsLost after the /XRefStm read in part); src/AdCodicem.Pdf/Validation/Rules/ReservedTypeRule.cs:55

CLAIM:
8ccd868 regressed a validator finding. Take a stream that the hybrid table's /XRefStm reads in part, which then refuses its own declared ranges (RefuseRowsLost -> RefuseDeclared, ranges kept with no refusing section), and which the table's /Prev reads again whole (R2 [1]'s mirror of branch (c)). On that second reading, every row of the stream hits RefusedToOlderRows: its own first reading's ranges. AddFromChain returns false, so each reserved-type row is counted in ReservedRowsRefused on the /Prev record (and RowsRefusedBefore is set there too). The reader reports nothing more for readAgain, but ReservedTypeRule reads that record and says the reader 'did not read the row so, its number refused before it was read', while the reader read it as the null object. Round 3's [4] rule ('only another section's refusal') was applied to _refusedNumbers only, never to the ranges. XRefSectionRecord.ReservedRowsRefused's doc and validation-rules.md:55 ('by a newer section, or by a row of the stream's own for the same number') do not cover this case either.

EVIDENCE:
Probe ZzR4SecProbe.ZzMirror (source /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r4sec/ZzR4SecProbe.cs.txt; outputs out-head-final.txt and out19122-final.txt in the same folder). The file is HybridFiles.PrevFallingShortOfTheStreamBefore(reservedRow) with object 7 '(seven as the oldest has it)' indexed by the oldest table, and stream 6 declaring '/Index [4 1 6 3]' while holding rows 4, 6 and 7 (type 7), one row short. The newest table at 1845 gives '/Prev 1675 /XRefStm 1682'.
Head b4ab948: reader `Warning xref.section-unreadable @1682: The cross-reference stream /XRefStm names at offset 1682 was read only in part: it holds 3 rows where its /Index and /Size declare 4; the rows it holds were kept, ...`, `xref.entry-reserved @1682: ... the row was read so, and no older section's row stands for its object.`, `xref.offset-adjusted`, `xref.hybrid-stream-lost`. Record `S /Prev@1682 Relocated Stream inc=True reserved=1 reservedRefused=1 rowsRefusedBefore=True`, `idx 7=Free`, `obj 7=PdfNull repaired=False` (read as null before validation). Rule: `F Warning xref.entry-type-reserved: The cross-reference stream at offset 1682 gives 1 row a reserved type: the row for object 7 gives it type 7. ISO 32000-1 defines types 0, 1 and 2, and reads any other as a reference to the null object (7.5.8.3), but the reader did not read the row so, its number refused before it was read: it reads its object as the rebuild finds it.`
19122f0, same file: same reader output, but the rule says `..., as the reader did until it rebuilt the index by scanning the file; it now reads each object such a row numbers as the rebuild finds it.`, which is true.
Control, the stream read whole (`/Index [4 1 6 2]`): head gives reservedRefused=0 and the 'did until it rebuilt' text. The existing test A_prev_falling_short_of_the_stream_its_xrefstm_read_reads_it_whole_and_the_naming_is_reported(reservedRow) counts the rule's findings and never checks their text, so it passes either way.

REVIEWER FIX:
In the readAgain block (PdfFileReader.cs:3351-3354), carry what the first reading found onto the /Prev's record, since the rows are the same: `section.ReservedRowsRefused = streamsPlace.ReservedRowsRefused; section.RowsRefusedBefore = streamsPlace.RowsRefusedBefore;`. Alternatively, make the second reading not count a refusal that the place's own RefuseRowsLost made. Add a read-in-part row to A_prev_falling_short_of_the_stream_its_xrefstm_read_reads_it_whole_and_the_naming_is_reported, in HybridFiles: `/Index [4 1 6 3]` with the reserved row, plus an older definition of object 7. Assert the full xref.entry-type-reserved text ('as the reader did until it rebuilt the index ...'), that ReservedRowsRefused is 0 on sections[2], and that object 7 reads null before validation. Run ADR 53's catalog over the added lines.

REFUTER REASON:
The finding holds. I rebuilt it with a probe of my own, on b4ab948 and on a 19122f0 tree exported with git archive.

Case: R2 [1]'s mirror of branch (c). The table's /XRefStm reads stream 6 at its header, and the stream turns out to be read in part. RefuseRowsLost then refuses the stream's own declared ranges through RefuseDeclared, and those ranges record no refusing section. The table's /Prev, 7 bytes short of the stream, reads the stream again, in the readAgain block at PdfFileReader.cs:3349-3361.

On that second reading, every row hits RefusedToOlderRows (:7228), because the first reading's own ranges cover it. AddFromChain returns false, so the reserved arm (:7199-7202) counts the row in ReservedRowsRefused on the /Prev record. The first reading had indexed the row as free.

The readAgain block skips the reader's reports, so the reader's text still comes from the first reading and is true: "the row was read so". Object 7 reads as PdfNull before validation. ReservedTypeRule (:55) reads the /Prev record, finds ReservedRowsRefused == ReservedRows, and says "the reader did not read the row so, its number refused before it was read". That is false, and it contradicts the reader's own diagnostic.

On 19122f0 the rule chose its text only from WasRepaired, and said "as the reader did until it rebuilt the index", which is true. So 8ccd868 regressed this text. Round 3's [4] check (refuser != section) covers only _refusedNumbers, not the declared ranges. No decision, round, or open point in r3-fix-result.json settled this case.

What I narrow:
(1) It is text only. The index, the objects served before and after validation, the repairs and the diagnostic codes are identical on 19122f0 and head.
(2) Only an invalid file reaches it: a stream read in part, holding a reserved-type row, named by both the /XRefStm and a /Prev that falls short of it.
(3) RowsRefusedBefore=True on the /Prev record is older: 19122f0 shows it as well. It is also invisible. Only ReportSectionReadInPart reads it, readAgain skips that call, and no validator rule reads it.
(4) The docs do not need changing. validation-rules.md:55 and the XML doc on XRefSectionRecord.ReservedRowsRefused ("by a newer section, or by a row of the stream's own for the same number") describe the intended rule, which excludes this case. The code is what departs from them.

The own-duplicate shapes come out right on head, read whole and read in part: a refused row for 7, then a reserved row for 7. Their "did not read the row so" is true, and the fix keeps it. This is not a design question for the maintainer.

REFUTER EVIDENCE:
Probe ZzRefuteMirror, built in Release with 0 warnings. It is the HybridFiles.PrevFallingShortOfTheStreamBefore shape with the reserved row (type 3), plus an optional older definition of object 7 in the oldest table. The newest table at 1828 gives `/Prev 1658 /XRefStm 1665`. Outputs:
- head: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute-mirror/out.txt
- 19122f0: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute-mirror/out19122.txt
- with the fix applied: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute-mirror/outfix.txt

Head b4ab948, `/Index [4 1 6 3]` holding rows 4, 6 and 7 (type 3):
- Reader: `Warning xref.section-unreadable @1665: ... read only in part: it holds 3 rows where its /Index and /Size declare 4; the rows it holds were kept, ...`
- Reader: `Warning xref.entry-reserved @1665: ... the row for object 7 gives it type 3. ... (7.5.8.3): the row was read so, and no older section's row stands for its object.`
- Record: `S /Prev@1665 Relocated Stream inc=True reserved=1 reservedRefused=1 rrb=True`
- `obj 7=PdfNull repaired=False`
- Rule: `F Warning xref.entry-type-reserved: ... (7.5.8.3), but the reader did not read the row so, its number refused before it was read: it reads its object as the rebuild finds it.`

Variants on head:
- Without an older object 7: same false text, and after the rebuild obj7=PdfNull.
- With two reserved rows (`[4 1 6 2 7 2]`): reservedRefused=2 and `but the reader did not read the rows so, their numbers refused before they were read`, while the reader says `the rows were read so`.

19122f0, the same files: the reader output is identical, `/Prev@1665 ... reserved=1 rrb=True`, and the rule says `..., as the reader did until it rebuilt the index by scanning the file; it now reads each object such a row numbers as the rebuild finds it.`

Control, read whole (`[4 1 6 2]`): head gives reservedRefused=0 and the "did until it rebuilt" text.

The existing test A_prev_falling_short_of_the_stream_its_xrefstm_read_reads_it_whole_and_the_naming_is_reported uses only `[4 1 6 2]` and counts the findings without checking their text, so it cannot catch this.

I checked the fix proposed below in my worktree, then reverted it. Every read-in-part variant gets reservedRefused=0, rrb=False, and the "did until it rebuilt" text. Both own-duplicate variants keep reservedRefused=1 and "did not read the row so". The classes *CrossReference*, *Hybrid*, *Reserved* and *Partial* pass: 926 tests, 0 failed.

REFUTER FIX:
In the readAgain block, at PdfFileReader.cs:3353, right after `LeaveReadingToPrevious(streamsPlace!, section, sectionsAt);`, carry what the /XRefStm's reading found onto the /Prev's record. The rows are the same, and the reader reports from that first reading:

    section.ReservedRowsRefused = streamsPlace!.ReservedRowsRefused;
    section.RowsRefusedBefore = streamsPlace.RowsRefusedBefore;

Also extend the readAgain comment to say that what the first reading refused of its own is not taken for a refusal before.

Test: add a read-in-part row to that theory, through a HybridFiles variant: `/Index [4 1 6 3]` with the reserved row, and optionally an older definition of object 7. Assert:
- the full xref.entry-type-reserved text ("as the reader did until it rebuilt the index by scanning the file; ...");
- sections[2].ReservedRowsRefused == 0;
- object 7 reads PdfNull before validation.

Add the own-duplicate mirror (`[4 1 6 2 7 1]`: a refused row for 7, then a reserved row for 7) as a control that keeps "did not read the row so". Run ADR 53's catalog over the two added lines; dropping either assignment must be killed, the second by the own-duplicate row if RowsRefusedBefore is asserted. No doc change is needed: validation-rules.md:55 and the XML doc on ReservedRowsRefused already describe the fixed behavior.

######## [7] lens sections — sections:own-duplicate-still-said-kept-beside-a-newer-refusal
reviewer severity: nit; refuter: confirmed / nit; needs_maintainer: False
location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:4540-4545 (RefusedRow arm, someRefused text)

CLAIM:
Round 3's [4] said two things: a newer section was said to have refused rows, and the dropped own duplicate was said kept ('the other rows read kept'). The fix removed the first half. When a newer section also refused another row of the section, the second half remains: the RefusedRow arm still says 'the other rows read kept but for those whose numbers a newer section refused', and the duplicate row, dropped by the section's own refusal, falls under 'kept'. The wording is identical on 19122f0, so this is not a regression, just an incomplete fix.

EVIDENCE:
Probe ZzR4SecProbe.ZzOwnDupPastNewer, case 1: PartialFiles.PastALostStream("[4 1]", "3 2\n{three} 65536 n \n{four} 00000 n \n3 1\n{three} 00000 n \ntrailer\n<< /Size 7 /Prev {oldest} >>\n"). Head: `xref.section-unreadable @438: ... the row for object 3, at offset 447, gives it generation 65536, outside 0 to 65535; that row was refused, the rows after it read, and the other rows read kept but for those whose numbers a newer section refused; its object is looked for by rebuilding the index where no row read before it indexes it, ...`. The output also shows `idx 3=none` (the sound duplicate row for 3 was not kept) and `obj 3=new three repaired=True`. 19122f0 gives the same sentence ('...where no section read before it...').

REVIEWER FIX:
In the someRefused text of the RefusedRow arm, use the shared tail: `", and the other rows read kept" + KeptButFor(section)`, which here gives 'kept but for those it refused and those whose numbers a newer section refused'. RefusedRow is always set in this arm, so the duplicate dropped under the section's own refusal is covered. Add the probe's shape as a row of A_table_that_gives_a_number_twice_and_refused_one_of_the_rows_says_no_newer_section_refused_the_other, past a lost stream declaring [4 1].

REFUTER REASON:
The finding holds as stated, and the same clause reaches one more shape. It is not a regression of round 3. The clause came with round 2's 95d8e0f, and 19122f0 has the same sentence apart from 'no section read before it'. Round 3 [4] named two falsehoods: 'a newer section refused' and 'the other rows read kept'. The decided fix ([4]: RowsRefusedBefore only for another section's refusal) removes both when no newer section refused anything in the section, and R3b implemented it as decided. When a newer section did refuse another number of the section, the RefusedRow arm's someRefused text (PdfFileReader.cs:4544-4545) still says 'the other rows read kept but for those whose numbers a newer section refused'. R3b brought KeptButFor into the Incomplete arms and ReportLostSection for [15], but not into this arm. Two kinds of row are dropped there yet fall under 'kept':
(A) the finding's shape, a later sound row for the refused number, dropped by the section's own refusal (idx 3 = none);
(E) wider than claimed: a second row the section refused itself (generation 65536). Object 5's fate is told nowhere else in the message, and idx 5 = none.
The code also falls short of its own docs. The XRefSectionUnreadable XML doc (PdfDiagnosticCodes.cs:63-64: 'The rows read were kept, but for those it refused itself, which the message says...'), diagnostics.md:112 and lazy-reading.md:163 all say the message leaves out rows the section refused itself.
Narrowing: only invalid files reach it. Three things must meet: a newer section refusing a number of the section, a row refused alone, and a second row that is a duplicate of that number or refused itself. Objects served, repairs and codes are right. In shape A the tail already says object 3 is looked for by rebuilding the index.
Severity: nit. The known residual F (two own refusals and no newer refusal: 'that row alone was refused') has the same root and was accepted as residual wording. The reverse order (D) stays true, because its sound first row is kept and serves object 3. By [15]'s precedent, shape E alone could be argued minor, since it is the same 'kept' falsehood for an own-refused row.

REFUTER EVIDENCE:
Built the branch head b4ab948 in Release (0 errors) and ran probe ZzRefOwnDupNewer (copy at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute-owndup-newer/ZzRefOwnDupNewer.cs.txt, output in .../refute-owndup-newer/out.txt). Every case uses PartialFiles.PastALostStream; the middle table is at 438.

(A) The finding's shape: index "[4 1]", table "3 2\n{three} 65536 n \n{four} 00000 n \n3 1\n{three} 00000 n \ntrailer...".
- Message: `xref.section-unreadable @438: ... the row for object 3, at offset 447, gives it generation 65536, outside 0 to 65535; that row was refused, the rows after it read, and the other rows read kept but for those whose numbers a newer section refused; its object is looked for by rebuilding the index where no row read before it indexes it, rather than taken from an older section's row.`
- Record and objects: rowsRefusedBefore=True, idx 3=none, idx 4=none, obj 3=new three, repaired=True.

(E) Wider: index "[4 1]", table "3 3\n{three} 65536 n \n{four} 00000 n \n0000000009 65536 n \n...".
- Message: the same sentence.
- Record: idx 5=none. The row for 5 was refused by the section itself, yet it falls under 'kept'.

(D) Reverse order, the sound row for 3 first: the same sentence, idx 3=Regular@385, obj 3=new three, repaired=False. True here.

(C) A duplicate with "[10 1]", and (F) two own refusals with "[10 1]": rowsRefusedBefore=False, so the message says 'that row alone was refused, the rows after it read, and its object is looked for ...'. That is round 3's fixed text, plus the known residual for F.

Source:
- `git show 19122f0:.../PdfFileReader.cs` line 4435 has the same clause, with 'where no section read before it'.
- 761ce0b has only 'that row alone was refused' (lines 4175-4176).
- `git log -S"the other rows read kept"` names only 95d8e0f.
- The tests pin the current clause at CrossReferenceChainTests.cs:2714 and :2717.

REFUTER FIX:
Not a maintainer choice. In the RefusedRow arm (PdfFileReader.cs:4544), change the someRefused text to `", and the other rows read kept" + KeptButFor(section) + "; its object is looked for by rebuilding the index where no row read before it indexes it, rather than taken from an older section's row"`. RefusedRow is always set in this arm, so the text reads 'kept but for those it refused and those whose numbers a newer section refused'. That covers shape A (the duplicate dropped under the section's own refusal) and shape E (a second own-refused row). Shape D stays true. Update the two rows at CrossReferenceChainTests.cs:2714 and :2717. Add rows past a lost stream declaring [4 1]: one with the later duplicate of 3, one with a second own-refused row (5), and the reverse order as a control. Fit them into A_table_that_refused_a_row_past_a_lost_stream_says_it_alone_was_refused_only_where_the_others_stand, which already uses PastALostStream, rather than the GivenTwice test, which has no lost stream. Assert idx 3 or idx 5 and the object served. No docs change is needed: the XML doc, diagnostics.md:112 and lazy-reading.md:163 already describe the fixed behavior. Run the ADR 53 mutants over the changed line.

######## [8] lens sections — sections:refused-numbers-dictionary-allocates-42-percent-more
reviewer severity: nit; refuter: confirmed / minor; needs_maintainer: False
location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:639 (Dictionary<int, XRefSectionRecord>? _refusedNumbers), :7267 (Add)

CLAIM:
Changing _refusedNumbers from HashSet<int> to Dictionary<int, XRefSectionRecord> makes each refused number cost 24+4 bytes instead of 12+4. It is still allocated only on the first refusal and still bounded by MaxIndexEntries, which counts it with the index. On a hostile table that refuses every row, opening now allocates 42% more than at 19122f0. At the default bound of 8,388,607 numbers, the final capacity of 11.2M slots costs about 314 MB instead of about 180 MB, and more while the table grows. The guard bounds the cost, so this is not a defect; but neither the MaxIndexEntries remarks ('40 bytes for each it is sized for') nor the threat model mention it. A per-section ordinal (Dictionary<int,int>, 16+4 bytes) would carry the same information.

EVIDENCE:
Probe ZzR4SecProbe.ZzRefusedMemory: one table whose subsection '3 400000' gives 400,000 rows '0000000010 65536 n', file 8,000,247 bytes, MaxXRefSectionLength 64 MB, GC.GetAllocatedBytesForCurrentThread around PdfDocument.Open, three runs each. Head b4ab948: allocated=52,383,712 (52,384,720 / 52,383,760). 19122f0: allocated=36,794,400 in all three runs. That is +15.6 MB, about 39 bytes more per refused row of 20 file bytes, which matches the two prime-grown arrays (701,819 slots at 28 B against 16 B, summed over the doublings).

REVIEWER FIX:
Either key the refusal to a section ordinal (`Dictionary<int, int>`, the chain counting the sections it reads) to recover a third of the difference, or keep the design and state the cost in the MaxIndexEntries remarks and the threat-model row (the refused numbers counted with the index cost about 28 bytes each beside its 40). Either way, no maintainer question is needed.

REFUTER REASON:
The finding holds. Its core figure reproduces exactly. 8ccd868 (R3b, round 3 finding [4]) changed `_refusedNumbers` from `HashSet<int>`, as on main 21a0298 and on 19122f0, to `Dictionary<int, XRefSectionRecord>` (PdfFileReader.cs:639, set at :7264, read at :7222). That makes each slot 28 bytes (a 24-byte entry plus a 4-byte bucket) instead of 16. The set is still allocated only on the first refusal and still bounded by MaxIndexEntries, so valid files pay nothing, and invariant 4 holds because the bound is checked.

Three of the finding's figures are slightly off:
- Growth follows HashHelpers' prime table. At 400,000 numbers the final capacity is 672,827 slots, not 701,819.
- At the default bound it is about 12.0M slots, not 11.2M. That costs about 336 MB against 192 MB, not 314 against 180.
- A per-section ordinal (`Dictionary<int,int>`, 20 bytes a slot) recovers two thirds of the increase, not one third.

The finding also understates how cheaply the cost is reached. Its probe raised MaxXRefSectionLength to 64 MB, which is already the default. And a cross-reference stream reaches the default index bound from a 74 KB file. Measured that way, opening allocates 283 MB more than at 19122f0, and the process's peak working set rises by about 360 MB.

On severity: the brief defines minor as including "a cost that is bounded but avoidable", and nit as wording or style. This is a cost, it is material at the default limits, and two thirds of it can be avoided with a mechanical change, so minor fits better than the reviewer's nit.

The documentation part is accurate but not new. Neither PdfReaderLimits.MaxIndexEntries nor docs/threat-model.md:106/:429 ever stated what the refused-number set costs, even at 16 bytes a slot on main. No document is made false by the change. What is new in this batch is that the set is 75% larger.

REFUTER EVIDENCE:
I rebuilt the probe as tests/AdCodicem.Pdf.Tests/ZzRefuteRefusedMemory.cs (removed afterwards; a copy is at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r4ref-ZzRefuteRefusedMemory.cs.txt). I built it Release at b4ab948 and at 19122f0 (scratch worktree), and measured with GC.GetAllocatedBytesForCurrentThread around PdfDocument.Open.

(1) The reviewer's table: subsection '3 400000' of rows '0000000010 65536 n', file 8,000,247 bytes.
- b4ab948: allocated=52,392,536 / 52,383,760 / 52,383,760.
- 19122f0: 36,805,248 / 36,794,400 / 36,794,400.
- Diagnostics are xref.section-unreadable on both.
- Control with the same rows written free: 16,005,328 on head and 16,005,320 on 19122f0, which is the index room.
- 100,000 rows: 12,459,928 against 8,837,920.

(2) The set shapes alone, filled one Add at a time as Refuse fills them:
- 400,000 numbers: HashSet<int> 20,786,856; Dictionary<int,ref> 36,376,184; Dictionary<int,int> 25,983,264. The head-vs-19122f0 delta (15,589,360) equals the Dictionary-vs-HashSet delta (15,589,328).
- 8,388,607 numbers: 377,377,992; 660,410,488; 471,722,112.

(3) The default limits, `new PdfReaderOptions()`. A cross-reference stream with /W [1 4 1], /Size 8,388,603 and /FlateDecode; rows 3 onward are type 2 naming object stream 0, so all are refused at PdfFileReader.cs:7189. File 73,596 bytes.
- b4ab948: allocated=1,196,321,008 / 1,196,208,520; retained while open 420.8 MB (the set is let go after the chain); first-round process PeakWorkingSet64 1,120,141,312 and 1,159,344,128 in two runs.
- 19122f0: allocated=913,295,576 / 913,176,040; retained 420.8 MB; peak 780,189,696 and 757,358,592.

Code checks:
- `git log -S"Dictionary<int, XRefSectionRecord>" 19122f0..b4ab948` lists only 8ccd868.
- Main and 19122f0 declare `private HashSet<int>? _refusedNumbers`.
- r3-fix-result.json says R3b "took the refuter's first option", and r3-findings [4] offered "record or ordinal".
- PdfReaderLimits.cs:150-162 mentions only the index's 40 bytes, unchanged since main.
- A grep of docs/ and src/ finds no cost stated for the refused numbers.

REFUTER FIX:
Key the refusal to a section ordinal instead of the record:
- `Dictionary<int, int> _refusedNumbers`, its value the ordinal of the refusing section.
- The chain gives each XRefSectionRecord an ordinal as it starts reading it, for example a counter or the section's place in the structure's list.
- AddFromChain sets `section.RowsRefusedBefore |= refuserOrdinal != section.Ordinal`.

This takes a slot from 28 bytes to 20 (16 on main) and recovers two thirds of the increase: 472 MB instead of 660 MB cumulative at the default bound.

In the same commit, state the cost where the bound is described:
- In the MaxIndexEntries remarks and in threat-model rows 106/429: while the chain is read, the numbers it refused take about 20 bytes each beside the index's 40, up to about twice that while the set grows, and are let go once the chain is read.
- Add a line to the status.md batch entry naming the figure.

If the ordinal change is not wanted, keeping the record is acceptable provided the docs state 28 bytes a slot. A design that costs nothing extra (two HashSets swapped and merged at each section's end) is more complex and gains little. Neither choice needs the maintainer.

######## [9] lens sections — sections:reserved-row-refused-by-own-earlier-row-untested
reviewer severity: nit; refuter: confirmed / nit; needs_maintainer: False
location: tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs (A_row_of_a_reserved_type_whose_number_was_refused_before_it_is_said_to_stand_for_nothing), CrossReferenceRuleTests.cs (A_row_of_a_reserved_type_whose_number_was_refused_before_it_is_not_said_to_have_been_read_as_null)

CLAIM:
These texts name a case no test builds: the XML doc of XRefSectionRecord.ReservedRowsRefused, the PdfDiagnosticCodes.XRefEntryReserved summary, diagnostics.md:117, validation-rules.md:55, lazy-reading.md:93-94 and the ReportReservedRows remarks all say a reserved row stands for nothing where 'a row of the stream's own for the same number' refused it. Every row of the two new theories gets its refusal from a newer section. The behavior is right, as the probe shows, but the documented case has no test (CLAUDE.md: every feature ships with its tests).

EVIDENCE:
Probe ZzR4SecProbe.ZzOwnStreamDup: stream 6 at 275 (startxref), /W [1 4 3] /Index [3 1 3 1 6 1], rows (1, three, 65536) then (7,0,0). Head: `xref.section-unreadable @275: ... the row for object 3 gives it generation 65536, ...; that row alone was refused, ...`, `xref.entry-reserved @275: ... (7.5.8.3), but its number was refused before the row was read: it stands for nothing, and its object is looked for by rebuilding the index.`, record reservedRefused=1 rowsRefusedBefore=False, idx 3=none, obj 3='new three' repaired=True. Rule: `... but the reader did not read the row so, its number refused before it was read: ...`. Reversed order: reservedRefused=0, the text 'the row was read so', idx 3=Free, obj 3=PdfNull. grep finds no such shape in tests/.

REVIEWER FIX:
Add both orders as rows of the two theories (a ReservedRowFiles builder with /W [1 4 3] and /Index [3 1 3 1 ...]), asserting ReservedRowsRefused, RowsRefusedBefore false, the reader's and the rule's full texts, and object 3.

REFUTER REASON:
The finding holds. The reader and the validator both handle this case correctly. Six texts in src/ and docs/ name it: a reserved-type row whose number the stream's own earlier row for the same number refused. No test in the unit suite builds that case.

The theories at CrossReferenceChainTests.cs:4999-5044 and CrossReferenceRuleTests.cs:2325-2350, and the relocated Fact at :5047, all build their files with ReservedRowFiles.PastARefusal. In every case the refusal comes from a newer section: the lost stream 9, or the newest table's row refused alone. The [10 1] row and the sound-row row are controls in which nothing is refused.

The fix stage chose the neutral words "its number was refused before the row was read" for this exact case (r3-fix-result.json: 'the count includes a row the stream's own earlier row for the same number refused, and for that row "a newer section" would be false'). Nothing pins that choice. If the messages were reworded to "a newer section having refused its number", every test would still pass.

The code path is the composition of two pieces that are each covered. GivenTwice covers AddFromChain's `refuser != section` branch. The newer-refusal rows cover the reserved arm's `!AddFromChain → ReservedRowsRefused++`. That explains why ADR 53's single-place mutants left no survivor here, and why this is a nit, not a minor.

This is not a regression against 19122f0 or main. The case is a gap in the tests of 8ccd868. The behavior is right.

REFUTER EVIDENCE:
I rebuilt the reproducer myself on b4ab948 (Release, 0 warnings). The probe is tests/AdCodicem.Pdf.Tests/ZzR4RefOwnReserved.cs, since removed. The older stream 5 has /W [1 4 3] and places object 3 (three). The newer stream 6 is at 414, which startxref names: /Index [3 1 3 1 6 1], /W [1 4 3], /Prev to stream 5. Its rows give object 3 the row (1, offset of (new three), 65536) and the row (7,0,0), in either order. Output is in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r4ref-ownres.txt.

Refused row first:
- `xref.section-unreadable @414: ... the row for object 3 gives it generation 65536, outside 0 to 65535; that row alone was refused, the rows after it read, and its object is looked for by rebuilding the index where no row read before it indexes it, rather than taken from an older section's row.`
- `xref.entry-reserved @414: ... (7.5.8.3), but its number was refused before the row was read: it stands for nothing, and its object is looked for by rebuilding the index.`
- Section record: section @414 reservedRefused=1, rowsRefusedBefore=False. Older section @226: rowsRefusedBefore=True.
- idx 3=none. Object 3 reads 'new three', repaired=True.
- Rule: `... but the reader did not read the row so, its number refused before it was read: it reads its object as the rebuild finds it.`

Reserved row first:
- reservedRefused=0, and the reader says `: the row was read so, and no older section's row stands for its object.`
- idx 3=Free. Object 3 reads null, repaired=False.
- Rule: `as the reader did until it rebuilt the index ...`. The validation rebuilds the index because the section was read in part.

Both outputs match the reviewer's.

To check that no test reaches this path, I instrumented the reserved arm of ReadXRefStreamRows (PdfFileReader.cs:7199) to append to a file whenever `_refusedNumbers[number] == section`. The whole unit suite then ran: 7,600 tests, 0 failed, 20 skipped, and the file was never written. The probe alone then wrote it once, so the instrumentation does fire. I reverted the instrumentation afterwards, and the worktree is clean.

Texts that name the case:
- XRefSectionRecord.cs:81-85
- PdfDiagnosticCodes.cs:122-123
- PdfFileReader.cs:4601-4603 (the ReportReservedRows remarks)
- ReservedTypeRule.cs:25-26
- diagnostics.md:117
- validation-rules.md:55
- lazy-reading.md:92-93

REFUTER FIX:
Add a builder to ReservedRowFiles, for example `OwnRefusal(bool refusedFirst)`. Its newer cross-reference stream would have /W [1 4 3] and /Index [3 1 3 1 6 1]. Its two rows for object 3 would be (1, offset of (new three), 65536) and (7, 0, 0), in the order the parameter gives. Its /Prev would name an older stream that places (three).

Add a theory, or rows to the two existing theories, covering both orders:
- In the chain test, assert ReservedRowsRefused (1, then 0), RowsRefusedBefore false on that section, the full xref.entry-reserved message, idx 3 absent or Free, and object 3 read as 'new three' with WasRepaired, or as PdfNull.
- In the rule test, assert the full xref.entry-type-reserved message for both orders.

Add a mutant run on these new rows: one that makes AddFromChain return true where `refuser == section`. These tests should kill it. No maintainer decision is needed: this is a test-only change, and no text or code changes.

######## [10] lens sections — sections:dropped-candidate-leaves-its-refusals
reviewer severity: nit; refuter: confirmed / minor; needs_maintainer: False
location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:4887 (ForgetReadThrough for a dropped candidate) with :7253-7271 (Refuse records the attempt record) and :7222-7225

CLAIM:
#320 is filed for a dropped relocation candidate leaving its rows in the index. A dropped candidate also leaves its refusals in _refusedNumbers, keyed since 8ccd868 by the dropped attempt's record. The section taken next then loses its own sound row for that number. It is flagged RowsRefusedBefore as if a newer section had refused it, though none did, and with no report of the drop: the object is only rebuilt. #320's text names rows, not refusals. Its 'room after a dropped attempt is what it was' implies the refusals, but does not say so. The behavior is the same at 19122f0, so this is not a regression.

EVIDENCE:
Probe ZzR4SecProbe.ZzDroppedCandidateRefusal. The real table at 164 places object 3 '(new three)'. A decoy at 316 is `% xref\n3 1\n{oldThree} 65536 n \njunk\n`: the word xref in a comment, Malformed, so dropped by the line test. The newest table at 413 gives `/Prev 348` (inside the decoy's 'junk'). Head: only `Repair xref.offset-adjusted @164: The cross-reference section /Prev names at offset 348 was found 184 bytes before it.`; record `S /Prev@164 Relocated Table rowsRefusedBefore=True refusedRow=-`, `idx 3=none`, `obj 3=new three repaired=True`; findings only xref.section-shifted.

REVIEWER FIX:
Do not fix in this batch: comment on #320 with this reproduction, so that its closing rolls back a dropped candidate's refusals (the _refusedNumbers entries keyed to the attempt) as well as its rows.

REFUTER REASON:
The finding holds as stated. I rebuilt it at b4ab948, at 19122f0 and at main 21a0298.

When a relocation candidate is dropped, the reader forgets only the values that candidate read through (ForgetReadThrough, src/AdCodicem.Pdf/IO/PdfFileReader.cs:4878; the finding's :4887 is off by 9). Two other things stay:
- Refuse (:7253-7270) put the numbers the dropped attempt refused into _refusedNumbers, keyed to the attempt's record (:7264).
- Refuse also set _indexIncomplete = true (:7255), unconditionally and before its early return.

AddFromChain (:7222-7225) then refuses the next section's sound row for that number, and flags RowsRefusedBefore because the refuser (the dropped attempt) is not that section. Round 3's [4] meant that flag for a refusal another section made, and the dropped attempt counts as one here. No earlier round, decision or debt covers refusals left by a dropped candidate. #320's body and its 2026-11-04 comment name rows only.

It is wider than the finding's nit:

(1) The flag reaches the text. If the section taken next also refuses a row of its own, its xref.section-unreadable report says "the other rows read kept but for those whose numbers a newer section refused". No newer section refused anything. Without the decoy the same file says "that row alone was refused". This is a misleading text, which the brief rates minor. It is batch wording, present since R2b and still there after 8ccd868. Main gives no read-in-part report here at all.

(2) If the dropped attempt refuses a number a newer section already indexes, Refuse returns early but leaves _indexIncomplete set. Validation's SettleIndex then rebuilds a file whose chain index lacks nothing: xref.rebuilt and object.redefined are reported and WasRepaired becomes True. The control without the decoy does not rebuild.

(3) #320's 2026-11-04 comment says only an object giving /W can still be dropped. That is stale. Since round 2's [2] and round 3's [1][8], a table whose xref keyword sits in a comment or a string on its line is dropped too. Such a table still leaves rows: at head, decoy rows with generation 0 serve "old three" silently. That is #320's original harm, through a table candidate.

Not a regression. 19122f0 gives the same output in every shape. On main 21a0298, with the newest table moved out of the search window (main's window reaches it in the reviewer's own layout, so that file loops there), the index outcome is the same: object 3 lost to a rebuild, and the spurious rebuild at validation. Only the false clause is the batch's own.

The object served is still the right one, "new three", through the rebuild, and the load reports xref.rebuilt. So this is not major. The wording and the state behind it come from the dropped attempt, whose rollback design (a rollback mark in PdfXRefTable, or staged rows) is #320's open choice under Q12. Commenting on #320 follows that decision and needs no new question.

REFUTER EVIDENCE:
Probe: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r4ref-drop/ZzRefDropRefusal.cs.txt. Outputs, one file per checkout, in the same folder: out-head.txt and out.txt (head), out-19122.txt, out-main.txt.

The file is the reviewer's shape:
- real table at 164;
- decoy at 316, `% xref\n3 1\n{oldThree:D10} 65536 n \njunk\n`;
- newest table at 413, giving /Prev 348.

Every variant has a control, in which the decoy is replaced by a `%zzz...` comment of the same length.

A, the reviewer's shape, head:
- `S /Prev@164 Relocated Table rowsRefusedBefore=True refusedRow=-`, `idx 3=none`
- `obj 3=new three repaired=True`
- diagnostics: `Repair xref.offset-adjusted @164 ... 184 bytes before it.`, `Repair xref.rebuilt`, `Information object.redefined`
- validator: only `Warning xref.section-shifted`

A0, its control: `idx 3=Regular`, `obj 3=new three repaired=False`, only xref.offset-adjusted.

C, the decoy plus the real table refusing its own row 2 (generation 65536), head:
- `Warning xref.section-unreadable @164: ... was read only in part: the row for object 2, at offset 213, gives it generation 65536, outside 0 to 65535; that row was refused, the rows after it read, and the other rows read kept but for those whose numbers a newer section refused; ...`

C0, its control: `... that row alone was refused, the rows after it read, and its object is looked for by rebuilding the index ...`

D, the decoy refusing 3 while the newest table indexes 3 itself, head:
- `idx 2=Regular idx 3=Regular`, `obj 3=new three repaired=False`
- after Validate: `repaired=True`, with `Repair xref.rebuilt` and `Information object.redefined`

D0, its control: after Validate `repaired=False`, no rebuild.

B, decoy rows with generation 00000, head: `idx 3=Regular`, `obj 3=old three repaired=False`; diagnostics only xref.offset-adjusted, validator only section-shifted.

19122f0: A, C and D give the same output, with "no section read before it" in place of "no row read before it" in the tail.

main 21a0298, in the reviewer's layout: the search window reaches the newest table at 413, so both A and A0 loop (xref.chain-cycle, then a rebuild).

main 21a0298, with the newest table moved to 1054, out of the window:
- E (A's shape): `idx 3=none`, `obj 3=new three repaired=True`, with xref.rebuilt.
- E0 (its control): `idx 3=Regular`, `repaired=False`.
- G (D's shape): after Validate `repaired=True`.
- G0 (its control): `repaired=False`.
- F (C's shape): no xref.section-unreadable at all. The false clause is batch wording.

REFUTER FIX:
No code change in this batch. Comment on #320 with these reproductions, so that its closing rolls back three things a dropped candidate leaves:
- its rows (as filed);
- its refusals, the _refusedNumbers entries keyed to the attempt's record, which also give back their room under MaxIndexEntries;
- the _indexIncomplete flag its Refuse set.

The comment should add a test for each:
- object 3 read from the table at 164 with no rebuild;
- no "a newer section refused" clause when the section taken next refuses a row of its own;
- no rebuild at validation when the refused number was already indexed.

The comment should also correct the 2026-11-04 comment. Since round 2's [2], a table whose xref keyword lies in a comment or a string on its line is dropped as well, and still leaves rows: the generation-0 decoy serves "old three" silently at head.

If the maintainer wants the misleading clause gone before #320, a narrow fix is possible:
- on a dropped candidate, remove the _refusedNumbers entries the attempt added, recorded on the attempt and bounded by its rows;
- restore _indexIncomplete to its value before the attempt.

That fix settles part of #320's design ahead of it, so it is a maintainer choice. The default is the comment only.

######## [11] lens headers — headers:pending-short-read-crash-and-reread
reviewer severity: major; refuter: confirmed / major; needs_maintainer: False
location: src/AdCodicem.Pdf/IO/PendingEndStream.cs:151 and :180 (comment at :92-93); the window loop at src/AdCodicem.Pdf/IO/PdfFileReader.cs:2562

CLAIM:
ea348ce's waiting keyword breaks on a PdfFileSource whose Read returns fewer bytes than asked before the end of the file. The public documentation of Read allows this (#52), and the reader's threat model trusts a caller's own source. Two defects are new against 19122f0 and main. (a) Decide sets `_readTo = at + bytes.Length` after every read. A read that ends before the one before it therefore moves `_readTo` back, so the next read is processed again from there. A token can then be taken twice, and the keyword ends the data where a whole read stops at the header. The comment at :92-93 says such a read 'adds nothing', but it moves the mark back. (b) A read of 10 bytes or fewer makes the next read start one byte on (`next = position + 1`). An `obj` token whose first bytes the earlier reads held then ends in a read that starts after the token. TokenEnds calls `text.HeaderAt((int)(_tokenStart - at))` with a negative index, and HeaderText.ByteAt throws IndexOutOfRangeException, which escapes GetObject. The sources that ship with the library cannot reach either path, and neither can CutShortSource: their reads end only at the end of the file or at the cut, and nothing is read past it.

EVIDENCE:
Probe ZzHdrR4Tests, saved at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r4h/probes/ZzHdrR4Tests.cs.

The file: object 5 is '<< /Length 9999999 >>' stream with 20,000 'A' bytes, then '\nendobj\n12 % endstream\n0 obj\n(twelve)\nendobj'. Object 6 is an 8,002-byte string. The table places objects 1, 2, 5 and 6. Short counts apply only to reads that start inside the data. The test calls GetObject(5).

On b4ab948:
- Memory source: 'length 20000; endobjMissing False; stream.truncated: ... before the next object, at 20171: its object's endobj ends it, after 20000 bytes.'
- (a) The search window at 8308 gives 11,880 bytes, ending just past '0 '. The next read, at 20178, gives 4 bytes. Every other read is whole. Result: 'length 20013; endobjMissing True; stream.length-invalid: The stream declared 9999999 bytes but ended after 20013.'
- (b) Reads at 20180 through 20188 give 10 down to 2 bytes, each ending just past 'ob'. The read at 20189 is whole. Result: 'THREW IndexOutOfRangeException: Index was outside the bounds of the array. at HeaderText.ByteAt (HeaderText.cs:488) at HeaderText.TryFindStart (:281) at HeaderText.HeaderAt (:254) at PendingEndStream.TokenEnds (:180) at PendingEndStream.Decide (:142) at StreamEnds.SearchStretch (:131)'.

On 19122f0 (worktree build), the same probe gives 'length 20013; endobjMissing True; stream.length-invalid ... ended after 20013.' for the memory source and for both short-count sources, with no exception.

With src patched to `_readTo = Math.Max(_readTo, at + bytes.Length)` and HeaderAt called only when `_tokenStart >= at`: (a) gives 20000, as the memory source does. (b) gives 20013 (the keyword is taken), with no exception.

REVIEWER FIX:
In Decide, keep the furthest point reached: `_readTo = Math.Max(_readTo, at + bytes.Length)`. In TokenEnds, call HeaderAt only when `_tokenStart >= at`. Otherwise take the keyword, as a search that can read no further does, or have the window loop start the next read no later than a pending token's start. Correct the comment at :92-93. Add a test through a source that returns short counts (the PartialSource in the probe). Alternatively, pay #52 now: GetWindow loops until the buffer is full or Read returns 0, which removes both paths. In any case, comment on #52 that a short count can now throw from the stream-end search. If the code is left as it is, record ea348ce's body ('where the reads part the data changes nothing') under 'Commit bodies not rewritten'.

REFUTER REASON:
Both defects reproduce on b4ab948, and neither appears on 19122f0. They come only through a caller's own PdfFileSource whose Read returns short counts. Read's public documentation allows that, and the threat model trusts such a source. #52 lists short counts as giving degraded reads, not as throwing.

(b) The crash is easier to reach than the finding says. Hand-picked short counts are not needed: a source that never reads past the end of the 512-, 1,024- or 4,096-byte block a read starts in does it. Such a source opens the file, and GetObject(5) throws IndexOutOfRangeException when the `obj` of the pending header straddles a block boundary. Block-limited reads like these are what a custom source over a block store may do. The file's author can place the `obj` across a 4,096-byte boundary.

(a) The finding's own reproducer gives 20,013 bytes, the same as 19122f0, so as built it shows only that the fix does not hold under short reads. The same mechanism also gives a new wrong result. Moving `_readTo` back re-reads a comment as tokens, so a `7 0 obj` inside a comment after the keyword is taken as a header. The data then runs past the endstream keyword, and the report names a "next object" whose position is inside a comment.

The comment at PendingEndStream.cs:92-93 is false: such a read moves `_readTo` back, so it does not add nothing. The remarks' "does not depend on where the reads part the data" and ea348ce's body ("where the reads part the data changes nothing") hold only for whole reads.

A crash would rank as a blocker in the brief's table. I keep it at major because only a trusted caller source with short counts reaches it (open debt #52, scheduled for M03). The library's own sources cannot reach it, and neither can CutShortSource: its reads all end at the cut, so `_readTo` never moves back.

REFUTER EVIDENCE:
The probe is the finding's ZzHdrR4Tests plus two cases, saved at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/ref-hdr-short/ZzRefShortTests.cs. It was built and run on origin head b4ab948 and on a 19122f0 worktree.

**The finding's two reproducers**
- On b4ab948:
  - memory source: "length 20000; endobjMissing False; stream.truncated ... at 20171: its object's endobj ends it, after 20000 bytes."
  - (a), a read at 20178 that gives 4 of the 30 bytes asked: "length 20013; endobjMissing True; stream.length-invalid ... ended after 20013."
  - (b), reads at 20180 to 20188 that give 10 down to 2 bytes: "THREW IndexOutOfRangeException at HeaderText.ByteAt (HeaderText.cs:488) <- TryFindStart (:281) <- HeaderAt (:254) <- PendingEndStream.TokenEnds (:180) <- Decide (:142) <- StreamEnds.SearchStretch (:131)".
- On 19122f0, memory and both short-count sources give "length 20013; endobjMissing True; stream.length-invalid ... ended after 20013." No exception.

**New: a source whose reads stop at the end of the block they start in**
- The read is `Math.Min(asked, chunk - offset % chunk)`, with chunk 512, 1,024 and 4,096. Filler is set so the `o` of `obj` lands at 20478.
- On b4ab948 the reads go "read at 20470 asked 8251 gave 10 ... read at 20479 gave 1, read at 20480 gave 4096", then the same IndexOutOfRangeException for all three chunk sizes. 61 of the 62 reads were short at chunk 4,096, and the file still opened.
- On 19122f0: "chunked: length 20303; endobjMissing True; repaired True; stream.length-invalid ... ended after 20303." No exception.

**New: a header taken from a comment after the keyword ((a) gives a new wrong result)**
- The bytes are `...AAAA\nendobj\n12 % endstream\n% 7 0 obj\n(x)\n(twelve)\nendobj`. One window ends just after the comment line, at 20196. The next read, at 20186, gives 4 of 30 bytes, and the read after it is whole.
- On b4ab948:
  - memory source: "length 20013; endobjMissing True; stream.length-invalid ... ended after 20013".
  - short-count source: "length 20025; endobjMissing False; stream.length-invalid: The stream declared 9999999 bytes, and no endstream follows its data before the next object, at 20188; the 20025 bytes up to that object are taken as its data." Position 20188 is the `7` inside the comment.
- On 19122f0 both sources give 20013.

**The proposed patch, applied temporarily**
- The patch: `_readTo = Math.Max(_readTo, at + bytes.Length)`, and `HeaderAt` called only when `_tokenStart >= at`.
- (a) gives 20000 and the comment case 20013, both matching the memory source.
- (b) and the block-limited source give 20013 and 20303 (the keyword is taken, as on 19122f0), with no exception.
- StreamEndsTests, StreamLengthTests, WindowEdgeTests and PropertyTests: 381 total, 379 passed, 2 skipped, 0 failed.

**Earlier rounds**
decisions.md and r1/r2/r3-findings.md settle nothing about short counts in this search.

REFUTER FIX:
In PendingEndStream.Decide, keep the furthest point reached: `_readTo = Math.Max(_readTo, at + bytes.Length)`. In TokenEnds, call `text.HeaderAt` only when `_tokenStart >= at`. Otherwise take the keyword: the obj's start is no longer in the read, and the header rule cannot read it back from carried runs.

Rewrite the comment at :92-93 to say a read that ends before the one before it leaves `_readTo` where it was. Narrow the remarks' "does not depend on where the reads part the data" to whole reads, citing #52.

Add two tests through a short-count source:
- The block-limited source with `obj` across a 4,096-byte boundary. It throws on b4ab948, and it kills the `_tokenStart >= at` mutant.
- The finding's 4-byte read, or the `% 7 0 obj` comment case. Each gives a result that differs from the memory source on b4ab948, and each kills the dropped-Max mutant.

Comment on #52: a short count no longer only cuts reads short. On b4ab948 it can throw from the stream-end search, and after this fix it can still change that search's outcome (the keyword taken where whole reads find the header). The alternative is to pay #52 now (GetWindow loops until the buffer is full or Read returns 0), which removes both paths. That is a scheduling choice, since #52 is filed under M03, not a requirement of this fix.

Record ea348ce's body ("where the reads part the data changes nothing") under 'Commit bodies not rewritten' as true for whole reads only.

######## [12] lens headers — headers:placed-check-places-nothing
reviewer severity: minor; refuter: confirmed / minor; needs_maintainer: False
location: tests/AdCodicem.Pdf.Tests/WindowEdgeTests.cs:972-977 (assertions at :754-755; the test's comment at :714-719; the helper's XML doc above :959)

CLAIM:
HeaderEnding(placed: true) sets rows[12], but it writes the table with HandWrittenFile.Table(7, rows, "/Size 7 ..."), which writes rows only for numbers 0 to 6. Object 12 is never placed. `HeaderEnding(header, 100, null, placed: true).Should().Be(HeaderEnding(header, 100, null))`, and the same check at 20,000, therefore compare two identical files. The test's comment and the helper's doc say it checks that the data ends at the same place whether the index places the header's object or not. R3b's deviation note says round 3's [9] property is held there. It is not held.

EVIDENCE:
(1) I added a check after line 977: if placed, open the file and throw '12 not placed' unless GetObject(12) is a PdfString. The theory An_endstream_a_comment_between_a_header_s_tokens_holds_ends_no_data_wherever_the_reads_part_the_header then throws 'System.InvalidOperationException : 12 not placed' in every row.
(2) With Table(placed ? 13 : 7, ...) and the matching /Size, all 10 rows pass: the claim holds once it is actually checked.
(3) Probe ZzPlacedReallyPlaces, saved at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r4h/probes/ZzHdrR4SweepTests.cs, with the text '\nendobj\n12 % c\n0 % 5 0 obj\nobj':
- not placed: 'past 19; stream.length-invalid: the L bytes up to that object are taken as its data'
- the helper's Table(7) placement: 'past 19', identical
- Table(13) placement: 'past 0; stream.truncated: its object's endobj ends it'
A real placement changes the answer where the helper's placement does not. WindowEdgeTests.cs was restored afterwards.

REVIEWER FIX:
In HeaderEnding, write `file.Table(placed ? 13 : 7, rows, placed ? "/Size 13 /Root 1 0 R" : "/Size 7 /Root 1 0 R")`. In the placed case, assert that object 12 reads as (twelve), so the check cannot pass on a file that places nothing.

REFUTER REASON:
The finding holds as stated. HandWrittenFile.Table(size, rows, trailer) (tests/AdCodicem.Pdf.Tests/HandWrittenFile.cs:49-69) writes rows only for numbers 0 to size-1. HeaderEnding (tests/AdCodicem.Pdf.Tests/WindowEdgeTests.cs:959-990) sets rows[12] when placed is true but calls file.Table(7, rows, "/Size 7 /Root 1 0 R"), so row 12 is dropped and the placed file is byte-identical to the unplaced one. The two checks at :754-755 therefore compare a file with itself and can never fail.

Three records say the opposite:
- the test's comment at :714-719: "whether the index places the header's object or not";
- the helper's XML doc above :959: "unless placed says the index places object 12";
- R3b's record in r3-fix-result.json: tests_added says "plus a check that the index placing object 12 gives the same result", and the deviation note says "[9] property ... is checked at document level inside the WindowEdge theory, at two fillers".

Round 3's [9] fix code is not at fault. With a real placement, all 10 theory rows give the same length, code, endobj judgment and rule as the unplaced file, so the property the refuter asked for does hold. Only the test that claims to hold it is vacuous. Commit ea348ce's body describes the code's behavior, not the test, and it is true. No commit body needs a status.md note.

The reviewer's extra row, '\nendobj\n12 % c\n0 % 5 0 obj\nobj', does differ with a real placement: F+19 length-invalid unplaced, F+0 truncated placed. That comes from the documented #171 gap: a '% 4 0 obj' on its obj's own line, "at which the search for a lost endstream stops", threat-model.md:442-444. It is not a new defect, but that row must not be added to the fixed property.

REFUTER EVIDENCE:
I rebuilt the probe myself on origin/ccr-6d6c0ee6-dqm4lm-188 at b4ab948 (Release, 0 warnings). It is saved at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r4ref-placed-probe.cs.txt, with its output in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r4ref-placed-out.txt. It builds HeaderEnding's file three ways: not placed with Table(7); placed with Table(7), as the helper does; and placed with Table(13) and /Size 13. It ran at F=100 and F=20,000 for all 10 theory rows plus the reviewer's extra row.

Results:
- In every case, helperPlacedBytesIdentical=True: the helper's 'placed' file is byte-for-byte the unplaced one, and GetObject(12) is null in both.
- With Table(13), GetObject(12) = "twelve". Every one of the 10 theory rows still matches the unplaced result. For example, '\nendobj\n12 % endstream\n0 obj' gives len=F+0, stream.truncated, endobjMissing=False and rule=False in all three files. '\n12 % endstream\n0 obj' gives F+1, stream.length-invalid, True and True in all three.
- Extra row '\nendobj\n12 % c\n0 % 5 0 obj\nobj': not placed and Table(7)-placed both give len=F+19, stream.length-invalid. Table(13)-placed gives len=F+0, stream.truncated. This is the #171 comment-on-obj-line gap.

Fix check: in WindowEdgeTests.HeaderEnding I temporarily used Table(placed ? 13 : 7, ...) with the matching /Size, and threw unless GetObject(12) reads "twelve" when placed. An_endstream_a_comment_between_a_header_s_tokens_holds_ends_no_data_wherever_the_reads_part_the_header then passed 10/10. The file was restored afterwards with git checkout and the probe deleted; the worktree is clean.

REFUTER FIX:
In WindowEdgeTests.HeaderEnding, write `file.Table(placed ? 13 : 7, rows, placed ? "/Size 13 /Root 1 0 R" : "/Size 7 /Root 1 0 R")`. When placed, assert that object 12 reads as (twelve), for example `document.GetObject(new PdfObjectId(12)).Should()...` or a check in the theory, so the comparison cannot pass on a file that places nothing. It then holds for all 10 rows, as verified.

Do not add the '% 5 0 obj'-inside-the-comment row to this property: it differs by index through the documented #171 gap.

Optional hardening: HandWrittenFile.Table could refuse a row whose number is not below size, so a dropped row cannot pass silently. Check its other callers first.

Record in decisions.md that R3b's 'placed' check was vacuous until this fix. No commit body is false.

######## [13] lens headers — headers:pending-cost-past-documented-max
reviewer severity: minor; refuter: confirmed / minor; needs_maintainer: False
location: src/AdCodicem.Pdf/IO/PendingEndStream.cs:100-112; docs/threat-model.md:529; benchmarks/AdCodicem.Pdf.Benchmarks/StreamEndSearchBenchmarks.cs:21

CLAIM:
While a keyword waits, Decide makes two vectorized calls per comment line: IndexOfAny for the end of the line, then IndexOfAnyExcept for the white space. This comes on top of HeaderText's own lexing of the same bytes. On a run of '%\n' lines after a waiting keyword, the search costs about 2.2 times what the same lines cost without the keyword. That is the most of any shape. It exceeds the threat model's 'about 10 ns a byte at most' (whose largest shape is nested strings), and contradicts the benchmark remark that comment lines are 'the most the search costs'. No benchmark shape exercises the waiting keyword. The cost is linear, allocates nothing, and the amount read stays bounded as documented. So this is a gap in the documentation and the benchmark, plus a constant factor that can be avoided.

EVIDENCE:
I added four shapes (uncommitted, reverted since) to StreamEndSearchBenchmarks and ran ShortRun on b4ab948 and 19122f0.
- b4ab948: comment-lines 40.6 ms, nested-strings 61.2 ms, pending-comment-lines ('12 % endstream\n' then 4 MB of '%\n') 87.7 ms, pending-white-space 10.5 ms, pending-one-comment 0.45 ms, pending-many 5.6 us.
- 19122f0: comment-lines 35.5 ms, nested-strings 61.8 ms, pending-comment-lines 3.4 ms (the search stops at the keyword).
- The other shapes are equal within noise, for example one-byte-tokens 53.7 vs 53.8 ms and digits 20.5 vs 24.2 ms. ObjectScanBenchmarks is unchanged: comment-headers 51.4 vs 73.1 ms, sound 3.7 vs 4.2 ms (noisy).
- Document probe ZzManyPendingStreams through StrictCountingSource (scratchpad r4h/probes/ZzHdrR4CostTests.cs): 20 streams, each '12 % endstream' then 400 KB of '%\n'. b4ab948: the searches read 16,955,519 bytes in 176-186 ms once warm (1,318 ms cold). 19122f0: 9,115,750 bytes in 15 ms.
- With a scalar test of the first byte in the Comment arm (an end of line) and the Space arm (a '%') before the vectorized calls, pending-comment-lines drops to 41.5 ms, against 37.4 ms for comment-lines in the same run. pending-white-space stays at 9.8 ms.

REVIEWER FIX:
Add the two scalar fast paths at the top of the Comment and Space arms of Decide. Add pending shapes to StreamEndSearchBenchmarks, at least comment lines and white space after '12 % endstream'. Update the figure in threat-model row 529 and the benchmark's 'most the search costs' remark to the measured worst case.

REFUTER REASON:
The finding holds on b4ab948. One point of mechanism needs correcting, and it changes nothing in the conclusion. While a keyword waits, StreamEnds.SearchStretch (src/AdCodicem.Pdf/IO/StreamEnds.cs:129-132) calls only PendingEndStream.Decide: no IndexOf for endstream and no FindHeader. The two vectorized calls Decide makes per comment line therefore do not add to HeaderText's lexing. They add to the per-window follow-through that every search does anyway: EndObjTail.Read through held.ReadTo/tail.Read (src/AdCodicem.Pdf/IO/EndObjTail.cs:115-143), which already makes IndexOfAnyExcept(whitespace), IndexOfAny(EOL) and IndexOf(endobj) per comment line. That follow-through is what the comment-lines shape costs in the first place. On '%\n' lines, Decide adds two more calls per two bytes, and the search costs 2.4 times comment-lines and 1.5 times nested-strings, the largest shape the benchmark holds. So three statements are now false:
- the benchmark remark (StreamEndSearchBenchmarks.cs:19-21: a run of comment lines etc. 'is the most the search costs');
- threat-model row 529's 'about 10 ns a byte at most — 4 MB of strings opened one inside the other cost 44 ms'. Scaled from that reference, this shape costs about 66 ms, or about 16 ns a byte;
- the coverage of the search's costs: no benchmark shape puts an endstream in a comment, so the waiting path added by ea348ce has no shape.

The cost is linear and allocates nothing beyond about 0.4 KB a document. The bytes read stay within what the search would read without the keyword, as documented and as StreamLengthTests holds. It is therefore not a DoS. It is a bounded, avoidable constant factor plus a text that misstates the worst case. That is minor under the brief. On 19122f0 the search stopped at the keyword, which was round 3's [9] defect. Having to read on is inherent to that fix; only the factor can be avoided. This is not a design choice for the maintainer.

REFUTER EVIDENCE:
I rebuilt the reproducer on origin/ccr-6d6c0ee6-dqm4lm-188 at b4ab948 in my worktree. I added two shapes to StreamEndSearchBenchmarks, uncommitted and reverted since:
- "pending-comment-lines" = "12 % endstream\n" + Repeat("%\n");
- "pending-white-space" = "12 % endstream\n" + new string(' ', Size).

I built Release and ran BenchmarkDotNet. The logs are in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r4ref-pend/.

ShortRun, all 12 shapes (head.log):
| Shape | Mean | Allocated |
|---|---|---|
| binary | 468.1 us | 6.95 KB |
| comment-lines | 37,949.9 us | |
| digits | 22,594.6 us | |
| hex-string | 681.7 us | |
| nested-strings | 61,369.9 us | |
| one-byte-tokens | 52,694.6 us | |
| one-token | 763.3 us | |
| open-string | 690.0 us | |
| pending-comment-lines | 90,379.1 us | 7.37 KB |
| pending-white-space | 9,393.5 us | 7.34 KB |
| text-lines | 631.6 us | |
| white-space | 7,654.2 us | 6.95 KB |

Tighter run, 5 warmups and 15 iterations, two shapes (head2.log): comment-lines 38.40 ms ± 2.0, pending-comment-lines 91.96 ms ± 6.6. That is 2.4x.

With the reviewer's scalar fast paths applied to Decide (fastpath.diff), same settings (fast2.log): comment-lines 37.53 ms ± 1.4, pending-comment-lines 45.12 ms ± 2.2. That is 1.2x, below nested-strings at 61-63 ms. pending-white-space was unchanged at 9.3 ms.

The fast paths are:
- in the Comment arm, if bytes[index] is '\r' or '\n': index++, Space;
- in the Space arm, if bytes[index] == '%': index++, Comment.

Code read:
- the Params list holds no shape whose endstream lies in a comment;
- the StreamLengthTests test for the waiting keyword (StreamLengthTests.cs:1768) uses 1,024-byte comment lines and holds bytes and allocation, not time;
- the r3-fix-result.json measurements cover bytes read and allocation only.

I did not rerun the reviewer's document probe or the 19122f0 build. The 19122f0 difference is the round 3 [9] fix itself, and it is not needed for this finding.

REFUTER FIX:
1. In PendingEndStream.Decide (src/AdCodicem.Pdf/IO/PendingEndStream.cs:100-112), add the two scalar fast paths before the vectorized calls:
   - Comment arm: if bytes[index] is '\r' or '\n', index++ and _mode = Space; break.
   - Space arm: if bytes[index] == '%', index++ and _mode = Comment; break.

   For ADR 53, dropping either `if` is an equivalent mutant (cost only). Negating either changes the lexing and is killable by the existing WindowEdge/StreamLength rows.

2. Add two shapes to StreamEndSearchBenchmarks: comment lines after "12 % endstream\n", and white space after it. Name them for what they hold, not by measurement keys. Extend the Shape summary to match.

3. Bring two texts in line with the measured worst case:
   - Rewrite the remark at StreamEndSearchBenchmarks.cs:19-21 so it no longer says comment lines are 'the most the search costs', or so it names the waiting keyword's comment lines with its measured factor.
   - In docs/threat-model.md row 529, re-measure the 'about 10 ns a byte at most' figure and its examples on one machine with the new shapes. With the fast paths, nested strings stay the maximum and the clause can stay as is, plus a figure for the waiting keyword. Without them, the maximum becomes the waiting keyword's comment lines at about 22 ns a byte on this machine, about 16 ns at the row's reference.

Optional: EndObjTail.Read has the same per-line vectorized calls and would gain from the same fast paths. That is the comment-lines baseline, outside this finding.

######## [14] lens headers — headers:same-place-whether-placed-overclaims
reviewer severity: nit; refuter: narrowed / nit; needs_maintainer: False
location: docs/website/docs/concepts/diagnostics.md:67-68 (with :53-54 'Comments may lie between the tokens, as a load reads them')

CLAIM:
Round 3 added the sentence 'The data so ends at the same place whether the index places the header's object or not.' It is false where the comment holding the endstream also holds, before the endstream, text that the rule takes for a header. This is #171's documented gap: the line of the obj keyword is read by its digits, whatever a comment there holds. It is not a regression, but the new sentence is unconditional.

EVIDENCE:
Probe ZzPlacedReallyPlaces, with the text '\nendobj\n12 % 5 0 obj endstream\n0 obj', at fillers of 100 and 20,000:
- not placed: 'past 13; stream.length-invalid: the L bytes up to that object are taken as its data'
- object 12 really placed at its header (Table(13)): 'past 0; stream.truncated: its object's endobj ends it'
19122f0 also gives past 13 when 12 is not placed.

REVIEWER FIX:
Qualify the sentence, for example 'but where that comment holds, before the endstream, text the rule takes for a header ([#171])', or drop it. The threat model's #171 entry already names the gap.

REFUTER REASON:
The finding reproduces on b4ab948. The sentence round 3 added at docs/website/docs/concepts/diagnostics.md:67-68, "The data so ends at the same place whether the index places the header's object or not", is false when a comment between the header's tokens holds, before the endstream, text the rule takes for a header. Example: `5 0 obj` in `12 % 5 0 obj endstream` / `0 obj`. In that case the data ends 13 bytes into the text when no row places object 12, and at its endobj (0 bytes) when a row places 12 at its header.

Narrowed in three ways:
(1) The cause is only #171's known gap, not the waiting endstream that round 3 added. The obj keyword's line is read by its digits whatever a comment there holds, so `5 0 obj` inside the comment is taken for a header, and the search stops there before the 12 header that starts earlier. The same split appears with no endstream at all (`12 % 5 0 obj` / `0 obj`). It appears on 19122f0 too, with the same outputs. So it is not a regression; only the sentence is new.
(2) Every row of round 3's own theory (An_endstream_a_comment_between_a_header_s_tokens_holds_ends_no_data_...) gives the same place with object 12 really placed. The sentence is true for the inputs round 3 meant.
(3) The threat model's #171 entry (row 432) names the gap: "The line of the obj keyword itself is read by its digits, whatever a comment there holds". R1 settled the gap by commenting on #171, not by filing a new debt. But the user concept page names #171 nowhere (0 hits). Its header paragraph (:53-55) says only "Comments may lie between the tokens, as a load reads them", so a reader of the page has nothing that qualifies the new unconditional sentence.

Severity nit: only the wording overclaims, on a contrived input inside a filed gap, and the code's behavior is deterministic and unchanged since 19122f0. No maintainer decision needed.

REFUTER EVIDENCE:
The probe is my own, built on b4ab948 and on a 19122f0 worktree. Its source is saved at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/ZzSamePlaceRefuteTests.cs.txt. It uses WindowEdgeTests.HeaderEnding's layout (LostEndPrefix + filler 'A's + header + "\n(twelve)\nendobj\n", object 6, classic table). 'none' means no row 12. 'placed13' means Table(13) with rows[12] at the header's "12"; GetObject(12) returns "twelve" there.

b4ab948, at filler 100 and at filler 20,000 (identical):
- `\nendobj\n12 % 5 0 obj endstream\n0 obj`, none: past 13, "stream.length-invalid: ... the 113 bytes up to that object are taken as its data."
- Same text, placed13: past 0, "stream.truncated: ... its object's endobj ends it, after 100 bytes."
- `\nendobj\n12 % x 5 0 obj endstream\n0 obj`: 15 vs 0.
- `\nendobj\n12 0 % 5 0 obj endstream\nobj`: 15 vs 0.
- `\n12 % 5 0 obj endstream\n0 obj`: 6 vs 1.
- `\nendobj\n12 % 5 0 obj\n0 obj` (no endstream at all): 13 vs 0.
- Controls with no header-like text in the comment give the same place both ways: `12 % 5 0 R endstream`, `12 % 7 obj endstream`, and all 10 rows of round 3's theory, for example `\nendobj\n12 % endstream\n0 obj` gives 0/0 truncated and `\n12 % endstream\n0 obj` gives 1/1.

19122f0: the `5 0 obj` rows give exactly the same numbers (13/0, 15/0, 6/1, 13/0). Round 3's theory rows differ there (for example 13 vs 0), which is what round 3 fixed.

Side note, not this finding's claim: the theory's own check `HeaderEnding(header, …, placed: true).Should().Be(HeaderEnding(header, …))` (WindowEdgeTests.cs, end of the theory) never places object 12. HeaderEnding writes `file.Table(7, rows, "/Size 7 …")`, and HandWrittenFile.Table writes only rows 0..size-1, so rows[12] is dropped; my 'placed7' mode gave twelve=null and the same output as 'none'. The sentence was checked by a comparison of two unplaced runs. With Table(13), the theory's rows still pass.

REFUTER FIX:
Docs only.

1. In concepts/diagnostics.md, name the gap once in the header paragraph (:53-55). For example, after "as the scan that rebuilds a damaged index reads it", add: "— but for the line of the `obj` keyword, which is read by its digits whatever a comment there holds, as the scan reads it ([#171](https://github.com/AdCodicem/AdCodicem.Pdf/issues/171))".
2. Qualify the sentence at :67-68, for example: "The data so ends at the same place whether the index places the header's object or not, but where a comment between its tokens holds, before the keyword, text the rule takes for a header (#171, above)." Dropping the sentence is also acceptable.

Separately, so that the sentence is really held by a test: make HeaderEnding write `Table(13, rows, "/Size 13 /Root 1 0 R")` when `placed` is set, so that row 12 exists. This may be another reviewer's finding; dedupe against it.

######## [15] lens headers — headers:test-comment-unwrapped
reviewer severity: nit; refuter: confirmed / nit; needs_maintainer: False
location: tests/AdCodicem.Pdf.Tests/WindowEdgeTests.cs:715 and :776-777

CLAIM:
Two comments ea348ce added were left unwrapped. The comment above the waiting-keyword theory has one 188-column line among lines wrapped at 120. The control theory's comment breaks 'An endstream in a string, or one that' onto a line of its own.

EVIDENCE:
`awk 'NR==715{print length}'` prints 188. Line 776 reads '// object. An endstream in a string, or one that', and line 777 continues '// is a token, is in no comment, ...'.

REVIEWER FIX:
Rewrap both comments at the file's width.

REFUTER REASON:
The finding holds as stated. At b4ab948, line 715 of tests/AdCodicem.Pdf.Tests/WindowEdgeTests.cs is 188 columns long, in a comment whose other lines wrap at about 120 to 126. Line 776 breaks after 'or one that' at 49 columns, and the sentence carries on at line 777. git blame puts both comments on ea348ce, the round 3 fix 'end no stream's data at an endstream a comment between a header's tokens holds'. Neither is in 19122f0. The build does not catch this: .editorconfig sets no max_line_length, and other comment lines in the tests also run past 130 columns. So it is a style nit with no effect on behavior. What makes it worth fixing is that both lines look like editing leftovers (two lines run together at 715, a stray break at 776), not a deliberate layout.

REFUTER EVIDENCE:
On origin/ccr-6d6c0ee6-dqm4lm-188 (b4ab948), awk printed the line numbers and lengths of comment lines over 126 columns in WindowEdgeTests.cs: '228: 127, 299: 127, 591: 128, 715: 188, 949: 150, 954: 128'. Line 715 is far longer than any of the others. Line 715 reads '// parts (#261) — the line before it holding a string it leaves open, or not. That endstream, the first of the data, ended it, inside the header, which the data ran into, and the'. Lines 714, 716, 717 and 718 are 126, about 120, 126 and 126 columns. Line 776 reads '// object. An endstream in a string, or one that' and line 777 continues '// is a token, is in no comment, and a header after it starts past it. ...' at 125 columns. git blame -L 711,721 and -L 772,779 give ea348ce5 for every line. grep max_line_length .editorconfig finds nothing, so no build rule applies.

REFUTER FIX:
Rewrap the comment at lines 714 to 719 and the one at lines 773 to 778 so they fill each line to the width their neighbors use (about 126 columns with the indent), leaving the wording unchanged. This is not a decision for the maintainer.

######## [16] lens validator-tests — timing-317-past-bound-under-load
reviewer severity: nit; refuter: refuted / none; needs_maintainer: False
location: tests/AdCodicem.Pdf.Tests/ReaderTimingTests.cs:129 (The_validator_judges_an_object_stream_s_members_in_linear_time, the #317 test of B19-8 / R1-Q4)

CLAIM:
Under four busy loops, on a machine other review agents were also loading (1-min load 3.85 before my loops started), the #317 timing test failed once, at a ratio of 15.8 against its bound of 8. The comment records 9.0 as the worst it has seen under a heavier load. Every other ReaderTimingTests case and every budget test the round touched passed in every run.

EVIDENCE:
Script /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r4vt-timing.sh on b4ab948 (Release, --no-build). Idle: ReaderTimingTests 5/5 passed (25 cases, 31-34 s each); the five touched budget tests (11 cases: the crossing test's 4 rows, A_table_reads_nothing... x3, The_look_at_what_a_prev_names... x2, A_prev_looping_back_through_the_spaces..., StreamLengthTests An_endstream_in_a_header_s_comment_is_decided_by_reading_on_once...) 5/5 passed. Under four busy loops: budget 3/3 passed. ReaderTimingTests passed in runs 2 and 3 and failed in run 1 (12:33:09-12:34:04, while I ran nothing CPU-heavy): 'failed ...The_validator_judges_an_object_stream_s_members_in_linear_time(shape: "an index wrong for every member") ... Expected largeTime to be less than 256ms and 672.0µs because four times the members must cost about four times the time (00:00:00.0320840 for 50,000, 00:00:00.5066963 for 200,000), but found 506ms and 696.3µs.' Four more runs under four busy loops with the load logged (r4vt-timing2.sh; 1-min load 1.75 to 5.82; 12 dotnet processes on the box): 4/4 passed. In total 1 failure in 7 loaded runs, at a ratio far beyond the comment's figures.

REVIEWER FIX:
No code change is needed in this batch: B19-8 consented to this design, and the comment already warns that a heavier load slows the large probes. Record the observation (15.8, 1 in 7 under four busy loops on a shared four-core box) beside the B19-8 validation figures in status.md. If CI reproduces it, file a debt to raise the probes per turn or the turns.

REFUTER REASON:
The measurement reproduces, but it is not a new defect. It shows again what round 2 [13] (t317-fails-with-builds-beside) found, which was narrowed to a nit and settled by 46e4796's comment. The comment does not misstate it, and the reviewer's own fix touches neither code nor docs.

1. The reviewer's failure is real. Their log busy-timing-1.txt shows 32.1 ms against 506.7 ms, a ratio of 15.8. The small probes ran at their usual time while every large turn slowed. The comment at ReaderTimingTests.cs:143-149 describes exactly this, under 'a heavier load'.

2. The reviewer's condition was not 'four busy loops' alone. They give a 1-min load of 3.85 from other agents before their loops started. Four loops on top of that put a four-core box into the regime the comment calls heavier (load 8 to 14 there). In my own runs the split by load is clean:
   - four busy loops at low ambient load: every run passed, well inside 8;
   - the same loops with other agents' builds pushing the load to 7-11: two failures, at 9.68 and 9.07, the small probes again at their usual time and the large turns slowed.
   This matches both of the comment's statements: 'never past 8' under four busy loops (R1e, load 4.7-6.3), and 'now and then' past 8 under heavier load.

3. The figures 8.6 and 9.0 are observations, not a stated ceiling. The same passage records other timing tests of the class failing under that load at up to 18.3 against a bound of 12, so 15.8 does not contradict it. The frequencies are comparable: the comment has 1 in 5 beside other tests, the reviewer 1 in 7, and I got 2 in 10.

4. Nothing in round 3 touched the test or the probe: git diff 19122f0 b4ab948 is empty for ReaderTimingTests.cs and CrossReferenceProbe.cs. B19-8 and R1-Q4 were implemented as decided and validated under their own condition.

5. The only fix proposed is a status.md record, and the brief says status.md's batch 19 entry is batch-end work, not a finding.

Optional, not a finding: when the batch-end writes #317's validation into status.md, it may cite these heavier-load observations beside R1e's figures. If CI ever crosses the bound, file a debt then.

REFUTER EVIDENCE:
Setup: Release build of b4ab948 (origin/ccr-6d6c0ee6-dqm4lm-188) in my worktree, 0 warnings. The only change was a temporary environment-gated line before the #317 assertion that appends small ms, large ms, ratio and /proc/loadavg to a file. It is reverted and the worktree is clean. The four busy loops were bash `while :; do :; done` (PIDs 780-783), killed by PID and checked gone. Script and logs: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r4ref-t317/ (run.sh, summary.txt, ratios-idle.tsv, ratios-busy.tsv, busy-N.txt).

- **No busy loops, ambient load 4.5-5.0 from other agents, #317 theory filtered alone, 3 runs:** all passed. Streams 4.23, wrong-index 4.27-4.48, broken 4.35-4.60.
- **Four busy loops, whole ReaderTimingTests class (25 cases), 10 runs:**
  - Runs 1-5, 1-min load 5.4-5.9: 5/5 passed. #317 ratios 3.21-6.10.
  - Runs 6-10, load rising to 7.2-11.2 while other agents built: runs 6 and 7 failed, runs 8-10 passed.
  - Run 6 (load 7.18): `failed ...The_validator_judges_an_object_stream_s_members_in_linear_time(shape: "an index wrong for every member") ... (00:00:00.0347722 for 50,000, 00:00:00.3364652 for 200,000), but found 336ms and 465.2µs`, ratio 9.68.
  - Run 7 (load 9.93): `failed ...(shape: "a header broken before its last member") ... (00:00:00.0199637 for 50,000, 00:00:00.1811063 for 200,000)`, ratio 9.07.
  - In both failures the small probes were at their usual 20-35 ms. No other case of the class failed in any run.

Round 2 [13] already covers this, settled as a nit. r2-findings.md:816-856 records #317 at 9.04 with builds beside four busy loops, and the refuter's verdict that it is wording only. decisions.md lists it under 'Review round 2' ('[13] #317 comment (heavier load)'), and 46e4796 rewrote the comment to record the heavier-load excursions (8.6, 9.0) and other tests failing up to 18.3 at bound 12. git diff 19122f0 b4ab948 -- tests/AdCodicem.Pdf.Tests/ReaderTimingTests.cs src/AdCodicem.Pdf/Validation/CrossReferenceProbe.cs is empty.

REFUTER FIX:
None in this batch. The test, its bound and its comment stand as B19-8 and R1-Q4 decided and round 2 [13] settled. If wanted, the batch-end status.md record of #317's validation can add that round 4 saw the ratio past 8 under heavier load, at 9.07, 9.68 and 15.8. File a debt only if CI crosses the bound.

######## [17] lens validator-tests — stream-own-row-reserved-refusal-untested
reviewer severity: nit; refuter: confirmed / nit; needs_maintainer: False
location: src/AdCodicem.Pdf/IO/XRef/XRefSectionRecord.cs:86 (ReservedRowsRefused); tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:5007 and CrossReferenceRuleTests.cs:2325 (the reserved-refusal theories)

CLAIM:
Four texts name a second way a reserved row's number can be refused before it is read: by an earlier row of the same stream for the same number. They are the ReservedRowsRefused XML doc, ReportReservedRows' remarks, ReservedTypeRule's remarks and validation-rules.md:55. R3b chose the neutral wording ('refused before it was read', not 'a newer section') because of that case. None of the round's reserved-row rows builds it: every row uses a lost stream's declared range, every number, or the newest table's own row. The behavior is right today, but no test pins the case the wording was chosen for.

EVIDENCE:
Probe ZzR4vtProbeTests.ZzOwnRowThenReserved, uncommitted, in a scratch worktree at b4ab948 that has since been removed. The section startxref names is stream 5, /Index [0 4 3 1] /W [1 4 3], whose rows for object 3 are type 1 with generation 65536 and then type 7. Output: 'D xref.entry-reserved ... gives 1 row a reserved type: the row for object 3 gives it type 7. ... (7.5.8.3), but its number was refused before the row was read: it stands for nothing, and its object is looked for by rebuilding the index.' / 'S startxref Read @133 reserved=1 refused=1 rowsRefusedBefore=False' / 'obj3 = "three" repaired=True' / 'F xref.entry-type-reserved ... but the reader did not read the row so, its number refused before it was read: it reads its object as the rebuild finds it.' The count did not double after the rebuild that ran before validation. grep for ReservedRowsRefused in tests/ finds only CrossReferenceChainTests.cs:5023 and :5058, both on ReservedRowFiles.PastARefusal shapes.

REVIEWER FIX:
Add a row for the stream's own earlier refused row to A_row_of_a_reserved_type_whose_number_was_refused_before_it_is_said_to_stand_for_nothing, and its validator twin: ReservedRowFiles.PastARefusal could take an /Index [3 1 3 1] option, or use a small builder like the probe's. Assert ReservedRowsRefused 1, RowsRefusedBefore false and both messages.

REFUTER REASON:
The finding holds, and it is wider than stated. I rebuilt the reproducer at b4ab948. The behavior is right. ReadXRefStreamRows' `case 1 or 2` calls Refuse, which records the number with the section itself as refuser. The stream's later type-7 row then goes through AddFromChain: the refuser is the same section, so RowsRefusedBefore stays false, it returns false and ReservedRowsRefused++ runs. Both reports then use the neutral wording. R3b chose that wording for exactly this case: r3-fix-result.json:127 says "the count includes a row the stream's own earlier row for the same number refused, and for that row 'a newer section' would be false". No test builds that case. In tests/, ReservedRowsRefused is asserted only at CrossReferenceChainTests.cs:5023 and :5058. The neutral messages are asserted only at CrossReferenceChainTests.cs:5000-5003 and :5060 and CrossReferenceRuleTests.cs:2326-2327. All of those use ReservedRowFiles.PastARefusal, where the refuser is always another section: a lost stream's range, every number, or the newest table's row. No stream in tests/ gives the same number twice. Eight texts name the case, not four: the four the finding cites, plus the public XML doc of PdfDiagnosticCodes.XRefEntryReserved (:122-123), diagnostics.md:117, lazy-reading.md:93-94 and threat-model.md:427. The threat-model row cites A_row_of_a_reserved_type_whose_number_was_refused_before_it_is_said_to_stand_for_nothing for that clause, and that test does not build the case. It stays a nit. Every branch involved is reached separately: refuser == section by A_table_that_gives_a_number_twice_and_refused_one_of_the_rows_says_no_newer_section_refused_the_other, the reserved arm's false return and both message arms by the PastARefusal rows. Neither message depends on RowsRefusedBefore. I see no first-order mutant (ADR 53 catalog) that only this case would kill. So nothing is wrong today; a case the texts name is simply not pinned by a test (invariant 9, test names cited by the threat model).

REFUTER EVIDENCE:
Probe ZzR4RefOwnRowTests.ZzOwnRowThenReserved, built in this worktree at b4ab948 (detached origin/ccr-6d6c0ee6-dqm4lm-188), now removed; copy at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/ZzR4RefOwnRowTests.cs.txt, output at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r4ref-ownrow-out.txt. The file is objects 1 to 3, then stream 5 at offset 133, which startxref names: /Index [0 4 3 1] /W [1 4 3]. Its rows for object 3 are type 1, generation 65536, then type 7. Output with refusedFirst=True:
'D xref.section-unreadable @133: ... the row for object 3 gives it generation 65536, outside 0 to 65535; that row alone was refused, the rows after it read, and its object is looked for by rebuilding the index where no row read before it indexes it, rather than taken from an older section's row.'
'D xref.entry-reserved @133: The cross-reference stream startxref names at offset 133 gives 1 row a reserved type: the row for object 3 gives it type 7. ... (7.5.8.3), but its number was refused before the row was read: it stands for nothing, and its object is looked for by rebuilding the index.'
'S startxref Read @133 reserved=1 refused=1 rowsRefusedBefore=False'
'index has 3 = False' / 'obj3 = "three" repaired=True'
'F xref.entry-type-reserved @133: ... but the reader did not read the row so, its number refused before it was read: it reads its object as the rebuild finds it.'
This matches the finding's output exactly; the count stays 1 after the rebuild. Control with the rows the other way round (refusedFirst=False): 'refused=0', ': the row was read so, and no older section's row stands for its object', 'index has 3 = True', obj3 PdfNull. In tests/, grep for ReservedRowsRefused finds only CrossReferenceChainTests.cs:5023 and :5058, and grep for the neutral messages finds only the PastARefusal rows. No stream in tests/ repeats a number in its /Index (checked the matching /Index patterns in ChainFiles, HybridFiles, ConvergenceShapes, HostileInputTests, CrossReferenceChainTests and CrossReferenceRuleTests).

REFUTER FIX:
Add the stream's own earlier refused row as a case in both theories. In the chain theory, A_row_of_a_reserved_type_whose_number_was_refused_before_it_is_said_to_stand_for_nothing, assert: the xref.entry-reserved message ending ', but its number was refused before the row was read: ...', ReservedRowsRefused 1, RowsRefusedBefore false, ChainIndex lacks 3, object 3 read as "new three" through a rebuild, WasRepaired true. In the validator twin, A_row_of_a_reserved_type_whose_number_was_refused_before_it_is_not_said_to_have_been_read_as_null, assert the 'but the reader did not read the row so, its number refused before it was read: ...' finding. The cheapest builder change: give ReservedRowFiles.PastARefusal an option that writes the middle stream with /Index [3 1 3 1] and rows (2, 0, 0) then (7, 0, 0), with lost null and newest empty. Under /W [1 4 2], a type-2 row whose object stream is numbered 0 is refused alone, which is the own-row refusal needed. A small builder like the probe's also works. Optionally add this test to the threat-model row (threat-model.md:427) next to the one it already cites for that clause. Nothing here needs a maintainer decision.

######## [18] lens validator-tests — test-names-omit-glued-letter-row
reviewer severity: nit; refuter: confirmed / nit; needs_maintainer: False
location: tests/AdCodicem.Pdf.Tests/CrossReferenceRuleTests.cs:812 (A_stream_header_in_a_comment_or_a_string_near_where_an_offset_names_no_section_is_no_malformed_section); tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:1126 (The_header_of_a_cross_reference_stream_in_a_comment_or_a_string_near_where_an_offset_names_no_section_is_no_section_found_moved)

CLAIM:
Both names say the header lies 'in a comment or a string'. Each theory also has the row 'x8 0 obj << /Type /XRef >>', a header whose number a letter runs into, which is in neither. The comments mention it, but the names state less than the tests hold.

EVIDENCE:
CrossReferenceRuleTests.cs:812 `[InlineData("a /Prev", "x8 0 obj << /Type /XRef >>\n")]` and CrossReferenceChainTests.cs:1126 `[InlineData("x8 0 obj << /Type /XRef >>\n")]`. With 19122f0's src under the b4ab948 tests, these rows fail as their siblings do (74 of 194 round cases fail there, all as claimed), so the rows are real; only the names lag.

REVIEWER FIX:
Rename both to cover the third shape, for example '..._in_a_comment_a_string_or_a_longer_token_near_where_...', or move the glued-letter row into a theory named for it.

REFUTER REASON:
The finding holds as stated, and the fix needs to go one step further.

Both theories were added by the round 3 fix for finding [8], which asked for 'comment, string and x8 shapes'. Each has three kinds of row:
- the header in a comment;
- the header in a string;
- `x8 0 obj << /Type /XRef >>`.

In NearbyFiles.UpdateBehindAStrayWord and HybridBehindAStrayWord, the `between` text is written raw on its own line right after `endobj\n`, at top level. So in the third row, `x8` is a regular token: a header whose number a letter runs into, which is neither a comment nor a string. The test comments say so ('or such a header a letter runs into' / 'or such a header whose number a letter runs into'). The names say only 'in_a_comment_or_a_string'.

The name is still true for the rows it covers. It just says less than the test holds, so this is a wording issue and not a test that fails to hold its claim. Earlier rounds settled test-name findings at nit and fixed them by renaming (R1 [35]/[36], R2 [16]/[17], R3 [7]/[16]), so nit is consistent.

The sibling theories need no change:
- The_word_xref_in_a_comment_or_a_string_near_where_an_offset_names_no_section_is_no_section_found_moved has no glued row.
- An_xrefstm_falling_short_of_its_stream_past_a_stream_header_in_a_comment_or_a_string_reads_the_stream has only the comment and string rows.

So the finding is not wider than the evidence. One thing it leaves out: docs/threat-model.md:419 cites both names verbatim, so a rename must update that row too.

REFUTER EVIDENCE:
At b4ab948:
- tests/AdCodicem.Pdf.Tests/CrossReferenceRuleTests.cs:812 is `[InlineData("a /Prev", "x8 0 obj << /Type /XRef >>\n")]`, on the method at :814, A_stream_header_in_a_comment_or_a_string_near_where_an_offset_names_no_section_is_no_malformed_section.
- tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:1126 is `[InlineData("x8 0 obj << /Type /XRef >>\n")]`, on the method at :1127, The_header_of_a_cross_reference_stream_in_a_comment_or_a_string_near_where_an_offset_names_no_section_is_no_section_found_moved.
- In NearbyFiles.cs, UpdateBehindAStrayWord (:616) writes `file.Raw(between)` right after object 3's or object 6's `endobj\n`, and HybridBehindAStrayWord (:683) writes it right after object 7's. Either way the text is at top level, not in a comment or a string.
- `grep` finds both names cited verbatim in docs/threat-model.md:419, in the defense column of the 'A missing section tried at many places' row.
- The reviewer's run of the b4ab948 tests over 19122f0's src is at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r4vt-old-run.txt (lines 102 and 558) and r4vt-old-failed.txt (lines 34 and 43). Both x8 rows fail there, so the rows are real. r3-fix-result.json:19 also records the glued-digit row as failing on 19122f0.
- Round 3's verdict (r3-findings.md:641-650) reproduced `x8 0 obj` on 19122f0: found 6 bytes after, section-unreadable, rebuilt, 'three old'. That is the behavior this row pins.

I did not rebuild, because the claim concerns names only. The row placement and the citations come from reading the source at b4ab948.

REFUTER FIX:
Rename both theories so their names cover the third shape. For example:
- `The_header_of_a_cross_reference_stream_in_a_comment_a_string_or_behind_a_letter_near_where_an_offset_names_no_section_is_no_section_found_moved`
- `A_stream_header_in_a_comment_a_string_or_behind_a_letter_near_where_an_offset_names_no_section_is_no_malformed_section`

A name that states the behavior would also work, such as `..._a_header_no_load_reads_near_where_...`. Or move the x8 row into a theory of its own named for a header whose number a letter runs into.

In the same commit, update both citations in docs/threat-model.md:419. That means a `test:` commit, with the footer citing #281 as the tests' comments do.

No code or assertion changes, and the maintainer has nothing to decide.

######## [19] lens validator-tests — look-test-name-tautological
reviewer severity: nit; refuter: confirmed / nit; needs_maintainer: False
location: tests/AdCodicem.Pdf.Tests/HostileInputTests.cs:4651 (The_look_at_what_a_prev_names_reads_it_through_no_more_windows_than_a_raised_trailer_limit_allows_it)

CLAIM:
Read literally, the name says the look reads no more windows than the raised limit allows, which every read does by definition. What the test holds, and what kills M29 and M30, is something else: raising MaxTrailerLength to 1 MB reads exactly as many bytes as the default does. A window full of stream data, or the file's end, grows no window.

EVIDENCE:
Body: `Read(PdfReaderLimits.Default with { MaxTrailerLength = 1024 * 1024 }).Should().Be(Read(PdfReaderLimits.Default));`. The comment says 'no larger window read for what a window full of data holds, nor again for the file's end'.

REVIEWER FIX:
Rename to the behavior asserted, for example 'The_look_at_what_a_prev_names_reads_as_many_bytes_under_a_raised_trailer_limit_as_under_the_default'.

REFUTER REASON:
The finding holds. Read literally, the name says the look reads no more windows than the raised limit allows. The code guarantees that on its own, because TryParseUnrecordedAt grows the window eightfold only while `windowSize < maxWindow`, and maxWindow is `_guard.Bound(PdfLimit.Trailer)`. So the name's literal claim stays true on both mutants the test exists to kill, M29 and M30 (r3-fix-result.json lists it as their killer). What the body actually asserts is that MaxTrailerLength raised to 1 MB reads exactly as many bytes as the default (64 KB, which equals XRefWindow, so the default never grows a window). In other words, no window grows for a window full of stream data, nor for a cut at the file's end. Under M30 the mutant reads exactly as many windows as the raised limit's growth steps allow, which the literal name permits, and the test still fails. The test itself is real: it passes at b4ab948 and kills both mutants. The name is the only problem. Round 2 settled its naming findings at nit, and round 3 its [7]/[16]. This is the same kind of finding, so it stays nit. One thing the finding left out: docs/threat-model.md row 417 cites the test by its current name, so a rename must update that citation in the same commit. The maintainer has nothing to decide.

REFUTER EVIDENCE:
At b4ab948 (origin/ccr-6d6c0ee6-dqm4lm-188), I added a probe (uncommitted, now removed) that builds the test's two files and counts StrictCountingSource.BytesRead at three trailer limits: the default, 1 MB and 8 MB.
- Head: 100 KB stream row 445520 / 445520 / 445520; dictionary cut by EOF row 24070 / 24070 / 24070. The named test passes.
- M29, grow condition `if (edge && windowSize < maxWindow)`: stream row 445520 / 546984 / 546984, one extra window of 101,464 bytes (64 KB grown to 512 KB, within the 1 MB the limit allows). The test fails: "Expected Read(PdfReaderLimits.Default with { MaxTrailerLength = 1024 * 1024 }) to be 445520L, but found 546984L (difference of 101464)."
- M30, grow condition `if (parser.IsTruncated && windowSize < maxWindow)`: dictionary row 24070 / 26834 / 28216. That is two extra reads of the tail at 1 MB (64K to 512K to 1M) and three at 8 MB, one per growth step the limit allows, so exactly what the literal name permits. The test fails: "Expected ... to be 24070L, but found 26834L (difference of 2764)."
- Code: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3763-3782. maxWindow = _guard.Bound(PdfLimit.Trailer), and the window grows only while windowSize < maxWindow. XRefWindow = 64 * 1024 (PdfFileReader.cs:36). The default MaxTrailerLength = 64 * 1024 (PdfReaderLimits.cs:148).
- Citation: `grep no_more_windows_than_a_raised` finds tests/AdCodicem.Pdf.Tests/HostileInputTests.cs:4651 and docs/threat-model.md:417, whose Held-by list names the test.
After the probe, the worktree was restored: `git checkout -- src/...` and the probe file deleted.

REFUTER FIX:
Rename tests/AdCodicem.Pdf.Tests/HostileInputTests.cs:4651 after what it asserts, for example `The_look_at_what_a_prev_names_reads_as_many_bytes_under_a_raised_trailer_limit_as_under_the_default`, or, closer to the comment, `The_look_at_what_a_prev_names_grows_no_window_for_stream_data_or_the_file_s_end_under_a_raised_trailer_limit`. Replace the old name in docs/threat-model.md:417's Held-by list in the same `test:` commit (footer Refs #156). The body and comment stay as they are. No code change, and nothing for the maintainer to decide.

######## [20] lens validator-tests — windowedge-comment-wrap
reviewer severity: nit; refuter: confirmed / nit; needs_maintainer: False
location: tests/AdCodicem.Pdf.Tests/WindowEdgeTests.cs:715 and :776-777

CLAIM:
Two comments the round added were not rewrapped. Line 715 runs to 188 characters, where its neighbours stop at about 120. Lines 776-777 break after 'or one that', leaving a short line before the paragraph goes on.

EVIDENCE:
awk on b4ab948: '715: 188:         // parts (#261) — the line before it holding a string it leaves open, or not. That endstream, the first of the data, ended it, inside the header, which the data ran into, and the'. Line 776 reads '        // object. An endstream in a string, or one that', and 777 continues '// is a token, is in no comment, ...'.

REVIEWER FIX:
Rewrap both comment paragraphs to the file's width.

REFUTER REASON:
The finding holds on b4ab948. Both comments were added by round 3's fix ea348ce, which is in 19122f0..b4ab948. `git log -L715,715` on that range names ea348ce, and `git diff 19122f0 b4ab948` shows both paragraphs as new lines. Line 715 of tests/AdCodicem.Pdf.Tests/WindowEdgeTests.cs is 188 bytes long. The other lines of its paragraph (714, 716-719) stop at 104-126. Lines 776-777 break after 'or one that' (56 bytes) and then run on to 125. Nothing breaks: no max_line_length is set in .editorconfig, and the build and format pass. This is style only, so it stays a nit. The pattern is a little wider than the finding says. The same round-3 fix set added an over-long line to an XML doc paragraph in 8ccd868: src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:50 is 150 bytes, where its neighbours (lines 44-56) stop at 106-126. The finding's fix should cover that line too. The other comment lines over 128 that round 3 added are one-line `<summary>` and `<param>` tags, which the codebase already writes on one line throughout. Those are not this defect. Older paragraph lines over 140 were already on main 21a0298 (9 lines, for example PdfFileReader.cs, ObjectStreamBrokenRule.cs:11 and ObjectRuleTests.cs:2174), so this round did not start that pattern.

REFUTER EVIDENCE:
At b4ab948, `awk '{print NR": "length($0)}'` on WindowEdgeTests.cs gives: 714: 126, 715: 188, 716: 120, 717: 126, 718: 126, 719: 104; 775: 120, 776: 56 ('        // object. An endstream in a string, or one that'), 777: 125, 778: 102. `git log --format=%h -L715,715:tests/AdCodicem.Pdf.Tests/WindowEdgeTests.cs 19122f0..b4ab948` gives ea348ce ('fix: end no stream's data at an endstream a comment between a header's tokens holds'). In PdfDiagnosticCodes.cs at b4ab948, lines 44-56 measure 106/122/121/120/122/124/150/125/126/125/119/125/117. Line 50 is '    /// newer section refused every number, as the message says —; the objects only the rest indexes are missing from the index, which is rebuilt by', and `git log -L50,50` names 8ccd868. A scan of every comment line added in 19122f0..b4ab948 finds paragraph-width defects only at WindowEdgeTests.cs:715, WindowEdgeTests.cs:776 and PdfDiagnosticCodes.cs:50. The other hits are one-line summary and param tags. No .editorconfig max_line_length exists.

REFUTER FIX:
Rewrap three comment paragraphs to about 120-126 columns, like the lines around them: the comment at WindowEdgeTests.cs:714-719, the comment at WindowEdgeTests.cs:773-778, and the `<remarks>` paragraph around PdfDiagnosticCodes.cs:50. Change only the wrapping, not the text. This needs no maintainer decision.

######## [21] lens docs — docs-171-earlier-string-gap-untracked
reviewer severity: minor; refuter: narrowed / minor; needs_maintainer: False
location: docs/website/docs/concepts/lazy-reading.md:185-186; docs/threat-model.md:447-449 (the [#171] known-gaps entry)

CLAIM:
Round 3 writes a known gap into the docs: the search near an offset the chain names still takes the word xref, or a /Type /XRef header, on a line that starts inside a string an earlier line opened. Both pages put it under #171. Issue #171 does not name this search. Its body and its six comments cover the rebuild's scan, the search near a row and the stream-end rule, not the chain's search for a section found moved. Its 'What closes it' list would not close this gap. The R3a/R3c open point left the #171 comment optional, so the gap is filed in no issue, and the threat model cites only [#281] for it, which this batch closes.

EVIDENCE:
Probe ZzStringOpenedOnEarlierLine (saved at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r4docs-ZzR4DocsProbe.cs.txt) uses NearbyFiles.UpdateAfterAStrayLine. Results on b4ab948:
- Line "9 0 obj\n(aaa\nxref see the table below)\nendobj\n": '/Prev Malformed 1535'; 'xref.offset-adjusted @1535: ... names at offset 1584 was found 49 bytes before it'; 'xref.section-unreadable @1535: ... it holds the keyword see at offset 1540 ...'; 'xref.rebuilt'; object 4 = "old four", WasRepaired=True. The update had freed object 4.
- Control, the same string on one line: '/Prev Relocated 1649', '/Prev Read 159', object 4 = null, WasRepaired=False.
- mcp issue_read of #171 (body and get_comments, 6 comments): none mentions the search near an offset the chain names, the xref keyword, or a /Type /XRef header.

REVIEWER FIX:
Comment on #171 with this reproduction, naming the search near an offset (TryRelocateXRefSection/IsTokenOnItsLine) as a fourth reader of a line that starts inside a string, or file it as a debt beside #171. Keep the docs' link pointing to the issue that tracks it.

REFUTER REASON:
The finding holds in substance. Two of its sub-claims are wider than the evidence.

What holds:
- The gap is real at b4ab948, and it regresses against main 21a0298. The search near an offset the chain names takes the word `xref`, or a `/Type /XRef` stream header, on a line that starts inside a string an earlier line opened. It reports that place as the section found moved, rebuilds the index, and serves the object the update freed. Main skips that place, finds the real table and gives null (round 3 [1], the refuter's 'known case' row).
- Issue #171's body names the rebuild's scan, the search near a row (for an object) and trailers. Its six comments add the chunk edge, the stream-end rule, `% N G obj` on the keyword's line, and a header split across lines inside a string. None of them names the chain's search for a section found moved (TryRelocateXRefSection / IsTokenOnItsLine).
- None of the open debts filed since 2026-11-03 (#320 to #330, #292, #260) names it either.
- lazy-reading.md:187-188 links #171 for it, and the code remark of IsTokenOnItsLine cites #171. A reader who follows the link finds nothing about this search.
- Round 3's open point is recorded in decisions.md as 'Optional #171 comment (earlier-string limit) left'. The batch did comment on #171 for the other two shapes it added to that same Known gaps entry: the chunk edge (comment of 2026-11-03) and the string split across lines in a header (2026-11-04). This shape is the only one without a comment.
- CLAUDE.md requires an issue for anything found and left.

Narrowed:
1. 'The threat model cites only [#281] for it' misreads the page. The sentence sits inside the bullet headed [#171]. Its inner [#281] names where the behavior came from, as [#261] does in the same bullet for the chunk-edge shape. #261 is also closed by this batch.
2. 'Its What closes it list would not close this gap' is too strong. #171's list ends with 'the Known gaps entry becomes a defense'. Closing #171 as written would therefore take in this sentence, so the gap is tied to #171's closing through the threat model. What is missing is issue-level text that names the search and records the regression against main.

The docs describe the behavior correctly. The defect is the tracking: a link to an issue that does not name the gap, and a regression against main recorded in no issue text. Minor: no code or doc text is wrong, and the fix is one GitHub comment that needs no maintainer decision.

REFUTER EVIDENCE:
My probe ran at b4ab948, built Release, through NearbyFiles.UpdateAfterAStrayLine. It is saved at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/ZzRefute171Gap.cs.txt, with its output at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/zz-refute171-out.txt.

- Line "9 0 obj\n(aaa\nxref see the table below)\nendobj\n" (sections: startxref Read 1814, /Prev Malformed 1535):
  - 'D xref.offset-adjusted @1535: The cross-reference section /Prev names at offset 1584 was found 49 bytes before it.'
  - 'D xref.section-unreadable @1535: ... it holds the keyword see at offset 1540 ...'
  - 'D xref.rebuilt'
  - object 4 = "old four", WasRepaired=True
- Line "9 0 obj\n(aaa\n12 0 obj << /Type /XRef >> stream)\nendobj\n" (a variant the finding did not test):
  - '/Prev Malformed 1535'
  - 'xref.offset-adjusted ... 1593 was found 58 bytes before it'
  - 'xref.section-unreadable ... its /W does not give the widths of three fields'
  - 'xref.rebuilt'
  - object 4 = "old four", WasRepaired=True
- Control, the same string on one line:
  - '/Prev Relocated 1649', '/Prev Read 159'
  - 'found 65 bytes after it'
  - object 4 = null, WasRepaired=False
- Main 21a0298, from round 3 [1]'s refuter, multi-line string row: 'found 65 bytes from there', GetObject(4) = null.

Issues:
- #171, read with mcp issue_read (get, and get_comments, 6 comments). It never mentions the search for a section, the xref keyword, or a /Type /XRef header. Its closing list includes 'the Known gaps entry becomes a defense'.
- Open issues updated since 2026-11-03: #171, #260, #292, #320 to #330. None names this gap.

Docs and code:
- threat-model.md:441-449: the sentence sits in the '- [#171]' bullet, citing [#281] as the same bullet cites [#261].
- lazy-reading.md:187-188 links #171.
- PdfFileReader.cs:4897-4898, in IsTokenOnItsLine's remark: 'A string a line before it opened is not seen, as in an object's header (#171).'
- decisions.md, round 3 fixes: 'Optional #171 comment (earlier-string limit) left'.

REFUTER FIX:
No code or doc change. At the batch's end, with the other GitHub tracking, comment on #171. The comment should:
- name the chain's search near an offset a /Prev or /XRefStm names (TryRelocateXRefSection, IsTokenOnItsLine) as a fourth reader that takes a token on a line starting inside a string an earlier line opened: the word `xref`, or a `/Type /XRef` stream header;
- give the reproducer: UpdateAfterAStrayLine with "(aaa\nxref see the table below)", which serves freed object 4 as "old four" after xref.rebuilt;
- say that this regresses against main, where such a place was skipped and the real table found 65 bytes off;
- say that paying #171 should pay it too, the entry's sentence becoming a defense.

The links in lazy-reading.md:188, in threat-model.md (the [#171] bullet) and in the code remark can stay as they are. Filing a separate debt beside #171 is an acceptable alternative, but the comment is enough and matches what the batch did for the two other shapes it added to that entry. Not a maintainer decision.

######## [22] lens docs — docs-taken-stream-faulty-prev-held-by-no-test
reviewer severity: minor; refuter: narrowed / minor; needs_maintainer: False
location: docs/threat-model.md:417; docs/website/docs/concepts/lazy-reading.md:124-132; docs/website/docs/reference/diagnostics.md:115; src/AdCodicem.Pdf/IO/PdfFileReader.cs:4740-4743 (TryRelocateXRefSection remarks)

CLAIM:
Round 3's rewording for round 3's [13] adds a behavior to four texts. A /Prev's search takes the cross-reference stream the /XRefStm read in the stream's place wherever the stream's dictionary gives any /Prev, and 'a stream whose /Prev then names no older section is read whole all the same, and that /Prev reported as the chain finds it: a loop, an offset outside the file, no offset'. No test holds that clause, and none of the tests row 417's 'Held by' cites does. The only test of the taken stream, A_prev_falling_short_of_the_stream_its_xrefstm_read_reads_it_whole_and_the_naming_is_reported, uses a stream whose /Prev names the oldest table. The round 3 refuter noted this gap, and R3a added no row for it.

EVIDENCE:
Probe ZzStreamTakenWhateverItsPrevNames (shape of HybridFiles.PrevFallingShortOfTheStreamBefore, only stream 6's /Prev varied), on b4ab948:
- itself: '/Prev Relocated 1521', then hybrid-stream-lost, then 'xref.chain-cycle @1521: ... names offset 1521, which the chain has already read.', then xref.rebuilt.
- 99999999: Relocated 1521, then section-missing '... offset 99999999 lies outside the file, which is 1,815 bytes long ...', then rebuilt.
- (x): Relocated 1521, with 'The /Prev of the cross-reference section at offset 1521 is not an offset', then rebuilt.
- null, and no /Prev: '/XRefStm Read 1521', '/Prev NotFound 1514', '... is not there, nor near it', then rebuilt.
The texts are true. grep of tests/ for PrevFallingShortOfTheStreamBefore finds only the reservedRow theory and the older-table fact.

REVIEWER FIX:
Add rows to A_prev_falling_short_of_the_stream_its_xrefstm_read_reads_it_whole... (or a sibling theory with a /Prev parameter on the builder): itself gives chain-cycle, 99999999 gives section-missing outside the file, (x) gives 'is not an offset', and null or none leaves the stream untaken with '/Prev NotFound'. Cite the test in row 417's Held by.

REFUTER REASON:
The finding is half right. The texts are true at b4ab948, but the half of the clause that says a stream with a faulty /Prev is still taken is held by no test. I narrowed it to that half.

What the four texts say, and what the code does, PdfFileReader.cs:4814-4815:
- The four texts are threat-model.md:417, lazy-reading.md:125-130, diagnostics.md:115 and the remarks at PdfFileReader.cs:4739-4742.
- They say a /Prev's search takes the cross-reference stream the /XRefStm read in the stream's place wherever the stream's own dictionary gives a /Prev, whatever that /Prev names.
- They also say such a stream is read whole even when its /Prev names no older section, and that /Prev is reported as the chain finds it: a loop, an offset outside the file, or no offset.
- The code matches: `readInStreamsPlace is { Kind: Stream } stream && candidates.Remove(takeAt) && stream.Trailer?.GetRaw(PdfName.Prev) is not null`.

Where the finding is too wide:
- Its fix asks for null and no-/Prev rows. The 'not taken where no /Prev' half is already held.
- When I dropped the `GetRaw(...) is not null` condition, CrossReferenceChainTests.A_prev_naming_a_place_inside_the_stream_its_hybrid_table_names_is_missing_not_found_in_that_stream failed, and so did CrossReferenceRuleTests.A_prev_naming_a_place_inside_the_stream_its_hybrid_table_names_is_not_found. Row 419 cites the first of these.

Where it holds:
- The 'faulty /Prev, taken all the same' half (the stream itself, outside the file, a string, a negative number) is held by nothing.
- The only test of the taken stream is A_prev_falling_short_of_the_stream_its_xrefstm_read_reads_it_whole_and_the_naming_is_reported. Its stream's /Prev names the oldest table, so a stricter check passes it as well.
- I narrowed the code to round 3's rejected option 1: take the stream only where its /Prev is an in-file offset the chain has not read. The whole unit suite still passes. So that rule could quietly replace the documented one while the four texts went on saying otherwise.
- Round 3's refuter flagged this gap as optional rows. decisions.md settled [13] as 'reword the five texts' only, and R3a added no test for it.

Why minor: the texts state a behavior no test holds, and none of the tests row 417's 'Held by' cites holds it. Nothing is served wrong, it is not a regression, and the first-order catalog of ADR 53 is killed (negating or dropping the condition both fail tests). It is not a maintainer choice: round 3 already settled the behavior, and this only adds tests for it.

REFUTER EVIDENCE:
My own probe, scratchpad/lot19/r4ref-ZzRefTakenStreamPrevTests.cs.txt. It uses the shape of HybridFiles.PrevFallingShortOfTheStreamBefore: oldest table at 159, 1,200 bytes off; stream 6 at 1526; the newest table's /Prev is 1519 and its /XRefStm is 1526. Only stream 6's own /Prev changes between rows. Output is in scratchpad/lot19/r4ref-takenprev-head.txt.

At b4ab948:
- oldest (the control): sections /XRefStm NamedAgain 1526, /Prev Relocated Stream 1526, /Prev Read Table 159. Diagnostics: offset-adjusted, hybrid-stream-lost. WasRepaired=False.
- self: Relocated Stream 1526, then offset-adjusted, hybrid-stream-lost, 'xref.chain-cycle @1526: ... the /Prev of the section at offset 1526 names offset 1526, which the chain has already read.', then xref.rebuilt. The validator gives xref.chain-loop [Error].
- 0099999999: Relocated 1526, then hybrid-stream-lost, 'xref.section-missing @1526: ... offset 99999999 lies outside the file, which is 1,822 bytes long ...', then rebuilt.
- (x) and -5: Relocated 1526, with 'The /Prev of the cross-reference section at offset 1526 is not an offset' (reported first), offset-adjusted, hybrid-stream-lost, then rebuilt. The validator gives 'is a value of type string' and 'is the integer -5'.
- null, and no /Prev: /XRefStm Read Stream 1526, /Prev NotFound 1519 '... is not there, nor near it', then rebuilt.

Narrowing mutant: at :4815 I replaced the check with `GetRaw(Prev)?.AsInteger() is { } p && p >= 0 && IsInFile(PositionOf(p)) && !_sectionsReadAt.Contains(PositionOf(p))`.
- The probe then shows self, outside, (x) and -5 all as '/XRefStm Read Stream 1526, /Prev NotFound 1519', with no hybrid-stream-lost.
- The whole AdCodicem.Pdf.Tests suite: total 7607, failed 7, all of them my probe's Assert.Fail rows; succeeded 7580; skipped 20. Log: scratchpad/lot19/r4ref-takenprev-mutant-suite.txt.

Mutant dropping the `is not null` condition:
- Failed: CrossReferenceChainTests.A_prev_naming_a_place_inside_the_stream_its_hybrid_table_names_is_missing_not_found_in_that_stream and CrossReferenceRuleTests.A_prev_naming_a_place_inside_the_stream_its_hybrid_table_names_is_not_found, plus the probe rows. Log: scratchpad/lot19/r4ref-takenprev-dropmutant-suite.txt.

The source was restored and the probe removed. The worktree is clean.

REFUTER FIX:
Add a sibling theory beside A_prev_falling_short_of_the_stream_its_xrefstm_read_reads_it_whole_and_the_naming_is_reported in CrossReferenceChainTests. Give HybridFiles.PrevFallingShortOfTheStreamBefore a parameter for stream 6's /Prev entry, keeping the oldest as the default. The rows, each asserting /Prev Relocated Stream at the stream, hybrid-stream-lost, and the fault of the stream's own /Prev:
- the stream's own offset: xref.chain-cycle '... names offset {stream}, which the chain has already read';
- 99999999: xref.section-missing '... lies outside the file';
- (x): '... is not an offset'.

Optionally add a fourth row, /Prev null, asserting '/XRefStm Read Stream, /Prev NotFound {stream - 7}'. That holds null alongside the existing no-/Prev test. Cite the new test in threat-model.md row 417's 'Held by'. The code and the texts stay as they are. This is not a maintainer choice.

######## [23] lens docs — docs-threat-model-425-no-prev-place-exception
reviewer severity: nit; refuter: confirmed / nit; needs_maintainer: False
location: docs/threat-model.md:425

CLAIM:
Row 425 still states without exception: 'A place that declares another type — a CIDFont, whose /W gives widths —, or whose dictionary a guard cut before it said what it is, is a guess dropped, the next place tried'. Since 2b26d18, at the place a hybrid table's /Prev names, where its /XRefStm's search comes to it, the search stops instead when a guard cut that place before it said what it is, or cut a value its rows are read with (KeptPlace.Guard). That holds for a stream that gives another /Type too. Row 417 says so, and b4ab948 added the same 'but for' to reference/reader-limits.md:166-172. Row 425 was not brought in line, so the two rows of one page disagree.

EVIDENCE:
CrossReferenceChainTests.An_xrefstm_stops_short_of_its_stream_only_where_its_table_s_prev_keeps_its_place passes, including the row 'a font whose dictionary a guard cuts', which stops (12/12 with the long-line test). Probe ZzValueCutAtPrevPlace, row 'font /W cut' (object 4 = '<< /Type /Font /W 5 0 R /Size 3 /Length 30 >>' stream, object 5 cut by MaxObjectLength 1024): '/XRefStm NamedAgain 3003 ... ReadAs=/Prev@3021 NamesNoStream=null', '/Prev Malformed 3021 CutByLimit=True'. Diagnostics: only limit.object. Findings: only xref.checked-in-part. WasRepaired=False. The search stopped at the place; it was not dropped and the next place was not tried.

REVIEWER FIX:
In row 425, after 'is a guess dropped, the next place tried', add as the reference row does: '— but for what a hybrid table's /Prev names, where its /XRefStm's search near the offset it names comes to it: a guard's cut there, before it said what it is or in a value its rows are read with, stops the search, the naming the guard's (above)'.

REFUTER REASON:
The finding holds at b4ab948. Row 425 of docs/threat-model.md states without exception: "A place that declares another type — a CIDFont, whose /W gives widths —, or whose dictionary a guard cut before it said what it is, is a guess dropped, the next place tried". That is true for an ordinary place the search near an offset tries. It is false at one place: what a hybrid table's /Prev names, when that table's /XRefStm search comes to it.

At that place, TryRelocateXRefSection (PdfFileReader.cs:4827-4836) asks KeepsItsPlace. KeepsItsPlace (3702-3718) reads the object with candidate: false, as the /Prev's own reading does. It returns Guard when check.CutByLimit is set: either a dictionary cut before its /Type, or a typed-other stream with a value its rows are read with cut. The search then breaks, so the next place is never tried. This follows round 3 decisions [0] and [10].

Row 417 of the same table states the exception ("Where the search stopped only because a guard cut what the /Prev names before it said what it is, or a value its rows are read with, the naming is the guard's"). b4ab948 added the same "but for" to reference/reader-limits.md:166-172. Row 425 was left untouched in 19122f0..b4ab948, so two rows of one page now disagree.

The gap is a little wider than the finding says, without changing its nature. At the same place, a stream of another type that reads as a section (KeptPlace.Section) also stops the search. Row 417 covers that case ("an object that gives a /W and reads there as a section, whatever its type"). Row 425 and the reference page's "but for" name only the guard's case. That is fine for a limits page, but row 425 states the rule in general terms.

On "Since 2b26d18": the stop for a dictionary cut before its /Type already existed at 19122f0 (KeepsItsPlace => DeclaresStreamAt's guard arm, from round 2a). What 2b26d18 added is the stop for a typed-other stream whose value was cut, the stop for a typed-other stream that reads as a section, and the guard's wording of the naming. So row 425 was already partly inconsistent before this round. The verdict does not change.

Severity is nit, matching round 2 [23]: a threat-model row leaves out an exception that another row of the same page and the user docs state. The code and tests are right, and no reader is misled about behavior if they read row 417.

REFUTER EVIDENCE:
The probe ZzTm425Probe (uncommitted, now deleted) mirrors HybridFiles.NamingAnObjectBesideTheStream, with object 6 changed to a 2,000-byte string. Layout: oldest 1481, stream 1649, object 4 at 1891, /XRefStm 1861. Run on b4ab948, built Release with 0 warnings.

Rows where the /Prev names object 4:
- Object 4 = '<< /Type /Font /W 6 0 R /Size 3 /Length 30 >>' stream, MaxObjectLength 1024. Sections: '/XRefStm NamedAgain 1861 readAs=/Prev@1891 noStream=null', '/Prev Malformed 1891 cut=True'. Diagnostics: only 'limit.object Warning @3202'. WasRepaired=False. Findings: only xref.checked-in-part. The stream at 1649 is not read, so the search stopped at the place.
- Object 4 = a /Type /Font dictionary with 70,000 bytes before its /Type, default limits. Sections: '/XRefStm NamedAgain 1861 readAs=/Prev@1891', '/Prev Malformed 1891 cut=True'. Diagnostics: 'limit.trailer @1891'. The search also stopped.
- Object 4 = a /Type /XRefStream stream that reads. Sections: '/XRefStm NamedAgain 1861 readAs=/Prev@1891', '/Prev Read 1891'. This is the wider Section case.

Contrast: the same three bytes, with the /Prev naming the oldest table so that object 4 is an ordinary place, give '/XRefStm Relocated 1649' and '/Prev Read 1481' in all three rows. There the place is dropped and the next one tried, which is row 425's rule.

The committed tests An_xrefstm_stops_short_of_its_stream_* pass 14/14 at the head. That includes the row ("a font whose dictionary a guard cuts", 0, stops=true) and the typed-other "reads a stream whatever type it gives" rows.

At 19122f0, KeepsItsPlace => table || DeclaresStreamAt(start), and DeclaresStreamAt returns DeclaresOnlyCrossReferenceStream(value) || (cut dictionary without /Type). So only the cut-before-/Type case stopped there. `git diff 19122f0 b4ab948 -- docs/threat-model.md` leaves row 425 unchanged.

REFUTER FIX:
In docs/threat-model.md row 425, after "is a guess dropped, the next place tried", add a "but for" pointing to row 417 that covers both of the code's stops, for example: "— but for what a hybrid table's /Prev names, where its /XRefStm's search near the offset it names comes to it: there the search stops where the /Prev keeps its place, as its own reading does — a stream of another type that reads there as a section, or what a guard cut before it said what it is or in a value its rows are read with, the naming then the guard's (above)". The reviewer's narrower wording, which covers only the guard, is acceptable if the Section case is left to row 417. Optionally, give reference/reader-limits.md:166-172's "but for" the same half-clause for a stream of another type that reads as a section; as a limits page it may stay guard-only. This is a docs-only change and needs no maintainer decision.

######## [24] lens docs — docs-names-what-previous-reads-omits-value-cut
reviewer severity: nit; refuter: confirmed / nit; needs_maintainer: False
location: src/AdCodicem.Pdf/IO/XRef/XRefSectionRecord.cs:263-264 (NamesWhatPreviousReads summary)

CLAIM:
The summary lists the guard-cut stop as false only 'where the search near the offset the /XRefStm names stopped only where a guard cut what the /Prev names, before it said what it is'. The Guard arm (PdfFileReader.cs:3496-3506, NameAgain with readAs null) also covers a cut of a value its rows are read with (CutByValue). There too the property is false, ReadAs is set and nothing is reported. The sibling docs (ReadAs, NamesNoStream, the threat model and reference pages) say 'or a value its rows are read with', or stay generic.

EVIDENCE:
Probe ZzValueCutAtPrevPlace, row 'untyped /W cut' (object 4 = '<< /W 5 0 R /Size 3 /Length 30 >>' stream, object 5 '[1 4 ...' cut by MaxObjectLength 1024): '/XRefStm NamedAgain 3003 CutByLimit=False NamesWhatPreviousReads=False ReadAs=/Prev@3021 NamesNoStream=null'. Diagnostics: limit.object only. WasRepaired=False before and after validation.

REVIEWER FIX:
End the sentence '..., before it said what it is or in a value its rows are read with.'

REFUTER REASON:
The finding holds. At b4ab948 the NamesWhatPreviousReads summary (src/AdCodicem.Pdf/IO/XRef/XRefSectionRecord.cs:263-264) lists only one guard-cut stop as false: the search near the offset the /XRefStm names "stopped only where a guard cut what the /Prev names, before it said what it is". In this codebase, "before it said what it is" means the case where the dictionary is cut. KeepsItsPlace's own doc (PdfFileReader.cs:3687-3689) draws the line: "its dictionary before it said what it is, or a value its rows are read with". KeepsItsPlace returns KeptPlace.Guard on check.CutByLimit, and CutByValue (PdfFileReader.cs:6468) sets that flag. So the Guard arm (PdfFileReader.cs:3496-3506) also covers a value cut. Its comment says so ("or cut a value its rows are read with"), and r3-fix-result.json's [10] deviation says it was meant ("Guard covers a dictionary cut before its /Type and also a cut value of a /W object (CutByValue)"). Every other text on the same rule names both cases: PdfDiagnosticCodes.cs:103, concepts/reader-limits.md:86-87, concepts/lazy-reading.md:131-132 and reference/reader-limits.md:168. The ReadAs and NamesNoStream docs are generic ("where a guard cut what that /Prev names"), so they cover both. The sentence comes from round 3 fix 2b26d18 (git blame), the commit that added the arm. This is only the doc's wording: the code, the reports and the tests are right, and nothing is blamed on the file.

REFUTER EVIDENCE:
I wrote my own probe on origin/ccr-6d6c0ee6-dqm4lm-188 (b4ab948), modeled on the GuardCutValueTests layout with no count guard. It is saved at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refuter-vc/ZzRefVcProbe.cs.txt and removed from the tree.

Layout:
- Object 5 = '[1 4' + 1524 spaces + ' 2]', so MaxObjectLength 1024 cuts the /W value.
- Stream 9 (/Type /XRef) at 2882, then object 8.
- Object 4 at 3021 = '<< /W 5 0 R /Size 3 /Length 30 >>' stream, with 30 bytes of data.
- Newest trailer: '/Prev 3021 /XRefStm 3003'.

Output, limits MaxObjectLength=1024, MaxTrailerLength=2048:
  startxref Read 4320 Kind=Table
  /XRefStm NamedAgain 3003 CutByLimit=False NWPR=False ReadAs=/Prev@3021 NNS=null
  /Prev Malformed 3021 Kind=Stream CutByLimit=True CutByValue=True
  DIAG limit.object: Object 5 runs past 1 KB ...
  WasRepaired=False
  FIND xref.checked-in-part: ... section at offset 3021 whole ...
  WasRepairedAfter=False

Control, the documented dictionary cut (object 4 = '<< /Size 3 /Filler (5000 y) /W [1 4 2] /Length 0 >>', MaxTrailerLength 2048): '/XRefStm NamedAgain 3003 NWPR=False ReadAs=/Prev@3021 NNS=null', '/Prev Malformed 3021 CutByLimit=True CutByValue=False', DIAG limit.trailer only, FIND xref.checked-in-part only. So the value cut takes the same path as the documented case, NWPR false included, and the summary does not name it.

A side note: when the cut falls after the last element ('[1 4 2' + spaces + ']'), the /W is read whole. The check then returns Read, KeepsItsPlace says Section, and the property is true. The omission is therefore only for a cut that hides a value the reading needs (CutByValue).

REFUTER FIX:
In XRefSectionRecord.cs:263-264, end the NamesWhatPreviousReads summary like PdfDiagnosticCodes.cs:103: '... or where the search near the offset the <c>/XRefStm</c> names stopped only where a guard cut what the <c>/Prev</c> names, before it said what it is or in a value its rows are read with.' This is a wording change only. It needs no maintainer decision and no test.

######## [25] lens docs — docs-reserved-report-says-which-rows
reviewer severity: nit; refuter: confirmed / nit; needs_maintainer: False
location: docs/website/docs/reference/diagnostics.md:117 ('which the message says, and which of the rows it counts are such'); src/AdCodicem.Pdf/IO/PdfFileReader.cs:4603-4604 (ReportReservedRows remarks); also 8ccd868's body ('or which of the rows they count do not')

CLAIM:
The texts say the xref.entry-reserved report tells which of the reserved rows it counts stand for nothing. Where only some do, the message names the first reserved row and says only 'the rows were read so but for those whose numbers were refused'. It neither says which rows nor how many. The row it names may itself be a refused one, and the message does not say so.

EVIDENCE:
CrossReferenceChainTests.A_row_of_a_reserved_type_whose_number_was_refused_before_it_is_said_to_stand_for_nothing, row ("[3 1]", 2, ...), passes on b4ab948 (13/13 with the validator twin). Its asserted message: '... gives 2 rows a reserved type: the row for object 3 gives it type 7, the first of them. ... (7.5.8.3): the rows were read so but for those whose numbers were refused before they were read, which stand for nothing ...'. Object 3's is the refused row (ReservedRowsRefused 1, ChainIndex lacks 3), and object 4's stands.

REVIEWER FIX:
Reword the docs and the remarks to what the message says: '... and, where only some do, that those whose numbers were refused stand for nothing'. Alternatively, make the message say whether the named row is one of them, or count them (', 1 of them refused before it was read').

REFUTER REASON:
The finding holds, and the same wording appears in two more places than it lists. The texts say the xref.entry-reserved report (and, by 8ccd868's body and validation-rules.md, the validator's xref.entry-type-reserved finding) tells which of the counted reserved rows stand for nothing. In the 'some' case neither message does that.

**What the messages say.** Both give a criterion only: 'but for those whose numbers were refused before they were read'. They give no count and name no row. The row they do name is simply the first reserved row read (ReservedRow ??= at PdfFileReader.cs:7204, whatever AddFromChain returned), and it may or may not be a refused one.

**Two files, one message.** Two files that refuse different rows produce byte-identical reports from both the reader and the validator. No other report settles it either: the lost stream's own xref.section-unreadable says 'the objects it numbers' and names no range. So the 'by criterion' reading, the convention round 2 used for the read-in-part tails, does not rescue the wording here. A reader of the diagnostics cannot resolve the criterion.

**Where the wording appears.**
- docs/website/docs/reference/diagnostics.md:117 ('which the message says, and which of the rows it counts are such')
- src/AdCodicem.Pdf/IO/PdfFileReader.cs:4603-4604 (ReportReservedRows remarks)
- docs/website/docs/reference/validation-rules.md:55 ('or which of the rows it did not'), not in the finding's location
- the test comment at tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:5013-5014, not in the finding's location
- 8ccd868's body ('or which of the rows they count do not'), already pushed

**What is fine.** lazy-reading.md:86-95 ('both reports say so'), PdfDiagnosticCodes.XRefEntryReserved, XRefSectionRecord.ReservedRow and ReservedRowsRefused do not overclaim. The behavior is right: refused rows are not indexed, and their objects are rebuilt.

**Severity.** Nit. Only the clause about what the message identifies is wrong. The behavior described, and the message's own statements, are true.

**Settled earlier?** No. Round 3's [5] asked for exactly this 'some' wording without identification, and the docs stage then described it as more than it is. No earlier finding settled this.

REFUTER EVIDENCE:
**Setup.** Probe ZzR4RefReservedWhichTests (uncommitted, removed after the run) on detached origin/ccr-6d6c0ee6-dqm4lm-188 at b4ab948, built Release with 0 errors. It uses ReservedRowFiles.PastARefusal(lost, reserved: 2, newest: ""): stream 7 at 347 gives objects 3 and 4 type 7, and lost stream 9 at 465 has /Index `lost`.

**lost=[3 1]** (object 3 refused, object 4 stands)
- `Warning xref.section-unreadable @465: The cross-reference stream /Prev names at offset 465 is there but cannot be read: its /W gives a field a negative width. The chain goes on through its /Prev; the objects it numbers that no section read before it indexes are looked for by rebuilding the index, rather than taken from an older section's rows.`
- `Warning xref.entry-reserved @347: The cross-reference stream /Prev names at offset 347 gives 2 rows a reserved type: the row for object 3 gives it type 7, the first of them. ISO 32000-1 defines types 0, 1 and 2, and reads any other as a reference to the null object (7.5.8.3): the rows were read so but for those whose numbers were refused before they were read, which stand for nothing and whose objects are looked for by rebuilding the index; no older section's row stands for the objects of the others.`
- Index: idx3=False, idx4=True. Section @347: reserved=2, refused=1.
- `F xref.entry-type-reserved: ... (7.5.8.3), as the reader did until it rebuilt the index by scanning the file, but for the rows whose numbers were refused before they were read; it now reads each object such a row numbers as the rebuild finds it.`

**lost=[4 1]** (object 3 stands, object 4 refused)
- The xref.section-unreadable, xref.entry-reserved and xref.entry-type-reserved texts are byte-identical to the ones above.
- Index: idx3=True, idx4=False. Section @347: reserved=2, refused=1.

So no report tells the two files apart, and in the [3 1] case the named row (object 3) is the refused one. The existing theory row ("[3 1]", 2, ...) in CrossReferenceChainTests.A_row_of_a_reserved_type_whose_number_was_refused_before_it_is_said_to_stand_for_nothing asserts that exact reader message.

**Code.** ReportReservedRows' last arm is a fixed string, with no count and no number. ReservedTypeRule's 'some' clause is fixed as well.

**The wording, found by grep for 'which of the rows':**
- PdfFileReader.cs:4604
- CrossReferenceChainTests.cs:5014
- diagnostics.md:117
- validation-rules.md:55 ('the finding says the reader did not read it so, or which of the rows it did not')

REFUTER FIX:
I recommend changing only the docs, so that they say what the messages say.

**Reader texts**
- diagnostics.md:117: '... and its object is found by rebuilding the index, which the message says: of the row, of all the rows it counts, or, where only some of them are such, of those whose numbers were refused, without naming them'.
- PdfFileReader.cs:4603-4604, the ReportReservedRows remarks: '... and the message says so rather than that it was read so: of every such row, or, where only some of the rows it counts are such, of those whose numbers were refused, which it does not name nor count'.
- CrossReferenceChainTests.cs:5013-5014, the test comment: the same.

**Validator text**
- validation-rules.md:55: '... the finding says the reader did not read it so, or, where only some were, that it did not read those whose numbers were refused, without naming them'.

**Commit body.** 8ccd868's body ('or which of the rows they count do not') is pushed and stays as it is. List it among the status.md 'Commit bodies not rewritten'.

**Optional, not needed.** The message could count the refused rows (', 1 of them refused before it was read', from ReservedRowsRefused) and say whether the named row is one of them (a flag set where ReservedRow is first assigned). That would change both messages and their tests. It is a wording choice, not a decision the maintainer must take.

######## [26] lens docs — docs-commit-2b26d18-body-overstates
reviewer severity: nit; refuter: confirmed / nit; needs_maintainer: False
location: commit 2b26d18, last paragraph of its body

CLAIM:
For the status.md list of commit bodies not rewritten: 2b26d18's last sentence says 'the texts on a stream taken by a /Prev's search say the check made: any /Prev the stream's dictionary gives'. That commit changed four of round 3's [13] five texts: the threat model, the diagnostics reference, and the PdfFileReader remarks and comment. It left lazy-reading.md:122-123, which b4ab948 reworded. The other five bodies of the round (4e42737, ea348ce, fb79663, 8ccd868, b4ab948) are true of their diffs, apart from the 'which of the rows' nit above. All six messages are conventional, with subjects of 73 to 92 characters, no issue number outside the footer, and no review key.

EVIDENCE:
git show 2b26d18:docs/website/docs/concepts/lazy-reading.md | grep -n 'names an older section, with no other section near' gives line 123: '  names an older section, with no other section near — (`xref.hybrid-stream-lost`,'. git show --stat 2b26d18 does not list lazy-reading.md, and git show --stat b4ab948 does (57 lines). Subject lengths: 92, 73, 83, 91, 84, 84. A grep of the bodies for B19, R[0-9]-, [n], P1b, P2, b2t, c9b, A-prime, N-DUP, N-TND, S[0-9] and M[0-9][0-9] finds nothing. #n occurs only on the Refs lines.

REVIEWER FIX:
Do not rewrite. Add 2b26d18 to status.md's 'Commit bodies not rewritten' with this note ('lazy-reading.md followed in b4ab948'), and say it right in the squash body.

REFUTER REASON:
The finding holds. The last paragraph of 2b26d18's body says "the texts on a stream taken by a /Prev's search say the check made: any /Prev the stream's dictionary gives". At 2b26d18 that was true of four of round 3's [13] texts: threat-model.md:417, reference/diagnostics.md:115, the PdfFileReader doc comments at :4593 and :4611, and the code comment at :4656. It was not true of concepts/lazy-reading.md:122-123, which 2b26d18 did not touch. That page still said "a stream whose own `/Prev` names an older section, with no other section near" until b4ab948 reworded it. The split is the round's own staging: R3a, in decisions.md, owned the code, the threat model and the reference, and R3c owned the concept pages. So at the branch head every text says the check made, and only the record of commit bodies is affected. That makes it a nit, not a defect.

The mechanical claims also hold:
- All six subjects are conventional, with lengths 92, 73, 83, 91, 84 and 84.
- No review key appears in any body.
- Issue numbers appear only on the Refs lines.
- 4e42737's body is true of its diff: it renames the test and the threat model cites the new name.

I did not re-audit the "which of the rows" nit the reviewer mentions. It belongs to another finding.

REFUTER EVIDENCE:
- `git log --format=%h 19122f0..origin/ccr-6d6c0ee6-dqm4lm-188 -- docs/website/docs/concepts/lazy-reading.md` gives only b4ab948.
- `git grep -n -i -e "names an older section, with no other" -e "names an older section and" -e "names an older section, which a section" -e "names an older section, and no other"` on each commit:
  - 19122f0: threat-model.md:417, lazy-reading.md:123, PdfFileReader.cs:4611 and :4656. PdfFileReader.cs:4593 is matched by a separate grep, since its line ends at "names an older".
  - 2b26d18: only lazy-reading.md:123, "names an older section, with no other section near — (`xref.hybrid-stream-lost`,".
  - b4ab948: nothing.
- `git show b4ab948 -- lazy-reading.md` deletes "…a classic table, or a stream whose own `/Prev`" and adds "…a classic table, or a stream whose own dictionary gives a `/Prev`, whatever it names, with no other".
- 2b26d18's diff rewords threat-model.md:417 to "only where its own dictionary gives a `/Prev`, whatever that `/Prev` names", reference/diagnostics.md:115 to "a cross-reference stream whose own dictionary gives a `/Prev`, whatever it names", and PdfFileReader.cs :4593, :4611 and :4656 to the same check.
- Subject lengths from awk: 84, 84, 91, 83, 73, 92 (b4ab948 back to 2b26d18).
- A grep of the six bodies for B19, R[0-9]-, [n], P1b, P2, b2t, c9b, A-prime, N-DUP, N-TND, S[0-9], M[0-9][0-9] and #[0-9] finds only the six Refs lines.
- decisions.md, round 3: "[13] reword the five texts to the check made (any /Prev the stream's dictionary gives)". Also: R3a is 2b26d18, and R3c is b4ab948 with "lazy-reading, diagnostics, validation-rules…".

REFUTER FIX:
Leave the pushed body as it is. In the batch's status.md record, add 2b26d18 to "Commit bodies not rewritten", next to 394028c, d63f093, 7c0748b, e67fef9, b09f981 and 5656c64, with the note: "its last paragraph says the texts on a stream taken by a /Prev's search give the check made; concepts/lazy-reading.md followed only in b4ab948". If the pull request is squashed, the squash body should state the rule once for all five texts. This is not a maintainer question.
