"""Markdown comparison of shapes across runs: python3 -I compare.py label=file ..."""
import sys
sys.path.insert(0, '/tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/measure/stream-prev/scripts')
import importlib.util

spec = importlib.util.spec_from_file_location('table', sys.path[0] + '/table.py')
source = open(sys.path[0] + '/table.py').read().replace('\nmain()\n', '\n')
namespace = {}
exec(compile(source, 'table.py', 'exec'), namespace)
parse = namespace['parse']

runs = [(arg.split('=', 1)[0], parse(arg.split('=', 1)[1])) for arg in sys.argv[1:]]
names = list(runs[0][1].keys())


def cell(v):
    if v is None:
        return 'n/a'
    states = ' > '.join(s.split(':')[1] for s in v['S'])
    reader = ', '.join(v['D']) or '-'
    validator = ', '.join(v['F']) or '-'
    return f"{states}<br>R: {reader}<br>O: {' '.join(v['O'])}<br>V: {validator}"


print('| Shape | ' + ' | '.join(label for label, _ in runs) + ' |')
print('|---|' + '---|' * len(runs))
for name in names:
    print(f'| {name} | ' + ' | '.join(cell(r.get(name)) for _, r in runs) + ' |')
