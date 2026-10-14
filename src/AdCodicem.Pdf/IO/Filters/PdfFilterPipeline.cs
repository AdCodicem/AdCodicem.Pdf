using System.Globalization;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO.Filters;

/// <summary>Applies the chain of filters declared by a stream.</summary>
internal static class PdfFilterPipeline
{
    /// <summary>
    /// Decodes a stream's data, stopping at an image filter, and at a filter it does not know.
    /// </summary>
    /// <remarks>
    /// Image filters are left in place on purpose: a JPEG inside a PDF is already a JPEG, and decoding it
    /// to pixels only to encode it again is both slow and lossy. Callers that want pixels ask for them. A filter
    /// the library does not know leaves the data as the filters before it decoded it: the filters after it would
    /// decode what it encoded, and meet a fault of their own, or none, in data they cannot read.
    /// </remarks>
    /// <param name="stream">The stream to decode.</param>
    /// <param name="diagnostics">Receives what decoding met; when none are supplied, the stream's document does.</param>
    /// <exception cref="PdfLimitExceededException">
    /// The data decodes past its document's bound, and the document was opened to throw when it does.
    /// </exception>
    public static ReadOnlyMemory<byte> Decode(PdfStream stream, PdfDiagnostics? diagnostics) =>
        Decode(stream, diagnostics, stream.Data.LimitGuard);

    /// <summary>Decodes a stream's data under the guards given, rather than those of its document.</summary>
    /// <param name="stream">The stream to decode.</param>
    /// <param name="diagnostics">
    /// Receives what decoding met; when none are supplied, the guard's document does, so that a stream read
    /// from a document is never decoded in silence.
    /// </param>
    /// <param name="guard">Bounds what each filter's output may be, and says what reaching it does.</param>
    public static ReadOnlyMemory<byte> Decode(PdfStream stream, PdfDiagnostics? diagnostics, PdfLimitGuard guard)
    {
        diagnostics ??= guard.DocumentDiagnostics;

        // Every value the filters are decoded with is resolved before any of the data is read: one an object stream holds
        // decodes that stream while this one holds nothing decoded (#165).
        var chain = FilterChain.Resolve(stream.Dictionary, readParameters: true);
        var data = stream.GetRawBytes();

        for (var index = 0; index < chain.Count; index++)
        {
            data = ApplyOne(chain[index], data, diagnostics, stream.Data, guard);
        }

        ReportEnd(chain, diagnostics, stream.Data.Position);
        return data;
    }

    /// <summary>
    /// Determines whether a filter produces an image format that should be left encoded.
    /// </summary>
    public static bool IsImageFilter(PdfName name) =>
        name == PdfName.DCTDecode || name == PdfName.JPXDecode ||
        name == PdfName.JBIG2Decode || name == PdfName.CCITTFaxDecode;

    private static ReadOnlyMemory<byte> ApplyOne(
        in FilterStep step,
        ReadOnlyMemory<byte> data,
        PdfDiagnostics? diagnostics,
        PdfStreamData source,
        PdfLimitGuard guard)
    {
        var name = step.Name;
        var position = source.Position;
        var maxLength = guard.Bound(PdfLimit.DecodedStream);

        if (name == PdfName.FlateDecode)
        {
            if (!FlateFilter.TryDecode(data, out var decoded, out var repaired, out var ending, out var faultAt, out var limited, maxLength))
            {
                diagnostics?.Warn(PdfDiagnosticCodes.FilterFailed, "A Flate stream could not be decoded.", position);
                return data;
            }

            if (repaired)
            {
                diagnostics?.Repair(PdfDiagnosticCodes.FilterFailed, "A Flate stream was not valid zlib data.", position);
            }

            // Data a guard of the reader's cut ends early because the reader stopped reading it, which the guard
            // reported: that it ran out says nothing of the file. A fault, or a checksum that disagrees, lies in
            // bytes the reader did read, and the whole data would meet it at the same point: it is the file's.
            ReportEnding(
                source.CutByGuard && ending is FlateEnding.TailLost or FlateEnding.ChecksumMissing ? FlateEnding.Whole : ending,
                decoded.Length,
                faultAt,
                data.Length,
                diagnostics,
                position);
            ReportLimit(limited, name, diagnostics, position, guard);
            return ApplyPredictor(decoded, step, diagnostics, position);
        }

        if (name == PdfName.LZWDecode)
        {
            var decoded = LzwFilter.Decode(data.Span, step.EarlyChange is 0 ? 0 : 1, out var limited, out var undefinedCode, maxLength);

            if (undefinedCode >= 0)
            {
                diagnostics?.Warn(
                    PdfDiagnosticCodes.FilterFailed,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"An LZW stream uses code {undefinedCode}, which it has not defined; what decoded before it was kept."),
                    position);
            }

            ReportLimit(limited, name, diagnostics, position, guard);
            return ApplyPredictor(decoded, step, diagnostics, position);
        }

        if (name == PdfName.ASCII85Decode)
        {
            var decoded = Ascii85Filter.Decode(data.Span, out var limited, maxLength);
            ReportLimit(limited, name, diagnostics, position, guard);
            return decoded;
        }

        if (name == PdfName.ASCIIHexDecode)
        {
            var decoded = AsciiHexFilter.Decode(data.Span, out var limited, maxLength);
            ReportLimit(limited, name, diagnostics, position, guard);
            return decoded;
        }

        if (name == PdfName.RunLengthDecode)
        {
            var decoded = RunLengthFilter.Decode(data.Span, out var limited, maxLength);
            ReportLimit(limited, name, diagnostics, position, guard);
            return decoded;
        }

        // The identity crypt filter is a no-op; anything else needs the security handler (M16).
        return data;
    }

    /// <summary>Reports what ended the chain before its last element, when it is no image filter.</summary>
    private static void ReportEnd(FilterChain chain, PdfDiagnostics? diagnostics, long position)
    {
        var message = chain.End switch
        {
            FilterChainEnd.Unsupported => $"The filter {FileQuote.Name((PdfName)chain.EndValue!)} is not supported; decoding stopped there.",
            FilterChainEnd.NotAName => string.Create(
                CultureInfo.InvariantCulture,
                $"Element {chain.EndIndex} of the /Filter entry is {PdfObjectParser.KindOf(chain.EndValue)}, which names no filter; decoding stopped there."),
            FilterChainEnd.NotAFilterEntry =>
                $"The /Filter entry is {PdfObjectParser.KindOf(chain.EndValue)}, neither a name nor an array; the data was left as it is.",
            _ => null,
        };

        if (message is not null)
        {
            diagnostics?.Warn(PdfDiagnosticCodes.FilterUnsupported, message, position);
        }
    }

    /// <summary>
    /// Reports a Flate stream whose data did not end where its format says it does, or whose checksum disagrees
    /// with it. Whatever decoded was kept; the report says whether any of the data was lost, and where.
    /// </summary>
    /// <param name="ending">How the data ended.</param>
    /// <param name="decoded">How many bytes were kept, before any predictor.</param>
    /// <param name="faultAt">For corrupt data, where in it, counted from 1, lies the byte the inflater met the fault in.</param>
    /// <param name="encoded">How many bytes of encoded data the filter was given.</param>
    /// <param name="diagnostics">Receives the report.</param>
    /// <param name="position">Where the stream's data starts.</param>
    private static void ReportEnding(
        FlateEnding ending, int decoded, int faultAt, int encoded, PdfDiagnostics? diagnostics, long position)
    {
        switch (ending)
        {
            case FlateEnding.ChecksumMissing:
                diagnostics?.Repair(
                    PdfDiagnosticCodes.FilterFailed,
                    "A Flate stream ends before its checksum does; its data decoded whole, unchecked.",
                    position);
                break;

            case FlateEnding.ChecksumMismatch:
                diagnostics?.Warn(
                    PdfDiagnosticCodes.FilterChecksumMismatch,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"A Flate stream's checksum disagrees with the {decoded} bytes its data decoded to; all were kept, and some may be wrong."),
                    position);
                break;

            case FlateEnding.TailLost:
                diagnostics?.Warn(
                    PdfDiagnosticCodes.FilterFailed,
                    "A Flate stream ends before its data does; what decoded before the end was kept.",
                    position);
                break;

            case FlateEnding.Corrupt:
                diagnostics?.Warn(
                    PdfDiagnosticCodes.FilterFailed,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"A Flate stream is corrupt at byte {faultAt} of its {encoded}; the {decoded} bytes decoded before the fault was found were kept."),
                    position);
                break;
        }
    }

    /// <summary>
    /// Reports that a filter stopped at the bound: a guard of the reader's, which the file may well be within
    /// its rights to exceed, and not damage in it.
    /// </summary>
    private static void ReportLimit(bool limited, PdfName name, PdfDiagnostics? diagnostics, long position, PdfLimitGuard guard)
    {
        if (limited)
        {
            var bound = PdfLimitGuard.FormatLength(guard.Bound(PdfLimit.DecodedStream));
            guard.Reach(
                PdfLimit.DecodedStream,
                diagnostics,
                $"The {FileQuote.Name(name)} data decodes to more than {bound}; decoding stopped there.",
                position);
        }
    }

    private static ReadOnlyMemory<byte> ApplyPredictor(
        byte[] decoded, in FilterStep step, PdfDiagnostics? diagnostics, long position)
    {
        if (step.Predictor <= 1)
        {
            return decoded;
        }

        if (!PredictorTransform.TryApply(decoded, step.Predictor, step.Colors, step.BitsPerComponent, step.Columns, out var result))
        {
            diagnostics?.Warn(
                PdfDiagnosticCodes.FilterFailed,
                "The predictor's parameters describe rows longer than the decoded data; the data was left as decoded.",
                position);
        }

        return result;
    }
}
