"""Prototype A: report a section read in part (#207, #210, refused rows) and a table with no subsection (#222).

usage: python3 -I apply_a.py <worktree>
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
    ('''    public bool Incomplete { get; set; }
''', '''    public bool Incomplete { get; set; }

    /// <summary>
    /// Gets a value indicating whether a classic table holds no subsection: its <c>trailer</c> keyword follows its
    /// <c>xref</c> keyword, where ISO 32000-1 (7.5.4) has one or more subsections follow it.
    /// </summary>
    public bool NoSubsection { get; set; }
'''),
    ('''        Incomplete = attempt.Incomplete;
''', '''        Incomplete = attempt.Incomplete;
        NoSubsection = attempt.NoSubsection;
'''),
])

patch("src/AdCodicem.Pdf/IO/PdfFileReader.cs", [
    ('''            using var window = _source.GetWindow(absolute, windowSize);
            var span = window.Memory.Span;
            var lexer = new PdfLexer(span);

            // A window shorter than asked holds the end of the file''',
     '''            using var window = _source.GetWindow(absolute, windowSize);
            var span = window.Memory.Span;
            var lexer = new PdfLexer(span);
            var subsections = 0;

            // A window shorter than asked holds the end of the file'''),
    ('''                if (token.IsKeyword(TrailerKeyword))
                {
                    section.TrailerPosition = absolute + token.Start;
''', '''                if (token.IsKeyword(TrailerKeyword))
                {
                    section.TrailerPosition = absolute + token.Start;
                    section.NoSubsection = subsections == 0;
'''),
    ('''                        $"the subsection header at offset {absolute + token.Start} numbers its rows past object {PdfObjectId.MaxNumber}");
                    return false;
                }
''', '''                        $"the subsection header at offset {absolute + token.Start} numbers its rows past object {PdfObjectId.MaxNumber}");
                    return false;
                }

                subsections++;
'''),
    ('''                else
                {
                    // The file ends before the table's trailer.
                    section.TrailerFault = XRefTrailerFault.Missing;
                    section.TrailerPosition = _source.Length;
                }

                // Keep whatever was indexed.
                return _xref.Count > 0;''',
     '''                else
                {
                    // The file ends before the table's trailer: a table that read rows is read in part, and reported so by
                    // the chain; one that read none is no section to keep (#207). The count is the section's rows, not the
                    // index's, which a newer section may have filled.
                    section.TrailerFault = XRefTrailerFault.Missing;
                    section.TrailerPosition = _source.Length;
                    return section.HighestNumber >= 0;
                }

                // Keep whatever was indexed.
                return _xref.Count > 0;'''),
    ('''            _olderNumberedPast |= sections > 1 && section.HighestNumber > _highestRead;
            _highestRead = Math.Max(_highestRead, section.HighestNumber);

            // A guard kept a trailer from being read whole''',
     '''            ReportSectionReadInPart(section, naming);
            _olderNumberedPast |= sections > 1 && section.HighestNumber > _highestRead;
            _highestRead = Math.Max(_highestRead, section.HighestNumber);

            // A guard kept a trailer from being read whole'''),
    ('''                    ReportLostSection(stream, "/XRefStm", hybrid, section.Offset);
                }
''', '''                    ReportLostSection(stream, "/XRefStm", hybrid, section.Offset);
                }
                else
                {
                    ReportSectionReadInPart(stream, "/XRefStm");
                }
'''),
    ('''    /// <summary>
    /// Reports a section of the chain that could not be found, and marks the index incomplete: at the offset it was''',
     '''    /// <summary>
    /// Reports a section of the chain that was read, but only in part — a row that is no row ended a subsection, a stream
    /// holds fewer rows than it declares, the end of the file cuts a table before its trailer, a row was refused —, and
    /// marks the index incomplete; or a classic table that holds no subsection, read as indexing nothing.
    /// </summary>
    /// <remarks>
    /// Reported once, at the section, the first fault in the message (#207, #210, #222). A section one of the reader's
    /// guards cut is the guard's, which says so (ADR 34), whatever the reading met before the cut.
    /// </remarks>
    private void ReportSectionReadInPart(XRefSectionRecord section, string naming)
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
        else if (section.RefusedRow is { } refused)
        {
            fault = refused;
            kept = "that row alone was refused, the rows after it read, and its object is looked for by rebuilding the index";
        }
        else
        {
            if (section.NoSubsection)
            {
                _diagnostics.Repair(
                    SectionEmptyCode,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The cross-reference table {naming} names at offset {section.Offset} holds no subsection: its trailer keyword follows its xref keyword. It was read as indexing nothing."),
                    section.Offset);
            }

            return;
        }

        _indexIncomplete = true;
        _diagnostics.Warn(
            ReadInPartCode,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The cross-reference {kind} {naming} names at offset {section.Offset} was read only in part: {fault}; {kept}."),
            section.Offset);
    }

    /// <summary>The code a section read in part is reported under (prototype: chosen by the maintainer).</summary>
    private const string ReadInPartCode = PdfDiagnosticCodes.XRefSectionUnreadable;

    /// <summary>The code a classic table with no subsection is reported under (prototype: chosen by the maintainer).</summary>
    private const string SectionEmptyCode = PdfDiagnosticCodes.XRefSectionEmpty;

    /// <summary>
    /// Reports a section of the chain that could not be found, and marks the index incomplete: at the offset it was'''),
])

patch("src/AdCodicem.Pdf/Diagnostics/PdfDiagnosticCodes.cs", [
    ('''    public const string XRefSectionUnreadable = "xref.section-unreadable";
''', '''    public const string XRefSectionUnreadable = "xref.section-unreadable";

    /// <summary>
    /// A classic cross-reference table holds no subsection: its <c>trailer</c> keyword follows its <c>xref</c> keyword,
    /// where ISO 32000-1 (7.5.4) has one or more subsections follow it. It was read as indexing nothing, as a table whose
    /// one subsection is <c>0 0</c> is.
    /// </summary>
    public const string XRefSectionEmpty = "xref.section-empty";
'''),
])
print("applied A (reader)")
