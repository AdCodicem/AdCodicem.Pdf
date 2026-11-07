######## [1] sections:table-past-bound-at-chain-end-texts-say-every-other  reviewer=nit  refuter=narrowed/nit maint=False
LOCATION: src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:66-69; src/AdCodicem.Pdf/IO/PdfFileReader.cs:4505-4508 (ReportSectionReadInPart remarks) and :4599-4605 (table arm); docs/website/docs/reference/diagnostics.md:112; docs/website/docs/concepts/lazy-reading.md:162-167; docs/threat-model.md:431
CLAIM: Four texts written or widened in 8692234 say the same thing. Where a section's lost ranges pass 4,096, its own or once joined, the section refused every number, and the message then says every other object (every object where no row was kept) is looked for by rebuilding the index. The texts: the XRefSectionUnreadable summary ('every object neither its rows nor a section read before it index, where the section refused every number: ... or more than 4,096 ranges lost, its own or ...'), the ReportSectionReadInPart remark, diagnostics.md ('every number past 4,096 of them ... where it refused every number so, the message says every other object ...'), lazy-reading.md and threat-model.md:431. Accepted deviation 3 breaks this for one shape: a table that startxref or a /Prev names, which the end of the file cuts after more than 4,096 lost one-row subsections. Its table arm now gives the rest-of-table tail. At 8ba4589 the same file said 'every other object'. The message itself is true: the chain ends at the table, every number is refused either way (SubsectionsUnknown always calls RefuseEveryNumber), and the reader rebuilds, at opening under startxref and on demand under /Prev. So only the texts misstate this case. 8692234's body ('A table whose rest cannot be known that startxref or a /Prev names keeps its words') is also not true of this shape, whose words changed. That is for status.md's 'Commit bodies not rewritten' if wanted. Startxref reaches this shape only through a '%startxref N' comment, since TailSearchLength is 4,096 bytes. The /Prev shape needs nothing special.
EVIDENCE: Probe ZzR6SecProbe.ZzCutLong. Source: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r6sec/ZzR6SecProbe.cs.txt. Outputs: out-head.txt.cutlong, out-b8.txt.cutlong, out-main.txt.cutlong in the same folder. The file: one good row (5), then N one-row subsections '100+2i 1 / 0 0 x', then the end of the file. Results with N=4097:
- Head (65306d4), /Prev: 'The cross-reference table /Prev names at offset 413 was read only in part: the row for object 100, at offset 449, is not an offset, a generation and n or f; the rows read were kept, and what only the rest of the table, or the sections its trailer would name, index is looked for by rebuilding the index.' Then idx 3=none, rep0=False, 'obj 3=only in the oldest rep=True'.
- Head, startxref: the same tail, then xref.rebuilt at opening.
- 8ba4589, both cases: '... the rows read were kept, and every other object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows.'
- With N=4096, head and 8ba4589 both give the rest-of-table tail.
- Main 21a0298 gives no xref.section-unreadable.
A diff of every other probe group (streams, ranges, cut, readagain, allrefused) between head and 8ba4589 shows only these intended tail changes: no diagnostic added, dropped or reordered.
FIX: Docs only. In the four texts, set the exception beside the 4,096 clause, for example: 'but a table startxref or a /Prev names whose rest cannot be known, at which the chain ends, says that what only the rest of the table, or the sections its trailer would name, index is looked for'. Changing the code back is not worth it: for such a table the ≤4,096 and >4,096 shapes refuse alike and now read alike.
--- REFUTER REASON: The behavior reproduces. At 65306d4, a table named by startxref or a /Prev, cut by the end of the file after more than 4,096 lost ranges, gives the rest-of-table tail. At 8ba4589 the same file gave 'every other object'. The code is as decided: decisions.md, 'Review round 5 fixes', deviation 3 accepts exactly this, and [7]'s triage says to keep the SubsectionsUnknown tail of a table startxref or a /Prev names.

The claim needs narrowing in two ways.

(1) The texts are wrong only about the message's words, not about what happens. The chain ends at such a table, and RefuseRowsLost calls RefuseEveryNumber for SubsectionsUnknown or _rowsLostPast either way. So 'what only the rest of the table, or the sections its trailer would name, index' covers the same objects as 'every other object no section read before it indexes'. Every miss is rebuilt: object 3, found only in the oldest table, is not in the index, and it reads after the rebuild.

(2) The texts are paraphrases, not quotations. The code comment above the table arm (PdfFileReader.cs:4589-4595) states the exception exactly ('whatever it refused'). The comment at :4550-4555 already lists 'a table whose rest cannot be known' among the cases that refuse every number, with 'every other one' left to the rebuild. Yet a table at or under 4,096 ranges has carried the rest-of-table words since before round 5. So the batch's own texts already treat those words as meaning the same thing.

Still, diagnostics.md:112 ('where it refused every number so, the message says every other object no section read before it indexes is found so') puts this shape in its list ('every number past 4,096 of them'). The same goes for the ReportSectionReadInPart remark (:4505-4508), the XRefSectionUnreadable summary (:66-69) and threat-model.md:431 ('says ... that every object ... is looked for'). Each says the message speaks of every object, and for this shape it uses the rest-of-table words. lazy-reading.md:162-167 ('the report ... says so') holds loosely.

8692234's body says 'A table whose rest cannot be known that startxref or a /Prev names keeps its words'. That is not true of the case over 4,096, whose words changed. It belongs in status.md's 'Commit bodies not rewritten' record if wanted, not in a rewrite.

The shape needs more than 4,096 lost ranges that do not touch, plus a cut, so only a hostile file builds it. Main gives no xref.section-unreadable for it.
REFUTER EVIDENCE: I rebuilt the probe on origin/ccr-6d6c0ee6-dqm4lm-188 (65306d4), Release build: ZzCutLong copied as ZzR6RefCutLong, output in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r6ref-cutlong/out-head.txt.cutlong.

All four rows (startxref and /Prev, N=4096 and N=4097) end with '... the rows read were kept, and what only the rest of the table, or the sections its trailer would name, index is looked for by rebuilding the index.'

The 8ba4589 table arm, from git show, gives 'every other object' whenever _rowsLostPast is set:
`(_rowsLostPast || (section.SubsectionsUnknown && naming == "/XRefStm")) ? ", and every other object ..." : !section.SubsectionsUnknown ? ... : ", and what only the rest of the table, ..."`
That matches the reviewer's out-b8.txt.cutlong for N=4097.

The head arm is `section.SubsectionsUnknown ? (naming == "/XRefStm" ? EveryOtherObject : rest-of-table) : (everyRefused ? EveryOtherObject : rest-of-subsection)`.

RefuseRowsLost has `else if (_rowsLostPast || section.SubsectionsUnknown) RefuseEveryNumber();`, so every number is refused in both shapes.

On head, for N=4097, the index lacks object 3 ('idx 3=none'), 'obj 3=only in the oldest rep=True', and startxref rebuilds at opening.

The r5-fix-result.json deviation reads: 'a table named by startxref or /Prev that both loses more than 4,096 ranges and has unknown subsections now uses the rest-of-table tail instead of every other ... no test pinned that combination'. decisions.md accepts it as deviation 3.

diagnostics.md:112 also says, for a cut table, 'which the message names, saying what the lost trailer would name is looked for as well'. That is consistent with the message, and it pulls against the later 'every other object' sentence.
REFUTER FIX: Docs only, optional at nit level. Where a text speaks of the message's words, add the exception beside the 4,096 clause. In diagnostics.md:112, after 'every object where it kept no row', add: '; a table startxref or a /Prev names whose rest cannot be known, at which the chain ends, says instead what only its rest, or the sections its trailer would name, index — the same objects'. Add the same few words to the ReportSectionReadInPart remark (:4505-4508), the XRefSectionUnreadable summary and threat-model.md:431. lazy-reading.md can stay as it is.

Keep the code: for such a table the shapes at or under 4,096 and over 4,096 refuse the same numbers and now read alike, as deviation 3 accepted.

Optionally, record 8692234's 'keeps its words' under status.md's 'Commit bodies not rewritten'. Not a maintainer decision.

######## [2] sections:every-object-split-not-where-no-row-of-several-stands  reviewer=nit  refuter=narrowed/nit maint=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:4572 and :4585 (keptAndLacking in the '_' arm); docs/website/docs/reference/diagnostics.md:112 ('every object where it kept no row'); tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:2743 (test name)
CLAIM: Accepted deviation 1 says 'every object' where the stream kept no row and 'every other object' where it kept some, and diagnostics.md:112 says the same ('every object where it kept no row'). The code splits on RowsHeld instead: 0 rows, one row refused or not standing, and rows left to a guard get 'every object'. A stream that holds two or more rows, none of which stands (one refused by the stream itself, one whose number a newer section refused), gets '... kept but for those it refused and those whose numbers a newer section refused, and every other object ...'. That message is not false: 'other' than none kept is every object. It only departs from the documented rule. The test A_stream_read_in_part_whose_numbering_holds_a_reference_says_every_object_is_looked_for_whatever_rows_it_holds has a similar gap: its kept-row row asserts 'every other object'.
EVIDENCE: Probe ZzR6SecProbe.ZzAllRowsRefused, outputs out-*.txt.allrefused in the scratchpad folder r6sec. The shape: stream 6 under /Prev, /Index [4 1 6 9 0 R] with 9 -> 2. It holds row 4, type 2 in object stream 0, which the stream refuses itself, and row 6, whose number the newest table refuses by giving generation 65536.
- Head: 'it holds 2 rows where its /Index and /Size declare 3; the rows it holds were kept but for those it refused and those whose numbers a newer section refused, and every other object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows.' Then idx 4=none, idx 6=none, idx 1=none, rep0=True.
- 8ba4589: the same up to '... and the objects whose rows it lacks are looked for ...'.
- Main: no report.
- Direct /Index [4 1 6 2] control: the 'objects whose rows it lacks' tail on both commits.
FIX: Reword diagnostics.md:112, and the deviation as recorded, to the split the code makes. For example: 'every object where the stream holds no row, or its one row was refused or does not stand, or a guard left its rows unread'. Optionally rename the test, e.g. '..._says_every_object_or_every_other_one_is_looked_for_whatever_rows_it_holds'. Making the code count the held rows that stand would cost more than it gives.
--- REFUTER REASON: The documentation part holds, and it covers more shapes than the finding names. diagnostics.md:112 was written by this batch (8692234, round 5's [7]). It says the message gives 'every other object ... every object where it kept no row'. The code does not count the rows that stand. A stream's arms say 'every object' only where it holds no row, where its one row was refused or does not stand, or where a guard left its rows unread. A stream holding two or more rows, none of them standing, says 'the rows it holds were kept but for those it refused and those whose numbers a newer section refused, and every other object ...'. The table arm says 'every other object' too wherever the section refused every number itself, even when a newer section refused every row it read. So the documented rule fails in both shapes. The message is still true: nothing outside it is kept, and the objects rebuilt are the right ones. That leaves a wording mismatch in a text the batch wrote, so nit. The test-name part does not hold. 'A_stream_read_in_part_whose_numbering_holds_a_reference_says_every_object_is_looked_for_whatever_rows_it_holds' names a behavior: every object only an older section indexes goes to a rebuild (3 not indexed, WasRepaired true). Every row of the test holds that. Its comment states the wording split: 'every other object ... beside the row it kept, and every object, where it kept none'. Earlier rounds did not raise or settle this. decisions.md records only the deviation, in the same loose words, and r5-fix-result.json lists the RowsHeld split exactly (held 0, one refused, one not standing, left to a guard).
REFUTER EVIDENCE: I rebuilt the reviewer's probe as ZzR6RefAllRefused on 65306d4 (Release build 0/0). Output: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r6ref-allref-out-head.txt.allrefused
Shape: stream 6 under /Prev, /Index [4 1 6 9 0 R] where 9 is 2, holding rows 4 and 6. Row 4 is type 2 in object stream 0, refused by the stream. Row 6's number is refused by the newest table (generation 65536). Message: 'it holds 2 rows where its /Index and /Size declare 3; the rows it holds were kept but for those it refused and those whose numbers a newer section refused, and every other object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows.' Index: 1, 3, 4 and 6 none; WasRepaired true. This matches the reviewer's out-head.txt.allrefused, and 8ba4589's 'the objects whose rows it lacks' tail (out-b8.txt.allrefused).
Wider table shape (my ZzTableNoneKept, output .tablenone in the same place): a hybrid table refuses object 5's row (generation 65536), and its /XRefStm names a table 'xref\n5 1\n0000000000 00000 f \n' cut by the end of the file. Message: 'The cross-reference table /XRefStm names at offset 573 was read only in part: the file ends before its trailer; the rows read were kept but for those whose numbers a newer section refused, and every other object no section read before it indexes is looked for ...'. Idx 5 none, so it kept no row and still says 'every other'.
Code, PdfFileReader.cs:4572-4585: keptAndLacking is EveryOtherObject in the '1 =>' and '_ =>' arms. The table arm (:4596-4602) says EveryOtherObject whenever SubsectionsUnknown with /XRefStm, or everyRefused. Only the RowsHeld 0, 'RefusedRow' 1, 'someRefused' 1 and RowsLeftToGuard arms say EveryObject.
Other texts: threat-model.md:431 says 'every object ... but those its rows kept', which is accurate. lazy-reading.md has no split, and the code comments (':4566 the clause agrees with their count') are accurate. The body of commit 8692234 ('or every object where the stream kept no row') is loose in the same way. It is already pushed, so it is only worth a line in the status.md record.
REFUTER FIX: A documentation fix only; the code stays. Counting the held rows that stand would cost more than it gives, and the message is not false. In diagnostics.md:112, replace 'the message says every other object no section read before it indexes is found so, every object where it kept no row' with 'the message says every other object no section read before it indexes is found so, beside the rows it read — every object where a stream holds no row, its one row was refused or does not stand, or a guard left its rows unread —'. That states the split the code makes for streams and tables alike. Optionally, the status.md record of commit bodies not rewritten can note that 8692234's 'every object where the stream kept no row' also covers only those cases. No test rename is needed.

######## [3] window:probe-cost-one-probe-clause-conjoined  reviewer=nit  refuter=narrowed/nit maint=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:5179-5183 (PaddingAt remark); docs/threat-model.md:468-472 (#47 cost note)
CLAIM: The cost sentence states the ten-times bound unconditionally ('the bytes read come to less than ten times those from the position to that token's end ..., and to one probe where that token ends within the first, short of its edge'), and the threat model does the same ('less than ten times what it crosses ..., and one probe of 32 bytes where that token ends within it'). Joined with 'and', both clauses claim to hold. The first is false wherever the first token ends within 3 bytes of the position, because one 32-byte probe is then more than ten times what it crosses. That happens in the most common conforming case: startxref or a /Prev naming a cross-reference stream at its header, with an object number below 1,000. The bound holds wherever the probe grows; only the joining word is wrong.
EVIDENCE: Probe ZzR6WinMsg (copy at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r6win-ZzR6WinMsgTests.cs.txt; output r6win-msg-head.txt in the same folder) on 65306d4. A conforming file whose startxref names its stream at the header, reads logged at that offset:
- '12 0 obj': [32,206], first token ends at 2, so the probe reads 16 times what it crosses.
- '123 0 obj': [32,986], first token ends at 3, so 10.7 times.
- '1234 0 obj': [32,8766], first token ends at 4, so 8 times.
The 206, 986 and 8766 reads are the object's own window. Every shape that grows stays under ten times: 521 shapes in ZzR6WinCost (r6win-cost.txt; the violations list r6win-cost-bad.txt is empty), maximum 9.143.
FIX: Make the clauses alternatives. In PdfFileReader.cs: '... so that, where they grow, the bytes read come to less than ten times those from the position to that token's end — ... —, and one probe is read where that token ends within the first, short of its edge.' In threat-model.md:469-471: '... where it grows, less than ten times what it crosses up to the end of the first token past the offset, ..., and otherwise one probe of 32 bytes, where that token ends within it, short of its edge'. Wording only.
--- REFUTER REASON: The wording claim holds. On 65306d4, PaddingAt's remark (src/AdCodicem.Pdf/IO/PdfFileReader.cs:5179-5183) states the bound without a condition: "the bytes read come to less than ten times those from the position to that token's end ..., and to one probe where that token ends within the first, short of its edge". The threat model (docs/threat-model.md:468-472) does the same: "less than ten times what it crosses ..., and one probe of 32 bytes where that token ends within it". Read literally, both clauses claim to hold. The first is false wherever the first token ends 1 to 3 bytes past the position: one 32-byte probe is then at least 32/3 = 10.7 times what it crosses. The batch wrote both texts. On main, the threat model said "about nine times the run it crosses", and a2c5ed4 replaced that with the stronger claim. Round 5's triage chose the content ("less than ten times ..., one probe where ...") but never weighed this case. So this is not a re-report of a settled point. The code is unaffected: wherever the probe grows, the bytes read stay under 64/7 times what it crosses, and the one-probe case costs 32 bytes.

I narrowed one part. The reviewer called this "the most common conforming case", but a classic table named at its keyword is not affected: `xref` ends at byte 4 at the earliest, and 32/4 = 8. The failure needs a first token of 1 to 3 bytes. In a conforming file, that means a cross-reference stream named at its header (by startxref or a /Prev) whose object number has 1 to 3 digits. That case is common, but it is not the general one. No known issue (#289, #295, #331, #52) covers this.

This is wording only. It needs no maintainer decision.
REFUTER EVIDENCE: I wrote my own probe (copy at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/ref-ZzRefProbeOneTests.cs.txt, output at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/ref-probeone.txt) and ran it on origin/ccr-6d6c0ee6-dqm4lm-188 at 65306d4. It uses a counting source that logs each read at the offset startxref names. All files are conforming and produce no diagnostics. The last read in each list is the section's own window.
- stream 3 0 obj @110: asked [32,141], first token ends at byte 1 (32x)
- stream 9 0 obj: [32,184], ends at 1 (32x)
- stream 12 0 obj: [32,206], ends at 2 (16x)
- stream 99 0 obj: [32,817], ends at 2 (16x)
- stream 123 0 obj: [32,986], ends at 3 (10.7x)
- stream 999 0 obj: [32,7120], ends at 3 (10.7x)
- stream 1234 0 obj: [32,8766], ends at 4 (8x)
- classic table at its keyword, 0 spaces: [32,123], ends at 4 (8x)
- 1 space: [32,124], ends at 5
- 27 spaces: [32,150], ends at 31

This matches the reviewer's r6win-msg-head.txt lines 366-368 ([32,206], [32,986], [32,8766]).

The arithmetic: a probe grows from P to 8P only when the first token ends at or past P (PdfFileReader.cs, `first.End >= probe.Length`). So where it grows, the sum is below (64/7)·e, about 9.14e, for e the distance to the token's end. Where it does not grow, it is one probe of 32 bytes, and 32 < 10e fails exactly for e ≤ 3.
REFUTER FIX: State the two cases as alternatives.

In src/AdCodicem.Pdf/IO/PdfFileReader.cs:5181-5183: "... so that the bytes read come to one probe where that token ends within the first, short of its edge, and otherwise to less than ten times those from the position to that token's end — white space, comments and the token alike, a token the probe's edge cuts counted as far as the probe reaches —." The reviewer's form also works: "where they grow, ..., and one probe is read where ...".

In docs/threat-model.md:469-471: "... one probe of 32 bytes where the first token past the offset ends within it, short of its edge, and otherwise less than ten times what it crosses up to that token's end, a token its edge cuts counted as far as it reaches, ...".

No code or test change is needed. This is not a maintainer choice.

######## [4] window:memo-remark-candidate-probe-one-window  reviewer=nit  refuter=confirmed/nit maint=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:347-348 (_paddingProbes remarks; written in 2b26d18, round 3)
CLAIM: The remark says each place a search near an offset tries 'starts at a token — a keyword, an object's header —, whose probe is one window of XRefProbeLength bytes'. A header candidate whose object number is written with 32 or more digits grows the probe, because TryReadHeaderBackwards judges a run by its value however many zeros lead it (:8445-8455). The object number is then the first token, and the probe's edge cuts it. The cost stays bounded: the number lies within the search's 1 KB window, so the probe reads at most 2,336 bytes for a candidate, and MaxRelocationCandidates (32) bounds the candidates for the whole document. Only the stated window count is false.
EVIDENCE: Probe ZzR6WinCand (/tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r6win-ZzR6WinCandTests.cs.txt; output r6win-cand.txt) on 65306d4. A /Prev names garbage at 110, and object 9 sits at 130 with its number zero-padded to N digits. Reads at the candidate:
- 1 digit: [32,3158]
- 31 digits: [32,3188]
- 32 digits: [32,256,3189]
- 40 digits: [32,256,3197]
- 300 digits: [32,256,2048,3457]
The last read in each list is the object window. The diagnostics are [xref.section-missing] in every row.
FIX: Reword to '... whose probe is one window of XRefProbeLength bytes, or a few within the search's own window where a header's number runs past it, MaxRelocationCandidates bounding them'. Or drop the window count and keep the reason they are not kept: they are reached through _sectionsReadAt, and keeping them would push out the hybrid /Prev's probe.
--- REFUTER REASON: The finding holds as stated. The remark on _paddingProbes (src/AdCodicem.Pdf/IO/PdfFileReader.cs:347-348) was added in 2b26d18 (round 3); main does not have it. It says each place a search near an offset tries "starts at a token — a keyword, an object's header —, whose probe is one window of XRefProbeLength bytes". That is true for an xref keyword (4 bytes). It is also true for a header whose object number has 31 digits or fewer. It is false when the number has 32 digits or more: TryReadHeaderBackwards judges a digit run by its value, however many zeros lead it (:8445-8455), so the run is accepted. PaddingAt then grows its probe whenever `first.End >= probe.Length` (:5214). A 32-digit number ends exactly at the 32-byte edge, so it already grows the probe.

The cost stays bounded:
- The header's `obj` has to lie inside the search window (NearbySearchRadius = 512, so the window is about 1 KB). The number is therefore shorter than 2,048 bytes, and the probe reads at most 32 + 256 + 2,048 = 2,336 bytes for one candidate.
- MaxRelocationCandidates (32) bounds the candidates for the whole document.
- No report changes.

The remark's conclusion still holds: a section found there is reached through _sectionsReadAt, and keeping candidates would push out the hybrid /Prev's probe. Only the window count is overstated, and only for an object number zero-padded to 32 digits or more. The threat-model #47 bullet (docs/threat-model.md:466-472) is conditional ("one probe of 32 bytes where that token ends within it, short of its edge"), so it is correct. No earlier round raised this point: round 3's [3] refuter wrote the "one 32-byte window" premise, and no round questioned it after that. I keep the severity at nit because this is wording in a private member's remarks, with no behavior or cost consequence beyond a bound that is already stated elsewhere.
REFUTER EVIDENCE: I wrote my own probe, ZzRefMemoProbe, on 65306d4 (origin/ccr-6d6c0ee6-dqm4lm-188). A copy is at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r6ref-memo/ZzRefMemoProbeTests.cs.txt and the output at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r6ref-memo/out.txt.

The file: a classic table's /Prev names "not a section at all" at 110. At 131 (the place) the search finds either the word xref (digits=0) or `0…07 0 obj 42 endobj`, the number zero-padded to N digits. A 2,000-byte comment follows. The probe records every read the source gets at the place; the last read in each list is the object or table window:
- digits=0: [32,2150], diag [xref.offset-adjusted@131 | xref.section-unreadable@131]
- digits=1: [32,2154], diag [xref.section-missing@110]
- digits=31: [32,2184]
- digits=32: [32,256,2185]
- digits=33: [32,256,2186]
- digits=255: [32,256,2408]
- digits=256: [32,256,2048,2409]
- digits=900: [] (the header's obj lies past the 1 KB window, so the place is not a candidate)

The diagnostics are the same in every header row (xref.section-missing@110). This matches the reviewer's ZzR6WinCand output: 31 digits [32,3188], 32 digits [32,256,3189], 300 digits [32,256,2048,3457].

Code: PaddingAt :5214 `if (first.End >= probe.Length && probe.Length == probeLength && probeLength < reach)` grows the probe 8x. TryReadHeaderBackwards :8445-8455 parses the run by value, so leading zeros are accepted.
REFUTER FIX: This is wording only, with no code change and no maintainer decision. At src/AdCodicem.Pdf/IO/PdfFileReader.cs:347-348, replace "whose probe is one window of <see cref="XRefProbeLength"/> bytes" with "whose probe crosses no white space: one window of <see cref="XRefProbeLength"/> bytes where that token ends short of its edge, and at most a few more within the search's own window where leading zeros carry a header's number past it, which <see cref="MaxRelocationCandidates"/> bounds". The reviewer's alternative also works: drop the count and keep only the reasons candidates are not kept (they are reached through _sectionsReadAt, and keeping them would push out the hybrid /Prev's probe). No test is needed, and docs/threat-model.md's #47 bullet already words it correctly.

######## [5] window:reachedat-remark-dangling-which  reviewer=nit  refuter=confirmed/nit maint=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3660-3662 (ReachedAt remarks; f0d90c8, round 4)
CLAIM: A rewrap left the word 'which' alone on its own XML-doc line, between 'for each offset the chain names,' and '<see cref="PdfReaderLimits.MaxXRefSectionCount"/> bounds'.
EVIDENCE: git blame -L 3659,3663 gives 3660-3661 from f0d90c87 and 3662 from d63f093f. Line 3661 reads '    /// which'. No line on main 21a0298 matches '^    /// which$'.
FIX: Rewrap the three lines, for example '... for each offset the chain names, which <see cref="PdfReaderLimits.MaxXRefSectionCount"/> bounds — the probe ...'.
--- REFUTER REASON: The finding holds as stated. At 65306d4, line 3661 of src/AdCodicem.Pdf/IO/PdfFileReader.cs is just `    /// which`, sitting inside the ReachedAt <remarks> between line 3660 (124 columns, ending '...for each offset the chain names,') and line 3662 (129 columns, starting '<see cref="PdfReaderLimits.MaxXRefSectionCount"/> bounds'). It came from f0d90c8, a round 4 fix that was inside round 5's range b4ab948..8ba4589. That commit lengthened the old line 'further than a trailer's, for each offset the chain names, which' and pushed 'which' onto a line of its own without rewrapping the next one. Main 21a0298 has no such line. r1-findings.md to r5-findings.md and decisions.md never report it: round 4's rewrap findings ([15]/[20]) were about WindowEdgeTests.cs and PdfDiagnosticCodes.cs, not this remark. None of #289, #295, #331 or #52 covers it. The finding is no wider than this one line: across the batch's whole diff of src/ and tests/, it is the only added comment line that holds a single bare word. The fault is wrapping only. The sentence reads correctly once the lines are joined, so the doc does not misstate the code.
REFUTER EVIDENCE: `grep -n '^    /// which$' src/AdCodicem.Pdf/IO/PdfFileReader.cs` at 65306d4 gives 3661. `git blame -L 3657,3664` puts lines 3660 and 3661 on f0d90c87 ('fix: read a named section from its first token, its white space apart') and lines 3662 and 3663 on d63f093f. In f0d90c87's diff, '-    /// further than a trailer's, for each offset the chain names, which' is replaced by '+    /// further than a trailer's and the few bytes past it that tell what starts at its end, for each offset the chain names,' followed by '+    /// which'. On 21a0298, `git show 21a0298:src/AdCodicem.Pdf/IO/PdfFileReader.cs | grep -n '^    /// which$'` finds nothing. f0d90c87 lies between b4ab948 and 8ba4589. `git diff 21a0298 HEAD -- src tests | grep -nE '^\+\s*(///|//) [a-z,]+$'` finds only this one line. Line widths for 3657 to 3663 are 121, 127, 119, 125, 13, 129 and 82, so the file wraps these comments at about 120 to 129 columns.
REFUTER FIX: Rewrap lines 3661 to 3663 of PdfFileReader.cs and leave the wording as it is: `    /// which <see cref="PdfReaderLimits.MaxXRefSectionCount"/> bounds — the probe the section's own reading there then answers` / `    /// from, rather than crossing the white space again (<see cref="_paddingProbes"/>).` This touches a comment only, so it needs no mutant and no test, and no maintainer decision. It can ride with any round 6 fix commit, for example as a `docs:` or `refactor:` change.

######## [6] short-reads:tm52-longer-short-reads-spend-the-search-budget  reviewer=minor  refuter=narrowed/minor maint=False
LOCATION: docs/threat-model.md:368-369 (the [#52] known-gap entry, sentence written by f85a814)
CLAIM: The new clause 'while a read of more, short or not, leaves the search as whole reads leave it' is false. Each window in the loop adds every byte it returns to _endStreamSearchBytes (PdfFileReader.cs:2551). A read of 11 bytes moves the search on 1 byte and repeats 10, so a run of 11-byte reads counts 11 times what whole reads count. Reads of 12 count 6 times, and reads of 13 count 4.3 times. That spends the document's EndStreamSearchPasses budget (4 x file length). Once it is spent, NextSignificant (PdfFileReader.cs:2048) returns -1. NextSignificant is used by CheckEndStreamOn, which judges what follows the keyword. That judgment is part of the same search's StreamEndSearch result, built at :2199. Every later search then has readsFile false. The data end of the search that made the short reads stays as whole reads leave it, and the SearchEndStream remark (data end and endObjSeen) is exact. But the endobj judgment of that same stream changes, and so do later streams' data ends. The behavior is the same on main 21a0298 (#52, pre-existing). The batch's text is what overclaims: it describes the #52 gap as narrower than it is.
EVIDENCE: Probes in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r6sr/probes (ZzR6BudgetTests.cs, ZzR6BudgetRuleTests.cs); outputs budget-one.txt, budget-two.txt, budget-rule.txt next to them. The source caps only the reads that start in object 5's data.
(1) One stream, data '...20,000 A' + '\nendstream\nfoo\n12 0 obj', file 24,522 bytes:
- whole: 'RawLength 20000, IsEndObjMissing True, object.endobj-missing fires True'.
- cap 11 (11,856 short reads): 'RawLength 20000, IsEndObjMissing False, object.endobj-missing fires False'.
- cap 12: same as whole.
- With object 6 padded to 100,000 bytes (a larger budget), cap 11 gives SAME. So the budget is the cause.
(2) Two streams, 5 (30,000 bytes of data) then 7 (9,000), each followed by '\nendstream\nendobj', file 43,559 bytes. Caps apply to stream 5's reads only:
- whole: '5: 30000, 7: 9000 [stream.length-invalid ... ended after 30000. | stream.length-invalid ... ended after 9000.]'.
- cap 11: '5: 30000, 7: 9018 [... | stream.length-invalid:The stream declared 9999999 bytes, and no endstream follows its data in the bytes read with its object; the 9018 bytes up to the next object are taken as its data without a search, the document's searches for endstream having read as much of the file as they may.]'.
- With stream 5 at 60,000 bytes, cap 12 gives the same 9018 output; caps 13, 14 and 40 give SAME.
(3) Main 21a0298 (src extracted with git archive, the same probes) gives byte-identical outputs for (1) and (2).
(4) The sweep (sweep-caps.txt, 31,122 runs) shows the same thing at cap 11 for every shape whose keyword is not followed by endobj, at fillers 20,000..20,031: 32/91 differ, data length equal, missing True vs False.
FIX: Reword the clause to what holds, for example: '... while a read of more, short or not, finds what whole reads find, the data ending where they end it — though each read counts the ten bytes it repeats, so that reads of eleven to thirteen bytes count up to eleven times what whole reads count toward the passes the searches may read (EndStreamSearchPasses): what follows the keyword may then go unread, its endobj unjudged, and a later stream unsearched —'. Optionally add a comment on #52 with reproducer (2). A code alternative would count only the bytes a window adds past the one before, but that touches #52's design and is not needed for the text.
--- REFUTER REASON: The finding holds, with one narrowing. The clause at docs/threat-model.md:368-369, "while a read of more, short or not, leaves the search as whole reads leave it", is new in f85a814. Neither 8ba4589's [#52] entry nor main's has it. It is false as written.

The window loop counts every byte a read returns toward `_endStreamSearchBytes` (PdfFileReader.cs:2553). It then moves on only `Math.Max(1, span.Length - EndStreamSearchOverlap)`. So a read of n > 10 bytes counts n/(n-10) times what it moves the search on: 11 times at 11 bytes, 6 at 12, 4.3 at 13, 2 at 20, 1.33 at 40. Whole reads count about 1. This spends the shared EndStreamSearchPasses budget sooner.

The search that makes the short reads still ends its data where whole reads end it. Its loop never checks the budget, and its `endObjSeen` is unchanged. So the SearchEndStream remark, the StreamEnds and PendingEndStream remarks, and the WindowEdgeTests comment, which all speak of the data end, are accurate. But once the budget is spent:
- NextSignificant returns -1 (:2048). So CheckEndStreamOn leaves what follows that same stream's keyword Unseen, and the StreamEndSearch built at :2199 loses its endobj judgment.
- Every later search runs with `readsFile` false, and its stream is taken without a search.

The behavior is the same on main 21a0298. Main has the same `+= span.Length` and the same `Math.Max(1, ...)` step (main :2381, :2399), so the behavior is #52's. But the brief asked to check that the newly stated threshold text is true, and the batch's text describes the gap as narrower than it is. The round 5 refuter of [16] had already noted this caveat ("a short read still counts every byte it repeats ... the shared EndStreamSearchPasses budget runs out sooner for later searches"), and the fix stage wrote the broader claim anyway. #52's body and comments do not hold the budget effect.

Narrowing: the reviewer's probe (1) also capped CheckEndStream's own read at the data's end (its cap range reaches past the keyword). With only the search's data reads capped, '\nendstream\nfoo' is unchanged, because CheckEndStream's first 64-byte read decides Absent with no NextSignificant. For that same stream's endobj judgment to change, its keyword must be followed by white space longer than that first read, or the read at the keyword must be short as well. My variant shows the first case. Probe (2), a later stream left unsearched, is clean: that stream's reads are whole.

Severity is minor: a known-gap entry in the threat model understates the gap. Nothing behaves worse than on main.
REFUTER EVIDENCE: Rebuilt on origin/ccr-6d6c0ee6-dqm4lm-188 at 65306d4 (Release, 0 warnings). I ran the reviewer's probes with their output redirected to /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r6ref-budget/. Outputs are identical to theirs:
- budget-rule.txt: whole 'RawLength 20000, IsEndObjMissing True, object.endobj-missing fires True'. Cap 11: 'RawLength 20000, IsEndObjMissing False, object.endobj-missing fires False'. Cap 12: same as whole.
- budget-two.txt, filler 30000/9000, file 43559: whole '5: 30000, 7: 9000 [... ended after 30000. | ... ended after 9000.]'. Cap 11: '5: 30000, 7: 9018 [... | stream.length-invalid:The stream declared 9999999 bytes, and no endstream follows its data in the bytes read with its object; the 9018 bytes up to the next object are taken as its data without a search, the document's searches for endstream having read as much of the file as they may.]'. Caps 12, 13, 14 and 40: SAME. At filler 60000/9000, cap 12 differs too.

My variant, /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r6ref-budget/ZzR6RefBudgetTests.cs.txt, output ref-dataonly.txt next to it. It caps only the reads that start in [dataStart, dataStart+20000), the search's data reads, and logs Reader.EndStreamSearchBytes:
- '\nendstream<80 spaces>foo\n12 0 obj', file 24601, budget 98404. Whole: 'len=20000 missing=True', searchBytes 12166.
  - cap 11: searchBytes 130405, 'DIFF len=20000 missing=False'.
  - cap 12: 71328, SAME. Cap 13: 51568, SAME. Cap 40: 16032, SAME.
  - With padding 100000 (budget 482024), cap 11 gives SAME. So the budget is the cause.
- '\nendstream\nfoo\n12 0 obj' with the same data-only cap: SAME at every cap (searchBytes 130405 at cap 11 against 11895 whole). CheckEndStream's first read decides without reading on, which is the narrowing above.

Code: PdfFileReader.cs:2553 '_endStreamSearchBytes += span.Length;' and :2583 'position + Math.Max(1, span.Length - EndStreamSearchOverlap)'. Main 21a0298 has the same at :2381 and :2399. `git show 8ba4589:docs/threat-model.md` [#52] has no 'leaves the search as whole reads leave it'.
REFUTER FIX: A docs-only change to docs/threat-model.md's [#52] entry. Replace 'while a read of more, short or not, leaves the search as whole reads leave it' with something like: 'while a read of more ends the data where whole reads end it, though each read counts the ten bytes it repeats toward what the searches may read together (`EndStreamSearchPasses`), eleven times what whole reads count at reads of eleven bytes and twice at twenty, so that the reads on past its keyword, and the streams searched after it, may stop sooner'. Keep the rest of the sentence.

Optionally, in the end-of-batch #52 comment, add reproducer (2): a later stream is taken without a search after an earlier one's 11-byte reads. That comment could also correct the posted comment's 'the next window starts one byte past the short one's start' to reads of ten bytes or fewer.

A code alternative is to count only the bytes each window adds past the one before. It would make the clause true as written, but it changes #52's ground and is not needed. Not a maintainer decision.

######## [7] short-reads:comment-keyword-exception-needs-a-whole-window  reviewer=nit  refuter=confirmed/nit maint=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:2470-2471 (SearchEndStream remark, sentence restated by f85a814); docs/threat-model.md:369-370
CLAIM: The remark says reads of 10 bytes or fewer may miss 'a keyword or a header no window then holds whole ... — but for a keyword in a comment that waits, which is decided as whole windows decide it'. The threat model says 'an endstream in a comment that waits is decided as whole reads decide it'. The exception holds only once some window holds the keyword whole, which needs reads of 9 bytes or more across it. Reads of 8 bytes or fewer, or a source whose reads stop at an offset inside the keyword, miss a keyword in a comment like any other. The search then ends elsewhere, such as at the next header. The 'but for' reads as if such a keyword cannot be missed. What is exact is that once a keyword waits, its decision does not depend on the reads: every cut outside the keyword gives the whole-read result.
EVIDENCE: ZzR6ShortReadTests.ZzSweepCaps and ZzR6CutTests.ZzCutAtOffsets; outputs sweep-caps.txt and cuts.txt in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r6sr.
- Shape '\n12 % endstream\n0 other\n12 0 obj', filler 8141, cap 8: whole 'len=8147 missing=True [stream.length-invalid:The stream declared 9999999 bytes but ended after 8147.]'; capped 'len=8165 missing=True [stream.length-invalid:The stream declared 9999999 bytes, and no endstream follows its data before the next object, at 8328; the 8165 bytes up to that object are taken as its data.]'. Caps 4 to 8 give the same result.
- The same shape at filler 8195, with reads stopping at cuts 7..14 (inside the comment's endstream): 'len=8219 ... the 8219 bytes up to that object are taken as its data', against whole 8201. Every other cut gives the whole-read result.
- '\n12 % endstream\r\nendstream...', cuts inside the first keyword: 8210 (the second keyword) against 8201.
FIX: Bound the exception to a keyword some window holds: '— but for a keyword in a comment that waits, once a window holds it whole, which is then decided as whole windows decide it'. In the threat model: 'and an endstream in a comment that waits, once a read holds it whole, is decided as whole reads decide it'.
--- REFUTER REASON: The behavior reproduces exactly as the finding states, and the texts are ambiguous in the way it says. Both are the batch's own: cfaa90c added the clause and f85a814 restated it. PendingEndStream does not exist on main, so there is no regression to compare against.

What holds in the code:
- Once the search has found a keyword in a comment and it waits, its decision does not depend on the reads after it. Caps of 9 bytes and more, and every cut outside the keyword, give the whole-read result.
- A keyword that no window holds whole is never found, so it never waits. Reads of 8 bytes or fewer (9-byte windows are the smallest that can hold `endstream`), or a source whose reads keep stopping at an offset inside the keyword, miss a keyword in a comment just as they miss any other. The search then ends at the next header, or the object's `endobj` ends the data.

Why the texts mislead:
- In the SearchEndStream remark, the clause comes right after "a keyword or a header no window then holds whole ... may be missed, and the search may end elsewhere". It begins "but for", so it reads as an exception to "may be missed".
- The same remark earlier says that this whole category waits ("a first endstream inside a comment, on a line whose tokens may begin a header, which waits"). So a reader takes "a keyword in a comment that waits" as that category, and concludes short reads cannot miss it.
- The threat model's #52 entry ("an `endstream` in a comment that waits is decided as whole reads decide it") is open to the same reading.
- Read strictly, with "that waits" meaning already found, both texts are true. But then the exception covers only what follows the keyword, which neither text says.

Why this is not already settled:
- #52 holds the behavior, not this wording.
- Round 5 [9]'s reviewer data already showed caps 1 to 8 differing for keywords in a header's comment. But neither [9] nor [16], which wrote the present sentence, looked at the scope of the "but for" clause.
- Round 5 [16]'s refuter probe used a comment keyword inside a complete header (`12 % endstream\n0 obj`). There, missing the keyword changes nothing, so the gap did not show.

Severity: nit. Only the wording is at stake, and nothing behaves wrongly. No maintainer decision is needed.
REFUTER EVIDENCE: My own probe ran on origin/ccr-6d6c0ee6-dqm4lm-188 at 65306d4, in a Release build with 0 errors. It is saved at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/ZzRefCommentWaitTests.cs.txt and its output at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refcw-out.txt.

The file: object 5 is `<< /Length 9999999 >>` stream, then filler 'A' bytes, then the tail. Reads go through ShortCountSource, with caps applied only inside the data.

Tail `\n12 % endstream\n0 other\n12 0 obj`:
- Filler 8141, whole read: `len=8147 [stream.length-invalid: ... but ended after 8147.]`
- Caps 4, 7 and 8: `len=8165 [stream.length-invalid: ... no endstream follows its data before the next object, at 8312; the 8165 bytes up to that object are taken as its data.]`. The finding's 8328 is 16 bytes further because its catalog also carries `/Metadata 5 0 R`.
- Caps 9, 10, 11, 12 and 40: SAME.
- Reads that keep stopping at a cut:
  - kw-3 to kw+0: SAME.
  - kw+1 to kw+8, inside the keyword: 8165.
  - kw+9 to kw+12: SAME.
- A single cut, where only one read is cut, at any of kw-3 to kw+12: SAME.
- Filler 8195: whole 8201. Caps 4 to 8 and cuts kw+1 to kw+8, which are the finding's cuts 7 to 14, give `len=8219 ... the 8219 bytes up to that object are taken as its data`.
- Filler 20000: whole 20006. Caps 4 to 8 and cuts inside the keyword give 20024.

Tail `\n12 % endstream\r\nendstream\nendobj\n12 0 obj`:
- Filler 8195: whole 8201. Cuts inside the first keyword give `len=8210 [stream.length-invalid: ... ended after 8210.]`, the second keyword's place.
- Caps 4 to 8 miss both keywords: `len=8221 [stream.truncated: ... its object's endobj ends it, after 8221 bytes.]`.

Texts:
- PdfFileReader.cs:2471 reads "... and the search may end elsewhere — but for a keyword in a comment that waits, which is decided as whole windows decide it." `git log -S` traces the clause to cfaa90c.
- docs/threat-model.md:369-370 reads "... and an `endstream` in a comment that waits is decided as whole reads decide it."
REFUTER FIX: Wording only; no code or test change.

1. PdfFileReader.cs:2470-2471 (SearchEndStream remarks). Make the exception cover the decision, not the finding of the keyword: "... may be missed, a keyword in a comment among them, and the search may end elsewhere — but once a window has held a keyword in a comment whole and it waits, it is decided as whole windows decide it, however short the reads after it." The finding's own wording ("but for a keyword in a comment that waits, once a window holds it whole, which is then decided as whole windows decide it") would also do.

2. docs/threat-model.md:369-370 (#52 gap): "... and an `endstream` in a comment, once a read has held it whole and it waits, is decided as whole reads decide it, however short the reads after it."

3. Optional, for consistency: threat-model row 541's "reads a source gives short before the end of the file deciding it as whole reads do" could say "the reads after it". It is not false as it stands, because there "it" is the waiting keyword's decision.

This goes into the next docs stage and needs no maintainer decision.

######## [8] short-reads:theory-comment-caps-below-four-run-past-object-12  reviewer=nit  refuter=confirmed/nit maint=False
LOCATION: tests/AdCodicem.Pdf.Tests/WindowEdgeTests.cs:803-804 (comment of c57c9ef's theory)
CLAIM: The comment says, without asserting it, that 'reads shorter than the keyword miss it, the data then running to the object's endobj'. That holds for reads of 4 to 8 bytes: the data ends at object 5's own endobj, filler + 10 or 11. Reads of 1 to 3 bytes also miss the obj of the following '12 0 obj' header, because FindHeader needs obj and the byte after it in one read. The data then runs past object 12's header to object 12's endobj, filler + 35 or 36. The reader still reports that as 'its object's endobj'.
EVIDENCE: ZzR6TheoryLowCapsTests (theory-lowcaps.txt), the theory's own file shape and fillers windowEdge+30..+59:
- LF: cap 1, 2, 3 give 'filler+35 x30', e.g. 'filler 8185: len=8220 ... stream.truncated: ... before the next object, at 8391: its object's endobj ends it, after 8220 bytes.'. Cap 4, 7, 8 give 'filler+10 x30'. Cap 9 gives filler+1 and cap 10 gives filler+0.
- CRLF: caps 1-3 give filler+36, caps 4-8 filler+11, cap 9 filler+2 and cap 10 filler+1.
FIX: '... and reads shorter than the keyword miss it, the data then running to the object's endobj, or, where they are shorter than obj and the byte after it, past object 12's header to that object's endobj.' Or drop the clause on reads shorter than the keyword.
--- REFUTER REASON: The finding holds. Round 5's commit c57c9ef added the comment at tests/AdCodicem.Pdf.Tests/WindowEdgeTests.cs:797-805; the theory did not exist at 8ba4589. The comment opens with 'Object 5 ...' and then says that 'reads shorter than the keyword miss it, the data then running to the object's endobj'. That is true for caps 4 to 8: the data ends before object 5's own endobj. It is false for caps 1 to 3. HeaderText.FindHeader (src/AdCodicem.Pdf/IO/HeaderText.cs:221-245) only takes a header when obj and the byte after it are whole in one read, and its own remark says so. A read of 3 bytes or fewer can never hold those 4 bytes, so the '12 0 obj' header in the capped stretch is never found. The search then runs on to the next object, object 6. The data takes in object 12's header and its '(twelve)' and ends at object 12's endobj, filler + 35 (LF) or filler + 36 (CRLF). The reader still reports that as 'its object's endobj ends it'. This is not the #52 gap re-reported. The brief keeps in scope a text the batch wrote that misstates a known gap, and this comment is one, but only for a behavior the test explicitly says it does not hold ('not held here'). No other src, docs or test text makes this claim: grep finds only this comment. The other statements in the same sentence hold. Reads of 10 leave the CR in the data: CRLF filler+1, LF filler+0. Reads of 9 leave the LF as well: CRLF filler+2, LF filler+1. Caps 11 and 12 match the whole read, filler+0. So this is a nit: a test comment that is inexact for 3 of the 8 caps it describes. It does not affect the code, the reports, or what the test asserts.
REFUTER EVIDENCE: This is my own probe, independent of the reviewer's. It lived in tests/AdCodicem.Pdf.Tests/ZzRefLowCapsTests.cs and was removed afterwards. It copies HeaderFile with header = eol + "endstream\nendobj\n12 0 obj", padding TailSearchLength, and the theory's ShortCountSource predicate (offset > dataStart && offset < dataStart + filler + header.Length ? Math.Min(held, cap) : held), for fillers windowEdge+30..+59 and caps 1..12. It was run at 65306d4 (origin/ccr-6d6c0ee6-dqm4lm-188); the output is in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r6ref-lowcaps/out.txt.
LF:
- cap 1, 2, 3: filler+35 x30; e.g. filler 8185, dataStart 163: len 8220. Object 5's endobj is at 8359 and object 12's at 8384. Report: [stream.truncated: ... no endstream follows its data before the next object, at 8391: its object's endobj ends it, after 8220 bytes.] 8391 is object 6's header.
- cap 4 to 8: filler+10 x30. Report: '... before the next object, at 8366: its object's endobj ends it, after 8195 bytes.' 8366 is object 12's header.
- cap 9: filler+1.
- cap 10, 11, 12: filler+0.
CRLF:
- cap 1, 2, 3: filler+36. Report: '... at 8392: its object's endobj ends it, after 8221 bytes.'
- cap 4 to 8: filler+11.
- cap 9: filler+2.
- cap 10: filler+1.
- cap 11, 12: filler+0.
In every row endObjMissing=False. FindHeader at HeaderText.cs:236 only accepts obj when the byte after it lies in the same read (`after < _bytes.Length ? !IsRegular(_bytes[after]) : endsRange`). git blame puts lines 795-806 in c57c9ef8.
REFUTER FIX: Change only the comment at WindowEdgeTests.cs:803-804: '... and reads shorter than the keyword miss it, the data then running to the object's endobj, or, in reads of fewer than four bytes, which never hold obj and the byte after it, past object 12's header to that object's endobj, which the reader takes for its own.' Or drop the clause on reads shorter than the keyword, leaving only the 10-byte and 9-byte cases. Either way there is no code or test change, and the maintainer has nothing to decide.

######## [9] tests:readagain-copy-default-survives  reviewer=minor  refuter=narrowed/nit maint=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3383 (the readAgain copy); tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:4836-4919 (the only row pinning the copy)
CLAIM: Round 5's [8] copy is pinned on one side only. The new theory row covers the case where the stream's own first reading refused every number, so the copy must give false. Nothing covers the case where a newer section refused every number before the /XRefStm read the place, where the copy must keep true. Replacing the copied value with `false` at that one place survives every unit test. This is outside item 4's strict wording ("return the default value from a member"). It is inside the reading R5b applied when it mutated the limit.trailer message argument `_guard.Bound(...)` to 0. Nothing a caller sees changes: no report or rule reads the field on a record read again. But the internal record then says no newer section refused every number when one did, which is the half the [8] verdict called correct ("the copy keeps it true").
EVIDENCE: Mutant K2: `section.EveryNumberRefusedBefore = false;` in place of the copy, in a worktree at 65306d4 (script /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r6t-mut/k2full.sh). Whole unit suite without the remote corpus: total 7677, failed 0, succeeded 7657, skipped 20. The seven cross-reference classes: 2345 total, 0 failed (/tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r6t-mut/results.tsv). Reproducer: HybridFiles.PrevFallingShortOfTheStreamBefore("no row"), then appended `20 0 obj << /Type /XRef /Size 21 /Index /X /W [1 -4 2] /Prev 1770 /Length 0 >> stream endstream endobj` and a table naming it through /Prev, which startxref names. At head the records are: `S /Prev named=1903 Malformed Stream`, `S /Prev named=1770 Read Table enrb=True`, `S /XRefStm named=1614 NamedAgain`, `S /Prev named=1607 off=1614 Relocated Stream inc=False enrb=True rrb=True`. Under K2 the last one reads `enrb=False rrb=True`. Diagnostics are identical. Outputs: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r6t-dump/readagain-newer.txt and readagain-newer-K2.txt.
FIX: Add a row to A_prev_falling_short_of_the_stream_its_xrefstm_read_reads_it_whole_and_the_naming_is_reported, or a Fact beside it, with that shape: a lost stream whose /Index is no array between a newer table and the hybrid table. Assert that the /Prev's record (Sections[4]) has EveryNumberRefusedBefore true and RowsRefusedBefore true. Alternatively, record K2 in the ADR 53 record as equivalent from outside, with the reason that no report or rule reads the field on a section read again. The first matches what R4 did for the RowsRefusedBefore copy (M04).
--- REFUTER REASON: The facts reproduce. The copy at src/AdCodicem.Pdf/IO/PdfFileReader.cs:3383 is pinned on its false half only. Replacing it with `false` (K2) survives every test that reads the field. In the reviewer's shape, where a newer section refused every number before the /XRefStm read the place, K2 changes only the /Prev record's EveryNumberRefusedBefore, true to false. Head is correct there (true).

The finding is wider than the evidence in three ways.

1. Nothing is wrong with the fix, and [8] was implemented as decided. decisions.md "Review round 5" [8] asks for the field to be settable and carried beside the two copies, with "row asserting sections[2] false and the oldest true (kills the drop-the-copy mutant)". The row at CrossReferenceChainTests.cs:4836-4919 does that, and it fails without the copy (R5a B01, killed). The test claims nothing about the newer-section half, so it holds everything it claims. The line is covered. Neither "minor" case applies: no test fails to hold its claim and no member is uncovered.

2. K2 is outside ADR 53's catalog as R5a ran it on this line. The catalog's mutant for a statement is to remove it (item 3), and that one is killed. Item 4 returns the default from a member's body. K2 instead replaces one property read at one site, which is the call-site specialization ADR 53 rules out ("a helper is mutated once, in its body, and not specialized at each of its callers"). R5b replacing `_guard.Bound(...)` with 0 at a message site was its own wide reading. It does not widen the catalog for R5a.

3. Even inside the catalog, ADR 53 and CLAUDE.md say a survivor "is a gap in the tests, not a review finding". It is settled in the mutation step: the batch-wide ADR 53 step, task 26, is still pending.

Nothing a caller sees changes. The field is read only by ReportLostSection (:4409) and ReportSectionReadInPart (:4548). The first runs before readAgain, from :3358, :3565 and HandOnPrevious :4078 (only on the not-read branch). The second is skipped for a record read again (:3417). No validation rule reads it, and PdfDocument.Reader and XRefSectionRecord are internal.

What is left is a consistency nit. The batch pins both halves of the RowsRefusedBefore copy: the fourRefused row asserts true, the own-refusal rows false. It pins both halves of ReservedRowsRefused too (the refusedFirst row gives 1). EveryNumberRefusedBefore is the only copy whose non-default half no row asserts. #289, #295, #331, #52 and r1 to r5 do not cover this. Round 5 [8]'s verdict states the true half is correct ("the copy keeps it true"), and that holds at head.
REFUTER EVIDENCE: My own probe, ZzRefuteK2Tests.ZzReadAgainAfterNewerEveryRefused, saved at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refk2-ZzRefuteK2Tests.cs.txt. It was built at 65306d4 (Release, 0 warnings). It takes HybridFiles.PrevFallingShortOfTheStreamBefore("no row") and appends two things: `20 0 obj << /Type /XRef /Size 21 /Index /X /W [1 -4 2] /Prev 1770 /Length 0 >> stream endstream endobj`, then a table at 2007 naming it through /Prev, which startxref names.

Head output (/tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refk2-head.txt):
- S startxref 2007 Read Table enrb=False
- S /Prev 1903 Malformed Stream enrb=False
- S /Prev 1770 Read Table enrb=True rrb=True
- S /XRefStm 1614 NamedAgain Unknown
- S /Prev named=1607 off=1614 Relocated Stream enrb=True rrb=True ord=5
- S /Prev 247 Read Table enrb=True rrb=True
- D: xref.section-unreadable @1903, xref.offset-adjusted @1614, xref.hybrid-stream-lost @1614, xref.rebuilt, object.redefined
- F: xref.section-malformed, xref.section-shifted, xref.hybrid-stream-wrong, page-tree.kid-invalid

This matches the reviewer's dump.

Under K2 (`section.EveryNumberRefusedBefore = false;` at :3383; /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refk2-K2.txt), `diff` shows one line: `S /Prev named=1607 off=1614 Relocated Stream enrb=True` becomes `enrb=False`. Every D and F line is identical.

Under K2, CrossReferenceChainTests and GuardCutValueTests, the only test classes that read the field, gave 813 total, 0 failed. A wider filter (CrossReference*, GuardCutValue*, HybridFile*, Relocat*, Nearby*, ReaderLimits*, DocumentReader*) gave 2092 total, 0 failed. The reviewer's whole-suite run gave 7677 total, 0 failed.

R5a's mutant list (r5-fix-result.json) has only B01 for this line: "the EveryNumberRefusedBefore copy in the read-again block dropped: killed (1, the new read-again row alone)". ADR 53 :55-63 gives the catalog and the no-specialization rule. :65 says a survivor "does not count as a review finding". The worktree is restored and the probe removed.
REFUTER FIX: Cheap and optional; no review round needed. Pin the true half alongside the existing rows. Either give HybridFiles.PrevFallingShortOfTheStreamBefore an option, or add a Fact next to A_prev_falling_short_of_the_stream_its_xrefstm_read_reads_it_whole_and_the_naming_is_reported. The shape: a newer cross-reference stream whose /Index is no array (/W [1 -4 2], /Prev naming the hybrid table), named through /Prev by a table startxref names. Assert three things:
- the /Prev's record read again (Sections[4]) has EveryNumberRefusedBefore true and RowsRefusedBefore true;
- the diagnostics are section-unreadable, offset-adjusted, hybrid-stream-lost, rebuilt, object.redefined;
- the builder's summary is updated to describe the new option.

This matches how the batch pins the RowsRefusedBefore copy (the fourRefused row) and ReservedRowsRefused (refusedFirst). The alternative is to settle K2 in the batch-wide ADR 53 step (task 26), recorded as outside the catalog: the statement's in-catalog mutant, its removal, is killed, and no report or rule reads the field on a section read again. Not a maintainer question.

######## [10] tests:holds-a-reference-name-says-every-object  reviewer=nit  refuter=confirmed/nit maint=False
LOCATION: tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:2743 (row :2739); cited at docs/threat-model.md:431
CLAIM: The theory is named ..._says_every_object_is_looked_for_whatever_rows_it_holds, but its first row, the stream keeping its one row, pins "every other object", not "every object". The name is false for that row. The test's own comment gets it right: "every other object is, beside the row it kept, and every object, where it kept none". The two sibling theories (A_stream_read_in_part_whose_numbering_is_written_with_references_says_every_other_object..., A_section_read_in_part_whose_lost_numbers_pass_the_bound...) carry control rows whose stream does not meet the name's subject. Their names stay true of every row that meets it, so only this name is wrong.
EVIDENCE: Row :2739 `InlineData(1, false, false, "1 row where its /Index and /Size declare 3; the row it holds was kept, and every other object no section read before it indexes is looked for by rebuilding the index, ...")`, and it passes at head. Dump /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r6t-dump/ntar-r1.txt has the same message.
FIX: Rename it, for example A_stream_read_in_part_whose_numbering_holds_a_reference_leaves_every_object_but_the_row_it_kept_to_a_rebuild_whatever_rows_it_holds, and update the citation in threat-model.md:431.
--- REFUTER REASON: The finding holds. Commit 8692234 added this theory as part of the round 5 fixes (8ba4589..65306d4). Its name says the report "says every object is looked for whatever rows it holds". All four of its rows meet the name's subject: each builds the stream with /Index [4 1 6 9 0 R], a numbering that holds a reference. Three rows assert "every object" and the name holds for them: no row, the stream's one row refused by the stream itself, and that row's number refused by the newest table. The first row is different. The stream keeps object 4's row, and the row asserts "the row it holds was kept, and every other object no section read before it indexes is looked for by rebuilding the index". The chain index still places object 4 there (TryGet(4) is true). So for that row the name states a report the library does not make.

The test's own comment (":2748-2749 it says every other object is, beside the row it kept, and every object, where it kept none") and threat-model.md:431's prose ("every object no section read before it indexes but those its rows kept") both describe the behavior correctly. Only the identifier, and its citation in the threat model's evidence column, overstate it. The two sibling theories are different: their control rows (a direct /Index; 4,095 ranges) do not meet their names' subjects, so those names stay true.

The assertions are right and the test is real: the fix stage recorded all 4 rows failing on 8ba4589, and they pass at head. That makes this wording, not a test that fails to hold its claim. Rounds 2, 3 and 4 settled their test-name findings at nit (r3 [7]/[16], r4 [18]/[19]), so this stays nit. No issue (#289, #295, #331, #52) or earlier round holds it.
REFUTER EVIDENCE: On origin/ccr-6d6c0ee6-dqm4lm-188 (65306d4), tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:2743 is `A_stream_read_in_part_whose_numbering_holds_a_reference_says_every_object_is_looked_for_whatever_rows_it_holds(int rows, bool refused, bool fourRefused, string said)`. Its rows:
- :2739 `InlineData(1, false, false, "1 row where its /Index and /Size declare 3; the row it holds was kept, and every other object no section read before it indexes is looked for by rebuilding the index, ...")`
- :2740-2742: the refused, fourRefused and 0-row rows, each saying "every object".

All four call `PartialFiles.NumberedThroughAReference("[4 1 6 9 0 R]", "2", rows, ...)`. :2754 asserts `TryGet(4)` is `rows == 1 && !refused && !fourRefused`, so it is true for row :2739.

I built Release in my worktree and ran `--filter-method "*...holds_a_reference_says_every_object_is_looked_for_whatever_rows_it_holds*"`: total 4, failed 0, succeeded 4.

The dump /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r6t-dump/ntar-r1.txt shows "D Warning xref.section-unreadable @291: The cross-reference stream /Prev names at offset 291 was read only in part: it holds 1 row where its /Index and /Size declare 3; the row it holds was kept, and every other object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows." with "obj 4 idx=True = (\"four\")".

`git log -S` places the name in 8692234, which is between 8ba4589 and 65306d4. `grep` finds the name only at the test and at docs/threat-model.md:431. It does not appear in r1-findings.md to r5-findings.md or in decisions.md.
REFUTER FIX: Rename the theory so its name is true of the kept-row row as well. For example: `A_stream_read_in_part_whose_numbering_holds_a_reference_says_every_object_but_a_row_it_kept_is_looked_for_whatever_rows_it_holds`. The reviewer's longer proposal also works. Update the citation in docs/threat-model.md:431's evidence column in the same commit. The assertions, the comment and the threat model's prose stay as they are. This is not a design choice for the maintainer.

######## [11] tests:hybrid-builder-summary-object-seven  reviewer=nit  refuter=confirmed/nit maint=False
LOCATION: tests/AdCodicem.Pdf.Tests/HybridFiles.cs:603-606 (summary of PrevFallingShortOfTheStreamBefore)
CLAIM: Round 5 added the shape "no row, its /Index holding a reference" to the builder, a stream holding 2 of the 3 rows it declares. The summary still says the oldest table indexes object 7, "(seven as the oldest has it)", "where the stream is one row short or refuses a row". The new shape is one row short, but the builder's `olderSeven` holds only for "a reserved row, one row short" and "a reserved row after a refused one". The oldest table built for the new shape indexes objects 0 to 3 only, so the summary misstates the file it builds.
EVIDENCE: Dump of PrevFallingShortOfTheStreamBefore("no row, its /Index holding a reference") (/tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r6t-dump/hyb-noref.txt). The oldest table is `xref\n0 4\n0000000000 65535 f \n0000000009 ...\n0000000058 ...\n0000000203 ...\ntrailer\n<< /Size 4 /Root 1 0 R >>`, with no row for 7. Stream 6 is `/Index [4 1 6 9 0 R]` with object 9 = 2 and holds rows for 4 and 6.
FIX: Say "and, where the stream gives a reserved row one row short of what it declares, or refuses a row, object 7", or name the two shapes, so the new shape is visibly excluded.
--- REFUTER REASON: The finding holds. Round 5 (8ba4589..65306d4) added the shape "no row, its /Index holding a reference" to HybridFiles.PrevFallingShortOfTheStreamBefore. The summary describes that shape as one where "the stream holds 2 of the 3 rows it declares", which is one row short. The same summary then says the oldest table indexes object 7 "where the stream is one row short or refuses a row". But the builder writes object 7 into the oldest table only for "a reserved row, one row short" and "a reserved row after a refused one" (line 618). For the new shape the oldest table indexes objects 0 to 3 only, under /Size 4. This is a text the batch wrote in round 5, so it is not covered by an earlier settlement or a filed issue. No test depends on the wrong reading: the theory A_prev_falling_short_of_the_stream_its_xrefstm_read_reads_it_whole_and_the_naming_is_reported never reads object 7 for the "no row" shapes. So the mistake is only in the builder's documentation, which makes it a nit.
REFUTER EVIDENCE: Checked on 65306d4 (origin/ccr-6d6c0ee6-dqm4lm-188), tests/AdCodicem.Pdf.Tests/HybridFiles.cs:
- Lines 602-603 describe the new shape: "so that the stream holds 2 of the 3 rows it declares".
- Lines 605-606: "and, where the stream is one row short or refuses a row, object 7, <c>(seven as the oldest has it)</c>".
- Line 618: `var olderSeven = seven is "a reserved row, one row short" or "a reserved row after a refused one";`. Line 619 leaves oldSeven empty otherwise, and line 621 writes `/Size {(olderSeven ? 8 : 4)}`.
- Line 630: the new shape gives `StreamRows((1, four, 0), (1, stream, 0))` with `/Index [4 1 6 9 0 R]`.

The diff 8ba4589..65306d4 shows this shape and its summary sentence were added in round 5, and the 'one row short' clause was left unchanged.

The finder's dump (/tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r6t-dump/hyb-noref.txt) matches the code:
- The oldest table is `xref\n0 4\n...trailer\n<< /Size 4 /Root 1 0 R >>`, with no row for 7.
- Stream 6 is `/Index [4 1 6 9 0 R]`, and object 9 is `2`.
- The reader agrees the stream is one row short: `xref.section-unreadable @1614: ... it holds 2 rows where its /Index and /Size declare 3`.

In CrossReferenceChainTests.cs:4839-4920 the theory checks Text(document, 7) only for "a reserved row after a refused one", and checks object 7 as null only for the shapes that do not start with "no row". No assertion relies on the summary's claim.
REFUTER FIX: In the summary of PrevFallingShortOfTheStreamBefore (HybridFiles.cs:605-606), name the two shapes instead of using the descriptive phrase. For example: "..., the page among them as <c>(three as the oldest has it)</c>, and, for <c>"a reserved row, one row short"</c> and <c>"a reserved row after a refused one"</c>, object 7, <c>(seven as the oldest has it)</c>; ...". Another option is "where the stream gives a reserved row one row short of what it declares, or refuses a row". Either wording visibly leaves out the /Index-reference shape. This changes only the documentation; the maintainer does not need to decide anything.

######## [12] docs:inpart-every-number-tail-misstated-for-cut-table  reviewer=nit  refuter=narrowed/nit maint=False
LOCATION: docs/website/docs/reference/diagnostics.md:112 ("The section is reported once it refused what it lost, and where it refused every number so, the message says every other object ..."); docs/website/docs/concepts/lazy-reading.md:166; docs/threat-model.md:431 ("where that refusal refused every number, that every object no section read before it indexes but those its rows kept is looked for"); src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:66-69; src/AdCodicem.Pdf/IO/PdfFileReader.cs:4505-4509 (ReportSectionReadInPart remark)
CLAIM: Round 5 wrote, in five places, that a section read in part whose refusal refused every number (past 4,096 lost ranges among the cases listed) is reported with the 'every other object no section read before it indexes' tail. One shape does not get it: a classic table named by startxref or a /Prev that the end of the file cuts (SubsectionsUnknown) and that lost more than 4,096 ranges. Under R5a's accepted table-arm split it keeps the rest-of-table tail. At 8ba4589 it gave 'every other object'. No test pins the shape (R5a deviation 3). The code comment at the table arm (PdfFileReader.cs:4590-4595) states the exception; none of the docs does. Separately, the PdfDiagnosticCodes list leaves out the /XRefStm-named table the end of the file cuts, which does say 'every other object'. Its 'neither its rows nor a section read before it index' also lacks verb agreement.
EVIDENCE: Probe ZzR6DocsCutTableTests (saved as /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r6docs-ZzR6DocsCutTableTests.cs.txt). File: catalog, pages and object 3 in an oldest table; then the table named by startxref (or by a newest table's /Prev): 'xref\n0 1\n0000000000 65535 f \n' plus N one-row subsections '{100+2i} 1\n0 0 x\n', ending in '% startxref {offset}\n%%EOF\n' with no trailer, so the end of the file cuts it. Head 65306d4, N=4097 under startxref: 'Warning xref.section-unreadable @276: The cross-reference table startxref names at offset 276 was read only in part: the row for object 100, at offset 311, is not an offset, a generation and n or f; the rows read were kept, and what only the rest of the table, or the sections its trailer would name, index is looked for by rebuilding the index.' The /Prev variant (N=4097, @476) gives the same tail, and so do the N=4095 controls. Same probe at 8ba4589, N=4097, both namings: '...; the rows read were kept, and every other object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows.' N=4095 at 8ba4589 gives the rest-of-table tail. The message is true either way, because the chain ends at such a table; only the docs' account of it is wrong.
FIX: Docs only, keeping the accepted deviation. In diagnostics.md:112, lazy-reading.md:166, threat-model.md:431, the XRefSectionUnreadable summary and the ReportSectionReadInPart remark, add the carve-out the code comment states: 'but for a table startxref or a /Prev names whose rest the end of the file leaves unknown, which ends the chain: its message says what only the rest of the table, and the sections its trailer would name, index is looked for by rebuilding the index'. In the PdfDiagnosticCodes list, add 'a classic table an /XRefStm names whose rest cannot be known' and fix the verb ('indexes'). Optionally add a row with 4,097 lost ranges and an end-of-file cut under startxref to pin the tail the deviation chose.
--- REFUTER REASON: The main claim holds. Round 5 wrote two docs sentences that say what the message reads when a section's own refusal refuses every number: diagnostics.md:112 ("where it refused every number so, the message says every other object no section read before it indexes is found so") and threat-model.md:431 ("where that refusal refused every number, that every object no section read before it indexes but those its rows kept is looked for"). Both treat ranges past 4,096 as one way that refusal happens.

One shape does not fit. Take a table that startxref or a /Prev names, that lost more than 4,096 ranges, and that the end of the file cuts. Under R5a's accepted table-arm split it keeps the rest-of-table tail. The code is clear on this: RefuseRowsLost runs RefuseEveryNumber for `_rowsLostPast || section.SubsectionsUnknown` (PdfFileReader.cs:4732-4735), but the table arm checks SubsectionsUnknown before everyRefused (:4597-4604). So the docs now misstate an exception that was accepted (deviation 3) and that only the code comment at :4590-4595 states. The round 5 refuter's fix had said to keep that arm. The docs text came in later, in round 5 itself: `git diff --word-diff 8ba4589 65306d4` shows both sentences as additions, so this is a text the batch wrote, not a regression of the docs against 8ba4589. The message stays true, because the chain ends at such a table, so this is wording only.

The finding needs narrowing in three places:
- **PdfDiagnosticCodes.XRefSectionUnreadable** does not misstate the message. It says what is missing from the index ("every object neither its rows nor a section read before it index... are missing"), and that is true for this shape. Its only defect is verb agreement: in "neither its rows nor a section", the verb agrees with the nearer subject, "a section", so it should read "indexes". That text was also written in round 5.
- **The /XRefStm-named cut table** left out of that summary's list is not a misstatement. The same summary covers it a few lines above (:52-55, "for any number its rows do not give (#156)").
- **lazy-reading.md:166 and the ReportSectionReadInPart remark** (:4503-4509) only paraphrase the scope ("says so"; "says what that refusal leaves to a rebuild", followed by "the chain ends there"). They are loose rather than false, so a carve-out there is optional.

No test pins the shape. The only rest-of-table assertions are the 4 at CrossReferenceChainTests.cs:2422/2455/2480/3099, and the 4,096-range tests (:3264, :3348) all end in a trailer.
REFUTER EVIDENCE: I rebuilt the reviewer's probe (ZzR6RefCutTableTests, a copy of r6docs-ZzR6DocsCutTableTests.cs.txt) on origin/ccr-6d6c0ee6-dqm4lm-188 at 65306d4. The Release build had 0 warnings and 0 errors, and all 4 rows ran. Output is in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r6ref-cut-head.txt.

- **lost=4097, startxref:** "Warning xref.section-unreadable @276: The cross-reference table startxref names at offset 276 was read only in part: the row for object 100, at offset 311, is not an offset, a generation and n or f; the rows read were kept, and what only the rest of the table, or the sections its trailer would name, index is looked for by rebuilding the index." (incomplete=True, unknown=True)
- **lost=4097, /Prev (@476):** the same tail.
- **lost=4095 controls, both namings:** the same tail.

8ba4589, from code reading: the table arm was `_rowsLostPast || (section.SubsectionsUnknown && naming == "/XRefStm") ? ", and every other object ..."`. With 4,097 ranges `_rowsLostPast` holds, so the tail was "every other object", which matches r6docs-cut-8ba.txt.

Head code:
- RefuseRowsLost (PdfFileReader.cs:4732-4735) refuses every number for `_rowsLostPast || section.SubsectionsUnknown`.
- The table arm (:4597-4604) is `section.SubsectionsUnknown ? (naming == "/XRefStm" ? EveryOtherObject : ", and what only the rest of the table, ...") : (everyRefused ? EveryOtherObject : ...)`.

Docs history: `git diff --word-diff 8ba4589 65306d4` shows these as additions in round 5:
- diagnostics.md: "{+The section is reported once it refused what it lost, and where it refused every number so, the message says every other object no section read before it indexes is found so, every object where it kept no row (#210).+}"
- threat-model.md: "{+refused — where that refusal refused every number, that every object no section read before it indexes but those its rows kept is looked for by rebuilding the index —,+}"
- PdfDiagnosticCodes.cs:66-67: "every object neither its rows nor a section read before it index".

decisions.md 'Review round 5 fixes', deviations accepted: "A startxref or /Prev table that both lost more than 4,096 ranges and has unknown subsections now uses the rest-of-table tail."

The probe file has been removed and the worktree is clean.
REFUTER FIX: This is a docs-only change that keeps the accepted deviation; it needs no maintainer choice.

1. In diagnostics.md:112, after "...is found so, every object where it kept no row", add: "— but for a table `startxref` or a `/Prev` names that the end of the file cuts, at which the chain ends, whose message says what only the rest of the table, or the sections its trailer would name, index is found so —".
2. In threat-model.md:431, add the same carve-out inside the dash clause: "where that refusal refused every number, that every object no section read before it indexes but those its rows kept is looked for by rebuilding the index, or, for a table startxref or a /Prev names that the end of the file cuts, what only the rest of it or the sections its trailer would name index —".
3. In PdfDiagnosticCodes.cs:67, change "neither its rows nor a section read before it index" to "indexes".
4. Optional: add the same carve-out to lazy-reading.md:166 and to the ReportSectionReadInPart remark (:4505-4509).
5. Optional: add a CrossReferenceChainTests row with 4,097 lost one-row subsections and an end-of-file cut under startxref, pinning the rest-of-table tail the deviation chose.

Adding the /XRefStm cut table to the PdfDiagnosticCodes list is not needed, since :52-55 already covers it.

######## [13] docs:white-space-guard-reads-nothing-past-residue  reviewer=nit  refuter=narrowed/nit maint=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:3738 (KeepsItsPlace summary: "nor does one that names white space and comments running on past a trailer's window, whose reading reads nothing past them"); :3714 (StartOfNamed: "which its reading reads nothing past"); :3698 (StartOf: "which a section read there reads nothing past, and the chain reaches nothing through")
CLAIM: Round 5's [2] reworded 'what follows them was not read' to 'the section was not read from there' at the sites the triage listed. Three private summaries still say the /Prev's reading reads nothing past white space that runs past MaxTrailerLength, and that the chain reaches nothing through it. That is false under a bound below the nearby-search reach, which is exactly the shape [2] named: the /Prev's search near the offset finds the section past the run and reads it. KeepsItsPlace gives this as the reason such a /Prev keeps no place, so the reason it states does not hold in that shape.
EVIDENCE: On head 65306d4, CrossReferenceChainTests.A_prev_naming_more_white_space_than_a_lowered_trailer_bound_is_not_read_from_there_and_finds_its_section_past_it passes, 2 of 2 (classic table and stream). Under MaxTrailerLength 256, a /Prev naming the first of 257 spaces gives sections ('/Prev', Relocated, Table|Stream, original, run) and diagnostics (limit.trailer @run, xref.offset-adjusted @original). Objects 5 = 'five' and 7 = 'new seven' are read from the section past the run. So the /Prev's reading does read past them, and the chain reaches the section beyond them.
FIX: Reword as [2] did. StartOfNamed: '...or white space and comments that run on past a trailer's window, from which its reading reads no section (PaddingAt), though its search near the offset may find one past them'. KeepsItsPlace: '...nor does one that names white space and comments running on past a trailer's window, which leave the section not read from there'. StartOf: drop 'and the chain reaches nothing through', or say the section is not read from there.
--- REFUTER REASON: The finding holds for two of the three summaries, KeepsItsPlace (:3734) and StartOfNamed (:3710). It does not hold for StartOf (:3694-3695).

KeepsItsPlace and StartOfNamed say the /Prev's "reading reads nothing past" white space that runs on past a trailer's window. That is the same overclaim round 5's [2] removed from the message ("what follows them was not read"). The triage's fix list did not name these private summaries, so round 5 missed them. They are unchanged since 8ba4589, written by the batch before round 5; main has no such members.

KeepsItsPlace's own summary counts the nearby search as part of the /Prev's reading:
- :3729 says "rather than looking for its section nearby, as the chain reading it there does".
- :3735 says "the /Prev looking for its section nearby".

So "whose reading reads nothing past them" covers that search. Under a bound below the 512-byte radius, the search reads the section past the run. StartOfNamed cites PaddingAt for the claim, but PaddingAt's remark, reworded in round 5, now says "while a search near the position may find it past them". The two texts contradict each other.

The behavior these summaries describe is right. In the hybrid branch, previousStart = StartOfNamed(...) is -1 (:3452) and is passed as leaveAt (:3518), so KeepsItsPlace is never asked and the /Prev keeps no place. Only the reason given is too wide. The real reason is that the reading from the offset finds no start past the run.

StartOf's two clauses are accurate:
- "which a section read there reads nothing past": "there" is the position. TryReadXRefSection at that position gives NotFound with limit.trailer.
- "the chain reaches nothing through": "reaches" means ReachedAt's notion, the one Q9 settled. ReachedAt (:3691) gives -1 through such a run unless the offset was named before. A relocated section's loop is placed at the offset named, not past the run (:3221, moved.NamedOffset == looped => looped), so the chain never reaches the section through the white space.

The finding's proposed fix would delete StartOf's correct description of ReachedAt. ReachedAt's own param at :3666 uses the same notion and holds too. The test comment at CrossReferenceChainTests.cs:4480 holds where it is written: the default bound and a run of 65,537 bytes, which puts the section beyond the search's reach.

These are private XML docs. Nothing behaves differently, and no user-facing text is involved, so the severity is nit.
REFUTER EVIDENCE: Run on 65306d4 (origin/ccr-6d6c0ee6-dqm4lm-188), Release build. The filter selected A_prev_naming_more_white_space_than_a_lowered_trailer_bound_is_not_read_from_there_and_finds_its_section_past_it and An_xrefstm_naming_the_token_before_white_space_its_table_s_prev_names_past_a_trailer_s_window_is_left_to_no_prev: "Passed! total: 3 failed: 0 succeeded: 3". That is the first test's two rows (table and stream) plus the neighbor.

Under MaxTrailerLength 256, a /Prev naming the first of 257 spaces gives:
- section ('/Prev', Relocated, Table or Stream, original, run);
- diagnostics limit.trailer at the run and xref.offset-adjusted at the original;
- objects 5 = "five" and 7 = "new seven", read from the section past the run.

So the /Prev's reading, counting its nearby search as KeepsItsPlace's summary (:3729, :3735) does, reads past the run.

`git show 8ba4589:src/AdCodicem.Pdf/IO/PdfFileReader.cs` has the same texts: "reads nothing past" at :3694, :3710 and :3734, and "reaches nothing" at :3666 and :3695. bd70ef5, the round 5 [2] fix, did not change them. The search for the functions on 21a0298 found nothing, so main does not have them.

Control flow, from the code:
- StartOfNamed (:3724) gives StartOf's -1 when PaddingAt gives -1.
- previousStart (:3452) becomes leaveAt in TryRelocateXRefSection (:3518).
- `candidate == leaveAt` (:4908) is never true for -1, so KeepsItsPlace is never asked.
- ReachedAt (:3691) gives -1 through the run for an offset not named before.
REFUTER FIX: Reword the two summaries the way round 5's [2] reworded the message; leave StartOf alone.

- StartOfNamed (:3709-3711): replace "or white space and comments that run on past a trailer's window, which its reading reads nothing past (PaddingAt)" with "or white space and comments that run on past a trailer's window, past which its reading from the offset comes to no start (PaddingAt), though its search near the offset may find the section past them".
- KeepsItsPlace (:3733-3734): replace "nor does one that names white space and comments running on past a trailer's window, whose reading reads nothing past them" with "nor does one that names white space and comments running on past a trailer's window, whose reading comes to no start past them, the section not being read from there, though its search near the offset may find it past them".
- StartOf (:3694-3695): no change. "a section read there" is scoped to the position, and "reaches" is ReachedAt's notion, which gives -1 through such a run.

It is a comment-only change with no mutants. It can ride with the end-of-batch work. This is not a maintainer decision.

######## [14] batch:xref-in-stream-data-declares-a-section  reviewer=major  refuter=narrowed/major maint=False
LOCATION: src/AdCodicem.Pdf/IO/PdfFileReader.cs:4934-4938 (the search's acceptance of a Malformed place); IsTokenOnItsLine at :4988
CLAIM: When an offset falls short of its section, the search near that offset (TryRelocateXRefSection) can find the word xref, or the header of an object giving /Type /XRef, at the start of a line inside an object's stream data, such as an uncompressed embedded file. It takes that place for the section found moved, though no load ever lexes stream data. The same happens on a line inside an object's dictionary or array. Because the nearest such place ends the search (R1-Q1), the reader reports a section that cannot be read: Warning xref.section-unreadable and Error xref.section-malformed, both blamed on the file. The real section, a little farther on, is never read. The index is then rebuilt at opening, which brings back an object the update freed; for an /XRefStm, the older table's row stands and an older revision is served. Main dropped such places, since they are Malformed and not cut by a guard, and read the real section. R2 [2]'s line test excludes only comments and strings, while the principle it settled is that a word no load reads as a token declares no section. Stream data is never lexed. The threat model's #171 Known-gaps entry names, for this search, only strings an earlier line opened, so this case is neither tracked nor documented. Present at 8ba4589 as well: not a round 5 regression. It entered with 0353e24 (R1-Q1).
EVIDENCE: Reproduced on head 65306d4 and main 21a0298 with probe Probe.cs, under default limits.

(1) /Prev case, file r6bat/genE/e00024_n6.pdf (LF).
- Oldest table at 163: object 3 '(three old)', 5 '(five old)', 6 '(six, freed by the update)'.
- Update: 3 '(three new)', then '4 0 obj << /Type /EmbeddedFile /Length 147 >> stream', whose data is 'Notes on the file format.\nxref tables list offsets; see section 7.5.4.\nEnd of notes.\n%xxxx...'. Its table at 616 gives rows 3 and 4, frees 6 ('0000000000 00001 f') and has /Prev 163.
- Second update: 5 '(five new)'; table at 783 with /Prev 480, 2 bytes past the 'xref' at 478 inside the embedded data; startxref 783.

Main:
- object 6 = null, WasRepaired False
- 'Repair xref.offset-adjusted at 616: The cross-reference section /Prev names at offset 480 was found 136 bytes after it.'
- 'Warning xref.section-shifted at offset 616'

Head:
- object 6 = '(six, freed by the update)', WasRepaired True at opening
- 'Repair xref.offset-adjusted at 478: ... was found 2 bytes before it.'
- 'Warning xref.section-unreadable at 478: The cross-reference table /Prev names at offset 480, found at offset 478, is there but cannot be read: it holds the keyword tables at offset 483, where a subsection or the trailer should start. ...'
- 'Repair xref.rebuilt'
- 'Error xref.section-malformed at offset 478'
- 'Warning xref.section-shifted at offset 478'

Variants: the data '8 0 obj\n<< /Type /XRef /Size 9 /W [1 2 1] >>\nendobj' (genE/e00030_n6.pdf) gives the same freed object 6, with '... is there but cannot be read: its dictionary is not followed by stream data.' A cut inner table (e00036) also gives object 6. All 18 such variants (CR/LF, CRLF; /Prev 2, 10 or 40 bytes past the decoy) differ from main; the 24 controls, with no decoy or a readable inner table, are identical on both trees.

(2) /XRefStm case, file r6bat/genH/h00002_n7.pdf.
- Oldest table at 137: 3 '(three old)'.
- Update: 3 '(three new)'; an embedded file whose data holds 'Example:\n8 0 obj\n<< /Type /XRef /Size 9 /W [1 2 1] >>\nendobj'; the real cross-reference stream '7 0 obj' at 480, /Index [3 1].
- Table at 580: trailer '<< /Size 8 /Root 1 0 R /Prev 137 /XRefStm 351 >>', 351 being 2 bytes past the decoy header at 349.

Main:
- object 3 = '(three new)'
- 'Repair xref.offset-adjusted at 480: The cross-reference section /XRefStm names at offset 351 was found 129 bytes after it.'

Head:
- object 3 = '(three old)' when read in ascending order; '(three new)' when a miss set the rebuild off first
- 'Repair xref.offset-adjusted at 349: ... was found 2 bytes before it.'
- 'Warning xref.section-unreadable at 349: The cross-reference stream /XRefStm names at offset 351, found at offset 349, is there but cannot be read: its dictionary is not followed by stream data. ...'
- 'Error xref.section-malformed at offset 349'
- object 8 served as '<</Size 9 /Type /XRef /W [1 2 1]>>' (the rebuild then takes the header in the data, #171)

(3) Object bodies, files r6bat/genL/l000NN_n6.pdf, /Prev template of (1):
- '[ 1 2\nxref 3 ]' (l00006)
- '<< /A 1\nxref /B 2 >>' (l00008)
- '<< /A 1\n9 0 obj << /Type /XRef >> >>' (l00012)
Each gives head 'xref.section-unreadable ... found at offset 367' plus Error, rebuild and object 6 resurrected, where main gives 'found 87 bytes after it' and object 6 null. A name '/xref', '/x#72ef' or '12xref' is correctly no section on both trees.
FIX: Make a place inside stream data declare nothing. The line test already reads back up to LineStartSearchLength bytes, so it could also refuse a place that a 'stream' keyword precedes with no 'endstream' between them. A cheaper alternative refuses a place inside an object: one that an 'N G obj' header precedes with no 'endobj' between them. Hold it with tests built from e00024 (object 6 null, no rebuild), h00002 (object 3 '(three new)', no Error) and l00006. If what a place inside an object declares is judged a design question beyond R2 [2], put it to the maintainer. At the least, comment on #171 and name stream data and object bodies in the threat model's #171 Known-gaps entry and in lazy-reading.md:194 beside 'a comment or a string', as round 4's [21] did for strings an earlier line opened.
--- REFUTER REASON: Part of the finding holds and part does not.

**What holds: stream data.** I rebuilt the stream-data case with my own generator under default limits. Head 65306d4 and 8ba4589 take the word `xref`, or a `/Type /XRef` dictionary header, inside an uncompressed stream's data as the section found moved. Each then:
- reports `xref.section-unreadable` and an Error `xref.section-malformed` against the file;
- rebuilds the index;
- serves an object the update freed.

Main 21a0298 drops the place, because it is Malformed and no guard cut it. It then finds the real table and serves null. So this is a regression against main. It came in with 0353e24 (R1-Q1) and is not a round 5 regression.

**Why it is not refuted as known.**
- R2 [2]'s claim named stream data ("inside comments, strings and stream data too"), but the decision and the fix covered only a comment or a string on the place's line.
- No later round, fix report or decision addresses stream data. r2 to r5 fix results do not, and decisions.md records no acceptance.
- #171's title covers headers inside stream data in general. Its 2026-11-06 comment brings this search into #171 only for a string an earlier line opened.
- Every text names that string as the only limit, and the reference rows give no limit at all:
  - lazy-reading.md:193-201;
  - threat-model.md:457-459 (the #171 Known-gaps entry);
  - IsTokenOnItsLine's remark (src/AdCodicem.Pdf/IO/PdfFileReader.cs:4966-4987, "as a load reads it");
  - diagnostics.md:110 and validation-rules.md:56, which say "a token a load reads from the start of its line".
- No load ever lexes stream data. The batch made the case worse than main, and the texts misstate it.

**Narrowed out: object bodies (sub-case 3).** `[ 1 2\nxref 3 ]` and `<< /A 1\nxref /B 2 >>` reproduce. But `xref` there is a token that a load of the object does lex. Main's own load of my array file reports it: `Warning syntax.unexpected-token at 405: A token was found where a value was expected.` That is exactly what R2 [2] decided ("a token a load lexes from its line's start"). Whether such a keyword should declare a section is a design widening, not a defect.

**Widened.** The line test accepts any token on the line, as decided for `] endobj xref`. So in stream data the word does not have to start a line: the prose line `see xref tables.` is taken too.

**Side note, outside this finding.** The order-dependent value in the /XRefStm case (`(three old)` read in ascending order, `(three new)` in descending order) also occurs when the /XRefStm names the decoy header exactly. That is the B19-1 path: a `/Type /XRef` dictionary with no stream data is excluded from the refusal by DeclaredByLostStream. So the read-order dependence does not come from the search itself.

**Severity: major.** By the brief's scale this is a wrong object served and a false Error blamed on the file, against main, undocumented and untracked. Its reach is narrow, though: it needs an imprecise offset, uncompressed text data holding the word or such a header, and that place nearer than the real section within 512 bytes.

**Fix.** Telling stream data apart is #171's open design. That is why I recommend documenting and tracking it, as the batch did for the string an earlier line opened (round 3 [1], round 4 [21]). Once that is done, it stands where round 4 [21] left that case.
REFUTER EVIDENCE: **Probes.** My own:
- `/tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r6ref-streamdata/gen.py` builds the files in `files/` beside it.
- The console probe is `src/P.cs`, built against head 65306d4, main 21a0298 and 8ba4589.
- Outputs are `out-head.txt` and `out-main.txt` in the same folder.

**Layout.**
- The oldest table at 197 indexes 1, 2, 3 `(three old)`, 5, and 6 `(six, freed later)`.
- The update writes 3 `(three new)` and object 4, an EmbeddedFile with `/Length 124` whose data holds the line `xref tables list offsets; see 7.5.4.` at 465. Its table at 586 frees 6 and has `/Prev 197`.
- The newest table's `/Prev` is 467, 2 bytes past the word. startxref names that table.

**s1, `xref` at a line start in stream data.**
- Head:
  - `open repaired=True`
  - `O 6 = (six, freed later)`
  - `Repair xref.offset-adjusted at 465: ... /Prev names at offset 467 was found 2 bytes before it.`
  - `Warning xref.section-unreadable at 465: ... it holds the keyword tables at offset 470, where a subsection or the trailer should start. ...`
  - `Repair xref.rebuilt`
  - `F Error xref.section-malformed at offset 465`
  - `F Warning xref.section-shifted at offset 465`
- Main:
  - `open repaired=False`
  - `O 6 = null`
  - `Repair xref.offset-adjusted at 586: ... found 119 bytes from there.`
  - `F Warning xref.section-shifted at offset 586`
- 8ba4589: identical to head.

**s2, data `Example:\n8 0 obj\n<< /Type /XRef /Size 9 /W [1 2 1] >>\nendobj`.**
- Head: found at 453, `... is there but cannot be read: its dictionary is not followed by stream data.`, rebuilt, `O 6 = (six, freed later)`, Error section-malformed.
- Main: `found 129 bytes from there`, `O 6 = null`.

**s3, `see xref tables.` in the middle of a line in stream data.**
- Head: taken at 469, `it holds the keyword tables.`, rebuilt, object 6 resurrected.
- Main: `found 95 bytes`, null.

**s0, control with no word.** Identical on both trees: `found 140 bytes`, `O 6 = null`, no rebuild.

**The reviewer's files.**
- r6bat/genE/e00024_n6.pdf:
  - head: `open repaired=True`, `O 6 = (six, freed by the update)`, `found at offset 478 ... keyword tables at offset 483`, Error at 478;
  - main: `O 6 = null`, `found 136 bytes from there` at 616.
  - Two slips in the evidence, neither of which changes the result: the oldest table is at 205, not 163, and main's wording is "from there", not "after it".
- r6bat/genH/h00002_n7.pdf:
  - head, ascending order: `O 3 = (three old)`, `O 8 = <</Type /XRef ...>>`, offset-adjusted at 349 `2 bytes before it`, section-unreadable `its dictionary is not followed by stream data`, rebuilt, Error section-malformed at 349;
  - head, descending order: `O 3 = (three new)`;
  - main: `O 3 = (three new)`, `O 8 = null`, offset-adjusted at 480 `129 bytes from there`.
  - The same file with `/XRefStm 349` (named exactly) on head: ascending `(three old)`, descending `(three new)`. The order dependence is the B19-1 path's, not the search's.

**Object bodies.**
- b1 `[ 1 2\nxref 3 ]`:
  - head takes it at 405, `the subsection header at offset 410 does not give a first object number and a count of rows`, rebuilt, object 6 resurrected;
  - main finds the table 74 bytes off. Main's own load of object 4 reports `Warning syntax.unexpected-token at 405: A token was found where a value was expected.`, so a load does lex that `xref`, as R2 [2] decided.
- b2 `<< /A 1\nxref /B 2 >>`: the same, with `A dictionary key was not a name.`

**Tracking.**
- r2-findings.md [2]'s claim names stream data. decisions.md round 2 [2] decides only "not in a comment or a string on that line".
- No r2 to r5 fix result or decision mentions stream data for this search.
- #171 (body and 7 comments): the 2026-11-06 comment names this search for a string an earlier line opened only.
- Texts that name only that string, or no limit: lazy-reading.md:193-201, threat-model.md:457-459, PdfFileReader.cs:4966-4987 (summary "lexed from its start as a load reads it"; remark "A string a line before it opened is not seen"), diagnostics.md:110 and validation-rules.md:56 ("a token a load reads from the start of its line").
- The acceptance is at PdfFileReader.cs:4934-4938.
REFUTER FIX: **Recommended in this batch: no code change.** This follows round 3 [1] and round 4 [21] for the string an earlier line opened. The steps are the docs and a #171 comment.

**1. Name stream data as the line test's second unseen context.** Write it as "stream data, or a string, that an earlier line opened" in:
- the IsTokenOnItsLine remark, and qualify "as a load reads it" in its summary (PdfFileReader.cs:4966-4987);
- the comment at the acceptance (:4929-4933);
- lazy-reading.md:200-201;
- the threat model's [#171] Known-gaps sentence (:457-459). Say that the search takes the word `xref`, or such a header, anywhere on such a line, its middle included.
- diagnostics.md:110 and validation-rules.md:56: after "a token a load reads from the start of its line", add "outside stream data and strings an earlier line opened (#171)".

**2. Comment on #171 at the batch's end, with the other tracking.** Name stream data for the chain's search near an offset. Give the e00024 or s1 reproducer: object 6, freed, served as `(six, ...)` after `xref.rebuilt`, where main serves null and finds the table 119 or 136 bytes off. Give the h00002 /XRefStm case: the older revision served. Say it regresses against main, and that paying #171 pays it.

**3. Optional:** a test that pins the gap and cites #171, as `The_line_of_an_obj_keyword_is_read_by_its_digits_whatever_else_it_holds` does.

**Leave object bodies as decided.** Mention them only if the docs are reworded anyway.

**If the behavior should change instead, the maintainer has to choose.** It is a design choice under #171 and R1-Q1. The options:
- **(a) Look back for an open stream.** Refuse a place that `stream` precedes, with no `endstream` between them, within LineStartSearchLength. This is partial: data that starts more than 4,096 bytes before the place escapes it, and the keywords inside data confuse it. The reviewer's `N G obj` ... `endobj` variant has the same limit.
- **(b) Narrow what declares itself in the search.**
  - A table's keyword must be followed by a subsection header or `trailer`.
  - A `/Type /XRef` header must be followed by stream data.
  - This drops every prose and dictionary-only decoy shown here. But it passes over a real section whose first subsection header is damaged, which is the silent loss R1-Q1 chose to avoid.
