"""Applies or reverts one ADR 53 mutant of the batch-wide step in a worktree.
usage: python -I mutant.py WORKTREE ID            apply mutant ID (its one replacement of OLD by NEW in FILE)
       python -I mutant.py WORKTREE ID --revert   restore FILE from git (git checkout -- FILE)
       python -I mutant.py - ID                   print the mutant (file, old, new, member, site, filter)
       python -I mutant.py --confirm OUT.json SHARD_NAME HEAD ID[,ID...] [--extra Class1,Class2]
                                                  write a shard file for W/adr53/run.py with these mutants, their first
                                                  filter widened by the extra classes (the classes the new tests are in)
The mutants are read from W/adr53/shards/shard-*.json."""
import glob, json, os, subprocess, sys
HERE = os.path.dirname(os.path.abspath(__file__))
SHARDS = os.path.join(HERE, '..', 'adr53', 'shards')
mutants = {}
for f in sorted(glob.glob(os.path.join(SHARDS, 'shard-*.json'))):
    for m in json.load(open(f, encoding='utf-8'))['mutants']:
        mutants[m['id']] = m
if sys.argv[1] == '--confirm':
    out, name, head, ids = sys.argv[2], sys.argv[3], sys.argv[4], sys.argv[5].split(',')
    extra = sys.argv[sys.argv.index('--extra') + 1].split(',') if '--extra' in sys.argv else []
    chosen = []
    for i in ids:
        m = dict(mutants[i])
        m['filter'] = list(dict.fromkeys((m.get('filter') or []) + extra))
        chosen.append(m)
    json.dump({'shard': name, 'head': head, 'mutants': chosen}, open(out, 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
    print(f'{out}: {len(chosen)} mutants at {head}')
    sys.exit()
tree, mid = sys.argv[1], sys.argv[2]
m = mutants[mid]
if tree == '-':
    print(json.dumps(m, indent=1, ensure_ascii=False)); sys.exit()
path = os.path.join(tree, m['file'])
if '--revert' in sys.argv:
    subprocess.run(['git', 'checkout', '--', m['file']], cwd=tree, check=True); print(f'{mid}: {m["file"]} restored'); sys.exit()
raw = open(path, 'rb').read().decode('utf-8')
crlf = '\r\n' in raw
lf = raw.replace('\r\n', '\n')
if lf.count(m['old']) != 1:
    sys.exit(f'{mid}: OLD occurs {lf.count(m["old"])} times in {m["file"]}: not applied')
text = lf.replace(m['old'], m['new'])
open(path, 'wb').write((text.replace('\n', '\r\n') if crlf else text).encode('utf-8'))
print(f'{mid}: applied to {m["file"]} ({m.get("member")}, {m.get("site")})')
