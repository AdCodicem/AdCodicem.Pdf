# Residuals the implementation stages reported (unfiled unless said)

[1-flaky] Neighbor, note only per the decisions: Four_times_as_many_object_streams_that_need_one_another_are_judged_circular_in_about_four_times_the_time (ReaderTimingTests.cs, around :314 after this change) still runs ObjectStreamDependencies.Run at room int.MaxValue with a bound of 12. It was not measured here.

[1-flaky] Observation, not filed: the first row a theory runs in a fresh process can be timed while the JIT is still optimizing the code. Here the broken row read 2.14 and 2.30 under busy loops, and 3.08 to 3.29 in some runs alone. Ratios this low hide nothing at bound 8, but a quadratic regression confined to that row could be masked when the test runs filtered. The measurement listed this as a possible debt; the decisions' debt list does not include it. The orchestrator decides whether to file it.

[2-messages] Noted, not changed: PdfFileReader.cs:7908 'only the first 1 of its 2 objects can be read from it' is wording, not agreement (the lens's neighbor list). With one byte held, the RunLength message now reads 'of which it holds 1; it was kept' ('it ... it'), as the decision chose. GuardCutValueTests.cs:35 quotes #224's old 'declared 1 bytes' as history and was left alone.

[3-window] For stage 7 (#207/#222), observed and not changed here: a bare 'xref' keyword that ends the file, with nothing after it, is read as a section today. A relocation lands on it ('was found 20 bytes after it'). CrossReferenceChainTests.The_search_near_a_section_reaches_the_file_s_last_byte_where_an_xref_keyword_ending_the_file_is_one_more_place_tried is written to hold whether that keyword reads or not. It counts the 32 places and asserts the table beyond is never tried. If #207 makes such a table unreadable, this test should still pass unchanged.

[4-comment-head] Remaining difference from a load, in both the scan and the stream-end rule: the obj keyword's own line is read by its digits, so '% 1 0 obj' (a header inside a comment on obj's own line) is taken by both, while a load's lexer reads it as a comment. Worth filing as debt if wanted.

[4-comment-head] Remaining difference from a load: a line that starts inside a literal string opened on an earlier line is lexed from the line's start, not from the string's start. A load reads the string across lines.

[4-comment-head] If the pathological-shape cost of the stream-end search matters (19 ms vs 4.7 ms per 4 MB of '12 (x) % c\n0 obj' lines), a scalar fast path in LineTokens.Pass for short spans could narrow it. Not filed.

[4-comment-head] The documentation site build (npm ci, docfx, docusaurus) was not run. The doc changes are prose plus one [#261] link definition in the same form as its neighbors; CI will build the site.

[5-relocation] Not done here, a neighbor of #323: the conversion for an object of another type applies only to a section a /Prev or /XRefStm names. When startxref names a CIDFont, the reader rebuilds, and the record stays Malformed, so SectionMalformedRule says 'The cross-reference stream at offset X cannot be read' of a font. #323's fix should consider it.

[5-relocation] Residual, not filed: a /Prev naming a byte inside the white space before an already-read section's keyword, other than the offset that first named it, still reads that section once more. The loop is then caught one step later, through visited. The set knows a section by where its first token starts.

[6-stream-prev] Residual, not filed (for review): RetakeDeferred (the section's own row for its deferred /Length) and LeadsPastIndex consult _refusedNumbers only, not the refused ranges. I left them because a deferred /Length's data extent belongs to the stream's own revision. The prototype changed only AddFromChain as well.

[6-stream-prev] Residual, not filed: the hand-on message says 'The chain goes on through its /Prev' even when that /Prev names nothing. The next diagnostic (xref.section-missing, or xref.chain-cycle) says so.

[6-stream-prev] Residual, not filed: a lost stream's position is not counted as read. A /Prev naming its header through another written offset (white space before it) reads it once more: it hands on again, and the loop is caught one step later through the offsets named.

[7-partial] Residual, not filed: a hybrid table read in part refuses its lost numbers before its own /XRefStm stream is read, so that stream's rows do not stand for them either; the objects are found by rebuilding. A Malformed classic table that an /XRefStm names leaves older rows standing for whatever it lost, the chain going on through the hybrid table's /Prev; what it lost is unknown past the fault. Both are worth a look in stage 8 or in review.

[7-partial] Residual (the lens observed it, unchanged): rows that run into '<<' with no trailer keyword leave the table Malformed, while rows the end of the file cuts after a row keep it read in part. #322 (a trailer keyword followed by no dictionary) and #323 (startxref's section unreadable whole is reported only as xref.rebuilt) are unchanged and remain filed.

[7-partial] A refused row whose number a newer section already gave still makes the section 'read only in part' and the index incomplete; the lens observed this of Refuse. The message stays true: 'where no section read before it indexes it'.

[8-xrefstm] Residual, not filed: an /XRefStm naming a classic table that cannot be read (Malformed) is reported as xref.section-unreadable / xref.section-malformed only, not as hybrid-stream-lost / hybrid-stream-wrong. This follows P2 and the decided list, which names tables read; review may want both.

[8-xrefstm] Residual, not filed: two offsets past what a long holds share position long.MaxValue in the lookup map, so a naming again of one may take the other's record as ReadAs. Both are NotFound outside the file, so nothing is reported either way. Also, under MaxXRefSectionCount, the /Prev-also-named record keeps ReadAs null if the guard stops the chain before the /Prev's record (documented on ReadAs).

[8-xrefstm] #321 (a copy two or more revisions late) is unchanged, but it is now detectable from the records: a NamedAgain whose first naming table is not adjacent. Stage 7's residual (a Malformed classic table named by /XRefStm leaves older rows standing for what it lost) is unchanged.

[9-guards] Residual, not filed: a classic table a guard cuts before any row, while every newer section indexed nothing, returns Malformed through `_xref.Count > 0`. The chain then ends through ReportLostSection as the guard's. A /Prev that is no offset is still reported, because SectionOffset runs, but an offset outside the file or a loop is not judged there. This is the partial lens's interaction note; it is rare and worth a look in review.

[9-guards] Residual, not filed (the guards lens's neighbor): the guard-cut variants that return before any /Prev is judged are still unjudged. They are CutByValue, and FaultBesideCut with rows > 0 (a stream whose value a guard cut and whose data holds rows). #264 names only the three rows guards.

[9-guards] Residual, cosmetic, not filed (the guards lens proposed no debt): under a rows guard or a full index, a /Prev or /XRefStm written as a reference to an object MaxObjectLength cut gets no 'names older sections, but its /Prev is the reference ... cut' xref.checked-in-part finding, only the section's own. LeadsPastIndex catches the cut first. A_link_a_section_a_guard_stops_writes_as_a_reference_to_an_object_the_object_guard_cut_stays_the_guard_s holds the rest of the behavior.

[9-guards] Consequence to note for review: past a guard, an /XRefStm that names what its /Prev names in the file is reported, and ReportNoStream marks the index incomplete. Objects past the guard are then found by a rebuild on demand rather than read as null, which is the decided rule that a fault seen past the guard leaves the index incomplete, as read whole.

[10-reserved] Residual, already filed: a relocation candidate that is tried and dropped leaves its reserved rows in the index as free entries, as it leaves its other rows (#320). Its ReservedRow is not reported, because only a kept candidate's record is copied (RelocateTo).

[10-reserved] Observed, not changed: a file whose only section gives nothing but reserved rows no longer 'indexes nothing'. Its free entries make the chain non-empty, so the opening rebuild happens through the catalog search rather than the empty-chain path, as for a section of free rows alone. The diagnostics on that path were not compared.

[11-docs] Unfiled residuals the stages reported, for review or for filing. Stage 4: '% 1 0 obj' on the obj keyword's own line is taken by the scan and the stream-end rule, though a load reads a comment; a line that starts inside a string opened on an earlier line is lexed from the line's start. Stage 5: a /Prev naming the white space before a section already read reads it once more. Stage 6: RetakeDeferred and LeadsPastIndex consult only the refused numbers, not the refused ranges; a lost stream's position is not counted as read. Stage 8: an /XRefStm that names a Malformed classic table is reported as section-unreadable or section-malformed only. Stage 9: a classic table cut before any row with an empty index; the CutByValue and FaultBesideCut paths leave /Prev unjudged. Stage 1: the JIT warm-up of a theory's first row in ReaderTimingTests. None is in the threat model, which cites only filed debts.

[11-docs] Neighbor of #323 (stage 5): when startxref names a CIDFont, the record stays Malformed, and SectionMalformedRule calls the font an unreadable cross-reference stream. Worth a comment on #323 if it is not there already.