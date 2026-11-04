"""Pools every correct-code run (no mutant) of the probe, by room and row: the ratio each prototype's measure gives.

usage: python3 -I overall.py data_dir
"""
import glob
import os
import statistics
import sys

pools = {}
for path in glob.glob(os.path.join(sys.argv[1], "*.txt")):
    name = os.path.basename(path)
    if name.startswith(("M1", "M2", "M3", "test-", "try", "kept", "builds")) or name.endswith((".load.txt", ".raw.txt")) or "+M3" in name:
        continue
    for line in open(path):
        p = line.rstrip("\n").split("\t")
        if len(p) < 4 or "r5=" not in line:
            continue
        rec = dict(x.split("=", 1) for x in p[3:] if "=" in x)
        if rec.get("noGc", "0") != "0":
            continue
        room = "1000" if rec["room"] == "1000" else "max"
        key = (room, p[1])
        pools.setdefault(key, {"r5": [], "rb": [], "tags": set()})
        pools[key]["r5"].append(float(rec["r5"]))
        pools[key]["rb"].append(float(rec["rBoE"]))
        pools[key]["tags"].add(p[0])

for (room, row), v in sorted(pools.items()):
    r5 = sorted(v["r5"])
    rb = sorted(v["rb"])
    print(f"room={room} row={row} runs={len(r5)} conditions={len(v['tags'])} "
          f"r5 min={r5[0]:.2f} med={statistics.median(r5):.2f} max={r5[-1]:.2f} >=8:{sum(x >= 8 for x in r5)} >=12:{sum(x >= 12 for x in r5)} | "
          f"rBoE min={rb[0]:.2f} med={statistics.median(rb):.2f} max={rb[-1]:.2f} >=8:{sum(x >= 8 for x in rb)} >=12:{sum(x >= 12 for x in rb)}")
