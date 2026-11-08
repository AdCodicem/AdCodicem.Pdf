"""Own-stream differential driver, the steps of r8proto/run_variant.sh in Python (shell scripts cannot be run here).

usage: python diff_run.py WORKTREE NAME [--no-build] [--vs=r10a,r10b]
Copies the probe in as tests/AdCodicem.Pdf.Tests/ZzProtoDiffTests.cs, builds the solution in Release (unless --no-build),
runs the probe twice (the outputs must be byte-identical), classifies against the three baselines, round 8's and round 9's
check runs (out-r8f, out-r9f) and the runs --vs names, and deletes the probe again.
"""
import filecmp
import os
import shutil
import subprocess
import sys

R = 'C:/Users/clemm/AppData/Local/Temp/claude/7278f9d03177d71e/898783fc-049f-469a-ab79-dfd299ad558e/scratchpad/cloud/r8proto'
tree, name = os.path.abspath(sys.argv[1]), sys.argv[2]
build = '--no-build' not in sys.argv
if os.path.normcase(tree) == os.path.normcase('C:/Users/clemm/repos/AdCodicem.Pdf'):
    sys.exit('refusing to run in the main checkout')
probe = os.path.join(tree, 'tests', 'AdCodicem.Pdf.Tests', 'ZzProtoDiffTests.cs')
env = dict(os.environ, DOTNET_CLI_UI_LANGUAGE='en-US', VSLANG='1033', DOTNET_GCHeapHardLimit='0xC0000000')


def run(args, log, timeout, extra=None):
    with open(log, 'w', encoding='utf-8') as handle:
        process = subprocess.Popen(args, cwd=tree, env=dict(env, **(extra or {})), stdout=handle, stderr=subprocess.STDOUT)
        print(f'pid={process.pid} {" ".join(args[:3])}', flush=True)
        try:
            return process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            subprocess.run(['taskkill', '/PID', str(process.pid), '/T', '/F'])
            sys.exit(f'timeout: {log}')


shutil.copyfile(f'{R}/ZzProtoDiffTests.cs.txt', probe)
try:
    if build:
        log = f'{R}/build-{name}.log'
        if run(['dotnet', 'build', 'AdCodicem.Pdf.slnx', '-c', 'Release', '-m:2'], log, 1500) != 0:
            sys.exit(open(log, encoding='utf-8').read()[-3000:])
        print(''.join(l for l in open(log, encoding='utf-8') if 'Warning(s)' in l or 'Error(s)' in l), end='')
    for n in (1, 2):
        log = f'{R}/test-{name}.run{n}.log'
        code = run(['dotnet', 'test', '--project', 'tests/AdCodicem.Pdf.Tests/AdCodicem.Pdf.Tests.csproj', '-c', 'Release',
                    '--no-build', '--', '--filter-method', '*ZzProtoDiff*'], log, 3600,
                   {'ZZ_PROTO_MANIFEST': f'{R}/manifest.tsv', 'ZZ_PROTO_OUT': f'{R}/out-{name}.run{n}.txt'})
        if code != 0:
            sys.exit(open(log, encoding='utf-8').read()[-3000:])
finally:
    os.remove(probe)
if not filecmp.cmp(f'{R}/out-{name}.run1.txt', f'{R}/out-{name}.run2.txt', shallow=False):
    sys.exit(f'NOT DETERMINISTIC: out-{name}.run1.txt differs from run2')
os.replace(f'{R}/out-{name}.run1.txt', f'{R}/out-{name}.txt')
os.remove(f'{R}/out-{name}.run2.txt')
blocks = sum(1 for line in open(f'{R}/out-{name}.txt', encoding='utf-8') if line.startswith('=== '))
print(f'blocks: {blocks} (deterministic over two runs)')
extra = [a[5:] for a in sys.argv if a.startswith('--vs=')]
bases = ['main', 'd3ef', 'head', 'r8f', 'r9f'] + [b for a in extra for b in a.split(',') if b]
for base in bases:
    with open(f'{R}/cls-{name}-vs-{base}.txt', 'w', encoding='utf-8') as handle:
        subprocess.run([sys.executable, '-I', f'{R}/classify.py', f'{R}/out-{name}.txt', f'{R}/out-{base}.txt', '--manifest',
                        f'{R}/manifest.tsv', '--tsv', f'{R}/cls-{name}-vs-{base}.tsv'], stdout=handle, check=True)
    lines = open(f'{R}/cls-{name}-vs-{base}.txt', encoding='utf-8').read().splitlines()
    print(f'== vs {base}')
    print('\n'.join(lines[1:3]))
