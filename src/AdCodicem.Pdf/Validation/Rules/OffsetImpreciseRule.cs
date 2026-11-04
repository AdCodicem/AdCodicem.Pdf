using System.Globalization;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.IO.XRef;

namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.XRefOffsetImprecise"/>: every offset names the first byte of what it designates.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-1 counts an entry's offset "to the beginning of the object" (7.5.4), and the one <c>startxref</c> gives
/// "to the beginning of the xref keyword" (7.5.5). Microsoft Print to PDF names the line feed before each object,
/// and some tools the one before <c>xref</c>: every reader skips the white space and reads the file as intended,
/// qpdf warning only about the second. Wrong, and harmless: a warning, once for the file, with how many offsets and
/// the first of them — a writer that does it does it everywhere.
/// </para>
/// <para>
/// Sections come first, in the chain's order, then entries, by object number. A section counts once for each offset, as
/// written, that names it short: where it was read, where an <c>/XRefStm</c> names it again, or names what its table's
/// <c>/Prev</c> reads, through the white space before it (#156), and where a <c>/Prev</c> loops back to it so (#188) —
/// so that what is reported does not depend on which naming the chain met first.
/// </para>
/// </remarks>
internal sealed class OffsetImpreciseRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.XRefOffsetImprecise;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Warning;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        var count = 0;
        ProbeFinding? first = null;

        foreach (var section in context.Structure.Sections)
        {
            // What a naming again reached is the section it records as read there, where the white space it falls short by
            // ends: one it reached and could not read designates nothing read, as a section that cannot be read does not
            // count where it is named; and one its table's /Prev found moved lies past what the naming crossed.
            var kind = section switch
            {
                { State: XRefSectionState.Read } => section.Kind,
                { State: XRefSectionState.NamedAgain, ReadAs: { State: XRefSectionState.Read or XRefSectionState.Relocated } read }
                    when read.Offset + read.Padding == section.Offset + section.Padding => read.Kind,
                _ => XRefSectionKind.Unknown,
            };

            if (kind == XRefSectionKind.Unknown || section.Padding <= 0)
            {
                continue;
            }

            if (count++ == 0)
            {
                first = new ProbeFinding(
                    PdfValidationLocation.AtPosition(section.Offset),
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{section.NamedBy} gives offset {section.Offset}, {CountText.Bytes(section.Padding)} before {Target(kind)}"));
            }
        }

        // A /Prev that loops back through the white space before a section the chain read names it short too, the loop
        // placed where that section starts, past what the offset names: the chain's last naming. One that loops back to the
        // offset it names, or to a section the chain could not read, names none short.
        var structure = context.Structure;
        var named = context.Document.Reader.PositionOf(structure.LoopWrittenOffset);

        if (structure.LoopOffset > named && ReadAt(structure, structure.LoopOffset) is { } looped)
        {
            if (count++ == 0)
            {
                first = new ProbeFinding(
                    PdfValidationLocation.AtPosition(named),
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{structure.LoopNamedBy} gives offset {named}, {CountText.Bytes(structure.LoopOffset - named)} before {Target(looped.Kind)}"));
            }
        }

        var probe = context.Probe;
        count += probe.ImpreciseCount;
        first ??= probe.FirstImprecise;

        if (first is not { } example)
        {
            return;
        }

        context.Report(
            this,
            example.Location,
            count == 1
                ? $"An offset names the white space before what it designates rather than its first byte: {example.Message}."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{count} offsets name the white space before what they designate rather than its first byte; the first: {example.Message}."),
            "Write each offset as the position of the first byte of what it designates.");
    }

    /// <summary>Gives what an offset that names a section of <paramref name="kind"/> designates, in words.</summary>
    private static string Target(XRefSectionKind kind) =>
        kind == XRefSectionKind.Stream ? "the header of the cross-reference stream" : "the xref keyword";

    /// <summary>
    /// Gives the section the chain read, where it was named or found moved, that starts at <paramref name="start"/>, past
    /// the white space before it; null when none does.
    /// </summary>
    private static XRefSectionRecord? ReadAt(FileStructure structure, long start)
    {
        foreach (var section in structure.Sections)
        {
            if (section.State is XRefSectionState.Read or XRefSectionState.Relocated && section.Offset + section.Padding == start)
            {
                return section;
            }
        }

        return null;
    }
}
