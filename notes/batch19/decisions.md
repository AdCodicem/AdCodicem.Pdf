# Batch 19 decisions

Branch ccr-6d6c0ee6-dqm4lm-188 from origin/main 21a0298 (batch 18's #318 merged 2026-11-03 05:30Z).
Issues: #188, #197, #156, with #207, #208, #210, #221, #222, #241, #261, #264, and #281 and #317 placed with it.
Measurement: wf_9ceb535e-b92 on main 21a0298 (9 lenses in worktrees + critic), reports in measure/<lens>/report.md.
To record in status.md at the end: item 18's PR #318 merged on 2026-11-03.

# Maintainer answers, first call (2026-11-03), all as recommended
B19-1 (#281, #188, #208): a section "there" = the xref keyword, or a stream whose dictionary gives /Type /XRef. Such a
   section is reported unreadable where it is, never relocated, and a /Type /XRef stream hands its /Prev on (#208).
   An object giving only /W stays a guess (relocated; #263's tests). Composed build relocation (a)+b2t+c9b, hand-on
   narrowed to TypedXRef.
B19-2 (#208, #210): refuse to older rows the numbers a stream that lost rows declares (stream-prev B's
   RefuseDeclared + critic's experiment R): unreadable stream handing /Prev on, stream read in part (rows short),
   unreadable hybrid /XRefStm stream. MaxRefusedRanges 4,096 internal constant (ADR 34); timing test with 1,024
   chained unreadable streams ships with it.
B19-3 (#210, #207, refused rows): reuse xref.section-unreadable, message "was read only in part: <fault>; ...";
   XML doc and diagnostics.md widen to "cannot be read whole" and startxref's section; validator unchanged. Debt:
   startxref section unreadable whole reported only as xref.rebuilt.
B19-4 (#222): reader xref.section-empty (Repair) and rule xref.subsection-missing (Warning); read as indexing
   nothing like "0 0", index whole; two tests writing empty updates move to "0 0".

# Maintainer answers, second call (2026-11-03)
B19-5 (#156), as recommended: reader xref.hybrid-stream-lost (Warning), index marked incomplete; rule
   xref.hybrid-stream-wrong (Error); chain-cycle and chain-loop narrowed to /Prev (prototype P2, branch (c): an
   /XRefStm naming the offset its table's /Prev names is not followed, the /Prev reads it whole).
B19-6 (#221), as recommended: a reserved row reads as the null object (indexed as a free entry, no older row stands);
   reader xref.entry-reserved (Warning), rule xref.entry-type-reserved (Warning); the message says ISO 32000-1
   "defines" types 0, 1 and 2 (check the standard's wording); ~44 fixtures adjusted, 8 tests change meaning.
B19-7 (#261 stream-end rule): NOT the recommendation — EXTEND IN THIS BATCH. HeaderText carries a comment state across
   reads, PdfObjectParser.IsHeader and MayBeHeaderBytes accept comments between a header's tokens, the two
   StreamLengthTests rows ('12 0 %\nobj', '12 %\n0 obj') flip; a damaged stream stops at a comment header. The four
   texts (HeaderText.cs:131-133, concepts/diagnostics.md:52-53, reference/diagnostics.md:116, status.md's record of
   2026-09-30) say the rule now follows the scan.
B19-8 (#317), as recommended: the probe gets a validation's default room (1,000), five turns, bound 8 (prototype D);
   test-only. Note in the PR that it is a fourth way, consented.

# Maintainer answers, third call (2026-11-03), all as recommended
Q9 positions: one notion of "already read" — the set of positions where the chain read or relocated a section
   (_sectionsReadAt, header offset counted, SeededComparer) — for the loop check, #156's NamedAgain/firstAt, #264's
   pastGuard and relocation. S7 fixed and tested.
Q10 N-DUP: trailer.root-recovered reported once per document; #207/#210 tests assert it appears once.
Q11 A5 (/XRefStm copied 2+ revisions late): debt under M02 with the reproduction and the detection (NamedAgain whose
   first naming table is not adjacent: a read table between the two namings); rule decided with M03's writer work.
Q12 S8 (dropped relocation candidate leaves its rows): debt under M02, reproduction and both designs (rollback mark in
   PdfXRefTable, or a candidate's rows staged until taken).

# Decided without a question (measurement, critic) — see measure/critic/summary.md "decided_without_question"
- #197 fix the window (o + Math.Min(512, Length - o), guard past -512 kept); #171's left edge paid (window reads one
  byte before its start; a header/candidate counts only where it starts inside); filter inside FindSectionCandidates,
  no RemoveAll/lambda. Comment on #171.
- #261 forward confirmation from a token boundary (P261-B, lexes the line with PdfLexer). Test hiding device
  '% ...\nobj' switched to 'N +0 obj' through one shared ChainFiles helper in #261's commit.
- #207: cut table kept as read in part when it read a row (HighestNumber >= 0), unreadable when none ("the file ends
  before its trailer"). file.size-wrong not judged when a section was read in part (variant C, section.Incomplete).
- #210: reported once at the section, first fault, whatever a guard also did; guard-cut section that lost no rows
  stays the guard's. startxref's section read in part not rebuilt at opening.
- #208 tests use /W [1 -4 2] or /W [1 4] (issue's /W [1 9 2] paid on main). /Prev handed on followed under visited
  (positions), section count guard, relocation. Stream dictionary never merged as trailer; file.trailer-value-wrong
  judges the /Prev alone.
- #156: classic table named by /XRefStm read in the stream's place (batch 16), now reported + index incomplete;
  branch (c); four one-table loop tests move to a two-table shape looping through an offset only named; shared
  stream's rows counted per naming, its /Size judged at its oldest naming (P1b), pdfmaker7 drops file.size-wrong;
  NamedAgain lookup by Dictionary; HostileInputTests: 1,000 tables naming one stream read it once.
- #264 P1; JudgeStreamPastGuard mirrors #156 (NamedAgain + ReportNoStream) under rows guards.
- #221: reader and validator both report; Warning/Warning.
- #241: distance "N byte(s) before|after where the index said" / "before|after it"; each site keeps its digit grouping
  (inconsistency filed as debt); verb/pronoun agree; CountText in IO namespace shared by reader and validator; new
  messages use it; four call-site conditions with no count-of-one test get tests.
- Test spacers (16 sites) removed.
- Landing order: #317; #241 CountText; #197 + #171 edge; #261 (+ stream-end rule, B19-7); #188/#281 positions and
  DeclaresItselfASection; #208 + RefuseDeclared (+ timing test); #207/#210/#222 + R; #156; #264; #221.
- Debts to file (M02 unless said): S8 (Q12); A5 (Q11); N-TND (table whose trailer keyword has no dictionary ends
  the chain in silence, ipres2017/t04-009); startxref names an unreadable stream -> only xref.rebuilt (B19-3
  asymmetry); classic table mid-chain with a bad row/subsection header/stray token stops the chain (T1-T3);
  SectionMalformedRule counts a section only when TrailerFault == None (check intended); digit grouping
  inconsistent + object-stream header fault worded twice; signed-number header neighbor ('4 +0 obj' missed by scan,
  '+4 0 obj' taken as 4); /Prev or /XRefStm in a trailer MaxTrailerLength cut never judged (or comment #163);
  file.root-invalid 'No object of the file is a catalog' after a rebuild found one (check open debts first);
  corpus-contributions: a real incremental update with a damaged middle cross-reference stream.
- Comments: #171 (left edge paid; chunk overlap misses comment headers > ~61 bytes), #292 (rows guard + MaxObjectLength
  path), #317 neighbor (Four_times_as_many... at room max with bound 12 — note only).

# Implementation
Workflow wf_38fa60d3-d24 (lot19-implement): track (debts + comments), stages 1-flaky, 2-messages, 3-window,
4-comment-headers (+ B19-7), 5-relocation, 6-stream-prev, 7-partial, 8-xrefstm, 9-guards, 10-reserved, 11-docs,
12-check; each stage in its own worktree, pushing to ccr-6d6c0ee6-dqm4lm-188.
- 2026-11-04: container restart stopped the run during stage 6; resumed (same run id, task w99bg12km). Done before:
  track filed #320 (dropped candidate's rows, batch 20), #321 (/XRefStm copied 2+ revisions late, batch 31), #322
  (trailer keyword with no dictionary, batch 22), #323 (startxref section unreadable -> only xref.rebuilt, batch 22),
  #324 (classic table mid-chain with a bad row stops the chain, batch 27), #325 (xref.section-malformed not reported
  when the trailer is also at fault, batch 22), #326 (digit grouping, batch 26), #327 (signed header numbers, batch
  21), #328 (/Prev in a trailer MaxTrailerLength cut, batch 27); commented #260 (file.root-invalid after a rebuild),
  #171, #292. Commits 4c744a5 (#317), 954cef7 (#241), 768e06f (#197), e67fef9 + b09f981 (#261, B19-7), d5b4d48
  (#188, #281). Notes for review: '% 1 0 obj' on obj's own line is taken by the scan and stream-end rule though a load
  reads a comment (stage 4 residual, unfiled); a line starting inside a multi-line string is lexed from the line's
  start; stream-end search ~1.6-4x slower on pathological lines (linear, no allocation).
- Implementation finished 2026-11-04: 4c744a5..071f829 (12 commits; 8c33e4b #208, 54d56c6 #207/#210/#222, 7d35896 #156,
  294d550 #264, 01426c1 #221, 071f829 docs). Check stage: build 0 warnings; whole suite 9,817, 0 failed, 3 skipped;
  format clean; commit rules OK; corpus diff vs main: 1,072 rewordings in 28 docs (#241) and pdfmaker7's false
  file.size-wrong gone, nothing else. Residuals the stages reported: residuals-impl.md.
  ADR 34 amendment says "Amended on 2026-11-04, while #264 was fixed" (not "by the maintainer").

# Review round 1 (wf_8527b0df-f42 on 071f829): 49 findings, 47 confirmed or narrowed (10 major, 27 minor, 10 nit),
# 2 refuted ([2] hybrid-in-part-refuses-its-own-stream, [37]). Findings in r1-findings.md ('######## [n]').
Maintainer (2026-11-04), all as recommended:
R1-Q1 [3]: a relocation candidate that declares itself a section (xref keyword, /Type /XRef) but cannot be read ends
   the search when it is the nearest: taken as the section found moved (RelocateTo, xref.offset-adjusted), returned
   Malformed so the chain reports it (xref.section-unreadable), its declared numbers refused, a /Type /XRef stream
   whose dictionary was read whole hands its /Prev on (#208); validator: section-shifted + section-malformed. #320's
   expected outcome changes (its oldest table taken): comment on #320.
R1-Q2 [10]: a classic table named by /XRefStm that is Malformed (not cut by a guard): refuse what it surely lost (rest
   of the faulty subsection, _rowsLost) AND, where what follows the fault cannot be known (a bad subsection header, a
   stray token, the end of the file after a whole subsection), refuse every number (RefuseEveryNumber); the test
   asserting 'old seven' (CrossReferenceChainTests ~:2143) changes; lazy-reading's principle covers it.
R1-Q3 [25]: the mirror order (/XRefStm read first, a later /Prev reaches it, reported as a loop): debt under M02
   beside #321. In this batch: SizeWrongRule does not judge a chain that ends in a loop (LoopOffset >= 0).
R1-Q4 [31]: #317: time several back-to-back probes per turn (e.g. 4), room 1,000, bound 8; validate over ~300 runs
   under four busy loops with the combined filter, and the header-scan mutant still caught; correct the comment.
Decided without a question (refuters' fixes): [0][9][21] 'reached' judged by the first token's start (StartOf
   helper; ReachedBefore and the /XRefStm lookup), a lost/handed-on stream's Offset+Padding recorded as reached;
   [1] branch (c) decided by the section both links reach (positions), not written offsets; [5] hand-on message
   only says the chain goes on when /Prev reads as an offset; [6][24] branch (c) wording when outside the file or
   nothing there; [14] messages of a relocated section name the offset named and where it was found; [27][43] a
   startxref-named object of another /Type with a /W converted as /Prev ones are (no 'cross-reference stream' said
   of a font), comment on #323 not needed; [26] an /XRefStm naming a Malformed table also gets ReportNoStream
   (hybrid-stream-lost/-wrong); [8] a proper prefix of 'trailer' at the end of the file is the end-of-file case;
   [7] a table a rows guard cuts before its first row is the guard's whatever the index holds (windowFull ->
   true); [4][11] LeadsPastIndex consults the refused ranges; RefuseRowsLost after the TrailersHiddenByGuard
   block; [12][13][23][30] read-in-part message tails (guard, FaultBesideCut rows not read, row count agreement,
   /XRefStm-named cut table); [15] LoseRows joins adjacent ranges; [32] test for RefuseRanges after every number;
   [16] stream-end search: EndObjTail/endObjSeen restored at a header found to start before where they read
   (copies at the carried numbers' starts); [17][46] commit a stream-end search benchmark and an object-scan rebuild
   benchmark (shapes listed), a timing test for the stream-end lexing on a long line of one-byte tokens, the
   threat model's figure; [19] property: the stream-end rule takes the header the scan takes (signs excluded,
   #327); [22] ReservedTypeRule's clause by WasRepaired; [28] Flate checksum on zero bytes decoded; [34] lost
   streams timing bound 12; [35][36] test renames; docs [20][29][38][39][40][41][44][45][48]; [18][42] status.md
   record of 2026-09-30 amended at the end; [47] M04.md note in the PR.
Tracking: debt for the value guards (CutByValue, FaultBesideCut with rows) leaving /Prev unjudged (mirror of #264);
   debt for R1-Q3; comments on #171 ('% N G obj' on obj's line taken by scan and stream-end rule; a line starting
   inside a string opened earlier), #322 (rows running into '<<' with no trailer keyword stay Malformed while an EOF
   cut keeps the table in part), #260 (a file whose only section holds only reserved rows -> file.root-invalid;
   add a test), #320 (R1-Q1 changes its expected outcome).
Round 1 fixes: workflow wf_a2870240-11e (track, R1a-reached, R1b-relocation, R1c-sections, R1d-headers, R1e-validator-tests, R1f-docs, R1g-check).
- 2026-11-04: second container restart during R1a; its unpushed commit 477971d + WIP saved on local branch lot19-r1a-wip; run resumed (task wkwoke1ii), R1a told to start from it.
- 2026-11-04: PAUSED at the maintainer's request ("Fais une pause"). Run wf_a2870240-11e stopped during R1c.
  Pushed: c29761c (R1a, reached by first token) and 0353e24 (R1b, relocation). R1c's unfinished work saved on local
  branch lot19-r1c-wip (c1775ff, includes probe ZzR1cProbe.cs: never push). Track filed #329 (value guards' /Prev,
  batch 27) and #330 (mirror order of #156, batch 31); commented #171, #322, #260, #320.
  TO RESUME: Workflow({scriptPath: lot19-r1-fixes-wf_a2870240-11e.js, resumeFromRunId: 'wf_a2870240-11e'}); the
  script's R1c prompt already says to start from lot19-r1c-wip. Then R1d, R1e, R1f docs, R1g check, then round 2.
- 2026-11-04: at the maintainer's request, R1c's WIP pushed as remote branch ccr-6d6c0ee6-dqm4lm-188-wip (9a45b9c, [skip ci], holds the probe; not for merging). Delete it once R1c's commits land on the batch branch.
- 2026-11-04: working notes pushed onto ccr-6d6c0ee6-dqm4lm-188-wip as bb21cbc (notes/batch19: decisions, briefs, measurement summaries, diffs and probes, stage reports, round 1 findings, workflow scripts). The R1c prompt now starts from lot19-r1c-wip~1 (the code wip, 9a45b9c).
- 2026-11-05: resumed at the maintainer's request (task wsnw1c4z3); R1c starts from lot19-r1c-wip~1.
- 2026-11-05: round 1 fixes finished: c29761c (R1a), 0353e24 (R1b), 0fc7ce5 (R1c), b4cfda0 33b243f 2febbac 51d47af (R1d),
  de0fa05 0e974e6 23121f6 e548beb (R1e), 761ce0b (R1f docs). R1g check at 761ce0b: build 0 warnings; whole suite 10,005,
  0 failed, 3 skipped; format clean; corpus vs main: only #241 wording and pdfmaker7's false size-wrong gone. #317 now
  k=4 probes a turn: 300/300 combined runs and 100/100 alone under four busy loops, ratios 2.1-7.7. Mutation per stage:
  R1a 146 (130 killed, 6 eq, 4 superseded, 6 n/c), R1b 63 (54/2/7), R1c 85 (82/1/2), R1d 123 (104/8/11) + 15 helper
  (11/4), R1e 14 (14). For status.md: benchmarks (StreamEndSearchBenchmarks, ObjectScanBenchmarks) figures in
  r1-fix-result.json; e67fef9/b09f981 bodies under 'Commit bodies not rewritten'; amend the 2026-09-30 record (:2900);
  PR notes: M04.md clarification, B19-8 fourth way, b09f981's long-line claim. For round 2: a section read in part
  after a newer section refused every number still says 'the rows read were kept'.
  The -wip branch is kept as the off-container backup of these notes (local lot19-r1c-wip deleted by R1c).
- 2026-11-05: review round 2 launched: wf_cc2b032a-f83 (task wnqp7obdr), lenses chain, sections, headers, validator-tests, docs, on 761ce0b.

# Review round 2 (wf_cc2b032a-f83 on 761ce0b): 24 findings, 23 confirmed or narrowed (3 major, 10 minor, 10 nit), 1
# refuted ([12]). Findings in r2-findings.md ('######## [n]'). No maintainer question: every fix implements a decision.
- [0] the /XRefStm's search stops at leaveAt only where the /Prev would keep its own place (it reads, a guard cut it,
  or it declares itself a section there); otherwise it goes on (font/CIDFont/string shapes, constant shift).
- [1] mirror of branch (c): a /Prev whose search comes to the start this table's /XRefStm read in the stream's place
  (links unread) takes it and reads it whole; the /XRefStm's record becomes the branch (c) naming; a real stream so
  read gets the branch (c) report.
- [2] a Malformed table candidate declares itself only where its 'xref' is a token a load lexes from its line's start
  (not in a comment or a string on that line); else dropped (ForgetReadThrough). Not widened at the named offset
  (B19-1 unchanged there: a load at that offset reads the keyword).
- [3] cross each position's white space once (memo PaddingAt); [4] a loop through an exact white-space offset placed at
  the section's start; [5] OffsetImpreciseRule counts each written offset once; [6] branch (c) text under the section
  count guard; [20] the loop record says 'reached but not read' for a lost section started there.
- [7][14][19] a section read in part after a newer section refused every number (or a range covering its rows) says
  none of its rows stands (recorded independent of read order); [8] tail by SubsectionsUnknown; [9] LoseRows joins
  ranges meeting on either side; [10] an /XRefStm-named table cut before its first row is not said read.
- Tests/docs nits: [11] benchmark summary; [13] #317 comment (heavier load); [15] R1-Q2 theory column; [16] checksum
  theory rename; [17] rename; [18] 'without load'; [21] XML doc + reference wording; [22] rebuild moments; [23]
  threat-model size-wrong loop clause.
- 2026-11-05: round 2 fixes launched: wf_66d7eddc-e11 (task w01pj3hun), script scratchpad lot19/lot19-r2-fixes.js: R2a-chain, R2b-sections, R2c-nits (+docs, site), R2d-check.
- 2026-11-05: session restart during R2d; R2a-c had pushed d63f093..19122f0 (15 commits). Run resumed (task wh6nnahqq): only R2d (check) reruns.
- 2026-11-05: round 2 fixes finished: R2a d63f093 394028c b9de553 cb22c69 d5d99f3 7c0748b 5656c64; R2b 95d8e0f 930d26f;
  R2c 734487e 46e4796 2a84c29 1bdbf98 b88ee54 19122f0. R2d check at 19122f0: build 0 warnings; whole suite 10,114, 0
  failed, 3 skipped; corpus output unchanged from 761ce0b. Mutation: R2a 108 (100 killed, 6 eq, 2 n/v), R2b 85 settled.
  Open for round 3: R2b's wording gaps (a stream read in part whose one row it refused itself still 'kept the row it
  holds'; a table read in part that refused a row of its own still 'the rows read were kept'); comment on #330 the
  trade-off (an /XRefStm passing a /Prev that does not keep its place may find a stream farther off); corpus-
  contributions.md:310 '(batch 19)' (precedent on main) and the unused duplicate [#51] at :750-751 (pre-existing).
- 2026-11-05: review round 3 launched: wf_46f5fc30-494 (task wgma2wgur), script lot19/lot19-review-r3.js, on 19122f0.
- 2026-11-06: round 3 stopped by the weekly usage limit (16 refuters failed; 5 reviewers + 2 refuters done); resumed at the maintainer's request (task wy11m56gh): only the 16 refuters rerun.

# Review round 3 (wf_46f5fc30-494 on 19122f0): 18 findings, all confirmed or narrowed (4 major, 9 minor, 5 nit). Findings
# in r3-findings.md. No maintainer question (none flagged); every fix implements a decision already taken.
- [0] KeepsItsPlace answers as the /Prev's own reading does: the keyword, /Type /XRef, a dictionary a guard cut before
  its /Type, and an object giving /W (untyped or of another /Type) only where it READS as a section: a dry read of its
  rows ('validate only' mode of the row reader, nothing indexed, nothing reported), not a rollback mark (that would
  settle #320's open choice). If a dry read proves too large, stop and report (maintainer question), do not file.
- [1] IsTokenOnItsLine lexes from the line's real start: read back through an internal-constant window (e.g. 4 KB,
  ADR 34 reason: a conforming xref begins its line) to the last end of line; no end of line within it -> the place
  declares nothing (dropped). Rename the test pinning window-start lexing; docs name only the #171 string limit.
- [8] the line test applies to a Malformed /Type /XRef stream candidate too (its header's number a token on its line).
- [10] a stop that came only from a guard cut, the /XRefStm naming a different offset: the naming is the guard's (no
  hybrid-stream-lost, no Error, index not marked incomplete); same offset: Error worded as the limit's.
- [2] the look at the /Prev's object made lazily (only when a candidate equals leaveAt); [3][14] a place the search
  tries is not remembered by the PaddingAt memo; [11] lengthen the test's chain.
- [4] RowsRefusedBefore only for another section's refusal; [5] reserved-row report under a newer refusal; [15] a
  section read in part that refused its own row(s): not said kept; [6] HighestNumber < 0 && !Incomplete; [9] the
  stream-end search when its first endstream lies inside a pending header's comment (B19-7 as written).
- Nits: [7][16] renames + threat-model citation; [12] chain-loop rule id summary; [13] reword the five texts to the
  check made (any /Prev the stream's dictionary gives); [17] 5656c64's body: not rewritten (pushed), record in
  status.md 'Commit bodies not rewritten'.
- 2026-11-06: round 3 fixes launched: wf_a4863e2b-02c (task w7u1bre9d), script lot19/lot19-r3-fixes.js: R3a-chain, R3b-sections-headers, R3c-docs, R3d-check.
- 2026-11-06: round 3 fixes done (wf_a4863e2b-02c). R3a 2b26d18 (KeepsItsPlace mirrors the /Prev's reading with a
  check-only row read that stops at the numbering, no rollback mark; line read back through LineStartSearchLength
  4,096, internal constant; /Type /XRef candidates under the line test; guard-cut stop with another offset is the
  guard's, same offset worded as the limit's; [2]'s optional lazy previousStart reverted: no saving outside loops).
  R3b 4e42737 ea348ce fb79663 8ccd868 (_refusedNumbers -> Dictionary<int, XRefSectionRecord>; reserved rows refused
  before reading 'stand for nothing', neutral wording; KeptButFor also in ReportLostSection; PendingEndStream reads
  up to two tokens past the keyword's line). R3c b4ab948 (lazy-reading, diagnostics, validation-rules, the
  XRefHybridStreamLost summary; stacked <summary> in CrossReferenceChainTests fixed; site built clean). R3d check at
  b4ab948: build 0 warnings; whole suite 10,223, 0 failed, 3 skipped; format clean; commit rules OK; no keys; corpus
  byte-identical to round 2's (only #241 rewordings + pdfmaker7's false size-wrong gone).
  Mutation: R3a 83 (killed incl. tests written; eq M26-28, M34, M36, M62, M81; withdrawn M05-M10); R3b 149 (140
  killed, 7 eq, 1 removed with dead line, 1 not viable). Open: M40/M41 (MarkCutByGuard in TryParseUnrecordedAt: no
  input built tells them apart) -> round 4 chain lens tries to build one; else file as debt (ADR 53).
  For status.md 'Commit bodies not rewritten': add 394028c, d63f093, 7c0748b (with e67fef9, b09f981, 5656c64).
  Optional #171 comment (earlier-string limit) left: the user docs and threat model now name it with a #171 link.
  Residual wording (pre-existing): a section refusing two rows of its own, each alone, says 'that row alone'.
- 2026-11-06: review round 4 launched: wf_ae5066b2-0d3 (task w745low0h), script lot19/lot19-review-r4.js, on b4ab948; six lenses (chain-keep incl. M40/M41, chain-line, sections, headers, validator-tests, docs).
- 2026-11-06: review round 4 done (wf_ae5066b2-0d3): 27 findings (1 blocker, 2 major, 12 minor after refuters, rest
  nits; [16] refuted). Findings in r4-findings.md ('######## [n]'), verdicts r4-verified.json.

# Review round 4 (wf_ae5066b2-0d3 on b4ab948): triage
R4-Q1 [0] (maintainer): a named section's object window opens at the first token PaddingAt found ("Au premier jeton
  (Recommandé)"): TryParseNumberedAt(absolute + section.Padding) in TryReadXRefStream; white space and dictionary each
  bounded by MaxTrailerLength; PaddingAt's padding-whole case reports limit.trailer itself (keeps the three startxref
  white-space rows); the check, StartOfNamed, branch (c), ReachedAt stay and become true; reader-limits.md row, the
  PaddingAt and KeepsItsPlace remarks restated; rows (a), (b) at run = limit and limit - 6, (c), each failing on b4ab948.
- [1] previousStart only where the /Prev can come to the place (not ReachedBefore(previous, visited)); rows typed
  /XRefStream with /W via a reference + loop, untyped and white-space variants; KeepsItsPlace/leaveAt docs.
- [2] test killing M40/M41 (trailer window ending on 'stream' + a /W width a guard cut); record killed, no debt.
- [3] blocker: IsTokenOnItsLine returns false when bytes.Length <= before + at; StartsToken stops at EndOfInput and does
  not count it; tests with CutShortSource rows and a source capped at 4,096 bytes a read, with a bounded wait.
- [4] CR rows killing both '\r'-dropped mutants (read-back and in-window).
- [5] nit accepted: the per-place read-back is the cost round 3's [1] named, within MaxRelocationCandidates (remark
  says so). No change.
- [6] readAgain carries ReservedRowsRefused/RowsRefusedBefore from the /XRefStm's reading; rows incl. own-duplicate
  control. [7] RefusedRow arm's someRefused tail uses KeptButFor. [9]/[17] test a reserved row refused by the stream's
  own earlier row (both orders), chain + rule.
- [8] _refusedNumbers -> Dictionary<int,int> keyed to a section ordinal; state the cost (MaxIndexEntries remarks,
  threat-model rows 106/429).
- [10] no code: comment on #320 (refusals and _indexIncomplete a dropped candidate leaves; correct its 2026-11-04
  comment: a table whose xref keyword lies in a comment/string on its line is dropped too) — consistent with Q12.
- [11] PendingEndStream: _readTo = Math.Max; HeaderAt only when _tokenStart >= at else take the keyword; comment
  :92-93; test through a short-count source (block-limited too); comment on #52 (short counts and this search).
- [12] HeaderEnding(placed) writes Table(13...) and asserts object 12 placed. [13] Decide's scalar fast paths; two
  benchmark shapes (waiting keyword + comment lines / white space); benchmark remark; threat-model row 529 figures.
- [14] concepts/diagnostics.md names #171's obj-line gap and qualifies the sentence. [15]/[20] rewrap (+
  PdfDiagnosticCodes.cs:50). [18] [19] renames + threat-model citations. [21] comment on #171 (the chain's search near
  an offset as a fourth reader; regresses vs main). [22] sibling theory: the taken stream's own /Prev a loop, outside
  the file, not an offset (+ null row). [23] threat-model row 425 'but for'. [24] NamesWhatPreviousReads summary.
  [25] docs say what the reserved-row messages say. [26] status.md: 2b26d18 under 'Commit bodies not rewritten'.
- [16] refuted; for status.md #317 record: round 4 saw the ratio past 8 under heavier load (9.07, 9.68, 15.8).
- 2026-11-06: round 4 fixes launched: wf_170d4322-ba5 (task wtndj6zaz), script lot19/lot19-r4-fixes.js: R4a-window, R4b-line, R4c-sections, R4d-headers, R4e-docs, then R4-track (comments #320, #52, #171) beside R4f-check.
- 2026-11-06: round 4 fixes done (wf_170d4322-ba5, 7 agents, 0 errors). 22 commits b4ab948..8ba4589, all pushed,
  footers checked. R4f: build 0 warnings, suite 10,275 (10,272 passed, 3 skipped as before), format clean, 22
  messages pass, no keys; corpus vs main: one document differs after normalize241 (pdfmaker7-powerpoint-va-cancer
  loses its false file.size-wrong, #156); branch dump byte-identical to round 3's. Track comments: #320
  (issuecomment-6694391457), #52 (-6694397059), #171 (-6694401086). Results in r4-fix-result.json.
  Deviations kept: R4d reads the header eagerly at the token's first byte ('o') instead of HeaderAt only when
  _tokenStart >= at (matches the memory source in all short-read rows; the guarded variant does not); R4a reports the
  white-space guard in TryReadXRefSection (PaddingAt is also called by the reached test and KeepsItsPlace) and probes
  XRefProbeLength past the window so a token at the window's end is read; StartOf/StartOfNamed give -1 past the window;
  limit.trailer for a stream's dictionary now at its header. R4c: ordinal per reading (TryReadXRefSection entry),
  int, wrap noted; allocation test. R4b: guard on bytes.Length <= before + at, StartsToken stops at EndOfInput.
  R3b's 'placed' check was vacuous until fb35341 (no commit body false there).
  status.md items: 'Commit bodies not rewritten' add 2b26d18 (true only past MaxTrailerLength of the edge until
  f0d90c8), ea348ce (whole reads only until cfaa90c), 8ccd868 (overclaims; corrected in 056461b). Figures: every-row-
  refused stream opens with 1.01 GB (1.20 at b4ab948, 0.91 at 19122f0); 100,000 refused rows 6.04 MB (8.45, 4.83);
  waiting comment lines 87.3 -> 43.5 ms, nested strings 58 ms (~14 ns a byte) the worst shape.
  Open points triaged:
  - white space past MaxTrailerLength -> NotFound + nearby search (startxref rebuilt), and a classic table's white
    space counting toward MaxXRefSectionLength: these are #289 and #295 (M02, placed in batch 27 with #163), behavior
    as main but the guard's message and the stream's header window. No maintainer question: comment on #289/#295 at
    the end with what batch 19 changed (R4-Q1 window at first token; limit.trailer worded for white space; probe 32
    bytes past the window so a keyword at its end is read; #295's two shapes re-measured) and what is left.
  - ReadTrailerPastRows hang on short windows (pre-existing, main) + StartsToken(long) Span[0]: filed #331 (debt,
    area: reader, M03, placed with #52). threat-model.md:366 to cite #331 (round 5 fixes or docs).
  - P02 (Array.MaxLength clamp on the probe's reach, > 2 GB only): file with the batch-wide ADR 53 survivors (M23).
- 2026-11-06: #331 filed (ReadTrailerPastRows short-window hang, M03, placed with #52). Notes pushed to -wip (980cd2c). Review round 5 launched: wf_48083bc4-1e9 (task wqyaj1vax), script lot19/lot19-review-r5.js, range b4ab948..8ba4589, six lenses (window, line, sections, headers, validator-tests, docs) + a refuter per finding.

# Review round 5 (wf_48083bc4-1e9 on 8ba4589): triage
24 agents, 0 errors. 18 findings: no blocker, no major; minors [1] (narrowed), [7], [14], [15] (narrowed), [16];
nits [2] (narrowed from minor), [3], [4], [5], [6], [8], [9], [10], [12], [13], [17], [18]; [11] refuted.
Files: r5-findings.md ('######## [n]'), r5-verified.json, r5-reviewers.json. No maintainer question.
- [1] no code (root #289, batch 27): an /XRefStm whose own white space reached the guard is looked for nearby and
  batch 19's code then gives (a) the stop at the /Prev's place -> hybrid-stream-lost + hybrid-stream-wrong Error,
  (b) a table read in the stream's place -> chain-cycle + chain-loop Error beside hybrid-stream-wrong (B2 layout),
  where main gave a section-shifted Warning but served object 5 null and object 7's old revision (head serves the
  right objects). (C) refuted (the Error is the file's under any bound). Correction of round 4's triage line:
  'behavior as main' holds for startxref and /Prev, not for an /XRefStm. End-of-batch #289 comment: these
  consequences + close criteria widened to HybridStreamWrongRule, ChainLoopRule and xref.hybrid-stream-lost
  standing down, rows (A) and (B2).
- [2] nit: the white-space guard's message and texts say 'what follows them was not read'; false where
  MaxTrailerLength < ~512 (NearbySearchRadius) and the nearby search reads past the run. Reword to 'the section was
  not read from there' (message, PdfReaderLimits remark, reader-limits.md:91, lazy-reading.md:179,
  diagnostics.md:141, code comment :5102-5104, PaddingAt remark), pinned message in CrossReferenceRuleTests, a chain
  row (bound 256, /Prev naming the first of 257 spaces: limit.trailer, offset-adjusted, Relocated, 5 and 7 read).
  (b) refuted.
- [3]/[13]/[17] nit (one change): PaddingAt's cost remark: less than ten times the bytes from the position to the
  first token's end (white space, comments and the token, a token its edge cuts as far as the probe reaches), one
  probe where that token ends within the first. Do not restore 'a seventh more'. threat-model.md:463 aligned
  (main's wording, optional, do it).
- [4] no change (the docs' rule holds measured from the window; the proposed rewording would be off by one). The
  file's-first-line case one byte farther: accepted, reads a section rather than dropping one.
- [5] nit: IsTokenOnItsLine remark (bytes lexed run to the end of the first token past the place, within the bytes
  searched and read back) and the comment at :4968-4969 (bytes ending at or before the place lack its first byte).
- [6] nit: NearbyFiles.cs:681 summary and CrossReferenceChainTests.cs:1293-1294 comment, refuter's wording.
- [7] minor: ReportSectionReadInPart (and ReportReservedRows) moved after RefuseRowsLost at both call sites; where
  the section's own refusal refused every number (a stream /Index holding or being a reference; ranges merged past
  MaxRefusedRanges) and not EveryNumberRefusedBefore, the tail says 'every other object no section read before it
  indexes is looked for by rebuilding the index, rather than taken from an older section's rows'; keep the
  SubsectionsUnknown tail of a table startxref or a /Prev names (4 pinned assertions); rows E2, F1, E5 + /XRefStm
  path (V10), controls E1, E4; diagnostics.md:112 in-part list, lazy-reading.md:162. ADR 53.
- [8] nit: EveryNumberRefusedBefore settable and carried in readAgain beside the two copies; comment list; row
  asserting sections[2] false and the oldest true (kills the drop-the-copy mutant).
- [9]/[16] minor (one change): SearchEndStream remark (holds of windows of more than EndStreamSearchOverlap bytes,
  short or not; one of no more ... byte past its start), StreamEnds.cs:110-111, PendingEndStream.cs:27-29;
  PropertyTests.cs:466 stays; optional WindowEdgeTests theory pinning caps 11+ match memory for LF and CRLF.
- [10] nit: HeaderText.HeaderAt summary (reads only the bytes before; whether the token is obj is the caller's).
- [12]/[15] minor (docs only, within #289/#295's placement): 'read from its first token' holds for a stream only; a
  classic table's window opens at the offset named, its white space counting toward MaxXRefSectionLength ([#289]):
  PdfReaderLimits.cs:145-148, threat-model.md:429, lazy-reading.md:176-177, diagnostics.md:141 (white-space clause
  for any named section, table or stream); optional MaxXRefSectionLength rows (reader-limits.md:19,
  threat-model.md:104). Keep threat-model.md:105, reader-limits.md:21, :204-207.
- [14] minor: threat-model.md #52 gap split: the hang under a new [#331] entry (+ StartsToken(long) on an empty
  window), link definition.
- [18] status.md 'Commit bodies not rewritten': f0d90c8 ('looked for nearby as before' holds for a /Prev or an
  /XRefStm; startxref's section is rebuilt) and 8ba4589 (before it the LimitTrailer summary said nothing of white
  space; only the MaxTrailerLength remarks and reader-limits.md said 'near the offset').
- Open points: #295 end comment adds the cost measured (one naming through a 65,530-byte run reads 343,127 bytes,
  84,854 at 60,000, the table's windows re-reading the run; 7.1 MB at MaxTrailerLength 1 MB with a 1,048,574 run;
  bounded by MaxXRefSectionCount). #52 end comment may add: a /Type /XRef stream's header among the places a 4,096-byte
  cap drops (read whole: Malformed found moved; capped: NotFound, section-missing). R4c M11 recorded killed in its
  viable form (keep ++_sectionReadings, every Ordinal 0: 2 rows fail). Validator lens: the batch improves two guard
  cases vs main (256 spaces at a bound of 256 reads with offset-imprecise only; a /Prev naming them read in place).
- 2026-11-06: round 5 fixes launched: wf_e4c461ab-9b4 (task wdhxvbzfr), script lot19/lot19-r5-fixes.js: R5a-sections ([7] report after refusal, [8] carry EveryNumberRefusedBefore), R5b-window ([2] guard message, [3]/[13]/[17] PaddingAt cost remark, [15] src remarks), R5c-remarks ([5] [6] [9]/[16] [10]), R5d-docs ([12] [14] [15] pages, #331 entry), then R5f-check.
- 2026-11-06: container restarted during R5a (wf_e4c461ab-9b4 lost, nothing pushed, R5a's worktree clean); the 24 round 5 review worktrees and R5a's removed; round 5 fixes relaunched: wf_56d89d35-9d1 (task wfzb1xcws).

# Review round 5 fixes (wf_56d89d35-9d1): result

- 8ba4589..65306d4, 11 commits, all footers checked:
  - R5a: 8692234 [7], ea32a78 [8], f3c78d4 (docs for the widened guard arm).
  - R5b: bd70ef5 [2], a2c5ed4 [3]/[13]/[17]/[15].
  - R5c: f85a814 [9]/[16], 4b2dcaf [5], b3540ba [10], c57c9ef (WindowEdgeTests caps 11/12/40), cdcf461 [6].
  - R5d: 65306d4 [14]/[12]/[15]/[2] pages.
  - R5f: no commit.
- Deviations accepted:
  - 'every object' tail where the stream kept no row (matches ReportLostSection).
  - The guard-left-unread arm also tests _everyNumberRefused.
  - The table arm split: SubsectionsUnknown and '/XRefStm' give every other; the rest-of-table tail otherwise. A startxref or /Prev table that both lost more than 4,096 ranges and has unknown subsections now uses the rest-of-table tail.
  - 'short of its edge' in the PaddingAt cost sentence.
  - The StreamEnds wording names the window of the file.
  - HeaderAt parameter renamed tokenStart.
  - reference/reader-limits.md :21 and :206 reworded under [2].
  - The limit.xref-section-length row in reference/diagnostics.md:139 gains the #289 clause.
- Mutants:
  - R5a: 34 mutants, 33 killed, 1 not viable (CS8510), 0 survivors.
  - R5b: 3 mutants on the message line, all killed.
  - R5c and R5d: no code.
- Probe cost measured (R5b): worst 9.14x at 2,044 spaces + xref (64/7). The cap gives 84,288 / 65,568 = 1.286.
- R5f:
  - build 0/0; suite 10,299 / 10,296 passed / 3 skipped / 0 failed; format clean; msgs OK; keys: 0 new; links OK.
  - Corpus vs main: only pdfmaker7 (file.size-wrong gone), as in round 4.
  - Branch dump byte-identical to round 4's.
- Open for the end of the batch: the #289 comment adds the LimitXRefSectionLength summary and message ('runs past' when white space fills the window, pre-existing on main), and may cite the probe-cost figures.
- 2026-11-07: review round 6 launched: wf_aab3e6f6-540 (task w1wd2dslv), script lot19/lot19-review-r6.js, six lenses (sections, window, short-reads, tests, docs, batch differential) over 8ba4589..65306d4.

# Review round 6 (wf_aab3e6f6-540 on 65306d4): triage
20 agents, 0 errors. 14 findings, none refuted:
- major: [14] (narrowed);
- minor: [6] (narrowed);
- nits: [1], [2], [3], [4], [5], [7], [8], [9] (narrowed from minor), [10], [11], [12], [13].
Files: r6-findings.md ('######## [n]'), r6-verified.json, r6-reviewers.json.
Settled:
- [14] major, regression vs main since 0353e24 (R1-Q1), not new in round 5. The search near an offset takes 'xref' or a
  /Type /XRef header inside uncompressed stream data (anywhere on its line, its middle included) as the section found
  moved, Malformed. The reader then gives section-unreadable, an Error section-malformed and a rebuild, serving a freed
  object or an older revision (/XRefStm). Main read the real section.
  Object bodies ('[ 1 2\nxref 3 ]') are narrowed out: a load lexes them, as R2 [2] decided.
  The read-order dependence in h00002 is B19-1's path, not the search's.
  Precedent: round 4 [21], strings an earlier line opened (docs + #171 comment).
  -> maintainer question R6-Q1.
- [6] minor, docs: the threat-model #52 clause 'a read of more, short or not, leaves the search as whole reads leave it'
  is false, because each read counts the ten bytes it repeats toward EndStreamSearchPasses (11x at 11-byte reads). The
  data end of that search holds, but its endobj judgment and later searches may stop sooner. Reword per the refuter.
  The #52 end comment may add the two-stream budget reproducer and correct 'one byte past the short one's start'.
- [1]/[12] nit, docs: the 4,096-ranges clause carve-out for a startxref or /Prev table at the chain's end whose rest
  cannot be known (rest-of-table tail, the same objects). Where: diagnostics.md:112, threat-model.md:431, the
  ReportSectionReadInPart remark, the XRefSectionUnreadable summary (+ verb 'indexes'), lazy-reading.md:166 (also the
  deviation-6 clause). Optional: a chain row with 4,097 lost one-row subsections cut by EOF under startxref (pins the
  deviation-3 tail).
- [2] nit, docs: diagnostics.md:112 states the split the code makes ('every object where a stream holds no row, its one
  row was refused or does not stand, or a guard left its rows unread'). No test rename for this one.
- [3] nit: the PaddingAt cost sentence and threat-model #47 note give the two cases as alternatives (one probe where
  the token ends within the first, short of its edge; otherwise less than ten times).
- [4] nit: the _paddingProbes remark (:347-348): a header whose number carries leading zeros past the edge grows the
  probe within the search's window, bounded by MaxRelocationCandidates.
- [5] nit: rewrap the ReachedAt remark (:3660-3662), the lone 'which'.
- [7] nit: the SearchEndStream remark (:2470-2471) and threat-model #52: the waiting-comment exception holds once a
  window has held the keyword whole (and, optional, row 541 says 'the reads after it').
- [8] nit: the WindowEdgeTests comment (:803-804), caps 1-3 run past object 12's header to its endobj.
- [9] nit: pin the true half of the readAgain copy (newer stream /Index no array before the hybrid; Sections[4]
  EveryNumberRefusedBefore and RowsRefusedBefore true; diagnostics list). Kills K2 (call-site replacement with false).
- [10] nit: rename ..._says_every_object_is_looked_for_whatever_rows_it_holds to
  ..._says_every_object_but_a_row_it_kept_is_looked_for_whatever_rows_it_holds; threat-model.md:431 citation.
- [11] nit: the HybridFiles.PrevFallingShortOfTheStreamBefore summary names the two shapes that index object 7.
- [13] nit: the StartOfNamed (:3709-3711) and KeepsItsPlace (:3733-3734) summaries say the reading from the offset
  comes to no start past the run, though its search near the offset may find the section past it. StartOf unchanged.
- status.md 'Commit bodies not rewritten' += 8692234 ('keeps its words' is false for the >4,096 cut-table shape; 'every
  object where the stream kept no row' is the RowsHeld split).
- End-of-batch #289 comment adds:
  - f02393: under MaxTrailerLength 256, a /Prev naming the first of 511 spaces before a table below offset 512 gives
    section-missing + Error section-not-found + a rebuild. Main found it through its 1,024-byte window (#197's
    intended fix); under default limits it reads with offset-imprecise only.
  - the probe-cost figures.
- Batch lens: no throw or hang; doubled reports 26 on main, 0 on head; silent older revisions 51 -> 14 (all
  pre-existing). Read-order differences only through rebuilds (documented). Valid variants add nothing over main.
  Timing tests stable idle and under load.
- R6-Q1 (maintainer, 2026-11-07): [14] document and track under #171, as recommended. No code change. The texts name stream data beside strings an earlier line opened; a test pins the behavior citing #171; a comment on #171 gives the reproducers.
- 2026-11-07: round 6 fixes launched: wf_cf83bb0a-d9e (task wzgbeykjn), script lot19/lot19-r6-fixes.js: R6a-src (source texts + tests [9] [10] [11] [8] [14] gap test, cut-table row), R6b-docs (pages [6] [7] [1]/[12] [2] [3] [14]), then R6-track (#171 comment) beside R6f-check.

# Review round 6 fixes (wf_cf83bb0a-d9e): result
- 2026-11-07: done. 7 commits 65306d4..d3ef236, footers ok (grep -qx):
  a867e66 docs (IsTokenOnItsLine / stream data, Refs #171); e4130e8 docs (remarks [3][4][5][7][13][1/12], Refs #156 #289 #52 #261 #210);
  2ff7781 test (two #171 pinning tests: /Prev 4 rows, /XRefStm 2 rows; builders NearbyFiles.UpdatePastAWordInStreamData, HybridPastAWordInStreamData);
  4778af4 test ([9] readAgain true side Fact + HybridFiles.PrevFallingShortOfTheStreamBeforeALostStream; cut-table theory 4 rows 4,096/4,097 x startxref/Prev + PartialFiles.CutAfterLostSubsections; [10] rename; [11],[8] texts);
  b446b1f docs ([6][7] #52 entry, [3] #47 cost note); de12b13 docs ([14] threat-model gap + row, diagnostics :110, validation-rules :56, lazy-reading :202-208);
  d3ef236 docs ([1]/[12] carve-out, [2] split, [9] citation).
- K2 (readAgain copy replaced with false) KILLED by exactly 1 failure: the new [9] Fact. Cut-table old form (naming=="/XRefStm" || _rowsLostPast) killed by the 2 rows at 4,097.
- [14] probes: '%' or '(' on the same line hides the word; a string opened on an earlier line does not; /XRefStm with xref word -> every number refused, rebuild at opening serves new; header decoy -> older revision read first (ascending).
- [6] probe: cap 11 counts x10.99; 43,559-byte file streams 30,000/9,000: stream 7 taken without a search at cap 11 (9,018 vs 9,000); 60,000/9,000 caps 11-12; endobj unjudged at cap 11 with 80 spaces + 'foo'.
- R6f on d3ef236: build 0/0; unit 10,310 total / 10,307 passed / 3 skipped / 0 failed; format ok; site 269 pages ok; msgs 7/7 ok; keys 0 in round 6, batch 9 = round 5 (false positives); linkcheck ok; 1,158 / 310 citations exist; corpus: branch dump byte-identical to r5f (761,206 bytes), main identical; one document differs after normalizing (pdfmaker7 loses false file.size-wrong).
- #171 comment posted: https://github.com/AdCodicem/AdCodicem.Pdf/issues/171#issuecomment-6703927674
- Open points carried: lazy-reading.md:285 'stream data is not even read from disk' (pre-existing; the search near an offset reads 512 bytes either side, stream data included) -> known for round 7, may become a nit; status.md:2391 cites a missing test name (historical record, pre-existing, leave); [#51] duplicate in corpus-contributions.md (pre-existing).
- status.md additions: 'Commit bodies not rewritten' add 8692234 ('keeps its words' false for the cut-table shape), f0d90c8, 8ba4589 (already listed).
- #52 end comment: two-stream reproducer (43,559 bytes; 30,000/9,000 at cap 11; 60,000/9,000 at caps 11-12), endobj unjudged at cap 11; correct 'one byte past the short one's start' to reads <= 10 bytes.
- Worktrees cleaned (23 removed). Local = origin = d3ef236.
