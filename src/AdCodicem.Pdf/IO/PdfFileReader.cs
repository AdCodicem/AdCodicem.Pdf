using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.ExceptionServices;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO.XRef;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO;

/// <summary>
/// Indexes a PDF file and reads its objects on demand.
/// </summary>
/// <remarks>
/// Opening a document reads the cross-reference chain and nothing else. Objects are parsed the first time
/// something asks for them and kept in a bounded cache, so memory follows what the caller touches rather
/// than the size of the file. When the index turns out to be wrong — which real files manage in a
/// remarkable number of ways — the reader rebuilds it by scanning, and says so in the diagnostics.
/// </remarks>
internal sealed class PdfFileReader : IPdfObjectSource, IPdfStreamDataProvider, IDisposable
{
    internal const int InitialObjectWindow = 8 * 1024;
    internal const int XRefWindow = 64 * 1024;

    /// <summary>
    /// Most entries a subsection may claim. Not a guard a valid file reaches: a classic table holds its rows
    /// within <see cref="PdfReaderLimits.MaxXRefSectionLength"/>, a stream within its decoded length, and a
    /// count past both describes rows that are not there.
    /// </summary>
    private const int MaxSubsectionEntries = 50_000_000;
    private const int HeaderSearchLength = 4096;
    private const int TailSearchLength = 4096;
    private const int NearbySearchRadius = 512;
    private const int ScanChunkSize = 1024 * 1024;
    private const int ScanOverlap = 64;
    private const int MaxRepairObjects = 2_000_000;

    /// <summary>Asks <see cref="TryParseAt"/> for a direct object, with no object header: a trailer.</summary>
    private const int DirectObject = -1;

    /// <summary>Asks <see cref="TryParseAt"/> for an object whatever its number: a cross-reference stream.</summary>
    private const int AnyObject = 0;

    /// <summary>
    /// Deepest chain of objects loaded while loading another. Real documents stay within a handful — a stream
    /// in an object stream whose <c>/Length</c> is indirect is three —; each level costs a dozen frames.
    /// </summary>
    private const int MaxNestedLoads = 64;

    private static ReadOnlySpan<byte> ObjKeyword => "obj"u8;
    private static ReadOnlySpan<byte> TrailerKeyword => "trailer"u8;
    private static ReadOnlySpan<byte> StartXRefKeyword => "startxref"u8;
    private static ReadOnlySpan<byte> HeaderMarker => "%PDF-"u8;

    private readonly PdfFileSource _source;
    private readonly PdfDiagnostics _diagnostics;
    private readonly PdfLimitGuard _guard;

    /// <summary>The guards already reported, and where: an object read again is not reported again.</summary>
    private readonly HashSet<(PdfLimit Limit, long Position)> _limitsReached = [];

    /// <summary>
    /// What parses through a window report until the window is known to have been large enough. Shared by
    /// nested loads, each of which keeps or drops only what it recorded after its own mark.
    /// </summary>
    private readonly PdfDiagnostics _pending;

    private readonly PdfXRefTable _xref = new();
    private readonly Dictionary<PdfObjectId, PdfObject> _cache = [];
    private readonly Queue<PdfObjectId> _cacheOrder = new();
    private readonly Dictionary<int, ObjectStreamContents?> _objectStreams = [];
    private readonly HashSet<PdfObjectId> _loading = [];
    private readonly int _cacheCapacity;
    private readonly bool _ownsSource;

    private long _headerOffset;
    private bool _repaired;
    private bool _nestingReported;

    public PdfFileReader(
        PdfFileSource source, PdfDiagnostics diagnostics, PdfLimitGuard guard, int cacheCapacity, bool ownsSource)
    {
        _source = source;
        _diagnostics = diagnostics;
        _guard = guard;
        _pending = new PdfDiagnostics { Capacity = diagnostics.Capacity };
        _cacheCapacity = Math.Max(64, cacheCapacity);
        _ownsSource = ownsSource;

        Initialize();
    }

    /// <summary>Gets the trailer of the document, merged across the whole cross-reference chain.</summary>
    public PdfDictionary Trailer => _xref.Trailer;

    /// <summary>Gets the version declared by the file header, such as <c>1.7</c>.</summary>
    public string Version { get; private set; } = "1.4";

    /// <summary>Gets a value indicating whether the index had to be rebuilt.</summary>
    public bool WasRepaired => _repaired;

    /// <summary>Gets the number of indexed objects.</summary>
    public int ObjectCount => _xref.Count;

    /// <summary>Gets the object numbers the file defines.</summary>
    public IEnumerable<int> ObjectNumbers => _xref.Entries.Keys;

    /// <inheritdoc/>
    public PdfObject GetObject(PdfObjectId id)
    {
        if (id.Number <= 0)
        {
            return PdfNull.Instance;
        }

        if (_cache.TryGetValue(id, out var cached))
        {
            return cached;
        }

        // A file can make an object's length depend on the object itself. Refusing to re-enter turns an
        // infinite recursion into a null.
        if (!_loading.Add(id))
        {
            return PdfNull.Instance;
        }

        try
        {
            // Loading an object can load another — an indirect /Length is resolved while its stream is
            // parsed — and a file can chain such objects as long as it likes, each a level deeper on the
            // stack. Past a depth no real document comes near, the next one reads as null rather than taking
            // the process with it. Nothing is cached, so the same object loaded from a shallower place reads
            // normally.
            if (_loading.Count > MaxNestedLoads)
            {
                ReportNestingTooDeep(id);
                return PdfNull.Instance;
            }

            var value = LoadObject(id);
            Cache(id, value);
            return value;
        }
        finally
        {
            _loading.Remove(id);
        }
    }

    private void ReportNestingTooDeep(PdfObjectId id)
    {
        // Once is enough: the same chain is met again each time an attempt that reached it is repeated.
        if (_nestingReported)
        {
            return;
        }

        _nestingReported = true;
        var position = _xref.TryGet(id.Number, out var entry) && entry.Kind == XRefEntryKind.Regular
            ? entry.Offset + _headerOffset
            : -1;

        _diagnostics.Warn(
            PdfDiagnosticCodes.SyntaxDepthExceeded,
            $"Object {id.Number} is reached through more nested objects than the reader will follow, and reads as null.",
            position);
    }

    /// <inheritdoc/>
    long IPdfStreamDataProvider.SourceLength => _source.Length;

    /// <inheritdoc/>
    PdfStreamData IPdfStreamDataProvider.Create(long absoluteOffset, int length)
    {
        // A declared length is a claim made by the file, so it is clamped to what the file can hold.
        // Without this, "/Length 2147483647" would ask for two gigabytes of memory.
        var available = (int)Math.Clamp(_source.Length - absoluteOffset, 0, int.MaxValue);
        return new FileStreamData(_source, absoluteOffset, Math.Clamp(length, 0, available), _guard);
    }

    /// <inheritdoc/>
    bool IPdfStreamDataProvider.IsEndStreamAt(long absoluteOffset)
    {
        // A source is only asked for bytes it has, as GetWindow does: a subclass need not accept an offset
        // or a length that runs past its end.
        if (absoluteOffset < 0 || absoluteOffset >= _source.Length)
        {
            return false;
        }

        Span<byte> tail = stackalloc byte[PdfObjectParser.EndStreamLookahead];
        var available = (int)Math.Min(tail.Length, _source.Length - absoluteOffset);
        var read = _source.Read(absoluteOffset, tail[..available]);
        return PdfObjectParser.IsEndStreamAt(tail[..read], 0);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsSource)
        {
            _source.Dispose();
        }
    }

    private void Initialize()
    {
        DetectHeader();

        var startXref = FindStartXRef();
        var indexed = startXref >= 0 && TryReadXRefChain(startXref);

        if (!indexed || !HasUsableRoot())
        {
            Repair();
        }
    }

    private void DetectHeader()
    {
        using var window = _source.GetWindow(0, (int)Math.Min(HeaderSearchLength, _source.Length));
        var span = window.Memory.Span;
        var index = span.IndexOf(HeaderMarker);

        if (index < 0)
        {
            _diagnostics.Warn(PdfDiagnosticCodes.XRefRebuilt, "The file does not start with a PDF header.");
            return;
        }

        if (index > 0)
        {
            // Bytes before the header shift every offset in the file by the same amount.
            _headerOffset = index;
            _diagnostics.Repair(
                PdfDiagnosticCodes.XRefOffsetAdjusted,
                $"The PDF header starts {index} bytes into the file; offsets were shifted accordingly.",
                index);
        }

        var versionStart = index + HeaderMarker.Length;
        var versionEnd = versionStart;
        while (versionEnd < span.Length && versionEnd - versionStart < 8 &&
               (PdfCharacters.IsDigit(span[versionEnd]) || span[versionEnd] == (byte)'.'))
        {
            versionEnd++;
        }

        if (versionEnd > versionStart)
        {
            Version = System.Text.Encoding.ASCII.GetString(span[versionStart..versionEnd]);
        }
    }

    private long FindStartXRef()
    {
        var length = (int)Math.Min(TailSearchLength, _source.Length);
        if (length <= 0)
        {
            return -1;
        }

        using var window = _source.GetWindow(_source.Length - length, length);
        var span = window.Memory.Span;
        var index = span.LastIndexOf(StartXRefKeyword);

        if (index < 0)
        {
            return -1;
        }

        var lexer = new PdfLexer(span, index + StartXRefKeyword.Length);
        var token = lexer.Read();

        return token.Kind == PdfTokenKind.Integer && token.Integer >= 0 ? token.Integer : -1;
    }

    private bool TryReadXRefChain(long startOffset)
    {
        var visited = new HashSet<long>();
        var offset = startOffset;
        var sections = 0;

        while (offset >= 0)
        {
            if (!visited.Add(offset))
            {
                _diagnostics.Warn(PdfDiagnosticCodes.XRefChainCycle, "The cross-reference chain loops back on itself.", offset);
                break;
            }

            if (++sections > _guard.Bound(PdfLimit.XRefSectionCount))
            {
                // Each incremental save adds a section, so a long chain is a sound file saved often; the newest
                // sections, read first, are the ones that win.
                ReachLimit(
                    PdfLimit.XRefSectionCount,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The cross-reference chain has more than {_guard.Bound(PdfLimit.XRefSectionCount):N0} sections; the older ones were not read."),
                    offset + _headerOffset);
                break;
            }

            if (!TryReadXRefSection(offset, out var previous, out var hybrid))
            {
                return sections > 1;
            }

            if (hybrid >= 0 && visited.Add(hybrid))
            {
                // A hybrid-reference file keeps a classic table for old readers and a stream for the rest.
                TryReadXRefSection(hybrid, out _, out _);
            }

            offset = previous;
        }

        return _xref.Count > 0;
    }

    private bool TryReadXRefSection(long offset, out long previous, out long hybrid)
    {
        previous = -1;
        hybrid = -1;

        var absolute = offset + _headerOffset;
        if (absolute < 0 || absolute >= _source.Length)
        {
            _diagnostics.Warn(PdfDiagnosticCodes.XRefEntryOutOfRange, "A cross-reference section points outside the file.", absolute);
            return false;
        }

        using (var probe = _source.GetWindow(absolute, 32))
        {
            var lexer = new PdfLexer(probe.Memory.Span);
            if (lexer.Read().IsKeyword("xref"u8))
            {
                return TryReadClassicTable(absolute, out previous, out hybrid);
            }
        }

        return TryReadXRefStream(absolute, out previous);
    }

    private bool TryReadClassicTable(long absolute, out long previous, out long hybrid)
    {
        previous = -1;
        hybrid = -1;
        var maxWindow = _guard.Bound(PdfLimit.XRefSectionLength);
        var windowSize = Math.Min(XRefWindow, maxWindow);

        while (true)
        {
            using var window = _source.GetWindow(absolute, windowSize);
            var span = window.Memory.Span;
            var lexer = new PdfLexer(span);

            // A window shorter than asked holds the end of the file: what it holds is all there is. A full one
            // may have cut the table, and is grown until the guard allows no more.
            var windowFull = window.Length == windowSize;
            var canGrow = windowFull && windowSize < maxWindow;
            var keyword = lexer.Read();
            var truncated = windowFull && keyword.End >= span.Length;

            if (!truncated && !keyword.IsKeyword("xref"u8))
            {
                return false;
            }

            while (!truncated)
            {
                var token = lexer.Read();

                // A token that reaches the window's edge may have been cut by it — "trai" of "trailer", "32"
                // of "3270" — so it is read again in a larger window rather than taken for what it seems.
                if (token.Kind == PdfTokenKind.EndOfInput || (windowFull && token.End >= span.Length))
                {
                    break;
                }

                if (token.IsKeyword(TrailerKeyword))
                {
                    if (ReadTrailer(window.Memory, absolute, lexer.Position, windowFull) is { } trailer)
                    {
                        _xref.MergeTrailer(trailer);
                        previous = trailer.GetInteger(PdfName.Prev) ?? -1;
                        hybrid = trailer.GetInteger(PdfName.XRefStm) ?? -1;
                    }

                    return true;
                }

                if (token.Kind != PdfTokenKind.Integer)
                {
                    return false;
                }

                var first = token.Integer;
                var countToken = lexer.Read();

                if (countToken.Kind == PdfTokenKind.EndOfInput || (windowFull && countToken.End >= span.Length))
                {
                    break;
                }

                if (countToken.Kind != PdfTokenKind.Integer ||
                    countToken.Integer is < 0 or > MaxSubsectionEntries ||
                    first is < 0 or > int.MaxValue)
                {
                    return false;
                }

                truncated = !ReadSubsection(ref lexer, (int)first, (int)countToken.Integer);
            }

            if (!canGrow)
            {
                if (windowFull)
                {
                    ReachLimit(
                        PdfLimit.XRefSectionLength,
                        $"The cross-reference table runs past {PdfLimitGuard.FormatLength(maxWindow)}; only the entries within it were read.",
                        absolute);
                }

                // Keep whatever was indexed.
                return _xref.Count > 0;
            }

            windowSize = (int)Math.Min((long)windowSize * 4, maxWindow);
        }
    }

    /// <summary>
    /// Parses the trailer dictionary that starts at <paramref name="position"/> in a classic table's window.
    /// </summary>
    /// <remarks>
    /// A trailer cut by the window's edge would lose its /Root or its /Prev. It is parsed again where it
    /// starts, through a window of its own that stops at <see cref="PdfReaderLimits.MaxTrailerLength"/> — not
    /// by growing the table's, which a dictionary that never closes would grow to the size of the file, for
    /// every section of a chain.
    /// </remarks>
    private PdfDictionary? ReadTrailer(ReadOnlyMemory<byte> window, long absolute, int position, bool windowFull)
    {
        var mark = _pending.GetMark();

        try
        {
            var parser = new PdfObjectParser(window, absolute, this, _pending, this);
            parser.Position = position;
            var parsed = parser.ParseObject();

            if (!parser.IsTruncated || !windowFull)
            {
                _pending.MoveTo(_diagnostics, mark);
                return parsed.AsDictionary();
            }
        }
        finally
        {
            _pending.RollBack(mark);
        }

        // The trailer starts inside the window, so inside the file: a direct object is always parsed there,
        // and one that is not a dictionary is no trailer.
        _ = TryParseAt(absolute + position, DirectObject, PdfLimit.Trailer, out var value);
        return value.AsDictionary();
    }

    private bool ReadSubsection(ref PdfLexer lexer, int first, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var offsetToken = lexer.Read();
            var generationToken = lexer.Read();
            var kindToken = lexer.Read();

            if (offsetToken.Kind == PdfTokenKind.EndOfInput ||
                generationToken.Kind == PdfTokenKind.EndOfInput ||
                kindToken.Kind == PdfTokenKind.EndOfInput)
            {
                return false;
            }

            if (offsetToken.Kind != PdfTokenKind.Integer || generationToken.Kind != PdfTokenKind.Integer)
            {
                // A malformed row: stop this subsection rather than misread every row after it.
                return true;
            }

            var number = first + i;

            if (kindToken.IsKeyword("n"u8))
            {
                _xref.TryAdd(number, XRefEntry.Regular(offsetToken.Integer, (int)generationToken.Integer));
            }
            else if (kindToken.IsKeyword("f"u8))
            {
                _xref.TryAdd(number, XRefEntry.Free);
            }
            else
            {
                return true;
            }
        }

        return true;
    }

    private bool TryReadXRefStream(long absolute, out long previous)
    {
        previous = -1;

        // The first window, of 64 KB, holds the data of all but the largest cross-reference streams, so the
        // parser checks their /Length against the data rather than only at the end the /Length gives. The
        // dictionary is the trailer, and grows its window no further than a trailer may.
        if (!TryParseAt(absolute, AnyObject, PdfLimit.Trailer, out var value, XRefWindow) || value is not PdfStream stream)
        {
            return false;
        }

        var dictionary = stream.Dictionary;
        var widths = dictionary.GetArray(PdfName.W);

        if (widths is null || widths.Count < 3)
        {
            return false;
        }

        Span<int> fieldWidths = stackalloc int[3];
        var rowLength = 0;

        for (var i = 0; i < 3; i++)
        {
            var width = (int)(widths.Resolved(i).AsInteger() ?? 0);
            if (width is < 0 or > 8)
            {
                return false;
            }

            fieldWidths[i] = width;
            rowLength += width;
        }

        if (rowLength == 0)
        {
            return false;
        }

        var data = stream.Decode(_diagnostics).Span;
        var size = (int)dictionary.GetInteger(PdfName.Size, 0);
        var ranges = dictionary.GetArray(PdfName.Index);
        var position = 0;

        if (ranges is null)
        {
            ReadXRefStreamRows(data, ref position, fieldWidths, rowLength, 0, size);
        }
        else
        {
            for (var i = 0; i + 1 < ranges.Count; i += 2)
            {
                var start = (int)(ranges.Resolved(i).AsInteger() ?? 0);
                var count = (int)(ranges.Resolved(i + 1).AsInteger() ?? 0);

                if (count is < 0 or > MaxSubsectionEntries)
                {
                    break;
                }

                ReadXRefStreamRows(data, ref position, fieldWidths, rowLength, start, count);
            }
        }

        _xref.MergeTrailer(dictionary);
        previous = dictionary.GetInteger(PdfName.Prev) ?? -1;
        return true;
    }

    private void ReadXRefStreamRows(
        ReadOnlySpan<byte> data,
        ref int position,
        ReadOnlySpan<int> widths,
        int rowLength,
        int first,
        int count)
    {
        for (var i = 0; i < count && position + rowLength <= data.Length; i++, position += rowLength)
        {
            var row = data.Slice(position, rowLength);
            var cursor = 0;

            // A zero-width type field means the type is 1: an ordinary object at an offset.
            var type = widths[0] == 0 ? 1 : (int)ReadField(row, ref cursor, widths[0]);
            var second = ReadField(row, ref cursor, widths[1]);
            var third = ReadField(row, ref cursor, widths[2]);
            var number = first + i;

            switch (type)
            {
                case 0:
                    _xref.TryAdd(number, XRefEntry.Free);
                    break;

                case 1:
                    _xref.TryAdd(number, XRefEntry.Regular((long)second, (int)third));
                    break;

                case 2:
                    _xref.TryAdd(number, XRefEntry.Compressed((int)second, (int)third));
                    break;

                default:
                    // Types beyond 2 are reserved; the specification says to ignore the entry.
                    break;
            }
        }
    }

    private static ulong ReadField(ReadOnlySpan<byte> row, ref int cursor, int width)
    {
        ulong value = 0;

        for (var i = 0; i < width; i++)
        {
            value = (value << 8) | row[cursor + i];
        }

        cursor += width;
        return value;
    }

    private bool HasUsableRoot()
    {
        var root = Trailer.GetDictionary(PdfName.Root);
        return root is not null && (root.IsOfType(PdfName.Catalog) || root.ContainsKey(PdfName.Pages));
    }

    private PdfObject LoadObject(PdfObjectId id)
    {
        if (!_xref.TryGet(id.Number, out var entry))
        {
            if (_repaired)
            {
                return PdfNull.Instance;
            }

            Repair();
            return _xref.TryGet(id.Number, out entry) ? LoadFromEntry(id, entry) : PdfNull.Instance;
        }

        return LoadFromEntry(id, entry);
    }

    private PdfObject LoadFromEntry(PdfObjectId id, XRefEntry entry) => entry.Kind switch
    {
        XRefEntryKind.Regular => LoadRegularObject(id, entry.Offset + _headerOffset),
        XRefEntryKind.Compressed => LoadCompressedObject(id, entry),
        _ => PdfNull.Instance,
    };

    /// <summary>
    /// Loads an object the index places at <paramref name="offset"/>, in at most three attempts: where the
    /// index says, in the neighbourhood, and wherever a rebuilt index says.
    /// </summary>
    /// <remarks>
    /// The attempts are counted rather than chained. An earlier version let relocation call back into
    /// loading, and a fuzzed file drove the two into each other until the stack ran out — a file killing
    /// the process is the exact outcome the reader exists to prevent.
    /// </remarks>
    private PdfObject LoadRegularObject(PdfObjectId id, long offset)
    {
        if (offset < 0 || offset >= _source.Length)
        {
            _diagnostics.Warn(
                PdfDiagnosticCodes.XRefEntryOutOfRange, $"Object {id.Number} points outside the file.", offset);
        }
        else if (TryParseObjectAt(id.Number, offset, out var atRecordedOffset))
        {
            return atRecordedOffset;
        }

        // Offsets are commonly off by a few bytes in files from careless tools, so the neighbourhood is
        // searched before the index is given up on entirely.
        if (TryFindObjectHeader(id.Number, offset, out var nearby) &&
            TryParseObjectAt(id.Number, nearby, out var relocated))
        {
            _diagnostics.Repair(
                PdfDiagnosticCodes.XRefOffsetAdjusted,
                $"Object {id.Number} was found {nearby - offset} bytes from where the index said.",
                nearby);

            _xref.Set(id.Number, XRefEntry.Regular(nearby - _headerOffset, id.Generation));
            return relocated;
        }

        if (_repaired)
        {
            return PdfNull.Instance;
        }

        Repair();

        return _xref.TryGet(id.Number, out var entry) &&
               entry.Kind == XRefEntryKind.Regular &&
               TryParseObjectAt(id.Number, entry.Offset + _headerOffset, out var afterRebuild)
            ? afterRebuild
            : PdfNull.Instance;
    }

    /// <summary>
    /// Parses object <paramref name="number"/> at an exact offset, growing the window while the object runs
    /// past its end. Returns false when nothing with that number is there.
    /// </summary>
    private bool TryParseObjectAt(int number, long offset, out PdfObject value) =>
        TryParseAt(offset, number, PdfLimit.Object, out value);

    /// <summary>
    /// Parses what starts at an exact offset — object <paramref name="number"/>, any object for
    /// <see cref="AnyObject"/>, or a direct object for <see cref="DirectObject"/> —, growing the window while it
    /// runs past its end, up to the bound of <paramref name="limit"/>. Returns false when nothing with the
    /// expected number is there.
    /// </summary>
    /// <remarks>
    /// What the parser notices is held back until an attempt is kept. An attempt that ran out of window
    /// sees a cut token, a missing <c>endstream</c> or an object that stops mid-way: artefacts of the window,
    /// not of the file, and reporting them would put a syntax error the file does not have in the document's
    /// diagnostics — with the real ones reported once per attempt. What nested loads report, such as a
    /// relocated <c>/Length</c> object, goes straight to the document's diagnostics and stays there. An object
    /// that still runs past the window once the guard allows no larger one is kept as far as it was read, and
    /// the guard is reported in place of what the cut made the parser notice.
    /// </remarks>
    private bool TryParseAt(long offset, int number, PdfLimit limit, out PdfObject value, int initialWindow = InitialObjectWindow)
    {
        value = PdfNull.Instance;

        if (offset < 0 || offset >= _source.Length)
        {
            return false;
        }

        var maxWindow = _guard.Bound(limit);
        var windowSize = Math.Min(initialWindow, maxWindow);
        var mark = _pending.GetMark();

        try
        {
            while (true)
            {
                using var window = _source.GetWindow(offset, windowSize);
                var parser = new PdfObjectParser(window.Memory, offset, this, _pending, this);
                var cut = window.Length == windowSize;
                PdfObject parsed;

                if (number == DirectObject)
                {
                    parsed = parser.ParseObject();
                }
                else if (!parser.TryReadIndirectObject(out var found, out parsed))
                {
                    // An object header the window's edge cut is read again in a larger one; anything else
                    // that is not an object header is not the object.
                    if (!parser.IsTruncated || !cut)
                    {
                        return false;
                    }

                    _pending.RollBack(mark);

                    if (windowSize >= maxWindow)
                    {
                        ReachLimit(limit, LimitSubject(number, maxWindow), offset);
                        return false;
                    }

                    windowSize = (int)Math.Min((long)windowSize * 8, maxWindow);
                    continue;
                }
                else if (number != AnyObject && found.Number != number)
                {
                    return false;
                }

                if (parser.IsTruncated && cut)
                {
                    _pending.RollBack(mark);

                    if (windowSize < maxWindow)
                    {
                        windowSize = (int)Math.Min((long)windowSize * 8, maxWindow);
                        continue;
                    }

                    ReachLimit(limit, LimitSubject(number, maxWindow), offset);
                }

                _pending.MoveTo(_diagnostics, mark);
                value = parsed;
                return true;
            }
        }
        finally
        {
            // Whatever an attempt that was not kept noticed is dropped with it, however it ended.
            _pending.RollBack(mark);
        }
    }

    private static string LimitSubject(int number, int bound) => number switch
    {
        DirectObject => $"A trailer runs past {PdfLimitGuard.FormatLength(bound)}; only what lies within it was read.",
        AnyObject => $"A cross-reference stream's dictionary runs past {PdfLimitGuard.FormatLength(bound)}; only what lies within it was read.",
        _ => $"Object {number} runs past {PdfLimitGuard.FormatLength(bound)}, its stream data aside; only what lies within it was read.",
    };

    /// <summary>
    /// Reports a guard reached at <paramref name="position"/>, once: an object read again, after the cache
    /// let it go, reaches it again. A document that throws on a guard throws each time.
    /// </summary>
    private void ReachLimit(PdfLimit limit, string what, long position)
    {
        if (!_limitsReached.Contains((limit, position)))
        {
            _guard.Reach(limit, _diagnostics, what, position);
            _limitsReached.Add((limit, position));
        }
    }

    private bool TryFindObjectHeader(int number, long approximateOffset, out long actualOffset)
    {
        actualOffset = -1;

        var start = Math.Max(0, approximateOffset - NearbySearchRadius);
        var length = (int)Math.Min(NearbySearchRadius * 2, _source.Length - start);

        if (length <= 0)
        {
            return false;
        }

        using var window = _source.GetWindow(start, length);
        var span = window.Memory.Span;
        var searchFrom = 0;

        while (searchFrom < span.Length)
        {
            var index = span[searchFrom..].IndexOf(ObjKeyword);
            if (index < 0)
            {
                return false;
            }

            var position = searchFrom + index;

            if (TryReadHeaderBackwards(span, position, out var found, out var headerStart) && found == number)
            {
                actualOffset = start + headerStart;
                return true;
            }

            searchFrom = position + ObjKeyword.Length;
        }

        return false;
    }

    /// <summary>
    /// Reads the <c>N G</c> that precedes an <c>obj</c> keyword. Walking backwards is what distinguishes a
    /// real object header from the <c>obj</c> inside <c>endobj</c>.
    /// </summary>
    private static bool TryReadHeaderBackwards(ReadOnlySpan<byte> span, int objPosition, out int number, out int headerStart)
    {
        number = 0;
        headerStart = 0;

        var index = objPosition - 1;

        if (index < 0 || !PdfCharacters.IsWhitespace(span[index]))
        {
            return false;
        }

        while (index >= 0 && PdfCharacters.IsWhitespace(span[index]))
        {
            index--;
        }

        var generationEnd = index + 1;
        while (index >= 0 && PdfCharacters.IsDigit(span[index]))
        {
            index--;
        }

        var generationStart = index + 1;
        if (generationStart == generationEnd)
        {
            return false;
        }

        if (index < 0 || !PdfCharacters.IsWhitespace(span[index]))
        {
            return false;
        }

        while (index >= 0 && PdfCharacters.IsWhitespace(span[index]))
        {
            index--;
        }

        var numberEnd = index + 1;
        while (index >= 0 && PdfCharacters.IsDigit(span[index]))
        {
            index--;
        }

        var numberStart = index + 1;
        if (numberStart == numberEnd || numberEnd - numberStart > 10)
        {
            return false;
        }

        if (!PdfNumberParser.TryParse(span[numberStart..numberEnd], out var parsed, out _, out var isReal) ||
            isReal || parsed is <= 0 or > int.MaxValue)
        {
            return false;
        }

        number = (int)parsed;
        headerStart = numberStart;
        return true;
    }

    private PdfObject LoadCompressedObject(PdfObjectId id, XRefEntry entry)
    {
        var contents = GetObjectStream(entry.ObjectStreamNumber);

        if (contents is null)
        {
            return PdfNull.Instance;
        }

        return contents.Parse(entry.IndexInObjectStream, id.Number, this, _diagnostics);
    }

    private ObjectStreamContents? GetObjectStream(int number)
    {
        if (_objectStreams.TryGetValue(number, out var cached))
        {
            return cached;
        }

        // Recorded before loading: an object stream that contains itself would otherwise recurse.
        _objectStreams[number] = null;

        var contents = GetObject(new PdfObjectId(number)) is PdfStream stream
            ? ObjectStreamContents.TryCreate(stream, _diagnostics)
            : null;

        _objectStreams[number] = contents;
        return contents;
    }

    private void Cache(PdfObjectId id, PdfObject value)
    {
        if (_cache.Count >= _cacheCapacity && _cacheOrder.TryDequeue(out var oldest))
        {
            _cache.Remove(oldest);
        }

        if (_cache.TryAdd(id, value))
        {
            _cacheOrder.Enqueue(id);
        }
    }

    /// <summary>
    /// Rebuilds the index by scanning the whole file for object headers, keeping the last definition of
    /// each object number, and then looking for a trailer and for anything that can act as one.
    /// </summary>
    private void Repair()
    {
        if (_repaired)
        {
            return;
        }

        _repaired = true;
        _diagnostics.Repair(PdfDiagnosticCodes.XRefRebuilt, "The cross-reference index was rebuilt by scanning the file.");

        _xref.Clear();
        _cache.Clear();
        _cacheOrder.Clear();
        _objectStreams.Clear();

        ScanForObjects();
        ScanForTrailers();

        // Loading objects can reach a guard. A document opened to throw on one throws once the index is
        // whole, not half-way through rebuilding it: a rebuild is never run twice, and one abandoned mid-way
        // would leave the document unable to find the objects it had not reached.
        var reached = ExpandObjectStreams();

        if (!HasUsableRoot())
        {
            // Searched even when the expansion reached a guard; the first guard reached is the one thrown.
            var searching = FindCatalog();
            reached ??= searching;
        }

        reached?.Throw();
    }

    private void ScanForObjects()
    {
        var position = 0L;
        var found = 0;

        while (position < _source.Length && found < MaxRepairObjects)
        {
            var length = (int)Math.Min(ScanChunkSize, _source.Length - position);
            using var window = _source.GetWindow(position, length);
            var span = window.Memory.Span;
            var searchFrom = 0;

            while (searchFrom < span.Length)
            {
                var index = span[searchFrom..].IndexOf(ObjKeyword);
                if (index < 0)
                {
                    break;
                }

                var objPosition = searchFrom + index;

                if (TryReadHeaderBackwards(span, objPosition, out var number, out var headerStart))
                {
                    // The last definition wins: that is what an incrementally updated file means.
                    _xref.Set(number, XRefEntry.Regular(position + headerStart - _headerOffset, 0));
                    found++;
                }

                searchFrom = objPosition + ObjKeyword.Length;
            }

            if (position + length >= _source.Length)
            {
                break;
            }

            position += length - ScanOverlap;
        }
    }

    private void ScanForTrailers()
    {
        var positions = new List<long>();
        var position = 0L;

        while (position < _source.Length)
        {
            var length = (int)Math.Min(ScanChunkSize, _source.Length - position);
            using var window = _source.GetWindow(position, length);
            var span = window.Memory.Span;
            var searchFrom = 0;

            while (searchFrom < span.Length)
            {
                var index = span[searchFrom..].IndexOf(TrailerKeyword);
                if (index < 0)
                {
                    break;
                }

                positions.Add(position + searchFrom + index + TrailerKeyword.Length);
                searchFrom += index + TrailerKeyword.Length;
            }

            if (position + length >= _source.Length)
            {
                break;
            }

            position += length - ScanOverlap;
        }

        // The newest trailer is the last one in the file, and its keys must win.
        for (var i = positions.Count - 1; i >= 0; i--)
        {
            using var window = _source.GetWindow(positions[i], XRefWindow);
            var parser = new PdfObjectParser(window.Memory, positions[i], this, _diagnostics, this);

            if (parser.ParseObject().AsDictionary() is { } trailer)
            {
                _xref.MergeTrailer(trailer);
            }
        }
    }

    private ExceptionDispatchInfo? ExpandObjectStreams()
    {
        var numbers = new List<int>(_xref.Entries.Keys);
        ExceptionDispatchInfo? reached = null;

        foreach (var number in numbers)
        {
            if (!_xref.TryGet(number, out var entry) || entry.Kind != XRefEntryKind.Regular)
            {
                continue;
            }

            ObjectStreamContents? contents;

            try
            {
                if (GetObject(new PdfObjectId(number)) is not PdfStream stream ||
                    !stream.Dictionary.IsOfType(PdfName.ObjStm))
                {
                    continue;
                }

                contents = ObjectStreamContents.TryCreate(stream, _diagnostics);
            }
            catch (PdfLimitExceededException exception)
            {
                reached ??= ExceptionDispatchInfo.Capture(exception);
                continue;
            }

            if (contents is null)
            {
                continue;
            }

            _objectStreams[number] = contents;

            for (var index = 0; index < contents.Count; index++)
            {
                // An object written directly in the file wins over a copy inside an object stream.
                _xref.TryAdd(contents.NumberAt(index), XRefEntry.Compressed(number, index));
            }
        }

        return reached;
    }

    private ExceptionDispatchInfo? FindCatalog()
    {
        ExceptionDispatchInfo? reached = null;

        foreach (var number in _xref.Entries.Keys)
        {
            PdfObject candidate;

            try
            {
                candidate = GetObject(new PdfObjectId(number));
            }
            catch (PdfLimitExceededException exception)
            {
                reached ??= ExceptionDispatchInfo.Capture(exception);
                continue;
            }

            if (candidate.AsDictionary() is { } dictionary && dictionary.IsOfType(PdfName.Catalog))
            {
                Trailer.Set(PdfName.Root, new PdfReference(new PdfObjectId(number), this));
                _diagnostics.Repair(PdfDiagnosticCodes.XRefRebuilt, $"The document catalogue was found as object {number}.");
                break;
            }
        }

        return reached;
    }

    private sealed class FileStreamData(PdfFileSource source, long offset, int length, PdfLimitGuard guard) : PdfStreamData
    {
        private byte[]? _bytes;

        public override int Length => length;

        public override long Position => offset;

        internal override PdfLimitGuard LimitGuard => guard;

        public override ReadOnlyMemory<byte> GetBytes()
        {
            if (_bytes is not null)
            {
                return _bytes;
            }

            var buffer = new byte[length];
            var read = source.Read(offset, buffer);
            _bytes = read == length ? buffer : buffer[..read];
            return _bytes;
        }
    }

    /// <summary>The objects packed inside one object stream, and where each of them starts.</summary>
    private sealed class ObjectStreamContents
    {
        private readonly ReadOnlyMemory<byte> _data;
        private readonly int[] _numbers;
        private readonly int[] _offsets;
        private readonly int _first;

        private ObjectStreamContents(ReadOnlyMemory<byte> data, int[] numbers, int[] offsets, int first)
        {
            _data = data;
            _numbers = numbers;
            _offsets = offsets;
            _first = first;
        }

        public int Count => _numbers.Length;

        public int NumberAt(int index) => _numbers[index];

        public static ObjectStreamContents? TryCreate(PdfStream stream, PdfDiagnostics diagnostics)
        {
            var count = (int)stream.Dictionary.GetInteger(PdfName.N, 0);
            var first = (int)stream.Dictionary.GetInteger(PdfName.First, -1);

            // Each entry of the header costs at least "0 0 ", so a count far beyond what the header could
            // hold is a lie, and believing it would mean allocating arrays the file asked for.
            if (count <= 0 || first < 0 || count > (first / 2) + 1)
            {
                return null;
            }

            var data = stream.Decode(diagnostics);

            if (first > data.Length)
            {
                diagnostics.Warn(PdfDiagnosticCodes.StreamTruncated, "An object stream is shorter than its header claims.");
                return null;
            }

            var numbers = new int[count];
            var offsets = new int[count];
            var lexer = new PdfLexer(data.Span[..first]);

            for (var i = 0; i < count; i++)
            {
                var numberToken = lexer.Read();
                var offsetToken = lexer.Read();

                if (numberToken.Kind != PdfTokenKind.Integer || offsetToken.Kind != PdfTokenKind.Integer)
                {
                    diagnostics.Warn(PdfDiagnosticCodes.SyntaxUnexpectedToken, "An object stream header is malformed.");
                    return i > 0 ? new ObjectStreamContents(data, numbers[..i], offsets[..i], first) : null;
                }

                numbers[i] = (int)numberToken.Integer;
                offsets[i] = (int)offsetToken.Integer;
            }

            return new ObjectStreamContents(data, numbers, offsets, first);
        }

        public PdfObject Parse(int index, int expectedNumber, IPdfObjectSource source, PdfDiagnostics diagnostics)
        {
            if (index < 0 || index >= _numbers.Length)
            {
                // The index in the entry is a hint; the object number is the truth.
                index = Array.IndexOf(_numbers, expectedNumber);
                if (index < 0)
                {
                    return PdfNull.Instance;
                }
            }

            if (_numbers[index] != expectedNumber)
            {
                var corrected = Array.IndexOf(_numbers, expectedNumber);
                if (corrected < 0)
                {
                    return PdfNull.Instance;
                }

                diagnostics.Repair(
                    PdfDiagnosticCodes.XRefOffsetAdjusted,
                    $"Object {expectedNumber} was at index {corrected} of its object stream, not {index}.");

                index = corrected;
            }

            var start = _first + _offsets[index];

            if (start < 0 || start >= _data.Length)
            {
                return PdfNull.Instance;
            }

            var parser = new PdfObjectParser(_data, 0, source, diagnostics, streamData: null);
            parser.Position = start;
            return parser.ParseObject();
        }
    }
}
