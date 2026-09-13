using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Documents;

/// <summary>
/// A PDF document opened for reading.
/// </summary>
/// <remarks>
/// Opening a document indexes it and reads nothing else. A document is not thread-safe: it caches what it
/// parses, so one document belongs to one thread at a time. Several documents can of course be processed
/// in parallel, and the reader holds no shared mutable state to make that unsafe.
/// </remarks>
public sealed class PdfDocument : IDisposable
{
    private readonly PdfFileReader _reader;
    private bool _disposed;

    private PdfDocument(PdfFileReader reader, PdfDiagnostics diagnostics)
    {
        _reader = reader;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets what the reader noticed while opening and reading the document.</summary>
    public PdfDiagnostics Diagnostics { get; }

    /// <summary>Gets the document trailer.</summary>
    public PdfDictionary Trailer => _reader.Trailer;

    /// <summary>Gets the document catalogue, the root of the object graph.</summary>
    public PdfDictionary? Catalog => Trailer.GetDictionary(PdfName.Root);

    /// <summary>Gets the version declared by the file header.</summary>
    public string Version => _reader.Version;

    /// <summary>Gets a value indicating whether the cross-reference index had to be rebuilt.</summary>
    public bool WasRepaired => _reader.WasRepaired;

    /// <summary>Gets the number of objects the file defines.</summary>
    public int ObjectCount => _reader.ObjectCount;

    /// <summary>Gets a value indicating whether the document is encrypted.</summary>
    public bool IsEncrypted => Trailer.ContainsKey(PdfName.Encrypt);

    /// <summary>Opens a document from a file, reading its contents on demand.</summary>
    public static PdfDocument Open(string path, PdfReaderOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Open(PdfFileSource.FromFile(path), options, ownsSource: true);
    }

    /// <summary>Opens a document from bytes already in memory.</summary>
    public static PdfDocument Open(ReadOnlyMemory<byte> bytes, PdfReaderOptions? options = null) =>
        Open(PdfFileSource.FromMemory(bytes), options, ownsSource: true);

    /// <summary>Opens a document from a stream. A non-seekable stream is buffered in full.</summary>
    public static PdfDocument Open(Stream stream, PdfReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Open(PdfFileSource.FromStream(stream), options, ownsSource: true);
    }

    /// <summary>Opens a document from a source the caller keeps ownership of.</summary>
    public static PdfDocument Open(PdfFileSource source, PdfReaderOptions? options, bool ownsSource)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= PdfReaderOptions.Default;

        if (source.Length == 0)
        {
            throw new PdfFormatException("The input is empty.");
        }

        var diagnostics = new PdfDiagnostics { Capacity = options.DiagnosticCapacity };
        var reader = new PdfFileReader(source, diagnostics, options.ObjectCacheCapacity, ownsSource);
        if (reader.ObjectCount == 0)
        {
            reader.Dispose();
            throw new PdfFormatException("No PDF object could be found in the input.");
        }

        var document = new PdfDocument(reader, diagnostics);

        if (document.IsEncrypted && options.ThrowOnEncrypted)
        {
            document.Dispose();
            throw new PdfEncryptedException("The document is encrypted; decryption is not supported yet.");
        }

        return document;
    }

    /// <summary>Returns the object with the given identifier, reading it if it is not already in memory.</summary>
    public PdfObject GetObject(PdfObjectId id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _reader.GetObject(id);
    }

    /// <summary>Returns the object numbers the file defines.</summary>
    public IEnumerable<int> ObjectNumbers => _reader.ObjectNumbers;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _reader.Dispose();
    }
}
