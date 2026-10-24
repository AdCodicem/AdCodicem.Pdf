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
    public static ReadOnlyMemory<byte> Decode(PdfStream stream, PdfDiagnostics? diagnostics, PdfLimitGuard guard) =>
        Decode(stream, diagnostics, guard, endHidden: false, reachedBefore: false, out _, out _);

    /// <summary>
    /// Decodes a stream's data under its document's guards, and says whether one of them ended decoding — the most filters
    /// the reader lets data pass through, or a value the filters read that a guard cut or left out of the index: what comes
    /// back is then the data still encoded by the filters past it, no part of what the stream decodes to —, and whether one
    /// cut the data, as a reader of its rows or its objects must know. Data a proven <c>endstream</c> ended past white space
    /// that only a hidden length says is the data's or not (<see cref="PdfStreamData.EndHidden"/>) decodes as read whole,
    /// and a filter that runs out at its end says nothing of the file.
    /// </summary>
    /// <param name="stream">The stream to decode.</param>
    /// <param name="diagnostics">Receives what decoding met; when none are supplied, the stream's document does.</param>
    /// <param name="leftEncoded">
    /// Whether <see cref="Documents.PdfReaderLimits.MaxFilterChainLength"/> ended decoding, or a value the filters read that
    /// a guard cut or left out of the index did (#263).
    /// </param>
    /// <param name="cutByGuard">
    /// Whether the data decoded is cut by a guard: the stream's own data, which the reader read in part, or what a filter
    /// stopped at <see cref="Documents.PdfReaderLimits.MaxDecodedStreamLength"/>. A guard that loading a value the filters
    /// read reaches is that value's, and does not cut the data.
    /// </param>
    /// <exception cref="PdfLimitExceededException">
    /// Decoding reaches one of its document's guards, and the document was opened to throw when it does.
    /// </exception>
    public static ReadOnlyMemory<byte> Decode(PdfStream stream, PdfDiagnostics? diagnostics, out bool leftEncoded, out bool cutByGuard) =>
        Decode(stream, diagnostics, stream.Data.LimitGuard, endHidden: false, reachedBefore: false, out leftEncoded, out cutByGuard);

    /// <summary>
    /// Decodes a stream's data as <see cref="Decode(PdfStream, PdfDiagnostics?, out bool, out bool)"/> does, its end, with
    /// <paramref name="endHidden"/>, one a guard hides: a filter that runs out there says nothing of the file, as for data a
    /// guard cut, while a fault in what it read is the file's still.
    /// </summary>
    /// <param name="stream">The stream to decode.</param>
    /// <param name="diagnostics">Receives what decoding met; when none are supplied, the stream's document does.</param>
    /// <param name="endHidden">
    /// Whether where the data ends is the guard's to hide: data a search took up to an <c>endstream</c> nothing confirmed,
    /// where the length that would say is one a guard cut, or a guard on decoding kept the data from being taken again to
    /// it. The data decodes as data a guard cut does — a last group a filter holds in part, which the hidden end may have
    /// parted, is dropped, and a predictor's last row undone as far as it goes —, though it is not counted as cut by it: a
    /// group dropped so counts against <see cref="Documents.PdfReaderLimits.MaxDecodedStreamLength"/> all the same, and
    /// against what the filters before the last share, the whole data holding at least as much. A filter after it is given
    /// less, and may stay within the bound where the whole data would not: a decoding of data whose end is hidden that a
    /// decoding of it reached the bound before keeps that verdict, with <paramref name="reachedBefore"/>.
    /// </param>
    /// <param name="reachedBefore">
    /// Whether a decoding of the same data stopped at <see cref="Documents.PdfReaderLimits.MaxDecodedStreamLength"/> before,
    /// and reported it: the data comes back cut by the guard, and no filter reports it again.
    /// </param>
    /// <param name="leftEncoded">As the other overload gives it.</param>
    /// <param name="cutByGuard">As the other overload gives it.</param>
    /// <exception cref="PdfLimitExceededException">
    /// Decoding reaches one of its document's guards, and the document was opened to throw when it does.
    /// </exception>
    public static ReadOnlyMemory<byte> Decode(
        PdfStream stream, PdfDiagnostics? diagnostics, bool endHidden, bool reachedBefore, out bool leftEncoded, out bool cutByGuard) =>
        Decode(stream, diagnostics, stream.Data.LimitGuard, endHidden, reachedBefore, out leftEncoded, out cutByGuard);

    private static ReadOnlyMemory<byte> Decode(
        PdfStream stream,
        PdfDiagnostics? diagnostics,
        PdfLimitGuard guard,
        bool endHidden,
        bool reachedBefore,
        out bool leftEncoded,
        out bool cutByGuard)
    {
        diagnostics ??= guard.DocumentDiagnostics;

        // Every value the filters are decoded with is resolved before any of the data is read: one an object stream holds
        // decodes that stream while this one holds nothing decoded (#165).
        var chain = FilterChain.Resolve(stream.Dictionary, guard.Bound(PdfLimit.FilterChain));
        var data = stream.GetRawBytes();

        // The document's own diagnostics hold what decoding a stream of the document met once, however often the stream is
        // decoded: the reader decodes an object stream again once its budget let it go, a rebuild and the validator decode
        // it once more, and a caller who passes those diagnostics, or none, as often as it likes (#144). The decoding reports
        // apart, and what the document holds already is not moved in, so that a repeat takes no room there. Diagnostics a
        // caller passes of its own hold every decoding's reports. The values the filters read decoded before, each moving
        // its own reports in.
        if (diagnostics is null || !ReferenceEquals(diagnostics, guard.DocumentDiagnostics))
        {
            // Data MaxStreamLength cut is reported once in the document's diagnostics, as the reader reads it; diagnostics of
            // a caller's own receive it with each decoding, as they receive what decoding meets.
            if (diagnostics is not null && stream.Data.CutByGuard && stream.Data.Length > guard.Bound(PdfLimit.StreamLength))
            {
                guard.Reach(PdfLimit.StreamLength, diagnostics, guard.DescribeStreamLengthCut(), stream.Data.Position);
            }

            return DecodeChain(stream, chain, data, diagnostics, guard, endHidden, reachedBefore, out leftEncoded, out cutByGuard);
        }

        var decodings = diagnostics.Decodings;
        var mark = decodings.GetMark();

        try
        {
            return DecodeChain(stream, chain, data, decodings, guard, endHidden, reachedBefore, out leftEncoded, out cutByGuard);
        }
        finally
        {
            // Thrown at a guard or not, what the decoding met before is moved in.
            decodings.MoveDecodingReportsTo(diagnostics, mark);
        }
    }

    /// <summary>Decodes <paramref name="data"/> through the filters of <paramref name="chain"/>, reporting into <paramref name="diagnostics"/>.</summary>
    private static ReadOnlyMemory<byte> DecodeChain(
        PdfStream stream,
        FilterChain chain,
        ReadOnlyMemory<byte> data,
        PdfDiagnostics? diagnostics,
        PdfLimitGuard guard,
        bool endHidden,
        bool reachedBefore,
        out bool leftEncoded,
        out bool cutByGuard)
    {
        leftEncoded = false;

        // Whether the data a filter is given ends where the file's may not: data the reader cut at one of its limits —
        // MaxStreamLength, or the window a guard stopped at, cut the stream's data, or a filter before it stopped at
        // MaxDecodedStreamLength —, or data whose end the caller says a guard hides. Every filter after it is given such
        // data, and a cut is reported once, by the guard that made it; data whose end is hidden is no cut. A filter drops a
        // last group such data holds in part, and counts it against the bound all the same (Ascii85Filter, AsciiHexFilter),
        // and against what the filters before the last share.
        var cut = stream.Data.CutByGuard || endHidden;

        // Data whose endstream is proven its own past white space only a hidden length says is the data's or not ends where
        // the file's does but for that white space, which parts no ASCII85 or ASCIIHex group and falls in a Flate stream's
        // checksum: it decodes as it would read whole, and a filter that runs out there says nothing of the file.
        var silenced = stream.Data.EndHidden;

        // A guard a decoding of the same data reached before was reported then: its verdict stands, whatever this one meets.
        var limitReported = reachedBefore;

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
                silenced,
                ref limitReported,
                out var dropped,
                out var failed);

            // Data a filter could not decode at all comes back as the filters before it left it, as it does at a filter the
            // library does not know: the filters after it would read what it encoded, and each fail on it again.
            if (failed)
            {
                cutByGuard = stream.Data.CutByGuard || limitReported;
                return data;
            }

            // A group the filter dropped at the cut is data the whole would decode to, and leaves the filters after it no
            // more room than the whole data would.
            shared -= data.Length + dropped;
        }

        ReportEnd(chain, diagnostics, stream.Data.Position, guard);

        // A value a guard cut or left out of the index was reported where the guard was reached: the chain ends there in
        // silence.
        leftEncoded = chain.End is FilterChainEnd.Limit or FilterChainEnd.Guard;
        cutByGuard = stream.Data.CutByGuard || limitReported;
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
    /// <param name="silenced">
    /// Whether where the data ends says nothing of the file, though it is no cut: a filter that runs out there reports
    /// nothing, and decodes as it would read whole.
    /// </param>
    /// <param name="limitReported">Whether the stream reported a filter that stopped at the bound already.</param>
    /// <param name="dropped">How many bytes a last group the cut parted would decode to, which the filter did not write.</param>
    /// <param name="failed">Whether the filter could decode nothing, which ends the chain.</param>
    private static ReadOnlyMemory<byte> ApplyOne(
        in FilterStep step,
        ReadOnlyMemory<byte> data,
        Bounds bounds,
        PdfDiagnostics? diagnostics,
        long position,
        PdfLimitGuard guard,
        ref bool cut,
        bool silenced,
        ref bool limitReported,
        out int dropped,
        out bool failed)
    {
        var name = step.Name;
        var maxLength = bounds.MaxLength;
        var given = cut;
        var quiet = given || silenced;
        dropped = 0;
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
            // this one stopped decoding it, which the guard reported, and data whose end a guard hides ends where the guard
            // may: that it ran out says nothing of the file. A fault, or a checksum that disagrees, lies in bytes the reader
            // did read, and the whole data would meet it at the same point: it is the file's.
            ReportEnding(
                quiet && ending is FlateEnding.TailLost or FlateEnding.ChecksumMissing ? FlateEnding.Whole : ending,
                decoded.Length,
                faultAt,
                data.Length,
                diagnostics,
                position);
            Reached(limited, name, bounds.Shared, diagnostics, position, guard, ref cut, ref limitReported);

            // Data that ran out where a guard hides its end may have lost the end of its last row there: it is undone as far
            // as it goes, as of data a guard cut.
            return ApplyPredictor(decoded, step, diagnostics, position, cut || (silenced && ending == FlateEnding.TailLost));
        }

        if (name == PdfName.LZWDecode)
        {
            var decoded = LzwFilter.Decode(
                data.Span, step.Parameters.EarlyChange, out var limited, out var undefinedCode, out var ending, maxLength);

            if (undefinedCode >= 0)
            {
                diagnostics?.Warn(
                    PdfDiagnosticCodes.FilterFailed,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"An LZW stream uses code {undefinedCode}, which it has not defined; what decoded before it was kept."),
                    position);
            }
            else if (ending == DataEnding.Unmarked && !quiet)
            {
                // Data the reader cut ends early because the reader stopped reading it, and data whose end a guard hides
                // ends where the guard may: that no code ends it says nothing of the file.
                diagnostics?.Repair(
                    PdfDiagnosticCodes.FilterFailed,
                    "An LZW stream ends without its end-of-data code; it decoded to the end of its data.",
                    position);
            }
            else if (ending == DataEnding.UnmarkedInsideCode && !quiet)
            {
                diagnostics?.Warn(
                    PdfDiagnosticCodes.FilterFailed,
                    "An LZW stream ends without its end-of-data code, inside a code, whose bits were dropped; data may have been lost.",
                    position);
            }

            Reached(limited, name, bounds.Shared, diagnostics, position, guard, ref cut, ref limitReported);
            return ApplyPredictor(
                decoded, step, diagnostics, position, cut || (silenced && ending is DataEnding.Unmarked or DataEnding.UnmarkedInsideCode));
        }

        byte[] text;
        bool textLimited;
        DataFaults faults;

        if (name == PdfName.ASCII85Decode)
        {
            text = Ascii85Filter.Decode(data.Span, out textLimited, out faults, maxLength, given);
        }
        else if (name == PdfName.ASCIIHexDecode)
        {
            text = AsciiHexFilter.Decode(data.Span, out textLimited, out faults, maxLength, given);
        }
        else
        {
            // The chain runs no other step (FilterChain.Steps).
            text = RunLengthFilter.Decode(data.Span, out textLimited, out faults, maxLength);
        }

        // The end of data the reader cut, or whose end a guard hides, or of data the filter stopped decoding at the bound, is
        // the reader's: only what the filter read is the file's.
        ReportFaults(faults, name, data.Length, textLimited, ended: !quiet && !textLimited, diagnostics, position);
        Reached(textLimited, name, bounds.Shared, diagnostics, position, guard, ref cut, ref limitReported);
        dropped = faults.Dropped;
        return text;
    }

    /// <summary>
    /// Reports what an ASCII85, ASCIIHex or RunLength filter met that its format does not allow: a fault of the data the
    /// filter read once for each kind, at the byte where the first lies, and how the data ended when it ended without its
    /// end marker. Nothing lost is a repair, and data that may have been lost or decoded wrong a warning.
    /// </summary>
    /// <param name="faults">What the filter met.</param>
    /// <param name="name">The filter.</param>
    /// <param name="encoded">How many bytes the filter was given.</param>
    /// <param name="stopped">Whether the filter stopped at the bound, the faults it counts being those of the part it read.</param>
    /// <param name="ended">
    /// Whether the filter read its data to the end the file gave it, which its end then says something of: an end marker
    /// missing, or a <c>~</c> no <c>&gt;</c> follows, where the reader cut the data, a guard hides its end, or the filter
    /// stopped is the cut's.
    /// </param>
    /// <param name="diagnostics">Receives the reports.</param>
    /// <param name="position">Where the stream's data starts.</param>
    private static void ReportFaults(
        in DataFaults faults, PdfName name, int encoded, bool stopped, bool ended, PdfDiagnostics? diagnostics, long position)
    {
        if (diagnostics is null)
        {
            return;
        }

        var subject = name == PdfName.ASCII85Decode ? "An ASCII85 stream" : name == PdfName.ASCIIHexDecode ? "An ASCIIHex stream" : "A RunLength stream";
        var read = stopped ? " before decoding stopped at the bound" : string.Empty;

        if (faults.Introducer)
        {
            diagnostics.Repair(
                PdfDiagnosticCodes.FilterFailed,
                $"{subject} starts with the <~ PostScript writes and PDF does not; it was passed over.",
                position);
        }

        if (faults.Passed > 0)
        {
            diagnostics.Warn(
                PdfDiagnosticCodes.FilterFailed,
                faults.Passed == 1
                    ? Invariant($"{subject} holds a byte that encodes nothing{read}, at byte {faults.FirstPassed} of its {encoded}; it was passed over.")
                    : Invariant($"{subject} holds {faults.Passed} bytes that encode nothing{read}, the first at byte {faults.FirstPassed} of its {encoded}; they were passed over."),
                position);
        }

        if (faults.Overflowing > 0)
        {
            diagnostics.Warn(
                PdfDiagnosticCodes.FilterFailed,
                faults.Overflowing == 1
                    ? Invariant($"{subject} holds a group whose value does not fit in four bytes{read}, ending at byte {faults.FirstOverflowing} of its {encoded}; it was decoded to the four low-order bytes of its value, which may be wrong.")
                    : Invariant($"{subject} holds {faults.Overflowing} groups whose values do not fit in four bytes{read}, the first ending at byte {faults.FirstOverflowing} of its {encoded}; each was decoded to the four low-order bytes of its value, which may be wrong."),
                position);
        }

        if (faults.BrokenMarker > 0)
        {
            diagnostics.Warn(
                PdfDiagnosticCodes.FilterFailed,
                Invariant($"{subject} holds a ~ that no > follows, at byte {faults.BrokenMarker} of its {encoded}; decoding stopped there, and what follows was not decoded."),
                position);
        }

        if (faults.LoneDigit)
        {
            diagnostics.Warn(
                PdfDiagnosticCodes.FilterFailed,
                $"{subject} ends, before the ~ that closes it, with a group of a single digit, which encodes no byte; it was dropped.",
                position);
        }

        if (!ended)
        {
            return;
        }

        if (faults.UnfinishedMarker)
        {
            diagnostics.Repair(
                PdfDiagnosticCodes.FilterFailed,
                $"{subject} ends on a ~ that no > follows; it decoded to the end of its data.",
                position);
        }

        if (faults.Ending == DataEnding.Marked)
        {
            return;
        }

        var marker = name == PdfName.ASCII85Decode ? "its ~> end marker" : name == PdfName.ASCIIHexDecode ? "its > end marker" : "its end-of-data byte";

        if (faults.Ending == DataEnding.Unmarked)
        {
            diagnostics.Repair(PdfDiagnosticCodes.FilterFailed, $"{subject} ends without {marker}; it decoded to the end of its data.", position);
            return;
        }

        var message = faults.Ending switch
        {
            DataEnding.UnmarkedInsideUnit when name == PdfName.ASCII85Decode =>
                Invariant($"{subject} ends without {marker}, inside a group of {faults.UnitLength} digits; the group was completed as a last one is, and data may have been lost."),
            DataEnding.UnmarkedInsideUnit =>
                $"{subject} ends without {marker}, after a digit without its pair; it was completed with a 0 as a last one is, and data may have been lost.",
            DataEnding.UnmarkedAfterLoneDigit =>
                $"{subject} ends without {marker}, after a group of a single digit, which encodes no byte; it was dropped, and data may have been lost.",
            DataEnding.UnmarkedInsideRun when faults.UnitLength == 1 =>
                $"{subject} ends after the length of a run of 1 byte, without that byte; data may have been lost.",
            DataEnding.UnmarkedInsideRun when faults.UnitHeld == 0 =>
                Invariant($"{subject} ends after the length of a run of {faults.UnitLength} bytes, without any of them; data may have been lost."),
            DataEnding.UnmarkedInsideRun =>
                Invariant($"{subject} ends inside a run of {faults.UnitLength} bytes, of which it holds {faults.UnitHeld}; they were kept, and data may have been lost."),
            _ => $"{subject} ends after the length of a run, without the byte the run repeats; data may have been lost.",
        };

        diagnostics.Warn(PdfDiagnosticCodes.FilterFailed, message, position);
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

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
