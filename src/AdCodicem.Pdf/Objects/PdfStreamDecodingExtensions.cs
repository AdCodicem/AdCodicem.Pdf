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
    /// Receives anomalies met while decoding: diagnostics of your own, each time the stream is decoded. A stream read from
    /// a document reports, when none are supplied, in the document's own <see cref="Documents.PdfDocument.Diagnostics"/>,
    /// which hold each report once, however often the stream is decoded, whether they are supplied or not; one built in
    /// memory reports nothing.
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
    /// <see cref="PdfDiagnosticCodes.FilterChecksumMismatch"/>, since some of it may be wrong. Flate data that nothing
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
    /// reported as damage. So do they data that runs past <see cref="Documents.PdfReaderLimits.MaxStreamLength"/>, of which
    /// the reader reads what lies within it, reported as <see cref="PdfDiagnosticCodes.LimitStreamLength"/>: once in the
    /// document's diagnostics, and in diagnostics of your own each time the stream is decoded. A <c>/Filter</c> array that
    /// names more filters than
    /// <see cref="Documents.PdfReaderLimits.MaxFilterChainLength"/> ends decoding before the first past them, reported as
    /// <see cref="PdfDiagnosticCodes.LimitFilterChain"/> when decoding reaches it.
    /// </para>
    /// <para>
    /// The filters and the values of their parameters are read before any of the data is: a parameter held in an
    /// object stream decodes that stream first. The parameters of a filter are the element at its place of a
    /// <c>/DecodeParms</c> array, the first for a single filter, or a <c>/DecodeParms</c> dictionary for every filter.
    /// </para>
    /// </remarks>
    /// <exception cref="PdfLimitExceededException">
    /// Decoding reaches one of its document's guards — the encoded data runs past
    /// <see cref="Documents.PdfReaderLimits.MaxStreamLength"/>, the data decodes past
    /// <see cref="Documents.PdfReaderLimits.MaxDecodedStreamLength"/>, or decoding reaches the filter past
    /// <see cref="Documents.PdfReaderLimits.MaxFilterChainLength"/> —, and the document was opened with
    /// <see cref="Documents.PdfReaderOptions.ThrowOnLimit"/>.
    /// </exception>
    public static ReadOnlyMemory<byte> Decode(this PdfStream stream, PdfDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return PdfFilterPipeline.Decode(stream, diagnostics);
    }

    /// <summary>
    /// Determines whether the stream's data is an encoded image that <see cref="Decode"/> leaves alone: whether its
    /// <c>/Filter</c> entry names an image filter before anything that ends decoding whatever the data holds — a filter
    /// the library does not know, an element that names no filter, a filter past
    /// <see cref="Documents.PdfReaderLimits.MaxFilterChainLength"/>, a filter's value or parameter that leads to an object
    /// one of the reader's guards cut or left out of the index.
    /// </summary>
    /// <remarks>
    /// None of the data is read: whether a filter before the image filter fails on it, or whether the image is sound,
    /// takes decoding it, and <see cref="Decode"/> reports what it meets. The filters' parameters are read as decoding reads
    /// them: one held in an object stream decodes that stream first.
    /// </remarks>
    public static bool HasImageFilter(this PdfStream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return FilterChain.EndOf(stream.Dictionary, stream.Data.LimitGuard.Bound(IO.PdfLimit.FilterChain)) == FilterChainEnd.Image;
    }
}
