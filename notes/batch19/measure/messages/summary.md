# messages — counts in messages (#241 and its comment): count of one, signed distance, shared helper for reader and validator

Report: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/measure/messages/ — NOTE: the harness refused report.md ("Subagents should return findings as text, not write report files"), so the full report is this structured output itself. Every artifact it cites is in this folder: proto-a-counttext.diff, proto-b-counttext-with-tests.diff, tests-updated-only.diff, apply_proto.py, apply_tests.py, CountText.cs.txt, CountTextTests.cs.txt, ZzCountMessages.cs.txt, ZzCorpusDump.cs.txt, shapes-main2-{1,2}.txt (main, all shapes, identical), shapes-main-{1,2}.txt (main, first pair without S11e2, identical), shapes-proto-{1,2}.txt (identical), shapes-main-vs-proto.diff, suite-main.log, suite-proto.log, suite-proto-failures.txt, suite-proto-b.log, counttext-tests.log, corpus-main.txt, corpus-proto.txt, corpus-diff.txt, corpus-wording-check.txt, changed-docs.txt, wording_check.py, scan_manifest.py, probe.sh, shapes.sh, suite.sh, PdfFileReader.main.cs.txt. The worktree /home/user/AdCodicem.Pdf/.claude/worktrees/wf_9ceb535e-b92-8 holds proto-b applied (uncommitted) plus the two Zz probes.

INVENTORY at 21a0298 (307 interpolated strings in src/ read one by one; no count is concatenated, no verbatim/raw interpolation; the only exception that carries a count is PdfLimitExceededException, whose message is the limit.* text). "yes" = a count of one or a negative distance reaches it (S-numbers = probe shapes); "in principle" = reachable by the code's logic, no shape built; "agrees" = already picks the noun; "no" = cannot be one.
READER src/AdCodicem.Pdf/IO/PdfFileReader.cs:
R1 :2578 "…; the chain took its data up to its endstream, after {deferred.Taken} bytes." — in principle (xref stream whose data to endstream is 1 byte). Tests quote other counts.
R2 :2586 "The stream declared {length} bytes but ended after {deferred.Taken}." — YES (S10). DocumentReaderTests:1967-1968 (46, 38) and docs reference/diagnostics.md:27 (19954) unchanged.
R3 :2592 "…gives {length} bytes, which an endstream confirms; … up to an endstream {deferred.Taken} bytes in…" — in principle.
R4 :2832 "{HeaderSearchLength:N0} bytes" — no (constant 4,096).
R5 :2845 "The PDF header starts {index} bytes into the file; offsets were shifted accordingly." — YES (S1; corpus 15 docs). No test quotes it with 1.
R6 :2994, :2997 "more than {Bound(XRefSectionCount):N0} sections" — YES (S11a, MaxXRefSectionCount minimum is 1). HeaderOffsetTests:146 quotes "more than 1 sections" (3 cases).
R7 :3199 "{fault}" — through R11-R14 (agree or cannot be one).
R8 :3220 "which is {_source.Length:N0} bytes long" — no (a file naming a section holds startxref). CrossReferenceChainTests:85, :220, HeaderOffsetTests:61 use file lengths.
R9 :3293 "…names at offset {absolute} was found {candidate - absolute} bytes from there." — YES, SIGNED (S3: -1, -3, 2, 3; corpus IBM -12). CrossReferenceChainTests:47 (5 cases) and DocumentReaderTests:2820 quote it: every text changes form. Not listed in the issue.
R10 :3586 "The cross-reference table runs past {FormatLength(maxWindow)}" — YES (S11f) through FormatLength.
R11 :4370, R12 :4758, R14 :5117 / :5145 / :5464 (rows, bytes, values, rows, entries) — agree (inline count == 1 ? … : …).
R13 :4841 "{count:N0} rows, more than object numbers 0 to … hold" — no (count > 8,388,608).
R15 :5640 "Object {n} was found {nearby - offset} bytes from where the index said." — YES, SIGNED (S2a -1, S2b -3, S2d 2; corpus 998 lines, 2 docs). DocumentReaderTests:203 (-3; sv-SE, fa-IR).
R16 :5999, :6000, :6003 trailer / xref-stream dictionary / object "runs past {FormatLength(bound)}" — YES (S11e2, S11g) through FormatLength.
R17 :6210 "it decodes to {data.Length} bytes, fewer than its /First of {first}" (the fault xref.object-stream-broken quotes) — YES (S5b). No test quotes it.
R18 :6227, :6229, :6232 "its header lists {header.Count} of the {count} objects its /N declares, then …" (quoted by xref.object-stream-broken; the comment's case) — YES (S4, S4b, S4c). CrossReferenceRuleTests:748/770/784/817/1070 use /N 2, unchanged.
R19 :6417 "Object {n}, at byte … of object stream …'s decoded data, runs past {FormatLength(bound)}" — YES (MaxObjectLength 1) through FormatLength.
R20 :7560 "{Count:N0} definitions" — no (Count ≥ 2 in that arm).
R21 :7771 "which is {data.Length:N0} bytes long" — YES (S5a, S5b; corpus 5 docs). DocumentReaderTests:1495 (8) and docs diagnostics.md:231 unchanged.
R22 :7791, :7794, :7797 "after {header.Count:N0} of the {count:N0} objects its /N declares" — YES (S4/S4b/S4c; corpus 18 lines, 10 docs). DocumentReaderTests:1563 (2 cases), :1847-1848 quote "of the 1 objects".
R23 :7908 "only the first {kept:N0} of its {count:N0} objects" — no (1 ≤ kept < count).
R24 IO/XRef/ObjectStreamHeader.cs:138 "its {dataLength:N0} bytes of decoded data" — no (a pair is lexed from data[..first]: ≥ 3 bytes).
PARSER src/AdCodicem.Pdf/IO/PdfObjectParser.cs:
P1 :951 "The stream declared {length} bytes, and no endstream follows them; … without a search" — in principle (searches spent); pronoun "them".
P2 :954 "… the {kept} bytes {…} are taken as its data without a search" — in principle; verb "are".
P3 :970 "endobj follows the {length} bytes it declared, which are taken as its data." — YES (S9b); "which are".
P4 :988 "…: its object's endobj ends it, after {taken} bytes." — YES (S9c).
P5 :1004 "The stream declared {length} bytes, and no endstream follows them {Where}…" — in principle; "them".
P6 :1030 "the {upToStop} bytes {…} are taken as its data." — in principle; "are".
P7 :1227 "The stream declared {declared.Value} bytes but ended after {recovered}." — YES (S9a). No assertion with 1 (GuardCutValueTests:35 comment records #224's old "declared 1 bytes").
P8 :1228 "{Describe}; its data ends after {recovered} bytes." — in principle.
P9 :1237 Describe "The stream declared {declared.Value} bytes" (opens P2, P4, P6, P8) — YES.
P10 :1491, :1548, :1554 — agree.
FILTERS / GUARD:
F1 PdfFilterPipeline.cs:536, :546 — agree. F2 :595 ASCII85 "group of {UnitLength} digits" — no (one digit is UnmarkedAfterLoneDigit, Ascii85Filter.cs:160). F3 :603 — no (UnitLength 1 has :601).
F4 :605 "a run of {UnitLength} bytes, of which it holds {UnitHeld}; they were kept" — YES (S8): pronoun for UnitHeld 1.
F5 :627 "names more than {EndIndex:N0} filters; decoding stopped after the first {EndIndex:N0}." — YES (S11b, MaxFilterChainLength 1). ReaderLimitsTests:3673, FilterTests:740, docs reader-limits.md:102 use 2/64, unchanged.
F6 :676 "checksum disagrees with the {decoded} bytes its data decoded to; all were kept, and some may be wrong." — YES (S7a).
F7 :692 "the {decoded} bytes decoded before the fault was found were kept." — YES (S7b; corpus 5 docs). FilterDamageTests:1720/1723 helpers and CorpusReadingTests:439 never pass 1.
F8 :724, :725 "decode(s) to more than {FormatLength(bound)}" — YES (S11c) through FormatLength.
L1 PdfLimitGuard.cs:76 "A stream's data runs past {FormatLength(…)}" — YES (S11d). L2 PdfLimitGuard.cs:83 FormatLength "{length:N0} bytes" — YES: the shared arm of R10, R16, R19, F8, L1.
Q1 IO/FileQuote.cs:147 "(the first {bytes} of {at least }{whole:N0} bytes)" — no (a cut name is > 127 bytes; a cut keyword, only from FileHeaderVersionRule, is ≥ 3,067 bytes).
VALIDATOR src/AdCodicem.Pdf/Validation/:
V1 Rules/FileHeaderOffsetRule.cs:34 "The PDF header starts {header} bytes into the file instead of at its first byte." — YES (S1; corpus 15 findings). FileRuleTests:63 (19) unchanged.
V2 Rules/FileHeaderMissingRule.cs:45, V4 EndOfFileMarkerRule.cs:50, StartXRefMissingRule.cs:46 — no (an openable file holds an object; S12: a one-byte file throws "PdfFormatException: No PDF object could be found in the input.").
V3 Rules/FileHeaderMissingRule.cs:55 "starts {header} bytes into the file, past the first 1024 bytes" — NO: reached only for header ≥ 1,024. The issue lists this line; it cannot say "1 bytes".
V5 SectionNotFoundRule.cs:49, CrossReferenceProbe.cs:231 "within {NearbySearchRadius} bytes" — no (constant 512).
V6 PageTreeWalk.cs:929 "is an array of {other.Count} values, not a rectangle." — YES (S6). PageTreeRuleTests:428/463/835/862 (3 values) unchanged.
V7 BrokenObjectStream.cs:47/49/51/53 quote {Fault} = R17/R18 — YES through the reader.
V8 agree: CrossReferenceProbe.cs:161, :191, :208, :294-295, :352; OffsetImpreciseRule.cs:49, :66-69; ObjectPastSizeRule.cs:36-42; ObjectGraph.cs:343-375; ArlingtonText.cs:36-60, :86, :153; PageTreeWalk.cs:513, :774, :786; RuleText.cs:100; BrokenObjectStream.cs own counts.
V9 xref.section-shifted (SectionShiftedRule.cs:40) and xref.entry-shifted (CrossReferenceProbe.cs:208) use RuleText.Distance: they ALREADY say "before": "starts 1 byte before it, at offset 1336", "the object starts 1 byte before it", "starts 2 bytes after it". The validator never writes a negative count. Signed distances exist only at R9 and R15.
Object model ToString: PdfStream.cs:59-62 and PdfDictionary.cs:93 agree; PdfArray "[array of {n}]" and "<<dictionary of {n}>>" write no noun; PdfString.cs:140 cannot be one. PdfValidationReport.ToString writes labels ("errors 1").
TOTALS: 32 interpolation lines are wrong for one or a distance back and reachable — 25 shown by a shape (R2, R5, R6×2, R9, R15, R17, R18×3, R21, R22×3, P3, P4, P7, P9, F4, F5, F6, F7, L2, V1, V6), 7 in principle (R1, R3, P1, P2, P5, P6, P8) — plus 8 limit message lines that follow L2 (R10, R16×3, R19, F8×2, L1). Cannot be one: R4, R8, R13, R20, R23, R24, F2, F3, Q1, V2, V3, V4 (2), V5 (2).

TOUCHED TEXTS: unit tests — 6 assertion lines in 3 files, 14 test cases (DocumentReaderTests:203 ×2, :1563 ×2, :1847-1848 ×1, :2820 ×1; CrossReferenceChainTests:47 ×5; HeaderOffsetTests:146 ×3) plus ValidatorTests:577-578 (RuleText.Pages moves). tests/corpus/manifest.json: 0 entries (requiredDiagnostics holds codes, findings rule ids; scan_manifest.py found only prose in title/unsupported). Docs: docs/website/docs/reference/diagnostics.md:36 (the example "-7 bytes"); validation-rules.md quotes none of these texts.

## Reproduced

### #241 — matches today: True
Shape: S1: a sample document (TestPdfBuilder, 4 objects, classic table) with one space before %PDF- (control S1b: two spaces). Probe ZzCountMessages, shapes-main2-1.txt and -2.txt identical.

== S1 header one byte in
    F Warning file.header-offset at offset 0: The PDF header starts 1 bytes into the file instead of at its first byte.
    D Repair xref.offset-adjusted at 1: The PDF header starts 1 bytes into the file; offsets were shifted accordingly.
(S1b: "starts 2 bytes" both.) Corpus: 15 remote iPRES 2017 t02-05-01-* documents, as the issue's sweep. Sources moved: reader :1424 -> PdfFileReader.cs:2845; validator FileHeaderOffsetRule.cs:34 (unchanged). The issue's FileHeaderMissingRule.cs:55 cannot be given one (header >= 1,024 there).

### #241 — matches today: True
Shape: S2a/S2b/S2c/S2d: every row 1 byte past its object (offsetError 1), 3 past (control), 1 before, 2 before; object 4 decoded, document validated.

S2a: D Repair xref.offset-adjusted at 15: Object 1 was found -1 bytes from where the index said. (same for objects 2, 3, 4) — and the validator, already both ways: F Warning xref.entry-shifted at object 1 0, at offset 16: The entry of object 1 gives offset 16, where there is no object header; the object starts 1 byte before it, at offset 15.
S2b: "Object 1 was found -3 bytes from where the index said." / "the object starts 3 bytes before it".
S2c (1 before): no relocation; F Warning xref.offset-imprecise …: 4 offsets name the white space before what they designate rather than its first byte; the first: the entry of object 1 gives offset 14, 1 byte before its header.
S2d: D Repair xref.offset-adjusted at 15: Object 1 was found 2 bytes from where the index said. / F … the object starts 2 bytes after it, at offset 15.
Corpus: 998 lines in 2 docs — IBM QMF manual 997 (all back: 310×-12, 301×-10, 103×-6, 70×-7, 64×-4, 38×-8, 36×-1, …; its 1,000 diagnostics are DiagnosticCapacity's cap; the issue counted 76 "-1" on another read path) and ipres2017/t03-007-offset-wrong.pdf "Object 3 was found 60 bytes from where the index said.". Source :2977 -> PdfFileReader.cs:5640.

### #241 — matches today: True
Shape: S3: an update (AppendIncrementalUpdate) whose /Prev names the original table 1 and 3 bytes late, 2 and 3 bytes early.

== S3 /Prev off by 1
    F Warning xref.section-shifted at offset 1336: The cross-reference section /Prev names at offset 1337 starts 1 byte before it, at offset 1336.
    D Repair xref.offset-adjusted at 1336: The cross-reference section /Prev names at offset 1337 was found -1 bytes from there.
(+3: "-3 bytes from there" / "starts 3 bytes before it"; -2: "was found 2 bytes from there" / "starts 2 bytes after it"; -3: "3 bytes from there" / "3 bytes after it".) Corpus: IBM manual "…names at offset 1569328 was found -12 bytes from there." The section relocation (PdfFileReader.cs:3293) is not in the issue's list but has the same fault; the validator's xref.section-shifted already says before/after.

### #241 (comment) — matches today: True
Shape: S4/S4b/S4c: BuildWithXRefStream with object 3 packed in object stream 4, /N 1, header empty / 'x '-prefixed / '3 999 '; object 3 read, document validated.

S4:  F Error xref.object-stream-broken at object 4 0, at offset 116: Object stream 4, where the index places 1 object, cannot be read: its header lists 0 of the 1 objects its /N declares, then ends.
     D Warning object-stream.unreadable at 175: Object stream 4 has a header that ends after 0 of the 1 objects its /N declares; none of its objects can be read from it.
S4b: … then something else. / … holds something other than an object number and an offset after 0 of the 1 objects its /N declares; …
S4c: … its header lists 0 of the 1 objects its /N declares, then object 3 at offset 999, past what its 15 bytes of decoded data hold. / … after 0 of the 1 objects its /N declares; …
Exactly the comment's texts. Corpus: 18 diagnostics "of the 1 objects" in 10 documents (issue: 9). No corpus finding quotes it (the comment's 122 findings come from batch 18's 23,069 review inputs, not this corpus). Sources: fault PdfFileReader.cs:6227/:6229/:6232, diagnostic :7791/:7794/:7797.

### #241 — matches today: True
Shape: S5a: rebuild (no xref) of object stream 5 '/N 1 /First 40', data 'X' (1 byte). S5b: hand-written xref stream placing object 6 at index 0 of object stream 5 '/N 1 /First 5 /Length 1', data 'X'.

S5a: D Warning object-stream.unreadable at 176: Object stream 5 gives in /First an offset, 40, past the end of its decoded data, which is 1 bytes long; none of its objects can be read from it.
S5b: F Error xref.object-stream-broken at object 5 0, at offset 123: Object stream 5, where the index places 1 object, cannot be read: it decodes to 1 bytes, fewer than its /First of 5.
     D Warning object-stream.unreadable at 182: Object stream 5 gives in /First an offset, 5, past the end of its decoded data, which is 1 bytes long; none of its objects can be read from it.
Corpus: "1 bytes long" 5 lines in 5 documents (issue: 4). Sources :4207 -> :7771; the validator's fault at :6210 is new to the inventory.

### #241 (comment) — matches today: True
Shape: S6: a page whose /MediaBox is [612].

F Warning page-tree.mediabox-invalid at page 1, object 3 0: The /MediaBox of object 3 is an array of 1 values, not a rectangle.  (PageTreeWalk.cs:929)

### #241 — matches today: True
Shape: S7a: zlib('A') with its Adler-32 last byte flipped, decoded alone. S7b: zlib stored block of 1 byte 'A' then a block of reserved type (78 01 00 01 00 FE FF 41 07 00 00 00). S7c: same with 2 bytes (control).

S7a: D Warning filter.checksum-mismatch: A Flate stream's checksum disagrees with the 1 bytes its data decoded to; all were kept, and some may be wrong.
S7b: D Warning filter.failed: A Flate stream is corrupt at byte 9 of its 12; the 1 bytes decoded before the fault was found were kept.
S7c: … the 2 bytes decoded before the fault was found were kept.
Corpus: "the 1 bytes decoded before the fault" 5 lines in 5 documents (issue: 4 for both forms). Source PdfFilterPipeline.cs:237 -> :692 (and :676).

### #241 (inventory) — matches today: True
Shape: S8: RunLength data 02 41 (a run of 3 literal bytes, 1 present).

D Warning filter.failed: A RunLength stream ends inside a run of 3 bytes, of which it holds 1; they were kept, and data may have been lost.  (PdfFilterPipeline.cs:605 — pronoun, not noun)

### #241 (inventory) — matches today: True
Shape: S9a: object 5 '<< /Length 1 >> stream ABC endstream'. S9b: /Length 1, data 'X', then endobj, no endstream. S9c: /Length 99, data 'X', endobj. S10: ChainFiles.AloneWithIndirectLength(-41) (xref stream /Length 4 0 R gives 1, data 42).

S9a: D Warning stream.length-invalid at 325: The stream declared 1 bytes but ended after 3.
S9b: D Warning stream.truncated at 325: The stream has no endstream: its object's endobj follows the 1 bytes it declared, which are taken as its data.
S9c: D Warning stream.truncated at 326: The stream declared 99 bytes, and no endstream follows its data before the next object, at 335: its object's endobj ends it, after 1 bytes.
S10: D Warning stream.length-invalid at 298: The stream declared 1 bytes but ended after 42.
(PdfObjectParser.cs:1227, :970, :988; PdfFileReader.cs:2586.) No corpus document reaches these.

### #241 (inventory) — matches today: True
Shape: S11a-g: reader limits of one (PdfReaderLimits allows a minimum of 1): MaxXRefSectionCount on the S3 file; MaxFilterChainLength with /Filter [/ASCIIHexDecode /ASCIIHexDecode]; MaxDecodedStreamLength; MaxStreamLength; MaxTrailerLength on a classic file (S11e) and an xref-stream file (S11e2); MaxXRefSectionLength; MaxObjectLength.

S11a: D Warning limit.xref-section-count at 1336: The cross-reference chain has more than 1 sections; the older ones were not read. Raise PdfReaderLimits.MaxXRefSectionCount to read past it.
S11b: D Warning limit.filter-chain: The /Filter entry names more than 1 filters; decoding stopped after the first 1. Raise …
S11c: D Warning limit.decoded-stream: The /ASCIIHexDecode data decodes to more than 1 bytes; decoding stopped there. Raise …
S11d: D Warning limit.stream-length at 258: A stream's data runs past 1 bytes; only what lies within it was read. Raise …
S11e: (nothing — documented: a classic trailer within its table's window is read whole)
S11e2: D Warning limit.trailer at 294: The dictionary of the object read as a cross-reference stream runs past 1 bytes; only what lies within it was read. Raise …
S11f: D Warning limit.xref-section-length at 294: The cross-reference table runs past 1 bytes; only the entries within it were read. Raise …
S11g: D Warning limit.object at 15: Object 1 runs past 1 bytes, its stream data aside; only what lies within it was read. Raise … (×4 objects)
S12 (one-byte file '%'): THREW PdfFormatException: No PDF object could be found in the input. — which is why V2/V4 can never say '1 bytes'.

## Prototypes

### proto-a: CountText in AdCodicem.Pdf.IO, 32 sites routed through it (src only)
Diff: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/measure/messages/proto-a-counttext.diff

New src/AdCodicem.Pdf/IO/CountText.cs (internal static, beside IO.FileQuote; copy CountText.cs.txt): Bytes(long), Objects(long), Pages(long), Of(long count, string one, string many) => $"{count} {(count == 1 ? one : many)}" (invariant), Grouped(…) => same with {count:N0}, Distance(long delta, string from) => delta < 0 ? $"{Bytes(-delta)} before {from}" : $"{Bytes(delta)} after {from}". RuleText loses Bytes/Objects/Pages/Distance (its summary now points to IO.CountText); their 13 call sites (CrossReferenceProbe ×4, OffsetImpreciseRule, SectionShiftedRule, PageTreeWalk ×3, BrokenObjectStream ×5) call CountText, RuleText.Distance(d) becoming CountText.Distance(d, "it") so no validator text changes; ValidatorTests:577-578 move with RuleText.Pages. Sites (all edits in apply_proto.py): R5/V1 header Bytes; R9 Distance(candidate - absolute, "it"); R15 Distance(nearby - offset, "where the index said"); R17 Bytes, R18 Objects (ungrouped as today); R21/R22 Grouped (grouped as today); R1-R3 Bytes; R6 Grouped(…, "section", "sections"); L2 FormatLength's last arm Grouped(length, "byte", "bytes") (carries R10, R16, R19, F8, L1); P1-P9 Bytes plus the clause's verb/pronoun (follows it/them, is/are taken, which is/are taken), declared.Value passed as .GetValueOrDefault() as PdfObjectParser.cs:840 already does; F4 'it was'/'they were' kept; F5 'names more than {Grouped(EndIndex, filter, filters)}; decoding stopped after the first{(EndIndex == 1 ? "" : " " + N0)}.'; F6 '{Bytes(decoded)} its data decoded to; {(decoded == 1 ? "it was kept, and may be wrong" : "all were kept, and some may be wrong")}.'; F7 '{Bytes(decoded)} decoded before the fault was found {was/were} kept.'; V6 'an array of {Of(Count, "value", "values")}'. Untouched: the sites that cannot be one and the sites that already agree. git diff --stat: 13 files, +94 −67. No digit grouping changes anywhere.

Shapes after: shapes-proto-1.txt = shapes-proto-2.txt (identical). Every difference from main (shapes-main-vs-proto.diff, 45 changed lines, nothing else differs):
S1: F … The PDF header starts 1 byte into the file instead of at its first byte. / D … The PDF header starts 1 byte into the file; offsets were shifted accordingly.
S2a: Object 1 was found 1 byte before where the index said. (×4) S2b: … 3 bytes before where the index said. S2d: … 2 bytes after where the index said. (validator texts unchanged: 'the object starts 1 byte before it')
S3: … names at offset 1337 was found 1 byte before it. / 1339 … 3 bytes before it. / 1334 … 2 bytes after it. / 1333 … 3 bytes after it.
S4: F … its header lists 0 of the 1 object its /N declares, then ends. / D … ends after 0 of the 1 object its /N declares; none of its objects can be read from it. (S4b, S4c likewise)
S5a/S5b: … which is 1 byte long; … / F … it decodes to 1 byte, fewer than its /First of 5.
S6: … is an array of 1 value, not a rectangle.
S7a: … checksum disagrees with the 1 byte its data decoded to; it was kept, and may be wrong.
S7b: … the 1 byte decoded before the fault was found was kept.
S8: … of which it holds 1; it was kept, and data may have been lost.
S9a: The stream declared 1 byte but ended after 3. S9b: … endobj follows the 1 byte it declared, which is taken as its data. S9c: … its object's endobj ends it, after 1 byte. S10: The stream declared 1 byte but ended after 42.
S11a: … has more than 1 section; … S11b: … names more than 1 filter; decoding stopped after the first. Raise … S11c: … decodes to more than 1 byte; … S11d/S11e2/S11f/S11g: … runs past 1 byte …
Controls with counts ≠ 1 (S1b, S7c, S9c's '99 bytes', S4c's '15 bytes') unchanged.

Unit suite: tests/AdCodicem.Pdf.Tests, Release, without the remote corpus (as CI's unit run), Zz probes included. MAIN 21a0298: total 6,659, failed 0, succeeded 6,639, skipped 20 (suite-main.log). PROTO-A: total 6,659, failed 14, succeeded 6,625, skipped 20 (suite-proto.log, suite-proto-failures.txt). Every failure is a test quoting a reworded text — 6 assertion lines, 3 files: DocumentReaderTests.Writes_the_distance_to_an_object_found_before_its_entry_the_same_whatever_the_culture (sv-SE, fa-IR) :203 '" was found -3 bytes from where the index said."' (R15); DocumentReaderTests.Reports_an_object_stream_whose_header_starts_with_a_stray_token_as_serving_none_of_its_objects (2 cases) :1563 'after 0 of the 1 objects' (R22); DocumentReaderTests.Indexes_no_member_a_rebuild_finds_listed_under_a_number_no_object_can_have :1847-1848 (R22, twice); DocumentReaderTests.A_place_a_section_may_have_moved_to_whose_dictionary_a_guard_cuts_is_no_section_the_chain_ends_at :2820 'was found {stream - named} bytes from there.' (R9); CrossReferenceChainTests.Reads_a_section_that_prev_names_a_few_bytes_off (-7, -2, 1, 3, 12) :47 'was found {-error} bytes from there.' (R9, every case); HeaderOffsetTests.A_chain_cut_where_it_goes_on_past_what_a_long_holds_is_placed_at_the_section_naming_it (3 cases) :146 'more than 1 sections' (R6). No test quotes a count of one for R2, R5, R17, R18, R21, P1-P9, F4-F8, L1-L2, V1, V6.

Corpus: ZzCorpusDump (lot 18's, unchanged) over 403 documents (168 committed + 235 remote linked; 7 remote absent locally), each validated twice; corpus-main.txt vs corpus-proto.txt via cmp_corpus.py (corpus-diff.txt): 'docs 403 403 same keys True / F diff 15 F order 0 D diff 28 D order 0 / totals main F 2982 D 1343 / totals branch F 2982 D 1343'. WORDING ONLY, proven: wording_check.py rewrites main's dump with the 13 wording rules alone and compares document by document, line by line: 'documents 403 mismatches after rewriting 0' (corpus-wording-check.txt). Lines changed 1,072 = 1,042 diagnostics + 30 finding lines (15 findings × 2 rounds): object relocation 998, of the 1 objects 18, header offset 15 D + 15 F, 1 bytes long 5, flate corrupt 1 5, section relocation 1. So 1,057 messages change in 28 documents (7 committed: jp-nta indesign-distiller18-nta-gift-tax-vertical, opf pdfmaker9-word-distiller-aes128-{no-accessibility-extraction,no-copy,no-printing,unknown-open-password}, pikepdf livecycle-dod-dd293-aes128-xfa, us-federal livecycle-uscis-ar11-xfa-form; 21 remote: 15 ipres2017 t02-05-01-*, ipres2017 t03-007-offset-wrong, IBM QMF manual, canada livecycle-es9/es10, jhove-hul-109, jhove-hul-114). Counts, codes, severities, positions and order are main's. Manifest: 0 entries change.

### proto-b: proto-a + the 6 test assertions + CountTextTests (one test per message family with a count of one)
Diff: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/measure/messages/proto-b-counttext-with-tests.diff

proto-a plus tests-updated-only.diff (apply_tests.py: DocumentReaderTests:203 -> '" was found 3 bytes before where the index said."', :1563/:1847/:1848 -> 'of the 1 object', :2820 -> 'bytes after it.'; CrossReferenceChainTests theory now [InlineData(-7, "7 bytes after")] … [InlineData(1, "1 byte before")] … with 'was found {found} it.'; HeaderOffsetTests:146 -> 'more than 1 section') and tests/AdCodicem.Pdf.Tests/CountTextTests.cs (CountTextTests.cs.txt, 25 cases): the helper (singular for one alone incl. 0 and -1, each noun, Grouped, Distance both ways, sv-SE/fa-IR cultures); header 1 byte (reader + validator); object relocation 1 byte before / 2 bytes after; section relocation 1 byte before / 2 bytes after it; /N 1 (finding + diagnostic); data of 1 byte (finding + diagnostic); /MediaBox of 1 value; Flate checksum, Flate corrupt, RunLength held 1; stream /Length 1 (3 forms); xref-stream deferred /Length 1; limits of one (section count, filter chain, decoded stream, stream length). Not covered: P1, P2, P5, P6, P8, R1, R3 (in-principle sites) and R10/R16/R19 (only through FormatLength's shared arm).

Shapes after: Same as proto-a (tests only added).

Unit suite: PROTO-B: total 6,684 (6,659 + 25), failed 0, succeeded 6,664, skipped 20 (suite-proto-b.log); CountTextTests alone 25/25 (counttext-tests.log). ADR 53 cost: CountText's 3 conditions are killed by CountTextTests; the call sites add 9 verb/pronoun conditions (P1, P2, P3, P5, P6, F4, F5, F6, F7), of which P1, P2, P5, P6 have no test with one yet (P1/P2 need the document's endstream searches spent, ChainFiles.UpdatesSpendingTheSearches).

Corpus: Identical to proto-a (tests do not change src).

## Questions

### 241-Q1 (#241)
How should the reader say where it found a relocated object or section?

- [R] (a) 'Object 3 was found 1 byte before where the index said.' / '… 2 bytes after where the index said.'; section: '… names at offset 1337 was found 1 byte before it.' / '… 2 bytes after it.': The issue's own words; 'it' is the named offset, as in the validator's xref.section-shifted 'starts 1 byte before it, at offset 1336'. CountText.Distance(delta, referent). 999 corpus diagnostics reworded (997 IBM + 1 t03-007 + IBM's section); 3 assertion lines / 8 cases updated (DocumentReaderTests:203, :2820, CrossReferenceChainTests:47); no doc change. Codes and severities (xref.offset-adjusted, Repair) unchanged.
- (b) Name the found offset too: 'Object 3 was found at offset 15, 1 byte before where the index said.' / '… was found at offset 1336, 1 byte before it.': Mirrors the validator's 'starts …, at offset N'; repeats the diagnostic's Position; longer; same tests touched, plus each expected text gains the offset.
- (c) No pronoun: '… 1 byte before the offset the index gives' / '… 1 byte before that offset': No referent to find; departs from the validator's established 'before it' form, so reader and validator word the same fact differently.

Why: It is what the issue's closing list asks for, it reuses the validator's existing Distance form (one helper, one wording for both), and the diagnostic already carries the found position, so (b) only repeats it.

### 241-Q2 (#241)
Should the counts the helper writes group thousands (N0)?

- [R] (a) Keep every site's digits as today: CountText.Of (ungrouped) where the site writes {n}, CountText.Grouped where it writes {n:N0}; file the inconsistency as debt: The change stays wording-only (proven over the corpus: 0 mismatches after the wording rewrite); no other test, doc example or manifest entry moves. The object stream header fault keeps two digit styles: validator's fault '{count}' (PdfFileReader.cs:6227-6232) vs diagnostic '{count:N0}' (:7791-7797) — HostileInputTests:157 reads 'of the 1,000,000,000 objects', the finding would read 'of the 1000000000 objects'.
- (b) Group every count: Changes e.g. 'The stream declared 19954 bytes' (docs reference/diagnostics.md:27, :216-217, :289-290), RuleText-era validator counts, the header offset, Flate counts, and dozens of test texts; a value the file wrote (/Length 19954) is then quoted otherwise than written.
- (c) Group no count: Changes every limit text ('more than 1,000 bytes' -> '1000 bytes', FormatLength, docs reference/reader-limits.md and diagnostics.md:36 '1,048,576 bytes', ~20 test texts) and the grouped /N, /First, rows, entries texts.

Why: #241 is about agreement and direction; changing grouping too would multiply the corpus and test churn far past the 1,057 messages that are actually wrong, and the grouping policy (including the one fault written twice) deserves its own decision rather than riding on this fix.

### 241-Q3 (#241)
When a count of one carries a verb or pronoun in the same clause, agree them by the count, or reword so only the noun depends on it?

- [R] (a) Agree them by the count (it/them, is/are, was/were, 'it was kept, and may be wrong'): Precedent: BrokenObjectStream (is/are, it/them) and CrossReferenceProbe.cs:294, :352 (was/were, it/them). Plural texts unchanged (no doc example, no existing test moves). 9 new conditions at call sites (P1, P2, P3, P5, P6, F4, F5, F6, F7), each needing a test with one under ADR 53; P1, P2, P5, P6 have none yet (P1/P2 need the endstream searches spent).
- (b) Reword so nothing but the noun depends on the count ('and no endstream follows its data', 'what lies up to the next object, 1 byte, is taken as its data', 'what decoded before the fault, 1 byte, was kept'): No condition beyond CountText's; but the plural texts change too: docs reference/diagnostics.md:216-217, :289-290, FilterDamageTests' CorruptAt/ChecksumMismatch helpers, CorpusReadingTests:439 and some 40 test texts; larger corpus diff (every Flate fault, every stream length fault).

Why: It follows the codebase's established agreement style and leaves every plural message, test and doc example as it is; the cost is four extra tests, which ADR 53 would demand anyway.

## decided_without_question

- Placement: AdCodicem.Pdf.IO.CountText (internal static), beside IO.FileQuote. The validator already depends on IO (RuleText's summary cites IO.FileQuote; CrossReferenceProbe, PageTreeWalk use IO); Objects already calls IO.FileQuote (PdfName.ToString); the reader never depends on Validation — the comment's point holds. The Diagnostics namespace holds only public types and no text helper.
- One place: RuleText.Bytes/Objects/Pages/Distance move into CountText (13 call sites in 5 lines' worth of files + ValidatorTests:577-578); RuleText keeps Kind, Capitalize, Path. Thin delegates would leave two helpers.
- '1 byte' in digits, not 'one byte': RuleText.Bytes ('1 byte'), PdfStream.ToString ('1 byte', documented in reference/object-model.md:52), ArlingtonText.Elements ('1 element'), BrokenObjectStream ('1 object') all write the digit.
- The distance is 'N byte(s) before|after <referent>' with the referent passed by the caller: 'it' for the validator's two texts (unchanged) and the section relocation, 'where the index said' for the object relocation. Zero cannot reach any site (TryRelocateXRefSection skips candidate == absolute; an object found at its own offset would have parsed there; WasRelocated and HeaderPlace.Nearby imply a move). -long.MinValue cannot arise (distances are bounded by the file).
- The validator's xref.entry-shifted and xref.section-shifted texts already handle 'before' (RuleText.Distance): 'the object starts 1 byte before it, at offset 15', 'starts 12 bytes before it, at offset 1569316' on the IBM manual. They need no change beyond the call moving to CountText.Distance(delta, "it").
- Limits of one: FormatLength(1) -> '1 byte' (shared by MaxStreamLength, MaxDecodedStreamLength, MaxObjectLength, MaxXRefSectionLength, MaxTrailerLength texts); 'more than 1 section'; 'names more than 1 filter; decoding stopped after the first.' (plural 'after the first 2.' unchanged; 'after the first filter' would do as well).
- Sites a count of one cannot reach keep their text (R4 :2832, R8 :3220, R13 :4841, R20 :7560, R23 :7908, R24 ObjectStreamHeader.cs:138, F2 :595, F3 :603, FileQuote.cs:147, FileHeaderMissingRule.cs:45/:55, EndOfFileMarkerRule.cs:50, StartXRefMissingRule.cs:46, SectionNotFoundRule.cs:49, CrossReferenceProbe.cs:231), each with its reason in the inventory; routing them through CountText is free and may be done for uniformity. FileHeaderMissingRule.cs:55, which the issue lists, cannot say '1 bytes' (header >= 1,024).
- Sites that already agree inline (PdfFileReader.cs:4370, :4758, :5117, :5145, :5464; PdfObjectParser.cs:1548; RuleText.Path; ArlingtonText.Elements; PdfStream/PdfDictionary.ToString) may move onto CountText.Of/Grouped with no text change; #241 does not need it.
- The culture test DocumentReaderTests:188-209 loses its minus sign (Swedish U+2212, Persian U+200E U+2212): its assertion becomes '3 bytes before where the index said' and its comment is rewritten; CountTextTests keeps a culture case for grouped digits and for the distance.
- tests/corpus/manifest.json needs no change: requiredDiagnostics lists codes and findings lists rule ids; no message text is held there.
- Codes, severities and positions are untouched: xref.offset-adjusted stays a Repair at the found position; every other code keeps its severity (ADR 45 unaffected).

## interactions

- relocation lens (#188, #281): rewrites TryRelocateXRefSection and its reports; the section relocation message (PdfFileReader.cs:3293) must keep CountText.Distance(candidate - absolute, "it"), and any new report of a relocation refused, or landing on a section already read, that states a distance uses it too. Its corpus diff on the IBM QMF manual will also show this lens's rewording ('was found 12 bytes before it') — compare with wording normalized (wording_check.py) to separate the two.
- Shared test lines: CrossReferenceChainTests:47 (Reads_a_section_that_prev_names_a_few_bytes_off) and DocumentReaderTests:2820 are relocation tests the relocation lens may also change; DocumentReaderTests:203 is the object relocation's culture test.
- window lens (#197, #261): the nearby radius appears as a constant in CrossReferenceProbe.cs:231 and SectionNotFoundRule.cs:49 ('within 512 bytes of it'); if the reach near the file's start changes, those texts follow; never a count of one.
- partial lens (#207, #210, #222): any new report of a section read in part ('holds N rows where its /Index and /Size declare M', 'the last row …', a byte count of a short stream) writes its counts through CountText (PdfFileReader.cs:4370's inline row/rows is the precedent; Grouped keeps its N0).
- guards lens (#264): every guard text goes through PdfLimitGuard.FormatLength, which now agrees ('runs past 1 byte'); new guard texts should keep using FormatLength.
- xrefstm (#156), stream-prev (#208), reserved (#221): any new message that writes a count or a distance uses CountText; FileQuote stays the way to quote a name.
- Corpus comparisons of every lens in this batch: if #241 lands first, every other lens's main-vs-proto corpus diff gains 1,057 wording-only lines in 28 documents (998 of them in the IBM manual); normalizing with wording_check.py's rules removes them.

## new_debts_or_neighbors

- One object stream fault, two texts: ReadObjectStreamHeader (PdfFileReader.cs:6210-6232, quoted by xref.object-stream-broken: 'it decodes to N bytes, fewer than its /First of F', 'its header lists X of the N objects its /N declares, then …', ungrouped) and the reader's object-stream.unreadable (:7771-7797: 'gives in /First an offset, F, past the end of its decoded data, which is N bytes long', 'has a header that … after X of the N objects …', grouped) word the same fault differently and group digits differently. Debt candidate: one writer for a header fault (Q2 decides the grouping).
- Digit grouping is inconsistent across messages (N0 on limits, /N, /First, rows, entries, strays; none on offsets, stream lengths, Flate counts, header offset, RuleText counts) — debt if Q2 (a) is chosen.
- PdfFileReader.cs:7908 'only the first 1 of its 2 objects can be read from it' reads oddly when one is kept ('only the first of its 2 objects') — wording, not agreement.
- PdfFilterPipeline.cs:605 with one byte held reads, after the prototype, 'of which it holds 1; it was kept' (it … it) — Q3's option (b) would reword it.
- tests/AdCodicem.Pdf.Tests/GuardCutValueTests.cs:35 records #224's old texts ('declared 1 bytes') in a comment — history, may stay or be updated.
- MaxTrailerLength = 1 on a classic file reports nothing (S11e) — documented in PdfReaderLimits.cs:134-136 (a classic trailer ending within its table's window is read whole): not a defect, noted to avoid a false lead.

## docs_to_update

- docs/website/docs/reference/diagnostics.md:36 — the example '-7 bytes' ('numbers are written in the invariant culture …: `-7 bytes`, `1,048,576 bytes`') no longer exists: no message writes a negative count of bytes. Replace with a minus sign the file wrote (e.g. `object -5`, from 'lists object -5 at offset 0', or the /Length -1 text), and add a bullet to 'How a message quotes the file': a count agrees with its noun (`1 byte`, `2 bytes`) and a distance says which way it runs (`was found 3 bytes before where the index said`).
- docs/website/docs/reference/diagnostics.md examples at :27, :216-217, :231, :289-290, :293 and reference/reader-limits.md:78, :102 quote counts other than one: unchanged under Q2 (a) and Q3 (a); they change under Q2 (b)/(c) or Q3 (b).
- docs/website/docs/reference/validation-rules.md: no change (its rows for file.header-offset, xref.section-shifted, xref.entry-shifted, xref.object-stream-broken, page-tree.mediabox-invalid describe, they quote no message).
- docs/status.md: the batch's record (#241 closed by the batch; the 1,057 reworded messages in 28 corpus documents).
- XML documentation: CountText's summary (new), RuleText's summary (no longer says it holds counts and distances).
