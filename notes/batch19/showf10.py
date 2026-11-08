"""usage: python showf10.py FILE MAXCHARS N [N...] -- prints the findings '## N. key' of r10-findings.md."""
import re
import sys

text = open(sys.argv[1], encoding='utf-8').read()
cap = int(sys.argv[2])
parts = re.split(r'(?m)^(?=## \d+\. )', text)
for n in sys.argv[3:]:
    for part in parts:
        if part.startswith(f'## {n}. '):
            print(part[:cap])
            print('-' * 40)
