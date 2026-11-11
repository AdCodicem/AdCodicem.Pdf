using System.Globalization;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO.XRef;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO;

internal sealed partial class PdfFileReader
{
    /// <summary>
    /// Reads the cross-reference stream at <paramref name="absolute"/>, if one is there, and records what became of
    /// it in <paramref name="section"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An object that declares itself a cross-reference stream — <c>/Type /XRef</c>, or a <c>/W</c>, beside no other
    /// <c>/Type</c> at a place the section may have been moved to — and cannot be read is a malformed section; any
    /// other object there, or none, is no section at all, and a guard that cut the dictionary of what declares another
    /// type cut no section, though one that cut a value of a stream with a <c>/W</c> where the chain names the section
    /// leaves it the section, as read whole — a place it may have been moved to takes no stream of another type. Where the
    /// chain names the section, a guard that cut the dictionary before it declared anything — before its <c>/Type</c>, or
    /// before the <c>/W</c> of one whose <c>/Type</c> is no name, which declares nothing — cut what may be the section,
    /// which is the guard's (ADR 34). What makes it unreadable whatever a value a guard cut holds is the file's fault, as
    /// read whole (<see cref="FaultBesideCut"/>). At a place the section may have been moved to
    /// (<paramref name="candidate"/>), nothing such an object's reading met is reported or recorded — its search for an
    /// <c>endstream</c> aside, which is kept as every search is —: it was read as a section, through a section's window,
    /// and is read as itself when something asks for it.
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
    /// bounds the dictionary alone, read from the object's header, and, apart, the white space before it, which the offset
    /// falls short by (<see cref="PaddingAt"/>).
    /// </para>
    /// <para>
    /// A check (<paramref name="checkOnly"/>) tells only whether the stream reads, as the chain naming it would read it: the
    /// object is read as <see cref="TryParseUnrecordedAt"/> reads it, and the same faults are looked for — what the object
    /// declares, a guard's cut of its dictionary, its stream data, a <c>/Length</c> the chain cannot read whose data ends
    /// nowhere, a value its rows are read with that no section read so far places or a guard cut, a fault beside that cut,
    /// <c>/W</c>, <c>/Index</c> and <c>/Size</c>, then data its filters decode nothing of —, and the record says what the
    /// reading found, but nothing is indexed, refused, reported or recorded: the data is decoded as the reading first decodes
    /// it, what the decoding reports dropped — but for a guard it reached, which stands, reported once for the stream, where
    /// its data is first read, and by no later reading —, and no row is read. Reading the rows makes no section unreadable —
    /// rows the data lacks, or that a guard on decoding cut, leave it read in part —, and the check gives
    /// <see cref="XRefSectionState.Read"/> there. The data of a stream of <c>/Type /XRef</c> is not decoded, the check
    /// giving <see cref="XRefSectionState.Read"/> once its numbering is read, and judging beside a value a guard cut only
    /// what the values read give: such a stream keeps its place whatever its data holds (<see cref="KeepsItsPlace"/>). What
    /// a value it read through a guard's cut leads to is the caller's to forget (<see cref="ForgetReadThrough"/>).
    /// </para>
    /// <para>
    /// Data the filters decode nothing of — left at a filter the library does not know, at an element of <c>/Filter</c>
    /// that names none or at a <c>/Filter</c> that is neither a name nor an array, or by Flate data corrupt before its first
    /// byte decodes, the one fault of a filter that leaves the data as it was given — holds none of the rows, whatever its
    /// bytes would read as, and the section cannot be read: its fault is the file's, the decoding's report standing beside
    /// it, and a <c>/Prev</c> its dictionary gives may name the older sections all the same (#208, #221). At a place the
    /// section may have been moved to, where the object gives only a <c>/W</c> and so declares no section, the place is
    /// dropped, and what the decoding reported goes with it (<see cref="XRefSectionRecord.UndecodedReports"/>), what reading
    /// the dictionary reported standing. Data any other filter decodes holds the rows it does — what a filter decoded of its
    /// data before a fault, or the nothing LZW whose first code is undefined decodes to —, the section read in part where it
    /// lacks some (#210); data an image filter leaves is read as the rows it holds.
    /// </para>
    /// </remarks>
    /// <param name="absolute">Where the stream is looked for, in the file.</param>
    /// <param name="section">
    /// Receives what became of the section; gives the white space before the first token at <paramref name="absolute"/>,
    /// past which the stream's object is read (<see cref="PaddingAt"/>).
    /// </param>
    /// <param name="previous">The offset its <c>/Prev</c> gives, or -1.</param>
    /// <param name="candidate">Whether the position is a place the section may have been moved to.</param>
    /// <param name="checkOnly">Whether the stream is only checked, nothing of it kept (#156).</param>
    /// <param name="tablePrevious">
    /// The offset a hybrid table's <c>/Prev</c> names, as written, for a place the search near it tries where that table's
    /// <c>/XRefStm</c> read a cross-reference stream, or names again one a newer table's read, and the place starts no
    /// sooner than that stream; -1 otherwise (<see cref="TryReadXRefSection"/>).
    /// </param>
    private XRefSectionState TryReadXRefStream(
        long absolute, XRefSectionRecord section, out long previous, bool candidate, bool checkOnly = false, long tablePrevious = -1)
    {
        previous = -1;

        // The first window, of 64 KB, holds the data of all but the largest cross-reference streams. The dictionary is the
        // trailer, and grows its window no further than a trailer may: the data never counts toward it, and a /Length the
        // chain cannot read has the data searched for its endstream past the window (#174, #220). The windows open where the
        // first token is, past the white space the offset falls short by, which a trailer's window bounds apart (PaddingAt):
        // what a check of the place reads, from that token, is what the chain reads there.
        var at = absolute + section.Padding;
        var guards = _guardsReached;
        PdfObject value;
        int number;
        long header;
        long digits;
        bool syntaxFault;
        bool objectRead;
        bool cutByGuard;

        // A place the section may have been moved to is a guess: what is there and does not declare itself a cross-reference
        // stream — an object of another /Type, one that gives neither /Type /XRef nor a /W, or no dictionary — leaves nothing
        // of its reading, not even a guard its dictionary reached. One that does is kept as read: where the search drops it
        // later — an object that gives only a /W and cannot be read, among others —, what it read through goes with it, and
        // so does what decoding its data reported, but for a guard the decoding reached, while what reading its dictionary
        // reported stands, once, a guard that cut the dictionary among it (TryRelocateXRefSection). Where the chain names a
        // section, the guard is reported; where it is only checked, the guard's cut is known and nothing is reported.
        if (checkOnly)
        {
            objectRead = TryParseUnrecordedAt(at, out value, out number, out header, out digits, out cutByGuard);
            syntaxFault = false;
        }
        else
        {
            // Near the offset a hybrid table's /Prev names, a cross-reference stream that may be that table's own, its
            // dictionary read whole and told by its own /Prev, is not kept either: the reading stops at the object, and the
            // search passes over it, to try it again once every other place was (TryRelocateXRefSection).
            PdfDictionary? passedOver = null;
            objectRead = tablePrevious >= 0
                ? TryParseNearHybridPrev(at, tablePrevious, out value, out number, out header, out digits, out syntaxFault, out passedOver)
                : TryParseNumberedAt(
                    at,
                    AnyObject,
                    PdfLimit.Trailer,
                    out value,
                    out number,
                    out header,
                    out digits,
                    out syntaxFault,
                    XRefWindow,
                    candidate ? DeclaresOnlyCrossReferenceStream : null);

            if (passedOver is not null)
            {
                // Whether it declares itself a section is kept with it: the search reports it, where it does not come back
                // to it, only if it does (TryRelocateXRefSection).
                section.MayBeTablesOwn = true;
                section.TypedXRef = passedOver.IsOfType(PdfName.XRef);
                return XRefSectionState.NotFound;
            }

            cutByGuard = _guardsReached != guards;

            // A guard that cut the reading at an offset the chain names hid what lay past the cut, whatever it cut: a
            // dictionary, which the parser keeps as far as it read it (#279); the object's header — its number, its
            // generation or its obj keyword running on past the guard, or white space or a comment between its tokens —,
            // which leaves no object read (#289); or a token where no header starts, such as dashes read from inside a
            // comment. The guard is MaxTrailerLength, the only one this reading counts: its windows are a trailer's, a
            // value it reads through a reference is read without loading, no guard counted (ReadWithoutLoading), and the
            // stream's data is neither read nor decoded yet. Where a /Prev or startxref names the offset, any /W makes a
            // stream of any /Type the section there (#263): the cut may have hidden a /Prev the chain would follow — /XRef
            // parted, inside the name or inside a #xx escape, a stream of another /Type that gives a /W, a /Type given
            // twice whose later /XRef lies past the cut, or the whole dictionary past a header the cut parted —, which the
            // reading, finding an object of another type there or none, does not follow, wherever it lies. Where an
            // /XRefStm names the offset, the chain never follows that stream's /Prev, but the guard's cut is what would end
            // the chain there (#264), as #279's closing reads it. Either way no stream passed over is reported anywhere in
            // the document (ReportStreamsPassedOver), whatever the cut left: the reader cannot tell what it hid without
            // reading past the guard (ADR 34), and a cut that hid nothing — a dictionary of another type that gives no /W,
            // a token no header starts — withholds the reports too. What is there is read as written all the same — an
            // object of another type, or none, the section then looked for as one not there —, until #279 and #289 settle
            // it; where the reading takes it for the guard's section, the chain's own cut withholds them already. A place
            // the search tries is a guess, and what the guard cut there, a dictionary or a header, hides no link the chain
            // names.
            if (!candidate && cutByGuard)
            {
                _guardMayHideLink = true;
            }
        }

        if (!objectRead || !(candidate ? DeclaresOnlyCrossReferenceStream(value) : DeclaresCrossReferenceStream(value)))
        {
            if (!candidate && cutByGuard && value is PdfDictionary { } cut && cut.GetRaw(PdfName.Type) is not PdfName)
            {
                // A dictionary MaxTrailerLength cut before it said what it is may be the section's: unknown, and the
                // guard's, which says so (ADR 34), rather than a section missing. One whose /Type is no name has not said
                // it either: such a /Type declares nothing, as everywhere the reader asks what a dictionary declares, and
                // the dictionary declares a section only through a /W, which the cut may have hidden
                // (DeclaresOnlyCrossReferenceStream). An array, a dictionary whose /Type names another type, or a stream
                // whose dictionary was read whole is no cross-reference stream, cut or not — a /Type the cut parted among
                // them, its name read as far as it was read until #279 settles it —, and a header the cut parted leaves
                // no object to tell, the section looked for as one not there (#289): where a guard cut any of them, the
                // cut withholds only the report of streams passed over, as set above.
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

        // The header lies where the object read starts, past however much white space the offset falls short by, and the
        // digits of its number past the run of signs it is written with, if any, as the reading lexed them: a search near an
        // offset tells the header it finds there as this stream, however few of those signs its window holds
        // (NumberRunsInto).
        section.Padding = (int)(header - absolute);
        section.DigitsAt = digits;
        section.TrailerPosition = absolute;
        section.TypedXRef = dictionary.IsOfType(PdfName.XRef);
        section.TypedOther = !section.TypedXRef && dictionary.GetRaw(PdfName.Type) is PdfName;

        // A guard that cut the dictionary of what declares another type cut no section: such a dictionary — a CIDFont's,
        // whose /W gives widths — has no stream data read whole, and is no section. A stream of that type, which the cut
        // hides, would be read as the section whole: the cut takes it for none (#263).
        section.CutByLimit |= cutByGuard && DeclaresOnlyCrossReferenceStream(value);

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
                // What makes the section unreadable whatever the length the guard hid holds is the file's, as read whole: a
                // value no section read so far places, or what the values read give.
                var file = UnreadValue(dictionary, out var unreadCut) is { } unplaced && unreadCut is null
                    ? unplaced
                    : FaultBesideCut(dictionary, null, out _);
                section.Fault = file ?? lengthFault;
                return file is null ? CutByValue(section, past) : XRefSectionState.Malformed;
            }

            var where = search.NextObject is { } next
                ? string.Create(CultureInfo.InvariantCulture, $"before the next object, at {next}")
                : "before the end of the file";
            section.Fault = string.Create(
                CultureInfo.InvariantCulture,
                $"{lengthFault}, and {(search.Searched ? "no endstream follows its data " + where : "its data was not searched for its endstream, the document's searches for endstream having read as much of the file as they may")}");
            return XRefSectionState.Malformed;
        }

        if (UnreadValue(dictionary, out var valueCut) is { } unread)
        {
            section.Fault = unread;

            // A value no section read so far places is the file's fault. One a guard cut is the guard's, as rows a guard cut
            // are (CutByValue), but for what makes the section unreadable whatever the value holds: the file's, as read whole.
            if (valueCut is not { } cut)
            {
                return XRefSectionState.Malformed;
            }

            // FaultBesideCut decodes data whose extent the file gives, to tell whether its filters decode anything of it, and
            // that decoding reports into the document's diagnostics, as the reading below does. A check gives it no stream of
            // /Type /XRef, which keeps its place whatever its data holds (KeepsItsPlace), and drops what the decoding of any
            // other reported, as it does below, but for a guard it reached, which stands, as at a place dropped: the cut
            // MaxStreamLength makes of the data is reported once for the stream, where its data is first read
            // (ReachStreamLength), and by no later reading. At a place the section may have been moved to, where the data holds
            // no row, what the decoding reported goes with the place where the search drops it (TryRelocateXRefSection). The
            // mark, taken once for a check and once for each place read that far, which MaxRelocationCandidates bounds, holds
            // the decodings' reports.
            var judged = deferred is null && !xrefStream.Data.CutByGuard && !xrefStream.Data.EndHidden &&
                !(checkOnly && section.TypedXRef)
                ? xrefStream
                : null;
            var besideCut = candidate || checkOnly ? _diagnostics.GetMarkWithDecodingReports() : default;
            var file = FaultBesideCut(dictionary, judged, out var holds);

            if (checkOnly)
            {
                _diagnostics.RollBackBesideLimits(besideCut);
            }

            if (file is not null)
            {
                section.Fault = file;

                if (holds is not { } rows)
                {
                    section.UndecodedReports = candidate && file == DataUndecoded ? besideCut : null;
                    return XRefSectionState.Malformed;
                }

                // Read as below, whole or in part: a check keeps nothing of it.
                if (checkOnly)
                {
                    return XRefSectionState.Read;
                }

                // Data whose extent the file gives that cannot hold the rows the values read declare lacks rows whatever the
                // cut value holds: the section is read as read whole, the file's to lack them — its trailer merged, the index
                // rebuilt —, and the guard is reached once the chain is read. The rows the data may hold, at the fewest bytes
                // a row may take, are read with the cut value: the guard's, which a whole read reads, the chain ending here, as
                // where rows a guard cut, since an older section's rows would stand for them. Data that holds none leaves the
                // chain going on.
                _indexIncomplete = true;
                section.Incomplete = true;
                _xref.MergeTrailer(dictionary);
                (_chainValuesCut ??= []).Add(cut);

                if (rows > 0)
                {
                    // None of those rows is read: the section kept none, which its report says (#210).
                    _cutByGuard = true;
                    section.CutByLimit = true;
                    section.RowsLeftToGuard = true;
                    return XRefSectionState.Read;
                }

                section.RowsHeld = 0;
                string? hiddenPrevious = null;
                previous = section.NamedBy == "/XRefStm" ? -1 : SectionOffset(dictionary, PdfName.Prev, absolute, out hiddenPrevious);
                section.ChainHiddenByGuard = hiddenPrevious;
                return XRefSectionState.Read;
            }

            return CutByValue(section, cut);
        }

        var widths = dictionary.GetArray(PdfName.W);

        if (widths is null || widths.Count < 3)
        {
            section.Fault = WidthsMissing;
            return XRefSectionState.Malformed;
        }

        // A width counts bytes, and ISO 32000-1 bounds none (Table 17): a field wider than the value the reader keeps holds
        // in its leading bytes what no entry can (ReadField). Each is narrowed once the data is decoded, against its length.
        Span<long> declaredWidths = stackalloc long[3];
        var anyWidth = false;

        for (var i = 0; i < 3; i++)
        {
            var width = widths.Resolved(i).AsInteger();

            if (WidthFault(width) is { } widthFault)
            {
                section.Fault = widthFault;
                return XRefSectionState.Malformed;
            }

            declaredWidths[i] = width.GetValueOrDefault();
            anyWidth |= width > 0;
        }

        if (!anyWidth)
        {
            section.Fault = NoRowBytes;
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

        // Past here, only data the filters decode nothing of makes the section unreadable, as the reading finds once it decodes
        // it: what else the data decodes to, and the rows it holds, read it whole or in part. A check decodes the data as the
        // reading first does, to tell, and drops what the decoding reports, a reading of the stream meeting it again, but for
        // a guard it reached, which stands: the cut MaxStreamLength makes of the data is reported once for the stream, where
        // its data is first read (ReachStreamLength), and by no later reading. The check reads no row. A stream of /Type /XRef
        // keeps its place whatever its data holds (KeepsItsPlace): its data is not decoded.
        if (checkOnly)
        {
            if (section.TypedXRef)
            {
                return XRefSectionState.Read;
            }

            var decodingChecked = _diagnostics.GetMarkWithDecodingReports();
            _ = Filters.PdfFilterPipeline.Decode(
                xrefStream, _diagnostics, endHidden: false, reachedBefore: false, out _, out _, out var undecodedData);
            _diagnostics.RollBackBesideLimits(decodingChecked);

            if (undecodedData)
            {
                section.Fault = DataUndecoded;
                return XRefSectionState.Malformed;
            }

            return XRefSectionState.Read;
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

        // What a decoding reports of data taken again is dropped with it, and may be met again. So is what it reports of data
        // the filters decode nothing of at a place the section may have been moved to, where the place declares no section
        // (TryRelocateXRefSection): the mark taken for it, one for each place read that far, which MaxRelocationCandidates
        // bounds, holds the decodings' reports.
        var decoding = deferred is null && !candidate ? _diagnostics.GetMark() : _diagnostics.GetMarkWithDecodingReports();
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

        // Whether the decoding before stopped at MaxDecodedStreamLength, which the data decoded again knowing its end is
        // hidden keeps reached: the whole data holds at least what that decoding read.
        var reachedBefore = false;

        // How many rows the decoding before held where it stopped at MaxDecodedStreamLength, which the data decoded again
        // knowing its end hidden reads no more of.
        long heldBefore = 0;

        while (true)
        {
            var decoded = Filters.PdfFilterPipeline.Decode(
                xrefStream, _diagnostics, endHidden, reachedBefore, out var leftEncoded, out var cut, out var undecoded);

            // Data the filters decoded nothing of — left at a filter the library does not know, at an element of /Filter that
            // names none or at a /Filter that is neither a name nor an array, or by Flate data corrupt before its first byte
            // decodes — is not what the stream decodes to, and holds none of its rows, whatever its bytes would read as: the
            // section cannot be read, the fault the file's, and what the decoding reported stands beside it, but at a place
            // the section may have been moved to that is dropped. Its rows are lost, as a stream's whose /W no row can be read
            // with, and its /Prev may name the older sections all the same (#208, #221). Any other filter gives what it
            // decoded, part of its data or none of it, and the data holds the rows it holds, read in part where it lacks some.
            if (undecoded)
            {
                section.Fault = DataUndecoded;
                section.UndecodedReports = candidate ? decoding : null;
                return XRefSectionState.Malformed;
            }

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

            // The data decoded again knowing its end hidden lacks the group a filter dropped there, and a filter after it,
            // given less, may be given more room than the decoding before gave it, and read rows past where that one stopped
            // at a guard on decoding: the guard stops it where it stops the data read whole, the decoding before (#220). A
            // decoding no guard stopped holds fewer rows only where its end, decoded as the file's, lost a predicted row the
            // data decoded again undoes as far as it goes: those it completes are the file's.
            if (endHidden && reachedBefore && held > heldBefore)
            {
                held = heldBefore;
                data = data[..(int)(held * row)];
            }

            // Fewer rows than the section declares, in data a /Length the chain could not read left to the first endstream:
            // where the section's own row for that /Length, among those held, gives more data, which an endstream confirms,
            // the rows spelled an endstream, and the data is taken again to that length (#220). Data that holds no row whole
            // is asked too: a newer section may place the object, which a guard cut.
            if (!retaken && !endHidden && deferred is { } unsettled && declared > held && decodedWhole)
            {
                long[] ranges = index is null ? [0, declared] : subsections;
                var whole = RetakeDeferred(xrefStream, unsettled, data, fieldWidths, row, held, ranges, out lengthCut, out var numbered);

                // The row for the /Length may lie in a predicted row the spelled endstream parts, which the data decoded as
                // data whose end the file gives dropped, left as decoded — the first predicted row parted — or left still
                // differenced: the data decoded knowing its end hidden undoes it as far as it goes, and holds the rows before
                // the endstream whole. What that decoding reports is dropped with it, but for a guard on decoding it reached,
                // which the whole data, holding at least as much, reaches too: where it found no row for the /Length, where
                // the rows end is the guard's, as where the decoding before reached it.
                if (whole is null && numbered && row > 0)
                {
                    var again = _diagnostics.GetMarkWithDecodingReports();
                    var undone = Filters.PdfFilterPipeline.Decode(
                        xrefStream, _diagnostics, endHidden: true, reachedBefore: false, out var leftAgain, out var reached, out _);

                    if (!leftAgain)
                    {
                        whole = RetakeDeferred(xrefStream, unsettled, undone.Span, fieldWidths, row, undone.Length / row, ranges, out lengthCut, out _);
                    }

                    if (whole is null && reached)
                    {
                        _diagnostics.RollBackBesideLimits(decoding);
                        reachedBefore = true;
                        heldBefore = undone.Length / row;
                        endHidden = true;
                        continue;
                    }

                    _diagnostics.RollBack(again);
                }

                if (whole is not null)
                {
                    _diagnostics.RollBack(decoding);
                    xrefStream = whole;
                    deferred = unsettled with { Taken = whole.Data.Length };
                    retaken = true;
                    continue;
                }
            }

            // Where a guard cut the length, the endstream the data ends at — as the chain took it, or took it again — is the
            // stream's own, and the rows it lacks the file's, only where what follows proves it, as a load of the stream
            // reads it: the rows the data holds place objects too (#224).
            if (lengthCut && !endHidden && EndsAtOwnEndStream(xrefStream, data, fieldWidths, row, held))
            {
                lengthCut = false;
            }

            // Data a /Length the chain could not read left to an endstream nothing confirmed, where the length that would say
            // whether it is the data's own is one a guard cut, or where a guard on decoding kept the data, which lacks rows,
            // from being taken again to the length: where it ends is the guard's, and a filter run out there says nothing of
            // the file. The data is decoded once more knowing it, as data a guard cut, a group its end parts dropped, the
            // faults in what it read standing (#220, #224). A guard on decoding the first decoding reached stands too, with
            // its report: a filter after the one that drops the group, given less, may stay within the bound.
            if (!endHidden && deferred is not null && !xrefStream.Data.CutByGuard &&
                (lengthCut || (!retaken && !decodedWhole && declared > held)))
            {
                reachedBefore = cut;
                heldBefore = held;

                if (reachedBefore)
                {
                    _diagnostics.RollBackBesideLimits(decoding);
                }
                else
                {
                    _diagnostics.RollBack(decoding);
                }

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
                section.RowsHeld = held;
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

        // Data a guard cut that lacks rows — MaxStreamLength as the data was read, or a guard on decoding it — is the guard's,
        // which says so (ADR 34), and is not checked against a length: the cut kept the data from being taken again to it.
        // Data that holds every row the section declares, which nothing would take again, is checked whatever a guard did —
        // the check reads the length and where the search found the endstream, which no cut moves —, but not against a
        // length a guard cut, which is no length the file gave.
        if (deferred is { } check && (decodedWhole || (!lengthCut && declared <= held)))
        {
            (_deferredLengths ??= new(SeededComparer.Instance))[xrefStream.Data.Position] = check;
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
        var named = section.NamedBy == "/XRefStm" ? -1 : SectionOffset(dictionary, PdfName.Prev, absolute, out previousHidden, rowsHidden: rowsCut);
        section.ChainHiddenByGuard = previousHidden;

        // Where a guard cut the rows, or filled the index, the chain ends here, and the /Prev is judged as far as the guard
        // lets it be, never read (#264): a reference that leads to a row the guard left unread names what is unknown, and an
        // offset is the chain's to judge. So it is where the stream was found moved, a stream read there being the section.
        if (rowsCut || _indexFullAt >= 0)
        {
            _previousPastGuard = named;
        }
        else
        {
            previous = named;
        }

        return XRefSectionState.Read;
    }

    /// <summary>
    /// Parses what starts at <paramref name="at"/>, in a place the search near the offset a hybrid table's <c>/Prev</c> names
    /// tries where the table's <c>/XRefStm</c> read a cross-reference stream, or names again one a newer table's read, and
    /// the place starts no sooner than that stream, as <see cref="TryReadXRefStream"/> parses any place: kept only where it
    /// declares a cross-reference stream and no other type, and not where it is a stream, its dictionary read whole, that
    /// may be the table's own (<see cref="MayBeTablesOwn"/>), which <paramref name="passedOver"/> says. A dictionary a guard
    /// cut, which says nothing of a <c>/Prev</c> past the cut, is kept, and the guard reached as
    /// <see cref="TryParseNumberedAt"/> reaches it. What the test refuses leaves nothing of its reading — a search for its
    /// <c>endstream</c> aside, kept as every search is, nothing it met is reported or recorded, but for the objects a
    /// <c>/Prev</c> written as a reference leads to, read as any value the chain reads (<see cref="ReadWithoutLoading"/>),
    /// what it read past a guard forgotten with the place (<see cref="ForgetReadThrough"/>) —, and a stream's data is
    /// neither decoded nor read as rows. A header the window cuts, which reads no object to test, reaches the guard as at
    /// any place the search tries.
    /// </summary>
    /// <param name="at">Where the object is read, past the white space at the place.</param>
    /// <param name="tablePrevious">The offset the table's <c>/Prev</c> names, as written.</param>
    /// <param name="value">The object read, where it is kept.</param>
    /// <param name="number">The number of the object found, kept or not.</param>
    /// <param name="header">Where the object's header starts.</param>
    /// <param name="digits">Where the digits of the object's number start, past any run of signs it is written with.</param>
    /// <param name="syntaxFault">Whether the reading kept met a fault of the syntax.</param>
    /// <param name="passedOver">
    /// The dictionary of what is there, where it is a cross-reference stream, its dictionary read whole, that may be the
    /// table's own; null otherwise.
    /// </param>
    private bool TryParseNearHybridPrev(
        long at,
        long tablePrevious,
        out PdfObject value,
        out int number,
        out long header,
        out long digits,
        out bool syntaxFault,
        out PdfDictionary? passedOver)
    {
        PdfDictionary? mayBeOwn = null;
        var read = TryParseNumberedAt(
            at,
            AnyObject,
            PdfLimit.Trailer,
            out value,
            out number,
            out header,
            out digits,
            out syntaxFault,
            XRefWindow,
            parsed =>
            {
                if (!DeclaresOnlyCrossReferenceStream(parsed))
                {
                    return false;
                }

                mayBeOwn = parsed is PdfStream stream && MayBeTablesOwn(parsed, tablePrevious) ? stream.Dictionary : null;
                return mayBeOwn is null;
            });
        passedOver = mayBeOwn;
        return read;
    }

    /// <summary>
    /// Determines whether the <c>endstream</c> cross-reference stream <paramref name="stream"/>'s data ends at, as the chain
    /// took it or took it again, is the stream's own beyond what a <c>/Length</c> a guard cut could say
    /// (<see cref="IPdfStreamDataProvider.IsOwnEndStream"/>): where the data ends is then the file's (#224). The next
    /// object an index places is the nearer of the one the sections read so far place and the first the
    /// <paramref name="held"/> rows of <paramref name="data"/> place past the start of the data, as the index a load of the
    /// stream reads with holds them.
    /// </summary>
    private bool EndsAtOwnEndStream(PdfStream stream, ReadOnlySpan<byte> data, ReadOnlySpan<int> widths, int row, long held)
    {
        var dataStart = stream.Data.Position;
        var dataEnd = dataStart + stream.Data.Length;
        var provider = (IPdfStreamDataProvider)this;

        if (!_endStreamSearches.TryGetValue((dataStart, true), out var known) ||
            provider.CheckEndStream(dataEnd) is not EndObjState endObj || endObj is not (EndObjState.Absent or EndObjState.Present))
        {
            return false;
        }

        var next = known.Search.NextObject;

        // Each row lies within the data, whose length an int holds.
        for (var at = 0L; at < held; at++)
        {
            var fields = data.Slice((int)at * row, row);
            var cursor = 0;
            var type = widths[0] == 0 ? 1 : ReadField(fields, ref cursor, widths[0]);
            var offset = ReadField(fields, ref cursor, widths[1]);

            if (type == 1 && offset <= long.MaxValue && PositionOf((long)offset) is var placed && placed > dataStart &&
                (next is null || placed < next))
            {
                next = placed;
            }
        }

        return provider.IsOwnEndStream(dataEnd, endObj, next);
    }

    /// <summary>
    /// Gives cross-reference stream <paramref name="stream"/> with its data taken again to the length its <c>/Length</c>
    /// gives, which the chain could not read and took the data up to the first <c>endstream</c> for, when the rows
    /// <paramref name="data"/> holds show that <c>endstream</c> was in the rows (#220): the section's own row for the object
    /// the <c>/Length</c> names, among those held, places it, and the length it gives runs past the data taken, to an
    /// <c>endstream</c> that confirms it. Null otherwise: the length is checked once the chain is read — and a subsection
    /// numbers the object, its row past those held or not standing, where <paramref name="numbered"/> says so.
    /// </summary>
    /// <remarks>
    /// The section's row for the object is the first that numbers it, whatever its type, as the reading of its rows takes
    /// it: one of a reserved type stands for the null object, as ISO 32000-1 reads it, and places no length (#221,
    /// <see cref="ReadXRefStreamRows"/>). The object is read where that row places it as <see cref="ReadWithoutLoading"/>
    /// reads one: nothing is corrected, cached, recorded or reported. One a guard cuts gives what the cut left of it, an
    /// integer read before the cut only a place to look for <c>endstream</c> at (#224), and <paramref name="lengthCut"/>
    /// says so, whether an <c>endstream</c> confirms it or not: rows the data taken again still lacks are the guard's. A
    /// reference it holds, which a cut after it leaves whole, leads on, as does the <c>/Length</c> when a newer section gave
    /// the number: to a length an object read whole holds, the file's, or to the integer the cut left of an object a guard
    /// cut, read where the sections read so far place it, only a place to look at then (<see cref="TakeAgainThrough"/>). A
    /// row that does not stand otherwise — a newer section gave the number, or refused it; the row lies past those held,
    /// places the object in an object stream, under another generation, or nowhere — leaves the data as it was taken.
    /// </remarks>
    private PdfStream? RetakeDeferred(
        PdfStream stream,
        DeferredLength deferred,
        ReadOnlySpan<byte> data,
        ReadOnlySpan<int> widths,
        int row,
        long held,
        long[] subsections,
        out bool lengthCut,
        out bool numbered)
    {
        lengthCut = false;
        numbered = false;
        var number = deferred.Length.Number;

        if (_xref.TryGet(number, out _) || _refusedNumbers?.ContainsKey(number) == true)
        {
            // The object a newer section places, which the chain could not read: a guard that cut it, or the object a chain
            // of references from it leads to, leaves how far the data runs to the integer the cut left, where an endstream
            // confirms it, and unknown otherwise.
            return TakeAgainThrough(stream, deferred, stream.Dictionary.GetRaw(PdfName.Length), LengthLinks, out lengthCut);
        }

        // The first row that numbers the object, in the order the subsections give them, is the one the reading of the rows
        // takes, whatever its type: one of a reserved type stands for the null object (#221). Their counts sum within a long,
        // as the section's do. A number no subsection numbers leaves the type 0, which places nothing.
        long ordinal = 0;
        ulong type = 0;
        ulong offset = 0;
        ulong generation = 0;

        for (var i = 0; i < subsections.Length && !numbered; i += 2)
        {
            if (number >= subsections[i] && number - subsections[i] < subsections[i + 1])
            {
                var at = ordinal + (number - subsections[i]);
                numbered = true;

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
            }

            ordinal += subsections[i + 1];
        }

        if (type != 1 || offset > long.MaxValue || generation != (ulong)deferred.Length.Generation)
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
    /// Takes a section a value's cut keeps from being read for the guard's: the section counts as cut, the chain ends there
    /// and nothing is rebuilt for it (ADR 34), and the object the guard cut is loaded once the chain is read, which reaches
    /// the guard (#263). Where the chain names the section, whatever <c>/Type</c> the stream gives, as a stream with a
    /// <c>/W</c> is read there as the section whole — a place it may have been moved to takes no stream of another type —:
    /// the value hides whether its rows are, but for what <see cref="FaultBesideCut"/> finds wrong whatever it holds.
    /// </summary>
    /// <param name="section">The section, whose fault names the value.</param>
    /// <param name="cut">The object the guard cut that the value leads to.</param>
    private XRefSectionState CutByValue(XRefSectionRecord section, PdfObjectId cut)
    {
        section.CutByLimit = true;
        section.CutByValue = true;
        (_chainValuesCut ??= []).Add(cut);
        return XRefSectionState.Malformed;
    }

    /// <summary>
    /// Says what makes a cross-reference stream unreadable whatever the values a guard cut hold, as a whole read says it:
    /// the widths its <c>/W</c> gives, then how it numbers its rows, as far as the values read say, then, for
    /// <paramref name="stream"/>, whether its data can hold the rows they declare, judged where the <c>/Index</c> or
    /// <c>/Size</c> it numbers them with was read — and whether its filters decode anything of it, data they decode nothing
    /// of holding no row whatever the counts declare, judged whatever <c>/Index</c> or <c>/Size</c> a guard cut (#221). A
    /// value a guard cut, or an element of one, says nothing, nor does what depends on it — the <c>/Size</c> an
    /// <c>/Index</c> so cut may stand beside —; an <c>/Index</c> that holds no pairs is wrong whatever its elements hold.
    /// Null when nothing read is wrong: the section is then the guard's.
    /// </summary>
    /// <remarks>
    /// <see cref="UnreadValue"/> followed every value read here, and found none that no section read so far places: a value
    /// that cannot be read is one a guard cut.
    /// </remarks>
    /// <param name="dictionary">The stream's dictionary.</param>
    /// <param name="stream">The stream, whose data's extent is the file's, or null when it is not.</param>
    /// <param name="held">
    /// The most rows the data holds, where the fault is that it holds fewer than the section declares; null otherwise.
    /// </param>
    private string? FaultBesideCut(PdfDictionary dictionary, PdfStream? stream, out long? held)
    {
        held = null;

        // The fewest bytes a row may hold, which the widths read give, and the fewest rows the counts read declare.
        long row = 0;
        long declared = -1;
        var exact = true;

        if (Unread(dictionary, PdfName.W, out var w, out _) is null)
        {
            if (w is not PdfArray widths || widths.Count < 3)
            {
                return WidthsMissing;
            }

            var known = true;
            var anyWidth = false;

            for (var i = 0; i < 3; i++)
            {
                if (Follow(PdfName.W, widths[i], entry: false, out var element, out _) is not null)
                {
                    known = false;
                    continue;
                }

                var width = element?.AsInteger();

                if (WidthFault(width) is { } fault)
                {
                    return fault;
                }

                anyWidth |= width > 0;

                // A width past what an int counts makes a row longer than any data decodes to: three so narrowed are within
                // a long.
                row += Math.Min(width.GetValueOrDefault(), int.MaxValue + 1L);
            }

            if (known && !anyWidth)
            {
                return NoRowBytes;
            }
        }

        // Data its filters decode nothing of holds no row whatever the counts declare: the section cannot be read, whatever an
        // /Index or a /Size a guard cut holds, as a whole read finds (#221).
        if (Unread(dictionary, PdfName.Index, out var index, out _) is not null)
        {
            return UndecodedFault(stream);
        }

        if (index is null or PdfNull)
        {
            if (Unread(dictionary, PdfName.Size, out var size, out _) is not null)
            {
                return UndecodedFault(stream);
            }

            if (NumberingFault(null, null, size?.AsInteger(), out _) is { } sizeFault)
            {
                return sizeFault;
            }

            declared = size?.AsInteger() ?? 0;
        }
        else
        {
            if (index is not PdfArray ranges || PairsFault(ranges) is not null)
            {
                return NumberingFault(dictionary.GetRaw(PdfName.Index), index, null, out _);
            }

            declared = 0;

            for (var i = 0; i < ranges.Count; i += 2)
            {
                var startRead = Follow(PdfName.Index, ranges[i], entry: false, out var start, out _) is null;
                var countRead = Follow(PdfName.Index, ranges[i + 1], entry: false, out var count, out _) is null;

                if ((startRead && countRead
                        ? SubsectionFault(ranges, i, start?.AsInteger(), count?.AsInteger())
                        : HalfSubsectionFault(ranges, i, startRead, start?.AsInteger(), countRead, count?.AsInteger())) is { } fault)
                {
                    return fault;
                }

                // Each count read is within the object numbers, and an /Index holds no more pairs than a trailer: their sum
                // is within a long. A count a guard cut is one more, of 0 rows or more.
                if (countRead)
                {
                    declared += count!.AsInteger().GetValueOrDefault();
                }
                else
                {
                    exact = false;
                }
            }
        }

        // Data its filters decode nothing of holds no row, whatever the counts declare, though they declare none (#221).
        var undecoded = false;

        if (stream is null || RowsHeldAtMost(stream, Math.Max(row, 1), out undecoded) is not { } most || declared <= most.Rows)
        {
            return undecoded ? DataUndecoded : null;
        }

        held = most.Rows;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"its data, {most.Length:N0} {(most.Length == 1 ? "byte" : "bytes")} decoded, holds at most {most.Rows:N0} of the {declared:N0}{(exact ? string.Empty : " or more")} {(declared == 1 && exact ? "row" : "rows")} its /Index and /Size declare");
    }

    /// <summary>
    /// Gives <see cref="DataUndecoded"/> where the filters of cross-reference stream <paramref name="stream"/>, written
    /// direct, decode nothing of its data, which then holds no row, whatever the values a guard cut hold (#221); null
    /// otherwise, and for no stream. The data is decoded to tell (<see cref="RowsHeldAtMost"/>), and what the decoding
    /// meets is reported into the document's diagnostics, which the caller rolls back where it is to keep nothing of it.
    /// </summary>
    /// <param name="stream">The stream, whose data's extent is the file's, or null when it is not.</param>
    private static string? UndecodedFault(PdfStream? stream)
    {
        if (stream is null)
        {
            return null;
        }

        // How many rows the data holds says nothing here: only whether it holds any.
        _ = RowsHeldAtMost(stream, 1, out var undecoded);
        return undecoded ? DataUndecoded : null;
    }

    /// <summary>
    /// Gives how many rows of <paramref name="row"/> bytes or more the data of cross-reference stream
    /// <paramref name="stream"/> holds at most, and how long it decodes to, when its filters are written direct and decode
    /// it whole; null otherwise: a value of them a reference holds is read as the chain reads one, and its decoding is what
    /// the section's reading does. The decoding is given no diagnostics of its own, and reports what it meets into those of
    /// the stream's document, as any decoding of the stream does: the caller rolls them back where it is to keep nothing of
    /// it (<see cref="TryReadXRefStream"/>).
    /// </summary>
    /// <param name="stream">The stream, whose data's extent is the file's.</param>
    /// <param name="row">The fewest bytes a row may hold.</param>
    /// <param name="undecoded">
    /// Whether its filters, written direct, decode nothing of the data, which then holds no row (#221); null is given then.
    /// </param>
    private static (long Rows, int Length)? RowsHeldAtMost(PdfStream stream, long row, out bool undecoded)
    {
        undecoded = false;

        if (!IsWrittenDirect(stream.Dictionary.GetRaw(PdfName.Filter)) || !IsWrittenDirect(stream.Dictionary.GetRaw(PdfName.DecodeParms)))
        {
            return null;
        }

        var decoded = Filters.PdfFilterPipeline.Decode(
            stream, diagnostics: null, endHidden: false, reachedBefore: false, out var leftEncoded, out var cut, out undecoded);
        return leftEncoded || cut || undecoded ? null : (decoded.Length / row, decoded.Length);
    }

    /// <summary>
    /// Determines whether a stream's <c>/Filter</c> or <c>/DecodeParms</c> is written with no reference: itself, the
    /// elements of an array, and the values of a dictionary, its own or an element's.
    /// </summary>
    private static bool IsWrittenDirect(PdfObject? value)
    {
        if (value is PdfArray elements)
        {
            for (var i = 0; i < elements.Count; i++)
            {
                if (elements[i] is PdfReference || (elements[i] is PdfDictionary parameters && !HoldsNoReference(parameters)))
                {
                    return false;
                }
            }

            return true;
        }

        return value is not PdfReference && (value is not PdfDictionary dictionary || HoldsNoReference(dictionary));
    }

    /// <summary>Determines whether none of the values of <paramref name="dictionary"/> is a reference.</summary>
    private static bool HoldsNoReference(PdfDictionary dictionary)
    {
        foreach (var entry in dictionary)
        {
            if (entry.Value is PdfReference)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Says what is wrong with the subsection an <c>/Index</c> gives at <paramref name="at"/>, one of whose two elements a
    /// guard cut, whatever that element holds: the other one is no integer, or a count below 0 or past what object numbers
    /// hold, or a first number outside them. Null when the element read is right for some value of the other.
    /// </summary>
    /// <param name="ranges">The <c>/Index</c>, whose written elements a fault describes.</param>
    /// <param name="at">Where the subsection's first number lies.</param>
    /// <param name="startRead">Whether the first number was read.</param>
    /// <param name="start">The first number, when it was read and is an integer.</param>
    /// <param name="countRead">Whether the count was read.</param>
    /// <param name="count">The count, when it was read and is an integer.</param>
    private static string? HalfSubsectionFault(PdfArray ranges, int at, bool startRead, long? start, bool countRead, long? count)
    {
        if ((startRead && start is null) || (countRead && count is null))
        {
            return $"its /Index holds {DescribeValue(ranges[startRead && start is null ? at : at + 1])} where an integer belongs";
        }

        if (countRead && count < 0)
        {
            return "its /Index gives a subsection a count of rows out of range";
        }

        if (countRead && count > PdfObjectId.MaxNumber + 1L)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"its /Index gives a subsection of {count:N0} rows, more than object numbers 0 to {PdfObjectId.MaxNumber} hold");
        }

        return startRead && start is < 0 or > PdfObjectId.MaxNumber
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"its /Index gives a subsection from object {start}, outside object numbers 0 to {PdfObjectId.MaxNumber}")
            : null;
    }

    /// <summary>The fault of a <c>/W</c> that is no array of three widths.</summary>
    private const string WidthsMissing = "its /W does not give the widths of three fields";

    /// <summary>The fault of a <c>/W</c> whose widths are all 0.</summary>
    private const string NoRowBytes = "its /W gives rows of no bytes";

    /// <summary>
    /// The fault of data the filters decode nothing of: left at a filter the library does not know, at an element of
    /// <c>/Filter</c> that names none or at a <c>/Filter</c> that is neither a name nor an array, or by Flate data corrupt
    /// before its first byte decodes (#221).
    /// </summary>
    private const string DataUndecoded = "its data could not be decoded";

    /// <summary>Says what is wrong with a width a <c>/W</c> gives, or null when nothing is.</summary>
    /// <param name="width">The width, when it is an integer.</param>
    private static string? WidthFault(long? width) => width switch
    {
        null => "its /W gives a field a width that is not an integer",
        < 0 => "its /W gives a field a negative width",
        _ => null,
    };

    /// <summary>
    /// Determines whether what a place a section may have been moved to holds declares itself a cross-reference stream, and
    /// no other type: <c>/Type /XRef</c>, or a <c>/W</c> beside no <c>/Type</c> that names another. A guess is no section
    /// where it says it is something else — a CIDFont's <c>/W</c> gives its glyphs' widths.
    /// </summary>
    private static bool DeclaresOnlyCrossReferenceStream(PdfObject value) =>
        (value is PdfStream stream ? stream.Dictionary : value as PdfDictionary) is { } dictionary &&
        (dictionary.IsOfType(PdfName.XRef) || (dictionary.ContainsKey(PdfName.W) && dictionary.GetRaw(PdfName.Type) is not PdfName));

    /// <summary>
    /// Determines whether a cross-reference stream near the offset a hybrid table's <c>/Prev</c> names, starting no sooner
    /// than the stream the table's <c>/XRefStm</c> read or names again, may be that table's own, told by its own
    /// <c>/Prev</c> as the chain reads it (<see cref="StreamPrevious"/>): one that gives none, or gives
    /// <paramref name="tablePrevious"/>, the offset the table's names, hands the chain nothing on, or back to that offset,
    /// as a hybrid table's own stream may, and is not the section that <c>/Prev</c> means (#156). A <c>/Prev</c> of null is
    /// none (7.3.7); one that names another offset, or gives no offset in the file, is a <c>/Prev</c> all the same.
    /// </summary>
    /// <param name="value">
    /// The stream, its dictionary read whole, or the dictionary that the reading of the stream the <c>/XRefStm</c> read
    /// kept; null for none, which gives no <c>/Prev</c>.
    /// </param>
    /// <param name="tablePrevious">The offset the table's <c>/Prev</c> names, as written.</param>
    private bool MayBeTablesOwn(PdfObject? value, long tablePrevious) =>
        StreamPrevious(value) is var given && (given == NoStreamPrevious || given == tablePrevious);

    /// <summary>What <see cref="StreamPrevious"/> gives for a dictionary that gives no <c>/Prev</c>, or gives null.</summary>
    private const long NoStreamPrevious = -1;

    /// <summary>
    /// What <see cref="StreamPrevious"/> gives for a <c>/Prev</c> from which the chain reads no offset in the file.
    /// </summary>
    private const long StreamPreviousNoOffset = -2;

    /// <summary>
    /// Reads the offset a cross-reference stream's <c>/Prev</c> gives as the chain reads it
    /// (<see cref="ReadSectionOffset"/>), nothing reported or loaded: a real with no fractional part as its integer, and a
    /// reference as what a section already read places there (<see cref="ReadWithoutLoading"/>) — through each object a
    /// reference that object holds leads to, as many as a reference is resolved through
    /// (<see cref="PdfReference.MaxChainLength"/>), each read so while the chain is read —, what is read kept as the chain
    /// keeps it, for the stream's own reading to find. <see cref="NoStreamPrevious"/> where the dictionary gives none, or
    /// null; <see cref="StreamPreviousNoOffset"/> where what it gives reads as no offset in the file — no integer, a
    /// reference that leads to no object a section read places where it can be read, a negative offset, one past the file's
    /// end —: a <c>/Prev</c> all the same, which the chain reports as it finds it where it takes the stream.
    /// </summary>
    /// <param name="value">The stream, or its dictionary; null for none, which gives no <c>/Prev</c>.</param>
    private long StreamPrevious(PdfObject? value)
    {
        var given = (value is PdfStream stream ? stream.Dictionary : value as PdfDictionary).GetRaw(PdfName.Prev);

        if (given is null)
        {
            return NoStreamPrevious;
        }

        // Read as the chain reads it: an object that is itself a reference is resolved, through GetObject, which reads
        // without loading while the chain is read. A negative offset names a position before the file's start, as given
        // (PositionOf).
        var read = given is PdfReference reference ? ReadWithoutLoading(reference.Id) : given;
        return read.AsInteger() is { } offset && IsInFile(PositionOf(offset)) ? offset : StreamPreviousNoOffset;
    }

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
    /// <returns>
    /// The first value that no section read so far places, which the sections a whole read reads before make the file's
    /// fault; otherwise the first a guard cut, the values past it read on — but for what depends on it: the <c>/Size</c>
    /// an <c>/Index</c> so cut may stand beside, and the filters' values past one (#263).
    /// </returns>
    private string? UnreadValue(PdfDictionary dictionary, out PdfObjectId? cut)
    {
        var first = default(FirstUnread);

        if (first.Ends(Unread(dictionary, PdfName.W, out var widths, out cut), cut))
        {
            return first.Fault;
        }

        for (var i = 0; widths is PdfArray w && i < Math.Min(3, w.Count); i++)
        {
            if (first.Ends(Follow(PdfName.W, w[i], entry: false, out _, out cut), cut))
            {
                return first.Fault;
            }
        }

        var indexFault = Unread(dictionary, PdfName.Index, out var index, out cut);

        if (first.Ends(indexFault, cut))
        {
            return first.Fault;
        }

        if (index is PdfArray ranges)
        {
            foreach (var element in ranges)
            {
                if (first.Ends(Follow(PdfName.Index, element, entry: false, out _, out cut), cut))
                {
                    return first.Fault;
                }
            }
        }
        else if (indexFault is null && (index is null or PdfNull) && first.Ends(Unread(dictionary, PdfName.Size, out _, out cut), cut))
        {
            return first.Fault;
        }

        var following = new Following(this);

        if (!Filters.FilterChain.TryFollow(dictionary, _guard.Bound(PdfLimit.FilterChain), ref following))
        {
            first.Ends(following.Fault, following.Cut);
        }

        cut = first.Cut;
        return first.Fault;
    }

    /// <summary>
    /// Keeps the first value of a cross-reference stream's dictionary a guard cut, until one that no section read so far
    /// places, which replaces it.
    /// </summary>
    private struct FirstUnread
    {
        /// <summary>Gets what could not be read.</summary>
        public string? Fault { get; private set; }

        /// <summary>Gets the object a guard cut that the value which could not be read leads to, if a guard cut one.</summary>
        public PdfObjectId? Cut { get; private set; }

        /// <summary>Takes what a value could not be read for, and says whether it ends the reading: no guard cut it.</summary>
        /// <param name="fault">What the value could not be read for, or null when it could be.</param>
        /// <param name="cut">The object a guard cut that left it unread, or null when none did.</param>
        public bool Ends(string? fault, PdfObjectId? cut)
        {
            if (fault is null)
            {
                return false;
            }

            if (cut is null || Fault is null)
            {
                Fault = fault;
                Cut = cut;
            }

            return cut is null;
        }
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

        if (PairsFault(ranges) is { } pairs)
        {
            return pairs;
        }

        var checkedSubsections = new long[ranges.Count];

        for (var i = 0; i < ranges.Count; i += 2)
        {
            var start = ranges.Resolved(i).AsInteger();
            var count = ranges.Resolved(i + 1).AsInteger();

            if (SubsectionFault(ranges, i, start, count) is { } fault)
            {
                return fault;
            }

            checkedSubsections[i] = start.GetValueOrDefault();
            checkedSubsections[i + 1] = count.GetValueOrDefault();
        }

        subsections = checkedSubsections;
        return null;
    }

    /// <summary>Says what is wrong with how many values an <c>/Index</c> holds, or null when it holds pairs.</summary>
    private static string? PairsFault(PdfArray ranges) => ranges.Count == 0 || ranges.Count % 2 != 0
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"its /Index holds {ranges.Count} {(ranges.Count == 1 ? "value" : "values")}, not pairs of a first object number and a count of rows")
        : null;

    /// <summary>
    /// Says what is wrong with the subsection an <c>/Index</c> gives at <paramref name="at"/>, or null when nothing is: its
    /// first number is an object number, and its count numbers object numbers alone.
    /// </summary>
    /// <param name="ranges">The <c>/Index</c>, whose written elements a fault describes.</param>
    /// <param name="at">Where the subsection's first number lies.</param>
    /// <param name="start">The first number, when it is an integer.</param>
    /// <param name="count">The count, when it is an integer.</param>
    private static string? SubsectionFault(PdfArray ranges, int at, long? start, long? count)
    {
        if (start is null || count is null)
        {
            return $"its /Index holds {DescribeValue(ranges[start is null ? at : at + 1])} where an integer belongs";
        }

        if (count < 0)
        {
            return "its /Index gives a subsection a count of rows out of range";
        }

        // The last number is compared by subtraction: a count as large as a long would carry start + count - 1 past what one
        // holds, and the rows past int.MaxValue would be read under negative numbers.
        return start is < 0 or > PdfObjectId.MaxNumber || count > PdfObjectId.MaxNumber - start + 1
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"its /Index gives a subsection of {count:N0} {(count == 1 ? "row" : "rows")} from object {start}, outside object numbers 0 to {PdfObjectId.MaxNumber}")
            : null;
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
    /// unknown, and the chain ends there for the guard, which is reported, not for the file (#175) — as it does where a guard
    /// cut the section's own rows, which may number the object the reference names (#264). So does one that leads to an
    /// object <see cref="PdfReaderLimits.MaxObjectLength"/> cut, as a cross-reference stream's values do: the guard is
    /// reached once the chain is read (#263).
    /// </remarks>
    /// <param name="trailer">The section's trailer, or its stream's dictionary.</param>
    /// <param name="key">The entry to read, <c>/Prev</c> or <c>/XRefStm</c>.</param>
    /// <param name="section">The offset of the section, which a report names.</param>
    /// <param name="hidden">
    /// What the entry is, when it leads to an object a guard cut, which ends the chain — "its /Prev is the reference 9 0 R,
    /// which one of the reader's limits cut" —; null otherwise.
    /// </param>
    /// <param name="rowsHidden">
    /// Whether a guard cut the section's own rows, or filled the index at them: a reference that leads to a number a row it
    /// left unread may give names what is unknown, as past a full index (#264).
    /// </param>
    private long SectionOffset(PdfDictionary trailer, PdfName key, long section, out string? hidden, bool rowsHidden = false)
    {
        var offset = ReadSectionOffset(trailer, key, out hidden, out var notAnOffset, rowsHidden);

        if (notAnOffset is not null)
        {
            ReportNotAnOffset(key, notAnOffset, section);
        }

        return offset;
    }

    /// <summary>
    /// Reads the offset a section's <c>/Prev</c> or <c>/XRefStm</c> gives as <see cref="SectionOffset"/> does, or -1 when it
    /// gives none, and says, rather than reports, what it gives when that is no offset: a cross-reference stream the chain
    /// goes on past through its <c>/Prev</c> is reported first, in words that depend on it (<see cref="HandOnPrevious"/>).
    /// </summary>
    /// <param name="trailer">The section's trailer, or its stream's dictionary.</param>
    /// <param name="key">The entry to read, <c>/Prev</c> or <c>/XRefStm</c>.</param>
    /// <param name="hidden">What the entry is, when it leads to an object a guard cut, which ends the chain; null otherwise.</param>
    /// <param name="notAnOffset">
    /// What the entry is, when it names no section, its sections lost — neither an offset nor what a guard hides —, which
    /// <see cref="ReportNotAnOffset"/> reports: "is the name /Nowhere, not an offset"; null otherwise.
    /// </param>
    /// <param name="rowsHidden">Whether a guard cut the section's own rows, or filled the index at them.</param>
    private long ReadSectionOffset(PdfDictionary trailer, PdfName key, out string? hidden, out string? notAnOffset, bool rowsHidden = false)
    {
        hidden = null;
        notAnOffset = null;

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
        // guard, not the file (#175). So does one leading to a row a guard left unread in the section itself, its rows cut
        // (#264).
        if ((_indexFullAt >= 0 || rowsHidden) && LeadsPastIndex(value, out _))
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

        notAnOffset = read is null
            ? $"is {DescribeValue(value)}, which no section read before it places where it can be read"
            : $"is {DescribeValue(value)}, not an offset";
        return -1;
    }

    /// <summary>
    /// Reports that the <c>/Prev</c> or <c>/XRefStm</c> of the section at <paramref name="section"/> is no offset
    /// (<see cref="ReadSectionOffset"/>), records it, its fault <paramref name="fault"/>, and marks the index incomplete: the
    /// sections it names are looked for by rebuilding the index.
    /// </summary>
    /// <param name="key">The entry, <c>/Prev</c> or <c>/XRefStm</c>.</param>
    /// <param name="fault">What the entry is, in words, as <see cref="ReadSectionOffset"/> gives it.</param>
    /// <param name="section">The offset of the section, which the report names.</param>
    private void ReportNotAnOffset(PdfName key, string fault, long section)
    {
        _indexIncomplete = true;
        _structure.Add(new XRefSectionRecord(FileQuote.Name(key), -1, section) { State = XRefSectionState.NotFound, Fault = fault });
        _diagnostics.Warn(
            PdfDiagnosticCodes.XRefSectionMissing,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The {FileQuote.Name(key)} of the cross-reference section at offset {section} is not an offset; the sections it names are looked for by rebuilding the index."),
            section);
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
                    // ISO 32000-1 defines types 0, 1 and 2, and reads any other "as a reference to the null object, thus
                    // permitting new entry types to be defined in the future" (7.5.8.3): the row stands for its object as a
                    // free row does, no older section's row standing for it, and is reported with the section (#221). A type
                    // field wider than 8 bytes whose leading bytes are not all zero gives one too. One whose number was
                    // refused before it was read stands for nothing, which the report says.
                    if (!AddFromChain(number, XRefEntry.Free, section))
                    {
                        section.ReservedRowsRefused++;
                    }

                    section.ReservedRow ??= ReservedRowFault(number, type, row, widths[0]);
                    section.ReservedRows++;
                    break;
            }
        }
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

        static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Says what a cross-reference stream's row of a reserved type gives object <paramref name="number"/>: its type, or a
    /// type of more than 64 bits, where the type field, <paramref name="width"/> bytes wide, holds one (#221).
    /// </summary>
    /// <remarks>
    /// A type field of 8 bytes whose every bit is set gives the largest value 64 bits hold, as one past them is read
    /// (<see cref="ReadField"/>): the field's bytes tell the two apart.
    /// </remarks>
    private static string ReservedRowFault(int number, ulong type, ReadOnlySpan<byte> row, int width) =>
        IsPast64Bits(row, 0, width)
            ? string.Create(CultureInfo.InvariantCulture, $"the row for object {number} gives it a type of more than 64 bits")
            : string.Create(CultureInfo.InvariantCulture, $"the row for object {number} gives it type {type}");

    /// <summary>
    /// Determines whether the field of <paramref name="row"/> that starts at <paramref name="start"/>, <paramref name="width"/>
    /// bytes wide, holds a value past 64 bits: it is wider than 8 bytes, and its leading bytes are not all zero.
    /// </summary>
    private static bool IsPast64Bits(ReadOnlySpan<byte> row, int start, int width) =>
        width > sizeof(ulong) && row.Slice(start, width - sizeof(ulong)).ContainsAnyExcept((byte)0);

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
}
