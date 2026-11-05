export const meta = {
  name: 'lot19-review-r2',
  description: 'Adversarial review round 2 of batch 19: the round 1 fixes (071f829..761ce0b) under five lenses with the whole batch in view, each finding put to a refuter',
  phases: [
    { title: 'Review', detail: 'one reviewer per lens over 071f829..761ce0b, the whole batch 21a0298..761ce0b in view' },
    { title: 'Verify', detail: 'a refuter per finding' },
  ],
}

const SP = '/tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19'
const BRIEF = `Read ${SP}/review-brief.md first and follow it (the branch head is now 761ce0b). This is review ROUND 2. Round 1 reviewed 21a0298..071f829; its findings and the refuters' verdicts are in ${SP}/r1-findings.md ('######## [n]'), and decisions.md's section 'Review round 1' says how each was settled (R1-Q1 to R1-Q4 and the fixes decided without a question) and which were filed (#329, #330) or commented (#171, #260, #320, #322). The round 1 fixes are 071f829..761ce0b (12 commits: \`git log 071f829..761ce0b\`); their stages' reports, deviations and open points are in ${SP}/r1-fix-result.json (keys reports[].deviations, reports[].open_points). Review those fixes first and hardest: did each fix what its finding said, completely, without breaking what the batch decided; is each new test real (would it fail without the fix); did a fix open a new path (doubled reports, read-order dependence, unbounded cost, a guard's cut blamed on the file). Then anything in the whole batch round 1 missed. Do not re-report a round 1 finding settled as 'refuted', filed as debt or accepted in decisions.md, unless the code or the docs now misstate it. Debts filed: #320 to #330.`

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
  { key: 'chain', prompt: `Lens 'chain': c29761c (a section known by where its first token starts: StartOf/StartOfNamed, ReachedBefore, the naming map, lost and handed-on streams recorded, branch (c) by positions, OffsetImpreciseRule) and 0353e24 (relocation stops at the nearest declared unreadable candidate, taken as found moved; messages naming the offset named and where found; startxref's object of another /Type; the hand-on message; branch (c)'s texts; an /XRefStm naming a malformed table). Combine them with the rest of the chain (hand-on, refusals, guards, hybrid records, the section count guard) in hostile files: white space and comments before sections at several namings, relocations onto sections found moved, declared candidates near the offset beside readable ones, loops through each kind of naming, headers not at offset 0, and the cost of StartOf on many namings (invariant 4: a chain of MaxXRefSectionCount links each naming white space; allocation and time).` },
  { key: 'sections', prompt: `Lens 'sections': 0fc7ce5 (a classic table an /XRefStm names that cannot be read refuses what it lost and every number where the rest cannot be known; SubsectionsUnknown; read-in-part tails, RowsHeld, RowsLeftToGuard; the trailer prefix at the end of the file; a table a rows guard cuts before its first row; LeadsPastIndex with the refused ranges and RefuseRowsLost's order; LoseRows joining ranges; SizeWrongRule and loops). Check each against its finding and the decisions; build sections with several faults, under each guard, as startxref's, /Prev's and /XRefStm's, and with refusals from newer sections; the open point that a section read in part after a newer section refused every number still says 'the rows read were kept'.` },
  { key: 'headers', prompt: `Lens 'headers': b4cfda0 (EndObjTail copies at carried number starts, EndObjTail.Seen replacing HoldsEndObj, each read fed to the tail only up to the next read's start), 33b243f (StreamEndSearchBenchmarks, ObjectScanBenchmarks, the linearity timing test), 2febbac (the property holding the stream-end header rule to the scan's header; TryReadHeaderBackwards made internal), 51d47af. Hunt for a split of the reads that still changes the served data, the code or the endobj judgment (sweep the window edge, the first read's edge, MaxObjectLength, CR/LF splits, comments with endobj, several carried numbers); a cost the copies add (bounded? allocation-free?); whether the benchmarks measure what they claim and run; whether the timing test is robust (run it 10 times and 5 under four busy loops) and fails on a quadratic mutant; whether the property is strong enough to catch a scan/rule divergence (try a mutant of each).` },
  { key: 'validator-tests', prompt: `Lens 'validator-tests': de0fa05, 0e974e6, 23121f6, e548beb, and every test the round added or changed (git diff 071f829..761ce0b -- tests/). For #317's test (four probes a turn) and the lost streams' bound 12: run each 10 times alone and 5 times under four busy loops with the combined filter (the class filters that run the batch's timing and allocation tests in one process) and report the ratios; check the header-scan mutant (${SP}/measure/flaky/mutants/M1-scan-indexof.diff) still fails #317's test. For the round's other tests: does each hold what its name says (revert the src lines it targets on a copy and run it); are names behaviors; are new members covered; any review or measurement key in tests (round 1 finding numbers, R1-Q, shape names).` },
  { key: 'docs', prompt: `Lens 'docs': 761ce0b and every doc and XML doc the round changed (git diff 071f829..761ce0b -- docs src | the XML docs and remarks): ADR 34's scoped Decision bullet and amendment, the threat model (rows ~414-427, the Known gaps for #171, #323, #329, #330), concepts (lazy-reading, reader-limits, validation, diagnostics), reference (diagnostics, validation-rules, reader-limits), M02.md. Check each statement against the code (probe it), the decisions and the issues; links defined; American spelling; Diátaxis. Also the round's commit messages (\`git log --format=%B 071f829..761ce0b\`): conventional, subject at most 100, no issue number outside the footer, no review key, each body true of its diff.` },
]

phase('Review')
const results = await pipeline(LENSES,
  l => agent(`${BRIEF}\n\n${l.prompt}\n\nBe thorough and concrete: every finding needs a reproducer you ran.`, { label: `review:${l.key}`, phase: 'Review', schema: FINDINGS, isolation: 'worktree' }),
  (r, l) => r ? parallel(r.findings.map(f => () => agent(`${BRIEF}\n\nYou are a refuter. A reviewer (lens '${l.key}') claims the finding below about batch 19 at 761ce0b. Try to REFUTE it: rebuild its reproducer yourself on origin/ccr-6d6c0ee6-dqm4lm-188 (and on 071f829 or main 21a0298 if it claims a difference), check its claim against the code, the decisions in decisions.md, r1-findings.md and the issues. If it holds, say so, narrow it if it is wider than the evidence, set the severity (blocker/major/minor/nit, or none if refuted), and give the fix you would make (say if it is a design choice the maintainer must make). Default to refuted when the evidence does not reproduce.\n\nFinding:\n${JSON.stringify(f, null, 1)}`, { label: `verify:${l.key}:${f.key}`.slice(0, 60), phase: 'Verify', schema: VERDICT, isolation: 'worktree' }).then(v => ({ lens: l.key, finding: f, verdict: v })))).then(vs => ({ review: r, verified: vs })) : null
)
return { results }
