using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO.XRef;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO;

/// <summary>
/// Indexes a PDF file and reads its objects on demand.
/// </summary>
/// <remarks>
/// Opening a document reads the cross-reference chain and resolves the trailer's <c>/Root</c>; it loads other objects
/// only to check the <c>/Length</c> of a cross-reference stream the chain could not read it for, to look for a catalog
/// that <c>/Root</c> does not lead to, or to rebuild the index. While the chain is read, nothing is loaded: a value the
/// sections refer to is read only where a section already read places it (<see cref="ReadWithoutLoading"/>), so that
/// the index the chain gives is the file's own. Objects are parsed the first time something
/// asks for them and kept in a bounded cache, so memory follows what the caller touches rather than the size of the
/// file. When the index turns out to be wrong — which real files manage in a remarkable number of ways — the reader
/// rebuilds it by scanning, and says so in the diagnostics. A rebuild loads every object written directly in the
/// file, to take in those its object streams hold.
/// </remarks>
internal sealed class PdfFileReader : IPdfObjectSource, IPdfStreamDataProvider, IDisposable
{
    internal const int InitialObjectWindow = 8 * 1024;
    internal const int XRefWindow = 64 * 1024;

    /// <summary>What the codes of the faults of the object syntax start with.</summary>
    private const string SyntaxCodePrefix = "syntax.";

    /// <summary>How much of a section is read to tell a classic table from a cross-reference stream.</summary>
    internal const int XRefProbeLength = 32;

    /// <summary>
    /// Most entries a subsection may claim. Not a guard a valid file reaches: a classic table holds its rows
    /// within <see cref="PdfReaderLimits.MaxXRefSectionLength"/>, a stream within its decoded length, and a
    /// count past both describes rows that are not there.
    /// </summary>
    private const int MaxSubsectionEntries = 50_000_000;

    /// <summary>
    /// How much of the file's start is searched for <c>%PDF-</c>. Not a guard a valid file reaches: ISO 32000-1
    /// (7.5.2) puts the header on the file's first line. A file that has none within these bytes is reported as
    /// lacking one, under <see cref="PdfDiagnosticCodes.HeaderMissing"/>.
    /// </summary>
    private const int HeaderSearchLength = 4096;

    /// <summary>
    /// How much of the file's end is searched for <c>startxref</c>. Not a guard a valid file reaches: ISO 32000-1
    /// (7.5.5) puts <c>startxref</c> and its offset just before the <c>%%EOF</c> that ends the file. A file that has
    /// none within these bytes has its index rebuilt by scanning, which is reported.
    /// </summary>
    internal const int TailSearchLength = 4096;

    /// <summary>
    /// How far either side of the offset it was given the reader looks for an object or a cross-reference section
    /// that is not there. Not a guard a valid file reaches: a valid file's index places every object and every
    /// section exactly. An object not found this near is looked for by rebuilding the index; a section, reported
    /// missing.
    /// </summary>
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

    /// <summary>The most a search for a stream's <c>endstream</c> reads of the file at once.</summary>
    internal const int EndStreamSearchChunk = 64 * 1024;

    /// <summary>
    /// How many bytes each read of a search for <c>endstream</c> repeats of the one before it: one less than the keyword
    /// and the end-of-line before it that the data leaves out, which are then whole in one read wherever an edge cuts
    /// them — as are <c>obj</c> and the byte after it, which decide an object header with what <see cref="HeaderText"/>
    /// carries into the read of the reads before.
    /// </summary>
    internal const int EndStreamSearchOverlap = 10;

    /// <summary>
    /// How many times the length of the file the searches for <c>endstream</c> of one document may read, together,
    /// before a stream whose declared length no <c>endstream</c> follows keeps it without a search.
    /// </summary>
    /// <remarks>
    /// Not a guard a valid file reaches (ADR 34): a valid file is never searched. Each search reads from a stream's
    /// data to the first object header the bytes hold after it, or to the next object the index as written places,
    /// whichever is nearer, and its reads grow from twice what the parser holds of the data
    /// (<see cref="EndStreamSearchFirstRead"/>): it reads at most about twice what its object spans, and the searches of
    /// objects whose headers follow one another read disjoint stretches — the file twice over at most. Only searches that
    /// share a stretch read more: objects that overlap, the header of one inside the dictionary of another, before its
    /// data; or a stream whose header the search does not take — a regular character glued before its number, where a
    /// rebuild's scan takes the digits after it for one — and the index as written does not place, which only a rebuilt
    /// one does. This keeps what they read in proportion to the file, as a window keeps the parsing of their
    /// dictionaries. Which streams keep their declared length unsearched past it depends on the order they are read in:
    /// crafted files reach it, and no document of the corpus comes near, whose searches read 0.35 of a file at most.
    /// </remarks>
    internal const int EndStreamSearchPasses = 4;

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

    /// <summary>
    /// How many references <see cref="Follow"/> follows from a value, as <see cref="PdfReference.Resolve"/> does — it fetches
    /// one more, and reads null whatever that one holds (#173) —: a value still a reference past them reads as null there,
    /// and cannot be read here. No valid file reaches it: a valid file never chains references.
    /// </summary>
    private const int MaxReferenceLinks = 31;

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
    /// The faults of the syntax the document's diagnostics keep: an object parsed again — after the cache let it go, the
    /// index was rebuilt, or the validator read it — is not reported again. As many as the diagnostics keep, at most.
    /// </summary>
    private readonly HashSet<PdfDiagnostic> _syntaxReported = [];

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
    /// rebuild —; null while <see cref="_xref"/> is still that index. Nothing changes it while the chain is read, since
    /// nothing is loaded then (<see cref="_readingChain"/>).
    /// </summary>
    private PdfXRefTable? _chainIndex;

    /// <summary>
    /// Whether the cross-reference chain is being read. Nothing is loaded meanwhile: an object is read only where a
    /// section already read places it, and nothing is corrected, rebuilt, cached or recorded (#182).
    /// </summary>
    private bool _readingChain;

    /// <summary>
    /// What <see cref="ReadWithoutLoading"/> read of each object while the chain is read, null for one it could not: an
    /// object referred to again is not parsed again, as the cache spares a load. The entry it was read from never changes
    /// while the chain is read, so neither does what it gives; the whole is let go once the chain is read, and nothing of
    /// it outlives the chain.
    /// </summary>
    private Dictionary<PdfObjectId, PdfObject?>? _readWhileChainIsRead;

    /// <summary>
    /// The cross-reference streams the chain read whose indirect <c>/Length</c> it could not read, by where their data
    /// starts: each <c>/Length</c> is read once the chain is read, and checked against the data the chain took.
    /// </summary>
    private Dictionary<long, DeferredLength>? _deferredLengths;

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
    /// The objects a guard cut, read only as far as the limit let the reader go: what they hold past it is unknown,
    /// so the validation rules judge nothing of their shape.
    /// </summary>
    private readonly HashSet<int> _cutAtLimit = [];

    /// <summary>
    /// The objects read whose value <c>endobj</c> does not follow, and where each was read. An object stream's
    /// members have none to follow them, and are never in it.
    /// </summary>
    private readonly Dictionary<int, long> _endObjMissing = [];

    /// <summary>
    /// What was found of each stream read whose length the file did not confirm, by object number, as the reading kept
    /// last found it: a sound file records nothing.
    /// </summary>
    private readonly Dictionary<int, StreamLengthFault> _streamLengthFaults = [];

    /// <summary>
    /// What each search of the file for a stream's <c>endstream</c> found, by where the stream's data starts: the file
    /// is searched once for each stream, however many times it is parsed.
    /// </summary>
    private readonly Dictionary<long, StreamEndSearch> _endStreamSearches = [];

    /// <summary>
    /// Where the data starts of each stream whose length fault a reading the reader kept reported: the stream is not
    /// reported again when it is parsed again.
    /// </summary>
    private readonly HashSet<long> _lengthFaultsReported = [];

    /// <summary>What the object streams' reports said already: each fault is reported once, however often its stream is decoded.</summary>
    private readonly ObjectStreamReports _objectStreamReports = new();

    /// <summary>How many bytes the searches for <c>endstream</c> read, which <see cref="EndStreamSearchPasses"/> bounds.</summary>
    private long _endStreamSearchBytes;

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
    /// guard stopped the chain or a table before its end, a cross-reference stream holds fewer rows than it
    /// declares, or a row was refused. Only then is an object the index lacks looked for by rebuilding it; otherwise a reference to it
    /// is null, as the specification says.
    /// </summary>
    private bool _indexIncomplete;

    /// <summary>
    /// The object numbers whose row a section of the chain refused, or null while none was: the sections the chain reads
    /// after it describe older revisions, whose rows must not stand for the current one.
    /// </summary>
    private HashSet<int>? _refusedNumbers;

    public PdfFileReader(
        PdfFileSource source, PdfDiagnostics diagnostics, PdfLimitGuard guard, int cacheCapacity, bool ownsSource)
    {
        _source = source;
        _diagnostics = diagnostics;
        _guard = guard;
        _pending = new PdfDiagnostics { Capacity = diagnostics.Capacity, KeptIn = diagnostics };
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

    /// <summary>Gets the object numbers the index holds an entry for, free ones included.</summary>
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
    /// <remarks>
    /// An entry gives the generation of the row that placed it, or, for one a rebuild made or a relocation moved, the
    /// generation the header found there gives, whatever the row gave (#118). A row whose header, at the offset the row
    /// gives, contradicts its generation keeps its own, as the chain's index does:
    /// <see cref="Validation.PdfValidationRuleIds.XRefGenerationMismatch"/> reports it.
    /// </remarks>
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

        // While the chain is read, what a load could do — correct an entry, rebuild the index, cache a null for an object a
        // section still to be read places — would be served afterward as what the file wrote (#182).
        if (_readingChain)
        {
            return ReadWithoutLoading(id) ?? PdfNull.Instance;
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
    /// Reads object <paramref name="id"/> while the chain is read, without loading it: where a section already read places
    /// it, at the offset its row gives, under the number and generation the reference gives; or gives null when it cannot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing is corrected, rebuilt, cached, recorded or reported. An object the sections read so far do not place, or
    /// place in an object stream, or at an offset that holds another object or none, is one the chain cannot read: a load
    /// could only read it by changing the index it is building — correcting an entry, rebuilding the index —, or would
    /// keep a null for an object a section still to be read places, and either would be served afterward as what the file
    /// wrote (#182). A stream is not read either: no value the chain reads is one.
    /// </para>
    /// <para>
    /// The object is read within <see cref="PdfReaderLimits.MaxObjectLength"/>, as a load reads it; one the guard cuts is
    /// left unread, and the guard is neither reported nor counted: the chain's sections are cut only by their own windows
    /// and decoding, and a guard that holds is reached when the object is loaded once the chain is read. The objects read
    /// through it nest as loads do, within <see cref="MaxNestedLoads"/>.
    /// </para>
    /// </remarks>
    private PdfObject? ReadWithoutLoading(PdfObjectId id)
    {
        if (!_xref.TryGet(id.Number, out var entry) || entry.Kind != XRefEntryKind.Regular || entry.Generation != id.Generation)
        {
            return null;
        }

        if (_readWhileChainIsRead?.TryGetValue(id, out var known) == true)
        {
            return known;
        }

        // A read nested too deep, or one that needs itself, says nothing of the object: it is not kept.
        if (!_loading.Add(id.Number))
        {
            return null;
        }

        try
        {
            if (_loading.Count > MaxNestedLoads || !RuntimeHelpers.TryEnsureSufficientExecutionStack())
            {
                return null;
            }

            var read = TryReadUnrecorded(id, entry.Offset + _headerOffset, out var value) ? value : null;
            (_readWhileChainIsRead ??= [])[id] = read;
            return read;
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
    /// or the object stream's, and the cross-reference rules report it. Object 0 is missing whatever its entry says: it
    /// heads the free list and is never in use (ISO 32000-1, 7.5.4), and the reader never reads it.
    /// </remarks>
    internal ObjectPresence GetPresence(int number)
    {
        if (number <= 0)
        {
            return ObjectPresence.Missing;
        }

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
    /// Determines whether object <paramref name="number"/>, as it was read, was cut by one of the reader's limits and
    /// kept as far as the limit let it be read.
    /// </summary>
    internal bool IsCutAtLimit(int number) => _cutAtLimit.Contains(number);

    /// <summary>
    /// Determines whether object <paramref name="number"/>, as it was read, is a stream whose length the file did not
    /// confirm, and gives what the reader found of it.
    /// </summary>
    internal bool TryGetStreamLengthFault(int number, out StreamLengthFault fault) =>
        _streamLengthFaults.TryGetValue(number, out fault);

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
            string.Create(
                CultureInfo.InvariantCulture,
                $"Object {id.Number} is reached through more nested objects than the reader will follow, and reads as null."),
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
    EndObjState? IPdfStreamDataProvider.CheckEndStream(long absoluteOffset)
    {
        if (absoluteOffset < 0 || absoluteOffset >= _source.Length)
        {
            return null;
        }

        Span<byte> tail = stackalloc byte[PdfObjectParser.EndObjLookahead];
        var available = (int)Math.Min(tail.Length, _source.Length - absoluteOffset);
        var read = _source.Read(absoluteOffset, tail[..available]);
        return PdfObjectParser.ReadStreamEnd(tail[..read], absoluteOffset + read >= _source.Length);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The search stops at the nearer of two places, or at the end of the file: the next object after the start of the data
    /// that <see cref="SearchIndex"/> places — the index the chain gave, as the file wrote it, or, for a document whose chain
    /// gave none, the one rebuilt as it opened, as it stood once opened —, and the first object header the file's bytes hold
    /// after the start of the data (<see cref="HeaderText"/>). Stopping there keeps an <c>endstream</c> that belongs to
    /// a later object from ending this one; when none lies before it, the declared length is kept. The stream's own entry is
    /// no next object: an entry that missed its object may place it inside its own data.
    /// <para>
    /// Neither bound changes once the document has opened, whatever is read: the bytes do not, nor does the index as written.
    /// The index the reader reads with is left out on purpose, though a rebuild of it places objects the file wrote no entry
    /// for: it changes as objects are read — rebuilt when a broken entry is met, corrected when an object is found near its
    /// entry —, and a stream it bounded would end where it does according to what was read before it. The headers the bytes
    /// hold stand in for it: they place what only a rebuilt index would — a copy of an object the file superseded, an object
    /// its entries mark free —, and a stream that lost its <c>endstream</c> stops at the object after it rather than running
    /// on to the next one the index as written places. That index places in turn what the bytes no longer show: the objects
    /// a damaged stretch of the file erased, as a block of zeros does. What a search keeps depends on the file's bytes and on
    /// the index as written, then. It can still depend on the number a stream is first read under — its own entry, stepped
    /// over, is that number's —, on the rebuild's scan for trailers, which reads a stream under none, and, once the
    /// document's searches have read as much as they may (<see cref="EndStreamSearchPasses"/>), on which streams were
    /// searched first: crafted files make it do so, no document of the corpus does (#138).
    /// </para>
    /// <para>
    /// This bound is no guard (ADR 34): a valid file's <c>endstream</c> follows its declared length (ISO 32000-1,
    /// 7.3.8.1) — so do those of the 35,873 streams of the corpus whose length is confirmed, a gap of 0 to 2 bytes
    /// between them —, and only an invalid file is searched at all. What it reads is the stretch between the stream and
    /// the next object, once for each stream: the result is kept, and a stream parsed again — after the cache let it go,
    /// or a rebuild of the index — is not searched again. What the searches read together is bounded by
    /// <see cref="EndStreamSearchPasses"/>.
    /// </para>
    /// </remarks>
    StreamEndSearch IPdfStreamDataProvider.FindEndStream(int number, long absoluteDataStart, ReadOnlySpan<byte> buffered)
    {
        if (_endStreamSearches.TryGetValue(absoluteDataStart, out var known))
        {
            return known;
        }

        StreamEndSearch search;

        if (_endStreamSearchBytes >= EndStreamSearchPasses * _source.Length)
        {
            search = new StreamEndSearch(null, null, EndObjState.Unknown, Searched: false);
        }
        else
        {
            var nextObject = NextObjectStart(number, absoluteDataStart);
            var dataEnd = SearchEndStream(absoluteDataStart, buffered, nextObject ?? _source.Length, out var header);

            // A header the bytes hold is looked for only before the next object the index places: found, it is the nearer.
            var stop = header >= 0 ? header : nextObject;

            // Past int.MaxValue bytes, no length the reader holds a stream by reaches the endstream found: only a file of
            // more than 2 GB can put one there, and the declared length is kept, as when none is found.
            search = dataEnd >= 0 && dataEnd - absoluteDataStart <= int.MaxValue
                ? new StreamEndSearch(
                    (int)(dataEnd - absoluteDataStart),
                    stop,
                    ((IPdfStreamDataProvider)this).CheckEndStream(dataEnd) ?? EndObjState.Unseen)
                : new StreamEndSearch(null, stop, EndObjState.Unknown);
        }

        _endStreamSearches[absoluteDataStart] = search;
        return search;
    }

    /// <inheritdoc/>
    PdfObject IPdfStreamDataProvider.ResolveLength(PdfObjectId id, out ObjectPresence presence)
    {
        if (_readingChain)
        {
            // A /Length no section read so far places is read once the chain is read: the data is found by its endstream
            // meanwhile, and the length checked then (#182).
            var read = ReadWithoutLoading(id);
            presence = read is null ? ObjectPresence.Deferred : ObjectPresence.Defined;
            return read ?? PdfNull.Instance;
        }

        var reentries = _reentries;
        var value = GetObject(id);

        // A null a guard against re-entry gave says nothing of the object: it could not be read from here.
        presence = value is PdfNull && reentries != _reentries ? ObjectPresence.Unproduced : GetPresence(id.Number);
        return value;
    }

    /// <inheritdoc/>
    bool IPdfStreamDataProvider.IsLengthFaultReported(long absoluteDataStart) =>
        _lengthFaultsReported.Contains(absoluteDataStart);

    /// <summary>
    /// Gets the index whose offsets bound the searches for <c>endstream</c>: the one the chain gave, as the file wrote
    /// it, or, for a document whose chain gave none, the one rebuilt as it opened.
    /// </summary>
    /// <remarks>
    /// Neither changes once the document has opened, whatever is read. The reader changes the chain's index — an object
    /// found near its entry, a rebuild — only after <see cref="PreserveChainIndex"/> has copied it. An index rebuilt as
    /// the document opened is the one the reader keeps, and no copy of it is needed: the rebuild runs once, and loads
    /// every object it places directly as it takes in the object streams, so that an object found near its entry is
    /// found, and its entry changed, before the document has opened; object streams add entries that place no offset.
    /// While the chain is read, and while a document whose chain gave none is rebuilt, the index being made bounds the
    /// searches made meanwhile, which opening makes the same whatever is read after it.
    /// </remarks>
    private PdfXRefTable SearchIndex => ChainIndex ?? _xref;

    /// <summary>
    /// Gives where the next object after <paramref name="position"/> starts, as <see cref="SearchIndex"/> places it,
    /// object <paramref name="number"/>'s own entry aside; null when it places none before the end of the file.
    /// </summary>
    /// <remarks>
    /// Offsets in an index count from the header, which <see cref="_headerOffset"/> places in the file; an offset past
    /// the end of the file places nothing. The index keeps its offsets sorted once asked, so that a lookup costs a
    /// logarithm of it, however many streams are searched. The object's own entry is stepped over: the object read,
    /// relocated from an entry that missed it, may have that entry past the start of its data. Its entry in the index the
    /// reader reads with is not looked at: that one changes as objects are read, and the bound would change with it.
    /// </remarks>
    private long? NextObjectStart(int number, long position)
    {
        var index = SearchIndex;
        var next = index.FirstOffsetAfter(position - _headerOffset);

        // Offsets come back each greater than the last, so the object's own is stepped over once at most.
        if (index.TryGet(number, out var own) && own.Kind == XRefEntryKind.Regular && next == own.Offset)
        {
            next = index.FirstOffsetAfter(next);
        }

        return next < _source.Length - _headerOffset ? next + _headerOffset : null;
    }

    /// <summary>
    /// Gives how much of the file the first read of a search for <c>endstream</c> past the parser's window reads: twice what
    /// the parser holds of the data, <paramref name="buffered"/> bytes — twice what each read repeats at least —, up to
    /// <see cref="EndStreamSearchChunk"/>. Each read after it reads twice as much as the one before, up to that too.
    /// </summary>
    /// <remarks>
    /// The data runs past the window, so the object spans at least what the parser holds of it: a search stopped by a
    /// header it could not know of before reading it reads at most about twice what its object spans, whatever window the
    /// object was parsed through — the default 8 KB, or less when <see cref="PdfReaderLimits.MaxObjectLength"/> is set
    /// lower —, and one whose <c>endstream</c> lies no further past the window than the parser holds of the data, as a
    /// length a few bytes off leaves it, reads the file once.
    /// </remarks>
    internal static int EndStreamSearchFirstRead(int buffered) =>
        (int)Math.Min(2L * Math.Max(buffered, EndStreamSearchOverlap), EndStreamSearchChunk);

    /// <summary>
    /// Finds the first <c>endstream</c> after <paramref name="dataStart"/> that ends before <paramref name="limit"/> and
    /// before the first object header the bytes hold — in the bytes the parser holds, then in the file past them —, and
    /// returns where the data ends, the end-of-line before the keyword left out; -1 when there is none, and then
    /// <paramref name="header"/> is where that header starts, when one stopped the search, or -1.
    /// </summary>
    /// <remarks>
    /// The file is read forward through windows the source lends, each repeating the last
    /// <see cref="EndStreamSearchOverlap"/> bytes of the one before, each twice as large as the one before from
    /// <see cref="EndStreamSearchFirstRead"/> up to <see cref="EndStreamSearchChunk"/>, and each starting further on than the
    /// last whatever the source returns: the search ends at <paramref name="limit"/>, at the end of the file, at a header,
    /// or at a source that has nothing more to give, and reads no byte past the end of the file. The file is read once, but
    /// for what each read repeats, and each read is searched for both: a header only before the <c>endstream</c> it holds,
    /// with what <see cref="HeaderText"/> carries into it of the reads before.
    /// </remarks>
    private long SearchEndStream(long dataStart, ReadOnlySpan<byte> buffered, long limit, out long header)
    {
        Span<byte> carried = stackalloc byte[HeaderText.CarriedLength];
        Span<long> carriedAt = stackalloc long[HeaderText.CarriedLength];
        var text = new HeaderText(carried, carriedAt, dataStart);

        var inBuffer = (int)Math.Clamp(limit - dataStart, 0, buffered.Length);
        text.Read(buffered[..inBuffer], dataStart);
        var found = SearchStretch(text, endsRange: dataStart + inBuffer >= limit, out header);

        if (found >= 0 || header >= 0)
        {
            return found;
        }

        var end = Math.Min(limit, _source.Length);

        if (dataStart + buffered.Length >= end)
        {
            return -1;
        }

        // Each window starts before the end, and the next one further on than it: the loop ends at one of the returns.
        var position = dataStart + Math.Max(0, buffered.Length - EndStreamSearchOverlap);
        var size = EndStreamSearchFirstRead(buffered.Length);
        text.CarryTo(position);

        while (true)
        {
            using var window = _source.GetWindow(position, (int)Math.Min(size, end - position));
            var span = window.Memory.Span;
            _endStreamSearchBytes += span.Length;

            if (span.IsEmpty)
            {
                return -1;
            }

            text.Read(span, position);
            found = SearchStretch(text, endsRange: position + span.Length >= end, out header);

            if (found >= 0 || header >= 0 || position + span.Length >= end)
            {
                return found;
            }

            position += Math.Max(1, span.Length - EndStreamSearchOverlap);
            text.CarryTo(position);
            size = Math.Min(2 * size, EndStreamSearchChunk);
        }
    }

    /// <summary>
    /// Searches one read of a search for <c>endstream</c> — the parser's bytes, or a window of the file — for the first
    /// <c>endstream</c>, and for the first object header before it: returns where the data ends in the file, the
    /// end-of-line before the keyword left out, or -1 when the read holds no <c>endstream</c> or a header comes first,
    /// whose start in the file is then <paramref name="header"/>, -1 otherwise.
    /// </summary>
    /// <remarks>
    /// The overlap between reads puts the end-of-line before a keyword first seen in a read inside it, unless the keyword
    /// starts the data.
    /// </remarks>
    /// <param name="text">The bytes read, after what the reads before carry into them.</param>
    /// <param name="endsRange">Whether they end where the search does: at the next object the index places, or the end of the file.</param>
    /// <param name="header">Where the header that stopped the search starts in the file, or -1.</param>
    private static long SearchStretch(in HeaderText text, bool endsRange, out long header)
    {
        var span = text.Bytes;
        var keyword = span.IndexOf("endstream"u8);
        header = text.FindHeader(keyword >= 0 ? keyword : span.Length, endsRange);

        if (keyword < 0 || header >= 0)
        {
            return -1;
        }

        if (keyword > 0 && span[keyword - 1] == (byte)'\n')
        {
            keyword--;
        }

        if (keyword > 0 && span[keyword - 1] == (byte)'\r')
        {
            keyword--;
        }

        return text.PositionOf(keyword);
    }

    /// <summary>
    /// One read of a search for <c>endstream</c> as the rule for object headers sees it (<see cref="FindHeader"/>): its
    /// bytes, after a few that stand for what the search read before them.
    /// </summary>
    /// <remarks>
    /// Whether <c>obj</c> ends an object header depends on the runs of white space and of digits before it, which may run
    /// back past the start of a read, as far as the start of the data. What the reads before hold is carried into each one
    /// as a few bytes that decide every header as they would: the runs of white space or of digits that end where the read
    /// starts, four at most — each run of white space as one space, each run of digits as the digits of its value, or of
    /// 2,147,483,648 for any larger one, which no number nor generation reaches however many digits follow —, and the byte
    /// before them. A header holds four such runs, and asks of the byte before them only whether it ends a token: no run
    /// reads that byte. The start of the data is carried as a space, which ends a token as white space does and holds no
    /// digit. The bytes carried have negative indexes, and each records where the run it stands for starts in the file.
    /// None of the sizes below bounds what the file holds (ADR 34): a run of any length is carried, as what decides a
    /// header — its kind and its value.
    /// </remarks>
    private ref struct HeaderText
    {
        /// <summary>The most runs of white space or digits before <c>obj</c> that decide a header: those it holds.</summary>
        private const int MaxRuns = 4;

        /// <summary>The most digits a run of digits is carried as: those of <see cref="Beyond"/>.</summary>
        private const int MaxDigits = 10;

        /// <summary>The most bytes carried into a read: the byte before the runs, two runs of white space and two of digits.</summary>
        internal const int CarriedLength = 1 + (2 * (1 + MaxDigits));

        /// <summary>The value a run of digits is taken for when it is larger: past any object number and generation.</summary>
        private const long Beyond = (long)PdfObjectId.MaxNumber + 1;

        private readonly Span<byte> _carried;
        private readonly Span<long> _carriedAt;
        private int _carriedLength;
        private ReadOnlySpan<byte> _bytes;
        private long _at;

        /// <summary>Starts a search at <paramref name="dataStart"/>, before which nothing is read.</summary>
        /// <param name="carried">Room for <see cref="CarriedLength"/> bytes carried into a read.</param>
        /// <param name="carriedAt">Room for where the run each of them stands for starts in the file.</param>
        /// <param name="dataStart">Where the data starts in the file.</param>
        public HeaderText(Span<byte> carried, Span<long> carriedAt, long dataStart)
        {
            _carried = carried;
            _carriedAt = carriedAt;
            _carried[0] = (byte)' ';
            _carriedAt[0] = dataStart;
            _carriedLength = 1;
        }

        /// <summary>Gets the bytes of the read.</summary>
        public readonly ReadOnlySpan<byte> Bytes => _bytes;

        /// <summary>Gets the index of the first byte carried into the read: the byte before the runs, which no run reads.</summary>
        private readonly int FirstCarried => -_carriedLength;

        private readonly byte this[int index] => index >= 0 ? _bytes[index] : _carried[_carriedLength + index];

        /// <summary>Takes <paramref name="bytes"/>, read at <paramref name="at"/> in the file, as the read that follows what is carried.</summary>
        public void Read(ReadOnlySpan<byte> bytes, long at)
        {
            _bytes = bytes;
            _at = at;
        }

        /// <summary>Gives where the byte at <paramref name="index"/> is in the file, or, for a byte carried, where its run starts.</summary>
        public readonly long PositionOf(int index) => index >= 0 ? _at + index : _carriedAt[_carriedLength + index];

        /// <summary>
        /// Carries into the next read, which starts at <paramref name="position"/> in this one, the runs of white space and
        /// of digits that end there, and the byte before them.
        /// </summary>
        public void CarryTo(long position)
        {
            Span<long> runAt = stackalloc long[MaxRuns];
            Span<long> runValue = stackalloc long[MaxRuns];
            var end = (int)(position - _at);
            var runs = 0;

            while (runs < MaxRuns && end > FirstCarried + 1 &&
                (PdfCharacters.IsDigit(this[end - 1]) || PdfCharacters.IsWhitespace(this[end - 1])))
            {
                var digits = PdfCharacters.IsDigit(this[end - 1]);
                var start = RunStart(end, digits);
                runAt[runs] = PositionOf(start);
                runValue[runs] = digits ? Value(start, end) : -1;
                end = start;
                runs++;
            }

            // What is carried is read whole before it is written over.
            var before = this[end - 1];
            _carriedAt[0] = PositionOf(end - 1);
            _carried[0] = before;
            var length = 1;

            for (var run = runs - 1; run >= 0; run--)
            {
                var written = 1;

                if (runValue[run] < 0)
                {
                    _carried[length] = (byte)' ';
                }
                else
                {
                    // The room carried has ten digits for each run of digits, and no value carried has more.
                    _ = runValue[run].TryFormat(_carried[length..], out written, default, CultureInfo.InvariantCulture);
                }

                _carriedAt.Slice(length, written).Fill(runAt[run]);
                length += written;
            }

            _carriedLength = length;
        }

        /// <summary>
        /// Finds the first object header whose <c>obj</c> ends before <paramref name="before"/> in the read, and gives where
        /// it starts in the file; -1 when there is none.
        /// </summary>
        /// <remarks>
        /// A header is <c>N G obj</c> at a token boundary, as the file's bytes hold it: white space, a delimiter or the start
        /// of the data before the number; a run of digits whose value the parser takes for an object number, 1 to
        /// 2,147,483,647, and one whose value it takes for a generation, 0 to 65,535, however many zeros lead them; white
        /// space before each of the generation and <c>obj</c>, as much as the file holds; and white space, a delimiter or the
        /// end of the search after <c>obj</c>. <c>endobj</c> is no header, nor <c>10 0 objx</c>, nor digits a regular
        /// character precedes, nor tokens a comment separates. The scan that rebuilds an index refuses <c>endobj</c> and
        /// tokens a comment separates too, but takes <c>10 0 objx</c> and digits a regular character precedes (#171).
        /// A header lies whole in what the search reads, from the start of the data to the next object the index places: the
        /// stream's own header, and whatever precedes the data, is never one, and one that the next object's offset cuts is
        /// not seen, that offset stopping the search first. The rule reads nothing but the bytes, so where it stops cannot
        /// depend on what was read before; and what a read's start cuts of a header is carried into it, and <c>obj</c> with
        /// the byte after it is whole in the read that decides it, so where the reads start cannot change it either: a
        /// candidate whose <c>obj</c> ends the read, where the search goes on, is left to the next read, which repeats it.
        /// <para>
        /// The rule is no guard (ADR 34): only a stream whose <c>endstream</c> does not follow its declared length is
        /// searched, which no valid file holds (ISO 32000-1, 7.3.8.1). A valid stream's data may hold text that reads as a
        /// header — an embedded PDF written uncompressed —: its length, confirmed, is never searched. An invalid stream whose
        /// data holds such text before its <c>endstream</c> stops there, keeps its declared length and is reported, as one
        /// whose next object came first; text that only looks like a header is read past, and an <c>endstream</c> after it
        /// ends the data.
        /// </para>
        /// </remarks>
        /// <param name="before">Where the <c>endstream</c> found in the read starts, or its length.</param>
        /// <param name="endsRange">Whether the read ends where the search does, where <c>obj</c> may end.</param>
        public readonly long FindHeader(int before, bool endsRange)
        {
            var from = 0;

            while (true)
            {
                var index = _bytes[from..before].IndexOf(ObjKeyword);

                if (index < 0)
                {
                    return -1;
                }

                var keyword = from + index;
                var after = keyword + ObjKeyword.Length;

                // What follows obj past the end of the read is the next read's to see, which repeats it: the end of the read
                // ends the token only where the search ends.
                if ((after < _bytes.Length ? !PdfCharacters.IsRegular(_bytes[after]) : endsRange) &&
                    TryFindStart(keyword, out var start))
                {
                    return PositionOf(start);
                }

                from = after;
            }
        }

        /// <summary>
        /// Reads back from the <c>obj</c> at <paramref name="keyword"/> for the rest of an object header, as
        /// <see cref="FindHeader"/> takes one, and gives where its number starts.
        /// </summary>
        private readonly bool TryFindStart(int keyword, out int start)
        {
            var generationEnd = RunStart(keyword, digits: false);
            var generationStart = RunStart(generationEnd, digits: true);
            var numberEnd = RunStart(generationStart, digits: false);
            start = RunStart(numberEnd, digits: true);

            // Each run holds a byte at least, what precedes the number ends a token, and the values are an object number's
            // and a generation's.
            return generationEnd < keyword && generationStart < generationEnd && numberEnd < generationStart &&
                start < numberEnd && !PdfCharacters.IsRegular(this[start - 1]) &&
                Value(start, numberEnd) is >= 1 and <= PdfObjectId.MaxNumber &&
                Value(generationStart, generationEnd) <= PdfObjectId.MaxGeneration;
        }

        /// <summary>
        /// Gives where the run of digits, or of white space, that ends at <paramref name="end"/> starts, the first byte
        /// carried left out.
        /// </summary>
        private readonly int RunStart(int end, bool digits)
        {
            var start = end;

            while (start > FirstCarried + 1 &&
                (digits ? PdfCharacters.IsDigit(this[start - 1]) : PdfCharacters.IsWhitespace(this[start - 1])))
            {
                start--;
            }

            return start;
        }

        /// <summary>
        /// Gives the value of the digits from <paramref name="start"/> to <paramref name="end"/>, or <see cref="Beyond"/> when
        /// it is larger.
        /// </summary>
        private readonly long Value(int start, int end)
        {
            var value = 0L;

            for (var index = start; index < end; index++)
            {
                value = Math.Min((value * 10) + (this[index] - '0'), Beyond);
            }

            return value;
        }
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
        bool chainRead;
        _readingChain = true;

        try
        {
            chainRead = startXref >= 0 && TryReadXRefChain(startXref);
        }
        finally
        {
            _readingChain = false;
            _readWhileChainIsRead = null;
        }

        if (!chainRead)
        {
            Repair();
            return;
        }

        _structure.ChainRead = true;
        _structure.TrailerRead = Trailer.Count > 0;
        _structure.SizeAsWritten = Trailer.GetRaw(PdfName.Size);
        _structure.RootAsWritten = Trailer.GetRaw(PdfName.Root);
        CheckDeferredLengths();

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
    /// Reads, once the chain is read, the <c>/Length</c> of each cross-reference stream the chain read without it, and
    /// reports one that is no length, one the data does not confirm, and one that gives data past the <c>endstream</c> the
    /// chain took the data up to (#182).
    /// </summary>
    /// <remarks>
    /// The <c>/Length</c> is loaded as any object is: what that load finds or changes comes after the chain's index is
    /// copied (<see cref="PreserveChainIndex"/>). A length the <c>endstream</c> the chain stopped at confirms — the white
    /// space before it counted, or not all of it — is the stream's own, as the parser takes it for any stream, and says
    /// nothing. One confirmed by an <c>endstream</c> past it gives data the chain did not read, rows included: they are not
    /// read again, and the report says so. The stream is not parsed again, and each is reported once.
    /// </remarks>
    private void CheckDeferredLengths()
    {
        if (_deferredLengths is null)
        {
            return;
        }

        foreach (var (dataStart, deferred) in _deferredLengths)
        {
            var reentries = _reentries;
            var value = GetObject(deferred.Length);
            var (number, generation) = (deferred.Length.Number, deferred.Length.Generation);
            string? message = null;

            if (value.AsInteger() is not { } length || length is < 0 or > int.MaxValue)
            {
                var presence = value is PdfNull && reentries != _reentries ? ObjectPresence.Unproduced : GetPresence(number);
                var held = presence switch
                {
                    ObjectPresence.Missing => "which the file lacks",
                    ObjectPresence.Unproduced => "which could not be read",
                    _ => "which holds " + DescribeValue(value) + ", not a length",
                };
                message = string.Create(
                    CultureInfo.InvariantCulture,
                    $"The /Length of cross-reference stream {deferred.Stream} names object {number} {generation}, {held}; the chain took its data up to its endstream, after {deferred.Taken} bytes.");
            }
            else if (length != deferred.Taken)
            {
                var stopped = EndStreamAfter(dataStart + deferred.Taken);

                if (!((IPdfStreamDataProvider)this).IsEndStreamAt(dataStart + length))
                {
                    message = string.Create(CultureInfo.InvariantCulture, $"The stream declared {length} bytes but ended after {deferred.Taken}.");
                }
                else if (dataStart + length > stopped)
                {
                    message = string.Create(
                        CultureInfo.InvariantCulture,
                        $"The /Length of cross-reference stream {deferred.Stream} gives {length} bytes, which an endstream confirms; the chain, which could not read it, took the data up to an endstream {deferred.Taken} bytes in, and read no row past it.");
                }
            }

            if (message is not null && _lengthFaultsReported.Add(dataStart))
            {
                _diagnostics.Warn(PdfDiagnosticCodes.StreamLengthInvalid, message, dataStart);
            }
        }
    }

    /// <summary>
    /// Gives where the <c>endstream</c> keyword starts that the end-of-line or white space at <paramref name="position"/>
    /// leads to, or <paramref name="position"/> itself when none follows within the gap the parser allows.
    /// </summary>
    private long EndStreamAfter(long position)
    {
        Span<byte> tail = stackalloc byte[PdfObjectParser.EndStreamLookahead];
        var read = position < 0 || position >= _source.Length
            ? 0
            : _source.Read(position, tail[..(int)Math.Min(tail.Length, _source.Length - position)]);
        var keyword = tail[..read].IndexOf("endstream"u8);
        return keyword < 0 ? position : position + keyword;
    }

    /// <summary>
    /// A cross-reference stream's <c>/Length</c> the chain could not read: the object it names, how many bytes of data the
    /// chain took, and the stream's own number.
    /// </summary>
    private readonly record struct DeferredLength(PdfObjectId Length, int Taken, int Stream);

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
            // Nothing is rebuilt for it: the offsets the file gives are counted from its first byte, as from a header
            // there. An empty source has no first byte to point at.
            _diagnostics.Repair(
                PdfDiagnosticCodes.HeaderMissing,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The file has no PDF header (%PDF-) in its first {HeaderSearchLength:N0} bytes; its offsets were counted from its first byte."),
                _source.Length > 0 ? 0 : -1);
            return;
        }

        _structure.HeaderPosition = index;

        if (index > 0)
        {
            // Bytes before the header shift every offset in the file by the same amount.
            _headerOffset = index;
            _diagnostics.Repair(
                PdfDiagnosticCodes.XRefOffsetAdjusted,
                string.Create(CultureInfo.InvariantCulture, $"The PDF header starts {index} bytes into the file; offsets were shifted accordingly."),
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
                // Placed at the section looped back to, a position in the file; one named outside the file, as an
                // /XRefStm and a /Prev naming the same offset past its end are, is placed at what named it.
                _indexIncomplete = true;
                _structure.LoopOffset = offset + _headerOffset;
                _structure.LoopNamedBy = naming;
                _structure.LoopNamedFrom = namedFrom;
                var inFile = IsInFile(offset + _headerOffset);
                _diagnostics.Warn(
                    PdfDiagnosticCodes.XRefChainCycle,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The cross-reference chain loops back on itself: the {naming} of the section at offset {namedFrom} names offset {offset + _headerOffset}, {(inFile ? "which the chain has already read" : "outside the file, which the chain has already named")}."),
                    inFile ? offset + _headerOffset : namedFrom);
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
                    ReportLostSection(section, naming, offset, namedFrom);
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
                    ReportLostSection(stream, "/XRefStm", hybrid, section.Offset);
                }
            }

            offset = previous;
            naming = "/Prev";
            namedFrom = section.Offset;
        }

        return _xref.Count > 0;
    }

    /// <summary>Determines whether <paramref name="position"/>, a position in the file, lies inside it.</summary>
    private bool IsInFile(long position) => position >= 0 && position < _source.Length;

    /// <summary>
    /// Reports a section of the chain that could not be read where it was named, nor found near it, and marks the
    /// index incomplete: as one that is there and cannot be read when the named offset holds a section, as a missing
    /// one otherwise.
    /// </summary>
    /// <remarks>
    /// A section one of the reader's guards cut is already reported, under the guard's own code, which names the limit
    /// that lifts it: what the reader met past the cut is not the file's fault (ADR 34).
    /// </remarks>
    private void ReportLostSection(XRefSectionRecord section, string naming, long offset, long namedFrom)
    {
        if (section.State != XRefSectionState.Malformed)
        {
            ReportMissingSection(naming, offset, namedFrom);
            return;
        }

        _indexIncomplete = true;

        if (section.CutByLimit)
        {
            return;
        }

        // Every fault but a table's missing trailer keyword is described where it is met: rows that run into a
        // dictionary, or into the end of the file, are the one left.
        var fault = section.Fault ?? "its rows are not followed by the trailer keyword";
        _diagnostics.Warn(
            PdfDiagnosticCodes.XRefSectionUnreadable,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The cross-reference {(section.Kind == XRefSectionKind.Stream ? "stream" : "table")} {naming} names at offset {section.Offset} is there but cannot be read: {fault}. The rows read before the fault, if any, were kept; the objects only the rest indexes are looked for by rebuilding the index."),
            section.Offset);
    }

    /// <summary>
    /// Reports a section of the chain that could not be found, and marks the index incomplete: at the offset it was
    /// named at, or, when that lies outside the file, at <paramref name="namedFrom"/>, the section whose trailer named
    /// it.
    /// </summary>
    private void ReportMissingSection(string naming, long offset, long namedFrom)
    {
        _indexIncomplete = true;
        var absolute = offset + _headerOffset;
        _diagnostics.Warn(
            PdfDiagnosticCodes.XRefSectionMissing,
            IsInFile(absolute)
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"The cross-reference section {naming} names at offset {absolute} is not there, nor near it; the objects only it indexes are looked for by rebuilding the index.")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"The cross-reference section {naming} names at offset {absolute} lies outside the file, which is {_source.Length:N0} bytes long; the objects only it indexes are looked for by rebuilding the index."),
            IsInFile(absolute) ? absolute : namedFrom);
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

            if (TryReadXRefSection(candidate - _headerOffset, attempt, out previous, out hybrid, candidate: true) == XRefSectionState.Read)
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

            if (TryReadHeaderBackwards(span, position, out _, out _, out var headerStart))
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
    /// <param name="offset">Where the section is named, counted from the header.</param>
    /// <param name="section">Receives what became of the section.</param>
    /// <param name="previous">The offset its <c>/Prev</c> gives, or -1.</param>
    /// <param name="hybrid">The offset its <c>/XRefStm</c> gives, or -1.</param>
    /// <param name="candidate">
    /// Whether the offset is a place the section may have been moved to rather than one the chain names: an object there
    /// that is no cross-reference stream leaves nothing of its reading.
    /// </param>
    private XRefSectionState TryReadXRefSection(
        long offset, XRefSectionRecord section, out long previous, out long hybrid, bool candidate = false)
    {
        previous = -1;
        hybrid = -1;

        var absolute = offset + _headerOffset;
        if (absolute < 0 || absolute >= _source.Length)
        {
            // Reported once, by what the chain makes of it: a missing section, or a rebuild for the first.
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

        section.State = TryReadXRefStream(absolute, section, out previous, candidate);
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

            // A window shorter than asked holds the end of the file: what it holds is all there is, as it is for a full one
            // that ends where the file does. Any other full one may have cut the table, and is grown until the guard allows
            // no more.
            var windowFull = window.Length == windowSize && absolute + window.Length < _source.Length;
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
                    first is < 0 or > PdfObjectId.MaxNumber)
                {
                    section.Fault ??= string.Create(
                        CultureInfo.InvariantCulture,
                        $"the subsection header at offset {absolute + token.Start} does not give a first object number and a count of rows");
                    return false;
                }

                if (first + countToken.Integer - 1 > PdfObjectId.MaxNumber)
                {
                    section.Fault ??= string.Create(
                        CultureInfo.InvariantCulture,
                        $"the subsection header at offset {absolute + token.Start} numbers its rows past object {PdfObjectId.MaxNumber}");
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
    /// every section of a chain. Only a dictionary written as one is a trailer: a reference is not followed to one. The
    /// values it refers to are read later, but for a <c>/Prev</c> or an <c>/XRefStm</c> written as one, which is read
    /// without loading anything while the index is still being read (<see cref="SectionOffset"/>).
    /// </remarks>
    private PdfDictionary? ReadTrailer(
        ReadOnlyMemory<byte> window, long absolute, int position, bool windowFull, XRefSectionRecord section)
    {
        var mark = _pending.GetMark();

        try
        {
            var parser = new PdfObjectParser(window, absolute, this, _pending, this, endsData: absolute + window.Length >= _source.Length);
            parser.Position = position;
            var parsed = parser.ParseObject();

            if (!parser.IsTruncated || !windowFull)
            {
                // Cut short where the window holds the end of the file, the dictionary runs to it unclosed.
                var malformed = parser.IsTruncated || SyntaxFaultSince(_pending, mark);
                KeepPending(mark);
                return JudgeTrailer(parsed, malformed, section);
            }
        }
        finally
        {
            _pending.RollBack(mark);
        }

        // The trailer starts inside the window, so inside the file as long as the source's length holds;
        // where nothing can be parsed the value is null, and anything that is not a dictionary is no trailer.
        var guards = _guardsReached;
        _ = TryParseNumberedAt(absolute + position, DirectObject, PdfLimit.Trailer, out var value, out _, out var syntaxFault);
        section.CutByLimit |= _guardsReached != guards;
        return JudgeTrailer(value, syntaxFault, section);
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

    /// <summary>
    /// Keeps what an attempt recorded since <paramref name="mark"/>: moves it into the document's diagnostics, but for a
    /// fault of the syntax they keep already, met again as its object is parsed again.
    /// </summary>
    /// <param name="mark">Where the attempt's entries start in the pending diagnostics.</param>
    /// <param name="syntaxOnly">Whether only the faults of the syntax are kept, the rest dropped with a guard's cut.</param>
    private void KeepPending(PdfDiagnosticsMark mark, bool syntaxOnly = false) =>
        _pending.MoveTo(_diagnostics, mark, (this, syntaxOnly), static (entry, state) => state.Item1.IsKept(entry, state.syntaxOnly));

    /// <summary>Determines whether <paramref name="entry"/> goes into the document's diagnostics.</summary>
    private bool IsKept(PdfDiagnostic entry, bool syntaxOnly)
    {
        if (!entry.Code.StartsWith(SyntaxCodePrefix, StringComparison.Ordinal))
        {
            return !syntaxOnly;
        }

        // Once the diagnostics are full, what is added is counted and dropped, and need not be remembered.
        return _diagnostics.IsFull || _syntaxReported.Add(entry);
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
            if (diagnostics[i].Code.StartsWith(SyntaxCodePrefix, StringComparison.Ordinal))
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
        PdfTokenKind.Keyword => "the keyword " + FileQuote.Keyword(token.Text),
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

            if (kindToken.IsKeyword("f"u8))
            {
                // A free row's generation serves no object: some producers give the head of the free list 65,536.
                AddFromChain(number, XRefEntry.Free);
            }
            else if (generationToken.Integer is >= 0 and <= PdfObjectId.MaxGeneration)
            {
                AddFromChain(number, XRefEntry.Regular(offsetToken.Integer, (int)generationToken.Integer));
            }
            else
            {
                // The rows after it are still in step: only this one is refused, and its object is the index's to find.
                Refuse(number);
                section.RefusedRow ??= string.Create(
                    CultureInfo.InvariantCulture,
                    $"the row for object {number}, at offset {absolute + offsetToken.Start}, gives it generation {generationToken.Integer}, outside 0 to {PdfObjectId.MaxGeneration}");
            }
        }

        return true;
    }

    /// <summary>
    /// Reads the cross-reference stream at <paramref name="absolute"/>, if one is there, and records what became of
    /// it in <paramref name="section"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An object that declares itself a cross-reference stream — <c>/Type /XRef</c>, or a <c>/W</c> — and cannot be
    /// read is a malformed section; any other object there, or none, is no section at all. At a place the section may have
    /// been moved to (<paramref name="candidate"/>), nothing such an object's reading met is reported or recorded — its
    /// search for an <c>endstream</c> aside, which is kept as every search is —: it was read as a section, through a
    /// section's window, and is read as itself when something asks for it.
    /// </para>
    /// <para>
    /// ISO 32000-1 (7.5.8.2) makes the values of this dictionary direct, the elements of <c>/W</c> and <c>/Index</c>
    /// with them, and the <c>/Filter</c> and <c>/DecodeParms</c> of an encoded stream; <c>/Length</c> may be indirect.
    /// A value written as a reference is read where a section already read places it (<see cref="ReadWithoutLoading"/>),
    /// as other readers read it, and the validator reports it. One no section read so far places makes the section
    /// malformed when the rows cannot be read without it (<see cref="UnreadValue"/>); a <c>/Length</c> no section read
    /// so far places is read once the chain is read (<see cref="CheckDeferredLengths"/>).
    /// </para>
    /// </remarks>
    private XRefSectionState TryReadXRefStream(long absolute, XRefSectionRecord section, out long previous, bool candidate)
    {
        previous = -1;

        // The first window, of 64 KB, holds the data of all but the largest cross-reference streams, so the
        // parser checks their /Length against the data rather than only at the end the /Length gives. The
        // dictionary is the trailer, and grows its window no further than a trailer may.
        var guards = _guardsReached;

        // A place the section may have been moved to is a guess: what is there and is no section leaves nothing of its
        // reading, not even a guard its dictionary reached. Where the chain names a section, the guard is reported.
        if (!TryParseNumberedAt(
                absolute,
                AnyObject,
                PdfLimit.Trailer,
                out var value,
                out var number,
                out var syntaxFault,
                XRefWindow,
                candidate ? DeclaresCrossReferenceStream : null) ||
            !DeclaresCrossReferenceStream(value))
        {
            section.Fault = number == 0
                ? "holds neither the xref keyword nor an object"
                : string.Create(CultureInfo.InvariantCulture, $"holds object {number}, which is not a cross-reference stream");
            return XRefSectionState.NotFound;
        }

        var dictionary = value is PdfStream parsed ? parsed.Dictionary : (PdfDictionary)value;
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

        if (syntaxFault)
        {
            section.TrailerFault = XRefTrailerFault.Malformed;
        }

        // A /Length no section read so far places: the data was taken up to the first endstream it holds, and the /Length is
        // read, and checked, once the chain is, if the section is read. Data a guard cut is the guard's, which says so
        // (ADR 34), and is not checked against a length.
        var deferred = dictionary.GetRaw(PdfName.Length) is PdfReference length && ReadWithoutLoading(length.Id) is null &&
            !section.CutByLimit && !xrefStream.Data.CutByGuard
            ? new DeferredLength(length.Id, xrefStream.Data.Length, number)
            : (DeferredLength?)null;

        if (UnreadValue(dictionary) is { } unread)
        {
            section.Fault = unread;
            return XRefSectionState.Malformed;
        }

        var widths = dictionary.GetArray(PdfName.W);

        if (widths is null || widths.Count < 3)
        {
            section.Fault = "its /W does not give the widths of three fields";
            return XRefSectionState.Malformed;
        }

        // A width counts bytes, and ISO 32000-1 bounds none (Table 17): a field wider than the value the reader keeps holds
        // in its leading bytes what no entry can (ReadField). Each is narrowed once the data is decoded, against its length.
        Span<long> declaredWidths = stackalloc long[3];
        var anyWidth = false;

        for (var i = 0; i < 3; i++)
        {
            var width = widths.Resolved(i).AsInteger();

            if (width is null)
            {
                section.Fault = "its /W gives a field a width that is not an integer";
                return XRefSectionState.Malformed;
            }

            if (width < 0)
            {
                section.Fault = "its /W gives a field a negative width";
                return XRefSectionState.Malformed;
            }

            declaredWidths[i] = width.Value;
            anyWidth |= width > 0;
        }

        if (!anyWidth)
        {
            section.Fault = "its /W gives rows of no bytes";
            return XRefSectionState.Malformed;
        }

        // An /Index that is null, or names an object that holds null, is one the dictionary does not have (7.3.7, 7.3.10):
        // one that names an object no section read so far places is UnreadValue's fault.
        var written = dictionary.GetRaw(PdfName.Index);
        var index = written?.Resolve() is { } given and not PdfNull ? given : null;
        var size = dictionary.GetInteger(PdfName.Size);

        // The numbering is checked before any row is read: a section that numbers its rows wrongly gives none to believe.
        // Each value is read once, so that what is read is what was checked.
        if (NumberingFault(written, index, size, out var subsections) is { } numbering)
        {
            section.Fault = numbering;
            return XRefSectionState.Malformed;
        }

        var decoding = _diagnostics.GetMark();
        var data = xrefStream.Decode(_diagnostics).Span;
        var decodedWhole = !LimitSince(_diagnostics, decoding) && !xrefStream.Data.CutByGuard;
        section.CutByLimit |= !decodedWhole;

        // A row longer than the decoded data is one the data does not hold. Not a guard (ADR 34): the data's length is
        // bounded by MaxDecodedStreamLength, a guard, and a valid section's rows are in its data. Each width is narrowed
        // once it is known to be within the data, whose length an int holds, and their sum is then within a long.
        Span<int> fieldWidths = stackalloc int[3];
        long rowLength = 0;

        for (var i = 0; i < 3; i++)
        {
            fieldWidths[i] = (int)Math.Min(declaredWidths[i], data.Length + 1L);
            rowLength += fieldWidths[i];
        }

        var row = rowLength <= data.Length ? (int)rowLength : 0;
        var held = row == 0 ? 0 : data.Length / row;
        var position = 0;
        long declared = 0;

        if (index is null)
        {
            // Rows past the data are not read, so a /Size past an int numbers no row past one: the data holds fewer.
            declared = size.GetValueOrDefault();

            if (row > 0)
            {
                ReadXRefStreamRows(data, ref position, fieldWidths, row, 0, (int)Math.Min(declared, int.MaxValue), section);
            }
        }
        else
        {
            for (var i = 0; i < subsections.Length && row > 0; i += 2)
            {
                ReadXRefStreamRows(data, ref position, fieldWidths, row, subsections[i], subsections[i + 1], section);
            }

            for (var i = 1; i < subsections.Length; i += 2)
            {
                declared += subsections[i];
            }
        }

        // Compared by division: a /Size can be as large as a long, and its product with the row length overflow one.
        if (declared > held)
        {
            // Rows the data does not hold index nothing: the objects they stood for are the index's to find.
            _indexIncomplete = true;

            if (decodedWhole)
            {
                // Rows the data does not hold, when the reader decoded all of it, are the file's to answer for. Every
                // fault met before returned, and a refused row is kept apart: this is the section's first.
                section.Incomplete = true;
                section.Fault = string.Create(
                    CultureInfo.InvariantCulture,
                    $"it holds {held:N0} {(held == 1 ? "row" : "rows")} where its /Index and /Size declare {declared:N0}");
            }
        }

        if (deferred is { } check)
        {
            (_deferredLengths ??= [])[xrefStream.Data.Position] = check;
        }

        _xref.MergeTrailer(dictionary);
        previous = SectionOffset(dictionary, PdfName.Prev, absolute);
        return XRefSectionState.Read;
    }

    /// <summary>
    /// Determines whether what an offset of the chain holds declares itself a cross-reference stream: <c>/Type /XRef</c>,
    /// or a <c>/W</c>.
    /// </summary>
    private static bool DeclaresCrossReferenceStream(PdfObject value) =>
        (value is PdfStream stream ? stream.Dictionary : value as PdfDictionary) is { } dictionary &&
        (dictionary.IsOfType(PdfName.XRef) || dictionary.ContainsKey(PdfName.W));

    /// <summary>
    /// Says which value a cross-reference stream's dictionary refers to, among those its rows are read with, that no section
    /// read so far places where it can be read; null when every one can be.
    /// </summary>
    /// <remarks>
    /// What is followed is what reading the rows reads, as it reads it: <c>/W</c> and the widths of its three fields;
    /// <c>/Index</c> and its elements, or <c>/Size</c> when there is no <c>/Index</c>; <c>/Filter</c> and its elements; and,
    /// when the data is encoded, <c>/DecodeParms</c>, its elements, and in the parameters of each Flate or LZW step,
    /// <c>/Predictor</c>, LZW's <c>/EarlyChange</c>, and <c>/Colors</c>, <c>/BitsPerComponent</c> and <c>/Columns</c> when
    /// the predictor is past 1 — each through as many references as <see cref="PdfReference.Resolve"/> follows. A value the
    /// rows are read without — <c>/Type</c>, <c>/Size</c> beside an <c>/Index</c>, an entry no filter reads — leaves the
    /// section readable; the validator reports how it is written.
    /// </remarks>
    private string? UnreadValue(PdfDictionary dictionary)
    {
        if (Unread(dictionary, PdfName.W, out var widths) is { } fault)
        {
            return fault;
        }

        for (var i = 0; widths is PdfArray w && i < Math.Min(3, w.Count); i++)
        {
            if (Follow(PdfName.W, w[i], entry: false, out _) is { } elementFault)
            {
                return elementFault;
            }
        }

        if (Unread(dictionary, PdfName.Index, out var index) is { } indexFault)
        {
            return indexFault;
        }

        if (index is PdfArray ranges)
        {
            foreach (var element in ranges)
            {
                if (Follow(PdfName.Index, element, entry: false, out _) is { } elementFault)
                {
                    return elementFault;
                }
            }
        }
        else if ((index is null or PdfNull) && Unread(dictionary, PdfName.Size, out _) is { } sizeFault)
        {
            return sizeFault;
        }

        if (Unread(dictionary, PdfName.Filter, out var filters) is { } filterFault)
        {
            return filterFault;
        }

        // Data that is not encoded is read without its parameters.
        if (filters is null or PdfNull)
        {
            return null;
        }

        if (Unread(dictionary, PdfName.DecodeParms, out var parameters) is { } parametersFault)
        {
            return parametersFault;
        }

        if (filters is PdfName single)
        {
            return UnreadParameter(single, parameters as PdfDictionary);
        }

        for (var i = 0; filters is PdfArray chain && i < chain.Count; i++)
        {
            if (Follow(PdfName.Filter, chain[i], entry: false, out var step) is { } stepFilterFault)
            {
                return stepFilterFault;
            }

            var stepParameters = parameters;

            if (parameters is PdfArray parameterArray)
            {
                stepParameters = null;

                if (i < parameterArray.Count && Follow(PdfName.DecodeParms, parameterArray[i], entry: false, out stepParameters) is { } stepParametersFault)
                {
                    return stepParametersFault;
                }
            }

            if (step is PdfName name)
            {
                // Decoding stops at an image filter, and reads no parameter past it.
                if (Filters.PdfFilterPipeline.IsImageFilter(name))
                {
                    return null;
                }

                if (UnreadParameter(name, stepParameters as PdfDictionary) is { } stepFault)
                {
                    return stepFault;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Says which value the parameters of a Flate or LZW step refer to, among those decoding reads, that no section read so
    /// far places where it can be read; null when every one can be, or the step reads none.
    /// </summary>
    private string? UnreadParameter(PdfName filter, PdfDictionary? parameters)
    {
        if (parameters is null || (filter != PdfName.FlateDecode && filter != PdfName.LZWDecode))
        {
            return null;
        }

        if (filter == PdfName.LZWDecode && Follow(PdfName.DecodeParms, parameters.GetRaw(PdfName.EarlyChange), entry: false, out _) is { } earlyChange)
        {
            return earlyChange;
        }

        if (Follow(PdfName.DecodeParms, parameters.GetRaw(PdfName.Predictor), entry: false, out var predictor) is { } predictorFault)
        {
            return predictorFault;
        }

        if (predictor?.AsInteger() is not > 1)
        {
            return null;
        }

        foreach (var key in (ReadOnlySpan<PdfName>)[PdfName.Colors, PdfName.BitsPerComponent, PdfName.Columns])
        {
            if (Follow(PdfName.DecodeParms, parameters.GetRaw(key), entry: false, out _) is { } unread)
            {
                return unread;
            }
        }

        return null;
    }

    /// <summary>
    /// Follows the value <paramref name="key"/> gives in <paramref name="dictionary"/>, and says so when a reference it is,
    /// or leads to, cannot be read.
    /// </summary>
    private string? Unread(PdfDictionary dictionary, PdfName key, out PdfObject? value) =>
        Follow(key, dictionary.GetRaw(key), entry: true, out value);

    /// <summary>
    /// Follows <paramref name="written"/> through as many references as <see cref="PdfReference.Resolve"/> does, each read
    /// with <see cref="ReadWithoutLoading"/>, and says, as the fault of <paramref name="key"/>, which could not be read, or
    /// that the value is still a reference past them; null when the value is reached.
    /// </summary>
    /// <param name="key">The entry of the dictionary that holds the value, which the fault names.</param>
    /// <param name="written">The value as the file wrote it.</param>
    /// <param name="entry">Whether <paramref name="written"/> is the entry's own value, rather than an element of it.</param>
    /// <param name="value">The value it leads to, or null when it leads to none.</param>
    private string? Follow(PdfName key, PdfObject? written, bool entry, out PdfObject? value)
    {
        value = written;

        for (var link = 0; value is PdfReference reference; link++)
        {
            if (link == MaxReferenceLinks)
            {
                value = null;
                return $"its {FileQuote.Name(key)} leads from reference to reference without reaching a value";
            }

            if (ReadWithoutLoading(reference.Id) is not { } read)
            {
                value = null;
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"its {FileQuote.Name(key)} {(entry && link == 0 ? "is" : "holds")} the reference {reference.Id.Number} {reference.Id.Generation} R, which no section read before it places where it can be read");
            }

            value = read;
        }

        return null;
    }

    /// <summary>
    /// Says what is wrong with how a cross-reference stream numbers its rows — through its <c>/Index</c>, or through its
    /// <c>/Size</c> when it has no <c>/Index</c> —, or gives null when nothing is, with the first number and the count of
    /// each subsection its <c>/Index</c> gives, which then all fit an <see cref="int"/>.
    /// </summary>
    /// <remarks>
    /// An integral real counts as an integer, as everywhere the reader reads one from a value rather than from the tokens
    /// of a table or a header: it is read as written, and the validator reports it (#215). A <c>/Size</c> larger than the data's
    /// rows is no fault of numbering: the rows the data does not hold are reported as missing once it is decoded.
    /// </remarks>
    /// <param name="written">The <c>/Index</c> as the file wrote it, which a fault describes.</param>
    /// <param name="index">The <c>/Index</c>, resolved, or null when there is none.</param>
    /// <param name="size">The <c>/Size</c>, when it is an integer.</param>
    /// <param name="subsections">Each subsection's first number then its count, empty without an <c>/Index</c> or with a fault.</param>
    private static string? NumberingFault(PdfObject? written, PdfObject? index, long? size, out int[] subsections)
    {
        subsections = [];

        // The /Index is resolved from what was written: either is null only when the dictionary has none to read.
        if (written is null || index is null)
        {
            return size is null or < 0 ? "it has no /Index, and its /Size gives no count of objects" : null;
        }

        if (index is not PdfArray ranges)
        {
            return $"its /Index is {DescribeValue(written)}, not an array";
        }

        if (ranges.Count == 0 || ranges.Count % 2 != 0)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"its /Index holds {ranges.Count} {(ranges.Count == 1 ? "value" : "values")}, not pairs of a first object number and a count of rows");
        }

        var checkedSubsections = new int[ranges.Count];

        for (var i = 0; i < ranges.Count; i += 2)
        {
            var start = ranges.Resolved(i).AsInteger();
            var count = ranges.Resolved(i + 1).AsInteger();

            if (start is null || count is null)
            {
                return $"its /Index holds {DescribeValue(ranges[start is null ? i : i + 1])} where an integer belongs";
            }

            if (count is < 0 or > MaxSubsectionEntries)
            {
                return "its /Index gives a subsection a count of rows out of range";
            }

            if (start is < 0 or > PdfObjectId.MaxNumber || start + count - 1 > PdfObjectId.MaxNumber)
            {
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"its /Index gives a subsection of {count:N0} {(count == 1 ? "row" : "rows")} from object {start}, outside object numbers 0 to {PdfObjectId.MaxNumber}");
            }

            checkedSubsections[i] = (int)start.Value;
            checkedSubsections[i + 1] = (int)count.Value;
        }

        subsections = checkedSubsections;
        return null;
    }

    /// <summary>
    /// Reads the offset a section's <c>/Prev</c> or <c>/XRefStm</c> gives, or -1 when it gives none.
    /// </summary>
    /// <remarks>
    /// ISO 32000-1 makes <c>/Prev</c> a direct integer (Table 15), and <c>/XRefStm</c> an integer (Table 19). One written
    /// as a reference is read where a section already read places it, as every value the chain reads is
    /// (<see cref="ReadWithoutLoading"/>), and a real with no fractional part as its integer, as the reader reads integers
    /// elsewhere; the validator reports the form the specification does not allow. Anything else — tiff2pdf writes
    /// <c>/Prev 576066 0 R</c>, naming an object the file lacks — names no section, and the sections it should have named
    /// are reported missing, to be looked for by rebuilding the index. A negative integer is no offset either.
    /// </remarks>
    private long SectionOffset(PdfDictionary trailer, PdfName key, long section)
    {
        var value = trailer.GetRaw(key);

        if (value is null)
        {
            return -1;
        }

        var read = value is PdfReference reference ? ReadWithoutLoading(reference.Id) : value;

        if (read?.AsInteger() is { } offset and >= 0)
        {
            return offset;
        }

        _indexIncomplete = true;
        _structure.Add(new XRefSectionRecord(FileQuote.Name(key), -1, section)
        {
            State = XRefSectionState.NotFound,
            Fault = read is null
                ? $"is {DescribeValue(value)}, which no section read before it places where it can be read"
                : $"is {DescribeValue(value)}, not an offset",
        });
        _diagnostics.Warn(
            PdfDiagnosticCodes.XRefSectionMissing,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The {FileQuote.Name(key)} of the cross-reference section at offset {section} is not an offset; the sections it names are looked for by rebuilding the index."),
            section);
        return -1;
    }

    /// <summary>Says what a value written where an offset or an integer belongs is, a real as PDF writes one.</summary>
    private static string DescribeValue(PdfObject value) => value switch
    {
        PdfReference reference => string.Create(
            CultureInfo.InvariantCulture, $"the reference {reference.Id.Number} {reference.Id.Generation} R"),
        PdfInteger integer => string.Create(CultureInfo.InvariantCulture, $"the integer {integer.Value}"),
        PdfReal real => "the real number " + Expanded(real.Value),
        PdfName name => "the name " + FileQuote.Name(name),
        PdfNull => "null",
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

        for (var i = 0; i < count && rowLength <= data.Length - position; i++, position += rowLength)
        {
            var row = data.Slice(position, rowLength);
            var cursor = 0;

            // A zero-width type field means the type is 1: an ordinary object at an offset.
            var type = widths[0] == 0 ? 1 : ReadField(row, ref cursor, widths[0]);
            var second = ReadField(row, ref cursor, widths[1]);
            var third = ReadField(row, ref cursor, widths[2]);
            var number = first + i;
            section.HighestNumber = Math.Max(section.HighestNumber, number);

            // Each field is read whole, as the unsigned value its bytes give, and narrowed only once it is known to fit.
            switch (type)
            {
                case 0:
                    AddFromChain(number, XRefEntry.Free);
                    break;

                case 1 when second <= long.MaxValue && third <= PdfObjectId.MaxGeneration:
                    AddFromChain(number, XRefEntry.Regular((long)second, (int)third));
                    break;

                case 2 when second is >= 1 and <= PdfObjectId.MaxNumber && third <= int.MaxValue:
                    AddFromChain(number, XRefEntry.Compressed((int)second, (int)third));
                    break;

                case 1 or 2:
                    // The rows after it are still in step: only this one is refused, and its object is the index's to find.
                    Refuse(number);
                    section.RefusedRow ??= RowFault(number, type, second, third, row, widths);
                    break;

                default:
                    // Types beyond 2 are reserved; the specification says to ignore the entry. A type field wider than 8
                    // bytes whose leading bytes are not all zero gives one.
                    break;
            }
        }
    }

    /// <summary>
    /// Writes a real read from a file as the shortest decimal that reads back to it, with no exponent, as PDF writes
    /// reals: 1E-07 is written 0.0000001.
    /// </summary>
    private static string Expanded(double value)
    {
        var shortest = value.ToString("R", CultureInfo.InvariantCulture);
        var e = shortest.IndexOf('E', StringComparison.Ordinal);

        if (e < 0)
        {
            return shortest;
        }

        var exponent = int.Parse(shortest.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var sign = shortest[0] == '-' ? "-" : string.Empty;
        var mantissa = shortest[sign.Length..e];
        var point = mantissa.IndexOf('.', StringComparison.Ordinal);
        var digits = point < 0 ? mantissa : mantissa.Remove(point, 1);
        var whole = (point < 0 ? mantissa.Length : point) + exponent;

        // "R" writes an exponent only below 10^-4, where the whole part is none, and from 10^17, where it is longer than
        // the 17 digits a double needs at most: the last arm stands for a runtime that writes one sooner.
        return sign + (whole <= 0
            ? "0." + new string('0', -whole) + digits
            : whole >= digits.Length ? digits + new string('0', whole - digits.Length) : digits[..whole] + "." + digits[whole..]);
    }

    /// <summary>
    /// Indexes what a row of the chain says of object <paramref name="number"/>, unless a newer section said it already or
    /// refused its row.
    /// </summary>
    private void AddFromChain(int number, XRefEntry entry)
    {
        if (_refusedNumbers is null || !_refusedNumbers.Contains(number))
        {
            _xref.TryAdd(number, entry);
        }
    }

    /// <summary>
    /// Refuses a row of the chain for object <paramref name="number"/>: the object is left for a rebuild to find, and no
    /// older section's row stands for it.
    /// </summary>
    private void Refuse(int number)
    {
        _indexIncomplete = true;
        (_refusedNumbers ??= []).Add(number);
    }

    /// <summary>Says why a cross-reference stream's row of type 1 or 2 was refused.</summary>
    /// <remarks>
    /// A field wider than 8 bytes whose leading bytes are not all zero holds more than 64 bits, read as the largest value
    /// (<see cref="ReadField"/>), and is said to; one whose leading bytes are zero holds the value its last 8 give.
    /// </remarks>
    private static string RowFault(int number, ulong type, ulong second, ulong third, ReadOnlySpan<byte> row, ReadOnlySpan<int> widths)
    {
        var wideSecond = IsPast64Bits(row, widths[0], widths[1]);
        var wideThird = IsPast64Bits(row, widths[0] + widths[1], widths[2]);

        return type == 1
            ? second > long.MaxValue
                ? wideSecond
                    ? Invariant($"the row for object {number} gives it an offset of more than 64 bits, past any a file can have")
                    : Invariant($"the row for object {number} gives it offset {second}, past any a file can have")
                : wideThird
                    ? Invariant($"the row for object {number} gives it a generation of more than 64 bits, outside 0 to {PdfObjectId.MaxGeneration}")
                    : Invariant($"the row for object {number} gives it generation {third}, outside 0 to {PdfObjectId.MaxGeneration}")
            : second is 0 or > PdfObjectId.MaxNumber
                ? wideSecond
                    ? Invariant($"the row for object {number} places it in an object stream whose number takes more than 64 bits, which is no object number")
                    : Invariant($"the row for object {number} places it in object stream {second}, which is no object number")
                : wideThird
                    ? Invariant($"the row for object {number} places it at an index of more than 64 bits in object stream {second}, past any an object stream can hold")
                    : Invariant($"the row for object {number} places it at index {third} of object stream {second}, past any an object stream can hold");

        static bool IsPast64Bits(ReadOnlySpan<byte> row, int start, int width) =>
            width > sizeof(ulong) && row.Slice(start, width - sizeof(ulong)).ContainsAnyExcept((byte)0);

        static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads a field of a cross-reference stream's row, high byte first (ISO 32000-1, 7.5.8.3), as the unsigned value its
    /// bytes give.
    /// </summary>
    /// <remarks>
    /// A field may be wider than the 8 bytes the value is read into: leading bytes that are all zero add nothing, and any
    /// other makes a value past 64 bits, read as the largest, which no entry holds and no type is. Not a guard
    /// (ADR 34): every value an entry can hold fits in 64 bits, and every byte of the field is still read.
    /// </remarks>
    private static ulong ReadField(ReadOnlySpan<byte> row, ref int cursor, int width)
    {
        var start = cursor;
        cursor += width;

        if (width > sizeof(ulong))
        {
            if (row.Slice(start, width - sizeof(ulong)).ContainsAnyExcept((byte)0))
            {
                return ulong.MaxValue;
            }

            start += width - sizeof(ulong);
        }

        ulong value = 0;

        for (var i = start; i < cursor; i++)
        {
            value = (value << 8) | row[i];
        }

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
            // No position in the file names the entry, which the index does not keep where it was read.
            _diagnostics.Warn(
                PdfDiagnosticCodes.XRefEntryOutOfRange,
                string.Create(CultureInfo.InvariantCulture, $"The entry of object {id.Number} places it at offset {offset}, outside the file."));
        }
        else if (TryParseObjectAt(id.Number, offset, out var atRecordedOffset))
        {
            return atRecordedOffset;
        }

        // Offsets are commonly off by a few bytes in files from careless tools, so the neighborhood is
        // searched before the index is given up on entirely.
        if (TryFindObjectHeader(id.Number, offset, out var nearby, out var generation) &&
            TryParseObjectAt(id.Number, nearby, out var relocated))
        {
            _diagnostics.Repair(
                PdfDiagnosticCodes.XRefOffsetAdjusted,
                string.Create(CultureInfo.InvariantCulture, $"Object {id.Number} was found {nearby - offset} bytes from where the index said."),
                nearby);

            // The entry records the header found, as a rebuilt one does: not the generation of the reference that asked
            // first, which would make the index depend on the order objects were asked for (#118).
            PreserveChainIndex();
            _xref.Set(id.Number, XRefEntry.Regular(nearby - _headerOffset, generation));
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
        TryParseNumberedAt(offset, number, limit, out value, out _, out _, initialWindow);

    /// <summary>
    /// Parses what starts at an exact offset, as <see cref="TryParseAt"/> does, and gives the number of the object
    /// found there — 0 for a direct object.
    /// </summary>
    /// <param name="offset">Where the object starts in the file.</param>
    /// <param name="number">The object's number, <see cref="AnyObject"/> or <see cref="DirectObject"/>.</param>
    /// <param name="limit">The guard that bounds the window.</param>
    /// <param name="value">The object read.</param>
    /// <param name="foundNumber">The number of the object found, even when <paramref name="keep"/> refuses it.</param>
    /// <param name="syntaxFault">
    /// Whether the reading kept met a fault of the syntax, or may have, the pending diagnostics full: reported now or
    /// already, by an earlier reading of the same bytes.
    /// </param>
    /// <param name="initialWindow">The window the first attempt reads through.</param>
    /// <param name="keep">
    /// Whether what was read is what was looked for: when it says no, nothing the reading met is reported or recorded —
    /// a search for a stream's <c>endstream</c> aside, which is kept as every search is (<see cref="IPdfStreamDataProvider.FindEndStream"/>)
    /// —, a guard its window reached included, and the method returns false.
    /// </param>
    private bool TryParseNumberedAt(
        long offset,
        int number,
        PdfLimit limit,
        out PdfObject value,
        out int foundNumber,
        out bool syntaxFault,
        int initialWindow = InitialObjectWindow,
        Func<PdfObject, bool>? keep = null)
    {
        value = PdfNull.Instance;
        foundNumber = 0;
        syntaxFault = false;

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

                // A window that ends where the file does cut nothing, however long it is: what it holds is all there is.
                var cut = window.Length == windowSize && offset + window.Length < _source.Length;
                var parser = new PdfObjectParser(window.Memory, offset, this, _pending, this, endsData: !cut);
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

                var lengthFault = parser.LengthFault;

                if (parser.IsTruncated && cut)
                {
                    if (windowSize < maxWindow)
                    {
                        _pending.RollBack(mark);
                        windowSize = (int)Math.Min((long)windowSize * 8, maxWindow);
                        continue;
                    }

                    if (keep?.Invoke(parsed) == false)
                    {
                        return false;
                    }

                    // What the parser met before the cut is the file's, and is kept; it reported nothing of the token the
                    // window's edge cut, which is the guard's (ADR 34), and what it found of a stream is dropped with the cut.
                    syntaxFault = SyntaxFaultSince(_pending, mark);
                    KeepPending(mark, syntaxOnly: true);
                    ReachLimit(limit, LimitSubject(number, maxWindow), offset);
                    MarkCutByGuard(parsed, offset + window.Length);

                    if (foundNumber > 0)
                    {
                        _cutAtLimit.Add(foundNumber);
                    }

                    // A stream the guard cut ran into the window's edge, not the file's end: what the parser found of it
                    // is the limit's, and was dropped with what it reported.
                    lengthFault = null;
                }
                else if (keep?.Invoke(parsed) == false)
                {
                    return false;
                }

                syntaxFault |= SyntaxFaultSince(_pending, mark);
                KeepPending(mark);
                RecordEndObj(number, foundNumber, parser.EndObj, cut, offset);
                RecordStreamLength(number, foundNumber, parsed, lengthFault);
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
    /// Parses object <paramref name="id"/> at an exact offset, as <see cref="TryParseAt"/> does, but keeps nothing of the
    /// reading: what the parser notices is dropped, nothing is recorded, and an object that still runs past the window
    /// once <see cref="PdfReaderLimits.MaxObjectLength"/> allows no larger one is not read rather than reported. A stream
    /// is not read: its data is looked for in the window alone, and the method returns false.
    /// </summary>
    private bool TryReadUnrecorded(PdfObjectId id, long offset, out PdfObject value)
    {
        value = PdfNull.Instance;

        if (offset < 0 || offset >= _source.Length)
        {
            return false;
        }

        var maxWindow = _guard.Bound(PdfLimit.Object);
        var windowSize = Math.Min(InitialObjectWindow, maxWindow);
        var mark = _pending.GetMark();

        try
        {
            while (true)
            {
                using var window = _source.GetWindow(offset, windowSize);
                var cut = window.Length == windowSize && offset + window.Length < _source.Length;
                var parser = new PdfObjectParser(window.Memory, offset, this, _pending, endsData: !cut);
                var read = parser.TryReadIndirectObject(out var found, out var parsed);

                if (parser.IsTruncated && cut)
                {
                    if (windowSize >= maxWindow)
                    {
                        return false;
                    }

                    _pending.RollBack(mark);
                    windowSize = (int)Math.Min((long)windowSize * 8, maxWindow);
                    continue;
                }

                if (!read || found != id || parsed is PdfStream)
                {
                    return false;
                }

                value = parsed;
                return true;
            }
        }
        finally
        {
            _pending.RollBack(mark);
        }
    }

    /// <summary>
    /// Records whether <c>endobj</c> follows an object read at <paramref name="offset"/>: it does not when another
    /// token follows it, or when the file ends first — not when the window the reader offered ended first, where
    /// what follows was never seen. A stream whose data runs past the window is seen to its end all the same: the
    /// file is asked what follows its <c>endstream</c> (#55).
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
    /// Records what was found of the stream a kept reading of object <paramref name="found"/> ended with, when the file
    /// did not confirm its length, and that it was reported: the stream is not reported again when it is parsed again.
    /// A kept reading that shows the stream sound, or the object no stream, clears what an earlier one recorded.
    /// </summary>
    private void RecordStreamLength(int number, int found, PdfObject parsed, StreamLengthFault? fault)
    {
        if (fault is { } reported)
        {
            _lengthFaultsReported.Add(reported.DataStart);
        }

        if (number == DirectObject || found <= 0)
        {
            return;
        }

        if (fault is { } kept && parsed is PdfStream)
        {
            _streamLengthFaults[found] = kept;
        }
        else
        {
            _streamLengthFaults.Remove(found);
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
        _ => string.Create(
            CultureInfo.InvariantCulture,
            $"Object {number} runs past {PdfLimitGuard.FormatLength(bound)}, its stream data aside; only what lies within it was read."),
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

    private bool TryFindObjectHeader(int number, long approximateOffset, out long actualOffset, out int generation) =>
        TryFindObjectHeader(_source, number, approximateOffset, out actualOffset, out generation);

    /// <summary>
    /// Looks for the header of object <paramref name="number"/> within <see cref="NearbySearchRadius"/> bytes either
    /// side of <paramref name="approximateOffset"/>, where careless writers leave an object their index misplaced,
    /// and gives the first found, with the generation it gives.
    /// </summary>
    internal static bool TryFindObjectHeader(
        PdfFileSource source, int number, long approximateOffset, out long actualOffset, out int generation)
    {
        actualOffset = -1;
        generation = 0;

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

            if (TryReadHeaderBackwards(span, position, out var found, out var foundGeneration, out var headerStart) && found == number)
            {
                actualOffset = start + headerStart;
                generation = foundGeneration;
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
            (TryFindObjectHeader(number, offset, out var nearby, out _) && TryParseObjectAt(number, nearby, out value));

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

            if (numberToken.Kind != PdfTokenKind.Integer || offsetToken.Kind != PdfTokenKind.Integer)
            {
                fault = string.Create(
                    CultureInfo.InvariantCulture, $"its header lists {i} of the {count} objects its /N declares, then something else");
                return ObjectStreamHeaderResult.Unreadable;
            }

            if (numberToken.Integer is <= 0 or > PdfObjectId.MaxNumber || offsetToken.Integer is < 0 or > int.MaxValue)
            {
                fault = string.Create(
                    CultureInfo.InvariantCulture,
                    $"its header lists {i} of the {count} objects its /N declares, then object {numberToken.Integer} at offset {offsetToken.Integer}, which no member can be");
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
    /// <param name="span">The bytes the keyword lies in.</param>
    /// <param name="objPosition">Where the keyword starts in <paramref name="span"/>.</param>
    /// <param name="number">The object number the header gives.</param>
    /// <param name="generation">The generation the header gives, as the parser reads it: what an entry recording the
    /// header records (#118).</param>
    /// <param name="headerStart">Where the header starts in <paramref name="span"/>.</param>
    private static bool TryReadHeaderBackwards(
        ReadOnlySpan<byte> span, int objPosition, out int number, out int generation, out int headerStart)
    {
        number = 0;
        generation = 0;
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
        if (numberStart == numberEnd)
        {
            return false;
        }

        // Each run is judged by its value, however many zeros lead it, as the parser judges it: a header the parser would
        // refuse, found here, would be an object no read can serve. A run of digits always parses, as an integer within a
        // long or as a real past one.
        _ = PdfNumberParser.TryParse(span[numberStart..numberEnd], out var parsed, out _, out var numberPastLong);
        _ = PdfNumberParser.TryParse(span[generationStart..generationEnd], out var parsedGeneration, out _, out var generationPastLong);

        if (numberPastLong || parsed is <= 0 or > PdfObjectId.MaxNumber || generationPastLong || parsedGeneration > PdfObjectId.MaxGeneration)
        {
            return false;
        }

        number = (int)parsed;
        generation = (int)parsedGeneration;
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

        var mark = _pending.GetMark();

        try
        {
            var value = contents.Parse(entry.IndexInObjectStream, id.Number, this, _diagnostics, _pending, out var cut);

            if (cut)
            {
                // A member that runs into the end of data a guard cut is kept as far as it was read, like a regular
                // object cut at MaxObjectLength; what the parser met at the cut is the guard's, not the file's, and was
                // not reported, while what it met before the cut is the file's (ADR 34).
                _cutAtLimit.Add(id.Number);
                KeepPending(mark, syntaxOnly: true);
            }
            else
            {
                KeepPending(mark);
            }

            return value;
        }
        finally
        {
            _pending.RollBack(mark);
        }
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
            var contents = loaded is PdfStream stream ? ObjectStreamContents.TryCreate(number, stream, _diagnostics, _objectStreamReports) : null;

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
    /// each object number under the generation its header gives, and then looking for a trailer and for anything
    /// that can act as one.
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

        var redefinitions = new Redefinitions();
        ScanForObjects(redefinitions);
        ScanForTrailers();

        // Loading objects can reach a guard. A document opened to throw on one throws once the index is
        // whole, not half-way through rebuilding it: a rebuild is never run twice, and one abandoned mid-way
        // would leave the document unable to find the objects it had not reached.
        var reached = ExpandObjectStreams(redefinitions);

        if (redefinitions.Count > 0)
        {
            // An updated file defines its changed objects again, so this is what most rebuilds meet: worth knowing,
            // once, rather than a report for each number.
            _diagnostics.Add(PdfDiagnosticSeverity.Information, PdfDiagnosticCodes.ObjectRedefined, redefinitions.Describe());
        }

        if (!HasUsableRoot())
        {
            // Searched even when the expansion reached a guard; the first guard reached is the one thrown.
            var searching = FindCatalog();
            reached ??= searching;
        }

        reached?.Throw();
    }

    private void ScanForObjects(Redefinitions redefinitions)
    {
        var position = 0L;
        var found = 0;

        while (position < _source.Length && found < MaxRepairObjects)
        {
            var length = (int)Math.Min(ScanChunkSize, _source.Length - position);
            using var window = _source.GetWindow(position, length);
            var span = window.Memory.Span;

            // An obj keyword wholly within the overlap was found by the window before, which held it and what precedes
            // it: each header is read once.
            var searchFrom = position == 0 ? 0 : ScanOverlap - ObjKeyword.Length + 1;

            while (searchFrom < span.Length)
            {
                var index = span[searchFrom..].IndexOf(ObjKeyword);
                if (index < 0)
                {
                    break;
                }

                var objPosition = searchFrom + index;

                if (TryReadHeaderBackwards(span, objPosition, out var number, out var generation, out var headerStart))
                {
                    // The last definition wins: that is what an incrementally updated file means. Its entry records
                    // the generation its header gives, whatever an earlier definition gave (#118).
                    if (_xref.TryGet(number, out _))
                    {
                        redefinitions.Add(number);
                    }

                    _xref.Set(number, XRefEntry.Regular(position + headerStart - _headerOffset, generation));
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
            // A trailer longer than the window is cut at its edge, which is not the file's end (#49): what the edge cuts
            // short is not reported. One the chain read already is not reported again.
            using var window = _source.GetWindow(positions[i], XRefWindow);
            var mark = _pending.GetMark();

            try
            {
                var parser = new PdfObjectParser(
                    window.Memory, positions[i], this, _pending, this, endsData: positions[i] + window.Length >= _source.Length);

                if (parser.ParseObject().AsDictionary() is { } trailer)
                {
                    _xref.MergeTrailer(trailer);
                }

                KeepPending(mark);
            }
            finally
            {
                _pending.RollBack(mark);
            }
        }
    }

    private ExceptionDispatchInfo? ExpandObjectStreams(Redefinitions redefinitions)
    {
        _expandingObjectStreams = true;

        try
        {
            return ExpandEachObjectStream(redefinitions);
        }
        finally
        {
            _expandingObjectStreams = false;
        }
    }

    private ExceptionDispatchInfo? ExpandEachObjectStream(Redefinitions redefinitions)
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

                contents = ObjectStreamContents.TryCreate(number, stream, _diagnostics, _objectStreamReports);
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
                // An object written directly in the file wins over a copy inside an object stream, and the first
                // copy listed in the object streams read wins over the others.
                if (!_xref.TryAdd(contents.NumberAt(index), XRefEntry.Compressed(number, index)))
                {
                    redefinitions.Add(contents.NumberAt(index));
                }
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
                // The reference names the catalog as its entry does, read once the catalog is — a relocation on the way
                // gives the header's generation —: an entry of 1 1 makes 1 1 R, not 1 0 R (#118).
                var generation = _xref.TryGet(number, out var entry) && entry.Kind == XRefEntryKind.Regular ? entry.Generation : 0;
                Trailer.Set(PdfName.Root, new PdfReference(new PdfObjectId(number, generation), this));
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
    /// file's own, and the searches for <c>endstream</c> are still bounded by it. Once is enough: the copy is taken
    /// before the first change.
    /// </summary>
    private void PreserveChainIndex()
    {
        if (_structure.ChainRead && _chainIndex is null)
        {
            _chainIndex = _xref.CopyEntries();

            // The copy bounds the searches from now on, and sorts its own offsets when one asks.
            _xref.ForgetSortedOffsets();
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

    /// <summary>
    /// The definitions a rebuild met of object numbers it had already found: how many, and the first few numbers, kept
    /// without allocating for each.
    /// </summary>
    private sealed class Redefinitions
    {
        /// <summary>How many numbers the report lists at most: enough to look a few up in the file, few enough to read.</summary>
        private const int Listed = 10;

        private readonly int[] _numbers = new int[Listed];
        private int _listed;
        private bool _unlisted;

        /// <summary>
        /// Gets how many definitions met a number already found: a long, since the object streams a rebuild expands can
        /// list more members than an int counts.
        /// </summary>
        public long Count { get; private set; }

        /// <summary>Records a definition of <paramref name="number"/>, which was already found.</summary>
        public void Add(int number)
        {
            Count++;

            if (_numbers.AsSpan(0, _listed).Contains(number))
            {
                return;
            }

            if (_listed < Listed)
            {
                _numbers[_listed++] = number;
            }
            else
            {
                _unlisted = true;
            }
        }

        /// <summary>Says how many definitions were met, of which numbers, and which definition was kept.</summary>
        public string Describe()
        {
            var numbers = new StringBuilder();

            foreach (var number in _numbers.AsSpan(0, _listed))
            {
                numbers.Append(numbers.Length == 0 ? string.Empty : ", ").Append(number.ToString(CultureInfo.InvariantCulture));
            }

            var met = Count == 1
                ? string.Create(CultureInfo.InvariantCulture, $"Rebuilding the index met a second definition of object {numbers}.")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Rebuilding the index met {Count:N0} definitions of object numbers it had already found, {(_unlisted ? "among them those of objects" : _listed == 1 ? "of object" : "of objects")} {numbers}.");
            return met +
                " Of each number, the definition kept is the last written directly in the file, or, for a number written only inside object streams, the first listed in the object stream read first.";
        }
    }

    /// <summary>What the object streams' reports said already, so that each fault is reported once.</summary>
    private sealed class ObjectStreamReports
    {
        /// <summary>Where the data starts of each object stream whose own fault was reported.</summary>
        private readonly HashSet<long> _faults = [];

        /// <summary>Each object reported at another index of an object stream than its entry gives, with that stream.</summary>
        private readonly HashSet<(int Stream, int Number)> _moves = [];

        /// <summary>Records the fault of the object stream whose data starts at <paramref name="dataStart"/>, and tells whether it is the first.</summary>
        public bool IsFirstFault(long dataStart) => _faults.Add(dataStart);

        /// <summary>Records that object <paramref name="number"/> is elsewhere in object stream <paramref name="stream"/>, and tells whether it is the first time.</summary>
        public bool IsFirstMove(int stream, int number) => _moves.Add((stream, number));
    }

    /// <summary>The objects packed inside one object stream, and where each of them starts.</summary>
    private sealed class ObjectStreamContents
    {
        private readonly ReadOnlyMemory<byte> _data;
        private readonly int[] _numbers;
        private readonly int[] _offsets;
        private readonly int _first;

        /// <summary>The stream's object number, which its reports name.</summary>
        private readonly int _number;

        /// <summary>Where the stream's data starts in the file, where its reports are placed.</summary>
        private readonly long _position;

        /// <summary>What the object streams' reports said already.</summary>
        private readonly ObjectStreamReports _reports;

        /// <summary>Whether a guard cut the stream's data, raw or decoded: its end is then the limit's, not the file's.</summary>
        private readonly bool _cutByGuard;

        /// <summary>
        /// Where each number lies in the header, the first place when it lies in several; built the first time an
        /// entry's index is wrong, so that an index every entry of the file gets wrong costs a lookup, not a search.
        /// </summary>
        private Dictionary<int, int>? _indexes;

        private ObjectStreamContents(
            int number,
            long position,
            ObjectStreamReports reports,
            ReadOnlyMemory<byte> data,
            int[] numbers,
            int[] offsets,
            int first,
            bool cutByGuard)
        {
            _number = number;
            _position = position;
            _reports = reports;
            _data = data;
            _numbers = numbers;
            _offsets = offsets;
            _first = first;
            _cutByGuard = cutByGuard;
        }

        public int Count => _numbers.Length;

        /// <summary>Gets the length of the decoded data, which is what keeping the contents costs.</summary>
        public int Length => _data.Length;

        public int NumberAt(int index) => _numbers[index];

        /// <summary>
        /// Reads the header of object stream <paramref name="number"/>, or gives null when it serves no object: it
        /// declares none, or what it says of itself cannot be believed, which is reported.
        /// </summary>
        /// <remarks>
        /// Each report is placed where the stream's data starts, and says which fault it met and how many of the
        /// stream's objects can still be read from it; it is made once for each stream, however often the stream is
        /// decoded. A <c>/First</c> past data a guard cut is not reported: the guard was, under its own code.
        /// </remarks>
        public static ObjectStreamContents? TryCreate(int number, PdfStream stream, PdfDiagnostics diagnostics, ObjectStreamReports reports)
        {
            var position = stream.Data.Position;
            var count = stream.Dictionary.GetInteger(PdfName.N, -1);
            var first = stream.Dictionary.GetInteger(PdfName.First, -1);

            // A stream that declares no object is empty, not damaged.
            if (count == 0)
            {
                return null;
            }

            if (count < 0)
            {
                ReportUnreadable(diagnostics, reports, number, position, "has no /N that gives a count of objects", 0, count);
                return null;
            }

            if (first < 0)
            {
                ReportUnreadable(diagnostics, reports, number, position, "has no /First that gives where its objects start", 0, count);
                return null;
            }

            // Each entry of the header costs at least "0 0 ", so a count far beyond what the header could
            // hold is a lie, and believing it would mean allocating arrays the file asked for.
            if (count > (first / 2) + 1)
            {
                ReportUnreadable(
                    diagnostics,
                    reports,
                    number,
                    position,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"declares {count:N0} objects in /N, more than a header of the {first:N0} bytes its /First gives can list"),
                    0,
                    count);
                return null;
            }

            var mark = diagnostics.GetMark();
            var data = stream.Decode(diagnostics);
            var cutByGuard = LimitSince(diagnostics, mark) || stream.Data.CutByGuard;

            if (first > data.Length)
            {
                if (!cutByGuard)
                {
                    ReportUnreadable(
                        diagnostics,
                        reports,
                        number,
                        position,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"gives in /First an offset, {first:N0}, past the end of its decoded data, which is {data.Length:N0} bytes long"),
                        0,
                        count);
                }

                return null;
            }

            // Both now fit an int: the count is bounded by the header, and the header by the data.
            var numbers = new int[count];
            var offsets = new int[count];
            var lexer = new PdfLexer(data.Span[..(int)first]);

            for (var i = 0; i < count; i++)
            {
                var numberToken = lexer.Read();
                var offsetToken = lexer.Read();

                if (numberToken.Kind != PdfTokenKind.Integer || offsetToken.Kind != PdfTokenKind.Integer)
                {
                    // A stray token is the fault whatever follows it; the header ends where the first missing token is.
                    var ends = numberToken.Kind == PdfTokenKind.EndOfInput ||
                        (numberToken.Kind == PdfTokenKind.Integer && offsetToken.Kind == PdfTokenKind.EndOfInput);
                    var fault = ends
                        ? "has a header that ends after"
                        : "has a header that holds something other than an object number and an offset after";
                    ReportUnreadable(
                        diagnostics,
                        reports,
                        number,
                        position,
                        string.Create(CultureInfo.InvariantCulture, $"{fault} {i:N0} of the {count:N0} objects its /N declares"),
                        i,
                        count);
                    return i > 0 ? new ObjectStreamContents(number, position, reports, data, numbers[..i], offsets[..i], (int)first, cutByGuard) : null;
                }

                // A pair no member can have ends the header as a stray token does: narrowed, it would name another object
                // or start it elsewhere.
                if (numberToken.Integer is <= 0 or > PdfObjectId.MaxNumber || offsetToken.Integer is < 0 or > int.MaxValue)
                {
                    ReportUnreadable(
                        diagnostics,
                        reports,
                        number,
                        position,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"has a header that lists object {numberToken.Integer} at offset {offsetToken.Integer}, which no member can be, after {i:N0} of the {count:N0} objects its /N declares"),
                        i,
                        count);
                    return i > 0 ? new ObjectStreamContents(number, position, reports, data, numbers[..i], offsets[..i], (int)first, cutByGuard) : null;
                }

                numbers[i] = (int)numberToken.Integer;
                offsets[i] = (int)offsetToken.Integer;
            }

            return new ObjectStreamContents(number, position, reports, data, numbers, offsets, (int)first, cutByGuard);
        }

        /// <summary>Parses the object the stream holds under <paramref name="expectedNumber"/>, or gives null when it holds none.</summary>
        /// <param name="index">Where the index says the object is among the stream's.</param>
        /// <param name="expectedNumber">The object's number.</param>
        /// <param name="source">Resolves what the object refers to.</param>
        /// <param name="diagnostics">Receives what the index got wrong of the object's place.</param>
        /// <param name="parsing">Receives what parsing met, for the caller to keep or drop.</param>
        /// <param name="cut">Whether the object runs into the end of data a guard cut, and was read only as far as it.</param>
        public PdfObject? Parse(
            int index, int expectedNumber, IPdfObjectSource source, PdfDiagnostics diagnostics, PdfDiagnostics parsing, out bool cut)
        {
            cut = false;

            if (index < 0 || index >= _numbers.Length || _numbers[index] != expectedNumber)
            {
                // The index in the entry is a hint; the object number is the truth.
                var corrected = IndexOf(expectedNumber);
                if (corrected < 0)
                {
                    return null;
                }

                // The entry is left as it is: the object is found by its number each time it is parsed, and
                // reported the first.
                if (_reports.IsFirstMove(_number, expectedNumber))
                {
                    diagnostics.Repair(
                        PdfDiagnosticCodes.ObjectStreamMemberMoved,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Object {expectedNumber} is at index {corrected} of object stream {_number}, not at index {index}, where the cross-reference index places it."),
                        _position);
                }

                index = corrected;
            }

            // Both are at most int.MaxValue, so their sum is computed as a long, where it cannot wrap.
            var start = (long)_first + _offsets[index];

            if (start >= _data.Length)
            {
                return null;
            }

            var parser = PdfObjectParser.ForObjectStreamMember(_data, _number, _position, expectedNumber, source, parsing, endsData: !_cutByGuard);
            parser.Position = (int)start;
            var value = parser.ParseObject();
            cut = _cutByGuard && parser.IsTruncated;
            return value;
        }

        /// <summary>
        /// Reports, unless it was already, that object stream <paramref name="number"/> serves <paramref name="kept"/> of
        /// its objects at most.
        /// </summary>
        private static void ReportUnreadable(
            PdfDiagnostics diagnostics, ObjectStreamReports reports, int number, long position, string fault, int kept, long count)
        {
            if (!reports.IsFirstFault(position))
            {
                return;
            }

            diagnostics.Warn(
                PdfDiagnosticCodes.ObjectStreamUnreadable,
                kept == 0
                    ? string.Create(CultureInfo.InvariantCulture, $"Object stream {number} {fault}; none of its objects can be read from it.")
                    : string.Create(CultureInfo.InvariantCulture, $"Object stream {number} {fault}; only the first {kept:N0} of its {count:N0} objects can be read from it."),
                position);
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
