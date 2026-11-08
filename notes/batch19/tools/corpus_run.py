"""Corpus dump driver, the steps of lot19/tools/corpus-run.sh in Python (shell scripts cannot be run from this session).

usage: python corpus_run.py OUTDIR MAINTREE BRANCHTREE [RUNS]
Runs the dump on the main tree and the branch tree alternately, RUNS each (default 2), one dotnet process at a time, each
under a 30-minute timeout; writes OUTDIR/corpus-{main,branch}-<run>.txt, their logs and corpus-progress.txt. Both trees
must hold tests/AdCodicem.Pdf.Tests/ZzCorpusDump.cs, a hard-linked copy of the remote corpus and a Release build.
"""
import datetime
import os
import subprocess
import sys

out, main, branch = sys.argv[1], sys.argv[2], sys.argv[3]
runs = int(sys.argv[4]) if len(sys.argv) > 4 else 2
os.makedirs(out, exist_ok=True)
progress = os.path.join(out, 'corpus-progress.txt')
open(progress, 'w').close()
env = dict(os.environ, DOTNET_CLI_UI_LANGUAGE='en-US', VSLANG='1033', DOTNET_GCHeapHardLimit='0xC0000000')
for run in range(1, runs + 1):
    for name, tree in (('main', main), ('branch', branch)):
        if not tree:
            continue
        env['ZZ_CORPUS_OUT'] = os.path.join(out, f'corpus-{name}-{run}.txt').replace('\\', '/')
        log = os.path.join(out, f'corpus-{name}-{run}.log')
        with open(log, 'w', encoding='utf-8') as handle:
            process = subprocess.Popen(
                ['dotnet', 'test', '--project', 'tests/AdCodicem.Pdf.Tests/AdCodicem.Pdf.Tests.csproj', '-c', 'Release',
                 '--no-build', '--', '--filter-method', '*ZzCorpusDumpAll'],
                cwd=tree, env=env, stdout=handle, stderr=subprocess.STDOUT)
            try:
                code = process.wait(timeout=1800)
            except subprocess.TimeoutExpired:
                subprocess.run(['taskkill', '/PID', str(process.pid), '/T', '/F'])
                code = 'TIMEOUT'
        with open(progress, 'a') as handle:
            handle.write(f'{datetime.datetime.now():%H:%M:%S} {name} run {run} pid={process.pid} exit={code}\n')
with open(progress, 'a') as handle:
    handle.write('done\n')
