export const meta = {
  name: 'lot19-adr53-settle',
  description: 'Batch 19 ADR 53: settle a batch of survivors (kill with a test, class equivalent, or file as debt), then merge, confirm with the harness and push',
  phases: [
    { title: 'Settle', detail: 'one agent per group of survivors, at most POOL at once, tests as local patches' },
    { title: 'Merge', detail: 'apply the patches on the branch, build, run, confirm the kills with the harness, push' },
  ],
}

const W = 'C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud'
const SP = `${W}/lot19`
const S = `${W}/settle`
const A = `${W}/adr53`
const BR = 'ccr-6d6c0ee6-dqm4lm-188'
const BATCH = args.batch
const POOL = args.pool || 3
const RUNNING = args.running !== false

const ENV = `Environment: Windows 11, Git Bash (POSIX sh) and PowerShell; .NET SDK 10.0.404; Python 3.14 (\`python -I\`). Always \`export DOTNET_CLI_UI_LANGUAGE=en-US\`. Kill a process only by its PID, never by name or pattern. Never touch the maintainer's checkout C:/Users/clemm/repos/AdCodicem.Pdf (reading its tests/corpus/remote to hard-link it is fine). ${RUNNING ? 'The batch-wide mutation run is going on this machine at the same time (8 workers): never run the timing, budget, allocation or memory tests (ReaderTimingTests, HostileInputMemoryTests, any *Budget*/*budget*/*llocat* method), never run a benchmark, never two builds at once in your worktree, and build with -m:2.' : 'Never run the timing, budget, allocation or memory tests beside another dotnet process of yours.'}
Build and run: \`dotnet build tests/AdCodicem.Pdf.Tests/AdCodicem.Pdf.Tests.csproj -c Release -m:2\` (TreatWarningsAsErrors is on: 0 warnings), then the test executable directly: \`tests/AdCodicem.Pdf.Tests/bin/Release/net10.0/AdCodicem.Pdf.Tests.exe --filter-class AdCodicem.Pdf.Tests.<Class>\` or \`--filter-method '*<Name>*'\` (xUnit v3; its summary line gives total, failed, succeeded, skipped). The remote corpus: some tests read tests/corpus/remote; hard-link it with \`cp -al C:/Users/clemm/repos/AdCodicem.Pdf/tests/corpus/remote tests/corpus/remote\` when a test you run needs it, and remove that folder (\`rm -rf tests/corpus/remote\`, which removes only the links) before you finish.`

const RULES = `ADR 53 (CLAUDE.md 'Mutation testing', docs/adr/0053-*.md): a survivor is a gap in the tests, not a finding. Each is settled one of three ways, and never reopened:
- KILLED by a new test: a test that fails with the mutant applied and passes on the head, asserting the behavior the mutant changes (an exact value, an exact (Code, Position) list with Should().Equal, an exact section list), not the mutant's text;
- EQUIVALENT: no input can tell the mutant from the code. One line that holds for EVERY input, checked by reading every read and write of what the site touches (a field written before any read of it, a capacity no path reaches, a sentinel compared only by sign, a bound a guard earlier on the path already enforces). 'No test of the corpus reaches it' is never a reason;
- DEBT: only an input no test can build tells it apart (say which input and why a test cannot build it: a file past the size a test can write, a state only a concurrent caller reaches, a branch an earlier check makes unreachable for every file but not provably so). These go into the batch-wide survivor issue later.
A mutant the harness wrote SURVIVED with '[unconfirmed failures: X]' saw test X fail once in the whole-suite step, then pass when run again: run X alone with the mutant applied three times. If it fails all three, the mutant is KILLED by an existing test (say which, and that the harness's confirm run missed it: then check X on the head three times too and say whether it is flaky). Otherwise settle the mutant as any other survivor.
Tests: xUnit v3, AwesomeAssertions; put each test in the existing test class that holds the behavior (find it with the site's member and the classes in the mutant's filter list; reuse the batch's builders and TestSupport's HandWrittenFile); a test name states a behavior and is true of every row; a theory's rows each say something. Internal members may be tested directly when no document reaches them (CLAUDE.md 'Coverage'): a private member is not tested by reflection. Match the class's style, comment density and wrapping. American spelling.
NEVER write in src/, tests/ or docs/ any key of the review or of this step: mutant ids (HT-0024, EOTC-0027 and the like), the words 'mutant', 'survivor', 'surviving', 'kill', 'killed' in a test name or comment, finding numbers, stage labels, scratchpad paths, probe file names. Name behaviors in plain words.
Commits: conventional (\`test:\` here), subject lowercase after the type, at most 100 characters, no issue number in the subject or body, a body wrapped under 80 columns saying what the tests pin and why, true of the diff; footer exactly (message written to a file with LF endings, \`git commit -F <file>\`):
Refs #N[, #M]

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
The Refs: the issue(s) the site's code pays, found from the batch commits that wrote its line (\`git log --format='%h %s%n%b' 21a0298..HEAD -L<line>,<line>:<file>\`, their Refs footers). Check your message with \`python -I ${SP}/tools/check_msgs.py <file>\` (it reads \`git log --format='%x01%h%x00%B' <base>..HEAD\` output) and your diff with \`python -I ${SP}/tools/grep_keys.py <diff>\` and \`python -I ${SP}/tools/widegrep.py <diff>\`.`

const HELPER = `Helper: \`python -I ${S}/mutant.py - <ID>\` prints a mutant (file, old, new, member, site, first-filter classes); \`python -I ${S}/mutant.py . <ID>\` applies it in the current worktree (one exact replacement, line endings kept); \`python -I ${S}/mutant.py . <ID> --revert\` restores the file from git. The harness's logs for each mutant are in ${A}/logs/<shard>/<ID>-{build,first,confirm,suite}.txt and its line in ${A}/summary-<shard>.txt (shard = the id's shard in ${A}/shards).`

const GROUP = (g, base) => `You settle surviving mutants of the batch-wide ADR 53 step of batch 19 of milestone M02 of AdCodicem.Pdf (.NET 10 / C# 14 PDF library; CLAUDE.md is loaded: follow its invariants and conventions exactly). The batch pays #156, #188, #197, #207, #208, #210, #221, #222, #241, #261, #264, #281 and #317 and started from main at 21a0298; the mutation run is at 19a2296, where src/ is the same as on the branch head you start from (only tests and docs may have moved since).
${ENV}
${RULES}
${HELPER}
Git: your worktree is fresh. Run \`git fetch origin ${BR}\` then \`git checkout --detach origin/${BR}\`; record \`git rev-parse --short HEAD\` as your base (expected ${base}). Commit locally, ONE commit for your group's tests (none if no test is needed), never push. When done, write the patch: \`mkdir -p ${S}/patches/${g.label} && git format-patch -1 -o ${S}/patches/${g.label}/ HEAD\` (only if you committed). Leave src/ exactly as the base has it (revert every mutant you applied; \`git status --short\` clean apart from your commit).
Your group: ${g.label}. Read it from ${S}/${BATCH}-args.json: the entry of "groups" whose "label" is "${g.label}" gives its file, its members and its survivors, each with its id, site, member, its replacement (old -> new) and the harness's summary line.
Work: read the member(s) whole and every use of what each site touches; settle each survivor; for each test you write, show it fails with each mutant it is meant to settle applied (one at a time) and passes on the base; run the whole class you added to on the base. Several survivors may fall to one test, or be equivalent for one reason: say so per id.
Return the structured report: per survivor its fate (KILLED-NEW with the test's full name, KILLED-EXISTING with the test and the three runs, EQUIVALENT with its one-line reason, DEBT with the input and why no test builds it), the tests added (class, name, rows, what each pins), the commit (sha, subject, Refs) and the patch path, the commands run with their counts (each mutant applied: which tests failed), and anything found on the way (a real fault the mutant reveals in the code: describe it with a reproducer and do NOT fix src/; a stale comment; a flaky test).`

const MERGE = (results, base) => `You merge the tests that settle a batch of surviving mutants of the batch-wide ADR 53 step of batch 19 of milestone M02 of AdCodicem.Pdf (CLAUDE.md is loaded). The mutation run is at 19a2296; the group agents worked from base ${base}.
${ENV}
${RULES}
${HELPER}
Git: your worktree is fresh. \`git fetch origin ${BR}\`, \`git checkout --detach origin/${BR}\`, note the head. Apply every patch under ${S}/patches/${BATCH}-*/ in label order with \`git am -3\` (keep each commit as written; resolve a conflict by keeping both sides' tests, in the class's order, and say so). ${args.extra ? `Then: ${args.extra}` : ''}
The group reports (each survivor's fate, tests, patch): ${JSON.stringify(results)}
Checks, in order: \`dotnet build AdCodicem.Pdf.slnx -c Release -m:2\` with 0 warnings; \`dotnet format AdCodicem.Pdf.slnx --verify-no-changes\`; every test class the patches touched, whole, with the remote corpus hard-linked (then removed); check_msgs over the new commits (\`git log --format='%x01%h%x00%B' <head-before>..HEAD > f\`), grep_keys and widegrep over \`git diff <head-before> HEAD\`. Fix what fails in a new \`test:\` commit (never rewrite pushed history; amending a commit you have not pushed is fine).
Confirm the kills with the harness at your new head: collect the ids settled KILLED-NEW, and the classes their tests are in; \`python -I ${S}/mutant.py --confirm ${S}/${BATCH}-confirm.json ${BATCH} <full new head sha> <ids,comma-separated> --extra <classes>\`; then \`timeout 7200 python -I ${A}/run.py ${S}/${BATCH}-confirm.json <new head sha> 2 --any-commit --stop-on-fail --repo <your worktree>\` (it writes ${A}/summary-${BATCH}.txt; read the harness's docstring first). Every id must read KILLED; one that does not goes back to its group's reasoning: fix the test in a new commit and run the harness again with \`--ids <id> --rerun\`.
Then push: \`git push origin HEAD:${BR}\` (fast-forward; on a network error retry up to 4 times, waiting 2, 4, 8, 16 s; if the branch moved, \`git fetch\` and rebase your unpushed commits on it, then run the build and the touched classes again; never force).
Return: the head before and after, each commit (sha, subject, Refs, pushed), the confirm summary (each id and its line), the tests run with counts, and every problem or deviation.`

const GROUP_REPORT = {
  type: 'object',
  properties: {
    label: { type: 'string' },
    base: { type: 'string' },
    survivors: { type: 'array', items: { type: 'object', properties: { id: { type: 'string' }, fate: { type: 'string', enum: ['KILLED-NEW', 'KILLED-EXISTING', 'EQUIVALENT', 'DEBT'] }, test: { type: 'string' }, reason: { type: 'string' } }, required: ['id', 'fate', 'reason'] } },
    tests_added: { type: 'array', items: { type: 'string' } },
    commit: { type: 'string' },
    patch: { type: 'string' },
    commands: { type: 'array', items: { type: 'string' } },
    found: { type: 'array', items: { type: 'string' } },
  },
  required: ['label', 'base', 'survivors', 'tests_added', 'commit', 'patch', 'commands', 'found'],
}
const MERGE_REPORT = {
  type: 'object',
  properties: {
    head_before: { type: 'string' }, head_after: { type: 'string' },
    commits: { type: 'array', items: { type: 'string' } },
    confirm: { type: 'array', items: { type: 'string' } },
    tests_run: { type: 'array', items: { type: 'string' } },
    problems: { type: 'array', items: { type: 'string' } },
  },
  required: ['head_before', 'head_after', 'commits', 'confirm', 'tests_run', 'problems'],
}

async function pool(items, n, fn) {
  const out = new Array(items.length)
  let next = 0
  const lanes = Array.from({ length: Math.min(n, items.length) }, async () => {
    while (next < items.length) {
      const k = next++
      out[k] = await fn(items[k], k)
    }
  })
  await Promise.all(lanes)
  return out
}

const base = args.base
phase('Settle')
const groups = args.labels.map(label => ({ label }))
const reports = await pool(groups, POOL, g =>
  agent(GROUP(g, base), { label: `settle-${g.label}`, phase: 'Settle', schema: GROUP_REPORT, isolation: 'worktree' }))
const done = reports.filter(Boolean)
log(`${done.length} of ${groups.length} groups reported`)
const missing = groups.filter((g, k) => !reports[k]).map(g => g.label)
if (missing.length) log(`groups with no report: ${missing.join(', ')}`)

phase('Merge')
const merged = await agent(MERGE(done, base), { label: `merge-${BATCH}`, phase: 'Merge', schema: MERGE_REPORT, isolation: 'worktree' })
return { batch: BATCH, reports: done, missing, merge: merged }
