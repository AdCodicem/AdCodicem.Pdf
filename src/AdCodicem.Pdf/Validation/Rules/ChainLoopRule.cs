using System.Globalization;

namespace AdCodicem.Pdf.Validation.Rules;

/// <summary>
/// <see cref="PdfValidationRuleIds.XRefChainLoop"/>: the chain of cross-reference sections ends, each <c>/Prev</c>
/// naming an older section.
/// </summary>
/// <remarks>
/// A <c>/Prev</c> that names a section the chain has already read makes a loop a careless reader never leaves. The
/// reader stops there, but the section the chain should have gone on to is unknown: what only it indexed is found,
/// if at all, by rebuilding the index, after which the reader cannot vouch that it reads what was written — an
/// error (ADR 45), as a section that cannot be found is.
/// </remarks>
internal sealed class ChainLoopRule : IValidationRule
{
    /// <inheritdoc/>
    public string Id => PdfValidationRuleIds.XRefChainLoop;

    /// <inheritdoc/>
    public PdfValidationSeverity Severity => PdfValidationSeverity.Error;

    /// <inheritdoc/>
    public void Check(ValidationContext context)
    {
        var structure = context.Structure;

        if (structure.LoopOffset < 0)
        {
            return;
        }

        context.Report(
            this,
            structure.LoopNamedFrom >= 0 ? PdfValidationLocation.AtPosition(structure.LoopNamedFrom) : default,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The {structure.LoopNamedBy} of the cross-reference section at offset {structure.LoopNamedFrom} names offset {structure.LoopOffset}, a section the chain has already read: the chain loops."),
            "Point the /Prev at the section before it, or remove it from the oldest section.");
    }
}
