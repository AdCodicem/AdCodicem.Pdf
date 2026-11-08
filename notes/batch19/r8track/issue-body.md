**A hybrid file's table may name, through `/XRefStm`, a cross-reference stream an older revision wrote, while an older table's `/Prev` falls a few bytes short of that same stream. The chain meets the stream first through the newer table's `/XRefStm`. When it then looks near the older table's `/Prev`, the search passes the stream over as a section the chain has reached and never takes it. With nothing else in reach, or only the older table's own unnamed stream, the chain is cut, the index is rebuilt at opening, and the validator gives a false Error: `file.root-invalid`, or `xref.section-not-found` with the stream 7 bytes away. `main` reads the same files whole.**

## Why

`TryRelocateXRefSection` (`src/AdCodicem.Pdf/IO/PdfFileReader.cs:4978` at `da2b06f`, the head of batch 19's branch) skips every place where a section the chain has reached starts (`:5047`). Batch 19 decided so for #188 and #281: `main` landed on such sections, and reported loops and shifts the files did not have. There is one exception, `takeAt` (`:5027`): the stream the table's own `/XRefStm` read in the stream's place, which that table's `/Prev` may take.

A stream a newer table's `/XRefStm` read is no exception, whether this table's `/XRefStm` names it again or not. Batch 19 decided what a naming again does. The `NameAgain` branch of `TryReadXRefChain` (`:3523-3530`) keeps the stream as `streamNamedAgain`, and the search near this table's `/Prev` uses it only to tell where the table's own stream can start (`:5028-5030`, `:5045`):
- a stream that starts before it keeps its nearest-first place;
- one at or past it that gives no `/Prev`, or the table's, is tried after every other place;
- the stream named again itself stays passed over as reached, and is never taken in its place.

`docs/website/docs/concepts/lazy-reading.md:196-198` and the #156 row of `docs/threat-model.md` (`:432`) say so.

Taking it was left open because that is #330's question: whether a `/Prev` may reach a stream the chain read only through another table's `/XRefStm`. At the exact offset, the three files below are #330's loop, on `main` and on the branch alike: `xref.chain-cycle`, a rebuild at opening, and `Error xref.chain-loop` at revision 2's table. A few bytes off, `main` reads them whole only because its search does not look at what the chain has read, which is #188's defect landing, here, on the right section.

## The shapes, as measured

The three files share revisions 0, 1 and 3:
- revision 0: the catalog, the page tree and the page, objects 1 to 3, and object 5, `(old five)`, indexed by a classic table at 208, `/Size 6`, then a comment line of 1,200 dashes;
- revision 1: object 4, `(four)`, and S1, object 10 at 1579, a cross-reference stream indexing objects 4 and 10, `/Prev 208`;
- revision 2: object 5 again, `(new five)`, then as below;
- revision 3: object 6, `(six)`, and a table indexing it, which `startxref` names, its `/Prev` revision 2's table and its `/XRefStm` 1579. The chain reads it, then S1 through its `/XRefStm`, then revision 2's table.

Revision 2 differs:
1. **An `/XRefStm` copied into a later trailer, its naming again landing where the chain needs it** (2,065 bytes). Revision 2 writes its own stream, object 11 at 1730, indexing objects 5 and 11, with no `/Prev`, which nothing names. Its table at 1845 gives `/Size 12 /Prev 1572 /XRefStm 1579`: the `/Prev` falls 7 bytes short of S1, and the `/XRefStm` names S1, the previous revision's stream, which #156 reports at this table. Revision 3's table, at 1955, copies the `/XRefStm`.
2. **A stream named again by a table whose `/Prev` falls a few bytes off** (1,974 bytes). Revision 2's table, at 1730, indexes object 5 and gives `/Size 11 /Prev 1572 /XRefStm 1579`. Revision 3's table, at 1864, copies the `/XRefStm`.
3. **A plain naming again**, #330's shape with the `/Prev` 7 bytes off (1,960 bytes). Revision 2's table, at 1730, indexes object 5 and gives `/Size 11 /Prev 1572`, with no `/XRefStm`: it names S1 again through its `/Prev` alone. Revision 3's table is at 1850.

Shapes 1 and 2 copy revision 2's `/XRefStm` into revision 3's trailer one revision late, the writer pattern #321 finds in two corpus documents.

**On the branch at `da2b06f`**, run twice, identical: at opening, then the objects, then the findings. Shape 1:

```
Repair xref.offset-adjusted at 1730: The cross-reference section /Prev names at offset 1572 was found 158 bytes after it.
Repair xref.rebuilt: The cross-reference index was rebuilt by scanning the file.
Information object.redefined: Rebuilding the index met a second definition of object 5. [...]
WasRepaired True; GetObject(5) = "new five"; GetObject(6) = "six"
Error file.root-invalid at object 1 0: The trailer's /Root names object 1 0, which the file does not hold. No object of the file is a catalog.
Warning file.size-wrong at offset 1579: The cross-reference stream at offset 1579 gives /Size 11, where the highest object number it and the sections it updates use, 11, makes it 12.
Warning xref.section-shifted at offset 1730: The cross-reference section /Prev names at offset 1572 starts 158 bytes after it, at offset 1730.
```

The search passes S1 over, then takes revision 2's own stream, 158 bytes off, which starts past S1 and gives no `/Prev`. The chain ends there, the classic table that holds the catalog is never read, and the index is rebuilt at opening. The `file.root-invalid` message is #260's.

Shapes 2 and 3 give the same lines; shape 2's sections also record revision 2's `/XRefStm` as named again:

```
Warning xref.section-missing at 1572: The cross-reference section /Prev names at offset 1572 is not there, nor near it; the objects only it indexes are looked for by rebuilding the index.
Repair xref.rebuilt: The cross-reference index was rebuilt by scanning the file.
Information object.redefined: Rebuilding the index met a second definition of object 5. [...]
WasRepaired True; GetObject(5) = "new five"; GetObject(6) = "six"
Error xref.section-not-found at offset 1572: The cross-reference section /Prev names at offset 1572 is not there, nor within 512 bytes of it: the offset holds neither the xref keyword nor an object.
```

**On `main` at `21a0298`**, run twice, identical, the search takes S1, 7 bytes on, reads it again as revision 2's previous section, then reads the classic table. All three files give:

```
Repair xref.offset-adjusted at 1579: The cross-reference section /Prev names at offset 1572 was found 7 bytes from there.
WasRepaired False
Warning xref.section-shifted at offset 1579: The cross-reference section /Prev names at offset 1572 starts 7 bytes after it, at offset 1579.
```

Shape 1 reads object 5 as `"old five"` there, since nothing names revision 2's own stream, and adds a `file.size-wrong` at revision 2's table and one at revision 3's. Shapes 2 and 3 read `"new five"` from revision 2's table. All three read object 6 as `"six"`.

Without revision 3, each file reads whole on the branch too: the search takes S1 and goes on to the classic table, and shapes 1 and 2 add `xref.hybrid-stream-lost` and `Error xref.hybrid-stream-wrong` at revision 2's table, whose `/XRefStm` names what its `/Prev` reads. So whether revision 2's `/Prev` reads right depends on what revision 3 wrote.

Where the copy comes two revisions late, as in #321, and revision 2's own stream lies past the stream named again, batch 19's decision does help. Take an `/XRefStm` copied into two later trailers, with revision 2's `/Prev` falling 73 bytes past revision 1's table and 66 bytes short of revision 2's own unnamed stream. It now reads whole through revision 1's table, with `Warning xref.stream-passed-over` at the own stream. `main` takes the own stream and rebuilds, with `Error file.root-invalid`.

## Reproducer

`python3 gen.py` writes `shape1.pdf` to `shape3.pdf` with revision 2's `/Prev` 7 bytes short of S1, byte for byte the files measured above; `python3 gen.py 0` writes them at #330's exact offset.

```python
import sys

def build(shape, short):
    buf = bytearray()
    def raw(s): buf.extend(s.encode("latin-1") if isinstance(s, str) else s)
    def obj(n, body): at = len(buf); raw(f"{n} 0 obj\n{body}\nendobj\n"); return at
    def row(at): return f"{at:010d} 00000 n \n"
    def xstream(n, size, index, rs, extra=""):  # a cross-reference stream, /W [1 4 2]
        at = len(buf)
        d = b"".join(t.to_bytes(1, "big") + a.to_bytes(4, "big") + g.to_bytes(2, "big") for t, a, g in rs)
        raw(f"{n} 0 obj\n<< /Type /XRef /Size {size} /W [1 4 2] /Index [{index}] {extra}/Length {len(d)} >>\nstream\n")
        raw(d); raw("\nendstream\nendobj\n"); return at
    raw("%PDF-1.7\n")
    o = [obj(1, "<< /Type /Catalog /Pages 2 0 R >>"), obj(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
         obj(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 9 9] >>"), obj(5, "(old five)")]
    t0 = len(buf)  # revision 0: a classic table
    raw(f"xref\n0 4\n0000000000 65535 f \n{row(o[0])}{row(o[1])}{row(o[2])}5 1\n{row(o[3])}trailer\n<< /Size 6 /Root 1 0 R >>\n")
    raw("%" + "-" * 1200 + "\n")
    four = obj(4, "(four)")
    s1 = len(buf)  # revision 1: a cross-reference stream S1, its /Prev the classic table
    xstream(10, 11, "4 1 10 1", [(1, four, 0), (1, s1, 0)], f"/Prev {t0} ")
    five = obj(5, "(new five)")
    if shape == 1:  # revision 2: its own stream, which nothing names, and a table naming S1 through /XRefStm
        s2 = len(buf)
        xstream(11, 12, "5 1 11 1", [(1, five, 0), (1, s2, 0)])
        t2 = len(buf)
        raw(f"xref\n0 1\n0000000000 65535 f \ntrailer\n<< /Size 12 /Root 1 0 R /Prev {s1 - short} /XRefStm {s1} >>\n")
    else:  # revision 2: a table indexing object 5, naming S1 through /XRefStm (2) or not (3)
        t2 = len(buf)
        xs = f" /XRefStm {s1}" if shape == 2 else ""
        raw(f"xref\n0 1\n0000000000 65535 f \n5 1\n{row(five)}trailer\n<< /Size 11 /Root 1 0 R /Prev {s1 - short}{xs} >>\n")
    six = obj(6, "(six)")
    t3 = len(buf)  # revision 3: its /XRefStm names S1, the first naming the chain meets
    raw(f"xref\n6 1\n{row(six)}trailer\n<< /Size {12 if shape == 1 else 11} /Root 1 0 R /Prev {t2} /XRefStm {s1} >>\n")
    raw(f"startxref\n{t3}\n%%EOF\n")
    return bytes(buf)

short = int(sys.argv[1]) if len(sys.argv) > 1 else 7  # how far before S1 revision 2's /Prev falls; 0 is #330's exact offset
for shape in (1, 2, 3):
    with open(f"shape{shape}.pdf", "wb") as f:
        f.write(build(shape, short))
```

## What it waits on

Blocked by: #330

Taking S1 means a `/Prev` reaching a stream the chain read only through another table's `/XRefStm`, which #330 asks the maintainer to settle:
- Under its first option, where the newer table's `/XRefStm` is at fault, the search may take S1. The three files then read whole, at the exact offset as a few bytes off, and revision 3's `/XRefStm` is reported (`xref.hybrid-stream-lost`, `Error xref.hybrid-stream-wrong`).
- Under its second, a loop, the relocated offset should give the loop the exact offset gives, not a missing section, nor `main`'s silent read.

Either way the outcome follows #330's decision.

## What closes it

- #330 is decided.
- The search near a table's `/Prev` treats a cross-reference stream a newer table's `/XRefStm` read as #330's decision treats the exact offset, whether this table's `/XRefStm` names it again (shapes 1 and 2) or not (shape 3). Under the first option:
  - the three shapes read through S1 to the classic table, with no rebuild at opening, and no `file.root-invalid` or `xref.section-not-found`;
  - shapes 1 and 2 keep the `xref.hybrid-stream-lost` at revision 2's table that the files without revision 3 give;
  - revision 2's own stream in shape 1 stays unread, as on `main` and in the file without revision 3.
- The order batch 19 gives the other streams near the `/Prev` from the stream named again stays as it is.
- `CrossReferenceChainTests` and `CrossReferenceRuleTests` hold the three shapes, beside the same files without revision 3 and at the exact offset. `HybridFiles.CopyingTheXRefStmIntoLaterTrailers` builds a neighbor.
- `docs/website/docs/concepts/lazy-reading.md`, `docs/threat-model.md` (the #156 row), `docs/website/docs/reference/diagnostics.md` and `docs/website/docs/reference/validation-rules.md` say which streams a newer table's `/XRefStm` read the search may take, in place of "never taken in its place".

Placed with batch 31 of M02's step 4, beside #330 and #321, whose records of a stream more than one table names it shares.

Found by batch 19's adversarial review on `012e13d`, and measured again on `da2b06f` and on `main` at `21a0298`.

---
_Generated by [Claude Code](https://claude.ai/code)_