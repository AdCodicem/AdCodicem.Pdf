using System.Globalization;
using AdCodicem.Pdf.IO;
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
/// as it was before the reader looked for a catalog. A trailer that could not be read at all is another rule's to report.
/// Where the chain gave no index, it judges the trailer the rebuild merged from those the file holds, and only where the
/// reader found no catalog at all: a document with none has no way in (#132). A catalog one of the reader's limits left out of the index
/// <see cref="PdfValidationRuleIds.XRefCheckedInPart"/>'s. A catalog one of them cut is not judged: what it lacks may lie
/// past the cut, and the reader's report names the limit. Nor is a <c>/Root</c> the trailers lack where one of them kept a
/// trailer from being read whole: it may lie past the cut, or in an older trailer the chain stopped short of.
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
        var reader = context.Document.Reader;

        if (!structure.ChainRead)
        {
            CheckRebuiltTrailer(context, reader);
            return;
        }

        if (!structure.TrailerRead || structure.RootUsable)
        {
            return;
        }

        // A catalog a guard left out of the index as the document opened is unknown, not lacking: xref.checked-in-part says
        // the index was stopped short of it, and raising the guard reads it. So is a /Root the trailers lack where a guard
        // kept one of them from being read whole: it may lie past the cut, or in an older trailer the chain stopped short
        // of. It stays so once a rebuild finds it, as the trailer judged is the one the document opened with, and /Root as
        // it resolved then.
        if (structure.RootHiddenByGuard)
        {
            return;
        }

        // Nor is a catalog a guard cut as the document opened: what it lacks may lie past the cut — its /Type, its /Pages —,
        // and the reader took it as read. The reader's report names the guard — limit.object, or limit.decoded-stream for a
        // catalog in an object stream, cut through or left unread past the cut data (#259) —, and raising it reads the
        // catalog whole.
        if (structure.RootCutByGuard)
        {
            return;
        }

        var trailer = structure.Sections.Count > 0
            ? PdfValidationLocation.AtPosition(structure.Sections[0].TrailerLocation)
            : default;
        ReportRoot(context, structure.RootAsWritten, structure.RootResolved, trailer, "The trailer has no /Root.", Found(context, reader));
    }

    /// <summary>
    /// Judges, where the chain gave no index, the trailer the rebuild merged from those the file holds, only where the reader
    /// found no catalog at all: a catalog it found, through that trailer's <c>/Root</c> or by searching, is the file's
    /// enough for the rebuild's own report, and the rules that judge what made the chain unreadable say why (#132). A
    /// <c>/Root</c> one of the reader's limits cut, or left out of the index, is taken as read.
    /// </summary>
    private void CheckRebuiltTrailer(ValidationContext context, PdfFileReader reader)
    {
        if (reader.HasCatalog || reader.CatalogFoundAs > 0 || reader.RootLeadsPastGuard)
        {
            return;
        }

        var root = context.Document.Trailer.GetRaw(PdfName.Root);
        var resolved = root is PdfReference reference ? context.Document.GetObject(reference.Id) : null;
        ReportRoot(context, root, resolved, default, "No trailer of the file gives /Root.", Found(context, reader));
    }

    /// <summary>
    /// Reports <paramref name="root"/>, the <c>/Root</c> judged, which <paramref name="resolved"/> is what it led to, and
    /// ends the message with <paramref name="found"/>.
    /// </summary>
    private void ReportRoot(
        ValidationContext context, PdfObject? root, PdfObject? resolved, PdfValidationLocation trailer, string none, string found)
    {
        // An entry whose value is null is one the dictionary does not have (ISO 32000-1, 7.3.7).
        switch (root)
        {
            case null or PdfNull:
                Report(context, trailer, none + found);
                break;

            case PdfReference reference:
                // Led to nothing through the chain's index, the object may be one a rebuild found since: the chain does not
                // place it, which is not the file's not holding it (#132).
                var what = resolved is null or PdfNull && context.Document.GetObject(reference.Id) is not PdfNull
                    ? "the cross-reference chain does not place"
                    : Describe(resolved);
                Report(
                    context,
                    PdfValidationLocation.OfObject(reference.Id),
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The trailer's /Root names object {reference.Id.Number} {reference.Id.Generation}, which {what}.{found}"));
                break;

            default:
                Report(context, trailer, $"The trailer's /Root is {Kind(root)}, not a reference to the document catalog.{found}");
                break;
        }
    }

    /// <summary>
    /// Gives the last sentence of a finding: what the reader knows of the catalog — the one it took, the one a rebuild made
    /// <c>/Root</c> lead to, or none among the objects it could read whole, a guard having cut some, or none at all (#260).
    /// </summary>
    /// <remarks>
    /// The reader's, not the structure's: a rebuild run after the document opened may have taken another.
    /// </remarks>
    private static string Found(ValidationContext context, PdfFileReader reader)
    {
        if (reader.CatalogFoundAs is > 0 and var catalog)
        {
            return string.Create(CultureInfo.InvariantCulture, $" The reader took object {catalog}, which is one, for the catalog.");
        }

        if (reader.HasCatalog)
        {
            return context.Document.Trailer.GetRaw(PdfName.Root) is PdfReference rebuilt
                ? string.Create(CultureInfo.InvariantCulture, $" The reader rebuilt the index, through which /Root leads to object {rebuilt.Id.Number}, which is one.")
                : " The reader rebuilt the index, whose trailer gives one.";
        }

        return reader.CatalogCandidateCut
            ? " No object the reader read whole is a catalog: one of the reader's limits cut some, which its report names."
            : " No object of the file is a catalog.";
    }

    private void Report(ValidationContext context, PdfValidationLocation location, string message) =>
        context.Report(this, location, message, "Point /Root at the document catalog, as an indirect reference.");

    private static string Describe(PdfObject? resolved) => resolved switch
    {
        null or PdfNull => "the file does not hold",
        PdfStream => "is a stream, not a document catalog",
        PdfDictionary dictionary when dictionary.GetName(PdfName.Type) is { } type =>
            $"is a dictionary of /Type {FileQuote.Name(type)}, not a document catalog",
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
