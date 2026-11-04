# Batch 19 — measurement brief (main 21a0298)

Batch 19 of M02's step 4 (docs/status.md, *Next concrete step*, item 19): the chain's reading of where its
cross-reference sections lie, and what it reports of a section it reads only in part.

Issues (read each with its comments through the GitHub tools: owner AdCodicem, repo AdCodicem.Pdf,
mcp__github__issue_read, methods get and get_comments; load the tool with ToolSearch "select:mcp__github__issue_read"):

| Issue | Subject | Lens |
|---|---|---|
| #188 | relocation lands on a section the chain already read (false loop/shift; hybrid table taken for its own /XRefStm) | relocation |
| #281 | a section that is there and malformed is relocated onto a newer one already read, reported as a loop | relocation |
| #197 | near the file's start the nearby search (section and object) reaches 1,024 bytes past the offset, not 512 | window |
| #261 | neither the search near a row nor the rebuild's scan reads an object header with a comment inside it | window |
| #207 | a table the end of the file cuts before its trailer reads as sound in silence (`_xref.Count > 0` decides) | partial |
| #210 | a section read only in part (last row no row before the trailer; stream data/`/Index` short) reported nowhere; also RefusedRow's reader report (batch 5 comment) | partial |
| #222 | a classic section with no subsection (`xref` then `trailer`) reads as sound in silence | partial |
| #208 | the chain stops at a cross-reference stream whose rows cannot be read though its dictionary names /Prev | stream-prev |
| #156 | an /XRefStm naming a section already read leaves no record; file.size-wrong; /XRefStm naming anything but a stream | xrefstm |
| #221 | a cross-reference stream row of a reserved type (> 2) is skipped, where ISO 32000-1 7.5.8.3 reads null | reserved |
| #264 | a section a guard stops (MaxXRefSectionLength, full index, MaxDecodedStreamLength) judges no fault of its /Prev (shapes F, G; E1 paid by batch 16) | guards |
| #241 | messages put a count of one in the plural, a distance back as a negative count; shared helper for reader and validator | messages |
| #317 | ReaderTimingTests' object-stream linear-time row runs past its bound of 8 (CI, .NET 10 and 11) | flaky |

Code: src/AdCodicem.Pdf/IO/PdfFileReader.cs — TryReadXRefChain (~2936), ReportLostSection/ReportMissingSection
(~3150-3220), TryRelocateXRefSection (~3240), FindSectionCandidates, TryReadXRefSection (~3384), TryReadClassicTable
(~3429), ReadTrailerPastRows (~3641), ReadSubsection (~3887), TryReadXRefStream (~3976), SectionOffset (~5171),
ReadXRefStreamRows (~5264), TryFindObjectHeader (~6053-6110), TryReadHeaderBackwards (~6294), the rebuild's scan
(~6933); src/AdCodicem.Pdf/IO/XRef/XRefSectionRecord.cs, FileStructure.cs; src/AdCodicem.Pdf/Validation/ (rules under
Rules/, CrossReferenceProbe.cs, RuleText.cs); PdfDiagnosticCodes. Docs: docs/threat-model.md (*Indexing* rows),
docs/website/docs/reference/diagnostics.md, validation-rules.md, concepts/lazy-reading.md, docs/adr/ (ADR 34 guards,
ADR 45 severities). Tests: tests/AdCodicem.Pdf.Tests (CrossReferenceChainTests, CrossReferenceRuleTests,
DocumentReaderTests, FileRuleTests, ReaderLimitsTests, GuardCutValueTests, HostileInputTests, ReaderTimingTests),
fixtures in tests/AdCodicem.Pdf.TestSupport (ChainFiles, etc.).

## Conventions for every measurement agent

- You work in your own git worktree (the Workflow gave you one, at main 21a0298). Do not touch
  /home/user/AdCodicem.Pdf itself; do not commit, push, or comment on GitHub.
- Build Release: `dotnet build AdCodicem.Pdf.slnx -c Release` (or the test project only). The machine has 4 CPUs and
  another agent builds beside you: use `-m:2` if you build the solution, and never run two builds of your own at once.
- Probes are uncommitted test files named Zz*.cs in tests/AdCodicem.Pdf.Tests, run with
  `dotnet test --project tests/AdCodicem.Pdf.Tests/AdCodicem.Pdf.Tests.csproj -c Release --no-build -- --filter-method "*ZzName*"`.
  Copy every probe you write to your output folder as `<name>.cs.txt` so it survives the worktree.
- Reproduce each shape the issue gives on main first, twice, and say whether it matches the issue's text today (code
  moved since the issue was filed: batches 5 to 18 touched these paths; some shapes may be paid or changed).
- Prototype the fix(es) the issue proposes, and any alternative you judge better, in the worktree. For each: the
  shapes' results after it, the full unit suite's result (`dotnet test --project tests/AdCodicem.Pdf.Tests/... -c Release`,
  counts and every failure with its reason), and, for a change to what the reader or validator reports, the corpus
  diff: the probe /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot18/r9code/walks/ZzCorpusDump.cs.txt
  (copy it into tests/AdCodicem.Pdf.Tests/ZzCorpusDump.cs; ZZ_CORPUS_OUT=<file>) run on main and on the prototype,
  compared with scratchpad/lot18/verify/cmp_corpus.py (python3 -I cmp_corpus.py main.txt proto.txt). To include the
  235 remote documents, symlink them first: `ln -s /home/user/AdCodicem.Pdf/tests/corpus/remote tests/corpus/remote`
  inside your worktree (the manifest lists them; Corpus.Documents takes those present).
- Save your prototype as a diff (`git diff > <output>/<name>.diff`) and a short report. The output folder is
  /tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/measure/<lens>/.
- Never run `pkill`/`pgrep -f` with a pattern that appears in the same command line. Kill a stuck dotnet by PID.
- A question for the maintainer is a real choice the code cannot settle: give each its options, what each implies
  (behavior, codes, severities under ADR 45, cost, tests, docs), and the one you recommend, with its reason. What the
  measurement settles is "decided without a question", with the evidence.
- Everything you write is in English. Cite file:line at main 21a0298.
