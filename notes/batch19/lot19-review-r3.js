export const meta = {
  name: 'lot19-review-r3',
  description: 'Adversarial review round 3 of batch 19: the round 2 fixes (761ce0b..19122f0) under five lenses with the whole batch in view, each finding put to a refuter',
  phases: [
    { title: 'Review', detail: 'one reviewer per lens over 761ce0b..19122f0, the whole batch 21a0298..19122f0 in view' },
    { title: 'Verify', detail: 'a refuter per finding' },
  ],
}

const SP = '/tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19'
const BRIEF = `Read ${SP}/review-brief.md first and follow it (the branch head is now 19122f0). This is review ROUND 3. Round 1 reviewed 21a0298..071f829 (findings ${SP}/r1-findings.md); its fixes are 071f829..761ce0b. Round 2 reviewed those fixes and the batch (findings and refuters' verdicts in ${SP}/r2-findings.md, '######## [n]'); decisions.md's sections 'Review round 1' and 'Review round 2' say how each finding was settled, and which were filed (#320 to #330) or commented. The round 2 fixes are 761ce0b..19122f0 (15 commits: \`git log 761ce0b..19122f0\`); their stages' reports, deviations and open points are in ${SP}/r2-fix-result.json. Review those fixes first and hardest: did each fix what its finding said, completely, without breaking what the batch decided (R1-Q1: a declared unreadable candidate taken as found moved; B19-1; Q9's one notion of reached); is each new test real; did a fix open a new path (a regression against 761ce0b or main 21a0298: a section no longer read, an older revision served, a report doubled or missing, read-order dependence, unbounded cost, a guard's cut blamed on the file). The two wording gaps R2b left (a stream read in part whose one row it refused itself; a table read in part that refused a row of its own) are known: confirm or narrow them, do not re-hunt. Then anything in the whole batch earlier rounds missed. Do not re-report a finding settled as refuted, filed or accepted unless the code or the docs now misstate it.`

const FINDINGS = {
  type: 'object',
  properties: {
    lens: { type: 'string' },
    findings: { type: 'array', items: { type: 'object', properties: {
      key: { type: 'string' }, severity: { type: 'string', enum: ['blocker', 'major', 'minor', 'nit'] },
      location: { type: 'string' }, claim: { type: 'string' }, evidence: { type: 'string' }, fix: { type: 'string' } },
      required: ['key', 'severity', 'location', 'claim', 'evidence', 'fix'] } },
    open_points_judged: { type: 'array', items: { type: 'string' }, description: 'each open point of r1-fix-result.json your lens covers: fix / file / accept, with why' },
    checked_and_sound: { type: 'array', items: { type: 'string' } },
  },
  required: ['lens', 'findings', 'open_points_judged', 'checked_and_sound'],
}
const VERDICT = {
  type: 'object',
  properties: {
    key: { type: 'string' },
    verdict: { type: 'string', enum: ['confirmed', 'narrowed', 'refuted'] },
    severity: { type: 'string', enum: ['blocker', 'major', 'minor', 'nit', 'none'] },
    reason: { type: 'string' }, evidence: { type: 'string' }, fix: { type: 'string' },
    needs_maintainer: { type: 'boolean' },
  },
  required: ['key', 'verdict', 'severity', 'reason', 'evidence', 'fix', 'needs_maintainer'],
}

const LENSES = [
  { key: 'chain', prompt: `Lens 'chain': d63f093 (PaddingAt's two-entry memo, kept in read order), 394028c (leaveAt only where the /Prev keeps its place: KeepsItsPlace, DeclaresStreamAt, a look that indexes nothing and reports nothing; LeaveReadingToPrevious: the /Prev's search takes what the /XRefStm read in the stream's place and reads it whole), b9de553 (IsTokenOnItsLine: a word xref in a comment or a string is no section found moved), cb22c69 (loop placement, XRefLoopTarget Named/Reached/Read, OffsetImpreciseRule counting each written offset once), d5d99f3, 7c0748b, 5656c64. Build hostile files that combine them with the hand-on, refusals, guards and hybrid records; check the look DeclaresStreamAt makes (what it reads, its bound, that it reports and keeps nothing, its cost on many tables); the memo under adversarial orders of namings (bytes read, never wrong); IsTokenOnItsLine's limits (a line longer than its window, CR alone, a string or comment opened before the window). Compare with 761ce0b and main where a shape reads differently.` },
  { key: 'sections', prompt: `Lens 'sections': 95d8e0f and 930d26f (EveryNumberRefusedBefore, RowsRefusedBefore, the read-in-part and lost-section tails by what newer sections refused; LoseRows joining on either side; the table cut before its first row an /XRefStm names; the R1-Q2 theory's rule column). Check each against its finding; build sections read in part under every kind of newer refusal (every number, a range covering some or all of its rows, a row refused alone), as startxref's, /Prev's and /XRefStm's, under guards; confirm or narrow R2b's two known wording gaps.` },
  { key: 'headers', prompt: `Lens 'headers': the batch's header recognition as it stands at 19122f0 (TryFindObjectHeader's window, TryReadHeaderBackwards and HeaderNumbers, the rebuild's scan, FindSectionCandidates with IsTokenOnItsLine, HeaderText/LineTokens/EndObjTail copies and the stream-end search, StreamEndSearchBenchmarks, ObjectScanBenchmarks). Round 2 found nothing major here; hunt for what rounds 1 and 2 missed: IsTokenOnItsLine against the scan's own notion of a line and of a comment; a split of reads still changing a stream's served data or report; a cost on a file built for it (run the benchmarks ShortRun against 761ce0b where the code changed).` },
  { key: 'validator-tests', prompt: `Lens 'validator-tests': ChainLoopRule's messages by XRefLoopTarget, OffsetImpreciseRule counting each written offset once, HybridStreamWrongRule's texts after R2a, and every test the round added or changed (git diff 761ce0b..19122f0 -- tests/): does each hold what its name says (revert the src lines it targets on a copy and run it), are names behaviors, are new members not private covered (internal seams like CutShortSource.CutWhenReadAt), any review or measurement key in tests. Run ReaderTimingTests and the new HostileInputTests budgets 5 times each, and 3 times under four busy loops, and report.` },
  { key: 'docs', prompt: `Lens 'docs': 1bdbf98, b88ee54, 19122f0, 734487e, 46e4796 and every doc and XML doc the round changed (git diff 761ce0b..19122f0 -- docs src: the threat model rows ~413-427 and the [#47] gap, concepts/lazy-reading.md, concepts/reader-limits.md, reference/diagnostics.md rows 110-117 and 134, validation-rules.md rows 53-60, the ReservedTypeRule remarks, PdfDiagnosticCodes). Check each statement against the code (probe it), the decisions and the issues; links defined; American spelling; Diátaxis; the round's commit messages (conventional, subject at most 100, no issue number outside the footer, no review key, each body true of its diff).` },
]

phase('Review')
const results = await pipeline(LENSES,
  l => agent(`${BRIEF}\n\n${l.prompt}\n\nBe thorough and concrete: every finding needs a reproducer you ran.`, { label: `review:${l.key}`, phase: 'Review', schema: FINDINGS, isolation: 'worktree' }),
  (r, l) => r ? parallel(r.findings.map(f => () => agent(`${BRIEF}\n\nYou are a refuter. A reviewer (lens '${l.key}') claims the finding below about batch 19 at 19122f0. Try to REFUTE it: rebuild its reproducer yourself on origin/ccr-6d6c0ee6-dqm4lm-188 (and on 761ce0b or main 21a0298 if it claims a difference), check its claim against the code, the decisions in decisions.md, r1-findings.md, r2-findings.md and the issues. If it holds, say so, narrow it if it is wider than the evidence, set the severity (blocker/major/minor/nit, or none if refuted), and give the fix you would make (say if it is a design choice the maintainer must make). Default to refuted when the evidence does not reproduce.\n\nFinding:\n${JSON.stringify(f, null, 1)}`, { label: `verify:${l.key}:${f.key}`.slice(0, 60), phase: 'Verify', schema: VERDICT, isolation: 'worktree' }).then(v => ({ lens: l.key, finding: f, verdict: v })))).then(vs => ({ review: r, verified: vs })) : null
)
return { results }
