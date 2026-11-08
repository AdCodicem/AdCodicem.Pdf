"""Checks that each Class.Method a Markdown file cites is a method of that test class."""
import os
import re
import sys

root, doc = sys.argv[1], sys.argv[2]
methods = {}
for base, _, files in os.walk(os.path.join(root, 'tests')):
    if '/bin' in base or '/obj' in base:
        continue
    for name in files:
        if not name.endswith('.cs'):
            continue
        text = open(os.path.join(base, name), encoding='utf-8').read()
        for cls in re.findall(r'\bclass\s+(\w+)', text):
            methods.setdefault(cls, set())
        for m in re.finditer(r'\b(?:public|internal|private)\s+(?:static\s+)?(?:async\s+)?(?:void|Task|ValueTask)\s+(\w+)\s*\(', text):
            for cls in re.findall(r'\bclass\s+(\w+)', text):
                methods[cls].add(m.group(1))
text = open(os.path.join(root, doc), encoding='utf-8').read()
cited = set(re.findall(r'\b([A-Z]\w*Tests)\.(\w+)', text))
missing = sorted((c, m) for c, m in cited if m not in methods.get(c, set()))
print(doc, 'cited', len(cited), 'missing', len(missing))
for c, m in missing:
    print('  MISSING', c + '.' + m)
