using System.Globalization;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.IO.Filters;
using AdCodicem.Pdf.IO.XRef;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Validation;

/// <summary>
/// Which object streams need, to be read, an object that only reading them — or reading another stream that needs
/// them in turn — can give: a <c>/Length</c>, <c>/Type</c>, <c>/N</c>, <c>/First</c>, <c>/Filter</c> or
/// <c>/DecodeParms</c> leading to an object the stream holds (#51).
/// </summary>
/// <remarks>
/// <para>
/// It is worked out from the stream dictionaries and the index, not from what the reader happened to do: each stream the
/// index uses as an object stream, in the order of its number, is walked as the reader decodes it to read one of its
/// objects, and what that read needs is recorded in the order the reader needs it (#277). The stream's parse resolves its
/// <c>/Length</c>; the reader then resolves its <c>/Type</c>, and of a stream of <c>/Type /ObjStm</c> its <c>/N</c> and its
/// <c>/First</c>; and only when they let it decode — a count above 0, a start not below 0 — what
/// <see cref="FilterChain"/> reads of its filters and their parameters, through an <see cref="IFilterValueReader"/> that
/// records what each value leads to: the <c>/Filter</c> entry and its names up to the first that ends the chain, of
/// <c>/DecodeParms</c> only what a Flate or LZW step reads, and nothing past a value whose chain reaches an object a guard
/// cut, or left out of the index, where decoding ends (#263) — asked of the object the walk's own chain reached, not by
/// following the chain again. Each value is followed as <see cref="PdfReference.Resolve"/> follows it — as many objects as
/// it loads, the value of the last dropped (#173), a <c>/Length</c> one object further, which the parse loads before it
/// resolves what it gives —, and nothing written inside an array or a dictionary is read but what decoding reads of it.
/// </para>
/// <para>
/// An object a chain loads reads as the reader serves it while the stream is decoded. One the stream holds reads as null,
/// and is a need. One another object stream holds is served as that stream's decoding gives it, which the walk follows only
/// when the holder is self-contained: its own walk, as the reader decodes it — its <c>/Length</c>, <c>/Type</c>,
/// <c>/N</c>, <c>/First</c> and filters, and the streams met on their chains —, meets no object of an object stream, its
/// own included. Its decoding is then the same whatever stream is in flight and whatever was read before, and the value
/// the reader serves is the one the walked stream's decoding reads: it is followed, and needs nothing, a stream that needs
/// none lying on no loop. Of any other holder, the walk cannot vouch for the value: what its decoding gives may depend on
/// which stream of a loop the reader decoded first, and on what a caller read before (#277's review). The object is then
/// a need, and reads as null, as an object of a stream in flight does: a stream keeps at most one need for each value its
/// decoding reads. Whether a stream is self-contained is worked out once a validation, the first time an object of it is
/// met, by a walk of that stream that follows no object of an object stream — one it meets is enough —, and so never asks
/// it of another stream: what it answers is the same whatever was walked or read before. A
/// stream met on the way — a parameter, or a link of a chain, which ISO 32000-1 allows neither — gives its dictionary, and
/// its parse resolves its <c>/Length</c>, a level deeper, once a walk, as the reader keeps a stream it parsed; an object
/// it holds reads as null meanwhile, before it is decoded, and is its need, not the walked stream's.
/// </para>
/// <para>
/// A loop that closes only through the value of an object whose holder is not self-contained is left unseen, as the review
/// of #277 decided: a stream needs an object of a second, whose decoding meets an object of a third, and that value names
/// an object the first holds. The reader reports it as it decodes the first; the walk, which does not follow the value,
/// does not find the first circular.
/// </para>
/// <para>
/// Of a stream that is not of <c>/Type /ObjStm</c>, only what its <c>/Type</c> needs judges it (#274): a loop through its
/// <c>/Type</c> is why it is not taken for an object stream, and is the fault; a loop through its <c>/Length</c> alone
/// leaves its objects lost to its type all the same, which <see cref="PdfValidationRuleIds.XRefObjectStreamBroken"/>
/// says. What its <c>/Length</c> needs still links it to the streams it needs: the reader parses it as it serves one of
/// its objects, and a stream whose decoding reads that object goes through the parse, and is circular when the parse
/// leads back to it.
/// </para>
/// <para>
/// Loads nest as they do when one of the stream's objects is the first read: the object is the first load in flight, the
/// stream the second, the objects of its <c>/Length</c> the third, those of its other values the second again, and a
/// stream met on a chain resolves its <c>/Length</c> a level below the chain. A load past
/// <see cref="PdfFileReader.MaxNestedLoads"/> reads as null; so does an object another stream holds at that depth, that
/// stream being loaded a level deeper; one the walked stream holds is a need at any depth, the reader reporting it before
/// it counts. No recursion goes deeper than twice that bound: the walk that works out whether a stream is self-contained
/// runs within the walk that met one of its objects, and follows no object of an object stream itself. What the reader has
/// read and kept is not modeled, so that the verdict does not depend on what was read before: a read that reaches the
/// stream through another stream's decoding nests deeper, and cuts earlier; nor is the thread's stack, which the reader
/// asks as well.
/// </para>
/// <para>
/// The streams so needed are linked, each to each stream it needs, and a stream its links lead back to is circular: one
/// that needs itself, or whose strongly connected component in that graph holds another stream, each of the two leading to
/// the other (#168). The components are worked out once, and only when a stream needs one, by Tarjan's algorithm, in time
/// and memory linear in the streams and their needs: a search from each need of each stream, which they replace, cost the
/// square of a chain's length. Each stream keeps, of the objects of each stream it needs, the first it needs: another of
/// the same stream says nothing more of a loop. Its finding names the first of them, in the order the reader needs them,
/// whose stream is itself or lies in its component — the first that leads back to it —, among those that judge it.
/// </para>
/// <para>
/// It reads each stream's dictionary, and the objects written directly in the file its values lead through, where the
/// chain's index places them — not through the index the reader reads with now, which a rebuild may have made another. The
/// chain's index is read again for each lookup: until the reader first changes its own, the two are one table (#128). What
/// such an object gives the walks is read once a validation, however many streams lead through it, under whatever
/// generation, and kept by object number (#201): what decoding may read of it — a scalar or a reference as it is, of a
/// dictionary the parameters a filter reads, of an array as many elements as a chain of filters reads, of a stream its
/// parameters and its <c>/Length</c> —, and, of one whose value is a reference, the run of such objects a chain loads from
/// it, each a reference to the next and none a stream, which needs nothing and is taken at once, however many streams
/// share the chain. An object a self-contained object stream holds is kept as well, as the reader serves it, and a run
/// goes on through it once the walk knows its holder to be self-contained: what it gives is the same whatever stream is
/// walked. An object a run passes over is not kept for itself: a run keeps where it leads. As many objects are kept as the
/// reader keeps in its cache, an object let go read again when met again. A value a stream reads again — the parameters
/// each step of a chain of filters reads — is followed once a stream, as the reader keeps what it loaded. As it holds each
/// dictionary, it also notes the integers of its header written as reals.
/// </para>
/// </remarks>
internal sealed class ObjectStreamDependencies
{
    /// <summary>
    /// How many objects a stream's parse loads for its <c>/Length</c>: the one it names, then as many as a reference
    /// resolves through, the value of the last dropped.
    /// </summary>
    private const int LengthLinks = PdfReference.MaxChainLength + 1;

    /// <summary>
    /// The furthest object of a chain whose value <see cref="PdfReference.Resolve"/> takes: the one decoding asks whether
    /// a guard cut, when the chain runs that far (<see cref="PdfFileReader.LeftByGuard"/>).
    /// </summary>
    private const int TargetLink = PdfReference.MaxChainLength - 1;

    /// <summary>
    /// How deep the reader loads the values it reads of an object stream before its data, once the stream is parsed: one
    /// level below the object read, as the stream itself.
    /// </summary>
    private const int ValueDepth = 2;

    /// <summary>How deep the stream's parse loads its <c>/Length</c>: one level below the stream.</summary>
    private const int LengthDepth = ValueDepth + 1;

    /// <summary>The keys of an object stream that ISO 32000-1 makes integers (Tables 5 and 16).</summary>
    private static readonly PdfName[] IntegerKeys = [PdfName.N, PdfName.First, PdfName.Length];

    // The keys a need is found under, as a finding quotes them: written once, rather than once for each need.
    private static readonly string LengthKey = FileQuote.Name(PdfName.Length);
    private static readonly string TypeKey = FileQuote.Name(PdfName.Type);
    private static readonly string CountKey = FileQuote.Name(PdfName.N);
    private static readonly string FirstKey = FileQuote.Name(PdfName.First);
    private static readonly string FilterKey = FileQuote.Name(PdfName.Filter);
    private static readonly string ParametersKey = FileQuote.Name(PdfName.DecodeParms);

    private ObjectStreamDependencies(int room)
    {
        Circular = new(room);
        IntegersWrittenAsReals = new(room);
    }

    /// <summary>Gets the circular object streams, one finding each, in the order of their numbers.</summary>
    public ProbeFindings Circular { get; }

    /// <summary>Gets the numbers of the circular object streams.</summary>
    public HashSet<int> CircularStreams { get; } = new(SeededComparer.Instance);

    /// <summary>
    /// Gets the object streams whose <c>/N</c>, <c>/First</c> or <c>/Length</c> is a real number with no fractional part,
    /// where ISO 32000-1 asks for an integer (Tables 5 and 16): one finding per stream and key, in the order of their
    /// numbers. The reader reads each as the integer it equals (#215).
    /// </summary>
    public ProbeFindings IntegersWrittenAsReals { get; }

    /// <summary>
    /// Works out which object streams of <paramref name="document"/> are circular, writing of each kind of fault the first
    /// <paramref name="room"/> it finds and counting the rest: the room the report has when the walk is made, or every
    /// fault outside a validation. <see cref="CircularStreams"/> holds every circular stream, whatever the room.
    /// </summary>
    public static ObjectStreamDependencies Run(PdfDocument document, int room = int.MaxValue)
    {
        var result = new ObjectStreamDependencies(room);
        var reader = document.Reader;
        var streams = new SortedSet<int>();

        foreach (var (_, entry) in IndexOf(reader))
        {
            if (entry.Kind == XRefEntryKind.Compressed)
            {
                streams.Add(entry.ObjectStreamNumber);
            }
        }

        if (streams.Count == 0)
        {
            return result;
        }

        // The streams that need an object stream, in the order of their numbers, each with where the chain's row places it
        // and the first object of each stream it needs, another or itself.
        var needing = new List<Needing>();
        var walk = new Walk(document, new Memo(document, reader.CacheCapacity, reader.FilterChainBound), followsMembers: true);

        foreach (var stream in streams)
        {
            if (!IndexOf(reader).TryGet(stream, out var entry) || entry.Kind != XRefEntryKind.Regular ||
                Read(document, new PdfObjectId(stream, entry.Generation), entry) is not PdfStream objectStream)
            {
                continue;
            }

            var isObjectStream = walk.Run(stream, objectStream.Dictionary);

            if (walk.TakeNeeds(out var judging) is { } found)
            {
                needing.Add(new Needing(stream, Location(reader, stream, entry), found, judging));
            }

            // A stream that is not of /Type /ObjStm serves no object, to the reader, the probe or the rebuild alike (#274):
            // none of its values is read as an integer.
            if (!isObjectStream)
            {
                continue;
            }

            foreach (var key in IntegerKeys)
            {
                if (objectStream.Dictionary.GetRaw(key)?.Resolve() is PdfReal real && real.AsInteger() is not null &&
                    result.IntegersWrittenAsReals.Take())
                {
                    result.IntegersWrittenAsReals.Keep(new ProbeFinding(
                        Location(reader, stream, entry),
                        Invariant($"Object stream {stream} gives its {FileQuote.Name(key)} as a real number, where Table {(key == PdfName.Length ? 5 : 16)} of ISO 32000-1 asks for an integer; the reader read it as the integer it equals.")));
                }
            }
        }

        // A stream that needs none leads nowhere, and lies on no loop: with none needing one, none is circular.
        if (needing.Count == 0)
        {
            return result;
        }

        var places = new Dictionary<int, int>(needing.Count, SeededComparer.Instance);

        for (var place = 0; place < needing.Count; place++)
        {
            places.Add(needing[place].Stream, place);
        }

        var components = Components(needing, places);

        for (var place = 0; place < needing.Count; place++)
        {
            var (stream, location, own, judging) = needing[place];

            if (WayBack(components[place], own, judging, places, components) is not { } need)
            {
                continue;
            }

            // Every circular stream is known, for the rule that passes over them; its finding is written while there is
            // room (#168).
            result.CircularStreams.Add(stream);

            if (result.Circular.Take())
            {
                result.Circular.Keep(new ProbeFinding(
                    location,
                    need.Stream == stream
                        ? Invariant($"Object stream {stream} needs object {need.Object}, which it holds, to be read: its {need.Key} names it, and the object cannot be read before the stream is.")
                        : Invariant($"Object stream {stream} needs object {need.Object}, which object stream {need.Stream} holds, to be read: its {need.Key} names it, and reading object stream {need.Stream} needs object stream {stream} in turn.")));
            }
        }

        return result;
    }

    /// <summary>
    /// Gives the first of <paramref name="own"/>, a stream's needs in the order the reader needs them, from
    /// <paramref name="judging"/> on, whose stream lies in <paramref name="component"/>, the stream's own — the stream itself
    /// among them —: the first that leads back to it, directly or through other streams; null when none does, and the
    /// stream is not circular.
    /// </summary>
    /// <remarks>
    /// A need leads back when the stream it names is the stream itself, or leads to it in turn: the two then lead to each
    /// other, which is what lying in one component means (#168). A stream that needs none has no place, and leads nowhere.
    /// </remarks>
    /// <param name="component">The component of the stream whose needs <paramref name="own"/> are.</param>
    /// <param name="own">The stream's needs, the first of each stream it needs, in the order the reader needs them.</param>
    /// <param name="judging">
    /// Where the needs that judge the stream start in <paramref name="own"/>: past those of its <c>/Length</c>, of a stream
    /// that is not of <c>/Type /ObjStm</c> (#274); 0 otherwise.
    /// </param>
    /// <param name="places">The place of each stream that needs one, by which <paramref name="components"/> is indexed.</param>
    /// <param name="components">The component of each stream that needs one, by its place.</param>
    private static Need? WayBack(int component, List<Need> own, int judging, Dictionary<int, int> places, int[] components)
    {
        for (var index = judging; index < own.Count; index++)
        {
            if (places.TryGetValue(own[index].Stream, out var place) && components[place] == component)
            {
                return own[index];
            }
        }

        return null;
    }

    /// <summary>
    /// Gives the strongly connected component of each stream of <paramref name="needing"/>, by its place there, in the graph
    /// the needs make, each stream linked to each stream it needs: two streams share one when each leads to the other. They
    /// are numbered from 1, in the order they close.
    /// </summary>
    /// <remarks>
    /// It is Tarjan's algorithm (1972), which takes each stream and each need once (#168): a walk in depth from each stream
    /// not reached yet, which keeps, of each stream, the order it was reached in and the earliest-reached stream still open
    /// that it leads to; a stream that leads to none reached before it closes a component, of itself and the streams reached
    /// since that are still open. Its frames are kept on a stack of its own, not the thread's, which a chain of streams as
    /// long as an index allows would exhaust (invariant 4): each of its arrays holds each stream once at most, and has room
    /// for as many as there are streams that need one. A stream that needs none is not taken: it leads nowhere.
    /// </remarks>
    /// <param name="needing">The streams that need an object stream, each with the first object of each stream it needs.</param>
    /// <param name="places">The place in <paramref name="needing"/> of each stream it holds.</param>
    private static int[] Components(List<Needing> needing, Dictionary<int, int> places)
    {
        var count = needing.Count;

        // The order each stream was reached in, from 1, 0 for one not reached yet; of the streams still open, the earliest
        // reached it leads to; and its component, 0 while it is open — reached, and not yet closed into one.
        var reached = new int[count];
        var earliest = new int[count];
        var components = new int[count];

        // The streams still open, the last reached on top; and the walk's frames, each a stream and how many of its needs
        // were taken, the stream being walked on top.
        var open = new int[count];
        var frames = new int[count];
        var taken = new int[count];
        var order = 0;
        var closed = 0;
        var top = 0;

        for (var root = 0; root < count; root++)
        {
            if (reached[root] != 0)
            {
                continue;
            }

            frames[0] = root;
            taken[0] = 0;
            var depth = 1;

            while (depth > 0)
            {
                var place = frames[depth - 1];

                // A stream framed is walked at once: on top for the first time, it is reached.
                if (reached[place] == 0)
                {
                    reached[place] = earliest[place] = ++order;
                    open[top++] = place;
                }

                var own = needing[place].Needs;

                if (taken[depth - 1] < own.Count)
                {
                    if (places.TryGetValue(own[taken[depth - 1]++].Stream, out var next))
                    {
                        if (reached[next] == 0)
                        {
                            frames[depth] = next;
                            taken[depth++] = 0;
                        }
                        else if (components[next] == 0)
                        {
                            // Reached and still open: it leads back to the stream being walked, or to one framed below it.
                            earliest[place] = Math.Min(earliest[place], reached[next]);
                        }
                    }

                    continue;
                }

                // Every need taken: a stream that leads to no stream still open reached before it closes a component.
                depth--;

                if (earliest[place] == reached[place])
                {
                    closed++;
                    int member;

                    do
                    {
                        member = open[--top];
                        components[member] = closed;
                    }
                    while (member != place);
                }

                // What it leads to, the stream that framed it leads to as well.
                if (depth > 0)
                {
                    var caller = frames[depth - 1];
                    earliest[caller] = Math.Min(earliest[caller], earliest[place]);
                }
            }
        }

        return components;
    }

    /// <summary>
    /// Gives the index the walk judges: the chain's, read again for each lookup, since the reader copies it before it first
    /// changes its own; or the one rebuilt as the document opened, when the chain gave none.
    /// </summary>
    private static PdfXRefTable IndexOf(PdfFileReader reader) => reader.ChainIndex ?? reader.Index;

    /// <summary>
    /// Reads the object a regular <paramref name="entry"/> of the chain's index places, where it places it: a rebuild since
    /// may serve another definition of the number, or none (#128). Where the chain places nothing of that number — a row
    /// outside the file, or one with nothing at or near it —, and when the chain gave no index, the object is loaded as any
    /// read loads it, through the index the reader reads with now.
    /// </summary>
    private static PdfObject Read(PdfDocument document, PdfObjectId id, XRefEntry entry) =>
        (document.Reader.ChainIndex is null ? null : document.Reader.ReadWhereChainPlaces(id.Number, entry)) ??
        document.GetObject(id);

    /// <summary>
    /// Locates object stream <paramref name="stream"/> where <paramref name="entry"/>, the chain's row the walk read it
    /// through, places it — the row as the file wrote it, which the reader copies before it first changes its own: a load
    /// that relocates the stream, or rebuilds the index, changes the live entry, not the row (#118, #128). A row that
    /// places the stream outside the file, which the reader found elsewhere, places it nowhere: the finding is located at
    /// the object alone (#125).
    /// </summary>
    private static PdfValidationLocation Location(PdfFileReader reader, int stream, XRefEntry entry) =>
        At(reader, new PdfObjectId(stream, entry.Generation), reader.PositionOf(entry));

    /// <summary>Locates object <paramref name="id"/> at <paramref name="position"/>, or at the object alone outside the file.</summary>
    private static PdfValidationLocation At(PdfFileReader reader, PdfObjectId id, long position) =>
        reader.IsInFile(position) ? PdfValidationLocation.OfObject(id, position) : PdfValidationLocation.OfObject(id);

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    /// <summary>An object a stream needs to be read, the object stream holding it, and the key that leads to it.</summary>
    private readonly record struct Need(int Stream, int Object, string Key);

    /// <summary>
    /// An object stream that needs an object stream to be read, another or itself: where the chain's row places it, and the
    /// first object of each stream it needs, in the order the reader needs them.
    /// </summary>
    /// <param name="Stream">The stream's number.</param>
    /// <param name="Location">Where the chain's row places it.</param>
    /// <param name="Needs">
    /// The first object of each stream it needs, in the order the reader needs them: every one links it to that stream in the
    /// graph the needs make.
    /// </param>
    /// <param name="Judging">
    /// Where the needs that judge the stream itself start in <see cref="Needs"/>: 0 for an object stream; past those of its
    /// <c>/Length</c> for a stream that is not of <c>/Type /ObjStm</c>, which link it to the streams they name but judge
    /// nothing, a loop through them leaving it broken rather than circular (#274). Each part keeps the first need of each
    /// stream on its own.
    /// </param>
    private readonly record struct Needing(int Stream, PdfValidationLocation Location, List<Need> Needs, int Judging);

    /// <summary>
    /// The walk of one object stream at a time, as the reader decodes it to read one of its objects: what that read loads,
    /// and which objects of object streams it needs, in the order it needs them (#277). One serves every stream of a
    /// validation, what it holds for one let go before the next.
    /// </summary>
    /// <param name="document">The document whose object streams are walked.</param>
    /// <param name="memo">What each object written in the file, or a self-contained object stream holds, gives the walks.</param>
    /// <param name="followsMembers">
    /// Whether the walk follows the value of an object a self-contained object stream holds: the walk that judges the
    /// streams does; the one that works out whether a stream is self-contained does not, any object of an object stream it
    /// meets being a need, and enough to tell.
    /// </param>
    private sealed class Walk(PdfDocument document, Memo memo, bool followsMembers)
    {
        /// <summary>The needs met, in the order they were met, each stream's first kept when the walk ends.</summary>
        private readonly List<Need> _found = [];

        /// <summary>The streams <see cref="TakeNeeds"/> kept a need of.</summary>
        private readonly HashSet<int> _needed = new(SeededComparer.Instance);

        /// <summary>
        /// The streams met on the way whose parse is resolving their <c>/Length</c>: an object one of them holds cannot be
        /// read from it meanwhile, the reader refusing to load again a stream it is loading.
        /// </summary>
        private readonly HashSet<int> _inFlight = new(SeededComparer.Instance);

        /// <summary>
        /// The streams whose parse the walk followed, the walked stream first: the reader keeps a stream it parsed, and does
        /// not parse it again.
        /// </summary>
        private readonly HashSet<int> _parsed = new(SeededComparer.Instance);

        /// <summary>What the values read once the stream is parsed led to, by the number of the object each names.</summary>
        private readonly Dictionary<int, Followed> _values = new(SeededComparer.Instance);

        /// <summary>
        /// The walk that works out whether an object stream is self-contained, which follows no member; made the first time
        /// the walk that judges the streams meets a member of one whose status is not known yet.
        /// </summary>
        private Walk? _containment;

        /// <summary>The object stream walked, whose objects read as null while it is decoded.</summary>
        private int _stream;

        /// <summary>
        /// Where the needs met past the stream's <c>/Length</c> start in <see cref="_found"/>, of a stream that is not of
        /// <c>/Type /ObjStm</c>, whose <c>/Length</c> needs judge nothing (#274); 0 for an object stream.
        /// </summary>
        private int _judging;

        /// <summary>
        /// Gets a value indicating whether the walk is done: one that follows no member has met an object of an object stream,
        /// which is all it asks.
        /// </summary>
        public bool IsDone => !followsMembers && _found.Count > 0;

        /// <summary>
        /// Follows what the reader reads of object stream <paramref name="stream"/>, whose dictionary is
        /// <paramref name="dictionary"/>, before any of its data: its <c>/Length</c> as its parse resolves it, its
        /// <c>/Type</c>, and of an object stream its <c>/N</c>, its <c>/First</c> and, when they let it decode, what
        /// decoding reads of its filters and their parameters.
        /// </summary>
        /// <returns>Whether the reader takes the stream for an object stream: its <c>/Type</c> leads to <c>/ObjStm</c>.</returns>
        public bool Run(int stream, PdfDictionary dictionary)
        {
            _stream = stream;
            _found.Clear();
            _inFlight.Clear();
            _parsed.Clear();
            _values.Clear();
            _judging = 0;

            // The stream is parsed as one of its objects is read, a level below it; the parse resolves its /Length a level
            // deeper, and the reader keeps the stream, so that it is not parsed again. An object it holds is read as null
            // whatever is in flight.
            _parsed.Add(stream);
            Follow(dictionary.GetRaw(PdfName.Length), LengthKey, LengthLinks, LengthDepth, out _);
            var ofLength = _found.Count;

            // The reader then reads its /Type, and of a stream of another type nothing more (#274). What its /Length needs
            // judges nothing of it, its objects being lost to its type whatever the parse reads; but a read of one of them
            // parses it, and leads through it to what the parse needs.
            if (FollowValue(dictionary.GetRaw(PdfName.Type), TypeKey).Value is not PdfName type || type != PdfName.ObjStm)
            {
                _judging = ofLength;
                return false;
            }

            var count = FollowValue(dictionary.GetRaw(PdfName.N), CountKey).Value.AsInteger() ?? -1;
            var first = FollowValue(dictionary.GetRaw(PdfName.First), FirstKey).Value.AsInteger() ?? -1;

            // A stream that declares no object, or whose /N or /First is not one the reader can read, is not decoded.
            if (count > 0 && first >= 0)
            {
                var recorder = new Recorder(this);
                FilterChain.TryFollow(dictionary, document.Reader.FilterChainBound, ref recorder);
            }

            return true;
        }

        /// <summary>
        /// Gives the needs of the stream walked last, the first of each stream it needs, in the order the reader needs them;
        /// null when it needs none.
        /// </summary>
        /// <param name="judging">
        /// Where those that judge the stream itself start: 0 for an object stream; past the needs of its <c>/Length</c> for a
        /// stream that is not of <c>/Type /ObjStm</c>, which link it to the streams they name but judge nothing (#274). Each
        /// part keeps the first need of each stream on its own.
        /// </param>
        public List<Need>? TakeNeeds(out int judging)
        {
            var kept = Distinct(0, _judging, null);
            judging = kept?.Count ?? 0;
            return Distinct(_judging, _found.Count, kept);
        }

        /// <summary>
        /// Works out whether object stream <paramref name="stream"/> is self-contained, read where the chain's row places it,
        /// as <see cref="ObjectStreamDependencies.Run"/> reads each stream: its walk meets no object of an object stream, its
        /// own included, so that its decoding is the same whatever stream is in flight and whatever was read before. One that
        /// is no stream there is not: whatever the reader comes to serve of it is no decoding the walk followed.
        /// </summary>
        public bool MeetsNoMember(int stream)
        {
            if (!IndexOf(document.Reader).TryGet(stream, out var entry) || entry.Kind != XRefEntryKind.Regular ||
                Read(document, new PdfObjectId(stream, entry.Generation), entry) is not PdfStream objectStream)
            {
                return false;
            }

            Run(stream, objectStream.Dictionary);
            return _found.Count == 0;
        }

        /// <summary>
        /// Follows <paramref name="written"/>, a value the reader reads of the stream once it is parsed — its <c>/Type</c>,
        /// <c>/N</c> or <c>/First</c>, or one decoding reads —, as <see cref="Follow"/> does, under <paramref name="key"/>;
        /// once a walk for each object it names. The reader keeps what it loaded: a value read again — the parameters each
        /// step of a chain of filters reads, the element each names — loads nothing more, and needs nothing it did not.
        /// </summary>
        /// <param name="written">The value as the file wrote it.</param>
        /// <param name="key">The key of the stream's dictionary the value is read for, as a finding quotes it.</param>
        public Followed FollowValue(PdfObject? written, string key)
        {
            if (written is not PdfReference reference)
            {
                return new Followed(written, LeftByGuard: false);
            }

            if (!_values.TryGetValue(reference.Id.Number, out var followed))
            {
                // Whether a guard cut the object the chain reached, or left it out of the index, is asked once the chain is
                // followed, of that object (#263): what the walk read, or the reader served, of it was read by then.
                var value = Follow(written, key, PdfReference.MaxChainLength, ValueDepth, out var target);
                followed = new Followed(value, document.Reader.IsLeftByGuard(target));
                _values[reference.Id.Number] = followed;
            }

            return followed;
        }

        /// <summary>
        /// Follows <paramref name="written"/> as <see cref="PdfReference.Resolve"/> does — through
        /// <paramref name="links"/> objects at most, each loaded at <paramref name="depth"/>, the value of the last dropped
        /// (#173) —, recording what the objects it loads need, under <paramref name="key"/>; and gives the value the reader
        /// reads.
        /// </summary>
        /// <param name="written">The value as the file wrote it.</param>
        /// <param name="key">The key of the stream's dictionary the value is read for, as a finding quotes it.</param>
        /// <param name="links">How many objects the chain loads at most.</param>
        /// <param name="depth">How deep each of them is loaded.</param>
        /// <param name="target">
        /// The object <see cref="PdfReference.Target"/> gives of the chain — the last whose value the chain took, the
        /// 31st at most —, which decoding asks whether a guard cut; 0 when <paramref name="written"/> is no reference.
        /// </param>
        public PdfObject? Follow(PdfObject? written, string key, int links, int depth, out int target)
        {
            target = 0;

            if (written is not PdfReference reference)
            {
                return written;
            }

            var number = reference.Id.Number;

            // A run through objects self-contained object streams hold is taken where such an object is read as its holder
            // serves it: by the walk that follows them, within the reader's nesting, its holder loaded a level deeper. No
            // such run passes an object the walked stream holds, nor one of a stream whose /Length is being resolved: the
            // holder's own walk would meet it — the walk of the stream walked, or that /Length, which the holder's walk
            // resolves too, a level higher —, and the holder would not be self-contained.
            var throughMembers = followsMembers && depth < PdfFileReader.MaxNestedLoads;

            for (var loaded = 0; loaded < links;)
            {
                // Object `number` is the next the chain loads. A run of links, each a reference to the next and none a stream,
                // needs nothing: it is taken at once, as far as the chain loads it; elsewhere the chain goes on an object at
                // a time.
                if (depth <= PdfFileReader.MaxNestedLoads && memo.RunFrom(number, links - loaded, throughMembers) is { } run &&
                    (throughMembers || !run.PassesMembers))
                {
                    var taken = Math.Min(run.Numbers.Length, links - loaded);

                    if (loaded < TargetLink)
                    {
                        target = run.Numbers[Math.Min(taken, TargetLink - loaded) - 1];
                    }

                    loaded += taken;
                    number = run.Next;
                    continue;
                }

                loaded++;

                if (loaded <= TargetLink)
                {
                    target = number;
                }

                var value = Load(number, key, depth);

                // Resolve drops the value of the last object it loads (#173).
                if (loaded < links)
                {
                    if (value is not PdfReference next)
                    {
                        return value;
                    }

                    number = next.Id.Number;
                }
            }

            return PdfNull.Instance;
        }

        /// <summary>
        /// Keeps, of the needs in <see cref="_found"/> from <paramref name="start"/> to <paramref name="end"/>, the first of
        /// each stream, in order, adding them to <paramref name="kept"/>; gives what it holds, null when it is none.
        /// </summary>
        private List<Need>? Distinct(int start, int end, List<Need>? kept)
        {
            _needed.Clear();

            for (var index = start; index < end; index++)
            {
                if (_needed.Add(_found[index].Stream))
                {
                    (kept ??= []).Add(_found[index]);
                }
            }

            return kept;
        }

        /// <summary>
        /// Loads object <paramref name="number"/> as the reader loads it at <paramref name="depth"/> while the stream is
        /// decoded, recording, under <paramref name="key"/>, what it needs.
        /// </summary>
        private PdfObject Load(int number, string key, int depth)
        {
            // Object 0 heads the free list and is never in use (ISO 32000-1, 7.5.4): the reader never reads it, whatever the
            // index's first entry says, so no stream needs it.
            if (number <= 0 || !IndexOf(document.Reader).TryGet(number, out var entry))
            {
                return PdfNull.Instance;
            }

            if (entry.Kind == XRefEntryKind.Compressed)
            {
                return Member(number, entry, key, depth);
            }

            // Past the reader's nesting, a load reads as null.
            if (entry.Kind != XRefEntryKind.Regular || depth > PdfFileReader.MaxNestedLoads)
            {
                return PdfNull.Instance;
            }

            var held = memo.Get(number, entry);

            // A stream's parse resolves its /Length a level deeper, while it is in flight; once, as the reader keeps it. One met
            // again while in flight, which the reader reads as null, is met on the chain of a /Length, whose value the walk
            // drops: what it gives there changes nothing.
            if (held.IsStream && _parsed.Add(number))
            {
                _inFlight.Add(number);
                Follow(held.Length, key, LengthLinks, depth + 1, out _);
                _inFlight.Remove(number);
            }

            return held.Value;
        }

        /// <summary>
        /// Loads object <paramref name="number"/>, which object stream <paramref name="entry"/> names holds, as the reader
        /// loads it at <paramref name="depth"/> while the stream walked is decoded.
        /// </summary>
        private PdfObject Member(int number, XRefEntry entry, string key, int depth)
        {
            var holder = entry.ObjectStreamNumber;

            // One the walked stream holds reads as null while it is decoded, and is reported before the reader counts the
            // depth (#51).
            if (holder == _stream)
            {
                _found.Add(new Need(holder, number, key));
                return PdfNull.Instance;
            }

            // Another's stream is loaded a level deeper: past the reader's nesting, or while it is in flight — a stream met on
            // the way, its /Length being resolved —, the object reads as null before that stream is decoded.
            if (depth >= PdfFileReader.MaxNestedLoads || _inFlight.Contains(holder))
            {
                return PdfNull.Instance;
            }

            // Otherwise the reader decodes that stream, within its own guards and budget, and serves the object. A
            // self-contained stream decodes alike whoever reads it, and needs no stream: the object is read as the reader
            // serves it, and followed. Of another, what the reader serves may depend on what it decoded first (#277's
            // review): the object is a need, and reads as null, as one of a stream in flight does.
            if (!followsMembers || !IsSelfContained(holder))
            {
                _found.Add(new Need(holder, number, key));
                return PdfNull.Instance;
            }

            return memo.Get(number, entry).Value;
        }

        /// <summary>
        /// Gives whether object stream <paramref name="holder"/> is self-contained: known already, or worked out once a
        /// validation by a walk of its own that follows no member, and so asks no other stream.
        /// </summary>
        private bool IsSelfContained(int holder)
        {
            if (memo.IsSelfContained(holder) is { } known)
            {
                return known;
            }

            var selfContained = (_containment ??= new Walk(document, memo, followsMembers: false)).MeetsNoMember(holder);
            memo.NoteSelfContained(holder, selfContained);
            return selfContained;
        }
    }

    /// <summary>What a value read once a stream is parsed leads to.</summary>
    /// <param name="Value">The value the reader reads.</param>
    /// <param name="LeftByGuard">
    /// Whether its chain reached an object a guard cut, or left out of the index (<see cref="PdfFileReader.LeftByGuard"/>):
    /// decoding ends at such a value, which is the guard's rather than one the file gave (#263).
    /// </param>
    private readonly record struct Followed(PdfObject? Value, bool LeftByGuard);

    /// <summary>
    /// Records what decoding reads of a stream's filters and their parameters, in the order <see cref="FilterChain"/> reads
    /// it, each value followed by the walk; and ends the reading where decoding ends it, at a value whose chain reached an
    /// object a guard cut, or left out of the index (#263): nothing past it is needed. The walk that works out whether a
    /// stream is self-contained ends it at the first object of an object stream it meets, which tells it all it asks.
    /// </summary>
    /// <param name="walk">The walk of the stream.</param>
    private readonly struct Recorder(Walk walk) : IFilterValueReader
    {
        /// <inheritdoc/>
        public bool TryRead(PdfName key, PdfObject? written, bool entry, out PdfObject? value)
        {
            if (walk.IsDone)
            {
                value = null;
                return false;
            }

            var followed = walk.FollowValue(written, key == PdfName.Filter ? FilterKey : ParametersKey);
            value = followed.LeftByGuard ? null : followed.Value;
            return !followed.LeftByGuard;
        }
    }

    /// <summary>
    /// What each object written in the file, or held by a self-contained object stream, gives the walks, by object number —
    /// a reference's generation leads the reader to no other object —: what decoding may read of it, and the run a chain
    /// loads from it. As many objects as <paramref name="capacity"/> says, the oldest let go first, and read again when met
    /// again, so that what the walks keep follows the reader's cache rather than the file (#201). It also keeps whether each
    /// object stream the walks asked about is self-contained.
    /// </summary>
    /// <param name="document">The document whose objects are read.</param>
    /// <param name="capacity">How many objects are kept, at most: as many as the reader caches.</param>
    /// <param name="maxFilters">The most filters a stream's data passes through, which bounds what is kept of an array.</param>
    private sealed class Memo(PdfDocument document, int capacity, int maxFilters)
    {
        private readonly Dictionary<int, Held> _held = new(SeededComparer.Instance);
        private readonly Queue<int> _order = new();

        /// <summary>
        /// Whether each object stream the walks met an object of is self-contained, by its number: worked out once a
        /// validation, and kept for good, the answer being the same each time; made the first time one is, so that a walk
        /// that meets none pays nothing for it. It holds no more entries than the index names object streams, which the walk
        /// takes one by one as well: no value the file gives chooses its size.
        /// </summary>
        private Dictionary<int, bool>? _selfContained;

        /// <summary>The numbers of the run being worked out, the longest a chain loads.</summary>
        private readonly int[] _run = new int[LengthLinks];

        /// <summary>Gives whether object stream <paramref name="stream"/> is self-contained; null when that is not known yet.</summary>
        public bool? IsSelfContained(int stream) =>
            _selfContained is not null && _selfContained.TryGetValue(stream, out var selfContained) ? selfContained : null;

        /// <summary>Notes whether object stream <paramref name="stream"/> is self-contained.</summary>
        public void NoteSelfContained(int stream, bool selfContained) =>
            (_selfContained ??= new(SeededComparer.Instance))[stream] = selfContained;

        /// <summary>
        /// Gives what object <paramref name="number"/>, which <paramref name="entry"/> places, gives the walks: kept already,
        /// or read where the chain places it — or, of one a self-contained object stream holds, as the reader serves it —,
        /// and kept.
        /// </summary>
        public Held Get(int number, XRefEntry entry)
        {
            if (!_held.TryGetValue(number, out var held))
            {
                held = Project(Fetch(number, entry));
                Keep(number, held);
            }

            return held;
        }

        /// <summary>
        /// Gives the run a chain loads from object <paramref name="number"/>, worked out the first time as far as
        /// <paramref name="loads"/> objects, or to the first that is no link: null when the object is neither written in the
        /// file nor, when <paramref name="throughMembers"/> says so, held by an object stream known to be self-contained;
        /// is a stream; or holds no reference, so that loading it says more than where the chain goes on. A run kept from a
        /// chain that loaded fewer objects, or took none such streams hold, ends where that chain did; the chain that goes on
        /// past it goes on from there, as from any object it loads.
        /// </summary>
        /// <param name="number">The object the chain loads next.</param>
        /// <param name="loads">How many objects the chain loads at most from it.</param>
        /// <param name="throughMembers">Whether the chain may take at once objects self-contained object streams hold.</param>
        public LinkRun? RunFrom(int number, int loads, bool throughMembers)
        {
            if (number <= 0 || !IndexOf(document.Reader).TryGet(number, out var entry) || !IsAlikeForEveryStream(entry, throughMembers))
            {
                return null;
            }

            var held = Get(number, entry);

            if (held.IsStream || held.Value is not PdfReference)
            {
                return null;
            }

            return held.Run ??= Build(number, entry, held, loads, throughMembers);
        }

        /// <summary>
        /// Works out the run a chain loads from object <paramref name="number"/>, which <paramref name="entry"/> places and
        /// whose value <paramref name="held"/> is a reference: as far as <paramref name="loads"/> objects — no object the
        /// chain does not load is read —, or to the first object that is not such a link. A loop of links runs on to
        /// <paramref name="loads"/>, as the chain does.
        /// </summary>
        private LinkRun Build(int number, XRefEntry entry, Held held, int loads, bool throughMembers)
        {
            var count = 0;
            var passesMembers = entry.Kind == XRefEntryKind.Compressed;
            _run[count++] = number;
            var next = ((PdfReference)held.Value).Id.Number;

            while (count < loads &&
                next > 0 && IndexOf(document.Reader).TryGet(next, out var nextEntry) && IsAlikeForEveryStream(nextEntry, throughMembers))
            {
                // An object the run passes over is not kept for itself, so that the run's start, which is, outlives it. One
                // that ends the run is kept: the chain loads it next.
                var nextHeld = _held.TryGetValue(next, out var kept) ? kept : Project(Fetch(next, nextEntry));

                if (nextHeld.IsStream || nextHeld.Value is not PdfReference link)
                {
                    if (kept is null)
                    {
                        Keep(next, nextHeld);
                    }

                    break;
                }

                passesMembers |= nextEntry.Kind == XRefEntryKind.Compressed;
                _run[count++] = next;
                next = link.Id.Number;
            }

            return new LinkRun(_run.AsSpan(0, count).ToArray(), next, passesMembers);
        }

        /// <summary>
        /// Gives whether what the object <paramref name="entry"/> places gives the walks is the same whatever stream is walked,
        /// and may be kept for all of them: an object written in the file; or, when <paramref name="throughMembers"/> says so,
        /// one an object stream known to be self-contained holds.
        /// </summary>
        private bool IsAlikeForEveryStream(XRefEntry entry, bool throughMembers) => entry.Kind switch
        {
            XRefEntryKind.Regular => true,
            XRefEntryKind.Compressed => throughMembers && IsSelfContained(entry.ObjectStreamNumber) == true,
            _ => false,
        };

        /// <summary>
        /// Reads object <paramref name="number"/>, which <paramref name="entry"/> places: where the chain places it; or, one
        /// a self-contained object stream holds, as the reader serves it, which is the same whoever reads it.
        /// </summary>
        private PdfObject Fetch(int number, XRefEntry entry) =>
            entry.Kind == XRefEntryKind.Compressed
                ? document.GetObject(new PdfObjectId(number))
                : Read(document, new PdfObjectId(number, entry.Generation), entry);

        /// <summary>Keeps what object <paramref name="number"/>, which is not kept yet, gives the walks.</summary>
        private void Keep(int number, Held held)
        {
            if (_held.Count >= capacity && _order.TryDequeue(out var oldest))
            {
                _held.Remove(oldest);
            }

            _held[number] = held;
            _order.Enqueue(number);
        }

        /// <summary>
        /// Gives what decoding may read of <paramref name="value"/>, whatever reads it: a scalar or a reference as it is; of a
        /// dictionary, the entries of a step's parameters (<see cref="FilterChain.ParameterKeys"/>); of a stream, those of its
        /// dictionary, and its <c>/Length</c> apart; of an array, as many elements as a chain of filters reads — an element
        /// of <c>/Filter</c>, or of <c>/DecodeParms</c> at a step's place, past the guard is never read.
        /// </summary>
        private Held Project(PdfObject value) => value switch
        {
            PdfStream stream => new Held(Parameters(stream.Dictionary), stream.Dictionary.GetRaw(PdfName.Length), isStream: true),
            PdfDictionary dictionary => new Held(Parameters(dictionary), length: null, isStream: false),

            // No element past the bound is read, which needs no more than the bound: past it, nothing is added to it, which
            // could overflow — no bound at all is int.MaxValue (PdfReaderLimits.Unbounded), which no array's count exceeds.
            PdfArray array when array.Count > maxFilters => new Held(Head(array, maxFilters), length: null, isStream: false),
            _ => new Held(value, length: null, isStream: false),
        };

        /// <summary>Gives the entries of <paramref name="dictionary"/> a step's parameters are read from.</summary>
        private static PdfDictionary Parameters(PdfDictionary dictionary)
        {
            var parameters = new PdfDictionary();

            foreach (var key in FilterChain.ParameterKeys)
            {
                if (dictionary.GetRaw(key) is { } value)
                {
                    parameters.Set(key, value);
                }
            }

            return parameters;
        }

        /// <summary>Gives the first <paramref name="count"/> elements of <paramref name="array"/>.</summary>
        private static PdfArray Head(PdfArray array, int count)
        {
            var head = new PdfArray(count);

            for (var index = 0; index < count; index++)
            {
                head.Add(array[index]);
            }

            return head;
        }
    }

    /// <summary>What the walks keep of an object written in the file, or held by a self-contained object stream.</summary>
    /// <param name="value">What decoding may read of its value.</param>
    /// <param name="length">The <c>/Length</c> of a stream, as written; null for anything else.</param>
    /// <param name="isStream">Whether the object is a stream.</param>
    private sealed class Held(PdfObject value, PdfObject? length, bool isStream)
    {
        /// <summary>Gets what decoding may read of the object's value; of a stream, of its dictionary.</summary>
        public PdfObject Value { get; } = value;

        /// <summary>Gets the <c>/Length</c> of a stream, as written, which its parse resolves; null for anything else.</summary>
        public PdfObject? Length { get; } = length;

        /// <summary>Gets a value indicating whether the object is a stream.</summary>
        public bool IsStream { get; } = isStream;

        /// <summary>Gets or sets the run a chain loads from the object, once worked out, when its value is a reference.</summary>
        public LinkRun? Run { get; set; }
    }

    /// <summary>
    /// The objects a chain loads one after the other from an object written in the file, or held by a self-contained object
    /// stream, each a reference to the next and none a stream — 33 at most, as many as the longest chain loads —, and the
    /// object it loads after them.
    /// </summary>
    /// <param name="numbers">The objects, in the order the chain loads them.</param>
    /// <param name="next">The object the last of them names.</param>
    /// <param name="passesMembers">Whether an object stream holds one of the objects.</param>
    private sealed class LinkRun(int[] numbers, int next, bool passesMembers)
    {
        /// <summary>Gets the objects, in the order the chain loads them.</summary>
        public int[] Numbers { get; } = numbers;

        /// <summary>Gets the object the last of them names, which the chain loads next.</summary>
        public int Next { get; } = next;

        /// <summary>
        /// Gets a value indicating whether an object stream holds one of the objects: the run is then taken only where a
        /// chain reads such an object as its holder serves it, that holder loaded a level deeper within the reader's nesting.
        /// </summary>
        public bool PassesMembers { get; } = passesMembers;
    }
}
