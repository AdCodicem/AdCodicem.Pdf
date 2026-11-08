"""Greps a diff's added lines for the names the task forbids (review keys, probe names, mutant ids, words)."""
import re
import sys

pats = [
    ('decision key', re.compile(r'\bR\d+-Q\d|\bB19-|\bJ[12]\b|\bP1[ab]\b|\bfix \((?:i|ii)\)')),
    ('probe name', re.compile(r'lostprev|guard-own|\bh2026\d+|\bg00\d{4}\b|older_t0|older_none|b_header|ref_comment|bound_prev|bound_other|'
                              r'\bkfirst\b|\bkagain|\bx_again|q_xref_again|q_plain_again|\bp_first|\bcutown|outofwindow|'
                              r'\bshape \d|\bpod\b|\bpog\b', re.I)),
    ('FIVE', re.compile(r'\bFIVE\b')),
    ('mutant id', re.compile(r'(?<![:{"])\b[A-HK]\d{1,2}\b(?![}"])|\bM\d{2}\b')),
    ('words', re.compile(r'decoy|reviewer|refuter|prototype|\bvariants?\b', re.I)),
]
cur = None
hits = 0
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
            hits += 1
            start = max(0, m.start() - 60)
            print(f'{name}\t{cur}\t{m.group(0)}\t{text[start:m.end() + 60]}')
print('hits', hits, file=sys.stderr)
