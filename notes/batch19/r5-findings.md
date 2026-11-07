######## [1] lens=window key=ws-guard-feeds-hybrid-and-loop-errors sev=minor -> narrowed/minor maint=False
## location
src/AdCodicem.Pdf/IO/PdfFileReader.cs:5100-5114 (white-space guard: NotFound, CutByLimit left false), with :3512-3522 (the /XRefStm's search stops at the /Prev's place, LeaveToPrevious) and src/AdCodicem.Pdf/Validation/Rules/HybridStreamWrongRule.cs:47-52
## claim
White space can run one byte past MaxTrailerLength before what an /XRefStm names, or before what an /XRefStm and a /Prev both name. The section then reaches the white-space guard, reads as NotFound and is looked for nearby (#289). Under default limits that search now feeds batch 19's #156/#188 code, and three Errors and a Warning follow that a 1 MB bound does not give: the guard's cut is charged to the file (invariant 12, and round 3's [10] principle).
(A) The search stops at the /Prev's place and the /XRefStm is reported as naming what its /Prev names.
(B) The /XRefStm takes the oldest table into the stream's place, so the /Prev of the next section reads as a loop.
(C) Where both name one offset, the Error is not worded as the limit's.
The round 4 triage of R4a's open point calls this 'behavior as main but the guard's message'. For hybrid files that is not so: main gave Warnings only, and served object 7's old revision with object 5 null. b4ab948 behaves the same as head apart from the guard's wording, so this is not a round 4 regression. It is #289's family running through batch 19 code, and #289's close criteria name neither of these rules.
## evidence
(A) ZzHybridEdge, samePrev=False, run=64K+1. T0 (old seven) is at 230; objects 5 and 7 follow it. A run of 65,537 line feeds starts at 426. Stream 9 (/Type /XRef, 5 and 7) is at 65963. The newest table gives /Prev 230 and /XRefStm 426.
- Head:
  - S /XRefStm NamedAgain off=426
  - D limit.trailer @426 'White space and comments run past 64 KB where a cross-reference section is named; what follows them was not read.'
  - D xref.hybrid-stream-lost @426 '...names offset 426, what its /Prev names too, which the chain looks for through that /Prev...'
  - repairedAfter=True
  - F xref.hybrid-stream-wrong [Error] '...names offset 426, the section its /Prev names too, which the chain read through that /Prev...'
- MaxTrailerLength 1 MB: 'S /XRefStm Read Stream off=426 pad=65537', no hybrid-* report.
- b4ab948: identical, limit.trailer worded 'The dictionary of the object read as a cross-reference stream runs past 64 KB'.
- main: '/XRefStm Relocated Table off=230', section-shifted [Warning], obj5=PdfNull, obj7=old seven, no Error.

(B) ZzXRefStmOneByteBeforeThePrevsRun, run=70000. The /XRefStm names 1627, the end of line before a 70,000-byte run. The /Prev names 11628, 60,000 bytes before stream 4, whose own /Prev is T0 at 1481.
- Head:
  - S /XRefStm Relocated Table off=1481
  - D limit.trailer @1627
  - D xref.offset-adjusted @1481
  - D xref.hybrid-stream-lost @1627 '...a cross-reference table, whose rows were read in the stream's place'
  - D xref.chain-cycle @1481 'the /Prev of the section at offset 11628 names offset 1481, which the chain has already read.'
  - F xref.chain-loop [Error]
  - F xref.hybrid-stream-wrong [Error]
- 1 MB: '/XRefStm Read Stream off=1627 pad=70001', no loop, no hybrid report.
- b4ab948: identical apart from the guard's wording.
- main: Relocated Table 1481, the /Prev reads T0 again, Warnings only, obj5=PdfNull, obj7=old seven.

(C) samePrev=True: the /XRefStm and the /Prev both name 426, run 65,537.
- Head: '/Prev Relocated Table off=230 ... found 196 bytes before it', limit.trailer, hybrid-stream-lost, and the Error '...the section its /Prev names too, which the chain read through that /Prev'. For a same-offset guard cut, round 3's [10] decided the wording 'past the limit that cut it there'.
## fix
This is #289's ground, placed in batch 27. Do not change code in batch 19. Add to the planned #289 comment the three consequences batch 19 gives when a section whose own white space reached the guard is looked for nearby:
(a) an /XRefStm's search stops at its table's /Prev place, giving hybrid-stream-lost and the hybrid-stream-wrong Error;
(b) an /XRefStm relocated to an older table read in the stream's place leaves the next /Prev reported as chain-cycle and chain-loop (with #330);
(c) a same-offset naming gets the read wording, not the limit's.
Also extend #289's close criteria beyond 'StartXRefWrongRule and SectionNotFoundRule stand down' to HybridStreamWrongRule, ChainLoopRule and the reader's xref.hybrid-stream-lost. Correct decisions.md's triage line ('behavior as main') for hybrid files.
If the maintainer prefers to fix it now: a section whose reading met the white-space guard becomes the guard's (CutByLimit; an /XRefStm naming recorded as KeptPlace.Guard is, NameAgain with readAs null, nothing reported, index not left incomplete), with (A), (B) and (C) as test rows.
## VERDICT reason
(A) reproduces exactly. (B) holds in a cleaner layout. (C) is refuted. In every case the root is #289: white space past MaxTrailerLength makes an /XRefStm's section NotFound, CutByLimit false, and it is looked for nearby. No guard-specific path is new. What batch 19 changed is what a nearby search of an /XRefStm that holds nothing leads to:
- the stop at the /Prev's place (round 2 [0]; validation-rules.md:60, 'as the nearest section to an offset that holds none where that /Prev keeps its place');
- a table read in the stream's place reported (#156);
- the loop check by positions (Q9; #330's family).
Garbage in place of the run gives the same outcomes without any guard.

Against main, the batch improved the data and hardened the verdict:
- Data: main served object 5 as null and object 7's old revision. Head serves 'five' and 'new seven'.
- Verdict: main charged the guard's cut to the file as a Warning (section-shifted). Head charges it as one Error in (A) and two Errors in (B2). A 1 MB bound gives neither.

So the decisions.md triage line ('behavior as main but the guard's message and the stream's header window') is incomplete for hybrid files. #289's close criteria name only StartXRefWrongRule and SectionNotFoundRule.

The docs do not misstate the code. reader-limits.md:21 and diagnostics.md:141 say the section is 'looked for nearby as one that is not there'. diagnostics.md:115 and validation-rules.md:60 describe the stop.

(C) is refuted. With /Prev and /XRefStm on one offset, the hybrid-stream-wrong Error also stands under 1 MB, as the decided branch (c). Main gives a chain-loop Error at both bounds, so the guard creates no Error there. Round 3 [10]'s limit wording is for a /Prev's section that a guard cut (KeptPlace.Guard). That does not apply here.

The finding's own (B) layout does not reproduce its 1 MB line. With the /XRefStm and the /Prev both reaching stream 4, the 1 MB read also gives the decided branch (c) Error. The B2 layout below, with a hybrid stream of its own, shows the claimed difference.

This is not a round 4 regression, and the code needs no change in batch 19.
## VERDICT evidence
The probe source is /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r5ref-ws-probe/ZzWsGuardHybridRefuteTests.cs. Its outputs are in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r5ref-ws-out/head/ and /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r5ref-ws-out/main/.

**(A)** t0=230, runAt=426, stream=65963, /Prev 230, /XRefStm 426, run 65,537.
- Head, default:
  - /XRefStm NamedAgain off=426; /Prev Read Table 230.
  - D limit.trailer @426 'White space and comments run past 64 KB where a cross-reference section is named...'
  - D xref.hybrid-stream-lost @426 '...names offset 426, what its /Prev names too...'
  - obj5="five", obj7="new seven".
  - F xref.hybrid-stream-wrong [Error] '...names offset 426, the section its /Prev names too, which the chain read through that /Prev...'
- Head, 1 MB: /XRefStm Read Stream off=426 pad=65537; Warnings only (file.size-wrong, xref.offset-imprecise).
- Run 65,536 at default: the same as 1 MB.
- Main, default:
  - /XRefStm Relocated Table off=230; limit.trailer 'The dictionary of the object read as a cross-reference stream runs past 64 KB'.
  - xref.offset-adjusted.
  - obj5=PdfNull, obj7="old seven".
  - F xref.section-shifted [Warning]; no Error.

**(B2)** A separate hybrid stream 9 past the run; the /Prev names stream 4, whose own /Prev is T0. t0=1304, /XRefStm 1450 (T0's end of line), run 70,000 (65,536 gives the same).
- Head, default:
  - /XRefStm Relocated Table off=1304; limit.trailer @1450; xref.offset-adjusted.
  - D xref.hybrid-stream-lost '...a cross-reference table, whose rows were read in the stream's place'.
  - D xref.chain-cycle @1304 'the /Prev of the section at offset 72566 names offset 1304, which the chain has already read'.
  - obj5="five", obj7="new seven".
  - F xref.section-shifted [Warning], F xref.chain-loop [Error], F xref.hybrid-stream-wrong [Error].
- Head, 1 MB: /XRefStm Read Stream pad=70001; Warnings only.
- Main, default: Relocated Table 1304, then T0 read again through the /Prev; obj5=PdfNull, obj7="old seven"; Warnings only (section-shifted, file.size-wrong x3).

**The finding's (B) layout** (/XRefStm 1427, /Prev 11428 = stream 4 - 60,000).
- Head, default: chain-loop [Error] + hybrid-stream-wrong [Error], obj7="old seven".
- Head, 1 MB: /XRefStm NamedAgain pad=70001, hybrid-stream-lost, F xref.hybrid-stream-wrong [Error] (branch (c) by positions), not 'no hybrid report'.

**(C)** /Prev = /XRefStm = 426.
- Head, default: /Prev Relocated 230, hybrid-stream-lost, F section-shifted [Warning] + F hybrid-stream-wrong [Error].
- Head, 1 MB: F hybrid-stream-wrong [Error] too.
- Main: F xref.chain-loop [Error] at default and at 1 MB.

The guard block is PdfFileReader.cs:5097-5112. The wording is HybridStreamWrongRule.cs:46-52, 'the section its /Prev names too, which the chain read through that /Prev'. The 'past the limit that cut it there' wording applies only where previous.CutByLimit.
## VERDICT fix
No code change in batch 19: the root is #289, placed in batch 27.

Add to the #289 comment already planned for the batch's end what batch 19 does with an /XRefStm whose white space reached the guard:
- (a) Its nearby search stops at the place the table's /Prev keeps. That gives xref.hybrid-stream-lost and the xref.hybrid-stream-wrong Error, whose text says the /XRefStm names what its /Prev names. Shape (A).
- (b) A table found nearby is read in the stream's place. A later /Prev that names that table then gives xref.chain-cycle and an xref.chain-loop Error, beside the hybrid-stream-wrong Error. Shape (B2), with #330.
- (c) Main gave only a section-shifted Warning there, but served object 5 as null and object 7's old revision. Head serves the right objects.

Extend #289's close criteria: HybridStreamWrongRule, ChainLoopRule and the reader's xref.hybrid-stream-lost stand down when the /XRefStm's own white space reached the guard, with (A) and (B2) as test rows. Either of #289's designs (white space not counted, or CutByLimit with no relocation) gives this.

Correct the decisions.md triage line: 'behavior as main' holds for startxref and /Prev, not for an /XRefStm.

Drop the (C) consequence: that Error is the file's, under any bound.

######## [2] lens=window key=ws-guard-message-says-what-follows-unread sev=minor -> narrowed/nit maint=False
## location
src/AdCodicem.Pdf/IO/PdfFileReader.cs:5107 (message) and :5190 (padding = first.Start > bound ? -1 : first.Start); src/AdCodicem.Pdf/Documents/PdfReaderLimits.cs:147-150; docs/website/docs/concepts/reader-limits.md:91
## claim
f0d90c8 added the guard text 'what follows them was not read. Raise PdfReaderLimits.MaxTrailerLength to read past it.' The MaxTrailerLength remark says 'what follows them is not read', and the concepts page says 'nothing past them is read'. All three are false in two shapes.
(a) With MaxTrailerLength below about 512, the search near the offset finds the section right past the run and reads it.
(b) Where the run ends the file, nothing follows it: the guard is reported and the advice to raise it changes nothing.
The behavior is #289's and matches main and b4ab948; only the text is new. In (b), PaddingAt takes a probe the file's end shortened, holding only the end of input past the bound, for the guard's.
## evidence
(a) ZzPrevEdge, MaxTrailerLength 256: a /Prev names the first of 257 spaces before the older table.
- Head:
  - D limit.trailer @252 'White space and comments run past 256 bytes where a cross-reference section is named; what follows them was not read. Raise PdfReaderLimits.MaxTrailerLength to read past it.'
  - D xref.offset-adjusted @509 'The cross-reference section /Prev names at offset 252 was found 257 bytes after it.'
  - S /Prev Relocated Table off=509
  - obj5=five, obj7=new seven: the section past the run was read.
- A stream gives the same, and runs of 289 and 300 give the same.
- b4ab948 and main: the same codes, with the 'The dictionary of the object read as a cross-reference stream runs past 256 bytes' text.

(b) ZzRunEndsTheFile, MaxTrailerLength 256: a /Prev names the first of 257 to 356 spaces that end the file.
- Head: limit.trailer @965 with the same text, xref.section-missing, and F xref.section-not-found [Error].
- Raised to 1 MB: section-missing and section-not-found only.
## fix
Word the message after what the reader did, for example: 'White space and comments run past {bound} where a cross-reference section is named; the reader did not cross them there.' Restate the PdfReaderLimits remark and concepts/reader-limits.md:91 to match: the section is not read from that offset, and is looked for near it, where the search may read it.
In PaddingAt, treat a probe the file's end shortened whose first token is the end of input as white space that ends the file, not the guard's: padding = first.Start when first.Kind == EndOfInput && probe.Length < probeLength. The outcome then equals the raised read.
Add a row for each shape.
## VERDICT reason
Part (a) holds, but only in a narrow case. The texts say nothing past the run of white space is read. When MaxTrailerLength is below about 512 bytes (the NearbySearchRadius), the search near the offset finds the section just past the run and reads it.

Five texts make this claim:
- the message at PdfFileReader.cs:5107;
- the PdfReaderLimits.cs:147-150 remark;
- concepts/reader-limits.md:91;
- concepts/lazy-reading.md:179 (not in the finding);
- reference/diagnostics.md:141 (not in the finding).

In that case the report contradicts itself. The next entry, xref.offset-adjusted, says the section was "found 257 bytes after it".

At the default 64 KB the texts hold: a run longer than 64 KB puts the section outside the 512-byte radius. So the misstatement needs a caller-lowered limit below the size of real trailers. No wrong object is served, and the advice to raise the limit does change the outcome (read in place with offset-imprecise, instead of Relocated with section-shifted). That puts it with round 4's overclaiming-text findings ([14], [26]), which were nits.

Part (b) is refuted. Where the run ends the file, nothing follows it, so "what follows them was not read" is not false. "Raise ... to read past it" is the tail PdfLimitGuard puts on every guard. Reporting limit.trailer there is #289's shape, unchanged from main and b4ab948 (same code at the same position, only main's wording differs). From bound+32 up (XRefProbeLength being 32), the reader cannot know the run ends the file without reading past the guard. Only in the 31-byte band above the bound does the probe see the end of the file. Skipping the guard there is a behavior change in #289's space, which #289's own closing (white space not counted toward the bound) removes. It is not a text that misstates.
## VERDICT evidence
Reproduced at 8ba4589 with an uncommitted probe, ZzWsGuardMsgTests (copy at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/ZzWsGuardMsgTests.cs.txt). Outputs are zz-wsg-head.txt, zz-wsg-b4.txt and zz-wsg-main.txt in the same lot19 folder.

(a) Two classic tables. The newer table's /Prev names offset 159, the first of a run of spaces before the older table. MaxTrailerLength is 256.

Head, run 257:
- D limit.trailer @159 'White space and comments run past 256 bytes where a cross-reference section is named; what follows them was not read. Raise PdfReaderLimits.MaxTrailerLength to read past it.'
- D xref.offset-adjusted @416 'The cross-reference section /Prev names at offset 159 was found 257 bytes after it.'
- S /Prev Relocated Table off=416 named=159
- obj5="five", obj7="new seven"
- F xref.section-shifted [Warning]

Runs of 289, 300, 400 and 480 give the same, and so does an older cross-reference stream (Relocated Stream).

Raised to 1 MB: S /Prev Read Table off=159, F xref.offset-imprecise, no diagnostic.

Run 600, which lies beyond the radius: limit.trailer, xref.section-missing, xref.rebuilt, F xref.section-not-found [Error]. Here the text holds. Main relocated that one only because its window was clamped at the file's start, which #197 removed.

b4ab948 and main give the same codes for runs 257 to 480, with the 'The dictionary of the object read as a cross-reference stream runs past 256 bytes; only what lies within it was read.' text. Main also says 'from there' where head says 'after it' (#241).

(b) A single table whose /Prev names the first of N spaces that end the file, with MaxTrailerLength 256:
- Head, N = 257, 270, 287, 288, 289, 300, 356 and 400: limit.trailer @395 (same text), xref.section-missing, F xref.section-not-found [Error].
- Raised: section-missing only.
- b4ab948: identical codes, the old text.
- Main: limit.trailer @395 too, then #188's relocation onto the newer table with chain-cycle.

The guard report is therefore unchanged since main.
## VERDICT fix
Make the texts say that the section is not read from the offset, not that nothing past the run is read.

- The message (PdfFileReader.cs:5107): for example, '...where a cross-reference section is named; the section was not read from there.'
- The PdfReaderLimits.cs:148 remark, reader-limits.md:91, lazy-reading.md:179 and diagnostics.md:141: 'the section is not read from that offset, and is looked for nearby as one that is not there, where a search may find it past them'.
- Tighten the code comment at PdfFileReader.cs:5102-5104 and the PaddingAt remark at :5144 the same way.
- Update the pinned message in CrossReferenceRuleTests.cs:2600.
- Add a CrossReferenceChainTests row for the behavior the text now describes: MaxTrailerLength 256, a /Prev naming the first of 257 spaces before the older table, giving limit.trailer at the run's start, then xref.offset-adjusted and a Relocated section, with objects 5 and 7 read.

No change for (b). If a probe that the file's end shortened should stop counting as the guard's, that belongs with #289 when batch 27 pays it.

######## [3] lens=window key=probe-cost-remark-overclaims sev=nit -> narrowed/nit maint=False
## location
src/AdCodicem.Pdf/IO/PdfFileReader.cs:5145-5146 (PaddingAt remark); docs/threat-model.md:463 ('about nine times the run it crosses', which predates the batch)
## claim
f0d90c8 changed the PaddingAt remark to 'the bytes read come to less than ten times the run crossed, or one probe where there is none'. That is false. The probe grows for a first token the edge cuts just as it does for white space, so a long first token with no white space before it reads about 84 KB. A short run before a keyword the first probe cuts reads more than ten times the run. The wording it replaced ('less than a seventh more than the last window holds') did not hold under the clamp either: 18,720 bytes come before a last window of 65,568.
## evidence
ZzProbeReadsAtANamedOffset: startxref names a 70,000-byte string, or name, with no white space before it.
- Reads at the named offset, head: [32, 256, 2048, 16384, 65568, 65536]. That is 84,288 bytes of probe for a run of 0.
- b4ab948: [32, 256, 2048, 16384, 65536, 65536].
- 28 spaces and then a keyword cut at byte 32 read 32 + 256 bytes when the file goes on, 10.3 times the run.
## fix
State the bound that holds: the probes total less than about 2.15 times their reach (a trailer's window and the 32 bytes past it), and a first token the edge cuts grows them as white space does. Qualify threat-model.md:463 the same way: 'the run it crosses, or a first token the probe's edge cuts'.
## VERDICT reason
The finding holds for the PaddingAt remark, which f0d90c8 wrote in this batch. Its last sentence reads "the bytes read come to less than ten times the run crossed, or one probe where there is none". In that remark, "run" means the white space and comments: the sentence before it says "White space and comments that fill the window and run on past it". The parallel remark at PdfFileReader.cs:109 states its bound over "the bytes it crossed up to its decision — white space, comments, the header's tokens —". PaddingAt's remark leaves the first token out, and the code (:5179) grows the probe for a first token the edge cuts exactly as it does for white space. A long first token at the named offset, or a short run before a token that reaches the first probe's edge, therefore reads far more than ten times the run.

The bound only holds if "run crossed" is read as including the first token up to the probe's reach. Then the total is at most (64/7) times the window outgrown, so under 9.15 times. The wording does not say that.

The wording it replaced at b4ab948 ("less than a seventh more than the last window holds") was also false under the clamp. The code at b4ab948 grew 32, 256, 2048, 16384, then 65,536, and 18,720 is more than 65,536/7. Both are batch text, since PaddingAt did not exist on main.

Narrowed: docs/threat-model.md:463 ("about nine times the run it crosses") is main's wording. Main's probe had the same growth and read 32+256+2048+16384+65,536 = 84,256 bytes for the same long token. The batch reworded the sentence around the claim but did not make it worse, so that line is not this batch's defect. It can be qualified in the same change if wanted.

Nit, matching the precedent for remark overclaims (round 4 [14]): it is a private remark, and the cost stays bounded at under 15/7 of the probe's reach (MaxTrailerLength + 32), at most once per section and remembered. The remark itself admits the cut-token growth one sentence earlier.
## VERDICT evidence
Reproduced on 8ba4589 with a throwaway probe, ZzProbeReadsAtANamedOffset, since removed. It used a WindowLoggingSource-style source that records each read's requested length. The file is HandWrittenFile with a catalog and a pages object, then the shape at the named offset 110, then "startxref 110". Reads at offset 110:
- 70,000-byte string, run 0: [32, 256, 2048, 16384, 65568, 65536]. The probe is 84,288 bytes for a run of 0 (limit.trailer | xref.rebuilt | trailer.root-recovered).
- 70,000-byte name, run 0: same reads.
- 70,000-byte string, run 1: [32, 256, 2048, 16384, 65568], 84,288 bytes for a run of 1.
- 28 spaces, then the xref table, with the file going on (40,000-byte comment after the trailer): [32, 256, 40153]. The probe is 288 bytes, 10.29 times the run.
- 12 spaces, then a 20-digit header number '00000000000000000003 0 obj', with the file going on: [32, 256]. That is 288 bytes, 24 times the run.
- For contrast, 2044 spaces then xref: [32, 256, 2048, 16384, 42169], a probe of 18,720 bytes, 9.16 times the run. The bound holds only for white space before a short token.
- In a short file (no tail), the 28-space row reads [32, 151, 151], because the source clamps to the file's end. The 10.3 times appears only when the file goes on, as the finding says.

Code: PdfFileReader.cs:5179 `if (first.End >= probe.Length && probe.Length == probeLength && probeLength < reach)` grows the probe whatever first is. :5171 `reach = bound + XRefProbeLength`.

b4ab948 (from the f0d90c8 diff, not run): `Math.Min(_guard.Bound(PdfLimit.Trailer), probeLength * 8)` gives 32, 256, 2048, 16384, 65,536. Main 21a0298 (:3404-3407) grows the same way and its threat-model row 450 reads "about nine times the run it crosses".
## VERDICT fix
Reword PdfFileReader.cs:5144-5146 to state the bound that holds. For example: "The windows grow eightfold from XRefProbeLength bytes, so that the bytes read come to less than ten times those the probe crossed to its first token's end — white space, comments and that token alike, a token its edge cuts as far as the probe reaches —, one probe at least, and less than 15/7 of its reach." Optionally, qualify docs/threat-model.md:463 the same way ("about nine times the run it crosses, or a first token the probe's edge cuts, up to about twice `MaxTrailerLength`"). That wording is main's, so it is not required. No code change, no maintainer decision.

######## [4] lens=line key=line-docs-line-start-off-by-one sev=nit -> narrowed/nit maint=False
## location
docs/threat-model.md:424; docs/website/docs/reference/diagnostics.md:110; docs/website/docs/reference/validation-rules.md:56; docs/website/docs/concepts/lazy-reading.md:192; test name at tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:1215
## claim
Four pages describe the bound the same way: the line is read 'as far as 4,096 bytes before the bytes searched, a place whose line starts farther back declaring nothing'. The code checks something else. IsTokenOnItsLine looks for the END OF LINE before the place inside those 4,096 bytes (`bytes[..before].LastIndexOfAny(...)`, then `endOfLine >= 0 || from == 0`). So a place whose line starts exactly 4,096 bytes before the bytes searched declares nothing, even though that line does not start 'farther back': its end of line sits at from-1, 4,097 bytes back. The only line read from that position is one starting at the file's first byte. The test that holds this boundary has the same flaw in its name, '..._only_where_its_line_starts_within_reach_of_the_bytes_searched': in its farther=1 row the line starts at the first byte read back, so it is within reach, and the place is still dropped. The code's own texts state the rule correctly: the LineStartSearchLength summary ('the end of line that starts a place's line is looked for') and the IsTokenOnItsLine remark.
## evidence
Probe ZzLineR5Files/ZzLineR5Sweep (saved in scratchpad/r5line/probes), whole reads at 8ba4589. Each file: a 1,200-byte comment, then '4 0 obj [' + N spaces + '] endobj xref 0 1 bad', with the newest /Prev naming the place 50 bytes before the keyword.
- d-exact4096-lf (end of line at from, 4,096 bytes before the bytes searched): '[startxref Read 7308] [/Prev Malformed 6093] | Repair xref.offset-adjusted at 6093: ... found 50 bytes after it. | Warning xref.section-unreadable at 6093'.
- d-exact4097-lf (end of line at from-1, so the line starts at from, exactly 4,096 bytes before the bytes searched): '[startxref Read 7309] [/Prev NotFound 6044] | Warning xref.section-missing at 6044'.
- d-crlf-lf-before-from (CR at from-2, LF at from-1): '[/Prev NotFound 6045] xref.section-missing'.
The whole-read descriptions at b4ab948 are identical. The existing row An_xref_keyword_on_a_long_line_...(farther: 1) asserts NotFound, and its own comment says 'where that end of line lies within 4,096 bytes of them'.
## fix
Restate the four pages as: 'its line read again where the end of line before it lies within 4,096 bytes before the bytes searched; a place whose end of line lies farther back declares nothing, unless its line starts at the file's first byte'. Rename the test to '..._only_where_the_end_of_line_before_it_lies_within_reach_of_the_bytes_searched' and update the citation in threat-model.md:424 to match.
## VERDICT reason
The general claim does not hold. It measures "the bytes searched" from the byte before the window. The docs measure from the window itself: 512 bytes either side of the offset (validation-rules.md:56, lazy-reading.md:174), with the byte before it read on the side (threat-model.md:511 "the byte before the window read so that a number its start cuts is no header", and the test name A_window_that_starts_on_the_file_s_second_byte_reads_its_first_too). With that window, the code applies exactly the docs' rule to every line after an end of line. A line that starts up to 4,096 bytes before the first byte searched (offset - 512) is found, and one that starts 4,097 bytes or more before it is dropped. The read-back reaches 4,096 bytes before the byte before the window. That is enough to see the end of line before a line that starts 4,096 bytes before the window.

The finding's case "line starts exactly 4,096 bytes before the bytes searched, dropped" is, in the docs' terms, a line that starts 4,097 bytes back: "farther back", so declaring nothing is correct. The test name holds for the same reason. In its farther=1 row the line starts 4,097 bytes before offset - 512, so outside the 4,096 bytes. Its comment counts the end of line from the byte before the window, as the code's own texts do, and the two statements are equivalent.

The proposed fix would introduce an off-by-one in the docs' terms. In my 4,096-space row the end of line lies 4,097 bytes before the window and the line is found, but the new text would say it declares nothing.

One case survives, and the finding did not claim it. The file's first line has no end of line before it. The code accepts it wherever the file's start lies inside the bytes read back (from == 0), which is one byte farther than the rule. So when the window starts exactly 4,097 bytes into the file (offset named 4,609, first line unbroken past it), a keyword on that line is a section found moved. Its line starts 4,097 bytes before the bytes searched, and the four pages say such a place declares nothing. This is one geometry at the file's start, behaves the same at b4ab948, and affects nothing else.
## VERDICT evidence
Probe ZzRefLineStartProbe (saved in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r5ref-ZzRefLineStartProbe.cs.txt), whole reads at 8ba4589 (origin/ccr-6d6c0ee6-dqm4lm-188). The file is the same as the test An_xref_keyword_on_a_long_line_... (1,200-byte comment, '4 0 obj [' + spaces + '] endobj xref 0 1 bad', newest /Prev 50 bytes before the keyword). windowFirst is named - NearbySearchRadius.

Mid-file line (its end of line at 1434):
- lineStartBeforeWindowFirst=4094: Malformed 6091, xref.offset-adjusted xref.section-unreadable
- lineStartBeforeWindowFirst=4095: Malformed 6092, same codes
- lineStartBeforeWindowFirst=4096 (lineStartBeforeSpanStart=4095, end of line 4,096 before the byte before the window): Malformed 6093, same codes
- lineStartBeforeWindowFirst=4097 (lineStartBeforeSpanStart=4096): NotFound 6044, xref.section-missing
- lineStartBeforeWindowFirst=4098: NotFound 6045, xref.section-missing

So a line that starts exactly 4,096 bytes before the bytes searched, as the docs define them, is found. The finding's 'd-exact4097-lf' is the 4097 row.

File's first line, no header (FILESTART):
- windowFirst=4095: Malformed 4657
- windowFirst=4096: Malformed 4658
- windowFirst=4097 (line starts 4,097 before the window): Malformed 4659, header.missing xref.offset-adjusted xref.section-unreadable. The docs' rule would say NotFound.
- windowFirst=4098: NotFound 4610, xref.section-missing
- windowFirst=4099: NotFound 4611

Code: PdfFileReader.cs:4970-4980. from = spanStart - 4096, where spanStart is the byte before the window. The end of line is looked for in bytes[..before]. The condition is (endOfLine >= 0 || from == 0).
## VERDICT fix
Do not apply the proposed rewording, and do not rename the test. Both are right in the vocabulary the pages use, and the rewording would make them off by one.

For the surviving file-start case, I recommend leaving the text as it is. It is one offset, the file's first line, and it reads a section rather than dropping one.

If exactness is wanted, either:
(a) Change `from == 0` to `spanStart < LineStartSearchLength` in IsTokenOnItsLine. The file's first line then follows the same 4,096-byte bound, and the remark's "the file starts farther back" should be reworded to the end-of-line/file-start counting. The pages then hold exactly, but this needs a test row for windowFirst=4097 and a mutant run.
(b) Add to the four pages a clause such as "the file's first line read where it starts one byte farther".

This does not need the maintainer.

######## [5] lens=line key=line-remark-lexing-cost-and-short-window-comment sev=nit -> narrowed/nit maint=False
## location
src/AdCodicem.Pdf/IO/PdfFileReader.cs:4951-4954 (IsTokenOnItsLine remark) and :4968-4969 (comment above the read-back)
## claim
(a) The remark says 'The bytes lexed are those before the place on its line, once for each place that cannot be read'. That understates what StartsToken does. It lexes up to the end of the first token that ends past the place. For a place inside a comment, PdfLexer.Read's SkipWhitespaceAndComments skips the rest of that comment, then every white-space byte and comment line after it, and returns the next token, which may be several lines on. For a place inside a string, lexing runs to the string's close. The cost stays bounded, by the bytes read: at most LineStartSearchLength plus the 1,025 bytes searched. But the text names the wrong bound.
(b) e008c20's comment says 'Bytes given that end at or before the place hold neither the end of line looked for, whole, nor the place'. That is false whenever the bytes given end between the start of the bytes searched and the place, as a 4,096-byte read does (exactly `before`). Such bytes do hold the whole stretch where the end of line is looked for. What they lack is only the place's first byte, which is what the guard tests.
## evidence
(a) ZzLex probe: PdfLexer from the line's start, with StartsToken's loop.
- '%'+700 a+' xref see the table below\n6 0 obj': 'place 702, its line ends at 726; token Integer [727, 728) after 1 read(s): bytes lexed 728, of which past the place 26'.
- The same with blank lines and a 2,000-byte comment line after it: 'place 702, its line ends at 710; token Integer [2740, 2741) after 1 read(s): bytes lexed 2741, of which past the place 2039'.
- A string closing 3,000 bytes on: 'token LiteralString [0, 3728) ... of which past the place 3026'.
(b) ZzReadBackLengths (the read-back read alone given every length 0..5121) on c-keyword-long-lf: 'given 0: OTHER [/Prev NotFound 6001] / xref.section-missing', then 'given 4660 (= before +564 past spanStart): WHOLE'. Lengths 4096..4659 hold bytes[..before] whole and are still dropped, because they stop short of the place's first byte (before + at = 4659).
## fix
(a) 'The bytes lexed run from the line's start to the end of the first token past the place — through the comment that holds it and the comments and white space after it, or to the end of the string that holds it —, within the bytes read: at most LineStartSearchLength bytes and the bytes searched, once for each place that cannot be read, which MaxRelocationCandidates bounds, and one window more at most for each.'
(b) 'Bytes given that end at or before the place do not hold its first byte, whatever they hold of the end of line looked for: it declares nothing, as past the bound.'
## VERDICT reason
Both halves are true, and both are wording only. The behavior is right, and the cost stays bounded at about 5 KB a place.

(a) The remark at src/AdCodicem.Pdf/IO/PdfFileReader.cs:4951-4954 says "The bytes lexed are those before the place on its line". StartsToken (:4989-5001) does not stop there. It keeps calling PdfLexer.Read until a token ends past the place. PdfLexer.Read (PdfLexer.cs:58-60, :32-55) first skips white space and comments. So for a place inside a comment, the lexer runs to the end of that comment, through every blank line and comment line after it, and on to the next token. For a place inside a string, it runs to the string's close. Either way it stops at the end of the bytes it was given at the latest.

The narrowing: in the real code those bytes are the window searched, at most 1,025 bytes, or that window plus the bytes read back before it (`before` + span.Length, at most 5,121). So the bytes lexed past the place are fewer than 1,025. The reviewer's 2,039 bytes past the place came from a standalone lexer over a longer buffer and cannot happen here. The bound the finding proposes, LineStartSearchLength plus the bytes searched for each place, is the right one.

Round 4's [5] accepted the cost of reading the line back once per place, on the grounds that "the remark says so". That decision covered the bytes read, not this sentence about the bytes lexed. Round 4's refuter even noted that "a comment is skipped to the end of the input", and the sentence was never corrected. So this is not a re-hunt of a settled finding: the text misstates the code now. It is a private remark, and no doc carries it: threat-model.md row 424 states no figure for the bytes lexed.

(b) e008c20's comment at :4968-4969 says that bytes given that end at or before the place "hold neither the end of line looked for, whole, nor the place". The guard is `bytes.Length <= before + at` (:4975). Any length from `before` up to `before + at` holds bytes[..before] whole, which is where the end of line is looked for. Since lineStart == 0 on this path, span[..at] has no end of line. Such bytes lack only the place's first byte. The "neither ... nor" is therefore false over that range.

Only this inline comment is wrong. The XML remark's sentence ("a token starts at the place only where its first byte is given, which, with the bytes before it, tells whether one does") and the commit body (which treats the two cases separately) are both accurate. The behavior itself is right: without the place's first byte, nothing tells whether a token starts there.

Neither half is a defect in behavior, cost or reports, and neither is a design choice for the maintainer.
## VERDICT evidence
The branch was checked out at 8ba4589, built Release (0 warnings), and probed with ZzLexCostRefuteTests. The probe runs StartsToken's loop, copied verbatim, over a 1,025-byte span, the size of the window searched. The probe is saved at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r5-refute-lexcost/ZzLexCostRefuteTests.cs.txt and its output at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r5-refute-lexcost/out.txt. The probe file has been removed from the worktree.

Output:
- comment, then blank lines and a 650-byte comment line: "span 1025, place 302, its line ends at 326; token Integer [981, 982) after 1 read(s): bytes lexed 982, past the place 680, starts at place False"
- comment, then white space to the window's end: "place 302, its line ends at 310; token EndOfInput [1025, 1025) ...: bytes lexed 1025, past the place 723"
- a string closing 800 bytes on: "place 102 ...; token LiteralString [0, 908) ...: bytes lexed 908, past the place 806"
- control, the keyword on its own line: "token Keyword [7, 11) ...: bytes lexed 4, past the place 4, starts at place True"

So the lexing runs to the window's end at most, never past the bytes given.

(b) is shown by the code itself. The read-back is `using var window = _source.GetWindow(from, before + span.Length); ... if (bytes.Length <= before + at) return false; var endOfLine = bytes[..before].LastIndexOfAny(...)` (PdfFileReader.cs:4970-4980). Any length in [before, before + at] holds bytes[..before], where the end of line is looked for, whole, and is still dropped.

decisions.md, round 4 triage: "[5] nit accepted: the per-place read-back is the cost round 3's [1] named, within MaxRelocationCandidates (remark says so). No change." That covers the bytes read, not this sentence about the bytes lexed. grep shows no doc page that repeats the "bytes lexed" claim.
## VERDICT fix
(a) At PdfFileReader.cs:4951-4954, replace the last sentence of IsTokenOnItsLine's remark: "The bytes lexed run from the line's start to the end of the first token past the place — through the comment that holds it and the white space and comments after it, or to the end of the string that holds it —, within the bytes searched and those read back before them: at most LineStartSearchLength bytes more than the bytes searched, once for each place that cannot be read, which MaxRelocationCandidates bounds, and one window more read at most for each."

(b) At :4968-4969, replace the second sentence of the comment: "Bytes given that end at or before the place do not hold its first byte, whatever they hold of the stretch the end of line is looked for in: it declares nothing, as past the bound."

The change is to comments only: no test, no mutant, no maintainer decision.

######## [6] lens=line key=line-test-texts-row-endings sev=nit -> narrowed/nit maint=False
## location
tests/AdCodicem.Pdf.Tests/NearbyFiles.cs:681 (KeywordPastALongComment summary); tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:1293-1294 (comment of An_xref_keyword_past_a_comment_a_line_feed_or_a_lone_carriage_return_ends_...)
## claim
Two test texts misstate how the file's table rows end.
- The builder's summary says 'The first table's rows end with a carriage return and a line feed whatever eol is'. HandWrittenFile.Table ends rows with ' \n' (a space and a line feed) when eol is "\n", and with "\r\n" otherwise. That applies to both tables, not only the first.
- The theory's comment says the eol 'ends every line of the file but the first table's rows'. In the CR rows, the newest table's rows also end with "\r\n".
Neither error changes what the tests hold. The newest table lies after the place, and the first table's LF lies more than 4,096 bytes back, so 43bee9b's two rows still each kill their '\r'-dropped mutant. Only the texts misdescribe the file.
## evidence
Row endings, read with Python from the files the builder writes:
- c-keyword-long-lf: [b'f \n', b'n \n', b'n \n', b'f \n', b'n \n', b'n \n']
- c-keyword-long-cr: [b'f\r\n', b'n\r\n', b'n\r\n', b'f\r\n', b'n\r\n', b'n\r\n']
Each list holds the three rows of each table. HandWrittenFile.cs:52: `var eol = _eol == "\n" ? " \n" : "\r\n";`.
## fix
Summary: 'Both tables' rows end with a space and a line feed where eol is a line feed, and with a carriage return and a line feed otherwise.'
Test comment: '... as it ends every line of the file but the two tables' rows ...'.
## VERDICT reason
The finding holds for both texts, with one narrowing. Both texts came from round 4's fixes: the builder summary from e008c20, the theory comment from 43bee9b. Both commits are in b4ab948..8ba4589.

HandWrittenFile.Table (HandWrittenFile.cs:52, `var eol = _eol == "\n" ? " \n" : "\r\n";`) ends every row of every table it writes. Rows end with a space and a line feed when eol is "\n", and with "\r\n" otherwise.

1. NearbyFiles.cs:681 says "The first table's rows end with a carriage return and a line feed whatever eol is". This is false for eol "\n", where the rows end with a space and a line feed. It also leaves out the newest table, whose rows end the same way.

2. CrossReferenceChainTests.cs:1293-1294 says the comment's end of line "ends every line of the file but the first table's rows".
- In the "\r" rows, the newest table's rows are also exceptions, since they end with "\r\n".
- In the "\n" rows, nothing is an exception: " \n" still ends in a line feed. The proposed "but the two tables' rows" would therefore still be wrong for the "\n" rows. This is the narrowing.

Neither text changes what the tests hold. The fact the reasoning needs is still true: the first table's rows hold a line feed whatever eol is, and that line feed lies more than 4,096 bytes before the bytes searched. The comment's end of line is the only one in that span for the long line, and there is none for the short line. So 43bee9b's CR rows still separate the '\r'-dropped mutants. This is a wording slip in test texts with no effect on behavior or coverage, so it is a nit, not minor.
## VERDICT evidence
git checkout --detach origin/ccr-6d6c0ee6-dqm4lm-188 (8ba4589).

HandWrittenFile.cs:52: `var eol = _eol == "\n" ? " \n" : "\r\n";`. Every row of Table is appended with that eol.

`git log -S` attributes the summary sentence to e008c20 and the comment to 43bee9b, both in b4ab948..8ba4589.

I rebuilt KeywordPastALongComment line for line in Python (/tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r5-rows/b.py), using the four InlineData cases of the theory and NearbySearchRadius = 512 (PdfFileReader.cs:85):
- eol '\n', long line (830 bytes): row endings [b' \n'] x6; ends of line in [searched-4096, searched): 1; first table's last end of line 5257 bytes before searched
- eol '\n', short line (19 bytes): [b' \n'] x6; ends of line in span: 0; first table's last end of line 4446 bytes before
- eol '\r', long line: [b'\r\n'] x6; ends of line in span: 1; 5257 bytes before
- eol '\r', short line: [b'\r\n'] x6; ends of line in span: 0; 4446 bytes before

So both tables' rows end in " \n" or "\r\n", never only the first table's. The first table's line feed is always more than 4,096 bytes before the bytes searched, so the tests still hold their claim.
## VERDICT fix
- NearbyFiles.cs:681: take the finding's wording: "Both tables' rows end with a space and a line feed where eol is a line feed, and with a carriage return and a line feed otherwise."
- CrossReferenceChainTests.cs:1293-1294: the proposed "but the two tables' rows" is still wrong for the "\n" rows. Write instead: "... which a line feed or a lone carriage return ends, as it ends every line of the file but, where it is a carriage return, the tables' rows, which a carriage return and a line feed end, ..."

This is a test-text-only change with no design choice, and it can ride with the next fix commit.

######## [7] lens=sections key=inpart-report-understates-every-number-refusal sev=minor -> confirmed/minor maint=False
## location
src/AdCodicem.Pdf/IO/PdfFileReader.cs:4547-4563 (the stream arm of ReportSectionReadInPart, `Lacking`) and :4566-4577 (the table arm), reported at :3388 and :3599, before RefuseRowsLost at :3416 and :3603; RefuseDeclared :4111; docs/website/docs/reference/diagnostics.md:112 (the in-part list)
## claim
A section read in part is reported before the numbers it lost are refused. Its message only foresees the stream's declared ranges, or the table's own `_rowsLostPast`. Two shapes make the refusal that follows refuse every number. (a) A stream whose /Index holds a reference, which RefuseDeclared cannot keep apart: `HoldsReference(index)` sends it to RefuseEveryNumber. (b) A table, or a stream, whose lost range passes MaxRefusedRanges once it is merged with the ranges a newer lost stream left. In both, every object that only older sections index is refused and rebuilt, at opening when the catalog is among them. The message still says only that "the objects whose rows it lacks", or "the objects the rest of that subsection numbers", are looked for by rebuilding the index. ReportLostSection runs after its refusal and says "every object no section read before it indexes" for the same causes, so the in-part report is the one that understates. The defect belongs to the batch: the refusal and the report came with #210 and B19-2/B19-3, and b4ab948 is identical. Main refuses nothing here and reports nothing. Only an invalid file reaches it, and the objects served are right. Only the message misstates what is rebuilt.
## evidence
Probe ZzR5SecProbe.ZzEveryInPart (/tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r5sec/ZzR5SecProbe.cs.txt; outputs everyinpart-head.txt, everyinpart-b4a.txt and everyinpart-main.txt in the same folder). The file is: newest table → /Prev → stream 6 (rows for 4 and 6 only) → /Prev → oldest table, which indexes 1, 2 and 3. The newest table places object 9 = 2.
E1 (control, stream /Index [4 1 6 2] direct), head:
- `Warning xref.section-unreadable @299: The cross-reference stream /Prev names at offset 299 was read only in part: it holds 2 rows where its /Index and /Size declare 3; the rows it holds were kept, and the objects whose rows it lacks are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows.`
- `idx 1=Regular`, `idx 3=Regular`, `repaired-before-load=False`, `obj 3=three as the oldest has it repaired=False`.
E2 (/Index [4 1 6 9 0 R]), head:
- The same message, word for word.
- `idx 1=none`, `idx 3=none`, `repaired-before-load=True` (xref.rebuilt at opening).
- Oldest table record: `rrb=True enrb=True`.
E4 (control: a lost stream declaring 4,095 one-number ranges from 1000, then a table `3 2` whose row for 4 ends in x), head:
- `xref.section-unreadable @367: ... the row for object 4, at offset 396, is not an offset, a generation and n or f; the rows read were kept, and the objects the rest of that subsection numbers are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows.`
- `idx 1=Regular`, `repaired-before-load=False`.
E5 (4,096 ranges, so the table's one range passes the bound once merged), head:
- The same message.
- `idx 1=none`, `repaired-before-load=True`, oldest `enrb=True`.
The hybrid path does the same. V10 (every-head.txt): the /XRefStm reads stream /Index [4 1 6 9 0 R] in part, the message says "the objects whose rows it lacks", and the file is rebuilt at opening.
b4ab948 gives byte-identical output apart from the ordinals. Main 21a0298 gives no xref.section-unreadable and `idx 1=Regular` in E1 to E5.
## fix
Report after refusing. At both call sites, move ReportSectionReadInPart and ReportReservedRows below `if (... Incomplete) RefuseRowsLost(...)`. No diagnostic is written between them: NamesOlderSection and the TrailersHiddenByGuard block report nothing. EveryNumberRefusedBefore is recorded at creation, which keeps the "newer section" case apart, as the comment at :3303 intends. In both Incomplete arms, when `_everyNumberRefused && !section.EveryNumberRefusedBefore`, end with the table arm's `_rowsLostPast` words: "every other object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows". Add E2 and E5 as theory rows that assert that text and `idx 1` absent. In diagnostics.md:112's in-part list, name beside "every number past 4,096 of them" the stream whose numbering is written with references. Run ADR 53's catalog over the moved calls and the new condition.
## VERDICT reason
The finding holds at 8ba4589. ReportSectionReadInPart runs before RefuseRowsLost at both call sites, :3388 then :3416 on the chain, and :3599 then :3603 on the /XRefStm path. The report reads only section.EveryNumberRefusedBefore, section.RowsRefusedBefore, _rowsLostPast and SubsectionsUnknown. It cannot know that the section's own refusal is about to refuse every number. Two shapes do that:
(a) A stream read in part whose /Index is a reference, or holds one. RefuseDeclared :4111 sends it to RefuseEveryNumber through `HoldsReference(index)`, or because an /Index written as a reference is no array.
(b) A lost range that passes MaxRefusedRanges once RefuseRanges merges it with the ranges a newer lost stream left.
In both cases the stream arm still says "the objects whose rows it lacks". The table arm still says "the objects the rest of that subsection numbers". Yet every row that only older sections hold is refused, and the file is rebuilt at opening when the catalog is among them.

The sibling reports already handle this. HandOnPrevious refuses (:4069) before ReportLostSection (:4074), and the /XRefStm lost path does too. So their texts say "every object no section read before it indexes". Tests pin those texts: A_lost_stream_whose_numbering_cannot_be_read_leaves_no_older_row_standing, and A_hybrid_file_s_stream_that_cannot_be_read_... row "/Index [4 2 7 1 0 R]". The in-part report is the only one that understates.

This is not settled anywhere. Round 2's [7][14][19] covered only a newer section's every-number refusal, now EveryNumberRefusedBefore. It kept "every other object" only where the refusal is the table's own (_rowsLostPast, or SubsectionsUnknown under /XRefStm). Neither shape here is covered by r1 to r4, decisions.md or residuals-impl.md, and no open debt holds it.

The defect belongs to the batch. Main 21a0298 has no RefuseDeclared, RefuseEveryNumber or "read only in part". b4ab948 has the same order (:3360 then :3388, :3571 then :3575).

Severity is minor. Only an invalid file reaches it, the objects served are right after the rebuild, and only the message misstates what is rebuilt.

One narrowing, of the fix. RefuseRowsLost also refuses every number for any table whose SubsectionsUnknown is set, whatever names it. Applied as written to both Incomplete arms, the reviewer's new condition would turn the deliberate tail of a table startxref or /Prev names, whose rest cannot be known, into "every other object…". That tail is "what only the rest of the table, or the sections its trailer would name, index is looked for", pinned by 4 assertions in CrossReferenceChainTests (round 2 [8]). The chain ends at such a table, so no older row is read after it anyway. That arm must keep its words.

The claim that nothing is reported between the moved calls also holds. NamesOlderSection reads through ReadWithoutLoading and TryReadUnrecorded, which records nothing.
## VERDICT evidence
I rebuilt the reviewer's probe in my own worktree on origin/ccr-6d6c0ee6-dqm4lm-188 (8ba4589; Release, 0 warnings) and added one case, F1, where /Index is a whole reference. Probe: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refinpart/ZzRefInPartProbe.cs.txt. Output: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refinpart/head.txt.

Chain shape for E1 to F1: newest table, then /Prev to stream 6, which holds rows for 4 and 6 only, then /Prev to the oldest table, which indexes 1, 2 and 3.

- E1 (/Index [4 1 6 2], direct): `xref.section-unreadable @299: ... was read only in part: it holds 2 rows where its /Index and /Size declare 3; the rows it holds were kept, and the objects whose rows it lacks are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows.` Then idx 1=Regular, idx 3=Regular, repaired-before-load=False.
- E2 (/Index [4 1 6 9 0 R], 9 -> 2): the same message, word for word. Then idx 1=none, idx 3=none, repaired-before-load=True. The oldest table has rrb=True and enrb=True.
- F1 (/Index 9 0 R, 9 -> [4 1 6 2]): the same message. Then idx 1=none, idx 3=none, repaired-before-load=True.
- E3 (control: /Size 9 0 R beside a direct /Index): idx 1=Regular. Size is consulted only without an /Index.
- E4 (4,095 lost ranges, then table `3 2` with a bad row for 4): `xref.section-unreadable @367: ... was read only in part: the row for object 4, at offset 396, is not an offset, a generation and n or f; the rows read were kept, and the objects the rest of that subsection numbers are looked for ...`. Then idx 1=Regular, repaired-before-load=False.
- E5 (4,096 ranges, so the merge passes the bound): the same message. Then idx 1=none, repaired-before-load=True, and the oldest table has enrb=True.

The reviewer's hybrid V10 output (every-head.txt) shows the /XRefStm path does the same: `/XRefStm names at offset 1632 was read only in part: ... the objects whose rows it lacks ...`, then repaired-before-load=True.

Code:
- `git show b4ab948` has ReportSectionReadInPart at :3360 before RefuseRowsLost at :3388, and :3571 before :3575, the same order as head.
- `git show 21a0298` has no RefuseDeclared, RefuseEveryNumber or "read only in part". The reviewer's main output gives idx 1=Regular and no section-unreadable in E1 to E5.
- RefuseRowsLost :4704 refuses every number for `_rowsLostPast || section.SubsectionsUnknown`, whatever names the table.
- `grep -c 'or the sections its trailer would name, index is looked for' CrossReferenceChainTests.cs` gives 4.

Docs: diagnostics.md:112's in-part list says "every number a stream declares" and nothing of numbering written with references. Its lost-stream sentence and threat-model.md:425 do say it. lazy-reading.md:162 says "a stream whose numbering cannot be read", which is loose for an in-part stream whose reference was followed to read its rows.
## VERDICT fix
Not a maintainer choice: it makes the message say what the refusal does, as B19-3's message rule asks.

1. At both call sites (:3386-3390 and :3599-3606), move `ReportSectionReadInPart` and `ReportReservedRows` below `if (section.Incomplete) RefuseRowsLost(section)` (and its stream twin). EveryNumberRefusedBefore is already recorded at creation, so the "newer section" case stays apart, as the comment at :3301 intends.
2. Stream arm: when `_everyNumberRefused` holds after the refusal (and noneStands does not), replace the `Lacking` tail with ", and every other object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows". Do the same in the RowsHeld 0 and 1 arms and in the RowsLeftToGuard arm.
3. Table arm: use the existing "every other object" tail where `_everyNumberRefused && !section.SubsectionsUnknown` (this covers `_rowsLostPast` and the merge past the bound), or `SubsectionsUnknown && naming == "/XRefStm"`. Keep the "what only the rest of the table, or the sections its trailer would name" arm for a SubsectionsUnknown table that startxref or a /Prev names, where the chain ends, so the 4 pinned assertions stay.
4. Tests: add E2, F1 and E5 as rows asserting that text and `ChainIndex.TryGet(1)` false, plus the /XRefStm path (V10's shape). Keep E1 and E4 as controls.
5. Docs: in diagnostics.md:112's in-part list, next to "every number a stream declares", add: every number where its numbering is written with references, or where the ranges refused so pass 4,096. In lazy-reading.md:162, write "cannot be read, or is written with references", as threat-model.md:425 does.
6. Run ADR 53's catalog over the moved calls and the new conditions.

######## [8] lens=sections key=read-again-keeps-every-number-refused-before sev=nit -> confirmed/nit maint=False
## location
src/AdCodicem.Pdf/IO/PdfFileReader.cs:3373-3382 (readAgain block); src/AdCodicem.Pdf/IO/XRef/XRefSectionRecord.cs:194-200 (EveryNumberRefusedBefore, init-only)
## claim
7b1d926 carries two of the first reading's refusal fields onto the /Prev's record: ReservedRowsRefused and RowsRefusedBefore. It does not carry EveryNumberRefusedBefore. The record took that field at creation from `_everyNumberRefused`, which the /XRefStm's own reading of the same stream set through RefuseRowsLost. The /Prev's record then says a section read before it refused every number, where only the stream's own first reading did. That contradicts the field's doc ("What the section refuses once it is read is its own, and does not count") and the reasoning the fix applied to RowsRefusedBefore. Nothing reads the field for a record read again: the reports are skipped and no rule reads it. RowsRefusedBefore's copy is just as invisible, yet the fix carried it and a test row pins it. So nothing a caller sees changes.
## evidence
Probe ZzR5SecProbe.ZzReadAgainEvery, output /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r5sec/every-head.txt. The shape is the readAgain one: the /XRefStm names stream 6 at its header, the /Prev 7 bytes short. The stream's /Index is [4 1 6 9 0 R], 9 → 40, so it is read in part and its own RefuseRowsLost refuses every number. Head records:
- `S /XRefStm@1632 NamedAgain ... enrb=False`
- `S /Prev@1632 Relocated Stream inc=True reserved=1 reservedRefused=0 rrb=False enrb=True ord=3`
rrb and reservedRefused were carried from the first reading; enrb was not.
## fix
Either make EveryNumberRefusedBefore settable and set `section.EveryNumberRefusedBefore = streamsPlace.EveryNumberRefusedBefore` beside the two copies, asserting it on a row like V10. Or say in the readAgain comment that the field is left as the record was created, since nothing reads it for a section read again.
## VERDICT reason
The finding holds as stated, and it is a nit. 7b1d926's readAgain block (src/AdCodicem.Pdf/IO/PdfFileReader.cs:3373-3382) copies only ReservedRowsRefused and RowsRefusedBefore from the /XRefStm's first reading onto the /Prev's record. EveryNumberRefusedBefore is init-only (src/AdCodicem.Pdf/IO/XRef/XRefSectionRecord.cs:200). The /Prev's record took it when it was created (:3308), from `_everyNumberRefused`. The stream's own first reading had already set that flag through RefuseRowsLost, because its /Index holds a reference and the stream is read in part. So the record says a section read before it refused every number, where only the stream's own reading did. That contradicts the field's doc and the readAgain comment's own rule: "is no refusal by a section read before".

Nothing a caller sees depends on it. The field is read only by ReportLostSection (:4405) and ReportSectionReadInPart (:4540). The first runs before readAgain and the second is skipped for a record read again. No validation rule reads the field, and XRefSectionRecord is internal. With the copy applied, my probe's diagnostics, findings and served objects come out byte-identical, and 970 cross-reference tests still pass.

The batch keeps one rule here: a stream's own refusals are not "before" it. The fix stage carried RowsRefusedBefore, just as invisible, and pinned it with a test row to kill mutant M04 (r4-fix-result.json). The sibling field fails that same rule, so this is a consistency gap, not a behavior defect. None of #289, #295, #331, #52, decisions.md or r1-r4 records it, and main has no readAgain block, so this is new in the batch.

The proposed fix is also correct where a newer section really did refuse every number before the place was read. In that case the place's own EveryNumberRefusedBefore is already true, and the copy keeps it true.
## VERDICT evidence
I rebuilt the reproducer with my own probe, ZzRefuteEnrb, on 8ba4589 in Release with 0 errors. The shape: the oldest table indexes 1, 2, 7 "(seven as the oldest has it)" and 9 (= 40). Stream 6 holds the rows 4, 6 and 7 (type 3) under `/Index [4 1 6 9 0 R]`. The newest table at 1736 gives `/Prev 1561 /XRefStm 1568`, and indexes 9 and 5.

Head output (/tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute-enrb/head.txt), shape A:
- `S /XRefStm@1568 NamedAgain Unknown ... enrb=False`
- `S /Prev@1568 Relocated Stream inc=True reserved=1 reservedRefused=0 rrb=False enrb=True numbersRefused=False ord=3`
- `S /Prev@172 Read Table ... rrb=True enrb=True` (correct: this table really was read after the stream)

Reader diagnostics: xref.section-unreadable (holds 3 rows, /Index and /Size declare 41), xref.entry-reserved ("the row was read so"), xref.offset-adjusted, xref.hybrid-stream-lost, xref.rebuilt. Rule: xref.entry-type-reserved, "as the reader did until it rebuilt the index". In the controls, `/Index [4 1 6 3]` (read in part, numbering readable) and `/Index [4 1 6 2]` (read whole), the /Prev record has enrb=False.

I then applied the proposed fix locally: init became set, plus `section.EveryNumberRefusedBefore = streamsPlace.EveryNumberRefusedBefore;` next to the two copies. Output: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/refute-enrb/fix.txt. `diff head.txt fix.txt` shows one line only: `S /Prev@1568 ... enrb=True` became `enrb=False`. Every D (diagnostic), F (finding) and obj line is identical. With the fix, CrossReferenceChainTests, CrossReferenceRuleTests and HybridFileTests give 970 total, 0 failed. Without it they also pass: no test pins the field on a record read again.

The worktree was reverted and the probe removed afterwards.
## VERDICT fix
Make XRefSectionRecord.EveryNumberRefusedBefore `{ get; set; }`, and add `section.EveryNumberRefusedBefore = streamsPlace.EveryNumberRefusedBefore;` beside the two copies in the readAgain block. Extend the comment's list ("nor every number said refused before it"). Add a row to CrossReferenceChainTests.A_prev_falling_short_of_the_stream_its_xrefstm_read_reads_it_whole_and_the_naming_is_reported, through HybridFiles.PrevFallingShortOfTheStreamBefore: a stream whose /Index holds a reference that an older table places (V10-like). Assert sections[2].EveryNumberRefusedBefore is false and the oldest table's is true. ADR 53's drop-the-copy mutant would otherwise survive, as M04 did for RowsRefusedBefore. A cheaper alternative is a one-line note in the readAgain comment saying the field keeps its creation value because nothing reads it for a section read again. Either is acceptable. This is not a maintainer question.

######## [9] lens=headers key=headers:short-read-remarks-say-every-short-read-steps-a-byte sev=nit -> narrowed/nit maint=False
## location
src/AdCodicem.Pdf/IO/PdfFileReader.cs:2467-2468; src/AdCodicem.Pdf/IO/StreamEnds.cs:111; src/AdCodicem.Pdf/IO/PendingEndStream.cs:28-29; tests/AdCodicem.Pdf.Tests/PropertyTests.cs:466
## claim
cfaa90c's texts on short counts say that any read a source gives short makes the next read start a byte past its own start, repeating less than the overlap. PdfFileReader adds that the window loop's split independence holds only 'of windows as large as asked'. The code does that only for a read of ten bytes or fewer. The window loop at PdfFileReader.cs:2582 computes `next = position + Math.Max(1, span.Length - EndStreamSearchOverlap)` with an overlap of 10, so a short read of 11 bytes or more still repeats the full overlap, and the search ends where whole reads end it. cfaa90c's own commit body gets this right ('A read of ten bytes or fewer makes the next start a byte on'). The four texts are false for every longer short read, and they set a tighter precondition than the code needs. StreamEnds.cs:111 has the same flaw: 'or the read before was one a source gave short' is true only of a read before it of ten bytes or fewer.
## evidence
Probes saved in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r5h/probes/; outputs in the same r5h folder.
(1) ZzR5HdrTests.ZzShortReadsEverywhere drives StreamEnds.SearchStretch and HeaderText.CarryTo with SearchEndStream's own read placement: a first read the parser's b bytes, then windows doubling from EndStreamSearchFirstRead(b), next = position + max(1, given - 10). It runs 56 texts that put endstream in a header's comment, every b from 1 to the length, and every window capped at 1 to 24 bytes, and compares each with one read of the whole. Differences from one read appear only for caps 1 to 8: 12,607 rows (cap 1-3: 2,334 each; cap 4-8: 1,121 each). Caps 9 to 24 give 0 differences (shortall.txt).
(2) ZzR5HdrDocTests.ZzDocShortStreamRegion runs 13,440 documents. Reads are capped, by count or at block edges, at 9 to 40 bytes from past the parser's window to object 5's end, at fillers 20,000, 20,001 and 20,005. In every run, object 5's RawLength equals the memory source's. The only difference is the endobj judgment after a keyword in 787 runs. That judgment comes from CheckEndStreamOn/NextSignificant, which `git diff 21a0298 8ba4589` shows the batch did not touch: #52 (docregion2.txt).
(3) The remark at PdfFileReader.cs:2467-2468 reads: 'That holds of windows as large as asked: one a source gives short before the end of the file (#52) makes the next start a byte past its own start, repeating less of it than the overlap'. PendingEndStream.cs:28-29 reads '... and makes the next start a byte past its own start'. PropertyTests.cs:466 reads 'and the next starts a byte past its start'. The property's reads are 1 to 40 bytes, so most of them do not.
## fix
Say 'a read a source gives short, of ten bytes or fewer (EndStreamSearchOverlap), makes the next start a byte past its own start' in all four places. In PdfFileReader, replace 'That holds of windows as large as asked' with 'That holds of windows longer than the overlap'. In StreamEnds, narrow 'the read before was one a source gave short' to 'the read before was one of no more bytes than the overlap, which a source gave short'. These are comments only, with no code or test change.
## VERDICT reason
The finding holds for two of its four texts. In src/AdCodicem.Pdf/IO/PdfFileReader.cs:2467-2469, the remark says that any window a source gives short "makes the next start a byte past its own start, repeating less of it than the overlap". src/AdCodicem.Pdf/IO/PendingEndStream.cs:27-29, new in this batch, says without condition that such a read "makes the next start a byte past its own start". Both are false for longer short reads. The loop at PdfFileReader.cs:2582 computes `next = position + Math.Max(1, span.Length - EndStreamSearchOverlap)`. So the next read starts a byte on only when the short read is 11 bytes or fewer, and it repeats less than the overlap only when the read is 10 bytes or fewer. A read of 12 bytes or more still repeats the full 10 bytes. The finding is slightly off at the boundary: a read of exactly 11 bytes does make the next start a byte on, but it repeats exactly the overlap, not less. cfaa90c's commit body ("A read of ten bytes or fewer makes the next start a byte on") is true and needs no record.

The other two texts are not false, so I refute them.
- tests/AdCodicem.Pdf.Tests/PropertyTests.cs:466 says "a read may then end before the one before it, and the next starts a byte past its start". Here "its" is the read that ended before the one before it. Such a read is at most 9 bytes: the next start is at least the previous end minus 10, so ending before that end leaves fewer than 10 bytes. The next read therefore always starts a byte past it, the same arithmetic in the loop and in EndReadPartly. The sentence is exact, and it says nothing about the property's other reads of 1 to 40 bytes.
- src/AdCodicem.Pdf/IO/StreamEnds.cs:111 says the end of line is inside "unless ... the read before was one a source gave short". That clause only lists where the claim may fail; it does not say it always fails there. It names more cases than needed, but nothing in it is false.

"That holds of windows as large as asked" is a stronger condition than the code needs, but it is not false either. The rest of the remark is correct, so all of this is wording.

Known debts do not cover it: #52 is about the behavior, and these sentences describe the mechanism wrongly. No earlier round raised these sentences; round 4 [11] produced them, through cfaa90c.
## VERDICT evidence
Code on 8ba4589 (origin/ccr-6d6c0ee6-dqm4lm-188), PdfFileReader.cs:2582: `var next = position + Math.Max(1, span.Length - EndStreamSearchOverlap);` with EndStreamSearchOverlap = 10 (:177).

Probe, my own, through the real SearchEndStream: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r5ref-shortwindow/ZzR5RefShortWindowTests.cs. Output: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r5ref-shortwindow.txt.
- The file: object 5 is `<< /Length 9999999 >>` stream with 20,000 'A' bytes, then `\nendstream\nendobj`.
- The source logs every Read and caps each read that starts 100 or more bytes past the data start. The probe calls GetObject(5).
- Search-loop reads (offsets are past the data start):
  - cap 15: at 8145 asked 11873 given 15, then 8150, 8155, 8160… The next read starts 5 bytes on and repeats 10 bytes. RawLength 20000.
  - cap 11: 8145, 8146, 8147… The next starts 1 byte on and repeats 10 bytes. RawLength 20000.
  - cap 10: 8145, 8146… The next starts 1 byte on and repeats 9 bytes. RawLength 20000.
  - cap 5: the next starts 1 byte on and repeats 4 bytes. RawLength 20010, so the keyword is missed, as the remark allows.
- So a short read of 15 bytes does not make the next start a byte past its own start, and one of 11 bytes repeats the whole overlap, not less. The remark at PdfFileReader.cs:2467-2469 and PendingEndStream.cs:27-29 are false for those reads.

PropertyTests.cs:466: the previous read ends at e, and the next starts at s ≥ e − 10. A read [s, s+L) that ends before e has L < 10, so the next start is max(s+1, s+L−10) = s+1. The sentence is exact. PropertyTests.cs:819 ("or starting a byte past its start where it is shorter than that") is also accurate.

StreamEnds.cs:111 is an "unless" clause with one case more than needed (short reads longer than the overlap). It asserts nothing false.

The reviewer's own probe output (r5h/shortall.txt, 12,607 rows of difference at caps 1 to 8 and none at 9 to 24) agrees with this: a short read longer than the overlap leaves the search as whole reads leave it. PendingEndStream.cs is not on main 21a0298. cfaa90c introduced the texts.
## VERDICT fix
Comments only; no code or test change.

- PdfFileReader.cs:2467-2469: change "one a source gives short before the end of the file (#52) makes the next start a byte past its own start, repeating less of it than the overlap" to "one a source gives short before the end of the file (#52), of no more bytes than the overlap, makes the next start a byte past its own start, repeating less of it than the overlap". Optionally also change "That holds of windows as large as asked" to "That holds of windows longer than the overlap". Only the SearchEndStream outputs follow from that; the endobj check after a keyword is still #52.
- PendingEndStream.cs:27-29: change "and makes the next start a byte past its own start" to "and one of no more bytes than the overlap makes the next start a byte past its own start".
- StreamEnds.cs:111: may optionally be narrowed to "the read before was one a source gave short, of no more bytes than the overlap (#52)". It is not false as it stands.
- PropertyTests.cs:466: leave as is; it is exact.

This can go into the round 5 docs or comment fix stage. It does not need the maintainer.

######## [10] lens=headers key=headers:headerat-summary-predates-the-eager-call sev=nit -> confirmed/nit maint=False
## location
src/AdCodicem.Pdf/IO/HeaderText.cs:249-254 (caller src/AdCodicem.Pdf/IO/PendingEndStream.cs:145)
## claim
HeaderText.HeaderAt's summary still describes its argument as the obj of a header: 'Gives where the object header that ends with the obj at keyword in the read starts in the file, as FindHeader takes one, or -1 when it takes none there: the token obj, which white space or a delimiter follows, or the end of the search'. Since cfaa90c, PendingEndStream calls it at the first byte of any token that starts with 'o', before anyone knows whether the token is obj. HeaderAt checks nothing at or after that byte. It reads back from it and returns a header start for 'o', 'ob' or 'other' too. TokenEnds then discards that value unless the token proves to be obj (`header = _matched == ObjKeyword.Length ? _header : -1`). The behavior is right. The summary misstates what the method guarantees, and a later caller may take a non-negative result as a header.
## evidence
Probe ZzR5HdrAtTests.ZzHeaderAtOnOther, saved in scratchpad r5h/probes, output r5h/headerat.txt. Data '12 % endstream\n0 other\n' at data start 100: 'HeaderAt(index of 'o' in other = 17) = 100; FindHeader = -1'. The document-level sweep shows that the discarded value never surfaces. The texts '\nendobj\n12 % endstream\n0 other\n12 0 obj', '...\n0 ob', '...\n0 o', '12 0 % endstream\nobject' and others give the same data end and report as the memory source under every split and short-read cap I ran (docwhole.txt, docshort.txt).
## fix
Restate the summary to match the new call. HeaderAt reads only the bytes before keyword, and gives where a header would start were the token there obj, which white space, a delimiter or the end of the search follows. Whether it is obj is the caller's to decide: FindHeader before the call, PendingEndStream when the token ends. This is a comment only.
## VERDICT reason
The finding holds. HeaderText.HeaderAt (src/AdCodicem.Pdf/IO/HeaderText.cs:249-254) came in with ea348ce, and cfaa90c did not touch it. Its summary describes the argument as 'the obj at keyword … the token obj, which white space or a delimiter follows, or the end of the search'. Up to b4ab948 that matched the only caller: PendingEndStream.TokenEnds called HeaderAt only after the token had ended and `_matched == 3`. Since cfaa90c (the R4d deviation, kept in decisions.md), PendingEndStream.cs:145 calls it at the first byte of every token that starts with 'o', before anyone knows the token is obj or what ends it. HeaderAt checks nothing at or after that byte. TryFindStart reads only the bytes before it, so the method returns a header start for 'o', 'ob', 'other' or 'objx'. Read literally ('or -1 when it takes none there', meaning where FindHeader takes none), the summary is now false for those inputs. Read as a precondition, the only caller no longer meets it. The behavior is right: TokenEnds discards `_header` unless the token is exactly obj (`header = _matched == ObjKeyword.Length ? _header : -1`), and the eager reading is a deviation decisions.md accepted. The code comment at the call site (:143-144) and the PendingEndStream remarks (:17-20, :23-27) state the new arrangement correctly. Only the callee's summary is stale. No round 1 to 4 finding and no open point covers it. It is an internal member with one caller, so this is a nit, not a minor.
## VERDICT evidence
I rebuilt the probe myself on 8ba4589 (detached at origin/ccr-6d6c0ee6-dqm4lm-188), saved at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r5h-ref/ZzR5HdrAtRefTests.cs. Output, in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r5h-ref/headerat.txt, with the read at 100 and HeaderAt at the last 'o':
- '12 % endstream\n0 other\n': HeaderAt(17) = 100; FindHeader = -1
- '12 % endstream\n0 o': HeaderAt(17) = 100; FindHeader = -1
- '12 % endstream\n0 ob': HeaderAt(17) = 100; FindHeader = -1
- '12 % endstream\n0 obj\n': HeaderAt(17) = 100; FindHeader = 100
- '12 0 other\n': HeaderAt(5) = 100; FindHeader = -1
- '12 0 objx\n': HeaderAt(5) = 100; FindHeader = -1

`git show cfaa90c -- src/AdCodicem.Pdf/IO/PendingEndStream.cs` shows the call moving out of TokenEnds. The removed line was `header = _matched == ObjKeyword.Length ? text.HeaderAt((int)(_tokenStart - at)) : -1;`, which ran only once the token was obj. The new call at Space→Token is `_header = bytes[index] == ObjKeyword[0] ? text.HeaderAt(index) : -1;`. `git log 21a0298..8ba4589 -- src/AdCodicem.Pdf/IO/HeaderText.cs` lists no commit after ea348ce. grep finds HeaderText.HeaderAt called only at PendingEndStream.cs:145. The discarded value never surfaces, as the reviewer's document sweep and the TokenEnds code both show.
## VERDICT fix
A comment-only change in HeaderText.cs:249-253. Restate the summary so that it says what the method reads and leaves the token's identity to the caller. For example: 'Gives where an object header would start in the file, as FindHeader takes one, were the token whose first byte is at keyword in the read obj, which white space, a delimiter or the end of the search ends; or -1 when the rule takes no header there. Reads only the bytes before keyword: whether the token is obj, and what ends it, is the caller's to tell (PendingEndStream reads this at the token's first byte and keeps it only when the token proves to be obj).' Rename the parameter if wanted (keyword to tokenStart, say). No code or test change. This is not a design choice for the maintainer.

######## [11] lens=validator-tests key=test-name-reads-whole-vs-rows-read-in-part sev=nit -> refuted/none maint=False
## location
tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs:4713
## claim
Round 4 (R4c) added two rows to A_prev_falling_short_of_the_stream_its_xrefstm_read_reads_it_whole_and_the_naming_is_reported in which the reader itself says stream 6 was read only in part. The name says the /Prev 'reads it whole', which those two rows contradict. The test's assertions are right; only the name no longer covers its rows.
## evidence
Probe ZzR5vtWhole on 8ba4589 (copy at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/ZzR5vtWhole.cs is not kept; output in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r5vt-probe2-whole.txt). Row 'a reserved row, one row short': `diag xref.section-unreadable @1682: The cross-reference stream /XRefStm names at offset 1682 was read only in part: it holds 3 rows where its /Index and /Size declare 4; ...`, and section `/Prev Relocated @1682 incomplete True`. Row 'a reserved row after a refused one': `diag xref.section-unreadable @1682: The cross-reference stream /XRefStm names at offset 1682 was read only in part: the row for object 7 places it in object stream 0, which is no object number; that row alone was refused, ...`. In both rows the validator also gives `xref.section-malformed` for that stream.
## fix
Rename the theory to say what all five rows hold, for example A_prev_falling_short_of_the_stream_its_xrefstm_read_takes_it_and_the_naming_is_reported (the sibling theory already says 'takes it'). Or move the two refusal rows into a sibling theory named for what they hold, such as 'a stream a prev reads again is judged by its first reading's refusals'. Update the threat-model citation (row 422/427) to match.
## VERDICT reason
The facts reproduce, but they do not contradict the name. In the two rows R4c added, the reader does say "read only in part": the theory's own codes arm asserts XRefSectionUnreadable for both. That phrase and the name's "whole" describe different things.

- "Read only in part" is what the /XRefStm's first reading reports about the stream's rows: it holds fewer rows than it declares, or it refuses one row. The diagnostic says "The cross-reference stream /XRefStm names at offset 1682 was read only in part".
- "Reads it whole", in the name, is about the /Prev's reading. Here the batch uses "whole" to mean read as the chain reads a section, its links followed. The opposite is the /XRefStm's reading "in the stream's place, its links left unread". Every text on this path defines it that way:
  - the test comment R4c rewrote: "the /Prev reads the stream whole, its own /Prev with it";
  - PdfFileReader.cs:3363: "the /Prev read it again, whole, its links with it". The next sentence speaks of "what that reading reported of the section, read in part", so the code uses both notions side by side on purpose;
  - the R4c commit body (7b1d926): "a stream that the table's /Prev ... then reads again whole", followed by "the numbers a stream read in part declares";
  - threat-model.md row 422: "it takes it, reading it whole".

In that sense the name holds in all five rows. Each row asserts the stream's own /Prev followed to the oldest table, as section 3: ("/Prev", Read, Table, oldest). Each row also asserts the naming reported, with xref.hybrid-stream-lost as the last diagnostic. The name holds even in the stricter sense. Where the stream is one row short, the /Prev reads every row the data holds plus the stream's /Prev, and leaves nothing of the stream unread. The shortfall is in the file, not in the reading. The batch has more than a dozen test names on this sense of whole, among them A_stream_reads_whole_wherever_the_window_edge_falls_near_its_end, A_cross_reference_stream_whose_length_is_wrong_is_read_whole_and_reported, ..._as_a_whole_read_does and the sibling A_prev_falling_short_of_the_table_its_xrefstm_read_in_the_stream_s_place_reads_that_table_whole.

This is unlike the naming findings earlier rounds confirmed. R4 [18]'s name said less than its rows held, and R4 [19]'s was true by definition. Here the name is true for every row in the meaning its own comment gives it, and not falsified by the test's assertions.
## VERDICT evidence
Built tests/AdCodicem.Pdf.Tests in Release on origin/ccr-6d6c0ee6-dqm4lm-188 (8ba4589) and ran the theory with --filter-method "*A_prev_falling_short_of_the_stream_its_xrefstm_read_reads_it_whole_and_the_naming_is_reported*": 5 passed, 0 failed.

- For rows "a reserved row, one row short" and "a reserved row after a refused one", the codes arm `_ => [XRefSectionUnreadable, XRefEntryReserved, XRefOffsetAdjusted, XRefHybridStreamLost]` (CrossReferenceChainTests.cs:4737) holds the reader's "read only in part" report. That message comes from PdfFileReader.cs:4614, `{SectionNamed(section, naming)} was read only in part`. The reviewer's probe output (/tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r5vt-probe2-whole.txt) words it "The cross-reference stream /XRefStm names at offset 1682 was read only in part ...", which is the /XRefStm's reading.
- In the same rows, the sections assertion (CrossReferenceChainTests.cs:4727-4731) requires ("/Prev", Relocated, Stream, stream) followed by ("/Prev", Read, Table, oldest). The /Prev took the stream with its own /Prev. The probe shows it: section /Prev Relocated @1682, then /Prev Read @291.
- Texts defining "whole" on this path:
  - the test comment at :4719-4720, "the /Prev reads the stream whole, its own /Prev with it";
  - PdfFileReader.cs:3338, "which it takes, reading it whole";
  - PdfFileReader.cs:3363-3366, "the /Prev read it again, whole, its links with it ... what that reading reported of the section, read in part ..., is not reported again";
  - the 7b1d926 commit body;
  - docs/threat-model.md:422.
## VERDICT fix
None needed. If the maintainer wants the name to avoid "whole" next to rows the reader reports as read in part, the rename the reviewer proposed is optional style: ..._takes_it_and_the_naming_is_reported, matching the sibling's "takes it". The threat-model.md citations at rows 422 and 427 would change in the same `test:` commit, with footer Refs #156. It fixes no misstatement.

######## [12] lens=validator-tests key=limit-trailer-row-ties-white-space-guard-to-streams sev=nit -> confirmed/nit maint=False
## location
docs/website/docs/reference/diagnostics.md:141
## claim
The limit.trailer row, as round 4 reworded it, puts the white-space case inside a sentence about a stream: 'A stream's dictionary is measured from its object's header, reported there, the white space and comments the offset that names it falls short by measured apart: where they run on past the bound ...'. The guard and its message apply to any named section. A classic table that startxref or a /Prev names through more white space than MaxTrailerLength gets the same report, as CrossReferenceRuleTests.A_startxref_naming_more_white_space_than_a_trailer_s_window_before_a_classic_table_reaches_the_guard pins. The XML doc (PdfDiagnosticCodes.LimitTrailer) and reference/reader-limits.md:21 are general; this row reads as stream-only.
## evidence
Probe ZzR5vtGuardBlame on 8ba4589 (output in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r5vt-guardblame-head.txt). A classic table only, startxref naming 70,000 spaces before its xref keyword: `diag limit.trailer @230: White space and comments run past 64 KB where a cross-reference section is named; what follows them was not read. Raise PdfReaderLimits.MaxTrailerLength to read past it.` The file holds no cross-reference stream at all.
## fix
Split the clause, for example: 'A section is read from its first token — its xref keyword, or its stream's object header, where a stream's dictionary is measured from and reported —, the white space and comments the offset that names it falls short by measured apart: where they run on past the bound ...'.
## VERDICT reason
The finding holds as a nit. The white-space guard does not depend on the section's type. TryReadXRefSection (src/AdCodicem.Pdf/IO/PdfFileReader.cs:5098-5111) calls PaddingAt and reports limit.trailer whenever padding < 0. That check runs before the reader knows whether a table or a stream lies there. Past the bound it cannot know, because the first token is never read.

Two of the other texts say this in general terms: PdfDiagnosticCodes.LimitTrailer (src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs:330-334) and the reader-limits.md row and bullet (lines 21 and 204). The quoted message itself says 'where a cross-reference section is named'. The diagnostics.md:141 row is different. Its white-space clause hangs off 'A stream's dictionary is measured from its object's header, … the white space and comments the offset that names it falls short by'. There 'it' is the stream, and nothing in the row covers a classic table named through white space. Someone who meets the message on a file that has only classic tables and looks up the row would read it as being about streams. Nothing the row says is false for streams. It leaves out tables, and it implies a type the reader never learns. That makes it wording, not a misstatement of behavior: nit.

#289 and #295 hold the behavior, not this row's wording, and no earlier round reported the row.

The reviewer's proposed fix needs narrowing. 'A section is read from its first token — its xref keyword, …' would be false for a classic table. TryReadClassicTable opens its windows at the offset (`_source.GetWindow(absolute, windowSize)` with `maxWindow = _guard.Bound(PdfLimit.XRefSectionLength)`, PdfFileReader.cs:5219-5225), so white space under the bound counts toward MaxXRefSectionLength (#295). The rewording must not say that a table is read from its keyword.
## VERDICT evidence
I checked the claim on 8ba4589 (detached at origin/ccr-6d6c0ee6-dqm4lm-188, built in Release with 0 warnings). The test the finding cites, CrossReferenceRuleTests.A_startxref_naming_more_white_space_than_a_trailer_s_window_before_a_classic_table_reaches_the_guard (tests/AdCodicem.Pdf.Tests/CrossReferenceRuleTests.cs:2553-2600), passes all 5 rows: 'total: 5 failed: 0 succeeded: 5'. Its file holds three objects and a classic table only, and startxref names the table through padding. It asserts:
- the diagnostics are exactly [limit.trailer, xref.rebuilt];
- the section is NotFound with fault 'holds neither the xref keyword nor an object';
- the diagnostic is at the offset, with the message 'White space and comments run past 64 KB where a cross-reference section is named; what follows them was not read. Raise PdfReaderLimits.MaxTrailerLength to read past it.' (70,000 spaces, default limits).

This matches the reviewer's probe output in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r5vt-guardblame-head.txt: 'diag limit.trailer @230: White space and comments run past 64 KB …' for a startxref with 70,000 spaces before a classic table, and the same for a /Prev at 65,537.

In the code, the guard sits at PdfFileReader.cs:5098-5111 in TryReadXRefSection, before the `if (table)` branch at :5116. The row text at docs/website/docs/reference/diagnostics.md:141 reads: 'A stream's dictionary is measured from its object's header, reported there, the white space and comments the offset that names it falls short by measured apart: where they run on past the bound, the message says so …'. The general texts are at PdfDiagnosticCodes.cs:330-334 and reader-limits.md:21 and :204.
## VERDICT fix
Split the sentence in docs/website/docs/reference/diagnostics.md:141 so that the white-space clause covers any named section, and do not say that a table is read from its keyword (#295). For example: 'A stream's dictionary is measured from its object's header, and reported there. Wherever the chain names a section, a table or a stream, the white space and comments the offset falls short by are bounded apart: where they run on past the bound, the message says so — `White space and comments run past 64 KB where a cross-reference section is named` —, it is reported at the offset, nothing past them is read, whatever they hide, and the section is looked for nearby as one that is not there, the index rebuilt for the one `startxref` names.'

docs/website/docs/concepts/reader-limits.md:88-91 has the same stream-first structure, but 'Wherever the chain names a section' frames it in general terms, so it can stay or be aligned in the same edit. This is a docs-only edit with no design choice for the maintainer.

######## [13] lens=validator-tests key=paddingat-cost-remark-ten-times sev=nit -> narrowed/nit maint=False
## location
src/AdCodicem.Pdf/IO/PdfFileReader.cs:5145
## claim
Round 4 rewrote the PaddingAt remark's cost claim. It now says 'so that the bytes read come to less than ten times the run crossed, or one probe where there is none'. That is false where the first 32-byte probe ends exactly at the keyword's end. The probe grows on 'first.End >= probe.Length', so 28 bytes of white space before xref read 32 + 256 = 288 bytes, 10.3 times the run. A first token the probe's edge cuts with no white space also grows the probe, so 'one probe where there is none' does not hold either. The old remark ('less than a seventh more than the last window holds') was exact. No cost bound is broken; only the text overstates.
## evidence
Probe ZzR5vtProbeCost on 8ba4589 (copy at /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/ZzR5vtProbeCost.cs, output in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/r5vt-probecost.txt). The source logs reads at the offset startxref names; the last read there is the table's own window (#295). `run 28: reads at the offset named 32, 256, 3153` gives a probe of 288 bytes for 28 crossed, 10.29x. `run 27: reads at the offset named 32, 3152` gives one probe. `run 29: 32, 256, 3154` gives 288/29 = 9.93x. `run 250: 32, 256, 3375` and `run 2040: 32, 256, 2048, 5165` stay well under.
## fix
Restore the exact bound: 'so that the bytes read are less than a seventh more than the last window holds', which is at most about nine times the run once the run is past the first probe. Or say 'about nine times the run crossed, 288 bytes at most for a run the first probe holds'.
## VERDICT reason
The main claim holds. The remark f0d90c8 rewrote (PdfFileReader.cs:5145-5146) says the probes cost "less than ten times the run crossed, or one probe where there is none", and on 8ba4589 that is false in three cases.
- A run of 28 bytes before `xref` puts the keyword's end exactly on the first probe's edge. The probe grows, so 288 bytes are read: 10.29 times the run.
- A run of 1 to 3 bytes reads one 32-byte probe, which is more than ten times the run.
- With no white space at all, a first token of 32 bytes or more makes the probe grow, which only a malformed file has at a section's offset. So "one probe where there is none" does not hold either.

The finding's side claim is refuted. It says the old remark ("less than a seventh more than the last window holds", added in c29761c and present on b4ab948) "was exact". It was not: the probe's growth is capped at a trailer's window, and 65536 (or 65568 now) is not 32 times a power of eight. So at the default MaxTrailerLength, a run past 16 KiB reads 32+256+2048+16384+65568 = 84288 bytes. That is 28.5% more than the last window, not less than a seventh. On b4ab948 it was 84256 against 65536. The finding's first fix would therefore put back a text that was false already. Its second fix ("about nine times the run") still misstates the 10.29x case.

The true bound comes from the code's own growth rule. A probe grows from P to 8P only when the first token ends at or past P. So the bytes read are at most P(8 + 8/7) = (64/7)P, which is under ten times the offset's distance to the end of its first token, including the token. No cost bound is broken and nothing changes in behavior. Only a private member's remark misstates an edge, so the severity is nit. This is not one of the known issues #289, #295, #331 or #52.
## VERDICT evidence
Rebuilt on origin/ccr-6d6c0ee6-dqm4lm-188 at 8ba4589 (Release). The probes are uncommitted and have been removed from the worktree; copies are in /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/ZzR5rfProbeCost.cs and /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/ZzR5rfProbeCost2.cs. In each case below, the last read listed at the offset is the table's own window or the rebuild's, not a probe.

ZzR5rfProbeCost (the reviewer's file, with more runs):
- run 1: 32, 3126. One 32-byte probe, 32x the run.
- run 3: 32, 3128. 10.7x the run.
- run 27: 32, 3152.
- run 28: 32, 256, 3153. Probes total 288 bytes, 10.29x the run; reproduces the finding.
- run 29: 32, 256, 3154. 9.93x the run.

ZzR5rfProbeCost2 (more than 65 KiB after the offset, startxref at the end):
- run 20000 token 0: 32, 256, 2048, 16384, 65568, 65536. Probes total 84288 bytes against a last window of 65568, so 1.285 times it. The old "a seventh" remark is false here.
- run 0 token 40: 32, 256, 65536 (xref.rebuilt). Two probes with no run.
- run 0 token 300: 32, 256, 2048, 65536.

b4ab948 (not rebuilt; read from its source): the growth is `probeLength = Math.Min(_guard.Bound(PdfLimit.Trailer), probeLength * 8)`, with a default bound of 64 * 1024. That gives probes 32, 256, 2048, 16384, 65536: 84256 bytes, 1.286 times the last window.

`git log -S"a seventh more"` names c29761c, which added the old text, and f0d90c8, which replaced it.
## VERDICT fix
Change the last sentence of the remark at PdfFileReader.cs:5145-5146. A text the code holds in every case, the cap at a trailer's window included:

"The windows grow eightfold from XRefProbeLength bytes, and only past a first token the last one held to its edge, so that the bytes read come to less than ten times the run and its first token together, or one probe where these fit in it."

Do not restore the old "a seventh more than the last window" wording; it is false once the cap is reached. Wording only, with no change to code or tests. Not a maintainer decision.

######## [14] lens=docs key=tm-52-gap-no-331 sev=minor -> confirmed/minor maint=False
## location
docs/threat-model.md:366-371 (known gaps under 'Opening and the file source'), and no [#331] link definition
## claim
The #52 known gap still files the ReadTrailerPastRows hang under #52. #331 was filed after round 4 for exactly that hang (debt, area: reader, M03) and is cited nowhere in the docs. #331's own 'What closes it' says the threat model's #52 gap should cite #331 in place of the text that names the hang under #52. decisions.md also planned it ('threat-model.md:366 to cite #331').
## evidence
`grep -rn 331 docs/threat-model.md docs/website/docs` matches only line 369, on the word 'shrinks', and finds no '#331'. The link check over the 7 touched pages finds no [#331] definition. Line 366-369 reads: '- [#52] A source whose `Read` returns fewer bytes than asked is taken as the end of the data — not everywhere alike: the search for a classic table's trailer past the rows a guard left unread reads one window again, without end, where the source gives it 32 bytes or fewer before the file's end, as the library's own `FileSource` does for a file that shrinks while it is open; ...'. Issue #331 (open, created 2026-11-06T20:21:59Z) says: '[#52] does not close this' and 'Documentation. docs/threat-model.md's known gap for [#52] cites this issue in place of the text that names the hang under #52 today.' The code confirms the hang: PdfFileReader.cs:5476-5560 sets edge = span.Length - XRefProbeLength for a window that is not the file's last, and sets position = start + at.
## fix
Split the entry. Add '- [#331] The search for a classic table's trailer past the rows a guard left unread (`ReadTrailerPastRows`) reads one window again without end where a window comes back shorter than asked before the file's end, 32 bytes or fewer or empty, as the library's own `FileSource` gives for a file that shrinks while it is open; making reads loop ([#52]) does not close it.' The #331 text may also name StartsToken(long) on an empty window. Keep only the endstream-search clause under #52, and add `[#331]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/331` among the link definitions.
## VERDICT reason
The finding holds as stated. At 8ba4589 the threat model's known gap for [#52] (docs/threat-model.md:366-371) still files the hang in `ReadTrailerPastRows` under #52, and #331 is cited nowhere. #331 was filed for exactly this hang and says #52 does not close it: making `GetWindow` read until the buffer is full still leaves a short window wherever the data ends before `Length`. Its "What closes it" list asks for the #52 gap to cite #331 in place of this text, and decisions.md planned the same ("threat-model.md:366 to cite #331 (round 5 fixes or docs)"), so this is an open to-do, not something settled as accepted. The batch itself wrote the text: main 21a0298 and b4ab948 have only "[#52] A source whose `Read` returns fewer bytes than asked is taken as the end of the data.", and 8ba4589 (20:03 UTC) added the clause naming the hang 18 minutes before #331 was opened (20:21 UTC). The brief's "known, do not re-hunt" covers the hang itself, not the text, and the exception for "the docs now misstate it" applies here. The text describes the behavior correctly but names the wrong issue. Someone closing #52 in M03 would take this gap as paid while the hang stays open, and nothing in the docs points to #331. The other #52 citations (threat-model.md:270, 424, 534; reference/validation-rules.md, reference/diagnostics.md, concepts/lazy-reading.md, concepts/diagnostics.md) are about the line test and the stream-end search reading differently after a short read, which really are #52's; none of them names the hang. Severity is minor: a misleading text that sends the hang to the wrong debt, with no change in behavior.
## VERDICT evidence
- **Searches.** On the detached head 8ba4589 a search for `#331|issues/331` over the repository finds nothing in docs/ or src/; the only hit is an unrelated pdf-association URL in tools/Arlington. `ReadTrailerPastRows` appears in docs/threat-model.md only at line 427, in a table's defense column. There is no `[#331]:` link definition: the definitions end at `[#330]` on line 1035.
- **The gap's text at the head.** docs/threat-model.md:366-371 reads: "- [#52] A source whose `Read` returns fewer bytes than asked is taken as the end of the data — not everywhere alike: the search for a classic table's trailer past the rows a guard left unread reads one window again, without end, where the source gives it 32 bytes or fewer before the file's end, as the library's own `FileSource` does for a file that shrinks while it is open; and the search for a lost `endstream` may miss a keyword …".
- **The same line on main and b4ab948.** `git show 21a0298:docs/threat-model.md` has at line 365 only "- [#52] A source whose `Read` returns fewer bytes than asked is taken as the end of the data."; b4ab948 has the same at line 366.
- **Where the clause came from.** `git log -S'reads one window again' 21a0298..HEAD -- docs/threat-model.md` gives a single commit, 8ba4589 at 2026-11-06 20:03:57 +0000, "docs: bring the pages in line with the round's reader fixes".
- **Issue #331.** It is open, labeled debt and "area: reader", under M03, created 2026-11-06T20:21:59Z. Its body says "[#52] does not close this" and "Documentation. `docs/threat-model.md`'s known gap for [#52] cites this issue in place of the text that names the hang under #52 today."
- **decisions.md.** Lines 293-294 say: "ReadTrailerPastRows hang on short windows … filed #331 … threat-model.md:366 to cite #331 (round 5 fixes or docs)."
- **The code.** PdfFileReader.cs:5466-5560 matches the gap's text. Where the window is not the file's last, `edge` is `span.Length - XRefProbeLength`, and `position = start + at` does not move when a window of 32 bytes or fewer has a byte that is not a row byte at offset 0, or when the window is empty.
## VERDICT fix
In docs/threat-model.md, under "Opening and the file source", split the entry in two:
1. Keep under [#52] only what a short read changes and #52 closes: "- [#52] A source whose `Read` returns fewer bytes than asked is taken as the end of the data — not everywhere alike: the search for a lost `endstream` may miss a keyword, a header, or the end of line before a keyword that no window then holds whole, the data ending later, while an `endstream` in a comment that waits is decided as whole reads decide it."
2. Add a new entry: "- [#331] The search for a classic table's trailer past the rows a guard left unread reads one window again, without end, where a window comes back 32 bytes or fewer before the file's end, or empty. The library's own `FileSource` gives such a window for a file that shrinks while it is open, and so does a caller's source whose `Length` is larger than the data it serves. Making reads loop ([#52]) does not close it." The entry may also say that `StartsToken(long)` takes the first byte of a window that may be empty.
3. Add `[#331]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/331` after `[#330]` among the link definitions.

Then run the link check and the site build. This is not a design choice for the maintainer: #331's own "What closes it" prescribes the change.

######## [15] lens=docs key=classic-table-white-space-not-apart sev=minor -> narrowed/minor maint=False
## location
src/AdCodicem.Pdf/Documents/PdfReaderLimits.cs:145-149; docs/threat-model.md:105 and :429; docs/website/docs/concepts/lazy-reading.md:176-179; docs/website/docs/reference/reader-limits.md:21 and :204-207; docs/website/docs/reference/diagnostics.md:141
## claim
Round 4's texts say a named section is read from its first token, 'its xref keyword, or its stream's object header' (PdfReaderLimits remarks), or 'A section is read from its first token' (threat-model :429). They say the white space before it is 'bounded apart' or 'counted apart' by MaxTrailerLength. That holds for a cross-reference stream only. A classic table's windows still open at the offset named (TryReadClassicTable(absolute, ...), PdfFileReader.cs:5114), so the white space before its keyword also counts toward MaxXRefSectionLength (R4a's open point, #295/#289 territory). The limit.trailer reference row (:141) has the opposite gap: it ties the white-space guard to 'the offset that names it [a stream's dictionary]'. Yet that guard is reached before a classic table too, which is CrossReferenceRuleTests.A_startxref_naming_more_white_space_than_a_trailer_s_window_before_a_classic_table_reaches_the_guard. The behavior is the known debt. The new texts misstate it, and none cites #289 or #295.
## evidence
Probe at 8ba4589: a classic table plus trailer of 143 bytes, 600 spaces before its keyword, startxref naming the first space, MaxXRefSectionLength = 650. Output: 'CLASSIC padding=600 named=600 (offset 203, keyword at 803) MaxXRefSectionLength=650: state=Read kind=Table padding=600 cutByLimit=True; diag=[limit.xref-section-length@203: The cross-reference table runs past 650 bytes; only the entries within it were read. ...]'. With startxref naming the keyword: 'named=0 ... cutByLimit=False; diag=[]'. With the bound at 1,000,000: 'named=600 ... cutByLimit=False; diag=[]'. So the 600 bytes of white space are counted toward the table's own bound, not apart. `grep -n '#289\|#295'` over docs/threat-model.md and docs/website/docs finds nothing.
## fix
Narrow the claim to streams and name the exception. For example: 'a stream's dictionary is read from its object's header, the white space before it bounded apart; a classic table's window opens at the offset named, so the white space before its keyword counts toward MaxXRefSectionLength as well ([#289], [#295])'. Apply this in the MaxTrailerLength remarks, the threat-model rows 105 and 429, lazy-reading :176-179 and reference/reader-limits :21 and :204-207. In reference/diagnostics.md:141, say that the white-space clause applies to any section the chain names, a classic table's too.
## VERDICT reason
The finding holds, but only for the sentences that say a section, a classic table included, is read from its first token. The behavior is the known debt and is unchanged since main. The table's window still opens at the offset named: TryReadClassicTable(absolute, ...) at PdfFileReader.cs:5119 on head, and the same call on b4ab948 (:5037) and on main 21a0298 (:3416). Only the stream's object window was moved to absolute + Padding (R4-Q1). Round 4 then wrote a general rule that is true only for the stream. That wording is new: neither b4ab948 nor main has "first token" in PdfReaderLimits.cs, or "A section is read from its first token" in threat-model.md.

What misstates a table:
(1) PdfReaderLimits.cs:145-148, the public XML docs: "the section is read from its first token — its <c>xref</c> keyword, or its stream's object header —". This names the table's keyword as the place reading starts. In fact the table's MaxXRefSectionLength window starts at the offset named.
(2) threat-model.md:429: "A section is read from its first token, ..., the white space ... bounded apart".
(3) lazy-reading.md:176-177: "is read from that token where it is named". This one is weaker: the sentence is mainly about the section being read where it is named rather than looked for nearby.
(4) reference/diagnostics.md:141: the white-space clause hangs on "A stream's dictionary ... the offset that names it". Yet the guard is reached before any named section, since nothing past the white space is read. CrossReferenceRuleTests' classic-table guard test proves it. The XML docs of PdfDiagnosticCodes.LimitTrailer already say "a cross-reference section", correctly. This part is nit-level.

Not misstated, so dropped from the finding: threat-model.md:105, reference/reader-limits.md:21 and reader-limits.md:204-207. They say only that MaxTrailerLength bounds the white space apart from the trailer and the dictionary it bounds. That holds for a table as well: PaddingAt bounds it for both kinds. None of them claims anything about MaxXRefSectionLength, and :204-207 scopes its consequence to a dictionary.

The impact of the misstatement is small: at the defaults it needs a table within 64 KB of 64 MB. Still, it is a public API remark and a threat-model row stating a bound's measure wrongly. That is a misleading text, so minor.

Separately and not round 4's doing: on main too, the limit.xref-section-length message and the LimitXRefSectionLength docs ("A classic cross-reference section is longer than ...") blame a 123-byte table for running past 650 bytes. That belongs in the planned comment on #289.
## VERDICT evidence
Reproduced at 8ba4589 with a probe built from HandWrittenFile (since deleted). The file holds a catalog, the page tree and a page, then N spaces, then a classic table of 4 rows: 123 bytes up to `startxref`. MaxXRefSectionLength is lowered. Output:
- padding=600, startxref names the first space (offset 203, keyword at 803), MaxXRefSectionLength=650: state=Read kind=Table padding=600 cutByLimit=True; diag=[limit.xref-section-length@203: The cross-reference table runs past 650 bytes; only the entries within it were read. Raise PdfReaderLimits.MaxXRefSectionLength to read past it.]
- the same file, startxref naming the keyword (803), bound 650: padding=0 cutByLimit=False; diag=[]
- startxref naming the first space, bound 1,000,000: padding=600 cutByLimit=False; diag=[]
- no padding, bound 650: cutByLimit=False; diag=[]

So the 600 bytes of white space count toward the table's own bound. TryReadClassicTable(absolute, ...) is the same call on b4ab948 and on main, so the behavior is unchanged. The wording is new: `grep "first token"` finds nothing in b4ab948's PdfReaderLimits.cs, and "A section is read from its first token" is absent from b4ab948's and main's threat-model.md. Neither docs/threat-model.md nor docs/website/docs cites #289 or #295 (only docs/status.md does). The R4a and R4e reports already raised this open point ("a 665-byte table 600 bytes early is cut at 700 bytes and at 1 KB"). decisions.md placed it with #289/#295 without a maintainer question. That placement is fine for the behavior, but the texts written that round generalize past it.
## VERDICT fix
A docs-only fix within the triage already made, citing #289. That issue's "What closes it" covers white space counting toward the window it is read through. Changes:
- PdfReaderLimits.cs:145-148: "...falls short by: a cross-reference stream is read from its object's header, so that its dictionary is cut only where it alone runs past the bound; a classic table's window opens at the offset named, so that white space also counts toward MaxXRefSectionLength (#289)."
- threat-model.md:429: "A stream's dictionary is read from its object's header, the white space and comments the offset that names it falls short by bounded apart ...; a classic table is read through a window that opens at the offset, that white space counting toward MaxXRefSectionLength as well ([#289])."
- lazy-reading.md:176-177: say "is read where it is named" rather than "from that token", or add the same table clause.
- reference/diagnostics.md:141: detach the white-space clause from the stream, e.g. "...measured from its object's header, reported there. The white space and comments an offset that names any section falls short by are measured apart: where they run on past the bound...".
- Optionally, add "the white space before the xref keyword included ([#289])" to the MaxXRefSectionLength rows: reader-limits.md:19 and threat-model.md:104.

Leave threat-model.md:105 and reader-limits.md:21 and :204-207 as they are.

The other route makes the texts true: open the table's windows at absolute + Padding, adjusting its guard position and ReadTrailerPastRows' lower bound, as R4a sketched. That changes behavior #289 owns and is placed in batch 27, so whether to take it in batch 19 is the maintainer's call. The docs fix needs no decision.

######## [16] lens=docs key=endstream-short-read-remarks-overbroad sev=minor -> confirmed/minor maint=False
## location
src/AdCodicem.Pdf/IO/PdfFileReader.cs:2467-2471 (SearchEndStream remarks); src/AdCodicem.Pdf/IO/StreamEnds.cs:110-111 (overlap remark)
## claim
cfaa90c narrowed these remarks. SearchEndStream's now says any window 'a source gives short before the end of the file (#52) makes the next start a byte past its own start, repeating less of it than the overlap'. StreamEnds says the overlap fails whenever 'the read before was one a source gave short'. The code steps `position + Math.Max(1, span.Length - EndStreamSearchOverlap)` (PdfFileReader.cs:2582, overlap 10). Only a window of 10 bytes or fewer repeats less than the overlap. A short window of 11 bytes or more keeps the full 10-byte overlap and changes nothing. The claim that their guarantee 'holds of windows as large as asked' is too narrow, and the mechanism given for short reads is false for most short reads.
## evidence
Probe at 8ba4589: a stream /Length 9999999 with 20,000 to 20,119 bytes of data, then '\nendstream\nendobj'. It is read through ShortCountSource, which caps each search read to N bytes past dataStart + 4,000, and compared with an in-memory read over 120 placements. Output: 'ENDSTREAM cap=40: 0 of 120 placements differ', 'cap=12: 0 of 120', 'cap=11: 0 of 120', 'cap=10: 0 of 120', 'cap=7: 120 of 120 placements differ; first: filler 20000: whole 20000, short 20010'. Only reads shorter than the keyword and its end of line miss anything.
## fix
SearchEndStream: 'That holds of windows of more than EndStreamSearchOverlap bytes, short or not: one of no more, which a source may give short before the end of the file (#52), makes the next start a byte past its own start, repeating less of it than the overlap, so that a keyword or a header no window then holds whole ... may be missed'. StreamEnds: '... unless the keyword starts the data or the read before held no more bytes than the overlap, which a source may give short (#52)'. Optionally make the same narrowing in PendingEndStream's 'makes the next start a byte past its own start'.
## VERDICT reason
The finding holds, with one narrowing. The window loop steps `next = position + Math.Max(1, span.Length - EndStreamSearchOverlap)` (PdfFileReader.cs:2582, overlap 10). The code and the probe give these thresholds:
- A short read of 12 bytes or more does not make the next read start a byte on.
- A short read of 11 bytes makes it start a byte on, but still repeats the full 10 bytes.
- Only a read of 10 bytes or fewer repeats less than the overlap.

HeaderText carries runs of any length, so the overlap does not decide headers. EndObjTail follows each read up to `next`, whatever the read's size.

So, for this search, a short read of more than 10 bytes changes neither the data end, the end of line before the keyword, a header nor a waiting keyword. The remark at PdfFileReader.cs:2467-2471 says otherwise: any window "a source gives short before the end of the file (#52) makes the next start a byte past its own start, repeating less of it than the overlap". StreamEnds.cs:110-111 says the end of line is lost whenever "the read before was one a source gave short". Both state a mechanism that is false for most short reads. The cfaa90c body ("a short read may still make the search miss...") is hedged and not false, and so is its "A read of ten bytes or fewer makes the next start a byte on". No status.md entry is needed. The docs pages only describe the waiting keyword, which is accurate.

Narrowing: "changes nothing" holds for this search's outcome. A short read still counts every byte it repeats toward `_endStreamSearchBytes`, up to 11 times the stretch at 11-byte reads, so the shared EndStreamSearchPasses budget runs out sooner for later searches. That is short reads in general (#52), and the remarks under review do not cover it.

Severity: minor, because a remark states a false mechanism. It is not a nit, though nothing behaves wrongly and the text only errs on the pessimistic side. No earlier round settled this: round 4's [11] asked to narrow the claim to whole reads, and cfaa90c over-narrowed it with a false mechanism.
## VERDICT evidence
Probe /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/ZzEndStreamShortRemarkTests.cs.txt, run on origin/ccr-6d6c0ee6-dqm4lm-188 at 8ba4589 with a Release build and 0 warnings. Output: /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/zz-r5-endstream-short.txt.

The file: object 5 is `<< /Length 9999999 >>` stream with 20,000 to 20,119 'A' bytes, then an end of line and a tail. Object 6 is an 8,000-byte string. It is read through ShortCountSource, with every read that starts past dataStart+4000, and before the data's end+200, capped to `cap` bytes. Each read is compared with an in-memory read, 120 placements per row.

- LF + `endstream\nendobj`: cap 40, 20, 12, 11 and 10 give 0 of 120 differ. Cap 9 gives 120 of 120 ('whole 20000 ... short 20001', the LF left in the data). Cap 7 gives 120 of 120 ('short 20010 [stream.truncated ... its object's endobj ends it, after 20010 bytes]', the keyword missed).
- CRLF + `endstream\nendobj`: cap 40, 20, 12 and 11 give 0 of 120. Cap 10 gives 120 of 120 ('whole 20000 ... short 20001', the CR left in the data). Cap 9 gives 120 of 120 (20002). Cap 7 gives 120 of 120 (20011, stream.truncated).
- `endobj\n12 0 obj ...`, `endobj\n12 % c\n0 % d\nobj ...` and `endobj\n12 % endstream\n0 obj ...`: every cap from 40 down to 7 gives 0 of 120 differ.

The end of line is lost exactly when the read before held no more than 10 bytes: CRLF at cap 10, LF at cap 9. Reads of 11 bytes or more, short or not, change nothing in this search, across about 1.43 million short reads per row at cap 11.
## VERDICT fix
Docs only, no code change.
1. PdfFileReader.cs:2467-2471, SearchEndStream remarks: "That holds of windows of more than EndStreamSearchOverlap bytes, short or not: one of no more, which a source may give short before the end of the file (#52), makes the next start a byte past its own start, repeating less of it than the overlap, so that a keyword or a header no window then holds whole, or the end of line before a keyword, may be missed, and the search may end elsewhere — but for a keyword in a comment that waits, which is decided as whole windows decide it."
2. StreamEnds.cs:110-111: "... unless the keyword starts the data or the read before held no more bytes than the overlap, which only a source that gives fewer bytes than asked may do before the end of the file (#52)."
3. Optional nit: in PendingEndStream's remarks, read "a read that gives fewer bytes than asked (#52) may end before the one before it, which reads nothing again, and, when it holds no more than the overlap, makes the next start a byte past its own start".
4. Optionally add a WindowEdgeTests theory over the probe: a capped source at 11 bytes and more matches the memory read for LF and CRLF. That pins the threshold the corrected text states.

No maintainer decision is needed.

######## [17] lens=docs key=paddingat-cost-ratio sev=nit -> confirmed/nit maint=False
## location
src/AdCodicem.Pdf/IO/PdfFileReader.cs:5144-5146 (PaddingAt remarks, rewritten in f0d90c8); docs/threat-model.md:463 (#47 gap: 'about nine times the run it crosses')
## claim
The remark says the probe's windows grow 'so that the bytes read come to less than ten times the run crossed, or one probe where there is none'. The probe also grows for a first token that its edge cuts, whatever the run (PaddingAt:5181, first.End >= probe.Length). A short run, or none, before a long token reads up to every window, about 84 KB at the default. The cost stays bounded by MaxTrailerLength + 32, but the stated ratio is false.
## evidence
Probe at 8ba4589, through StrictCountingSource with files of the same size (70,446 bytes). startxref names 1 space, then a 70,000-byte name, then spaces. Against it, 1 space, then a 1-byte name, then spaces. Output: 'PROBECOST run=1 token=70000: bytes read at open 323531; diag=[limit.trailer | xref.rebuilt]' and 'PROBECOST run=1 token=1: bytes read at open 239275; diag=[xref.rebuilt]'. The difference, 84,256, equals exactly 32+256+2,048+16,384+65,568 − 32: every probe window, for a run of 1 byte.
## fix
Bound it by what the probe crosses to the first token's end: 'so that the bytes read come to less than ten times the run and the first token it crosses, or one probe where both lie within it'. Give the threat model's #47 gap the same wording ('about nine times what it crosses to the first token's end').
## VERDICT reason
The finding holds, and the problem is a little wider than stated. At 8ba4589, PaddingAt grows its probe whenever the first token reaches the probe's edge (`first.End >= probe.Length`, src/AdCodicem.Pdf/IO/PdfFileReader.cs:5181). That includes a token after a short run of white space, or after none. So the remark at :5144-5146 is false: it says the bytes read "come to less than ten times the run crossed, or one probe where there is none". A long token does not need to be hostile to break it. An ordinary `xref` keyword or an object number that crosses the 32-byte edge after a run of 22 to 31 bytes already makes 288 bytes read, more than ten times the run.

The cost itself is bounded: at most MaxTrailerLength + 32 bytes per probe, one probe per section, memoized. Only the text is wrong.

The wrong sentence is new in the batch. f0d90c8 replaced b4ab948's accurate remark ("less than a seventh more than the last window holds") with the ratio, and the growth condition was the same at b4ab948.

The threat-model #47 bullet (docs/threat-model.md:461-465, "about nine times the run it crosses") already said this on main 21a0298, whose probe also grew for a cut first token. The batch rewrote that bullet and kept the claim, so it is a pre-existing misstatement the batch carried forward.

The correct bound counts the bytes up to the first token's end, e. The probe grows only while e ≥ the window, and each window is 8 times the last. So the windows read sum to less than (8/7)·8·e ≈ 9.14·e, and to one probe where e < 32.

This is not one of the known issues (#289, #295, #331, #52), and no earlier round recorded it. Round 2's [3] was about how many times a section is probed, not about this ratio.
## VERDICT evidence
My own probe, saved as /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/r5ref-ZzPadCostTests.cs.txt and run at 8ba4589 in a Release build. It uses a logging source that records every read starting at the offset startxref names. Each file is 70,531 bytes, with spaces up to a fixed region. Only the PaddingAt windows are listed below, which are the leading eightfold series. The 65,536 and 70,421 windows that follow some of them are the table or stream reading and the rebuild.

- **1 space, then a 70,000-byte name (the finding's shape):** windows [32,256,2048,16384,65568] = 84,288 bytes. Diagnostics: limit.trailer, xref.rebuilt, trailer.root-recovered.
- **1 space, then `/A`:** windows [32] = 32 bytes. The difference is 84,256, which is exactly the finding's figure.
- **No space, then a 70,000-byte name:** windows [32,256,2048,16384,65568] = 84,288 bytes. The remark says "one probe where there is none".
- **1 space, then a 69,002-byte string:** windows [32,256,2048,16384,65568] = 84,288 bytes.
- **28 spaces, then `xref` (keyword ending at byte 32):** windows [32,256] = 288 bytes, 10.29 times the run.
- **27 spaces, then `xref`:** windows [32] = 32 bytes.
- **25 spaces, then `1234567890 0 obj`:** windows [32,256] = 288 bytes, 11.52 times the run.
- **Control, white space alone holds the ratio:** 2,048 spaces, then `xref`, gives [32,256,2048,16384] = 18,720 bytes (9.14 times). 60,000 spaces gives [32,256,2048,16384,65568] = 84,288 bytes (1.40 times).

History of the text:
- `git show b4ab948` has the PaddingAt remark "so that the bytes read are less than a seventh more than the last window holds", with the same `first.End >= probe.Length` growth.
- f0d90c8 rewrote it to the ten-times claim.
- main 21a0298's threat-model.md:448-450 already says "about nine times the run it crosses", and main's probe also grows on a cut first token.
## VERDICT fix
Reword PaddingAt's remark at src/AdCodicem.Pdf/IO/PdfFileReader.cs:5144-5146 to bound what the probe crosses up to the first token's end: "The windows grow eightfold from XRefProbeLength bytes, so that the bytes read come to less than ten times those from the position to the first token's end, white space, comments and the token alike, and to one probe where that token ends within the first."

Give the threat-model #47 bullet (docs/threat-model.md:463) the same bound: "about nine times what it crosses up to the end of the first token past the offset". That bullet's wording predates the batch.

Both are wording changes only, with no change in behavior. This is not a design choice for the maintainer.

######## [18] lens=docs key=commit-bodies-false-f0d90c8-8ba4589 sev=nit -> confirmed/nit maint=False
## location
commits f0d90c8 and 8ba4589 (pushed; for status.md's 'Commit bodies not rewritten')
## claim
Two of the round's 22 commit bodies are false in part. f0d90c8 says white space past a trailer's window reaches the guard 'and the section is looked for nearby as before'. The section startxref names is not looked for nearby: the index is rebuilt, as R4e's own deviation noted. 8ba4589 says 'The limit's remarks, the limit.trailer summary and the reference rows said it was always looked for near the offset'. Before 8ba4589, the LimitTrailer summary said nothing of white space, and reference/diagnostics.md:141 already said 'the index rebuilt for the one startxref names' (8ba4589 does not touch that file). The other 20 bodies are true of their diffs as far as checked.
## evidence
CrossReferenceRuleTests.A_startxref_naming_more_white_space_than_a_trailer_s_window_before_a_classic_table_reaches_the_guard asserts Diagnostics codes = [limit.trailer, xref.rebuilt] and passes at 8ba4589, run with A_prev_naming_more_white_space_than_a_trailer_s_window_reaches_the_guard_where_it_starts_and_is_looked_for_nearby: 6 succeeded, 0 failed. `git show 7419e37:src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs` lines 329-333 give the LimitTrailer summary as 'A trailer, or a cross-reference stream's dictionary, is longer than MaxTrailerLength, and only the part within it was parsed.', which has no white-space clause. `git show --stat 8ba4589` lists no reference/diagnostics.md.
## fix
Do not rewrite the pushed commits. Under status.md's 'Commit bodies not rewritten', add these two lines. f0d90c8: 'looked for nearby as before' holds for a /Prev or an /XRefStm; startxref's section is rebuilt. 8ba4589: the LimitTrailer summary had no white-space clause before it; only the MaxTrailerLength remarks and reference/reader-limits.md said 'near the offset'.
## VERDICT reason
Both bodies are false in part, as the finding says. f0d90c8's body says the section past a run of white space "is looked for nearby as before". The "as before" holds, because the behavior is unchanged from b4ab948 and from main. "Looked for nearby" does not hold for the section startxref names: that one is rebuilt. f0d90c8 itself wrote that exception into reference/diagnostics.md's limit.trailer row ("the index rebuilt for the one `startxref` names"), and R4e's deviation calls the same wording in the remarks and on reader-limits.md "wrong for the section startxref names". 8ba4589's body says "the limit.trailer summary ... said it was always looked for near the offset". Before 8ba4589 the LimitTrailer XML summary said nothing about white space at all. R4e's own report says the summary "also now mentions the white-space case". The diagnostics.md row with that code already carried the startxref exception, so the sentence is false whichever "summary" is meant. The remark about "the reference rows" is true only of reference/reader-limits.md's table row and note. Neither entry is in the 'Commit bodies not rewritten' list that decisions.md has built so far (e67fef9, b09f981, 5656c64, 394028c, d63f093, 7c0748b, 2b26d18, ea348ce, 8ccd868). The code and the current docs are right, so only the status.md record is affected.
## VERDICT evidence
At 8ba4589, PdfFileReader.cs:3322-3331 is the code path. When TryReadXRefSection is not Read and `sections == 1` (startxref), it calls NoSectionWhereAnotherType(section) and returns false, which rebuilds the index without trying TryRelocateXRefSection. The white-space guard at :5099-5112 calls ReachLimit and sets NotFound, and it never sets CutByLimit, so a /Prev or an /XRefStm in the same case goes on to the search near the offset (:3338-3340 and :3512-3514). I built Release at 8ba4589 and ran CrossReferenceRuleTests.A_startxref_naming_more_white_space_than_a_trailer_s_window_before_a_classic_table_reaches_the_guard together with CrossReferenceChainTests.A_prev_naming_more_white_space_than_a_trailer_s_window_reaches_the_guard_where_it_starts_and_is_looked_for_nearby: 6 succeeded, 0 failed. The startxref test asserts Diagnostics codes = [limit.trailer, xref.rebuilt]. `git show 8ba4589^:src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs` gives the LimitTrailer summary at ee33155 as "A trailer, or a cross-reference stream's dictionary, is longer than MaxTrailerLength, and only the part within it was parsed." It has no white-space clause, and `git log 21a0298..8ba4589` on that file shows that only 8ba4589 touched it in round 4. `git show f0d90c8:docs/website/docs/reference/diagnostics.md` ends the limit.trailer row with "the section is looked for nearby as one that is not there, the index rebuilt for the one `startxref` names". In r4-fix-result.json, R4e reports: "R4a's wording 'the section is looked for near the offset' is wrong for the section startxref names, which is rebuilt instead, as reference/diagnostics.md:141 already said. I corrected it in ... the LimitTrailer summary (which also now mentions the white-space case)".
## VERDICT fix
Do not rewrite the pushed commits. Add two lines under status.md's 'Commit bodies not rewritten', next to 2b26d18, ea348ce and 8ccd868. For f0d90c8: "'the section is looked for nearby as before' holds for a /Prev or an /XRefStm; the section startxref names is rebuilt, as its own reference/diagnostics.md row said". For 8ba4589: "before it, the LimitTrailer summary said nothing of white space; only the MaxTrailerLength remarks and reference/reader-limits.md said 'near the offset'". The code and docs need no change.
