export const meta = {
  name: 'lot19-review-r1',
  description: 'Adversarial review round 1 of batch 19 (21a0298..071f829): six lenses find defects with reproducers, each finding put to a refuter',
  phases: [
    { title: 'Review', detail: 'one reviewer per lens, each in its own worktree' },
    { title: 'Verify', detail: 'a refuter per finding' },
  ],
}

const SP = '/tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19'
const BRIEF = `Read ${SP}/review-brief.md first and follow it.`

const FINDINGS = {
  type: 'object',
  properties: {
    lens: { type: 'string' },
    findings: { type: 'array', items: { type: 'object', properties: {
      key: { type: 'string' }, severity: { type: 'string', enum: ['blocker', 'major', 'minor', 'nit'] },
      location: { type: 'string' }, claim: { type: 'string' }, evidence: { type: 'string' }, fix: { type: 'string' } },
      required: ['key', 'severity', 'location', 'claim', 'evidence', 'fix'] } },
    residuals_judged: { type: 'array', items: { type: 'string' }, description: 'each residual of residuals-impl.md your lens covers: fix / file / accept, with why' },
    checked_and_sound: { type: 'array', items: { type: 'string' }, description: 'what you checked and found sound' },
  },
  required: ['lens', 'findings', 'residuals_judged', 'checked_and_sound'],
}
const VERDICT = {
  type: 'object',
  properties: {
    key: { type: 'string' },
    verdict: { type: 'string', enum: ['confirmed', 'narrowed', 'refuted'] },
    severity: { type: 'string', enum: ['blocker', 'major', 'minor', 'nit', 'none'] },
    reason: { type: 'string' },
    evidence: { type: 'string' },
    fix: { type: 'string' },
    needs_maintainer: { type: 'boolean', description: 'true when the fix is a design choice the maintainer must make' },
  },
  required: ['key', 'verdict', 'severity', 'reason', 'evidence', 'fix', 'needs_maintainer'],
}

const LENSES = [
  { key: 'chain', prompt: `Lens 'chain': PdfFileReader.TryReadXRefChain and everything it calls for where sections lie and what follows them: relocation (TryRelocateXRefSection, FindSectionCandidates, DeclaresItselfASection, the read-positions set and ReachedBefore), the loop check and xref.chain-cycle, the hand-on of a /Type /XRef stream's /Prev (#208), the hybrid branches, NamedAgain records, ReportNoStream and xref.hybrid-stream-lost (#156), the guard path (pastGuard, JudgeLinksPastGuard, _previousPastGuard, NamesOlderSection, TrailersHiddenByGuard; #264), ReportLostSection, ReportMissingSection. Build hostile chains that combine the batch's features: a relocation and a hand-on in one chain; an /XRefStm naming a section a hand-on reached; loops through positions and through offsets only named; the section count guard with hand-ons; a guard stopping a section whose /XRefStm names a relocated section; headers not at offset 0. Check every report is right, once, independent of read order, and that the index is marked incomplete exactly where something is lost.` },
  { key: 'sections', prompt: `Lens 'sections': what the reader makes of one section: TryReadClassicTable, ReadSubsection, ReadTrailerPastRows, TryReadXRefStream, ReadXRefStreamRows, the read-in-part report (ReportSectionReadInPart; #207, #210, refused rows), the refused ranges (RefuseDeclared, RefuseRanges, the classic table's pending ranges, MaxRefusedRanges, AddFromChain, RetakeDeferred, LeadsPastIndex; B19-2), the table with no subsection (xref.section-empty; #222), the reserved rows (#221: indexed as free, xref.entry-reserved, counting against MaxIndexEntries), trailer.root-recovered reported once (FindCatalog, _catalogReportedAs). Build sections with several faults at once (a reserved row and a short stream; a refused row then a bad row; a table cut by the end of the file inside its second subsection; a table with no subsection named by /XRefStm), first sections and later ones, under each guard. Check the refusal is right (no older row standing for a lost number, no number refused that the section did give), bounded (invariant 4: ranges, rentals returned to the pool on every path, exceptions included), and that what a caller reads is the same whatever the order of reads.` },
  { key: 'headers', prompt: `Lens 'headers': where an object header is recognized. The nearby window (#197: TryFindObjectHeader, TryRelocateXRefSection's window, the left edge), TryReadHeaderBackwards and its comment walk (HeaderNumbers), the rebuild's scan, FindSectionCandidates (#261); and the stream-end rule (B19-7): HeaderText (its carried runs, lead and line state), LineTokens, PdfObjectParser.IsHeader, MayBeHeaderBytes, HeaderUndecided. Hunt for: a header the lexer reads that these miss, or one they take that a load would not read (comments holding digits and 'obj', strings with '%', names, hex strings, CR alone, CRLF split across reads, a comment line longer than a read, a line starting inside a string opened earlier); differences between the scan, the nearby search and the stream-end rule; any super-linear time or allocation on a file built for it (many lines holding '%', one huge line, nested parentheses), compared with main (benchmarks/AdCodicem.Pdf.Benchmarks, ShortRun, and timing ratios); LineTokens against PdfLexer (is the property test strong enough?).` },
  { key: 'validator', prompt: `Lens 'validator': the validation side and the messages. The new and changed rules (HybridStreamWrongRule, ReservedTypeRule, SubsectionMissingRule, SizeWrongRule with its per-naming count, judgedAt, IsWhole and Incomplete, TrailerValueWrongRule for a /Prev handed on, SectionShiftedRule, PageTreePageOrphanedRule.IsIndexSound, SectionMalformedRule as it meets the new states, CrossReferenceProbe, EntryShifted/EntryBroken and the window), their ADR 45 severities, their texts, their order in the structural profile, the rule ids against the codes; and #241's CountText and every message it changed (RuleText, BrokenObjectStream, PageTreeWalk, the filter pipeline, the reader's messages): a count of one or zero, a negative or zero distance, digit grouping as each site had it, verbs and pronouns agreeing. Build files for each rule's edge (a rule that fires twice for one fault, or never; a finding that contradicts a reader diagnostic; a finding at the wrong offset or object).` },
  { key: 'tests', prompt: `Lens 'tests': the tests the batch added and changed (git diff 21a0298..origin/ccr-6d6c0ee6-dqm4lm-188 -- tests/; new files NearbyFiles.cs, LostStreamFiles.cs, PartialFiles.cs, HybridFiles.cs, StoppedSectionFiles.cs, CountTextTests and others). For each test class: does each test hold what its name says (would it pass with the fix reverted, or with a plausible wrong implementation? Try reverting the src lines it targets on a copy and run it); are the timing tests robust (#317's test, #208's timing and allocation tests, any new ReaderTimingTests or HostileInputTests budgets: run them 10 times, and 5 times under four busy loops, and report ratios); are the 'beside the same file read whole / out of reach' comparisons real; was any test weakened by the batch (an assertion removed or loosened, a spacer removed that changed what a test tests, a fixture changed so a test no longer covers its original subject — check the reserved-type fixture changes, the '+0' hiding device, the '0 0' moves, the four loop tests reshaped); are new members not private covered; do test names state behaviors; any measurement or review key in tests.` },
  { key: 'docs', prompt: `Lens 'docs': the documentation against the code. Every doc the batch changed (git diff --stat 21a0298..origin/ccr-6d6c0ee6-dqm4lm-188 -- docs src | grep -v '\\.cs ' and the XML docs and remarks of every member the batch changed): docs/threat-model.md (Indexing rows, Known gaps added and removed, the validator rows), docs/adr/0034's amendment, docs/milestones/M02.md and M04.md, docs/corpus-contributions.md, docs/website/docs/concepts (lazy-reading, diagnostics, reader-limits, validation) and reference (diagnostics, validation-rules, reader-limits), PdfDiagnosticCodes and PdfValidationRuleIds XML docs, the remarks of the changed reader methods. For each statement, check it against the code (probe it where it states behavior) and against the decisions; the test names it cites exist; [#n] links are defined; Diátaxis modes kept; American spelling; what each doc says a code's severity is matches the code. Also commit messages: \`git log --format=%B 21a0298..origin/ccr-6d6c0ee6-dqm4lm-188\` (conventional type, subject at most 100 characters, no issue number outside the footer, each body true of its diff).` },
]

phase('Review')
const results = await pipeline(LENSES,
  l => agent(`${BRIEF}\n\n${l.prompt}\n\nBe thorough and concrete: every finding needs a reproducer you ran.`, { label: `review:${l.key}`, phase: 'Review', schema: FINDINGS, isolation: 'worktree' }),
  (r, l) => r ? parallel(r.findings.map(f => () => agent(`${BRIEF}\n\nYou are a refuter. A reviewer (lens '${l.key}') claims the finding below about batch 19. Try to REFUTE it: rebuild its reproducer yourself on origin/ccr-6d6c0ee6-dqm4lm-188 (and on main 21a0298 if it claims a difference), check its claim against the code, the decisions in decisions.md and the issues. If it holds, say so, narrow it if it is wider than the evidence, set the severity (blocker/major/minor/nit, or none if refuted), and give the fix you would make (say if it is a design choice the maintainer must make). Default to refuted when the evidence does not reproduce.\n\nFinding:\n${JSON.stringify(f, null, 1)}`, { label: `verify:${l.key}:${f.key}`.slice(0, 60), phase: 'Verify', schema: VERDICT, isolation: 'worktree' }).then(v => ({ lens: l.key, finding: f, verdict: v })))) : []
)
const reviews = []
const verified = []
for (let i = 0; i < LENSES.length; i++) {
  verified.push(...(results[i] || []).filter(Boolean))
}
return { verified }
