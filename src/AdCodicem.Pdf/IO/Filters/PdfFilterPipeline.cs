using System.Globalization;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO.Filters;

/// <summary>Applies the chain of filters declared by a stream.</summary>
internal static class PdfFilterPipeline
{
    /// <summary>
    /// Decodes a stream's data, stopping at an image filter.
    /// </summary>
    /// <remarks>
    /// Image filters are left in place on purpose: a JPEG inside a PDF is already a JPEG, and decoding it
    /// to pixels only to encode it again is both slow and lossy. Callers that want pixels ask for them.
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
        var data = stream.GetRawBytes();
        var filters = stream.Dictionary.GetRaw(PdfName.Filter).Resolved();

        if (filters is null)
        {
            return data;
        }

        var parameters = stream.Dictionary.GetRaw(PdfName.DecodeParms).Resolved();

        if (filters is PdfName single)
        {
            return ApplyOne(single, data, parameters.AsDictionary(), diagnostics, stream.Data, guard);
        }

        if (filters is not PdfArray chain)
        {
            diagnostics?.Warn(PdfDiagnosticCodes.FilterUnsupported, "The /Filter entry is neither a name nor an array.");
            return data;
        }

        var parameterArray = parameters as PdfArray;

        for (var index = 0; index < chain.Count; index++)
        {
            if (chain.Resolved(index) is not PdfName name)
            {
                continue;
            }

            var stepParameters = parameterArray is not null && index < parameterArray.Count
                ? parameterArray.Resolved(index).AsDictionary()
                : parameters.AsDictionary();

            if (IsImageFilter(name))
            {
                return data;
            }

            data = ApplyOne(name, data, stepParameters, diagnostics, stream.Data, guard);
        }

        return data;
    }

    /// <summary>
    /// Determines whether a filter produces an image format that should be left encoded.
    /// </summary>
    public static bool IsImageFilter(PdfName name) =>
        name == PdfName.DCTDecode || name == PdfName.JPXDecode ||
        name == PdfName.JBIG2Decode || name == PdfName.CCITTFaxDecode;

    private static ReadOnlyMemory<byte> ApplyOne(
        PdfName name,
        ReadOnlyMemory<byte> data,
        PdfDictionary? parameters,
        PdfDiagnostics? diagnostics,
        PdfStreamData source,
        PdfLimitGuard guard)
    {
        var position = source.Position;

        if (IsImageFilter(name))
        {
            return data;
        }

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
            return ApplyPredictor(decoded, parameters, diagnostics, position);
        }

        if (name == PdfName.LZWDecode)
        {
            var earlyChange = (int)(parameters.GetInteger(PdfName.EarlyChange) ?? 1);
            var decoded = LzwFilter.Decode(data.Span, earlyChange is 0 ? 0 : 1, out var limited, out var undefinedCode, maxLength);

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
            return ApplyPredictor(decoded, parameters, diagnostics, position);
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

        if (name == PdfName.Crypt)
        {
            // The identity crypt filter is a no-op; anything else needs the security handler (M16).
            return data;
        }

        diagnostics?.Warn(
            PdfDiagnosticCodes.FilterUnsupported, $"The filter /{name.Value} is not supported.", position);
        return data;
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
                $"The /{name.Value} data decodes to more than {bound}; decoding stopped there.",
                position);
        }
    }

    private static ReadOnlyMemory<byte> ApplyPredictor(
        byte[] decoded, PdfDictionary? parameters, PdfDiagnostics? diagnostics, long position)
    {
        if (parameters is null)
        {
            return decoded;
        }

        var predictor = (int)parameters.GetInteger(PdfName.Predictor, 1);

        // Without a predictor the other parameters mean nothing, and are not read: each can be a reference,
        // and resolving one can load, or even rebuild, what nothing asked for.
        if (predictor <= 1)
        {
            return decoded;
        }

        if (!PredictorTransform.TryApply(
                decoded,
                predictor,
                (int)parameters.GetInteger(PdfName.Colors, 1),
                (int)parameters.GetInteger(PdfName.BitsPerComponent, 8),
                (int)Math.Clamp(parameters.GetInteger(PdfName.Columns, 1), 1, int.MaxValue),
                out var result))
        {
            diagnostics?.Warn(
                PdfDiagnosticCodes.FilterFailed,
                "The predictor's parameters describe rows longer than the decoded data; the data was left as decoded.",
                position);
        }

        return result;
    }
}
