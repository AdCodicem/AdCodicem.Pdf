"""Rewrites main's corpus dump with #241's wording rules alone, so cmp_corpus.py shows what differs beyond wording.

argv[1]: main's dump; argv[2]: output path. The rules are the messages lens's (wording_check.py), which stage 2 checked
against its own build: 0 mismatches over 403 documents.
"""
import collections
import importlib.util
import re
import sys


def bytes_(n):
    return f"{n} byte" if n == 1 else f"{n} bytes"


RULES = [
    ("header offset", re.compile(r"starts 1 bytes into the file"), lambda m: "starts 1 byte into the file"),
    ("object relocation", re.compile(r"was found (-?\d+) bytes from where the index said\."),
     lambda m: f"was found {bytes_(abs(int(m.group(1))))} {'before' if int(m.group(1)) < 0 else 'after'} where the index said."),
    ("section relocation", re.compile(r"was found (-?\d+) bytes from there\."),
     lambda m: f"was found {bytes_(abs(int(m.group(1))))} {'before' if int(m.group(1)) < 0 else 'after'} it."),
    ("of the 1 objects", re.compile(r"of the 1 objects its /N declares"), lambda m: "of the 1 object its /N declares"),
    ("1 bytes long", re.compile(r"which is 1 bytes long"), lambda m: "which is 1 byte long"),
    ("decodes to 1 bytes", re.compile(r"it decodes to 1 bytes,"), lambda m: "it decodes to 1 byte,"),
    ("flate corrupt 1", re.compile(r"the 1 bytes decoded before the fault was found were kept"),
     lambda m: "the 1 byte decoded before the fault was found was kept"),
    ("flate checksum 1", re.compile(r"with the 1 bytes its data decoded to; all were kept, and some may be wrong"),
     lambda m: "with the 1 byte its data decoded to; it was kept, and may be wrong"),
    ("mediabox 1 values", re.compile(r"is an array of 1 values,"), lambda m: "is an array of 1 value,"),
    ("declared 1 bytes", re.compile(r"The stream declared 1 bytes"), lambda m: "The stream declared 1 byte"),
    ("runs past 1 bytes", re.compile(r"(runs past|more than) 1 bytes"), lambda m: f"{m.group(1)} 1 byte"),
    ("1 sections", re.compile(r"more than 1 sections"), lambda m: "more than 1 section"),
    ("run holds 1", re.compile(r"of which it holds 1; they were kept"), lambda m: "of which it holds 1; it was kept"),
]

kinds = collections.Counter()
out = []
for line in open(sys.argv[1], encoding="utf-8"):
    new = line
    for kind, pattern, replace in RULES:
        new, count = pattern.subn(replace, new)
        if count:
            kinds[kind] += count
    out.append(new)
open(sys.argv[2], "w", encoding="utf-8").write("".join(out))
for kind, count in sorted(kinds.items()):
    print(f"  {kind:22s} {count}")
print("  total rewrites:", sum(kinds.values()))
