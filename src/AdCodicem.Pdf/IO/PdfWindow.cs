using System.Buffers;

namespace AdCodicem.Pdf.IO;

/// <summary>
/// A view onto part of a file. For an in-memory source it is a slice; for a file it is a pooled buffer
/// that must be returned, which is why the window is disposable and short-lived by design.
/// </summary>
internal readonly struct PdfWindow : IDisposable
{
    private readonly byte[]? _rented;

    public PdfWindow(ReadOnlyMemory<byte> memory, long offset, byte[]? rented)
    {
        Memory = memory;
        Offset = offset;
        _rented = rented;
    }

    /// <summary>An empty window, over nothing.</summary>
    public static PdfWindow Empty => default;

    /// <summary>Gets the bytes of the window.</summary>
    public ReadOnlyMemory<byte> Memory { get; }

    /// <summary>Gets the offset in the file the window starts at.</summary>
    public long Offset { get; }

    /// <summary>Gets the number of bytes in the window.</summary>
    public int Length => Memory.Length;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_rented is not null)
        {
            ArrayPool<byte>.Shared.Return(_rented);
        }
    }
}
