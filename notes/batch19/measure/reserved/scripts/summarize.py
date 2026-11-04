"""Summarizes a shapes probe output: per shape, object 3, its index entry, the count and the codes of diagnostics and findings."""
import sys, re, collections

def parse(path):
    shapes = collections.OrderedDict()
    cur = None
    for line in open(path, encoding='utf-8'):
        line = line.rstrip('\n')
        m = re.match(r'== (\S+)', line)
        if m:
            cur = shapes.setdefault(m.group(1), {'obj': '', 'entry': '', 'count': '', 'diag': [], 'find': set(), 'rounds': []})
            continue
        if cur is None:
            continue
        m = re.match(r'  GetObject\(3\): (.*?); presence=(\w+); WasRepaired=(\w+)', line)
        if m:
            cur['obj'] = m.group(1)[:40]
            cur['presence'] = m.group(2)
            cur['repaired'] = m.group(3)
        m = re.match(r'  index: count=(\d+) .* entry\(3\)=(.*)', line)
        if m:
            cur['count'] = m.group(1)
            k = re.search(r'Kind = (\w+)', m.group(2))
            cur['entry'] = k.group(1) if k else m.group(2)
        m = re.match(r'  at open: (\d+)', line)
        if m:
            cur['open'] = m.group(1)
        m = re.match(r'    D (\w+) (\S+)', line)
        if m and 'after' not in cur:
            cur['diag'].append(m.group(1) + ' ' + m.group(2))
        if line.startswith('  after:'):
            cur['after'] = True
        m = re.match(r'    F (\w+) (\S+)', line)
        if m:
            cur['find'].add(m.group(1) + ' ' + m.group(2))
        m = re.match(r'  validator round (\d): (\d+)', line)
        if m:
            cur['rounds'].append(m.group(2))
    return shapes

shapes = parse(sys.argv[1])
print('| Shape | Object 3 | Entry for 3 | ObjectCount | Reader at open | Validator (round 0 = round 1) |')
print('|---|---|---|---|---|---|')
for name, s in shapes.items():
    rounds = '=' if len(set(s['rounds'])) == 1 else 'DIFFER ' + '/'.join(s['rounds'])
    print(f"| {name} | {s['obj']} | {s['entry']} | {s['count']} | {', '.join(s['diag']) or 'none'} | {', '.join(sorted(s['find'])) or 'none'} {rounds} |")
