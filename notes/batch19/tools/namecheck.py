"""Checks that each test-like name (four underscores or more) a Markdown file cites is a method in tests/."""
import os
import re
import sys

root, doc = sys.argv[1], sys.argv[2]
names = set()
for base, _, files in os.walk(os.path.join(root, 'tests')):
    if '/bin' in base or '/obj' in base:
        continue
    for name in files:
        if name.endswith('.cs'):
            text = open(os.path.join(base, name), encoding='utf-8').read()
            names.update(re.findall(r'\b(?:void|Task|ValueTask)\s+(\w+)\s*\(', text))
text = open(os.path.join(root, doc), encoding='utf-8').read()
cited = set(n for n in re.findall(r'\b([A-Z][A-Za-z0-9]*(?:_[A-Za-z0-9]+){4,})\b', text))
missing = sorted(n for n in cited if n not in names)
print(doc, 'cited', len(cited), 'missing', len(missing))
for n in missing:
    print('  MISSING', n)
