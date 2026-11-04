"""Ratios a real test's failure messages print, per row, from test-<tag>.txt files.

usage: python3 -I ratios.py test-a.txt [...]
"""
import re
import statistics
import sys

row_re = re.compile(r'\(shape: "([^"]+)"\)')
times_re = re.compile(r"\((\d\d):(\d\d):(\d\d\.\d+) for [\d,]+, (\d\d):(\d\d):(\d\d\.\d+) for [\d,]+\)")


def seconds(h, m, s):
    return int(h) * 3600 + int(m) * 60 + float(s)


for path in sys.argv[1:]:
    rows = {}
    row = None
    for line in open(path):
        m = row_re.search(line)
        if m and line.startswith("failed "):
            row = m.group(1)
            continue
        t = times_re.search(line)
        if t and row is not None:
            small = seconds(*t.group(1, 2, 3))
            large = seconds(*t.group(4, 5, 6))
            rows.setdefault(row, []).append((large / small, small * 1000, large * 1000))
            row = None
    for name, values in rows.items():
        ratios = sorted(v[0] for v in values)
        print(f"{path} | {name} | n={len(ratios)} | ratios " + " ".join(f"{r:.2f}" for r in ratios)
              + f" | median {statistics.median(ratios):.2f} | small ms median {statistics.median(v[1] for v in values):.1f}"
              + f" | large ms median {statistics.median(v[2] for v in values):.1f}")
