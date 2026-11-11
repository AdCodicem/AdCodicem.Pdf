"""usage: python -I chunk.py BATCH_JSON OUT_ARGS_JSON BATCH_NAME [MAX]: chunks ready members (same file, in order) into
groups of at most MAX survivors (a member is never split) and writes the workflow's args."""
import json, sys
d = json.load(open(sys.argv[1], encoding='utf-8'))
name, cap = sys.argv[3], int(sys.argv[4]) if len(sys.argv) > 4 else 12
groups, cur = [], None
for g in d['ready']:
    n = len(g['survivors'])
    if cur and cur['file'] == g['file'] and cur['n'] + n <= cap:
        cur['members'].append(g['member']); cur['survivors'] += g['survivors']; cur['n'] += n
    else:
        cur = {'file': g['file'], 'members': [g['member']], 'survivors': list(g['survivors']), 'n': n}
        groups.append(cur)
for i, g in enumerate(groups, 1):
    g['label'] = f"{name}-{i:02d}"
    for s in g['survivors']:
        s.pop('filter', None)
json.dump({'batch': name, 'groups': groups}, open(sys.argv[2], 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
for g in groups:
    print(g['label'], g['n'], g['file'].rsplit('/', 1)[-1], ', '.join(g['members']))
