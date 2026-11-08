# Resuming batch 19 (paused 2026-11-08, again late that day)

This is the handoff from the cloud session that ran batch 19 of M02's step 4. It was stopped at the maintainer's
request so that the work can resume on the maintainer's Windows PC. These notes hold review keys, probe names and
scratchpad paths. **None of that may go into src/, tests/ or docs/.**

## 1. Where things stand (updated 2026-11-08 late, Windows session, paused at the maintainer's request)

- **Batch.** Batch 19 of M02's step 4 pays #156, #188, #197, #207, #208, #210, #221, #222, #241, #261, #264, #281
  and #317. It started from `main` at 21a0298.
- **Branch.** `ccr-6d6c0ee6-dqm4lm-188` is at **7b1de15**, pushed and clean. No pull request exists yet.
  - Implementation: 21a0298..071f829. Review rounds 1 to 10 fixed: 071f829..79c6a7f (ranges in `decisions.md`).
  - Round 11 fixes so far: 79c6a7f..fdd5a71 (part 1, R11a and R11b, complete and checked) and fdd5a71..7b1de15
    (part 2's R11c, **incomplete**: 5fb9444 test, 7b1de15 fix).
- **WIP branch.** `ccr-6d6c0ee6-dqm4lm-188-wip` holds only these notes, in `notes/batch19/`. It is never merged.
- **Round 11** (wf_556f39fe-f5c on 79c6a7f) is reviewed and triaged (`decisions.md`, *Review round 11 ...: triage*,
  R11-Q1 answered "document scope"). 24 findings, `r11-findings.md`.
- **Part 1** (wf_47fb1f52-837): result in `r11-fix1-result.json` and `decisions.md` (*Review round 11 fixes, part 1*).
- **Part 2** (wf_629fa085-d7b, `lot19-r11-fixes-win-2.js`) was **stopped by the maintainer during R11c's ADR 53
  step**. No agent of that run completed, so a `resumeFromRunId` replays nothing and would redo R11c from scratch.
  What R11c left, all under the scratchpad's `cloud/r11wip/`:
  - pushed: 5fb9444 (rows for the white-space guard's document-wide hold, shapes A and B) and 7b1de15 (the parted
    /Type sets the flag, renamed `_guardMayHideLink`; texts in src/ for both cases);
  - `r11c-diffrun.log`: the differential run is identical to r10f and r11a (9,228 blocks);
  - `R11c-corpus/corpus-branch-1.txt`: the corpus run on 7b1de15 is done, **not yet compared** (vs main and vs
    `r10f/corpus-branch-1.txt`);
  - `r11c-mut/summary.txt`, `run.py`, `logs/`: ADR 53 over 7b1de15's lines. 24 mutants: 17 killed, 3 not viable
    (M07, M08, M20; M20b killed), **4 survived the whole suite: M01, M11, M13, M14** (`run.py` says what each
    changes). Each must be killed by a row or classed equivalent with a reason;
  - `r11c-probes/`: before/after probes of the parted /Type and the white-space reach.
  - R11c returned no report: its tests_added, deviations and open points (pages for R11d) are only in its commits'
    bodies and in the probes.
- **Where the outputs are.** The Windows scratchpad
  `C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad`
  (W = its `cloud/`, SP = `cloud/lot19`) holds every round's outputs, scripts and tools as long as Temp is not
  cleaned. R11c's outputs and round 10's corpus baseline are also in `r11c-left.tar.gz` here.
- All workflow worktrees were removed on pause (none held uncommitted work; the remote corpus copy was hard links,
  unlinked first; the maintainer's 242 remote files are intact).

## 2. What remains, in order

0. **Finish round 11's part 2.** Edit `lot19-r11-fixes-win-2.js` (scratchpad copy; a copy is here) so R11c's prompt
   says 5fb9444 and 7b1de15 are pushed and its remaining work is: settle M01, M11, M13, M14 (kill or class
   equivalent), compare the corpus run, write its report (tests_added, deviations, open points for R11d); then run
   R11d (pages), tracking and R11f (check) unchanged. Launch it fresh (no resume: nothing is cached). Then record
   the result in `decisions.md`, and launch review round 12 on 79c6a7f..head (model: `lot19-review-r11.js`).
1. **Review rounds** until one has no blocker, major or minor finding: triage each in `decisions.md`, ask the
   maintainer any design question, fix (model: `lot19-r10-fixes-win.js`).
2. **The batch-wide ADR 53 step.** Run the mutation catalog over every src/ line the batch changes.
3. **Verification.** Coverage at 100 % of the patch (Codecov's count); integration tests twice (Docker); the corpus
   diff; benchmarks, alone; the site; the Remote corpus workflow on the branch; the .NET 11 compatibility island.
4. **`docs/status.md`.** The journal entry and the milestone checklist; the records in section 4 and in
   `decisions.md` (search "status.md").
5. **End-of-batch GitHub comments:** #289, #295, #52, and optionally #260 (search `decisions.md`).
6. **The pull request.** A `fix!:` title; body `Closes #156, #188, #197, #207, #208, #210, #221, #222, #241, #261,
   #264, #281, #317`. Never merged.

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
- **Processes.** On the maintainer's PC (16 cores, 32 GB), dotnet builds and tests may run in parallel: mutants in
  parallel worktrees, the suite beside a corpus dump, review agents building side by side (maintainer, 2026-11-08).
  The one exception: a benchmark run, or a timing, budget, allocation or memory test, runs alone. Never two builds
  in the same worktree at once. In the 16 GB cloud container, parallel runs exhausted memory twice: there, one
  process at a time. Kill a process by PID. Never use a `pkill` pattern that appears on the same command line.
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
- **Added by rounds 9 and 10** (see decisions.md's two *fixes: result* sections): 68a55fa / e157359 / e3d0c99;
  092b772 false for a guard until 42ca0bb; e157359 + 68a55fa counted a held guard as left out until daca13c; b55738a
  false for the white-space guard until 5395eed; R9b's M11 reason (re-judged S09) and NumberRunsInto remark premise
  until 2be5c44; 9823992's 'judgement'.
- **Debts filed by the batch:** #320 to #333.

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
