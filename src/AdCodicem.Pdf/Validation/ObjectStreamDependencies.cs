using System.Globalization;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.IO.XRef;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Validation;

/// <summary>
/// Which object streams need, to be read, an object that only reading them — or reading another stream that needs
/// them in turn — can give: a <c>/Type</c>, <c>/Length</c>, <c>/Filter</c>, <c>/DecodeParms</c>, <c>/N</c> or
/// <c>/First</c> naming an object the stream holds (#51).
/// </summary>
/// <remarks>
/// <para>
/// It is worked out from the stream dictionaries and the index, not from what the reader happened to do: each stream the
/// index uses as an object stream, in the order of its number, has the references under those keys followed, as the reader
/// follows them, through objects written directly in the file, until one names an object the index places in an object
/// stream — a filter and its parameters through their direct arrays and dictionaries too, whose elements and entries the
/// reader resolves, and the others, a name or a number, through a chain of references alone, as a reference resolves.
/// The streams so needed are linked, and a stream that its links lead back to is circular.
/// </para>
/// <para>
/// The reader resolves a stream's <c>/Length</c> as it parses it, then its <c>/Type</c>, and nothing else of one that is
/// not of <c>/Type /ObjStm</c> (#274): of such a stream, only its <c>/Type</c> is followed. A loop through its
/// <c>/Type</c> is why it is not taken for an object stream, and is the fault; a loop through its <c>/Length</c> alone
/// leaves its objects lost to its type all the same, which <see cref="PdfValidationRuleIds.XRefObjectStreamBroken"/> says.
/// </para>
/// <para>
/// It reads each stream's dictionary, and the few objects written directly in the file its keys name, where the chain's
/// index places them — not through the index the reader reads with now, which a rebuild may have made another —, and
/// decodes nothing. The chain's index is read again for each lookup: until the reader first changes its own, the two are
/// one table (#128). An object written directly in the file is read once, however many streams lead through it, under
/// whatever generation, and what is kept of it is the references it holds — of as many objects as the reader keeps in its
/// cache, an object let go read again when met again. As it holds each dictionary, it also notes the integers of its header
/// written as reals.
/// </para>
/// </remarks>
internal sealed class ObjectStreamDependencies
{
    /// <summary>
    /// The keys of an object stream whose values must be known before its data can be read, its length, which its parse
    /// resolves, and its type first.
    /// </summary>
    private static readonly PdfName[] DecodingKeys = [PdfName.Length, PdfName.Type, PdfName.Filter, PdfName.DecodeParms, PdfName.N, PdfName.First];

    /// <summary>The key that decides whether the reader takes a stream for an object stream.</summary>
    private static readonly PdfName[] TypeKey = [PdfName.Type];

    /// <summary>The keys of an object stream that ISO 32000-1 makes integers (Tables 5 and 16).</summary>
    private static readonly PdfName[] IntegerKeys = [PdfName.N, PdfName.First, PdfName.Length];

    private ObjectStreamDependencies()
    {
    }

    /// <summary>Gets the circular object streams, one finding each, in the order of their numbers.</summary>
    public List<ProbeFinding> Circular { get; } = [];

    /// <summary>Gets the numbers of the circular object streams.</summary>
    public HashSet<int> CircularStreams { get; } = [];

    /// <summary>
    /// Gets the object streams whose <c>/N</c>, <c>/First</c> or <c>/Length</c> is a real number with no fractional part,
    /// where ISO 32000-1 asks for an integer (Tables 5 and 16): one finding per stream and key, in the order of their
    /// numbers. The reader reads each as the integer it equals (#215).
    /// </summary>
    public List<ProbeFinding> IntegersWrittenAsReals { get; } = [];

    /// <summary>Works out which object streams of <paramref name="document"/> are circular.</summary>
    public static ObjectStreamDependencies Run(PdfDocument document)
    {
        var result = new ObjectStreamDependencies();
        var reader = document.Reader;
        var streams = new SortedSet<int>();

        foreach (var (_, entry) in IndexOf(reader).Entries)
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

        // What each stream needs: the first object of another stream, or of itself, found under each decoding key; and where
        // the chain's row places each stream read.
        var needs = new Dictionary<int, List<Need>>();
        var locations = new Dictionary<int, PdfValidationLocation>();

        // The references each object written in the file holds, which the decoding keys lead through: read once for every
        // stream they lead from, as many objects kept as the reader keeps in its cache, and nothing else of them.
        var references = new ReferenceMemo(reader.CacheCapacity);

        // A stream that is not of /Type /ObjStm serves no object, to the reader, the probe or the rebuild alike (#274):
        // nothing is read from its data, so nothing it names is needed but what its /Type does, and none of its values is
        // read as an integer. A /Type that names an object the stream holds is a need, as any other key's.
        foreach (var stream in streams)
        {
            if (IndexOf(reader).TryGet(stream, out var entry) && entry.Kind == XRefEntryKind.Regular &&
                Read(document, new PdfObjectId(stream, entry.Generation), entry) is PdfStream objectStream)
            {
                if (!objectStream.Dictionary.IsOfType(PdfName.ObjStm))
                {
                    if (Needs(document, objectStream.Dictionary, TypeKey, references) is { Count: > 0 } typeNeeds)
                    {
                        locations[stream] = Location(reader, stream, entry);
                        needs[stream] = typeNeeds;
                    }

                    continue;
                }

                locations[stream] = Location(reader, stream, entry);
                needs[stream] = Needs(document, objectStream.Dictionary, DecodingKeys, references);

                foreach (var key in IntegerKeys)
                {
                    if (objectStream.Dictionary.GetRaw(key)?.Resolve() is PdfReal real && real.AsInteger() is not null)
                    {
                        result.IntegersWrittenAsReals.Add(new ProbeFinding(
                            locations[stream],
                            Invariant($"Object stream {stream} gives its {FileQuote.Name(key)} as a real number, where Table {(key == PdfName.Length ? 5 : 16)} of ISO 32000-1 asks for an integer; the reader read it as the integer it equals.")));
                    }
                }
            }
        }

        foreach (var stream in streams)
        {
            if (needs.TryGetValue(stream, out var own) && FindWayBack(stream, own, needs) is { } need)
            {
                result.CircularStreams.Add(stream);
                result.Circular.Add(new ProbeFinding(
                    locations[stream],
                    need.Stream == stream
                        ? Invariant($"Object stream {stream} needs object {need.Object}, which it holds, to be read: its {need.Key} names it, and the object cannot be read before the stream is.")
                        : Invariant($"Object stream {stream} needs object {need.Object}, which object stream {need.Stream} holds, to be read: its {need.Key} names it, and reading object stream {need.Stream} needs object stream {stream} in turn.")));
            }
        }

        return result;
    }

    /// <summary>
    /// Lists the objects, held in object streams, that <paramref name="keys"/> of <paramref name="dictionary"/> lead to,
    /// through the objects written in the file whose references <paramref name="references"/> holds, or which it reads and
    /// holds.
    /// </summary>
    private static List<Need> Needs(PdfDocument document, PdfDictionary dictionary, PdfName[] keys, ReferenceMemo references)
    {
        var found = new List<Need>();
        var seen = new HashSet<int>();
        var work = new Stack<PdfObjectId>();

        foreach (var key in keys)
        {
            if (dictionary.GetRaw(key) is not { } value)
            {
                continue;
            }

            // A filter and its parameters are read whole, each element and entry resolved; the other values, a name or a
            // number, as a reference resolves — the object it names, while that is a reference in turn —, nothing written
            // inside an array or a dictionary read for them.
            var whole = key == PdfName.Filter || key == PdfName.DecodeParms;

            if (whole)
            {
                Push(work, ReferencesIn(value));
            }
            else if (value is PdfReference reference)
            {
                work.Push(reference.Id);
            }

            while (work.TryPop(out var id))
            {
                // Object 0 heads the free list and is never in use (ISO 32000-1, 7.5.4): the reader never reads it,
                // whatever the index's first entry says, so no stream needs it.
                if (id.Number <= 0 || !seen.Add(id.Number) || !IndexOf(document.Reader).TryGet(id.Number, out var entry))
                {
                    continue;
                }

                if (entry.Kind == XRefEntryKind.Compressed)
                {
                    found.Add(new Need(entry.ObjectStreamNumber, id.Number, FileQuote.Name(key)));
                }
                else if (entry.Kind == XRefEntryKind.Regular)
                {
                    if (!references.TryGet(id.Number, out var held))
                    {
                        var read = Read(document, id, entry);
                        held = new HeldReferences(ReferencesIn(read), read is PdfReference);
                        references.Add(id.Number, held);
                    }

                    if (whole)
                    {
                        Push(work, held.References);
                    }
                    else if (held.IsReference)
                    {
                        work.Push(held.References[0]);
                    }
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Gives the references <paramref name="value"/> holds, in its arrays and dictionaries, in the order a walk taking the
    /// last value it met first meets them; the objects they name are not read.
    /// </summary>
    private static PdfObjectId[] ReferencesIn(PdfObject value)
    {
        var found = new List<PdfObjectId>();
        var work = new Stack<PdfObject>();
        work.Push(value);

        while (work.TryPop(out var item))
        {
            switch (item)
            {
                case PdfReference reference:
                    found.Add(reference.Id);
                    break;

                case PdfArray array:
                    foreach (var element in array)
                    {
                        work.Push(element);
                    }

                    break;

                case PdfDictionary dictionary:
                    foreach (var (_, element) in dictionary)
                    {
                        work.Push(element);
                    }

                    break;
            }
        }

        return [.. found];
    }

    /// <summary>Pushes <paramref name="references"/> so that the first of them is the next taken.</summary>
    private static void Push(Stack<PdfObjectId> work, PdfObjectId[] references)
    {
        for (var index = references.Length - 1; index >= 0; index--)
        {
            work.Push(references[index]);
        }
    }

    /// <summary>Gives the need of <paramref name="stream"/> that leads back to it, directly or through other streams, if any.</summary>
    private static Need? FindWayBack(int stream, List<Need> own, Dictionary<int, List<Need>> needs)
    {
        foreach (var need in own)
        {
            if (need.Stream == stream || Reaches(need.Stream, stream, needs))
            {
                return need;
            }
        }

        return null;
    }

    private static bool Reaches(int from, int target, Dictionary<int, List<Need>> needs)
    {
        var seen = new HashSet<int> { from };
        var work = new Stack<int>();
        work.Push(from);

        while (work.TryPop(out var current))
        {
            if (!needs.TryGetValue(current, out var next))
            {
                continue;
            }

            foreach (var need in next)
            {
                if (need.Stream == target)
                {
                    return true;
                }

                if (seen.Add(need.Stream))
                {
                    work.Push(need.Stream);
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Gives the index the walk judges: the chain's, read again for each lookup, since the reader copies it before it first
    /// changes its own; or the one rebuilt as the document opened, when the chain gave none.
    /// </summary>
    private static PdfXRefTable IndexOf(IO.PdfFileReader reader) => reader.ChainIndex ?? reader.Index;

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
    private static PdfValidationLocation Location(IO.PdfFileReader reader, int stream, XRefEntry entry) =>
        At(reader, new PdfObjectId(stream, entry.Generation), reader.PositionOf(entry));

    /// <summary>Locates object <paramref name="id"/> at <paramref name="position"/>, or at the object alone outside the file.</summary>
    private static PdfValidationLocation At(IO.PdfFileReader reader, PdfObjectId id, long position) =>
        reader.IsInFile(position) ? PdfValidationLocation.OfObject(id, position) : PdfValidationLocation.OfObject(id);

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    /// <summary>An object a stream needs to be read, the object stream holding it, and the key that leads to it.</summary>
    private readonly record struct Need(int Stream, int Object, string Key);

    /// <summary>The references an object written in the file holds, and whether its value is one of them, alone.</summary>
    private readonly record struct HeldReferences(PdfObjectId[] References, bool IsReference);

    /// <summary>
    /// The references held by objects the walk read, by object number — a reference's generation leads the reader to no
    /// other object —: as many objects as <paramref name="capacity"/> says, the oldest let go first, and read again when met
    /// again, so that what the walk keeps follows the reader's cache rather than the file.
    /// </summary>
    private sealed class ReferenceMemo(int capacity)
    {
        private readonly Dictionary<int, HeldReferences> _held = [];
        private readonly Queue<int> _order = new();

        /// <summary>Gives the references object <paramref name="number"/> holds, if they are kept.</summary>
        public bool TryGet(int number, out HeldReferences references) => _held.TryGetValue(number, out references);

        /// <summary>Keeps the references object <paramref name="number"/>, which is not kept yet, holds.</summary>
        public void Add(int number, HeldReferences references)
        {
            if (_held.Count >= capacity && _order.TryDequeue(out var oldest))
            {
                _held.Remove(oldest);
            }

            _held[number] = references;
            _order.Enqueue(number);
        }
    }
}
