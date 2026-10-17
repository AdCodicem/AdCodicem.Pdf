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
/// It reads each object's header as a load reads it, and nothing of its body: what the reader decides of each entry as it
/// settles its index (<see cref="PdfFileReader.PlaceHeader"/>), so that an entry a load takes is never called broken, and
/// one a load gives up on never taken. A header is read in a few dozen bytes, then in windows grown eightfold, up to
/// <see cref="Documents.PdfReaderLimits.MaxObjectLength"/>; over all the headers, what their reading consumes past the first
/// 64 bytes of each — the bytes crossed up to each decision, a run of white space several entries lead into once — is at
/// most twice the file's length, which a valid file's headers, distinct bytes, never reach, and an entry left undecided
/// past that is said not checked, as one whose header runs past the limit is. An entry outside the file is looked for
/// near its offset, as a load looks for it. It reads each object stream once, without keeping it, so that memory follows the
/// largest of them rather than their sum. It probes the index the file's chain gave, as the reader first read it — not
/// one the reader has corrected or rebuilt since —, and nothing when the chain gave none: the file rules have then said
/// the index is unusable, and every entry would only say it again.
/// </para>
/// <para>
/// Several rules read it, so the validation context works it out once. What it keeps is what it found wrong,
/// never an account of every entry.
/// </para>
/// </remarks>
internal sealed class CrossReferenceProbe
{
    /// <summary>What the headers' reading may still consume past the first bytes of each, and the run of white space it crossed.</summary>
    private readonly HeaderReading _reading;

    /// <summary>How many entries the probe left undecided, its reading spent.</summary>
    private int _undecidedCount;

    /// <summary>The first of those entries, in the order of object numbers.</summary>
    private ProbeFinding? _firstUndecided;

    private CrossReferenceProbe(long allowance) => _reading = new HeaderReading(allowance);

    /// <summary>Gets the entries whose object is neither where they place it nor near it.</summary>
    public List<ProbeFinding> Broken { get; } = [];

    /// <summary>Gets the entries whose object is a few bytes from where they place it, or at another index of its stream.</summary>
    public List<ProbeFinding> Shifted { get; } = [];

    /// <summary>Gets the entries whose generation is not the one their object is written with.</summary>
    public List<ProbeFinding> GenerationMismatches { get; } = [];

    /// <summary>Gets the object streams, one finding each, that cannot serve the objects the index places in them.</summary>
    public List<ProbeFinding> BrokenObjectStreams { get; } = [];

    /// <summary>
    /// Gets what could not be checked: an entry whose header runs past a limit, entries left undecided past what the probe
    /// may read, object streams a limit cut, or all of them in an encrypted document.
    /// </summary>
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
        var reader = document.Reader;
        var probe = new CrossReferenceProbe(reader.HeaderReadingAllowance);

        if (reader.ChainIndex is not { } index)
        {
            return probe;
        }

        var numbers = new int[index.Count];
        index.Entries.Keys.CopyTo(numbers, 0);
        Array.Sort(numbers);

        var size = reader.Structure.SizeAsWritten is PdfInteger { Value: >= 0 } written ? written.Value : -1;
        var packed = new SortedDictionary<int, List<(int Number, int Index)>>();

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
                probe.ProbeObjectHeader(reader, number, entry);
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

        if (probe._firstUndecided is { } first)
        {
            const string Stopped = "The validator stopped reading object headers where entries place them once it had read twice the file's length of them, past the first bytes of each";

            probe.NotChecked.Add(new ProbeFinding(
                first.Location,
                probe._undecidedCount == 1
                    ? Invariant($"{Stopped}: the entry of {first.Message}, which it had not decided by then, was not checked.")
                    : Invariant($"{Stopped}: the entries of {RuleText.Objects(probe._undecidedCount)} it had not decided by then, the first {first.Message}'s, were not checked.")));
        }

        if (packed.Count > 0)
        {
            probe.CheckObjectStreams(document, reader, packed);
        }

        return probe;
    }

    private void ProbeObjectHeader(PdfFileReader reader, int number, XRefEntry entry)
    {
        var id = new PdfObjectId(number, entry.Generation);
        var offset = reader.PositionOf(entry);

        // An offset outside the file is no place to locate a finding at, and a load looks near it all the same: an object it
        // finds there, in the file, is one it relocates.
        var inFile = reader.IsInFile(offset);
        var at = inFile ? PdfValidationLocation.OfObject(id, offset) : PdfValidationLocation.OfObject(id);
        var placement = reader.PlaceHeader(number, entry, _reading);

        switch (placement.Place)
        {
            case HeaderPlace.AtOffset:
                if (placement.Position > offset && ImpreciseCount++ == 0)
                {
                    FirstImprecise = new ProbeFinding(
                        PdfValidationLocation.OfObject(id, offset),
                        Invariant($"the entry of object {number} gives offset {offset}, {RuleText.Bytes(placement.Position - offset)} before its header"));
                }

                CheckGeneration(number, entry, placement.Position, placement.Generation);
                return;

            case HeaderPlace.Nearby when !placement.GuardAtOffset:
                // The generation is the one the reader records for the object it relocates, read as a load reads it: a
                // header longer than a first read is judged all the same (#118).
                var found = !inFile ? "outside the file" :
                    placement.NumberAtOffset > 0 ? Invariant($"where there is the header of object {placement.NumberAtOffset}") :
                    "where there is no object header";

                Shifted.Add(new ProbeFinding(
                    at,
                    Invariant($"The entry of object {number} gives offset {Named(reader, entry, offset, inFile)}, {found}; the object starts {RuleText.Distance(placement.Position - offset)}, at offset {placement.Position}.")));

                CheckGeneration(number, entry, placement.Position, placement.Generation);
                return;

            case HeaderPlace.NotFound when !inFile:
                Broken.Add(new ProbeFinding(
                    at,
                    Invariant($"The entry of object {number} gives offset {Named(reader, entry, offset, inFile)}, outside the file.")));
                return;

            case HeaderPlace.NotFound:
                var there = placement.NumberAtOffset > 0 ? Invariant($"the header of object {placement.NumberAtOffset}") : "no object header";

                Broken.Add(new ProbeFinding(
                    at,
                    Invariant($"The entry of object {number} gives offset {offset}, where there is {there}, and the object is not within {PdfFileReader.NearbySearchRadius} bytes of it.")));
                return;

            case HeaderPlace.Undecided:
                if (_undecidedCount++ == 0)
                {
                    _firstUndecided = new ProbeFinding(at, Invariant($"object {number}"));
                }

                return;

            default:
                // A header that runs past the object guard, where the entry places the object or near it: what a load finds
                // there is the limit's to say, and raising it lets the entry be checked.
                NotChecked.Add(new ProbeFinding(
                    at,
                    Invariant($"Reading the header of object {number} where its entry places it, at offset {Named(reader, entry, offset, inFile)}, or near it, the reader reached PdfReaderLimits.MaxObjectLength: the entry was not checked. Raising the limit lets it be.")));
                return;
        }
    }

    /// <summary>Writes the offset an entry gives, as a finding names it: built only for a finding.</summary>
    private static string Named(PdfFileReader reader, XRefEntry entry, long offset, bool inFile) =>
        inFile ? offset.ToString(CultureInfo.InvariantCulture) : reader.DescribeOffset(entry.Offset);

    private void CheckGeneration(int number, XRefEntry entry, long position, long generation)
    {
        if (generation >= 0 && generation != entry.Generation)
        {
            GenerationMismatches.Add(new ProbeFinding(
                PdfValidationLocation.OfObject(new PdfObjectId(number, entry.Generation), position),
                Invariant($"The entry of object {number} gives generation {entry.Generation}, and the object is written as {number} {generation} obj.")));
        }
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
            // An object stream one of the reader's limits left out of the chain's index is unknown, and what it holds
            // unchecked, though a rebuild found it since: xref.checked-in-part says the index was stopped short.
            if (reader.ChainStoppedByGuard)
            {
                return;
            }

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

        var offset = reader.PositionOf(entry);

        // A row outside the file, its stream found near it, places it nowhere in the file: the finding is located at the
        // object alone, as the entry's own is (#125).
        var at = reader.IsInFile(offset) ? PdfValidationLocation.OfObject(id, offset) : PdfValidationLocation.OfObject(id);

        switch (reader.ReadObjectStreamHeader(stream, offset, out var numbers, out var fault))
        {
            case ObjectStreamHeaderResult.NotFound:
                // The stream's own entry is broken, which its probe reports; what it holds cannot be told.
                return;

            case ObjectStreamHeaderResult.CutByLimit:
                NotChecked.Add(new ProbeFinding(
                    at,
                    Invariant($"One of the reader's limits stopped it reading object stream {stream} before its header ended: the {RuleText.Objects(objects.Count)} the index places in it were not checked. Raising the limit the reader reported lets them be.")));
                return;

            case ObjectStreamHeaderResult.NotAnObjectStream:
                BrokenObjectStreams.Add(new ProbeFinding(
                    at,
                    Invariant($"Object {stream}, where the index places {RuleText.Objects(objects.Count)}, {fault}.")));
                return;

            case ObjectStreamHeaderResult.Unreadable:
                BrokenObjectStreams.Add(new ProbeFinding(
                    at,
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
