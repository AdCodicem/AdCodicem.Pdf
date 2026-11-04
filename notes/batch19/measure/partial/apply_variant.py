"""Variants on top of prototype A, each applied or reverted alone.

usage: python3 -I apply_variant.py <worktree> <a2|b|t|c|n> [revert]

a2: a table the end of the file cuts before its trailer is always Read, rows or none, and reported as read in part.
b:  such a table is always Malformed (as one whose rows run into a dictionary): ReportLostSection, relocation tried for a
    /Prev or /XRefStm section, a rebuild at opening for the first.
t:  a table whose trailer keyword is followed by no dictionary is read in part too: its /Prev is lost (neighbor).
c:  file.size-wrong does not judge a chain one of whose sections was read in part (neighbor), through a record flag.
n:  the read-in-part report under a new code, xref.section-incomplete, rather than xref.section-unreadable.
"""
import sys

root, name = sys.argv[1], sys.argv[2]
revert = len(sys.argv) > 3
reader = "src/AdCodicem.Pdf/IO/PdfFileReader.cs"

variants = {
    "a2": [(reader, '''                    section.TrailerPosition = _source.Length;
                    section.Incomplete = true;
                    return section.HighestNumber >= 0;''', '''                    section.TrailerPosition = _source.Length;
                    section.Incomplete = true;
                    return true;''')],
    "b": [(reader, '''                    section.TrailerPosition = _source.Length;
                    section.Incomplete = true;
                    return section.HighestNumber >= 0;''', '''                    section.TrailerPosition = _source.Length;
                    section.Incomplete = true;
                    return false;''')],
    "t": [(reader, '''        else if (section.RefusedRow is { } refused)
        {''', '''        else if (section.TrailerFault == XRefTrailerFault.NotADictionary)
        {
            fault = "its trailer keyword is followed by no dictionary";
            kept = "its rows were kept, and the objects only the sections its trailer would name index are looked for by rebuilding the index";
        }
        else if (section.RefusedRow is { } refused)
        {''')],
    "c": [
        ("src/AdCodicem.Pdf/IO/XRef/XRefSectionRecord.cs", '''    public bool NoSubsection { get; set; }
''', '''    public bool NoSubsection { get; set; }

    /// <summary>
    /// Gets a value indicating whether the chain read the section only in part, which the reader reported: what the rest
    /// would have indexed, or the sections its trailer would have named, are unknown.
    /// </summary>
    public bool ReadInPart { get; set; }
'''),
        (reader, '''        _indexIncomplete = true;
        _diagnostics.Warn(
            ReadInPartCode,''', '''        _indexIncomplete = true;
        section.ReadInPart = true;
        _diagnostics.Warn(
            ReadInPartCode,'''),
        ("src/AdCodicem.Pdf/Validation/Rules/SizeWrongRule.cs", '''            if (section.State is XRefSectionState.NotFound or XRefSectionState.Malformed || section.CutByLimit ||''',
         '''            if (section.State is XRefSectionState.NotFound or XRefSectionState.Malformed || section.CutByLimit || section.ReadInPart ||'''),
    ],
    "n": [
        ("tests/corpus/manifest.schema.json", '''              "xref.section-empty",
''', '''              "xref.section-empty",
              "xref.section-incomplete",
'''),
        (reader, '''    private const string ReadInPartCode = PdfDiagnosticCodes.XRefSectionUnreadable;''',
         '''    private const string ReadInPartCode = PdfDiagnosticCodes.XRefSectionIncomplete;'''),
        ("src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs", '''    public const string XRefSectionEmpty = "xref.section-empty";
''', '''    public const string XRefSectionEmpty = "xref.section-empty";

    /// <summary>
    /// A cross-reference section the chain names was read only in part: a row that is no row ended a subsection, a stream
    /// holds fewer rows than it declares, the end of the file cuts a table before its trailer, or a row gives an object in
    /// use what no entry can hold. The rows read were kept; the objects only the rest indexes are missing from the index,
    /// which is rebuilt by scanning the file when one of them is asked for.
    /// </summary>
    public const string XRefSectionIncomplete = "xref.section-incomplete";
'''),
    ],
}

for path, a, b in variants[name]:
    full = f"{root}/{path}"
    s = open(full, encoding="utf-8").read()
    old, new = (b, a) if revert else (a, b)
    assert s.count(old) == 1, (name, path, old[:70], s.count(old))
    s = s.replace(old, new, 1)
    open(full, "w", encoding="utf-8").write(s)
print(("reverted " if revert else "applied ") + name)
