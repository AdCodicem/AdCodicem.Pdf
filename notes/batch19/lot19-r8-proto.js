export const meta = {
  name: 'lot19-r8-proto',
  description: 'Prototype candidate refinements of R7-Q2 (how the search near a hybrid table\'s /Prev tells the table\'s own stream) and measure each over the round 8 shapes and a 3,000-file differential against main, d3ef236 and head',
  phases: [
    { title: 'Harness', detail: 'collect every round 8 own-stream file, write a canonical probe, baseline main, d3ef236 and head' },
    { title: 'Variants', detail: 'one agent per candidate rule, each in its own worktree' },
    { title: 'Judge', detail: 'compare the variants and recommend' },
  ],
}

const SC = '/tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad'
const SP = SC + '/lot19'
const P = SC + '/r8proto'
const COMMON = `Context: batch 19 of AdCodicem.Pdf, branch ccr-6d6c0ee6-dqm4lm-188 at 012e13d (repository /home/user/AdCodicem.Pdf; never modify that checkout, never commit, never push, never comment on GitHub; work in your own git worktree or scratch worktrees you add and remove yourself; build with \`dotnet build AdCodicem.Pdf.slnx -c Release -m:2\`; probes are uncommitted Zz*.cs test files run with \`dotnet test --project tests/AdCodicem.Pdf.Tests/AdCodicem.Pdf.Tests.csproj -c Release --no-build -- --filter-method "*ZzName*"\`; never run pkill or pgrep -f with a pattern that appears in the same command line, kill by PID). Read ${SP}/decisions.md's sections 'R7-Q2' and 'Review round 8 (wf_6e5c9222-451 on 012e13d): triage', and the findings [7], [8], [9], [11], [12], [13], [16], [26], [29] and [30] in ${SP}/r8-findings.md (python3 ${SP}/showf.py ${SP}/r8-findings.md 4000 7 8 9 11 12 13 16 26 29 30 prints them in full). R7-Q2, as the maintainer decided it: in the search near the offset a hybrid table's /Prev names, a stream that may be the table's own is told by its own /Prev: one giving no /Prev, or the offset the table's /Prev names, is tried after every other place; the stream the /XRefStm read keeps its nearest-first place where its own /Prev names another offset. The code is TryRelocateXRefSection, TryParseNearHybridPrev and MayBeTablesOwn in src/AdCodicem.Pdf/IO/PdfFileReader.cs. Round 8 found that the rule defers a first revision's cross-reference stream (which gives no /Prev by ISO 32000 Table 17) and so loses the right section ([7], [29]); that a deferred stream is counted twice against MaxRelocationCandidates ([8]); that a dictionary MaxTrailerLength cut is judged as if whole ([9], [13]); that the stream's /Prev is compared as written ([11]); and that a table whose /XRefStm names a stream again gets no deferral ([30], tied to open issue #330). Do not write any review key, finding number or probe name into src/ or tests/ beyond your own uncommitted Zz probe files, and delete them before you finish.`

const HARNESS = {
  type: 'object',
  properties: {
    manifest: { type: 'string' }, probe_source: { type: 'string' }, classify: { type: 'string' },
    outputs: { type: 'object', properties: { main: { type: 'string' }, d3ef: { type: 'string' }, head: { type: 'string' } }, required: ['main', 'd3ef', 'head'] },
    files: { type: 'integer' }, blocks: { type: 'integer' },
    head_vs_main: { type: 'string', description: 'counts per class and the list of blocks where head is worse than main, each with its origin finding' },
    head_vs_d3ef: { type: 'string' },
    how_to_run: { type: 'string', description: 'exact commands a variant agent runs to produce and classify its own output' },
    notes: { type: 'string' },
  },
  required: ['manifest', 'probe_source', 'classify', 'outputs', 'files', 'blocks', 'head_vs_main', 'head_vs_d3ef', 'how_to_run', 'notes'],
}
const VARIANT = {
  type: 'object',
  properties: {
    variant: { type: 'string' },
    rule: { type: 'string', description: 'the rule as implemented, in one paragraph' },
    patch: { type: 'string', description: 'path of the saved diff against 012e13d' },
    output: { type: 'string' },
    vs_main: { type: 'string', description: 'counts per class; every block worse than main, with its origin and why' },
    vs_d3ef: { type: 'string' },
    vs_head: { type: 'string' },
    round8_shapes: { type: 'string', description: 'for each shape of findings 7, 8, 9, 11, 13, 29, 30 and the round 7 [20] and shape A rows: what this variant gives, against main and head' },
    existing_tests: { type: 'string', description: 'the run of CrossReferenceChainTests, CrossReferenceRuleTests, GuardCutValueTests, HostileInputTests, DocumentReaderTests, WindowEdgeTests and HeaderOffsetTests: every failing row, and whether its flip is intended by the rule or a regression' },
    whole_suite: { type: 'string' },
    complexity: { type: 'string', description: 'lines changed, allocations added on a valid file\'s path, bound on reads' },
    verdict: { type: 'string' },
  },
  required: ['variant', 'rule', 'patch', 'output', 'vs_main', 'vs_d3ef', 'vs_head', 'round8_shapes', 'existing_tests', 'whole_suite', 'complexity', 'verdict'],
}

phase('Harness')
const h = await agent(`${COMMON}

Your task: build the differential harness the variant agents will use. Output directory ${P} (create it).
1. Collect into ${P}/files/ every PDF the round 8 reviewers and refuters generated for the own-stream findings, regenerating from their generators where only a generator was kept: ${SC}/r8own/gen/*.pdf (3,000 seeded hybrid files) and the hand-built shapes beside it (${SC}/r8own/*.pdf: dense, cut, cut2, far, budget …), ${SC}/r8ref-own/out/, ${SC}/r8refbudget/, ${SC}/r8ref-prevform/ (gen.py), ${SC}/r8ref-cutown/, ${SC}/ref35/gen/, ${SP}/r8bat/genL/ and the genK files (${SP}/r8bat/genK.py and its outputs), ${SP}/r8ref-kagain/ (x_again, q_xref_again, q_plain_again, p_again, kagain, kfirst …), ${SP}/r8ref-ghp/ (the 70,000-byte /Pad shape: write it out as a file, with its /Prev-before-pad control). Also write out the files the round 7 [20] and shape A tests build (HybridFiles builders the CrossReferenceChainTests own-stream rows use: PrevFallingShortOfTheStreamBefore…, the 'table before' and offset-zero rows), so the variants are measured on them too. Name each file by its origin. Write ${P}/manifest.tsv: path, origin (finding or test), and the limit sets to run it under: every file under default limits; the 3,000 seeded files also under MaxTrailerLength 100 and MaxXRefSectionCount 2 as the reviewer ran them; each hand-built shape under the limits its finding names (MaxTrailerLength 100 for cutprev, ThrowOnLimit and MaxTrailerLength 1 MB for the pad shape, MaxRelocationCandidates as the budget shapes need).
2. Write one probe, ZzProtoDiffTests.cs, uncommitted, that reads the manifest and for each file and limit set prints one canonical block: the sections (FileStructure.Sections: naming, named offset, offset read, kind, state), every diagnostic at opening (severity, code, offset, message), WasRepaired at opening, every object number the file uses read ascending in one open and descending in a fresh open (value summarized: a string's text, a number, 'stream', 'dict', 'null', or the exception's type), the diagnostics added by those reads, and the validator's findings (severity, rule, offset). It writes to the path an environment variable names. Save its source to ${P}/ZzProtoDiffTests.cs.txt.
3. Run it on main 21a0298, d3ef236 and 012e13d (worktrees you add and remove; Release builds), giving ${P}/out-main.txt, out-d3ef.txt, out-head.txt. Each run must be deterministic: run head twice and confirm byte-identical output.
4. Write ${P}/classify.py: given a variant's output and a baseline's, compare block by block and class each block as: identical; wording-only (same sections taken, same objects in both orders, same repair at opening, same Error and Warning finding codes, messages differing); better (the baseline rebuilds at opening, serves a freed or older object, or gives an Error that the variant does not, while the variant reads the chain whole); worse (the variant rebuilds at opening where the baseline does not, serves another value for any object in either order, gives an Error finding the baseline does not, takes a section farther off, loses a section the baseline read, throws, or depends on read order where the baseline does not); other. It prints counts per class and lists the better and worse blocks with their origin. Reuse ${SC}/r8own/classify.py and compare.py where they help.
5. Report head's classes against main and against d3ef236, with every worse block's origin, and give the exact commands a variant agent runs (build its worktree, run the probe with the manifest, classify against the three baselines).`, { label: 'harness', phase: 'Harness', schema: HARNESS })

if (!h) return { error: 'harness failed' }
log(`harness: ${h.files} files, ${h.blocks} blocks`)

const HBRIEF = `The harness is ready: manifest ${h.manifest}, probe source ${h.probe_source}, classifier ${h.classify}, baselines main ${h.outputs.main}, d3ef236 ${h.outputs.d3ef}, head ${h.outputs.head}. How to run: ${h.how_to_run}. Head against main: ${h.head_vs_main}. Head against d3ef236: ${h.head_vs_d3ef}.`
const BOTH = `Besides the rule itself, every variant includes two fixes the triage settled without a question: (i) a dictionary a guard cut is never judged 'may be the table's own' (in TryParseNearHybridPrev, mayBeOwn = parsed is PdfStream && MayBeTablesOwn(...)), so a cut stream keeps its place and its guard is reported as on d3ef236; (ii) a place passed over is counted once against MaxRelocationCandidates, at its first reading, and its retry is not counted again (a retry of a counted place still runs once the bound stops new places), so reads stay under twice the bound. Implement the variant in your own worktree from 012e13d, save the diff to ${P}/<variant>.diff, build, run the harness and classify against main, d3ef236 and head; then run CrossReferenceChainTests, CrossReferenceRuleTests, GuardCutValueTests, HostileInputTests, DocumentReaderTests, WindowEdgeTests and HeaderOffsetTests and the whole unit suite, and for each failing row say whether its flip is what the rule intends (the pinned cut-dictionary row ('stream 11', 300) flips to the guard's outcome under (i), as decided) or a regression. Inspect by hand every block worse than main or worse than d3ef236 and explain it. Be exact: counts, origins, the messages that differ.`

const VARIANTS = [
  { key: 'P1-positional', prompt: `Variant P1, 'position, then /Prev'. Only a stream that starts past the start of the stream the table's /XRefStm read (where the chain read it) may be the table's own: under append-only updates a revision's own stream lies past every older revision's section, and the stream the /XRefStm read is either the table's own or an older revision's, so a stream that starts at or before it is never the table's own and keeps its nearest-first place. Among the streams past it, R7-Q2 applies as now (no /Prev, or the table's /Prev offset: tried once every other place was, nearest first). The stream the /XRefStm read is judged as now. Measure two sub-variants for finding [11] in the same worktree, each with its own diff and output: P1a compares a stream's /Prev as the chain reads it (a real with no fractional part as its integer; a reference as the integer it gives where a section already read places its object, read without loading and reporting nothing), and counts a /Prev from which the chain reads no offset (negative, past the file, a reference nothing places, not a number) as giving no /Prev; P1b reads a real and a reference as P1a does but counts a /Prev naming no offset as another offset (R7-Q2's wording). Report P1 (as written), P1a and P1b separately. Note which existing tests' builders put a 'table's own' stream before the stream the /XRefStm read (the offset-zero row does) and whether such a file can be produced by an append-only writer.` },
  { key: 'P1-named-again', prompt: `Variant P1 ('position, then /Prev': only a stream that starts past the start of the stream the table's /XRefStm read may be the table's own; a stream at or before it keeps its nearest-first place; among the streams past it R7-Q2 applies as now), extended for finding [30], a table whose /XRefStm names again a stream a newer table's /XRefStm read. Measure two sub-variants, each with its own diff and output: PNA-b arms only the deferral in the NameAgain branch of TryReadXRefChain (the table's /Prev search treats the named-again stream as the stream its /XRefStm read for the deferral, without taking it in its place); PNA-a also lets the table's /Prev take that stream in its place where its own /Prev names another offset, as for a first naming (readInStreamsPlace = again.ReadAs where it is a cross-reference stream the chain read). For each, say which [30] shapes (kagain, p_again, x_again, q_xref_again, q_plain_again, kfirst, genKt1, genKlost) read as on main, which still differ, and what each implies for #330's open question (what a /Prev is when it reaches a stream the chain read only through another table's /XRefStm).` },
  { key: 'P2-by-xrefstm-stream', prompt: `Variant P2, 'judge the others by the stream the /XRefStm read' (the [7] refuter's proposal). Let S be the stream the table's /XRefStm read. Where S gives no /Prev, or the table's /Prev offset, S is the table's own: defer nothing else (S itself is dropped or deferred as now). Where S gives a /Prev that names exactly a place in the window, that place is S's previous section: it keeps its nearest-first place. Other streams that give no /Prev, or the table's, are deferred only where S's /Prev names an offset that holds no place in the window. Compare /Prev values as now (as written).` },
  { key: 'P3-prev-target', prompt: `Variant P3, the minimal one (the [29] refuter's option A). The rule stays as at 012e13d, but a stream at the offset the /Prev of the stream the /XRefStm read names (where that /Prev is a direct integer) is the section meant and is never deferred. Nothing else changes.` },
]

phase('Variants')
const results = await parallel(VARIANTS.map(v => () => agent(`${COMMON}\n\n${HBRIEF}\n\n${v.prompt}\n\n${BOTH}`, { label: v.key, phase: 'Variants', schema: VARIANT, isolation: 'worktree' })))

phase('Judge')
const judge = await agent(`${COMMON}\n\n${HBRIEF}\n\nFour variant agents prototyped candidate refinements of R7-Q2 and measured them. Their reports:\n${JSON.stringify(results.filter(Boolean), null, 1)}\n\nJudge them. Check their claims where they disagree or look too good (rerun a classifier or a probe yourself; the outputs and diffs are under ${P}). Then give: a table, per variant and sub-variant, of blocks worse than main, worse than d3ef236, better than main, which round 8 shapes it fixes, which existing tests flip (intended or not), and its complexity; the rule you recommend for the maintainer, with what it still gets wrong and what the docs must then say; and, for [30], whether to fix it in this batch (which sub-variant) or file it beside #330 and #321, with the measured consequence each way. Plain text, precise, in English.`, { label: 'judge', phase: 'Judge' })

return { harness: h, variants: results, judge }
