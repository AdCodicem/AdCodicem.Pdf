# Batch 19 working notes (not for merging)

The orchestrating session's notes for batch 19 of M02's step 4, saved at the maintainer's request. They hold
measurement and review keys and scratchpad paths, which never belong in src/, tests/ or docs/.

**To resume, read `RESUME.md` first** (state on 2026-11-08, what remains, the standing rules, the records for
status.md, and how to run the tools on Windows). `PROMPT-fr.md` is the prompt to start the next session with.

- `decisions.md`: every maintainer decision (B19-1 to B19-8, Q9 to Q12, R1-Q1 to R1-Q4, R4-Q1, R6-Q1, R7-Q1 to
  R7-Q3, R8-Q1 to R8-Q3, R9-Q1, R9-Q2), each round's triage and result, the debts filed (#320 to #332), the comments
  made.
- `brief.md`, `review-brief.md`: the briefs the measurement and the reviews ran under.
- `measure/<lens>/`: the measurement on main 21a0298. `impl-result.json`, `residuals-impl.md`: the implementation.
- `rN-findings.md`, `rN-verified.json`, `rN-reviewers.json`, `rN-fix-result.json` (N = 1 to 9): review rounds and
  their fixes, findings numbered `######## [n]` (`python3 showf.py rN-findings.md 6000 <n...>`).
- `lot19-*.js`, `workflows/`: the workflow scripts; `lot19-r9-fixes.js` is the next to run.
- `tools/`, `baselines/`, `harness.tar.gz`, `r9probes.tar.gz`: the check scripts, round 8's corpus dumps, the
  own-stream differential, and round 9's probes (see RESUME.md section 5).
- `stage*/`, `track*/`, `r8b-wip/`, `r8track/`, `debts/`: probes and records of earlier stages.
