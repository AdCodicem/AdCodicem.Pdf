using System.Buffers;
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
/// <para>
/// It is not disposable: a reference read from the file hands it out as its <see cref="PdfReference.Source"/>, and no
/// holder of one may close the document's file. The document closes it, through <see cref="Close"/>.
/// </para>
/// </remarks>
internal sealed class PdfFileReader : IPdfObjectSource, IPdfStreamDataProvider
{
    internal const int InitialObjectWindow = 8 * 1024;
    internal const int XRefWindow = 64 * 1024;

    /// <summary>What the codes of the faults of the object syntax start with.</summary>
    private const string SyntaxCodePrefix = "syntax.";

    /// <summary>How much of a section is read to tell a classic table from a cross-reference stream.</summary>
    internal const int XRefProbeLength = 32;

    /// <summary>
    /// The length of a classic table's row as producers write it short, its end of line a single byte where ISO 32000-1
    /// (7.5.4) asks for two: the index makes room for no more rows than a table's bytes hold at this length, nor than they
    /// hold lines. A row written shorter still takes room as the map grows.
    /// </summary>
    private const int ShortRowLength = 19;

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
    /// How many bytes <see cref="HeaderAt"/> reads first where an entry places its object, free of what a pass that reads
    /// every entry's header may consume: enough for a header after some white space. A header that runs past them, or that
    /// their edge may have cut, is read on from there in a window eight times larger, and so on, the window a load starts
    /// with taken on the way, no further than a load reads. Not a bound: the size of a first read, which holds the headers
    /// of real files.
    /// </summary>
    private const int HeaderCheckWindow = 64;

    /// <summary>What <see cref="HeaderAt"/> gives where a header still runs past <see cref="PdfReaderLimits.MaxObjectLength"/>.</summary>
    private const long HeaderPastGuard = -1;

    /// <summary>
    /// What <see cref="HeaderAt"/> gives where reading a header further would consume more than its caller has left.
    /// </summary>
    private const long HeaderReadingSpent = -2;

    /// <summary>
    /// How many times the file's length a pass that reads the header of every entry of an index consumes at most, over all
    /// the headers it reads, past the first <see cref="HeaderCheckWindow"/> bytes of each: <see cref="SettleIndex"/>'s, which
    /// rebuilds the index past it, in one pass over the file, rather than read on; and each validation's probe of the
    /// cross-reference entries, which says the entries it did not decide were not checked. A header's reading consumes the
    /// bytes it crossed up to its decision — white space, comments, the header's tokens —, and reads about ten times that at
    /// most, its windows grown eightfold, past a few small windows an entry reads free: the first, and the next where the
    /// first does not decide — white space, a comment or a header running past its edge —; past a run it skips, it consumes
    /// every window it reads whole, the run it skipped
    /// consumed by another. A run of white space several entries lead into is
    /// consumed once, while it is one of the runs remembered. Entries can
    /// place their objects in one long comment, each read again to its end, which is what the bound stops. An internal
    /// bound (ADR 34): a valid file's entries place their objects' headers each at its own offset, and its headers, distinct
    /// bytes, consume at most the file's length together.
    /// </summary>
    private const int HeaderReadingFactor = 2;

    /// <summary>
    /// The most <see cref="HeaderAt"/> reads at once of a run of white space it crosses. Not a bound: what it reads of a run in
    /// one window, the run being read to its end, or to where a load stops, in as many as it takes.
    /// </summary>
    private const int BlankCrossingWindow = 64 * 1024;

    /// <summary>
    /// The most places near missing cross-reference sections that are tried as one, over a whole document. Real
    /// files miss by a few bytes, with the section the nearest candidate; a file packed with object headers
    /// there, or with sections that all miss, gets no more attempts. Not a guard a valid file can reach: a valid
    /// file's sections are where its chain names them.
    /// </summary>
    private const int MaxRelocationCandidates = 32;
    private const int ScanChunkSize = 1024 * 1024;
    private const int ScanOverlap = 64;

    /// <summary>
    /// What the object streams kept at once weigh, in bytes: their decoded data, and the tables and lookup of their
    /// headers (#160). Past it the oldest is let go, and decoded again if an object it holds is asked for, so no file is
    /// read differently for it: the bound is on memory, not on what can be read. The object stream decoded last is kept
    /// whatever its size, so memory follows the heaviest object stream rather than the number of them.
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
    /// before a stream whose declared length no <c>endstream</c> follows keeps it without a search past the bytes the
    /// parser holds, and white space after a declared length is read no further than a first read.
    /// </summary>
    /// <remarks>
    /// Not a guard a valid file reaches (ADR 34), argued here once for every read it counts. A valid file is read past the
    /// parser's window in three ways only: a cross-reference stream whose <c>/Length</c> the chain cannot read (#220) is
    /// searched once to its <c>endstream</c>; a stream whose <c>/Length</c> a guard cut or left out of the index (#224) is
    /// searched once to its <c>endstream</c>, or to a header its data spells, which leaves the data up to it to the guard;
    /// and white space after a declared length that runs past a first read is read on once for each place (#174). The data
    /// of different streams lies apart, and so do the runs of white space after them: together those reads cover the file
    /// once, and one read past each — about twice the file at most. Every other search is of an invalid stream, whose
    /// <c>endstream</c> does not follow its declared length. Each
    /// reads from a stream's data to the first object header the bytes hold after it, or to the next object the index as
    /// written places, whichever is nearer, and its reads grow from twice what the parser holds of the data
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

    /// <summary>Asks <see cref="TryParseNumberedAt"/> for a direct object, with no object header: a trailer.</summary>
    private const int DirectObject = -1;

    /// <summary>Asks <see cref="TryParseNumberedAt"/> to note what it reads of the object it was asked for, wherever it lies.</summary>
    private const long RecordedAnywhere = -1;

    /// <summary>Asks <see cref="TryParseNumberedAt"/> to note nothing of the object it reads, by its number.</summary>
    private const long RecordedNowhere = -2;

    /// <summary>Asks <see cref="TryParseNumberedAt"/> for an object whatever its number: a cross-reference stream.</summary>
    private const int AnyObject = 0;

    /// <summary>
    /// Deepest chain of objects loaded while loading another. Real documents stay within a handful — a stream
    /// in an object stream whose <c>/Length</c> is indirect is three —; each level costs a dozen frames, and the
    /// parsing of the object it loads besides. No level holds decoded data while it loads the next: a stream resolves
    /// its filters and every parameter they read before its first filter runs (<see cref="Filters.FilterChain"/>), so
    /// that a chain of loads holds at once one stream's decoding, and the object streams
    /// <see cref="ObjectStreamBudget"/> keeps; no stream keeps the raw data it read (#170). A stream an object stream
    /// holds, which ISO 32000-1 (7.5.7) forbids, is no exception: it is no value, and its <c>/Length</c> is not read
    /// (#161). The count alone does not bound the stack, since each load parses as deep as the parser allows: the stack
    /// is asked as well before each load.
    /// </summary>
    private const int MaxNestedLoads = 64;

    /// <summary>
    /// How many references <see cref="Follow"/> follows from a value, as <see cref="PdfReference.Resolve"/> does — it fetches
    /// one more, and reads null whatever that one holds (#173) —: a value still a reference past them reads as null there,
    /// and cannot be read here. No valid file reaches it: a valid file never chains references.
    /// </summary>
    private const int MaxReferenceLinks = 31;

    /// <summary>
    /// How many references are followed from a value read from the object a reference names and then resolved, as a stream's
    /// <c>/Length</c> is parsed and a trailer's <c>/Prev</c> and <c>/XRefStm</c> are read: one more than
    /// <see cref="MaxReferenceLinks"/>, the first object's value resolving through as many as a reference does. No valid file
    /// reaches it either.
    /// </summary>
    private const int LengthLinks = MaxReferenceLinks + 1;

    private static ReadOnlySpan<byte> ObjKeyword => "obj"u8;
    private static ReadOnlySpan<byte> TrailerKeyword => "trailer"u8;
    private static ReadOnlySpan<byte> StartXRefKeyword => "startxref"u8;
    private static ReadOnlySpan<byte> HeaderMarker => "%PDF-"u8;

    /// <summary>
    /// What a classic table's rows and subsection headers are made of: digits, signs, white space, <c>n</c> and <c>f</c>.
    /// </summary>
    private static readonly SearchValues<byte> TableBytes = SearchValues.Create("0123456789+-nf\0\t\n\f\r "u8);

    /// <summary>The bytes that may start a comment, the trailer keyword or a dictionary, among what no table holds.</summary>
    private static readonly SearchValues<byte> TrailerSearchBytes = SearchValues.Create("%t<"u8);

    private PdfFileSource _source;
    private readonly PdfDiagnostics _diagnostics;
    private readonly PdfLimitGuard _guard;

    /// <summary>The guards already reported, and where: an object read again is not reported again.</summary>
    private readonly HashSet<(PdfLimit Limit, long Position)> _limitsReached = [];

    /// <summary>
    /// The object stream members <see cref="PdfReaderLimits.MaxObjectLength"/> cut, by where their stream's data starts and
    /// their number, each reported once: a stream defined again, which a rebuild may serve, is another place.
    /// </summary>
    private readonly HashSet<(long DataStart, int Number)> _memberLimitsReached = [];

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
    /// The trailer of the section the chain ended at because one of the reader's limits cut it, or null: what the document's
    /// trailer takes from it is read only as far as the limit let it be.
    /// </summary>
    private PdfDictionary? _cutTrailer;

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
    /// The objects <see cref="ReadWithoutLoading"/> could not read while the chain is read because they run past
    /// <see cref="PdfReaderLimits.MaxObjectLength"/>, each with the integer the cut left of its value, or
    /// <see cref="PdfNull"/> when it left none; null while none did. What they hold is the guard's to hide, and the integer
    /// only a place to look for <c>endstream</c> at (#224), read once however many sections ask. One whose value the cut left
    /// a whole reference is read through, and is not among them. Let go with <see cref="_readWhileChainIsRead"/>.
    /// </summary>
    private Dictionary<PdfObjectId, PdfObject>? _cutWhileChainIsRead;

    /// <summary>
    /// The objects a guard cut that a cross-reference stream's values led to, which the chain could not read and ended at, and
    /// those it read through, the cut having left their value a whole reference — but for an object it read as a section
    /// and refused —: each is loaded once the chain is read, so that its guard is reached where a load reaches it (#263).
    /// Let go then, or when the chain is rebuilt.
    /// </summary>
    private List<PdfObjectId>? _chainValuesCut;

    /// <summary>
    /// The object numbers whose rows a guard left unread in the cross-reference stream just read — past the data it cut, or
    /// from the row the index had no room for —, as pairs of a first number and a count; null where the section just read
    /// is a table, whose rows past a guard may number any object. Set as each section is read, for its own trailer.
    /// </summary>
    private long[]? _rowsLeftUnread;

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
    /// and what they weigh — their data, their headers' tables and lookups —, which <see cref="ObjectStreamBudget"/> bounds.
    /// </summary>
    private readonly Dictionary<int, ObjectStreamContents?> _objectStreams = [];

    /// <summary>
    /// The object streams, by number, that a guard left unable to serve their objects — cut short of their stream, their
    /// data cut before <c>/First</c> or left encoded, a guard reached decoding them under
    /// <see cref="PdfReaderOptions.ThrowOnLimit"/> —: an object they hold is unread for the guard's reasons, not the file's,
    /// as one a guard cut is (#259). A rebuild's expansion records any object a guard cut short of a stream, which the
    /// chain's index may place members in.
    /// </summary>
    private readonly HashSet<int> _objectStreamsHiddenByGuard = [];
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
    /// What each search of the file for a stream's <c>endstream</c> found, by where the stream's data starts and whether
    /// its <c>/Length</c> was one the chain could not read: the file is searched once for each stream, however many times
    /// it is parsed. With it, the declared end it last said what follows of, read again only when that end changes.
    /// </summary>
    private readonly Dictionary<(long DataStart, bool Deferred), (StreamEndSearch Search, long DeclaredEnd)> _endStreamSearches = [];

    /// <summary>
    /// What follows each place the reader read on past white space and comments to find an <c>endstream</c> (#174): read
    /// once, however many times its stream is parsed.
    /// </summary>
    private readonly Dictionary<long, EndObjState?> _endStreamChecks = [];

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

    /// <summary>
    /// Whether the document was disposed: from then on, whatever asks the reader for an object or for a stream's data is
    /// refused, whatever it kept.
    /// </summary>
    private bool _closed;

    /// <summary>The version the header names, one digit, a period and one digit; null when it names none.</summary>
    private string? _headerVersion;

    private bool _repaired;

    /// <summary>
    /// A rebuild a load set off, which runs with that load and those it is part of still in flight, and is finished once the
    /// outermost returns; null when no rebuild waits to be finished.
    /// </summary>
    private UnfinishedRebuild? _unfinishedRebuild;
    private bool _nestingReported;

    /// <summary>
    /// Whether the document finished opening: <see cref="Structure"/> holds what the file showed then, and what the reader
    /// finds afterward — a rebuild run later — is kept apart from it.
    /// </summary>
    private readonly bool _opened;

    /// <summary>
    /// Whether <see cref="SettleIndex"/> ran: it rebuilt the index, or found that no load can; either way no later load
    /// rebuilds it, and it does not look again.
    /// </summary>
    private bool _settled;

    /// <summary>What <see cref="SettleIndex"/>'s reading of the headers may still consume, and the run of white space it crossed.</summary>
    private HeaderReading? _settlingReading;

    /// <summary>The object the reader last took for the catalog, <c>/Root</c> leading to none; 0 while it took none.</summary>
    private int _catalogFoundAs;

    /// <summary>
    /// Where the newest dictionary a rebuild merged into the trailer starts, at its <c>trailer</c> keyword; -1 while no
    /// rebuild merged one.
    /// </summary>
    private long _rebuiltTrailerPosition = -1;

    /// <summary>How many places have been tried for sections of the chain that were not where it named them.</summary>
    private int _relocationCandidatesTried;

    /// <summary>
    /// Whether the index may lack objects the file defines because the file is damaged: a section the chain names could not
    /// be found or read, the chain loops, a cross-reference stream's data holds fewer rows than it declares, or a row was
    /// refused. Only then is an object the index lacks looked for by rebuilding it; otherwise a reference to it is null, as
    /// the specification says.
    /// </summary>
    private bool _indexIncomplete;

    /// <summary>
    /// Whether one of the reader's guards left part of the chain unread: <see cref="PdfReaderLimits.MaxXRefSectionCount"/>
    /// stopped it, <see cref="PdfReaderLimits.MaxXRefSectionLength"/> cut a table, or a cross-reference stream lost rows to
    /// <see cref="PdfReaderLimits.MaxDecodedStreamLength"/> or the section itself to <see cref="PdfReaderLimits.MaxTrailerLength"/>.
    /// What lies past the cut reads as null until the caller lifts the guard, whose report names the property: the cut is
    /// the reader's, not damage in the file, and the index is not rebuilt for it (ADR 34).
    /// </summary>
    private bool _cutByGuard;

    /// <summary>
    /// Whether the index as last built had no room for a number it lacks, <see cref="PdfReaderLimits.MaxIndexEntries"/>
    /// reached: an object it holds no entry for reads as null, and is not looked for by rebuilding it.
    /// </summary>
    private bool _indexFull;

    /// <summary>
    /// The object numbers whose row a section of the chain refused, or null while none was: the sections the chain reads
    /// after it describe older revisions, whose rows must not stand for the current one. Let go once the chain is read,
    /// when nothing reads it any more; until then <see cref="PdfReaderLimits.MaxIndexEntries"/> counts it with the index,
    /// which no number is in at once.
    /// </summary>
    private HashSet<int>? _refusedNumbers;

    /// <summary>
    /// Where the index first had no room for an object number, <see cref="PdfReaderLimits.MaxIndexEntries"/> reached: the
    /// section of the chain whose row it was, or the header or object stream a rebuild found it in; -1 while it has room.
    /// </summary>
    private long _indexFullAt = -1;

    /// <summary>Whether the index filled as the chain was read, before any rebuild: the chain's index stops short of it.</summary>
    private bool _chainFilledIndex;

    /// <summary>
    /// The highest object number a row of the chain's sections read so far gave, or -1: a <c>/Size</c> an update did not
    /// raise numbers fewer objects than the sections give, and the index makes room for these too.
    /// </summary>
    private int _highestRead = -1;

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
        Version = DeclaredVersion();
        _opened = true;
    }

    /// <summary>Gets the trailer of the document, merged across the whole cross-reference chain.</summary>
    public PdfDictionary Trailer => _xref.Trailer;

    /// <summary>
    /// Gets the version the file declares, such as <c>1.7</c>: its header's, or its catalog's <c>/Version</c> when that
    /// is later; null when its header names none.
    /// </summary>
    public string? Version { get; }

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
    /// Gets where the newest trailer the reader merged into <see cref="Trailer"/> lies, which a finding on a value the trailer
    /// holds points at; -1 when the reader merged none.
    /// </summary>
    /// <remarks>
    /// The first section's own trailer when the chain read it and merged it — a table's <c>trailer</c> keyword, or a
    /// cross-reference stream's object —, though the index be rebuilt afterward; otherwise the newest dictionary a rebuild
    /// found after a <c>trailer</c> keyword, which a rebuild run after opening — set off by a read, or before a validation's
    /// first rule — finds then. A key only an older trailer gave is placed there too: in the file, at a trailer the reader
    /// read (#126). The first section is never relocated: one the chain cannot read where <c>startxref</c> says, whether
    /// nothing is there or the section there is malformed, leaves the index to a rebuild, and the chain merged no trailer of
    /// its own; its record names the offset <c>startxref</c> gives, or the section's start, not a trailer the reader merged.
    /// </remarks>
    public long TrailerLocation =>
        _structure.Sections is [{ State: XRefSectionState.Read, Trailer: not null } first, ..]
            ? first.TrailerLocation
            : _rebuiltTrailerPosition;

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

    /// <summary>Gets what the object streams the reader keeps weigh, in bytes — their decoded data and their headers —, which it bounds.</summary>
    internal long ObjectStreamBytes => _objectStreamBytes;

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The document was disposed.</exception>
    public PdfObject GetObject(PdfObjectId id)
    {
        // First, before the cache: what a reference taken from a disposed document answers does not depend on what was
        // read before, nor on what the cache kept.
        ObjectDisposedException.ThrowIf(_closed, typeof(PdfDocument));

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

        // A rebuild a load set off and could not finish, the load having thrown, is finished before anything is read.
        if (_unfinishedRebuild is not null && _loading.Count == 0 && _objectStreamsLoading.Count == 0)
        {
            FinishUnfinishedRebuild();
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

        PdfObject value;

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
            var repaired = _repaired;
            value = LoadObject(id);

            // A null a guard against re-entry produced on the way is not the object's: it is read again when asked
            // again, rather than kept for good (#51). Nor is what was read through an index its own load had rebuilt — an
            // indirect /Length whose entry leads nowhere —: the next read takes the rebuilt index's definition of the
            // number, as it would had the rebuild run before.
            if ((value is not PdfNull || reentries == _reentries) && repaired == _repaired)
            {
                Cache(id.Number, value);
            }
        }
        catch (Exception) when (_unfinishedRebuild is not null)
        {
            // A load threw while a rebuild a load set off is unfinished: the outermost finishes it before the exception
            // leaves, so that the index stays the one a rebuild run first gives. Decided here, not in the filter, which runs
            // before the loads this one is part of have let go of their numbers.
            if (_loading.Count == 1 && _objectStreamsLoading.Count == 0)
            {
                _loading.Remove(id.Number);
                FinishUnfinishedRebuildAfterFailure();
            }

            throw;
        }
        finally
        {
            _loading.Remove(id.Number);
        }

        // A rebuild this load set off is finished once no load is in flight, as it would have been had it run before them;
        // the object is then read as the finished index serves it.
        if (_unfinishedRebuild is not null && _loading.Count == 0 && _objectStreamsLoading.Count == 0)
        {
            FinishUnfinishedRebuild();
            return GetObject(id);
        }

        return value;
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
    /// and decoding, and a guard that holds is reached when the object is loaded once the chain is read. One whose value
    /// the cut left a whole reference reads as that reference, as a load reads through it, and is loaded once the chain is
    /// read for its guard to be reached. The objects read through it nest as loads do, within <see cref="MaxNestedLoads"/>.
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

            // A reference the cut left whole leads on, as a load reads through it (#224): the guard hides nothing the chain
            // needs of the object, and is reached once the chain is read, where loading the object reaches it.
            var parsed = TryReadUnrecorded(id, PositionOf(entry), out var value, out var pastGuard);
            var read = parsed || (pastGuard && value is PdfReference) ? value : null;
            (_readWhileChainIsRead ??= [])[id] = read;

            if (pastGuard && read is null)
            {
                (_cutWhileChainIsRead ??= [])[id] = value.AsInteger() is null ? PdfNull.Instance : value;
            }
            else if (pastGuard)
            {
                (_chainValuesCut ??= []).Add(id);
            }

            return read;
        }
        finally
        {
            _loading.Remove(id.Number);
        }
    }

    /// <summary>
    /// Reads, while the chain is read, what the <c>/Length</c> object <paramref name="id"/> leads to through as many
    /// references as the parse resolves a <c>/Length</c> through (<see cref="LengthLinks"/>), each read with
    /// <see cref="ReadWithoutLoading"/>; null when one of them cannot be read — no section read so far places it, or a guard
    /// cut it —: a value whose chain leads there waits for the chain, as one the first reference cannot read does. A value
    /// still a reference past them is one the parse reads as null.
    /// </summary>
    private PdfObject? ReadThroughWithoutLoading(PdfObjectId id)
    {
        var read = ReadWithoutLoading(id);

        for (var link = 1; read is PdfReference next && link < LengthLinks; link++)
        {
            read = ReadWithoutLoading(next.Id);
        }

        return read;
    }

    /// <summary>
    /// Says whether the file holds object <paramref name="number"/> and whether the reader could produce it, as
    /// of the last time it was asked for: call it after resolving the object.
    /// </summary>
    /// <remarks>
    /// An object the index lacks, or holds as free — the one the chain gave as the reader first read it, and the
    /// one it reads with now —, is missing: a reference to it is null (ISO 32000-1, 7.3.10). One either index holds
    /// in use, and that the reader gave up on or lost in a rebuild, is unproduced: what went wrong is the index's
    /// or the object stream's, and the cross-reference rules report it. One the index lacks while a guard left part of the
    /// chain unread, or filled the index, is unindexed: whether the file holds it is unknown; and so is one the reader could
    /// not produce because the object stream that holds it is such an object. Object 0 is missing whatever its entry says:
    /// it heads the free list and is never in use (ISO 32000-1, 7.5.4), and the reader never reads it.
    /// </remarks>
    internal ObjectPresence GetPresence(int number)
    {
        if (number <= 0)
        {
            return ObjectPresence.Missing;
        }

        var indexed = _xref.TryGet(number, out var entry);

        if (indexed && entry.Kind != XRefEntryKind.Free)
        {
            return !_unproduced.Contains(number) ? ObjectPresence.Defined
                : HiddenByGuard(number) ? ObjectPresence.Unindexed
                : ObjectPresence.Unproduced;
        }

        if (ChainIndex?.TryGet(number, out var written) == true && written.Kind != XRefEntryKind.Free)
        {
            return ObjectPresence.Unproduced;
        }

        return !indexed && IndexStoppedByGuard ? ObjectPresence.Unindexed : ObjectPresence.Missing;
    }

    /// <summary>
    /// Gets a value indicating whether a guard stopped the index short of the file: it filled, or it is the chain's and a
    /// guard left part of the chain unread. A rebuild scans the whole file, and lifts the second.
    /// </summary>
    private bool IndexStoppedByGuard => _indexFull || (_cutByGuard && !_repaired);

    /// <summary>
    /// Gets a value indicating whether a guard stopped <see cref="ChainIndex"/> short of the file: the index filled as the
    /// chain was read, or a guard left part of the chain unread. An object the chain's index lacks is then unknown to it,
    /// whatever a rebuild found since.
    /// </summary>
    internal bool ChainStoppedByGuard => _chainFilledIndex || _cutByGuard;

    /// <summary>
    /// Gets the most filters a stream's data passes through as the reader decodes it
    /// (<see cref="PdfReaderLimits.MaxFilterChainLength"/>): the chain ends at the element past them.
    /// </summary>
    internal int FilterChainBound => _guard.Bound(PdfLimit.FilterChain);

    /// <summary>
    /// Gets where the index first had no room for an object number, <see cref="PdfReaderLimits.MaxIndexEntries"/> reached —
    /// as the document opened, or in a rebuild run since —, or -1 while it had room for every number it met.
    /// <see cref="FileStructure.IndexFullAt"/> keeps what it was when the document opened.
    /// </summary>
    internal long IndexFullAt => _indexFullAt;

    /// <summary>
    /// Gets the object the reader took for the catalog, <c>/Root</c> leading to none — as the document opened, or in a
    /// rebuild run since —, or 0 while it took none. <see cref="FileStructure.CatalogFoundAs"/> keeps what it was when the
    /// document opened.
    /// </summary>
    internal int CatalogFoundAs => _catalogFoundAs;

    /// <summary>Gets how many objects the reader keeps in its cache, at most.</summary>
    internal int CacheCapacity => _cacheCapacity;

    /// <summary>
    /// Brings the index to where no later load rebuilds it: rebuilds it now, once, when a load could still rebuild it, and
    /// otherwise leaves it as it is. The validator calls it before its first rule, so that what every rule reads was
    /// read after any rebuild, however much or little was read before (#237).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A load rebuilds the index in two cases: the index is known to be incomplete through damage and lacks the object; or
    /// the object is neither where its entry places it, nor near it. The first is known without reading anything. The
    /// second is decided here without loading anything: for each entry in use that places an object in the file, the header
    /// a load would parse there is read alone, as a load reads it — after white space and comments, in a window of
    /// <see cref="HeaderCheckWindow"/> bytes, then in windows eight times larger while the header runs past it, no further
    /// than a load reads —, and the 512 bytes either side are searched as a load searches them. An object a load
    /// would find so is one it finds, whatever its body holds; an entry that places an object in an object stream rebuilds
    /// nothing of its own, the stream's entry aside, which is one of the others. The checks stop at the first entry a load
    /// would rebuild for, and at the first header whose reading would take what settling consumed past twice the file's
    /// length, past the first bytes of each header — what a valid file's headers never take —: the index is then rebuilt,
    /// in one pass, rather than read on.
    /// </para>
    /// <para>
    /// A header that still runs past <see cref="PdfReaderLimits.MaxObjectLength"/> is where a load reaches that guard: one
    /// reports it and looks on, as here; a document opened to throw on a guard throws there instead, and rebuilds nothing,
    /// so that settling rebuilds nothing for it either, and leaves the guard to a load that asks for the object.
    /// </para>
    /// <para>
    /// Once settled, the index changes only where a load relocates an object found near its entry, which changes neither
    /// the entry's kind nor any other entry. A rebuild that reaches a guard, the document opened to throw on one, throws
    /// here once, with the index whole: the next call finds it settled. A rebuild a load set off and left unfinished, the
    /// load having failed, is finished first.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The document was disposed.</exception>
    /// <exception cref="PdfLimitExceededException">
    /// The rebuild reached one of the reader's guards, and the document was opened to throw on one.
    /// </exception>
    internal void SettleIndex()
    {
        ObjectDisposedException.ThrowIf(_closed, typeof(PdfDocument));

        // A rebuild a load set off and could not finish, the load having thrown, is finished first.
        if (_unfinishedRebuild is not null)
        {
            FinishUnfinishedRebuild();
        }

        if (_settled)
        {
            return;
        }

        _settled = true;
        _settlingReading = new HeaderReading(HeaderReadingAllowance);

        if (!_repaired && (_indexIncomplete || AnyLoadWouldRebuild()))
        {
            Repair();
        }
    }

    /// <summary>Determines whether loading some object the index holds in use, where it places it, would rebuild the index.</summary>
    private bool AnyLoadWouldRebuild()
    {
        foreach (var (number, entry) in _xref.Entries)
        {
            if (number > 0 && entry.Kind == XRefEntryKind.Regular && !LoadFindsWhereEntryPlaces(number, entry))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether a load of object <paramref name="number"/> finds it where <paramref name="entry"/> places it, or
    /// near it, as <see cref="LoadRegularObject"/> looks — what it would parse there is an object of that number —, or would
    /// not rebuild the index for it all the same, a guard thrown first. A guard a load reaches on the way and reports is
    /// reported here as it would be.
    /// </summary>
    private bool LoadFindsWhereEntryPlaces(int number, XRefEntry entry)
    {
        var placement = PlaceHeader(number, entry, _settlingReading!);

        // A load that reaches the guard on a header reports it and looks on; one opened to throw on a guard throws there,
        // and rebuilds nothing, which settling leaves to a load that asks for the object.
        if (!_guard.ThrowsOnLimit)
        {
            var offset = PositionOf(entry);

            // The guard's message is built only where the guard was reached: settling allocates nothing an entry.
            if (placement.GuardAtOffset)
            {
                ReachLimit(PdfLimit.Object, LimitSubject(number, _guard.Bound(PdfLimit.Object)), offset);
            }

            if (placement.Place == HeaderPlace.PastGuard && placement.Position != offset)
            {
                ReachLimit(PdfLimit.Object, LimitSubject(number, _guard.Bound(PdfLimit.Object)), placement.Position);
            }
        }

        return placement.Place switch
        {
            HeaderPlace.AtOffset or HeaderPlace.Nearby => true,
            HeaderPlace.PastGuard => _guard.ThrowsOnLimit,
            _ => false,
        };
    }

    /// <summary>Gets how much a pass that reads the header of every entry of an index may consume past the first window of each.</summary>
    internal long HeaderReadingAllowance => HeaderReadingFactor * _source.Length;

    /// <summary>
    /// Says what a load of object <paramref name="number"/> finds where <paramref name="entry"/> places it, as
    /// <see cref="LoadRegularObject"/> looks: its header at the offset the entry gives, read as <see cref="TryParseNumberedAt"/>
    /// reads it; else the first header of that number near the offset, which what a load parses there must confirm; else
    /// none. Nothing is parsed past a header, reported or recorded: the guard a load would reach on the way is said, not
    /// reported. Settling and the validator's probe of the cross-reference entries decide each entry through it, alike.
    /// </summary>
    /// <param name="number">The object's number.</param>
    /// <param name="entry">The regular entry that places it.</param>
    /// <param name="reading">
    /// What the pass may still consume past the first window of each header, which what they consume is taken from — a
    /// header that would need more is <see cref="HeaderPlace.Undecided"/> —, and the run of white space it crossed.
    /// </param>
    internal HeaderPlacement PlaceHeader(int number, XRefEntry entry, HeaderReading reading)
    {
        var offset = PositionOf(entry);
        long atOffset = 0;
        var guardAtOffset = false;

        if (IsInFile(offset))
        {
            var found = HeaderAt(offset, number, reading, out var generation, out var start);

            if (found == HeaderReadingSpent)
            {
                return new HeaderPlacement(HeaderPlace.Undecided, offset, -1, 0, GuardAtOffset: false);
            }

            if (found == number)
            {
                return new HeaderPlacement(HeaderPlace.AtOffset, start, generation, number, GuardAtOffset: false);
            }

            if (found == HeaderPastGuard)
            {
                // A document opened to throw on a guard throws there, and looks no further.
                if (_guard.ThrowsOnLimit)
                {
                    return new HeaderPlacement(HeaderPlace.PastGuard, offset, -1, 0, GuardAtOffset: true);
                }

                guardAtOffset = true;
            }
            else
            {
                atOffset = found;
            }
        }

        var nowhere = new HeaderPlacement(
            guardAtOffset ? HeaderPlace.PastGuard : HeaderPlace.NotFound, offset, -1, atOffset, guardAtOffset);

        if (!TryFindObjectHeader(number, offset, out var nearby, out _))
        {
            return nowhere;
        }

        // The search reads a header backwards from an "obj"; a load parses forwards from where it starts, and refuses what
        // the search took — "4 0 objection" —, or reaches the guard on it.
        var near = HeaderAt(nearby, number, reading, out var nearGeneration, out _);

        return near switch
        {
            HeaderReadingSpent => new HeaderPlacement(HeaderPlace.Undecided, offset, -1, atOffset, guardAtOffset),
            HeaderPastGuard => new HeaderPlacement(HeaderPlace.PastGuard, nearby, -1, atOffset, guardAtOffset),
            _ when near == number => new HeaderPlacement(HeaderPlace.Nearby, nearby, nearGeneration, atOffset, guardAtOffset),
            _ => nowhere,
        };
    }

    /// <summary>
    /// Gives the number of the object whose header starts at <paramref name="offset"/>, read as <see cref="TryParseNumberedAt"/>
    /// reads it for object <paramref name="expected"/> — after white space and comments, in windows read on while the header
    /// runs past them or may have been cut at their edge, up to <see cref="PdfReaderLimits.MaxObjectLength"/> —, with its
    /// generation and where it starts; 0 where no header is; or <see cref="HeaderPastGuard"/> where the header still runs
    /// past the largest window, where a load reaches that guard; or <see cref="HeaderReadingSpent"/> where reading it further
    /// would consume more than <paramref name="reading"/> has left. Nothing is parsed past the header, reported or recorded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It decides as a load does, and reads no more: a first window of <see cref="HeaderCheckWindow"/> bytes, which holds the
    /// headers of real files; then, while the header runs past the last or may have been cut at its edge, windows eight
    /// times larger each, the one a load starts with, <see cref="InitialObjectWindow"/>, taken on the way, then each the
    /// rest of the window a load reads from the offset; past a run of white space it skips, the windows start again from the
    /// first. Each reads on from where the last stopped — the white space it
    /// crossed, the tokens it lexed whole, a comment running on — and reads again only a token its edge may have cut, from
    /// its start; each ends no further than the window a load reads its first bytes in, but one that reads such a token
    /// again. A header is so read in no window reaching past the one a load reads it in, and with no more read than a load
    /// reads to decide it, the small windows aside, and one crossing window more where a crossing stopped at a run another
    /// reading crossed finds a string that runs through it, which is read again from its start. What decides is the first whole token that rules a header out, or the
    /// header's three, which any window long enough reads alike — or, as a load reads it, an <c>obj</c> a load's window ends
    /// on, after the number of another object than <paramref name="expected"/>, which a load takes as it is.
    /// </para>
    /// <para>
    /// What it consumes past the first window is taken from <paramref name="reading"/>: the bytes it read up to its decision,
    /// past the next window where the first does not decide, which consume what lies up to the decision;
    /// past a run it skips, every window it reads, whole. What it skipped it did not
    /// consume, and a window as large as a load's from the offset may lie far past it: so no reading reads much more than it
    /// consumes. A valid file's entries place their headers exactly, and cross no run to remember. The runs of white space crossed before the header's first token are remembered:
    /// an offset inside one, a crossing that reaches one, or a reading between two tokens whose window ends in one, reads on
    /// from its end, where a load would find what it finds, and what it holds is consumed once; a window that lexes reads
    /// through a run, which a string may hold. Inside a comment, which runs to the end of its line, a run is read as any
    /// other bytes: it may hold no line end.
    /// </para>
    /// </remarks>
    private long HeaderAt(long offset, int expected, HeaderReading reading, out long generation, out long start)
    {
        generation = -1;
        start = offset;

        // What a load reads from the offset at most: its largest window, or the rest of the file.
        var reach = Math.Min(offset + _guard.Bound(PdfLimit.Object), _source.Length);

        // How far the reading has consumed: the first window is read free of the pass's allowance.
        var consumed = offset + HeaderCheckWindow;
        var origin = offset;
        var windowSize = (long)HeaderCheckWindow;
        var crossing = true;

        // Whether the reading skipped a run another reading crossed: past it, every window it reads is consumed whole, since
        // what it skipped was not, and a window as large as a load's from the row may lie far past what it read.
        var skipped = false;

        // What the windows before lexed of the header: the tokens read whole — its number, where it starts, its generation —,
        // whether they stopped inside a comment, and whether the window starts at a token its edge may have cut.
        var lexed = 0;
        long number = 0;
        long numberStart = 0;
        long written = 0;
        var inComment = false;
        var readingAgain = false;

        while (true)
        {
            // White space a reading crossed already is not read again, from the offset or on the way: what lies past it
            // decides, read from a first window again, and what it holds was consumed then. Inside a comment, it is the
            // comment's up to a line end it may not hold.
            if (!inComment)
            {
                var past = reading.PastBlanks(origin);

                if (past != origin)
                {
                    origin = past;
                    consumed = Math.Max(consumed, origin);
                    windowSize = HeaderCheckWindow;
                    skipped = true;
                }
            }

            if (origin >= reach)
            {
                // White space up to where a load stops reading: the file's end, or the guard's.
                return reach < _source.Length ? HeaderPastGuard : 0;
            }

            var length = (int)Math.Min(windowSize, reach - origin);

            // A window ends no further than the one a load reads its first bytes in, from the offset, but one that reads
            // again from its start a token the last window's edge may have cut, which that window reached.
            if (!readingAgain)
            {
                var loadEnd = offset + LoadWindowPast(origin - offset);

                if (origin + length > loadEnd)
                {
                    length = (int)(loadEnd - origin);
                }
            }

            // While white space is crossed, a window stops where a run remembered starts: the crossing joins it there. A
            // window that lexes reads on through it: a string may hold it.
            if (crossing && reading.NextRunAfter(origin) is var next && origin + length > next)
            {
                length = (int)(next - origin);
            }

            // A pass that has spent its allowance reads no more than the first window of each header: what is left of it past
            // a run skipped into it, or past white space crossed, decides, or leaves the entry undecided; a token an edge cut
            // is not read again, since no window that ends in the first one reads it further.
            if (origin + length > consumed && reading.Remaining == 0)
            {
                var free = offset + HeaderCheckWindow;

                if (origin >= free || readingAgain)
                {
                    return HeaderReadingSpent;
                }

                length = (int)(free - origin);
            }

            using var window = _source.GetWindow(origin, length);
            var span = window.Memory.Span;

            // A source that gives nothing where its length says bytes lie ends there, as a load reads it: no header.
            if (span.IsEmpty)
            {
                return 0;
            }

            if (crossing)
            {
                // A window of nothing but white space is crossed whole: what decides lies past it, and so does what a load
                // reads in the larger windows it grows to. It never reaches into the run remembered, which it stops at or
                // reads on from.
                var firstToken = span.IndexOfAnyExcept(PdfCharacters.Whitespace);

                if (firstToken < 0)
                {
                    var crossed = origin + span.Length;

                    if (!Consume(reading, ref consumed, crossed))
                    {
                        return HeaderReadingSpent;
                    }

                    // From the offset to here is white space: crossed, or a run an earlier reading crossed. The windows
                    // grow as a lexing window does, up to the most a crossing reads at once.
                    reading.Crossed(offset, crossed);
                    origin = crossed;
                    windowSize = Math.Min(Grown(windowSize), BlankCrossingWindow);
                    continue;
                }

                // The white space the window starts with ends the run crossed: it is consumed, and remembered with the run;
                // the window is lexed from the first token on, as the lexer reads it from the offset.
                // Taken however little remains: the window it lies in may decide, and a deciding window is charged whole.
                var lead = origin + firstToken;
                Consume(reading, ref consumed, lead);
                reading.Crossed(offset, lead);
                crossing = false;
                span = span[firstToken..];
                length -= firstToken;
                origin = lead;
                windowSize = StepAtMost(length);
            }

            // The window ends where the file does, or where a load's largest does: past it a load reads nothing.
            var end = origin + span.Length;
            var cut = span.Length == length && end < _source.Length;
            var atReach = origin + length >= reach;

            // A token the window's edge reaches may go on past it — "0000" may be the start of "00004", "obj" that of
            // "objx" —, but not where white space a reading crossed lies, which ends any token but a string still open,
            // which runs through it; at a load's largest window, a load reaches the guard on it whatever follows.
            var blankPast = !atReach && reading.PastBlanks(end) != end;
            var lineEnd = inComment ? LineEndIn(span) : 0;
            var commentRunsOn = inComment && lineEnd == span.Length;
            var lexer = new PdfLexer(span, lineEnd);
            long resume;

            while (true)
            {
                var before = lexer.Position;
                var token = lexer.Read();

                var open = token.Kind is PdfTokenKind.LiteralString or PdfTokenKind.HexString && token.End - token.Start < token.Text.Length + 2;
                var edge = blankPast && !open ? span.Length + 1 : span.Length;

                if (cut && (token.Kind == PdfTokenKind.EndOfInput || token.End >= edge))
                {
                    // The header may go on past the window. At a load's largest, a load keeps a header it read whole —
                    // "5 0 obj" up to the edge —, and reaches the guard on one it did not; at the end of a smaller one, it
                    // takes such a header of another object than the one it reads as it is, and reads no further.
                    if (atReach || (lexed == 2 && number != expected && token.IsKeyword("obj"u8) && EndsLoadWindow(offset, end)))
                    {
                        Consume(reading, ref consumed, end);

                        if (lexed == 2 && token.IsKeyword("obj"u8))
                        {
                            generation = written;
                            start = numberStart;
                            return number;
                        }

                        return HeaderPastGuard;
                    }

                    if (!Consume(reading, ref consumed, end))
                    {
                        return HeaderReadingSpent;
                    }

                    // Read on from the edge, past the white space and comments up to it, the tokens lexed whole kept; or from
                    // the start of the token the edge may have cut.
                    if (token.Kind == PdfTokenKind.EndOfInput)
                    {
                        resume = end;
                        inComment = commentRunsOn || EndsInComment(span[before..]);
                        readingAgain = false;
                    }
                    else
                    {
                        resume = origin + token.Start;
                        inComment = false;
                        readingAgain = true;
                    }

                    break;
                }

                // A whole token: one no header begins with decides, as the header's three do.
                var whole = lexed switch
                {
                    0 => token.Kind == PdfTokenKind.Integer && token.Integer is > 0 and <= PdfObjectId.MaxNumber,
                    1 => token.Kind == PdfTokenKind.Integer && token.Integer is >= 0 and <= PdfObjectId.MaxGeneration,
                    _ => token.IsKeyword("obj"u8),
                };

                if (!whole || lexed == 2)
                {
                    // Decided: what the reading crossed up to its decision is consumed, however little the pass has left — the
                    // whole window past a run it skipped.
                    Consume(reading, ref consumed, skipped ? end : origin + Math.Min(token.End, span.Length));

                    if (!whole)
                    {
                        return 0;
                    }

                    generation = written;
                    start = numberStart;
                    return number;
                }

                if (lexed == 0)
                {
                    number = token.Integer;
                    numberStart = origin + token.Start;
                }
                else
                {
                    written = token.Integer;
                }

                lexed++;
            }

            // The next window grows from this one — twice what it reads again at least — while it is one of the small ones
            // and ends short of the window a load reads past this one's edge, from the offset; past them, it is the rest of
            // that window, as a load reads none between: after a crossing, a window starts past the offset, and one as
            // large as a load's would read past it.
            var toLoadEnd = offset + LoadWindowPast(end - offset) - resume;
            windowSize = Grown(windowSize);

            if (windowSize < 2L * (end - resume))
            {
                windowSize = Grown(windowSize);
            }

            if (windowSize >= InitialObjectWindow || 2 * windowSize > toLoadEnd)
            {
                windowSize = toLoadEnd;
            }

            origin = resume;
        }
    }

    /// <summary>
    /// Determines whether <paramref name="end"/> is where a window a load reads from <paramref name="offset"/> ends, one of
    /// <see cref="InitialObjectWindow"/> grown eightfold.
    /// </summary>
    private bool EndsLoadWindow(long offset, long end) => LoadWindowPast(end - offset - 1) == end - offset;

    /// <summary>
    /// Gives where the line ends in <paramref name="span"/>, which starts inside a comment: the first carriage return or line
    /// feed, which the lexer reads on from, or the span's length when the comment runs past it.
    /// </summary>
    private static int LineEndIn(ReadOnlySpan<byte> span)
    {
        var lineEnd = span.IndexOfAny((byte)'\r', (byte)'\n');
        return lineEnd < 0 ? span.Length : lineEnd;
    }

    /// <summary>
    /// Determines whether <paramref name="span"/>, white space and comments the lexer skipped to their end, ends inside a
    /// comment: its last byte that is no white space is a comment's, and no line ends after it.
    /// </summary>
    private static bool EndsInComment(ReadOnlySpan<byte> span)
    {
        var last = span.LastIndexOfAnyExcept(PdfCharacters.Whitespace);
        return last >= 0 && span[(last + 1)..].IndexOfAny((byte)'\r', (byte)'\n') < 0;
    }

    /// <summary>
    /// Gives the window <see cref="HeaderAt"/> reads after one of <paramref name="size"/> bytes: eight times larger, the window
    /// a load starts with, <see cref="InitialObjectWindow"/>, taken on the way, so that no window is larger than the one a
    /// load reads the same bytes in.
    /// </summary>
    private static long Grown(long size) => size < InitialObjectWindow ? Math.Min(size * 8, InitialObjectWindow) : size * 8;

    /// <summary>
    /// Gives the window a load reads from an entry's offset that ends past the first <paramref name="length"/> bytes: the
    /// smallest of its windows, <see cref="InitialObjectWindow"/> grown eightfold up to its guard, that is larger. A header
    /// a window ending there cuts is one a load reads in that window at least.
    /// </summary>
    private long LoadWindowPast(long length)
    {
        var bound = _guard.Bound(PdfLimit.Object);
        var window = (long)InitialObjectWindow;

        while (window <= length && window < bound)
        {
            window *= 8;
        }

        return Math.Min(window, bound);
    }

    /// <summary>
    /// Gives the largest window <see cref="Grown"/> steps through, from <see cref="HeaderCheckWindow"/>, that is no larger
    /// than <paramref name="length"/>, or the first: the one after it is larger than <paramref name="length"/>.
    /// </summary>
    private static long StepAtMost(long length)
    {
        var step = (long)HeaderCheckWindow;

        while (Grown(step) <= length)
        {
            step = Grown(step);
        }

        return step;
    }

    /// <summary>
    /// Takes from <paramref name="reading"/> what a header's reading consumed from <paramref name="consumed"/> to
    /// <paramref name="end"/>, and moves <paramref name="consumed"/> there.
    /// </summary>
    /// <returns><see langword="false"/> when the pass had less left, and is now spent.</returns>
    private static bool Consume(HeaderReading reading, ref long consumed, long end)
    {
        if (end <= consumed)
        {
            return true;
        }

        var taken = reading.Consume(end - consumed);
        consumed = end;
        return taken;
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
    /// Determines whether the object <paramref name="reference"/> leads to — through a chain of references too, as
    /// <see cref="PdfReference.Resolve"/> follows it — is one a guard cut, or left out of the index: what it holds may lie
    /// past the cut, or is unknown, and is the guard's until the guard is lifted, rather than a value the file gave
    /// (#224, #263). Call it once the reference is resolved.
    /// </summary>
    /// <param name="reference">The reference, as the file wrote it.</param>
    /// <param name="objects">How many objects its chain is followed through (<see cref="PdfReference.Target"/>).</param>
    internal bool LeftByGuard(PdfReference reference, int objects = PdfReference.MaxChainLength - 1)
    {
        var target = reference.Target(objects).Number;
        return IsCutAtLimit(target) || GetPresence(target) == ObjectPresence.Unindexed;
    }

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
            ? EntryPosition(entry)
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
            ? EntryPosition(entry)
            : -1;

        _diagnostics.Warn(
            PdfDiagnosticCodes.SyntaxDepthExceeded,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Object {id.Number} is reached through more nested objects than the reader will follow, and reads as null."),
            position);
    }

    /// <summary>
    /// Gives where a regular entry places its object, for a report on the object; -1 when that lies outside the file, where
    /// no report is placed (#125): the object, when it was read, was read elsewhere, and its entry is corrected only once it
    /// is.
    /// </summary>
    private long EntryPosition(XRefEntry entry)
    {
        var position = PositionOf(entry);
        return IsInFile(position) ? position : -1;
    }

    /// <inheritdoc/>
    long IPdfStreamDataProvider.SourceLength => _source.Length;

    /// <inheritdoc/>
    PdfStreamData IPdfStreamDataProvider.Create(long absoluteOffset, int length, bool cutByGuard)
    {
        // A declared length is a claim made by the file, so it is clamped to what the file can hold.
        // Without this, "/Length 2147483647" would ask for two gigabytes of memory.
        var available = (int)Math.Clamp(_source.Length - absoluteOffset, 0, int.MaxValue);
        var data = new FileStreamData(this, absoluteOffset, Math.Clamp(length, 0, available), _guard);

        if (cutByGuard)
        {
            data.MarkCutByGuard();
        }

        return data;
    }

    /// <inheritdoc/>
    bool IPdfFileEdge.TokenEndsAt(long absoluteOffset)
    {
        Span<byte> next = stackalloc byte[1];
        return absoluteOffset < 0 || absoluteOffset >= _source.Length || _source.Read(absoluteOffset, next) == 0 ||
            !PdfCharacters.IsRegular(next[0]);
    }

    /// <inheritdoc/>
    long IPdfStreamDataProvider.DataStart(long absoluteKeywordEnd)
    {
        // The end-of-line after "stream" is a carriage return and a line feed, or either alone, as the lexer skips it.
        Span<byte> endOfLine = stackalloc byte[2];
        var read = absoluteKeywordEnd < 0 || absoluteKeywordEnd >= _source.Length
            ? 0
            : _source.Read(absoluteKeywordEnd, endOfLine[..(int)Math.Min(endOfLine.Length, _source.Length - absoluteKeywordEnd)]);
        if (read > 0 && PdfCharacters.IsRegular(endOfLine[0]))
        {
            return -1;
        }

        var skipped = read > 0 && endOfLine[0] == (byte)'\r' ? 1 : 0;

        if (skipped < read && endOfLine[skipped] == (byte)'\n')
        {
            skipped++;
        }

        return absoluteKeywordEnd + skipped;
    }

    /// <inheritdoc/>
    EndObjState? IPdfStreamDataProvider.CheckEndStream(long absoluteOffset)
    {
        // A source is only asked for bytes it has, as GetWindow does: a subclass need not accept an offset
        // or a length that runs past its end.
        if (absoluteOffset < 0 || absoluteOffset >= _source.Length)
        {
            return null;
        }

        // One read decides for all but a stream whose white space runs past it: a few bytes, on the stack.
        Span<byte> tail = stackalloc byte[PdfObjectParser.EndObjLookahead];
        var read = _source.Read(absoluteOffset, tail[..(int)Math.Min(tail.Length, _source.Length - absoluteOffset)]);
        var end = StreamEnds.ReadEnd(tail[..read], absoluteOffset + read >= _source.Length);

        return end.Kind switch
        {
            StreamEndKind.None => null,
            StreamEndKind.EndStream when end.EndObj != EndObjState.Unseen => end.EndObj,
            _ => CheckEndStreamOn(absoluteOffset, end),
        };
    }

    /// <summary>
    /// Reads on, past what one read of <see cref="IPdfStreamDataProvider.CheckEndStream"/> holds, through the white space
    /// and comments that may lead to an <c>endstream</c> at <paramref name="absoluteOffset"/>, or to what follows it: once
    /// for each place, however often its stream is parsed.
    /// </summary>
    /// <param name="absoluteOffset">Where the data ends.</param>
    /// <param name="first">What the first read said: undecided, or an <c>endstream</c> after which it saw nothing.</param>
    private EndObjState? CheckEndStreamOn(long absoluteOffset, StreamEnd first)
    {
        if (_endStreamChecks.TryGetValue(absoluteOffset, out var known))
        {
            return known;
        }

        EndObjState? state;
        var keywordEnd = absoluteOffset + first.KeywordEnd;

        if (first.Kind == StreamEndKind.Undecided)
        {
            var at = NextSignificant(absoluteOffset);
            Span<byte> keyword = stackalloc byte[StreamEnds.EndStreamKeyword.Length + 1];
            var read = at < 0 ? 0 : _source.Read(at, keyword[..(int)Math.Min(keyword.Length, _source.Length - at)]);

            if (!keyword[..read].StartsWith(StreamEnds.EndStreamKeyword))
            {
                // Once the searches have read as much as they may, a run the first read could not see past confirms nothing.
                _endStreamChecks[absoluteOffset] = null;
                return null;
            }

            // "endstreamx" is no keyword, and is what follows the data.
            if (read == keyword.Length && PdfCharacters.IsRegular(keyword[^1]))
            {
                _endStreamChecks[absoluteOffset] = EndObjState.Absent;
                return EndObjState.Absent;
            }

            keywordEnd = at + StreamEnds.EndStreamKeyword.Length;
        }

        var next = NextSignificant(keywordEnd);
        state = next < 0 ? EndObjState.Unseen : IsEndObjKeywordAt(next) ? EndObjState.Present : EndObjState.Absent;
        _endStreamChecks[absoluteOffset] = state;
        return state;
    }

    /// <summary>
    /// Determines whether an <c>endobj</c> keyword, whole, follows the declared length of a stream whose data starts at
    /// <paramref name="dataStart"/>, white space and comments between, a comment only after white space: in the bytes the
    /// parser holds when they say, in the file otherwise.
    /// </summary>
    private bool EndObjFollows(long dataStart, long declaredEnd, ReadOnlySpan<byte> buffered) =>
        (declaredEnd - dataStart <= buffered.Length
            ? StreamEnds.IsEndObjAt(buffered, (int)(declaredEnd - dataStart), reachesEndOfFile: dataStart + buffered.Length >= _source.Length)
            : null) ?? IsEndObjAt(declaredEnd);

    /// <summary>
    /// Determines whether an <c>endobj</c> keyword, whole, follows <paramref name="absoluteOffset"/>, where a stream's declared
    /// length ends, after white space and comments, a comment only after white space (<see cref="StreamEnds.SkipGap"/>):
    /// the end of a stream's object that has no <c>endstream</c>.
    /// </summary>
    private bool IsEndObjAt(long absoluteOffset)
    {
        if (absoluteOffset < 0 || absoluteOffset >= _source.Length)
        {
            return false;
        }

        Span<byte> tail = stackalloc byte[PdfObjectParser.EndObjLookahead];
        var read = _source.Read(absoluteOffset, tail[..(int)Math.Min(tail.Length, _source.Length - absoluteOffset)]);

        if (StreamEnds.IsEndObjAt(tail[..read], 0, absoluteOffset + read >= _source.Length) is { } decided)
        {
            return decided;
        }

        var next = NextSignificant(absoluteOffset);
        return next >= 0 && IsEndObjKeywordAt(next);
    }

    /// <summary>
    /// Gives where the first byte at or after <paramref name="position"/> lies that is neither white space nor part of a
    /// comment, reading the file on as far as they run, or the length of the file when they run to its end; -1 when the
    /// searches for <c>endstream</c> have read as much as they may (<see cref="EndStreamSearchPasses"/>), which these reads
    /// count with, or the source gives less than it holds.
    /// </summary>
    private long NextSignificant(long position)
    {
        var size = PdfObjectParser.EndObjLookahead;
        var inComment = false;

        while (position < _source.Length)
        {
            if (_endStreamSearchBytes >= EndStreamSearchPasses * _source.Length)
            {
                return -1;
            }

            using var window = _source.GetWindow(position, (int)Math.Min(size, _source.Length - position));
            var span = window.Memory.Span;
            _endStreamSearchBytes += span.Length;

            if (span.IsEmpty)
            {
                return -1;
            }

            for (var i = 0; i < span.Length; i++)
            {
                var current = span[i];

                if (inComment)
                {
                    // A comment runs to the end of the line; both line endings occur in the wild.
                    inComment = current is not ((byte)'\r' or (byte)'\n');
                }
                else if (current == (byte)'%')
                {
                    inComment = true;
                }
                else if (!PdfCharacters.IsWhitespace(current))
                {
                    return position + i;
                }
            }

            position += span.Length;
            size = (int)Math.Min(2L * size, EndStreamSearchChunk);
        }

        return _source.Length;
    }

    /// <summary>Determines whether <c>endobj</c>, a whole keyword, starts at <paramref name="position"/>.</summary>
    private bool IsEndObjKeywordAt(long position)
    {
        if (position >= _source.Length)
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[StreamEnds.EndObjKeyword.Length + 1];
        var read = _source.Read(position, bytes[..(int)Math.Min(bytes.Length, _source.Length - position)]);
        return bytes[..read].StartsWith(StreamEnds.EndObjKeyword) &&
            (read == StreamEnds.EndObjKeyword.Length ? position + read >= _source.Length : !PdfCharacters.IsRegular(bytes[^1]));
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
    /// 7.3.8.1) — so do those of the 35,716 streams of the corpus whose length is confirmed, a gap of 0 to 2 bytes
    /// between them, counted over the final index of each of its 401 documents —, and only an invalid stream is searched,
    /// but for a cross-reference stream whose <c>/Length</c> the chain could not read, which is searched with neither
    /// stop the rule for headers makes nor a carriage return trimmed, as <paramref name="deferred"/>: its rows may spell
    /// a header, and end in a carriage return; and for a stream whose <c>/Length</c> a guard cut or left out of the index,
    /// which a valid file may hold, searched as an invalid one is: a header its data spells — an embedded file's does —
    /// leaves the data up to it to the guard (#224). What it reads is the stretch between the stream and the next object,
    /// once for each stream: the result is kept, and a stream parsed again — after the cache let it go, or a rebuild of
    /// the index — is not searched again. What the searches read together is bounded by
    /// <see cref="EndStreamSearchPasses"/>.
    /// </para>
    /// </remarks>
    StreamEndSearch IPdfStreamDataProvider.FindEndStream(
        int number, long absoluteDataStart, long declaredEnd, ReadOnlySpan<byte> buffered, bool deferred)
    {
        var found = _endStreamSearches.TryGetValue((absoluteDataStart, deferred), out var known);

        if (found && known.DeclaredEnd == declaredEnd)
        {
            return known.Search;
        }

        var search = found ? known.Search : SearchEndStreamOnce(number, absoluteDataStart, buffered, deferred);

        // Whether the object's endobj follows the declared length depends on the length the stream is read with now, which a
        // rebuild can change — the /Length an object another definition replaces —: it is read again when that length does,
        // and the search, whose result the bytes and the index as written alone decide, is not.
        if (search.Length is null && search.Searched)
        {
            search = search with
            {
                EndObjAfterDeclared = declaredEnd >= absoluteDataStart && declaredEnd <= (search.NextObject ?? _source.Length) &&
                    EndObjFollows(absoluteDataStart, declaredEnd, buffered),
            };
        }

        _endStreamSearches[(absoluteDataStart, deferred)] = (search, declaredEnd);
        return search;
    }

    /// <summary>
    /// Searches, once for each stream, the data that starts at <paramref name="absoluteDataStart"/> for its first
    /// <c>endstream</c>, and reads what its object's <c>endobj</c> does when none is found; what follows the declared length
    /// is left to <see cref="IPdfStreamDataProvider.FindEndStream"/>.
    /// </summary>
    private StreamEndSearch SearchEndStreamOnce(int number, long absoluteDataStart, ReadOnlySpan<byte> buffered, bool deferred)
    {
        var nextObject = NextObjectStart(number, absoluteDataStart);

        // Once the searches have read as much of the file as they may, the bytes the parser holds are searched alone: they
        // cost no read.
        var readsFile = _endStreamSearchBytes < EndStreamSearchPasses * _source.Length;
        var dataEnd = SearchEndStream(
            absoluteDataStart,
            buffered,
            nextObject ?? _source.Length,
            deferred,
            readsFile,
            out var header,
            out var searched,
            out var endObjSeen,
            out var objectEnd);

        // A header the bytes hold is looked for only before the next object the index places: found, it is the nearer.
        var stop = header >= 0 ? header : nextObject;

        // Past int.MaxValue bytes, no length the reader holds a stream by reaches the endstream found: only a file of more
        // than 2 GB can put one there, and the stream is read as one whose endstream is not found. With none, what the
        // object's endobj does is read once with the search, as long as the searches may read.
        if (dataEnd >= 0 && dataEnd - absoluteDataStart <= int.MaxValue)
        {
            var endObj = ((IPdfStreamDataProvider)this).CheckEndStream(dataEnd) ?? EndObjState.Unseen;
            return new StreamEndSearch((int)(dataEnd - absoluteDataStart), stop, endObj)
            {
                NextObjectFollows = endObj == EndObjState.Absent && nextObject is { } next && EndStreamLeadsTo(dataEnd, next),
            };
        }

        return new StreamEndSearch(null, stop, EndObjState.Unknown, searched)
        {
            EndObjSeen = endObjSeen,
            ObjectEnd = objectEnd,
            HeaderStopped = header >= 0,
        };
    }

    /// <summary>
    /// Determines whether the first token after the <c>endstream</c> that ends data at <paramref name="dataEnd"/>, white space
    /// and comments between, starts at <paramref name="next"/>, as far as the searches may read.
    /// </summary>
    private bool EndStreamLeadsTo(long dataEnd, long next)
    {
        var keyword = NextSignificant(dataEnd);
        return keyword >= 0 && NextSignificant(keyword + StreamEnds.EndStreamKeyword.Length) == next;
    }

    /// <inheritdoc/>
    PdfObject IPdfStreamDataProvider.ResolveLength(PdfReference reference, out ObjectPresence presence)
    {
        var id = reference.Id;

        if (_readingChain)
        {
            // A /Length no section read so far places — or one a chain of references from it leads to, or one a guard cut — is
            // read once the chain is read: the data is found by its endstream meanwhile, and the length checked then (#182).
            var read = ReadThroughWithoutLoading(id);
            presence = read is null ? ObjectPresence.Deferred : ObjectPresence.Defined;

            // A chain longer than the parse resolves a /Length through gives null, as it does once the chain is read.
            return read is null or PdfReference ? PdfNull.Instance : read;
        }

        var reentries = _reentries;
        var value = GetObject(id);

        // A null a guard against re-entry gave says nothing of the object: it could not be read from here.
        presence = value is PdfNull && reentries != _reentries ? ObjectPresence.Unproduced : GetPresence(id.Number);

        // What the object holds, or the one a chain of references from it leads to, is the guard's when a guard cut it or
        // left it out of the index: a value the cut left, null or an integer it parted, is none the file gave (#224).
        if (presence != ObjectPresence.Unindexed)
        {
            // The object is not loaded again unless its value leads further: the chain is resolved first, as the parse
            // resolves it, so that the object it leads to is read when it is asked whether a guard cut it.
            if (value is PdfReference next)
            {
                next.Resolve();
            }

            var target = value is PdfReference ? reference.Target(PdfReference.MaxChainLength).Number : id.Number;
            presence = IsCutAtLimit(target) ? ObjectPresence.Cut
                : target != id.Number && GetPresence(target) == ObjectPresence.Unindexed ? ObjectPresence.Unindexed
                : presence;
        }

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
    /// the end of the file places nothing, nor does a row's negative one, which the index leaves out (#125). The index
    /// keeps its offsets sorted once asked, so that a lookup costs a logarithm of it, however many streams are searched.
    /// The object's own entry is stepped over: the object read, relocated from an entry that missed it, may have that
    /// entry past the start of its data. Its entry in the index the reader reads with is not looked at: that one changes
    /// as objects are read, and the bound would change with it.
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
    /// <param name="dataStart">Where the data starts in the file.</param>
    /// <param name="buffered">The bytes of the data the parser holds, from its start.</param>
    /// <param name="limit">Where the next object the index places starts, or the end of the file.</param>
    /// <param name="deferred">Whether the stream's <c>/Length</c> is one the chain could not read, which no header stops.</param>
    /// <param name="readsFile">Whether the file past the bytes the parser holds may be read.</param>
    /// <param name="header">Where the header that stopped the search starts, or -1.</param>
    /// <param name="searched">Whether the search reached where it stops, rather than the end of what it might read.</param>
    /// <param name="endObjSeen">Whether the bytes searched, before a header that stopped the search, hold an <c>endobj</c>.</param>
    /// <param name="objectEnd">
    /// Where the data ends, when no <c>endstream</c> is found and the search reached where it stops, at the object's own
    /// <c>endobj</c> that only white space and comments separate from there (<see cref="EndObjTail"/>); -1 otherwise.
    /// </param>
    private long SearchEndStream(
        long dataStart,
        ReadOnlySpan<byte> buffered,
        long limit,
        bool deferred,
        bool readsFile,
        out long header,
        out bool searched,
        out bool endObjSeen,
        out long objectEnd)
    {
        Span<byte> carried = stackalloc byte[HeaderText.CarriedLength];
        Span<long> carriedAt = stackalloc long[HeaderText.CarriedLength];
        var text = new HeaderText(carried, carriedAt, dataStart);
        var tail = new EndObjTail(dataStart);
        searched = true;
        objectEnd = -1;

        var inBuffer = (int)Math.Clamp(limit - dataStart, 0, buffered.Length);
        text.Read(buffered[..inBuffer], dataStart);
        var found = StreamEnds.SearchStretch(text, endsRange: dataStart + inBuffer >= limit, deferred, out header);
        endObjSeen = StreamEnds.HoldsEndObj(buffered[..inBuffer], header >= 0 ? (int)(header - dataStart) : inBuffer);
        tail.Read(buffered[..inBuffer], dataStart, header >= 0 ? header : dataStart + inBuffer);

        if (found >= 0 || header >= 0)
        {
            objectEnd = found < 0 ? tail.DataEnd(header) : -1;
            return found;
        }

        var end = Math.Min(limit, _source.Length);

        if (dataStart + buffered.Length >= end)
        {
            objectEnd = tail.DataEnd(end);
            return -1;
        }

        if (!readsFile)
        {
            searched = false;
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
            found = StreamEnds.SearchStretch(text, endsRange: position + span.Length >= end, deferred, out header);
            endObjSeen |= StreamEnds.HoldsEndObj(span, header >= 0 ? (int)(header - position) : span.Length);
            tail.Read(span, position, header >= 0 ? header : position + span.Length);

            if (found >= 0 || header >= 0 || position + span.Length >= end)
            {
                objectEnd = found < 0 ? tail.DataEnd(header >= 0 ? header : end) : -1;
                return found;
            }

            position += Math.Max(1, span.Length - EndStreamSearchOverlap);
            text.CarryTo(position);
            size = Math.Min(2 * size, EndStreamSearchChunk);
        }
    }

    /// <summary>
    /// Closes the reader as its document is disposed: whatever asks it for an object or for a stream's data from now on
    /// is refused with an <see cref="ObjectDisposedException"/> that names the document, and what it kept — the objects
    /// it cached, the object streams it decoded, its index, what it noted of each object it read, the source — is let go,
    /// though a reference the caller holds keeps the reader itself. Closing it again does nothing.
    /// </summary>
    internal void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;

        if (_ownsSource)
        {
            _source.Dispose();
        }

        _source = PdfFileSource.FromMemory(ReadOnlyMemory<byte>.Empty);
        _cache.Clear();
        _cache.TrimExcess();
        _cacheOrder.Clear();
        _cacheOrder.TrimExcess();
        _objectStreams.Clear();
        _objectStreamsHiddenByGuard.Clear();
        _objectStreams.TrimExcess();
        _objectStreamOrder.Clear();
        _objectStreamOrder.TrimExcess();
        _objectStreamBytes = 0;
        _xref.Release();
        _chainIndex = null;
        _cutTrailer = null;

        // What it noted of each object grows with a damaged file, past what the file weighs: nothing reads it once closed.
        _nullWhileDecoding.Clear();
        _nullWhileDecoding.TrimExcess();
        Release(_unproduced);
        Release(_cutAtLimit);
        _endObjMissing.Clear();
        _endObjMissing.TrimExcess();
        _streamLengthFaults.Clear();
        _streamLengthFaults.TrimExcess();
        _endStreamSearches.Clear();
        _endStreamSearches.TrimExcess();
        _endStreamChecks.Clear();
        _endStreamChecks.TrimExcess();
        Release(_lengthFaultsReported);
        Release(_syntaxReported);
        Release(_limitsReached);
        Release(_memberLimitsReached);
        _objectStreamReports.Clear();

        static void Release<T>(HashSet<T> set)
        {
            set.Clear();
            set.TrimExcess();
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
            _cutWhileChainIsRead = null;
            _refusedNumbers = null;
        }

        if (!chainRead)
        {
            // A rebuild reads every object again, and loads none the chain read through.
            _chainValuesCut = null;
            Repair();
            return;
        }

        _structure.ChainRead = true;
        _structure.TrailerRead = Trailer.Count > 0;
        _structure.SizeAsWritten = Trailer.GetRaw(PdfName.Size);
        _structure.RootAsWritten = Trailer.GetRaw(PdfName.Root);
        CheckDeferredLengths();
        ReachChainValuesCut();

        if (!HasUsableRoot())
        {
            _structure.RootUsable = false;
            _structure.RootResolved = _structure.RootAsWritten is PdfReference root ? root.Resolve() : null;
            _structure.RootHiddenByGuard = RootHiddenByGuard();
            _structure.RootCutByGuard = RootCutByGuard();

            // Resolving /Root may itself have rebuilt the index — its object was neither where its entry said nor
            // near it —, and the rebuild looked for the catalog in the rebuilt index. A catalog a guard cut is taken as
            // read: what it lacks may lie past the cut, and another found for it would be the reader's guess; so is one a
            // guard left out of the index, which reads as null until the guard is lifted (#175).
            if (!_repaired && !_structure.RootCutByGuard && !RootLeftOutByGuard())
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
    /// nothing; so is one the data was taken again to, the section's own row placing it (<see cref="RetakeDeferred"/>).
    /// One confirmed by an <c>endstream</c> past it gives data the chain did not read, rows included — the section's row
    /// for the <c>/Length</c> among them, or one that does not stand —: they are not read again, and the report says so.
    /// The stream is not parsed again, and each is reported once.
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

            // The chain is resolved as the parse resolves a /Length, one object further than a reference: the object it
            // leads to is read before it is asked whether a guard cut it, and what it holds is what is judged.
            var resolved = value.Resolve();

            // A /Length a guard cut or left out of the index — or the object a chain of references from it leads to — is
            // unknown, not wrong: the guard's report says why, and what the cut left of it, null or a parted integer, is
            // none the file gave (#224).
            if (LeftByGuard(new PdfReference(deferred.Length, this), PdfReference.MaxChainLength))
            {
                continue;
            }

            if (resolved.AsInteger() is not { } length || length is < 0 or > int.MaxValue)
            {
                var presence = value is PdfNull && reentries != _reentries ? ObjectPresence.Unproduced : GetPresence(number);
                var held = presence switch
                {
                    ObjectPresence.Missing => "which the file lacks",
                    ObjectPresence.Unproduced => "which could not be read",
                    _ => "which holds " + DescribeValue(resolved) + ", not a length",
                };
                message = string.Create(
                    CultureInfo.InvariantCulture,
                    $"The /Length of cross-reference stream {deferred.Stream} names object {number} {generation}, {held}; the chain took its data up to its endstream, after {deferred.Taken} bytes.");
            }
            else if (length != deferred.Taken)
            {
                var stopped = EndStreamAfter(dataStart + deferred.Taken);

                if (((IPdfStreamDataProvider)this).CheckEndStream(dataStart + length) is null)
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
    /// Forgets the objects read through since <see cref="_chainValuesCut"/> held <paramref name="mark"/> of them, which an
    /// object at a place the section may have been moved to, and that is no section, led to: nothing of its reading is kept,
    /// and a value the chain keeps that leads to one of them reads it anew, for it to be loaded once the chain is read.
    /// </summary>
    private void ForgetReadThrough(int mark)
    {
        if (_chainValuesCut is not { } cut)
        {
            return;
        }

        for (var i = mark; i < cut.Count; i++)
        {
            _readWhileChainIsRead?.Remove(cut[i]);
        }

        cut.RemoveRange(mark, cut.Count - mark);
    }

    /// <summary>
    /// Loads, once the chain is read, each object a guard cut that a cross-reference stream's value led to, which the chain
    /// ended at or read through: the guard is reached, and reported, where a load reaches it — and thrown, when the caller
    /// asked for it —, as for a <c>/Length</c> read once the chain is (#263).
    /// </summary>
    private void ReachChainValuesCut()
    {
        if (_chainValuesCut is not { } cut)
        {
            return;
        }

        _chainValuesCut = null;

        foreach (var id in cut)
        {
            GetObject(id);
        }
    }

    /// <summary>
    /// Gives where the <c>endstream</c> keyword starts that the white space and comments at <paramref name="position"/>
    /// lead to, or <paramref name="position"/> itself when none follows them.
    /// </summary>
    private long EndStreamAfter(long position)
    {
        var at = NextSignificant(position);
        Span<byte> keyword = stackalloc byte[StreamEnds.EndStreamKeyword.Length];
        var read = at < 0 || at >= _source.Length ? 0 : _source.Read(at, keyword[..(int)Math.Min(keyword.Length, _source.Length - at)]);
        return keyword[..read].SequenceEqual(StreamEnds.EndStreamKeyword) ? at : position;
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

        if (!HasUsableRoot() && !_repaired && !RootHiddenByGuard())
        {
            Repair();
        }

        reached?.Throw();
    }

    /// <summary>
    /// Determines whether <c>/Root</c> names an object the index lacks because a guard stopped it short: the catalog lies
    /// past the cut, and reads as null until the guard is lifted, as any object there does. A <c>/Root</c> that names an
    /// object the index holds, or none, says the file is damaged.
    /// </summary>
    /// <remarks>
    /// So is a <c>/Root</c> the trailers lack where a guard kept one of them from being read whole: it may lie past the cut, or
    /// in an older trailer the chain stopped short of.
    /// </remarks>
    private bool RootHiddenByGuard() => Trailer.GetRaw(PdfName.Root) switch
    {
        PdfReference => RootLeftOutByGuard(),
        null => _structure.TrailersHiddenByGuard,
        _ => false,
    };

    /// <summary>
    /// Determines whether <c>/Root</c>, resolved — through a chain of references too —, names an object a guard left out of
    /// the index: the catalog, which reads as null until the guard is lifted, rather than looked for among the objects the
    /// index holds — any found there is another object than the one <c>/Root</c> names, which an update may have superseded
    /// (#175).
    /// </summary>
    private bool RootLeftOutByGuard() => Trailer.GetRaw(PdfName.Root) is PdfReference root && HiddenByGuard(root.Target().Number);

    /// <summary>
    /// Determines whether <c>/Root</c>, resolved, names an object a guard cut: it may be the catalog, its <c>/Type</c> or its
    /// <c>/Pages</c> past the cut, and it is taken for the catalog as read, until the guard is lifted, rather than looked
    /// for elsewhere — a search could take a catalog an update superseded, and a rebuild would scan the file for what the
    /// guard's report already names (ADR 34).
    /// </summary>
    /// <remarks>A catalog written in a trailer a guard cut, rather than referred to, is so cut (#175).</remarks>
    private bool RootCutByGuard() => Trailer.GetRaw(PdfName.Root) is PdfReference root
        ? IsCutAtLimit(root.Target().Number)
        : IsWrittenInCutTrailer(PdfName.Root);

    /// <summary>
    /// Merges the trailer of <paramref name="section"/> into the document's, keeping it apart when one of the reader's limits
    /// cut it: the chain ends there, and a value the document's trailer takes from it may lie past the cut in part.
    /// </summary>
    private void MergeSectionTrailer(PdfDictionary trailer, XRefSectionRecord section)
    {
        if (section.TrailerCutByLimit)
        {
            _cutTrailer = trailer;
        }

        _xref.MergeTrailer(trailer);
    }

    /// <summary>
    /// Determines whether the trailer's <paramref name="key"/> is a value written in a trailer one of the reader's limits
    /// cut — a catalog written there rather than referred to —, which it was read only as far as the limit let it be: what
    /// it lacks may lie past the cut. A reference is whole, and the object it names judged as it was read.
    /// </summary>
    internal bool IsWrittenInCutTrailer(PdfName key) =>
        _cutTrailer?.GetRaw(key) is { } value and not PdfReference && ReferenceEquals(Trailer.GetRaw(key), value);

    /// <summary>
    /// Determines whether <paramref name="trailer"/> names an older section of the chain — through <c>/Prev</c>, or a
    /// hybrid table's stream through <c>/XRefStm</c>, which a cross-reference stream's dictionary does not name —, whose
    /// trailer a chain that ends at it leaves unread: an offset in the file the chain has not <paramref name="visited"/>,
    /// read as <see cref="SectionOffset"/> reads it.
    /// </summary>
    private bool NamesOlderSection(PdfDictionary trailer, bool stream, HashSet<long> visited) =>
        NamesSection(trailer.GetRaw(PdfName.Prev), visited) ||
        (!stream && NamesSection(trailer.GetRaw(PdfName.XRefStm), visited));

    /// <summary>
    /// Determines whether <paramref name="value"/>, a <c>/Prev</c> or an <c>/XRefStm</c>, names a section the chain has
    /// not read: an offset in the file it has not <paramref name="visited"/>; or a reference, or a chain of them, that leads
    /// to what the guard that ended the chain hides (<see cref="LeadsPastIndex"/>). One that leads elsewhere reads as a
    /// whole read reads it, rows read first winning.
    /// </summary>
    private bool NamesSection(PdfObject? value, HashSet<long> visited) =>
        LeadsPastIndex(value, out var read) ||
        (read?.AsInteger() is { } offset and >= 0 && IsInFile(PositionOf(offset)) && !visited.Contains(offset));

    /// <summary>
    /// Follows <paramref name="value"/> through a chain of references, each read where a section already read places it,
    /// as many as a resolution follows, and determines whether the chain leads to what a guard hides: an object that runs
    /// past <see cref="PdfReaderLimits.MaxObjectLength"/>, or one whose number the index read so far lacks and whose row may
    /// be among those a guard left unread. <paramref name="read"/> is where the chain ends: null at a link that reads as
    /// nothing, or past the links a resolution follows, where a whole read finds no value either.
    /// </summary>
    private bool LeadsPastIndex(PdfObject? value, out PdfObject? read)
    {
        for (var link = 0; value is PdfReference reference && link < PdfReference.MaxChainLength; link++)
        {
            value = ReadWithoutLoading(reference.Id);

            if (value is null)
            {
                // Object 0 is never read, and a number the chain refused is never placed: no row past a guard changes either.
                var number = reference.Id.Number;
                read = null;
                return number > 0 && _refusedNumbers?.Contains(number) != true &&
                    (_cutWhileChainIsRead?.ContainsKey(reference.Id) == true || (!_xref.TryGet(number, out _) && MayBeLeftUnread(number)));
            }
        }

        read = value is PdfReference ? null : value;
        return false;
    }

    /// <summary>
    /// Determines whether a row a guard left unread in the section just read may number object <paramref name="number"/>:
    /// any row of a table may, and of a cross-reference stream, only those its <c>/Index</c> or <c>/Size</c> declares past
    /// where the guard stopped it.
    /// </summary>
    private bool MayBeLeftUnread(int number)
    {
        if (_rowsLeftUnread is not { } rows)
        {
            return true;
        }

        for (var i = 0; i + 1 < rows.Length; i += 2)
        {
            if (number >= rows[i] && number - rows[i] < rows[i + 1])
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether a guard keeps object <paramref name="number"/> from the reader: it stopped the index short of
    /// the file, and the index lacks the object, or holds it in an object stream it lacks.
    /// </summary>
    private bool HiddenByGuard(int number) =>
        IndexStoppedByGuard &&
        (!_xref.TryGet(number, out var entry) ||
            (entry.Kind == XRefEntryKind.Compressed && !_xref.TryGet(entry.ObjectStreamNumber, out _)));

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

        // What follows %PDF- runs to the white space, the comment or the end of the file that ends it, and names a version
        // only when it is one digit, a period and one digit (ISO 32000-1, 7.5.2): 1.4.5.6, 1.7abc and 14 name none. Text
        // the window cuts names none either, what follows it being unknown; the window bounds what is kept of it.
        var versionStart = index + HeaderMarker.Length;
        var versionEnd = versionStart;
        while (versionEnd < span.Length && !PdfCharacters.IsWhitespace(span[versionEnd]) && span[versionEnd] != (byte)'%')
        {
            versionEnd++;
        }

        var written = span[versionStart..versionEnd];
        _structure.HeaderVersion = System.Text.Encoding.Latin1.GetString(written);
        _structure.HeaderVersionCut = versionEnd == span.Length && span.Length < _source.Length;

        if (written is [>= (byte)'0' and <= (byte)'9', (byte)'.', >= (byte)'0' and <= (byte)'9'] &&
            !_structure.HeaderVersionCut)
        {
            _headerVersion = _structure.HeaderVersion;
        }
    }

    /// <summary>
    /// Works out the version the file declares: its header's, or its catalog's <c>/Version</c> when that is later (ISO
    /// 32000-1, 7.5.2 and 7.7.2); none when the header names none, whatever the catalog says.
    /// </summary>
    /// <remarks>
    /// A catalog's <c>/Version</c> is written as the header's is, one digit, a period and one digit; any other value, or
    /// a catalog the reader cannot read — one in an object stream of an encrypted document —, leaves the header's. The
    /// catalog is the one the document opened with: worked out once, the version does not move as more is read.
    /// </remarks>
    private string? DeclaredVersion()
    {
        if (_headerVersion is not { } header)
        {
            return null;
        }

        return Trailer.GetDictionary(PdfName.Root)?.GetName(PdfName.Version)?.Value is
            [>= '0' and <= '9', '.', >= '0' and <= '9'] catalog && string.CompareOrdinal(catalog, header) > 0
            ? catalog
            : header;
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
                // A section the chain named there may have been read, or not: an /XRefStm that names nothing, named again by
                // a /Prev, is a loop through an offset the chain only named.
                _indexIncomplete = true;
                var looped = PositionOf(offset);
                var read = ReadSectionNamedAt(offset);
                _structure.LoopOffset = looped;
                _structure.LoopWrittenOffset = offset;
                _structure.LoopOffsetRead = read;
                _structure.LoopNamedBy = naming;
                _structure.LoopNamedFrom = namedFrom;
                var inFile = IsInFile(looped);
                _diagnostics.Warn(
                    PdfDiagnosticCodes.XRefChainCycle,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The cross-reference chain loops back on itself: the {naming} of the section at offset {namedFrom} names offset {DescribeOffset(offset)}, {(read ? "which the chain has already read" : inFile ? "which the chain has already named" : "outside the file, which the chain has already named")}."),
                    inFile ? looped : namedFrom);
                break;
            }

            if (++sections > _guard.Bound(PdfLimit.XRefSectionCount))
            {
                // Each incremental save adds a section, so a long chain is a sound file saved often; the newest
                // sections, read first, are the ones that win, and what only the older ones index reads as null until the
                // guard is lifted.
                // Placed where the chain goes on, a position in the file; one named outside the file is placed at what
                // named it, the offset in the message, as a section that is missing is (#125).
                _cutByGuard = true;
                var cut = PositionOf(offset);
                var inFile = IsInFile(cut);

                // An older section the file can hold may give what the trailers read lack; one outside the file gives
                // nothing.
                _structure.TrailersHiddenByGuard |= inFile;
                _structure.ChainCutAt = cut;
                _structure.ChainCutWrittenOffset = offset;
                _structure.ChainCutNamedFrom = namedFrom;
                ReachLimit(
                    PdfLimit.XRefSectionCount,
                    inFile
                        ? string.Create(
                            CultureInfo.InvariantCulture,
                            $"The cross-reference chain has more than {_guard.Bound(PdfLimit.XRefSectionCount):N0} sections; the older ones were not read.")
                        : string.Create(
                            CultureInfo.InvariantCulture,
                            $"The cross-reference chain has more than {_guard.Bound(PdfLimit.XRefSectionCount):N0} sections; the older ones were not read: the chain goes on at offset {DescribeOffset(offset)}, outside the file."),
                    inFile ? cut : namedFrom);
                break;
            }

            var section = new XRefSectionRecord(naming, PositionOf(offset), namedFrom) { WrittenOffset = offset };
            _structure.Add(section);

            if (TryReadXRefSection(section.Offset, section, out var previous, out var hybrid) != XRefSectionState.Read)
            {
                // The first section is where startxref says, and one that is not there leaves no index to
                // complete: the file is scanned whole, which is reported. A later one is named by the section
                // before it.
                if (sections == 1)
                {
                    return false;
                }

                // A section a guard cut is where it was named, and is not looked for elsewhere: what lies near it would
                // stand for it.
                if (section.CutByLimit || !TryRelocateXRefSection(offset, section, out previous, out hybrid))
                {
                    ReportLostSection(section, naming, offset, namedFrom);
                    break;
                }
            }

            _highestRead = Math.Max(_highestRead, section.HighestNumber);

            // A guard kept a trailer from being read whole: one it cut; or, where rows it cut or an index it filled end the
            // chain at this section, the older ones its trailer names, or its own where it was read at a place it may have
            // been moved to, whose trailer is not looked for past the guard. Rows that run to the end of the file, or into
            // anything but a trailer, are the file's to lack one, and a trailer that names no section in the file names no
            // trailer to hide; a cut that leaves every row read, the chain going on, hides none.
            var endedByGuard = _indexFullAt >= 0 || (section.CutByLimit && previous < 0);

            if (section.TrailerCutByLimit ||
                (endedByGuard &&
                    (section.Trailer is { } trailer
                        ? NamesOlderSection(trailer, section.Kind == XRefSectionKind.Stream, visited)
                        : section.State == XRefSectionState.Relocated)))
            {
                _structure.TrailersHiddenByGuard = true;
            }

            if (section.TrailerCutByLimit)
            {
                // A trailer a limit cut may have named an older section past the cut, and given keys the older trailers
                // give otherwise: the chain ends here, and what only the older sections index is the guard's to hide, not
                // the file's to lack.
                _cutByGuard = true;
                previous = -1;
            }

            if (_indexFullAt >= 0)
            {
                // The index takes no more numbers: an older section could only add rows for numbers it lacks, which it
                // has no room for, or for numbers a newer section gave already.
                break;
            }

            if (hybrid >= 0 && visited.Add(hybrid))
            {
                // A hybrid-reference file keeps a classic table for old readers and a stream for the rest: without
                // the stream, the objects it indexes are missing, and the chain goes on through /Prev.
                var cut = _cutByGuard;
                var stream = new XRefSectionRecord("/XRefStm", PositionOf(hybrid), section.Offset)
                {
                    WrittenOffset = hybrid,
                    NamingTrailer = section.Trailer,
                };
                _structure.Add(stream);

                if (TryReadXRefSection(stream.Offset, stream, out _, out _) != XRefSectionState.Read &&
                    (stream.CutByLimit || !TryRelocateXRefSection(hybrid, stream, out _, out _)))
                {
                    ReportLostSection(stream, "/XRefStm", hybrid, section.Offset);
                }

                _highestRead = Math.Max(_highestRead, stream.HighestNumber);

                if (_indexFullAt >= 0 || _cutByGuard != cut)
                {
                    // Filled by the stream, an older section could add nothing; the stream's rows, or the stream, cut by a
                    // limit, an older section's rows would stand for the objects the rows left unread define. Its trailer,
                    // unread, may give what the read ones lack, where the file can hold it and the chain has not read it.
                    _structure.TrailersHiddenByGuard |= previous >= 0 && IsInFile(PositionOf(previous)) && !visited.Contains(previous);
                    break;
                }
            }

            offset = previous;
            naming = "/Prev";
            namedFrom = section.Offset;
        }

        return _xref.Count > 0;
    }

    /// <summary>
    /// Determines whether the chain read a section it named at <paramref name="offset"/>, as the file writes it: where it was
    /// named, or near it.
    /// </summary>
    /// <remarks>Run once, when the chain loops: the sections are at most as many as the chain may read.</remarks>
    private bool ReadSectionNamedAt(long offset)
    {
        foreach (var section in _structure.Sections)
        {
            if (section.WrittenOffset == offset && section.State is XRefSectionState.Read or XRefSectionState.Relocated)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Determines whether <paramref name="position"/>, a position in the file, lies inside it.</summary>
    public bool IsInFile(long position) => position >= 0 && position < _source.Length;

    /// <summary>
    /// Gives the position in the file <paramref name="offset"/>, an offset the file gives, names: the header's own offset
    /// added, as every offset the file gives counts from its header.
    /// </summary>
    /// <remarks>
    /// The offset is tested before anything is added to it, so that none wraps (#125). A negative one, which a table's row
    /// may give, is kept as given: it lies before the file's start, whatever precedes the header. One whose sum a long
    /// cannot hold gives <see cref="long.MaxValue"/>, past the end of any file. Either reads as outside the file wherever a
    /// position is tested, as an offset past its end does, and is treated as one: the section is not there, the entry
    /// places its object nowhere. Not a guard (ADR 34): an offset that names a byte of the file is less than its length,
    /// with the header's offset added too, so no file a reader could read whole reaches either.
    /// </remarks>
    public long PositionOf(long offset) => PositionOf(offset, _headerOffset);

    /// <summary>
    /// Gives the position in the file <paramref name="offset"/> names, <paramref name="headerOffset"/> added, as
    /// <see cref="PositionOf(long)"/> does.
    /// </summary>
    public static long PositionOf(long offset, long headerOffset) =>
        offset < 0 ? offset : offset > long.MaxValue - headerOffset ? long.MaxValue : offset + headerOffset;

    /// <summary>
    /// Gives the position in the file a regular entry places its object at: as <see cref="PositionOf(long)"/> gives it for an
    /// offset a row of the file gives; where the reader found the object's header for one it found, before the header
    /// though it be.
    /// </summary>
    public long PositionOf(XRefEntry entry) => entry.FoundByReader ? entry.Offset + _headerOffset : PositionOf(entry.Offset);

    /// <summary>
    /// Writes the position <paramref name="offset"/>, an offset the file gives, names, as a message names it: exactly, the
    /// header's offset added, though a long cannot hold the sum; a negative offset, which names no position, as given.
    /// </summary>
    public string DescribeOffset(long offset) => DescribeOffset(offset, _headerOffset);

    /// <summary>
    /// Writes the position <paramref name="offset"/> names, <paramref name="headerOffset"/> added, as
    /// <see cref="DescribeOffset(long)"/> does.
    /// </summary>
    /// <remarks>
    /// An unsigned 64-bit sum always holds it: an offset is at most <see cref="long.MaxValue"/>, and the header starts within
    /// the first <see cref="HeaderSearchLength"/> bytes.
    /// </remarks>
    public static string DescribeOffset(long offset, long headerOffset) =>
        offset < 0
            ? offset.ToString(CultureInfo.InvariantCulture)
            : ((ulong)offset + (ulong)headerOffset).ToString(CultureInfo.InvariantCulture);

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

        if (section.CutByLimit)
        {
            _cutByGuard = true;
            _structure.TrailersHiddenByGuard = true;
            return;
        }

        _indexIncomplete = true;

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
        var absolute = PositionOf(offset);
        _diagnostics.Warn(
            PdfDiagnosticCodes.XRefSectionMissing,
            IsInFile(absolute)
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"The cross-reference section {naming} names at offset {absolute} is not there, nor near it; the objects only it indexes are looked for by rebuilding the index.")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"The cross-reference section {naming} names at offset {DescribeOffset(offset)} lies outside the file, which is {_source.Length:N0} bytes long; the objects only it indexes are looked for by rebuilding the index."),
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

        var absolute = PositionOf(offset);

        if (!IsInFile(absolute))
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

            if (TryReadXRefSection(candidate, attempt, out previous, out hybrid, candidate: true) == XRefSectionState.Read)
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
    /// Reads the section at <paramref name="absolute"/> into the index, and what became of it into
    /// <paramref name="section"/>: <see cref="XRefSectionState.Read"/>, <see cref="XRefSectionState.NotFound"/> when
    /// nothing there is a section, or <see cref="XRefSectionState.Malformed"/> when one is and cannot be read.
    /// </summary>
    /// <param name="absolute">
    /// Where the section is looked for, a position in the file: one an offset the chain gives names, or one the reader found,
    /// before the header though it be.
    /// </param>
    /// <param name="section">Receives what became of the section.</param>
    /// <param name="previous">The offset its <c>/Prev</c> gives, or -1.</param>
    /// <param name="hybrid">The offset its <c>/XRefStm</c> gives, or -1.</param>
    /// <param name="candidate">
    /// Whether the position is a place the section may have been moved to rather than one the chain names: an object there
    /// that is no cross-reference stream leaves nothing of its reading.
    /// </param>
    private XRefSectionState TryReadXRefSection(
        long absolute, XRefSectionRecord section, out long previous, out long hybrid, bool candidate = false)
    {
        previous = -1;
        hybrid = -1;
        _rowsLeftUnread = null;

        if (!IsInFile(absolute))
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
                section.State = TryReadClassicTable(absolute, section, out previous, out hybrid, candidate)
                    ? XRefSectionState.Read
                    : XRefSectionState.Malformed;
                return section.State;
            }
        }

        section.State = TryReadXRefStream(absolute, section, out previous, candidate);
        return section.State;
    }

    private bool TryReadClassicTable(long absolute, XRefSectionRecord section, out long previous, out long hybrid, bool candidate)
    {
        previous = -1;
        hybrid = -1;
        var maxWindow = _guard.Bound(PdfLimit.XRefSectionLength);
        var windowSize = Math.Min(XRefWindow, maxWindow);
        var reserved = false;

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

            if (!truncated && !reserved)
            {
                // The rows are read in the window that holds their end, rather than read again in each larger one: until
                // a window holds it, or more rows than the index has room for, the window grows. The index makes room for
                // as many rows as their bytes hold before they are read, once: a subsection's count is the file's claim,
                // and room made for each subsection in turn, or for rows read again, would double the map each time.
                var rows = span[keyword.End..];
                var rowsEnd = rows.IndexOfAnyExcept(TableBytes);
                var held = RowsHeld(rowsEnd < 0 ? rows : rows[..rowsEnd]);

                if (rowsEnd < 0 && canGrow && held <= IndexRoomLeft)
                {
                    windowSize = (int)Math.Min((long)windowSize * 4, maxWindow);
                    continue;
                }

                ReserveIndex(held);
                reserved = true;
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

                    var trailer = ReadTrailer(window.Memory, absolute, lexer.Position, windowFull, section);

                    if (trailer is not null)
                    {
                        MergeSectionTrailer(trailer, section);
                    }

                    // A table an /XRefStm names, where ISO 32000-1 names a cross-reference stream (Table 19), stands in its
                    // place: as a hybrid file's stream's own /Prev, what its trailer names is not read, to name a section
                    // missing or end the chain for a guard, the chain going on through the table that names it.
                    if (trailer is not null && section.NamedBy != "/XRefStm")
                    {
                        previous = SectionOffset(trailer, PdfName.Prev, absolute, out var previousHidden);
                        hybrid = SectionOffset(trailer, PdfName.XRefStm, absolute, out var streamHidden);
                        section.ChainHiddenByGuard = streamHidden ?? previousHidden;
                        section.ChainHiddenAtStream = streamHidden is not null;

                        // A stream a guard hid holds rows newer than the older sections': the chain ends with it, as with a
                        // stream a guard cut, since their rows would stand for the stream's.
                        previous = streamHidden is null ? previous : -1;
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

                if (countToken.Kind != PdfTokenKind.Integer || countToken.Integer < 0 || first is < 0 or > PdfObjectId.MaxNumber)
                {
                    section.Fault ??= string.Create(
                        CultureInfo.InvariantCulture,
                        $"the subsection header at offset {absolute + token.Start} does not give a first object number and a count of rows");
                    return false;
                }

                // Compared by subtraction: a count as large as a long would carry first + count - 1 past what one holds.
                if (countToken.Integer > PdfObjectId.MaxNumber - first + 1)
                {
                    section.Fault ??= string.Create(
                        CultureInfo.InvariantCulture,
                        $"the subsection header at offset {absolute + token.Start} numbers its rows past object {PdfObjectId.MaxNumber}");
                    return false;
                }

                // A count is bounded by what it numbers, and its rows by the window, which MaxXRefSectionLength bounds: the
                // most rows an int counts lie far past any window, and a count past them describes rows that are not read.
                truncated = !ReadSubsection(ref lexer, (int)first, (int)Math.Min(countToken.Integer, int.MaxValue), section, absolute);

                if (_indexFullAt >= 0)
                {
                    // The index takes no more numbers: the rows left can only repeat numbers it holds, and are not read.
                    // The trailer past them still names the catalog, the encryption and the identity of the document.
                    if (!candidate)
                    {
                        ReadTrailerPastRows(absolute + lexer.Position, section);
                    }

                    return true;
                }
            }

            if (!canGrow)
            {
                if (windowFull)
                {
                    _cutByGuard = true;
                    section.CutByLimit = true;
                    ReachLimit(
                        PdfLimit.XRefSectionLength,
                        $"The cross-reference table runs past {PdfLimitGuard.FormatLength(maxWindow)}; only the entries within it were read.",
                        absolute);

                    // The keyword may start within the window's last bytes, which it cut, though not before the table's
                    // own. A place the section may have been moved to is a guess, and its trailer is not looked for past
                    // the guard: each guess would read the file to its end.
                    if (!candidate)
                    {
                        // From the start of the line the cut fell in, outside any comment: a line holds a row, a header, a
                        // comment or the trailer keyword, which the cut may have split. Not before the table's own keyword.
                        var line = span.LastIndexOfAny((byte)'\r', (byte)'\n');
                        ReadTrailerPastRows(
                            Math.Max(absolute + (line >= 0 ? line + 1 : windowSize - TrailerKeyword.Length), absolute + section.Padding + "xref".Length),
                            section);
                    }
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
    /// Finds the trailer of a classic table past the rows the reader left unread from <paramref name="from"/> on — those
    /// <see cref="PdfReaderLimits.MaxXRefSectionLength"/> cut, or those past an index
    /// <see cref="PdfReaderLimits.MaxIndexEntries"/> filled —, and merges it into the document's: the rows are not
    /// indexed, but the trailer names the catalog, the encryption and the identity of the document. The chain goes no
    /// further: an older section's rows would stand for the objects the rows left unread define, or find no room.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rows are searched a piece at a time for the first byte no table holds, so that reaching the trailer costs a
    /// read of them, not memory for them. A comment is skipped, as the lexer skips it. Anything else that is no part of a
    /// table — a damaged row, a stray token — is the section's fault, and the search goes on past it to the trailer
    /// keyword, which a reader that read the rows would reach as well: the trailer names the encryption, which must not be
    /// lost to damage the guard hid. <paramref name="from"/> starts a line, outside any comment.
    /// </para>
    /// <para>
    /// The search ends at the trailer keyword, at a dictionary no keyword introduces, or at the end of the file; the last
    /// two are a missing trailer, as they are where no guard stopped the reader, unless something no table holds came
    /// first, which is then all the section is judged by. The file is read a megabyte at a time, each piece once and every
    /// byte of it looked at before the next is read, however many comments or stray bytes it holds; what starts within a
    /// piece's last bytes, which may run past its end, starts the next. The search is made once a document: the chain
    /// ends at the section it is made for.
    /// </para>
    /// </remarks>
    private void ReadTrailerPastRows(long from, XRefSectionRecord section)
    {
        var position = from;
        var stray = -1L;
        var found = -1L;
        var dictionary = -1L;
        var comment = false;

        while (position < _source.Length && found < 0 && dictionary < 0)
        {
            var start = position;
            var length = (int)Math.Min(ScanChunkSize, _source.Length - start);
            using var window = _source.GetWindow(start, length);
            var span = window.Memory.Span;

            // What starts within the window's last bytes may run past its end: it is looked at again from a window that
            // starts with it, so that each piece of the file is read once.
            var edge = start + length < _source.Length ? span.Length - XRefProbeLength : span.Length;
            var at = 0;

            while (at < span.Length)
            {
                var rest = span[at..];
                var next = comment ? rest.IndexOfAny((byte)'\r', (byte)'\n')
                    : stray < 0 ? rest.IndexOfAnyExcept(TableBytes)
                    : rest.IndexOfAny(TrailerSearchBytes);

                if (next < 0)
                {
                    at = span.Length;
                    break;
                }

                at += next;

                if (comment)
                {
                    comment = false;
                    continue;
                }

                if (at >= edge)
                {
                    break;
                }

                var head = span[at..];

                if (head[0] == (byte)'%')
                {
                    comment = true;
                    at++;
                }
                else if (head.StartsWith("<<"u8))
                {
                    dictionary = start + at;
                    break;
                }
                else if (head[0] == (byte)'t' &&
                    (at > 0 ? !PdfCharacters.IsRegular(span[at - 1]) : StartsToken(start)) &&
                    new PdfLexer(head).Read().IsKeyword(TrailerKeyword))
                {
                    found = start + at;
                    break;
                }
                else
                {
                    if (stray < 0)
                    {
                        stray = start + at;
                        section.Fault ??= string.Create(
                            CultureInfo.InvariantCulture,
                            $"it holds {DescribeToken(new PdfLexer(head).Read())} at offset {stray}, where a subsection or the trailer should start");
                    }

                    at++;
                }
            }

            position = start + at;
        }

        if (found < 0)
        {
            // Rows that end in the file's end, or in a dictionary no keyword introduces, lack their trailer; rows that end in
            // something else are judged by it alone.
            if (stray < 0)
            {
                section.TrailerFault = XRefTrailerFault.Missing;
            }

            section.TrailerPosition = stray >= 0 ? stray : dictionary >= 0 ? dictionary : _source.Length;
            return;
        }

        section.TrailerPosition = found;
        var guards = _guardsReached;
        _ = TryParseNumberedAt(found + TrailerKeyword.Length, DirectObject, PdfLimit.Trailer, out var value, out _, out var syntaxFault);

        if (_guardsReached != guards)
        {
            section.CutByLimit = true;
            section.TrailerCutByLimit = true;
        }

        if (JudgeTrailer(value, syntaxFault, section) is { } trailer)
        {
            MergeSectionTrailer(trailer, section);
        }
    }

    /// <summary>
    /// Determines whether a token can start at <paramref name="position"/>: the byte before it is white space or a
    /// delimiter, so that it is not the end of a longer one. The search for a trailer asks where one of its windows
    /// starts, past the table's own keyword: never the file's first byte.
    /// </summary>
    private bool StartsToken(long position)
    {
        using var window = _source.GetWindow(position - 1, 1);
        return !PdfCharacters.IsRegular(window.Memory.Span[0]);
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
            // The table's window ends the data unless it is full and the file goes on, as the table's reading took it.
            var parser = new PdfObjectParser(window, absolute, this, _pending, this, endsData: !windowFull);
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

        if (_guardsReached != guards)
        {
            section.CutByLimit = true;
            section.TrailerCutByLimit = true;
        }

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

        // Once the diagnostics are full, what is added is counted and dropped, each time it is met, and need not be
        // remembered: the set holds no more than the diagnostics do. A report met again then reads as one the diagnostics
        // drop — its message not built — and is counted again, kept before or not.
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
        for (var i = 0; i < count && _indexFullAt < 0; i++)
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
                AddFromChain(number, XRefEntry.Free, section);
            }
            else if (generationToken.Integer is >= 0 and <= PdfObjectId.MaxGeneration)
            {
                AddFromChain(number, XRefEntry.Regular(offsetToken.Integer, (int)generationToken.Integer), section);
            }
            else
            {
                // The rows after it are still in step: only this one is refused, and its object is the index's to find.
                Refuse(number, section);
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
    /// <para>
    /// The data of such a <c>/Length</c> runs to its first <c>endstream</c>, which the file is searched for past the bytes
    /// the dictionary was read with — up to the next object the sections read so far place, or the end of the file —, as
    /// a stream's whose length is not confirmed, but neither stopped by an object header its rows spell nor trimmed of a
    /// carriage return its last row ends in (#220). With none there, the data ends nowhere the section says, and the
    /// section is malformed. Rows that spell an <c>endstream</c> are told from the data's end by the section's own row for
    /// the <c>/Length</c>, when it lies before them: the data is taken again to the length the object it places gives,
    /// where an <c>endstream</c> confirms it (<see cref="RetakeDeferred"/>). <see cref="PdfReaderLimits.MaxTrailerLength"/>
    /// bounds the dictionary alone.
    /// </para>
    /// </remarks>
    private XRefSectionState TryReadXRefStream(long absolute, XRefSectionRecord section, out long previous, bool candidate)
    {
        previous = -1;

        // The first window, of 64 KB, holds the data of all but the largest cross-reference streams. The dictionary is the
        // trailer, and grows its window no further than a trailer may: the data never counts toward it, and a /Length the
        // chain cannot read has the data searched for its endstream past the window (#174, #220).
        var guards = _guardsReached;
        var readThrough = _chainValuesCut?.Count ?? 0;

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
            if (candidate)
            {
                ForgetReadThrough(readThrough);
            }

            if (!candidate && _guardsReached != guards && value is PdfDictionary { } cut && !cut.ContainsKey(PdfName.Type))
            {
                // A dictionary MaxTrailerLength cut before it said what it is may be the section's: unknown, and the
                // guard's, which says so (ADR 34), rather than a section missing. An array, a dictionary of another type,
                // or a stream whose dictionary was read whole is no cross-reference stream, cut or not.
                section.CutByLimit = true;
                section.Fault = string.Create(
                    CultureInfo.InvariantCulture,
                    $"holds object {number}, whose dictionary one of the reader's limits cut before it declared a cross-reference stream");
                return XRefSectionState.Malformed;
            }

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
        // read, and checked, once the chain is, if the section is read — or before its rows are, where the section places
        // it among them. So is one a chain of references from it leads to, or a guard cut, which the check then passes over.
        var deferred = dictionary.GetRaw(PdfName.Length) is PdfReference length && ReadThroughWithoutLoading(length.Id) is null &&
            !section.CutByLimit
            ? new DeferredLength(length.Id, xrefStream.Data.Length, number)
            : (DeferredLength?)null;

        // Data a search found no endstream for, before the next object the sections read so far place or the end of the
        // file, ends nowhere the section says: its rows are not read (#220). Where the /Length leads to an object a guard cut,
        // the length that would have said is the guard's, as a value its rows are read with is (#263): the section counts as
        // cut, the chain ends here, and the guard is reached once the chain is read.
        if (deferred is not null && _endStreamSearches.TryGetValue((xrefStream.Data.Position, true), out var known) &&
            known.Search is { Length: null } search)
        {
            // The fault names the link of the chain that could not be read — the one no section read so far places, or the
            // one a guard cut —, which the chain read without loading stopped at, as Follow does.
            var lengthFault = Follow(PdfName.Length, dictionary.GetRaw(PdfName.Length), entry: true, out _, out var cutObject, LengthLinks);

            if (cutObject is { } past)
            {
                section.Fault = lengthFault;
                section.CutByLimit = true;
                (_chainValuesCut ??= []).Add(past);
                return XRefSectionState.Malformed;
            }

            var where = search.NextObject is { } next
                ? string.Create(CultureInfo.InvariantCulture, $"before the next object, at {next}")
                : "before the end of the file";
            section.Fault = string.Create(
                CultureInfo.InvariantCulture,
                $"{lengthFault}, and {(search.Searched ? "no endstream follows its data " + where : "its data was not searched for its endstream, the document's searches for endstream having read as much of the file as they may")}");
            return XRefSectionState.Malformed;
        }

        if (UnreadValue(dictionary, out var unreadCut) is { } unread)
        {
            // A value a guard cut is the guard's, as rows a guard cut are: the section counts as cut, the chain ends here,
            // and nothing is rebuilt for it (ADR 34). The guard is reached once the chain is read (#263).
            section.Fault = unread;

            if (unreadCut is { } cut)
            {
                section.CutByLimit = true;
                (_chainValuesCut ??= []).Add(cut);
            }

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

        // Beside an /Index, the rows are read without the /Size: it is left as written (UnreadValue).
        var size = index is null ? dictionary.GetInteger(PdfName.Size) : null;

        // The numbering is checked before any row is read: a section that numbers its rows wrongly gives none to believe.
        // Each value is read once, so that what is read is what was checked.
        if (NumberingFault(written, index, size, out var subsections) is { } numbering)
        {
            section.Fault = numbering;
            return XRefSectionState.Malformed;
        }

        long declared = size.GetValueOrDefault();

        if (index is not null)
        {
            // Each count is within the object numbers, and an /Index holds no more pairs than a trailer: their sum is
            // within a long.
            declared = 0;

            for (var i = 1; i < subsections.Length; i += 2)
            {
                declared += subsections[i];
            }
        }

        // What a decoding reports of data taken again is dropped with it, and may be met again.
        var decoding = deferred is null ? _diagnostics.GetMark() : _diagnostics.GetMarkWithDecodingReports();
        Span<int> fieldWidths = stackalloc int[3];
        ReadOnlySpan<byte> data;
        bool decodedWhole;
        int row;
        long held;
        var retaken = false;

        // Whether a guard cut the /Length the section's own row places, which leaves how far the data runs unknown.
        var lengthCut = false;

        // Whether where the data ends is the guard's to hide, which the data is decoded knowing.
        var endHidden = false;

        while (true)
        {
            var decoded = Filters.PdfFilterPipeline.Decode(xrefStream, _diagnostics, endHidden, out var leftEncoded, out var cut);

            // Data the guard on filters left encoded holds none of the section's rows: the section is cut before its first.
            data = leftEncoded ? default : decoded.Span;
            decodedWhole = !cut && !leftEncoded;

            // A row longer than the decoded data is one the data does not hold. Not a guard (ADR 34): the data's length is
            // bounded by MaxDecodedStreamLength, a guard, and a valid section's rows are in its data. Each width is narrowed
            // once it is known to be within the data, whose length an int holds, and their sum is then within a long.
            long rowLength = 0;

            for (var i = 0; i < 3; i++)
            {
                fieldWidths[i] = (int)Math.Min(declaredWidths[i], data.Length + 1L);
                rowLength += fieldWidths[i];
            }

            row = rowLength <= data.Length ? (int)rowLength : 0;
            held = row == 0 ? 0 : data.Length / row;

            // Fewer rows than the section declares, in data a /Length the chain could not read left to the first endstream:
            // where the section's own row for that /Length, among those held, gives more data, which an endstream confirms,
            // the rows spelled an endstream, and the data is taken again to that length (#220). Data that holds no row whole
            // is asked too: a newer section may place the object, which a guard cut.
            if (!retaken && !endHidden && deferred is { } unsettled && declared > held && decodedWhole)
            {
                if (RetakeDeferred(xrefStream, unsettled, data, fieldWidths, row, held, index is null ? [0, declared] : subsections, out lengthCut) is { } whole)
                {
                    _diagnostics.RollBack(decoding);
                    xrefStream = whole;
                    deferred = unsettled with { Taken = whole.Data.Length };
                    retaken = true;
                    endHidden = lengthCut;
                    continue;
                }
            }

            // Data a /Length the chain could not read left to an endstream nothing confirmed, where the length that would say
            // whether it is the data's own is one a guard cut, or where a guard on decoding kept the data, which lacks rows,
            // from being taken again to the length: where it ends is the guard's, and a filter run out there says nothing of
            // the file. The data is decoded once more knowing it, to the same bytes, whose faults stand (#220, #224).
            if (!endHidden && deferred is not null && !xrefStream.Data.CutByGuard &&
                (lengthCut || (!retaken && !decodedWhole && declared > held)))
            {
                _diagnostics.RollBack(decoding);
                endHidden = true;
                continue;
            }

            break;
        }

        section.CutByLimit |= !decodedWhole;
        var position = 0;

        // A declared count is the file's claim: the index makes room for no more rows than the data holds.
        ReserveIndex(Math.Min(declared, held));

        if (index is null)
        {
            // Rows past the data are not read, so a /Size past an int numbers no row past one: the data holds fewer.
            if (row > 0)
            {
                ReadXRefStreamRows(data, ref position, fieldWidths, row, 0, (int)Math.Min(declared, int.MaxValue), section);
            }
        }
        else
        {
            // A count is bounded by what it numbers, and its rows by the decoded data: the most rows an int counts lie past
            // any data a stream decodes to, and a count past them describes rows that are not read.
            for (var i = 0; i < subsections.Length && row > 0; i += 2)
            {
                ReadXRefStreamRows(data, ref position, fieldWidths, row, (int)subsections[i], (int)Math.Min(subsections[i + 1], int.MaxValue), section);
            }
        }

        // Compared by division: a /Size can be as large as a long, and its product with the row length overflow one.
        var rowsCut = false;

        if (declared > held)
        {
            if (decodedWhole && !lengthCut)
            {
                // Rows the data does not hold, when the reader decoded all of it, are the file's to answer for, and the
                // objects they stood for the index's to find. Every fault met before returned, and a refused row is kept
                // apart: this is the section's first.
                _indexIncomplete = true;
                section.Incomplete = true;
                section.Fault = string.Create(
                    CultureInfo.InvariantCulture,
                    $"it holds {held:N0} {(held == 1 ? "row" : "rows")} where its /Index and /Size declare {declared:N0}");
            }
            else
            {
                // Rows past what MaxDecodedStreamLength let the data decode to are the guard's — or past the endstream the
                // rows spelled, when a guard cut the /Length the section's own row places, which would say how far the data
                // runs —: what they index reads as null until it is lifted, and the chain ends here, since an older
                // section's rows would stand for them.
                _cutByGuard = true;
                section.CutByLimit = true;
                rowsCut = true;
            }
        }

        // Data a guard cut — MaxStreamLength as the data was read, or a guard on decoding it — is the guard's, which says so
        // (ADR 34), and is not checked against a length: the cut kept the data from being taken again to it.
        if (deferred is { } check && decodedWhole)
        {
            (_deferredLengths ??= [])[xrefStream.Data.Position] = check;
        }

        // The rows a guard left unread are the only ones that may yet place an object the dictionary names, before any older
        // section is read: from the row the index had no room for, which the reading passed — every row, for an index full
        // before the first —, up to the last the data holds; or every row past the data a guard cut. Rows that data decoded
        // whole does not hold are none: the file's to lack.
        var stopped = _indexFullAt >= 0 && row > 0 ? Math.Max(0, (position / row) - 1) : long.MaxValue;
        stopped = rowsCut ? Math.Min(stopped, held) : stopped;
        _rowsLeftUnread = RowsFrom(index is null ? [0, declared] : subsections, stopped, rowsCut ? long.MaxValue : held);

        _xref.MergeTrailer(dictionary);

        // A hybrid file's stream gives a /Prev of its own, which the reader does not follow: the chain goes on through the
        // table's, as ISO 32000-1 (7.5.8.4) describes, and the stream's is not read, to name a section missing or end the
        // chain for a guard.
        string? previousHidden = null;
        previous = rowsCut || section.NamedBy == "/XRefStm" ? -1 : SectionOffset(dictionary, PdfName.Prev, absolute, out previousHidden);
        section.ChainHiddenByGuard = previousHidden;
        return XRefSectionState.Read;
    }

    /// <summary>
    /// Gives cross-reference stream <paramref name="stream"/> with its data taken again to the length its <c>/Length</c>
    /// gives, which the chain could not read and took the data up to the first <c>endstream</c> for, when the rows
    /// <paramref name="data"/> holds show that <c>endstream</c> was in the rows (#220): the section's own row for the object
    /// the <c>/Length</c> names, among those held, places it, and the length it gives runs past the data taken, to an
    /// <c>endstream</c> that confirms it. Null otherwise: the length is checked once the chain is read.
    /// </summary>
    /// <remarks>
    /// The section's row for the object is the first that numbers it with a type the reading of its rows takes: one of a
    /// reserved type is ignored there, as the specification says (<see cref="ReadXRefStreamRows"/>). The object is read
    /// where that row places it as <see cref="ReadWithoutLoading"/> reads one: nothing is corrected, cached, recorded or
    /// reported. One a guard cuts gives what the cut left of it, an integer read before the cut only a place to look for
    /// <c>endstream</c> at (#224), and <paramref name="lengthCut"/> says so, whether an <c>endstream</c> confirms it or not:
    /// rows the data taken again still lacks are the guard's. A reference it holds, which a cut after it leaves whole, leads
    /// on, as does the <c>/Length</c> when a newer section gave the number: to a length an object read whole holds, the
    /// file's, or to the integer the cut left of an object a guard cut, read where the sections read so far place it, only a
    /// place to look at then (<see cref="TakeAgainThrough"/>). A row that does not stand otherwise — a newer section gave
    /// the number, or refused it; the row lies past those held, places the object in an object stream, or under another
    /// generation — leaves the data as it was taken.
    /// </remarks>
    private PdfStream? RetakeDeferred(
        PdfStream stream,
        DeferredLength deferred,
        ReadOnlySpan<byte> data,
        ReadOnlySpan<int> widths,
        int row,
        long held,
        long[] subsections,
        out bool lengthCut)
    {
        lengthCut = false;
        var number = deferred.Length.Number;

        if (_xref.TryGet(number, out _) || _refusedNumbers?.Contains(number) == true)
        {
            // The object a newer section places, which the chain could not read: a guard that cut it, or the object a chain
            // of references from it leads to, leaves how far the data runs to the integer the cut left, where an endstream
            // confirms it, and unknown otherwise.
            return TakeAgainThrough(stream, deferred, stream.Dictionary.GetRaw(PdfName.Length), LengthLinks, out lengthCut);
        }

        // The rows that number the object, in the order the subsections give them, up to the first of a type the reading
        // takes: their counts sum within a long, as the section's do.
        long ordinal = 0;
        ulong type = 0;
        ulong offset = 0;
        ulong generation = 0;
        var numbered = false;

        for (var i = 0; i < subsections.Length && !numbered; i += 2)
        {
            if (number >= subsections[i] && number - subsections[i] < subsections[i + 1])
            {
                var at = ordinal + (number - subsections[i]);

                if (at >= held)
                {
                    return null;
                }

                // The row lies within the data, whose length an int holds.
                var fields = data.Slice((int)at * row, row);
                var cursor = 0;
                type = widths[0] == 0 ? 1 : ReadField(fields, ref cursor, widths[0]);
                offset = ReadField(fields, ref cursor, widths[1]);
                generation = ReadField(fields, ref cursor, widths[2]);
                numbered = type <= 2;
            }

            ordinal += subsections[i + 1];
        }

        if (!numbered || type != 1 || offset > long.MaxValue || generation != (ulong)deferred.Length.Generation)
        {
            return null;
        }

        if (!TryReadUnrecorded(deferred.Length, PositionOf((long)offset), out var value, out var pastGuard) && !pastGuard)
        {
            return null;
        }

        // The object read holds the chain's second link, which a cut after it leaves whole: the chain goes on as far as the
        // parse resolves a /Length through.
        if (value is PdfReference)
        {
            return TakeAgainThrough(stream, deferred, value, LengthLinks - 1, out lengthCut);
        }

        // Data taken again to an integer a guard's cut left is no length the file gave: rows it still lacks are the guard's.
        lengthCut = pastGuard;
        return TakeAgain(stream, deferred, value);
    }

    /// <summary>
    /// Gives cross-reference stream <paramref name="stream"/> with its data taken again to the length
    /// <paramref name="value"/>, an integer, gives, when it gives more than the data taken and an <c>endstream</c> confirms
    /// it; null otherwise.
    /// </summary>
    private PdfStream? TakeAgain(PdfStream stream, DeferredLength deferred, PdfObject value)
    {
        var dataStart = stream.Data.Position;

        return value.AsInteger() is { } length && length > deferred.Taken && length <= int.MaxValue &&
            length <= _source.Length - dataStart && ((IPdfStreamDataProvider)this).CheckEndStream(dataStart + length) is not null
            ? new PdfStream(stream.Dictionary, ((IPdfStreamDataProvider)this).Create(dataStart, (int)length))
            : null;
    }

    /// <summary>
    /// Gives cross-reference stream <paramref name="stream"/> with its data taken again to the length
    /// <paramref name="written"/> leads to, read through the sections read so far and <paramref name="links"/> references,
    /// as <see cref="TakeAgain"/> takes it; null when the chain reaches no length.
    /// </summary>
    /// <remarks>
    /// An object a guard cut ends the chain at the integer the cut left of it, read once, where a section read so far places
    /// it (#224): only a place to look for <c>endstream</c> at, which <paramref name="lengthCut"/> says, the data taken again
    /// or not — rows it still lacks are the guard's. One whose value the cut left a whole reference is read through, as a load
    /// reads through it (<see cref="ReadWithoutLoading"/>). A chain that reaches no value — an object no section read so far
    /// places, or a reference past the links — leaves the data as it was taken, as a whole read leaves it.
    /// </remarks>
    private PdfStream? TakeAgainThrough(PdfStream stream, DeferredLength deferred, PdfObject? written, int links, out bool lengthCut)
    {
        lengthCut = false;
        var value = written;

        for (var link = 0; value is PdfReference reference; link++)
        {
            if (link == links)
            {
                return null;
            }

            if (ReadWithoutLoading(reference.Id) is { } read)
            {
                value = read;
            }
            else if (_cutWhileChainIsRead?.TryGetValue(reference.Id, out var left) == true)
            {
                value = left;
                lengthCut = true;
            }
            else
            {
                return null;
            }
        }

        return value is null ? null : TakeAgain(stream, deferred, value);
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
    /// <c>/Index</c> and its elements, or <c>/Size</c> when there is no <c>/Index</c>; and what decoding the data reads
    /// before any of it is (<see cref="Filters.FilterChain"/>): <c>/Filter</c> and its elements up to the first that ends
    /// decoding, and for each Flate or LZW step, <c>/DecodeParms</c>, its element at the step's place, LZW's
    /// <c>/EarlyChange</c>, <c>/Predictor</c>, and <c>/Colors</c>, <c>/BitsPerComponent</c> and <c>/Columns</c> when the
    /// predictor is past 1 — each through as many references as <see cref="PdfReference.Resolve"/> follows. A value the
    /// rows are read without — <c>/Type</c>, <c>/Size</c> beside an <c>/Index</c>, an entry no filter reads — leaves the
    /// section readable; the validator reports how it is written.
    /// </remarks>
    /// <param name="dictionary">The cross-reference stream's dictionary.</param>
    /// <param name="cut">The object a guard cut that the value which cannot be read leads to, or null when no guard did.</param>
    private string? UnreadValue(PdfDictionary dictionary, out PdfObjectId? cut)
    {
        if (Unread(dictionary, PdfName.W, out var widths, out cut) is { } fault)
        {
            return fault;
        }

        for (var i = 0; widths is PdfArray w && i < Math.Min(3, w.Count); i++)
        {
            if (Follow(PdfName.W, w[i], entry: false, out _, out cut) is { } elementFault)
            {
                return elementFault;
            }
        }

        if (Unread(dictionary, PdfName.Index, out var index, out cut) is { } indexFault)
        {
            return indexFault;
        }

        if (index is PdfArray ranges)
        {
            foreach (var element in ranges)
            {
                if (Follow(PdfName.Index, element, entry: false, out _, out cut) is { } elementFault)
                {
                    return elementFault;
                }
            }
        }
        else if ((index is null or PdfNull) && Unread(dictionary, PdfName.Size, out _, out cut) is { } sizeFault)
        {
            return sizeFault;
        }

        var following = new Following(this);
        var filtersRead = Filters.FilterChain.TryFollow(dictionary, _guard.Bound(PdfLimit.FilterChain), ref following);
        cut = following.Cut;
        return filtersRead ? null : following.Fault;
    }

    /// <summary>
    /// Follows the values a cross-reference stream's filters are decoded with through what the sections read so far place,
    /// and keeps, as the section's fault, the first that cannot be read.
    /// </summary>
    /// <param name="reader">The reader whose sections place the values.</param>
    private struct Following(PdfFileReader reader) : Filters.IFilterValueReader
    {
        /// <summary>Gets what could not be read, once a value could not be.</summary>
        public string? Fault { get; private set; }

        /// <summary>Gets the object a guard cut that the value which could not be read leads to, if a guard cut one.</summary>
        public PdfObjectId? Cut { get; private set; }

        /// <inheritdoc/>
        public bool TryRead(PdfName key, PdfObject? written, bool entry, out PdfObject? value)
        {
            Fault = reader.Follow(key, written, entry, out value, out var cut);
            Cut = cut;
            return Fault is null;
        }
    }

    /// <summary>
    /// Follows the value <paramref name="key"/> gives in <paramref name="dictionary"/>, and says so when a reference it is,
    /// or leads to, cannot be read.
    /// </summary>
    private string? Unread(PdfDictionary dictionary, PdfName key, out PdfObject? value, out PdfObjectId? cut) =>
        Follow(key, dictionary.GetRaw(key), entry: true, out value, out cut);

    /// <summary>
    /// Follows <paramref name="written"/> through as many references as <see cref="PdfReference.Resolve"/> does, each read
    /// with <see cref="ReadWithoutLoading"/>, and says, as the fault of <paramref name="key"/>, which could not be read, or
    /// that the value is still a reference past them; null when the value is reached.
    /// </summary>
    /// <param name="key">The entry of the dictionary that holds the value, which the fault names.</param>
    /// <param name="written">The value as the file wrote it.</param>
    /// <param name="entry">Whether <paramref name="written"/> is the entry's own value, rather than an element of it.</param>
    /// <param name="value">The value it leads to, or null when it leads to none.</param>
    /// <param name="cut">The object a guard cut that left the value unread, or null when none did.</param>
    /// <param name="links">
    /// How many references are followed: one more for a value read from the object a reference names and then resolved —
    /// a stream's <c>/Length</c>, a trailer's <c>/Prev</c> or <c>/XRefStm</c> —, as <see cref="LengthLinks"/> says.
    /// </param>
    private string? Follow(
        PdfName key, PdfObject? written, bool entry, out PdfObject? value, out PdfObjectId? cut, int links = MaxReferenceLinks)
    {
        value = written;
        cut = null;

        for (var link = 0; value is PdfReference reference; link++)
        {
            if (link == links)
            {
                value = null;
                return $"its {FileQuote.Name(key)} leads from reference to reference without reaching a value";
            }

            if (ReadWithoutLoading(reference.Id) is not { } read)
            {
                // An object a section read so far places, which runs past MaxObjectLength: the guard's, not the file's.
                var place = "which no section read before it places where it can be read";

                if (_cutWhileChainIsRead?.ContainsKey(reference.Id) == true)
                {
                    cut = reference.Id;
                    place = "which one of the reader's limits cut";
                }

                value = null;
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"its {FileQuote.Name(key)} {(entry && link == 0 ? "is" : "holds")} the reference {reference.Id.Number} {reference.Id.Generation} R, {place}");
            }

            value = read;
        }

        return null;
    }

    /// <summary>
    /// Says what is wrong with how a cross-reference stream numbers its rows — through its <c>/Index</c>, or through its
    /// <c>/Size</c> when it has no <c>/Index</c> —, or gives null when nothing is, with the first number and the count of
    /// each subsection its <c>/Index</c> gives: each first number is an object number, and each count numbers object
    /// numbers alone.
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
    private static string? NumberingFault(PdfObject? written, PdfObject? index, long? size, out long[] subsections)
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

        var checkedSubsections = new long[ranges.Count];

        for (var i = 0; i < ranges.Count; i += 2)
        {
            var start = ranges.Resolved(i).AsInteger();
            var count = ranges.Resolved(i + 1).AsInteger();

            if (start is null || count is null)
            {
                return $"its /Index holds {DescribeValue(ranges[start is null ? i : i + 1])} where an integer belongs";
            }

            if (count < 0)
            {
                return "its /Index gives a subsection a count of rows out of range";
            }

            // The last number is compared by subtraction: a count as large as a long would carry start + count - 1 past what
            // one holds, and the rows past int.MaxValue would be read under negative numbers.
            if (start is < 0 or > PdfObjectId.MaxNumber || count > PdfObjectId.MaxNumber - start + 1)
            {
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"its /Index gives a subsection of {count:N0} {(count == 1 ? "row" : "rows")} from object {start}, outside object numbers 0 to {PdfObjectId.MaxNumber}");
            }

            checkedSubsections[i] = start.Value;
            checkedSubsections[i + 1] = count.Value;
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
    /// are reported missing, to be looked for by rebuilding the index. A negative integer is no offset either. Once a guard
    /// filled the index, though, a reference that leads to what the guard hides (<see cref="LeadsPastIndex"/>) names what is
    /// unknown, and the chain ends there for the guard, which is reported, not for the file (#175). So does one that leads
    /// to an object <see cref="PdfReaderLimits.MaxObjectLength"/> cut, as a cross-reference stream's values do: the guard is
    /// reached once the chain is read (#263).
    /// </remarks>
    /// <param name="trailer">The section's trailer, or its stream's dictionary.</param>
    /// <param name="key">The entry to read, <c>/Prev</c> or <c>/XRefStm</c>.</param>
    /// <param name="section">The offset of the section, which a report names.</param>
    /// <param name="hidden">
    /// What the entry is, when it leads to an object a guard cut, which ends the chain — "its /Prev is the reference 9 0 R,
    /// which one of the reader's limits cut" —; null otherwise.
    /// </param>
    private long SectionOffset(PdfDictionary trailer, PdfName key, long section, out string? hidden)
    {
        hidden = null;

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

        // A reference leading to what a guard that filled the index hides names what is unknown: the chain ends there for the
        // guard, not the file (#175).
        if (_indexFullAt >= 0 && LeadsPastIndex(value, out _))
        {
            return -1;
        }

        // So does one leading to an object a guard cut, whose loading, once the chain is read, reaches the guard (#263). The
        // older sections, and their trailers, are the guard's to hide.
        if (value is PdfReference && Follow(key, value, entry: true, out _, out var cut, LengthLinks) is { } fault && cut is { } past)
        {
            _cutByGuard = true;
            _structure.TrailersHiddenByGuard = true;
            (_chainValuesCut ??= []).Add(past);
            hidden = fault;
            return -1;
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

    /// <summary>
    /// Gives the numbers of the rows <paramref name="subsections"/> declares, as pairs of a first number and a count, from
    /// the one at <paramref name="from"/> in their order up to the one at <paramref name="to"/>, which is not among them.
    /// </summary>
    private static long[] RowsFrom(long[] subsections, long from, long to)
    {
        var rows = new List<long>();
        long ordinal = 0;

        // Each count is within the object numbers, and their sum within a long, as the section's reading found.
        for (var i = 0; i + 1 < subsections.Length; i += 2)
        {
            var count = subsections[i + 1];
            var start = Math.Max(from, ordinal);
            var end = Math.Min(to, ordinal + count);

            if (start < end)
            {
                rows.Add(subsections[i] + (start - ordinal));
                rows.Add(end - start);
            }

            ordinal += count;
        }

        return [.. rows];
    }

    /// <summary>Says what a value written where an offset or an integer belongs is, a real as PDF writes one.</summary>
    private static string DescribeValue(PdfObject value) => value switch
    {
        PdfReference reference => string.Create(
            CultureInfo.InvariantCulture, $"the reference {reference.Id.Number} {reference.Id.Generation} R"),
        PdfInteger integer => string.Create(CultureInfo.InvariantCulture, $"the integer {integer.Value}"),
        PdfReal real => "the real number " + real.ToString(),
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
        // Rows the data does not hold are the caller's to judge, once every subsection is read: the data was cut by a guard,
        // or holds fewer rows than the section declares.
        // Once the index takes no more numbers, the rows left can only repeat numbers it holds: they are not read.
        for (var i = 0; i < count && rowLength <= data.Length - position && _indexFullAt < 0; i++, position += rowLength)
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
                    AddFromChain(number, XRefEntry.Free, section);
                    break;

                case 1 when second <= long.MaxValue && third <= PdfObjectId.MaxGeneration:
                    AddFromChain(number, XRefEntry.Regular((long)second, (int)third), section);
                    break;

                case 2 when second is >= 1 and <= PdfObjectId.MaxNumber && third <= int.MaxValue:
                    AddFromChain(number, XRefEntry.Compressed((int)second, (int)third), section);
                    break;

                case 1 or 2:
                    // The rows after it are still in step: only this one is refused, and its object is the index's to find.
                    Refuse(number, section);
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
    /// Indexes what a row of <paramref name="section"/> says of object <paramref name="number"/>, unless a newer section
    /// said it already or refused its row, or the index has no room left for a number it lacks.
    /// </summary>
    private void AddFromChain(int number, XRefEntry entry, XRefSectionRecord section)
    {
        if (_refusedNumbers?.Contains(number) == true)
        {
            return;
        }

        // While the index has room, a number it holds is simply not taken again: one lookup a row.
        if (IndexHasRoom)
        {
            _xref.TryAdd(number, entry);
        }
        else if (!_xref.TryGet(number, out _))
        {
            ReachIndexBound(section.Offset);
            section.CutByLimit = true;
        }
    }

    /// <summary>
    /// Refuses a row of <paramref name="section"/> for object <paramref name="number"/>: the object is left for a rebuild to
    /// find, and no older section's row stands for it. A row for a number a newer section gave already describes an older
    /// revision, and refusing it changes nothing.
    /// </summary>
    private void Refuse(int number, XRefSectionRecord section)
    {
        _indexIncomplete = true;

        if (_xref.TryGet(number, out _) || _refusedNumbers?.Contains(number) == true)
        {
            return;
        }

        if (IndexHasRoom)
        {
            (_refusedNumbers ??= []).Add(number);
        }
        else
        {
            ReachIndexBound(section.Offset);
            section.CutByLimit = true;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the index has room for one more object number, the numbers the chain refused counted
    /// with it: no number is in both. A row or a header for a number the index holds already takes no room.
    /// </summary>
    private bool IndexHasRoom => IndexRoomLeft > 0;

    /// <summary>Gets how many object numbers more the index has room for, the numbers the chain refused counted with it.</summary>
    private int IndexRoomLeft => _guard.Bound(PdfLimit.IndexEntries) - _xref.Count - (_refusedNumbers?.Count ?? 0);

    /// <summary>
    /// Gets how many rows a classic table's <paramref name="rows"/> hold at most, as the index sizes itself: a line each,
    /// each line of <see cref="ShortRowLength"/> bytes at least. Lines are counted by their ends, a line feed or a carriage
    /// return alone ending one as both together do; a subsection's header is a line too, and is counted as a row.
    /// </summary>
    private static int RowsHeld(ReadOnlySpan<byte> rows) =>
        Math.Min(Math.Max(rows.Count((byte)'\n'), rows.Count((byte)'\r')), rows.Length / ShortRowLength);

    /// <summary>
    /// Makes room in the index for the <paramref name="rows"/> a section is about to give, so that the map is sized once
    /// rather than grown by doubling, and for no more numbers than <see cref="PdfReaderLimits.MaxIndexEntries"/> lets it
    /// take from here on, nor than the newest section's <c>/Size</c> numbers once it is read: one more than the document's
    /// highest object number (ISO 32000-1, 7.5.5), which an older section's rows, superseded, add nothing to. The caller
    /// bounds the rows by those the section holds: a count the file declares is a claim.
    /// </summary>
    /// <remarks>
    /// A chain of many like sections makes room section after section, the map doubling at least each time it must grow.
    /// Once a doubling would bring it within a doubling of what the <c>/Size</c> numbers, room is made for that at once:
    /// the last step lands on the size, rather than past it, and makes room for no more than four times what the map held,
    /// which the rows read paid for.
    /// </remarks>
    private void ReserveIndex(long rows)
    {
        var most = _guard.Bound(PdfLimit.IndexEntries);
        var room = (int)Math.Min(rows, int.MaxValue);

        if (_xref.Trailer.GetInteger(PdfName.Size) is { } size)
        {
            // A size the index holds as many entries as already asks for no room, nor does one below zero, which is not
            // narrowed to an int that would. One the sections read so far number past is stale, and they stand for it.
            most = (int)Math.Min(most, Math.Max(size, _highestRead + 1L));

            if ((long)_xref.Count + room > _xref.Capacity && 4L * _xref.Capacity >= most)
            {
                room = most - _xref.Count;
            }
        }

        _xref.Reserve(room, most);
    }

    /// <summary>
    /// Reports that the index had no room for a number it lacks, met at <paramref name="position"/>: the first time, under
    /// <see cref="PdfReaderLimits.MaxIndexEntries"/>. The index stays full from then on, and is reported once whatever is
    /// met afterward.
    /// </summary>
    /// <exception cref="PdfLimitExceededException">
    /// The guard is reached for the first time, and the document was opened to throw on one.
    /// </exception>
    private void ReachIndexBound(long position)
    {
        _indexFull = true;
        _chainFilledIndex |= !_repaired;

        if (_indexFullAt >= 0)
        {
            return;
        }

        _indexFullAt = position;

        // The file's structure is what the document opened with: where a rebuild run since filled the index is the reader's.
        if (!_opened)
        {
            _structure.IndexFullAt = position;
        }

        ReachLimit(PdfLimit.IndexEntries, IndexFullMessage(), position);
    }

    /// <summary>Says that the index reached <see cref="PdfReaderLimits.MaxIndexEntries"/>.</summary>
    private string IndexFullMessage()
    {
        var bound = _guard.Bound(PdfLimit.IndexEntries);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"The index reached {bound:N0} {(bound == 1 ? "entry" : "entries")}; the objects past them were not indexed.");
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
            // A reference to an object the file does not define is null (ISO 32000-1, 7.3.10): only an index damage
            // may have lost entries from is rebuilt to look for it, once. One a guard cut or filled reads it as null until
            // the guard is lifted (ADR 34): a rebuild would scan the file for what the guard's report already names.
            if (!_repaired && _indexIncomplete)
            {
                Repair();

                if (_xref.TryGet(id.Number, out entry))
                {
                    return LoadFromEntry(id, entry);
                }
            }

            // While a rebuilt index takes in what its object streams hold, the object may be in one not
            // expanded yet: that null is not the object's, and is not kept.
            if (_expandingObjectStreams)
            {
                _reentries++;
            }

            return NotIndexed();
        }

        return LoadFromEntry(id, entry);
    }

    /// <summary>
    /// Answers null for an object the index holds no entry for; or, for a document opened to throw on a guard, throws each
    /// time the index <see cref="PdfReaderLimits.MaxIndexEntries"/> filled leaves one out, as an object read again past
    /// its guard throws again.
    /// </summary>
    /// <exception cref="PdfLimitExceededException">The index is full, and the document was opened to throw on a guard.</exception>
    private PdfNull NotIndexed()
    {
        if (_indexFull && _guard.ThrowsOnLimit)
        {
            _guard.Reach(PdfLimit.IndexEntries, null, IndexFullMessage(), _indexFullAt);
        }

        return PdfNull.Instance;
    }

    private PdfObject LoadFromEntry(PdfObjectId id, XRefEntry entry)
    {
        var loaded = entry.Kind switch
        {
            XRefEntryKind.Regular => LoadRegularObject(id, entry),
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
    /// Loads an object <paramref name="entry"/> places, in at most three attempts: where the index says, in the
    /// neighborhood, and wherever a rebuilt index says.
    /// </summary>
    /// <remarks>
    /// The attempts are counted rather than chained. An earlier version let relocation call back into
    /// loading, and a fuzzed file drove the two into each other until the stack ran out — a file killing
    /// the process is the exact outcome the reader exists to prevent.
    /// </remarks>
    private PdfObject? LoadRegularObject(PdfObjectId id, XRefEntry entry)
    {
        var offset = PositionOf(entry);

        if (!IsInFile(offset))
        {
            // No position in the file names the entry, which the index does not keep where it was read. Only a row of the
            // file places an object there: one the reader found is in the file.
            _diagnostics.Warn(
                PdfDiagnosticCodes.XRefEntryOutOfRange,
                string.Create(
                    CultureInfo.InvariantCulture, $"The entry of object {id.Number} places it at offset {DescribeOffset(entry.Offset)}, outside the file."));
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
            _xref.Set(id.Number, XRefEntry.Found(nearby - _headerOffset, generation));
            return relocated;
        }

        if (_repaired)
        {
            return null;
        }

        Repair();

        return _xref.TryGet(id.Number, out var rebuilt) &&
               rebuilt.Kind == XRefEntryKind.Regular &&
               TryParseObjectAt(id.Number, PositionOf(rebuilt), out var afterRebuild)
            ? afterRebuild
            : null;
    }

    /// <summary>
    /// Parses object <paramref name="number"/> at an exact offset, growing the window while the object runs
    /// past its end. Returns false when nothing with that number is there.
    /// </summary>
    private bool TryParseObjectAt(int number, long offset, out PdfObject value, long recordedAt = RecordedAnywhere) =>
        TryParseNumberedAt(offset, number, PdfLimit.Object, out value, out _, out _, recordedAt: recordedAt);

    /// <summary>
    /// Parses what starts at an exact offset — object <paramref name="number"/>, any object for
    /// <see cref="AnyObject"/>, or a direct object for <see cref="DirectObject"/> —, growing the window while it
    /// runs past its end, up to the bound of <paramref name="limit"/>, and gives the number of the object found there —
    /// 0 for a direct object. Returns false when nothing with the expected number is there.
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
    /// <param name="recordedAt">
    /// Where the object's header must start for what the reading notes of it by its number — its <c>endobj</c>, its stream's
    /// length, a cut — to be noted: <see cref="RecordedAnywhere"/>, <see cref="RecordedNowhere"/>, or the place of the
    /// definition the reader serves, which a reading where the chain places the object may not be (#255).
    /// </param>
    private bool TryParseNumberedAt(
        long offset,
        int number,
        PdfLimit limit,
        out PdfObject value,
        out int foundNumber,
        out bool syntaxFault,
        int initialWindow = InitialObjectWindow,
        Func<PdfObject, bool>? keep = null,
        long recordedAt = RecordedAnywhere)
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

                    // The guard is reported first, so that what the parse met before the cut cannot crowd it out of full
                    // diagnostics: it is the one report that names the property to raise. What the parser met before the cut
                    // is the file's, and is kept after it; it reported nothing of the token the window's edge cut, which is
                    // the guard's (ADR 34), and what it found of a stream is dropped with the cut.
                    syntaxFault = SyntaxFaultSince(_pending, mark);
                    ReachLimit(limit, LimitSubject(number, maxWindow), offset);
                    MarkCutByGuard(parsed, offset + window.Length);

                    // What the chain reads of a cross-reference stream is the section's, which records its cut: the object of that
                    // number a load reads is noted when it is read (#175).
                    if (foundNumber > 0 && number != AnyObject && Records(recordedAt, offset + parser.HeaderStart))
                    {
                        _cutAtLimit.Add(foundNumber);
                    }

                    KeepPending(mark, syntaxOnly: true);

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

                // A reading of a definition the reader no longer serves notes nothing of the number (#255).
                var noted = Records(recordedAt, offset + parser.HeaderStart);
                RecordEndObj(noted ? number : DirectObject, foundNumber, parser.EndObj, cut, offset);
                RecordStreamLength(noted ? number : DirectObject, foundNumber, parsed, lengthFault);
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
    /// Parses object <paramref name="id"/> at an exact offset, as <see cref="TryParseNumberedAt"/> does, but keeps nothing of the
    /// reading: what the parser notices is dropped, nothing is recorded, and an object that still runs past the window
    /// once <see cref="PdfReaderLimits.MaxObjectLength"/> allows no larger one is not read rather than reported, which
    /// <paramref name="pastGuard"/> says, <paramref name="value"/> then giving what the cut left of it. A stream is not
    /// read: whatever its data, the method returns false.
    /// </summary>
    private bool TryReadUnrecorded(PdfObjectId id, long offset, out PdfObject value, out bool pastGuard)
    {
        value = PdfNull.Instance;
        pastGuard = false;

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
                // The file says where a token the window's edge reaches ends, as it does to a load; no stream's data is read.
                var parser = new PdfObjectParser(window.Memory, offset, this, _pending, endsData: !cut, fileEdge: this);
                var read = parser.TryReadIndirectObject(out var found, out var parsed);

                // A header read whole that names another object, or another generation, is not this object, however far
                // its body runs, the chain reading each object under its reference's own number and generation: no guard
                // hides it. A stream is not read either, wherever its data ends.
                if (read && (found != id || parsed is PdfStream))
                {
                    return false;
                }

                if (parser.IsTruncated && cut)
                {
                    if (windowSize >= maxWindow)
                    {
                        // What the cut left of the object, which a caller may take as a place to look at, never as its value: a
                        // header read whole that names another object, or a stream, returned above.
                        pastGuard = true;
                        value = read ? parsed : PdfNull.Instance;
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
    /// Determines whether a reading of an object whose header starts at <paramref name="headerStart"/> notes what it found
    /// by the object's number, as <paramref name="recordedAt"/> asks.
    /// </summary>
    private static bool Records(long recordedAt, long headerStart) =>
        recordedAt == RecordedAnywhere || recordedAt == headerStart;

    /// <summary>
    /// Gives where a reading of object <paramref name="number"/> where the chain's <paramref name="entry"/> places it notes
    /// what it found by the number: anywhere, when the reader serves that entry; at the header of the definition it serves
    /// instead, read from the file, which may be the same one; nowhere, when it serves the number from an object stream or
    /// not at all (#255).
    /// </summary>
    private long RecordedAtFor(int number, XRefEntry entry)
    {
        if (!_xref.TryGet(number, out var served) || served.Kind != XRefEntryKind.Regular)
        {
            return RecordedNowhere;
        }

        return served == entry ? RecordedAnywhere : PositionOf(served);
    }

    /// <summary>
    /// Records whether <c>endobj</c> follows an object read at <paramref name="offset"/>: it does not when another
    /// token follows it, or when the file ends first — not when the window the reader offered ended first, where
    /// what follows was never seen. A stream whose data runs past the window is seen to its end all the same: the
    /// file is asked what follows its <c>endstream</c> (#55). A reading that finds it, or cannot tell where the object
    /// ends — a stream whose end a guard hides —, takes back what an earlier one recorded: the chain reads a
    /// cross-reference stream whose <c>/Length</c> it cannot read up to the first <c>endstream</c> it holds, which its rows
    /// may spell (#224).
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
        else if (state is EndObjState.Present or EndObjState.Unknown)
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
    /// Marks a stream whose data reaches the edge of the window a guard stopped at — its dictionary ran to that edge, the
    /// data no search can be anchored at past it —: the reader cut it there, and its lost tail is the guard's, not the
    /// file's. A stream whose data starts within the window is found by its <c>endstream</c>, which no window bounds.
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

    /// <summary>
    /// Reaches <see cref="PdfReaderLimits.MaxStreamLength"/> for the stream whose data starts at <paramref name="position"/>,
    /// as <see cref="ReachLimit"/> does: its message is made once, as the stream is reported once, however often its data is
    /// read.
    /// </summary>
    private void ReachStreamLength(long position)
    {
        _guardsReached++;

        if (!_limitsReached.Contains((PdfLimit.StreamLength, position)))
        {
            _guard.Reach(PdfLimit.StreamLength, _diagnostics, _guard.DescribeStreamLengthCut(), position);
            _limitsReached.Add((PdfLimit.StreamLength, position));
        }
    }

    /// <summary>
    /// Reports, once for each member, that <see cref="PdfReaderLimits.MaxObjectLength"/> cut member <paramref name="number"/>
    /// of the object stream whose data starts at <paramref name="position"/>: its members share that place, and each is
    /// reported. A document that throws on a guard throws each time.
    /// </summary>
    private void ReachMemberLimit(int number, string what, long position)
    {
        _guardsReached++;

        if (!_memberLimitsReached.Contains((position, number)))
        {
            _guard.Reach(PdfLimit.Object, _diagnostics, what, position);
            _memberLimitsReached.Add((position, number));
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

        if (approximateOffset < -NearbySearchRadius)
        {
            // Nothing of the file lies within reach of an offset that far before its start, as a negative row gives; the
            // window is not worked out from it, which would wrap (#125).
            return false;
        }

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
    /// Reads object <paramref name="number"/> where an entry of the chain's index places it — at the offset it gives, or
    /// near it —, neither through the index the reader reads with now nor from its cache: what the file's own index gives,
    /// whatever a rebuild made of the number since. Null when nothing of that number is there.
    /// </summary>
    /// <remarks>
    /// It parses as a load parses, and notes what a load notes of the object, as <see cref="ReadObjectStreamHeader"/> does;
    /// it neither relocates the entry nor rebuilds the index. A stream's indirect <c>/Length</c> is still loaded as any
    /// object is.
    /// </remarks>
    /// <param name="number">The object's number.</param>
    /// <param name="entry">The regular entry of the chain's index that places it.</param>
    internal PdfObject? ReadWhereChainPlaces(int number, XRefEntry entry)
    {
        var offset = PositionOf(entry);
        var recordedAt = RecordedAtFor(number, entry);

        return TryParseObjectAt(number, offset, out var value, recordedAt) ||
            (TryFindObjectHeader(number, offset, out var nearby, out _) && TryParseObjectAt(number, nearby, out value, recordedAt))
            ? value
            : null;
    }

    /// <summary>
    /// Reads which objects the object stream <paramref name="number"/> holds, in order, from the header its data
    /// starts with — without keeping the stream, nor what it decodes to: the validation rules read each once, or once more
    /// where the chain's index places some of its objects and a rebuilt index alone others, and
    /// memory follows the largest of them rather than their sum.
    /// </summary>
    /// <param name="number">The object stream's number.</param>
    /// <param name="entry">
    /// The regular entry that places it: the chain's index's, or that of the index the reader serves, for the members only a
    /// rebuild places.
    /// </param>
    /// <param name="numbers">
    /// The object numbers its header lists, in order: all of them when it was read, those listed before the fault when its
    /// header could be read only in part — the objects the reader serves from it —, none otherwise.
    /// </param>
    /// <param name="fault">What is wrong with it, in words, when it could not be read whole and is there.</param>
    /// <param name="streamMembers">
    /// The places in the header, among those of <paramref name="numbers"/>, of the objects written as a stream, which the
    /// reader reads as null (#161): each member is read as the reader reads it, in its window, unless the window holds no
    /// <c>stream</c> at all.
    /// </param>
    internal ObjectStreamHeaderResult ReadObjectStreamHeader(
        int number, XRefEntry entry, out int[] numbers, out string? fault, out int[] streamMembers)
    {
        numbers = [];
        fault = null;
        streamMembers = [];

        var guards = _guardsReached;
        var offset = PositionOf(entry);
        var recordedAt = RecordedAtFor(number, entry);
        var found = TryParseObjectAt(number, offset, out var value, recordedAt) ||
            (TryFindObjectHeader(number, offset, out var nearby, out _) && TryParseObjectAt(number, nearby, out value, recordedAt));

        // A dictionary one of the reader's limits cut is the limit's, not the file's: what it would have said is unknown. A
        // stream's dictionary was read whole, and is the file's; what a limit cut of its data is weighed as it is decoded.
        if (_guardsReached != guards && value is not PdfStream)
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

        // A stream that declares no object is empty, as the reader reads it: neither its /First nor its data is read.
        if (count == 0)
        {
            return ObjectStreamHeaderResult.Read;
        }

        if (count < 0 || first < 0)
        {
            fault = "its /N or its /First is missing or negative";
            return ObjectStreamHeaderResult.Unreadable;
        }

        var data = Filters.PdfFilterPipeline.Decode(stream, _diagnostics, out var leftEncoded, out var cut);

        // Data the guard on filters left encoded holds none of the stream's objects: what its header says is unknown.
        if (leftEncoded)
        {
            return ObjectStreamHeaderResult.CutByLimit;
        }

        if (first > data.Length)
        {
            fault = string.Create(
                CultureInfo.InvariantCulture, $"it decodes to {data.Length} bytes, fewer than its /First of {first}");
            return cut ? ObjectStreamHeaderResult.CutByLimit : ObjectStreamHeaderResult.Unreadable;
        }

        // The reader's own reading of the header: the objects it serves are those the pairs read list (#275).
        var header = ObjectStreamHeader.Read(data.Span, count, (int)first, cut);
        numbers = header.Numbers;
        streamMembers = StreamMembers(number, stream.Data.Position, data, header, (int)first, cut);

        if (header.Fault == ObjectStreamHeaderFault.None)
        {
            return ObjectStreamHeaderResult.Read;
        }

        fault = header.Fault switch
        {
            ObjectStreamHeaderFault.Ended => string.Create(
                CultureInfo.InvariantCulture, $"its header lists {header.Count} of the {count} objects its /N declares, then ends"),
            ObjectStreamHeaderFault.Stray => string.Create(
                CultureInfo.InvariantCulture, $"its header lists {header.Count} of the {count} objects its /N declares, then something else"),
            _ => string.Create(
                CultureInfo.InvariantCulture,
                $"its header lists {header.Count} of the {count} objects its /N declares, then object {header.FaultNumber} at offset {header.FaultOffset}, {header.Why(data.Length)}"),
        };
        return ObjectStreamHeaderResult.Unreadable;
    }

    /// <summary>
    /// Gives the places in <paramref name="header"/> of the members of object stream <paramref name="number"/> written as a
    /// stream, each read as a load reads it, in its window: one whose window holds no <c>stream</c> is none. What the
    /// parses meet is dropped.
    /// </summary>
    private int[] StreamMembers(int number, long dataStart, ReadOnlyMemory<byte> data, ObjectStreamHeader header, int first, bool cut)
    {
        List<int>? found = null;
        var bound = _guard.Bound(PdfLimit.Object);
        var mark = _pending.GetMark();

        try
        {
            for (var index = 0; index < header.Count; index++)
            {
                // A member past data a guard cut is unread.
                if ((long)first + header.Offsets[index] >= data.Length)
                {
                    continue;
                }

                var window = ObjectStreamMemberWindow.Of(header.Offsets, index, first, data.Length, cut, bound);

                if (data.Span[window.Start..window.End].IndexOf("stream"u8) < 0)
                {
                    continue;
                }

                var parser = PdfObjectParser.ForObjectStreamMember(
                    data[..window.End], number, dataStart, header.Numbers[index], this, _pending, window.EndsData, window.EndsAtNextMember);
                parser.Position = window.Start;
                _ = parser.ParseObject();

                if (parser.IsMemberStream)
                {
                    (found ??= []).Add(index);
                }
            }
        }
        finally
        {
            _pending.RollBack(mark);
        }

        return found is null ? [] : [.. found];
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
            if (notYet)
            {
                return PdfNull.Instance;
            }

            // One a guard left unable to serve its objects leaves the object unread for the guard's reasons, which its
            // report names: the object is one a guard cut, read as far as it was — not at all (#259).
            if (_objectStreamsHiddenByGuard.Contains(entry.ObjectStreamNumber))
            {
                _cutAtLimit.Add(id.Number);
            }

            return null;
        }

        var mark = _pending.GetMark();

        try
        {
            var bound = _guard.Bound(PdfLimit.Object);
            var value = contents.Parse(
                entry.IndexInObjectStream, id.Number, this, _diagnostics, _pending, bound, out var cut, out var pastLength, out var streamAt);

            // A member written as a stream reads as null, what parsing met of it dropped, and is reported once (#161).
            if (streamAt >= 0)
            {
                if (_objectStreamReports.IsFirstStreamMember(contents.DataStart, id.Number))
                {
                    _diagnostics.Warn(
                        PdfDiagnosticCodes.ObjectStreamMemberStream,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Object {id.Number}, at byte {streamAt} of object stream {entry.ObjectStreamNumber}'s decoded data, is written as a stream, which an object stream cannot hold; it reads as null."),
                        contents.DataStart);
                }

                return null;
            }

            if (pastLength >= 0)
            {
                ReachMemberLimit(
                    id.Number,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Object {id.Number}, at byte {pastLength} of object stream {entry.ObjectStreamNumber}'s decoded data, runs past {PdfLimitGuard.FormatLength(bound)}; only what lies within it was read."),
                    contents.DataStart);
            }

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
            var repaired = _repaired;
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
            ObjectStreamContents? contents = null;
            bool hidden;

            // A stream whose dictionary was read whole is one a guard hid only as its decoding says; one a guard cut short
            // of its stream, before or inside its dictionary, is hidden by that guard.
            if (loaded is PdfStream stream)
            {
                contents = ObjectStreamContents.TryCreate(number, stream, _diagnostics, _objectStreamReports, out hidden);
            }
            else
            {
                hidden = IsCutAtLimit(number);
            }

            if (contents is null && hidden)
            {
                _objectStreamsHiddenByGuard.Add(number);
            }

            // A stream read through an index its own load had rebuilt is decoded again when next asked for, from the
            // rebuilt index's definition.
            if (repaired == _repaired)
            {
                KeepObjectStream(number, contents);
            }

            return contents;
        }
        finally
        {
            _objectStreamsLoading.Remove(number);
        }
    }

    /// <summary>
    /// Keeps an object stream's contents, letting go of the oldest ones kept while what they weigh and its own weight
    /// exceed <see cref="ObjectStreamBudget"/>.
    /// </summary>
    private void KeepObjectStream(int number, ObjectStreamContents? contents)
    {
        if (_objectStreams.TryGetValue(number, out var previous) && previous is not null)
        {
            _objectStreamBytes -= previous.Weight;
        }

        _objectStreams[number] = contents;

        if (contents is null)
        {
            return;
        }

        while (_objectStreamBytes + contents.Weight > ObjectStreamBudget && _objectStreamOrder.TryDequeue(out var oldest))
        {
            // The queue can name a number twice, once replaced; letting it go early costs a decoding, nothing else.
            if (oldest != number && _objectStreams.Remove(oldest, out var released) && released is not null)
            {
                _objectStreamBytes -= released.Weight;
            }
        }

        _objectStreamOrder.Enqueue(number);
        _objectStreamBytes += contents.Weight;
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

        // What a chain a guard cut kept is kept through a rebuild that damage set off: the scan finds every header the file
        // holds, the objects the guard hid included, but not every entry the chain gave — an object stream's members it has
        // not expanded, a header it cannot read.
        var kept = _cutByGuard ? _chainIndex : null;
        _indexFull = false;
        _xref.Clear();
        _cache.Clear();
        _cacheOrder.Clear();
        _objectStreams.Clear();
        _objectStreamsHiddenByGuard.Clear();
        _objectStreamOrder.Clear();
        _objectStreamBytes = 0;
        _nullWhileDecoding.Clear();

        // What was noted of each object as it was read is noted again as each is read from the rebuilt index — every object
        // written directly in the file is, below —: kept, a note of a definition the rebuilt index no longer serves, or of a
        // number it no longer holds, would be read as the note of the one it serves. What could not be produced is noted again
        // as each number is resolved, which every caller of GetPresence does first: cleared, the note is true before that too.
        _unproduced.Clear();
        _cutAtLimit.Clear();
        _endObjMissing.Clear();
        _streamLengthFaults.Clear();

        // A catalog a search took is the reader's guess, which the trailer's /Root holds in place of the file's own: the
        // rebuilt index is read with the file's, which it may now serve as a catalog — whole, or cut by a guard and taken
        // as read —, and searched again only when it does not (#175).
        if (_catalogFoundAs != 0 && Trailer.GetRaw(PdfName.Root) is PdfReference guess && guess.Id.Number == _catalogFoundAs)
        {
            if (_structure.RootAsWritten is { } written)
            {
                Trailer.Set(PdfName.Root, written);
            }
            else
            {
                Trailer.Remove(PdfName.Root);
            }

            _catalogFoundAs = 0;
        }

        var redefinitions = new Redefinitions();

        // Indexing and loading objects can reach a guard. A document opened to throw on one throws once the index is
        // whole, not half-way through rebuilding it: a rebuild is never run twice, and one abandoned mid-way would leave
        // the document unable to find the objects it had not reached. The first guard reached is the one thrown.
        var reached = ScanForObjects(redefinitions);
        ScanForTrailers();

        // A rebuild a load sets off runs with that load, and those it is part of, still in flight: the objects they are
        // loading read as null meanwhile, and so would the values an object stream among them needs, so that expanding the
        // object streams now would expand such a stream without them, or not at all. The loads in flight read through the
        // scan's index, and the rebuild is finished from it once the outermost returns, or throws, as a rebuild run before
        // them would have been: a member is not lost to the load that set the rebuild off, nor read otherwise than had the
        // rebuild run first, and the object streams are expanded once. A guard the scan reached is the first this read
        // reaches: it is thrown now, and the outermost load finishes the index before it lets the exception go.
        if (_loading.Count > 0 || _objectStreamsLoading.Count > 0)
        {
            _unfinishedRebuild = new UnfinishedRebuild(
                new Dictionary<int, XRefEntry>(_xref.Entries), redefinitions, kept, _indexFull, _indexFullAt, MarkReports());
            reached?.Throw();
            return;
        }

        FinishRebuild(redefinitions, kept, reached);
    }

    /// <summary>
    /// Finishes a rebuild from the index its scan made: takes in what the object streams hold, keeps the entries a chain a
    /// guard cut gave that the scan did not find, reports the definitions met again, and looks for a catalog when
    /// <c>/Root</c> leads to none.
    /// </summary>
    /// <exception cref="PdfLimitExceededException">
    /// A guard was reached, by the scan or here, and the document was opened to throw on one: the first reached.
    /// </exception>
    private void FinishRebuild(Redefinitions redefinitions, PdfXRefTable? kept, ExceptionDispatchInfo? reached)
    {
        var expanding = ExpandObjectStreams(redefinitions);
        reached ??= expanding;

        if (kept is not null)
        {
            var merging = MergeChainIndex(kept);
            reached ??= merging;
        }

        var hiding = KeepMembersHiddenByGuard();
        reached ??= hiding;

        if (redefinitions.Count > 0)
        {
            // An updated file defines its changed objects again, so this is what most rebuilds meet: worth knowing,
            // once, rather than a report for each number.
            _diagnostics.Add(PdfDiagnosticSeverity.Information, PdfDiagnosticCodes.ObjectRedefined, redefinitions.Describe());
        }

        // The file's /Root, a search's guess having been set aside as the rebuild began, is read through the rebuilt index;
        // a guard reached reading it is held, as any reached in a rebuild, and it is then a /Root a guard cut.
        bool usable;

        try
        {
            usable = HasUsableRoot();
        }
        catch (PdfLimitExceededException exception)
        {
            reached ??= ExceptionDispatchInfo.Capture(exception);
            usable = true;
        }

        // A /Root a guard cut, or left out of the rebuilt index, is taken as read, and no other is looked for. Searched even
        // when the expansion reached a guard; the first guard reached is the one thrown.
        if (!usable && !RootCutByGuard() && !RootLeftOutByGuard())
        {
            var searching = FindCatalog();
            reached ??= searching;
        }

        reached?.Throw();
    }

    /// <summary>
    /// Finishes the rebuild a load set off, now that no load is in flight: the index is brought back to what the scan
    /// made, what was read meanwhile let go, and the rebuild finished as one run before the load would have been. A finish
    /// that fails — the source failing to give its bytes — leaves the rebuild to be finished again, from the scan, by the
    /// next read.
    /// </summary>
    /// <exception cref="PdfLimitExceededException">
    /// A guard was reached in finishing, and the document was opened to throw on one; the index is finished.
    /// </exception>
    private void FinishUnfinishedRebuild()
    {
        var unfinished = _unfinishedRebuild!;
        _unfinishedRebuild = null;

        // What the loads in flight reported, they read through the scan's index and with each other's objects reading as
        // null: it is withdrawn, and reported again as far as the finished index's own reading reports it, as a rebuild run
        // first does (#273). Once: what is reported after a finish the source failed, before the next, stays.
        if (unfinished.Reports is { } reports)
        {
            WithdrawReportsSince(reports);
        }

        // A finish the source fails withdraws what it reported, which the finish run again reports. Withdrawn, the reports
        // are what the in-flight mark holds, which the withdrawal leaves as it was: it marks the finish too.
        var attempt = unfinished.Reports ?? MarkReports();

        _xref.Clear();

        foreach (var (number, entry) in unfinished.Scanned)
        {
            _xref.Set(number, entry);
        }

        _indexFull = unfinished.IndexFull;
        _indexFullAt = unfinished.IndexFullAt;
        _cache.Clear();
        _cacheOrder.Clear();
        _objectStreams.Clear();
        _objectStreamsHiddenByGuard.Clear();
        _objectStreamOrder.Clear();
        _objectStreamBytes = 0;

        // What read as null while the loads were in flight was theirs, not the file's; and what was noted of each object is
        // noted again as each is read from the finished index.
        _nullWhileDecoding.Clear();
        _unproduced.Clear();
        _cutAtLimit.Clear();
        _endObjMissing.Clear();
        _streamLengthFaults.Clear();

        try
        {
            FinishRebuild(unfinished.Redefinitions.Copy(), unfinished.Kept, reached: null);
        }
        catch (Exception exception) when (exception is not PdfLimitExceededException)
        {
            WithdrawReportsSince(attempt);
            _unfinishedRebuild = unfinished with { Reports = null };
            throw;
        }
    }

    /// <summary>
    /// Finishes the rebuild a load set off while that load's exception leaves it, which stands for the read: a guard the
    /// finish reaches afterward is left, the first guard reached being the one thrown, and a finish the source fails leaves
    /// the rebuild to the next read.
    /// </summary>
    private void FinishUnfinishedRebuildAfterFailure()
    {
        try
        {
            FinishUnfinishedRebuild();
        }
        catch (PdfLimitExceededException)
        {
            // Reached after the read's own exception, which is the one thrown.
        }
        catch (IOException)
        {
            // The rebuild is left unfinished, and finished by the next read.
        }
    }

    /// <summary>
    /// Records where the document's reports stand, and what it holds as reported once, for
    /// <see cref="WithdrawReportsSince"/>. It copies what it records: it is taken when a rebuild is left unfinished, whose
    /// finish it marks too, and as each finish after one the source failed begins: once a document, and once more for each
    /// finish the source fails.
    /// </summary>
    private ReportsMark MarkReports() => new(
        _diagnostics.GetMarkWithDecodingReports(),
        new HashSet<long>(_lengthFaultsReported),
        new HashSet<PdfDiagnostic>(_syntaxReported),
        new HashSet<(PdfLimit Limit, long Position)>(_limitsReached),
        new HashSet<(long DataStart, int Number)>(_memberLimitsReached),
        new HashSet<int>(_objectStreamsNeedingThemselves),
        _nestingReported,
        _objectStreamReports.Copy());

    /// <summary>
    /// Withdraws what the document reported since <paramref name="mark"/>, and forgets that it was reported: what is read
    /// again reports it again.
    /// </summary>
    private void WithdrawReportsSince(ReportsMark mark)
    {
        _diagnostics.RollBack(mark.Diagnostics);
        Restore(_lengthFaultsReported, mark.LengthFaults);
        Restore(_syntaxReported, mark.Syntax);
        Restore(_limitsReached, mark.Limits);
        Restore(_memberLimitsReached, mark.MemberLimits);
        Restore(_objectStreamsNeedingThemselves, mark.NeedingThemselves);
        _nestingReported = mark.NestingReported;
        _objectStreamReports.RestoreFrom(mark.ObjectStreams);
    }

    /// <summary>Brings <paramref name="set"/> back to what <paramref name="copy"/> holds.</summary>
    private static void Restore<T>(HashSet<T> set, HashSet<T> copy)
    {
        set.Clear();
        set.UnionWith(copy);
    }

    /// <summary>
    /// Indexes every object header the file holds, read once each, to its end: the last definition of a number wins, under
    /// the generation its header gives.
    /// </summary>
    /// <remarks>
    /// The index takes object numbers as the file gives them, as many as <see cref="PdfReaderLimits.MaxIndexEntries"/>
    /// lets it; past that, a header for a number it holds still replaces its entry, so that the newest definition wins
    /// whatever came before it, and one for a number it lacks is left out. A number defined again takes no room, so a file
    /// that repeats one header millions of times hides nothing behind them. The scan's time follows the file's length, and
    /// what it keeps the index's bound.
    /// </remarks>
    /// <returns>The exception a document opened to throw asked for, held until the index is whole; or null.</returns>
    private ExceptionDispatchInfo? ScanForObjects(Redefinitions redefinitions)
    {
        var position = 0L;
        ExceptionDispatchInfo? reached = null;

        while (position < _source.Length)
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
                    // the generation its header gives, whatever an earlier definition gave (#118). A number the index
                    // lacks takes room in it, which it may have no more of.
                    var known = _xref.TryGet(number, out _);

                    if (known)
                    {
                        redefinitions.Add(number);
                    }

                    if (known || HasRoomInRebuild(position + headerStart, ref reached))
                    {
                        _xref.Set(number, XRefEntry.Found(position + headerStart - _headerOffset, generation));
                    }
                }

                searchFrom = objPosition + ObjKeyword.Length;
            }

            if (position + length >= _source.Length)
            {
                break;
            }

            position += length - ScanOverlap;
        }

        return reached;
    }

    /// <summary>
    /// Takes into a rebuilt index the entries in use of <paramref name="chain"/>, the index a chain a guard cut gave, for the
    /// numbers the rebuild did not find: what the file defines last, and the scan found, wins.
    /// </summary>
    /// <returns>The exception a document opened to throw asked for, held until the index is whole; or null.</returns>
    private ExceptionDispatchInfo? MergeChainIndex(PdfXRefTable chain)
    {
        ExceptionDispatchInfo? reached = null;

        // A chain a guard cut is kept only when it was read, its first section at least.
        var position = _structure.Sections[0].Offset;

        foreach (var (number, entry) in chain.Entries)
        {
            if (entry.Kind == XRefEntryKind.Free || _xref.TryGet(number, out _))
            {
                continue;
            }

            if (HasRoomInRebuild(position, ref reached))
            {
                _xref.Set(number, entry);
            }
        }

        return reached;
    }

    /// <summary>
    /// Takes into a rebuilt index the members the chain's index places in an object stream a guard keeps from serving them,
    /// for the numbers the rebuild did not find: no header of theirs lies in the file, nor one the stream gives, and they
    /// are unread for the guard's reasons, as before the rebuild — a catalog among them is taken as read (#259).
    /// </summary>
    /// <returns>The exception a document opened to throw asked for, held until the index is whole; or null.</returns>
    private ExceptionDispatchInfo? KeepMembersHiddenByGuard()
    {
        ExceptionDispatchInfo? reached = null;

        if (_objectStreamsHiddenByGuard.Count == 0 || _chainIndex is not { } chain)
        {
            return reached;
        }

        foreach (var (number, entry) in chain.Entries)
        {
            if (entry.Kind != XRefEntryKind.Compressed ||
                !_objectStreamsHiddenByGuard.Contains(entry.ObjectStreamNumber) ||
                _xref.TryGet(number, out _))
            {
                continue;
            }

            var position = _xref.TryGet(entry.ObjectStreamNumber, out var stream) ? PositionOf(stream) : 0;

            if (HasRoomInRebuild(position, ref reached))
            {
                _xref.Set(number, entry);
            }
        }

        return reached;
    }

    /// <summary>
    /// Determines whether a rebuilt index has room for one more object number, met at <paramref name="position"/>; when it
    /// has none, reports <see cref="PdfReaderLimits.MaxIndexEntries"/> the first time, and holds the exception a document
    /// opened to throw asked for in <paramref name="reached"/>, unless one is held already: the rebuild finishes first.
    /// </summary>
    private bool HasRoomInRebuild(long position, ref ExceptionDispatchInfo? reached)
    {
        if (IndexHasRoom)
        {
            return true;
        }

        try
        {
            ReachIndexBound(position);
        }
        catch (PdfLimitExceededException exception)
        {
            reached ??= ExceptionDispatchInfo.Capture(exception);
        }

        return false;
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
            // short is not reported. A window shorter than asked holds the end of the data, as every window does. One the
            // chain read already is not reported again.
            using var window = _source.GetWindow(positions[i], XRefWindow);
            var cut = window.Length == XRefWindow && positions[i] + window.Length < _source.Length;
            var mark = _pending.GetMark();

            try
            {
                var parser = new PdfObjectParser(window.Memory, positions[i], this, _pending, this, endsData: !cut);

                if (parser.ParseObject().AsDictionary() is { } trailer)
                {
                    _xref.MergeTrailer(trailer);

                    if (_rebuiltTrailerPosition < 0)
                    {
                        _rebuiltTrailerPosition = positions[i] - TrailerKeyword.Length;
                    }
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
                var loaded = GetObject(new PdfObjectId(number));

                if (loaded is not PdfStream stream)
                {
                    // An object a guard cut short of a stream may be an object stream the chain's index places members in.
                    if (IsCutAtLimit(number))
                    {
                        _objectStreamsHiddenByGuard.Add(number);
                    }

                    continue;
                }

                if (!stream.Dictionary.IsOfType(PdfName.ObjStm))
                {
                    continue;
                }

                contents = ObjectStreamContents.TryCreate(number, stream, _diagnostics, _objectStreamReports, out var hidden);

                if (contents is null && hidden)
                {
                    _objectStreamsHiddenByGuard.Add(number);
                }
            }
            catch (PdfLimitExceededException exception)
            {
                // A guard reached loading or decoding the object hides it, as one reached without throwing would.
                reached ??= ExceptionDispatchInfo.Capture(exception);
                _objectStreamsHiddenByGuard.Add(number);
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
                // copy listed in the object streams read wins over the others. A member the index lacks takes room in
                // it, which it may have no more of.
                if (_xref.TryGet(contents.NumberAt(index), out _))
                {
                    redefinitions.Add(contents.NumberAt(index));
                }
                else if (HasRoomInRebuild(PositionOf(entry), ref reached))
                {
                    _xref.Set(contents.NumberAt(index), XRefEntry.Compressed(number, index));
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
            bool isCatalog;

            // A /Type written as a reference is read as the candidate is, and may reach a guard as its read may: the search
            // goes on, and the first guard reached is the one thrown once it ends.
            try
            {
                isCatalog = GetObject(new PdfObjectId(number)).AsDictionary() is { } dictionary && dictionary.IsOfType(PdfName.Catalog);
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

            if (isCatalog)
            {
                // The reference names the catalog as its entry does, read once the catalog is — a relocation on the way
                // gives the header's generation —: an entry of 1 1 makes 1 1 R, not 1 0 R (#118).
                var generation = _xref.TryGet(number, out var entry) && entry.Kind == XRefEntryKind.Regular ? entry.Generation : 0;
                Trailer.Set(PdfName.Root, new PdfReference(new PdfObjectId(number, generation), this));
                _catalogFoundAs = number;

                // The file's structure is what the document opened with: the catalog a rebuild run since took is the reader's.
                if (!_opened)
                {
                    _structure.CatalogFoundAs = number;
                }

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

    /// <summary>
    /// A stream's data in the file: where it lies and how long it is, read each time it is asked for, kept by nothing
    /// (#170), and refused once the document is disposed.
    /// </summary>
    /// <remarks>
    /// Streams whose data share a stretch of the file — overlapping objects —, or the many streams of a document read once
    /// each, hold none of it: what a caller keeps is its own, and memory follows what is being decoded. A source in
    /// memory lends the bytes where they lie, without a copy, valid for as long as its memory is; any other reads them
    /// into an array of their length. No more than <see cref="PdfReaderLimits.MaxStreamLength"/> bytes are read: past it,
    /// the data is cut there, marked cut by a guard, and the guard reported, once for each stream.
    /// </remarks>
    private sealed class FileStreamData(PdfFileReader reader, long offset, int length, PdfLimitGuard guard) : PdfStreamData
    {
        private bool _cutByGuard;

        public override int Length => length;

        public override long Position => offset;

        internal override PdfLimitGuard LimitGuard => guard;

        internal override bool CutByGuard => _cutByGuard;

        public void MarkCutByGuard() => _cutByGuard = true;

        /// <exception cref="ObjectDisposedException">The document was disposed.</exception>
        /// <exception cref="Diagnostics.PdfLimitExceededException">
        /// The data runs past <see cref="PdfReaderLimits.MaxStreamLength"/>, and the document was opened to throw when it does.
        /// </exception>
        public override ReadOnlyMemory<byte> GetBytes()
        {
            ObjectDisposedException.ThrowIf(reader._closed, typeof(PdfDocument));

            var count = length;
            var bound = guard.Bound(PdfLimit.StreamLength);

            if (count > bound)
            {
                _cutByGuard = true;
                count = bound;
                reader.ReachStreamLength(offset);
            }

            if (reader._source.TryLend(offset, count, out var lent))
            {
                return lent;
            }

            // A source that gives less than it holds leaves the array longer than what it gave: the bytes it gave are
            // handed back, not copied again.
            var buffer = new byte[count];
            var read = reader._source.Read(offset, buffer);
            return buffer.AsMemory(0, read);
        }
    }

    /// <summary>What a rebuild a load set off needs to be finished once no load is in flight.</summary>
    /// <param name="Scanned">The entries the scan made, before the object streams were expanded.</param>
    /// <param name="Redefinitions">The definitions the scan met again.</param>
    /// <param name="Kept">The entries a chain a guard cut gave, kept through the rebuild; null when no guard cut it.</param>
    /// <param name="IndexFull">Whether the scan filled the index.</param>
    /// <param name="IndexFullAt">Where the index first had no room for a number, or -1.</param>
    /// <param name="Reports">
    /// What the document had reported once the scan was done, which the finish brings its reports back to; null once a
    /// finish has.
    /// </param>
    private sealed record UnfinishedRebuild(
        Dictionary<int, XRefEntry> Scanned,
        Redefinitions Redefinitions,
        PdfXRefTable? Kept,
        bool IndexFull,
        long IndexFullAt,
        ReportsMark? Reports);

    /// <summary>
    /// Where the document's reports stood, and what it held as reported once — a guard, a fault of the syntax, a stream's
    /// length, an object stream's fault —, so that what was reported since can be withdrawn and reported again.
    /// </summary>
    /// <param name="Diagnostics">Where the diagnostics stood, with which of their entries decodings moved in.</param>
    /// <param name="LengthFaults">The streams whose length fault was reported, by where their data starts.</param>
    /// <param name="Syntax">The faults of the syntax reported.</param>
    /// <param name="Limits">The guards reported, and where.</param>
    /// <param name="MemberLimits">The object stream members whose cut was reported.</param>
    /// <param name="NeedingThemselves">The object streams reported as needing an object they hold.</param>
    /// <param name="NestingReported">Whether loads nested too deep were reported.</param>
    /// <param name="ObjectStreams">What the object streams' reports said.</param>
    private sealed record ReportsMark(
        PdfDiagnosticsMark Diagnostics,
        HashSet<long> LengthFaults,
        HashSet<PdfDiagnostic> Syntax,
        HashSet<(PdfLimit Limit, long Position)> Limits,
        HashSet<(long DataStart, int Number)> MemberLimits,
        HashSet<int> NeedingThemselves,
        bool NestingReported,
        ObjectStreamReports ObjectStreams);

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

        /// <summary>
        /// Gives a copy of what was met, which a finish adds to: a finish the source fails is run again from what the scan
        /// met, not from what it added.
        /// </summary>
        public Redefinitions Copy()
        {
            var copy = new Redefinitions { Count = Count, _listed = _listed, _unlisted = _unlisted };
            _numbers.CopyTo(copy._numbers, 0);
            return copy;
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

        /// <summary>
        /// Each object reported at another index of an object stream than its entry gives, with where that stream's data
        /// starts: a stream defined again, which a rebuild may serve, is another place.
        /// </summary>
        private readonly HashSet<(long DataStart, int Number)> _moves = [];

        /// <summary>Each object reported written as a stream in an object stream, with where that stream's data starts.</summary>
        private readonly HashSet<(long DataStart, int Number)> _streamMembers = [];

        /// <summary>Records the fault of the object stream whose data starts at <paramref name="dataStart"/>, and tells whether it is the first.</summary>
        public bool IsFirstFault(long dataStart) => _faults.Add(dataStart);

        /// <summary>Records that object <paramref name="number"/> is elsewhere in the object stream whose data starts at <paramref name="dataStart"/>, and tells whether it is the first time.</summary>
        public bool IsFirstMove(long dataStart, int number) => _moves.Add((dataStart, number));

        /// <summary>Records that object <paramref name="number"/> is written as a stream in the object stream whose data starts at <paramref name="dataStart"/>, and tells whether it is the first time.</summary>
        public bool IsFirstStreamMember(long dataStart, int number) => _streamMembers.Add((dataStart, number));

        /// <summary>Gives a copy of what was reported, which <see cref="RestoreFrom"/> brings back.</summary>
        public ObjectStreamReports Copy()
        {
            var copy = new ObjectStreamReports();
            copy._faults.UnionWith(_faults);
            copy._moves.UnionWith(_moves);
            copy._streamMembers.UnionWith(_streamMembers);
            return copy;
        }

        /// <summary>Brings back what <paramref name="copy"/> held as reported, and forgets what was reported since.</summary>
        public void RestoreFrom(ObjectStreamReports copy)
        {
            Restore(_faults, copy._faults);
            Restore(_moves, copy._moves);
            Restore(_streamMembers, copy._streamMembers);
        }

        /// <summary>Forgets what was reported, for a reader that will report nothing more.</summary>
        public void Clear()
        {
            _faults.Clear();
            _faults.TrimExcess();
            _moves.Clear();
            _moves.TrimExcess();
            _streamMembers.Clear();
            _streamMembers.TrimExcess();
        }
    }

    /// <summary>The objects packed inside one object stream, and where each of them starts.</summary>
    private sealed class ObjectStreamContents : IPdfFileEdge
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
        /// Each number of the header with its place, the number in the upper half, sorted, so that the first place of a
        /// number is found by a binary search; built the first time an entry's index is wrong, so that an index every entry
        /// of the file gets wrong costs a lookup, not a search. <see cref="Weight"/> counts it from the start.
        /// </summary>
        private long[]? _places;

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

        /// <summary>
        /// Determines whether a token that reaches a member window's edge at byte <paramref name="absoluteOffset"/> of the
        /// decoded data ends there: no regular character follows. A window MaxObjectLength bounds ends inside the data; past
        /// the end of data a guard cut there is nothing to ask, and the token stays undecided.
        /// </summary>
        bool IPdfFileEdge.TokenEndsAt(long absoluteOffset) =>
            absoluteOffset < _data.Length && !PdfCharacters.IsRegular(_data.Span[(int)absoluteOffset]);

        /// <summary>
        /// Gets what keeping the contents costs, in bytes: the decoded data, the header's two tables and the lookup of its
        /// numbers, whether built yet or not — four bytes for each table and eight for the lookup, for each member (#160).
        /// </summary>
        public long Weight => _data.Length + (16L * _numbers.Length);

        public int NumberAt(int index) => _numbers[index];

        /// <summary>Gets where the stream's data starts in the file, where its reports are placed.</summary>
        public long DataStart => _position;

        /// <summary>
        /// Reads the header of object stream <paramref name="number"/>, or gives null when it serves no object: it
        /// declares none, or what it says of itself cannot be believed, which is reported.
        /// </summary>
        /// <remarks>
        /// Each report is placed where the stream's data starts, and says which fault it met and how many of the
        /// stream's objects can still be read from it; it is made once for each stream, however often the stream is
        /// decoded. A <c>/First</c> past data a guard cut is not reported, nor data the guard on filters left encoded:
        /// the guard was, under its own code.
        /// </remarks>
        /// <param name="number">The object stream's number.</param>
        /// <param name="stream">The object stream.</param>
        /// <param name="diagnostics">Receives what is wrong with the stream.</param>
        /// <param name="reports">Says which streams were reported already.</param>
        /// <param name="hiddenByGuard">Whether a guard, not the file, left the stream unable to serve its objects.</param>
        public static ObjectStreamContents? TryCreate(
            int number, PdfStream stream, PdfDiagnostics diagnostics, ObjectStreamReports reports, out bool hiddenByGuard)
        {
            hiddenByGuard = false;
            var position = stream.Data.Position;

            // A stream of another type serves no member, as the validator and the rebuild read it (#274): nothing past its
            // type is read — its /Length, the parse resolved —, so nothing else it names is needed.
            if (!stream.Dictionary.IsOfType(PdfName.ObjStm))
            {
                ReportUnreadable(diagnostics, reports, number, position, "is a stream that is not of /Type /ObjStm", 0, 0);
                return null;
            }

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

            var data = Filters.PdfFilterPipeline.Decode(stream, diagnostics, out var leftEncoded, out var cutByGuard);

            // Data the guard on filters left encoded holds none of the stream's objects, as data a guard cut before /First.
            if (leftEncoded)
            {
                hiddenByGuard = true;
                return null;
            }

            if (first > data.Length)
            {
                hiddenByGuard = cutByGuard;

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

            var header = ObjectStreamHeader.Read(data.Span, count, (int)first, cutByGuard);

            if (header.Fault != ObjectStreamHeaderFault.None)
            {
                ReportUnreadable(
                    diagnostics,
                    reports,
                    number,
                    position,
                    header.Fault switch
                    {
                        ObjectStreamHeaderFault.Ended => string.Create(
                            CultureInfo.InvariantCulture, $"has a header that ends after {header.Count:N0} of the {count:N0} objects its /N declares"),
                        ObjectStreamHeaderFault.Stray => string.Create(
                            CultureInfo.InvariantCulture,
                            $"has a header that holds something other than an object number and an offset after {header.Count:N0} of the {count:N0} objects its /N declares"),
                        _ => string.Create(
                            CultureInfo.InvariantCulture,
                            $"has a header that lists object {header.FaultNumber} at offset {header.FaultOffset}, {header.Why(data.Length)}, after {header.Count:N0} of the {count:N0} objects its /N declares"),
                    },
                    header.Count,
                    count);
            }

            return header.Count > 0
                ? new ObjectStreamContents(number, position, reports, data, header.Numbers, header.Offsets, (int)first, cutByGuard)
                : null;
        }

        /// <summary>Parses the object the stream holds under <paramref name="expectedNumber"/>, or gives null when it holds none.</summary>
        /// <param name="index">Where the index says the object is among the stream's.</param>
        /// <param name="expectedNumber">The object's number.</param>
        /// <param name="source">Resolves what the object refers to.</param>
        /// <param name="diagnostics">Receives what the index got wrong of the object's place.</param>
        /// <param name="parsing">Receives what parsing met, for the caller to keep or drop.</param>
        /// <param name="maxLength">The most of the data the member is read over, <see cref="PdfReaderLimits.MaxObjectLength"/>.</param>
        /// <param name="cut">
        /// Whether the object runs into the end of data a guard cut, or past <paramref name="maxLength"/>, and was read only
        /// as far as it, or starts past data a guard cut, and was not read.
        /// </param>
        /// <param name="pastLength">
        /// Where the member starts in the data, when it runs past <paramref name="maxLength"/>, which a guard then reports;
        /// -1 otherwise.
        /// </param>
        /// <param name="streamAt">
        /// Where the member starts in the data, when it is written as a stream, which an object stream cannot hold: it is then
        /// no value; -1 otherwise.
        /// </param>
        public PdfObject? Parse(
            int index,
            int expectedNumber,
            IPdfObjectSource source,
            PdfDiagnostics diagnostics,
            PdfDiagnostics parsing,
            int maxLength,
            out bool cut,
            out long pastLength,
            out long streamAt)
        {
            cut = false;
            pastLength = -1;
            streamAt = -1;

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
                if (_reports.IsFirstMove(_position, expectedNumber))
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

            // Past data a guard cut, the object is unread for the guard's reasons, not the file's (#259): the header keeps a
            // pair whose member starts past the data only then. Both are at most int.MaxValue, so their sum is a long.
            if ((long)_first + _offsets[index] >= _data.Length)
            {
                cut = true;
                return null;
            }

            var window = ObjectStreamMemberWindow.Of(_offsets, index, _first, _data.Length, _cutByGuard, maxLength);
            var parser = PdfObjectParser.ForObjectStreamMember(
                _data[..window.End], _number, _position, expectedNumber, source, parsing, window.EndsData, window.EndsAtNextMember, this);
            parser.Position = window.Start;
            var value = parser.ParseObject();

            // A member written as a stream is no value (#161).
            if (parser.IsMemberStream)
            {
                streamAt = window.Start;
                return null;
            }

            cut = !window.EndsData && parser.IsTruncated;
            pastLength = cut && window.Bounded ? window.Start : -1;
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
            _places ??= ObjectStreamPlaces.Of(_numbers);
            return ObjectStreamPlaces.FirstIndexOf(_places, number);
        }
    }
}
