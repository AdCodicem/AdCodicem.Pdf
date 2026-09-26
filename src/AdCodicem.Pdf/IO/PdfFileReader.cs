using System.Buffers.Binary;
using AdCodicem.Pdf.Diagnostics;
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
    private const int MaxObjectWindow = 16 * 1024 * 1024;
    internal const int XRefWindow = 64 * 1024;
    private const int MaxXRefWindow = 64 * 1024 * 1024;
    private const int MaxXRefSections = 1024;
    private const int MaxSubsectionEntries = 50_000_000;
    private const int HeaderSearchLength = 4096;
    private const int TailSearchLength = 4096;
    private const int NearbySearchRadius = 512;
    private const int ScanChunkSize = 1024 * 1024;
    private const int ScanOverlap = 64;
    private const int MaxRepairObjects = 2_000_000;

    /// <summary>Asks <see cref="TryParseObjectAt"/> for whatever object starts at an offset.</summary>
    private const int AnyNumber = 0;

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

    public PdfFileReader(PdfFileSource source, PdfDiagnostics diagnostics, int cacheCapacity, bool ownsSource)
    {
        _source = source;
        _diagnostics = diagnostics;
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
    PdfStreamData IPdfStreamDataProvider.Create(long absoluteOffset, int length)
    {
        // A declared length is a claim made by the file, so it is clamped to what the file can hold.
        // Without this, "/Length 2147483647" would ask for two gigabytes of memory.
        var available = (int)Math.Clamp(_source.Length - absoluteOffset, 0, int.MaxValue);
        return new FileStreamData(_source, absoluteOffset, Math.Clamp(length, 0, available));
    }

    /// <inheritdoc/>
    bool IPdfStreamDataProvider.IsEndStreamAt(long absoluteOffset)
    {
        // A source is only asked for bytes it has: a subclass need not accept an offset at its end.
        if (absoluteOffset < 0 || absoluteOffset >= _source.Length)
        {
            return false;
        }

        Span<byte> tail = stackalloc byte[PdfObjectParser.EndStreamLookahead];
        var read = _source.Read(absoluteOffset, tail);
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

            if (++sections > MaxXRefSections)
            {
                _diagnostics.Warn(PdfDiagnosticCodes.XRefChainCycle, "The cross-reference chain is unreasonably long.", offset);
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
        var windowSize = XRefWindow;

        while (true)
        {
            using var window = _source.GetWindow(absolute, windowSize);
            var span = window.Memory.Span;
            var lexer = new PdfLexer(span);

            if (!lexer.Read().IsKeyword("xref"u8))
            {
                return false;
            }

            // Past the largest window, or at the end of the file, what the window holds is all there is.
            var canGrow = windowSize < MaxXRefWindow && window.Length == windowSize;
            var truncated = false;

            while (!truncated)
            {
                var token = lexer.Read();

                // A token that reaches the window's edge may have been cut by it — "trai" of "trailer", "32"
                // of "3270" — so it is read again in a larger window rather than taken for what it seems.
                if (token.Kind == PdfTokenKind.EndOfInput || (canGrow && token.End >= span.Length))
                {
                    // The table runs past the window; the caller grows it and reads again.
                    break;
                }

                if (token.IsKeyword(TrailerKeyword))
                {
                    if (!TryReadTrailer(window.Memory, absolute, lexer.Position, canGrow, out var trailer))
                    {
                        // A trailer cut by the window's edge would lose its /Root or its /Prev; the window
                        // grows instead, as it does for a table that runs past it.
                        break;
                    }

                    if (trailer is not null)
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

                if (countToken.Kind == PdfTokenKind.EndOfInput || (canGrow && countToken.End >= span.Length))
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

            if (windowSize >= MaxXRefWindow || window.Length < windowSize)
            {
                // The table runs to the end of what exists; keep whatever was indexed.
                return _xref.Count > 0;
            }

            windowSize = (int)Math.Min((long)windowSize * 4, MaxXRefWindow);
        }
    }

    /// <summary>
    /// Parses the trailer dictionary that starts at <paramref name="position"/> in a classic table's window.
    /// Returns false, having reported nothing, when the dictionary runs past a window that can grow.
    /// </summary>
    private bool TryReadTrailer(
        ReadOnlyMemory<byte> window, long absolute, int position, bool canGrow, out PdfDictionary? trailer)
    {
        trailer = null;
        var mark = _pending.GetMark();

        try
        {
            var parser = new PdfObjectParser(window, absolute, this, _pending, this);
            parser.Position = position;
            var parsed = parser.ParseObject();

            if (parser.IsTruncated && canGrow)
            {
                return false;
            }

            _pending.MoveTo(_diagnostics, mark);
            trailer = parsed.AsDictionary();
            return true;
        }
        finally
        {
            _pending.RollBack(mark);
        }
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

        // Parsed like any other object, through a window that grows when the dictionary runs past it; the
        // data is read from the file when it is decoded.
        if (!TryParseObjectAt(AnyNumber, absolute, out var value) || value is not PdfStream stream)
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
    /// Parses the object at an exact offset, growing the window while the object runs past its end.
    /// Returns false when nothing with the expected <paramref name="number"/> is there; <see cref="AnyNumber"/>
    /// accepts whatever object starts at the offset.
    /// </summary>
    /// <remarks>
    /// What the parser notices is held back until an attempt is kept. An attempt that ran out of window
    /// sees a cut token, a missing <c>endstream</c> or an object that stops mid-way: artefacts of the window,
    /// not of the file, and reporting them would put a syntax error the file does not have in the document's
    /// diagnostics — with the real ones reported once per attempt. What nested loads report, such as a
    /// relocated <c>/Length</c> object, goes straight to the document's diagnostics and stays there.
    /// </remarks>
    private bool TryParseObjectAt(int number, long offset, out PdfObject value)
    {
        value = PdfNull.Instance;

        if (offset < 0 || offset >= _source.Length)
        {
            return false;
        }

        var windowSize = InitialObjectWindow;
        var mark = _pending.GetMark();

        try
        {
            while (true)
            {
                using var window = _source.GetWindow(offset, windowSize);
                var parser = new PdfObjectParser(window.Memory, offset, this, _pending, this);

                if (!parser.TryReadIndirectObject(out var found, out var parsed) || (number != AnyNumber && found.Number != number))
                {
                    return false;
                }

                if (parser.IsTruncated && windowSize < MaxObjectWindow && window.Length == windowSize)
                {
                    _pending.RollBack(mark);
                    windowSize = (int)Math.Min((long)windowSize * 8, MaxObjectWindow);
                    continue;
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
        ExpandObjectStreams();

        if (!HasUsableRoot())
        {
            FindCatalog();
        }
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

    private void ExpandObjectStreams()
    {
        var numbers = new List<int>(_xref.Entries.Keys);

        foreach (var number in numbers)
        {
            if (!_xref.TryGet(number, out var entry) || entry.Kind != XRefEntryKind.Regular)
            {
                continue;
            }

            if (GetObject(new PdfObjectId(number)) is not PdfStream stream ||
                !stream.Dictionary.IsOfType(PdfName.ObjStm))
            {
                continue;
            }

            var contents = ObjectStreamContents.TryCreate(stream, _diagnostics);
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
    }

    private void FindCatalog()
    {
        foreach (var number in _xref.Entries.Keys)
        {
            var candidate = GetObject(new PdfObjectId(number));

            if (candidate.AsDictionary() is { } dictionary && dictionary.IsOfType(PdfName.Catalog))
            {
                Trailer.Set(PdfName.Root, new PdfReference(new PdfObjectId(number), this));
                _diagnostics.Repair(PdfDiagnosticCodes.XRefRebuilt, $"The document catalogue was found as object {number}.");
                return;
            }
        }
    }

    private sealed class FileStreamData(PdfFileSource source, long offset, int length) : PdfStreamData
    {
        private byte[]? _bytes;

        public override int Length => length;

        public override long Position => offset;

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
