namespace AdCodicem.Pdf.Validation;

/// <summary>Controls how a <see cref="PdfValidator"/> validates documents.</summary>
public sealed record PdfValidatorOptions
{
    /// <summary>The options used when none are supplied.</summary>
    public static PdfValidatorOptions Default { get; } = new();

    /// <summary>Gets the profile documents are validated against. Defaults to <see cref="ValidationProfile.Structural"/>.</summary>
    public ValidationProfile Profile
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = ValidationProfile.Structural;

    /// <summary>
    /// Gets the largest number of findings a report keeps. A pathological file can have a fault in every
    /// object; beyond this number findings are counted, not kept, and the walks that find them do not write them, so that
    /// the validator holds no more findings than this whatever the file (#168). What the walks count of a file's faults,
    /// they count by object. Defaults to 1,000; zero keeps none and still counts them.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int FindingCapacity
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = 1000;
}
