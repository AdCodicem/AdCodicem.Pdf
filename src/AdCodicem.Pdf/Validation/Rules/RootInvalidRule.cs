using System.Globalization;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.FileRootInvalid"/>: the trailer's <c>/Root</c> is a reference to the document
/// catalog.
/// </summary>
/// <remarks>
/// <para>
/// ISO 32000-1 (Table 15) requires it, as an indirect reference. Without it a reader has no way into the document:
/// qpdf finds no catalog, others look for an object typed <c>/Catalog</c> — which the reader does too, among the
/// objects the index holds before rebuilding anything. The catalog it finds is its choice, not the file's: an error
/// (ADR 45).
/// </para>
/// <para>
/// It judges the trailer the file's chain gave, merged across its sections as readers merge them, and <c>/Root</c>
/// as it was before the reader looked for a catalog. A trailer that could not be read at all, or an index the chain
/// did not give, is another rule's to report.
/// </para>
/// </remarks>
internal sealed class RootInvalidRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.FileRootInvalid;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Error;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        var structure = context.Structure;

        if (!structure.ChainRead || !structure.TrailerRead || structure.RootUsable)
        {
            return;
        }

        var found = structure.CatalogFoundAs > 0
            ? string.Create(CultureInfo.InvariantCulture, $" The reader took object {structure.CatalogFoundAs}, which is one, for the catalog.")
            : " No object of the file is a catalog.";
        var trailer = structure.Sections.Count > 0
            ? PdfValidationLocation.AtPosition(structure.Sections[0].TrailerLocation)
            : default;

        // An entry whose value is null is one the dictionary does not have (ISO 32000-1, 7.3.7).
        switch (structure.RootAsWritten)
        {
            case null or PdfNull:
                Report(context, trailer, "The trailer has no /Root." + found);
                break;

            case PdfReference reference:
                Report(
                    context,
                    PdfValidationLocation.OfObject(reference.Id),
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The trailer's /Root names object {reference.Id.Number} {reference.Id.Generation}, which {Describe(structure.RootResolved)}.{found}"));
                break;

            default:
                Report(
                    context,
                    trailer,
                    $"The trailer's /Root is {Kind(structure.RootAsWritten)}, not a reference to the document catalog.{found}");
                break;
        }
    }

    private void Report(ValidationContext context, PdfValidationLocation location, string message) =>
        context.Report(this, location, message, "Point /Root at the document catalog, as an indirect reference.");

    private static string Describe(PdfObject? resolved) => resolved switch
    {
        null or PdfNull => "the file does not hold",
        PdfStream => "is a stream, not a document catalog",
        PdfDictionary dictionary when dictionary.GetName(PdfName.Type) is { } type =>
            $"is a dictionary of /Type /{type.Value}, not a document catalog",
        PdfDictionary => "is a dictionary that is not a document catalog",
        _ => $"is {Kind(resolved)}, not a document catalog",
    };

    private static string Kind(PdfObject value) => value switch
    {
        PdfInteger or PdfReal => "a number",
        PdfName => "a name",
        PdfString => "a string",
        PdfArray => "an array",
        PdfDictionary => "a dictionary written in the trailer",
        PdfStream => "a stream written in the trailer",
        PdfBoolean => "a boolean",
        _ => "not an object",
    };
}
