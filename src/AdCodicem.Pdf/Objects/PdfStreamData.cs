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

    /// <summary>Returns the encoded bytes, exactly as they appear in the file.</summary>
    public abstract ReadOnlyMemory<byte> GetBytes();

    /// <summary>Creates stream data over bytes already in memory.</summary>
    public static PdfStreamData FromMemory(ReadOnlyMemory<byte> bytes) => new MemoryStreamData(bytes);

    private sealed class MemoryStreamData(ReadOnlyMemory<byte> bytes) : PdfStreamData
    {
        public override int Length => bytes.Length;

        public override ReadOnlyMemory<byte> GetBytes() => bytes;
    }
}
