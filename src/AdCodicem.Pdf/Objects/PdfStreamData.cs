namespace AdCodicem.Pdf.Objects;

/// <summary>
/// Supplies the raw, still-encoded bytes of a stream.
/// </summary>
/// <remarks>
/// Stream data is deliberately indirect. A stream read from a file knows only where its bytes live, and
/// touches them when — and only when — the caller asks. Copying a stream between documents therefore
/// moves encoded bytes without a decompress/recompress cycle.
/// </remarks>
public abstract class PdfStreamData
{
    /// <summary>Gets the number of encoded bytes.</summary>
    public abstract int Length { get; }

    /// <summary>
    /// Gets the offset in the file the data was read from, or -1 when it never came from one. A
    /// diagnostic about a stream is only actionable if it says which stream.
    /// </summary>
    public virtual long Position => -1;

    /// <summary>
    /// Gets the guards decoding the data works under: those of the document it was read from, however long
    /// after opening it is decoded, or the defaults for data that never came from one.
    /// </summary>
    internal virtual IO.PdfLimitGuard LimitGuard => IO.PdfLimitGuard.Default;

    /// <summary>
    /// Gets a value indicating whether the reader cut the data at one of its guards, rather than finding it
    /// cut in the file: decoding it then says nothing of the tail it lost, which the guard already reported.
    /// </summary>
    internal virtual bool CutByGuard => false;

    /// <summary>
    /// Gets a value indicating whether where the data ends is the guard's to hide, though the data is the file's: an
    /// <c>endstream</c> proven the stream's own ended it under a <c>/Length</c> a guard hid, with white space, or comments,
    /// before the keyword that only the length could say are the data's or not (#224). Its first filter decodes it up to
    /// where that run starts, what lies before it the file's, its running out there saying nothing of the file, and the
    /// end of what it may lack the guard's; Flate data whose deflate body ends in the data as taken is whole.
    /// </summary>
    internal virtual bool EndHidden => false;

    /// <summary>Returns the encoded bytes, as they appear in the file, up to the reader's bound.</summary>
    /// <remarks>
    /// Data read from a document held in memory is a view of that memory, which keeps it alive and changes as it does. Data
    /// read from a document gives no more than <see cref="Documents.PdfReaderLimits.MaxStreamLength"/> bytes: past it,
    /// what lies within it comes back, fewer bytes than <see cref="Length"/> says, and
    /// <see cref="Diagnostics.PdfDiagnosticCodes.LimitStreamLength"/> is reported once in the document's diagnostics.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The data was read from a document that is disposed.</exception>
    /// <exception cref="Diagnostics.PdfLimitExceededException">
    /// The data runs past <see cref="Documents.PdfReaderLimits.MaxStreamLength"/>, and its document was opened with
    /// <see cref="Documents.PdfReaderOptions.ThrowOnLimit"/>.
    /// </exception>
    public abstract ReadOnlyMemory<byte> GetBytes();

    /// <summary>Creates stream data over bytes already in memory.</summary>
    public static PdfStreamData FromMemory(ReadOnlyMemory<byte> bytes) => new MemoryStreamData(bytes);

    private sealed class MemoryStreamData(ReadOnlyMemory<byte> bytes) : PdfStreamData
    {
        public override int Length => bytes.Length;

        public override ReadOnlyMemory<byte> GetBytes() => bytes;
    }
}
