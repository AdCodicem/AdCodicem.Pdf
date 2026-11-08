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
- 2026-11-07: review round 7 launched: wf_08ed9f52-eed (task wrq95q3zu), script lot19-review-r7.js, range 65306d4..d3ef236, six lenses: src-texts, tests, docs, stream-data, issues (each closed issue paid completely), batch.

# Review round 7 (wf_08ed9f52-eed on d3ef236): triage
27 agents, 0 errors. 21 findings, none refuted:
- major: [18] (narrowed, maintainer), [20] (narrowed, maintainer);
- minor: [7] (narrowed), [13] (narrowed), [14], [15] (narrowed; tracking choice);
- nits: [1] [2] [3] [4] [5] [6] [8] [9] [10] [11] [12] [16] [17] [19] [21].
Files: r7-findings.md, r7-verified.json, r7-reviewers.json, r7-raw.json.
- [18] major, regression vs main through #221 (01426c1): an xref stream whose filters decoded nothing (Flate failing
  before any output, an unsupported filter, /Fl) has its encoded bytes read as rows (pre-existing fallback); since #221
  a row of type > 2 (zlib's 0x78) reads as null and no older row stands -> null where main served the older section's
  object or a rebuild. Messages 'gives it type 120', 'the row it holds was kept'. Writer-like damaged update: main served
  'old four' (wrong too), head null. Single-section damaged stream: head adds entry-reserved x3 and file.root-invalid.
  Empty data already takes the read-in-part path and serves right. -> R7-Q1.
- [20] major, regression vs main since 394028c (round 2 [1], 'tried last'): hybrid table with /XRefStm naming the
  previous revision's stream and /Prev 7 bytes off it, plus the update's own xref stream named by nothing within 512
  bytes: head takes the own stream (no /Prev) -> chain ends, rebuild at opening, freed object 5 served (via rebuild),
  false Error file.root-invalid. Own-prev variant: chain-cycle + Error chain-loop. Main read the chain whole. Shape A
  (/XRefStm names the table's own stream with a /Prev inside itself) is why the rule exists: head reads whole, main loops.
  No test pins the ordering (mutant passes 7,675). -> R7-Q2.
- [15] minor, pre-existing: a section in stream data that reads (whole or in part) is accepted before the line test
  (PdfFileReader.cs:4944); chain goes through its trailer (ends, or follows a quoted /Prev). Texts name only unreadable
  places; closing #171 needs the stream-data check on every candidate. -> R7-Q3 (tracking: #171 or own debt).
- R7-Q1 (maintainer, 2026-11-07): [18] 'Sans rangées' as recommended. An xref stream whose filters decoded nothing (a
  Flate failure before any output, an unsupported filter) holds no rows: Malformed with its own fault ('its data could
  not be decoded'), its declared numbers refused to older rows (B19-2), its /Prev handed on (#208); the rebuild serves.
  Expose the failure through a new flag of the filter pipeline, not leftEncoded (the guards'). The fallback reading raw
  rows under a wrong /Filter goes (the rebuild catches them).
- R7-Q2 (maintainer, 2026-11-07): [20] 'Juger au /Prev' as recommended. A candidate stream that may be the table's own
  is told by its own /Prev: one whose dictionary gives no /Prev, or a /Prev naming the offset the table's /Prev names,
  is tried after every other place (the /XRefStm's stream dropped when it gives no /Prev, as now; any other such
  candidate moved to the end); the stream the /XRefStm read keeps its nearest-first place when its /Prev names another
  offset. Tests: the finding's shape (own stream no /Prev), the own-prev variant, shape A with the stream's /Prev.
- R7-Q3 (maintainer, 2026-11-07): [15] under #171 as recommended: widen the threat-model #171 entry (a readable section
  in stream data is taken before any line test; chain goes through its trailer); comment on #171 with the reproducer;
  closing needs the stream-data check on every candidate. No code change.
Settled without a question (texts and tests):
- [1]/[8]/[16a]/[19]: 'anywhere on its line' -> 'wherever its line, lexed alone, reads it as a token, its middle
  included' (threat-model #171 entry, lazy-reading :203, the acceptance comment :4942, IsTokenOnItsLine remark if
  touched); TryRelocateXRefSection remarks :4810-4814 name stream data and a string an earlier line opened (#171).
- [2]/[10]: carve-out in five places (ReportSectionReadInPart remark, XRefSectionUnreadable summary, diagnostics.md
  :112, lazy-reading :167, threat-model :434): 'however many ranges it lost' + 'unless a newer section refused every
  number before the chain came to it, which its message says instead'.
- [3]: _paddingProbes remark: 'at most two more, of up to 256 and 2,048 bytes, where leading zeros carry a header's
  number to that edge or past it: they may read past the search's window, but the number lies within it'.
- [4]/[13]: the #171 end-of-batch comment corrects: offset that misses (short or past); stream data whose bytes hold
  the text as written (no filter, Flate stored block, RunLength literal runs); with no real section within 512 bytes
  the place is still taken (section-unreadable + Error section-malformed instead of section-missing + section-not-found,
  same rebuild); 'No load lexes an embedded file's data' (object streams' members are lexed, class of object bodies).
  Optional test comment CrossReferenceChainTests :1197 'No load lexes an embedded file's data'.
- [5]: rename the two tests (…_where_the_word_xref_in_a_comment_or_a_string_lies_between_…, The_word_xref_in_a_comment_
  or_a_string_on_a_line_that_starts_before_…) + threat-model :432 citations.
- [6]: the cut-table theory pins the code list (XRefSectionUnreadable, XRefRebuilt); optional same for :3339 sibling.
- [7]: threat-model #52: budget clause for every short read (about n times at n ≤ 10, eleven at eleven, twice at
  twenty). #52 end comment: two-stream reproducer at caps 8 to 11 (60,000/9,000 at 8 to 12), endobj unjudged 9 to 11.
- [9]/[14]: threat-model :465-466 'Telling stream data, and strings an earlier line opened, apart, which this issue
  asks for, closes it' — and with [15] 'asked of every place the search takes, one that reads included'. Optional pin
  of the string context (object body string opened on an earlier line) citing #171: do it (cheap, symmetric).
- [11]: validation-rules.md rows 54 (section-malformed) and 57 (section-shifted) name the #171 contexts.
- [12]/[17]/[21]: lazy-reading.md:282-284 rewritten to state what holds (no stream's data read for its own sake;
  windows read whatever lies in reach; searches named; rebuild; endstream search).
- [16b]: threat-model :464-465 older revision only for each number the header does not refuse; [16c] 'farther off,
  within the 512 bytes searched'.
- 2026-11-07: round 7 fixes launched: wf_5cf097f0-910 (task wnvtfthsg), script lot19-r7-fixes.js: R7a-undecoded (R7-Q1), R7b-own-stream (R7-Q2), R7c-texts, R7d-docs, then R7-track (#171 comment edited + new comment) beside R7f-check.

# Review round 7 fixes (wf_5cf097f0-910): result
- 2026-11-07: done, 8 commits d3ef236..012e13d, all pushed, footers checked (grep -qx), no issue number outside Refs.
  - 092b772 fix (R7-Q1, Refs #221, #208, #210): PdfFilterPipeline.Decode gains an `undecoded` out (unsupported filter, NotAName, NotAFilterEntry, Flate failing before output; not image filters, not empty data). TryReadXRefStream -> Malformed 'its data could not be decoded'; check path (KeepsItsPlace) decodes too with mark/rollback; dropped relocation candidates roll back decode reports (RollBackBesideLimits); FaultBesideCut/RowsHeldAtMost report undecoded. 22 rows fail on d3ef236, 2 controls pass. ADR 53: 35 mutants, 30 killed, 2 equivalent (M09 roll back after every drop; M14 check's Fault assignment, KeepsItsPlace never reads it), 3 not viable.
  - 3a1ac79 fix (R7-Q2, Refs #156, #188): ordering by own /Prev, new private TryParseNearHybridPrev; deferred streams nearest-first among themselves; /Prev compared as a direct integer only; null = none. 9 rows fail on d3ef236, 13 controls pass. ADR 53: round 1 52 mutants (8 survivors -> killed by new tests or vanished with refactor), round 2 18 all killed; P3 equivalent. Variants A3/V2/V3 killed.
  - a2eedd5 docs (src comments only), f409eee test (string pin + readable-section pin, renames, threat-model :432 citations), ed61728/8f8ac51/253f179/012e13d docs pages. Site build clean, 269 pages.
  - Track: #171 comment 6703927674 edited in place ("Edited on" line), new comment 6708779926 (readable section reproducer). Bodies in lot19/track7/.
  - R7f check: build 0 warnings; whole suite with remote corpus 10,370 / 10,367 passed / 3 skipped / 0 failed; timing/budget/alloc/memory/hostile x3 all green; format clean; keys 0 new; spelling 0; links ok; citecheck 313, namecheck 1,175; corpus branch dump byte-identical to r6f (761,206 bytes), main byte-identical too; only pdfmaker7 differs after normalizing (#156). R7-Q1/R7-Q2 reach no corpus document.
  - Open consequences to put in round 8's known list: (a) R7-Q2 'table before' row: /XRefStm right, table /Prev wrong a few bytes into the stream whose own /Prev names another offset -> branch (c) + Error hybrid-stream-wrong (decided rule; validation-rules row 60 states it). (b) stream of no /Type with /W and undecodable data takes the guess path (B19-1/#263): HybridFiles.PrevNamingAStreamBesideTheOldestTable("", filter:"/Foo") object 7 reads 'old seven' (d3ef236 'new seven'). (c) ThrowOnLimit: check's decode may throw at MaxDecodedStreamLength during /XRefStm search, same exception as the /Prev's own read. (d) lazy-reading.md ~:120 'whose data the reader decoded whole holds fewer rows' not reworded. (e) de12b13 body 'anywhere on its line' -> status 'Commit bodies not rewritten'.
- 2026-11-07: review round 8 launched: wf_6e5c9222-451 (task wamelbvhq), script lot19-review-r8.js, range d3ef236..012e13d, six lenses: undecoded (R7-Q1 code), own-stream (R7-Q2 code), src-texts, tests, docs (+ #171 comments, commit msgs), batch (new-seed differential + #221/#208/#210/#156/#188 still paid, #320-#331 still true).

# Review round 8 (wf_6e5c9222-451 on 012e13d): triage
38 agents, 0 errors. 32 findings: 3 refuted ([10], [20], [32]); majors [1]=[14] (dup), [3], [7]=[29] (dup family), [8], [9]=[13] (dup), [30]; minors [4], [5], [11], [23]; rest nits ([12] is an ADR 53 survivor, not a review minor).
Files: r8-findings.md, r8-verified.json, r8-reviewers.json, r8-raw.json; showf.py prints a finding.
Settled without a question (existing principles decide):
- [1]/[14] major (092b772): RollBackBesideLimits at the dropped candidate rolls back the parse's kept syntax.*/stream.* reports too, and _syntaxReported/_lengthFaultsReported keep them as made -> lost for good (invariant 5). Fix: mark taken just before the decode in TryReadXRefStream (candidate), recorded on the attempt, rolled back to that mark only. Tests: stray ']' rows (/Foo and /W [1 -4 2]), hybrid row key-repeated once + filter.unsupported once, /Length-over-data row. Correct the test comment ~:5069-5070.
- [5] minor: M09 is NOT equivalent (premise false); killed by the negative-width rows of [1]. Correct the R7a ADR 53 tally.
- [3] major: FaultBesideCut returns null before judging undecoded data when the whole /Size or /Index is cut -> guard blamed (ADR 34). Fix: judge undecoded before those early returns; rows '/Size 5 0 R' (/Foo, /FlateDecode) and '/Index 5 0 R'; summary + reader-limits.md clause.
- [4] minor: the check decodes a /Type /XRef stream although KeepsItsPlace keeps it whatever; skip the decode when TypedXRef; texts (KeepsItsPlace remark, check paragraph, :6239 comment, threat-model :432); cost test or survivor with reason.
- [9]/[13] major (3a1ac79): MayBeTablesOwn judged on a dictionary MaxTrailerLength cut -> hidden /Prev decides the section, no limit report, ThrowOnLimit ignored (ADR 34, #281). Fix: mayBeOwn = parsed is PdfStream && MayBeTablesOwn(...). Changes the ('stream 11', 300) row to the guard's outcome (limit.trailer, Malformed, checked-in-part); add the 70,000-byte /Pad shape (defaults, ThrowOnLimit, 1 MB) + /Prev-before-pad control. Also settles [16] (plain dictionary no longer deferred). Texts: remarks, TryParseNearHybridPrev summary, MayBeTablesOwn docs, lazy-reading/threat-model.
- [8] major: a deferred stream is counted twice (pass-over + retry) -> dense window or spent budget loses a section 5 bytes away. Fix: count a passed-over place once; retries of counted places run past the bound (reads < 2 x MaxRelocationCandidates windows); tests Dense(31 headers), Budget(28), freed-object row; texts (_relocationCandidatesTried, MaxRelocationCandidates summary, remark, threat-model :434).
- [12]: mayBeOwn initializer mutant survives -> kill it on the final code (stream past 16 ordinary objects).
- [2] nit: rename the writer's-update test to what its rows hold; optional threat-model :442 clause (damaged Flate may decode to garbage rows).
- Texts: [6]/[15]/[31] undecoded causes named in full everywhere (DataUndecoded summary, loop comment, TryReadXRefStream remark, pipeline comment, lazy-reading :113, validation-rules :54, FilterTests :1250) and 'one that could decode none' -> Flate corrupt before its first byte; [17] window sizes; [18] TryParseNearHybridPrev summary (what the test refuses); [19] pin limit.object (Equal form); [21] exact code list in the may-be-own theory; [22] builder summaries; [23] threat-model :442 rebuild clause inside /Type /XRef + the guess; [24] diagnostics :117/:133/:135 startxref exception (#323); [25] exact Flate wording + unknown filter in reader-limits :130-132, threat-model :616; [26] fold the deferred-order clauses (tm :432, diag :115, param doc); [27] lazy-reading :119-120 'decoded whole' -> no guard cut; [28] status.md record 092b772, ed61728, 253f179.
- Refuted, nothing to do: [10] (follows #188's rule; class worse on main), [20], [32].
For the maintainer (R7-Q2 refinements): [7]/[29]/[8] first-revision stream (no /Prev by Table 17) deferred -> wrong section, false hybrid-stream-wrong, rebuild, section-not-found; [11] /Prev compared as written (real, reference, offset naming nothing); [30] namings again (kagain, x_again, q_xref_again, q_plain_again; tied to #330/#321).
- 2026-11-07: before asking, prototype the candidate rules over the round 8 shapes and the 3,000-file differential (workflow lot19-r8-proto).
- 2026-11-07: prototype workflow launched: wf_0e14da16-684 (task wjt1xo2hx), script lot19-r8-proto.js: harness (r8proto/), variants P1-positional (P1a/P1b for [11]), P1-named-again (PNA-a/PNA-b for [30]), P2-by-xrefstm-stream, P3-prev-target, each with the [13] cut fix and [8] count-once; judge.

# R8 prototypes (wf_0e14da16-684): result
6 agents, 0 errors. Result in r8-proto-result.json (harness, variants, judge). Harness: r8proto/ (3,210 files, 9,228 blocks; manifest.tsv, ZzProtoDiffTests.cs.txt, classify.py, run_variant.sh; baselines out-main/out-d3ef/out-head, deterministic).
Worse than main (M/BB/plain); vs d3ef; vs head (worse/better):
- head 1434 (317/379/738); d3ef 697; -
- fixes (i)+(ii) only 1388; 650; 40/9
- P1 positional, /Prev as written 1159 (224/301/634); 416; 237/56
- P1a (resolved; /Prev naming no offset = no /Prev) 1217; 359; 349/59 -- flips 2 rows unintentionally
- P1b (resolved; /Prev naming no offset = another offset) 1187 (254/301/632); 386; 287/58
- PNA-b (P1 + NameAgain deferral) 1165; 422; 243/56. PNA-a (+ take named-again in place) 1164 (q_xref_again only; x_again order-dependent)
- P2 (by the /XRefStm's stream) 1204, 0 new vs d3ef, but loses kfirst x6, refs/g001618 unfixed
- P3 (/Prev target) 1326; leaves mother/lref/genA/(c)/outofwindow/cutown as head
- J1 (judge) = P1b + PNA-b's deferral + fixes: 1193; 392; 293/58. Seven classes 2,224 tests, 2 fail: pad-300 row (fix (i), intended) and offset-zero row (builder writes an impossible shape; rewrite).
- J2 (positional only where S gives /Prev) rejected: 1248.
Judge recommends J1. Rule: (1) a stream near the table's /Prev may be the table's own only if it starts at/after S (the stream the /XRefStm read, or the stream it names again); (2) such a stream judged by its /Prev as the chain reads it (real integer, reference resolved through sections already read; no /Prev or the table's /Prev -> deferred; S itself with no /Prev dropped; any other offset incl. none in the file -> nearest-first); (3) a guard-cut dictionary keeps its place, reported as the guard's; (4) each place counts once, retries run past the bound; (5) named-again stream stays passed over (#330 untouched), only arms (1).
J1 still wrong (measured): p_first x2 (older revision's hidden stream before S, nearer than its table -> chain ends, rebuild, false root-invalid; = main/d3ef, head read whole); g001581/g003714 (S gives no /Prev, first revision's stream taken 451-508 bytes off, middle revisions lost with only offset-adjusted/section-shifted; three baselines rebuild); [7](c) type ~76 seeded + cutown x2 + outofwindow x2 (= main: rebuild + root-invalid #260; head was silent older table); /Prev naming no offset (g002426, prevform f/g/h) = main + hybrid-stream-wrong; x_again, q_xref_again, q_plain_again (#330).
[12]: on J1 the mayBeOwn initializer mutant leaves all 9,228 blocks identical (only adds window reads): kill it with a counting-source assertion (ordinary object at/after S read through one window).
[30]: fix the deferral half now (NameAgain sets tablePrevious/streamStart from the named-again stream, +17/-3, only kagain x6 change, read whole like kfirst); file the take-in-place half beside #330/#321 with reproducers; #330 comment (its shape with /Prev a few bytes off reads whole on main).
- R8-Q1 (maintainer, 2026-11-07): 'Position puis /Prev' as recommended (J1 rules 1-4): a stream near a hybrid table's /Prev may be the table's own only if it starts at or after the stream S the table's /XRefStm read (or names again); a stream before S keeps its nearest-first place. Accepted consequences: p_first back to main's rebuild (false root-invalid), two seeded files read the first revision's stream ~500 bytes off (middle revisions lost, only a section-moved report), the offset-zero test row rewritten (no append-only writer makes that shape).
- R8-Q2 (maintainer, 2026-11-07): 'Comme la chaîne le lit' as recommended (P1b): a real with no fractional part counts as its integer, a reference as what a section already read places there, a /Prev naming no offset in the file counts as another offset.
- R8-Q3 (maintainer, 2026-11-07): 'Armer la règle, #330' as recommended: in the NameAgain branch the stream named again serves as S for the other streams (not taken in its place); kagain then reads like kfirst ('old five', 11 null, the passed-over stream reported). Taking the stream named again in its place (x_again, q_xref_again, q_plain_again) filed as debt beside #330/#321 with reproducers; #330 comment.
- Settled by invariant 5 (told the maintainer before the questions): a stream the search passes over as possibly the table's own, and that the chain never reads, is reported (Warning), so kfirst/kagain no longer lose its objects in silence (today only xref.offset-adjusted).
- 2026-11-07: round 8 fixes launched: wf_4942b351-0fe (task ww0l3qzb3), script lot19-r8-fixes.js: R8a-undecoded ([1]/[14], [5], [3], [4], [2], undecoded texts), R8b-own-stream (R8-Q1..Q3 from the judge's patch, fixes (i)/(ii), passed-over Warning, [12], texts; differential vs out-jdg-p1bna), R8c-docs, then R8-track (debt beside #330/#321 + #330 comment) beside R8f-check.
- 2026-11-07: the container restarted during R8b. R8a had finished and pushed 5 commits (5de20b7, 5d10a36, da0e157, e71eeda, 76f87e9; report in r8a-result.json). R8b's uncommitted work (+1,202/-174, new code xref.stream-passed-over; mutation pass 2 stopped after C8; survivors A4, B5, B7, C7) saved as lot19/r8b-wip/r8b-wip.diff against 76f87e9. Workflow resumed (task wxqs8tfao, same run id): R8a cached, R8b told to apply and review the WIP, then finish.
- 2026-11-08: a second container restart cut R8b's second run before its commit (suite 10,416 / 2 failed being fixed; mutants 47 killed, 1 not viable, survivors A4 B7 C7 C22 D1 D9 G4). Both restarts came while several dotnet builds/tests ran at once (two mutation workers + suite + corpus dump): memory. Its work saved as lot19/r8b-wip/r8b-wip2.diff (against 76f87e9). Script edited: R8b applies wip2, RESOURCE RULE (one dotnet process at a time, checkpoint diffs, commit and push as soon as the suite passes), same rule for R8c, R8-track and R8f, and R8-track then R8f run one after the other. Resumed as task wik8fq9qu (same run id; R8a cached).

# Review round 8 fixes (wf_4942b351-0fe): result
5 agents, 0 errors (third run, after two container restarts). Result in r8-fix-result.json (raw: r8-fix-result.raw). Branch 012e13d..da2b06f, 11 commits, all pushed, footers checked:
- R8a (5): 5de20b7, 5d10a36, da0e157, e71eeda, 76f87e9 (see r8a-result.json). R7a ADR 53 tally corrected: 35 mutants, 31 killed, 1 equivalent (M14), 3 not viable (M09 killed).
- R8b (4): 9a07bb9 (J1 rule: positional S, /Prev as the chain reads it, named-again S, fixes (i)/(ii), new Warning xref.stream-passed-over at the stream, added to manifest.schema.json, not made when the chain indexed nothing), cd19b17 (tests: count-once, guard a rebuild reaches), 7ff4f39 (StreamPrevious reads a /Prev through a reference chain, as ReadSectionOffset does; found by survivor H4; one cached read per link, at most MaxChainLength - 1), 06d6650 (threat-model citation rename). ADR 53: 87 mutants, 78 killed, 6 equivalent (A4, B7, C22, G4, H3, H12 with reasons), 3 not viable, 0 open. Differential vs judge's J1 run: 9,066 identical + 162 blocks that only add the passed-over Warning; corpus byte-identical to round 7. Suite 10,420 / 0 failed.
- Accepted deviations: new code instead of widening hybrid-stream-lost (index left as is; no validator rule); reference-chain read in StreamPrevious; `offset >= 0` dropped (-1 row pins it); DenseFreed folded into the count-once rows.
- R8c (2): d8e67de, da2b06f (pages only: threat model 432/433/434/442/616, lazy-reading, concepts/reader-limits, diagnostics, reader-limits (new 'What cannot be lifted' row for places tried: 32 counted, retries uncounted, reads <= 64), validation-rules). Site 269 pages, clean.
- R8-track: #332 opened (debt, area: reader + validation, M02, Blocked by: #330; three shapes + generator), comment on #330 (issuecomment-6716295153).
- R8f: all checks pass on da2b06f (build, suite 10,420/0 failed/3 skipped, format, messages, keys, links, citations 1,205, site, timing/budget/allocation x3, corpus main vs branch: only pdfmaker7 after #241 normalization, differential = judge's).
Open points for the batch's end (status.md / later docs pass, not blocking):
- threat-model.md:440 cell 1 ('leaves nothing of its reading') may read as covering guesses since 5de20b7.
- concepts/reader-limits.md:119-120 ('Data a filter decodes to nothing ends decoding too'): a later element can still report filter.unsupported (from reading the chain).
- R8a consequence: under a guard that cut /Size or the whole /Index, the data is now decoded to tell, so MaxDecodedStreamLength may be reached (and thrown under ThrowOnLimit) before the guard that cut the value. Not on any page; status.md or a reader-limits note.
- Commit bodies of round 8: none known false.
- 2026-11-08: review round 9 next, on 012e13d..da2b06f, at most three agents at a time (resource rule).
- 2026-11-08: review round 9 launched: wf_a01bab38-4ae (task wyb8smip7), script lot19-review-r9.js, lenses own-stream, undecoded, src-texts, tests, docs, batch; at most four agents at a time; review-brief.md gains the RESOURCE RULE (one dotnet process per agent, setsid timeout, DOTNET_GCHeapHardLimit for probes and mutants, no build-server shutdown).

# Review round 9 (wf_a01bab38-4ae on da2b06f): triage
37 agents, 0 errors, 4h02. 31 findings (r9-findings.md, r9-verified.json, r9-reviewers.json, raw r9-raw.json): 3 major, 19 minor, 8 nit, 1 refuted ([31]). No refuter asked for the maintainer; [16] and [30] carry a design choice the refuters leave open.
Fix (code):
- [1]+[10] (major/minor) xref.stream-passed-over at a stream the chain reached and reported unreadable (ReportLostSection at the /Prev no-hand-on break, the /XRefStm lost branch for an untyped stream): treat a section the chain took and could not read as reached for this report only (separate set of starts, or Offset+Padding of a record Read/Relocated/Malformed), not via _sectionsReadAt (loops, #263). Rows: oldest /Prev naming stream 11 with /W [1 -4 2], /Filter /Foo, untyped; untyped stream 11 named by the oldest /XRefStm; a second search 5 bytes short and exact. Remark :3683-3695 brought in line.
- [2]+[11] (major) the Warning under a guard: not made where a guard ended the chain or may hide a link (count limit, pastGuard/endedByGuard, _cutByGuard, _indexFullAt, a trailer cut hiding a link, rows cut by MaxXRefSectionLength/MaxDecodedStreamLength/MaxStreamLength, a value cut), and a stream the chain reached and a guard cut counts as reached. ADR 34 + #264. Texts 'read to its end' reworded in PdfDiagnosticCodes, ReportStreamsPassedOver, _streamsPassedOver doc, diagnostics.md:116, lazy-reading.md:207, threat-model.md:432. Rows: oldestNamesOwn at count 2 vs 3/default; stale-/XRefStm shape at MaxIndexEntries 4 vs 6; /W 12 0 R at MaxObjectLength 1024; seeded g001929/g003252 count2 lose the Warning.
- [4] (minor) S's judgement (MayBeTablesOwn/StreamPrevious in the takeAt branch) reads through a guard-cut value outside any attempt: one mark per iteration, ForgetReadThrough in the takeAt continue and the bound's continue. Rows big-S (no limit.object, opens under ThrowOnLimit) + retry-S control. Texts :4933-4934, _chainValuesCut doc :385-388, threat-model 439/440.
- [7] (minor, regression in 5de20b7/5d10a36) a candidate's FaultBesideCut decode (UnreadValue branch) is not rolled back: take GetMarkWithDecodingReports before it for a candidate and record it on UndecodedReports where DataUndecoded with no rows. Rows: place-near-offset shape cut by MaxObjectLength at /W, a /W element, /Size, whole /Index, under /Foo and /FlateDecode; guarded diagnostics = whole read's. Round 8's premise 'RowsHeldAtMost reports nothing' was false (PdfFilterPipeline.cs:124 diagnostics ??= guard.DocumentDiagnostics).
- [8]+[12] (minor) the check decodes a typed stream beside a cut value: `!(checkOnly && section.TypedXRef)` before the stream in the UnreadValue branch's FaultBesideCut; for an untyped stream in a check, mark + RollBack around it ('nothing reported' true). Rows in The_look_at_what_a_prev_names_reads_a_stream_s_data_only_where...: typed /Size 5 0 R, /W 5 0 R, /Index 9 0 R cut at MaxObjectLength 1024 -> 1 read; untyped control -> 2; guarded order row. r8a open_points[7] then true as written.
Fix (tests):
- [5] H3 is not equivalent (Resolve's chain bound shifted by one): row 'the table's /Prev, through a chain of 32 objects' (builder writes objects 20..51), optional 33-object row in the 'names no offset' sibling. R8b ADR 53 record corrected: 87 mutants, 79 killed, 5 equivalent (A4, B7, C22, G4, H12), 3 not viable. 7ff4f39 cost line: up to MaxChainLength reads past the first, the last dropped.
- [15] the 'not made where the chain indexed nothing' rule untested: theory (older table 0 0 -> [XRefOffsetAdjusted, XRefRebuilt]; 0 1 control with the Warning), cited at threat-model 432.
- [17] UndecodedFault's null-stream return uncovered: GuardCutValueTests theory for a deferred /Length with a cut /Size or whole /Index (coverage only; R19/R20 not viable).
- [18] four tests pin exact code lists (forms given by the refuter); optionally :6087.
- [13]/[23] optional into=28 row in the bound test (NamedAgain, no section-missing at spent=32).
- Nits: [19] rename the place test (..._reports_nothing_its_reading_met) + threat-model 432 citation + comment 'once the chain is read'; [20] rename the FilterTests theory (Data_is_said_undecoded_only_where_a_filter_the_library_cannot_run_is_left_some_or_flate_data_is_corrupt_before_its_first_byte) + threat-model 442/616 citations; [21] comment on object 4.
Fix (texts):
- [3] (minor; [31] refuted the skip at opening) message's last sentence: '...the index the chain read lacks the objects only it indexes, which only a rebuild of the index for another cause finds'; tests at :5531/:5995 and the :5550 row pin it; diagnostics.md:116 quote.
- [9]+[14]+[24] threat-model 440 cell 1: an object that declares no cross-reference stream (another /Type, or neither /Type /XRef nor a /W, no dictionary) leaves nothing of its reading nor what it read through; one that may be such a stream and is dropped (only a /W and unreadable, a dictionary a guard cut before it said, a /Type /XRef header a comment or string holds on its line) leaves what it read through and what decoding its data reported (but for a guard the decoding reached), and what reading its dictionary reported stands once, the guard that cut it among it. Cell 2 cites A_stream_of_no_type_the_search_near_an_offset_drops_keeps_what_reading_its_dictionary_reported_once... Source comments :6158-6160 and ForgetReadThrough summary :2816-2818 aligned. Closes the R8 open point.
- [13]+[23] the bound's exception: past the 32 places, the place a hybrid table's /Prev names, where nearest, is still asked (KeepsItsPlace, once per hybrid table) and the section left to the /Prev there; the /XRefStm's own stream put back unread is counted where it is read and the bound may stop it. Comment :5079-5081, threat-model 434, reader-limits.md:262; ':132/:4940' nit.
- [6] #171: threat-model #171 entry, lazy-reading 230-232 (+ src comment :5109-5111): a quoted cross-reference stream at/after S near a hybrid /Prev, no /Prev, is passed over and reported as xref.stream-passed-over at a place holding no section; closing condition 'asked of every place the search tries, one it passes over included'. Comment on #171 with the isolating reproducer.
- [22]+[29] first known gap restated (threat-model 432, lazy-reading 209-211): the older stream before S that gives no /Prev ends the chain; one that gives a /Prev is taken, the chain goes on through it, its own table unread, what only it indexes null, only the section found moved reported, no rebuild (= main); where the catalog is indexed elsewhere, nothing is rebuilt either. Row pinning the /Prev variant (WasRepaired false, object null) and the catalog-indexed variant. New debt (area: reader, M02) for the silent loss, with the first-revision ~500-byte case and the classic-table analog; #156 closes with the batch.
- [25] #332 under Known gaps (threat-model, after #330; link def), cited beside #330 at 432, lazy-reading 197-198/209, diagnostics.md:115.
- [27] threat-model 616 qualified (data still to decode; none left: 0 rows, filter.unsupported, read in part) + R8 open point concepts/reader-limits.md:119-120.
- [16] texts at least: HostileInputTests.cs:4739-4743, src :5042-5044, threat-model 432/lazy-reading 199-201 (an /XRefStm landing inside its own stream's object number counts that stream's header as before S). Code: R9-Q1.
- [30] the Warning names places the search would drop (unreadable comment/string header, unreadable /W-only stream): R9-Q2.
Tracking / records:
- [26] #332 body item 1 corrected.
- [28] status.md 'Commit bodies not rewritten': d8e67de (unconditional older-stream gap; the 64-reads figure leaves out the /Prev check past the bound), 5de20b7 (false where a guard cut a value), da0e157 ('only an object that gives a /W and no such type has its data decoded' false beside a cut).
- R8a consequence (c) for status.md: until [8]'s fix the check's throw also applied to a typed stream beside a cut value.
- R9-Q1 (maintainer, 2026-11-08): 'Le traiter comme S' as recommended ([16]): a candidate before S whose stream data starts where S's does is S itself (dropped where it gives no /Prev, deferred where it gives the table's), restoring 012e13d's whole reading for an /XRefStm landing inside its own stream's object number. Tests, src comment, pages, ADR 53.
- R9-Q2 (maintainer, 2026-11-08): 'En-têtes typés' as recommended ([30]): xref.stream-passed-over names only a place that declares itself (/Type /XRef, a header that is a token on its own line); no extra read. A readable /W-only stream and a readable commented header lose the Warning. Rows: comment header with no /Size, same-line string header with no /Size, unreadable /W-only stream, readable /W-only stream, readable commented header (no Warning, another section taken); a typed header on its line, unreadable, keeps it.
- 2026-11-08: round 9 fixes launched: wf_756885b9-080 (task wp860qqzb), script lot19-r9-fixes.js: R9a-passed-over, R9b-own-stream, R9c-undecoded, R9d-docs (sequential, each pushed), then R9-track (#332 body, new debt for the older stream before S, #171 comment) and R9f-check one after the other.
- 2026-11-08: round 9 fixes stopped at the maintainer's request before any change (R9a had only read and probed; no diff, no commit; branch still at da2b06f). Work moves to the maintainer's Windows PC: RESUME.md and PROMPT-fr.md on the -wip branch.
