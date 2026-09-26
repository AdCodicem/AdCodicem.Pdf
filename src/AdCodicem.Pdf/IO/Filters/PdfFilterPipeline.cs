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
    /// <param name="maxLength">The most each filter's output may be; tests lower it.</param>
    public static ReadOnlyMemory<byte> Decode(
        PdfStream stream, PdfDiagnostics? diagnostics, int maxLength = PdfFilterLimits.MaxDecodedLength)
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
            return ApplyOne(single, data, parameters.AsDictionary(), diagnostics, position, maxLength);
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

            data = ApplyOne(name, data, stepParameters, diagnostics, position, maxLength);
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
        int maxLength)
    {
        if (IsImageFilter(name))
        {
            return data;
        }

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

            ReportLimit(limited, name, diagnostics, position, maxLength);
            return ApplyPredictor(decoded, parameters);
        }

        if (name == PdfName.LZWDecode)
        {
            var earlyChange = (int)(parameters.GetInteger(PdfName.EarlyChange) ?? 1);
            var decoded = LzwFilter.Decode(data.Span, earlyChange is 0 ? 0 : 1, out var limited, maxLength);
            ReportLimit(limited, name, diagnostics, position, maxLength);
            return ApplyPredictor(decoded, parameters);
        }

        if (name == PdfName.ASCII85Decode)
        {
            var decoded = Ascii85Filter.Decode(data.Span, out var limited, maxLength);
            ReportLimit(limited, name, diagnostics, position, maxLength);
            return decoded;
        }

        if (name == PdfName.ASCIIHexDecode)
        {
            var decoded = AsciiHexFilter.Decode(data.Span, out var limited, maxLength);
            ReportLimit(limited, name, diagnostics, position, maxLength);
            return decoded;
        }

        if (name == PdfName.RunLengthDecode)
        {
            var decoded = RunLengthFilter.Decode(data.Span, out var limited, maxLength);
            ReportLimit(limited, name, diagnostics, position, maxLength);
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
    /// Reports that a filter stopped at the bound: a limit of the reader's, which the file may well be
    /// within its rights to exceed, and not damage in it.
    /// </summary>
    private static void ReportLimit(bool limited, PdfName name, PdfDiagnostics? diagnostics, long position, int maxLength)
    {
        if (limited)
        {
            var bound = maxLength % (1024 * 1024) == 0 ? $"{maxLength / (1024 * 1024)} MB" : $"{maxLength} bytes";
            diagnostics?.Warn(
                PdfDiagnosticCodes.FilterLimitExceeded,
                $"The /{name.Value} data decodes to more than the {bound} the reader decodes; the first {bound} were kept.",
                position);
        }
    }

    private static ReadOnlyMemory<byte> ApplyPredictor(byte[] decoded, PdfDictionary? parameters)
    {
        if (parameters is null)
        {
            return decoded;
        }

        var predictor = (int)parameters.GetInteger(PdfName.Predictor, 1);

        return predictor <= 1
            ? decoded
            : PredictorTransform.Apply(
                decoded,
                predictor,
                (int)parameters.GetInteger(PdfName.Colors, 1),
                (int)parameters.GetInteger(PdfName.BitsPerComponent, 8),
                (int)parameters.GetInteger(PdfName.Columns, 1));
    }
}
