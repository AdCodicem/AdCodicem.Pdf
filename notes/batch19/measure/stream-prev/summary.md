# stream-prev (#208): a cross-reference stream whose rows cannot be read but whose dictionary names /Prev

Report: NOT WRITTEN. The harness refused the Write of /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/measure/stream-prev/report.md ("Subagents should return findings as text, not write report files"), so the full report is in these structured fields. Every piece of evidence is in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/measure/stream-prev/ (below: OUT/).
- Shapes: OUT/shapes-main-1.txt, -2, OUT/shapes-main3-mx.txt, OUT/shapes-main4-mx.txt (main); OUT/shapes-a2c1-mx.txt, -a2c2 (clean A2); OUT/shapes-b1-mx.txt, -b2 (clean B); OUT/shapes-r1-m{1,3,5,9,13,17,21}.txt and OUT/shapes-r3-*.txt (switch variants).
- Side-by-side tables: OUT/compare-main-a2-b.md, OUT/compare-variants.md, OUT/table-*.txt.
- Diffs: OUT/proto-a2-hand-prev-on.diff, OUT/proto-b-hand-prev-on-refuse-declared.diff, OUT/proto-measurement-switches.diff.
- Probes: OUT/ZzStreamPrev.cs.txt, OUT/ZzCorpusDump.cs.txt.
- Suites: OUT/suite-m1.log, OUT/suite-m5.log, OUT/suite-mb.log.
- Corpus: OUT/corpus-main.txt, OUT/corpus-m*.txt, OUT/corpus-diff-*.txt.
- Prototype B is left applied, uncommitted, in worktree /home/user/AdCodicem.Pdf/.claude/worktrees/wf_9ceb535e-b92-4. Its bin holds the clean A2 build: rebuild before running.

## Reproduced

### #208 — matches today: False
Shape: I0 — the issue's literal shape, three sections. Objects 1, 2, 3 '(only in the oldest)' and 7 '(old seven)', then the oldest table at 173 (rows 0-3 and 7, /Size 8 /Root 1 0 R). 1,200 bytes of comment. Objects 4 '(only in the middle)' and 7 '(new seven)'. At 1583, object 5, a cross-reference stream /Type /XRef /Size 8 /Index [4 2 7 1] /W [1 9 2] /Root 1 0 R /Prev 173, with 12-byte rows for 4, 5 and 7. 1,200 bytes of comment. Object 6, then the newest table (rows 0 and 6, /Prev 1583). Objects read each in a fresh document.

Paid as written. Reproduced twice, outputs identical. All three sections read; 'GetObject(3): "only in the oldest"; WasRepaired: False'; 4, 7 ('new seven') and 6 read with no rebuild; no diagnostic, no finding. WidthFault (PdfFileReader.cs:4859-4864) faults only a width that is not an integer or is negative. A width past 8 is narrowed once the data is decoded (:4138-4153, :4229-4233). The issue's 'width outside 0 to 8 bytes' no longer exists. Variant I0b, /W [1 9 2] over 7-byte rows: the section is Read and Incomplete ('it holds 1 row where its /Index and /Size declare 3') and its /Prev is followed. Object 4 comes back through a rebuild (its row gives offset 1671257674285056). Object 7 reads 'old seven' with NO rebuild, and the reader reports nothing. Validator: xref.section-malformed and xref.entry-broken.

### #208 — matches today: True
Shape: W1: the same file with the stream's /W [1 -4 2] (a negative width). Also W2 /W [1 4], W3 /W [1 4 /x], W4 /W [0 0 0], W5 no /W, W6 /W 99 0 R (no section places 99).

The defect is alive. Exact output for W1, reproduced twice:
- 'opened: WasRepaired True'
- sections: 2961 Read; 1583 Malformed with trailer=set, trailerPrev=173, fault='its /W gives a field a negative width'
- reader: 'Warning xref.section-unreadable at 1583: The cross-reference stream /Prev names at offset 1583 is there but cannot be read: its /W gives a field a negative width. The rows read before the fault, if any, were kept; the objects only the rest indexes are looked for by rebuilding the index.', then 'Repair xref.rebuilt', then 'Information object.redefined' (object 7)
- objects: 3, 4, 7 ('new seven') and 6 all read with WasRepaired True
- validator: 'Error xref.section-malformed at offset 1583: ... cannot be read: its /W gives a field a negative width.'
The oldest table is never read: the catalog lives only there, so the rebuild runs at opening. W2-W6 behave the same, each with its own fault text.

### #208 — matches today: True
Shape: X1-X8, faults of the numbering: /Index [4] (odd), [-1 2 7 1], /X (not an array), [4 /x 7 1], [4 -1 7 1], [] (empty); no /Index and no /Size; no /Index and /Size -1.

In every one: the section is Malformed (return at :4176), the dictionary was read whole and kept (trailerPrev=173), and the chain stops. Reader: xref.section-unreadable, xref.rebuilt. Every object comes back through a rebuild. Validator: xref.section-malformed.

### #208 — matches today: True
Shape: D8: a deferred /Length 9 0 R that no section places, and 'endstream' misspelled, so no endstream is found before the next object (#220's fault).

Malformed (return at :4080), dictionary kept, the chain stops. Fault text: 'its /Length is the reference 9 0 R, which no section read before it places where it can be read, and no endstream follows its data before the next object, at 2927'. Reader: xref.section-unreadable, xref.rebuilt, stream.truncated.

### #208 — matches today: True
Shape: D7: object 5 is a dictionary (/Type /XRef /W [1 4 2] ... /Prev 173) followed by no stream data.

Malformed (return at :4034), fault 'its dictionary is not followed by stream data'. The dictionary was read whole and is in hand when the fault returns, but it is not kept: section.Trailer is null. The chain stops; reader xref.section-unreadable and xref.rebuilt. With MaxTrailerLength 256 cutting the dictionary after /Type /XRef (G1b), the section is CutByLimit and the chain ends for the guard: limit.trailer, objects 3, 4 and 7 read as null, validator xref.checked-in-part.

### #208 — matches today: False
Shape: Data faults: D1 /Filter /FlateDecode over data that is not deflated; D2 /Filter /Foo; D3 /Length 5 (too short); D4 /Length 999 (too long); D5 /Index [4 5 7 1] over data for 3 rows; D6 Flate data cut to 6 bytes.

None of them stops the chain: the section is Read and its /Prev is followed (:4408).
- D1: filter.failed, the data kept raw, every row read.
- D2: filter.unsupported, every row read.
- D3, D4: stream.length-invalid, the data taken up to endstream, every row read.
- D5: Read and Incomplete (:4359-4371); object 7 reads 'old seven' with no rebuild; the reader reports nothing; validator xref.section-malformed.
- D6: filter.failed, 0 rows held, Incomplete; object 4 through a rebuild; object 7 'old seven' with no rebuild.
Wherever the stream holds fewer rows than it declares, the older rows already stand for the missing ones.

### #208 — matches today: True
Shape: P1-P10, dictionary faults beside /W [1 -4 2]: /Prev /X, /Prev 99999999 (outside the file), /Prev -5, /Prev 8 0 R (8 nowhere), /Type /Foo, no /Type, a stray ')' in the dictionary (a syntax fault), /Prev 173.0, /Prev 1583 (the stream itself); P8 has no /Prev at all. Controls with sound rows: P1b (/Prev /X) and P9b (/Prev 173.0).

Every one is Malformed with its dictionary kept, and the chain stops before the /Prev is read. P7 shows syntax.unexpected-token; its validator gives only 'file.trailer-malformed', with no xref.section-malformed, because SectionMalformedRule.cs:40 counts a section unreadable only when TrailerFault == None. The controls show what a whole read reports for a bad /Prev: P1b gives xref.section-missing and the Error xref.section-not-found; P9b gives file.trailer-value-wrong ('gives its /Prev as a real number').

### #208 (guards, ADR 34) — matches today: True
Shape: G1: MaxTrailerLength 256 cuts the stream's dictionary before /Type. G2: MaxDecodedStreamLength 10 cuts Flate rows. G3: MaxObjectLength 1024 cuts object 9, the stream's /W 9 0 R, which the newest table places. G3c: the same cut beside /Index [4 -1 7 1]. G4: /Prev 9 0 R with 9 cut by MaxObjectLength, beside /W [1 -4 2]. G4b: G4 under the default limits.

G1, G1b and G3 are Malformed and CutByLimit; G2 is Read and CutByLimit with previous -1 (rowsCut). In all four the chain ends for the guard: limit.* is reported, nothing is rebuilt, objects 3, 4 and 7 read as null, and the validator gives xref.checked-in-part. G3c is the file's own fault (FaultBesideCut, return at :4100): Malformed without CutByLimit, the chain stops, and the index is rebuilt. G4 and G4b: Malformed, the chain stops, rebuild.

### #208 (hybrid) — matches today: True
Shape: H1, H2: the newest is a hybrid table whose /XRefStm names the unreadable stream (/W [1 -4 2]). The stream's own /Prev is 99999999 in H1 and 173 in H2; the table's /Prev is 173.

The stream's /Prev is not followed: no record for 99999999. The chain goes on through the table's /Prev. Object 4 comes back through a rebuild. Object 7 reads 'old seven' with NO rebuild. Probe Q2 reads 7, 4, 7 in one document and gets 'old seven', then 'new seven' once the rebuild set off by object 4 has run. The answer depends on the order of reads, on main today.

### #281 / #188 (relocation) — matches today: True
Shape: R1: the newest table 100 bytes after the unreadable stream. R2: the oldest table 212 bytes before it. R3: both near.

R1 is #281's shape. The stream is relocated onto the newest table, already read: 'xref.offset-adjusted ... found 278 bytes from there', then xref.chain-cycle and xref.rebuilt. Validator: file.size-wrong twice, xref.section-shifted, xref.chain-loop (Error).
R2 and R3 are relocated onto the OLDER table, not yet read ('found -212 bytes from there'), which is then read in the stream's place. GetObject(4) returns null with WasRepaired False: the stream's rows are lost silently, and the only report is a Repair. Validator: xref.section-shifted only.

### #208 (first section) — matches today: True
Shape: S1: startxref names the unreadable stream (/W [1 -4 2], /Prev 173). There is no newer section.

return false (:3010-3013): the whole file is rebuilt at opening. The reader reports only xref.rebuilt; xref.section-unreadable never appears. Validator: xref.section-malformed. Objects: 3 and 4 found, 7 'new seven', all through the rebuild.

### #208 (classic table) — matches today: True
Shape: A classic table in the middle, its /Prev naming the oldest. T1: 'xref 4 4' with a bad row for 5. T2: a bad subsection header '7 x'. T3: a stray '/Bad 1' after the last row. T4: the rows run into '<<' with no 'trailer' keyword.

T1-T3: Malformed with Trailer null. The trailer comes after the rows and is never reached, so the chain stops and the index is rebuilt. T4: Malformed with TrailerFault.Missing; validator file.trailer-missing. The rows read before the fault are kept (object 4).

## Prototypes

### A2: hand /Prev on, no relocation (the issue as written)
Diff: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/measure/stream-prev/proto-a2-hand-prev-on.diff

In TryReadXRefChain (:3005-3021), for a section that is not the first, a stream hands its /Prev on when:
- it reads Malformed and no guard cut it (!CutByLimit);
- its Kind is Stream and NamedBy is not "/XRefStm";
- its kept dictionary (section.Trailer) has a /Prev key.
Then ReportLostSection runs (xref.section-unreadable, once, the index incomplete) and previous = SectionOffset(dictionary, /Prev, section.Offset). The chain goes on, under visited, the section count guard and SectionOffset's own checks. No relocation is tried for such a stream. 24 lines in PdfFileReader.cs.

Shapes after: Clean build, shapes run twice, outputs identical.
- W1-W6, X1-X8, D8, P5, P6, P7, N1, G3c, G4b, K1, K2: the oldest table is read and xref.section-unreadable is reported once. Object 3 reads with no rebuild and object 4 through a rebuild. Object 7 reads 'old seven' with NO rebuild: the older revision, silently.
- P1, P3, P4: xref.section-unreadable, then xref.section-missing ('The /Prev of the cross-reference section at offset 1583 is not an offset'); validator xref.section-malformed and xref.section-not-found.
- P2: xref.section-missing for 99999999.
- P10: xref.chain-cycle '...names offset 1583, which the chain has already named.'; validator xref.chain-loop.
- P9 (/Prev 173.0): followed, but the real is not judged by the validator.
- G4: the chain ends for the guard; xref.checked-in-part names the /Prev reference the guard cut.
- R1: the false loop is gone (xref.section-unreadable, the oldest read).
- R2, R3: the silent loss is gone (object 4 found by a rebuild).
- Q1 (W1, reading 7, 4, 7 in one document): 'old seven' with no rebuild, then 4 through a rebuild, then 'new seven'.
- Unchanged: D1-D7, G1, G1b, G2, G3, H1, H2, S1, T1-T4, P8, I0, I0b.

Unit suite: Run on the switch build in mode 1, which is the same logic but reports section-missing before section-unreadable: total 9,282, failed 0, succeeded 9,279, skipped 3, 6 m 27 s (OUT/suite-m1.log). The total includes the two uncommitted probes.

Corpus: None. Clean A2 (OUT/corpus-diff-ma2.txt) and mode 1 (OUT/corpus-diff-m1.txt) each give: docs 403/403, same keys; F diff 0, F order 0, D diff 0, D order 0; F 2,982 and D 1,343 on both sides; 168 committed and 235 remote documents. No corpus document has an unreadable section past the first.

### A1: relocation first, /Prev handed on only when no relocation is found
Diff: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/measure/stream-prev/proto-measurement-switches.diff

Switch mode 3 (ZZ_208=3). TryRelocateXRefSection is tried first; the /Prev is handed on only if it fails.

Shapes after: Same as A2 on every shape except R1, R2 and R3, which keep main's results: R1's false loop (offset-adjusted, chain-cycle, chain-loop) and R2/R3's relocation onto the older table, with object 4 reading null and no rebuild.

Unit suite: Not run: it differs from A2 only where relocation finds a candidate.

Corpus: Not run.

### B: A2, plus refusing the stream's declared numbers to the older rows (recommended)
Diff: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/measure/stream-prev/proto-b-hand-prev-on-refuse-declared.diff

A2, plus five changes:
- RefuseDeclared(dictionary) runs before SectionOffset. It takes the stream's /Index subsections, or [0, /Size) when there is no /Index, through the existing NumberingFault. When the numbering cannot be read (a fault, or /Index or /Size written as a reference), every number is refused (_olderRowsRefused).
- The ranges are merged and sorted into one array (_refusedRanges) that spans the chain. Past MaxRefusedRanges = 4,096, every number is refused. This is an internal constant, not a guard: only a file whose sections cannot be read reaches it.
- AddFromChain (:5321) returns before indexing a number in those ranges. It is a binary search, and only runs while a refusal exists. This mirrors _refusedNumbers (:545-551): 'the sections the chain reads after it describe older revisions, whose rows must not stand for the current one'.
- XRefSectionRecord.PreviousHandedOn is set. TrailerValueWrongRule judges the /Prev of such a section, and only its /Prev.
- ReportLostSection runs before SectionOffset.
PdfFileReader.cs +165, XRefSectionRecord.cs +6, TrailerValueWrongRule.cs +8.

Shapes after: Clean build, shapes run twice, outputs identical.
- W1-W6, D8, P5, P6, P7, G4b, K1 (4,002 ranges): object 3 reads with no rebuild, 4 through a rebuild, 7 'new seven' through a rebuild. W1's exact result: opened WasRepaired False; sections 2961 Read > 1583 Malformed > 173 Read; one 'Warning xref.section-unreadable at 1583'; GetObject(3) 'only in the oldest' WasRepaired False; GetObject(4) 'only in the middle' True; GetObject(7) 'new seven' True; validator 'Error xref.section-malformed at offset 1583'.
- X1-X8, N1 (no /Index, so [0, 8)), G3c, K2 (5,002 ranges): every number is refused. Objects read as on main (the catalog through a rebuild at opening), but the oldest section is read and judged.
- P9: 'Warning file.trailer-value-wrong ... gives its /Prev as a real number', plus xref.section-malformed.
- G4: the chain ends for the guard; xref.checked-in-part and file.trailer-value-wrong ('writes its /Prev as the reference 9 0 R').
- P1-P4, P10, R1-R3: as in A2, with the objects as in B.
- Q1: 'new seven' (rebuild), 4 (rebuild), 'new seven': the answer no longer depends on the order of reads.
- Unchanged: H1, H2, S1, T1-T4, D1-D7, G1-G3, I0, I0b.

Unit suite: Clean B build: total 9,282, failed 0, succeeded 9,279, skipped 3, 6 m 29 s (OUT/suite-mb.log). Switch mode 5, the same logic: the same counts, 6 m 57 s (OUT/suite-m5.log). No existing test pins the chain stopping at an unreadable stream that has a /Prev. Not run: a timing row for 1,024 chained unreadable streams, each with many subsections (MaxRefusedRanges, MaxTrailerLength); it belongs to the implementation.

Corpus: None. Clean B (OUT/corpus-diff-mb.txt) and mode 5 (OUT/corpus-diff-m5.txt) each give: F diff 0, F order 0, D diff 0, D order 0; F 2,982 and D 1,343 on both sides; 403 documents including the 235 remote.

### C: a classic table read on to its trailer past a fault, its /Prev handed on
Diff: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/measure/stream-prev/proto-measurement-switches.diff

Switch bit 8: mode 9 is A2 plus tables, mode 13 is B plus tables. At the fault returns of TryReadClassicTable (:3517-3534 non-dictionary token, :3544-3558 subsection header), outside relocation candidates, ReadTrailerPastRows (:3641) runs from the start of the faulty line. It finds and merges the trailer, and the table's /Prev is then handed on as in A2.

Shapes after: Mode 9:
- T1 (bad row for 5) and T2 (bad header): the trailer is found and the oldest read; objects 3 and 4 read with no rebuild, but 7 reads 'old seven' (STALE). The rows after the fault are unread, and the subsection headers among them unparsed.
- T3 (stray name after the last row): every object correct, no rebuild.
- T4: unchanged (no trailer keyword).
Mode 13: the table's trailer has no /Index, so its /Size 8 refuses every number. Objects read as on main; the older sections are read and judged.

Unit suite: Not run.

Corpus: Mode 13: none (OUT/corpus-diff-m13.txt). The five remote/ipres2017/t03-* files with table faults are first sections and are rebuilt whatever the variant.

### First section handed on too (switch bit 16)
Diff: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/measure/stream-prev/proto-measurement-switches.diff

The sections == 1 return (:3010-3013) is skipped when the first section hands its /Prev on. Mode 17 is A2 plus this; mode 21 is B plus this. The stream's dictionary is not merged as the document's trailer (not prototyped).

Shapes after: S1, mode 17: the reader reports xref.section-unreadable; object 3 with no rebuild; 7 'old seven' (stale). S1, mode 21: object 3 with no rebuild; 7 'new seven' through a rebuild. In both, /Root, /Encrypt and /ID would come from the older trailer, because the newest trailer (the stream's dictionary) is not merged.

Unit suite: Not run.

Corpus: Mode 21: none (OUT/corpus-diff-m21.txt).

## Questions

### 208-older-rows (#208)
When the chain goes on past a cross-reference stream whose rows it cannot read but whose dictionary names /Prev, which of the older sections' rows does the index take?

- [R] B: hand /Prev on, and refuse to the older rows the numbers the stream declares (recommended): The refused numbers are the stream's /Index subsections, or 0 to /Size - 1. Every number is refused when the numbering cannot be read, and past 4,096 merged ranges.
- Objects that only the older sections index read with no rebuild, wherever the stream's /Index can be read (W1: object 3).
- What the stream redefines goes to the rebuild and reads its last definition, whatever was read before (object 7 'new seven'; Q1).
- With no /Index, or a numbering fault, objects read as today, but the older sections are read, their trailers merged, and the validator judges them.
- Codes and severities (ADR 45) unchanged: Warning xref.section-unreadable once, Error xref.section-malformed. file.trailer-value-wrong now judges the handed /Prev.
- Cost: one range array and a binary search per older row while one exists; MaxRefusedRanges is an internal constant (ADR 34).
- Tests: CrossReferenceChainTests (W1's 3/4/7, N1, X1, K1/K2, Q1's order), CrossReferenceRuleTests (section-malformed, the older table judged, P9's real /Prev), a ReaderTimingTests/HostileInputTests row (1,024 chained unreadable streams).
- Docs: diagnostics.md, lazy-reading.md, validation-rules.md, a new threat-model row.
- A2: hand /Prev on only, as the issue words it: 24 lines. Object 3 reads with no rebuild. But an object the unreadable stream redefines (object 7) reads its older revision with no rebuild and no report. In one document the same number gives 'old seven', then 'new seven' once a rebuild set off by another object has run (Q1). Main already has this flaw past an unreadable /XRefStm (Q2) and past rows short (D5, D6, Q3).
- Close #208 with no change: Every object of the older sections keeps costing a rebuild (W1: one at opening, since the catalog is there). The R2/R3 relocation loss and R1's false loop stay with #281/#188.

Why: B gives the issue's gain exactly where it is safe, and sends to the rebuild what the older revision would answer wrongly. The reader already refuses rows this way (_refusedNumbers, PdfFileReader.cs:545-551, Refuse :5345). A2 trades a rebuild for silent stale objects that also depend on the order of reads, which runs against 'never a silent break' (invariant 7) and against determinism as a caller sees it. Measured: every unit test passes and the corpus does not change for either.

### 208-relocation (#208, #281, #188)
Is a cross-reference stream that is there, cannot be read, and hands its /Prev on still looked for nearby (relocated) first?

- [R] No: it is there, its /Prev is followed, and nothing is relocated (recommended): R1 (#281's shape with a /W fault): no xref.offset-adjusted, chain-cycle, section-shifted, chain-loop or doubled file.size-wrong. Instead, xref.section-unreadable and xref.section-malformed, and the oldest table is read.
R2/R3: no relocation onto the older, unread table, which on main reads in the stream's place and loses object 4 silently (null, no rebuild, only a Repair). Object 4 is found by a rebuild.
Composes with the relocation lens's 'no relocation when malformed' for #281: one decision. The threat-model row at docs/threat-model.md:416 gains a clause.
- Relocation first; hand /Prev on only when nothing is found (A1): R1 keeps #281's false loop; R2/R3 keep the silent loss of the stream's rows. Everything else as the first option.

Why: Measured on R1-R3: relocation of a stream that is demonstrably there only ever lands on another section, read or not, and either reports a false loop or silently drops the stream's rows. The relocation lens's #281 question is the same choice, and one answer settles both.

### 208-first-section (#208)
When startxref names an unreadable cross-reference stream whose dictionary names /Prev (the first section), does the chain go on too?

- [R] Unchanged: rebuild the whole file at opening, and file a debt for the reader's missing xref.section-unreadable (recommended): S1 as today. Objects are correct through the rebuild. The reader reports only xref.rebuilt; the validator gives xref.section-malformed. No code in this batch.
- Hand on with B's refusal (mode 21): S1: object 3 with no rebuild, object 7 'new seven' through a rebuild, and xref.section-unreadable reported. But the stream's dictionary is the document's newest trailer and would have to be merged first, which was not prototyped. Without that merge, /Root, /Encrypt and /ID come from an older revision. Needs tests for the trailer merge and the catalog.

Why: The issue concerns a section the chain stops at. The first section's dictionary is the document's own trailer, which a hand-on would have to adopt while its rows are lost, and a full rebuild already gives the newest revision correctly. Measured: mode 21 is correct for objects, but the trailer merge is unmeasured.

### 208-tables (#208 (neighbor: classic tables))
A classic table in the chain with a bad row, a bad subsection header or a stray token: should the reader read on to its trailer (as ReadTrailerPastRows already does for guard cuts) and follow its /Prev?

- [R] Not in this batch: file a debt with the measurement (recommended): T1-T3 as today: the chain stops and the index is rebuilt. The debt records variant C's results.
- Read on to the trailer, hand /Prev on, and refuse every number to the older rows (mode 13): Objects read as today. The older trailers are merged and the older sections judged by the validator. Cost: one ReadTrailerPastRows scan per malformed table, plus tests for T1-T4.
- Read on and hand on without refusal (mode 9): T3 (fault after the last row) becomes entirely correct with no rebuild. T1/T2 read object 7's older revision silently: the rows after the fault are unread, and their numbers cannot be known because the subsection headers among them are not parsed.

Why: The only refusal that is safe for a table refuses every number and gains nothing on objects. The variant that gains is unsafe for any fault before the last row. Only a fault proven to follow every row (as in T3) is worth handing on, and telling that apart needs a parse of the rest that is not prototyped.

## decided_without_question

- The issue's literal shape (/W [1 9 2]) is paid on main 21a0298: WidthFault (PdfFileReader.cs:4859-4864) no longer bounds a width, and shape I0 reads all three sections. Tests for #208 should use a fault that still faults: /W [1 -4 2] (as CrossReferenceChainTests already does), or /W [1 4].
- Which faults hand /Prev on (measured on every return of TryReadXRefStream). Hand on: every Malformed return after section.Trailer = dictionary (:4037) that no guard cut, namely :4080 (#220's deferred /Length with no endstream), :4091 (UnreadValue), :4100 (a file fault beside a guard cut), :4135 (/W missing or short), :4150 (a width not an integer or negative), :4160 (all widths 0), :4176 (/Index and /Size faults), and :4071's file-fault branch. Do not hand on: :4008 and :4071/:4127 CutByValue (guard's, ADR 34); :4014 (not a section); :4034, a dictionary not followed by stream data (D7). D7's dictionary is in hand but not kept (Trailer null), it is no stream's dictionary, and a CIDFont's /W dictionary lands there. Data faults (filter failure D1/D2, a wrong direct /Length D3/D4, rows short D5/D6) already read Read and follow /Prev at :4408.
- A fault in the dictionary itself does not stop the hand-on. SectionOffset judges the /Prev as a whole read does: /Prev /X, -5 and 8 0 R give xref.section-missing and xref.section-not-found (P1, P3, P4, as P1b shows for a readable stream); outside the file gives xref.section-missing (P2); naming the stream itself gives xref.chain-cycle and xref.chain-loop (P10). /Type /Foo with /W (P5), no /Type (P6) and a dictionary with syntax faults (P7) hand on, as a whole read takes each for the section.
- xref.section-unreadable is reported before the /Prev's own report: ReportLostSection runs before SectionOffset. The switch measurement (mode 1) showed the reverse order, and the clean A2 and B fix it.
- A guard cut stays the guard's: G1, G1b, G2 and G3 are identical in every variant. A handed /Prev that leads to an object MaxObjectLength cut ends the chain for the guard through SectionOffset (:5196-5205); xref.checked-in-part names it (G4).
- An /XRefStm stream's /Prev is never followed (NamedBy == "/XRefStm"): H1's /Prev 99999999 creates no record in any variant.
- visited and the section count guard apply unchanged, because the handed /Prev re-enters the loop head: P10 loops through visited, and the unreadable section counts toward MaxXRefSectionCount.
- file.trailer-value-wrong judges the handed /Prev, and only it among the stream's values: it is the one value the reader read (P9's /Prev 173.0, G4's 9 0 R). Done in prototype B through XRefSectionRecord.PreviousHandedOn.
- The unreadable stream's dictionary is not merged into the document's trailer, per the issue: 'its /Prev (and only that)'.
- Validator: SectionMalformedRule still finds the stream; SizeWrongRule.IsWhole stays false for a chain with a malformed section; the older sections are now judged (CrossReferenceRuleTests, as the issue asks).

## interactions

- relocation (#188, #281): a stream that hands /Prev on is not relocated. This settles R1 (#281's false loop) and R2/R3, a variant the relocation lens may lack: relocation lands on the OLDER, not-yet-read section and silently drops the stream's rows (object 4 null, no rebuild, only a Repair). It composes with relocation's proto-a-b2-no-relocation-when-malformed: #208's check runs first, and a malformed section with no /Prev, or a table, falls to the relocation path. 208-relocation and #281's question are one decision.
- partial (#210, #207, #222): rows short (D5, D6, I0b, Q3) leave the stream Read and Incomplete with its /Prev followed, and the older rows answer for the missing ones ('old seven', never rebuilt, nothing reported by the reader). B's refusal of the declared-but-not-held numbers would pay the stale answer; #210 pays the silence.
- guards (#264): SectionOffset is now also called on an unreadable stream's /Prev. The batch-16 guard logic (_chainValuesCut, TrailersHiddenByGuard, ChainHiddenByGuard) applies as for a whole read (G4: xref.checked-in-part). #264's F/G shapes (sections a guard stops) are not touched.
- xrefstm (#156): the hybrid path is unchanged. An unreadable /XRefStm stream already lets the /Prev chain's rows stand for its numbers, with order-dependent answers (H2, Q2). The same refusal could be applied there.
- window (#197): moot for streams that hand /Prev on, which are no longer relocated; still matters for malformed sections with no /Prev and for tables.
- messages (#241): no new plural or count messages; the xref.section-unreadable text might add that the chain goes on through the /Prev (wording only).
- reserved (#221): no interaction.

## new_debts_or_neighbors

- Rows short let older rows stand silently: a stream decoded whole that holds fewer rows than it declares is Read and Incomplete, its /Prev is followed, and an older section's row answers for a number whose row is missing (D5, D6, I0b, Q3: 'old seven' with no rebuild, nothing in the reader's diagnostics). The stale answer is not in #210, which covers only the silence. Remedy: B's refusal applied to the declared-but-not-held numbers.
- An unreadable /XRefStm stream lets the /Prev chain's rows stand for its numbers (H2: 'old seven' with no rebuild). In one document the same number reads two objects, depending on whether a rebuild ran before (Q2 on main). Same remedy.
- SectionMalformedRule (Validation/Rules/SectionMalformedRule.cs:40) counts a section unreadable only when TrailerFault == None. A stream whose dictionary has a syntax fault and whose /W is negative (P7) gets only file.trailer-malformed, never xref.section-malformed, though the reader reports xref.section-unreadable.
- First section: when startxref names an unreadable stream, the reader reports only xref.rebuilt, never xref.section-unreadable (S1).
- Relocation onto an older, not-yet-read section (R2, R3, main): the stream's rows are lost silently, object 4 reads null with no rebuild, and the only report is xref.offset-adjusted (a Repair). Hand to the relocation lens / #281.
- Classic tables with a bad row or header in the middle of the chain (T1-T3) stop the chain. Variant C finds the trailer, but the numbers of the rows after the fault cannot be known (see question 208-tables).
- Corpus: no document has an unreadable cross-reference section past the first. A real incremental update with a damaged middle cross-reference stream is wanted (docs/corpus-contributions.md); #208's acceptance is otherwise synthetic only (invariant 10).
- For implementation (prototype B): a timing/hostile-input test for a chain of 1,024 unreadable streams, each with up to MaxTrailerLength/4 subsections, to measure RefuseDeclared's sort-and-merge under MaxRefusedRanges. Not measured here.

## docs_to_update

- docs/website/docs/reference/diagnostics.md:106, xref.section-unreadable: a cross-reference stream whose dictionary was read and gives a /Prev hands it on, and the chain goes on to the older sections. Under B, the older rows for the numbers the stream declares are refused and found by a rebuild. The 'the chain stops' reading of the row changes.
- docs/website/docs/concepts/lazy-reading.md:80-92: the chain goes on past an unreadable stream through its /Prev; what it redefines is looked for by rebuilding.
- docs/website/docs/reference/validation-rules.md:48, file.trailer-value-wrong: the /Prev of an unreadable cross-reference stream that the chain went on through is judged; the stream's other values are not.
- docs/threat-model.md, Indexing: a new row for a chain going on past unreadable streams (bounded by visited and MaxXRefSectionCount; refused ranges bounded by the internal MaxRefusedRanges, with a timing test named). docs/threat-model.md:416 (missing section tried at many places): a stream that is there and hands /Prev on is not relocated.
- PdfFileReader.cs TryReadXRefChain remarks (:2927-2934: 'the chain stops at a missing /Prev') and ReportLostSection's summary (:3166-3174); the TryReadXRefStream remarks where they say what an unreadable stream leaves.
- docs/status.md at the batch's end (with #208's outcome), and the issue's 'What closes it' if B's refusal is adopted: object 7, which both the stream and the older table give, read through a rebuild.
