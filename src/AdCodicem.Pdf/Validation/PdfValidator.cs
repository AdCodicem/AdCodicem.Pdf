using AdCodicem.Pdf.Documents;

namespace AdCodicem.Pdf.Validation;

/// <summary>
/// Validates documents against a profile, and says what is wrong with them — separately from whether they
/// could be read at all.
/// </summary>
/// <remarks>
/// <para>
/// A validator holds no state beyond its options, so one instance serves any number of threads and can be
/// registered as a singleton. A <see cref="PdfDocument"/>, on the other hand, belongs to one thread at a time,
/// validation included.
/// </para>
/// <para>
/// Validation reads through the document like any other caller, lazily and within the document's reader
/// limits: what it resolves joins the document's cache, and what the reader notices on the way joins
/// <see cref="PdfDocument.Diagnostics"/>. Opening the document with raised limits lets it check more.
/// </para>
/// </remarks>
public sealed class PdfValidator
{
    /// <summary>Initialises a validator.</summary>
    /// <param name="options">How to validate; <see cref="PdfValidatorOptions.Default"/> when null.</param>
    public PdfValidator(PdfValidatorOptions? options = null) => Options = options ?? PdfValidatorOptions.Default;

    /// <summary>Gets how this validator validates.</summary>
    public PdfValidatorOptions Options { get; }

    /// <summary>Validates a document against the profile of <see cref="Options"/>.</summary>
    /// <param name="document">A document the caller opened, and keeps: it is left open.</param>
    /// <returns>What the profile found, which is empty for a sound document.</returns>
    /// <remarks>
    /// A document the reader could open is reported on, never refused: what is wrong with it is what the report
    /// is for.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
    /// <exception cref="ObjectDisposedException"><paramref name="document"/> was disposed.</exception>
    /// <exception cref="Diagnostics.PdfLimitExceededException">
    /// Validation reached one of the document's reader limits, and the document was opened with
    /// <see cref="PdfReaderOptions.ThrowOnLimit"/>. Without it, a rule reports at most that what the reader cut
    /// was not checked whole.
    /// </exception>
    public PdfValidationReport Validate(PdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.ThrowIfDisposed();

        var profile = Options.Profile;
        var context = new ValidationContext(document, Options.FindingCapacity);

        for (var index = 0; index < profile.Rules.Count; index++)
        {
            profile.Rules[index].Check(context);
        }

        return context.ToReport(profile);
    }
}
