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
    /// tail or turned corrupt, or an LZW stream that uses a code it has not defined, comes back as what came
    /// before, reported as <see cref="PdfDiagnosticCodes.FilterFailed"/>. A Flate stream that decodes to its end
    /// but whose zlib checksum disagrees comes back whole, reported as
    /// <see cref="PdfDiagnosticCodes.FilterChecksumMismatch"/>, since some of it may be wrong. Data that nothing
    /// could decode comes back as the filters before it left it — encoded, when it is the first —, reported as
    /// <see cref="PdfDiagnosticCodes.FilterFailed"/>, and the filters after it are not tried. Decoding stops
    /// at a filter the library does not recognize: the data comes back as the filters before it decoded it, reported
    /// as <see cref="PdfDiagnosticCodes.FilterUnsupported"/>, and the filters after it are not tried on data they
    /// could not read. An element of the <c>/Filter</c> array that names no filter ends decoding the same way, and a
    /// <c>/Filter</c> entry that is neither a name nor an array leaves the data as it found it, reported the same way.
    /// A <c>/Filter</c> that is null is one the stream does not have.
    /// </para>
    /// <para>
    /// A stream read from a document decodes under that document's
    /// <see cref="Documents.PdfReaderOptions.Limits"/>; one built in memory, under
    /// <see cref="Documents.PdfReaderLimits.Default"/>. Data that decodes past the bound is kept up to it and
    /// reported once as <see cref="PdfDiagnosticCodes.LimitDecodedStream"/>; the filters before the last share the
    /// bound for what they pass along together (<see cref="Documents.PdfReaderLimits.MaxDecodedStreamLength"/>). The
    /// filters after one the bound stopped decode what it kept, as data the reader cut: that it runs out is not
    /// reported as damage.
    /// </para>
    /// <para>
    /// The filters and the values of their parameters are read before any of the data is: a parameter held in an
    /// object stream decodes that stream first. The parameters of a filter are the element at its place of a
    /// <c>/DecodeParms</c> array, the first for a single filter, or a <c>/DecodeParms</c> dictionary for every filter.
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
    /// Determines whether the stream's data is an encoded image that <see cref="Decode"/> leaves alone: whether its
    /// filters, decoded in order, reach an image filter before anything else ends them.
    /// </summary>
    public static bool HasImageFilter(this PdfStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return FilterChain.EndOf(stream.Dictionary) == FilterChainEnd.Image;
    }
}
