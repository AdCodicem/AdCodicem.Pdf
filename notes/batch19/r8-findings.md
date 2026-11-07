######## [1] lens=undecoded key=undecoded-drop-loses-parse-reports sev=major -> verdict=confirmed sev=major maint=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:5010 (mark taken at :4967)
CLAIM: 092b772 calls RollBackBesideLimits(reports) when the search drops a candidate whose data cannot be decoded. That call removes every non-limit report the candidate's reading made, not only the decoding's. TryParseNumberedAt had already kept the syntax.* and stream.* reports of the accepted stream dictionary (KeepPending). The entries leave the diagnostics, but _syntaxReported, and the stream length recorded for the object, still count them as made. So no later reading of the object reports them again. This is a missing report (invariant 5), new in 092b772. Only the decoding's own report comes back, because the mark restores _decodingReports.
EVIDENCE: Probe ZzR8uLostTests (copy in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r8u-probes/). The oldest table indexes 1 to 3. After a 1,200-byte comment comes object 9: '<< /Size 10 /W [1 4 2] /W [1 4 2] /Index [3 1] /Filter /Foo /Length 7 >> stream xxxxxxx'. The newest table's /Prev names a place 3 bytes into object 9's header.
- Head: opening gives only 'Warning xref.section-missing at 1479 ...' and 'Repair xref.rebuilt'. GetObject(9) adds nothing. The validator gives only 'Error xref.section-not-found'. syntax.key-repeated never appears.
- d3ef236 and main both give 'Warning syntax.key-repeated at 1507: The dictionary gives the key /W more than once; the last value given is kept.'
- With '/Length 3', head never gives 'Warning stream.length-invalid at 1553: The stream declared 3 bytes but ended after 7.' d3ef236 does.
- Control on head: the same dictionary with /W [1 -4 2] is dropped for its widths, with no rollback, and keeps 'Warning syntax.key-repeated at 1508' and 'Warning stream.length-invalid at 1554'.
- Decoding object 9 after opening gives 'Warning filter.unsupported at 1564' again: only the decoding's report comes back.
FIX: Roll back only what the decoding reported. For a candidate, take the decoding mark in TryReadXRefStream just before the Decode call, and around RowsHeldAtMost's decode in FaultBesideCut. Roll back to it there when the data is undecoded, or record that mark on the attempt and roll back to it in TryRelocateXRefSection. The reports the parse kept then stay, as for any other dropped Malformed candidate. If they are meant to go as well, also remove them from _syntaxReported and the recorded lengths, so that a later load reports them. Test: in the probe's undecodable shape, syntax.key-repeated and stream.length-invalid are each reported once. Pair it with the bad-/W control, which also kills M09.
--- REFUTER: The finding reproduces. At 012e13d, TryRelocateXRefSection takes its mark before TryReadXRefSection (PdfFileReader.cs:4967). When the dropped candidate's fault is DataUndecoded, it calls RollBackBesideLimits(reports) at :5012. That removes every non-limit entry the candidate's reading made, including the syntax.* and stream.* reports that TryParseNumberedAt's KeepPending had moved into the document's diagnostics. _syntaxReported and _lengthFaultsReported still record those reports as made, so a later load of the object stays silent. The result is a report missing for good (invariant 5).

d3ef236 and main 21a0298 both report them. The batch made it new in 092b772.

The code also does more than its own texts say. The comment at :5006-5009 says "what the decoding reported goes with it", and 092b772's body says such a place "leaves no report of its decoding".

The ADR 53 record of 092b772 (r7-fix-result.json) is wrong too. It classes M09 (the condition replaced by true) as equivalent, because "A dropped place's reading reports nothing but guards". That premise is false: the bad-/W control keeps syntax.key-repeated on head, and loses it under M09.

The shape is narrow. It is a stream near a /Prev offset where nothing starts, with no /Type /XRef, a valid /W and numbering, and data its filters decode nothing of. A typed stream is kept as Malformed, and its reports stay. The issue is not covered by the accepted B19-1/#263 guess-path consequence (refuses nothing, hands no /Prev on): losing reports is a worse consequence that no text states. #171, #289, #295, #331, #323 and #52 do not hold it. By the brief's rubric (a missing report), it is major.
REFUTER EVIDENCE: I rebuilt the probe myself, as ZzR8uLostTests.Build(dict, 3) with my own runner. After opening, the runner calls GetObject(9) and then Decode(doc.Diagnostics) on it.

Head 012e13d:
- dict '/Size 10 /W [1 4 2] /W [1 4 2] /Index [3 1] /Filter /Foo /Length 7':
  - Opening gives only 'Warning xref.section-missing at 1479 ...' and 'Repair xref.rebuilt'; sections 'S 1479 NotFound'.
  - GetObject(9) and the decode add only 'Warning filter.unsupported at 1564'.
  - syntax.key-repeated never appears.
- '/Length 3' variant: stream.length-invalid never appears. Only filter.unsupported at 1553, on decoding.
- Control '/W [1 -4 2]' (dropped for its widths, no rollback): keeps 'Warning syntax.key-repeated at 1508'.
- Typed '/Type /XRef' variant: taken Malformed, keeps 'syntax.key-repeated at 1519'.

d3ef236 (rebuilt in my worktree): the same file gives 'Warning syntax.key-repeated at 1507' and 'Warning filter.unsupported at 1564' at opening. The /Length 3 variant gives 'Warning stream.length-invalid at 1553: The stream declared 3 bytes but ended after 7.' The reviewer's main output matches d3ef236 for the repeated key.

M09 on head (the condition replaced by 'true'): the bad-/W control loses 'syntax.key-repeated at 1508', and opening gives only xref.section-missing and xref.rebuilt. So M09 is not equivalent.

Fix trial on head: a decoding-reports mark taken in TryReadXRefStream just before the decode loop, for a candidate, recorded on the attempt when undecoded and rolled back to in place of the outer mark.
- Repeated-key shape: 'syntax.key-repeated at 1507' once at opening. filter.unsupported is absent at opening and appears once on decoding.
- /Length 3: 'stream.length-invalid at 1553' once.
- Control unchanged.
- Whole unit suite with the trial: 7,770 total, 0 failed.

Code: PdfFileReader.cs:4967 (mark), :5010-5013 (rollback), :5810-5824 (KeepPending/IsKept, _syntaxReported.Add). PdfDiagnostics.cs:146-176 (RollBackBesideLimits drops every non-limit entry). All probes are removed and the worktree is clean.
REFUTER FIX: Roll back only what the candidate's decoding reported.
- In TryReadXRefStream, when candidate is true, take _diagnostics.GetMarkWithDecodingReports() just before the decode loop.
- Where the decode says undecoded, record that mark on the section record (e.g. XRefSectionRecord.UndecodedReports).
- In TryRelocateXRefSection, call RollBackBesideLimits with that recorded mark when the dropped attempt's Fault is DataUndecoded, and drop the mark taken at :4967.
- RowsHeldAtMost, called from FaultBesideCut, decodes with diagnostics: null and reports nothing, so it needs no mark.
- This is still at most one mark per candidate, so the comment's MaxRelocationCandidates bound holds.
- I verified this variant: the whole unit suite passes, and the reports come out once each.

Tests, in the undecodable no-/Type shape whose /Prev falls 3 bytes into the stream's header:
- With a repeated /W, syntax.key-repeated is reported once at opening, filter.unsupported is absent at opening, and filter.unsupported is reported once on decoding.
- With /Length 3, stream.length-invalid is reported once.
- The bad-/W control keeps syntax.key-repeated. This kills M09.

Also:
- Correct the ADR 53 record of 092b772: M09 is not equivalent, and the premise that a dropped place's reading reports nothing but guards is false.
- 092b772's body ('leaves no report of its decoding') is already pushed and is true of the fixed code. It needs no change; only the status.md record should note the round-8 fix.

This is not a maintainer decision. The comment and the commit already state the intent (the decoding's reports only). Dropping the parse reports as well would need _syntaxReported and _lengthFaultsReported un-recorded too, and no text asks for that.

######## [2] lens=undecoded key=raw-deflate-garbage-rows sev=major -> verdict=narrowed sev=nit maint=False
LOCATION: src/AdCodicem.Pdf/IO/Filters/PdfFilterPipeline.cs:429 (Flate branch of ApplyOne; FlateFilter.cs:92 repaired = raw); tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:664
CLAIM: Take zlib data whose first byte is damaged. Read as zlib it fails before any output. The raw-deflate repair, however, decodes a few bytes of garbage before it turns corrupt. TryDecode then returns true, so `undecoded` stays false, and the reader reads rows from the garbage. A row of a reserved type then reads as null with no older row standing. So [18]'s consequence remains for this shape, while R7-Q1 fixed it for damage at byte 1 or 2. The test name 'A_writer_s_update_in_zlib_data_serves_the_new_revision_of_what_it_rewrites_whether_or_not_its_rows_decode' claims more than holds.
EVIDENCE: Pipeline probe on two rows of zlib xref data: of the 256 values for byte 0, 50 give undecoded=False and garbage output, e.g. 0x0A -> len=10 [519C3230303050303032]. Complementing byte 1 or byte 2 gives undecoded=True.
Reader probe ZzR8uReadTests.ZzUpdateByte0 uses UpdateInZlibData's writer shape with byte 0 set to 0x0A.
- Head and d3ef236 give: 'Repair filter.failed at 432: A Flate stream was not valid zlib data.' | 'Warning filter.failed at 432: A Flate stream is corrupt at byte 10 of its 19; the 7 bytes decoded before the fault was found were kept.' | 'Warning xref.section-unreadable at 309: ... was read only in part: it holds 1 row where its /Index and /Size declare 2; ...' | 'Warning xref.entry-reserved at 309: ... the row for object 4 gives it type 81. ...'. Then 'obj 4: null'. The validator gives Error xref.section-malformed and Warning xref.entry-type-reserved 'type 81'.
- Main 21a0298 gives 'obj 4: "old four"'.
- Byte 0 set to 0x3A gives the same, with type 195.
- Damage at byte 2 on head gives 'new four' through the rebuild.
FIX: For the maintainer: should Flate count as undecoded for a cross-reference stream when its zlib reading failed before any output and its raw-deflate fallback only turned corrupt (repaired, ending FlateEnding.Corrupt)?
- If yes: have ApplyOne say so through a flag beside `failed`, returning the same data to other callers, and add a byte-0 row to the FilterTests theory and to the writer's-update theory.
- If no: say in the `undecoded` XML doc, threat-model.md:442 and diagnostics.md that data the raw-deflate repair decodes part of is read as rows, reserved types included, and rename the test.
--- REFUTER: The reproducer holds on 012e13d, but the major claim does not. Only the test name's overreach is left, and that is a nit.

1. The shape is outside R7-Q1 as decided. R7-Q1 covers filters that decoded nothing: "a Flate failure before any output, an unsupported filter". Here the Flate filter produced output through its own raw-deflate repair, and the library's own report says so: "the 7 bytes decoded before the fault was found were kept". FlateFilter.TryDecode returns true, so ApplyOne's `failed` is never set.

2. The texts already state that such data gives rows. The R7a stage recorded it as a deviation: "A filter's partial output and an image filter's data give rows". The `undecoded` XML doc says "A filter that decoded part of its data before a fault gives what it decoded, which this does not say" (PdfFilterPipeline.cs:90-101). threat-model.md:442 says "data a filter decoded part of before a fault holds the rows that part does, the stream read in part where it lacks some". diagnostics.md:133 (filter.failed) says "otherwise what decoded is kept". The reserved-type rule (#221) then applies to rows read from decoded data, as written. No text misstates the shape.

3. The class is generic, not specific to the repair path. Damaged deflate data commonly decodes to garbage before a fault is detected, or decodes whole with only the checksum disagreeing. Complementing byte 7 of the same stream leaves the zlib header sound and involves no repair, yet it reads a reserved row for object 6 and serves "old four". In a 32-row variant, about 20 of roughly 100 single-byte body damages give xref.entry-reserved. A flag for "repaired raw deflate that turned corrupt" would close one instance of the class, not the class.

4. The finding is not worse than what was settled in round 7. On this writer's-update shape, round 7's refuter of [18] judged main's "old four" against the batch's null as "differently wrong, not a regression". Byte 0 on head gives the same result d3ef236 gave, with every anomaly reported: filter.failed (Repair and Warning), xref.section-unreadable read in part, xref.entry-reserved, Error xref.section-malformed, Warning xref.entry-type-reserved. No invariant 5 gap.

What survives is the test name. A_writer_s_update_in_zlib_data_serves_the_new_revision_of_what_it_rewrites_whether_or_not_its_rows_decode reads as a universal claim, but its rows cover only sound data and damage at byte 2, which decodes nothing. Damage at byte 0 (0x0A) or byte 7 also leaves the rows undecoded, and neither serves "new four". The builder's doc and the test's comment do say "third byte ... decodes to nothing", so the overreach is in the name only.
REFUTER EVIDENCE: Probe: my own uncommitted ZzR8RawDeflTests, copy at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r8ref-rawdefl/ZzR8RawDeflTests.cs.txt, outputs beside it as out-*.txt. It rebuilds UndecodedStreamFiles.UpdateInZlibData's shape on 012e13d.

- Byte 0 set to 0x0A:
  - "Repair filter.failed at 432: A Flate stream was not valid zlib data."
  - "Warning filter.failed at 432: A Flate stream is corrupt at byte 10 of its 19; the 7 bytes decoded before the fault was found were kept."
  - "Warning xref.section-unreadable at 309: ... read only in part: it holds 1 row where its /Index and /Size declare 2; ..."
  - "Warning xref.entry-reserved at 309: ... the row for object 4 gives it type 81. ..."
  - "obj 4: null repaired=False"
  - Validator: Error xref.section-malformed ("holds 1 row where ... declare 2") and Warning xref.entry-type-reserved.
  - Byte 0 set to 0x3A gives the same output with type 195.
- Sweep of byte 0 over 0..255: 204 values give "new four" through the rebuild, 48 give null with entry-reserved, 2 give out-of-range plus a rebuild, 1 gives section-unreadable plus a rebuild, and 1 is the undamaged byte. This matches the finding's 50.
- Complementing byte 0, 1 or 2: "Warning filter.failed: A Flate stream could not be decoded.", then xref.rebuilt, then "new four". The validator gives "cannot be read: its data could not be decoded."
- Complementing byte 3, 4 or 5: "corrupt at byte 10 of its 19; the 5 bytes decoded ... were kept", then "holds 0 rows", then the rebuild serves "new four". This is partial output giving rows, as documented.
- Complementing byte 7 (sound zlib header, no repair):
  - "Warning filter.checksum-mismatch at 432: A Flate stream's checksum disagrees with the 127 bytes its data decoded to; ..."
  - "Warning xref.entry-reserved at 309: ... the row for object 6 gives it type 20. ..."
  - "Repair xref.offset-adjusted at 110"
  - "obj 4: \"old four\""
  - Garbage rows read from damaged Flate that decodes, with R7-Q1 not involved.
- A 32-row variant swept over every byte gives xref.entry-reserved for about 20 of the roughly 100 damaged positions, including whole decodes with only a checksum mismatch.

Texts:
- PdfFilterPipeline.cs:90-101 (`undecoded` doc): "A filter that decoded part of its data before a fault gives what it decoded, which this does not say".
- threat-model.md:442: "data a filter decoded part of before a fault holds the rows that part does".
- r7-fix-result.json, R7a deviations and open points: "A filter's partial output and an image filter's data give rows".
- decisions.md R7-Q1: "filters decoded nothing (a Flate failure before any output, an unsupported filter)".
- r7-findings.md [18], refuter on the writer-like update: main's "old four" against head's null is "differently wrong, not a regression".

Main 21a0298 was not rebuilt for byte 0; the verdict does not depend on it, since main serves the superseded revision there too.
REFUTER FIX: Code: none. R7-Q1 is implemented as decided, and a flag limited to "repaired raw deflate that turned corrupt" would not close the class, since body damage under a sound zlib header gives the same garbage rows. Widening "undecoded" to data that decoded, wholly or in part, would be a new design question; I do not recommend raising it.

Rename the test to say only what its rows hold, for example A_writer_s_update_in_zlib_data_serves_the_new_revision_of_what_it_rewrites_whether_its_data_decodes_whole_or_nothing_of_it_decodes, and update its citation in threat-model.md:442.

Optionally, in threat-model.md:442 after "data a filter decoded part of before a fault holds the rows that part does", add "whatever they read as: damaged Flate data, raw deflate read past a damaged zlib header among it, may decode to bytes that are no rows, of a reserved type too". This makes the residual explicit.

######## [3] lens=undecoded key=guard-cut-size-index-hides-undecoded sev=major -> verdict=narrowed sev=major maint=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:6848 and :6855 (FaultBesideCut returns null before the data check at :6905)
CLAIM: /Size or /Index (the whole value) can lead to an object a guard cut. FaultBesideCut then returns null before it checks the data. Under the guard, a stream whose filters decode nothing of its data is therefore the guard's: CutByLimit, xref.checked-in-part, the chain ending there, no /Prev handed on, nothing rebuilt. A whole read finds the same stream Malformed with 'its data could not be decoded'. The file's fault is blamed on the guard (ADR 34). This contradicts four texts: 092b772's body ('a stream whose value a guard cut is found unreadable for such data, as a whole read finds it'), the first sentence of FaultBesideCut's summary, diagnostics.md:112 and validation-rules.md:54. Both pages say 'but for what makes the section unreadable whatever the value holds ... or data ... its filters, written direct, decode nothing of'.
EVIDENCE: Probe ZzR8uGuardTests uses GuardCutValueTests' UnderAnUpdate shape with MaxObjectLength 1024. Object 6 is '<< /Type /XRef /W [1 4 2] /Size 5 0 R /Filter /Foo /Length 7 >> stream xxxxxxx', and object 5 is '8' followed by 1,100 spaces.
- Whole read: 'Warning filter.unsupported at 1399' | 'Warning xref.section-unreadable at 1320: ... cannot be read: its data could not be decoded. ...'. Section Malformed, fault 'its data could not be decoded'; 'Error xref.section-malformed'; object 3 served through the rebuild.
- Guarded read: only 'Warning limit.object at 203: Object 5 runs past 1 KB ...'. Section 'Malformed cut=True fault=its /Size is the reference 5 0 R, which one of the reader's limits cut'; 'Information xref.checked-in-part at offset 1320'; 'obj 3: null'.
- '/Index 5 0 R', with object 5 '[0 8]', and the same shape under /FlateDecode behave alike.
- '/W 5 0 R' and '/Index [0 5 0 R]' give the file's fault under the guard, as read whole: these are the theory's rows.
FIX: In FaultBesideCut, judge data its filters decode nothing of before the early returns for /Index and /Size; the decoding needs no count. When the stream is given and RowsHeldAtMost says undecoded, return DataUndecoded whatever /W, /Index or /Size a guard cut: a whole read finds the section unreadable whatever they hold, and only its fault's wording could differ. Add '/Size 5 0 R' and '/Index 5 0 R' rows to GuardCutValueTests.A_section_whose_filters_decode_nothing_of_its_data_whatever_a_value_a_guard_cut_holds_cannot_be_read_as_read_whole. The other way is to narrow the summary and both pages to say that a cut /Index or /Size leaves the section the guard's. Either way, record 092b772's sentence under 'Commit bodies not rewritten'.
--- REFUTER: The behavior holds at 012e13d. FaultBesideCut returns null at :6850 when the whole /Index is cut, and at :6857 when /Size is cut with no /Index. Both returns come before the data check at :6905-6907, so data its filters decode nothing of is never judged in those two shapes. Under the guard, the file's fault (Error xref.section-malformed, the rebuild serving objects 1-3) becomes the guard's: CutByLimit, Information xref.checked-in-part, objects read as null.

This breaks the rule FaultBesideCut exists for: what makes the section unreadable whatever the cut value holds is the file's, as read whole. Undecoded data needs no count. The R7a stage said its change "keeps 'as a whole read says it' true", which is false for these two shapes. Earlier rounds rated the same kind of defect major: r1 [4] and r1 [7] part 2, both a missing Error under a guard where a whole read reports one.

Narrowed in three ways:
(1) FaultBesideCut's summary does not contradict the code. Its one sentence ends "judged where the /Index or /Size it numbers them with was read", which is exactly what the code does. It gives no reason, though: undecoded data does not depend on the count, and the summary's own rule is that only what depends on a cut value says nothing.
(2) Two more texts overstate. docs/threat-model.md:440 says such data "holds no row whatever the counts read declare, though they declare none", with no condition. The test name GuardCutValueTests.A_section_whose_filters_decode_nothing_of_its_data_whatever_a_value_a_guard_cut_holds_cannot_be_read_as_read_whole covers only rows where /W or an element of /Index is cut.
(3) It is not a regression in the guarded output. Main 21a0298 and d3ef236 have the same early returns (main :4697/:4706), so the guarded read was the guard's there too. The batch made /Size or /Index cut over undecoded data inconsistent with the rule it now states, by turning undecodable data into a fault "whatever the value holds" (R7-Q1).

None of #171, #263, #289, #295, #323, #329, #331 or #52 holds this, nor does the accepted list. The B19-1 guess path accepted there is for a stream with no /Type; this one gives /Type /XRef.
REFUTER EVIDENCE: Probe: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r8ref-sizeindex/ZzR8RefSizeIndexTests.cs.txt. It uses GuardCutValueTests' shape: ChainFiles.UnderAnUpdate, object 5 followed by 1,100 spaces, MaxObjectLength 1024, and object 6 '<< /Type /XRef VALUES /Filter F /Length 7 >> stream xxxxxxx'. Output files: out-head.txt and out-fix.txt in the same folder.

At 012e13d, '/W [1 4 2] /Size 5 0 R', five='8', /Foo:
- Whole read: section 1320 'Malformed cut=False fault=its data could not be decoded'. Diagnostics: 'Warning filter.unsupported at 1399', then 'Warning xref.section-unreadable at 1320: ... cannot be read: its data could not be decoded. ...', then 'Repair xref.rebuilt'. Objects 1-3 served. Rule 'Error xref.section-malformed'.
- Guarded read: section 1320 'Malformed cut=True fault=its /Size is the reference 5 0 R, which one of the reader's limits cut'. The only diagnostic is 'Warning limit.object at 203: Object 5 runs past 1 KB ...'. repaired=False, objects 1, 2, 3, 6 and 7 null. The only rule is 'Information xref.checked-in-part'.

The same split shows for '/Size 5 0 R' under /FlateDecode ('filter.failed ... A Flate stream could not be decoded' when read whole), for '/Index 5 0 R' with five='[0 8]' under /Foo and /FlateDecode, and for '/Index 5 0 R /Size 8'.

Controls: '/W 5 0 R /Size 8' and '/W [1 4 2] /Index [0 5 0 R]' give 'Malformed cut=False fault=its data could not be decoded' under the guard, with diagnostics equal to the whole read's but for limit.object. '/Size 5 0 R' under /LZWDecode, whose first code is undefined, is read in part when read whole (accepted) and stays the guard's.

Prototype fix (reverted): the two early returns call RowsHeldAtMost and return DataUndecoded when it reports undecoded. Every undecoded row then gives under the guard exactly the whole read's sections, diagnostics (plus limit.object), objects and 'Error xref.section-malformed'. The LZW control is unchanged. GuardCutValueTests, CrossReferenceChainTests, CrossReferenceRuleTests and HostileInputTests pass: 1,632 of 1,632.

Texts that state it with no condition: 092b772's body ('a stream whose value a guard cut is found unreadable for such data, as a whole read finds it'), diagnostics.md:112, validation-rules.md:54 and threat-model.md:440. reference/reader-limits.md:181-191 lists the faults beside a cut without undecoded data at all.
REFUTER FIX: Fix the code; no maintainer question is needed, because the rule is already settled (FaultBesideCut's purpose and R7-Q1).

In FaultBesideCut (PdfFileReader.cs:6848-6857), check for undecoded data before the early returns, where /Index or /Size cannot be read. When the stream is given and RowsHeldAtMost reports undecoded, return DataUndecoded; otherwise null, as now. A small helper shared with :6905 avoids writing the test twice. Then:
- Reword the summary's 'judged where the /Index or /Size it numbers them with was read' so it applies only to the held-fewer check, and say that undecoded data is judged whatever /Index or /Size a guard cut.
- Add rows '/W [1 4 2] /Size 5 0 R' with five '8' (/Foo and /FlateDecode) and '/W [1 4 2] /Index 5 0 R' with five '[0 8]' to GuardCutValueTests.A_section_whose_filters_decode_nothing_of_its_data_whatever_a_value_a_guard_cut_holds_cannot_be_read_as_read_whole. They fail at 012e13d.
- Add the undecoded-data clause to reference/reader-limits.md's list of faults beside a cut, beside the other pages.
- Run ADR 53 over the new lines.

If the maintainer prefers to keep the gap instead, narrow diagnostics.md:112, validation-rules.md:54, threat-model.md:440 and the test name to 'where the /Index or /Size it numbers them with was read', and record 092b772's sentence under 'Commit bodies not rewritten'.

######## [4] lens=undecoded key=check-decodes-typed-stream sev=minor -> verdict=confirmed sev=minor maint=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:6244-6258 (the check's decode) with :3772-3776 (KeepsItsPlace)
CLAIM: The check of a /Prev's place now decodes the stream's data even for a stream of /Type /XRef. For such a stream KeepsItsPlace answers Section whatever the check's state (`state == Read || check.TypedXRef`). That is a whole decoding, up to MaxDecodedStreamLength, that nothing uses. It happens at most once per hybrid table, so the cost is bounded but avoidable, and on that shape it nearly doubles the time to open.
EVIDENCE: Probe ZzR8uCostTests: a hybrid table whose /Prev names a /Type /XRef stream with zlib data decoding to 200 MB, and whose /XRefStm names a place 3 bytes into that stream's header. Over 8 runs:
- Typed stream: head 661 to 1,158 ms (median about 700); d3ef236 296 to 845 ms (median about 440). Both serve 'new three', with diagnostics xref.hybrid-stream-lost.
- Untyped stream, where the check's decode does decide: head 583 to 1,005 ms; d3ef236 283 to 690 ms.
Bytes read from the source are equal; the difference is one more 200 MB decode.
FIX: In the check, skip the decode when section.TypedXRef, since KeepsItsPlace keeps the place whatever the data. For example, return Read there with a comment saying why. In the KeepsItsPlace remark, say the extra decoding happens only for a stream of no /Type.
--- REFUTER: The finding holds as stated: the decode is unused and bounded, but it can be avoided. 092b772 added a whole decode to TryReadXRefStream's check (PdfFileReader.cs:6244-6258) and runs it for every stream that gets that far. section.TypedXRef is set at :6075, before that decode. KeepsItsPlace, the check's only caller (:3772), returns Section whenever `state == Read || check.TypedXRef` (:3775). So for a stream of /Type /XRef the decode cannot change the answer. R7a's own mutant notes agree: M14 was classed equivalent because KeepsItsPlace reads only State, TypedXRef and CutByLimit.

The cost is new to round 7. On d3ef236 the check returned Read straight after the numbering, with no decode. It happens at most once per hybrid table whose /XRefStm search comes to the /Prev's place, within MaxDecodedStreamLength. The /Prev's own reading decodes the stream just after, so the extra work is at most one decode per table. That makes it bounded but avoidable, which is minor on the brief's scale.

Nothing settles it already. R7a's result states the cost ('once per KeepsItsPlace … within MaxDecodedStreamLength') but never says the decode is useless for a typed stream. decisions.md accepts consequence (c), the ThrowOnLimit throw during the check, but not the extra decode. The KeepsItsPlace remark is not false ('one decoding more of the stream … where the object reads that far'), but it says this happens for every object, typed ones included. The claim 'nearly doubles' is roughly right: it varies with the shape, and I measured about 2.5x where the decode dominates.
REFUTER EVIDENCE: Built head 012e13d in Release (0 warnings). I wrote my own probe instead of reusing ZzR8uCostTests; it is kept at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r8ref-typeddec/ZzR8RefTypedDecodeTests.cs.txt.

The file: an oldest classic table indexes objects 1 to 3. A newer revision writes (new three) and object 4, a /FlateDecode stream (/W [1 4 2] /Index [3 1] /Prev oldest) holding one row plus 200 MB of zeros, 208,603 bytes in all. The newest classic table gives /Prev = object 4 and /XRefStm = 4 bytes into object 4's header ("near"), or the middle of a 3,000-byte comment ("far", so the search finds nothing and KeepsItsPlace is never asked). I timed 6 runs of opening the file and reading object 3.

Head:
- typed-near: 675 717 877 758 666 807 ms, median 758; xref.hybrid-stream-lost; three = new three
- typed-far: median 307; xref.section-missing
- untyped-near: median 716
- untyped-far: median 311

Head with the proposed fix (`if (section.TypedXRef) return XRefSectionState.Read;` at the top of the check's `if (checkOnly)` block; diff at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r8ref-typeddec/fix.diff):
- typed-near: 291 393 299 293 285 251 ms, median 293
- typed-far: median 313
- untyped-near: median 789, unchanged, as expected, since there the decode decides
- Every shape gives the same object text, the same WasRepaired and the same diagnostic codes as on head.

The whole unit suite with the fix (local corpus): 7,747 total, 7,727 passed, 20 skipped, 0 failed. No test depends on the typed stream's decode, so removing the skip again would survive every existing test. The worktree was restored afterwards.

Texts that describe the decode for every object:
- the KeepsItsPlace remark at :3753-3757
- the TryReadXRefStream check paragraph at :5961-5970 ('the data is decoded as the reading first decodes it')
- the comment at :6239-6242
- threat-model.md:432 ('a stream of `/Type /XRef`, or an object that gives a `/W` and reads there as a section … told by reading it as the chain would … its data decoded')
REFUTER FIX: 1. In TryReadXRefStream's `if (checkOnly)` block at :6244, add `if (section.TypedXRef) { return XRefSectionState.Read; }` before taking the mark, with a comment: a stream of /Type /XRef keeps its place whatever its data holds (KeepsItsPlace), so its data is not decoded. This cannot change any answer, because KeepsItsPlace short-circuits on TypedXRef. It also removes accepted consequence (c), the ThrowOnLimit throw during the check, for typed streams; the /Prev's own reading still throws the same exception just after.
2. Reword the texts so the extra decode applies only to a stream of no /Type, or of another /Type, that gives a /W: the KeepsItsPlace remark ('one decoding more of the stream … where the object gives no /Type /XRef and reads that far'), the check paragraph of TryReadXRefStream's remarks, the comment at :6239, and threat-model.md:432 ('its data decoded' moves to the object that gives a /W).
3. Under ADR 53, the 'remove the early return' mutant changes only cost. Kill it with a cost test, for example opening the probe's typed-near shape from a counting non-memory stream (DocumentReaderTests already has one) and asserting the stream's data is read once rather than twice. Otherwise class it as a survivor with its reason.

This is not a design choice for the maintainer: R7-Q1 asked for the check to tell undecodable data, and a typed stream's place is kept by the earlier rule whatever its data.

######## [5] lens=undecoded key=m09-not-equivalent sev=minor -> verdict=confirmed sev=minor maint=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:5010
CLAIM: The R7a stage classed M09 (the condition replaced with true, so every dropped candidate is rolled back beside limits) as equivalent, on the ground that 'a dropped place's reading reports nothing but guards'. That ground is false. For a dropped candidate whose dictionary declares a cross-reference stream, TryParseNumberedAt kept the parse's syntax.* and stream.* reports. M09 changes what is reported, and it passed the whole suite: it is a surviving mutant, i.e. a test gap.
EVIDENCE: M09 applied on head, rebuilt, and ZzR8uLostTests run:
- Control badW-repeated-key: opening no longer gives 'Warning syntax.key-repeated at 1508: The dictionary gives the key /W more than once; ...', which unmutated head gives.
- Control badW-length-wrong: loses 'Warning stream.length-invalid at 1554: The stream declared 3 bytes but ended after 7.'
The stage's report says M09 passed the whole suite (10,344 tests).
FIX: Reclassify M09 as a survivor and kill it with a test. Shape: a stream of no /Type that gives /W [1 -4 2] twice, near a /Prev offset. It is dropped and keeps syntax.key-repeated, once. Write it together with the fix for undecoded-drop-loses-parse-reports.
--- REFUTER: The finding holds. The R7a stage's ground for calling M09 equivalent ('A dropped place's reading reports nothing but guards') is false.

At a relocation candidate, TryReadXRefStream parses with keep = DeclaresOnlyCrossReferenceStream (PdfFileReader.cs ~:6028-6037). An object of no /Type that gives a /W passes that test, so TryParseNumberedAt runs KeepPending and its syntax.* and stream.* reports go into the document's diagnostics. If that stream is then Malformed for any reason other than undecodable data (a negative /W, for example), it is not TypedXRef. The candidate loop drops it at :5003-5020 and the reports stay.

M09 (`if (attempt.Fault == DataUndecoded)` turned into `if (true)`) rolls them back beside limits. The loss is permanent: RollBackBesideLimits removes the entry from _diagnostics, but IsKept's _syntaxReported set (:5824) still holds it. Every later reading of the same bytes is therefore deduplicated and never reports it again, which breaks invariant 5 in a way anyone can observe.

At head 012e13d the mutant still survives everything in AdCodicem.Pdf.Tests, including the tests R7b and R7c added after the stage measured it. Every test change since 092b772 is in that project. So this is a test gap under ADR 53, not an equivalent mutant.

Nothing in decisions.md, r1-findings.md to r7-findings.md, residuals-impl.md or the R7 open points records it. Minor: a survivor misclassified as equivalent, with no wrong behavior at head. Two caveats:
- My shape and offsets differ from the reviewer's (1508/1554), but the effect is the same.
- In my probe the same rollback that M09 extends already loses syntax.key-repeated, with no mutant, when the dropped stream's data is undecodable (/Foo plus a repeated /W). That is the separate finding undecoded-drop-loses-parse-reports. Its fix may change what M09 means.
REFUTER EVIDENCE: Probe ZzR8RefM09Tests (saved at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r8ref-ZzR8RefM09Tests.cs.txt), built on origin/ccr-6d6c0ee6-dqm4lm-188 = 012e13d. It uses HybridFiles.PrevNamingAStreamBesideTheOldestTable(string.Empty, widths: ...): oldest table at 1459, object 4 at 1675, /XRefStm named at 1647.

Unmutated head (sections in every row: /XRefStm NamedAgain 1647, /Prev Relocated 1459; object 7 = "old seven"):
- widths "[1 -4 2] /W [1 -4 2]":
  - `Warning syntax.key-repeated at 1707: The dictionary gives the key /W more than once; the last value given is kept.`
  - `Repair xref.offset-adjusted at 1459 (/XRefStm ... 188 bytes before it)`
  - `Warning xref.hybrid-stream-lost at 1647`
  - `Repair xref.offset-adjusted at 1459 (/Prev ... 216 bytes before it)`
- widths "[1 -4 2]" with /Length 3 in place of 7: `Warning stream.length-invalid at 1751: The stream declared 3 bytes but ended after 7.`, then the same three reports.

With M09 applied (one line, `if (attempt.Fault == DataUndecoded)` -> `if (true)`) and rebuilt:
- the first shape keeps only the two xref.offset-adjusted reports and xref.hybrid-stream-lost; syntax.key-repeated is gone;
- the second shape loses stream.length-invalid.

Control widths "[1 4 2] /W [1 4 2]" (object 4 reads, /Prev Read 1675, object 7 = "new seven") gives `Warning syntax.key-repeated at 1706` under both builds.

Whole AdCodicem.Pdf.Tests under M09 at head: total 7752, succeeded 7727, skipped 20, failed 5. The 5 failures are the probe's own Assert.Fail rows (`5 failed AdCodicem.Pdf.Tests.ZzR8RefM09Tests.ZzR8RefM09Probe`), so no existing test kills M09. r7a/mutate.log records `M09 survived` on 2128 related tests, and the stage report says the whole suite (10,344) passed.

Same probe with /Filter /Foo and a repeated valid /W, unmutated head: no syntax.key-repeated at all (the rollback under the original condition loses it). That is the separate undecoded-drop finding.

Source and probe restored or removed afterwards.
REFUTER FIX: Reclassify M09 as a survivor and kill it with a test. Put it in CrossReferenceChainTests beside An_xrefstm_goes_on_past_its_table_s_prev_where_the_prev_names_a_stream_of_no_type_whose_filters_decode_nothing_of_it, for example 'A_stream_of_no_type_the_search_near_an_offset_drops_keeps_what_its_reading_reports_once'. Use HybridFiles.PrevNamingAStreamBesideTheOldestTable(string.Empty, widths: "[1 -4 2] /W [1 -4 2]"). Assert:
- the sections (/XRefStm NamedAgain at Named, /Prev Relocated at Oldest);
- the whole code list: SyntaxKeyRepeated, XRefOffsetAdjusted, XRefHybridStreamLost, XRefOffsetAdjusted, with the key-repeated report inside object 4's dictionary.

Optionally add a row whose /Length is short, for stream.length-invalid. The test must fail under M09 and pass at head; both hold in the probe.

Write it with the fix for undecoded-drop-loses-parse-reports. If that fix narrows the rollback to the decoding's own reports (for example, the mark taken past the object's parse, or the syntax set restored with the entries), re-run M09 on the new code. A drop of data that decodes runs no decoding, so M09 may then become truly equivalent; class it with that reason if so. Correct the R7a stage's record ('2 equivalent') in the batch's ADR 53 tally. No maintainer decision is needed.

######## [6] lens=undecoded key=undecoded-list-omits-names-none sev=nit -> verdict=confirmed sev=nit maint=False
LOCATION: docs/website/docs/reference/validation-rules.md:54; docs/website/docs/concepts/lazy-reading.md:113-114
CLAIM: Both pages list data its filters decode nothing of as data 'left at a filter the library does not know, or by Flate data corrupt before its first byte decodes'. They leave out an element of /Filter that names no filter, and a /Filter that is neither a name nor an array. Both give the same fault. threat-model.md:442 names them, and diagnostics.md:112 covers them through filter.unsupported.
EVIDENCE: ZzR8uConsTests cons-null-element (/Filter [null]) on head:
- 'Warning filter.unsupported at 1767: Element 0 of the /Filter entry is null, which names no filter; decoding stopped there.'
- 'Warning xref.section-unreadable at 1650: ... cannot be read: its data could not be decoded. ...'
- Validator: 'Error xref.section-malformed ... its data could not be decoded.'
The pipeline probe gives undecoded=True for /Filter 5, << >> and (FlateDecode).
FIX: Add 'at an element of /Filter that names none, or at a /Filter that is neither a name nor an array' to both lists, worded as threat-model.md:442 words it.
--- REFUTER: The finding reproduces on 012e13d. PdfFilterPipeline.cs:297 sets `undecoded` for FilterChainEnd.Unsupported, NotAName and NotAFilterEntry, and the reader then makes the stream Malformed with the fault 'its data could not be decoded'. So an element of /Filter that names no filter, and a /Filter that is neither a name nor an array, both hold no rows. Three texts say so: threat-model.md:442, the remark at PdfFileReader.cs:5973 and the XML doc of the `undecoded` parameter. reference/diagnostics.md and concepts/diagnostics.md cover the same cases through 'left where `filter.unsupported` says decoding stopped'. Two pages do not: validation-rules.md:54 (the xref.section-malformed row) and lazy-reading.md:113-114. Each gives the cause as a closed two-item list, 'left at a filter the library does not know, or by Flate data corrupt before its first byte decodes'. A null, an integer, a dictionary or a string is not 'a filter the library does not know', and the library's own filter.unsupported messages for these cases do not call it one ('names no filter', 'neither a name nor an array'). So a reader checking a stream with /Filter 5 or /Filter [null] against these two pages would not find the case. The R7d docs stage noted the wording in r7-fix-result.json, deviations[8]. Neither decisions.md nor the round 8 brief lists it as accepted, so it is not settled. No issue (#171, #221, #289, #295, #331, #323, #263, #52) holds it. The code is right, the general clause 'data its filters decode nothing of' arguably still covers the cases, and only the gloss is short. So it stays a nit. A related omission, not in the finding: the private summary of DataUndecoded at PdfFileReader.cs:7021-7022 gives the same two-item gloss.
REFUTER EVIDENCE: Probe built on 012e13d: LostStreamFiles.ThreeRevisions("/Type /XRef /Size 9 /Index [4 2 7 1] /W [1 4 2] /Filter " + filter, written: 21 x 'x'), run through PdfDocument.Open and PdfValidator. Every filter gave the stream at 1650 the state 'Malformed fault=its data could not be decoded handedOn=True', the sections at 200 and the newest one 'Read', and the validator 'Error xref.section-malformed at 1650: The cross-reference stream at offset 1650 cannot be read: its data could not be decoded.' The filter.unsupported message for each:
- [null]: 'Warning filter.unsupported at 1767: Element 0 of the /Filter entry is null, which names no filter; decoding stopped there.'
- [5]: 'Element 0 of the /Filter entry is an integer, which names no filter; decoding stopped there.'
- 5: 'The /Filter entry is an integer, neither a name nor an array; the data was left as it is.'
- << >>: '... is a dictionary, neither a name nor an array ...'
- (FlateDecode): '... is a string, neither a name nor an array ...'
- /Foo (control): 'The filter /Foo is not supported; decoding stopped there.'
Each case was then followed by 'Warning xref.section-unreadable at 1650: The cross-reference stream /Prev names at offset 1650 is there but cannot be read: its data could not be decoded. The chain goes on through its /Prev; ...'.
The texts:
- validation-rules.md:54: '— left at a filter the library does not know, or by Flate data corrupt before its first byte decodes: its data `could not be decoded`, the finding says ([#221]) —'
- lazy-reading.md:113-114: '— left at a filter the library does not know, or by Flate data corrupt before its first byte decodes —'
- threat-model.md:442: '— left at a filter the library does not know, `/Fl` among them, at an element of `/Filter` that names none or at a `/Filter` that is neither a name nor an array, or by Flate data corrupt before its first byte decodes —'
FilterTests.Data_is_said_undecoded_only_where_... pins `undecoded`=true for '[/ASCIIHexDecode 5]' and '5'. The probe file was removed afterwards; the worktree is clean.
REFUTER FIX: In validation-rules.md:54 and lazy-reading.md:113-114, use threat-model.md:442's list: '— left at a filter the library does not know, `/Fl` among them, at an element of `/Filter` that names none or at a `/Filter` that is neither a name nor an array, or by Flate data corrupt before its first byte decodes —'. The other option is the diagnostics pages' wording, '— left where `filter.unsupported` says decoding stopped, or by Flate data of which nothing decodes —'. Make the same change to the private DataUndecoded summary at PdfFileReader.cs:7021-7022: 'left at a filter the library does not know, at an element of /Filter that names none or at a /Filter that is neither a name nor an array, or by one that could decode none of what it was given'. This is wording only, so the maintainer has nothing to decide.

######## [7] lens=own-stream key=first-revision-stream-deferred sev=major -> verdict=narrowed sev=major maint=True
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:4924 and :7053 (MayBeTablesOwn); docs/website/docs/reference/validation-rules.md:60
CLAIM: Under R7-Q2, a stream whose dictionary gives no /Prev 'may be the table's own'. That test also catches the first revision's cross-reference stream, which gives no /Prev by the standard, and any revision's stream that lacks one. When a hybrid table's /Prev misses such a stream by a few bytes, the stream is passed over for any other section within reach: the /XRefStm's own stream, a table nothing names, or an older table. This happens even when the /XRefStm's stream lies outside the 512-byte window. Main and d3ef236 take the stream nearest-first and read the chain right. The texts do not state this consequence. validation-rules.md:60 limits a false hybrid-stream-wrong to a /Prev that 'comes to that stream before any other section'. In shape (a) the /Prev came to the first revision's stream first.
EVIDENCE: Generated files r8own/gen/g001167.pdf, g002927.pdf and g001038.pdf, default limits, same output in both read orders.
(a) g001167: X0 (9 0 obj /Type /XRef, no /Prev) is at 193. The table's /Prev is 187 and its /XRefStm is 575, stream 12 with /Prev 0000000193.
- main and d3ef: '/Prev named=187 at=193 Relocated Stream', offset-adjusted 'found 6 bytes after it', only 'Warning xref.section-shifted offset 193'.
- head: '/Prev named=187 at=575 Relocated Stream' and 'offset-adjusted @575 ... found 388 bytes after it', then 'Warning xref.hybrid-stream-lost @575', 'Error xref.hybrid-stream-wrong offset 1395' and 'section-shifted offset 575 ... 388 bytes after it'. repairedAfter=True; 4='four r2' arrives through a rebuild.
(b) g002927: X0 is at 193 with no /Prev, and a table nothing names is at 435. The /XRefStm is 827, outside the window, and the table's /Prev is 93.
- main and d3ef: relocated to 193, 'found 100 bytes after it', no repair.
- head: '/Prev named=93 at=435 Relocated Table', 'found 342 bytes after it'. 'Repair xref.rebuilt' at opening, 'Error file.root-invalid', and 6='six stale'.
(c) g001038: T0 is at 193. X1 (rev1, no /Prev) is at 455, the /Prev is 444, and the /XRefStm is 1255, outside the window.
- main and d3ef: X1 'found 11 bytes after it', then a rebuild at opening; 7='seven r1'.
- head: '/Prev named=444 at=193 Relocated Table', 'found 251 bytes before it', no repair. 7=null and 13=null, and the validator gives only 'section-shifted offset 193': revision 1 is lost in silence.
Count over the default runs: 'target is a stream giving no /Prev, head takes another place, d3ef and main take the target' covers about 60 of the 3,000 files.
FIX: This needs the maintainer, as a consequence of the decided rule. A suggested refinement: when the /XRefStm's stream S gives a /Prev naming exactly a candidate in the window, that candidate is S's previous section, so do not defer it. Defer other streams that give no /Prev only where S itself looks like a previous revision's stream, that is S gives a /Prev naming another offset (the round 7 [20] shape). Where S gives no /Prev, or gives the table's, S is the table's own and nothing else near can be. Each of (a), (b), (c) and r8own/dense-*.pdf then reads as on main, while the [20] shapes and shape A keep their outcome. Whatever is decided, state the consequence in validation-rules.md:60, threat-model.md:432-434, lazy-reading.md:196 and the TryRelocateXRefSection remarks, and pin the three shapes.
--- REFUTER: The finding holds. The code does what R7-Q2 says: MayBeTablesOwn (PdfFileReader.cs:7053) defers every stream candidate whose dictionary gives no /Prev, wherever the table's /XRefStm read a stream (:4917-4925, :4938, :6027). The first revision's cross-reference stream gives no /Prev, as Table 17 has it. So when a hybrid table's /Prev misses that stream by a few bytes, the stream is passed over for any other place within reach. Main and d3ef236 take it first and read the file right.

The consequence is not on the brief's accepted list. The accepted list has the scope of the deferral (inside or outside the window), its order, and the 'table before' row, where the /Prev is written a few bytes into the stream the /XRefStm read. It does not have the right section being passed over. No round, no decision, no R7b open point and no open debt holds it (#321 and #330 are other shapes).

The texts state the rule, not this consequence. lazy-reading.md:196, threat-model.md:432/434 and diagnostics.md:115 all say that 'any stream near the offset whose own dictionary gives no /Prev ... is tried once every other place was'. None says that such a stream may be the very section the /Prev means, or what follows from that. validation-rules.md:60 gives the false hybrid-stream-wrong only where 'the table's /Prev, written a few bytes off, comes to that stream before any other section'. In shape (a) the /Prev is written 6 bytes off the first revision's stream and about 390 bytes off the stream the /XRefStm read. The text is literally true only if 'comes to' is read in the search's order, after the deferral, and it misleads. The MaxRelocationCandidates summary (:126-133) says 'with the section the nearest candidate', which no longer means it is tried first.

Narrowed on four points:
1. The /XRefStm's own stream beats the right section only where its own /Prev names another offset than the table's. With no /Prev it is dropped, and with the table's /Prev it is deferred behind the nearer first-revision stream. Both read right on head.
2. Shape (c) is weaker evidence. Its middle revision's stream wrongly gives no /Prev, so main does not read that chain right either: it rebuilds at opening, with the Error file.root-invalid of #260. Head instead takes the older table. Revision 1 is then skipped, with only offset-adjusted and section-shifted reported, so 7 and 13 read null. That outcome is different and silent, not clearly worse.
3. The dense files fail through the attempt budget, not through another section. The passed-over first visit spends an attempt, as threat-model.md:434 states. With 31 other headers in the window, the 32 attempts run out before the right stream's retry.
4. The proposed refinement does not bring (c) back to main's reading. In (c) the stream the /XRefStm read gives /Prev 193, which is the older table, so the refinement takes that table, as head does.

Every result below is the same in both read orders on head.
REFUTER EVIDENCE: I wrote my own probe and builder and built head 012e13d, d3ef236 and main 21a0298 in my own trees, at default limits. The probe is saved at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r8ref-own/ZzRefOwnFirstRevTests.cs.txt. Outputs are in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r8ref-own/out-head.txt, out-d3ef.txt and out-main.txt. Generated files are in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r8ref-own/out/.

**My shape (a): nearown-6-150-x0.pdf, 816 bytes.**
- The file: X0 (object 9, /Type /XRef, no /Prev) at 167, with startxref 167. An update writes 3 and 10, its own stream 12 at 547 giving /Prev 167, and a table at 685 giving /Prev 161 and /XRefStm 547.
- Main: '/Prev named=161 at=167 Relocated Stream', 'Repair xref.offset-adjusted @167 ... found 6 bytes from there', only 'Warning xref.section-shifted offset 167'. d3ef236 is the same ('6 bytes after it').
- Head:
  - '/XRefStm NamedAgain', then '/Prev named=161 at=547 Relocated Stream', then '/Prev named=167 Read Stream';
  - 'offset-adjusted @547 ... found 386 bytes after it';
  - 'Warning xref.hybrid-stream-lost @547: ... names offset 547, the section its /Prev names too';
  - 'Repair xref.rebuilt' on the first miss (after=True);
  - validator: 'Error xref.hybrid-stream-wrong offset 685' and 'section-shifted offset 547 ... 386 bytes after it'.
  - Objects served are the same.
- Controls that read right on head: pad 0 gives the same failure at +234. Stream 12 giving no /Prev, or the table's /Prev 161, reads right, and so does stream 12 lying 700 bytes on, out of reach.

**Reviewer's files, run on my builds; results match the finding.**
- g001167: head takes 575 (+388), hybrid-stream-lost, a rebuild, and Error hybrid-stream-wrong offset 1395. Main and d3ef take 193 (+6).
- g002927: head '/Prev named=93 at=435 Relocated Table', 'found 342 bytes after it', open repaired=True, 6="six stale", 'Error file.root-invalid object 1 0'. Main and d3ef: 193 (+100), no repair, 6=null.
- g001038: head '/Prev named=444 at=193 Relocated Table', '251 bytes before it', no repair, 4=7=13=null, validator only section-shifted. Main and d3ef: 455 (+11), xref.rebuilt at opening, 7="seven r1", and also 'Error file.root-invalid' (pre-existing, #260).
- dense-20-9.pdf (X0 5 bytes off, 31 other headers within 512 bytes):
  - head: '/Prev named=557 at=557 NotFound Unknown', 'Warning xref.section-missing ... not there, nor near it', rebuilt at opening, and 'Error xref.section-not-found offset 557 ... nor within 512 bytes of it';
  - main and d3ef: 'found 5 bytes', no repair.
  - My own dense shapes with up to about 30 other headers read right on head. The threshold is the others plus 2 visits of X0 going past 32.

**Code.** At :4986, `_relocationCandidatesTried++` is spent before the passed-over read, and the place is then re-added at :5018. The texts quoted are at validation-rules.md:60, lazy-reading.md:194-198, threat-model.md:432 and :434, and diagnostics.md:115.
REFUTER FIX: This is a design choice for the maintainer, since it qualifies R7-Q2.

**Recommended refinement.** It keeps the [20] shapes and shape A as they are. Judge the other streams by the stream S that the /XRefStm read:
- Where S gives no /Prev, or the table's /Prev, S is the table's own stream. A revision has only one, so no other stream near the offset can be the table's own: defer nothing else (S itself is dropped or deferred, as now). This fixes the dense files.
- Where S gives a /Prev that names exactly a place in the window, that place is S's previous section: try it in its nearest-first place. This fixes (a) and (b).
- Defer other streams that give no /Prev only where S's /Prev names an offset that holds no candidate in the window. That is the [20] shape, the previous revision's stream named by the /XRefStm.
- Shape (c) then still reads the older table that S's /Prev names. That should be stated.

**Optionally.** Do not spend an attempt on a deferred stream's first visit, which is only an object parse, so that the 32-attempt budget cannot run out before the stream's retry. Otherwise, state that it can, and that it then reports section-missing or section-not-found 'nor within 512 bytes of it'.

**Pin these shapes:**
- (a) as my nearown file: the first revision's stream gives no /Prev, and the update's own stream gives /Prev at the stream's true offset, within reach;
- (b) an unnamed table in reach;
- the dense budget shape;
- the s1Prev none/table controls.

**If the rule is kept as is,** state the consequence in validation-rules.md:60, lazy-reading.md:196, threat-model.md:432/434, diagnostics.md:115, the TryRelocateXRefSection remarks and the MaxRelocationCandidates summary. A stream near the offset that gives no /Prev includes the first revision's stream, which gives none by Table 17. It is passed over for any table, or any stream with another /Prev, within 512 bytes. That gives:
- a section taken hundreds of bytes off;
- a false hybrid-stream-lost and Error hybrid-stream-wrong;
- a rebuild at opening with file.root-invalid, or an older table read with a revision skipped in silence;
- with enough headers near the offset, section-missing and section-not-found while the section is a few bytes away.

######## [8] lens=own-stream key=deferred-stream-budget sev=major -> verdict=confirmed sev=major maint=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:4960 and :5018; docs/threat-model.md:434
CLAIM: A stream that may be the table's own is retried only after every other place in the window has been tried. Each of those places spends an attempt, the pass-over spends one and the retry spends another. In a window holding 31 other object headers, MaxRelocationCandidates is spent before the search comes back. The section, 5 bytes away, is then reported missing 'not there, nor near it'. The validator adds a false Error section-not-found 'nor within 512 bytes of it', and the index is rebuilt at opening. Main and d3ef236 find it at their first attempt. The budget is shared across searches, so a newer /Prev's search that spent 28 attempts loses it the same way. threat-model.md:434 says only that such a stream 'spends an attempt each time it is read'.
EVIDENCE: Dense(20, 9) in r8own/ZzR8OwnShapesTests.cs, file r8own/dense-head.txt.dense-20-9.pdf (2,680 bytes). X0 (no /Prev) is at 562 with 22 small objects before it and 9 after, all within 512 bytes. A hybrid table names /Prev 557 and its own stream at 2392 through /XRefStm.
- main and d3ef: 'S /Prev named=557 at=562 Relocated Stream', 'offset-adjusted @562 ... found 5 bytes after it', repairedAtOpen=False.
- head: 'S /Prev named=557 at=557 NotFound Unknown', 'Warning xref.section-missing @557: ... is not there, nor near it', 'Repair xref.rebuilt', 'Error xref.section-not-found offset 557: ... is not there, nor within 512 bytes of it'. repairedAtOpen=True.
Same for Dense(20,10) and Dense(20,15).
Budget(28..31): a newer classic table's /Prev search spends 28 to 31 attempts first. head then gives 'S /Prev named=162 at=162 NotFound' plus section-not-found in every row. main and d3ef relocate to 167, 'found 5 bytes after it'.
FIX: Count a place that was passed over once, at its first reading. Once the window's places are done, or once the bound stops the search, retry the passed-over places nearest first without counting them again. They are at most the places already counted, so reads stay under twice MaxRelocationCandidates. Add the Dense row as a test, and say in threat-model.md:434 what a retry costs.
--- REFUTER: The finding holds as stated, and one consequence is worse than it says.

How it happens:
- TryRelocateXRefSection counts a stream that may be the table's own once when it is passed over (the increment at PdfFileReader.cs:4960 comes before TryReadXRefSection).
- It then re-adds the stream at the end of the list (:5016-5019), and the retry counts again.
- R7-Q2 puts that stream after every other place in the window, and each of those spends one of the 32 attempts (MaxRelocationCandidates). The budget covers the whole document.
- So a window holding 31 other object headers, or a budget that earlier searches have mostly spent, uses up all 32 before the retry. The section is then reported missing, though it lies 5 bytes away and is the nearest candidate. main 21a0298 and d3ef236 try it first and find it.

Worse than claimed: when the hybrid update frees an object that the lost stream places, the rebuild at opening serves the freed object where main and d3ef236 serve null. This is a wrong object served, the same kind of consequence that made round 7's [20] major.

What was decided and what was not:
- R7-Q2 decided only the order ("tried after every other place").
- Counting a retried place a second time was the R7b stage's own choice. It is documented in the remarks at :4843-4844 and in _relocationCandidatesTried's summary, but its consequence is not.
- The reason MaxRelocationCandidates gives at :127-130 ("Real files miss by a few bytes, with the section the nearest candidate") no longer holds for this class of stream: the nearest candidate is now tried last.
- threat-model.md:434 and lazy-reading.md:196-197 do not say the deferred stream can lose the search to the budget.

Is it known or accepted?
- No issue holds it (#156, #188, #171, #289, #295, #331, #323, #263 and #52 do not, nor does any other open debt).
- No earlier round's finding holds it. r3 and r4 discussed what reads the budget bounds, not a section lost to the order.
- The brief does not list it among the accepted consequences. "Deferred streams nearest-first among themselves" is a different point.

On realism: it needs a dense window (about 31 headers within 512 bytes on either side) or several earlier misses in the same document. That is rare, but the severity scale goes by consequence, and main read these files whole.
REFUTER EVIDENCE: I rebuilt the reviewer's builders myself. My probe is /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r8refbudget/ZzR8RefBudgetTests.cs.txt, with outputs head.txt*, d3ef.txt*, main.txt* and patched.txt* in the same folder. It ran on three Release builds: 012e13d, d3ef236 and 21a0298.

**Dense(20,9)**, 2,680 bytes: x0=562, /Prev 557, /XRefStm 2392.
- head: `S /Prev named=557 at=557 NotFound Unknown`; `Warning xref.section-missing @557: ... is not there, nor near it`; `Repair xref.rebuilt`; `Information object.redefined`; repairedAtOpen=True. Validator: `Error xref.section-not-found offset 557: ... is not there, nor within 512 bytes of it`.
- d3ef236: `S /Prev named=557 at=562 Relocated Stream`, `xref.offset-adjusted @562 ... found 5 bytes after it`, repairedAtOpen=False.
- main: the same, worded '5 bytes from there'.
- Dense(20,10) and Dense(20,15) give the same split.

**Sweep on head, by other headers in the window** (the count is approximate):
- 22, 26, 28, 29 or 30 (after=0, 4, 6, 7, 8): relocated to 562, no repair.
- About 31 or more (after=9, 12): NotFound and a rebuild.
- So the cut is exactly 1 pass-over + 31 others = 32 attempts, and the retry is the 33rd.

**Budget(28..31, none) and Budget(30, tablesPrev):**
- head: `S /Prev named=162 at=162 NotFound`, plus section-missing, a rebuild and Error section-not-found in every row.
- d3ef236 and main: `named=162 at=167 Relocated Stream`, 'found 5 bytes after it'.

**My worse variant, DenseFreed(20,9):** table T1 also writes `45 1` with a free row, and X0 places object 45 as "(fortyfive)".
- head: repairedAtOpen=True, `45="fortyfive"`, and Error section-not-found.
- d3ef236 and main: Relocated to 569, `45=null`, Warning section-shifted.
- With after=8, head also gives 45=null.

**The fix works.** I applied a trial patch: a place passed over is counted once, the retry is not counted again, and once the bound is reached the search jumps to the retries instead of breaking. With it, every Dense, Budget and DenseFreed row relocates as main does (45=null, no repair). CrossReferenceChainTests, CrossReferenceRuleTests, HostileInputTests, HeaderOffsetTests, DocumentReaderTests, WindowEdgeTests and GuardCutValueTests ran 2,224 tests, all passed. No existing test pins the double count. The patch was reverted, the probe removed, and the scratch worktrees deleted.
REFUTER FIX: In TryRelocateXRefSection, count each place once, at its first reading:
- A stream re-added after it was read and passed over (attempt.MayBeTablesOwn) is not counted again when it is retried.
- The /XRefStm's own stream, re-added without being read, is still counted at its first reading.
- When the bound is reached on a place of the window, skip to the retries instead of `break`. A retry already counted is still tried, and one not yet counted stops at the bound.
- Reads stay under 2 x MaxRelocationCandidates windows a document, since each uncounted retry is a place already counted.

The trial patch did this with a HashSet<long>, allocated only when a stream is passed over (or use a parallel flag list). It needs:
- 16 lines in total;
- ADR 53 mutants over those lines;
- new tests that fail on 012e13d: the Dense row at 31 headers (sections and codes, no rebuild), a Budget row (a newer search spends 28 attempts), and the freed-object row (45 null).

Texts to update:
- _relocationCandidatesTried's summary and the remark at :4843-4844 ("counts again" becomes "counted once, at its first reading").
- MaxRelocationCandidates' summary: a stream tried last is still tried once the others have spent the bound.
- threat-model.md:434: what a retry costs, at most one more window for each place passed over.

This stays within R7-Q2's order, so no maintainer question is needed. If the fixer would rather keep reads at 32, the choice becomes the maintainer's: keep the loss and document it in threat-model.md:434 and in the MaxRelocationCandidates remark (a deferred stream can lose the search to the document's budget, and the section is reported missing and the index rebuilt), or file it as debt.

######## [9] lens=own-stream key=cut-dictionary-passed-over sev=major -> verdict=narrowed sev=major maint=True
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:6537-6561 (TryParseNearHybridPrev keep) and :7053
CLAIM: The keep predicate judges MayBeTablesOwn on a dictionary that MaxTrailerLength cut. A /Prev written past the cut is unseen, so a stream that declared /Type /XRef and whose /Prev names another offset is passed over as 'may be the table's own'. TryParseNumberedAt then reports nothing, the guard included. An older section farther off is taken, and the stream's rows are lost in silence. So a guard's cut ends up reported as nothing at all, against ADR 34 and #281 ('a section that cannot be read is the section all the same where what is there declared itself one ... before a guard's cut'). Main and d3ef236 find the stream cut and report the guard.
EVIDENCE: CutPrev(prevLast: true) in r8own/ZzR8OwnShapesTests.cs, file r8own/cut2-head.txt.cutprev-True.pdf, MaxTrailerLength 100. T0 is at 142. X1 at 440 is '13 0 obj << /Type /XRef ... /ID [...] /Prev 142 >>', with its /Prev past byte 100. The hybrid table names /Prev 460 and /XRefStm 2011.
- main and d3ef: '/Prev named=460 at=440 Malformed Stream', 'Warning limit.trailer @440: ... Raise PdfReaderLimits.MaxTrailerLength', 'Information xref.checked-in-part offset 440'.
- head: '/Prev named=460 at=142 Relocated Table', 'offset-adjusted @142 ... found 318 bytes before it', and only 'Warning xref.section-shifted offset 142'. No limit report anywhere; 7=null.
Controls: with default limits all three read X1 and 7='seven r1'. With /Prev written early in the dictionary (prevLast: false) head matches d3ef.
FIX: Do not pass over a stream whose reading the guard cut, because its /Prev is unknown. Keep it in its nearest place, so it is read, found cut and reported as the guard's, as on d3ef. This changes the row A_place_the_search_near_a_hybrid_table_s_prev_passes_over_or_finds_no_section_at_leaves_nothing_of_its_reading("stream 11", 300) at CrossReferenceChainTests.cs:5598, which pins a silent guard on a cut stream: decide that row with the maintainer. Correct TryParseNearHybridPrev's summary ('a guard its window reached included') and the XRefSectionRecord.MayBeTablesOwn summary.
--- REFUTER: The finding holds. I rebuilt it at 012e13d, d3ef236 and 21a0298. TryParseNearHybridPrev's keep predicate calls MayBeTablesOwn on a dictionary that MaxTrailerLength cut. TryParseNumberedAt (PdfFileReader.cs:8053) runs keep in its cut branch before ReachLimit. So when the stream's /Prev lies past the cut, the stream reads as giving none and is passed over, and no limit.* is reported. The search then takes an older section farther off. That is what #281 and the TryRelocateXRefSection comment set out to prevent ('so that no older section farther off stands for its lost rows in silence'). It also breaks ADR 34: the guard's cut changes what is served, and the only reports blame the file. R7-Q2 did not decide this case, the round-8 accepted list does not hold it, and neither does any open issue: #328 is about a chain trailer's own /Prev, #289 and #295 about white space. Two narrowings. (1) It needs another place in the 512-byte window that reads or is taken. With the older table 700 bytes back, head tries the stream again with nothing passed over and reports limit.trailer and xref.checked-in-part, as d3ef and main do. (2) Where the /Prev past the cut is absent, or names the table's own /Prev, raising the limit serves the same objects, and only the guard report is missing. The pinned row ("stream 11", 300) is that case. Where the /Prev names another offset, which is the finding's shape, the guard changes what is served and adds a false relocation. It needs a stream dictionary longer than MaxTrailerLength (64 KB by default) with its /Prev past the bound. That is rare, but under the brief's rubric this is a missing guard report, the guard's cut charged to the file, and a regression against both d3ef and main: major.
REFUTER EVIDENCE: The probe is /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r8ref-cutown/ZzR8RefCutOwnTests.cs, the reviewer's CutPrev shape with variants. Outputs are out-head.txt, out-d3ef.txt, out-main.txt and out-fix.txt in the same folder. In the file, T0 is at 142, X1 '13 0 obj << /Type /XRef ... /ID [...] /Prev 142 >>' at 440 (dictionary 168 bytes long), and the hybrid table gives /Prev 460 and /XRefStm 2011.
- x1Prev=t0, MaxTrailerLength 100:
  - head: sections startxref Read, /XRefStm Read, '/Prev at=142 Relocated Table'. One diagnostic, 'Repair xref.offset-adjusted @142: ... found 318 bytes before it.' Validator: 'Warning xref.section-shifted offset 142 ... starts 318 bytes before it', and file.size-wrong at 231. Objects: 1 and 2 are served through T0, 7=null, 13=null. No limit.* report anywhere.
  - d3ef236: '/Prev at=440 Malformed Stream', 'Warning limit.trailer @440: ... Raise PdfReaderLimits.MaxTrailerLength to read past it.', and 'Information xref.checked-in-part offset 440'. Objects 1, 2 and 7 are null.
  - main: the same, worded '-20 bytes from there'.
- Controls:
  - MaxTrailerLength 65536: head, d3ef and main all read X1 ('/Prev at=440 Relocated Stream', then T0), 7="seven r1".
  - /Prev written first (t0early): head equals d3ef.
  - T0 placed 700 bytes back (outside the window), at limit 100: head equals d3ef, with limit.trailer and checked-in-part at 1040.
  - x1Prev none or 'tables' at limit 100: head takes T0 silently, and limit 65536 gives the same objects. Only the guard report differs.
- Fix check: I patched head so the keep predicate is told the parse was cut, with mayBeOwn = !cut && MayBeTablesOwn(...). Every cut row then equals d3ef236, and every uncut row equals head, so R7-Q2 is kept. Over the CrossReference*, HybridFile*, ReaderLimits* and DocumentReader* classes (1,815 tests), the only failure is CrossReferenceChainTests.A_place_the_search_near_a_hybrid_table_s_prev_passes_over_or_finds_no_section_at_leaves_nothing_of_its_reading(near: "stream 11", pad: 300). That row pins the silence. The patch, the probe and the scratch worktrees have been removed.
REFUTER FIX: The maintainer must make a design choice, because the change undoes a pinned row.
Recommended (a): do not judge MayBeTablesOwn on a dictionary the guard cut, whose /Prev cannot be told.
- Pass keep whether the parse was cut, for example a Func<PdfObject, bool, bool>, or a flag set around the cut-branch call at PdfFileReader.cs:8053.
- In TryParseNearHybridPrev, write mayBeOwn = !cut && MayBeTablesOwn(parsed, tablePrevious).
- The stream then keeps its nearest place. It is read, found Malformed with TypedXRef, taken, and reported as the guard's (limit.trailer, xref.checked-in-part), as on d3ef236 and main.
Alternative (b): keep deferring it, but when the search takes another place, report the guard at the deferred cut stream. This keeps the older table's rows but says nothing true about which section the /Prev meant, and needs more code.
Either way:
- Settle the row ("stream 11", 300) at CrossReferenceChainTests.cs:5598 with the maintainer. Under (a) it gives stream 11 Malformed with limit.trailer, the chain ending for the guard.
- Add the finding's shape as a row: /Prev last, MaxTrailerLength 100, beside the default-limits control and the /Prev-first control.
- Correct the texts: TryParseNearHybridPrev's summary ('a guard its window reached included'); the XRefSectionRecord.MayBeTablesOwn summary; the TryRelocateXRefSection remarks ('told by its own /Prev as its dictionary writes it' should add 'as far as a guard lets it be read: a stream whose dictionary a guard cut is not passed over'); and the own-stream clauses in threat-model.md (the #156 row) and lazy-reading.md.

######## [10] lens=own-stream key=undecodable-xrefstm-stream-leaves-own-taken sev=major -> verdict=refuted sev=none maint=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:4924 (scope readInStreamsPlace is { Kind: Stream }) and :3594 (set only for an /XRefStm reading that was not lost)
CLAIM: Take the round 7 [20] shape and give the /XRefStm's stream 10 data its filters cannot decode. Under R7-Q1 that stream is lost and reached, so the /Prev's search passes it over and does not defer the update's own stream 11, which gives no /Prev. Stream 11 is taken. The chain ends there, the oldest table goes unread, and the index is rebuilt at opening. The object the first update freed is then served, and offset-adjusted names stream 11 as the section found. These are exactly the consequences R7-Q2 was decided to remove. Main reads the chain whole. d3ef236 also took stream 11, so this is a shape the decision does not reach, not a round 7 regression.
EVIDENCE: Round7("/Filter /Foo ") in r8own/ZzR8OwnShapesTests.cs, outputs in r8own/cut-{main,d3ef,head}.txt. t0=192, s1=1569, x2=1752, t2=1867.
- main: '/XRefStm named=1569 Read Stream', '/Prev named=1562 at=1569 Relocated', '/Prev named=192 Read Table'; 5=null.
- d3ef: '/Prev named=1562 at=1752 Relocated Stream', rebuilt at opening, 5="five, freed by the first update", Error file.root-invalid.
- head: '/XRefStm named=1569 Malformed Stream', 'xref.section-unreadable @1569 ... its data could not be decoded', '/Prev named=1562 at=1752 Relocated Stream ... found 190 bytes after it', 'Repair xref.rebuilt' at opening, 5="five, freed by the first update" in both orders.
Control Round7(""): head relocates to 1569, 'found 7 bytes after it', reads T0, 5=null.
FIX: Needs the maintainer. One option: let the /Prev's search take what the table's /XRefStm reached and could not read where it declares itself a /Type /XRef stream, as a section found moved and Malformed, with its /Prev handed on (#208). The own-stream deferral then applies there too and the chain reads whole, as on main. Otherwise, state the gap under #156 with this reproducer.
--- REFUTER: The mechanics reproduce. On head, the /XRefStm's stream 10 cannot be decoded, so it is Malformed and reached. readInStreamsPlace stays null (PdfFileReader.cs:3594 sets it only on the read branch), so the /Prev's search passes 1569 over as reached and takes stream 11. The chain ends there, T0 goes unread, and the index is rebuilt at opening.

The consequences the finding gives as major do not follow from that take.

(1) The freed object 5 is served because R7-Q1 refuses the numbers a lost stream declares to the older rows, and the rebuild then serves them. That happens even when the chain is read whole. With the /Prev naming 1569 exactly, head reads T2, then stream 10 (Malformed, its /Prev handed on), then T0, opens with no repair, and still serves 5="five, freed" when 5 is asked first. Head on the finding's file serves exactly the same objects, in both orders: 5 freed, 3 "three new", 4 "four", 1 the catalog. Neither file gives a false file.root-invalid.

(2) "Main reads the chain whole" holds only because main read raw rows under a fake /Filter. R7-Q1 removed that fallback on purpose ("the rebuild catches them"). Where stream 10 is lost for any other fault (a bad /W), main also takes stream 11, relocates the /XRefStm onto it as well, rebuilds at opening and adds a false Error file.root-invalid. On a stream whose Flate data really is corrupt, main serves "three old" and null for object 4, without a word; head serves "three new" and "four". So the class predates the batch: main has it, worse, and d3ef236 behaves exactly like head for a bad /W. R7-Q1 brought undecodable data into it, as decided.

(3) The rule is settled and stated. A lost section the chain reached is never a candidate (#188/#281: lazy-reading.md:192-193, "one there that it could not read though it declares itself a section"; threat-model.md:434). The own-stream deferral is scoped "where it read a stream" (lazy-reading.md:195-196) and to what the /XRefStm "read in the stream's place" (threat-model.md:432). The deferral alone would change nothing here: with no stream 11, the search finds nothing, reports section-missing and rebuilds at opening; and deferred, stream 11 would still be taken last. The proposed fix reverses that documented #188 rule. It is not a gap in how R7-Q2 was implemented.

What is left is report-level, in a file with three faults at once: a wrong /XRefStm, a /Prev 7 bytes off, and undecodable data. offset-adjusted and section-shifted name stream 11 rather than branch (c), and the rebuild comes at opening rather than on the first miss. No text misstates that.
REFUTER EVIDENCE: Probe: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r8ref-undxs/ZzR8RefUndXsTests.cs.txt. It uses the finding's Round7 builder, asks for 5 first (5,3,4,1) and then for 4 first. Outputs are out-head.txt, out-main.txt and out-d3ef.txt in the same folder. Head is 012e13d, main is 21a0298.

A, /Filter /Foo, the finding's file (t0=192 s1=1569 x2=1752 t2=1867):
- Head reproduces the finding: '/XRefStm 1569 Malformed', '/Prev named=1562 at=1752 Relocated', 'found 190 bytes after it', xref.rebuilt, repairedAtOpen=True.
- Objects in both orders: 5="five, freed", 3="three new", 4="four".
- Findings: Error section-malformed @1569 and Warning section-shifted @1752. No file.root-invalid.

D, /Foo with the /Prev at exactly 1569:
- Head: sections '/Prev named=1569 Malformed Stream' then '/Prev named=192 Read Table'.
- Reports hybrid-stream-lost, then section-unreadable 'The chain goes on through its /Prev'.
- repairedAtOpen=False, yet 5-first gives 5="five, freed", 3="three new", 4="four": the same objects as A.

C, /Foo with no stream 11:
- Head: '/Prev named=1562 NotFound', section-missing, xref.rebuilt at opening, the same objects. Without stream 11 the chain still ends.

F, a bad /W [1 -4 2] (also G, /W [1 4]), no filter:
- Main: '/XRefStm named=1569 at=1740 Relocated Stream', '/Prev named=1562 at=1740 Relocated Stream', rebuilt at opening, 'Error file.root-invalid ... No object of the file is a catalog'.
- d3ef236 and head: '/XRefStm 1569 Malformed', '/Prev at=1740 Relocated', rebuilt at opening, no root-invalid.

E, /FlateDecode with corrupt data:
- Main: chain read whole on garbage rows; 3="three old", 4=null and 5="five, freed", in silence.
- Head: rebuilt at opening; 3="three new", 4="four" and 5="five, freed".
- d3ef236: entry-reserved, then stream 11 taken and a false root-invalid.

A on main: chain whole with 5=null, which holds only because the raw rows under /Foo happen to be valid. That is the fallback R7-Q1 dropped.

Texts:
- lazy-reading.md:192-198: 'A section is never found where a section the chain reached starts — one it read, or one there that it could not read though it declares itself a section —' ... 'where it read a stream, any stream near the offset ... is tried once every other place was'.
- threat-model.md:434: 'or there though it could not be read and declares itself one — is no candidate'.
REFUTER FIX: Nothing to fix in this batch. The behavior follows the documented #188/#281 rule, and the class predates the batch: it is worse on main for any stream lost to a fault other than its filters.

If the maintainer later wants a /Prev that falls a few bytes off a lost /XRefStm stream of /Type /XRef to reach that stream, which would mean taking it as found moved and Malformed, with its /Prev handed on under #208, that is a design change to #188's rule for lost reached sections. It is not a fix to R7-Q2, and it could be filed as debt under #188 with probe D (exact offset: chain whole) against A (7 bytes off: chain ends). Even then, object 5 would still come from the rebuild under R7-Q1, not as null as on main.

Optional nit, no fix required: threat-model.md:432's Threat column, 'a /Prev a few bytes off the stream the /XRefStm read whose search took the update's own stream', could add 'read whole' or 'could read'. The defense column already scopes the case to what the /XRefStm read in the stream's place.

######## [11] lens=own-stream key=prev-compared-as-written-loops sev=minor -> verdict=narrowed sev=minor maint=True
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:7053 (MayBeTablesOwn); docs/threat-model.md:433
CLAIM: This is the accepted 'compared as written' deviation, but its consequence is worse than stated. MayBeTablesOwn treats each of these /Prev forms as 'another offset': a real such as 1297.0, a reference to an integer equal to the table's /Prev, a negative offset, and an offset outside the file. The chain itself reads the real as its integer and the reference without loading (ReadSectionOffset, threat-model.md:433). In shape A, the decision's own case (/XRefStm names the table's own stream and the /Prev falls a few bytes into it), that stream is therefore taken. The chain then loops, or ends outside the file, and is rebuilt at opening with Error chain-loop or section-not-found plus Error hybrid-stream-wrong. A freed object is served. d3ef236 read the chain whole there. g001618 shows the real form combined with the first-revision deferral.
EVIDENCE: Default limits:
- g003625 (classic T0 at 193 and T1 at 889; stream 11 at 1135 with '/Prev 20 0 R' where object 20 is 0000001133; table /Prev 1133, /XRefStm 1135). d3ef: '/Prev named=1133 at=889 Relocated Table', no repair, 5=null. head: '/Prev named=1133 at=1135 Relocated Stream', chain-cycle, rebuilt at opening, 5="five r0", 'Error xref.chain-loop offset 1135' and 'Error xref.hybrid-stream-wrong offset 1586'.
- g003835 (/Prev 1297.0, table /Prev 1297, stream at 1266). d3ef: relocated to the table at 1039, no repair. head: relocated to 1266, chain-cycle @1297, rebuilt, Error chain-loop.
- g002426 (/Prev 99999999999). d3ef: relocated to the table at 1762. head: relocated to 2013, 'section-missing ... lies outside the file', rebuilt, 'Error xref.section-not-found', Error hybrid-stream-wrong, 5="five r0".
FIX: Judge the stream's /Prev as the chain reads it, without reporting. A real with no fractional part counts as its integer, which costs nothing. A reference is read with ReadWithoutLoading, which is cached and which the chain reads anyway when it takes the stream. Count a /Prev that names no offset in the file (negative, outside the file, not a number) as handing the chain nothing on, so it may be the table's own. Add shape-A rows with the real, reference and outside-the-file forms.
--- REFUTER: Head behaves as the finding says, and d3ef236 read every one of these files whole. But the claim that the consequence is worse than stated does not hold in shape A, the finding's own case. There, main 21a0298 does the same thing for every /Prev form the finding names (a real, a reference, a negative offset, one outside the file, and a name as well): it takes the stream, the chain loops or ends, the index is rebuilt at opening, and the freed object is served. Main does this even for the forms head now reads right: no /Prev, and a direct integer equal to the table's /Prev. Head's only addition is Error hybrid-stream-wrong, which is accepted consequence (a).

The texts state this outcome:
- threat-model.md:432: 'told by its own /Prev as its dictionary writes it ... a stream whose /Prev then names no older section is read whole all the same, and that /Prev reported as the chain finds it: a loop, an offset outside the file, no offset'.
- PdfFileReader.cs:4834-4840 says the same.
- MayBeTablesOwn's XML doc (:7046-7049) says 'or is no integer, is a /Prev all the same'.

So in shape A this is the accepted deviation. It is a regression against d3ef236, which was never released, and no worse than main.

Two points survive.

(1) One shape where head is worse than main: the reviewer's g001618. The table's /Prev falls 10 bytes short of the first revision's stream. That stream gives no /Prev, as every first revision's does, so R7-Q2 defers it. The /XRefStm's stream gives /Prev 183.0, the table's /Prev written as a real.
- Main: reads the chain whole, no repair.
- Head: takes the /XRefStm's stream 291 bytes off, loops, rebuilds at opening, and adds Error chain-loop and Error hybrid-stream-wrong. The objects served are the same.
- The same file with the /Prev written as the integer 000000000183 reads whole on head.
So here the as-written comparison of a real is what costs. It takes an invalid file: 7.5.8.2 makes this entry direct and Table 17 an integer, and the table's /Prev is also off.

(2) The user pages do not say 'as written', so they misstate the code for these forms:
- lazy-reading.md:132-133 and :196, diagnostics.md:115 and validation-rules.md:60 say 'if that /Prev names the table's'.
- lazy-reading.md:17-18 and threat-model.md:433 say a /Prev written as a reference is read where a section places its object, and a real with no fractional part as its integer.
- A reader of those pages expects a stream whose /Prev is '20 0 R' (object 20 holds the table's offset), or the same offset written as a real, to be tried last. The code takes it at its nearest place.

The finding's third proposal, counting a negative offset, one outside the file, or a /Prev that is not a number as 'may be the table's own', goes beyond R7-Q2's 'names another offset'. That is the maintainer's to decide.

A side observation outside this key: in g001618 with the stream's /Prev written as the integer 193 (another offset, all values direct), head takes the /XRefStm's stream 291 bytes off rather than the first revision's stream 10 bytes off. It gives branch (c), a false Error hybrid-stream-wrong and a rebuild when an object is asked for, where main reads clean. That comes from deferring the first revision's stream for having no /Prev, which no round recorded, and accepted consequence (a) does not cover it: the table's /Prev there is not 'a few bytes into' its own stream.
REFUTER EVIDENCE: Generator /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r8ref-prevform/gen.py writes shape A.
- Layout: T0 at 193 holds the catalog and object 5; T1 at 639 frees 5; stream 11 at 878 is the table's own stream (indexes objects 4 and 11); object 20 holds P; T2 gives /Prev P and /XRefStm 878.
- P is 890 (12 bytes into the stream) or 876 (2 bytes short of it).
- Stream 11's /Prev forms: none, P, 639, '20 0 R', 'P.0', -5, 99999999999, /Foo.
Probe: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r8ref-prevform/ZzR8RefPrevFormTests.cs.txt. Outputs: out-head.txt, out-d3ef.txt, out-main.txt in the same folder.

Shape A, head 012e13d:
- none and P: '/Prev named=890 at=639 Relocated Table', no repair, 5=null.
- '20 0 R' and 890.0: 'Relocated Stream' at 878, 'xref.chain-cycle @890', open repaired=True, 5="five r0". Findings: 'Error xref.chain-loop offset 878', 'Error xref.hybrid-stream-wrong offset 1080' (1079 for the real).
- -5 and /Foo: section-missing 'is not an offset', rebuilt at opening, 'Error xref.section-not-found ... is the integer -5, not an offset'.
- 99999999999: 'Error xref.section-not-found ... lies outside the file'.
- Both P values (890 and 876) give the same.

d3ef236: all 16 files read whole (Relocated Table at 639, then 193 read), no repair, 5=null.

main 21a0298: every form, none and P included, gives 'Relocated Stream' at 878, rebuilt at opening, 5="five r0". There is chain-loop for P, '20 0 R' and P.0, and section-not-found for -5, 99999999999 and /Foo. The only exception is 639, which reads whole. So head equals main for every form the finding names, plus head's hybrid-stream-wrong (accepted consequence (a)).

The reviewer's files on all three trees:
- g003625, g003835 and g002426: head and main both rebuild at opening with 5="five r0". Main gives chain-loop or section-not-found, without hybrid-stream-wrong. d3ef236 reads them whole.
- g001618 is the exception:
  - head: '/Prev named=183 at=474 Relocated Stream', open repaired=True, 'Error xref.chain-loop offset 474', 'Error xref.hybrid-stream-wrong offset 1323'.
  - main and d3ef236: '/Prev named=183 at=193 Relocated Stream', no repair.
  - The objects served are the same on head and main (5="five r0", 7=null).

g001618 variants made by changing only the /Prev bytes (vary1618.py, outputs v1618-head.txt and v1618-main.txt):
- int 000000000183 and none: head reads whole at 193, the same as main.
- real: head loops and rebuilds at opening.
- other (000000000193): head relocates to 474 ('291 bytes after it'), then reads 193. It reports hybrid-stream-lost, a rebuild when an object is asked for (repairedAfter=True) and Error hybrid-stream-wrong. Main relocates to 193 with no repair.

Texts:
- lazy-reading.md:17-18: a /Prev 'is read only where a section already read places it'.
- threat-model.md:433: 'a real with no fractional part as its integer'.
- lazy-reading.md:132-133 and :196, diagnostics.md:115, validation-rules.md:60: 'names the table's', with no 'as written'.
- PdfFileReader.cs:7383-7400, ReadSectionOffset: `value is PdfReference reference ? ReadWithoutLoading(reference.Id) : value` then `AsInteger()`.
REFUTER FIX: Compare the stream's /Prev in MayBeTablesOwn (PdfFileReader.cs:7053) as ReadSectionOffset reads it, reporting nothing.
- A real with no fractional part counts as its integer. This costs nothing.
- A reference counts as what ReadWithoutLoading gives where a section already read places its object, through AsInteger(). That is at most one cached read per candidate, which MaxRelocationCandidates bounds, and it records nothing. MayBeTablesOwn becomes an instance method.
- This implements R7-Q2 as worded ('a /Prev naming the offset the table's /Prev names'). It takes back the accepted R7b narrowing for these two forms, so the triage should confirm it.

Add rows to A_prev_falling_inside_the_stream_its_xrefstm_read_takes_it_only_where_the_stream_s_own_prev_names_another_offset:
- the stream's /Prev written as the table's offset in real form, and as a reference. Each asserts the previous table taken, no repair and no chain-loop.
- the g001618 shape with the real form, asserting the chain read whole.

If the comparison stays as written, state it in the user pages instead: lazy-reading.md:132-133 and :196, diagnostics.md:115, validation-rules.md:60. A /Prev that is a reference, a real, or any non-integer counts as naming another offset, whatever it leads to. The stream is then taken at its nearest place, and the chain loops, ends, or is rebuilt as the document opens.

Do not count a negative offset, one outside the file, or a name as 'may be the table's own' without asking. R7-Q2 says 'names another offset', and main behaves the same there. That is a maintainer choice.

Put the g1618-other observation, the deferral of a first revision's stream for having no /Prev, to the triage as its own point: under R7-Q2 it beats main nowhere and loses to it there.

######## [12] lens=own-stream key=maybeown-initializer-survivor sev=minor -> verdict=narrowed sev=nit maint=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:6540
CLAIM: An ADR 53 catalog mutant that the stage did not run survives and is not equivalent: `var mayBeOwn = false;` changed to `true`. The keep predicate returns before assigning mayBeOwn for a place that is no cross-reference stream. Under the mutant, every ordinary object in the window is passed over, put back and read again, each read counted. A deferred stream lying past 16 of them is then lost.
EVIDENCE: Mutant built in a scratch copy of 012e13d. These 7 classes gave total 2,224, failed 0: CrossReferenceChainTests, CrossReferenceRuleTests, GuardCutValueTests, HostileInputTests, HeaderOffsetTests, WindowEdgeTests, DocumentReaderTests.
Kill: DenseFar(16, -160) in r8own/ZzR8OwnShapesTests.cs puts 16 null objects nearer the /Prev than X0, which gives no /Prev.
- head: '/Prev named=920 at=1080 Relocated Stream ... found 160 bytes after it', repairedAtOpen=False.
- mutant: '/Prev named=920 at=920 NotFound', 'section-missing', 'xref.rebuilt', 'Error xref.section-not-found'.
Outputs in r8own/far-head.txt and r8own/far-mut.txt.
FIX: Add a test where a stream that may be the table's own lies past at least 16 ordinary objects in the window, and assert it is found. That kills the mutant.
--- REFUTER: Every fact in the finding holds. The mutant changes `var mayBeOwn = false;` to `true` at src/AdCodicem.Pdf/IO/PdfFileReader.cs:6540, which 3a1ac79 added.

1. The stage did not run it. r7b/mutate.py and r7b/mutate2.py hold no mutant on this initializer. They do flip its sibling, `var passedOver = false;` to `true` (S1). So the mutant is in the catalog by the stage's own reading (rule 1, a negated condition).
2. It survives the whole unit suite, not only the reviewer's 7 classes.
3. It is not equivalent. The keep predicate returns before it assigns mayBeOwn for any place that is not a cross-reference stream. The same holds for a place whose parse fails before keep is asked. Under the mutant all such places get passedOver = true and are added back to the list. They are read again with nothing passed over, and each reading spends one of MaxRelocationCandidates (32 per document).

Narrowed in two ways.

(a) Scope. The only observable effect is the attempt budget, plus one more window read per ordinary place, at most 32. An ordinary place is dropped on both readings, so the order change never changes which place is taken. The deferred stream is lost once the attempts before its second reading pass 32. Those are: every place in the window once, then the ordinary places nearer than the stream again, then the stream (2·near + far + 2 > 32, less what other searches spent). With nothing beyond the stream and no other search, that takes 16 nearer objects. With 15 the stream is attempt 32 and is still taken.

(b) Class. ADR 53 and CLAUDE.md say a survivor is a gap in the tests, settled in the mutation step and outside the review. It "does not count as a review finding and does not restart the review." So this should not count as a minor for the round's stopping rule. It does need to be handed to the batch-wide ADR 53 step (#26) by name. ADR 53 does not mutate again a line already mutated, and line 6540 counts as mutated by R7b, so that step would skip this mutant unless told.

No text misstates the code. The XML docs of TryParseNearHybridPrev and TryRelocateXRefSection describe head: only a stream that may be the table's own is passed over and counted again. No test name claims what the mutant breaks. The 'passes over or finds no section at' row holds both outcomes. The budget Fact's /XRefStm reads no stream, so tablePrevious is -1 there and the mutant cannot reach it.
REFUTER EVIDENCE: The reviewer's probe, ZzR8OwnShapesTests.ZzR8DenseFar (DenseAt), was rebuilt in my worktree at 012e13d (origin/ccr-6d6c0ee6-dqm4lm-188), Release.

Head, (16, -160), x0=1080 s1=2694 t1=2848 prev=920:
- `S /Prev named=920 at=1080 Relocated Stream`
- `D Repair xref.offset-adjusted @1080: The cross-reference section /Prev names at offset 920 was found 160 bytes after it.`
- repairedAtOpen=False
- validator: only `Warning xref.section-shifted` and `file.size-wrong`

Mutant, line 6540 set to `var mayBeOwn = true;`, same file:
- `S /Prev named=920 at=920 NotFound Unknown`
- `D Warning xref.section-missing @920 ...`
- `D Repair xref.rebuilt @-1`
- `D Information object.redefined @-1`
- repairedAtOpen=True
- `F Error xref.section-not-found offset 920`

The (20, -200) row behaves the same way at 964 and 1164.

Whole unit suite on the mutant: total 7,766, failed 0, succeeded 7,746, skipped 20. That count includes the 2 probe rows, which only dump their output.

The stage's catalog: r7b/mutate2.py lists S1 (`var passedOver = false;` to `true`), N1, N2, O1-O3, T1 and T2 on this function, and nothing on `var mayBeOwn = false;`. r7b/mutate.py (round 1) has none either.

Code: the keep lambda at PdfFileReader.cs:6549-6558 returns false before `mayBeOwn = MayBeTablesOwn(...)` when DeclaresOnlyCrossReferenceStream is false. TryParseNumberedAt (about :8000-8035) returns false without calling keep when no object header is read or a guard cuts the header. TryRelocateXRefSection (:5018) adds back every attempt with MayBeTablesOwn set. MaxRelocationCandidates = 32 (:133).

Outputs: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r8ref-maybeown/head.txt, /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r8ref-maybeown/mut.txt and /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r8ref-maybeown/suite-mut.txt. The worktree was reverted to clean.
REFUTER FIX: Settle it in the batch-wide ADR 53 step (#26), or in the next fix stage's ADR 53 run, by naming this mutant there. Line 6540 is already counted as mutated, so the step would otherwise skip it.

The killing test is a row in CrossReferenceChainTests, with its file in HybridFiles beside PrevFallingShortOfAPlaceBesideTheStream. The file:
- a first revision whose cross-reference stream X0 gives no /Prev;
- 16 small objects ('n 0 obj null endobj') packed before X0, inside the 512-byte window;
- the earlier objects padded out of the window;
- a hybrid update whose /XRefStm names its own stream about 1,300 bytes on, and whose /Prev names x0 - 160, among those objects.

Assert:
- sections end with ("/Prev", Relocated, Stream, x0);
- diagnostics are exactly [xref.offset-adjusted], with the message '... was found 160 bytes after it.';
- WasRepaired is false.

It fails on the mutant (NotFound, xref.section-missing, xref.rebuilt). A second option is a counting-source assertion that each ordinary place near the hybrid /Prev is read through one window only.

Count it as an ADR 53 survivor, not a minor review finding. No code or text change is needed, and the maintainer has nothing to decide.

######## [13] lens=src-texts key=guard-hidden-prev-decides-in-silence sev=major -> verdict=confirmed sev=major maint=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:6557 (TryParseNearHybridPrev's keep test). Texts: :4834-4843 (TryRelocateXRefSection remarks: 'told by its own /Prev as its dictionary writes it', 'keeping nothing where the stream may be the table's own'), :6521-6528 (TryParseNearHybridPrev summary: 'nothing it met is reported or recorded, a guard its window reached included'), src/AdCodicem.Pdf/IO/XRef/XRefSectionRecord.cs:149-156 (MayBeTablesOwn)
CLAIM: In the search near a hybrid table's /Prev, a stream is judged 'may be the table's own' from whatever part of its dictionary the MaxTrailerLength window read. If the cut falls before the stream's /Prev, that /Prev is taken as absent, and the stream is passed over without any report. The /Prev the guard hid then decides which section is taken. The reader reports no limit.*, does not throw under ThrowOnLimit, and blames the file (Warning xref.hybrid-stream-lost, Error xref.hybrid-stream-wrong, a rebuild). This is a regression from 3a1ac79: d3ef236 and main report the guard, and the section is the guard's (ADR 34, invariant 12). The texts claim the stream is 'told by its own /Prev as its dictionary writes it', but the verdict rests on a /Prev the guard hid. 'A guard its window reached included' describes exactly how the guard's cut is lost.
EVIDENCE: Shape: catalog and pages; oldest table at 110; 1,200 dashes; object 4; xref stream 6 at 1457 (/Prev 110, indexes 4 and 6); object 10; xref stream 12 at 1613 (/Type /XRef /Index [10 1] /Root 1 0 R, then /Pad of 70,000 bytes, then /Prev 110); 600 dashes; newest table at 72348 with /XRefStm 1457 /Prev 1606 (7 bytes before stream 12).

Head 012e13d, default limits: sections are [startxref Table Read 72348; /XRefStm Unknown NamedAgain 1457; /Prev named 1606 at 1457 Stream Relocated; /Prev Table Read 110]. Diagnostics: Repair xref.offset-adjusted 'found 149 bytes before it'; Warning xref.hybrid-stream-lost '...names offset 1457, the section its /Prev names too...'. WasRepaired=True. Validator: Warning xref.section-shifted, Error xref.hybrid-stream-wrong. ThrowOnLimit=true gives the same and does not throw. With MaxTrailerLength 1 MB: /Prev at 1613 Stream Relocated 'found 7 bytes after it', no hybrid-stream-lost, no repair.

d3ef236 and main 21a0298, default limits: /Prev at 1613 Stream Malformed 'its dictionary is not followed by stream data'; Warning limit.trailer @1613 '...runs past 64 KB ... Raise PdfReaderLimits.MaxTrailerLength to read past it.'; validator Information xref.checked-in-part. ThrowOnLimit throws PdfLimitExceededException.

With the fix below applied, the head gives d3ef236's output, throw included. Of CrossReferenceChainTests, CrossReferenceRuleTests, GuardCutValueTests, DocumentReaderTests and HostileInputTests, 2,067 of 2,068 pass. The one failure is A_place_the_search_near_a_hybrid_table_s_prev_passes_over_or_finds_no_section_at_leaves_nothing_of_its_reading(near: "stream 11", pad: 300), the row that pins the silent pass-over. Its stream 11 has no /Prev past the pad, so lifting the guard happens not to change that row's outcome.
FIX: In TryParseNearHybridPrev, apply the rule only to a stream whose dictionary was read whole: `mayBeOwn = parsed is PdfStream && MayBeTablesOwn(parsed, tablePrevious);`. A cut dictionary is never a PdfStream. Such a place is then kept, its guard reported, and the section is the guard's, as on d3ef236. This change also covers the plain-dictionary nit below. Change the ('stream 11', 300) row to expect limit.trailer and d3ef236's outcome. Add the shape above as a test (defaults, ThrowOnLimit, and 1 MB). Change the three texts to say a stream is told by its /Prev only where its dictionary was read whole, and that a dictionary the window cut is the guard's.
--- REFUTER: The finding holds as stated, and it is a regression against both d3ef236 and main 21a0298.

**Where it happens.** In the search near a hybrid table's /Prev, TryParseNearHybridPrev's keep test (PdfFileReader.cs:6557) checks MayBeTablesOwn against the dictionary MaxTrailerLength left. TryParseNumberedAt calls keep in its cut branch before ReachLimit (:8040-8043), and a refusal there drops the guard.

**What goes wrong.** Take a stream that gives /Type /XRef before the cut and /Prev after it. The reader treats its /Prev as absent and passes the stream over in silence. The search then takes the /XRefStm's stream 6, whose /Prev names another offset. The section is chosen by a /Prev the guard hid:
- there is no limit.* report;
- ThrowOnLimit does not throw;
- the file is blamed instead: Warning xref.hybrid-stream-lost, a rebuild when object 10 is asked for, and validator Error xref.hybrid-stream-wrong plus section-shifted.

With the limit raised, the reader takes stream 12 seven bytes away, and the file reads cleanly with no repair. So a guard's cut is blamed on the file (invariant 12, ADR 34).

**Not known or accepted.**
- R7-Q2 decides by "one whose dictionary gives no /Prev". A cut dictionary leaves that unknown, and the decision says nothing of a cut.
- The silent pass-over of a cut dictionary was the fix stage's own choice. It was pinned only to kill mutant O2, in the ('stream 11', 300) row, and recorded as a measurement. It is not among R7b's deviations or open points, not in decisions.md, and not in the brief's accepted list.
- None of #289, #295, #331, #323, #263, #171 or #52 holds it.

**Texts.** They misstate the code here:
- :4834 says "told by its own /Prev as its dictionary writes it". This dictionary writes /Prev 110.
- XRefSectionRecord.cs:151 says "its dictionary gives no /Prev". It lacks the "as far as it was read" that TypedXRef's doc carries.
- Two texts say a place that declares /Type /XRef before a guard's cut and cannot be read ends the search, and the head passes it over:
  - the #281 clause of the same remarks (:4819-4821, "the search ending there, where it declared itself one ... /Type /XRef, before a fault or a guard's cut");
  - lazy-reading.md:196-202.
- :6521-6528 is true to the code: it states the mechanism that drops the guard.

**Narrowing.** The silent choice needs another place taken before the deferred retry. If nothing else is taken first, the stream is read again with nothing passed over, and its guard is reported. Where the hidden /Prev is absent or names the table's /Prev, the head's default outcome happens to match a whole read, still without the guard. Only a hidden /Prev naming another offset changes the section.

**Severity.** Major: a wrong Error and Warning, a missing limit report, and ThrowOnLimit not honored, all under default limits. This is the same class as round 3 [10], also rated major. The shape is contrived: a cross-reference stream dictionary over 64 KB, with a hybrid /Prev off by 7 bytes.

**Maintainer.** Not needed: ADR 34 and the settled #281 rule decide it.
REFUTER EVIDENCE: I rebuilt the shape myself with probe ZzR8GuardHiddenPrevTests (uncommitted, now removed). Its source is saved at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r8ref-ghp/ZzR8GuardHiddenPrevTests.cs.txt, and its outputs per tree are in that folder (head/, d3ef/, main/, fix/).

**The shape.** Oldest table at 110, stream 6 at 1457 (/Prev 110), stream 12 at 1613 (/Type /XRef, /Pad of 70,000 bytes, then /Prev 110), newest table at 72348 (/XRefStm 1457, /Prev 1606).

**Head 012e13d, default limits:**
- Sections: [startxref Read Table 72348; /XRefStm NamedAgain Unknown 1457; /Prev Relocated Stream 1457; /Prev Read Table 110].
- D Repair xref.offset-adjusted @1457 'found 149 bytes before it'.
- D Warning xref.hybrid-stream-lost @1457 '...names offset 1457, the section its /Prev names too, which the chain read through that /Prev...'.
- WasRepaired is False at opening and True after object 10 ("ten") is read.
- F Warning xref.section-shifted (149 before) and F Error xref.hybrid-stream-wrong.
- No limit.trailer. ThrowOnLimit=true gives the same output and does not throw.

**Head, MaxTrailerLength 1 MB:** /XRefStm Read 1457; /Prev Relocated Stream 1613 'found 7 bytes after it'; /Prev Read Table 110; no hybrid-stream-lost, no repair. Findings: size-wrong and section-shifted only.

**Control, /Prev written before the pad (head, defaults):** /Prev Malformed Stream 1613 'its dictionary is not followed by stream data', plus Warning limit.trailer @1613 'Raise PdfReaderLimits.MaxTrailerLength...', and checked-in-part. The verdict depends on where the key sits relative to the cut.

**d3ef236 and main 21a0298, defaults:** /Prev Malformed Stream 1613, Warning limit.trailer @1613, offset-adjusted (7 after), obj10 null, no repair, findings section-shifted and checked-in-part. Under ThrowOnLimit both throw PdfLimitExceededException ('...runs past 64 KB...'). The 'none' and 'table' variants give the same on d3ef236.

**With `mayBeOwn = parsed is PdfStream && MayBeTablesOwn(parsed, tablePrevious)` applied:**
- Every default-limit row (oldest, none, table, oldest-before) and the ThrowOnLimit row are byte-identical to d3ef236.
- The 1 MB rows 'none' and 'table' stay byte-identical to the head, so R7-Q2 is kept where the dictionary is whole.
- Whole unit suite: 7,756 total, 7,735 passed, 20 skipped, 1 failed. The failure is A_place_the_search_near_a_hybrid_table_s_prev_passes_over_or_finds_no_section_at_leaves_nothing_of_its_reading(near: "stream 11", pad: 300), the row that pins the silent pass-over.
- After the run the worktree was reverted to a clean state.
REFUTER FIX: **Code.** In TryParseNearHybridPrev, judge only a stream whose dictionary was read whole:

`mayBeOwn = parsed is PdfStream && MayBeTablesOwn(parsed, tablePrevious);`

A dictionary the window cut is never a PdfStream. Such a place is kept, as on d3ef236, under the #281 rule: it declared /Type /XRef before the cut, the guard is reported, ThrowOnLimit throws, and the section is the guard's. One side effect, to note for whoever triages the plain-dictionary point: a plain dictionary of /Type /XRef read whole is no longer deferred either. That again matches d3ef236.

**Tests.**
- Change the ('stream 11', 300) row to expect d3ef236's outcome: /Prev Malformed at stream 11, limit.trailer, no hybrid-stream-lost.
- Add the shape above as a theory with three rows: default limits (limit.trailer @1613, section the guard's, no hybrid-stream-wrong, checked-in-part), ThrowOnLimit (throws), and MaxTrailerLength 1 MB (relocated to 1613, chain read whole, no repair).
- Add a control with the /Prev before the pad.
- Run ADR 53 over the changed line. A mutant dropping `parsed is PdfStream` must fail the new rows.

**Texts.**
- Say that a stream is told by its /Prev only where its dictionary was read whole, and that a dictionary a guard cut is the guard's, kept and reported as any typed place cut by a guard is:
  - the TryRelocateXRefSection remarks (:4833-4843, and the readInStreamsPlace param);
  - the TryParseNearHybridPrev summary (:6521-6528);
  - the MayBeTablesOwn helper (:7043) and XRefSectionRecord.MayBeTablesOwn (:149-156);
  - lazy-reading.md:196 and threat-model.md:432's 'told by its own /Prev'.
- Update the test's comment ('neither the fault nor the limit lies in what the chain reads') for the cut row.

######## [14] lens=src-texts key=undecoded-rollback-drops-the-places-own-reports sev=minor -> verdict=confirmed sev=major maint=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:5007-5013
CLAIM: After a dropped place whose data cannot be decoded, RollBackBesideLimits(reports) rolls back to a mark taken before the whole reading. It removes the syntax and stream-length reports of the place's own dictionary, not only 'what the decoding reported', as the comment says. A /W-only place dropped for any other fault keeps those reports, so the same dictionary fault is reported or not depending on whether the data decodes. 'A reading of that stream meeting it again' does not apply when no section indexes the object: the report is lost for good. R7a classed mutant M09 (roll back after every drop) as equivalent on the premise that 'a dropped place's reading reports nothing but guards'. These rows disprove that premise, and M09 survives.
EVIDENCE: Shape: oldest table at 137 (indexes 1, 2 and 7 '(old seven)'); object 8, a string; object 9 at 308, a stream with /W [1 4 2] /Size 10 /Index [7 1] and no /Type; newest table with /Prev 301, 7 bytes before 9. Search finds the oldest table 164 bytes before.
- /Filter /Foo plus a stray ']' in the dictionary: diagnostics [Repair xref.offset-adjusted @137].
- /W [1 -4 2] plus a stray ']': [Warning syntax.unexpected-token @353 'A dictionary key was not a name.', Repair xref.offset-adjusted @137].
- /Filter /Foo with /Length 7 over 14 bytes: [xref.offset-adjusted].
- /W [1 -4 2] with /Length 7 over 14 bytes: [Warning stream.length-invalid @374 'The stream declared 7 bytes but ended after 14.', xref.offset-adjusted].
GetObject(9) afterwards is null and adds nothing.

Under M09 (`if (attempt.Fault == DataUndecoded)` turned to `if (true)`), the two negative-width rows lose syntax.unexpected-token and stream.length-invalid. CrossReferenceChainTests, DocumentReaderTests, GuardCutValueTests, CrossReferenceRuleTests, HostileInputTests and WindowEdgeTests: 2,178 passed, 0 failed under the mutant.
FIX: Narrow the rollback to the decoding: take the mark just before the decode in TryReadXRefStream and hand it to the caller, or roll back only the reports that came after the parse was kept. Then the comment is true and both rows keep syntax.unexpected-token. Alternatively, decide that every dropped guess leaves nothing, apply that to every drop, and say so. Either way, pin it with the stray-']' rows, one undecodable and one with a negative width, which also kills M09. Reclassify M09 as a survivor killed by that test.
--- REFUTER: The finding holds and goes further than claimed. The mark at PdfFileReader.cs:4967 is taken before TryReadXRefSection, so RollBackBesideLimits at :5012 drops every non-limit report the place's reading made: its dictionary's syntax.* reports and stream.length-invalid, not only 'what the decoding reported' (:5007-5009). A /W-only place dropped for any other fault, such as a negative width, keeps those reports.

'A reading of that stream meeting it again' is also false for them, even where a reading does meet the object again. RollBackBesideLimits restores the decoding-report dictionary only. It leaves the reader's reported-once sets as they were: _syntaxReported (:297, :5824) and _lengthFaultsReported. The reader's own MarkReports/WithdrawReportsSince (:9224-9260) does restore them. So any later parse of the object has its syntax and length reports suppressed as already reported.

In the hybrid shape, object 4 is served by GetObject (through the rebuild), and its syntax.key-repeated, syntax.unexpected-token or stream.length-invalid never appears. d3ef236 and main 21a0298 report each one once. 092b772 introduced the loss: a report missing for a served object, against invariant 5, so I rate it major. The finding's own shape, where object 9 is never served, would be minor alone.

None of the accepted points or debts covers this. #320 covers the rows, refusals and incomplete flag a dropped candidate leaves, not its reports. #263's accepted point covers the chain's own reading.

M09 (roll back after every drop) is not equivalent. R7a's premise, 'a dropped place's reading reports nothing but guards', is false. Under M09 the five negative-width rows lose their reports, and the related classes still pass.

The narrow fix needs no maintainer decision, since it does what the comment and 092b772's body ('leaves no report of its decoding') say was decided. Only the alternative, that every dropped guess leaves nothing, is a design choice, tied to #320.

Outside this finding, on d3ef236 too: in the hybrid file, GetObject(7) gives "old seven" when asked first and "new seven" after GetObject(4). I did not check this against the issues.
REFUTER EVIDENCE: The probe is /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r8ref20-ZzR8UndecodedRollbackTests.cs.txt. The files are in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r8ref20-files/. The outputs are r8ref20-files-head2.txt, -d3e.txt, -main.txt, -m09.txt and -fix.txt in the same lot19 folder.

**Finding's shape at 012e13d.** Oldest table at 137. Object 9 at 323, with no /Type, /W, /Size 10 and /Index [7 1]. Newest table /Prev 316.
- /Foo plus a stray ']': [Repair xref.offset-adjusted @137]. After GetObject(4/7/9) the total is 1.
- /W [1 -4 2] plus a stray ']': [Warning syntax.unexpected-token @346 'A dictionary key was not a name.', Repair xref.offset-adjusted @137].
- /Foo with a repeated /W key: offset-adjusted only. With /W [1 -4 2] and a repeated /W key, syntax.key-repeated @346 is kept.
- /Foo with /Length 7 over 14 bytes: offset-adjusted only. With /W [1 -4 2], 'Warning stream.length-invalid @388 The stream declared 7 bytes but ended after 14.' is kept.
- On d3ef236 and main, the /Foo rows report syntax.unexpected-token @345, syntax.key-repeated @345 and stream.length-invalid @400, with filter.unsupported. There the place is taken (R7-Q1 changes that) and the reports follow.

**Hybrid shape at 012e13d.** HybridFiles.PrevNamingAStreamBesideTheOldestTable, object 4 at 1675. The /XRefStm search drops it, the /Prev reading reads it again, and GetObject(4) serves '<<stream of 7 bytes>>'.
- widths '[1 4 2] /W [1 4 2]' with /Foo: [offset-adjusted @1459, hybrid-stream-lost @1647, filter.unsupported @1774, offset-adjusted @1459]. After GetObject there is still no syntax.key-repeated.
- The same with /FlateDecode: no syntax.key-repeated either.
- '[1 4 2] ]' with /Foo: no syntax.unexpected-token.
- /Foo with /Length 3 over 7 bytes: no stream.length-invalid.
- Controls with /W [1 -4 2] keep syntax.key-repeated @1707, syntax.unexpected-token @1707 and stream.length-invalid @1751, each once.
- d3ef236 and main report syntax.key-repeated @1706, syntax.unexpected-token @1706 and stream.length-invalid @1763 for the /Foo and Flate rows.

**M09.** I turned `if (attempt.Fault == DataUndecoded)` into `... || true`. The neg-stray, neg-repkey, neg-len, h-neg-rep and h-neg-stray rows lose their report: totals 2→1 and 6→5. CrossReferenceChainTests, DocumentReaderTests, GuardCutValueTests, CrossReferenceRuleTests, HostileInputTests, WindowEdgeTests, FilterTests and FilterDamageTests: 2,517 passed, 0 failed.

**Narrow-fix prototype.** I took a GetMarkWithDecodingReports mark in TryReadXRefStream beside `decoding` (:6275), for candidates only, and rolled back to it at :5012. Every /Foo and Flate row above gets its syntax or length report back, once, and the decoding report still goes, re-reported once by the hybrid's /Prev reading. The same 8 classes: 2,517 passed, 0 failed. The prototype is reverted.
REFUTER FIX: Roll back only the decoding. In TryReadXRefStream, take `_diagnostics.GetMarkWithDecodingReports()` just before the decode loop, beside `decoding` at :6275, when `candidate` is true. Hand it to the caller, for example on the attempt record next to Fault. At :5012, roll back to that mark instead of the one taken at :4967, and drop the :4967 mark.

Reword :5007-5009 to say that only what the decoding reported goes, and that what the dictionary's reading reported stands, as for any dropped place. Do not widen the rollback with RollBackBesideLimits alone: it leaves _syntaxReported and _lengthFaultsReported as they are, so a dropped report is never made again.

If the maintainer instead wants every dropped guess to leave nothing, use MarkReports/WithdrawReportsSince, which forgets the reported-once sets, apply it to every drop, and decide it with #320. That is a design choice.

Tests, in CrossReferenceChainTests:
- a pair of rows in the finding's shape: /Foo plus a stray ']', and /W [1 -4 2] plus a stray ']', each reporting syntax.unexpected-token once. The /Foo row fails at 012e13d, the negative-width row kills M09;
- a hybrid row with PrevNamingAStreamBesideTheOldestTable(widths: "[1 4 2] /W [1 4 2]", filter: "/Foo"): syntax.key-repeated once and filter.unsupported once, before and after GetObject(4);
- a /Length-over-data row.

Also correct the comment of An_xrefstm_goes_on_past_its_table_s_prev_where_the_prev_names_a_stream_of_no_type_whose_filters_decode_nothing_of_it (CrossReferenceChainTests.cs ~:5069-5070). Its 'so is what the search's reading of object 4 met: the /Prev's own reading reports it, once' holds for the decoding's report only.

Reclassify M09 in the round-7 record as a survivor, killed by the negative-width rows.

######## [15] lens=src-texts key=undecoded-cases-misnamed sev=minor -> verdict=narrowed sev=nit maint=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:7020-7023 (DataUndecoded summary), :6302-6303 (comment in the reading loop), :5972-5975 (TryReadXRefStream remark); src/AdCodicem.Pdf/IO/Filters/PdfFilterPipeline.cs:90-99 (undecoded param), :294-295
CLAIM: The texts that say which data counts as undecoded overstate in one direction and understate in the other. DataUndecoded and the reading's comment say 'or by one that could decode none of what it was given', with no qualifier. The param doc and the remark use the general phrase with Flate as an aside. Only Flate failing before its first byte counts. LZW whose first code is undefined, ASCIIHex or ASCII85 data none of whose bytes encode anything, RunLength opening on its end marker, and Flate holding only a header byte all decode none of their data, yet each is read in part (0 rows), not as unreadable. That matches the accepted LZW behavior, but these texts misstate it. In the other direction, an element of /Filter that names no filter and a /Filter that is neither a name nor an array are undecoded. DataUndecoded and the loop comment do not name them; the TryReadXRefStream remark and the pipeline's closing comment omit the second.
EVIDENCE: UndecodedStreamFiles.OverAnOlderTable (startxref names the stream at 306, oldest table at 163):
- /LZWDecode, first 9-bit code 300: section Read incomplete=True 'it holds 0 rows where its /Index and /Size declare 1'; filter.failed 'An LZW stream uses code 300, which it has not defined; what decoded before it was kept.'; xref.section-unreadable 'read only in part'; /Prev read; validator 'is malformed: it holds 0 rows...'.
- /ASCIIHexDecode 'zzzzzz>': same result; filter.failed 'holds 6 bytes that encode nothing'.
- /ASCII85Decode 'vvvvv~>': same result.
- /RunLengthDecode '\x80xxxxxx': same result.
- /FlateDecode 'x': same result; 'A Flate stream ends before its data does'.
Against these:
- /FlateDecode 'xxxxxxx': Malformed 'its data could not be decoded'.
- /Filter 42: Malformed 'its data could not be decoded'; filter.unsupported 'The /Filter entry is an integer, neither a name nor an array'.
- [/ASCIIHexDecode 42]: Malformed; 'Element 1 of the /Filter entry is an integer, which names no filter'.
FIX: Name the cases exactly in all five places: left at a filter the library does not know, at an element of /Filter that names none, or under a /Filter that is neither a name nor an array; or by Flate data corrupt before its first byte decodes, the one filter that can decode none of its data. Add that any other filter gives what it decoded, nothing included, and that data holds the rows it holds, read in part where it lacks some.
--- REFUTER: Every behavior the finding cites reproduces exactly at 012e13d. The behavior is accepted (LZW whose first code is undefined is read in part with 0 rows). The finding is about the texts, and two of them do misstate it. Only Flate's TryDecode failing sets `failed` (PdfFilterPipeline.cs:428-433). The end-of-chain flag is set only for Unsupported, NotAName and NotAFilterEntry (:296).

Overstatement:
- DataUndecoded's summary (PdfFileReader.cs:7020-7023) and the comment in the reading loop (:6302-6306) say "or by one that could decode none of what it was given", with no qualifier.
- LZW whose first code is undefined is exactly that case, a filter that fails before any output. It is read in part (0 rows), the section Read, the /Prev followed. It is not "its data could not be decoded", and the section is not "cannot be read".
- Flate cut after its header byte ('x') is arguably the same case.
- The ASCIIHex, ASCII85 and RunLength shapes are data that encodes nothing, not a filter that "could decode none". The overstatement is weaker for them.
- The pages are already exact: they say "or by Flate data corrupt before its first byte decodes" (lazy-reading.md:113-114, validation-rules.md:54). The round 7 docs stage chose that wording because LZW is read in part. So the src texts now disagree with the pages.

Understatement:
- DataUndecoded and the loop comment name only "a filter the library does not know". /Filter 42 and [/ASCIIHexDecode 42] give the same Malformed fault.
- The TryReadXRefStream remark (:5973-5975) and the pipeline's closing comment (:294-295) leave out a /Filter that is neither a name nor an array.

Narrowed:
- The `undecoded` param doc (PdfFilterPipeline.cs:90-99) names all three unsupported shapes. Its appositive "— Flate data corrupt before its first byte decodes —" reads naturally as the definition. So it is at most a nit.

Severity:
- All five texts are internal: a private const, a comment, a private method's remarks, an internal class. Round 7 rated src-text imprecision of the same kind as nit (its [1], [2], [3], [4] and [19]). No worse consequence follows. Hence nit, not minor.

Not known:
- r1 to r7, decisions.md and the r7 docs open point say LZW is read in part. None says the src texts name it as undecoded.
REFUTER EVIDENCE: Probe on 012e13d: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r8ref-ZzR8UndecNamedTests.cs.txt, run through UndecodedStreamFiles.OverAnOlderTable. Output is in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r8ref-undecnamed-out.txt. The stream is at 306, the oldest table at 163.

Read in part:
- /LZWDecode, data 0x96 0x00..., so the first 9-bit code is 300:
  - section 306 Read incomplete=True rowsHeld=0 fault='it holds 0 rows where its /Index and /Size declare 1'; section 163 Read.
  - filter.failed 'An LZW stream uses code 300, which it has not defined; what decoded before it was kept.'
  - xref.section-unreadable '...was read only in part: it holds 0 rows...'
  - validator: 'is malformed: it holds 0 rows where its /Index and /Size declare 1.'
- The same result for:
  - /ASCIIHexDecode 'zzzzzz>' (filter.failed '...holds 6 bytes that encode nothing...');
  - /ASCII85Decode 'vvvvv~>' ('...5 bytes that encode nothing...');
  - /FlateDecode 'x' ('A Flate stream ends before its data does; what decoded before the end was kept.');
  - /RunLengthDecode '\x80xxxxxx', which gives no filter report at all. The finding's 'same result' is loose there.

Malformed with fault 'its data could not be decoded', diagnostics [decoding code, xref.rebuilt], validator 'cannot be read: its data could not be decoded.':
- /FlateDecode 'xxxxxxx': 'A Flate stream could not be decoded.'
- /Foo.
- /Filter 42: filter.unsupported 'The /Filter entry is an integer, neither a name nor an array; the data was left as it is.'
- [/ASCIIHexDecode 42]: 'Element 1 of the /Filter entry is an integer, which names no filter; decoding stopped there.'

Code:
- `failed` is set only on the !FlateFilter.TryDecode path (PdfFilterPipeline.cs:428-433).
- `undecoded = !data.IsEmpty && chain.End is Unsupported or NotAName or NotAFilterEntry` (:296), which the comment at :294-295 describes without NotAFilterEntry.
- The pages say 'or by Flate data corrupt before its first byte decodes' (docs/website/docs/concepts/lazy-reading.md:113-114, docs/website/docs/reference/validation-rules.md:54).
- r7-fix-result.json, docs stage open point: 'LZW whose first code is undefined decodes to empty data: it is a stream read in part (0 rows), not undecoded, so no page counts it.'
REFUTER FIX: Use the pages' wording, made complete, in every src text.

1. DataUndecoded's summary (PdfFileReader.cs:7020-7023) and the comment in the reading loop (:6302-6306): "left at a filter the library does not know, at an element of /Filter that names none, or under a /Filter that is neither a name nor an array; or by Flate data corrupt before its first byte decodes, the one filter that fails without output. Any other filter gives what it decoded, nothing included, and that data holds the rows it holds, the section read in part where it lacks some."
2. The TryReadXRefStream remark (:5973-5975) and the pipeline's closing comment (PdfFilterPipeline.cs:294-295): add "or a /Filter that is neither a name nor an array".
3. The `undecoded` param (PdfFilterPipeline.cs:90-99), optionally: make "Flate ..., the only filter that can" explicit, and add that LZW whose first code is undefined, or ASCII data that encodes nothing, gives empty data, which this does not say.

This is text only, with no design choice for the maintainer. Optional, for consistency: lazy-reading.md:113 and validation-rules.md:54 also name only 'a filter the library does not know'. They could add the two /Filter shapes the threat model already lists.

######## [16] lens=src-texts key=plain-dictionary-passed-over-as-own-stream sev=nit -> verdict=narrowed sev=nit maint=False
LOCATION: src/AdCodicem.Pdf/IO/XRef/XRefSectionRecord.cs:149-156; src/AdCodicem.Pdf/IO/PdfFileReader.cs:6557, :4831-4843
CLAIM: MayBeTablesOwn and the TryRelocateXRefSection remarks say a cross-reference stream that may be the table's own is passed over and tried again. The keep test also flags a plain dictionary that gives a /W, no /Type and no /Prev, such as a border style dictionary. That dictionary is no stream and never a section, yet it is passed over and read again: one more window, and one more of the document's MaxRelocationCandidates attempts.
EVIDENCE: Shape: oldest table at 110; stream 6 at 1457, more than 512 bytes off; object 10; object 12 at 2815 holding `<< /W 1 /S /S >>`; newest table with /XRefStm 1457 /Prev 2808. A logging source records these reads at 2815: [32, 745, 32, 745, 745]. With `<< /W 1 /S /S /Prev 5 >>`: [32, 753, 753]. With `<< /S /S >>`: [32, 740, 740]. The outcome is the same in all three (xref.section-missing at 2808, rebuild). With `parsed is PdfStream &&` the /W dictionary reads [32, 745, 745].
FIX: Apply the same `parsed is PdfStream` test as in the first finding, or have the texts say 'an object that declares only a cross-reference stream, a dictionary that gives a /W among them'.
--- REFUTER: The finding reproduces. The keep test at PdfFileReader.cs:6557 runs MayBeTablesOwn on anything DeclaresOnlyCrossReferenceStream accepts, and that includes a plain dictionary. So a dictionary that gives /W, no /Type and no /Prev is passed over in the first pass and read again at the end. That costs one more 32-byte probe, one more window read, and one more of the document's 32 MaxRelocationCandidates attempts. 3a1ac79 introduced this: on d3ef236 the same dictionary is read once. The outcome is the same in every variant. On the cost side it adds nothing new: the texts already accept 'read again, counted again' for a stream that gives no /Prev, and a hostile file can write such streams just as cheaply.

What the texts get wrong is small. MayBeTablesOwn's summary says the place 'holds a cross-reference stream'. The TryRelocateXRefSection remarks, the comment at ~:4910 and the readInStreamsPlace param say 'a stream near the offset'. The code applies the rule to any object that declares a cross-reference stream. The remark 'An object that only gives a /W ... is dropped, the search going on to the next place' also leaves out that such an object with no /Prev is first deferred. lazy-reading.md:196 says 'any stream'. No reader sees a difference, since the dictionary is dropped either way.

The narrowing is on the fix. The reviewer's code fix (`parsed is PdfStream &&`) is wrong. It does bring the /W dictionary back to one reading. But it breaks an existing pin, CrossReferenceChainTests.A_place_the_search_near_a_hybrid_table_s_prev_passes_over_or_finds_no_section_at_leaves_nothing_of_its_reading(near: "stream 11", pad: 300). When MaxTrailerLength cuts stream 11's dictionary, the parse hands the keep test a dictionary, not a PdfStream, and that test cannot tell it from a plain dictionary. With the fix, stream 11 is kept, the guard's cut is reported, and the place is taken. Treating every declaring object as a possible own stream is therefore the conservative behavior, and only the wording needs to change.
REFUTER EVIDENCE: Probe /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r8ref-ZzR8PlainDictPassOverTests.cs.txt has the reviewer's shape: oldest table @110, stream 6 @1457 with /Prev 110 and more than 512 bytes off, a 1,200-byte object 10, object 12 @2810, and the newest table with /Prev 2803 /XRefStm 1457. A logging source records every read at 2810 and the reflected _relocationCandidatesTried.

At 012e13d (r8ref-plaindict-head.txt):
- `<< /W 1 /S /S >>`: readsAtPlace=[32,770,32,770,770] tried=2
- `/Prev 5` variant: [32,778,778] tried=1
- `<< /S /S >>`: [32,765,765] tried=1
- 5 copies of the /W dict: tried=10, against 5 for 5 copies of the /S dict
- every variant: sections=[startxref Read Table @3445 ; /XRefStm Read Stream @1457 ; /Prev NotFound Unknown @2803], diags=[xref.section-missing@2803 | xref.rebuilt]

At d3ef236 (r8ref-plaindict-d3e.txt): the /W dict gives [32,770,770] tried=1 and 5 copies give tried=5. Same sections and diagnostics.

With `mayBeOwn = parsed is PdfStream && MayBeTablesOwn(...)` (r8ref-plaindict-fix.txt): the /W dict gives [32,770,770] tried=1. But `--filter-class "*CrossReference*"` gives 1,042 total and 1 failed: A_place_the_search_near_a_hybrid_table_s_prev_passes_over_or_finds_no_section_at_leaves_nothing_of_its_reading(near: "stream 11", pad: 300), where the expected Sections (startxref Read @2626, /XRefStm NamedAgain @1457, /Prev Relocated Stream @1457, ...) no longer match. The worktree was restored to a clean 012e13d afterwards.
REFUTER FIX: Change the texts, not the code.
- XRefSectionRecord.MayBeTablesOwn (:149-156): replace 'holds a cross-reference stream that may be that table's own' with 'holds an object that declares a cross-reference stream and may be that table's own — a stream, or a dictionary no stream data follows, which the reading does not tell from a stream whose dictionary a guard cut —'.
- TryRelocateXRefSection remarks (:4833-4838), the loop comment (~:4910) and the readInStreamsPlace param: write 'an object near the offset that declares a cross-reference stream' wherever they now say 'a stream near the offset ... may be the table's own'.
- The sentence 'An object that only gives a /W ... is dropped, the search going on to the next place': add 'or, near a hybrid table's /Prev, where it gives no /Prev or the table's, once every other place was tried'.
- lazy-reading.md:196, optional: 'any stream' could read 'any object declaring a stream'.

Do not apply `parsed is PdfStream`: it breaks the pad-300 guard-cut row. A code alternative would pass a dictionary over only when a guard was reached during its parse. That would save one window and one attempt for an uncut dictionary, but it is optional and not needed.

######## [17] lens=src-texts key=passed-over-window-not-64kb sev=nit -> verdict=confirmed sev=nit maint=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:4843 (new in 3a1ac79), and the earlier :4812-4813
CLAIM: 'A place so passed over is read again when the search comes back to it, through a window of up to 64 KB' holds only at the default MaxTrailerLength. The window grows eightfold up to PdfReaderLimits.MaxTrailerLength, both on the first reading that passes the place over and on the retry.
EVIDENCE: The shape of the plain-dictionary nit, with object 12 a /Type /XRef stream (no /Prev) whose dictionary holds a /Pad of 70,000 bytes. With MaxTrailerLength 1 MB, the reads at 2815 are 32, 65536, 70824 (passed over), then 32, 65536, 70824 (retry, taken: '/Prev named 2808 at 2815 Stream Relocated'), then 8192, 65536, 70824 (rebuild). At the default limit: 32, 65536, then 32, 65536, and limit.trailer is reported.
FIX: Say 'through windows of up to PdfReaderLimits.MaxTrailerLength, 64 KB by default' in both sentences.
--- REFUTER: The finding holds for :4843, the sentence 3a1ac79 added. A place passed over is always a cross-reference stream. Both its first reading in TryParseNearHybridPrev and its retry, which is TryReadXRefStream with tablePrevious -1 once i >= inWindow, go through TryParseNumberedAt(XRefWindow, PdfLimit.Trailer). That call opens a 64 KB window and grows it eightfold up to _guard.Bound(Trailer), which is MaxTrailerLength. "Up to 64 KB" is therefore true only at the default MaxTrailerLength, or below it. The text understates the cost: no behavior is wrong and nothing is reported wrongly, so nit. The finding also understates :4812-4813, and its fix is wrong there. That sentence dates from main (21a0298:3232, word for word) and covers every place the search tries, classic tables included. TryReadClassicTable(candidate: true) grows its window up to MaxXRefSectionLength (64 MB by default; the threat model at :437 says "grows ×4 from 64 KB"). So a table candidate goes past 64 KB even at the default limits. Writing "up to MaxTrailerLength" there would swap one wrong bound for another. None of #171, #289, #295, #331, #52, decisions.md or rounds 1 to 7 holds this point. The cost note in r7-fix-result.json:86 repeats the same "one window of up to 64 KB" claim but was never triaged. The 3a1ac79 commit body gives no window size, so it is not false. No maintainer decision is needed.
REFUTER EVIDENCE: The probe is saved at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r8ref-ZzR8WinTests.cs.txt. I ran it on origin/ccr-6d6c0ee6-dqm4lm-188 at 012e13d, logging reads at the header of stream 12, at 2815. The file: a hybrid table whose /XRefStm reads stream 6 at 1457 (/Prev names the oldest table), more than 512 bytes from 2808; its /Prev names 2808, 7 bytes before stream 12, which is /Type /XRef with no /Prev and a 70,000-byte /Pad after /Type.
- Default limits: reads of 32 and 65536, then 32 and 65536 again; "Warning limit.trailer @2815: The dictionary of the object read as a cross-reference stream runs past 64 KB"; "/Prev named=2808 off=2815 Malformed Stream".
- MaxTrailerLength 1 MB: reads of 32, 65536, 70849 (passed over), then 32, 65536, 70849 (retry, taken: "/Prev named=2808 off=2815 Relocated Stream"), then two loads of 8192, 65536, 70849.
- /Pad of 600,000 bytes, MaxTrailerLength 8 MB: reads of 32, 65536, 524288, 600850 on the pass-over and the same four on the retry. The window grows eightfold toward MaxTrailerLength.
- Second probe for :4813: a /Prev naming 7 bytes before a classic table of 5000 rows (100 KB), default limits. Reads at 110 are 32, 65536, then 100214 (the source clamped a larger request to the file's end); "/Prev named=103 off=110 Relocated Table". A table candidate grows past 64 KB at the defaults.
- Main 21a0298 has the same :4813 text (line 3232) and the same growth in TryReadClassicTable (maxWindow = Bound(XRefSectionLength), canGrow).
REFUTER FIX: Edit both sentences in the TryRelocateXRefSection remarks of src/AdCodicem.Pdf/IO/PdfFileReader.cs.
- At :4843, replace "through a window of up to 64 KB" with "through windows that grow from 64 KB up to PdfReaderLimits.MaxTrailerLength, as a stream's dictionary is read".
- At :4812-4813, do not write MaxTrailerLength alone, because the sentence covers tables too. Say instead that each place "is read as a section is, through a window of 64 KB first, grown up to PdfReaderLimits.MaxXRefSectionLength for a table and PdfReaderLimits.MaxTrailerLength for a stream's dictionary".
Wording only, no maintainer decision.

######## [18] lens=src-texts key=near-prev-header-guard-reported sev=nit -> verdict=confirmed sev=nit maint=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:6525-6527
CLAIM: The TryParseNearHybridPrev summary says 'What is not kept leaves nothing of its reading — ..., a guard its window reached included'. A place whose header the window cuts is not kept, but TryParseNumberedAt reports the guard before the keep test is asked. Only what the keep test refuses leaves nothing, as TryParseNumberedAt's own doc ('when it says no') says.
EVIDENCE: The plain-dictionary nit's hybrid shape, with object 12's header written as 300 zeros then '12 0 obj' (a /Type /XRef stream with no /Prev) and MaxTrailerLength 256. Diagnostics include Warning limit.trailer @2815 'The dictionary of the object read as a cross-reference stream runs past 256 bytes; only what lies within it was read.'; '/Prev named 2808' is NotFound; xref.section-missing and a rebuild follow. The output is identical without the /XRefStm.
FIX: Write 'What the test refuses leaves nothing of its reading ...; a header the window cuts reaches the guard as TryParseNumberedAt reaches it.'
--- REFUTER: The finding holds as stated. Commit 3a1ac79 wrote the summary of TryParseNearHybridPrev (src/AdCodicem.Pdf/IO/PdfFileReader.cs:6521-6528), and nothing earlier settled it. The summary says "What is not kept leaves nothing of its reading — ... nothing it met is reported or recorded, a guard its window reached included (TryParseNumberedAt) —". In TryParseNumberedAt (:8016-8032), a place whose header the window's edge cuts takes the `!parser.TryReadIndirectObject` branch. Once windowSize reaches maxWindow, that branch calls ReachLimit and returns false without ever asking `keep`. So the method returns false (nothing kept, passedOver false), yet the guard is reported. Only what the keep test refuses leaves nothing, and that is all TryParseNumberedAt's own `keep` param says ("when it says no", :7961-7964). The caller's comment in TryReadXRefStream (:6013-6015) is narrower and still true: it says "a guard its dictionary reached", not "its window".

The behavior itself is no defect. It is the same as the plain candidate path (keep = DeclaresOnlyCrossReferenceStream), which was already there on d3ef236. The guard is reported once, and it is a real guard: leading zeros are valid integer syntax. Under default limits it cannot happen, because a header that the search's 1 KB window holds is far shorter than 64 KB. It needs a MaxTrailerLength below about 1 KB. No decision, issue (#171, #289, #295, #331) or earlier round covers this text. The test A_place_the_search_near_a_hybrid_table_s_prev_passes_over_or_finds_no_section_at_leaves_nothing_of_its_reading (CrossReferenceChainTests.cs:5598) covers a cut dictionary only, where keep is asked, so it does not contradict this. The severity stays nit: this is wording in a private member's XML summary, with no consequence for behavior or reports.
REFUTER EVIDENCE: I rebuilt the reproducer at 012e13d (probe kept at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r8ref-hdrguard/ZzR8HdrGuardTests.cs.txt, output at .../r8ref-hdrguard/head.txt). The file is a hybrid one: the /XRefStm reads stream 6 at 1457, and the table's /Prev names 2808. At 2815 there is `000…00012 0 obj` (300 zeros) holding `<< /Type /XRef /Size 13 /W [1 4 2] /Index [10 1] /Length 7 >>`, with no /Prev. MaxTrailerLength is 256.
  S startxref named=3820 Read Table; S /XRefStm named=1457 Read Stream; S /Prev named=2808 off=2808 NotFound Unknown
  D Warning limit.trailer @2815: The dictionary of the object read as a cross-reference stream runs past 256 bytes; only what lies within it was read. Raise PdfReaderLimits.MaxTrailerLength to read past it.
  D Warning xref.section-missing @2808: ... is not there, nor near it; ...
  D Repair xref.rebuilt @-1
The output is the same with no /XRefStm, which uses the plain candidate path, and the same when stream 12 gives /Prev 5 or no /Type. With 0 zeros, or default limits, the stream is found moved (xref.offset-adjusted @2815) and no limit.trailer is reported. tablePrevious >= 0 here, so TryReadXRefStream (:6026-6027) calls TryParseNearHybridPrev at 2815, and that call is the one that reports the guard.
REFUTER FIX: Narrow the summary at PdfFileReader.cs:6525-6528 to what the test refuses. For example: "What the test refuses leaves nothing of its reading — a search for its endstream aside, kept as every search is, nothing it met is reported or recorded, a guard its window reached included (TryParseNumberedAt) —, and a stream's data is neither decoded nor read as rows; a header the guard's window cuts, which reads no object to test, reaches the guard as at any place the search tries." Only the comment changes; no code or test changes.

######## [19] lens=tests key=guard-undecoded-theory-does-not-pin-its-guard sev=nit -> verdict=narrowed sev=nit maint=False
LOCATION: tests/AdCodicem.Pdf.Tests/GuardCutValueTests.cs:3782
CLAIM: The new theory A_section_whose_filters_decode_nothing_of_its_data_whatever_a_value_a_guard_cut_holds_cannot_be_read_as_read_whole never checks that the guard actually cut a value in its guarded read. Its sibling just above does check this (`Should().Contain(PdfDiagnosticCodes.LimitObject)`, :3732). Every assertion holds when no guard is in force at all, so the theory would still pass, testing nothing about a guard's cut, if object 5's padding or ObjectGuard ever stopped cutting. Today the guard is reached, so the test does hold its name for now.
EVIDENCE: Probe ZzR8tGuardOffTests copies the theory's assertions word for word but opens the guarded read with `new PdfReaderOptions()` (no guard). Its last line asserts there is no limit.object. Result on 012e13d: 'total: 4 failed: 0 succeeded: 4', so all four rows pass with no guard. A probe of the real guarded read shows the guard is reached today, e.g. '/W 5 0 R /Size 8 /FlateDecode: guard=1024 state=Malformed cut=False fault=its data could not be decoded codes=filter.failed xref.section-unreadable xref.rebuilt limit.object'.
FIX: Add `document.Diagnostics.Select(entry => entry.Code).Should().Contain(PdfDiagnosticCodes.LimitObject);` before the `Where(code => code != LimitObject)` comparison, as the sibling theory does at :3732.
--- REFUTER: The finding holds. One detail is narrowed, and that detail does not change the fix.

What holds:
- The theory at GuardCutValueTests.cs:3745-3784, new in 092b772, never pins that MaxObjectLength cuts object 5. Without that cut, its assertions just compare a whole read with a second whole read.
- Its sibling theory (:3732) pins the cut with Contain(LimitObject), and so does the older test at :1668-1670 (7f5c648). Every other test in the file that compares codes "but for limit.object" also pins that limit.object is there; this theory is the only exception.
- It matters more than the finding says. r7-fix-result.json's mutant list shows that two ADR 53 mutants are killed by this theory alone: M28 (FaultBesideCut's `return undecoded ? DataUndecoded : null` made `return null`, "killed 4, by the new guard theory") and M31 (RowsHeldAtMost dropping `|| undecoded`, "killed 3, by the new guard theory").
- I rebuilt M28. The real theory fails all 4 rows. Under the same mutant, the theory's assertions pass all 4 rows both with no guard and with the guard in force but object 5 unpadded, so not cut. So if the fixture stopped producing the cut, these two mutants would survive silently. That could happen if the 1,100-byte padding were shortened, or if MaxObjectLength stopped counting trailing white space (the direction #289's closing points).
- The test holds its name today. It is real: it failed on d3ef236, and it kills M28 at head. So this is a gap in exactness, not a false test. Round 7's [6] (a theory that would miss a doubled report) is the same kind of gap, and it was settled as a nit.

What is narrowed: the claim says the theory never checks that the guard cut a value "in its guarded read". At head, though, the section's read does not leave the limit.object report:
- With the default (non-throwing) guard, limit.object comes after xref.rebuilt.
- Under ThrowOnLimit, the guard first throws in the rebuild: FinishRebuild -> ExpandEachObjectStream -> GetObject(5). It is then rethrown at PdfReference.Resolve.

So the proposed pin, like the sibling's, proves that object 5 lies past the guard, as the rebuild reads it. It does not prove that the section's read went through the cut. That is still enough to catch the fixture drifting, which is the risk here. This is not a design choice for the maintainer.
REFUTER EVIDENCE: Built and run on origin/ccr-6d6c0ee6-dqm4lm-188 at 012e13d (Release, 0 errors). Probe source: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r8ref-ZzR8RefGuardPinTests.cs.txt. Probe removed afterwards; worktree clean.

(1) ZzTheoryAssertionsHoldWithoutACut copies the theory's assertions and its file. It adds NotContain(LimitObject) and runs 8 rows: the 4 InlineData rows with the guarded read opened under `new PdfReaderOptions()`, and the same 4 under ObjectGuard with object 5 unpadded. At head, all 8 pass.

(2) ZzGuardedReadReachesTheGuard. All 4 rows reach the guard at head. Output file: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r8ref-guardpin.txt (overwritten by the mutant run in (3), which appended the cut=True lines). Head lines as captured:
- '/W 5 0 R /Size 8 /FlateDecode: state=Malformed cut=False fault=its data could not be decoded codes=filter.failed xref.section-unreadable xref.rebuilt limit.object limitAt=3 msg=Object 5 runs past 1 KB, its stream data aside; ...'
- /Foo: the same, with filter.unsupported in place of filter.failed.
- '/W 5 0 R /Size 0' and '/W [1 4 2] /Index [5 0 R 8]': the same codes, limitAt=3.

(3) Mutant M28: PdfFileReader.cs:6907 `return undecoded ? DataUndecoded : null;` made `return null;`.
- The real theory fails 4/4 with 'Expected boolean to be False, but found True' (CutByLimit). The guarded read gives state=Malformed, cut=True, fault 'its /W is the reference 5 0 R, which one of the reader's limits cut', codes 'filter.failed limit.object'.
- Under the same mutant, the 8 no-cut probe rows still pass: total 16, failed 4 (the real theory), succeeded 12.
- Source restored with git checkout.

(4) ZzWhereTheGuardThrows, under MaxObjectLength=1024 with ThrowOnLimit=true. The original throw site is PdfLimitGuard.Reach <- PdfFileReader.ReachLimit <- TryParseNumberedAt <- ... <- GetObject <- ExpandEachObjectStream <- FinishRebuild <- FinishUnfinishedRebuild <- GetObject <- HasUsableRoot <- Initialize. After '--- End of stack trace from previous location ---', it is rethrown at PdfReference.Resolve <- PdfDocument.Open. That stack is in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r8ref-guardpin-where.txt.

(5) r7-fix-result.json, reports[0].mutants: 'M28 (return null) killed 4, by the new guard theory; ... M31 (drop `|| undecoded`) killed 3, by the new guard theory'.
REFUTER FIX: In GuardCutValueTests.A_section_whose_filters_decode_nothing_of_its_data_whatever_a_value_a_guard_cut_holds_cannot_be_read_as_read_whole, add this line just before the `Where(code => code != PdfDiagnosticCodes.LimitObject)` comparison (:3782), as the sibling theory does at :3732:

`document.Diagnostics.Select(entry => entry.Code).Should().Contain(PdfDiagnosticCodes.LimitObject);`

A stronger option, which holds on all 4 rows at head: replace both lines with `document.Diagnostics.Select(entry => entry.Code).Should().Equal([.. wholeCodes, PdfDiagnosticCodes.LimitObject]);`. That pins the guard as reached exactly once, after the rebuild, and pins that the chain's cut is not reported as the guard's before it.

Optionally, one comment line saying that the limit.object comes from the rebuild reading object 5, which the section's guarded read did not report.

Nothing to file. It is not a maintainer decision.

######## [20] lens=tests key=guard-undecoded-theory-comment-misstates-the-old-reading sev=nit -> verdict=refuted sev=none maint=False
LOCATION: tests/AdCodicem.Pdf.Tests/GuardCutValueTests.cs:3751
CLAIM: The comment says the encoded bytes on d3ef236 'held at most one row of the eight the stream declares'. That is true only of the /Index row. In the two rows where /W is the value the guard cut, the old reading said at most 7 of the 8 rows.
EVIDENCE: ZzGuardProbe run on a d3ef236 worktree, guarded reads (MaxObjectLength 1024): '/W 5 0 R /Size 8 /FlateDecode: state=Read cut=True fault=its data, 7 bytes decoded, holds at most 7 of the 8 rows its /Index and /Size declare'. The /Foo row is the same. The '/W [1 4 2] /Index [5 0 R 8]' row gives '... holds at most 1 of the 8 rows ...'. The '/W 5 0 R /Size 0' row gives 'state=Malformed cut=True fault=its /W is the reference 5 0 R, which one of the reader's limits cut'. That last result is the guard's, as the comment says.
FIX: Reword to 'which held fewer rows than the eight the stream declares — at most seven where /W was the cut value, one where /Index was —'.
--- REFUTER: The report numbers the finding quotes are right, but the comment is not false. It says the encoded bytes, taken for the decoded data, 'held at most one row of the eight the stream declares'. That is a statement about the data, not a quote of the guarded report. In all three rows that declare eight, object 5 holds /W [1 4 2], so the 7 bytes hold exactly one 7-byte row. The d3ef236 whole read counts exactly that in each of the three: 'it holds 1 row where its /Index and /Size declare 8'. The guarded read gave the looser bound 'at most 7' because it could not know the cut /W, and the comment never says the report counted one. Its point is that the data held fewer rows than declared, so the old rule made the section the guard's. That holds whether the bound is 1 or 7. The '/Size 0' row is covered by 'or, declaring none, it was the guard's, whatever its data', which the finding agrees with. 'That is true only of the /Index row' is wrong: the sentence is true in every row.
REFUTER EVIDENCE: I rebuilt the reproducer as the probe ZzGcmtProbe, on a fresh d3ef236 worktree (since removed). It uses the theory's own four rows, a whole read, then a guarded read with MaxObjectLength 1024.
- '/W 5 0 R /Size 8 | /FlateDecode', whole read: 'state=Read cut=False incomplete=True fault=it holds 1 row where its /Index and /Size declare 8'. The message is 'The cross-reference stream /Prev names at offset 1326 was read only in part: it holds 1 row where its /Index and /Size declare 8; the row it holds was kept, ...'.
- The same row, guarded: 'state=Read cut=True fault=its data, 7 bytes decoded, holds at most 7 of the 8 rows its /Index and /Size declare'.
- '/W 5 0 R /Size 8 | /Foo': the same, 1 row read whole and 'at most 7' guarded.
- '/W [1 4 2] /Index [5 0 R 8]': 1 row read whole, and 'at most 1 of the 8 rows' guarded.
- '/W 5 0 R /Size 0', guarded: 'state=Malformed cut=True fault=its /W is the reference 5 0 R, which one of the reader's limits cut'.
So the data held one row in every row that declares eight, as the comment says (tests/AdCodicem.Pdf.Tests/GuardCutValueTests.cs:3747-3751 on 012e13d). Only the guarded report's bound differs, and the comment does not state that bound.
REFUTER FIX: None needed. If the maintainer wants the comment to say both what the data held and what the old report said, an optional reword: 'which held one row of the eight the stream declares (the guarded report bounding it at seven where /W was the cut value)'. This is wording only.

######## [21] lens=tests key=may-be-own-theory-codes-not-exact sev=nit -> verdict=narrowed sev=nit maint=False
LOCATION: tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:5518
CLAIM: A_prev_falling_short_of_the_stream_its_xrefstm_read_takes_a_stream_that_may_be_the_table_s_own_once_no_other_place_holds_a_section is the only new own-stream theory that does not pin its exact report sequence. It checks one xref.offset-adjusted and the number of xref.chain-cycle reports. A doubled xref.rebuilt or object.redefined would pass, and so would a stray xref.hybrid-stream-lost, syntax.* or limit.* from a passed-over stream. Every sibling (before the table's own, inside the stream, leaves nothing, offset zero, the budget fact) pins its exact sequence.
EVIDENCE: Probe ZzOwnCodes on 012e13d prints the sequences the theory would have to pin. Row (streamPrev nothing, shortOfOwn False): 'xref.offset-adjusted xref.rebuilt object.redefined'. Row (the table's /Prev, True): 'xref.offset-adjusted xref.rebuilt object.redefined'. Row (the table's /Prev, False): 'xref.offset-adjusted xref.hybrid-stream-lost xref.chain-cycle xref.rebuilt object.redefined'. The theory's assertions (:5516-5522) check none of xref.rebuilt, object.redefined or xref.hybrid-stream-lost.
FIX: Replace the ContainSingle and Count checks with `document.Diagnostics.Select(d => d.Code).Should().Equal(taken == stream ? [OffsetAdjusted, HybridStreamLost, ChainCycle, Rebuilt, ObjectRedefined] : [OffsetAdjusted, Rebuilt, ObjectRedefined])`, and keep the message check on Diagnostics[0].
--- REFUTER: The facts reproduce. The theory at tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:5500-5523, added by 3a1ac79, checks four things: sections[0]; a single /Prev record in state Relocated (its offset and NamedOffset); a single xref.offset-adjusted with its message; and the number of xref.chain-cycle reports, plus WasRepaired. It pins neither the code list nor the full section list.

Among 3a1ac79's other new tests, every one pins its exact code list with Should().Equal: before the table's own :5480, A_stream_nothing_names :5541, inside the stream :5575, leaves nothing :5611, the budget Fact :5627, offset zero :5653/:5664. Round 7 settled the same kind of gap the same way: decisions.md 'Review round 7', [6], cut-table theory, narrowed to a nit and fixed by pinning the code list.

Three things narrow it.
(1) The test holds what its name says. It checks which stream is taken, and r7-fix-result says its first row fails on d3ef236.
(2) Nothing on the branch can double or add a report in this fixture, because stream 11 and stream 6 are well formed. The one path only this theory exercises is a stream other than the /XRefStm's that is passed over at its first read, put back on the list, then read again and taken (rows 'the table's /Prev'/true and 'nothing'/*). On a clean stream that path has nothing to report or leave behind. The pass-over leaving nothing is pinned exactly by the leaves-nothing theory.
(3) The codes the theory leaves unchecked are pinned on the same code paths elsewhere. The 'the table's /Prev' row of the :5401 theory pins [offset-adjusted, hybrid-stream-lost, chain-cycle, rebuilt, object.redefined] in that order. A_stream_nothing_names pins that no hybrid-stream-lost appears where a stream other than the /XRefStm's is taken.

What remains is an exactness nit. Of the new own-stream tests, this is the one theory that would not see a doubled or stray diagnostic. It also leaves the /XRefStm record's state unpinned (Read, or NamedAgain with ReadAs set). This is not a maintainer decision.
REFUTER EVIDENCE: Built origin/ccr-6d6c0ee6-dqm4lm-188 at 012e13d in Release: 0 warnings, 0 errors.

Probe ZzOwnCodesRefute (source: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r8ref-ZzOwnCodesRefuteTests.cs.txt) runs over HybridFiles.PrevFallingShortOfTheStreamBeforeTheTablesOwn("nothing", shortOfOwn, streamPrev). Its output:
- (nothing, False), stream=1683 own=1862: sections startxref Read Table 2579; /XRefStm Read Stream 1683; /Prev Relocated Stream 1862 named=1676. Diagnostics: xref.offset-adjusted ('...at offset 1676 was found 186 bytes after it.'), xref.rebuilt, object.redefined. WasRepaired=True.
- (the table's /Prev, True), own=1879: sections /XRefStm Read Stream 1683; /Prev Relocated Stream 1879 named=1872. Diagnostics: xref.offset-adjusted ('7 bytes after'), xref.rebuilt, object.redefined.
- (the table's /Prev, False): sections /XRefStm NamedAgain Unknown 1683; /Prev Relocated Stream 1683 named=1676. Diagnostics: xref.offset-adjusted, xref.hybrid-stream-lost, xref.chain-cycle ('...the /Prev of the section at offset 1683 names offset 1676...'), xref.rebuilt, object.redefined.
- (nothing, True): the same codes as row 1.

These are exactly the sequences the finding states. The theory's assertions check none of xref.rebuilt, object.redefined or xref.hybrid-stream-lost, and do not count them.

I applied the proposed exact assertion to the theory temporarily. The test project built with 0 warnings, and the theory's 3 rows passed. The worktree was restored afterwards.
REFUTER FIX: In A_prev_falling_short_of_the_stream_its_xrefstm_read_takes_a_stream_that_may_be_the_table_s_own_once_no_other_place_holds_a_section (:5516-5521), replace the ContainSingle and Count checks with the ternary-of-collection-expressions form the :5401 sibling already uses:

document.Diagnostics.Select(diagnostic => diagnostic.Code).Should().Equal(taken == stream ? [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefHybridStreamLost, PdfDiagnosticCodes.XRefChainCycle, PdfDiagnosticCodes.XRefRebuilt, PdfDiagnosticCodes.ObjectRedefined] : [PdfDiagnosticCodes.XRefOffsetAdjusted, PdfDiagnosticCodes.XRefRebuilt, PdfDiagnosticCodes.ObjectRedefined]);

then check document.Diagnostics[0].Message as it is now. Optionally pin the section list as well: startxref Read Table newest; /XRefStm (Read, Stream) or (NamedAgain, Unknown, with ReadAs being the /Prev record) at stream; /Prev Relocated Stream at taken. I checked that the code-list form compiles and passes all 3 rows at 012e13d. This is a test-only change, with no design question for the maintainer.

######## [22] lens=tests key=builder-summaries-wrap-and-scope sev=nit -> verdict=confirmed sev=nit maint=False
LOCATION: tests/AdCodicem.Pdf.Tests/LostStreamFiles.cs:23
CLAIM: Two small builder text problems. (1) The `written` addition to ThreeRevisions' summary broke the paragraph's wrap: one line ends after 'Past <paramref name="after"/> bytes of comment, the' and the sentence continues on the next. (2) UndecodedStreamFiles' class summary (UndecodedStreamFiles.cs:7-10) says its files 'give data their filters decode nothing of'. But UpdateInZlibData(null) writes sound zlib data that decodes to the update's rows, and is the control row of the writer's-update theory. The class also holds the general Zlib and StreamRows helpers.
EVIDENCE: `sed -n 17,27p LostStreamFiles.cs` shows ':23 /// fields of 1, 4 and 2 bytes, ... or <paramref name="written"/> when it is given.' then ':24 /// Past <paramref name="after"/> bytes of comment, the' then ':25 /// third writes object 6 ...'. A dump of UpdateInZlibData(null) (r8t/dump/zlib-sound.pdf) holds stream 6 '/Filter /FlateDecode /Length 19' with data 789c6364606094066220326560000001df0055, which decodes; CrossReferenceChainTests' damagedAt:null row passes with no diagnostic.
FIX: Rewrap ThreeRevisions' summary paragraph. Reword UndecodedStreamFiles' summary to 'Files whose cross-reference streams give data their filters may decode nothing of — a filter the library does not know, or Flate data corrupt before its first byte decodes — and a writer's sound update to set them against', or similar.
--- REFUTER: Both points hold at 012e13d, and both come from 092b772, a round 7 fix that no earlier round saw. No issue, decision or earlier finding covers them. (1) The wrap break is real. In 092b772 the clause about `written` was added to line 23 and the next sentence was pushed onto a new line 24, which runs to 59 columns. Lines 17 to 26 around it run 99 to 125. The words are right; only the wrap is wrong. (2) The summary of the UndecodedStreamFiles class says every file in the class gives data its filters decode nothing of. That is not true of the class as its tests use it. UpdateInZlibData(null) writes sound zlib: Zlib changes a byte only when damagedAt is given. That call is the null row of A_writer_s_update_in_zlib_data_serves_the_new_revision_of_what_it_rewrites_whether_or_not_its_rows_decode, which asserts no diagnostic and WasRepaired false. The reviewer missed a second control: OverAnOlderTable(string.Empty, "\0xxxxxx") at CrossReferenceChainTests.cs:652 gives no /Filter, and its data is read as rows. Each method's own summary is accurate (UpdateInZlibData's says 'Where damagedAt is given'; OverAnOlderTable's says 'none when empty'). Only the class summary overstates, and it does so in test support, not in src/ or the docs, so this is a nit.
REFUTER EVIDENCE: `git blame -L 17,27 tests/AdCodicem.Pdf.Tests/LostStreamFiles.cs` attributes lines 23 and 24 to 092b7722. The other lines come from 8c33e4bf. Line lengths for lines 17 to 26: 105, 120, 118, 112, 125, 122, 120, 59, 123, 99. In 092b772's diff, the original line '... whatever <c>/W</c> the entries give. Past <paramref name="after"/> bytes of comment, the' became two lines, '... whatever <c>/W</c> the entries give, or <paramref name="written"/> when it is given.' and '/// Past <paramref name="after"/> bytes of comment, the'. UndecodedStreamFiles.cs:7-10 has a single commit in its history, 092b772, and reads: 'Files whose cross-reference streams give data their filters decode nothing of: a filter the library does not know, or Flate data corrupt before its first byte decodes. Such data holds none of the section's rows ...'. UndecodedStreamFiles.cs:56 calls `Zlib(StreamRows(...), damagedAt)`. Zlib changes `data[at]` only under `if (damagedAt is { } at)`. CrossReferenceChainTests.cs:665-679, theory rows [InlineData(null)] and [InlineData(2)], asserts `damagedAt is null ? []` for diagnostics and `WasRepaired.Should().Be(damagedAt is not null)`. CrossReferenceChainTests.cs:648-660, A_cross_reference_stream_under_no_filter_gives_its_data_as_rows_whatever_its_bytes, calls OverAnOlderTable(string.Empty, "\0xxxxxx") and asserts no diagnostic, object 3 as PdfNull, and no repair. I did not rebuild to run them: the claim concerns text, and the code path is plain from the source.
REFUTER FIX: (1) Rewrap LostStreamFiles.cs:22-26 so the sentence runs on: '... whatever <c>/W</c> the entries give, or <paramref name="written"/> when it is given. Past' / '<paramref name="after"/> bytes of comment, the third writes object 6 <c>(only in the newest)</c>, and object 9 as' / '<paramref name="nine"/> when one is given, and a classic table at <c>Newest</c> that indexes them and names the' / 'stream through <c>/Prev</c>.' (2) Reword the UndecodedStreamFiles summary so it also names the controls, for example: 'Files whose cross-reference streams give data their filters decode nothing of: a filter the library does not know, or Flate data corrupt before its first byte decodes. Such data holds none of the section's rows, whatever its bytes would read as (#221). The same files, with a stream under no filter or with sound zlib data, set them against. Offsets are counted from the header, which starts the file.' The class also holds the general Zlib and StreamRows writers; the summary need not list them. This is a test-text fix only and needs no decision from the maintainer.

######## [23] lens=docs key=tm442-untyped-undecoded-stream-older-rows-serve sev=minor -> verdict=confirmed sev=minor maint=False
LOCATION: docs/threat-model.md:442 (the stream-rows row, cell 1, sentence 'Data its filters decode nothing of ... and the objects it numbers are found by rebuilding the index ([#221])')
CLAIM: The row says data its filters decode nothing of holds no row, the stream cannot be read with the fault 'its data could not be decoded', and 'the objects it numbers are found by rebuilding the index'. Only the /Prev hand-on and the refusal are limited to a stream of /Type /XRef. The rebuild clause reads as holding for every such stream, and it does not. Take a stream of no /Type that gives a /W (the accepted guess path, B19-1/#263) with an older section within 512 bytes. The search drops the stream and takes the older section. The stream's fault is never reported, nothing is rebuilt, and the older revision serves the objects the stream renumbers. That is the threat row 435 names, an older revision's row standing in silence. Row 434 ('An object that only gives a /W is a guess, looked for nearby and dropped where it cannot be read') states the rule, so the two rows contradict each other.
EVIDENCE: Probe ZzR8DocsTests.Undecoded("/Foo", raw valid row, prevNamed: true, typed: false, gap: 0). The file: the oldest table (old three) at 137, then stream 5 at 307, '<< /Size 6 /Index [3 1] /W [1 4 2] /Root 1 0 R /Prev 137 /Filter /Foo ... >>', which writes object 3 anew, then a newest table whose /Prev is 307. Probe saved at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r8docs/ZzR8DocsTests.cs.txt; output in r8docs/dump-head.txt and dump-main.txt, row q1-guess-near. Head 012e13d: 'S /Prev named=307 off=137 Relocated Table'; 'Warning filter.unsupported @405'; 'Repair xref.offset-adjusted @137: The cross-reference section /Prev names at offset 307 was found 170 bytes before it.'; 'O 3="old three" repaired=False'. The only validator finding is 'Warning xref.section-shifted [offset 137]'. Main 21a0298: the stream is read at 307, object 3 is "new three", and the validator gives no finding. Control: the same stream with /Type /XRef (q1-typed-near) gives section-unreadable '... its data could not be decoded. The chain goes on through its /Prev; ...', and 3="new three" through the rebuild. Untyped with no section near (q1-guess-far): Malformed, no hand-on, rebuilt at opening, 3="new three".
FIX: Bring the rebuild clause inside the /Type /XRef parenthesis and name the guess. For example: '... as one whose rows cannot be read, above — a stream of /Type /XRef handing its /Prev on, the numbers it declares refused, the objects it numbers found by rebuilding the index; a stream that only gives a /W, a guess, dropped where a section near it reads, the rows of that older section then standing for what it numbers, with nothing reported but the filter and the offset adjusted ([#263]) —'.
--- REFUTER: The finding holds, and it is about the text only. Commit ed61728 (a round 7 fix; main has no such sentence) wrote the threat-model row 442 sentence. In it, the hand-on and the refusal are limited to a stream of /Type /XRef, but the clause 'and the objects it numbers are found by rebuilding the index ([#221])' sits outside that parenthesis. So it reads as true of every stream whose filters decode nothing of its data. The same goes for the fault being 'its data could not be decoded'.

It is not true for one shape: a stream of no /Type that gives a /W, with a section within 512 bytes. That stream is a guess (B19-1/#263). The search drops it and takes the nearby section, whose rows then serve the objects the stream renumbers. Nothing is rebuilt, and the stream's 'could not be decoded' fault is never reported. Row 434 states the rule ('An object that only gives a /W is a guess, looked for nearby and dropped where it cannot be read'), so for this shape row 442 contradicts it.

The behavior itself is accepted (decisions.md, consequence (b): PrevNamingAStreamBesideTheOldestTable with /Foo reads 'old seven'). The difference with main also comes from that accepted change: main read the raw /Foo bytes as rows, and the probe happens to write raw bytes that form a valid row. So no code defect is claimed. Only the new text misstates the accepted consequence, which the brief allows to be reported. lazy-reading.md:151-160 gets it right: there, the hand-on and the rebuild belong to a stream whose dictionary gives /Type /XRef. Row 442 is the only page that widens them.
REFUTER EVIDENCE: I rebuilt the reproducer myself at 012e13d (origin/ccr-6d6c0ee6-dqm4lm-188, Release, 0 warnings). The probe is saved at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r8ref-guessnear/ZzR8RefGuessNearTests.cs.txt and its output at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r8ref-guessnear/out-head.txt.

The file: an oldest table at 137 indexes 1, 2 and 3 ('old three'). Next comes 3 0 obj ('new three'), then stream 5 0 at 307: '<< /Size 6 /Index [3 1] /W [1 4 2] /Root 1 0 R /Prev 137 /Filter /Foo /Length 7 >>', its raw row pointing at the new object 3. Last comes a newest table, /Prev 307.

guess-near-foo (head):
  S /Prev named=307 off=137 Relocated Table
  D Warning filter.unsupported @405
  D Repair xref.offset-adjusted @137: The cross-reference section /Prev names at offset 307 was found 170 bytes before it.
  O 3="old three" repaired=False
  F Warning xref.section-shifted [offset 137]
There is no section-unreadable, no 'could not be decoded' and no rebuild.

Controls:
- typed-near-foo (/Type /XRef added): 'S /Prev named=307 off=307 Malformed Stream', then section-unreadable '... its data could not be decoded. The chain goes on through its /Prev; ... looked for by rebuilding the index ...', then 3="new three" after xref.rebuilt.
- guess-far-foo (no section within 512 bytes): Malformed, 'could not be decoded', rebuilt at opening, 3="new three".
- guess-near-nofilter: the stream is read, 3="new three".

Text at docs/threat-model.md:442: '... holds no row, whatever its bytes would read as: the stream cannot be read, its fault `its data could not be decoded`, as one whose rows cannot be read, above — a stream of `/Type /XRef` handing its `/Prev` on, the numbers it declares refused —, and the objects it numbers are found by rebuilding the index ([#221])'. It was added by ed61728 (absent on 21a0298). The test CrossReferenceChainTests.An_xrefstm_goes_on_past_its_table_s_prev_where_the_prev_names_a_stream_of_no_type_whose_filters_decode_nothing_of_it pins the relocation to the oldest table for this guess. It does not pin which revision of the renumbered object is served.
REFUTER FIX: This is a text-only fix in docs/threat-model.md:442: move the fault and the rebuild inside the /Type /XRef case and state the guess. For example: '... holds no row, whatever its bytes would read as: a stream of `/Type /XRef` cannot be read, its fault `its data could not be decoded`, as one whose rows cannot be read, above, handing its `/Prev` on, the numbers it declares refused and the objects it numbers found by rebuilding the index ([#221]); an object that only gives a `/W` is a guess, dropped where a section within 512 bytes reads, that section's rows then standing for what it numbers, nothing reported of it but `filter.unsupported` or `filter.failed` and the offset adjusted, and reported so, `its data could not be decoded`, only where nothing near it reads ([#263]); ...'.

Optionally, add a pin that object 3 is served from the nearby section in this shape, or name the consequence in row 434's guess sentence. The behavior is the maintainer's accepted rule, so no design choice is needed.

######## [24] lens=docs key=diag-undecoded-unreadable-code-under-startxref sev=nit -> verdict=confirmed sev=nit maint=False
LOCATION: docs/website/docs/reference/diagnostics.md:117 (xref.entry-reserved), :133 (filter.failed), :135 (filter.unsupported)
CLAIM: These rows say a cross-reference stream whose filters decode nothing of its data cannot be read, and point to '(`xref.section-unreadable`, #221)', as if the reader always reports that code. When startxref names such a stream, the reader reports only the filter code and then xref.rebuilt. Row 112 of the same page says so ('The section startxref names that cannot be read hands nothing on: the whole file is rebuilt, which is all the reader reports (#323)'). Someone who looks up filter.failed or filter.unsupported for such a file expects a code that never comes. The fault is named only by the validator's xref.section-malformed.
EVIDENCE: Probe rows q1-startxref-foo and q1-startxref-flate, built by UndecodedStreamFiles-like files whose stream 5 is named by startxref, under /Foo or under /FlateDecode with data 'xxxxxxx' (r8docs/dump-head.txt). Head 012e13d gives 'S startxref named=1509 off=1509 Malformed Stream', then 'D Warning filter.unsupported @1619' (or 'D Warning filter.failed @1627: A Flate stream could not be decoded.'), then 'D Repair xref.rebuilt', then object.redefined. No xref.section-unreadable is reported. The validator gives 'F Error xref.section-malformed [offset 1509]: The cross-reference stream at offset 1509 cannot be read: its data could not be decoded.'
FIX: Qualify each parenthesis. For example: '(`xref.section-unreadable` where a /Prev or an /XRefStm names it; where startxref does, the file is rebuilt and `xref.rebuilt` alone is reported, [#323](...); [#221](...))'. Alternatively, link to row 112's startxref clause.
--- REFUTER: The finding holds. Round 7 added three parentheticals, at docs/website/docs/reference/diagnostics.md:117 (xref.entry-reserved), :133 (filter.failed) and :135 (filter.unsupported). Each says a cross-reference stream whose filters decode nothing of its data cannot be read, followed by "(`xref.section-unreadable`, [#221])", with no qualifier. None of the three is in d3ef236 or main 21a0298, and on main no other row cites xref.section-unreadable in a parenthesis.

When startxref names such a stream, the reader reports the filter code, then xref.rebuilt, and never xref.section-unreadable. The brief accepts this behavior (#323, placed in batch 22). Row 112 of the same page states it correctly: "The section `startxref` names that cannot be read hands nothing on: the whole file is rebuilt, which is all the reader reports (#323)". threat-model.md:435 also scopes it. The three new parentheticals are the only texts that contradict it, so this falls under "report only if a text misstates it".

The code behaves as decided and as #323 records, so this is a text defect and not a code defect. It is a nit, not minor. The parenthetical points to the xref.section-unreadable row, which states the startxref exception. The code actually reported (filter.unsupported or filter.failed) is correct. Round 7 rated similar unqualified clauses as nits ([2], [10]). The texts become true once #323 makes xref.section-unreadable cover the first section.
REFUTER EVIDENCE: I rebuilt the reproducer myself with a file-based probe against src/AdCodicem.Pdf at 012e13d: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r8ref-undecsx/probe.cs. It builds the same shape as the reviewer's file: object 5 is a /Type /XRef stream at 1509 with /Index [3 1] /W [1 4 2] /Prev 137 and data 'xxxxxxx'; the oldest table at 137 alone indexes object 3; object 3 is redefined before the stream.

Head 012e13d, stream named by startxref:
- /Foo: 'Warning filter.unsupported @1619: The filter /Foo is not supported; decoding stopped there.', then 'Repair xref.rebuilt', then 'Information object.redefined'. WasRepaired is True at opening and GetObject(3) gives "new three". The validator gives 'Error xref.section-malformed: The cross-reference stream at offset 1509 cannot be read: its data could not be decoded.' No xref.section-unreadable, before or after GetObject.
- /FlateDecode: the same sequence, with 'Warning filter.failed @1627: A Flate stream could not be decoded.' first.
- /Fl: the same sequence, with 'filter.unsupported @1618' first.

Control, the same stream named by a newest table's /Prev: '... filter.unsupported' (or 'filter.failed'), then 'Warning xref.section-unreadable @1509: The cross-reference stream /Prev names at offset 1509 is there but cannot be read: its data could not be decoded. The chain goes on through its /Prev; ...'.

The reviewer's dump (r8docs/dump-head.txt, rows q1-startxref-foo, -flate, -hexfoo and -foo-64hex) and the undecoded refuter's r8ref-undecnamed-out.txt (an integer /Filter, /Foo, /FlateDecode under startxref, each giving only xref.rebuilt) agree.

`git diff d3ef236..012e13d -- docs/website/docs/reference/diagnostics.md` shows the three parentheticals were added in this round. The text of #323 confirms that the behavior is known and that what closes it only touches row 112.
REFUTER FIX: Add the startxref exception to each of the three parentheticals, the way row 112 and threat-model.md:435 state it. For example, at :117, :133 and :135: '(`xref.section-unreadable` where a `/Prev` or an `/XRefStm` names it; the section `startxref` names, which cannot be read, is answered by rebuilding the whole file, `xref.rebuilt` alone reported ([#323](https://github.com/AdCodicem/AdCodicem.Pdf/issues/323)); [#221](https://github.com/AdCodicem/AdCodicem.Pdf/issues/221))'. A shorter option is '(`xref.section-unreadable` but where `startxref` names it, [#323](...); [#221](...))'.

Optionally, add a line to #323's 'What closes it' so that the parentheticals at :117, :133 and :135 drop the exception once the first section is reported. The maintainer does not need to decide anything: this only brings the text in line with behavior that is already accepted.

######## [25] lens=docs key=decodes-nothing-vs-decodes-to-nothing-and-unknown-filter-in-bound sev=nit -> verdict=narrowed sev=nit maint=False
LOCATION: docs/website/docs/reference/reader-limits.md:130-132 (new sentence) and :122 ('a Flate filter within it that decodes nothing ends decoding first, reported as filter.failed alone'); docs/website/docs/reference/diagnostics.md:112 and :117 ('Flate data of which nothing decodes'); docs/threat-model.md:616 ('Flate data that decodes nothing')
CLAIM: These pages name the undecoded Flate case 'decodes nothing'. Elsewhere it is worded exactly: threat-model.md:442, validation-rules.md:54 and lazy-reading.md:112 say 'Flate data corrupt before its first byte decodes', and diagnostics.md:133 and :264 say nothing decodes 'before the fault'. Flate data that decodes, whole, to nothing (a sound empty zlib stream) is not undecoded. Alone, it is read in part (0 rows). Ahead of more filters than MaxFilterChainLength, it is the guard's cut: limit.filter-chain and Information xref.checked-in-part, the rows unknown. So reader-limits' new 'a cross-reference stream whose Flate filter within the bound decodes nothing cannot be read whatever the limit, the file's fault rather than the guard's' is false under the plain reading. Its sentence also leaves out the other undecoded case within the bound: a filter the library does not know ends decoding before the bound is reached, and that is the file's fault as well.
EVIDENCE: Probe rows in r8docs/dump-head.txt. The stream's /Prev is the oldest table, and startxref names the stream unless the row says /Prev.
- q1-startxref-emptyflate, sound zlib of empty input under /FlateDecode: 'Read Stream'; 'Warning xref.section-unreadable ... was read only in part: it holds 0 rows where its /Index and /Size declare 1'; 'F Error xref.section-malformed ... is malformed: it holds 0 rows'.
- q1-startxref-emptyflate-64hex, the same zlib data under [/FlateDecode + 64 x /ASCIIHexDecode]: 'Read Stream'; 'Warning limit.filter-chain @2653: ... decoding stopped after the first 64'; 'F Information xref.checked-in-part'. No filter.failed is reported, and nothing is charged to the file.
- The /Prev-named variant, q1-prev-emptyflate-64hex: object 3=null, and the older table is not read. Main gives the same for both rows, so the behavior predates round 7; only the new text is at issue.
- Corrupt data, q1-startxref-corruptflate-64hex: 'Malformed Stream', 'filter.failed', 'Error xref.section-malformed ... could not be decoded'. This matches the page under the 'corrupt' reading.
- Unknown filter within the bound, q1-startxref-foo-64hex ([/Foo + 64 x /ASCIIHexDecode]): 'Malformed Stream', 'Warning filter.unsupported', no limit.filter-chain, 'Error xref.section-malformed ... could not be decoded'.
FIX: In reader-limits.md:130-132, write: 'But a cross-reference stream whose filters within the bound decode nothing of its data — Flate data corrupt before its first byte decodes, or a filter the library does not know — cannot be read whatever the limit: the file's fault rather than the guard's'. Use the same exact wording at :122, at diagnostics.md:112 and :117 ('Flate data corrupt before its first byte decodes') and at threat-model.md:616. Optionally add that Flate data decoding to nothing ahead of the bound is the guard's cut.
--- REFUTER: The behavior reproduces exactly at 012e13d. The finding is right that the new texts use a looser phrase than the exact one elsewhere, and that reader-limits' new 'But' sentence names only Flate. Its claim that the sentence is 'false under the plain reading' goes too far, for three reasons.
(1) In the same bullet, reader-limits.md:121 already ties 'a Flate filter within it that decodes nothing' to the case that 'ends decoding first, reported as filter.failed alone'. That wording predates the batch (it is :99 on main 21a0298). A sound empty zlib stream reports no filter.failed and does not end decoding first. So the page's own definition already leaves it out, and :131 reuses that phrase rather than adding a new ambiguity.
(2) diagnostics.md:112 and :117 place 'Flate data of which nothing decodes' inside the frame 'data its filters decode nothing of', which refers to the input. Read that way it is accurate: a sound empty zlib stream decodes all of its input, to an empty output.
(3) The empty-data case is the accepted read-in-part path (R7 triage: 'Empty data already takes the read-in-part path and serves right'), and the guard's handling of it ahead of the bound predates round 7.
What does hold, as a nit:
(a) Four new places shorten the exact wording. validation-rules.md:54, lazy-reading.md:113-114 and threat-model.md:442 say 'Flate data corrupt before its first byte decodes', and diagnostics.md:133 and :264 say 'before the fault'. diagnostics.md:112 and :117, threat-model.md:616 and reader-limits.md:131 drop that precision. threat-model.md:616, 'Flate data that decodes nothing', is the loosest: it could be read as 'decodes to nothing'.
(b) reader-limits.md:130-132 carves out a Flate filter that fails within the bound, but not a filter the library does not know within the bound. That filter also ends decoding before the bound, reports filter.unsupported with no limit.filter-chain, and makes the stream Malformed with 'could not be decoded'. Strictly, 'met so' (still encoded by the filters past the bound) covers neither case, so the omission is an incomplete clarification rather than a false statement. It repeats an omission :121 already had on main. No wrong object, report or behavior follows. The issue is wording only.
REFUTER EVIDENCE: Probe rebuilt independently on origin/ccr-6d6c0ee6-dqm4lm-188 (012e13d), Release build. Probe source: an uncommitted Zz test, now removed. Output: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r8refute-emptyflate/out.txt. Each case was opened at MaxFilterChainLength 64 (default) and 65.
- Sound empty zlib under /FlateDecode, at 64 and 65: 'S startxref off=1509 Read Stream'; 'Warning xref.section-unreadable: ... was read only in part: it holds 0 rows where its /Index and /Size declare 1'; 'F Error xref.section-malformed: ... is malformed: it holds 0 rows'.
- The same data under [/FlateDecode + 64 x /ASCIIHexDecode], startxref-named, at 64: 'Read Stream cutByLimit=True'; 'Warning limit.filter-chain ... Raise PdfReaderLimits.MaxFilterChainLength'; 'Repair xref.rebuilt'; 'F Information xref.checked-in-part'. Prev-named at 64: 'O 3=null'. At 65: read in part (0 rows), 'O 3="new three"', Error section-malformed 'holds 0 rows'. No filter.failed in any of these rows.
- Corrupt data 'xxxxxxx' under [/FlateDecode + 64 hex], at 64 and 65: 'Malformed Stream'; 'Warning filter.failed: A Flate stream could not be decoded.'; no limit.filter-chain; 'F Error xref.section-malformed: ... cannot be read: its data could not be decoded.' This matches reader-limits :121 and :131 under the filter.failed reading.
- [/Foo + 64 hex], at 64 and 65: 'Malformed Stream'; 'Warning filter.unsupported: The filter /Foo is not supported; decoding stopped there.'; no limit.filter-chain; 'Error xref.section-malformed ... could not be decoded'. Prev-named: the chain goes on through its /Prev, and the rebuild serves 'new three'.
Text: reader-limits.md:121, 'a Flate filter within it that decodes nothing ends decoding first, reported as `filter.failed` alone', is on main (line 99). Lines :130-132, diagnostics.md:112 and :117, and threat-model.md:616 are new in ed61728 (git diff d3ef236..012e13d). PdfFilterPipeline.cs:297 sets undecoded only for non-empty data at Unsupported, NotAName or NotAFilterEntry, plus the Flate `failed` path at :265-270, so empty output is never 'undecoded' (decisions.md R7 triage: 'Empty data already takes the read-in-part path').
REFUTER FIX: Wording only, no code change. In reader-limits.md:130-132, write: 'But a cross-reference stream whose filters within the bound decode nothing of its data — Flate data corrupt before its first byte decodes, or a filter the library does not know — cannot be read whatever the limit, the file's fault rather than the guard's (#221).' In threat-model.md:616, replace 'or Flate data that decodes nothing' with 'or Flate data corrupt before its first byte decodes'. Optional, for consistency only: diagnostics.md:112 and :117 'Flate data corrupt before its first byte decodes', and reader-limits.md:121 'a Flate filter within it corrupt before its first byte decodes, or a filter the library does not know, ends decoding first, reported as filter.failed or filter.unsupported alone' (pre-existing on main). It is not necessary to add a sentence on empty Flate output ahead of the bound: the paragraph's general rule ('rows unknown until you raise the limit') already states what the code does.

######## [26] lens=docs key=deferred-streams-order-unstated-in-tm432-and-diag115 sev=nit -> verdict=narrowed sev=nit maint=False
LOCATION: docs/threat-model.md:432 (cell 1, 'a cross-reference stream ... once every other place was if it names the table's ...'. 'Any other stream near the offset ... is tried once every other place was, nearest first'); docs/website/docs/reference/diagnostics.md:115 (the same two clauses)
CLAIM: Both texts say the stream the /XRefStm read, when its /Prev names the table's, is tried 'once every other place was'. Separately, they say any other stream that may be the table's own is tried 'once every other place was, nearest first'. The code puts both kinds in one nearest-first order among the deferred places, an accepted deviation. Read as written, each clause is false whenever the other kind is present, so the texts never say which of the two goes first. In one of those shapes the threat row 432 calls closed comes back. The update's own stream lies nearer than a stream the /XRefStm read whose /Prev is the table's. It is then still taken before that stream, the chain ends there, the index is rebuilt at opening, a freed object is served and the validator says the file holds no catalog. lazy-reading.md:195-197 has it right: 'any stream near the offset whose own dictionary gives no /Prev, or the one the table's names, ... is tried once every other place was, nearest first'.
EVIDENCE: Probe rows built by HybridFiles.PrevFallingShortOfTheStreamBeforeTheTablesOwn("nothing", shortOfOwn, "the table's /Prev"), output in r8docs/dump-head.txt.
- q2-near6-tableprev (shortOfOwn false): 'S /Prev named=1676 off=1683 Relocated Stream' takes stream 6 first, then 'xref.chain-cycle @1676', a rebuild, and 'F Error xref.chain-loop'. Stream 11, another place, is never tried, so stream 6 was not tried 'once every other place was'.
- q2-near11-tableprev (shortOfOwn true): 'S /Prev named=1872 off=1879 Relocated Stream' takes stream 11, the update's own, before stream 6, which the /XRefStm read. Then 'repairedAtOpen=True', 'O 8="eight, which stream 6 frees"', and 'F Error file.root-invalid ... No object of the file is a catalog.'
CrossReferenceChainTests.A_prev_falling_short_of_the_stream_its_xrefstm_read_takes_a_stream_that_may_be_the_table_s_own_once_no_other_place_holds_a_section pins both rows, and it passes at 012e13d.
FIX: Fold the two clauses into one, as lazy-reading.md does. For example: '... once every other place was, nearest first among the streams that may be the table's own, the stream the /XRefStm read among them where its /Prev names the table's, and never where it gives none'. At threat-model.md:432, also say that such a stream nearer than the stream the /XRefStm read, when that stream's /Prev names the table's, is still taken first, the chain ending or looping either way.
--- REFUTER: Only the wording point holds. Two texts never say how the two kinds of deferred stream are ordered against each other. The claim that a threat the row calls closed comes back does not hold.

1. The behavior is as the finding says, and it is the accepted deviation. At 012e13d, TryRelocateXRefSection (src/AdCodicem.Pdf/IO/PdfFileReader.cs, around lines 4913-4930) sorts candidates nearest first. It puts each passed-over stream back at the end of the list, so all deferred streams are tried in one nearest-first order: the stream the /XRefStm read when its /Prev names the table's, and any other stream with no /Prev or the table's. CrossReferenceChainTests:5500 pins both orders. R7-Q2 did not fix the order among deferred streams, and the brief lists one nearest-first order as accepted.

2. The texts leave the order unstated rather than misstating it. threat-model.md:432 and diagnostics.md:115 put the stream the /XRefStm read ('once every other place was if it names the table's') and 'any other stream ... once every other place was, nearest first' in two clauses. Only the second says 'nearest first'. A reader may think the /XRefStm's stream comes after every other deferred stream, which q2-near6-tableprev disproves. The finding's 'each clause is false' reading is too literal: by that reading lazy-reading.md:195-197, which the finding calls right, would be false too. Taking 'every other place' as the places that are not deferred, nothing is false; the relative order is simply missing. lazy-reading.md:195-197, the method's remarks (PdfFileReader.cs:4833-4838) and the source comment at :4914-4919 state it as one group.

3. 'The threat row 432 calls closed comes back' is wrong in substance. In that shape the stream the /XRefStm read has its own /Prev naming the table's /Prev offset, so taking it first also loops the chain. The row already says such a stream's /Prev is reported 'as the chain finds it: a loop'. q2-near6-tableprev takes stream 6 first and gives:
- the same rebuild at opening;
- the same freed object served from it;
- an Error, xref.chain-loop instead of file.root-invalid.
The harm R7-Q2 closed, a chain that reads whole lost to the update's own stream, cannot happen here. Whichever stream goes first, the chain ends or loops; only which Error the validator gives changes.

The fix's second half asks the row to say this shape still ends or loops the chain. The row already implies it through 'a loop', so that half is not needed.
REFUTER EVIDENCE: Code at 012e13d, PdfFileReader.cs:4913-4930:
- `candidates.Sort(...)` orders candidates by distance.
- The comment reads 'Such a stream is tried once every other place was, nearest first, but for the stream the /XRefStm read when it gives no /Prev ... the stream the /XRefStm read among them ... A place passed over is put back at the end of the list'.
- `passOver = i < inWindow ? tablePrevious : -1`, so deferred streams keep their nearest-first order among themselves.

Probe rows, lot19/r8docs/dump-head.txt.

q2-near11-tableprev (stream 11 nearer):
- '/XRefStm named=1683 off=1683 Read Stream'
- '/Prev named=1872 off=1879 Relocated Stream'
- repairedAtOpen=True
- 'O 8="eight, which stream 6 frees" repaired=True'
- 'F Error file.root-invalid'

q2-near6-tableprev (stream 6 nearer, taken first):
- '/Prev named=1676 off=1683 Relocated Stream'
- 'D Warning xref.chain-cycle @1676'
- repairedAtOpen=True
- 'O 8="eight, which stream 6 frees" repaired=True'
- 'F Error xref.chain-loop'
- 'F Error xref.hybrid-stream-wrong'

Either order rebuilds at opening and serves the freed object 8.

Texts:
- threat-model.md:432 has the two clauses quoted in the finding, 'nearest first' only on 'Any other stream ...'.
- diagnostics.md:115 has the same split: 'once every other place was if that /Prev names the table's, and never if it gives none; any other stream near that offset ... is tried once every other place was, nearest first'.
- lazy-reading.md:195-197 has one group: 'any stream near the offset whose own dictionary gives no /Prev, or the one the table's names, ... is tried once every other place was, nearest first, but never the stream the /XRefStm read where it gives none'.
- R7-Q2 in decisions.md: 'is tried after every other place ... the stream the /XRefStm read keeps its nearest-first place when its /Prev names another offset'. It sets no order among the deferred streams.
- The test at CrossReferenceChainTests.cs:5500 pins taken = own when shortOfOwn is true, and taken = stream (with one xref.chain-cycle) when stream 6 is nearer.
REFUTER FIX: Wording only, in two places.

At threat-model.md:432, replace the two clauses with one, as lazy-reading.md does. For example: 'a cross-reference stream, which may be the table's own, told by its own /Prev as its dictionary writes it: where it comes nearest if that /Prev names another offset than the table's, and never if it gives none. Any stream near the offset that gives no /Prev, or the table's, the stream the /XRefStm read among them, may be the table's own and is tried once every other place was, nearest first, ...'.

Make the same fold at diagnostics.md:115: '... a cross-reference stream where it comes nearest if its own /Prev names another offset than the table's, and never if it gives none; any stream near that offset that gives no /Prev, or the table's, the one the /XRefStm read among them, may be the table's own, and is tried once every other place was, nearest first ...'.

Optionally fold the readInStreamsPlace param doc the same way (PdfFileReader.cs:4862-4869). It has the same split and leaves out 'nearest first'.

Do not add the proposed sentence about the shape still ending or looping: the row already reports the /XRefStm's stream's /Prev as a loop. This is not a design choice for the maintainer.

######## [27] lens=docs key=lazy-reading-120-decoded-whole-omits-partial-decode sev=nit -> verdict=narrowed sev=nit maint=False
LOCATION: docs/website/docs/concepts/lazy-reading.md:119-120 ('a cross-reference stream whose data the reader decoded whole holds fewer rows than it declares')
CLAIM: Since round 7, how much the filters decoded decides how a cross-reference stream is read. Data they decode nothing of holds no rows and cannot be read. Data a filter decoded part of before a fault holds the rows of that part, and the stream is read in part. threat-model.md:442 says the second: 'data a filter decoded part of before a fault holds the rows that part does, the stream read in part where it lacks some'. This page lists only data 'decoded whole' among the causes of a section read in part. A reader then takes partly decoded data to be unreadable, the same as data decoded not at all. Judged as asked: it now misleads, and contradicts threat-model.md:442.
EVIDENCE: Probe ZzR8Partial, row p-partial-flate in r8docs/dump-head.txt. Stream 5 declares /Index [3 2]. Its /FlateDecode data is a zlib header, a stored block holding one row (object 3 new), then a block of the reserved type. Head 012e13d: 'D Warning filter.failed @1700: A Flate stream is corrupt at byte 15 of its 19; the 7 bytes decoded before the fault was found were kept.' Then 'D Warning xref.section-unreadable @1581: The cross-reference stream /Prev names at offset 1581 was read only in part: it holds 1 row where its /Index and /Size declare 2; the row it holds was kept, ...'. Object 3 is "new three" from the row read, and object 4 is "new four" through the rebuild.
FIX: Write 'a cross-reference stream whose data the reader decoded, whole or up to a fault in it, no guard stopping it, holds fewer rows than it declares'. Optionally add '— data its filters decode nothing of holds none, above —'.
--- REFUTER: The finding holds as an ambiguity, not as the contradiction it claims. I rebuilt the reproducer myself at 012e13d. On the branch, a stream whose Flate data decodes part of its rows before a fault is read in part. lazy-reading.md:119-120 lists that case only as data the reader 'decoded whole'. The phrase comes from the code: PdfFileReader.cs:6314 sets `decodedWhole = !cut && !leftEncoded`, which means no guard cut the decoding, and Flate data stopped by a fault counts. In plain English, though, 'decoded whole' means decoded to the end, and concepts/diagnostics.md:139 uses it that way ('after its last block decoded whole'). Since R7-Q1 the page draws a line at data decoded nothing of (:113-115, 'Flate data corrupt before its first byte decodes'). So partly decoded data is now placed nowhere plainly on the page. It is excluded from the unreadable case by 'before its first byte', and it is not plainly inside 'decoded whole'. threat-model.md:442 and the code remark at PdfFileReader.cs:6301-6305 both state it. The fix stage noted the same gap and left it (r7-fix-result.json, open points at :222 and :256), and the brief asked for it to be judged as a nit. The finding overstates in two places. The page does not contradict threat-model.md:442: it says nothing contrary, it only leaves the case out. And a reader is not led to take partly decoded data as unreadable, since the unreadable clause's 'before its first byte decodes' already excludes it. The real defect is that the case is not placed. The wording also stood on main 21a0298 (:92).
REFUTER EVIDENCE: My own probe, uncommitted and since deleted. It builds the file the finding describes: an oldest table indexing 3 and 4; an update with stream 5, /Index [3 2] /W [1 4 2], /FlateDecode, whose data is zlib header 78 01, a stored non-final block of 7 bytes holding the row for object 3, then a byte 07 starting a block of the reserved type; and a newest table whose /Prev names the stream. At 012e13d:
- Partial data: S /Prev 1581 Read Stream; 'Warning filter.failed: A Flate stream is corrupt at byte 15 of its 19; the 7 bytes decoded before the fault was found were kept.'; 'Warning xref.section-unreadable: The cross-reference stream /Prev names at offset 1581 was read only in part: it holds 1 row where its /Index and /Size declare 2; the row it holds was kept, ...'; O 3="new three"; O 4="new four" (rebuilt); F Error xref.section-malformed: '... holds 1 row where its /Index and /Size declare 2.'
- Control, the same file with the stored block removed so the fault comes before the first byte: S /Prev 1581 Malformed Stream; 'filter.failed: A Flate stream could not be decoded.'; 'xref.section-unreadable: ... is there but cannot be read: its data could not be decoded. The chain goes on through its /Prev; ...'.
So the reader reads partial data in part and unreadable data not at all. The page names the second case explicitly (:113-115), but names the first only as 'decoded whole' (:120). This matches r8docs/dump-head.txt, row p-partial-flate. On main 21a0298 the same row gives filter.failed with no section report, and object 4 is "old four" (dump-main.txt).
REFUTER FIX: In docs/website/docs/concepts/lazy-reading.md:119-120, replace 'a cross-reference stream whose data the reader decoded whole holds fewer rows than it declares' with 'a cross-reference stream whose data no guard cut — decoded to its end, or as far as a filter decoded it before a fault — holds fewer rows than it declares'. The finding's own wording works too: 'whose data the reader decoded, whole or up to a fault in it, no guard stopping it, holds fewer rows than it declares'. Optional, for consistency, not part of this finding: the same 'decoded whole' gloss recurs in the guard-cut sentences at reference/diagnostics.md:112, reference/reader-limits.md:187, reference/validation-rules.md:54 and threat-model.md:440. There it is set against a guard's cut and reads correctly, but it could take the same 'no guard cut it' wording. This is wording only; no maintainer decision is needed.

######## [28] lens=docs key=commit-bodies-record-092b772-253f179 sev=nit -> verdict=confirmed sev=nit maint=False
LOCATION: commit bodies 092b772 and 253f179 (already pushed, for docs/status.md's 'Commit bodies not rewritten')
CLAIM: Two of the round's eight bodies say more than the diffs do. 092b772: 'Such a stream holds no rows and cannot be read ...: a /Prev it gives names the older sections all the same, the numbers it declares are refused to their rows, and the rebuild serves the objects.' This holds for a stream of /Type /XRef only. A stream of no /Type that gives a /W hands no /Prev on and refuses nothing, and where an older section is near, that section serves the objects. 253f179: 'The threat model's entry for that gap says ... It cites the tests that pin both contexts.' The #171 Known gaps entry (threat-model.md:457-482) cites no test. The two pins are cited in the Held-by column of row 434. The other six bodies are true of their diffs. Every subject is conventional and at most 82 characters, no issue number appears outside a Refs footer, and no review or measurement key appears.
EVIDENCE: 092b772, untyped stream: probe rows q1-guess-far ('S /Prev named=1509 off=1509 Malformed Stream', no third section, message '... could not be decoded. The rows read before the fault, if any, were kept; ...' without 'The chain goes on through its /Prev', repairedAtOpen=True) and q1-guess-near (older table taken, 3="old three"). 253f179: `grep -n 'A_section_in_stream_data_that_reads_short_of_where_a_prev_names\|header_in_a_string_short_of_where_a_prev_names' docs/threat-model.md` returns only line 434, the defense row. Subject lengths are 76, 77, 82, 76, 82, 80, 72 and 81. The bodies were grepped for review keys and for '#n' outside 'Refs': none.
FIX: No rewrite, since the commits are pushed. Add 092b772 ('a /Prev it gives names the older sections ... the numbers it declares are refused' holds only for a stream of /Type /XRef) and 253f179 ('It cites the tests': the citations are in row 434, not the entry) to docs/status.md's 'Commit bodies not rewritten', beside de12b13.
--- REFUTER: Both claims hold at 012e13d, and they are in scope. The brief accepts the behavior for an untyped stream as consequence (b). It also says to report a pushed body that is false, so the status.md record can list it.

092b772: the body says, without qualification, that a stream whose filters decode nothing hands its /Prev on, has its numbers refused, and leaves its objects to the rebuild. The code does that only for /Type /XRef. `DeclaredByLostStream` requires `TypedXRef: true` (PdfFileReader.cs:4049), and `PreviousToHandOn` and `RefuseDeclared` go through it.

The reader still calls the untyped stream a cross-reference stream: its own message says "The cross-reference stream /Prev names at offset 1507 ... could not be decoded". The remark at PdfFileReader.cs:4461-4462 also says one that gives no /Type "stays a cross-reference stream that cannot be read". So the body's "Such a stream" covers it, and the body is false for it.

The pages ed61728 changed qualify the clause ("a stream of /Type /XRef handing its /Prev on, the numbers it declares refused", in lazy-reading.md and reference/diagnostics.md). ed61728's own body drops that qualifier, just as 092b772's does. The finding calls that body true, but it is equally inexact, so I widen the finding by one commit.

253f179: the body's "It cites the tests that pin both contexts" has "the threat model's entry for that gap" as its subject. The #171 Known-gaps bullet (docs/threat-model.md:457-482) contains no test name. 253f179 added the two citations to the Held-by cell of row 434, the defense row. The threat model does cite the tests; only the place named is wrong. That makes it the weaker of the two.

The other checks in the finding hold:
- Subject lengths are 76, 77, 82, 76, 82, 80, 72 and 81.
- Every subject is conventional.

By precedent, bodies true of only one case are recorded: f0d90c8, 8692234, 2b26d18 and de12b13 are in decisions.md. The fix is the record and not a rewrite: the commits are pushed, and the behavior is the maintainer's accepted rule.
REFUTER EVIDENCE: Probe built on 012e13d, run as an uncommitted Zz test, then removed. Shape: an oldest classic table indexes objects 1-3 with 3 = "(old three)". Then come object 3 again as "(new three)" and stream 5 `<< {type}/Size 6 /Index [3 1] /W [1 4 2] /Root 1 0 R /Prev <oldest> /Filter /Foo /Length 7 >>`, its raw row as data. Last, a newest table indexes object 6 with /Prev naming stream 5. "far" puts a 1,198-byte comment between the oldest table and the stream.

- Typed, far: sections are startxref Read, /Prev Malformed at 1507, /Prev Read at 137. The message says "...its data could not be decoded. The chain goes on through its /Prev; ...". Object 3 = "new three" after the rebuild.
- Untyped, far: sections are startxref Read and /Prev Malformed Stream at 1507, with no third section. The message says "The cross-reference stream /Prev names at offset 1507 is there but cannot be read: its data could not be decoded. The rows read before the fault, if any, were kept; the objects only the rest indexes are looked for by rebuilding the index." The /Prev is not handed on; repairedAtOpen=True because the catalog was lost with the chain.
- Untyped, near: /Prev named=307 is Relocated to the table at 137, with only xref.offset-adjusted after filter.unsupported. Object 3 = "old three": the older row stands, nothing is refused and nothing is rebuilt.
- Typed, near: the chain goes on, and object 3 = "new three".

These rows match the finding's q1-guess-far and q1-guess-near.

253f179: `grep -n 'A_section_in_stream_data_that_reads_short_of_where_a_prev_names\|header_in_a_string_short_of_where_a_prev_names' docs/threat-model.md` returns only line 434. The #171 bullet at lines 457-482 holds no backticked test name. `git show 253f179 --word-diff` shows the two names added to the Held-by cell of row 434.

ed61728's body: "the stream cannot be read, its message saying its data could not be decoded, a /Prev it gives is handed on, the numbers it declares are refused, and the rebuild answers for its objects". lazy-reading.md:150-153 instead reads "a cross-reference stream whose dictionary gives `/Type /XRef` ... hands on the `/Prev`".

Subjects measured with `git log --format=%s d3ef236..012e13d` are 76, 77, 82, 76, 82, 80, 72 and 81 characters.
REFUTER FIX: Do not rewrite the pushed commits. Under docs/status.md's "Commit bodies not rewritten" (:814, beside de12b13), add three entries:
- 092b772: "a /Prev it gives names the older sections all the same, the numbers it declares are refused to their rows, and the rebuild serves the objects" holds for a stream of /Type /XRef only. An untyped stream that gives a /W is a guess (#263): it hands nothing on and refuses nothing, and an older section near the named offset serves the objects in its place.
- ed61728: the same clause, "a /Prev it gives is handed on, the numbers it declares are refused", leaves out the /Type /XRef qualifier that its own pages give.
- 253f179: "It cites the tests that pin both contexts". The citations are in the Held-by cell of the defense row (threat-model.md:434), not in the #171 Known-gaps entry.

No maintainer decision is needed.

######## [29] lens=batch key=oldest-stream-deferred-behind-tables-own-stream sev=major -> verdict=narrowed sev=major maint=True
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:4924 and :4938 (TryRelocateXRefSection deferral), :7053 (MayBeTablesOwn); docs/website/docs/reference/validation-rules.md:60
CLAIM: Regression from 3a1ac79 (R7-Q2), on both main 21a0298 and d3ef236. A first revision's cross-reference stream never gives a /Prev, so the rule classes it as 'may be the table's own' and defers it. Take a hybrid update on top of such a stream: the table's /Prev misses that stream by a few bytes, its /XRefStm rightly names the update's own stream, and that own stream gives a /Prev that is another offset (the right one, or a reference). The own stream keeps its nearest-first place, so it is taken even though it is 170 bytes away and the oldest stream is 12 bytes away. The reader then reports branch (c) and the validator gives a false Error xref.hybrid-stream-wrong. The index is marked incomplete, and a later rebuild serves the object the update freed. With a reference /Prev the chain ends at the own stream instead: the index is rebuilt at opening, and the validator adds Error xref.section-not-found. This is not accepted consequence (a): there the table's /Prev falls inside the own stream, which is the nearest place. validation-rules.md:60 says the Error comes only where the /Prev 'comes to that stream before any other section'. Here the /Prev comes first to another section, the oldest stream, which the rule then pushes back.
EVIDENCE: File /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r8bat/genL/lright_m12_p0_n10.pdf (715 bytes, generator genL.py in the same folder). Layout: objects 1-3, 5 '(old five)' and 7 '(seven, which the update frees)'. Stream 8 at 255: '<< /Type /XRef /Size 9 /W [1 4 2] /Root 1 0 R /Length 63 >>', no /Prev, rows 0-8. Object 5 '(new five)'. Stream 9 at 437: '<< /Type /XRef /Size 10 /W [1 4 2] /Index [5 1 9 1] /Prev 255 /Length 14 >>'. Table at 560: frees 7, row for 9, trailer '<< /Size 10 /Root 1 0 R /Prev 267 /XRefStm 437 >>'. startxref 560.
main and d3ef236: 'Repair xref.offset-adjusted at 255: ... /Prev names at offset 267 was found 12 bytes before it.', only 'Warning xref.section-shifted at offset 255', object 5 = (new five), object 7 = null, no repair.
012e13d: 'Repair xref.offset-adjusted at 437: ... was found 170 bytes after it.', 'Warning xref.hybrid-stream-lost at 437: ... the section its /Prev names too ...', 'Repair xref.rebuilt', 'Error xref.hybrid-stream-wrong at offset 560 ...'. ORDERDIFF: object 7 is null ascending but '(seven, which the update frees)' descending.
The same file with stream 9 giving '/Prev 99 0 R' (lref_m12_p0): main and d3e as above, plus file.trailer-value-wrong. Head: 'Warning xref.section-missing at 437: The /Prev ... is not an offset', OPEN repaired=True, object 7 the freed one in both orders, 'Error xref.section-not-found at offset 437', 'Error xref.hybrid-stream-wrong at offset 563'.
Every right and ref variant (miss 12, 3, -5; pad 0 and 300) gives the Error on head and never on main. The none and table modes read right.
The random differential hit it too: gen8.py seed 202611071648, f00994_n18, where r0 is a stream and r1's own stream gives /Prev 99 0 R. Head: 'found 500 bytes after it', hybrid-stream-lost, section-missing, rebuild, freed object 6 served. main and d3e: 'found 12 bytes before it', every object right.
FIX: Maintainer question: the rule cannot tell a first revision's stream from the table's own. Proposed narrowing:
(1) A stream at the offset the /XRefStm's stream names through its own /Prev is the section meant, so it is never deferred. This fixes the 'right' mode, and the PrevFallingShortOfTheStreamBeforeTheTablesOwn rows keep their outcome, since there the oldest table lies out of reach.
(2) A /Prev of the /XRefStm's stream that is no direct integer counts as none, as null already does: the stream is passed over and does not beat a deferred nearer stream. This fixes the 'ref' mode.
Add a PrevFallingShortOfTheStreamBefore row whose oldest section is a /Type /XRef stream with no /Prev, the table's /Prev 12 bytes past it, the own stream giving /Prev = the oldest offset and 99 0 R. Assert: no hybrid-stream-lost, no Error, object 7 null in both orders. If the maintainer keeps the rule, validation-rules.md:60, lazy-reading.md:196 and threat-model.md:430/432 must state this consequence.
--- REFUTER: The finding holds. 3a1ac79 introduced it: 092b772, d3ef236 and main all read the file right. The code does what R7-Q2 decided, word for word. MayBeTablesOwn (PdfFileReader.cs:7053) classes any stream whose dictionary gives no /Prev as 'may be the table's own'. A first revision's cross-reference stream never gives a /Prev, so it is deferred (:4930, :5018). Meanwhile the stream the /XRefStm read keeps its nearest-first place (:4938), because its /Prev names another offset. That stream is then taken although the oldest stream lies nearer. The result is branch (c), a false Error xref.hybrid-stream-wrong, the index marked incomplete, and the freed object served after a miss.

This is not accepted consequence (a). There the table's /Prev falls inside the table's own stream, which is the nearest place. Here the nearest place holds the right section. No R7b test, open point or decision covers a stream shaped like a first revision's near a hybrid /Prev. The decision assumed the stream the /XRefStm read keeps its place only because it is a previous revision's stream (the shape of R7's finding [20]). It did not foresee the table's own stream giving a /Prev.

Narrowing, from my own runs:
- **Needed:** the stream the /XRefStm read gives a /Prev other than the table's. If it gives none, or the table's /Prev, the head reads right (the 'none' and 'table' modes).
- **Wider than claimed:** a wrong direct integer (/Prev 9) regresses as well, and so does an oldest stream whose /Prev is null.
- **The reference case:** it rests on the accepted rule that a reference counts as a /Prev. The rule is accepted, but this consequence (rebuild at opening, freed object 7 served in both orders, two Errors) was not stated with it.
- **Docs:** they understate rather than contradict. lazy-reading.md:196 and the remarks at :4834 state the rule ('any stream near the offset whose own dictionary gives no /Prev ... may be the table's own'), which literally covers a first revision's stream. validation-rules.md:60 says the Error comes where the /Prev 'written a few bytes off, comes to that stream before any other section'. That is true only in search order, after the deferral, and none of them states this consequence.

This is a design choice for the maintainer.
REFUTER EVIDENCE: I rebuilt the file with my own generator (/tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/ref35/gen/mk.py). cmp finds it byte-identical to genL/lright_m12_p0_n10.pdf and genL/lref_m12_p0_n10.pdf. Layout: oldest stream 8 at 255, no /Prev; stream 9 at 437 with /Prev 255; table at 560 with /Prev 267 and /XRefStm 437.

I checked that the main and d3e trees match `git archive` of 21a0298 and d3ef236 (diff -rq clean), and built a worktree at 092b772. One probe ran against every tree. Outputs are in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/ref35/out-{head,main,d3e,092,fix1}.txt.

Head 012e13d, right mode:
- 'Repair xref.offset-adjusted at 437: ... /Prev names at offset 267 was found 170 bytes after it.', 'Warning xref.hybrid-stream-lost at 437', 'Repair xref.rebuilt', 'Information object.redefined'.
- Validator: 'Warning xref.section-shifted at offset 437' and 'Error xref.hybrid-stream-wrong at offset 560'.
- OPEN repaired=False, then repaired=True. ORDERDIFF: object 7 is null ascending but '(seven, which the update frees)' descending.

Main 21a0298: 'found -12 bytes from there', only section-shifted at 255, object 7 null, no repair. d3ef236 and 092b772: 'found 12 bytes before it', the same result.

Head, ref mode (/Prev 99 0 R):
- 'Warning xref.section-missing at 437: The /Prev ... is not an offset', offset-adjusted at 437, hybrid-stream-lost, rebuilt.
- OPEN repaired=True, object 7 the freed one.
- 'Error xref.section-not-found at offset 437' and 'Error xref.hybrid-stream-wrong at offset 563'.
- Main: section-shifted at 255 plus file.trailer-value-wrong, object 7 null.

Variants:
- All 6 right and 6 ref variants (miss 12/3/-5, pad 0/300) give the Error on head and never on main or d3e.
- The extra variants regress the same way: stream 9 /Prev 9 (6 variants), and the oldest stream with /Prev null.
- The 'none' and 'table' modes read right on head. So does an oldest stream with /Prev 5, which is not deferred.
- Random differential genA/f00994_n18.pdf: head 'found 500 bytes after it', section-missing, hybrid-stream-lost, rebuilt, Error section-not-found plus hybrid-stream-wrong. Main and d3e: 'found 12 bytes before it', no repair.

Fix probes, uncommitted and reverted:
- (1) Never defer the stream at the offset named by the /Prev of the stream the /XRefStm read, when that /Prev is a direct integer: CrossReferenceChainTests plus *Hybrid* plus *XRef* gives 555/555 passed. It cures all right variants and the null one. The ref and /Prev 9 variants still give branch (c) and the Error.
- (2) Positional (do not defer a stream that starts before the stream the /XRefStm read): cures every mode but fails A_prev_naming_offset_zero_passes_over_a_stream_near_the_file_s_start_that_may_be_the_table_s_own(true). In that row, HybridFiles.PrevOfZeroNearTheStream puts the 'own' stream 11, which gives no /Prev, at the file's start, before stream 6 and the oldest table. That is exactly a first revision's shape, so the row pins this very behavior.
REFUTER FIX: This is the maintainer's choice, a follow-up to R7-Q2. Options:

(A) The finding's (1), recommended as the minimum. A stream at the offset named by the /Prev of the stream the /XRefStm read, when that /Prev is a direct integer, is the section meant and is never deferred. It is a few lines in TryRelocateXRefSection: compute PositionOf(that /Prev) once, and pass tablePrevious = -1 for that candidate. I measured it green on the 555 chain, hybrid and xref tests. It cures the case where a writer got the stream's /Prev right and the table's wrong. It leaves the reference and wrong-integer variants regressed. For the reference variant, the finding's (2) (count a non-integer /Prev as none) would reverse the accepted 'a reference counts as a /Prev' rule. The wrong-integer variant needs (B) or a stated consequence.

(B) Positional. With append-only updates, a revision's own stream lies past every older revision's section. So a stream that starts before the stream the /XRefStm read is not the table's own, and is not deferred. This cures every variant, but A_prev_naming_offset_zero_..._may_be_the_table_s_own(true) and its builder must be reworked: their 'own' stream lies at the file's start.

(C) Keep the rule and state the consequence: a first revision's stream, which never gives a /Prev, is deferred behind the stream the /XRefStm read when that stream's /Prev names another offset. The /XRefStm is then found wrong and the freed objects are served through the rebuild. State it in validation-rules.md:60 (not only 'a few bytes off ... comes to that stream'), lazy-reading.md:196, the remarks at PdfFileReader.cs:4834-4840 and the comment at :4914-4920, threat-model.md:430/432, and the MayBeTablesOwn summary at :7044.

Whichever is chosen, add a row to the PrevFallingShortOfTheStreamBefore family. Its oldest section is a /Type /XRef stream with no /Prev, the table's /Prev lies 12 bytes past it, and the table's own stream gives /Prev = the oldest offset (plus the 99 0 R and wrong-integer rows if (B) is chosen). Assert:
- 'found 12 bytes before it';
- no hybrid-stream-lost and no Error hybrid-stream-wrong;
- WasRepaired false;
- object 7 null in both orders.

######## [30] lens=batch key=deferral-skipped-when-xrefstm-names-a-stream-again sev=major -> verdict=narrowed sev=major maint=True
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3478 (NameAgain branch: readInStreamsPlace stays null) and :4924 (tablePrevious set only when readInStreamsPlace is a stream)
CLAIM: R7-Q2 says a stream that may be the table's own, one giving no /Prev or the table's, is tried after every other place. It is applied only when this table's /XRefStm is the first naming of the stream. A writer that copies /XRefStm into every later trailer, as #156 and M03 describe, makes the second and later copies namings again. A hybrid table whose /Prev misses then takes its own unnamed stream before the previous table, and round 7's [20] harm comes back: the chain is cut short at the own stream, the index is rebuilt at opening, and the validator gives a false Error file.root-invalid. When the own stream gives the table's /Prev, it gives xref.chain-cycle and Error xref.chain-loop instead. The same middle update reads whole when no newer table copies the /XRefStm. So whether revision 2's /Prev is read right depends on what revision 3 wrote. main and d3ef236 behave like head here (not a regression), but the decided rule is not implemented for this shape.
EVIDENCE: Generator /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r8bat/genK.py. File genK/kagain_none_40_n12.pdf, 4,073 bytes:
- table T0 at 262, objects 1-3, '(old five)' and 7;
- stream 10 at 1657 indexing object 4, no /Prev;
- T1 at 1772, '<< /Size 11 /Root 1 0 R /Prev 262 /XRefStm 1657 >>';
- '(new five)', then stream 11 at 1911, giving object 5 and a free 7, no /Prev, named by nothing;
- T2 at 2651, '<< /Size 12 /Root 1 0 R /Prev 1845 /XRefStm 1657 >>' (1845 is 40 bytes before the end of T1's trailer);
- T3 at 3963, '<< ... /Prev 2651 /XRefStm 1657 >>' (the copy again); startxref 3963.
Head (same on main and d3e): 'Repair xref.offset-adjusted at 1911: The cross-reference section /Prev names at offset 1845 was found 66 bytes after it.', 'Repair xref.rebuilt', OPEN repaired=True, 'Error file.root-invalid at object 1 0: The trailer's /Root names object 1 0, which the file does not hold. No object of the file is a catalog.'
The same file without T3 (kfirst_none_40), head: 'Repair xref.offset-adjusted at 1772: ... found 73 bytes before it', chain read whole, no repair, no Error. main took 1911 there and rebuilt.
kagain_table_40, where stream 11 gives /Prev 1845. Head: 'Warning xref.chain-cycle at 1845', 'Repair xref.rebuilt', 'Error xref.chain-loop at offset 1911'. kfirst_table_40 reads whole.
All six into-offsets (40, 20, 7) behave alike.
FIX: In the NameAgain branch, when again.ReadAs is a cross-reference stream the chain read (Kind Stream, Read or Relocated), arm the deferral for the table's /Prev search. Pass tablePrevious = offset, but with no takeAt, since that stream is not to be read again in the stream's place. Add kagain rows (none and table) to the PrevFallingShortOfTheStreamBeforeTheTablesOwn family: a newer table copies the same /XRefStm, and the rows assert the oldest table read, no repair at opening, and no file.root-invalid or chain-loop. Alternatively, if the maintainer scopes R7-Q2 to a first naming, lazy-reading.md:196 and threat-model.md:430 should say that a naming again, an /XRefStm read as a classic table, or a lost /XRefStm leaves the table's own stream to be taken nearest-first. Probes genKt1 and genKlost show the same chain cut in those two cases.
--- REFUTER: The reproducer holds, byte for byte and message for message. The finding is wrong on two points and understates a third.

What holds. In the NameAgain branch (PdfFileReader.cs:3478), readInStreamsPlace stays null. Line 4924 arms the deferral only when readInStreamsPlace is a stream. So the /Prev of a table whose /XRefStm names a stream a newer table already read gets no deferral. kagain cuts the chain at the unnamed own stream: rebuild at opening, then Error file.root-invalid, or chain-cycle plus Error chain-loop. kfirst reads whole on head. Head, d3ef236 and main give the same output on kagain. main and d3ef236 also cut kfirst, which head now reads whole.

Wrong point 1: "the decided rule is not implemented". R7-Q2's text is framed on "the stream the /XRefStm read", so it presupposes that the table's /XRefStm read a stream. The fix stage was told to keep the deferral's scope where it applied at d3ef236 (lot19-r7-fixes.js, the R7b prompt: "Keep the rule's scope where the deferral applies today"). The R7b report states that scope, and the brief accepts "the deferral applies whenever the table's /XRefStm read a stream". The texts say the same:
- lazy-reading.md:194-197 says "where it read a stream";
- threat-model.md:430 says a naming of what the chain has reached "reads nothing", recorded as named again;
- the TryRelocateXRefSection remarks say "Where that is a cross-reference stream".
No text misstates the scope. Covering namings again is a scope extension, not a misimplementation.

Wrong point 2: the shape does not need #321. In my p_again file every revision names its own stream, and the newest table copies the middle table's /XRefStm only one revision late, which is the writer pattern two corpus documents show (#321 accepts a one-late copy). It cuts the same way, on head, d3ef236 and main alike.

What the finding understates: the same null readInStreamsPlace also withholds takeAt. Where the middle table's /Prev falls short of the named-again stream itself, #188 passes that stream over as reached, and head and d3ef236 cut a chain that main reads whole. That is a regression against main, from earlier in the batch (#188 plus round 2's takeAt, which is limited to the table's own /XRefStm). Two files show it:
- x_again: round 7 [20]'s exact shape plus a newer table copying the /XRefStm;
- q_xref_again: the same without the unnamed own stream.
The finding's proposed fix (deferral, no takeAt) leaves both cut.

q_plain_again is #330's shape with the /Prev 7 bytes off, and the middle table has no /XRefStm. It regresses the same way. Round 2 left the other-table case to #330 on the belief that main loops there too, which holds only at the exact offset. Neither #321 nor #330 nor their comments mention the relocated variant.

The severity is major for those regressions: a needless rebuild at opening, and a false Error, as in [20]. The fix hangs on #330's open question (what a /Prev is when it reaches a stream read only through another table's /XRefStm), so it is the maintainer's to make.
REFUTER EVIDENCE: Probe: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r8ref-kagain/ZzKAgainRefuteTests.cs.txt, run on head 012e13d, on d3ef236 and on main 21a0298 (both from git archive). It was removed from the worktree afterwards. Outputs are out*-head.txt, out*-d3e.txt and out*-main.txt in that same folder.

**The finding's files**, regenerated with genK.py into files/. The offsets match the finding (T0 262, stream 10 at 1657, T1 1772, stream 11 at 1911, T2 2651 with /Prev 1845, T3 3963).

kagain_none_40 on head:
- sections: /XRefStm NamedAgain 1657, then /Prev Relocated Stream 1911;
- 'Repair xref.offset-adjusted at 1911: The cross-reference section /Prev names at offset 1845 was found 66 bytes after it.', then xref.rebuilt;
- repaired=True;
- 'Error file.root-invalid at object 1 0: ... No object of the file is a catalog.'

kagain_table_40 on head:
- 'Warning xref.chain-cycle at 1845', then xref.rebuilt;
- 'Error xref.chain-loop at offset 1911'.

kfirst_none_40 on head:
- 'Repair xref.offset-adjusted at 1772: ... found 73 bytes before it';
- the oldest table read, repaired=False, no Error.

d3ef236 and main: all twelve files relocate to 1911 and rebuild (kfirst too), with root-invalid or chain-loop.

**genP.py, filesP (no two-revisions-late copy).** T0 at 208, then S1 (stream 10, no /Prev) at 1579, T1 at 1694 (/Prev 208 /XRefStm 1579), S2 (stream 11, no /Prev) at 1808, and T2 at 1923 with /Prev 1591 (12 bytes into S1) and /XRefStm 1808. p_again adds T3 at 2033 (/Prev 1923 /XRefStm 1808), a one-revision-late copy.
- p_again_12 on head: '/Prev Relocated Stream at 1579', 'Repair xref.offset-adjusted at 1579: ... names at offset 1591 was found 12 bytes before it.', xref.rebuilt, repaired=True, 'Error file.root-invalid at object 1 0'.
- p_first_12 on head: 'Relocated Table at 1694', 'found 103 bytes after it', repaired=False, no Error.
- d3ef236 and main: both files 1579, rebuilt, root-invalid.
- Offset 30 gives the same.

**x_again: [20] plus a copy.** S1 (stream 10 at 1579) gives /Prev 208. S2 (stream 11 at 1730) gives no /Prev and nothing names it. T2 at 1845 has /Prev 1572 /XRefStm 1579, and T3 at 1955 has /Prev 1845 /XRefStm 1579.
- head and d3ef236: '/Prev Relocated Stream at 1730', 'Repair xref.offset-adjusted at 1730: ... names at offset 1572 was found 158 bytes after it.', xref.rebuilt, repaired=True, 'Error file.root-invalid at object 1 0'.
- main: relocated to 1579 ('found 7 bytes from there'), T0 read, repaired=False, no Error.
- x_first: head reads whole (branch (c), one Error hybrid-stream-wrong), main reads whole, d3ef236 cuts.

**genQ.py, filesQ (no own stream).** q_xref_again: T2 at 1730 has /Prev 1572 /XRefStm 1579, and T3 at 1864 has /XRefStm 1579.
- head and d3ef236: '/Prev NotFound at 1572', 'Warning xref.section-missing at 1572: ... is not there, nor near it', xref.rebuilt, repaired=True, 'Error xref.section-not-found at offset 1572: ... is not there, nor within 512 bytes of it'. The stream is 7 bytes away.
- main: relocated to 1579, T0 read, no repair, no Error.
- q_plain_again (T2 has no /XRefStm): identical on all three trees.
- q_xref_first and q_plain_first read whole on head.

**Scope records:**
- lot19-r7-fixes.js, the R7b prompt: 'Keep the rule's scope where the deferral applies today';
- r7-fix-result.json, R7b measurements: 'only where that table's /XRefStm read a cross-reference stream in the stream's place';
- r2-findings.md:159: 'not covered by #330 ... which is a loop on main too'.
REFUTER FIX: This is a design choice the maintainer must make, tied to #330's open decision and to #321. Three options:

**(A) Full fix, recommended once #330 is decided.**
- In the NameAgain branch: when again.ReadAs is a cross-reference stream that a newer table's /XRefStm read in its stream's place (Kind Stream, Read or Relocated, IsHybridStream, its links never followed), set readInStreamsPlace = again.ReadAs. This table's /Prev then gets both exceptions:
  - takeAt: it takes that stream where its own /Prev names another offset (x_again and q_xref_again read whole, as on main);
  - the deferral: it tries other streams that may be the table's own after every other place (kagain and p_again read whole).
- The newer table's /XRefStm naming then becomes the naming of what an older table's /Prev read. That is #330's option 1 (xref.hybrid-stream-lost, with Error xref.hybrid-stream-wrong at the newer table).
- q_plain_again has no naming at the middle table, so it is settled by the same #330 decision: whether a /Prev's search may take a stream read only through another table's /XRefStm.

**(B) Minimal, the finding's fix.** Arm only the deferral in NameAgain (tablePrevious = offset, no takeAt). This fixes kagain and p_again but not x_again or q_xref_again.

**(C) Defer.**
- File a debt beside #321 and #330 (batch 31) with the six reproducers.
- Comment on #330 that its shape with the /Prev a few bytes off reads whole on main, while the branch reports section-missing, Error section-not-found and a rebuild at opening.
- Add one clause to lazy-reading.md:194-197 and threat-model.md:430: a table whose /XRefStm names again what a newer table read gets neither exception, and its /Prev's search passes that stream over as reached and takes the other places nearest-first.

**Tests, whichever option is taken:** rows for kagain and p_again (the deferral) and for x_again and q_xref_again (the named-again stream taken) in the PrevFallingShortOfTheStreamBeforeTheTablesOwn family. Each row asserts:
- the oldest table read;
- no repair at opening;
- no file.root-invalid, chain-loop or section-not-found.

######## [31] lens=batch key=undecoded-causes-named-in-part sev=nit -> verdict=confirmed sev=nit maint=False
LOCATION: docs/website/docs/concepts/lazy-reading.md:113-114; src/AdCodicem.Pdf/IO/PdfFileReader.cs:7021-7022 (DataUndecoded summary) and :6302-6303 (comment)
CLAIM: These three texts give the data no filter decoded as two cases: data left at a filter the library does not know, and Flate data corrupt before its first byte. The code (PdfFilterPipeline undecoded) also counts two more: a /Filter element that names no filter, such as [/FlateDecode 5] or [null], and a /Filter that is neither a name nor an array, such as /Filter 5. Both give the same Malformed section. The pipeline's XML doc and threat-model.md name all four. R7d's deviation notes the shortening; the page then understates which files lose their rows.
EVIDENCE: gen8.py seed 202611071648, f00463_n19.pdf. Stream 15 is written '<< /Type /XRef /Size 30 /W [1 4 2] /Index [6 1 11 1 15 1] /Filter 5 /Root 1 0 R /Prev 1375 /Length 21 >>' over raw rows. Head: 'Warning filter.unsupported at 1807: The /Filter entry is an integer, neither a name nor an array; the data was left as it is.', 'Warning xref.section-unreadable at 1681: ... is there but cannot be read: its data could not be decoded. The chain goes on through its /Prev; ...', 'Error xref.section-malformed at offset 1681: The cross-reference stream at offset 1681 cannot be read: its data could not be decoded.' The null-elem ([null]) and elem5 ([/FlateDecode 5]) shapes give the same report (genT t1_elem5_*, t4_elem5_*).
FIX: lazy-reading.md:113: '— left at a filter the library does not know, or at an element of /Filter that names none, or by Flate data corrupt before its first byte decodes —' (a /Filter that is neither a name nor an array is the same 'names none' case). Give the DataUndecoded summary and the comment at :6302 the same list, as the remarks at :5973 already do.
--- REFUTER: The finding holds, and the gap reaches one more page than it names. In the code, PdfFilterPipeline.cs:297 sets `undecoded = !data.IsEmpty && chain.End is FilterChainEnd.Unsupported or FilterChainEnd.NotAName or FilterChainEnd.NotAFilterEntry`, and :273 sets it when Flate fails, the only step that raises `failed` (:432). That makes four causes. TryReadXRefStream (PdfFileReader.cs:6308) turns every one into Malformed with DataUndecoded. The pipeline's `undecoded` param doc, the remarks at PdfFileReader.cs:5973, diagnostics.md:135 and threat-model.md:442/:616 all name an element of /Filter that names no filter, and diagnostics.md:135 and the param doc also name a /Filter that is neither a name nor an array, apart from 'a filter the library does not know'. So by the project's own wording, 'a filter the library does not know' does not cover those cases. Five texts list only the shorter set: lazy-reading.md:113-114 and validation-rules.md:54 (both read 'left at a filter the library does not know, or by Flate data corrupt before its first byte decodes'), the DataUndecoded summary at PdfFileReader.cs:7021-7022, the comment at :6302-6303, and the test comment at FilterTests.cs:1250. The finding names three of them; it misses validation-rules.md:54 and the test comment. R7d recorded the shortened page wording as a deviation. decisions.md ('Review round 7 fixes: result') does not record it as accepted, and the round 8 brief does not list it among the accepted consequences. No issue holds it. It changes no behavior: the pages only understate which files lose a cross-reference stream's rows. So it is a nit.
REFUTER EVIDENCE: I rebuilt the reproducer on 012e13d with a Release build and the uncommitted probe ZzR8UndecCausesTests, which I have removed. It edits the /Filter entry of stream 15 in r8bat/genA/f00463_n19.pdf in place, keeping its length. Run 4 times: the 2 shapes below gave this output; the [/FlateDecode 5] and /Foo rows did not run, because the probe's same-length edit check failed on them.
/Filter 5: section 1681 Malformed fault='its data could not be decoded' handedOn=True (the sections at 1375, 587 and 378 Read); 'Warning filter.unsupported at 1807: The /Filter entry is an integer, neither a name nor an array; the data was left as it is.'; 'Warning xref.section-unreadable at 1681: The cross-reference stream /Prev names at offset 1681 is there but cannot be read: its data could not be decoded. The chain goes on through its /Prev; ...'; validator 'Error xref.section-malformed: The cross-reference stream at offset 1681 cannot be read: its data could not be decoded.'
/Filter [null]: the same sections and reports, apart from 'Warning filter.unsupported at 1807: Element 0 of the /Filter entry is null, which names no filter; decoding stopped there.'
An element that names no filter inside an array is shown by the earlier probe lot19/r8ref-undecnamed-out.txt on head: [/ASCIIHexDecode 42] gives 'Element 1 of the /Filter entry is an integer, which names no filter; decoding stopped there.', section Malformed 'its data could not be decoded', and xref.rebuilt.
The two-case texts, found with grep 'left at a filter the library does not' and 'does not know, or by one' on 012e13d: lazy-reading.md:113, validation-rules.md:54, PdfFileReader.cs:6302 and :7021, FilterTests.cs:1250. Four causes are named at PdfFilterPipeline.cs:90-97 (param doc) and threat-model.md:442.
REFUTER FIX: Name the same causes in all five places, the way threat-model.md:442 does.
- lazy-reading.md:113-114 and validation-rules.md:54: '— left at a filter the library does not know, at an element of /Filter that names none or at a /Filter that is neither a name nor an array, or by Flate data corrupt before its first byte decodes —'.
- DataUndecoded summary (PdfFileReader.cs:7021-7022) and the comment at :6302-6303: 'left at a filter the library does not know, or at an element of /Filter that names none, or by one that could decode none of what it was given'.
- Optionally the same in the comment at FilterTests.cs:1250, whose rows already cover both shapes.
This changes text only, with no maintainer decision needed.

######## [32] lens=batch key=issue-329-now-judged-for-undecodable-data sev=nit -> verdict=refuted sev=none maint=False
LOCATION: GitHub #329 (title and opening paragraph); tracking only
CLAIM: #329's title says a cross-reference stream whose value a guard cut 'never has its /Prev judged'. Since 092b772 that no longer holds when the stream's data cannot be decoded. FaultBesideCut makes the stream Malformed as the file's fault, which matches the whole read, and its /Prev is handed on and judged under the guard. The issue's own reproduction, with decodable data, is unchanged and still true. Without a note, whoever pays #329 may not see that the undecodable sub-case already reads as a whole read does.
EVIDENCE: genT t3_*: object 9 is '[1 4 2]' plus 3,000 spaces; stream 20 gives '/W 9 0 R /Index [4 1 20 1]' and '/Prev 99999999'. Under MaxObjectLength 1024:
- /Filter /FooDecode (t3_foo_out), head: 'Warning xref.section-unreadable at 3629: ... its data could not be decoded. The chain goes on through its /Prev; ...', 'Warning xref.section-missing at 3629: ... 99999999 lies outside the file ...', 'Warning limit.object at 573 ...', 'Error xref.section-not-found at offset 3629'. d3ef236 gave 'Information xref.checked-in-part' only, with object 4 null.
- Flate data (t3_flate_out), head and d3ef236: 'Warning limit.object at 573' and 'Information xref.checked-in-part at offset 3629' only, as #329 describes.
FIX: At the batch's end, comment on #329: a stream whose data its filters decode nothing of is now judged, its /Prev handed on as in the whole read. The issue's shapes (decodable data, CutByValue and FaultBesideCut with rows) remain open.
--- REFUTER: The behavior change is real, but it does not make #329 untrue. #329's title is shorthand, and its body sets the scope: two paths, CutByValue and a fault beside the cut whose data may hold rows. The body also already says a stream whose fault beside the cut leaves no rows has its /Prev read.

Before 092b772, the title was already false as a general statement. A width fault beside a cut /W element, or data of 0 bytes, had its /Prev judged under the guard on d3ef236, exactly as it does at the head. 092b772 moves one sub-case: undecodable data where the cut value is /W, an element of /W, or an element of /Index. That sub-case leaves the CutByValue path and joins the faults that make the stream unreadable whatever the value holds, which #329 never claimed were unjudged.

#329's own shapes have no filter, and they read the same at the head and on d3ef236. Its two paths still return before /Prev is read, and its 'What closes it' still describes open work. Nothing in it misstates the code.

The proposed comment would itself overstate. When the cut value is the whole /Index, /Size or /Filter, an undecodable stream is still CutByValue at the head, and its /Prev is still unjudged. These shapes stay inside #329's CutByValue scope, as they should. FaultBesideCut's summary documents this: the data is "judged where the /Index or /Size it numbers them with was read", and only for direct filters and a known extent. For the same reason, a cut /Length or /DecodeParms also stays CutByValue, going by the code.
REFUTER EVIDENCE: Probe /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r8ref329-ZzR8Issue329Tests.cs.txt builds #329's own file. Objects 1 to 3, object 5 the cut value plus 3,000 spaces, stream 7 /Type /XRef with /Prev, and a classic table indexing 5 with /Prev to stream 7. It was run at the head 012e13d and at d3ef236; outputs are in r8ref329-head.txt and r8ref329-d3e.txt in the same folder.

- **A1 (/W 5 0 R, /Filter /FooDecode, /Prev 99999999), under MaxObjectLength=1024.**
  - Head: section Malformed cut=False 'its data could not be decoded', then 'Warning xref.section-unreadable ... The chain goes on through its /Prev', 'Warning xref.section-missing ... 99999999 lies outside the file', 'Repair xref.rebuilt', 'Warning limit.object at 182', and the finding 'Error xref.section-not-found at offset 3205'. This is the whole read plus limit.object.
  - d3ef236: section Malformed cut=True, and only 'Information xref.checked-in-part'.
  - The /Prev SELF, (x) and 0 variants, and the element cuts C4 (/Index [5 0 R 8]) and C5 (/W [1 5 0 R 2]) with /FooDecode, behave the same way. The reproduction holds.
- **B1 (Flate, decodable) and B2 (no filter, #329's shape):** identical at the head and on d3ef236. Under the guard they give 'Malformed cut=True its /W is the reference 5 0 R ...' and only 'Information xref.checked-in-part'.
- **Pre-existing, identical on d3ef236 and at the head, guarded:**
  - D1 (/W [-1 5 0 R 2]) gives 'Malformed cut=False its /W gives a field a negative width', 'xref.section-missing ... 99999999', and the finding 'Error xref.section-not-found'.
  - D2 (/W [1 5 0 R 2], data of 0 bytes) gives 'Read ... its data, 0 bytes decoded, holds at most 0 of the 8 rows', 'xref.section-missing', and 'Error xref.section-not-found'.
  - So the title's 'never' already failed for faults that leave no rows before 092b772, as #329's body says ('Data that can hold no row has its /Prev read').
- **Undecodable data still unjudged under the guard at the head:**
  - C1 (/Index 5 0 R): 'Malformed cut=True its /Index is the reference 5 0 R, which one of the reader's limits cut', and only 'Information xref.checked-in-part'.
  - C2 (/Size 5 0 R, no /Index) and C3 (/Filter 5 0 R pointing to /FooDecode) give the same.
  - Read whole, each of the three gives 'its data could not be decoded', 'xref.section-missing' and 'Error xref.section-not-found'.
REFUTER FIX: Nothing to fix, because #329 still states the code. If the maintainer wants a note on #329 anyway at the batch's end, it must be narrower than the proposed one. It could read: since 092b772, a stream whose data its direct filters decode nothing of, beside a cut /W or a cut element of /W or /Index, is Malformed as the file's fault and its /Prev is handed on, as a whole read does. With the whole /Index, /Size, /Filter, /DecodeParms or /Length cut, it is still CutByValue and stays in this issue. The issue's shapes, and both paths it names, are unchanged.
