# Batch 19 (M02): final end-of-batch verification

Base: main `21a0298`. Head before step 0: `9a21ac7`. Final head H: `2c7cf01` (origin/ccr-6d6c0ee6-dqm4lm-188, pushed by
fast-forward 9a21ac7..2c7cf01). Worktrees: `verify/final/h` (H, remote corpus hard-linked, 242 files), `verify/final/cov`
(H, no remote corpus), `verify/final/m` (main 21a0298). Logs: `verify/final/logs/`.

## Step 0: the test comment

`CrossReferenceChainTests.A_file_whose_only_cross_reference_stream_decodes_to_nothing_is_rebuilt_and_its_catalog_found`
said the encoded bytes "were read as rows, three of a reserved type". Checked against `UndecodedStreamFiles.AloneInDamagedZlibData`
by rebuilding its bytes with the same code on this SDK (`scratchpad/rows/rows.cs`):

- `/W [1 4 2]` (7-byte rows), `/Size 4`, no `/Index`: the default `[0 4]`, four rows, 28 bytes asked for.
- Rows written: (0, 0, 65535), (1, 9, 0) catalog, (1, 58, 0) pages, (1, 110, 0) the stream; 28 bytes raw.
- Zlib (Optimal) gives 29 bytes: `120 156 99 96 96 96 96 248 255 159 145 129 129 129 147 129 1 68 89 65 168 60 6 6 0 49 64 2 179`;
  byte 2 complemented (99 -> 156).
- Read as rows: 29 >= 28, so four whole rows, types 120, 248, 147, 60 - none 0, 1 or 2, so all four reserved.

The comment was wrong (three); now "were read as four rows, all of a reserved type". Commit `2c7cf01`
`test: say all four rows read from a lost stream's encoded bytes were reserved` (77 chars, body under 80 columns,
`Refs #260`, blank, Co-Authored-By; LF; `git commit -F`). check_msgs OK; grep_keys and widegrep: nothing. Build 0 warnings.
Pushed fast-forward.

## Step 1: build, format, messages at H

- `dotnet build AdCodicem.Pdf.slnx -c Release -m:4`: succeeded, 0 warnings, 0 errors (logs/step1-build.txt).
- `dotnet format AdCodicem.Pdf.slnx --verify-no-changes`: exit 0, no changes (logs/step1-format.txt).
- check_msgs over `git log 21a0298..2c7cf01`: 219 commits, 218 OK, one flagged:
  `761ce0b` (docs: scope what a guard leaves judged, and say what the chain now reads) - line 31 len 87: its footer
  `Refs #156, #171, #188, #207, #208, #210, #221, #241, #261, #264, #281, #323, #329, #330`, 87 columns. Pushed history,
  recorded only.

## Step 2: whole unit suite at H, remote corpus present (242 files)

`dotnet test --project tests/AdCodicem.Pdf.Tests/AdCodicem.Pdf.Tests.csproj -c Release --no-build`: total 11,155,
passed 11,152, failed 0, skipped 3, duration 3 m 58 s. Skipped: `CorpusManifestSchemaTests.A_private_manifest_follows_the_same_schema`
(no private manifest), and `CorpusReadingTests.Opening_does_not_read_the_content_of` for two remote files (#47, M23).

## Step 3: coverage as CI runs it (worktree `cov` at H, no remote corpus)

CI's unit-test step: `dotnet test --project tests/AdCodicem.Pdf.Tests/AdCodicem.Pdf.Tests.csproj -c Release --no-build --
--coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml --results-directory TestResults`, after
`dotnet restore` and `dotnet build -c Release --no-restore` (0 warnings). Run: total 8,466, passed 8,445, failed 1,
skipped 20, 5 m 18 s (logs/step3-coverage-run.txt; report kept as logs/step3-coverage.cobertura.xml).

The failure: `SeededComparerTests.No_method_of_the_library_builds_a_table_keyed_by_numbers_without_the_seeded_comparer`,
reporting `ArlingtonWalk..ctor: Dictionary<Int32, Byte>..ctor` (ArlingtonWalk.cs:86 does pass `SeededComparer.Instance`).
This is open issue #336: on Windows the coverage profiler rewrites a method's IL when it is compiled, and the test reads
the rewritten IL, whose probes (`ldc.i8 <address>; ldc.i4.1; stind.i1`) are not the one shape it skips, once a method that
builds a seeded table has run earlier in the process. Neither ArlingtonWalk.cs nor SeededComparerTests.cs changed in the
batch; it passed in step 2's run without coverage; CI runs coverage on ubuntu-latest only. Run alone three times under the
same coverage options: passed 3 of 3 (logs/step3-seeded-alone-cov-{1,2,3}.txt). No code changed.

Patch coverage (cov_patch.py, base 21a0298, head 2c7cf01, src/ only): 1,461 coverable changed lines; 1,460 run;
1,457 whole = 99.73 %. src/ lines covered: 19,700 of 19,713 file lines = 99.93 % (the report's own line-rate 99.90 %,
19,941 / 19,961 counting lines held by several classes; branch-rate 99.18 %).

Lines not whole:
- `src/AdCodicem.Pdf/IO/PdfFileReader.cs:4075` (partial 2/4) and `:4077` (missed): `TryParseUnrecordedAt`'s
  `if (offset < 0 || offset >= _source.Length) return false;`. Defensive branch no input takes: its only caller is
  `TryReadXRefStream(checkOnly: true)`, called only from `KeepsItsPlace(leaveAt, ...)` where `leaveAt` equals a candidate
  the search found in the file (a token's start, `StartOfNamed` giving -1 outside the file), and the check record's
  padding is 0, so the offset is always inside the file.
- `src/AdCodicem.Pdf/IO/PdfFileReader.cs:5567` (partial 1/2): `DigitsPastSigns`'s `signs < 0 ? at : at + signs`.
  Defensive branch no input takes: a place the search finds lies whole within the bytes searched, its number or `xref`
  with it, so a byte that is no sign always follows (its remarks say so).
- `src/AdCodicem.Pdf/Validation/Rules/SizeWrongRule.cs:71` (partial 1/2): the null arm of `section?.HighestNumber ?? -1`.
  A `?.` on a value no input makes null there: `Counted` returns null only for a `NamedAgain` record whose `ReadAs` is no
  hybrid stream, and every such record (NameAgain over a read non-hybrid section, an unreadable table, a guard; LeaveToPrevious;
  LeaveReadingToPrevious) carries `NamesNoStream`, a Malformed/NotFound section or a guard's cut, all of which `IsWhole`
  refuses before the loop. The ADR 53 mutants of that `-1` (SWR-0024, SWR-0025) survived, consistent with it.
No gap found.

## Step 4: timing, budget, allocation, memory, hostile-input groups (timing.ps1, alone, worktree h)

| Group | Run 1 | Run 2 | Run 3 |
|---|---|---|---|
| timing (`*ReaderTimingTests`) | 25/25, 27.8 s | 25/25, 28.6 s | 25/25, 30.0 s |
| budget (`*budget*`, `*Budget*`) | 14/14, 3.9 s | 14/14, 4.0 s | 14/14, 4.0 s |
| alloc (`*llocat*`) | 20/20, 21.8 s | 20/20, 23.5 s | 20/20, 23.3 s |
| memory (`*HostileInputMemoryTests`) | 19/19, 27.7 s | 19/19, 28.6 s | 19/19, 26.3 s |
| hostile (`*.HostileInputTests`) | 263/263, 71.2 s | 263/263, 73.2 s | 263/263, 69.5 s |

All exit 0, no failure, none skipped (logs/step4-timing-summary.txt, timing/timing-*.log).

## Step 5: own-stream differential at H

`diff_run.py <h> final --vs=r12i`: build 0 warnings; 9,228 blocks, deterministic over two runs. `out-final.txt` is
byte-identical to `out-r13a.txt` and to `out-r12i.txt` (cmp); classify: vs r12i identical 9,228, vs r9f identical 9,228.
No block differs. (Against the older baselines, as before: vs main identical 3,290, wording-only 3,933, +codes 19, better 69,
worse 1,193, other 724; vs r8f identical 9,066, wording-only 129, +codes 33.) The probe file was removed afterwards.

## Step 6: benchmarks, alone (h at 2c7cf01, m at 21a0298, Release)

BenchmarkDotNet 0.15.8, `--job short` (ShortRun: 3 warmups, 3 iterations, 1 launch), as the status's "Current measurements
(BenchmarkDotNet, ShortRun)" say; no class sets a job of its own. Each class run head, main, head, main, one process at a
time, build servers shut down first; the two new classes at head only (main lacks them). All 28 runs exit 0
(bench/bench-summary.txt). Tables: bench/<side>-<n>-<Class>.md; full artifacts bench/runs/. Comparison: logs/step6-compare.txt.
Ratio = mean of head's two means / mean of main's two means.

| Class / case | head 1 | head 2 | main 1 | main 2 | time ratio | allocated head / main |
|---|---|---|---|---|---|---|
| Reader, index, 10 | 4.769 ± 1.124 µs | 4.524 ± 1.008 µs | 4.276 ± 1.004 µs | 4.241 ± 0.549 µs | 1.091 | 6.23 / 5.47 KB |
| Reader, index then read, 10 | 30.71 ± 13.65 µs | 30.29 ± 2.08 µs | 29.53 ± 1.53 µs | 29.63 ± 2.56 µs | 1.031 | 23.02 / 22.26 KB |
| Reader, index, 1000 | 173.4 ± 41.9 µs | 171.5 ± 4.4 µs | 168.8 ± 19.4 µs | 167.2 ± 41.1 µs | 1.026 | 83.59 / 82.84 KB |
| Reader, index then read, 1000 | 2.926 ± 2.289 ms | 2.861 ± 1.151 ms | 2.836 ± 2.119 ms | 2.755 ± 0.064 ms | 1.035 | 1748.84 / 1748.09 KB |
| Index 300k, Table | 25.83 ± 4.60 ms | 27.80 ± 3.80 ms | 24.75 ± 0.99 ms | 24.85 ± 1.96 ms | 1.081 | 11.45 / 11.45 MB |
| Index 300k, Stream | 7.713 ± 0.571 ms | 7.588 ± 0.237 ms | 7.480 ± 0.544 ms | 7.447 ± 0.347 ms | 1.025 | 16.33 / 16.33 MB |
| Index 300k, Update | 30.48 ± 3.91 ms | 30.10 ± 2.22 ms | 30.22 ± 7.22 ms | 30.75 ± 12.35 ms | 0.994 | 13.11 / 13.11 MB |
| Index 300k, Hybrid | 32.60 ± 2.65 ms | 32.19 ± 1.54 ms | 31.41 ± 3.62 ms | 31.36 ± 1.24 ms | 1.032 | 16.33 / 16.33 MB |
| Index 300k, Chain | 33.71 ± 3.96 ms | 33.91 ± 1.72 ms | 32.87 ± 2.63 ms | 32.81 ± 1.09 ms | 1.029 | 23.38 / 23.11 MB |
| Index 300k, Spread | 49.96 ± 29.97 ms | 49.54 ± 8.70 ms | 46.81 ± 1.93 ms | 48.13 ± 2.44 ms | 1.048 | 20.88 / 20.88 MB |
| Validation, validate, 10 | 29.78 ± 11.46 µs | 29.36 ± 18.60 µs | 29.16 ± 1.62 µs | 30.06 ± 1.36 µs | 0.998 | 14.42 / 14.28 KB |
| Validation, Arlington, 10 | 18.18 ± 9.91 µs | 17.76 ± 4.71 µs | 17.57 ± 0.87 µs | 18.03 ± 2.03 µs | 1.010 | 5.24 / 5.24 KB |
| Validation, open and validate, 10 | 64.11 ± 2.50 µs | 63.67 ± 4.91 µs | 62.54 ± 0.77 µs | 65.58 ± 2.36 µs | 0.997 | 37.69 / 36.79 KB |
| Validation, open, 10 | 4.459 ± 1.059 µs | 4.546 ± 2.914 µs | 4.190 ± 0.203 µs | 4.339 ± 0.857 µs | 1.056 | 6.23 / 5.47 KB |
| Validation, open and settle, 10 | 6.676 ± 0.525 µs | 6.491 ± 0.112 µs | 6.090 ± 0.815 µs | 6.430 ± 0.180 µs | 1.052 | 6.57 / 5.81 KB |
| Validation, validate, 1000 | 2.551 ± 0.015 ms | 2.515 ± 0.053 ms | 2.562 ± 0.184 ms | 2.653 ± 0.605 ms | 0.971 | 622.77 / 622.63 KB |
| Validation, Arlington, 1000 | 1.574 ± 0.230 ms | 1.600 ± 0.027 ms | 1.588 ± 0.308 ms | 1.642 ± 0.313 ms | 0.983 | 312.08 / 312.08 KB |
| Validation, open and validate, 1000 | 5.840 ± 3.724 ms | 5.672 ± 0.549 ms | 5.585 ± 0.453 ms | 5.611 ± 0.211 ms | 1.028 | 2371.79 / 2370.89 KB |
| Validation, open, 1000 | 171.3 ± 7.9 µs | 170.6 ± 128.3 µs | 164.7 ± 23.7 µs | 169.1 ± 2.9 µs | 1.024 | 83.59 / 82.84 KB |
| Validation, open and settle, 1000 | 356.9 ± 76.6 µs | 352.7 ± 9.0 µs | 350.2 ± 104.3 µs | 351.6 ± 11.4 µs | 1.011 | 83.94 / 83.18 KB |
| Probe, sound, 80 | 19.27 ± 0.92 µs | 19.29 ± 3.94 µs | 19.10 ± 6.14 µs | 19.20 ± 1.20 µs | 1.007 | 1.7 / 1.7 KB |
| Probe, blanks, 80 | 106.4 ± 54.0 µs | 105.2 ± 24.9 µs | 105.3 ± 7.7 µs | 103.8 ± 4.4 µs | 1.012 | 46.5 / 46.5 KB |
| Probe, sound, 800 | 198.1 ± 24.2 µs | 196.3 ± 17.7 µs | 196.7 ± 7.3 µs | 197.9 ± 157.4 µs | 1.000 | 7.32 / 7.32 KB |
| Probe, blanks, 800 | 336.1 ± 40.7 µs | 334.2 ± 25.5 µs | 334.8 ± 10.0 µs | 337.9 ± 23.8 µs | 0.996 | 428.41 / 428.41 KB |
| StreamLength 4096, 0, memory | 2.744 ± 0.167 ms | 2.862 ± 1.736 ms | 2.745 ± 0.194 ms | 2.723 ± 0.455 ms | 1.025 | 1.71 / 1.71 MB |
| StreamLength 4096, 0, file | 8.520 ± 3.278 ms | 9.123 ± 17.334 ms | 8.529 ± 0.608 ms | 8.345 ± 1.590 ms | 1.046 | 1.71 / 1.71 MB |
| StreamLength 4096, 2, memory | 3.966 ± 0.225 ms | 3.992 ± 0.212 ms | 4.036 ± 0.818 ms | 3.930 ± 0.189 ms | 0.999 | 2.69 / 2.67 MB |
| StreamLength 4096, 2, file | 12.07 ± 13.64 ms | 11.77 ± 2.04 ms | 11.67 ± 0.74 ms | 12.23 ± 14.16 ms | 0.997 | 2.69 / 2.67 MB |
| StreamLength 16384, 0, memory | 2.740 ± 0.246 ms | 2.756 ± 0.379 ms | 2.720 ± 0.174 ms | 2.701 ± 0.514 ms | 1.014 | 1.71 / 1.71 MB |
| StreamLength 16384, 0, file | 11.54 ± 1.50 ms | 12.98 ± 18.93 ms | 11.25 ± 0.31 ms | 11.70 ± 8.00 ms | 1.069 | 1.71 / 1.71 MB |
| StreamLength 16384, 2, memory | 6.469 ± 3.439 ms | 6.546 ± 3.855 ms | 5.834 ± 1.016 ms | 6.109 ± 1.122 ms | 1.090 | 2.70 / 2.67 MB |
| StreamLength 16384, 2, file | 19.43 ± 1.90 ms | 19.97 ± 16.54 ms | 18.93 ± 3.61 ms | 19.22 ± 3.90 ms | 1.033 | 2.70 / 2.67 MB |
| TrailerScan, `<< /Size 3 >>` | 492.8 ± 111.2 ms | 501.8 ± 262.8 ms | 498.9 ± 134.6 ms | 494.4 ± 125.6 ms | 1.001 | 3449.84 / 3449.62 KB |
| TrailerScan, `5 0 R` | 206.2 ± 24.3 ms | 199.9 ± 8.2 ms | 204.0 ± 130.0 ms | 196.7 ± 44.9 ms | 1.013 | 642.24 / 642.02 KB |
| TrailerScan, `null` | 198.8 ± 3.0 ms | 203.7 ± 287.0 ms | 199.0 ± 9.7 ms | 220.9 ± 30.9 ms | 0.959 | 262.34 / 262.12 KB |

(StreamLength's columns: stream size, bytes the length is wrong by, from a file.)

New in the batch, head only:

| Class / case | head 1 | head 2 | allocated |
|---|---|---|---|
| StreamEndSearch, binary | 422.8 ± 181.7 µs | 410.1 ± 5.0 µs | 7.2 KB |
| StreamEndSearch, comment-lines | 33.38 ± 0.59 ms | 32.65 ± 1.84 ms | 7.2 KB |
| StreamEndSearch, digits | 10.40 ± 0.74 ms | 11.38 ± 3.33 ms | 7.2 KB |
| StreamEndSearch, endst(...)lines [36] | 37.57 ± 1.02 ms | 37.29 ± 2.74 ms | 7.58 KB |
| StreamEndSearch, endst(...)space [34] | 8.771 ± 5.784 ms | 8.357 ± 0.490 ms | 7.58 KB |
| StreamEndSearch, hex-string | 663.0 ± 221.5 µs | 661.8 ± 18.5 µs | 7.2 KB |
| StreamEndSearch, nested-strings | 36.08 ± 0.84 ms | 34.99 ± 0.81 ms | 7.2 KB |
| StreamEndSearch, one-byte-tokens | 41.28 ± 10.27 ms | 40.94 ± 0.44 ms | 7.2 KB |
| StreamEndSearch, one-token | 734.0 ± 742.7 µs | 693.7 ± 147.5 µs | 7.2 KB |
| StreamEndSearch, open-string | 703.0 ± 65.2 µs | 690.5 ± 33.7 µs | 7.2 KB |
| StreamEndSearch, text-lines | 403.0 ± 70.5 µs | 398.7 ± 17.6 µs | 7.2 KB |
| StreamEndSearch, white-space | 5.515 ± 0.117 ms | 5.354 ± 1.019 ms | 7.2 KB |
| ObjectScan, sound | 2.915 ± 0.153 ms | 2.911 ± 0.318 ms | 1840.8 KB |
| ObjectScan, plain-headers | 38.41 ± 1.65 ms | 38.32 ± 0.75 ms | 18176.6 KB |
| ObjectScan, comment-headers | 45.10 ± 6.59 ms | 44.79 ± 1.59 ms | 18176.6 KB |
| ObjectScan, comment-lines | 8.051 ± 0.139 ms | 8.069 ± 0.243 ms | 6.16 KB |
| ObjectScan, percent-strings | 15.54 ± 0.89 ms | 15.78 ± 0.24 ms | 7.59 KB |

Reading: times within ShortRun's spread everywhere (ratios 0.96 to 1.09, each with error bars wider than the gap; the
largest, Reader index 10 at 1.09 and Index Table at 1.08, sit inside head's own run-to-run spread). Allocation: opening
costs about 770 bytes more on every document (Reader/Validation open 5.47 -> 6.23 KB at 10 pages, 82.84 -> 83.59 KB at
1000; open-and-settle the same); the chain of a thousand sections 23.11 -> 23.38 MB (about 280 bytes a section); a wrong
`/Length` 2.67 -> 2.69/2.70 MB for a thousand streams (20 to 30 bytes a stream); validation 10 pages +140 B; everything
else equal to the byte or to 0.2 KB.

## Step 7: documentation site at H, alone

`cd docs/website && npm ci && npm run build`: `npm ci` exit 0 in 26 s (npm's own notices: 33 audit vulnerabilities in the
site's dependency tree, `whatwg-encoding@3.1.1` deprecated, core-js's postinstall blocked by allowScripts); `npm run build`
exit 0 in 56 s: docfx metadata 42 items, 0 warnings, 0 errors, 42 of 42 API pages tidied; Docusaurus server and client
compiled, "Generated static files in build"; the site check: 269 built pages, none showing compiled MDX, escaped markup or
a misplaced banner. No Docusaurus warning or error (no broken link or anchor reported); the only other line is Node's
`ExperimentalWarning: localStorage is not available` (logs/step7-npm-ci.txt, logs/step7-build.txt).

## Cleanup

Worktrees h, cov and m removed (the hard-linked remote corpus removed with `rm -rf` first); the maintainer's checkout was
not touched.
