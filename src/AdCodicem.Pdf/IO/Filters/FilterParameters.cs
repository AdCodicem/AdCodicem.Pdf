namespace AdCodicem.Pdf.IO.Filters;

/// <summary>
/// The values of a Flate or LZW step's parameters that decoding it reads, as plain values: what a step's parameters say
/// is read before any data is, and held, so that no step loads an object while data is decoded.
/// </summary>
internal readonly record struct FilterParameters
{
    /// <summary>Initializes the values a step decodes with when its parameters say nothing.</summary>
    public FilterParameters()
    {
    }

    /// <summary>Gets LZW's <c>/EarlyChange</c>: 0 when the parameters say 0, 1 otherwise.</summary>
    public int EarlyChange { get; init; } = 1;

    /// <summary>
    /// Gets the step's <c>/Predictor</c>: 1, no predictor, unless the parameters say otherwise; a value past an int is the
    /// int nearest it, as are <see cref="Colors"/> and <see cref="BitsPerComponent"/>.
    /// </summary>
    public int Predictor { get; init; } = 1;

    /// <summary>Gets the predictor's <c>/Colors</c>, read only when the predictor is past 1.</summary>
    public int Colors { get; init; } = 1;

    /// <summary>Gets the predictor's <c>/BitsPerComponent</c>, read only when the predictor is past 1.</summary>
    public int BitsPerComponent { get; init; } = 8;

    /// <summary>Gets the predictor's <c>/Columns</c>, at least 1, read only when the predictor is past 1.</summary>
    public int Columns { get; init; } = 1;
}
