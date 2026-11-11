import re
import sys
import textwrap

BASE = sys.argv[1]
HEAD = BASE + '/status.head.md'
OUT = BASE + '/status.md'
ENTRY = BASE + '/work/entry.src'

PR = '{{PR: the pull request\'s number}}'
WIDTH = 120


def wrap(text, first, cont):
    protected = re.sub(r'`[^`]*`', lambda m: m.group(0).replace(' ', '\x00'), text)
    protected = re.sub(r'\{\{[^}]*\}\}',
                       lambda m: m.group(0).replace(' ', '\x00') if len(m.group(0)) <= 60 else m.group(0), protected)
    lines = textwrap.wrap(protected, WIDTH, initial_indent=first, subsequent_indent=cont,
                          break_long_words=False, break_on_hyphens=False)
    lines = [l.replace('\x00', ' ') for l in lines]
    for l in lines[1:]:
        body = l[len(cont):]
        if re.match(r'(- |\+ |\* |#{1,6}( |$)|\d+[.)] |> )', body):
            raise SystemExit('continuation line would change the markup: ' + l)
    return lines


def wrap_source(src):
    out = []
    for raw in src.split('\n'):
        if raw.strip() == '' or raw.startswith('#'):
            out.append(raw)
            continue
        m = re.match(r'^( *)(- )?', raw)
        lead = m.group(1)
        bullet = m.group(2) or ''
        first = lead + bullet
        cont = ' ' * len(first)
        out.extend(wrap(raw[len(first):], first, cont))
    return out


lines = open(HEAD, encoding='utf-8').read().split('\n')
assert lines[-1] == ''


def expect(n, prefix):
    actual = lines[n - 1]
    if not actual.startswith(prefix):
        raise SystemExit('line %d is %r, expected it to start with %r' % (n, actual, prefix))


# Every edit is (first line, last line, new lines), on the head's line numbers, applied from the bottom up.
edits = []

# 1. At a glance: the current milestone's batches and debts.
expect(26, '  52), with [#308] on 2026-10-30; the eighteenth')
expect(36, '  the eighteenth [#310] under M03 and [#311] under M23.')
milestone = (
    "52), with [#308] on 2026-10-30; the eighteenth, [#167] and [#168], what a rebuild leaves a caller and what the "
    "validator's analyses cost, with [#201], [#277], [#307] and [#312], with [#318] on 2026-11-03; the nineteenth, "
    "[#188], [#197] and [#156], where the chain's sections lie and what it reports of one it reads in part, with [#207], "
    "[#208], [#210], [#221], [#222], [#241], [#261], [#264], [#281] and [#317], in [#" + PR + "]. Then the other "
    "batches, slices 4 to 6 ([#60] to [#62]); then the milestone's adversarial review, slice 7 ([#137], ADR 46), once "
    "every other issue filed under M02 is closed. The debts come from the reader's own work, the threat model's first "
    "version ([#135], `docs/threat-model.md`), M01's review after the fact ([#136], `docs/reviews/M01.md`), the planning "
    "of step 4 ([#199] to [#202]), its second batch ([#207] to [#210]), its third ([#212]), its fifth ([#215], [#216]), "
    "its sixth ([#218] to [#224]), its eighth ([#229], [#230]), its ninth ([#237]), its tenth ([#241], [#247]), its "
    "twelfth ([#250]), slice 4's blockers ([#253]), its thirteenth ([#255] to [#257]), its fourteenth ([#259] to "
    "[#264]), its fifteenth ([#273] to [#277]), its sixteenth ([#279] to [#283], [#287] to [#290], [#292] to [#296]), its "
    "seventeenth ([#307]), its eighteenth ([#309], [#312] to [#315], [#317]) and its nineteenth ([#320] to [#330], "
    "[#332], [#333], [#336]); [#123] moved to M20. The seventeenth filed [#305] and [#306] under M23, the eighteenth "
    "[#310] under M03 and [#311] under M23, the nineteenth [#331] under M03."
)
edits.append((26, 36, wrap(milestone, '  ', '  ')))

# 2. At a glance: tests.
expect(42, '- **Tests**: 6,657 unit on [#318]')
expect(51, '  checks, at its one rerun, on the library code [#318] ends with.')
tests = (
    "**Tests**: {{UNIT: the unit count on the committed corpus at the branch's last code, 6,657 on [#318]'s, its "
    "skipped count to confirm}} unit on [#" + PR + "]'s branch (20 skipped: 2 by design, the theory over the remote "
    "corpus's streams whose length is wrong, the fifteen remote documents of the theory over the corpus's filter chains, "
    "which have no document without them, the test held to PDFBox's remote report, and iPRES's stream that lost its "
    "`endstream`) + 1,495 integration (skipped without Docker) + 48 for the remote corpus's fetcher + 43 for the "
    "roadmap's mirror on GitHub. With all 242 remote documents fetched, on [#" + PR + "]'s branch: {{UNIT_REMOTE: the "
    "unit count with them at the branch's last code}} unit on the maintainer's machine — 11,052 at `19a2296` —, 3 "
    "skipped — the laziness test on the two documents recorded as unsupported until [#47], which every other test holds "
    "to their expectations, and the private manifest the machine lacks —; {{INTEGRATION: the integration count against "
    "qpdf in its container, passed and skipped}}; and `Remote corpus` run {{REMOTE_RUN: the run number}}'s {{REMOTE: "
    "its acceptance tests and referee checks}}, on the library code [#" + PR + "] ends with."
)
edits.append((42, 51, wrap(tests, '- ', '  ')))

# 3. At a glance: coverage, its first part; the list of what is left stays as [#318] gave it.
expect(52, '- **Coverage**: on the committed corpus')
expect(58, "  reach. Forty-seven are [#297]'s, at the line numbers [#318] gives them; three are the batch's, in")
coverage = (
    "**Coverage**: on the committed corpus, as Codecov counts it (a line with an untaken branch is partial, the "
    "generated Arlington tables left out), {{COVERAGE: the share of `src/` and its lines covered on the branch's last "
    "code; of the patch's coverable lines, those run and those whole, and its branches}} on [#" + PR + "]'s branch — "
    "at `dfb03fc`, of its patch's 1,462 coverable lines 1,460 run and 1,456 whole, the six not whole a defensive branch "
    "of the parse of an object no section records (two lines), the line test's guard against a place before the bytes "
    "it read (two), a branch the compiler adds in `SizeWrongRule` (71) and the defensive arm of the walk past a "
    "header's signs, none of which an input takes —; on "
    "[#318]'s, 99.44 %, 8,859 of 8,909 lines, of its patch's 1,123 coverable lines 1,122 run and 1,120 whole, and 933 of "
    "their 935 branches. Codecov's patch report leaves `ObjectStreamDependencies.cs` out, so [#318]'s figures were "
    "counted from the unit suite's own report by Codecov's rule, which gives Codecov's totals for `663cb80` to the line. "
    "{{COVERAGE_LEFT: how many lines are left on the branch, and the line numbers its code gives those listed below, "
    "`PdfFileReader`'s moved by the batch}} The 50 left on [#318]'s branch are those the rule of 2026-09-29 leaves "
    "(`CLAUDE.md`, *Coverage*), and five that only faults filed reach. Forty-seven are [#297]'s, at the line numbers "
    "[#318] gives them; three are batch 18's, in"
)
edits.append((52, 58, wrap(coverage, '- ', '  ')))

# 4. At a glance: mutation testing.
expect(105, '  run ([#318]).')
mutation = (
    "run ([#318]). Batch 19 ran it at each stage of its implementation and of its fixes that changed code in `src/`, "
    "and over its whole patch at its end, 3,751 mutants, of which 15 a stage record still covered: {{ADR53: the "
    "batch-wide tally — killed, equivalent, survivors filed as debt, not viable}} ([#" + PR + "])."
)
edits.append((105, 105, wrap(mutation, '  ', '  ')))

# 5. At a glance: CI, its first line and its end.
expect(106, "- **CI**: green on `main` at `75999e4` (CI run 859), and on [#318]'s branch at `755750b` (CI run 924). `Remote")
ci_first = (
    "**CI**: green on `main` at `21a0298` ({{CI_MAIN: the CI run green on main at 21a0298}}), and on [#" + PR + "]'s "
    "branch at {{CI_BRANCH: the branch's last commit and its green CI run}}. `Remote"
)
edits.append((106, 106, wrap(ci_first, '- ', '  ')))
expect(166, '  other passing, and passed whole at its one rerun.')
ci_end = (
    "other passing, and passed whole at its one rerun. {{NIGHTLY: the nightly `Remote corpus` runs on `main` at "
    "`21a0298`, [#318] merged, and their outcome}}. On [#" + PR + "]'s branch, {{REMOTE: the `Remote corpus` runs, "
    "their commits and outcome, acceptance tests and referee checks}}."
)
edits.append((166, 166, wrap(ci_end, '  ', '  ')))

# 6. At a glance: the corpus's last green nightly run.
expect(171, "  corpus`, last green in run 97 on `main` on 2026-11-02, from the cache alone.")
edits.append((171, 171, [lines[170].replace(
    "run 97 on `main` on 2026-11-02, from the cache alone",
    "run {{NIGHTLY_LAST: the last green nightly run on `main`, its date, and whether from the cache alone}}")]))

# 7. Current measurements: two rows and a paragraph.
expect(241, '| The same, each stream declared 2 bytes too long')
rows = [
    "| Searching a damaged stream's data for its `endstream`: text lines, then nested strings, the worst shape | "
    "generated, 4 MB | {{BENCH: StreamEndSearchBenchmarks' times, text lines and nested strings}} | {{BENCH: their "
    "allocation, 6.9 KB in the review's first round}} |",
    "| Rebuilding an index by scanning for object headers, each with a comment between its tokens | generated, 4 MB, "
    "100,000 headers | {{BENCH: ObjectScanBenchmarks' time for comment headers}} | {{BENCH: its allocation, about "
    "18 MB}} |",
]
edits.append((241, 241, [lines[240]] + rows))
expect(347, '(`TrailerScanBenchmarks`, a medium job, twice, alternated).')
measures = (
    "Batch 19 ([#" + PR + "]) {{BENCH: what the final runs give of indexing, reading every page, the probe, "
    "validating, the stream-length rows and the rebuild's scan for trailers, against `main`, and how they were run}}. "
    "Measured as the batch went, on a shared machine: the search for a damaged stream's `endstream`, which now lexes "
    "from its start a line that holds a `%`, costs what it did on text and on binary data, about 0.5 ms for 4 MB, and "
    "allocates 6.9 KB against 6.4 KB, whatever the data (`StreamEndSearchBenchmarks`, ShortRun, twice, alternated with "
    "`main`, in the review's first round); data built to cross such a line at every byte or two costs more, in time "
    "linear in its size — 4 MB of one-byte tokens 36 to 39 ms, of comment lines 27 ms, of nested strings 44 ms, each "
    "about 0.5 ms on `main`, which took no header with a comment —, and a timing test holds it so, 4 MB taking 3.99 "
    "times what 1 MB takes; the review's fourth round halved what a keyword waiting on comment lines costs, 87.3 to "
    "43.5 ms, and measured nested strings, the worst shape, at 58 ms, some 14 ns a byte. The rebuild's scan for object "
    "headers costs what it did on sound files, 3.0 to 3.2 ms for 4 MB, and on plain headers, 32 to 34 ms against 34 to "
    "36; lines that hold a `%` 6.9 to 7.5 ms against 1.3, strings that hold one 12.7 to 13.7 ms against 2.8, and "
    "100,000 headers with a comment between their tokens 40 to 43 ms and 18 MB, indexed where `main` found none "
    "(`ObjectScanBenchmarks`, the same runs). The chain pays for the numbers it refuses while it is read, about 60 "
    "bytes a number: 6.0 MB for 100,000 refused rows, and 1.01 GB for a stream whose every row is refused at the "
    "default bound on the index's entries; 1,024 chained streams that cannot be read open in about four times what 256 "
    "take, 197 to 201 ms against 47 to 53 under a `MaxTrailerLength` of 8 KB. A thousand tables naming one stream of "
    "100,000 rows read it once, 168 bytes more a naming. The white space before a named section is crossed once a "
    "position, its probes reading less than ten times the bytes to the first token's end — 9.14 times at worst, 2,044 "
    "spaces before `xref` —, and 84,288 bytes at most under the default `MaxTrailerLength`, 1.29 times the 65,568 they "
    "reach; a link naming the start of a run of 65,530 spaces before a table reads 343,127 bytes, the table's windows "
    "reading the run again ([#295])."
)
edits.append((347, 347, [lines[346], ''] + wrap(measures, '', '')))

# 8. Next concrete step: items 18 and 19.
expect(406, "   18. [#167] and [#168], what a rebuild leaves a caller")
expect(409, "   19. [#188], [#197] and [#156], with [#207], [#208], [#210], [#221], [#222], [#241], [#261] and [#264].")
item18 = (
    "[#167] and [#168], what a rebuild leaves a caller and what the validator's analyses cost, with [#201], [#277] and "
    "[#307], and [#312], which the batch filed — done, merged with [#318] on 2026-11-03, `Remote corpus` runs 98 and "
    "99, the second at its one rerun, green on its branch (journal of 2026-10-30)."
)
item19 = (
    "[#188], [#197] and [#156], where the chain's sections lie and what it reports of one it reads in part, with "
    "[#207], [#208], [#210], [#221], [#222], [#241], [#261] and [#264], and [#281] and [#317], placed with it — done, "
    "in [#" + PR + "], `Remote corpus` run {{REMOTE_RUN: the run number}} green on its branch (journal of "
    "2026-11-03)."
)
edits.append((406, 409, wrap(item18, '   18. ', ' ' * 7) + wrap(item19, '   19. ', ' ' * 7)))

# 9. Next concrete step: where batch 19's debts go.
expect(455, '   with [#168].')
placed = (
    "with [#168]. Of the fifteen batch 19 filed, [#331], the search for a classic table's trailer that a source "
    "serving short reads sends round one window without end, goes under M03, with [#52], where the source's contract "
    "is written. Of the fourteen left under M02, twelve go with the batch whose code they share: [#320], the rows a "
    "relocation candidate leaves when it is dropped, with [#189], the next batch, whose fix settles which of a "
    "number's definitions the index keeps; [#327], a header whose number or generation carries a sign, with [#171], "
    "whose fix reads the byte before a header's number in the same method; [#322], a table whose `trailer` keyword no "
    "dictionary follows, [#323], a first section that cannot be read reported only as a rebuild, and [#325], "
    "`xref.section-malformed` silent where the trailer is at fault too, with [#132], [#218] and [#229], whose rebuilds "
    "at opening, search for the catalog and rule over the same records decide them; [#326], counts grouped in some "
    "messages and not others, with [#162], whose filter reports it rewords; [#324], a classic table mid-chain whose bad "
    "row ends the chain, [#328], a `/Prev` in a trailer `MaxTrailerLength` cut, and [#329], the `/Prev` of a stream at "
    "which a value's cut ends the chain, with [#163], the reader at its guards; [#321], an `/XRefStm` copied two "
    "revisions late, [#330], a sound `/Prev` reported as a loop where a newer `/XRefStm` read its stream first, and "
    "[#332], blocked by [#330], a `/Prev` a few bytes off a stream a newer `/XRefStm` read, with [#107] and [#111], "
    "whose rules the maintainer names. [#333], an older revision's stream taken near a hybrid table's `/Prev` before "
    "its own revision's table, {{PLACE_333: the batch it goes with — its body names none; beside #332, #330 and #321 in "
    "batch 31, the other side of the same search, fits}}; and [#336], the test of the seeded comparer that fails under "
    "coverage on Windows, {{PLACE_336: the batch it goes with — its body names none; with what remains of #158 in "
    "batch 32, the tests' own debt, fits}}."
)
edits.append((455, 455, wrap(placed, '   ', '   ')))

# 10. The journal: batch 19's entry first.
expect(475, '## Journal')
expect(477, '### 2026-10-30 — Batch 18')
entry = wrap_source(open(ENTRY, encoding='utf-8').read().rstrip('\n'))
edits.append((477, 477, entry + [''] + [lines[476]]))

# 11. The record of 2026-09-30: the stream-end rule follows the scan since batch 19.
expect(2900, "  read four times the file (`EndStreamSearchPasses`); a header with a comment between its tokens is not taken, as in")
expect(2903, "  crafted files still order-dependent are [#138] (M23). SAMHSA's object 27, now read whole, became [#56]'s case.")
record = (
    "read four times the file (`EndStreamSearchPasses`); a header with a comment between its tokens was not taken, as "
    "in the rebuild's scan then (maintainer, 2026-09-30), and both take it since batch 19 ([#261]), as a load reads it. "
    "`stream.length-invalid` went from 81 to 96 reports, each stream reported once; 15 of the 16 wrong lengths read to "
    "their `endstream`, held to qpdf by `StreamLengthRefereeTests`; crafted files still order-dependent are [#138] "
    "(M23). SAMHSA's object 27, now read whole, became [#56]'s case."
)
edits.append((2900, 2903, wrap(record, '  ', '  ')))

# 12. Link definitions, in the file's order.
expect(3175, '[#318]: https://github.com/AdCodicem/AdCodicem.Pdf/pull/318')
defs = ['[#%d]: https://github.com/AdCodicem/AdCodicem.Pdf/issues/%d' % (n, n)
        for n in list(range(320, 334)) + [336]]
defs.append('[#' + PR + ']: https://github.com/AdCodicem/AdCodicem.Pdf/pull/' + PR)
edits.append((3175, 3175, [lines[3174]] + defs))

for first, last, new in sorted(edits, key=lambda e: -e[0]):
    lines[first - 1:last] = new

open(OUT, 'w', encoding='utf-8', newline='\n').write('\n'.join(lines))
print('written', OUT, len(lines) - 1, 'lines')
