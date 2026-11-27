using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.FileRootDirect"/>: the trailer's <c>/Root</c> is an indirect reference, not the catalog
/// written in the trailer (ISO 32000-1, Table 15).
/// </summary>
/// <remarks>
/// The reader takes a direct dictionary of <c>/Type /Catalog</c>, or one holding <c>/Pages</c>, as the catalog it is: the
/// file reads as written, and <see cref="PdfValidationRuleIds.FileRootInvalid"/>, whose error is the reader choosing a
/// catalog the file does not name, does not apply. A warning (ADR 45): an object no reference names has no number for an
/// incremental update to replace it under, nor for a signature or a structure to name it by. A direct dictionary that is no
/// catalog is <see cref="PdfValidationRuleIds.FileRootInvalid"/>'s. It judges the trailer
/// <see cref="PdfValidationRuleIds.FileRootInvalid"/> judges: the one the chain gave, as written; where the chain gave no
/// index, the one the rebuild merged.
/// </remarks>
internal sealed class RootDirectRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.FileRootDirect;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Warning;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        var structure = context.Structure;
        var reader = context.Document.Reader;
        PdfValidationLocation trailer;

        if (structure.ChainRead)
        {
            // /Root as the chain's trailer wrote it, which the reader took for the catalog as the document opened.
            if (!structure.TrailerRead || !structure.RootUsable || structure.RootAsWritten is not PdfDictionary)
            {
                return;
            }

            trailer = structure.Sections.Count > 0 ? PdfValidationLocation.AtPosition(structure.Sections[0].TrailerLocation) : default;
        }
        else
        {
            // The rebuild's trailer: a catalog its search took is written as a reference, so a direct one is the trailer's.
            if (!reader.HasCatalog || context.Document.Trailer.GetRaw(PdfName.Root) is not PdfDictionary)
            {
                return;
            }

            trailer = reader.TrailerLocation >= 0 ? PdfValidationLocation.AtPosition(reader.TrailerLocation) : default;
        }

        if (context.HasRoomFor(this))
        {
            context.Report(
                this,
                trailer,
                "The trailer's /Root is the document catalog written in the trailer, not an indirect reference to it.",
                "Write the catalog as an indirect object, and point /Root at it.");
        }
    }
}
