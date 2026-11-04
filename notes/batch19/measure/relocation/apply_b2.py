# Prototype (b2): a section that is there and malformed is reported where it is, with no relocation looked for (#281).
# Applies on the reader as prototype (a) leaves it, or on main. Run from the worktree root: python3 -I <path>/apply_b2.py
p = 'src/AdCodicem.Pdf/IO/PdfFileReader.cs'
s = open(p, encoding='utf-8').read()

old = """                // A section a guard cut is where it was named, and is not looked for elsewhere: what lies near it would
                // stand for it.
                if (section.CutByLimit || !TryRelocateXRefSection(offset, section, out previous, out hybrid))"""
new = """                // A section a guard cut is where it was named, and is not looked for elsewhere: what lies near it would
                // stand for it. So is one that is there and cannot be read — the xref keyword, or a cross-reference stream's
                // header and dictionary —: the offset names it, and any other section near it is another (#281).
                if (section.CutByLimit || section.State == XRefSectionState.Malformed ||
                    !TryRelocateXRefSection(offset, section, out previous, out hybrid))"""
assert s.count(old) == 1
s = s.replace(old, new)

old = """                if (TryReadXRefSection(stream.Offset, stream, out _, out _) != XRefSectionState.Read &&
                    (stream.CutByLimit || !TryRelocateXRefSection(hybrid, stream, out _, out _)))"""
new = """                if (TryReadXRefSection(stream.Offset, stream, out _, out _) != XRefSectionState.Read &&
                    (stream.CutByLimit || stream.State == XRefSectionState.Malformed ||
                        !TryRelocateXRefSection(hybrid, stream, out _, out _)))"""
assert s.count(old) == 1
s = s.replace(old, new)
open(p, 'w', encoding='utf-8').write(s)
print('b2 applied')
