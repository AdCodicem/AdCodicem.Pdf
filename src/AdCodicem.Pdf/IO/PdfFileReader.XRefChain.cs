using System.Buffers;
using System.Globalization;
using System.Runtime.InteropServices;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO.XRef;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO;

internal sealed partial class PdfFileReader
{
    /// <summary>
    /// Reads the chain of cross-reference sections from the newest, following each <c>/Prev</c> and the
    /// <c>/XRefStm</c> of a hybrid file.
    /// </summary>
    /// <remarks>
    /// A section the chain names must be read, or the objects only it indexes are lost. One that is not where
    /// it is named is looked for nearby, as an object is — but for one that is there and cannot be read, the
    /// <c>xref</c> keyword or a stream whose dictionary gives <c>/Type /XRef</c>, which is reported where it is: the
    /// offset names it, and any section near it is another (#281). Looked for nearby, the nearest place that declares itself
    /// a section so is the section found moved, though it cannot be read: it is reported where it was found, as where it is
    /// named, rather than passed over for an older section farther off. One that cannot be found or read is reported and
    /// leaves the index incomplete: the chain stops at a lost <c>/Prev</c> and goes on past a lost <c>/XRefStm</c>,
    /// and an object the index then lacks is looked for by rebuilding it, when it is asked for. A cross-reference stream
    /// whose dictionary gives <c>/Type /XRef</c> and a <c>/Prev</c>, read whole, is lost for its rows alone: the chain goes
    /// on through that <c>/Prev</c>, and no older section's row stands for a number the stream declares, which is looked
    /// for by rebuilding the index too (#208) — but where <c>startxref</c> names it, the whole file is rebuilt, as for any
    /// first section that cannot be read. A section read only in part — a row that is no row ends a subsection, a stream
    /// holds fewer rows than it declares, the end of the file cuts a table before its trailer once it read a row, a row is
    /// refused — is kept, the section <c>startxref</c> names among them: its rows are read and its trailer followed where it
    /// has one, it is reported once, the index is left incomplete, and no section read after it stands for a number whose
    /// row it lost (#207, #210). Nothing is rebuilt at opening that nobody asks for. A section the chain has already
    /// reached — by the offset that names it, or where it starts, read there, or reached there and lost though it declares
    /// itself one —, is not read again, whatever white space or comments the offset that names it again falls short by:
    /// named by a <c>/Prev</c>, it ends the chain in a loop (#188); named by an <c>/XRefStm</c>, it is recorded as named
    /// again, a hybrid file's stream counting for each table that names it (#156). An <c>/XRefStm</c> that names no
    /// cross-reference stream of its table's revision — a classic table, the table itself among them, or a section the chain
    /// reads through <c>/Prev</c> or <c>startxref</c> — is reported, and leaves the index incomplete: the stream the table
    /// meant is unknown, as the section past a loop is. One that reaches the section its own table's <c>/Prev</c> reaches —
    /// by the same offset, through the white space before it, or found near the offset it names — is not followed: the
    /// <c>/Prev</c> reads that section whole, its own links with it. A classic table an <c>/XRefStm</c> names is read in the
    /// stream's place all the same, its rows taken and its own links left unread (#263). A section whose rows a guard
    /// stopped — cut them, or filled the index at them —, or a hybrid table whose stream a guard so stopped, ends the chain:
    /// an older section's rows would stand for those the guard left unread. What it names is judged all the same, as far as
    /// the guard lets it be, and nothing past the guard is read (#264): a <c>/Prev</c> or an <c>/XRefStm</c> that can name no
    /// section whatever the guard hid — no offset, an offset outside the file, a section the chain has reached, what the
    /// table's <c>/Prev</c> reaches too — is reported as a whole read reports it, and leaves the index incomplete as a whole
    /// read does; one that may name a sound section — an offset in the file the chain has not reached, a reference to a row
    /// the guard left unread — is the guard's. What became of each section is recorded in <see cref="Structure"/>, for the
    /// validation rules.
    /// </remarks>
    /// <param name="startOffset">The offset <c>startxref</c> gives.</param>
    private bool TryReadXRefChain(long startOffset)
    {
        var visited = new HashSet<long>(SeededComparer.Instance);

        // The record of what the chain named, or read, first at each position it reached: where each section was named,
        // and where it starts once read. An /XRefStm that names a position again is recorded as naming what is there
        // (#156), keyed as the positions the chain read at are (_sectionsReadAt).
        var sectionsAt = new Dictionary<long, XRefSectionRecord>(SeededComparer.Instance);

        // The record of an /XRefStm that names what its table's /Prev names, which that /Prev reads next.
        XRefSectionRecord? namedWithPrevious = null;

        // The record of what the table's /XRefStm read in the stream's place, its links left unread, which that table's /Prev,
        // read next, may come to through the search near the offset it names (#156).
        XRefSectionRecord? readInStreamsPlace = null;

        // The record of the cross-reference stream a newer table's /XRefStm read in the stream's place, which the table's own
        // /XRefStm names again: the search near the offset the table's /Prev, read next, names tells by it which streams there
        // may be the table's own, as by a stream the /XRefStm read (#156).
        XRefSectionRecord? streamNamedAgain = null;
        var offset = startOffset;
        var sections = 0;
        var naming = "startxref";
        var namedFrom = _structure.StartXRefPosition;

        // Whether the offset was named by a section a guard stopped, which the chain ends at: judged, never read (#264).
        var pastGuard = false;

        while (offset >= 0)
        {
            // What the /XRefStm of the table that names this offset read, if the offset is that table's /Prev.
            var streamsPlace = readInStreamsPlace;
            readInStreamsPlace = null;
            var namedAgainStream = streamNamedAgain;
            streamNamedAgain = null;

            // An offset in the file that a section a guard stopped names, where the chain has not been, is where the guard
            // hides the older sections, which its report says: a section there would be read only past the guard. One the
            // chain has reached, or one outside the file, names no section there whatever the guard hid, and is judged as a
            // whole read judges it.
            var reached = ReachedAt(offset, visited, out _);

            if (pastGuard && reached < 0 && IsInFile(PositionOf(offset)))
            {
                break;
            }

            if (reached >= 0)
            {
                // The section the chain should have gone on to is unknown: what only it indexed is the index's to
                // find, as a missing section's is.
                // Placed at the section looped back to, a position in the file — where it starts, when the offset names the
                // white space before it, whether the chain named that offset before or not (#188) —; one named outside the
                // file, as an /XRefStm and a /Prev naming the same offset past its end are, is placed at what named it. An
                // offset the chain named and found no section at, nor any reached past the white space there, is where it
                // loops: one where it found the section moved among them.
                _indexIncomplete = true;
                var looped = reached;

                if (IsInFile(looped) && !_sectionsReadAt.Contains(looped))
                {
                    // Where the chain read, or looked for, a section at the offset, it knows where the first token there
                    // starts; a section found moved from it keeps the loop where it was named, known without a look. Only an
                    // offset that leads to what another link's record holds is looked at again.
                    var start = sectionsAt.GetValueOrDefault(looped) switch
                    {
                        { } there when there.Offset == looped => looped + there.Padding,
                        { } moved when moved.NamedOffset == looped => looped,
                        _ => StartOf(looped, out _),
                    };
                    looped = _sectionsReadAt.Contains(start) ? start : looped;
                }

                // A section the chain named there may have been read, or not: one read where it was found moved is read all
                // the same, though the chain named another offset for it, and one that is there and could not be read,
                // though it declares itself one, was reached, where it starts, or where the chain found it moved. Anything
                // else is an offset the chain only named: an /XRefStm that names nothing, named again by a /Prev, among them.
                // What was first named, or read, at a position the chain reached is recorded there, and changes its state
                // when it is read where it was named, or found moved.
                var target = sectionsAt.GetValueOrDefault(looped) switch
                {
                    { State: XRefSectionState.Read or XRefSectionState.Relocated } => XRefLoopTarget.Read,
                    { State: XRefSectionState.Malformed } lost when _sectionsReadAt.Contains(lost.Offset + lost.Padding) => XRefLoopTarget.Reached,
                    _ => XRefLoopTarget.Named,
                };
                _structure.LoopOffset = looped;
                _structure.LoopWrittenOffset = offset;
                _structure.LoopTarget = target;
                _structure.LoopNamedBy = naming;
                _structure.LoopNamedFrom = namedFrom;
                var inFile = IsInFile(looped);
                var what = target switch
                {
                    XRefLoopTarget.Read => "which the chain has already read",
                    XRefLoopTarget.Reached => "where the chain has already reached a section it could not read",
                    _ => inFile ? "which the chain has already named" : "outside the file, which the chain has already named",
                };
                _diagnostics.Warn(
                    PdfDiagnosticCodes.XRefChainCycle,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The cross-reference chain loops back on itself: the {naming} of the section at offset {namedFrom} names offset {DescribeOffset(offset)}, {what}."),
                    inFile ? looped : namedFrom);
                break;
            }

            visited.Add(offset);

            if (++sections > _guard.Bound(PdfLimit.XRefSectionCount))
            {
                // Each incremental save adds a section, so a long chain is a sound file saved often; the newest
                // sections, read first, are the ones that win, and what only the older ones index reads as null until the
                // guard is lifted.
                // Placed where the chain goes on, a position in the file; one named outside the file is placed at what
                // named it, the offset in the message, as a section that is missing is (#125).
                _cutByGuard = true;

                // An /XRefStm of the table before that names what this /Prev names too was reported as naming what the chain
                // looks for there, which the guard keeps it from reading: the guard hides what is there, as past a guard
                // that stopped the table (#156, #264).
                if (namedWithPrevious is { NamesWhatPreviousReads: true } leftToGuard)
                {
                    leftToGuard.NamesWhatPreviousReads = false;
                    leftToGuard.NamesNoStream = "what its /Prev names too, past the guard that ended the chain there";
                }

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
                            $"The cross-reference chain has more than {CountText.Grouped(_guard.Bound(PdfLimit.XRefSectionCount), "section", "sections")}; the older ones were not read.")
                        : string.Create(
                            CultureInfo.InvariantCulture,
                            $"The cross-reference chain has more than {CountText.Grouped(_guard.Bound(PdfLimit.XRefSectionCount), "section", "sections")}; the older ones were not read: the chain goes on at offset {DescribeOffset(offset)}, outside the file."),
                    inFile ? cut : namedFrom);
                break;
            }

            // Whether the sections read so far refused every number is noted before this one refuses what it loses, so that its
            // report, made once it refused it, tells the two apart (#210).
            var section = new XRefSectionRecord(naming, PositionOf(offset), namedFrom)
            {
                WrittenOffset = offset,
                EveryNumberRefusedBefore = _everyNumberRefused,
            };
            _structure.Add(section);
            sectionsAt.TryAdd(section.NamedOffset, section);

            if (namedWithPrevious is not null)
            {
                // The /XRefStm of the table before named this section too: the /Prev reads it, and it is what the offset that
                // /XRefStm names leads to, should an older table name that offset again.
                namedWithPrevious.ReadAs = section;
                sectionsAt.TryAdd(namedWithPrevious.NamedOffset, section);
                namedWithPrevious = null;
            }

            if (TryReadXRefSection(section.Offset, section, out var previous, out var hybrid) != XRefSectionState.Read)
            {
                // The first section is where startxref says, and one that is not there leaves no index to
                // complete: the file is scanned whole, which is reported. A later one is named by the section
                // before it.
                if (sections == 1)
                {
                    // What gives a /W and says it is another type holds no section, as where a /Prev names it (#281).
                    NoSectionWhereAnotherType(section);
                    return false;
                }

                // A section a guard cut is where it was named, and is not looked for elsewhere: what lies near it would
                // stand for it. So is one that is there and cannot be read: the offset names it, and any section near it
                // is another (#281). Looked for nearby, it may be found moved where it cannot be read, the nearest place
                // declaring itself a section (TryRelocateXRefSection): it is reported there, as where it is named. The search
                // may come to what the table's /XRefStm read in the stream's place, which it takes, reading it whole.
                if (section.CutByLimit || DeclaresItselfASection(section) ||
                    !TryRelocateXRefSection(
                        offset, section, leaveAt: -1, leaveIsTable: false, streamsPlace, namedAgainStream, out previous, out hybrid, out _))
                {
                    // A cross-reference stream whose dictionary was read, and whose rows cannot be, names the older sections
                    // through its /Prev as a whole read names them: the chain goes on through it, the section reported, its
                    // rows lost and the numbers it declares refused to the older sections' rows (#208), where it was named
                    // or where it was found moved. It is not looked for elsewhere, and where it starts is reached: a /Prev
                    // that names it again, at that offset or through the white space before it, ends the chain in a loop,
                    // and an /XRefStm records its naming again.
                    if (PreviousToHandOn(section) is { } dictionary)
                    {
                        RecordSectionReached(section, sectionsAt);
                        offset = HandOnPrevious(section, dictionary, naming, offset, namedFrom);
                        naming = "/Prev";
                        namedFrom = section.Offset;
                        continue;
                    }

                    ReportLostSection(section, naming, offset, namedFrom);
                    break;
                }
            }

            // Found where the table's /XRefStm read a section in the stream's place, the /Prev read it again, whole, its links
            // with it: the two links of the table reach one section, which the /Prev reads, as where the /XRefStm reaches what
            // the /Prev names (#156). The /XRefStm's reading becomes the naming of what the /Prev read, and what that reading
            // reported of the section, read in part or giving rows of a reserved type, is not reported again. Only the search
            // near the offset the /Prev names comes there: an offset that names where the section starts, or the white space
            // before it, reaches it, and ends the chain in a loop, or leaves the /XRefStm to the /Prev.
            var readAgain = streamsPlace is { } place && section.Offset == place.Offset + place.Padding;

            if (readAgain)
            {
                LeaveReadingToPrevious(streamsPlace!, section, sectionsAt);

                // The rows are those the /XRefStm's reading read, and the reader reported them from that reading. What it
                // refused itself — a row for a number it gives twice, the numbers a stream it read in part declares, every
                // number where they cannot be kept apart —, which the second reading meets as refused before it, is no
                // refusal by a section read before: a row of a reserved type it read as the null object is not said to stand
                // for nothing, nor a row it refused said to be a newer section's refusal, nor every number said refused before
                // the chain came to the section (#156, #221).
                section.ReservedRowsRefused = streamsPlace!.ReservedRowsRefused;
                section.RowsRefusedBefore = streamsPlace.RowsRefusedBefore;
                section.EveryNumberRefusedBefore = streamsPlace.EveryNumberRefusedBefore;
            }

            RecordSectionReached(section, sectionsAt);
            _olderNumberedPast |= sections > 1 && section.HighestNumber > _highestRead;
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

            // The numbers whose rows the section lost are refused to the older sections' rows once what its trailer names is
            // judged: a reference there to a number it lost may name what its rows past a guard hold, the rows read whole
            // giving it (#264); what newer sections refused, no row of this one places.
            if (section.Incomplete)
            {
                RefuseRowsLost(section);
            }

            // The section is reported once it has refused what it lost, so that its report says what that refusal leaves to a
            // rebuild — every object, where it refused every number (#210) —, in the order its reading met them: nothing
            // between its reading and here reports on it.
            if (!readAgain)
            {
                ReportSectionReadInPart(section, naming);
                ReportReservedRows(section, naming);
            }

            if (section.TrailerCutByLimit)
            {
                // A trailer a limit cut may have named an older section past the cut, and given keys the older trailers
                // give otherwise: the chain ends here, and what only the older sections index is the guard's to hide, not
                // the file's to lack.
                _cutByGuard = true;
                previous = -1;
            }

            // Rows a guard stopped — cut, or past an index it filled — end the chain at this section: an older section's rows
            // would stand for those the guard left unread, or find no room, being for numbers the index lacks. What the section
            // names is judged all the same, as far as the guard lets it be, and nothing past the guard is read (#264): an
            // /XRefStm that names what the chain has reached, or what the /Prev names too, or that lies outside the file, as a
            // whole read judges it; then the /Prev, at the top of the loop. Where a guard stopped the rows, the section's
            // reading gives its links apart (_previousPastGuard), and none to follow.
            pastGuard = _previousPastGuard >= 0 || _streamPastGuard >= 0;

            if (pastGuard)
            {
                previous = _previousPastGuard;
                hybrid = _streamPastGuard;
            }

            // What the /XRefStm reaches, and where the section the /Prev names starts: the two links reach one section by the
            // same offset, or by two that name the white space before it, or where the search near the offset the /XRefStm
            // names finds what the /Prev names — but for a /Prev that names what the chain has reached, which reads nothing.
            var hybridStart = -1L;
            var hybridReached = hybrid >= 0 ? ReachedAt(hybrid, visited, out hybridStart) : -1;
            var previousTable = false;
            var previousStart = hybrid >= 0 && hybridReached < 0 ? StartOfNamed(previous, visited, out previousTable) : -1;

            if (hybrid >= 0 && hybridReached < 0 && (hybrid == previous || (previousStart >= 0 && hybridStart == previousStart)))
            {
                // The /XRefStm names what the /Prev names: read as the table's stream, that section would have its own links
                // left unread, and the /Prev would loop back to it. The /Prev reads it, whole, and the table's own stream is
                // unknown (#156) — or, past a guard that stopped the table, reads nothing in the file, which the guard hides.
                // Named through the white space before that section, the /XRefStm falls short of it as a /Prev read there
                // would; where both name one offset, the /Prev's record says so once.
                var shortBy = hybrid != previous ? (int)(hybridStart - PositionOf(hybrid)) : 0;
                namedWithPrevious = LeaveToPrevious(hybrid, section, pastGuard, shortBy);
            }
            else if (hybridReached >= 0)
            {
                // Named again: what is there was read, or looked for, once, where the chain first reached it, and the naming
                // is recorded, so that a hybrid file's stream a newer table names too counts for each (#156). What the chain
                // read there and is no hybrid file's stream stands for none: the table's own stream is unknown.
                // Reached through the white space before where that section starts, the naming falls short of it as a
                // section read there does.
                var again = NameAgain(
                    hybrid, section, sectionsAt.GetValueOrDefault(hybridReached), (int)(hybridReached - PositionOf(hybrid)));

                if (again.ReadAs is { State: XRefSectionState.Read or XRefSectionState.Relocated, IsHybridStream: false } read)
                {
                    ReportNoStream(
                        again,
                        ReferenceEquals(read, section)
                            ? "the table itself"
                            : string.Create(
                                CultureInfo.InvariantCulture,
                                $"the cross-reference {(read.Kind == XRefSectionKind.Stream ? "stream" : "table")} at offset {read.Offset}, which the chain read through {read.NamedBy}"));
                }
                else if (again.ReadAs is { Kind: XRefSectionKind.Table, State: XRefSectionState.Malformed } table)
                {
                    // A classic table that cannot be read is no stream either, whatever its rows hold: each naming of it is
                    // reported, as each of one that reads is. One a guard cut is none of them: the chain ends where it was
                    // named, and no older table names it again.
                    ReportNoStream(
                        again,
                        string.Create(CultureInfo.InvariantCulture, $"the cross-reference table at offset {table.Offset}, which cannot be read"));
                }
                else if (again.ReadAs is { State: XRefSectionState.Read or XRefSectionState.Relocated, IsHybridStream: true } namedStream)
                {
                    // A hybrid file's stream a newer table read, its /XRefStm copied into this table's trailer, as a writer
                    // that copies it leaves it: the table's own stream, which nothing may name, lies past it, if anywhere.
                    // The table's /Prev, read next, tells by it which streams near the offset it names may be that one; it
                    // is not taken, a section the chain reached (#330).
                    streamNamedAgain = namedStream;
                }
            }
            else if (hybrid >= 0 && !(pastGuard && IsInFile(PositionOf(hybrid))))
            {
                // A hybrid-reference file keeps a classic table for old readers and a stream for the rest: without
                // the stream, the objects it indexes are missing, and the chain goes on through /Prev. Past a guard that
                // stopped the table, one outside the file is missing as read whole, nothing there being read; one in the
                // file is the guard's to hide.
                visited.Add(hybrid);
                var cut = _cutByGuard;
                var stream = new XRefSectionRecord("/XRefStm", PositionOf(hybrid), section.Offset)
                {
                    WrittenOffset = hybrid,
                    NamingTrailer = section.Trailer,
                    EveryNumberRefusedBefore = _everyNumberRefused,
                };

                // Where nothing the /XRefStm names can be read, the search near it stops where the section the /Prev names
                // starts, which it would find there before any section farther off: what the /Prev names, read through it
                // alone. Only where the /Prev keeps its place there, as its own reading does: a /Prev that names no section —
                // an object of another type, a string, an object that gives a /W and does not read as a section — looks for
                // its own nearby, and the /XRefStm's search goes on past what it names, a place like any other. Told only when
                // the search comes there (KeepsItsPlace).
                var left = KeptPlace.None;
                var lost = TryReadXRefSection(stream.Offset, stream, out _, out _) != XRefSectionState.Read &&
                    (stream.CutByLimit || DeclaresItselfASection(stream) ||
                        !TryRelocateXRefSection(hybrid, stream, previousStart, previousTable, null, null, out _, out _, out left));

                if (left == KeptPlace.Section)
                {
                    // The /XRefStm names, a few bytes off, what the /Prev names: left to that /Prev, as where it names the same
                    // offset, the offset named all the same, so that an older table naming it again, as a writer that copies
                    // /XRefStm into the next trailer leaves it, names what the /Prev read. Only a table no guard stopped has
                    // its /XRefStm looked for near the offset it names.
                    namedWithPrevious = LeaveToPrevious(hybrid, section, pastGuard: false, shortBy: 0);
                }
                else if (left == KeptPlace.Guard)
                {
                    // The search stopped where a guard cut what the /Prev names before it said what it is, or cut a value
                    // its rows are read with: whether the /Prev keeps its place there, and the /XRefStm's search would go on
                    // past it to the table's stream, is the guard's to hide, and the /Prev's reading reports the guard there.
                    // The naming is recorded, leading to what the /Prev reads, and is the guard's, not the file's: nothing is
                    // reported of it, and the index is not left incomplete for it, as for an /XRefStm that names an offset
                    // in the file past a guard that stopped the table (#156, #264, ADR 34).
                    namedWithPrevious = NameAgain(hybrid, section, readAs: null, shortBy: 0);
                }
                else
                {
                    _structure.Add(stream);
                    sectionsAt.TryAdd(stream.NamedOffset, stream);

                    if (lost)
                    {
                        // A stream that declares itself one, its dictionary read whole, lost its rows alone: they may give a
                        // newer revision of any number it declares, which no section read after it stands for, as for a
                        // stream the chain goes on past through its /Prev (#210).
                        if (DeclaredByLostStream(stream) is { } declared)
                        {
                            RefuseDeclared(declared);
                            stream.NumbersRefused = true;
                        }
                        else if (stream.Incomplete || stream.SubsectionsUnknown)
                        {
                            // So does a classic table that cannot be read, the chain going on past it through the table that
                            // names it — a stream that lost rows so is read, never lost —: the rest of the subsection its fault
                            // ended is lost, and, where what follows the fault cannot be known — a subsection header, a stray
                            // token, the end of the file —, any number may have had a row there, and every number is refused,
                            // as for a stream whose numbering cannot be read (#156, #210). A table a guard cut is kept, the
                            // guard's.
                            RefuseRowsLost(stream);
                            stream.NumbersRefused = true;
                        }

                        ReportLostSection(stream, "/XRefStm", hybrid, section.Offset);

                        // One that is there and declares itself a section is reached where it starts, reported once: a
                        // table that names it again, at that offset or through the white space before it, records its
                        // naming again (#156).
                        if (!stream.CutByLimit && DeclaresItselfASection(stream))
                        {
                            RecordSectionReached(stream, sectionsAt);

                            if (stream.Kind == XRefSectionKind.Table)
                            {
                                // Its keyword says it is a classic table, whatever its rows hold: the /XRefStm names no stream
                                // of the table's revision, which is reported beside the table's own fault (#156).
                                ReportNoStream(stream, "a cross-reference table, which cannot be read");
                            }
                        }
                    }
                    else
                    {
                        RecordSectionReached(stream, sectionsAt);

                        // Read in the stream's place, its links left unread: the table's /Prev, read next, may come to it.
                        readInStreamsPlace = stream;

                        if (stream.Kind == XRefSectionKind.Table)
                        {
                            // A classic table stands in the stream's place, its rows read and its own links left unread
                            // (#263): whether it holds what the table's stream would have is unknown (#156). One a guard cut
                            // before its first row read none, which the naming says, the guard's own report beside it; one
                            // whose first row is no row reached that row before the cut, and its fault is reported beside the
                            // naming, which keeps the whole read's words.
                            ReportNoStream(
                                stream,
                                stream.CutByLimit && stream.HighestNumber < 0 && !stream.Incomplete
                                    ? "a cross-reference table, which one of the reader's limits cut before its first row"
                                    : "a cross-reference table, whose rows were read in the stream's place");
                        }

                        if (stream.Incomplete)
                        {
                            RefuseRowsLost(stream);
                        }

                        // Reported once it refused what it lost, as the chain's sections are (#210).
                        ReportSectionReadInPart(stream, "/XRefStm");
                        ReportReservedRows(stream, "/XRefStm");
                    }
                }

                _olderNumberedPast |= sections > 1 && stream.HighestNumber > _highestRead;
                _highestRead = Math.Max(_highestRead, stream.HighestNumber);

                if (_indexFullAt >= 0 || _cutByGuard != cut)
                {
                    // Filled by the stream, an older section could add nothing; the stream's rows, or the stream, cut by a
                    // limit, an older section's rows would stand for the objects the rows left unread define. Its trailer,
                    // unread, may give what the read ones lack, where the file can hold it and the chain has not reached it.
                    // The chain ends at the stream, and the table's /Prev is judged as one a section a guard stopped gives
                    // (#264).
                    _structure.TrailersHiddenByGuard |= previous >= 0 && IsInFile(PositionOf(previous)) && !ReachedBefore(previous, visited);
                    pastGuard = true;
                }
            }

            offset = previous;
            naming = "/Prev";
            namedFrom = section.Offset;
        }

        // A chain that indexed nothing leaves the whole file to a rebuild, which reads every object: no stream passed over is
        // left unread then.
        if (_xref.Count == 0)
        {
            return false;
        }

        ReportStreamsPassedOver();
        return true;
    }

    /// <summary>
    /// Reports each cross-reference stream the search near the offset a hybrid table's <c>/Prev</c> names passed over as
    /// possibly that table's own, another section taken first, that declares itself one — its dictionary gives
    /// <c>/Type /XRef</c>, and its header is a token a load reads from the start of its line — and that the chain has not
    /// reached, at its header or inside its object number (<see cref="_streamsPassedOver"/>): it may hold that table's rows,
    /// which the index then lacks (#156). None is reported where one of the reader's limits left part of the chain unread,
    /// nor anywhere in the document where <see cref="PdfReaderLimits.MaxTrailerLength"/> may have hidden a link the
    /// chain would have followed, or cut what would end it (<see cref="_guardMayHideLink"/>): where white space and
    /// comments at an offset the chain names run past it, which turned the reading of the section there into the search
    /// near the offset, whatever section that search takes, the one past them read whole among them; or wherever it cut
    /// the reading at such an offset, whatever it cut, that the reading does not take for a section the guard cut — a
    /// dictionary, whatever its <c>/Type</c>, which then reads as one of another type, its <c>/Prev</c> not followed, or
    /// the object's header, which leaves no object read (ADR 34); where the reading takes it for the guard's section, the
    /// chain's own cut withholds the reports already. Nor is it asked where the chain indexed nothing, the whole file then
    /// rebuilt.
    /// </summary>
    /// <remarks>
    /// Told here, once the chain is read, and nowhere sooner: a link of an older section that reaches the stream, where it
    /// is named or found moved, reads it or reports it as it finds it — read, read in part, or there and unreadable, a
    /// guard's cut among it —; one the search came back to was read, taken or dropped as holding no section. The stream is
    /// told by its data, not by the byte a reading of it started at, as the search tells the stream an <c>/XRefStm</c> read
    /// (<see cref="NumberRunsInto"/>): a link that names a place inside its header's object number past its first byte —
    /// the first sign of the run of signs the number is written with, where that run starts with a <c>+</c>, as the
    /// header's token does (<see cref="HeaderTokenStart"/>), or its first digit — and reads a header there reads the same
    /// dictionary and the same data, and reaches that stream as one that names its header does; one that reads none there —
    /// digits that read 0, as the 0 of a number 20, or a <c>-</c> of that run — reads no object, and the search near that
    /// place finds the stream moved, its record starting at its first digit, or at its first sign where the line its number
    /// is on, which an end of line ends before <c>obj</c>, holds a <c>%</c> — a comment that ends that line, or one a
    /// string holds —, the walk back from <c>obj</c> then lexing that line (<see cref="TryReadHeaderBackwards"/>): the
    /// stream is reached all the same. A limit that left part of the chain unread — the number of its sections, a section
    /// it cut before the section said what it is, rows it cut or an index it filled, a trailer it cut, an object it cut
    /// that a value the rows or a link are read with leads to — may hide the link that names the stream, as an older
    /// table's <c>/XRefStm</c> or <c>/Prev</c> may name any offset: what lies past the guard is the guard's, unknown rather
    /// than missing (#264). So may a trailer's window that cuts the reading at an offset a <c>/Prev</c> or <c>startxref</c>
    /// names, whatever it cuts: a dictionary, whatever its <c>/Type</c>, which the parser keeps as far as it read it, the
    /// object read as one of another type, as #279 leaves it, its <c>/Prev</c> not followed, wherever it lies, past the cut
    /// or before it; or the object's header — its number, its generation or its <c>obj</c> keyword running on past the
    /// window, or white space or a comment between its tokens —, or a token where no header starts, which leaves no object
    /// read, the section looked for as one not there, as #289 leaves it. Any <c>/W</c> makes a stream of any <c>/Type</c>
    /// the section there (#263), and the cut may have hidden one — <c>/XRef</c> parted, inside the name or inside a
    /// <c>#xx</c> escape, a stream of another <c>/Type</c> that gives a <c>/W</c>, a <c>/Type</c> given twice whose later
    /// <c>/XRef</c> lies past the cut, or the whole dictionary past a header the cut parted. Where an <c>/XRefStm</c> names
    /// the offset, the chain never follows that stream's <c>/Prev</c>, and the cut hides no link the chain follows, but it
    /// is what would end the chain there, as the guard's cut of that stream does (#264), the reading #279's closing gives
    /// it: the reports are withheld alike. The reader cannot tell what the cut hid without reading past the guard, and
    /// withholds them even where it hid nothing, a dictionary of another type that gives no <c>/W</c>, or a token no header
    /// starts, among them; where the reading takes what is there for the guard's section, the chain's own cut withholds
    /// them already. So may white space and comments past a trailer's window where the chain names a section, which is not
    /// read from there: the search near the offset stands for that reading, and may take a section other than the one past
    /// them, pass over a stream the reading would not have come to, or find nothing, what the section names left unread —
    /// the guard's, though the section is looked for as one that is not there (#289). Which, the reader cannot tell without
    /// reading past the guard: once that guard is reached, no stream passed over anywhere in the document is reported, even
    /// where the search finds the section whole past the white space, which only a
    /// <see cref="PdfReaderLimits.MaxTrailerLength"/> under the 512 bytes the search reaches past the offset allows. A cut
    /// that leaves nothing unread — data past a stream's last row, an object past a whole reference it holds — hides no
    /// link, and leaves the report made, and so does the cut of the trailer of a classic table an <c>/XRefStm</c> names,
    /// whose links the chain never follows (#263): it hides no link the chain follows and ends nothing, the chain going on
    /// through the table that names it. What it costs is a look at the records of the sections the chain reached, and at
    /// as many positions as the header's object number has bytes for each stream: nothing is read here, and no stream is
    /// read to report it, its dictionary having been parsed in the search's window when it was passed over, and its
    /// number's signs and digits scanned there when the search took another section, as its line was lexed — in that
    /// window, or, where the line starts before the bytes searched, in those bytes read again with as many as
    /// <see cref="LineStartSearchLength"/> bytes before them, one window more for each such stream
    /// (<see cref="IsTokenOnItsLine"/>), a line that starts farther back declaring nothing, and the stream then not
    /// reported. Reported once for each stream, at the stream, however many searches passed over it; the index is not left
    /// incomplete for it, nor is anything looked for in its place: whether it is the table's own is unknown, and no link
    /// the chain read reaches it.
    /// </remarks>
    private void ReportStreamsPassedOver()
    {
        if (_streamsPassedOver is not { } passedOver)
        {
            return;
        }

        // A limit that left part of the chain unread may hide the link that names a stream passed over, and leaves what lies
        // past it to its own report: each marks the chain cut by a guard, or filling the index. A trailer's window may hide it
        // too where the chain is not taken for one a guard cut, which is marked apart: white space past it where the chain
        // names a section, which turned the reading of that link into a search, whatever section the search takes — the one
        // past the white space, or another, which the reader cannot tell apart without reading past the guard —, or its
        // cut of the reading at an offset the chain names, whatever it cut, which may hide a /Prev the chain would
        // follow, or, where an /XRefStm names it, is what would end the chain there (#264, #279, #289). Either withholds
        // every report of the document. A cut that left nothing unread marks neither — data past a stream's last row, an
        // object past a whole reference it holds —, and the report is made (ADR 34).
        if (ChainStoppedByGuard || _guardMayHideLink)
        {
            return;
        }

        // Not reported: a stream where a section the chain reached starts — read there, found moved there, or there and not
        // read, for a fault or a guard's cut, whether a link named it there or a search found it —, at its header or inside
        // its object number, which the chain reported as it found it; and a stream reported already.
        var told = new HashSet<long>(SeededComparer.Instance);

        foreach (var section in _structure.Sections)
        {
            if (section.State is XRefSectionState.Read or XRefSectionState.Relocated or XRefSectionState.Malformed)
            {
                told.Add(section.Offset + section.Padding);
            }
        }

        foreach (var (at, numberEnd, named, table) in passedOver)
        {
            if (StartsWithin(told, at, numberEnd))
            {
                continue;
            }

            told.Add(at);
            _diagnostics.Warn(
                PdfDiagnosticCodes.XRefStreamPassedOver,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The cross-reference stream at offset {at}, near offset {named}, which the /Prev of the cross-reference table at offset {table} names, gives no /Prev, or one that names that offset too, as that table's own stream may: the search near that offset took another section first, and the chain did not read the stream. If it is that table's own stream, the index the chain read lacks the objects only it indexes, which only a rebuild of the index for another cause finds."),
                at);
        }
    }

    /// <summary>
    /// Determines whether one of <paramref name="starts"/> lies at <paramref name="from"/> or past it, before
    /// <paramref name="to"/>: where a section the chain reached starts at the header of a stream the search near an offset
    /// passed over or inside its object number (<see cref="ReportStreamsPassedOver"/>).
    /// </summary>
    /// <remarks>
    /// A look at each position between the two, which lie within the bytes the search found the header in: the bytes of
    /// its object number, the run of signs it is written with among them. Nothing is read for it, nor allocated.
    /// </remarks>
    /// <param name="starts">Where each section the chain reached starts.</param>
    /// <param name="from">Where the header's token starts (<see cref="HeaderTokenStart"/>).</param>
    /// <param name="to">Where the header's object number ends (<see cref="NumberEnd"/>).</param>
    private static bool StartsWithin(HashSet<long> starts, long from, long to)
    {
        for (var position = from; position < to; position++)
        {
            if (starts.Contains(position))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether the chain has already reached what <paramref name="offset"/>, an offset the file gives, names
    /// (<see cref="ReachedAt"/>).
    /// </summary>
    /// <param name="offset">The offset a <c>/Prev</c> or an <c>/XRefStm</c> gives, not negative.</param>
    /// <param name="named">The offsets the chain has named so far, as the file writes them.</param>
    private bool ReachedBefore(long offset, HashSet<long> named) => ReachedAt(offset, named, out _) >= 0;

    /// <summary>
    /// Gives where the chain has already reached what <paramref name="offset"/>, an offset the file gives, names, or -1 when
    /// it has not: it named that offset before, as written — among <paramref name="named"/>, whether it read a section there
    /// or not —, or a section it reached starts at the position the offset names, or at the first token past the white space
    /// and comments there (<see cref="StartOf"/>) — where that section was named, where it was found moved, or where it was
    /// reached and lost though it declares itself one (<see cref="_sectionsReadAt"/>). The one notion of "already reached"
    /// the chain's checks share, which a section's first token decides — and, for a stream, where the digits of its number
    /// start —, not the offset written for it: a <c>/Prev</c> that names what the chain has reached ends the chain in a
    /// loop, rather than reading a section twice; an <c>/XRefStm</c> that names it is recorded as naming what is there, read
    /// once (#156); and a relocation, which tries positions, passes over those where a section the chain reached starts,
    /// and those where the digits of a stream the chain reached start past the place's own run of signs, as that stream's
    /// reading lexed them — a header found at its digit, wherever its run of signs starts and whatever sign starts it, or
    /// at the first sign of a run a <c>+</c> starts within the bytes searched, where the line its number is on holds a
    /// <c>%</c> (<see cref="DigitsPastSigns"/>, <see cref="_sectionDigitsAt"/>, #188, #327) —, but for what a hybrid
    /// table's <c>/XRefStm</c> read in the stream's place, which that table's <c>/Prev</c> takes (#156).
    /// </summary>
    /// <remarks>
    /// The offsets named are kept as written: one the chain named and found nothing at, or found its section moved from,
    /// leads nowhere new when named again — a loop, not a place to look for a section again. Where the offset names neither,
    /// and lies in the file, the bytes there are read as far as a section's own probe reads them: one window, grown no
    /// further than a trailer's and the few bytes past it that tell what starts at its end, for each offset the chain names,
    /// which <see cref="PdfReaderLimits.MaxXRefSectionCount"/> bounds — the probe the section's own reading there then
    /// answers from, rather than crossing the white space again (<see cref="_paddingProbes"/>).
    /// </remarks>
    /// <param name="offset">The offset a <c>/Prev</c> or an <c>/XRefStm</c> gives, not negative.</param>
    /// <param name="named">The offsets the chain has named so far, as the file writes them.</param>
    /// <param name="start">
    /// Where the first token past the white space and comments at the position the offset names starts, where that was read
    /// — the offset named neither, and lies in the file —; -1 otherwise, and where the white space and comments there run on
    /// past a trailer's window, the guard's, through which the chain reaches nothing (<see cref="PaddingAt"/>).
    /// </param>
    /// <returns>
    /// The position the offset names, where the chain named it or a section starts there; where the section it reaches
    /// starts, past the white space before it; or -1.
    /// </returns>
    private long ReachedAt(long offset, HashSet<long> named, out long start)
    {
        start = -1;
        var position = PositionOf(offset);

        if (named.Contains(offset) || _sectionsReadAt.Contains(position))
        {
            return position;
        }

        if (!IsInFile(position))
        {
            return -1;
        }

        start = StartOf(position, out _);
        return _sectionsReadAt.Contains(start) ? start : -1;
    }

    /// <summary>
    /// Gives where the first token at <paramref name="position"/>, a position in the file, starts: past the white space and
    /// comments there, as a section read there starts (<see cref="PaddingAt"/>), the keyword of a table or the header of a
    /// stream's object; -1 where they run on past a trailer's window, which a section read there reads nothing past, and
    /// the chain reaches nothing through.
    /// </summary>
    /// <param name="position">The position, in the file.</param>
    /// <param name="table">Receives whether the first token is the <c>xref</c> keyword, a classic table's.</param>
    private long StartOf(long position, out bool table)
    {
        var padding = PaddingAt(position, out table);
        return padding < 0 ? -1 : position + padding;
    }

    /// <summary>
    /// Gives where the section <paramref name="offset"/>, a <c>/Prev</c> the file gives, names starts, past the white space
    /// and comments there (<see cref="StartOf"/>), where the <c>/Prev</c>'s own reading, next, comes to it; -1 where it
    /// comes to none — no offset, -1, whose position is -1 too; an offset outside the file; what the chain has reached
    /// (<see cref="ReachedBefore"/>), where the <c>/Prev</c> ends the chain in a loop, the reached only growing until then;
    /// or white space and comments that run on past a trailer's window, past which its reading from the offset comes to no
    /// start (<see cref="PaddingAt"/>), the section not being read from there, though its search near the offset may find
    /// the section past them.
    /// </summary>
    /// <param name="offset">The offset the <c>/Prev</c> gives, as written, or -1.</param>
    /// <param name="named">The offsets the chain has named so far, as the file writes them.</param>
    /// <param name="table">Receives whether what starts there is the <c>xref</c> keyword, a classic table's.</param>
    private long StartOfNamed(long offset, HashSet<long> named, out bool table)
    {
        table = false;
        var position = PositionOf(offset);
        return IsInFile(position) && !ReachedBefore(offset, named) ? StartOf(position, out table) : -1;
    }

    /// <summary>
    /// Tells whether, and why, a <c>/Prev</c> keeps its place at <paramref name="start"/>, where the first token at the offset
    /// it names starts, rather than looking for its section nearby, as the chain reading it there does (#156, #281): it
    /// declares itself a section there — a table's <c>xref</c> keyword (<paramref name="table"/>), or an object whose
    /// dictionary gives <c>/Type /XRef</c>, read or not —, or the object reads there as a section, whole or in part —
    /// whatever it is, so long as it gives a <c>/W</c> —: <see cref="KeptPlace.Section"/>. A guard cut it there — its
    /// dictionary before it said what it is, a <c>/Type</c> that is no name saying nothing, or a value its rows are read
    /// with —: <see cref="KeptPlace.Guard"/>, the guard's to hide. Anything else — an object that gives a <c>/W</c> and
    /// does not read as a section, a font, cut or not, a string, no object — keeps no place: <see cref="KeptPlace.None"/>,
    /// the <c>/Prev</c> looking for its section nearby. Asked only of a <c>/Prev</c> the chain reads next: one that names
    /// what the chain has reached ends the chain in a loop, reading nothing, and keeps no place, nor does one that names
    /// white space and comments running on past a trailer's window, whose reading from the offset comes to no start past
    /// them, the section not being read from there, though its search near the offset may find the section past them.
    /// </summary>
    /// <remarks>
    /// Told without indexing anything, as a hybrid table's <c>/XRefStm</c>, whose search near the offset it names comes to
    /// that place, asks it before the <c>/Prev</c> is read: the object is read as the chain reads one it names as a
    /// cross-reference stream, its rows aside, and its data decoded where it gives no <c>/Type /XRef</c>, a stream of that
    /// type keeping its place whatever its data holds — from its header, through the windows the <c>/Prev</c>'s own reading
    /// opens there, past the white space it falls short by, which a trailer's window bounds apart (<see cref="PaddingAt"/>),
    /// so that the two meet one cut, or none —, and nothing of the reading is kept or reported, but for a guard its decoding
    /// reached, which no later reading reports (<see cref="TryReadXRefStream"/>'s check alone), the <c>/Prev</c>'s own
    /// reading, next, reading it again as itself. At most once for each hybrid table whose <c>/XRefStm</c> cannot be read where it
    /// names, which <see cref="PdfReaderLimits.MaxXRefSectionCount"/> bounds: one decoding more of the stream, within
    /// <see cref="PdfReaderLimits.MaxDecodedStreamLength"/>, where the object gives no <c>/Type /XRef</c> and reads that
    /// far.
    /// </remarks>
    /// <param name="start">Where the first token at the offset the <c>/Prev</c> names starts, in the file.</param>
    /// <param name="table">Whether that token is the <c>xref</c> keyword.</param>
    private KeptPlace KeepsItsPlace(long start, bool table)
    {
        if (table)
        {
            return KeptPlace.Section;
        }

        // What a value of the object leads to through a guard's cut is forgotten with the check, as with a place a section may
        // have been moved to that is dropped: the /Prev's reading reads it anew.
        var check = new XRefSectionRecord("/Prev", start, start);
        var readThrough = _chainValuesCut?.Count ?? 0;
        var state = TryReadXRefStream(start, check, out _, candidate: false, checkOnly: true);
        ForgetReadThrough(readThrough);

        return state == XRefSectionState.Read || check.TypedXRef ? KeptPlace.Section
            : check.CutByLimit ? KeptPlace.Guard
            : KeptPlace.None;
    }

    /// <summary>Whether, and why, a <c>/Prev</c> keeps its place where it names (<see cref="KeepsItsPlace"/>).</summary>
    private enum KeptPlace
    {
        /// <summary>It keeps none: it looks for its section nearby, and what it names is a place like any other.</summary>
        None,

        /// <summary>It reads a section there, or one there declares itself a section.</summary>
        Section,

        /// <summary>A guard cut what is there before the reading could tell: the guard's to hide.</summary>
        Guard,
    }

    /// <summary>
    /// Parses what starts at <paramref name="offset"/> as <see cref="TryParseNumberedAt"/> reads an object the chain names as
    /// a cross-reference stream — any object, through the windows a trailer's is read through, grown eightfold no further
    /// than <see cref="PdfReaderLimits.MaxTrailerLength"/>, its stream's data, which counts toward none, served from the
    /// file —, but keeps nothing of the reading: what the parser notices is dropped, no end of an object or length is noted,
    /// and the guard its window reaches is neither reported nor counted, which <paramref name="cut"/> says. The search for a
    /// stream's <c>endstream</c> is kept, as every search is (<see cref="IPdfStreamDataProvider.FindEndStream"/>).
    /// </summary>
    /// <param name="offset">Where the object starts in the file.</param>
    /// <param name="value">The object read, as far as the window let it be.</param>
    /// <param name="foundNumber">The number of the object found.</param>
    /// <param name="headerAt">Where in the file the object read starts, past the white space and comments before it.</param>
    /// <param name="digitsAt">
    /// Where in the file the digits of the object's number start, past the run of signs it is written with, where it is
    /// (<see cref="PdfObjectParser.HeaderDigits"/>); <paramref name="headerAt"/> otherwise.
    /// </param>
    /// <param name="cut">
    /// Whether the guard's window cut what was read, where <see cref="TryParseNumberedAt"/> reaches the guard.
    /// </param>
    /// <returns>Whether an object starts there, whole or cut.</returns>
    private bool TryParseUnrecordedAt(
        long offset, out PdfObject value, out int foundNumber, out long headerAt, out long digitsAt, out bool cut)
    {
        value = PdfNull.Instance;
        foundNumber = 0;
        headerAt = offset;
        digitsAt = offset;
        cut = false;

        if (offset < 0 || offset >= _source.Length)
        {
            return false;
        }

        var maxWindow = _guard.Bound(PdfLimit.Trailer);
        var windowSize = Math.Min(XRefWindow, maxWindow);
        var mark = _pending.GetMark();

        try
        {
            while (true)
            {
                using var window = _source.GetWindow(offset, windowSize);
                var edge = window.Length == windowSize && offset + window.Length < _source.Length;
                var parser = new PdfObjectParser(window.Memory, offset, this, _pending, this, endsData: !edge);
                var read = parser.TryReadIndirectObject(out var found, out var parsed);

                // What a window's edge may have cut is read again in a larger one, as far as the guard allows: past it, a
                // header it cut is no object, and an object it cut is kept as far as it was read, as TryParseNumberedAt
                // keeps it.
                if (parser.IsTruncated && edge && windowSize < maxWindow)
                {
                    _pending.RollBack(mark);
                    windowSize = (int)Math.Min((long)windowSize * 8, maxWindow);
                    continue;
                }

                if (!read)
                {
                    return false;
                }

                cut = parser.IsTruncated && edge;

                if (cut)
                {
                    MarkCutByGuard(parsed, offset + window.Length);
                }

                value = parsed;
                foundNumber = found.Number;
                headerAt = offset + parser.HeaderStart;
                digitsAt = offset + parser.HeaderDigits;
                return true;
            }
        }
        finally
        {
            _pending.RollBack(mark);
        }
    }

    /// <summary>
    /// Records where <paramref name="section"/> starts: its <c>xref</c> keyword or its stream's object header, past the white
    /// space the offset that names it fell short by (<see cref="_sectionsReadAt"/>), and where the digits of a stream's
    /// object number start (<see cref="_sectionDigitsAt"/>) — and, in <paramref name="sectionsAt"/>, that the section is
    /// what is there, unless what was first named there is (#156). A section the chain read, where it was named or where it
    /// was found moved; or one it reached and could not read, which declares itself a section, and is reported once where
    /// it is: a cross-reference stream whose <c>/Prev</c> the chain goes on through (#208), or a section an
    /// <c>/XRefStm</c> names.
    /// </summary>
    /// <remarks>
    /// Each such section is a classic table, which starts at its keyword, or a stream whose header was read, its object
    /// number lexed (<see cref="XRefSectionRecord.DigitsAt"/>): a section lost before its header was read declares nothing,
    /// and is not recorded. So a section recorded that starts at a sign of the run of signs a number is written with is a
    /// stream whose header was read from that sign, the digits past the run recorded with it (#327).
    /// </remarks>
    private void RecordSectionReached(XRefSectionRecord section, Dictionary<long, XRefSectionRecord> sectionsAt)
    {
        var start = section.Offset + section.Padding;
        _sectionsReadAt.Add(start);
        _sectionDigitsAt.Add(section.DigitsAt);
        sectionsAt.TryAdd(start, section);
    }

    /// <summary>
    /// Records that <paramref name="table"/>'s <c>/XRefStm</c>, which names <paramref name="offset"/>, reaches the section the
    /// table's <c>/Prev</c> names too, and reports it (#156): read as the table's stream, that section would have its own
    /// links left unread, and the <c>/Prev</c> would loop back to it. The <c>/Prev</c> reads it next, whole, and the table's
    /// own stream is unknown — or, past a guard that stopped the table, reads nothing in the file, which the guard hides
    /// (#264).
    /// </summary>
    /// <remarks>
    /// The report comes before the <c>/Prev</c> reads what is there, and says no more than is known: an offset outside the
    /// file, which holds no section; one past the guard; or what the <c>/Prev</c> names, which the chain looks for, and which
    /// may hold no section, or one that cannot be read. The validation rules say what the <c>/Prev</c> read there, once the
    /// chain is read (<see cref="XRefSectionRecord.NamesWhatPreviousReads"/>).
    /// </remarks>
    /// <param name="offset">The offset the <c>/XRefStm</c> gives, as written.</param>
    /// <param name="table">The record of the table whose trailer gives it.</param>
    /// <param name="pastGuard">Whether a guard stopped the table, its <c>/Prev</c> judged and not read.</param>
    /// <param name="shortBy">
    /// The bytes of white space and comments between where the <c>/XRefStm</c> names and that section's first token, where
    /// it names another offset than the <c>/Prev</c> does (<see cref="XRefSectionRecord.Padding"/>).
    /// </param>
    /// <returns>The record of the naming, whose <see cref="XRefSectionRecord.ReadAs"/> the <c>/Prev</c>'s reading sets.</returns>
    private XRefSectionRecord LeaveToPrevious(long offset, XRefSectionRecord table, bool pastGuard, int shortBy)
    {
        var named = NameAgain(offset, table, readAs: null, shortBy);

        // What the /Prev names is not read yet: an offset outside the file holds nothing, one past a guard that stopped the
        // table is the guard's to hide, and what any other holds is known once the /Prev reads it, which the validation
        // rules say (XRefSectionRecord.NamesWhatPreviousReads).
        var inFile = IsInFile(named.NamedOffset);
        named.NamesWhatPreviousReads = inFile && !pastGuard;
        ReportNoStream(
            named,
            !inFile ? "outside the file, which its /Prev names too"
            : pastGuard ? "the section its /Prev names too, past the guard that stopped the table"
            : "what its /Prev names too, which the chain looks for through that /Prev");
        return named;
    }

    /// <summary>
    /// Records that the <c>/XRefStm</c> whose reading in the stream's place <paramref name="place"/> records reached the
    /// section its own table's <c>/Prev</c> read next, <paramref name="previous"/>, whose search near the offset it names came
    /// to that section (#156): as where the <c>/XRefStm</c> reaches what the <c>/Prev</c> names, the <c>/Prev</c> reads it
    /// whole, its links with it, and the <c>/XRefStm</c> names no stream of the table's revision. Its record becomes the
    /// naming of what the <c>/Prev</c> read, in the chain's order, and what the chain reached there leads to that
    /// <c>/Prev</c>'s record.
    /// </summary>
    /// <remarks>
    /// A classic table so read was reported as one whose rows were read in the stream's place, which stays true; a
    /// cross-reference stream was reported as nothing, and is reported now, once the <c>/Prev</c> has read it. The validation
    /// rules say what the <c>/Prev</c> read (<see cref="XRefSectionRecord.NamesWhatPreviousReads"/>).
    /// </remarks>
    /// <param name="place">The record of the <c>/XRefStm</c>'s reading, a table or a stream read where it named, or found moved.</param>
    /// <param name="previous">The record of the <c>/Prev</c>, which read the section where it found it.</param>
    /// <param name="sectionsAt">What the chain first named, or read, at each position it reached.</param>
    private void LeaveReadingToPrevious(XRefSectionRecord place, XRefSectionRecord previous, Dictionary<long, XRefSectionRecord> sectionsAt)
    {
        // The white space the /XRefStm crossed to the section falls short of it, as a section read there does; a section it
        // found moved lies past what it crossed.
        var named = new XRefSectionRecord("/XRefStm", place.NamedOffset, place.NamedFrom)
        {
            WrittenOffset = place.WrittenOffset,
            NamingTrailer = place.NamingTrailer,
            State = XRefSectionState.NamedAgain,
            ReadAs = previous,
            Padding = place.WasRelocated ? 0 : place.Padding,
            NamesWhatPreviousReads = true,
            NamesNoStream = place.NamesNoStream,
        };
        _structure.Replace(place, named);
        sectionsAt[place.NamedOffset] = previous;
        sectionsAt[place.Offset + place.Padding] = previous;

        if (place.Kind == XRefSectionKind.Stream)
        {
            ReportNoStream(named, "the section its /Prev names too, which the chain read through that /Prev");
        }
    }

    /// <summary>
    /// Records that <paramref name="table"/>'s <c>/XRefStm</c> names <paramref name="offset"/>, which the chain has reached
    /// before, where it read, or looked for, <paramref name="readAs"/> — a hybrid file's stream a newer table named too, as a
    /// writer that copies <c>/XRefStm</c> into the next trailer leaves it, or what no such stream is —, or that the table's
    /// <c>/Prev</c> names too, which reads it next, but past a guard that stopped the table (#156, #264). Nothing is read:
    /// what is there was read once, where it was first reached.
    /// </summary>
    /// <remarks>One record for each table the chain reads, at most: as many as the sections the chain may read.</remarks>
    /// <param name="offset">The offset the <c>/XRefStm</c> gives, as written.</param>
    /// <param name="table">The record of the table whose trailer gives it.</param>
    /// <param name="readAs">The record of what the chain read, or looked for, there; null while that is unknown.</param>
    /// <param name="shortBy">
    /// The bytes of white space and comments between where the offset names and the first token of the section it reaches
    /// (<see cref="XRefSectionRecord.Padding"/>): none where it names that token, or the offset the chain reached it by.
    /// </param>
    private XRefSectionRecord NameAgain(long offset, XRefSectionRecord table, XRefSectionRecord? readAs, int shortBy)
    {
        var again = new XRefSectionRecord("/XRefStm", PositionOf(offset), table.Offset)
        {
            WrittenOffset = offset,
            NamingTrailer = table.Trailer,
            State = XRefSectionState.NamedAgain,
            ReadAs = readAs,
            Padding = shortBy,
        };
        _structure.Add(again);
        return again;
    }

    /// <summary>
    /// Reports that the <c>/XRefStm</c> <paramref name="naming"/> records names <paramref name="what"/>, no cross-reference
    /// stream of its table's revision, and marks the index incomplete: the objects only that stream indexes are looked for by
    /// rebuilding the index when they are asked for, as a missing section's are (#156).
    /// </summary>
    /// <remarks>
    /// Reported at the offset the <c>/XRefStm</c> names, or at its table when that offset lies outside the file, as a missing
    /// section is.
    /// </remarks>
    /// <param name="naming">The record of the <c>/XRefStm</c>, its table at <see cref="XRefSectionRecord.NamedFrom"/>.</param>
    /// <param name="what">What the offset holds, in words: "the table itself".</param>
    private void ReportNoStream(XRefSectionRecord naming, string what)
    {
        naming.NamesNoStream = what;
        _indexIncomplete = true;
        _diagnostics.Warn(
            PdfDiagnosticCodes.XRefHybridStreamLost,
            string.Create(
                CultureInfo.InvariantCulture,
                $"The /XRefStm of the cross-reference table at offset {naming.NamedFrom} names offset {DescribeOffset(naming.WrittenOffset)}, {what}: ISO 32000-1 has it name the cross-reference stream of the table's revision (Table 19). The objects only that stream indexes are looked for by rebuilding the index."),
            IsInFile(naming.NamedOffset) ? naming.NamedOffset : naming.NamedFrom);
    }

    /// <summary>
    /// Determines whether a section the chain could not read declares itself one all the same: a classic table's
    /// <c>xref</c> keyword, or a stream whose dictionary gives <c>/Type /XRef</c>. Such a section is reported where it is,
    /// and not looked for nearby: the offset names it, and any section near it is another — a newer one the chain has
    /// read, or an older one whose rows would stand for the lost ones (#281). An object that only gives a <c>/W</c> may be
    /// anything — a CIDFont's <c>/W</c> gives its glyphs' widths — and leaves the section a guess, looked for nearby
    /// (#263).
    /// </summary>
    /// <remarks>
    /// Asked only of a section the chain could not read, whose state is then <see cref="XRefSectionState.Malformed"/>: a
    /// table is one from its keyword on, and a stream gives <c>/Type /XRef</c> only once its object reads as one.
    /// </remarks>
    private static bool DeclaresItselfASection(XRefSectionRecord section) =>
        section.Kind == XRefSectionKind.Table || section.TypedXRef;

    /// <summary>
    /// Gives the dictionary of a cross-reference stream the chain could not read that names an older section all the same:
    /// one whose dictionary declares the numbers its rows give (<see cref="DeclaredByLostStream"/>) and gives a
    /// <c>/Prev</c> (#208). Null for any other, and for a stream that names no older section.
    /// </summary>
    /// <remarks>
    /// Asked only of a section the chain named through <c>/Prev</c> and could not read: <c>startxref</c>'s is the whole
    /// file's to rebuild, and a hybrid file's stream gives a <c>/Prev</c> no reader follows.
    /// </remarks>
    private static PdfDictionary? PreviousToHandOn(XRefSectionRecord section) =>
        DeclaredByLostStream(section) is { } dictionary && dictionary.GetRaw(PdfName.Prev) is not null ? dictionary : null;

    /// <summary>
    /// Gives the dictionary of a cross-reference stream the chain could not read, whose rows alone are lost: one that
    /// declares itself a section, <c>/Type /XRef</c> (<see cref="DeclaresItselfASection"/>), whose dictionary was read whole,
    /// where the fault is in its rows or in a value they are read with — the numbers it declares, which its rows would have
    /// given, are then known (#208, #210). Null for any other: a table, whose subsections are unknown past a fault among
    /// them; a stream no guard let be read whole, which is the guard's (ADR 34); an object that gives only a <c>/W</c>, which
    /// may be anything (#263); and a dictionary no stream data follows, which is no stream's.
    /// </summary>
    private static PdfDictionary? DeclaredByLostStream(XRefSectionRecord section) =>
        section is { TypedXRef: true, CutByLimit: false, Trailer: { } dictionary } ? dictionary : null;

    /// <summary>
    /// Goes on past cross-reference stream <paramref name="section"/>, which the chain could not read, through the
    /// <c>/Prev</c> its <paramref name="dictionary"/> gives (#208), and gives the offset that <c>/Prev</c> names, or -1 when
    /// it names none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The numbers the stream declares are refused to the older sections' rows first (<see cref="RefuseDeclared"/>): its
    /// rows, lost, may give a newer revision of any of them, which an older row would stand for in silence, and the object
    /// is looked for by rebuilding the index instead, which finds the last definition the file writes. Its <c>/Prev</c> is
    /// read as a whole read reads it (<see cref="ReadSectionOffset"/>), and the section is then reported once, as one that is
    /// there and cannot be read, the index left incomplete: where the chain goes on through that <c>/Prev</c> — it names an
    /// offset, or leads to what a guard hides —, the report says so; where it is no offset, the report after it says so
    /// (<see cref="ReportNotAnOffset"/>), and the chain ends there. The section the <c>/Prev</c> names is read under the
    /// checks any other is: what the chain has already reached (<see cref="ReachedBefore"/>),
    /// <see cref="PdfReaderLimits.MaxXRefSectionCount"/>, and the search near an offset that holds no section.
    /// </para>
    /// <para>
    /// The <c>/Prev</c> is all that is read of the dictionary: it is not merged into the document's trailer, whose keys the
    /// sections read give, and no other of its values names a section.
    /// </para>
    /// </remarks>
    /// <param name="section">
    /// The record of the stream, which the chain named at <paramref name="offset"/>, or found moved near it.
    /// </param>
    /// <param name="dictionary">The stream's dictionary, read whole.</param>
    /// <param name="naming">What named the stream: <c>/Prev</c>.</param>
    /// <param name="offset">The offset that named the stream, as written.</param>
    /// <param name="namedFrom">Where what named the stream is written.</param>
    private long HandOnPrevious(XRefSectionRecord section, PdfDictionary dictionary, string naming, long offset, long namedFrom)
    {
        RefuseDeclared(dictionary);
        section.NumbersRefused = true;
        section.PreviousHandedOn = true;
        var previous = ReadSectionOffset(dictionary, PdfName.Prev, out var hidden, out var notAnOffset);
        section.ChainHiddenByGuard = hidden;
        ReportLostSection(section, naming, offset, namedFrom, goesOn: notAnOffset is null);

        if (notAnOffset is not null)
        {
            ReportNotAnOffset(PdfName.Prev, notAnOffset, section.Offset);
        }

        return previous;
    }

    /// <summary>
    /// Refuses to the rows of every section the chain reads from here on, older revisions, the object numbers cross-reference
    /// stream <paramref name="dictionary"/> declares, whose rows were lost, all or some: the subsections its <c>/Index</c>
    /// gives, or 0 up to its <c>/Size</c> without one, as its rows would number them — a stream the chain goes on past
    /// through its <c>/Prev</c> (#208), an unreadable hybrid file's stream, or one read only in part (#210), whose rows read
    /// are indexed already. What an older row gives for such a number is an older revision of the object than the stream's
    /// row would have given, and the object is left for a rebuild to find, as one a row refused is (<see cref="Refuse"/>).
    /// </summary>
    /// <remarks>
    /// Where the numbering cannot be read, every number is refused, and what the sections the chain read before do not index
    /// is looked for by rebuilding the index: an <c>/Index</c> or a <c>/Size</c> <see cref="NumberingFault"/> finds wrong, or
    /// one written as a reference, or holding one. ISO 32000-1 makes those values direct (7.5.8.2); the reader follows such
    /// a reference where it lets a section's rows be read, as other readers do, but here rows were lost, and which older
    /// rows stand is not left to what an object a section placed holds.
    /// </remarks>
    private void RefuseDeclared(PdfDictionary dictionary)
    {
        if (_everyNumberRefused)
        {
            return;
        }

        // An /Index that is null is one the dictionary does not have (7.3.7): the parse leaves the key out.
        var index = dictionary.GetRaw(PdfName.Index);
        var size = index is null ? dictionary.GetRaw(PdfName.Size) : null;

        // An /Index written as a reference is no array to NumberingFault.
        if (size is PdfReference || HoldsReference(index) || NumberingFault(index, index, size?.AsInteger(), out var subsections) is not null)
        {
            RefuseEveryNumber();
            return;
        }

        // Without an /Index, the stream numbers its rows from 0 to its /Size less one, as one subsection.
        if (index is null)
        {
            subsections = [0, size!.AsInteger().GetValueOrDefault()];
        }

        var declared = ArrayPool<(long First, long End)>.Shared.Rent(subsections.Length / 2);

        try
        {
            // Each first number and count is within the object numbers, as NumberingFault checked, and so is their sum. A
            // subsection of no rows declares no number.
            var added = 0;

            for (var i = 0; i < subsections.Length; i += 2)
            {
                if (subsections[i + 1] > 0)
                {
                    declared[added++] = (subsections[i], subsections[i] + subsections[i + 1]);
                }
            }

            RefuseRanges(declared.AsSpan(0, added));
        }
        finally
        {
            ArrayPool<(long First, long End)>.Shared.Return(declared);
        }
    }

    /// <summary>
    /// Refuses to the rows of every section the chain reads from here on, older revisions, the numbers of
    /// <paramref name="ranges"/>, each from a first number up to the one past the last, which a section lost (#208, #210).
    /// </summary>
    /// <remarks>
    /// The ranges are kept sorted, those that meet joined, and an older row's number is looked for among them by halves
    /// (<see cref="RefusedToOlderRows"/>). Each section's are sorted alone, in place, then merged with those kept in one pass
    /// over both, so that a refusal costs the section's own ranges and at most <see cref="MaxRefusedRanges"/> more; past that
    /// many ranges, every number is refused, and nothing is merged again. The arrays come from the shared pool, and go back
    /// to it once read.
    /// </remarks>
    private void RefuseRanges(Span<(long First, long End)> ranges)
    {
        if (_everyNumberRefused)
        {
            return;
        }

        ranges.Sort();
        var merged = ArrayPool<(long First, long End)>.Shared.Rent(Math.Min(_refusedRangeCount + ranges.Length, MaxRefusedRanges));
        var refused = _refusedRanges.AsSpan(0, _refusedRangeCount);
        var count = 0;
        var kept = 0;
        var added = 0;
        var past = false;

        // One pass over both, in the order of their first numbers: a range that meets the last one merged joins it.
        while (kept < refused.Length || added < ranges.Length)
        {
            var next = added == ranges.Length || (kept < refused.Length && refused[kept].First <= ranges[added].First)
                ? refused[kept++]
                : ranges[added++];

            if (count > 0 && next.First <= merged[count - 1].End)
            {
                merged[count - 1].End = Math.Max(merged[count - 1].End, next.End);
            }
            else if (count < MaxRefusedRanges)
            {
                merged[count++] = next;
            }
            else
            {
                past = true;
                break;
            }
        }

        ReleaseRefusedRanges();
        _refusedRanges = merged;
        _refusedRangeCount = count;

        if (past)
        {
            RefuseEveryNumber();
        }
    }

    /// <summary>
    /// Refuses every object number to the rows of the sections the chain reads from here on, and lets go of the ranges
    /// refused so far, which every number includes (<see cref="RefuseDeclared"/>).
    /// </summary>
    private void RefuseEveryNumber()
    {
        _everyNumberRefused = true;
        ReleaseRefusedRanges();
    }

    /// <summary>Gives the ranges of refused numbers back to the shared pool, and refuses none of them any more.</summary>
    private void ReleaseRefusedRanges()
    {
        if (_refusedRanges is { } ranges)
        {
            ArrayPool<(long First, long End)>.Shared.Return(ranges);
            _refusedRanges = null;
        }

        _refusedRangeCount = 0;
    }

    /// <summary>
    /// Determines whether a row of a section the chain reads now, older than every cross-reference stream it went on past,
    /// may not stand for object <paramref name="number"/>: a stream it went on past, its rows lost, declares it, or numbers
    /// its rows in a way that cannot be read (<see cref="RefuseDeclared"/>). A lookup by halves among the ranges refused,
    /// allocating nothing: it is asked of every row the chain reads.
    /// </summary>
    private bool RefusedToOlderRows(int number)
    {
        if (_everyNumberRefused)
        {
            return true;
        }

        var ranges = _refusedRanges;
        var low = 0;
        var high = _refusedRangeCount - 1;

        while (low <= high)
        {
            var middle = (low + high) >>> 1;

            if (number < ranges![middle].First)
            {
                high = middle - 1;
            }
            else if (number >= ranges[middle].End)
            {
                low = middle + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Determines whether <paramref name="value"/> is an array one element of which is written as a reference.</summary>
    private static bool HoldsReference(PdfObject? value)
    {
        if (value is PdfArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                if (array[i] is PdfReference)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Reports a section of the chain that could not be read where it was named, nor found near it, and marks the
    /// index incomplete: as one that is there and cannot be read when the named offset holds a section, as a missing
    /// one otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A section one of the reader's guards cut is already reported, under the guard's own code, which names the limit
    /// that lifts it: what the reader met past the cut is not the file's fault (ADR 34).
    /// </para>
    /// <para>
    /// An object whose dictionary gives a <c>/W</c> and a <c>/Type</c> that names another type holds no section, and the
    /// section is missing (<see cref="NoSectionWhereAnotherType"/>).
    /// </para>
    /// <para>
    /// A stream whose numbers were refused (<see cref="RefuseDeclared"/>) says what becomes of the objects it numbers,
    /// which no older section's row stands for: they are looked for by rebuilding the index, where no section read before it
    /// indexes them — every object, where the numbers it declares could not be kept apart. One the chain goes on past through
    /// its <c>/Prev</c> (<see cref="HandOnPrevious"/>) says so too, where that <c>/Prev</c> names an offset, or leads to what
    /// a guard hides (#208); a hybrid file's stream leaves the chain to its table's (#210). So does a classic table an
    /// <c>/XRefStm</c> names, whose lost rows were refused (<see cref="RefuseRowsLost"/>): the rest of the subsection its fault
    /// ended, or every number but those its rows read before the fault give (#156).
    /// </para>
    /// <para>
    /// The rows read before the fault are said kept but for those the section refused itself and those whose numbers a newer
    /// section refused, which stand for nothing (<see cref="KeptButFor"/>); where a newer section refused every number before
    /// the chain came to the section, none of them stands, and every object the sections read before do not index is looked
    /// for by rebuilding the index (<see cref="XRefSectionRecord.EveryNumberRefusedBefore"/>). What the section refuses once
    /// its rows are read, before it is reported, is told apart (#210).
    /// </para>
    /// <para>
    /// A section found moved is named by both offsets: the one that names it and where it was found
    /// (<see cref="SectionNamed"/>).
    /// </para>
    /// </remarks>
    /// <param name="section">The record of the section, read where it was named or found moved.</param>
    /// <param name="naming">What named the section: <c>/Prev</c> or <c>/XRefStm</c>.</param>
    /// <param name="offset">The offset that named the section, as written.</param>
    /// <param name="namedFrom">Where what named the section is written.</param>
    /// <param name="goesOn">
    /// Whether the chain goes on past the section through the <c>/Prev</c> its dictionary gives, which names an offset
    /// (<see cref="HandOnPrevious"/>).
    /// </param>
    private void ReportLostSection(XRefSectionRecord section, string naming, long offset, long namedFrom, bool goesOn = false)
    {
        NoSectionWhereAnotherType(section);

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

        // A stream whose numbers were refused says what becomes of them, and one whose /Prev the chain goes on through says
        // so (#208, #210). The rows read before the fault stand but for those the section refused and the numbers a newer
        // section refused: none, where it refused every one before the chain came to the section; a stream whose numbers
        // were refused keeps none anyway.
        var kept = "The rows read before the fault, if any, were kept" + KeptButFor(section);
        string lost;

        if (section.EveryNumberRefusedBefore && (!section.NumbersRefused || section.Kind == XRefSectionKind.Table))
        {
            lost = "None of the rows read before the fault, if any, stands, a newer section having refused every number to the older sections' rows; every object no section read before it indexes is looked for by rebuilding the index.";
        }
        else if (!section.NumbersRefused)
        {
            lost = kept + "; the objects only the rest indexes are looked for by rebuilding the index.";
        }
        else if (section.Kind == XRefSectionKind.Table)
        {
            // Every number refused is the table's own here, what follows its fault unknown (#156).
            lost = kept + (_everyNumberRefused
                ? "; every other object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows."
                : "; the objects the rest of that subsection numbers are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows.");
        }
        else if (goesOn)
        {
            lost = _everyNumberRefused
                ? "The chain goes on through its /Prev; every object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows."
                : "The chain goes on through its /Prev; the objects it numbers that no section read before it indexes are looked for by rebuilding the index, rather than taken from an older section's rows.";
        }
        else
        {
            lost = _everyNumberRefused
                ? "Every object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows."
                : "The objects it numbers that no section read before it indexes are looked for by rebuilding the index, rather than taken from an older section's rows.";
        }

        _diagnostics.Warn(
            PdfDiagnosticCodes.XRefSectionUnreadable,
            string.Create(CultureInfo.InvariantCulture, $"{SectionNamed(section, naming)} is there but cannot be read: {fault}. {lost}"),
            section.Offset);
    }

    /// <summary>
    /// Takes <paramref name="section"/>, which the chain could not read, for no section where what is there gives a
    /// <c>/W</c> and a <c>/Type</c> that names another type — a CIDFont, whose <c>/W</c> gives its glyphs' widths — and no
    /// guard cut it: the section is not found, its fault that the offset holds an object that is not a cross-reference
    /// stream, and what it held is no trailer either, which the validation rules judge none of (#281).
    /// </summary>
    /// <remarks>
    /// Such an object is read as the section where the chain names it, as #263 settled, and is the section when it reads as
    /// one. When it does not, it holds no section, as at a place the section may have been moved to, where such an object is
    /// no section at all, rather than a cross-reference stream that cannot be read: where <c>startxref</c> names it too, which
    /// <see cref="Validation.PdfValidationRuleIds.FileStartXRefWrong"/> then judges. One that gives no <c>/Type</c> stays a
    /// cross-reference stream that cannot be read, as untyped streams are read as sections; one a guard cut a value of is the
    /// guard's (ADR 34).
    /// </remarks>
    private static void NoSectionWhereAnotherType(XRefSectionRecord section)
    {
        if (section.TypedOther && !section.CutByLimit)
        {
            section.State = XRefSectionState.NotFound;
            section.Fault = string.Create(
                CultureInfo.InvariantCulture, $"holds object {section.StreamObjectNumber}, which is not a cross-reference stream");
            section.Trailer = null;
            section.TrailerFault = XRefTrailerFault.None;
        }
    }

    /// <summary>
    /// Gives the words the reports on <paramref name="section"/>, a section of the chain the chain kept or lost, open with:
    /// the cross-reference table or stream <paramref name="naming"/> names at an offset, and, for one found moved, where it
    /// was found, so that a report agrees with the <see cref="PdfDiagnosticCodes.XRefOffsetAdjusted"/> before it, which names
    /// both offsets too.
    /// </summary>
    /// <param name="section">The record of the section.</param>
    /// <param name="naming">What named the section: <c>startxref</c>, <c>/Prev</c> or <c>/XRefStm</c>.</param>
    private static string SectionNamed(XRefSectionRecord section, string naming)
    {
        var kind = section.Kind == XRefSectionKind.Stream ? "stream" : "table";
        return section.WasRelocated
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"The cross-reference {kind} {naming} names at offset {section.NamedOffset}, found at offset {section.Offset},")
            : string.Create(CultureInfo.InvariantCulture, $"The cross-reference {kind} {naming} names at offset {section.Offset}");
    }

    /// <summary>
    /// Reports a section of the chain kept though read only in part — a row that is no row ended a subsection of a table,
    /// a stream holds fewer rows than it declares, the end of the file cuts a table before its trailer, a row was refused —,
    /// and marks the index incomplete; or reports a classic table that holds no subsection, read as indexing nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A section read only in part is reported once, at the section, under
    /// <see cref="PdfDiagnosticCodes.XRefSectionUnreadable"/> — it cannot be read whole —, one fault in the message: the
    /// first that lost rows, else the end of the file before its trailer, else the first row it refused (#207, #210). The
    /// section <c>startxref</c> names is reported so too, and what it lost is looked for by rebuilding the index when it is
    /// asked for, not at opening. What the reading met before a guard's cut is the file's, whatever the guard also did; a
    /// section a guard cut that lost no row is the guard's alone, which says so (ADR 34), and the message says how far a
    /// guard let the rows after a refused one be read, and that a stream whose rows a guard left unread kept none.
    /// </para>
    /// <para>
    /// The numbers whose rows were lost are refused as a lost stream's are, once what the section names is judged
    /// (<see cref="RefuseRowsLost"/>): a newer revision of an object may lie in a lost row, which an older section's row
    /// would stand for in silence. The section is reported once they are, and the message says what that refusal leaves to a
    /// rebuild: every object the sections read before do not index, where it refused every number — a stream whose numbering
    /// is written with references, which ISO 32000-1 makes direct, or ranges lost past <see cref="MaxRefusedRanges"/>, the
    /// section's own or once joined with those refused before (#210) —; but a table <c>startxref</c> or a <c>/Prev</c> names
    /// whose rest cannot be known, at which the chain ends, says, however many ranges it lost, that what only the rest of
    /// the table, or the sections its trailer would name, index is looked for: the same objects, no section past it being
    /// read — unless a newer section refused every number before the chain came to it, which only a <c>/Prev</c> can name
    /// and whose message says so instead (<see cref="XRefSectionRecord.EveryNumberRefusedBefore"/>, below). A table the end
    /// of the file cuts loses its trailer too, and with it the older sections that trailer would name: the chain ends there,
    /// where <c>startxref</c> or a <c>/Prev</c> names it. One an <c>/XRefStm</c> names, whose trailer's links are not
    /// followed either (#263), leaves the chain going on through the table that names it, and what the rest of it held
    /// cannot be known: every number but those its rows give is refused (#156). The message agrees with the count of rows a
    /// stream holds: the row it holds, or none (#241). A table whose fault is followed by the end of the file loses its
    /// trailer as one the end of the file cuts does, which the message says.
    /// </para>
    /// <para>
    /// The rows read are said kept but for those the section refused itself and those whose numbers a newer section refused,
    /// which stand for nothing (<see cref="KeptButFor"/>): a row the section refused is not named beside a fault that lost
    /// rows, but is not said to be kept either, and a stream's one row it refused is said to be refused. Where a newer
    /// section refused every number before the chain came to the section, none of them stands, and every object the sections
    /// read before do not index is looked for by rebuilding the index
    /// (<see cref="XRefSectionRecord.EveryNumberRefusedBefore"/>), whatever the section lost (#208, #210). A row the section
    /// refused itself is then not said to be the one row that does not stand. A row the section refused alone, its other rows
    /// read, leaves its object to a rebuild where no row read before it indexes it: a newer section's, or the section's own
    /// for the same number, which gives it twice.
    /// </para>
    /// <para>
    /// A classic table whose <c>trailer</c> keyword follows its <c>xref</c> keyword indexes nothing, as one whose one
    /// subsection is <c>0 0</c> does, which ISO 32000-1 allows: it is read as written and reported, the index left whole, and
    /// nothing rebuilt for it (#222).
    /// </para>
    /// <para>
    /// A section found moved is named by both offsets, the one that names it and where it was found
    /// (<see cref="SectionNamed"/>).
    /// </para>
    /// </remarks>
    /// <param name="section">The section the chain kept, read where it was named or found moved.</param>
    /// <param name="naming">What named the section: <c>startxref</c>, <c>/Prev</c> or <c>/XRefStm</c>.</param>
    private void ReportSectionReadInPart(XRefSectionRecord section, string naming)
    {
        string said;

        // Where a newer section refused every number before the chain came to the section, none of its rows stands, and
        // every object the sections read before do not index is the rebuild's; where it refused some, the rows read stand but
        // for those (#208, #210).
        const string EveryNumberRefused =
            "a newer section having refused every number to the older sections' rows, and every object no section read before it indexes is looked for by rebuilding the index";
        var noneStands = section.EveryNumberRefusedBefore;
        var someRefused = section.RowsRefusedBefore;

        // The section is reported once it refused what it lost, and every number may be refused then: by a newer section
        // before the chain came to it, which the arms below tell first, or by the section's own refusal — a stream whose
        // numbering is written with references, which ISO 32000-1 makes direct; more ranges lost than are kept apart, its own
        // or once joined with those refused before; a table whose rest cannot be known. Every object the sections read before
        // do not index is then the rebuild's, not only those whose rows it lost: every other one, beside the rows it kept
        // (#210).
        var everyRefused = _everyNumberRefused;
        const string EveryObject =
            "every object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows";
        const string EveryOtherObject =
            ", and every other object no section read before it indexes is looked for by rebuilding the index, rather than taken from an older section's rows";

        if (section.Incomplete && section.Kind == XRefSectionKind.Stream)
        {
            // Rows a guard left unread are none the stream kept, whoever refused every number; of those it holds, the clause
            // agrees with their count, and one it refused itself, whose object no older section's row stands for either, is
            // not said to be kept.
            const string Lacking =
                ", and the objects whose rows it lacks are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows";
            const string Numbered =
                "the objects it numbers are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows";
            var keptAndLacking = everyRefused ? EveryOtherObject : Lacking;
            said = section.Fault + (section.RowsLeftToGuard
                ? "; the rows its data may hold were not read, a value they are read with having been cut by one of the reader's limits, and " + (everyRefused ? EveryObject : Numbered)
                : section.RowsHeld switch
                {
                    0 when noneStands => "; every object no section read before it indexes is looked for by rebuilding the index, a newer section having refused every number to the older sections' rows",
                    0 when everyRefused => "; " + EveryObject,
                    0 => "; the objects whose rows it lacks are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows",
                    1 when noneStands => "; the row it holds does not stand, " + EveryNumberRefused,
                    1 when section.RefusedRow is not null => "; the row it holds was refused, and " + (everyRefused ? EveryObject : Numbered),
                    1 when someRefused => "; the row it holds does not stand, a newer section having refused its number" + (everyRefused ? ", and " + EveryObject : Lacking),
                    1 => "; the row it holds was kept" + keptAndLacking,
                    _ when noneStands => "; none of the rows it holds stands, " + EveryNumberRefused,
                    _ => "; the rows it holds were kept" + KeptButFor(section) + keptAndLacking,
                });
        }
        else if (section.Incomplete)
        {
            // Every number is refused where what the table lost cannot be told — the rest of one an /XRefStm names, past which
            // the chain goes on, or more ranges than are kept apart, its own or once joined with those refused before. A table
            // read in part lost rows to a row that is no row, or to the end of the file: the rest of the subsection the fault
            // ended is all the first loses where the next subsection or the trailer follows; where the end of the file cuts the
            // table, after a fault too, the trailer is lost, and with it the sections it would name. One whose rest cannot be
            // known ends the chain where startxref or a /Prev names it, however many ranges it lost: what only that rest, or
            // the sections its trailer would name, index is the rebuild's, which the message says unless a newer section
            // refused every number before the chain came to the table, which it says instead.
            said = (section.Fault ?? "the file ends before its trailer") + (noneStands
                ? "; none of the rows read stands, " + EveryNumberRefused
                : "; the rows read were kept" + KeptButFor(section) + (section.SubsectionsUnknown
                    ? (naming == "/XRefStm"
                        ? EveryOtherObject
                        : ", and what only the rest of the table, or the sections its trailer would name, index is looked for by rebuilding the index")
                    : (everyRefused
                        ? EveryOtherObject
                        : ", and the objects the rest of that subsection numbers are looked for by rebuilding the index where no section read before it indexes them, rather than taken from an older section's rows")));
        }
        else if (section.RefusedRow is { } refused)
        {
            // A guard that stopped the section past the row stopped the rows after it too, which its own report says. Beside
            // the rows a newer section refused, those the section refused itself after the one named — another row refused,
            // a row for a number it gives again — are not said to be kept either.
            said = refused + (noneStands
                ? (section.CutByLimit
                    ? "; none of the rows read, as far as one of the reader's limits let the section be read, stands, "
                    : "; none of the rows read stands, ") + EveryNumberRefused
                : (someRefused ? "; that row was refused" : "; that row alone was refused") +
                    (section.CutByLimit
                        ? ", the rows after it read as far as one of the reader's limits let the section be read"
                        : ", the rows after it read") +
                    (someRefused
                        ? ", and the other rows read kept" + KeptButFor(section) + "; its object is looked for by rebuilding the index where no row read before it indexes it, rather than taken from an older section's row"
                        : ", and its object is looked for by rebuilding the index where no row read before it indexes it, rather than taken from an older section's row"));
        }
        else
        {
            if (section.NoSubsection)
            {
                _diagnostics.Repair(
                    PdfDiagnosticCodes.XRefSectionEmpty,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{SectionNamed(section, naming)} holds no subsection: its trailer keyword follows its xref keyword. It was read as indexing nothing."),
                    section.Offset);
            }

            return;
        }

        _indexIncomplete = true;
        _diagnostics.Warn(
            PdfDiagnosticCodes.XRefSectionUnreadable,
            string.Create(CultureInfo.InvariantCulture, $"{SectionNamed(section, naming)} was read only in part: {said}."),
            section.Offset);
    }

    /// <summary>
    /// Gives the words that follow "were kept" in a report on <paramref name="section"/>, a section read in part or that
    /// cannot be read, for the rows read that do not stand: those the section refused itself
    /// (<see cref="XRefSectionRecord.RefusedRow"/>), not named beside the fault the message names, and those whose numbers a
    /// newer section refused (<see cref="XRefSectionRecord.RowsRefusedBefore"/>); nothing where every row read stands (#210).
    /// </summary>
    /// <param name="section">The record of the section, its rows read.</param>
    private static string KeptButFor(XRefSectionRecord section) => (section.RefusedRow is not null, section.RowsRefusedBefore) switch
    {
        (true, true) => " but for those it refused and those whose numbers a newer section refused",
        (true, false) => " but for those it refused",
        (false, true) => " but for those whose numbers a newer section refused",
        _ => string.Empty,
    };

    /// <summary>
    /// Reports the rows of a reserved type a cross-reference stream the chain kept gives, once for the section, the first of
    /// them in the message and the others counted (#221): each was read as ISO 32000-1 reads it, a reference to the null
    /// object, its object indexed as a free one's is, so that no older section's row stands for it
    /// (<see cref="ReadXRefStreamRows"/>), but for one whose number was refused before it was read, which stands for nothing,
    /// its object looked for by rebuilding the index (<see cref="XRefSectionRecord.ReservedRowsRefused"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reported after what <see cref="ReportSectionReadInPart"/> says of the section, whatever became of it once its rows were
    /// read: read whole or in part, found moved, or stopped by a guard, which leaves the rows read before it the file's. A
    /// section whose rows were never read gives none, and the section <c>startxref</c> names, when it cannot be read, leads to
    /// a rebuild of the whole index, which none of its rows is in. A section found moved is named by both offsets
    /// (<see cref="SectionNamed"/>).
    /// </para>
    /// <para>
    /// A row whose number a newer section refused — a row refused alone, a range a stream whose rows were lost declares, every
    /// number —, or a row of the stream's own for the same number refused, is not read as the null object: it stands for
    /// nothing, as every row whose number was refused does, and the message says so rather than that it was read so: of the
    /// row, of all the rows it counts, or, where only some of them are such, of those whose numbers were refused, which it
    /// neither names nor counts (#208, #210, #221). A row a newer section's row for the same number supersedes
    /// is read so, and stands for nothing older, as every superseded row does.
    /// </para>
    /// </remarks>
    /// <param name="section">The section the chain kept, read where it was named or found moved.</param>
    /// <param name="naming">What named the section: <c>startxref</c>, <c>/Prev</c> or <c>/XRefStm</c>.</param>
    private void ReportReservedRows(XRefSectionRecord section, string naming)
    {
        if (section.ReservedRow is not { } first)
        {
            return;
        }

        var many = section.ReservedRows > 1;
        var refused = section.ReservedRowsRefused;
        var said = (many, refused) switch
        {
            (false, 0) => ": the row was read so, and no older section's row stands for its object",
            (false, _) => ", but its number was refused before the row was read: it stands for nothing, and its object is looked for by rebuilding the index",
            (true, 0) => ": the rows were read so, and no older section's row stands for their objects",
            _ when refused == section.ReservedRows => ", but their numbers were refused before the rows were read: they stand for nothing, and their objects are looked for by rebuilding the index",
            _ => ": the rows were read so but for those whose numbers were refused before they were read, which stand for nothing and whose objects are looked for by rebuilding the index; no older section's row stands for the objects of the others",
        };

        _diagnostics.Warn(
            PdfDiagnosticCodes.XRefEntryReserved,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{SectionNamed(section, naming)} gives {CountText.Grouped(section.ReservedRows, "row", "rows")} a reserved type: {first}{(many ? ", the first of them" : string.Empty)}. ISO 32000-1 defines types 0, 1 and 2, and reads any other as a reference to the null object (7.5.8.3){said}."),
            section.Offset);
    }

    /// <summary>
    /// Refuses to the rows of every section the chain reads from here on the object numbers whose rows
    /// <paramref name="section"/>, kept though read only in part, or a classic table an <c>/XRefStm</c> names that cannot be
    /// read, lost (#156, #210): every number a cross-reference stream declares — those it holds rows for are indexed already,
    /// and no later row stands for them anyway —, or the rest of each subsection a classic table's faults ended
    /// (<see cref="_rowsLost"/>), every number where they were more ranges than <see cref="MaxRefusedRanges"/>, or where what
    /// the table holds past where its reading stopped cannot be known (<see cref="XRefSectionRecord.SubsectionsUnknown"/>).
    /// </summary>
    /// <remarks>
    /// A table <c>startxref</c> or a <c>/Prev</c> names whose rest cannot be known ends the chain, its trailer lost or
    /// unread: no older section's row is read after it, whatever it refuses.
    /// </remarks>
    private void RefuseRowsLost(XRefSectionRecord section)
    {
        if (section.Kind == XRefSectionKind.Stream)
        {
            // A stream's rows are read from its dictionary on, which is its trailer.
            RefuseDeclared(section.Trailer!);
        }
        else if (_rowsLostPast || section.SubsectionsUnknown)
        {
            RefuseEveryNumber();
        }
        else if (_rowsLost is { } lost)
        {
            RefuseRanges(CollectionsMarshal.AsSpan(lost));
        }
    }

    /// <summary>
    /// Notes that the classic table being read lost the rows of object numbers <paramref name="first"/> up to
    /// <paramref name="end"/>, which is not among them (<see cref="_rowsLost"/>).
    /// </summary>
    private void LoseRows(long first, long end)
    {
        // Past the bound, the list holds as many ranges as it may, and every number will be refused.
        var lost = _rowsLost ??= [];

        // A range that meets the last one lost — from within it, just past it or just below it, as the subsections a table
        // writes in the order of their numbers, or in the reverse order, lose them — joins it before the bound counts it, as
        // the refused ranges are counted once those that meet are joined (RefuseRanges). One apart from the last is kept
        // apart, though it may meet one lost before: the list is not searched, each range lost costing one comparison.
        if (lost.Count > 0 && first <= lost[^1].End && end >= lost[^1].First)
        {
            lost[^1] = (Math.Min(lost[^1].First, first), Math.Max(lost[^1].End, end));
        }
        else if (lost.Count < MaxRefusedRanges)
        {
            lost.Add((first, end));
        }
        else
        {
            _rowsLostPast = true;
        }
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
    /// Indexes what a row of <paramref name="section"/> says of object <paramref name="number"/>, unless a newer section
    /// said it already or refused its row, a newer cross-reference stream whose rows were lost declares it
    /// (<see cref="RefusedToOlderRows"/>), the section refused a row of its own for the number before, or the index has no
    /// room left for a number it lacks. A row a newer section refused so is noted on the section, whose report says it does
    /// not stand (<see cref="XRefSectionRecord.RowsRefusedBefore"/>); one whose number the section refused itself, which
    /// gives it twice — the reading that refused it the section's own (<see cref="XRefSectionRecord.Ordinal"/>) —, is not,
    /// its report saying the section refused rows of its own.
    /// </summary>
    /// <returns>False where the row does not stand, its number refused before it was read; true otherwise.</returns>
    private bool AddFromChain(int number, XRefEntry entry, XRefSectionRecord section)
    {
        if (_refusedNumbers is not null && _refusedNumbers.TryGetValue(number, out var refuser))
        {
            section.RowsRefusedBefore |= refuser != section.Ordinal;
            return false;
        }

        if (RefusedToOlderRows(number))
        {
            section.RowsRefusedBefore = true;
            return false;
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

        return true;
    }

    /// <summary>
    /// Refuses a row of <paramref name="section"/> for object <paramref name="number"/>: the object is left for a rebuild to
    /// find, and no older section's row stands for it. A row for a number a newer section gave already describes an older
    /// revision, and refusing it changes nothing.
    /// </summary>
    private void Refuse(int number, XRefSectionRecord section)
    {
        _indexIncomplete = true;

        if (_xref.TryGet(number, out _) || _refusedNumbers?.ContainsKey(number) == true)
        {
            return;
        }

        if (IndexHasRoom)
        {
            (_refusedNumbers ??= new(SeededComparer.Instance)).Add(number, section.Ordinal);
            _refusedOrder?.Add(number);
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
    /// Makes room in the index for the <paramref name="rows"/> a section is about to give, so that the index is sized once
    /// rather than grown by doubling, and for no more numbers than <see cref="PdfReaderLimits.MaxIndexEntries"/> lets it
    /// take from here on, nor than the newest section's <c>/Size</c> numbers, written direct, once it is read: one more than the document's
    /// highest object number (ISO 32000-1, 7.5.5), which an older section's rows, superseded, add nothing to. A size the
    /// sections read so far number past is stale: one past the highest number read stands for it, until an older section
    /// numbers past the newer ones too, and from then on it bounds nothing. The caller bounds the rows by those the section
    /// holds: a count the file declares is a claim.
    /// </summary>
    /// <remarks>
    /// A chain of many like sections makes room section after section, the index doubling at least each time it must grow.
    /// Once a doubling would bring it within a doubling of what the <c>/Size</c> numbers, room is made for that at once:
    /// the last step lands on the size, rather than past it, and makes room for no more than four times what the index
    /// held, which the rows read paid for.
    /// </remarks>
    private void ReserveIndex(long rows)
    {
        var most = _guard.Bound(PdfLimit.IndexEntries);
        var room = (int)Math.Min(rows, int.MaxValue);

        // The /Size as written, which ISO 32000-1 makes direct (Table 15, 7.5.8.2), an integral real read as the integer it
        // is: a reference is not read through to make room by, as the newest cross-reference stream's beside an /Index, which
        // numbers no row, would be while the chain is.
        // A size the sections read so far number past is stale, as when an update did not raise it, and one past the highest
        // number read stands for it while the older sections number below the newer ones, as they do in such a chain. Once
        // one has numbered past them, the size bounds nothing: capped at one past the highest number read, the room would
        // grow by one entry at each older section numbering one further, and the index be copied whole each time (#155).
        // A hybrid file's newest table is merged before its /XRefStm stream is read, so that the stream's room is bounded by
        // the table's size, or one past the table's highest number where that size is stale, and its rows grow by doubling
        // past it.
        if (_xref.Trailer.GetRaw(PdfName.Size) is { } written and not PdfReference && written.AsInteger() is { } size
            && (size > _highestRead || !_olderNumberedPast))
        {
            // Worked out as a long, the bound lies between zero and the guard, so that narrowing it wraps nothing: a size
            // below zero, stale like any the sections number past, gives way to one past the highest number read. A bound
            // the index holds as many entries as already asks for no room.
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
}
