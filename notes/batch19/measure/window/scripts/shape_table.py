import re, sys
# One row per shape: what each probe output says (object read, repaired, the xref finding's rule), side by side.
def parse(path):
    shapes = {}
    current = None
    for line in open(path, encoding='utf-8'):
        line = line.rstrip('\n')
        if line.startswith('### '):
            current = line[4:]
            shapes[current] = {'object': '', 'repaired': '', 'findings': [], 'adjusted': ''}
        elif current is None:
            continue
        elif line.startswith('  object '):
            m = re.match(r'  object \d+: (.*); WasRepaired: (\w+)', line)
            shapes[current]['object'] = m.group(1)
            shapes[current]['repaired'] = m.group(2)
        elif line.startswith('  finding: '):
            m = re.match(r'  finding: (\w+) ([\w.-]+)', line)
            shapes[current]['findings'].append(m.group(1)[0] + ' ' + m.group(2))
        elif 'xref.offset-adjusted' in line and 'found' in line:
            m = re.search(r'found (-?\d+) bytes', line)
            if m:
                shapes[current]['adjusted'] = m.group(1)
    return shapes

files = sys.argv[1:]
tables = [parse(path) for path in files]
print('| Shape | ' + ' | '.join(path.split('/')[-1] for path in files) + ' |')
print('|---|' + '---|' * len(files))
for shape in tables[0]:
    cells = []
    for table in tables:
        s = table.get(shape)
        if s is None:
            cells.append('-')
            continue
        text = s['object']
        text += ', rebuilt' if s['repaired'] == 'True' else ''
        if s['adjusted']:
            text += ', moved ' + s['adjusted']
        xref = [f for f in s['findings'] if 'xref.' in f or 'file.' in f]
        if xref:
            text += '; ' + ', '.join(xref)
        cells.append(text)
    print('| ' + shape + ' | ' + ' | '.join(cells) + ' |')
