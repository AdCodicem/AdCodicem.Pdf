using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Documents;

/// <summary>
/// A PDF document opened for reading.
/// </summary>
/// <remarks>
/// Opening a document indexes it from its cross-reference sections, with the objects a cross-reference stream's
/// dictionary refers to for its data and its rows, and reads its catalog through the trailer's <c>/Root</c>. When
/// <c>/Root</c> leads to no catalog, opening loads the indexed objects one after another until one is a catalog, and
/// rebuilds the index when none is. A rebuild scans the whole file and loads every object written directly in it, to
/// take in those its object streams hold, then looks for the catalog the same way if <c>/Root</c> still leads to
/// none. It happens as the document opens when its sections cannot be read or lead to no catalog, and, as it opens or
/// later, when an object it reads is neither where its entry places it nor near it, or is missing from an index the
/// sections did not give whole: <see cref="WasRepaired"/> can turn true after opening. Anything else is read when
/// something asks for it. A document is not thread-safe: it caches what it parses, so one document belongs to one
/// thread at a time. Several documents can of course be processed in parallel, and the reader holds no shared
/// mutable state to make that unsafe.
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

    /// <summary>Gets the document catalog, the root of the object graph.</summary>
    public PdfDictionary? Catalog => Trailer.GetDictionary(PdfName.Root);

    /// <summary>Gets the version declared by the file header.</summary>
    public string Version => _reader.Version;

    /// <summary>Gets a value indicating whether the cross-reference index had to be rebuilt.</summary>
    /// <remarks>
    /// The index is rebuilt as the document opens, or later, when something read asks for it: an object that is
    /// neither where the index places it nor near it, or one missing from an index the file's sections did not give
    /// whole. It can therefore turn true after the document has opened, as more of the document is read.
    /// </remarks>
    public bool WasRepaired => _reader.WasRepaired;

    /// <summary>
    /// Gets the number of entries in the cross-reference index, one per object number: those in use, and those the
    /// file's sections mark free, object 0 — the head of the free list — among them. A rebuild empties the index,
    /// free entries included, and fills it with the objects its scan finds and those their object streams hold.
    /// </summary>
    public int ObjectCount => _reader.ObjectCount;

    /// <summary>Gets a value indicating whether the document is encrypted.</summary>
    public bool IsEncrypted => Trailer.ContainsKey(PdfName.Encrypt);

    /// <summary>Opens a document from a file, reading its contents on demand.</summary>
    /// <param name="path">The path of the file, which stays open until the document is disposed.</param>
    /// <param name="options">How to open the document, or null for <see cref="PdfReaderOptions.Default"/>.</param>
    /// <exception cref="PdfFormatException">The file is empty, or no object could be found in it.</exception>
    /// <exception cref="PdfEncryptedException">
    /// The document is encrypted, and <see cref="PdfReaderOptions.ThrowOnEncrypted"/> is set, as it is by default.
    /// </exception>
    /// <exception cref="PdfLimitExceededException">
    /// Opening reached one of <see cref="PdfReaderOptions.Limits"/>, and <see cref="PdfReaderOptions.ThrowOnLimit"/> is set.
    /// </exception>
    public static PdfDocument Open(string path, PdfReaderOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Open(PdfFileSource.FromFile(path), options, ownsSource: true);
    }

    /// <summary>Opens a document from bytes already in memory.</summary>
    /// <param name="bytes">The bytes of the file, which the document reads where they are, not copied.</param>
    /// <param name="options">How to open the document, or null for <see cref="PdfReaderOptions.Default"/>.</param>
    /// <exception cref="PdfFormatException">The bytes are empty, or no object could be found in them.</exception>
    /// <exception cref="PdfEncryptedException">
    /// The document is encrypted, and <see cref="PdfReaderOptions.ThrowOnEncrypted"/> is set, as it is by default.
    /// </exception>
    /// <exception cref="PdfLimitExceededException">
    /// Opening reached one of <see cref="PdfReaderOptions.Limits"/>, and <see cref="PdfReaderOptions.ThrowOnLimit"/> is set.
    /// </exception>
    public static PdfDocument Open(ReadOnlyMemory<byte> bytes, PdfReaderOptions? options = null) =>
        Open(PdfFileSource.FromMemory(bytes), options, ownsSource: true);

    /// <summary>Opens a document from a stream. A non-seekable stream is buffered in full.</summary>
    /// <param name="stream">The stream that holds the file, which the document does not dispose of.</param>
    /// <param name="options">How to open the document, or null for <see cref="PdfReaderOptions.Default"/>.</param>
    /// <exception cref="PdfFormatException">
    /// What was read from the stream is empty, or no object could be found in it.
    /// </exception>
    /// <exception cref="PdfEncryptedException">
    /// The document is encrypted, and <see cref="PdfReaderOptions.ThrowOnEncrypted"/> is set, as it is by default.
    /// </exception>
    /// <exception cref="PdfLimitExceededException">
    /// Opening reached one of <see cref="PdfReaderOptions.Limits"/>, and <see cref="PdfReaderOptions.ThrowOnLimit"/> is set.
    /// </exception>
    public static PdfDocument Open(Stream stream, PdfReaderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Open(PdfFileSource.FromStream(stream), options, ownsSource: true);
    }

    /// <summary>
    /// Opens a document from a source, which the document takes over or leaves to the caller as
    /// <paramref name="ownsSource"/> says.
    /// </summary>
    /// <param name="source">The bytes of the file.</param>
    /// <param name="options">How to open the document, or null for <see cref="PdfReaderOptions.Default"/>.</param>
    /// <param name="ownsSource">
    /// Whether the document takes <paramref name="source"/> over: when true, disposing of the document disposes of the
    /// source, and so does an opening that fails; when false, the source stays the caller's to dispose of.
    /// </param>
    /// <exception cref="PdfFormatException">The source is empty, or no object could be found in it.</exception>
    /// <exception cref="PdfEncryptedException">
    /// The document is encrypted, and <see cref="PdfReaderOptions.ThrowOnEncrypted"/> is set, as it is by default.
    /// </exception>
    /// <exception cref="PdfLimitExceededException">
    /// Opening reached one of <see cref="PdfReaderOptions.Limits"/>, and <see cref="PdfReaderOptions.ThrowOnLimit"/> is set.
    /// </exception>
    public static PdfDocument Open(PdfFileSource source, PdfReaderOptions? options, bool ownsSource)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= PdfReaderOptions.Default;

        var diagnostics = new PdfDiagnostics { Capacity = options.DiagnosticCapacity };
        var guard = new PdfLimitGuard(options.Limits, options.ThrowOnLimit, diagnostics);
        PdfFileReader reader;

        try
        {
            if (source.Length == 0)
            {
                throw new PdfFormatException("The input is empty.");
            }

            reader = new PdfFileReader(source, diagnostics, guard, options.ObjectCacheCapacity, ownsSource);
        }
        catch
        {
            // Opening failed before a reader existed to own the source — an empty input, or a guard reached
            // while indexing by a document opened to throw on one —, so the source is released here.
            if (ownsSource)
            {
                source.Dispose();
            }

            throw;
        }

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
    /// <exception cref="PdfLimitExceededException">
    /// Reading it reached one of <see cref="PdfReaderOptions.Limits"/>, and <see cref="PdfReaderOptions.ThrowOnLimit"/> is set.
    /// </exception>
    public PdfObject GetObject(PdfObjectId id)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _reader.GetObject(id);
    }

    /// <summary>
    /// Gets the object numbers of the entries <see cref="ObjectCount"/> counts, those the file's sections mark free
    /// included — object 0, the head of the free list, among them.
    /// </summary>
    public IEnumerable<int> ObjectNumbers => _reader.ObjectNumbers;

    /// <summary>Gets the bytes the document is read from, for the validation rules that look at the file itself.</summary>
    internal PdfFileSource Source
    {
        get
        {
            ThrowIfDisposed();
            return _reader.Source;
        }
    }

    /// <summary>
    /// Gets the reader behind the document, for the validation rules that judge the file's own structure — its
    /// sections, its index as the file wrote it — rather than its objects.
    /// </summary>
    internal PdfFileReader Reader
    {
        get
        {
            ThrowIfDisposed();
            return _reader;
        }
    }

    /// <summary>Throws if the document was disposed, for an operation that would otherwise fail later.</summary>
    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

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
