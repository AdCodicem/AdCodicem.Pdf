using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
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
internal sealed partial class PdfFileReader : IPdfObjectSource, IPdfStreamDataProvider
{
    internal const int InitialObjectWindow = 8 * 1024;
    internal const int XRefWindow = 64 * 1024;

    /// <summary>What the codes of the faults of the object syntax start with.</summary>
    private const string SyntaxCodePrefix = "syntax.";

    /// <summary>
    /// How much of a section is read first to tell a classic table from a cross-reference stream: a read that holds
    /// white space and comments alone, or a first token its edge may cut, is read again eight times larger, as far as a
    /// trailer's window reaches and this many bytes past it, which hold the keyword whole where it starts at the window's
    /// end (<see cref="PaddingAt"/>).
    /// </summary>
    internal const int XRefProbeLength = 32;

    /// <summary>
    /// The length of a classic table's row as producers write it short, its end of line a single byte where ISO 32000-1
    /// (7.5.4) asks for two: the index makes room for no more rows than a table's bytes hold at this length, nor than they
    /// hold lines. A row written shorter still takes room as the index grows.
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
    /// <remarks>
    /// Within the radius means lying whole between the offset less the radius and the offset plus it, clamped to the
    /// file, wherever the offset falls: a header from where a load starts its number's token — its first digit, or the
    /// first sign of the run of signs it is written with (#327) — to the end of its <c>obj</c> keyword, a classic
    /// table's <c>xref</c> keyword from its first byte to its last. Near the file's start the search reaches no further
    /// past the offset than elsewhere, where a window clamped there once reached up to twice the radius past it (#197);
    /// nothing is looked for near an offset more than the radius before the file's start. The bytes before the window
    /// are read as well — one for a section, and for an object as many as an end-of-file marker has, to which a writer
    /// may glue a header —, so that a number its start cuts — <c>14 0 obj</c> read from its <c>4</c> — is not taken for
    /// a header that starts there.
    /// </remarks>
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
    /// What <see cref="HeaderAt"/> gives, and what a placement gives for the number at the offset
    /// (<see cref="HeaderPlacement.NumberAtOffset"/>), where an entry's row cuts a number (<see cref="RowCutsNumber"/>):
    /// <c>14 0 obj</c> named from its <c>4</c>.
    /// </summary>
    internal const long HeaderCutsNumber = -3;

    /// <summary>
    /// How many bytes a reading of an entry's row reads before it, with its first window: the byte that tells a row that cuts
    /// a number (<see cref="RowCutsNumber"/>).
    /// </summary>
    internal const int ByteBeforeRow = 1;

    /// <summary>
    /// How many headers of one number the bytes the search near a row reads can hold: a header takes 8 bytes at least,
    /// <c>1 0 obj</c> and the byte that ends its keyword, since a number glued to it makes it no keyword (#189).
    /// </summary>
    private const int MaxNearbyHeaders = (2 * NearbySearchRadius / 8) + 1;

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
    /// The most places near missing cross-reference sections that are tried as one, over a whole document, each counted
    /// once, at its first reading. Real files miss by a few bytes, with the section the nearest candidate; a file packed
    /// with object headers there, or with sections that all miss, gets no more attempts. A place passed over as possibly a
    /// hybrid table's own stream, tried again once every other place near it was, is not counted again, and is tried even
    /// once the bound stops new places: the places are tried at most twice as many times as the bound. The stream a hybrid
    /// table's <c>/XRefStm</c> read, put back unread, is counted where it is read, and the bound may stop it. Past the
    /// bound, the place a hybrid table's <c>/Prev</c> names, where it is the nearest place not yet read, is still asked
    /// whether that <c>/Prev</c> keeps its place there, in the look <see cref="KeepsItsPlace"/> makes once for each hybrid
    /// table, beside the places tried, and the section is left to that <c>/Prev</c> where it does. Not a guard a valid
    /// file can reach: a valid file's sections are where its chain names them.
    /// </summary>
    private const int MaxRelocationCandidates = 32;

    /// <summary>
    /// How far before the bytes searched near an offset the end of line that starts a place's line is looked for, read
    /// with those bytes again where the line starts before them, so that the line is lexed from its start
    /// (<see cref="IsTokenOnItsLine"/>): the line test judges every place the search tries that cannot be read — one the
    /// chain names, one past where the scan's memo is whole, and one the memo holds — and every cross-reference stream it
    /// passes over that may be reported (<see cref="StartsItsLine"/>, <see cref="ReportStreamsPassedOver"/>). Where no end
    /// of line lies within them, and the file starts farther back, the test cannot tell, and <see cref="StartsItsLine"/>
    /// says what stands then. Not a guard a valid file can reach (ADR 34): the search runs only near an offset that names
    /// no section, and a valid file's sections are where its chain names them.
    /// </summary>
    private const int LineStartSearchLength = 4096;

    /// <summary>
    /// The most ranges of object numbers the sections a chain reads refuse to the older sections' rows, their rows lost —
    /// the cross-reference streams it goes on past, and the sections it keeps though read only in part —, once ranges that
    /// meet are joined (<see cref="RefuseRanges"/>); past it every number is refused, and what the older sections index is
    /// looked for by rebuilding the index, as for a stream whose numbering cannot be read. So many ranges a classic table's
    /// faults lose are kept at most, too, each joined as it is lost to the last one lost where the two meet, until the table
    /// is kept (#210) or, named by an <c>/XRefStm</c>, lost (#156). Not a guard a valid file can reach: only a file whose
    /// cross-reference sections cannot be read whole refuses any number. It keeps what one refusal costs, a merge of the
    /// ranges kept with those a section lost, from growing with every section a chain of such sections holds, each losing
    /// thousands of subsections (#208, #210).
    /// </summary>
    private const int MaxRefusedRanges = 4096;

    private const int ScanChunkSize = 1024 * 1024;

    /// <summary>
    /// How many bytes before where its search resumes each piece of the rebuild's scan reads: those a short header's walk
    /// back from its <c>obj</c> reads. A walk that needs more reads them again (<see cref="ReadHeaderFurtherBack"/>), so this
    /// sizes a read, and bounds nothing.
    /// </summary>
    private const int ScanLead = 64;

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
    /// Not a guard a valid file reaches (ADR 34), argued here once for every read it counts. A valid file is read past
    /// the parser's window in four ways only. Three read the data or what follows it: a cross-reference stream whose
    /// <c>/Length</c> the chain cannot read (#220) is searched once to its <c>endstream</c>; a stream whose
    /// <c>/Length</c> a guard cut or left out of the index (#224) is searched once to its <c>endstream</c>, or to a
    /// header its data spells, which leaves the data up to it to the guard; and white space after a declared length
    /// that runs past a first read is read on once for each place, and past an <c>endstream</c> once for each keyword
    /// however many places lead to it (#174). The data of different streams lies apart, and so do the runs of white
    /// space after them: together those reads cover the file once, and one read past each — about twice the file —, but
    /// where one run is two ways' at once: the white space before the <c>endstream</c> of a cross-reference stream whose
    /// <c>/Length</c> the chain cannot read is the data its search takes and the run its length, once read, is read on
    /// through, about three times. The fourth adds a little to each stream of the second way: the proof that an <c>endstream</c> found is the
    /// stream's own (#224) reads past it to its object's end and what follows, three first reads of
    /// <see cref="ProofRead"/> bytes, and a fourth past a row that names the white space before the next object's
    /// header, each once however often the stream is parsed — 48 or 64 bytes, about what the smallest such stream spans
    /// with its header. White
    /// space it crosses adds to that: the run before the object's <c>endobj</c>, or before what follows the keyword where
    /// no <c>endobj</c> does, it takes from the read-on that read it, the run after an <c>endobj</c> it doubles through, as
    /// the read-on does, and the run a row names before the next header it
    /// reads once from there, a first read reaching the header. No run is read more than three times, and the file
    /// about three times at most in all, within the four the searches may read. Every other search is of an invalid
    /// stream, whose <c>endstream</c> does not follow its declared length. Each reads from a stream's data to the first
    /// object header the bytes hold after it, or to the next object the index as written places, whichever is nearer,
    /// and its reads grow from twice what the parser holds of the data (<see cref="EndStreamSearchFirstRead"/>): it reads
    /// at most about twice what its object spans, and the searches of objects whose headers follow one another read
    /// disjoint stretches — the file twice over at most. Only searches that share a stretch read more: objects that
    /// overlap, the header of one inside the dictionary of another, before its data; or a stream whose header the
    /// search does not take — a regular character glued before its number, where a rebuild's scan takes the digits
    /// after it for one — and the index as written does not place, which only a rebuilt one does. This keeps what they
    /// read in proportion to the file, as a window keeps the parsing of their dictionaries. Which streams keep their
    /// declared length unsearched past it depends on the order they are read in: crafted files reach it, and no
    /// document of the corpus comes near, whose searches read 0.35 of a file at most. The rebuild's scan counts its own
    /// reads past declared lengths apart, against the same multiple: it reads the white space after each length its
    /// stream headers declare, the runs after different streams apart, about the file once for a valid one.
    /// </remarks>
    internal const int EndStreamSearchPasses = 4;

    /// <summary>
    /// How many bytes the first read of each step of an <c>endstream</c>'s proof reads (<see cref="EndStreamFollower"/>):
    /// what follows a keyword is mostly an end-of-line away, and the reads double past it as white space runs on.
    /// </summary>
    private const int ProofRead = 16;

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
    /// is asked as well before each load. The validator's walk of the object streams' dependencies nests its loads as these
    /// do, and reads as null past this same bound (<see cref="Validation.ObjectStreamDependencies"/>, #277): were the bound
    /// made a guard (#163), the walk would take the guard's.
    /// </summary>
    internal const int MaxNestedLoads = 64;

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
    private static ReadOnlySpan<byte> XRefKeyword => "xref"u8;
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
    private readonly HashSet<(PdfLimit Limit, long Position)> _limitsReached = new(SeededComparer.Instance);

    /// <summary>
    /// What the report of the memo's bound is held under among the guards reported (<see cref="ReachScanMemoBound"/>): a place
    /// no byte of the file has, so that it is told apart from the index's report of the same bound wherever that lies.
    /// </summary>
    private static readonly (PdfLimit Limit, long Position) ScanMemoBoundReported = (PdfLimit.IndexEntries, long.MinValue);

    /// <summary>
    /// The object stream members <see cref="PdfReaderLimits.MaxObjectLength"/> cut, by where their stream's data starts and
    /// their number, each reported once: a stream defined again, which a rebuild may serve, is another place.
    /// </summary>
    private readonly HashSet<(long DataStart, int Number)> _memberLimitsReached = new(SeededComparer.Instance);

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
    /// Where each section the chain read starts, a position in the file: its <c>xref</c> keyword or its stream's object
    /// header, where it was named — past the white space the offset fell short by — or where it was found moved, a hybrid
    /// file's <c>/XRefStm</c> stream included; and where each section starts that the chain reached and could not read,
    /// though it declares itself one — a cross-reference stream whose <c>/Prev</c> the chain went on through, a table or a
    /// stream an <c>/XRefStm</c> names —, which is there, reported once. With the offsets the chain named, as written, it
    /// is what the chain has already reached (<see cref="ReachedAt"/>): a section found moved is named at one offset and
    /// read at another, bytes before the header set every position apart from the offset that names it (#188), and an
    /// offset that names the white space before a section reaches that section all the same.
    /// </summary>
    /// <remarks>
    /// As many as the sections the chain reads, and let go once it is read, when nothing reads it any more.
    /// </remarks>
    private readonly HashSet<long> _sectionsReadAt = new(SeededComparer.Instance);

    /// <summary>
    /// Where the digits of the object number of each cross-reference stream the chain reached start, a position in the file,
    /// as its reading lexed them (<see cref="XRefSectionRecord.DigitsAt"/>): past the run of signs the number is written
    /// with, where it is, and where the header starts otherwise — beside each start <see cref="_sectionsReadAt"/> holds
    /// for a stream, and -1 for a classic table, which no place equals. The search near an offset finds a header written
    /// <c>-+5 0 obj</c> or <c>++5 0 obj</c> at its digit, wherever the run of signs before it starts — or the second at
    /// the run's first sign, within the bytes searched, where the line its number is on holds a <c>%</c> —, and a reading
    /// from a <c>+</c> of that run, or from the digit past it, read the same header: it tells the header as that section
    /// by this position, looked up past the run of signs the place it found starts with (<see cref="DigitsPastSigns"/>,
    /// #327).
    /// </summary>
    /// <remarks>
    /// One entry for each start <see cref="_sectionsReadAt"/> takes, and let go with them once the chain is read.
    /// </remarks>
    private readonly HashSet<long> _sectionDigitsAt = new(SeededComparer.Instance);

    /// <summary>
    /// What the last probe <see cref="PaddingAt"/> read found, and the one before it: the chain asks of each position it
    /// names where the first token there starts, to know whether it has reached what is there (<see cref="ReachedAt"/>), then
    /// again where it reads the section there, and of a hybrid table's <c>/Prev</c> in between. Each position's white space
    /// is crossed once, however often it is asked of: through a run of it, a probe reads as far as a trailer's window does.
    /// </summary>
    /// <remarks>
    /// The file's bytes and the trailer's bound do not change while the reader reads it, so that a probe's answer stands
    /// wherever it is asked for again: two are kept, nothing more, and what is read is what a reading that asked once read.
    /// The places a search near an offset tries are not kept, however many it tries: each starts at a token — a keyword, an
    /// object's header —, whose probe crosses no white space: one window of <see cref="XRefProbeLength"/> bytes where that
    /// token ends short of its edge, and at most two more, of up to 256 and 2,048 bytes, where leading zeros carry a
    /// header's number to the last one's edge or past it: they may read past the search's window, but the number lies
    /// within it, and <see cref="MaxRelocationCandidates"/> bounds the places. A section found there is reached through
    /// <see cref="_sectionsReadAt"/>, not asked of again; kept, they would push out the probe of a hybrid table's
    /// <c>/Prev</c>, taken before its <c>/XRefStm</c>'s search, and the chain would cross that run again (#188).
    /// </remarks>
    private (PaddingProbe Last, PaddingProbe Before) _paddingProbes = (PaddingProbe.None, PaddingProbe.None);

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
    /// those it read through, the cut having left their value a whole reference — but for an object it read for a place
    /// the section may have been moved to that the search did not take, the <c>/Prev</c> of the stream a hybrid table's
    /// <c>/XRefStm</c> read, judged there, among them; what the offset the chain names holds keeps all of its reading —:
    /// each is loaded once the chain is read, so that its guard is reached where a load reaches it (#263).
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
    /// The offset the <c>/Prev</c> of the section just read gives where a guard stopped its rows — cut them, or filled the
    /// index —, which ends the chain at it: judged as far as the guard lets it be, and never read (#264). -1 where it gives
    /// none, or where no guard stopped the rows, the chain going on through the <c>/Prev</c> the section's reading gives.
    /// Set as each section is read.
    /// </summary>
    private long _previousPastGuard = -1;

    /// <summary>
    /// The offset the <c>/XRefStm</c> of the classic table just read gives where a guard stopped its rows, as
    /// <see cref="_previousPastGuard"/> is for its <c>/Prev</c> (#264).
    /// </summary>
    private long _streamPastGuard = -1;

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
    private readonly Dictionary<int, PdfObject> _cache = new(SeededComparer.Instance);
    private readonly Queue<int> _cacheOrder = new();
    /// <summary>
    /// The object streams decoded, by number, null for one that cannot be read; with the order they were decoded in
    /// and what they weigh — their data, their headers' tables and lookups —, which <see cref="ObjectStreamBudget"/> bounds.
    /// </summary>
    private readonly Dictionary<int, ObjectStreamContents?> _objectStreams = new(SeededComparer.Instance);

    /// <summary>
    /// The object streams, by number, that a guard left unable to serve their objects — cut short of their stream, their
    /// data cut before <c>/First</c> or left encoded, a guard reached decoding them under
    /// <see cref="PdfReaderOptions.ThrowOnLimit"/> —: an object they hold is unread for the guard's reasons, not the file's,
    /// as one a guard cut is (#259). A rebuild's expansion records any object a guard cut short of a stream, which the
    /// chain's index may place members in.
    /// </summary>
    private readonly HashSet<int> _objectStreamsHiddenByGuard = new(SeededComparer.Instance);
    private readonly Queue<int> _objectStreamOrder = new();
    private long _objectStreamBytes;

    /// <summary>The numbers of the objects being loaded, by number for the reason <see cref="_cache"/> gives.</summary>
    private readonly HashSet<int> _loading = new(SeededComparer.Instance);

    /// <summary>The object streams being decoded: an object one of them holds cannot be read meanwhile (#51).</summary>
    private readonly HashSet<int> _objectStreamsLoading = new(SeededComparer.Instance);

    /// <summary>The object streams already reported as needing an object they hold to be read.</summary>
    private readonly HashSet<int> _objectStreamsNeedingThemselves = new(SeededComparer.Instance);

    /// <summary>
    /// The objects each object stream read as null while it was decoded because of how the file is made — an object
    /// it holds, or one being loaded —, kept after the stream is let go: decoded again, it reads them as null again,
    /// so that what it holds does not depend on what was read, and kept, in between, nor on
    /// <see cref="ObjectStreamBudget"/>. A load too deep to follow depends on where it was asked from, not on the
    /// file, and is not recorded.
    /// </summary>
    private readonly Dictionary<int, HashSet<int>> _nullWhileDecoding = new(SeededComparer.Instance);

    /// <summary>
    /// The objects the index holds in use that the reader gave up on the last time they were asked for — nothing
    /// where the entry places them nor near it, an object stream that cannot serve them —, for the validation rules
    /// to tell an object the file lacks from one it holds and the reader could not produce.
    /// </summary>
    private readonly HashSet<int> _unproduced = new(SeededComparer.Instance);

    /// <summary>
    /// The objects a guard cut, read only as far as the limit let the reader go: what they hold past it is unknown,
    /// so the validation rules judge nothing of their shape.
    /// </summary>
    private readonly HashSet<int> _cutAtLimit = new(SeededComparer.Instance);

    /// <summary>
    /// The objects read whose value <c>endobj</c> does not follow, and where each was read. An object stream's
    /// members have none to follow them, and are never in it.
    /// </summary>
    private readonly Dictionary<int, long> _endObjMissing = new(SeededComparer.Instance);

    /// <summary>
    /// What was found of each stream read whose length the file did not confirm, by object number, as the reading kept
    /// last found it: a sound file records nothing.
    /// </summary>
    private readonly Dictionary<int, StreamLengthFault> _streamLengthFaults = new(SeededComparer.Instance);

    /// <summary>
    /// What each search of the file for a stream's <c>endstream</c> found, by where the stream's data starts and whether
    /// its <c>/Length</c> was one the chain could not read: the file is searched once for each stream, however many times
    /// it is parsed. With it, the declared end it last said what follows of, read again only when that end changes.
    /// </summary>
    private readonly Dictionary<(long DataStart, bool Deferred), (StreamEndSearch Search, long DeclaredEnd)> _endStreamSearches = new(SeededComparer.Instance);

    /// <summary>
    /// What follows each place the reader read on past white space and comments to find an <c>endstream</c> (#174): read
    /// once, however many times its stream is parsed.
    /// </summary>
    private readonly Dictionary<long, EndObjState?> _endStreamChecks = new(SeededComparer.Instance);

    /// <summary>
    /// Where the first significant byte lies past each <c>endstream</c> keyword the reader read on past (#174), an
    /// <c>endobj</c> there or not: another place that leads to the keyword, and the proof that the keyword ends the stream's
    /// own data (#224), take it rather than read the white space again.
    /// </summary>
    private readonly Dictionary<long, long> _endObjStarts = new(SeededComparer.Instance);

    /// <summary>
    /// What follows the object's end after each place a search ended data at, with what was said follows the
    /// <c>endstream</c> there, as <see cref="IPdfStreamDataProvider.IsOwnEndStream"/> reads it: where its first significant
    /// byte lies, -1 where nothing could be read, and whether the end of the file or a cross-reference section's keyword
    /// stands there. Read once, however many times its stream is parsed; each caller's next object is weighed against it.
    /// </summary>
    private readonly Dictionary<(long DataEnd, EndObjState EndObj), (long Position, bool Boundary)> _endStreamFollowers = new(SeededComparer.Instance);

    /// <summary>
    /// Where the first significant byte lies past each offset a row names before the place it is weighed against, as
    /// <see cref="IsPlacedAt"/> reads it: -1 where nothing could be read. Read once, however many times its stream is
    /// parsed.
    /// </summary>
    private readonly Dictionary<long, long> _placedStarts = new(SeededComparer.Instance);

    /// <summary>
    /// Where the first significant byte lies past each place a search ended data at, the <c>endstream</c> keyword it found:
    /// -1 where nothing could be read. Read once, however many times its stream is parsed.
    /// </summary>
    private readonly Dictionary<long, long> _endStreamKeywords = new(SeededComparer.Instance);

    /// <summary>
    /// Where the data starts of each stream whose length fault a reading the reader kept reported: the stream is not
    /// reported again when it is parsed again.
    /// </summary>
    private readonly HashSet<long> _lengthFaultsReported = new(SeededComparer.Instance);

    /// <summary>What the object streams' reports said already: each fault is reported once, however often its stream is decoded.</summary>
    private readonly ObjectStreamReports _objectStreamReports = new();

    /// <summary>How many bytes the searches for <c>endstream</c> read, which <see cref="EndStreamSearchPasses"/> bounds.</summary>
    private long _endStreamSearchBytes;

    /// <summary>
    /// How many bytes the rebuild's scan read past streams' declared lengths to confirm them, which
    /// <see cref="EndStreamSearchPasses"/> bounds apart from the loads' searches, so that what the scan reads leaves them
    /// theirs.
    /// </summary>
    private long _scanGapBytes;

    /// <summary>
    /// What the rebuild's scan for objects takes of the file, made the first time something asks for it, and kept for the
    /// document's life (<see cref="GetScanMemo"/>); null before.
    /// </summary>
    private ScanMemo? _scanMemo;

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
    /// outermost returns; or one the source failed, which the next read or validation finishes again from the index its
    /// scan made when the source failed the finish the outermost load ran, and runs again from its scan otherwise; null
    /// when no rebuild waits to be finished.
    /// </summary>
    private UnfinishedRebuild? _unfinishedRebuild;
    private bool _nestingReported;

    /// <summary>
    /// Whether an object the validator's walks of the object streams did not load for want of stack was reported.
    /// </summary>
    private bool _walkCutReported;

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
    /// The object <see cref="PdfDiagnosticCodes.TrailerRootRecovered"/> last named, 0 while it named none — object 0 heads the
    /// free list, and no catalog is: a rebuild after opening sets aside the catalog a search took, and the search it runs
    /// takes the same object again, which is not reported twice.
    /// </summary>
    private int _catalogReportedAs;

    /// <summary>
    /// Where the newest dictionary a rebuild merged into the trailer starts, at its <c>trailer</c> keyword; -1 while no
    /// rebuild merged one.
    /// </summary>
    private long _rebuiltTrailerPosition = -1;

    /// <summary>
    /// How many places have been tried for sections of the chain that were not where it named them, each counted once, at
    /// its first reading: a place read and passed over, tried again once every other place was, is not counted again
    /// (<see cref="XRefSectionRecord.MayBeTablesOwn"/>).
    /// </summary>
    private int _relocationCandidatesTried;

    /// <summary>
    /// The cross-reference streams the search near the offset a hybrid table's <c>/Prev</c> names passed over as
    /// possibly that table's own and never came back to, another section taken first, that declare themselves one — the
    /// dictionary gives <c>/Type /XRef</c> —: where each header's token starts, at the first sign of the run its number
    /// is written with where that run starts with a <c>+</c> (<see cref="HeaderTokenStart"/>), where its object number
    /// ends, the offset the <c>/Prev</c> names, and the table. Null while there is none. Each the chain has not
    /// reached, at its header or inside its object number, is reported once the chain is read, but where one of the
    /// reader's limits left part of the chain unread, or may have hidden a link the chain would have followed or cut
    /// what would end it (<see cref="_guardMayHideLink"/>,
    /// <see cref="ReportStreamsPassedOver"/>).
    /// </summary>
    /// <remarks>
    /// As many as the places passed over, which <see cref="MaxRelocationCandidates"/> counts, and let go once the chain is
    /// read.
    /// </remarks>
    private List<(long At, long NumberEnd, long Named, long Table)>? _streamsPassedOver;

    /// <summary>
    /// Whether the index may lack objects the file defines because the file is damaged: a section the chain names could not
    /// be found or read, or was read only in part — a row that is no row ended a subsection, a cross-reference stream's data
    /// holds fewer rows than it declares, the end of the file cuts a table before its trailer, a row was refused —, or the
    /// chain loops. Only then is an object the index lacks looked for by rebuilding it; otherwise a reference to it is null,
    /// as the specification says.
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
    /// Whether <see cref="PdfReaderLimits.MaxTrailerLength"/> may have hidden a link the chain would have followed, or cut
    /// what would end the chain, at an offset the chain names: set beside the chain's own cut
    /// (<see cref="ChainStoppedByGuard"/>) where that marks it too, and alone where it does not. Set where white space and
    /// comments ran on past it at an offset the chain names, the section not read from there
    /// (<see cref="TryReadXRefSection"/>): the search near the offset stands for that reading, and may take a section other
    /// than the one past them, whose links the chain then follows in that section's place, pass over a stream the reading
    /// would not have come to, or find nothing, what the section names left unread (#289). Which, the reader cannot tell
    /// without reading past the guard: the flag is set even where the search finds the section whole past them, which only
    /// a bound under the 512 bytes the search reaches past the offset allows (<see cref="NearbySearchRadius"/>). Set too
    /// wherever the guard cut the reading at an offset the chain names, whatever it cut (<see cref="TryReadXRefStream"/>):
    /// a dictionary, whatever its <c>/Type</c>; the object's header — its number, its generation or its <c>obj</c> keyword
    /// running on past the guard, or white space or a comment between its tokens —; or a token where no header starts.
    /// Where the reading takes what is there for the guard's section, the chain's own cut marks it already; where it does
    /// not, what is there reads as an object of another type, which holds no section, the parser keeping a dictionary as
    /// far as it read it (#279), or as no object at all, a header the cut parted (#289), and what lay past the cut is
    /// unknown. Where a <c>/Prev</c> or <c>startxref</c> names the offset, any <c>/W</c> makes a stream of any <c>/Type</c>
    /// the section there (#263), and the cut may have hidden one, the object's <c>/Prev</c> a link the chain would follow,
    /// not followed wherever it lies, past the cut or before it: <c>/XRef</c> parted, inside the name or inside a
    /// <c>#xx</c> escape, a stream of another <c>/Type</c> that gives a <c>/W</c>, a <c>/Type</c> given twice whose later
    /// <c>/XRef</c> lies past the cut, or the whole dictionary past a header the cut parted. Where an <c>/XRefStm</c> names
    /// it, the chain never follows that stream's <c>/Prev</c>, and the cut hides no link the chain follows, but it is what
    /// would end the chain there, as the guard's cut of that stream does (#264), the reading #279's closing gives it.
    /// Which, the reader cannot tell without reading past the guard: the flag is set even where the cut hid nothing, a
    /// dictionary of another type that gives no <c>/W</c>, or a token no header starts, among them. Only
    /// <see cref="ReportStreamsPassedOver"/> reads it, which then reports no stream passed over anywhere in the document
    /// (ADR 34). Neither takes the chain for one a guard cut: what the reading finds there is reported as the file's — the
    /// section found moved, or missing and the index rebuilt —, as #279 and #289 leave it.
    /// </summary>
    private bool _guardMayHideLink;

    /// <summary>
    /// Whether the index as last built had no room for a number it lacks, <see cref="PdfReaderLimits.MaxIndexEntries"/>
    /// reached: an object it holds no entry for reads as null, and is not looked for by rebuilding it.
    /// </summary>
    private bool _indexFull;

    /// <summary>
    /// The object numbers whose row a section of the chain refused, each with the ordinal of the reading that refused it
    /// first (<see cref="XRefSectionRecord.Ordinal"/>), or null while none was: the sections the chain reads after it describe
    /// older revisions, whose rows must not stand for the current one. A row of the section that refused the number, which
    /// gives it twice, does not stand either, and is not said to have been refused by a newer section
    /// (<see cref="XRefSectionRecord.RowsRefusedBefore"/>). Let go once the chain is read, when nothing reads it any more;
    /// until then <see cref="PdfReaderLimits.MaxIndexEntries"/> counts it with the index, which no number is in at once.
    /// </summary>
    /// <remarks>
    /// The ordinal of the reading rather than its record: each slot of the map takes 20 bytes — its entry and its bucket —, 4
    /// more than a set of the numbers alone and 8 fewer than with a reference. Filled a number at a time, as rows are
    /// refused, it holds up to about twice as many slots as numbers, and allocates about 60 bytes a number in all as it
    /// grows, where a reference took about 85, beside the index's 40, for no more numbers than the guard lets the index hold.
    /// </remarks>
    private Dictionary<int, int>? _refusedNumbers;

    /// <summary>
    /// The numbers the reading of a place a search near an offset tries refuses, while it is read, so that a place dropped
    /// gives them back (<see cref="RollBackIndex"/>, #320); null outside such a reading.
    /// </summary>
    private List<int>? _refusedOrder;

    /// <summary>
    /// How many readings of a cross-reference section the chain started — the chain's own, and each place a section may have
    /// been moved to that it tried —, the count given to each reading's record as it starts
    /// (<see cref="XRefSectionRecord.Ordinal"/>). Only told equal or not: two readings share one only 4,294,967,296 readings
    /// apart, past what any file gives, the chain reading at most two sections for each one
    /// <see cref="PdfReaderLimits.MaxXRefSectionCount"/> counts, and twice <see cref="MaxRelocationCandidates"/> places more.
    /// </summary>
    private int _sectionReadings;

    /// <summary>
    /// The object numbers whose rows the sections the chain read lost — the numbers a cross-reference stream it went on past
    /// declares (#208), those a section it kept though read only in part lost (#210), and those a classic table an
    /// <c>/XRefStm</c> names lost, which cannot be read (#156) —, refused to the rows of the sections the chain reads after
    /// them, which describe older revisions: ranges from a first number up to the one past the last, sorted by their first
    /// number, none meeting another, in the first <see cref="_refusedRangeCount"/> elements. Null while no section lost a
    /// number; rented from the shared pool, at most <see cref="MaxRefusedRanges"/> long, and given back once the chain is
    /// read, when nothing reads it any more (<see cref="RefuseRanges"/>).
    /// </summary>
    private (long First, long End)[]? _refusedRanges;

    /// <summary>How many ranges of <see cref="_refusedRanges"/> hold refused numbers.</summary>
    private int _refusedRangeCount;

    /// <summary>
    /// Whether every object number is refused to the rows of the sections the chain reads from here on: a cross-reference
    /// stream that lost its rows numbers them in a way that cannot be read, the sections that lost rows lost them in more
    /// ranges than <see cref="MaxRefusedRanges"/> (#208, #210), or a classic table lost what it holds past where its reading
    /// stopped, which cannot be known (<see cref="XRefSectionRecord.SubsectionsUnknown"/>, #156).
    /// </summary>
    private bool _everyNumberRefused;

    /// <summary>
    /// The object numbers the rows of the classic table being read lost — the rest of each subsection a row that is no row,
    /// or the end of the file, ended —, as ranges from a first number up to the one past the last, each joined as it is lost
    /// to the last one lost where the two meet, at most <see cref="MaxRefusedRanges"/> of them: refused to the rows of the
    /// sections the chain reads after the table, once it keeps the table read only in part (#210), or loses one an
    /// <c>/XRefStm</c> names (#156). Emptied as each pass over a table's rows starts, and let go once the chain is read; null
    /// while no table lost a row.
    /// </summary>
    private List<(long First, long End)>? _rowsLost;

    /// <summary>
    /// Whether the classic table being read lost rows in more ranges than <see cref="MaxRefusedRanges"/>: kept read only in
    /// part, or lost where an <c>/XRefStm</c> names it, it refuses every number to the rows of the sections the chain reads
    /// after it.
    /// </summary>
    private bool _rowsLostPast;

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

    /// <summary>
    /// Whether a section older than the newest gave a number past every one the sections before it gave: the newest
    /// <c>/Size</c>, stale, then bounds the index's room no more (<see cref="ReserveIndex"/>).
    /// </summary>
    private bool _olderNumberedPast;

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
    /// <remarks>
    /// Read it again rather than keep it: a rebuild merges into a new trailer, leaving the one taken before as it was
    /// (#167).
    /// </remarks>
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

    /// <summary>
    /// Gets the object numbers the index holds an entry for, free ones included: those it held when the enumeration began,
    /// whatever a rebuild does meanwhile (#167).
    /// </summary>
    public IEnumerable<int> ObjectNumbers => _xref.Numbers;

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
    /// cross-reference stream's object —, though the index be rebuilt afterward; otherwise the newest trailer a rebuild
    /// merged — the dictionary after a <c>trailer</c> keyword, where the keyword lies, or a cross-reference stream's, where
    /// its header lies (#154) —, which a rebuild run after opening — set off by a read, or before a validation's first rule
    /// — finds then. A key only an older trailer gave is placed there too: in the file, at a trailer the reader
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

    /// <summary>
    /// Gets the decoded data the reader keeps of object stream <paramref name="number"/>, or no data when it keeps none: the
    /// stream not decoded yet, let go by the budget or by closing, or one that serves no object.
    /// </summary>
    /// <remarks>
    /// Nothing in the library reads it. A test watches the array behind it through a weak reference to see that disposing a
    /// document lets go of it: the memory of the whole process, which a shared pool's trim moves by tens of megabytes between
    /// two measures, cannot say so (#307). It looks the stream up, and never enumerates the streams kept, whose order the
    /// seeded hash sets (#155).
    /// </remarks>
    internal ReadOnlyMemory<byte> KeptObjectStreamData(int number) =>
        _objectStreams.TryGetValue(number, out var contents) && contents is not null ? contents.Data : default;

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

        // A rebuild a load set off and could not finish, the load having thrown, is finished before anything is read; and so
        // is one the source failed, which is run again.
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
        catch (Exception) when (_unfinishedRebuild is RebuildAwaitingFinish)
        {
            // A load threw while a rebuild a load set off is unfinished: the outermost finishes it before the exception
            // leaves, so that the index stays the one a rebuild run first gives. Decided here, not in the filter, which runs
            // before the loads this one is part of have let go of their numbers. A rebuild whose scan the source failed is
            // the exception itself, and is left to the next read, as a finish the source fails is.
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
    /// read for its guard to be reached — unless what read it was a place the section may have been moved to, which the
    /// chain dropped (<see cref="ForgetReadThrough"/>). The objects read through it nest as loads do, within
    /// <see cref="MaxNestedLoads"/>.
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
    /// Gets how many bytes the searches for <c>endstream</c>, and the proofs that what they found is a stream's own, have
    /// read so far: what <see cref="EndStreamSearchPasses"/> bounds.
    /// </summary>
    internal long EndStreamSearchBytes => _endStreamSearchBytes;

    /// <summary>
    /// Determines whether the object <paramref name="reference"/> leads to — through a chain of references too, as
    /// <see cref="PdfReference.Resolve"/> follows it — is one a guard cut, or left out of the index: what it holds may lie
    /// past the cut, or is unknown, and is the guard's until the guard is lifted, rather than a value the file gave
    /// (#224, #263). Call it once the reference is resolved.
    /// </summary>
    /// <param name="reference">The reference, as the file wrote it.</param>
    /// <param name="objects">How many objects its chain is followed through (<see cref="PdfReference.Target"/>).</param>
    internal bool LeftByGuard(PdfReference reference, int objects = PdfReference.MaxChainLength - 1) =>
        IsLeftByGuard(reference.Target(objects).Number);

    /// <summary>
    /// Determines whether object <paramref name="number"/> is one a guard cut, or left out of the index: what
    /// <see cref="LeftByGuard"/> asks of the object a chain of references leads to, for one who followed the chain
    /// already — the validator's walk of the object streams' dependencies, which follows it as decoding does (#277).
    /// </summary>
    internal bool IsLeftByGuard(int number) => IsCutAtLimit(number) || GetPresence(number) == ObjectPresence.Unindexed;

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
                numbers = new HashSet<int>(SeededComparer.Instance);
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
    /// Reports object <paramref name="number"/>, which the validator's walks of the object streams did not load for want of
    /// stack and read as null (#312), placed as <see cref="ReportNestingTooDeep"/> places an object; and, of a walk working
    /// out the standing of object stream <paramref name="unlike"/>, that it takes that stream for one that does not decode
    /// alike in every read order.
    /// </summary>
    /// <remarks>
    /// Once a document, as the reader's own cut is. A validation run from a thread short of stack cuts the walk of each
    /// object stream about as deep: reported for each, as many as the index names object streams, the cuts would fill the
    /// diagnostics' capacity, 1,000 by default, with one fact about the thread, crowding out what the reader found. The
    /// first cut names the object and the reason; what the cuts change is in the validator's report, which takes nothing
    /// from the diagnostics, so that a second validation from the same place gives the same report and adds nothing here.
    /// No rebuild withdraws it: the walks report between the reader's loads, and a rebuild withdraws only what was reported
    /// while it, or the loads it ran with, were in flight.
    /// </remarks>
    /// <param name="number">The object the walk did not load.</param>
    /// <param name="unlike">The object stream whose standing the walk was working out; null for the walk that judges.</param>
    internal void ReportWalkTooDeep(int number, int? unlike)
    {
        if (_walkCutReported)
        {
            return;
        }

        _walkCutReported = true;
        var position = _xref.TryGet(number, out var entry) && entry.Kind == XRefEntryKind.Regular
            ? EntryPosition(entry)
            : -1;

        _diagnostics.Warn(
            PdfDiagnosticCodes.SyntaxDepthExceeded,
            unlike is { } stream
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"Object {number} is reached through more nested objects than the thread's stack lets the validator follow: the validator reads it as null, and takes object stream {stream} for one that does not decode alike in every read order.")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Object {number} is reached through more nested objects than the thread's stack lets the validator follow: the validator reads it as null."),
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

    /// <summary>
    /// Closes the reader as its document is disposed: whatever asks it for an object or for a stream's data from now on
    /// is refused with an <see cref="ObjectDisposedException"/> that names the document, and what it kept — the objects
    /// it cached, the object streams it decoded, its index, the memo of its scan, what it noted of each object it read,
    /// the source — is let go, though a reference the caller holds keeps the reader itself. Closing it again does nothing.
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
        _scanMemo = null;
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
        _endObjStarts.Clear();
        _endObjStarts.TrimExcess();
        _endStreamFollowers.Clear();
        _endStreamFollowers.TrimExcess();
        _placedStarts.Clear();
        _placedStarts.TrimExcess();
        _endStreamKeywords.Clear();
        _endStreamKeywords.TrimExcess();
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
}
