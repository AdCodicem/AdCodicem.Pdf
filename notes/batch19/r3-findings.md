######## [0] chain:keeps-its-place-does-not-mirror-the-prev — reviewer major; refuter narrowed major maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3652 (KeepsItsPlace) and :3667-3700 (DeclaresStreamAt, through DeclaresOnlyCrossReferenceStream :6468), against the /Prev's own decision at :3292; docs/website/docs/concepts/lazy-reading.md:120
CLAIM: Round 2's [0] fix is incomplete. It stops the /XRefStm's search at leaveAt only where the /Prev 'keeps its place', but KeepsItsPlace does not answer as the /Prev's own reading does. The /Prev keeps its place where it reads, where a guard cut it, or where it finds the keyword or /Type /XRef (:3292, DeclaresItselfASection). The fix gets this wrong both ways. (A) An object that gives /W and no /Type, such as a border-style dictionary << /W 1 /S /S >> or an untyped /W stream whose rows cannot be read, counts as keeping its place. The /Prev's own reading takes it for a guess (B19-1, #263) and searches near it. So the /XRefStm is left to a /Prev that then takes another section, its stream is never read, and an older revision is served with a false hybrid-stream-lost and a false hybrid-stream-wrong Error. That is the exact class of round 2's [0], and main reads the stream. (B) An object of another /Type that reads as a cross-reference stream where the /Prev names it (#263) does keep its place, but DeclaresStreamAt answers no. The /XRefStm's search then passes it and reads the oldest table in the stream's place. The /Prev reads the object and loops back to that table, so the older rows win, with a false chain-cycle and a chain-loop Error. 761ce0b served the new revision there. lazy-reading.md:120 ('keeps its place there — it names a section there, not a font or a string') and KeepsItsPlace's remark ('an object that declares a cross-reference stream and no other type') state the /W-only case against B19-1.
EVIDENCE: Probe ZzR3Chain (source saved at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r3chain/ZzR3Chain.cs.txt), run on 19122f0, 761ce0b and 21a0298.

P1b file:
- oldest table at 1415, indexing 1-3 and 7 '(old seven)';
- object 4 at 1623 = '<< /W 1 /S /S >>';
- object 8 at 1673, whose string the /XRefStm names at 1693;
- objects 5 and 7 '(new seven)';
- stream 9 (/Type /XRef, /Index [5 1 7 1]) at 1901;
- newest table at 3237: /Prev 1623 /XRefStm 1693.

Branch 19122f0:
- D xref.hybrid-stream-lost @1693: '...names offset 1693, what its /Prev names too, which the chain looks for through that /Prev...'
- D xref.offset-adjusted @1415: 'The cross-reference section /Prev names at offset 1623 was found 208 bytes before it.'
- Sections: /XRefStm NamedAgain readAs=/Prev@1415; /Prev Relocated Table 1415. The stream is never read.
- GetObject(7) = old seven, WasRepaired at opening False.
- F xref.hybrid-stream-wrong [Error] '...the section its /Prev names too, which the chain read through that /Prev...: what only that stream indexes is lost.'

Same file with '<< /Type /Border /W 1 /S /S >>' on the branch:
- /XRefStm Relocated Stream 1901, /Prev Relocated Table 1415;
- GetObject(7) = new seven, no rebuild, no hybrid-stream-*.

Main 21a0298, /W shape: /XRefStm Relocated Stream 1901, /Prev Relocated 1415, GetObject(7) = new seven. 761ce0b: old seven for both shapes, which is round 2's [0].

In HybridFiles.NamingAnObjectBesideTheStream's layout, an untyped '<< /W [1 -4 2] /Size 10 /Length 0 >> stream' or '<< /W 1 /S /S >>' as object 4 gives on the branch:
- hybrid-stream-lost;
- '/Prev names at offset 1874 was found 242 bytes before it', taking the stream at 1632 as the older section;
- the oldest table never read, xref.rebuilt at opening;
- F hybrid-stream-wrong [Error].
The typed control reads the stream and the oldest table with no rebuild.

P2 file:
- oldest table at 213, with 7 '(old seven)';
- 7 '(new seven)' at 380;
- object 4 at 407 = '<< /Type /XRefStream /Size 10 /W [1 4 2] /Index [7 1] /Prev 213 /Length 7 >> stream <row 7 -> 380>';
- newest table at 1747: /Prev 407 /XRefStm 405.

Branch:
- D xref.offset-adjusted @213: '/XRefStm names at offset 405 was found 192 bytes before it'
- D xref.hybrid-stream-lost: '...a cross-reference table, whose rows were read in the stream's place'
- /Prev Read Stream 407
- D xref.chain-cycle @213: '...the /Prev of the section at offset 407 names offset 213, which the chain has already read.'
- GetObject(7) = old seven
- F xref.chain-loop [Error], F xref.hybrid-stream-wrong [Error]

761ce0b: /XRefStm NamedAgain readAs=/Prev@407, then /Prev 213 Read, GetObject(7) = new seven, no loop. Main: old seven, with only section-shifted.
FIX (reviewer): Make KeepsItsPlace answer as the /Prev's reading at :3292 does:
- the keyword;
- a dictionary that gives /Type /XRef;
- a dictionary a guard cut before it said what it is;
- for an object that gives a /W, untyped or of another /Type, only where it reads as a section. Read its rows without indexing them, through a rollback mark on PdfXRefTable, which #320's designs need too, or an equivalent dry read.

Correct KeepsItsPlace's remark and lazy-reading.md:120 to match.

Add rows to An_xrefstm_stops_short_of_its_stream_only_where_its_table_s_prev_keeps_its_place:
- '<< /W 1 /S /S >>' and an untyped /W stream that cannot be read: the search goes on to the stream, 7 = new seven, no hybrid-stream-wrong;
- a /Type /XRefStream stream that reads: the search stops, the /Prev reads it, 7 = new seven, no chain-loop.

Each row must fail on 19122f0.
--- VERDICT REASON: Both halves reproduce with my own builder, run on 19122f0, 761ce0b and 21a0298. Round 2's [0] decision says the /XRefStm's search stops at leaveAt "only where the /Prev would keep its own place (it reads, a guard cut it, or it declares itself a section there)". KeepsItsPlace (PdfFileReader.cs:3652) does not test that. It reads only the dictionary, through DeclaresStreamAt (:3667) and DeclaresOnlyCrossReferenceStream (:6468), so it cannot tell whether a /W object reads. The /Prev's own decision at :3292 depends on exactly that, because DeclaresItselfASection covers only the keyword and /Type /XRef (B19-1).

(A) An untyped /W object whose rows do not read counts as keeping its place. The /Prev's reading takes it for a guess and looks nearby (B19-1, #263), so the stream is never read. This is round 2's [0] class left in place for one shape: it is still a regression against main, and the branch matches 761ce0b. It serves an older revision with no rebuild, plus a false hybrid-stream-lost Warning and a false hybrid-stream-wrong Error. That is major on the brief's scale (wrong object served, decision not implemented). The branch is not worse than 761ce0b here.

(B) A stream of another /Type that gives a /W and reads where the /Prev names it does keep its place, but DeclaresStreamAt answers no. DeclaresStreamAt's remark admits this: "though one that reads as a cross-reference stream where the chain names it is read there". This half is narrower:
- It regresses only against 761ce0b, which was never merged. Main serves the same old seven.
- Against main, what it adds is xref.chain-cycle, the chain-loop Error and the hybrid-stream-* reports. The loop comes from #330's mirror order (the /XRefStm reads a section, then a later /Prev reaches it).
- On its own it would be minor. It is still a departure from [0]'s decided rule.

On the docs:
- lazy-reading.md:120 states the decided criterion ("the /Prev keeps its place there"), so it is not wrong in itself. It misstates the code only because the code does not follow it.
- The texts that actually contradict B19-1 are KeepsItsPlace's summary and the leaveAt param doc. Both count "an object that declares a cross-reference stream and no other type" as declaring itself a section, which by B19-1 a /W-only object does not.
- No existing test covers either shape. The 'a dictionary of no type' and 'a stream of no type' rows of An_xrefstm_stops_short_of_its_stream_only_where_its_table_s_prev_keeps_its_place give no /W.

This is not a maintainer question. It implements [0] as decided.
VERDICT EVIDENCE: Probe: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute-keeps/ZzRefKeeps.cs.txt. Outputs out-branch.txt, out-761.txt, out-main.txt and out-branchC.txt are in the same folder. Each commit was built in its own worktree, and the worktrees were removed afterwards.

(A) The file:
- oldest table T0 at 1215, indexing 1-3 and 7 '(old seven)';
- object 4 at 1405, which /Prev names;
- object 8, a string, inside which /XRefStm names 1490;
- objects 5 and 7 '(new seven)';
- stream S at 1705 (/Type /XRef, /Index [5 1 7 1]);
- newest table at 2841.

Branch 19122f0, with object 4 = '<< /W 1 /S /S >>' (A1), or an untyped stream '<< /W [1 -4 2] /Size 10 /Length 0 >>' (A2):
- D xref.hybrid-stream-lost @1490 '...names offset 1490, what its /Prev names too, which the chain looks for through that /Prev...'
- D xref.offset-adjusted @1215 'The cross-reference section /Prev names at offset 1405 was found 190 bytes before it.'
- S /XRefStm NamedAgain readAs=/Prev@1215; S /Prev Relocated Table 1215. S is in no record.
- Get(7) = old seven, WasRepaired(open)=False. Get(5) only after a rebuild.
- F xref.hybrid-stream-wrong [Error] '...the section its /Prev names too, which the chain read through that /Prev...: what only that stream indexes is lost.'

Controls on the branch, '<< /Type /Border /W 1 /S /S >>' and '<< /S /S >>':
- /XRefStm Relocated Stream 1705, /Prev Relocated Table 1215;
- Get(7) = new seven, no rebuild, no hybrid-stream-*.

Main 21a0298 for A1 and A2:
- /XRefStm Relocated Stream 1705 ('found 215 bytes from there');
- Get(7) = new seven, no rebuild;
- only section-shifted x2 and file.size-wrong.

761ce0b: old seven plus the false reports for all four shapes, which is round 2's [0].

In HybridFiles.NamingAnObjectBesideTheStream's layout, A1 and A2 on the branch give:
- D xref.hybrid-stream-lost @1861;
- 'The cross-reference section /Prev names at offset 1891 was found 242 bytes before it' (it took the stream at 1649 as the older section);
- D xref.rebuilt at opening (WasRepaired(open)=True);
- F hybrid-stream-wrong [Error].
The typed control reads the stream and the oldest table (1481), with no rebuild.

(B) The file:
- T0 at 213 with 7 '(old seven)';
- 7 '(new seven)' at 380;
- object 8 string, inside which /XRefStm names 427;
- object 4 at 455 = '<< /Type /XRefStream /Size 10 /W [1 4 2] /Index [7 1] /Prev 213 /Length 7 >> stream <row 7 -> 380>';
- newest table at 1595.

Branch:
- D xref.offset-adjusted @213 '/XRefStm names at offset 427 was found 214 bytes before it'
- D xref.hybrid-stream-lost '...a cross-reference table, whose rows were read in the stream's place...'
- S /Prev Read Stream 455
- D xref.chain-cycle @213 '...the /Prev of the section at offset 455 names offset 213, which the chain has already read.'
- Get(7) = old seven
- F xref.chain-loop [Error], F xref.hybrid-stream-wrong [Error]

The same file with /Type /XRef on the branch, and the /XRefStream file on 761ce0b:
- /XRefStm NamedAgain readAs=/Prev@455, then /Prev Read 213;
- Get(7) = new seven, no loop.

Main, /XRefStream file:
- /XRefStm Relocated Table 213, /Prev Read 455 then 213;
- Get(7) = old seven, file.size-wrong x2 and section-shifted, no loop.
FIX (refuter): Make KeepsItsPlace answer as :3292 does:
- true for the xref keyword;
- true for a dictionary giving /Type /XRef;
- true for a dictionary a guard cut before it gave a /Type (as now);
- for any other object giving a /W (untyped, or of another /Type), true only where it reads as a section.

To tell 'reads' without indexing, use a dry read of the stream's rows: the same faults TryReadXRefStream checks (UnreadValue, the /W and /Index faults, FaultBesideCut, decoding, the data's end), with nothing added to the index. It runs at most once per hybrid table whose /XRefStm cannot be read and whose /Prev names such an object, so the section-count guard bounds it.

Prefer a 'validate only' mode of the row reader to a rollback mark on PdfXRefTable. Choosing that mark here would settle #320's open choice between two designs (Q12). If the implementer finds a dry read too large for this batch, the alternative is a maintainer question: file the residual under #320 and say the known shapes in KeepsItsPlace's remark and lazy-reading.

Correct KeepsItsPlace's summary and the leaveAt param doc. A /W-only object does not declare itself a section (B19-1): it keeps its place only where it reads.

Add rows to An_xrefstm_stops_short_of_its_stream_only_where_its_table_s_prev_keeps_its_place:
- '<< /W 1 /S /S >>' and an untyped /W stream whose rows cannot be read: the search goes on to the stream, 7 = new seven, no rebuild, no hybrid-stream-wrong;
- an untyped /W stream that reads: the search stops (NamedAgain, readAs the /Prev);
- a /Type /XRefStream stream that reads, its /Prev naming the oldest table: the search stops, the /Prev reads it then the oldest table, 7 = new seven, no chain-cycle and no chain-loop.

The rows for the two /W shapes that do not read, and for the typed-other stream that reads, must fail on 19122f0.

######## [1] chain:token-on-its-line-lexed-from-window-start — reviewer major; refuter confirmed major maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:4741 (IsTokenOnItsLine lexes from the window's first byte when the line starts before it), remark :4731-4734; test tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:1094 pins it; docs/website/docs/reference/diagnostics.md:110, reference/validation-rules.md:56, docs/threat-model.md:419, concepts/lazy-reading.md:172
CLAIM: Round 2's [2] was decided as: a Malformed table candidate declares itself only where its 'xref' is a token that a load lexes from its line's start, not a word in a comment or a string on that line. Where the line starts more than ~512 bytes before the place, IsTokenOnItsLine instead lexes from the start of the window. A comment whose '%' lies before the window, or a string whose '(' does, is then not seen, and the word xref in it is taken for the keyword. It becomes the section found moved and cannot be read. The chain ends there, every older section is lost, and the index is rebuilt at opening, which serves an object the update had freed. Main, and lines shorter than the window, drop the word and find the real table. The code remark admits the limit, but nothing is filed. The four docs state the rule with no such limit, and a mutation-killing test pins the window-start lexing.
EVIDENCE: P4b file:
- T0 at 212 indexes 1-4, with 4 '(old four)';
- then a comment line '%' + 700 'a' + ' xref see the table below';
- object 6 at 2304, then 60 spaces;
- T1 at 2385 frees 4 (gen 1), indexes 6, /Prev 212;
- the newest table's /Prev names 2320, inside object 6's endobj.

Branch 19122f0:
- D xref.offset-adjusted @2279: '...names at offset 2320 was found 41 bytes before it.'
- D xref.section-unreadable @2279: '...found at offset 2279, is there but cannot be read: it holds the keyword see at offset 2284, where a subsection or the trailer should start...'
- D xref.rebuilt; WasRepairedAtOpen=True
- GetObject(4) = old four
- F xref.section-malformed [Error] @2279

With a string object '(' + 700 'a' + ' xref see the table below)' in place of the comment: found at 2287, the same reports, GetObject(4) = old four.

Control with a 100-byte comment line, on the branch: '/Prev names at offset 1720 was found 65 bytes after it', GetObject(4) = null, no rebuild.

Main 21a0298, both 700-byte shapes: 'found 65 bytes from there' @2385 or @2401, GetObject(4) = null, WasRepaired False.

P4, the same without the deletion: the branch reports section-unreadable and rebuilds; main and the 100-byte control find T1 65 bytes after.
FIX (reviewer): Lex the line from its real start:
- read back from the window's start to the end of line before it, bounded (a constant or MaxTrailerLength, the bytes counted);
- where no end of line lies within the bound, take the place for no keyword and drop it, as main did. A conforming 'xref' begins its line, so only an invalid file loses.

The test at CrossReferenceChainTests.cs:1094 (the keyword after '] endobj' on an 860-byte line) keeps passing, since that keyword is a token when its line is lexed from its start. Rename it to what it holds.

Add the 700-byte comment and string rows to The_word_xref_in_a_comment_or_a_string_near_where_an_offset_names_no_section_is_no_section_found_moved, and to the deleted-object test.

If the limit is kept instead: file it as debt, and say it in diagnostics.md:110, validation-rules.md:56, threat-model.md:419 and lazy-reading.md:172.
--- VERDICT REASON: The finding holds as stated, and I rebuilt it myself.

**The code.** `IsTokenOnItsLine` (PdfFileReader.cs:4739-4751) starts its lexer at `span[..at].LastIndexOfAny('\r','\n') + 1`, inside the window only. When the line starts before `start - lead`, the lexer starts at the window's first byte. A `%` or a `(` that lies before that byte is never seen. The word `xref` then lexes as a token, so the Malformed table candidate declares itself (:4693-4696) and becomes the section found moved.

**Against what was decided.** decisions.md, round 2 [2], says: "a token a load lexes from its line's start (not in a comment or a string on that line)". The implementation keeps that only when the line starts inside the window. The R2a stage report does not list this limit among its deviations or open points. No issue holds it:
- #171 and its comments cover object headers and `trailer` keywords, not this search's `xref` candidate.
- The threat model's Known gaps do not name it.
- diagnostics.md:110, validation-rules.md:56, threat-model.md:419 and lazy-reading.md:172 all state the rule with no limit.
- Only the code remark (:4731-4732) admits it.

**Against main.** Main dropped every such word and found the real table. This is a regression on main 21a0298 for these lines. The consequences are the same as round 2 [2], which was settled major:
- a freed object is served from its old revision;
- a rebuild runs at opening;
- a false `xref.section-malformed` Error.

**The test.** CrossReferenceChainTests.cs:1094 (`An_xref_keyword_on_a_line_that_starts_before_the_bytes_searched_is_lexed_from_where_they_start`) names the window-start lexing as behavior. Its file (`] endobj xref` on an 818-byte line) also gives a token when lexed from the line's real start. So it would hold under the fix, with a new name.

**One precision on the condition.** What decides the case is where the line starts, before the window's first byte, which lies 513 bytes before the offset named. It is not "~512 bytes before the place", and not the line's length. In my rows the word lay 41 bytes before the named offset. A comment whose `%` was 472 bytes before the word was still seen, because the `%` sat on the lead byte. One whose `%` was 482 bytes before it was not.

**A related case, not counted in this finding.** A string opened on an earlier line (`(aaa\n xref see...)`) also regresses against main, at any length. That is the #171-class limit round 2's refuter named. It is written nowhere for this search either.

**Reach.** It needs an imprecise offset, plus a one-line comment or string holding the word, starting before the window and nearer than the real table. That is narrow. Even so, by the brief's scale a wrong object served and a decision not implemented are major.
VERDICT EVIDENCE: **The probe.** My own, at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute-tokline/ZzTokLine.cs.txt, outputs out-branch.txt, out-main.txt and out-proto.txt beside it. The file:
- T0 at 159 indexes 1 to 4, with 4 = `(old four)`;
- a 1,200-byte comment;
- then the stray line;
- object 6, then 60 spaces;
- T1, which frees 4 (gen 1) or keeps it, indexes 6, and has /Prev T0;
- object 7, then the newest table, whose /Prev names 2 bytes into object 6's endobj.

**Branch 19122f0.**

Comment `%` + 700 `a` + ` xref see the table below`, object 4 freed: word at 2226, named 2267, T1 at 2332, window start 1755.
- `D xref.offset-adjusted [Repair] @2226: The cross-reference section /Prev names at offset 2267 was found 41 bytes before it.`
- `D xref.section-unreadable [Warning] @2226: The cross-reference table /Prev names at offset 2267, found at offset 2226, is there but cannot be read: it holds the keyword see at offset 2231, where a subsection or the trailer should start. ...`
- `D xref.rebuilt`; WasRepairedAtOpen=True; `GetObject(4) = old four`.
- Sections: 2497 Read, 2226 Malformed.
- `F xref.section-malformed [Error] @2226`, `F xref.section-shifted [Warning] @2226`.

String object `(` + 700 `a` + ` xref see the table below)`, freed:
- `found 49 bytes before it` @2234, section-unreadable, rebuilt, `GetObject(4) = old four`, section-malformed [Error].

Comment of 520 and of 480 `a`: the same, at @2046 and @2006. Without the deletion (700, comment and string), the same reports and the rebuild.

Controls on the branch:
- Comment of 100: `found 65 bytes after it` @1732, WasRepaired=False, `GetObject(4) = null`.
- Comment of 470 (its `%` on the lead byte 1524): `65 bytes after it` @2102, null.
- String of 100: `65 bytes after` @1748, null.

**Main 21a0298, every row.** `D xref.offset-adjusted ... found 65 bytes from there` at T1 (@2332, @2348, @2152, @2112 ...), WasRepairedAtOpen=False, `GetObject(4) = null` when freed, and only `F xref.section-shifted`.

**The known case.** Multi-line string `(` + 100 `a` + `\n xref see...)`:
- branch: found 49 bytes before, section-unreadable, rebuilt, `old four`;
- main: 65 bytes from there, null.

**Prototype of the fix** (/tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute-tokline/proto-fix.diff, since reverted). Where no end of line lies in the window before the place, it reads back up to 4,096 bytes to one. It drops the place where none lies within that bound and the file starts earlier.
- Every same-line row gives main's result: `65 bytes after it`, no rebuild, `GetObject(4) = null`.
- The multi-line string row is unchanged.
- CrossReferenceChainTests, CrossReferenceRuleTests, HostileInputTests, GuardCutValueTests and HybridCrossReferenceTests: 1,441 passed, 0 failed, the test at :1094 included.
FIX (refuter): Implement round 2 [2] as decided. This is not a design choice.

**The code.** In `TryRelocateXRefSection`, when `span[..at]` holds no end of line and the window does not start the file, lex the line from its real start:
- read back from `start - lead` through a window of an internal constant (4 KB is enough; do not use `MaxTrailerLength`, which is a guard) to the last CR or LF;
- lex from there to the place;
- where no end of line lies within the bound and the file starts earlier, the place declares nothing and is dropped (ForgetReadThrough), as on main.

Under ADR 34 the bound is an internal constant. Write its reason where it is declared: only a search near a wrong offset reaches it, and a conforming `xref` begins its line, so no valid file does. The cost stays bounded: one extra window per unreadable place, under `MaxRelocationCandidates`. Update the remark at :4731-4734. Only the string opened on an earlier line stays the #171 limit there.

**The tests.**
- Rename CrossReferenceChainTests.cs:1094 to what it holds: an xref keyword after `endobj` on a line that starts before the bytes searched is lexed from the line's start, and is the section found moved.
- Add 700-byte comment and string rows to `The_word_xref_in_a_comment_or_a_string_near_where_an_offset_names_no_section_is_no_section_found_moved`, to its CrossReferenceRuleTests twin (no section-malformed), and to `An_object_an_update_frees_stays_free_where_the_word_xref_lies_between_where_its_table_is_named_and_where_it_is`.
- Add a row whose line runs past the bound, so the drop is pinned.

**The docs.** In diagnostics.md:110, validation-rules.md:56, threat-model.md:419 and lazy-reading.md:172, name the one limit that remains: a word in a string an earlier line opened is taken. Also add it to the #171 Known gaps entry, or comment it on #171.

If the maintainer prefers to keep the window-start lexing instead: file it as debt under M02 and state the limit on those four pages and in the Known gaps.

######## [2] chain:look-at-prev-where-no-search-runs — reviewer minor; refuter confirmed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3456-3458 (KeepsItsPlace, so DeclaresStreamAt, evaluated as an argument before TryRelocateXRefSection, which returns false at once for an offset outside the file at :4632-4635); comment :3453 'Told only when the search runs.'
CLAIM: The look at the /Prev's object (DeclaresStreamAt, a MaxTrailerLength window, 64 KB by default) is made whenever the /XRefStm's offset cannot be read and declares nothing. That includes an /XRefStm outside the file, where no search runs. It also includes a table a guard stopped: there the hybrid branch runs only for an /XRefStm outside the file, so the look reads the /Prev's object past the guard, against #264's 'nothing past the guard is read' (:3107, :3374). Nothing is reported or kept. The cost is one window per such table, bounded but avoidable, and the comment misstates when the look is made.
EVIDENCE: P5 file: the newest table (40 rows) names through /Prev cross-reference stream 9 at 186, which is sound and followed by a 100 KB comment.

With /XRefStm 99999999 and no guard:
- branch 148,468 bytes read, 761ce0b 83,028 (+65,440, one 64 KB window);
- without the /XRefStm: branch 82,914, 761ce0b 82,978.

With MaxXRefSectionLength 300 (rows cut, the chain ends at the newest table) and /XRefStm 99999999:
- branch 319,308, 761ce0b 253,868 (+65,440): the stream at 186, past the guard, is read;
- the diagnostics are identical on both: limit.xref-section-length, xref.section-missing, xref.rebuilt.
FIX (reviewer): Make the look lazy:
- pass previousStart and previousTable into TryRelocateXRefSection, and call DeclaresStreamAt only when the search comes to candidate == previousStart;
- at least, guard the argument with IsInFile(PositionOf(hybrid)).

StartOfNamed(previous) at :3392 is likewise of no use where the /XRefStm lies outside the file (hybridStart is -1 there). Under a guard it probes past the guard, which is older, from R1.

Correct the comment. Add a HostileInputTests bytes-read test: a table whose /XRefStm lies outside the file reads no more than the same table without it, with and without a rows guard.
--- VERDICT REASON: The finding holds, and the cost goes slightly further than it says. KeepsItsPlace(previousStart, previousTable) is passed as an argument (PdfFileReader.cs:3456-3458), so it runs before TryRelocateXRefSection. That method returns false at once for an offset outside the file (:4632-4635). So whenever the /XRefStm's offset lies outside the file, DeclaresStreamAt reads one MaxTrailerLength window (64 KB) at the /Prev's object, and the result is thrown away. Under a rows guard, the hybrid branch runs only for an /XRefStm outside the file (:3433), so this look is always useless there, and it reads the /Prev's object past the guard. That goes against the letter of #264 ("judges its /Prev as far as it can without reading past the guard") and the remarks at :3107 and :3374 ("nothing past the guard is read"). Nothing is reported or kept: the diagnostics and sections are identical to 761ce0b. So this is a bounded, avoidable cost plus a comment that misstates when the look is made. Severity: minor.

Where it applies:
- Only a /Prev that names an in-file place not starting with the xref keyword. A /Prev naming a classic table short-circuits on previousTable.
- Only an /XRefStm written as an offset of zero or more. A negative offset or a reference gives previousStart = -1.

Where it goes further than claimed: the look is also useless for an /XRefStm inside the file that lies more than NearbySearchRadius (512 bytes) from the /Prev's start. The search runs there but never comes to previousStart. The lazy fix removes that window too.

The comment at :3453, "Told only when the search runs.", is wrong for an offset outside the file.

StartOfNamed(previous) at :3392 is likewise of no use when the /XRefStm lies outside the file: hybridStart is -1 there, so branch (c) cannot match and there is no search. Under a guard it is a probe of about 32 bytes past the guard, and it dates from round 1, as the finding says.
VERDICT EVIDENCE: I rebuilt the reproducer myself on 19122f0 and on 761ce0b. It is the uncommitted probe ZzLookPrevTests, kept at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/ZzLookPrevTests.cs.txt, and it counts bytes with StrictCountingSource.

The file:
- catalog and pages;
- cross-reference stream 9 at offset 110, which is sound and indexes both;
- a comment line of 100 KB;
- the newest table (40 rows, /Prev 110), with or without /XRefStm.

Bytes read, head / 761ce0b:
- /XRefStm 99999999, no guard: 148,486 / 83,046 (+65,440, one 64 KB window less the 96 bytes the PaddingAt memo saves). Without the /XRefStm: 82,932 / 82,996.
- /XRefStm 99999999, MaxXRefSectionLength 300: 82,969 / 17,529 (+65,440). Without the /XRefStm: 17,397 / 17,461. The 64 KB at the /Prev's object is read past the guard.
- Diagnostics and sections are identical on both commits in every case:
  - no guard: xref.section-missing for /XRefStm 99999999; sections startxref Read, /XRefStm NotFound, /Prev Read 110;
  - guard: limit.xref-section-length and xref.section-missing; sections startxref Read, /XRefStm NotFound.
  The reviewer listed xref.section-missing and xref.rebuilt. In my file only xref.section-missing appears, with WasRepaired false.
- /Prev naming a classic table instead: 82,880 against 82,862 without the /XRefStm (no guard), and 17,433 against 17,397 (guard). No window is read.
- /XRefStm -5 or 5 0 R: no extra read (82,944 and 82,947).
- /XRefStm 50000, inside the file and about 50 KB past the /Prev, so beyond the 512-byte radius: the search runs and the look is useless again. Head 275,406; with the lazy fix 209,870.

I checked the lazy fix: I passed previousStart as leaveAt, as 761ce0b did, and evaluated KeepsItsPlace(candidate, previousTable) only at candidate == leaveAt.
- It gives 82,950 / 17,433 / 209,870 for the three cases above, with the same diagnostics.
- The whole unit suite passes: 7,472 passed, 0 failed, 20 skipped.

I then reverted the change and removed the probe.
FIX (refuter): Make the look lazy:
- Pass the /Prev's start as leaveAt, as 761ce0b did, with previousTable added as a parameter of TryRelocateXRefSection.
- In the loop, test `candidate == leaveAt && KeepsItsPlace(candidate, leaveIsTable)` before setting left.
- When the /Prev does not keep its place, the candidate is tried like any other place, as now.

This removes the window outside the file, under the guard, and for an /XRefStm beyond the radius. It needs no closure. I ran it against the whole unit suite and it passed.

Optionally, compute previousStart only when hybridStart >= 0. Branch (c) and the search use it only then, so the round 1 probe of the /Prev past a guard goes too.

Correct the comment at :3449-3453 and the leaveAt parameter documentation to say the look is made only when the search comes to the /Prev's start.

Add a HostileInputTests bytes-read test: a table whose /Prev names a cross-reference stream followed by more than 64 KB reads no more with /XRefStm 99999999 than without it, with and without MaxXRefSectionLength cutting its rows. Add a row for an /XRefStm in the file beyond NearbySearchRadius as well.

This is not a design choice for the maintainer.

######## [3] chain:padding-memo-evicted-by-two-places — reviewer nit; refuter confirmed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:328-338 (_paddingProbes doc 'Each position's white space is crossed once, however often it is asked of') and :4873-4876; docs/threat-model.md:414 ('once for each position, however often the chain asks of it') and :456 ('once per section')
CLAIM: Where the search near a hybrid table's /XRefStm tries two places or more between StartOfNamed(/Prev) and the next iteration's ReachedAt(/Prev), both memo entries are replaced. The /Prev's run of white space is then crossed a second time. MaxRelocationCandidates bounds this: each such table spends two or more of the document's 32 attempts, so at most 16 a document, about 1.35 MB at the default MaxTrailerLength. Main crosses each run once.
EVIDENCE: P3 file: 100 hybrid tables. Each /Prev names the start of 60,000 spaces before the older table's xref keyword. Each /XRefStm names 3 bytes short of k small objects followed by its stream. The figures are bytes read minus those of the same file whose /Prev names the keywords:
- k=0: branch 8,482,198 (84,821 a table), main 8,482,198;
- k=1: branch 9,829,760 (16 tables relocated, +16 x 84,256), main 8,482,176;
- k=2: branch 9,408,618 (10 relocated), main 8,482,154;
- k=3: branch 9,155,924 (8 relocated), main 8,482,132.
FIX (reviewer): Either keep the /Prev's probe while its table's /XRefStm is looked for nearby (a slot pinned for previousStart, or carry StartOfNamed's padding and flag to the next iteration), or say in the field doc and threat-model rows 414 and 456 that a hybrid table's /Prev is crossed again where the search near its /XRefStm tries two places or more, at most 16 tables a document.
--- VERDICT REASON: The finding holds. I rebuilt it independently and the numbers match the reviewer's to within 2 bytes.

**What happens.** The memo in PdfFileReader.cs:338 keeps the last two probes, in the order they were read (:4873-4903).
1. A hybrid table's ReachedAt(hybrid) probes H, then StartOfNamed(previous) at :3392 probes P. The memo now holds (P, H).
2. The /XRefStm's own read at H is answered from the memo.
3. TryRelocateXRefSection (:4617) calls TryReadXRefSection(candidate, candidate: true) for each place it tries. That call ends in PaddingAt (:4842), and each such probe enters the memo.
4. One place tried gives (C1, P), and P survives. That is the only case 7c0748b's test covers (streamShortBy 3, nothing between the named offset and the stream). Two places give (C2, C1), and P is evicted.
5. The next iteration's ReachedAt(P), through StartOf, then crosses P's run of white space again: one full probe of 32+256+2,048+16,384+65,536 bytes.

**Why it is bounded.** No other path between StartOfNamed and the next ReachedAt probes two new positions. The only probe call sites are :3176, :3392, :3617, :3639 and :4842. The guard path's ReachedBefore(previous) at :3552 is cut by the same eviction, under the same bound. Every candidate read spends one of the document's 32 MaxRelocationCandidates. So at most 16 tables a document are crossed again. Each crossing costs at most about 8/7 of MaxTrailerLength: 84,256 bytes at the default 64 KB, about 1.35 MB a document. A caller who raises MaxTrailerLength raises it proportionally.

**One precision.** With k=2, 11 tables are crossed again, not 10. The eleventh table's search ran out of attempts after two tries, so it was not relocated, but P was still evicted. The bound of 16 stands.

**Impact.**
- Diagnostics and sections are identical on the branch and on main. Only the bytes read grow.
- Main 21a0298 crosses each run once.
- This is the incomplete part of round 2's [3] fix. 7c0748b's subject, "keep a hybrid table's prev probe while its xrefstm is found nearby", and its code comment, "a place its search near the offset tries", cover only one place tried.
- The docs state the property absolutely, and it is false here:
  - the field's summary: "Each position's white space is crossed once, however often it is asked of";
  - the field's remarks: "what is read is what a reading that asked once read";
  - docs/threat-model.md:414: "once for each position, however often the chain asks of it";
  - docs/threat-model.md:456: "once per section, however often the chain asks".

**Severity.** By the brief's rubric this is minor on two counts: a bounded but avoidable cost (a regression against main), and a text that misstates the code. It sits at the low end of minor: a per-document constant, about 3% more bytes in the reproducer, no change to any report. The reviewer's nit is defensible if the orchestrator weighs size over the rubric.
VERDICT EVIDENCE: **The probe.** My own uncommitted ZzPadMemo, now deleted. It builds the reviewer's P3 file:
- 100 hybrid tables;
- each /Prev names the start of the 60,000 spaces before the older table's xref keyword;
- each /XRefStm names 3 bytes short of k one-line objects (`N 0 obj\n1\nendobj\n`), which are followed by its stream.

Each figure is bytes read on that file minus bytes read on the same file whose /Prev names the keywords (StrictCountingSource):

| k | 19122f0 | main 21a0298 | 19122f0 minus main | Relocated |
|---|---|---|---|---|
| 0 | 8,482,198 | 8,482,198 | 0 | 32 |
| 1 | 9,829,762 | 8,482,178 | +1,347,584 = 16 × 84,224 | 16 |
| 2 | 9,408,622 | 8,482,158 | +926,464 = 11 × 84,224 | 10 |
| 3 | 9,155,930 | 8,482,138 | +673,792 = 8 × 84,224 | 8 |

- Both builds give identical diagnostics on both files. For k=1, for example: xref.offset-adjusted ×16, xref.rebuilt ×1, xref.section-missing ×84, 200 sections. The section states are identical too.
- The existing test's bound, Tables × (Crossing + 4,096) = 8,835,200, would fail on k=1 at 19122f0 (9,829,762) and pass on main (8,482,178).

**A trial fix**, since reverted. TryReadXRefSection passed `remember: !candidate` to PaddingAt, and the memo was updated only when `remember` was true. With that change the bytes read return exactly to main's: 8,482,198 / 8,482,178 / 8,482,158 / 8,482,138, with the same section counts. The whole unit suite then passed: 7,495 total, 0 failed, 20 skipped.

**Files.** Under /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r3-padmemo/:
- out-branch.txt
- out-main.txt
- out-fix.txt
- trial-fix.diff
FIX (refuter): **Code fix (what I would make).** Keep a place the search near an offset tries out of the memo, so that the /Prev's probe, and the /XRefStm's, survive any number of places tried.
- In TryReadXRefSection (PdfFileReader.cs:4842), call `PaddingAt(absolute, out var table, remember: !candidate)`.
- In PaddingAt (:4903), store `_paddingProbes` only when `remember` is true.
- Why this costs nothing: a candidate is the start of a header or of an xref keyword, so its probe is one 32-byte window. A candidate the chain reaches again is known through `_sectionsReadAt`, not through the memo.
- Verified above: bytes read return exactly to main's, and the whole suite passes.

**Wording to change with it:**
- the comment at :4873-4875: "the places its search near the offset tries are not kept";
- the field's summary and remarks at :328-337.

**Test.** Add a row to `HostileInputTests.A_chain_whose_offsets_name_long_runs_of_white_space_crosses_each_run_once` with one object or more between the offset the /XRefStm names and its stream. For example, a third parameter `objectsBefore: 1`. At 19122f0 that row reads 9,829,762 against the bound of 8,835,200, so it fails there and passes with the fix.

**If the code is left as is,** correct the docs instead: the field's summary and remarks, and docs/threat-model.md rows 414 and 456. Say that a hybrid table's /Prev is crossed again where the search near its /XRefStm tries two places or more: at most 16 tables a document (MaxRelocationCandidates / 2), each about 8/7 of MaxTrailerLength.

This needs no design choice from the maintainer.

######## [4] sections:own-duplicate-said-refused-by-newer — reviewer minor; refuter narrowed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:6970 (AddFromChain sets RowsRefusedBefore on any _refusedNumbers hit, the section's own included); :4430-4436 (refused-row arm); :4416 and :4249 use the same flag
CLAIM: 95d8e0f regressed a report. RowsRefusedBefore is set for every row AddFromChain drops through _refusedNumbers. That set also holds numbers the section refused itself. A section that gives one number twice, the first row refused, now says 'a newer section refused' when no newer section exists, and that 'the other rows read [were] kept' when the duplicate row was dropped. R2b's deviation called this 'technically true', but it is false. A related problem is older, from 54d56c6 (#210): with the order reversed, the report says the object is 'looked for by rebuilding the index', yet the section's own earlier row indexes it and serves it.
EVIDENCE: Probe ZzR3SecProbe.ZzDuplicateRefusedFirst (source in scratchpad r3sec/ZzR3SecProbe.cs.txt, output in r3sec/p1-duplicate.txt). File: objects 1-3, then the table startxref names at 133: `xref\n0 4\n0000000000 65535 f \n{1} 00000 n \n{2} 00000 n \n{three} 65536 n \n3 1\n{three} 00000 n \ntrailer\n<< /Size 4 /Root 1 0 R >>`.

Head 19122f0 gives `Warning xref.section-unreadable @133: The cross-reference table startxref names at offset 133 was read only in part: the row for object 3, at offset 202, gives it generation 65536, outside 0 to 65535; that row was refused, the rows after it read, and the other rows read kept but for those whose numbers a newer section refused; its object is looked for by rebuilding the index where no section read before it indexes it, rather than taken from an older section's row.` The record shows `startxref@133 everyBefore=False rowsRefusedBefore=True`, idx 3=n, and obj3=three repaired=True.

761ce0b, same file: `...; that row alone was refused, the rows after it read, and its object is looked for by rebuilding the index where no section read before it indexes it, rather than taken from an older section's row.`

Rows swapped (sound row first, then the refused duplicate at 226), on head and on 761ce0b alike: `...that row alone was refused, the rows after it read, and its object is looked for by rebuilding the index where no section read before it indexes it...`, with idx 3=y and obj3=three read with repaired=False. Main 21a0298 gives no read-in-part report.
FIX (reviewer): Flag RowsRefusedBefore only for a refusal that a section read before made. Either keep the numbers the section refuses itself on its record (empty for every valid file) and skip them in AddFromChain's flag, or reword the clause to 'kept but for those whose numbers were refused before them' with no 'newer section'. For the reversed order, word the refused-row tail 'where no row read before it indexes it', or choose it by whether Refuse refused anything. Add both orders as test rows.
--- VERDICT REASON: The finding holds. I rebuilt it on 19122f0 and on a 761ce0b tree exported with git archive, and checked it against the code.

Refused-first order (a 95d8e0f regression): Refuse(3) puts 3 in _refusedNumbers. The sound duplicate row then reaches AddFromChain (PdfFileReader.cs:6968-6970), which sets RowsRefusedBefore on any _refusedNumbers hit, the section's own refusal included. The refused-row arm (:4430-4436) then says "kept but for those whose numbers a newer section refused", but no newer section exists. It names a refusal that never happened, so this is not "technically true" as R2b's deviation called it. R2b also went past what the round 2 refuter specified for [7]: set the flag only when "a newer section's refusal is in _refusedNumbers". The XML doc on XRefSectionRecord.RowsRefusedBefore (:192-197) mentions the own-duplicate case. The message, the remarks of ReportSectionReadInPart (:4358), diagnostics.md:112 and threat-model.md:421 all say "a newer section".

Reversed order (older, from 54d56c6 #210, the same on 761ce0b): Refuse returns early because the section's own earlier row indexes 3. The report still says "its object is looked for by rebuilding the index", yet the object is served from that row with WasRepaired false. Main 21a0298 has no such report. No earlier round mentions duplicate numbers within a section, and decisions.md does not accept this point: its open points for round 3 list only the two other R2b wording gaps.

How it narrows:
(1) Only invalid files reach it: a refused row (generation 65536) plus a number given twice in one section.
(2) It is text only. The index, the objects served, the repairs and the diagnostic code are identical on 761ce0b and head.
(3) I reproduced only the refused-row arm. The table-read-in-part (:4416), stream (:4400) and lost-section (:4249) arms read the same flag, so they would likely misword the same way, but I did not reproduce them. My attempt at a table read in part came out Malformed and was rebuilt.
(4) Only the refused-first wording is a regression against 761ce0b; the reversed-order text is older batch-19 debt, not a round 2 regression.

Minor, not nit: the message names a section that does not exist, and the reversed order claims a rebuild that never happens. It is the same class as the two known R2b wording gaps and can be fixed with them.
VERDICT EVIDENCE: Probe source: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r3ref-dup/ZzRefDupProbe.cs.txt. Outputs: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r3ref-dup/out.txt (head) and /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r3ref-dup/out-761.txt (761ce0b).

File: objects 1-3, then `xref\n0 4\n0000000000 65535 f \n{1}{2}{rows}trailer\n<< /Size 4 /Root 1 0 R >>`. The rows are either `{three} 65536 n` then `3 1` and `{three} 00000 n`, or the reverse.

Head 19122f0, refused first: `Warning xref.section-unreadable @133: ... the row for object 3, at offset 202, gives it generation 65536, outside 0 to 65535; that row was refused, the rows after it read, and the other rows read kept but for those whose numbers a newer section refused; its object is looked for by rebuilding the index where no section read before it indexes it, rather than taken from an older section's row.` Asking for object 3 gives `three` with repaired=True and adds xref.rebuilt.

761ce0b, same file: `...; that row alone was refused, the rows after it read, and its object is looked for by rebuilding the index where no section read before it indexes it, rather than taken from an older section's row.` Same object and same repair.

Reversed order, identical on head and 761ce0b: `...the row for object 3, at offset 226, gives it generation 65536...; that row alone was refused, the rows after it read, and its object is looked for by rebuilding the index...`. Object 3 = `three`, repaired=False, and no xref.rebuilt.

With an older /Prev table giving `(old three)` under the section: refused first, head again says `a newer section refused`, with obj3=`new three` repaired=True. Reversed, obj3=`new three` repaired=False, though the report says rebuilt.

Code: AddFromChain :6968-6970 sets `section.RowsRefusedBefore = true` on `_refusedNumbers?.Contains(number) == true`. Refuse :6995 returns early when `_xref.TryGet(number, out _)`, the section's own earlier row included. On 761ce0b, AddFromChain :6618-6621 set no flag, and the refused-row arm :4174-4176 always said "that row alone was refused".
FIX (refuter): Not a maintainer choice; this is wording, plus one record change.

1. Set RowsRefusedBefore only for a refusal another section made. For example, make _refusedNumbers a map from number to the refusing section's record or ordinal, and in AddFromChain flag only when the refuser is not `section`. Alternatively, keep a per-section set of the section's own refusals, allocated only on the section's first refusal, so a valid file allocates nothing. Either way, the own duplicate is still dropped. Correct the XML doc on XRefSectionRecord.RowsRefusedBefore (:192-197) to match. If neither fits, the fallback is a neutral clause, "kept but for those whose numbers were refused before they were read", carried into diagnostics.md:112 and threat-model.md:421.

2. In the refused-row arm (:4435-4436), word the tail "its object is looked for by rebuilding the index where no row read before it indexes it". That reads true whether a newer section or the section's own earlier row indexes it. The alternative is to choose the tail by whether Refuse added the number.

3. Add both orders as rows of the refused-row test in CrossReferenceChainTests. Each row asserts the whole message, idx[3], and the object and WasRepaired after asking for object 3. Optionally add an older /Prev table under it, asserting `new three` either way.

These can be fixed together with the two known R2b wording gaps.

######## [5] sections:reserved-row-said-read-past-newer-refusal — reviewer minor; refuter narrowed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:6952-6954 (ReservedRows++ whatever AddFromChain did) and :4487 (ReportReservedRows clause); src/AdCodicem.Pdf/Validation/Rules/ReservedTypeRule.cs:50
CLAIM: This is a sibling of [7][14][19] that R2b left. Under a newer refusal of every number, or of a range covering it, a stream's reserved-type row does not stand: AddFromChain drops it and the object is looked for by rebuilding the index. xref.entry-reserved still says 'the row was read so' (as a reference to the null object), which implies the object reads null, when the reader serves what the rebuild finds. The validator's xref.entry-type-reserved then says 'as the reader did until it rebuilt the index', but the reader never read the row so. Both texts are new in this batch (#221 with #208).
EVIDENCE: Probe ZzR3SecProbe.ZzReservedRowPastLostStream (output in scratchpad r3sec/p10-reserved-past-lost.txt). File, oldest first:
- the oldest table (137) indexes 1-3, object 3 being `(old three)`;
- object 3 is written again as `(three a rebuild finds)`;
- stream 7 at 299: `/Type /XRef /Size 8 /Index [3 1] /W [1 4 2] /Prev 137`, one row of type 7;
- lost stream 9 at 409: `/Index X /W [1 -4 2] /Prev 299`;
- the newest table (startxref) indexes 1-2, `/Prev 409`.

X=/X on head: `Warning xref.entry-reserved @299: The cross-reference stream /Prev names at offset 299 gives 1 row a reserved type: the row for object 3 gives it type 7. ISO 32000-1 defines types 0, 1 and 2, and reads any other as a reference to the null object (7.5.8.3): the row was read so, and no older section's row stands for its object.` The record shows `/Prev@299 everyBefore=True rowsRefusedBefore=True`, idx 3=n, and obj3='three a rebuild finds' repaired=True. The rule says `Warning xref.entry-type-reserved: ... (7.5.8.3), as the reader did until it rebuilt the index by scanning the file; it now reads each object such a row numbers as the rebuild finds it.`

X=[3 1]: the same two texts, rowsRefusedBefore=True, idx 3=n, obj3 rebuilt.

Control X=[10 1]: the same reader text, idx 3=y, obj3=null repaired=False, which is true there.
FIX (reviewer): Have AddFromChain report whether it refused the row, or test the refusal first in the reserved branch. For a reserved row a newer section refused, count it apart (or record a flag) and word the clause, e.g. 'the row does not stand, a newer section having refused its number: its object is looked for by rebuilding the index'. Give ReservedTypeRule the same record fact instead of WasRepaired alone. Test /X, a covering range and [10 1] as the control.
--- VERDICT REASON: The finding holds on 19122f0. It is a sibling R2b left of [7][14][19]: it reproduces exactly, and it has a wider trigger than claimed.

**Code path.** The reserved arm of ReadXRefStreamRows (PdfFileReader.cs:6950-6954) calls AddFromChain. It then records ReservedRow and increments ReservedRows, whether or not AddFromChain kept the row. AddFromChain (:6968-6971) drops the row when a newer section refused its number, and only sets RowsRefusedBefore. ReportReservedRows (:4487) does not consult that flag. ReservedTypeRule (:49-51) branches only on WasRepaired.

**What happens.** The object has no entry in the index. It is served through a rebuild: 'new three', or 'old three' when the file writes no newer definition. The reader's report still says 'the row was read so'. The rule says 'as the reader did until it rebuilt the index', yet the reader never indexed the row as free and never served null for it.

**Wider than claimed.** The finding names a lost stream refusing every number or a range. A newer table's own row for the number, refused alone (generation 65536), has the same effect with no lost stream at all. With two reserved rows where only one was refused, 'the rows were read so' is half true.

**Narrower than claimed.**
- The reader's second half, 'no older section's row stands for its object', is true.
- The claim that the text 'implies the object reads null' is no stronger here than in an ordinary case nobody treats as a defect. When a newer table gives a sound row for 3, both texts come out identical ('the row was read so'; 'as the reader does') while object 3 reads 'new three'. That is the chain's newer-wins precedence, which every row-level message leaves untold.
- What makes the refused case a defect is the convention R2b set for [7][14][19]: rows a newer section refused 'stand for nothing', and the report says so ('kept but for those whose numbers a newer section refused'). Superseded rows stay unmentioned. This report misses that convention.
- In every refusal shape, the newer section's own report already says the object is looked for by rebuilding the index. Behavior is right.
- Every refusal sets _indexIncomplete, so validation always rebuilds. The rule therefore always takes the 'did until it rebuilt' arm, and its second half ('it now reads each object ... as the rebuild finds it') is true. Only 'as the reader did' is false for a refused row.

**Not a regression of the round 2 fixes.** The reader text is identical at 761ce0b. Main 21a0298 has no reserved report at all. The text came with 01426c1 (#221), combined with this batch's refusals (#208, #210).

**Docs.** The general docs state the rule without either exception: ReservedRow's XML doc ('the row stands for its object as a free row does'), PdfDiagnosticCodes.cs:114-119, diagnostics.md:117, validation-rules.md:55, lazy-reading.md:86-92 and the ReservedTypeRule remarks. Under the supersession precedent the docs part is a nit at most.

**Severity.** Minor, matching the siblings [7][14][19]: a misleading text in a path no test covers. It is not major, because the newer section's report states what happens to the object.

**Not settled earlier.** Neither decisions.md nor r2-fix-result.json lists this case as an open point, and no earlier finding raised it.
VERDICT EVIDENCE: I wrote my own probe on detached origin/ccr-6d6c0ee6-dqm4lm-188 (19122f0), built in Release with 0 warnings. Source: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute-reserved-past/ZzRefuteReservedPast.cs.txt. Output: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute-reserved-past/out.txt. The probe was removed from the worktree afterwards.

**Shape.** The oldest table indexes 1-4. Object 3 may be written again as '(new three)'. Middle stream 7 has `/Index [3 1] /W [1 4 2]` and one row of type 7. An optional lost stream 9 has `/Index X /W [1 -4 2] /Prev middle`. The newest table indexes 1-2. Validation runs on a freshly opened document.

**X=/X**
- `xref.section-unreadable @443: ... The chain goes on through its /Prev; every object no section read before it indexes is looked for by rebuilding the index...`
- `xref.entry-reserved @333: ... gives 1 row a reserved type: the row for object 3 gives it type 7. ISO 32000-1 defines types 0, 1 and 2, and reads any other as a reference to the null object (7.5.8.3): the row was read so, and no older section's row stands for its object.`
- Record: `/Prev@333 Stream Read reserved=1 rowsRefusedBefore=True everyBefore=True`. Index: idx3=False. Reading object 3 gives 'new three' with WasRepaired=True.
- Rule: `... (7.5.8.3), as the reader did until it rebuilt the index by scanning the file; it now reads each object such a row numbers as the rebuild finds it.`

**X=[3 1]:** the same texts. rowsRefusedBefore=True, idx3=False, idx4=True. Object 3 reads 'new three' through the rebuild.

**X=/X, no newer definition of 3:** the same texts. Object 3 reads 'old three' with WasRepaired=True.

**Wider trigger: no lost stream, the newest table gives `3 1 / 0000000137 65536 n`**
- `xref.section-unreadable @443: The cross-reference table startxref names ... gives it generation 65536 ...; that row alone was refused ... its object is looked for by rebuilding the index...`
- The reserved report is the same as above.
- rowsRefusedBefore=True, idx3=False. Object 3 reads 'new three' with WasRepaired=True. The rule takes the 'did until it rebuilt' arm.

**Two reserved rows (3 and 4), X=[3 1]:** `gives 2 rows a reserved type ... the rows were read so, and no older section's row stands for their objects`. idx3=False and idx4=True(Free): object 3 was refused, object 4 stands.

**Controls**
- X=[10 1]: idx3=True(Free). Object 3 reads null with WasRepaired=False. After validation the rule says 'did until rebuilt' and object 3 reads 'new three'. The text is true here.
- No lost stream: 'as the reader does'. Object 3 reads null.

**Narrowing control.** The newest table gives a sound row for 3. The reserved report is identical ('the row was read so'). idx3=Regular@306, object 3 reads 'new three' with WasRepaired=False, and the rule says 'as the reader does'.

**761ce0b and main.** At 761ce0b the format string is the same: `git show 761ce0b:.../PdfFileReader.cs` lines 4222-4230. On main 21a0298, `git grep` finds no xref.entry-reserved message, only the old 'reserved type is ignored' remark.
FIX (refuter): No maintainer design choice is needed. The fix carries the [7][14][19] convention into the reserved-row report: a row a newer section refused stands for nothing, and the report says so. A row a newer section's sound row supersedes keeps today's text, as superseded rows stay 'kept' in the read-in-part tails.

**Reader**
- Have AddFromChain return whether it took the row's number. Alternatively, test `_refusedNumbers?.Contains(number) == true || RefusedToOlderRows(number)` first in the reserved arm (PdfFileReader.cs:6950-6954).
- Keep counting every reserved row, since #221 reports them whatever became of the section. Also count those a newer refusal set aside, for example `section.ReservedRowsRefused`.
- In ReportReservedRows (:4487):
  - When every one was refused: '...the row was read so, but a newer section had refused its number: it stands for nothing, and its object is looked for by rebuilding the index'.
  - When some were: '...the rows were read so but for those whose numbers a newer section refused, which stand for nothing'.

**Validator**
- In ReservedTypeRule (:46-51), choose the clause from that record fact as well as WasRepaired.
- For a section whose reserved rows were all refused, for example: '..., which the reader did not read so, a newer section having refused their numbers: it reads each such object as the rebuild finds it'. The 'did until it rebuilt' arm then stays for rows that stood until the rebuild.

**Docs.** Add 'but for one whose number a newer section refused, which stands for nothing and is looked for by rebuilding the index' to ReservedRow's XML doc (XRefSectionRecord.cs:66-71), PdfDiagnosticCodes.cs:114-119, diagnostics.md:117 and validation-rules.md:55. This part is a nit.

**Tests**
- Cover /X, a covering range [3 1], and a newer table's row refused alone (generation 65536).
- Cover two reserved rows with one refused, for the count wording.
- Add two controls that keep today's texts: [10 1], and a newer sound row for 3.
- Assert the full reader and rule messages, and that object 3 reads the rebuilt definition.

######## [6] sections:xrefstm-table-bad-first-row-said-cut-before-it — reviewer nit; refuter narrowed nit maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3526 (`stream.CutByLimit && stream.HighestNumber < 0`); docs/website/docs/reference/diagnostics.md:115, docs/website/docs/reference/validation-rules.md:60, docs/threat-model.md:417 ('none of its rows read')
CLAIM: [10]'s fix tests HighestNumber < 0, which only sound or refused rows set. Take a table an /XRefStm names whose first row is no row and which a guard cuts after that row. The new naming says a limit 'cut [it] before its first row'. The xref.section-unreadable report beside it names that first row's fault, and the validator copies the naming next to xref.section-malformed for the same row. The two reports contradict each other, and the docs' condition ('none of its rows read') is narrower than the code's.
EVIDENCE: Probe ZzR3SecProbe.ZzXRefStmTableBadFirstRowThenGuard (output in scratchpad r3sec/p4-xrefstm-badrow-guard.txt). The table at 132 is `xref\n10 2\njunk junk junk\n%<200 dashes>\n12 1\n{row:10}\ntrailer\n<< /Size 13 >>`. The newest table (407) is `xref\n0 0\ntrailer\n<< /Size 13 /Root 1 0 R /XRefStm 132 >>`. MaxXRefSectionLength=128.

Head gives:
- `limit.xref-section-length @132`
- `Warning xref.hybrid-stream-lost @132: The /XRefStm of the cross-reference table at offset 407 names offset 132, a cross-reference table, which one of the reader's limits cut before its first row: ...`
- `Warning xref.section-unreadable @132: The cross-reference table /XRefStm names at offset 132 was read only in part: the row for object 10, at offset 142, is not an offset, a generation and n or f; the rows read were kept, ...`
- the record `/XRefStm@132 Table Read inc=True cut=True highest=-1`
- the rules `Error xref.section-malformed: ... the row for object 10, at offset 142, is not an offset...` and `Error xref.hybrid-stream-wrong: ... a cross-reference table, which one of the reader's limits cut before its first row, where ISO 32000-1 ...`

761ce0b gives 'a cross-reference table, whose rows were read in the stream's place' in both. It is reachable only with a limit below the table's first rows, or more than 64 MB of padding by default, as [10] was.
FIX (reviewer): Use `stream.CutByLimit && stream.HighestNumber < 0 && stream.Fault is null` (no row read, sound or not, before the cut), or `!stream.Incomplete`. Add a bad-first-row row to A_table_an_xrefstm_names_that_a_guard_cuts_before_its_rows_is_said_to_be_cut_there_and_never_one_that_cannot_be_read and to CrossReferenceRuleTests' cut-before-first-row theory.
--- VERDICT REASON: The defect is real, but the finding's primary fix is wrong. [10]'s fix (PdfFileReader.cs:3526) tests `stream.CutByLimit && stream.HighestNumber < 0`. HighestNumber is set at :5496, after the malformed-row branch (:5481-5493) has already returned, so it records sound and refused rows only. A table whose first row is no row, and which MaxXRefSectionLength then cuts, still has HighestNumber -1. The naming then says a limit "cut [it] before its first row". The xref.section-unreadable report beside it (and xref.section-malformed in the validator) names that same first row, which was reached inside the window and judged. The two reports contradict each other. The docs say "where the guard cut it before its first row, none of its rows read" (threat-model.md:417), "unless one of the reader's limits cut it before its first row" (diagnostics.md:115) and "unless a limit cut it before its first row" (validation-rules.md:60). That condition is narrower than the code's. At 761ce0b, :3429 always wrote "whose rows were read in the stream's place", so this wording comes in with [10]'s fix.

Narrowed: the fix the finding offers first, `&& stream.Fault is null`, would reopen [10]. ReadTrailerPastRows (:5253) sets Fault for a stray word past the cut, and does not set Incomplete. So a table the guard truly cut before its first row, with a stray word after the rows left unread, would go back to saying "whose rows were read in the stream's place". Only the alternative the finding offers, `!stream.Incomplete`, is right. On a table, Incomplete is set only by a row that is no row (:5488) and by the end of the file before the trailer once a row was read (:5133). The second cannot happen under windowFull. So `CutByLimit && HighestNumber < 0 && !Incomplete` means exactly "no row (sound, refused or no row) was reached before the cut", and with it the docs hold as written.

Nit: only wording is wrong. Nothing is served differently. The guard's own limit.xref-section-length and xref.checked-in-part sit beside it. It is reachable only with a caller limit below the table's first rows, or under the default with more than 64 MB of padding after a bad first row, as for [10]. A trailer guard does not reach this branch: with MaxTrailerLength=64 the section stays cut=False, because a table read in the stream's place does not have its trailer parsed.
VERDICT EVIDENCE: The probe was ZzRefuteBadRowGuard, an uncommitted test file, built Release with 0 warnings on detached 19122f0 and removed afterwards. Outputs are in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute-badrow/shapes.txt and more.txt, and in rule-rows-fixed.txt and rule-rows-faultnull.txt. rule-rows.txt in the same folder holds the last run, which is the Fault-is-null variant.

Reviewer's shape, at head, with MaxXRefSectionLength=128:
- `limit.xref-section-length @132`
- `xref.hybrid-stream-lost @132: ... names offset 132, a cross-reference table, which one of the reader's limits cut before its first row: ...`
- `xref.section-unreadable @132: ... the row for object 10, at offset 142, is not an offset, a generation and n or f; the rows read were kept, ...`
- record `/XRefStm@132 Table Read inc=True cut=True highest=-1`
- rules `xref.section-malformed: ... the row for object 10, at offset 142, ...` and `xref.hybrid-stream-wrong: ... which one of the reader's limits cut before its first row, ...`

The same file unguarded says "whose rows were read in the stream's place" (highest=12). The reviewer's output reproduces exactly.

Simpler shape C, `10 2 / junk junk junk / %<200> / trailer`, gives the same result.

Shape E, a bad only row with no guard, is inc=True cut=False highest=-1 and says "whose rows were read in the stream's place". That is the whole read's words for a table that read no row.

Shape G, a table truly cut before its first row (`%<200> / 10 1 / row / junk`), is inc=False cut=True highest=-1. It says "cut before its first row", which is correct, and its section-malformed reads "it holds the keyword junk at offset 364", so Fault is set.

I ran HybridFiles.NamingATableNeverRead with MaxXRefSectionLength=150 on three rows: R1 `10 1\njunk junk junk\n{300 spaces}`, R2 `{spaces}10 1\n{row:10}\njunk\n`, R3 `{spaces}10 1\n{row:10}\n`.
- Head: R1, R2 and R3 all say "cut before its first row". R1 is wrong.
- With `&& !stream.Incomplete`: R1 says "whose rows were read in the stream's place". R2 and R3 say "cut before its first row". All three are right.
- With `&& stream.Fault is null`: R1 and R2 say "whose rows were read in the stream's place". R2 is wrong; that is [10] again.

With the `!Incomplete` patch applied, I ran the classes matching *CrossReference*, *ReaderLimits*, *GuardCut* and *Hybrid*: 1532 tests, all passed. `git show 761ce0b:src/AdCodicem.Pdf/IO/PdfFileReader.cs` has only "whose rows were read in the stream's place" at :3429.
FIX (refuter): In PdfFileReader.cs:3526 make the condition `stream.CutByLimit && stream.HighestNumber < 0 && !stream.Incomplete`. Do not use `stream.Fault is null`: a stray word past the cut sets Fault in ReadTrailerPastRows, and that would bring [10] back. Extend the comment at :3521-3522 to say that a table whose first row is no row reached its first row before the cut and keeps the whole read's words.

No docs change is needed: the threat-model, diagnostics.md and validation-rules.md wording then matches the code.

Tests: add two rows to CrossReferenceRuleTests.An_xrefstm_naming_a_table_a_guard_cuts_before_its_first_row_is_wrong_in_words_that_say_none_of_its_rows_was_read.
- `(true, "10 1\njunk junk junk\n{spaces}", "a cross-reference table, whose rows were read in the stream's place")`. It fails at head and kills a mutant that drops `!Incomplete`.
- `(true, "{spaces}10 1\n{row:10}\njunk\n", "a cross-reference table, which one of the reader's limits cut before its first row")`. It holds against the `Fault is null` variant.

The CrossReferenceChainTests theory the finding names asserts that no xref.section-unreadable is reported, so a bad-first-row row does not fit it. Use a separate fact, or the rule-theory rows above.

Run ADR 53's catalog on the changed line. This is not a design choice for the maintainer.

######## [7] sections:test-name-lost-trailer-names-nothing-more — reviewer nit; refuter confirmed nit maintainer=False
LOCATION: tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:2169 (also listed in docs/threat-model.md:421)
CLAIM: 95d8e0f added a test whose name says the report says 'its lost trailer names nothing more'. The test asserts the opposite kind of message: the report says that what the sections the lost trailer would name index is looked for by rebuilding the index. Its /Prev twin at :2144 is named for that behavior.
EVIDENCE: `dotnet test ... --filter-method "*says_its_lost_trailer_names_nothing_more*"`: 1 passed. Its assertion (:2181-2182) expects the message to end '...; the rows read were kept, and what only the rest of the table, or the sections its trailer would name, index is looked for by rebuilding the index.' and then Text(document, 4) to come from a rebuild. The twin is named A_table_prev_names_whose_row_that_is_no_row_the_end_of_the_file_follows_says_what_its_lost_trailer_would_name_is_found_by_a_rebuild.
FIX (reviewer): Rename it to The_table_startxref_names_whose_row_that_is_no_row_the_end_of_the_file_follows_says_what_its_lost_trailer_would_name_is_found_by_a_rebuild, and update threat-model.md:421's list.
--- VERDICT REASON: The finding holds. Commit 95d8e0f added the R2 [8] fix and this test with it. The test's name says the report says "its lost trailer names nothing more". What the test asserts is the opposite: the report says the sections the lost trailer would name are looked for by rebuilding the index. It then finds object 4 that way, and only an older table that nothing names indexes object 4. In PartialFiles.CutFirst, that older table is exactly what the cut table's lost trailer would have named. The test's own comment and the /Prev twin at :2144 both describe that behavior, and the twin is named for it. The behavior is fully tested: the test passes on 19122f0, and r2-fix-result.json records that it fails before the fix. Only the identifier misstates the behavior, both in the test file and in the evidence list of threat-model.md:421. That makes it wording, so nit, not minor. No coverage or behavior is wrong.
VERDICT EVIDENCE: On origin/ccr-6d6c0ee6-dqm4lm-188 (19122f0), the test at tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:2169 is named `The_table_startxref_names_whose_row_that_is_no_row_the_end_of_the_file_follows_says_its_lost_trailer_names_nothing_more`. Its assertion at :2181-2182 expects the message to end "...the rows read were kept, and what only the rest of the table, or the sections its trailer would name, index is looked for by rebuilding the index." Then, at :2183-2185, WasRepaired is false, Text(document, 4) is "object 4", and WasRepaired becomes true.

Its comment at :2171-2173 says "its trailer is lost, and the report says what it would have named is looked for by rebuilding the index".

The XML doc of PartialFiles.CutFirst (PartialFiles.cs:54-57) says an older table, which nothing names, indexes object 4, and the cut table's trailer is lost.

The twin at :2144 is `A_table_prev_names_whose_row_that_is_no_row_the_end_of_the_file_follows_says_what_its_lost_trailer_would_name_is_found_by_a_rebuild`.

Both names appear in docs/threat-model.md:421, which I confirmed with grep.

I built the tests in Release and ran `dotnet test --project tests/AdCodicem.Pdf.Tests/AdCodicem.Pdf.Tests.csproj -c Release --no-build -- --filter-method "*lost_trailer*"`: total 4, succeeded 4, failed 0. That is the twin's three cases plus this test.

The phrase "nothing more" appears nowhere else in src/ or tests/, so no older wording explains the name.
FIX (refuter): Rename the test to `The_table_startxref_names_whose_row_that_is_no_row_the_end_of_the_file_follows_says_what_its_lost_trailer_would_name_is_found_by_a_rebuild`, matching its /Prev twin. Replace the old name with the new one in docs/threat-model.md:421's list of evidence tests. This changes no behavior or code, and the maintainer does not need to decide anything.

######## [8] headers:stray-typed-xref-header-ends-relocation — reviewer major; refuter narrowed major maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:4697 (acceptance `attempt.CutByValue || attempt.TypedXRef || (Kind == Table && IsTokenOnItsLine(...))`); header candidates come from FindSectionCandidates :4800 through TryReadHeaderBackwards, which reads the obj keyword's own line by its digits
CLAIM: b9de553 fixed R2 [2] (a stray word xref in a comment or a string ends the search near an offset) only for tables. The stream side is unchanged. FindSectionCandidates takes `N G obj` written inside a comment or a string on the obj keyword's line (`% 8 0 obj << /Type /XRef >>`, `(see 8 0 obj << /Type /XRef >> here)`), because the scan reads that line by its digits. Read from its number, that text is a /Type /XRef dictionary that has no stream data. It is therefore Malformed with TypedXRef, and since 0353e24 (R1-Q1) the nearest such candidate ends the search unconditionally. The comment or the string is then reported as a cross-reference stream that cannot be read. The real section, a few bytes farther, is never read. Behind a /Prev the chain ends, the file is rebuilt at opening, and the rebuild brings back an object the update freed. Behind an /XRefStm the older table's row stands for what the stream indexes, so an older revision is served silently, with no rebuild. Main dropped these candidates because they were Malformed without CutByLimit. The behavior is a regression against main and breaks the principle R2 [2] settled: a word in a comment or a string declares no section.
EVIDENCE: Probe ZzStrayTypedXRef (scratchpad r3h-probes/ZzHdrR3Stray.cs.txt). The file is NearbyFiles.UpdateBehindAStrayWord inlined: the newest /Prev names 2 bytes into the endobj before the update's table, with <between> in front of that table.
Branch 19122f0, between='% 8 0 obj << /Type /XRef >>\n', deleted=true: 'repaired at open True; object 3 = "three old"'; 'D Repair xref.offset-adjusted at 1530: The cross-reference section /Prev names at offset 1523 was found 7 bytes after it.'; 'D Warning xref.section-unreadable at 1530: The cross-reference stream /Prev names at offset 1523, found at offset 1530, is there but cannot be read: its dictionary is not followed by stream data. ...'; 'D Repair xref.rebuilt'; 'F Error xref.section-malformed at offset 1530'.
between='6 0 obj\n(see 8 0 obj << /Type /XRef >> here)\nendobj\n': the same at 1541 ('found 18 bytes after it'), rebuilt, object 3 = "three old".
Main 21a0298, same files: 'repaired at open False; object 3 = null'; 'xref.offset-adjusted at 1556: ... found 33 bytes from there' (string: 57 bytes); only xref.section-shifted.
Hybrid probe ZzStrayTypedXRefHybrid (HybridBehindAStrayWord inlined), between='% 8 0 obj << /Type /XRef >>\n'. Branch: 'repaired at open False; object 7 = "old seven"'; 'xref.section-unreadable at 1626: The cross-reference stream /XRefStm names at offset 1619, found at offset 1626, is there but cannot be read: its dictionary is not followed by stream data.'; 'F Error xref.section-malformed at offset 1626'. The string variant gives the same at 1633. Main: object 7 = "new seven", 'found 33 bytes from there' (string: 48).
Control '% 8 0 obj << /W [1 2 1] >>' (untyped): the branch finds the table 32 bytes off, as main does.
Fix experiment: gating TypedXRef on IsTokenOnItsLine gives main's outcomes in every row above (no rebuild, object 3 null or "three new"). CrossReferenceChainTests, CrossReferenceRuleTests, HostileInputTests, ReaderLimitsTests, GuardCutValueTests and DocumentReaderTests: 2,214 passed, 0 failed. The experiment was reverted.
FIX (reviewer): At :4697, apply the line test to a header candidate too: `attempt.CutByValue || ((attempt.TypedXRef || attempt.Kind == XRefSectionKind.Table) && IsTokenOnItsLine(window.Memory.Span, (int)(candidate - (start - lead))))`. A stream found moved then declares itself only where its header's number is a token a load lexes from the start of its line. That excludes a comment, a string on the line, and digits glued to a regular byte (`x8 0 obj`, #171). A genuine comment header such as `8 % c\n0 obj` is still taken. Optionally gate CutByValue the same way. Widen the IsTokenOnItsLine summary and the TryRelocateXRefSection remarks (:4583-4585). Widen the four texts that now say only 'an xref keyword a load reads ...' to say a stream's header is held to the same test: lazy-reading.md:170-172, threat-model.md:419, reference/diagnostics.md:110 and validation-rules.md:56. Add CrossReferenceChainTests rows for the comment, string and glued shapes, behind a /Prev (deleted object stays null, no rebuild, no section-unreadable) and behind an /XRefStm ("new seven", no section-unreadable). Add matching CrossReferenceRuleTests rows asserting no xref.section-malformed.
--- VERDICT REASON: The finding holds. I rebuilt it on the branch at 19122f0 and on main at 21a0298, and both differences reproduce.

**Cause.** b9de553 applied the line test to the table side only. At src/AdCodicem.Pdf/IO/PdfFileReader.cs:4697 the acceptance still takes any Malformed candidate with TypedXRef. FindSectionCandidates collects headers through TryReadHeaderBackwards, which reads the obj keyword's line by its digits. So these texts count as a /Type /XRef stream with no stream data:
- `N G obj << /Type /XRef >>` in a comment;
- the same text in a string on the obj keyword's line;
- digits glued to a regular byte (`x8 0 obj`).

Since 0353e24 (R1-Q1), the first such candidate in nearest-first order ends the search. Main required CutByLimit and dropped them.

**The narrowing.** The claim says the hybrid case serves an older revision "silently". It is not silent: a false xref.section-unreadable warning and an Error xref.section-malformed finding blame the comment or the string. No report says object 7 now comes from an older revision.

**Why major, and not a #171 question.** This is the same class as R2 [2], which was settled major, and it breaks that fix's principle: a word in a comment or a string declares no section. It is a regression against main. It serves an object the update freed, and an older revision behind an /XRefStm, and it makes a false Error. It needs an imprecise offset and such text within 512 bytes, nearer than the real section. The file still opens.

#171 and the threat model's known gap cover reading a comment's header in the scan, in the search near a row and in the stream-end rule. Main has that behavior too. What is new is that, here, a Malformed one ends the chain. The fix only gates whether that ends the search, as b9de553 does for tables. It changes neither what is collected nor whether a candidate that reads is taken, so the maintainer has nothing to decide. Lexing the obj keyword's own line everywhere stays #171's question.

The docs agree with the code as it stands. They limit the line test to the xref keyword and list "a stream of /Type /XRef" without it. The fix must widen them.
VERDICT EVIDENCE: **Probes.** I ran the finding's probes (ZzHdrR3Stray, ZzHdrR3StrayHybrid) and my own, ZzRefStrayHdr: the deleted-object file, plus glued and genuine header shapes. All were uncommitted and are now removed; the outputs are in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r3ref-stray/.

**Branch 19122f0, deleted-object file (`/Prev` names 2 bytes into the endobj).**
- `% 8 0 obj << /Type /XRef >>`:
  - "repaired at open True; object 3 = \"three old\""
  - "xref.offset-adjusted at 1530: ... /Prev names at offset 1523 was found 7 bytes after it."
  - "xref.section-unreadable at 1530: The cross-reference stream /Prev names at offset 1523, found at offset 1530, is there but cannot be read: its dictionary is not followed by stream data."
  - xref.rebuilt; F Error xref.section-malformed.
- String shape `6 0 obj\n(see 8 0 obj << /Type /XRef >> here)\nendobj`: the same at 1541 ("18 bytes after it"), rebuilt, "three old".
- My file: `%8 0 obj` and `x8 0 obj` were each found 6 bytes after, section-unreadable, rebuilt, "three old".

**Main 21a0298, same files.**
- "repaired at open False; object 3 = null".
- "found 33 bytes from there" (comment) and "57 bytes" (string); only xref.section-shifted.

**Hybrid file, `% 8 0 obj << /Type /XRef >>` between the /XRefStm offset and the stream.**
- Branch:
  - "repaired at open False; object 7 = \"old seven\""
  - "xref.section-unreadable at 1626: The cross-reference stream /XRefStm names at offset 1619, found at offset 1626, is there but cannot be read ..."
  - F Error xref.section-malformed.
  - The string variant gives the same at 1633.
- Main: "object 7 = \"new seven\"", found 33 bytes off (string: 48), section-shifted only.

**Untyped control `% 8 0 obj << /W [1 2 1] >>`.** The branch finds the table 32 bytes off, as main does.

**Fix experiment.** The line test was gated on `TypedXRef || Kind == Table`.
- Comment, `%8`, string and `x8` shapes: "repaired at open False; object 3 = null", found 33/32/57/32 bytes after, section-shifted only.
- Hybrid shapes: "new seven" at 33 and 48 bytes.
- Genuine headers are still taken as R1-Q1 decided: `8 % c\n0 obj << /Type /XRef >>` (5 bytes), `endobj 8 0 obj << /Type /XRef >>` (12 bytes) and `8 0 obj << /Type /XRef >>` (5 bytes) each give section-unreadable and a rebuild.

**Test run with the fix.** CrossReferenceChainTests, CrossReferenceRuleTests, HostileInputTests, ReaderLimitsTests, GuardCutValueTests, DocumentReaderTests, StreamLengthTests and ReadOrderTests: 2,605 total, 0 failed, 2 skipped. No existing test exercises the dropped path. The experiment was reverted and the worktree is clean.
FIX (refuter): **Code.** At PdfFileReader.cs:4697, apply the line test to a stream candidate as to a table:

```
attempt.CutByValue || ((attempt.TypedXRef || attempt.Kind == XRefSectionKind.Table) && IsTokenOnItsLine(window.Memory.Span, (int)(candidate - (start - lead))))
```

A Malformed /Type /XRef candidate then declares itself only where its header's number is a token a load lexes from the start of its line. That excludes a comment, a string on the line and glued digits. A header with comments between its tokens, or one after other tokens on its line, is still taken. A candidate that reads is still taken wherever it lies, as on main. The cost stays bounded by MaxRelocationCandidates. Gating CutByValue the same way is optional: main already took it with CutByLimit, so it is not a regression.

**Code comments.** Widen:
- the IsTokenOnItsLine summary, so it also covers a header's number;
- the TryRelocateXRefSection remarks (:4583-4585) and the acceptance comment (:4687-4693).

**Docs.** Widen the texts that apply the line test to the xref keyword only:
- lazy-reading.md:170-172;
- threat-model.md:419;
- reference/diagnostics.md:110;
- validation-rules.md:56.

Say that a stream's header is held to the same test, and that a header in a comment or a string is no section found moved.

**Tests.**
- CrossReferenceChainTests rows for the comment, string and `x8` shapes:
  - behind a /Prev: the deleted object stays null, no rebuild, no section-unreadable, found 33/57/32 bytes off;
  - behind an /XRefStm: "new seven", no section-unreadable.
- A control row: `8 % c\n0 obj << /Type /XRef >>` is still found moved and unreadable.
- Matching CrossReferenceRuleTests rows asserting no xref.section-malformed.

The maintainer has nothing to decide: this implements R1-Q1 and R2 [2] as written.

######## [9] headers:endstream-in-header-comment-ends-data-first — reviewer minor; refuter confirmed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/StreamEnds.cs:122 (FindHeader asked only for an obj before the first endstream); docs/website/docs/concepts/diagnostics.md:43-53 and :62-65; docs/threat-model.md:526
CLAIM: B19-7 decided that a damaged stream stops at a header with comments between its tokens. A header's span can now hold an endstream inside such a comment, which was impossible before the batch, when only digits and white space lay between the tokens. The search takes the raw keyword at the first endstream and looks only for a header whose obj lies before it. So `12 % endstream\n0 obj`, which a load reads as object 12's header, does not stop the stream. Its data runs 6 bytes into object 12's header. With the object's own endobj before that header, the data takes in that endobj too, the report becomes 'ended after N', and the object's endobj is judged missing. That is the outcome R1 [16] removed for an endobj in the same comment. The docs say the search runs up to the first header the bytes hold, and that text which reads as a header stops the search, comments included. Main gives the same bytes, since the comment made no header there, so this is no regression. It is a hole in B19-7's rule, and the new texts misstate it.
EVIDENCE: Probe ZzEndstreamInHeaderComment (scratchpad r3h-probes/ZzHdrR3.cs.txt). Object 5 is '<< /Length 9999999 >> stream' with F bytes of A, then the text below, then '\n(twelve)\nendobj'. The index places 1, 2, 5 and 6, not 12. Branch 19122f0 at F=100 (the same at F=20,000):
- '\n12 % endstream\n0 obj': 'data 106 (filler+6); endobj missing True; rule True; stream.length-invalid @163: The stream declared 9999999 bytes but ended after 106.'
- '\nendobj\n12 % endstream\n0 obj': 'data 113 (filler+13); endobj missing True; rule True; ... but ended after 113.'
- '\n12 0 % endstream\nobj': 'data 108 (filler+8) ... ended after 108.'
- Controls: '\n12 0 obj' gives 'data 101; ... no endstream follows its data before the next object, at 264; the 101 bytes up to that object are taken as its data.' '\n12 % endobj\n0 obj' gives the same 101 bytes. WindowEdgeTests' row '\nendobj\n12 % endobj\n0 obj' expects data = filler with 'its object's endobj ends it'.
Main 21a0298 gives the same bytes for the endstream rows: 106, 113 and 108.
The split fuzz, which includes '% endstream' pieces, found the outcome deterministic: 0 differences.
FIX (reviewer): The maintainer decides between two options. (a) Apply the rule: when the first endstream of a read lies past where a number the header rule still carries, or a run of digits in the read, may start a header, search on until that candidate is decided. Take the header where it starts before the keyword. The cost stays within the search's own reads, which hold the header's lines anyway. Add StreamLengthTests or WindowEdgeTests rows for '\nendobj\n12 % endstream\n0 obj', expecting data = filler, stream.truncated and endobj present. (b) Keep the behavior and document it in concepts/diagnostics.md:50-53 and threat-model.md:526: an endstream a comment between a header's tokens holds ends the data before that header is decided.
--- VERDICT REASON: The finding holds, and the evidence is slightly stronger than the reviewer stated. StreamEnds.SearchStretch (src/AdCodicem.Pdf/IO/StreamEnds.cs:121-122) takes the raw first `endstream` in a read. It then asks HeaderText.FindHeader only for a header whose `obj` ends before that keyword. Since B19-7, the span between a header's number and its `obj` can hold comment lines. So `12 % endstream\n0 obj`, which a load and the rebuild's scan both take as object 12's header, does not stop a damaged stream's search. The data runs into that header: 6 bytes, or 13 with the object's own `endobj` before it. The object's `endobj` is then judged missing, and the report is `stream.length-invalid ... ended after N` rather than `stream.truncated ... its object's endobj ends it`.

New evidence beyond the reviewer's: on the branch, the rebuild's scan and the load do take object 12 at the header's start. So the same bytes give two outcomes depending on the index. When the index (written or rebuilt) places 12, the data stops at the header: 100 bytes, stream.truncated, endobj present. When the written index leaves 12 out, the data runs 13 bytes into the header and the endobj is judged missing. B19-7 said "the rule now follows the scan" and closed exactly this gap. On main both cases give 106/113, so there is no regression in what a reader serves when it keeps the written index. The search is deterministic: the parser path (F=100) and the file path (F=20,000) give the same result.

The texts state the rule as option (a) would implement it, and the code does not:
- concepts/diagnostics.md:43-46: "up to ... the first object header ... whichever comes first".
- concepts/diagnostics.md:50-53: text that reads as a header stops the search, "Comments may lie between the tokens, as a load reads them".
- threat-model.md:526: "up to the nearer of the first object header the bytes hold".
- The XML doc of PdfFileReader.SearchEndStream at :2415: "before the first object header the bytes hold".
Only the internal remark at :2425 ("a header only before the endstream it holds") and FindHeader's "whose obj ends before" describe what the code does.

No earlier round, decision, residual or debt (#320-#330) covers it. R1 [16] was the endobj in the same place, rated major because the outcome changed with where the reads split; this case does not split. Severity is minor: the input is contrived (a stream that lost its endstream, then a header whose comment holds `endstream`), the result is deterministic, and it matches main for a written index. Its concrete defect is a hole in B19-7's extension that the texts state wrongly.
VERDICT EVIDENCE: Probe: uncommitted test ZzEshcRefute, my own rebuild, saved as /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r3ref-eshc/ZzEshcRefute.cs.txt. The file holds objects 1 (catalog, /Metadata 5 0 R) and 2. Object 5 is `5 0 obj\n<< /Length 9999999 >>\nstream\n` + F x 'A' + the text below + `\n(twelve)\nendobj\n`. Object 6 is `(six)`. The table places 1, 2, 5 and 6 (and 12 for place12=True). In the rebuild case, the table is garbage and startxref points to 999999. Each run calls GetObject(5).

Branch 19122f0 (Release, 0 warnings), output in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r3ref-eshc/out.txt, identical at F=100 and F=20,000:
- written index without 12, `\n12 % endstream\n0 obj`: data F+6, past the filler `\n12 % `. endobjMissing True; object.endobj-missing True. `stream.length-invalid: The stream declared 9999999 bytes but ended after 106.`
- same index, `\nendobj\n12 % endstream\n0 obj`: data F+13 (`\nendobj\n12 % `); endobjMissing True; rule True; `... but ended after 113.`
- same index, `\n12 0 % endstream\nobj`: data F+8.
- same index, `\nendobj\n12 % c\n% endstream\n0 obj`: data F+17.
- controls: `\nendobj\n12 % endobj\n0 obj` and `\nendobj\n12 0 obj` give data F, endobjMissing False, `stream.truncated ... before the next object, at 271: its object's endobj ends it, after 100 bytes.`
- index placing 12, or a rebuilt index: `\nendobj\n12 % endstream\n0 obj` gives data F, endobjMissing False, stream.truncated, and obj12 "twelve" @271. The rebuild's scan and the load take that header (rebuilt True, obj12 "twelve"). `\n12 % endstream\n0 obj` gives data F+1, stream.length-invalid "the 101 bytes up to that object".

Main 21a0298, the same probe built from a git archive export:
- written index without 12: 106, 113, 108 and 117, the same as the branch.
- rebuilt index: still 106 and 113, with obj12 null, because main's scan and load took no comment header.
So the branch serves the same bytes as main for a written index. Only on the branch do a written index and a rebuilt one disagree on these bytes.

Code: StreamEnds.cs:121-122 (`keyword = span.IndexOf(EndStreamKeyword); header = ... text.FindHeader(keyword >= 0 ? keyword : span.Length, endsRange)`); HeaderText.FindHeader searches `_bytes[from..before]` for `obj`.
FIX (refuter): Make the code follow B19-7 and the texts as written (the reviewer's option (a)). When the first `endstream` of a read lies inside a comment, look at the line it sits on, lexed as HeaderText already lexes a line (LineTokens, with the line state carried in). If that line's tokens before the comment are one or two runs of digits, or none, or the read carries numbers (CarriedStarts), a header that starts before the keyword may still be pending. Do not take the keyword yet. Read on past it, over the rest of its line and any blank or comment-only lines, to the first line with a token outside a comment. Then call FindHeader up to that line's `obj`. If a header starts before the keyword, it stops the search, its tail taken from the copy at its start (EndObjTailCopies). Otherwise the keyword ends the data.

An `endstream` that is a token, outside a comment, ends any candidate, since no walk crosses it. The extra reading stays within the search's range (the next object or the end of the file), which the search would have read anyway without the keyword. The parser path (PdfObjectParser.cs:1106) must hand such an undecided keyword to the file search, as it already does for a candidate cut at the end of its bytes.

Tests: StreamLengthTests or WindowEdgeTests rows, at a filler inside and past the window:
- `\nendobj\n12 % endstream\n0 obj`: expect data = filler, stream.truncated, endobj present.
- `\n12 % endstream\n0 obj`: expect data = filler+1, length-invalid at the header.
- control `\nAAAA % endstream\n0 obj`: the keyword still ends the data.
Add a property: the data ends at the same place whether the index places the header's object or not. Also correct the internal remark at PdfFileReader.cs:2425.

If the maintainer judges this reading-on not worth its complexity, the alternative is option (b): keep the behavior and state it in concepts/diagnostics.md:43-53, threat-model.md:526 and the SearchEndStream XML doc at PdfFileReader.cs:2415. The text would say that an `endstream` a comment between a header's tokens holds ends the data before that header is decided, and so data that a rebuilt index stops at the header runs into it with the written index. That narrows B19-7 and would need the maintainer's decision. Option (a) needs no new question.

######## [10] validator-tests:hybrid-wrong-blames-file-for-guard-cut-prev — reviewer major; refuter narrowed major maintainer=False
LOCATION: src/AdCodicem.Pdf/Validation/Rules/HybridStreamWrongRule.cs:43-47; src/AdCodicem.Pdf/IO/PdfFileReader.cs:3458 (leaveAt through KeepsItsPlace) and :3652/:3667 (DeclaresStreamAt's guard arm); docs/website/docs/reference/validation-rules.md:60, docs/website/docs/reference/diagnostics.md:115, docs/website/docs/concepts/lazy-reading.md:121; tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:3719
CLAIM: R2a ([0]) keeps the /XRefStm's search stopping where the /Prev 'keeps its place' because a guard cut the object there. In that case the /Prev's record is Malformed with CutByLimit, the /XRefStm becomes a NamedAgain with NamesWhatPreviousReads=true, and HybridStreamWrongRule's ReadAs arm says 'what its /Prev names too, which holds no section the chain could read'. That is an Error worded as the file's fault, though the guard caused it. With the limit raised, the same file has no hybrid-stream-wrong finding: the /XRefStm finds its stream 212 bytes away. Under default limits this is also a regression against main 21a0298. Main found the stream and reported only the guard. The branch reports xref.hybrid-stream-lost, rebuilds the index as the document opens (Repair), and adds the Error. The [6] fix covered only the count guard ('past the guard that ended the chain there'). Three doc pages say the /Prev keeps its place only at 'a section there, not a font or a string' and leave out the guard case: lazy-reading.md:121, diagnostics.md:115, and validation-rules.md:60, whose list of what the finding says has no guard-cut-/Prev case either. The R2a test rows that hold the guard stop (stops=true) assert only the section records, never the finding.
EVIDENCE: Probe ZzR3vtGuardCutPrev (uncommitted, now deleted) on 19122f0. File: HybridFiles.NamingAnObjectBesideTheStream with object 4 = '<< /Pad (70,000 dashes) /Type /Font ... >>': oldest table 1481, stream 1649, object 4 at 1891, newest 73192, newest trailer '/Prev 1891 /XRefStm 1861'.
Branch, default limits:
  D xref.hybrid-stream-lost [Warning] @1861: The /XRefStm of the cross-reference table at offset 73192 names offset 1861, what its /Prev names too, which the chain looks for through that /Prev: ...
  D limit.trailer [Warning] @1891 ...
  D xref.rebuilt [Repair] @-1
  D object.redefined [Information]
  S /XRefStm named=1861 NamedAgain prevReads=True readAs=/Prev@1891:Malformed:cut=True
  F xref.hybrid-stream-wrong [Error] @73192: The /XRefStm of the cross-reference table at offset 73192 names offset 1861, what its /Prev names too, which holds no section the chain could read, where ISO 32000-1 names the cross-reference stream of the table's revision (Table 19): what only that stream indexes is lost.
  F xref.checked-in-part [Information] @1891
Branch, MaxTrailerLength 1 MB: D xref.offset-adjusted @1649 (the /XRefStm found 212 bytes before), D xref.offset-adjusted @1481; findings are only size-wrong and two section-shifted, with no hybrid-stream-wrong.
The same with object 4's dictionary at 2,000 bytes and MaxTrailerLength 1024: the same Error at default, none at 1 MB.
Main 21a0298, the same bytes at default limits: D xref.offset-adjusted @1649 (found -212 bytes), D limit.trailer @1891; F xref.section-shifted @1649, F xref.checked-in-part @1891. No hybrid-stream-lost, no rebuild, no Error.
Same-offset variant (/XRefStm 1891 or 1889, the white space): at default the Error still says 'which holds no section the chain could read'. At 1 MB it says 'the section its /Prev names too, which the chain read through that /Prev'.
FIX (reviewer): Where the /Prev's record was cut by a guard (ReadAs { State: Malformed, CutByLimit: true }), word the naming as the guard's, after the count-guard precedent: in the reader, NamesWhatPreviousReads=false and NamesNoStream = 'what its /Prev names too, past the limit that cut it there', or reword in HybridStreamWrongRule's ReadAs arm. Where the /XRefStm wrote another offset than the /Prev's and only its search came to the cut object, leave the naming to the guard (no Error, the index left to the guard rather than marked incomplete). This is how #264 leaves to the guard an /XRefStm that names an offset in the file the chain did not reach. If that needs a maintainer decision, at least make the words say the limit hides it. Assert the hybrid-stream-wrong text (or its absence) in the stops=true rows of An_xrefstm_stops_short_of_its_stream_only_where_its_table_s_prev_keeps_its_place. Add the guard case to validation-rules.md:60, diagnostics.md:115 and lazy-reading.md:121.
--- VERDICT REASON: The finding reproduces exactly at 19122f0. When the /Prev names a non-section object whose dictionary MaxTrailerLength cuts before its /Type, KeepsItsPlace returns true through DeclaresStreamAt's guard arm (PdfFileReader.cs:3652, and :3458 for leaveAt). The /XRefStm's search near 1861 then stops at that object, 30 bytes off. It never reaches the real stream 212 bytes away.

Two things follow:
- The reader reports xref.hybrid-stream-lost (Warning) and marks the index incomplete, so the index is rebuilt at opening (Repair).
- HybridStreamWrongRule's ReadAs arm (:43-47) sees a Malformed, CutByLimit /Prev and gives an Error: "what its /Prev names too, which holds no section the chain could read".

With the limit raised, none of this happens. The /XRefStm finds the stream, and the only findings are size-wrong and section-shifted x2. So the Error's existence depends on what the guard hid, and the guard's cut is blamed on the file, against invariant 12 and ADR 34.

On main 21a0298, the same bytes give only xref.offset-adjusted for the stream, limit.trailer and checked-in-part. There is no Warning, no rebuild and no Error, so this is a regression against main.

**Narrowings:**
1. **Not new in round 2.** 761ce0b gives the same output at default limits, because the round 2 [0] bug stopped the search unconditionally. Round 2a narrowed the stop and deliberately kept the guard arm, as round 2 [0]'s refuter suggested ("a dictionary a guard cut before it gave /Type can count as kept"). It did not consider what that arm then reports. That stop is a settled point. Its reporting is not settled, and it breaks ADR 34.
2. **The same-offset variant is only a wording point.** In that variant (/XRefStm 1891 = /Prev), branch (c) is decided whatever the guard does. The Error stands there, as in round 2 [6]. Only "holds no section the chain could read" misstates what the guard hid; the [6] precedent makes that a nit.
3. **No wrong object is served.** On the branch, objects 5, 7 and 1 come out right after the rebuild; on main, object 1 reads PdfNull under the guard. The defect is in the reports (a Warning and an Error the file does not earn) and in a rebuild charged to the hybrid stream.

**Doc and test claims hold:**
- lazy-reading.md:121 ("it names a section there, not a font or a string") and diagnostics.md:115 ("a section there and no font or string") leave out the guard-cut object the code stops at.
- validation-rules.md:60 lists no "the limit hides it" wording for a /Prev cut by a guard.
- The rule's own remarks name only the section count guard.
- The stops=true rows of An_xrefstm_stops_short_of_its_stream_only_where_its_table_s_prev_keeps_its_place (CrossReferenceChainTests.cs:3719) assert only the section records, never the diagnostics or the finding.

**Severity:** major on the brief's scale. It is a wrong report (an Error and a Warning blaming the file for a guard's cut, under default limits) and a regression against main, though the shape is contrived: both links are wrong, and an object holds more than 64 KB before its /Type.
VERDICT EVIDENCE: My own probe on 19122f0 (uncommitted, now deleted) built HybridFiles.NamingAnObjectBesideTheStream with object 4 = '<< /Pad (70,000 dashes) /Type /Font ... >>'. Layout: oldest 1481, stream 1649, four 1891, newest 73192; trailer '/Prev 1891 /XRefStm 1861'. The bytes were saved and read on 19122f0, 761ce0b and 21a0298, each built in its own scratch worktree (since removed).

**19122f0, default limits** (WasRepaired after open = True):
- D Warning xref.hybrid-stream-lost at 1861: '...names offset 1861, what its /Prev names too, which the chain looks for through that /Prev...'
- D Warning limit.trailer at 1891
- D Repair xref.rebuilt
- D Information object.redefined
- S /XRefStm named=1861 NamedAgain prevReads=True readAs=/Prev@1891:Malformed:cut=True
- S /Prev 1891 Malformed cut=True
- obj 5 = five, obj 7 = new seven, obj 1 = PdfDictionary
- F xref.hybrid-stream-wrong [Error] '...names offset 1861, what its /Prev names too, which holds no section the chain could read, where ISO 32000-1 names ... what only that stream indexes is lost.'
- F xref.checked-in-part @1891

**19122f0, MaxTrailerLength 1 MB** (WasRepaired = False):
- D Repair xref.offset-adjusted at 1649 ('/XRefStm names at offset 1861 was found 212 bytes before it')
- D Repair xref.offset-adjusted at 1481
- /XRefStm Relocated Stream 1649, /Prev Relocated Table 1481
- F file.size-wrong, F section-shifted x2; no hybrid-stream-wrong

**Small variant** (2,000-byte /Pad, MaxTrailerLength 1024): the same Warning, rebuild and Error. The same file at default limits gives no hybrid-stream-wrong.

**21a0298, the same bytes, default limits** (WasRepaired = False):
- D Repair xref.offset-adjusted at 1649 ('found -212 bytes from there')
- D Warning limit.trailer at 1891
- /XRefStm Relocated Stream 1649, /Prev Malformed cut=True
- obj 5 five, obj 7 new seven, obj 1 PdfNull
- F section-shifted @1649, F checked-in-part @1891
- No hybrid-stream-lost, no rebuild, no Error. The small variant at 1024 is the same.

**761ce0b, default limits:** identical to 19122f0 (Warning, rebuild, the same Error). At 1 MB, 761ce0b also stops there and reports 'the section its /Prev names too, which the chain read through that /Prev' (the round 2 [0] bug).

**Same-offset variant** (/XRefStm 1891):
- 19122f0, default: Error '...names offset 1891, what its /Prev names too, which holds no section the chain could read...'
- 19122f0, 1 MB: the /Prev is relocated to the stream at 1649, and the Error says '...the section its /Prev names too, which the chain read through that /Prev...'
- 21a0298, default: only limit.trailer and checked-in-part; objects 5 and 7 read PdfNull.

**Code:**
- KeepsItsPlace (:3652) is true through DeclaresStreamAt's cut arm (`!whole && value is PdfDictionary cutShort && !cutShort.ContainsKey(PdfName.Type)`).
- leaveAt is passed at :3458.
- The count-guard fix ([6]) at :3228-3232 is the only place NamesWhatPreviousReads is cleared for a guard.
- HybridStreamWrongRule:43-47 treats any state other than Read or Relocated, CutByLimit included, as 'holds no section the chain could read'.
- The test at CrossReferenceChainTests.cs:3754-3760 (stops=true) asserts sections only.
FIX (refuter): **1. Different offsets** (the /XRefStm writes an offset other than the /Prev's, and the stop came only from DeclaresStreamAt's guard arm):
- Have DeclaresStreamAt/KeepsItsPlace report "guard-cut" apart from "declares itself a section".
- Where the search stopped there and the /XRefStm names a different offset, leave the naming to the guard, as #264's documented rule does for an /XRefStm naming an offset in the file the chain did not reach: no xref.hybrid-stream-lost, no NamesNoStream (so no Error), and the index not marked incomplete. The /Prev's limit.trailer and checked-in-part already cover it.
- This keeps round 2 [0]'s settled stop and follows ADR 34 and #264, with no new decision.
- The alternative, not stopping at a guard-cut place at all, restores main's output but reopens [0]'s settled guard arm. That is a maintainer choice.

**2. Same offset or white space before it** (branch (c), whatever the guard does):
- Keep the Error, worded as the guard's, after [6]: when the /Prev's record ends CutByLimit with NamesWhatPreviousReads=true, set NamesWhatPreviousReads=false and NamesNoStream='what its /Prev names too, past the limit that cut it there'. Do this in the reader where the /Prev's record is set, or as a CutByLimit arm in HybridStreamWrongRule.

**3. Docs:**
- Add the guard-cut case to lazy-reading.md:121, diagnostics.md:115, validation-rules.md:60, HybridStreamWrongRule's remarks, and the NamesWhatPreviousReads/NamesNoStream XML docs.

**4. Tests:**
- In the stops=true rows of An_xrefstm_stops_short_of_its_stream_only_where_its_table_s_prev_keeps_its_place, assert the diagnostics and the hybrid-stream-wrong finding (its absence for the different-offset file, the limit's wording for a same-offset row).
- Add a same-offset guard row.
- Each new assertion must fail at 19122f0.

**Minimum if the reporting choice is deferred:** at least make the words say the limit hides it, in both shapes, and fix the docs and tests.

######## [11] validator-tests:known-without-a-look-test-holds-memo-not-claim — reviewer minor; refuter narrowed minor maintainer=False
LOCATION: tests/AdCodicem.Pdf.Tests/HostileInputTests.cs:4591 (A_prev_looping_back_through_the_spaces_before_a_table_it_named_there_is_known_without_a_look); code it targets src/AdCodicem.Pdf/IO/PdfFileReader.cs:3174
CLAIM: The test claims the loop's place is 'known from what the chain read there, without a look at the spaces again', and counts bytes read to show it. In its three-table shape (ChainNamingTheSpacesBeforeTheMiddle), only one other position is probed between the newest /Prev's probe of middle-3 and the loop. So the two-entry PaddingAt memo still holds middle-3, and a look costs no byte. Replacing the arm '{ } there when there.Offset == looped => looped + there.Padding' with StartOf(looped), a real look, passes the test. The test holds the memo, not the claim. ADR 53's arm-removal mutant is killed by other tests, because it places the loop in the white space; the look itself is not.
EVIDENCE: Mutant copy of 19122f0, switch M9: the arm returns StartOf(looped). HostileInputTests.A_prev_looping_back_through_the_spaces_before_a_table_it_named_there_is_known_without_a_look PASSES under M9, and so do every A_prev_looping_back* and Each_offset_that* test (15 run, 14 passed; the one failure is my probe below). Probe ZzR3vtLongLoopBytes: chain N -> /Prev M-3 -> M -> /Prev A -> A -> /Prev B -> B -> /Prev M-3 (or M), so two other probes come after M's. Unmutated: 'repeated=3230 atKeyword=3230', loop @418 in both. M9: 'repeated=3262 atKeyword=3230', 32 bytes more, one probe; the test fails.
FIX (reviewer): Lengthen the test's chain so at least two other positions are probed between the first naming of middle-3 and the loop (as in the probe: two tables read between). Alternatively, assert through a source that records read offsets that no read starts at middle-3 once the middle table was read.
--- VERDICT REASON: The gap is real, and I reproduced it on 19122f0. Take HostileInputTests.A_prev_looping_back_through_the_spaces_before_a_table_it_named_there_is_known_without_a_look (tests/AdCodicem.Pdf.Tests/HostileInputTests.cs:4591). It cannot tell the loop arm at src/AdCodicem.Pdf/IO/PdfFileReader.cs:3174, which places the loop from the record with no read, from a call to StartOf(looped). StartOf goes through PaddingAt, and in the test's three-table shape PaddingAt answers from its two-entry memo.

The order of probes in ChainNamingTheSpacesBeforeTheMiddle(3, oldestPrev: 3) is newest, then middle-3, then oldest. When the oldest /Prev loops, the memo still holds (oldest, middle-3). A look therefore costs 0 bytes, and the two files read the same number of bytes whichever way the arm is written. Under the mutant the test passes, and so does the whole unit project. No other test there kills it.

Once two other positions are probed after middle-3, the memo has dropped it, and the mutant reads one more 32-byte probe. So the mutant is not equivalent, and the test's name and comment ("known from what the chain read there, without a look at the spaces again") claim more than it holds.

I narrow the finding in two ways:
- **The cost it fails to guard is bounded.** A loop ends the chain, so the mutant adds at most one probe per document. That is 32 bytes here, and at most about 84 KB (windows growing up to MaxTrailerLength, 64 KiB) when long white space comes before the section. Objects, reports and the loop's placement are unchanged: the loop stays at 318/457/596 in every shape.
- **The finding's alternative fix does not work on its own.** In the short shape a look answered by the memo makes no read at all, so a source that logs read offsets would see nothing at middle-3, with the mutant or without it. Only a shape that pushes middle-3 out of the memo can kill the mutant.

This is minor, as a test that does not hold its claim. It is not a design question for the maintainer.
VERDICT EVIDENCE: My probe is /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/ZzRefLookTests.cs.txt, built on a detached origin/ccr-6d6c0ee6-dqm4lm-188 at 19122f0. The long chain is newest, then /Prev middle-3, then middle, then T(k-1) ... T0 with `between` tables, and T0's /Prev names middle-3 ("repeated") or middle ("atKeyword").

Unmutated (/tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute-look-head.txt):
- SHORT (the test's own shape): repeated=2668 loop@318, atKeyword=2668 loop@318.
- LONG between=1: 2679 / 2679.
- LONG between=2: 3676 / 3676, loop@457.
- LONG between=3: 4812 / 4812, loop@596.

Mutant: line 3174's arm changed to `{ } there when there.Offset == looped => StartOf(looped),` (/tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute-look-m9.txt):
- SHORT: 2668 / 2668. The targeted test passes.
- LONG between=1: 2679 / 2679.
- LONG between=2: repeated=3708 against atKeyword=3676, 32 bytes more.
- LONG between=3: 4844 against 4812.
- The loop is placed at the same position and gives the same xref.chain-cycle text in every case.

The whole AdCodicem.Pdf.Tests project under the mutant gives: total 7495, failed 0, succeeded 7475, skipped 20 (/tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute-look-m9-suite.txt).

Code trace: PaddingAt (PdfFileReader.cs:4871-4905) keeps (Last, Before) in the order it read them. ReachedAt and each section's own read probe newest, middle-3 and oldest in turn, so at the loop the memo holds (oldest, middle-3) and StartOf(middle-3) is a hit. The source and the probe were reverted afterwards; the worktree is clean.
FIX (refuter): Lengthen the test's chain so that at least two other positions are probed after middle-3 and before the loop. Either write between two and three tables between the middle and the oldest, as in the probe, or give ChainNamingTheSpacesBeforeTheMiddle an option for extra older tables. Keep the assertion that both files read the same bytes. At between=2 it fails under the StartOf mutant (3708 against 3676) and passes unmutated. Update the test's comment to say that the chain has since moved past the place, so a look would read it again. Do not take the logged-read-offsets alternative on its own: in the current short shape a look answered by the memo makes no read, so the log would show nothing at middle-3 there either.

######## [12] validator-tests:chain-loop-rule-id-doc-omits-reached — reviewer nit; refuter narrowed nit maintainer=False
LOCATION: src/AdCodicem.Pdf/Validation/PdfValidationRuleIds.cs:113-114 (XRefChainLoop, public XML doc)
CLAIM: The public summary of xref.chain-loop still says a /Prev 'names a section the chain has already read, or an offset it has already named'. Since #208 and R2a's XRefLoopTarget, the rule also reports a loop through a section the chain reached and could not read, at an offset it never named. The rule's own message says 'a section the chain has already reached and could not read'. The summary was not touched in the batch.
EVIDENCE: CrossReferenceChainTests.A_prev_looping_back_to_a_lost_stream_found_moved_says_the_chain_reached_a_section_it_could_not_read (passes at 19122f0): the chain named only stream-7, and the finding is 'The /Prev of the cross-reference section at offset {stream} names offset {stream}, a section the chain has already reached and could not read: the chain loops.' The offset is neither a section read nor an offset named. 'git diff 21a0298..19122f0 -- src/AdCodicem.Pdf/Validation/PdfValidationRuleIds.cs' does not touch XRefChainLoop.
FIX (reviewer): Reword the summary: 'names a section the chain has already read, or one it reached there and could not read, or an offset it has already named where it reached no section'. This matches validation-rules.md:59 and ChainLoopRule.
--- VERDICT REASON: The finding holds on substance. The public summary of PdfValidationRuleIds.XRefChainLoop (src/AdCodicem.Pdf/Validation/PdfValidationRuleIds.cs:112-116) still lists only two cases: a section the chain has already read, or an offset it has already named. Round 2's fix cb22c69 (round 2 finding [20]) added a third loop target, XRefLoopTarget.Reached: a section the chain reached and could not read. The rule reports it, and in the found-moved and white-space shapes the /Prev names an offset the chain never named. Neither listed case covers those shapes, so the summary understates when the rule fires. It does not misstate any one finding.

Narrowings:
(1) The claim 'the summary was not touched in the batch' is wrong. `git diff 21a0298..19122f0` does reword it: 7d35896 (#156) changed it to 'A /Prev of the chain ...'. What is true is that cb22c69 left it alone, though that commit added Reached. cb22c69 updated the reader's message, ChainLoopRule, XRefLoopTarget, FileStructure, diagnostics.md:114 and validation-rules.md:59. PdfDiagnosticCodes.XRefChainCycle's public doc (:81-88) already names 'one there that it could not read though it declares itself one'. Round 2's fix list for [20] (r2-findings.md:1161-1167) named the reference pages but not this XML summary, which is how it was missed.
(2) The gap is only a reached-and-unread section at an offset the chain never named: found moved, or reached through the white space before its header, from either side. When the looping /Prev names the exact offset the chain named for the lost stream, 'an offset it has already named' still holds.

On main 21a0298 the summary was complete, because no lost stream's /Prev was followed and no Reached target existed. So this is a gap the round 2 fix opened, in a public doc that also appears in the generated API reference. It is wording only: behavior, the message and both reference pages are right. Severity nit, as the reviewer set it. No maintainer decision is needed.
VERDICT EVIDENCE: Built tests/AdCodicem.Pdf.Tests in Release at 19122f0 (worktree detached on origin/ccr-6d6c0ee6-dqm4lm-188). Ran `--filter-method "*A_prev_looping_back_to_a_lost_stream_found_moved*" --filter-method "*A_lost_stream_named_again_through_the_end_of_line_before_its_header*"`: Passed, 4/4.

The found-moved test (CrossReferenceChainTests.cs:4137-4154) builds LostStreamFiles.ThreeRevisions(prev: "{stream}", namedShortBy: 7). The newest table names stream-7, Sections[1] is Malformed at Offset == stream, and the stream's /Prev names {stream}. The validator's finding is: 'The /Prev of the cross-reference section at offset {stream} names offset {stream}, a section the chain has already reached and could not read: the chain loops.' The chain never named {stream}, which is neither a section read nor an offset named. The theory at :4106-4133, rows (1,0) and (0,1), gives the same through the white space, with LoopTarget == XRefLoopTarget.Reached.

At HEAD, PdfValidationRuleIds.cs:112-116 reads: 'A <c>/Prev</c> of the chain of cross-reference sections names a section the chain has already read, or an offset it has already named, and the sections before the loop are out of reach.'

ChainLoopRule.cs:40-47 has three arms (Read / Reached / named). XRefLoopTarget.cs, docs/website/docs/reference/validation-rules.md:59 ('or a section the chain reached there and could not read') and diagnostics.md:114 all name the third case.

`git log 21a0298..HEAD -- PdfValidationRuleIds.cs` gives 01426c1, 7d35896 and 54d56c6, and 7d35896 is the one that reworded XRefChainLoop. `git show cb22c69 --stat` (the commit that adds XRefLoopTarget.Reached) does not include PdfValidationRuleIds.cs. On main 21a0298, ChainLoopRule had only LoopOffsetRead (read / named).
FIX (refuter): Reword the XML summary at src/AdCodicem.Pdf/Validation/PdfValidationRuleIds.cs:112-115 to name the third case, as PdfDiagnosticCodes.XRefChainCycle and validation-rules.md:59 already do. For example: 'A <c>/Prev</c> of the chain of cross-reference sections names a section the chain has already read, or one it has already reached and could not read though it declares itself one, or an offset it has already named, and the sections before the loop are out of reach. <see cref="PdfValidationSeverity.Error"/>.'

There is no behavior change and no new test. The existing Reached tests already pin the message. The site's generated API page picks the summary up on the next build.

######## [13] docs:stream-taken-on-any-prev — reviewer minor; refuter confirmed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:4661 (check), :4593-4594, :4611-4612, :4656-4657 (texts); docs/threat-model.md:417; docs/website/docs/concepts/lazy-reading.md:122-123; docs/website/docs/reference/diagnostics.md:115; body of commit 394028c
CLAIM: Five texts give the rule for when a /Prev's search takes a cross-reference stream that the /XRefStm read in the stream's place: "only where its (own) /Prev names an older section" (with no other section near). The code checks something weaker. It takes the stream wherever the stream's dictionary gives any /Prev other than null: `stream.Trailer?.GetRaw(PdfName.Prev) is not null` at :4661. That includes a /Prev naming the stream itself, an offset outside the file, or a string. The stream is then read whole, the /XRefStm is reported as xref.hybrid-stream-lost, and the chain loops or ends missing. Under the documented rule, the search would pass the stream over and report the /Prev missing 7 bytes from it.
EVIDENCE: Probe ZzR3DocsProbe.ZzStreamPrevTaken (saved as scratchpad/r3docs-ZzR3DocsProbe.cs.txt). It uses the shape of HybridFiles.PrevFallingShortOfTheStreamBefore: /XRefStm names stream 6 at 1614, /Prev names 1607, and the oldest table is 1,200 bytes off. Only stream 6's /Prev changes between rows.

On 19122f0:
- /Prev {oldest} (control): startxref Read Table 1770, /XRefStm NamedAgain 1614, /Prev Relocated Stream 1614, /Prev Read Table 247; WasRepaired=False.
- /Prev 1614 (the stream itself): /XRefStm NamedAgain 1614, /Prev Relocated Stream 1614. Diagnostics:
  - xref.hybrid-stream-lost: '...names offset 1614, the section its /Prev names too, which the chain read through that /Prev...'
  - xref.chain-cycle: 'the /Prev of the section at offset 1614 names offset 1614, which the chain has already read.'
  - xref.rebuilt
- /Prev 99999999: the stream is taken, then xref.section-missing ('...offset 99999999 lies outside the file, which is 1,908 bytes long...') and xref.rebuilt.
- /Prev (x): the stream is taken, then 'The /Prev of the cross-reference section at offset 1614 is not an offset' and xref.rebuilt.
- No /Prev, and /Prev null: the stream is not taken. /XRefStm Read Stream 1614, /Prev NotFound 1607 ('is not there, nor near it'), xref.rebuilt.

On main 21a0298 the /Prev's search takes the stream in all six rows, since main has no pass-over of reached sections. So this is not a regression; the texts misstate the condition the code applies.
FIX (reviewer): Two options.
1. Make the code check what the texts say: take the stream only where its /Prev reads as an integer offset naming a position in the file that the chain has not reached (IsInFile, ReachedAt < 0). Add a theory over: /Prev naming the stream itself, an offset outside the file, a string, and null.
2. Or reword all five texts (and the code comment at :4656) to "whose own dictionary gives a /Prev".

Separately, diagnostics.md:115 has a wording problem. "So is what the /XRefStm read in the stream's place..." follows "what only the table's stream indexed is found by rebuilding the index", so it reads as if that section were found by rebuilding. Open the sentence with its own subject, e.g. "What the /XRefStm read in the stream's place ... is no stream of the revision either: ...".
--- VERDICT REASON: The finding holds. The texts say a /Prev's search takes the cross-reference stream the /XRefStm read in the stream's place "only where its (own) /Prev names an older section". Those texts are threat-model.md:417, lazy-reading.md:122-123, diagnostics.md:115, the remarks and param docs at PdfFileReader.cs:4593 and :4611, the comment at :4656, and the body of 394028c. The code is weaker. At PdfFileReader.cs:4661 it checks `stream.Trailer?.GetRaw(PdfName.Prev) is not null`, so any /Prev that is not null counts: one naming the stream itself, one naming the newest table (newer and already reached), an offset outside the file, a negative integer, a string, a reference, or the offset of a plain object.

The R2a stage chose this on purpose and recorded it in r2-fix-result.json, deviations/2: "only for a stream that has a /Prev". The docs then paraphrase it as a stricter rule. The same file already uses the phrase in two senses. PreviousToHandOn (:3864) says "names an older section all the same" to mean "gives a /Prev". NamesOlderSection (:2882) means an offset in the file that the chain has not reached. A reader of the docs has only the strict sense.

The code's rule is the better behavior. In every row with a broken /Prev, the branch does what main 21a0298 does: it takes the stream and reports the stream's own /Prev fault, plus the hybrid-stream-lost naming. Under the documented rule, those rows would instead say "not there, nor near it" about a stream 7 bytes away. So the text should change, not the code. That is not a design choice for the maintainer: it rewords to the rule the stage already implemented and recorded.

Severity is minor: a misleading text about which reports a doubly broken hybrid file gets. No object is served wrong and the stream is read once. Not a regression.

The diagnostics.md:115 wording point also holds, as a nit. 394028c added the sentence "So is what the /XRefStm read in the stream's place …". It follows "what only the table's stream indexed is found by rebuilding the index when it is asked for", so it reads as though that section were found by rebuilding. In fact the /Prev reads it whole.
VERDICT EVIDENCE: I wrote my own probe on 19122f0 (source at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r3ref-sp/ZzRefSpProbe.cs.txt; output in out.txt and out-main.txt in the same folder). It uses the shape of HybridFiles.PrevFallingShortOfTheStreamBefore: oldest table at 140, 1,200 bytes off; object 4 at 1485; stream 6 at 1507. The newest table's /Prev is 1500 and its /XRefStm is 1507. Only stream 6's own /Prev changes between rows.

Branch 19122f0:
- /Prev 140 (oldest, the control): sections /XRefStm NamedAgain 1507, /Prev Relocated Stream 1507, /Prev Read Table 140. Diagnostics: offset-adjusted, hybrid-stream-lost '…names offset 1507, the section its /Prev names too, which the chain read through that /Prev…'. WasRepaired=False.
- /Prev 1507 (the stream itself): the stream is taken, then hybrid-stream-lost, chain-cycle '…the /Prev of the section at offset 1507 names offset 1507, which the chain has already read', and xref.rebuilt.
- /Prev naming the newest table (1670): the stream is taken, then hybrid-stream-lost and chain-cycle @1670, then rebuilt.
- /Prev 99999999: the stream is taken, then hybrid-stream-lost, section-missing '…99999999 lies outside the file, which is 1,801 bytes long…', and rebuilt.
- /Prev (x), /Prev -5, /Prev 4 0 R: the stream is taken, then hybrid-stream-lost, '…is not an offset', and rebuilt. The validator gives 'is a value of type string', 'the integer -5', 'the reference 4 0 R'.
- /Prev 1485 (object 4, no section): the stream is taken, then section-missing @1485 'not there, nor near it', and rebuilt.
- No /Prev, and /Prev null: the stream is not taken. Sections: /XRefStm Read Stream 1507, /Prev NotFound 1500. section-missing '…offset 1500 is not there, nor near it', rebuilt, and section-not-found [Error] '…nor within 512 bytes of it'.

Main 21a0298 (same probe): the stream is read again through /Prev in all ten rows ('/Prev Relocated Stream at 1507'), with the same fault of the stream's own /Prev (chain-cycle, outside the file, not an offset) and no hybrid-stream-lost. So the branch's rule matches main on every row with a broken /Prev, and differs only for none and null, which the documented trade-off covers ("which may be the table's own").

r2-fix-result.json, reports/0/deviations/2: "what the /XRefStm read in the stream's place is taken by the /Prev's search only for a stream that has a /Prev, and only after every other candidate has been tried."
FIX (refuter): Reword, and leave the code as it is. In all five places, say what the check is, for example "a cross-reference stream, which may be the table's own, only where its own dictionary gives a /Prev, whatever that /Prev names, and no other place near holds one":
- threat-model.md:417
- lazy-reading.md:122-123
- diagnostics.md:115
- PdfFileReader.cs:4593 and :4611
- the comment at :4656, for example "only where its dictionary gives a /Prev, as a section the /Prev means does, and a hybrid table's own stream does not".

It is worth saying in one clause that a stream whose /Prev is then faulty is still read whole, and its /Prev reported as the chain finds it (a loop, outside the file, not an offset).

Optionally, add theory rows that hold the rule: /Prev naming the stream itself, and /Prev (x), both taken; /Prev null, not taken. No test covers a taken stream with a faulty /Prev today.

Do not take option 1, checking for an in-file offset the chain has not reached. On those rows it would report "not there, nor near it" about a stream 7 bytes away, drop the hybrid-stream-lost naming, and differ from main.

394028c's body cannot be rewritten. Add it to status.md's "Commit bodies not rewritten" list in the batch's record.

diagnostics.md:115 (nit): give the sentence its own subject, for example "What the /XRefStm read in the stream's place where the /Prev's own search … comes to it — … — names no stream of the revision either: that /Prev reads it again, whole; …". Then it no longer reads as found by rebuilding.

######## [14] docs:prev-run-crossed-twice-past-two-candidates — reviewer minor; refuter confirmed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:329-338 (_paddingProbes summary), :4842 (TryReadXRefSection probes each place tried), :4871-4904 (PaddingAt); docs/threat-model.md:414 and :455-457 ([#47]); tests/AdCodicem.Pdf.Tests/HostileInputTests.cs:4526; bodies of d63f093 and 7c0748b
CLAIM: The fix for round 2's [3] (cross each position's white space once) is incomplete, and five texts overstate it.

The mechanism:
- The memo keeps the two probes last read.
- A hybrid table's /Prev is probed (StartOfNamed) before its /XRefStm is read.
- Each place the /XRefStm's search tries is read through TryReadXRefSection, which probes that place and stores the result.
- When the search tries two places or more, the /Prev's probe is pushed out, and the chain crosses the /Prev's run of white space again when it reaches it.

So these claims hold only for a search that tries a single place:
- "Each position's white space is crossed once, however often it is asked of" (XML doc).
- "once for each position, however often the chain asks of it — ... and a hybrid table's /Prev between the two —" (threat-model:414).
- "once per section, however often the chain asks where that section starts" ([#47]).
- "the /Prev's outlasts its /XRefStm's search" (7c0748b; HostileInputTests:4526).

Main crossed such a run once. The extra cost is bounded: MaxRelocationCandidates (32 a document) allows at most 16 repeats, about 84 KB each at the default MaxTrailerLength.

Also, [#47]'s "the last two answers kept" reads like the order 7c0748b removed (most recently answered). The memo keeps the last two probes read.
EVIDENCE: Probe ZzR3DocsProbe.ZzRunCrossedOnce uses the hybrid shape of HostileInputTests.A_chain_whose_offsets_name_long_runs_of_white_space_crosses_each_run_once, with 10 tables. Each /Prev names either the start of the 60,000 spaces before the older table (padded) or its keyword (exact). Each /XRefStm names 3 bytes into the first of N plain objects written before its stream. All ten streams are found, with 11 diagnostics on every side. Bytes are counted with StrictCountingSource; a crossing is 84,256 bytes.

| Decoys | 19122f0 padded / exact | diff | crossings a table | main 21a0298 padded / exact | crossings a table |
|---|---|---|---|---|---|
| 0 | 4,693,176 / 3,791,137 | 902,039 | 1.07 | 4,693,572 / 3,791,533 | 1.07 |
| 1 | 6,184,209 / 4,524,173 | 1,660,036 | 1.97 | 5,426,295 / 4,524,275 | 1.07 |
| 2 | 6,916,957 / 5,256,940 | 1,660,017 | 1.97 | 6,159,043 / 5,257,042 | 1.07 |

With one decoy, the branch reads 757,914 more bytes than main for the padded file (+14%): exactly one more crossing per table.
FIX (reviewer): Keep the /Prev's probe from being pushed out by the places the search tries. Either:
- have the reads TryRelocateXRefSection makes (candidate: true) take their padding without storing it (a place starts at its token, so its probe is 32 bytes); or
- keep the hybrid table's /Prev probe in a slot of its own until the chain reaches it.

Then add the one-decoy shape as a row of the HostileInputTests theory, bounding padded minus exact by Tables × (Crossing + 4096); that row fails on 19122f0.

Otherwise, narrow the texts to "once, or twice for a hybrid table's /Prev whose /XRefStm's search tries two places or more, which MaxRelocationCandidates bounds".

Either way, change [#47]'s "the last two answers kept" to "the last two probes read kept".
--- VERDICT REASON: The finding holds as stated. I rebuilt the reproducer on my own and traced the code path at 19122f0.

**How a hybrid table runs through the memo:**
1. ReachedAt(hybrid) probes the /XRefStm's place: memo (H, P_i).
2. StartOfNamed(previous) at :3392 crosses the /Prev's run: memo (P_prev, H).
3. TryReadXRefSection(H) is a memo hit, so the order does not change. It fails.
4. TryRelocateXRefSection then calls TryReadXRefSection(candidate, candidate: true) for each place it tries (:4686). Each call probes through PaddingAt (:4842) and stores its result (:4904).
   - With one place tried, the memo is (C1, P_prev), and the /Prev survives.
   - With two places tried, it is (C2, C1). The /Prev's probe is gone, and the next iteration's ReachedAt(P_prev) crosses the 84 KB run again.

Nothing else probes between steps 2 and 4. The extra cost needs every condition below at once:
- a hybrid table;
- an /XRefStm not read where it is named;
- a search that tries at least two places before it ends;
- a /Prev that names a run of white space or comments.

**Bound.** Each extra crossing uses at least 2 of MaxRelocationCandidates (32 a document), so there are at most 16 a document. Each costs 84,256 bytes at the default MaxTrailerLength, and scales with it when raised. Diagnostics and section states are identical on every build.

**Against the baselines.** It is a regression against main 21a0298, which crosses once for every number of decoys. It is a gain against 761ce0b, which crosses about 3 times.

**The texts.** These are true only for a search that tries a single place:
- the _paddingProbes summary, "crossed once, however often it is asked of" (:329-333), and its remarks, "what is read is what a reading that asked once read" (:334-337);
- threat-model.md:414, "once for each position, however often the chain asks of it";
- [#47] at :454-457, "once per section, however often…";
- the test comment at HostileInputTests.cs:4526, "the /Prev's outlasting its /XRefStm's search". This is literally true of the test's own one-candidate shape, but it is phrased as a general claim.

The bodies of d63f093 and 7c0748b overstate it the same way. They are history and cannot be corrected short of a rewrite.

[#47]'s "the last two answers kept" is imprecise (nit). Since 7c0748b, a hit no longer reorders the memo, so the memo keeps the last two probes read, not the last two positions answered. The inline comment in PaddingAt (:4874-4876) says "a place its search near the offset tries" (singular), which is accurate.

Round 2's decision for [3] in decisions.md was "cross each position's white space once (memo PaddingAt)". The code fix below implements that decision completely, so no maintainer choice is needed.

The scratchpad already held lot19/r3-padmemo with the same measurement and the same trial fix, probably from a related chain-lens finding. The finding may duplicate it.
VERDICT EVIDENCE: **The probe.** My own ZzRefPadDecoyTests, since deleted, built on the shape of HostileInputTests.A_chain_whose_offsets_name_long_runs_of_white_space_crosses_each_run_once:
- Each table is laid out as N decoys `{10+d} 0 obj\nnull\nendobj\n`, then the 7-byte xref stream, then 60,000 spaces, then `xref`.
- /XRefStm names first decoy + 3, or stream − 3 when N = 0.
- /Prev names either the start of the run (padded) or the `xref` keyword (exact).
- Bytes are counted with StrictCountingSource. A crossing is 84,256 bytes.

**Branch 19122f0, 10 tables** (padded / exact, then diff and crossings a table):
- N=0: 4,693,176 / 3,791,137; diff 902,039; 1.07
- N=1: 6,102,335 / 4,442,301; diff 1,660,034; 1.97
- N=2: 6,753,211 / 5,093,198; diff 1,660,013; 1.97
- N=3: diff 1,575,768; 1.87. Here 8 streams are relocated and 2 not found, because the 32-place budget is spent.

**Branch 19122f0, 100 tables** (diff):
- N=0: 8,482,198
- N=1: 9,829,761. That is 16 extra crossings: 16 searches of 2 places use the 32.
- N=2: 9,408,620, or 11 extra.
- N=3: 9,155,927, or 8 extra.

**Main 21a0298**, same probe:
- 10 tables: diff 902,039 / 902,018 / 901,997 / 901,976 for N=0..3, 1.07 crossings a table.
- 100 tables: diff 8,482,198 / 8,482,177 / 8,482,156 / 8,482,135, 1.01 crossings a table.
- With 10 tables and N=1, the padded file reads 5,344,421 bytes, so the branch reads 757,914 more, exactly the finding's figure.

**761ce0b**: 2.96 to 3.00 crossings a table on every row, for example a diff of 25,237,344 with 100 tables and N=1.

**Diagnostics** are identical on all three builds for every row. For example, with 10 tables and N=1, both padded and exact give object.redefined x1, xref.offset-adjusted x10 and xref.rebuilt x1, with /XRefStm:Relocated x10.

**Trial fix.** In TryReadXRefSection, call PaddingAt(absolute, out var table, remember: !candidate), and store the probe only when remember is set.
- Every row comes back to main's figures: 10 tables, diff 902,039 / 902,018 / 901,997 / 901,976; 100 tables, diff 8,482,198 / 8,482,177 / 8,482,156 / 8,482,135.
- Diagnostics and section states are byte-identical to 19122f0's.
- Whole unit suite with the fix: 7,492 total, 0 failed, 7,472 succeeded, 20 skipped.
- The theory's bound, BeInRange(100×84,256, 100×(84,256+4,096)) = [8,425,600, 8,835,200]:
  - with a one-decoy row it fails on 19122f0 (9,829,761);
  - it passes with the fix (8,482,177).
- Reverted afterwards. The scratch worktrees are removed and nothing is committed.
FIX (refuter): Make the code true to the texts. This is the decision already taken for round 2's [3], so it needs no maintainer.

1. **The code.** In TryReadXRefSection (PdfFileReader.cs:4842), pass `remember: !candidate` to PaddingAt, and store the probe at :4904 only when it is set.
   - A place the search tries starts at its token (an `xref` keyword or an object header), so its probe is a single 32-byte window.
   - Nothing asks of that place again through the memo: a section found there is reached through _sectionsReadAt.
   - Alternatively, keep the hybrid /Prev's probe in a slot of its own. The first option is one line and measured equal to main.
   - Extend PaddingAt's inline comment (:4874-4876) to say that the places a search tries are not kept.
2. **The test.** Add a decoy count to the HostileInputTests theory, for example an InlineData(true, 3, 1) row that writes one plain object before each stream and names 3 bytes into it. Keep the bound Tables × (Crossing + 4096). That row fails on 19122f0 (9,829,761 against 8,835,200) and passes with the fix.
3. **The docs.** In [#47] (threat-model.md:457), change "the last two answers kept" to "the last two probes read kept". The other texts (the _paddingProbes doc, threat-model:414, [#47], HostileInputTests:4526) then hold as written. The commit bodies of d63f093 and 7c0748b stay as history; the fix commit's body says what is now true.

If the code fix is not made, narrow those four texts instead, to "once, or twice for a hybrid table's /Prev whose /XRefStm's search near the offset tries two places or more, at most 16 times a document (MaxRelocationCandidates)". In that case, file the regression against main as `debt` beside #47.

######## [15] docs:own-refused-row-said-kept — reviewer minor; refuter confirmed minor maintainer=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:4401, :4404, :4416 (ReportSectionReadInPart); docs/website/docs/reference/diagnostics.md:112; src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:62-64
CLAIM: This confirms R2b's two known wording gaps; gap (a) is wider than stated.
- (a) is not limited to a stream holding one row. Any stream read in part that refused a row of its own says "the row it holds was kept" or "the rows it holds were kept", though the refused row was not indexed.
- (b) A table read in part that refused a row of its own says "the rows read were kept".

The reference repeats both: diagnostics.md:112 says "The rows read are kept, but for those whose numbers a newer section refused" and "the row it holds was kept". So does the XRefSectionUnreadable XML doc ("The rows read were kept, but for those whose numbers a newer section refused"). Only invalid files reach either gap, and only the 'kept' clause is wrong: the object is still served from a rebuild.
EVIDENCE: Probe ZzR3DocsProbe.ZzOwnRefusalGaps on 19122f0. In each case startxref names the section, and an older table indexes objects 0 to 3 or 0 to 4.

(a) Stream /W [1 4 3] /Index [3 2], holding one row: object 3, type 1, generation 65536.
- Reported: 'The cross-reference stream startxref names at offset 307 was read only in part: it holds 1 row where its /Index and /Size declare 2; the row it holds was kept, and the objects whose rows it lacks are looked for by rebuilding the index ...'
- ChainIndex has 3: False. object 3 = "new three" after xref.rebuilt.

(a2) Same stream with /Index [3 3], holding two rows; object 3's is refused the same way.
- Reported: '... it holds 2 rows where its /Index and /Size declare 3; the rows it holds were kept, ...'
- ChainIndex has 3: False.

(b) Table 'xref\n3 3' with object 3's row at generation 65536, object 4's row sound, then 'junk junk junk'.
- Reported: '... was read only in part: the row for object 5, at offset 428, is not an offset, a generation and n or f; the rows read were kept, and the objects the rest of that subsection numbers are looked for ...'
- ChainIndex has 3: False.
FIX (reviewer): Fix in this batch; it is cheap. The section already records its first own refusal in RefusedRow. Where RefusedRow is set beside another fault, say "kept but for the row it refused" ("... but for those it refused" for a stream). For a single-row stream, say the row it holds was refused and does not stand.

Add a theory over the three probes above. Bring diagnostics.md:112 (both clauses) and the XRefSectionUnreadable XML doc in line.

If the batch is to stay closed to it, file it as debt under M02 with these three reproductions, and add "but for a row it refused itself" to the two docs now.
--- VERDICT REASON: The finding holds as stated, and R2b's gap (a) really is wider than R2b wrote it. In ReadXRefStreamRows (PdfFileReader.cs:6941-6944) and the table reader (:5509-5511), a type 1 or 2 row that no entry can hold is refused with Refuse(number, section), and only the first such row is recorded in RefusedRow. ReportSectionReadInPart checks RefusedRow only in its third branch (:4423), after `section.Incomplete`. So whenever a section is also read in part, it says kept whatever its own refusals were:
- a stream gets ":4401 the row it holds was kept" for one row, or ":4404 the rows it holds were kept" for more;
- a table gets ":4416 the rows read were kept".

R2b's note named only the one-row stream. The two-row stream says "the rows it holds were kept" the same way, and so does the someRefused arm at :4403, which leaves out the section's own refusal.

What is served is correct in every case: the refused number is in no index, and object 3 comes back as its newer revision through the rebuild. Only the "kept" clause is wrong, and only an invalid file reaches it, so this is a misleading text (minor), not a wrong object.

The two docs say the same. diagnostics.md:112 has "The rows read are kept, but for those whose numbers a newer section refused" and "The message agrees with the count of rows a stream holds: the row it holds was kept". The XRefSectionUnreadable XML doc (PdfDiagnosticCodes.cs:62-63) has "The rows read were kept, but for those whose numbers a newer section refused". Neither says that a row the section refused itself is not kept.

No decision is broken. B19-3's one-fault rule ("the first row it refused, which is not named beside either of the others") forbids naming the row, not saying it was not kept. The read-in-part message tails were settled without a question in round 1 ([12][13][23][30]). No test covers the combination: CrossReferenceChainTests only takes the RefusedRow branch with no other fault, or a refusal made by a newer section.
VERDICT EVIDENCE: I rebuilt the reproducer on 19122f0 (the build had 0 warnings) as probe ZzOwnRefusalGaps. Its copy is /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/ZzRefuteOwnRefusal.cs.txt and its output is /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/zz-refute-own-head.txt. In every case the oldest table indexes objects 0 to 4, and startxref names the newest section.

(a) Stream 6, /W [1 4 3] /Index [3 2], holding one row: object 3, type 1, generation 65536.
- Reported: "The cross-reference stream startxref names at offset 347 was read only in part: it holds 1 row where its /Index and /Size declare 2; the row it holds was kept, and the objects whose rows it lacks are looked for by rebuilding the index ..."
- ChainIndex has 3: False. Object 3 = "new three", after xref.rebuilt.

(a2) The same stream with /Index [3 3], holding two rows: object 3's is refused the same way, and object 4's is sound.
- Reported: "... it holds 2 rows where its /Index and /Size declare 3; the rows it holds were kept, and the objects whose rows it lacks ..."
- ChainIndex has 3: False, has 4: True. Object 3 = "new three".

(b) Table "xref\n3 3", with object 3's row at 65536, object 4's row sound, then "junk junk junk".
- Reported: "... was read only in part: the row for object 5, at offset 396, is not an offset, a generation and n or f; the rows read were kept, and the objects the rest of that subsection numbers are looked for ..."
- ChainIndex has 3: False. Object 3 = "new three".

(c) Control: the same table with "3 2" and no other fault.
- Reported, correctly: "... gives it generation 65536, outside 0 to 65535; that row alone was refused, the rows after it read, and its object is looked for by rebuilding the index ..."

On the branch head, the cited lines hold the strings in question: PdfFileReader.cs:4401, :4404 and :4416, diagnostics.md:112 and PdfDiagnosticCodes.cs:62. The finding claims no difference from 761ce0b or main, so neither was rebuilt.
FIX (refuter): Fix it in this batch. It is a message tail of the kind round 1 settled without a question, and the one-fault rule stands: the refused row is not named, only said not kept.

In ReportSectionReadInPart, where `section.RefusedRow is not null` and the section is Incomplete:
- **Stream, one row** (RowsLeftToGuard false, RowsHeld 1, the row refused by the stream itself): say "; the row it holds was refused" + Lacking, not "was kept".
- **Stream, several rows:** say "the rows it holds were kept but for any it refused". Combined with someRefused, say "kept but for any it refused and those whose numbers a newer section refused". Use "any" or "those", because RefusedRow records only the first of possibly several.
- **Table:** say "the rows read were kept but for any it refused" at :4416, and add the same clause to the someRefused text there. The noneStands texts already say none stands, so they need nothing.

Tests: add a theory in CrossReferenceChainTests over the three shapes (a), (a2) and (b), plus (b) with the end of the file cutting the table in place of the junk row. Each asserts the exact message, that ChainIndex does not hold 3, and that object 3 reads "new three". Run the ADR 53 mutants over the changed lines.

Docs:
- diagnostics.md:112 should read "The rows read are kept, but for any it refused itself and those whose numbers a newer section refused", and "the row it holds was kept, unless it refused it, ...".
- Change the XRefSectionUnreadable XML doc (PdfDiagnosticCodes.cs:62-63) the same way.
- Change the third remarks paragraph of ReportSectionReadInPart the same way.

If the batch is closed to code changes, file it as debt under M02 with these four reproductions. Then fix only the two docs now, to say that a row the section refused itself is not kept, though the message does not yet say so. Either choice is the orchestrator's; neither needs the maintainer.

######## [16] docs:startxref-lost-trailer-test-name — reviewer nit; refuter confirmed nit maintainer=False
LOCATION: tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:2169, cited at docs/threat-model.md:421
CLAIM: The test The_table_startxref_names_whose_row_that_is_no_row_the_end_of_the_file_follows_says_its_lost_trailer_names_nothing_more asserts the opposite of what its name says. Its message ends: "what only the rest of the table, or the sections its trailer would name, index is looked for by rebuilding the index". So the report says what the lost trailer would name is looked for; it does not say the trailer names nothing more. The test's own comment agrees with the assertion, not with the name.
EVIDENCE: Read at CrossReferenceChainTests.cs:2169-2187. The asserted message is "...; the rows read were kept, and what only the rest of the table, or the sections its trailer would name, index is looked for by rebuilding the index." The comment says "the report says what it would have named is looked for by rebuilding the index, rather than the rest of that subsection alone". The threat model cites the test under that name.
FIX (reviewer): Rename the test, e.g. ..._says_what_its_lost_trailer_would_name_is_found_by_a_rebuild_at_a_read, matching its /Prev sibling at :2144. Update the citation in threat-model.md:421.
--- VERDICT REASON: The finding holds. Round 2's fix for finding [8] (commit 95d8e0f) added this test. A table startxref names has a row that is no row, then the file ends. The test asserts that the report gives the trailer tail ("what only the rest of the table, or the sections its trailer would name, index is looked for by rebuilding the index"). The point of that fix is that the report now says what the lost trailer would name is left to a rebuild. The test's name says the report "says its lost trailer names nothing more". Read plainly, that says the opposite: that the lost trailer adds nothing. The test's own comment matches the assertion, not the name ("the report says what it would have named is looked for by rebuilding the index, rather than the rest of that subsection alone"). Its /Prev sibling, added in the same commit, is named after the behavior ("..._says_what_its_lost_trailer_would_name_is_found_by_a_rebuild"). One strained reading would make the name mean "mentions the lost trailer and names no second fault", but that needs a missing "and", and the comment does not support it. The assertions are correct and the test is real. Only the name, and the threat model's citation of it, misstate what it holds. Round 2 settled its test-name findings ([16], [17]) at nit, so this stays nit.
VERDICT EVIDENCE: On origin/ccr-6d6c0ee6-dqm4lm-188 at 19122f0:
- tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:2169 declares `The_table_startxref_names_whose_row_that_is_no_row_the_end_of_the_file_follows_says_its_lost_trailer_names_nothing_more()`.
- Its comment (:2171-2173) says "its trailer is lost, and the report says what it would have named is looked for by rebuilding the index, rather than the rest of that subsection alone (#207, #210). Object 4, which only an older table nothing names indexes, is found so."
- :2183 asserts the message "...the row for object 4, at offset {row}, is not an offset, a generation and n or f; the rows read were kept, and what only the rest of the table, or the sections its trailer would name, index is looked for by rebuilding the index."
- :2184-2186 assert WasRepaired false, then Text(document, 4) == "object 4", then WasRepaired true.
- The sibling at :2144 is `A_table_prev_names_whose_row_that_is_no_row_the_end_of_the_file_follows_says_what_its_lost_trailer_would_name_is_found_by_a_rebuild` and asserts the same tail.
- `git log -S` shows both names came from 95d8e0f, the fix for r2 finding [8] (bad-row-then-eof-tail-omits-lost-trailer). Before that fix, the startxref case gave the subsection-only tail.
- r2-fix-result.json lists the test under this name ("fails before the fix").
- docs/threat-model.md:421 cites the test by this name, beside its behavior text "that a table whose row that is no row the end of the file follows lost its trailer too".
- Nothing else in src/, tests/ or docs/ refers to the name.
I did not build this. The claim is only about the test's name against its asserted text, and the code above shows that directly.
FIX (refuter): Rename the test after the behavior it holds, matching its /Prev sibling: `The_table_startxref_names_whose_row_that_is_no_row_the_end_of_the_file_follows_says_what_its_lost_trailer_would_name_is_found_by_a_rebuild`. Update its citation in docs/threat-model.md:421 in the same commit (test: commit, footer Refs #207, #210). No code or assertion changes, and no maintainer decision is needed.

######## [17] docs:round-commit-bodies — reviewer nit; refuter narrowed nit maintainer=False
LOCATION: bodies of commits 5656c64 and 19122f0
CLAIM: Two commit bodies say something their diffs do not.
- 5656c64 says "an /XRefStm naming what its /Prev names outside the file where the count guard ends the chain is the guard's". The test it adds (An_xrefstm_naming_what_its_table_s_prev_names_outside_the_file_where_the_section_count_guard_ends_the_chain_says_so) holds the opposite: the finding names the offset as outside the file, as when the file is read whole, not as the guard's. The code agrees with the test: NamesWhatPreviousReads is false outside the file, so the count-guard branch at :3226-3232 leaves the naming alone.
- 19122f0 says "both pages now say so" of the rule "one that meets only an earlier range kept apart", but reader-limits.md:147-148 does not say it.
EVIDENCE: CrossReferenceRuleTests.cs:871-880 asserts the finding text '... names offset 99999999, outside the file, which its /Prev names too, where ISO 32000-1 names ...'. In reader-limits.md the text reads "those of the sections read so far that meet joined, and those a table loses each joined as it is lost to the last one lost where the two meet", with no 'kept apart'.
FIX (reviewer): If the branch is squash-merged, as main's history shows, the bodies are lost; say it right in the pull request text. Otherwise reword: 5656c64 "... outside the file, where the count guard ends the chain, still says outside the file"; 19122f0 "the lazy reading page says so, and the limits page names the join".
--- VERDICT REASON: The 5656c64 half holds. Its body says the /XRefStm naming of what its /Prev names outside the file, where the count guard ends the chain, "is the guard's". The test the commit adds asserts the opposite, and so does the code, now and at d5d99f3. LeaveToPrevious sets NamesWhatPreviousReads = inFile && !pastGuard (PdfFileReader.cs:3747), which is false for an offset outside the file. The count-guard branch at PdfFileReader.cs:3228-3231 only rewrites a record whose NamesWhatPreviousReads is true, so it leaves this one alone. The message stays "outside the file, which its /Prev names too". It never says "past the guard that ended the chain there".

The 19122f0 half does not hold as a misstatement. Both pages carry the rule the body names: a table's own ranges are each joined, as they are lost, to the last one lost where the two meet. reader-limits.md:147-148 says this in so many words. "Kept apart" follows from it, because a range is joined only to the last one lost, so a range that meets only an earlier one is not joined. The page leaves that consequence implicit rather than contradicting the body, which matches the code (LoseRows, PdfFileReader.cs:4524-4545). That is at most a stylistic gap in a commit body, below a finding.

The finding's suggested fix rests on a wrong premise. A squash merge does not lose the bodies: main's squash commits keep each branch commit's subject and body as bullet items. 21a0298 shows this ("* test: watch the object streams ...", followed by the full body). So 5656c64's wrong sentence would land on main unless it is corrected.
VERDICT EVIDENCE: On the branch head 19122f0, in worktree /home/user/AdCodicem.Pdf/.claude/worktrees/wf_46f5fc30-494-23:
- 5656c64's body: "an /XRefStm naming what its /Prev names outside the file where the count guard ends the chain is the guard's".
- The test it adds, CrossReferenceRuleTests.cs:871 An_xrefstm_naming_what_its_table_s_prev_names_outside_the_file_where_the_section_count_guard_ends_the_chain_says_so, uses MaxXRefSectionCount = 1. It asserts the message "... names offset 99999999, outside the file, which its /Prev names too, where ISO 32000-1 names ...". Its comment reads: "what lies outside the file holds no section whatever the guard hid, which the finding says, as read whole".
- I built Release and ran `--filter-method "*An_xrefstm_naming_what_its_table_s_prev_names*"`: 15 passed, 0 failed. The test does assert "outside the file" under the count guard.
- In the code, PdfFileReader.cs:3747 `named.NamesWhatPreviousReads = inFile && !pastGuard;`. The branch at :3228 is `if (namedWithPrevious is { NamesWhatPreviousReads: true } leftToGuard) { ... NamesNoStream = "what its /Prev names too, past the guard that ended the chain there"; }`, so it is skipped outside the file.
- 19122f0, reader-limits.md:147-148: "those a table loses each joined as it is lost to the last one lost where the two meet". That is the same rule lazy-reading.md states, without spelling out the "kept apart" consequence.
- `git show -s 21a0298` (main, "(#318)"): the squash body carries "* test: ..." and "* fix: ..." items with their full bodies, so branch commit bodies reach main.
FIX (refuter): Correct the 5656c64 sentence before it reaches main. It could read: "... and an /XRefStm naming what its /Prev names outside the file, where the count guard ends the chain, still says outside the file, as when the file is read whole". There are two ways to do it. One is to edit the text in the squash-merge body when the pull request is merged; GitHub prefills it from the commit bodies. The other is to rewrite 5656c64's message on the session branch, for example with `git filter-branch --msg-filter` or a scripted rebase, and force-push it. Whether a force-push to the session branch is acceptable is the maintainer's call; editing the squash body needs no rewrite. 19122f0 needs no change. If anyone wants the wording tighter, "the lazy reading page spells it out" would do, but it is optional.
