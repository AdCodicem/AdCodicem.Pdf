"""Prototype A' on top of A: the report keys on what the file lost, not on whether a guard also cut the section.

- XRefSectionRecord.Incomplete widens from "a stream holds fewer rows than it declares" to "the section's own reading lost
  rows through the file's fault": set too by a classic row that is no row (ReadSubsection) and by a table the end of the
  file cuts before its trailer (#207).
- ReportSectionReadInPart reports a read section that is Incomplete or refused a row, whether or not a guard also cut it
  (SectionMalformedRule judges those the file's too; GuardCutValueTests holds a guarded read to report what a whole read
  reports, #263). A fault met past a guard's cut on the way to the trailer (ReadTrailerPastRows) loses no row and is not
  reported by the reader: the guard's report stands.
- Mechanical: manifest schema admits the new code and rule.

usage: python3 -I apply_a_prime.py <worktree>
"""
import sys

root = sys.argv[1]


def patch(path, pairs):
    full = f"{root}/{path}"
    s = open(full, encoding="utf-8").read()
    for old, new in pairs:
        assert s.count(old) == 1, (path, old[:80], s.count(old))
        s = s.replace(old, new, 1)
    open(full, "w", encoding="utf-8").write(s)


patch("src/AdCodicem.Pdf/IO/XRef/XRefSectionRecord.cs", [
    ('''    /// <summary>
    /// Gets a value indicating whether a cross-reference stream holds fewer rows than it declares: the rows it holds place
    /// objects, but where a value they are read with is one a guard cut, beside which the data holds too few whatever the
    /// value holds.
    /// </summary>
    public bool Incomplete { get; set; }''', '''    /// <summary>
    /// Gets a value indicating whether the section's own reading lost rows through the file's fault, the rows before the
    /// fault kept: a classic table's row that is no row, which ends its subsection; a table the end of the file cuts before
    /// its trailer; a cross-reference stream that holds fewer rows than it declares — but where a value they are read with
    /// is one a guard cut, beside which the data holds too few whatever the value holds.
    /// </summary>
    public bool Incomplete { get; set; }'''),
])

patch("src/AdCodicem.Pdf/IO/PdfFileReader.cs", [
    ('''                _indexIncomplete = true;
                section.Fault ??= string.Create(
                    CultureInfo.InvariantCulture,
                    $"the row for object {number}, at offset {absolute + offsetToken.Start}, is not an offset, a generation and n or f");
                return true;''', '''                _indexIncomplete = true;
                section.Incomplete = true;
                section.Fault ??= string.Create(
                    CultureInfo.InvariantCulture,
                    $"the row for object {number}, at offset {absolute + offsetToken.Start}, is not an offset, a generation and n or f");
                return true;'''),
    ('''                    section.TrailerFault = XRefTrailerFault.Missing;
                    section.TrailerPosition = _source.Length;
                    return section.HighestNumber >= 0;''', '''                    section.TrailerFault = XRefTrailerFault.Missing;
                    section.TrailerPosition = _source.Length;
                    section.Incomplete = true;
                    return section.HighestNumber >= 0;'''),
    ('''    private void ReportSectionReadInPart(XRefSectionRecord section, string naming)
    {
        if (section.CutByLimit || section.State is not (XRefSectionState.Read or XRefSectionState.Relocated))
        {
            return;
        }

        var kind = section.Kind == XRefSectionKind.Stream ? "stream" : "table";
        string fault;
        string kept;

        if (section.Fault is { } stopped)
        {
            fault = stopped;
            kept = "the rows before it were kept, and the objects only the rest would index are looked for by rebuilding the index";
        }
        else if (section.TrailerFault == XRefTrailerFault.Missing && section.TrailerPosition >= _source.Length)
        {
            fault = "the file ends before its trailer";
            kept = "its rows were kept, and the objects only the sections its trailer would name index are looked for by rebuilding the index";
        }
        else if (section.RefusedRow is { } refused)''', '''    private void ReportSectionReadInPart(XRefSectionRecord section, string naming)
    {
        if (section.State is not (XRefSectionState.Read or XRefSectionState.Relocated))
        {
            return;
        }

        var kind = section.Kind == XRefSectionKind.Stream ? "stream" : "table";
        string fault;
        string kept;

        if (section.Incomplete && section.Fault is { } stopped)
        {
            fault = stopped;
            kept = "the rows before it were kept, and the objects only the rest would index are looked for by rebuilding the index";
        }
        else if (section.Incomplete)
        {
            fault = "the file ends before its trailer";
            kept = "its rows were kept, and the objects only the sections its trailer would name index are looked for by rebuilding the index";
        }
        else if (section.RefusedRow is { } refused)'''),
])

patch("tests/corpus/manifest.schema.json", [
    ('''              "xref.section-unreadable",
''', '''              "xref.section-unreadable",
              "xref.section-empty",
'''),
    ('''              "xref.section-shifted",
''', '''              "xref.section-shifted",
              "xref.subsection-missing",
'''),
])
print("applied A'")
