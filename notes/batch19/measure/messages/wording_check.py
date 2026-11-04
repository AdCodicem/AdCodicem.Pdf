"""Checks that a corpus dump differs from main's only by #241's wording.

Rewrites every line of main's dump the way the prototype words it, then compares, document by document, the rewritten
main with the prototype's dump. Counts the lines the rewrite changed, by kind.
"""
import collections
import re
import sys


def parse(path):
    docs = collections.OrderedDict()
    current = None
    for line in open(path, encoding="utf-8"):
        line = line.rstrip("\n")
        if line.startswith("== "):
            current = line[3:]
            docs[current] = []
        else:
            docs[current].append(line)
    return docs


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

main = parse(sys.argv[1])
proto = parse(sys.argv[2])
assert list(main) == list(proto), "documents differ"
kinds = collections.Counter()
documents = collections.Counter()
mismatch = 0

for name, lines in main.items():
    rewritten = []
    for line in lines:
        new = line
        for kind, pattern, replace in RULES:
            new, count = pattern.subn(replace, new)
            if count:
                kinds[(kind, line.lstrip()[:1])] += count
                documents[kind, name] += 0
        rewritten.append(new)
    if rewritten != proto[name]:
        mismatch += 1
        print("MISMATCH", name)
        for a, b in zip(rewritten, proto[name]):
            if a != b:
                print("   rewritten:", a[:200])
                print("   prototype:", b[:200])

changed_documents = collections.defaultdict(set)
for kind, name in documents:
    changed_documents[kind].add(name)

print("documents", len(main), "mismatches after rewriting", mismatch)
for (kind, letter), count in sorted(kinds.items()):
    print(f"  {kind:22s} {'finding' if letter == 'F' else 'diagnostic'}s: {count}")
print("  total lines changed:", sum(kinds.values()))
for kind, names in sorted(changed_documents.items()):
    print(f"  {kind:22s} documents: {len(names)}")
print("  documents with any change:", len(set(n for names in changed_documents.values() for n in names)))
