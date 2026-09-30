using System.Buffers;
using System.IO.Compression;

namespace AdCodicem.Pdf.IO.Filters;

/// <summary>Decodes the <c>FlateDecode</c> filter.</summary>
/// <remarks>
/// <para>
/// The specification says zlib, and most files comply. The ones that do not are common enough to be worth
/// handling: raw deflate with no zlib header, leading white space before the header, data whose tail was lost,
/// and data that turns corrupt part of the way. In the last two cases the bytes that did decode are worth more
/// than an exception — but the loss is said, never left for the caller to find.
/// </para>
/// <para>
/// The framework's inflater throws from the read that meets a fault, and what that read decoded from the input
/// it was last handed is lost with it. Data that faults is therefore read again, and only what the first reading
/// lost is added to what it kept. A zlib stream's body goes first, read as raw deflate at full speed: raw deflate
/// has no checksum, so a body that reads to its end is whole, and only the checksum after it disagreed — all of
/// it is kept, as other readers keep it, and the disagreement is said. A body that faults again, and raw deflate
/// that faulted, are read once more: at full speed up to the input the fault was met in, and one byte at a time
/// from there (<see cref="FlateInput"/>), so that what decoded before the byte the fault lies in is kept, and where
/// the fault lies is known. What that one byte decoded ahead of the fault is lost with it: the inflater throws
/// before it says how much it wrote.
/// </para>
/// <para>
/// Reading again adds no bound, and needs none (ADR 34). A form of the data — zlib, raw deflate, zlib after white
/// space — is read again only after a fault, at most twice more, and its readings write into one output, under
/// <see cref="Documents.PdfReaderLimits.MaxDecodedStreamLength"/>; each reading ends with its input, and the one
/// a byte at a time covers at most one piece of it, <see cref="FlateInput.ChunkLength"/> long. The next form is
/// tried only when one decoded nothing before its fault, or, for raw deflate read from white space, turned corrupt
/// with a zlib header after the white space. No zlib header starts with white space, so data that has the third
/// form fails the first at its header. What raw deflate kept is let go while zlib after the white space is read,
/// and raw deflate is read again when it read further: a stream is read eight times at most, a byte at a time
/// through three pieces at most, and holds one output at a time. Sound data is read once, as it always was.
/// </para>
/// </remarks>
internal static class FlateFilter
{
    /// <summary>
    /// The length of a zlib header without a preset dictionary. A PDF stream has no way to supply one: a
    /// header that asks for one fails as zlib, and is read as the other forms are.
    /// </summary>
    private const int ZlibHeaderLength = 2;

    /// <summary>
    /// Decodes a Flate stream, reporting success explicitly.
    /// </summary>
    /// <remarks>
    /// Success is a separate answer from the length of the output. A validly compressed stream that
    /// contains nothing — an empty content stream, an empty appearance — decodes to zero bytes, and
    /// treating that as a failure would hand the caller the compressed bytes instead of the empty
    /// content it asked for.
    /// </remarks>
    /// <param name="data">The encoded data.</param>
    /// <param name="decoded">What was decoded, at most <paramref name="maxLength"/> bytes.</param>
    /// <param name="repaired">Whether the data was not zlib as the specification says, and was read anyway.</param>
    /// <param name="ending">Whether the data ended where its format says it does, and what was kept if not.</param>
    /// <param name="faultAt">
    /// For data that turned <see cref="FlateEnding.Corrupt"/>, where in <paramref name="data"/>, counted from 1, lies
    /// the byte the inflater met the fault in: the last it had been handed; 0 otherwise.
    /// </param>
    /// <param name="limited">Whether the data decodes to more than <paramref name="maxLength"/> bytes.</param>
    /// <param name="maxLength">The most the data may decode to.</param>
    public static bool TryDecode(
        ReadOnlyMemory<byte> data,
        out byte[] decoded,
        out bool repaired,
        out FlateEnding ending,
        out int faultAt,
        out bool limited,
        int maxLength)
    {
        repaired = false;
        ending = FlateEnding.Whole;
        faultAt = 0;
        limited = false;

        if (data.IsEmpty)
        {
            decoded = [];
            return true;
        }

        if (TryInflate(data, zlibHeader: true, maxLength, out decoded, out ending, out faultAt, out limited))
        {
            return true;
        }

        // A raw deflate stream, or a zlib header that was mangled.
        var raw = TryInflate(data, zlibHeader: false, maxLength, out decoded, out ending, out faultAt, out limited);
        var skipped = SkipLeadingWhitespace(data);
        repaired = raw;

        // Leading white space before the header happens when a generator miscounts /Length. Read as raw deflate, the
        // white space and the header after it may decode to a few bytes before they fault: a zlib stream after the
        // white space is taken over those when it reads at least as far. Raw deflate can start with a byte PDF
        // counts as white space too — a zero byte starts a stored block, a line feed a block of fixed codes, a form
        // feed or a carriage return one of dynamic codes —, and a zlib reading that faults sooner is then bytes
        // that only passed for a header.
        if (skipped > 0 && (!raw || (ending == FlateEnding.Corrupt && HasPlainZlibHeader(data.Span[skipped..]))))
        {
            // What raw deflate kept is let go while zlib is read, so that no more than one output is held at once,
            // and read again, to the same bytes, if raw deflate read further.
            var rawFault = faultAt;
            decoded = [];

            if (TryInflate(data[skipped..], zlibHeader: true, maxLength, out decoded, out ending, out faultAt, out limited) &&
                (!raw || ending != FlateEnding.Corrupt || skipped + faultAt >= rawFault))
            {
                faultAt = ending == FlateEnding.Corrupt ? skipped + faultAt : 0;
                repaired = true;
                return true;
            }

            if (raw)
            {
                _ = TryInflate(data, zlibHeader: false, maxLength, out decoded, out ending, out faultAt, out limited);
            }
        }

        if (raw)
        {
            return true;
        }

        decoded = [];
        ending = FlateEnding.Whole;
        faultAt = 0;
        return false;
    }

    private static int SkipLeadingWhitespace(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        var index = 0;

        while (index < span.Length && PdfCharacters.IsWhitespace(span[index]))
        {
            index++;
        }

        return index;
    }

    private static bool TryInflate(
        ReadOnlyMemory<byte> data,
        bool zlibHeader,
        int maxLength,
        out byte[] result,
        out FlateEnding ending,
        out int faultAt,
        out bool limited)
    {
        ending = FlateEnding.Whole;
        faultAt = 0;

        using var input = new FlateInput(data);
        var output = new PdfBoundedOutput(Math.Max(1024, data.Length * 4L), maxLength);
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);

        try
        {
            // Data that stops at the bound neither runs out nor faults: its end was never seen.
            var faulted = !Inflate(input, zlibHeader, output, buffer, out limited);

            // The inflater returns the end of its input as the end of the data. Having asked for more, it had
            // not reached the end of its last block — or, for zlib, of the checksum after it. A host that sets
            // System.IO.Compression.UseStrictValidation has the framework throw there instead: the same lost
            // tail, which is why the request is looked at before the fault.
            if (input.ReadPastEnd)
            {
                ending = zlibHeader && data.Length > ZlibHeaderLength &&
                    IsWholeDeflate(data[ZlibHeaderLength..], output.Count, buffer)
                    ? FlateEnding.ChecksumMissing
                    : FlateEnding.TailLost;
            }
            else if (faulted)
            {
                ending = Recover(data, zlibHeader, input.LastReadStart, output, buffer, out faultAt, out limited);
            }

            result = output.ToArray();

            // Data corrupt before anything decoded is read another way, or left encoded.
            return ending != FlateEnding.Corrupt || output.Count > 0;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Reads data that faulted again, to keep what the reading that met the fault lost: the whole of a zlib
    /// stream whose checksum alone was wrong, and otherwise what decoded before the byte the fault lies in.
    /// </summary>
    /// <param name="data">The data that faulted.</param>
    /// <param name="zlibHeader">Whether the reading that faulted took it for zlib.</param>
    /// <param name="faultedRead">Where the input that reading met the fault in starts.</param>
    /// <param name="output">What that reading kept, to which what it lost is added.</param>
    /// <param name="buffer">A buffer to decode into.</param>
    /// <param name="faultAt">Where in <paramref name="data"/>, counted from 1, lies the byte the inflater met the fault in.</param>
    /// <param name="limited">Whether reading again reached the bound.</param>
    private static FlateEnding Recover(
        ReadOnlyMemory<byte> data,
        bool zlibHeader,
        int faultedRead,
        PdfBoundedOutput output,
        byte[] buffer,
        out int faultAt,
        out bool limited)
    {
        faultAt = 0;
        limited = false;
        var start = 0;

        if (zlibHeader)
        {
            // A header the inflater turns down faults before anything decodes, and one that asks for a preset
            // dictionary, which a PDF stream cannot supply, cannot be read past: neither is read again.
            if (!HasPlainZlibHeader(data.Span))
            {
                return FlateEnding.Corrupt;
            }

            // The body read as raw deflate is the same deflate data without the checksum after it: it faults
            // where the zlib reading did, or, when the checksum was the fault, reads to its end.
            start = ZlibHeaderLength;
            using var body = new FlateInput(data[start..]);

            if (Inflate(body, zlibHeader: false, output, buffer, out limited))
            {
                return limited ? FlateEnding.Whole : FlateEnding.ChecksumMismatch;
            }

            faultedRead = body.LastReadStart;
        }

        // Once more, one byte at a time through the input the fault was met in: it faults at the same byte
        // again, unless it reaches the bound first, and loses no more than what that byte decoded before it.
        using var input = new FlateInput(data[start..], slowFrom: faultedRead);
        _ = Inflate(input, zlibHeader: false, output, buffer, out limited);

        if (limited)
        {
            return FlateEnding.Whole;
        }

        faultAt = start + input.BytesRead;
        return FlateEnding.Corrupt;
    }

    /// <summary>
    /// Decodes <paramref name="input"/> into <paramref name="output"/>, adding only what lies past the bytes the
    /// output already holds, which an earlier reading of the same data kept.
    /// </summary>
    /// <returns>
    /// False when the inflater met a fault; true when it stopped at the end of the data, at the end of its input,
    /// or, <paramref name="limited"/>, at the bound.
    /// </returns>
    private static bool Inflate(FlateInput input, bool zlibHeader, PdfBoundedOutput output, byte[] buffer, out bool limited)
    {
        using Stream decompressor = zlibHeader
            ? new ZLibStream(input, CompressionMode.Decompress, leaveOpen: true)
            : new DeflateStream(input, CompressionMode.Decompress, leaveOpen: true);
        var kept = output.Count;
        long decoded = 0;
        limited = false;

        while (true)
        {
            int read;

            try
            {
                read = decompressor.Read(buffer, 0, buffer.Length);
            }
            catch (Exception exception) when (IsInflaterFault(exception))
            {
                return false;
            }

            if (read == 0)
            {
                return true;
            }

            var skip = (int)Math.Clamp(kept - decoded, 0, read);
            decoded += read;

            if (!output.TryWrite(buffer.AsSpan(skip, read - skip)))
            {
                limited = true;
                return true;
            }
        }
    }

    /// <summary>
    /// Determines whether <paramref name="data"/> starts with a zlib header the inflater reads past: deflate, a
    /// window of at most 32 KB, a check that holds, and no preset dictionary.
    /// </summary>
    internal static bool HasPlainZlibHeader(ReadOnlySpan<byte> data) =>
        data.Length >= ZlibHeaderLength &&
        (data[0] & 0x0F) == 8 &&
        data[0] >> 4 <= 7 &&
        ((data[0] << 8) | data[1]) % 31 == 0 &&
        (data[1] & 0x20) == 0;

    /// <summary>
    /// Determines whether <paramref name="body"/> is deflate data that reaches the end of its last block,
    /// reading it again without keeping what it decodes to.
    /// </summary>
    /// <remarks>
    /// A zlib stream that ran out may have lost its tail, or only its checksum; the framework's inflater does
    /// not say which. Its body read as raw deflate does: raw deflate carries no checksum, so it ends where the
    /// last block does. Only a stream that ran out is read twice, and the second reading stops as soon as it
    /// decodes past the <paramref name="decoded"/> bytes the first one did, which it never does when it is the
    /// same data.
    /// </remarks>
    /// <param name="body">The data after the zlib header.</param>
    /// <param name="decoded">What the zlib reading decoded, which the body must decode to exactly.</param>
    /// <param name="buffer">A buffer to decode into, whose contents are discarded.</param>
    internal static bool IsWholeDeflate(ReadOnlyMemory<byte> body, int decoded, byte[] buffer)
    {
        using var input = new FlateInput(body);
        using var decompressor = new DeflateStream(input, CompressionMode.Decompress, leaveOpen: true);
        long total = 0;

        try
        {
            int read;

            while ((read = decompressor.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;

                if (total > decoded)
                {
                    return false;
                }
            }
        }
        catch (Exception exception) when (IsInflaterFault(exception))
        {
            return false;
        }

        return !input.ReadPastEnd && total == decoded;
    }

    /// <summary>
    /// Determines whether an exception is the inflater's complaint about its data: an
    /// <see cref="InvalidDataException"/> for data it cannot decode, or the <see cref="IOException"/> its zlib
    /// raises for what it cannot go on with, such as a header that asks for a preset dictionary. The input is
    /// memory, and never throws either.
    /// </summary>
    internal static bool IsInflaterFault(Exception exception) => exception is InvalidDataException or IOException;
}
