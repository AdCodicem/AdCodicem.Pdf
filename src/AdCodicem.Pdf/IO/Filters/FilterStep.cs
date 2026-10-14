using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO.Filters;

/// <summary>
/// A filter of a chain, and the values of its parameters that decoding it reads, as plain values: what a step's
/// parameters say is read before any data is, and held, so that no step loads an object while data is decoded.
/// </summary>
/// <param name="Name">The filter.</param>
internal readonly record struct FilterStep(PdfName Name)
{
    /// <summary>Gets LZW's <c>/EarlyChange</c>: 1 unless the parameters say otherwise.</summary>
    public int EarlyChange { get; init; } = 1;

    /// <summary>Gets a Flate or LZW step's <c>/Predictor</c>: 1, no predictor, unless the parameters say otherwise.</summary>
    public int Predictor { get; init; } = 1;

    /// <summary>Gets the predictor's <c>/Colors</c>, read only when the predictor is past 1.</summary>
    public int Colors { get; init; } = 1;

    /// <summary>Gets the predictor's <c>/BitsPerComponent</c>, read only when the predictor is past 1.</summary>
    public int BitsPerComponent { get; init; } = 8;

    /// <summary>Gets the predictor's <c>/Columns</c>, at least 1, read only when the predictor is past 1.</summary>
    public int Columns { get; init; } = 1;
}
