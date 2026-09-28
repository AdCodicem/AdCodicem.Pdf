namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.ObjectReferenceMissing"/>: every reference of an object reachable from the trailer
/// names an object the file holds.
/// </summary>
/// <remarks>
/// <para>
/// A reference to an object the file does not define — none in its index, or a free entry — is null (ISO 32000-1,
/// 7.3.10), which the reader reads as the specification says, rebuilding nothing: a warning (ADR 45). What the
/// reference was to give is lost — an image not drawn, an outline cut short —, and it most often marks damage or a
/// writer's mistake. One finding per object that holds such references, located at that object and at its page when
/// it is a page, naming the first of them.
/// </para>
/// <para>
/// An object the index holds in use and the reader could not produce is the index's fault, which
/// <see cref="PdfValidationRuleIds.XRefEntryBroken"/> or <see cref="PdfValidationRuleIds.XRefObjectStreamBroken"/>
/// reports, or its encryption's (M16); an object numbered past <c>/Size</c> that the file holds is
/// <see cref="PdfValidationRuleIds.XRefObjectPastSize"/>'s; a kid of the page tree that names nothing is
/// <see cref="PdfValidationRuleIds.PageTreeKidInvalid"/>'s. Objects nothing reachable refers to are not walked.
/// </para>
/// </remarks>
internal sealed class ReferenceMissingRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.ObjectReferenceMissing;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Warning;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        foreach (var finding in context.Graph.MissingReferences)
        {
            context.Report(this, finding.Location, finding.Message, "Remove the reference, or add the object it names.");
        }
    }
}
