using System.Globalization;
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
/// Sections come first, in the chain's order, then entries, by object number.
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
            if (section.State != XRefSectionState.Read || section.Padding <= 0)
            {
                continue;
            }

            if (count++ == 0)
            {
                var target = section.Kind == XRefSectionKind.Stream ? "the header of the cross-reference stream" : "the xref keyword";
                first = new ProbeFinding(
                    PdfValidationLocation.AtPosition(section.Offset),
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{section.NamedBy} gives offset {section.Offset}, {RuleText.Bytes(section.Padding)} before {target}"));
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
}
