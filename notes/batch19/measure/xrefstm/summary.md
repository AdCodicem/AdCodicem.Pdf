# xrefstm (#156: an /XRefStm naming a section already read, file.size-wrong, an /XRefStm naming anything but a stream)

Report: NOT WRITTEN: the harness refused report .md files ("Subagents should return findings as text"); the full report is the final message of this agent, and every evidence file it cites is in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/measure/xrefstm/ (repro-*.txt, suite-*.log, corpus-*.txt, hybrid-corpus-*.txt, many-*.txt, shared-records-p2.txt, *.diff, Zz*.cs.txt, files/*.pdf)

## Reproduced

### #156 — matches today: True
Shape: A1: T0 (1-3,/Size 4); T1 (4,5,/Size 7,/Prev T0,/XRefStm S1); S1 object 5 /Index [6 1]; T2 (7,/Size 8,/Prev T1,/XRefStm S1)

Records T2, S1(named by T2), T1, T0. Warning file.size-wrong at offset 562: The trailer of the cross-reference table at offset 489 gives /Size 7, where the highest object number it and the sections it updates use, 5, makes it 6. (twice identical, OUT/repro-main-run1.txt and run2)

### #156 — matches today: True
Shape: A2: the same with T1 /Size 6 (wrong)

0 findings. Controls A3/A4 (T2 names no /XRefStm): /Size 7 none, /Size 6 'makes it 7' - right.

### #156 — matches today: True
Shape: B1: a table whose /XRefStm names the table itself (stream 7, named by nothing, alone indexes the page)

No record, no diagnostic, GetObject(3)=null, WasRepaired=False; only Warning page-tree.kid-invalid (object 3 which the file lacks).

### #156 — matches today: True
Shape: B2: the original table's /XRefStm names the newer table

Same as B1: silent, object 3 null, no rebuild, only page-tree.kid-invalid.

### #156 — matches today: True
Shape: B3: an update's /XRefStm names a classic table no /Prev names (ChainFiles.HybridNamingATable shape)

Table read in the stream's place (record /XRefStm kind=Table state=Read), objects 9 and 10 read, 0 diagnostics, 0 findings (held by GuardCutValueTests.cs:3190 with Diagnostics empty).

### #156 (new neighbor) — matches today: False
Shape: B4: the update's /XRefStm names the older table its own /Prev names

Not silent but mis-blamed: Warning xref.chain-cycle at 203 (the /Prev of the section at offset 368 names offset 203, which the chain has already read); Error xref.chain-loop at offset 368 on a right /Prev.

### #156 (new neighbor) — matches today: True
Shape: C1: an older table's /XRefStm names the stream section at the chain's head

Silent, 0 findings (an /XRefStm naming a section already read).

### #156 (new neighbor) — matches today: False
Shape: C2: the update's /XRefStm names the older stream section its own /Prev names

Stream section read as /XRefStm stand-in, its /Prev (T00) never followed; xref.chain-cycle, Repair xref.rebuilt at opening (WasRepaired=True); Error xref.chain-loop at offset 539.

### #156 — matches today: True
Shape: xref.chain-loop documented as 'the chain names a section it has already read' vs ChainLoopRule reading FileStructure.LoopOffset

ChainLoopRule.cs:28 reads LoopOffset, set only at PdfFileReader.cs:2957 on the /Prev path; B1/B2/C1/A1 give no chain-loop. Also diagnostics.md:107 and PdfDiagnosticCodes.XRefChainCycle (:46-50) claim an /XRefStm can raise xref.chain-cycle: it never does (LoopNamedBy is always /Prev).

### #156 (new neighbor) — matches today: False
Shape: A5: T3 copies T1's /XRefStm two revisions late; T2 rewrote object 6 between

GetObject(6) = "old six" (S1's stale row wins over T2's), silent; plus the false size-wrong on T1. No corpus file has such a late copy.

### #188 overlap — matches today: True
Shape: E1/E2: /XRefStm names object 4 << /Type /Foo >> 32 / 1,500 bytes before the table

E1: Repair xref.offset-adjusted (found 32 bytes from there) onto the naming table itself, GetObject(3)=null, xref.section-shifted + page-tree.kid-invalid. E2: xref.section-missing, rebuild finds object 3, Error xref.section-not-found.

## Prototypes

### P1a: every /XRefStm naming recorded (NamedAgain record), shared stream counted per naming table
Diff: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/measure/xrefstm/p1a-count-per-naming.diff

XRefSectionState.NamedAgain and XRefSectionRecord.ReadAs; TryReadXRefChain: if visited.Contains(hybrid) NameAgain(hybrid, section) else as main; NameAgain adds a record after the naming table (NamingTrailer = its trailer, ReadAs = first record by a linear scan, HighestNumber = first's only if it is a hybrid stream). PageTreePageOrphanedRule.IsIndexSound accepts NamedAgain; every other Sections consumer checked and unaffected. No diagnostic, nothing read again.

Shapes after: A1: + NamedAgain record (from=489, highest 6); 0 findings. A2: Warning file.size-wrong ... table at offset 489 gives /Size 6, where ... 6, makes it 7. A5: false finding gone (object 6 still 'old six'). B1/B2/C1: + NamedAgain record only.

Unit suite: total 9284, failed 0, succeeded 9281, skipped 3 (4 probe tests included) - OUT/suite-p1a.log

Corpus: none: 403 documents, findings 2982=2982, diagnostics 1343=1343 (OUT/corpus-p1a.txt vs corpus-main.txt)

### P1b: P1a + a shared stream's own /Size judged at its oldest naming
Diff: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/measure/xrefstm/p1b-judge-at-oldest.diff

SizeWrongRule maps each hybrid-stream record to the index of its last (oldest-table) naming and judges the stream only there, with NamingTableSize of that naming record; the oldest table that names a stream is the revision that wrote it.

Shapes after: Same as P1a (OUT/repro-p1b.txt).

Unit suite: total 9285, failed 1, succeeded 9281, skipped 3 (5 probe tests). Failure: CorpusValidationTests.Every_document_produces_exactly_its_declared_findings(pdfmaker7-powerpoint-va-cancer-database-course) - 'declares file.size-wrong and the validator reported nothing' (manifest edit needed). OUT/suite-p1b.log

Corpus: 1 document: vendor/opf-format-corpus/pdfmaker7-powerpoint-va-cancer-database-course.pdf loses 'Warning file.size-wrong at offset 1269: The cross-reference stream at offset 1269 gives /Size 1261, where ... 1275, makes it 1276.' (false: judged after the copying table 179581 instead of its owner 116). F 2982 -> 2980 (two rounds), D unchanged. jhove-hul-122's stream finding (Word's trait) stays.

### P2: P1b + /XRefStm naming no stream reported (xref.hybrid-stream-lost / xref.hybrid-stream-wrong Error), index marked incomplete, branch (c) for /XRefStm == /Prev
Diff: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/measure/xrefstm/p2-full-with-prev-branch.diff

NameAgain via Dictionary<long, XRefSectionRecord> firstAt (written offsets). Reported with diagnostic xref.hybrid-stream-lost (Warning), _indexIncomplete = true, record.NamesNoStream: (a) named again where the first record is Read/Relocated and not a hybrid stream (own table, a table already read, a section read through startxref or /Prev); (b) a classic table read where named or relocated, rows kept; (c) hybrid == previous: /XRefStm not followed, /Prev reads the section whole, NamedAgain record with ReadAs set when the /Prev record is made. New HybridStreamWrongRule xref.hybrid-stream-wrong, Error, at the naming table, HasRoomFor. Docs rows and schema enums added. Variants: OUT/p2-core-no-prev-branch.diff (no (c)), OUT/p2-core-not-incomplete.diff (no incomplete mark).

Shapes after: B1: D Warning xref.hybrid-stream-lost at 303 '... names offset 303, the table itself, ...'; object 3 found by rebuild; F Error xref.hybrid-stream-wrong at offset 303; kid-invalid gone. B2: same ('the cross-reference table at offset 492, which the chain has already read'). B3: rows read, F Error at 443. B4: no cycle, one F Error at 368 ('the section its /Prev names'). C1: F Error at 368. C2: no cycle, WasRepaired=False, whole chain read, F Error at 539. E1: reported as 'a cross-reference table, whose rows were read in the stream's place' (it is the naming table, #188), object 3 found. A1-A5, E2 as P1b. Without (c): B4 gets chain-loop + hybrid-stream-wrong, C2 keeps main's loop and rebuild. Without the incomplete mark: B1/B2/E1 keep object 3 null and page-tree.kid-invalid.

Unit suite: P2 full: total 9286, failed 10, succeeded 9273, skipped 3: ValidatorTests.The_structural_profile_names_the_rules_it_runs_in_order (new id); GuardCutValueTests.A_table_an_xrefstm_names_in_a_stream_s_place_has_its_own_links_left_unread x4 (Diagnostics not empty: xref.hybrid-stream-lost); CorpusValidationTests pdfmaker7 (P1b); HeaderOffsetTests.A_chain_that_names_the_same_offset_outside_the_file_twice_loops x2 (expected LoopOffset, found -1); CrossReferenceRuleTests.A_loop_through_an_offset_the_chain_only_named_says_no_section_was_read_there and CrossReferenceChainTests.Places_a_loop_back_to_a_section_named_outside_the_file_at_what_named_it (no xref.chain-cycle) - the last four from branch (c). P2 without (c): total 9286, failed 6 (profile, GuardCutValue x4, pdfmaker7). Many-namings: 1,000 tables naming one 700 KB stream read 50,368,016 bytes on main/P1b/P2 alike, one stream record Read, 999 NamedAgain; open best-of-7 main 14.66 ms, P2 16.74 ms; 1,000 self-naming tables main 10.51, P1b 41.83 (linear scan), P2 11.46 ms.

Corpus: Same as P1b: only pdfmaker7's false file.size-wrong removed; no diagnostic added or removed on any of 403 documents (no corpus file names a non-stream). Hybrid scan: 13 committed + 14 remote hybrid files; 2 shared streams (pdfmaker7 committed, jhove-hul-122 remote), both now recorded.

## Questions

### Q156-1 (#156)
When a hybrid table's /XRefStm names anything but a cross-reference stream of its revision (its own table, a table the chain read, a table read in the stream's place, a section read through /Prev or startxref), does the reader mark the index incomplete?

- [R] Yes, as for a /Prev loop and a missing section (Recommended): An object the index lacks is looked for by a rebuild when asked: B1/B2's lost page is found (WasRepaired=True), page-tree.kid-invalid goes. The validator's settling rebuilds such a file before its first rule; no corpus file is affected. Hostile file of 1,000 self-naming tables: validation re-reads ~49.7 MB more through the rebuild's per-trailer windows (#49, existing). Message ends '...the objects only the table's stream indexes are looked for by rebuilding the index'.
- No, report only: B1/B2/E1 keep object 3 as null with no rebuild; page-tree.kid-invalid (Warning) beside the new Error. No validation-time rebuild. Inconsistent with the chain's remarks (a lost section leaves the index incomplete) and with xref.chain-loop's handling.

Why: The stream the table meant is unknown exactly as a section after a /Prev loop is; the reader already marks that case incomplete (PdfFileReader.cs:2954) and rebuilds only on demand, so the cost falls on non-conforming files alone (none in the corpus), and B1/B2 recover their page.

### Q156-2 (#156)
Which rule reports an /XRefStm naming anything but a cross-reference stream, and with which reader code?

- [R] A new rule xref.hybrid-stream-wrong (Error) with a new reader code xref.hybrid-stream-lost (Recommended): One finding per naming at the naming table; covers own table, newer table, table never read, /Prev or startxref section. New constants in PdfValidationRuleIds/PdfDiagnosticCodes, schema enums (CorpusManifestSchemaTests), rows in validation-rules.md and diagnostics.md; rule and code must differ (ValidationRuleIdTests). xref.chain-loop and xref.chain-cycle docs narrowed to /Prev. Names are placeholders.
- Widen xref.chain-loop (Error), reuse xref.chain-cycle: Fits 'names a section already read' (own/newer table, /Prev or startxref section) but ChainLoopRule must iterate records instead of the single LoopOffset, and chain-cycle's text ('loops back on itself', ends the chain) would describe a chain that goes on. A table never read (B3) is no loop: it needs its own rule or stays unreported.
- Fold into xref.section-not-found (Error) / xref.section-missing: Record state NotFound with a fault; but SectionNotFoundRule says 'is not there, nor within 512 bytes', false for a table read in place, and B3's rows would have to go unread for consistency (Q156-3's second option).

Why: Only a new rule covers the four shapes the issue names with one wording and one severity; ADR 45 makes it an Error whatever the reader does (the table's own stream is lost, or the reader chose a table in its place), and ADR 36 keeps one severity per rule.

### Q156-3 (#156)
A classic table an /XRefStm names and no /Prev reaches (B3): keep reading its rows in the stream's place (batch 16, #263) and report it, or treat it as no stream?

- [R] Keep reading its rows, report it, mark incomplete (Recommended): Objects it indexes read without a rebuild (B3: objects 9, 10), new Error finding; GuardCutValueTests.A_table_an_xrefstm_names_in_a_stream_s_place_has_its_own_links_left_unread keeps its subject, its Diagnostics.BeEmpty line becomes the new diagnostic (4 cases).
- Treat it as no stream: rows unread, missing section, rebuild on demand: As qpdf does (reconstruct); reverses batch 16's settled reading; B3's object 10 found only by a rebuild; GuardCutValueTests rewritten; xref.section-not-found / section-missing wording must change.

Why: Batch 16 settled that a table in the stream's place is read with its links left unread; reporting it and marking the index incomplete makes the fault visible without reversing that choice, and anything the table lacks is still found by a rebuild.

### Q156-4 (#156)
An /XRefStm naming the very offset its own table's /Prev names (B4, C2, and the one-table shape of three existing loop tests): stop following the /XRefStm there, read the section through /Prev and report the /XRefStm (branch (c)), or keep main's reading?

- [R] Branch (c): /Prev reads it whole, the /XRefStm is reported (Recommended): B4: one Error (xref.hybrid-stream-wrong) instead of a false xref.chain-loop on a right /Prev; C2: no false loop, no rebuild, the older chain (T00) read. HeaderOffsetTests.A_chain_that_names_the_same_offset_outside_the_file_twice_loops (x2), CrossReferenceRuleTests.A_loop_through_an_offset_the_chain_only_named_says_no_section_was_read_there and CrossReferenceChainTests.Places_a_loop_back_to_a_section_named_outside_the_file_at_what_named_it move to a two-table shape (newer /XRefStm, older /Prev naming the same X), which still loops through an offset only named. No corpus file affected.
- Keep main's reading (P2 without (c)): B4 gets two Errors for one fault (chain-loop + hybrid-stream-wrong); C2 keeps a false loop and loses T00's chain to a rebuild (to be filed as debt); the four loop tests stay as they are.

Why: The /Prev is right and the /XRefStm is wrong; following the /Prev keeps the whole chain read and blames the right key, and the tests' purpose (#187/#125: placing a loop through an offset only named) survives in the two-table shape.

### Q156-5 (#156)
Should the validator also report a copied /XRefStm itself (one stream named by two or more tables), which on a copy two or more revisions late lets the stale stream's rows win over the revisions between (A5: object 6 reads 'old six')?

- [R] Not in this batch: file a debt issue with A5's reproduction (Recommended): #156 closes on recording and counting; pdfmaker7 and jhove-hul-122 keep their findings as P1b leaves them; a later rule (Warning under ADR 45: read as the file's own trailers claim, as hybrid-aware readers read it) decided with the M03 writer work that must not copy /XRefStm.
- A new Warning rule now: Both corpus files (pdfmaker7 committed, jhove-hul-122 remote) gain a finding and a manifest edit; one more rule, docs row and schema enum in this batch.

Why: The issue asks for the naming to be recorded and counted, not judged; the corpus has two copies, neither late enough to misread, and the batch is already large.

## decided_without_question

- Every /XRefStm naming is a record of its own with a new XRefSectionState.NamedAgain and XRefSectionRecord.ReadAs (not a link on the table's record): checked every consumer of FileStructure.Sections - only PageTreePageOrphanedRule.IsIndexSound needs the new state added; TrailerMalformed, OffsetImprecise, SectionShifted, SectionMalformed, TrailerMissing, SectionNotFound, StartXRefWrong, RootInvalid, CheckedInPart, TrailerValueWrong (+IsFollowed) and the reader's IsStreamSectionAt, ReadSectionNamedAt, TrailerLocation, MergeChainIndex ignore it; P1a suite 0 failures, corpus diff none.
- A shared stream's rows count for each table naming it (the issue's text): A1 /Size 7 no finding, A2 /Size 6 'makes it 7'; 1,000-table hostile file loses 999 false findings.
- A shared stream's own /Size is judged at its oldest naming (P1b): pdfmaker7's only finding, 'stream at offset 1269 gives /Size 1261 ... 1275, makes it 1276', is false (owner table 116 + 153674 give highest 1260); its manifest drops file.size-wrong; jhove-hul-122's stream finding is Word's trait (same on word-invoice-fr, word2019-ccs-contract-schedule, word2019-diia) and stays.
- The stream is read once: bytes read identical on main, P1b and P2 (50,368,016 for 1,000 tables naming one 700 KB stream; one stream record Read, 999 NamedAgain).
- NameAgain finds the first record through a Dictionary keyed by written offset, as visited is: the linear scan cost 31 ms on 1,000 self-naming tables (41.83 vs 10.51 ms), the dictionary 11.46 ms.
- A shared hybrid stream raises no reader diagnostic (nothing lost, nothing read twice); a naming again of a section not found or malformed is not reported twice.
- Severity Error under ADR 45 whatever Q156-1 decides: the stream the table meant is lost, or the reader chose a table in its place.
- The xref.chain-cycle documentation (diagnostics.md:107, PdfDiagnosticCodes.XRefChainCycle) and xref.chain-loop's (validation-rules.md:57, PdfValidationRuleIds.XRefChainLoop) are corrected to a /Prev in any case: an /XRefStm never raises the cycle (LoopNamedBy is always /Prev).
- The rule id and the reader code must be two names (ValidationRuleIdTests.A_rule_identifier_is_never_a_reader_diagnostic_code) and both enter manifest.schema.json (CorpusManifestSchemaTests).

## interactions

- #188 (relocation lens), second shape E1: relocation lands on the hybrid table itself; P2's (b) reports it as 'a cross-reference table, whose rows were read in the stream's place' (wrong wording) and recovers object 3 by rebuild; once #188 keeps relocation off read sections, E1 becomes E2 (section-missing / section-not-found) and (b) sees only a table at the named offset or a relocation onto an unread table. Suggest the /XRefStm relocation accept stream candidates only (FindSectionCandidates also offers xref keywords).
- #188's batch 9 comment proposes positions instead of written offsets for what the chain read: firstAt/NameAgain must key the same way as visited, or a stream named through two written offsets relocating to one position is read and judged twice.
- Branch (c) changes the one-table 'loop through an offset only named' tests (HeaderOffsetTests.cs:89 x2, CrossReferenceRuleTests.cs:478, CrossReferenceChainTests.cs:140), the same loop reporting #281 (relocation lens) touches; the path stays reachable with two tables.
- Partial lens (#207/#210/#222): both add record fields and conditions in XRefSectionRecord, SizeWrongRule.IsWhole and PageTreePageOrphanedRule.IsIndexSound - merge points.
- Guards lens (#264): P2's (b) excludes a stream record cut by a guard; the chain already ends after a cut /XRefStm (_cutByGuard != cut), so no second naming follows a cut.
- Messages lens (#241): the new messages carry no counts or distances; nothing to share.
- GuardCutValueTests.cs:3190 (batch 16's table-in-the-stream's-place test) changes its Diagnostics.BeEmpty line under P2 (4 cases).

## new_debts_or_neighbors

- A copied /XRefStm two or more revisions late lets the stale stream's rows win over the revisions between, silently (A5: GetObject(6) = 'old six' where T2 wrote 'new six'); nothing reports a stream named by two tables. No corpus file has a late copy (pdfmaker7 and jhove-hul-122 copy one revision late). File as debt if Q156-5 is answered 'not in this batch'.
- If Q156-4 is answered 'keep main': an /XRefStm naming the section its own /Prev names reads it as a stand-in, leaves its /Prev unread and reports a false /Prev loop; a stream section's older chain (C2's T00) is lost to a rebuild.
- The relocation of an /XRefStm accepts a classic table candidate (FindSectionCandidates adds xref keywords), which can only be 'a table in the stream's place' - for the relocation lens / #188.
- diagnostics.md:107 and PdfDiagnosticCodes.XRefChainCycle claim an /XRefStm can raise xref.chain-cycle; it never does - fix in this batch's docs.
- Existing debts made visible by the hostile measurement, no new one: 1,000 small tables read 50 MB through 64 KB windows (#47); a validation rebuild re-reads ~50 MB through the trailer windows (#49).

## docs_to_update

- docs/website/docs/reference/validation-rules.md: :47 file.size-wrong (a stream several tables name counts for each and is judged where the oldest names it); :57 xref.chain-loop (a /Prev, or as Q156-2 decides); new row for the new rule
- docs/website/docs/reference/diagnostics.md: :107 xref.chain-cycle (a /Prev names an offset the chain already named, whether a section was read there through /Prev or /XRefStm, or not); new row for the new reader code
- src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs: XRefChainCycle XML doc (:46-50) and the new code
- src/AdCodicem.Pdf/Validation/PdfValidationRuleIds.cs: XRefChainLoop (:100-103) and the new rule id
- src/AdCodicem.Pdf/IO/XRef/FileStructure.cs (Sections: each /XRefStm after the table that named it, a stream named again too), XRefSectionRecord.cs, XRefSectionState.cs; PdfFileReader.TryReadXRefChain remarks; SizeWrongRule remarks
- docs/threat-model.md: Known gaps entry :447 ([#156]) becomes a defense in *Indexing* (the loop row :411 or a new row), naming the new FileRuleTests/CrossReferenceRuleTests/HostileInputTests tests
- docs/website/docs/concepts/lazy-reading.md (~:83): an /XRefStm that names no cross-reference stream leaves the index incomplete
- tests/corpus/manifest.json: pdfmaker7-powerpoint-va-cancer-database-course expect.findings drops file.size-wrong (P1b)
- tests/corpus/manifest.schema.json: the new reader code and rule id
- docs/milestones/M04.md:71 (a hybrid table and its stream are one section): a stream several tables name belongs to the oldest one's revision
- docs/status.md: batch 19's state
