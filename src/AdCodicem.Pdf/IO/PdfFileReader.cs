using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
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

    /// <summary>How much of a section is read to tell a classic table from a cross-reference stream.</summary>
    internal const int XRefProbeLength = 32;

    /// <summary>
    /// Most entries a subsection may claim. Not a guard a valid file reaches: a classic table holds its rows
    /// within <see cref="PdfReaderLimits.MaxXRefSectionLength"/>, a stream within its decoded length, and a
    /// count past both describes rows that are not there.
    /// </summary>
    private const int MaxSubsectionEntries = 50_000_000;
    private const int HeaderSearchLength = 4096;
    internal const int TailSearchLength = 4096;
    internal const int NearbySearchRadius = 512;

    /// <summary>
    /// The most places near missing cross-reference sections that are tried as one, over a whole document. Real
    /// files miss by a few bytes, with the section the nearest candidate; a file packed with object headers
    /// there, or with sections that all miss, gets no more attempts. Not a guard a valid file can reach: a valid
    /// file's sections are where its chain names them.
    /// </summary>
    private const int MaxRelocationCandidates = 32;
    private const int ScanChunkSize = 1024 * 1024;
    private const int ScanOverlap = 64;
    private const int MaxRepairObjects = 2_000_000;

    /// <summary>
    /// The decoded data of object streams kept at once, in bytes. Past it the oldest is let go, and decoded again if
    /// an object it holds is asked for, so no file is read differently for it: the bound is on memory, not on what
    /// can be read. The object stream decoded last is kept whatever its size, so memory follows the heaviest object
    /// stream rather than the number of them.
    /// </summary>
    private const long ObjectStreamBudget = 32L * 1024 * 1024;

    /// <summary>Asks <see cref="TryParseAt"/> for a direct object, with no object header: a trailer.</summary>
    private const int DirectObject = -1;

    /// <summary>Asks <see cref="TryParseAt"/> for an object whatever its number: a cross-reference stream.</summary>
    private const int AnyObject = 0;

    /// <summary>
    /// Deepest chain of objects loaded while loading another. Real documents stay within a handful — a stream
    /// in an object stream whose <c>/Length</c> is indirect is three —; each level costs a dozen frames, and the
    /// parsing of the object it loads besides. The count alone does not bound the stack, since each load parses
    /// as deep as the parser allows: the stack is asked as well before each load.
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

    /// <summary>What the file's own structure looked like as the document opened, for the validation rules.</summary>
    private readonly FileStructure _structure = new();

    /// <summary>
    /// The index as the chain gave it, copied before anything changed it — an object found near its offset, or a
    /// rebuild —; null while <see cref="_xref"/> is still that index.
    /// </summary>
    private PdfXRefTable? _chainIndex;

    /// <summary>How many times a guard was reached, reported or not: a read that reached one was cut by it.</summary>
    private int _guardsReached;

    /// <summary>
    /// The objects read, by object number. The generation is left out of the key on purpose: the index holds one
    /// entry per number, so every generation of a number loads the same object, and keying by both would parse and
    /// keep it once per generation a file cares to reference.
    /// </summary>
    private readonly Dictionary<int, PdfObject> _cache = [];
    private readonly Queue<int> _cacheOrder = new();
    /// <summary>
    /// The object streams decoded, by number, null for one that cannot be read; with the order they were decoded in
    /// and the bytes their data holds, which <see cref="ObjectStreamBudget"/> bounds.
    /// </summary>
    private readonly Dictionary<int, ObjectStreamContents?> _objectStreams = [];
    private readonly Queue<int> _objectStreamOrder = new();
    private long _objectStreamBytes;

    /// <summary>The numbers of the objects being loaded, by number for the reason <see cref="_cache"/> gives.</summary>
    private readonly HashSet<int> _loading = [];

    /// <summary>The object streams being decoded: an object one of them holds cannot be read meanwhile (#51).</summary>
    private readonly HashSet<int> _objectStreamsLoading = [];

    /// <summary>The object streams already reported as needing an object they hold to be read.</summary>
    private readonly HashSet<int> _objectStreamsNeedingThemselves = [];

    /// <summary>
    /// The objects each object stream read as null while it was decoded because of how the file is made — an object
    /// it holds, or one being loaded —, kept after the stream is let go: decoded again, it reads them as null again,
    /// so that what it holds does not depend on what was read, and kept, in between, nor on
    /// <see cref="ObjectStreamBudget"/>. A load too deep to follow depends on where it was asked from, not on the
    /// file, and is not recorded.
    /// </summary>
    private readonly Dictionary<int, HashSet<int>> _nullWhileDecoding = [];

    /// <summary>
    /// The objects the index holds in use that the reader gave up on the last time they were asked for — nothing
    /// where the entry places them nor near it, an object stream that cannot serve them —, for the validation rules
    /// to tell an object the file lacks from one it holds and the reader could not produce.
    /// </summary>
    private readonly HashSet<int> _unproduced = [];

    /// <summary>
    /// The objects read whose value <c>endobj</c> does not follow, and where each was read. An object stream's
    /// members have none to follow them, and are never in it.
    /// </summary>
    private readonly Dictionary<int, long> _endObjMissing = [];

    /// <summary>
    /// How many times a load answered null because the object was being loaded already, or because the object
    /// stream holding it was: that null says nothing of the object, and is not cached (#51).
    /// </summary>
    private int _reentries;

    /// <summary>Whether a rebuilt index is taking in the objects its object streams hold.</summary>
    private bool _expandingObjectStreams;
    private readonly int _cacheCapacity;
    private readonly bool _ownsSource;

    private long _headerOffset;
    private bool _repaired;
    private bool _nestingReported;

    /// <summary>How many places have been tried for sections of the chain that were not where it named them.</summary>
    private int _relocationCandidatesTried;

    /// <summary>
    /// Whether the index may lack objects the file defines: a section the chain names could not be found, a
    /// guard stopped the chain or a table before its end, or a cross-reference stream holds fewer rows than it
    /// declares. Only then is an object the index lacks looked for by rebuilding it; otherwise a reference to it
    /// is null, as the specification says.
    /// </summary>
    private bool _indexIncomplete;

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

    /// <summary>Gets the bytes the file is read from.</summary>
    public PdfFileSource Source => _source;

    /// <summary>Gets what the file's own structure looked like as the document opened.</summary>
    public FileStructure Structure => _structure;

    /// <summary>Gets how far into the file its header starts: every offset the file gives is counted from there.</summary>
    public long HeaderOffset => _headerOffset;

    /// <summary>
    /// Gets the index as the file's chain of cross-reference sections gave it — before anything the reader found
    /// changed it —, or null when the chain gave none and the index was rebuilt as the document opened.
    /// </summary>
    /// <remarks>
    /// Read it again for each lookup rather than keeping it: an object read in between may make the reader copy
    /// it before changing its own.
    /// </remarks>
    public PdfXRefTable? ChainIndex => _structure.ChainRead ? _chainIndex ?? _xref : null;

    /// <summary>
    /// Gets the index the reader reads with now: the chain's, as corrected since, or the one a rebuild made. Read it
    /// again for each lookup, as <see cref="ChainIndex"/>.
    /// </summary>
    public PdfXRefTable Index => _xref;

    /// <summary>Gets the bytes of decoded object stream data the reader keeps, which it bounds.</summary>
    internal long ObjectStreamBytes => _objectStreamBytes;

    /// <inheritdoc/>
    public PdfObject GetObject(PdfObjectId id)
    {
        if (id.Number <= 0)
        {
            return PdfNull.Instance;
        }

        // Asked before the cache: an object stream decoded again, once the budget let it go, must read as null what
        // it read as null the first time, whatever was read and kept since.
        if (_objectStreamsLoading.Count > 0)
        {
            // An object stream whose decoding needs an object it holds — its /DecodeParms, its /N — cannot be read
            // from while it is decoded: the object reads as null there, is reported, and is read again once the
            // stream is decoded (#51).
            if (IsHeldByObjectStreamBeingLoaded(id.Number, out var objectStream))
            {
                ReportObjectStreamNeedsItself(objectStream, id.Number);
                return NullWhileDecoding(id.Number);
            }

            if (ReadAsNullBefore(id.Number))
            {
                return NullWhileDecoding(id.Number);
            }
        }

        if (_cache.TryGetValue(id.Number, out var cached))
        {
            return cached;
        }

        // A file can make an object's length depend on the object itself, under any generation. Refusing to
        // re-enter turns an infinite recursion into a null.
        if (!_loading.Add(id.Number))
        {
            return NullWhileDecoding(id.Number);
        }

        try
        {
            // Loading an object can load another — an indirect /Length is resolved while its stream is
            // parsed — and a file can chain such objects as long as it likes, each a level deeper on the
            // stack. Past a depth no real document comes near, or once the thread's stack has no room left for
            // another load, the next one reads as null rather than taking the process with it. Nothing is
            // cached, so the same object loaded from a shallower place reads normally.
            if (_loading.Count > MaxNestedLoads || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
            {
                // Counted as a re-entry, so that nothing that asked for the object keeps the null for good.
                _reentries++;
                ReportNestingTooDeep(id);
                return PdfNull.Instance;
            }

            var reentries = _reentries;
            var value = LoadObject(id);

            // A null a guard against re-entry produced on the way is not the object's: it is read again when asked
            // again, rather than kept for good (#51).
            if (value is not PdfNull || reentries == _reentries)
            {
                Cache(id.Number, value);
            }

            return value;
        }
        finally
        {
            _loading.Remove(id.Number);
        }
    }

    /// <summary>
    /// Says whether the file holds object <paramref name="number"/> and whether the reader could produce it, as
    /// of the last time it was asked for: call it after resolving the object.
    /// </summary>
    /// <remarks>
    /// An object the index lacks, or holds as free — the one the chain gave as the reader first read it, and the
    /// one it reads with now —, is missing: a reference to it is null (ISO 32000-1, 7.3.10). One either index holds
    /// in use, and that the reader gave up on or lost in a rebuild, is unproduced: what went wrong is the index's
    /// or the object stream's, and the cross-reference rules report it.
    /// </remarks>
    internal ObjectPresence GetPresence(int number)
    {
        if (_xref.TryGet(number, out var entry) && entry.Kind != XRefEntryKind.Free)
        {
            return _unproduced.Contains(number) ? ObjectPresence.Unproduced : ObjectPresence.Defined;
        }

        return ChainIndex?.TryGet(number, out var written) == true && written.Kind != XRefEntryKind.Free
            ? ObjectPresence.Unproduced
            : ObjectPresence.Missing;
    }

    /// <summary>
    /// Determines whether object <paramref name="number"/>, as it was read, is not followed by <c>endobj</c>, and
    /// gives where it was read.
    /// </summary>
    internal bool IsEndObjMissing(int number, out long position) => _endObjMissing.TryGetValue(number, out position);

    /// <summary>
    /// Answers null for object <paramref name="number"/>, a null that says nothing of the object and is not cached,
    /// and records it for each object stream being decoded, which reads it as null again whenever it is decoded again.
    /// </summary>
    private PdfNull NullWhileDecoding(int number)
    {
        _reentries++;

        foreach (var objectStream in _objectStreamsLoading)
        {
            if (!_nullWhileDecoding.TryGetValue(objectStream, out var numbers))
            {
                numbers = [];
                _nullWhileDecoding[objectStream] = numbers;
            }

            numbers.Add(number);
        }

        return PdfNull.Instance;
    }

    /// <summary>Determines whether an object stream being decoded read object <paramref name="number"/> as null before.</summary>
    private bool ReadAsNullBefore(int number)
    {
        foreach (var objectStream in _objectStreamsLoading)
        {
            if (_nullWhileDecoding.TryGetValue(objectStream, out var numbers) && numbers.Contains(number))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Determines whether the index places object <paramref name="number"/> in an object stream being decoded.</summary>
    private bool IsHeldByObjectStreamBeingLoaded(int number, out int objectStream)
    {
        objectStream = 0;

        if (_objectStreamsLoading.Count == 0 ||
            !_xref.TryGet(number, out var entry) ||
            entry.Kind != XRefEntryKind.Compressed ||
            !_objectStreamsLoading.Contains(entry.ObjectStreamNumber))
        {
            return false;
        }

        objectStream = entry.ObjectStreamNumber;
        return true;
    }

    /// <summary>Reports, once for each, an object stream that needs one of the objects it holds to be read.</summary>
    private void ReportObjectStreamNeedsItself(int objectStream, int number)
    {
        if (!_objectStreamsNeedingThemselves.Add(objectStream))
        {
            return;
        }

        var position = _xref.TryGet(objectStream, out var entry) && entry.Kind == XRefEntryKind.Regular
            ? entry.Offset + _headerOffset
            : -1;

        _diagnostics.Warn(
            PdfDiagnosticCodes.StreamSelfReference,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Object stream {objectStream} needs object {number}, which it holds, to be read: the object reads as null while the stream is decoded, and the stream is decoded without it."),
            position);
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

        if (startXref < 0 || !TryReadXRefChain(startXref))
        {
            Repair();
            return;
        }

        _structure.ChainRead = true;
        _structure.TrailerRead = Trailer.Count > 0;
        _structure.SizeAsWritten = Trailer.GetRaw(PdfName.Size);
        _structure.RootAsWritten = Trailer.GetRaw(PdfName.Root);

        if (!HasUsableRoot())
        {
            _structure.RootUsable = false;
            _structure.RootResolved = _structure.RootAsWritten is PdfReference root ? root.Resolve() : null;

            // Resolving /Root may itself have rebuilt the index — its object was neither where its entry said nor
            // near it —, and the rebuild looked for the catalog in the rebuilt index.
            if (!_repaired)
            {
                RecoverCatalog();
            }
        }
    }

    /// <summary>
    /// Looks for the catalog among the objects the chain indexed, the trailer's <c>/Root</c> leading to none, and
    /// rebuilds the index only when none of them is one.
    /// </summary>
    /// <remarks>
    /// A broken <c>/Root</c> says nothing against the index: the objects are where the chain says. Keeping the index
    /// keeps what an incremental update superseded superseded, which a rebuild — the last definition in the file
    /// winning — does not always manage.
    /// </remarks>
    private void RecoverCatalog()
    {
        var reached = FindCatalog();

        if (!HasUsableRoot() && !_repaired)
        {
            Repair();
        }

        reached?.Throw();
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

        _structure.HeaderPosition = index;

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

        _structure.HeaderVersion = System.Text.Encoding.ASCII.GetString(span[versionStart..versionEnd]);

        if (versionEnd > versionStart)
        {
            Version = _structure.HeaderVersion;
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

        _structure.StartXRefPosition = window.Offset + index;

        var lexer = new PdfLexer(span, index + StartXRefKeyword.Length);
        var token = lexer.Read();

        if (token.Kind == PdfTokenKind.Integer && token.Integer >= 0)
        {
            _structure.StartXRef = token.Integer;
            return token.Integer;
        }

        return -1;
    }

    /// <summary>
    /// Reads the chain of cross-reference sections from the newest, following each <c>/Prev</c> and the
    /// <c>/XRefStm</c> of a hybrid file.
    /// </summary>
    /// <remarks>
    /// A section the chain names must be read, or the objects only it indexes are lost. One that is not where
    /// it is named is looked for nearby, as an object is. One that cannot be found is reported and leaves the
    /// index incomplete: the chain stops at a missing <c>/Prev</c> and goes on past a missing <c>/XRefStm</c>,
    /// and an object the index then lacks is looked for by rebuilding it, when it is asked for. Nothing is
    /// rebuilt at opening that nobody asks for. What became of each section is recorded in
    /// <see cref="Structure"/>, for the validation rules.
    /// </remarks>
    /// <param name="startOffset">The offset <c>startxref</c> gives.</param>
    private bool TryReadXRefChain(long startOffset)
    {
        var visited = new HashSet<long>();
        var offset = startOffset;
        var sections = 0;
        var naming = "startxref";
        var namedFrom = _structure.StartXRefPosition;

        while (offset >= 0)
        {
            if (!visited.Add(offset))
            {
                // The section the chain should have gone on to is unknown: what only it indexed is the index's to
                // find, as a missing section's is.
                _indexIncomplete = true;
                _structure.LoopOffset = offset + _headerOffset;
                _structure.LoopNamedBy = naming;
                _structure.LoopNamedFrom = namedFrom;
                _diagnostics.Warn(PdfDiagnosticCodes.XRefChainCycle, "The cross-reference chain loops back on itself.", offset);
                break;
            }

            if (++sections > _guard.Bound(PdfLimit.XRefSectionCount))
            {
                // Each incremental save adds a section, so a long chain is a sound file saved often; the newest
                // sections, read first, are the ones that win, and what only the older ones index is found by
                // rebuilding the index when it is asked for.
                _indexIncomplete = true;
                _structure.ChainCutAt = offset + _headerOffset;
                ReachLimit(
                    PdfLimit.XRefSectionCount,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The cross-reference chain has more than {_guard.Bound(PdfLimit.XRefSectionCount):N0} sections; the older ones were not read."),
                    offset + _headerOffset);
                break;
            }

            var section = new XRefSectionRecord(naming, offset + _headerOffset, namedFrom);
            _structure.Add(section);

            if (TryReadXRefSection(offset, section, out var previous, out var hybrid) != XRefSectionState.Read)
            {
                // The first section is where startxref says, and one that is not there leaves no index to
                // complete: the file is scanned whole, which is reported. A later one is named by the section
                // before it.
                if (sections == 1)
                {
                    return false;
                }

                if (!TryRelocateXRefSection(offset, section, out previous, out hybrid))
                {
                    ReportMissingSection(naming, offset);
                    break;
                }
            }

            if (hybrid >= 0 && visited.Add(hybrid))
            {
                // A hybrid-reference file keeps a classic table for old readers and a stream for the rest: without
                // the stream, the objects it indexes are missing, and the chain goes on through /Prev.
                var stream = new XRefSectionRecord("/XRefStm", hybrid + _headerOffset, section.Offset) { NamingTrailer = section.Trailer };
                _structure.Add(stream);

                if (TryReadXRefSection(hybrid, stream, out _, out _) != XRefSectionState.Read &&
                    !TryRelocateXRefSection(hybrid, stream, out _, out _))
                {
                    ReportMissingSection("/XRefStm", hybrid);
                }
            }

            offset = previous;
            naming = "/Prev";
            namedFrom = section.Offset;
        }

        return _xref.Count > 0;
    }

    /// <summary>Reports a section of the chain that could not be found, and marks the index incomplete.</summary>
    private void ReportMissingSection(string naming, long offset)
    {
        _indexIncomplete = true;
        _diagnostics.Warn(
            PdfDiagnosticCodes.XRefSectionMissing,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The cross-reference section {naming} names at offset {offset + _headerOffset} is not there, nor near it; the objects only it indexes are looked for by rebuilding the index."),
            offset + _headerOffset);
    }

    /// <summary>
    /// Looks for a cross-reference section near the offset the chain named, where a careless writer's
    /// arithmetic leaves it: a classic table's <c>xref</c> keyword, or the header of an object that reads as a
    /// cross-reference stream, nearest first.
    /// </summary>
    /// <remarks>
    /// The search spans <see cref="NearbySearchRadius"/> bytes either side, as an object's does, and tries at most
    /// <see cref="MaxRelocationCandidates"/> places in all, over the whole document: each is read through a window
    /// of up to 64 KB, the file chooses how many headers lie near a section, and a chain can name a section for
    /// each of its <see cref="PdfReaderLimits.MaxXRefSectionCount"/> links. Found, the section's record takes what
    /// was read there; not found, it keeps what the named offset held.
    /// </remarks>
    private bool TryRelocateXRefSection(long offset, XRefSectionRecord section, out long previous, out long hybrid)
    {
        previous = -1;
        hybrid = -1;

        var absolute = offset + _headerOffset;

        if (absolute < 0 || absolute >= _source.Length)
        {
            return false;
        }

        var start = Math.Max(0, absolute - NearbySearchRadius);
        var length = (int)Math.Min(NearbySearchRadius * 2, _source.Length - start);
        var candidates = new List<long>();

        using (var window = _source.GetWindow(start, length))
        {
            FindSectionCandidates(window.Memory.Span, start, candidates);
        }

        candidates.Sort((left, right) => Math.Abs(left - absolute).CompareTo(Math.Abs(right - absolute)));

        foreach (var candidate in candidates)
        {
            if (candidate == absolute)
            {
                continue;
            }

            if (_relocationCandidatesTried++ >= MaxRelocationCandidates)
            {
                break;
            }

            var attempt = new XRefSectionRecord(section.NamedBy, candidate, section.NamedFrom);

            if (TryReadXRefSection(candidate - _headerOffset, attempt, out previous, out hybrid) == XRefSectionState.Read)
            {
                section.RelocateTo(attempt);
                _diagnostics.Repair(
                    PdfDiagnosticCodes.XRefOffsetAdjusted,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The cross-reference section {section.NamedBy} names at offset {absolute} was found {candidate - absolute} bytes from there."),
                    candidate);
                return true;
            }
        }

        previous = -1;
        hybrid = -1;
        return false;
    }

    /// <summary>
    /// Collects where a cross-reference section could start in <paramref name="span"/>: an <c>xref</c> keyword
    /// standing on its own — not the end of <c>startxref</c> —, and every object header.
    /// </summary>
    private static void FindSectionCandidates(ReadOnlySpan<byte> span, long start, List<long> candidates)
    {
        var searchFrom = 0;

        while (searchFrom < span.Length)
        {
            var index = span[searchFrom..].IndexOf("xref"u8);
            if (index < 0)
            {
                break;
            }

            var position = searchFrom + index;
            var end = position + 4;

            if ((position == 0 || !PdfCharacters.IsRegular(span[position - 1])) &&
                (end == span.Length || !PdfCharacters.IsRegular(span[end])))
            {
                candidates.Add(start + position);
            }

            searchFrom = end;
        }

        searchFrom = 0;

        while (searchFrom < span.Length)
        {
            var index = span[searchFrom..].IndexOf(ObjKeyword);
            if (index < 0)
            {
                break;
            }

            var position = searchFrom + index;

            if (TryReadHeaderBackwards(span, position, out _, out var headerStart))
            {
                candidates.Add(start + headerStart);
            }

            searchFrom = position + ObjKeyword.Length;
        }
    }

    /// <summary>
    /// Reads the section at <paramref name="offset"/> into the index, and what became of it into
    /// <paramref name="section"/>: <see cref="XRefSectionState.Read"/>, <see cref="XRefSectionState.NotFound"/> when
    /// nothing there is a section, or <see cref="XRefSectionState.Malformed"/> when one is and cannot be read.
    /// </summary>
    private XRefSectionState TryReadXRefSection(long offset, XRefSectionRecord section, out long previous, out long hybrid)
    {
        previous = -1;
        hybrid = -1;

        var absolute = offset + _headerOffset;
        if (absolute < 0 || absolute >= _source.Length)
        {
            _diagnostics.Warn(PdfDiagnosticCodes.XRefEntryOutOfRange, "A cross-reference section points outside the file.", absolute);
            section.Fault = "lies outside the file";
            section.State = XRefSectionState.NotFound;
            return section.State;
        }

        using (var probe = _source.GetWindow(absolute, XRefProbeLength))
        {
            var lexer = new PdfLexer(probe.Memory.Span);
            var first = lexer.Read();
            section.Padding = first.Kind == PdfTokenKind.EndOfInput ? probe.Length : first.Start;

            if (first.IsKeyword("xref"u8))
            {
                section.Kind = XRefSectionKind.Table;
                section.State = TryReadClassicTable(absolute, section, out previous, out hybrid)
                    ? XRefSectionState.Read
                    : XRefSectionState.Malformed;
                return section.State;
            }
        }

        section.State = TryReadXRefStream(absolute, section, out previous);
        return section.State;
    }

    private bool TryReadClassicTable(long absolute, XRefSectionRecord section, out long previous, out long hybrid)
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
                section.Fault = "does not start with the xref keyword";
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
                    section.TrailerPosition = absolute + token.Start;

                    if (ReadTrailer(window.Memory, absolute, lexer.Position, windowFull, section) is { } trailer)
                    {
                        _xref.MergeTrailer(trailer);
                        previous = SectionOffset(trailer, PdfName.Prev, absolute);
                        hybrid = SectionOffset(trailer, PdfName.XRefStm, absolute);
                    }

                    return true;
                }

                if (token.Kind != PdfTokenKind.Integer)
                {
                    if (token.Kind == PdfTokenKind.DictionaryStart)
                    {
                        // The rows run into a dictionary no trailer keyword introduces: the trailer is missing,
                        // whatever that dictionary holds.
                        section.TrailerFault = XRefTrailerFault.Missing;
                        section.TrailerPosition = absolute + token.Start;
                    }
                    else
                    {
                        section.Fault ??= string.Create(
                            CultureInfo.InvariantCulture,
                            $"it holds {DescribeToken(token)} at offset {absolute + token.Start}, where a subsection or the trailer should start");
                    }

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
                    section.Fault ??= string.Create(
                        CultureInfo.InvariantCulture,
                        $"the subsection header at offset {absolute + token.Start} does not give a first object number and a count of rows");
                    return false;
                }

                truncated = !ReadSubsection(ref lexer, (int)first, (int)countToken.Integer, section, absolute);
            }

            if (!canGrow)
            {
                if (windowFull)
                {
                    _indexIncomplete = true;
                    section.CutByLimit = true;
                    ReachLimit(
                        PdfLimit.XRefSectionLength,
                        $"The cross-reference table runs past {PdfLimitGuard.FormatLength(maxWindow)}; only the entries within it were read.",
                        absolute);
                }
                else
                {
                    // The file ends before the table's trailer.
                    section.TrailerFault = XRefTrailerFault.Missing;
                    section.TrailerPosition = _source.Length;
                }

                // Keep whatever was indexed.
                return _xref.Count > 0;
            }

            windowSize = (int)Math.Min((long)windowSize * 4, maxWindow);
        }
    }

    /// <summary>
    /// Parses the trailer dictionary that starts at <paramref name="position"/> in a classic table's window, and
    /// records in <paramref name="section"/> what was wrong with it.
    /// </summary>
    /// <remarks>
    /// A trailer cut by the window's edge would lose its /Root or its /Prev. It is parsed again where it
    /// starts, through a window of its own that stops at <see cref="PdfReaderLimits.MaxTrailerLength"/> — not
    /// by growing the table's, which a dictionary that never closes would grow to the size of the file, for
    /// every section of a chain. Only a dictionary written as one is a trailer: a reference is not resolved, which
    /// would load an object while the index is still being read.
    /// </remarks>
    private PdfDictionary? ReadTrailer(
        ReadOnlyMemory<byte> window, long absolute, int position, bool windowFull, XRefSectionRecord section)
    {
        var mark = _pending.GetMark();

        try
        {
            var parser = new PdfObjectParser(window, absolute, this, _pending, this);
            parser.Position = position;
            var parsed = parser.ParseObject();

            if (!parser.IsTruncated || !windowFull)
            {
                // Cut short where the window holds the end of the file, the dictionary runs to it unclosed.
                var malformed = parser.IsTruncated || SyntaxFaultSince(_pending, mark);
                _pending.MoveTo(_diagnostics, mark);
                return JudgeTrailer(parsed, malformed, section);
            }
        }
        finally
        {
            _pending.RollBack(mark);
        }

        // The trailer starts inside the window, so inside the file as long as the source's length holds;
        // where nothing can be parsed the value is null, and anything that is not a dictionary is no trailer.
        var before = _diagnostics.GetMark();
        var guards = _guardsReached;
        _ = TryParseAt(absolute + position, DirectObject, PdfLimit.Trailer, out var value);
        section.CutByLimit |= _guardsReached != guards;
        return JudgeTrailer(value, SyntaxFaultSince(_diagnostics, before), section);
    }

    /// <summary>Records a section's trailer, or what is wrong with it, and returns it when it is a dictionary.</summary>
    private static PdfDictionary? JudgeTrailer(PdfObject parsed, bool malformed, XRefSectionRecord section)
    {
        if (parsed is not PdfDictionary dictionary)
        {
            section.TrailerFault = XRefTrailerFault.NotADictionary;
            return null;
        }

        section.Trailer = dictionary;

        if (malformed)
        {
            section.TrailerFault = XRefTrailerFault.Malformed;
        }

        return dictionary;
    }

    /// <summary>Determines whether a syntax error was recorded since <paramref name="mark"/>, or may have been, the capacity reached.</summary>
    private static bool SyntaxFaultSince(PdfDiagnostics diagnostics, PdfDiagnosticsMark mark)
    {
        if (diagnostics.SuppressedCount > mark.Suppressed)
        {
            return true;
        }

        for (var i = mark.Count; i < diagnostics.Count; i++)
        {
            if (diagnostics[i].Code.StartsWith("syntax.", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Determines whether a guard was reported since <paramref name="mark"/>.</summary>
    private static bool LimitSince(PdfDiagnostics diagnostics, PdfDiagnosticsMark mark)
    {
        for (var i = mark.Count; i < diagnostics.Count; i++)
        {
            if (diagnostics[i].Code.StartsWith("limit.", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Says what a token is, for a message about what stood where something else should have.</summary>
    private static string DescribeToken(PdfToken token) => token.Kind switch
    {
        PdfTokenKind.Keyword => $"the keyword {System.Text.Encoding.Latin1.GetString(token.Text)}",
        PdfTokenKind.Name => "a name",
        PdfTokenKind.Real => "a real number",
        PdfTokenKind.LiteralString or PdfTokenKind.HexString => "a string",
        PdfTokenKind.ArrayStart or PdfTokenKind.ArrayEnd => "an array delimiter",
        PdfTokenKind.DictionaryEnd => "the end of a dictionary",
        _ => "a byte that starts no token",
    };

    private bool ReadSubsection(ref PdfLexer lexer, int first, int count, XRefSectionRecord section, long absolute)
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

            var number = first + i;

            if (offsetToken.Kind != PdfTokenKind.Integer || generationToken.Kind != PdfTokenKind.Integer ||
                !(kindToken.IsKeyword("n"u8) || kindToken.IsKeyword("f"u8)))
            {
                // A malformed row: stop this subsection rather than misread every row after it. The rows it
                // leaves unread index nothing, and the objects they stood for are the index's to find.
                _indexIncomplete = true;
                section.Fault ??= string.Create(
                    CultureInfo.InvariantCulture,
                    $"the row for object {number}, at offset {absolute + offsetToken.Start}, is not an offset, a generation and n or f");
                return true;
            }

            section.HighestNumber = Math.Max(section.HighestNumber, number);

            if (kindToken.IsKeyword("n"u8))
            {
                _xref.TryAdd(number, XRefEntry.Regular(offsetToken.Integer, (int)generationToken.Integer));
            }
            else
            {
                _xref.TryAdd(number, XRefEntry.Free);
            }
        }

        return true;
    }

    /// <summary>
    /// Reads the cross-reference stream at <paramref name="absolute"/>, if one is there, and records what became of
    /// it in <paramref name="section"/>.
    /// </summary>
    /// <remarks>
    /// An object that declares itself a cross-reference stream — <c>/Type /XRef</c>, or a <c>/W</c> — and cannot be
    /// read is a malformed section; any other object there, or none, is no section at all.
    /// </remarks>
    private XRefSectionState TryReadXRefStream(long absolute, XRefSectionRecord section, out long previous)
    {
        previous = -1;

        // The first window, of 64 KB, holds the data of all but the largest cross-reference streams, so the
        // parser checks their /Length against the data rather than only at the end the /Length gives. The
        // dictionary is the trailer, and grows its window no further than a trailer may.
        var before = _diagnostics.GetMark();
        var guards = _guardsReached;

        if (!TryParseNumberedAt(absolute, AnyObject, PdfLimit.Trailer, out var value, out var number, XRefWindow))
        {
            section.Fault = "holds neither the xref keyword nor an object";
            return XRefSectionState.NotFound;
        }

        var dictionary = value switch
        {
            PdfStream stream => stream.Dictionary,
            PdfDictionary direct => direct,
            _ => null,
        };

        if (dictionary is null || !(dictionary.IsOfType(PdfName.XRef) || dictionary.ContainsKey(PdfName.W)))
        {
            section.Fault = string.Create(
                CultureInfo.InvariantCulture, $"holds object {number}, which is not a cross-reference stream");
            return XRefSectionState.NotFound;
        }

        section.Kind = XRefSectionKind.Stream;
        section.StreamObjectNumber = number;
        section.TrailerPosition = absolute;
        section.CutByLimit |= _guardsReached != guards;

        if (value is not PdfStream xrefStream)
        {
            section.Fault = "its dictionary is not followed by stream data";
            return XRefSectionState.Malformed;
        }

        section.Trailer = dictionary;

        if (SyntaxFaultSince(_diagnostics, before))
        {
            section.TrailerFault = XRefTrailerFault.Malformed;
        }

        var widths = dictionary.GetArray(PdfName.W);

        if (widths is null || widths.Count < 3)
        {
            section.Fault = "its /W does not give the widths of three fields";
            return XRefSectionState.Malformed;
        }

        Span<int> fieldWidths = stackalloc int[3];
        var rowLength = 0;

        for (var i = 0; i < 3; i++)
        {
            var width = (int)(widths.Resolved(i).AsInteger() ?? 0);
            if (width is < 0 or > 8)
            {
                section.Fault = "its /W gives a field a width outside 0 to 8 bytes";
                return XRefSectionState.Malformed;
            }

            fieldWidths[i] = width;
            rowLength += width;
        }

        if (rowLength == 0)
        {
            section.Fault = "its /W gives rows of no bytes";
            return XRefSectionState.Malformed;
        }

        var decoding = _diagnostics.GetMark();
        var data = xrefStream.Decode(_diagnostics).Span;
        var decodedWhole = !LimitSince(_diagnostics, decoding) && !xrefStream.Data.CutByGuard;
        section.CutByLimit |= !decodedWhole;

        var size = (int)dictionary.GetInteger(PdfName.Size, 0);
        var ranges = dictionary.GetArray(PdfName.Index);
        var position = 0;
        long declared = 0;

        if (ranges is null)
        {
            declared = size;
            ReadXRefStreamRows(data, ref position, fieldWidths, rowLength, 0, size, section);
        }
        else
        {
            for (var i = 0; i + 1 < ranges.Count; i += 2)
            {
                var start = (int)(ranges.Resolved(i).AsInteger() ?? 0);
                var count = (int)(ranges.Resolved(i + 1).AsInteger() ?? 0);

                if (count is < 0 or > MaxSubsectionEntries)
                {
                    section.Fault = "its /Index gives a subsection a count of rows out of range";
                    _indexIncomplete = true;
                    break;
                }

                declared += count;
                ReadXRefStreamRows(data, ref position, fieldWidths, rowLength, start, count, section);
            }
        }

        if (declared * rowLength > data.Length && decodedWhole)
        {
            // Rows the data does not hold, when the reader decoded all of it, are the file's to answer for.
            section.Incomplete = true;
            section.Fault ??= string.Create(
                CultureInfo.InvariantCulture,
                $"it holds {data.Length / rowLength:N0} rows where its /Index and /Size declare {declared:N0}");
        }

        _xref.MergeTrailer(dictionary);
        previous = SectionOffset(dictionary, PdfName.Prev, absolute);
        return XRefSectionState.Read;
    }

    /// <summary>
    /// Reads the offset a section's <c>/Prev</c> or <c>/XRefStm</c> gives, or -1 when it gives none.
    /// </summary>
    /// <remarks>
    /// The specification makes it a direct integer. Anything else — tiff2pdf writes <c>/Prev 576066 0 R</c> —
    /// names no section: it is not resolved, which would load an object while the index is still being read,
    /// and the sections it should have named are reported missing, to be looked for by rebuilding the index. A
    /// negative integer is no offset either.
    /// </remarks>
    private long SectionOffset(PdfDictionary trailer, PdfName key, long section)
    {
        var value = trailer.GetRaw(key);

        if (value is null)
        {
            return -1;
        }

        if (value is PdfInteger { Value: >= 0 } offset)
        {
            return offset.Value;
        }

        _indexIncomplete = true;
        _structure.Add(new XRefSectionRecord("/" + key.Value, -1, section)
        {
            State = XRefSectionState.NotFound,
            Fault = $"is {DescribeValue(value)}, not an offset",
        });
        _diagnostics.Warn(
            PdfDiagnosticCodes.XRefSectionMissing,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The /{key.Value} of the cross-reference section at offset {section} is not an offset; the sections it names are looked for by rebuilding the index."),
            section);
        return -1;
    }

    /// <summary>Says what a value written where an offset belongs is, as the file wrote it.</summary>
    private static string DescribeValue(PdfObject value) => value switch
    {
        PdfReference reference => string.Create(
            CultureInfo.InvariantCulture, $"the reference {reference.Id.Number} {reference.Id.Generation} R"),
        PdfInteger integer => string.Create(CultureInfo.InvariantCulture, $"the integer {integer.Value}"),
        PdfName name => $"the name /{name.Value}",
        _ => $"a value of type {value.GetType().Name.Replace("Pdf", string.Empty, StringComparison.Ordinal).ToLowerInvariant()}",
    };

    private void ReadXRefStreamRows(
        ReadOnlySpan<byte> data,
        ref int position,
        ReadOnlySpan<int> widths,
        int rowLength,
        int first,
        int count,
        XRefSectionRecord section)
    {
        if ((long)count * rowLength > data.Length - position)
        {
            // Rows the data does not hold index nothing: the objects they stood for are the index's to find.
            _indexIncomplete = true;
        }

        for (var i = 0; i < count && position + rowLength <= data.Length; i++, position += rowLength)
        {
            var row = data.Slice(position, rowLength);
            var cursor = 0;

            // A zero-width type field means the type is 1: an ordinary object at an offset.
            var type = widths[0] == 0 ? 1 : (int)ReadField(row, ref cursor, widths[0]);
            var second = ReadField(row, ref cursor, widths[1]);
            var third = ReadField(row, ref cursor, widths[2]);
            var number = first + i;
            section.HighestNumber = Math.Max(section.HighestNumber, number);

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
            // A reference to an object the file does not define is null (ISO 32000-1, 7.3.10): only an index
            // that may have lost entries is rebuilt to look for it, once.
            if (_repaired || !_indexIncomplete)
            {
                // While a rebuilt index takes in what its object streams hold, the object may be in one not
                // expanded yet: that null is not the object's, and is not kept.
                if (_expandingObjectStreams)
                {
                    _reentries++;
                }

                return PdfNull.Instance;
            }

            Repair();
            return _xref.TryGet(id.Number, out entry) ? LoadFromEntry(id, entry) : PdfNull.Instance;
        }

        return LoadFromEntry(id, entry);
    }

    private PdfObject LoadFromEntry(PdfObjectId id, XRefEntry entry)
    {
        var loaded = entry.Kind switch
        {
            XRefEntryKind.Regular => LoadRegularObject(id, entry.Offset + _headerOffset),
            XRefEntryKind.Compressed => LoadCompressedObject(id, entry),
            _ => PdfNull.Instance,
        };

        // Null for an object the index holds in use means the reader gave up on it, unless the object is a
        // literal null: the loads say which.
        if (loaded is { } value)
        {
            _unproduced.Remove(id.Number);
            return value;
        }

        _unproduced.Add(id.Number);
        return PdfNull.Instance;
    }

    /// <summary>
    /// Loads an object the index places at <paramref name="offset"/>, in at most three attempts: where the
    /// index says, in the neighborhood, and wherever a rebuilt index says.
    /// </summary>
    /// <remarks>
    /// The attempts are counted rather than chained. An earlier version let relocation call back into
    /// loading, and a fuzzed file drove the two into each other until the stack ran out — a file killing
    /// the process is the exact outcome the reader exists to prevent.
    /// </remarks>
    private PdfObject? LoadRegularObject(PdfObjectId id, long offset)
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

        // Offsets are commonly off by a few bytes in files from careless tools, so the neighborhood is
        // searched before the index is given up on entirely.
        if (TryFindObjectHeader(id.Number, offset, out var nearby) &&
            TryParseObjectAt(id.Number, nearby, out var relocated))
        {
            _diagnostics.Repair(
                PdfDiagnosticCodes.XRefOffsetAdjusted,
                $"Object {id.Number} was found {nearby - offset} bytes from where the index said.",
                nearby);

            PreserveChainIndex();
            _xref.Set(id.Number, XRefEntry.Regular(nearby - _headerOffset, id.Generation));
            return relocated;
        }

        if (_repaired)
        {
            return null;
        }

        Repair();

        return _xref.TryGet(id.Number, out var entry) &&
               entry.Kind == XRefEntryKind.Regular &&
               TryParseObjectAt(id.Number, entry.Offset + _headerOffset, out var afterRebuild)
            ? afterRebuild
            : null;
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
    /// sees a cut token, a missing <c>endstream</c> or an object that stops mid-way: artifacts of the window,
    /// not of the file, and reporting them would put a syntax error the file does not have in the document's
    /// diagnostics — with the real ones reported once per attempt. What nested loads report, such as a
    /// relocated <c>/Length</c> object, goes straight to the document's diagnostics and stays there. An object
    /// that still runs past the window once the guard allows no larger one is kept as far as it was read, and
    /// the guard is reported in place of what the cut made the parser notice.
    /// </remarks>
    private bool TryParseAt(long offset, int number, PdfLimit limit, out PdfObject value, int initialWindow = InitialObjectWindow) =>
        TryParseNumberedAt(offset, number, limit, out value, out _, initialWindow);

    /// <summary>
    /// Parses what starts at an exact offset, as <see cref="TryParseAt"/> does, and gives the number of the object
    /// found there — 0 for a direct object.
    /// </summary>
    private bool TryParseNumberedAt(
        long offset, int number, PdfLimit limit, out PdfObject value, out int foundNumber, int initialWindow = InitialObjectWindow)
    {
        value = PdfNull.Instance;
        foundNumber = 0;

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
                else
                {
                    foundNumber = found.Number;
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
                    MarkCutByGuard(parsed, offset + window.Length);
                }

                _pending.MoveTo(_diagnostics, mark);
                RecordEndObj(number, foundNumber, parser.EndObj, cut, offset);
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

    /// <summary>
    /// Records whether <c>endobj</c> follows an object read at <paramref name="offset"/>: it does not when another
    /// token follows it, or when the file ends first — not when the window the reader offered ended first, a
    /// stream's data running past it included, where what follows was never seen.
    /// </summary>
    private void RecordEndObj(int number, int found, EndObjState state, bool cut, long offset)
    {
        if (number == DirectObject || found <= 0)
        {
            return;
        }

        if (state == EndObjState.Absent || (state == EndObjState.Unseen && !cut))
        {
            _endObjMissing[found] = offset;
        }
        else if (state == EndObjState.Present)
        {
            _endObjMissing.Remove(found);
        }
    }

    /// <summary>
    /// Marks a stream whose data reaches the edge of the window a guard stopped at: the reader cut it there,
    /// where its <c>endstream</c> was still to be found, and its lost tail is the guard's, not the file's.
    /// </summary>
    private static void MarkCutByGuard(PdfObject parsed, long windowEnd)
    {
        if (parsed is PdfStream { Data: FileStreamData data } && data.Position + data.Length >= windowEnd)
        {
            data.MarkCutByGuard();
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
        _guardsReached++;

        if (!_limitsReached.Contains((limit, position)))
        {
            _guard.Reach(limit, _diagnostics, what, position);
            _limitsReached.Add((limit, position));
        }
    }

    private bool TryFindObjectHeader(int number, long approximateOffset, out long actualOffset) =>
        TryFindObjectHeader(_source, number, approximateOffset, out actualOffset);

    /// <summary>
    /// Looks for the header of object <paramref name="number"/> within <see cref="NearbySearchRadius"/> bytes either
    /// side of <paramref name="approximateOffset"/>, where careless writers leave an object their index misplaced,
    /// and gives the first found.
    /// </summary>
    internal static bool TryFindObjectHeader(PdfFileSource source, int number, long approximateOffset, out long actualOffset)
    {
        actualOffset = -1;

        var start = Math.Max(0, approximateOffset - NearbySearchRadius);
        var length = (int)Math.Min(NearbySearchRadius * 2, source.Length - start);

        if (length <= 0)
        {
            return false;
        }

        using var window = source.GetWindow(start, length);
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
    /// Reads which objects the object stream <paramref name="number"/> holds, in order, from the header its data
    /// starts with — without keeping the stream, nor what it decodes to: the validation rules read each once, and
    /// memory follows the largest of them rather than their sum.
    /// </summary>
    /// <param name="number">The object stream's number.</param>
    /// <param name="offset">Where the chain's index places it, in the file.</param>
    /// <param name="numbers">The object numbers its header lists, in order; empty unless it was read.</param>
    /// <param name="fault">What is wrong with it, in words, when it could not be read and is there.</param>
    internal ObjectStreamHeaderResult ReadObjectStreamHeader(int number, long offset, out int[] numbers, out string? fault)
    {
        numbers = [];
        fault = null;

        var guards = _guardsReached;
        var found = TryParseObjectAt(number, offset, out var value) ||
            (TryFindObjectHeader(number, offset, out var nearby) && TryParseObjectAt(number, nearby, out value));

        // A dictionary one of the reader's limits cut is the limit's, not the file's: what it would have said is unknown.
        if (_guardsReached != guards)
        {
            return ObjectStreamHeaderResult.CutByLimit;
        }

        if (!found)
        {
            return ObjectStreamHeaderResult.NotFound;
        }

        if (value is not PdfStream stream || !stream.Dictionary.IsOfType(PdfName.ObjStm))
        {
            fault = value is PdfStream ? "is a stream that is not of /Type /ObjStm" : "is not a stream";
            return ObjectStreamHeaderResult.NotAnObjectStream;
        }

        var count = stream.Dictionary.GetInteger(PdfName.N, -1);
        var first = stream.Dictionary.GetInteger(PdfName.First, -1);

        if (count < 0 || first < 0)
        {
            fault = "its /N or its /First is missing or negative";
            return ObjectStreamHeaderResult.Unreadable;
        }

        // Each entry of the header costs at least "0 0 ", so a count far beyond what the header could hold is a
        // lie, and believing it would mean allocating an array the file asked for.
        if (count > (first / 2) + 1)
        {
            fault = string.Create(
                CultureInfo.InvariantCulture, $"its /N declares {count} objects, more than its /First of {first} leaves room for");
            return ObjectStreamHeaderResult.Unreadable;
        }

        var mark = _diagnostics.GetMark();
        var data = stream.Decode(_diagnostics);
        var cut = LimitSince(_diagnostics, mark) || stream.Data.CutByGuard;

        if (first > data.Length)
        {
            fault = string.Create(
                CultureInfo.InvariantCulture, $"it decodes to {data.Length} bytes, fewer than its /First of {first}");
            return cut ? ObjectStreamHeaderResult.CutByLimit : ObjectStreamHeaderResult.Unreadable;
        }

        var lexer = new PdfLexer(data.Span[..(int)first]);
        var header = new int[count];

        for (var i = 0; i < header.Length; i++)
        {
            var numberToken = lexer.Read();
            var offsetToken = lexer.Read();

            if (numberToken.Kind != PdfTokenKind.Integer || offsetToken.Kind != PdfTokenKind.Integer ||
                numberToken.Integer is <= 0 or > int.MaxValue)
            {
                fault = string.Create(
                    CultureInfo.InvariantCulture, $"its header lists {i} of the {count} objects its /N declares, then something else");
                return ObjectStreamHeaderResult.Unreadable;
            }

            header[i] = (int)numberToken.Integer;
        }

        numbers = header;
        return ObjectStreamHeaderResult.Read;
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

    private PdfObject? LoadCompressedObject(PdfObjectId id, XRefEntry entry)
    {
        var contents = GetObjectStream(entry.ObjectStreamNumber, id.Number, out var notYet);

        if (contents is null)
        {
            // A stream that cannot be read yet, because its own dictionary is being read, says nothing of the
            // object: it is read again when asked again.
            return notYet ? PdfNull.Instance : null;
        }

        return contents.Parse(entry.IndexInObjectStream, id.Number, this, _diagnostics);
    }

    /// <summary>
    /// Gives what object stream <paramref name="number"/> holds, decoding it the first time, or null when it cannot
    /// serve its objects — for good, or, when <paramref name="notYet"/> says so, only while its own dictionary is
    /// being read.
    /// </summary>
    private ObjectStreamContents? GetObjectStream(int number, int wanted, out bool notYet)
    {
        notYet = false;

        if (_objectStreams.TryGetValue(number, out var cached))
        {
            return cached;
        }

        // The stream's own dictionary is being read, and names an object the stream holds — its /Length —: the
        // stream cannot be read yet, and will be once its dictionary is (#51). Nothing is recorded.
        if (_loading.Contains(number))
        {
            NullWhileDecoding(wanted);
            notYet = true;
            ReportObjectStreamNeedsItself(number, wanted);
            return null;
        }

        // Marked while it is decoded: an object it holds, asked for meanwhile, reads as null rather than
        // recursing, and is not kept as null.
        _objectStreamsLoading.Add(number);

        try
        {
            var reentries = _reentries;
            var loaded = GetObject(new PdfObjectId(number));

            // The stream itself could not be loaded from here — too deep, or needed by what is being loaded —: that
            // says nothing of it, and it is read again when one of its objects is asked for again.
            if (loaded is PdfNull && reentries != _reentries)
            {
                notYet = true;
                return null;
            }

            // What decoding without an object the stream holds gives is all the stream can give, and is kept; decoded
            // again once the budget let it go, the stream reads the same objects as null, and gives the same.
            var contents = loaded is PdfStream stream ? ObjectStreamContents.TryCreate(stream, _diagnostics) : null;

            KeepObjectStream(number, contents);
            return contents;
        }
        finally
        {
            _objectStreamsLoading.Remove(number);
        }
    }

    /// <summary>
    /// Keeps an object stream's contents, letting go of the oldest ones kept while their data and its own exceed
    /// <see cref="ObjectStreamBudget"/>.
    /// </summary>
    private void KeepObjectStream(int number, ObjectStreamContents? contents)
    {
        if (_objectStreams.TryGetValue(number, out var previous) && previous is not null)
        {
            _objectStreamBytes -= previous.Length;
        }

        _objectStreams[number] = contents;

        if (contents is null)
        {
            return;
        }

        while (_objectStreamBytes + contents.Length > ObjectStreamBudget && _objectStreamOrder.TryDequeue(out var oldest))
        {
            // The queue can name a number twice, once replaced; letting it go early costs a decoding, nothing else.
            if (oldest != number && _objectStreams.Remove(oldest, out var released) && released is not null)
            {
                _objectStreamBytes -= released.Length;
            }
        }

        _objectStreamOrder.Enqueue(number);
        _objectStreamBytes += contents.Length;
    }

    private void Cache(int number, PdfObject value)
    {
        if (_cache.Count >= _cacheCapacity && _cacheOrder.TryDequeue(out var oldest))
        {
            _cache.Remove(oldest);
        }

        if (_cache.TryAdd(number, value))
        {
            _cacheOrder.Enqueue(number);
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

        PreserveChainIndex();
        _xref.Clear();
        _cache.Clear();
        _cacheOrder.Clear();
        _objectStreams.Clear();
        _objectStreamOrder.Clear();
        _objectStreamBytes = 0;
        _nullWhileDecoding.Clear();

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
        _expandingObjectStreams = true;

        try
        {
            return ExpandEachObjectStream();
        }
        finally
        {
            _expandingObjectStreams = false;
        }
    }

    private ExceptionDispatchInfo? ExpandEachObjectStream()
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
            var reentries = _reentries;

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

            // Contents decoded without an object the index did not hold yet are decoded again when first asked for.
            if (reentries == _reentries)
            {
                KeepObjectStream(number, contents);
            }

            for (var index = 0; index < contents.Count; index++)
            {
                // An object written directly in the file wins over a copy inside an object stream.
                _xref.TryAdd(contents.NumberAt(index), XRefEntry.Compressed(number, index));
            }
        }

        return reached;
    }

    /// <summary>
    /// Looks for an object of <c>/Type /Catalog</c> among the indexed ones, and makes the first it finds the
    /// trailer's <c>/Root</c>.
    /// </summary>
    /// <remarks>
    /// It runs on the index the chain gave, and on a rebuilt one. Loading a candidate from the chain's index can
    /// rebuild it — the candidate's entry being broken —, and the rebuild runs a search of its own: the search it
    /// interrupted stops there, over the numbers it copied before it began.
    /// </remarks>
    private ExceptionDispatchInfo? FindCatalog()
    {
        ExceptionDispatchInfo? reached = null;
        var rebuilt = _repaired;
        var numbers = new List<int>(_xref.Entries.Keys);

        foreach (var number in numbers)
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

            if (_repaired != rebuilt)
            {
                break;
            }

            if (candidate.AsDictionary() is { } dictionary && dictionary.IsOfType(PdfName.Catalog))
            {
                Trailer.Set(PdfName.Root, new PdfReference(new PdfObjectId(number), this));
                _structure.CatalogFoundAs = number;
                _diagnostics.Repair(
                    PdfDiagnosticCodes.TrailerRootRecovered,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The trailer's /Root does not lead to a document catalog; the catalog was found as object {number}."));
                break;
            }
        }

        return reached;
    }

    /// <summary>
    /// Copies the index the chain gave before the reader changes it, so that the validation rules still judge the
    /// file's own. Once is enough: the copy is taken before the first change.
    /// </summary>
    private void PreserveChainIndex()
    {
        if (_structure.ChainRead && _chainIndex is null)
        {
            _chainIndex = _xref.CopyEntries();
        }
    }

    private sealed class FileStreamData(PdfFileSource source, long offset, int length, PdfLimitGuard guard) : PdfStreamData
    {
        private byte[]? _bytes;
        private bool _cutByGuard;

        public override int Length => length;

        public override long Position => offset;

        internal override PdfLimitGuard LimitGuard => guard;

        internal override bool CutByGuard => _cutByGuard;

        public void MarkCutByGuard() => _cutByGuard = true;

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

        /// <summary>
        /// Where each number lies in the header, the first place when it lies in several; built the first time an
        /// entry's index is wrong, so that an index every entry of the file gets wrong costs a lookup, not a search.
        /// </summary>
        private Dictionary<int, int>? _indexes;

        private ObjectStreamContents(ReadOnlyMemory<byte> data, int[] numbers, int[] offsets, int first)
        {
            _data = data;
            _numbers = numbers;
            _offsets = offsets;
            _first = first;
        }

        public int Count => _numbers.Length;

        /// <summary>Gets the length of the decoded data, which is what keeping the contents costs.</summary>
        public int Length => _data.Length;

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

        /// <summary>Parses the object the stream holds under <paramref name="expectedNumber"/>, or gives null when it holds none.</summary>
        public PdfObject? Parse(int index, int expectedNumber, IPdfObjectSource source, PdfDiagnostics diagnostics)
        {
            if (index < 0 || index >= _numbers.Length)
            {
                // The index in the entry is a hint; the object number is the truth.
                index = IndexOf(expectedNumber);
                if (index < 0)
                {
                    return null;
                }
            }

            if (_numbers[index] != expectedNumber)
            {
                var corrected = IndexOf(expectedNumber);
                if (corrected < 0)
                {
                    return null;
                }

                diagnostics.Repair(
                    PdfDiagnosticCodes.XRefOffsetAdjusted,
                    $"Object {expectedNumber} was at index {corrected} of its object stream, not {index}.");

                index = corrected;
            }

            var start = _first + _offsets[index];

            if (start < 0 || start >= _data.Length)
            {
                return null;
            }

            var parser = new PdfObjectParser(_data, 0, source, diagnostics, streamData: null);
            parser.Position = start;
            return parser.ParseObject();
        }

        private int IndexOf(int number)
        {
            if (_indexes is null)
            {
                _indexes = new Dictionary<int, int>(_numbers.Length);

                for (var i = 0; i < _numbers.Length; i++)
                {
                    _indexes.TryAdd(_numbers[i], i);
                }
            }

            return _indexes.TryGetValue(number, out var index) ? index : -1;
        }
    }
}
