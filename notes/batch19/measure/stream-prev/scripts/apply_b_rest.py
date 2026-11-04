import sys

root = sys.argv[1]

p = root + '/src/AdCodicem.Pdf/IO/PdfFileReader.cs'
s = open(p).read()
old = '''        if ((_refusedRanges is not null || _olderRowsRefused) && RefusedToOlderRows(number) && !_xref.TryGet(number, out _))
        {'''
new = '''        if ((_refusedRanges is not null || _olderRowsRefused) && RefusedToOlderRows(number))
        {'''
assert old in s
s = s.replace(old, new)
open(p, 'w').write(s)

p = root + '/src/AdCodicem.Pdf/IO/XRef/XRefSectionRecord.cs'
s = open(p).read()
old = '''    /// <summary>Gets the section's own trailer — a cross-reference stream's dictionary —, or null when none was read.</summary>'''
new = '''    /// <summary>
    /// Gets a value indicating whether the chain went on through the <c>/Prev</c> of a cross-reference stream it cannot read,
    /// read from the stream's dictionary as a whole read reads it (#208): the one value of that dictionary the reader read.
    /// </summary>
    public bool PreviousHandedOn { get; set; }

    /// <summary>Gets the section's own trailer — a cross-reference stream's dictionary —, or null when none was read.</summary>'''
assert old in s
s = s.replace(old, new)
open(p, 'w').write(s)

p = root + '/src/AdCodicem.Pdf/Validation/Rules/TrailerValueWrongRule.cs'
s = open(p).read()
old = '''        foreach (var section in sections)
        {
            if (section.State is not (XRefSectionState.Read or XRefSectionState.Relocated) || section.Trailer is not { } trailer)
            {
                continue;
            }

            var judge = new Judge(context, this, section);
'''
new = '''        foreach (var section in sections)
        {
            if (section.PreviousHandedOn && section.Trailer is { } unread && IsFollowed(sections, section, PdfName.Prev))
            {
                // A cross-reference stream the reader cannot read, but whose /Prev the chain went on through: that value alone
                // was read, and is judged (#208).
                new Judge(context, this, section).Value(new What(PdfName.Prev), unread.GetRaw(PdfName.Prev), Iso1, "Table 17", resolve: true);
                continue;
            }

            if (section.State is not (XRefSectionState.Read or XRefSectionState.Relocated) || section.Trailer is not { } trailer)
            {
                continue;
            }

            var judge = new Judge(context, this, section);
'''
assert old in s
s = s.replace(old, new)
open(p, 'w').write(s)
print('applied')
