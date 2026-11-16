using System.Globalization;
using AdCodicem.Pdf.IO.XRef;

namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.XRefChainEmpty"/>: the chain of cross-reference sections gives the index a row — at
/// least the free head of the list ISO 32000-1 makes a file's table hold (7.5.4).
/// </summary>
/// <remarks>
/// <para>
/// A chain each of whose sections reads whole and indexes nothing — a cross-reference stream of <c>/Size 0</c>, or whose
/// <c>/Index</c> numbers no object, a table of one subsection <c>0 0</c>, or of none — leaves the reader no object to read:
/// it rebuilds the index by scanning the file, and cannot vouch that it then reads what was written: an error (ADR 45).
/// qpdf finds no <c>/Root</c> dictionary there. The fault is the chain's, not a section's: an update that indexes nothing
/// over a section that indexes the file's objects is lawful.
/// </para>
/// <para>
/// A chain that lost a section, read one only in part, looped, or that one of the reader's limits stopped, gave no row for
/// a fault another rule names — <see cref="PdfValidationRuleIds.XRefSectionMalformed"/>,
/// <see cref="PdfValidationRuleIds.XRefSectionNotFound"/>, <see cref="PdfValidationRuleIds.XRefChainLoop"/>,
/// <see cref="PdfValidationRuleIds.XRefCheckedInPart"/> among them — and is not this rule's.
/// </para>
/// </remarks>
internal sealed class ChainEmptyRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.XRefChainEmpty;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Error;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        var structure = context.Structure;

        if (!structure.ChainEmpty || !context.HasRoomFor(this))
        {
            return;
        }

        var first = structure.Sections[0];
        var kind = first.Kind == XRefSectionKind.Stream ? "stream" : "table";

        // The sections read, not the namings: a hybrid file's stream a later table names again counts once (#156).
        var count = 0;

        foreach (var section in structure.Sections)
        {
            count += section.State is XRefSectionState.Read or XRefSectionState.Relocated ? 1 : 0;
        }
        var what = count == 1
            ? string.Create(CultureInfo.InvariantCulture, $"The cross-reference {kind} startxref names at offset {first.Offset} indexes no object")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"None of the {count} cross-reference sections of the chain startxref names at offset {first.Offset} indexes an object");

        context.Report(
            this,
            PdfValidationLocation.AtPosition(first.Offset),
            what + ", not even object 0, the head of the list of free objects: the index was rebuilt by scanning the file.",
            "Write a cross-reference section that indexes the file's objects: object 0, free, and each object in use.");
    }
}
