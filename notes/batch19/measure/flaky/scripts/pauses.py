"""Mean time and GC pause of each probe, per (tag, row), from ZzFlaky317 raw files.

usage: python3 -I pauses.py file.raw.txt [...]
raw columns: tag row turn small_ms large_ms gcS gcL allocS allocL pauseS_ms pauseL_ms
"""
import statistics
import sys
from collections import OrderedDict

groups = OrderedDict()
for path in sys.argv[1:]:
    for line in open(path):
        p = line.rstrip("\n").split("\t")
        if len(p) < 11:
            continue
        groups.setdefault((p[0], p[1]), []).append([float(x) for x in (p[3], p[4], p[9], p[10])])

print("| Condition | Row | Probes | small: median ms (of which GC pause) | large: median ms (of which GC pause) | large minus pause / small minus pause |")
print("|---|---|---|---|---|---|")
for (tag, row), runs in groups.items():
    s = statistics.median(r[0] for r in runs)
    l = statistics.median(r[1] for r in runs)
    ps = statistics.median(r[2] for r in runs)
    pl = statistics.median(r[3] for r in runs)
    print(f"| {tag} | {row} | {len(runs)} | {s:.1f} ({ps:.1f}) | {l:.1f} ({pl:.1f}) | {(l - pl) / (s - ps):.2f} |")
