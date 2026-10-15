using System.Globalization;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO.Filters;

/// <summary>Applies the chain of filters declared by a stream.</summary>
internal static class PdfFilterPipeline
{
    /// <summary>
    /// Decodes a stream's data, stopping at an image filter, at a filter it does not know, and past the most filters the
    /// reader's guard lets data pass through.
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
    /// Decoding reaches one of its document's guards, and the document was opened to throw when it does.
    /// </exception>
    public static ReadOnlyMemory<byte> Decode(PdfStream stream, PdfDiagnostics? diagnostics) =>
        Decode(stream, diagnostics, stream.Data.LimitGuard);

    /// <summary>Decodes a stream's data under the guards given, rather than those of its document.</summary>
    /// <param name="stream">The stream to decode.</param>
    /// <param name="diagnostics">
    /// Receives what decoding met; when none are supplied, the guard's document does, so that a stream read
    /// from a document is never decoded in silence.
    /// </param>
    /// <param name="guard">Bounds what each filter's output may be and how many filters run, and says what reaching either does.</param>
    public static ReadOnlyMemory<byte> Decode(PdfStream stream, PdfDiagnostics? diagnostics, PdfLimitGuard guard)
    {
        diagnostics ??= guard.DocumentDiagnostics;

        // Every value the filters are decoded with is resolved before any of the data is read: one an object stream holds
        // decodes that stream while this one holds nothing decoded (#165).
        var chain = FilterChain.Resolve(stream.Dictionary, guard.Bound(PdfLimit.FilterChain));
        var data = stream.GetRawBytes();

        // Whether the data a filter is given is data the reader cut at one of its limits: the parser's window cut the
        // stream, or a filter before it stopped at MaxDecodedStreamLength. Every filter after a cut is given cut data,
        // and the cut is reported once, by the guard that made it.
        var cut = stream.Data.CutByGuard;
        var limitReported = false;

        // The filters before the last share one bound for what they pass along together, and the last keeps its own: a
        // stream of one or two filters decodes as each filter alone would, and a chain however long holds, and fills, no
        // more than twice the bound (#166). A /Crypt filter is no step: it passes its data along as it is, holds nothing
        // more, and is never the last. Under a bound the reader cannot raise, the filters share none either.
        var bound = guard.Bound(PdfLimit.DecodedStream);
        var shared = bound >= Array.MaxLength ? long.MaxValue : bound;

        foreach (var step in chain)
        {
            // Every filter decodes nothing to nothing, and says nothing of it: the filters left would only cost their
            // number, which the file chooses.
            if (data.IsEmpty)
            {
                break;
            }

            var last = step.Index == chain.LastIndex;
            var maxLength = last ? bound : (int)Math.Clamp(shared, 0, bound);
            data = ApplyOne(
                step,
                data,
                new Bounds(maxLength, Shared: maxLength < bound),
                diagnostics,
                stream.Data.Position,
                guard,
                ref cut,
                ref limitReported,
                out var failed);

            // Data a filter could not decode at all comes back as the filters before it left it, as it does at a filter the
            // library does not know: the filters after it would read what it encoded, and each fail on it again.
            if (failed)
            {
                return data;
            }

            shared -= data.Length;
        }

        ReportEnd(chain, diagnostics, stream.Data.Position, guard);
        return data;
    }

    /// <summary>
    /// Determines whether a filter produces an image format that should be left encoded.
    /// </summary>
    public static bool IsImageFilter(PdfName name) =>
        name == PdfName.DCTDecode || name == PdfName.JPXDecode ||
        name == PdfName.JBIG2Decode || name == PdfName.CCITTFaxDecode;

    /// <summary>Decodes the data one filter of the chain is given.</summary>
    /// <param name="step">The filter, and the values of its parameters.</param>
    /// <param name="data">What the filters before it decoded.</param>
    /// <param name="bounds">The most the filter's output may hold, and whether it is what the filters before the last share.</param>
    /// <param name="diagnostics">Receives what decoding met.</param>
    /// <param name="position">Where the stream's data starts, where every report is placed.</param>
    /// <param name="guard">Says what reaching the bound does.</param>
    /// <param name="cut">
    /// Whether <paramref name="data"/> is data the reader cut; set when this filter stops at the bound too.
    /// </param>
    /// <param name="limitReported">Whether the stream reported a filter that stopped at the bound already.</param>
    /// <param name="failed">Whether the filter could decode nothing, which ends the chain.</param>
    private static ReadOnlyMemory<byte> ApplyOne(
        in FilterStep step,
        ReadOnlyMemory<byte> data,
        Bounds bounds,
        PdfDiagnostics? diagnostics,
        long position,
        PdfLimitGuard guard,
        ref bool cut,
        ref bool limitReported,
        out bool failed)
    {
        var name = step.Name;
        var maxLength = bounds.MaxLength;
        var given = cut;
        failed = false;

        if (name == PdfName.FlateDecode)
        {
            if (!FlateFilter.TryDecode(data, out var decoded, out var repaired, out var ending, out var faultAt, out var limited, maxLength))
            {
                diagnostics?.Warn(PdfDiagnosticCodes.FilterFailed, "A Flate stream could not be decoded.", position);
                failed = true;
                return data;
            }

            if (repaired)
            {
                diagnostics?.Repair(PdfDiagnosticCodes.FilterFailed, "A Flate stream was not valid zlib data.", position);
            }

            // Data a guard of the reader's cut ends early because the reader stopped reading it, or a filter before
            // this one stopped decoding it, which the guard reported: that it ran out says nothing of the file. A fault,
            // or a checksum that disagrees, lies in bytes the reader did read, and the whole data would meet it at the
            // same point: it is the file's.
            ReportEnding(
                given && ending is FlateEnding.TailLost or FlateEnding.ChecksumMissing ? FlateEnding.Whole : ending,
                decoded.Length,
                faultAt,
                data.Length,
                diagnostics,
                position);
            Reached(limited, name, bounds.Shared, diagnostics, position, guard, ref cut, ref limitReported);
            return ApplyPredictor(decoded, step, diagnostics, position, cut);
        }

        if (name == PdfName.LZWDecode)
        {
            var decoded = LzwFilter.Decode(data.Span, step.Parameters.EarlyChange, out var limited, out var undefinedCode, maxLength);

            if (undefinedCode >= 0)
            {
                diagnostics?.Warn(
                    PdfDiagnosticCodes.FilterFailed,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"An LZW stream uses code {undefinedCode}, which it has not defined; what decoded before it was kept."),
                    position);
            }

            Reached(limited, name, bounds.Shared, diagnostics, position, guard, ref cut, ref limitReported);
            return ApplyPredictor(decoded, step, diagnostics, position, cut);
        }

        if (name == PdfName.ASCII85Decode)
        {
            var decoded = Ascii85Filter.Decode(data.Span, out var limited, maxLength, given);
            Reached(limited, name, bounds.Shared, diagnostics, position, guard, ref cut, ref limitReported);
            return decoded;
        }

        if (name == PdfName.ASCIIHexDecode)
        {
            var decoded = AsciiHexFilter.Decode(data.Span, out var limited, maxLength, given);
            Reached(limited, name, bounds.Shared, diagnostics, position, guard, ref cut, ref limitReported);
            return decoded;
        }

        // The chain runs no other step (FilterChain.Steps).
        var runs = RunLengthFilter.Decode(data.Span, out var runsLimited, maxLength);
        Reached(runsLimited, name, bounds.Shared, diagnostics, position, guard, ref cut, ref limitReported);
        return runs;
    }

    /// <summary>Reports what ended the chain before its last element, when it is no image filter.</summary>
    /// <exception cref="PdfLimitExceededException">
    /// The guard ended the chain, and the document was opened to throw when it does.
    /// </exception>
    private static void ReportEnd(FilterChain chain, PdfDiagnostics? diagnostics, long position, PdfLimitGuard guard)
    {
        if (chain.End == FilterChainEnd.Limit)
        {
            guard.Reach(
                PdfLimit.FilterChain,
                diagnostics,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The /Filter entry names more than {chain.EndIndex:N0} filters; decoding stopped after the first {chain.EndIndex:N0}."),
                position);
            return;
        }

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
    /// Takes the data a filter that stopped at the bound gives the next as data the reader cut, and reports the first
    /// filter of the stream that did: a guard of the reader's, which the file may well be within its rights to exceed,
    /// and not damage in it.
    /// </summary>
    private static void Reached(
        bool limited, PdfName name, bool shared, PdfDiagnostics? diagnostics, long position, PdfLimitGuard guard, ref bool cut, ref bool limitReported)
    {
        if (!limited)
        {
            return;
        }

        cut = true;

        if (limitReported)
        {
            return;
        }

        limitReported = true;
        var bound = PdfLimitGuard.FormatLength(guard.Bound(PdfLimit.DecodedStream));
        guard.Reach(
            PdfLimit.DecodedStream,
            diagnostics,
            shared
                ? $"The filters before the last decode to more than {bound} together; decoding stopped in the {FileQuote.Name(name)} data."
                : $"The {FileQuote.Name(name)} data decodes to more than {bound}; decoding stopped there.",
            position);
    }

    /// <summary>The most a filter's output may hold, and whether it is what the filters before the last have left of their bound.</summary>
    /// <param name="MaxLength">The most the output may hold.</param>
    /// <param name="Shared">Whether the bound the filters before the last share holds it to less than the bound itself.</param>
    private readonly record struct Bounds(int MaxLength, bool Shared);

    private static ReadOnlyMemory<byte> ApplyPredictor(
        byte[] decoded, in FilterStep step, PdfDiagnostics? diagnostics, long position, bool cut)
    {
        var parameters = step.Parameters;

        if (parameters.Predictor <= 1)
        {
            return decoded;
        }

        if (!PredictorTransform.TryApply(
                decoded, parameters.Predictor, parameters.Colors, parameters.BitsPerComponent, parameters.Columns, cut, out var result))
        {
            diagnostics?.Warn(
                PdfDiagnosticCodes.FilterFailed,
                "The predictor's parameters describe rows longer than the decoded data; the data was left as decoded.",
                position);
        }

        return result;
    }
}
