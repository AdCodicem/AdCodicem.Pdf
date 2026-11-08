# Review round 11 (wf_556f39fe-f5c): findings and refuter verdicts

## 1. ws-flag-withholds-warning-the-guard-cannot-have-hidden — major → narrowed minor (src-texts)
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:5562 (flag set with no condition); texts at PdfFileReader.cs:663, :3711-3714, :3751-3752, PdfDiagnosticCodes.cs:125-128, diagnostics.md:116, lazy-reading.md:220-222, reference/diagnostics.md:142

Claim: Open point (a). _whiteSpacePastGuard holds back every xref.stream-passed-over in the document, including a stream the guard can neither have hidden nor caused to be passed over. In the shape below, the search the guard caused finds the named section at the first token past the white space and reads it whole, so nothing is left unread. That search's window does not hold the stream, which a different search, one no guard touched, passed over. The chain and the objects served are identical to a read at default limits. The texts justify the rule with 'the limit's report stands for what it left unread, or for what that search passed over or did not find' (PdfDiagnosticCodes:127-128), and with 'the guard's report stands for what the search passed over or left unread (ADR 34)' (:663). Both are false here: the guard left nothing unread, and its search neither passed over nor missed anything. The Warning is the file's own: default limits make it. Under ADR 34 the guard owns only what lies past it, and here nothing does. Only this flag holds the report back where the chain did not end. ChainStoppedByGuard always means part of the chain is unread.

Evidence: Probe ZzR11SrcTexts.ZzWhiteSpaceFoundWhole (r11wip/src-texts/ZzR11SrcTexts.cs.txt, output head-ws.txt). It uses the round's own builder: HybridFiles.PassingOverAStreamAnOlderTableNamesPastWhiteSpace("the oldest table", 300, oldestNamesOwn: false), with stream 12 at 362, the run at 943 and the oldest table at 1243.
Default: '/Prev Read Table off=943 pad=300'; open: xref.offset-adjusted@802, xref.stream-passed-over@362; objs 1-11 'Dictionary Dictionary Dictionary "four" "five" - - - - - Stream'.
MaxTrailerLength 256: '/Prev Relocated Table off=1243 named=943' (1243 = 943 + 300, the first token past the run); open: xref.offset-adjusted@802, limit.trailer@943, xref.offset-adjusted@1243; chainStopped=False; the same objects. The Warning at 362 is gone.
Run 400: the same. Run 1000, where the table is out of reach: section-missing and a rebuild, so the guard did hide something there.
A second shape appends a newer table whose /Prev names 300 spaces placed before the hybrid table of PrevFallingShortOfTheStreamBeforeTheTablesOwn("nothing", true) (newer-run300). Default: xref.stream-passed-over@1872. Under 256: '/Prev Relocated Table off=2889 named=2589' (2589 + 300), and the later /Prev search, which no guard touched, passes stream 11 over. The output is [limit.trailer@2589, xref.offset-adjusted@2889, xref.offset-adjusted@1683, xref.hybrid-stream-lost@1683], with no Warning.
The R10c theory has no row with the file's own Warning and a run between MaxTrailerLength and 512.

Fix: Narrow the flag to what the guard may have caused or hidden. Mark the stream records made by a search the white-space guard caused, and leave those unreported (this keeps R10c's b1/b4 rows). Hold back everything only where that search did not take the first token past the run: it found nothing, or took another place. The bytes between the offset and the place taken lie in the window still open, so telling needs no extra read. Rows: the two shapes above under 256 keep their Warning, and R10c's rows stay as pinned. If the maintainer keeps the document-wide rule instead, the texts at :663, :3711-3714, :3751, PdfDiagnosticCodes:125-128 and the three pages must say so. They would then state that the Warning is held back even where the search finds the section whole past the run and passes nothing over. They would drop the 'stands for what that search passed over or did not find' rationale.

Verdict: The behavior reproduces exactly on 79c6a7f. With MaxTrailerLength lowered below the search's reach, the search the white-space guard caused takes the first token past the run (1243 = 943 + 300). That gives the same chain, the same objects and no rebuild as a read at default limits. Yet _whiteSpacePastGuard withholds xref.stream-passed-over@362, which default limits and Unbounded both make, and which a different search, one no guard touched, passed over. The guard can neither have hidden nor caused that Warning. Dropping it goes against round 9's [2]/[11] rule ("not made where a guard ended the chain or may hide a link"). It also goes against ADR 34's "Reaching a guard keeps what fits within it". This answers open point (a): yes, there is such a shape.

Narrowed in three ways.
(1) Default limits cannot produce it. NearbySearchRadius is 512 and MaxTrailerLength defaults to 64 KB, so any run that trips the guard lies past the search's reach. At default limits the flag is set only where the guard hid the section past the run (section missing, rebuilt) or sent the chain elsewhere, and withholding there follows "may hide a link". The loss needs a caller to set MaxTrailerLength under about 508 bytes, below a few hundred bytes of trailer. The limit's own report is still made, and no object changes.
(2) The code does what round 10's triage decided: "nothing is reported where a limit left part of the chain unread or turned the chain's reading into a search", with a flag tested beside ChainStoppedByGuard, which is document-wide by construction. The scope was left open as open point (a). So this is not a decision left unimplemented.
(3) The texts do not misstate what the code does. PdfDiagnosticCodes:123-128, PdfFileReader :630-632, :659-666, :3711-3714, :3727-3731 and :3749-3754, diagnostics.md:116 and lazy-reading.md:214-224 all state an unconditional rule ("Nothing is reported where … white space … run past MaxTrailerLength, which turned the reading … into the search"), which matches the code. What is untrue here is only the reason they give ("the limit's report stands for what it left unread, or for what that search passed over or did not find", and "may hide the link"): in this shape nothing is unread, passed over or missed. That is misleading, not false.
Major is too high. Rounds 10 [9] and [21] were the mirror case: a Warning the guard caused, made under T256. Both were rated minor. Main 21a0298 never makes this code, so nothing here is worse than main. It is the batch's own new Warning behaving against its own principle.

Verdict evidence: Probe: C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r11wip/verify-ws-flag-withholds-warning-the-guard-cannot-have-hidden/ZzR11VerWsFlag.cs.txt. Output: head-out.txt in the same folder. It was run on origin/ccr-6d6c0ee6-dqm4lm-188 at 79c6a7f, Release build, 0 warnings.

Row A, PassingOverAStreamAnOlderTableNamesPastWhiteSpace("the oldest table", run, oldestNamesOwn: false), with stream 12 at 362, the run at 943 and the older table at 802:
- run 300, Default and Unbounded: '/Prev Read Table off=943 pad=300'; [xref.offset-adjusted@802, xref.stream-passed-over@362]; objs '1:Dictionary 2:Dictionary 3:Dictionary 4:"four" 5:"five" 6:- 7:- 8:- 9:- 10:- 11:Stream 12:-'; repaired=False.
- run 300, T256: '/Prev Relocated Table off=1243 named=943'; [xref.offset-adjusted@802, limit.trailer@943, xref.offset-adjusted@1243]; chainStopped=False; the same objects; repaired=False. The Warning is gone.
- T100 gives the same result, plus a limit.trailer@562.
- Runs 400 and 480 at T256: the same (Relocated 1343 and 1423).
- Runs 600 and 1000 at T256: '/Prev NotFound', [offset-adjusted, limit.trailer@943, section-missing@943], then rebuilt. Here the guard did hide the oldest table.

Row B, the same builder with a run of 70,000 at default limits: [xref.offset-adjusted@802, limit.trailer@943, xref.section-missing@943], then xref.rebuilt. The search's 512 bytes cannot reach past the run. Unbounded gives 'Read pad=70000' and the Warning at 362. So at default limits the flag is set only where the guard hid what lies past the run.

Row C, oldestNamesOwn: true (control): no Warning at Default or T256; /XRefStm reads 362.

Row D, PrevNamingWhiteSpaceBeforeTheOldestTable(300) (round 10's b1 class, where the guard's own search passes stream 12 over): T256 gives [limit.trailer@455, xref.offset-adjusted@755] with no Warning. This must stay withheld under any narrowing.

Row E, the reviewer's newer-run shape (a table appended whose /Prev names 300 spaces before PrevFallingShortOfTheStreamBeforeTheTablesOwn("nothing", true)'s hybrid table):
- Default: '/Prev Read Table off=2589 pad=300', with xref.stream-passed-over@1872.
- T256: '/Prev Relocated Table off=2889 named=2589', then [limit.trailer@2589, xref.offset-adjusted@2889, xref.offset-adjusted@1683, xref.hybrid-stream-lost@1683] with no Warning; the same sections after it and the same objects.

The R10c theory A_stream_passed_over_near_a_hybrid_table_s_prev_is_left_to_the_trailer_guard_... (CrossReferenceChainTests.cs:6700-6746) has no row with oldestNamesOwn false and a run under 512 that the guard cuts. Its only file-made-Warning rows are (1000, 512) and (1000, 0).
fix: This is a design choice for the maintainer. Round 10's triage wrote the document-wide rule into eight texts, and the case is reachable only under MaxTrailerLength < 512. There are two options.

(A) Recommended, in line with ADR 34 and round 9's [2]/[11] rule: narrow the flag to what the guard caused or may hide.
- Tag each _streamsPassedOver record that the search following the white-space guard's NotFound adds. Leave tagged records unreported always, which keeps rows D and b1/b4.
- Hold back every record only where that search did not take the first token past the run (it found nothing, or took another place).
- The test needs no read: the window [offset-512, offset+512] is open, and a lexer pass from the named offset to the place taken shows whether only white space and comments lie between.
- Elsewhere this is conservative: where the token past the run is no section, the search equals what a default read would do, and still holds everything back.
- Consequences: rows A (run 300/400/480 at T256) and E keep xref.stream-passed-over@362 and @1872, and R10c's rows stay as pinned. It costs a bit per record, new rows and an ADR 53 pass, and the texts change to "where that guard may have hidden a link, or for what its search passed over".

(B) Keep the document-wide rule and make the texts say it.
- Edit PdfDiagnosticCodes.XRefStreamPassedOver, PdfFileReader :630-632, :659-666, :3711-3714, :3727-3731 and :3749-3754, diagnostics.md:116, lazy-reading.md:214-224 and threat-model 432. They should say the Warning is held back throughout the document once that guard is reached, even where the search finds the section whole past the run and passes nothing over. That happens only under a MaxTrailerLength lowered below the search's 512 bytes.
- Drop "stands for what that search passed over or did not find" as the reason, or limit it.
- Pin row A run 300 at T256 ([offset-adjusted, limit.trailer, offset-adjusted], no Warning) in the R10c theory.
- Consequence: a file anomaly stays hidden under a caller-lowered guard, for a lower cost.
needs_maintainer: True

## 2. passed-over-warning-made-where-a-parted-type-hid-the-link — minor → narrowed minor (src-texts)
Location: src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:123-125 (the list 'a section it cut before the section said what it is'); cause at src/AdCodicem.Pdf/IO/PdfFileReader.cs:6416

Claim: The round's rule text says nothing is reported where a limit cut a section before the section said what it is. The code breaks this where MaxTrailerLength cuts inside the /Type value's name. The parser keeps the parted name '/X' (#279), and :6416 tests only !cut.ContainsKey(/Type). The section is then read as an object of another type: NotFound, cut=False, ChainStoppedByGuard false. Its /Prev, which names the passed-over stream, goes unread, and xref.stream-passed-over is made for a stream that default limits reach. So the guard hid the link and the Warning blames the file. The section-missing and the Error xref.section-not-found are main's behavior, #279's class. The Warning is new in batch 19, and the round 10 text claims this case is covered.

Evidence: Probe ZzR11SrcTexts.ZzPartedType (output head-parted.txt). The oldest section is cross-reference stream 7 at 292, '<< /Pad (...) /Type /XRef ... /Prev 1969 >>', padded so that a window of 140 bytes ends after '/Type /X'. Its /Prev names stream 11 at 1969. The newest hybrid table's /Prev falls 7 bytes short of stream 11.
Default: sections '/Prev Read Stream off=292', '/Prev Read Stream off=1969'; codes xref.offset-adjusted@1780, xref.hybrid-stream-lost@1780; no Warning.
MaxTrailerLength 139, 140, 141 and 142: '/Prev NotFound off=292 fault=holds object 7, which is not a cross-reference stream cut=False'. Codes: xref.offset-adjusted@1780, xref.hybrid-stream-lost@1780, limit.trailer@292, xref.section-missing@292, xref.stream-passed-over@1969, xref.rebuilt; chainStopped=False. Rule: 'Error xref.section-not-found: ... the offset holds object 7, which is not a cross-reference stream'.
MaxTrailerLength 135 (the key cut) and 143-144 (/XRef whole): 'Malformed cut=True', chainStopped=True, no Warning, Information xref.checked-in-part.
On main 21a0298 (worktree removed), the same /Type parted in the /XRefStm's stream (ZzTrailerEdge, Base shape, MaxTrailerLength 18-21) gives the same '/XRefStm NotFound ... which is not a cross-reference stream' and section-missing x2: the blame is pre-existing.

Fix: Comment on #279 with this shape. Add to its closing a row where a /Type parted by MaxTrailerLength reads as the guard's (Malformed, cut). That keeps xref.section-missing, xref.section-not-found and xref.stream-passed-over away. Until #279 lands, qualify the list in PdfDiagnosticCodes:123-125, ReportStreamsPassedOver's remark and diagnostics.md:116: a cut that parts the /Type's name is read as written (#279). Or, in this batch: at :6416, also treat as cut before it said what it is a dictionary whose /Type is a name the cut may have parted. That name is the last value parsed, and the guard was reached.

Verdict: The finding holds. I rebuilt it on 79c6a7f with two shapes of my own, and both reproduce it. When MaxTrailerLength cuts a chain-named cross-reference stream inside its /Type value, the parser keeps what it read of the name ('/', '/X', '/XR' or '/XRe'), as #279 describes. The test at PdfFileReader.cs:6416 asks only !cut.ContainsKey(/Type), so the section reads as NotFound with cut=False and ChainStoppedByGuard stays false. The stream's /Prev lies past the cut and is not read. That /Prev is the link through which default limits reach the passed-over stream, and xref.stream-passed-over is still made. The texts do not hold either. PdfDiagnosticCodes.XRefStreamPassedOver (:123-125), the remark of ReportStreamsPassedOver (:3718-3720) and diagnostics.md:116 say 'Nothing is reported where one of the reader's limits left part of the chain unread', and that clause already fails here before its list is read. The texts carry no #279 qualification.

The finding is narrower than written in four ways.
(1) The Warning is the batch's only new consequence. The blame of a parted /Type is main's, #279's class. I checked it on a plain chain of my own: a table whose /Prev names a stream with a parted /Type. Main gives xref.chain-cycle, xref.rebuilt and Error xref.chain-loop; the branch gives xref.section-missing, xref.rebuilt and Error xref.section-not-found. The codes differ (#188 moved where the blame lands), but both blame the file.
(2) The threat model already holds the general class. Its #279 gap says the parted token is kept 'by every reader of the object it cut', and :439 (main's text) defines 'before it said what it is' as 'no /Type yet'. So the misstatement is in the flat promise of the passed-over rule only.
(3) On main, this exact file reads the same at every window. Main takes stream 11 at the /Prev and never reaches the oldest stream. The route to the parted section comes from the batch's designed pass-over (#156); it is not a regression of round 10.
(4) The window is four bytes per section: from the empty name to '/XRe'. It opens only under a lowered MaxTrailerLength, or for a dictionary past 64 KB with its /Type at the boundary.

It is minor, not major: it is one more Warning, made beside limit.trailer, on top of an Error that main already blames on the file.

Verdict evidence: Probe: C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r11wip/verify-passed-over-warning-made-where-a-parted-type-hid-the-link/ZzR11VerifyParted.cs.txt (ZzVerifyParted, ZzVerifyPartedPlain). Files are in the same folder (parted-*.pdf). Outputs: head-parted.txt, main-parted.txt, head-parted-plain.txt, main-parted-plain.txt.

Shape 1 is mine and differs from the reviewer's: 120 spaces before /Type are the padding, the oldest stream 7 sits at 209 with /Prev 1972, and the newest table's /Prev falls 5 bytes short of stream 11.
- Default: sections '/Prev Relocated Stream off=1794 named=1967', '/Prev Read Stream off=209', '/Prev Read Stream off=1972'. Codes: xref.offset-adjusted@1794, xref.hybrid-stream-lost@1794. Object 10 = "ten". No Warning.
- MaxTrailerLength 137, 138, 139, 140 (window ends '/Type /', '/X', '/XR', '/XRe'): '/Prev NotFound Unknown off=209 cut=False fault=holds object 7, which is not a cross-reference stream'. Codes: ... limit.trailer@209, Warning xref.section-missing@209, Warning xref.stream-passed-over@1972, xref.rebuilt; chainStopped=False. Rule: Error xref.section-not-found.
- 129 to 136 (/Type key absent or with no value) and 141 to 142 (/XRef whole): Malformed cut=True, chainStopped=True, no Warning.

Shape 2 is mine and new: a classic table near the /Prev (2842) is what the search takes, and its /Prev names the parted stream 7.
- Default: no Warning; object 10 found.
- 137 to 140: limit.trailer@209, section-missing@209, Warning xref.stream-passed-over@2984, rebuilt; Error section-not-found.
- 136 and 141: limit.trailer only, chainStopped=True, object 10 null.

Control: the oldest stream gives no /Prev. The Warning is made at default and withheld under the key cut and under /XRef whole, as the rule says. It is made at 137 to 140.

Main 21a0298, the same files: shapes 1 and 2 give 'xref.offset-adjusted@1972|2984, xref.rebuilt' at every window, default included. Main takes stream 11 and never reaches stream 7.

Plain chain (parted-plain.pdf: a table at 442 whose /Prev names stream 7 at 186, with a parted /Type):
- Branch, 137 to 138: section-missing@186, rebuilt, Error xref.section-not-found.
- Main, 137 to 138: '/Prev Relocated Table off=442 named=186', xref.offset-adjusted@442, xref.chain-cycle@186, rebuilt, Error xref.chain-loop.
- Both: 136 and 141 give Malformed cut=True.
So the blame of a parted /Type is main's (#279), and only its code changed.

Code: the :6416 condition is main's (git show 21a0298, PdfFileReader.cs:3999), refactored in 2b26d18. The general gap is in threat-model.md:418 (#279). Earlier rounds (r1 to r10), decisions.md and residuals-impl.md do not hold this shape.
fix: Recommended, in this batch, following round 10's precedent for #289's class:
- Where a chain-named offset (not a candidate) holds a dictionary that the guard cut (cutByGuard) and whose /Type is a name that is a strict prefix of XRef (the empty name, X, XR, XRe), the cut may have parted /XRef. In that case, set a flag that only ReportStreamsPassedOver reads, beside ChainStoppedByGuard and _whiteSpacePastGuard (or widen that flag and rename it).
- Leave section-missing, section-not-found and the rebuild to #279.
- Rows: the two shapes above at MaxTrailerLength 137 to 140 give no Warning; default limits and the windows of the key cut and of /XRef whole stay unchanged; /Type /Catalog cut later still makes the report.
- Then comment on #279 with this shape and a closing row: a /Type that MaxTrailerLength parts at a chain-named offset reads as the guard's (Malformed, cut), so that section-missing and section-not-found go too.

Alternative:
- Give such a dictionary the guard's reading now, at :6416 (CutByLimit, Malformed), as for a missing /Type. This removes main's file blame in this reader as well, and fits either rule #279 settles. It touches what #279 plans to settle in batch 27.

Text-only fallback, if the triage leaves the code:
- Qualify PdfDiagnosticCodes.XRefStreamPassedOver (:123-125), the summary and remark of ReportStreamsPassedOver (:3708-3712, :3718-3720), diagnostics.md:116 and threat-model 432's passed-over clause: a /Type the cut parts is read as the part read, so as another type, and the section as missing, its links unread and the stream reported (#279).
- Comment on #279 either way.

The triage can make the choice without the maintainer: each option applies ADR 34 and round 9's [2]/[11] rule.
needs_maintainer: False

## 3. signed-header-stream-never-reported-as-passed-over — minor → confirmed minor (src-texts)
Location: src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:121-123; src/AdCodicem.Pdf/IO/PdfFileReader.cs:3707-3710, :5284-5289

Claim: Open point (b). The texts say a passed-over stream is reported where 'its dictionary gives /Type /XRef, and its header is a token a load reads from the start of its line, not in a comment or a string there'. A typed stream written '+11 0 obj' on its own line meets that wording: a load reads '+11' as a token, and the reader serves object 11 from there (#327). It is never reported. The walk finds it at the digit '1', and IsTokenOnItsLine is false there. The same header parted by a comment ('+11 0 %c\nobj') is found at the sign and reported. Its objects drop out of the index the chain read with no Warning. No text states this: the NumberRunsInto remark names the walk's '1' only for telling S.

Evidence: Probe ZzR11SrcTexts.ZzSignedOwnHeader (output head-signed.txt). The shape is PrevFallingShortOfTheStreamBeforeTheTablesOwn("nothing", true) rebuilt with stream 11's header varied; every variant gives the same sections.
'11 0 obj': xref.offset-adjusted@1683, xref.hybrid-stream-lost@1683, xref.stream-passed-over@1872.
'+11 0 obj': xref.offset-adjusted@1683, xref.hybrid-stream-lost@1683, with no Warning; object 11 reads as Stream through its row at the '+'.
'+11 0 %c\nobj': Warning@1872.
'011 0 obj': Warning.
'+011 0 obj': no Warning.

Fix: Comment on #327 with this shape. Its closing reads the sign in FindSectionCandidates, which places the header at the '+'; IsTokenOnItsLine then holds, and the stream is reported. Add a row to the closing tests. Until then, qualify PdfDiagnosticCodes:121-123 and diagnostics.md:116: 'its object number a token there, not digits after a sign the search does not read (#327)'. No code change in this batch.

Verdict: The finding reproduces at 79c6a7f. TryReadHeaderBackwards finds a '+11 0 obj' header at its digit '1' and never looks at the byte before it. At that '1', IsTokenOnItsLine is false, because the lexer's token starts at the '+'. So the record at PdfFileReader.cs:5284-5289 never records the stream, and nothing reports it. The same header parted by a comment is found at the '+' and is reported.

The public texts (PdfDiagnosticCodes:121-123, the ReportStreamsPassedOver summary at :3707-3708, diagnostics.md:116, lazy-reading.md:214, threat-model.md:476/485) promise the report for any typed stream 'whose header is a token a load reads from the start of its line'. '+11 0 obj' meets that wording: a load reads '+11' from the line's start, and the row at the '+' serves object 11.

Only private remarks imply the exception: IsTokenOnItsLine's 'nor digits a regular byte before them runs into' and NumberRunsInto's 'starts at its digit 1'. #327 holds the root: the section search reads a header's number as digits alone, and its closing would place this header at the '+'. But #327 does not name this consequence, and its threat-model entry (:504-506) does not either. So the known-debt rule does not refute it: a text misstates it, and the batch made it worse than main.

One correction to the evidence. In the reviewer's own shape (A), the claim 'its objects drop out of the index with no Warning' is false. xref.hybrid-stream-lost (a Warning) is made, and object 10 is served after a rebuild. The real consequence shows in another shape (B): an older classic table near the /Prev, taken first, with S read in its place. There the signed variant loses object 10 with no report at all and no rebuild, while main serves object 10. That loss comes from the batch's deferral (#156, R8-Q1) without the Warning that was accepted as its counterpart.

Severity stays minor, not major. It is a missing report confined to a damaged hybrid file whose typed stream writes its object number with a sign, which no writer known to the corpus does. The root is a filed debt. Round 10's analogous signed-header finding [20] was also minor.

Verdict evidence: The probe is my own builder, not the reviewer's: C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r11wip/verify-signed-header-stream-never-reported-as-passed-over/ZzVerifySignedPassedOver.cs.txt. Outputs are head-out.txt (79c6a7f) and main-out.txt (21a0298, from a temporary worktree, since removed), in the same folder. Default limits.

Shape A: the own stream 11 (typed, no /Prev) lies past S (stream 6, /Prev the oldest table). The newest table's /Prev falls 7 bytes short of stream 11; its /XRefStm names S.
- 79c6a7f, '11 0 obj': sections [startxref Read Table, /XRefStm NamedAgain 1617, /Prev Relocated Stream 1617, /Prev Read Table 226]. Open: Repair offset-adjusted@1617, Warning hybrid-stream-lost@1617, Warning stream-passed-over@1795. Object 10 is "ten" after Repair xref.rebuilt.
- 79c6a7f, '+11 0 obj': same sections. Open: offset-adjusted@1617, Warning hybrid-stream-lost@1617, with no stream-passed-over. Object 10 is "ten" after a rebuild, and 11 is a Stream. So in this shape a Warning is made and nothing is lost.
- 79c6a7f, '+11 0 %c\nobj' and ' 11 0 obj': the Warning is made. 'x11 0 obj': no Warning, which is right for x11; the '+' is treated like the 'x'.

Shape B: as A, plus an older classic table (object 9) at 1795, 134 bytes before the /Prev's offset 1929. Own stream at 1936.
- 79c6a7f, '11 0 obj': [startxref Read Table 2653, /XRefStm Read Stream 1617, /Prev Relocated Table 1795, /Prev Read Table 226]. Open: Repair offset-adjusted@1795, Warning xref.stream-passed-over@1936. Objects: 9 "nine", 10 -. repaired=False.
- 79c6a7f, '+11 0 obj': same sections. Open: Repair offset-adjusted@1795 only. Object 10 is -, with repaired=False and no report about it at all. Findings: [Warning:file.size-wrong, Warning:xref.section-shifted].
- 79c6a7f, '+11 0 %c\nobj': Warning stream-passed-over@1936.
- 79c6a7f, unreadable (/W [1 -4 2]): '11 0 obj' gives the Warning @1936; '+11 0 obj' gives none.
- main, '+11 0 obj': [/XRefStm Read Stream 1617, /Prev Relocated Stream 1937]. Open: offset-adjusted@1937, xref.rebuilt. Objects: 9 "nine", 10 "ten". Main serves the object the branch drops silently. Main is the same for '11 0 obj', from 1936.

Code at 79c6a7f:
- :5284-5285 `typedPassedOver?.Contains(passed) == true && IsTokenOnItsLine(window.Memory.Span, start - lead, (int)(passed - (start - lead)))`.
- StartsToken returns `token.Start == at`, and the lexer's '+11' token starts at the '+'.
- The same test gates the take of a Malformed place at :5259.
- r10-fix-result.json (R10b) already noted this as 'pre-existing and not changed (R9-Q2's rule)'.
- #327 (open) names the digit walk, but not this consequence.
fix: Recommended: fix it in this batch's code, as round 10's [20] was fixed because it was worse than main. This applies R9-Q2 as decided ('a header that is a token on its own line'), with no new decision.
- Where the walk's header starts one byte past a '+' (span[at - 1] == '+', a byte the window holds), ask IsTokenOnItsLine at the '+'.
- Do it at both line-test calls: the take of a Malformed place at :5259 and the record at :5285.
- Record the place at the '+'. NumberEnd already skips a sign, so the report's position and its 'reached' range match the comment path's.
- A '-' stays out, because #327's closing says '-4 0 obj' is no header.
- Rows, in shape B above: '+11 0 obj' gives the Warning at the '+' and object 10 null. Controls: '11 0 obj', '+11 0 %c\nobj', '-11 0 obj' (no Warning) and the unreadable '+11' (Warning).
- Bring the NumberRunsInto and IsTokenOnItsLine remarks in line.

Fallback, if the fix stage keeps it out of scope (the reviewer's fix), with no code change:
- Comment on #327 with shape B, not shape A: the silent loss, no rebuild, and main serving object 10.
- Name the consequence in threat-model.md's #327 Known-gaps entry (:504-506).
- Qualify the rule where it stands. In PdfDiagnosticCodes.XRefStreamPassedOver (:121-123), the ReportStreamsPassedOver summary (:3707-3708), the comment at :5266-5279, diagnostics.md:116, lazy-reading.md:214 and threat-model.md:476/485, add: 'its object number a token there — not digits after a sign, which the search near an offset does not read (#327)'.

Whichever is chosen, #327's closing tests should gain the shape-B row.
needs_maintainer: False

## 4. inside-number-reading-does-not-read-the-same-stream — nit → confirmed nit (src-texts)
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3721-3723; src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:120-121; docs/website/docs/reference/diagnostics.md:116; docs/website/docs/concepts/lazy-reading.md:216-217

Claim: The texts say 'a link that names a place inside its header's object number, past its first byte, reads the same dictionary and the same data from there, and reaches that stream as one that names its header does'. They also say 'at its header or inside its object number, from which a reading reads the same stream'. Neither holds where the digits past the named byte read as 0, as in 20, 100 or 1000. A reading at '0 0 obj' is no header. The stream is then reached only as a section found moved: the search near the link finds the header 1 or 2 bytes before, and xref.offset-adjusted is reported. The report's outcome is still right (the Relocated record starts at the header). 2be5c44's body makes the same claim, which matters for status.md's 'Commit bodies not rewritten'.

Evidence: Probe ZzR11SrcTexts.ZzInsideNumberZero (output head-inside.txt), on PrevFallingShortOfTheStreamBeforeTheTablesOwn("nothing", true, oldestNames, into, ownNumber).
/XRefStm, ownNumber 20, into 1: 'sec /XRefStm Relocated Stream off=1892 named=1893', plus xref.offset-adjusted@1892. Against ownNumber 21, into 1: 'sec /XRefStm Read Stream off=1893 named=1893', with no offset-adjusted.
/Prev, 20 at +1: 'Relocated Stream off=1889 named=1890' with xref.offset-adjusted@1889.
ownNumber 100 at +1 and +2: Relocated with offset-adjusted. ownNumber 101 at +2: Read at 1894.
No row gives xref.stream-passed-over.

Fix: Reword the four texts. A link naming a place inside the number reads the same stream from there where the digits from that place still read as an object number. Where they read 0, it reaches the stream as a section found moved, reported so. Either way the report counts the stream reached. Add the 2be5c44 body to status.md's 'Commit bodies not rewritten'.

Verdict: The finding holds. It is a text-only inaccuracy: the code's outcome is right. PdfObjectParser.TryReadIndirectObject (src/AdCodicem.Pdf/IO/PdfObjectParser.cs:322) refuses `number.Integer is <= 0`. So a link that names a place inside a header's object number where the remaining digits read 0 (20 at +1; 100 at +1 and +2; 1000 at +3) reads no object there. TryReadXRefStream gives NotFound ('holds neither the xref keyword nor an object'). The search near that offset then finds the stream moved, 1 to 3 bytes back at its header, and reports xref.offset-adjusted. The Relocated record starts at the header, and StartsWithin(told, at, numberEnd) counts it reached, so no xref.stream-passed-over follows. The texts are false for that case: (1) the remark at PdfFileReader.cs:3721-3723 says such a link 'reads the same dictionary and the same data from there, and reaches that stream as one that names its header does'; (2) PdfDiagnosticCodes.cs:120-121, diagnostics.md:116 and lazy-reading.md:216-217 say a reading from inside the number 'reads the same stream'. The finding misses one place: docs/threat-model.md:432 carries the same clause. 2be5c44's body over-generalizes the same way ('reads the same dictionary and data from there, and its record starts past the header'). The probe covers all three link kinds: an older /XRefStm, an older /Prev, and a newer table's /XRefStm read before the search. Nothing settles the case: decisions.md (round 10's [3]/[8]/[17] rows use 11, 111 and '13 named at the 3'), r10-findings.md, r10-fix-result.json, and #156, #171, #330 and #332 never name digits that read 0. It is not one of the accepted consequences either. The same text over-generalized with no behavior change, as round 10's [7] was, so it stays a nit. The main condition, 'the chain did not reach the stream, at its header or inside its object number', still matches the code.

Verdict evidence: Rebuilt on origin/ccr-6d6c0ee6-dqm4lm-188 at 79c6a7f (Release build, 0 warnings). The probe is at C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r11wip/verify-inside-number-reading-does-not-read-the-same-stream/ZzVerifyInsideZero.cs.txt. Outputs: older.txt and newer.txt in the same folder. The shape is HybridFiles.PrevFallingShortOfTheStreamBeforeTheTablesOwn("nothing", shortOfOwn: true, "the oldest table", oldestNames, into, ownNumber).

Older /XRefStm:
- own#20 into=0: 'sec /XRefStm Read Stream off=1892', no adjustment.
- own#20 into=1: 'sec /XRefStm Relocated Stream off=1892' plus 'Repair xref.offset-adjusted@1892: The cross-reference section /XRefStm names at offset 1893 was found 1 byte before it.'
- own#21 into=1: 'Read Stream off=1893', no adjustment.
- own#100 into=1 and into=2: Relocated off=1892, adjusted by 1 and 2 bytes.
- own#101 into=1 and into=2: Read at 1893 and 1894.
- own#1000 into=3: Relocated, 'found 3 bytes before it'.
- own#1001 into=3: Read off=1895.
- own#105 into=1: Read off=1893.

Older /Prev, the same pattern:
- own#20 into=1: 'Relocated Stream off=1889' plus 'xref.offset-adjusted@1889 ... names at offset 1890 was found 1 byte before it'.
- own#21 into=1: 'Read Stream off=1890'.
- own#100 at +1 and +2, and own#1000 at +3: Relocated.
- own#101 and own#1001: Read.

Newer table's /XRefStm (the probe's own append of a table naming own+into):
- own#20 into=1: 'sec /XRefStm Relocated Stream off=1872' plus 'xref.offset-adjusted@1872 ... names at offset 1873 was found 1 byte before it'.
- own#21 into=1: 'Read Stream off=1873'.
- own#100 into=2: Relocated, 2 bytes.
- own#101 into=2: Read off=1874.

No row gives xref.stream-passed-over, and every row serves object 10 as 'ten' with repaired=False. Cause: PdfObjectParser.cs:322 refuses `number.Integer is <= 0`, so '0 0 obj' read from inside '20 0 obj' is no header.

The same clause also stands at docs/threat-model.md:432: 'at its header or inside its object number, from which a reading reads the same stream, is reported as the chain finds it'. The test comment at CrossReferenceChainTests.cs:6496-6501 holds for its rows (11 at +1, 111 at +1 and +2) and needs no change.
fix: Texts only; the code needs no change. This is not a design choice.

1. PdfFileReader.cs:3721-3723 (the ReportStreamsPassedOver remark). Say that a link naming a place inside the header's object number, past its first byte, reads the same dictionary and data from there, its record starting there, where the digits from that place still give a number above 0. Where they read 0, as in 20 named at its 0, no object is read there, and the search near that place finds the stream moved, at its header, after xref.offset-adjusted. Either way a section the chain reached starts at the header or inside the number, and the stream counts as reached.

2. PdfDiagnosticCodes.cs:120-121, docs/website/docs/reference/diagnostics.md:116 (the second clause, 'A stream the chain reached ...'), docs/website/docs/concepts/lazy-reading.md:216-217 and docs/threat-model.md:432 (which the finding missed). Replace 'at its header or inside its object number, from which a reading reads the same stream' with 'at its header, or inside its object number, from which a reading reads the same stream where the digits from there give a number above 0, or past which, where they read 0, the search near that place finds it moved, at its header'. Or drop the relative clause and keep 'at its header or inside its object number', which is the condition the code tests.

3. Add 2be5c44's body to status.md's record of commit bodies not rewritten. It says a link naming a place inside the number reads the same dictionary and data from there, which is false where the digits from there read 0.

4. Optional: pin the route the new text states with a row or a small theory beside A_stream_passed_over_near_a_hybrid_table_s_prev_that_an_older_section_names_at_its_header_or_inside_its_object_number_..., taking ownNumber 20 at into 1 and ownNumber 100 at into 2. Expect Relocated at own, xref.offset-adjusted at own, and no xref.stream-passed-over.
needs_maintainer: False

## 5. line-start-summary-keeps-the-cannot-be-read-likeness — nit → refuted none (src-texts)
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:144-145

Claim: a7be8b1 rewrote the LineStartSearchLength summary. It says 'a place that cannot be read, which declares a section only where it starts with such a token'. That is the likeness round 10 [7] corrected everywhere else to 'a place a fault of the file keeps from being read', and a7be8b1's own body says why. A place a guard's cut of a value keeps from being read is taken whatever its line: CutByValue is tested before IsTokenOnItsLine, at :5257.

Evidence: Probe ZzR11SrcTexts.ZzCommentedPlaceCutByValue (output head-cutvalue.txt). A /Prev falls 20 bytes short of a comment line '% 9 0 obj << /Type /XRef /Size 10 /W 12 0 R /Index [9 1] /Length 3 >> stream'. Object 12 is '[1 1 1]' plus 1,100 spaces, and the oldest table is out of reach.
MaxObjectLength 1024: 'sec /Prev Malformed Stream off=1101 named=1081 cut=True'; open: xref.offset-adjusted@1101, limit.object@1899. The place in a comment is taken.
Control '% 9 0 obj << ... /W [1 -4 2] ...': 'sec /Prev NotFound off=1081'; xref.section-missing@1081. This is a fault of the file, and the place is dropped.

Fix: At :144-145, write 'a place a fault of the file keeps from being read, which declares a section only where it starts with such a token', as the other texts round 10 [7] corrected do.

Verdict: The behavior the reviewer shows is real, but the summary does not misstate it.

1. The summary at PdfFileReader.cs:143-146 is not round 10 [7]'s likeness. Round 10 [7] corrected a claim about taking: "as a place that cannot be read is taken only where it does". That claim is false for a place whose value a guard cut. The summary makes no claim about what is taken. It says what the line test (IsTokenOnItsLine) tells, and that an unreadable place "declares a section only where it starts with such a token". That is a necessary condition about declaring.

2. The code keeps declaring and the guard's ground apart. The comment at :5241-5244 takes an unreadable place where it "declared itself one ... or where a guard cut an object a value it is read with leads to (#263)". a7be8b1's own body says such a place "is taken whatever it declares". A commented place whose value a guard cut declares no section. It is taken on the guard's ground. Nothing in the summary contradicts that.

3. The summary's category, "a place that cannot be read", is the right one. The line test tells both a place the file's fault keeps from being read and a place a guard's cut of the place itself keeps from being read. The only places it skips are those whose value a guard cut (CutByValue, short-circuited at :5257) and untyped non-table places. IsTokenOnItsLine's own remark (~:5388) uses the same scope: "asked only of a place that cannot be read". With "only", that is accurate.

4. The proposed fix would put a misstatement in. "A place a fault of the file keeps from being read" leaves out a /Type /XRef place whose own dictionary a guard cut. That place is line-tested too: dropped where its header is in a comment, taken where it is a token on its line (probe below). The summary as written covers that place; the replacement would not.

5. The category is older than a7be8b1. c2a2aaa's summary already said "to tell a place that cannot be read and starts with a token ...". Round 10's [6]/[19] rewrote that summary without flagging the category, and decisions.md scoped [7] to the taking analogy only (:4985-4987, :5208-5209, three pages, one test comment).

Verdict evidence: Rebuilt on origin/ccr-6d6c0ee6-dqm4lm-188 (79c6a7f), Release build, 0 Warning(s). Probe: C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r11wip/verify-line-start-summary-keeps-the-cannot-be-read-likeness/ZzVerifyLineStartSummary.cs.txt. Output: .../verify-line-start-summary-keeps-the-cannot-be-read-likeness/head-out.txt. The shape is the reviewer's: a /Prev 20 bytes short of a header line, with the oldest table out of reach.

(1) The reviewer's value cut reproduces. '% 9 0 obj << /Type /XRef ... /W 12 0 R ...' under MaxObjectLength 1024 gives 'sec /Prev Malformed Stream off=1101 named=1081 cut=True fault=its /W is the reference 12 0 R, which one of the reader's limits cut' and 'open: xref.offset-adjusted@1101, limit.object@1899'. The place is taken.
Control, '% ... /W [1 -4 2]': 'sec /Prev NotFound', 'xref.section-missing@1081'. Uncommented '/W [1 -4 2]': 'Malformed Stream off=1099 ... fault=its /W gives a field a negative width', with xref.offset-adjusted and xref.section-unreadable. A place the file's fault keeps from being read is decided by its line.

(2) A guard's cut of the place itself. A /Type /XRef dictionary with '/Pad (200 p)' under MaxTrailerLength 120: no value cut, CutByValue false.
- Commented: 'sec /Prev NotFound Unknown off=1081', 'open: limit.trailer@1101, xref.section-missing@1081, xref.rebuilt@-1'. Dropped.
- Uncommented: 'sec /Prev Malformed Stream off=1099 named=1079 cut=True fault=its dictionary is not followed by stream data', 'open: limit.trailer@1099, xref.offset-adjusted@1099 ... chainStopped=True'. Taken.
So the line test also tells a place a guard keeps from being read. Only a value cut skips it (:5257, `attempt.CutByValue || ((TypedXRef || Table) && IsTokenOnItsLine(...))`).

Texts: the summary at :143-146 speaks of declaring, not taking. :5241-5244 lists the value cut as a ground separate from declaring. a7be8b1's body says such a place is "taken whatever it declares". IsTokenOnItsLine's remark says "It is asked only of a place that cannot be read". decisions.md 'Fix (texts)' gives [7]'s scope as the taking analogy; the summary was rewritten under [6]/[19]. The probe file was removed from the worktree, and nothing is committed.
fix: No change needed. Do not apply the proposed wording: "a place a fault of the file keeps from being read" would leave out a /Type /XRef place a guard's cut of itself keeps from being read, and the line test tells that place too (probe case 2). The maintainer may still want the summary to name the one exception explicitly. In that case, append to the summary's clause: "a place that cannot be read — but for one a guard's cut of a value it is read with keeps from being read, taken for the guard's whatever its line (#263) —, which declares a section only where it starts with such a token". That is optional precision, not a correction.
needs_maintainer: False

## 6. signed-header-never-passed-over — minor → narrowed minor (docs)
Location: docs/website/docs/reference/diagnostics.md:116 (also concepts/lazy-reading.md:213-215, threat-model.md:432 cell 2, src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:121-123) against src/AdCodicem.Pdf/IO/PdfFileReader.cs:5283-5288

Claim: This is open point (b). The pages, the XML doc and R9-Q2 say a passed-over stream is reported when its dictionary gives /Type /XRef and 'its header is a token a load reads from the start of its line'. A header written '+9 0 obj' on a line of its own is such a token: a load reads it. The walk back from obj records the place at the first digit, though, and IsTokenOnItsLine(place) is false there because the token starts at the sign. So the stream is never recorded and gets no Warning. The same header with a comment parting its tokens ('+9 0 %c\nobj') is found at the sign and does get the Warning. The pages state no such exception, and the outcome depends on how the header is spelled rather than on its data.

Evidence: Generator: C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r11wip/docs/genb/gen.py (run with python -I in genb/). It builds a hybrid table whose /Prev falls 20 bytes short of object 9, a readable /Type /XRef stream with no /Prev past stream 21, with the oldest table within reach. Only object 9's header changes between files. Probe: ZzR11DocsProbe.cs.txt, driven by run.py with spec-b.txt. Outputs: out-b-head.txt (79c6a7f), out-b-pre.txt (2be5c44), out-b-main.txt (21a0298). On 79c6a7f:
- plain.pdf ('9 0 obj'): 'Repair xref.offset-adjusted at 229: ... 285 bytes before it.' then 'Warning xref.stream-passed-over at 534: The cross-reference stream at offset 534, near offset 514, ...'.
- sign.pdf ('+9 0 obj'): only 'Repair xref.offset-adjusted at 229: The cross-reference section /Prev names at offset 514 was found 285 bytes before it.', with no Warning.
- sign-comment.pdf ('+9 0 %c\nobj'): 'Warning xref.stream-passed-over at 534'.
- comment.pdf ('9 0 %c\nobj'): Warning at 534.
- sign-space.pdf (' +9 0 obj'): no Warning.
- signed-load.pdf (a classic table placing object 9 at '+9 0 obj'): 'O ... 9="nine"', so a load reads that header.
2be5c44 gives the same results. main has no xref.stream-passed-over code, so this is not a regression against main; it is a gap in R9-Q2 as decided ('a header that is a token on its own line').

Fix: Preferred: when the walk back from obj records a passed-over place, take a '+' (or '-') immediately before the digit run as the header's start for the line test. This is one byte in the window that is still open, with no read. The comment path already starts there, and NumberEnd already skips the first byte, so the two paths agree. Add rows for sign, sign-space and sign-comment beside A_place_passed_over_near_a_hybrid_table_s_prev_is_reported_only_where_it_declares_itself_a_cross_reference_stream. Otherwise, file a debt and have diagnostics.md:116, lazy-reading.md, threat-model 432 and PdfDiagnosticCodes.XRefStreamPassedOver say that a header whose number is written with a sign, found at its first digit, is not reported unless a comment parts its tokens.

Verdict: The behavior reproduces exactly. TryReadHeaderBackwards finds '+9 0 obj' at its digit 9. IsTokenOnItsLine(9) is false because the lexer's token '+9' starts at the sign, so the stream never goes into _streamsPassedOver (PdfFileReader.cs:5284-5288). The comment path finds '+9 0 %c\nobj' at the sign and reports it.

It is narrower than claimed: this is not a gap in R9-Q2 as decided. It is #327, a debt this batch filed and placed in batch 21. #327 says the section search (FindSectionCandidates) reads a header's number as digits alone and leaves out the sign. Its closing, "+4 0 obj is object 4's header" in the section search too, removes this outcome.

The code treats the found place '9' exactly like 'x9 0 obj', which is accepted: the test names say "behind a letter", and the pages say "digits a regular byte runs into", '+' being a regular byte. The same mechanism also reaches #281's take. An unreadable typed stream written '+9 0 obj', nearest the offset, is dropped and an older table farther off is taken, the same as 'x9' and a /W-only object (accepted class).

For that reason the reviewer's preferred fix is wrong:
- Taking the sign only where the passed-over place is recorded would make the report and the take disagree. The pages say a stream is reported "as a place a fault of the file keeps from being read is taken only where it does".
- Its '(or -)' contradicts #327's closing: '-4 0 obj' is no header.

The texts are not false, but they mislead when read alone:
- diagnostics.md:116, lazy-reading.md:213-214, threat-model 432 and PdfDiagnosticCodes.XRefStreamPassedOver (:121-122) make "a token a load reads from the start of its line" the test, and #327 itself says a load reads '+9 0 obj' as object 9's header.
- That the search finds a signed header "at the digit past" the '+' is said only in the /XRefStm context (diagnostics.md:115, threat-model 432/434, lazy-reading.md:202).
- The pages list the exclusion "digits a regular byte runs into" for the take (diagnostics.md:110, threat-model 434), not in row 116.
- Neither the pages nor #327 name either consequence.

Not worse than main for the Warning, which main does not have. The take's shape reads worse than main, but so do the accepted 'x9' and /W-only shapes; main gets them right only through the #188 loop and a rebuild. So this is minor: a text that mispredicts an exotic spelling, plus an untracked consequence of a filed debt.

Verdict evidence: The generator and probe are my own, under C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r11wip/verify-signed-header-never-passed-over/:
- gen.py (run as `python -I gen.py <dir>/files`)
- ZzR11VerifySignedProbe.cs.txt
- outputs out-head.txt (79c6a7f) and out-main.txt (21a0298, built in a temporary worktree, since removed)

Shape A is the reviewer's layout: the /Prev falls 20 bytes short of object 9, a readable /Type /XRef stream with no /Prev past S; the oldest table is at 229. On 79c6a7f:
- a-plain '9 0 obj': 'Repair xref.offset-adjusted at 229 ... 285 bytes before it.' then 'Warning xref.stream-passed-over at 534'.
- a-sign '+9 0 obj': only 'Repair xref.offset-adjusted at 229'.
- a-sign-comment '+9 0 %c\nobj': Warning at 534.
- a-comment: Warning at 534.
- a-sign-space ' +9 0 obj': no Warning.
- a-zero '09 0 obj': Warning.
- a-minus '-9 0 obj': no Warning, which is right per #327.
- a-gen-sign '9 +0 obj': no Warning; #327's first shape, not found at all.

On main every A row is 'Relocated Stream', then xref.rebuilt; main has no stream-passed-over code.

Shape B is the #281 take. A classic table's /Prev names 6 bytes short of an unreadable stream ('/W [1 -4 2]') indexing object 5 'new five'; an older table at 229 lies 390 bytes off with 'old five'. On 79c6a7f:
- b-plain and b-sign-comment: 'S /Prev named=619 Malformed Stream at=625', 'Warning xref.section-unreadable at 625', 'Repair xref.rebuilt', 5="new five".
- b-sign '+9 0 obj': 'S /Prev named=619 Relocated Table at=229', only 'Repair xref.offset-adjusted at 229 ... 390 bytes before it.', WasRepaired False, 5="old five".
- b-letter 'x9 0 obj' and b-wonly (no /Type, /W only): identical to b-sign, 5="old five".

On main all five B rows read 'Relocated Table at=727/728' (the newest table, #188), 'Warning xref.chain-cycle at 619', 'Repair xref.rebuilt', 5="new five".

Code and texts at 79c6a7f:
- NumberRunsInto's remark (:5338-5341): "the header +17 0 obj starts at its digit 1, one byte past the sign".
- IsTokenOnItsLine's summary: "nor digits a regular byte before them runs into (#281)".
- #327 body: "the search for a misplaced section (FindSectionCandidates) ... a sign, like any other byte (#171), is left out of the number". Its closing: "the section search read a header's number ... its sign included. +4 0 obj ... [is] object 4's header. -4 0 obj ... is no header".
- The threat model's #327 Known-gaps entry (:504-506) names only the rebuild's and the row search's consequences.
fix: No code change in this batch. The root is #327, already placed in batch 21. Its fix, the walk taking a '+' right before the digit run where the byte before that is not regular, never a '-', moves the take and the report together.

In this batch:
1. Tracking: comment on #327 with both consequences and their shapes, with this probe's outputs on 79c6a7f and main:
   - Passed over: a-sign, a-sign-space and a-gen-sign get no Warning, while a-sign-comment gets one.
   - #281's take: b-sign drops the unreadable typed stream and takes the older table, 'old five' in silence, while b-sign-comment is section-unreadable and rebuilt.
   - Add to its closing the passed-over rows (sign, sign-space, sign-comment) beside A_place_passed_over_near_a_hybrid_table_s_prev_is_reported_only_where_it_declares_itself_a_cross_reference_stream, and a take row beside The_header_of_a_cross_reference_stream_a_load_reads_near_where_an_offset_names_no_section_is_the_section_found_moved.
2. Text: one clause in the threat model's #327 Known-gaps entry, and the same in diagnostics.md:116 or lazy-reading.md:213-215 if wanted. Suggested wording: "the search near a section finds a header whose number carries a sign at its first digit, digits a regular byte runs into: such a header declares nothing, so a stream of /Type /XRef so written is neither taken where it cannot be read nor reported as passed over, unless a comment parts its tokens."

Do not take the reviewer's record-only patch: it would report as passed over a header the take still treats as declaring nothing, and its '-' contradicts #327's closing. This is no new design choice; #327's placement already decides it. Fixing #327's section-search part now instead would be the maintainer's call: it moves the take, the report and NumberRunsInto's '+' mapping (b), which would then no longer apply to the walk, and needs its own mutation run.
needs_maintainer: False

## 7. 144-row-guard-never-counted-left-out — minor → narrowed minor (docs)
Location: docs/threat-model.md:561 (the #144 row, cell 2)

Claim: Round 10 added this clause to the row: 'The guard is still counted ... — counted reached, and never as an entry the capacity left out, whatever a rollback of the reading that met it again keeps —'. It is false where the capacity left the guard's report out. Each later meeting of that guard, including the /Prev's own reading after the check rolled back beside the limits (the case the clause names), is counted again in SuppressedCount. The tests the row itself cites show this: A_report_full_diagnostics_left_out_is_counted_again_each_time_its_stream_is_decoded and A_copy_of_a_report_the_capacity_left_out_is_counted_again_each_time_a_decoding_meets_it. R10a's open point 1 gave the qualifier 'whose report the document holds'; R10d (deviation 2) dropped it. The body of commit 220eab4 states the same thing unqualified: 'A guard a decoding meets again is counted reached, never as an entry the capacity left out.'

Evidence: File: genh/check-untyped-flate.pdf, written by ZzR11DocsShapes.cs.txt as HybridFiles.PrevNamingAStreamBesideTheOldestTable("", padding: 4000, filter: "/FlateDecode", deflated: true). Run under MaxDecodedStreamLength 1000 with the probe's Redecode=4, which decodes object 4 three times after open. Outputs: out-shapes-head.txt and out-b-head.txt (79c6a7f), out-b-main.txt (21a0298), under C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r11wip/docs.
- Capacity 1000: entries 'Warning limit.decoded-stream at 1772' and 'Warning xref.hybrid-stream-lost at 1647', 'suppressed=0', and the three decodes leave it at 0.
- DiagnosticCapacity 0: 'repaired-at-open=False suppressed=3' (two reports, but three counted: the guard counted twice, once for the check's decoding and once for the /Prev's reading that meets it again), then '[redecode0 suppressed=6 count=0] [redecode1 suppressed=7 count=0] [redecode2 suppressed=8 count=0]'. The step from 3 to 6 includes the rebuild's two reports; after that, +1 for each decode that meets the guard again.
- DiagnosticCapacity 2: the report is kept, and the decodes leave SuppressedCount at 2.
main also gives 'suppressed=3' at open with capacity 0, so the code is unchanged. Only the new text overstates.

Fix: Restore the qualifier: '— counted reached, and, where the document holds its report, never as an entry the capacity left out, whatever a rollback of the reading that met it again keeps; where the capacity left its report out, each meeting is counted as one left out, as any report is —'. Record 220eab4's sentence in status.md under 'Commit bodies not rewritten'.

Verdict: The finding holds, but only in a narrower case than it says. The clause round 10 added to the #144 row (docs/threat-model.md:561, cell 2, from 220eab4) says a guard is counted reached "and never as an entry the capacity left out, whatever a rollback of the reading that met it again keeps". That is true where the document holds the guard's report. It is false where the capacity left that report out. In that case the guard is counted in SuppressedCount at every meeting. This includes the /Prev's own reading after the check rolled back beside the limits, which is the case the clause names.

The code does this on purpose. RollBackBesideLimits keeps the left-out guard counted through _limitsSuppressed. MoveDecodingReportsTo then finds no held copy and adds the guard again, so the full branch counts it again. Its remark says a repeat of a dropped entry "is counted again, as any report is".

Other texts keep the qualifier. PdfDiagnostics.cs:158 says "A guard a decoding reached again, whose report a decoding moved in already, is counted reached with no report". R10a's open point proposed the row clause as "a guard a decoding meets again, whose report the document holds". R10d (deviation, r10-fix-result.json) dropped that qualifier. The row now also contradicts a test it cites by name: DiagnosticsTests.A_guard_s_report_the_capacity_left_out_stays_counted_left_out_through_a_rollback_beside_the_guards_however_it_came. GuardCutValueTests' test, cited beside it, is named "..._where_the_diagnostics_have_room".

One could read "still" as limiting "the guard" to one whose report the document holds. But the clause speaks of the capacity explicitly and says "never". With capacity 0 the document holds no report, so that reading has nothing to apply to.

The finding cites two tests as proof. Both use filter faults, not guards, so they show only the general rule. The test that holds the guard case is the one named above.

This is not known debt and was not settled in an earlier round: the clause is new in 220eab4. The code behind it is not new, and the finding does not claim it is. Severity is minor: the text misstates what SuppressedCount counts, and no behavior is wrong.

Verdict evidence: I rebuilt the reproducer myself on 79c6a7f (origin/ccr-6d6c0ee6-dqm4lm-188): Release build of the test project with 0 warnings, then my own probe. The probe is C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r11wip/verify-144-row-guard-never-counted-left-out/ZzR11Verify144.cs.txt. It builds the file in-process with HybridFiles.PrevNamingAStreamBesideTheOldestTable("", padding: 4000, filter: "/FlateDecode", deflated: true), saved as check-untyped-flate.pdf in the same folder. It opens the file under MaxDecodedStreamLength 1000, reads object 4, decodes it three times, and prints Count, SuppressedCount and LimitsReached. Output is out-head.txt in the same folder.

- cap=0: open "count=0 suppressed=3 limitsReached=2". The two reports are limit.decoded-stream and xref.hybrid-stream-lost, so the guard is counted left out twice: once by the check's decoding and once by the /Prev's reading after RollBackBesideLimits. After GetObject(4): "suppressed=5" (the rebuild's two reports). Then "decode0: suppressed=6 limitsReached=3", "decode1: suppressed=7 limitsReached=4", "decode2: suppressed=8 limitsReached=5". Each meeting adds one left out.
- cap=1: guard held. Open "count=1 suppressed=1 limitsReached=2"; the decodes stay at "suppressed=3" while limitsReached goes 3, 4, 5.
- cap=2 and cap=1000: open "suppressed=0 limitsReached=2", with both reports held. The decodes leave SuppressedCount unchanged (2 at cap=2 after the rebuild's reports, 0 at cap=1000) while limitsReached goes 3, 4, 5. This is the clause's true case.

These match the reviewer's out-b-head.txt and out-shapes-head.txt under r11wip/docs. Code: PdfDiagnostics.cs:94-101 (Add's full branch counts a limit.* entry in _suppressed and _limitsSuppressed), :188 (RollBackBesideLimits keeps _limitsSuppressed in _suppressed), :264-279 (MoveDecodingReportsTo adds a copy the target does not hold, so a dropped guard is counted again). The commit body of 220eab4 says, unqualified: "A guard a decoding meets again is counted reached, never as an entry the capacity left out."
fix: Restore the qualifier R10a gave, in docs/threat-model.md:561 cell 2. For example: "The guard is still counted, so that what it cut is never taken for damage — counted reached and, where its document holds its report, never as an entry the capacity left out, whatever a rollback of the reading that met it again keeps; a report of it the capacity left out is counted again at each meeting, as any report is —, and a document opened to throw …". Leave the tests and the code as they are; the row's citations then agree with its text.

Add 220eab4's sentence to the batch's status.md records of commit bodies not rewritten, beside "e157359 + 68a55fa counted a held guard as left out until daca13c": the sentence is true only where the document holds the guard's report.

This is not a design choice for the maintainer. Text only.
needs_maintainer: False

## 8. unwrapped-prose-lines-220eab4 — nit → narrowed nit (docs)
Location: docs/website/docs/concepts/reader-limits.md:98; docs/website/docs/concepts/lazy-reading.md:225

Claim: Commit 220eab4 left two prose lines unwrapped. Lines around them wrap at 120 columns, but reader-limits.md:98 is 203 characters ('over near a hybrid table's `/Prev` is then reported as one the chain did not reach. An `/XRefStm` that names ...') and lazy-reading.md:225 is 146 characters ('holds — leaves the report made. Four cases fall short: ...'). The rendered pages are unaffected.

Evidence: Measured with awk 'length($0) > 120 && $0 !~ /^\|/' on 79c6a7f: 'docs/website/docs/concepts/reader-limits.md:98: 203' and 'docs/website/docs/concepts/lazy-reading.md:225: 146'. lr.diff (git diff c2a2aaa..79c6a7f) shows both lines come from this round: lazy-reading's old line was 120 wide before the edit, and reader-limits.md:97-98 are new.

Fix: Rewrap both paragraphs at 120 columns. The text stays the same.

Verdict: The finding holds. Commit 220eab4 left two source lines much wider than the rest of their paragraphs, and the rendered pages do not change. I narrowed it in three ways. (1) lazy-reading.md:225 is 144 characters, not 146. The reviewer's awk counted bytes, and the line holds one em dash, which takes 3 bytes in UTF-8. reader-limits.md:98 is 203 characters, as claimed. (2) Nothing in the repo sets a 120-column rule for Markdown. .editorconfig's [*.md] section only sets trim_trailing_whitespace, there is no markdownlint or prettier config, and CLAUDE.md says nothing about it. Both pages also had unwrapped prose lines on main 21a0298, so 'lines around them wrap at 120' is true of the two paragraphs, not of the pages. (3) Across this round's whole docs diff, these are the only two lines that stand out. The only other added line over 120 is docs/threat-model.md:480 at 121 characters, and that file already has 27 prose lines over 120. The batch has fixed this kind of nit before when it was in text the batch added: round 4 [15]/[20] and round 6 [5], in decisions.md. Rewrapping these two lines is consistent with that, so the finding stands as a nit.

Verdict evidence: The probes are under C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r11wip/verify-unwrapped-prose-lines-220eab4/. longlines.py counts characters per line, and addedlong.py lists the lines a diff adds over 120 characters. docs.diff is `git diff c2a2aaa 79c6a7f -- docs`, and rl-*.md and lr-*.md are the two pages on main and on c2a2aaa.

At 79c6a7f, addedlong.py over docs.diff prints:
- 'docs/threat-model.md:480: 121 chars, 121 bytes'
- 'docs/website/docs/concepts/lazy-reading.md:225: 144 chars, 146 bytes'
- 'docs/website/docs/concepts/reader-limits.md:98: 203 chars, 203 bytes'

`git blame` puts both flagged lines on 220eab40.

Line widths around them:
- reader-limits.md:88-101 run 111-118, then :98 is 203.
- lazy-reading.md:215-231 run 113-120, then :225 is 144.

On main 21a0298, longlines.py finds prose lines over 120 in the same pages:
- rl-main.md: 6 lines (:42 is 190, :63 is 185).
- lr-main.md: 11 lines (:22 is 180, :39 is 204, :42 is 170, :71 is 139, :74 is 140).

c2a2aaa has the same counts (6 and 11).

.editorconfig's [*.md] section only sets 'trim_trailing_whitespace = false'.
fix: Rewrap the end of the paragraph at docs/website/docs/concepts/reader-limits.md:97-99 and the lines at docs/website/docs/concepts/lazy-reading.md:224-226 so that no line passes 120 characters. Change only the wrapping, not the text. The rendered pages stay the same. This needs no maintainer decision. It can go in the next docs commit or be dropped, since the pages already carry older unwrapped lines and no rule enforces a width.
needs_maintainer: False

## 9. 171-comment-default-message-main — nit → confirmed nit (docs)
Location: GitHub comment 6724664613 on #171, section '`comment-40.pdf` at default limits, on the branch and on `main` alike'

Claim: The section says the outcome is the same on the branch and on main, and quotes the branch's message 'was found 232 bytes before it.'. main prints 'was found -232 bytes from there.' (#241's rewording). The comment gives main's wording for the 650 case and the #332 comment does the same for its own case, but this section does not. The outcome itself (dropped, stream 11 taken, nothing rebuilt, "new four", section-shifted at 382) is identical.

Evidence: Regenerated the comment's files with its own gen.py (C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r11wip/docs/gen171; byte-identical to r10track/g171). out-comments-head.txt gives 'D Repair xref.offset-adjusted at 382: The cross-reference section /XRefStm names at offset 614 was found 232 bytes before it.' out-comments-main.txt gives 'D Repair xref.offset-adjusted at 382: The cross-reference section /XRefStm names at offset 614 was found -232 bytes from there.'

Fix: None needed beyond an optional edit to the comment, '(`main` words it "was found -232 bytes from there")', as the #332 comment does. Do not post a new comment for it.

Verdict: The finding holds. The section of the posted #171 comment (6724664613) headed "`comment-40.pdf` at default limits, on the branch and on `main` alike" quotes one message line. That line is the branch's #241 wording, "was found 232 bytes before it.". main at 21a0298 prints "was found -232 bytes from there." for the same file. The outcome really is the same on both: stream 11 is taken at 382, nothing is rebuilt and object 4 reads "new four". So only the quoted text is off. The comment is also inconsistent with itself here: its next main section, the one under 650, gives main's wording in parentheses ("was found -115 bytes from there"). None of decisions.md, r1- to r10-findings.md, r10-fix-result.json or the issues settles or accepts this. It is wording in a tracking comment, not in the repository, and nothing reads it as a behavior claim. So it is a nit, not minor.

Verdict evidence: - **The posted comment.** Fetched with `gh api repos/AdCodicem/AdCodicem.Pdf/issues/comments/6724664613` into W/r11wip/verify-171-comment-default-message-main/posted.md. Line 50 is the "on the branch and on `main` alike" heading. Line 53 quotes `Repair xref.offset-adjusted at 382: The cross-reference section /XRefStm names at offset 614 was found 232 bytes before it.`. Line 70, the main section under 650, gives main's own wording: ("was found -115 bytes from there").
- **The files, rebuilt.** I took the comment's Python block out of the posted body with extract.py, saved it as gen.py and ran it in .../verify-171-comment-default-message-main/files. comment-40.pdf is byte-identical to r10track/g171/comment-40.pdf (SHA-256 2296C5DA…C73D). Stream 11 is at 382, the run's comment is at 497 and the hybrid table has `/XRefStm 614`.
- **main at 21a0298.** My worktree was at main. I built it Release and ran the probe .../verify-171-comment-default-message-main/ZzVerify171Main.cs.txt, then removed the probe from the worktree. Output, in .../out-main.txt:
  - `D Repair xref.offset-adjusted at 382: The cross-reference section /XRefStm names at offset 614 was found -232 bytes from there.`
  - `4=new four`
  - `repaired=False`
- **The message sources.** main's PdfFileReader.cs:3293 formats `was found {candidate - absolute} bytes from there.`. The branch's PdfFileReader.cs:5266 at 79c6a7f formats `was found {CountText.Distance(candidate - absolute, "it")}.`, which gives "232 bytes before it".
- **The branch's line.** I did not run the branch myself. Its output comes from the reviewer's out-comments-head.txt:324, which matches the comment's quote and agrees with the format string above. Both runs show the same outcome otherwise: Relocated Stream at 382, nothing rebuilt, "new four", and Warning xref.section-shifted at offset 382.
fix: Optionally edit comment 6724664613 in place, after the quoted line: "(`main` words it "was found -232 bytes from there")". That matches what the comment already does for the 650 case and what the #332 comment does. Do not post a new comment. Nothing in src/, tests/ or docs/ changes.
needs_maintainer: False

## 10. check-guard-ahead-of-search-overstated — minor → confirmed minor (undecoded: 42ca0bb and 97b09a8)
Location: docs/website/docs/reference/reader-limits.md:205

Claim: The page says the guard the look at a hybrid /Prev's place reaches is reported "there, once — ahead of what the /XRefStm's search reports". That is false when the search tries a nearer place first and drops it. What reading that place's dictionary reported stands by design (TryReadXRefStream's candidate remark, TryRelocateXRefSection), and it is reported before the look's guard.

Evidence: Generator: W/r11wip/undecoded/ZzUndGenTests.cs.txt, fact ZzUndGen2, writes files to W/r11wip/undecoded/files2. Object 8, the nearest place to the /XRefStm's offset, is an untyped /W stream with /Filter /Foo. In e_keyrep its dictionary repeats a key (/Q 1 /Q 2); in e_len its /Length is 9 for 3 bytes of data. Object 4, which the /Prev names, is untyped with 107 bytes of data. Reader: ZzUndReadTests.cs.txt, fact ZzUndRead2, output in W/r11wip/undecoded/read2-head.txt. Results on 79c6a7f:
- e_keyrep, MaxStreamLength 106: [syntax.key-repeated @1676, limit.stream-length @1812, xref.hybrid-stream-lost @1647].
- e_len, MaxStreamLength 106: [stream.length-invalid @1704, limit.stream-length @1802, xref.hybrid-stream-lost @1647].
- e_keyrep_flate, MaxDecodedStreamLength 1000: [syntax.key-repeated @1676, limit.decoded-stream @1832, xref.hybrid-stream-lost @1647].
In each, the dictionary report of the place the search dropped (object 8) comes before the look's guard. On c2a2aaa (read2-c2a.txt), e_keyrep at 106 gives [syntax.key-repeated, xref.hybrid-stream-lost] and no guard at all, which is the regression 42ca0bb fixed. The sentence was added in 220eab4.

Fix: Reword to "reported there, once, where the search comes to that place — after what reading the nearer places it tried reported, ahead of the /XRefStm's own report —, and not again by ...". Optionally add an e_keyrep-like row that pins the order.

Verdict: The finding holds, and I rebuilt it on 79c6a7f with my own generator. reader-limits.md:205 says the guard the look reaches is reported "there, once — ahead of what the /XRefStm's search reports". That is false where the search tries a nearer place first and drops it, and reading that place reports something that stands. By design (TryRelocateXRefSection :5303-5310, "What reading its dictionary reported stands too, as for any place dropped"; TryReadXRefStream's candidate comment), the dropped place's dictionary report stays, and it comes before the look's guard. Candidates are sorted nearest first, and the look (KeepsItsPlace) is asked only when the loop reaches leaveAt, so every nearer place has been read by then.

Scope, which matches what the finding says:
- The guard does come ahead of the /XRefStm's own report (xref.hybrid-stream-lost) in every shape I built.
- Where the look keeps no place and the search goes on, the guard also comes ahead of what the search reports after the /Prev's place (xref.offset-adjusted).
- A nearer place that leaves nothing behind (a string, my control) keeps the guard first.

threat-model.md:432 already states this correctly ("ahead of the /XRefStm's own report"). Only the reference page's sentence, new in 220eab4, is too broad. It is not an accepted or filed item, and neither #171 nor #289 holds it. No code defect: the order is what the design says. Only the text misstates it.

Verdict evidence: Probe (my own builder, independent of the reviewer's): W/r11wip/verify-check-guard-ahead-of-search-overstated/ZzVcgaTests.cs.txt. Output: out-head.txt in the same folder. Generated files: files/. W = C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud.

Shape:
- Oldest table at 1459.
- Object 8 at 1627, the nearest place to the /XRefStm offset 1647. It is either a string (control), or an untyped stream `<< /W [1 4 2] /Size 10 /Index [7 1] /K 1 /K 2 /Filter /Foo /Length 3 >>` (keyrep), or the same with `/Length 11` for 3 bytes of data (lenbad).
- Object 4, which the newest table's /Prev names: an untyped or typed stream with 107 bytes of data, either raw or with /Filter /Foo.

Results on 79c6a7f, MaxStreamLength 50:
- string_untyped_raw: [limit.stream-length @1762, xref.hybrid-stream-lost @1647]. The guard comes first.
- keyrep_untyped_raw: [syntax.key-repeated @1676, limit.stream-length @1812, xref.hybrid-stream-lost @1647]. Under ThrowOnLimit it throws limit.stream-length @1812.
- lenbad_untyped_raw: [stream.length-invalid @1705, limit.stream-length @1803, xref.hybrid-stream-lost @1647].
- keyrep_untyped_foo (the look keeps no place and the search goes on): [syntax.key-repeated @1676, limit.stream-length @1825, xref.offset-adjusted @1459, xref.hybrid-stream-lost @1647, filter.unsupported @1825, xref.offset-adjusted @1459].
- Typed controls: keyrep_typed_raw gives [syntax.key-repeated, xref.hybrid-stream-lost, limit.stream-length @1824]. There the guard comes from the /Prev's own reading.

Under default limits, keyrep_untyped_raw gives [syntax.key-repeated @1676, xref.hybrid-stream-lost]: the report is made before the /XRefStm's report and never again. So it comes from the search's reading of object 8, not from a later read.

These results match the reviewer's read2-head.txt (e_keyrep, e_len, e_keyrep_flate). No other text claims this order: grep finds 'ahead of what the `/XRefStm`'s search reports' only at reader-limits.md:205. The worktree is restored, and no probe is left in tests/.
fix: Make the reference page match threat-model 432. In reader-limits.md:205, replace "is reported there, once — ahead of what the `/XRefStm`'s search reports, and not again by ..." with "is reported there, once, where the search comes to that place — after what reading a nearer place it tried and dropped reported, which stands, ahead of the `/XRefStm`'s own report and of what the search reports past that place —, and not again by ...". Optionally pin the order with a sibling row next to GuardCutValueTests.A_guard_reached_decoding_a_stream_a_prev_names_is_reported_once_whatever_the_look_at_the_prev_s_place_drops: a nearer untyped /W place whose dictionary repeats a key, object 4 untyped with 107 raw bytes, MaxStreamLength 50, expecting exactly [syntax.key-repeated, limit.stream-length, xref.hybrid-stream-lost]. This needs a text change only, with no code change and no maintainer decision.
needs_maintainer: False

## 11. check-decoding-guard-position-untested — minor → confirmed minor (undecoded: 42ca0bb and 97b09a8)
Location: tests/AdCodicem.Pdf.Tests/GuardCutValueTests.cs:3916

Claim: Every row of A_guard_reached_decoding_a_stream_a_prev_names_is_reported_once_whatever_the_look_at_the_prev_s_place_drops is a MaxStreamLength row, and that guard is reached by reading the data. No test pins where a decoding guard is reported (MaxDecodedStreamLength, MaxFilterChainLength) when the look reaches it with every value read and the place keeps its section. reader-limits.md:204-206 and TryReadXRefStream's check paragraph (PdfFileReader.cs:6318-6319, 'reported once for the stream, where its data is first read') say that guard is reported at the look, ahead of the /XRefStm's report. Head does this. c2a2aaa's line (plain RollBack at :6644) still passes every test except the three MaxStreamLength rows: it reports those guards later, after xref.hybrid-stream-lost. The A_guard_reached_again (cut: null) row cannot tell either: its [/FlateDecode /Foo] place is dropped and tried again as a candidate, so c2a2aaa gives the same order there.

Evidence: Mutant M2: `_diagnostics.RollBack(decodingChecked);` at PdfFileReader.cs:6644, in a worktree of 79c6a7f. It ran GuardCutValueTests, CrossReferenceChainTests, HostileInputTests and DiagnosticsTests: 1,329 tests, 3 failed, exactly the MaxStreamLength rows (/Foo at 50, none at 50, none at 5). Log: W/r11wip/undecoded/mut2-tests.log.
The probe (ZzUndGenTests.cs.txt, fact ZzUndGen, writing W/r11wip/undecoded/files; ZzUndReadTests.cs.txt, fact ZzUndRead) gives the same output on M2 as on c2a2aaa: `python -I blocks.py read-c2a.txt read-m2.txt` reports 0 differing blocks of 512.
Head against M2 and c2a2aaa:
- u_flate (untyped /FlateDecode, data deflate(row + 4,000 dashes)) under MaxDecodedStreamLength 106 or 4006: head [limit.decoded-stream @1772, xref.hybrid-stream-lost @1647]; M2 and c2a2aaa [xref.hybrid-stream-lost @1647, limit.decoded-stream @1772].
- u_hexhex ([/ASCIIHexDecode /ASCIIHexDecode]) under MaxFilterChainLength 1: head [limit.filter-chain @1794, xref.hybrid-stream-lost, xref.rebuilt, object.redefined]; M2 [xref.hybrid-stream-lost, limit.filter-chain @1794, xref.rebuilt, object.redefined].
- The typed twins report the guard after xref.hybrid-stream-lost on head too, so they serve as controls: t_flate at 4006 gives [xref.hybrid-stream-lost, limit.decoded-stream @1784].
M2 lies outside ADR 53's catalog. The catalog mutant, which removes the call, is killed (see checked_and_sound).

Fix: Add rows, or a sibling theory, on HybridFiles.PrevNamingAStreamBesideTheOldestTable:
- An untyped stream, filter /FlateDecode, deflated, padding 4000, under MaxDecodedStreamLength 1000. Expect [limit.decoded-stream, xref.hybrid-stream-lost], and limit.decoded-stream once more after GetObject(4).Decode().
- An untyped stream under [/ASCIIHexDecode /ASCIIHexDecode] (needs a hex-encoded data option on the builder) with MaxFilterChainLength 1. Expect [limit.filter-chain, xref.hybrid-stream-lost, xref.rebuilt, object.redefined].
- The typed twins as controls.
The untyped rows fail on c2a2aaa and under M2.

Verdict: The finding holds as stated, and the whole unit suite confirms it, not only the four classes the reviewer ran. The texts promise something no test checks. reader-limits.md:199-206, TryReadXRefStream's check paragraph (PdfFileReader.cs:6318-6319) and threat-model.md row 432 all say that any guard the look's decoding reaches is reported at the look, ahead of the /XRefStm's search report: MaxStreamLength, MaxFilterChainLength and MaxDecodedStreamLength. Head does this. But every row of the theory at GuardCutValueTests.cs:3916 uses MaxStreamLength. With RollBack swapped back in at :6644 (c2a2aaa's line), MaxStreamLength's report is lost. The two decoding guards are still reported once, but later, after xref.hybrid-stream-lost, against the texts, and no test notices.

Why this is not settled or accepted already:
- Round 10's triage ordered only the MaxStreamLength rows. 42ca0bb's message says truthfully that only those rows were added, so no text is false.
- No issue among #320 to #333 holds this gap.
- The catalog mutant, which removes the call, is killed, and the swap lies outside ADR 53's catalog. So this is a test gap, not a survivor.
- The R10a stage proposed citing A_guard_reached_again (cut: null) for the 'ahead of' claim in threat-model row 432, which now cites it. That row cannot tell the order: its place is dropped, the search decodes the stream again and reports the guard first, whatever the look did.

Minor, not major: the guard is reported once either way. Only its documented position goes unchecked. It is not a maintainer's choice.

Verdict evidence: V = C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r11wip/verify-check-decoding-guard-position-untested. I built everything in my own worktree at 79c6a7f (Release, 0 warnings), with the remote corpus hard-linked and removed afterwards. The worktree is clean again.

The probe is V/ZzVcdgTests.cs.txt. Fact ZzVcdg uses its own builder, which mirrors HybridFiles.PrevNamingAStreamBesideTheOldestTable with no cut. Theory ZzVcdgRow uses the committed builder.

Head (V/out-head.txt):
- u_flate (untyped /FlateDecode, deflate(row + 4,000 dashes)) under MaxDecodedStreamLength 1000 and 4006: open [limit.decoded-stream @1772, xref.hybrid-stream-lost @1647]. After GetObject(4).Decode() twice, nothing more from the guard (only xref.rebuilt and object.redefined).
- u_hexhex ([/ASCIIHexDecode /ASCIIHexDecode]) under MaxFilterChainLength 1: [limit.filter-chain @1794, xref.hybrid-stream-lost, xref.rebuilt, object.redefined].
- Typed controls: t_flate gives [xref.hybrid-stream-lost, limit.decoded-stream @1784] and t_hexhex gives [xref.hybrid-stream-lost, limit.filter-chain @1806, ...].

M2, with `_diagnostics.RollBack(decodingChecked);` at PdfFileReader.cs:6644 (V/out-m2.txt, diffed against head):
- Both decoding guards move after xref.hybrid-stream-lost and are still reported once.
- u_raw under MaxStreamLength 50 loses limit.stream-length.
- The typed rows are unchanged.

The whole unit suite under M2 (V/suite-m2.log): total 10,623, failed 3, succeeded 10,617, skipped 3. The three failures are exactly the MaxStreamLength rows ("" "" 50, "" "/Foo" 50, "" "" 5). A_guard_reached_again (cut: null) and every other test pass.

The proposed row on the committed builder: PrevNamingAStreamBesideTheOldestTable("", padding: 4000, filter: "/FlateDecode", deflated: true) under MaxDecodedStreamLength 1000.
- On head it passes (V/row-head.log): codes [limit.decoded-stream, xref.hybrid-stream-lost], Decode().Length 1000, guard count 1.
- Under M2 it fails (V/row-m2.log): 'but {"xref.hybrid-stream-lost", "limit.decoded-stream"} differs at index 0'.
- The typed twin [xref.hybrid-stream-lost, limit.decoded-stream] passes on both.

I did not rebuild c2a2aaa: M2 is its line at that site, on head. I did not check the cut-value path at :6530, which may have the same gap for decoding guards.
fix: Add a sibling theory to GuardCutValueTests, or rows if the theory takes a limits parameter. The MaxDecodedStreamLength rows need no builder change:
- HybridFiles.PrevNamingAStreamBesideTheOldestTable(type, padding: 4000, filter: "/FlateDecode", deflated: true) under MaxDecodedStreamLength 1000.
- Untyped: expect exactly [limit.decoded-stream, xref.hybrid-stream-lost]. GetObject(4).AsStream().Required().Decode() gives 1000 bytes and the guard is still reported once.
- Typed twin as a control: [xref.hybrid-stream-lost, limit.decoded-stream].
- The untyped row fails under the RollBack swap at :6644.

Optionally, add a MaxFilterChainLength 1 row under [/ASCIIHexDecode /ASCIIHexDecode]. It needs a hex-encoding option on the builder and expects [limit.filter-chain, xref.hybrid-stream-lost, xref.rebuilt, object.redefined], typed twin [xref.hybrid-stream-lost, limit.filter-chain, xref.rebuilt, object.redefined].

Then:
- Cite the new theory in threat-model.md row 432 beside or in place of A_guard_reached_again, which cannot tell the order.
- Record M2 (RollBack for RollBackBesideLimits at :6644) as killed by the decoding row as well.
- While there, check whether the cut-value path's RollBackBesideLimits(besideCut) at :6530 has the same gap for decoding guards.
needs_maintainer: False

## 12. threat-model-561-guard-never-counted-left-out-overstated — minor → confirmed minor (diagnostics: daca13c and PdfDi)
Location: docs/threat-model.md:561 (the #144 row, clause added by 220eab4 from R10a's open point)

Claim: The new clause says a guard that a stream decoded again meets is 'counted reached, and never as an entry the capacity left out, whatever a rollback of the reading that met it again keeps'. The code does not give 'never'. (i) Where the capacity left the guard's first report out, nothing holds that report, so each later decoding that meets the guard counts it once more as left out, and the rollback beside the guards keeps each count. This happens through documents. (ii) Even where the document holds the report, a decoding whose own reports reach Capacity before the guard has the Decodings instance drop the guard, and the document then counts it as left out with room to spare. This is shown at unit level only. Both behaviors follow the documented repeat rule: MoveDecodingReportsTo's summary says 'an entry the capacity dropped was counted, and a repeat of it is counted again', and the test A_copy_of_a_report_the_capacity_left_out_is_counted_again_each_time_a_decoding_meets_it pins it. Main does the same. Only the sentence overstates it.

Evidence: Files written by W/r11wip/diagnostics/ZzDiagGen.cs.txt and ZzDiagGen2.cs.txt:
- files/phantom_none.pdf = HybridFiles.PrevNamingAStreamBesideTheOldestTable("", padding: 4000, filter: "[/FlateDecode /Foo]", deflated: true).
- files2/phantomorder_ahxodd_none.pdf = the same layout with [/ASCIIHexDecode /FlateDecode /Foo] and an odd final hex digit.
Opened under MaxDecodedStreamLength 1000. Probe ZzDiagLs.cs.txt reads the private _limitsSuppressed by reflection. Output ls-head.txt / ls2-head.txt on 79c6a7f:
- 'phantom_none cap 0: Count 0 Suppressed 7 guards' reports left out 3 LimitsReached 3 []'. With room, 5 entries at opening, one of them limit.decoded-stream.
- 'phantomorder_ahxodd_none cap 1: Count 1 Suppressed 7 guards' reports left out 3 LimitsReached 3 [xref.offset-adjusted]'.
- Same file at cap 2: 'Count 2 Suppressed 4 guards' reports left out 0 ... [limit.decoded-stream,xref.offset-adjusted]'. With room, 6 entries at opening.
In both files, one guard is reached by three decodings of object 4: the check, the search's candidate, and the /Prev's reading. It is counted three times as an entry the capacity left out, two of them after the check's RollBackBesideLimits.
Unit case (ZzDiagExplicit.cs.txt, case 2e, explicit-head.txt), Capacity 2:
- '2e first decoding target: Count 1 Suppressed 0 Limits 1 [limit.decoded-stream]'
- '2e second decoding target: Count 2 Suppressed 2 Limits 2 [limit.decoded-stream,filter.failed]' (the second decoding reports two faults before the guard)
- '2e after beside target: Count 1 Suppressed 1 Limits 2 [limit.decoded-stream]'
So a guard whose report the document holds is counted as left out with room to spare.
Comparison: main counts in the same way (sweep-main.txt, phantom_none mdsl cap 0: 'C0 S5 L2 []' with 3 entries at full capacity). c2a2aaa's sweep gives L2 instead of L3, because its check rolled the guard back.

Fix: Scope the clause, for example: '— counted reached; where the document holds its report, and the decoding met fewer reports than the capacity before it, not as an entry the capacity left out, whatever a rollback of the reading that met it again keeps; a guard's report the capacity left out is counted left out again by each decoding that meets it, as any report is —'. Optionally cite DiagnosticsTests.A_copy_of_a_report_the_capacity_left_out_is_counted_again_each_time_a_decoding_meets_it beside it; it is already in the row's evidence.

Verdict: The finding holds, and part (ii) goes further than it claimed: it is not unit-level only. I reproduced it through a document on 79c6a7f.

Part (ii) falls inside the clause's own scope: a guard whose report the document already holds, met again. One decoding's reports go to the `Decodings` instance (PdfDiagnostics.cs:246, `Capacity = Capacity`). That instance's `Add` drops its own entries once it holds `Capacity` of them. `MoveDecodingReportsTo` then adds what it dropped to the document's `_suppressed` and `_limitsSuppressed`, held or not (:282-284). So when a decoding reports `Capacity` faults before the guard, the guard's copy never reaches the held-report skip, and `SuppressedCount` counts it as left out.

The route through a document: a caller sets `DiagnosticCapacity` 2. One decoding of the stream sees one fault before the guard and the document keeps the guard's report. A later decoding of the same stream sees two faults before it. After that, every decoding adds 1 to the count of guards' reports left out, while the document still holds that guard's report as moved in.

Part (i), a guard whose first report the capacity dropped, reproduces exactly as stated. But the clause sits after 'adds nothing it holds already' and says the guard is 'still' counted. Read that way, it speaks of a held guard, and (i) falls outside it. R10a's open point gave that scope ('whose report the document holds'), and the docs stage dropped it. The row's evidence column also cites tests that pin (i). Read literally, though, 'never' covers (i) as well.

The code follows rules written on main:
- `Decodings`: 'It holds what one decoding reports, within the same capacity'.
- `MoveDecodingReportsTo`: 'an entry the capacity dropped was counted, and a repeat of it is counted again'.
- `HostileInputTests.A_report_the_capacity_leaves_out_of_one_decoding_is_counted_by_its_document`.

Since main, the batch changed `Add` and `MoveDecodingReportsTo` only to carry `_limitsSuppressed`. `SuppressedCount` on a caller's decoding is therefore main's behavior. What the batch added is the text: threat-model.md:561, from 220eab4. The same overstatement is in the `RollBackBesideLimits` summary that daca13c rewrote (PdfDiagnostics.cs:158-159, 'which the capacity did not leave out').

Severity is minor: the text is misleading and no behavior changed. The case needs a caller to set `DiagnosticCapacity` no higher than the number of reports one decoding makes before the guard, which is 2 here. With the default capacity of 1000 and `MaxFilterChainLength` 64, I see no way to reach it.

Verdict evidence: Probe: W/r11wip/verify-threat-model-561-guard-never-counted-left-out-overstated/ZzV561Probe.cs.txt. Files are in files/, output in probe-head.txt and probe-head2.txt. Run on 79c6a7f (Release, 0 warnings) with `MaxDecodedStreamLength` 1000. The probe reads `_limitsSuppressed` (shown as guardsLeftOut) and the document's `_decodingReports` (shown as heldGuards) by reflection.

**(A) The reviewer's files, rebuilt. Exact match with the finding.**
- A_phantom_none (HybridFiles.PrevNamingAStreamBesideTheOldestTable("", padding: 4000, filter: "[/FlateDecode /Foo]", deflated: true)), cap 0: 'Count 0 Suppressed 7 guardsLeftOut 3 Limits 3 [] heldGuards []'.
- A_ahxodd_none ([/ASCIIHexDecode /FlateDecode /Foo], odd last digit):
  - cap 1: 'Count 1 Suppressed 7 guardsLeftOut 3 Limits 3 [xref.offset-adjusted] heldGuards []'
  - cap 2: 'Count 2 Suppressed 4 guardsLeftOut 0 Limits 3 [limit.decoded-stream,xref.offset-adjusted] heldGuards [limit.decoded-stream@1837x1]'

So in (i) the guard's report is never held.

**(B) The unit case, matching the finding's 2e.**
- 'B first decoding: Count 1 Suppressed 0 guardsLeftOut 0 Limits 1 [limit.decoded-stream] heldGuards [limit.decoded-stream@5x1]'
- 'B second decoding: Count 2 Suppressed 2 guardsLeftOut 1 Limits 2 [limit.decoded-stream,filter.failed]'
- 'B after beside: Count 1 Suppressed 1 guardsLeftOut 1 Limits 2 [limit.decoded-stream] heldGuards [limit.decoded-stream@5x1]'

**(C) Part (ii) through a document, which the finding did not have.**
C_indirect_tail.pdf uses the same hybrid layout as (A):
- Object 4: /Filter [/ASCIIHexDecode /FlateDecode /Foo], /Length 5 0 R, and only the oldest table places object 5. Its data is the hex of the deflated row plus 4,000 dashes, then '\nendstream\nzz qq'.
- The newest table also places object 4, so a caller can decode it after opening.
- The chain reads the data to the first endstream. That decoding reports one fault (no > marker) before the guard, and the document keeps the guard's report.
- Read through the index, the data is 80 bytes. Each such decoding reports two faults before the guard: 'An ASCIIHex stream holds 9 bytes that encode nothing, the first at byte 67 of its 80' (Warning) and 'ends without its > end marker' (Repair).

At `DiagnosticCapacity` 2:
- open: 'Count 2 Suppressed 6 guardsLeftOut 1 Limits 3 [limit.decoded-stream,xref.offset-adjusted] heldGuards [limit.decoded-stream@1840x1]'
- after one Decode() of object 4: 'Suppressed 10 guardsLeftOut 2 Limits 4 ... heldGuards [limit.decoded-stream@1840x1]'
- after a second: 'Suppressed 14 guardsLeftOut 3 Limits 5'

So the document holds the guard's report, and each decoding that meets the guard again counts it once more as left out, with no rollback involved.

At cap 8 there is a single limit.decoded-stream entry ('The /FlateDecode data decodes to more than 1,000 bytes; decoding stopped there.' @1840). The guard's report is therefore the same one in every decoding.

Controls:
- C_indirect_notail (one fault before the guard in every decoding), cap 2: guardsLeftOut 0 → 0 → 0 across the two decodes, guard held.
- C_direct_tail (/Length direct, two faults in every decoding), cap 2: the guard is never held, guardsLeftOut 3 → 4 → 5. That is case (i).

Main 21a0298 was not run. `git diff 21a0298 79c6a7f -- PdfDiagnostics.cs` shows `Add`, `Decodings` and `MoveDecodingReportsTo`'s `_suppressed` delta unchanged apart from the new `_limitsSuppressed` lines. The probe was removed from the worktree, which is clean.
fix: This is a text change. No code change is needed, because the code follows the per-decoding capacity rule on main.

**threat-model.md:561.** Replace '— counted reached, and never as an entry the capacity left out, whatever a rollback of the reading that met it again keeps —' with something like:

'— counted reached; where the document holds its report, the repeat is not counted as an entry the capacity left out, whatever a rollback of the reading that met it again keeps, unless the decoding that met it again made as many reports as the capacity before it: a decoding holds no more than the capacity, and counts what it drops as left out, as any report is. A guard's report the capacity left out is counted left out again by each decoding that meets it —'.

**PdfDiagnostics.cs:158-159** (the `RollBackBesideLimits` summary, from daca13c). Scope 'which the capacity did not leave out' the same way, for example: 'which the capacity did not leave out, unless the decoding's own capacity dropped it (Decodings), when it stays counted left out'.

**Optional test.** Pin the document case in HostileInputTests (or GuardCutValueTests), using C_indirect_tail's layout at `DiagnosticCapacity` 2:
- the guard's report is held;
- each `Decode()` of object 4 adds 1 to `LimitsReached` and 4 to `SuppressedCount`;
- with `DiagnosticCapacity` 8, `SuppressedCount` stays 0.

Cite the new test in the row, beside the existing `A_report_the_capacity_leaves_out_of_one_decoding_is_counted_by_its_document`.

**Alternative, if the maintainer wants the clause's 'never' to be true.** In `Decodings`' full branch, do not count as left out a copy that `KeptIn` holds as moved in. That changes main's documented rule, so it belongs in a debt or a decision, not in this text fix.
needs_maintainer: False

## 13. diagnostics-test-comment-full-once-guard-kept — nit → confirmed nit (diagnostics: daca13c and PdfDi)
Location: tests/AdCodicem.Pdf.Tests/DiagnosticsTests.cs:251

Claim: The comment of Rolling_back_beside_the_guards_of_full_diagnostics_counts_left_out_only_the_guards_reports_the_capacity_dropped says 'the diagnostics are full once the first decoding's guard is kept'. With Capacity 3, the kept guard leaves two places free. It is the two target.Warn("kept", "Fills the diagnostics.") calls (:262-263) that fill them.

Evidence: W/r11wip/diagnostics/ZzDiagNit.cs.txt replays the test's setup. Output nit-head.txt on 79c6a7f: 'after the guard is kept: Count 1 of Capacity 3, full False' / 'after the two fillers: Count 3 of Capacity 3, full True'.

Fix: 'As above, but the diagnostics are filled once the first decoding's guard is kept: two entries take the room it leaves.'

Verdict: This holds as a wording nit. The test is new in daca13c (round 10's fixes) and no earlier round reviewed it. Its comment at tests/AdCodicem.Pdf.Tests/DiagnosticsTests.cs:251 says "the diagnostics are full once the first decoding's guard is kept". That makes keeping the guard the event that fills the diagnostics. With Capacity = 3, the kept guard takes one place and leaves two free. The two `target.Warn("kept", "Fills the diagnostics.")` calls at :262-263 are what fill them, and the test depends on them. Without the two calls the diagnostics are not full when the second decoding runs. Nothing is then dropped, and the test's assertions (SuppressedCount 2, then 1) would fail. The comment never mentions the two filler entries. Only their message text hints at them. The code is correct and the test holds what its name says, so this is wording only, not a test that misses its claim.

Verdict evidence: Rebuilt on origin/ccr-6d6c0ee6-dqm4lm-188 at 79c6a7f. The probe is W/r11wip/verify-diagnostics-test-comment-full-once-guard-kept/ZzVerifyFullOnce.cs.txt, a theory that replays the test's setup with 2 fillers and with 0. Output, W/r11wip/verify-diagnostics-test-comment-full-once-guard-kept/out-head.txt:
fillers 2: after the guard is kept: Count 1/3, full False
fillers 2: after the fillers: Count 3/3, full True
fillers 2: second decoding moved in: Count 3, suppressed 2
fillers 2: rolled back beside: [The decoding's guard. | Fills the diagnostics. | Fills the diagnostics.], limits 3, suppressed 1
fillers 0: after the guard is kept: Count 1/3, full False
fillers 0: after the fillers: Count 1/3, full False
fillers 0: second decoding moved in: Count 3, suppressed 0
fillers 0: rolled back beside: [The decoding's guard. | Another guard.], limits 3, suppressed 0
The test itself, Rolling_back_beside_the_guards_of_full_diagnostics_counts_left_out_only_the_guards_reports_the_capacity_dropped, passes in the same run (3 of 3 passed). This matches the reviewer's nit-head.txt. W = C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud. The probe was removed from the worktree afterward, and the worktree is clean.
fix: Rewrite the first sentence of the comment at DiagnosticsTests.cs:251 to name the filler entries. Either the reviewer's wording, "As above, but the diagnostics are filled once the first decoding's guard is kept: two entries take the room it leaves.", or "As above, but after the first decoding's guard is kept, two entries fill the diagnostics." The rest of the comment and the code stay as they are. This is a comment-only change, so it needs no mutation run.
needs_maintainer: False

## 14. sign-run-escapes-number-end — minor → confirmed minor (own-stream (2be5c44, 5395eed, )
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:5355-5370 (NumberRunsInto, NumberEnd), remark :5336-5345

Claim: NumberEnd skips only the header's first byte before it scans the digits, and NumberRunsInto's sign arm maps only `candidate == streamStart + 1` after a single '+'. PdfNumberParser (PdfNumberParser.cs:63-72) reads a run of signs, though. So the xref-stream reading, a load and the comment-path header all read '++13 0 obj' and '+-13 0 obj' as object 13. For such a header NumberEnd stops at the second sign, and both rules that round 10 fixed for a single sign fail again. (i) A passed-over comment-parted typed header '++11 0 %c\nobj' or '+-11 0 %c\nobj' that an older section reads at own+1 to own+3 is read by the chain and still reported as passed over. If it is unreadable, it is reported unreadable and passed over both. This is round 10's [3]/[8]/[17] defect. (ii) S written '++13 0 %c\nobj' and read by the /XRefStm at S+1 to S+3 is found by the comment path at its first sign and taken as a stream before S, which is [20]'s comment-path direction and worse than main. (iii) S written '++13 0 obj' or '+++13 0 obj' and named at its first sign: the walk's header, 2 or 3 bytes past, is tried as another stream and taken where S's /Prev names another offset. The stream is read twice and the naming goes unreported, as on main.

Evidence: ZzR11Signs (out/head-signs.txt vs out/main-signs.txt). (i) r=++11 0 %c\nobj link=/XRefStm into=1 own=1828 on 79c6a7f: `S[... /XRefStm:Read:Stream@1829+0 ]` `D[R:xref.offset-adjusted@1638 W:xref.hybrid-stream-lost@1638 W:xref.stream-passed-over@1828]`. into=2 gives the same with Read@1830, and so does /Prev at into 1 to 3. The single-sign twin r=+11 0 %c\nobj link=/XRefStm into=1 gives no passed-over Warning. Generator: tools/genwarn.py finds 16 blocks on head where the Warning stands beside a Read or Malformed record inside the header's first token, all with '++11' or '+-11' headers. For example, g343 default (place '+-11 0 %c\nobj', unreadable, oldest /XRefStm at placeAt+3) gives `W:section-unreadable@+204 W:stream-passed-over@+201`. (ii) h=++13 0 %c\nobj prev=nothing into=1, head: `S[startxref:Read:Table@1152+0 /XRefStm:Read:Stream@419+0 /Prev:Relocated:Stream@418+0 ]` `D[R:xref.offset-adjusted@418 R:xref.rebuilt@-1]` rep=True `F[E:file.root-invalid ...]`. main: `S[... /XRefStm:Read:Stream@419+0 /Prev:Relocated:Table@229+0 ]` `D[R:xref.offset-adjusted@229]` rep=False. With prev=oldest, head reads S twice (`/XRefStm:Read:Stream@419 /Prev:Relocated:Stream@418`) with no hybrid-stream-lost and no Error. With prev=table, head gives `W:chain-cycle@-7 R:rebuilt`, `E:xref.chain-loop`. '+-13 0 %c\nobj' at into 2 and 3 behaves the same. (iii) h=++13 0 obj prev=oldest into=0: `S[... /XRefStm:Read:Stream@418+0 /Prev:Relocated:Stream@420+0 /Prev:Read:Table@229+0 ]` `D[R:xref.offset-adjusted@420]`, no lost, no Error. '+++13 0 obj' into 0 and 1 reads S twice the same way. The unsigned and single-sign twins give NamedAgain, hybrid-stream-lost and Error hybrid-stream-wrong.

Fix: In NumberEnd, skip the run of signs past the first byte before the digit scan, as PdfNumberParser reads it. In NumberRunsInto, map a candidate that starts where a run of signs beginning with '+' at streamStart ends. A reading at '-' is refused already. Rows: S '++13 0 %c\nobj' at into 1 to 3 with S's /Prev nothing, the oldest table and the table's; S '++13 0 obj' at into 0 and '+++13 0 obj' at into 0 and 1 with /Prev the oldest; a passed-over '++11 0 %c\nobj' and '+-11 0 %c\nobj' named by an older /XRefStm or /Prev at own+1 to own+3, giving no Warning, and when unreadable a single section-unreadable. Bring the texts in line: the NumberRunsInto remark ('one byte past the sign'), the NumberEnd summary, lazy-reading.md:200-203 and threat-model 432 ('the + the number is written with' should become a run of signs). Alternatively the maintainer decides that more than one sign makes no header for any reader of headers, with #327's closing, and this is filed there.

Verdict: The claim holds, and all three directions reproduce exactly. PdfNumberParser.TryParse (PdfNumberParser.cs:63-72) skips a whole run of signs, and the first sign decides. So the xref-stream reading, a load and the comment path's header (#261) all read '++13 0 obj' and '+-13 0 obj' as object 13. NumberEnd (PdfFileReader.cs:5369-5370) skips only the header's first byte before its digit scan, so for '++13' it stops at the second sign. NumberRunsInto's sign arm (:5359) maps only candidate == streamStart + 1.

(i) A passed-over, comment-parted typed header with a run of signs ('++11', '+-11', '+++11' followed by ' 0 %c\nobj') is read by an older /XRefStm or /Prev at a byte inside its number. The chain reads it, and it is still reported xref.stream-passed-over. When it is unreadable it also gets section-unreadable, so it is reported both ways. This is round 10's [3]/[8]/[17] defect, which 2be5c44 fixed only for zero or one sign. The Warning exists only on the branch.

(ii) S written '++13 0 %c\nobj', '+-13 0 %c\nobj' or '+++13 0 %c\nobj' and named inside its number is taken by the comment path at its first sign as a stream before itself. This is [20]'s comment-path direction, and it is worse than main. Main never finds that header, so it reads these files whole with no rebuild.

(iii) The walk's direction with a run (S named at its first sign, or at any sign before the last one) is the same as main: S is read twice and nothing is reported. The finding says so already. The single-sign and unsigned twins now get NamedAgain, xref.hybrid-stream-lost and Error hybrid-stream-wrong.

It is not refuted as known. #327 holds only how the scan and the searches read a signed header. It does not hold the told-by-its-data rule (R9-Q1 and round 10's [20]) or the passed-over report. Both are this batch's, and the batch made (i) and (ii) worse than main.

The texts claim more than the code does:
- lazy-reading.md:199-203: 'told by its data, whatever byte of its header the /XRefStm names'.
- threat-model.md:432, cell 1: 'told by its data rather than by the byte its reading started at'.
- The remarks at :3721-3723 and :5345-5349.

Why minor, as round 10 rated [20]: ISO 32000 7.3.3 allows one sign, so a run of signs in an object header is non-conforming and exotic. Every object is still served in every row. The cost is a rebuild with a false validator Error, or a false or extra Warning.

Two side notes. R10b's ADR 53 record calls E02 (at+2 in NumberEnd) equivalent because 'it moves the end onto the white space or comment after the number'. That reason is false for a run of signs: '++11' read at own+1 to own+3 tells E02 apart. And no maintainer question is needed: the fix applies R9-Q1 and round 10's [20] decision to a form the reader already accepts everywhere else.

Verdict evidence: Probes are in C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r11wip/verify-sign-run-escapes-number-end/:
- ZzR11OwnTests.cs: the reviewer's builders, copied.
- ZzVerifySignRunTests.cs: my rows.
- Outputs in out/: head-signs.txt and head-signrun.txt (79c6a7f, built in my worktree); main-signs.txt and main-signrun.txt (21a0298, built in a temporary worktree, since removed).
- head-signs.txt and main-signs.txt are byte-identical to the reviewer's r11wip/own-stream/out files.

(i) Branch, readable stream:
- r='++11 0 %c\nobj', link=/XRefStm, into=1, own=1828: `S[... /XRefStm:Read:Stream@1829+0 ]` `D[R:xref.offset-adjusted@1638 W:xref.hybrid-stream-lost@1638 W:xref.stream-passed-over@1828]`.
- into=2 and 3 give the same, with Read@1830 and Read@1831. /Prev at into 1 to 3 gives the same, with Read@1826 to Read@1828.
- '+-11' at into 2 and 3, and '+++11' at into 1 to 4, give the same.
- Twins with no Warning: '+11 0 %c\nobj' at into 1 and 2, and '11 0 %c\nobj' at into 1.

(i) Branch, unreadable stream (/W [1 -4 2]):
- '++11', link=/XRefStm, into=1: `/XRefStm:Malformed:Stream@1829` `W:xref.section-unreadable@1829 W:xref.stream-passed-over@1828`.
- '+11' at into 1 gives only `W:xref.section-unreadable@1829`.
- Main has no passed-over Warning at all.

(ii) S written '++13 0 %c\nobj', branch against main:
- prev=nothing, into 1 to 3. Branch: `S[startxref:Read:Table@1152 /XRefStm:Read:Stream@419 /Prev:Relocated:Stream@418]` `D[R:xref.offset-adjusted@418 R:xref.rebuilt@-1]` rep=True `F[E:file.root-invalid W:xref.section-shifted]`. Main: `/Prev:Relocated:Table@229` `D[R:xref.offset-adjusted@229]` rep=False `F[W:xref.section-shifted]`.
- prev=table, into 1 to 3. Branch: `D[R:xref.offset-adjusted@418 W:xref.chain-cycle@411 R:xref.rebuilt@-1]` `F[W:xref.section-shifted E:xref.chain-loop]`. Main reads it whole.
- prev=oldest, into 1 to 3. Branch: `/XRefStm:Read:Stream@419 /Prev:Relocated:Stream@418 /Prev:Read:Table@229`, with no hybrid-stream-lost. Main: `/Prev:Relocated:Table@229`.
- '+-13' at into 2 and 3, and '+++13' at into 1 to 4, behave the same. The single-sign twin '+13 0 %c\nobj' reads like main.

(iii):
- '++13 0 obj', prev=oldest, into=0: `/XRefStm:Read:Stream@418 /Prev:Relocated:Stream@420 /Prev:Read:Table@229` `D[R:xref.offset-adjusted@420]`, the same on main.
- '+++13 0 obj' at into 0 and 1 is the same.
- '13 0 obj' at into 0 gives `NamedAgain@418`, `W:xref.hybrid-stream-lost@418` and `E:xref.hybrid-stream-wrong`.

Prototype fix, in prototype-fix.diff, applied in my worktree and since reverted:
- NumberEnd skips the run of signs after the first byte, then scans the digits.
- The sign arm maps a candidate past streamStart when the byte at streamStart is '+' and every byte of [streamStart, candidate) is a sign, guarded by streamStart >= spanStart.
- 67 of the 274 signrun rows change, and only these: every (i) row loses the passed-over Warning and keeps a single section-unreadable; every (ii) row reads like main (prev nothing or table) or like the single-sign twin (prev oldest: NamedAgain, hybrid-stream-lost, Error hybrid-stream-wrong); every (iii) row reads like the twin.
- AdCodicem.Pdf.Tests with the prototype: total 7939, failed 0, skipped 20 (remote corpus not linked; log suite-fix.log). No existing test pins the old behavior.
fix: Fix it in this batch, as an application of R9-Q1 and round 10's [20], not as a new decision.

1. NumberEnd: skip the run of '+' and '-' after the header's first byte, then scan the digits, the way PdfNumberParser reads a number.
2. NumberRunsInto's second arm: map a candidate that starts past streamStart when:
   - the byte at streamStart is '+' (a reading at '-' is refused already);
   - every byte of [streamStart, candidate) is a sign;
   - streamStart >= spanStart, because the window holds only the bytes searched.
   The prototype is in r11wip/verify-sign-run-escapes-number-end/prototype-fix.diff, and the unit suite passes with it.

Rows:
- S written '++13 0 %c\nobj' at into 1 to 3, with S's /Prev naming nothing, the oldest table, or the table's offset. Expect: as main, as main, and NamedAgain + xref.hybrid-stream-lost + Error hybrid-stream-wrong.
- '+-13 0 %c\nobj' at into 2 and 3.
- '++13 0 obj' at into 0, and '+++13 0 obj' at into 0 and 1, with S's /Prev the oldest table. Expect NamedAgain, hybrid-stream-lost and the Error.
- A passed-over '++11 0 %c\nobj' or '+-11 0 %c\nobj' named by an older /XRefStm or /Prev at a byte inside its number. Expect no xref.stream-passed-over, and a single section-unreadable when it is unreadable.

Texts: say 'the run of signs the number is written with' in place of 'the +' in:
- the NumberRunsInto summary and remark: 'one byte past the sign', and 'one that starts one byte past ... the window holding the byte before its first';
- the NumberEnd summary;
- the comments at :5161-5166 and :3721-3723;
- lazy-reading.md:199-203;
- threat-model.md:432 (both cells) and :434;
- diagnostics.md:115;
- validation-rules.md:60.

Records: the fix's ADR 53 record re-judges R10b's E02, whose 'white space after the number' reason fails for '++11'.

Alternative, only if the maintainer prefers it: treat more than one sign as making no header for the comment path and the told rule. That means refusing a run of signs in TryReadHeaderBackwards's lexed header, which restores main for (ii) only. It would be filed under #327 with its closing widened, and the texts would say a header carries at most one sign. (i) and (iii) would then stay as they are, so this choice is weaker.
needs_maintainer: False

## 15. ws-flag-withholds-unhidden-warning — minor → narrowed minor (own-stream (2be5c44, 5395eed, )
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3755 and :5562; src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:123-130; docs/website/docs/reference/diagnostics.md:116; docs/website/docs/concepts/lazy-reading.md:219-224

Claim: Open point (a). _whiteSpacePastGuard is set wherever the white-space guard is met at a chain-named offset, whether or not the guard left anything unread, and it withholds every passed-over report in the document. Where the search near the guarded offset finds the section past the white space and reads it whole, that search passes over nothing and hides no link. Even then, a Warning that another search made earlier is lost; it is the file's own and is made at default limits. ChainStoppedByGuard is set only where a limit left part of the chain unread, so the analogy the fix stage drew (and the #289 comment draws) does not hold. The texts state the rule document-wide, but give a reason that is false for this shape: 'the limit's report stands for what it left unread, or for what that search passed over or did not find'. That search left nothing unread, passed over nothing and found the section. Invariant 5 loses a report here that ADR 34 does not make the guard's.

Evidence: ZzR11Ws (out/head-ws.txt). File Ws(400, oldestNamesOwn false): the newest /Prev falls in a comment near stream 12 at 362, the search passes it over and takes the table at 802, whose /Prev names 400 spaces at 943 before the oldest table at 1343, which names nothing. Default: `S[... /Prev:Relocated:Table@802+0 /Prev:Read:Table@943+400 ]` `D[R:xref.offset-adjusted@802 W:xref.stream-passed-over@362]`. MaxTrailerLength 256 on 79c6a7f: `S[... /Prev:Relocated:Table@802+0 /Prev:Relocated:Table@1343+0 ]` `D[R:xref.offset-adjusted@802 W:limit.trailer@943 R:xref.offset-adjusted@1343]`, rep=False: the oldest table is found 400 bytes on and read whole, and the Warning is gone. On c2a2aaa the same run kept `W:xref.stream-passed-over@362`. Runs of 300 and 470 give the same result. The /XRefStm variant WsAtXRefStm(300) and (400) behaves the same: at 256 the stream-12 Warning that default makes is withheld. Generator (tools/wsgen.py on out/head-gen.txt): 27 of 600 files make the Warning at default and lose it under 256, where limit.trailer is reported at the run, no section is missing and nothing is rebuilt. Example g492: `default ... W:stream-passed-over@+166`, `trailer256 ... W:limit.trailer@-1370 R:offset-adjusted@-1070`.

Fix: This is the maintainer's choice between two options. (1) Scope the flag. Do not record passed-over streams in the search near a white-space-guarded offset, since they are the guard's artefact. Withhold the other records only where that section ended without a section read past the white space: missing, unreadable, or a place taken before the white space ends. (2) Accept the document-wide reach, as for ChainStoppedByGuard. Then state it honestly in PdfDiagnosticCodes.XRefStreamPassedOver, the ReportStreamsPassedOver summary and remark, the _whiteSpacePastGuard doc, diagnostics.md:116 and lazy-reading.md:219-224: the flag withholds every passed-over report of the document, including one another search made where the guard hid nothing. Also add this shape as a row to the #289 comment's 'What closing this issue should keep': PassingOverAStreamAnOlderTableNamesPastWhiteSpace('the oldest table', 400, false) at 256, where the Warning is the file's. Option (2) is the smaller change, and #289 reworks the guard anyway.

Verdict: The finding reproduces on 79c6a7f, and the flag is what causes it. In the shape, the search near the guarded offset finds the oldest table past the white space and reads it whole. The chain therefore reads the same three tables a default read does, and ChainStoppedByGuard stays false. Even so, xref.stream-passed-over at stream 12 is withheld. That Warning comes from a different search: the one near the newest table's /Prev. The guard could not have hidden it, and the reader knows this, because it read the section past the run.

I narrow it in three ways.

(1) It needs a caller to lower MaxTrailerLength below NearbySearchRadius (512, PdfFileReader.cs:85). Only then can the guarded search reach the section past a run the guard cut. At the default 64 KiB the search never gets past the run, so the named section stays unread: missing, or another place taken. Withholding the Warning across the whole document is then the case the texts mean by 'may hide the link that names the stream'. The #289 comment already shows that case, as shape 2's unnamed variant.

(2) The texts state the rule itself correctly and for the whole document: 'Nothing is reported ... nor where white space and comments ... run past MaxTrailerLength'. What they get wrong is the reason for this shape. The reason given is 'the limit's report stands for what it left unread, or for what that search passed over or did not find', and the summary adds 'a cut that leaves nothing unread ... leaves the report made'. Here the guard left nothing unread, and the guarded search passed over nothing and found the section, yet the report is not made. The _whiteSpacePastGuard doc says the same ('the guard's report stands for what the search passed over or left unread').

(3) The #289 comment's analogy holds as to reach ('Like the chain's own guard flag, it withholds every passed-over report of the document'). It does not hold as to justification: ChainStoppedByGuard is set only where something is left unread (sections, rows, a trailer, a cut value, index entries).

This is not known or accepted: decisions.md leaves it as open point (a). The behavior is new to batch 19: c2a2aaa made the Warning, and main has no such code. Severity is minor. The text misleads, and a true Warning is lost only under a caller-lowered guard whose limit.trailer is reported and whose message names the property to raise. No object is served wrong, and nothing is blamed on the file.

Verdict evidence: Probe: C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r11wip/verify-ws-flag-withholds-unhidden-warning/ZzR11VerWsUnhidden.cs.txt. It uses the repository's own builder, HybridFiles.PassingOverAStreamAnOlderTableNamesPastWhiteSpace, plus a copy of the reviewer's WsAtXRefStm. It was run in a Release build of origin/ccr-6d6c0ee6-dqm4lm-188 at 79c6a7f. Output: out/head-ws.txt and out/head-xrefstm.txt in that folder.

('the oldest table', 400, oldestNamesOwn false), default limits:
- `S[startxref:Read:Table@1419+0 /XRefStm:Read:Stream@247+0 /Prev:Relocated:Table@802+0 /Prev:Read:Table@943+400 ]`
- `D[R:xref.offset-adjusted@802 W:xref.stream-passed-over@362] stopped=False o5="five"`

The same file, MaxTrailerLength 256:
- `S[... /Prev:Relocated:Table@802+0 /Prev:Relocated:Table@1343+0 ]`
- `D[R:xref.offset-adjusted@802 W:limit.trailer@943 R:xref.offset-adjusted@1343] stopped=False o5="five" rep=False`
- Messages: 'White space and comments run past 256 bytes where a cross-reference section is named; the section was not read from there. Raise PdfReaderLimits.MaxTrailerLength to read past it.' and 'The cross-reference section /Prev names at offset 943 was found 400 bytes after it.'

Every row where the Warning is lost while the section is found past the run and read whole: run 300 at 256; runs 400, 470 and 505 at 256 and at 300. Every one has run > MaxTrailerLength and run < 512. With MaxTrailerLength 511 or 512 and run 505 or less, the table is read where named, and the Warning is made. With a run of 600 or 1000 and a limit of 512 or below, and with a run of 70,000 at the default limits, the guarded section ends `/Prev:NotFound:Unknown@943`, `W:limit.trailer@943 W:xref.section-missing@943`. The oldest table is then unread, and in the oldestNamesOwn true rows it holds the /XRefStm that names stream 12: this is the 'may hide' case.

Causality: a no-flag mutant (line 3755 changed to `ChainStoppedByGuard || (_whiteSpacePastGuard && _xref.Count < 0)`; output in out/mutnoflag-ws.txt) puts `W:xref.stream-passed-over@362` back in each of those rows, and leaves everything else identical. The reviewer's run on c2a2aaa (own-stream/out/c2a-ws.txt:176) shows the same.

The reviewer's /XRefStm variant: WsAtXRefStm(400) under 256 gives `D[W:limit.trailer@1573 R:xref.offset-adjusted@1973 R:xref.offset-adjusted@1973 W:xref.hybrid-stream-lost@1573]`, while the default read and the read with MaxTrailerLength unbounded add `W:xref.stream-passed-over@2098`. WsAtXRefStm(70000) at the default limits does not lose a Warning the guard could not have hidden. The /XRefStm ends NotFound, and the /Prev's search takes stream 12 itself (`/Prev:Relocated:Stream@71698`, then section-missing and a rebuild): #289's known class, with no passed-over record.

Texts at 79c6a7f that give the reason this shape contradicts:
- PdfDiagnosticCodes.cs:122-130
- PdfFileReader.cs:658-666, the _whiteSpacePastGuard doc
- PdfFileReader.cs:3710-3714, the ReportStreamsPassedOver summary
- PdfFileReader.cs:3749-3753, the comment
- diagnostics.md:116
- lazy-reading.md:218-224

The existing theory `..._keeps_what_an_older_link_names_unread` (CrossReferenceChainTests.cs:6697) has no ('the oldest table', false, 400, 256) row. Its expected list for a cut run under 512 would pin the current withholding.

I did not rerun the reviewer's 600-file generator.
fix: Recommended: option (2) of the finding, a text change that the round 10 rule already covers, so it needs no maintainer question. The precise statement to make: where MaxTrailerLength is below the 512 bytes the search near an offset reaches, that search can find the section past the white space and read it whole. The guard then hides nothing, and every passed-over report of the document is withheld all the same, including one another search made, which the file makes read whole.

Make this one statement in these places, each sentence written once:
- PdfDiagnosticCodes.XRefStreamPassedOver
- the _whiteSpacePastGuard doc
- the ReportStreamsPassedOver summary and remark, and the comment at :3749-3753
- diagnostics.md:116
- lazy-reading.md:218-224

Each place should qualify 'the limit's report stands for what it left unread, or for what that search passed over or did not find' and 'a cut that leaves nothing unread leaves the report made' accordingly.

Add the row ('the oldest table', false, 400, 256) to CrossReferenceChainTests' `..._keeps_what_an_older_link_names_unread` theory, with a comment saying the Warning lost there is the file's and the flag's reach withholds it.

Track it on #289 with one line in its closing list. If the section becomes the guard's, this row loses the Warning for a reason: the oldest table is unread. If white space stops counting toward the bound, the Warning comes back.

Option (1) would scope the flag. Set it only where the guarded section is not read past the run (missing, or a place taken before the run ends), and do not record what the guarded search itself passes over. That follows ADR 34's 'reported as a whole read reports it' and round 9's 'may hide a link' rule more closely. But it needs code that checks where the run ends against the found section, new tests and mutants, and both of #289's planned fixes remove the flag anyway. It is not worth doing now, unless the maintainer wants the default-limit-equivalent behavior under a lowered MaxTrailerLength.
needs_maintainer: False

## 16. signed-header-never-declares-itself — minor → narrowed nit (own-stream (2be5c44, 5395eed, )
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:5285 (record site line test) and :5259 (the take's line test); PdfDiagnosticCodes.cs:121-122; docs/website/docs/reference/diagnostics.md:116; docs/website/docs/concepts/lazy-reading.md:213-214

Claim: Open point (b). A typed stream whose header is '+12 0 obj' is read by a load: the xref-stream reading at the '+' reads it. The walk finds the header at its first digit, though, where no token starts. As a result it is never recorded as passed over: readable or not, with nothing naming it, no Warning is made. Unreadable, it is never taken as declaring itself either: a link that misses it gives section-missing rather than section-unreadable. The same header parted by a comment ('+11 0 %c\nobj') is found at the sign and reported. The texts say only that the header must be 'a token a load reads from the start of its line', which a signed header is, and name no exception. The cause is #327 (the walk reads no sign). This round did not change the behaviour, but the texts misstate it.

Evidence: ZzR11Reached (out/head-reached.txt), nothing naming the stream. h=+12 0 obj link=nothing own=1808: `D[R:xref.offset-adjusted@1618 W:xref.hybrid-stream-lost@1618]`, with no passed-over Warning. h=+11 0 %c\nobj link=nothing: `D[... W:xref.stream-passed-over@1808]`. h=11 0 obj link=nothing: `W:xref.stream-passed-over@1808`. A load reads the signed header: h=+12 0 obj link=/XRefStm into=0 gives `/XRefStm:Read:Stream@1828+0`. Unreadable and named 2 bytes short: h=+12 0 obj link=/Prev w=[1 -4 2] into=-2 gives `/Prev:NotFound:Unknown@1823+0`, `W:xref.section-missing@1823`. h=12 0 obj, same row, gives `/Prev:Malformed:Stream@1825+0`, `R:xref.offset-adjusted@1825 W:xref.section-unreadable@1825`.

Fix: File it: comment on #327 that its fix, reading the sign as the lexer does, also makes the passed-over report and the take of an unreadable typed stream see a signed header. It also leaves NumberRunsInto's one-byte sign arm dead. Give the reproducer rows above. In this batch, add a clause to the four texts: a header whose number is written with a sign, which the search finds at its first digit, declares nothing (#327).

Verdict: The behavior reproduces exactly on 79c6a7f, but it is #327's and is no worse than main. The finding's main claim, that the texts name no exception, is mostly wrong.

(1) Known debt. #327's body names FindSectionCandidates: the walk never reads the byte before a header's number. Its closing condition requires the section search to read a header's sign as the lexer does, so that `+4 0 obj` is object 4's header. That one fix makes a signed typed header declare itself both in the take of an unreadable section and in the passed-over report.

(2) Not worse than main. Main 21a0298 treats the signed and unsigned headers alike in every row I built. It has no passed-over report at all, and it never takes an unreadable typed stream. Batch 19 improved the unsigned case only. In the /XRefStm row named 2 bytes short, head is better than main for the signed header too: main loses object 9 silently, head reports section-missing and finds 9 through the rebuild.

(3) The texts do state the exception, in ISO 32000-1 7.2.2 terms, where '+' is a regular character:
- The NumberRunsInto remark (PdfFileReader.cs:5340-5344) says the walk 'does not look at that byte — a sign, the end of a keyword such as endobj, any regular byte —: the header +17 0 obj starts at its digit 1'.
- The IsTokenOnItsLine summary (:5377) and the take's comments (:4995, :5245) say 'nor digits a regular byte runs into' declares nothing.
- diagnostics.md:110, validation-rules.md:56, threat-model.md:434 and lazy-reading.md:244 say the same of the take.
- lazy-reading.md:200 says that where a number is written with '+', the header is found at the digit past it.
- The passed-over texts (diagnostics.md:116, lazy-reading.md:214, threat-model.md:432, the comment at :5262-5264) apply the take's rule: 'as a place a fault of the file keeps from being read is taken only where it does'.

What survives is wording. No text says in plain words that a sign is such a regular byte. The pages call it 'the + the number is written with', which makes it part of the number. And the XML summary of PdfDiagnosticCodes.XRefStreamPassedOver (:121-123) states the test without pointing to the take's rule; 'its header is a token a load reads from the start of its line' reads as if a signed header qualified. Neither the code nor R9-Q2 is wrong: R9-Q2's rule (a token on its line) is applied, through the walk, as decided.

Verdict evidence: My own builder, independent of the reviewer's: a first revision's classic table at 226, then a hybrid revision. Its /XRefStm reads S, object 6, typed, with /Prev 226. Past S comes a typed stream 'own', indexing object 9 alone, written with the header given. The newest table's /Prev points 5 bytes short of own. An optional link from the oldest trailer names own+into. Probe: W/r11wip/verify-signed-header-never-declares-itself/ZzR11SignedDeclTests.cs. Outputs: head-out.txt (79c6a7f), main-out.txt (21a0298), head-signed-nothing.pdf.

On 79c6a7f, with nothing naming own:
- `21 0 obj`: D[R:xref.offset-adjusted@1519 W:xref.hybrid-stream-lost@1519 W:xref.stream-passed-over@1698 R:xref.rebuilt@-1]
- `+21 0 obj`: D[R:xref.offset-adjusted@1519 W:xref.hybrid-stream-lost@1519 R:xref.rebuilt@-1], with no passed-over Warning.
- `+21 0 %c\nobj`: W:xref.stream-passed-over@1698, found at the sign.
- `x21 0 obj`, the control: no Warning, like the signed header.

A load reads the signed header: with link /XRefStm into=0 it gives /XRefStm:Read:Stream@1717+0.

Unreadable (/W [1 -4 2]), link /Prev into=-2:
- `+21 0 obj`: /Prev:NotFound:Unknown@1712+0, W:xref.section-missing@1712.
- `21 0 obj`: /Prev:Malformed:Stream@1714+0, R:xref.offset-adjusted@1714, W:xref.section-unreadable@1714.
- `+21 0 %c\nobj`: like the unsigned header, taken at the sign.

The same holds with /XRefStm into=-2: signed gives section-missing@1715, unsigned gives offset-adjusted plus section-unreadable@1717.

On main 21a0298, signed and unsigned are alike:
- Nothing naming own: own taken, /Prev:Relocated:Stream@1698 (unsigned) or @1699 (signed), then R:xref.rebuilt.
- /Prev into=-2: both give R:xref.offset-adjusted@1535 x2, W:xref.chain-cycle@226, R:xref.rebuilt.
- /XRefStm into=-2: both give R:xref.offset-adjusted@1538 x2 and object 9 null, with nothing else reported.

The reviewer's rows (own-stream/out/head-reached.txt:781-783 against :361-363) match.
fix: File it under #327 instead of fixing the code here. Comment on #327 with the rows above:
- The take of an unreadable typed section (#281) and the xref.stream-passed-over report (R9-Q2) both follow the walk. A plain `+N 0 obj` therefore declares nothing: no passed-over Warning, and section-missing rather than section-unreadable when a link falls short. A comment-parted `+N 0 %c\nobj` is found at its sign and declares itself.
- #327's fix, reading the sign as the lexer does, makes both see a signed header.
- That fix leaves NumberRunsInto's one-byte '+' arm (PdfFileReader.cs:5359) dead, to remove or re-judge then. Its rows in HostileInputTests/HybridFiles would then have to be read again.

Optional text change in this batch (nit):
- In the PdfDiagnosticCodes.XRefStreamPassedOver summary (:121-123), after 'not in a comment or a string there', add: 'nor a header whose number is written with a sign, which the search finds at its first digit (#327)'.
- Where the pages say 'nor digits a regular byte (character) runs into', add 'a sign among them (#327)': diagnostics.md:110, validation-rules.md:56, threat-model.md:434, lazy-reading.md:244.

The maintainer has nothing to decide: R9-Q2 is applied as decided, and whether a sign counts is already settled by #327's closing condition (batch 21).
needs_maintainer: False

## 17. adr53-equivalents-told-apart-and-survivors — minor → narrowed minor (own-stream (2be5c44, 5395eed, )
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:5288 (record site), :5358, :5370 (NumberEnd), :3800 (StartsWithin); R10b's ADR 53 record (r10-fix-result.json)

Claim: (a) Three of R10b's six equivalents are told apart by inputs a test can build. B05 (`position <= to`), S09 (`>=`) and E02 (`at + 2`) were all justified by the claim that the byte at NumberEnd is white space or a comment, where no section and no reading starts. That is false where a second sign follows the header's first byte. B08, S05 and S17 stand: no probe of mine tells them apart. (b) Four first-order mutants on lines 2be5c44 changed survive the whole AdCodicem.Pdf.Tests run. N1 and N2 remove an end of the digit set in NumberEnd (`IndexOfAnyExceptInRange('0','8')` and `('1','9')`); no test names inside a header number holding a 0 or a 9 past its first digit. R1 and R2 shift a bound by one in the record site's NumberEnd call (`passed - 1` and `start - lead + 1`, round 10's [18] class); no test records a signed passed-over header. R3 (`passed + 1`) is equivalent except for a run of signs.

Evidence: Mutants applied by tools/mutate.py in their own worktrees at 79c6a7f; summaries in mut-out/summary.txt, mut-out-b/summary.txt and mut-out-d. Whole test project: R1, R2, N1, N2 and R3 all give `total: 7937, failed: 0`. Probe differences against head: B05 on r=++11 0 %c\nobj link=/XRefStm into=1: head `W:xref.stream-passed-over@+190`, mutant none. S09 on h=++13 0 %c\nobj prev=nothing into=1: head `/Pre:Relo:Str@+0 | R:offset-adjusted@+0 R:rebuilt@-419 ... rep | E:file.root-invalid`, mutant `/Pre:Relo:Tab@-189 | R:offset-adjusted@-189`. E02 differs on the same two rows. N1 on r=195 0 obj link=/XRefStm into=1 and 2, and N2 on r=105 0 obj into=1 and 2: head has no Warning, the mutant adds `W:stream-passed-over@+190`. R1 on r=+105 0 %c\nobj link=/XRefStm into=0 (named at its header) and R2 at into=1 and 2: the mutant adds `W:stream-passed-over@+190`. B08, S05 and S17 give 0 differing blocks over reached (3,123 blocks), signs and digits.

Fix: Add rows that kill N1 and N2: a passed-over header '195 0 obj' or '105 0 obj' named by an older /XRefStm at own+1 and own+2, and S '195 0 obj' or '105 0 obj' named at +1 and +2. Add rows that kill R1 and R2: a passed-over '+11 0 %c\nobj' named by an older link at its header and at own+1. Re-judge B05, S09 and E02 on the NumberEnd of the first finding's fix; its sign-run rows should kill or settle them. Correct the R10b record for status.md (38 mutants: B05, S09 and E02 not equivalent as reasoned) and add the four survivors with what kills them.

Verdict: The finding holds, but it is narrower than stated in three ways.

(a) B05, S09 and E02 are not equivalent. R10b's reasons for all three rest on one premise: the byte at NumberEnd is the white space or comment after the number, and no section and no reading starts there. That premise is false for a run of signs. The parser takes `++11` as the integer 11 (PdfNumberParser: a run of signs, the first one decides). The comment path finds the header `++11 0 %c\nobj` at its first `+`, and NumberEnd skips only one byte, so it stops on the second `+`. A reading from that `+` starts a token and reads the same stream. B08, S05 and S17 show no difference on any of my rows, so their equivalence stands.

The narrowing on (a): on every input that tells B05, S09 or E02 apart, the head is the side that is wrong, and the mutant fixes it:
- for a passed-over header, the head reports xref.stream-passed-over at a stream the chain read;
- on the S path, the head takes S as a stream before the one the /XRefStm read, and rebuilds.
This is the sign-run defect, which is a separate finding. So a test that kills these mutants now would pin a defect. They have to be judged again once that defect is fixed.

(b) N1 and N2 are in ADR 53's catalog: item 2, a constant bound n moved to n ± 1, or item 5, one element removed from a set. Both are on NumberEnd's line, which 2be5c44 rewrote, and R10b's E01 to E06 never touched the range bounds. Both survive the whole AdCodicem.Pdf.Tests project with the 242 remote documents (10,623 tests, 0 failed). The reviewer ran without the remote corpus (7,937 tests, 20 skipped); the result is the same. The committed builders kill both mutants just by changing the number.

R1 and R2 also survive the whole suite. But round 10's [18] ruled that shifting an argument offset such as `start - lead + 1` is outside the catalog as written, so R1 and R2 are test gaps, not ADR 53 survivors. R3 is outside the catalog too. Its only difference is on the sign-run rows, where it removes the head's false Warning, so calling it "equivalent except for a run of signs" does not apply.

Severity is minor, following round 10's [15] and rounds 7 and 8 (M09, H3): an equivalent classed on a false reason means the record is false and a test is missing. The survivors alone would be nit (ADR 53; round 8 [12], round 10 [18]). None of #171, #289, #295, #320 to #333 or #52 covers this. Nothing for the maintainer to decide.

Verdict evidence: Rebuilt independently at 79c6a7f (Release, 0 warnings).

**Probe.** C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r11wip/verify-adr53-equivalents-told-apart-and-survivors/ZzVa53Tests.cs.txt has four row sets:
- the committed builders, unchanged: HybridFiles.PrevFallingShortOfTheStreamBeforeTheTablesOwn with ownNumber 11, 111, 195, 105, 19, 10, 190, 109, and NamingTheUpdatesStreamAtOrInsideItsNumber with headers "195 0 obj", "105 0 obj", "19 0 obj", "10 0 obj";
- my own passed-over builder, the header given as text;
- my own update (S) builder, the header given as text.
Positions are printed relative to the own stream or S.

**Driver.** mut.py in the same folder: one exact replacement per mutant, run in a separate worktree. The base run is byte-identical to the head's (out-base.txt = out-head.txt). Outputs are out-<ID>.txt; whole-suite logs are suite-<ID>.txt.

**Head output.** On the passed-over `++11 0 %c\nobj` named by the oldest /XRefStm or /Prev at +1, +2 and +3, the chain reads the stream there (`/XRefStm:Read:Stream@+1`), and the head still adds `W:xref.stream-passed-over@0`.

**Rows that differ from the head:**
- **B05:** only `++11 0 %c\nobj` at +1, with /XRefStm or /Prev. The Warning is gone.
- **S09:** only U `++13 0 %c\nobj` at +1.
  - prev=nothing: head `/Prev:Relocated:Stream@0 D[R:offset-adjusted@0 R:rebuilt@-421] rep=True`; mutant `/Prev:Relocated:Table@-191 D[R:offset-adjusted@-191] rep=False`.
  - prev=oldest: head takes S as another stream; mutant gives `NamedAgain`, `hybrid-stream-lost`.
  - prev=table: head adds `chain-cycle` and a rebuild.
- **E02:** the B05 rows and the S09 rows, and nothing else.
- **B08:** 0 rows. My other rows do not tell S05 or S17 apart.
- **N1:** 30 rows, all on committed builders. Passed-over n=195 at +1 and +2, n=19 at +1, n=190 at +1, n=109 at +2: the mutant adds `W:xref.stream-passed-over@0`. Update "195 0 obj" and "19 0 obj" at +1 and +2, prev=nothing: the mutant gives `/Prev:Relocated:Stream@0 R:rebuilt@-419 rep=True`, where the head gives `/Prev:Relocated:Table@-189`.
- **N2:** 26 rows. n=105 and n=109 at +1 and +2, and update "105 0 obj", the same way.
- **R1:** `+11 0 %c\nobj` and `+105 0 %c\nobj` at every into from 0 to 4: the mutant adds the Warning.
- **R2:** the same headers at into 1 to 3.
- **R3:** only the `++11` rows, where it removes the head's Warning.

**Whole suite** (remote corpus hard-linked, summary-suite-m.txt and summary-suite-w.txt): N1, N2, R1 and R2 each give `total 10623, failed 0, succeeded 10620, skipped 3`.

**Record.** R10b's catalog (W/r10wip/r10b-mut/run_mutants.py) has E01 to E06 for NumberEnd and no range-bound mutant.

**Cleanup.** Worktrees cleaned, remote copies removed, the maintainer's 242 remote files intact.
fix: 1. Kill N1 and N2 with the committed builders, no new builder needed.
   - Add rows to A_stream_passed_over_near_a_hybrid_table_s_prev_that_an_older_section_names_at_its_header_or_inside_its_object_number_is_reported_as_the_chain_finds_it_not_as_passed_over: ("/XRefStm", 195, 1, "[1 4 2]"), ("/Prev", 195, 2, "[1 4 2]"), ("/XRefStm", 105, 1, "[1 4 2]"), ("/Prev", 105, 2, "[1 4 2]"). They expect the existing readable outcome (offset-adjusted and hybrid-stream-lost, ten read). A single row with 109 at into 2 kills both mutants.
   - Add rows "195 0 obj" and "105 0 obj" at into 1 and 2 with "nothing" to The_stream_a_hybrid_table_s_xrefstm_read_is_told_near_its_prev_alike_whether_its_number_is_written_with_a_sign_or_not, or to a sibling theory: /XRefStm Read at S+into, /Prev Relocated Table, no rebuild.

2. Do not pin the head's output on `++11 0 %c\nobj` or `++13 0 %c\nobj`: it is the sign-run defect. Judge B05, S09 and E02 again on the NumberEnd that the sign-run fix produces. Its rows (a passed-over `++11 0 %c\nobj` named at +1 and +2, and S `++13 0 %c\nobj` at +1) will either kill each mutant or let it be classed equivalent with a reason that holds.

3. Optional, outside the catalog: a header-text parameter on the passed-over builder, with rows `+11 0 %c\nobj` named at its header and at +1 (no Warning). These kill R1 and R2.

4. Correct R10b's ADR 53 record in status.md. The current record says 32 killed and 6 equivalent. It becomes:
   - 3 equivalents: B08, S05, S17;
   - B05, S09, E02 not equivalent as reasoned, because a run of signs tells them apart; they are settled with the sign-run fix;
   - N1 and N2, the catalog's two range-bound mutants on NumberEnd's line, missing from the run, and killed by the new rows;
   - R1 and R2 noted as outside the catalog.
needs_maintainer: False

## 18. hybrid-naming-again-told-by-byte — minor → narrowed minor (own-stream (2be5c44, 5395eed, )
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3507 (`hybrid == previous || (previousStart >= 0 && hybridStart == previousStart)`); #332 comment 6724664195 (r10track/body332.md)

Claim: Open point (c) is #332's and is accepted. But the round 10 comment on #332 says that 'the search's reached test is the one place left that tells it by a byte', and that is false. The check that one hybrid table's /XRefStm and /Prev name the same section (#156, B19-5) also compares starts by byte. Take an older hybrid table whose /XRefStm names a stream's header and whose /Prev names one byte into its object number, or the reverse: it reads the same stream twice, with no xref.hybrid-stream-lost, the index not left incomplete, and no validator Error. Named at the same byte, the same pair is reported. This is the 'told by its data' rule that 2be5c44 applied to the passed-over report, missing at a second site.

Evidence: ZzR11Reached, 'both' rows (out/head-reached.txt). The oldest table names the own stream through /XRefStm at own+a and /Prev at own+b. both xrefstm=0 prev=1 own=1845: `S[... /Prev:Read:Table@226+0 /XRefStm:Read:Stream@1845+0 /Prev:Read:Stream@1846+0 ]` `D[R:xref.offset-adjusted@1655 W:xref.hybrid-stream-lost@1655]` `F[W:xref.section-shifted E:xref.hybrid-stream-wrong]`. Nothing here concerns the oldest table: the single lost and Error belong to the newest table. both xrefstm=1 prev=-1 gives the same (`/XRefStm:Read:Stream@1846+0 /Prev:Read:Stream@1844+1`). Control both xrefstm=1 prev=1: `/XRefStm:NamedAgain:Unknown@1846+0 /Prev:Read:Stream@1846+0`, `W:xref.hybrid-stream-lost@1846`, `E:xref.hybrid-stream-wrong E:xref.hybrid-stream-wrong`.

Fix: File it, with no code in this batch. Comment on #332 to correct 'the one place left' and to add this shape (the 'both' rows): the decision #332 waits on should cover the naming-again check at :3507 too, which would tell a section by its data, as NumberRunsInto and the passed-over report do.

Verdict: The text half holds, and goes further than the finding says. The behavior half is not a batch defect.

Text half. The round 10 comment on #332 (6724664195, r10track/body332.md) says: "The search's reached test is the one place left that tells it by a byte." That is false. Q9 made one position-based notion of "already reached" for every chain check, and that notion tells a section by a byte. So it is not only the search at PdfFileReader.cs:5180 that does this. It is also:
- the same-section test between a hybrid table's /XRefStm and its /Prev (:3507, `hybridStart == previousStart`);
- ReachedAt itself (:3854), used by the /Prev loop check (:3248) and by the /XRefStm naming-again record.
In each, a link that names one byte inside a stream's object number reads `1 0 obj`: the same dictionary and the same data. The check counts it as another section. The comment can steer #330/#332's decision toward fixing :5180 alone.

Behavior half. On 79c6a7f, a table whose /XRefStm names the stream's header and whose /Prev names one byte into its number (or the reverse) reads the stream twice. Nothing is reported, the index is not left incomplete, and the validator finds nothing. The same pair named at one byte is reported. But main 21a0298 gives exactly the same output for that pair: read twice, silent. So the batch made nothing worse.

The shape is also outside what was decided and what the texts claim:
- B19-5 decided branch (c) by the offset, and round 1 widened it to the section both links reach under Q9's position notion.
- lazy-reading.md:127, diagnostics.md:115 and the threat model's #156 row (:432) list what branch (c) covers: the same offset, the white space before the section, or where the search comes to where the /Prev keeps its place. Inside the object number is not in that list, and the code comment at :3500-3503 says the same.
- R9-Q1's "told by its data" rule was decided for S. 2be5c44 applied it to the passed-over report only because that report's texts were false.

Telling sections by their data in Q9's checks would widen a maintainer decision. That belongs with #330, which already waits on the maintainer and blocks #332. The shape is not a batch defect to fix.

Verdict evidence: Probe: W/r11wip/verify-hybrid-naming-again-told-by-byte/ZzVerifyNamingAgainByteTests.cs. It is self-contained and runs alike on the branch and on main. Outputs are in out/head-naming.txt, out/main-naming.txt, out/head-three.txt and out/main-three.txt, and the simple files are out/simple_<a>_<b>.pdf.

The simple shape:
- T0 is a classic table holding the catalog, at 229.
- S is object 11, a cross-reference stream at 418, with /Prev 229.
- T2 is startxref's table, at 576, with /XRefStm S+a and /Prev S+b.

Results on 79c6a7f:
- a=0, b=0: `/XRefStm:NamedAgain:Unknown@418+0 /Prev:Read:Stream@418+0 /Prev:Read:Table@229+0`, with `Warning xref.hybrid-stream-lost at 418` and `Error xref.hybrid-stream-wrong at offset 576`.
- a=0, b=1: `/XRefStm:Read:Stream@418+0 /Prev:Read:Stream@419+0 /Prev:Read:Table@229+0`, with D[] and F[]. a=1, b=0 gives the same with 419 and 418 swapped.
- a=-1, b=1 also reads twice; only `Warning xref.offset-imprecise` is found.
- Controls that are reported: a=-1/b=0, a=0/b=-1, a=0/b=+2 and a=+2/b=0.
- Objects read 4="four", 5="old five", 6="six" and rep=False in every row.

On main 21a0298, a=0/b=1 and a=1/b=0 give the same lines, D[] and F[], read twice. a=0/b=0 gives `xref.chain-cycle` and a rebuild, with `Error xref.chain-loop`.

The reviewer's 'both' rows (r11wip/own-stream/out/head-reached.txt) reproduce. In that shape main never reaches the oldest table, so it cannot be compared there.

ReachedAt also tells by a byte (three-revision shape: T2 has /XRefStm S and /Prev T1; out/*-three.txt):
- T1 /Prev S+0 gives `W:xref.chain-cycle@418` and `R:xref.rebuilt`, with `Error xref.chain-loop` (#330's loop).
- T1 /Prev S+1 gives `/Prev:Read:Stream@419+0 /Prev:Read:Table@229+0`, D[] and F[]: S is read again, with no loop.
- T1 /XRefStm S+0 is recorded `NamedAgain@418`. T1 /XRefStm S+1 gives `/XRefStm:Read:Stream@419+0`: read again, not named again.
- main gives the same at S+1.
fix: No code in this batch. Post a follow-up comment on #332, cross-referenced on #330, that does three things:
- Correct the sentence. The search's reached test is one of several places that tell a stream by the byte its reading started at, as Q9's one notion of "reached" does. The others are the /Prev loop check (ReachedAt, :3248/:3854), the /XRefStm naming-again record (ReachedAt), and the test of whether a hybrid table's two links reach one section (:3507).
- Add the three reproducers with their branch and main outputs: a=0/b=1 in the simple shape; #330's shape with T1's /Prev one byte into S, which reads whole where the exact offset loops and rebuilds; and a copied /XRefStm one byte in, read again rather than named again. Each reads as main does.
- Ask that the decision #330/#332 wait on say whether these checks tell a section by its data, as R9-Q1 does for S.

The options for that design choice, which goes with #330:
- (a) Keep Q9's byte notion and say in lazy-reading.md, diagnostics.md:115 and the threat model's #156 row that a link naming a place inside a section's object number reaches another section: read again, as on main.
- (b) Tell by data. Record each section the chain reads with where its object number ends, as NumberEnd does for passed-over places, and count as reached any named position inside [header, number end) of a section read. Then branch (c) reports the S/S+1 pairs, #330's loop also fires at S+1, and a copied /XRefStm one byte off is recorded as named again. It costs one span lookup per naming, bounded by MaxXRefSectionCount.

No repository text has to change now: the code comment at :3500-3503 and the pages list the namings branch (c) covers, and that list holds.
needs_maintainer: False

## 19. ws-flag-withholds-a-warning-its-guard-left-nothing-unread-for — minor → narrowed minor (tests)
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:5562 (flag set), :3755 (read); texts src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:122-128, docs/website/docs/reference/diagnostics.md:116; tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:6710 (no row pins it)

Claim: Open point (a). `_whiteSpacePastGuard` withholds every passed-over report in the document, even where the guard's search finds the section moved past the run, reads it whole and passes nothing over. A Warning that the guard could neither have hidden nor caused is then lost. The texts say the reverse: the limit's report "stands for what it left unread, or for what that search passed over or did not find", and "a cut that leaves nothing unread … leaves the report made". No row of the two white-space theories covers this shape. The one guarded row whose search finds the section, ('the stream', true, 400, 256), reads stream 12 anyway.

Evidence: Probe W/r11wip/tests/probes/ZzR11Shapes.cs.txt, section 'open point (a)'; output W/r11wip/tests/dumps/shapes.txt. File: HybridFiles.PassingOverAStreamAnOlderTableNamesPastWhiteSpace("the oldest table", 300 (or 400), oldestNamesOwn: false), at 79c6a7f. Default limits: sections [startxref Read Table @1319, /XRefStm Read Stream @247, /Prev Relocated Table @802, /Prev Read Table @943+300]; diagnostics [xref.offset-adjusted @802, xref.stream-passed-over @362]. MaxTrailerLength 256: sections [... /Prev Relocated Table @802, /Prev Relocated Table @1243]; diagnostics [xref.offset-adjusted @802, limit.trailer @943, xref.offset-adjusted @1243 'found 300 bytes after it']; object 5 "five"; no rebuild; no Warning. The guard's search near 943 finds the oldest table 300 bytes on and reads it whole, and it names nothing. Stream 12 at 362 is 581 bytes before 943, outside that search's reach. It was passed over earlier by the search near 562 (the newest table's /Prev), which has nothing to do with the guard. Run 400 gives the same result. main has no xref.stream-passed-over, so there is nothing to compare.

Fix: Either narrow the flag: set it only where the guarded section is not found (NotFound, then section-missing), and otherwise withhold only the streams that the guarded search itself passed over (mark those records). Or keep the rule and say in PdfDiagnosticCodes.XRefStreamPassedOver, the ReportStreamsPassedOver remark, diagnostics.md:116, lazy-reading.md:221 and threat-model 432 that the guard withholds every passed-over report in the document, even where its search finds the section moved and leaves nothing unread; qualify the 'stands for' and 'a cut that leaves nothing unread' sentences to match. Either way, add rows ('the oldest table', false, 300, 256) and (…, 400, 256) to A_stream_passed_over_near_a_hybrid_table_s_prev_is_left_to_the_trailer_guard_where_white_space_past_it_keeps_what_an_older_link_names_unread, with codes as decided.

Verdict: The behavior reproduces exactly at 79c6a7f. The finding is narrowed because two of its three claims fail.

What is refuted:

1. "The texts say the reverse" does not hold. PdfDiagnosticCodes.XRefStreamPassedOver (:122-128), the ReportStreamsPassedOver summary and remark (:3706-3735), diagnostics.md:116 and lazy-reading.md:218-224 all state without condition that nothing is reported "where white space and comments at an offset the chain names run past MaxTrailerLength, which turned the reading of the section there into the search near the offset". The shape meets that condition: limit.trailer is reported at 943, then the search runs near 943. threat-model.md:439 goes further: "no cross-reference stream a search passed over reported", meaning any search. The document-wide scope is also on record:
   - round 10 decided the flag would be "tested in ReportStreamsPassedOver beside ChainStoppedByGuard" (decisions.md [5]/[9]/[21]);
   - R10c's result says it "withholds every passed-over report of the document";
   - the #289 comment 6724664402 says it "withholds every passed-over report of the document, not only one near the link the guard met", and gives a variant that loses a Warning the file makes at default limits.
   The sentence "a cut that leaves nothing unread ... leaves the report made" lists cuts the reader knows to be harmless (data past a stream's last row, an object past a whole reference it holds). The texts name the white-space class separately.

2. "The guard could neither have hidden nor caused" the loss is true of shape A's bytes, but not of anything the reader can see. Shape B (below) has the same guard and its search also takes a section reported found moved. There, the link the guard hid names stream 12. Telling A from B means knowing where the run ends, which means reading past MaxTrailerLength. That is the open design of #289. The comment's two ways of closing #289 settle the shape: if white space stops counting toward the bound, shape A gets its Warning back; if the section becomes the guard's, the Warning stays withheld. The batch is no worse than main, which has no xref.stream-passed-over.

3. The finding's first fix (set the flag only where the section ends missing, and otherwise withhold only the streams the guarded search itself passed over) is unsound. Built as a mutant, it makes the Warning in shape B under the guard, where default limits read stream 12. That brings back round 10's [5]/[9]/[21] defect (ADR 34: the guard's cut laid on the file).

What holds: no row pins the document-wide scope in the case where the guard's search finds a section, neither shape A nor shape B. The unsound narrowing passes the whole unit project. This is the same kind of gap as round 9's [15] (a documented rule with no test, and a non-first-order change passing the suite), which was settled as minor. No maintainer decision is needed: the rule is decided, and only rows are missing.

Verdict evidence: All probe files and outputs are under C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r11wip/verify-ws-flag-withholds-a-warning-its-guard-left-nothing-unread-for/. Probe: ZzR11VerWsLeft.cs.txt. Release build of 79c6a7f in this worktree. Outputs: out-head.txt, out-noflag.txt, out-narrow.txt. Mutant: narrowing-mutant.diff. Suite log: suite-narrow.log. The source and probe were reverted afterwards; the worktree is clean.

Shape A, the reviewer's file: HybridFiles.PassingOverAStreamAnOlderTableNamesPastWhiteSpace("the oldest table", 300, false). Offsets: stream 11 at 247, stream 12 at 362, the run at 943, the older table at 802, the oldest table at 1243, the newest table at 1319.
- Default limits: sections are startxref Read Table @1319, /XRefStm Read Stream @247, /Prev Relocated Table @802, /Prev Read Table @943+300. Diagnostics: Repair xref.offset-adjusted @802, then Warning xref.stream-passed-over @362. Object 5 reads "five".
- MaxTrailerLength 256: the last /Prev becomes Relocated Table @1243+0. Diagnostics: xref.offset-adjusted @802, then Warning limit.trailer @943, then Repair xref.offset-adjusted @1243 ("found 300 bytes after it"). There is no Warning. Object 5 reads "five", and the document is not repaired.
- Run 400 gives the same. With oldestNamesOwn true, stream 12 is read (/XRefStm Read Stream @362) under both limits.

Shape B, the counter-shape: the same file with an unread classic table placed right before the run. It indexes nothing and gives no /Prev. The oldest table's /XRefStm names stream 12. Offsets: decoy table at 943, run at 995, oldest table at 1295.
- Default limits: /Prev Read Table @995+300, /XRefStm Read Stream @362. The only diagnostic is xref.offset-adjusted @802.
- MaxTrailerLength 256 at 79c6a7f: /Prev Relocated Table @943+0. Diagnostics: xref.offset-adjusted @802, limit.trailer @995, then xref.offset-adjusted @943 ("found 52 bytes before it"). Object 5 reads null. There is no Warning, which is right: the guard hid the link that names stream 12.

The finding's first fix, built as a mutant (narrowing-mutant.diff): the flag is set only in ReportLostSection for a section the white-space guard left NotFound, and the guarded search's own passed-over records are marked and skipped.
- Shape B under 256 then gives "Warning xref.stream-passed-over @362: ... the chain did not read the stream", where default limits read stream 12.
- Shape A's Warning also comes back. Removing the flag outright (out-noflag.txt) gives the same diffs.
- The whole AdCodicem.Pdf.Tests project under that mutant: 7,934 total, 7,912 passed, 20 skipped (no remote corpus here), 2 failed. The 2 failures are SeededComparerTests source scans of the mutant's own HashSet<long>, not any behavior.

The texts were checked at 79c6a7f:
- PdfDiagnosticCodes.cs:122-128
- PdfFileReader.cs:658-667, :3706-3735, :3748-3758, :5552-5562
- diagnostics.md:116
- lazy-reading.md:218-224
- threat-model.md:432 and :439
- #289 comment body in r10track/body289.md, line 18.
fix: Keep the code. Do not narrow the flag: the finding's first fix brings back round 10's [5]/[9]/[21] defect in shape B. Pin the scope the texts and #289 state by adding rows.

1. In A_stream_passed_over_near_a_hybrid_table_s_prev_is_left_to_the_trailer_guard_where_white_space_past_it_keeps_what_an_older_link_names_unread (CrossReferenceChainTests.cs:6700), or in a sibling theory, add rows ("the oldest table", false, 300, 256) and ("the oldest table", false, 400, 256). Expect:
   - codes [XRefOffsetAdjusted, LimitTrailer, XRefOffsetAdjusted], the last at the oldest table;
   - object 5 read;
   - WasRepaired false.
   The default-limit rows of the same file keep their Warning at stream 12.

2. Add the shape-B row through a builder parameter: a table no link names, written between the older table and the run, the oldest table naming stream 12.
   - Under MaxTrailerLength 256: /Prev Relocated at the decoy ("found 52 bytes before it"), codes [XRefOffsetAdjusted, LimitTrailer, XRefOffsetAdjusted], object 5 null, no Warning.
   - At default limits: stream 12 read, and only XRefOffsetAdjusted.
   The test comment should say why the scope is the document's: the guard's search may take a section other than the one past the run, and then the link that names the stream is the guard's to hide. The reader cannot tell which without reading past the guard (#289, ADR 34).

Optional nit: in PdfDiagnosticCodes.XRefStreamPassedOver, the ReportStreamsPassedOver summary, diagnostics.md:116 and lazy-reading.md:221, say "no stream any search passed over is reported, even where the search near that offset finds the section moved", as threat-model.md:439 already does.

If shape A's Warning is wanted back, that belongs to #289's closing (white space no longer counting toward the bound), not to a change of the flag in this batch.
needs_maintainer: False

## 20. sign-run-escapes-told-by-data — minor → confirmed minor (tests)
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:5355-5370 (NumberRunsInto, NumberEnd); tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:5789 (sign theory rows)

Claim: R10b's [20] fix covers one leading '+'. PdfNumberParser.TryParse accepts "an optional run of signs", the first deciding, so a load reads '++13 0 obj' and '+-13 0 obj' from their first byte as object 13. Two things then miss: NumberRunsInto's sign arm, which tests candidate == streamStart + 1 only, and NumberEnd, which skips one byte only. In the comment-path direction the stream the /XRefStm read is taken as a stream before itself: the chain ends there, the index is rebuilt as the document opens, and the validator reports file.root-invalid. That is worse than main, the same regression [20] fixed for one '+'. In the walk direction the stream is read twice and the naming goes unreported, as on main, where one '+' now gives NamedAgain, xref.hybrid-stream-lost and Error xref.hybrid-stream-wrong. The sign theory has no row with a run of signs.

Evidence: Probe W/r11wip/tests/probes/ZzR11Signs.cs.txt, self-contained, the sign theory's builder written out. Outputs W/r11wip/tests/dumps/signs-head.txt (79c6a7f) and signs-main.txt (21a0298). '++13 0 %c\nobj', into 1 or 2, streamPrev nothing. Head: /XRefStm Read Stream @419, /Prev Relocated Stream @418, [xref.offset-adjusted @418, xref.rebuilt], WasRepaired True at open, validator Error file.root-invalid. Main: /Prev Relocated Table @229, no rebuild, no Error. Head with a single '+': Relocated Table @229, like main. '++13 0 obj' (and '+-13 0 obj'), into 0, streamPrev the oldest table. Head: /XRefStm Read Stream @418, /Prev Relocated Stream @420, /Prev Read Table @229, [xref.offset-adjusted @420], no validator Error; main is identical. Head with '+13 0 obj': /XRefStm NamedAgain @418, [offset-adjusted, xref.hybrid-stream-lost], Error xref.hybrid-stream-wrong. '++13 0 %c\nobj', into 1, the oldest table. Head: /Prev Relocated Stream @418, then the oldest table; the stream is read twice and nothing is reported. Main: Relocated Table @229.

Fix: Either refuse a header whose number carries more than one sign; ISO 32000 integers take at most one sign, so this means the load and the walk agree on it. Or treat the run as one sign: NumberEnd skips the run of '+'/'-' before scanning digits; NumberRunsInto's second arm maps a candidate past streamStart where span[streamStart] is '+' and every byte in [streamStart, candidate) is a sign, bounded by the window. Add rows ('++13 0 obj', 0, 'the oldest table') and ('++13 0 %c\nobj', 1, 'nothing') to The_stream_a_hybrid_table_s_xrefstm_read_is_told_near_its_prev_alike_whether_its_number_is_written_with_a_sign_or_not. Reword 'the + the number is written with' in the remark and pages if the run is accepted.

Verdict: The finding holds as claimed, and it reaches one shape further than claimed. The load reads a run of signs with the first sign deciding (PdfNumberParser.TryParse), so '++13 0 obj' and '+-13 0 obj' read as object 13 from their first byte. R10b handles one sign only. NumberEnd (PdfFileReader.cs:5369-5370) skips just the header's first byte. NumberRunsInto's second arm (:5359) maps only candidate == streamStart + 1.

Comment-path direction (header '++13 0 %c\nobj', or '+-13' or '+++13'), with the /XRefStm naming any byte past the first sign: S's own header, found at the first sign, is taken as an older stream before S. This is worse than main, the regression round 10's [20] fixed for one '+':
- S gives no /Prev: the index is rebuilt at open and the validator reports Error file.root-invalid.
- S's /Prev names the table's /Prev offset (not claimed by the reviewer): a Warning xref.chain-cycle, a rebuild at open, and Error xref.chain-loop.
- Main reads both shapes cleanly.

Walk direction ('++13 0 obj' with the /XRefStm naming the first sign, and S's /Prev naming the oldest table): S is read twice and the naming goes unreported. This is identical to main, so it is not a regression. It does not match the one-'+' rows, which now report NamedAgain and Error xref.hybrid-stream-wrong.

Why it is not known or accepted:
- No issue holds it. #327 covers the scan and the search near a row reading signed headers. Its closing would not fix NumberEnd for the comment path.
- It is R9-Q1 ('a candidate before S whose data starts where S's does is S itself'), already decided, left unimplemented for a run of signs.

A text misstates it:
- lazy-reading.md:200 says S is told by its data 'whatever byte of its header the /XRefStm names'. That is false when the /XRefStm names a sign of a run.
- threat-model.md:432 lists the risk as mitigated, for 'the +' only.

Minor, as [20] was: it needs an invalid header (ISO 32000-1 7.3.3 allows one sign) together with a hybrid /Prev that misses, and every object is still served after the rebuild.

Verdict evidence: Probe: C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r11wip/verify-sign-run-escapes-told-by-data/ZzVerifySignRunData.cs. It is self-contained, with its own builder:
- Shape: oldest table (objects 1-3 and 5), object 4, stream 13 at S indexing 4 and 13, a comment of 600 or 250 '=', newest table with /Prev at S-7 or S-3 and /XRefStm at S+into.
- Grid: 12 headers × 3 values of S's /Prev × every into × 2 gaps × 2 backs = 516 rows.

Runs:
- Head: 79c6a7f, this worktree.
- Main: 21a0298, a temporary worktree at r11wip/vsrd, now removed. The longer path failed git's index reset.
- Outputs, in the same folder: head-signrun-data.txt and main-signrun-data.txt. Row-by-row diff in cmp-600-7.txt, made by cmp.py.

Positions are relative to S.

Comment path, '++13 0 %c\nobj', S /Prev nothing, into 1, 2 and 3 (also '+-13' into 2-3 and '+++13' into 1-4, at every gap and back):
- Head: [startxref Read Table NEWEST] [/XRefStm Read Stream S+into] [/Prev Relocated Stream S+0]. Diagnostics: [Repair xref.offset-adjusted @S+0] [Repair xref.rebuilt @-1]. repairedAtOpen=True. Findings: E:file.root-invalid, W:xref.section-shifted.
- Main: [/Prev Relocated Table OLDEST], offset-adjusted @OLDEST only, repairedAtOpen=False, W:xref.section-shifted only.

Same header with S /Prev naming the table's /Prev offset (S-7), into 1-3:
- Head: [/Prev Relocated Stream S+0], [Warning xref.chain-cycle @S-7] [Repair xref.rebuilt @-1], repairedAtOpen=True, E:xref.chain-loop.
- Main: Relocated Table OLDEST, clean.

Same header with S /Prev naming the oldest table, into 1-3:
- Head: [/XRefStm Read Stream S+into] [/Prev Relocated Stream S+0] [/Prev Read Table OLDEST]. offset-adjusted @S+0 only, no hybrid-stream-lost.
- Main: Relocated Table OLDEST.

Walk path, '++13 0 obj', S /Prev the oldest table, into 0 (also '+-13' into 0 and '+++13' into 0-1):
- Head and main identical: [/XRefStm Read Stream S+0] [/Prev Relocated Stream S+2] [/Prev Read Table OLDEST], [Repair xref.offset-adjusted @S+2], W:xref.section-shifted.
- Head with '+13 0 obj': [/XRefStm NamedAgain Unknown S+0] [/Prev Relocated Stream S+0], [Warning xref.hybrid-stream-lost @S+0], E:xref.hybrid-stream-wrong.

The head is better than main on the other '++' walk rows. For example '++13 0 obj' with S /Prev nothing: main gives Relocated Stream S+2 + xref.rebuilt + E:file.root-invalid, the head gives Relocated Table OLDEST.

Prototype: proto.diff in the same folder, now reverted. Results:
- Across the 516 rows: 0 rebuilt-at-open rows, 0 file.root-invalid, 0 xref.chain-loop. The head has 72/36/36 and main 176/88/88.
- Every run-of-signs row now reads like its one-sign row. '++13 0 obj' into 0 with the oldest table gives NamedAgain S+0 + hybrid-stream-lost + E:xref.hybrid-stream-wrong. '++13 0 %c\nobj' into 1 with nothing gives Relocated Table OLDEST, no rebuild (cmp-proto.txt).
- CrossReferenceChainTests 667/667 pass. Whole AdCodicem.Pdf.Tests: 7,934, 0 failed, 20 skipped, no remote corpus copy (proto-suite.txt).
fix: Fix it in this batch under R9-Q1, with no new decision. Treat a run of signs the way one sign is treated, using the window already open and reading nothing.

1. NumberEnd: where the header's first byte is a sign, skip the whole run of '+' and '-' before scanning digits. A header whose tokens a comment parts starts at the lexer's token, the whole run included.
2. NumberRunsInto's second arm: map a candidate where all of the following hold:
   - candidate > streamStart;
   - streamStart >= spanStart;
   - the byte at streamStart is '+';
   - every byte in [streamStart, candidate) is a sign.
   A reading that starts at a '-' reads a negative number and never a stream. The prototype is proto.diff in W/r11wip/verify-sign-run-escapes-told-by-data.
3. Add rows to The_stream_a_hybrid_table_s_xrefstm_read_is_told_near_its_prev_alike_whether_its_number_is_written_with_a_sign_or_not. Its expectations already fit unchanged:
   - ('++13 0 obj', 0, 'the oldest table');
   - ('+-13 0 obj', 0, 'the oldest table');
   - ('++13 0 %c\nobj', 1, 'nothing');
   - ('++13 0 %c\nobj', 2, 'nothing');
   - ('++13 0 %c\nobj', 1, 'the oldest table');
   - ("++13 0 %c\nobj", 1, "the table's /Prev").
4. Run ADR 53's mutants over the new conditions.
5. Reword 'the + the number is written with' to name the sign or run of signs it starts with, in:
   - the code at PdfFileReader.cs:5012-5013, :5072, :5165-5166, :5336-5337, and NumberEnd's summary at :5362-5364;
   - lazy-reading.md:200-203, threat-model.md:432 and :434, and diagnostics.md:115.

The same NumberEnd change also governs the passed-over report's [place, number end) test, which the sibling finding sign-run-escapes-number-end covers.

Refusing a header whose number carries more than one sign, in the load and the walk alike, is the alternative. That would be a maintainer's design choice, and it belongs to #327 (signed header numbers, batch 21), not to this fix.
needs_maintainer: False

## 21. signed-passed-over-header-never-reported — minor → confirmed minor (tests)
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:5284-5288 (record loop, IsTokenOnItsLine at the digit); texts src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:120-122

Claim: Open point (b). Take a typed cross-reference stream that gives no /Prev, header '+9 0 obj' alone on its line, passed over near a hybrid table's /Prev. The walk finds it at '9'; the token starts at the '+', so IsTokenOnItsLine is false and the stream is never recorded or reported. The same header unsigned, or signed with a comment parting its tokens (found at the sign), is reported. The texts give the rule as "its header is a token a load reads from the start of its line". '+9 0 obj' is such a token, since a load reads the stream from the sign: the code misses a stream that declares itself. No test covers a signed passed-over header in either direction.

Evidence: Probe W/r11wip/tests/probes/ZzR11Shapes.cs.txt, LongLine(header, 10, lineBroken): the PrevFallingInsideAStreamOnALongLine layout with the header given. Output W/r11wip/tests/dumps/shapes.txt, section 'open point (b)', at 79c6a7f. '9 0 obj', line broken or not: [xref.offset-adjusted @494, xref.stream-passed-over @351]. '+9 0 obj', line broken or not: [xref.offset-adjusted @495] only. '+9 0 %c\nobj': [xref.offset-adjusted @498, xref.stream-passed-over @351]. '9 0 %c\nobj': [xref.offset-adjusted @497, xref.stream-passed-over @351]. All four: /Prev Relocated Table oldest, object 5 "old five". main has no such report.

Fix: In the record loop, where the byte before `passed` is '+' (or a run of signs, if the fix above accepts one), test IsTokenOnItsLine at the sign and record the place there, with NumberEnd from it. Add rows '+9 0 obj' (reported) to a passed-over theory, for example a header parameter on HybridFiles.PrevFallingInsideAStreamOnALongLine. Otherwise file a debt and state the gap where the rule is written.

Verdict: The finding reproduces exactly on 79c6a7f with my own builder.

Cause. FindSectionCandidates finds headers through TryReadHeaderBackwards. The walk takes only the run of digits and stops at the sign, so '+9 0 obj' is found at the '9'. The record loop then asks IsTokenOnItsLine at that '9' (PdfFileReader.cs:5284-5285). The lexer's token "+9" starts one byte earlier, so the test is false and the stream is never recorded. Where a comment parts the header's tokens, TakeLine takes the lexer's token start, so the header is found at the '+', the test passes, and the stream is reported.

Why this is not an accepted or settled consequence:
- R9-Q2 decided to report "a header that is a token on its own line". Round 3's [8] put the line test on typed candidates as "its header's number a token on its line". '+9 0 obj' is such a header as a load reads it: a load at a row naming '+4 0 obj' reads object 4 with no report.
- The texts give the rule as "a token a load reads from the start of its line" with no exception for a sign: PdfDiagnosticCodes.cs:120-122, the ReportStreamsPassedOver summary (:3708-3709), the loop comment (:5268-5270), diagnostics.md:116 and lazy-reading.md:213-214. Read as written, they say this stream is reported. The pages mention a sign only for telling S (diagnostics.md:115, threat-model 432/434).
- The brief accepts "a header that is not a token on its line"; this header is one.
- #327, filed by this batch and planned for batch 21, holds the root: the section search starts a signed header at its digit, and the comment path starts it at its sign. It does not name this consequence or the next one, and both are new in this batch. So the finding is not refuted as known.
- No test covers a signed passed-over header in either direction. The '+13'/'+9' rows at CrossReferenceChainTests.cs:5131 and :5781-5787 only test how S is told.
- This is not a regression of round 10: c2a2aaa has the same record loop (:5218-5219). It comes from R9a, 8b31ebf. main has no such report at all.

Wider than claimed: the same split, the line test asked at the digit, also hits the #281 rule for a place that cannot be read (:5255-5259), and there it is worse than main. Take a typed stream alone near a /Prev, its dictionary cut by MaxTrailerLength after /Type /XRef:
- '9 0 obj', and '+9 0 %c\nobj', are taken as the guard's cut.
- '+9 0 obj' is dropped and reported as xref.section-missing, and the index is rebuilt. That blames the file for a guard's cut (invariant 12, ADR 34).
- main 21a0298 takes '+9 0 obj' as the guard's cut, with no rebuild.
On its own that would be major. The shape is exotic, though: a signed object number, a guard cut, and no other section within reach.

The finding's own consequence is a Warning not made, with every object served the same, in a shape no producer writes. Hence minor. The fix the finding proposes, a change in the record loop alone, would leave the candidate rule split. That rule is the one the loop comment says the report follows ("as a place the search takes though a fault of the file keeps it from being read does"), and it would leave the regression against main in place.

Verdict evidence: Probe: W/r11wip/verify-signed-passed-over-header-never-reported/ZzVerifySignedPassedOverHeader.cs.txt, my own builders. A is the passed-over shape: S, then a line of object 8, then object 9 of /Type /XRef with no /Prev, its header alone on its line; then object 5 and the oldest table; the newest hybrid table's /Prev names place+20. B is object 9 unreadable and alone. D is object 9 alone with its dictionary cut by MaxTrailerLength 160 after /Type /XRef. C is a load at a row. Outputs are out-head.txt (79c6a7f) and out-main.txt (21a0298), with build-head.txt and build-main.txt both at 0 warnings.

A, head. Every row has sections [startxref Read Table, /XRefStm Read Stream @225, /Prev Relocated Table @oldest], object 5 "old five" and repaired False:
- '9 0 obj': [Repair xref.offset-adjusted @508, Warning xref.stream-passed-over @365]
- '+9 0 obj': [Repair xref.offset-adjusted @509] only
- '  +9 0 obj': [offset-adjusted @511] only
- '+9 0\nobj': [offset-adjusted @509] only
- '+9 0 %c\nobj': [offset-adjusted @512, Warning xref.stream-passed-over @365], the sign
- '9 0 %c\nobj': [offset-adjusted @511, stream-passed-over @365]
- '-9 0 obj': [offset-adjusted @509] only, which is right: a load reads -9.
A on main: offset-adjusted only, every row.

C, head and main: a row naming '+4 0 obj' reads object 4 as "four", with 0 diagnostics, the same as '4 0 obj'.

D, head:
- '9 0 obj': /Prev Malformed Stream @1236 cutByLimit True, [limit.trailer @1236, offset-adjusted @1236], obj 5 null, repaired False
- '+9 0 obj': /Prev NotFound @1216+3, [Warning limit.trailer @1237, Warning xref.section-missing @1216, Repair xref.rebuilt], obj 5 "old five", repaired True
- '+9 0 %c\nobj': Malformed @1236 cutByLimit True, as the unsigned row
D on main, '+9 0 obj': /Prev Malformed Stream @1237 cutByLimit True, [limit.trailer @1237, offset-adjusted @1237], obj 5 null, repaired False.

B, head: '+9 0 obj' gives section-missing and a rebuild, while '9 0 obj' and '+9 0 %c\nobj' give Malformed and section-unreadable. main rebuilds in every B row, so B is not worse than main.

Code at 79c6a7f: PdfFileReader.cs:5284-5285 `typedPassedOver?.Contains(passed) == true && IsTokenOnItsLine(window.Memory.Span, start - lead, (int)(passed - (start - lead)))`, and :5257-5259 the same test for a Malformed candidate. c2a2aaa :5218-5219 is identical.
fix: The fix is in this batch and needs no question: it applies R9-Q2 and round 3's [8] as decided, for a header a load reads today.

Code: give one private helper to both line-test sites. Where the byte before a place the walk found is '+' (the window holds it, per NumberRunsInto's remark; guard the file's first byte), ask IsTokenOnItsLine at the '+'.
- Record loop (:5284-5288): record the place at the '+', with NumberEnd from it. StartsWithin then counts a chain reading at the sign or at the digit as reached.
- Candidate acceptance (:5257-5259): keep reading and relocating at the digit, as main does, and ask only the line test at the sign.
A '-' declares nothing, since a load reads -9. Leave the walk itself and NumberRunsInto's arm to #327: changing TryReadHeaderBackwards ripples into the scan, the search near a row and #327's hiding helper.

Tests:
- add a header parameter to HybridFiles.PrevFallingInsideAStreamOnALongLine, with rows '+9 0 obj' (reported at the sign) and '-9 0 obj' (not reported) beside '9 0 obj';
- add a '+9 0 obj' row, cut by MaxTrailerLength after /Type /XRef, alone: taken as the guard's, no section-missing, no rebuild, as on main;
- run ADR 53 over the helper.

Texts: once this lands they are true as written. Optionally add "a header whose number is written with a sign counts from its sign" where the rule is stated.

Tracking: comment on #327 with these rows, and say that its closing must keep the line test at the header's token start. If the maintainer later closes #327 with "a sign makes no header for any reader", every path changes together.

Fallback, only if the code fix is deferred: state the gap at each text named in the reason (the walk starts a signed header at its digit, so it declares nothing unless a comment parts its tokens, #327), and file the regression against main as debt. That leaves a guard's cut blamed on the file, which ADR 34 does not allow, so I do not recommend it.
needs_maintainer: False

## 22. seeded-comparer-test-fails-under-ci-coverage-on-windows — minor → confirmed minor (tests)
Location: tests/AdCodicem.Pdf.Tests/SeededComparerTests.cs:2675 (IL read through MethodBody.GetILAsByteArray), assertion :367

Claim: Pre-existing on main 21a0298; not round 10's. Run under the CI's coverage command (--coverage, cobertura), No_method_of_the_library_builds_a_table_keyed_by_numbers_without_the_seeded_comparer fails whenever a test that runs ArlingtonWalk runs first in the same process. It flags ArlingtonWalk..ctor, which does pass SeededComparer.Instance (ArlingtonWalk.cs:86). The coverage profiler rewrites the IL that GetMethodBody() returns once a method has been JIT-instrumented. The test therefore depends on which other tests run beside it. CI on Linux is green (run 42503186689 at 79c6a7f), so the order or platform hides it there.

Evidence: Whole unit suite with the CI's command in my worktree at 79c6a7f, no remote corpus as in CI (W/r11wip/tests/coverage.sh, output coverage.txt): total 7,933, 1 failed with 'found at least one item {"AdCodicem.Pdf.Validation.ArlingtonWalk..ctor: Dictionary`2<Int32, Byte>..ctor"}'. Reproduced: --filter-class *ArlingtonRuleTests --filter-class *SeededComparerTests with --coverage fails (356 run, 1 failed) on 79c6a7f and on 21a0298. The same two classes without --coverage: 356/356 pass. SeededComparerTests alone with --coverage: 203/203 pass.

Fix: File as debt, outside this batch. Read the IL from the assembly file (System.Reflection.Metadata PEReader / MethodBodyBlock) instead of the runtime MethodBody, so that a profiler's rewrite is never seen. Or run the test in a process the coverage collector does not instrument.

Verdict: The finding holds. It was there before this batch and is not round 10's: between 21a0298 and 79c6a7f, SeededComparerTests.cs and ArlingtonWalk.cs have an empty diff. No issue covers it: there is nothing in #320 to #333, #306 or the other open debt, nor in decisions.md or r1 to r10. CI runs on ubuntu-latest only.

The mechanism is right, but the scope is wider than the finding says. On Windows, Microsoft code coverage instruments dynamically. When a method is compiled, it replaces its IL, and RuntimeMethodInfo.GetMethodBody() then returns the rewritten IL. The probes it writes are 'ldc.i8 <address>; ldc.i4.1; stind.i1', and one follows each call. So one lands between 'call SeededComparer::get_Instance' and 'newobj'. WithoutProbes (SeededComparerTests.cs:3110-3172) recognizes only the static shape CI's Linux run writes ('ldsfld StaticManagedTrackerTemplate*...'). By the design comment at :826-834, a probe of any other shape refuses the table. As a result, every method of the library that builds a seeded table, or that reaches PdfName's interning table, is flagged once it has been compiled in the process before the reading runs. It is not only ArlingtonWalk..ctor, and not only after ArlingtonRuleTests. ArlingtonWalk..ctor is just the item AwesomeAssertions' BeEmpty prints ('at least one item', the first in GetTypes() order). After one PdfDocument.Open and Validate under --coverage there are 55 violations: PdfFileReader..ctor, PdfName..cctor, PageTreeWalk..ctor, PdfXRefTable.Add and others. In a full suite run nearly every class opens a document, so on Windows the CI coverage command fails almost every time. Without coverage, or with SeededComparerTests alone, the test passes. The test therefore depends on the machine and on the order the tests run in. It does not misjudge the library.

Verdict evidence: Branch head 79c6a7f, built Release in worktree wf_556f39fe-f5c-29 (0 warnings). Probes and outputs are under W/r11wip/verify-seeded-comparer-test-fails-under-ci-coverage-on-windows/.

(1) The two classes with --coverage --coverage-output-format cobertura (head-cov-2classes.txt): total 356, failed 1. The message is 'found at least one item {"AdCodicem.Pdf.Validation.ArlingtonWalk..ctor: Dictionary`2<Int32, Byte>..ctor"}'.
(2) The same two classes without coverage (head-nocov-2classes.txt): 356/356 pass.
(3) SeededComparerTests alone with coverage (head-cov-seededonly.txt): 203/203 pass.
(4) Probe ZzIlProbeTests.cs.txt, test ZzDumpArlingtonWalkCtor, run with coverage (zz-il-dump.txt). It printed 'CORECLR_ENABLE_PROFILING=1' and 'file IL 278 bytes, runtime IL before 278 bytes (same as file: True), after 603 bytes (same as file: False)'. In the rewritten IL, 'IL_0022: call SeededComparer::get_Instance' is followed by 'IL_0027: ldc.i8 / IL_0030: ldc.i4.1 / IL_0031: stind.i1 / IL_0032: newobj Dictionary`2[Int32,Byte]::.ctor'.
(5) Test ZzAllViolationsAfterOpeningAndValidating opens and validates one 3-object file, then calls CompiledRules.TablesBuilt through reflection. With coverage (zz-all-violations-cov.txt): '55 violation(s)', covering ArlingtonWalk..ctor x6, ObjectGraph, ObjectStreamDependencies, PageTreeWalk..ctor x19, OffsetImpreciseRule.Check, PdfName..cctor (ConcurrentDictionary&lt;String, PdfName&gt;), PdfFileReader.TryReadXRefChain / ReportStreamsPassedOver / ..ctor x21, PdfXRefTable.Add and ObjectStreamReports..ctor. The same test without coverage: '0 violation(s)'.
(6) The fix prototyped (fix-probe.diff): TablesBuilt reads the IL from the assembly file through PEReader and MethodBodyBlock, and falls back to GetMethodBody for a dynamic module. With coverage, the two classes pass 356/356 (fix-cov-2classes.txt), including all 203 SeededComparerTests (the emitted 'Probed' samples among them), and ZzAllViolations reports '0 violation(s)'.

The patch and the probe are reverted, and the worktree is clean.
fix: File as debt (area: tests), outside batch 19. In CompiledRules.TablesBuilt (SeededComparerTests.cs:2675), read each method's IL from the assembly image rather than from RuntimeMethodInfo.GetMethodBody(): use System.Reflection.Metadata PEReader over Module.Assembly.Location, MetadataTokens.Handle(method.MetadataToken), and GetMethodBody(rva).GetILBytes(), with RVA 0 meaning no body. A dynamic module (the emitted 'Probed' samples) keeps GetMethodBody. A profiler that rewrites a method when it is compiled then never changes what is read. WithoutProbes stays strict for the static shape CI writes, whether that shape is on disk or not. Update the comment at :826 and WithoutProbes' summary to say that the reading is of the assembly image. fix-probe.diff holds a working prototype.

Rejected alternative: recognizing 'ldc.i8 <n>; ldc.i4.1; stind.i1' as a probe. It widens what the reading skips, since a store of one byte at a constant address could be written in the source. It would also still depend on the instrumenter's shape, against the test's own fail-closed rule.
needs_maintainer: False

## 23. builder-summary-says-stream-12-begins-its-line — nit → confirmed nit (tests)
Location: tests/AdCodicem.Pdf.Tests/HybridFiles.cs:1788

Claim: The PassingOverAStreamAnOlderTableNamesPastWhiteSpace summary says both streams have 'its header beginning its line'. Where runBefore is "the stream", the 700 or 400 spaces of the run sit on stream 12's own line, before its header. Stream 12's line therefore starts at the run, before the bytes the newest /Prev's search reads, and the line test reads back a window for it.

Evidence: Dump W/r11wip/tests/dumps/dumps.txt (probe W/r11wip/tests/probes/ZzR11TestsProbe.cs.txt). ('the stream', 700): own 1062, run 362, byte before stream 12 is ' ', no end of line between 362 and 1062. ('the stream', 400): own 762, byte before is ' '. Rows with runBefore 'the oldest table': byte before stream 12 is '\n'.

Fix: Say 'stream 11's header beginning its line, stream 12's past the run on the same line where runBefore is "the stream"', or write the run with an end of line before stream 12.

Verdict: The claim holds. In the summary of HybridFiles.PassingOverAStreamAnOlderTableNamesPastWhiteSpace (tests/AdCodicem.Pdf.Tests/HybridFiles.cs:1786-1789, added in 5395eed), each stream of /Type /XRef has "its header beginning its line". Stream 11 always does, and so does stream 12 where runBefore is "the oldest table". Where runBefore is "the stream", the builder writes the run with file.Raw right after stream 11's "endobj\n", then stream 12's object with no end of line between them. So stream 12's line starts at Run, 700 or 400 bytes before its header, and the header is the first token on a line of spaces. It does not begin its line. The texts use the phrase strictly. At HybridFiles.cs:860-861 "its header beginning its line" is written where the header starts right after an end of line. PrevFallingInsideAStreamOnALongLine's summary (HybridFiles.cs:905-917) names a header whose "line starts before the bytes that search reads" as a separate shape, and the "the stream" rows are exactly that shape. The finding's extra claim follows from the code too. The newest table's /Prev names Own + 200, so its search window starts at Own - 313 (PdfFileReader.cs ~:5115, NearbySearchRadius 512 plus one byte of lead). Stream 12's line starts before that window, so IsTokenOnItsLine (PdfFileReader.cs:5401) finds no end of line in the bytes searched and reads back a window of up to LineStartSearchLength bytes. That record loop (~:5285) runs before ReportStreamsPassedOver's white-space flag is consulted. There is no behavioral consequence: lexed from its start, the line holds only spaces before the header, so the header is still a token on its line and stream 12 is recorded as before. The rows hold what the test claims. The r10 fix stage confirmed that ('the stream', true, 700, 256) fails on c2a2aaa and 2be5c44, which add the Warning. This is a wording slip in a test builder's summary, so it is a nit. No decision, filed issue or earlier finding holds it: decisions.md and r1-r10 findings never mention the phrase or this builder's layout.

Verdict evidence: Rebuilt independently on origin/ccr-6d6c0ee6-dqm4lm-188 at 79c6a7f. The probe is W/r11wip/verify-builder-summary-says-stream-12-begins-its-line/ZzR11VerifyBuilderSummaryProbe.cs.txt, run as an uncommitted test (build: 0 warnings, 0 errors), and its output is out.txt in the same folder. It calls the builder for the test's 9 rows, prints the layout, and runs each row's reading, both as built and with the byte before stream 12 turned into '\n'. Output for the "the stream" rows:
(the stream,True,700,256): stream 247 own 1062 run 362. stream 11: byte before = \n. stream 12: byte before = ' ', its line starts at 362 (700 bytes before its header, all spaces: True). newest /Prev search window starts at 749: line starts BEFORE it. As built: [xref.offset-adjusted@1502, limit.trailer@362, xref.section-missing@362]. Run ending with an end of line: identical.
(the stream,True,700,0): same layout. As built: [xref.offset-adjusted@1502]. With an end of line: identical.
(the stream,True,400,256): own 762 run 362. Byte before ' ', line starts at 362 (400 bytes before), window starts at 449: BEFORE. As built: [xref.offset-adjusted@1202, limit.trailer@362, xref.offset-adjusted@762]. With an end of line: identical.
All six "the oldest table" rows: stream 12's byte before = \n, its line starts at its header (0 bytes before).
Source: HandWrittenFile.Object writes "{n} 0 obj\n{body}\nendobj\n" (HandWrittenFile.cs:35). The builder writes `file.Raw(new string(' ', run))` and then file.Object(12, ...) (HybridFiles.cs:1811-1818).
fix: Correct the summary and leave the bytes as they are, so the rows keep the exact files the fix stage ran its failing rows and mutants on. For example, at HybridFiles.cs:1786-1789: "...then two cross-reference streams that index object 4 and themselves and give no /Prev, each of /Type /XRef: stream 11, at Stream, its header beginning its line, and stream 12, at Own, right after it, its header beginning its line too — or, where runBefore is "the stream", following on that line run spaces, at Run, which start the line before the bytes a search near the newest table's /Prev reads —, which a comment of dashes follows." The alternative is to write the run as run - 1 spaces and an end of line, so the summary holds as written. The probe shows the three "the stream" rows give the same codes and positions that way. It would drop this test's incidental pass through the line test's read-back, which the dedicated theory A_stream_passed_over_near_a_hybrid_table_s_prev_is_reported_only_where_its_line_starts_within_the_reach_of_the_line_test already covers. The text fix is the smaller change. Neither option needs a maintainer decision.
needs_maintainer: False

## 24. signed-header-unreadable-xref-stream-dropped-silently — major → confirmed major (batch: 21a0298..79c6a7f agains)
Location: src/AdCodicem.Pdf/IO/PdfFileReader.cs:5256-5259 (the Malformed candidate's acceptance: `(attempt.TypedXRef || Kind == Table) && IsTokenOnItsLine(...)`), with IsTokenOnItsLine/StartsToken at :5401-5440; the candidate comes from the walk back from `obj` (FindSectionCandidates, TryReadHeaderBackwards, ~:5495), which finds `+14 0 obj` at its first digit (#327)

Claim: A cross-reference stream that gives /Type /XRef and cannot be read (here an unknown filter, unreadable by R7-Q1), whose header is written with a sign ('+14 0 obj', which a load reads as object 14's header), is dropped by the search near an offset. The walk finds the header at its first digit, and the line test says no token starts there, because the lexer's token starts at the '+'. R1-Q1 says such a place is taken as the section found moved, reported unreadable, its numbers refused and its /Prev handed on. Instead the search takes the next readable section farther off, and the revision's objects are served from an older revision, or as null. Nothing reports the lost section: no section-unreadable, no filter.unsupported (it is rolled back with the dropped candidate), no rebuild. Main serves every object right. The same file with '014 0 obj', ' 14 0 obj' or the comment-parted '+14 0 %c\nobj' follows the decided rule on the branch. This is not round 10's change (c2a2aaa gives the same) but a batch regression: the line test for Malformed /Type /XRef candidates (round 3 [8]) meets #327's digit-only walk. It is a worse consequence than the one open point (b) and R10b's deviation state, which name only the missing passed-over Warning. #327 and the threat model's #327 entry do not name it either.

Evidence: Generator W/r11wip/batch/tools/hand_sign.py (W = C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud) writes W/r11wip/batch/hs/*.pdf. bad_sign_m40_n14.pdf (888 bytes): r0 classic table at 218 indexes objects 1-4, 4 = '(old four)'. r1: stream 13 at 387 (/Type /XRef, /Prev 218) indexes object 5. r2: objects 4 '(four, r2)' and 6 '(six, r2)', then at 575 `+14 0 obj` << /Type /XRef /Size 15 /W [1 4 2] /Index [4 1 6 1 14 1] /Filter /FooDecode /Root 1 0 R /Prev 387 /Length 21 >>. r3: classic table at 770 indexes object 7, `/Prev 615` (40 past r2's stream), startxref 770. Probe (W/r11wip/batch/probe-src/Probe.cs, cfg D, objects read in two orders, then the validator). Outputs in hs/out-{main,r9f,branch}.txt.
Branch 79c6a7f: `OPEN count=8 repaired=False after: count=8 repaired=False suppressed=0 kept=1`; `O 4: <6F6C6420666F7572>` ('old four'); `O 6: null`; only diagnostic `Repair xref.offset-adjusted at 387: The cross-reference section /Prev names at offset 615 was found 228 bytes before it.`; validator: file.size-wrong at 823 and 387, `Warning xref.section-shifted at offset 387`. c2a2aaa: identical. The /Prev at -40 (bad_sign_m-40, /Prev 535) gives the same, found 148 bytes before it.
Same bytes with `014 0 obj` (bad_zero_m40) on the branch: `Warning filter.unsupported at 704`; `Repair xref.offset-adjusted at 575 ... found 40 bytes before it`; `Warning xref.section-unreadable at 575: The cross-reference stream /Prev names at offset 615, found at offset 575, is there but cannot be read: its data could not be decoded. The chain goes on through its /Prev; ...`; `Repair xref.rebuilt`; objects 4 'four, r2' and 6 'six, r2'; `Error xref.section-malformed at offset 575`. bad_plain_m40 (' 14 0 obj') and bad_signcmt_m40 ('+14 0 %c\nobj') give the same.
Main 21a0298 on bad_sign_m40: `O 4: four, r2`, `O 6: six, r2`, filter.unsupported at 704, offset-adjusted at 576 (-39).
In the seeded corpus: W/r11wip/batch/gen/f01914_n20.pdf (tools/gen11.py, seed 202611081926, file 1914; r2 is a pure /Type /XRef stream `+15 0 obj` with /Filter [/FlateDecode 5]; r3's /Prev 100 bytes off it). The branch serves object 11 as null (expected r2's) with only two offset-adjusted reports and no rebuild. Its '0' twin (W/r11wip/batch/genz, GEN_SIGN=0, same bytes but the sign) takes it: section-unreadable, rebuild, object 11 right.

Fix: Treat a '+' immediately before the digit run as the header's first byte in the line test, as NumberEnd (2be5c44) and the comment path already do. Where the byte before `at` is '+' and `at` holds a digit, ask StartsToken at at-1: the lexer's integer token, sign included. Keep '-' rejected, since -N is no object number (#327). Equivalently, the walk could record a candidate at the sign, as #327's closing asks. Tests: hand_sign.py's bad_sign_m40 and bad_sign_m-40 read like bad_zero (section-unreadable at 575, Error section-malformed, rebuild, objects 4 and 6 from r2), with ok_sign_* as controls (readable, taken before any line test). Add a row for open point (b): a typed signed header passed over near a hybrid /Prev, unreadable, now reported as passed over. Texts: the IsTokenOnItsLine remark ('nor digits a regular byte before them runs into' must not cover a sign), the threat-model #327 entry (:504), and a comment on #327 giving the consequence and this reproducer. ADR 53 mutants on the new condition.

Verdict: The finding holds. I rebuilt it with my own generator and layout on 79c6a7f and on main 21a0298.

**What the branch does.** The search near an offset drops a cross-reference stream that gives /Type /XRef and cannot be read when its header is written '+11 0 obj'. The walk back from obj finds the header at its first digit. IsTokenOnItsLine then lexes the line and finds the integer token starting at the '+', so it says no token starts at the digit. The Malformed candidate's acceptance (PdfFileReader.cs:5256-5259) therefore refuses it. The search takes the next readable section farther off instead. The revision's objects are then served from the older revision or as null. Nothing reports the lost section: no section-unreadable, no filter report (it is rolled back with the dropped candidate), no rebuild. The only report is xref.offset-adjusted, naming the far section.

**Its unsigned and comment-parted twins follow R1-Q1 on the branch.** The same bytes written '11 0 obj', '011 0 obj' or '+11 0 %c\nobj' are taken as the section found moved, reported section-unreadable, rebuilt, and objects 4 and 6 come from r2.

**It is the batch's, and worse than main.** The line test on /Type /XRef candidates came in 2b26d18 (round 3 [8]), whose aim was to exclude a header in a comment or a string, and digits glued to a byte that makes another token ('x8 0 obj'). A '+' does not make another token: a load reads '+11 0 obj' as object 11's header (#327). So R1-Q1's rule ('declares itself a section', the header a token a load reads from its line's start, threat-model :434) is not applied to this header form.
- Main relocates to the digit. With /FooDecode over raw rows, main serves every object right.
- With undecodable Flate data, main serves objects 4 and 6 as null but reports filter.failed. The branch serves object 4 from the older revision and object 6 as null, with no Warning at all.

So the regression holds in both data shapes. The claim's 'main serves every object right' is true only of the reviewer's /FooDecode file.

**Nothing names this consequence.** #327, the threat model's #327 entry (:504), open point (b) and R10b's note ('never recorded as passed over') speak only of the scan, the search near a row, and the passed-over Warning. The IsTokenOnItsLine summary ('nor digits a regular byte before them runs into') and the relocation comment (:5243) cover '+' only literally, as a 'regular byte'. That misstates what a load reads, so a text misstates it.

**Not affected.**
- '-11 0 obj', dropped on the branch, is right by #327: no header.
- Readable signed streams are taken on both builds.
- The results do not depend on read order.

**Severity: major.** A wrong object is served, the decided rule R1-Q1 is not implemented for a header a load reads, and the branch is worse than main. The shape needs a signed object number, a /Prev that misses the stream, unreadable data, and an older section within 512 bytes.

Verdict evidence: **Files.** Generator W/r11wip/verify-signed-header-unreadable-xref-stream-dropped-silently/gen.py, where W = C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud. It writes 60 files in files/. Layout:
- r0: a classic table at 218 indexing objects 1-4 (4 = 'old four').
- r1: an older section at 386, either stream 10 (/Type /XRef) or a classic table, indexing object 5.
- r2: objects 4 'four r2' and 6 'six r2', then stream 11 at 572 (509 in the table_* files) with /Type /XRef /Index [4 1 6 1 11 1]. Its data is /FooDecode over raw rows, Flate garbage, or readable. Its header is written '11 0 obj', '+11 0 obj', '011 0 obj', '+11 0 %c\nobj' or '-11 0 obj'.
- r3: a classic table indexing object 7, whose /Prev names r2's header +30 or -12.

**Probe.** ZzVsuProbe.cs (same folder) opens each file at default limits, reads objects 1-15 forward and reversed, and runs the validator. Outputs are out-branch.txt (79c6a7f) and out-main.txt (21a0298, built in a temporary worktree, since removed). summ.py writes summary.txt.

**Branch, stream_foo_sign_d30** (/Prev 602, r2's stream at 572):
- 'D0 Repair xref.offset-adjusted @386: The cross-reference section /Prev names at offset 602 was found 216 bytes before it.'
- Sections: startxref Read 766; /Prev 602 Relocated Stream at 386; /Prev 218 Read.
- 'O 4: (old four)', 'O 6: null', 'O 11: null', repaired=False.
- Findings: F Warning xref.section-shifted at 386, plus two file.size-wrong.
- No other diagnostic.

**Branch, stream_foo_zero_d30** (same bytes, '011 0 obj'):
- 'Warning filter.unsupported @701'
- 'Repair xref.offset-adjusted @572: ... found 30 bytes before it.'
- 'Warning xref.section-unreadable @572: The cross-reference stream /Prev names at offset 602, found at offset 572, is there but cannot be read: its data could not be decoded. The chain goes on through its /Prev; ...'
- 'Repair xref.rebuilt'
- 'O 4: (four r2)', 'O 6: (six r2)'
- 'F Error xref.section-malformed at offset 572'

The plain and signcmt files give the same as zero. The d-12 files give the same pattern, and so do the table_* files (older classic table) and the flate_* files.

**Main, stream_foo_sign_d30:** sections /Prev 602 Relocated at 573; 4=(four r2), 6=(six r2), 11=stream; 'filter.unsupported@701', 'xref.offset-adjusted@573' ('found -29 bytes from there').

**Main, stream_flate_sign_d30:** 4=null, 6=null, 'filter.failed@703', 'xref.offset-adjusted@573'. The branch on the same file gives 4=(old four), 6=null, and only 'xref.offset-adjusted@386'.

**Minus header ('-11 0 obj'):** the branch drops it just as it drops '+11' (right by #327). Main took it.

**Readable controls (ok_*):** sign, zero and plain give the same on both builds (Relocated at 573 or 572, every object right).

No ORDERDIFF in any file.

**Origin.** `git log -S "attempt.TypedXRef || attempt.Kind == XRefSectionKind.Table" 21a0298..HEAD` and `-S "nor digits a regular byte"` both give only 2b26d18 (round 3, R3a).

**Texts checked.**
- threat-model.md:504 (#327 entry) names only '4 +0 obj' missed and '-4 0 obj' taken.
- threat-model.md:434 states the rule as 'the header of a stream that gives /Type /XRef, a token a load reads from the start of its line'.
- r10-fix-result.json R10b note: 'never recorded as passed over, because the number is not a token on its line'.
- #327's body: 'a load at an object's row reads 4 +0 obj and +4 0 obj as object 4's header'.

The main worktree is removed. The worktree probe is deleted. No remote corpus was copied.
fix: Fix it in this batch. No maintainer question is needed: this applies R1-Q1 and R9-Q2 to a header a load reads (#327), as round 10's [20] fixed the sign in NumberRunsInto within the batch.

**Preferred: record the place at the sign.** In FindSectionCandidates only, after TryReadHeaderBackwards finds a header at h:
- Where h-1 lies in the bytes searched and span[h-1] == '+', record h-1, the sign the lexer's token starts at, instead of h.
- Keep '-' as it is (no header, #327).
- Leave the scan and the search near a row to #327.

Then:
- TryReadXRefSection reads '+11 0 obj' there.
- IsTokenOnItsLine passes where the '+' starts a token, and still fails for 'x+11'.
- NumberEnd already skips the first byte.
- StartsWithin covers [+, end).
- The passed-over report takes the signed header, which also settles open point (b).

Costs to state:
- A readable signed stream's relocation is reported at the '+', one byte earlier than today and on main.
- NumberRunsInto's `candidate == streamStart + 1 && '+'` arm loses its walk case. Re-judge it, or remove it if nothing else reaches it, under ADR 53.

**Alternative: ask the line test at the sign.** In both IsTokenOnItsLine call sites, where span[at-1] == '+' and at holds a digit, ask StartsToken at at-1. Recording the passed-over place at the digit then also needs StartsWithin's range to start at the '+'. Without that, a link naming the stream's '+' exactly would not count as reaching it, and a false Warning would follow.

**Tests.**
- Rows from gen.py: stream_foo_sign_d30 and d-12, and stream_flate_sign_d30. Each must read as its '011' twin: section-unreadable at 572, Error section-malformed, rebuild, objects 4 and 6 from r2.
- The table_* variant (older classic table).
- '-11 0 obj' stays dropped.
- The ok_sign_* files as controls.
- A passed-over row for a typed, signed, unreadable header near a hybrid /Prev, now reported.
- Each row should fail on 79c6a7f.
- ADR 53 mutants on the new condition.

**Texts.**
- The IsTokenOnItsLine summary and the relocation comment (:5243): 'digits a regular byte runs into' must exclude a sign, whose integer token a load reads.
- threat-model :434 and the #327 entry (:504).
- The NumberRunsInto remark, if the arm changes.
- A comment on #327 with this reproducer and its consequence, so that its fix of the walk keeps the section search's handling.
- A status.md record: 2b26d18's line test dropped signed typed headers until this fix.
needs_maintainer: False
