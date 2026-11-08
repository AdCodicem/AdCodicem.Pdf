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
