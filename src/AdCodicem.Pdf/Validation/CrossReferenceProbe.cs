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
/// 64 bytes of each — the bytes crossed up to each decision, a run of white space several entries lead into once, and past a
/// run an entry skips every byte it reads — is at
/// most twice the file's length, which a valid file's headers, distinct bytes, never reach, and an entry left undecided
/// past that is said not checked, as one whose header runs past the limit is. An entry outside the file is looked for
/// near its offset, as a load looks for it. It reads each object stream once, without keeping it — once more when the
/// reader rebuilt its index, the chain's places some of its objects and only the rebuilt one others, or the rebuilt one
/// serves elsewhere some the chain's places —, so that memory
/// follows the largest of them
/// rather than their sum, each member whose bytes hold <c>stream</c> parsed as a load parses it. It probes
/// the index the file's chain gave, as the reader first read it — not one the reader has corrected or rebuilt since —,
/// and nothing of it when the chain gave none: the file and cross-reference rules have then said why, and every entry would
/// only say it again. Of the index a rebuild made, it judges the members written as a stream that it places where the
/// chain's places nothing — or, the chain stopped short by a guard, where the part of it the reader read places nothing —,
/// which the reader reads as null and no entry of the file's the reader read would name (#161, #202); and, of the objects
/// the chain's index places soundly in an object stream, those it lacks or serves from an object stream that does not serve
/// them, which the reader loses though the chain's entry is sound (#276, #349).
/// </para>
/// <para>
/// Several rules read it, so the validation context works it out once. What it keeps is what it found wrong, never an
/// account of every entry, and of each kind of fault no more than the report has room for: it is made with that room,
/// writes the first faults of each kind up to it and counts the rest, which their rules count in turn (#168). The object
/// streams it finds broken are the exception, a record each — numbers, and the reader's fault —, written by their rule,
/// which passes over those the dependency walk finds circular, something the probe cannot know: one record per stream
/// number the index places objects in at most, which <see cref="Documents.PdfReaderLimits.MaxIndexEntries"/> bounds as
/// it bounds the index.
/// </para>
/// </remarks>
internal sealed class CrossReferenceProbe
{
    /// <summary>What corrects an object written as a stream inside an object stream: no entry can.</summary>
    private const string StreamMemberRemedy = "Write the object outside the object stream, and index it there.";

    /// <summary>What the headers' reading may still consume past the first bytes of each, and the run of white space it crossed.</summary>
    private readonly HeaderReading _reading;

    /// <summary>How many entries the probe left undecided, its reading spent.</summary>
    private int _undecidedCount;

    /// <summary>The first of those entries, in the order of object numbers.</summary>
    private ProbeFinding? _firstUndecided;

    private CrossReferenceProbe(long allowance, int room)
    {
        _reading = new HeaderReading(allowance);
        Broken = new(room);
        Shifted = new(room);
        Unconfirmed = new(room);
        GenerationMismatches = new(room);
        NotChecked = new(room);
    }

    /// <summary>Gets the entries whose object is neither where they place it nor near it.</summary>
    public ProbeFindings Broken { get; }

    /// <summary>Gets the entries whose object is a few bytes from where they place it, or at another index of its stream.</summary>
    public ProbeFindings Shifted { get; }

    /// <summary>
    /// Gets the entries that lead the reader to a header of their object it does not vouch for
    /// (<see cref="PdfFileReader.PlaceHeader"/>).
    /// </summary>
    public ProbeFindings Unconfirmed { get; }

    /// <summary>Gets the entries whose generation is not the one their object is written with.</summary>
    public ProbeFindings GenerationMismatches { get; }

    /// <summary>
    /// Gets the object streams that cannot serve the objects the index places in them, a record each, in the order of their
    /// numbers: every one, whatever the room, their rule passing over those the dependency walk finds circular.
    /// </summary>
    public List<BrokenObjectStream> BrokenObjectStreams { get; } = [];

    /// <summary>
    /// Gets what could not be checked: an entry whose header runs past a limit, entries left undecided past what the probe
    /// may read, object streams a limit cut, or all of them in an encrypted document.
    /// </summary>
    public ProbeFindings NotChecked { get; }

    /// <summary>Gets how many entries place their object on the white space before its header.</summary>
    public int ImpreciseCount { get; private set; }

    /// <summary>Gets the first of those entries, in the order of object numbers.</summary>
    public ProbeFinding? FirstImprecise { get; private set; }

    /// <summary>Gets how many in-use objects are numbered above the trailer's <c>/Size</c>.</summary>
    public int PastSizeCount { get; private set; }

    /// <summary>Gets the lowest numbered of those objects.</summary>
    public ProbeFinding? FirstPastSize { get; private set; }

    /// <summary>
    /// Probes the index the document's chain gave, writing of each kind of fault the first <paramref name="room"/> it finds
    /// and counting the rest: the room the report has when the probe is made, or every fault outside a validation.
    /// </summary>
    public static CrossReferenceProbe Run(PdfDocument document, int room = int.MaxValue)
    {
        var reader = document.Reader;
        var probe = new CrossReferenceProbe(reader.HeaderReadingAllowance, room);

        if (reader.ChainIndex is not { } index)
        {
            probe.CheckMembersOnlyARebuildPlaces(document, reader);
            return probe;
        }

        var numbers = index.CopyNumbers();
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

        if (probe._firstUndecided is { } first && probe.NotChecked.Take())
        {
            const string Stopped = "The validator stopped reading object headers where entries place them once it had read twice the file's length of them, past the first bytes of each";

            probe.NotChecked.Keep(new ProbeFinding(
                first.Location,
                probe._undecidedCount == 1
                    ? Invariant($"{Stopped}: the entry of {first.Message}, which it had not decided by then, was not checked.")
                    : Invariant($"{Stopped}: the entries of {CountText.Objects(probe._undecidedCount)} it had not decided by then, the first {first.Message}'s, were not checked.")));
        }

        if (packed.Count > 0)
        {
            // The objects the rebuild may have lost are kept only until they are judged: the probe keeps what it found wrong.
            List<ServedElsewhere>? servedElsewhere = null;
            probe.CheckObjectStreams(document, reader, packed, ref servedElsewhere);
            probe.CheckMembersARebuildServesElsewhere(reader, servedElsewhere);
        }

        probe.CheckMembersOnlyARebuildPlaces(document, reader);
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
            case HeaderPlace.AtOffset when placement.Confirmation == HeaderConfirmation.Vouched:
                if (placement.Position > offset && ImpreciseCount++ == 0)
                {
                    FirstImprecise = new ProbeFinding(
                        PdfValidationLocation.OfObject(id, offset),
                        Invariant($"the entry of object {number} gives offset {offset}, {CountText.Bytes(placement.Position - offset)} before its header"));
                }

                CheckGeneration(number, entry, placement.Position, placement.Generation);
                return;

            case HeaderPlace.AtOffset or HeaderPlace.Nearby when !placement.GuardAtOffset:
                // The generation is the one the reader records for the object it relocates, read as a load reads it: a
                // header longer than a first read is judged all the same (#118). A header the reader does not vouch for is a
                // choice the file does not designate (ADR 45).
                var findings = placement.Confirmation == HeaderConfirmation.Vouched ? Shifted : Unconfirmed;

                if (findings.Take())
                {
                    findings.Keep(new ProbeFinding(at, Relocation(reader, number, entry, offset, inFile, placement)));
                }

                CheckGeneration(number, entry, placement.Position, placement.Generation);
                return;

            case HeaderPlace.NotFound when !inFile:
                if (Broken.Take())
                {
                    Broken.Keep(new ProbeFinding(
                        at,
                        Invariant($"The entry of object {number} gives offset {Named(reader, entry, offset, inFile)}, outside the file.")));
                }

                return;

            case HeaderPlace.NotFound:
                // An object a guard left out of the index the reader rebuilt is unknown, not lost: its row is not judged.
                if (reader.GetPresence(number) != ObjectPresence.Unindexed && Broken.Take())
                {
                    Broken.Keep(new ProbeFinding(
                        at,
                        Invariant($"The entry of object {number} gives offset {offset}, {WhatLiesAt(placement)}, and the object is not within {PdfFileReader.NearbySearchRadius} bytes of it.")));
                }

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
                if (NotChecked.Take())
                {
                    NotChecked.Keep(new ProbeFinding(
                        at,
                        Invariant($"Reading the header of object {number} where its entry places it, at offset {Named(reader, entry, offset, inFile)}, or near it, the reader reached PdfReaderLimits.MaxObjectLength: the entry was not checked. Raising the limit lets it be.")));
                }

                return;
        }
    }

    /// <summary>
    /// Says where the reader takes the header of object <paramref name="number"/> its entry does not place exactly, and, where
    /// it does not vouch for it, why: built only for a finding.
    /// </summary>
    private static string Relocation(PdfFileReader reader, int number, XRefEntry entry, long offset, bool inFile, HeaderPlacement placement)
    {
        if (placement.Confirmation == HeaderConfirmation.Glued)
        {
            return Invariant($"The entry of object {number} gives offset {offset}, where the object's header is glued to the digits before it, which the reader cannot tell from an offset that cuts another header's number; no other definition of the object lies within {PdfFileReader.NearbySearchRadius} bytes of it, and the reader takes that header.");
        }

        var given = Invariant($"The entry of object {number} gives offset {Named(reader, entry, offset, inFile)}, {(inFile ? WhatLiesAt(placement) : "outside the file")}");
        var taken = Invariant($"{CountText.Distance(placement.Position - offset, "it")}, at offset {placement.Position}");

        return placement.Confirmation switch
        {
            HeaderConfirmation.Vouched => Invariant($"{given}; the object starts {taken}."),
            HeaderConfirmation.Ambiguous => Invariant($"{given}; the object is defined more than once within {PdfFileReader.NearbySearchRadius} bytes of it, and the reader takes the nearest of the definitions its scan for objects takes between objects, {taken}, which the entries next to it do not confirm."),
            HeaderConfirmation.Raw => Invariant($"{given}; the reader takes the header {taken}, which it cannot tell lies between objects: it may lie in the data of a stream whose length its scan for objects does not confirm, which it does for a direct /Length an endstream follows and for an indirect one whose object the file's bytes give where an endstream follows the length it holds; in a comment or a string past where a load ends such data; in an attached file's data; or in a string that runs past its object."),
            HeaderConfirmation.PastScan when !reader.GetScanMemo().IsPastBound(placement.Position) => Invariant($"{given}; the reader takes the header {taken}, which it cannot tell lies between objects: it lies past the last object its scan for objects passed before the end of a read that a source answered with fewer bytes than asked, or, where that read ended while the scan awaited the object of a stream's indirect /Length, past that stream's data's start."),
            _ => Invariant($"{given}; the reader takes the header {taken}, which it cannot tell lies between objects: it lies past the places its scan for objects records within PdfReaderLimits.MaxIndexEntries, or, where the scan reached that bound while it awaited the object of a stream's indirect /Length, past that stream's data's start. Raising the limit lets it tell."),
        };
    }

    /// <summary>Says what lies at the offset an entry gives, in the file, as a finding words it: built only for a finding.</summary>
    private static string WhatLiesAt(HeaderPlacement placement) => placement.NumberAtOffset switch
    {
        > 0 => Invariant($"where there is the header of object {placement.NumberAtOffset}"),
        PdfFileReader.HeaderCutsNumber => "where it cuts a number",
        _ => "where there is no object header",
    };

    /// <summary>Writes the offset an entry gives, as a finding names it: built only for a finding.</summary>
    private static string Named(PdfFileReader reader, XRefEntry entry, long offset, bool inFile) =>
        inFile ? offset.ToString(CultureInfo.InvariantCulture) : reader.DescribeOffset(entry.Offset);

    private void CheckGeneration(int number, XRefEntry entry, long position, long generation)
    {
        if (generation >= 0 && generation != entry.Generation && GenerationMismatches.Take())
        {
            GenerationMismatches.Keep(new ProbeFinding(
                PdfValidationLocation.OfObject(new PdfObjectId(number, entry.Generation), position),
                Invariant($"The entry of object {number} gives generation {entry.Generation}, and the object is written as {number} {generation} obj.")));
        }
    }

    /// <summary>
    /// Determines whether the index the reader rebuilt serves object <paramref name="number"/> as the chain's index does: from
    /// object stream <paramref name="stream"/>, of the definition whose header starts at <paramref name="headerAt"/>, at
    /// <paramref name="index"/>, where the chain's index places it, or at <paramref name="actual"/>, where a load finds it by
    /// its number.
    /// </summary>
    /// <remarks>
    /// The definition is told by where a load found its header, not by the row's offset: a row a few bytes early, on the white
    /// space before the header, places the definition the rebuilt index serves all the same.
    /// </remarks>
    private static bool ServedWhereChainPlaces(PdfFileReader reader, int number, int stream, int index, int actual, long headerAt) =>
        reader.Index.TryGet(number, out var served) &&
        served.Kind == XRefEntryKind.Compressed &&
        served.ObjectStreamNumber == stream &&
        (served.IndexInObjectStream == index || served.IndexInObjectStream == actual) &&
        reader.Index.TryGet(stream, out var definition) &&
        definition.Kind == XRefEntryKind.Regular &&
        reader.PositionOf(definition) == headerAt;

    /// <summary>
    /// Judges the objects the chain's index places soundly in an object stream that the index the reader rebuilt does not
    /// serve as it does (#276, #349): lost where the definition it serves one from — or, lacking it, the definition of its
    /// stream it serves — does not list it, lists it written as a stream, which the reader reads as null, cannot be read or is
    /// no object stream; or where what it serves under that stream's number is no object stream of its own. The header of each
    /// definition it serves them from is read once, however many it serves from there. An object a guard left out of the
    /// rebuilt index is unknown, not lost, and a definition a guard cut is the guard's to say, which the reader reported.
    /// </summary>
    private void CheckMembersARebuildServesElsewhere(PdfFileReader reader, List<ServedElsewhere>? servedElsewhere)
    {
        if (servedElsewhere is null)
        {
            return;
        }

        SortedDictionary<int, List<(ServedElsewhere Member, int Served)>>? byServed = null;

        foreach (var member in servedElsewhere)
        {
            int servedStream, servedIndex;

            if (!reader.Index.TryGet(member.Number, out var served))
            {
                if (reader.GetPresence(member.Number) == ObjectPresence.Unindexed)
                {
                    continue;
                }

                // The rebuild takes in what each object stream it serves lists: lacking the object, what it serves under its
                // stream's number says why.
                (servedStream, servedIndex) = (member.Stream, -1);
            }
            else if (served.Kind == XRefEntryKind.Compressed)
            {
                (servedStream, servedIndex) = (served.ObjectStreamNumber, served.IndexInObjectStream);
            }
            else
            {
                // Placed outside any object stream, the object is not one the rebuild lost from one.
                continue;
            }

            byServed ??= [];

            if (!byServed.TryGetValue(servedStream, out var members))
            {
                members = [];
                byServed.Add(servedStream, members);
            }

            members.Add((member, servedIndex));
        }

        if (byServed is null)
        {
            return;
        }

        foreach (var (servedStream, members) in byServed)
        {
            var definition = new ServedDefinition(reader, servedStream);

            if (definition.Result == ObjectStreamHeaderResult.CutByLimit)
            {
                continue;
            }

            foreach (var (member, served) in members)
            {
                var stream = false;
                var why = served < 0 ? definition.WhyLacking(member.Number) : definition.WhyLost(member.Number, served, out stream);

                if (why is null || !Broken.Take())
                {
                    continue;
                }

                var placed = member.Actual == member.Index
                    ? Invariant($"The entry of object {member.Number} places it at index {member.Index} of object stream {member.Stream}")
                    : Invariant($"The entry of object {member.Number} places it at index {member.Index} of object stream {member.Stream}, whose header lists it at index {member.Actual}");
                Broken.Keep(new ProbeFinding(
                    PdfValidationLocation.OfObject(new PdfObjectId(member.Number)),
                    served < 0
                        ? Invariant($"{placed}, and the index the reader rebuilt lacks it{why}.")
                        : Invariant($"{placed}; the index the reader rebuilt places it at index {served} of object stream {servedStream}{why}."),
                    stream ? StreamMemberRemedy : null));
            }
        }
    }

    private void CheckObjectStreams(
        PdfDocument document,
        PdfFileReader reader,
        SortedDictionary<int, List<(int Number, int Index)>> packed,
        ref List<ServedElsewhere>? servedElsewhere)
    {
        if (document.IsEncrypted || reader.EncryptFoundAfterOpening)
        {
            // An encrypted document's object streams are encrypted with it: until the library decrypts (M16),
            // what they hold cannot be read, and saying anything about it would be a guess. So are those of a document a
            // rebuild after opening found an /Encrypt for, read as it opened (#154).
            var objects = 0;
            foreach (var (_, list) in packed)
            {
                objects += list.Count;
            }

            // The verb follows the count of objects, and the stream and its pronoun the count of object streams that hold
            // them: a single stream can hold them all.
            if (NotChecked.Take())
            {
                var why = document.IsEncrypted ? "The document is encrypted" : "The trailer the rebuilt index merged gives /Encrypt";
                NotChecked.Keep(new ProbeFinding(
                    default,
                    objects == 1
                        ? why + ": the 1 object its index places in an object stream was not checked, that stream being readable only once decrypted."
                        : packed.Count == 1
                            ? Invariant($"{why}: the {objects} objects its index places in an object stream were not checked, that stream being readable only once decrypted.")
                            : Invariant($"{why}: the {objects} objects its index places in object streams were not checked, those streams being readable only once decrypted.")));
            }

            return;
        }

        foreach (var (stream, objects) in packed)
        {
            CheckObjectStream(reader, stream, objects, ref servedElsewhere);
        }
    }

    private void CheckObjectStream(
        PdfFileReader reader, int stream, List<(int Number, int Index)> objects, ref List<ServedElsewhere>? servedElsewhere)
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

            BrokenObjectStreams.Add(new BrokenObjectStream(stream, BrokenObjectStreamKind.NotHeld, objects.Count));
            return;
        }

        if (entry.Kind == XRefEntryKind.Compressed)
        {
            BrokenObjectStreams.Add(
                new BrokenObjectStream(stream, BrokenObjectStreamKind.InAnotherStream, objects.Count, Detail: entry.ObjectStreamNumber));
            return;
        }

        var offset = reader.PositionOf(entry);

        // A row outside the file, its stream found near it, places it nowhere in the file: the finding is located at the
        // object alone, as the entry's own is (#125).
        var position = reader.IsInFile(offset) ? offset : -1;
        var at = position >= 0 ? PdfValidationLocation.OfObject(id, position) : PdfValidationLocation.OfObject(id);

        var result = reader.ReadObjectStreamHeader(stream, entry, out var numbers, out var fault, out var streamMembers, out var headerAt);
        long[]? places = null;

        switch (result)
        {
            case ObjectStreamHeaderResult.NotFound:
                // The stream's own entry is broken, which its probe reports; what it holds cannot be told.
                return;

            case ObjectStreamHeaderResult.CutByLimit:
                if (NotChecked.Take())
                {
                    NotChecked.Keep(new ProbeFinding(
                        at,
                        Invariant($"One of the reader's limits stopped it reading object stream {stream} before its header ended: the {CountText.Objects(objects.Count)} the index places in it {(objects.Count == 1 ? "was" : "were")} not checked. Raising the limit the reader reported lets {(objects.Count == 1 ? "it" : "them")} be.")));
                }

                return;

            case ObjectStreamHeaderResult.NotAnObjectStream:
                BrokenObjectStreams.Add(
                    new BrokenObjectStream(stream, BrokenObjectStreamKind.NotAnObjectStream, objects.Count, position, Fault: fault));
                return;

            case ObjectStreamHeaderResult.Unreadable when numbers.Length == 0:
                BrokenObjectStreams.Add(
                    new BrokenObjectStream(stream, BrokenObjectStreamKind.Unreadable, objects.Count, position, Fault: fault));
                return;

            case ObjectStreamHeaderResult.Unreadable:
                // The reader serves the objects the header lists before its fault, and those alone (#275): when the index
                // places none past them, nothing is lost, and the reader's own report says what the header lacks.
                var lost = 0;

                foreach (var (number, index) in objects)
                {
                    lost += Listed(numbers, number, index) || FirstIndexOf(numbers, number, ref places) >= 0 ? 0 : 1;
                }

                if (lost > 0)
                {
                    BrokenObjectStreams.Add(
                        new BrokenObjectStream(stream, BrokenObjectStreamKind.ReadInPart, objects.Count, position, lost, fault));
                }

                break;
        }

        foreach (var (number, index) in objects)
        {
            var listed = Listed(numbers, number, index);
            var actual = listed ? index : FirstIndexOf(numbers, number, ref places);

            // An object written as a stream is lost: the reader reads it as null (#161, #202). The places of such members
            // are listed in increasing order.
            if (actual >= 0 && Array.BinarySearch(streamMembers, actual) >= 0)
            {
                if (!Broken.Take())
                {
                    continue;
                }

                Broken.Keep(new ProbeFinding(
                    PdfValidationLocation.OfObject(new PdfObjectId(number)),
                    listed
                        ? Invariant($"The entry of object {number} places it at index {index} of object stream {stream}, where it is written as a stream, which an object stream cannot hold.")
                        : Invariant($"The entry of object {number} places it at index {index} of object stream {stream}, whose header lists it at index {actual}, where it is written as a stream, which an object stream cannot hold."),
                    StreamMemberRemedy));
                continue;
            }

            // Sound where the chain's index places it, or where a load finds it by its number, the object may still be lost to
            // a rebuild that serves it from elsewhere, or serves another definition of its stream (#276): a shift the entry
            // makes is its warning, the loss its error.
            if (actual >= 0 && reader.WasRepaired && !ServedWhereChainPlaces(reader, number, stream, index, actual, headerAt))
            {
                (servedElsewhere ??= []).Add(new ServedElsewhere(number, stream, index, actual));
            }

            if (listed)
            {
                continue;
            }

            // An object a header broken before it does not list is the stream's finding's, not its entry's.
            if (actual < 0 && result == ObjectStreamHeaderResult.Unreadable)
            {
                continue;
            }

            if (actual >= 0)
            {
                if (Shifted.Take())
                {
                    Shifted.Keep(new ProbeFinding(
                        PdfValidationLocation.OfObject(new PdfObjectId(number)),
                        Invariant($"The entry of object {number} places it at index {index} of object stream {stream}, whose header lists it at index {actual}.")));
                }
            }
            else if (Broken.Take())
            {
                Broken.Keep(new ProbeFinding(
                    PdfValidationLocation.OfObject(new PdfObjectId(number)),
                    Invariant($"The entry of object {number} places it at index {index} of object stream {stream}, whose header does not list it.")));
            }
        }
    }

    /// <summary>
    /// Judges the members written as a stream that the index the reader serves places, where the chain's index places
    /// nothing under their number: a rebuild found them, expanding an object stream, and the reader reads each as null
    /// (#161, #202), which no entry of the chain's says. Only a rebuild places a member the chain's index lacks. Where a
    /// guard stopped the chain short, the entry that places the member may lie past where it stopped: the finding says the
    /// part of the file's index the reader read does not place it.
    /// </summary>
    private void CheckMembersOnlyARebuildPlaces(PdfDocument document, PdfFileReader reader)
    {
        if (!reader.WasRepaired || document.IsEncrypted || reader.EncryptFoundAfterOpening)
        {
            return;
        }

        SortedDictionary<int, List<(int Number, int Index)>>? packed = null;

        foreach (var (number, entry) in reader.Index)
        {
            if (entry.Kind == XRefEntryKind.Compressed &&
                (reader.ChainIndex?.TryGet(number, out var placed) != true || placed.Kind == XRefEntryKind.Free))
            {
                packed ??= [];

                if (!packed.TryGetValue(entry.ObjectStreamNumber, out var objects))
                {
                    objects = [];
                    packed.Add(entry.ObjectStreamNumber, objects);
                }

                objects.Add((number, entry.IndexInObjectStream));
            }
        }

        if (packed is null)
        {
            return;
        }

        foreach (var (stream, objects) in packed)
        {
            if (!reader.Index.TryGet(stream, out var entry) || entry.Kind != XRefEntryKind.Regular ||
                reader.ReadObjectStreamHeader(stream, entry, out var numbers, out _, out var streamMembers, out _) is
                    ObjectStreamHeaderResult.NotFound or ObjectStreamHeaderResult.CutByLimit or ObjectStreamHeaderResult.NotAnObjectStream)
            {
                continue;
            }

            objects.Sort();

            foreach (var (number, index) in objects)
            {
                if (Listed(numbers, number, index) && Array.BinarySearch(streamMembers, index) >= 0 && Broken.Take())
                {
                    // A number the chain's index lacks, a guard having stopped it short, may be placed past where it stopped.
                    var unread = reader.ChainStoppedByGuard && reader.ChainIndex?.TryGet(number, out _) != true;

                    Broken.Keep(new ProbeFinding(
                        PdfValidationLocation.OfObject(new PdfObjectId(number)),
                        unread
                            ? Invariant($"The index the reader rebuilt places object {number} at index {index} of object stream {stream}, where it is written as a stream, which an object stream cannot hold; the part of the file's own index the reader read does not place it.")
                            : Invariant($"The index the reader rebuilt places object {number} at index {index} of object stream {stream}, where it is written as a stream, which an object stream cannot hold; the file's own index does not place it."),
                        StreamMemberRemedy));
                }
            }
        }
    }

    /// <summary>Determines whether the header that lists <paramref name="numbers"/> lists <paramref name="number"/> at <paramref name="index"/>.</summary>
    private static bool Listed(int[] numbers, int number, int index) =>
        index >= 0 && index < numbers.Length && numbers[index] == number;

    /// <summary>
    /// Gives the first place the header that lists <paramref name="numbers"/> lists <paramref name="number"/> at, or -1,
    /// through a lookup built the first time one is needed.
    /// </summary>
    private static int FirstIndexOf(int[] numbers, int number, ref long[]? places)
    {
        places ??= ObjectStreamPlaces.Of(numbers);
        return ObjectStreamPlaces.FirstIndexOf(places, number);
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// An object the chain's index places soundly in an object stream — at <paramref name="Index"/>, or at
    /// <paramref name="Actual"/> where a load finds it by its number — that the index the reader rebuilt does not serve from
    /// that definition, there.
    /// </summary>
    private readonly record struct ServedElsewhere(int Number, int Stream, int Index, int Actual);

    /// <summary>
    /// What the index the reader rebuilt serves under an object stream's number, read once for every object it serves from
    /// there, or lacks while it serves that stream.
    /// </summary>
    private sealed class ServedDefinition
    {
        private readonly XRefEntry _entry;
        private readonly int[] _numbers = [];
        private readonly string? _fault;
        private readonly int[] _streamMembers = [];

        /// <summary>The lookup of the header by object number, built the first time one is needed.</summary>
        private long[]? _places;

        /// <summary>Reads what the index the reader rebuilt serves under object stream <paramref name="stream"/>.</summary>
        public ServedDefinition(PdfFileReader reader, int stream)
        {
            Stream = stream;

            if (!reader.Index.TryGet(stream, out _entry) || _entry.Kind != XRefEntryKind.Regular)
            {
                return;
            }

            Result = reader.ReadObjectStreamHeader(stream, _entry, out _numbers, out _fault, out _streamMembers, out _);
            At = reader.PositionOf(_entry);
        }

        /// <summary>Gets the object stream's number.</summary>
        public int Stream { get; }

        /// <summary>Gets where the rebuilt index places the stream in the file, for a regular entry; -1 otherwise.</summary>
        public long At { get; } = -1;

        /// <summary>Gets what reading its header found, for a regular entry.</summary>
        public ObjectStreamHeaderResult Result { get; } = ObjectStreamHeaderResult.NotFound;

        /// <summary>
        /// Says why the rebuilt index lacks object <paramref name="number"/>, which the rebuild takes in from every object
        /// stream it serves that lists it, as a finding words it after "lacks it"; nothing where the definition it serves
        /// lists it.
        /// </summary>
        public string WhyLacking(int number)
        {
            if (At < 0)
            {
                return _entry.Kind == XRefEntryKind.Compressed
                    ? Invariant($": it places object {Stream} itself at index {_entry.IndexInObjectStream} of object stream {_entry.ObjectStreamNumber}")
                    : Invariant($": it holds no object {Stream}");
            }

            return Result switch
            {
                ObjectStreamHeaderResult.NotFound => Invariant($": it places object stream {Stream} at offset {At}, where no object {Stream} lies"),
                ObjectStreamHeaderResult.NotAnObjectStream => Invariant($": the object {Stream} it serves, at offset {At}, {_fault}"),
                _ when FirstIndexOf(_numbers, number, ref _places) >= 0 => string.Empty,
                ObjectStreamHeaderResult.Unreadable => Invariant($": the definition of object stream {Stream} it serves, at offset {At}, {Unread()}"),
                _ => Invariant($": the definition of object stream {Stream} it serves, at offset {At}, does not list it"),
            };
        }

        /// <summary>
        /// Says why object <paramref name="number"/>, which the rebuilt index places at index <paramref name="served"/> of
        /// this stream, is lost, as a finding words it after where it places it; null where the reader serves it from there,
        /// at that index or at the one it finds it at by its number.
        /// </summary>
        /// <param name="number">The object's number.</param>
        /// <param name="served">The index the rebuilt index places it at.</param>
        /// <param name="stream">Whether it is lost for being written as a stream there.</param>
        public string? WhyLost(int number, int served, out bool stream)
        {
            stream = false;

            if (At < 0)
            {
                return _entry.Kind == XRefEntryKind.Compressed
                    ? Invariant($", which it places in object stream {_entry.ObjectStreamNumber} in turn")
                    : ", which it does not hold as an object";
            }

            switch (Result)
            {
                case ObjectStreamHeaderResult.NotFound:
                    return Invariant($", at offset {At}, where no object {Stream} lies");
                case ObjectStreamHeaderResult.NotAnObjectStream:
                    return Invariant($", at offset {At}, which {_fault}");
            }

            // A load takes the object at the index the entry gives where the header lists it there, else at the first place
            // the header lists it at.
            var place = Listed(_numbers, number, served) ? served : FirstIndexOf(_numbers, number, ref _places);

            if (place >= 0)
            {
                stream = Array.BinarySearch(_streamMembers, place) >= 0;
                return !stream ? null
                    : place == served ? Invariant($", at offset {At}, where it is written as a stream, which an object stream cannot hold")
                    : Invariant($", at offset {At}, whose header lists it at index {place}, where it is written as a stream, which an object stream cannot hold");
            }

            return Result == ObjectStreamHeaderResult.Unreadable
                ? Invariant($", at offset {At}, which {Unread()}")
                : Invariant($", at offset {At}, whose header does not list it");
        }

        /// <summary>Says that the header cannot be read, whole or at all, the reader's fault quoted.</summary>
        private string Unread() =>
            _numbers.Length == 0 ? Invariant($"cannot be read: it {_fault}") : Invariant($"can be read only in part: it {_fault}");
    }
}
