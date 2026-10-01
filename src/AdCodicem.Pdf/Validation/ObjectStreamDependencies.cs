using System.Globalization;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO.XRef;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Validation;

/// <summary>
/// Which object streams need, to be read, an object that only reading them — or reading another stream that needs
/// them in turn — can give: a <c>/Length</c>, <c>/Filter</c>, <c>/DecodeParms</c>, <c>/N</c> or <c>/First</c> naming
/// an object the stream holds (#51).
/// </summary>
/// <remarks>
/// <para>
/// It is worked out from the stream dictionaries and the index, not from what the reader happened to do: each object
/// stream the index uses, in the order of its number, has the references under those keys followed — through direct
/// arrays and dictionaries, and through objects written directly in the file — until one names an object the index
/// places in an object stream. The streams so needed are linked, and a stream that its links lead back to is
/// circular.
/// </para>
/// <para>
/// It resolves each stream's dictionary and the few objects its keys name, and decodes nothing.
/// </para>
/// </remarks>
internal sealed class ObjectStreamDependencies
{
    /// <summary>The keys of an object stream whose values must be known before its data can be read.</summary>
    private static readonly PdfName[] DecodingKeys = [PdfName.Length, PdfName.Filter, PdfName.DecodeParms, PdfName.N, PdfName.First];

    private ObjectStreamDependencies()
    {
    }

    /// <summary>Gets the circular object streams, one finding each, in the order of their numbers.</summary>
    public List<ProbeFinding> Circular { get; } = [];

    /// <summary>Gets the numbers of the circular object streams.</summary>
    public HashSet<int> CircularStreams { get; } = [];

    /// <summary>Works out which object streams of <paramref name="document"/> are circular.</summary>
    public static ObjectStreamDependencies Run(PdfDocument document)
    {
        var result = new ObjectStreamDependencies();
        var reader = document.Reader;
        var index = reader.ChainIndex ?? reader.Index;
        var streams = new SortedSet<int>();

        foreach (var (_, entry) in index.Entries)
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

        // What each stream needs: the first object of another stream, or of itself, found under each decoding key.
        var needs = new Dictionary<int, List<Need>>();

        foreach (var stream in streams)
        {
            if (index.TryGet(stream, out var entry) && entry.Kind == XRefEntryKind.Regular &&
                document.GetObject(new PdfObjectId(stream, entry.Generation)) is PdfStream objectStream)
            {
                needs[stream] = Needs(document, index, objectStream.Dictionary);
            }
        }

        foreach (var stream in streams)
        {
            if (needs.TryGetValue(stream, out var own) && FindWayBack(stream, own, needs) is { } need)
            {
                result.CircularStreams.Add(stream);
                result.Circular.Add(new ProbeFinding(
                    Location(reader, index, stream),
                    need.Stream == stream
                        ? Invariant($"Object stream {stream} needs object {need.Object}, which it holds, to be read: its {need.Key} names it, and the object cannot be read before the stream is.")
                        : Invariant($"Object stream {stream} needs object {need.Object}, which object stream {need.Stream} holds, to be read: its {need.Key} names it, and reading object stream {need.Stream} needs object stream {stream} in turn.")));
            }
        }

        return result;
    }

    /// <summary>Lists the objects, held in object streams, that the decoding keys of <paramref name="dictionary"/> lead to.</summary>
    private static List<Need> Needs(PdfDocument document, PdfXRefTable index, PdfDictionary dictionary)
    {
        var found = new List<Need>();
        var seen = new HashSet<int>();
        var work = new Stack<PdfObject>();

        foreach (var key in DecodingKeys)
        {
            if (dictionary.GetRaw(key) is not { } value)
            {
                continue;
            }

            work.Push(value);

            while (work.TryPop(out var item))
            {
                switch (item)
                {
                    // Object 0 heads the free list and is never in use (ISO 32000-1, 7.5.4): the reader never reads it,
                    // whatever the index's first entry says, so no stream needs it.
                    case PdfReference reference when reference.Id.Number > 0 && seen.Add(reference.Id.Number) && index.TryGet(reference.Id.Number, out var entry):
                        if (entry.Kind == XRefEntryKind.Compressed)
                        {
                            found.Add(new Need(entry.ObjectStreamNumber, reference.Id.Number, RuleText.Name(key)));
                        }
                        else if (entry.Kind == XRefEntryKind.Regular)
                        {
                            work.Push(document.GetObject(reference.Id));
                        }

                        break;

                    case PdfArray array:
                        foreach (var element in array)
                        {
                            work.Push(element);
                        }

                        break;

                    case PdfDictionary nested:
                        foreach (var (_, element) in nested)
                        {
                            work.Push(element);
                        }

                        break;
                }
            }
        }

        return found;
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

    private static PdfValidationLocation Location(IO.PdfFileReader reader, PdfXRefTable index, int stream) =>
        index.TryGet(stream, out var entry) && entry.Kind == XRefEntryKind.Regular
            ? PdfValidationLocation.OfObject(new PdfObjectId(stream, entry.Generation), entry.Offset + reader.HeaderOffset)
            : PdfValidationLocation.OfObject(new PdfObjectId(stream));

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    /// <summary>An object a stream needs to be read, the object stream holding it, and the key that leads to it.</summary>
    private readonly record struct Need(int Stream, int Object, string Key);
}
