export const meta = {
  name: 'lot19-end-texts',
  description: 'Batch 19: fix the stale texts the ADR 53 settling found (push), and fill the end-of-batch comments\' measurements (no post), each comment checked by a refuter',
  phases: [
    { title: 'Texts and measures', detail: 'one cleanup agent; one measuring agent per comment' },
    { title: 'Check', detail: 'a refuter per filled comment' },
  ],
}

const W = 'C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud'
const SP = `${W}/lot19`
const S = `${W}/settle`
const D = `${W}/status-draft`
const BR = 'ccr-6d6c0ee6-dqm4lm-188'
const MAIN = 'C:/Users/clemm/repos/AdCodicem.Pdf'

const ENV = `Environment: Windows 11 (16 cores, 32 GB), Git Bash (POSIX sh) and PowerShell; .NET SDK 10.0.404; Python 3.14 (\`python -I\`). Always \`export DOTNET_CLI_UI_LANGUAGE=en-US\` (the test executable prints in French otherwise). Kill a process only by its PID. Never touch the maintainer's checkout ${MAIN} (hard-linking its tests/corpus/remote into a worktree is fine: \`cp -al ${MAIN}/tests/corpus/remote <wt>/tests/corpus/remote\`, removed with \`rm -rf <wt>/tests/corpus/remote\` before the worktree goes). Other agents and an integration-test run share the machine: never run the timing, budget, allocation or memory tests, never a benchmark, never two builds in one worktree; build with -m:2. A scratch worktree of your own for another commit (main is 21a0298): \`git worktree add --detach <path under ${W}/endtexts/> <commit>\`, removed after with \`git worktree remove --force\` (\`rm -rf\` and \`git worktree prune\` on 'Filename too long'). The batch's src/ has not changed since 19a2296: every later head of ${BR} reads files exactly as 19a2296 does.`

const NOKEYS = `NEVER write in src/, tests/ or docs/ (nor in an issue comment) any key of the review or of the mutation step: mutant ids (HT-0024, PFR-0644, LT-0050...), finding numbers, stage or round labels, scratchpad paths, probe file names (Zz*), the words 'mutant', 'survivor', 'decoy', 'reviewer', 'refuter', 'prototype', 'variant'. American spelling.`

const CLEAN = `You make the last text fixes of batch 19 of milestone M02 of AdCodicem.Pdf (CLAUDE.md is loaded). The batch-wide ADR 53 step has just ended: its settling agents, while settling the surviving mutants, reported texts that are stale or inaccurate. Those are minors and nits under the maintainer's amended stopping rule: they get this one pass with no further review; a text that would need a behavior change to become true is NOT changed: report it instead.
${ENV}
${NOKEYS}
Sources: every "found" entry, every "problems" entry and every refuter reason in ${S}/b1-result.json, b1r-result.json, b2-result.json, b3-result.json, b4-result.json and b5-result.json (journal lines: each has a "result"), and these already noted in ${SP}/decisions.md: HeaderText's class remark and PositionOf(int)'s summary say a carried byte records 'where its run starts' (not true of the lead's bytes: the lead's first byte is placed at -1, and the space CarryNumber writes after each lead number at that number's start); ViewLength's summary describes only the runs view (the lead view it also sizes holds a byte and two numbers with a space after each, the same 23 bytes laid out otherwise); the comment in CrossReferenceChainTests.A_hybrid_table_s_own_stream_read_from_a_sign_that_cannot_be_read_is_reported_once_however_few_of_its_signs_the_window_near_its_prev_holds (stale since a later change: read it and the code). List every candidate, read the code at the head, and decide each: a text false or incomplete about what the code does (fix it), a text that is true (leave it, say why), or a code observation that is no text defect (leave it). Do not change any code line, test logic or test data; comments and XML documentation only, in src/ and tests/, matching the surrounding style and wrapping.
Git: your worktree is fresh. \`git fetch origin ${BR}\`, \`git checkout --detach origin/${BR}\`, note the head. Commit src/ texts as \`docs:\` and test comments as \`test:\` (one commit each at most), conventional subject lowercase after the type, at most 100 characters, no issue number in subject or body, a body under 80 columns true of the diff, footer exactly (message file with LF endings, \`git commit -F\`):
Refs #N[, #M]

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
(the Refs: the issue each text's code pays, from the batch commits that wrote it). Checks: \`git diff --stat\` shows only comment/XML-doc lines (say how you checked that no code line changed); \`dotnet build AdCodicem.Pdf.slnx -c Release -m:2\` 0 warnings; \`dotnet format AdCodicem.Pdf.slnx --verify-no-changes\`; \`python -I ${SP}/tools/check_msgs.py\` over \`git log --format='%x01%h%x00%B' <head>..HEAD\`; \`python -I ${SP}/tools/grep_keys.py\` and \`widegrep.py\` over the diff; \`python -I ${SP}/tools/citecheck.py\` and \`namecheck.py\` (read their heads) so every test name docs/threat-model.md and the website pages cite still exists after the batch's settling commits (the settling added tests and turned at least one Fact into a Theory; a page citing a test by name must still name a real one). Push: \`git push origin HEAD:${BR}\` (fast-forward; if the branch moved, fetch, rebase, build again; never force).
Return: each candidate with its decision and reason, the commits (sha, subject, Refs, pushed), the checks with their output.`

const MEASURE = (n) => `You complete the end-of-batch comment for issue #${n} of AdCodicem.Pdf (CLAUDE.md is loaded), drafted at ${D}/comments/${n}.md for batch 19 of milestone M02 (it is NOT posted; do not post anything: \`gh issue comment\` is forbidden; read-only gh calls are fine). The draft holds placeholders of the form {{MEASURE: ...}}: each says what to measure. Measure each on the batch's final source (any head of ${BR}: src/ = 19a2296) and, where it asks, on main 21a0298, then replace the placeholder with the result written in the draft's own style (what the reader does, the diagnostics' codes and positions, the figures), and correct every other sentence of the draft that your measurements show to be wrong or that rests on an older commit's behavior (the draft's refuter listed these as unverified: they are in ${D}/draft-result.json, the check of the comments). Keep every statement true of the final head; where a figure the draft quotes from an earlier record differs from your measurement, keep yours and say in the draft what was measured. Read the issue and all its comments first (\`gh issue view ${n} -R AdCodicem/AdCodicem.Pdf --comments\`).
${ENV}
${NOKEYS}
Probes: in your own scratch worktrees (one at origin/${BR}, one at 21a0298 where needed), write a temporary test file tests/AdCodicem.Pdf.Tests/Zz${n}Probe.cs that builds the files the placeholder describes (reuse TestSupport's HandWrittenFile and the batch's builders such as HybridFiles, LostStreamFiles, NearbyFiles where the draft names them; a counting source to count bytes read) and prints what you measure; run it with the test executable's --filter-class; never commit it, delete it after. Save each probe's source and output under ${W}/endtexts/${n}/ for the record.
Rules for the comment's text: no scratchpad path, no probe file name, no review key, finding number, mutant id, model name or pull request number; issue numbers and commit shas are fine; LF endings; the body ends exactly with "\\n\\n---\\n_Generated by [Claude Code](https://claude.ai/code)_" and nothing after it.
Return: each placeholder with what replaced it, every other sentence you changed and why, the measurements (commands, outputs, file paths), and anything you could not measure.`

const CHECK = (n) => `You check the end-of-batch comment drafted for issue #${n} of AdCodicem.Pdf at ${D}/comments/${n}.md (batch 19 of milestone M02; nothing is posted; do not post). Its measurements were just filled in; the measuring agent's probes and outputs are under ${W}/endtexts/${n}/. Try to REFUTE every statement: each behavior against the code at origin/${BR} (src/ = 19a2296) and, where the comment compares, at main 21a0298; each figure against the probe outputs (rerun a probe where a figure looks doubtful); each claim about the issue's history against the issue and its comments (\`gh issue view ${n} -R AdCodicem/AdCodicem.Pdf --comments\`); and that the comment adds what the issue does not already say. Check the rules: no scratchpad path, probe file name, review key, finding number, mutant id, model name or pull request number; LF endings; it ends exactly with "\\n\\n---\\n_Generated by [Claude Code](https://claude.ai/code)_". No {{...}} placeholder may remain.
${ENV}
Fix every defect directly in ${D}/comments/${n}.md. Return the defects and what you changed, and what you could not verify.`

const R1 = { type: 'object', properties: { decisions: { type: 'array', items: { type: 'string' } }, commits: { type: 'array', items: { type: 'string' } }, checks: { type: 'array', items: { type: 'string' } }, problems: { type: 'array', items: { type: 'string' } } }, required: ['decisions', 'commits', 'checks', 'problems'] }
const R2 = { type: 'object', properties: { placeholders: { type: 'array', items: { type: 'string' } }, changed: { type: 'array', items: { type: 'string' } }, measurements: { type: 'array', items: { type: 'string' } }, unmeasured: { type: 'array', items: { type: 'string' } } }, required: ['placeholders', 'changed', 'measurements', 'unmeasured'] }
const R3 = { type: 'object', properties: { defects: { type: 'array', items: { type: 'string' } }, unverified: { type: 'array', items: { type: 'string' } } }, required: ['defects', 'unverified'] }

const ISSUES = ['289', '295', '52', '260']
phase('Texts and measures')
const [clean, comments] = await Promise.all([
  agent(CLEAN, { label: 'clean-texts', phase: 'Texts and measures', schema: R1, isolation: 'worktree' }),
  pipeline(ISSUES,
    n => agent(MEASURE(n), { label: `measure-${n}`, phase: 'Texts and measures', schema: R2, isolation: 'worktree' }),
    (m, n) => agent(CHECK(n) + `\n\nThe measuring agent's report: ${JSON.stringify(m)}`, { label: `check-${n}`, phase: 'Check', schema: R3, isolation: 'worktree' }).then(c => ({ issue: n, measure: m, check: c }))),
])
return { clean, comments }
