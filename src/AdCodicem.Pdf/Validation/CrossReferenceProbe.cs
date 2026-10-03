using System.Globalization;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.IO.XRef;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Validation;

/// <summary>
/// What probing each in-use entry of the file's own index found: whether its object is where the entry places
/// it, under the generation the entry gives, and — for an object stored in an object stream — whether that
/// stream holds it at the index the entry gives.
/// </summary>
/// <remarks>
/// <para>
/// It reads each object's header, a few dozen bytes, and nothing of its body; and each object stream once,
/// without keeping it, so that memory follows the largest of them rather than their sum. It probes the index the
/// file's chain gave, as the reader first read it — not one the reader has corrected or rebuilt since —, and
/// nothing when the chain gave none: the file rules have then said the index is unusable, and every entry would
/// only say it again.
/// </para>
/// <para>
/// Several rules read it, so the validation context works it out once. What it keeps is what it found wrong,
/// never an account of every entry.
/// </para>
/// </remarks>
internal sealed class CrossReferenceProbe
{
    /// <summary>How many bytes are read where an entry places its object: enough for its header after some white space.</summary>
    private const int HeaderProbeLength = 64;

    private CrossReferenceProbe()
    {
    }

    /// <summary>Gets the entries whose object is neither where they place it nor near it.</summary>
    public List<ProbeFinding> Broken { get; } = [];

    /// <summary>Gets the entries whose object is a few bytes from where they place it, or at another index of its stream.</summary>
    public List<ProbeFinding> Shifted { get; } = [];

    /// <summary>Gets the entries whose generation is not the one their object is written with.</summary>
    public List<ProbeFinding> GenerationMismatches { get; } = [];

    /// <summary>Gets the object streams, one finding each, that cannot serve the objects the index places in them.</summary>
    public List<ProbeFinding> BrokenObjectStreams { get; } = [];

    /// <summary>Gets what could not be checked: object streams a limit cut, or all of them in an encrypted document.</summary>
    public List<ProbeFinding> NotChecked { get; } = [];

    /// <summary>Gets how many entries place their object on the white space before its header.</summary>
    public int ImpreciseCount { get; private set; }

    /// <summary>Gets the first of those entries, in the order of object numbers.</summary>
    public ProbeFinding? FirstImprecise { get; private set; }

    /// <summary>Gets how many in-use objects are numbered above the trailer's <c>/Size</c>.</summary>
    public int PastSizeCount { get; private set; }

    /// <summary>Gets the lowest numbered of those objects.</summary>
    public ProbeFinding? FirstPastSize { get; private set; }

    /// <summary>Probes the index the document's chain gave.</summary>
    public static CrossReferenceProbe Run(PdfDocument document)
    {
        var probe = new CrossReferenceProbe();
        var reader = document.Reader;

        if (reader.ChainIndex is not { } index)
        {
            return probe;
        }

        var numbers = new int[index.Count];
        index.Entries.Keys.CopyTo(numbers, 0);
        Array.Sort(numbers);

        var size = reader.Structure.SizeAsWritten is PdfInteger { Value: >= 0 } written ? written.Value : -1;
        var packed = new SortedDictionary<int, List<(int Number, int Index)>>();
        Span<byte> buffer = stackalloc byte[HeaderProbeLength];

        foreach (var number in numbers)
        {
            // Read again for each entry: an object read meanwhile — an indirect /Length — may make the reader copy
            // the chain's index before changing its own, and the copy is the file's.
            if (number <= 0 || reader.ChainIndex?.TryGet(number, out var entry) != true || entry.Kind == XRefEntryKind.Free)
            {
                continue;
            }

            if (size >= 0 && number > size && probe.PastSizeCount++ == 0)
            {
                probe.FirstPastSize = new ProbeFinding(
                    PdfValidationLocation.OfObject(new PdfObjectId(number, entry.Generation)),
                    Invariant($"object {number}"));
            }

            if (entry.Kind == XRefEntryKind.Regular)
            {
                probe.ProbeObjectHeader(reader, number, entry, buffer);
            }
            else
            {
                if (!packed.TryGetValue(entry.ObjectStreamNumber, out var objects))
                {
                    objects = [];
                    packed.Add(entry.ObjectStreamNumber, objects);
                }

                objects.Add((number, entry.IndexInObjectStream));
            }
        }

        if (packed.Count > 0)
        {
            probe.CheckObjectStreams(document, reader, packed);
        }

        return probe;
    }

    private void ProbeObjectHeader(PdfFileReader reader, int number, XRefEntry entry, Span<byte> buffer)
    {
        var source = reader.Source;
        var id = new PdfObjectId(number, entry.Generation);
        var offset = entry.Offset + reader.HeaderOffset;

        if (offset < 0 || offset >= source.Length)
        {
            Broken.Add(new ProbeFinding(
                PdfValidationLocation.OfObject(id),
                Invariant($"The entry of object {number} gives offset {offset}, outside the file.")));
            return;
        }

        var header = ReadHeader(source, offset, buffer);

        if (header.Number == number)
        {
            if (header.Start > 0 && ImpreciseCount++ == 0)
            {
                FirstImprecise = new ProbeFinding(
                    PdfValidationLocation.OfObject(id, offset),
                    Invariant($"the entry of object {number} gives offset {offset}, {RuleText.Bytes(header.Start)} before its header"));
            }

            CheckGeneration(number, entry, offset + header.Start, header.Generation);
            return;
        }

        var found = header.Number > 0 ? Invariant($"the header of object {header.Number}") : "no object header";

        // The generation is the one the reader records for the object it relocates, read as the search reads it: a header
        // longer than the probe's own read is judged all the same (#118).
        if (PdfFileReader.TryFindObjectHeader(source, number, offset, out var actual, out var generation))
        {
            Shifted.Add(new ProbeFinding(
                PdfValidationLocation.OfObject(id, offset),
                Invariant($"The entry of object {number} gives offset {offset}, where there is {found}; the object starts {RuleText.Distance(actual - offset)}, at offset {actual}.")));

            CheckGeneration(number, entry, actual, generation);
            return;
        }

        Broken.Add(new ProbeFinding(
            PdfValidationLocation.OfObject(id, offset),
            Invariant($"The entry of object {number} gives offset {offset}, where there is {found}, and the object is not within {PdfFileReader.NearbySearchRadius} bytes of it.")));
    }

    private void CheckGeneration(int number, XRefEntry entry, long position, long generation)
    {
        if (generation >= 0 && generation != entry.Generation)
        {
            GenerationMismatches.Add(new ProbeFinding(
                PdfValidationLocation.OfObject(new PdfObjectId(number, entry.Generation), position),
                Invariant($"The entry of object {number} gives generation {entry.Generation}, and the object is written as {number} {generation} obj.")));
        }
    }

    /// <summary>
    /// Reads the object header at <paramref name="offset"/>, after any white space or comment: its number — 0 when
    /// there is none —, its generation, and how far into the bytes read it starts.
    /// </summary>
    /// <remarks>
    /// A header the reader's parser refuses — a number or a generation no object can have — is none: no read can serve an
    /// object there.
    /// </remarks>
    private static (long Number, long Generation, int Start) ReadHeader(PdfFileSource source, long offset, Span<byte> buffer)
    {
        var read = source.Read(offset, buffer);
        var lexer = new PdfLexer(buffer[..read]);
        var number = lexer.Read();
        var generation = lexer.Read();
        var keyword = lexer.Read();

        return number.Kind == PdfTokenKind.Integer && generation.Kind == PdfTokenKind.Integer && keyword.IsKeyword("obj"u8) &&
            number.Integer is > 0 and <= PdfObjectId.MaxNumber && generation.Integer is >= 0 and <= PdfObjectId.MaxGeneration
            ? (number.Integer, generation.Integer, number.Start)
            : (0, -1, 0);
    }

    private void CheckObjectStreams(
        PdfDocument document, PdfFileReader reader, SortedDictionary<int, List<(int Number, int Index)>> packed)
    {
        if (document.IsEncrypted)
        {
            // An encrypted document's object streams are encrypted with it: until the library decrypts (M16),
            // what they hold cannot be read, and saying anything about it would be a guess.
            var objects = 0;
            foreach (var (_, list) in packed)
            {
                objects += list.Count;
            }

            NotChecked.Add(new ProbeFinding(
                default,
                Invariant($"The document is encrypted: the {RuleText.Objects(objects)} its index places in object streams were not checked, those streams being readable only once decrypted.")));
            return;
        }

        foreach (var (stream, objects) in packed)
        {
            CheckObjectStream(reader, stream, objects);
        }
    }

    private void CheckObjectStream(PdfFileReader reader, int stream, List<(int Number, int Index)> objects)
    {
        var id = new PdfObjectId(stream);

        if (reader.ChainIndex?.TryGet(stream, out var entry) != true || entry.Kind == XRefEntryKind.Free)
        {
            BrokenObjectStreams.Add(new ProbeFinding(
                PdfValidationLocation.OfObject(id),
                Invariant($"The index places {RuleText.Objects(objects.Count)} in object stream {stream}, which it does not hold as an object.")));
            return;
        }

        if (entry.Kind == XRefEntryKind.Compressed)
        {
            BrokenObjectStreams.Add(new ProbeFinding(
                PdfValidationLocation.OfObject(id),
                Invariant($"The index places {RuleText.Objects(objects.Count)} in object stream {stream}, which it places in object stream {entry.ObjectStreamNumber} in turn: an object stream cannot be stored in another.")));
            return;
        }

        var offset = entry.Offset + reader.HeaderOffset;

        switch (reader.ReadObjectStreamHeader(stream, offset, out var numbers, out var fault))
        {
            case ObjectStreamHeaderResult.NotFound:
                // The stream's own entry is broken, which its probe reports; what it holds cannot be told.
                return;

            case ObjectStreamHeaderResult.CutByLimit:
                NotChecked.Add(new ProbeFinding(
                    PdfValidationLocation.OfObject(id, offset),
                    Invariant($"One of the reader's limits stopped it reading object stream {stream} before its header ended: the {RuleText.Objects(objects.Count)} the index places in it were not checked. Raising the limit the reader reported lets them be.")));
                return;

            case ObjectStreamHeaderResult.NotAnObjectStream:
                BrokenObjectStreams.Add(new ProbeFinding(
                    PdfValidationLocation.OfObject(id, offset),
                    Invariant($"Object {stream}, where the index places {RuleText.Objects(objects.Count)}, {fault}.")));
                return;

            case ObjectStreamHeaderResult.Unreadable:
                BrokenObjectStreams.Add(new ProbeFinding(
                    PdfValidationLocation.OfObject(id, offset),
                    Invariant($"Object stream {stream}, where the index places {RuleText.Objects(objects.Count)}, cannot be read: {fault}.")));
                return;
        }

        foreach (var (number, index) in objects)
        {
            if (index >= 0 && index < numbers.Length && numbers[index] == number)
            {
                continue;
            }

            var actual = Array.IndexOf(numbers, number);

            if (actual >= 0)
            {
                Shifted.Add(new ProbeFinding(
                    PdfValidationLocation.OfObject(new PdfObjectId(number)),
                    Invariant($"The entry of object {number} places it at index {index} of object stream {stream}, whose header lists it at index {actual}.")));
            }
            else
            {
                Broken.Add(new ProbeFinding(
                    PdfValidationLocation.OfObject(new PdfObjectId(number)),
                    Invariant($"The entry of object {number} places it at index {index} of object stream {stream}, whose header does not list it.")));
            }
        }
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
