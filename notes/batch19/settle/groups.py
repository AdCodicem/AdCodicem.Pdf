"""Groups the ADR 53 survivors by member. usage: python -I groups.py ADR53_DIR OUT_JSON [--done-ids FILE]
A member is ready when every mutant of it has a final line (not BUILD ERROR, not harness error, not missing).
Writes {"ready": [{"member", "file", "survivors": [mutant + summary line]}], "pending_members": n, "counts": {...}}."""
import json, os, re, sys, glob
adr, out = sys.argv[1], sys.argv[2]
done = set(open(sys.argv[sys.argv.index('--done-ids') + 1]).read().split()) if '--done-ids' in sys.argv else set()
mutants = {}
for f in sorted(glob.glob(os.path.join(adr, 'shards', 'shard-*.json'))):
    for m in json.load(open(f, encoding='utf-8'))['mutants']:
        mutants[m['id']] = m
last = {}
for f in sorted(glob.glob(os.path.join(adr, 'summary-*.txt'))):
    for line in open(f, encoding='utf-8'):
        if ': ' in line:
            mid, rest = line.rstrip('\n').split(': ', 1)
            last[mid] = rest
def final(rest):
    return rest is not None and not rest.startswith('BUILD ERROR') and not rest.startswith('harness error') and not rest.startswith('ERROR')
by_member = {}
for mid, m in mutants.items():
    by_member.setdefault((m['file'], m.get('member') or '?'), []).append(mid)
ready, pending = [], 0
counts = {}
for rest in last.values():
    k = re.match(r'[A-Z -]+[A-Z]', rest).group(0) if re.match(r'[A-Z -]+[A-Z]', rest) else rest[:20]
    counts[k] = counts.get(k, 0) + 1
for (file, member), ids in sorted(by_member.items()):
    if not all(final(last.get(i)) for i in ids):
        pending += 1
        continue
    surv = [dict(mutants[i], summary=last[i]) for i in ids if last[i].startswith('SURVIVED') and i not in done]
    if surv:
        ready.append({'member': member, 'file': file, 'survivors': surv})
json.dump({'ready': ready, 'pending_members': pending, 'counts': counts, 'mutants': len(mutants), 'with_result': len(last)},
          open(out, 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
print(f"{len(ready)} ready members, {sum(len(g['survivors']) for g in ready)} survivors; {pending} members pending; {counts}")
for g in ready:
    print(f"  {g['member']} ({g['file'].rsplit('/',1)[-1]}): {len(g['survivors'])}")
