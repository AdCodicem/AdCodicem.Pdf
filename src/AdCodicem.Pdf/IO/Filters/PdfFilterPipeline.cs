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
    /// <param name="diagnostics">Receives what decoding met, if supplied.</param>
    /// <exception cref="PdfLimitExceededException">
    /// The data decodes past its document's bound, and the document was opened to throw when it does.
    /// </exception>
    public static ReadOnlyMemory<byte> Decode(PdfStream stream, PdfDiagnostics? diagnostics) =>
        Decode(stream, diagnostics, stream.Data.LimitGuard);

    /// <summary>Decodes a stream's data under the guards given, rather than those of its document.</summary>
    /// <param name="stream">The stream to decode.</param>
    /// <param name="diagnostics">Receives what decoding met, if supplied.</param>
    /// <param name="guard">Bounds what each filter's output may be, and says what reaching it does.</param>
    public static ReadOnlyMemory<byte> Decode(PdfStream stream, PdfDiagnostics? diagnostics, PdfLimitGuard guard)
    {
        var data = stream.GetRawBytes();
        var filters = stream.Dictionary.GetRaw(PdfName.Filter).Resolved();

        if (filters is null)
        {
            return data;
        }

        var parameters = stream.Dictionary.GetRaw(PdfName.DecodeParms).Resolved();

        var position = stream.Data.Position;

        if (filters is PdfName single)
        {
            return ApplyOne(single, data, parameters.AsDictionary(), diagnostics, position, guard);
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

            data = ApplyOne(name, data, stepParameters, diagnostics, position, guard);
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
        long position,
        PdfLimitGuard guard)
    {
        if (IsImageFilter(name))
        {
            return data;
        }

        var maxLength = guard.Bound(PdfLimit.DecodedStream);

        if (name == PdfName.FlateDecode)
        {
            if (!FlateFilter.TryDecode(data, out var decoded, out var repaired, out var truncated, out var limited, maxLength))
            {
                diagnostics?.Warn(PdfDiagnosticCodes.FilterFailed, "A Flate stream could not be decoded.", position);
                return data;
            }

            if (repaired)
            {
                diagnostics?.Repair(PdfDiagnosticCodes.FilterFailed, "A Flate stream was not valid zlib data.", position);
            }

            if (truncated)
            {
                diagnostics?.Warn(
                    PdfDiagnosticCodes.FilterFailed,
                    "A Flate stream was truncated; the decoded prefix was kept.",
                    position);
            }

            ReportLimit(limited, name, diagnostics, position, guard);
            return ApplyPredictor(decoded, parameters, diagnostics, position);
        }

        if (name == PdfName.LZWDecode)
        {
            var earlyChange = (int)(parameters.GetInteger(PdfName.EarlyChange) ?? 1);
            var decoded = LzwFilter.Decode(data.Span, earlyChange is 0 ? 0 : 1, out var limited, maxLength);
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
            // The identity crypt filter is a no-op; anything else needs the security handler (M9).
            return data;
        }

        diagnostics?.Warn(
            PdfDiagnosticCodes.FilterUnsupported, $"The filter /{name.Value} is not supported.", position);
        return data;
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
