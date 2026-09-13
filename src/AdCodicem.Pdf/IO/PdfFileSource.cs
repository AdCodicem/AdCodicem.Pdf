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
    public abstract int Read(long offset, Span<byte> buffer);

    /// <summary>Creates a source over bytes already in memory.</summary>
    public static PdfFileSource FromMemory(ReadOnlyMemory<byte> data) => new MemorySource(data);

    /// <summary>Creates a source that reads from a file on demand.</summary>
    public static PdfFileSource FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return new FileSource(path);
    }

    /// <summary>Reads a stream fully into memory and serves it from there.</summary>
    /// <remarks>
    /// A non-seekable stream cannot be read at random, so it is buffered. Callers that care about memory
    /// should hand the reader a file path instead.
    /// </remarks>
    public static PdfFileSource FromStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (stream is MemoryStream memoryStream && memoryStream.TryGetBuffer(out var segment))
        {
            return new MemorySource(segment.AsMemory());
        }

        var buffer = new MemoryStream();
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

        if (this is MemorySource memory)
        {
            return new PdfWindow(memory.Slice(offset, available), offset, rented: null);
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

        public ReadOnlyMemory<byte> Slice(long offset, int length) => data.Slice((int)offset, length);
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
