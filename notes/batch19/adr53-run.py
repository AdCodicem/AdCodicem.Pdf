"""ADR 53 mutation harness for the batch-wide step (batch 19 of M02, head written in each shard file).

usage: python -I run.py SHARD.json COMMIT WORKERS [options]

  SHARD.json   {"shard": "07", "head": "<sha>", "mutants": [{"id", "file", "old", "new", "site", "filter": [class, ...],
               "timeout": seconds}, ...]}: each mutant is one exact replacement of OLD by NEW in FILE (LF-normalized
               match, OLD occurring once), and FILTER the test classes that reach its site (the first filter).
  COMMIT       the commit every worker's worktree is checked out at; must equal the shard's head unless --any-commit.
  WORKERS      worker count; each owns a git worktree at COMMIT, W/adr53/w<shard>-<n>, the remote corpus hard-linked.

options:
  --ids A,B,C      run only these mutant ids (default: all of the shard not yet in its summary)
  --rerun          run ids already in the summary again (their new line is appended; the last line counts)
  --first-only     stop after the first filter: a mutant it does not kill is written SURVIVED-FIRST (run it later with
                   --ids ... --rerun to give it the rest of the suite)
  --stop-on-fail   pass xUnit's --stop-on-fail to the first filter: a killed mutant's run stops at its first failure, which
                   halves the time per mutant at 8 workers; its failure count is then a floor, not the count
  --no-confirm     do not run a kill by one or two failing tests again
  --keep           keep the worktrees at the end (default: removed, corpus links first)
  --repo PATH      a worktree of the repository to run `git worktree add` from (default: the current directory's)
  --corpus PATH    the remote corpus to hard-link (default: the maintainer's checkout's tests/corpus/remote)
  --any-commit     allow COMMIT to differ from the shard's head

For each mutant, a worker restores the file to the commit's text, applies the one replacement, builds the test project
in Release without warnings as errors and without analyzers (an in-memory check of the catalog already marked the
mutants the compiler refuses: the IL is the same, and a warning no longer makes a mutant 'not viable'), runs the first
filter, and, if it does not kill the mutant, the whole AdCodicem.Pdf.Tests project but for the timing, budget,
allocation and memory groups (which run alone, never here) and for the first filter's classes, which passed on that same
build (the two runs together are the whole project). A kill by at most two failing tests is confirmed by running
those tests again; if they pass then, the kill is not counted and the mutant goes on to the suite. A test run past its
timeout counts as a kill (the mutant loops); a test process that crashes counts as a kill (stack overflow, the 3 GB heap
limit).

One line per mutant goes to W/adr53/summary-<shard>.txt and one JSON object to W/adr53/results-<shard>.jsonl; logs go to
W/adr53/logs/<shard>/<id>-{build,first,confirm,suite}.txt (test logs past 256 KB keep their head and tail). The states:
KILLED (by the first filter or the rest of the suite; 'fail', 'timeout' or 'crash'), SURVIVED, NOT VIABLE (a compiler
error), BUILD ERROR (a build failing without one: not counted, retried on the next run of the shard), NOT APPLIED (OLD
does not occur once). An interrupted run is resumed by running the same command: ids already in the summary are skipped.

The run, as measured on this machine (16 cores, 32 GB): two workers per agent, at most four agents at once (8 workers:
12 gave less throughput than 8). From the agent's own worktree, after `git fetch origin ccr-6d6c0ee6-dqm4lm-188`:

  export DOTNET_CLI_UI_LANGUAGE=en-US
  timeout 14400 python -I W/adr53/run.py W/adr53/shards/shard-07.json 19a2296 2

W is C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud.
"""
import json
import os
import queue
import re
import shutil
import subprocess
import sys
import threading
import time

HERE = os.path.dirname(os.path.abspath(__file__))
PROJ = 'tests/AdCodicem.Pdf.Tests/AdCodicem.Pdf.Tests.csproj'
BIN = 'tests/AdCodicem.Pdf.Tests/bin/Release/net10.0'
EXE = 'AdCodicem.Pdf.Tests.exe'
DEFAULT_CORPUS = 'C:/Users/clemm/repos/AdCodicem.Pdf/tests/corpus/remote'
BUILD = ['dotnet', 'build', PROJ, '-c', 'Release', '-m:1', '-nologo', '-p:TreatWarningsAsErrors=false', '-p:RunAnalyzers=false']
EXCLUDE = ['--filter-not-class', '*ReaderTimingTests', '--filter-not-class', '*HostileInputMemoryTests',
           '--filter-not-method', '*budget*', '--filter-not-method', '*Budget*', '--filter-not-method', '*llocat*']
SUITE_TIMEOUT = 2400
BUILD_TIMEOUT = 900
LOG_CAP = 256 * 1024

env = dict(os.environ, DOTNET_CLI_UI_LANGUAGE='en-US', DOTNET_GCHeapHardLimit='0xC0000000', DOTNET_NOLOGO='1',
           DOTNET_CLI_TELEMETRY_OPTOUT='1')

FAILED = re.compile(r'^\s*failed (AdCodicem\.Pdf\.Tests\.[^\s(]+)', re.M)
TOTAL = re.compile(r'total: (\d+)\s+failed: (\d+)\s+succeeded: (\d+)\s+skipped: (\d+)')


def arg(name, default=None):
    if name in sys.argv:
        return sys.argv[sys.argv.index(name) + 1]
    return default


# A process Windows could not start (STATUS_DLL_INIT_FAILED, seen as 0xC0000142 under load): it wrote nothing and ran
# nothing, so it is neither a build failure nor a crash of the tests. It is started again after a wait; six failures in
# a row raise, which the worker reports as a harness error (not counted, run again on the next run of the shard).
START_FAILED = (3221225794, -1073741502)
START_WAITS = (10, 20, 40, 80, 160)


def process_count():
    try:
        out = subprocess.run(['tasklist', '/FO', 'CSV', '/NH'], capture_output=True, text=True, timeout=60).stdout
        names = [line.split('","')[0].strip('"').lower() for line in out.splitlines() if line]
        return (f'{len(names)} processes, {names.count("dotnet.exe")} dotnet, {names.count("conhost.exe")} conhost, '
                f'{names.count("adcodicem.pdf.tests.exe")} test hosts, {names.count("vbcscompiler.exe")} compiler servers')
    except Exception as error:  # noqa: BLE001 - only a note
        return f'tasklist failed: {error!r}'


def run(cmd, cwd, timeout, log):
    """Runs CMD in CWD, output to LOG; returns the exit code, or 'timeout' after killing the process tree. A process
    that could not start is started again after a wait (START_WAITS); one that never starts raises RuntimeError."""
    for attempt in range(len(START_WAITS) + 1):
        rc = run_once(cmd, cwd, timeout, log)
        if rc not in START_FAILED or os.path.getsize(log) > 0:
            return rc
        note = (f'{time.strftime("%Y-%m-%d %H:%M:%S")} {os.path.basename(cmd[0])} in {cwd} did not start (rc={rc}, '
                f'attempt {attempt + 1}): {process_count()}\n')
        with open(os.path.join(HERE, 'startfail.log'), 'a', encoding='utf-8') as handle:
            handle.write(note)
        if attempt < len(START_WAITS):
            time.sleep(START_WAITS[attempt])
    raise RuntimeError(f'{os.path.basename(cmd[0])} did not start {len(START_WAITS) + 1} times in a row (rc={rc})')


def run_once(cmd, cwd, timeout, log):
    with open(log, 'w', encoding='utf-8', errors='replace') as handle:
        process = subprocess.Popen(cmd, cwd=cwd, env=env, stdout=handle, stderr=subprocess.STDOUT)
        try:
            return process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            subprocess.run(['taskkill', '/PID', str(process.pid), '/T', '/F'], capture_output=True)
            try:
                process.wait(timeout=60)
            except subprocess.TimeoutExpired:
                pass
            return 'timeout'


def read_log(log):
    body = open(log, encoding='utf-8', errors='replace').read()
    if len(body) > LOG_CAP:
        open(log, 'w', encoding='utf-8').write(body[:LOG_CAP - 16384] + '\n[... cut ...]\n' + body[-16384:])
    return body


def outcome(rc, body):
    """(state, failed tests, totals) of a test run: 'pass', 'fail', 'timeout', 'crash' or 'none' (no test ran)."""
    failed = []
    for name in FAILED.findall(body):
        if name not in failed:
            failed.append(name)
    total = TOTAL.search(body)
    totals = tuple(int(x) for x in total.groups()) if total else None
    if rc == 'timeout':
        return 'timeout', failed, totals
    if rc == 0:
        return ('none' if totals and totals[0] == 0 else 'pass'), failed, totals
    if rc == 8 or (totals and totals[0] == 0 and not failed):
        return 'none', failed, totals
    if failed or rc == 2:
        return 'fail', failed, totals
    return 'crash', failed, totals


def short(name):
    return name.replace('AdCodicem.Pdf.Tests.', '')


class Worker(threading.Thread):
    def __init__(self, n, ctx):
        super().__init__(daemon=True)
        self.n = n
        self.ctx = ctx
        self.tree = os.path.join(HERE, f'w{ctx["shard"]}-{n}')
        self.pristine = {}

    def log(self, msg):
        with self.ctx['lock']:
            print(f'[w{self.n}] {msg}', flush=True)

    def setup(self):
        ctx = self.ctx
        if not os.path.isdir(self.tree):
            subprocess.run(['git', 'worktree', 'add', '--detach', self.tree, ctx['commit']], cwd=ctx['repo'], check=True,
                           capture_output=True)
        else:
            subprocess.run(['git', 'checkout', '-q', '--force', '--detach', ctx['commit']], cwd=self.tree, check=True)
        remote = os.path.join(self.tree, 'tests', 'corpus', 'remote')
        if not os.path.isdir(remote):
            subprocess.run(['cp', '-al', ctx['corpus'], remote], check=True)
        base = os.path.join(ctx['logs'], f'w{self.n}-baseline-build.txt')
        rc = run(['dotnet', 'build', PROJ, '-c', 'Release', '-m:1', '-nologo', '-p:TreatWarningsAsErrors=false',
                  '-p:RunAnalyzers=false'], self.tree, BUILD_TIMEOUT, base)
        if rc != 0:
            raise RuntimeError(f'baseline build failed in {self.tree}: see {base}')

    def text_of(self, rel):
        if rel not in self.pristine:
            raw = open(os.path.join(self.tree, rel), 'rb').read()
            self.pristine[rel] = raw
        return self.pristine[rel]

    def restore(self, rel):
        open(os.path.join(self.tree, rel), 'wb').write(self.text_of(rel))

    def run(self):
        try:
            self.setup()
        except Exception as error:  # noqa: BLE001 - reported, the other workers go on
            self.log(f'setup failed: {error}')
            self.ctx['errors'].append(str(error))
            return
        while True:
            try:
                m = self.ctx['jobs'].get_nowait()
            except queue.Empty:
                return
            try:
                self.mutant(m)
            except Exception as error:  # noqa: BLE001
                self.log(f'{m["id"]}: harness error {error!r}')
                self.ctx['errors'].append(f'{m["id"]}: {error!r}')
                self.restore(m['file'])

    def tests(self, args, timeout, log):
        rc = run([os.path.join(self.tree, BIN, EXE)] + args, os.path.join(self.tree, BIN), timeout, log)
        return outcome(rc, read_log(log)), rc

    def mutant(self, m):
        ctx = self.ctx
        mid, rel = m['id'], m['file']
        t0 = time.time()
        raw = self.text_of(rel)
        text = raw.decode('utf-8')
        crlf = '\r\n' in text
        lf = text.replace('\r\n', '\n')
        result = {'id': mid, 'file': rel, 'site': m.get('site'), 'worker': self.n}
        count = lf.count(m['old'])
        overlapping = lf.find(m['old'], lf.find(m['old']) + 1) >= 0 if count == 1 else True
        if count != 1 or overlapping:
            result.update(status='NOT APPLIED', detail=f'{count} occurrences')
            return self.write(result, t0)
        mutated = lf.replace(m['old'], m['new'])
        if crlf:
            mutated = mutated.replace('\n', '\r\n')
        open(os.path.join(self.tree, rel), 'wb').write(mutated.encode('utf-8'))
        logs = ctx['logs']
        try:
            build_log = os.path.join(logs, f'{mid}-build.txt')
            tb = time.time()
            rc = run(BUILD + ['--no-restore'], self.tree, BUILD_TIMEOUT, build_log)
            result['build_s'] = round(time.time() - tb, 1)
            if rc != 0:
                errors = [line.strip() for line in open(build_log, encoding='utf-8', errors='replace') if ': error ' in line]
                compiler = [e for e in errors if ': error CS' in e]
                first = (compiler or errors or [f'build rc={rc}'])[0]
                first = re.sub(r'\s*\[[^\]]*\.csproj\]$', '', first.replace(self.tree + os.sep, '').replace(self.tree.replace('/', '\\') + '\\', ''))
                if not compiler:
                    # not the compiler refusing the mutant (an SDK update, a locked file, a timeout): run it again later
                    result.update(status='BUILD ERROR', detail=f'no compiler error, rerun it: {first[:250]}')
                    return self.write(result, t0)
                result.update(status='NOT VIABLE', detail=first[:300])
                if m.get('expect') != 'not viable':
                    result['detail'] += ' [the in-memory check passed it: look at it]'
                return self.write(result, t0)
            if m.get('expect') == 'not viable':
                # the catalog's in-memory check refused it, the build did not: it runs like any other mutant
                result['built_though_refused_in_memory'] = True
            classes = m.get('filter') or []
            if classes:
                args = []
                for c in classes:
                    args += ['--filter-class', c if '.' in c or '*' in c else f'AdCodicem.Pdf.Tests.{c}']
                if ctx['stop_on_fail']:
                    args += ['--stop-on-fail', 'on']
                tf = time.time()
                (state, failed, totals), rc = self.tests(args + EXCLUDE[4:], m.get('timeout', 300),
                                                         os.path.join(logs, f'{mid}-first.txt'))
                result['first_s'] = round(time.time() - tf, 1)
                result['first_totals'] = totals
                if state in ('fail', 'timeout', 'crash'):
                    if state == 'fail' and 0 < len(failed) <= 2 and not ctx['no_confirm']:
                        cargs = []
                        for name in failed:
                            cargs += ['--filter-method', name]
                        (cstate, cfailed, _), _ = self.tests(cargs, m.get('timeout', 300), os.path.join(logs, f'{mid}-confirm.txt'))
                        if cstate == 'pass':
                            result['unconfirmed'] = [short(x) for x in failed]
                            state = 'pass'
                    if state != 'pass':
                        result.update(status='KILLED', stage='first', how=state, failed=len(failed) if state == 'fail' else None,
                                      first_failed=short(failed[0]) if failed else None,
                                      failed_tests=[short(x) for x in failed[:20]], rc=rc)
                        return self.write(result, t0)
                if ctx['first_only'] and state == 'pass':
                    result.update(status='SURVIVED-FIRST')
                    return self.write(result, t0)
            # The rest of the suite: the classes the first filter ran on this same build passed, so they are not run
            # again; the union is the whole project but for the timing, budget, allocation and memory groups.
            rest = []
            if state == 'pass' if classes else False:
                for c in classes:
                    rest += ['--filter-not-class', c if '.' in c or '*' in c else f'AdCodicem.Pdf.Tests.{c}']
            ts = time.time()
            (state, failed, totals), rc = self.tests(EXCLUDE + rest, SUITE_TIMEOUT, os.path.join(logs, f'{mid}-suite.txt'))
            result['suite_s'] = round(time.time() - ts, 1)
            result['suite_totals'] = totals
            if state in ('fail', 'timeout', 'crash'):
                if state == 'fail' and 0 < len(failed) <= 2 and not ctx['no_confirm']:
                    cargs = []
                    for name in failed:
                        cargs += ['--filter-method', name]
                    (cstate, _, _), _ = self.tests(cargs, 600, os.path.join(logs, f'{mid}-confirm-suite.txt'))
                    if cstate == 'pass':
                        result.update(status='SURVIVED', unconfirmed=[short(x) for x in failed])
                        return self.write(result, t0)
                result.update(status='KILLED', stage='suite', how=state, failed=len(failed) if state == 'fail' else None,
                              first_failed=short(failed[0]) if failed else None, failed_tests=[short(x) for x in failed[:20]], rc=rc)
            elif state == 'pass':
                result.update(status='SURVIVED')
            else:
                result.update(status='ERROR', detail=f'suite ran no test rc={rc}')
            return self.write(result, t0)
        finally:
            self.restore(rel)

    def write(self, result, t0):
        result['seconds'] = round(time.time() - t0, 1)
        r = result
        if r['status'] == 'KILLED':
            if r['how'] == 'fail':
                totals = r.get('first_totals') if r['stage'] == 'first' else r.get('suite_totals')
                rows = f'{totals[1]} of {totals[0]} rows' if totals else f'{r["failed"]} tests'
                line = f'{r["id"]}: KILLED by {r["stage"]} filter, {rows} failed ({r["failed"]} tests), first {r["first_failed"]}'
            else:
                line = f'{r["id"]}: KILLED by {r["stage"]} filter ({r["how"]}, rc={r.get("rc")}){", first " + r["first_failed"] if r.get("first_failed") else ""}'
        elif r['status'] in ('NOT VIABLE', 'NOT APPLIED', 'ERROR', 'BUILD ERROR'):
            line = f'{r["id"]}: {r["status"]} {r.get("detail", "")}'
        else:
            line = f'{r["id"]}: {r["status"]}' + (f' (first filter {r["first_totals"]}, suite {r.get("suite_totals")})')
        if r.get('unconfirmed'):
            line += f' [unconfirmed failures: {", ".join(r["unconfirmed"])}]'
        if r.get('built_though_refused_in_memory'):
            line += ' [built, though the in-memory check refused it]'
        if self.ctx['stop_on_fail'] and r.get('stage') == 'first':
            line += ' [stopped at the first failure: the count is a floor]'
        line += f' ({r["seconds"]:.0f}s)'
        ctx = self.ctx
        with ctx['lock']:
            ctx['summary'].write(line + '\n')
            ctx['summary'].flush()
            ctx['results'].write(json.dumps(result) + '\n')
            ctx['results'].flush()
            print(line, flush=True)

    def remove(self):
        remote = os.path.join(self.tree, 'tests', 'corpus', 'remote')
        if os.path.isdir(remote):
            shutil.rmtree(remote)
        r = subprocess.run(['git', 'worktree', 'remove', '--force', self.tree], cwd=self.ctx['repo'], capture_output=True, text=True)
        if r.returncode != 0:
            subprocess.run(['rm', '-rf', self.tree])
            subprocess.run(['git', 'worktree', 'prune'], cwd=self.ctx['repo'])


def main():
    positional = [a for a in sys.argv[1:] if not a.startswith('--')]
    for opt in ('--ids', '--repo', '--corpus'):
        if opt in sys.argv:
            positional.remove(arg(opt))
    shard_path, commit, workers = positional[0], positional[1], int(positional[2])
    shard = json.load(open(shard_path, encoding='utf-8'))
    name = str(shard['shard'])
    full = subprocess.run(['git', 'rev-parse', commit], capture_output=True, text=True,
                          cwd=arg('--repo') or os.getcwd()).stdout.strip()
    if not full:
        sys.exit(f'commit {commit} not found')
    if not full.startswith(shard['head']) and not shard['head'].startswith(full) and '--any-commit' not in sys.argv:
        sys.exit(f'commit {full} is not the shard head {shard["head"]}')
    repo = arg('--repo') or subprocess.run(['git', 'rev-parse', '--show-toplevel'], capture_output=True, text=True).stdout.strip()
    summary_path = os.path.join(HERE, f'summary-{name}.txt')
    done = set()
    if os.path.exists(summary_path) and '--rerun' not in sys.argv:
        for line in open(summary_path, encoding='utf-8'):
            if ': ' in line and ' harness error ' not in line and ': BUILD ERROR ' not in line and ': ERROR ' not in line:
                done.add(line.split(':', 1)[0])
    ids = arg('--ids')
    wanted = set(ids.split(',')) if ids else None
    jobs = queue.Queue()
    count = 0
    for m in shard['mutants']:
        if (wanted is None or m['id'] in wanted) and (m['id'] not in done or wanted is not None and '--rerun' in sys.argv):
            jobs.put(m)
            count += 1
    logs = os.path.join(HERE, 'logs', name)
    os.makedirs(logs, exist_ok=True)
    print(f'shard {name}: {count} mutants to run at {full[:7]} with {workers} workers ({len(done)} already in the summary)', flush=True)
    if count == 0:
        return
    ctx = {'shard': name, 'commit': full, 'repo': repo, 'corpus': arg('--corpus', DEFAULT_CORPUS), 'jobs': jobs,
           'lock': threading.Lock(), 'logs': logs, 'errors': [], 'first_only': '--first-only' in sys.argv,
           'no_confirm': '--no-confirm' in sys.argv, 'stop_on_fail': '--stop-on-fail' in sys.argv}
    t0 = time.time()
    with open(summary_path, 'a', encoding='utf-8') as summary, open(os.path.join(HERE, f'results-{name}.jsonl'), 'a', encoding='utf-8') as results:
        ctx['summary'] = summary
        ctx['results'] = results
        threads = [Worker(k, ctx) for k in range(1, min(workers, count) + 1)]
        try:
            for t in threads:
                t.start()
                time.sleep(2)
            for t in threads:
                while t.is_alive():
                    t.join(timeout=5)
        finally:
            if '--keep' not in sys.argv:
                for t in threads:
                    if not t.is_alive():
                        t.remove()
    print(f'shard {name}: done in {time.time() - t0:.0f}s; errors: {ctx["errors"] or "none"}', flush=True)


if __name__ == '__main__':
    main()
