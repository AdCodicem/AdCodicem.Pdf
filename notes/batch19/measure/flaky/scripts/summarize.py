"""Summarizes ZzFlaky317 summary files: per tag and row, the distribution of each ratio.

usage: python3 -I summarize.py file.txt [file.txt...]
"""
import statistics
import sys
from collections import defaultdict


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


def dist(values):
    values = sorted(values)
    n = len(values)
    if n == 0:
        return "n=0"
    med = statistics.median(values)
    p90 = values[min(n - 1, int(round(0.9 * (n - 1))))]
    return f"n={n} min={values[0]:.2f} med={med:.2f} p90={p90:.2f} max={values[-1]:.2f}"


def main():
    groups = defaultdict(list)
    for path in sys.argv[1:]:
        for rec in parse(path):
            groups[(rec["tag"], rec["row"])].append(rec)
    for (tag, row), recs in sorted(groups.items()):
        print(f"== {tag} / {row} ({recs[0]['runtime']}, room={recs[0].get('room')}, noGc={recs[0].get('noGc')})")
        r5 = [float(r["r5"]) for r in recs]
        rb = [float(r["rBoE"]) for r in recs]
        print(f"  r5 (test as is, 5 turns):        {dist(r5)}  >8: {sum(x >= 8 for x in r5)}  >12: {sum(x >= 12 for x in r5)}")
        print(f"  rBoE (BestOfEach, collect):      {dist(rb)}  >8: {sum(x >= 8 for x in rb)}  >12: {sum(x >= 12 for x in rb)}")
        if "rNoPause5" in recs[0]:
            rn = [float(r["rNoPause5"]) for r in recs]
            rnb = [float(r["rNoPauseB"]) for r in recs]
            print(f"  r5 minus GC pauses:              {dist(rn)}  >8: {sum(x >= 8 for x in rn)}")
            print(f"  rBoE minus GC pauses:            {dist(rnb)}  >8: {sum(x >= 8 for x in rnb)}")
        ra = [float(r["rAlloc"]) for r in recs]
        print(f"  allocation ratio (first run):    {dist(ra)}  allocS {recs[0]['allocS']} allocL {recs[0]['allocL']}")
        s5 = [float(r["small5"]) for r in recs]
        l5 = [float(r["large5"]) for r in recs]
        print(f"  small best5 ms: {dist(s5)}")
        print(f"  large best5 ms: {dist(l5)}")
        sp5 = [float(r["spent5"]) for r in recs]
        spb = [float(r["spentB"]) for r in recs]
        tu = [float(r["turns"]) for r in recs]
        print(f"  time of 5 turns ms: {dist(sp5)}")
        print(f"  time of BestOfEach ms: {dist(spb)}  turns: {dist(tu)}")
        gcl = defaultdict(int)
        gcs = defaultdict(int)
        for r in recs:
            for g in r["gcL"].strip(",").split(","):
                gcl[g] += 1
            for g in r["gcS"].strip(",").split(","):
                gcs[g] += 1
        print(f"  collections in a large probe (gen0+/gen1+/gen2 counts -> runs): {dict(sorted(gcl.items()))}")
        print(f"  collections in a small probe: {dict(sorted(gcs.items()))}")


main()
