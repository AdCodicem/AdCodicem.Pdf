"""usage: python -I summarize.py JOURNAL.jsonl BATCH: prints a settle batch's outcome and appends each id's final fate
to W/settle/fates.jsonl (settle fate, overridden by a refuter's OVERTURNED)."""
import json, os, sys
from collections import Counter
HERE = os.path.dirname(os.path.abspath(__file__))
journal, batch = sys.argv[1], sys.argv[2]
res = [json.loads(l) for l in open(journal, encoding='utf-8') if json.loads(l).get('type') == 'result']
json.dump(res, open(os.path.join(HERE, f'{batch}-result.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
fates, merge, found = {}, None, []
refutes = []
for o in res:
    v = o['result']
    if 'survivors' in v:
        for s in v['survivors']:
            fates[s['id']] = {'id': s['id'], 'batch': batch, 'group': v['label'], 'fate': s['fate'], 'reason': s['reason'], 'test': s.get('test', '')}
        found += [f"{v['label']}: {f}" for f in v.get('found', [])]
    elif 'verdicts' in v:
        refutes.append(v)
    elif 'head_after' in v:
        merge = v
for v in refutes:
    for x in v['verdicts']:
        f = fates.setdefault(x['id'], {'id': x['id'], 'batch': batch, 'group': v['label'], 'fate': '?', 'reason': '', 'test': ''})
        f['refute'] = x['verdict']
        if x['verdict'] == 'OVERTURNED':
            f['fate'], f['reason'], f['test'] = 'KILLED-NEW', x['reason'], x.get('test', '')
        elif x['verdict'] == 'UPHELD' and x['reason']:
            f['reason_checked'] = x['reason']
c = Counter(f['fate'] for f in fates.values())
print(f'{batch}: {len(fates)} ids: {dict(c)}; refuter verdicts: {dict(Counter(f.get("refute", "-") for f in fates.values()))}')
for f in fates.values():
    if f.get('refute') == 'OVERTURNED':
        print(f"  OVERTURNED {f['id']}: {f['reason'][:200]}")
    if f['fate'] in ('DEBT', '?'):
        print(f"  {f['fate']} {f['id']}: {f['reason'][:300]}")
    if f.get('refute') == 'TEST-WEAK':
        print(f"  TEST-WEAK {f['id']}")
if merge:
    print(f"merge {merge['head_before'][:9]} -> {merge['head_after'][:9]}")
    for x in merge['commits']: print('  C', x[:200])
    bad = [x for x in merge['confirm'] if ': ' in x and 'KILLED' not in x and x[:3].isupper()]
    print(f"  confirm lines: {len(merge['confirm'])}, not KILLED: {bad[:5]}")
    for x in merge['problems']: print('  P', x[:220])
for x in found:
    if 'No ' not in x[:20]: print('  F', x[:240])
with open(os.path.join(HERE, 'fates.jsonl'), 'a', encoding='utf-8') as out:
    for f in fates.values():
        out.write(json.dumps(f, ensure_ascii=False) + '\n')
