# critic

Report: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/measure/critic/ — critic.md was NOT written: subagents are told not to write report files, and the harness refused that to every lens. These structured fields are the full report. Evidence in that folder:
- composed-src.diff: the seven src prototypes composed on main 21a0298, conflicts resolved by hand. They are relocation (a)+b2t+c9b; window P197+P197e+P261-B; stream-prev B with its hand-on narrowed to /Type /XRef; partial A-prime; xrefstm P2; reserved (a); guards P1 with its tests. d/ holds the diffs as applied.
- experiment-R-refuse-lost-rows.diff: the critic's experiment, on top of the composed tree.
- Suite logs: suite-composed.log, composed-failures.txt and union-expected.txt (the lenses' own failures); suite-composedR.log and composedR-failures.txt; unpad-composedR.log and unpadR-failures.txt.
- corpus-composed.txt.
- Each lens's probe run on the composed build: reloc-, partial-, streamprev-, streamprev-...R, partial-...R, xrefstm-, guards-, reserved- and window-composed.*.
- The critic's own probe: ZzCritic.cs.txt, with its output in critic-composed-1.txt.
- Scripts: probe.sh, suite.sh, unpad.sh.
Worktree /home/user/AdCodicem.Pdf/.claude/worktrees/wf_9ceb535e-b92-10 holds the composed tree, staged, with R unstaged on top. The Zz probes are untracked and tests/corpus/remote is symlinked. Nothing is committed.

## Reproduced

### #208 — what closes it: the handed /Prev 'is followed under ... the section count guard' (no lens measured it) — matches today: False
Shape: W1: three sections (oldest table, middle cross-reference stream with /W [1 -4 2] and /Prev naming the oldest, newest table). Read with MaxXRefSectionCount 2, and with the default limits. Composed build: on main the chain stops at the stream, so the guard is never met.

Composed build, default limits: the oldest table is read, with one 'Warning xref.section-unreadable at 1583'. GetObject(3) reads 'only in the oldest' with no rebuild.

With MaxXRefSectionCount 2, the handed /Prev re-enters the loop head and the guard ends the chain:
- 'Warning limit.xref-section-count at 173: The cross-reference chain has more than 2 sections; the older ones were not read. Raise PdfReaderLimits.MaxXRefSectionCount to read past it.'
- 'Information xref.checked-in-part at offset 173: The cross-reference chain goes on at offset 173, past the sections PdfReaderLimits.MaxXRefSectionCount lets the reader read: the older sections were not checked.'
- The stream's fault leaves the index incomplete, so the index is rebuilt and objects 3, 4 and 7 read through it. A damaged index's rebuild already comes before a guard's null today (LoadObject).

### #208 — what closes it: the handed /Prev is followed under '... relocation' (no lens measured it) — matches today: False
Shape: W1 with the stream's /Prev written 3 bytes past the oldest table (176) and 3 bytes before it (170). Composed build.

Composed build:
- /Prev 176: 'Repair xref.offset-adjusted at 173: The cross-reference section /Prev names at offset 176 was found -3 bytes from there.' and 'Warning xref.section-shifted at offset 173: ... starts 3 bytes before it, at offset 173.'
- /Prev 170: '... was found 3 bytes from there.' and '... starts 3 bytes after it'.
- Both: GetObject(3) reads with no rebuild, and section-malformed (Error) is kept for the stream.
- The section shifted backwards still shows #241's signed distance.

### #208 — what closes it: 'the validator ... judges the older table' (asserted by the lens, not shown with a fault in the older table) — matches today: False
Shape: W1 with the oldest table's row for object 3 written 2 bytes past its header. Control: the same with a sound stream (/W [1 4 2]).

Composed build: 'F Warning xref.entry-shifted at object 3 0, at offset 112: The entry of object 3 gives offset 112, where there is no object header; the object starts 2 bytes before it, at offset 110.', beside 'F Error xref.section-malformed at offset 1583'. The control gives the same entry-shifted.

On main the chain stops at the stream (stream-prev lens, W1), so the oldest table is never read and its rows are never judged: the issue's last bullet holds only with the hand-on.

### #156 x #264 — the two prototypes composed (no lens could see it) — matches today: False
Shape: The xrefstm lens's B1.pdf (a hybrid table whose /XRefStm names the table itself) and B2.pdf (an /XRefStm naming a newer table already read), read whole and under MaxXRefSectionLength 40 or MaxIndexEntries 2.

Composed build, read whole: 'Warning xref.hybrid-stream-lost at 303: The /XRefStm of the cross-reference table at offset 303 names offset 303, the table itself, ...' and 'Error xref.hybrid-stream-wrong at offset 303'. GetObject(3) is read through a rebuild.

Guarded:
- B1 under both guards: only limit.xref-section-length or limit.index-entries, plus 'Information xref.checked-in-part'. GetObject(3) is null and nothing reports the /XRefStm fault, though the guarded read did read the trailer that shows it.
- B2 under MaxIndexEntries 2: the same.

The cause: P1's JudgeStreamPastGuard passes silently over an /XRefStm the chain has read, 'as a whole read passes it over', which P2 makes untrue. On main, B1 and B2 are silent both ways (xrefstm lens).

### composition — the full unit suite — matches today: False
Shape: The composed build: the seven src prototypes, plus guards P1's 40 tests and every lens's probe. Remote corpus linked.

Composed: 9,336 tests, 76 failed, 9,257 succeeded, 3 skipped (suite-composed.log).

74 of the failures are exactly the distinct failures the lenses reported one by one: partial A-prime 15, xrefstm P2 10, reserved (a) 49 and window 6, with 5 tests failing under two or three lenses (listed in interactions).

The two new failures are ReaderLimitsTests.A_table_whose_hybrid_stream_a_guard_stops_reports_the_faults_of_its_prev_a_whole_read_reports with prev {stream} and guard 'stream rows' or 'stream index'. Both fail on 'Expected var fault = whole.Diagnostics to contain a single item matching xref.section-missing or xref.chain-cycle'. #156's branch (c) turns that whole read's loop into xref.hybrid-stream-lost. The guarded and whole reads still agree (guards-composed.txt), so only the test's expected codes change.

### composition — the corpus — matches today: True
Shape: ZzCorpusDump over the 403 documents (168 committed, 235 remote), composed build against relocation/corpus-main.txt.

cmp_corpus.py: 'docs 403 403 same keys True ... F diff 1 F order 0 D diff 0 D order 0 / totals main F 2982 D 1343 / totals branch F 2980 D 1343'.

The only difference is xrefstm P1b's removal of a false finding from vendor/opf-format-corpus/pdfmaker7-powerpoint-va-cancer-database-course.pdf: '- Warning file.size-wrong at offset 1269: ... gives /Size 1261, where ... 1275, makes it 1276.'

The prototypes add no corpus interaction.

### composition — #210's read-in-part report against what the reader serves — matches today: False
Shape: Stream-prev shapes I0b, D5 and D6: a middle cross-reference stream short of rows whose /Index declares object 7, while the oldest table also defines 7. Also H1 and H2: an unreadable hybrid /XRefStm stream.

Composed build, I0b: 'Warning xref.section-unreadable at 1583: The cross-reference stream /Prev names at offset 1583 was read only in part: it holds 1 row where its /Index and /Size declare 3; the rows before it were kept, and the objects only the rest would index are looked for by rebuilding the index.' Yet 'GetObject(7): "old seven"; WasRepaired: False'. The message is false for object 7, which the older revision answers with no rebuild.

D5 and D6 behave the same, and H1, H2, Q2 and Q3 are stale too; in Q2 the answer depends on the order of reads.

Under experiment R every one reads 'new seven' through a rebuild.

### composition — #207 (a table the end of the file cuts before its first row) under relocation b2t — matches today: False
Shape: Partial lens shapes 207-P0, 207-P0-far and 207-Prow.

Under A-prime alone, the false loop: offset-adjusted -133, chain-cycle, then chain-loop (Error), section-shifted and file.size-wrong twice, and file.trailer-missing lost.

Composed with b2t: 'Warning xref.section-unreadable at 346: The cross-reference table /Prev names at offset 346 is there but cannot be read: its rows are not followed by the trailer keyword. ...', with the one finding 'Error file.trailer-missing at offset 351'. There is no relocation, no loop and no shift.

So partial-cut-table's first option needs no A2 once b2t lands. Two notes:
- 'its rows' is said of a table that has none.
- There is no xref.section-malformed, because SectionMalformedRule skips TrailerFault != None. file.trailer-missing (Error) covers the section.

### composition — #208's hand-on predicate against #281's b2t and c9b — matches today: False
Shape: Stream-prev P5 (/Type /Foo, /W [1 -4 2], /Prev) and P6 (no /Type), with the hand-on narrowed to /Type /XRef as in the composed tree.

Composed build:
- P5: 'Warning xref.section-missing at 1583: The cross-reference section /Prev names at offset 1583 is not there, nor near it; ...' and 'Error xref.section-not-found ... the offset holds object 5, which is not a cross-reference stream'. The chain stops and the index is rebuilt at opening.
- P6: xref.section-unreadable, and the chain stops after a fruitless search.

B as measured hands on any stream that has a /Prev. Under c9b it would report P5 'not there, nor near it', though no search was made, and still follow its /Prev: the two predicates contradict each other.

Every other shape of stream-prev's output is unchanged, apart from #210's added reports. In the relocation probe, S6-streams now hands B's /Prev on: the oldest table is read and the catalog with no rebuild.

### composition — #197's 'a section 500 bytes after, found' test against #188 — matches today: False
Shape: Window lens S5: /XRefStm names 400, and the table that names it lies at 900.

Under P197 alone: a Repair offset-adjusted and a Warning section-shifted. The relocation lands on the table itself.

Composed with relocation (a): 'Warning xref.section-missing at 400' and 'Error xref.section-not-found at offset 400: ... nor within 512 bytes of it: the offset holds neither the xref keyword nor an object.'

So #197's 'found 500 bytes after' test for a section must name an unread section through /Prev (window S2). The issue's own shape B, and any /XRefStm that names its own table, become 'missing' whatever the window.

### composition — the 16 test spacers — matches today: False
Shape: relocation/unpad-tests.diff applied on the composed tree plus experiment R. The seven classes that hold spacers (1,664 tests).

68 failed, every one among the failures the same classes give with the spacers kept (unpadR-failures.txt against composedR-failures.txt: none new). All 16 spacer sites can go in the composed batch too, not only under (a)+b2t.

## Prototypes

### composed: the seven src prototypes in landing order (window, relocation, stream-prev narrowed, partial, xrefstm, reserved, guards)
Diff: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/measure/critic/composed-src.diff

The diffs are applied with git apply --3way, in the order window (combined-p197-edge-b), relocation (a-b2t-c9b), stream-prev B, partial A-prime, xrefstm P2, reserved (a), guards P1 with its tests. Every conflict is textual, in PdfFileReader.TryReadXRefChain and in XRefSectionRecord, and was merged by hand.

The merged loop:
- stream-prev's hand-on runs first.
- Then b2t's 'CutByLimit || DeclaresItselfASection || !TryRelocate'.
- Then, for a section kept: _sectionsReadAt.Add, ReportSectionReadInPart, ReportReservedRows.
- Then P1's guard branch.
- Then P2's three hybrid branches. P2's (b) report sits beside _sectionsReadAt.Add and ReportSectionReadInPart for the /XRefStm stream.

One semantic change: UnreadStreamNamingPrevious requires TypedXRef, so that #208 hands /Prev on exactly where b2t says a section is there. The messages and flaky diffs also apply cleanly on the composed tree (git apply --check).

Shapes after: Each lens's probe on the composed build, compared with that lens's own prototype output:
- Relocation (against shapes-ab2tc9b): identical except S6-streams, whose /Prev is now handed on; the oldest table is read and the catalog with no rebuild.
- Partial (against shapes-ap2): identical except 207-P0, -P0-far and -Prow, now reported unreadable where they are, with file.trailer-missing kept and no false loop.
- Stream-prev (against shapes-b1-mx): unchanged except P5 and P6 (see reproduced), and A-prime's read-in-part report added to I0b, D5 and D6.
- Xrefstm (against repro-p2): identical except E1, which now reads as E2: section-missing and section-not-found, where P2 alone gave the wrong wording 'a cross-reference table, whose rows were read in the stream's place'.
- Guards (against shapes-p1final): identical except the hybrid shapes where the table's /Prev names its own stream. Read whole and guarded alike, they now give xref.hybrid-stream-lost and xref.hybrid-stream-wrong in place of chain-cycle and chain-loop, and the stream is read through /Prev.
- Reserved (against shapes-a): identical.
- Window (against combined-run1): .261 and .hide identical; .197 differs only at S5, which is now missing.

Unit suite: 9,336 total, 76 failed, 9,257 succeeded, 3 skipped. That is the 74 distinct failures the lenses reported, plus 2: ReaderLimitsTests ...hybrid_stream_a_guard_stops... prev {stream}, guards 'stream rows' and 'stream index'. Those two expect chain-cycle where #156's branch (c) gives hybrid-stream-lost (suite-composed.log).

Corpus: F diff 1, D diff 0: pdfmaker7's false file.size-wrong goes, as xrefstm P1b alone measured. Totals F 2,982 to 2,980 over the two rounds, D 1,343 (corpus-composed.txt).

### R (critic's experiment): a cross-reference stream that lost rows refuses the numbers it declares to older rows
Diff: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/measure/critic/experiment-R-refuse-lost-rows.diff

On top of the composed tree, a new RefuseWhatALostRowDeclared(section) calls stream-prev's RefuseDeclared(section.Trailer) in two cases:
- a cross-reference stream kept Read or Relocated with Incomplete (#210's rows short), not cut by a guard;
- a hybrid /XRefStm stream that is Malformed and gives /Type /XRef, not cut by a guard.

It is called after each section of the chain and after the /XRefStm stream. Numbers the stream does hold are already in the index (first wins), so refusing every number it declares changes only the numbers it lost. That is 49 diff lines. A classic table read in part is not covered; its lost numbers are known exactly, from the bad row's number to the end of the subsection its header gives.

Shapes after: Stream-prev probe: I0b, D5, D6, H1, H2, Q2 and Q3 now read object 7 as 'new seven' through a rebuild, where the composed build served 'old seven' with no rebuild. In Q2 and Q3 the answer no longer depends on the order of reads. Everything else is unchanged.

Partial probe: identical to the composed build.

The read-in-part message ('the objects only the rest would index are looked for by rebuilding the index') becomes true of the streams.

Unit suite: 9,338 total, 79 failed, 9,256 succeeded, 3 skipped (suite-composedR.log). That is the composed build's 76, plus ZzCritic run without its environment, plus 2 real changes: GuardCutValueTests.A_section_lacking_rows_beside_a_value_a_guard_cut_reaches_the_guard_once_the_chain_is_read with naming /Prev and /XRefStm. The same four codes are reported in another order: 'Expected ... {xref.section-unreadable, xref.rebuilt, object.redefined, limit.object}, but {xref.section-unreadable, limit.object, xref.rebuilt, object.redefined}'.

Corpus: Not rerun. R reaches only a stream section read Incomplete and not cut, or an unreadable hybrid stream. The partial lens's corpuspartial dumps show no corpus section read with a fault, and the stream-prev lens found no unreadable section past the first in 403 documents.

### spacer removal on composed + R
Diff: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/measure/relocation/unpad-tests.diff

The relocation lens's unpad-tests.diff removes the 16 spacer sites (ChainFiles, CrossReferenceChainTests Updated(padding: 600), DocumentReaderTests:2454, GuardCutValueTests:3933 and :3963). It is applied on the composed tree with R, then reverted.

Shapes after: Does not apply.

Unit suite: The seven spacer classes: 1,664 tests, 68 failed. Every failure is one the same build gives with the spacers kept; none comes from removing them (unpad-composedR.log).

Corpus: Does not apply (tests only).

## Questions

### B19-1-section-there (#281, #188, #208 (with #263's accepted row; the relocation lens's Q1 and Q2 and the stream-prev lens's 208-relocation merged))
The /Prev or /XRefStm offset holds a cross-reference section the reader cannot read. Which objects count as 'a section that is there'? Such a section gets three treatments: it is reported unreadable where it is, it is never relocated onto a section nearby, and, if it is a stream whose dictionary gives /Prev, it hands that /Prev on to the older sections (#208).

- [R] The xref keyword, or a stream whose dictionary gives /Type /XRef; an object that only gives a /W stays a guess (composed build: relocation (a)+b2t+c9b, hand-on narrowed to /Type /XRef): Behavior, measured on the composed build:
- Every #188 and #281 shape reads as the same file with the section out of reach.
- S6 (a malformed table or stream within 512 bytes of an older, unread one): the section is reported unreadable, Error xref.section-malformed is found, and object 4 is read through a rebuild. Main loses object 4 in silence, with a Repair and Warnings only.
- #207's table cut before its first row: xref.section-unreadable where it is, file.trailer-missing kept. With relocation it gives a false loop.
- #208's W1 hands its /Prev on: the oldest table is read and object 3 needs no rebuild.
- An object that only gives a /W is looked for nearby, as #263's tests require. With nothing near: one naming another /Type (a CIDFont, P5) gives xref.section-missing and the Error xref.section-not-found 'the offset holds object N, which is not a cross-reference stream'. An untyped one (P6) gives xref.section-unreadable. Neither hands its /Prev on.

Codes and ADR 45 severities are unchanged. Cost: one predicate shared by relocation and hand-on (DeclaresItselfASection), a TypedOther flag, and about 12 lines in ReportLostSection.

Suite: the lenses' expected failures only, plus a 2-case test update (see #156). Corpus: unchanged apart from #156's pdfmaker7. All 16 test spacers can go.

Docs:
- diagnostics.md :105 and :106;
- validation-rules.md :55;
- threat-model.md :416, and #263's accepted row at :534-536;
- the TryRelocateXRefSection and TryReadXRefChain remarks.

Without c9b, a CIDFont at the offset with nothing near keeps the false 'The cross-reference stream ... is there but cannot be read', filed as debt.
- The same line for relocation, but any stream whose kept dictionary gives /Prev hands it on, typed or not (stream-prev B as measured, without c9b): P5 (/Type /Foo) and P6 (no /Type) with a /Prev are followed to the oldest table. The reader then has two notions of 'there': a stream that is a guess for relocation becomes a section for the hand-on.

c9b must then be dropped: it would report P5 'not there, nor near it' when no search was made, and still follow its /Prev. A CIDFont named with nothing near keeps a false message about a cross-reference stream.

Suite and corpus as option 1.
- Relocate first for every malformed section, skipping only the positions already read (relocation (a) alone); #208 hands /Prev on only after a fruitless search (A1): Fixes every listed #188 and #281 shape. But in S6, and in #208's R2 and R3, a malformed section within 512 bytes of an older, unread one is read as that one: what only it indexes reads as null with no rebuild, reported by a Repair and Warnings, never an Error.

#207's table cut before its first row then relocates onto the newest table (a false loop), unless every table the end of the file cuts is kept as read (partial A2).

One test spacer, Updated's Pad, must stay.

Why: One notion of a section that is there decides all three behaviors, and the composed build shows it holds:
- every lens's shapes, the suite and the corpus are as each lens measured;
- the only cross-lens cases (S6, 207-P0, P5, P6, E1, S5) come out right.

In the corpus, relocation fires once, from an offset 12 bytes into a table's rows, never from a declared section, so the line costs nothing on real files. #263's tests (a CIDFont or an untyped /W object at the offset, the real section 61 bytes on) keep passing because the line is drawn at /Type /XRef. The prototype that relocates every malformed section (b2) breaks them and is ruled out.

### B19-2-older-rows (#208, #210 (a composition finding; the stream-prev lens's 208-older-rows and its 'rows short' neighbor merged))
The chain reads past a cross-reference stream whose rows it lost. That stream may be one it cannot read and whose /Prev it hands on (#208), one read only in part because its data holds fewer rows than its /Index declares (#210), or an unreadable hybrid /XRefStm stream. May an older section's row then stand for a number that stream declares?

- [R] No: the numbers such a stream declares are refused to older sections' rows, and what it redefined is found by a rebuild (stream-prev B's RefuseDeclared, reused by experiment R): Measured:
- W1's object 7 reads 'new seven' through a rebuild, while object 3, only in the oldest table, needs none.
- I0b, D5, D6, H1 and H2 read 'new seven' where the composed build served 'old seven' with no rebuild. Q2 and Q3 no longer depend on the order of reads.
- Where the numbering cannot be read, every number is refused: objects read as on main, but the older sections are read and judged.
- #210's read-in-part message ('looked for by rebuilding the index') becomes true.

Codes and severities are unchanged. Cost: B's range array, with a binary search per older row while a refusal exists. MaxRefusedRanges (4,096) is an internal constant, which only a file with unreadable sections reaches (ADR 34).

A timing test with 1,024 chained unreadable streams, each declaring many subsections, must ship with it (invariant 4); none was run.

A classic table read in part would refuse the rest of its faulty subsection, numbers its header gives (not prototyped).

Suite: as composed, plus 2 GuardCutValueTests whose four codes change order. Corpus: no section it touches.
- Only for an unreadable stream whose /Prev is handed on (B as measured): #208 is safe. A stream short of rows, and an unreadable /XRefStm stream, keep letting older rows answer for their lost numbers (I0b, D5, D6, H1, H2: 'old seven', no rebuild), now under #210's report. That report's text must then say an older section's row may stand, or it is false.

The stale answers are filed as debt under M02.
- Yes, as the issue words #208: hand /Prev on, refuse nothing (A2): 24 lines. W1's object 7 reads its older revision with no rebuild, and the reader reports nothing about that object. In one document the same number reads 'old seven' and then 'new seven' once a rebuild has run (Q1). That is against invariant 7 and against determinism as a caller sees it.

Why: The reader already refuses older rows for a number a newer section refused (_refusedNumbers, PdfFileReader.cs:545-551). A section that lost its rows for a number it declares is the same case.

The composed build shows the gap is not confined to #208: #210's new report would announce a rebuild for objects the older revision answers silently.

Reusing RefuseDeclared costs 15 lines. Its suite effect is two report orders, and no corpus document reaches it.

### B19-3-read-in-part-code (#210, #207 (and the refused rows of #210's first comment); the partial lens's partial-code)
Under which code does the reader report a cross-reference section it read only in part? The section's rows are kept and the chain goes on, startxref's section included. The faults are a row that is no row ending a subsection, a stream holding fewer rows than it declares, a table the end of the file cuts before its trailer, and a row refused alone. The report is a Warning, once at the section, with the first fault in the message.

- [R] Reuse xref.section-unreadable, its message 'was read only in part: <fault>; ...' (prototype A-prime as measured): No new public constant. It is #207's own text ('under xref.section-unreadable when the chain named it').

Once #208 lands, the code already names a section the chain goes on past, so 'the chain stopped here' is no longer what it means. Its documented consequence is already 'the rows read before the fault were kept; what only the rest indexed is found by rebuilding'.

The XML doc and diagnostics.md :106 widen to 'cannot be read whole' and to startxref's section. The validator is unchanged (xref.section-malformed and file.trailer-missing, both Errors).

The remaining asymmetry, that a startxref section unreadable whole is reported only as xref.rebuilt, is filed as debt. Suite: A-prime's 15 failures. Corpus: unchanged.
- A new code, xref.section-incomplete (variant N): One constant in PdfDiagnosticCodes, one value in manifest.schema.json, and a diagnostics.md row. xref.section-unreadable keeps 'is there but cannot be read'. Since #208 also gives that code a section the chain goes on past, the two codes then differ in the fault, not in the consequence for a caller.

The same 15 tests take the new code. Corpus: unchanged.
- Two new codes: xref.section-incomplete (rows lost) and xref.row-refused (one row refused, the rows after it read): Two constants, two rows in diagnostics.md and two schema values: the finest split. A caller filters both for the same consequence, and the validator still folds both into xref.section-malformed.

Why: The partial lens's case for a new code is that one code would mean both 'the chain stopped' and 'the chain went on'. #208, in this same batch, makes xref.section-unreadable mean both anyway.

The consequence a caller acts on, objects found by a rebuild when asked for, is identical. The code's existing documentation already says it, and #207 asks for this code. Reuse keeps the public code set smaller at no loss of information, since the message carries the fault.

### B19-4-no-subsection (#222)
A classic table with no subsection ('xref', then 'trailer'): how is it reported, and does the reader mark the index incomplete for it?

- [R] Its own codes: reader xref.section-empty (Repair) and rule xref.subsection-missing (Warning). The table is read as indexing nothing, as '0 0' is, and the index is left whole (prototype A-prime): EU2, EU2p and EC2 gain 'Repair xref.section-empty: The cross-reference table {naming} names at offset N holds no subsection: its trailer keyword follows its xref keyword. It was read as indexing nothing.' and the finding 'Warning xref.subsection-missing'. EU1 and EC0 stay silent, and nothing is rebuilt.

The two identifiers must differ (ValidationRuleIdTests). Each needs a rule class, rows in validation-rules.md and diagnostics.md, and a schema value.

Two tests that write 'xref'/'trailer' as an empty update move to '0 0'. One of them writes 500 such sections. Corpus: 0 of 403 documents (28 tables write '0 0', none writes no subsection).
- Folded into the read-in-part report (B19-3), with the index incomplete and the validator's xref.section-malformed (Error): No new code or rule. Validation's settling then rebuilds the index before its rules (PdfFileReader.cs:1018), so ADR 45 makes the finding an Error. Yet EU2 indexes exactly what the lawful EU1 does, and every writer that omits '0 0' would cost a rebuild.
- As the first option, the reader's diagnostic a Warning rather than a Repair: #222's own words, 'an update holding none indexes nothing and may have lost what it meant to index', fit Warning ('the result may not be what the caller expects') as well as Repair ('the reader worked around it'). Nothing else changes.

Why: The reader reads the table as written, the same as the '0 0' table it indexes as much as, and rebuilds nothing. That is a Warning under ADR 45, the bound #222 sets. A Warning needs a rule of its own, because xref.section-malformed is an Error and one rule keeps one severity.

### B19-5-xrefstm-names-no-stream (#156 (the xrefstm lens's Q156-1 and Q156-2 merged; #156 says the maintainer decides both))
A hybrid table's /XRefStm may name something other than a cross-reference stream of its revision: the table itself, another table the chain read, a table read in the stream's place, or a section read through /Prev or startxref. How do the reader and the validator report it, and does the reader mark the index incomplete, as it does for a /Prev loop?

- [R] A new reader code, xref.hybrid-stream-lost (Warning), the index marked incomplete, and a new rule, xref.hybrid-stream-wrong (Error) (prototype P2): B1 and B2: the lost page is found by a rebuild when it is asked for, and page-tree.kid-invalid goes. B3: the rows are read in the stream's place and the Error is reported. B4 and C2: one Error, no false chain-loop (with branch (c), decided).

The ADR 45 Error stands either way: the stream the table meant is lost, or the reader chose a table in its place. The names are proposals. Docs: rows in validation-rules.md and diagnostics.md, and chain-cycle and chain-loop narrowed to /Prev.

A guarded read must give the same report where the stopped table's trailer shows the fault (composition finding, decided). Corpus: only pdfmaker7's false size-wrong goes.
- The same codes, but the index not marked incomplete (report only): B1, B2 and E1 keep object 3 as null with no rebuild, and page-tree.kid-invalid stays beside the new Error. Validation does not rebuild. This is inconsistent with the chain's remarks, where a lost section leaves the index incomplete, and with xref.chain-loop.
- Widen xref.chain-loop and reuse xref.chain-cycle: Fits a naming of a section already read. But ChainLoopRule must iterate the records rather than read LoopOffset alone, and chain-cycle's text ('loops back on itself') would describe a chain that goes on. B3, a table never read, is no loop and would stay unreported.

Why: Only a new rule covers the shapes #156 names with one wording and one severity. Marking the index incomplete follows what the reader does for a /Prev loop (PdfFileReader.cs:2954), and the cost falls on non-conforming files alone: none in the corpus.

### B19-6-reserved-type (#221 (the reserved lens's Q221-1, Q221-3 and Q221-4 merged; Q221-2 is settled by invariant 5))
A cross-reference stream row of a reserved type: a type past 2, or a type field wider than 8 bytes whose excess bytes are not all zero. How does the reader read it, and under which codes and severities is it reported, by the reader and by the validator, once per section?

- [R] As ISO 32000-1 7.5.8.3 reads it, a reference to the null object: the row is indexed as a free entry, so that no older section's row stands for its object. Reader xref.entry-reserved (Warning) and rule xref.entry-type-reserved (Warning) (prototype (a)): X09, X09n, X09w, X09w8, X09d and X09m read null, where main serves the older definition or the second subsection's. X04, X01 and X04w are unchanged (null).

Reserved rows count against MaxIndexEntries as free rows do: 8 million of them reach limit.index-entries, with allocation unchanged.

Tests: about 44 fixtures that write reserved types by accident must expect or exclude the diagnostic, and 8 tests change meaning (counts +2, the deferred-length row test inverts, 3 HostileInputTests fixtures unlist object 5 another way).

The rule's text must be checked against the standard first. 'ISO 32000-1 allows types 0, 1 and 2 only' was quoted from memory. If ISO only defines them, reading others as null 'permitting new entry types', the message must say 'defines', not 'allows'. The Warning stands either way.

Corpus: no reserved row in 403 documents.
- Skipped as today, an older section's row standing, and reported (prototype (b)): No change in what objects read. The comments, the threat model's rows :421-:422 and validation-rules.md :54 stop claiming the specification. Under ADR 45 this takes two rules: Warning where no older row stands, Error where one does (the reader serves what the update superseded). Telling the two apart needs a set of skipped numbers, bounded by the index guard and checked once the chain is read.

This matches libqpdf 12.3 and later, MuPDF and pypdf, but a later full rewrite would write the superseded revision as current.
- Reading (a) under other names (the maintainer's): The same cost as the first option. #221 asks the maintainer to name the codes; xref.entry-reserved and xref.entry-type-reserved are proposals, and a rule id may not equal a code.

Why: ISO names the reading, null, and never says to ignore the row. The readers the lens compared split four ways and none reads null, so no other reader is a reference.

Under (a) the file is read as the specification says: a plain Warning under ADR 45, one rule, and the reader never serves a revision an update superseded (invariant 7). Invariant 12 is not at stake, because no valid file holds such a row. Invariant 5 requires the reader's diagnostic whichever reading is chosen.

### B19-7-stream-end-rule (#261 (its consequence for HeaderText; the window lens's W3))
The search for a damaged stream's endstream stops at the first object header it meets. Its rule (HeaderText.FindHeader, PdfObjectParser.IsHeader and MayBeHeaderBytes) refuses a header with a comment between its tokens 'as the rebuild's scan does': your decision of 2026-09-30 (docs/status.md:2900; HeaderText.cs:131-133). Once #261 makes the scan read such headers, should the stream-end rule follow?

- [R] Keep the rule in batch 19, reword its reason in the four texts that state it, and file a debt to extend it: No stream's reading changes. StreamLengthTests' rows '12 0 %\nobj' and '12 %\n0 obj' stay non-headers. The scan and the stream-end rule then differ on comments, as they already do on 'x12 0 obj' and '12 0 objx' (#171, #189), and the texts say so.

The texts: HeaderText.cs:131-133, concepts/diagnostics.md:52-53, reference/diagnostics.md:116 and status.md's record. The debt: HeaderText must carry a comment state across the reads it splits data into, where today its carried bytes model only white space and digits.
- Extend the stream-end rule in batch 19: HeaderText carries a comment across read boundaries, IsHeader and MayBeHeaderBytes accept comments between tokens, and the two StreamLengthTests rows flip. A damaged stream then stops at a comment header. It is more code and review in an already wide batch.
- Keep it by design, with no debt: The texts state the difference as intended: the stream-end rule reads bytes alone, while the scan reads comments as the lexer does.

Why: Your earlier reason, consistency with the scan, goes away, so the choice is yours again. Extending HeaderText is not a local change, because it carries runs across read boundaries. Keeping the rule with a debt holds the batch to its scope without hiding the difference.

### B19-8-flaky-317 (#317)
ReaderTimingTests.The_validator_judges_an_object_stream_s_members_in_linear_time flakes because the collector runs inside the large probe. That probe keeps a finding for each of its 200,000 members at room int.MaxValue: 63 to 75 MB and 5 to 7 collections, against 1 for the small probe. #317 lists three remedies. Which one closes it?

- [R] Give the probe a validation's default room, 1,000 findings, and keep five turns and the bound of 8 (D; not one of #317's three): Test-only; AdCodicem.Pdf.dll is byte-identical to main's. No collection falls in the wrong-index probes.

Correct ratios run 2.2 to 6.1 over 162 runs, with none at or above 8, including under four busy loops, where main failed 5 of 10. The header-scan mutants measure 13.2 to 20.0 and were caught in 10 of 10 real runs. The suite passes on .NET 10, under coverage and on .NET 11. The test takes 5.4 to 6.4 s.

What stops being timed: probing with room for every finding, a path a caller reaches by raising PdfValidatorOptions.FindingCapacity, which has no upper bound. An allocation ratio at room max, deterministic at 4.03 on every run, can hold that path's allocation without the flake, if wanted.
- Widen the bound to 12, as b2d5e9b did (#317's first way, A): Under four busy loops it still failed 1 run in 10 (12.17), and it let the header scan through 2 runs in 12: when caught, the scan measured only 12.61 to 17.19. The claim that '12 still tells a scan apart' is false as written.
- Hold allocation and time through BestOfEach below 12 (#317's second way, B): Allocation is deterministic (4.03, 4.01, 4.00) but blind to the regression the test exists for: the scan mutants allocate as main does. Detection then rests on the time bound of 12, which the scans cleared by 1.16 at worst. It is the slowest option: 12.9 to 18.6 s, up to 26.4 s under load.
- Take more turns with BestOfEach at bound 8 (#317's third way, C): Does not fix the flake: the cost is systematic in every large probe. Under busy loops it failed 5 runs in 10, as main does.

Why: D removes the cause instead of widening a bound around it, and it is what a validation actually runs. The bound of 8 then sits between the worst correct run (6.13) and the best-caught scan (13.19) in every condition measured. A gives up the margin that catches the scan, and C changes nothing. #317 says 'one of these', so a fourth way needs your consent.

## decided_without_question

- #197: fix the window instead of documenting the reach (the window lens's W1). Both searches end at the offset plus 512, or the file's end, computed as o + Math.Min(512, Length - o), with the guard for offsets past -512 kept.
#197 offered the documentation route as the maintainer's alternative, but the measurement leaves it no ground: 0 of 403 corpus documents change, the suite does not change, and every text already says 512, so the fix makes them true. Composed: the window shapes are as P197 gave them.
- #171's left-edge bullet is paid with #197 (P197e): the window reads one byte before its start, and a header or candidate counts only where it starts inside the window. It is 3 lines in the code #197 rewrites, with zero corpus and suite change, and it makes 'within 512 bytes' exact at both edges. A comment on #171 records it.
The implementation filters candidates inside FindSectionCandidates, not with the prototype's RemoveAll and lambda: no closure on a parsing path (invariant 3's spirit).
- #261: candidates are confirmed forward from a token boundary (P261-B, the window lens's W2), not by the backward heuristic A.
- #261 asks for headers read 'as the lexer reads it'. B lexes the line with PdfLexer, the load's own lexer. A adds a heuristic of its own: it refuses after a parenthesis, and reads '%' in hex strings otherwise than the lexer.
- B is linear and allocates nothing. Its worst case is about 3 ns a byte on files built to cross many lines holding '%' (12 ms per 4 MB). Neither variant moves time or allocation on sound files or on 100,000 headers.
- Corpus: 0 changes; no corpus file has a comment header.
- #197's tests: the 'section 500 bytes after, found' case names an unread section through /Prev, as the window lens's S2 does. An /XRefStm that names its own table is 'missing' once #188 lands, whatever the window (composed S5).
The 'obj' keyword must end within 512 bytes of the row: K7 against K8.
- #261 switches the test fixtures that hide a header from the scan with '% ...\nobj' to 'N +0 obj', through one shared ChainFiles helper, in the commit that lands #261. Two reasons:
- The tests of other lenses use the device (ReadOrderTests, ReaderLimitsTests:1090, DocumentReaderTests:1944, ObjectRuleTests, ConvergenceShapes).
- The new device relies on a blind spot of the scan, which refuses signed numbers. If that neighbor is ever fixed, one helper changes.
- #188: the set relocation skips holds positions where the chain read or relocated a section, the /XRefStm stream included, the header's offset counted, and it takes SeededComparer. It does not hold visited's written offsets: prototype av loops on S8, and S1j shows that written offsets and positions differ by the header's offset.
b2 (no relocation for anything Malformed) is ruled out: it breaks #263's two tests. c9 is rejected as wider than c9b with no shape to justify it.
- #207: a table the end of the file cuts is kept as read in part when it read a row (section.HighestNumber >= 0), #207's own rule, and is unreadable when it read none (the partial lens's partial-cut-table, first option).
Composed with b2t, the no-row case is reported where it is, with file.trailer-missing kept and no false loop, so A2 is not needed. This holds unless B19-1 takes its third option.
The no-row message reads 'the file ends before its trailer', not 'its rows are not followed by the trailer keyword'.
- #207 and #210: file.size-wrong does not judge a chain one of whose sections was read only in part (the partial lens's variant C, keyed on section.Incomplete). It removes six false Warnings with no corpus or test change, and the rule's own principle ('not judged when the chain lost a section') covers it. It merges with #156's SizeWrongRule change in one edit.
- #210: a section read in part is reported once, at the section, with its first fault in the message, whatever a guard also did (prototype A failed nine #263 tests for leaving guard-cut sections out). A section a guard cut and that lost no rows stays the guard's (ADR 34).
startxref's section read in part is not rebuilt at opening.
- #208: tests use a fault that still faults, /W [1 -4 2] or /W [1 4]. The issue's /W [1 9 2] is paid on main: WidthFault no longer bounds a width.
- #208: a /Prev handed on is followed under visited, the section count guard and relocation. The critic probe shows all three on the composed build: limit.xref-section-count with checked-in-part; offset-adjusted at -3 and +3; P10 loops through visited.
The validator judges the older table: entry-shifted is found there, and is never found on main.
The stream's dictionary is never merged as the document's trailer; only its /Prev is read, and file.trailer-value-wrong judges that /Prev alone.
- #208: startxref's section unreadable stays a rebuild at opening (the stream-prev lens's 208-first-section). Classic tables mid-chain (208-tables) are not read on to their trailer. Both go to debt (see debts).
- #156: a classic table an /XRefStm names keeps being read in the stream's place, as batch 16 settled (#263); it is now reported and leaves the index incomplete (the xrefstm lens's Q156-3).
- #156: an /XRefStm naming the very offset its table's /Prev names is not followed, and the /Prev reads the section whole (branch (c), the xrefstm lens's Q156-4).
- B4 and C2 lose a false chain-loop, and C2's older chain is no longer lost to a rebuild.
- The four one-table loop tests (HeaderOffsetTests:89 twice, CrossReferenceRuleTests:478, CrossReferenceChainTests:140) move to a two-table shape that still loops through an offset only named.
- The guards lens's ReaderLimitsTests {stream} cases expect hybrid-stream-lost and hybrid-stream-wrong (composed: whole and guarded agree).
- #156: a shared stream's rows count for each table that names it, and its own /Size is judged at its oldest naming (P1b). pdfmaker7's manifest drops file.size-wrong.
The NamedAgain lookup uses a Dictionary keyed like visited: the linear scan cost 31 ms on 1,000 self-naming tables.
A HostileInputTests test holds that 1,000 tables naming one stream read it once: 50,368,016 bytes on main and on P2 alike.
- #264 x #156: a section a guard stops judges its /XRefStm as a whole read does, the record and report of #156 included. JudgeStreamPastGuard records NamedAgain and calls ReportNoStream for an /XRefStm the chain read that names no stream, instead of passing it over.
Otherwise B1 under MaxXRefSectionLength 40 or MaxIndexEntries 2, and B2 under MaxIndexEntries 2, stay silent where the whole read gives an Error (critic probe). Tests: B1 and B2 under each rows guard, beside the whole read.
- #264: P1, not P0 (the guards lens's Q1). The /XRefStm outside the file and the hybrid-stream stop are faults of the same kind, visible without reading past the guard. P0 would leave the ADR 34 amendment and the reader-limits docs false for hybrid files. P1 has 40 tests, suite 0 failures, corpus unchanged.
- #221: the reader and the validator both report a reserved row (the reserved lens's Q221-2). Invariant 5 requires the reader's diagnostic while reading continues, and the validator finds it at the section.
Severities follow ADR 45 from the reading B19-6 picks (Q221-4): under (a), Warning and Warning; under (b), two rules.
- #241: settled by the issue's text and the codebase's precedent (the messages lens's 241-Q1, Q2, Q3):
- The distance reads 'N byte(s) before|after where the index said' for objects and 'before|after it' for sections, as #241 words it and as the validator's RuleText.Distance already does.
- Each site keeps its digit grouping, N0 or not, with the inconsistency filed as debt.
- A verb or pronoun agrees with its count, as BrokenObjectStream and CrossReferenceProbe do.
- The four call-site conditions with no count-of-one test yet (P1, P2, P5, P6 in PdfObjectParser; P1 and P2 need the endstream searches spent) get tests under ADR 53.
- New messages in the batch (reserved rows, read in part) write their counts through CountText.
- Test spacers: all 16 sites go, with their doc comments. With (a)+b2t, 1,624 of 1,624 tests pass, and on the composed build with R removing them adds no failure.
- Composition: the prototypes compose. The suite is the union of each lens's own failures plus two test-expectation updates, and the corpus shows only pdfmaker7's one finding. Landing order, one commit or more each:
1. #317 (test only; steadies CI for the rest).
2. #241 CountText, so that every message the batch adds uses it and the corpus diff carries its 1,057 rewordings once.
3. #197 and #171's left edge.
4. #261 with the shared hiding helper, so that tests written later use the new device.
5. #188 and #281: DeclaresItselfASection and the positions set; the spacers go.
6. #208, including RefuseDeclared.
7. #207, #210 and #222: they need b2t for the no-row cut table and reuse RefuseDeclared, as B19-2 recommends.
8. #156: it needs (a) for E1, and its SizeWrongRule edit merges with C.
9. #264: it mirrors #156 in JudgeStreamPastGuard.
10. #221: its report shares the per-section hook with 7.
- Reserved-row and read-in-part reports share one per-section hook in TryReadXRefChain: after each section kept, after a lost section whose rows were read, and after the /XRefStm stream. The order is fixed: read in part, then reserved rows.

## interactions

- CRITIQUE (partial-code, ADR 45 unaffected): the partial lens's case for a new code is weak. It argues that 'one code would mean the chain stopped and the chain went on', but #208 already makes xref.section-unreadable name a section the chain goes on past, and the code's own documentation already states the read-in-part consequence. #207 prescribes reuse. B19-3 flips the recommendation to reuse.
- CRITIQUE (invariant 7): partial A-prime's message says 'the objects only the rest would index are looked for by rebuilding the index'. On the composed build that is false wherever an older section indexes a lost number: I0b serves 'old seven' with no rebuild, and D5, D6, H1 and H2 do the same. The recommendation is incomplete without B19-2's refusal (experiment R), or else the message must be reworded.
- CRITIQUE (one notion of 'there'): stream-prev B hands on any stream whose kept dictionary gives /Prev. Relocation's b2t and c9b treat a stream without /Type /XRef as a guess, so the two contradict on P5: reported 'not there, nor near it' while its /Prev is followed. The composed tree narrows the hand-on to /Type /XRef (B19-1).
- CRITIQUE (#264's principle against #156): P1's JudgeStreamPastGuard is documented as passing over an /XRefStm already read 'as a whole read passes it over'. Under P2 the whole read reports xref.hybrid-stream-lost and xref.hybrid-stream-wrong (Error), so the guarded read becomes silent where the whole read finds an Error (critic probe, B1 and B2). Decided: mirror P2 there.
- CRITIQUE (invariant 4): stream-prev B's RefuseDeclared sorts and merges up to MaxRefusedRanges plus the new ranges at each hand-on, and adds a binary search per older row. The timing test the lens left for later is a condition for landing, not a debt: 1,024 chained unreadable streams, each declaring as many subsections as MaxTrailerLength allows.
- CRITIQUE (invariant 7, #156 deferral): A5, a /XRefStm copied two or more revisions late, serves 'old six' in silence. The reader can detect it at no cost: a NamedAgain record whose first naming table is not the next one in the chain. Deferring it as debt is acceptable only with that named. At least a reader diagnostic is cheap.
- CRITIQUE (#221 rule text): the rule says 'ISO 32000-1 allows types 0, 1 and 2 only', quoted from memory; no copy of the standard was in the container. The implementation checks the text. If ISO only defines those types and reads others as null 'permitting new entry types', the message says 'defines', and the Warning rests on the type being unknown to this version.
- CRITIQUE (flaky D, invariant 9): D stops timing the probe at room max. PdfValidatorOptions.FindingCapacity has no upper bound, so callers reach that path. A deterministic allocation ratio at room max (4.03 on every run) could keep its allocation held at no flake risk. A time-only superlinearity in Keep would still go unseen.
- CRITIQUE (invariant 3): window P197e drops candidates with RemoveAll and a lambda. It should filter inside FindSectionCandidates: not a hot loop, but a parsing path.
- CRITIQUE (positions against written offsets): three places decide 'already read' by visited's written offsets: the /Prev loop check, #156's NamedAgain and firstAt, and #264's pastGuard test. Relocation decides by positions. Relocation's S7 is the measured symptom: a section relocated onto, then named by its own offset, is read twice. An /XRefStm naming the real position of a relocated section would likewise be read again, as 'a table read in the stream's place'. The implementation should use one notion, positions (_sectionsReadAt), for all four, or file it with S7.
- Shared tests: five tests fail under two or three lenses and must be updated once for the combined behavior:
- ValidatorTests.The_structural_profile_names_the_rules_it_runs_in_order: three new rules, from partial, xrefstm and reserved.
- DocumentReaderTests.A_reference_the_chain_cannot_read_rebuilds_nothing_while_the_chain_is_read: partial's report and window's hiding device.
- GuardCutValueTests.A_whole_reference_a_guard_cut_after_leads_a_chain_on_whichever_section_reads_it_first, both cases, and ...leads_the_own_row_s_length_on_as_a_whole_read_follows_it: partial and reserved.
- relocation x partial: b2t makes #207's table cut before its first row report where it is (composed), settling the partial lens's dependency on #281 and making A2 unnecessary. Under B19-1's third option, A2 is needed.
- relocation x xrefstm: (a) turns E1 into E2 (section-missing and section-not-found). Without it, P2's branch (b) words E1 wrongly ('a cross-reference table, whose rows were read in the stream's place'). Relocation lands first.
- relocation x window: once #188 lands, an /XRefStm that names its own table is missing whatever the window (S5 composed); #197's section tests use /Prev. #261 makes comment-header streams relocation candidates, measured to remove main's loop onto the update's own table.
- relocation x stream-prev: with the hand-on, S6-streams reads the older section through B's /Prev, the honest version of what relocation did by accident (composed). Under b2t, a /Type /XRef stream is never relocated, which settles stream-prev's 208-relocation.
- partial x stream-prev x reserved: RefuseDeclared serves #208 and, through R, the rows a stream short of rows lost. A reserved row read as free under #221 (a) is refused like any older row for a number a newer stream refused.
- partial x xrefstm: both change SizeWrongRule (C's IsWhole, P1b's judgedAt) and add fields to XRefSectionRecord. PageTreePageOrphanedRule.IsIndexSound takes NamedAgain (P2), and Incomplete widens (A-prime): one merge, already done in the composed tree.
- xrefstm x guards: branch (c) changes the guards lens's {stream} expectations to hybrid-stream-lost and hybrid-stream-wrong, both whole and guarded. JudgeStreamPastGuard must mirror NamedAgain (decided). xrefstm lands before guards.
- window #261 x every lens's tests: the '% ...\nobj' hiding device appears in ReadOrderTests (rebuild lens), DocumentReaderTests:1944 (chain), ReaderLimitsTests:1090 (guards #167), ObjectRuleTests and ConvergenceShapes (object streams). Switching it in the #261 commit keeps those tests testing what they say.
- messages x everyone: CountText first, so that the reserved, read-in-part and hybrid messages use it. The tests messages rewrites (CrossReferenceChainTests:47, DocumentReaderTests:203 and :2820) are relocation tests. Its corpus wording diff (1,057 messages in 28 documents) is normalized away by wording_check.py when later commits are compared.
- flaky: independent (test only, byte-identical dll). It guards CrossReferenceProbe's per-member path, which ADR 53's mutants on that path rely on.
- The corpus shows no cross-lens effect: the composed dump differs from main only by pdfmaker7's false size-wrong.

## new_debts_or_neighbors

- Debt (M02, invariant 7): a dropped relocation candidate leaves its rows in the index (relocation S8). The old revision is served with WasRepaired false, reported only as section-unreadable and section-shifted. Reproduced on main and on every prototype, including the composed build's relocation probe. The fix needs a rollback mark in PdfXRefTable, or a candidate's rows staged until it is taken.
- Debt (minor), or decide it in the batch: 'already read' by written offsets in the loop check, NamedAgain and pastGuard, against positions in relocation. Relocation S7 shows a section read twice, and a relocated section's position named by /XRefStm would be read again.
- Fix in batch (recommended), else debt: trailer.root-recovered reported twice when a rebuild after opening recovers the catalog again (N-DUP). It exists on main with a refused row, and partial A-prime makes it reachable by #207's own first-section shape (207-F1). The batch's test would otherwise pin a duplicate report.
- Debt (M02): a table whose trailer keyword is followed by no dictionary ends the chain in silence. What only older sections index reads null with no rebuild (N-TND; corpus remote/ipres2017/t04-009). Folding it in reaches N-DUP, and FileRuleTests pins today's choice for the first section.
- Debt (M02): when startxref names an unreadable cross-reference stream, the reader reports only xref.rebuilt, never xref.section-unreadable (stream-prev S1). Under B19-3's reuse, this is the asymmetry left with a startxref section read in part.
- Debt (M02): a classic table mid-chain with a bad row, a bad subsection header or a stray token stops the chain (stream-prev T1 to T3). Variant C finds the trailer, but the numbers of the rows past the fault are unknown.
- Debt: SectionMalformedRule counts a section only when TrailerFault == None (SectionMalformedRule.cs:40). P7 (a dictionary syntax fault with /W [1 -4 2]) gets only file.trailer-malformed. 207-P0, composed, gets only file.trailer-missing, an Error, so it is not silent: check that this is intended.
- Debt (M02, invariant 7): if B19-5 leaves it out, a /XRefStm copied two or more revisions late (#156's A5) serves the stale stream's rows in silence. The reader can detect it: a NamedAgain whose first naming table is not adjacent in the chain.
- Debt: digit grouping is inconsistent across messages. One object-stream header fault is worded twice: ReadObjectStreamHeader ungrouped, quoted by xref.object-stream-broken, and object-stream.unreadable grouped (PdfFileReader.cs:6210-6232 against :7771-7797).
- Comment on #171: batch 19 pays the nearby window's left edge (P197e, if decided). The 1 MB chunk overlap now also misses comment headers longer than about 61 bytes; one of 106 bytes was missed. '1' followed by 70 zeros and '4 0 obj' across the chunk start reads as object 4.
- Neighbor, worth a line or an issue: a header with a signed number. '4 +0 obj' is read by a load at its row but missed by the scan and the nearby search, and '+4 0 obj' is taken as 4 because the byte before the digits is never checked. #261's new hiding device relies on the first.
- Comment on #292: the same header-cut rebuild is reached through a rows guard (MaxXRefSectionLength or MaxDecodedStreamLength) together with MaxObjectLength (guards objcut-main.txt). P1 leaves it byte-identical, so #292's fix must cover that path.
- Debt or #163: a /Prev or /XRefStm in a trailer MaxTrailerLength cut (TrailerCutByLimit) is never judged, even when the cut left the value whole.
- Check against open debt before filing: file.root-invalid says 'No object of the file is a catalog.' after a rebuild found one through a rebuilt trailer (guards lens, shape F read whole).
- Code observations, unmeasured:
- Refuse sets _indexIncomplete even when a newer section already gave the number.
- ReadSubsection sets _indexIncomplete for a relocation candidate that is then dropped.
- Neighbor of #317: Four_times_as_many_object_streams_that_need_one_another_are_judged_circular... (ReaderTimingTests.cs:307) times ObjectDependencies at room int.MaxValue with bound 12 (b2d5e9b). A validation's room may let it return to 8.
Also: the first row of each timing theory in a fresh process is timed during JIT tiering (ratios down to 0.68).
- Corpus contribution (invariant 10): no corpus document has an unreadable cross-reference section past the first. #208, #210's stream cases and #281 are accepted on synthetic files alone. docs/corpus-contributions.md should ask for a real incremental update with a damaged middle cross-reference stream.
- Integration note (#221): qpdf changed between 12.2 (rebuild on an unknown type) and 12.3 (silent skip). A container test of this shape depends on the qpdf version pinned.
- Observation, no issue: missing trailers are judged two ways. Rows that run into '<<' with no keyword stay Malformed (PdfFileReader.cs:3518-3533). Rows the end of the file cuts become read in part. The difference can be recorded where both are declared.

## docs_to_update

- src/AdCodicem.Pdf/IO/PdfFileReader.cs remarks:
- TryReadXRefChain (:2923-2934): a section that is there and unreadable is reported where it is; a /Type /XRef stream hands its /Prev on; a section read in part is reported and leaves the index incomplete; what a stopped section names is judged.
- TryRelocateXRefSection (:3226-3239): which candidates are skipped; never called for a section that declares itself one.
- ReportLostSection (:3166-3174): c9b.
- NearbySearchRadius (:68-74): 'within' means a header lying whole in [o-512, o+512), clamped, at both edges.
- TryFindObjectHeader (:6056-6060) and TryReadHeaderBackwards (:6283-6293): comments, read forward from a line start.
- ReadTrailerPastRows and SectionOffset (rowsHidden).
- The comments at :4462-4464 and :5309-5312 (reserved rows).
- src/AdCodicem.Pdf/IO/XRef/:
- XRefSectionRecord.cs: Incomplete widened; NoSubsection, ReadAs, NamesNoStream, ReservedRow and ReservedRows, PreviousHandedOn, TypedOther.
- XRefSectionState.cs: Malformed, said of a table or a /Type /XRef stream; NamedAgain.
- FileStructure.cs: Sections includes NamedAgain records.
- src/AdCodicem.Pdf/IO/HeaderText.cs:131-133, and PdfObjectParser.cs:1636-1660 if B19-7 extends the rule.
- src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:
- XRefSectionUnreadable (:34-44): 'cannot be read whole', startxref's section and refused rows, if B19-3 takes reuse; otherwise the new code.
- XRefChainCycle (:46-50): /Prev only.
- New codes: section-empty, hybrid-stream-lost, entry-reserved.
src/AdCodicem.Pdf/Validation/PdfValidationRuleIds.cs: XRefChainLoop (:100-103), /Prev only; new ids subsection-missing, hybrid-stream-wrong, entry-type-reserved.
The new rules' remarks state their ADR 45 reasons. The RuleText summary points to IO.CountText. CountText is a new file.
- docs/threat-model.md, Indexing rows:
- :411 or a new row: an /XRefStm naming no stream (#156);
- :416: never relocated onto a read section; a declared section never relocated (#188, #281); a /Type /XRef stream hands /Prev on (#208); new tests named;
- :417, :423 and :425: what a stopped section names is judged (#264);
- :421, :422 and :424: reserved rows;
- :427: the scan reads comments (#261);
- :478: the window caveat goes (#197);
- :596: the validator's #197 caveat goes;
- new rows: a table the end of the file cuts and a section read in part (#207, #210); a chain going on past unreadable streams, with refused ranges, MaxRefusedRanges and the timing test (#208).
Known gaps removed: :445 (#221), :447 (#156), :452 (#188), :456-458 (#197), :459-460 (#261). #263's accepted row (:534-536) gets c9b.
- docs/website/docs/reference/diagnostics.md:
- :36: the '-7 bytes' example goes; add that a count agrees with its noun and a distance says which way it runs.
- :104 xref.offset-adjusted: never onto a section already read.
- :105 xref.section-missing: c9b's object of another /Type; the /Prev of a section a limit stopped.
- :106 xref.section-unreadable: read in part; a /Prev handed on; '— and nothing nearby can be read in its place' limited to a /W-only object.
- :107 xref.chain-cycle: /Prev only; past a guard.
- New rows: xref.section-empty, xref.hybrid-stream-lost, xref.entry-reserved, and xref.section-incomplete if B19-3 takes the new code.
- :116 only if B19-7 extends the rule.
- docs/website/docs/reference/validation-rules.md:
- :44 file.trailer-missing: the rebuild sentence is now true of the end-of-file case.
- :47 file.size-wrong: a shared stream counted per naming and judged at the oldest; not judged when a section was read in part.
- :48 file.trailer-value-wrong: a /Prev handed on is judged.
- :54 xref.section-malformed: the reserved-row wording.
- :55 xref.section-not-found: c9b, and past a guard.
- :57 xref.chain-loop: /Prev; past a guard.
- New rows: xref.subsection-missing, xref.hybrid-stream-wrong, xref.entry-type-reserved.
- docs/website/docs/concepts/:
- lazy-reading.md :80-103: read in part; the chain going on past an unreadable /Type /XRef stream, what it declares found by a rebuild; a table with no subsection rebuilds nothing; an /XRefStm that names no stream leaves the index incomplete; relocation never lands on a read section; reserved rows read null.
- diagnostics.md :52-53: B19-7.
- reader-limits.md :64-70: what a stopped section names is judged.
docs/website/docs/reference/reader-limits.md :60-62: the same, in reference form.
- Other documents:
- docs/adr/0034: the amendment for #264 (the chain ends at a cut; what the stopped section names is judged as far as the guard lets).
- docs/milestones/M04.md:71: a stream several tables name belongs to the oldest one's revision.
- PdfDocument.ObjectCount and ObjectNumbers XML docs, if B19-6 takes (a).
- docs/corpus-contributions.md: a real incremental update with a damaged middle cross-reference stream is wanted.
- tests/corpus/manifest.schema.json: every new code and rule id. tests/corpus/manifest.json: pdfmaker7-powerpoint-va-cancer-database-course drops file.size-wrong.
- Test comments:
- The spacers: ChainFiles.cs:25, :269-270, :331-332; CrossReferenceChainTests.cs:245-246, :1019-1021, :1047.
- The hiding device: ReadOrderTests.cs:72-73, :198-199, :284-285, :1061; ReaderLimitsTests.cs:1090; DocumentReaderTests.cs:1905-1906; ObjectRuleTests.cs:3420, :3427; ConvergenceShapes.Records.cs:623; ConvergenceShapes.ObjectStreams.cs:33, :39, :295.
- The reserved types: ChainFiles.cs:125, :180, :197-199, :267, :330; CrossReferenceRuleTests.cs:1169-1170, :1213, and the two test names that say 'is_ignored'.
- ReaderTimingTests' comment (#317).
- docs/status.md at the end of the batch: item 19's actual state. It closes #156, #188, #197, #207, #208, #210, #221, #222, #241, #261, #264, #281 and #317, and records that #171's left edge is paid.
