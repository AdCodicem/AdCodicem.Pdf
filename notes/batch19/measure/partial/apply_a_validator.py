"""Prototype A, validator side: xref.subsection-missing (Warning) for a classic table with no subsection (#222).

usage: python3 -I apply_a_validator.py <worktree>
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


patch("src/AdCodicem.Pdf/Validation/PdfValidationRuleIds.cs", [
    ('''    public const string XRefSectionShifted = "xref.section-shifted";
''', '''    public const string XRefSectionShifted = "xref.section-shifted";

    /// <summary>
    /// A classic cross-reference table holds no subsection: its <c>trailer</c> keyword follows its <c>xref</c> keyword.
    /// <see cref="PdfValidationSeverity.Warning"/>.
    /// </summary>
    public const string XRefSubsectionMissing = "xref.subsection-missing";
'''),
])

patch("src/AdCodicem.Pdf/Validation/ValidationProfile.cs", [
    ('''            new SectionShiftedRule(),
''', '''            new SectionShiftedRule(),
            new SubsectionMissingRule(),
'''),
])

open(f"{root}/src/AdCodicem.Pdf/Validation/Rules/SubsectionMissingRule.cs", "w", encoding="utf-8").write('''using System.Globalization;
using AdCodicem.Pdf.IO.XRef;

namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.XRefSubsectionMissing"/>: each classic cross-reference table holds one or more
/// subsections (ISO 32000-1, 7.5.4).
/// </summary>
/// <remarks>
/// A table whose <c>trailer</c> keyword follows its <c>xref</c> keyword is read as indexing nothing, as a table whose one
/// subsection is <c>0 0</c> is, which ISO 32000-1 allows: the reader reads the file as written, and rebuilds nothing for
/// it, so the fault is a warning (ADR 45). qpdf calls such a table's syntax invalid and reconstructs the file.
/// </remarks>
internal sealed class SubsectionMissingRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.XRefSubsectionMissing;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Warning;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        foreach (var section in context.Structure.Sections)
        {
            if (!section.NoSubsection || section.State is not (XRefSectionState.Read or XRefSectionState.Relocated) ||
                !context.HasRoomFor(this))
            {
                continue;
            }

            context.Report(
                this,
                PdfValidationLocation.AtPosition(section.Offset),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The cross-reference table at offset {section.Offset} holds no subsection: its trailer keyword follows its xref keyword."),
                "Write a subsection after the xref keyword — 0 0 for a table that indexes nothing.");
        }
    }
}
''')

patch("docs/website/docs/reference/validation-rules.md", [
    ('''| `xref.section-shifted` | Warning |''', '''| `xref.subsection-missing` | Warning | A classic cross-reference table holds no subsection: its `trailer` keyword follows its `xref` keyword, where ISO 32000-1 (7.5.4) has one or more subsections follow it. The reader reads it as indexing nothing, as a table whose one subsection is `0 0`, and rebuilds nothing for it ([#222]). | Each classic table of the chain |
| `xref.section-shifted` | Warning |'''),
])
print("applied A (validator)")
