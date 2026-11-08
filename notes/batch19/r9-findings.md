######## [1] lens=own-stream key=passed-over-lost-section-false sev=major -> verdict=confirmed sev=major maint=?
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3707 (ReportStreamsPassedOver tests only _sectionsReadAt), with ReportLostSection at :3388/:4467 and the /XRefStm lost branch at :3608 recording nothing for the section they lose
CLAIM: xref.stream-passed-over says 'the chain did not read the stream' for a stream the chain then named and tried to read. The stream was named exactly through an older /Prev, or through an older /XRefStm for a stream that gives only /W, and was reported unreadable at the same offset. A lost section that hands nothing on never enters _sectionsReadAt, so the stream gets two reports that contradict each other. diagnostics.md:116 and threat-model.md:432 say the stream is one the chain never reads.
EVIDENCE: On da2b06f, the differential probe (ZzR9DiffTests) on /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9own/hand/lostprev-own-negwidth.pdf at default limits. This is the PrevFallingShortOfTheStreamBeforeTheTablesOwn("nothing", shortOfOwn: true) shape, with the oldest table giving /Prev <own> and stream 11 giving /W [1 -4 2]. Output: 'S /Prev 1802 1802 Stream Malformed', 'D Warning xref.section-unreadable 1802 The cross-reference stream /Prev names at offset 1802 is there but cannot be read: its /W gives a field a negative width...', 'D Warning xref.stream-passed-over 1802 ... the search near that offset took another section first, and the chain did not read the stream', 'F Error xref.section-malformed offset 1802'. lostprev-own-undecodable.pdf (/Filter /Foo) gives the same pair at 1802, with 'its data could not be decoded'. lostprev-own-notype-negwidth.pdf gives the same pair. lostxs-own-notype-negwidth.pdf (untyped, named by the oldest table's /XRefStm) gives 'S /XRefStm 1805 1805 Stream Malformed', section-unreadable 1805 and passed-over 1805. Controls with no passed-over Warning: lostprev-own-ok.pdf (stream read) and lostxs-own-negwidth.pdf (typed, recorded at :3610). Generator: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9own/hand9.py; output: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9own/out-hand-head.txt.
FIX: Have ReportStreamsPassedOver skip every stream the chain named and tried. Either record the start of a section lost through ReportLostSection (and of an untyped stream an /XRefStm lost) in the set it consults, or skip any `at` that a record of the chain holds at Offset+Padding with a state other than NotFound. Add rows: the oldest table's /Prev naming stream 11 with /W [1 -4 2], and with /Filter /Foo; and an untyped stream 11 named by the oldest table's /XRefStm. Each row expects xref.section-unreadable at the stream and no xref.stream-passed-over.
VERDICT REASON: The finding reproduces on da2b06f. I built the files with my own generator, not the reviewer's. ReportStreamsPassedOver (PdfFileReader.cs:3707) skips a passed-over stream only when its start is in _sectionsReadAt. Two paths reach the stream and report it unreadable without adding its start to that set:
- the /Prev branch that loses a section with no /Prev to hand on (:3388, ReportLostSection then break);
- the /XRefStm lost branch, which records only a stream that declares itself a section (:3608).
So the stream gets two Warnings at one offset. xref.section-unreadable says it "is there but cannot be read" and that its objects are looked for by rebuilding the index; the index is marked incomplete and is rebuilt. xref.stream-passed-over then says "the chain did not read the stream" and that its objects "are missing from the index". The XML summary of XRefStreamPassedOver, diagnostics.md:116 and threat-model.md:432 add that nothing is rebuilt for them.

The Warning is false here against the code's own rule:
- The ReportStreamsPassedOver remark says a link that names the stream after the search "reads it, and the chain reports it as it finds it, if at all".
- The test A_stream_the_search_near_a_hybrid_table_s_prev_comes_back_to_and_takes_is_not_reported_as_passed_over_though_it_cannot_be_read pins that an unreadable stream the chain reaches is not passed over.
- The same unreadable stream gets no Warning when it hands on a /Prev (RecordSectionReached before HandOnPrevious), or when an /XRefStm names it and it gives /Type /XRef. So whether the Warning appears depends on the lost stream's /Prev and /Type, not on whether the chain reached it.

No object is served wrong: the section-unreadable report already forces the rebuild. The defect is a wrong, contradictory extra report, which this rubric and round 1's precedent on doubled reports class as major.

None of the brief's accepted items covers it. The accepted point about the passed-over Warning not marking the index incomplete is a different one. The Warning is new in 9a07bb9, so 012e13d and main do not have it, and no earlier round or debt holds it.

This is not a decision for the maintainer: the fix brings the code into line with its remark, its test and its pages.
VERDICT EVIDENCE: Rebuilt independently with my generator /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9refute-pol/gen.py. The files are in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9refute-pol/files. The probe was an uncommitted ZzRefutePolTests, now removed, run on da2b06f at default limits. Output: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9refute-pol/out-head.txt.

The shape: the oldest table at 184; stream 6 at 1592, which the newest table's /XRefStm names; stream 11 at 1781, which gives no /Prev; the newest table's /Prev is 1774, 7 bytes short of stream 11.

- a-prev-typed-negw: the oldest table's /Prev names 1781; stream 11 gives /Type /XRef and /W [1 -4 2].
  'S /Prev 1781 1781 Stream Malformed'
  'D Warning xref.section-unreadable 1781 The cross-reference stream /Prev names at offset 1781 is there but cannot be read: its /W gives a field a negative width. ... looked for by rebuilding the index.'
  'D Warning xref.stream-passed-over 1781 ... the search near that offset took another section first, and the chain did not read the stream...'
  WasRepaired: False at opening, True after the object reads.
- b-prev-typed-foo (/Filter /Foo): the same pair at 1781, with 'its data could not be decoded'.
- c-prev-untyped-negw: the same pair at 1781.
- d-xs-untyped-negw: the oldest table's /XRefStm names an untyped stream 11. 'S /XRefStm 1784 1784 Stream Malformed', then section-unreadable 1784 and passed-over 1784.
- j-xs-untyped-foo: the same pair at 1784.

Controls with no passed-over Warning:
- f-prev-typed-ok: stream 11 is read.
- e-xs-typed-negw: typed, named by /XRefStm, recorded at :3608.
- g-prev-typed-negw-tableprev: the same unreadable stream 11, but it gives /Prev 1774. It is handed on and recorded, and gets section-unreadable 1781 ('The chain goes on through its /Prev...') and chain-cycle, no passed-over.

The Warning is correctly made where the chain never names the stream (h-none-typed-negw, i-none-typed-ok: passed-over at 1764 only).

Code:
- :3388 ReportLostSection(section, naming, offset, namedFrom); break; records nothing.
- :3603-3608 records a lost /XRefStm stream only if `!stream.CutByLimit && DeclaresItselfASection(stream)`.
- :3707 `if (_sectionsReadAt.Contains(at) || !reported.Add(at)) continue;`

The reviewer's own run, /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9own/out-hand-head.txt, shows the same at 1802 and 1805.
VERDICT FIX: 1. Keep a set of the starts of sections the chain reached and reported unreadable, apart from _sectionsReadAt. Add Offset + Padding to it at the two ReportLostSection sites when the section is there: State Malformed and not CutByLimit, i.e. where xref.section-unreadable is made. Those sites are :3388 and the /XRefStm lost branch at :3603, whatever the stream's /Type.
2. Have ReportStreamsPassedOver skip an `at` held in that set. Do not add these starts to _sectionsReadAt: that set drives loop detection and the #263 rule for an untyped object. The equivalent alternative is to skip any `at` that a chain record holds at Offset + Padding in state Read or Malformed.
3. Add rows to the CrossReferenceChainTests family on HybridFiles.PrevFallingShortOfTheStreamBeforeTheTablesOwn. In each, the oldest table names stream 11, which gives no /Prev:
   - through /Prev, with /W [1 -4 2];
   - through /Prev, with /Filter /Foo;
   - through /Prev, untyped, with /W [1 -4 2];
   - through /XRefStm, untyped, with /W [1 -4 2].
   Each row expects one xref.section-unreadable at the stream and no xref.stream-passed-over. Keep the typed /XRefStm row and the handed-on row as controls.
4. Optionally add a clause to the XRefStreamPassedOver summary, diagnostics.md:116 and threat-model.md:432: a stream the chain reached and reported as unreadable is reported as that, not as passed over.
5. Run ADR 53 over the new lines.
VERDICT NEEDS_MAINTAINER: False

######## [2] lens=own-stream key=passed-over-under-guard sev=major -> verdict=narrowed sev=major maint=?
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3679 and :3696-3719 (ReportStreamsPassedOver runs whatever guard ended the chain); docs/website/docs/reference/diagnostics.md:116
CLAIM: The Warning is made when a guard ended the chain before the older section that names the stream would have read it. MaxXRefSectionCount and MaxIndexEntries both do this. With the guard lifted, the chain reads the stream and nothing is reported. A guard's cut becomes a file fault ('If it is that table's own stream, the objects only it indexes are missing'), against ADR 34 and invariant 12. diagnostics.md:116 itself says 'the chain, read to its end, did not read the stream'. The codebase already withdraws reports that a guard keeps from being confirmed: namedWithPrevious at the MaxXRefSectionCount cut, :3297-3301.
EVIDENCE: On da2b06f, /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9own/hand/guard-own-xrefstm.pdf. This is HybridFiles.PrevFallingShortOfTheStreamBeforeTheTablesOwn("nothing", shortOfOwn: true, oldestNamesOwn: true): the newest table's /Prev falls 7 bytes short of stream 11, and the oldest table names stream 11 through /XRefStm. At default limits and at MaxXRefSectionCount 3, the sections end '/Prev 205 205 Table Read', '/XRefStm 1805 1805 Stream Read', with no passed-over Warning. At MaxXRefSectionCount 2: 'D Warning limit.xref-section-count 205 The cross-reference chain has more than 2 sections; the older ones were not read...', then 'D Warning xref.stream-passed-over 1805 ... the chain did not read the stream...', then 'D Repair xref.rebuilt'. At MaxIndexEntries 6: 'limit.index-entries 205' and the same passed-over Warning at 1805.
FIX: Skip the report where the chain ended at a guard (_cutByGuard, _indexFullAt >= 0, or TrailersHiddenByGuard): an older section the guard hid may read the stream, and xref.checked-in-part already says those sections were not checked. Add a row to the passed-over tests: count 2 gives no passed-over Warning, and count 3 or default reads the stream through the oldest /XRefStm. State the rule in diagnostics.md:116, threat-model.md:432 and lazy-reading.md:207-209.
VERDICT REASON: The finding holds, and I reproduced it myself on da2b06f. ReportStreamsPassedOver (src/AdCodicem.Pdf/IO/PdfFileReader.cs:3679, :3696-3719) checks only `_sectionsReadAt` and `_xref.Count == 0`. So it reports any stream passed over that the chain did not read, even when a guard ended the chain before the section that would have read it. With the guard lifted, the stream is read and nothing is reported.

The texts say the report needs the chain read to its end, so on this point they misstate the code: the PdfDiagnosticCodes.XRefStreamPassedOver summary ("the chain, read to its end, did not read the stream"), the ReportStreamsPassedOver summary and remarks, diagnostics.md:116, threat-model.md:432 and lazy-reading.md:207-209.

Under ADR 34 ('Reaching a guard', 'What a guard leaves out of the index is not damage'), objects a guard leaves out are unknown, not missing. The Warning calls them missing and blames the search near the offset. The codebase already withdraws a report like this at a guard's cut (namedWithPrevious, :3294-3301). No decision covers the case: the invariant-5 settlement says 'never reads', and the accepted list has only 'not marking the index incomplete'. No test pins the Warning under a guard. On 012e13d the Warning does not exist (out-hand-012), so 9a07bb9 introduced this.

Narrowed on four points:
(1) Invariant 12 is not broken. A valid file never reaches the search near an offset; the breach is ADR 34's rule only.
(2) The MaxXRefSectionCount instance needs an older section that names a stream at or after S. That is the builder's shape (the first revision's table names a later revision's stream), which no append-only writer makes. The MaxIndexEntries instance also arises in a more natural shape, built below: a stale /XRefStm in the newest trailer naming the first revision's stream, and a /Prev falling 5 bytes short of the previous revision's own stream. The index fills at the taken table's rows, so that table's own /XRefStm, which names the stream passed over, is never read. Guards that stop the taken table's rows (MaxXRefSectionLength, MaxDecodedStreamLength) end the chain the same way. I did not probe them.
(3) The impact is one extra Warning beside the guard's own report. ThrowOnLimit still throws at the cut, no object is served differently, there is no Error and no validator rule.
(4) It stays major under the brief's scale and this batch's precedents: a wrong report that charges a guard's cut to the file.
VERDICT EVIDENCE: The probe is /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9ref-pog/ZzRefPassedOverGuardTests.cs.txt. I ran it on da2b06f (Release, uncommitted, now removed). Its output is /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9ref-pog/out.txt.

Builder shape, HybridFiles.PrevFallingShortOfTheStreamBeforeTheTablesOwn("nothing", shortOfOwn: true, oldestNamesOwn: true). Offsets: oldest 292, stream 1703, own 1892, newest 2609.
- default and MaxXRefSectionCount 3: sections end '/Prev 292 Table Read', '/XRefStm 1892 Stream Read'. Diagnostics are only offset-adjusted and hybrid-stream-lost. Object 10 "ten". Not repaired.
- MaxXRefSectionCount 2: 'Warning limit.xref-section-count 292 The cross-reference chain has more than 2 sections...', then 'Warning xref.stream-passed-over 1892 The cross-reference stream at offset 1892, near offset 1885, which the /Prev of the cross-reference table at offset 2609 names, gives no /Prev...', then 'Repair xref.rebuilt -1'. With ThrowOnLimit, Open throws PdfLimitExceededException.
- MaxIndexEntries 6 and 7: 'limit.index-entries 292' and the same passed-over Warning at 1892. Object 10 null.

Natural shape (four revisions). Rev1 has stream X1 at 133 and table T1. Rev2: X2 at 1607, then T2 at 1722 with /XRefStm X2. Rev3: X3 and T3. T4 at 2741 with /XRefStm 133 (stale) and /Prev 1602 (5 bytes short of X2).
- default, MaxXRefSectionCount 2, MaxIndexEntries 6 to 9: '/Prev 1722 Table Relocated', '/XRefStm 1607 Stream Read'. No passed-over Warning.
- MaxIndexEntries 4: sections end at '/Prev 1722 Table Relocated'. 'Warning limit.index-entries 1722 The index reached 4 entries...', then 'Repair xref.offset-adjusted 1722', then 'Warning xref.stream-passed-over 1607 The cross-reference stream at offset 1607, near offset 1602, which the /Prev of the cross-reference table at offset 2741 names, gives no /Prev...'. MaxIndexEntries 5 gives the same.

X2 is T2's own stream and T2 names it. It goes unread only because the guard stopped T2's rows before its /XRefStm.

Control, the same builder without oldestNamesOwn (nothing names stream 11): the Warning appears both at default and at count 2. So the fix below also drops it under a guard there, which xref.checked-in-part already covers.
VERDICT FIX: In TryReadXRefChain, call ReportStreamsPassedOver only where no guard ended the chain. This covers the section count (`_cutByGuard` at :3300), a trailer cut, rows cut by MaxXRefSectionLength, MaxDecodedStreamLength or MaxStreamLength, a full index (`_indexFullAt >= 0`) and a value cut. Check that each of these sets the flag tested; `_cutByGuard || _indexFullAt >= 0` may need the rows-cut paths added. xref.checked-in-part already says the older sections were not checked, and ADR 34 makes what they hide unknown rather than missing.

Tests:
- the builder shape at MaxXRefSectionCount 2 gives no passed-over Warning, and at 3 or default reads stream 11 through the oldest /XRefStm;
- the natural stale-/XRefStm shape at MaxIndexEntries 4 gives no Warning, and at 6 or default reads X2 through T2's /XRefStm;
- the control shape at count 2 has no Warning, while at default it keeps it.

Texts: add the guard exception to the XRefStreamPassedOver summary, the ReportStreamsPassedOver summary and remarks, diagnostics.md:116, threat-model.md:432 and lazy-reading.md:207-209.

No maintainer question is needed, since ADR 34 decides the case. Rewording the Warning as the guard's, the way NamesNoStream does, is the only alternative, and it is weaker.
VERDICT NEEDS_MAINTAINER: False

######## [3] lens=own-stream key=passed-over-message-after-rebuild sev=minor -> verdict=confirmed sev=minor maint=?
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3716 (message) and :3679 (report made inside TryReadXRefChain, before Initialize's RecoverCatalog rebuild at :2721-2735); src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:121-123
CLAIM: The message ends 'If it is that table's own stream, the objects only it indexes are missing from the index.' That is false when the chain ends without the catalog and the document is rebuilt as it opens: by the time the caller sees the Warning, the rebuilt index holds those objects. This happens often, not at the edges. The summary concedes that a rebuild for another cause finds them, but the message states the loss outright.
EVIDENCE: Seeded differential with the new seed base 202611080326 (1,500 files). At default limits, 17 of the 33 blocks with xref.stream-passed-over also have xref.rebuilt at opening. Example: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9own/files9/h202611081068.pdf at default limits on da2b06f gives 'R open True', 'D Warning xref.chain-cycle 1387 ...', 'D Warning xref.stream-passed-over 1026 ... If it is that table's own stream, the objects only it indexes are missing from the index.', then 'D Repair xref.rebuilt -1 The cross-reference index was rebuilt by scanning the file.' Output: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9own/out9-head.txt.
FIX: Keep the record and make the report in Initialize after the catalog check, skipping it when the index was rebuilt (_repaired). Otherwise, word the last sentence so it is true either way, for example 'the index the chain read lacks the objects only it indexes'. Add a row where the chain ends without the catalog.
VERDICT REASON: The finding holds. In the message made at src/AdCodicem.Pdf/IO/PdfFileReader.cs:3716 the last sentence states the loss with no condition: 'If it is that table's own stream, the objects only it indexes are missing from the index.' The report is made inside TryReadXRefChain (:3679), and that is before Initialize can rebuild the index for a missing catalog (:2721-2735, RecoverCatalog, then Repair). By the time the caller reads document.Diagnostics, the rebuilt index already serves those objects.

The code's summary (PdfDiagnosticCodes.cs:117-123) gets this right: 'only a rebuild for another cause, which scans the file, finds them'. So do lazy-reading.md:208 and diagnostics.md:116. R8b's deviation note also says a rebuild at opening can follow. Only the message gets it wrong, and diagnostics.md quotes that message. The brief accepts the Warning not marking the index incomplete, but it accepts no misstated message.

The finding is a little wider than the reviewer says. When the index is incomplete for another cause, asking for one of those objects rebuilds the index and finds it, so the sentence is false after any later read too. A chain cycle sets _indexIncomplete (:3241), and that is enough.

It is frequent. In the reviewer's 1,500-file run, 17 of the 33 default-limit blocks with the Warning are rebuilt at opening and 7 more are rebuilt on a later read. In the prototype's final run (out-r8f.txt), it is 62 of the 162 blocks with the Warning at opening and 45 more later.

Two parts of the proposed fix do not hold:
- A row where the chain ends without the catalog already exists. CrossReferenceChainTests.cs:5550-5580 pins the passed-over Warning followed by xref.rebuilt and object.redefined, with WasRepaired true. Only the message is not pinned there.
- Moving the report after the catalog check and skipping it when the index was rebuilt fixes only the rebuild at opening. A rebuild on a later read, which an incomplete index allows, still makes the sentence false. That approach also changes the order of the reports.

The wording fix covers every case. It is a misleading text, not a wrong behavior, so the severity is minor.
VERDICT EVIDENCE: I rebuilt the probe on da2b06f (Release build, probe not committed, now removed) with the reviewer's file /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9own/files9/h202611081068.pdf. Output is in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9own/refute-passed-over-rebuild.txt.

At default limits, PdfDocument.Open gives:
- WasRepaired at open: True
- D Repair xref.offset-adjusted 1356
- D Warning xref.chain-cycle 1387
- D Warning xref.stream-passed-over 1026 'The cross-reference stream at offset 1026, near offset 1387, which the /Prev of the cross-reference table at offset 1914 names, ... If it is that table's own stream, the objects only it indexes are missing from the index.'
- D Repair xref.rebuilt -1
- D Information object.redefined -1

Reading the objects afterwards:
- 'O 10 obj 10 r2'. Object 10 is indexed only by the passed-over stream 12 at 1026, whose /Index is [10 1 12 1]. Neither the table (rows 0, 3, 20, 21, 11) nor streams 13 and 11 index it.
- 'O 12 PdfStream'.
- 0 diagnostics added by these reads.

The file loses its catalog because object 1 is indexed only by the first revision's stream at 193, which the chain never reaches.

Contrast: the same file with object 1 added to the table (first subsection '0 2').
- 'contrast WasRepaired at open: False', with the same passed-over Warning and message.
- Then 'CO 10 obj 10 r2', and 'contrast WasRepaired after reads: True'. The chain cycle had marked the index incomplete (PdfFileReader.cs:3241), so asking for object 10 rebuilt the index and found it.

Counts:
- Reviewer's out9-head.txt, recounted at default limits: 33 blocks with the Warning, 17 with xref.rebuilt at opening, 7 more rebuilt by later reads.
- r8proto/out-r8f.txt: 162 blocks with the Warning, 62 rebuilt at opening, 45 rebuilt later.

The existing row at CrossReferenceChainTests.cs:5550-5580 already pins the Warning followed by xref.rebuilt at opening. Its comment reads 'the index is rebuilt as the document opens'.
VERDICT FIX: Reword the message's last sentence at PdfFileReader.cs:3716 so it holds whether or not a rebuild follows, in line with the summary and the pages. For example: 'If it is that table's own stream, the index the chain read lacks the objects only it indexes, which only a rebuild of the index for another cause finds.'

Then update:
- the two tests that pin the message, at CrossReferenceChainTests.cs:5531 and :5995;
- the message quoted in docs/website/docs/reference/diagnostics.md:116;
- the row at :5550 (passed over, then rebuilt at opening), which should also pin the message so the wording is checked where a rebuild follows.

Optional, as a design choice the batch can make under its existing rule, with no question to the maintainer: also skip the report when Initialize's catalog check rebuilds the index. This is the same reasoning as 'not made when the chain indexed nothing, the whole file being rebuilt then'. It does not replace the rewording, because a later rebuild of an incomplete index still finds the objects.
VERDICT NEEDS_MAINTAINER: False

######## [4] lens=own-stream key=s-judgment-leaves-guard sev=minor -> verdict=confirmed sev=minor maint=?
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:5054-5063 (S judged through MayBeTablesOwn/StreamPrevious outside any attempt, with no ForgetReadThrough); remarks :4933-4934; docs/threat-model.md:440
CLAIM: Judging the stream the /XRefStm read (S) reads the objects its /Prev reference leads to outside any attempt. An object a guard cut, whose value is a whole reference, then stays in _chainValuesCut. It is loaded once the chain is read, which reports limit.object, and under ThrowOnLimit the open throws, although the chain never follows S's /Prev: S is put back, another place is taken, and S is not retried. The same /Prev on any other passed-over stream is forgotten with its attempt. The remark says 'nothing loaded or reported'. threat-model.md:440 says such an object is loaded 'only for a value the chain keeps ... not for a place it guessed and dropped'.
EVIDENCE: On da2b06f, /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9own/hand/big-S.pdf. S is stream 6 at 308, with /Prev 20 0 R; object 20 is '21 0 R' followed by a 120-byte comment; object 21 holds 301, the table's /Prev; the oldest table at 143 lies in the window. At MaxObjectLength 64: 'S /Prev 301 143 Table Relocated', 'D Warning limit.object 597 Object 20 runs past 64 bytes...'. At MaxObjectLength 64 with ThrowOnLimit: 'T open PdfLimitExceededException Object 20 runs past 64 bytes...'. /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9own/hand/big-X.pdf puts the same /Prev on stream 11, past S, which is passed over: no limit.object at opening, and it opens under ThrowOnLimit. On 012e13d, big-S took S and followed its /Prev (a loop), so the guard was the chain's there.
FIX: Take _chainValuesCut?.Count before the takeAt branch, and ForgetReadThrough to it when S is put back or dropped, as the attempt path does. S's retry, if it comes, reads the /Prev again. Add a GuardCutValueTests row: S's /Prev reaching a cut object holding a whole reference, another section taken, and no limit.object or throw at open.
VERDICT REASON: The finding holds, and the mechanism is the one claimed. In the takeAt branch (src/AdCodicem.Pdf/IO/PdfFileReader.cs:5054-5063), MayBeTablesOwn and StreamPrevious read S's /Prev through ReadWithoutLoading outside any attempt, and no mark is taken. Object 20 is cut by MaxObjectLength and its value is a whole reference, so ReadWithoutLoading adds it to _chainValuesCut (:1020). Nothing removes it when S is put back and never retried, and ReachChainValuesCut then loads it at opening.

Every other look in the search forgets what it reads through: the attempt path calls ForgetReadThrough(readThrough) at :5097/:5141, and KeepsItsPlace's check does the same at :3850-3852. So whether the guard is reached depends on whether the stream being judged is S or another stream. The /XRefStm's own reading of S does not read S's /Prev (:6330 and :6662 skip it for NamedBy "/XRefStm"), so the judgement is the first and only read of object 20.

Three texts misstate this:
- the remark at :4933-4934 ("nothing loaded or reported");
- the _chainValuesCut doc at :385-388 ("but for an object it read at a place the section may have been moved to, and refused");
- docs/threat-model.md:440 ("loaded ... only for a value the chain keeps ... not for a place it guessed and dropped").

None of decisions.md, r1- to r8-findings.md, r8-fix-result.json or the known issues settles this; the brief's accepted MaxTrailerLength throw is a different cut.

Narrowing: in effect this is not worse than main or 012e13d on this file. Both took S, followed its /Prev into object 20 and threw at opening as well. The defect is the inconsistency with the other looks and the false texts, not a regression, so minor stands.

By reading only, with no file built for it: the same leak also follows when S's /Prev reads through such a cut object to another offset and MaxRelocationCandidates stops S being read (the continue at :5082-5090 comes after the judgement and before any mark).
VERDICT EVIDENCE: My independent generator, /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9ref-sjudge/gen.py, rebuilds the reviewer's big-S.pdf byte for byte (cmp identical). I ran it through a console probe that references each build's AdCodicem.Pdf.dll.

On da2b06f:
- MaxObjectLength 64: 'S /Prev 301 143 Table Relocated', then 'D Warning limit.object 597 Object 20 runs past 64 bytes, its stream data aside; ...'.
- With ThrowOnLimit added: 'T open PdfLimitExceededException: Object 20 runs past 64 bytes ...'.
- big-X.pdf (the same /Prev 20 0 R on stream 11, judged in the attempt path) under object64 and object64throw: it opens, no limit.object, one 'xref.stream-passed-over 469'.

On main 21a0298 (the dll in lot19/r8bat/main; its PdfFileReader.cs is identical to 21a0298 and no source is newer than the dll), big-S:
- object64: 'S /Prev 301 308 Stream Relocated', xref.chain-cycle, 'limit.object 597', xref.rebuilt.
- object64throw: 'T open PdfLimitExceededException: Object 20 ...'. 012e13d also threw (reviewer's out-hand-012.txt).

Prototype fix (saved as /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9ref-sjudge/fix.diff, then reverted):
- big-S under object64: only 'xref.offset-adjusted 143', no limit.object. Under object64throw it opens.
- Control retry-S.pdf (the oldest table padded out of the window and no stream 11, so S is deferred, retried and taken, and the chain follows its /Prev): head and the fix both report 'limit.object 1165' at opening and throw under ThrowOnLimit, so the guard is still reached when the chain keeps the value.
- With the fix, GuardCutValueTests, HostileInputTests, CrossReferenceChainTests, ReaderLimitsTests and CrossReferenceRuleTests pass: 2,017 of 2,017.
VERDICT FIX: In TryRelocateXRefSection, take the mark `var readThrough = _chainValuesCut?.Count ?? 0;` at the top of each iteration, before the takeAt branch, and use that one mark for the attempt (drop the second declaration at :5097). Call ForgetReadThrough(readThrough):
- in the takeAt branch, after the StreamPrevious check and before `continue`, so S is forgotten when put back or dropped;
- in the bound's `continue`.

The attempt's existing ForgetReadThrough then also covers a judgement that let S through and was then dropped. S's retry, when it comes, reads the /Prev again inside its own attempt, so a value the chain keeps still reaches the guard (the retry-S control shows it).

Tests: add a GuardCutValueTests (or HostileInputTests) row in the big-S shape (S's /Prev reaches a cut object holding a whole reference, another section taken: no limit.object at opening, opens under ThrowOnLimit). Add its retry-S control (S taken: limit.object at opening, throws).

Texts: the remark and threat-model.md:440 then hold. While there, threat-model.md:439 ('A hybrid file's stream's own /Prev ... is not read at all') should say it is read only to tell S's place near a hybrid /Prev, nothing kept or reported.

Not a maintainer choice: the existing principle decides, since KeepsItsPlace's check and the attempt path already forget what they read through.
VERDICT NEEDS_MAINTAINER: False

######## [5] lens=own-stream key=h3-not-equivalent sev=minor -> verdict=confirmed sev=minor maint=?
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:7279; tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs (A_prev_falling_inside_the_stream_its_xrefstm_read_takes_it_only_where_the_stream_s_own_prev_names_another_offset has no row at the chain-length bound)
CLAIM: Mutant H3 (`var read = given;`) is not equivalent, contrary to the stage's ADR 53 record. The explicit ReadWithoutLoading adds one link before AsInteger's Resolve, which follows 32 GetObject calls and checks 31 further values. So a /Prev written as a chain of 32 objects, the last holding the offset, resolves as ReadSectionOffset resolves it. Under H3 it reads as no offset, the stream is taken in its nearest-first place, and the chain loops. No test reaches the bound.
EVIDENCE: With H3 applied to da2b06f, CrossReferenceChainTests, HostileInputTests, GuardCutValueTests and WindowEdgeTests pass: 1,282 total, 0 failed. Across my 9,000 seeded blocks, 22 differ from head (16 worse, 6 wording+codes). All 6 files concerned have a 32-object /Prev chain. Example: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9own/files9/h202611080817.pdf at default limits. Head: '/Prev 792 474 Stream Relocated' and xref.stream-passed-over 767. Mutant: '/Prev 792 767 Stream Relocated', 'D Warning xref.chain-cycle 792', 'D Repair xref.rebuilt', 'F Error xref.chain-loop offset 767'. Outputs: out9-head.txt and out9-mutH3.txt, classification cls-mutH3.tsv, in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9own/.
FIX: Add rows 'the table's /Prev, through a chain of 32 objects' (deferred, which kills H3) and 'through a chain of 33 objects' (another offset, as ReadSectionOffset reads it). Correct the tally in decisions.md and r8-fix-result to 87 mutants: 79 killed, 5 equivalent, 3 not viable.
VERDICT REASON: The finding holds. At da2b06f, StreamPrevious (src/AdCodicem.Pdf/IO/PdfFileReader.cs:7279) reads the first object with ReadWithoutLoading, then AsInteger's Resolve checks up to 32 values (v1..v32). That matches ReadSectionOffset (:7621), so a /Prev written as a chain of 32 objects reads as an offset. Under H3 (`var read = given;`), Resolve spends its first iteration on the reference itself and checks only v1..v31. It loads object 32 and drops its value, so the same /Prev reads as no offset in the file. The stage's equivalence reason ("the explicit first read ... reads the same") misses that one link of Resolve's MaxChainLength is used up, which shifts the bound by one. H3 is therefore a live survivor, not an equivalent mutant: the ADR 53 record in r8-fix-result.json and decisions.md:576 is false, and no test pins the bound. The behavior at head is right, as decided (R8-Q2 'as the chain reads it'), so the defect is the test gap and the false record, not the code. Nothing in decisions.md, r1-r8 findings, residuals-impl.md or the listed issues already holds it. Two small corrections to the reviewer's fix: (1) a 33-object row kills no catalog mutant. It is optional, and its outcome is exactly the 'a name' row of the sibling theory A_prev_falling_inside_the_stream_its_xrefstm_read_takes_it_where_the_stream_s_own_prev_names_no_offset_in_the_file, so it belongs there, not in the theory the finding names. (2) The same off-by-one is in the scratchpad record of 7ff4f39's cost ('one cached read per link, at most PdfReference.MaxChainLength - 1'). Past the first read, Resolve makes up to MaxChainLength reads (objects 2 to 33, the last dropped), 33 reads in all. The cost is still bounded, so only the record is wrong.
VERDICT EVIDENCE: Independent reproducer: the file shape of HybridFiles.PrevFallingInsideTheTablesOwnStream, with stream 6's /Prev written as 20 0 R and objects 20..20+n-1 each naming the next. The last object holds either the table's /Prev (stream+12 = 1797) or the table before (1645), and all are indexed by the newest table. Probe: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9refH3/ZzRefH3ChainTests.cs.txt. Outputs: head.txt and mutH3.txt in the same folder. Both runs are at default limits with n = 1, 2, 30, 31, 32, 33, 34.

At da2b06f, n=32, last object holding the table's /Prev: 'S /XRefStm 1785 Stream Read', 'S /Prev 1797 1645 Table Relocated', 'S /Prev 247 Table Read', 'D Repair xref.offset-adjusted 1645 | ... found 152 bytes before it.', WasRepaired False, no Error findings. This is the same as n=1, 2, 30 and 31. At n=33 head reads the /Prev as no offset, as ReadSectionOffset does: the stream is taken, 'S /Prev -1 Unknown NotFound', codes section-missing, offset-adjusted, hybrid-stream-lost, rebuilt, redefined.

With H3 applied to da2b06f, only n=32 with the table's /Prev differs from head: 'S /XRefStm 1785 Unknown NamedAgain', 'S /Prev 1797 1785 Stream Relocated', 'D Warning xref.hybrid-stream-lost 1785', 'D Warning xref.chain-cycle 1797 | ... the /Prev of the section at offset 1785 names offset 1797, which the chain has already read.', 'D Repair xref.rebuilt', 'D Information object.redefined', WasRepaired True. Validator: 'F Error xref.chain-loop' and 'F Error xref.hybrid-stream-wrong'. All other rows are identical.

The whole unit project (tests/AdCodicem.Pdf.Tests) under H3: total 7798, failed 0, succeeded 7778, skipped 20 (log: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9refH3/all-mutH3.log). So no test kills H3.

The stage's record (r8-fix-result.json, reports[1]) reads: 'H3 (var read = given): AsInteger resolves a reference through GetObject, which reads without loading while the chain is read, so the explicit first read, kept to mirror ReadSectionOffset, reads the same.' Its tally is 87 mutants, 78 killed, 6 equivalent, 3 not viable.
VERDICT FIX: Add a row 'the table's /Prev, through a chain of 32 objects' to CrossReferenceChainTests.A_prev_falling_inside_the_stream_its_xrefstm_read_takes_it_only_where_the_stream_s_own_prev_names_another_offset. Do it by letting HybridFiles.PrevFallingInsideTheTablesOwnStream write objects 20..51, each naming the next and the last holding stream+12, with /Size 52 and a newest-table subsection '20 32'. The row expects the stream deferred and the table before taken, which kills H3. Optionally, add 'through a chain of 33 objects' to the sibling theory ..._takes_it_where_the_stream_s_own_prev_names_no_offset_in_the_file: it expects what the 'a name' row expects (section-missing, offset-adjusted, hybrid-stream-lost, rebuilt, redefined), and pins the bound's other side as ReadSectionOffset reads it. Update the builder's XML summary for the new value or values. Correct the R8b ADR 53 record in decisions.md and the end-of-batch record to 87 mutants: 79 killed (H3 by the new row), 5 equivalent (A4, B7, C22, G4, H12), 3 not viable. Also correct 7ff4f39's cost line to 'up to PdfReference.MaxChainLength reads past the first, the last dropped'. No source change and no maintainer decision needed.
VERDICT NEEDS_MAINTAINER: False

######## [6] lens=own-stream key=passed-over-in-stream-data sev=minor -> verdict=narrowed sev=minor maint=?
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:5129-5132 (record) and :3712; docs/threat-model.md:457-475 (the #171 entry)
CLAIM: A cross-reference stream that an attached file's data quotes, lying near a hybrid table's /Prev past S and giving no /Prev, is passed over as possibly the table's own. It is then reported as xref.stream-passed-over, which says the table's own stream's objects may be missing. This is a new consequence in a #171 context, and the #171 entry and comments do not name it: they name only a section in stream data being taken.
EVIDENCE: On da2b06f, /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9own/hand/quoted-in-data.pdf (generator quoted9.py) at default limits. '12 0 obj << /Type /XRef ... >> stream' sits at 1738, inside object 7's /EmbeddedFile data; the newest table's /Prev is 1731 and its /XRefStm is stream 6 at 1510. Output: 'S /Prev 1731 1510 Stream Relocated', 'D Warning xref.stream-passed-over 1738 The cross-reference stream at offset 1738, near offset 1731, ... the chain did not read the stream...'.
FIX: Name this consequence in the #171 threat-model entry and in a comment on #171: telling stream data apart, which closes #171, would drop it. No code change in this batch.
VERDICT REASON: The finding holds. I rebuilt it on da2b06f and got the reviewer's output exactly. 9a07bb9 adds a consequence in a #171 context that no text names. Near a hybrid table's /Prev, a cross-reference stream quoted inside an attached file's data is now passed over as possibly the table's own: it starts at or after S, gives no /Prev, and another place reads first. It then draws a Warning xref.stream-passed-over at a place that holds no section of the file.

Every text that lists what the search does with a readable section in stream data says only that it "is taken" and that the chain goes on through its trailer: the threat-model #171 entry (:468-472), lazy-reading.md:230-232, and the source comment at PdfFileReader.cs:5109-5111. The last #171 comment (6708779926) was written before R8. Its closing condition, "the stream-data check asked of every place the search takes", would leave this Warning in place: a passed-over place is not taken.

Narrowed on four points:
(1) The effect is only a hedged Warning ("If it is that table's own stream..."). No object is served wrong, no validator rule fires and the index is not marked incomplete. The batch reads these shapes better than main, which took the quoted stream, ended the chain, rebuilt at opening and gave a false Error file.root-invalid. So this is not worse than main. It is a text that no longer says what the code does, in the same class as round 7's [15], which was settled as minor.
(2) The reviewer's file mixes in the accepted [7](c) consequence (hybrid-stream-lost, then hybrid-stream-wrong). My cleaner shape isolates the new effect: head reads it exactly like its control, apart from the Warning.
(3) Only a quoted stream at or after S is affected. Before S, which is where a writer puts an attached file, the stream is taken as on main. That is the consequence the texts already name and R8-Q1 accepted, so it is not part of this finding.
(4) The silent deferral of such a stream dates from 3a1ac79 (R7-Q2). On 012e13d it was passed over with no report. Only the Warning is new, and it came with 9a07bb9.

This needs no design choice: R7-Q3's precedent covers it (widen the #171 entry and comment on #171).
VERDICT EVIDENCE: Reviewer's file, regenerated byte-identical with quoted9.py (cmp: same), on da2b06f at default limits:
S /Prev 1731 1510 Stream Relocated
D Warning xref.hybrid-stream-lost 1510
D Warning xref.stream-passed-over 1738 The cross-reference stream at offset 1738, near offset 1731, ... If it is that table's own stream, the objects only it indexes are missing from the index.
On main 21a0298 the same file gives: S /Prev 1731 1738 Stream Relocated; xref.rebuilt at opening; F Error file.root-invalid; F Error xref.entry-broken (object 12).

Isolating shape. Generator: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9ref-quoted/genv.py; outputs: out-head.txt, out-main.txt and out-012.txt in the same folder. The file is /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9ref-quoted/files/v2-after-s-noprev.pdf:
- the oldest table at 169;
- S, xref stream 6 at 375, with no /Prev;
- an /EmbeddedFile, object 7 at 507, whose data quotes '12 0 obj << /Type /XRef ... /Index [12 1] >> stream' at 586;
- the hybrid table at 713 with /XRefStm 375 and /Prev 517.
Results:
- da2b06f: S /Prev 517 169 Table Relocated; D Repair xref.offset-adjusted 169 (348 bytes before it); D Warning xref.stream-passed-over 586 (a place inside object 7's data); WasRepaired False; object 5 "five old", object 12 null.
- Control v3, the same file with prose in place of the quoted stream: identical sections, diagnostics and objects, minus the Warning.
- 012e13d: like da2b06f with no Warning.
- main: S /Prev 517 586 Stream Relocated; offset-adjusted for object 12 (571 bytes); xref.rebuilt at opening; F Error file.root-invalid.
Two more variants:
- v4, the quoted stream before S: taken on da2b06f as on main (rebuild, file.root-invalid), the named and accepted consequence.
- v5, S giving the right /Prev: the [7](c) type plus the Warning at 603.
No text names the Warning in this context: grep over threat-model.md, lazy-reading.md, diagnostics.md (the xref.stream-passed-over row, :116) and validation-rules.md finds no stream-data or #171 mention for it. The #171 entry (:468-472) and lazy-reading.md:230 say a quoted stream that reads 'is taken'.
VERDICT FIX: No code change. Documentation and tracking only:
(a) Threat model, #171 Known gaps entry (docs/threat-model.md:468-475). Add after the quoted-stream sentence: 'Near the offset a hybrid table's /Prev names, such a stream that starts no sooner than the stream the table's /XRefStm read, or names again, and gives no /Prev, or the table's, is tried last, as that table's own may be: where another place reads first, it is passed over and reported as xref.stream-passed-over at a place that holds no section of the file.' Change the closing to 'asked of every place the search tries, one it passes over included, and one that reads'.
(b) Make the same clause at lazy-reading.md:230-232. Optionally, add to the diagnostics.md xref.stream-passed-over row that the stream may lie in stream data the search does not tell apart (#171).
(c) Post a comment on #171 with the isolating reproducer (v2: da2b06f, control, main). It should say that the check which closes #171 must also keep a place in stream data out of the passed-over list, or the Warning stays.
Optionally, extend the source comment at PdfFileReader.cs:5109-5111 to name the passed-over case.
VERDICT NEEDS_MAINTAINER: False

######## [7] lens=undecoded key=undecoded-cut-path-report-stands sev=major -> verdict=narrowed sev=minor maint=?
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:6293 (FaultBesideCut called at a candidate with no mark) with :5149 (only attempt.UndecodedReports is rolled back) and :7123 (RowsHeldAtMost decodes with diagnostics: null)
CLAIM: 5de20b7 does not roll back what a dropped place's decoding reported when that decoding is the one FaultBesideCut makes (the UnreadValue branch, a value a guard cut). The place is dropped with UndecodedReports null, so filter.unsupported or filter.failed stays at opening for an object that no section indexes. A whole read of the same file never makes that report. This is a regression in 5de20b7: at 012e13d the outer mark, rolled back on Fault == DataUndecoded, covered this path. 5d10a36 widens it from a cut /W or /Index element to a cut /Size or whole /Index. The root cause is a false premise. Round 8's refuter (and R8a after them) took RowsHeldAtMost's `diagnostics: null` to mean that it reports nothing. In fact PdfFilterPipeline.cs:124 (`diagnostics ??= guard.DocumentDiagnostics`) sends that decoding's reports into the document. Five texts state the decided behavior and are false on this path: the loop comment at :5145-5148, the TryReadXRefStream remark at :6119-6122, the UndecodedReports doc at XRefSectionRecord.cs:160-168 ('null otherwise'), docs/threat-model.md:434 ('what decoding that data reported is dropped with it'), and 5de20b7's body.
EVIDENCE: Probe ZzR9CutPath (copy at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9u/ZzR9UndecTests.cs.txt, Build()). The oldest table at 2576 indexes 1-3 and 7. Object 5 is '8' plus 1,100 spaces, and the newest table places it. Object 4 at 2792 is '<< /Size 5 0 R /W [1 4 2] /Filter /Foo /Prev 2576 /Length 7 >> stream xxxxxxx'. The newest table gives /Prev 2796 (4 bytes into object 4's header) and no /XRefStm. Guard: MaxObjectLength 1024.
- da2b06f, whole read: sections 'startxref Read @4118, /Prev Relocated @2576'. Diagnostics: only 'Repair xref.offset-adjusted @2576: ... names at offset 2796 was found 220 bytes before it.'
- da2b06f, guarded read: the same sections. Diagnostics: 'Warning filter.unsupported @2870: The filter /Foo is not supported; decoding stopped there.', then 'Repair xref.offset-adjusted @2576'. limit.object is never reported. GetObject(4) is null, so nothing reads object 4 again.
- Same shape with '/W 5 0 R' (cut /W, judged undecoded since 092b772): on 012e13d the guarded read gives only 'xref.offset-adjusted @2582'. On da2b06f it gives 'Warning filter.unsupported @2884', then 'xref.offset-adjusted @2582'. This is the regression from 5de20b7.
- '/Index 5 0 R' with object 5 '[7 1]': the same extra 'filter.unsupported @2884'. '/Size 5 0 R' with /FlateDecode: an extra 'Warning filter.failed @2878: A Flate stream could not be decoded.'
- On 012e13d the /Size and /Index rows took object 4 as the guard's section found moved: 'Malformed @2792 cut=True fault=its /Size is the reference 5 0 R, which one of the reader's limits cut', limit.object, checked-in-part. 5d10a36 rightly drops it, as read whole, but the decoding's report stays.
- On main 21a0298 object 4's raw bytes are read as rows (pre-#221), so the outcome is not comparable.
- Prototype fix (/tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9u/fix-proto.diff): with it, every guarded candidate row gives the whole read's diagnostics exactly. CrossReferenceChain, CrossReferenceRule, GuardCutValue, Filter, FilterDamage, DocumentReader, HostileInput, WindowEdge and StreamLength pass: 2,822 total, 0 failed.
FIX: In TryReadXRefStream's UnreadValue branch, for a candidate, take `_diagnostics.GetMarkWithDecodingReports()` before calling FaultBesideCut. When it returns DataUndecoded with no rows held, record that mark on `section.UndecodedReports`, so that TryRelocateXRefSection's existing RollBackBesideLimits drops the decoding's report (prototype above, about 4 lines). Add to the 5de20b7 theory, or to GuardCutValueTests, a row in the candidate shape with a cut /W, /Size and whole /Index, under /Foo and /FlateDecode. It should assert that the guarded diagnostics equal the whole read's (here: offset-adjusted only). It fails on da2b06f. Optionally say in RowsHeldAtMost's and UndecodedFault's summaries that, given no diagnostics, the decoding reports into the document's. Record 5de20b7's body under 'Commit bodies not rewritten' (false where a guard cut a value).
VERDICT REASON: The finding reproduces exactly. Its root cause holds in the code. PdfFilterPipeline.cs:124 runs `diagnostics ??= guard.DocumentDiagnostics`, so the decoding RowsHeldAtMost does with `diagnostics: null` (PdfFileReader.cs:7123, reached through FaultBesideCut/UndecodedFault from the UnreadValue branch at :6293) reports into the document. That branch returns Malformed with UndecodedReports left null. TryRelocateXRefSection (:5149) therefore rolls nothing back when it drops the place. Under a guard that cut a value of the dropped stream, filter.unsupported or filter.failed stays at opening for object 4, which no section indexes (GetObject gives PdfNull). The same file read whole makes only xref.offset-adjusted.

It is a regression inside the batch. At 012e13d the outer mark, rolled back on Fault == DataUndecoded, covered the cut-/W and cut-/W-element shapes. 5de20b7 replaced that mark with one taken at the decode loop only. 5d10a36 then sends the cut /Size and whole /Index shapes here too: at 012e13d they were taken as the guard's section, which was wrong for another reason (round 8 [3]). The premise round 8's refuter wrote, 'RowsHeldAtMost ... decodes with diagnostics: null and reports nothing, so it needs no mark', is false, and decisions.md's [1]/[14] fix was built on it. The texts the finding names are false on this path:
- the loop comment at :5145-5148 ('what its decoding reported goes with it');
- the remark at :6119-6122;
- the XRefSectionRecord.UndecodedReports summary at :160-166 ('where its filters decoded nothing of it; null otherwise');
- docs/threat-model.md:434 ('what decoding that data reported is dropped with it');
- 5de20b7's body.
None of #171, #263, #289, #295, #323, #330, #331, #332 or #52 holds this. No accepted consequence covers it: the accepted R8a item is about MaxDecodedStreamLength being reached earlier, not about a decoding report left standing.

I narrow it from major to minor, as round 1 [9] (a doubled report) was narrowed. The extra report is one Warning, and true of the bytes it names. Main 21a0298 makes the same filter.unsupported for this stream in both reads, because main takes the stream as a section, so the report itself is no worse than main. No section, index entry, refusal or served object changes. The shape is narrow and hostile: a guessed stream of no /Type near a /Prev a few bytes off, a dictionary value written as a reference to an object a guard cuts, and data no filter decodes. What remains is a stray report that departs from the decided behavior, plus texts that misstate this path. No maintainer decision is needed: the texts already state the intent, and the fix is mechanical.
VERDICT EVIDENCE: I wrote my own probe ZzRefR9CutTests (copy at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9ref-ZzRefR9CutTests.cs.txt).
- File: the oldest table indexes 1-3 and 7. Object 5 is the value plus 1,100 spaces, placed by the newest table. Object 4 is '<< DICT /Prev oldest /Length 7 >> stream xxxxxxx'. The newest table's /Prev points 4 bytes into object 4's header, with no /XRefStm.
- Guard: MaxObjectLength 1024. Diagnostics are dumped at opening, then after GetObject(4) and GetObject(5).

da2b06f (outputs in r9ref-cut-head.txt in the scratchpad):
- Whole read, every row: sections 'startxref Read, /Prev Relocated @oldest'; diagnostics only 'Repair xref.offset-adjusted ... found 172 bytes before it'.
- Guarded read, same sections, but:
  - A '/Size 5 0 R /W [1 4 2] /Filter /Foo': 'Warning filter.unsupported @2791: The filter /Foo is not supported; decoding stopped there.', then offset-adjusted @2545.
  - B '/Index 5 0 R' (object 5 '[7 1]'): filter.unsupported @2805.
  - C '/W 5 0 R': filter.unsupported @2805.
  - E '/W [1 5 0 R 2]': filter.unsupported @2805.
  - D '/Size 5 0 R /Filter /FlateDecode': 'Warning filter.failed @2799: A Flate stream could not be decoded.'
  - In every row, obj4 is PdfNull, and limit.object appears only once object 5 is loaded.
- Control F (no value cut): the guarded read equals the whole read.

012e13d (scratch worktree, same probe):
- C and E guarded: only offset-adjusted, with no filter report. This confirms the regression from 5de20b7.
- A, B and D guarded: '/Prev Malformed @2713 cut=True fault=its /Size [/Index] is the reference 5 0 R, which one of the reader's limits cut', plus offset-adjusted @2713 and limit.object, with no filter report.

Main: the reviewer's outmain/cut.txt shows main taking the stream and reporting filter.unsupported in both reads.

Trial fix on da2b06f (r9ref-cut-trialfix.diff, 2 lines):
- Take `var besideCut = candidate ? _diagnostics.GetMarkWithDecodingReports() : default;` before FaultBesideCut.
- Set `section.UndecodedReports = candidate && file == DataUndecoded ? besideCut : null;` where no rows are held.
- With it, every guarded row's opening diagnostics equal the whole read's (offset-adjusted only).
- GuardCutValueTests, CrossReferenceChainTests, HostileInputTests and FilterDamageTests pass: 1,358 of 1,358.

The probe and the trial fix are reverted, and the scratch worktree is removed.
VERDICT FIX: Code, in TryReadXRefStream's UnreadValue branch (PdfFileReader.cs:6293):
- For a candidate, take `_diagnostics.GetMarkWithDecodingReports()` just before FaultBesideCut.
- Where it returns DataUndecoded with no rows held, record that mark on `section.UndecodedReports`, so that the existing RollBackBesideLimits at :5149 drops the decoding's report when the place is dropped.
- This is at most one mark per place, so the MaxRelocationCandidates bound in the comment holds.

Test: add a row, failing on da2b06f, to the 5de20b7 test in CrossReferenceChainTests or to GuardCutValueTests:
- shape: the place-near-an-offset shape (a /Prev a few bytes into the header of an untyped stream, no /XRefStm);
- cut by MaxObjectLength: /W, a /W element, /Size and the whole /Index, under /Foo and /FlateDecode;
- assert: the guarded opening diagnostics equal the whole read's (offset-adjusted only).
- Run ADR 53 over the new lines.

Texts:
- With the fix, the five texts become true.
- Optionally, say in RowsHeldAtMost's and UndecodedFault's summaries that, given no diagnostics, the decoding reports into the document's.

Records:
- Record 5de20b7's body under 'Commit bodies not rewritten': it is false where a guard cut a value.
- Note in decisions.md that round 8's premise 'RowsHeldAtMost reports nothing' was false.

The reviewer's prototype also changes the check path (a rollback in the check, and no decode for a /Type /XRef stream in a check). That part is outside this finding's evidence, and its rows there show only a change of order, so judge it on its own.
VERDICT NEEDS_MAINTAINER: False

######## [8] lens=undecoded key=check-decodes-typed-stream-beside-cut sev=minor -> verdict=narrowed sev=minor maint=?
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:6293 (the check reaches FaultBesideCut before the typed early return at :6394); texts at :3826-3836 (KeepsItsPlace remark), :6109-6111 (check paragraph), :6390-6391 (comment), docs/threat-model.md:432
CLAIM: da0e157 skips the check's decode only after the numbering is read. Where a guard cut a value of a /Type /XRef stream, the check (KeepsItsPlace) takes the UnreadValue branch first. FaultBesideCut then decodes the data through UndecodedFault or RowsHeldAtMost and reports into the document, with no rollback, although KeepsItsPlace answers Section for any typed stream whatever its data. For a cut /Size or whole /Index this decode is new in 5d10a36; for a cut /W or /Index element it dates from 092b772. The cost is one extra decoding, within MaxDecodedStreamLength, once per such hybrid table. The report it makes comes before the /XRefStm's own reports, so the guarded read's order differs from the whole read's. Under ThrowOnLimit the check is what throws. This contradicts da0e157's subject and body ('Only an object that gives a /W and no such type has its data decoded there'), the KeepsItsPlace remark ('its data decoded where it gives no /Type /XRef ... and nothing of the reading is kept or reported'), the TryReadXRefStream check paragraph ('The data of a stream of /Type /XRef is not decoded'), threat-model.md:432 ('its data decoded only where it gives no /Type /XRef ... reporting ... nothing') and the cost test's name. It also makes R8a's recorded consequence ('(c) now applies only to an object that gives no /Type /XRef') false. For an untyped stream with a cut value, the check also reports, which breaks 'nothing ... reported'.
EVIDENCE: Probe ZzR9CutPath, check shape. The newest table names object 4 (2792) through /Prev and, through /XRefStm, the place 20 bytes into object 8 (2764). Object 4 is '<< /Type /XRef /Size 5 0 R /W [1 4 2] /Filter /Foo ... >>'. Guard: MaxObjectLength 1024.
- da2b06f whole read: hybrid-stream-lost@2764, filter.unsupported@2882, section-unreadable@2792, rebuilt, object.redefined.
- da2b06f guarded read: filter.unsupported@2882, hybrid-stream-lost@2764, section-unreadable@2792, rebuilt, limit.object@257, object.redefined.
Probe ZzR9Reads counts source reads that start at the data's first byte (100 KB of data):
- typed uncut: head 1, 012e13d 2
- typed '/Size 5 0 R', guarded: head 2, 012e13d 0
- typed '/Index 5 0 R', guarded: head 2, 012e13d 0
- typed '/W 5 0 R', guarded: head 2, 012e13d 2
Probe ZzR9Throw: ThrowOnLimit, MaxObjectLength 1024, MaxDecodedStreamLength 1000, Flate data of 4,000 zero bytes.
- Typed /Size cut: 'PdfLimitExceededException: The /FlateDecode data decodes to more than 1,000 bytes ... | RowsHeldAtMost < UndecodedFault < FaultBesideCut < TryReadXRefStream < KeepsItsPlace < TryRelocateXRefSection'.
- Typed uncut: thrown from 'TryReadXRefStream < TryReadXRefSection', the /Prev's own reading.
- On 012e13d the cut row threw MaxObjectLength from ReachChainValuesCut.
With the prototype (FaultBesideCut given no stream in a check of a typed stream, and a check's FaultBesideCut rolled back):
- typed cut rows read the data once;
- the throw comes from TryReadXRefSection;
- the guarded order equals the whole read's;
- the nine classes pass (2,822 tests, 0 failed).
FIX: In the check (checkOnly), give FaultBesideCut no stream when section.TypedXRef: KeepsItsPlace keeps the place for a typed stream whatever its data. Wrap the check's FaultBesideCut in a mark and roll back to it, as the check's own decode does with decodingChecked (prototype diff above). Add a /Size-cut row to HostileInputTests.The_look_at_what_a_prev_names_reads_a_stream_s_data_only_where_the_data_decides_whether_the_prev_keeps_its_place under MaxObjectLength 1024, expecting 1 read; it gives 2 on da2b06f. If the code is kept as is instead, narrow the five texts to 'and reads that far, its numbering read', and correct R8a's consequence (c) in the status.md record. Either way, record da0e157's body as false where a guard cut a value.
VERDICT REASON: The finding holds. I rebuilt it with my own probe at da2b06f, at 012e13d, and with an instrumented build.

**What happens.** In a check (KeepsItsPlace, checkOnly), a stream whose /Size, whole /Index or /W leads to an object a guard cut takes the UnreadValue branch. That branch comes before the typed early return at :6394. FaultBesideCut then decodes the data (UndecodedFault or RowsHeldAtMost, PdfFileReader.cs:7019, :7026, :7123). The round 8 refuter said that decode, with diagnostics: null, reports nothing (r8-findings.md:46). That is false: PdfFilterPipeline.Decode:124 does `diagnostics ??= guard.DocumentDiagnostics`, and a stream the reader reads carries the document's guard. So the decode's report goes into the document, and nothing rolls it back. The instrumented build shows it: the check's FaultBesideCut adds 1 diagnostic for a typed stream and for an untyped one. The later readings add 0, because the decoding reports are deduplicated.

**Why it is waste for a typed stream.** KeepsItsPlace answers Section for any typed stream whatever the check returns (:3843). The decode changes no answer. It is exactly the decode da0e157 meant to remove.

**History.**
- For a cut /Size or whole /Index it is new in 5d10a36. At 012e13d the same rows read the data 0 times.
- For a cut /W it was already there at 012e13d (092b772).
- main has no KeepsItsPlace, so all of it is batch-new.
- No issue or earlier round holds it. Round 8 [4] covered only the uncut path.

**Narrowed.**
- The comment at :6386-6391 is scoped 'Past here', and is true where it stands.
- The ThrowOnLimit throw is already accepted: MaxDecodedStreamLength may now be thrown before the guard that cut the value. Only the frame that throws differs (KeepsItsPlace rather than TryReadXRefSection), and the exception is the same.
- The order change is the only visible effect. The guarded read gives the same codes as the whole read, but filter.unsupported moves ahead of xref.hybrid-stream-lost. Nothing is missing or doubled.
- For an untyped stream, the check does report. But rolling back the check alone leaves the visible order as it is. Later, the search near the offset decodes the same object as a candidate through FaultBesideCut, also without a rollback. That candidate path is outside this finding.

**Severity: minor.** The cost is bounded but avoidable: one decode per hybrid table, within MaxDecodedStreamLength, under a guard. The texts are false where a guard cut a value:
- the KeepsItsPlace remark (:3826-3836);
- the TryReadXRefStream check paragraph (:6106 'nothing is ... reported', :6109 'The data of a stream of /Type /XRef is not decoded');
- threat-model.md:432 ('decoded only where it gives no /Type /XRef ... reporting ... nothing');
- the general claim in the name of the cost test;
- da0e157's body (already pushed: correct it in the status.md record only);
- R8a's recorded consequence that (c) 'now applies only to an object that gives no /Type /XRef'.

This is not a design choice. The rule is settled: a typed stream keeps its place whatever its data, and a check reports nothing.
VERDICT EVIDENCE: **The probe.** It is my own, kept at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9ref-checkcut/ZzR9RefCheckCutTests.cs.txt, with outputs head.txt, 012.txt and fix.txt beside it.
- The newest table names object 4 through /Prev, and through /XRefStm the place 20 bytes into object 8, just before object 4.
- Object 5 is the value followed by 1100 spaces. The newest table places it, so the guard (MaxObjectLength 1024) cuts it.

**Head da2b06f: typed '/Type /XRef /Size 5 0 R /W [1 4 2] /Filter /Foo'.**
- Whole read: xref.hybrid-stream-lost@2764, filter.unsupported@2882, xref.section-unreadable@2792, xref.rebuilt, object.redefined.
- Guarded read: filter.unsupported@2882, xref.hybrid-stream-lost@2764, xref.section-unreadable@2792, xref.rebuilt, limit.object@257, object.redefined.
- The typed /W-cut row shows the same reorder (@2896 before @2770).

**012e13d.**
- The typed /Size-cut guarded read is xref.hybrid-stream-lost@2764, limit.object@257, xref.rebuilt, object.redefined. That is the old guard-blame shape, which 5d10a36 fixed.
- The typed /W-cut row already shows the reorder.

**Reads at the data's first byte** (100 KB of data; head / 012e13d):
- typed uncut, guarded: 1 / 2
- typed /Size cut, guarded: 2 / 0
- typed /Index cut, guarded: 2 / 0
- typed /W cut, guarded: 2 / 2
- whole reads: 1 for every typed row at head

**ThrowOnLimit** (MaxObjectLength 1024, MaxDecodedStreamLength 1000, Flate data of 4,000 zero bytes; typed /Size cut):
- head: 'The /FlateDecode data decodes to more than 1,000 bytes | RowsHeldAtMost < UndecodedFault < FaultBesideCut < TryReadXRefStream < KeepsItsPlace < TryRelocateXRefSection'
- 012e13d: 'Object 5 runs past 1 KB | ReachLimit < TryParseNumberedAt < ... < GetObject'

**Instrumented head.** It counts _diagnostics before and after FaultBesideCut:
- 'FBC check=True typed=True at=2792 added=1'
- 'FBC check=True typed=False at=2792 added=1'
- the non-check and candidate readings show added=0, through the dedup

**Prototype fix.** In a check, FaultBesideCut gets no stream when section.TypedXRef, and a check's FaultBesideCut is wrapped in GetMarkWithDecodingReports/RollBack. The diff is in fix.diff beside the probe. With it:
- every typed cut row reads the data once;
- the guarded order equals the whole read's;
- the throw comes from TryReadXRefSection;
- the untyped cut rows keep filter.unsupported first, through the candidate path's own unrolled decode;
- GuardCutValueTests, HostileInputTests, CrossReferenceChainTests, CrossReferenceRuleTests and FilterTests give 1,835 total, 0 failed.

My worktree is restored to da2b06f, clean.
VERDICT FIX: 1. **Code.** In TryReadXRefStream's UnreadValue branch (PdfFileReader.cs:6293), when checkOnly:
   - give FaultBesideCut no stream for a typed stream (`!(checkOnly && section.TypedXRef)`). Simpler: return XRefSectionState.Read in a check as soon as section.TypedXRef is set (:6222), since KeepsItsPlace answers Section for it whatever follows.
   - take `_diagnostics.GetMarkWithDecodingReports()` before the check's FaultBesideCut and `RollBack` to it afterwards, as the check's own decode does with decodingChecked. This makes 'nothing ... reported' true for an untyped stream too.

2. **Tests.**
   - Add typed '/Size 5 0 R' and '/W 5 0 R' rows under MaxObjectLength 1024 to HostileInputTests.The_look_at_what_a_prev_names_reads_a_stream_s_data_only_where_the_data_decides_whether_the_prev_keeps_its_place, expecting 1 read. Head gives 2.
   - Add a guarded-order row in which filter.unsupported follows xref.hybrid-stream-lost, as read whole.
   - Run ADR 53 over the new lines.

3. **Records.**
   - Correct R8a's consequence (c) in the status.md record: the check's throw also applied to a typed stream with a cut value until this fix.
   - Record da0e157's body ('Only an object that gives a /W and no such type has its data decoded there') as false where a guard cut a value.
   - Correct the round-8 premise that RowsHeldAtMost reports nothing. It reports into the document through the guard. The same unrolled decode in FaultBesideCut for a candidate (not covered by 5de20b7's mark) belongs to its own finding.

4. **If the code is kept as is instead,** narrow these texts to 'its numbering read' and drop 'reported' where a guard cut a value:
   - the KeepsItsPlace remark (:3826-3836)
   - the check paragraph (:6106, :6109)
   - threat-model.md:432

No maintainer decision is needed.
VERDICT NEEDS_MAINTAINER: False

######## [9] lens=undecoded key=tm440-leaves-nothing-of-its-reading sev=minor -> verdict=confirmed sev=minor maint=?
LOCATION: docs/threat-model.md:440 (cell 1, 'An object read at a place a section may have been moved to that is no section leaves nothing of its reading'); same wording in src/AdCodicem.Pdf/IO/PdfFileReader.cs:6159-6160
CLAIM: This is the open point, and it should be fixed in this batch. The threat-model cell says a place that holds no section leaves nothing of its reading. That is false for a stream that gives only a /W and is dropped: it keeps what reading its dictionary reported (a stray token, a key given twice, a /Length its data runs past), once. 5de20b7 decided this, and its new test pins it. Row 434 of the same threat model now says so, so the two rows contradict each other. A /W-only stream dropped for a bad width was already kept this way before the batch (round 8 [5]). The src comment at :6159-6160 ('what is there and is no section leaves nothing of its reading, not even a guard its dictionary reached') reads the same way.
EVIDENCE: Probe ZzR9Rebuild. Object 4 is '<< /Size 10 /W [1 4 2] /W [1 4 2] /Index [7 1] /Filter /Foo /Length 7 >>', at a place the newest table's /Prev names 4 bytes into. The oldest table lies beyond the search, so the chain ends and a rebuild indexes object 4.
- da2b06f opening: 'syntax.key-repeated@1579, xref.section-missing@1552, xref.rebuilt'.
- After GetObject(4) and Decode: filter.unsupported@1636 is added, and syntax.key-repeated is still there once.
- After the validator: unchanged.
- On 012e13d syntax.key-repeated never appears (the round 8 [1] bug). The place, which holds no section, keeps its dictionary's report.
CrossReferenceChainTests.A_stream_of_no_type_the_search_near_an_offset_drops_keeps_what_reading_its_dictionary_reported_once_whatever_made_it_unreadable asserts the same.
FIX: Reword cell 1: 'An object read at a place a section may have been moved to that is no section leaves nothing of its reading, nor what it read through — but for what reading the dictionary of a stream that gives a /W and no other type reported, which stands, once, its decoding's reports dropped with the place ([#221]) —'. Cite the 5de20b7 test in cell 2. Reword the src comment at :6159-6160 the same way: the guard is not kept, but the reports of a /W-only dictionary are.
VERDICT REASON: The finding holds, and the gap is a little wider than it says. Cell 1 of threat-model.md:440 says an object read at a place a section may have been moved to that is no section "leaves nothing of its reading". The same row defines that reading as "its reports, the guards it reached, what it read through". That is true only for an object that fails the keep predicate (DeclaresOnlyCrossReferenceStream): no /W, or another /Type. TryParseNumberedAt then rolls everything back, guard included.

An object that gives a /W and no other type passes that predicate. Its dictionary's reports go into the diagnostics (KeepPending), and it is then dropped by the search as a guess. Only what it read through (ForgetReadThrough) and what its decoding reported (UndecodedReports, rolled back beside limits) go with it.

So three things still stand, once:
- a stray token or a repeated key;
- a /Length its data runs past;
- a guard that cut its dictionary before /Type.

Since 5de20b7, row 434 says this outright ("what reading its dictionary reported ... is kept, once ([#221])"), so the two rows contradict each other. Row 440's cell 2 even cites DocumentReaderTests.A_place_a_section_may_have_moved_to_whose_dictionary_a_guard_cuts_is_no_section_the_chain_ends_at. That test asserts that the guessed place keeps limit.trailer, which contradicts the sentence the row cites it for. The code's own texts also call a /W-only object one that "declares no section": the TryReadXRefStream remark at :6120 and the TryRelocateXRefSection comment at ~:5096-5100. Read that way, "is no section" covers it.

The misstatement is partly older than the batch. On main the sentence was at :420, and main's version of that DocumentReaderTests test already asserts LimitTrailer then XRefOffsetAdjusted. The finding's "before the batch (round 8 [5])" is loose: [5] measured at 012e13d, not on main.

What the batch adds: the explicit contradiction with row 434, the 5de20b7 test, and the open point its own fix stage raised. No issue or accepted item holds this point. The fix is docs only, the behavior was settled in round 8 [1]/[14], and no maintainer decision is needed. Fix it in this batch rather than file it.

Minor: a misleading text in the threat model.

The src comment at PdfFileReader.cs:6159-6160 is a nit. In context it describes the keep predicate. But "is no section ... not even a guard its dictionary reached" is false for a /W-only dictionary a guard cut before /Type, if read with the method's own remark at :6120. It is verbatim on main.
VERDICT EVIDENCE: Probe ZzR9Tm440, built on da2b06f and removed afterwards. Objects 1 and 2 hold the catalog and pages, object 4 starts at 110, and one classic table's /Prev names 114, 4 bytes into object 4. Nothing else lies near. Each row shows the reports at opening, then after GetObject(4) and Decode:
- A, '<< /Size 10 /W [1 4 2] /W [1 4 2] /Index [7 1] /Filter /Foo /Length 7 >> stream': opening gives 'Warning syntax.key-repeated@141: The dictionary gives the key /W more than once; ...' and 'Warning xref.section-missing@114'. After get(4), which is PdfNull, the reports are the same. The dropped place keeps its dictionary's report about an object nothing serves.
- B, the same with '/W [1 -4 2] /W [1 -4 2]' and no filter: syntax.key-repeated@142, then xref.section-missing@114.
- C, '<< /Type /Foo /W [1 4 2] /W [1 4 2] ... >>' (another type): only xref.section-missing@114. Nothing is kept, as row 440 says.
- D, a CIDFont whose /W comes before what MaxTrailerLength=128 cuts, its /Type after the cut: 'Warning limit.trailer@110: The dictionary of the object read as a cross-reference stream runs past 128 bytes; ...', then xref.section-missing@114. The guessed place keeps the guard.

Existing tests:
- CrossReferenceChainTests.cs:5094 (5de20b7) asserts that the reported code comes first in the list, before the first xref.offset-adjusted, so the search's own reading made it. It is reported once even after GetObject(4).
- DocumentReaderTests.cs:3147 asserts LimitTrailer then XRefOffsetAdjusted for a guessed place. It is cited in row 440 cell 2, and is the same on main (main DocumentReaderTests.cs:2819).
- `git show 21a0298:docs/threat-model.md` has the same sentence at :420. The src comment is at main :3985.

Round 8 [1]/[14] (r8-findings.md) established that 012e13d lost these reports. I did not rebuild 012e13d.
VERDICT FIX: Docs only, in this batch, with no maintainer decision.

1. In threat-model.md:440 cell 1, replace the sentence with roughly: "An object read at a place a section may have been moved to that declares no cross-reference stream — no /W, or another /Type — leaves nothing of its reading, nor what it read through. One that gives a /W and no other type is a guess dropped where it cannot be read. What it read through goes with it, and so does what decoding its data reported, but for a guard that decoding reached. What reading its dictionary reported stands, once: a stray token, a key given twice, a /Length its data runs past, a guard that cut it before it said what it is ([#221], [#263]). One read at the offset the chain names keeps all of its reading ...". This also covers the guard half, which the finding's wording misses. Without it, the row's cited DocumentReaderTests test still contradicts the sentence.

2. In cell 2, add CrossReferenceChainTests.A_stream_of_no_type_the_search_near_an_offset_drops_keeps_what_reading_its_dictionary_reported_once_whatever_made_it_unreadable. The DocumentReaderTests guard test is already cited there.

3. Nit: reword the src comment at PdfFileReader.cs:6159-6160 to the TryReadXRefSection parameter doc's terms (:5316-5317, 'no cross-reference stream'). For example: "what is there and declares no cross-reference stream leaves nothing of its reading, not even a guard its dictionary reached; an object that gives only a /W, which the search drops later, keeps what its dictionary's reading reported (TryRelocateXRefSection)."
VERDICT NEEDS_MAINTAINER: False

######## [10] lens=src-texts key=passed-over-doubled-where-a-later-search-took-the-stream sev=major -> verdict=narrowed sev=minor maint=?
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3707 (ReportStreamsPassedOver's _sectionsReadAt test); texts at src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:116-124 and PdfFileReader.cs:3683-3695, and the same rule in docs/website/docs/reference/diagnostics.md:116
CLAIM: ReportStreamsPassedOver skips only positions in _sectionsReadAt. A later /Prev's search can take a stream an earlier search passed over, find it moved, and fail to read it. When that stream gives no /Prev, nothing hands it on, so RecordSectionReached never runs and the position never enters _sectionsReadAt. The stream then gets xref.section-unreadable (the chain took it) and also xref.stream-passed-over, which says 'the search near that offset took another section first, and the chain did not read the stream'. The summary's 'the chain, read to its end, did not read the stream' and the remark 'nor is anything looked for in its place ... no link names it' are false in this case. The same stream is reported twice, in contradictory terms.
EVIDENCE: Probe ZzR9St_PassedOverThenTakenByLaterSearch (copy in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9st-ZzR9StProbeTests.cs.txt). The file is PassingOverOneStreamTwice with two changes: the middle table names no /XRefStm, and stream 11 has /W [1 -4 2]. The newest table's /Prev is own+40 and its /XRefStm names stream 10; the middle table's /Prev is own-5. Output on da2b06f:
  S startxref Read Table @1966 / S /XRefStm Read Stream @1550 / S /Prev Relocated Table @1891 / S /Prev Malformed Stream @1775 fault=its /W gives a field a negative width
  D Repair xref.offset-adjusted @1891 ... found 76 bytes after it.
  D Repair xref.offset-adjusted @1775 ... names at offset 1770 was found 5 bytes after it.
  D Warning xref.section-unreadable @1775: The cross-reference stream /Prev names at offset 1770, found at offset 1775, is there but cannot be read: its /W gives a field a negative width. ...
  D Warning xref.stream-passed-over @1775: The cross-reference stream at offset 1775, near offset 1815, which the /Prev of the cross-reference table at offset 1966 names, gives no /Prev, ...: the search near that offset took another section first, and the chain did not read the stream. If it is that table's own stream, the objects only it indexes are missing from the index.
  D Repair xref.rebuilt @-1 ...
Control with /W [1 4 2]: the second search relocates stream 11, and no xref.stream-passed-over is made. On main and 012e13d this code does not exist, so only the other reports are made.
FIX: Treat every place the chain took or reached as read when deciding what to report: record the start of a section found moved that cannot be read (and of any record in _structure) in the set ReportStreamsPassedOver consults, or have ReportStreamsPassedOver skip any position where a record in _structure starts (Offset + Padding). Add the probe as a test row next to A_stream_the_search_near_a_hybrid_table_s_prev_comes_back_to_and_takes_is_not_reported_as_passed_over_though_it_cannot_be_read, with the later search taking the stream.
VERDICT REASON: The finding reproduces on da2b06f, and it is a little wider than claimed. ReportStreamsPassedOver (PdfFileReader.cs:3707) skips only the positions in _sectionsReadAt. A section the chain takes that declares itself one, cannot be read and gives no /Prev goes to ReportLostSection and the chain stops (the `ReportLostSection(...); break;` after PreviousToHandOn returns null, about :3388). RecordSectionReached never runs for it, so its start never enters _sectionsReadAt. A stream an earlier hybrid search passed over is then reported twice at the same offset: xref.section-unreadable from the chain, and xref.stream-passed-over at the end.

This does not need a later search. A later /Prev that names the stream's exact offset gives the same pair. That is the very case the remark at :3690-3692 sets aside ("a link of an older section that names the stream, read after the search, reads it, and the chain reports it as it finds it"). The remark's "no link names it" is false there too. Any cause of an unreadable stream does it: a negative /W, or a /W reference that no section places.

The rule the remark and the existing test A_stream_the_search_near_a_hybrid_table_s_prev_comes_back_to_and_takes_is_not_reported_as_passed_over_though_it_cannot_be_read state ("A stream the search came back to and took is no stream it passed over") holds only within one search. Across searches it fails. It also depends on whether the stream gives a /Prev. One that does is handed on, recorded, and not reported. One that does not is reported.

The doubling is new to batch 19. XRefStreamPassedOver came in with 9a07bb9, and git grep finds it neither at 012e13d nor at 21a0298.

Narrowed to minor, following R1[9] (a doubled report on a rare hostile shape, where nothing else changes). Objects 4, 5 and 1, WasRepaired and the rebuild on demand are the same with or without the extra Warning, because xref.section-unreadable already marks the index incomplete. Read literally, each message is true of what it says: the stream was not read, and the first search did take another section. What is false is the design text (the :3683-3695 remark), and the second Warning is redundant. Calling it contradictory is too strong.

None of this is on the brief's accepted list, and no issue holds it (#330 and #332 concern taking a stream in the table's place).
VERDICT EVIDENCE: My own probe, ZzR9Ref_PassedOverThenTakenByLaterSearch, 11 rows. Source is saved at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9ref-pod/ZzR9RefPodTests.cs.txt and output at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9ref-pod/two-searches.txt. The probe was run on da2b06f and then removed from the worktree.

The file is PassingOverOneStreamTwice changed in three ways: the middle table has no /XRefStm, the newest table indexes objects 1-3 so nothing is rebuilt at opening, and stream 11 can be made unreadable.

Row /W [1 -4 2], middle /Prev own-5 (stream=1550, own=1775, middle=1891, newest=1966):
  S /Prev Relocated Table @1891 / S /Prev Malformed Stream @1775 fault=its /W gives a field a negative width
  D Repair xref.offset-adjusted @1891 ... 1815 was found 76 bytes after it.
  D Repair xref.offset-adjusted @1775 ... 1770 was found 5 bytes after it.
  D Warning xref.section-unreadable @1775: ... found at offset 1775, is there but cannot be read: its /W gives a field a negative width. ...
  D Warning xref.stream-passed-over @1775: The cross-reference stream at offset 1775, near offset 1815, which the /Prev of the cross-reference table at offset 1966 names, gives no /Prev, ...: the search near that offset took another section first, and the chain did not read the stream. ...
  repaired=False; five=(five), repairedAfter=True

Row /W [1 -4 2], middle /Prev exactly own (no second search): offset-adjusted @1891, then `xref.section-unreadable @1775: The cross-reference stream /Prev names at offset 1775 is there but cannot be read ...`, then the same `xref.stream-passed-over @1775`.

Row /W 12 0 R (no section places object 12), own-5 and exact: the same pair at 1775.

Controls:
- /W [1 4 2], own-5 or exact: Relocated or Read Stream @1775, no passed-over.
- /W [1 -4 2] with /Prev 203 (handed on, so RecordSectionReached runs): section-unreadable only.
- Middle table hybrid (/XRefStm 1550): stream 11 is passed over by both searches and never reached; a single passed-over Warning is correct there.

Main and 012e13d: `git grep XRefStreamPassedOver 012e13d -- src` and the same at 21a0298 find nothing.
VERDICT FIX: Treat a section the chain took and could not read as reached when the passed-over streams are reported. Either:
(a) at the lost-section break in the chain loop (`ReportLostSection(section, naming, offset, namedFrom); break;`), call RecordSectionReached(section, sectionsAt) when section.State == XRefSectionState.Malformed. Nothing reads _sectionsReadAt after the chain except ReportStreamsPassedOver. If you take this route, extend the RecordSectionReached summary's list of cases to cover it.
(b) in ReportStreamsPassedOver, also skip any `at` equal to Offset + Padding of a record in _structure whose state is Read, Relocated or Malformed.

Then bring the remark at :3683-3695 in line ("a link ... that reaches the stream, where it is named or found moved, reads it or reports it as it finds it"). Add a test row beside the comes-back-and-takes test with stream 11 at /W [1 -4 2] reached by an older non-hybrid table's /Prev, both 5 bytes short (a second search) and exactly at the stream. Assert one xref.section-unreadable at own and no xref.stream-passed-over, with the /W [1 4 2] row as control.

This is not a maintainer decision: the remark and the existing test already state the intended rule.
VERDICT NEEDS_MAINTAINER: False

######## [11] lens=src-texts key=passed-over-warning-depends-on-a-guard sev=major -> verdict=confirmed sev=major maint=?
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3679 and 3707; src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:120 ('the chain, read to its end, did not read the stream'); PdfFileReader.cs:3685 and _streamsPassedOver doc ('once it is read to its end'); docs/website/docs/reference/diagnostics.md:116
CLAIM: xref.stream-passed-over is made after the chain stops for any reason, a guard included, and a stream a guard cut is not treated as reached. In two cases the guard alone decides whether this file-anomaly Warning appears: (1) when a guard ends the chain before the older link that would read the stream (MaxXRefSectionCount); (2) when that link reaches the stream and a guard cuts it (a /W that MaxObjectLength cuts). Neither case appears with the limit lifted. A guard's cut is reported as the file's (ADR 34, invariant 12). In case (2) the stream sits in Structure as reached and cut by the guard, yet the Warning says the chain did not read it. In case (1) the chain was not 'read to its end', which contradicts the summary. A rebuild also follows at opening and serves what the Warning calls missing.
EVIDENCE: Probe ZzR9St_GuardStopsChainBeforeOlderNamesPassedOver, on HybridFiles.PrevFallingShortOfTheStreamBeforeTheTablesOwn("nothing", shortOfOwn: true, oldestNamesOwn: true):
  default and MaxXRefSectionCount=3: diagnostics are only xref.offset-adjusted @1703 and xref.hybrid-stream-lost @1703; the oldest table's /XRefStm reads stream 11 (S /XRefStm Read Stream @1892); repaired=False.
  MaxXRefSectionCount=2: '... D Warning limit.xref-section-count @292: The cross-reference chain has more than 2 sections ...' then 'D Warning xref.stream-passed-over @1892: The cross-reference stream at offset 1892, near offset 1885, ... the chain did not read the stream. If it is that table's own stream, the objects only it indexes are missing from the index.' then 'D Repair xref.rebuilt @-1', repaired=True, object 10 (which only stream 11 indexes) served as '(ten)'.
Probe ZzR9St_ValueGuardOnOwnStreamOlderNames (same file, stream 11 /W 12 0 R, object 12 = [1 4 2] plus 1,100 spaces, indexed by the newest table):
  defaults: S /XRefStm Read Stream @1892, no passed-over report.
  MaxObjectLength=1024: 'S /XRefStm Malformed Stream @1892 cut=True fault=its /W is the reference 12 0 R, which one of the reader's limits cut', then 'D Warning xref.stream-passed-over @1892: ... the chain did not read the stream ...' and 'D Warning limit.object @2608'.
FIX: Report a passed-over stream only where nothing the guards hid could have read it. Skip the report when the chain ended for a guard (_cutByGuard, a pastGuard break, a limit reached while the chain was read), and count the start of a section the chain reached and a guard cut (CutByLimit, lost) as reached. Alternatively, report under a guard only the streams no hidden link can name. Then change 'read to its end' in the summary, the remarks, the field doc and diagnostics.md to say what holds. Add the two probes as rows, defaults against lowered limits.
VERDICT REASON: The finding holds. I rebuilt both cases on da2b06f and they come out as the reviewer says. The real seeded differential (out-r8f.txt) has the same thing in two files. xref.stream-passed-over is a file-anomaly Warning, and here it appears only because a guard was reached.

Case (1): the chain ends at MaxXRefSectionCount before the link that names the stream. In seeded files g001929 and g003252 under count2, the stream reported as passed over is the very next /Prev the count guard hid. limit.xref-section-count and xref.stream-passed-over sit at the same offset there.

Case (2): the chain reaches the stream, and a guard cuts its /W. Structure records the stream as reached ('/XRefStm Malformed Stream cut=True'), yet the Warning says 'the chain did not read the stream'.

This goes against ADR 34 ('What a guard leaves out of the index is not damage'; what the parser met at the cut is the guard's) and against the #264 rule this batch already follows for xref.hybrid-stream-lost (a naming that may lead to a sound section past a guard stays the guard's, unreported). The R8 settlement under invariant 5 ('a stream ... the chain never reads, is reported') was argued for kfirst/kagain, where objects are lost in silence. Under a guard nothing is lost in silence: the limit.* report is already there.

The texts misstate it too. PdfDiagnosticCodes.cs:120 says 'the chain, read to its end'. The ReportStreamsPassedOver summary at PdfFileReader.cs:3685 says 'read to its end', and its remark says a link that names the stream 'reads it, and the chain reports it as it finds it'. diagnostics.md:116 uses the same wording. Case (1) breaks the first, and case (2) breaks the remark.

The code is new in 9a07bb9; neither 012e13d nor main has it. Not on the accepted list. No test pins the Warning under a guard. R8b's differential counted the two count2 files among the 162 blocks that 'only gain the Warning' without judging them.

The one sentence I would not count as a separate defect is 'a rebuild also follows at opening and serves what the Warning calls missing'. It is true in case (1), but the code summary and diagnostics.md:116 already say that a rebuild for another cause finds those objects.

Major, matching the triage of R8 [3] and [9]/[13] (a guard's consequence reported as the file's, ADR 34). In practice it needs a lowered limit, or a chain longer than the default count.
VERDICT EVIDENCE: Probe /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9ref-pog/ZzR9RefPassedGuardTests.cs.txt; outputs in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r9ref-pog-count.txt and /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r9ref-pog-value.txt (da2b06f, Release).

Case 1, HybridFiles.PrevFallingShortOfTheStreamBeforeTheTablesOwn("nothing", shortOfOwn: true, oldestNamesOwn: true); oldest=292 stream=1703 own=1892 newest=2609:
- With defaults and with MaxXRefSectionCount=3, the sections are startxref Table 2609 / XRefStm NamedAgain 1703 / Prev Relocated Stream 1703 / Prev Table 292 / XRefStm Read Stream 1892. Diagnostics are offset-adjusted@1703 and hybrid-stream-lost@1703 only; repaired=False; obj 10 'ten'.
- With MaxXRefSectionCount=2, after offset-adjusted and hybrid-stream-lost come:
  'Warning limit.xref-section-count @292: The cross-reference chain has more than 2 sections; the older ones were not read...'
  'Warning xref.stream-passed-over @1892: ... the search near that offset took another section first, and the chain did not read the stream. If it is that table's own stream, the objects only it indexes are missing from the index.'
  'Repair xref.rebuilt @-1', object.redefined; repaired at open=True.
- With MaxXRefSectionCount=1 there is no passed-over report: the search never ran.

Seeded files in r8proto/out-r8f.txt, the only 2 of 3,000 count2 blocks where the Warning appears and the default block lacks it:
- r8own-gen_g001929.pdf, default: the chain reads /Prev Relocated Stream 1608, then /Prev Read Stream 1370, with no Warning. count2: 'Warning limit.xref-section-count 1370' then 'Warning xref.stream-passed-over 1370'; objects 7 and 13 null; validator xref.checked-in-part @1370.
- r8own-gen_g003252.pdf: the same at 539.

Case 2, the same file with stream 11 /W 12 0 R and object 12 = '[1 4 2]' plus 1,100 spaces, indexed by the newest table:
- Defaults: '/XRefStm Read Stream @1892', no passed-over report.
- MaxObjectLength=1024: 'S /XRefStm Malformed Stream @1892 cut=True fault=its /W is the reference 12 0 R, which one of the reader's limits cut', then 'Warning xref.stream-passed-over @1892: ... the chain did not read the stream ...', then 'Warning limit.object @2608'.

Code: PdfFileReader.cs:3608 records a /XRefStm stream as reached only when !stream.CutByLimit. ReportStreamsPassedOver (3696-3716) skips only starts in _sectionsReadAt. It is called at 3679 whenever _xref.Count > 0, whether the chain ended for a guard (pastGuard, _cutByGuard, the count limit) or not.
VERDICT FIX: Make the report the guard's wherever a guard may have hidden the reading, as #264 does for xref.hybrid-stream-lost:
(a) Skip ReportStreamsPassedOver when the chain ended for a guard: the count limit, a pastGuard or endedByGuard break, _cutByGuard, _indexFullAt, or a trailer a guard cut that may hide a link (TrailersHiddenByGuard or TrailerCutByLimit on a section read). Reporting only the streams no hidden link can name is not workable, because an older table's /XRefStm or /Prev can name any offset.
(b) Treat a stream the chain reached and a guard cut (a section record with CutByLimit whose start is the passed-over stream's) as reached for this report only. Use a separate check of _structure, or a set of starts, rather than RecordSectionReached, so the loop and naming-again rules (#156) stay as they are.
Then reword 'read to its end' in PdfDiagnosticCodes.XRefStreamPassedOver, the ReportStreamsPassedOver summary and remark, the _streamsPassedOver doc, diagnostics.md:116 and lazy-reading.md:207: not reported where one of the reader's limits stopped the chain or cut what it reached, the limit's report standing for it.
Add rows to CrossReferenceChainTests: the oldestNamesOwn file at defaults against MaxXRefSectionCount=2 (no passed-over Warning, limit.xref-section-count present), and the /W 12 0 R variant at defaults against MaxObjectLength=1024. Seeded g001929 and g003252 count2 should lose their Warning in the differential. This follows ADR 34 and #264, so no maintainer question is needed.
VERDICT NEEDS_MAINTAINER: False

######## [12] lens=src-texts key=check-decodes-typed-xref-stream-under-a-cut-value sev=minor -> verdict=narrowed sev=minor maint=?
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:6109-6111 (TryReadXRefStream remark), 6390-6391 (comment), 3828-3836 (KeepsItsPlace remark); same claim in docs/threat-model.md:432
CLAIM: The texts say the check (KeepsItsPlace) does not decode the data of a stream of /Type /XRef: 'The data of a stream of /Type /XRef is not decoded, the check giving Read once its numbering is read'; 'its data is not decoded'; 'its data decoded where it gives no /Type /XRef ... one decoding more of the stream ... where the object gives no /Type /XRef'. That holds only on the uncut path. Where a guard cut a value the rows are read with (since 5d10a36 a whole /Index or a /Size, before that a /W element), the check reaches FaultBesideCut before the TypedXRef return. UndecodedFault and RowsHeldAtMost then decode the data of the typed stream, which keeps its place whatever the decode finds. The decode is bounded (once per hybrid table) but avoidable, and the texts misstate it. The R8a consequence recorded for status.md (MaxDecodedStreamLength reached, and thrown under ThrowOnLimit, before the cut guard) therefore also applies to the /XRefStm's look at a /Type /XRef stream.
EVIDENCE: Probe ZzR9St_CheckDecodesTypedXRefUnderCut. The file is PrevNamingAStreamBesideTheOldestTable's shape with 100,000 bytes of data. Object 4 has /Index 9 0 R, and object 9 = [7 1] plus 1,100 spaces, indexed by the newest table. MaxObjectLength=1024. A logging source records the stack of every read at the data's offset:
  type='/Type /XRef ' cut=False readsAtData=1: READ via FileStreamData.GetBytes < TryReadXRefStream < TryReadXRefSection < TryReadXRefChain (the R8a skip works)
  type='/Type /XRef ' cut=True readsAtData=2:
    READ via: FileStreamData.GetBytes < RowsHeldAtMost < UndecodedFault < FaultBesideCut < TryReadXRefStream < KeepsItsPlace < TryRelocateXRefSection < TryReadXRefChain < Initialize < .ctor
    READ via: FileStreamData.GetBytes < RowsHeldAtMost < UndecodedFault < FaultBesideCut < TryReadXRefStream < TryReadXRefSection < TryReadXRefChain < Initialize < .ctor
FIX: In the check, return before FaultBesideCut decodes when section.TypedXRef: pass stream: null to FaultBesideCut when checkOnly && TypedXRef, or return Read and Malformed right after UnreadValue for a typed stream, since KeepsItsPlace keeps its place whatever the check finds. Pin it with the probe's counting assertion (one read at the data for the typed and cut row). Otherwise reword the three texts and threat-model :432 to name the exception.
VERDICT REASON: The finding holds and reproduces on da2b06f. I narrowed it on one point: the comment at PdfFileReader.cs:6390-6391 is true where it stands. It describes the code 'past here', and the cut branch returns before reaching that code. Three texts are wrong:
- the check paragraph of TryReadXRefStream's remark (:6109-6111);
- KeepsItsPlace's remark (:3828-3836), both what it says is decoded ('its data decoded where it gives no /Type /XRef') and the cost it states ('one decoding more ... where the object gives no /Type /XRef');
- threat-model.md:432 ('its data decoded only where it gives no `/Type /XRef`').

How the code goes: the TypedXRef early return in the check (:6392-6397) sits only on the uncut path. Where a guard cut a value the rows are read with, the check goes through UnreadValue -> FaultBesideCut (:6293). The stream is passed in, so FaultBesideCut decodes it through UndecodedFault -> RowsHeldAtMost (cut whole /Index or /Size) or through RowsHeldAtMost at :7073 (a /W element cut). KeepsItsPlace then returns Section for a TypedXRef stream whatever the check found (:3854), so this decode is wasted. It is bounded (once per hybrid table, within MaxDecodedStreamLength) and avoidable. The cited cost test's name also says more than its rows hold, since none of its rows has a cut.

Not covered by an accepted item:
- The brief's accepted 'under a guard that cut /Size or whole /Index the data now being decoded' is about the reading. It says nothing of texts claiming the check skips the decode of a typed stream.
- Round 8's [4] decision ('skip the decode when TypedXRef') was carried out on the uncut path only.

The ThrowOnLimit consequence follows as the finding says. The value's own guard is reached only in ReachChainValuesCut once the chain has been read, so MaxDecodedStreamLength throws inside KeepsItsPlace for a typed stream too. The exception is the same one the /Prev's own reading throws without the check, so the outcome is no worse than what was accepted. Only the record is wrong: r8a-result.json open_points[7] says the throw 'now applies only to an object that gives no /Type /XRef', and status.md should not repeat that.

Compared with 012e13d nothing got worse, since that commit decoded typed streams in the check on every path. The wrong texts are new with da0e157 and the round 8 docs commits.
VERDICT EVIDENCE: Probe (uncommitted, removed afterward; saved at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r9ref-typedcut/ZzR9RefTypedCutTests.cs.txt).

The file has PrevNamingAStreamBesideTheOldestTable's shape with 100,000 bytes of data. In the cut rows, object 4 has /Index 9 0 R (or /Size 9 0 R), and object 9 is '[7 1]' (or '10') plus 1,100 spaces, indexed by the newest table. MaxObjectLength=1024. A source logs the stack of every read at the data's first byte. Output on da2b06f:

```
type='/Type /XRef ' cut=False readsAtData=1
  READ via: GetBytes < Decode < TryReadXRefStream < TryReadXRefSection < TryReadXRefChain
type='/Type /XRef ' cut=True (/Index 9 0 R) readsAtData=2
  /Prev Malformed at 1675 fault=its /Index is the reference 9 0 R, which one of the reader's limits cut
  READ via: GetBytes < Decode < Decode < RowsHeldAtMost < UndecodedFault < FaultBesideCut < TryReadXRefStream < KeepsItsPlace < TryRelocateXRefSection < TryReadXRefChain
  READ via: GetBytes < Decode < Decode < RowsHeldAtMost < UndecodedFault < FaultBesideCut < TryReadXRefStream < TryReadXRefSection < TryReadXRefChain
type='/Type /XRef ' sizeCut=True (/Size 9 0 R) readsAtData=2   (same two stacks)
type='' cut=True readsAtData=2   (same two stacks; here the decode decides)
type='' cut=False readsAtData=2  (GetBytes < Decode < TryReadXRefStream < KeepsItsPlace ...; then the /Prev's own reading)
```

ThrowOnLimit, data /ASCIIHexDecode of 8,000 hex digits, MaxDecodedStreamLength=1024:
- typed and uncut: throws 'The /ASCIIHexDecode data decodes to more than 1 KB' through TryReadXRefStream < TryReadXRefSection, the /Prev's own reading;
- typed and cut: throws the same message through RowsHeldAtMost < UndecodedFault < FaultBesideCut < TryReadXRefStream < KeepsItsPlace < TryRelocateXRefSection, the /XRefStm's search.

Without that limit, the cut's own throw ('Object 9 runs past 1 KB') comes from ReachChainValuesCut < Initialize, once the chain has been read.
VERDICT FIX: Make the code match the texts. This is not a design choice. In TryReadXRefStream's cut branch (:6293), give FaultBesideCut no stream when the check reads a typed stream: put `!(checkOnly && section.TypedXRef) &&` in front of the stream condition. FaultBesideCut still judges the /W and the numbering, but decodes no data that cannot change the answer. KeepsItsPlace returns Section for TypedXRef either way, and the /Prev's own reading makes the same decoding reports right after (they are reported once per stream). So the change has only two effects: a ThrowOnLimit decode throw moves from the /XRefStm's search to the /Prev's own reading, with the same exception, and the decoding reports come in a different order.

Tests:
- Add a cut row to HostileInputTests.The_look_at_what_a_prev_names_reads_a_stream_s_data_only_where_the_data_decides_whether_the_prev_keeps_its_place: typed, /Index 9 0 R cut under MaxObjectLength=1024, 1 read at the data (it gives 2 on da2b06f). Also an untyped cut row (2 reads) as a control. That makes the test's name true.
- Run ADR 53 on the new condition.
- Correct r8a's open_points[7] before it goes into status.md: the throw in the check then applies only to an object that gives no /Type /XRef, as it says.

The fallback, if the code stays as it is: reword :3828-3836, :6109-6111, threat-model.md:432 and the test's name to say a typed stream's data is still decoded where a guard cut a value its rows are read with.
VERDICT NEEDS_MAINTAINER: False

######## [13] lens=src-texts key=bound-still-asks-the-place-the-prev-names sev=minor -> verdict=narrowed sev=minor maint=?
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:5079-5081 ('Once the bound is reached, the places the window holds that were not read are left'), :132 and :4940 ('places are read at most twice as many times as the bound'); docs/website/docs/reference/reader-limits.md:262 ('Once the 32 are spent, no new place is tried: a section not found by then is reported missing'); docs/threat-model.md:434 ('the places the window holds that were not read then left'); test tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:6108 (added in 9a07bb9)
CLAIM: The candidate == leaveAt test, and the KeepsItsPlace read it makes, still comes before the bound check, as on 012e13d. Once the 32 places are spent, the search near an /XRefStm still reads the place its table's /Prev names whenever that place is the nearest unread one. It reads that place through windows grown to MaxTrailerLength and leaves the /XRefStm to the /Prev. When any other place is nearer, the bound stops the search and the stream is reported missing, with a rebuild. So the texts above are false, and the outcome under the bound depends only on which place lies nearest. The new test name 'A_search_near_an_xrefstm_the_bound_stops_asks_nothing_more_of_the_places_its_window_holds' and its comment ('the stream is reported missing, as at any search the bound stops') hold only because object 8 lies nearer than object 4. The pre-existing KeepsItsPlace remark ('at most once for each hybrid table') is true, but these reads are not within 'twice the bound'.
EVIDENCE: Probe ZzR9St_BoundWithPrevPlaceNearest. The file is NamingAnObjectBesideTheStream with the 70,000-byte /Pad font, its /XRefStm moved from object 8 + 20 to object 8 + 28, still inside object 8's string. Object 4 is then 22 bytes away and object 8 is 28. Then UnderPlacesSpent(…, 32):
  spent=32 into=20 (object 8 nearest): 'S /XRefStm NotFound Unknown @1861', 'D Warning xref.section-missing @1861: The cross-reference section /XRefStm names at offset 1861 is not there, nor near it; ...', 'D Repair xref.rebuilt', repaired=True
  spent=32 into=28 (object 4 nearest): 'S /XRefStm NamedAgain Unknown @1869', 'S /Prev Malformed Unknown @1891 cut=True', only xref.offset-adjusted and limit.trailer, repaired=False
  spent=0: NamedAgain in both geometries.
FIX: Either ask KeepsItsPlace only while the bound allows new places (move the leaveAt test after the bound check, or skip it past the bound), or make the bound leave the /Prev's place to the /Prev in every geometry (look for leaveAt among the unread window places before stopping). Then bring the comment, the constant's summary, the remark at :4940, reader-limits.md:262 and threat-model.md:434 in line, and add the into=28 row to the bound test, or rename the test to what it holds.
VERDICT REASON: The reproducer holds on da2b06f. In TryRelocateXRefSection, the `candidate == leaveAt` test and its KeepsItsPlace read come before the bound check. So once the 32 places are spent, the search still asks about the place the table's /Prev names when that is the nearest unread place, stops there, and leaves the section to the /Prev (NamedAgain: no section-missing, no rebuild). When any other place is nearer, the bound check (`i = inWindow - 1`) leaves the whole window, the /Prev's place included, and the stream is reported missing with a rebuild.

The behavior is older than round 8. 012e13d has the same order with a `break` at the bound, and the order dates from the leaveAt stop of rounds 2 and 3. The texts that say the bound leaves every unread place in the window are new in 9a07bb9 and da2b06f:
- the comment at PdfFileReader.cs:5079-5081 ('Once the bound is reached, the places the window holds that were not read are left');
- threat-model.md:434 ('the places the window holds that were not read then left');
- reader-limits.md:262 ('Once the 32 are spent, no new place is tried: a section not found by then is reported missing').
None of them names this exception, so each is inexact in this geometry.

The behavior itself is coherent and should stay. With a spent bound and the /Prev's place the nearest unread one, there is nothing nearer left untried. Stopping there is exactly what the search does without the bound, and the outcome is the same as spent=0. With a nearer place unread, the search cannot tell what that place holds, so it stops.

Narrowed in three ways:
(1) The 'twice the bound' texts (:132, :4940) count the places the search tries. The KeepsItsPlace read is a separate cost: its own remark bounds it to once per hybrid table (MaxXRefSectionCount), and it happens whether or not the bound is spent. At most a nit.
(2) The test A_search_near_an_xrefstm_the_bound_stops_asks_nothing_more_of_the_places_its_window_holds holds what its name says. The name is about a search the bound stops, and into=28 is not one: KeepsItsPlace stops it first. Its comment is true in its own geometry. An into=28 row would add coverage; it is not needed for the test to hold its name.
(3) No outcome is wrong and no cost is unbounded. The flaw is that the texts omit this one exception. It is minor only because a user-facing reference page and the threat model state the bound's effect without it.
VERDICT EVIDENCE: Probe: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9bnd/ZzR9BoundPrevNearestTests.cs.txt (removed from the worktree after the run). Output: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9bnd/out-head.txt. Release build of da2b06f.

The file is HybridFiles.NamingAnObjectBesideTheStream with the 70,000-byte /Pad font. Its /XRefStm is rewritten in place to name object 8 + into, with the same digit count. Object 8 is at 1841, object 4 (which the /Prev names) at 1891. The file is then passed through UnderPlacesSpent.
- spent=32, into=20 (object 8 nearest): 'S /XRefStm NotFound Unknown @1861'; 'D Warning xref.section-missing @1861: ...not there, nor near it...'; 'D Repair xref.rebuilt'; repaired=True.
- spent=32, into=28 (object 4 nearest): 'S /XRefStm NamedAgain Unknown @1869'; 'S /Prev Malformed Unknown @1891 cut=True'; diagnostics are only xref.offset-adjusted (the spender's /Prev) and limit.trailer @1891; repaired=False.
- spent=0 gives NamedAgain in both geometries.
- spent=31, into=28 gives NamedAgain. (A spent=31, into=20 row was not run. From the code, object 8 then takes the 32nd place and is dropped, and the search comes to object 4.)

In code at da2b06f, the `if (candidate == leaveAt) { left = KeepsItsPlace(...); if (left != None) break; }` test comes before `if (!retry || i == uncountedAt) { if (_relocationCandidatesTried >= MaxRelocationCandidates) { if (!retry) i = inWindow - 1; continue; } ... }`. 012e13d has the same leaveAt test before `if (_relocationCandidatesTried++ >= MaxRelocationCandidates) break;`, so the outcome there is the same.

The reader-limits 'places tried' row and the threat-model clause 'the places the window holds that were not read then left' are absent from main and 012e13d. Both came with the round 8 fixes, as did the code comment.
VERDICT FIX: A text fix; keep the code as it is.
- Add the exception to the comment at PdfFileReader.cs:5079-5081, threat-model.md:434 and reader-limits.md:262. Suggested wording: 'but the place a hybrid table's /Prev names, where it is the nearest place not yet read: it is asked whether that /Prev keeps its place, as before the bound, in the read KeepsItsPlace makes once for each hybrid table, and the section is left to that /Prev there. Where a nearer place is left unread, the bound stops the search before it, and the section is reported missing.'
- Optionally, add an into=28 row (the /XRefStm 28 bytes into object 8) to the bound test, expecting NamedAgain and no section-missing at spent=32, so the order is held.
- Do not move the leaveAt test after the bound check. That would report missing and rebuild where nothing nearer is left untried and the chain reads that place anyway.
- Do not look for leaveAt among the unread places either. That would skip nearer places that may hold the section.
- If the maintainer wants the 64-reads figure to be exhaustive, :132 and :4940 can add 'beside the look at what a hybrid table's /Prev names, once for each such table (KeepsItsPlace)'. That is a nit.
No maintainer decision is needed.
VERDICT NEEDS_MAINTAINER: False

######## [14] lens=src-texts key=threat-model-440-leaves-nothing-of-its-reading sev=nit -> verdict=narrowed sev=nit maint=?
LOCATION: docs/threat-model.md:440 (cell 2: 'An object read at a place a section may have been moved to that is no section leaves nothing of its reading, nor what it read through')
CLAIM: For a place the search near an offset drops, the source texts and the threat model's own row :434 say what reading its dictionary reported (a stray token, a key given twice, a /Length its data runs past) stands, once. The source texts are XRefSectionRecord.cs:161-167 (UndecodedReports: 'what reading the stream's dictionary reported stands, as for any place dropped'), PdfFileReader.cs:5147 and PdfFileReader.cs:6120-6122. Only the decoding's reports, the guards and what the place read through go with it. Row :440 says the place 'leaves nothing of its reading', which disagrees. This is the R8 open point.
EVIDENCE: The suite's rows CrossReferenceChainTests.A_stream_of_no_type_the_search_near_an_offset_drops_keeps_what_reading_its_dictionary_reported_once_whatever_made_it_unreadable (6 rows: syntax.unexpected-token, syntax.key-repeated, stream.length-invalid kept once for a dropped object 4, with a negative width or undecodable data) run on da2b06f: 'total: 6 failed: 0 succeeded: 6'.
FIX: Reword :440: such an object leaves none of the guards it reached nor what it read through, and what decoding its data reported goes with it. What reading its dictionary reported stands, once, as for any place dropped (#221), as :434 says.
VERDICT REASON: The claim holds. At docs/threat-model.md:440, cell 2 ('An object read at a place a section may have been moved to that is no section leaves nothing of its reading, nor what it read through') is true only for an object the candidate keep test refuses: one that gives another /Type, gives no /W, or is not a dictionary. Since 5de20b7, an object that gives only a /W, read at such a place and dropped as unreadable, keeps what reading its dictionary reported, once. That holds whether its data cannot be decoded or a width is negative (the negative-width drops never rolled anything back). The code calls such an object one that 'declares no section' (PdfFileReader.cs:6120, XRefSectionRecord.cs:161-167), so the sentence reads as covering it. It then contradicts row :434 and the source.

The sentence predates the batch: it is word for word the same on main 21a0298 and on 012e13d. The R8 fix stage had already logged it as an open point (r8-fix-result.json:172). It is a scope ambiguity in a pre-existing text that the batch made more visible, not a misbehavior, so nit stands.

The finding is narrowed because its proposed fix is wrong in one part: such an object does NOT 'leave none of the guards it reached'.
- A guard that cut the dictionary of a /W-only guess past its /W stands after the place is dropped. DocumentReaderTests.A_place_a_section_may_have_moved_to_whose_dictionary_a_guard_cuts_is_no_section_the_chain_ends_at pins LimitTrailer then XRefOffsetAdjusted: a CIDFont whose /Type lies past the cut is dropped and the older stream is found.
- A guard the decoding reached stands too. The rollback is RollBackBesideLimits (PdfFileReader.cs:5151; PdfDiagnostics.cs:140-146: 'the guards reached, their limit.* reports and their count standing'), and the comment at PdfFileReader.cs:5144-5146 says 'a guard the decoding reached stands'.

Only what it read through goes (ForgetReadThrough at :5143), and so does what decoding its data reported. The same scope ambiguity is in the source comment at PdfFileReader.cs:6159-6160, also pre-existing on main at :3985, which explains the keep predicate passed to TryParseNumberedAt.
VERDICT EVIDENCE: On origin/ccr-6d6c0ee6-dqm4lm-188 (da2b06f), Release build, I ran the built test host with --filter-method on three tests:
- CrossReferenceChainTests.A_stream_of_no_type_the_search_near_an_offset_drops_keeps_what_reading_its_dictionary_reported_once_whatever_made_it_unreadable (6 rows): syntax.unexpected-token, syntax.key-repeated and stream.length-invalid are kept once for dropped object 4, whether the cause is undecodable data or a negative width.
- DocumentReaderTests.A_place_a_section_may_have_moved_to_whose_dictionary_a_guard_cuts_is_no_section_the_chain_ends_at: the place is dropped and LimitTrailer stands.
- DocumentReaderTests.A_place_a_section_may_have_moved_to_that_holds_another_object_leaves_nothing_of_its_reading (3 rows): '<< /A 1 /B >>', a long /Producer dictionary and a CIDFont typed before /W leave nothing; the diagnostics are exactly [XRefOffsetAdjusted].

Output: 'total: 10 failed: 0 succeeded: 10'.

Main 21a0298 and 012e13d carry the identical sentence ('An object read at a place a section may have been moved to that is no section leaves nothing of its reading, nor what it read through; one read at the offset the chain names keeps all of its reading, as a load would'). Main's src also has the twin comment at PdfFileReader.cs:3985 ('what is there and is no section leaves nothing of its reading, not even a guard its dictionary reached'). The keep predicate is DeclaresOnlyCrossReferenceStream (PdfFileReader.cs:6184, 7227-7229): with keep false, TryParseNumberedAt reports and records nothing (:8282-8285); with keep true, the dictionary's reports and a guard's cut are kept (:8268-8280) and stand when the place is dropped later.
VERDICT FIX: Fix it in this batch as a text-only change, docs/threat-model.md:440. Replace 'An object read at a place a section may have been moved to that is no section leaves nothing of its reading, nor what it read through;' with something like this:

'An object read at a place a section may have been moved to that does not declare itself a cross-reference stream — another /Type, no /W, no dictionary — leaves nothing of its reading, nor what it read through, not even a guard its dictionary reached; one that gives only a /W and is dropped where it cannot be read leaves what it read through, and what decoding its data reported, but what reading its dictionary reported stands, once, as for any place dropped ([#221]), and so does a guard its dictionary or its decoding reached;'

Narrow the twin source comment at src/AdCodicem.Pdf/IO/PdfFileReader.cs:6159-6160 the same way: 'what is there and does not declare itself a cross-reference stream leaves nothing of its reading, not even a guard its dictionary reached'.

Do not take the finding's wording 'leaves none of the guards it reached': the code keeps those guards (RollBackBesideLimits, and the LimitTrailer pinned by A_place_a_section_may_have_moved_to_whose_dictionary_a_guard_cuts_is_no_section_the_chain_ends_at). This needs no maintainer decision.
VERDICT NEEDS_MAINTAINER: False

######## [15] lens=tests key=passed-over-empty-chain-untested sev=minor -> verdict=confirmed sev=minor maint=?
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3672 (rule stated at docs/website/docs/reference/diagnostics.md:116 and docs/threat-model.md:432)
CLAIM: The documented rule that xref.stream-passed-over is not made where the chain indexed nothing has no test. If ReportStreamsPassedOver() is moved above the `_xref.Count == 0` return, the whole unit suite still passes, so a regression here would go unseen. No first-order catalog mutant reaches this, which is why ADR 53 never showed it.
EVIDENCE: Probe /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9t/probes/ZzR9tEmptyTests.cs builds this shape. A hybrid table with subsection `0 0` has its /Prev 7 bytes short of stream 11 (no /Prev, after S). Its /XRefStm names stream 6, which has `/Index [4 0]`. An older `0 0` table lies within reach.
- On da2b06f: sections are startxref Read Table 2214, /XRefStm Read Stream 1377, /Prev Relocated Table 1312. Codes are [xref.offset-adjusted @1312, xref.rebuilt]; no passed-over Warning, as documented.
- Control (the same file, the older table giving the free row `0 1`): [xref.offset-adjusted, xref.stream-passed-over @1524, xref.rebuilt].
- Mutant (the call moved above the check): the whole unit suite gives 7,799 total (my 2 probe rows included), 0 failed, 20 skipped. The empty-chain row then reports `xref.stream-passed-over @1504`.
FIX: Add a CrossReferenceChainTests fact with the empty-chain shape. It asserts the exact list [XRefOffsetAdjusted, XRefRebuilt], so that no xref.stream-passed-over appears, and WasRepaired true. Optionally add the free-row control, which asserts the Warning before xref.rebuilt.
VERDICT REASON: The finding holds. Two pages state the rule: diagnostics.md:116 says the Warning is made "not where the chain indexed nothing", and threat-model.md:432 says "nothing is reported where the chain indexed nothing". The code that implements it is at src/AdCodicem.Pdf/IO/PdfFileReader.cs:3672-3679, where the `_xref.Count == 0` return comes before ReportStreamsPassedOver(). No unit test pins it. I moved the call above the check and the whole unit suite still passed. Of the tests cited in threat-model.md:432's test column, none holds that clause. The rule matters: in this case the whole file is rebuilt, so a Warning that says the stream's objects "are missing from the index" would be false. The rule was a fix-stage choice (r8-fix-result.json, reports/1/deviations/3), not a maintainer decision, and no record says a test was waived for it. One narrowing. "No first-order catalog mutant reaches this" is loose: the R8b catalog's A1, A2 and A11 (r8b/mutate3.py, r8b3/mutate4.py) all touch these lines. A2 drops the guard with its return and is killed, but only by tests on the return value (Reports_a_table_that_prev_names_and_the_guard_cuts_before_any_row_under_the_guard_alone, GuardCutValueTests, ...), never by the Warning's absence. No catalog mutant isolates the order, so ADR 53 could not show the gap. None of the known issues (#171, #289, #295, #323, #330, #331, #332, #52) holds it, and no decision accepts the gap.
VERDICT EVIDENCE: I rebuilt the reviewer's probe as tests/AdCodicem.Pdf.Tests/ZzR9RefEmptyTests.cs (now removed) on da2b06f and ran it with a Release build. Output is in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9ref-empty/empty.txt.
- Empty-chain row (oldest table `0 0`, own=1504): sections are startxref Read Table 2214, /XRefStm Read Stream 1377, /Prev Relocated Table 1312. Diagnostics are `Repair xref.offset-adjusted @1312` ("found 185 bytes before it") and `Repair xref.rebuilt @-1`. WasRepaired is true and there is no passed-over Warning, as documented.
- Control row (oldest table gives free row `0 1`, own=1524): `xref.offset-adjusted @1312`, then `Warning xref.stream-passed-over @1524`, then `xref.rebuilt`; repaired=True.
- Mutant: ReportStreamsPassedOver() moved above `if (_xref.Count == 0) return false;`. The probe's empty-chain row now gives `xref.offset-adjusted @1312`, `Warning xref.stream-passed-over @1504`, `xref.rebuilt`.
- Whole unit suite under the mutant (DOTNET_GCHeapHardLimit set, setsid timeout): "Test run summary: Passed! total: 7799 failed: 0 succeeded: 7779 skipped: 20 duration: 3m 47s" (log in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9ref-empty/suite-mutant.txt). The 7,799 includes the 2 probe rows. All figures match the reviewer's.
- I checked every test that names XRefStreamPassedOver (CrossReferenceChainTests.cs:5522-6209). Each one indexes something, so none reaches the empty-chain return.
The worktree is restored: mutant reverted, probe deleted.
VERDICT FIX: Add a CrossReferenceChainTests theory with two rows, built from the probe's shape and placed as a HybridFiles builder beside PrevFallingShortOfTheStreamBeforeTheTablesOwn. The shape: the hybrid table's /Prev falls 7 bytes short of stream 11, which gives no /Prev and lies past S; its /XRefStm names stream 6, which has `/Index [4 0]`; an older table lies within the search's reach.
- Row 1, older table `0 0` (the chain indexes nothing): assert the exact code list [XRefOffsetAdjusted, XRefRebuilt] and WasRepaired true.
- Row 2, older table `0 1` (control): assert [XRefOffsetAdjusted, XRefStreamPassedOver, XRefRebuilt], with the Warning's position at stream 11. This is the accepted case where a rebuild for another cause follows the Warning.
Name the test for the behavior, for example A_stream_passed_over_is_not_reported_where_the_chain_indexed_nothing_and_the_file_is_rebuilt. Add it to the test column of threat-model.md:432. Check that row 1 fails under the moved-call mutant and passes on da2b06f. This is a test-only change, not a design choice for the maintainer.
VERDICT NEEDS_MAINTAINER: False

######## [16] lens=tests key=s-boundary-premise-false-in-its-own-file sev=minor -> verdict=narrowed sev=minor maint=?
LOCATION: tests/AdCodicem.Pdf.Tests/HostileInputTests.cs:4739 (same premise in the src comment at src/AdCodicem.Pdf/IO/PdfFileReader.cs:5042-5044, 'a stream that starts before it is never the table's own')
CLAIM: The S-boundary test justifies the positional rule with 'A place before the stream the /XRefStm read is no stream of the table's own revision'. Its own file contradicts that: HybridFiles.NamingAStreamOneByteIntoItsNumber's summary calls stream 13 the update's own stream, and the header 1 byte before S is that same stream, read from its true start. S is taken as Offset+Padding of where the /XRefStm read (here 1 byte into '13 0 obj'). So whenever an /XRefStm lands inside its own stream's header, the table's own stream counts as 'before S'. With an older section within reach, the chain then ends there. No text names this case, and the test's text states the opposite.
EVIDENCE: Probe /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9t/probes/ZzR9tOneByteTests.cs. It is the builder's shape, but the oldest table (alone indexing the catalog) lies within reach, and stream 13 indexes objects 4 and 13.
- da2b06f, /XRefStm 1 byte in: sections are startxref Read 1097, /XRefStm Read Stream 369, /Prev Relocated Stream 368 (named 361). Codes are [xref.offset-adjusted @368, xref.rebuilt]; WasRepaired True; validator Errors [file.root-invalid].
- da2b06f, /XRefStm exact: /Prev Relocated Table 203, no Error.
- 012e13d: both shapes give /Prev Relocated Table 203, WasRepaired False, no Error.
- main 21a0298: both shapes give /Prev Relocated Stream 368, rebuilt, file.root-invalid.
The one-byte shape is thus back to main's outcome (the consequence accepted for an older revision's stream before S), but it is the table's own stream.
FIX: Reword the test comment and the src comment. The rule judges by where the /XRefStm's reading started. Here the header before S is the very stream it read, one byte into its number, taken as any place before S; with an older section near, that ends the chain, as on main. Then either list this case beside the accepted consequences (threat-model 432), or take S's start from the header of the stream read, if the reader keeps it, so that an /XRefStm landing inside the header still arms the rule (own-stream lens to judge).
VERDICT REASON: The finding holds as a text defect. Its consequence is the same as main's, not worse; it is worse only than 012e13d. The S-boundary test's comment (tests/AdCodicem.Pdf.Tests/HostileInputTests.cs:4739-4740) says a place before the stream the /XRefStm read is no stream of the table's own revision, and applies this to 'the stream's header'. Its own builder (HybridFiles.cs:1215-1222) says that header is stream 13, the update's own stream. The src comment (PdfFileReader.cs:5042-5044, 'a stream that starts before it is never the table's own') makes the same false claim. The pages assert it too: threat-model.md:432 and lazy-reading.md:199-201 call a stream before S 'a first revision's ... or an older revision's'. The code takes S as streamStart = read.Offset + read.Padding, that is, where the /XRefStm's reading started. When the /XRefStm lands inside its own stream's multi-digit object number (here at '3' of '13 0 obj'), S is 1 byte past the stream's true header. That header is not takeAt, so the 'S itself, no /Prev, dropped' arm never fires. Instead the header counts as before S, keeps its nearest-first place and is taken as the /Prev's section. The stream gives no /Prev, so the chain ends there, the same stream read as two sections. No text names this. The known-gap clause (threat-model 432) covers only an older revision's stream before S, so the brief's accepted R8-Q1 consequence does not cover this case in its wording. It does in substance: the outcome is identical to main's, so the issue is the texts, not a regression from main. Narrowing: the shape is hand-built (the /XRefStm inside a two-digit-or-more object number, and the /Prev nearer that header than any older section). The test itself holds what its name says (one probe at the header, WasRepaired false).
VERDICT EVIDENCE: I rebuilt the reviewer's probe, with an extra variant, as /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9ref-onebyte/ZzR9RefOneByteTests.cs.txt. Outputs are out-da2.txt, out-012.txt and out-main.txt in that same folder.

**Reviewer's shape** (oldest table, which alone indexes the catalog, within reach; stream 13 indexes 4 and 13):
- da2b06f, /XRefStm 1 byte in: startxref Read Table 1097, /XRefStm Read Stream 369, /Prev Relocated Stream 368 (named 361). Diagnostics: xref.offset-adjusted @368, xref.rebuilt. WasRepaired True; validator Errors [file.root-invalid].
- da2b06f, exact: /Prev Relocated Table 203, no Error.
- 012e13d: both shapes give /Prev Relocated Table 203, WasRepaired False.
- main 21a0298: both shapes give /Prev Relocated Stream 368, rebuilt, file.root-invalid.

**My variant FIVE** (stream 13 indexes 1-4 and 13, as the builder's does; the oldest revision also writes object 5, which only the oldest table indexes, within reach):
- da2b06f, one byte in: /Prev Relocated Stream 418; object 5 null. Only xref.offset-adjusted @418, no passed-over Warning, WasRepaired False, no Error.
- da2b06f, exact: Relocated Table 229, object 5 'old five'.
- 012e13d: both shapes Table 229, 'old five'.
- main: both shapes (exact too) Stream 418, object 5 null, silent.

So the one-byte shape is back to main's outcome: the rebuild, or, where the own stream indexes the catalog, the older revision's objects null in silence. It is worse only than 012e13d. grep finds no page or comment naming a /XRefStm read from inside its stream's object number.
VERDICT FIX: Fix the texts in this batch:
- **Test comment (HostileInputTests.cs:4739-4743):** the rule judges by where the /XRefStm's reading started. The header 1 byte before is the very stream the /XRefStm read (stream 13, the update's own), and is taken as any place before S. Drop 'is no stream of the table's own revision'.
- **src comment (PdfFileReader.cs:5042-5044):** replace 'never the table's own' with what holds. Under appended updates, a stream that starts before the place the /XRefStm's reading started is an older revision's, or the table's own read from its true header where the /XRefStm names a place inside its object number. Either way it keeps its nearest-first place.
- **Known gaps (threat-model.md:432, and lazy-reading.md:199-201's 'a first revision's ... or an older revision's'):** add this case. Where the /XRefStm names a place inside its own stream's object number, that stream's header counts as before S and is taken when nearest. The chain ends there, as on main: the index is rebuilt with file.root-invalid, or what only the older revisions index reads null with only xref.offset-adjusted.

The code alternative is a refinement of R8-Q1 for the maintainer or the own-stream lens. A candidate before S whose stream is the one read at S (same data start, or a header whose object number runs into S) would be treated as S itself: dropped when it gives no /Prev, deferred when it gives the table's. That would restore 012e13d's outcome. File it as debt if not taken now.
VERDICT NEEDS_MAINTAINER: False

######## [17] lens=tests key=undecodedfault-null-branch-uncovered sev=minor -> verdict=confirmed sev=minor maint=?
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:7094 (7092 partial)
CLAIM: The only round 8 src line no test reaches is UndecodedFault's `return null` for no stream. Callers pass null where the /Length is deferred, or where the data is CutByGuard or EndHidden. An input reaches it, so the patch falls short of the 100% aim: 56 of 58 measured changed lines, counting the partial line as Codecov does.
EVIDENCE: Coverage over the whole unit suite on da2b06f (7,797 tests, `--coverage --coverage-output-format cobertura`, scored by /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9t/bin/patchcov.py against 012e13d..da2b06f): 'PARTIAL PdfFileReader.cs:7092 [50% (1/2)]', 'UNCOVERED PdfFileReader.cs:7094: return null;'. Every other measured changed line is fully covered, including both UndecodedReports lines (5149, 6468) at 100% of conditions.
Probe /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9t/probes/ZzR9tCovTests.cs covers 7094. It takes GuardCutValueTests' harness, object 6 = `<< /Type /XRef /W [1 4 2] /Size 5 0 R /Filter /Foo /Length 2 0 R >>`, with object 2 placed only by the unread stream 7.
- Whole read: Malformed 'its data could not be decoded', with filter.unsupported, section-unreadable, rebuilt and stream.length-invalid.
- Under MaxObjectLength 1024: Malformed cut=True 'its /Size is the reference 5 0 R, which one of the reader's limits cut', codes [limit.object] only.
- `/Index 5 0 R` gives the same.
This is the documented exception: reader-limits.md:192 judges only 'Data whose length the file gives'.
FIX: Add GuardCutValueTests rows `/W [1 4 2] /Size 5 0 R` and `/W [1 4 2] /Index 5 0 R` with `/Length 2 0 R` (a length that waits for the chain). Pin that the section stays the guard's (CutByLimit, the cut-value fault, [limit.object]) where the whole read finds the data undecodable.
VERDICT REASON: The finding holds. UndecodedFault's null-stream return (PdfFileReader.cs:7094; its test at 7092 is partial) is new in round 8. Before 5de20b7, FaultBesideCut returned null directly when the /Index or /Size was unread, and the suite reached those returns. FaultBesideCut gets a null stream in two places:
- line 6268, which is inside the deferred-/Length branch (6255 requires `deferred is not null`);
- line 6293, when the /Length is deferred, or the data is CutByGuard or EndHidden.
So the reviewer's list of null-passing cases is complete.

An input reaches the line: a guard cuts the /Size, or the whole /Index, of a cross-reference stream whose /Length waits for the chain. No unit test builds that file. Codecov counts only the unit suite (ci.yml:44-66), so the patch scores 56 of 58 measured lines (96.6 %). That passes the 95 % floor in codecov.yml but falls short of CLAUDE.md's 100 % aim for a line an input reaches. The private-member exemption does not apply, since a test reaches this line through a document, not only by reflection.

No catalog mutant would have caught the gap. r8a-result.json records R19 (negate `stream is null`) and R20 (remove the null return) as not viable (CS8604), so ADR 53 could not see it, and no debt from #320 to #332 holds it. The behavior itself is the documented exception, so no page misstates it: reader-limits.md:192, concepts/reader-limits.md:67, validation-rules.md:54 and threat-model.md:440 judge only 'data whose length the file gives'. This is a coverage gap, not a defect. Round 1's [32] (an uncovered line an input reaches, confirmed minor) is the precedent for the severity.
VERDICT EVIDENCE: On da2b06f (origin/ccr-6d6c0ee6-dqm4lm-188), in my worktree, I added one line before UndecodedFault's `return null` that appends Environment.StackTrace to a scratch file. Then I built Release (0 warnings).

Whole unit suite, my probe class excluded: 'total: 7797, failed: 0, succeeded: 7777, skipped: 20'. The trace file was never created, so no test reaches the line.

My probe (/tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9ref-ufnull/ZzR9RefUfNullTests.cs.txt) uses the same build as GuardCutValueTests' undecoded theory, but object 6 is `<< /Type /XRef /W [1 4 2] /Size 5 0 R /Filter /Foo /Length 2 0 R >>`. Object 2 is placed only by stream 7, which the redirected /Prev leaves unread, so the /Length is deferred. The probe wrote 2 HITs, with the stack `UndecodedFault ... line 7094 <- FaultBesideCut ... line 7026 <- TryReadXRefStream ... line 6293`.

Whole read: `/Prev Malformed Stream at=1320 cut=False fault=its data could not be decoded`, codes filter.unsupported, xref.section-unreadable, xref.rebuilt, stream.length-invalid.

Under MaxObjectLength 1024: `/Prev Malformed Stream at=1320 cut=True fault=its /Size is the reference 5 0 R, which one of the reader's limits cut`, the only diagnostic being `limit.object @203`.

`/Index 5 0 R` gives the same at offset 1324 (`its /Index is the reference 5 0 R, ...`).

Control: with `/Length 7` the guard read is Malformed 'its data could not be decoded', cut=False. That row is already tested.

Patch score: patchcov.py on the reviewer's whole-suite cobertura (r9t/covwhole/whole.cobertura.xml) gives 'PARTIAL PdfFileReader.cs:7092 [50% (1/2)]', 'UNCOVERED PdfFileReader.cs:7094', and '58 measured, 1 uncovered, 1 partial'.

I reverted the instrumentation and removed the probe; the worktree is clean.
VERDICT FIX: Add a GuardCutValueTests theory beside A_section_whose_filters_decode_nothing_of_its_data_whatever_a_value_a_guard_cut_holds_cannot_be_read_as_read_whole. It cannot be a new row of that theory, because that theory asserts Malformed and not cut. Suggested name: A_section_whose_length_waits_for_the_chain_is_the_guard_s_where_the_guard_cut_its_size_or_index_whatever_its_filters_decode.

Rows: `/W [1 4 2] /Size 5 0 R` with five = "8", and `/W [1 4 2] /Index 5 0 R` with five = "[0 8]". Both use `/Filter /Foo /Length 2 0 R`, with object 2 placed only by the stream the redirected /Prev leaves unread.

What it pins:
- Read whole, the section is Malformed with 'its data could not be decoded'.
- Under MaxObjectLength 1024, the section is Malformed, CutByLimit, with the fault 'its /Size (or /Index) is the reference 5 0 R, which one of the reader's limits cut', and the diagnostics equal [limit.object].

Optionally cite it in threat-model.md:440's evidence list. No catalog mutant is viable there (R19/R20, CS8604), so the test is for coverage only; record that in the stage's ADR 53 notes. No maintainer decision is needed, since the behavior is the documented 'data whose length the file gives' exception.
VERDICT NEEDS_MAINTAINER: False

######## [18] lens=tests key=count-instead-of-sequence sev=nit -> verdict=narrowed sev=nit maint=?
LOCATION: tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:5912 (also :6108, :6148, :6194)
CLAIM: Four new tests assert one diagnostic's place, or counts and Contain, rather than the exact sequence the other round 8 tests pin. A doubled or misordered report of another code would pass:
- the stream-before-S theory checks Diagnostics[0] and ContainSingle(Rebuilt);
- the bound theory counts xref.section-missing;
- the comes-back test checks ContainSingle(section-unreadable) and NotContain(passed-over);
- the put-back theory counts chain-cycle and passed-over.
EVIDENCE: Full sequences on da2b06f (probe ZzShowWeak, /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9t/show.txt):
- middle: [xref.offset-adjusted @446, xref.rebuilt]
- older: [xref.offset-adjusted @1620, xref.rebuilt, object.redefined]
- bound 0: [limit.trailer @1891]
- bound 32: [offset-adjusted @73192, section-missing @1861, limit.trailer, rebuilt, object.redefined]
- comes back: [offset-adjusted @1862, section-unreadable @1862, rebuilt, object.redefined]
- put back 0: [offset-adjusted, hybrid-stream-lost, chain-cycle, stream-passed-over @1879, rebuilt, object.redefined]
- put back 28: [offset-adjusted @2596, offset-adjusted @1879, rebuilt, object.redefined]
FIX: Assert these lists with Should().Equal(...), as the neighboring passed-over tests do.
VERDICT REASON: The facts reproduce on da2b06f. The four tests at tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:5912, :6108, :6148 and :6194 (all from 9a07bb9) check one diagnostic's place, or ContainSingle, NotContain and Count. None pins the whole list of codes, so a doubled report, or a stray one of another code, would pass. The sequences the reviewer gives are exact: with those lists asserted by Should().Equal, all 7 rows pass at head.

Four things narrow it:

(1) Two of the seven rows are already pinned exactly, on the same fixture, by an older sibling:
- Bound row 0 is NamingAnObjectBesideTheStream with the 70,000-byte /Pad font. The theory An_xrefstm_stops_short_of_its_stream_only_where_its_table_s_prev_keeps_its_place, row ("a font whose dictionary a guard cuts", 0, true) (:4654, assertion at :4710), pins exactly [limit.trailer].
- Put-back row 0 is PrevFallingShortOfTheStreamBeforeTheTablesOwn("nothing", false, "the table's /Prev"). The theory A_prev_falling_short_of_the_stream_its_xrefstm_read_takes_a_stream_that_may_be_the_table_s_own_once_no_other_place_holds_a_section, row ("the table's /Prev", false) (:5546, assertion at :5566), pins all six codes in order, the passed-over code included. That was round 8's [21] fix.
- So the gap is five rows: the middle and older rows of the stream-before-S theory, bound 32, comes back, and put back 28.

(2) Each test holds what its name says. The section records pin the outcome each name states:
- the stream before S is taken at `at`, and the chain has 3 sections;
- the /XRefStm record is NotFound under the bound, not NamedAgain, so the search did not go on to object 4;
- the last /Prev record is Malformed at `own`, and there is no passed-over report;
- the /Prev record is Relocated at `taken`.
r8-fix-result.json says these tests kill the ADR 53 mutants they were written for: D1, D2, D6, C21 and C35. The R8b pass left no open survivor (87 mutants, 78 killed, 6 equivalent, 3 not viable).

(3) No code path on the branch is shown to double a report in these fixtures. This is an exactness nit, not a missed defect. It is the same kind of gap rounds 7 ([6]) and 8 ([21]) settled the same way: narrowed to a nit, fixed by pinning the code list.

(4) The premise that 'the other round 8 tests' pin the exact sequence is not universal. A_table_whose_xrefstm_names_a_stream_that_cannot_be_read_tells_no_stream_near_its_prev_by_it (:6087) also asserts only the last section and NotContain(passed-over).

Nothing here is a design choice for the maintainer.
VERDICT EVIDENCE: Built origin/ccr-6d6c0ee6-dqm4lm-188 at da2b06f in Release: 0 warnings, 0 errors.

The probe ZzR9RefWeak (copy at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9ref-weak/ZzR9RefWeakTests.cs.txt, output at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9ref-weak/out.txt) rebuilds each of the four tests' fixtures and asserts the full code list with Should().Equal. All 7 rows passed (total 7, failed 0):
- middle: [offset-adjusted, rebuilt], and Diagnostics[0].Position == at;
- older: [offset-adjusted, rebuilt, object.redefined];
- bound 0: [limit.trailer];
- bound 32: [offset-adjusted, section-missing, limit.trailer, rebuilt, object.redefined];
- comes back: [offset-adjusted, section-unreadable, rebuilt, object.redefined], and Diagnostics[1].Position == own;
- put back 0: [offset-adjusted, hybrid-stream-lost, chain-cycle, stream-passed-over, rebuilt, object.redefined], and Diagnostics[3].Position == own;
- put back 28: [offset-adjusted, offset-adjusted, rebuilt, object.redefined].
These match the reviewer's show.txt.

The fixtures are shared elsewhere (`grep -n` on the builders):
- NamingAnObjectBesideTheStream with the 70,000-byte /Pad is also used at :4698, row ("a font whose dictionary a guard cuts", 0, true), which asserts `Diagnostics.Select(Code).Should().Equal(LimitTrailer)`.
- PrevFallingShortOfTheStreamBeforeTheTablesOwn("nothing", shortOfOwn, streamPrev) is also used at :5560, which asserts the six-code list for taken == stream.
- PrevFallingShortOfAMiddleRevisionsStream, PrevFallingShortOfAnOlderRevisionsStream, the comes-back fixture with ownWidths "[1 -4 2]", and the UnderPlacesSpent variants have no other caller.

r8-fix-result.json, reports[1].mutants[1]: 'D1: the comes-back-and-takes test. D2: the put-back-unread test. D6: the comes-back test. C21 and C35: the budget theories.' Precedent: r7-findings.md [6] and r8-findings.md [21], both narrowed nits, settled in decisions.md by pinning the code list.

The probe was removed and the worktree is clean.
VERDICT FIX: A test-only change, with no maintainer decision:
- Stream-before-S theory (:5932-5933): replace the Diagnostics[0] Match and the ContainSingle(Rebuilt) check with `document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(near == "a middle revision's stream" ? [XRefOffsetAdjusted, XRefRebuilt] : [XRefOffsetAdjusted, XRefRebuilt, ObjectRedefined]);`, and keep `document.Diagnostics[0].Position.Should().Be(at);`.
- Bound theory (:6124): replace the Count with `Should().Equal(spent > 0 ? [XRefOffsetAdjusted, XRefSectionMissing, LimitTrailer, XRefRebuilt, ObjectRedefined] : [LimitTrailer])`. Row 0 is already pinned at :4710, so this mainly adds row 32.
- Comes-back test (:6159-6160): `Should().Equal(XRefOffsetAdjusted, XRefSectionUnreadable, XRefRebuilt, ObjectRedefined)`, then `Diagnostics[1].Position.Should().Be(own)`. That also covers the NotContain(passed-over).
- Put-back theory (:6206-6207): `Should().Equal(spent > 0 ? [XRefOffsetAdjusted, XRefOffsetAdjusted, XRefRebuilt, ObjectRedefined] : [XRefOffsetAdjusted, XRefHybridStreamLost, XRefChainCycle, XRefStreamPassedOver, XRefRebuilt, ObjectRedefined])`. Row 0 is already pinned at :5566.

All four forms were checked to compile and pass at da2b06f. Optionally, give A_table_whose_xrefstm_names_a_stream_that_cannot_be_read_tells_no_stream_near_its_prev_by_it (:6087) the same treatment; its sequence was not dumped.
VERDICT NEEDS_MAINTAINER: False

######## [19] lens=tests key=place-test-name-vs-passed-over-row sev=nit -> verdict=narrowed sev=nit maint=?
LOCATION: tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:5704
CLAIM: A_place_the_search_near_a_hybrid_table_s_prev_passes_over_or_finds_no_section_at_leaves_nothing_of_its_reading: its 'stream 11' row now pins a Warning made at that place, built from what reading its dictionary found (no /Prev). The name says the place leaves nothing of its reading; only its syntax fault goes unreported.
EVIDENCE: The row asserts the exact list [XRefOffsetAdjusted, XRefHybridStreamLost, XRefStreamPassedOver]. The test's own comment says stream 11 'is reported once it is read'.
FIX: Rename to what both rows hold, for example ..._reports_no_fault_its_reading_met, and update the threat-model citation if one cites the name.
VERDICT REASON: The finding reproduces, but it is a wording nit. The test does hold its name in the sense the codebase gives the phrase.

What reproduces: on da2b06f, the 'stream 11' row of CrossReferenceChainTests.cs:5704 pins a Warning, xref.stream-passed-over, at stream 11's own offset. That is the place the name says leaves nothing of its reading. The Warning's message ('gives no /Prev, or one that names that offset too') states the condition under which stream 11 was passed over.

Why it is narrower than claimed:
- In src, "leaves nothing of its reading" means that nothing the reading met is reported or recorded. Head still uses the phrase for exactly this passed-over stream: PdfFileReader.cs:5322 says 'A stream there that may be the table's own leaves nothing of its reading, and is passed over', and XRefSectionRecord.MayBeTablesOwn says 'it left nothing of its reading but a search for its endstream and ...'.
- The Warning is made after the chain ends, by ReportStreamsPassedOver. Its remarks say 'nothing is read to report it'. So by design it reports what the chain did, not something the reading met.
- The claim says only the syntax fault goes unreported. In fact nothing of the reading is reported or recorded. The /Junk fault is not reported, and no section record is made at 1613 (the Sections list holds 4 records, none of them at stream 11).
- The test's comment states the Warning outright.

What is left: someone reading the name alone expects no diagnostic at that place, yet the row asserts one there. Also, the comment's 'is reported once it is read' has an ambiguous 'it'. It reads as stream 11, which the comment itself says the chain never reads; what is meant is once the chain is read.

Nothing in decisions.md, r8-fix-result.json or the earlier rounds settled this name. R8b changed the row and its comment and kept the name.
VERDICT EVIDENCE: The probe is uncommitted and now removed: ZzR9PlaceName, built on HybridFiles.PrevFallingShortOfAPlaceBesideTheStream at da2b06f. Its output is in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9ref-placename/out-head.txt.

Output for near='stream 11', extra='/Junk } ' (oldest=110, stream=1457, at=1613, newest=2326):
- sections: [startxref Read Table 2326, /XRefStm NamedAgain Unknown 1457, /Prev Relocated Stream 1457, /Prev Read Table 110]
- diagnostics:
  - Repair xref.offset-adjusted @1457
  - Warning xref.hybrid-stream-lost @1457
  - Warning xref.stream-passed-over @1613: 'The cross-reference stream at offset 1613, near offset 1606, which the /Prev of the cross-reference table at offset 2326 names, gives no /Prev, or one that names that offset too, as that table's own stream may: the search near that offset took another section first, and the chain did not read the stream. ...'

Output for 'an outline item': the first two codes only, nothing at 1613.

The real test passes on head: 2 rows, plus the probe's 3 rows, 5 of 5 passed, logged in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9ref-placename/test-out.txt.

`git log -L` shows 9a07bb9 added the passed-over code to the 'stream 11' row and the comment sentence 'Stream 11, which the chain then never reads, is reported once it is read', and kept the name.

Same phrase in src at head:
- PdfFileReader.cs:5322 (the tablePrevious param of TryReadXRefSection)
- PdfFileReader.cs:6687 (summary of TryParseNearHybridPrev)
- XRefSectionRecord.cs:155 (MayBeTablesOwn)
- ReportStreamsPassedOver remarks (PdfFileReader.cs:3690-3694): 'nothing is read to report it, the stream's dictionary having been parsed in the search's window when it was passed over'.
VERDICT FIX: Optional, wording only, and it can go with any later test or docs commit.

1. Rename the test to what both rows hold without the term of art, for example A_place_the_search_near_a_hybrid_table_s_prev_passes_over_or_finds_no_section_at_reports_nothing_its_reading_met.
2. Update its citation in docs/threat-model.md:432. That is the only citation; grep finds none in src or in other pages.
3. In the comment, change 'is reported once it is read' to 'is reported once the chain is read, as a stream passed over'.

Nothing in src needs to change: there the phrase describes what the reading left when the search made it, which stays true. This is not a design question for the maintainer.
VERDICT NEEDS_MAINTAINER: False

######## [20] lens=tests key=filter-test-name-vs-new-rows sev=nit -> verdict=confirmed sev=nit maint=?
LOCATION: tests/AdCodicem.Pdf.Tests/FilterTests.cs:1249
CLAIM: Data_is_said_undecoded_only_where_a_filter_the_library_cannot_run_or_one_decoding_none_of_it_is_left_some. The rows 76f87e9 added (LZW whose first code is undefined, ASCIIHex 'zzzzzz>') are filters that decode none of the data they are left, yet are not said undecoded. 'Only where' keeps the name formally true, but it now names a broader class than the rule: Flate corrupt before its first byte is the only decoding fault said undecoded, as the comment says.
EVIDENCE: Pipeline probe /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9t/probes/ZzR9tFilterTests.cs:
- /LZWDecode '\u0096\u0000': said=False out='' [filter.failed: An LZW stream uses code 300, which it has not defined]
- /ASCIIHexDecode 'zzzzzz>': said=False out='' [filter.failed ... 6 bytes that encode nothing]
FIX: Rename, for example Data_is_said_undecoded_only_where_a_filter_the_library_cannot_run_or_flate_corrupt_before_its_first_byte_is_left_some.
VERDICT REASON: The finding holds. The name at tests/AdCodicem.Pdf.Tests/FilterTests.cs:1249 still says 'or one decoding none of it is left some'. In round 8, decisions.md:542 settled finding [15]: the phrase 'one that could decode none' becomes 'Flate corrupt before its first byte' everywhere, and FilterTests :1250 was on the list. 76f87e9 carried that change into the comment below the name but left the name as it was.

The same commit added two rows that fall inside the name's second clause, LZW whose first code is undefined and ASCIIHex 'zzzzzz>'. Each is a filter that is left data and decodes none of it, and each is pinned undecoded=false. The comment says so in as many words ('decode none of their data, and give none ... none of them is said undecoded'). So does the source's param doc at src/AdCodicem.Pdf/IO/Filters/PdfFilterPipeline.cs:96-99 ('any other filter that decodes none of its data — LZW whose first code is undefined, ASCIIHex or ASCII85 data that encodes nothing — ... gives none: this says neither').

Because the name says 'only where', it states a necessary condition, so it stays formally true and the test still holds what it claims. That keeps this a nit, not a minor. The trouble is that its second clause names a class wider than the rule, a class the test's own comment and the code's doc both reject.

This was not filed. No open point, residual or decision covers the name: r8-fix-result.json mentions only the comment and the rows. The batch did not change behavior here. This is the wording of the round 8 text fix.
VERDICT EVIDENCE: On da2b06f (Release build of tests/AdCodicem.Pdf.Tests) I ran `dotnet test ... --filter-method "*Data_is_said_undecoded_only_where*"`: total 13, succeeded 13, failed 0.

The rows that pass include:
- [InlineData("LZW whose first code it has not defined", "/LZWDecode", "\u0096\u0000", false, "")]
- [InlineData("ASCIIHex whose bytes encode nothing", "/ASCIIHexDecode", "zzzzzz>", false, "")]
Both decode none of the data they are given and are not said undecoded. That matches the reviewer's probe: said=False, out='', with filter.failed 'An LZW stream uses code 300, which it has not defined' and '... 6 bytes that encode nothing'. Those are the messages at PdfFilterPipeline.cs:469 and :562.

Of the rows where a filter is left data, only these are true:
- an unsupported filter;
- an element of /Filter that names none, or a /Filter that is neither a name nor an array;
- Flate corrupt before its first byte, alone or after ASCIIHex.

This matches PdfFilterPipeline.cs:299: `undecoded = !data.IsEmpty && chain.End is Unsupported or NotAName or NotAFilterEntry`. Only Flate's failed TryDecode sets the end that counts as a decoding fault.

Two pages cite the name as it stands: docs/threat-model.md:442 and :616.

decisions.md:542 (round 8 texts): "'one that could decode none' -> Flate corrupt before its first byte", listing "FilterTests :1250".
VERDICT FIX: Rename the theory so that its second clause names Flate's fault rather than a filter that decodes none of its data, and update the two citations in docs/threat-model.md (:442 and :616) in the same commit. One way to word the name: Data_is_said_undecoded_only_where_a_filter_the_library_cannot_run_is_left_some_or_flate_data_is_corrupt_before_its_first_byte.

The reviewer's proposed wording, '..._or_flate_corrupt_before_its_first_byte_is_left_some', is grammatically off: it reads as though the corrupt data were what is left some.

The 'only' can stay. With the narrower clause, the rows now pin both directions. The commit is test-only (test:), with 'Refs #221'. No design choice is needed from the maintainer.
VERDICT NEEDS_MAINTAINER: False

######## [21] lens=tests key=count-once-comment-object-4 sev=nit -> verdict=confirmed sev=nit maint=?
LOCATION: tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:6013-6014
CLAIM: The comment lists 'objects of one null each, 28 or 32 within its reach, and objects 4, 5 and 10'. In the (16, 0) row, object 4 lies outside the search's reach.
EVIDENCE: Dump of PrevFallingShortOfTheTablesOwnStreamAmongObjects(16, 0), /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9t/dumps/among-16-0.pdf: center own-5 = 2151, window [1639, 2663). It holds stream 6 (-512), objects 5 and 10, 32 nulls and stream 11: 36 places. Object 4 at 1617 is at -534. In the (14, 0) row, object 4 is at -492, within reach.
FIX: Say 'objects 5 and 10, and object 4 where it lies within reach', or move object 4 nearer in the builder.
VERDICT REASON: The finding holds. The comment at CrossReferenceChainTests.cs:6013-6014 (added in 9a07bb9, round 8 fixes, so no earlier round reviewed it) lists object 4 among the places the search tries in every row. In the (16, 0) row, object 4's header starts 534 bytes before the offset the /Prev names. The search window opens 512 bytes before that offset, and FindSectionCandidates only scans from the window's start minus one lead byte, so object 4 is never a candidate there. In the (14, 0) and (0, 28) rows it is inside the window. No test result depends on this: in (16, 0) the 32 nulls, objects 5 and 10 and stream 11 already go past the bound of 32 without object 4. The comment's wording is wrong for that one row and nothing else. It is a test comment, so the severity is nit.
VERDICT EVIDENCE: I rebuilt the layout on my own from HybridFiles.PrevFallingShortOfTheTablesOwnStreamAmongObjects (HybridFiles.cs:1098-1139 at da2b06f), with a Python copy of HandWrittenFile.Object, Page and StreamRows. I also checked the reviewer's dumps.
- objects=14: own 2114, named offset 2109, window [1597, 2621). Object 4 is at -492 (inside), object 6 at -470, 5 at -333, 10 at -311. 28 nulls are within reach.
- objects=16: own 2156, named offset 2151, window [1639, 2663). Object 4 at 1617 is at -534, outside. Object 6 at -512 sits right at the window's start. Objects 5 and 10 are at -375 and -353. 32 nulls are within reach.
- (0, 28): objects 4, 5 and 10 are at -198, -39 and -17, all inside.
The window comes from PdfFileReader.cs:5005-5006: start = absolute - NearbySearchRadius (512), end = absolute + min(512, rest). GetWindow(start - lead, ...) at :5013, and FindSectionCandidates (:5254) only adds a header found inside that span. The span starts at 1638, so "4 0 obj" at 1617-1623 is never seen. The reviewer's dumps (scratchpad/r9t/dumps/among-14-0.pdf, among-16-0.pdf, among-0-28.pdf) give the same offsets.
VERDICT FIX: Change only the comment, for example: "...and tries every other place first: objects of one null each, 28 or 32 within its reach, and objects 5 and 10, and object 4 where the window still reaches it (the first row); or objects 4, 5 and 10 alone, a newer table's search having tried 28 places already." Do not move object 4 in the builder. That would shift stream 6 and every offset the three rows depend on (the (14, 0) row needs exactly 31 other places plus stream 11), so rewording is the safe fix.
VERDICT NEEDS_MAINTAINER: False

######## [22] lens=docs key=older-stream-gap-drops-no-prev sev=minor -> verdict=narrowed sev=minor maint=?
LOCATION: docs/threat-model.md:432 ('The rule leaves known gaps: an older revision's stream before the one the /XRefStm read, nearer than its own table, is taken, the chain ending there, the index rebuilt...'); docs/website/docs/concepts/lazy-reading.md:209-211 ('Three cases fall short: an older revision's stream ... is taken, and the chain ends there, the map rebuilt...'); commit d8e67de body
CLAIM: The consequence the maintainer accepted with R8-Q1 is for an older revision's stream before S *that gives no /Prev*. The XML remark of TryRelocateXRefSection keeps that condition ('keeps its nearest-first place, whatever its /Prev, the chain ending there where it gives none'), and so does the test name A_stream_before_the_one_a_hybrid_table_s_xrefstm_read_that_gives_no_prev_is_taken_..._the_chain_ending_there. Both pages drop the condition and say the chain ends there for any such stream. That is false when the stream gives a /Prev. The search takes it, the chain goes on through it, and that revision's own table is never read. What only that table indexes then reads as null, with no report, no incomplete index and no rebuild. This is a different and quieter consequence than the one the pages give (invariant 5). It already happens this way on main 21a0298 and on 012e13d.
EVIDENCE: Generator /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r9docs/gen_r9.py builds genP's p_first shape: every revision hybrid, T2's /Prev 12 bytes into S1, T2's /XRefStm names S2 past S1, and T1 indexes object 7 '(seven, only in the middle table)'. older_none_12.pdf (S1 gives no /Prev) on da2b06f matches the pages: 'SEC /Prev Relocated Stream at 1628', OPEN repaired=True, xref.rebuilt, Error file.root-invalid. older_t0_12.pdf (S1 gives /Prev 208, the oldest table) on da2b06f gives: 'SEC /XRefStm Read Stream at 1891 / SEC /Prev Relocated Stream at 1628 / SEC /Prev Read Table at 208', 'OPEN repaired=False', a single diagnostic 'Repair xref.offset-adjusted at 1628: ... was found 12 bytes before it.', 'OBJ 7 = PdfNull; repaired=False', and as the only finding 'Warning xref.section-shifted at offset 1628'. The chain did not end there, and T1's object 7 is lost in silence. main gives the same sections and OBJ 7 = PdfNull.
FIX: In both texts, say 'an older revision's stream before the one the /XRefStm read that gives no /Prev, nearer than its own table'. Add the other case: 'one that gives a /Prev is taken and read whole, the chain going on through it; its own table goes unread, and what only that table indexes reads as null with nothing reported, as on main'. File that silent loss as debt under #156 or beside #260, or note it on #156. For the status.md record, d8e67de's body has the same unconditional sentence.
VERDICT REASON: The finding holds, with one correction. Both pages state the first known gap with no condition: the older revision's stream is taken, the chain ends there, the index is rebuilt and file.root-invalid follows. That is true only when that stream gives no /Prev, which is the consequence the maintainer accepted with R8-Q1 (genP's p_first, where no stream gives a /Prev) and the condition the brief records. The code agrees with the narrower reading: the TryRelocateXRefSection remark (src/AdCodicem.Pdf/IO/PdfFileReader.cs:4919-4921) says "keeps its nearest-first place, whatever its /Prev, the chain ending there where it gives none", and the test name A_stream_before_the_one_a_hybrid_table_s_xrefstm_read_that_gives_no_prev_... (CrossReferenceChainTests.cs:5912) carries the same condition. When the older stream does give a /Prev naming an older section, it is still taken, because a stream before S keeps its nearest-first place. The chain then goes on through it, its own revision's table is never read, and what only that table indexes reads as null. No rebuild runs, the document is not marked repaired, and the index is not marked incomplete. That is quieter than the stated consequence: on the no-/Prev file the rebuild still serves object 7. So the pages misstate a gap that the brief says to report when a text misstates it. The correction to the finding: "no report" is too strong. The section found moved is reported (Repair xref.offset-adjusted and Warning xref.section-shifted). What goes unreported is the skipped table and its lost objects. The behavior is not a regression. On 012e13d (rebuilt here) older_t0_12 gives byte-identical output to da2b06f, and the reviewer's main 21a0298 output is the same; I did not rebuild main. No open issue covers this shape: not #156, #260, #330 or #332. The closest accepted consequence is R8-Q1's second one (revisions lost with only a section-moved report), which is a different shape. #156 closes with this batch, so nothing tracks the silent loss once the PR merges.
VERDICT EVIDENCE: I rebuilt the reviewer's generator (lot19/r9docs/gen_r9.py, copied to /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r9ref-older/gen/). The files I generated are byte-identical to the reviewer's. I ran them through the reviewer's probe program built against my own Release build of src/AdCodicem.Pdf at da2b06f, then against a build at 012e13d. Outputs: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r9ref-older/out-head.txt and out-012e.txt.

da2b06f, older_none_12.pdf (S1 gives no /Prev): this is the case the pages describe.
  SEC startxref Read Table at 1996 / SEC /XRefStm Read Stream at 1881 / SEC /Prev Relocated Stream at 1628
  OPEN repaired=True
  Diagnostics: Repair xref.offset-adjusted at 1628; Repair xref.rebuilt; Information object.redefined
  OBJ 7 = "seven, only in the middle table"
  Findings: Error file.root-invalid; Warning xref.section-shifted at offset 1628

da2b06f, older_t0_12.pdf (S1 gives /Prev 208, the oldest table):
  SEC startxref Read Table at 2006 / SEC /XRefStm Read Stream at 1891 / SEC /Prev Relocated Stream at 1628 / SEC /Prev Read Table at 208
  OPEN repaired=False
  Only diagnostic: Repair xref.offset-adjusted at 1628: The cross-reference section /Prev names at offset 1640 was found 12 bytes before it.
  OBJ 7 = PdfNull; repaired=False
  Findings: Warning xref.section-shifted at offset 1628; Warning page-tree.resources-missing
  T1 at 1743 is never read, and nothing reports it.

012e13d: older_t0_12 is byte-identical to da2b06f. older_none_12 differs: it reads whole through 'SEC /Prev Relocated Table at 1743', with OBJ 7 the string, which is the change R8-Q1 accepted. The reviewer's main 21a0298 output (r9docs/files1-main.txt) gives the same sections and OBJ 7 = PdfNull for older_t0_12.

The texts:
- threat-model.md:432: "The rule leaves known gaps: an older revision's stream before the one the `/XRefStm` read, nearer than its own table, is taken, the chain ending there, the index rebuilt and the validator saying the file holds no catalog ([#260])".
- lazy-reading.md:209-211: "Three cases fall short: an older revision's stream before the one the `/XRefStm` read, nearer than its own table, is taken, and the chain ends there, the map rebuilt as the document opens and the validator saying the file holds no catalog".
- Commit d8e67de's body: "an older revision's stream before the one the /XRefStm read is taken and the chain ends there".
- The source remark (PdfFileReader.cs:4921) and the test name (CrossReferenceChainTests.cs:5912) keep the "gives no /Prev" condition.
VERDICT FIX: 1. Texts (fix in this batch). In threat-model.md:432 and lazy-reading.md:209-211, make the first gap read "an older revision's stream before the one the `/XRefStm` read that gives no `/Prev`, nearer than its own table, is taken, the chain ending there, the index rebuilt...". Then add the other case: "one that gives a `/Prev` is taken and read whole, the chain going on through it, its own revision's table left unread: what only that table indexes reads as null, nothing reported of it but the section found moved, the index neither rebuilt nor left incomplete, as on main". lazy-reading's "Three cases" then becomes four, or the case joins the first.
2. Optional test. Add a row beside A_stream_before_the_one_a_hybrid_table_s_xrefstm_read_that_gives_no_prev_..., or a sibling test, that pins the /Prev variant as a known gap: the sections ending at the oldest table, object 7 null, WasRepaired false. That way the page's claim has a test to cite.
3. Tracking. #156 closes with this batch, so file a debt (area: reader) for the silent loss, rather than putting it "under #156" as the finding suggests. It could share an issue with the second gap (a first revision's stream taken about 500 bytes off, revisions lost with only a section-moved report), which nothing tracks either. Whether the rule should change later (for instance, telling an older stream before S by whether its /Prev skips a table that lies within reach) is a design question for that debt. The texts and the filing need no maintainer decision.
4. For the status.md record: d8e67de's body states the gap unconditionally. Commit bodies are not rewritten.
VERDICT NEEDS_MAINTAINER: False

######## [23] lens=docs key=places-bound-row-overstates sev=minor -> verdict=narrowed sev=minor maint=?
LOCATION: docs/website/docs/reference/reader-limits.md:262 (the new 'Places tried near a cross-reference section...' row); docs/threat-model.md:434 ('so that a document's searches read places at most 64 times'); d8e67de body ('so that places are read at most 64 times a document')
CLAIM: The new row makes two claims that do not hold. (a) 'Once the 32 are spent, no new place is tried: a section not found by then is reported missing'. A hybrid table's /XRefStm search still asks the place its table's /Prev names whether that /Prev keeps its place (KeepsItsPlace, which a stream answers by reading it). The code makes this check before the count, so it runs whatever the bound. Where that place is the nearest, the search settles there past the bound: the naming is left to the /Prev and reported as xref.hybrid-stream-lost, and the stream is not reported missing. That check read is uncounted, so the 'read 64 times at most' claim leaves it out. (b) 'A cross-reference stream passed over as possibly a hybrid table's own ... is not counted again, and is tried even once the 32 are spent'. That is false for the stream the /XRefStm read when it is put back unread: it is counted where it is read, and the bound may stop it. Threat-model row 434 says this correctly.
EVIDENCE: (a) /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r9docs/gen_r9.py writes bound_<near>_<spent>. A newest table names stream X through /Prev and X+3 through /XRefStm. With spent=32, 31 null objects and a newer table spend the 32 places first (UnderPlacesSpent's shape). On da2b06f, bound_other_32 (object 12 nearer X+3 than X) gives the documented outcome: 'SEC /XRefStm NotFound Unknown at 1561', 'Warning xref.section-missing at 1561', so the bound was spent. bound_prev_32 (X the nearest place) instead gives 'SEC /XRefStm NamedAgain Unknown at 1879', 'Warning xref.hybrid-stream-lost at 1879: ... names offset 1879, what its /Prev names too ...' and 'Error xref.hybrid-stream-wrong at offset 3210', with no section-missing. That is the same as bound_prev_0, where nothing was spent. (b) CrossReferenceChainTests.The_stream_a_hybrid_table_s_xrefstm_read_put_back_unread_counts_where_it_is_read_and_the_bound_may_stop_it(28) passes on da2b06f (run: 13/13 passed with the other cited tests): the bound stops stream 6, which the /XRefStm read and put back, and stream 11 is taken.
FIX: reader-limits.md:262: 'A cross-reference stream passed over as possibly a hybrid table's own once read ... is not counted again, and is tried even once the 32 are spent; the stream a hybrid table's /XRefStm read, put back unread, is counted where it is read, and the bound may stop it.' Then: 'Once the 32 are spent, no new place is tried, but for the place a hybrid table's /Prev names, which the search near its /XRefStm asks whether that /Prev keeps its place, once for each such table whatever the bound (MaxXRefSectionCount bounds them): a section not found by then is reported missing.' Say the same in threat-model.md:434's '64 times' clause, or scope it to the places counted and retried. Record d8e67de's '64 times' sentence for status.md.
VERDICT REASON: Both halves hold, but (a) is narrower than the finding says. The code is wrong about nothing: the batch only made it into a misstatement when d8e67de added the new reader-limits row and threat-model 434's '64 times' clause.

(a) In TryRelocateXRefSection, the `candidate == leaveAt` branch calls KeepsItsPlace (PdfFileReader.cs:5064-5075) before the count check (:5081-5094). The check reads the place uncounted. Once the bound is spent, the first place in the window that reaches the count check gives up the whole window (`i = inWindow - 1`). So the check still runs past the bound only where the /Prev's place is the nearest place the search comes to; the named offset and the sections already reached are skipped. The finding's 'whatever the bound' is wider than the code. Where that check keeps the place, the search settles there: NamedAgain, xref.hybrid-stream-lost. That is the same outcome as with nothing spent. It is not 'reported missing', which contradicts the row's 'Once the 32 are spent, no new place is tried: a section not found by then is reported missing'. Both '64' figures leave out that check read. The KeepsItsPlace XML bounds it at once for each hybrid table (MaxXRefSectionCount), and threat-model 432 describes the stop itself. No page says the stop holds once the 32 places are spent. The order was the same on 012e13d, and main has no such check, so only the text is new.

(b) The round 8 [8] fix as decided says the /XRefStm's own stream, put back without being read, is counted at its first reading, and one not yet counted stops at the bound. The code does this through `uncountedAt`. The row's 'passed over as possibly a hybrid table's own, tried again ..., is not counted again, and is tried even once the 32 are spent' reads as covering that stream, and for it the claim is false. A strict reading (that 'tried again' means a first try came before) would exclude it, but the row does not make that plain. Threat-model 434 states the exception correctly.

The d8e67de body's 'a retry uncounted ... read at most 64 times' has the same two gaps. Record them for status.md; commit bodies are not rewritten. The outcomes are no worse than with places to spare, so this is a misleading text, not a behavior defect.
VERDICT EVIDENCE: I rebuilt the finding's generator (copied to /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r9refute-bound/gen_r9.py, files in .../r9refute-bound/files, probe built against da2b06f's Release DLL, output in .../r9refute-bound/out-head.txt). On da2b06f:

- bound_other_32: 'SEC /XRefStm NotFound Unknown at 1561', 'Warning xref.section-missing at 1561', 'Error xref.section-not-found at offset 1561'. The bound was spent: the spender table was 'found 439 bytes before it'.
- bound_prev_32: 'SEC /Prev Relocated Table at 3210', 'SEC /XRefStm NamedAgain Unknown at 1879', 'SEC /Prev Read Stream at 1876', 'DIAG Warning xref.hybrid-stream-lost at 1879: The /XRefStm of the cross-reference table at offset 3210 names offset 1879, what its /Prev names too, ...', 'FIND Error xref.hybrid-stream-wrong at offset 3210'. There is no section-missing.
- bound_prev_0 gives the same NamedAgain, hybrid-stream-lost and hybrid-stream-wrong. The outcome is the same as with no place spent.

Code: PdfFileReader.cs:5064-5075 (KeepsItsPlace at leaveAt, then break) comes before :5081-5094 (`if (_relocationCandidatesTried >= MaxRelocationCandidates) { if (!retry) i = inWindow - 1; continue; }`). The /XRefStm's search has no retries: streamRead is null there, so passOver is -1. So the check runs at most once for each search. It is the same order on 012e13d (:4948-4963).

(b) runtests.sh: The_stream_a_hybrid_table_s_xrefstm_read_put_back_unread_counts_where_it_is_read_and_the_bound_may_stop_it(0, 28) and A_stream_passed_over_as_possibly_the_table_s_own_is_tried_again_however_many_places_the_document_counted_before: 'total: 5, failed: 0'. With 28 spent, stream 6, which the /XRefStm read and put back, is stopped by the bound and stream 11 is taken (the test's comment and assertions). The decided fix (r8-findings.md [8] REFUTER FIX) reads: 'The /XRefStm's own stream, re-added without being read, is still counted at its first reading ... one not yet counted stops at the bound.'
VERDICT FIX: These are text fixes; the code stays as it is.

1. docs/website/docs/reference/reader-limits.md:262. Replace 'A cross-reference stream passed over as possibly a hybrid table's own, tried again once every other place was, is not counted again, and is tried even once the 32 are spent, so that places are read 64 times a document at most. Once the 32 are spent, no new place is tried: a section not found by then is reported missing' with:

'A cross-reference stream read and passed over as possibly a hybrid table's own, tried again once every other place was, is not counted again, and is tried even once the 32 are spent, so that places are tried 64 times a document at most; the stream the table's `/XRefStm` read, put back without being read, is counted where it is read, and the bound may stop it. Once the 32 are spent, no new place is tried — but where the place a hybrid table's `/Prev` names is the nearest, which the search near its `/XRefStm` asks whether that `/Prev` keeps its place, read as that `/Prev` reads it, once for each hybrid table (`MaxXRefSectionCount` bounds them), the stream's naming then left to that `/Prev` as with places to spare —: a section not found by then is reported missing'.

Do not take the finding's 'whatever the bound'. Past the bound, the check runs only where that place is the nearest one the search comes to.

2. docs/threat-model.md:434. Scope 'so that a document's searches read places at most 64 times' to the places tried, besides the check of the place a hybrid table's /Prev names (once for each such table, row 432). Say that once the 32 attempts are spent, that check is still made where that place is the nearest, and the search stops there where the /Prev keeps its place.

3. Record for status.md that d8e67de's 'a retry uncounted ... read at most 64 times a document' does not except the stream the /XRefStm read, which is counted at its retry, and leaves out the check read.
VERDICT NEEDS_MAINTAINER: False

######## [24] lens=docs key=tm440-guess-leaves-reports sev=minor -> verdict=confirmed sev=minor maint=?
LOCATION: docs/threat-model.md:440, cell 1 ('An object read at a place a section may have been moved to that is no section leaves nothing of its reading, nor what it read through')
CLAIM: This is the open point from R8c. Cell 1 contradicts threat-model row 434 as round 8 wrote it ('what reading its dictionary reported, a stray token, a key given twice or a /Length its data runs past, is kept, once'). It also contradicts the code. An object that gives a /W and no /Type, read at a place the search tries and dropped as no section, keeps what reading its dictionary reported. Since 5de20b7 that holds where its data cannot be decoded. It already held, on main as well, where its rows fail for another reason. Only an object of another type leaves nothing of its reading.
EVIDENCE: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r9docs/gen_guess.py: a newest table's /Prev names a place 15 bytes into a string object. The search near it tries object 4 and drops it, then takes the classic table at 133. Object 4 gives a /W and no /Type, with a stray ']' in its dictionary. On da2b06f, guess_negw.pdf (/W [1 -4 2]) gives 'SEC /Prev Relocated Table at 133', 'Warning syntax.unexpected-token at 359: A dictionary key was not a name.' (inside object 4), then 'Repair xref.offset-adjusted at 133 ...'. guess_foo.pdf (/Filter /Foo) gives 'Warning syntax.unexpected-token at 358' kept and filter.unsupported dropped. guess_font.pdf (/Type /Font) reports nothing of object 4. CrossReferenceChainTests.A_stream_of_no_type_the_search_near_an_offset_drops_keeps_what_reading_its_dictionary_reported_once_whatever_made_it_unreadable passes (6 rows).
FIX: Fix it in this batch. Write: 'An object of another type read at a place a section may have been moved to leaves nothing of its reading, nor what it read through; an object that gives a /W and no /Type, a guess, dropped there, keeps what reading its dictionary reported, once, and drops what decoding its data reported (above).'
VERDICT REASON: The finding holds. docs/threat-model.md:440, cell 1, says "An object read at a place a section may have been moved to that is no section leaves nothing of its reading, nor what it read through". Row 434, as round 8 wrote it, says a dropped guess's dictionary reports are "kept, once", and the code does keep them, so the page now contradicts itself.

Why it happens: TryReadXRefStream reads a candidate through TryParseNumberedAt with keep = DeclaresOnlyCrossReferenceStream. Only an object that keep refuses leaves nothing. That is an object giving another /Type, or giving neither /Type /XRef nor a /W. Any other object it reads is kept. When TryRelocateXRefSection then drops it, nothing is rolled back except attempt.UndecodedReports (PdfFileReader.cs:5136-5145).

When it started:
- Main 21a0298 never rolls back a dropped place (its TryRelocateXRefSection has no rollback), so the sentence was already false on main for a guess whose rows fail.
- At 012e13d, undecoded data rolled back the whole reading, so for that cause the sentence still held. 5de20b7 makes the dictionary's reports stand there too, which widens the gap.
- R8c then wrote the opposite into row 434, so the page now disagrees with itself.

The finding is right but incomplete. Its "only an object of another type leaves nothing" is too narrow: a plain object with no /W leaves nothing too. Two more dropped places keep part of their reading:
- a dictionary a guard cut before it said what it is keeps the guard report (DocumentReaderTests.A_place_a_section_may_have_moved_to_whose_dictionary_a_guard_cuts_is_no_section_the_chain_ends_at pins limit.trailer);
- a /Type /XRef header that a comment holds keeps its syntax report.

The proposed rewrite would still be false for both. My verdict on the open point: fix it in this batch. It is a text-only edit to a row the batch already rewrote, and no design choice is needed.
VERDICT EVIDENCE: My own probe on da2b06f, independent of the reviewer's generator. It is saved at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9ref-mine/ZzRefTm440Tests.cs.txt, and its output is at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r9ref-mine/out.txt.

The file is classic only, with no /XRefStm. The first table is at 133. String object 9 comes next, then object 4 at 360. The newest table's /Prev 330 falls 12 bytes into object 9, so object 4 is tried and dropped and the first table is taken. Results:
- guess-negw (/W [1 -4 2] ]): 'SEC /Prev Relocated 133', 'Warning syntax.unexpected-token at 383: A dictionary key was not a name.', 'Repair xref.offset-adjusted at 133 ... found 197 bytes before it.' Object 4 is served as PdfNull, and the report still stands.
- guess-foo (/Filter /Foo ]): syntax.unexpected-token at 382 is kept, and filter.unsupported is dropped.
- guess-keytwice-negw: 'Warning syntax.key-repeated at 391' is kept.
- guess-len3-negw: 'Warning stream.length-invalid at 424: The stream declared 3 bytes but ended after 7.' is kept.
- font (/Type /Font /W ... ]): only xref.offset-adjusted.
- plain-nowidths (<< /Foo 1 ] ... >>): only xref.offset-adjusted.
- xref-in-comment-negw ('% 4 0 obj << /Type /XRef /W [1 -4 2] ] ...'): 'Warning syntax.unexpected-token at 397' is kept.

Both existing pins pass on da2b06f:
- CrossReferenceChainTests.A_stream_of_no_type_the_search_near_an_offset_drops_keeps_what_reading_its_dictionary_reported_once_whatever_made_it_unreadable (6/6);
- DocumentReaderTests.A_place_a_section_may_have_moved_to_whose_dictionary_a_guard_cuts_is_no_section_the_chain_ends_at (expects LimitTrailer, then XRefOffsetAdjusted).

The sentence is word for word the same in main's, 012e13d's and da2b06f's threat-model.md. Row 434 at da2b06f adds "what reading its dictionary reported, a stray token, a key given twice or a `/Length` its data runs past, is kept, once ([#221])".

From the code:
- main's TryRelocateXRefSection (reader line 3240) only calls ForgetReadThrough after a dropped place.
- 5de20b7's diff replaces the mark taken before TryReadXRefSection with one taken just before decoding.

The probe file has been removed from the worktree.
VERDICT FIX: In this batch, docs only. In docs/threat-model.md:440, cell 1, replace "An object read at a place a section may have been moved to that is no section leaves nothing of its reading, nor what it read through;" with:

"An object read at a place a section may have been moved to that declares no cross-reference stream — it gives another `/Type`, or neither `/Type /XRef` nor a `/W` — leaves nothing of its reading, nor what it read through; one that may be such a stream and is dropped — an object that only gives a `/W` and cannot be read, one whose dictionary a guard cut before it said what it is, or the header of a `/Type /XRef` stream that a comment or a string holds on its line — leaves nothing of what it read through, but what reading its dictionary reported stands, once, the guard that cut it among it, and what decoding its data reported goes with it (above);"

This differs from the reviewer's wording, which would stay false for the guard-cut case and the comment-held /Type /XRef header, and which misses objects that give no /W.

The same claim appears in source comments, which belong to the src-texts lens. Align them in the same change:
- PdfFileReader.cs:6158-6160 ("what is there and is no section leaves nothing of its reading, not even a guard its dictionary reached") contradicts the guard-cut pin above.
- ForgetReadThrough's summary at PdfFileReader.cs:2816-2818 ("nothing of its reading is kept") should say it forgets what the place read through, not that the place's reports are dropped.
VERDICT NEEDS_MAINTAINER: False

######## [25] lens=docs key=debt-332-not-in-known-gaps sev=minor -> verdict=confirmed sev=minor maint=?
LOCATION: docs/threat-model.md, Known gaps of 'Indexing: the cross-reference chain, trailers and the rebuild' (after the [#330] entry at :515), and row :432 ('never taken in its place ([#330])'); docs/website/docs/concepts/lazy-reading.md:197-198; docs/website/docs/reference/diagnostics.md:115
CLAIM: #332 was filed in round 8 as a `debt` (area: reader). It is the gap left by never taking the stream named again, or a stream a newer table's /XRefStm read, in its place. No page names it. The threat model's 'Keeping this document' says a missing defense 'is filed as a debt issue and named under Known gaps'. Every reader debt this batch filed is listed there (#320, #322-#324, #327-#331); #332 is not. The three pages state 'never taken in its place' citing only #330, which is the maintainer's question. #332 is the debt that tracks the false-Error consequence.
EVIDENCE: `grep -c '^- \[#332\]' docs/threat-model.md` gives 0, against 1 for each of #320-#325 and #327-#331. `grep -rn 332 docs/threat-model.md docs/website/docs/` finds nothing. #332 on GitHub: labels debt, area: reader, area: validation; milestone M02; 'Blocked by: #330'. Its shapes reproduce on da2b06f with r8track/final/gen.py: shape1 gives 'Error file.root-invalid', and shapes 2 and 3 give 'Error xref.section-not-found ... nor within 512 bytes of it', with the stream 7 bytes away.
FIX: Add a Known gaps entry: '- [#332] A /Prev a few bytes off a cross-reference stream a newer table's /XRefStm read never takes that stream: it is passed over as a section the chain reached, and the chain ends at another place or is cut, the index rebuilt with file.root-invalid or xref.section-not-found, where main reads the chain whole.' Add the [#332] definition. Cite it beside [#330] in row 432, in lazy-reading.md:197-198 and in diagnostics.md:115.
VERDICT REASON: The finding holds. No page names #332. Every other batch-19 debt that records a missing defense is named under a Known gaps list: #320, #322 to #324 and #327 to #331 in the indexing section, #321 and #325 in the validation section. #326 is about digit grouping and is cited inline at :298. The rule in "Keeping this document" (docs/threat-model.md:909-910) says a missing defense is "filed as a `debt` issue and named under *Known gaps*, never written as if it held". #332 breaks that rule in two ways. First, it has no entry in any Known gaps list. Second, two pages list the rule's gaps as if the list were complete, and both leave this one out: the #156 row ("The rule leaves known gaps:", three items) and lazy-reading.md:209 ("Three cases fall short", three items). The #156 row also describes the deferral as preventing "the update's own stream was taken before the stream the /XRefStm read ... the older revisions unread and the index rebuilt ... the validator saying the file holds no catalog". In the named-again branch that is exactly what still happens (shape 1 below). The cause is the order of the stages, not a decision: R8c wrote the pages, then R8-track opened #332, and R8-track only works on GitHub. No decision and no open point says #332 stays off the pages. The brief accepts the behavior (never taking the stream in its place, #330/#332). This finding is about the texts: a Known gaps entry is missing, and two pages list the gaps as if the list were complete. That is a text that misstates the behavior, so it is in scope. Severity is minor because the behavior is accepted and the debt is filed. It is a misleading text, not a wrong report. Narrowing: the citations of #330 in diagnostics.md:115 and validation-rules.md:60 are not false. #330 is the right citation for the exact-offset loop. They only lack #332 beside them for the case where the /Prev is a few bytes off.
VERDICT EVIDENCE: Branch head da2b06f, in a clean worktree. Per-number check: grep -c '^- \[#N\]' docs/threat-model.md gives 1 for 320, 321, 322, 323, 324, 325, 327, 328, 329, 330 and 331, and 0 for 332. grep -rl over docs/ finds no file citing #332 or issues/332. #326 appears only inline (threat-model.md:298 and the definition at :1056). Other checks: decisions.md:579 says R8-track opened #332 and posted a comment on #330, and no page was edited. r8-fix-result.json's 'track' report lists GitHub actions only. The order of the stages in decisions.md is R8c (pages: d8e67de, da2b06f), then R8-track. Reproducer: lot19/r8track/final/gen.py with the default short of 7 writes shape1.pdf (2,065 bytes), shape2.pdf (1,974) and shape3.pdf (1,960). The copies kept in final/ are the exact-offset run (/Prev 1579). I ran a probe built against src/ at da2b06f, then once with the prebuilt main probe (lot19/r8track/bin-main), one process at a time. On da2b06f, shape 1: 'SEC /XRefStm NamedAgain Unknown at 1579', 'SEC /Prev Relocated Stream at 1730', 'Repair xref.offset-adjusted at 1730: ... found 158 bytes after it.', 'Repair xref.rebuilt', 'FIND Error file.root-invalid at object 1 0: ... No object of the file is a catalog.' Shapes 2 and 3: 'SEC /Prev NotFound Unknown at 1572', 'Warning xref.section-missing at 1572', 'Repair xref.rebuilt', 'FIND Error xref.section-not-found at offset 1572: ... nor within 512 bytes of it'. On main, all three: 'SEC /Prev Relocated Stream at 1579', 'SEC /Prev Read Table at 208', 'OPEN repaired=False', 'Repair xref.offset-adjusted at 1579: ... found 7 bytes from there.' The only finding is 'Warning xref.section-shifted', plus two file.size-wrong in shape 1. Texts at da2b06f:
- threat-model.md:432 cites only #330 for 'never taken in its place'. Its 'The rule leaves known gaps:' list names #260, the case where S gives no /Prev, and the case where a /Prev names no offset. It does not name this case.
- lazy-reading.md:197-198 cites only #330. Its list at :209 says 'Three cases fall short' and gives the same three.
- diagnostics.md:115 says 'which is not taken ([#330])'.
- The Known gaps list ends at the [#330] entry (:515).
VERDICT FIX: In docs/threat-model.md, under the Known gaps of 'Indexing: the cross-reference chain, trailers and the rebuild', add this entry after [#330]: '- [#332] A `/Prev` a few bytes off a cross-reference stream a newer table's `/XRefStm` read never takes that stream, whether this table's `/XRefStm` names it again or not. The search passes it over as a section the chain reached. It takes the table's own stream past it, which gives no `/Prev`, or finds nothing within 512 bytes. The chain ends there or is cut, the index is rebuilt as the document opens, and the validator says the file holds no catalog (`file.root-invalid`) or that the section is not found (`xref.section-not-found`), where `main` reads the chain whole.' Add the [#332] link definition beside [#330] at :1060. In row :432, cite [#332] beside [#330] after 'never taken in its place', and add the case to 'The rule leaves known gaps:'. Two short clauses are enough: where the stream named again stands for S, the table's own stream past it is still taken, the chain ends and the index is rebuilt; where nothing else is in reach, the section is reported missing a few bytes from that stream. In lazy-reading.md:197-198, cite #332 beside #330, and at :209 change 'Three cases fall short' to four by adding the same case. In diagnostics.md:115, cite #332 beside #330. The validation-rules.md:60 citation of #330 (the exact-offset loop) may stay as it is. If the fix lands, the docs bullet in #332's 'What closes it' still applies. This is a pages-only change and needs no maintainer decision.
VERDICT NEEDS_MAINTAINER: False

######## [26] lens=docs key=issue332-shape1-reported-claim sev=nit -> verdict=confirmed sev=nit maint=?
LOCATION: GitHub #332 body, 'The shapes, as measured', item 1 ('the /XRefStm names S1, the previous revision's stream, which #156 reports at this table')
CLAIM: In shape 1 as generated and measured, with revision 3, nothing reports revision 2's /XRefStm at its table, on da2b06f or on main. Revision 3's /XRefStm names S1 first, so revision 2's naming is only recorded as named again. The xref.hybrid-stream-lost and Error xref.hybrid-stream-wrong at 1845 come only in the file without revision 3, which the body itself describes further down. Every other statement and figure in #332 and in the #330 comment (6716295153) held on both trees: offsets 208/1579/1730/1845/1850/1864/1955, file sizes 2,065/1,974/1,960, the head and main lines, the exact-offset loops, main's extra file.size-wrong, and the two-revisions-late 73/66-byte case (kagain_none_40). Both bodies end with the 'Generated by' footer.
EVIDENCE: python3 gen.py (7) on da2b06f, shape1.pdf: diagnostics 'Repair xref.offset-adjusted at 1730', 'Repair xref.rebuilt', 'Information object.redefined'. Findings: 'Error file.root-invalid', 'Warning file.size-wrong at offset 1579', 'Warning xref.section-shifted at offset 1730'. There is no xref.hybrid-stream-lost or xref.hybrid-stream-wrong. main shape1.pdf: file.size-wrong at 1984 and 1874 and section-shifted, with no hybrid-stream finding. Without revision 3 (/tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r9docs/gen_norev3.py), norev3_shape1.pdf on da2b06f gives 'Warning xref.hybrid-stream-lost at 1579 ... table at offset 1845' and 'Error xref.hybrid-stream-wrong at offset 1845'.
FIX: Edit #332's item 1: '... and the /XRefStm names S1, the previous revision's stream, which #156 reports at this table where no newer table names S1 first (the file without revision 3); here revision 3's /XRefStm names it first, and revision 2's naming is recorded as named again, unreported.'
VERDICT REASON: The finding holds. #332's item 1 says revision 2's /XRefStm naming S1 is something "#156 reports at this table". In the shape 1 file the body generates and measures (revision 3 present), nothing reports that naming at table 1845. This is true on da2b06f and on main, at 7 bytes off and at the exact offset alike. The reason: revision 3's /XRefStm reads S1 first, so revision 2's /XRefStm only gets a NamedAgain section record. That record is not a diagnostic or a finding. It sits at the stream's offset, not the table's, and the body itself calls the same record "record ... as named again" for shape 2. #156's reports at the table (Warning xref.hybrid-stream-lost naming table 1845, Error xref.hybrid-stream-wrong at 1845) appear only in the file without revision 3, which the body describes further down. The body's own shape 1 output block shows no such line, so the item contradicts the measurement printed under it. Main has no #156 report in any of these files. The text is wrong but nothing in the library is, and it sits in a debt issue body, not in shipped documentation, so the severity is nit. I checked the body's other figures for the three shapes, listed in the evidence, and they held. I did not re-run the two-revisions-late case or check the #330 comment, so I neither confirm nor dispute the finding's line that every other statement there held. No design question is involved.
VERDICT EVIDENCE: I rebuilt the reproducer myself.
- I extracted gen.py byte for byte from the posted #332 body, which matches the live issue as read through the GitHub tool. Running it gave shape1.pdf 2,065 bytes, shape2.pdf 1,974 and shape3.pdf 1,960.
- Section offsets: xref at 208; object 10 at 1579 with /Prev 208. Shape 1 adds object 11 at 1730, xref at 1845 (/Prev 1572 /XRefStm 1579) and xref at 1955 (/Prev 1845 /XRefStm 1579). Shape 2 has xref at 1730 (/Prev 1572 /XRefStm 1579) and xref at 1864 (/XRefStm 1579). Shape 3 has xref at 1730 (/Prev 1572) and xref at 1850.
- The probe is my own console program using the library's internals (built as AdCodicem.Pdf.Benchmarks). I built it once against main 21a0298 and once against da2b06f, and ran it one process at a time.

da2b06f, shape1.pdf:
- Sections: startxref Read Table 1955; /XRefStm Read Stream 1579; /Prev Read Table 1845; /XRefStm NamedAgain Unknown 1579; /Prev Relocated Stream 1730.
- Diagnostics: Repair xref.offset-adjusted at 1730 (158 bytes after it); Repair xref.rebuilt; Information object.redefined.
- Findings: Error file.root-invalid at object 1 0; Warning file.size-wrong at offset 1579; Warning xref.section-shifted at offset 1730; page-tree.resources-missing.
- No xref.hybrid-stream-lost and no xref.hybrid-stream-wrong anywhere.
- With gen.py 0 (exact offset), shape 1 gives Warning xref.chain-cycle at 1579, Repair xref.rebuilt and Error xref.chain-loop at offset 1845, again with no hybrid-stream line.

main, shape1.pdf:
- Sections: Relocated Stream 1579, then Table 208.
- Repair xref.offset-adjusted at 1579 ("7 bytes from there"); object 5 is "old five".
- Findings: file.size-wrong at 1984 and at 1874, xref.section-shifted at 1579. No hybrid-stream finding.

Without revision 3 (r9docs/gen_norev3.py), da2b06f:
- norev3_shape1.pdf gives "Warning xref.hybrid-stream-lost at 1579: The /XRefStm of the cross-reference table at offset 1845 names offset 1579, the section its /Prev names too..." and "FIND Error xref.hybrid-stream-wrong at offset 1845".
- norev3_shape2 gives the same at 1730. norev3_shape3 gives neither.
- On main, none of the norev3 files gets a hybrid-stream report.

The rest of the body's measured output matched on both trees:
- shape 2 and 3 lines on da2b06f (xref.section-missing at 1572, xref.rebuilt, Error xref.section-not-found at 1572);
- main's three files (offset-adjusted at 1579, WasRepaired False, section-shifted, "new five" for shapes 2 and 3, "six" in all three);
- the exact-offset loops on both trees, with main adding file.size-wrong.

Outputs: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r9ref332/out-head.txt, out-main.txt, out-head-norev3.txt, out-main-norev3.txt, out-head-exact.txt and out-main-exact.txt.
VERDICT FIX: Edit #332's item 1, replacing "and the `/XRefStm` names S1, the previous revision's stream, which #156 reports at this table" with: "and the `/XRefStm` names S1, the previous revision's stream. Revision 3's `/XRefStm` reads S1 first, so this naming is only recorded as named again, and nothing reports it at this table. #156 reports it (`xref.hybrid-stream-lost`, `Error xref.hybrid-stream-wrong` at 1845) only in the file without revision 3." This is a text-only edit to the issue body, with nothing to change in the code, and it needs no maintainer decision.
VERDICT NEEDS_MAINTAINER: False

######## [27] lens=docs key=tm616-empty-chain-read-in-part sev=nit -> verdict=confirmed sev=nit maint=?
LOCATION: docs/threat-model.md:616 ('No cross-reference stream's rows are read from what such a chain ... hands back: the stream cannot be read ([#221])')
CLAIM: Take a chain stopped at an unknown filter after a filter that decoded its data to nothing. filter.unsupported is reported ('decoding stopped there'), and the cross-reference stream is read in part, holding 0 rows, its /Prev followed. It is not 'cannot be read'. This is round 8's accepted consequence; the row's text misstates it. diagnostics.md's filter.unsupported row qualifies the case ('some of its data still to decode'); row 616 does not.
EVIDENCE: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r9docs/gen_undec.py u_empty_foo.pdf (/Filter [/FlateDecode /Foo], sound empty zlib, over an older table) on da2b06f: 'SEC startxref Read Stream at 306 / SEC /Prev Read Table at 163', 'Warning filter.unsupported at 431: The filter /Foo is not supported; decoding stopped there.', 'Warning xref.section-unreadable at 306: ... was read only in part: it holds 0 rows ...', finding 'Error xref.section-malformed ... it holds 0 rows where its /Index and /Size declare 1.'. u_foo.pdf (/Filter /Foo, data xxxxxxx) by contrast: 'SEC startxref Malformed Stream at 306', 'cannot be read: its data could not be decoded'.
FIX: Write 'from what such a chain, with data still to decode, ... hands back: the stream cannot be read ([#221]); data a filter before it decoded to nothing holds no rows and is read in part'.
VERDICT REASON: The finding holds. docs/threat-model.md:616 says a cross-reference stream left by "such a chain" (stopped at an unknown filter or at an element that names none), by a /Filter that is neither a name nor an array, or by Flate data corrupt before its first byte "cannot be read". The code says that only when data is left over: PdfFilterPipeline.cs:299 sets `undecoded = !data.IsEmpty && chain.End is Unsupported or NotAName or NotAFilterEntry`, and :209-212 breaks the loop on empty data. ReportEnd still reports filter.unsupported from reading the chain.

So a chain that reaches such a filter with nothing to decode does three things. It reports filter.unsupported ("decoding stopped there"). Its stream is read in part with 0 rows and its /Prev is followed. The validator says "is malformed: it holds 0 rows", not "cannot be read".

This is the accepted consequence the brief lists ("an unsupported filter given no data ... read in part (0 rows)"). The row's text misstates it, which the brief allows to be reported. The sibling row in diagnostics.md:136 qualifies the case ("some of its data still to decode"). Row 616 does not.

The case is wider than the finding says. A /Filter /Foo stream with /Length 0 has no filter before the unknown one, and it reads in part too. So does an element that names none after a filter that decoded to nothing. The finding's proposed fix ("data a filter before it decoded to nothing") would leave out the stream given no data at all.

None of r1 to r8 or decisions.md raised this for row 616. Round 8 [25] only changed its Flate wording. The sentence came in ed61728 and da2b06f edited it, so it is new in this batch: main has no such text. Severity is nit, as with round 8's [25] on the same sentence: a wording point on an accepted edge case, with no wrong behavior.
VERDICT EVIDENCE: I rebuilt the reproducer independently on da2b06f with a Release build of src/AdCodicem.Pdf. The generator is /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r9ref-tm616/gen.py and the probe is /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r9ref-tm616/probe/Program.cs. Each file has a classic table at 148 (objects 1-3), then a stream 5 that startxref names at 291, with /Index [3 1] and /Prev 148. Output is in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r9ref-tm616/out-head.txt.

These three cases give the same result:
- a_flate_empty_then_foo: [/FlateDecode /Foo] over sound empty zlib data.
- b_foo_no_data: /Filter /Foo with /Length 0.
- d_names_none_no_data: [/ASCIIHexDecode 42] over '>'.

That result is 'section startxref Read Stream @291', 'section /Prev Read Table @148' and wasRepaired=False. The diagnostics are 'Warning filter.unsupported at 416: The filter /Foo is not supported; decoding stopped there.' (for d: 'Element 1 of the /Filter entry is an integer, which names no filter; decoding stopped there.') and 'Warning xref.section-unreadable at 291: ... was read only in part: it holds 0 rows where its /Index and /Size declare 1; ...'. The validator finding is 'Error xref.section-malformed: The cross-reference stream at offset 291 is malformed: it holds 0 rows where its /Index and /Size declare 1.'

The controls with data left behave differently: c_foo_with_data is /Foo over 'xxxxxxx' and e_names_none_with_data is [/ASCIIHexDecode 42] over '41>'. Both give 'section startxref Malformed Stream @291', wasRepaired=True, 'Repair xref.rebuilt' and 'Error xref.section-malformed: ... cannot be read: its data could not be decoded.' f_flate_empty_alone (/FlateDecode over empty zlib) reads in part like a, b and d, but reports no filter.unsupported.

The text was introduced in ed61728 and edited in da2b06f (git log -S). It is absent on 21a0298.
VERDICT FIX: This is a wording fix in docs/threat-model.md:616, with no design choice involved. Qualify the sentence the way diagnostics.md:136 does, and cover a stream given no data, not only one whose earlier filter decoded to nothing:

"No cross-reference stream's rows are read from what such a chain, a `/Filter` that is neither a name nor an array, or Flate data corrupt before its first byte decodes, hands back with data still to decode: the stream cannot be read ([#221]). One that leaves it nothing to decode — no data, or data a filter before it decoded to nothing — gives no rows, `filter.unsupported` reported all the same, and the stream is read in part; the data an image filter leaves is read as rows all the same."

The same pass could settle the open point at concepts/reader-limits.md:119-120 by adding that a filter past the empty data is still read, and that one the library does not know is still reported as filter.unsupported.
VERDICT NEEDS_MAINTAINER: False

######## [28] lens=docs key=commit-d8e67de-body sev=nit -> verdict=narrowed sev=nit maint=?
LOCATION: commit d8e67de (docs: say which streams near a hybrid table's prev may be its own and report one passed over), body
CLAIM: This is for the status.md 'Commit bodies not rewritten' record. The body says 'an older revision's stream before the one the /XRefStm read is taken and the chain ends there'. That is false for one that gives a /Prev (finding older-stream-gap-drops-no-prev). It also says 'so that places are read at most 64 times a document', which leaves out the /Prev check a hybrid /XRefStm search makes whatever the bound (finding places-bound-row-overstates). The other ten bodies hold against their diffs. All 11 subjects are conventional and 67-93 characters long, issue numbers appear only in the Refs footers, and no review key appears.
EVIDENCE: older_t0_12.pdf on da2b06f: 'SEC /Prev Read Table at 208' after the taken stream, so the chain did not end. bound_prev_32.pdf: the /XRefStm is settled at the /Prev's place after the 32 were spent. Subject lengths from `git log --format='%h %s' 012e13d..da2b06f`: 93, 92, 67, 83, 84, 90, 86, 79, 83, 76, 92. Grepping the bodies for '#[0-9]' outside Refs lines and for review keys found nothing.
FIX: Do not rewrite the pushed body. Record both sentences under 'Commit bodies not rewritten' in docs/status.md at the batch's end.
VERDICT REASON: The first claim holds. d8e67de's body says, without a qualifier, that "an older revision's stream before the one the /XRefStm read is taken and the chain ends there". The consequence the maintainer accepted with R8-Q1 covers only such a stream that gives no /Prev; the source remark on TryRelocateXRefSection says "the chain ending there where it gives none". When that stream does give a /Prev, it is still taken, but the chain goes on through that /Prev. The older revision's own table is then lost, and the only reports are xref.offset-adjusted and xref.section-shifted. So the body is inexact, and the 'Commit bodies not rewritten' record should list it.

The second claim does not hold as evidenced. bound_prev_32 does show the /XRefStm search checking the place the /Prev names (KeepsItsPlace, called before the bound test at PdfFileReader.cs:5069) after the 32 places are spent. But that makes 33 place reads, not more than 64, so the body's "at most 64 times a document" is not contradicted. The check is a separate cost: KeepsItsPlace's remark bounds it at once per hybrid table, under MaxXRefSectionCount. It reads the place the table's /Prev names, which the chain reads next anyway. "Once the 32 are spent, no new place is tried" is in the page row (reader-limits.md:262), not in the body. So the second sentence belongs in the record only if the page finding places-bound-row-overstates is confirmed.

The rest of the finding holds: 11 conventional subjects of 67 to 93 characters (commitlint config-conventional allows 100), issue numbers only in Refs lines, and no review keys or British spellings in the bodies.
VERDICT EVIDENCE: I rebuilt the reproducer myself on da2b06f (origin/ccr-6d6c0ee6-dqm4lm-188). I wrote my own generator, /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r9ref-body/gen/gen.py, and a probe with a read-logging PdfFileSource, built against the head DLL. Outputs are in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r9ref-body/out-older-head.txt and out-bound-head.txt.

older_t0.pdf: S1 at 1628 gives /Prev 208, T1 at 1753 holds object 7, S2 at 1891 has no /Prev, and T2's /Prev 1640 falls 12 bytes into S1:
  SEC startxref Read Table at 2006
  SEC /XRefStm Read Stream at 1891
  SEC /Prev Relocated Stream at 1628
  SEC /Prev Read Table at 208
  OPEN repaired=False
  DIAG Repair xref.offset-adjusted at 1628
  OBJ 7 = PdfNull
  FIND Warning xref.section-shifted at offset 1628
The chain does not end at the taken stream, and T1's object 7 is lost.

older_none.pdf (the accepted case): "SEC /Prev Relocated Stream at 1628", nothing after it, xref.rebuilt, "FIND Error file.root-invalid". This matches what the body describes.

bound_prev_32.pdf, after the spender's search spent 32 places (31 null objects plus TN found 439 bytes off): "SEC /XRefStm NamedAgain Unknown at 1879", "READS-STARTING-AT 1876: 3", the same as bound_prev_0. bound_other_32.pdf: "SEC /XRefStm NotFound Unknown at 1561", "READS-STARTING-AT 1557: 0" against 2 in bound_other_0. So the bound stops ordinary places, but the check of what the /Prev names still runs (33 place reads in all). The bound test follows the leaveAt check on 012e13d too.

`git log --format='%h %s' 012e13d..da2b06f`: subject lengths 93, 92, 67, 83, 84, 90, 86, 79, 83, 76, 92. Grep of the bodies for '#[0-9]' outside 'Refs' lines, for review keys, and for British spellings: all empty.
VERDICT FIX: Do not rewrite the pushed body. At the batch's end, record under 'Commit bodies not rewritten' in docs/status.md that d8e67de says an older revision's stream before the one the /XRefStm read is taken and the chain ends there. That holds only where the stream gives no /Prev. Where it gives one, the chain goes on through it, and the older revision's table is lost with only a section-moved report. Add the 'at most 64 times' sentence to the record only if places-bound-row-overstates is confirmed against reader-limits.md:262. In that case, say that the figure leaves out the check of the place a hybrid table's /Prev names, which KeepsItsPlace makes at most once per hybrid table, past the bound.
VERDICT NEEDS_MAINTAINER: False

######## [29] lens=batch key=older-stream-before-S-ends-chain-in-silence sev=major -> verdict=narrowed sev=minor maint=?
LOCATION: docs/website/docs/concepts/lazy-reading.md:209-211; docs/threat-model.md:432 ('The rule leaves known gaps: an older revision's stream before the one the /XRefStm read, nearer than its own table, is taken, the chain ending there, the index rebuilt and the validator saying the file holds no catalog'); behaviour in src/AdCodicem.Pdf/IO/PdfFileReader.cs:5038-5043 (passOver only for candidate >= streamStart)
CLAIM: The texts describe R8-Q1's accepted gap as always ending in a rebuild at opening plus a false file.root-invalid. That holds only when the catalog lies in what the chain then loses. A first revision's stream normally indexes the catalog. When the taken stream does, nothing is rebuilt and nothing is reported beyond xref.offset-adjusted and xref.section-shifted. The objects that only the skipped sections index read as null, or as an OLDER revision, in silence. This breaks invariant 5 and serves a wrong object. Main does the same. 012e13d read these files whole. So this is worse than the consequence the maintainer accepted, and the gap the texts state is misstated. CrossReferenceChainTests.cs:5914-5922 pins only the catalog-in-the-lost-table shape, and its comment says so ('the catalog being the oldest table's alone').
EVIDENCE: Hand-built files: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r9bat/hand/, generator tools/hand.py. (1) a_first_hybrid_catalog_in_stream_n22.pdf. Revision 0 is hybrid: stream 20 at 278 (no /Prev) indexes objects 1-4, and table T0 at 426 (/XRefStm 278) indexes object 5. The update's table gives '/Prev 349 /XRefStm 587'. Branch da2b06f and main give the same result: 'Repair xref.offset-adjusted at 278: ... names at offset 349 was found 71 bytes before it.', WasRepaired False, GetObject(5) = null. The validator gives only Warning file.size-wrong and Warning xref.section-shifted, no Error. On 012e13d: 'found 77 bytes after it' at 426, and object 5 = '(five in the first revision's table)'. (2) a_middle_table_lost_n22.pdf. Revision 0 is a stream at 208 holding the catalog and '(old five)'. Revision 1 is a classic table at 405 with '(new five)' and /Prev 208. Revision 2 is a hybrid table with '/Prev 303 /XRefStm 524'. Branch and main: 'found 95 bytes before it' at 208, WasRepaired False, GetObject(5) = 'old five', no Error. The whole middle revision is lost in silence. On 012e13d: 'found 102 bytes after it' at 405, and object 5 = 'new five'. (3) Control a_first_hybrid_catalog_in_table_n22.pdf gives the stated consequence: xref.rebuilt and Error file.root-invalid. Differential (seed 202611080450): of the 23 files under default limits where da2b06f serves a wrong object and 012e13d the right one, 9 are this silent shape: f00061, f00844, f01126, f01284, f02029, f02164, f02430, f02678, f02983 (e.g. f00061: objects 6 and 8 null, 'found 64 bytes before it' at 4064, no rebuild). file.root-invalid counts under default limits: main 105, 012e13d 19, branch 45.
FIX: A maintainer question, since R8-Q1 was accepted on the stated consequence. (a) At least, restate the gap in lazy-reading.md:209-211, threat-model.md:432, the brief's accepted list and status.md. Where the stream taken indexes the catalog, nothing is rebuilt, and what only the sections farther off index reads as null or as an older revision with nothing reported. Add a test row for each of shapes (1) and (2) beside A_stream_before_the_one_a_hybrid_table_s_xrefstm_read_that_gives_no_prev_is_taken_where_it_comes_nearest_the_chain_ending_there. (b) Or report it: when the search near a hybrid /Prev takes a stream before S that gives no /Prev, ending the chain, while a section the window held farther off (an xref keyword, or a stream) was left untried, give a Warning at that section, the way xref.stream-passed-over does. Marking the index incomplete would not cover shape (2), whose old row stands. Otherwise file it as debt with these reproducers.
VERDICT REASON: The reproducers hold. Shapes (1) and (2) read the same on da2b06f and on main 21a0298; only main's message wording differs. 012e13d reads both whole. But the behaviour is no regression against main and is not the decided rule misapplied, so it is not major.

(a) The rule works as decided. PdfFileReader.cs:5044 passes over a candidate only when `candidate >= streamStart`. A stream before S therefore competes nearest-first, which is R8-Q1 as decided. lazy-reading.md:200-201 and threat-model.md:432 state that rule: 'tried where it comes nearest, whatever its /Prev'.

(b) The loss is the generic nearest-first ambiguity, not something the own-stream rule adds. A classic analog puts an older table where the stream was: T0 at 208, the meant T1 at 394, the update's /Prev 298. It loses the middle revision the same way on main, 012e13d and da2b06f.

(c) This consequence class was already put to the maintainer. The R8 judge reported about 186 seeded blocks going 'back from head's reading to main's'. R8-Q1 also accepted, for S giving no /Prev, 'revisions between lost, nothing reported of them but the section found moved'. Shape (2) is a first revision's stream nearer than a newer table; it is exactly the stated nearest-first rule, not a gap of the own-stream rule.

What survives is the text. lazy-reading.md:209-211 and threat-model.md:432 give the first known gap's consequence without condition: 'the map rebuilt as the document opens and the validator saying the file holds no catalog'. That is true only where the catalog is indexed solely by the sections the chain skips. In two cases the catalog is reachable anyway:
- the stream taken indexes it (shape 1, and the f00061-type seeded files);
- a section the chain did read indexes it, as when an update rewrote the catalog, which is common.
In both, nothing is rebuilt. What only the skipped table indexes reads as null, or as an older revision. The only reports are Repair xref.offset-adjusted and validator Warning xref.section-shifted.

That is a worse outcome than the one stated: the stated rebuild recovers those objects (object 5 in the control), while here they stay null. Because the texts misstate it, the finding stands under the brief's rule. The test comment at CrossReferenceChainTests.cs:5914-5922 is accurate to its own fixture ('the catalog being the oldest table's alone'). Nothing pins the silent variant.
VERDICT EVIDENCE: I rebuilt the reproducers with the finder's generator; the files are byte-identical to r9bat/hand. I wrote my own probe: default options, objects 1-22, diagnostics, and the validator on a second open. I built it against da2b06f, a scratch worktree at 21a0298 and one at 012e13d (since removed), one dotnet process at a time.

(1) a_first_hybrid_catalog_in_stream_n22.pdf
- da2b06f: repaired-at-open=False. Object 5 is null. 'D Repair xref.offset-adjusted at 278: The cross-reference section /Prev names at offset 349 was found 71 bytes before it.' Findings: Warning file.size-wrong (a /Size artifact of the generator), Warning xref.section-shifted at offset 278, Warning page-tree.resources-missing. No Error.
- main: the same, except the message says 'was found -71 bytes from there.'
- 012e13d: 'at 426 ... was found 77 bytes after it'. Object 5 is '(five in the first revision's table)'.

(2) a_middle_table_lost_n22.pdf
- da2b06f and main: object 5 is '(old five)'. 'xref.offset-adjusted at 208 ... 95 bytes before it' (main: '-95 bytes from there'). No rebuild, no Error.
- 012e13d: 'at 405 ... 102 bytes after it'. Object 5 is '(new five)'.

(3) Control, a_first_hybrid_catalog_in_table_n22.pdf
- da2b06f and main: xref.offset-adjusted at 278, then 'Repair xref.rebuilt', repaired=True. 'F Error file.root-invalid at object 1 0 ... No object of the file is a catalog.' The rebuild still serves object 5 '(five in the first revision's table)'.
- 012e13d: read whole at 393.

(4) My classic analog (analog.py; T0 at 208, T1 at 394, /Prev 298), as a classic update and as a hybrid update
- On main, 012e13d and da2b06f alike: object 5 is '(old five)'. 'xref.offset-adjusted at 208 ... 90 bytes before it' (main: '-90 bytes from there'). No rebuild, no Error.

Files are in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r9ref-oldstream/: files/, files2/, analog.py, probe/Program.cs, out-branch.txt, out-main.txt, out-r7.txt and out2-branch.txt, out2-main.txt, out2-r7.txt.
VERDICT FIX: Restate the first known gap in docs/website/docs/concepts/lazy-reading.md:209-211 and docs/threat-model.md:432, along these lines: 'an older revision's stream before the one the /XRefStm read, nearer than its own table, is taken, and the chain ends there: what only the sections past it index is lost, reported only as the section found moved, and where that includes the catalog the map is rebuilt as the document opens and the validator says the file holds none (#260)'. Carry the same wording into the accepted list for status.md.

Add a row beside A_stream_before_the_one_a_hybrid_table_s_xrefstm_read_that_gives_no_prev_is_taken_where_it_comes_nearest_the_chain_ending_there in which the stream taken indexes the catalog. It should pin: WasRepaired false; the only diagnostic XRefOffsetAdjusted at the stream; the object only the table indexes reading as null; no validator Error; XRefSectionShifted.

The finding's option (b), a Warning for a section the window held farther off and left untried, is a design choice. It would change the generic nearest-first search, which loses the same revision with a classic table on main too. It is not needed for this batch. If the maintainer wants it, file it as debt beside #260 with both the stream reproducers and the classic analog.
VERDICT NEEDS_MAINTAINER: False

######## [30] lens=batch key=passed-over-warning-names-a-comment-header-a-stream sev=minor -> verdict=narrowed sev=minor maint=?
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:5157-5160 (defers on MayBeTablesOwn before any line test) and :5129-5132 (records it as passed over); message :3716; docs/website/docs/reference/diagnostics.md:116
CLAIM: Round 8's new Warning xref.stream-passed-over is made for an object header inside a comment, or inside a string opened on the same line, and calls it 'the cross-reference stream at offset N'. A place that may be the table's own is deferred on its dictionary alone, before the line test (IsTokenOnItsLine) that keeps the word xref, or such a header, in a comment or a string out for unreadable places. If another section is taken first, the deferred place is reported. The texts say such a header is not taken, and the Warning's row does not name the #171 limit either (stream data). Neither main nor 012e13d gives such a Warning. Main takes the comment header instead, and 012e13d reads the file silently and right.
EVIDENCE: hand/b_header_in_comment_n22.pdf: revision 0 is a classic table at 204 (the catalog). Revision 1 is a hybrid update. Past its stream S1 (386) comes the line '% 9 0 obj << /Type /XRef /Size 3 /W [1 1 1] >> stream' (the header starts at 503), then its table at 555 with '/Prev 483 /XRefStm 386'. Branch: 'Repair xref.offset-adjusted at 204: ... names at offset 483 was found 279 bytes before it.' and 'Warning xref.stream-passed-over at 503: The cross-reference stream at offset 503, near offset 483, which the /Prev of the cross-reference table at offset 555 names, gives no /Prev, or one that names that offset too, as that table's own stream may: ... the chain did not read the stream. If it is that table's own stream, the objects only it indexes are missing from the index.' 012e13d gives the same, without the Warning. Main: 'found -10 bytes from there' at the comment, stream.truncated, xref.rebuilt and Error file.root-invalid. The same Warning comes for b_header_in_string_n22.pdf (line '8 0 obj\n(9 0 obj << /Type /XRef ... >> stream)', Warning at 510) and for b_header_in_streamdata_n22.pdf (Warning at 541, #171's context). Differential: f01887_n62 under D reports 'xref.stream-passed-over at 8605', whose bytes are '% 61 0 obj << /Type /XRef /W [1 4 2] /Size 3 >> stream'. f02186 and f00267 report a header quoted in stream data.
FIX: Before a place is deferred as possibly the table's own, and so before it is recorded in _streamsPassedOver, ask IsTokenOnItsLine of its header, as for an unreadable place. A header that is no token on its line declares nothing, and is dropped like any other place. Name the stream-data limit (#171) in the xref.stream-passed-over row of diagnostics.md and in the ReportStreamsPassedOver remarks. Add a row with the comment and the same-line string to the passed-over tests.
VERDICT REASON: The finding holds, but on a different class of place than its own files show, and its proposed fix is wrong.

What is false: its premise for its own reproducer. The comment header in b_header_in_comment_n22.pdf is '% 9 0 obj << /Type /XRef /Size 3 /W [1 1 1] >> stream', and the string header in the string file is the same. Both read in part: with no /Length, the data runs to the end of the file and gives rows. The texts say 'A place that reads, whole or in part, is taken before any line is lexed, whatever its line is' (PdfFileReader.cs:5106-5108, IsTokenOnItsLine remarks). The line test is asked only of a Malformed place (:5111-5113). The code does what the texts say: with no other section near, the branch takes such a header when it comes back to it (ref_comment_alone: '/Prev Relocated Stream 1229'), and main takes it as the nearest place. So for these files, a Warning that the place 'may be the table's own stream' matches the reader's own rule. 'The texts say such a header is not taken' is false for them. The stream-data file is #171's context (known, and consistent with #171's accepted 'taken wherever its line, lexed alone, reads it as a token').

What holds: 9a07bb9 records every place passed over on its dictionary alone (:5129-5132, deferred at :5157-5160) and reports it once the chain is read. It does so whatever the search would make of the place if it came back. Where the place cannot be read and declares nothing, the search drops it, yet the Warning calls it 'the cross-reference stream at offset N'. Three such places:
- a /Type /XRef header in a comment whose stream cannot be read (no /Size);
- the same in a string opened on its line;
- a /W-only stream (no /Type) that cannot be read, a token on its own line, no comment at all.

The search drops the last one as 'may be anything' (remarks at :4908-4909). Round 8 [16] settled the /W-only deferral on 'No reader sees a difference, since the dictionary is dropped either way'. The new Warning makes that false.

This is a regression in reports against 012e13d and main (the code exists in neither; git grep finds none). It is a hedged Warning, though. Objects served, repairs and validator findings do not change, and the shapes are contrived. So: minor.
VERDICT EVIDENCE: I rebuilt the reproducers with my own generator: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r9ref-cmt/gen.py. The probe is saved as /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r9ref-cmt/ZzRefCommentPassedOverTests.cs.txt, and its output is /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r9ref-cmt/out-head.txt (on da2b06f).

Every file has the same two revisions:
- revision 0: objects 1 to 3 and 5 '(old five)', a classic table at 208;
- revision 1: object 4, then S1 = stream 21 at 410 (no /Prev);
- then the variant line, then the table, whose trailer gives /Prev = header - 20 and /XRefStm 410.

Each '_alone' file adds a 700-byte comment, so revision 0's table lies outside the 512 bytes searched.

1. The reviewer's class reproduces on the branch.
- ref_comment (header with /Size 3): 'Repair xref.offset-adjusted @208 ... found 299 bytes before it.' then 'Warning xref.stream-passed-over @527: The cross-reference stream at offset 527, near offset 507, ...'. Objects 4 'four' and 5 'old five'.
- ref_string: the same Warning, @534.

But the search, coming back, takes these headers:
- ref_comment_alone: 'sec /Prev Relocated Stream 1229', 'stream.truncated @1281', 'xref.entry-reserved @1229 ... gives 3 rows a reserved type', 'xref.rebuilt'.
- ref_string_alone: the same, '/Prev Relocated Stream 1236'.
- ref_cmtreadable_alone (a comment header with /Length 3 and data on the next lines): 'Relocated Stream 1229', 'xref.section-unreadable ... read only in part'.
- main's output in r9bat/out-hand-main.txt for b_header_in_comment_n22.pdf: 'xref.offset-adjusted at 503 ... found 20 bytes from there', 'stream.truncated'.

So these headers read in part, and the reader treats them as sections.

2. The narrowed class.
- ref_cmtmalformed, line '% 9 0 obj << /Type /XRef /W [1 1 1] >> stream' (no /Size), table at 571 with /Prev 507 /XRefStm 410: 'Warning xref.stream-passed-over @527: The cross-reference stream at offset 527, near offset 507, which the /Prev of the cross-reference table at offset 571 names, gives no /Prev, ...'.
- ref_cmtmalformed_alone: 'sec /Prev NotFound Unknown 1209', 'Warning xref.section-missing @1209', 'xref.rebuilt', 'F Error xref.section-not-found @1209: ... the offset holds neither the xref keyword nor an object.' The line test dropped it.
- ref_strmalformed (line '(9 0 obj << /Type /XRef /W [1 1 1] >> stream)'): Warning @534. Its _alone file: 'NotFound', 'section-missing', 'section-not-found'.
- ref_wonly ('9 0 obj << /W [1 1 1] >> stream', a token on its line): Warning @525. ref_wonly_alone: 'NotFound Unknown 1207', 'section-missing', 'section-not-found ... neither the xref keyword nor an object'.

3. Controls.
- ref_realmalformed (the same header, not in a comment): Warning @525. Its _alone file: 'Malformed Stream 1227', 'xref.offset-adjusted', 'xref.section-unreadable ... it has no /Index, and its /Size gives no count of objects', 'Error xref.section-malformed'. It declares itself, so the Warning matches.
- ref_realstream: Warning @525, as designed.
- ref_none: no Warning.

4. History. git grep finds 'stream-passed-over' in neither 012e13d nor 21a0298; it was introduced by 9a07bb9. The reviewer's r7 output for b_header_in_comment shows only xref.offset-adjusted.
VERDICT FIX: Do not apply the proposed line test before the deferral. It would drop a comment or same-line-string header that reads in part, which the search otherwise takes whatever its line, and the rule would then depend on that header's /Prev: one naming another offset is still taken nearest-first. It would also miss the /W-only case, which involves no comment.

Instead, record a place in _streamsPassedOver (:5129-5132) only where the search, coming back, would take it:
- one that declares itself: /Type /XRef and a header that is a token on its line (IsTokenOnItsLine, the window being still open there), the condition at :5111-5113;
- one that reads, whole or in part. A /W-only stream, or a header in a comment or a string opened on its line, qualifies only this way, which takes reading it as the retry would. That is one read per recorded place, bounded by MaxRelocationCandidates. ReportStreamsPassedOver's remark 'nothing is read to report it' then changes.

If that read is unwanted, there is a cheaper choice for the fix stage: report only typed headers that are tokens on their line. It drops the Warning for a readable /W-only stream, which ISO 32000-1 Table 17 does not allow for a writer's own stream anyway, and for a commented-out header.

Tests: three rows in the passed-over family, each with another section taken first and no Warning expected, and alone, dropped as now:
- a comment header with no /Size;
- a same-line string header with no /Size;
- an unreadable /W-only stream.

Keep the readable comment-header row as a control: Warning kept, or taken when alone.

Texts:
- the diagnostics.md:116 row and the ReportStreamsPassedOver summary should say the place reported is one the search would take;
- the remark at :4908-4909 ('An object that only gives a /W ... is dropped') should add that, near a hybrid table's /Prev, such an object is first passed over (round 8 [16]'s missing clause).

The #171 stream-data context needs no new text in the row: it is the documented limit.

No maintainer question is needed: the rule that a place reported is one the search would take follows from the existing line test and typing rule.
VERDICT NEEDS_MAINTAINER: False

######## [31] lens=batch key=passed-over-warning-false-after-rebuild-at-opening sev=minor -> verdict=refuted sev=none maint=?
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3672-3679 (reported at the chain's end, before Initialize's rebuild), message :3716; docs/website/docs/reference/diagnostics.md:116
CLAIM: The Warning is kept back only when the chain indexed nothing ('the whole file then rebuilt'). The index is also rebuilt at opening when the catalog cannot be resolved, or when a loop leaves it lost. In that case the Warning still says 'the objects only it indexes are missing from the index', which is false in the opened document: the rebuild scanned the file and serves them. The rationale the batch gives for the empty-chain exception applies just as much here.
EVIDENCE: hand/c_passed_over_then_rebuilt_n22.pdf: revision 0's table gives the catalog's row offset 3009 (outside the file). The hybrid update's own stream 22 at 557, which nothing names, gives no /Prev and alone indexes object 7. The update's table gives '/Prev 537 /XRefStm 386'. Branch: 'Warning xref.stream-passed-over at 557: ... If it is that table's own stream, the objects only it indexes are missing from the index.', then 'Warning xref.entry-out-of-range: The entry of object 1 places it at offset 3009, outside the file.' and 'Repair xref.rebuilt'. WasRepaired is True at opening, and GetObject(7) = '(seven, only in the update's own stream)'. Differential: 6 files under default limits rebuild at opening beside the Warning, e.g. f01377_n49 (chain-cycle, then rebuild; object 5 served from the passed-over stream at 1666).
FIX: Report the passed-over streams from Initialize once it is known that no rebuild came at opening (skip them when _repaired), as the empty-chain case does. Or word the message as what the chain's index lacked ('were missing from the index the chain built'). Either way, add a row to the passed-over tests with a rebuild at opening.
VERDICT REASON: The behavior reproduces, but it is the documented and stated design, not a misstatement.

All four texts describe this exact case. Each says the report leaves the index as it is, and a rebuild for another cause alone finds the stream's objects:
- the XML doc of PdfDiagnosticCodes.XRefStreamPassedOver (src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:121-123): 'only a rebuild for another cause, which scans the file, finds them';
- docs/website/docs/reference/diagnostics.md:116: 'nothing is rebuilt for them, and only a rebuild for another cause, which scans the file, finds them';
- docs/website/docs/concepts/lazy-reading.md:208-209;
- docs/threat-model.md:432: 'a rebuild for another cause alone finding it'.

R8b raised this very case as deviation 3 in r8-fix-result.json: 'A rebuild for another cause can follow it, for example at opening for a missing catalog'. The brief lists the passed-over Warning leaving the index as it is among the accepted consequences.

The message is conditional ('If it is that table's own stream') and describes the index the chain built. The same ordered report then says 'Repair xref.rebuilt: The cross-reference index was rebuilt by scanning the file', so a reader of the diagnostics sees the index replaced.

The empty-chain exception is a different kind of case. There the chain built no index at all, so the claim would be about nothing. Here the chain's index existed and lacked the stream.

The proposed fix would not make the Warning true in every case. It would suppress the Warning only for a rebuild at opening. A rebuild for another cause that comes later, when a load meets an entry the index got wrong or an index left incomplete for another cause, would make the message just as stale, and no report made at Initialize can foresee it. The documented rule ('a rebuild for another cause finds them') is the one that covers every timing.
VERDICT EVIDENCE: I rebuilt the branch at da2b06f (Release) and ran a probe test (since removed) that opens the reviewer's file /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r9ref-passover/c_passed_over_then_rebuilt_n22.pdf under default options. The output is in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r9ref-passover/out.txt:

```
repairedAtOpen=True count=8
Repair xref.offset-adjusted at 204: The cross-reference section /Prev names at offset 537 was found 333 bytes before it.
Warning xref.stream-passed-over at 557: ... If it is that table's own stream, the objects only it indexes are missing from the index.
Warning xref.entry-out-of-range: The entry of object 1 places it at offset 3009, outside the file.
Repair xref.rebuilt: The cross-reference index was rebuilt by scanning the file.
O7: "seven, only in the update's own stream"
```

So the mechanics are as the finding says. What the texts say about it:
- src/AdCodicem.Pdf/IO/PdfFileReader.cs:3672-3679 keeps the report back only when _xref.Count == 0, as decisions.md's R8b entry records ('not made when the chain indexed nothing').
- PdfDiagnosticCodes.cs:121-123, diagnostics.md:116, lazy-reading.md:208-209 and threat-model.md:432 each state that a rebuild for another cause finds the objects.
- r8-fix-result.json reports[1].deviations[3] names 'at opening for a missing catalog' as such a cause.

This Warning is new in 9a07bb9, so main 21a0298 and 012e13d have nothing to compare against.
VERDICT FIX: None required. If the maintainer wants the message to stand alone even when a rebuild follows, a nit-level rewording would do it without a design change: '...the objects only it indexes are missing from the index the chain built'. Reporting from Initialize only when nothing was repaired at opening is a design choice for the maintainer. It would still leave the Warning stale after a rebuild that comes later, so I would not take it.
VERDICT NEEDS_MAINTAINER: False
