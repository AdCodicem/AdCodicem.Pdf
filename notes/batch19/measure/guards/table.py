import re
import sys


def parse(path):
    blocks = {}
    cur = None
    mode = None
    for line in open(path, encoding='utf-8'):
        line = line.rstrip('\n')
        if line.startswith('=== '):
            m = re.match(r'=== (\w+) prev=(\S+) root=(none|1 0 R) guard=(\w+)', line)
            cur = (m.group(1), m.group(2), m.group(3), m.group(4))
            blocks[cur] = {'reader': [], 'validator': [], 'misc': []}
            continue
        s = line.strip()
        if s.startswith('reader at opening') or s.startswith('reader after'):
            mode = 'reader'
            continue
        if s.startswith('validator:'):
            mode = 'validator'
            continue
        if s.startswith('throw:'):
            mode = None
            continue
        if s.startswith('section ') or s.startswith('trailersHidden') or s.startswith('catalog='):
            blocks[cur]['misc'].append(s)
            continue
        if mode == 'reader':
            blocks[cur]['reader'].append(s.split()[1].rstrip(':'))
        elif mode == 'validator':
            blocks[cur]['validator'].append(s.split()[1].rstrip(':'))
    return blocks


def faults(v):
    reader = [c for c in v['reader'] if c in ('xref.section-missing', 'xref.chain-cycle')]
    validator = [c for c in v['validator'] if c in ('xref.section-not-found', 'xref.chain-loop', 'file.trailer-value-wrong')]
    th = 'hidden' if any('trailersHidden=True' in m for m in v['misc']) else ''
    cat = [m for m in v['misc'] if m.startswith('catalog=')][0]
    rebuilt = 'rebuilt' if 'xref.rebuilt' in v['reader'] else ''
    catalog = 'catalog' if 'catalog=found' in cat else 'no catalog'
    return ' '.join(reader + validator) or '(none)', ' '.join(x for x in (th, rebuilt, catalog) if x)


def main():
    main_b = parse(sys.argv[1])
    p1_b = parse(sys.argv[2])
    root = sys.argv[3]
    print('| section | /Prev | guard | main, guarded | P1, guarded | whole read |')
    print('|---|---|---|---|---|---|')
    for key, v in main_b.items():
        if key[3] == 'default' or key[2] != root:
            continue
        whole = main_b[(key[0], key[1], key[2], 'default')]
        mf, ms = faults(v)
        pf, ps = faults(p1_b[key])
        wf, ws = faults(whole)
        print(f'| {key[0]} | {key[1]} | {key[3]} | {mf}; {ms} | {pf}; {ps} | {wf}; {ws} |')


main()
