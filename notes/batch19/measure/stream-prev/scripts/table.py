import sys, re, collections


def parse(p):
    shapes = collections.OrderedDict()
    cur = None
    for line in open(p, encoding='utf-8'):
        line = line.rstrip('\n')
        if line.startswith('### '):
            cur = line[4:].split(' (')[0]
            shapes[cur] = {'S': [], 'D': [], 'O': [], 'F': [], 'D2': []}
        elif cur is None:
            continue
        elif line.startswith('  section '):
            m = dict(re.findall(r'(\w+)=(\S+)', line))
            shapes[cur]['S'].append(f"{m['namedBy']}@{m['offset']}:{m['state']}" + ('/cut' if m['cut'] == 'True' else ''))
        elif line.startswith('  D '):
            shapes[cur]['D'].append(line.split()[2].rstrip(':'))
        elif line.startswith("  D' "):
            shapes[cur]['D2'].append(line.split()[2])
        elif line.startswith('  GetObject('):
            m = re.match(r'  GetObject\((\d+)\): (.*); WasRepaired: (\w+)', line)
            v = m.group(2).strip('"')
            short = {'only in the oldest': 'ok', 'only in the middle': 'ok', 'only in the newest': 'ok',
                     'new seven': 'NEW', 'old seven': 'OLD', 'null': 'null'}.get(v, v)
            shapes[cur]['O'].append(f"{m.group(1)}={short}{'(R)' if m.group(3) == 'True' else ''}")
        elif line.startswith('  F '):
            parts = line.split()
            shapes[cur]['F'].append(parts[2])
        elif line.startswith('  THREW'):
            shapes[cur]['O'].append(line.strip())
    return shapes


def main():
    for p in sys.argv[1:]:
        print('##', p.split('/')[-1])
        for k, v in parse(p).items():
            print(f"{k}\n   sections: {' | '.join(v['S'])}\n   reader: {', '.join(v['D']) or '-'}\n"
                  f"   objects: {' '.join(v['O'])}\n   validator: {', '.join(v['F']) or '-'}")


main()
