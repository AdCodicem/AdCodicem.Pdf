"""Markdown tables of ZzFlaky317 summaries, one row per (tag, row of the theory).

usage: python3 -I table.py file.txt [file.txt...]
"""
import statistics
import sys
from collections import OrderedDict


def parse(path):
    for line in open(path):
        parts = line.rstrip("\n").split("\t")
        if len(parts) < 4:
            continue
        rec = {"tag": parts[0], "row": parts[1], "runtime": parts[2]}
        for p in parts[3:]:
            if "=" in p:
                k, v = p.split("=", 1)
                rec[k] = v
        yield rec


def span(values):
    values = sorted(values)
    return f"{values[0]:.2f} / {statistics.median(values):.2f} / {values[-1]:.2f}"


groups = OrderedDict()
for path in sys.argv[1:]:
    for rec in parse(path):
        groups.setdefault((rec["tag"], rec["row"]), []).append(rec)

print("| Condition | Row | Runs | 5 turns: min / median / max | >= 8 | >= 12 | BestOfEach: min / median / max | >= 8 | "
      "small / large best, median ms | 5 turns, median ms | BestOfEach, median ms (turns) | collections, large probe |")
print("|---|---|---|---|---|---|---|---|---|---|---|---|")
for (tag, row), recs in groups.items():
    r5 = [float(r["r5"]) for r in recs]
    rb = [float(r["rBoE"]) for r in recs]
    s5 = statistics.median(float(r["small5"]) for r in recs)
    l5 = statistics.median(float(r["large5"]) for r in recs)
    sp5 = statistics.median(float(r["spent5"]) for r in recs)
    spb = statistics.median(float(r["spentB"]) for r in recs)
    tu = statistics.median(float(r["turns"]) for r in recs)
    gcs = {}
    for r in recs:
        for g in r["gcL"].strip(",").split(","):
            gcs[g] = gcs.get(g, 0) + 1
    common = ", ".join(f"{k[0]}/{k[1]}/{k[2]}" for k, _ in sorted(gcs.items(), key=lambda kv: -kv[1])[:2])
    print(f"| {tag} | {row} | {len(recs)} | {span(r5)} | {sum(x >= 8 for x in r5)} | {sum(x >= 12 for x in r5)} | "
          f"{span(rb)} | {sum(x >= 8 for x in rb)} | {s5:.1f} / {l5:.1f} | {sp5:.0f} | {spb:.0f} ({tu:.0f}) | {common} |")
