"""Lists the added lines of a unified diff that carry a review or measurement key."""
import re
import sys

patterns = [
    ('bracketed number', re.compile(r'(?<![\w\]\)])\[(\d{1,2})\](?!\s*[:(=])')),
    ('R1-Q', re.compile(r'\bR\d-Q\d*')),
    ('R2-', re.compile(r'\bR\d-\w')),
    ('B19-', re.compile(r'\bB\d\d-')),
    ('round tag', re.compile(r'\bround [123]\b', re.I)),
    ('shape name', re.compile(r'\b(S[1-9]|P1b|p1b|P2|P261-B|A[1-9]|A-prime|q[1-9]|T[1-3]|N-DUP|N-TND|R[1-3][a-g])\b')),
    ('finding key', re.compile(r'\b[a-z]+(?:-[a-z]+){3,}\b')),
    ('mutant id', re.compile(r'\b[Mm]ut(?:ant)?[-_ ]?\d+\b|\bM\d{2,3}[a-z]\b')),
    ('decision', re.compile(r'\bQ(?:9|10|11|12)\b')),
    ('Zz probe', re.compile(r'\bZz\w+')),
]
current = None
for raw in open(sys.argv[1], encoding='utf-8'):
    line = raw.rstrip('\n')
    if line.startswith('+++ '):
        current = line[6:]
        continue
    if not line.startswith('+') or line.startswith('+++'):
        continue
    text = line[1:]
    for name, rx in patterns:
        for m in rx.finditer(text):
            print(f'{name}\t{current}\t{m.group(0)}\t{text.strip()[:160]}')
