using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Documents;

/// <summary>
/// A PDF document opened for reading.
/// </summary>
/// <remarks>
/// Opening a document indexes it from its cross-reference sections, with the objects a cross-reference stream's
/// dictionary refers to for its data and its rows, and reads its catalog through the trailer's <c>/Root</c>, with what
/// its <c>/Type</c> and its <c>/Version</c> refer to. When
/// <c>/Root</c> leads to no catalog, opening loads the indexed objects one after another until one is a catalog, and
/// rebuilds the index when none is. A rebuild scans the whole file and loads every object written directly in it, to
/// take in those its object streams hold, then looks for the catalog the same way if <c>/Root</c> still leads to
/// none. It happens as the document opens when its sections cannot be read or lead to no catalog, and, as it opens or
/// later, when an object it reads is neither where its entry places it nor near it, or is missing from an index the
/// sections did not give whole, or when a validation starts and such a read could still happen: <see cref="WasRepaired"/>
/// can turn true after opening. Anything else is read when something asks for it. A document is not thread-safe: it
/// caches what it parses, so one document belongs to one thread at a time. Several documents can of course be processed
/// in parallel, and the reader holds no shared mutable state to make that unsafe.
/// <para>
/// Once disposed, a document answers nothing but its <see cref="Diagnostics"/>, the record of its reading, and
/// <see cref="Dispose"/>: every other member throws <see cref="ObjectDisposedException"/>, and so does whatever reaches
/// back into it from an object taken before — a reference resolved, through <see cref="PdfReference.Resolve"/> or an
/// accessor that resolves, a stream's data asked for —, however it was opened and whether or not it was read before.
/// What was handed out stays the caller's: a dictionary's direct values, a stream's <see cref="PdfStream.RawLength"/>,
/// every object's <c>ToString</c>. Disposing lets go of what the document kept: the objects it read, its index, and the
/// file, closed when it was opened from a path, and held no longer when it was opened from memory or a stream. A stream's
/// raw bytes taken before from a document held in memory are a view of that memory rather than a copy
/// (<see cref="PdfStream.GetRawBytes"/>), and so is what decoding gives back when no filter changes the data
/// (<see cref="PdfStreamDecodingExtensions.Decode"/>): for as long as they are held, they keep it alive, and they change
/// as it does.
/// </para>
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
    /// <remarks>
    /// <para>
    /// The record grows as the document is read, after opening too, and when a validation starts. An enumeration gives the
    /// entries recorded when it began, whatever a read or a validation it goes on through records meanwhile, which
    /// <see cref="PdfDiagnostics.Count"/> counts at once and the next enumeration gives (#167).
    /// </para>
    /// <para>
    /// It keeps answering once the document is disposed, as the final record of its reading: nothing adds to it then,
    /// since nothing can be read.
    /// </para>
    /// </remarks>
    public PdfDiagnostics Diagnostics { get; }

    /// <summary>Gets the document trailer.</summary>
    /// <exception cref="ObjectDisposedException">The document was disposed.</exception>
    public PdfDictionary Trailer
    {
        get
        {
            ThrowIfDisposed();
            return _reader.Trailer;
        }
    }

    /// <summary>Gets the document catalog, the root of the object graph.</summary>
    /// <exception cref="ObjectDisposedException">The document was disposed.</exception>
    public PdfDictionary? Catalog => Trailer.GetDictionary(PdfName.Root);

    /// <summary>
    /// Gets the version of PDF the document declares, such as <c>1.7</c>: its header's, or its catalog's
    /// <c>/Version</c> when that is later, as ISO 32000-1 (7.5.2, 7.7.2) has it; null when its header names none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A header names a version when <c>%PDF-</c> is followed by one digit, a period and one digit, and then by white
    /// space, a comment or the end of the file, within the first 4,096 bytes, where the reader looks for the header:
    /// <c>%PDF-1.4.5.6</c>, <c>%PDF-1.7abc</c> and <c>%PDF-14</c> name none, nor does a version those bytes end before
    /// what follows it, nor a file without a header, whatever its catalog says. A version so written that ISO 32000 does
    /// not define, such as <c>1.8</c>, is given as written. The catalog's <c>/Version</c> counts when it is a name written
    /// the same way; written as a reference, it is read as the document opens.
    /// </para>
    /// <para>
    /// It is worked out as the document opens, from the catalog it opens with. What the header says is judged by the
    /// validator's <c>file.header-missing</c> and <c>file.header-version-invalid</c> rules.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The document was disposed.</exception>
    public string? Version
    {
        get
        {
            ThrowIfDisposed();
            return _reader.Version;
        }
    }

    /// <summary>Gets a value indicating whether the cross-reference index had to be rebuilt.</summary>
    /// <remarks>
    /// The index is rebuilt as the document opens, or later, when something read asks for it: an object that is
    /// neither where the index places it nor near it, or one missing from an index the file's sections did not give
    /// whole. It can therefore turn true after the document has opened, as more of the document is read — or when a
    /// validation starts, which rebuilds the index before its first rule when a later read still could
    /// (<see cref="Validation.PdfValidator.Validate"/>).
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The document was disposed.</exception>
    public bool WasRepaired
    {
        get
        {
            ThrowIfDisposed();
            return _reader.WasRepaired;
        }
    }

    /// <summary>
    /// Gets the number of entries in the cross-reference index, one per object number: those in use, and those the
    /// file's sections mark free, object 0 — the head of the free list — among them. A rebuild empties the index,
    /// free entries included, and fills it with the objects its scan finds and those their object streams hold.
    /// </summary>
    /// <remarks>
    /// It can therefore change after opening, on a read or at the start of a validation that rebuilds the index
    /// (<see cref="WasRepaired"/>).
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The document was disposed.</exception>
    public int ObjectCount
    {
        get
        {
            ThrowIfDisposed();
            return _reader.ObjectCount;
        }
    }

    /// <summary>Gets a value indicating whether the document is encrypted.</summary>
    /// <exception cref="ObjectDisposedException">The document was disposed.</exception>
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

    /// <summary>
    /// Opens a document from a stream: from its start when it can seek, whatever its position, and from where it stands
    /// when it cannot.
    /// </summary>
    /// <remarks>
    /// A <see cref="MemoryStream"/> itself that exposes its buffer — one written to after <c>new MemoryStream()</c> or
    /// <c>new MemoryStream(capacity)</c>, or built with <c>publiclyVisible: true</c> — is read in that buffer, without a
    /// copy. Any other stream, a seekable one or a type derived from <see cref="MemoryStream"/> included, is copied into
    /// memory as the document opens, into an array of its length when it can seek: to read a file without holding it,
    /// open it from its path.
    /// </remarks>
    /// <param name="stream">
    /// The stream that holds the file. It is the document's until the document is disposed: neither write to it, move
    /// it, nor dispose of it meanwhile. The document does not dispose of it, and leaves its position unspecified.
    /// </param>
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
            reader.Close();
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
    /// <exception cref="ObjectDisposedException">The document was disposed.</exception>
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
    /// <remarks>
    /// <para>
    /// An enumeration gives the numbers the index held when it began, at its first step, in the order the index recorded
    /// them, whatever a read or a validation it goes on through does to the index meanwhile: a rebuild changes what the
    /// next enumeration gives, not this one, which never throws for it (#167). Nothing is copied; a rebuild during an
    /// enumeration records the rebuilt index's numbers in a new array, 4 bytes for each entry the index has room for.
    /// </para>
    /// <para>An enumeration begun before the document is disposed is refused at its next step after.</para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The document was disposed.</exception>
    public IEnumerable<int> ObjectNumbers
    {
        get
        {
            ThrowIfDisposed();
            return Numbers();

            IEnumerable<int> Numbers()
            {
                using var numbers = _reader.ObjectNumbers.GetEnumerator();

                while (true)
                {
                    ThrowIfDisposed();

                    if (!numbers.MoveNext())
                    {
                        yield break;
                    }

                    yield return numbers.Current;
                }
            }
        }
    }

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
        _reader.Close();
    }
}
