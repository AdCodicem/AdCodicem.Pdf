# Prototype (b2t), from (b2): only a section that declares itself one — a classic table's xref keyword, or a stream whose
# dictionary gives /Type /XRef — and cannot be read is reported where it is with no relocation; an object that only gives a
# /W where the chain names a section (a CIDFont's widths, a stream with no /Type) stays a guess, looked for nearby (#263).
p = 'src/AdCodicem.Pdf/IO/PdfFileReader.cs'
s = open(p, encoding='utf-8').read()

old = """                // stand for it. So is one that is there and cannot be read — the xref keyword, or a cross-reference stream's
                // header and dictionary —: the offset names it, and any other section near it is another (#281).
                if (section.CutByLimit || section.State == XRefSectionState.Malformed ||
                    !TryRelocateXRefSection(offset, section, out previous, out hybrid))"""
new = """                // stand for it. So is one that is there and cannot be read — the xref keyword, or a stream whose dictionary
                // gives /Type /XRef —: the offset names it, and any other section near it is another (#281).
                if (section.CutByLimit || DeclaresItselfASection(section) ||
                    !TryRelocateXRefSection(offset, section, out previous, out hybrid))"""
assert s.count(old) == 1
s = s.replace(old, new)

old = """                    (stream.CutByLimit || stream.State == XRefSectionState.Malformed ||
                        !TryRelocateXRefSection(hybrid, stream, out _, out _)))"""
new = """                    (stream.CutByLimit || DeclaresItselfASection(stream) ||
                        !TryRelocateXRefSection(hybrid, stream, out _, out _)))"""
assert s.count(old) == 1
s = s.replace(old, new)

old = """    /// <summary>
    /// Determines whether the chain read a section it named at <paramref name="offset"/>, as the file writes it: where it was
    /// named, or near it.
    /// </summary>"""
new = """    /// <summary>
    /// Determines whether a section the chain could not read is there all the same: a classic table's <c>xref</c> keyword, or
    /// a stream whose dictionary gives <c>/Type /XRef</c>. An object that only gives a <c>/W</c> may be anything — a CIDFont's
    /// widths — and leaves the section looked for nearby (#263, #281).
    /// </summary>
    private static bool DeclaresItselfASection(XRefSectionRecord section) =>
        section.State == XRefSectionState.Malformed && (section.Kind == XRefSectionKind.Table || section.TypedXRef);

""" + old
assert s.count(old) == 1
s = s.replace(old, new)
open(p, 'w', encoding='utf-8').write(s)
print('b2t applied')
