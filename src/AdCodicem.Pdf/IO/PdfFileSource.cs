using System.Buffers;
using Microsoft.Win32.SafeHandles;

namespace AdCodicem.Pdf.IO;

/// <summary>
/// The bytes of a PDF file, addressable at random without being held in memory.
/// </summary>
/// <remarks>
/// Everything the reader does goes through windows onto this source. A document is therefore never
/// "loaded": the reader keeps an index of where objects live and reads the few hundred bytes of an object
/// when something asks for it.
/// </remarks>
public abstract class PdfFileSource : IDisposable
{
    /// <summary>Gets the length of the file in bytes.</summary>
    public abstract long Length { get; }

    /// <summary>Reads into <paramref name="buffer"/> from <paramref name="offset"/>, returning the count read.</summary>
    /// <remarks>
    /// The reader asks a source only for bytes it holds: <paramref name="offset"/> is at least 0, <paramref name="buffer"/>
    /// is never empty, and the two together end at <see cref="Length"/> at most. A source need not accept any other read,
    /// and may refuse one: none comes from the reader, however damaged the file it reads (#167).
    /// </remarks>
    public abstract int Read(long offset, Span<byte> buffer);

    /// <summary>Creates a source over bytes already in memory.</summary>
    public static PdfFileSource FromMemory(ReadOnlyMemory<byte> data) => new MemorySource(data);

    /// <summary>Creates a source that reads from a file on demand.</summary>
    public static PdfFileSource FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return new FileSource(path);
    }

    /// <summary>
    /// Creates a source over a stream that holds a whole file: from its start when it can seek, whatever its position,
    /// and from where it stands when it cannot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <see cref="MemoryStream"/> itself — not a type derived from one — that exposes its buffer is read in that
    /// buffer, from its origin to its length, without a copy: it must neither change nor grow while the source is read.
    /// Any other stream is copied into memory, a seekable one included; callers that care about memory should hand the
    /// reader a file path instead. A derived <see cref="MemoryStream"/> is copied because it may lend its buffer again
    /// once disposed, as a pooled one does.
    /// </para>
    /// <para>The stream's position afterward is unspecified, and the stream is not disposed of.</para>
    /// </remarks>
    public static PdfFileSource FromStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (stream.GetType() == typeof(MemoryStream) && ((MemoryStream)stream).TryGetBuffer(out var segment))
        {
            return new MemorySource(segment.AsMemory());
        }

        // A file's offsets count from its first byte: a seekable stream is the file from its start, whatever was read of
        // it before, as a type sniffed from its first bytes and not rewound. It says how long it is, and is copied into
        // an array that long, rather than into one grown by doubling, which the document would keep at up to twice the
        // file; a length past what an array holds is left to the copy to refuse.
        var capacity = 0;

        if (stream.CanSeek)
        {
            stream.Position = 0;
            capacity = stream.Length <= Array.MaxLength ? (int)stream.Length : 0;
        }

        using var buffer = new MemoryStream(capacity);
        stream.CopyTo(buffer);
        return new MemorySource(buffer.GetBuffer().AsMemory(0, (int)buffer.Length));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases resources held by the source.</summary>
    protected virtual void Dispose(bool disposing)
    {
    }

    /// <summary>
    /// Hands out the bytes at <paramref name="offset"/> directly, when the source holds them in memory.
    /// </summary>
    /// <remarks>
    /// A source backed by memory serves a window without copying anything; one backed by a file cannot,
    /// and says so, which is why this is asked of the source rather than decided by testing its type.
    /// </remarks>
    private protected virtual bool TryGetMemory(long offset, int length, out ReadOnlyMemory<byte> memory)
    {
        memory = default;
        return false;
    }

    /// <summary>
    /// Lends the <paramref name="length"/> bytes at <paramref name="offset"/> where the source holds them, without a copy;
    /// false for a source that reads them from a file, or bytes it does not hold.
    /// </summary>
    internal bool TryLend(long offset, int length, out ReadOnlyMemory<byte> memory)
    {
        if (offset < 0 || length < 0 || offset > Length - length)
        {
            memory = default;
            return false;
        }

        return TryGetMemory(offset, length, out memory);
    }

    /// <summary>
    /// Returns a view of <paramref name="length"/> bytes at <paramref name="offset"/>, clamped to the file.
    /// The window must be disposed; for a file-backed source it holds a pooled buffer.
    /// </summary>
    internal PdfWindow GetWindow(long offset, int length)
    {
        if (offset < 0 || offset >= Length || length <= 0)
        {
            return PdfWindow.Empty;
        }

        var available = (int)Math.Min(length, Length - offset);

        if (TryGetMemory(offset, available, out var memory))
        {
            return new PdfWindow(memory, offset, rented: null);
        }

        var buffer = ArrayPool<byte>.Shared.Rent(available);
        var read = Read(offset, buffer.AsSpan(0, available));
        return new PdfWindow(buffer.AsMemory(0, read), offset, buffer);
    }

    private sealed class MemorySource(ReadOnlyMemory<byte> data) : PdfFileSource
    {
        public override long Length => data.Length;

        public override int Read(long offset, Span<byte> buffer)
        {
            if (offset < 0 || offset >= data.Length)
            {
                return 0;
            }

            var slice = data.Span[(int)offset..];
            var count = Math.Min(slice.Length, buffer.Length);
            slice[..count].CopyTo(buffer);
            return count;
        }

        private protected override bool TryGetMemory(long offset, int length, out ReadOnlyMemory<byte> memory)
        {
            memory = data.Slice((int)offset, length);
            return true;
        }
    }

    private sealed class FileSource : PdfFileSource
    {
        private readonly SafeFileHandle _handle;

        public FileSource(string path)
        {
            _handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
            Length = RandomAccess.GetLength(_handle);
        }

        public override long Length { get; }

        public override int Read(long offset, Span<byte> buffer)
        {
            if (offset < 0 || offset >= Length)
            {
                return 0;
            }

            var total = 0;

            while (total < buffer.Length)
            {
                var read = RandomAccess.Read(_handle, buffer[total..], offset + total);
                if (read == 0)
                {
                    break;
                }

                total += read;
            }

            return total;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _handle.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
