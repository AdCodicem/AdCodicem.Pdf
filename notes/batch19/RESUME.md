# Resuming batch 19 (paused 2026-11-08)

This is the handoff from the cloud session that ran batch 19 of M02's step 4. It was stopped at the maintainer's
request so that the work can resume on the maintainer's Windows PC. These notes hold review keys, probe names and
scratchpad paths. **None of that may go into src/, tests/ or docs/.**

## 1. Where things stand

- **Batch.** Batch 19 of M02's step 4 pays #156, #188, #197, #207, #208, #210, #221, #222, #241, #261, #264, #281
  and #317. It started from `main` at 21a0298.
- **Branch.** `ccr-6d6c0ee6-dqm4lm-188` is at **da2b06f**, pushed and clean. No pull request exists yet.
  - Implementation: 21a0298..071f829.
  - Review rounds 1 to 8 fixed: 071f829..da2b06f. Each round's ranges are in `decisions.md`.
- **WIP branch.** `ccr-6d6c0ee6-dqm4lm-188-wip` holds only these notes, in `notes/batch19/`. Its commits are
  `wip: ... [skip ci]`. It is never merged.
- **Review round 9 is done** (wf_a01bab38-4ae, on da2b06f).
  - It made 31 findings: 3 major, 19 minor, 8 nit, and 1 refuted ([31]).
  - They are in `r9-findings.md`, numbered `######## [n]`, each with the reviewer's claim, evidence and fix, then
    the refuter's verdict, reason, evidence and fix. `python3 showf.py r9-findings.md 6000 <n...>` prints them.
  - Also there: `r9-verified.json` (one line per finding), `r9-reviewers.json` and `r9-raw.json`.
- **The triage is written.** See `decisions.md`, section *Review round 9 (wf_a01bab38-4ae on da2b06f): triage*. It
  is the mandate for the round 9 fixes.
- **The maintainer answered both round 9 questions**, each as recommended. The answers are at the end of
  `decisions.md`.
  - **R9-Q1, "Le traiter comme S".** A candidate before S whose stream data starts where S's does *is* S. It is
    dropped where it gives no /Prev, and deferred where it gives the table's. This restores 012e13d's whole reading
    for an /XRefStm that lands inside its own stream's object number.
  - **R9-Q2, "En-têtes typés".** `xref.stream-passed-over` names only a place that declares itself: /Type /XRef,
    with a header that is a token on its own line. It costs no extra read.
- **The round 9 fixes were launched, then stopped before they changed anything.** The run was wf_756885b9-080 with
  script `lot19-r9-fixes.js`. Its first stage, R9a, had only read and probed: it left no diff and no commit, and
  nothing was pushed after da2b06f. **The round 9 fixes are entirely still to do.**

## 2. What remains, in order

1. **Round 9 fixes.** `lot19-r9-fixes.js` holds the full brief for each stage. Run them in order:
   1. **R9a: the passed-over Warning.**
      - [1]/[10]: no Warning for a stream the chain reached and reported unreadable.
      - [2]/[11]: no Warning where a guard ended the chain, or cut what the chain reached (ADR 34, #264).
      - [30]: as R9-Q2 decides.
      - [3]: reword the message.
      - [15]: test for an empty chain.
      - [18]: assert exact code lists.
      - [19] and [21]: nits.
      - [6]: the source comment.
   2. **R9b: the search near a hybrid /Prev.**
      - [16]: as R9-Q1 decides.
      - [4]: forget what judging S reads through.
      - [5]: H3 is not equivalent; add the row with a chain of 32 objects.
      - [13]/[23]: the bound's exception in the comments, plus an into=28 row.
      - [22]/[29]: rows that pin the known gaps.
   3. **R9c: undecoded data and the /Prev check.**
      - [7]: the candidate's FaultBesideCut decoding is rolled back.
      - [8]/[12]: the check gives a typed stream no decoding, and rolls an untyped one back.
      - [17]: cover the line by test.
      - [20]: rename the test.
      - [9]/[14]/[24]: the source comments.
   4. **R9d: the pages.** Every open point the stages above leave, and findings [2]/[11], [1]/[10], [3], [6],
      [9]/[14]/[24], [13]/[23], [15], [16], [19], [20], [22]/[29], [25], [27] and [30]. Also R8's open point at
      `concepts/reader-limits.md:119-120`, and the R8a consequence on MaxDecodedStreamLength.
   5. **R9-track, on GitHub.**
      - Correct item 1 of the #332 body.
      - Open a new `debt` issue (area: reader, M02) for an older revision's stream before S. Taken, it either ends
        the chain or leads past its own table in silence. The first revision's stream can also be taken about 500
        bytes off.
      - Comment on #171 about a quoted stream that is passed over.
   6. **R9f: the check.**
      - Build and test: build with 0 warnings, the whole unit suite with the remote corpus linked, and format.
      - Commit and text hygiene: commit rules, a grep for keys, links, and test citations.
      - The site build.
      - Timing, budget and allocation tests, three times each.
      - The corpus diff against main and against round 8's dump.
      - The own-stream differential, against round 8's check run (`out-r8f.txt`).
2. **Review round 10.** Model it on `lot19-review-r9.js`:
   - six lenses: own-stream, undecoded, src-texts, tests, docs, batch;
   - one refuter per finding;
   - at most 4 agents at a time.

   Then triage it in `decisions.md`, put any design question to the maintainer, and fix. **Repeat review rounds
   until a round has no blocker, major or minor finding.**
3. **The batch-wide ADR 53 step.** Run the mutation catalog over every src/ line the batch changes.
4. **Verification.**
   - Coverage: aim at 100 % of the patch, as Codecov counts it.
   - Integration tests, twice. They need Docker.
   - The corpus diff, main against branch.
   - Benchmarks.
   - The site: `docs/website`, `npm ci && npm run build`.
   - The Remote corpus workflow, run on the branch.
   - The .NET 11 compatibility island (`tests/Compat/global.json`).
5. **`docs/status.md`.** Write the journal entry and tick the milestone checklist. Do not edit it before then. The
   records are listed in section 4.
6. **End-of-batch GitHub comments:** #289, #295, #52, and optionally #260. Their content is gathered in
   `decisions.md`; search it for "#289 comment", "#295" and "#52 end comment".
7. **The pull request.** Its subject is a `fix!:` conventional title. Its body says
   `Closes #156, #188, #197, #207, #208, #210, #221, #222, #241, #261, #264, #281, #317`. Then offer to watch it.

## 3. Standing rules (the maintainer's, on top of CLAUDE.md)

- **Language.** Speak French to the maintainer. Write code, docs, commits and issues in English, with American
  spelling.
- **Questions** to the maintainer go only through AskUserQuestion, in French. Put the recommended option first and
  mark it "(Recommandé)". Make each question self-contained.
- **Commits.**
  - Conventional subject of at most 100 characters.
  - No `#n` in the subject or the body.
  - The footer is `Refs #n[, #m]`, a blank line, then the attribution lines your session gives. The cloud session
    used `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` and a `Claude-Session:` URL.
  - No model identifier in commits or the PR.
  - Never rewrite pushed history.
  - Push only to `ccr-6d6c0ee6-dqm4lm-188`. Notes and WIP go to `-wip`.
  - Never merge the PR.
- **GitHub texts.** Comments and issue bodies end with `\n\n---\n_Generated by [Claude Code](https://claude.ai/code)_`.
  They carry no scratchpad path, probe file name, review key or finding number.
- **Never write review or measurement keys in src/, tests/ or docs/.** That covers:
  - finding numbers like [18];
  - question keys like R9-Q1;
  - mutant ids like H3;
  - probe and shape names like kfirst or g001929;
  - the words reviewer, refuter, prototype and variant.

  Name behaviors in plain words.
- **Processes.** One dotnet build or test process at a time per agent. In the 16 GB cloud container, parallel runs
  exhausted memory twice. A PC may allow more, but keep mutation runs one at a time. Kill a process by PID. Never use
  a `pkill` pattern that appears on the same command line.
- **ADR 53.** Each code change runs the first-order catalog over its changed src/ lines. Every survivor is killed by
  a test, classed equivalent with a reason, or filed as debt.

## 4. Records for status.md at the batch's end

- **Commit bodies not rewritten.**
  - From earlier rounds: e67fef9, b09f981, 5656c64, 394028c, d63f093, 7c0748b, 2b26d18, ea348ce, 8ccd868, f0d90c8,
    8ba4589, 8692234, de12b13, 092b772, ed61728, 253f179.
  - Added by round 9:
    - d8e67de: its unconditional older-stream gap, and its 64-reads figure, which leaves out the /Prev check made
      past the bound.
    - 5de20b7: false where a guard cut a value.
    - da0e157: "only an object that gives a /W and no such type has its data decoded" is false beside a cut.
- **ADR 53 tallies corrected.**
  - R7a: 35 mutants, 31 killed, 1 equivalent (M14), 3 not viable.
  - R8b, once R9b's row kills H3: 87 mutants, 79 killed, 5 equivalent (A4, B7, C22, G4, H12), 3 not viable.
  - 7ff4f39's cost: up to MaxChainLength reads past the first, the last dropped.
- **R8a consequence.** Under a guard that cut /Size or the whole /Index, the data is decoded to tell. So
  MaxDecodedStreamLength may be reached, and thrown under ThrowOnLimit, before the guard that cut the value. Until
  R9c, this also applied in the /Prev check to a typed stream.
- Item 18's PR #318 merged on 2026-11-03. The other records are gathered in `decisions.md`; search it for
  "status.md".
- **Debts filed by the batch:** #320 to #332, plus the one R9-track opens.

## 5. The files here, and running them on Windows

- `decisions.md`: every decision and triage since the batch began. Read it first.
- `review-brief.md`: the brief the review rounds run under. It ends with the resource rule.
- `lot19-r9-fixes.js`: the round 9 fixes workflow, still to run. `lot19-review-r9.js`: the round 9 review, the
  model for round 10. Older `lot19-*.js` and `r1`..`r8` files are the earlier rounds.
- `tools/`:
  - `check_msgs.py`: commit messages;
  - `grep_keys.py`, `widegrep.py`: keys in src, tests and docs;
  - `citecheck.py`, `namecheck.py`: test names the pages cite;
  - `linkcheck.py`: Markdown links;
  - `normalize241.py`, `cmp_corpus.py`, `corpus-run.sh`, `ZzCorpusDump.cs.txt`: the corpus dump and diff. Copy
    the dump into `tests/AdCodicem.Pdf.Tests/ZzCorpusDump.cs` only for a run, and never commit it.
- `baselines/`: the corpus dumps of round 8's check. One is the branch at da2b06f. The other is main at 21a0298,
  raw and with #241's rewordings normalized.
- `harness.tar.gz`: the own-stream differential.
  - It holds `run_variant.sh`, `classify.py`, `manifest.tsv`, the probe `ZzProtoDiffTests.cs.txt`, 3,210 generated
    files, and their generators in `handgen/` and `regen/`.
  - The baselines inside are `out-main.txt` (21a0298), `out-d3ef.txt` (d3ef236), `out-head.txt` (012e13d) and
    `out-r8f.txt` (da2b06f).
  - Unpack it into a folder of its own.
- `r9probes.tar.gz`: the round 9 reviewers' and refuters' probes and generators. These are the paths their evidence
  names under the cloud scratchpad. Also inside: the ADR 53 mutation scripts of round 8 (`r8b/mutate*.py`,
  `r8b3/mutate*.py`).

**Windows notes.**
- The scripts hard-code the cloud scratchpad `/tmp/claude-0/.../scratchpad`. Before running them, edit:
  - the `SP`/`PR` constants in `lot19-r9-fixes.js`;
  - `R=` in `run_variant.sh`;
  - the `--manifest` default in `classify.py`;
  - `S=` in `corpus-run.sh`.

  Paths in `manifest.tsv` are relative to its folder.
- The shell scripts need bash: Git Bash or WSL. `setsid` is absent from Git Bash, so drop it there.
- The Python scripts need Python 3.
- Integration tests need Docker Desktop. The site needs Node.js. The compatibility island needs the .NET 11 SDK.
- If the Workflow tool is not available, run the stages yourself, or with subagents, in the same order. Push after
  each stage.

## 6. How to fetch these notes

```powershell
git fetch origin ccr-6d6c0ee6-dqm4lm-188 ccr-6d6c0ee6-dqm4lm-188-wip
git switch ccr-6d6c0ee6-dqm4lm-188          # the batch branch, at da2b06f
git worktree add ..\AdCodicem.Pdf-notes origin/ccr-6d6c0ee6-dqm4lm-188-wip
# the notes are then in ..\AdCodicem.Pdf-notes\notes\batch19
```
