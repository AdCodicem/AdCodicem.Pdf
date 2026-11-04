# window: the nearby search's window (#197) and what it and the rebuild's scan recognize as an object header (#261)

Report: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/measure/window/ is the evidence folder. No report.md was written there: the Write tool refuses report files from subagents, and the standing instructions for subagents forbid them. The full report is this structured output. The folder holds:
- the probe sources: ZzWindow.cs.txt, ZzCorpusDump.cs.txt, ZzScanBenchmarks.cs.txt;
- every probe output: main-final{1,2,3}.{197,261,hide}, p197-run1.*, p197e-run1.*, p261a-run{1,2}.*, p261b-run{1,2}.*, combined-run1.*;
- side-by-side tables: shapes-197-table.txt, shapes-261-table.txt, shapes-hide-table.txt, bench-table.txt;
- the diffs: p197-window.diff, p197e-edge-on-p197.diff, p197-plus-edge.diff, p261a-backward.diff, p261b-forward.diff, combined-p197-edge-b.diff;
- the unit logs: p197-unit.log, p261a-unit.log, p261b-unit.log, combined-unit.log;
- the corpus dumps: corpus-main-{1,2}.txt, corpus-p197.txt, corpus-p197e.txt, corpus-p261a.txt, corpus-p261b.txt, corpus-combined.txt;
- the benchmark runs: bench-*/results, bench-*.log, bench-progress.txt;
- the scripts in scripts/: apply_a.py, apply_b.py, apply_197e.py, bench.sh, bench_table.py, shape_table.py, cmp_corpus.py, grep_comment_headers.py;
- PdfFileReader.main.cs, the main source at 21a0298, which the file:line citations use.
Worktree: /home/user/AdCodicem.Pdf/.claude/worktrees/wf_9ceb535e-b92-2. Its source is back at main 21a0298; only the three uncommitted Zz probe files remain in it.

## Reproduced

### #197 — matches today: True
Shape: Issue shape A, the object case. Catalog 1, pages 2, a comment pads page 3 to offset 817, and row 3 gives 20. The window is [0,1024).

Identical in three runs (main-final1/2/3.197).
  object 3: <</Type /Page ... 4 entries>>; WasRepaired: False
  Repair xref.offset-adjusted at 817: Object 3 was found 797 bytes from where the index said.
  finding: Warning xref.entry-shifted at object 3 0, at offset 20: The entry of object 3 gives offset 20, where there is no object header; the object starts 797 bytes after it, at offset 817.
This is the issue's text, except that the fixture's page has 4 entries where the issue's had 3.

### #197 — matches today: True
Shape: Issue shape B, the section case. A classic table padded to 918, whose trailer gives /XRefStm 184.

  object 3: <</Type /Page ... 4 entries>>; WasRepaired: False
  Repair xref.offset-adjusted at 918: The cross-reference section /XRefStm names at offset 184 was found 734 bytes from there.
  finding: Warning xref.section-shifted at offset 918: The cross-reference section /XRefStm names at offset 184 starts 734 bytes after it, at offset 918.
The relocation lands on the table itself, which is #188's defect. The window is what lets it reach 734 bytes.

### #197 — matches today: True
Shape: Issue shape C, the control. 1,000 bytes of comment come before object 1, row 3 gives 1022, and object 3 lies at 1819.

  object 3: <</Type /Page ... 4 entries>>; WasRepaired: True
  Repair xref.rebuilt: The cross-reference index was rebuilt by scanning the file.
  finding: Error xref.entry-broken at object 3 0, at offset 1022: The entry of object 3 gives offset 1022, where there is no object header, and the object is not within 512 bytes of it.
One shift gets two readings: a Warning near the start, an Error past it.

### #197 — matches today: True
Shape: Comment of batch 9 (2026-10-04). Row 3 is -500 or -513, and object 3 lies at 823.

Row -500:
  Warning xref.entry-out-of-range: The entry of object 3 places it at offset -500, outside the file.
  Repair xref.offset-adjusted at 823: Object 3 was found 1323 bytes from where the index said.
  finding: Warning xref.entry-shifted at object 3 0: The entry of object 3 gives offset -500, outside the file; the object starts 1323 bytes after it, at offset 823.
  WasRepaired: False
Row -513:
  Warning xref.entry-out-of-range ...-513...
  Repair xref.rebuilt
  finding: Error xref.entry-broken at object 3 0: The entry of object 3 gives offset -513, outside the file.
  WasRepaired: True
Both match the comment exactly: 1323 = 823 + 500.

### #197 — matches today: True
Shape: Comment of batch 14 (2026-10-17, finding M3-02). Objects 1 to 3, then 800 spaces, then object 4, which lands at 1003 in this fixture (972 in the comment's). Rows -511, -9 and -400.

Row -511:
  object 4: "four"; WasRepaired: False
  Warning xref.entry-out-of-range: The entry of object 4 places it at offset -511, outside the file.
  Repair xref.offset-adjusted at 1003: Object 4 was found 1514 bytes from where the index said.
  finding: Warning xref.entry-shifted at object 4 0: The entry of object 4 gives offset -511, outside the file; the object starts 1514 bytes after it, at offset 1003.
Rows -9 and -400: the same, with 1012 and 1403.
The comment's arithmetic holds exactly (distance = position + |row|). Only the position differs, because the fixture's objects 1 to 3 are longer.

### #197 — matches today: True
Shape: The closing criteria's shapes, all found by main, with xref.offset-adjusted and a Warning:
- K1: row 100, object at 700 (600 after).
- K2: row 100, object at 600 (500 after).
- K3: row 400, object at 1000 (600 after).
- K4: row 400, object at 900 (500 after).
- K5: row -1, object at 600.
- K6: row -1, object at 500.
- K7: row -9, object at 500.
- K8: row -9, object at 490.
- S1: /Prev 100, table at 700 (600 after).
- S2: /Prev 200, table at 700 (500 after).
- S4: /XRefStm 300, table at 900 (600 after).
- S5: /XRefStm 400, table at 900 (500 after).
- S3, the control: /Prev 1400, table at 2000 (600 after, far from the start).

Objects K1 to K8, Warning xref.entry-shifted at distances 600, 500, 600, 500, 601, 501, 509 and 499.
S1, for example:
  Repair xref.offset-adjusted at 700: The cross-reference section /Prev names at offset 100 was found 600 bytes from there.
  finding: Warning xref.section-shifted at offset 700: ... starts 600 bytes after it, at offset 700.
S2, S4 and S5: Warning xref.section-shifted at 500, 600 and 500. S4 and S5 land on the table itself (#188).
S3, the control:
  Warning xref.section-missing at 1400: The cross-reference section /Prev names at offset 1400 is not there, nor near it; ...
  Repair xref.rebuilt
  Information object.redefined
  finding: Error xref.section-not-found at offset 1400: The cross-reference section /Prev names at offset 1400 is not there, nor within 512 bytes of it: the offset holds neither the xref keyword nor an object.

### #197 (neighbor, #171's open bullet) — matches today: True
Shape: E1. 14 0 obj (fourteen) lies at 805, and the real 4 0 obj 1,200 bytes further. Row 4 is 1318, so the window starts on the '4' of '14'.

  object 4: "fourteen"; WasRepaired: False
  Repair xref.offset-adjusted at 806: Object 4 was found -512 bytes from where the index said.
  finding: Warning xref.entry-shifted at object 4 0, at offset 1318: The entry of object 4 gives offset 1318, where there is no object header; the object starts 512 bytes before it, at offset 806.
Object 14's body is served as object 4. The forward confirmation (PlaceHeader's HeaderAt, the load's TryParseObjectAt) starts at the '4' the window cut, so it cannot catch this. It is #171's first bullet, which #171's comment of 2026-10-01 leaves open for the nearby window.

### #261 — matches today: True
Shape: The issue's shape. Catalog 1, pages 2, page 3, then object 4 written '4 % a comment\n0 obj\n(four)\nendobj\n' at 203. Three cases: the row at the header, the row 2 bytes past it, and an index otherwise damaged (startxref names 9).

Identical in three runs. Row at the header:
  object 4: "four"; WasRepaired: False
  (no finding)
Row +2:
  object 4: null; WasRepaired: True
  Repair xref.rebuilt: The cross-reference index was rebuilt by scanning the file.
  finding: Error xref.entry-broken at object 4 0, at offset 205: The entry of object 4 gives offset 205, where there is no object header, and the object is not within 512 bytes of it.
Index damaged:
  object 4: null; WasRepaired: True
  Repair xref.rebuilt
  finding: Error file.startxref-wrong at offset 380: startxref gives offset 9, which holds object 1, which is not a cross-reference stream.
This is exactly the issue's table: offset 205, null, xref.rebuilt.

### #261 — matches today: True
Shape: Variants, each with the row at the header, the row +2, and a rebuild:
- a comment between generation and obj: '4 0 % a comment\nobj';
- both: '4 % a\n0 % b\nobj';
- no space: '4% a comment\n0 obj';
- comment lines between tokens: '4\n% one\n% two\n0\n% three\nobj';
- CR: '4 % a comment\r0 obj';
- CRLF;
- CR before obj: '4 0 % a comment\robj';
- a comment holding %: '4 % 50% done\n0 obj';
- a string with % before the header on its line: '(50%) 4 % x\n0 obj';
- comments of 80 and of 600 bytes;
- endobj on the same line: 'endobj 4 % c\n0 obj';
- a string over two lines holding %: '(multi\nline 50%) 4 % c\n0 obj'.

Every variant gives:
- row at the header: "four", no finding;
- row +2: null, xref.rebuilt, Error xref.entry-broken;
- rebuild: null.
Controls '4 0 obj' and '4\n0 obj' (an end of line, no comment): row +2 is found with a Warning xref.entry-shifted (-2), and the rebuild reads "four".

### #261 — matches today: True
Shape: Main's false positive: a comment that ends in digits, then the generation: '% note 4\n0 obj', the row at the '%'. As the lexer reads it, this is a comment, then '0 obj': there is no header of object 4.

  object 4: "four"; WasRepaired: False
  Repair xref.offset-adjusted at 210: Object 4 was found 7 bytes from where the index said.
  finding: Warning xref.entry-shifted at object 4 0, at offset 203: The entry of object 4 gives offset 203, where there is no object header; the object starts 7 bytes after it, at offset 210.
The rebuild also reads "four". Main takes the '4' inside the comment for a header's number.

### #261 — matches today: True
Shape: A section candidate. An original cross-reference stream, object 5, at 225, whose header is written '5 % c\n0 obj' or '5 0 % c\nobj'. An update redefines object 3 in a classic table whose /Prev gives 227 (2 bytes past). The control writes '5 0 obj'.

Control: Repair xref.offset-adjusted at 225 (-2), and Warning xref.section-shifted.
Comment header:
  object 4: "four"; WasRepaired: True
  Repair xref.offset-adjusted at 472: The cross-reference section /Prev names at offset 227 was found 245 bytes from there.
  Warning xref.chain-cycle at 227: The cross-reference chain loops back on itself: the /Prev of the section at offset 472 names offset 227, which the chain has already read.
  Repair xref.rebuilt
  Information object.redefined
  finding: Warning file.size-wrong at offset 501: The trailer of the cross-reference table at offset 472 gives /Size 6, where the highest object number it and the sections it updates use, 3, makes it 4.
  (this finding appears twice, word for word)
  finding: Warning xref.section-shifted at offset 472: ... starts 245 bytes after it, at offset 472.
  finding: Error xref.chain-loop at offset 472: ...
The stream is not a candidate, so the relocation lands on the update's own table (#188), and the same finding is reported twice.

### #261 (the scan's chunk edge, #171) — matches today: True
Shape: The rebuild's scan, with object 4's obj keyword at 1,048,580 (the second 1 MB chunk starts at 1,048,512). Headers:
- '4 0 obj';
- '4 % c\n0 obj';
- '4 0 % c\nobj';
- '4 %' + 100 c + '\n0 obj', starting at 1,048,474;
- 70 zeros + '4 0 obj', starting at 1,048,506;
- '1' + 70 zeros + '4 0 obj', a number past MaxNumber, starting at 1,048,505.

- Plain header: "four".
- Both short comment headers: null.
- 106-byte comment header: null.
- 70 zeros: "four".
- '1' + 70 zeros + '4': "four". The chunk's start cuts the '1', so the scan reads a number past MaxNumber as 4. This is #171's remaining bullet.

### #261 (test fixtures) — matches today: True
Shape: Ways a test hides a header from the scan while a load reads it at its row: '4 0 % lost\nobj', '4 +0 obj', '+4 0 obj', '(x) 4 % lost\n0 obj'.

Row at the header: every one reads "four".
Rebuild:
- '4 0 % lost' (the device tests use today): null;
- '4 +0 obj': null;
- '+4 0 obj': "four";
- '(x) 4 % lost': null.

## Prototypes

### P197: the nearby window [max(0, o-512), min(Length, o+512))
Diff: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/measure/window/p197-window.diff

Two hunks, both in PdfFileReader.cs.

TryRelocateXRefSection (:3252-3253): length = (int)(absolute + Math.Min(NearbySearchRadius, _source.Length - absolute) - start). The method returns first when absolute lies outside the file (:3247), so absolute is in [0, Length) here and neither sum wraps.

TryFindObjectHeader (:6074-6075): end = approximateOffset + Math.Min(NearbySearchRadius, source.Length - approximateOffset), then length = end - start as a long, and length <= 0 returns false.
- The guard approximateOffset < -NearbySearchRadius (:6067) stays. Without it, o - 512 and Length - o wrap near long.MinValue, and the result would be right only by wrapping twice.
- long.MaxValue, which PositionOf gives an entry past a long (:3137-3138) and LoadRegularObject passes on (:5635): Length - long.MaxValue cannot wrap, end = Length, start = long.MaxValue - 512, the length is negative, and the search returns false, as on main.
- An offset in [Length, Length+512) gets the window [o-512, Length), as on main.
- An offset o in [-512, 0) gets [0, o+512); -512 gets an empty window.
- An equivalent form: end = o >= Length - 512 ? Length : o + 512.

What 'within 512' then means, everywhere: a header (or section) is found when it lies whole in the window, from its number's first digit to the end of obj. That is the meaning past the first 512 bytes today.

Shapes after: Per-shape detail in shapes-197-table.txt.

No longer found nearby; each is now rebuilt and reported Error xref.entry-broken:
- A: '... gives offset 20, where there is no object header, and the object is not within 512 bytes of it.'
- N1 and N3: '... gives offset -500 / -511 / -9 / -400, outside the file.'
- K1, K3, K5;
- K7: row -9, header at 500. The window [0,503) ends before obj at 504-506, so '500 after the start' means 509 bytes after a row of -9.

Sections B, S1 and S4: Warning xref.section-missing, then 'Error xref.section-not-found at offset 184|100|300: ... nor within 512 bytes of it: the offset holds neither the xref keyword nor an object.' S1 rebuilds as S3 does.

Still found with a Warning: K2, K4, K6 (row -1, window [0,511)), K8, S2 and S5. S5 still lands on the table itself (#188).

Controls C, N2 and S3: unchanged.

E1: unchanged; P197 does not touch the window's start.

Unit suite: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/measure/window/p197-unit.log, run without the remote corpus as CI's unit job runs: total 6,659, succeeded 6,639, failed 0, skipped 20, 5m27s. The total includes 2 Zz probe tests, so the suite proper is 6,657. No test depended on the reach near the start.

Corpus: 403 documents: 168 committed and 235 remote (symlinked). Besides findings and diagnostics, each document gets an index line: entry count and SHA-256 of the sorted index after validation.
- main run 1 against main run 2: 0 differences.
- main against P197: 'docs 403 403 same keys True / F diff 0 F order 0 D diff 0 D order 0 / totals F 2982 D 1343' on both sides, and every index line identical.
Why nothing changes: only 21 documents report xref.offset-adjusted.
- 19 are a header's shift ('The PDF header starts n bytes into the file').
- t03-007-offset-wrong.pdf: object 3 found 60 bytes from its row, at 182, within 512 either way.
- ibm-id-workbench-xpp-qmf-manual.pdf: 998 relocations, all of -12 bytes, at 1.5 MB.
No document names an object or section below 512 whose target lies more than 512 bytes after it.

### P197e: the window's start no longer cuts a number (pays the nearby-window part of #171's first bullet)
Diff: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/measure/window/p197e-edge-on-p197.diff

Applies on top of P197; P197 and P197e together are p197-plus-edge.diff.

Each nearby window that does not start the file is read with one byte more before it: lead = start > 0 ? 1 : 0, then GetWindow(start - lead, length + lead).
- TryFindObjectHeader takes a header only where headerStart >= lead, at start - lead + headerStart.
- TryRelocateXRefSection drops every candidate below start. The prototype does it with candidates.RemoveAll and a lambda; that path is no hot loop (32 attempts a document), but the implementation can filter inside FindSectionCandidates instead.

A header whose number the window's first byte cuts ('14 0 obj' read from its '4') is no longer taken. A header that starts exactly at o-512 is still found, since its preceding byte is read.

Shapes after: E1:
  object 4: "four, far away"; WasRepaired: True
  Repair xref.rebuilt
  finding: Error xref.entry-broken at object 4 0, at offset 1318: The entry of object 4 gives offset 1318, where there is no object header, and the object is not within 512 bytes of it.
Every other #197 shape reads as under P197 (p197e-run1.197).

Unit suite: Measured together with P197 and B, as the combined prototype (combined-unit.log, remote corpus present): total 9,284, failed 6, skipped 3. The 6 failures are exactly B's, listed under the next prototypes. P197e adds none.

Corpus: corpus-p197e.txt against corpus-main-1.txt: 403 documents, 0 differences in findings, diagnostics or index hash.

### P261-A: the backward reading steps over a comment it recognizes (first % of the line, refused after a parenthesis)
Diff: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/measure/window/p261a-backward.diff

TryReadHeaderBackwards (:6294) replaces its two white-space loops with SkipBackOverBlanksAndComments(span, index), a new private static method placed before LoadCompressedObject.

The method steps back over white space and notes whether the run crossed a CR or LF. Only then can a comment end before the run, on the line that end of line closes.
- It finds that line's start with LastIndexOfAny(CR, LF), vectorized, back to the span's start.
- If the line holds a '%', the comment is taken to start at the line's first '%', and the method goes on from the byte before it, looping over several comments.
- If a '(' or ')' precedes that '%' on the line, the '%' may lie in a string read backwards. No comment is recognized then, and the line is taken as it is.
- A line with no '%', or a run with no end of line, reads exactly as on main.

Names are no ambiguity: '%' is a delimiter, so '/A%x' is the name /A, then a comment, for the lexer as for this rule.

Residual ambiguities:
- A line whose start lies inside a string opened on an earlier line and closed after the '%' on the same line. '(multi\nline 50%) 4 % c\n0 obj' is read as object 50, at 'line 5|0'.
- A comment whose '%' lies before the window or chunk start, and whose tail ends with digits. Main shares this.
- A '%' inside a hex string. It is invalid PDF, but the rule takes it for a comment where the lexer does not.

Cost: a vectorized line search, and only when an end of line separates header tokens. Nothing is allocated.

Shapes after: Per-shape detail in shapes-261-table.txt and p261a-vs-main.261.diff.

Handles, nearby (row +2 found, Repair -2 and Warning xref.entry-shifted rather than Error entry-broken) and by the rebuild ("four"):
- comment between number and generation, LF, CR and CRLF;
- comment between generation and obj, LF and CR;
- both;
- a comment right after the digits ('4% a');
- comment-only lines between tokens;
- a comment holding '%';
- comments of 80 and 600 bytes;
- 'endobj 4 % c' on one line.

Refuses '(50%) 4 % x\n0 obj': null and entry-broken, as on main.

Misreads the multi-line string shape: object 4 null, and a false object 50 recorded by the rebuild.

Removes main's false positive '% note 4\n0 obj': null, rebuilt, Error entry-broken, where main served "four" moved 7.

Section candidates: the cross-reference stream '5 % c\n0 obj' or '5 0 % c\nobj' at /Prev +2 is now found at -2 (Warning xref.section-shifted only). The relocation onto the update's own table, the chain loop, the rebuild and the duplicated file.size-wrong are gone.

Scan chunk edge: the short comment headers are found. The 106-byte comment header straddling 1,048,512 is still missed, which is #171's overlap limit.

The #197 shapes are unchanged (p261a-run1.197 equals main).

Unit suite: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/measure/window/p261a-unit.log, remote corpus present: total 9,283, succeeded 9,274, failed 6, skipped 3, 6m45s. The total holds 3 probe tests, so the suite proper is 9,280.

Every failure is a fixture that hides a header from the scan with a comment, which the scan now reads:
1. ReadOrderTests.A_rebuild_forgets_that_a_stream_it_no_longer_indexes_gave_a_wrong_length(settling: True) and 2. (settling: False), :315: 'Expected document.Reader.TryGetStreamLengthFault(5, out _) to be False because the note was of a definition the rebuilt index no longer holds, but found True.'
3. ReadOrderTests.A_rebuild_forgets_that_the_read_which_set_it_off_found_no_endobj, :217: 'Expected document.GetObject(new PdfObjectId(5)) to be null because the rebuilt index holds no 5, but found <<stream of 10 bytes>>.'
4. ReadOrderTests.A_rebuild_forgets_that_the_read_which_set_it_off_found_a_wrong_length, :243: same message.
5. ReadOrderTests.A_rebuild_forgets_that_an_object_it_no_longer_indexes_lacked_its_endobj, :94: 'found {[/Type, /Page], [/Parent, 2 0 R]}'.
6. DocumentReaderTests.A_reference_the_chain_cannot_read_rebuilds_nothing_while_the_chain_is_read, :1944: 'Expected reader.ObjectCount to be 0, but found 2 (difference of 2).' Its two cross-reference streams are written '7 0 % ...\nobj' and '8 0 % another\nobj'.

Other fixtures use the same device and still pass, but their stated reason ('which the scan does not find (#261)') becomes false and must be revisited:
- ReadOrderTests.cs:1061-1071;
- ReaderLimitsTests.cs:1090-1099;
- ObjectRuleTests.cs:3420-3451 (RebuiltAfterTwoObjectStreams, '7 0 %x\nobj');
- ConvergenceShapes.Records.cs:623-635;
- ConvergenceShapes.ObjectStreams.cs:33-39 and :295.

A replacement device was verified on main, A and B alike: '4 +0 obj' is read where its row places it and found by no scan.

Corpus: corpus-p261a.txt against corpus-main-1.txt: 403 documents, 0 differences in findings, diagnostics or index hash (totals F 2982 D 1343).

grep_comment_headers.py over the raw bytes of all 403 files finds:
- no header with a comment between its tokens;
- one file whose headers put an end of line between their tokens, remote/opf-format-corpus/jhove-hul-78-quartz-google-slides-poster.pdf (107 headers 'N\n0\nobj'). With no '%' on those lines, both prototypes read them as main does.

### P261-B: candidates confirmed forward from a token boundary (each line holding a % lexed from its start)
Diff: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/measure/window/p261b-forward.diff

TryReadHeaderBackwards is rewritten as a loop that takes two numbers, the generation first.
- Each number is a run of digits read backwards exactly as on main (same value checks, same white space required before the generation), unless the white space just crossed holds a CR or LF and the line it closes holds a '%'.
- Then TakeLineTokens lexes that whole line forward from its start with PdfLexer, the load's lexer: from a line start, a comment begins where the lexer meets a '%' outside a string.
- The line's last tokens, nearest first, must be Integers; any other kind refuses the header.
- A line with fewer tokens than still wanted (a comment-only line) is passed, and the previous line is read the same way.
- A literal or hex string that runs to the line's end refuses the header: from that boundary, what follows lies inside the string.
- Number and generation limits are as on main (1..MaxNumber, 0..MaxGeneration); a negative or past-long value refuses.

Lines without '%' are never lexed, so every header main reads without a comment reads the same, including 'x4 0 obj' and '4 0 objx' (#171 and #189 unchanged).

The obj line itself is never lexed, only complete lines before an end of line the walk crosses. Each line is lexed at most once per obj walk, and a walk enters a line only through the white space right after it, so the cost is linear in the bytes. The window (≤1,024 bytes) or the scan's chunk bounds each walk.

Residual ambiguities:
- A line whose start lies inside a multi-line string. '(multi\nline 50%) 4 % c\n0 obj' is misread as 50, as by A.
- A comment's tail at a window or chunk start; main shares it.

Cost: a PdfLexer pass over each line holding '%' that a header walk crosses. PdfLexer is a ref struct, so nothing is allocated.

Shapes after: Every shape reads as under A, and one more is found: '(50%) 4 % x\n0 obj'. Row +2 gives "four", Repair -2 and Warning xref.entry-shifted, and the rebuild reads "four".

The hide device '(x) 4 % lost\n0 obj' is found by B's rebuild; A refuses it.

One more shape: '4 (abc % x\n0 obj' (an unterminated string through the line) is refused by design.

Comments between number and generation, between generation and obj, LF, CR, CRLF and comment-only lines: all handled, as by A.

The #197 shapes are unchanged.

Unit suite: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/measure/window/p261b-unit.log, remote corpus present: total 9,284, succeeded 9,275, failed 6, skipped 3, 7m20s. The 6 failures are A's six, with the same messages and the same cause (fixtures hiding headers with '% ...\nobj').

Corpus: corpus-p261b.txt against corpus-main-1.txt: 403 documents, 0 differences in findings, diagnostics or index hash.

### Scan cost benchmarks, main vs A vs B (ShortRun, MemoryDiagnoser)
Diff: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/measure/window/ZzScanBenchmarks.cs.txt

Two benchmark classes, each a variant built and run alternately: main ×3, A ×3, B ×4 (scripts/bench.sh, bench-table.txt; another agent was building on the machine, so expect ±10-20% noise).
- The existing TrailerScanBenchmarks, whose three Tails rebuild the file.
- The uncommitted ZzScanBenchmarks: PdfDocument.Open of files with no valid startxref, so the whole-file object scan runs. Shapes:
  - sound: a 1,000-page, 4.4 MB SyntheticDocument;
  - plain-headers: 100,000 headers 'N 0 obj';
  - comment-headers: 100,000 'N % a comment\n0 obj';
  - comment-lines: 4 MB of '%' + 60 c + '\n0 obj\n';
  - obj-lines: 4 MB of 1,000 x + '%\nobj\n';
  - percent-lines: 4 MB of '(50%) 4 % x\n0 obj\n'.
No committed benchmark measures the object scan alone; TrailerScanBenchmarks is dominated by the trailer parses.

Shapes after: Means, main / A / B:
- sound: 4.83, 4.60, 5.50 ms / 4.79, 4.70, 4.59 ms / 4.04, 4.75, 5.10, 4.54 ms. Allocated 1,840.23 KB in all: no difference.
- TrailerScan '<< /Size 3 >>': 879, 855, 859 / 965, 841, 858 / 896, 890, 870, 1000 ms.
- TrailerScan '5 0 R': 400, 335, 332 / 407, 314, 352 / 313, 356, 368, 364 ms.
- TrailerScan 'null': 333, 345, 330 / 442, 344, 372 / 337, 368, 385, 313 ms.
- TrailerScan allocation: 3,449.62, 642.0-648.0 and 262.12 KB. The 642-648 variation of '5 0 R' occurs within main too.
- plain-headers: main 60.7 ms, A 68.0 ms, B 57.4 ms, 18.17 MB each. This is the cost of indexing 100,000 objects.
- comment-headers: main 2.8-4.0 ms and 5.93 KB (it finds no object); A 63.7-71.1 ms, B 63.5-72.5 ms, 18.17 MB, the same as plain-headers on main. Reading the comments costs nothing measurable beyond indexing what is found.
- comment-lines: main 1.29-1.66 ms; A 3.0-4.3 ms (about 2.5×); B 12.0-12.9 ms (about 9×).
- obj-lines: main 0.47-0.50 ms; A 0.98-1.11 ms (2×); B 10.6-13.4 ms (about 24×). B lexes each 1,000-byte line once.
- percent-lines: main 3.9-5.4 ms; A 7.0-8.0 ms; B 19.2-22.5 ms. B finds about 230,000 headers of object 4: one entry, 7.36 KB.
- Allocation on the adversarial shapes: 5.93 KB in all three variants, except B's percent-lines (7.36 KB, for the entry it finds). No variant allocates per obj.

Both prototypes are linear. A's worst case is about 2-3× main's scan on files built to cross a line holding '%' at every obj. B's is about the cost of lexing those bytes once, ≈3 ns a byte, or 12 ms per 4 MB, a few times what rebuilding a sound 4.4 MB file costs (≈4.7 ms). That stays far below the trailer scan's existing cost (300-900 ms for 108 KB).

Unit suite: Not applicable: these are benchmark runs. Logs are in bench-*.log; the BenchmarkDotNet CSVs are in bench-*/results.

Corpus: Not applicable.

### Combined: P197 + P197e + B
Diff: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/measure/window/combined-p197-edge-b.diff

The three changes together, to check how they interact.

Shapes after: #197 shapes: as P197e. #261 shapes: as B, except one. The 600-byte comment header with the row 2 bytes past it puts obj at 809, past 205+512 = 717. It is no longer found nearby: rebuilt, Error xref.entry-broken "... not within 512 bytes of it." The rebuild still reads "four". This is the window's whole-header rule meeting a long comment.

Unit suite: combined-unit.log, remote corpus present: total 9,284, failed 6, skipped 3. The same six fixture failures as A and B; nothing from P197 or P197e.

Corpus: corpus-combined.txt against corpus-main-1.txt: 403 documents, 0 differences.

## Questions

### W1 (#197)
Near the file's start, the nearby search (objects and sections) reaches up to 1,024 bytes past the offset, or 1,536 for a row of -512. Should the window end at offset + 512 wherever the offset falls, or should the texts say it reaches further there?

- [R] Fix the window: [max(0, o-512), min(Length, o+512)), the end computed as o + min(512, Length - o), the guard o < -512 kept (P197): Behavior:
- A row or /Prev or /XRefStm in the first 512 bytes (or a negative row) whose target lies more than 512 bytes after it is no longer found nearby. Objects are found by the rebuild, the read as before.
- Severities (ADR 45, codes unchanged): Warning xref.entry-shifted becomes Error xref.entry-broken, and Warning xref.section-shifted becomes Warning xref.section-missing plus Error xref.section-not-found, as they already read past the first 512 bytes.
Cost: none (a smaller window). Corpus: 0 of 403 documents change. Suite: 0 failures.
Tests: the issue's DocumentReaderTests and CrossReferenceChainTests (600 not found, 500 found), the negative rows (K5, K6, K7, K8 show the obj keyword must end within 512 bytes of the row), and the CrossReferenceRuleTests case.
Docs: the threat model's #197 caveats (:456-458, :478, :596) go and its rows name the tests. NearbySearchRadius says what 'within' means. Every other text already says 512 and becomes true.
- Keep the reach, and document it: Every text listed in docs_to_update that states 512 (two runtime messages among them) must say 'up to 1,024 bytes after an offset in the first 512 bytes, 1,536 for a negative row'. One shift keeps two readings, Warning near the start and Error past it. The E1 left-edge neighbor stays as is.

Why: The issue frames it as a choice for the maintainer, but the measurement leaves the code fix no downside. Corpus (403 including the remote) and suite are unchanged, the change is two lines, and wrap-safety for long.MaxValue and for offsets far before the start is preserved. Every text already states 512, so the fix makes them true rather than rewriting them.

### W2 (#261)
How should the search near a row and the rebuild's scan read a header with a comment between its tokens?

- [R] B: keep the backward reading over digits and white space; where the white space crossed holds an end of line and the line it closes holds a '%', lex that line forward from its start with PdfLexer and take its last tokens (P261-B): Behavior:
- Reads every comment shape as a load does from a line start, including a string holding '%' before the header on its line ('(50%) 4 % x').
- No longer takes digits at the end of a comment for a header's number: main's '% note 4\n0 obj' reads null, rebuilt, Error entry-broken, where main served "four" with a Warning.
- Section candidates (cross-reference streams with comment headers) follow, since FindSectionCandidates shares the method.
- Lines without '%' read exactly as today.
Cost: no measurable change on sound files or 100,000 headers; adversarial files up to about 24× main's scan time on files built for it (12 ms per 4 MB, linear); no allocation.
Tests: the six failing fixtures and the others listed switch their hiding device to 'N +0 obj'. New DocumentReaderTests and CrossReferenceRuleTests per the issue, a scan test per position (LF, CR, CRLF, between number and generation, between generation and obj, comment-only lines), a section-candidate test, and the '% note 4' false positive.
Docs: threat model :427 and the #261 Known gap. HeaderText.cs:131-133 and concepts/diagnostics.md:52-53 (see W3).
- A: the backward reading recognizes a comment as starting at the line's first '%', unless a parenthesis precedes it on the line (P261-A): Behavior: the same as B, except '(50%) 4 % x\n0 obj' is not read, and '(x) 4 % c' hides a header from the scan.
Cost: cheaper on adversarial lines (2-3× main's scan, vectorized searches).
Docs: the rule ('first %, refused after a parenthesis') must be stated where it is declared, because it is a heuristic of its own rather than the lexer's reading.
Tests: same suite impact.
- Neither: leave headers with comments unread outside their exact offset, and document it: The issue stays open as a documented limit. Rows a few bytes off still lose such objects, and so does every rebuild.

Why: The issue's closing asks for headers read 'as the lexer reads it'. B uses the load's own lexer from a line start, so it agrees with a load wherever the line does not start inside a multi-line string, a residual A shares. A adds a heuristic of its own: the paren refusal, plus a different reading of '%' in hex strings. B's extra cost appears only on files built to cross many lines holding '%'; it is linear, allocation-free, and small next to the rebuild's other costs. On real files the two cost the same, and the corpus holds no comment header at all.

### W3 (#261 (its consequence for the stream-end search))
The search for a damaged stream's endstream stops at the first object header the bytes hold. Its rule (HeaderText.FindHeader, PdfObjectParser.IsHeader and MayBeHeaderBytes) refuses a header with a comment between its tokens 'as the rebuild's scan does' (maintainer, 2026-09-30, docs/status.md:2900; HeaderText.cs:131-133; concepts/diagnostics.md:52-53; reference/diagnostics.md:116). Once the scan reads such headers, should the stream-end rule follow?

- [R] Keep the stream-end rule as it is in batch 19, reword its reason in the four texts, and file a debt to extend it: Behavior: none changes for streams. StreamLengthTests' InlineData('12 0 %\nobj', null) and ('12 %\n0 obj', null) stay. The scan and the stream-end rule then differ on comments, as they already do on 'x12 0 obj' and '12 0 objx' (#171, #189), and the texts say so. The debt carries a comment state across HeaderText's reads, whose carried bytes model runs of white space and digits only today.
- Extend the stream-end rule in batch 19: Behavior: HeaderText must carry a comment across the reads it splits the data into. The parser's IsHeader and MayBeHeaderBytes accept comments between tokens, and the two StreamLengthTests rows flip to headers. A damaged stream stops at a comment header. More code and review in a batch that is already wide.
- Keep the stream-end rule as it is by design, with no debt: The texts state the difference as intended: the stream-end rule reads bytes alone, and the scan reads comments as the lexer does.

Why: The maintainer's earlier reason was consistency with the scan, and that reason goes away, so the choice is the maintainer's. Extending HeaderText is not a local change, since it carries runs across read boundaries. Keeping the rule with a debt holds batch 19 to its scope without hiding the difference.

### W4 (#197 / #171)
The nearby window's start can cut an object number: '14 0 obj' read from its '4' serves object 14's body as object 4, with a Warning (E1). That is #171's open bullet for the nearby window. Should batch 19 pay it with #197, which rewrites the same lines?

- [R] Yes: read one byte before each window that does not start the file, and take a header or section candidate only where it starts in the window (P197e); comment on #171 that its nearby-window part is paid: Behavior: E1 is rebuilt, the real object 4 is served, and the row is Error xref.entry-broken. A header starting exactly at o-512 is still found. 'Within 512 bytes' becomes exact on both edges.
Cost: none. Corpus: 0 changes. Suite: 0 failures.
Tests: a WindowEdgeTests or DocumentReaderTests case for the cut number, and a section candidate whose 'startxref' the window cuts to 'xref'.
Left in #171: the scan's chunk start (headers longer than about 61 bytes across a 1 MB boundary, which #261 makes more likely with comments), headers in stream data and strings, and the nearby search taking the first header rather than the nearest.
- No: leave it to #171's own batch: #197's window is exact on the right and still cuts numbers on the left; the threat model's row keeps the caveat.

Why: It is three lines in the very code #197 changes, measured at zero cost and zero corpus or suite change. It finishes the statement #197 is about, that a header is found when it lies within 512 bytes, at the window's other edge.

## decided_without_question

- What 'within 512 bytes' means once #197 is fixed: a header or section is found when it lies whole in [o-512, o+512) clamped to the file, from its number's first digit (or 'xref') to the end of obj. That is the meaning past the first 512 bytes today. Evidence: K7 (row -9, '3 0 obj' at 500, obj ends 515 bytes after the row) is no longer found, while K8 (header at 490) and K6 (row -1, header at 500) are. The issue's tests must place '500 after' so that obj ends within 512 bytes of the row. Reading past the window for a header's tail would reopen the reach #197 closes.
- With #261, a header whose comment pushes obj past o+512 is not found nearby even 2 bytes from its row. The rebuild then finds it, and the reader serves it with Error xref.entry-broken (combined prototype, 600-byte comment). The comment widens nothing: the window stays what is read, and the corpus holds no comment header at all.
- The window's end is computed as o + Math.Min(512, Length - o), which wraps for no admissible o. The guard o < -NearbySearchRadius stays, so nothing near long.MinValue relies on wrapping. long.MaxValue (an entry past a long) still returns false, and an offset in [Length, Length+512) still searches [o-512, Length). The existing HeaderOffsetTests that hold these cases pass.
- No corpus document depends on the reach near the start, and none holds a comment header. All prototypes give 0 differences over 403 documents (168 committed and 235 remote), in findings, diagnostics and a SHA-256 of each rebuilt or chain index. A raw-byte scan of all 403 files finds no comment between header tokens, and one file (jhove-hul-78) with an end of line between the tokens of its 107 headers, which both #261 prototypes read as main does.
- Every text stating 512 becomes true with P197; none needs rewording except the threat model's three #197 caveats. Among the true ones are the runtime messages built from PdfFileReader.NearbySearchRadius (CrossReferenceProbe.cs:231, SectionNotFoundRule.cs:49).
- Both #261 prototypes handle the positions the lens asks about:
- a comment between number and generation;
- a comment between generation and obj;
- both;
- a comment right after the digits ('4% a');
- comment-only lines between tokens;
- several comments;
- comments holding '%';
- comments of 80 and 600 bytes;
- lines ending in LF, CR and CRLF, both between number and generation and before obj.
A comment always ends at an end of line, so the byte before obj or before the generation is always white space: the existing first check (white space before obj) stays valid.
- Main's false positive is fixed by both prototypes. '% note 4\n0 obj' (digits ending a comment) is no longer read as object 4's header. ISO 32000-1 7.2.3 makes it a comment, and the issue's closing asks for headers read 'as the lexer reads it'.
- FindSectionCandidates uses TryReadHeaderBackwards, so cross-reference streams whose headers hold comments become relocation candidates with no separate change. Measured: the shape that on main relocated onto the update's own table 245 bytes away, looping, rebuilding, and reporting file.size-wrong twice, now relocates to the stream at -2 with only a Warning xref.section-shifted.
- The test fixtures that hide a header from the scan with '% ...\nobj' need another device. '4 +0 obj' was verified on main, A and B: a load reads it at its row, and no scan or nearby search finds it, because the backward reading requires white space before the generation's digits. It relies on the scan refusing a signed number, which HeaderText's stream-end rule also refuses ('digits alone ... no sign').
- Lines without '%' keep main's backward reading exactly in B, so 'x4 0 obj' (a digit run with a regular byte before it) and '4 0 objx' read as today. Those are #171's and #189's, not #261's.
- Cost: on sound files and on 100,000 headers neither prototype moves time or allocation beyond noise. Adversarial shapes are linear and allocation-free in both. The comment reading needs no guard and no new constant: the window (1,024 bytes) or the scan's 1 MB chunk bounds each walk, and a line is lexed or searched at most once per walk.
- The long comment header straddling a 1 MB chunk boundary (obj in the new chunk, number more than about 61 bytes before it) stays missed by both prototypes. That is the scan's 64-byte overlap, and #171's open bullet ('a header whose number starts before a window while its obj lies past the overlap's first 61 bytes'), not #261's: #261 asks for headers read 'within the bounds they have now'.

## interactions

- Relocation lens (#188, #281). P197 and P197e change TryRelocateXRefSection's window (PdfFileReader.cs:3252-3258): a textual conflict, trivial to merge.
- P197 shrinks the candidates near the file's start. Shapes B and S4 no longer land on the table itself; S5 (500 bytes) still does, so #188 is still needed.
- #261 adds cross-reference streams with comment headers as candidates. On main such a stream is missed, and the relocation lands on the update's own table 245 bytes away (#188's defect), reported as Warning xref.chain-cycle and Error xref.chain-loop.
- XRefStm / relocation lenses (#156). On main, the looping shape above reports 'Warning file.size-wrong at offset 501: The trailer of the cross-reference table at offset 472 gives /Size 6, where the highest object number it and the sections it updates use, 3, makes it 4.' twice, word for word, in one validation. #156 lists file.size-wrong; that lens should check whether the duplicate is its own.
- Validator probe and settling (CrossReferenceProbe.cs:182 → PdfFileReader.PlaceHeader :1100 → TryFindObjectHeader :1139). Both #197 and #261 reach them through the shared methods. PlaceHeader's forward confirmation (HeaderAt) already reads comments, so a comment header found backwards is confirmed with no change. The probe and settling stay in agreement: CrossReferenceRuleTests.The_probe_judges_an_entry_as_settling_does, which includes 'a comment between its number and its generation, three bytes past the row', passes under every prototype.
- Severities (ADR 45) are unchanged per code; only the classification of rows near the start changes. Warning xref.entry-shifted becomes Error xref.entry-broken, and Warning xref.section-shifted becomes Warning xref.section-missing plus Error xref.section-not-found, for targets more than 512 bytes after an offset in the first 512 bytes. #261 moves comment-header rows the other way: from Error entry-broken (rebuilt, null) to Warning entry-shifted (found).
- Stream-end search (HeaderText.FindHeader, PdfObjectParser.IsHeader and MayBeHeaderBytes; StreamLengthTests.Text_that_follows_a_lost_endstream_stops_the_search_when_it_is_an_object_header_and_only_then, InlineData('12 0 %\nobj') and ('12 %\n0 obj')). Its documented reason, 'as the rebuild's scan', no longer holds after #261: question W3.
- #171. E1 (the nearby window's start cuts a number) and E2 (the scan's chunk start cuts '1'+70 zeros+'4', read as 4) reproduce its open bullets. P197e pays the nearby-window part. #261 makes headers longer, so the scan's 64-byte chunk overlap now also loses comment headers longer than about 61 bytes across a 1 MB boundary (measured: a 106-byte one is missed). The nearby search still takes the first header of its number left to right, not the nearest.
- #189 ('4 0 objx'). Neither prototype changes it on the backward path. B's forward path refuses 'objx' only where it lexes, never at the obj itself, so nothing of #189 is paid.
- Tests of other lenses that use '% ...\nobj' as a hiding device: ReadOrderTests (rebuild and read-order lenses), DocumentReaderTests.A_reference_the_chain_cannot_read_rebuilds_nothing_while_the_chain_is_read (chain lens, #182), ReaderLimitsTests:1090 and ReadOrderTests:1061 (guards lens, #167), and ObjectRuleTests.RebuiltAfterTwoObjectStreams and ConvergenceShapes (object-stream lens). Whoever lands #261 must switch them to 'N +0 obj' in the same change, or the other lenses' tests silently stop testing what they say.

## new_debts_or_neighbors

- Neighbor, already filed (#171): the nearby window's first byte can cut an object number. Shape E1: '14 0 obj' at 805, row 4 at 1318; main serves 'fourteen' as object 4 with Repair -512 and Warning xref.entry-shifted. Prototype p197e-edge-on-p197.diff pays it (question W4). Not a new issue; a comment on #171 records it.
- Neighbor, already filed (#171): the scan's chunk start cuts a header longer than its 64-byte overlap. '1'+70 zeros+'4 0 obj' straddling 1,048,512 is read as object 4 (a number past MaxNumber made valid). With #261, a 106-byte comment header straddling the boundary is missed. A comment on #171 should add the comment-header case.
- Possible new debt, for the lens that owns #156 or #188 to confirm: on main, one validation reports the same 'Warning file.size-wrong at offset 501 ... /Size 6 ... makes it 4.' twice. The file: a chain whose /Prev is relocated onto the newest table itself (the cross-reference stream with a comment header at /Prev +2, probe file main-final1.261, 'xref stream header 5 % c\n0 obj: at 225, /Prev +2').
- Residual of both #261 prototypes, worth a line where the rule is declared rather than an issue: a line whose start lies inside a string opened on an earlier line and closed after a '%' on that line. '(multi\nline 50%) 4 % c\n0 obj' is read as a header of object 50 at 'line 5|0', and object 4 is not found. Main finds neither.
- Residual shared with main: a comment whose '%' lies before the window or chunk start, and whose visible tail ends with digits before an end of line and 'N obj', reads as a header. It is the same class as #171's cut-at-the-edge bullet.
- Neighbor, not filed: a header whose number or generation carries a sign ('4 +0 obj', '+4 0 obj') is read by a load at its row. The scan and nearby search miss '4 +0 obj', and take '+4 0 obj' as 4 because the byte before the digits is never checked. Signs are valid integers (ISO 32000-1 7.3.3); the stream-end rule refuses them by design ('digits alone ... no sign'). If W2's implementation adopts '4 +0 obj' as the test-hiding device, its tests rely on this behavior; the maintainer may want it recorded.

## docs_to_update

- docs/threat-model.md:456-458: remove the #197 Known gap (if W1 fixes it). docs/threat-model.md:459-460: remove the #261 Known gap.
- docs/threat-model.md:478 (*An object that is not where its entry says*): drop 'though a window clamped at the file's start reaches further ([#197])'. Name the new DocumentReaderTests and CrossReferenceRuleTests, and the P197e left-edge test if W4 is taken.
- docs/threat-model.md:416 (*A missing section tried at many places*): name the CrossReferenceChainTests for a section named below 512 (600 after not found, 500 after found).
- docs/threat-model.md:596 (the validator, *Reading the file's bytes*): drop '([#197] says how far near the file's start)'.
- docs/threat-model.md:427 (*The rebuild's scan*): say that a header is read with comments between its tokens, as the lexer reads a line holding '%' from its start (B), or by the line's-first-% rule (A); that the search near an entry's offset reads one so too; and name the new tests (LF, CR, CRLF, comment between number and generation, between generation and obj, comment-only lines, section candidate, '% note 4' no longer a header).
- src/AdCodicem.Pdf/IO/PdfFileReader.cs:68-74 (NearbySearchRadius): state what 'within' means, a header or section lying whole in [o-512, o+512) clamped to the file, wherever o falls (#197).
- src/AdCodicem.Pdf/IO/PdfFileReader.cs:6283-6293 (TryReadHeaderBackwards summary): it reads over comments, and how.
- src/AdCodicem.Pdf/IO/PdfFileReader.cs:3224-3238 (TryRelocateXRefSection remarks) and :6056-6060 (TryFindObjectHeader summary): already say 'either side'; add the left-edge rule if W4 is taken.
- src/AdCodicem.Pdf/IO/HeaderText.cs:131-133 ('The scan that rebuilds an index refuses endobj and tokens a comment separates too'): no longer true after #261. Reword per W3; PdfObjectParser.cs:1636-1660 too if W3 extends the rule.
- docs/website/docs/concepts/diagnostics.md:52-53 ('A comment between the tokens makes no header here, as it makes none for the scan that rebuilds a damaged index'): reword per W3. docs/website/docs/reference/diagnostics.md:116 changes only if W3 extends the stream-end rule.
- Optional: docs/website/docs/reference/validation-rules.md:58 (xref.entry-broken) and :55 (xref.section-not-found) can add 'however near the file's start the offset lies'. Every other text stating 512 becomes true and needs no change: PdfFileReader.cs:971 and :3230; CrossReferenceProbe.cs:231; SectionNotFoundRule.cs:12 and :49; EntryShiftedRule.cs:8; EntryBrokenRule.cs:9; SectionShiftedRule.cs:11; PdfValidator.cs:25; concepts/lazy-reading.md:93; concepts/validation.md:104; adr/0050:18; milestones/M01.md:55; threat-model.md:430 and :608.
- Test comments that state the scan misses comment headers, to rewrite with the new hiding device:
- ReadOrderTests.cs:72-73, 198-199, 284-285, 1061;
- ReaderLimitsTests.cs:1090;
- DocumentReaderTests.cs:1905-1906;
- ObjectRuleTests.cs:3420, 3427;
- ConvergenceShapes.Records.cs:623;
- ConvergenceShapes.ObjectStreams.cs:33, 39, 295.
- docs/status.md at the session's end: record batch 19's #197 and #261 outcome. The history at :1939, :2708 and :2900 stays, though :2900's 'as in the rebuild's scan' is the decision W3 revisits.
