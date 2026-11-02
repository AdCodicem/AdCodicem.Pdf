using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
/// and is a need. One another object stream holds is served as that stream's decoding gives it, which the walk follows
/// only where that stream decodes alike in every read order (#312). Its walk, as a first read of one of its objects
/// decodes it — its <c>/Length</c>, <c>/Type</c>, <c>/N</c>, <c>/First</c> and filters, the streams met on their chains,
/// and the decodings of the streams holding the objects they lead through —, then meets no object it holds; no object of
/// a stream that does not decode alike, nor of one whose standing is being worked out, which lies on a loop with it; no
/// stream met again while the reader holds it in its loads — its own while its <c>/Length</c> is resolved, one met on a
/// chain while its parse is, at the reader's deepest load as well as above it, the reader asking that first —, which the
/// reader reads as null for the rest of every decoding in flight (#51); no stream so met inside the parse of a stream met
/// again, which the walk parses again as a reader whose cache let it go between two meetings does; after a rebuild, no
/// object the reader's index places otherwise than the chain's; and no load the reader's nesting cuts whose value reaches
/// the decoding. Where its object is met, its deepest such load also stays within the reader's nesting. It then decodes
/// the same whatever stream is in flight and whatever was read before — but at the reader's deepest load, which its
/// nesting or its stack probe cuts, and where it serves what its cache kept, so that what was read before decides what
/// that load gives (#163) —, and lies on no loop: the object is read through the reader, and followed. Of any other
/// stream, the object is a need, and reads as null, as one of a stream in flight does: a stream keeps at most one need
/// for each value its decoding reads. A need on a stream that decodes alike closes no loop: a walk of a standing that
/// meets an object of a stream that does not decode alike tells its own stream unlike wherever it meets it, so that the
/// needs of a stream that decodes alike name only streams that decode alike, and the first of them to be worked out met
/// none of the others while theirs was. A stream met on the way — a parameter, or a link of a chain, which ISO 32000-1
/// allows neither — gives its dictionary, and its parse resolves its <c>/Length</c>, a level deeper; an object it holds
/// reads as null meanwhile, before it is decoded, and is its need, not the walked stream's. What that parse loads
/// places that stream's data, and nothing a decoding reads: cut there or not, a stream decodes the same, but where the
/// reader serves past the cut what its cache kept (#163).
/// </para>
/// <para>
/// Each object stream's standing is worked out once a validation, the first time an object of it is met, as a first read
/// decodes it: whether it decodes alike, and the deepest load whose value reaches its decoding. The walks that work them
/// out run one after the other, never one inside another: a walk that meets an object of a stream whose standing is not
/// known yet reads it as null, notes the stream, and is made again once the streams it noted are known, each worked out
/// first, on a stack of their own. A stream a later walk meets again is pushed again, its entry lower left stale, its stream
/// known by the time it is reached; the stack is rid of such entries, keeping the topmost entry of each stream not known
/// yet, whenever it holds more than 1,024 and twice as many as it kept the last time, so that it holds no more than twice
/// the most streams waiting at once, or 1,024 entries, whichever is more, besides one walk's unknown streams. A value
/// read as null makes a decoding read less, never more — a null link ends a chain, a null element a chain of filters, a
/// null <c>/Type</c>, <c>/N</c>, <c>/First</c>, <c>/Filter</c> or predictor what decoding would read after it —, so
/// that what such a walk meets, the decoding meets: a stream met while its own standing is being worked out lies on a
/// loop with the one that met it. A stream is walked once more for each value read as null that hid a stream not known
/// yet from it, which, value after value and level after level of a nest, comes to a few thousand walks of one stream
/// under the default limits, as many more as <see cref="PdfReaderLimits.MaxFilterChainLength"/> allows; what it reads
/// of its own dictionary is read once while the walk keeps it, for as many streams as the reader caches objects, the
/// oldest let go first and read again when walked again.
/// </para>
/// <para>
/// A loop that closes only through the value of an object whose stream does not decode alike where it is met stays unseen
/// — a stream that lies on a loop itself, and is circular in its own right; one whose decoding meets a stream again while
/// the reader holds it in its loads; one the reader's nesting cuts there; one a rebuilt index places otherwise —, and so
/// does what decoding would read after that value. The reader reports such a loop as it decodes (#314). The walk that
/// judges parses each stream once a walk, as a reader that keeps it does: it does not read as null, as the reader does for
/// the rest of the judged stream's decoding, a stream that decoding met again while it held it, nor parse again a stream
/// the reader's cache let go; deterministic, the verdict may then name a loop the reader meets in no read order, or miss
/// one it meets.
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
/// it counts. No recursion goes deeper than twice that bound: the walk that works out a standing runs within the walk that
/// met one of its objects, one at a time. Each level asks the thread for room before it goes down, as the reader asks
/// before each load: what the reader serves from its cache, and what the walks keep, is read with no load, and no probe of
/// the reader's, so that the walks of a document read before went their whole depth with none on the way (#312). On first
/// run, the two take about 0.8 KB of the thread's stack a level, some 110 KB at their deepest; with the reader's own 64
/// loads under a value they read through it, about 5.8 KB each, the deepest validation goes some 480 KB deep (420 KB on
/// the deepest shape measured), and with the 128 KB the reader's probe keeps free above its deepest load, it needs a
/// thread of some 610 KB for the probe to cut none of its loads (on that shape, none is cut from 560 KB). Recompiled by
/// tiered PGO, as a process leaves them once it has run them a while, the walks take 0.8 to 1.0 KB a level, as the
/// profile it gathered makes them, up to some 130 KB at their deepest, and a load about 8.3 KB: the deepest validation
/// goes some 670 KB deep (535 to 580 KB on that shape) and needs a thread of some 800 KB (on that shape, none is cut from
/// 720 KB). A thread of 1 MB has room for either. What the reader has read and kept is not modeled, so that the verdict
/// does not depend on what was read before, but for #163, above: a read that reaches the stream through another stream's
/// decoding nests deeper, and cuts earlier; nor is the thread's stack. On a thread with less room, a probe cuts. The
/// reader's, or its parser's, cuts a load the walks make through the reader, and the walks keep what that read gave,
/// for the rest of the validation, as it gave it (#163). The walks' own cuts a level they would go down, which reads as
/// null — a walk of a standing then taking its stream, for the rest of the validation, for one that does not decode
/// alike, and no parse the cut leaves short giving a nest a later walk would take —, and which the document reports
/// once. Either way, the report may depend on the thread.
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
/// share the chain. An object held by an object stream that decodes alike is kept as well, as the reader serves it, and a
/// run goes on through it once its holder's standing is known, where its holder's decoding reaches no deeper than the
/// reader's nesting lets it; a run that ends at an object of a stream whose standing is not known yet is not kept. A chain
/// with fewer loads left than a run kept goes on an object at a time there, so that what a run it takes passes, and how
/// deep its holders' decodings reach, is never that of an object the chain does not load (#312). After a rebuild, a
/// walk of a standing takes a run, or a nest, only where the reader's index places every object it went through as the
/// chain's does. An object a run passes over is not kept for itself: a run keeps where it leads. As many objects are kept
/// as the reader keeps in its cache, an object let go read again when met again. A value a stream reads again — the
/// parameters each step of a chain of filters reads — is followed once a stream, as the reader keeps what it loaded. As it
/// holds each dictionary, it also notes the integers of its header written as reals.
/// </para>
/// <para>
/// The parse of a stream met on a chain parses in turn the stream its <c>/Length</c> loads, a level deeper, and so on to the
/// reader's deepest load: a nest of 62 streams below one met at the second level, which streams of parameters, each heading
/// one, took past what the walk keeps — 16,128 streams a walk, read again for each object stream (#201). A parse that met
/// nothing the walk's own state answers — no object of an object stream, which reads as the stream walked and those in
/// flight make it, no stream parsed before, the stream walked among them, no stream the reader holds in its loads read
/// again, at its deepest load as well as above it, no load the thread's stack had no room for — parsed what the file
/// alone says from that depth down, the same for every walk. The streams it went through, its nest, are kept by the
/// number of the stream heading them, as many nests as the reader keeps objects, and taken at once where that stream is
/// met again at that depth or below it, or at any depth when the parse ended where its chains do, short of the reader's
/// nesting: its streams are parsed, as far as the nesting lets a parse from there reach, up to one parsed already,
/// where the parse would end; and the load the nesting cuts at its end, the first a <c>/Length</c> names, is a stream
/// read again or not, as there. A validation keeps one nest for the outermost parse, or for the outermost one inside it
/// that met nothing.
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

    /// <summary>The keys of an object stream's dictionary a walk reads, before any of its data.</summary>
    private static readonly PdfName[] HeadKeys = [PdfName.Length, PdfName.Type, PdfName.N, PdfName.First, PdfName.Filter, PdfName.DecodeParms];

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
    /// Gets the most entries the stack of object streams whose standing waits to be worked out held at once, stale entries
    /// included: a stream a later walk met again is counted again, until the stack is rid of its entry lower down (#312).
    /// </summary>
    /// <remarks>
    /// Nothing in the library reads it. Tests read it to see that the stack of streams waiting on one another stays within
    /// twice the most streams waiting at once, or 1,024 entries, besides one walk's unknown streams, rather than growing by
    /// an entry for each walk that met a stream before its standing was known, or for each stream it ever held: the
    /// allocation of a validation, which the walks' own objects outweigh, cannot tell them apart.
    /// </remarks>
    internal int MostWaiting { get; private set; }

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
        var walk = new Walk(document, new Memo(document, reader.CacheCapacity, reader.FilterChainBound), judges: true);

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

        result.MostWaiting = walk.MostWaiting;

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
    /// <remarks>
    /// A stream's indirect <c>/Length</c> is still loaded as any object is, at the top of the reader's loads
    /// (<see cref="PdfFileReader.ReadWhereChainPlaces"/>): reading an object stream's dictionary, or a stream met on a chain,
    /// decodes and keeps what that <c>/Length</c> leads through, before a value the walk reads through the reader decodes
    /// it a level deeper. The walk relies on it no more than on any other read: the reader keeps a decoding its deepest load
    /// cut whoever made it (#163).
    /// </remarks>
    private static PdfObject Read(PdfDocument document, PdfObjectId id, XRefEntry entry) =>
        (document.Reader.ChainIndex is null ? null : document.Reader.ReadWhereChainPlaces(id.Number, entry)) ??
        document.GetObject(id);

    /// <summary>
    /// Determines whether the index the reader reads with now places object <paramref name="number"/> as the chain's does:
    /// always, until a rebuild made the reader's own — a relocation finds the object the chain's row places, near it, as
    /// <see cref="Read"/> does —; after one, where both hold the number in use alike, at the same place in the file or in the
    /// same object stream at the same index, or neither holds it in use (#312).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A walk of a standing follows the decoding the reader makes, through its own index: an object the reader's index
    /// places otherwise, or holds in use where the chain's holds nothing, may decode the stream otherwise than the walk follows
    /// it. It asks no more than that, and relies on the reader's index staying as the validation settled it before its
    /// first rule (<see cref="PdfFileReader.SettleIndex"/>), which finishes a rebuild a load or the source left unfinished:
    /// after it, a load changes no entry's kind, and a source that fails a load ends the validation.
    /// </para>
    /// <para>
    /// After a rebuild, the reader serves an object stream's objects from the decoding the rebuild's expansion kept, made at
    /// the top of its loads with no object stream in flight, before the streams of higher numbers were expanded, rather than
    /// from a first read's: the same whatever was read before, the rebuild forgetting it, as long as the reader keeps that
    /// decoding. A rebuilt entry whose header no longer parses at its offset, which a load relocates, is not told apart from
    /// the chain's.
    /// </para>
    /// </remarks>
    private static bool Agrees(PdfFileReader reader, int number)
    {
        if (!reader.WasRepaired || reader.ChainIndex is not { } chain)
        {
            return true;
        }

        var inChain = chain.TryGet(number, out var entry) && entry.Kind != XRefEntryKind.Free;
        var live = reader.Index.TryGet(number, out var current) && current.Kind != XRefEntryKind.Free;

        if (!inChain || !live)
        {
            return inChain == live;
        }

        return entry.Kind == current.Kind &&
            (entry.Kind == XRefEntryKind.Compressed
                ? current.ObjectStreamNumber == entry.ObjectStreamNumber && current.IndexInObjectStream == entry.IndexInObjectStream
                : reader.PositionOf(current) == reader.PositionOf(entry));
    }

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
    /// <param name="memo">What each object written in the file, or held by an object stream that decodes alike, gives the walks.</param>
    /// <param name="judges">
    /// Whether the walk judges the streams, recording each need it meets and going on; otherwise it works out the standing
    /// of an object stream as the holder of an object another decoding reads (#312), and ends at the first thing that
    /// tells it the stream does not decode alike there.
    /// </param>
    private sealed class Walk(PdfDocument document, Memo memo, bool judges)
    {
        /// <summary>
        /// How many entries the stack of waiting standings holds before it is first rid of the entries walks left stale: a
        /// size chosen for the stack's own sake, which is rid of them again whenever it holds more than this and twice as many
        /// as it kept the last time, and never cut. It bounds nothing a file gives, and is no bound ADR 34 classifies.
        /// </summary>
        private const int CompactionFloor = 1024;

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
        /// not parse it again — as long as its cache keeps it, which a walk of a standing does not count on.
        /// </summary>
        private readonly HashSet<int> _parsed = new(SeededComparer.Instance);

        /// <summary>What the values read once the stream is parsed led to, by the number of the object each names.</summary>
        private readonly Dictionary<int, Followed> _values = new(SeededComparer.Instance);

        /// <summary>
        /// The streams parsed since the outermost parse in flight began — a stream met on a chain while no other's
        /// <c>/Length</c> is being resolved —, in order, that stream first: what a parse parsed in turn, which lies past it
        /// here, is its nest. Each parse parses one stream in turn at most, a level below it, its <c>/Length</c> ending at the
        /// first stream it loads: the list holds no more than the reader nests loads.
        /// </summary>
        private readonly List<int> _nested = [];

        /// <summary>
        /// Of each stream in <see cref="_nested"/>, at the same place, the first object its <c>/Length</c> loads, 0 for one
        /// written directly: the load the reader's nesting cuts when a nest is taken deep enough that its parse would not
        /// reach the stream after it, which re-enters a stream the reader holds in its loads, or not.
        /// </summary>
        private readonly List<int> _departures = [];

        /// <summary>
        /// The walk that works out the standing of an object stream, for the walk that judges: made the first time an object
        /// the walk meets is held by a stream whose standing is not known (#312). One serves every standing of a validation,
        /// one after the other: it does not work out another's from inside its own, so that no recursion goes deeper than its
        /// own walk inside the walk that judges, each within the reader's nesting.
        /// </summary>
        private Walk? _assessor;

        /// <summary>
        /// The object streams whose standing the walk that judges is working out, the one walked next last: each waits on
        /// those after it, whose objects its last walk met before their standing was known (#312). A walk pushes each stream
        /// it met that is not known yet, one already waiting included, so that the one walked next is the one met last; the
        /// entry that waits lower is stale by the time it is reached, its stream known, and is dropped then. Rid of such
        /// entries, keeping the topmost entry of each stream not known yet, whenever it holds more than
        /// <see cref="CompactionFloor"/> and twice as many as it kept the last time (<see cref="Compact"/>), it holds no more
        /// than twice the most streams waiting at once, or 1,024 entries, whichever is more, besides one walk's unknown
        /// streams.
        /// </summary>
        private List<int>? _waiting;

        /// <summary>The streams not known yet <see cref="Compact"/> kept an entry of, the topmost of each.</summary>
        private HashSet<int>? _kept;

        /// <summary>How many standings may wait before <see cref="_waiting"/> is rid of its stale entries.</summary>
        private int _compactAt = CompactionFloor;

        /// <summary>
        /// The deepest load the walk made whose value reaches the stream's decoding — a value decoding reads, the chain of its
        /// own <c>/Length</c>, and the decodings of the streams holding the objects they lead through —, not one a stream met
        /// on a chain loads for its own <c>/Length</c>, whose value only places that stream's data (#312).
        /// </summary>
        private int _deepest;

        /// <summary>
        /// Whether a walk of a standing met what makes the stream decode unlike in some read order, or cut where a first read
        /// decodes it (#312).
        /// </summary>
        private bool _unlike;

        /// <summary>
        /// The object streams, not known yet, whose standing a walk of a standing needs, in the order it met an object of each;
        /// the walk read those objects as null, and is made again once they are known (#312). Each is there once. Made by
        /// the walk of a standing, the first time it meets one.
        /// </summary>
        private List<int>? _unknown;

        /// <summary>The object streams in <see cref="_unknown"/>.</summary>
        private HashSet<int>? _unknownSet;

        /// <summary>
        /// How many times the walk met what its own state answers, rather than the file alone: an object of an object stream,
        /// which reads as the stream walked, the streams in flight and its holder's standing make it — or a run through such
        /// objects —, a stream the walk parsed already, which is not parsed again, a load that re-enters a stream the reader
        /// holds in its loads, and a load the thread's stack had no room for, where the thread decides how far a parse
        /// reaches. A parse that met none of them gives the memo its nest, the same for every walk (#201, #312).
        /// </summary>
        private int _stateful;

        /// <summary>
        /// How many loads the reader's nesting cut: a parse that met none ended where its chains do, and its nest is the same
        /// from any depth; one cut there may reach further from higher up.
        /// </summary>
        private int _cuts;

        /// <summary>
        /// How many objects the walk loaded, or took at once in a run or a nest, that the reader's rebuilt index places
        /// otherwise than the chain's (#312): a nest whose parse met none may be taken by a walk of a standing, which follows
        /// what the reader's index places.
        /// </summary>
        private int _disagreements;

        /// <summary>
        /// Of the parses ended inside the outermost one in flight, the outermost that met nothing the walk's state answers,
        /// whose nest the outermost parse gives the memo as it ends — its own when it met nothing either: a parse that met
        /// nothing takes over the one inside it, whose nest its own holds; one that met something leaves it. Null between
        /// outermost parses.
        /// </summary>
        private Settled? _settled;

        /// <summary>The object stream walked, whose objects read as null while it is decoded.</summary>
        private int _stream;

        /// <summary>
        /// Whether the walk follows the walked stream's own <c>/Length</c>, which the reader resolves while it holds the
        /// stream in its loads, parsing it: met again there, the stream is read again, and read as null for the rest of its
        /// decoding (#51).
        /// </summary>
        private bool _ownLength;

        /// <summary>
        /// Where the needs met past the stream's <c>/Length</c> start in <see cref="_found"/>, of a stream that is not of
        /// <c>/Type /ObjStm</c>, whose <c>/Length</c> needs judge nothing (#274); 0 for an object stream.
        /// </summary>
        private int _judging;

        /// <summary>
        /// Gets a value indicating whether the walk is done: one that works out a standing has met what makes the stream
        /// decode unlike — an object the stream holds, a loop, a stream read again, a load the nesting or the thread's stack
        /// cuts —, which is all it asks.
        /// </summary>
        public bool IsDone => !judges && _unlike;

        /// <summary>Gets the most entries the stack of waiting standings held at once, stale entries included (#312).</summary>
        public int MostWaiting { get; private set; }

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
            _settled = null;
            (_unlike, _deepest) = (false, ValueDepth);
            _unknown?.Clear();
            _unknownSet?.Clear();

            // The stream is parsed as one of its objects is read, a level below it; the parse resolves its /Length a level
            // deeper, while the reader holds the stream in its loads, and the reader keeps the stream, so that it is not
            // parsed again. An object it holds is read as null whatever is in flight.
            _parsed.Add(stream);
            _ownLength = true;
            Follow(dictionary.GetRaw(PdfName.Length), LengthKey, LengthLinks, LengthDepth, out _);
            _ownLength = false;
            var ofLength = _found.Count;

            if (IsDone)
            {
                return false;
            }

            // The reader then reads its /Type, and of a stream of another type nothing more (#274). What its /Length needs
            // judges nothing of it, its objects being lost to its type whatever the parse reads; but a read of one of them
            // parses it, and leads through it to what the parse needs.
            if (FollowValue(dictionary.GetRaw(PdfName.Type), TypeKey).Value is not PdfName type || type != PdfName.ObjStm)
            {
                _judging = ofLength;
                return false;
            }

            var count = FollowValue(dictionary.GetRaw(PdfName.N), CountKey).Value.AsInteger() ?? -1;
            var first = IsDone ? -1 : FollowValue(dictionary.GetRaw(PdfName.First), FirstKey).Value.AsInteger() ?? -1;

            // A stream that declares no object, or whose /N or /First is not one the reader can read, is not decoded.
            if (count > 0 && first >= 0 && !IsDone)
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
        /// Works out the standing of object stream <paramref name="stream"/>, read where the chain's row places it, as
        /// <see cref="ObjectStreamDependencies.Run"/> reads each stream, as a first read of one of its objects decodes it
        /// (#312): it decodes alike in every read order when its walk meets no object it holds; no object of a stream that
        /// does not decode alike there, or whose standing is being worked out — a loop —; no stream met again while the reader
        /// holds it in its loads, which the reader reads as null for the rest of every decoding in flight (#51), a stream met
        /// again being parsed again, as a reader whose cache let it go does; no object the reader's index places otherwise
        /// than the chain's; no load the reader's nesting cuts whose value reaches the decoding; and no load the thread's
        /// stack leaves the walk no room for. One that is no stream there, or that the reader's index places elsewhere, does
        /// not: whatever the reader serves of it is no decoding the walk followed.
        /// </summary>
        /// <remarks>
        /// What the walk reads of the dictionary of a stream that waits is kept in the memo, for as many streams as the reader
        /// caches objects, the oldest let go first: one kept is not read from the file again for each walk made once the
        /// streams it waits on are known.
        /// </remarks>
        /// <returns>
        /// The standing; or, when the walk met objects of streams whose standing is not known yet, which it read as null, null,
        /// and those streams in <paramref name="unknown"/>: the standing is worked out again once theirs are known.
        /// </returns>
        public Standing? Assess(int stream, out List<int> unknown)
        {
            var reader = document.Reader;
            unknown = _unknown ??= [];

            if (!IndexOf(reader).TryGet(stream, out var entry) || entry.Kind != XRefEntryKind.Regular || !Agrees(reader, stream))
            {
                return Standing.Unlike;
            }

            var dictionary = memo.HeadOf(stream);

            if (dictionary is null)
            {
                if (Read(document, new PdfObjectId(stream, entry.Generation), entry) is not PdfStream objectStream)
                {
                    return Standing.Unlike;
                }

                dictionary = objectStream.Dictionary;
            }

            Run(stream, dictionary);

            if (_unlike)
            {
                return Standing.Unlike;
            }

            if (unknown.Count == 0)
            {
                return new Standing(StandingKind.Alike, _deepest);
            }

            memo.KeepHead(stream, dictionary);
            return null;
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

            for (var loaded = 0; loaded < links;)
            {
                if (IsDone)
                {
                    return PdfNull.Instance;
                }

                // Object `number` is the next the chain loads. A run of links, each a reference to the next and none a stream,
                // needs nothing: it is taken at once — none is longer than the loads the chain has left —; elsewhere the chain
                // goes on an object at a time. A run through objects held by object streams that decode alike is taken where
                // each such object would be followed: its holder's decoding, from a level deeper, reaching no further than the
                // reader's nesting. No such run passes an object the walked stream holds, nor one of a stream whose /Length is
                // being resolved: the holder's own walk would meet it, and the holder would not decode alike. A walk of a
                // standing takes a run only where the reader's index places every object of it as the chain's does, and goes
                // an object at a time elsewhere, asking it of each (#312).
                if (depth <= PdfFileReader.MaxNestedLoads && memo.RunFrom(number, links - loaded) is { } run &&
                    (judges || run.Agrees) &&
                    (!run.PassesMembers || depth - 1 + run.Reach <= PdfFileReader.MaxNestedLoads))
                {
                    // A run through objects of object streams is the walk's state's to answer; its holders' decodings reach
                    // as deep as the deepest of them.
                    if (run.PassesMembers)
                    {
                        _stateful++;
                    }

                    if (!run.Agrees)
                    {
                        _disagreements++;
                    }

                    if (_inFlight.Count == 0)
                    {
                        _deepest = Math.Max(_deepest, run.PassesMembers ? depth - 1 + run.Reach : depth);
                    }

                    // The run holds no more objects than the chain has loads left, and is taken whole.
                    var taken = run.Numbers.Length;

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
        /// Determines whether the reader holds stream <paramref name="stream"/> in its loads where the walk stands: a stream met
        /// on the way whose <c>/Length</c> is being resolved, or the walked stream while its own is.
        /// </summary>
        private bool IsInFlight(int stream) => _inFlight.Contains(stream) || (_ownLength && stream == _stream);

        /// <summary>
        /// Loads object <paramref name="number"/> as the reader loads it at <paramref name="depth"/> while the stream is
        /// decoded, recording, under <paramref name="key"/>, what it needs.
        /// </summary>
        private PdfObject Load(int number, string key, int depth)
        {
            var reader = document.Reader;

            // Each load the walks follow is a level of their recursion — a stream met on a chain parses its /Length a level
            // deeper, and the walk of a standing runs inside the load that met an object of its stream —, and every way down
            // to the next level, a standing's walk included, goes through a load: a level asks the thread for room here, once,
            // before anything else, which allocates nothing. The reader's probe does not stand in for it: what the walks
            // keep, and what the reader serves from its cache, is read with no load, so that the walks of a document read
            // before went their whole depth with no probe on the way, and past the end of a short stack (#312). Short of
            // room, the object reads as null, as past the reader's nesting, and the cut counts as the nesting's cut of an
            // object the reader holds in its loads does, whatever the object, among what the walk's state answers: where it
            // falls is the thread's to say, not the file's. So no parse in flight gives a nest that a walk with more room
            // would take — the nesting's count of its cuts, which marks a nest given as ended there, has none to mark —, and
            // a walk of a standing takes its stream, for the rest of the validation, for one that does not decode alike: a
            // value read as null there makes the decoding read less than the reader's. It is reported, once a document.
            if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
            {
                _stateful++;
                _unlike |= !judges;
                reader.ReportWalkTooDeep(number, judges ? null : _stream);
                return PdfNull.Instance;
            }

            // A walk of a standing follows the decoding the reader makes, through its own index (#312): an object it places
            // otherwise than the chain's, or holds in use where the chain's holds nothing, may decode the stream otherwise
            // than the walk follows it. The walk that judges reads the chain's index, and counts it only for the nests its
            // parses give.
            var agrees = Agrees(reader, number);

            if (!agrees)
            {
                _disagreements++;

                if (!judges)
                {
                    _unlike = true;
                    return PdfNull.Instance;
                }
            }

            // Object 0 heads the free list and is never in use (ISO 32000-1, 7.5.4): the reader never reads it, whatever the
            // index's first entry says, so no stream needs it.
            if (number <= 0 || !IndexOf(reader).TryGet(number, out var entry))
            {
                return PdfNull.Instance;
            }

            // What an object of an object stream reads as, the walk's state answers.
            if (entry.Kind == XRefEntryKind.Compressed)
            {
                _stateful++;
                return Member(number, entry, key, depth, agrees);
            }

            if (entry.Kind != XRefEntryKind.Regular)
            {
                return PdfNull.Instance;
            }

            // A load whose value reaches the stream's decoding, one outside every parse, counts for how deep it decodes: the
            // walk makes it at the second or third level, within the reader's nesting, but a read that decodes the stream
            // deeper makes it deeper, where the nesting cuts it, and the decoding depends on how deep it is made — which the
            // walk that meets the stream's object asks of its standing (#312). A load inside the parse of a stream met on a
            // chain resolves that stream's /Length, which places its data and nothing else: cut there or not, the decoding
            // reads the same values, but where the reader serves past the cut what its cache kept (#163).
            if (_inFlight.Count == 0)
            {
                _deepest = Math.Max(_deepest, depth);
            }

            // Past the reader's nesting, a load reads as null, and is made inside a parse. The reader asks first whether it
            // holds the object in its loads: a stream it is parsing, met again, is read again, and read as null for the rest
            // of every decoding in flight (#51) — a cut to a parse, but not to the decoding of a stream whose standing is
            // worked out, which reads it so at a first read and not a level deeper.
            if (depth > PdfFileReader.MaxNestedLoads)
            {
                _cuts++;

                if (IsInFlight(number))
                {
                    _stateful++;
                    _unlike |= !judges;
                }

                return PdfNull.Instance;
            }

            var held = memo.Get(number, entry);

            // A stream's parse resolves its /Length a level deeper, while it is in flight; once, as the reader keeps it. One met
            // again while in flight, which the reader reads as null, is met on the chain of a /Length, whose value the walk
            // drops: what it gives there changes nothing to the stream walked. But the reader goes on reading it as null for
            // the rest of every decoding in flight (#51): an object stream decoded inside another's reads it as that one's
            // decoding makes it, and does not decode alike. That it is not parsed again, what the walk parsed before decides —
            // for the walk that judges, which parses each stream once, as a reader that keeps it does; a walk of a standing
            // parses it again, as a reader whose cache let it go between two meetings does, so that what that parse meets
            // tells the stream unlike as well.
            if (held.IsStream)
            {
                if (_parsed.Add(number))
                {
                    Parse(number, held, key, depth);
                }
                else
                {
                    _stateful++;

                    if (!judges)
                    {
                        if (IsInFlight(number))
                        {
                            _unlike = true;
                        }
                        else
                        {
                            Parse(number, held, key, depth);
                        }
                    }
                }
            }

            return held.Value;
        }

        /// <summary>
        /// Follows the <c>/Length</c> of stream <paramref name="number"/>, which <paramref name="held"/> gives, as its parse
        /// resolves it, under <paramref name="key"/>: met on a chain at <paramref name="depth"/>, the stream is in flight
        /// meanwhile, and the <c>/Length</c> loaded a level deeper. A nest the memo knows of it, from that depth or above, is
        /// taken at once, as a run of links is (#201).
        /// </summary>
        /// <remarks>
        /// <para>
        /// The parse parses in turn the first stream its <c>/Length</c> loads, which ends that chain, a level deeper. Streams
        /// of parameters each heading a nest of 62 such streams were 16,128 a walk, more than the memo keeps objects: the walk
        /// of each object stream read them all again, about 60 ms and 15 MB a stream (#201). A parse that met nothing the
        /// walk's state answers gives its nest; the outermost parse gives the memo its own, or that of the outermost inside it
        /// that met nothing, so that a validation keeps a nest for each stream met at the head of one, not one for each stream
        /// of it — which would let go of the heads' as the objects' let go of them.
        /// </para>
        /// <para>
        /// A parse that parsed no stream in turn gives a nest as well, which holds none: one whose <c>/Length</c> names an
        /// object written apart — an integer, as a writer that streams its output writes it — is taken at once rather than
        /// loading that object again once the memo let it go, which a nest holding no stream saved six times over in reads,
        /// where nests outlive objects. One whose <c>/Length</c> is written directly saves nothing, and takes a place among
        /// the nests, as many as the reader keeps objects.
        /// </para>
        /// <para>
        /// A nest taken by a walk of a standing is one the reader's rebuilt index places, every object its parse went through
        /// as the chain's index does (#312). The reader holds the stream in its loads while a nest is taken as while its
        /// <c>/Length</c> is followed: a stream of the nest, or the object the reader's nesting cuts at its end, that is in
        /// flight there is read again.
        /// </para>
        /// </remarks>
        private void Parse(int number, Held held, string key, int depth)
        {
            // The streams a parse parses in turn are noted, in order, for the nest it may give the memo; the outermost parse
            // starts the list anew.
            if (_inFlight.Count == 0)
            {
                _nested.Clear();
                _departures.Clear();
            }

            _nested.Add(number);
            _departures.Add(held.Length is PdfReference length ? length.Id.Number : 0);
            _inFlight.Add(number);

            if (memo.NestOf(number) is { } known && (known.EndsChain || depth >= known.Depth) && (judges || known.Agrees))
            {
                Take(known, depth);
            }
            else
            {
                var (stateful, cuts, disagreements, start) = (_stateful, _cuts, _disagreements, _nested.Count);
                Follow(held.Length, key, LengthLinks, depth + 1, out _);

                // A parse that met nothing the walk's state answers takes over the nest inside it, which its own holds; a walk
                // of a standing that ended inside it followed it in part.
                if (_stateful == stateful && !IsDone)
                {
                    _settled = new Settled(number, depth, _cuts == cuts, _disagreements == disagreements, start, _nested.Count);
                }
            }

            _inFlight.Remove(number);

            if (_inFlight.Count == 0 && _settled is { } settled && !IsDone)
            {
                memo.NoteNest(
                    settled.Stream,
                    new Nest(
                        settled.Depth,
                        settled.EndsChain,
                        settled.Agrees,
                        CollectionsMarshal.AsSpan(_nested)[settled.Start..settled.End].ToArray(),
                        CollectionsMarshal.AsSpan(_departures)[(settled.Start - 1)..settled.End].ToArray()));
                _settled = null;
            }
        }

        /// <summary>
        /// Takes at once the nest <paramref name="nest"/> of a stream met at <paramref name="depth"/>: parses its streams, as
        /// far as the reader's nesting lets the stream's parse reach from there — the first a level below the stream, each
        /// next a level further —, and up to one the walk parsed already, where that parse ends, the stream met again; and
        /// counts what the parse would have met, the load the reader's nesting cuts at its end included.
        /// </summary>
        private void Take(Nest nest, int depth)
        {
            var streams = nest.Streams;
            var reach = Math.Min(streams.Length, PdfFileReader.MaxNestedLoads - depth);

            if (!nest.Agrees)
            {
                _disagreements++;
            }

            for (var index = 0; index < reach; index++)
            {
                if (!_parsed.Add(streams[index]))
                {
                    _stateful++;

                    // The reader keeps a stream it parsed, and the parse ends there, for the walk that judges. A walk of a
                    // standing parses it again, as a reader whose cache let it go does, and the nest goes on — but for a stream
                    // the reader holds in its loads, which it reads again.
                    if (judges)
                    {
                        return;
                    }

                    if (IsInFlight(streams[index]))
                    {
                        _unlike = true;
                        return;
                    }
                }

                _nested.Add(streams[index]);
                _departures.Add(nest.Loads[index + 1]);
            }

            // Short of the nest's end, or at an end the reader's nesting made, the parse is cut there: a parse resolves a
            // /Length, which places the stream's data and nothing a decoding reads. The object the cut loads is the first the
            // /Length of the last stream taken names, and is read again when the reader holds it in its loads.
            if (reach < streams.Length || !nest.EndsChain)
            {
                _cuts++;

                if (IsInFlight(nest.Loads[reach]))
                {
                    _stateful++;
                    _unlike |= !judges;
                }
            }
        }

        /// <summary>
        /// Loads object <paramref name="number"/>, which object stream <paramref name="entry"/> names holds, as the reader
        /// loads it at <paramref name="depth"/> while the stream walked is decoded; <paramref name="agrees"/> says whether the
        /// reader's index places it as the chain's does.
        /// </summary>
        private PdfObject Member(int number, XRefEntry entry, string key, int depth, bool agrees)
        {
            var holder = entry.ObjectStreamNumber;

            // One the walked stream holds reads as null while it is decoded, and is reported before the reader counts the
            // depth (#51): a need of the stream judged; of one whose standing is worked out, what makes it decode unlike.
            if (holder == _stream)
            {
                if (judges)
                {
                    _found.Add(new Need(holder, number, key));
                }
                else
                {
                    _unlike = true;
                }

                return PdfNull.Instance;
            }

            // Another's stream is loaded a level deeper: past the reader's nesting, or while it is in flight — a stream met on
            // the way, its /Length being resolved —, the object reads as null before that stream is decoded. Not a re-entry a
            // walk of a standing need tell apart: the holder's own walk resolves that /Length too, meets its own object, and
            // does not decode alike, so that a later read of the object here tells the stream unlike. An object met at the
            // reader's deepest load is met inside a parse, on the chain of a /Length that places a stream's data alone: one
            // outside every parse is met at the second or third level.
            var reaching = _inFlight.Count == 0;

            if (reaching)
            {
                _deepest = Math.Max(_deepest, depth + 1);
            }

            if (depth >= PdfFileReader.MaxNestedLoads)
            {
                return PdfNull.Instance;
            }

            if (_inFlight.Contains(holder))
            {
                return PdfNull.Instance;
            }

            // Otherwise the reader decodes that stream, within its own guards and budget, and serves the object. A stream
            // that decodes alike in every read order, decoded here within the reader's nesting, gives the object the same
            // way whoever reads it, and lies on no loop: the object is read as the reader serves it, and followed (#312).
            // Of another, or of one the reader's index no longer places as the chain's, what the reader serves may depend on
            // what it decoded first: the object is a need, and reads as null, as one of a stream in flight does. A walk of a
            // standing that meets a stream whose standing is not known yet waits for it.
            Standing standing;

            if (!agrees)
            {
                standing = Standing.Unlike;
            }
            else if (memo.StandingOf(holder) is { } known)
            {
                standing = known;
            }
            else if (judges)
            {
                standing = Settle(holder);
            }
            else
            {
                if ((_unknownSet ??= new(SeededComparer.Instance)).Add(holder))
                {
                    (_unknown ??= []).Add(holder);
                }

                return PdfNull.Instance;
            }

            var offset = depth - 1;

            if (standing.IsAlikeAt(offset))
            {
                if (reaching)
                {
                    _deepest = Math.Max(_deepest, offset + standing.Reach);
                }

                return memo.Get(number, entry).Value;
            }

            // Of a stream that decodes alike, cut here, the value a /Length met on a chain reads places a stream's data alone:
            // what the walk reads there instead changes no value a decoding reads. Anywhere else, and of a stream that does not
            // decode alike, it does.
            if (judges)
            {
                _found.Add(new Need(holder, number, key));
            }
            else
            {
                _unlike |= reaching || standing.Kind != StandingKind.Alike;
            }

            return PdfNull.Instance;
        }

        /// <summary>
        /// Works out the standing of object stream <paramref name="holder"/>, and of every stream whose standing it needs
        /// first, one walk after the other, each as a first read decodes it: a walk that meets objects of streams whose
        /// standing is not known yet reads them as null, and waits for those streams, walked first, the first it met walked
        /// next; once they are known, the walk is made again. A stream met while its standing is being worked out lies on a
        /// loop with the stream that met it, whose walk met an object of the one it waits on in turn, each read in a walk that
        /// read nothing a decoding would not: neither decodes alike.
        /// </summary>
        /// <remarks>
        /// A stream is walked once more than the times a value read as null hid a stream not known yet from it. That is once
        /// for each link of a chain of such streams, and the hiding runs on from one value to the next: a null element ends a
        /// chain of filters, and a value gates the next — <c>/Type</c> then <c>/N</c> and <c>/First</c>, then <c>/Filter</c>,
        /// <c>/DecodeParms</c>, a predictor and what it reads —, as the levels of a nest each gate the one below. Under the
        /// default limits that is a few thousand walks of one stream, 1,921 for 64 filters each read through 30 such streams,
        /// and as many more as <see cref="PdfReaderLimits.MaxFilterChainLength"/> allows. Each walk repeats the stream's whole
        /// walk, so that one stream costs its walk once for each holder that hid from it, and nests the walk cannot keep are
        /// followed again on each of those walks (#181); the walks of a validation stay within about three per object stream,
        /// a walk being made again only once a stream not walked before has been (#315). No recursion goes deeper than one
        /// walk inside the walk that judges.
        /// </remarks>
        private Standing Settle(int holder)
        {
            var waiting = _waiting ??= [];
            var assessor = _assessor ??= new Walk(document, memo, judges: false);
            waiting.Add(holder);
            MostWaiting = Math.Max(MostWaiting, waiting.Count);

            while (waiting.Count > 0)
            {
                var stream = waiting[^1];

                // A stream waited on by two is worked out for the first that reaches it; the other's entry is stale.
                if (memo.StandingOf(stream) is { Kind: not StandingKind.Working })
                {
                    waiting.RemoveAt(waiting.Count - 1);
                    continue;
                }

                memo.NoteStanding(stream, Standing.Working);

                if (assessor.Assess(stream, out var unknown) is { } standing)
                {
                    memo.NoteStanding(stream, standing);
                    waiting.RemoveAt(waiting.Count - 1);
                    continue;
                }

                for (var index = unknown.Count - 1; index >= 0; index--)
                {
                    waiting.Add(unknown[index]);
                }

                MostWaiting = Math.Max(MostWaiting, waiting.Count);

                if (waiting.Count > _compactAt)
                {
                    Compact(waiting);
                }
            }

            return memo.StandingOf(holder)!.Value;
        }

        /// <summary>
        /// Rids <paramref name="waiting"/> of its stale entries, in place: of each stream not known yet — not reached yet, or
        /// being worked out —, the topmost entry is kept, the order of those kept unchanged; every entry of a stream known
        /// already is dropped. Exact: the topmost entry of a stream is the one taken up, one lower is reached only once that
        /// stream is known, and an entry of a known stream is dropped when it is reached.
        /// </summary>
        /// <remarks>
        /// An entry of a known stream is not always another's duplicate: a stream pushed below one that waits, pushed again
        /// above it by a later walk and worked out from there, leaves its lower entry its only one. Kept, such entries piled
        /// up a block for each level of streams that wait on one another, growing with every stream the stack ever held:
        /// 5,008 entries for 80 levels of 64, of 5,343 streams it held, where 144 streams waited at once (#312).
        /// </remarks>
        private void Compact(List<int> waiting)
        {
            var kept = _kept ??= new(SeededComparer.Instance);
            kept.Clear();
            var at = waiting.Count;

            for (var index = waiting.Count - 1; index >= 0; index--)
            {
                if (memo.StandingOf(waiting[index]) is null or { Kind: StandingKind.Working } && kept.Add(waiting[index]))
                {
                    waiting[--at] = waiting[index];
                }
            }

            waiting.RemoveRange(0, at);
            _compactAt = Math.Max(CompactionFloor, 2 * waiting.Count);
        }
    }

    /// <summary>
    /// A parse that met nothing the walk's state answers, whose nest the outermost parse gives the memo as it ends.
    /// </summary>
    /// <param name="Stream">The stream parsed.</param>
    /// <param name="Depth">The depth it was met at.</param>
    /// <param name="EndsChain">Whether the parse ended where its chains do, with no load the reader's nesting cut.</param>
    /// <param name="Agrees">
    /// Whether the reader's index places every object the parse went through as the chain's does (#312).
    /// </param>
    /// <param name="Start">Where the streams it parsed in turn start in the walk's list of the streams parsed.</param>
    /// <param name="End">Where they end there.</param>
    private readonly record struct Settled(int Stream, int Depth, bool EndsChain, bool Agrees, int Start, int End);

    /// <summary>
    /// What a walk found of an object stream as the holder of an object another decoding reads (#312): whether it decodes
    /// alike in every read order, and how deep.
    /// </summary>
    /// <param name="Kind">What was found.</param>
    /// <param name="Reach">
    /// Of a stream that decodes alike, how deep its decoding loads, nested decodings included, an object it holds read first
    /// being the first load and the stream the second.
    /// </param>
    private readonly record struct Standing(StandingKind Kind, int Reach)
    {
        /// <summary>Gets the standing of a stream whose standing is being worked out: one met meanwhile lies on a loop with it.</summary>
        public static Standing Working => new(StandingKind.Working, 0);

        /// <summary>Gets the standing of a stream that does not decode alike in every read order.</summary>
        public static Standing Unlike => new(StandingKind.Unlike, 0);

        /// <summary>
        /// Determines whether the stream decodes alike where an object it holds is read <paramref name="offset"/> levels
        /// deeper than a first read: it decodes alike, and its deepest load stays within the reader's nesting there.
        /// </summary>
        public bool IsAlikeAt(int offset) => Kind == StandingKind.Alike && offset + Reach <= PdfFileReader.MaxNestedLoads;
    }

    /// <summary>What a walk found of an object stream as the holder of an object another decoding reads.</summary>
    private enum StandingKind
    {
        /// <summary>Being worked out.</summary>
        Working,

        /// <summary>Decodes alike in every read order, wherever its deepest load stays within the reader's nesting.</summary>
        Alike,

        /// <summary>Does not decode alike in every read order, or a first read of one of its objects cuts its decoding.</summary>
        Unlike,
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
    /// object a guard cut, or left out of the index (#263): nothing past it is needed. A walk that works out a standing
    /// ends it at the first thing that tells the stream unlike, which tells it all it asks.
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
    /// What each object written in the file, or held by an object stream that decodes alike, gives the walks, by object
    /// number — a reference's generation leads the reader to no other object —: what decoding may read of it, and the run a
    /// chain loads from it. As many objects as <paramref name="capacity"/> says, the oldest let go first, and read again when
    /// met again, so that what the walks keep follows the reader's cache rather than the file (#201). It also keeps the
    /// standing of each object stream the walks met an object of (#312), what the walks of standings read of each stream's
    /// dictionary, and, of each outermost parse a walk followed, at most one nest: that of the outermost parse in it, itself
    /// included, that met nothing its state answers, by the number of the stream heading it.
    /// </summary>
    /// <param name="document">The document whose objects are read.</param>
    /// <param name="capacity">How many objects are kept, at most: as many as the reader caches; and as many nests.</param>
    /// <param name="maxFilters">The most filters a stream's data passes through, which bounds what is kept of an array.</param>
    private sealed class Memo(PdfDocument document, int capacity, int maxFilters)
    {
        private readonly Dictionary<int, Held> _held = new(SeededComparer.Instance);
        private readonly Queue<int> _order = new();

        /// <summary>
        /// The nests the walks found, by the number of the stream heading each: as many as <c>capacity</c> says, the oldest
        /// let go first, and found again when met again. They are kept apart from the objects, which the parse of one nest
        /// fills in turn, and which would let go of every nest a walk meets as it takes in their streams. Each holds 62
        /// numbers at most, as many streams as a parse reaches below a stream met at the second level, the shallowest any is
        /// met, within the reader's nesting (<see cref="PdfFileReader.MaxNestedLoads"/>), which no value the file gives
        /// changes: what they keep follows the reader's cache, not the file (#201).
        /// </summary>
        private readonly Dictionary<int, Nest> _nests = new(SeededComparer.Instance);
        private readonly Queue<int> _nestOrder = new();

        /// <summary>
        /// What the walks of standings read of the dictionary of each object stream whose standing waits on others — its
        /// <c>/Length</c>, <c>/Type</c>, <c>/N</c>, <c>/First</c>, <c>/Filter</c> and <c>/DecodeParms</c>, an array as far as
        /// a chain of filters reads it —, by the stream's number: as many as <c>capacity</c> says, the oldest let go first. Such
        /// a stream is walked again once they are known, a few thousand times at most; reading its dictionary from the file
        /// again for each walk cost as many times what the file wrote in it, up to the reader's bound on an object, 16 MB:
        /// 2.1 GB and 5 s for a dictionary of 512 KB walked 1,921 times (#312).
        /// </summary>
        private readonly Dictionary<int, PdfDictionary> _heads = new(SeededComparer.Instance);
        private readonly Queue<int> _headOrder = new();

        /// <summary>
        /// The standing of each object stream the walks met an object of, by its number: worked out once a validation, as a
        /// first read of one of its objects decodes it, and kept for good (#312). Made the first time one is, so that a walk
        /// that meets none pays nothing for it. It holds no more entries than the index names object streams, which the walk
        /// takes one by one as well: no value the file gives chooses its size.
        /// </summary>
        private Dictionary<int, Standing>? _standings;

        /// <summary>The numbers of the run being worked out, the longest a chain loads.</summary>
        private readonly int[] _run = new int[LengthLinks];

        /// <summary>Gives the standing of object stream <paramref name="stream"/>; null when it is not known yet.</summary>
        public Standing? StandingOf(int stream) =>
            _standings is not null && _standings.TryGetValue(stream, out var standing) ? standing : null;

        /// <summary>Notes the standing of object stream <paramref name="stream"/>.</summary>
        public void NoteStanding(int stream, Standing standing) =>
            (_standings ??= new(SeededComparer.Instance))[stream] = standing;

        /// <summary>Gives the nest a walk found of stream <paramref name="stream"/>; null when none is kept.</summary>
        public Nest? NestOf(int stream) => _nests.TryGetValue(stream, out var nest) ? nest : null;

        /// <summary>
        /// Keeps <paramref name="nest"/>, the nest of stream <paramref name="stream"/>: in place of the one kept, since a walk
        /// follows a parse only where none kept can be taken — the one kept found from deeper down, or, for a walk of a
        /// standing, through an object the reader's rebuilt index places otherwise than the chain's.
        /// </summary>
        public void NoteNest(int stream, Nest nest)
        {
            if (!_nests.ContainsKey(stream))
            {
                if (_nests.Count >= capacity && _nestOrder.TryDequeue(out var oldest))
                {
                    _nests.Remove(oldest);
                }

                _nestOrder.Enqueue(stream);
            }

            _nests[stream] = nest;
        }

        /// <summary>
        /// Gives what a walk of a standing read of the dictionary of object stream <paramref name="stream"/>, kept for the
        /// walks made again once the streams it waits on are known; null when none is kept.
        /// </summary>
        public PdfDictionary? HeadOf(int stream) => _heads.TryGetValue(stream, out var head) ? head : null;

        /// <summary>
        /// Keeps what the walks read of <paramref name="dictionary"/>, that of object stream <paramref name="stream"/>, whose
        /// standing waits on others, unless it is kept already.
        /// </summary>
        public void KeepHead(int stream, PdfDictionary dictionary)
        {
            if (_heads.ContainsKey(stream))
            {
                return;
            }

            var head = new PdfDictionary();

            foreach (var key in HeadKeys)
            {
                // As of an object kept, nothing is kept of a dictionary but what a step's parameters are read from, nor of an
                // array past what a chain of filters reads: what the file wrote elsewhere in the stream's dictionary is read
                // no more than once.
                if (dictionary.GetRaw(key) is { } value)
                {
                    head.Set(key, value switch
                    {
                        PdfDictionary parameters => Parameters(parameters),
                        PdfArray array when array.Count > maxFilters => Head(array, maxFilters),
                        _ => value,
                    });
                }
            }

            if (_heads.Count >= capacity && _headOrder.TryDequeue(out var oldest))
            {
                _heads.Remove(oldest);
            }

            _heads[stream] = head;
            _headOrder.Enqueue(stream);
        }

        /// <summary>
        /// Gives what object <paramref name="number"/>, which <paramref name="entry"/> places, gives the walks: kept already,
        /// or read where the chain places it — or, of one an object stream that decodes alike holds, as the reader serves
        /// it —, and kept.
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
        /// file nor held by an object stream known to decode alike; is a stream; holds no reference, so that loading it says
        /// more than where the chain goes on; or starts a run kept from a chain that loaded more than
        /// <paramref name="loads"/> objects, which the chain goes on from an object at a time. A run kept from a chain that
        /// loaded fewer objects ends where that chain did; the chain that goes on past it goes on from there, as from any
        /// object it loads. No run given is longer than the loads the chain has left: it passes, and reaches as deep as, what
        /// the chain loads alone.
        /// </summary>
        /// <param name="number">The object the chain loads next.</param>
        /// <param name="loads">How many objects the chain loads at most from it.</param>
        public LinkRun? RunFrom(int number, int loads)
        {
            if (number <= 0 || !IndexOf(document.Reader).TryGet(number, out var entry) || !IsAlikeForEveryStream(number, entry))
            {
                return null;
            }

            var held = Get(number, entry);

            if (held.IsStream || held.Value is not PdfReference)
            {
                return null;
            }

            // A run kept from a chain that loaded more objects than this one has left is not given: what it passes past them,
            // and how deep their holders' decodings reach, this chain never loads (#312). The chain goes on an object at a
            // time from there, as from an object no run starts at: each object it loads is kept, and the runs kept from the
            // objects after it are taken where they fit. Working out the shorter run again read again, for each chain, the
            // objects it passes over, which are not kept: 5.2 KB a stream, where its own walk takes 2.5 KB, for streams whose
            // /Length and predictor share a chain.
            if (held.Run is { } kept)
            {
                return kept.Numbers.Length <= loads ? kept : null;
            }

            // A run that ends at an object of a stream whose standing is not known yet is not kept: once it is known, the run
            // may go on past it (#312).
            var run = Build(number, entry, held, loads, out var settled);

            if (settled)
            {
                held.Run = run;
            }

            return run;
        }

        /// <summary>
        /// Works out the run a chain loads from object <paramref name="number"/>, which <paramref name="entry"/> places and
        /// whose value <paramref name="held"/> is a reference: as far as <paramref name="loads"/> objects — no object the
        /// chain does not load is read —, or to the first object that is not such a link. A loop of links runs on to
        /// <paramref name="loads"/>, as the chain does. Whether the reader's index places each object of it as the chain's
        /// does is worked out once, the index no longer changing once the validation settled it.
        /// </summary>
        private LinkRun Build(int number, XRefEntry entry, Held held, int loads, out bool settled)
        {
            settled = true;
            var count = 0;
            var passesMembers = entry.Kind == XRefEntryKind.Compressed;
            var reach = HolderReach(entry);
            var agrees = Agrees(document.Reader, number);
            _run[count++] = number;
            var next = ((PdfReference)held.Value).Id.Number;

            while (count < loads && next > 0 && IndexOf(document.Reader).TryGet(next, out var nextEntry))
            {
                if (!IsAlikeForEveryStream(next, nextEntry))
                {
                    settled = nextEntry.Kind != XRefEntryKind.Compressed || StandingOf(nextEntry.ObjectStreamNumber) is { Kind: not StandingKind.Working };
                    break;
                }

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
                reach = Math.Max(reach, HolderReach(nextEntry));
                agrees &= Agrees(document.Reader, next);
                _run[count++] = next;
                next = link.Id.Number;
            }

            return new LinkRun(_run.AsSpan(0, count).ToArray(), next, passesMembers, reach, agrees);
        }

        /// <summary>Gives how deep the decoding of the object stream holding the object <paramref name="entry"/> places reaches; 0 for one written in the file.</summary>
        private int HolderReach(XRefEntry entry) =>
            entry.Kind == XRefEntryKind.Compressed && StandingOf(entry.ObjectStreamNumber) is { } standing ? standing.Reach : 0;

        /// <summary>
        /// Gives whether what object <paramref name="number"/>, which <paramref name="entry"/> places, gives the walks is the
        /// same whatever stream is walked, and may be kept for all of them: an object written in the file; or one held by an
        /// object stream known to decode alike in every read order, which the reader's index places where the chain's does.
        /// </summary>
        private bool IsAlikeForEveryStream(int number, XRefEntry entry) => entry.Kind switch
        {
            XRefEntryKind.Regular => true,
            XRefEntryKind.Compressed => StandingOf(entry.ObjectStreamNumber)?.Kind == StandingKind.Alike && Agrees(document.Reader, number),
            _ => false,
        };

        /// <summary>
        /// Reads object <paramref name="number"/>, which <paramref name="entry"/> places: where the chain places it; or, one
        /// held by an object stream that decodes alike in every read order, as the reader serves it, which is the same
        /// whoever reads it — but for a decoding the reader's nesting cut and kept (#163).
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

    /// <summary>What the walks keep of an object written in the file, or held by an object stream that decodes alike.</summary>
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
    /// What the parse of a stream met on a chain parses in turn as it resolves its <c>/Length</c>, when it met nothing a
    /// walk's state answers — no object of an object stream, no stream parsed before, the stream walked included, no stream
    /// the reader holds in its loads read again —: the streams its chains lead through, each the first stream the
    /// <c>/Length</c> of the one before loads, a level below it, which the file alone gives from that depth down (#201).
    /// </summary>
    /// <param name="depth">The depth the stream was met at when its parse gave the nest: the nest is the same from below.</param>
    /// <param name="endsChain">
    /// Whether the parse ended where its chains do, with no load the reader's nesting cut: the nest is then the same from any
    /// depth, as far as the nesting lets a parse reach.
    /// </param>
    /// <param name="agrees">
    /// Whether the reader's index places every object the parse went through as the chain's does, which a walk of a
    /// standing asks of what it takes (#312).
    /// </param>
    /// <param name="streams">The streams it parsed in turn, in order, the first a level below the stream.</param>
    /// <param name="loads">
    /// Of the stream and of each of <paramref name="streams"/> after it, in order, the first object its <c>/Length</c> loads, 0
    /// for one written directly: the object the reader's nesting cuts, a level below the last stream a parse from a given
    /// depth reaches.
    /// </param>
    private sealed class Nest(int depth, bool endsChain, bool agrees, int[] streams, int[] loads)
    {
        /// <summary>Gets the depth the stream was met at when its parse gave the nest.</summary>
        public int Depth { get; } = depth;

        /// <summary>Gets a value indicating whether the parse ended where its chains do, rather than where the nesting cut it.</summary>
        public bool EndsChain { get; } = endsChain;

        /// <summary>
        /// Gets a value indicating whether the reader's index places every object the parse went through as the chain's does.
        /// </summary>
        public bool Agrees { get; } = agrees;

        /// <summary>Gets the streams the parse parsed in turn, in order, the first a level below the stream.</summary>
        public int[] Streams { get; } = streams;

        /// <summary>
        /// Gets, of the stream and of each of <see cref="Streams"/> after it, the first object its <c>/Length</c> loads: one
        /// more than the streams.
        /// </summary>
        public int[] Loads { get; } = loads;
    }

    /// <summary>
    /// The objects a chain loads one after the other from an object written in the file, or held by an object stream that
    /// decodes alike, each a reference to the next and none a stream — 33 at most, as many as the longest chain loads —, and
    /// the object it loads after them.
    /// </summary>
    /// <param name="numbers">The objects, in the order the chain loads them.</param>
    /// <param name="next">The object the last of them names.</param>
    /// <param name="passesMembers">Whether an object stream holds one of the objects.</param>
    /// <param name="reach">The deepest the decodings of the object streams holding the objects reach, as their standings say.</param>
    /// <param name="agrees">Whether the reader's index places each of the objects as the chain's does (#312).</param>
    private sealed class LinkRun(int[] numbers, int next, bool passesMembers, int reach, bool agrees)
    {
        /// <summary>Gets the objects, in the order the chain loads them.</summary>
        public int[] Numbers { get; } = numbers;

        /// <summary>Gets the object the last of them names, which the chain loads next.</summary>
        public int Next { get; } = next;

        /// <summary>
        /// Gets a value indicating whether an object stream holds one of the objects: the run is then taken only where a
        /// chain reads each such object as its holder serves it, that holder's decoding, from a level deeper, reaching no
        /// further than the reader's nesting.
        /// </summary>
        public bool PassesMembers { get; } = passesMembers;

        /// <summary>Gets the deepest the decodings of the object streams holding the objects reach, as their standings say.</summary>
        public int Reach { get; } = reach;

        /// <summary>
        /// Gets a value indicating whether the reader's index places each of the objects as the chain's does: a walk of a
        /// standing takes the run only then, and goes an object at a time elsewhere.
        /// </summary>
        public bool Agrees { get; } = agrees;
    }
}
