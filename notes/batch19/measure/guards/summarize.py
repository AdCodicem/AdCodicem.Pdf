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
            blocks[cur] = {'reader': [], 'validator': [], 'misc': [], 'throw': ''}
            continue
        s = line.strip()
        if s.startswith('reader at opening') or s.startswith('reader after'):
            mode = 'reader'
            continue
        if s.startswith('validator:'):
            mode = 'validator'
            continue
        if s.startswith('throw:'):
            blocks[cur]['throw'] = s
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


def main():
    b = parse(sys.argv[1])
    ign = {'object.redefined'}
    for key, v in b.items():
        if key[3] == 'default':
            continue
        whole = b[(key[0], key[1], key[2], 'default')]
        r = [c for c in v['reader'] if c not in ign]
        w = [c for c in whole['reader'] if c not in ign]
        cat = [m for m in v['misc'] if m.startswith('catalog=')][0]
        wcat = [m for m in whole['misc'] if m.startswith('catalog=')][0]
        th = [m for m in v['misc'] if m.startswith('trailersHidden')][0].split()[0]
        print(f"{key[0]:6} {key[1]:15} root={key[2]:5} {key[3]:7} | reader {','.join(r)} | validator {','.join(v['validator'])} | {th} | {v['throw']}")
        print(f"{'':6} {'':15} {'':10} {'whole':7} | reader {','.join(w)} | validator {','.join(whole['validator'])}")
        print(f"{'':6} {'':15} {'':10} {'':7}   guarded {cat} ; whole {wcat}")


main()
