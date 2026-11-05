######## [0] chain:chain:leave-at-non-section — reviewer major; refuter confirmed major maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:4380 (candidate == leaveAt), with leaveAt computed at :3306 (previousStart = StartOfNamed(previous))
CLAIM: c29761c ends an /XRefStm's search near its offset at the first token where the table's /Prev points, before that place is tried, even when nothing there is a section. If the /Prev names the header of an object that is not a section (a font, a CIDFont), the hybrid file's real stream, which lies farther off but within 512 bytes, is never read. The naming is reported as 'what its /Prev names too' (xref.hybrid-stream-lost, then xref.hybrid-stream-wrong, an Error). The /Prev's search then takes an older table, whose rows serve an older revision of every object the stream updates. Main read the stream. This is a regression the round 1 fix introduced: before c29761c, the search dropped the font (NotFound) and went on to the stream.
EVIDENCE: Probe ZzR2Chain.LeaveAtNonSection (source in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r2chain/ZzR2Chain.cs.txt, outputs out-branch.txt / out-main.txt beside it).

The file:
- T0 at 1426 indexes 1-3 and 7 '(old seven)'.
- Object 4 at 1926 is '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>'.
- Cross-reference stream S (obj 9, /Type /XRef, /Index [5 1 7 1]: 5 '(five only in S)', 7 '(new seven in S)') is at 2441.
- The newest table, at 3177, has /Prev 1926 /XRefStm 1936.

Branch 761ce0b:
- D xref.hybrid-stream-lost @1936: 'The /XRefStm of the cross-reference table at offset 3177 names offset 1936, what its /Prev names too, which the chain looks for through that /Prev: ...'
- D xref.offset-adjusted @1426: 'The cross-reference section /Prev names at offset 1926 was found 500 bytes before it.'
- Sections: /XRefStm 1936 NamedAgain readAs=/Prev@1426; /Prev 1926 Relocated at 1426. S is in no record.
- GetObject(7) = 'old seven' (no rebuild yet). GetObject(5) = 'five only in S' only after a rebuild.
- F xref.hybrid-stream-wrong [Error]: '... names offset 1936, the section its /Prev names too, which the chain read through that /Prev ...'
- The same results with 5 bytes before the header, and with a CIDFont giving /W at 1926.

Main 21a0298:
- D xref.offset-adjusted @2441: 'The cross-reference section /XRefStm names at offset 1936 was found 505 bytes from there.'
- D xref.offset-adjusted @1426 for the /Prev.
- GetObject(7) = 'new seven in S', with no rebuild.
- Findings: section-shifted ×2 and file.size-wrong only.
FIX (reviewer): Stop at leaveAt only where what starts there declares itself a section, one the search would take: the xref keyword (PaddingAt's table flag), or an object whose dictionary gives /Type /XRef, or one that reads. A place that is no section is passed over as the search passes over any other place, so the search goes on to the stream. Alternatively, compute leaveAt only once StartOfNamed(previous) is known to hold such a place. Add the font and CIDFont shapes as tests: object 7 reads '(new seven in S)', the stream is Relocated, and there is no hybrid-stream-lost.
--- VERDICT REASON: The finding holds as stated, and it reaches a little further than the reviewer showed.

**The code.** c29761c computes `previousStart = StartOfNamed(previous)` at PdfFileReader.cs:3306, which is only the first token at the offset the /Prev writes. It passes that value as `leaveAt`. At :4380, `if (candidate == leaveAt) { left = true; break; }` runs before the candidate is tried, so the place is never checked for being a section.

The /Prev does not work that way. At :3225 it keeps its own place only where that place reads, a guard cut it, or it declares itself a section (`DeclaresItselfASection`). Anywhere else it relocates, and its search passes over its own place (`candidate == absolute`).

So `leaveAt`'s premise fails when the /Prev names an object that is no section: a font, a CIDFont giving /W, a string. That premise is the param doc's "where the section another link of the same table reaches starts ... as it would take what it found there". The /XRefStm's search is cut off at a place neither link takes, and the real stream farther off is never tried. It is a regression of the round 1 fix: 071f829, the parent of c29761c, read the stream in every shape I built.

**What the docs promise.** The documentation describes the intended rule, not the code:
- threat-model row 417: "where the search near the offset it names, finding nothing nearer, comes to that section";
- validation-rules `xref.hybrid-stream-wrong`: "as the nearest section to an offset that holds none".

The validator's Error in these files says the /XRefStm names "the section its /Prev names too, which the chain read through that /Prev". That is false: the /Prev read T0 500 bytes away, and the /XRefStm never came to T0.

**Wider than the evidence.** I built a more plausible shape: every offset in the newest trailer is too large by the same amount e (300 or 400 bytes), so the /Prev lands on an update object's header. The branch leaves the /XRefStm to the /Prev. The /Prev's search then takes the stream S itself as the older section. T0, which holds the catalog, is never read, so the index is rebuilt at opening. The validator also gives a false Error saying the stream's objects are lost, though S was read. 071f829 read all three sections with no rebuild. Main failed this shape in another way (rebuild, `file.root-invalid`).

**Severity.** Major: an older revision is served for an object the stream updates, with no rebuild (`old seven`). There is a false Error, a needless rebuild at opening, and a regression against both 071f829 and main 21a0298 for the reviewer's shape. It is not a blocker: there is no crash, both links in these files are already wrong, and a rebuild finds the objects.

**The fix is not a maintainer question.** It makes the code do what [1] (decided without a question) and the docs already say, and it restores 071f829's results.

**One trade-off.** One shape I found is better as the branch has it. An object of L bytes is inserted right before T0 with no offset updated, and S follows T0 closely. There the branch keeps T0 and Told and loses S. The fix, like 071f829, has the /XRefStm read T0 in the stream's place, loses Told's chain, and rebuilds at opening, after which every object is right. Two guessed links stay ambiguous here under every rule, so this is no reason to keep the current stop. It could be noted in the debt filed for the fix, or beside #330.
VERDICT EVIDENCE: **The probe.** My own builder, independent of the reviewer's kit: source in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute-leaveat/ZzRefLeaveAt.cs. Outputs are out-branch.txt, out-071.txt, out-main.txt, out-fix.txt and out-ins-{branch,fix,071}.txt in the same folder. Each version (761ce0b, 071f829, 21a0298) was built in its own worktree.

**(A) The reviewer's shape, rebuilt**
- T0 at 1403 indexes 1, 2 and 7 '(old seven)'.
- Object 4 is a font at 1903, which /Prev names.
- /XRefStm names 1913.
- S (obj 9, /Type /XRef, [5 1 7 1]) is at 2418.
- The newest table is at 3254.

Branch 761ce0b:
- D xref.hybrid-stream-lost @1913 '...names offset 1913, what its /Prev names too, which the chain looks for through that /Prev...'
- D xref.offset-adjusted @1403 'The cross-reference section /Prev names at offset 1903 was found 500 bytes before it.'
- Sections: /XRefStm NamedAgain readAs=/Prev@1403; /Prev Relocated 1403. S is in no record.
- Get(7) = old seven, repaired=False. Get(5) = five only in S only after a rebuild.
- F xref.hybrid-stream-wrong [Error] '...names offset 1913, the section its /Prev names too, which the chain read through that /Prev ... what only that stream indexes is lost.'
- The same results with the /Prev 3 bytes short (white space), with a CIDFont giving /W, and with a plain string object.

071f829 and main 21a0298:
- '/XRefStm names at offset 1913 was found 505 bytes after it' @2418, Relocated Stream.
- The /Prev is relocated to 1403.
- Get(7) = new seven in S, no rebuild.
- Findings: section-shifted ×2 and file.size-wrong (the fixture's /Size) only.

**(B) Constant shift, e=300** (a=1403, b=1551; the /Prev names 1703, the header of object 6; the /XRefStm names 1851)
- Branch:
  - hybrid-stream-lost @1851 'what its /Prev names too'
  - '/Prev names at offset 1703 was found 152 bytes before it' @1551: the /Prev took S
  - D xref.rebuilt at opening, WasRepaired(open)=True
  - F hybrid-stream-wrong [Error] '...what only that stream indexes is lost'
  - e=400 gives the same.
- 071f829: /XRefStm Relocated Stream @1551, /Prev Relocated Table @1403, no rebuild, Get(7) = new seven.
- Main: both links relocated onto 1551, rebuild, F file.root-invalid [Error].

**Prototype of the proposed fix.** At :4380, stop only where `PaddingAt` says xref, or where the object there satisfies `DeclaresOnlyCrossReferenceStream`, read with `TryParseNumberedAt` whose `keep` returns false, so nothing is recorded. The diff is in prototype-fix.diff in the same folder.
- All six A and B shapes then match 071f829: S Relocated, the /Prev at T0, Get(7) = new seven, no rebuild, no hybrid-stream-lost.
- The whole unit suite passes: 7,383 total, 0 failed. So no existing test holds the stop at a place that is no section.

**(C) The trade-off shape:** a 450-byte object inserted before T0, S 94 bytes after T0, Told as T0's /Prev.
- Branch keeps T0 and Told, loses S, Get(7) = old seven, no rebuild.
- The fix and 071f829 read T0 in the stream's place: section-missing (L=450) or the /Prev on S (L=400), a rebuild at opening, then Get(7) = new seven.

The prototype and the probe were removed afterwards. My worktree is clean, and the scratch worktrees are removed.
FIX (refuter): **Where the stop applies.** In TryRelocateXRefSection, stop at `leaveAt` only where the /Prev would keep its own place, as it does at :3225: it reads there, a guard cut it there, or it declares itself a section there. Anywhere else, the /Prev's target is unknown until its own search runs, and the /XRefStm's search goes on as on 071f829.

**How to test the place cheaply, without reading rows into the index.**
- The xref keyword: `PaddingAt(candidate, out table)` gives `table`.
- An object header: its dictionary satisfies `DeclaresOnlyCrossReferenceStream` (/Type /XRef, or a /W with no /Type), read through `TryParseNumberedAt` with a `keep` that returns false, so the reading records and reports nothing.
- A dictionary a guard cut before it gave /Type can count as kept, matching CutByLimit.
- A place that holds none of these is passed over, and the search goes on.

The test can be done once per table, at the call site (:3306), by passing -1 as `leaveAt` when the /Prev's place holds no section. Branch (c)'s `hybridStart == previousStart` test can keep using `previousStart` as it is: that is the decided case of two links naming one place.

**Doc to correct.** Make the `leaveAt` param doc say "where the /Prev names a section", not "where the section ... reaches starts".

**Tests to add.**
- The font, CIDFont-/W and string shapes, and the constant-shift shape. Each asserts:
  - the /XRefStm Relocated onto the stream;
  - the /Prev Relocated onto T0;
  - Get(7) = '(new seven in S)';
  - WasRepaired false;
  - no xref.hybrid-stream-lost and no hybrid-stream-wrong.
- Each of these must fail on 761ce0b.

**Note with the fix.** Record the inserted-object trade-off, shape C, as known, in the debt or beside #330: the /XRefStm, passing over the /Prev's non-section place, can take the table the /Prev's search would find. 071f829 and main behave the same there.

No maintainer question is needed: this implements [1]'s decision ("decided by the section both links reach") and the documented rule.

######## [1] chain:chain:prev-found-moved-xrefstm-reaches-first — reviewer major; refuter confirmed major maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3308 (branch (c) decided by StartOfNamed(previous) only) with :4375 (the /Prev's search passes over the table the /XRefStm read)
CLAIM: Branch (c) by positions ([1]) covers an /XRefStm that reaches the /Prev's section through white space or a search near its own offset. It misses the mirror case: the /Prev reaches the previous table only through a search near its offset (a few bytes off), while the /XRefStm reaches the same table through the white space before its keyword, or exactly, or by a search. The /XRefStm reads that table in the stream's place, with its links unread. The /Prev's search then passes over the table, which is now reached (#188), and takes an older table farther off or none. When it takes an older table, a middle revision is skipped and an older revision of its objects is served silently. When it takes none, the report says 'not there, nor near it' about a table 7 bytes away, adds an Error, and rebuilds the index at opening. Main read every section in both shapes.
EVIDENCE: Probe ZzR2Chain.PrevFoundMovedSkipsMiddle / PrevFoundMovedXRefStmShort (out-branchE2.txt, out-mainE2.txt, out-branchE.txt, out-mainE.txt in the scratchpad r2chain folder).

E2-p7-x1 file:
- Tmid at 163 indexes 3 '(three as the middle revision has it)', /Prev Told.
- Told at 331 indexes 1-3, with 3 '(three as the oldest has it)'.
- T0 at 496 indexes 4, /Prev 163.
- The newest table, at 635, has /Prev 489 (T0-7, the endobj of object 4) and /XRefStm 495 (the end of line before T0).

Branch:
- D xref.hybrid-stream-lost @495: '... names offset 495, a cross-reference table, whose rows were read in the stream's place'
- D xref.offset-adjusted @331: 'The cross-reference section /Prev names at offset 489 was found 158 bytes before it.'
- Sections: /XRefStm Read at 495 (pad 1, T0); /Prev Relocated at 331 (Told). Tmid is never read.
- WasRepaired=False. GetObject(3) = 'three as the oldest has it'.

Main:
- D xref.offset-adjusted @496: '/Prev names at offset 489 was found 7 bytes from there'
- Sections 495 Read, 496 Relocated, 163, 331.
- GetObject(3) = 'three as the middle revision has it'.

E-p7-x1-far (Told 1,200 bytes away, no middle):
- Branch: 'xref.section-missing @1512: The cross-reference section /Prev names at offset 1512 is not there, nor near it' (T0 is at 1519), xref.rebuilt at opening, and F xref.section-not-found [Error] '... nor within 512 bytes of it'.
- Main: offset-adjusted @1519, every section read, no rebuild.
- E-p7-x3-far (/XRefStm relocated 3 bytes onto T0) gives the same branch output.
FIX (reviewer): Decide branch (c) by the section the /Prev reaches, including one its search would find.

Before reading the /XRefStm, find where the /Prev lands. Use StartOfNamed when a section declares itself there. Otherwise, run a search near the /Prev's offset that indexes nothing and gives the place it would take. When that place is where the /XRefStm reaches, leave the /XRefStm to the /Prev, as branch (c) does.

The refuter's alternative for [1] also works: a /Prev whose search comes to a place the chain reached only through this table's /XRefStm, read in the stream's place with its links unread, takes that section and reads its links rather than passing over it.

Add the E2 shape as a test (object 3 reads the middle revision, no rebuild) and the far shape (no section-missing, no section-not-found).
--- VERDICT REASON: The finding holds, and it is slightly wider than claimed. Branch (c) (PdfFileReader.cs:3306-3308) compares where the /XRefStm reaches with StartOfNamed(previous) only. That is the first token at the /Prev's own position, not the section the /Prev's search near that position would take.

When the /Prev is a few bytes off (for example on the endobj 7 bytes before the previous table), the check misses. The /XRefStm reaches that table exactly or through the white space before it, and the else-branch reads it in the stream's place, its links unread. Its start goes into _sectionsReadAt. The /Prev's relocation then passes over that start as "reached" (:4375, #188) and takes a section farther off, or none.

Results:
- With a middle revision, the middle revision is skipped. Object 3 is served from the oldest revision. The only reports are an offset-adjusted Repair and a section-shifted Warning saying "158 bytes before", where the section is really 7 bytes after.
- With no other section within 512 bytes, the branch reports xref.section-missing "is not there, nor near it" about a table 7 bytes away. It rebuilds the index at opening, and the validator gives Error xref.section-not-found "nor within 512 bytes of it".

Main 21a0298 reads every section in all these shapes. The behavior was already there at 071f829, so round 1 missed it: the fix for [1] (c29761c) covered only the /XRefStm's side, through the white space or leaveAt in the /XRefStm's search.

It is wider than the claim on one point. The same happens when the section is a real cross-reference stream: the /XRefStm reads it as the table's stream and the /Prev, 7 bytes short, passes over it. On the branch, no xref.hybrid-stream-lost is reported at all, and the /Prev is reported missing with a rebuild.

It is not covered by #330. That issue is another table's /Prev reaching a stream read through /XRefStm, which is a loop on main too. Here it is the same table's /Prev (B19-5 branch (c)), which main read correctly. No other open debt or decision covers it.

The docs overclaim. lazy-reading.md:117-119 says branch (c) applies to "what the table's own /Prev reaches too, whatever offset each writes: ... or one a few bytes off where the search near it comes to that section". The /Prev's own search is not honored.

Severity is major:
- a wrong object served with no report of the skipped revision;
- a false Error and a rebuild at opening;
- a regression against main;
- decision [1] ("branch (c) decided by the section both links reach (positions)") not implemented for this shape.

It is not a blocker: there is no crash, and the file is already invalid.

The existing test An_xrefstm_falling_short_of_where_its_table_s_prev_names_a_section_found_moved_names_no_section_short (CrossReferenceChainTests.cs:2985) covers only the shape where both links reach the endobj. The mirror has no test.
VERDICT EVIDENCE: I rebuilt the reproducer myself. Probe source: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r2refute-prevmoved/ZzRefPrevMoved.cs.txt. Outputs: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r2refute-prevmoved/out-branch.txt, /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r2refute-prevmoved/out-main.txt and /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r2refute-prevmoved/out-r1.txt.

**Middle shape** (Mid): Tmid at 140 indexes 3 '(three middle)', /Prev Told. Told at 294 indexes 1-3 with 3 '(three oldest)'. T0 at 459 indexes 4, /Prev 140. The newest table at 598 has /Prev 452 (T0-7, the endobj) and /XRefStm 459 (x0) or 458 (x1).

Branch 761ce0b, Mid-p7-x0:
- D xref.hybrid-stream-lost @459 '... names offset 459, a cross-reference table, whose rows were read in the stream's place'
- D xref.offset-adjusted @294 'The cross-reference section /Prev names at offset 452 was found 158 bytes before it.'
- Sections: startxref Read 598; /XRefStm Read 459; /Prev named=452 at=294 Relocated. Tmid is never read.
- GetObject(3) = three oldest. WasRepaired=False.
- Findings: section-shifted 'starts 158 bytes before it, at offset 294', and xref.hybrid-stream-wrong [Error].

Mid-p7-x1 gives the same, plus offset-imprecise.

Main 21a0298, Mid-p7-x0 and Mid-p7-x1:
- offset-adjusted @459 'found 7 bytes from there'
- Sections 598, XRefStm 459, /Prev Relocated 459, 140, 294.
- GetObject(3) = three middle.

**Far shape** (Told 1,200 bytes away, T0 at 1507, /Prev 1500).

Branch, Far-p7-x0, Far-p7-x1 and Far-p7-x3:
- D xref.section-missing @1500 'is not there, nor near it'
- D xref.rebuilt @-1; WasRepaired(open)=True
- F xref.section-not-found [Error] '... nor within 512 bytes of it'

Main: offset-adjusted @1507 'found 7 bytes from there', every section read, WasRepaired=False.

**Stream variant**, wider than the claim. T0 is a /Type /XRef stream object at 1507 with /Prev Told. The newest table has /Prev 1500 and /XRefStm 1507.

Branch, FarStream-p7-x0:
- no xref.hybrid-stream-lost at all
- D xref.section-missing @1500, D xref.rebuilt, F xref.section-not-found [Error]

Main: /Prev Relocated 1507, Told read, no rebuild.

**071f829** gives the branch's output for every p7 shape, so round 1 missed it.

**Controls, correct on the branch:**
- Mid-p0-x0 and p0-x1: branch (c) applies, Tmid read, 'three middle'.
- Far-p0-x7: leaveAt applies.
- Mid/Far with no /XRefStm: /Prev relocated 7 bytes, every section read.

**Docs:** lazy-reading.md:118 says 'whatever offset each writes ... or one a few bytes off where the search near it comes to that section'.
FIX (refuter): Make branch (c) honor the section the /Prev's search takes, as decision [1] already requires. No maintainer question is needed. Of the two implementations, I would take the narrower one.

**Preferred: the refuter's alternative for [1].**
- Carry into the /Prev's iteration the start the same table's /XRefStm read, a table or a stream in the stream's place with its links left unread.
- In TryRelocateXRefSection, a candidate equal to that start is not passed over at :4375. When it is the nearest, it is taken: the /Prev reads it whole, with its links.
- Its rows are the same rows. The index keeps the first, so reading it twice is harmless and bounded to one extra read per table.
- Then turn the /XRefStm's record into the branch (c) naming: NamedAgain, ReadAs set to the /Prev's record, NamesWhatPreviousReads. The validator then words it as branch (c) does, and file.size-wrong and section-shifted do not count T0 twice.
- Defer the reader's xref.hybrid-stream-lost text for a section read in the stream's place until the /Prev is resolved, so it does not say 'whose rows were read in the stream's place' about what the /Prev then reads. Short of that, keep that text, which is still true.
- A real cross-reference stream the /XRefStm read must get the branch (c) report, which today is missing entirely.

**Alternative: the reviewer's fix.** Before the /XRefStm reads, find where the /Prev's search would land. This needs a search mode that indexes nothing, since TryReadXRefSection indexes as it reads. It is heavier, and it shares the relocation-candidate budget.

**Tests to add:**
- Mid-p7-x0 and Mid-p7-x1: Tmid read, GetObject(3) = middle revision, WasRepaired false, no section-shifted '158 bytes before'.
- Far-p7-x0 and Far-p7-x3: no xref.section-missing, no xref.section-not-found, no rebuild.
- FarStream-p7-x0: xref.hybrid-stream-lost reported, Told read.

**Docs:** check lazy-reading.md:117-119 and threat-model row 417 against the result.

######## [2] chain:chain:stray-xref-word-ends-relocation — reviewer major; refuter confirmed major maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:4402 (acceptance: Malformed && DeclaresItselfASection(attempt)), candidates from FindSectionCandidates :4440
CLAIM: 0353e24 takes the nearest Malformed candidate that 'declares itself' a section as the section found moved (R1-Q1). FindSectionCandidates matches 'xref' as a raw word, inside comments, strings and stream data too. Lexed from that word, any such match reads as a table's keyword followed by garbage, so it is Malformed with Kind Table, and it now ends the search. Main, and the branch before 0353e24, dropped it and found the real section.

With an imprecise /Prev, a stray '%xref' comment, '% the xref section follows', or a string '(see xref)' within 512 bytes and nearer than the real table makes the reader report a cross-reference table that cannot be read where a comment is. The chain then ends, the older sections are lost, and the index is rebuilt at opening.

On the /XRefStm path, R1-Q2 then refuses every number, and the validator reports two Errors (section-malformed, hybrid-stream-wrong) for a stream that is 5 bytes off and sound.

A word in a comment or a string is no keyword: it does not declare a section, which is what B19-1, R1-Q1 and lazy-reading.md:159-161 ('an xref keyword') assume.
EVIDENCE: Probe ZzR2Chain.StrayXref / StrayXrefNearHybridStream (out-branchD.txt, out-mainD.txt, out-branchD2.txt).

D-comment file: T0 at 133, a 1,200-byte comment, object 4, '%xref\n', then T1 at 1507 (indexes 4, /Prev 133). The newest table, at 1646, has /Prev 1495, inside object 4's endobj.

Branch:
- 'xref.offset-adjusted @1501: ... /Prev names at offset 1495 was found 6 bytes after it.'
- 'xref.section-unreadable @1501: The cross-reference table /Prev names at offset 1495, found at offset 1501, is there but cannot be read: it holds the keyword xref at offset 1506, where a subsection or the trailer should start. ...'
- xref.rebuilt; WasRepaired=True at opening.
- F xref.section-malformed [Error] @1501.

Other stray shapes on the branch:
- '% the xref section follows': same output, 'it holds the keyword section at offset 1511'.
- '6 0 obj\n(see xref)\nendobj': found at 1513, 'a byte that starts no token at offset 1517'.

Main:
- 'offset-adjusted @1506 ... found 11 bytes from there' (32 and 31 bytes for the other two shapes).
- Every section read, no rebuild, section-shifted only.

D2, /XRefStm 4 bytes short of a hybrid stream with '%xref\n' before it, on the branch:
- 'xref.section-unreadable @1562: The cross-reference table /XRefStm names at offset 1557, found at offset 1562, is there but cannot be read: it holds the keyword obj at offset 1571 ... every other object no section read before it indexes is looked for by rebuilding the index ...'
- 'xref.hybrid-stream-lost ... a cross-reference table, which cannot be read', then xref.rebuilt.
- F section-malformed [Error] and hybrid-stream-wrong [Error].

Without the comment, the branch relocates onto the stream (offset-adjusted @1561) and reads it.
FIX (reviewer): In a search near an offset, count a Malformed table candidate as declaring itself a section only where it is a keyword. Two possible tests:
- 'xref' starts a line, as ISO 32000-1 7.5.4 has it (preceded by an end of line or by the window's first byte), so that '%xref', '% the xref' and '(see xref)' are excluded;
- or the keyword is followed by what a table holds: a subsection header of two non-negative integers, or the trailer keyword.

Any other match is dropped, as an untyped /W object is. Add the comment, words and string shapes as CrossReferenceChainTests rows (the table found 11, 32 and 31 bytes off, no section-unreadable, no rebuild) and the /XRefStm shape (the stream read, no hybrid-stream-lost).
--- VERDICT REASON: The finding holds, and I found one consequence the reviewer did not report: the branch can serve a deleted object. FindSectionCandidates (PdfFileReader.cs:4446) collects any delimited 'xref' bytes as candidates, including ones inside a comment or a string. '%' is a delimiter, so '%xref' passes the boundary test. PaddingAt then lexes from the candidate's own position and sees the keyword, so the attempt is Kind Table. The table read then fails on the next token, so the attempt is Malformed. Commit 0353e24 changed the acceptance at :4402 to `Malformed && (CutByValue || DeclaresItselfASection(attempt))`, and DeclaresItselfASection is simply `Kind == Table || TypedXRef`. So a stray word nearer than the real table now ends the search and is reported as the section found moved. Before 0353e24, the acceptance required `CutByLimit`. Main also required it, so both dropped the stray word and took the real table. This is a regression introduced by the round 1 fix for R1-Q1. It does not implement the decision as decided: R1-Q1, B19-1 and lazy-reading.md:158 speak of 'an xref keyword', and a word inside a comment or a string is not a token a load reads. Beyond the false Error findings and a rebuild at opening, the chain loses every older section. The rebuild then serves objects the chain had freed. It is a major because it serves a wrong object and makes false reports. It is not a blocker: the file still opens, and it needs an imprecise offset to begin with.
VERDICT EVIDENCE: I wrote my own probe, kept at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute-stray/ZzRefStrayXref.cs.txt, and ran it on the branch at 761ce0b and on main at 21a0298.

The file: T0 holds objects 1 to 3 ('three old'). Then a 1,200-byte comment, object 4, object 3 ('three new'), then <between>, then T1 (3 and 4, /Prev T0). Then object 5, and T2, whose /Prev names 2 bytes into object 3's 'endobj'.

Branch, comment shape ('%xref\n' between):
- D xref.offset-adjusted @1532 'found 6 bytes after it'.
- D xref.section-unreadable @1532 'The cross-reference table /Prev names at offset 1526, found at offset 1532, is there but cannot be read: it holds the keyword xref at offset 1537...'.
- D xref.rebuilt; WasRepaired at open=True.
- F xref.section-malformed [Error] @1532, and F xref.section-shifted.

Main, same file: offset-adjusted @1537 'found 11 bytes from there', WasRepaired=False, only xref.section-shifted.

The other shapes, branch against main:
- '% xref': 7 bytes after on the branch, against 12 on main.
- '% the xref section follows': the branch says 'holds the keyword section at offset 1542', against 32 bytes on main.
- '6 0 obj\n(see xref)\nendobj': the branch says 'a byte that starts no token at offset 1548', against 31 bytes on main.
- '% written by x, see xref': 25 bytes after on the branch, against 30 on main.
Each is section-unreadable, then a rebuild, then a section-malformed [Error] on the branch. Main reads every section with no rebuild.

Deleted-object variant: T1 frees object 3 with a free row, and no new object 3 is written.
- Main: GetObject(3) = null.
- Branch with no comment: GetObject(3) = null.
- Branch with '%xref\n': section-unreadable, then rebuilt, and GetObject(3) = 'three old'. The rebuild resurrects a deleted object.

Hybrid file: T1's /XRefStm names 'obj' inside the 'endobj' just before the stream, with '%xref\n' between them.
- Branch: section-unreadable @1557 'it holds the keyword obj at offset 1566 ... every other object no section read before it indexes is looked for by rebuilding the index', then xref.hybrid-stream-lost @1552 'a cross-reference table, which cannot be read', xref.rebuilt and object.redefined. Findings: section-malformed [Error] and hybrid-stream-wrong [Error].
- Main: offset-adjusted @1562 'found 10 bytes from there', and the stream is read with no rebuild.
- Branch with no comment: relocates onto the stream at 4 bytes.

The change: `git show 0353e24` replaces `Malformed && attempt.CutByLimit && (CutByValue || Kind == Table || TypedXRef)` with `Malformed && (CutByValue || DeclaresItselfASection(attempt))`, at PdfFileReader.cs:4402. No test in tests/ builds a stray 'xref' word near a relocation.

A side note, outside the finding's scope: a /Prev that names exactly the 'x' of '%xref' is a B19-1 case at the named offset, from d5b4d48. On the branch it is reported unreadable; main relocated it 5 bytes to the real table.
FIX (refuter): Count a Malformed table candidate as declaring itself a section only where its 'xref' is a keyword a load would lex. Any other Malformed table candidate is dropped, with ForgetReadThrough, as main did, and the search goes on. A candidate that reads is still taken wherever it lies. Two ways to test it:

(a) Cheapest: the keyword starts its line. Only white space may stand between it and the end of line before it, or the window's start; ISO 32000-1 7.5.4 has a section 'begin with a line containing the keyword xref'. FindSectionCandidates already reads the byte before the window, so it can record this per candidate, for example as a parallel flag or a set the acceptance consults. That avoids re-reading.

(b) Exact: lex the candidate's line from its start with PdfLexer, as #261's forward confirmation does. Require that the token starting at the candidate is the keyword. This excludes '%xref', '% the xref' and '(see xref)'. A string opened on an earlier line stays the known #171 limit.

Either way, update the DeclaresItselfASection remarks and lazy-reading.md:158-162 to say 'an xref keyword starting its line', or 'lexed as a token'.

Tests in CrossReferenceChainTests, one row per shape (comment, words, one-line string):
- the table is found at 11, 32 and 31 bytes;
- no xref.section-unreadable, no rebuild;
- the deleted-object variant keeps GetObject(3) null.
Add the /XRefStm hybrid shape: the stream is read and there is no hybrid-stream-lost. In CrossReferenceRuleTests, assert no section-malformed and no hybrid-stream-wrong.

Optionally, apply the same test at the named offset (B19-1), so that a /Prev landing exactly on a comment's 'xref' is not called a section. That widens a settled decision, so it is for the maintainer to choose. The main fix is not a design choice: it implements R1-Q1 as written.

######## [3] chain:chain:white-space-crossed-twice — reviewer minor; refuter confirmed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3517 (ReachedAt -> StartOf) and :4519 (TryReadXRefSection -> PaddingAt); :3306 StartOfNamed adds a third crossing of the /Prev's white space
CLAIM: For every naming the chain has not reached, ReachedAt crosses the white space and comments at the offset with the same probe, grown up to MaxTrailerLength, that TryReadXRefSection then runs again at the same position. A hybrid table's /Prev is crossed a third time, by StartOfNamed. R1a's open point put this at 32 bytes for a sound section. For a naming in white space, it is a full second probe of 32+256+2,048+16,384+65,536 bytes. Hostile files that point their /XRefStm into one long run of white space now read 41% more than on main. The cost is bounded by MaxXRefSectionCount, but it is avoidable.
EVIDENCE: Probe ZzR2Chain.WhiteSpaceNamings (out-branchC.txt, out-mainC.txt). The file is 1,024 tables (the default MaxXRefSectionCount), each with an /XRefStm naming its own position, 20 bytes apart, in a 100,000-byte run of spaces that ends in a plain object. It is 257,286 bytes.
- Branch: bytesRead=296,120,256, times 386/277/177 ms, 2,866,000 B allocated.
- Main: bytesRead=209,779,826, times 231/160/163 ms, 2,477,208 B allocated.
- The difference, 86,340,430 bytes, is exactly 1,024 × 84,317: one more full probe per naming.
- The same figures with a 100 KB comment in place of the spaces.
FIX (reviewer): Hand the start that ReachedAt found (its out start) to TryReadXRefSection, which then sets Padding from it rather than running PaddingAt again. Keep previousStart for the next iteration's ReachedAt(previous). Each naming then crosses its white space once. Add the shape to HostileInputTests, bounding bytes read against the same file read on main (or against one probe per naming).
--- VERDICT REASON: The finding holds as claimed, and the cost is wider than the /XRefStm shape it uses. ReachedAt (PdfFileReader.cs:3517) calls StartOf, which calls PaddingAt, for every offset in the file that is not already in `named` or `_sectionsReadAt`. TryReadXRefSection (:4519) then runs PaddingAt again at that same position, for a /Prev and for an /XRefStm stream. For a hybrid table, StartOfNamed (:3306) probes the /Prev's position as well, so that position is crossed three times. On main 21a0298, ReachedBefore did not probe, so each position was crossed once.

R1a's open point noted the double read but put it at "32 bytes for a sound section" and did not file it. Round 1's refuter for [21] priced it as "one small extra window". For a naming in white space it is in fact a full second grown probe: 32+256+2,048+16,384+65,536 = 84,256 bytes. This is the cost the brief classes as minor: bounded (by MaxXRefSectionCount times the trailer window) but avoidable. It is not settled in decisions.md or filed under #320 to #330. The behavior and the diagnostics are unchanged; only the reads grow.

One documentation line is now wrong. The known-limitation entry [#47] in docs/threat-model.md:456 still says the probe reads "about nine times the run it crosses, once per section". It is now twice per section, and three times for a hybrid table's /Prev.

The fix is mechanical and does not change behavior. A throwaway two-entry memo of PaddingAt brought every byte count back to main's, with the same diagnostics, and the whole unit suite passed: 7,383 tests, 0 failed, 20 skipped. No maintainer decision is needed, unless the orchestrator would rather file it as debt under M23 beside #47.
VERDICT EVIDENCE: This is my own probe (ZzRefWs, written independently of the reviewer's and now deleted), run on 761ce0b and on a fresh 21a0298 worktree. Each shape was opened three times through a counting PdfFileSource, and all three runs gave the same figures.

1. **The finding's shape**: 1,024 classic tables chained by /Prev, each /XRefStm naming its own place 20 bytes apart in a 100,000-byte run of spaces that ends in `99 0 obj`. The file is 257,275 bytes.
   - Branch: bytesRead=296,120,256, 16,450 reads, 243-290 ms.
   - Main: bytesRead=209,779,826, 9,283 reads, 162-215 ms.
   - That is +41.2%: per naming, 84,317 more bytes, one more full 84,256-byte probe plus two 32-byte probes. The reviewer's "exactly 1,024 × 84,317" is 178 bytes off; the per-naming reading is right.
   - The diagnostics are the same on both: limit.trailer ×500, xref.section-missing ×500.
   - With a 100 KB comment in place of the spaces, the bytes read are the same.

2. **Wider than claimed: /Prev into padding.** 1,000 tables, each /Prev naming the start of 60,000 spaces before the older table's `xref`. The file is 60,142,724 bytes and neither build gives a diagnostic.
   - Branch: 233,830,543 bytes, 10,995 reads, 155-171 ms.
   - Main: 149,658,767 bytes, 5,999 reads, 102-115 ms.
   - That is +56%: one more 84,256-byte crossing per section. The same shape with 100 tables gives 23,187,341 against 14,845,965.

3. **A sound hybrid file of 200 revisions**: 400 sections.
   - Branch: 11,525,056 bytes, 1,602 reads. Main: 11,505,888 bytes, 1,003 reads.
   - The difference is exactly 599 × 32 bytes: 400 ReachedAt probes plus 199 StartOfNamed probes. This confirms the third crossing of a hybrid table's /Prev.
   - With 3 revisions: 6,098 against 5,842 bytes, 26 against 18 reads.

4. **The double probe is avoidable.** I added a throwaway two-entry memo in front of PaddingAt, keyed by position (the source's bytes do not change during a read).
   - Shape 1: 209,777,088 bytes. Shape 2: 149,658,767, the same as main. Sound 200: 11,505,888, the same as main.
   - The diagnostics are identical.
   - Whole unit suite: total 7,383, failed 0, succeeded 7,363, skipped 20.
   - The patch was reverted.

**Code**: :3517 `start = StartOf(position);`, :3526 `StartOf => position + PaddingAt(position, out _)`, :4519 `section.Padding = PaddingAt(absolute, out var table);`, :3306 `StartOfNamed(previous)`.

**History**: r1-fix-result.json, R1a open_points: "Note, not filed: ReachedAt's probe ... and TryReadXRefSection's own probe read the same first window twice for every section. That is 32 bytes for a sound section". docs/threat-model.md:456 still reads "once per section".
FIX (refuter): Cross each position's white space once.

1. **The code.** Either of two fixes works; the first is what I would do.
   - **Memo PaddingAt.** Keep its last one or two results (position, padding, `table`) in fields of the reader, and return them for a repeated position. ReachedAt, the hybrid stream's TryReadXRefSection, StartOfNamed and the next iteration's ReachedAt and TryReadXRefSection then share the same probe. It is deterministic, because the source's bytes and the trailer bound are fixed for a read.
   - **Carry the probe's result explicitly.** ReachedAt's `start` alone is not enough: TryReadXRefSection also needs the `table` flag that PaddingAt gives and StartOf discards. So carry both the padding and the flag:
     - hand ReachedAt's result for `offset` into TryReadXRefSection(section.Offset, …), and `hybridStart` into the stream's TryReadXRefSection;
     - keep `previousStart` for the next iteration's ReachedAt(previous), which must still test `_sectionsReadAt.Contains(start)` afresh.

2. **A HostileInputTests cost test.** Use the /Prev-into-padding shape: 1,000 tables, each /Prev naming 60,000 spaces before the older table. Compare it with the same file whose /Prev names the keyword. Bound the difference in bytes read by tables × (32+256+2,048+16,384+65,536) plus the table windows' extra, which measured 15,277 bytes per section. It now reads about 2 × 84,256 more per section than the keyword file. Add the hybrid white-space shape the same way. Check that the test fails at 761ce0b.

3. **The docs.** If the fix is made, docs/threat-model.md:456 ("once per section") stands, and ReachedAt's remarks (:3486-3489) should say the window it reads is the one the section's own read then uses. If the cost is filed as debt instead (under M23 beside #47), correct :456 to "twice per section, three times for a hybrid table's /Prev" and name the debt.

######## [4] chain:chain:loop-placement-and-wording-through-white-space — reviewer minor; refuter confirmed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3507-3510 (named.Contains returns the written position) and :3138-3140 (comment); docs/threat-model.md:414; tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:3411-3412
CLAIM: Where a looping /Prev writes again exactly an offset in the white space before a section that the chain named earlier, ReachedAt returns the written position. xref.chain-cycle is then placed in the white space. One byte off, the loop is placed where the section starts. Threat-model row 414 ('…or the white space and comments before it, ends the chain at once, placed where the section starts') and the code comment at :3138 say it is always placed at the section. Separately, a loop through the white space before a section reached and not read says 'names offset X, which the chain has already named' for an X the chain never named. The passing test at :3411 asserts this, with ownShortBy 1.
EVIDENCE: Probe ZzR2Chain.LoopRepeat (out-branch.txt). The file is T2 -> /Prev 315 (3 spaces before T1 at 318) -> T1 -> T0, with T0's /Prev set as below.
- T0's /Prev = 315: 'xref.chain-cycle @315: ... the /Prev of the section at offset 133 names offset 315, which the chain has already read.' LoopOffset=315.
- T0's /Prev = 316: '@318', LoopOffset=318.
- T0's /Prev = 318: '@318'.

CrossReferenceChainTests.A_lost_stream_named_again_through_the_end_of_line_before_its_header_is_handed_on_once_and_ends_the_chain_in_a_loop(0, 1) passes. It asserts '... names offset {stream - 1}, which the chain has already named', and only {stream} was named.
FIX (reviewer): In ReachedAt, when named.Contains(offset) and the position is in the file, return StartOf(position) where that start is in _sectionsReadAt, so that the loop is placed where the section starts, whichever offset repeats. Word the not-read case by the section, not by the offset, for example 'which leads to a section the chain has already reached'. The same applies to the 'already named' text in ChainLoopRule. Add a row with the exact repeat (315/315) asserting the position 318.
--- VERDICT REASON: The finding holds as stated, and has one more effect the reviewer did not name.

1. Placement. ReachedAt (PdfFileReader.cs:3507-3510) returns PositionOf(offset) as soon as named.Contains(offset). A /Prev that repeats exactly a written offset in the white space before a section therefore places xref.chain-cycle in that white space, and sets LoopOffset there. Any other offset into the same white space goes through StartOf and is placed where the section starts.
- The call-site comment at :3138-3140 ("where it starts, when the offset names the white space before it") misstates this. So does threat-model row 414 ("an offset naming one of them, or the white space and comments before it, ends the chain at once, placed where the section starts").
- Both texts, and ReachedAt in its current form, came with the round 1 fix c29761c (the comment) and 761ce0b (the row).
- The exact-repeat placement itself is older: main 21a0298 gives the same @315 for the B-3-3 shape. The inconsistency is new, because on main the off-by-one offsets read the section again.

2. Further effect. OffsetImpreciseRule counts the loop's naming only when LoopOffset > PositionOf(LoopWrittenOffset). An exact repeat in the white space is therefore not counted as a naming that falls short, while one byte off is: "An offset names the white space…" against "2 offsets name the white space…". The rule's own comment says a /Prev looping back through the white space before a section "names it short too".

3. Wording. When the loop reaches a lost stream through its start, never through an offset the chain wrote, the reader message and ChainLoopRule both say "names offset X, which the chain has already named" for an X the chain never named. This happens in both rows of A_lost_stream_named_again_through_the_end_of_line_before_its_header_…(1,0) and (0,1), not only in (0,1), and both rows assert that text. In round 1 the same shapes were wrong in another way: the stream was read twice, and the offset in the message had been named. The misleading text is new with c29761c.

Severity is minor. Objects are served correctly, and every report is still made once. What is wrong is the position, LoopOffset, the imprecise count and the message text. The fix carries out round 1's decision ([0][9][21]: reached is judged by the first token's start) and the comment's stated intent, so no design question for the maintainer.
VERDICT EVIDENCE: My own probe, built on origin/ccr-6d6c0ee6-dqm4lm-188 at 761ce0b. Source: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refloop/ZzRefLoopPlace.cs.txt. Output: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refloop/out-branch.txt and out-s.txt.

(B) T2 /Prev 315 (3 spaces before T1 at 318) -> T1 -> T0, with T0's /Prev set as below:
- /Prev 315: "D xref.chain-cycle @315: ... the /Prev of the section at offset 133 names offset 315, which the chain has already read." Loop=315, written=315.
- /Prev 316: @318, Loop=318.
- /Prev 317: @318, Loop=318.
- /Prev 318: @318, Loop=318.
- With T2 /Prev 316 and T0 /Prev 316: @316.
- Main's earlier output for the 315 shape (r2chain/out-main.txt) is also @315.

(Lost stream at 1650, LostStreamFiles.ThreeRevisions; namedShortBy and own /Prev short-by as below):
- (1,1): cycle @1649, Loop=1649, "names offset 1649, which the chain has already named".
- (1,0): @1650, "the /Prev of the section at offset 1649 names offset 1650, which the chain has already named". Only 1649 was named.
- (0,1): @1650, "names offset 1649, which the chain has already named". Only 1650 was named.
- The validator says the same: "F xref.chain-loop … names offset 1649, which the chain has already named: the chain loops."
- The readable stream gives the same placement: (1,1) @1649 against (1,0) and (0,1) @1650.

(NearbyFiles.StreamNamedShortTwice: startxref 110, stream header at 112):
- /Prev 111: cycle @112, Loop=112, "F xref.offset-imprecise … 2 offsets name the white space …".
- /Prev 110 (the exact repeat): cycle @110, Loop=110, "An offset names the white space … startxref gives offset 110, 2 bytes before the header". The /Prev's own short naming is not counted.
- /Prev 112: @112, one offset.

The existing test A_lost_stream_named_again_through_the_end_of_line_before_its_header_is_handed_on_once_and_ends_the_chain_in_a_loop(0,1)/(1,0) passes, and asserts "which the chain has already named" for an offset that was not named.
FIX (refuter): 1. Placement. Fix it at the loop site (PdfFileReader.cs:~3146), not inside ReachedAt. The /XRefStm lookup and the relocation also call ReachedAt, and an extra probe for each exact /XRefStm repeat would add reads to the thousand-namings hostile case.
- When reached == PositionOf(offset), named.Contains(offset) and the position is in the file, place the loop at StartOf(position) when that start is in _sectionsReadAt.
- That is one probe, and the loop ends the chain. The alternative needs no read at all: take the start of the record that sectionsAt holds at that position, Offset + Padding, restricted to records whose Offset equals the position, so that a section found moved keeps its placement.
- LoopOffset then equals the section's start. OffsetImpreciseRule counts the repeat as a naming that falls short, as it already does one byte off.

2. Wording. When the loop is not a read and the offset itself was never named (LoopOffset != PositionOf(LoopWrittenOffset), or a flag set in FileStructure), word it by the section instead, e.g. "which leads to a section the chain has already reached and could not read". Make the same change in ChainLoopRule's "which the chain has already named" branch.

3. Tests.
- Add the (1,1) row to the lost-stream theory, asserting Position and LoopOffset == stream.
- Add a readable-table or B-3-3 shape with the exact repeat, asserting the position is the section start.
- Add a StreamNamedShortTwice variant whose /Prev repeats startxref's offset, asserting Position == stream and "2 offsets".
- Change the expected text of the (1,0) and (0,1) rows.

If the maintainer prefers to keep the code, the alternative is to amend the comment at :3138-3140, threat-model row 414 and reference/diagnostics.md's chain-cycle row to say that a repeat of an offset as written is placed where it was written. The recommended route is the code fix, which matches round 1's decision.

######## [5] chain:chain:imprecise-offset-counted-twice — reviewer nit; refuter confirmed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/Validation/Rules/OffsetImpreciseRule.cs:19-22 (remark) and the NamedAgain arm
CLAIM: The rule's remark says a section counts once for each offset, as written, that names it short. Two /Prev links writing the same white-space offset count once (B-3-3 below). One written offset that a newer table's /XRefStm names (left to its /Prev, branch (c)) and the older table's own /XRefStm names again counts twice, because both NamedAgain records carry the padding.
EVIDENCE: Probe F1-same (out-branchF.txt). T2 has /Prev 301 /XRefStm 299; T1 at 301 has /XRefStm 299; 2 spaces lie before T1's keyword. The finding is 'xref.offset-imprecise: 2 offsets name the white space before what they designate rather than its first byte; the first: /XRefStm gives offset 299, 2 bytes before the xref keyword.' F1-other (T1's /XRefStm 300) also gives 2. B-3-3, where two /Prev links both write 315, gives 'An offset names …', a count of one.
FIX (reviewer): Count a NamedAgain record only when no earlier record with the same WrittenOffset was counted (a set of written offsets in the rule), or narrow the remark to 'each naming'.
--- VERDICT REASON: The finding holds. It is a little wider than the reviewer said, and the wider part is why I raised it from nit to minor.

Cause: when a table's /XRefStm names the white space before the section its /Prev names, at an offset other than the /Prev's, the reader hands it to the /Prev (LeaveToPrevious with shortBy > 0, PdfFileReader.cs:3315-3316). That written offset is never added to `visited`. A later naming of the same written offset then misses ReachedAt's `named.Contains(offset)` test. ReachedAt instead returns the section's first token, so the later naming counts as short again:
- a later /XRefStm becomes a NamedAgain record with padding;
- a later /Prev becomes a loop with LoopOffset > named.

A written offset first met through a /Prev is in `visited`. Its later copies get padding 0, or a loop at the named position, and count nowhere.

So whether one written offset counts once or twice depends on which naming the chain met first. Three texts say otherwise:
- the rule's remark (lines 19-22: "counts once for each offset, as written … so that what is reported does not depend on which naming the chain met first");
- validation-rules.md:67 ("in whichever order the chain met the namings");
- the comment of the test A_prev_and_an_xrefstm_writing_one_offset_short_of_the_previous_section_count_once_among_the_imprecise_offsets ("counted once, as an offset a table copies from another is").

The new path comes from round 1's fix c29761c. Before it, at 071f829, the rule counted only Read records (`git diff 071f829..761ce0b` on OffsetImpreciseRule.cs).

Why minor and not major: the finding, its location and its first example are the same in every shape. Only the count in one Warning's message differs. The shapes already carry Error findings (hybrid-stream-wrong, chain-loop). Objects are served right in all of them. Not a design choice for the maintainer: the remark, the site and the test comment all state the intended rule.
VERDICT EVIDENCE: Probe built independently on 761ce0b. Source: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r2refute-imprecise/ZzImpreciseTwice.cs.txt. Outputs: out-branch.txt and out-mirror.txt in the same folder.

The files are T0 <- T1 <- T2. T1's xref keyword follows 3 spaces and the endobj line feed.

Shapes that count one written short offset twice:
- **F1-same** (T2 /Prev 301 /XRefStm 299; T1 /XRefStm 299). Records: `/XRefStm written=299 pad=2 NamedAgain readAs=/Prev@301`, twice. Output: "xref.offset-imprecise @offset 299: 2 offsets name the white space before what they designate rather than its first byte; the first: /XRefStm gives offset 299, 2 bytes before the xref keyword."
- **K** (T2 /Prev 318 /XRefStm 316; T0 /Prev 316). Loop=318 written=316. Output: "2 offsets name … the first: /XRefStm gives offset 316, 2 bytes before the xref keyword."

The same namings met in the other order count once:
- **K-mirror** (T2 /Prev 336; T0 /Prev 338 /XRefStm 336). The /XRefStm gets `written=336 pad=0 NamedAgain`, and the loop lies at its own written offset (Loop=338 written=338). Output: "An offset names the white space … /Prev gives offset 336, 2 bytes before the xref keyword."
- **G** (T2 /Prev 299; T1 /XRefStm 299), with the /XRefStm at pad=0: "An offset names …".
- **G2** (T2 /Prev 299 /XRefStm 299; T1 /XRefStm 299): "An offset names …".
- **B-3-3** (two /Prev 315; Loop=315 written=315): "An offset names … /Prev gives offset 315, 3 bytes before the xref keyword."

Two different written offsets count twice, as expected:
- F1-other (299 and 300): "2 offsets …".
- B-3-2 (315 and 316): "2 offsets …".

Behind both of these, one written offset is counted twice depending on whether its first naming was an /XRefStm left to the /Prev or a /Prev:
- F1-same (2) against G (1);
- K (2) against K-mirror (1).
FIX (refuter): Make the rule count each written offset once, as its remark already says. In OffsetImpreciseRule.Check, keep a small set of the WrittenOffset values already counted:
- a Read record (Padding > 0) and a NamedAgain record whose arm matches are counted only when Add(section.WrittenOffset) succeeds;
- the loop block is counted only when Add(structure.LoopWrittenOffset) succeeds.

The validator is not a hot path, so the HashSet is fine.

Add a theory over F1-same, K and K-mirror, all expecting "An offset names …". F1-other and B-3-2 keep expecting "2 offsets …".

Do not fix this in the reader by adding the /XRefStm's written offset to `visited` in the branch at PdfFileReader.cs:3315. That would move K's loop to the named offset (316) and change its chain-cycle wording.

The alternative is to keep the code and reword the remark and validation-rules.md:67 to "each naming". That is weaker: it would still contradict G, B-3-3 and K-mirror, and the promise that the order does not matter.

######## [6] chain:chain:branch-c-text-under-section-count-guard — reviewer nit; refuter confirmed nit maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3584 (LeaveToPrevious text) and src/AdCodicem.Pdf/Validation/Rules/HybridStreamWrongRule.cs:42
CLAIM: When MaxXRefSectionCount stops the chain right after a table whose /XRefStm and /Prev name the same offset, the reader has already said 'which the chain looks for through that /Prev'. The record keeps NamesWhatPreviousReads=true with ReadAs null, so the validator's Error repeats that text, though the chain looked for nothing there. The guard's xref.checked-in-part beside it says the older sections were not checked.
EVIDENCE: Probe F2-count-guard (out-branchF.txt): PrevFoundMovedXRefStmShort(0,0,0) read with MaxXRefSectionCount = 1.
- D xref.hybrid-stream-lost @317 '... names offset 317, what its /Prev names too, which the chain looks for through that /Prev ...'
- D limit.xref-section-count @317
- F xref.hybrid-stream-wrong [Error] '... names offset 317, what its /Prev names too, which the chain looks for through that /Prev, where ISO 32000-1 ...'
- F xref.checked-in-part '... the older sections were not checked.'
FIX (reviewer): In HybridStreamWrongRule, when NamesWhatPreviousReads is true and ReadAs is null, say 'what its /Prev names too, past the guard that ended the chain' (or 'which the chain did not read'). Alternatively, clear NamesWhatPreviousReads where the section count guard ends the chain before the /Prev's record is made.
--- VERDICT REASON: The finding holds on 761ce0b, on a file I built myself. In the newest table, /Prev and /XRefStm name the same offset, or /XRefStm names the white space just before that section. When MaxXRefSectionCount stops the chain right at that /Prev, three things happen. LeaveToPrevious (PdfFileReader.cs:3579) has already set NamesWhatPreviousReads = true. The count-guard break (:3166-3195) comes before `namedWithPrevious.ReadAs = section` (:3200-3206), so ReadAs stays null. HybridStreamWrongRule.cs:42 rewords only when ReadAs is set, so the Error repeats the reader's "which the chain looks for through that /Prev". The chain never looked there: xref.checked-in-part, at the same offset, says the older sections were not checked.

I checked whether this is wider than the count guard. It is not. The relocation "left" path cannot leave a reached section to the /Prev, because TryRelocateXRefSection skips _sectionsReadAt candidates before its leaveAt test (:4375). So the loop break cannot leave ReadAs null with the flag set. The count guard is the only path.

Round 1's [24] fix (R1b deviation) set the flag only "when the offset is in the file and no guard stopped the table". It did not consider the count guard ending the chain at the /Prev's own section. The text is not new in round 1: at 071f829 the same case said the still less accurate "the section its /Prev names too, which the chain reads through that /Prev". This is not a regression, but round 1's fix missed this case.

Three documents also state otherwise:
- NamesWhatPreviousReads' XML doc (XRefSectionRecord.cs:222-229) says the flag is false "where a guard ... kept the /Prev from being read". Here it is true.
- validation-rules.md:60 lists what the finding says ("the section it read or what holds no section the chain could read, or that the offset lies outside the file, or, past a limit that stopped the table, that the limit hides it"). This case is none of those.
- The rule's remarks say the text is said "as the chain found it once it read on".

The classification (Error) is right, as decided in B19-5 and #264, and the guard's own finding sits beside it. The path needs a lowered limit, or a 1,024-section chain whose last table has this shape. Only the words are wrong, so the severity stays nit. The reader's diagnostic was true when it was emitted, and the limit report follows it at the same position.
VERDICT EVIDENCE: I rebuilt it with my own uncommitted probe on origin/ccr-6d6c0ee6-dqm4lm-188 (761ce0b), Release build. The probe has since been removed. The file is: oldest table (objects 1-3) at 133; middle table (object 4, /Prev 133) at 302; newest table (object 5) at 441 with `/Prev 302 /XRefStm 302`.

MaxXRefSectionCount = 1:
D xref.hybrid-stream-lost [Warning] @302: The /XRefStm of the cross-reference table at offset 441 names offset 302, what its /Prev names too, which the chain looks for through that /Prev: ISO 32000-1 has it name ...
D limit.xref-section-count [Warning] @302: The cross-reference chain has more than 1 section; the older ones were not read. Raise PdfReaderLimits.MaxXRefSectionCount to read past it.
S /XRefStm named=302 from=441 NamedAgain Unknown readAs=null prevReads=True nostream=what its /Prev names too, which the chain looks for through that /Prev
F xref.hybrid-stream-wrong [Error] @offset 441: ... names offset 302, what its /Prev names too, which the chain looks for through that /Prev, where ISO 32000-1 names ...
F xref.checked-in-part [Information] @offset 302: The cross-reference chain goes on at offset 302, past the sections PdfReaderLimits.MaxXRefSectionCount lets the reader read: the older sections were not checked.

With /XRefStm 300 (2 bytes of white space before the middle table) and count 1, the result is the same: readAs=null, prevReads=True, and the Error says "names offset 300, what its /Prev names too, which the chain looks for through that /Prev". The limit report and checked-in-part are at 302.

Controls:
- Default limits: readAs=/Prev@302:Read, and the Error says "the section its /Prev names too, which the chain read through that /Prev".
- Count = 2, where the guard cuts at 133, one section later: readAs is set and the Error says "which the chain read through that /Prev".

So only a guard that ends the chain exactly at the /Prev's section leaves the reader's text in the finding. No test covers count guard + same offset. The ReaderLimitsTests cases at :2650-2680 use the table-rows and table-index guards, where pastGuard is true.
FIX (refuter): Either fix below works, and neither is a maintainer decision. The reader-side fix is preferred because it keeps the invariant "NamesWhatPreviousReads implies ReadAs is set once the chain is read". The rule-side fix is the smaller change.

Reader side: in TryReadXRefChain's count-guard branch (PdfFileReader.cs:3166-3195), check whether namedWithPrevious is non-null before the break. If it is, set NamesWhatPreviousReads = false and NamesNoStream to a guard text, e.g. "what its /Prev names too, past the guard that ended the chain there". The finding then says the limit hides it, as the table-stopping guards' text does. The reader's diagnostic is left as it is: it was true when said, and the limit report follows it at the same offset.

Rule side: in HybridStreamWrongRule, add a branch for `{ NamesWhatPreviousReads: true, ReadAs: null }` that writes that same guard text.

With either fix:
- Update NamesWhatPreviousReads' XML doc (XRefSectionRecord.cs:222-229) to name the guard that ends the chain.
- Update the rule's remarks ("once it read on") and validation-rules.md:60 so they list this case with the limit-hides-it wording.
- Add a test: same-offset file and white-space-before file, each read with MaxXRefSectionCount = 1, asserting the full xref.hybrid-stream-wrong message beside xref.checked-in-part. The test must fail on 761ce0b.

######## [7] sections:read-in-part-says-kept-after-newer-refusal — reviewer minor; refuter narrowed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:4149-4176 (ReportSectionReadInPart: stream tails :4155-4157, table tails :4166-4169, refused-row tail :4176); :4040-4042 (ReportLostSection table branch); src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:61; docs/website/docs/reference/diagnostics.md:112 ('The rows read are kept'); tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:2305
CLAIM: This is R1c's open point, and it holds. A newer section can refuse a number to the older sections' rows: every number (a lost stream whose /Index cannot be read), or a range (one whose /Index declares it). When it does, AddFromChain drops an older section's row for that number before indexing it. A section read only in part after that refusal is still reported as having kept its rows. Each report tail makes a false claim in this case: table read in part 'the rows read were kept'; stream read in part 'the rows it holds were kept' / 'the row it holds was kept' (and this tail never switches to the every-number tail the table branch got); refused row 'that row alone was refused, the rows after it read'; /XRefStm table that cannot be read 'The rows read before the fault, if any, were kept'. The index shows none of those rows stands. The test written for [32] pins the 'kept' text, while its own comment (:2287) says 'None of its rows stands'. Behavior is right: every object is served through a rebuild. The texts are new in this batch: git grep finds none of them on 21a0298, and they were added by 54d56c6 and 0fc7ce5.
EVIDENCE: Shape: newest table (startxref) -> /Prev lost stream 6 `<< /Type /XRef /Size 9 /Index /X /W [1 -4 2] /Prev T1 /Length 0 >>` -> T1 -> T0. T0 indexes 1-5 (old three, old four, old five). T1 gives new three and new four. Outputs:
(a) T1 = `3 3 / new3 / new4 / junk junk junk / trailer`: `xref.section-unreadable @405: ... was read only in part: the row for object 5, at offset 454, is not an offset, a generation and n or f; the rows read were kept, and every other object no section read before it indexes is looked for ...`; index `idx[3]=no idx[4]=no`.
(b) T1 = stream /Index [3 3] holding 2 of its rows: `... it holds 2 rows where its /Index and /Size declare 3; the rows it holds were kept, and the objects whose rows it lacks are looked for ...`; `idx[3]=no idx[4]=no`. With 1 row: `the row it holds was kept`, idx[3]=no.
(c) T1 = `3 2 / new3 with generation 65536 / new4 / trailer`: `... gives it generation 65536, outside 0 to 65535; that row alone was refused, the rows after it read, ...`; `idx[4]=no`. The same text appears when the lost stream's /Index is [3 2]: `idx[3]=no idx[4]=no idx[5]=yes`.
(d) Lost stream /Index [3 2] over (a): `the rows read were kept, and the objects the rest of that subsection numbers ...`; `idx[3]=no idx[4]=no`.
(e) Hybrid H (/XRefStm X, /Prev T0) under the /X stream, X = `6 2 / new6 / junk junk junk / more`: `The cross-reference table /XRefStm names at offset 797 is there but cannot be read: the row for object 7 ... The rows read before the fault, if any, were kept; every other object ...`; `idx[6]=no`. With X ending in `trailer` (read in part): `the rows read were kept`; `idx[6]=no`.
In every case the objects read `new three` / `new four` / `new six` through `repairedAfter=True`.
FIX (reviewer): Decide each tail by what newer sections refused before the section's own refusal. In the /XRefStm lost path, sample the flag before RefuseRowsLost(stream); everywhere else, RefuseRowsLost already runs after the report.
- When `_everyNumberRefused` held before the section was read: say no row of it stands, every number having been refused to the older sections' rows by a newer section, and use the every-number tail. Apply this to the stream branch and to the refused-row branch too.
- When only ranges were refused (`_refusedRangeCount > 0` or `_refusedNumbers`): say 'the rows read that no newer section refused were kept' (or the equivalent for a stream), and do not say 'that row alone was refused'.
Correct the assertion at CrossReferenceChainTests.cs:2305 to agree with its comment, and add rows for the stream and refused-row tails under /X and under a range. Align PdfDiagnosticCodes.cs:61 and diagnostics.md:112.
--- VERDICT REASON: The finding holds at 761ce0b. AddFromChain (PdfFileReader.cs:6616-6621) drops every row of an older section whose number a newer section refused. That covers every number after a lost stream whose /Index cannot be read, and a range when its /Index declares one. Even so, each report tail on that older section still says its rows were kept:
- ReportSectionReadInPart's table tail (:4160-4169), stream tail (:4152-4158) and refused-row tail (:4171-4176).
- ReportLostSection's table branch (:4039-4043).
The stream tail never checks _everyNumberRefused.

The index shows none of those rows stands. Behavior is right: every object is served through a rebuild. The [32] test pins the false text (CrossReferenceChainTests.cs:2305), while its own comment (:2287) says "None of its rows stands". PdfDiagnosticCodes.cs:61 ("The rows read were kept") and diagnostics.md:112 ("The rows read are kept") state the same thing without exception.

The finding is wider in one place. ReportLostSection's !NumbersRefused branch, a /Prev-named table that cannot be read under the lost stream, also says "The rows read before the fault, if any, were kept" while none stands.

It is narrower in one place. That last sentence is on main 21a0298 (:3199), so the claim that git grep finds none of the texts there is wrong for it. Main has no RefuseDeclared and no hand-on of /Prev, so main cannot reach these shapes. The false reading comes with this batch's #208/#210 refusals. The read-in-part texts themselves are new (54d56c6, 0fc7ce5).

The section's own every-number refusal is unaffected. The tests at :2384 and :2625, and an /XRefStm table that cannot be read under a disjoint range, keep their rows and say so truly. Any fix must keep these right.

This is minor: the text misleads, and a test pins a message its own comment contradicts. decisions.md flags it for round 2 and does not accept it, so it is not a settled point. No maintainer choice is needed: the fix makes the message say what the index holds.
VERDICT EVIDENCE: Probe on 761ce0b, Release, 0 warnings. Source: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/refute-kept/ZzRefuteKeptProbe.cs.txt. Outputs: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/refute-kept/out-head.txt and /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/refute-kept/out-head-hybrid.txt.

Shape: newest table -> /Prev lost stream 9 `<< /Type /XRef /Size 12 /Index {/X | [3 2] | [10 1]} /W [1 -4 2] /Prev T1 /Length 0 >>` -> T1 (new three, new four) -> T0 (old 3 to 6).

Every-number cases (/Index /X); the lost stream itself always reports "...The chain goes on through its /Prev; every object no section read before it indexes is looked for by rebuilding the index...":
(a) T1 = `3 3 / new3 / new4 / junk junk junk / trailer`: "...was read only in part: the row for object 5, at offset 487, ...; the rows read were kept, and every other object no section read before it indexes is looked for by rebuilding..." Index: idx[3]=no, idx[4]=no.
(b) T1 = a stream with /Index [3 3] and 2 rows: "...it holds 2 rows where its /Index and /Size declare 3; the rows it holds were kept, and the objects whose rows it lacks are looked for..." Index: idx[3]=no, idx[4]=no. With 1 row: "the row it holds was kept", idx[3]=no.
(c) Refused row (generation 65536): "...that row alone was refused, the rows after it read, ..." Index: idx[4]=no.
(f) T1 cannot be read (`junk junk junk / more`): "...is there but cannot be read: ... The rows read before the fault, if any, were kept; the objects only the rest indexes are looked for by rebuilding the index." Index: idx[3]=no, idx[4]=no.
(e) Hybrid table H (/XRefStm X, /Prev T0) under the /X stream, X = `6 2 / new6 / junk junk junk / more`: "...The rows read before the fault, if any, were kept; every other object..." Index: idx[6]=no. With X read in part (ends in trailer): "the rows read were kept, and every other object..." Index: idx[6]=no.

Range case (/Index [3 2]): (a') "the rows read were kept, and the objects the rest of that subsection numbers..." with idx[3]=no, idx[4]=no. (b') and (c') give the same texts as (b) and (c), with idx[3]=no and idx[4]=no; in (c') idx[5]=yes. (f') gives the same text, idx[3]=no.

Controls:
- /Index [10 1]: (a'') and (b'') keep idx[3] and idx[4] (yes). (c'') has idx[3]=no (its own refusal) and idx[4]=yes.
- (e'') an /XRefStm table that cannot be read under [10 1] says "every other object" (its own R1-Q2 refusal) and keeps idx[6]=yes.
- A sound T1 under /X gets no report, and idx[3]=no.
- In every case the objects read new three, new four and new six through repairedAfter=True.

Code: the table tail checks `_everyNumberRefused || _rowsLostPast || ...` (:4165) and still says "the rows read were kept". The stream tail (:4152-4158) has no such check. In the /XRefStm lost path, RefuseRowsLost(stream) (:3400) runs before ReportLostSection (:3404). In the main chain (:3249 then :3275) and in the /XRefStm read path (:3432 then :3436), RefuseRowsLost runs after the report.

git grep on 21a0298: only "The rows read before the fault, if any, were kept" (ReportLostSection, :3199). The read-in-part tails are absent there. git log -S puts them in 54d56c6 and 0fc7ce5. Main has HandOnPrevious, RefuseDeclared and _everyNumberRefused 0 times; it has only _refusedNumbers.
FIX (refuter): Record on the section what newer sections refused, independent of read order, instead of sampling the flags at report time. In AddFromChain, when a row is dropped because RefusedToOlderRows(number) holds, or a newer section's refusal is in _refusedNumbers, set a flag on XRefSectionRecord, such as RowsRefusedByNewer. When the section starts reading its rows, record whether _everyNumberRefused already held, such as EveryNumberRefusedBefore. Both are set before the section's own RefuseRowsLost, so the /XRefStm lost path, where that call comes before the report, needs nothing special.

Then pick each tail from those flags:
- EveryNumberRefusedBefore. Say that no row it read stands, a newer section having refused every number to the older sections' rows, and use the every-number tail. Apply this to the stream tail, the table tail, the refused-row tail, and both table branches of ReportLostSection, including the !NumbersRefused one.
- RowsRefusedByNewer only. Say "the rows read that no newer section refused were kept", or the stream's equivalent. Do not say "that row alone was refused" bare.
- Otherwise, keep today's texts. A section's own every-number refusal (_rowsLostPast, SubsectionsUnknown under /XRefStm, R1-Q2) keeps "the rows read were kept, and every other object…", which is true there.

Tests:
- Correct the assertion at CrossReferenceChainTests.cs:2305 to agree with its comment.
- Add rows under /X and under [3 2] for the stream tail (2 rows and 1 row), the refused-row tail, the /XRefStm table that cannot be read, and the /Prev table that cannot be read.
- Keep :2384, :2625 and an [10 1] /XRefStm control as the cases where the rows stand. They kill a mutant that reads the flag after the section's own refusal.

Docs: align PdfDiagnosticCodes.cs:61 ("The rows read were kept") and diagnostics.md:112 ("The rows read are kept") with "but for those whose numbers a newer section refused, which the rebuild finds".

This is a wording fix, not a design choice for the maintainer.

######## [8] sections:bad-row-then-eof-tail-omits-lost-trailer — reviewer minor; refuter confirmed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:4167-4169 (table tail chosen by `section.Fault is not null`); PdfDiagnosticCodes.cs:61-63 ('the message says what was lost: ... and those only the sections a lost trailer would name'); docs/website/docs/reference/diagnostics.md:112
CLAIM: A table named by /Prev or startxref can have a row that is no row and then be cut by the end of the file before its trailer (SubsectionsUnknown, TrailerFault Missing). Its report names the bad row, as decided ('one fault: the first that lost rows'). The tail is then chosen by whether a Fault is set, not by what was lost, so it says only that the rest of that subsection is looked for by rebuilding. The table's trailer is lost too, and with it the older sections the trailer would name. The plain cut says so ('or the sections its trailer would name'), and the XML doc and the reference promise the message says it. The reader's report never mentions the lost trailer; only the validator's file.trailer-missing does. Round 1's [30] made the /XRefStm-named case choose its tail by SubsectionsUnknown, but the /Prev and startxref case still chooses by Fault.
EVIDENCE: PrevCutEight: T2 (startxref) indexes 0-2 and names T1 by /Prev. T0 indexes 3 and object 8 `(eight only in the oldest)`. T1 ends the file.
- T1 = `3 2 / new3 / junk junk junk` + EOF: `Warning xref.section-unreadable @493: The cross-reference table /Prev names at offset 493 was read only in part: the row for object 4, at offset 522, is not an offset, a generation and n or f; the rows read were kept, and the objects the rest of that subsection numbers are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows.` Section `Read inc=True unk=True`; `idx[8]=no`; `obj8=eight only in the oldest` via a rebuild. Rules: file.trailer-missing and xref.section-malformed.
- T1 = `3 1 / new3` + EOF: `... the file ends before its trailer; the rows read were kept, and what only the rest of the table, or the sections its trailer would name, index is looked for by rebuilding the index.`
- With `trailer << /Prev T0 >>` after the bad row, T0 is read and `idx[8]=yes`.
- startxref naming (prevcut-first-badrow-eof) gives the same subsection-only tail.
- `junk junk junk\ntrai` gives the same as EOF (consistent with [8]).
No test has a bad row followed by the end of the file for /Prev or startxref.
FIX (reviewer): In ReportSectionReadInPart's table branch, choose the tail by what was lost. When `section.SubsectionsUnknown` and naming is not /XRefStm, use the 'what only the rest of the table, or the sections its trailer would name, index' tail, whatever Fault holds. Optionally add 'and the file ends before its trailer' after the named fault. Add a row `3 2\n{row:3}\njunk junk junk\n` to A_table_prev_names_that_the_end_of_the_file_cuts_after_a_row_... and to the startxref theory, asserting the full message.
--- VERDICT REASON: The finding holds. The problem case is a classic table, named by /Prev or by startxref, where a row that is no row ends a subsection and then the end of the file cuts the table before its trailer.

The reader handles it correctly:
- ReadSubsection sets Fault and Incomplete.
- The end-of-file branch of TryReadXRefTable (PdfFileReader.cs ~4772-4787) then sets TrailerFault Missing and SubsectionsUnknown and keeps the table read in part.
- The chain ends there.
- RefuseRowsLost refuses every number.
- An object that only the older section indexes is found by a rebuild.

The message is what is wrong. ReportSectionReadInPart (PdfFileReader.cs:4167) picks the table tail by `section.Fault is not null`, not by what was lost, so the message only says that the rest of that subsection is looked for by rebuilding. It never says that the trailer was lost, nor that the sections the trailer would name were lost with it. The only report of the lost trailer is the validator's file.trailer-missing.

Two texts promise more than the message gives:
- The public XML doc of XRefSectionUnreadable says "the message says what was lost: the objects the lost rows would index ..., and those only the sections a lost trailer would name index".
- reference/diagnostics.md:112 says "one fault ..., and what was lost".

This is not a re-report of round 1:
- [30] narrowed the tail only for the /XRefStm-named cut.
- [39] settled that the message names one fault, by priority. That concerns which fault is named, not the tail.

The same text was already there at 071f829, from 54d56c6. Round 1 missed it; the round 1 fixes did not introduce it. Nothing breaks in behavior, so this is not major. It is a misleading message against the documented promise, with no test for it, so minor.

The fix is a wording change, not a design choice for the maintainer. It keeps "one fault in its message" as decided: the tail changes and the named fault stays the same.
VERDICT EVIDENCE: I wrote an uncommitted probe, ZzBadRowEofProbe, and ran it on 761ce0b in a Release build. File layout: T0 (xref:1) indexes object 8 `(eight only in the oldest)`. T2 (startxref) indexes 0-2 and gives `/Prev {xref:3}`. T1 is the cut table and ends the file.

- T1 = `3 2\n{row:3}\njunk junk junk\n`: `Warning xref.section-unreadable @380: The cross-reference table /Prev names at offset 380 was read only in part: the row for object 4, at offset 409, is not an offset, a generation and n or f; the rows read were kept, and the objects the rest of that subsection numbers are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows.` The section is `/Prev @380 Table Read inc=True trailerFault=Missing`. WasRepaired is False before object 8 is read. obj8 = "eight only in the oldest" with repaired=True. Rules: `file.trailer-missing ... the file ends first` and `xref.section-malformed ... the row for object 4 ...`.
- The same T1 with `trai` after the bad row, or with `9 1\n` (a later subsection cut by the end of the file), gives the same subsection-only tail.
- T1 = `3 1\n{row:3}\n` (plain cut): `... the file ends before its trailer; the rows read were kept, and what only the rest of the table, or the sections its trailer would name, index is looked for by rebuilding the index.`
- Control: the same bad row followed by `trailer\n<< /Prev {xref:1} >>`. T0 is read (3 sections) and obj8 is served with repaired=False. The subsection tail is right there.
- startxref naming (`0 5\n{free}\n{row:1}\n{row:2}\n{row:3}\njunk junk junk\n` + EOF, after `startxref/%%EOF`): `Warning xref.section-unreadable @257: The cross-reference table startxref names at offset 257 was read only in part: the row for object 4, at offset 346, ...; the rows read were kept, and the objects the rest of that subsection numbers are looked for by rebuilding ...`. The section has trailerFault=Missing, and obj8 comes from a rebuild.

Code at 071f829, ReportSectionReadInPart: `section.Fault is { } stopped ? ...subsection tail : "the file ends before its trailer; ... or the sections its trailer would name..."`. That is the same text as at the branch head.

Trial fix: I changed line 4167 to `section.Fault is not null && !section.SubsectionsUnknown`. All bad-row-then-EOF shapes then got the trailer tail, and the bad-row-then-trailer control kept the subsection tail. The CrossReference*, GuardCut* and DocumentReader* test classes passed: 1534 passed, 0 failed. No existing test notices the change, which confirms that no test covers this shape. I reverted the trial fix and deleted the probe afterwards.

Every CutUpdate/CutFirst use in the tests builds a cut after sound, free or refused rows only. The two RowEndingInX shapes have their trailer.
FIX (refuter): In ReportSectionReadInPart's table branch (PdfFileReader.cs:4167), choose the tail by what was lost, not by whether a Fault is set. Change `: section.Fault is not null` to `: section.Fault is not null && !section.SubsectionsUnknown`.

For a table read in part that was named by /Prev or startxref, SubsectionsUnknown is set only by the end-of-file cut. A bad row followed by the end of the file, or by `t`..`traile`, or by a later subsection the end of the file cuts, then gets the existing tail "what only the rest of the table, or the sections its trailer would name, index is looked for by rebuilding the index". That tail already covers the rest of the faulty subsection.

Keep one fault in the message, the bad row, as [39] and the docs decided. Do not add "and the file ends before its trailer" unless XRefSectionUnreadable's XML doc and diagnostics.md ("one fault") are reworded with it.

Optionally, update the XML comment above the ternary so it says the tail follows what was lost.

Tests:
- Add `3 2\n{row:3}\njunk junk junk\n` and `...junk junk junk\ntrai` rows (or a sibling theory) beside A_table_prev_names_that_the_end_of_the_file_cuts_after_a_row_..., asserting the full message.
- Add the same with an object only the older table indexes, found by a rebuild.
- Add a startxref row (CutFirst with `0 5` and a junk row) beside The_table_startxref_names_that_the_end_of_the_file_cuts_after_a_row_....
- Keep the RowEndingInX test (bad row then trailer) asserting the subsection tail, so that a mutant dropping the new condition is killed.

No maintainer decision is needed.

######## [9] sections:lost-ranges-joined-one-way — reviewer minor; refuter confirmed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:4272 (LoseRows: `first >= lost[^1].First && first <= lost[^1].End`); docs/threat-model.md:421 ('those that meet joined as they are lost'); docs/website/docs/concepts/lazy-reading.md:147-148 ('those that meet joined first'); docs/website/docs/concepts/reader-limits.md:147; PdfFileReader.cs:136-138 (MaxRefusedRanges doc) and the _rowsLost doc
CLAIM: [15]'s fix joins a lost range into the last one only when the new range starts within the last or right at its end. A range that meets the last from below is kept apart, though it meets: (101,102) then (100,101). The count toward MaxRefusedRanges therefore depends on the order the file writes its subsections. A table losing 5,000 adjacent one-row subsections in ascending order keeps one range and reads normally. The same 5,000 in descending order make 5,000 ranges, refuse every number, and rebuild the whole file at opening. A stream's ranges (RefuseRanges) are sorted before they are joined, so they do not depend on order. The docs and the constant's comment say ranges that meet are joined as they are lost, which is false for this order. The cost is bounded but avoidable, and only a file that is not valid reaches it, as [15] was.
EVIDENCE: ZzDescending: an oldest table indexes 1-3; the newest (startxref) holds 5,000 subsections `N 1\n0 0 x`.
- Ascending (100..5099): `chainRead=True repairedAtOpen=False idx[3]=yes`, report tail 'the objects the rest of that subsection numbers ...'.
- Descending (5099..100): `... the row for object 5099 ...; the rows read were kept, and every other object no section read before it indexes is looked for ...`, then `Repair xref.rebuilt`, `chainRead=False repairedAtOpen=True idx[1]=no idx[3]=no`.
Trial fix (reverted afterwards): `if (lost.Count > 0 && first <= lost[^1].End && end >= lost[^1].First) lost[^1] = (Math.Min(lost[^1].First, first), Math.Max(lost[^1].End, end));`. With it, descending gives `chainRead=True repairedAtOpen=False idx[3]=yes`. CrossReferenceChainTests, HostileInputTests, ReadOrderTests and the probe pass: 714 passed, 0 failed, including Each_number_a_table_loses_is_refused_whatever_order_its_subsections_take, which holds that a range lost below and apart from the last is kept and not dropped.
FIX (reviewer): In LoseRows, join when the new range meets the last on either side (`first <= last.End && end >= last.First`), taking the minimum first and the maximum end. A range below the last that does not meet it (end < last.First) stays apart, so nothing is dropped. Add a descending row to Subsections_a_table_loses_one_after_the_other_are_one_range (or to Ranges_a_table_loses_count_toward_the_bound_once_joined), and run the mutation catalog on the condition. Alternatively, keep the code and make the five texts say that only a range lost in ascending order is joined as it is lost.
--- VERDICT REASON: The finding holds on 761ce0b. In LoseRows (src/AdCodicem.Pdf/IO/PdfFileReader.cs:4272), a new range joins the last one only when `first >= last.First && first <= last.End`. A range that meets the last from below, such as (5098,5099) after (5099,5100), is therefore added as a range of its own. A table whose one-row subsections are adjacent but written in descending order passes MaxRefusedRanges at 4,097 subsections, refuses every number, and the whole file is rebuilt at opening. The same subsections in ascending order stay one range and are read normally. A stream's ranges are sorted before RefuseRanges joins them, so streams do not depend on the order.

The inline comment at 4269-4271 states the limit honestly ('just past it ... One lost below the last is kept apart'). Five other texts say more than the code does:
- the MaxRefusedRanges XML doc (PdfFileReader.cs:136-138) and the _rowsLost doc (:638-639) both say 'those that meet joined as they are lost';
- threat-model.md:421 says the same;
- lazy-reading.md:147-148 ('those that meet joined first') and reader-limits.md:147 ('those that meet joined') are written generally, so their table half overstates.

Round 1 did not settle this point. The [15] refuter added `first >= last.First` so that a range below the last would not be dropped, and did not consider joining with Math.Min. decisions.md records only '[15] LoseRows joins adjacent ranges', and no deviation or open point in r1-fix-result.json covers the direction of the join.

Two caveats on the fix:
- The reviewer's two-sided join fixes the descending case, but the count still depends on order in general. Ranges written as (100,101), (300,301), (101,102) stay three ranges, because the third meets the first and not the last. So after the code fix the texts must still say 'a range that meets the last one lost', not 'those that meet'.
- Only a file that is not valid reaches this, and what it costs is bounded: one rebuild at opening. Hence minor, as for [15]. Same bytes give the same output, so invariant 6 is not broken.
VERDICT EVIDENCE: Probe ZzDescendingProbe (uncommitted, removed afterwards), built like PartialFiles.LostSubsections: objects 1-3, an oldest table `xref 0 4` with `/Size 4`, then a newest table (startxref) of N subsections `{100+step*i} 1\n0 0 x\n` with `/Size 30000 /Prev oldest`, written in ascending or descending order.

On 761ce0b:
- 5000, ascending, step 1: `chainRead=True repairedAtOpen=False idx[1]=yes idx[3]=yes idx[100]=no`. A single xref.section-unreadable whose message ends '...the objects the rest of that subsection numbers are looked for by rebuilding the index where no section read before it indexes them...'.
- 5000, descending, step 1: `chainRead=False repairedAtOpen=True idx[3]=null`. The message reads 'the row for object 5099, at offset 281, ... every other object no section read before it indexes is looked for by rebuilding the index', followed by `Repair xref.rebuilt`.
- 4096 descending, step 1: chain read, not repaired.
- 4097 descending, step 1: rebuilt.
- 4097 at step 2 (apart), in either order: rebuilt, which is the decided behavior.

Trial fix, reverted afterwards: `if (lost.Count > 0 && first <= lost[^1].End && end >= lost[^1].First) lost[^1] = (Math.Min(lost[^1].First, first), Math.Max(lost[^1].End, end));`
- With it, descending 5000 and descending 4097 at step 1 give `chainRead=True repairedAtOpen=False idx[1]=yes idx[3]=yes idx[100]=no`.
- Step 2 is unchanged (rebuilt) in both orders.
- CrossReferenceChainTests, HostileInputTests, ReadOrderTests, ReaderLimitsTests and CrossReferenceRuleTests all pass: total 1486, failed 0. This includes Each_number_a_table_loses_is_refused_whatever_order_its_subsections_take, whose row '7 1 / 5 1' keeps a range below the last, apart from it, and refused.

The worktree was restored to a clean 761ce0b afterwards.
FIX (refuter): In LoseRows (PdfFileReader.cs:4272), join when the new range meets the last on either side: `if (lost.Count > 0 && first <= lost[^1].End && end >= lost[^1].First) lost[^1] = (Math.Min(lost[^1].First, first), Math.Max(lost[^1].End, end));`. A range below the last that does not meet it (end < last.First) is still kept apart, so nothing is dropped.

Rewrite the inline comment as 'A range that meets the last one lost, from within, above or below, joins it ...'.

Tests:
- Add a descending case to Subsections_a_table_loses_one_after_the_other_are_one_range, for example a `descending` parameter on PartialFiles.LostSubsections; 5,000 adjacent subsections in descending order must not be repaired.
- Add a row to Each_number_a_table_loses_is_refused_whatever_order_its_subsections_take that loses a range partly below the last ('6 2\n0 0 x\n5 2\n0 0 x\n'), so that the Math.Min mutant dies.
- Run ADR 53's catalog on the new condition.

Docs: the MaxRefusedRanges doc (:136-138), the _rowsLost doc (:638-639), threat-model.md:421, lazy-reading.md:147-148 and reader-limits.md:147 should say that a table's lost range is joined to the last one it lost when they meet, as it is lost. 'Those that meet' overstates even after the fix, since a range that meets an earlier range but not the last stays apart.

Fully order-independent counting, by sorting and merging the list when it reaches the bound, is not worth its hostile-input cost. The alternative of keeping the code and only scoping the texts to ascending order is acceptable too. Either way it is not a decision the maintainer needs to make.

######## [10] sections:xrefstm-table-cut-before-rows-said-read — reviewer nit; refuter narrowed nit maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3429
CLAIM: A table that an /XRefStm names and that MaxXRefSectionLength cuts before its first row is Read: the guard's, and now whatever the index holds, after [7]. It is reported with xref.hybrid-stream-lost as 'a cross-reference table, whose rows were read in the stream's place', and the validator's xref.hybrid-stream-wrong copies the words, though none of its rows was read. Before [7], the same shape under an empty index was worded 'which cannot be read'.
EVIDENCE: HybridUnder with no lost stream, X = 'xref' + 300 spaces + `6 2 / new6 / new7 / trailer`, MaxXRefSectionLength=150: `limit.xref-section-length @696`, then `Warning xref.hybrid-stream-lost @696: The /XRefStm of the cross-reference table at offset 463 names offset 696, a cross-reference table, whose rows were read in the stream's place: ...`. Section `/XRefStm@696 Table Read cut=True`, `idx[6]=no idx[7]=no`. Rule `Error xref.hybrid-stream-wrong: ... whose rows were read in the stream's place ...`.
FIX (reviewer): When the stream record is CutByLimit and read no row (HighestNumber < 0), word the naming as 'a cross-reference table, which one of the reader's limits cut before its first row, in the stream's place', or drop 'whose rows were read'.
--- VERDICT REASON: The wording claim holds at 761ce0b; the history claim is wrong.

What holds: [7]'s fix makes TryReadClassicTable's `windowFull` branch return true. So a table that an /XRefStm names and that MaxXRefSectionLength cuts before its first row is Read, with CutByLimit set and HighestNumber -1. It then takes the branch that is not lost (PdfFileReader.cs:3424-3430). That branch calls ReportNoStream with "a cross-reference table, whose rows were read in the stream's place", but none of that table's rows was read. HybridStreamWrongRule copies NamesNoStream into its Error finding word for word. The main fault is still reported correctly: the keyword was seen, so the /XRefStm does name a table where it should name a stream (ADR 34: the file's fault). Only the clause about how the reader handled it is untrue. A table read whole that holds no rows (`0 0`) makes the clause empty, not false. Under the guard the rows exist and were not read, so here it is false.

What does not hold: "Before [7], the same shape under an empty index was worded 'which cannot be read'."
- At 0353e24 (R1b, after [26]'s fix, before [7]'s fix in 0fc7ce5), the empty-index table cut by the guard was Malformed with CutByLimit set. The `!stream.CutByLimit && DeclaresItselfASection(stream)` condition (line 3353 there) stopped any naming report. The test A_table_an_xrefstm_names_that_a_guard_cuts_before_its_rows_is_never_said_to_be_one_that_cannot_be_read, added in 0353e24, asserts exactly that.
- At 071f829 no table that cannot be read got ReportNoStream at all.
- In the finding's own shape, where a newer section indexes rows, 071f829 already returned true (`_xref.Count > 0`) and already used these words.

So the wording comes from #156's fix in this batch (main 21a0298 has none of it). [7] only extended it to the empty-index shape, which before had no naming report.

Why nit:
- The shape is reachable only with a limit the caller set below the first row, or with more than 64 MB before the first row under the default.
- limit.xref-section-length ("only the entries within it were read") sits at the same offset in the diagnostics, and xref.checked-in-part sits beside it in the validator's report.
- Nothing is served differently.

A related point, not raised by the reviewer: since [7], the test A_table_an_xrefstm_names_that_a_guard_cuts_before_its_rows_is_never_said_to_be_one_that_cannot_be_read no longer goes through the lost branch it was written for. It passes because the table is now Read, and it pins neither the naming's words nor its presence.
VERDICT EVIDENCE: I wrote a probe, ZzRefuteXRefStmCut (uncommitted, removed afterwards), on the detached head 761ce0b. Release build, 0 warnings. It runs PdfDocument.Open and then PdfValidator.

A. HybridFiles.NamingATableNeverRead(ten: 300 spaces + "10 1\n{row:10}\n"), MaxXRefSectionLength=150:
- `Warning limit.xref-section-length @390: The cross-reference table runs past 150 bytes; only the entries within it were read. ...`
- `Warning xref.hybrid-stream-lost @390: The /XRefStm of the cross-reference table at offset 743 names offset 390, a cross-reference table, whose rows were read in the stream's place: ...`
- `Repair xref.rebuilt`
- Section: `/XRefStm@390 Table Read cut=True highest=-1 nns='a cross-reference table, whose rows were read in the stream's place'`
- Validator: `Error xref.hybrid-stream-wrong: ... names offset 390, a cross-reference table, whose rows were read in the stream's place, where ISO 32000-1 ...` and `Information xref.checked-in-part`.

The same file read whole gives the same hybrid-stream-lost wording, with highest=10.

B. Rows cut by the guard after row 10 was read: cut=True highest=15, same wording. This is accurate, and it is the shape the MaxIndexEntries test pins as identical to the whole read's message.

C. The shape of the empty-index test (newer table `0 0`, comment of 200 bytes, MaxXRefSectionLength=128):
- `limit.xref-section-length @132`, then `xref.hybrid-stream-lost @132: ... names offset 132, a cross-reference table, whose rows were read in the stream's place`, then `xref.rebuilt`
- Section `/XRefStm@132 Table Read cut=True highest=-1`
- Validator: hybrid-stream-wrong with the same words.

History, from the code:
- 071f829 already has the text at :3304 and `// Keep whatever was indexed. return _xref.Count > 0;` at :4428-4429.
- 0353e24 has `if (!stream.CutByLimit && DeclaresItselfASection(stream))` at :3353 and `return _xref.Count > 0;` at :4672.
- `git show 21a0298:src/AdCodicem.Pdf/IO/PdfFileReader.cs | grep -c "whose rows were read in the stream's place"` gives 0.
FIX (refuter): In PdfFileReader.cs's not-lost branch (:3424-3430), choose the clause by whether the table read a row before a guard cut it:

```
ReportNoStream(stream, stream.CutByLimit && stream.HighestNumber < 0
    ? "a cross-reference table, which one of the reader's limits cut before its first row"
    : "a cross-reference table, whose rows were read in the stream's place");
```

Keep the test on CutByLimit, so that a table read whole with no rows keeps today's words. A table cut after some rows keeps the whole read's words, as A_table_an_xrefstm_names_is_reported_where_a_guard_cuts_its_rows_as_where_it_is_read_whole asserts.

Then:
- Add the new phrasing to XRefSectionRecord.NamesNoStream's list of words (:216).
- Turn A_table_an_xrefstm_names_that_a_guard_cuts_before_its_rows_is_never_said_to_be_one_that_cannot_be_read into a test that asserts the hybrid-stream-lost message and NamesNoStream under MaxXRefSectionLength=128, beside the whole read. It currently passes without going through the branch it names.
- Add a row to CrossReferenceRuleTests' hybrid-stream-wrong theory for the cut-before-first-row shape.

This is not a design choice for the maintainer. Run ADR 53's catalog on the changed line.

######## [11] headers:bench-binary-letters-summary — reviewer nit; refuter confirmed nit maintainer=False
LOCATION: benchmarks/AdCodicem.Pdf.Benchmarks/StreamEndSearchBenchmarks.cs:108
CLAIM: Binary()'s summary says the data holds "every value but the letters of obj and endstream". The code at :117 replaces only 'o', 'b', 'j' and 'e', so the data still holds 'n', 'd', 's', 't', 'r', 'a' and 'm', which are letters of endstream. The data does spell neither obj, endobj nor endstream, because 'e', 'o', 'b' and 'j' are gone, but the sentence names the wrong set of bytes.
EVIDENCE: Probe ZzBinaryLetters copies Binary() exactly (new Random(1729), 4 MB, the same `value is 'o' or 'b' or 'j' or 'e' ? 'z'`) and counts each letter. Output: `o=0 b=0 j=0 e=0 n=16298 d=16423 s=16191 t=16524 r=16053 a=16561 m=16404`.
FIX (reviewer): Write: "every value but o, b, j and e, so that the data spells neither obj nor endobj nor endstream".
--- VERDICT REASON: The finding holds as stated. At 761ce0b, the summary of `Binary()` (benchmarks/AdCodicem.Pdf.Benchmarks/StreamEndSearchBenchmarks.cs:108) says the data holds "every value but the letters of obj and endstream". The code at :117 replaces only 'o', 'b', 'j' and 'e', so 'n', 'd', 's', 't', 'r', 'a' and 'm', which are letters of `endstream`, are still drawn at their usual rate. The data still spells no keyword, because each one the search could stop at (obj, endobj, stream, endstream, xref, trailer) holds an 'o' or an 'e'. The benchmark therefore measures what it claims; only the sentence names the wrong set of bytes. The same wording appears a second time in the `Shape` property's summary at :36: "bytes of every value but the keywords' letters". The file entered the batch in 33b243f, one of the round 1 fixes. Neither r1-findings.md nor decisions.md mentions this wording, so this is not a re-report. It is documentation of a private helper in a benchmark, so the severity is nit.
VERDICT EVIDENCE: On origin/ccr-6d6c0ee6-dqm4lm-188 (761ce0b), `git log 21a0298..HEAD` on the file shows only 33b243f. Lines 108 and 117 read as the finding quotes them. A file-based probe (`dotnet run probe.cs`) copies `Binary()` exactly: new Random(1729), Size = 4 * 1024 * 1024, and `value is 'o' or 'b' or 'j' or 'e' ? 'z' : (char)value`. It printed:
`o=0 b=0 j=0 e=0 n=16298 d=16423 s=16191 t=16524 r=16053 m=16404 a=16561 x=16308 f=16519`
`obj=False endobj=False endstream=False stream=False xref=False trailer=False`
These counts match the reviewer's exactly. The probe is at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute-binletters/probe.cs.
FIX (refuter): At :108, write: `Draws <see cref="Size"/> bytes from a fixed seed, every value but <c>o</c>, <c>b</c>, <c>j</c> and <c>e</c>, so that the data spells no keyword: neither <c>obj</c>, <c>endobj</c> nor <c>endstream</c>.` At :36, make the matching change in the `Shape` summary: replace "bytes of every value but the keywords' letters" with "bytes of every value but o, b, j and e, so that they spell no keyword". This is a docs-only change and needs no decision from the maintainer.

######## [12] headers:validation-rules-dash-nesting — reviewer nit; refuter refuted none maintainer=False
LOCATION: docs/website/docs/reference/validation-rules.md:323
CLAIM: 51d47af (round 1, [20]) put a dash pair inside a dash parenthetical that was already open. The sentence now has three em dashes: "that `endobj` being the object's — or the next object's, when the object lost its own and that object's header is one the header rule does not take — a sign before its number, say —: the container then runs on". A reader pairs the first two dashes, so "a sign before its number, say —:" is left dangling, and the example cannot be told apart from the main clause.
EVIDENCE: `sed -n 322,324p docs/website/docs/reference/validation-rules.md` at 761ce0b: "that `endobj` being the object's — or the next object's, when the object lost its own and that object's header is one / the header rule does not take — a sign before its number, say —: the container then runs on to that `endobj`. A comment / between a header's tokens does not hide it: the rule reads comments as a load does ([#261])." `grep -n 'a sign before its number, say'` gives line 323.
FIX (reviewer): Close the first parenthetical with commas and keep one dash pair, or none. For example: "that `endobj` being the object's, or the next object's when the object lost its own and that object's header is one the header rule does not take, a sign before its number, say: the container then runs on to that `endobj`." This is round 1's own minimal fix.
--- VERDICT REASON: The quote is accurate. At 761ce0b, line 323 has three em dashes in that sentence. The defect the finding claims is not there, though.

1. The sentence parses only one way, and in that parse nothing dangles. Dash 1 opens the outer aside, as it already did on main 21a0298. That aside never had a closing dash: the colon ends it. On main the passage read "the object's — or the next object's, when ... holds a comment, which the header rule does not take for one: the container then runs on". The round 1 fix only put a complete pair inside that aside: "— a sign before its number, say —". The third dash closes that pair. "—:" is the page's own way to close a dash pair before a colon (line 316: "holds one —:"). If a reader pairs dashes 1 and 2, the result is ungrammatical ("being the object's a sign before its number, say —:"), so that reading does not hold. The example is set off by dashes 2 and 3, so it can be told apart from the main clause.

2. The page uses this structure elsewhere and nobody has flagged it. Line 54 of the same page opens an aside with "— but for what makes it unreadable ..." that has no closing dash, and nests a pair inside it: "as far as the values read say — a subsection wrong by the element read whatever the cut one holds among them —, or data ...". reference/diagnostics.md:112 has the same structure. Across the website docs, 63 sentences have three or more em dashes, and several have an odd count. A three-dash sentence breaks no project convention: CLAUDE.md and docs/ have no rule on dashes.

3. The fix is round 1's settled outcome, applied exactly. In [20], the refuter's fix named this text: "is one the header rule does not take — a sign before its number, say —:". decisions.md lists [20] among the docs fixes decided without a question, and 51d47af applies it word for word. The finding does not show that the docs now misstate the code. HeaderText.cs (cited in [20]) still matches the content.

4. The finding calls its replacement "round 1's own minimal fix", but it is not. Round 1's minimal form kept the opening dash and only added the comma ("a sign before its number, say:"). The proposed text drops the dash and moves a comma ("the next object's when"). That makes "a sign before its number, say" read as an apposition hanging off "does not take,", which is no clearer than the current text.

At most this is a matter of taste: the sentence is dense. It is not a misreading the text forces.
VERDICT EVIDENCE: - At 761ce0b (origin/ccr-6d6c0ee6-dqm4lm-188), `sed -n 322,324p docs/website/docs/reference/validation-rules.md` gives: "that `endobj` being the object's — or the next object's, when the object lost its own and that object's header is one / the header rule does not take — a sign before its number, say —: the container then runs on to that `endobj`. A comment / between a header's tokens does not hide it: the rule reads comments as a load does ([#261])."
- At 21a0298 (lines 318-320) the same aside was: "that `endobj` being the object's — or the next object's, when the object lost its own and that object's header holds / a comment, which the header rule does not take for one: the container then runs on to that `endobj`." That is one dash, with the aside ended by the colon.
- At 071f829: "... does not take, a sign before its number say: the container ...".
- `git show 51d47af` changes only those two lines, applying [20]'s refuter fix ("— a sign before its number, say —:").
- Same page, line 316: "holds one —: not a page of the tree". Line 54: "([#263]) — but for what makes it unreadable whatever the value holds, as read whole: ... as far as the values read say — a subsection wrong by the element read whatever the cut one holds among them —, or data whose length ...". That is the same outer-open, inner-paired structure.
- A sentence count over docs/website/docs finds 63 sentences with three or more em dashes. Those with an odd count include concepts/lazy-reading.md, concepts/validation.md, reference/diagnostics.md (3) and reference/validation-rules.md (several).
- `grep -rni 'em dash|dashes' CLAUDE.md docs/*.md docs/adr` finds no project rule on dashes.
- r1-findings.md [20]: "FIX (refuter): ... write '...that object's header is one the header rule does not take — a sign before its number, say —: the container then runs on to that `endobj`.' The smallest fix is to add the comma: 'a sign before its number, say:'".
FIX (refuter): No fix needed. The sentence follows the page's own dash conventions, and it applies the wording round 1 settled.

If the maintainer wants it lighter anyway, the change is optional and does not touch the meaning. Put the example in parentheses so that only one dash remains, the one that opens the aside: "that `endobj` being the object's — or the next object's, when the object lost its own and that object's header is one the header rule does not take (a sign before its number, say): the container then runs on to that `endobj`." I would not use the finding's comma-only rewrite, which leaves "a sign before its number, say" hanging after "does not take,".

######## [13] validator-tests:t317-fails-with-builds-beside — reviewer minor; refuter narrowed nit maintainer=False
LOCATION: tests/AdCodicem.Pdf.Tests/ReaderTimingTests.cs:169 (bound smallTime * 8); comment :143-149 ('never past 8' at :146)
CLAIM: Four probes a turn have not settled #317's test. Under four busy loops with builds running beside it, it still fails at bound 8. That is the condition the same commit (e548beb) cites to widen the lost streams test to 12. The comment's 'never past 8' holds only for R1e's runs at load 4.7-6.3.
EVIDENCE: Setup: a copy of 761ce0b in a scratch worktree, with ZzRatios.Record(name, small, large) inserted before each timing assertion; nothing else changed. Combined class filter in one process (286 tests): `--filter-class *.ReaderTimingTests --filter-class *.HostileInputTests --filter-class *.HostileInputMemoryTests`. Four `yes > /dev/null` loops, killed by PID afterwards.

Five runs, load at start 3.5 to 13.0:
- Run 2 (load 8.10; my scratch build and other agents' builds ran beside it) failed: `failed ...The_validator_judges_an_object_stream_s_members_in_linear_time(shape: "an index wrong for every member")`, `(00:00:00.0253968 for 50,000, 00:00:00.2296783 for 200,000)`, ratio 9.04. In the same run the other rows read 7.01 (streams: 47.5 against 333.3 ms) and 5.91 (broken: 16.7 against 98.5 ms). Every large probe slowed; no small one did.
- Run 4 (load 6.79; other agents' builds) failed an older test the batch did not change, `The_members_of_an_object_stream_one_byte_apart_are_read_in_linear_time`, at 9.42 (6.6 against 62.5 ms).
- #317 rows over the 5 runs: streams 3.91-7.01, wrong-index 4.08-9.04, broken 4.49-5.91.
- Lost streams (bound 12): 4.15-4.55. Line-of-tokens test: 3.92-4.07.

The mutant's first busy run (load 9.59, builds beside) shows the noise directly. The 'header broken' row, which the scan mutant does not touch, read 12.66 (15.0 against 190.2 ms); filtered alone it reads 2.59-4.26.

Without busy loops (other agents kept the load at 2-3), each test filtered alone, 10 runs each:
- #317: 10/10 passed. Streams 3.57-4.02, wrong-index 4.14-4.62, broken 4.35-4.65; small medians 42.0, 25.0 and 14.5 ms.
- Lost streams: 10/10 passed, 4.05-4.23.

Header-scan mutant (measure/flaky/mutants/M1-scan-indexof.diff, which still applies):
- Alone: caught 5/5 at 14.76-16.19.
- Under four busy loops: caught 3/3 at 20.52-21.24.

Data: scratchpad/lot19/r2vt/data/{alone317,alonelost,busycombined,m1alone,m1busy}.{ratios,runs}.
FIX (reviewer): This is the maintainer's call, since R1-Q4 fixed four probes and bound 8. Options:
(a) Time four small probes against one large, so both measures span the same time and contention hits them alike. Correct code then reads about 1.1; the scan mutant reads about 3.7-5.3 (4×252 ms against 3.8-5.4 s). A bound of 2 sits between them. This is R1e's open point 7.
(b) Time the thread's CPU time rather than wall time.
(c) Keep bound 8, and reword the comment (:146) and the PR. 'Never past 8' holds under four busy loops alone. With builds beside, the test reached 9.0, as did the unchanged one-byte-apart test in the same class.
--- VERDICT REASON: The measurement reproduces: under four busy loops with a build running beside them, #317's test still crosses its bound of 8 now and then. But the finding is wider than its evidence in three ways, and what is left is wording.

1. R1-Q4 was implemented as decided and passed the validation it set. It decided four probes, a room of 1,000 and a bound of 8, validated over about 300 runs under four busy loops with the combined filter. R1e ran 300 of 300 combined runs and 100 of 100 runs alone. Load with builds beside is a heavier condition than the one the maintainer chose. Under it, the suite's unchanged main timing tests fail as well, at bound 8 and at bound 12:
   - the members one byte apart reached 9.42 (the reviewer's run 4);
   - an object stream whose every index is wrong reached 11.3 (mine);
   - the circular object streams, at bound 12, reached 18.3 (mine).
   In my 8 runs of the whole class under that load, #317 passed all 8 (at most 7.27) while those two main tests failed. So #317's test is not weaker than the suite's standard. Calling it unsettled reopens R1-Q4, which the brief excludes unless the text misstates it.

2. The lost streams comparison does not show inconsistency. That test can take a bound of 12 because its functional assertion (TryGet(3) false) catches the uncapped merge before any time is compared. #317's bound is pinned by the header-scan mutant, which R1e measured as low as 13.1. A bound of 12 would not survive this load anyway: the main circular-streams test failed at 18.3 at bound 12, and the reviewer's correct 'header broken' row reached 12.66.

3. The comment does not state a false range. 'Never past 8' is scoped to four busy loops, and R1e's 400 runs bear it out (2.1 to 7.7). Two things are inexact:
   - Line 144 says the four probes are something 'which contention slows as it slows the large one's'. Both failures contradict that: the reviewer's (25.4 against 229.7 ms) and mine (16.1 against 138.8 ms, large normally about 65). In each, the small probes ran at their normal time while all five large turns were slowed. The 4-to-1 span still lets bursty load hit the large measure alone.
   - The comment says nothing about heavier load. The lost streams comment in the same commit records its excursion with builds beside.
   That is wording, so nit. The PR should scope #317's closure to four busy loops, as R1e's open point 4 already says.
VERDICT EVIDENCE: Setup: Release build of 761ce0b (origin/ccr-6d6c0ee6-dqm4lm-188) in my worktree. The test binary was copied to scratch; the only change was an environment-gated ZzRatios.Record line before three timing assertions (#317, the members one byte apart, the lost streams), reverted afterwards. The worktree is clean. Load was four `yes > /dev/null` loops, killed by PID and checked gone, plus a loop of `dotnet build src/AdCodicem.Pdf -c Debug --no-incremental -p:UseSharedCompilation=false -m:2`. 4 CPUs; other agents ran beside.

- Without added load, 3 runs of the method alone, load 1.8-2.0: all passed, ratios 3.95-4.51.

- Four busy loops plus the build loop, the #317 method alone, 20 runs, load 2.0 to 14.2: 19 passed. Run 11, at load 9.25, failed:
  `failed ...The_validator_judges_an_object_stream_s_members_in_linear_time(shape: "a header broken before its last member")`, `Expected largeTime to be less than 129ms and 137.6µs ... (00:00:00.0161422 for 50,000, 00:00:00.1388192 for 200,000), but found 138ms and 819.2µs.` (ratio 8.60)
  Row ranges over 20 runs: broken 3.35-8.60, wrong-index 2.55-6.55, streams 2.64-7.54.

- Same load, `--filter-class *.ReaderTimingTests`, 8 runs at load 8.8-14.1: #317 passed all 8 (broken 3.83-6.49, wrong-index 1.82-5.43, streams 3.87-7.27). Two tests the batch did not change failed (`git diff 21a0298 761ce0b` hunks start at line 130):
  - Run 1: `Four_times_as_many_object_streams_that_need_one_another_are_judged_circular_in_about_four_times_the_time(shape: "rho")`, bound 12, `(00:00:00.0081459 for 2,000, 00:00:00.1493387 for 8,000)`, ratio 18.3.
  - Run 4: `An_object_stream_whose_every_index_is_wrong_is_read_in_linear_time`, bound 8, `(00:00:00.0459720 for 50,000, 00:00:00.5188479 for 200,000)`, ratio 11.3.
  The members one byte apart read 2.80-7.48 and the lost streams 3.39-5.08.

- Against the decisions and data:
  - R1-Q4 (decisions.md:118) sets validation 'under four busy loops with the combined filter'.
  - R1e's data (scratchpad/lot19/r1e/data, long-busy-*) has 300 combined runs at load 4.73-6.31 and 100 alone runs at 3.50-6.22, all passed, ratios 2.14-7.68. That matches the comment at :143-149.
  - R1e's own 7.68 row had 'all five large turns slowed to 120 ms against 69', the same mechanism.
  - The scan mutant's floor is 13.1 (r1-fix-result.json R1e measurements), which rules out bound 12.

Data: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/refute-t317b/data/{quiet317,bb317,bbtiming}.{runs,ratios}, pids.log. Scripts in the same folder: run.sh, loaded.sh, summ.py, hook.py.
FIX (refuter): Keep R1-Q4's design: four probes a turn, a room of 1,000, bound 8. No widening, since the scan mutant's floor of 13.1 rules out 12.

Correct the comment at tests/AdCodicem.Pdf.Tests/ReaderTimingTests.cs:143-149:
- Replace 'which contention slows as it slows the large one's' with what was measured. For example: 'which a busy machine slows apart from the large one's far less often than a single probe'.
- Add the heavier-load record, as the lost streams comment does. For example: 'with builds running beside four busy loops, at a load of 9 to 14 on four cores, it passed 8 once in 20 runs on its own, at 8.6, and once in 5 runs beside the hostile input tests, at 9.0, a load under which the unchanged timing tests beside it failed too, up to 18.3 at a bound of 12'.

In the PR, and in status.md's record of this batch, say #317 is closed under four busy loops by R1e's 400 runs, not on an oversubscribed machine. This needs no maintainer decision.

If the maintainer wants the timing tests to survive builds beside them, that is a design choice. The options are equal spans (four small probes against one large, bound about 2, to be measured first) or thread CPU time. The choice covers the whole ReaderTimingTests class, whose main tests fail under the same load, so it belongs in a debt across the class, not in this batch.

######## [14] validator-tests:rows-said-kept-after-every-number-refused — reviewer minor; refuter confirmed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:4166 (table read in part), :4157 (stream read in part), :4037 (lost section's 'The rows read before the fault, if any, were kept'); src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:61; docs/website/docs/reference/diagnostics.md:112; pinned by tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:2305
CLAIM: Once a newer lost stream has refused every number, a section the chain reads after it is reported with its rows kept, though none of them stands. The round's own test asserts that sentence while it also asserts the row is gone (TryGet(3) false), and its comment says 'None of its rows stands'. The stream arm is wrong a second way: it says only the objects whose rows the stream lacks are rebuilt, when every object is. This is R1c's 'for round 2 to judge' point.
EVIDENCE: Setup for every probe (uncommitted Zz test, since removed): a newest table indexes 0-2 and names, through /Prev, a lost stream `<< /Type /XRef /Index /X /W [1 -4 2] /Prev <older> >>`. The older section then varies.

(1) Older section: a table whose row for object 4 ends in x. This is the round's test A_table_read_in_part_past_a_stream_that_refused_every_number_is_said_to_leave_every_object_to_a_rebuild, which passes:
`Warning xref.section-unreadable @146: The cross-reference table /Prev names at offset 146 was read only in part: the row for object 4, at offset 235, is not an offset, a generation and n or f; the rows read were kept, and every other object no section read before it indexes is looked for by rebuilding the index, ...`
Index: 1 and 2 at 9 and 58 (the newest table's rows), 3 False, 4 False.

(2) Older section: stream 7, `/Index [0 4 7 1]`, holding 4 of its 5 rows:
`Warning xref.section-unreadable @146: The cross-reference stream /Prev names at offset 146 was read only in part: it holds 4 rows where its /Index and /Size declare 5; the rows it holds were kept, and the objects whose rows it lacks are looked for by rebuilding the index where no section read before it indexes them, ...`
Index 3: False.

(3) Older section: a table with a bad row, then /Stray (Malformed):
`... is there but cannot be read: the row for object 4, at offset 235, is not an offset, a generation and n or f. The rows read before the fault, if any, were kept; the objects only the rest indexes are looked for by rebuilding the index.`
Index 3: False.

Main 21a0298 has no lost stream that refuses every number (#208 is new in this batch), so these texts are the batch's own.
FIX (reviewer): Where a newer section had already refused every number when the section is reported, drop 'the rows read were kept' / 'the rows it holds were kept' / 'The rows read before the fault, if any, were kept'. In ReportSectionReadInPart, `_everyNumberRefused` is still only a newer section's at that point. Say instead, for example: 'none of the rows read stands, a newer section having refused every number; every object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows'.

Take care not to touch R1-Q2's own refusal: an /XRefStm table with SubsectionsUnknown keeps its own rows (TryGet(6) true).

Add the same qualifier to the XML doc at PdfDiagnosticCodes.cs:61 and to reference/diagnostics.md:112 ('The rows read are kept').

Make the test at :2305 assert the corrected text, and add rows for the stream read in part and the Malformed table.
--- VERDICT REASON: The finding holds as stated. RefusedToOlderRows returns true for every number once _everyNumberRefused is set, so no row of a section the chain reads after such a lost stream reaches the index. Three message arms still say the section's rows were kept:
- ReportSectionReadInPart, table arm, :4166. It also says 'every other object', which implies the rows read stand.
- ReportSectionReadInPart, stream arm, :4155-4157. It never consults _everyNumberRefused, so it also says only 'the objects whose rows it lacks' are rebuilt.
- ReportLostSection, :4037. Under the refusal both of its halves are wrong.

The controls (the same files without the lost stream) show that 'kept' normally means indexed: idx[3]=yes and no rebuild. With the lost stream, idx[3]=no, and object 3 is served through a rebuild.

The round's own test pins the contradiction. CrossReferenceChainTests.cs:2287 says 'None of its rows stands', :2305 asserts 'the rows read were kept, and every other object ...', and the test then asserts TryGet(3) false.

This is the batch's own text. At 071f829 the table arm had no every-number tail; R1c added it under [15] and kept 'kept'. R1c reported this as an open point, and decisions.md left it 'For round 2', so it is unsettled, not accepted.

One more arm has the same flaw. ReportLostSection's NumbersRefused table arm (:4042, an /XRefStm-named Malformed table) also says 'The rows read before the fault, if any, were kept' under a newer every-number refusal; there idx[6]=no.

Related but outside the finding's claim: under a newer range refusal the same clause overstates for the refused numbers only (/Index [3 2]: 'the rows read were kept'/'the rows it holds were kept', idx[3]=no, idx[4]=no). The newer stream's message names those numbers, so it is less misleading.

The docs (PdfDiagnosticCodes.cs:61 'The rows read were kept'; diagnostics.md:112 'The rows read are kept') state the general rule. The lost stream's sentence in the same entries already says no older row stands under such a refusal, so the docs part is a nit at most.

Severity is minor: the message misleads and the test contradicts its own comment. Behavior is right, and the newer stream's report states the refusal.
VERDICT EVIDENCE: Setup: the newest table indexes 0-2. Through /Prev it names a lost stream `<< /Type /XRef /Size 7 /Index /X /W [1 -4 2] /Prev <older> /Length 0 >>`. Probe ZzKeptRows was rebuilt independently on 761ce0b and has since been removed. Each shape was also run without the lost stream as a control.

(1) Older section: a table whose last row (object 4) ends in x.
- Control: '...; the rows read were kept, and the objects the rest of that subsection numbers are looked for...'. idx 1,2,3 yes; obj3 read with no rebuild.
- With the lost stream: 'Warning xref.section-unreadable @146: The cross-reference table /Prev names at offset 146 was read only in part: the row for object 4, at offset 235, is not an offset, a generation and n or f; the rows read were kept, and every other object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows.' idx[1]=yes idx[2]=yes (the newest table's rows), idx[3]=no idx[4]=no. obj3='only in the oldest', WasRepaired false->true.

(2) Older section: stream 7 with /Index [0 4 7 1] holding 4 of its 5 rows.
- Control: idx[3]=yes, no rebuild.
- With the lost stream: '... was read only in part: it holds 4 rows where its /Index and /Size declare 5; the rows it holds were kept, and the objects whose rows it lacks are looked for by rebuilding the index where no section read before it indexes them, ...'. idx[3]=no; obj3 served after a rebuild (repairedAfter=True).

(3) Older section: a table with a bad row, then /Stray, so Malformed.
- Control: idx[3]=yes, no rebuild.
- With the lost stream: '... is there but cannot be read: the row for object 4, at offset 235, is not an offset, a generation and n or f. The rows read before the fault, if any, were kept; the objects only the rest indexes are looked for by rebuilding the index.' idx[3]=no, repairedAfter=True.

(4) The earlier refuter probe (refute-kept/ZzRefuteKeptProbe.cs.txt), re-run at head. Shape: hybrid, where the newest table names the lost /X stream, which names table H, whose /XRefStm is a Malformed classic table holding object 6's row.
- Output: 'The cross-reference table /XRefStm names at offset 331 is there but cannot be read: ... The rows read before the fault, if any, were kept; every other object ...'. idx[6]=no, repairedAfter=True.
- With the lost stream's /Index [10 1] (its own R1-Q2 refusal only): idx[6]=yes. There the text is right.

Code order: on the chain path (:3249 then :3275) and the /XRefStm read path (:3432 then :3436), ReportSectionReadInPart runs before RefuseRowsLost, so at :4165 _everyNumberRefused is only a newer section's. On the lost /XRefStm path, RefuseRowsLost (:3400) runs before ReportLostSection (:3404), so at :4041 the flag may be the table's own.

Test: CrossReferenceChainTests.cs:2287 comment 'None of its rows stands'; :2305 asserts 'the rows read were kept, and every other object ...'; :2312 asserts TryGet(3) false.

r1-fix-result.json, R1c deviation: "'The rows read were kept' is unchanged there, though under that refusal no row of the section stands ... Round 2 may want to look at it."
FIX (refuter): Record whether a newer section had refused every number before the section's own refusal runs. Use a local or record flag captured before RefuseRowsLost, because on the lost /XRefStm path the flag at report time may be the table's own. When it was a newer section's, replace the 'kept' clause in four arms: ReportSectionReadInPart's table arm (:4166) and stream arm (:4155-4157, which today ignore the flag), and ReportLostSection's two arms (:4037 and :4042).

Suggested text: 'none of the rows read stands, a newer section having refused every number; every object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows'. Use 'held' in place of 'read' for a stream.

Keep the current text where the every-number refusal is the section's own: an /XRefStm table with SubsectionsUnknown, and _rowsLostPast. Their rows stand (TryGet(6) true; the 4096/4097 theory at :2363).

Update the test at :2305 to the corrected text. Add rows for the stream read in part and the /Prev Malformed table, and one for the /XRefStm-named Malformed table under a newer /X stream.

Optionally, also qualify the general 'kept' for range refusals ('kept where no newer section refused their numbers'). Add the same qualifier to PdfDiagnosticCodes.cs:61 and to reference/diagnostics.md:112 ('The rows read are kept'). This part is a nit, since those entries already say no older row stands under the lost stream's refusal.

######## [15] validator-tests:r1q2-validator-assertion-either-rule — reviewer nit; refuter narrowed nit maintainer=False
LOCATION: tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:2683-2687
CLAIM: The R1-Q2 theory's comment says a table whose rows run into a dictionary or into the end of the file 'is said to be so, rather than malformed (#325)'. Its assertion accepts xref.section-malformed or file.trailer-missing on every row, so a mutant that swaps the two rules on any row passes.
EVIDENCE: Probe: each of the 8 rows of PartialFiles.TableAnXRefStmNamesOverOlderRows, validated.
- 6 rows give `xref.section-malformed, xref.hybrid-stream-wrong`.
- Rows `6 2\n{six}junk junk junk\n<< /Size 8 >>\n` and `6 1\n` give `file.trailer-missing, xref.hybrid-stream-wrong`.
The assertion `findings.Should().Contain(id => id == XRefSectionMalformed || id == FileTrailerMissing)` passes for all 8 whichever it is.
FIX (reviewer): Add an InlineData column naming the expected rule, and assert that rule and the absence of the other. Or keep the disjunction and replace the comment's 'rather than malformed' with 'which #325 settles'.
--- VERDICT REASON: The finding holds as stated. At 761ce0b, CrossReferenceChainTests.cs:2683-2684 has a comment saying a table whose rows run into a dictionary or into the end of the file "is said to be so, rather than malformed (#325)". The assertion at :2687 accepts xref.section-malformed or file.trailer-missing on all 8 rows. So the theory does not hold which rule each shape gets, though its comment says it does.

The gap is narrower than "a mutant that swaps the two rules passes". The per-shape behavior the comment describes is held elsewhere for tables named by /Prev and startxref, and the obvious first-order mutant on that behavior is killed elsewhere. That mutant drops `&& section.TrailerFault == XRefTrailerFault.None` in SectionMalformedRule. The theory lets it pass, but FileRuleTests.Rows_that_run_into_a_dictionary_without_the_trailer_keyword_miss_their_trailer kills it. So no surviving mutant was shown. What remains is a theory whose comment claims more than its assertion holds, for the /XRefStm-named path.

R1c listed the disjunction as a deviation in r1-fix-result.json: "accepts xref.section-malformed or file.trailer-missing ... #325's existing behavior". It was declared, not hidden. The comment's wording is also loose. Row "6 2\n{six}junk junk junk\nmore\n" also ends the file with no trailer, yet it is said to be malformed, which a per-row expected rule would make explicit.

This is not a design question. It is a test-precision nit.
VERDICT EVIDENCE: Rebuilt on origin/ccr-6d6c0ee6-dqm4lm-188 at 761ce0b with a Release build, 0 warnings. The probe ZzEitherRuleProbe (since deleted) validated PartialFiles.TableAnXRefStmNamesOverOlderRows(rows) for each of the theory's 8 rows:
- "6 2\n{six}{seven}/Bad\ntrailer\n<< /Size 8 >>\n" => [xref.section-malformed] [xref.hybrid-stream-wrong]
- "6 2\n{six}junk junk junk\nmore\n" => [xref.section-malformed] [xref.hybrid-stream-wrong]
- "6 1\n{six}7 x\n{seven}trailer\n<< /Size 8 >>\n" => [xref.section-malformed] [xref.hybrid-stream-wrong]
- "6 1\n" => [file.trailer-missing] [xref.hybrid-stream-wrong]
- "6 2\n{six}junk junk junk\ntrailer\n<< /Size 8 >>\n" => [xref.section-malformed] [xref.hybrid-stream-wrong]
- "6 1\n{six}2147483647 2\n{seven}trailer\n<< /Size 8 >>\n" => [xref.section-malformed] [xref.hybrid-stream-wrong]
- "6 2\n{six}junk junk junk\n<< /Size 8 >>\n" => [file.trailer-missing] [xref.hybrid-stream-wrong]
- "6 2\n{six}junk junk junk\n/Bad\ntrailer\n<< /Size 8 >>\n" => [xref.section-malformed] [xref.hybrid-stream-wrong]

This matches the reviewer exactly: 6 rows give section-malformed and 2 give trailer-missing.

Mutant check: I set SectionMalformedRule.cs to `var unreadable = section.State == XRefSectionState.Malformed;`. Rows "6 1\n" and "...junk junk junk\n<< /Size 8 >>\n" then give [file.trailer-missing] [xref.section-malformed] [xref.hybrid-stream-wrong]. The theory A_table_an_xrefstm_names_that_cannot_be_read_leaves_no_older_row_standing_for_what_it_lost still passes, 8/8. Across FileRuleTests, CrossReferenceRuleTests and ValidatorTests (798 tests), only FileRuleTests.Rows_that_run_into_a_dictionary_without_the_trailer_keyword_miss_their_trailer fails, so the mutant is killed there. Mutant reverted.
FIX (refuter): In the theory at CrossReferenceChainTests.cs:2638-2687, add a bool InlineData column `trailerMissing`, true for "6 1\n" and "6 2\n{six}junk junk junk\n<< /Size 8 >>\n" and false for the other six. Replace the disjunction with `findings.Should().Contain(trailerMissing ? PdfValidationRuleIds.FileTrailerMissing : PdfValidationRuleIds.XRefSectionMalformed)`. On the six rows that are false, also add `findings.Should().NotContain(PdfValidationRuleIds.FileTrailerMissing)`.

Do not assert that xref.section-malformed is absent on the "junk junk junk\n<< /Size 8 >>" row. Its bad row is a fault of its own, and closing #325 adds section-malformed there. Pinning its absence would make #325's fix flip this theory.

Keep the comment, made precise: "a table whose rows, after any bad row, run into a dictionary, or whose header the end of the file follows, is said to be missing its trailer alone (#325)". The cheaper alternative is to keep the disjunction and reword the comment to say only that either rule is accepted until #325 settles which.

######## [16] validator-tests:checksum-theory-name-zero-row — reviewer nit; refuter narrowed nit maintainer=False
LOCATION: tests/AdCodicem.Pdf.Tests/FilterDamageTests.cs:261 (helper ChecksumMismatch at :1763)
CLAIM: Keeps_all_of_a_zlib_stream_whose_checksum_is_wrong_and_warns_that_some_may_be_wrong still runs the payload "nothing". de0fa05 changed that row's report to 'which decoded to nothing', which no longer warns that anything may be wrong. The name does not hold for that row, and the helper now mirrors the source's `decoded == 0` branch to keep it passing.
EVIDENCE: With de0fa05's PdfFilterPipeline.cs reverted in a scratch copy, these fail:
- `Keeps_all_of_a_zlib_stream_whose_checksum_is_wrong_and_warns_that_some_may_be_wrong(payload: "nothing", plain: [])`
- the three rows of A_flate_stream_that_decodes_to_nothing_against_a_wrong_checksum_says_it_decoded_to_nothing

At head, the 'nothing' row passes. It asserts "A Flate stream's checksum disagrees with its data, which decoded to nothing." under a name that says 'warns that some may be wrong'.
FIX (reviewer): Leave the zero payload out of that theory, since the new theory holds it, and drop the helper's zero branch. Or rename the theory '..._and_warns_of_the_disagreement'.
--- VERDICT REASON: The claim holds. Keeps_all_of_a_zlib_stream_whose_checksum_is_wrong_and_warns_that_some_may_be_wrong (tests/AdCodicem.Pdf.Tests/FilterDamageTests.cs:261) takes its rows from Payloads, and Payloads includes { "nothing", [] }. Since de0fa05, that row expects "A Flate stream's checksum disagrees with its data, which decoded to nothing." de0fa05's own commit message says that text is there because "Nothing was kept that could be wrong", and concepts/diagnostics.md:133 agrees. So for that row the clause "warns that some may be wrong" in the name is false. The other three rows still match the name.

The behavior is pinned correctly and coverage is unchanged, so only the test's name is off: nit, as the reviewer said. The R1e stage report saw this row ("already decoded zero bytes and asserted the old text") and changed the helper, but left the name.

I narrowed the proposed fix. "Drop the helper's zero branch" would be wrong. The helper's zero branch (ChecksumMismatch at :1763, through ChecksumReport) is also the oracle of the property Keeps_all_of_any_zlib_stream_whose_checksum_is_wrong (:521-544), whose lengths come from Gen.Choose(0, 200_000). Without that branch the property's oracle is wrong at length 0. The fixed replay seed (WithReplay) may never draw 0, but the property claims to hold for any length. A helper whose expected text depends on the count is a normal oracle, not a defect. Leaving the zero payload out of this theory would also lose the "nothing followed by \n, \r, \r\n" tails, which the new zero-only theory does not exercise.
VERDICT EVIDENCE: Built Release in my worktree at 761ce0b (origin/ccr-6d6c0ee6-dqm4lm-188). At head, the filtered run of the three tests passes: 8 total, 8 succeeded (4 Payloads rows, 3 zero rows, 1 property).

Then I restored PdfFilterPipeline.cs from 51d47af (de0fa05's parent), with tests left at head, and rebuilt. 4 failed, 4 succeeded:
- Keeps_all_of_a_zlib_stream_whose_checksum_is_wrong_and_warns_that_some_may_be_wrong(payload: "nothing", plain: []): `Expected failures to be empty because nothing decoded whole, but found ... "followed by : 0 bytes, [Warning filter.checksum-mismatch: A Flate stream's checksum disagrees with the 0 bytes its data decoded to; all were kept, and some may be wrong.]"`
- A_flate_stream_that_decodes_to_nothing_against_a_wrong_checksum_says_it_decoded_to_nothing, all three rows (78DA030000000002, 789C0300FFFFFFFF, 789C010000FFFF00000002), each differing at index 75 from "...disagrees with its data, which decoded to nothing."
The property and the three non-empty rows pass both ways.

So at head the "nothing" row of the theory whose name says "warns that some may be wrong" asserts exactly the text that does not say so. de0fa05's message: "Nothing was kept that could be wrong, so the report now says only that the checksum disagrees with the data, which decoded to nothing."

The source was restored afterwards and the worktree is clean.
FIX (refuter): Rename the theory so its name holds for every Payloads row. For example, Keeps_all_of_a_zlib_stream_whose_checksum_is_wrong_and_warns_of_the_disagreement, or ..._and_reports_the_disagreement. Adjust its comment ("since the data may be wrong anywhere") to say that for data that decoded to nothing the report says only that. Update any threat-model or doc row that cites the old name.

Keep ChecksumMismatch's decoded == 0 branch: the property Keeps_all_of_any_zlib_stream_whose_checksum_is_wrong, over lengths 0..200,000, relies on it.

An equivalent alternative: give this theory member data without "nothing", and add the four tails ([], 0A, 0D, 0D0A) to A_flate_stream_that_decodes_to_nothing_against_a_wrong_checksum_says_it_decoded_to_nothing so no coverage is lost. This is the implementer's choice, not a maintainer question.

######## [17] validator-tests:test-name-refused-not — reviewer nit; refuter confirmed nit maintainer=False
LOCATION: tests/AdCodicem.Pdf.Tests/ReaderLimitsTests.cs:2406
CLAIM: The R1c test name A_reference_a_section_a_guard_stops_gives_to_a_number_a_newer_section_refused_not_stays_the_guard_s does not read as a sentence: 'refused_not'.
EVIDENCE: The name as written. Its comment says the newer stream declares object 45 'and not object 30, which the /Prev names'.
FIX (reviewer): Rename it A_reference_a_section_a_guard_stops_gives_to_a_number_no_newer_section_refused_stays_the_guard_s.
--- VERDICT REASON: The finding holds, and it is only a naming nit. The test was added by the R1c fix 0fc7ce5 and is still there at 761ce0b. Its name ends "..._a_newer_section_refused_not_stays_the_guard_s". Read as a sentence, that is "a number a newer section refused not": the "not" comes after the verb. That is not the "X_not_Y" contrast the suite uses elsewhere, as in "is_found_moved_not_passed_over" and "is_malformed_not_shifted". The proposed name is accurate. The test's newer stream declares only [45 1], so no newer section refuses object 30, the number the /Prev 30 0 R names. The finding leaves one thing out: docs/threat-model.md row 416 cites the test by its current name, so the rename must update that citation too. Nothing about behavior is at stake, and the maintainer has nothing to decide.
VERDICT EVIDENCE: At 761ce0b, tests/AdCodicem.Pdf.Tests/ReaderLimitsTests.cs:2406 reads `public void A_reference_a_section_a_guard_stops_gives_to_a_number_a_newer_section_refused_not_stays_the_guard_s(string guard)`. Its body calls `StoppedSectionFiles.Stopped("/Prev 30 0 R", ..., lost: "[45 1]")`, and its comment says the stream declares "object 45, its own, and not object 30, which the /Prev names". The test beside it, at line 2381, is `..._a_newer_section_refused_is_judged_as_a_whole_read_judges_it`. `git log -S'refused_not_stays' 21a0298..761ce0b` lists only 0fc7ce5, the R1c fix. A grep for other "_not_" test names finds only contrast forms: CrossReferenceChainTests.cs:995 `..._is_found_moved_not_passed_over`, CrossReferenceRuleTests.cs:622 `..._is_malformed_not_shifted` and CrossReferenceRuleTests.cs:1341 `..._is_shifted_not_broken`. Line 416 of docs/threat-model.md cites both test names. r1-fix-result.json:195 lists the test as R1c's control case.
FIX (refuter): In tests/AdCodicem.Pdf.Tests/ReaderLimitsTests.cs:2406, rename the test to A_reference_a_section_a_guard_stops_gives_to_a_number_no_newer_section_refused_stays_the_guard_s. Update the citation in docs/threat-model.md row 416 in the same commit, so the threat model does not cite a test that no longer exists. Use a `test:` commit with a footer of Refs #264.

######## [18] validator-tests:timing-comments-alone — reviewer nit; refuter confirmed nit maintainer=False
LOCATION: tests/AdCodicem.Pdf.Tests/ReaderTimingTests.cs:145-146 and :568
CLAIM: The timing comments use 'alone' for 'without busy loops', while the same comment uses 'on its own' for 'filtered alone'. So '4.0 to 4.2 alone over 20 runs' (lost streams) and '3.7 to 4.6 alone over 50 runs' (#317) read as filtered runs, and they were not.
EVIDENCE: R1e's measurements in r1-fix-result.json:
- 'Without load: 30 alone runs plus 20 combined runs, all passed, ratios 3.65-4.60'.
- 'Lost streams test ... in the same combined runs: ... and 3.99-4.15 over 20 runs without load'. All 20 of those were combined-filter runs.
My filtered runs measured 3.57-4.65 (#317) and 4.05-4.23 (lost streams), so the figures themselves are close.
FIX (reviewer): Write 'without load' where the comments say 'alone' (:145, :568). Keep 'on its own' for the runs filtered to the method.
--- VERDICT REASON: The finding holds at 761ce0b. In both comments, 'alone' means 'without busy loops', and the measurements behind them were not runs filtered to the method.

- At :145-146, one sentence uses 'alone' for 'without load' and then 'on its own' for 'filtered to the method': '...3.7 to 4.6 alone over 50 runs, and under four busy loops ... 2.1 to 7.7 over 100 runs on its own'.
- The class summary at :12-13 gives the word a third sense: 'run alone, in TimingCollection', meaning in a collection that runs by itself.
- Round 1's own notes used 'alone' for 'filtered alone' (r1-findings.md:1309, 1313: 'Alone it failed 0 times in 10', 'Filtered alone to the method').

A reader of :145 therefore cannot tell which kind of run the 50 were. At :568, 'alone over 20 runs' reads as runs of that test by itself, and it never was run that way.

The figures are right under the reading that was meant. That reading is 'without load', set at :140 by '5.0 to 7.3 alone and up to 10.5 under four busy loops'. No assertion, bound or code is affected, so this is wording only: a nit.

Related point, outside the finding's scope (I would fold it into the same fix): ':145-146 ... over 300 runs beside the other timing tests' overstates the run conditions. R1e's combined filter ran the method with only two other timing tests, not the class's other timing tests (r1e/run.sh: the #317 method, Four_times_as_many_lost_streams..., Older_rows_looked_for_among_4096_refused_ranges...).
VERDICT EVIDENCE: Branch head checked out detached at origin/ccr-6d6c0ee6-dqm4lm-188 = 761ce0b. `git diff 071f829 761ce0b -- tests/AdCodicem.Pdf.Tests/ReaderTimingTests.cs` shows that commit e548beb (the R1e fix) added both sentences. Main 21a0298 uses 'alone' in this file only at :13 and :133.

R1e's raw data, summarized with its own script (`python3 -I r1e/ratios.py data 'long-*'` in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r1e). In r1e/run.sh, MODE "alone" means filtered to the #317 method and "combined" means three timing tests in one process.

- long-quiet-alone (n=30): T317 ratios 4.08-4.58, 3.65-4.21, 3.94-4.16.
- long-quiet-combined (n=20): T317 ratios 4.31-4.60, 4.13-4.42, 3.97-4.23. LOST 3.99-4.15 (n=20).
- long-busy-alone (n=100): T317 max 7.68, next 6.36.
- long-busy-combined (n=300): T317 2.28-6.55. LOST 3.20-6.22.

So:
- '3.7 to 4.6 alone over 50 runs' (:145) is 30 filtered runs plus 20 combined runs, all without load (3.65-4.60).
- '4.0 to 4.2 alone over 20 runs' (:568) is entirely the 20 combined runs without load (3.99-4.15). This test was never run filtered to itself in that measurement.
- The busy figures check out: '2.1 to 7.7 over 100 runs on its own — 7.7 once, nothing else past 6.4' matches 7.68 and then 6.36.

R1e's report says the same: 'Without load: 30 alone runs plus 20 combined runs ... 3.65-4.60' and 'Lost streams test ... 3.99-4.15 over 20 runs without load'. In that report, R1e itself uses 'alone' to mean filtered, the sense the comment does not intend.

The reviewer's own filtered figures (3.57-4.65 and 4.05-4.23) are close to the commented ones, so the numbers hold under either reading. No build was needed: the claim concerns which runs the figures describe, and the committed data settles that.
FIX (refuter): Comment-only change in tests/AdCodicem.Pdf.Tests/ReaderTimingTests.cs:
- :145: '3.7 to 4.6 alone over 50 runs' -> '3.7 to 4.6 without load over 50 runs'.
- :568: '4.0 to 4.2 alone over 20 runs' -> '4.0 to 4.2 without load over 20 runs'.
- :140, for consistency in the same comment: '5.0 to 7.3 alone and up to 10.5 under four busy loops' -> '5.0 to 7.3 without load and ...'.
- Keep 'on its own' at :146 for the runs filtered to the method.
- At :145-146, also change 'over 300 runs beside the other timing tests' to 'over 300 runs beside two other timing tests'. Only two other timing tests shared the process.

No code, bound or decision changes. No maintainer question is needed.

######## [19] docs:rows-kept-after-every-number-refused — reviewer minor; refuter confirmed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:4153-4176 (ReportSectionReadInPart tails); docs/website/docs/reference/diagnostics.md:112 (xref.section-unreadable row); src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:61 (XRefSectionUnreadable XML doc)
CLAIM: Take a section read in part after a newer section has already refused every number (for example a lost stream whose /Index cannot be read). All three read-in-part messages then say the section's rows stand, but none does. The table branch says 'the rows read were kept'. The refused-row branch says 'that row alone was refused, the rows after it read'. The stream branch says 'the rows it holds were kept'. The public XML doc says 'The rows read were kept' and the reference row says 'The rows read are kept'. Commit 0fc7ce5's body says the report 'says every number was refused where it was', but the refused-row and stream branches never check _everyNumberRefused. This is the open point R1c and R1f passed to round 2. My judgement: fix it.
EVIDENCE: Probe ZzRowsKeptAfterEveryNumberRefused, on 761ce0b. The source is saved at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r2docs-out/ZzDocsRound2Probe.cs.txt and the output at r2docs-out/rows-kept.txt. In each file the newest table's /Prev names lost stream 6, `/Type /XRef /Size 7 /Index /X /W [1 -4 2] /Prev {oldest}`, which refuses every number. Its /Prev names the oldest section.
(a) Oldest is a table whose row 4 ends in x. Output: `Warning xref.section-unreadable at 146: The cross-reference table /Prev names at offset 146 was read only in part: the row for object 4, at offset 235, is not an offset, a generation and n or f; the rows read were kept, and every other object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows.` Then `object 3: inChainIndex=False value=only in the oldest WasRepaired=True`, so asking for object 3 rebuilt the index.
(b) Oldest is a table whose row 4 gives generation 70000. Output: `... was read only in part: the row for object 4, at offset 257, gives it generation 70000, outside 0 to 65535; that row alone was refused, the rows after it read, and its object is looked for by rebuilding the index where no section read before it indexes it, rather than taken from an older section's row.` Object 3 again gives inChainIndex=False, and WasRepaired turns True once it is asked for.
(c) Oldest is a stream with /Index [1 4] holding 3 rows. Output: `... was read only in part: it holds 3 rows where its /Index and /Size declare 4; the rows it holds were kept, and the objects whose rows it lacks are looked for ...`. Object 3 gives inChainIndex=False.
The committed test CrossReferenceChainTests.A_table_read_in_part_past_a_stream_that_refused_every_number_is_said_to_leave_every_object_to_a_rebuild (:2282-2314) pins (a)'s 'the rows read were kept'. The same test asserts ChainIndex.TryGet(3) is false, and its comment says 'None of its rows stands'.
The problem is new in this batch. Main 21a0298 has no hand-on (#208), so the older section is never read there.
FIX (reviewer): In ReportSectionReadInPart, check _everyNumberRefused first, before the stream, Incomplete and RefusedRow branches. At report time it holds only what earlier sections refused, because RefuseRowsLost runs after the report (:3249 then :3275, :3432 then :3436). When it is set, write a tail such as '; none of its rows stands, a newer section having refused every number to the older sections' rows, and every object no section read before it indexes is looked for by rebuilding the index'. Keep 'the rows read were kept' for _rowsLostPast and SubsectionsUnknown, the section's own refusals. Make the same change to ReportLostSection's 'The rows read before the fault, if any, were kept' when the refusal came before. Add 'unless a newer section refused every number, when none stands' to reference/diagnostics.md:112 and PdfDiagnosticCodes.cs:61. Update the test at :2305, and add rows for shapes (b) and (c).
--- VERDICT REASON: The finding holds on 761ce0b. ReportSectionReadInPart (src/AdCodicem.Pdf/IO/PdfFileReader.cs:4144-4200) and ReportLostSection (:4008-4065) say that a section's rows were kept. When a newer cross-reference stream has already set _everyNumberRefused, though, AddFromChain (:6616-6621) drops every row of the older section through RefusedToOlderRows (:3873-3877), so none is kept.

The clearest case is the classic-table Incomplete branch (:4165-4166). It picks its 'every other object' tail because _everyNumberRefused is set, so it knows about the newer refusal, and it still says 'the rows read were kept'. That contradicts the stream's report just before it, and it contradicts the index (object 3 is not in it and is rebuilt when asked for). The test that pins this text says the opposite in its own comment ('None of its rows stands') and asserts that ChainIndex.TryGet(3) is false.

The refused-row branch (:4174-4176), the stream branch (:4150-4158) and ReportLostSection's !NumbersRefused sentence (:4038) never look at _everyNumberRefused. In this situation they say 'that row alone was refused', 'the rows it holds were kept' and 'the objects only the rest indexes are looked for', and none of these holds.

Nobody has settled this: decisions.md ('For round 2: a section read in part after a newer section refused every number still says the rows read were kept') and R1c's deviation and open point pass it to round 2. It is new in this batch. Main 21a0298 has no hand-on (#208), no RefuseDeclared and no _everyNumberRefused, so the chain ends at the lost stream and never reads an older section.

Scope note: the same overstatement shows up, partly, when a newer stream refuses only a range that covers one of the rows (/Index [3 1]). There the newer stream's own message names the numbers it refuses ('the objects it numbers'), so the convention R1c described ('kept' meaning not dropped for the section's own fault) can stand. Under an every-number refusal, no reading makes 'kept' true.

Only the report text is wrong. The object served is correct (it is rebuilt on demand), so this is minor, not major. The docs part (the XML doc and the reference row) is a local generalization. Both pages, read whole, already say that no older row stands for a refused number, so the docs part alone would be a nit.
VERDICT EVIDENCE: I wrote my own probe, ZzRefuteRowsKept, on a detached 761ce0b and built it in Release with 0 warnings. Its source is saved at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute-rowskept-r2/ZzRefuteRowsKept.cs.txt and its output at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute-rowskept-r2/rows-kept.txt.

Each file is built as: newest table, its /Prev to lost stream 6 `/Index {X} /W [1 -4 2] /Prev oldest`, then the oldest section, which holds object 3. Each shape was run with three values of X: /X, which refuses every number; [5 1], a control; and [3 1].

(a) Oldest is a table whose row 4 ends in x. With /X:
`Warning xref.section-unreadable at 169: ... was read only in part: the row for object 4, at offset 258, is not an offset, a generation and n or f; the rows read were kept, and every other object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows.`
Object 3 gives inChainIndex=False and WasRepaired=True once it is read. With the [5 1] control, object 3 gives inChainIndex=True and WasRepaired=False.

(b) Oldest row 4 has generation 70000. With /X: `...; that row alone was refused, the rows after it read, and its object is looked for by rebuilding the index...`, and object 3 gives inChainIndex=False and WasRepaired=True. The control keeps object 3.

(c) Oldest is a stream, /Index [1 4], holding 3 rows. With /X: `...; the rows it holds were kept, and the objects whose rows it lacks are looked for...`, and object 3 gives inChainIndex=False. The control keeps object 3.

(d) A further path, ReportLostSection. The oldest table is Malformed (its rows run into `junk junk junk\nmore`). With /X: `... is there but cannot be read: the row for object 4, at offset 258, is not an offset, a generation and n or f. The rows read before the fault, if any, were kept; the objects only the rest indexes are looked for by rebuilding the index.` Object 3 gives inChainIndex=False and WasRepaired=True. The control keeps object 3.

The [3 1] runs print the same section texts while row 3 is refused, which is the partial-range overstatement in the scope note.

Code:
- AddFromChain :6618 returns before indexing whenever RefusedToOlderRows holds, and that is always true under _everyNumberRefused (:3875).
- ReportSectionReadInPart runs before RefuseRowsLost on the read path (:3249 then :3275; :3432 then :3436), so at that point _everyNumberRefused comes only from newer sections. On the lost /XRefStm path, RefuseRowsLost runs first (:3400 then :3404).
- The test at tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:2282-2314 asserts 'the rows read were kept' (:2305) and also `ChainIndex!.TryGet(3)...BeFalse()` (:2310). Its comment (:2287) says 'None of its rows stands'.
- Commit 0fc7ce5's body says 'says every number was refused where it was'.
- On main 21a0298, `git grep` finds no _everyNumberRefused, no HandOnPrevious and no 'read only in part'. Only ReportLostSection's 'The rows read before the fault, if any, were kept' exists there, and it is unreachable past a lost stream.
FIX (refuter): No maintainer decision is needed; this is a wording fix under the batch's rule that a message says what was lost.

1. In ReportSectionReadInPart, test a newer section's refusal of every number first, before the stream, Incomplete and RefusedRow branches. On this path it is simply _everyNumberRefused, because RefuseRowsLost runs after the report. Give it its own tail, for example: '...; none of its rows stands, a newer section having refused every number to the older sections' rows, and every object no section read before it indexes is looked for by rebuilding the index'. Keep RowsLeftToGuard's guard clause where it applies. Keep 'the rows read were kept, and every other object...' for _rowsLostPast and for an /XRefStm-named SubsectionsUnknown, which are the section's own refusals.

2. In ReportLostSection, change two sentences when every number was refused before the section was reached: the !NumbersRefused sentence ('The rows read before the fault, if any, were kept; the objects only the rest indexes...') and the Table+NumbersRefused one.
   - On the /Prev path, _everyNumberRefused at report time can only be a newer section's.
   - On the lost /XRefStm path, read `var refusedBefore = _everyNumberRefused;` before RefuseRowsLost (:3400) and pass it in, so that a table that refused everything itself still says its rows read were kept.
   - A stream that is handed on keeps no rows, so its sentences stay as they are.

3. Tests:
   - Change :2305 to the new tail.
   - Add rows that pin the full message, with ChainIndex.TryGet(3) false and WasRepaired turning true, for (b) the refused-row table, (c) the short stream, and (d) the Malformed /Prev table, each past the /Index /X stream.
   - Add the [5 1] control row, which keeps the old text and object 3, so that a mutant that drops or negates the new condition is killed both ways.

4. Docs: in docs/website/docs/reference/diagnostics.md:112 ('The rows read are kept') and src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:61 ('The rows read were kept'), add 'unless a newer cross-reference stream refused every number, when none stands, as the message says'.

Optional, and can stay under R1c's convention: the partial-range case ([3 1]) could add 'but for those a newer section refused'. The newer stream's report already names those numbers.

######## [20] docs:chain-cycle-named-offset-never-named — reviewer minor; refuter narrowed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3148-3159; src/AdCodicem.Pdf/Validation/Rules/ChainLoopRule.cs:39-43; docs/website/docs/reference/diagnostics.md:114; tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:3411-3412
CLAIM: Since R1a and R1b, the chain records where a section it reached but could not read starts. That covers a lost stream it went on past, whether named through the white space before it or found moved. A /Prev that loops back there is reported as 'names offset X, which the chain has already named', and the validator's xref.chain-loop says the same. But X was never named: the chain named another offset and reached the section through white space or by relocation. The reference page says 'The message says whether the chain read the section, or only named it'. It lists a section 'that is there and could not be read' as a third kind of place, which the message folds into 'named' and then describes falsely.
EVIDENCE: Probe ZzRelocatedLinksReports, on 761ce0b. The source is saved at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r2docs-out/ZzDocsRound2ProbeB.cs.txt and the output at r2docs-out/relocated-links.txt. Stream 5 sits at 155: `/Type /XRef /Size 10 /Index [3 1] /W [1 -4 2]`. The newest table names it through /Prev.
(1) Named at 148, 7 bytes short, with its own `/Prev 155`. Output: `Repair xref.offset-adjusted at 155: The cross-reference section /Prev names at offset 148 was found 7 bytes after it.`, then `Warning xref.chain-cycle at 155: The cross-reference chain loops back on itself: the /Prev of the section at offset 155 names offset 155, which the chain has already named.`, then `finding Error xref.chain-loop offset 155: The /Prev of the cross-reference section at offset 155 names offset 155, which the chain has already named: the chain loops.` Only 148 had been named.
(2) Named at 154, the end of line before the header, with its own /Prev 155. Output: `... the /Prev of the section at offset 154 names offset 155, which the chain has already named.` Only 154 had been named.
(3) Named at 155, with its own /Prev 154. Output: `... names offset 154, which the chain has already named.`
The same probe on main 21a0298 (worktree since removed, output at r2docs-out/relocated-links-main.txt) reports no loop in any of these shapes. Shape (1) gives `xref.section-missing at 148`. Shapes (2) and (3) give `xref.section-unreadable` alone, so the wording is new with the round 1 fixes.
CrossReferenceChainTests:3411-3412 pins 'which the chain has already named' for both of its rows, (1,0) and (0,1).
FIX (reviewer): Make the loop record three-valued: read, reached but not read (a section recorded there with State Malformed, or a lost or handed-on stream), or only named. The reader should say for example 'where the chain reached a section it could not read', and ChainLoopRule 'a section the chain has already reached and could not read'. Keep 'which the chain has already named' for an offset that was only named (CrossReferenceRuleTests.A_loop_through_an_offset_the_chain_only_named_says_no_section_was_read_there). Update the test at :3411, reference/diagnostics.md:114 and validation-rules.md:59 so that the message's three cases are named.
--- VERDICT REASON: The finding holds, but more narrowly than stated. At 761ce0b the loop message is two-valued. `read` is true only when the record at the looped position is Read or Relocated (PdfFileReader.cs:3148-3151, FileStructure.LoopOffsetRead), and every other case gets "which the chain has already named". ChainLoopRule.cs:39-43 does the same. Since R1a, a lost stream that declares itself a section is recorded as reached, and since R1b one found moved is too. A /Prev that loops back to such a stream through a different offset is now reported as "names offset X, which the chain has already named", but no link ever named X. The chain named another offset and reached the section through the white space before it, or by finding it moved. The validator's Error finding repeats the false clause. Where the text is false: the looping /Prev's offset differs from every offset the chain named for that lost section (relocation, or white space before the header in either direction). Where it is true: the looping /Prev names the same offset the chain named for the lost stream (the control rows below, and test :792's newest=false row). The same shapes with a readable stream say "already read" and are fine. Main reports no loop in any lost shape, so this text is new with the round 1 fixes. Round 1's refuter of [0] flagged this wording as an open choice. decisions.md settled only that lost streams count as reached, not how the loop message words them, so nothing settles it. The reader comment at :3138-3143 ("a section reached and lost" is "a loop through an offset the chain only named") and FileStructure.LoopOffsetRead's doc ("rather than only naming it there") encode the same conflation. Partial pushback on the docs: validation-rules.md:59 already lists the three cases correctly and does not misstate the message. diagnostics.md:114's "The message says whether the chain read the section, or only named it" only calls a reached-and-lost section "only named", which is loose rather than false. Behavior, code, severity and position are right. Only the text misleads, and a test pins it. So minor (a misleading diagnostic and validator text), not major.
VERDICT EVIDENCE: I rebuilt the reproducer myself as probe ZzRefuteLoopNamedShapes, uncommitted and since deleted. Output saved at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute-loop-named.txt (branch 761ce0b) and refute-loop-named-main.txt (main 21a0298). Shape: stream 5 at 155 `/Type /XRef /Size 10 /Index [3 1] /W [1 -4 2] /Prev {own}`, with the newest table's /Prev naming it.
Branch 761ce0b:
- Named 148 (found moved), own /Prev 155: `Warning xref.chain-cycle at 155: ... the /Prev of the section at offset 155 names offset 155, which the chain has already named.` and `finding Error xref.chain-loop: The /Prev of the cross-reference section at offset 155 names offset 155, which the chain has already named: the chain loops.` Only 148 was named.
- Named 154, own /Prev 155: `... the /Prev of the section at offset 154 names offset 155, which the chain has already named.` Only 154 was named.
- Named 155, own /Prev 154: `... names offset 154, which the chain has already named.` Only 155 was named.
- Controls, where the text is true: named 155 with own /Prev 155 gives `names offset 155, which the chain has already named`, and named 154 with own /Prev 154 gives `names offset 154, which the chain has already named`.
- The same three shapes with a readable stream say `which the chain has already read` and `a section the chain has already read`, which is accurate.
Main 21a0298, lost shapes: no xref.chain-cycle or xref.chain-loop in any (LoopOffset=-1). The relocated shape gives `xref.section-missing at 148`; the others give xref.section-unreadable and xref.section-malformed only.
Pins: CrossReferenceChainTests.cs:3411-3412 pins the false text for rows (1,0) and (0,1). Its comment at :3404-3406 calls the stream "a section the chain named and could not read", which shows the intent: "named" was meant to mean the section, but the clause attaches to the offset.
FIX (refuter): Make the loop record three-valued:
1. Replace FileStructure.LoopOffsetRead with a state: read, reached but not read, or only named. "Reached but not read" means the record at `looped` in sectionsAt is Malformed: a lost or handed-on stream that declares itself a section.
2. Reader message (PdfFileReader.cs:3159): read → "which the chain has already read"; reached but not read → "where the chain has already reached a section it could not read" (true for the exact-offset case too); otherwise keep "which the chain has already named" and its outside-the-file variant.
3. ChainLoopRule: add "a section the chain has already reached and could not read".
4. Update the reader comment at :3138-3143 and LoopOffsetRead's XML doc.
5. Tests: change CrossReferenceChainTests.cs:3411-3412 to the new text. Change the newest=false row at :792 too, or keep "named" there if the exact case should stay as it is. Add the relocated shape (148 → 155, own /Prev 155) as a row. Keep CrossReferenceRuleTests' A_loop_through_an_offset_the_chain_only_named_says_no_section_was_read_there unchanged.
6. diagnostics.md:114: say the message tells whether the chain read the section, reached it and could not read it, or only named the offset.
No maintainer question is needed: this is a wording fix that brings the text in line with the three cases the reference page already lists. Round 1 left the choice open but did not decide it.

######## [21] docs:checksum-zero-doc-elliptical — reviewer nit; refuter narrowed nit maintainer=False
LOCATION: src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:273-274; docs/website/docs/reference/diagnostics.md:134
CLAIM: The public XML doc of FilterChecksumMismatch and the reference row add 'Data that decoded to nothing is said to have, and nothing more.' The ellipsis 'is said to have' has no participle. The nearest predicate is 'was kept, and some of it may be wrong', so the sentence can read as saying such data is said to have been kept, the opposite of the message. The concepts page uses the clear form.
EVIDENCE: `grep -n 'is said to have'` finds PdfDiagnosticCodes.cs:274 and reference/diagnostics.md:134, each right after 'all of it was kept ... and some of it may be wrong.' The code's message (PdfFilterPipeline.cs:678) is 'A Flate stream's checksum disagrees with its data, which decoded to nothing.' concepts/diagnostics.md:133 says 'of data that decoded to nothing, the report says only that.'
FIX (reviewer): In both places write 'Of data that decoded to nothing, the report says only that it did.'
--- VERDICT REASON: The wording point holds, but the claimed misreading is overstated. At 761ce0b the sentence "Data that decoded to nothing is said to have, and nothing more." appears in two places:
- src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:273-274, the public XML doc of FilterChecksumMismatch;
- docs/website/docs/reference/diagnostics.md:134, the filter.checksum-mismatch row.

Commit 761ce0b added it. It copies, word for word, an optional open point the round 1 fix stage left for the docs stage (r1-fix-result.json, open_points: "could add that data that decoded to nothing is said to have, and nothing more"). The same commit gave the concepts page (concepts/diagnostics.md:133) a clearer form: "of data that decoded to nothing, the report says only that."

As English, the sentence is grammatical verb-phrase ellipsis. Its antecedent is the subject's own relative clause: "is said to have [decoded to nothing]". After "have", the elided phrase must match a participle form, so "decoded to nothing" is the reading the grammar picks. The reading "said to have been kept" would need a "been" that is not there.

Even read that way, it is not "the opposite of the message". For decoded == 0 the decode returns empty data, so nothing was dropped, and "kept" is vacuous rather than contrary.

Nor does the sentence misstate the code. For decoded == 0, PdfFilterPipeline.cs:677-678 says only "A Flate stream's checksum disagrees with its data, which decoded to nothing." It drops the "all were kept, and some may be wrong" clause, which is what "and nothing more" says.

What remains is a terse, hard-to-parse sentence in a public XML doc and a reference row. A clearer form already exists on the concepts page. That is a wording nit, not a misstatement. Finding [28] of round 1 is not being re-reported: it was fixed in de0fa05. This finding concerns only the doc wording that 761ce0b added for it.
VERDICT EVIDENCE: On origin/ccr-6d6c0ee6-dqm4lm-188 at 761ce0b, `grep -rn 'is said to have' src docs tests` finds only:
- src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:274: "/// is said to have, and nothing more."
- docs/website/docs/reference/diagnostics.md:134: "... all of it was kept, and some of it may be wrong. Data that decoded to nothing is said to have, and nothing more |"

`git log -S'is said to have' 21a0298..HEAD` names only 761ce0b, which also added concepts/diagnostics.md:133 "of data that decoded to nothing, the report says only that."

Code at PdfFilterPipeline.cs:674-681: `decoded == 0 ? "A Flate stream's checksum disagrees with its data, which decoded to nothing." : ... {(decoded == 1 ? "it was kept, and may be wrong" : "all were kept, and some may be wrong")}`. The zero case drops the kept/wrong clause, so "nothing more" is accurate.

Source of the wording: r1-fix-result.json open_points, "Optional, for R1f, docs/website/docs/reference/diagnostics.md:134 (filter.checksum-mismatch row): ... could add that data that decoded to nothing is said to have, and nothing more."

No test reads the reference row against the XML doc (grep for diagnostics.md in tests finds none). Changing the wording therefore touches no test.
FIX (refuter): In PdfDiagnosticCodes.cs:273-274 and reference/diagnostics.md:134, replace "Data that decoded to nothing is said to have, and nothing more" with "Of data that decoded to nothing, the report says only that it decoded to nothing" (or the finding's "...says only that it did"). Optionally, change concepts/diagnostics.md:133 "the report says only that" to "...only that it did", so that "that" cannot be read as pointing back to the kept/wrong clause. This is a docs-only change with no test impact. No maintainer decision is needed.

######## [22] docs:reserved-rebuild-moments-incomplete — reviewer nit; refuter confirmed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/Validation/Rules/ReservedTypeRule.cs:19-20; docs/website/docs/reference/diagnostics.md:117; docs/website/docs/reference/validation-rules.md:55; docs/website/docs/concepts/lazy-reading.md:90-91; docs/threat-model.md:427
CLAIM: Five texts say a reserved row reads as null until the index is rebuilt for another fault, 'at opening, or as the validation settles it'. That list leaves out the rebuild set off when an object asked for after opening is neither where its entry says nor near it. That rebuild also flips ReservedTypeRule's clause, because the clause keys on WasRepaired.
EVIDENCE: Probe ZzReservedRebuiltOnDemand, on 761ce0b (output at r2docs-out/reserved-on-demand.txt). Stream 5 gives object 3 a row of type 3, and object 4 a row pointing 900 bytes into a padding string. Output: `WasRepaired at opening: False`, then `object 4: four WasRepaired: True` with `Repair xref.rebuilt`. Validating then gives `Warning xref.entry-type-reserved: ... (7.5.8.3), as the reader did until it rebuilt the index by scanning the file; it now reads each object such a row numbers as the rebuild finds it.`, and object 3 reads 'three'.
FIX (reviewer): Name the third moment: 'until the index is rebuilt for another fault — at opening, when an object asked for is not where its entry says nor near it, or as a validation settles it —'. Or drop the list, as M02.md:113 and PdfDiagnosticCodes.cs:115 already do.
--- VERDICT REASON: The finding holds, and the gap is wider than the reviewer says: two moments are missing, not one. Five texts say a reserved row reads as null until the index is rebuilt for another fault, and name only two moments for that rebuild: at opening, and when a validation settles the index. The reader rebuilds at two more moments, both on a read after opening. LoadObject calls Repair() when an object the index lacks is asked for while the index may have lost entries (PdfFileReader.cs:6858-6860). It also calls Repair() when an object is neither where its entry says nor within 512 bytes of it (PdfFileReader.cs:6967). The reviewer named only the second. Either rebuild flips the reserved row's object from null to the scanned definition with no validation at all. ReservedTypeRule's code is correct, since it keys on WasRepaired whenever the rebuild ran. Only its remark and the four docs misstate when that happens. lazy-reading.md lists all four moments at lines 97-131, just below the paragraph at 86-92 that names only two, so that page contradicts itself. The wording is new in round 1's fix for [22] (0e974e6, 761ce0b; `git log -S` finds none of it at 071f829), and [22]'s refuter verdict already listed only the opening and the validator's settle. So the gap was carried in by the fix this round reviews hardest. I raise it from nit to minor. The text states when a served object changes value, and a plain read falsifies it: a caller who reads only diagnostics.md:117 would expect object 3 to stay null until validation, yet reading object 4 or object 9 changes it. The main claim ("until the index is rebuilt for another fault") is right, and the rule's message is right whatever the moment, so it is no higher than minor.
VERDICT EVIDENCE: I wrote my own probe, ZzRebuildMoments, on 761ce0b (detached origin/ccr-6d6c0ee6-dqm4lm-188, Release build, probe since removed). Output is at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute-rebuild-moments/out.txt.

(A) Trigger: an object the index lacks, asked for while the index may have lost entries. File ReservedRowFiles.Updated(type: 3, more: 2, missing: 1), no validation:
"at opening: WasRepaired=False; object 3 = null"
"object 9 = null; WasRepaired=True; object 3 now = three"
Diagnostics: "Warning xref.section-unreadable at 381: ... was read only in part ...", "Warning xref.entry-reserved at 381: ...", "Repair xref.rebuilt at -1".
Validated after the same read: "finding Warning xref.entry-type-reserved: ... (7.5.8.3), as the reader did until it rebuilt the index by scanning the file; it now reads each object such a row numbers as the rebuild finds it."

(B) Trigger: an object neither where its entry says nor near it. This is the reviewer's shape: object 3's row has type 3, and object 4's row points 900 bytes into a padding string. No validation:
"at opening: WasRepaired=False; object 3 = null"
"object 4 = four; WasRepaired=True; object 3 now = three"
Diagnostics: "Warning xref.entry-reserved at 1673: ...", "Repair xref.rebuilt at -1".
This matches the reviewer's r2docs-out/reserved-on-demand.txt.

Controls (ask object 1, which is sound): WasRepaired stays False and object 3 stays null until validation. The validation's settle then rebuilds, so validation alone does flip it, as documented.

Misstating texts at 761ce0b:
- ReservedTypeRule.cs:19-20 ("— at opening, or as the validation settles the index before its first rule —")
- diagnostics.md:117 ("at opening or as a validation settles it")
- validation-rules.md:55 ("— at opening, or as the validation settles the index before its first rule —")
- lazy-reading.md:90-91 ("at opening or as a validation settles it"); the same page's list at lines 97-131 names four moments
- threat-model.md:427 ("where the index was rebuilt, at opening or as the validation settles it")

M02.md:112-113 and PdfDiagnosticCodes.cs:115-116 say only "until a rebuild / the index is rebuilt for another fault", which is correct. No test covers a rebuild set off by a read after opening before validation. CrossReferenceChainTests.A_section_read_in_part_reports_its_rows_of_a_reserved_type_after_what_it_lost and CrossReferenceRuleTests.A_row_of_a_reserved_type_in_an_index_rebuilt_at_opening_... cover the settle and opening moments only.
FIX (refuter): This is a wording fix, not a design choice. In all five places, name the read moments or drop the list. Proposed: "until the index is rebuilt for another fault — at opening, by a read after it that asks for an object the index lacks while it may have lost entries or that finds an object neither where its entry says nor near it, or as a validation settles the index before its first rule —". Shorter alternative: "— at opening, by a later read, or as a validation settles it —", with lazy-reading.md pointing to its own list of when the map is rebuilt. Or drop the list, as M02.md:112-113 and PdfDiagnosticCodes.cs:115-116 do. Apply it to ReservedTypeRule.cs:19-20 (XML remark), diagnostics.md:117, validation-rules.md:55, lazy-reading.md:90-91 and threat-model.md:427. Optionally add a test: ReservedRowFiles.Updated(type: 3, more: 2, missing: 1), read object 9 before validating, then assert WasRepaired is true, object 3 reads "three", and the finding carries the "as the reader did until it rebuilt the index" clause. No code change is needed, since the rule keys on WasRepaired.

######## [23] docs:tm-size-wrong-loop-missing — reviewer nit; refuter confirmed nit maintainer=False
LOCATION: docs/threat-model.md:617
CLAIM: The row on SizeWrongRule names 'a right /Size reported' as its threat. Its defense lists the chain whose /XRefStm names no stream as not judged, but not a chain that ends in a loop. Commit 0fc7ce5 added that exclusion (R1-Q3), and validation-rules.md, M02.md and SizeWrongRule's remarks all state it. The test that holds it is cited nowhere in the threat model.
EVIDENCE: SizeWrongRule.cs:48 reads `if (!structure.ChainRead || structure.ChainCutAt >= 0 || structure.LoopOffset >= 0 || !IsWhole(sections))`. `grep -c 'A_chain_that_ends_in_a_loop_has_no_size_judged' docs/threat-model.md` gives 0. `grep -n 'SizeWrongRule\|file.size-wrong' docs/threat-model.md` matches only row 617, which has no loop clause.
FIX (reviewer): Add to row 617: 'and a chain that ends in a loop, the section the last /Prev should have named unknown, is not judged ([#156])'. Cite CrossReferenceRuleTests.A_chain_that_ends_in_a_loop_has_no_size_judged_the_section_it_lost_may_number_the_objects_of in its Held-by column.
--- VERDICT REASON: The finding holds at 761ce0b. SizeWrongRule.cs:48 returns early when `structure.LoopOffset >= 0`; commit 0fc7ce5 added this under R1-Q3. The rule's remarks, validation-rules.md:47 and M02.md:110 all describe this exclusion. The threat model does not mention it anywhere. Row 617 is the only row that names SizeWrongRule or file.size-wrong. Its threat includes "a right /Size reported", which is exactly the false positive the loop guard removes. Its defense names only the hybrid exclusion, the /XRefStm that names no stream. The loop row (414) says nothing about /Size either. 0fc7ce5 edited threat-model.md, but only the chain rows (413-422); the docs stage after it never touched row 617. Nothing is misstated: the row does not claim to list every case where the rule stays silent. It also leaves out the #207/#210 in-part exclusions, which appear only as test names in row 421's Held-by. That makes this an incomplete defense list, not a wrong one. The loop exclusion is tagged #156 in the commit and in the remark, the same issue as row 617, so row 617 is where it belongs. No behavior is affected, and no maintainer choice is involved.
VERDICT EVIDENCE: At origin/ccr-6d6c0ee6-dqm4lm-188 (761ce0b):
- `src/AdCodicem.Pdf/Validation/Rules/SizeWrongRule.cs:48`: `if (!structure.ChainRead || structure.ChainCutAt >= 0 || structure.LoopOffset >= 0 || !IsWhole(sections))`. The remark at lines 26-31 says "that ends in a loop — which leaves the section the last /Prev should have named unknown ... (#156) —, ... is not judged."
- `grep -n 'SizeWrongRule\|file.size-wrong\|size-wrong' docs/threat-model.md` matches only line 617. That row's defense ends "a chain where a table's /XRefStm names no stream lost that stream, and is not judged." It has no loop clause.
- `grep -c 'A_chain_that_ends_in_a_loop_has_no_size_judged' docs/threat-model.md` returns 0. The test is at tests/AdCodicem.Pdf.Tests/CrossReferenceRuleTests.cs:1068.
- Row 414 ("A /Prev chain that loops") has no /Size clause.
- `docs/website/docs/reference/validation-rules.md:47` says "or ends in a loop, which leaves the section the last /Prev should have named unknown ... ([#156])".
- `docs/milestones/M02.md:110` says "nor a chain that ends in a loop ([#156])".
- `git show 0fc7ce5 -- docs/threat-model.md` changes only the chain rows near line 413. Round 1's refuter fix for [25], in r1-findings.md, asked for the remark, a test and validation-rules.md, not the threat model.
FIX (refuter): In docs/threat-model.md:617, add a loop clause to the defense after the /XRefStm clause, for example: "...; a chain where a table's /XRefStm names no stream lost that stream, and a chain that ends in a loop leaves the section the last /Prev should have named unknown, its objects among those a /Size may count: neither is judged ([#156])". Add `CrossReferenceRuleTests.A_chain_that_ends_in_a_loop_has_no_size_judged_the_section_it_lost_may_number_the_objects_of` to the row's Held-by column, after the existing CrossReferenceRuleTests names. This is a documentation-only change; no code changes.
