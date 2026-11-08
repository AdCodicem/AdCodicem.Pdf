"""Wider grep of a diff's added lines for review and measurement keys."""
import re
import sys

pats = [
    ('bracket', re.compile(r'(?:^|(?<=[\s,;.:]))\[(\d{1,2})\](?!\s*[=:])')),
    ('R-key', re.compile(r'\bR\d-[A-Za-z0-9]+')),
    ('B19', re.compile(r'\bB\d\d-')),
    ('probe file', re.compile(r'\b[a-z]\d{5}\b')),
    ('K mutant', re.compile(r'\bK\d+\b')),
    ('M mutant', re.compile(r'\bM\d{2}\b')),
    ('words', re.compile(r'lot1[89]|scratchpad|reviewer|refuter|\bround \d|\bZz\w*|decoy|N-DUP|N-TND|A-prime', re.I)),
    ('P key', re.compile(r'\bP\d{2,3}\b')),
    ('shape letter', re.compile(r'\((?:A|B2?|C|E\d|F\d|V\d+)\)')),
]
cur = None
for raw in open(sys.argv[1], encoding='utf-8'):
    line = raw.rstrip('\n')
    if line.startswith('+++ '):
        cur = line[6:]
        continue
    if not line.startswith('+') or line.startswith('+++'):
        continue
    text = line[1:]
    for name, rx in pats:
        for m in rx.finditer(text):
            print(f'{name}\t{cur}\t{m.group(0)}\t{text.strip()[:150]}')
