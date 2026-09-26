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
    /// <param name="diagnostics">
    /// Receives anomalies met while decoding. When none are supplied, a stream read from a document reports
    /// them in the document's own <see cref="Documents.PdfDocument.Diagnostics"/>, and one built in memory
    /// reports nothing.
    /// </param>
    /// <remarks>
    /// <para>
    /// Decoding stops at an image filter: a stream encoded with <c>DCTDecode</c> comes back as the JPEG it
    /// already is, rather than as pixels nobody asked for.
    /// </para>
    /// <para>
    /// Data that is damaged decodes as far as it can, and what decoded is kept: a Flate stream that lost its
    /// tail, or an LZW stream that uses a code it has not defined, comes back as what came before, reported as
    /// <see cref="PdfDiagnosticCodes.FilterFailed"/>. Data that nothing could decode comes back encoded,
    /// reported under the same code.
    /// </para>
    /// <para>
    /// A stream read from a document decodes under that document's
    /// <see cref="Documents.PdfReaderOptions.Limits"/>; one built in memory, under
    /// <see cref="Documents.PdfReaderLimits.Default"/>. Data that decodes past the bound is kept up to it and
    /// reported as <see cref="PdfDiagnosticCodes.LimitDecodedStream"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="PdfLimitExceededException">
    /// The data decodes past its document's bound, and the document was opened with
    /// <see cref="Documents.PdfReaderOptions.ThrowOnLimit"/>.
    /// </exception>
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
