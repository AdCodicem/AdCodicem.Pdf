using System.Globalization;
using AdCodicem.Pdf.IO.XRef;

namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.XRefSectionMalformed"/>: each cross-reference section the chain names can be read
/// whole — its subsection headers and rows, or its stream's <c>/W</c>, <c>/Index</c>, <c>/Size</c> and rows —, and no row
/// of it gives an object in use what no entry can hold.
/// </summary>
/// <remarks>
/// A section that is there and cannot be read, or that holds fewer rows than it declares, leaves objects out of the
/// index, to be found by scanning the file — the first section unreadable, the reader rebuilds the whole index —,
/// after which it cannot vouch that it reads what was written: an error (ADR 45). A trailer at fault is <see cref="PdfValidationRuleIds.FileTrailerMissing"/>'s or
/// <see cref="PdfValidationRuleIds.FileTrailerMalformed"/>'s, and a section one of the reader's limits cut is not
/// malformed (ADR 34): <see cref="PdfValidationRuleIds.XRefCheckedInPart"/> says it was not checked whole.
/// </remarks>
internal sealed class SectionMalformedRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.XRefSectionMalformed;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Error;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        foreach (var section in context.Structure.Sections)
        {
            if (section.CutByLimit)
            {
                continue;
            }

            var unreadable = section.State == XRefSectionState.Malformed && section.TrailerFault == XRefTrailerFault.None;
            // A section read whole is faulty for what it lacks, or else for the first row it refused; one that cannot be read
            // is so for what stopped it, which a refused row never is.
            var readFault = section.Fault ?? section.RefusedRow;
            var faulty = section.State is XRefSectionState.Read or XRefSectionState.Relocated && readFault is not null;

            if (!unreadable && !faulty)
            {
                continue;
            }

            var kind = section.Kind == XRefSectionKind.Stream ? "stream" : "table";
            var said = unreadable ? section.Fault : readFault;
            var fault = said is null ? string.Empty : ": " + said;

            context.Report(
                this,
                PdfValidationLocation.AtPosition(section.Offset),
                unreadable
                    ? string.Create(CultureInfo.InvariantCulture, $"The cross-reference {kind} at offset {section.Offset} cannot be read{fault}.")
                    : string.Create(CultureInfo.InvariantCulture, $"The cross-reference {kind} at offset {section.Offset} is malformed{fault}."),
                "Rewrite the cross-reference section; a rewrite of the whole file rebuilds it from the objects.");
        }
    }
}
