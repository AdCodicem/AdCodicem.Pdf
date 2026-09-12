using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.IO.Filters;

namespace AdCodicem.Pdf.Objects;

/// <summary>Decoding of stream data.</summary>
public static class PdfStreamDecodingExtensions
{
    /// <summary>
    /// Applies the stream's filters and returns the decoded data.
    /// </summary>
    /// <param name="stream">The stream to decode.</param>
    /// <param name="diagnostics">Receives anomalies met while decoding, if supplied.</param>
    /// <remarks>
    /// Decoding stops at an image filter: a stream encoded with <c>DCTDecode</c> comes back as the JPEG it
    /// already is, rather than as pixels nobody asked for.
    /// </remarks>
    public static ReadOnlyMemory<byte> Decode(this PdfStream stream, PdfDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return PdfFilterPipeline.Decode(stream, diagnostics);
    }

    /// <summary>
    /// Determines whether the stream's data is an encoded image that <see cref="Decode"/> leaves alone.
    /// </summary>
    public static bool HasImageFilter(this PdfStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var filters = stream.Dictionary.GetRaw(PdfName.Filter).Resolved();

        switch (filters)
        {
            case PdfName single:
                return PdfFilterPipeline.IsImageFilter(single);

            case PdfArray chain:
                for (var index = 0; index < chain.Count; index++)
                {
                    if (chain.Resolved(index) is PdfName name && PdfFilterPipeline.IsImageFilter(name))
                    {
                        return true;
                    }
                }

                return false;

            default:
                return false;
        }
    }
}
