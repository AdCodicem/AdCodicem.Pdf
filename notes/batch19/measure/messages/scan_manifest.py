import json
import re
import sys

manifest = json.load(open(sys.argv[1]))
docs = manifest["documents"]
keys = set()
for d in docs:
    keys |= set(d.keys())
print(sorted(keys))
pat = re.compile(sys.argv[2])


def walk(o, path):
    if isinstance(o, dict):
        for k, v in o.items():
            yield from walk(v, path + [k])
    elif isinstance(o, list):
        for i, v in enumerate(o):
            yield from walk(v, path + [i])
    elif isinstance(o, str):
        yield path, o


n = 0
for d in docs:
    for p, s in walk(d, []):
        if pat.search(s):
            n += 1
            print(d.get("path") or d.get("file") or d.get("id"), p, s[:300])
print(n)
