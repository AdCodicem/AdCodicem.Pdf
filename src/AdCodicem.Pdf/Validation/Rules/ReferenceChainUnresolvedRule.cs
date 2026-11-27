namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.ObjectReferenceChainUnresolved"/>: no reference of an object reachable from the trailer leads to
/// a chain of references that reaches no value.
/// </summary>
/// <remarks>
/// <para>
/// An indirect object's value is one of the eight basic types of object (ISO 32000-1, 7.3.1 and 7.3.10), which a reference
/// is not. The reader follows a chain of objects whose values are references all the same, through as many as a resolution
/// follows (32), and reads the value the chain leads to; one that loops back to an object it passed, or runs past them,
/// reads as null, the value the reference was to give lost: an error (ADR 45), which the reader reports too
/// (<see cref="Diagnostics.PdfDiagnosticCodes.ObjectReferenceChainCut"/>). A chain that reaches a value is read as that
/// value, and judged by it. One finding per object that holds such references, located at that object and at its page
/// when it is a page, naming the first of them; an object of the chain itself holds none.
/// </para>
/// <para>
/// A reference another rule judges — a kid of the page tree (<see cref="PdfValidationRuleIds.PageTreeKidInvalid"/>), the
/// trailer's <c>/Root</c> (<see cref="PdfValidationRuleIds.FileRootInvalid"/>) — is that rule's, which says the chain
/// loops or runs too long. Objects nothing reachable refers to are not walked.
/// </para>
/// </remarks>
internal sealed class ReferenceChainUnresolvedRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.ObjectReferenceChainUnresolved;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Error;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        var findings = context.Graph.CutChains;

        foreach (var finding in findings)
        {
            context.Report(this, finding.Location, finding.Message, "Write the value the chain leads to in the object the reference names.");
        }

        // What the analysis counted past the room the report had when it was made, which it did not write (#168).
        context.Count(this, findings.Unkept);
    }
}
