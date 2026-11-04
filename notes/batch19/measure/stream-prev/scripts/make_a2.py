"""Writes the clean A2 prototype (hand /Prev on, no relocation, no refusal) from main's reader source."""
import sys

main_src, out = sys.argv[1], sys.argv[2]
s = open(main_src).read()

old = '''                // A section a guard cut is where it was named, and is not looked for elsewhere: what lies near it would
                // stand for it.
                if (section.CutByLimit || !TryRelocateXRefSection(offset, section, out previous, out hybrid))
                {
                    ReportLostSection(section, naming, offset, namedFrom);
                    break;
                }
            }'''
new = '''                // A cross-reference stream whose dictionary was read, and whose rows cannot be, is there: it is not looked for
                // elsewhere, what lies near it would stand for it. Its /Prev, read from that dictionary, names the older
                // sections as a whole read names them (#208): the section stays unreadable, reported once, and the index
                // incomplete for what only its rows index.
                if (UnreadStreamNamingPrevious(section) is { } dictionary)
                {
                    ReportLostSection(section, naming, offset, namedFrom);
                    previous = SectionOffset(dictionary, PdfName.Prev, section.Offset, out var hidden);
                    section.ChainHiddenByGuard = hidden;
                }

                // A section a guard cut is where it was named, and is not looked for elsewhere: what lies near it would
                // stand for it.
                else if (section.CutByLimit || !TryRelocateXRefSection(offset, section, out previous, out hybrid))
                {
                    ReportLostSection(section, naming, offset, namedFrom);
                    break;
                }
            }'''
assert old in s
s = s.replace(old, new)

old = '''    /// <summary>
    /// Determines whether the chain read a section it named at <paramref name="offset"/>, as the file writes it: where it was
    /// named, or near it.
    /// </summary>'''
new = '''    /// <summary>
    /// Gives the dictionary of a cross-reference stream the chain cannot read, where it was read as the section's and gives a
    /// <c>/Prev</c>: a fault in the rows, or in a value they are read with, leaves that <c>/Prev</c> as a whole read reads it
    /// (#208). Null for a section a guard cut, which is the guard's (ADR 34), for a hybrid file's stream, whose <c>/Prev</c>
    /// no reader follows, and for a table, whose trailer follows its rows.
    /// </summary>
    private static PdfDictionary? UnreadStreamNamingPrevious(XRefSectionRecord section) =>
        section is { State: XRefSectionState.Malformed, CutByLimit: false, Kind: XRefSectionKind.Stream } &&
        section.NamedBy != "/XRefStm" && section.Trailer is { } dictionary && dictionary.GetRaw(PdfName.Prev) is not null
            ? dictionary
            : null;

''' + old
assert old in s
s = s.replace(old, new, 1)
open(out, 'w').write(s)
print('written')
