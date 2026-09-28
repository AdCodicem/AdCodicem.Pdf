using System.Globalization;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.XRefObjectPastSize"/>: no in-use object is numbered above the trailer's
/// <c>/Size</c>.
/// </summary>
/// <remarks>
/// ISO 32000-1 (Table 15): "Any object in a cross-reference section whose number is greater than this value shall
/// be ignored and defined to be missing by a conforming reader." The entries, the objects' headers and the
/// references to them agree against <c>/Size</c>, and the reader reads those objects, as qpdf does while warning: a
/// warning (ADR 45), which says a reader applying the sentence would lose them — in iPRES t04-016, the catalog.
/// Reported once for the file, whose single <c>/Size</c> is the cause, with how many objects it leaves out and the
/// first of them; <see cref="PdfValidationRuleIds.FileSizeWrong"/> then says nothing more of that <c>/Size</c>.
/// </remarks>
internal sealed class ObjectPastSizeRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.XRefObjectPastSize;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Warning;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        var probe = context.Probe;

        if (probe.FirstPastSize is not { } first || context.Structure.SizeAsWritten is not PdfInteger size)
        {
            return;
        }

        var message = probe.PastSizeCount == 1
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"The index holds {first.Message} although the trailer's /Size is {size.Value}: the specification makes a conforming reader ignore an object numbered above it.")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"The index holds {probe.PastSizeCount} objects numbered above the trailer's /Size, {size.Value}, the first being {first.Message}: the specification makes a conforming reader ignore them.");

        context.Report(this, first.Location, message, "Set /Size to one more than the highest object number.");
    }
}
