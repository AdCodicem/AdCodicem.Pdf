using System.Globalization;
using AdCodicem.Pdf.IO.XRef;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.FileSizeWrong"/>: each section's <c>/Size</c> is one more than the highest object
/// number the section and those it updates use.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-1 requires <c>/Size</c> in every trailer (Table 15) and in every cross-reference stream (Table 17),
/// where it "shall be equivalent to the Size entry in a trailer dictionary"; an update's counts the objects of the
/// sections before it (7.5.6), and a hybrid file's trailer those of the stream its <c>/XRefStm</c> names (7.5.8.4); a
/// hybrid file's stream meets one of Table 17's two readings or the other (see <see cref="NamingTableSize"/>).
/// Readers use the newest trailer's, and only to size what they read: a wrong one is a warning.
/// </para>
/// <para>
/// A <c>/Size</c> that leaves in-use objects numbered above it is
/// <see cref="PdfValidationRuleIds.XRefObjectPastSize"/>'s instead, which says the specification makes a conforming
/// reader ignore those objects. The rule needs the whole chain to count the objects each section updates: a chain
/// that lost a section, or that one of the reader's limits cut, is not judged.
/// </para>
/// </remarks>
internal sealed class SizeWrongRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.FileSizeWrong;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Warning;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        var structure = context.Structure;
        var sections = structure.Sections;

        if (!structure.ChainRead || structure.ChainCutAt >= 0 || !IsWhole(sections))
        {
            return;
        }

        // The highest number each section and the older ones use: the chain runs from the newest section to the
        // oldest, each /XRefStm after the table that names it. A cross-reference stream is an object of the
        // revision it closes, counted even when it gives itself no entry; a hybrid file's /XRefStm stream is not —
        // the newer table that names it indexes it.
        var highest = new int[sections.Count];
        var running = -1;

        for (var index = sections.Count - 1; index >= 0; index--)
        {
            var section = sections[index];
            var own = section.Kind == XRefSectionKind.Stream && section.NamedBy != "/XRefStm" ? section.StreamObjectNumber : -1;
            running = Math.Max(running, Math.Max(section.HighestNumber, own));
            highest[index] = running;
        }

        var pastSize = context.Probe.PastSizeCount > 0 && structure.SizeAsWritten is PdfInteger merged ? merged.Value : -1;

        for (var index = 0; index < sections.Count; index++)
        {
            var section = sections[index];

            if (section.Trailer is not { } trailer || highest[index] < 0)
            {
                continue;
            }

            var expected = highest[index] + 1L;
            var subject = section.Kind == XRefSectionKind.Stream
                ? string.Create(CultureInfo.InvariantCulture, $"The cross-reference stream at offset {section.Offset}")
                : string.Create(CultureInfo.InvariantCulture, $"The trailer of the cross-reference table at offset {section.Offset}");
            string message;

            switch (trailer.GetRaw(PdfName.Size))
            {
                case null:
                    message = $"{subject} has no /Size.";
                    break;

                case PdfInteger { Value: >= 0 } size when size.Value == expected || size.Value == NamingTableSize(sections, index):
                    continue;

                case PdfInteger { Value: >= 0 } size:
                    if (size.Value < expected && size.Value == pastSize)
                    {
                        // Objects numbered above it: the error rule says so.
                        continue;
                    }

                    message = string.Create(
                        CultureInfo.InvariantCulture,
                        $"{subject} gives /Size {size.Value}, where the highest object number it and the sections it updates use, {expected - 1}, makes it {expected}.");
                    break;

                default:
                    message = $"{subject} gives a /Size that is not a count of objects.";
                    break;
            }

            context.Report(
                this,
                PdfValidationLocation.AtPosition(section.TrailerPosition >= 0 ? section.TrailerPosition : section.Offset),
                message,
                "Set /Size to one more than the highest object number the section and those it updates use.");
        }
    }

    /// <summary>
    /// Gets the <c>/Size</c> of the table whose <c>/XRefStm</c> names the section, or -1 when the section is not a
    /// hybrid file's stream.
    /// </summary>
    /// <remarks>
    /// Table 17 asks two things of a cross-reference stream's <c>/Size</c>: one more than the highest number it and
    /// the sections it updates use, and "equivalent to the Size entry in a trailer dictionary". A hybrid file's
    /// stream cannot always meet both — its own object is indexed by the newer table that names it —, and writers
    /// meet one or the other: either is accepted.
    /// </remarks>
    private static long NamingTableSize(IReadOnlyList<XRefSectionRecord> sections, int index)
    {
        var stream = sections[index];

        if (stream.NamedBy != "/XRefStm")
        {
            return -1;
        }

        for (var candidate = index - 1; candidate >= 0; candidate--)
        {
            if (sections[candidate].Offset == stream.NamedFrom)
            {
                return sections[candidate].Trailer.GetRaw(PdfName.Size) is PdfInteger { Value: >= 0 } size ? size.Value : -1;
            }
        }

        return -1;
    }

    private static bool IsWhole(IReadOnlyList<XRefSectionRecord> sections)
    {
        foreach (var section in sections)
        {
            if (section.State is XRefSectionState.NotFound or XRefSectionState.Malformed || section.CutByLimit)
            {
                return false;
            }
        }

        return true;
    }
}
