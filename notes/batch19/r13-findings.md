# Review round 13 (wf_40e79506-059): findings and refuter verdicts

## 1. leave-at-told-by-byte — major → refuted none (reached-test)
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:5324 (`if (candidate == leaveAt)`; leaveAt is StartOfNamed's token start, :3542/:3616); texts that claim the opposite: docs/threat-model.md:447 (defense: the search 'comes to where that /Prev keeps its place'), docs/website/docs/concepts/lazy-reading.md:125-129, docs/website/docs/reference/diagnostics.md:115; #327's Known gaps entry docs/threat-model.md:520-533 does not list it

Claim: The /XRefStm's search near the offset it names stops at the place the hybrid table's /Prev names (KeepsItsPlace, #156) only where a candidate equals leaveAt byte for byte. A cross-reference stream whose header is written with a run of signs and which the /Prev names at a sign of that run (or at the white space before it, leaveAt then being the run's first sign) is found by the walk back from obj at its digit, so KeepsItsPlace is never asked there; the same where a % is on its number's line and the /Prev names a later sign or the digit (the walk finds it at the first sign). The search then takes the stream as the /XRefStm's section found moved, and the /Prev reads it a second time. Readable: the stream is read twice (as the table's stream and as its previous section), no xref.hybrid-stream-lost, no Error xref.hybrid-stream-wrong, the index not left incomplete. Lost: xref.section-unreadable and Error xref.section-malformed are each made twice. The unsigned twin, and the same signed file whose /Prev names the digit, are left to the /Prev with #156's reports. This is the comparison R12a/R12g/R13a moved from the byte to the digits in the reached test, left by byte in the sibling test of the same loop. Not a regression against main 21a0298 (main reads the stream twice with no report for the signed and the unsigned file alike), but #156's decision as the pages and the threat model state it does not hold for this shape, and nothing (src, pages, #327) records it as a gap.

Evidence: Files W/r13wip/reached-test/files3leave (gen3.py): revision 1 = classic table T0 at 200; stream 5 at 450, its header written as named, /Type /XRef /Index [4 1 7 1] /W [1 4 2] (ok) or [1 -4 2] (lost), /Prev 200; newest table with /XRefStm 447 (inside 'endobj' before the stream) and /Prev 450+p. Dumps out3-head.txt (dfb03fc), out3-dad.txt (dadde18, identical for these), out3-main.txt.
leave-p-p0-xm3-oldest-ok.pdf ('+5 0 obj', /Prev 450 = its sign), head:
  S /XRefStm named=447 Relocated Stream at=451 / S /Prev named=450 Read Stream at=450 / S /Prev named=200 Read Table
  D Repair xref.offset-adjusted at 451: The cross-reference section /XRefStm names at offset 447 was found 4 bytes after it.
  F Warning xref.section-shifted at offset 451   (nothing else)
Twin leave-u-p0-xm3-oldest-ok.pdf ('5 0 obj'), head: S /XRefStm named=447 NamedAgain / S /Prev named=450 Read Stream at=450; D Warning xref.hybrid-stream-lost at 447 ('...names offset 447, what its /Prev names too...'); DA xref.rebuilt when objects are asked; F Error xref.hybrid-stream-wrong at offset 1820. leave-p-p1-xm3-oldest-ok.pdf (same '+5' file, /Prev 451 = the digit): NamedAgain + hybrid-stream-lost + Error, like the twin.
leave-p-p0-xm3-oldest-lost.pdf, head: S /XRefStm Malformed Stream at=451, S /Prev Malformed Stream at=450; D Warning xref.section-unreadable at 451 (found at offset 451) and D Warning xref.section-unreadable at 450; F Error xref.section-malformed at 451 and at 450. Twin leave-u-p0-xm3-oldest-lost.pdf: hybrid-stream-lost at 447, one section-unreadable at 450, Errors section-malformed 450 and hybrid-stream-wrong 1821.
leave.py over out3-head.txt: every readable row whose /Prev names a sign or the white space before the run ('+5','++5','+-5' at p-1/p0, '++5' at p1, '-+5' at its '+' p1, '++5 0 %c\nobj' at its second sign or digit p1/p2) shows '/XRefStm Relocated ... /Prev Read ...' with no LEFT; every unsigned row and every row naming the digit (or the first sign of the % shape) shows 'LEFT E'. Main: leave-u-p0 and leave-p-p0 ok both give '/XRefStm Relocated Stream /Prev Read Stream' with no report (lost: both take T0 twice).
With the existing theory's own builder (A_section_at_the_file_s_first_byte_is_a_place_like_any_other), probe ZzRtLeave.cs.txt, output leave-firstbyte.txt: ('/Prev 0 /XRefStm 4', number '9') -> '/XRefStm NamedAgain 4, /Prev Read 0' + Warning xref.hybrid-stream-lost at 4; ('/Prev 0 /XRefStm 4', number '+9') -> '/XRefStm Relocated 1, /Prev Read 0' + only Repair xref.offset-adjusted at 1; ('/Prev 1 /XRefStm 5', '+9') -> NamedAgain + hybrid-stream-lost.

Fix: Tell the place the /Prev names by its digits, as the reached test now does: compute once leaveDigits = the first non-sign byte at or past leaveAt (DigitsPastSigns over the window where leaveAt lies in it, from the probe StartOf already read otherwise; leaveAt itself for xref) and ask KeepsItsPlace where `candidate == leaveAt || (leaveAt >= 0 && DigitsPastSigns(window.Memory.Span, start - lead, candidate) == leaveDigits)` (a '-' at leaveAt then reads no object and keeps no place, the search going on as now). Rows: the first-byte theory with ('/Prev 0 /XRefStm 4', '', '+9', '/XRefStm NamedAgain 4, /Prev Read 0'), and the leave-* shapes (readable and lost, '+5'/'++5' at the sign and the white space before, '-+5' at its '+', '++5 0 %c' at its second sign) pinned to their unsigned twin's sections and reports. If the maintainer leaves it to #327 instead, add it to #327's Known gaps entry (threat-model.md:520-533), qualify threat-model.md:447 / lazy-reading.md:128 / diagnostics.md:115, and post the shape on #327.

Refuter: verdict refuted, severity none, needs_maintainer False
Reason: The behavior reproduces exactly as the finding says, but #332 already holds it, and the batch is no worse than main. Three points.

1. The finding is the by-byte class that #332's round-12 tracking comment (6769965753) records. The brief accepts that comment and lists it under "what stays #332's".
   - In this shape, a hybrid table's /XRefStm search finds a header at one byte of its token, and the /Prev starts its reading at another byte of the same token. The two places are compared by byte, so one stream counts as two sections.
   - The comment's pair.pdf is the finding's lost shape: '+-13 0 obj', the /XRefStm naming a place inside the dictionary, the /Prev naming the first sign. Its consequence is two section-unreadable and two Error section-malformed, while the unsigned twin gets hybrid-stream-lost with Error hybrid-stream-wrong. The comment also states the rule in general: "a header the /XRefStm's search finds at its digit and a /Prev that names its first sign are two sections". Its "by its data" option would make pair.pdf read as pair-plain.pdf, which is exactly the fix the finding proposes.
   - Round 12's verdict on finding 16 had already seen this mechanism (seed949_067: the /XRefStm finds '+-13' moved at its digit, the /Prev names its sign) and assigned it to #332.
   - The unsigned version gives the same outcome: '15 0 obj' with the /Prev one byte into the number. That is #332's accepted case, "a link one byte into '13 0 obj' doubles its reports".
   - The signed variants add nothing new. The finding's '++5 0 %c' shape, the /Prev at a later sign or at the digit, is a /Prev naming a byte inside the token past where the walk found it. That is #332's case directly.
   - One inaccuracy: the comment cites the test as `hybridStart == previousStart` (:3524). pair.pdf actually goes through `candidate == leaveAt` in TryRelocateXRefSection (:5324). The shape and the consequence are recorded correctly all the same.

2. The batch is not worse than main 21a0298.
   - Readable form: main reads the stream twice with no report, for signed and unsigned files alike. The branch does the same.
   - Lost form: main silently takes the oldest table twice. Objects 4 and 5 come back null, and object 8 comes from the older revision, a wrong object. On the '++5 0 %c' shape, main takes that oldest table for the /XRefStm even with the stream readable, and again serves object 8 from the older revision. The branch serves every object right; only its reports are doubled or missing.

3. No text newly misstates this.
   - threat-model.md:447 lists only the cases where the search "comes to where that /Prev keeps its place". Here the search comes to the digit, not to that byte, so the sentence is not false.
   - lazy-reading.md:128 and diagnostics.md:115 are equally imprecise for #332's accepted unsigned case (one byte into the number). The pages do not describe #332's by-byte class anywhere, and round 12's triage accepted that, asking for no page change.
   - R12-Q3 and R13-Q0 decided the reached test only. The test against the /Prev's place was never in their scope.

Refuter evidence: I built everything myself (W = C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud). The generators are W/r13wip/verify-leave-at-told-by-byte/genv.py (92 files in files/) and genv2.py ('15 0 obj' and '+15 0 obj', files2/); the probe is ZzVlaDump.cs.txt and the summarizer summ.py. Each file has the oldest table T0 at 162 indexing objects 1-3 and 8, then objects 4 and 8, two blank lines, and stream 5 at 382 written under the header the file name gives (/Type /XRef, /Index [4 1 5 1 8 1], /Prev 162). The ok files use /W [1 4 2]; the bad files use /W [1 4 x]. The newest hybrid table has /XRefStm 376 (inside 'endobj', variant xb) or 407 (inside the dictionary, variant xd), and its /Prev names 382+n.

dfb03fc (out-head.txt):
- v-p-n0-xb-ok ('+5', /Prev 382 = its sign): '/XRefStm Relocated@383, /Prev Read@382, /Prev Read@162'. Diagnostics: only Repair xref.offset-adjusted at 383 and F Warning section-shifted. Objects 4="r2 four", 8="r2 eight".
- The unsigned twin v-u-n0-xb-ok, and v-p-n1 (/Prev at the digit): '/XRefStm NamedAgain@376', Warning hybrid-stream-lost at 376, rebuild, F Error hybrid-stream-wrong.
- v-p-n0-xb-bad: '/XRefStm Malformed@383, /Prev Malformed@382'. section-unreadable at 383 and at 382; F Error file.trailer-malformed at 383 and at 382. The twin gets hybrid-stream-lost, one section-unreadable and Error hybrid-stream-wrong.
- The same split appears for '++5' at n-1/0/1, '+-5' at n-1/0, '-+5' at n1, and '++5 0 %c' at n1/n2.
- v-pm-n0-xd-bad is #332's pair.pdf shape and gives the same two section-unreadable and two Errors that the comment records.
- Unsigned class (out2-head.txt): v-u15-n1-xb-ok ('15 0 obj', /Prev 383, one byte into the number) gives '/XRefStm Relocated@382, /Prev Read@383' with no hybrid-stream-lost. v-u15-n1-xb-bad gives section-unreadable at 382 and at 383 and two Errors. This is the same outcome as the signed rows.

main 21a0298 (out-main.txt):
- v-u-n0-xb-ok and v-p-n0-xb-ok both give '/XRefStm Relocated Stream, /Prev Read Stream' with no report.
- Every bad file gives '/XRefStm Relocated Table@162, /Prev Relocated Table@162' and objects 4=null 5=null 8="r1 eight", with nothing said of the stream.
- v-ppc-n*-xb-ok gives '/XRefStm Relocated@162' and 8="r1 eight".

On record:
- #332 comment 6769965753: pair.pdf ('+-13 0 obj', /XRefStm 450 inside its dictionary, /Prev 420 its first sign) gives section-unreadable at 422 and at 420 and Error section-malformed at both. pair-plain.pdf gets hybrid-stream-lost and Error hybrid-stream-wrong.
- r12-findings.md finding 16 verdict: seed949_067 shows the same split.
- Brief, accepted: "What stays #332's: ... the hybridStart == previousStart test", and the #332 comment 6769965753.

Refuter fix: No change is needed in this batch. The shape stays #332's, to be settled by #332's decision; its "by its data" option covers it.

Optional tracking nit, for the closing comments: add a line on #332 saying two things.
- pair.pdf goes through `candidate == leaveAt` in TryRelocateXRefSection (the place a hybrid table's /Prev names, asked through KeepsItsPlace), not only through `hybridStart == previousStart`.
- The readable form reads the stream twice with no xref.hybrid-stream-lost, as main does.

Then whoever implements #332 by its data compares the /Prev's place by its digits too (leaveDigits = DigitsPastSigns at leaveAt, matched against DigitsPastSigns at the candidate), with the reviewer's rows as tests.

This is not a maintainer decision: #332 already waits on that decision.

## 2. n8-equivalence-refuted — minor → confirmed minor (reached-test)
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:5508 (NumberRunsInto, `candidate < streamStart && NumberEnd(...)`); R12b's ADR 53 record in r12-fix-result.json ('N8 ... a no-op')

Claim: R12b classed N8 ('<' becomes '<=' in the inside-the-number arm) equivalent: 'a candidate equal to the stream's start maps to that start, a no-op'. It is not: the stream the table's /XRefStm read, put back unread because its /Prev names the table's /Prev, comes back to NumberRunsInto as a retry whose candidate is streamStart; where that reading started two or more bytes before the window's span (a run of three or more signs read at its first sign, the byte the window holds before the bytes searched lying inside the run, the digit within them), '<=' calls NumberEnd(span, spanStart, streamStart) with a negative slice start and the open throws ArgumentOutOfRangeException. The real code is safe (candidate < streamStart is false there); no test builds the input, so the mutant survives the suite.

Evidence: W/r13wip/reached-test/files2/n8-s4-k515-table.pdf (gen2.py: T0, 1,198 dashes, objects 4 and 7, stream 5 '++++5 0 obj' at 1650 whose /Prev is 2165, 1,198 dashes, newest table /XRefStm 1650 /Prev 2165 = 1650+515, so the bytes searched start at 1653 and the window's byte before is 1652). Head (out2-head.txt): S /XRefStm named=1650 NamedAgain; S /Prev named=2165 Relocated Stream at=1650; D Repair xref.offset-adjusted at 1650 '...was found 515 bytes before it.'; D Warning xref.hybrid-stream-lost at 1650; D Warning xref.chain-cycle at 2165 (the stream's /Prev names the table's back, as written); rebuilt. N8 mutant (mutate.py N8, mut-N8-files2.txt): '  THROW ArgumentOutOfRangeException: Specified argument was out of the range of valid values.' for n8-s3-k515-table, n8-s4-k515-table, n8-s4-k516-table, n8-s6-k515/516/517-table; every other file identical to head. N8 survives CrossReferenceChainTests (1063/1063, mut-N8.txt) and the unit suite without the timing, budget, allocation and memory groups (suite-N8.txt: 10,971 total, 0 failed).

Fix: Add a row on the hybrid builder: header '++++5 0 obj' (or '+++5'), /XRefStm at its first sign, the stream's /Prev the table's /Prev, the table's /Prev 515 bytes past the stream's first byte; pin the sections (Read Table, /XRefStm NamedAgain, /Prev Relocated Stream at the first sign) and the reports. Re-class N8 as killed in the batch's ADR 53 record.

Refuter: verdict confirmed, severity minor, needs_maintainer False
Reason: The finding holds. R12b called N8 ('<' to '<=' in NumberRunsInto's inside-the-number arm) equivalent because 'a candidate equal to the stream's start maps to that start, a no-op'. That reasoning misses one candidate. The stream a hybrid table's /XRefStm read is put back unread when its /Prev names the table's /Prev (PdfFileReader.cs:5312-5318). It then comes back to NumberRunsInto as a retry whose candidate is streamStart, and that place can lie outside the window. Under '<=' the mutant calls NumberEnd(span, spanStart, streamStart), whose slice span[(int)(at - spanStart + 1)..] starts below zero once streamStart lies two or more bytes before the window's lead byte. The open then throws. The real code is safe: 'candidate < streamStart' is false for that retry, and every other candidate below streamStart comes from the window. The existing theory stops one byte short of the killing input. Its 'the table's /Prev' rows are (++13,0,514), (++13,0,513), (+++13,0,514) and (+++13,1,515), and in each the slice starts at exactly 0. So N8 survives the whole suite at dfb03fc. No earlier round or issue holds this: N8 appears only in R12b's record (r12-fix-result.json, decisions.md:1020). The severity is minor, the same as earlier wrongly classed equivalents (R7a's M09, R9c's M03): a gap in the tests and a wrong ADR 53 record, with no change in what the library does. A narrowing: the crash needs at least 3 signs, 515 <= k <= 512 + signs, and the stream's /Prev naming the table's /Prev. When the stream's /Prev names the oldest table, or the stream gives none, there is no retry at streamStart and the mutant reads the same as the code.

Refuter evidence: I wrote my own generator, W/r13wip/verify-n8-equivalence-refuted/gen.py, with W = C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud. It writes 144 files: signs 1 to 6, k = 512 to 519 (the table's /Prev names stream + k), and the stream's /Prev naming the table's /Prev (same), the oldest table, or nothing. My probe is ZzN8Verify.cs.txt in the same folder, run by run.py.

dfb03fc (out-head.txt): 144 files, no exception. For v-s3-k515-same:
  S startxref named=2776 Read Table
  S /XRefStm named=1507 NamedAgain
  S /Prev named=2022 Relocated Stream at=1507
  D Repair xref.offset-adjusted at 1507 '...found 515 bytes before it.'
  D Warning xref.hybrid-stream-lost at 1507
  D Warning xref.chain-cycle at 2022
  D Repair xref.rebuilt
Every object reads.

N8 mutant on dfb03fc (out-mut.txt): exactly 10 files differ, v-s3-k515, v-s4-k515/516, v-s5-k515/516/517 and v-s6-k515/516/517/518, all '-same'. That is the predicted set: signs >= 3, 515 <= k <= 512 + signs. Each gives:
  THROW ArgumentOutOfRangeException: Specified argument was out of the range of valid values.
    at PdfFileReader.NumberEnd (PdfFileReader.cs:5528)
    at PdfFileReader.NumberRunsInto (PdfFileReader.cs:5508)
    at PdfFileReader.TryRelocateXRefSection (PdfFileReader.cs:5273)
    at PdfFileReader.TryReadXRefChain
The other 134 files read the same as on dfb03fc.

The mutant survives the tests:
- CrossReferenceChainTests: 1063 of 1063 passed (tests-mut-chain.log).
- The unit suite without the timing, budget, allocation and memory groups, no remote corpus: 8282 total, 0 failed, 20 skipped (suite-mut.log).

The killing row: InlineData("+++13 0 obj", 0, 515, "the table's /Prev"), added to The_stream_a_hybrid_table_s_xrefstm_read_from_a_sign_and_whose_prev_names_an_offset_is_read_as_named_again_near_its_prev_however_few_of_its_signs_the_window_holds (CrossReferenceChainTests.cs:6669).
- On dfb03fc: 11 of 11 passed (tests-head-row.log).
- On the N8 mutant: 1 failed, with System.ArgumentOutOfRangeException at NumberEnd:5528 from NumberRunsInto:5508 (tests-mut-row.log).

The mutant worktree has been removed. It never had a remote corpus copy. My worktree is clean.

Refuter fix: 1. In tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:6669, add the row [InlineData("+++13 0 obj", 0, 515, "the table's /Prev")] to The_stream_a_hybrid_table_s_xrefstm_read_from_a_sign_and_whose_prev_names_an_offset_is_read_as_named_again_near_its_prev_however_few_of_its_signs_the_window_holds.
   - The theory's 'loop' expectations already hold for it on dfb03fc.
   - It kills N8.
   - The theory's comment ('513 to 515 bytes past') already covers it.
   - Optionally, also add ("++++13 0 obj", 0, 516, "the table's /Prev"): the stream's first sign three bytes before the lead byte.
2. Re-class N8 as killed in the batch's ADR 53 record:
   - R12b becomes 33 viable, 23 killed, 8 equivalent (U1, U2, U3, T1, D3, N9, X1, X2), 2 surviving (G2b, G5), 3 not viable.
   - Correct it in decisions.md's round 12 fix summary and in the docs/status.md batch 19 entry when that entry is written.
   - Drop the equivalence reason, including its 'killed once under load' note.

## 3. x2-x1-equivalence-stale-since-r12g — minor → confirmed minor (reached-test)
Location: src/AdCodicem.Pdf/IO/XRef/XRefSectionRecord.cs:367 (RelocateTo `DigitsAt = attempt.DigitsAt;`) and :335 (`= -1`); R12b's ADR 53 record ('EQUIVALENT (9) ... X1 ... X2'), to go into docs/status.md

Claim: R12b classed X1 (DigitsAt's -1 initializer removed) and X2 (RelocateTo copies 0) equivalent because DigitsAt was then read only of the stream an /XRefStm read or names again. Since R12g, RecordSectionReached adds every reached section's DigitsAt to _sectionDigitsAt, tables' -1 unconditionally (R12g's own deviation), so a 0 there makes the reached test pass over a place at the file's first byte. X2 puts 0 for every section found moved, table or stream: a headerless file whose classic table at byte 0 is looked for after a stream was found moved then reports the table missing, rebuilds and gives Error section-not-found. No test builds it: X2 survives the whole suite. X1 is now killed by two rows of the first-byte theory, so the record's '9 equivalent' is wrong by two.

Evidence: W/r13wip/reached-test/files2/x2-p50obj-prev5.pdf (gen2.py: no %PDF header, classic table at byte 0, objects, stream '+5 0 obj' whose /Prev is 5, newest table /Prev = stream - 3, so the stream is found moved). Head (out2-head.txt): S /Prev named=418 Relocated Stream at=422; S /Prev named=5 Relocated Table at=0; D Repair xref.offset-adjusted at 0 '...was found 5 bytes before it.'; repaired-at-open=False. X2 mutant (mut-X2-files2.txt): S /Prev named=5 NotFound Unknown; D Warning xref.section-missing at 5; D Repair xref.rebuilt; F Error xref.section-not-found at offset 5. Same with an unsigned stream (x2-50obj-prev3/5/9.pdf) and with '++5 0 %c' (x2-pp50cc_obj-*). X2 survives CrossReferenceChainTests (1063/1063) and the unit suite without the timing groups (suite-X2.txt: 10,971 total, 0 failed). X1 (mut-X1-chain.log): 2 failed, A_section_at_the_file_s_first_byte_is_a_place_like_any_other rows ('/Prev 0 /XRefStm 4', '9') and ('/Prev 999999 /XRefStm 4', '9').

Fix: Add an x2 row (headerless file, table at byte 0, a stream found moved whose /Prev names 5: the table relocated at 0, no rebuild), which kills X2; re-class X1 and X2 as killed in the ADR 53 record that goes into docs/status.md, and say there that R12g's change (every reached section's DigitsAt in the set) is what made them observable.

Refuter: verdict confirmed, severity minor, needs_maintainer False
Reason: The finding holds. R12b classed X1 and X2 equivalent because DigitsAt was then read only of the stream an /XRefStm read or names again. R12g changed that: RecordSectionReached now adds every reached section's DigitsAt to _sectionDigitsAt, and a table adds its -1 with no `>= 0` check (R12g's own deviation: 'no candidate is negative'). From then on the -1 initializer and RelocateTo's copy both matter. The reached test looks up DigitsPastSigns(place), which is the place itself when byte 0 is no sign. So a 0 in the set passes over any section at the file's first byte.
- X2 (`DigitsAt = 0;` in RelocateTo) puts 0 there for every section found moved, table or stream. A later search then passes over the right section at byte 0: section-missing, rebuild, Error section-not-found. No test builds that shape, so X2 survives the suite.
- X1 (initializer removed) is now killed by two rows of the first-byte theory.
So the ADR 53 record R12b wrote ('9 equivalent (U1 U2 U3 T1 D3 N8 N9 X1 X2)', r12-fix-result.json and decisions.md:1020), which is meant for docs/status.md, is wrong by two: X1 is killed, and X2 is a survivor with no test. The scale makes this minor: the code at the head is right, nothing is served wrong, and the issue is a test gap plus a wrong mutation record.
Scope: it only shows in a headerless file with a section at byte 0, since a header puts '%' there. The record is not in the repo yet, because docs/status.md has no batch 19 entry (accepted).

Refuter evidence: Rebuilt on origin/ccr-6d6c0ee6-dqm4lm-188 at dfb03fc; both builds had 0 warnings.
Generator: W/r13wip/verify-x2-x1-equivalence-stale-since-r12g/gen.py. It writes headerless files with the oldest section at byte 0 and the newest table's /Prev 3 bytes short of the middle section, plus in-place controls:
- t-moved: all three sections are classic tables, no stream.
- s-moved-50obj and s-moved-p50obj: the middle section is a stream, '5 0 obj' or '+5 0 obj'.
- z-stream0-moved: stream 9 at byte 0, a middle table found moved whose /Prev is 4.
Probe: ZzX2Verify.cs.txt in the same folder, deleted from the worktree afterwards. Outputs: out-head.txt, out-X2.txt, out-X1.txt.

Head, t-moved.pdf:
  S /Prev named=293 Relocated Table at=296
  S /Prev named=5 Relocated Table at=0
  repaired-at-open=False
  D Repair xref.offset-adjusted at 0: ...found 5 bytes before it.
  O 3="only in the oldest"
  F Warning xref.section-shifted at offset 0

X2, t-moved.pdf:
  S /Prev named=293 Relocated Table at=296 digits=0
  S /Prev named=5 NotFound Unknown at=5
  repaired-at-open=True
  D Warning xref.section-missing at 5
  D Repair xref.rebuilt
  F Error xref.section-not-found at offset 5

The other moved files fail the same way under X2:
- s-moved-50obj and s-moved-p50obj: NotFound at 5, rebuild, Error section-not-found at 5. Object 5, the xref stream, also changes from null to PdfStream through the rebuild.
- z-stream0-moved: '/Prev named=4 NotFound', section-missing at 4, rebuild, Error section-not-found at offset 4. Head relocates that stream to 0.
- The in-place controls differ only in the digits field.

X2 against the unit suite, without the timing, budget, allocation and memory groups, in a mutant worktree with no remote corpus: 8,282 total, 0 failed, 20 skipped (suite-X2.log). The reviewer's run with the remote corpus also had 0 failures out of 10,971.

X1 against CrossReferenceChainTests (chain-X1.log): 1,063 total, 2 failed, both rows of A_section_at_the_file_s_first_byte_is_a_place_like_any_other: ('/Prev 0 /XRefStm 4', '9') and ('/Prev 999999 /XRefStm 4', '9'). Under X1 all 8 of my files become NotFound plus rebuild, the controls included (out-X1.txt).

Mutants were applied by hand: X1 in my worktree, X2 in W/r13wip/vx2x1-mut. Both were reverted or removed afterwards, and the worktree is clean.

Refuter fix: 1. Add a test that kills X2. The simplest is one more row, or a sibling theory, next to A_section_at_the_file_s_first_byte_is_a_place_like_any_other, using that theory's headerless file:
- stream 9 at byte 0;
- a middle table whose /Prev is 4;
- the newest table's /Prev naming the middle table 3 bytes short, so the middle table is found moved.
Expected result: '/Prev Relocated <middle>, /Prev Relocated 0', no rebuild, object 2 served. That row fails under X2 (NotFound 4 plus rebuild). The all-tables t-moved shape from gen.py works as well.
2. In the ADR 53 record that goes into docs/status.md, class X1 as killed (the two first-byte rows) and X2 as killed by the new row. That makes R12b's count 24 killed and 7 equivalent.
3. Say in that record that R12g made both observable: every reached section's DigitsAt now goes into _sectionDigitsAt, and a table's -1 goes in with no `>= 0` check.
No maintainer decision is needed.

## 4. reachedat-summary-stale-window-qualifier — minor → confirmed minor (reached-test)
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3899-3901 (ReachedAt summary, from 0578bcb, untouched by a7bcdc6 and dfb03fc)

Claim: ReachedAt's summary still describes R12a's reached test: 'a relocation ... passes over those where a section the chain reached starts, and a header found at its digit where one starts at a sign of the run its number is written with, that run lying within the bytes searched (#188, #327)'. Since R12g the reached test passes such a header over wherever the run starts, before the bytes searched too, and whatever sign starts it (a stream reached at the '+' of '-+5'), and also where the chain reached it at the digit; since R13a it passes over a header found at its first sign (a % on its number's line) where a stream the chain reached at a later sign or the digit has its digits past those signs. The 'within the bytes searched' qualifier is the line test's, not the reached test's.

Evidence: W/r13wip/reached-test/files1/prevfar.pp.i0.e1.ok.pdf ('++5 0 obj' at 1650 reached by the newest /Prev at its first sign; its own /Prev 2163 = 1650+513, so the bytes searched start at its second sign and the run's first sign lies before them). Head (out1-head.txt): S /Prev named=1650 Read Stream at=1650; S /Prev named=2163 NotFound; D Warning xref.section-missing at 2163; F Error xref.section-not-found at offset 2163 (the header at 1652 passed over). dadde18 (out1-dad.txt): S /Prev named=2163 Relocated Stream at=1652; D Repair xref.offset-adjusted at 1652; D Warning xref.chain-cycle at 2163; F Error xref.chain-loop at offset 1652. The summary's qualifier describes dadde18's behavior.

Fix: Rewrite the clause as the reached test now reads: '... and a header found at its digit, or at the first sign of its run where the line its number is on holds a %, where the digits of a stream the chain reached, from a + of that run or from that digit, start past the place's own signs, wherever the run starts and whatever sign starts it (DigitsPastSigns, #188, #327)'.

Refuter: verdict confirmed, severity minor, needs_maintainer False
Reason: The finding holds. ReachedAt's summary is out of date, and nothing in the code is wrong. At dfb03fc, git blame puts lines 3900-3901 in 0578bcb (R12a's docs). Neither a7bcdc6 (R12g's docs) nor dfb03fc (R13a's docs) changed them, and the changed-text lists in r12-fix2-result.json and r13a-result.json do not name ReachedAt. The clause 'a header found at its digit where one starts at a sign of the run its number is written with, that run lying within the bytes searched' describes dadde18's test: StartsWithin over [HeaderTokenStart, candidate+1), where HeaderTokenStart requires the run to start with '+' and to lie within the bytes searched. The head's test (PdfFileReader.cs:5301-5304) looks _sectionDigitsAt up at DigitsPastSigns(candidate). That test does not depend on the window and does not care which sign starts the run. So a header found at its digit is now passed over even where its run's first sign lies before the bytes searched; the reproducer below shows this. The summary also leaves out R13a's case: a header found at its first sign (a % on its number's line) where the chain reached a later sign or the digit. The other texts on this test are current and match the code: the _sectionDigitsAt summary (:347-355), the comment above the reached test (:5285-5300) and diagnostics.md row 110 ('wherever that run starts and whatever sign starts it'). The other matches for 'within the bytes searched' belong to the line test (HeaderTokenStart :5560, lazy-reading.md:269, threat-model.md:524) and are correct there. Neither decisions.md nor the brief's accepted list mentions this text. Severity is minor because a misleading text is minor on the brief's scale; behavior is as R12-Q3 and R13-Q0 decided. For triage: round 13 already holds sibling folders, r13wip/verify-reachedat-summary-keeps-r12a-window-qualifier and r13wip/verify-reachedat-summary-states-removed-range. Those findings may cover the same text and should be merged with this one.

Refuter evidence: I wrote my own generator, W/r13wip/verify-reachedat-summary-stale-window-qualifier/gen.py, with files under files/. A classic table at startxref has /Prev naming a stream whose header writes its number with a run of signs at 1052. The stream's own /Prev names a comment line past it, and the search window there starts at that offset less 512 (NearbySearchRadius). The probe is ZzVerifyReachedSummary.cs.txt in the same folder. I built it 0 warnings on the head (my worktree, dfb03fc) and on dadde18 (a scratch worktree at W/r13wip/vrsw, since removed). Outputs are out-head.txt and out-dad.txt.

before.pdf: '++5 0 obj' at 1052, read at its first sign; its /Prev 1565 puts the window's start at 1053, so the run's first sign lies before the bytes searched.
- Head: 'S /Prev named=1052 Read Stream at=1052; S /Prev named=1565 NotFound Unknown at=1565; D Warning xref.section-missing at 1565; F Error xref.section-not-found at offset 1565'. The header at digit 1054 is passed over.
- dadde18: 'S /Prev named=1565 Relocated Stream at=1054; D Repair xref.offset-adjusted at 1054 ... 511 bytes before it; D Warning xref.chain-cycle at 1565; F Error xref.chain-loop at offset 1054'.

within.pdf (control, /Prev 1564, the run within the bytes searched): both heads give NotFound, section-missing at 1564 and section-not-found.

minusplus.pdf: '-+5 0 obj' read at its '+' (1053), /Prev 1564.
- Head: NotFound and section-not-found at 1564.
- dadde18: 'Relocated Stream at=1054 ... 510 bytes before it; chain-cycle; chain-loop at offset 1054'.

The summary's qualifier 'that run lying within the bytes searched' therefore describes dadde18's behavior, not the head's. The reviewer's own file (r13wip/reached-test/files1/prevfar.pp.i0.e1.ok.pdf, out1-head.txt and out1-dad.txt) shows the same split.

Refuter fix: This is documentation only, with no design choice for the maintainer. In ReachedAt's summary (src/AdCodicem.Pdf/IO/PdfFileReader.cs:3899-3901), replace 'and a relocation, which tries positions, passes over those where a section the chain reached starts, and a header found at its digit where one starts at a sign of the run its number is written with, that run lying within the bytes searched (#188, #327).' with: 'and a relocation, which tries positions, passes over those where a section the chain reached starts, and a header whose digits, past the place's own run of signs, start where those of a stream the chain reached from a + of that run, or from the digit past it, do: found at its digit, wherever the run starts and whatever sign starts it, or at the run's first sign where the line its number is on holds a % (DigitsPastSigns, _sectionDigitsAt, #188, #327).' This keeps it in line with the _sectionDigitsAt summary and diagnostics.md row 110. If a sibling round 13 finding already covers this text, fold the two into one fix.

## 5. signed-header-at-prev-escapes-keeps-its-place — major → refuted none (guard-flag:)
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:5324 (TryRelocateXRefSection, `if (candidate == leaveAt)`)

Claim: The search near an /XRefStm's offset stops at the /Prev's place only where a candidate equals leaveAt, the first token of what the /Prev names. A header whose number is written with a run of signs that starts with '+' ('+7 0 obj', '++7 0 obj') starts that token at its first sign, but the walk back from obj finds it at its digit, one or two bytes later. So KeepsItsPlace is never asked, and the #156 rule (an /XRefStm that names, a few bytes off, what its table's /Prev names is left to that /Prev and reported) does not apply. Instead the /XRefStm takes the /Prev's stream as the table's own stream (Relocated at the digit), and the /Prev then reads the same stream again from its sign. Warning xref.hybrid-stream-lost and the validator's Error xref.hybrid-stream-wrong are missing, and offset-adjusted and section-shifted are made instead. The index is not left incomplete, so an object that only the table's own (lost) stream would index reads as null and is never looked for by a rebuild. The twin without the sign, and '+7 0 %c\nobj' (found at its sign), report it and find the object. Under a lowered MaxTrailerLength the plus twin's /XRefStm takes the cut stream at its digit as the guard's section, where the plain twin leaves it to the /Prev. This predates the round: gf0052 (check family, '+7 0 obj') reads identically at 1cd2216 and dadde18. main has no KeepsItsPlace and reads both twins as the plus twin does, so this is the batch's #156 rule not implemented for signed headers (#327's class, but in none of #327's listed gaps), not a regression against main.

Evidence: Files: W/r13wip/guard-flag/twins2 and twins3, written by twin2.py and twin3.py. Two revisions: stream 7 at 225 holds the catalog, pages, page and object 5. The newest table (startxref) indexes object 9, with /Prev 225 and /XRefStm 221 (the 'obj' of the endobj before stream 7). twins3 adds object 8 '(eight)', which no section indexes. Outputs: out-twins2-head.txt, out-twins2-main.txt, out-twins3-head.txt, and out-twins-head.txt for the 3-revision 'check' family.
dfb03fc, twins3 plain.pdf ('7 0 obj'), default limits:
  S /XRefStm 221 221 Unknown NamedAgain | S /Prev 225 225 Stream Read
  D Warning xref.hybrid-stream-lost 221 The /XRefStm of the cross-reference table at offset 397 names offset 221, what its /Prev names too, which the chain looks ...
  O 8 "eight" "eight"; R asc True; A asc Repair xref.rebuilt
dfb03fc, twins3 plus.pdf ('+7 0 obj'):
  S /XRefStm 221 226 Stream Relocated | S /Prev 225 225 Stream Read
  D Repair xref.offset-adjusted 226 The cross-reference section /XRefStm names at offset 221 was found 5 bytes after it.
  O 8 null null; R asc False
plusplus.pdf: Relocated at 227, offset-adjusted 227, O 8 null. plus_cmt.pdf ('+7 0 %c\nobj'): reads as plain.pdf.
Validator (twins2): plain gives Error xref.hybrid-stream-wrong at offset 374; plus gives Warning xref.section-shifted at offset 226 and Warning file.size-wrong at offset 225.
3-revision check family (twins/check-*-noown-XRef.pdf), MaxTrailerLength 160: plain gives '/XRefStm 241 NamedAgain', hybrid-stream-lost@241, limit.trailer@245, /Prev 245 Malformed cut. plus gives '/XRefStm 241 246 Stream Malformed True', limit.trailer@246, offset-adjusted@246, and no hybrid-stream-lost.
main 21a0298 (out-twins2-main.txt): plain gives '/XRefStm 221 225 Stream Relocated', '/Prev 225 Read', offset-adjusted@225; plus gives the same at 226.

Fix: In TryRelocateXRefSection, tell the /Prev's place by the digits past its signs as well as by its first byte, for example `candidate == leaveAt || (leaveAt >= start - lead && leaveAt < end && span[(int)(leaveAt - (start - lead))] == (byte)'+' && DigitsPastSigns(span, start - lead, leaveAt) == candidate)`, then ask KeepsItsPlace(leaveAt, leaveIsTable). Pin rows for '7 0 obj', '+7 0 obj', '++7 0 obj' and '+7 0 %c\nobj' at a hybrid table's /Prev with the /XRefStm 4 bytes short, at default limits and MaxTrailerLength 160. Each signed row should read as its plain twin: sections, hybrid-stream-lost at the /XRefStm, the validator's Error, and an unindexed object found by the rebuild. Alternatively, file it under #327 and name it in #327's Known gaps entry and the pages.

Refuter: verdict refuted, severity none, needs_maintainer False
Reason: The behavior reproduces exactly at dfb03fc, but #332 already holds it, and the brief lists that as accepted. The batch makes nothing worse than main, and no project text misstates it.

1. What happens. TryRelocateXRefSection asks KeepsItsPlace only where `candidate == leaveAt` (PdfFileReader.cs:5324). leaveAt is the /Prev's first token, which for '+7 0 obj' is the sign. The walk back from obj finds the header at its digit. So the /XRefStm's search takes the stream as the table's own stream, and the /Prev then reads it again from the sign. The unsigned twin is left to the /Prev and reported.

2. Already held by #332. Round 12 found this split: r12-findings.md, finding 16's note on seed949_067, where the /XRefStm finds '+-13' at its digit and the /Prev names its sign. Round 12 classed it as #332's by-byte class, and the tracking comment #332 6769965753 holds it as pair.pdf:
   - pair.pdf is '+-13 0 obj', with the /XRefStm inside its dictionary 30 bytes on and the /Prev at its first sign.
   - The comment says "a header the /XRefStm's search finds at its digit and a /Prev that names its first sign are two sections".
   - Under "What the decision should cover", it has pair.pdf reading as pair-plain.pdf (hybrid-stream-lost and the Error).

   The brief accepts that comment, and accepts "the hybridStart == previousStart test" as #332's. My foo-p30-f-plusminus.pdf rebuilds pair.pdf and gives the same result: two section-unreadable, two Error section-malformed, no hybrid-stream-lost. The code path is the one the finding names.

3. The readable twin adds nothing worse. The finding's own shape ('+7 0 obj', a readable stream) has the consequence of "two sections" that #332's first comment (6762396088) already gives for its readable by-byte shapes: the stream read twice, no hybrid-stream-lost, no Error, the index neither rebuilt nor left incomplete. Here an offset-adjusted repair and a section-shifted Warning come with it. Under MaxTrailerLength 160, the guard cuts the stream at its digit instead of at the sign. The mechanism and the missing report are the same.

4. main. main 21a0298 reads every twin, signed or not, as the signed twin reads on the branch: the /XRefStm Relocated to the stream, the /Prev reading it again, object 8 null, nothing rebuilt.

5. The texts. The threat model's #156 defense says the stop is made where the search "comes to where that /Prev keeps its place". By the byte notion #332 keeps open, that place is the sign, not the digit. The #327 Known gaps entry lists only the line test and the passed-over record as asked at the sign, and the reached look as asked past the signs. It does not claim the stop is asked at the sign. So no text misstates it.

6. A tracking point, not a defect. Comment 6769965753 credits this split to hybridStart == previousStart (:3524). That test does not decide pair.pdf or this shape: `candidate == leaveAt` (:5324) does. Whoever settles #332 "by its data" must cover both comparisons.

Refuter evidence: Paths: W = C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud. My own generator is W/r13wip/verify-signed-header-at-prev-escapes-keeps-its-place/gen.py (files/) and gen2.py (files2/, adding /FooDecode, '+-7' and a 200-byte /Pad for the guard). The probe is ZzVkpProbeTests.cs.txt, run under default limits and MaxTrailerLength 160. Outputs are out-head.txt and out2-head.txt (dfb03fc) and out-main.txt (21a0298, my own worktree, built and removed). The layout: stream 7 at 214 indexes 1-3 and 5; object 8 is in no section; the newest table indexes 9, with /Prev 214 and /XRefStm 214+delta.

dfb03fc, delta -4 (out-head.txt):
- m4-a-plain ('7 0 obj'):
  - sections: /XRefStm 210 Unknown NamedAgain | /Prev 214 Stream Read
  - Warning xref.hybrid-stream-lost @210
  - O 8 "eight, in no section", repaired after reads
  - F Error xref.hybrid-stream-wrong offset 413
- m4-b-plus ('+7 0 obj'):
  - sections: /XRefStm named=210 at=215 Stream Relocated | /Prev 214 Stream Read
  - Repair xref.offset-adjusted @215 ("found 5 bytes after it")
  - O 8 null, not repaired
  - F Warning file.size-wrong offset 214, F Warning xref.section-shifted offset 215
- m4-c-plusplus: the same at 216.
- m4-d-plus-pct ('+7 0 %x\nobj') and m4-e-plain-pct read as plain.
- The same split with delta -9 and +30.

t160, long-m4 (out2-head.txt):
- plain: /Prev 214 Malformed cut, hybrid-stream-lost @210, limit.trailer @214, F Error hybrid-stream-wrong
- plus: /XRefStm at=215 Stream Malformed cut=True, limit.trailer @215, offset-adjusted @215, no hybrid-stream-lost, O 8 null

pair.pdf's class on dfb03fc (foo-p30-f-plusminus, '+-7 0 obj', unreadable, /XRefStm 30 bytes past, /Prev at the sign):
- /XRefStm at=216 Stream Malformed | /Prev 214 Stream Malformed
- section-unreadable @216 and @214
- F Error section-malformed at 216 and 214, Warning section-shifted @216
- foo-p30-a-plain gives hybrid-stream-lost @244 and Error hybrid-stream-wrong. This matches #332 6769965753's pair.pdf and pair-plain.pdf.

main 21a0298 (out-main.txt):
- m4-a-plain: /XRefStm named=210 at=214 Stream Relocated, /Prev 214 Read, offset-adjusted @214, O 8 null, section-shifted.
- m4-b-plus and m4-c-plusplus: the same at 215 and 216.
- Every plain twin reads as the signed twin does on the branch.

Related shape, also #332's: q1-b-plus has /XRefStm 215 (the digit) and /Prev 214 (the sign). On both trees: /XRefStm 215 Stream Read, /Prev 214 Stream Read, no diagnostic.

Records: r12-findings.md:831 ("The same split also defeats the hybrid test hybridStart == previousStart, the #332 by-byte class. Example: seed949_067, where the /XRefStm finds '+-13' moved at its digit 2194 and the /Prev names its sign 2192") and :836/:890. #332 comment 6769965753 (pair.pdf) and 6762396088 (both-0-1, readable, read twice silently). threat-model.md:447 (#156 defense) and :519-533 (#327 gap).

Refuter fix: No code in this batch: the shape is #332's, settled with round 12's note and comment.

Optional tracking note on #332 (not required to close the round):
- What decides pair.pdf and this readable twin is TryRelocateXRefSection's `candidate == leaveAt` (PdfFileReader.cs:5324 at dfb03fc), not only `hybridStart == previousStart` (:3524).
- A "by its data" decision therefore has to tell the /Prev's place by the digits past its signs too. One way: `candidate == leaveAt || (leaveAt is within the window && the byte at leaveAt is '+' && DigitsPastSigns(span, start - lead, leaveAt) == candidate)`, then ask KeepsItsPlace(leaveAt, leaveIsTable).
- Rows for that fix, built by gen.py and gen2.py in W/r13wip/verify-signed-header-at-prev-escapes-keeps-its-place:
  - headers '7 0 obj', '+7 0 obj', '++7 0 obj', '+-7 0 obj' and '+7 0 %x\nobj';
  - the /XRefStm 4 bytes short, 9 bytes short and 30 bytes past;
  - readable, /FooDecode, and a 200-byte /Pad under MaxTrailerLength 160.
  - Each signed row should read as its plain twin.

## 6. x2-equivalence-refuted-since-r12g — minor → confirmed minor (guard-flag:)
Location: src/AdCodicem.Pdf/IO/XRef/XRefSectionRecord.cs:367 (RelocateTo `DigitsAt = attempt.DigitsAt;`), read through PdfFileReader.cs:4136 (RecordSectionReached)

Claim: R12b classed X2 (RelocateTo copies 0) equivalent because DigitsAt was then read only of the stream an /XRefStm read or names again. Since 13e5021 (R12g), RecordSectionReached adds every reached section's DigitsAt to _sectionDigitsAt, so DigitsAt now matters for relocated sections as well. Under X2 a section found moved records 0, and a later search near an offset passes over a place at the file's first byte as reached. The mutant survives the whole unit suite at dfb03fc; an input tells it apart. X1 (the `= -1` initializer removed) is likewise no longer equivalent: tables now record their DigitsAt, so two rows of A_section_at_the_file_s_first_byte_is_a_place_like_any_other kill it.

Evidence: Mutant X2 in a worktree at dfb03fc: the suite gives total 11044, failed 0, succeeded 11041, skipped 3 (W/r13wip/guard-flag/mut-X2-suite.log). The four xref classes alone give total 2009, failed 0 (mut-X2-xref.log).
The input that tells it apart is ZzGfProbe.cs.txt, case 'headerless moved table then first byte'. Stream 9 starts at byte 0 of a file with no header. The newest table's /Prev falls 3 bytes short of table A, which is found moved. Table A's /Prev names 4.
Head (probe-head.txt): 'S /Prev named=238 at=241 Table Relocated', 'S /Prev named=4 at=0 Stream Relocated', 'D Repair xref.offset-adjusted @0: The cross-reference section /Prev names at offset 4 was found 4 bytes before it.', 'G stopped=False repaired=False'.
X2 (probe-X2.txt): 'S /Prev named=4 at=4 Unknown NotFound', 'D Warning xref.section-missing @4: The cross-reference section /Prev names at offset 4 is not there, nor near it; ...', 'D Repair xref.rebuilt @-1', 'G stopped=False repaired=True'.
X1 at dfb03fc fails 2 of 7 selected rows: ("/Prev 0 /XRefStm 4", ..., "/XRefStm NamedAgain 4, /Prev Read 0") and ("/Prev 999999 /XRefStm 4", ..., "/XRefStm Relocated 0, /Prev NotFound 999999").

Fix: Add the headerless row to A_section_at_the_file_s_first_byte_is_a_place_like_any_other (or a sibling Fact): a section found moved first, then a /Prev naming a place inside the stream at offset 0, pinning '/Prev Relocated 0' and offset-adjusted at 0. Record X2 as killed by that row and X1 as killed in the batch's ADR 53 record, not as equivalents.

Refuter: verdict confirmed, severity minor, needs_maintainer False
Reason: The finding holds. R12b's ADR 53 record still classes X1 and X2 as equivalent: r12-fix-result.json says so, and decisions.md's R12b line lists "9 equivalent (U1 U2 U3 T1 D3 N8 N9 X1 X2)". Its reasons were that DigitsAt was read only from the stream an /XRefStm read or names again, and that a relocated stream's digits start where its reading did.

13e5021 (R12g) changed that. RecordSectionReached now adds every reached section's DigitsAt to _sectionDigitsAt, tables found moved included. The reached test looks up DigitsPastSigns(candidate) in that set. R12g's ADR 53 run covered only the lines R12g changed (21 mutants, C1 and C2 equivalent). R12h's and R13a's runs did not cover XRefSectionRecord.cs either. Nothing re-settled X1 or X2 on the final code.

- **X2** (`DigitsAt = 0;` in RelocateTo) puts 0 into the set for any section found moved. A later search near an offset then passes over a header whose digits start at file position 0. I rebuilt that input, and X2 still survives the whole suite.
- **X1** (the `= -1` initializer removed) puts 0 there for every table. Two existing rows of A_section_at_the_file_s_first_byte_is_a_place_like_any_other now kill it, so it is no longer equivalent.

Narrowing: X2 can be told apart only where a header's digits start at file position 0, which takes a file with no header, and only after a section was found moved. For relocated streams themselves, R12b's reasoning still holds. A stream found at its digit has its digits at its start. One found at its first sign, with a % on its number's line, is found again at that sign, which _sectionsReadAt already holds.

Head behaves correctly. Nothing is served wrong and no report is wrong at dfb03fc, so no invariant breaks. What is wrong is the batch's ADR 53 record, and one survivor that no test kills. On the brief's scale that is minor: a test gap and a misleading record.

This is not known from earlier rounds. No round's findings and no decision mention X1, X2 or RelocateTo's copy of DigitsAt.

Refuter evidence: Probe: W/r13wip/verify-x2-equivalence-refuted-since-r12g/ZzX2Probe.cs.txt, run in my worktree at dfb03fc (built Release). It has three cases, all in a headerless file whose stream 9 starts at byte 0:
- mode 0: the newest table's /Prev names table A 3 bytes short, so A is found moved; table A's /Prev names offset 4.
- mode 1 (control): the newest table's /Prev names 4 directly.
- mode 2 (control): table A is read in place.

**Head** (probe-head.txt), mode 0:
```
S /Prev named=238 at=241 Table Relocated
S /Prev named=4 at=0 Stream Relocated
D Repair xref.offset-adjusted @0: The cross-reference section /Prev names at offset 4 was found 4 bytes before it.
G stopped=False repaired=False
```

**X2 applied** (probe-X2.txt), mode 0:
```
S /Prev named=4 at=4 Unknown NotFound
D Warning xref.section-missing @4: The cross-reference section /Prev names at offset 4 is not there, nor near it; ...
D Repair xref.rebuilt @-1
G stopped=False repaired=True
```
Modes 1 and 2 are identical on head and under X2: '/Prev named=4 at=0 Stream Relocated', offset-adjusted @0, repaired=False.

**X2, whole unit suite** (with the remote corpus hard-linked, GC heap hard limit 3 GB; mut-X2-suite.log): total 11045 (the suite plus my probe), failed 1, succeeded 11041, skipped 3. The one failure is ReaderTimingTests.An_index_of_numbers_chosen_to_share_a_bucket_validates_as_fast_as_one_numbered_in_order (10.7 ms against a 10.4 ms bound) under the suite's own parallel load. Rerun alone under X2, it passes. So X2 survives.

**X1 applied at dfb03fc** (mut-X1-firstbyte.log): A_section_at_the_file_s_first_byte_is_a_place_like_any_other gives total 6, failed 2. The failing rows are ("/Prev 999999 /XRefStm 4", ..., "/XRefStm Relocated 0, /Prev NotFound 999999") and ("/Prev 0 /XRefStm 4", ..., "/XRefStm NamedAgain 4, /Prev Read 0").

**Records:**
- decisions.md, R12b line: "ADR 53: 33 viable, 22 killed, 9 equivalent (U1 U2 U3 T1 D3 N8 N9 X1 X2 ...)".
- r12-fix2-result.json, R12g: "RelocateTo copies DigitsAt and TryReadXRefStream sets it" — the design now depends on that copy — and "ADR 53: 21 mutants, 19 killed, 2 equivalent (C1, C2)". Line 367 is not among them.
- R13a: "None" for XRefSectionRecord.

Code: XRefSectionRecord.cs:335 and :367, and PdfFileReader.cs:4136 (`_sectionDigitsAt.Add(section.DigitsAt)`) and :5302-5304.

My worktree was restored clean: the probe is deleted and the remote corpus hard links are removed.

Refuter fix: 1. Add a Fact beside A_section_at_the_file_s_first_byte_is_a_place_like_any_other (or a row, if the theory's builder can take a moved table). Use the probe's mode 0 file: headerless, stream 9 at byte 0, the newest table's /Prev 3 bytes short of table A, and table A's /Prev 4.
   - Pin the sections '/Prev Relocated 241, /Prev Relocated 0' (Skip(1)).
   - Pin the diagnostics: header.missing, then offset-adjusted at 241, then offset-adjusted at 0. No section-missing, no rebuild.
   - This kills X2: under X2 the second section is '/Prev NotFound 4', followed by section-missing and xref.rebuilt.
2. Correct the batch's ADR 53 record (decisions.md now, docs/status.md's batch 19 entry when it is written):
   - X1 is killed by the two first-byte rows named above since R12g.
   - X2 is killed by the new test.
   - R12b's tally reads 24 killed, 7 equivalent.
3. In the batch's last ADR 53 pass, rerun every mutant R12b classed equivalent on code R12g or R13a changed the meaning of — DigitsAt now reaches _sectionDigitsAt — rather than only the lines each stage edited.

This needs no design choice from the maintainer.

## 7. table-an-xrefstm-names-keeps-warning-under-a-trailer-cut — minor → narrowed minor (guard-flag:)
Location: docs/website/docs/concepts/reader-limits.md:99; also docs/website/docs/concepts/lazy-reading.md:230, docs/website/docs/reference/diagnostics.md:116 and :142, src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:131, src/AdCodicem.Pdf/IO/PdfFileReader.cs:686 and :6683, and the ReportStreamsPassedOver remark (:3785-3788)

Claim: The texts say that wherever MaxTrailerLength cuts the reading at an offset the chain names, whatever it cuts, no stream passed over is reported anywhere in the document, and that where the reading takes what is there for the guard's section the chain's own cut withholds the reports already. A classic table an /XRefStm names whose trailer runs past MaxTrailerLength breaks both statements. It is recorded as cut (CutByLimit and TrailerCutByLimit true), the chain goes on (ChainStoppedByGuard false), no flag is set, and xref.stream-passed-over is made. The behavior is right: that trailer's links are never followed (PdfFileReader.cs:6001-6004), so the cut hides nothing the chain would follow and ends nothing, as R12h's measurement notes. But no text states the exception, and the general sentences, read literally, deny it.

Evidence: ZzGfProbe.cs.txt, TableAtXRefStm(pad: 300), the batch's /XRefStm-offset layout with the dashes broken so that only the table's trailer is cut. MaxXRefSectionLength 250 makes the table's window end inside the trailer; MaxTrailerLength 100 (W/r13wip/guard-flag/probe-head.txt, block 'table at xrefstm pad=300 section=250 trailer=100'):
  S /XRefStm named=1638 at=1638 Table Read cut=True trailerCut=True
  S /Prev named=960 at=960 Table Read cut=False trailerCut=False
  D Warning limit.trailer @1699: A trailer runs past 100 bytes; only what lies within it was read. Raise PdfReaderLimits.MaxTrailerLength to read past it.
  D Warning xref.hybrid-stream-lost @1638: ...
  D Warning xref.stream-passed-over @362: The cross-reference stream at offset 362, near offset 562, ...
  G stopped=False repaired=False
The same file at default limits makes the same Warning.

Fix: Scope those sentences to what the chain reads at the offset as a section that names links: a stream's dictionary or object header, a token where no header starts, or white space and comments. Add, in the pages and in the remark, that a classic table an /XRefStm names keeps the report when the guard cuts its trailer, because the chain follows none of that trailer's links and goes on past it. No code change is needed.

Refuter: verdict narrowed, severity minor, needs_maintainer False
Reason: The behavior reproduces at dfb03fc and is right. A classic table that an /XRefStm names gets its trailer cut by MaxTrailerLength (CutByLimit and TrailerCutByLimit both true). No flag is set, ChainStoppedByGuard stays false, the chain goes on through the naming table's /Prev, and xref.stream-passed-over is made. The code says it means this. PdfFileReader.cs:6001-6003 does not read that trailer's links "to name a section missing or end the chain for a guard, the chain going on through the table that names it" (#263). In the /XRefStm branch, `_cutByGuard != cut` (:3714) does not fire on a trailer cut. R12h's report (r12-fix2-result.json, item 5) settled it: "On the /XRefStm path ... the cut hides no link the chain follows and ends nothing; no flag, unchanged". In my probe the cut actually hides that table's `/Prev 362`, which names the stream passed over. The chain would not follow it read whole either, and at default limits the same Warning is made.

The finding aims at the wrong sentences, so I narrow it.

(1) The 'whatever it cuts' sentences carry a qualifier that excludes this case. The table here is a section the guard cut (cut=True):
- lazy-reading.md:231: "which the reading does not take for a section the guard cut"
- diagnostics.md:116 and :142: "that the reading does not take for a section the guard cut"
- reference/reader-limits.md:21: "that the reading does not take for a section it cut"
- concepts/reader-limits.md:100-101: "a dictionary, whatever its /Type, that the reading does not take for a section it cut"
- PdfDiagnosticCodes.cs:131-133 lists only an object read as another type, or no object read.

These sentences are also about what is read at the offset. The page itself says a classic table's window at the offset is MaxXRefSectionLength's (reader-limits.md:92-93). The trailer is a later read, which the reader reports @1699, not at 1638.

(2) 'the chain's own cut withholds them already' appears in no page. It appears only in private code docs, each limited to the object reading of TryReadXRefStream:
- the field doc (:686), with `cref TryReadXRefStream`; it also defines the flag by a hidden link or a cut that would end the chain, and this cut is neither;
- the comment inside TryReadXRefStream (:6683);
- the remark (:3787), after a list of dictionary, header and token.

What does misstate this case, all of it new in the batch except one:
(a) Several texts list 'a trailer' among the cuts that withhold the report, with no exception for a table an /XRefStm names:
- PdfDiagnosticCodes.cs:127-129: "Nothing is reported where one of the reader's limits left part of the chain unread — ..., rows, a trailer, ..."
- lazy-reading.md:226-228, the same list
- diagnostics.md:116, the same list
- the ReportStreamsPassedOver remark, PdfFileReader.cs:3770-3771: "a trailer it cut"

(b) Two texts say the chain ends at a trailer cut, and here it goes on:
- reference/reader-limits.md:21: "The trailer as far as the bound, the chain ending there". d8e67de added this; main read "The trailer as far as the bound".
- diagnostics.md:142 limit.trailer: "and the chain ends there". This one is main's wording.

Read with the 'chain ends' rule, these imply the report is withheld, and it is not. No issue or earlier round holds this. Round 1 [30] settled only the read-in-part message of a table an /XRefStm names. It is a misleading text in a narrow, guard-only shape, so it is minor, with no behavior change.

Refuter evidence: The probe is my own rebuild, under W/r13wip/verify-table-an-xrefstm-names-keeps-warning-under-a-trailer-cut/:
- ZzVtxProbe.cs.txt: the guard-flag lens's TableAtXRefStm layout, plus a control. The control swaps the older hybrid table's /Prev and /XRefStm, so that the padded table is reached through /Prev.
- the files: stm-pad300.pdf and prev-pad300.pdf (with pad0 twins)
- probe-head.txt: the output, from a Release build of dfb03fc with 0 Warning(s), log build-head.log

Results at dfb03fc:

'xrefstm names padded pad=300 section=250 trailer=100' (section=180 gives the same):
  S /Prev named=562 at=799 Table Relocated cut=False trailerCut=False
  S /XRefStm named=1638 at=1638 Table Read cut=True trailerCut=True
  S /Prev named=960 at=960 Table Read cut=False trailerCut=False
  D Warning limit.trailer @1699: A trailer runs past 100 bytes; only what lies within it was read. ...
  D Warning xref.hybrid-stream-lost @1638: ...
  D Warning xref.stream-passed-over @362: The cross-reference stream at offset 362, near offset 562, which the /Prev of the cross-reference table at offset 2024 names, ...
  G stopped=False repaired=False

The same file at default limits, and at section=400 where the trailer fits the table's window, makes the same Warning with no cut. The trailer cut out of that table holds `/Prev 362`, which names the stream passed over.

Control 'prev names padded pad=300 section=250 trailer=100':
  S /Prev named=1638 at=1638 Table Read cut=True trailerCut=True
  D Warning limit.trailer @1699: ...
  G stopped=True
No xref.stream-passed-over: the chain's own cut withholds it.

Code:
- PdfFileReader.cs:6001-6004: `trailer is not null && section.NamedBy != "/XRefStm"`, with the comment "...to name a section missing or end the chain for a guard, the chain going on through the table that names it".
- The /XRefStm branch ends the chain only on `_indexFullAt >= 0 || _cutByGuard != cut` (:3714).
- On the /Prev and startxref path, a trailer cut sets `_cutByGuard` (:3513-3519).

Texts at dfb03fc:
- These carry the qualifier: lazy-reading.md:231, diagnostics.md:116 and :142, reference/reader-limits.md:21, concepts/reader-limits.md:100-101.
- These list 'a trailer' with no exception: PdfDiagnosticCodes.cs:128, PdfFileReader.cs:3771, lazy-reading.md:227, diagnostics.md:116.
- The 'chain ending there' wording: `git log -S'The trailer as far as the bound, the chain ending there' 21a0298..HEAD` finds d8e67de; main's row is 'The trailer as far as the bound'. diagnostics.md:142's 'the chain ends there' is on main at :132.
- `grep 'own cut'` finds that phrase only in PdfFileReader.cs at :676, :686, :3787 and :6683, and in no page.

The worktree is left clean; the probe was removed.

Refuter fix: Documentation only; nothing is left for the maintainer to decide (R12h already settled the behavior).

(1) Wherever 'a trailer' is listed among the limits that leave part of the chain unread, add the exception in one clause. The places:
- XRefStreamPassedOver summary, PdfDiagnosticCodes.cs:128
- ReportStreamsPassedOver remark, PdfFileReader.cs:3771
- lazy-reading.md:227
- diagnostics.md:116

Suggested wording: "a trailer — but for that of a classic table an /XRefStm names, whose links the chain never follows (#263): its cut hides no link the chain follows and ends nothing, the chain going on through the table that names it, and leaves the report made —".

(2) In reference/reader-limits.md:21 ('the chain ending there') and diagnostics.md:142 limit.trailer ('and the chain ends there'), add: "but for the trailer of a classic table an /XRefStm names, whose links the chain never follows (#263): the chain goes on through the table that names it". The fix stage may also add one clause after the 'whatever it cuts' sentence in concepts/reader-limits.md:98-106, saying such a table keeps the report.

(3) Optional: add a control row pinning the Warning for this shape, so a later change cannot withhold it silently. Use stm-pad300.pdf under MaxXRefSectionLength 250 and MaxTrailerLength 100: limit.trailer @1699, xref.hybrid-stream-lost @1638, xref.stream-passed-over @362, ChainStoppedByGuard false. Use the /Prev twin, prev-pad300.pdf, as its withheld control.

Leave the sentences the finding quotes as they are: their qualifiers already exclude this case. No code change is needed.

## 8. reachedat-summary-keeps-r12a-window-qualifier — minor → confirmed minor (src-texts)
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3900-3901 (ReachedAt summary)

Claim: ReachedAt's summary still gives R12a's rule (0578bcb): a relocation passes over 'a header found at its digit where one starts at a sign of the run its number is written with, that run lying within the bytes searched'. R12g (13e5021) took the HeaderTokenStart range out of the reached test, and R13a (a93e60a) made it look up _sectionDigitsAt at DigitsPastSigns(candidate). So a header found at its digit is now passed over wherever its run of signs starts, including where the first sign lies before the bytes searched. A header found at its first sign is passed over too, where a % on its number's line made the walk lex that line and the chain reached the stream at a later + or at its digit. On the first count the summary now says the opposite of the code, and it leaves out the second. The follow-up updated every other text but missed this one.

Evidence: Probe ZzR13SrcTexts.ZzRunBeforeWindow, in W/r13wip/src-texts/ZzR13SrcTexts.cs.txt, output head-runbefore.txt. LostStreamFiles.ThreeRevisions, readable stream: the newest /Prev names its first sign (into=0) or its second (into=1), and its own /Prev names stream+513 or stream+514, so the bytes searched start at 1651 or 1652. Run: header='++5 0 obj' past=513 into=0 stream=1650 windowBytesStart=1651 gives 'Read:1650 NotFound:2163 | xref.section-missing@2163 xref.rebuilt@-1 object.redefined@-1'. past=514 gives the same with 2164. '+++5 0 obj' and '++5 0 %c\nobj' behave alike. No row has a chain-cycle, and none has a section Relocated at the digit 1652. The header found at its digit was passed over although its run's first sign lies before the bytes searched, which is what the summary says is not passed over. R12g's own theory A_cross_reference_stream_a_prev_reaches_at_its_header_or_a_sign_of_its_number_is_not_taken_again_however_few_of_its_signs_the_bytes_searched_near_its_own_prev_hold pins the same. Blame: lines 3900-3901 come from 0578bcb, and neither 13e5021, a7bcdc6, a93e60a nor dfb03fc touched them.

Fix: Replace the clause, for example: '... passes over those where a section the chain reached starts, and a header whose digits, past the place's own run of signs, are where the reading of a stream the chain reached found them — found at its digit wherever the run starts, or at the run's first sign where the line its number is on holds a % (DigitsPastSigns, _sectionDigitsAt, #188, #327).'

Refuter: verdict confirmed, severity minor, needs_maintainer False
Reason: The finding holds. The summary of ReachedAt at PdfFileReader.cs:3899-3901 still says a relocation "passes over those where a section the chain reached starts, and a header found at its digit where one starts at a sign of the run its number is written with, that run lying within the bytes searched (#188, #327)". That is R12a's HeaderTokenStart rule, and it came in with 0578bcb. git blame on dfb03fc shows lines 3900-3901 unchanged since then: 13e5021, a7bcdc6, a93e60a and dfb03fc did not touch them. The fix stages' own lists of the texts they updated do not name this summary, and none says it was left on purpose. The R12g report keeps the window qualifier only in pages, where it belongs to the line test (validation-rules row 56, lazy-reading ~264, diagnostics row 110). Here the summary ties it to the relocation's reached test.

The reached test at dfb03fc (lines 5301-5304) is `candidate == absolute || (candidate != takeAt && (_sectionsReadAt.Contains(candidate) || _sectionDigitsAt.Contains(DigitsPastSigns(window.Memory.Span, start - lead, candidate))))`. It has no window condition and no HeaderTokenStart, which differs from the summary in two ways:
1. A header found at its digit is passed over wherever its run of signs starts, including when the first sign lies before the bytes searched. Taken as a condition, the summary's qualifier says it is not. This is narrower than the code rather than strictly "the opposite", but it misleads either way.
2. The summary leaves out the R13a case. A header found at its first sign is passed over when a % on its number's line made the walk lex that line and the chain reached the stream at a later + or at its digit. The summary's first clause (a section starts there) does not cover this, and its second clause covers only headers found at their digit.

This is a misleading text on a private member's XML doc, so minor under the brief's scale. No behavior is wrong. Nothing settled earlier holds it: it is not in decisions.md, r1-r12 findings, r12-fix2-result.json or r13a-result.json, and open point (e) concerns the pages' line-test qualifier, not this summary.

Refuter evidence: Code reading on dfb03fc (worktree detached at origin/ccr-6d6c0ee6-dqm4lm-188):
- `git blame -L 3891,3902` gives 3900-3901 = 0578bcb7 (2026-11-10 11:37). Every other summary line comes from c29761c7 or d5b4d486.
- The reached test at :5301-5304 is as quoted in the reason.
- DigitsPastSigns at :5551-5555 returns the first non-sign byte at or past the place.

Committed tests, built Release and run on dfb03fc: CrossReferenceChainTests' three theories, 57 rows, 57 passed. Log in W/r13wip/verify-reachedat-summary-keeps-r12a-window-qualifier/tests-dfb03fc.txt.
- A_cross_reference_stream_a_prev_reaches_at_its_header_or_a_sign_of_its_number_is_not_taken_again_however_few_of_its_signs_the_bytes_searched_near_its_own_prev_hold. Rows ("+5 0 obj",0,513,*), ("++5 0 obj",0|1,513|514,*), ("+++5 0 obj",0|2,513-515,*) and ("-+5 0 obj",1,513,*) start the bytes searched past the run's first sign, which stays outside them. Each pins sections [Read Table newest, Read or Malformed Stream named, NotFound Unknown stream+past] and diagnostics [(section-unreadable, named) when unreadable,] (section-missing, stream+past), (rebuilt, -1), (object.redefined, -1). There is no chain-loop: the header found at its digit is passed over although its run does not lie within the bytes searched.
- A_cross_reference_stream_a_prev_reaches_at_a_sign_or_the_digit_of_its_number_reads_the_same_whether_or_not_a_comment_ends_the_line_its_number_is_on. Rows ("++5 0 %c\nobj",1|2,...) and ("+5 0 %c\nobj",1,...): the header is found at its first sign, the chain reached it at a later sign or at the digit, and the place is passed over. The summary does not state this case.
- A_cross_reference_stream_a_prev_reaches_at_a_sign_of_its_number_is_not_taken_again_where_the_bytes_searched_start_at_its_first_sign (controls) also passes.

Refuter fix: Change only the end of ReachedAt's summary (:3899-3901), for example: "...; and a relocation, which tries positions, passes over those where a section the chain reached starts, and those where, past the place's own run of signs, the digits of a stream the chain reached start, as its reading lexed them: a header found at its digit, wherever its run of signs starts and whatever sign starts it, or at the run's first sign where the line its number is on holds a % (<see cref="DigitsPastSigns"/>, <see cref="_sectionDigitsAt"/>, #188, #327)." Also drop "that run lying within the bytes searched", which is now the line test's qualifier only. Optionally soften "which a section's first token decides" for the relocation, since its test also looks at a stream's digits. This is a docs-only commit (docs: ...) with no maintainer decision needed.

## 9. offset-adjusted-doc-never-where-reached-false-for-xrefstm-read-stream — minor → narrowed minor (src-texts)
Location: src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:20-21 (XRefOffsetAdjusted summary)

Claim: The public summary says the section is 'found nearby — a section never where a section the chain reached starts'. That is false at the byte level for #156: the /Prev's search takes what the same table's /XRefStm read in the stream's place, a start the chain reached (the code says 'taken, though reached', :5225), and reports xref.offset-adjusted there. The summary also lacks the #327 clause that the pages and the code now carry: a header whose digits past its own signs are those of a stream the chain reached is never found either. lazy-reading.md:193-197 states both the exception and the digits clause, and diagnostics.md:110 the digits clause. Two fix stages, R12a's deviation [1] and R12g's deviation [4], kept the summary as 'true at the byte level', and the probe refutes that.

Evidence: Probe ZzR13SrcTexts.ZzTakeAt, output W/r13wip/src-texts/head-takeat.txt, on HybridFiles.PrevFallingShortOfTheStreamBefore(), with oldest=247, stream=1614, newest=1770. It gives 'S /XRefStm named=1614 NamedAgain Unknown at=1614', 'S /Prev named=1607 Relocated Stream at=1614', 'S /Prev named=247 Read Table at=247' and 'D Repair xref.offset-adjusted@1614'. So the section the /Prev names was found at 1614, where the /XRefStm had read and recorded a section before the /Prev's search ran (RecordSectionReached at :3682, readInStreamsPlace, takeAt). On main 21a0298 the summary read only 'was not at the offset the file claimed, and was found nearby'; the clause came in with 761ce0b.

Fix: For example: '... found nearby — a section never where a section the chain reached starts, but for what a hybrid table's /XRefStm read in the stream's place, which that table's /Prev takes (#156), nor at a header where the digits past its own signs are those of a stream the chain reached from a + of the run its number is written with or from the digit past it (#327), nor near one that is there and cannot be read.' Optionally give diagnostics.md:110 ('never found where the chain read one') the same #156 exception.

Refuter: verdict narrowed, severity minor, needs_maintainer False
Reason: The #156 part holds, and it is a text that misstates the code. The public summary of PdfDiagnosticCodes.XRefOffsetAdjusted (PdfDiagnosticCodes.cs:19-21) says a section is found nearby "never where a section the chain reached starts". diagnostics.md:110 says "A section is never found where the chain read one". Both are false for a hybrid table whose /XRefStm read a section in the stream's place before its /Prev's search ran. That search takes the place and reports xref.offset-adjusted there. The code says so in plain words: the comment above the candidate loop (PdfFileReader.cs:5225, "is taken, though reached"), the reached-test comment (:5284-5287, "but for what the table's /XRefStm read in the stream's place, which is taken (#188, #156)"), the `candidate != takeAt` exception, and the TryRelocateXRefSection remark (:5098-5100). lazy-reading.md:193-198 and threat-model.md:449 carry the same exception, so only the public XML summary and diagnostics.md:110 leave it out. d5b4d48 wrote the "never" clause and 394028c and 3a1ac79 added the takeAt exception, but the summary never got the exception. No earlier round raised this, and no issue holds it. R12a's deviation [1] and R12g's deviation [4] judged the summary "true at the byte level" only for signed headers, not for this case.

The finding is narrowed on its second half. The summary not naming the #327 digits exclusion is a gap, not a misstatement. The summary names one kind of place where a section is never found. Passing over more places (a header whose digits past its own signs are those of a stream the chain reached) does not make "never where a section the chain reached starts" false. A header found at its digit, or at its first sign on a line that holds a %, is not where the reached stream starts unless that stream starts there too, and then _sectionsReadAt passes it over anyway. That gap was a deliberate fix-stage choice, and the probe does not refute it. Only the #156 exception does.

Refuter evidence: Rebuilt on origin/ccr-6d6c0ee6-dqm4lm-188 at dfb03fc (Release, 0 warnings). Probe: C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r13wip/verify-offset-adjusted-doc-never-where-reached-false-for-xrefstm-read-stream/ZzR13VerifyTakeAt.cs.txt. Output: head-takeat.txt in the same folder. The probe runs HybridFiles.PrevFallingShortOfTheStreamBefore() (oldest=247, stream=1614, newest=1770; the table's /Prev names 1607 and its /XRefStm names 1614). Output:
  S[0] startxref named=1770 Read Table at=1770
  S[1] /XRefStm named=1614 NamedAgain Unknown at=1614 readAs=2
  S[2] /Prev named=1607 Relocated Stream at=1614
  S[3] /Prev named=247 Read Table at=247
  D Repair xref.offset-adjusted@1614: The cross-reference section /Prev names at offset 1607 was found 7 bytes after it.
  D Warning xref.hybrid-stream-lost@1614: ... names offset 1614, the section its /Prev names too, which the chain read through that /Prev ...
  repaired=False
The /XRefStm's reading runs first (PdfFileReader.cs:3682 RecordSectionReached(stream, sectionsAt); :3685 readInStreamsPlace = stream). The /Prev's search then finds the section at 1614, a start the chain reached, and reports offset-adjusted there. The /XRefStm's record is relabeled NamedAgain afterwards (ReadAs = the /Prev's record). The committed theory A_prev_falling_short_of_the_stream_its_xrefstm_read_reads_it_whole_and_the_naming_is_reported (CrossReferenceChainTests.cs:5929) pins the same sections and codes and passes at dfb03fc (6 rows plus the probe, 7/7). Text state at dfb03fc: PdfDiagnosticCodes.cs:20 "a section never where a section the chain reached starts, nor near one that is there and cannot be read" (no exception); diagnostics.md:110 "A section is never found where the chain read one" (no exception); lazy-reading.md:196-198 "... but for what a table's /XRefStm read in the stream's place, which that table's /Prev takes, reading it whole"; threat-model.md:449 "but for what a table's /XRefStm read in the stream's place, which that table's /Prev takes, as above ([#156])". On main 21a0298 the summary had no "never" clause; d5b4d48 added it.

Refuter fix: A text-only fix for the final pass; no maintainer decision is needed. In PdfDiagnosticCodes.cs:19-21, change the clause to: "found nearby — a section never where a section the chain reached starts, but for what a hybrid table's <c>/XRefStm</c> read in the stream's place, which that table's <c>/Prev</c> takes, reading it whole (#156), nor near one that is there and cannot be read." In diagnostics.md:110, give the sentence the same exception, using the wording lazy-reading.md:196-198 already has: "A section is never found where the chain read one, but for what a table's `/XRefStm` read in the stream's place, which that table's `/Prev` takes, reading it whole ([#156](https://github.com/AdCodicem/AdCodicem.Pdf/issues/156)) — nor at a header where ...". Adding the #327 digits clause to the XML summary is optional, since its absence misstates nothing. If it is added, use the pages' words: "nor at a header where the digits past its own signs are those of a stream the chain reached (#327)". No test change is needed: the theory at CrossReferenceChainTests.cs:5929 already pins the behavior.

## 10. sign-run-texts-take-a-minus-for-a-plus — nit → confirmed nit (src-texts)
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:5541 (DigitsPastSigns remark); src/AdCodicem.Pdf/IO/XRef/XRefSectionRecord.cs:331 (DigitsAt); src/AdCodicem.Pdf/IO/PdfFileReader.cs:5289-5291 (comment above the reached test)

Claim: Three texts are true of a '+' and false of a '-'. (1) DigitsPastSigns' remark says 'a reading that started at a later sign of that run, or at the digit past it, read the same header'. XRefSectionRecord.DigitsAt says the same with 'from a later sign of the run or from its digit'. A later '-' reads a negative number and no header. Every other text, and the code's rule, says 'a + of that run'. (2) The comment above the reached test says 'The walk back from obj finds it at that digit — ... whatever sign starts it, a - among them ... —, or at the run's first sign where it lexed the line the number is on, which holds a %'. A run a '-' starts, on a line holding a %, is lexed as a negative number, and the walk finds the header nowhere, though a reading from its '+' reads it. Neither case changes behavior: there is no place to pass over, and no loop.

Evidence: Probe ZzR13SrcTexts.ZzSignRuns, output W/r13wip/src-texts/head-signruns.txt; readable stream, oldest table in reach (before 0, own /Prev at stream+30). For (1): header='+-5 0 %c\nobj' into=1 (the '-') gives 'Relocated:450 Relocated:200 | xref.offset-adjusted@450 xref.offset-adjusted@200'. The reading at 451 read no header, and the stream was found moved at its first sign, 450. For (2): header='-+5 0 %c\nobj' into=0 gives 'Relocated:200 | xref.offset-adjusted@200' in reach and 'NotFound:1650 | xref.section-missing@1650 xref.rebuilt@-1' out of reach. The walk found the header neither at its digit (452) nor at its first sign (450). The twin without the % finds it at its digit: '-+5 0 obj' into=0 gives 'Relocated:452'.

Fix: (1) Write 'a later + of that run' in both places. (2) Write '... or, where it lexed the line the number is on, which holds a %, at the run's first sign, a run a - starts being then no header the walk finds'.

Refuter: verdict confirmed, severity nit, needs_maintainer False
Reason: The finding holds as stated. I rebuilt it at dfb03fc and the code matches the reviewer's account on every point.

(1) A reading that starts at a later '-' of a '+'-started run reads no header. DigitsPastSigns' remark (PdfFileReader.cs:5541) says the reading reads "the same header", which is false there. XRefSectionRecord.DigitsAt (:331) says the same thing more weakly ("from a later sign of the run"). Every other text says "a + of that run": the comment above the reached test, the HeaderTokenStart remark, diagnostics.md:110/111 and threat-model.md:449.

(2) The comment above the reached test (PdfFileReader.cs:5289-5291) gives two cases: the walk finds the header at its digit, or at the run's first sign where it lexed the line. It misses a third case. For a run that starts with '-' on a line holding a '%', TakeLine lexes '-+5' as -5 and the walk finds the header nowhere. A reading from that run's '+' still reads the header.

There is no change in behavior. Nothing is reached at a '-', and a header the walk does not find is no place, so the reached test has nothing to tell, and nothing loops. The lost stream in '-+5 0 %c' named at its '-' (in reach, the oldest table taken) is the same on main. Main's walk finds no header behind '%c\nobj' at all, and R13a recorded the into=1 case as main's.

The texts are new in R13a: the DigitsPastSigns remark and the comment are a93e60a's, and the DigitsAt summary was changed in dfb03fc. No decision, earlier round, residual or #327 comment covers them, so they are not known.

Severity: nit. Each is a general statement that is false only for a narrow hostile shape, two or more signs where ISO allows one. The code is right, and the texts next to them state the '+' correctly. This is the same call as round 12's [24] and round 11's [4]. The fix is wording only.

Slightly wider than reported: the same omission as (2) is in the pages, at diagnostics.md:110 and :111 and threat-model.md:449. Each says "found at its digit, wherever that run starts and whatever sign starts it, or at the run's first sign where the line its number is on holds a %" and does not say that a '-'-started run on such a line is found nowhere. threat-model.md:447 already handles the '-' correctly.

Refuter evidence: Branch dfb03fc (origin/ccr-6d6c0ee6-dqm4lm-188), Release build with 0 warnings. Probe: W/r13wip/verify-sign-run-texts-take-a-minus-for-a-plus/ZzVerifySignTexts.cs.txt (generated by gen.py in the same folder). Outputs: head-walk.txt and head-signruns.txt in that folder. W = C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud.

The walk, PdfFileReader.TryReadHeaderBackwards called directly:
- '+-5 0 %c\nobj' -> True, number=5, headerStart=0 (first sign)
- '-+5 0 %c\nobj' -> False (no header)
- '+-+5 0 %c\nobj' -> True, headerStart=0
- '-+5 0 obj' -> True, headerStart=2 (digit)

Files from LostStreamFiles.ThreeRevisions with a readable stream at 450 (1650 out of reach) and the oldest table at 200:

(1) A reading at a later '-' reads nothing. A later '+' reads the header.
- '+-5 0 %c\nobj' into=1 (the '-'): '/Prev named=451 Relocated Stream at=450', [offset-adjusted@450, offset-adjusted@200]. Out of reach: 'named=1651 Relocated Stream at=1650'.
- '+-+5 0 %c\nobj' into=1 (the '-'): 'named=451 Relocated Stream at=450'.
- '+-+5 0 %c\nobj' into=2 (the second '+'): 'named=452 Read Stream at=452'.

(2) A reading at the '+' reads the header, but the walk never finds it.
- '-+5 0 %c\nobj' into=1: 'named=451 Read Stream at=451', object 5 null, Error entry-broken@450.
- '-+5 0 %c\nobj' into=0 in reach: 'named=450 Relocated Table at=200', [offset-adjusted@200], objects 4 and 5 null, 7 'old seven'.
- '-+5 0 %c\nobj' into=0 out of reach: 'named=1650 NotFound', [section-missing@1650, rebuilt@-1].
- The twin without the %, '-+5 0 obj' into=0: 'Relocated Stream at=452'.

Main's TryReadHeaderBackwards (git show 21a0298, around :6294) has no line lexing. It fails on any '...%c\nobj', so the % rows' loss of the stream is not a regression. The texts: PdfFileReader.cs:5541 and :5289-5291 come from a93e60a, and XRefSectionRecord.cs:331 from dfb03fc. The same omission as (2) is in diagnostics.md:110 and :111 and threat-model.md:449.

Refuter fix: Wording only. No code change, no ADR 53 run, no maintainer decision.

(1) Write "a later + of that run" in place of "a later sign of that run" in the DigitsPastSigns remark (PdfFileReader.cs:5541), and "from a later + of the run" in place of "from a later sign of the run" in XRefSectionRecord.DigitsAt (XRefSectionRecord.cs:331).

(2) In the comment above the reached test (PdfFileReader.cs:5289-5291), qualify the lexed case. For example: "... or, where it lexed the line the number is on, which holds a %, at the run's first sign — a run a - starts then reads no header there, and the walk finds none —". Use the same qualifier, or "a run a + starts", in diagnostics.md:110 and :111 ("or at the first sign of a run a + starts where the line its number is on holds a %") and in threat-model.md:449.

Optionally, add a walk row pinning '-+5 0 %c\nobj' as no header beside the existing '-+5 0 %c' read-at-'+' theory. The existing theory already pins the outcome, so the row is not required.

## 11. header-cut-enumeration-omits-white-space-between-tokens — nit → confirmed nit (src-texts)
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:684-685 (_guardMayHideLink); :3777-3778 (ReportStreamsPassedOver remark); :6667-6669 (cut-test comment); src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:133-134; the same enumeration in diagnostics.md:116, reader-limits.md (reference) :68-69 and :256-257, lazy-reading.md

Claim: Every text enumerates a header cut as 'its number or its generation running on past the guard, or a comment between its tokens'. White space between the header's tokens, spaces or ends of line, running past MaxTrailerLength is a header cut too. The parser marks it truncated ('white space up to it', PdfObjectParser.cs:339-345), so the guard is reached and the flag is set (`!candidate && cutByGuard`), and the reports are withheld. The code agrees with 'whatever it cut'. The enumerations, which the pages hold word for word, leave the case out.

Evidence: Probe ZzR13SrcTexts.ZzHeaderCuts, output W/r13wip/src-texts/head-headercuts.txt. File: HybridFiles.PassingOverAStreamAnOlderStreamWithAPaddedTypeNames('stream 6', namesOwn: false, header: H), with stream 7 at 358, which stream 6's /Prev names. H='7 0'+300 spaces+'obj' at the default limits gives '/Prev:Read:358 | ... xref.stream-passed-over@2405'. At MaxTrailerLength 160 or 200 it gives '/Prev:NotFound:358 | xref.offset-adjusted@2226 xref.hybrid-stream-lost@2226 limit.trailer@358 xref.section-missing@358', chainStopped=False and no stream-passed-over. H='7'+300 spaces+'0 obj' and H='7 0'+300 ends of line+'obj' give the same.

Fix: Write 'its number or its generation running on past the guard, or white space or a comment between its tokens' in the XML docs, the comment and the pages, word for word.

Refuter: verdict confirmed, severity nit, needs_maintainer False
Reason: The finding holds. The gap in the texts is wider than claimed, but it stays a wording nit. Each text opens with a general clause that is correct ('whatever it cut', or in the limit.trailer row 'where the cut falls before any object is read'). The code agrees with that clause (`!candidate && cutByGuard`, PdfFileReader.cs:6680). The em-dash list that follows reads as exhaustive. It describes a header cut only as 'its number or its generation running on past the guard, or a comment between its tokens'.

That list leaves out shapes that behave identically:
- White space between the header's tokens (spaces or ends of line, between number and generation or between generation and obj). The parser marks this truncated (PdfObjectParser.cs:339-345, 'white space up to it'). MayBeginHeader holds, so TryParseNumberedAt reaches the guard at :8799-8801.
- The obj keyword parted by the cut ('7 0 o' and '7 0 ob', which the parser comment at :340 names itself).
- White space or a comment between a whole header and its value. Here the value is cut to null and the object reads as no dictionary. That case is none of 'a dictionary', 'the object's header' or 'a token where no header starts'.

All of these give the same reports as the enumerated shapes, so no behavior is wrong. No text states something false; only the illustrative list is incomplete. That makes it a nit, not a minor: a reader is not misled about what the reader does, because the governing clause covers every case. It is not known or accepted elsewhere. No round 1 to 12 finding, decisions.md entry or r12-fix2/r13a open point covers it, and R12-Q4's accepted behavior is not disputed. The finding lists only part of the places that carry the enumeration.

Refuter evidence: The probe was rebuilt on dfb03fc, in this worktree with a Release build and 0 warnings. Source: W/r13wip/verify-header-cut-enumeration-omits-white-space-between-tokens/ZzVerifyHeaderCutWs.cs.txt. Output: head-wscuts.txt in the same folder.

Every file is HybridFiles.PassingOverAStreamAnOlderStreamWithAPaddedTypeNames("stream 6", false, header: H). In it, stream 7 sits at 358, which stream 6's /Prev names.

At default limits, every row gives '/Prev named=358 Read Stream' plus 'Warning xref.stream-passed-over@<own>'. At MaxTrailerLength 160, every row below gives the same result:
- '/Prev named=358 NotFound Unknown at=358 cut=False'
- 'chainStopped=False'
- 'D Warning limit.trailer@358'
- 'D Warning xref.section-missing@358 :: The cross-reference section /Prev names at offset 358 is not there, nor near it; ...'
- no stream-passed-over

The rows:
- A: '7 0'+300 spaces+'obj'
- B: '7'+300 spaces+'0 obj'
- C: '7 0\n'+300 '\n'+'obj'
- D (control, enumerated): '7 0 %'+300 dashes+'\nobj'
- E (control, enumerated): 300 zeros+'7 0 obj'
- F: '7 0'+155 spaces+'obj', so the window holds 'ob'
- G: '7 0'+156 spaces+'obj', so the window holds 'o'
- H: '7 0 obj'+300 spaces before '<<'
- I: '7 0 obj %'+300 dashes before '<<'

Control J, '7 0 obj' at 160, is different: '/Prev named=358 Malformed Stream cut=True', chainStopped=True, limit.trailer@358 alone. This matches the reviewer's head-headercuts.txt rows for A, B and C (bounds 160 and 200).

The enumeration, word for word or nearly so, appears at:
- src/AdCodicem.Pdf/IO/PdfFileReader.cs:684-685, :3777-3778 and :6667-6669
- src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:133-134
- docs/website/docs/reference/diagnostics.md:116 and :142 (limit.trailer row: 'the object's number or its generation running on past the bound, a comment between its header's tokens, a token where no header starts')
- docs/website/docs/reference/reader-limits.md:21, :68 and :257
- docs/website/docs/concepts/reader-limits.md:101
- docs/website/docs/concepts/lazy-reading.md:233
- docs/threat-model.md:430, :447 and :454

The finding names only part of this list. The test comments at CrossReferenceChainTests.cs:8158 and :9437 describe the theory's own three shapes and are accurate.

Refuter fix: This is a wording change in every place listed in the evidence. No maintainer decision is needed.

Replace the header's dash list with one that names any part of the header:

  'the object's header — any of its tokens, its number, its generation or its obj keyword, running on past the guard, or white space or a comment between them —'

In the limit.trailer row (diagnostics.md:142), use the same list:

  'the object's header, any of its tokens running on past the bound or white space or a comment between them, a token where no header starts'

Optionally, close the remaining hole too. In the flag's summary, the :6667 comment and :3777, add 'or white space or a comment past a whole header, before its value, the object read as no dictionary'. A shorter alternative is to drop the inner list and write 'the object's header, any part of it'. The general clause 'whatever it cut' already carries the rule.

Optionally, add white-space rows to the header-cut theory in CrossReferenceChainTests (A to C, F to H above) so the widened text is pinned. They pass on dfb03fc.

## 12. cut-dictionary-texts-lack-the-guard-section-qualifier-and-drift — nit → narrowed minor (src-texts)
Location: src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:131-133 (XRefStreamPassedOver); src/AdCodicem.Pdf/IO/PdfFileReader.cs:3753-3754 (ReportStreamsPassedOver summary); :676 and :647

Claim: (1) XRefStreamPassedOver says 'wherever it cut the reading at an offset the chain names, whatever it cut — a dictionary, whatever its /Type, the parser keeping it as far as it read it and the object then read as one of another type'. ReportStreamsPassedOver's summary says '... which then reads as one of another type'. A cut dictionary of /Type /XRef, or one whose /Type is no name cut before its /W, is read as the guard's section, and the chain stops. diagnostics.md:116, reader-limits.md and lazy-reading.md carry the qualifier 'that the reading does not take for a section the guard cut', and the XML docs do not. (2) The enumeration of what the cut may hide drifts between texts. XRefStreamPassedOver and diagnostics.md:116 say 'a stream of another /Type that gives a /W, a /Type given twice whose later value is /XRef'. _guardMayHideLink says '... a /W, ... whose later /XRef lies past the cut'. ReportStreamsPassedOver's remark and the cut-test comment say '... a /W past the cut, ... whose later /XRef lies past it'. reader-limits.md:71 and lazy-reading.md:236 say 'a /Type given twice'. (3) _guardMayHideLink's first sentence ('where it left nothing the chain's own cut marks') reads as if the flag were set only there, but it is now set where the chain's own cut marks it too. _streamsPassedOver's doc (:647-648) still gives only 'may have hidden a link the chain would have followed', without 'or cut what would end it'.

Evidence: Same probe and output as above, head-headercuts.txt. H='7 0 obj' at MaxTrailerLength 160 cuts stream 7's /Type /XRef dictionary past its /Type. namesOwn=True gives '/Prev:Malformed:358:cut | xref.offset-adjusted@1944 xref.hybrid-stream-lost@1944 limit.trailer@358' with chainStopped=True. namesOwn=False gives '/Prev:Malformed:358:cut ... limit.trailer@358' with chainStopped=True. The cut dictionary is read as the guard's section, not as an object of another type. The report is withheld by the chain's own cut. (2) and (3) are textual and were compared line by line: grep 'given twice' over src/ and the pages.

Fix: Add 'that the reading does not take for a section the guard cut' to the two XML sentences, as the pages have it. Give the enumeration one wording in the four src texts and the pages, for example 'a stream of another /Type that gives a /W past the cut, a /Type given twice whose later /XRef lies past it'. Make _guardMayHideLink's first sentence say that the flag is also set where the chain's own cut marks the cut. Add 'or cut what would end it' to _streamsPassedOver's doc.

Refuter: verdict narrowed, severity minor, needs_maintainer False
Reason: (1) Holds, checked on dfb03fc against the code and my own probe. Two XML sentences say a cut dictionary "whatever its /Type" is then read as an object of another type: XRefStreamPassedOver at PdfDiagnosticCodes.cs:131-133 (public API doc) and the ReportStreamsPassedOver summary at PdfFileReader.cs:3753-3754. That is false in two cases, which are instead read as the guard's section, with section.CutByLimit set and the chain stopped: a dictionary of /Type /XRef cut past its /Type, and a dictionary whose /Type is no name (TryReadXRefStream :6703-6716 and :6735). R12-Q1's own wording, the pages and the private texts all carry the qualifier ("that the reading does not take for the guard's section"; private texts at :686, :3786-3788 and :6683). So the public doc contradicts the site page for the same code. That is a misleading text, so minor under the brief's scale. It is not major: no report is wrong. The guard's-section case still withholds the reports, through the chain's own cut, which the same doc's earlier sentence covers ("a section it cut before the section said what it is ... a trailer"). The wording came in with 78885bf (R12c) and 69731a2 (R12h), so no earlier round reviewed it, and no decision settles it. (2) Narrowed. "A /Type given twice whose later value is /XRef" and "whose later /XRef lies past it" mean the same thing. The parser keeps the last value, so a later /XRef before the cut makes the dictionary /XRef, which is the guard's section. The short form "a /Type given twice" in reader-limits.md:71 and lazy-reading.md:236 is a harmless shorthand. On /W, though, the two wordings differ, and the narrower one is wrong, not the other way round. "A stream of another /Type that gives a /W past the cut" (ReportStreamsPassedOver remarks :3781, code comment :6674) leaves out a /W read before the cut with the stream past it. The code's own comment at :6731-6733 names that case, and so does the test comment above A_stream_passed_over_near_a_hybrid_table_s_prev_is_reported_where_no_link... ("whose /W, past the cut or before it"). The probe confirms it: /Catalog read whole is the section, and cut past its /W it is NotFound. The reviewer's fix would spread the narrower form. (3) Holds as a nit. The opening sentence of _guardMayHideLink (:675-676, "where it left nothing the chain's own cut marks") predates R12c, when it was exact. The flag is now also set where the chain's own cut marks the cut, which the doc's own later sentence states. The _streamsPassedOver doc at :647-648 leaves out "or cut what would end it" and points to _guardMayHideLink. Both are private and corrected or completed in the docs they point to.

Refuter evidence: Probe (mine): r13wip/verify-cut-dictionary-texts-lack-the-guard-section-qualifier-and-drift/ZzVerifyCutDict.cs.txt and ZzVerifyCutDict.wbefore.cs.txt. Each runs HybridFiles.PassingOverAStreamAnOlderStreamWithAPaddedTypeNames("stream 6", namesOwn, type) on dfb03fc with MaxTrailerLength = typeAt + window. Outputs are head-cutdicts.txt and head-cutdicts-wbefore.txt.

/Type /XRef, window 5 or 12 (cut past its /Type), both namesOwn: "S /Prev named=358 Malformed Stream at=358 cut=True fault=its dictionary is not followed by stream data", "chainStopped=True", and diagnostics only "xref.offset-adjusted, xref.hybrid-stream-lost, limit.trailer@358". This is the guard's section, not an object of another type.

/Type (notaname), windows -3, 5 and 12: "Malformed Unknown at=358 cut=True fault=holds object 7, whose dictionary one of the reader's limits cut before it declared a cross-reference stream", chainStopped=True.

/Catalog, window 5 or 12: "NotFound ... holds object 7, which is not a cross-reference stream", "limit.trailer@358 xref.section-missing@358", chainStopped=False, and no xref.stream-passed-over with namesOwn=False. This case is read as another type.

/Foo /Type /XRef, window 5 or 12: NotFound, the same as /Catalog. Window -3: the guard's section.

/Catalog with its /W read before the cut (window 30 or 40): "NotFound Stream ... which is not a cross-reference stream", section-missing, reports withheld. Window 0 (read whole): "Read Stream at=358". So the cut hid the stream, not the /W.

Texts: PdfDiagnosticCodes.cs:131-133 and PdfFileReader.cs:3753-3754 have no qualifier. The qualifier is present in diagnostics.md:116, reader-limits.md:66, lazy-reading.md:231, threat-model.md:426, decisions.md:1001 (R12-Q1) and PdfFileReader.cs:686, :3786-3788 and :6683. git log 1cd2216..dfb03fc puts the XML wording in 78885bf and 69731a2. The reviewer's head-headercuts.txt ('7 0 obj', bound 160: "/Prev named=358 Malformed Stream ... cut=True", chainStopped=True) agrees.

Refuter fix: Text only, no design choice.

(1) In XRefStreamPassedOver (PdfDiagnosticCodes.cs:131) and in the ReportStreamsPassedOver summary (PdfFileReader.cs:3753), add after "whatever it cut" the pages' qualifier, ", that the reading does not take for a section the guard cut". Optionally end the sentence with the remarks' "where the reading takes it for the guard's section, the chain's own cut withholds the reports already".

(2) Do not unify toward "a /W past the cut". In the ReportStreamsPassedOver remarks (:3781) and the comment at :6674, drop "past the cut" from the /W item, or write "a stream of another /Type that gives a /W, before the cut or past it", so they agree with XRefStreamPassedOver, _guardMayHideLink :692, the pages and the code's own comment at :6731-6733. The two /Type-given-twice wordings are equivalent. Harmonizing them is optional, and the pages' "a /Type given twice" may stay.

(3) Change the opening of _guardMayHideLink (:675-676) to something like "Whether ... may have hidden a link the chain would have followed, or cut what would end the chain, at an offset the chain names, beyond what the chain's own cut marks (ChainStoppedByGuard), which it is set beside too". In _streamsPassedOver's doc (:647-648), add "or cut what would end it". Both changes in (3) are nits, for the final pass.

## 13. r12b-x2-equivalence-refuted-at-head — minor → narrowed minor (tests)
Location: src/AdCodicem.Pdf/IO/XRef/XRefSectionRecord.cs:367 (RelocateTo's `DigitsAt = attempt.DigitsAt;`); read at src/AdCodicem.Pdf/IO/PdfFileReader.cs:4136 and :5239

Claim: R12b classed mutant X2 (RelocateTo copies 0 instead of attempt.DigitsAt) as equivalent, on the grounds that only the record of the stream an /XRefStm read is ever read. That stopped being true with 13e5021 (R12g). RecordSectionReached now adds every reached section's DigitsAt to _sectionDigitsAt, and NumberRunsInto takes streamDigits from the /XRefStm-read stream's record, which may be a relocated one. A relocated stream's digits can therefore be observed. An input tells X2 apart from the code, and no committed test kills it: X2 survives the whole suite. X1 (the -1 initializer removed) is no longer equivalent either, for the same reason, but the first-byte theory kills it.

Evidence: Probe: W/r13wip/tests/ZzR13X2Probe.cs.txt, where W = C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud. The file has no header. Cross-reference stream 9 starts at offset 0 and indexes the catalog and the page tree. Past 1,200 bytes of comment come object 3 and stream 13 (indexes 3 and 13). The newest table names stream 13 through /XRefStm, 5 bytes into its header (inside obj, so it is found moved), and gives /Prev 4, inside stream 9's header. Output at dfb03fc (W/r13wip/tests/x2probe-head.txt): 'sections: startxref Read Table 2152, /XRefStm Relocated Stream 1435, /Prev Relocated Stream 0; diagnostics: header.missing@0, xref.offset-adjusted@1435, xref.offset-adjusted@0; repaired at opening: False'. With X2 applied (x2probe-X2.txt): '/Prev NotFound Unknown 4; diagnostics: header.missing@0, xref.offset-adjusted@1435, xref.section-missing@4, xref.rebuilt@-1; repaired at opening: True'. X2 makes the relocated stream's digits 0, so NumberRunsInto maps candidate 0 onto stream 13 and the place is passed over. X1 gives the same output (x2probe-X1.txt). With the committed tests (W/r13wip/tests/mut-x12-classes.txt): 'bX1: KILLED by classes ('1638','2') A_section_at_the_file_s_first_byte_is_a_place_like_any_other(newest: "/Prev 999999 /XRefStm 4"...)' and 'bX2: SURVIVED classes ('1638','0')'. Whole suite run alone, without the timing groups (mut-x2-alone.txt): 'bX2: SURVIVED suite rc=0 ('10970','0')'. Under parallel load, X2 was once 'killed' only by CorpusReadingTests' 20 s bound (mut-head2.txt, log logs/mut-head2-bX2-suite.txt), which is the known flake. Driver: W/r13wip/tests/mutrun.py with spec-head.json.

Fix: Add a test with this shape, pinning (startxref Read, /XRefStm Relocated at stream 13, /Prev Relocated at 0), no rebuild at opening, and diagnostics exactly [header.missing@0, offset-adjusted@13's header, offset-adjusted@0]. A new row or fact beside A_section_at_the_file_s_first_byte_is_a_place_like_any_other would do: there a stream found moved comes before a /Prev search that finds a section at the file's first byte. In the batch-wide ADR 53 record, reclass X2 as killed by that test and X1 as killed by the first-byte theory. R12b's stated reason ('DigitsAt is read only of a stream's record') no longer holds since 13e5021.

Refuter: verdict narrowed, severity minor, needs_maintainer False
Reason: The finding holds at dfb03fc, but its history is wrong. Some input tells X2 (RelocateTo's `DigitsAt = 0`) apart from the code, and the whole committed suite still passes with X2 applied. So R12b's 'equivalent' is a wrong judgment, and a gap in the tests.

The equivalence did not stop holding with 13e5021: it never held. On dadde18, R12b's own code, the reviewer's exact shape already tells X2 apart. The test that does it is NumberRunsInto's `candidate == streamDigits`. With X2, a relocated stream that an /XRefStm read has streamDigits 0. Candidate 0 (a section at the file's first byte) is then mapped onto that stream, which is takeAt and gives no /Prev, so MayBeTablesOwn passes it over. R12b's reason ('a stream found moved starts at the header the search found... its digits start where its reading did') overlooked candidate 0.

R12g (13e5021) added a second place that reads the digits. With no /XRefStm at all, a section found moved puts 0 into _sectionDigitsAt through RecordSectionReached. A later /Prev search then passes over a section at offset 0. That shape tells X2 apart only from R12g on.

X1 is killed at head, by two rows of A_section_at_the_file_s_first_byte_is_a_place_like_any_other. Two independent runs confirm it: the reviewer's, and the one in verify-x2-equivalence-refuted-since-r12g.

Severity is minor: this is a test gap and a wrong line in the ADR 53 record. No wrong object is served and no report is wrong. Under ADR 53, a survivor is settled in the mutation step and does not restart the review. The brief's open point (c) does ask for exactly this check. The line must be corrected before the record reaches docs/status.md.

Refuter evidence: Probe rebuilt by me. W = C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud, and V = W/r13wip/verify-r12b-x2-equivalence-refuted-at-head. The probe is V/ZzR13X2Verify.cs.txt. It holds six /XRefStm variants of the reviewer's file, plus a 'prev chain, no /XRefStm' file. In that file the newest table's /Prev names stream 13 five bytes into its header, and stream 13 gives /Prev 4.

dfb03fc (V/out-head.txt, V/out-headb.txt). The reviewer's shape reads 'startxref Read Table 2152, /XRefStm Relocated Stream 1435, /Prev Relocated Stream 0; header.missing@0, xref.offset-adjusted@1435, xref.offset-adjusted@0; repaired at opening: False'. The prev chain reads '/Prev Relocated Stream 1435, /Prev Relocated Stream 0', with the same three diagnostics and no repair.

dfb03fc with X2 (V/out-x2.txt, V/out-x2b.txt). The reviewer's shape reads '/Prev NotFound Unknown 4; header.missing@0, xref.offset-adjusted@1435, xref.section-missing@4, xref.rebuilt@-1; repaired at opening: True'. /Prev 2 gives the same with section-missing@2. When stream 13 itself gives /Prev 4, the output is '/XRefStm NamedAgain Unknown 1440, /Prev Relocated Stream 1435 ... xref.hybrid-stream-lost@1440, xref.chain-cycle@4, xref.rebuilt@-1'. The prev chain reads '/Prev NotFound Unknown 4 ... xref.section-missing@4, xref.rebuilt@-1; repaired at opening: True'.

Whole suite on dfb03fc with X2 (V/suite-x2.log). It ran alone in my worktree, with the 242 remote documents hard-linked and the timing, budget, allocation and memory groups included: 'total: 11045 failed: 0 succeeded: 11042 skipped: 3 rc=0'. That is the 11,044 committed tests plus my probe, which asserts nothing. X2 survives.

dadde18 (V/out-dadde18.txt) is identical to head. dadde18 with X2 (V/out-dadde18-x2.txt) gives the same three /XRefStm differences as head with X2. Its prev chain is unchanged, so on dadde18 only NumberRunsInto tells X2 apart, and R12b's classification was already wrong there.

Worktree restored to dfb03fc and clean; the remote corpus copy was removed; the maintainer's 242 remote files are intact.

Refuter fix: Add a theory beside A_section_at_the_file_s_first_byte_is_a_place_like_any_other in CrossReferenceChainTests. It builds a headerless file: stream 9 at offset 0, indexing the catalog and the page tree; 1,200 bytes of comment; object 3; stream 13, indexing 3 and 13; 600 bytes of comment; the newest table. It needs two rows.

Row 1: the table gives '/Prev 4 /XRefStm <13's header + 5>'. Expect 'startxref Read, /XRefStm Relocated <13>, /Prev Relocated 0', diagnostics exactly [header.missing, xref.offset-adjusted@<13>, xref.offset-adjusted@0], and WasRepaired false. This row holds the place NumberRunsInto reads the digits; it fails under X2 on both dadde18 and head.

Row 2: the table gives '/Prev <13's header + 5>' and stream 13 gives '/Prev 4'. Expect '/Prev Relocated <13>, /Prev Relocated 0' with the same diagnostics. This row holds the _sectionDigitsAt read that R12g added.

Either row kills X2. Both are worth having so that each place that reads the digits is held. In the batch's ADR 53 record (r12-fix-result.json, decisions.md, then docs/status.md): reclass X2 as killed by that theory, and X1 as killed by the two first-byte rows ('/Prev 999999 /XRefStm 4' and '/Prev 0 /XRefStm 4'). R12b's equivalents then read U1, U2, U3, T1, D3, N8, N9. Strike R12b's X2 reason: NumberRunsInto's `candidate == streamDigits` tells X2 apart at candidate 0. This is not a design choice for the maintainer.

## 14. naming-updates-stream-summary-oldest-always-in-reach — minor → confirmed minor (tests)
Location: tests/AdCodicem.Pdf.Tests/HybridFiles.cs:1669-1671 (NamingTheUpdatesStreamAtOrInsideItsNumber summary)

Claim: The builder's summary ends 'A search near the place the /Prev names comes to the stream's header ..., and to the oldest table farther off, within reach', with no condition. Since R12b added prevFrom, three theories call it with prevFrom 513 to 515 (27 rows: ..._from_a_sign_and_that_gives_no_prev_..., ..._whose_prev_names_an_offset_is_read_as_named_again_..., A_hybrid_table_s_own_stream_read_from_a_sign_that_cannot_be_read_...). In those rows the oldest table lies 702 to 704 bytes from that place, outside the 512-byte reach, and the tests' own comments say it 'lies out of reach'. R12e rewrote this summary but left the clause. The sibling NamingTheUpdatesStreamAgainAtOrInsideItsNumber does qualify it ('where that place lies 7 bytes before the stream').

Evidence: Builder dump (probe W/r13wip/tests/ZzR13TestsDump.cs.txt, output W/r13wip/tests/dump.txt): '== NamingTheUpdatesStreamAtOrInsideItsNumber header=++13 0 obj into=0 prevFrom=514 catalog=True: stream=418 oldest=229 newest=1170 ... prev=932: window start=420 lead=[+] first=[13 0 o] at prev=[------------] oldest distance=703'. With prevFrom=513: 'oldest distance=702'. With prevFrom=515: 'oldest distance=704'. With prevFrom=7 and 30: 'oldest distance=196' and '219', the only rows the clause holds for.

Fix: Qualify the clause, as the sibling summary does: '..., and, where that place lies within 512 bytes of it (7 or 30 bytes from the stream's header), to the oldest table farther off; a place 513 to 515 bytes past the header leaves the oldest table out of reach, the bytes searched starting at a sign of the run or at its digit, the window holding the byte before them'.

Refuter: verdict confirmed, severity minor, needs_maintainer False
Reason: The finding holds as stated, and none of decisions.md, r1-findings.md to r12-findings.md, the fix results or the filed debts already covers it. At dfb03fc the summary of HybridFiles.NamingTheUpdatesStreamAtOrInsideItsNumber (tests/AdCodicem.Pdf.Tests/HybridFiles.cs:1669-1671) ends 'and to the oldest table farther off, within reach' with no condition. Since 589aeb3 added prevFrom, three theories pass 513 to 515, 27 rows in all: CrossReferenceChainTests.cs:6618-6629, 6670-6679 and 6736-6740. In those rows the oldest table starts 702 to 704 bytes before the place the /Prev names. The search reaches 512 bytes either side (PdfFileReader.NearbySearchRadius), so the table is out of reach, and the theories' own comments say it 'lies out of reach' (6637). The clause holds only for prevFrom -7 (the default), 7 and 30. The sibling NamingTheUpdatesStreamAgainAtOrInsideItsNumber already makes the clause conditional ('where that place lies 7 bytes before the stream'). Line 1671 is from d60acb4 (R12e), which rewrote this sentence and kept the unconditional clause. This is a misleading text in a test builder's summary, so minor on the brief's scale. Code behavior is unaffected.

Refuter evidence: I rebuilt the reproducer myself at dfb03fc (origin/ccr-6d6c0ee6-dqm4lm-188), building the test project in Release with 0 warnings. The probe is W/r13wip/verify-naming-updates-stream-summary-oldest-always-in-reach/ZzVerifyReachSummary.cs.txt and its output is out-head.txt in the same folder. It calls the committed builder, computes the window start as max(0, place - NearbySearchRadius), and opens the document. Output:
- prevFrom=-7, header '13 0 obj': oldest=229 stream=418 place=411 windowStart=0 oldestDistance=182 oldestWithinReach=True; the sections are 'startxref Read Table 1147', '/XRefStm Read Stream 418', '/Prev Relocated Table 229'.
- prevFrom=7, header '+13 0 obj': oldestDistance=196 True; '/Prev Relocated Table 229'.
- prevFrom=30, header '++13 0 obj', into=1: oldestDistance=219 True; '/Prev Relocated Table 229'.
- prevFrom=513, header '++13 0 obj', catalog=False: place=931 windowStart=419 oldestDistance=702 oldestWithinReach=False; '/Prev NotFound Unknown 931', 'Warning xref.section-missing @931', 'Repair xref.rebuilt @-1'.
- prevFrom=514, header '++13 0 obj', catalog=True: windowStart=420 oldestDistance=703 False; '/Prev NotFound Unknown 932', 'Warning xref.section-missing @932'.
- prevFrom=515, header '+++13 0 obj', into=1: windowStart=421 oldestDistance=704 False; '/Prev NotFound Unknown 933'.
- prevFrom=515 with streamPrev 'the oldest table': the table at 229 is read only through the stream's own /Prev ('/Prev Relocated Stream 419', then '/Prev Read Table 229'), not by the search.
By arithmetic as well: the oldest table spans 167 bytes from 229, and object 4 takes 22 more, so the stream sits at 418 and the distance is 189 + prevFrom. Rows 513 to 515 therefore exceed 512.

Refuter fix: Make the closing clause of the summary at HybridFiles.cs:1669-1671 conditional, as the sibling summary does. For example: '... A search near the place the /Prev names comes to the stream's header — at the digit past its signs, or at its first sign where the line its number is on, which an end of line ends before obj, holds a % —, and, where that place lies 7 bytes before the header or 7 or 30 bytes past it, to the oldest table farther off, within reach; 513 to 515 bytes past the header, the oldest table lies out of reach.' This is a text-only change, with no maintainer decision needed. The reviewer's extra clause about where the bytes searched start is optional: the three theories' comments already say it.

## 15. gcv-blank-line-after-brace — nit → confirmed nit (tests)
Location: tests/AdCodicem.Pdf.Tests/GuardCutValueTests.cs:4651

Claim: In What_the_chain_read_through_a_guard_s_cut_before_the_look_at_a_prev_s_place_is_loaded_once_the_chain_is_read_whatever_that_look_forgets, a blank line follows the opening brace of the using block. No other test in the file has one there. dotnet format does not flag it.

Evidence: Lines 4649-4652 at dfb03fc: '        using (var document = PdfDocument.Open(file, ObjectGuard))' / '        {' / '' / '            document.Reader.Structure.Sections.Select(...'.

Fix: Remove the blank line.

Refuter: verdict confirmed, severity nit, needs_maintainer False
Reason: The finding holds as stated. At dfb03fc, line 4651 of tests/AdCodicem.Pdf.Tests/GuardCutValueTests.cs is a blank line directly after the opening brace of the `using (var document = PdfDocument.Open(file, ObjectGuard))` block. It sits in What_the_chain_read_through_a_guard_s_cut_before_the_look_at_a_prev_s_place_is_loaded_once_the_chain_is_read_whatever_that_look_forgets. Round 12's test commit a3f865a ('test: cover a comment line past the search, a parted header a guard cuts and an earlier read-through') added it, inside the range under review (1cd2216..dadde18). This is the only such blank line anywhere: I scanned every .cs file under tests/ and src/, not just this one file, so the claim is in fact understated. No style rule in the build catches it. The repository has no StyleCop (SA1505), and .editorconfig and the Directory.Build/Packages props set no IDE2000-series rule. That explains why the build and dotnet format let it through. No earlier finding (r1 to r12) or decision covers it. It does not affect behavior, so the severity stays nit (wording and style).

Refuter evidence: On origin/ccr-6d6c0ee6-dqm4lm-188 at dfb03fc, `sed -n 4649,4652p | cat -A` prints: '        using (var document = PdfDocument.Open(file, ObjectGuard))$' / '        {$' / '$' / '            document.Reader.Structure.Sections.Select(section => (section.NamedBy, section.State, section.Offset)).Should().Equal($'. `git blame -L 4649,4652` attributes all four lines to a3f865a3 (2026-11-10). A Python scan of every tests/**/*.cs and src/**/*.cs for a line that is only '{' followed by a whitespace-only line prints one hit: 'tests\AdCodicem.Pdf.Tests\GuardCutValueTests.cs 4650' (the brace line, so the blank line is 4651). grep for stylecop, SA1505, IDE2002 and IDE2000 in .editorconfig, Directory.Build.props, Directory.Packages.props and tests/Directory.Build.props finds nothing. The worktree's isolation guard refused writing the scan into W/r13wip/verify-gcv-blank-line-after-brace/, so it was run inline only.

Refuter fix: Delete the blank line at tests/AdCodicem.Pdf.Tests/GuardCutValueTests.cs:4651 so the body starts directly after '{', as in every other block of the codebase. Fold it into the final minors-and-nits pass as a `test:` commit (or with that pass's other test edits). No maintainer decision needed.

## 16. tm-signed-run-before-window-or-minus-with-percent-not-found — minor → narrowed minor (docs)
Location: docs/threat-model.md:449 (the #197 clause 'and so does one whose first sign lies before the bytes searched, whose header, found at its digit, is read there all the same and taken where it reads') and docs/threat-model.md:526-528 (#327 Known gaps: 'A run that starts with a `-`, or whose first sign lies before the bytes searched, is asked about at the digit alone ... one that cannot be read is dropped there'; the entry also opens with '`-4 0 obj` ... is taken ... where it reads there')

Claim: These texts were written this round (dadde18, a7bcdc6, dfb03fc). They say a header is found at its digit, read there and taken where it reads when its run of signs starts before the bytes searched or starts with a '-'. That holds only when no % is on the line of its number. When that line holds a %, the walk back from obj lexes the line. The token then starts before the bytes searched (the #171 left edge), or it reads a negative number. Either way the place is no candidate: it is never found, never read and never taken, at the digit or anywhere else. A readable stream there is not taken: the section is reported missing, the index is rebuilt as the document opens, and the validator gives Error xref.section-not-found. Its twin without the % is taken. main reads the % files the same way, so this is a misleading text, not a regression.

Evidence: Generator W/r13wip/docs/edge/gen.py (W = C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud). It writes stream 11, which nothing names, near a /Prev offset that holds nothing; stream 11's own /Prev names the oldest table. Probe: W/r13wip/docs/ZzTrackProbe.cs.txt, run by W/r13wip/docs/tools/run.py. Outputs: W/r13wip/docs/edge/out-head.txt (dfb03fc) and out-main.txt (21a0298).

Run start before the bytes searched, at dfb03fc:
- edge-plus-513.pdf ('+11 0 obj', its '+' the byte before the bytes searched): `S /Prev 2114 -> 1602 Stream Relocated` and `Repair xref.offset-adjusted at 1602: The cross-reference section /Prev names at offset 2114 was found 512 bytes before it.`, nothing rebuilt.
- edge-plus-513-pct.pdf ('+11 0 %c', then 'obj'): `S /Prev 2114 -> 2114 Unknown NotFound`, `Warning xref.section-missing at 2114: The cross-reference section /Prev names at offset 2114 is not there, nor near it; ...`, `Repair xref.rebuilt`, `Error xref.section-not-found at offset 2114`.
- edge-plusplus-513 and edge-plusplus-514 read like these two (taken at 1603 without the %; NotFound with the %).

Run a '-' starts, within reach, at dfb03fc:
- edge-minus-300.pdf ('-11 0 obj'): `S /Prev 1901 -> 1602 Stream Relocated`, found 299 bytes before it.
- edge-minus-300-pct.pdf: `S /Prev 1901 -> 1901 Unknown NotFound`, section-missing, rebuilt, `Error xref.section-not-found at offset 1901`.
- edge-minusplus-300(-pct) gives the same split.

Lost twins: dropped either way, with section-missing.

main gives the same NotFound/section-missing/rebuild for every % file. Its twins are taken there too ('was found -512 bytes from there', '-299 bytes from there'). r13a-result.json says the same of its far rows: 'the % header is not found at all, its token starting before the bytes searched'.

Fix: In threat-model.md:449, after 'whose header, found at its digit, is read there all the same and taken where it reads', add: '— where the line its number is on holds a `%`, the walk lexes that line, and such a header, whose token starts before the bytes searched, is no place at all, as a header the window's start cuts ([#171]) —'.

In the #327 gap, after 'is asked about at the digit alone', add: 'where the walk finds it there: where the line its number is on holds a `%`, the walk lexes that line, and such a run, which starts before the bytes searched or reads a negative number, is no place, a readable section there not taken'.

Optionally, qualify the entry's '`-4 0 obj` ... is taken' with 'where no `%` is on its line'. Text only.

Refuter: verdict narrowed, severity minor, needs_maintainer False
Reason: The finding holds for a '+' run whose first sign lies before the bytes searched. Both texts were written in dadde18, inside this round's range, and neither covers the case where the number's line holds a %.

How the code behaves: TryReadHeaderBackwards lexes a line that holds a % from the start of the span (TakeLine). The first token is then the lead byte, the one byte the window holds before the bytes searched, so headerStart is 0, below 'from' (1). FindSectionCandidates drops it. That is the row's own #171 rule: 'a header whose number the window's start cuts is no candidate'.

What line 449 says instead: 'one whose first sign lies before the bytes searched, whose header, found at its digit, is read there all the same and taken where it reads, a guard reached in reading it standing'. With the % none of that happens: the header is not found, not read, not taken, and no guard is reached. A readable section there is reported missing, the index is rebuilt and the validator gives an Error.

The decision behind the text (decisions.md, round 12 [18]) and r13a-result.json note this ('the % header is not found at all'), but no page says it. Nothing accepts the text.

Narrowed on the '-' half. At 449, 'a run a - starts declares nothing' still holds with a %, and 'found at its digit' attaches grammatically to the before-window run. In the #327 gap, '-4 0 obj ... is taken' is an exact example with no %, so it is true as written. 'Is asked about at the digit alone ... one that cannot be read is dropped there' is loose for a '-' run on a % line: such a run is never tried, because the walk lexes it as a negative number and finds no header (#327 comment 6776569954 says exactly that). The outcome is the same for an unreadable stream. A readable one is simply not taken, which is the right behavior: it falls outside the defect the gap describes. That half is a nit inside the same fix.

Not a regression: main's walk does not cross comments at all, so it finds none of the % headers. The behavior has no defect; only the text is wrong. Severity: minor, a misleading text.

Refuter evidence: My own reproducer: generator W/r13wip/verify-tm-signed-run-before-window-or-minus-with-percent-not-found/gen.py (`python -I gen.py files`, 48 files). Probe: ZzVerifyTmSignedPct.cs.txt in the same folder. Output: out-head.txt there, at dfb03fc, Release build in my worktree, each file read twice with identical results.

Layout of each file: cross-reference stream 11, which nothing names, its header at 1510. The newest table's /Prev names a comment of dashes 200 or 512 to 515 bytes past that header. Stream 11's own /Prev names the oldest table at 226, out of reach. Each file comes in three modes: ok, foo (an unsupported filter, so the stream cannot be read) and pad (__t100, a dictionary MaxTrailerLength 100 cuts).

'+' run, first sign before the bytes searched:
- a-p1-k513-ok ('+11 0 obj', the '+' is the byte before the bytes searched): `S /Prev 2023 -> 1511 Stream Relocated`, then 226 Read, `Repair xref.offset-adjusted at 1511 ... found 512 bytes before it`, open repaired=False.
- a-p1-k513-pct-ok ('+11 0 %c' then 'obj'): `S /Prev 2023 -> 2023 Unknown NotFound`, `Warning xref.section-missing at 2023`, `Repair xref.rebuilt`, open repaired=True, `F Error xref.section-not-found at offset 2023`.
- a-p1-k513-pctgen-ok ('+11 %c' then '0 obj'): the same NotFound, rebuild and Error.
- a-p3-k514 and a-p3-k515 ('+++11', two or three signs before the bytes searched): taken at 1513 without the %; NotFound and Error with it.
- Guard rows: a-p1-k513-pad gives `Warning limit.trailer at 1511` before section-missing; a-p1-k513-pct-pad gives no limit.trailer at the stream, only the one at 2023. So 'a guard reached in reading it standing' does not hold with the %.
- Control a-p1-k513-pctown ('+11 0', then '%c' on its own line, then 'obj'): taken at 1511. The % must be on the number's line.
- Controls b-p1-k512-pct and b-u-k512-pct (first sign the first byte searched): taken at 1510.

'-' run, in reach (k200):
- c-m-k200-ok ('-11 0 obj'): Relocated at 1511 (#327's gap).
- c-m-k200-pct-ok: NotFound, section-missing, rebuilt, `Error xref.section-not-found at offset 1710`, object 11 null.
- c-mp-k200 ('-+11') splits the same way.
- c-pm-k200-pct ('+-11 0 %c'): taken at its sign, 1510.
- Unreadable (foo) rows: NotFound and section-missing whether or not the % is there.

main 21a0298: its TryReadHeaderBackwards (git show 21a0298:src/AdCodicem.Pdf/IO/PdfFileReader.cs:6294) walks digits and white space only, so it fails at the 'c' of any comment. The reviewer's W/r13wip/docs/edge/out-main.txt shows every % file NotFound on main, including the within-window control edge-plus-512-pct.

Code at dfb03fc: PdfFileReader.cs:9363 TryReadHeaderBackwards, whose TakeLine lexes from lineStart = 0 when the span holds no end of line before the number, and FindSectionCandidates at 5705 ('headerStart >= from').

Both texts come from dadde18 (`git log -S`).

Refuter fix: Text only, no code change.

1. docs/threat-model.md:449. Replace '... and so does one whose first sign lies before the bytes searched, whose header, found at its digit, is read there all the same and taken where it reads, a guard reached in reading it standing ([#197], [#327])' with: '... and so does one whose first sign lies before the bytes searched, whose header, found at its digit, is read there all the same and taken where it reads, a guard reached in reading it standing — but where the line its number is on, which an end of line ends before `obj`, holds a `%`: the walk back from `obj` then lexes that line, and finds no place, its token a negative number or starting before the bytes searched, as a header whose number the window's start cuts, a section there not taken ([#171]) — ([#197], [#327])'.

2. docs/threat-model.md:526-528, the #327 gap. After 'is asked about at the digit alone', insert: ', where the walk finds it there — where the line its number is on holds a `%`, the walk lexes that line and finds no header, a run a `-` starts reading as a negative number, one whose first sign lies before the bytes searched starting before them —'.

3. Optional, same omission but no effect on the outcome. The 'Nor is a header ...' clause at 449 and the comment above the reached test (PdfFileReader.cs:5289-5292) both say 'or at the run's first sign where the line its number is on holds a `%`'. Narrow that to a run that starts with a `+` within the bytes searched: on such a line, a '-' run or one starting before the window is found nowhere, so it is not taken again either way.

No design choice is involved.

## 17. commit-69731a2-body-three-rows — nit → confirmed nit (docs)
Location: commit 69731a2 (docs: say any cut a trailer guard makes where the chain names withholds streams passed over), body paragraph 2

Claim: The body says the threat model's 'entry for the tokens a cut parts and its three rows, whose evidence now cites the theory of a cut header' were changed. The diff changes the #279 entry and two rows: the /XRefStm row (now :447) and the trailer row (now :454). The missing-section row is untouched, as R12h's own deviation says. This goes in the status.md record of false bodies. Every other body in 1cd2216..dfb03fc checked true of its diff, apart from 5880c0b, 305c1fb and 78885bf, which are already accepted as stale.

Evidence: `git diff -U0 69731a2~1 69731a2 -- docs/threat-model.md` gives three hunks: `@@ -426,7 +426,9 @@` (the #279 entry), `@@ -445 +447 @@` and `@@ -452 +454 @@`. The header-cut theory is added twice ('^+' count 2). check_msgs.py over the 28 commits gives every commit OK: subjects 71 to 100 characters, footer 'Refs #n[, #m]', a blank line, then Co-Authored-By.

Fix: Record in docs/status.md that 69731a2's body says three threat-model rows changed, where it changed two (the /XRefStm and trailer rows). Pushed bodies stay as they are.

Refuter: verdict confirmed, severity nit, needs_maintainer False
Reason: The finding holds. 69731a2's body ends its second paragraph with "So do the pages: ... and the threat model's entry for the tokens a cut parts and its three rows, whose evidence now cites the theory of a cut header." The commit changes the #279 Known gaps entry and only two table rows: the /XRefStm row (a7bcdc6:445, now :447) and the trailer row (:452, now :454). Only those two evidence columns cite the new header-cut theory. The missing-section row ("A missing section tried at many places", a7bcdc6:447, now :449) is untouched, and its evidence does not cite the theory. R12h's own deviation in r12-fix2-result.json says the same: "of the three rows near :445, :447 and :452 at a7bcdc6, I changed the /XRefStm row (now :447) and the trailer row (now :454) ... The missing-section row (now :449) ... is unchanged." So the body probably reused the count from the stage's brief. None of these record it: decisions.md (its 'Commit bodies not rewritten' line for the follow-up lists only 5880c0b, 305c1fb and 78885bf), r12-fix2-result.json's open points, and R12i. Precedent from earlier rounds (r8 [28] commit-bodies-record-092b772-253f179, r9 [28] commit-d8e67de-body): a pushed body that says more than its diff is a confirmed nit, recorded under status.md's 'Commit bodies not rewritten'. 9e20161's and 28d6080's bodies correctly say "its two citations" and "its citations". This verdict covers only 69731a2's sentence. I did not re-check the finding's side claim that every other body in 1cd2216..dfb03fc is true of its diff.

Refuter evidence: In the worktree at origin/ccr-6d6c0ee6-dqm4lm-188 = dfb03fc:
- `git diff -U0 69731a2~1 69731a2 -- docs/threat-model.md | grep '^@@'` gives exactly three hunks: `@@ -426,7 +426,9 @@` (the #279 entry), `@@ -445 +447 @@` and `@@ -452 +454 @@`.
- `git show 69731a2:docs/threat-model.md | grep -n -o 'reported_nowhere_where_a_trailer_guard_cut_the_header_of_an_object_at_an_offset_the_chain_names'` gives lines 447 and 454 only. The same holds at dfb03fc.
- The rows at 69731a2: 447 is "An `/XRefStm` that names no cross-reference stream of its table's revision ...", 449 is "A missing section tried at many places ..." (no hunk, no citation of the theory), and 454 is "A trailer, or a cross-reference stream's dictionary, of any length ...".
- The body of 69731a2 (`git log -1 --format=%B 69731a2`) reads "... and the threat model's entry for the tokens a cut parts and its three rows, whose evidence now cites the theory of a cut header."
- R12h's deviation in r12-fix2-result.json confirms that only two rows changed. decisions.md:1082 records only 5880c0b, 305c1fb and 78885bf as the follow-up's bodies not rewritten.
Saved in C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r13wip/verify-commit-69731a2-body-three-rows/hunks.txt and rows-69731a2.txt.

Refuter fix: Do not rewrite the pushed commit. In the final pass, add 69731a2 to the batch's 'Commit bodies not rewritten' list (decisions.md, beside 5880c0b, 305c1fb and 78885bf). When docs/status.md's batch 19 entry is written, add a line saying that 69731a2's body names "the threat model's entry ... and its three rows" when it changed the entry and two rows, the /XRefStm row (:447) and the trailer row (:454). Only those two rows cite the header-cut theory. The missing-section row (:449) is unchanged. No code or page change is needed.

## 18. overlong-lines-in-pages — nit → confirmed nit (docs)
Location: docs/threat-model.md:434; docs/website/docs/concepts/lazy-reading.md:244; docs/website/docs/reference/reader-limits.md:267

Claim: The round's rewraps left three prose lines of 151, 149 and 149 columns in paragraphs that wrap at 120.

Evidence: W/r13wip/docs/tools/longlines.py 1cd2216 dfb03fc prints:
- `docs/threat-model.md 434 151   reported anywhere in the document (`xref.stream-passed-over`). A `/Type` that is no name`
- `docs/website/docs/concepts/lazy-reading.md 244 149 That search may take a section other than the one past them, ...`
- `docs/website/docs/reference/reader-limits.md 267 149   type that gives no `/W`, or a token no header starts, among them. A dictionary whose `/T`

The threat model's line 434 was 120 wide at dadde18; 69731a2 left it long.

Fix: Rewrap the three paragraphs at 120 columns.

Refuter: verdict confirmed, severity nit, needs_maintainer False
Reason: The finding holds. The commits under review add exactly these three prose lines over 120 characters to docs/, and nothing else over that width. Each one sits in a paragraph whose other lines are all 120 or less. The rendered pages are unaffected: Markdown ignores where a paragraph's source lines break. No rule in the repository sets a width. .editorconfig's [*.md] section sets only trim_trailing_whitespace, and there is no markdownlint or prettier config. All three pages already carry older prose lines over 120 (threat-model.md has 28, lazy-reading.md and reader-limits.md have 12 each). Even so, the batch has a settled precedent. Round 11 [8] (unwrapped-prose-lines-220eab4) was the same kind of nit in text the batch added, it was narrowed but kept as a nit, and the R11 fix stage rewrapped it (decisions.md:870 '[8] rewrap two lines'). The same applies here. One detail in the evidence is wrong: at dadde18 the threat model's line (then :432) was 117 characters wide, not 120. It was still under the limit, and 69731a2 pushed it to 151, so the claim stands.

Refuter evidence: Probes are under C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r13wip/verify-overlong-lines-in-pages/:
- addedlong.py: lines a diff adds under docs/ that run over 120 characters, counting both characters and UTF-8 bytes.
- ctx.py: widths of the lines around a given line.
- scan.py: prose lines over 120 across docs/.
- rewrap.py: a 120-column rewrap of a paragraph's tail.
- tm-dadde18.md: threat-model.md as it was at dadde18.

`addedlong.py <wt> 1cd2216 dfb03fc` on dfb03fc prints:
- `docs/threat-model.md:434: 151 chars, 151 bytes`
- `docs/website/docs/concepts/lazy-reading.md:244: 149 chars, 149 bytes`
- `docs/website/docs/reference/reader-limits.md:267: 149 chars, 149 bytes`

Splitting the range shows where each line came from:
- 1cd2216..dadde18 gives `lazy-reading.md:240: 149 chars`.
- dadde18..69731a2 gives `threat-model.md:434: 151` and `reader-limits.md:267: 149`.

git blame agrees: threat-model.md:434 is 69731a21, lazy-reading.md:244 is dadde188, and reader-limits.md:267 is 69731a21.

The other lines of each paragraph (ctx.py):
- threat-model.md:426-436 run 101-120; only :434 is 151.
- lazy-reading.md:236-252 run 80-120; only :244 is 149.
- reader-limits.md:259-269 run 50-118; only :267 is 149.

At dadde18 (tm-dadde18.md), the threat model's line was :432 at 117 characters. 69731a2 put 'reported anywhere in the document ' in front of it.

scan.py at dfb03fc counts older prose lines over 120 in the same pages: threat-model.md 28, lazy-reading.md 12, reader-limits.md 12.

None of the three lines contains a multibyte character, so their character and byte counts are equal.

Refuter fix: Rewrap the three paragraph tails at 120 columns. Change only where the lines break, not the words. rewrap.py gives these.

docs/threat-model.md:434-436 becomes:
```
  reported anywhere in the document (`xref.stream-passed-over`). A `/Type` that is no name says nothing of what the
  dictionary is: cut before its `/W`, such a dictionary is the guard's, as one cut before its `/Type` is, nothing
  reported missing. Reading every such dictionary as the guard's, as one the guard cut before its `/Type` is read,
  closes this part of it.
```
The widths are 115, 113, 114 and 25.

docs/website/docs/reference/reader-limits.md:267-269 becomes:
```
  type that gives no `/W`, or a token no header starts, among them. A dictionary whose `/Type` is no name, which says
  nothing of what it is, declares a section only through a `/W`: cut before it, it is a section the guard cut, as one
  cut before its `/Type` is, the chain ending there and nothing reported missing.
```

docs/website/docs/concepts/lazy-reading.md:244-246 becomes:
```
That search may take a section other than the one past them, which the reader cannot tell without reading past the
guard: once that guard is reached, nothing is said of a stream passed over even where the search finds the section whole
past the white space, which only a `MaxTrailerLength` under the 512 bytes the search reaches allows. A cut that leaves
```
After that, refill the rest of the paragraph. It runs to :290 and never lines up with the old breaks. If the fix stage wants a smaller diff, it can instead end the rewrap with a short line 'nothing unread — data past a' before the existing :247.

This changes no rendered text, needs no test or mutant, and needs no maintainer decision. It can go in the final docs pass, or be dropped since no rule enforces a width.

## 19. limit-trailer-row-guard-cut-no-section-either — nit → confirmed minor (docs)
Location: docs/website/docs/reference/diagnostics.md:142

Claim: 69731a2 added a sentence to row 142: 'Where the cut falls before any object is read there — the object's number or its generation running on past the bound, a comment between its header's tokens, a token where no header starts —, the guard cut no section either'. That states as a fact what is only the reader's reading, which #289 leaves as is. The preceding CIDFont case is truly no section. Here, what the guard cut may be a cross-reference stream that a whole read takes as the section. The threat model's trailer row says it honestly: 'leaves no object read, the section looked for as one that is not there, as [#289] leaves it'.

Evidence: W/r13wip/docs/trk/t2-p289/out-head.txt (dfb03fc, the #289 follow-up comment's files), near-table-zeros.pdf. Stream 7 is /Type /XRef, its header written as 300 zeros then '7 0 obj'.
- Default limits: `S /Prev 203 -> 203 Stream Read`.
- Under MaxTrailerLength 200: `S /Prev 203 -> 203 Unknown NotFound`, `Warning limit.trailer at 203: The dictionary of the object read as a cross-reference stream runs past 200 bytes; only what lies within it was read. ...`, `Warning xref.section-missing at 203`, `Repair xref.rebuilt`, `Error xref.section-not-found at offset 203`.

Fix: Replace 'the guard cut no section either' with 'the reading takes the cut for no section's either, as [#289] leaves it'.

Refuter: verdict confirmed, severity minor, needs_maintainer False
Reason: The finding holds. 69731a2 added this sentence to the limit.trailer row (docs/website/docs/reference/diagnostics.md:142): "Where the cut falls before any object is read there — the object's number or its generation running on past the bound, a comment between its header's tokens, a token where no header starts —, the guard cut no section either". For a file I rebuilt on dfb03fc, the guard's cut falls inside the header of the very cross-reference stream that a whole read takes as the section. So the sentence states as fact something false.

The same row contradicts it a few clauses later: among what "the cut may have hidden" it lists "the whole dictionary past a header the cut parted". Every sibling text from the same commit gives this as the reader's reading, not as a fact: the threat model's trailer row (docs/threat-model.md:430-432, "which leaves no object read, the section looked for as one that is not there, as [#289] leaves it"), reference/reader-limits.md:68 and :257-258 ("no object read"), and the XML doc at PdfFileReader.cs:3776-3779 ("as #289 leaves it"). R12-Q4 settled that "section-missing there stays #279 / #289's; texts and pages say it". This row cites #289 but calls the cut "no section", which hides that the reading is a known debt and not the file's fault.

The behavior the row describes is exact: reported as the dictionary of the object read as a cross-reference stream, looked for nearby as one not there, rebuilt. That behavior is accepted (R12-Q4, #289's follow-up comment 6774538819 holds this very shape), so only the text is at fault.

Severity: the reviewer chose nit. By the brief's scale this is minor: a reference row that states a falsehood about what the guard cut, contradicted within the same row, is a misleading text, not wording. Rounds 11 and 12 rated "a sentence false in one case" nit, so either rating changes nothing under the amended stopping rule: it is a text fix for the final pass.

One aside of the finding is too strong: "The preceding CIDFont case is truly no section". It is so for a CIDFont dictionary read whole. But the row itself lists "a stream of another /Type that gives a /W" among what a cut may hide (#263), so main's CIDFont sentence is the reader's reading too. It is main's text, not this batch's, and the finding does not rest on it. The batch did not make this worse than main: the sentence is new text, with no counterpart on main.

Refuter evidence: Rebuilt on origin/ccr-6d6c0ee6-dqm4lm-188 at dfb03fc: Release build, 0 warnings. The probe is the docs lens's ZzTrackProbe, renamed and saved as C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r13wip/verify-limit-trailer-row-guard-cut-no-section-either/ZzVerifyLtr.cs.txt, run with ZZ_TRK_DIR pointing at the files/ folder beside it, then removed from the worktree. Output: out-dfb03fc.txt in that folder.

The file is near-table-zeros.pdf, copied from W/r13wip/docs/trk/t2-p289 and generated by #289's follow-up comment's gen.py. At offset 203 it has 300 '0' bytes, then '7 0 obj' at 503, then '<< /Type /XRef /Size 12 /W [1 4 2] /Index [1 3] /Prev 0000003246 /Length 21 >> stream'.

- Default limits: 'S /Prev 203 -> 203 Stream Read', then 'S /Prev 3246 -> 3246 Stream Read'. No rebuild. Object 7=null.
- MaxTrailerLength 200 (and 160):
  - section: 'S /Prev 203 -> 203 Unknown NotFound', repaired=True, stopped=False;
  - 'D Warning limit.trailer at 203: The dictionary of the object read as a cross-reference stream runs past 200 bytes; only what lies within it was read. Raise PdfReaderLimits.MaxTrailerLength to read past it.';
  - 'D Warning xref.section-missing at 203: ... is not there, nor near it; ...';
  - 'D Repair xref.rebuilt';
  - 'F Error xref.section-not-found at offset 203: ... the offset holds neither the xref keyword nor an object.'
- MaxTrailerLength 320 (header read, dictionary cut): 'S /Prev 203 -> 203 Unknown Malformed cut', stopped=True, 'Information xref.checked-in-part at offset 203'. Here the reader reads it as the guard's section.

So under 200 the guard cut the header of the section a whole read takes, and the row calls that "no section". The same row says, among what the cut may have hidden: "the whole dictionary past a header the cut parted". The sibling texts say "as [#289] leaves it" (threat-model.md:430-432; PdfFileReader.cs:3778-3779) or "no object read" (reader-limits.md:68, :257-258). The sentence came in with 69731a2's diff of the row; main has no such sentence. 'grep -rn "cut no section"' in docs/ and src/ finds main's CIDFont phrase (reader-limits.md:201, PdfFileReader.cs:6533, :6732) and this row only.

Refuter fix: Text only, in the final pass; no maintainer decision is needed. In diagnostics.md:142, replace "the guard cut no section either:" with wording that matches the threat model, for example: "no object is read there, as [#289](https://github.com/AdCodicem/AdCodicem.Pdf/issues/289) leaves it, though what the cut parted may be a section's header:". Then drop the "([#289](...))" that now ends the sentence, or keep it. The reviewer's "the reading takes the cut for no section's either, as [#289] leaves it" works too. Either way, the row then agrees with its own later clause ("the whole dictionary past a header the cut parted") and with threat-model.md:430-432. Optionally, write main's CIDFont sentence just before it as the reader's reading too ("the reading takes the cut for no section's"), since #263 lets a stream of another /Type that gives a /W past the cut be the section. That sentence is main's, so leaving it is acceptable.

## 20. tm-279-gap-closing-sentence-every-such-dictionary — nit → narrowed nit (docs)
Location: docs/threat-model.md:435-436

Claim: The #279 gap ends with 'Reading every such dictionary as the guard's, as one the guard cut before its `/Type` is read, closes this part of it.' It now comes right after 'A `/Type` that is no name ...: cut before its `/W`, such a dictionary is the guard's'. So 'every such dictionary' reads as the no-name dictionaries, which already are the guard's, and the closing sentence says nothing. The intended referent is every dictionary the guard cut at an offset the chain names, whatever its /Type. Since R12-Q4 the entry also lists header cuts, and no dictionary rule closes those.

Evidence: `sed -n '418,436p' docs/threat-model.md` at dfb03fc shows the two sentences in that order, the second at lines 435-436. At 1cd2216 the closing sentence ('Reading such a dictionary as the guard's ...') followed the parted-/Type sentence directly.

Fix: Write: 'Reading as the guard's every dictionary the guard cut at an offset the chain names, whatever its `/Type`, as one cut before its `/Type` is read, closes this part of it; the header cuts close with [#289].'

Refuter: verdict narrowed, severity nit, needs_maintainer False
Reason: The wording defect is real. The fix as proposed would misstate #279's planned closing, so it has to be narrowed.

What holds:
- At dfb03fc, docs/threat-model.md:434-436 reads: 'A `/Type` that is no name ...: cut before its `/W`, such a dictionary is the guard's, ... Reading every such dictionary as the guard's, as one the guard cut before its `/Type` is read, closes this part of it.'
- The nearest antecedent of 'every such dictionary' is the dictionary whose /Type is no name. The code already reads that dictionary as the guard's: PdfFileReader.cs:6694, `!candidate && cutByGuard && value is PdfDictionary { } cut && cut.GetRaw(PdfName.Type) is not PdfName`.
- Read that way, the closing sentence names as the gap's fix something already done. The open part is a /Type the cut parted, which still reads as another type, with section-missing, a rebuild and Error xref.section-not-found.
- The order came from dadde18 (round 12's docs commit). That commit inserted the no-name sentence and turned 'Reading such a dictionary' into 'Reading every such dictionary'. At 1cd2216 the closing followed the sentence on a parted name `XRef` begins with, and the referent was clear. No earlier round reviewed this text: round 12 covered 79c6a7f..1cd2216.

Why it stays a nit:
- The code is right. The entry's opening sentences state the open consequence plainly, and 'this part of it' can still be recovered from them. The defect is an antecedent ambiguity, not a false statement about behavior.

What is narrowed:
1. The finding gives the intended referent as 'every dictionary the guard cut at an offset the chain names, whatever its /Type'. #279's tracking comments contradict that. Comment 6762402379 lists the closing's rows. Comment 6769967146 says 'this issue's closing does not touch a name the cut leaves whole'. Its examples are near-table-catalog.pdf under 150 and near-table-x.pdf under 148, which keep their reading and still need the flag. Comment 6774542497 says the flag stays until the closings of both #279 and #289 settle what the guard cut. So the proposed wording would wrongly promise that a /Catalog or an /X written whole and cut would become the guard's section once #279 closes.
2. On header cuts, the entry already says 'the section looked for as one that is not there, as [#289] leaves it'. A pointer to #289 in the closing sentence is a clarification, not a missing statement.

Nothing in #171, #279, #289, #327 or #332, in decisions.md or in r12-findings.md covers this sentence. It is not one of the accepted consequences.

Refuter evidence: The #279 Known-gaps entry was extracted at each commit into C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r13wip/verify-tm-279-gap-closing-sentence-every-such-dictionary/ (gap279-tm-main.txt, gap279-tm-1cd.txt, gap279-tm-dad.txt, gap279-tm-697.txt, gap279-dfb03fc.txt).

- main 21a0298: the entry has only the parted-token sentence, with no closing clause.
- 1cd2216: '... the `/Prev` past the cut may be a link the guard hid, and no stream passed over is reported anywhere in the document (`xref.stream-passed-over`). Reading such a dictionary as the guard's, as one the guard cut before its `/Type` is read, closes this part of it.'
- dadde18, unchanged at 69731a2 and dfb03fc (lines 434-436): '... A `/Type` that is no name says nothing of what the dictionary is: cut before its `/W`, such a dictionary is the guard's, as one cut before its `/Type` is, nothing reported missing. Reading every such dictionary as the guard's, as one the guard cut before its `/Type` is read, closes this part of it.'
- `git log -S'every such' -- docs/threat-model.md` gives dadde18 only. The same holds for `-S'such a dictionary is the guard'`.
- In the code, PdfFileReader.cs:6694 makes a cut dictionary whose /Type is no name the guard's section, so the near reading names an existing behavior.
- On the planned closing: #279 comment 6769967146 says 'this issue's closing does not touch a name the cut leaves whole: near-table-catalog.pdf under 150 and near-table-x.pdf under 148, which give no Warning at dadde18, still need it'.

No runtime probe was needed: this is a text-only finding, and no behavior is claimed.

Refuter fix: Text only, in docs/threat-model.md:435-436. Name the referent instead of 'every such dictionary', and keep it to what #279's closing reads as the guard's.

Suggested wording: 'Reading as the guard's a dictionary whose `/Type` the cut parted, inside the name or inside a `#xx` escape, as one the guard cut before its `/Type` is read, closes this part of it; what a cut leaves of an object's header is [#289]'s.'

Alternatively, move the no-name sentence ahead of the 'Wherever a guard cut ...' sentence, and restore the closing as 'Reading such a dictionary ...' right after the parted-/Type sentence.

Do not use the reviewer's 'every dictionary the guard cut at an offset the chain names, whatever its /Type'. #279's comments say its closing leaves a name written whole (a /Catalog, an /X) as the settled rule reads it, and the flag stays until #279's and #289's closings settle what the guard cut.

This needs no maintainer decision: it can go in the final pass.

## 21. concepts-reader-limits-garbled-clause-and-cut-lists — nit → narrowed nit (docs)
Location: docs/website/docs/concepts/reader-limits.md:103; docs/website/docs/reference/reader-limits.md:21

Claim: The concepts sentence 'the cut may have hidden the `/Prev` of a section, which any `/W` makes the dictionary there' is a garbled relative clause. It also drops the condition that a `/W` makes the section only where a `/Prev` or `startxref` names it. The concepts list of what the cut may cut, and the MaxTrailerLength row's list, leave out 'a token where no header starts'. The other pages and the XML doc of PdfDiagnosticCodes.XRefStreamPassedOver list it.

Evidence: `sed -n '97,107p' docs/website/docs/concepts/reader-limits.md` and line 21 of reference/reader-limits.md at dfb03fc. diagnostics.md:116 and :142, reader-limits.md:65-74 and :257-267, lazy-reading.md:240-242 and threat-model.md:430, :447 and :454 all name 'a token where no header starts'.

Fix: Concepts: 'the cut may have hidden the `/Prev` of a section — where a `/Prev` or `startxref` names the offset, any `/W` makes the dictionary there one —'. Add ', or a token where no header starts' to both lists.

Refuter: verdict narrowed, severity nit, needs_maintainer False
Reason: Only the list part holds, and it is a nit. The concepts list (docs/website/docs/concepts/reader-limits.md:99-101) and the MaxTrailerLength row (docs/website/docs/reference/reader-limits.md:21) of what the cut may cut both leave out 'a token where no header starts'. The same is true of the main list in reference/reader-limits.md:63-67, which names the token only in its 'hid nothing' tail at :75. The code comment (PdfFileReader.cs:6666-6669), the XML doc (PdfDiagnosticCodes.cs:134), reader-limits.md:257 and threat-model.md:431 do name it in their main lists. Nothing is misstated, though. Both texts open the list with 'whatever it cuts', and the concepts sentence closes with 'or where the cut hid nothing', so the token case is covered. The list reads as examples, not as a closed set. The other two parts of the claim do not hold. (1) The clause is not garbled. In 'the /Prev of a section, which any /W makes the dictionary there', 'which' is the object complement of 'makes', i.e. any /W makes the dictionary there a section. That is grammatical, if dense, and matches the other pages' 'any /W makes a stream of any /Type the section'. (2) The text does not drop a condition, and the condition the finding proposes would be wrong. The clause sits in the first branch, set against 'or be what would end the chain, where an /XRefStm names the offset', so it already reads as the /Prev or startxref case. Also, 'any /W makes the dictionary there a section' holds at every offset the chain names, an /XRefStm's included, not only where a /Prev or startxref names it. The /XRefStm's reading (PdfFileReader.cs:3614) calls TryReadXRefSection with candidate left false. That reaches TryReadXRefStream (5828), whose test at 6692 is DeclaresCrossReferenceStream (7468-7470: /Type /XRef or any /W). Only a place the search tries (candidate) asks DeclaresOnlyCrossReferenceStream (7770-7772). The other pages tie the clause to /Prev or startxref because the hidden-/Prev concern belongs there, not because a /W makes the section only there. The text came in with dadde18 (git log -S) and 69731a2 kept it. No earlier round reviewed it, and no decision covers it.

Refuter evidence: At dfb03fc (origin/ccr-6d6c0ee6-dqm4lm-188), `sed -n '85,115p' docs/website/docs/concepts/reader-limits.md` gives the list 'whatever it cuts — white space and comments, ..., a dictionary, whatever its `/Type`, ..., or the object's header, its number or its generation running on past the guard or a comment between its tokens —', with no token case. The sentence ends 'even where the search finds the section whole past the white space, or where the cut hid nothing'. reference/reader-limits.md:21 reads 'whatever it cuts, that the reading does not take for a section it cut — a dictionary, whatever its `/Type`, the object read as one of another type, or the object's header, its number or its generation running on past it or a comment between its tokens, no object read —', with no token case either. `grep -rn "no header starts"` matches threat-model.md:431, lazy-reading.md:241 (a 'hid nothing' tail), diagnostics.md:116 and :142, reader-limits.md:75 (a tail) and :257/:267, PdfDiagnosticCodes.cs:134/:141, and PdfFileReader.cs:685/697/3778/6669/6681. The token case is real: r12-fix2-result.json reports it tested ('a token where no header starts (comment dashes read from inside the comment)'), and 9e20161 sets the flag at '!candidate && cutByGuard' (PdfFileReader.cs:6688). For the condition: PdfFileReader.cs:3614 reads the /XRefStm offset with `TryReadXRefSection(stream.Offset, stream, out _, out _)` (candidate false), line 5828 calls TryReadXRefStream with that candidate, and line 6692 applies `candidate ? DeclaresOnlyCrossReferenceStream(value) : DeclaresCrossReferenceStream(value)`. DeclaresCrossReferenceStream is `IsOfType(PdfName.XRef) || ContainsKey(PdfName.W)`. `git log -S"which any \`/W\` makes the dictionary there" 21a0298..dfb03fc` returns dadde18. No behavior probe was needed: the claim is about the text alone, so no files were written under r13wip/verify-concepts-reader-limits-garbled-clause-and-cut-lists/.

Refuter fix: Optional wording pass for the final minors-and-nits round. In concepts/reader-limits.md:101, after 'or a comment between its tokens', add ', or a token where no header starts'. In the MaxTrailerLength row of reference/reader-limits.md:21, after 'or a comment between its tokens', add ', or a token where no header starts'. Their shared ', no object read' then covers both. For consistency, reader-limits.md:66-67's main list can take the same addition. If the clause is to read more easily, write 'the cut may have hidden the `/Prev` of a section — any `/W` makes the dictionary there one —'. Do not add 'only where a /Prev or startxref names it': a /W makes the section at an /XRefStm's offset too (PdfFileReader.cs:3614 → 6692). This is not a decision for the maintainer.

## 22. reachedat-summary-states-removed-range — minor → confirmed minor (batch:)
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3899-3901 (ReachedAt summary)

Claim: ReachedAt's summary still states R12a's rule, which R12g removed. It says a relocation passes over 'a header found at its digit where one starts at a sign of the run its number is written with, that run lying within the bytes searched (#188, #327)'. Since 13e5021 (R12g) and a93e60a (R13a), the reached test at :5301-5304 works differently. It passes a place over where the digits past the place's own signs are those of a stream the chain reached (_sectionDigitsAt, DigitsPastSigns). That holds wherever the run starts, before the bytes searched included, and whatever sign starts it. It also holds for a header found at its first sign where the walk lexed a line that holds a %. The qualifier 'that run lying within the bytes searched' is now false.

Evidence: git blame dfb03fc: lines 3900-3901 come from 0578bcb (R12a docs). Neither a7bcdc6 (R12g docs) nor dfb03fc (R13a docs) touched them.

Reproducer: W/r13wip/batch/gen/f00305_n162.pdf, from tools/gen13.py with seed 202611102016, index 305. Stream 16, written '+16 0 obj', sits at 10160. s3's /XRefStm reads it at its sign. s2's /XRefStm names 10673, so the bytes searched start at 10161 (its digit) and the '+' is only the lead byte, outside them.
- dfb03fc (out-branch.txt, D): 'Warning xref.section-missing at 10673 ... is not there, nor near it', a rebuild, and 'Error xref.section-not-found at offset 10673'. The stream is passed over though its run does not lie within the bytes searched.
- 1cd2216 (out-prev.txt): 'Repair xref.offset-adjusted at 10161: ... /XRefStm names at offset 10673 was found 512 bytes before it'.

The branch's own theory A_cross_reference_stream_a_prev_reaches_at_its_header_or_a_sign_of_its_number_is_not_taken_again_however_few_of_its_signs_the_bytes_searched_near_its_own_prev_hold pins the same pass-over (via-plus-513).

Fix: Rewrite the clause in the same summary as the reached test now reads. For example: '…passes over those where a section the chain reached starts, and a header whose digits, past the place's own run of signs, start where those of a stream the chain reached start, as that stream's reading lexed them (_sectionDigitsAt, DigitsPastSigns), wherever the run starts and whatever sign starts it (#188, #327)'. Text only, so no ADR 53 run is owed.

Refuter: verdict confirmed, severity minor, needs_maintainer False
Reason: The finding holds. ReachedAt's summary (PdfFileReader.cs:3899-3901) still states R12a's rule: a relocation passes over a header found at its digit where a section starts at a sign of its run, "that run lying within the bytes searched". R12g (13e5021) removed the HeaderTokenStart range from the reached test, and R13a (a93e60a) changed how digits are looked up. The test at :5301-5304 now reads `_sectionsReadAt.Contains(candidate) || _sectionDigitsAt.Contains(DigitsPastSigns(window, start - lead, candidate))`. DigitsPastSigns (:5551-5555) has no window condition on where the run starts. So a place is passed over wherever its run of signs starts, before the bytes searched included, and whatever sign starts it ('-+5'). That includes a header found at its first sign where the walk lexed a line holding a %, which the summary does not mention at all. The window qualifier now belongs only to HeaderTokenStart (the line test and the record of a stream passed over), whose own summary at :5557-5564 says so correctly. Neither commit's docs (a7bcdc6, dfb03fc) touched these lines, and both results' lists of changed source texts omit ReachedAt. This is a misleading text in a private member's XML summary, with behavior correct and pinned by tests, so it is minor under the brief's scale. No earlier round reviewed this text: 0578bcb came in round 12's fixes and is first reviewed now.

Refuter evidence: git blame dfb03fc -L 3891,3902: lines 3900-3901 come from 0578bcb (R12a docs), lines 3891-3899 from c29761c. git log 1cd2216..dfb03fc on PdfFileReader.cs lists a7bcdc6 and dfb03fc as docs commits. r12-fix2-result.json lists a7bcdc6's changed source texts: the TryRelocateXRefSection remark, HeaderTokenStart, StartsWithin and XRefSectionRecord.DigitsAt. r13a-result.json lists dfb03fc's: _sectionDigitsAt, the TryRelocateXRefSection remark, HeaderTokenStart and DigitsAt. Neither names ReachedAt.

Reproduced on dfb03fc (Release build). The probe is W/r13wip/verify-reachedat-summary-states-removed-range/ZzVerifyReachedAtSummary.cs, run uncommitted in tests/AdCodicem.Pdf.Tests and removed afterwards, with output in out-branch.txt. It opens W/r13wip/batch/gen/f00305_n162.pdf, where '+16 0 obj' starts at 10160. s3's /XRefStm 10160 reads the stream at its sign. s2's /XRefStm 10673 makes the search read bytes from 10161, the digit, with the '+' only the window's lead byte. Output:
S Read Table 11783 / S Read Stream 11623 / S Relocated Table 10294 / S Read Stream 10160 / S Read Table 8193 / S NotFound Unknown 10673 / S Read Table 3913 / S Read Stream 1686 / S Read Stream 378
D Repair xref.offset-adjusted at 10294 ... found 33 bytes before it.
D Warning xref.section-missing at 10673: ... is not there, nor near it; ... rebuilding the index.
V xref.section-not-found 10673
So the header at 10161 is passed over though its run does not lie within the bytes searched.

In the same run, all 19 rows of CrossReferenceChainTests.A_cross_reference_stream_a_prev_reaches_at_its_header_or_a_sign_of_its_number_is_not_taken_again_however_few_of_its_signs_the_bytes_searched_near_its_own_prev_hold pass (20/20 with the probe). Rows ("+5 0 obj",0,513,...) and ("-+5 0 obj",1,513,...) pin the pass-over where the bytes searched start at the digit, or where a '-' starts the run. Its comment states 'wherever its run of signs starts and whatever sign starts it'. The % case, a header found at its first sign, is pinned by the theory at :889ff.

Refuter fix: Text only, so no ADR 53 run is owed. Rewrite the last clause of ReachedAt's summary (PdfFileReader.cs:3899-3901) to match the reached test as it now reads. For example: "...and a relocation, which tries positions, passes over those where a section the chain reached starts, and those where the digits of a stream the chain reached start past the place's own run of signs, as that stream's reading lexed them (<see cref="DigitsPastSigns"/>, <see cref="_sectionDigitsAt"/>): a header found at its digit, or at its first sign where the line its number is on holds a %, wherever its run starts and whatever sign starts it (#188, #327)." Leave HeaderTokenStart's window qualifier (:5560) as it is: it is correct for the line test and the record of a stream passed over. Not a maintainer decision.

## 23. threat-model-455-cut-candidate-guard-said-to-stand — minor → refuted none (batch:)
Location: docs/threat-model.md:455 (row 'What the chain refers to', cell 1); code at src/AdCodicem.Pdf/IO/PdfFileReader.cs:6623-6629 and 6641-6653

Claim: The threat model places 'one whose dictionary a guard cut before it said what it is' among the dropped candidates whose dictionary reports stand once, 'the guard that cut it among it'. The code does the opposite. For a place the search tries, the reading is filtered by DeclaresOnlyCrossReferenceStream, and a dictionary MaxTrailerLength cut before its /Type value and any /W declares nothing. Such a dictionary, or one whose /Type the cut parted (#279's parted name, read as another type), leaves nothing of its reading, the guard included, as the code comment at :6623-6625 says. Under a lowered MaxTrailerLength the search then takes a different section, or the batch's own xref.stream-passed-over Warning vanishes, and no limit.* code is reported anywhere. main does the same in the plain layout, so the silent guard is not new; the batch's text misstates it, and the vanishing Warning is new with the batch's report.

Evidence: Generator: W/r13wip/batch/tools/hand_parted_candidate.py, files in W/r13wip/batch/hpc, outputs hpc/out-{branch,main}.txt (probe W/r13wip/batch/probe-*). Object 9, '/Type /XRef' with no /Prev, is padded so that a 256-byte window opened at its header ends K bytes into the /Type value.

Plain layout, pp_km3_n21.pdf (K=-3, object 9 at 419, the newest table's /Prev 421):
- Default limits, branch and main: 'Repair xref.offset-adjusted at 419 ... found 2 bytes before it', then a rebuild.
- MaxTrailerLength 256, branch and main: only 'Repair xref.offset-adjusted at 229 ... found 192 bytes before it' and 'Warning xref.section-shifted at offset 229', with kept=1. There is no limit.trailer: a different section is taken, and nothing says a guard was reached.

Hybrid layout, #327's 'passed' shape; pc_km3, pc_k0 and pc_k2 (object 9 at 534):
- Default: 'Warning xref.stream-passed-over at 534: The cross-reference stream at offset 534, near offset 514, ...'.
- MaxTrailerLength 256: no Warning and no limit.* code (kept=1). Only offset-adjusted at 229 and section-shifted remain.
- Controls pc_k5 and pc_k12 (the cut falls past /Type /XRef): 'Warning limit.trailer at 534' with the stream taken as the guard's section, plus xref.checked-in-part.

Generated instance: gen/f01476_n130.pdf (seed 202611102016, index 1476). Under D, 'Warning xref.stream-passed-over at 3391'. Under T256, nothing new: no Warning and no limit.*.

Fix: Correct cell 1 of threat-model row 455 to the code's rule. A place that declares no cross-reference stream leaves nothing of its reading, not even a guard its dictionary reached. That covers a dictionary a guard cut before its /Type and /W, and one whose /Type the cut parted (#279). Only a candidate that declared itself (a /W, or /Type /XRef read whole) keeps what reading its dictionary reported, the guard among it. Say in the xref.stream-passed-over row of diagnostics.md that a guard cutting the passed-over stream's own dictionary before its /Type is complete drops it with nothing reported. Comment on #279 with the pc_k2 and pp_km3 files. The other option is a behavior change: keep the guard's report for such a candidate (invariant 12). That belongs with #279 or a debt for the maintainer, not in this batch's final pass.

Refuter: verdict refuted, severity none, needs_maintainer False
Reason: The threat model's cell does not say what the finding reads into it. The sentence has two clauses. The first covers an object at a place the search tries that "declares no cross-reference stream — it gives another /Type, or neither /Type /XRef nor a /W, or is no dictionary". Such an object "leaves nothing of its reading", and the guard is part of that reading. The second clause covers only "one that may be such a stream and is dropped". Its item "one whose dictionary a guard cut before it said what it is" is therefore a dictionary that does declare one through a /W (DeclaresOnlyCrossReferenceStream holds) but whose /Type the cut hid. For that case the guard does stand, once.

Every file in the finding has its /W written after /Type, so the cut falls before both. Those files therefore exercise the first clause, and the code does what that clause says. The /Type the cut parts (/X) is "another /Type", read as written under #279, which is accepted. The code comment at PdfFileReader.cs:6623-6629 has the same two-clause structure: "not even a guard its dictionary reached" for what declares nothing, and "a guard that cut the dictionary among it" for what declares itself. The cell was worded this way on purpose in round 9: finding [24] in r9-findings.md, recorded in decisions.md:606. The refuter's wording there separates "plain-nowidths: only xref.offset-adjusted" from the CIDFont pin that keeps limit.trailer.

The behavior the finding points to is not new. A guard that cuts a guess which declares nothing goes unreported, and under a lowered limit another section is taken. That is main's behavior, which the finding itself concedes, and DocumentReaderTests pins it: the '<< /Producer (...) >>' row at MaxTrailerLength 128 of A_place_a_section_may_have_moved_to_that_holds_another_object_leaves_nothing_of_its_reading. The xref.stream-passed-over Warning is also absent under T256, and the documented rule explains why: PdfDiagnosticCodes.XRefStreamPassedOver reports "Only a stream that declares itself one … its dictionary gives /Type /XRef". A dictionary cut before its /Type value never gives it, so withholding the Warning is consistent with that text. It is not a report that went missing.

Refuter evidence: My own generator writes eight files: W/r13wip/verify-threat-model-455-cut-candidate-guard-said-to-stand/gen.py into files/. Object 9 is a /Type /XRef stream, and its 256-byte window ends either 3 bytes before the /Type value ('/Ty' read, km3) or inside it ('/X' read, k2). Two variants, nowidth (/W after /Type) and width (/W [1 1 1] written first), each in a plain and a hybrid layout ('passed'). The probe is ZzVerifyTm455Tests.cs.txt, run on dfb03fc with a Release build; its output is out-branch.txt.

T256 results:
- plain_nowidth_km3 / _k2 (the finding's pp_km3 shape): only 'Repair xref.offset-adjusted at 229 … found 192 bytes before it', kept=1. This is the first clause: it declares nothing and leaves nothing.
- plain_width_km3 (the cell's "cut before it said what it is" case: a /W read, /Type hidden): 'Warning limit.trailer at 419: The dictionary of the object read as a cross-reference stream runs past 256 bytes; … Raise PdfReaderLimits.MaxTrailerLength to read past it.' then 'Repair xref.offset-adjusted at 229'. The guess is dropped and its guard stands, as the cell says.
- hybrid_nowidth_km3 / _k2 (the finding's pc_km3 / pc_k2): offset-adjusted at 229 only. Default limits give 'Warning xref.stream-passed-over at 534'.
- hybrid_width_km3: 'Warning limit.trailer at 534' plus offset-adjusted at 229. The guard stands here too.
- plain_width_k2 / hybrid_width_k2 (/Type parted to /X, which reads as another type, #279): nothing.

The existing pins pass on dfb03fc: filter '*A_place_a_section_may_have_moved_to_*', 7/7. They include DocumentReaderTests.A_place_a_section_may_have_moved_to_whose_dictionary_a_guard_cuts_is_no_section_the_chain_ends_at, where a CIDFont's /W comes before its /Type: Equal(LimitTrailer, XRefOffsetAdjusted). They also include the '/Producer' row at 128, which reports XRefOffsetAdjusted only.

In the code, TryParseNumberedAt with keep = DeclaresOnlyCrossReferenceStream returns before ReachLimit when keep refuses the object (PdfFileReader.cs, the 'keep?.Invoke(parsed) == false' check in the IsTruncated && cut branch). TryParseNearHybridPrev uses the same filter.

Main has the same silent guard in the plain layout; the finding concedes it, and its hpc/out-main.txt shows no limit.* at T256.

Refuter fix: None needed: the cell, the code comment at :6623-6629 and the tests agree. An optional clarity tweak, not required: in threat-model.md:455 cell 1, write "one whose dictionary gives a /W and a guard cut before it said what it is", so the second clause's case cannot be read as covering a dictionary cut before both /Type and /W. That case is already covered by the first clause. The guard at a guess that declares nothing goes unreported, with the search possibly taking another section under a lowered limit. That is main's settled design, pinned by an existing test; reopening it would be a maintainer decision outside this batch, beside #279.
