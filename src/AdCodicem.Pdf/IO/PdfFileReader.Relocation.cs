using System.Globalization;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO.XRef;

namespace AdCodicem.Pdf.IO;

internal sealed partial class PdfFileReader
{
    /// <summary>
    /// Looks for a cross-reference section near the offset the chain named, where a careless writer's
    /// arithmetic leaves it: a classic table's <c>xref</c> keyword, or the header of an object that reads as a
    /// cross-reference stream, nearest first.
    /// </summary>
    /// <remarks>
    /// The search spans <see cref="NearbySearchRadius"/> bytes either side, as an object's does — a place counts where
    /// what starts there lies whole within them, however near the file's start the offset falls (#197) —, and tries at
    /// most <see cref="MaxRelocationCandidates"/> places in all, over the whole document, each counted once: each is
    /// read as a section is, past a probe of its first bytes, through a window of 64 KB first, grown up to
    /// <see cref="PdfReaderLimits.MaxXRefSectionLength"/> for a table and
    /// <see cref="PdfReaderLimits.MaxTrailerLength"/> for a stream's dictionary, the file chooses how many headers lie
    /// near a section, and a chain can name a section for each of its <see cref="PdfReaderLimits.MaxXRefSectionCount"/>
    /// links. Found, the section's record takes what was read there — a section that cannot be read too, which is not
    /// found to be read, and which the chain reports where it was found, as where it is named —; not found, it keeps
    /// what the named offset held. A place is a guess: a table's <c>xref</c> keyword, or an object that declares a
    /// cross-reference stream and no other type, tried or skipped as <see cref="CheckPlace"/> says. One that reads, whole
    /// or in part, is taken. One that cannot be read is the section all the same, the search ending there, where it
    /// declared itself one — the keyword, or the header of an object that gives <c>/Type /XRef</c>, before a fault or a
    /// guard's cut, its token starting its line
    /// (<see cref="StartsItsLine"/>) —, as at the offset named, so that no older section farther off stands for its lost
    /// rows in silence (#281), or where a value it is read with led to an object a guard cut (#263). An object that only
    /// gives a <c>/W</c>, or a dictionary a guard cut before it said what it is, may be anything, and is dropped, the
    /// search going on to the next place — near the offset a hybrid table's <c>/Prev</c> names, such an object that may
    /// be the table's own, as below, is passed over first, and is not reported where the search takes another section
    /// before it comes back to it.
    /// Two kinds of place are passed over, and count toward no limit: where the section was named, which holds no section
    /// that reads; and every place where a section the chain reached starts — read where it was named, or found moved,
    /// or lost there though it declares itself one; for a header whose number is written with a run of signs, where the
    /// digits of a stream the chain reached from a <c>+</c> of that run, or from the digit past it, start past the signs
    /// of the place, as its reading lexed them, the header found at its digit by the walk back from <c>obj</c>, wherever
    /// the run starts and whatever sign starts it, or at the first sign of a run a <c>+</c> starts within the bytes
    /// searched where the walk lexed the line the number is on, which holds a <c>%</c> (<see cref="DigitsPastSigns"/>,
    /// <see cref="_sectionDigitsAt"/>, #327) —, which holds that section, not the one looked for: taken, it would be read
    /// again, in a loop the file does not have, and what only the section looked for indexes would be lost in silence
    /// (#188) — but for what the same table's <c>/XRefStm</c> read in the stream's place, which a <c>/Prev</c>'s search
    /// takes: both links of the table reach it, and the <c>/Prev</c> reads it whole, its links with it (#156). Where that
    /// is a cross-reference stream — or where the table's <c>/XRefStm</c> names again a stream a newer table's
    /// <c>/XRefStm</c> read, which stays passed over as reached (#330) —, a stream near the offset may be the table's own,
    /// which the <c>/Prev</c> does not mean (<see cref="MayBeTablesOwn"/>), but only one that starts no sooner than that
    /// stream, which is told by its data, not by the byte its reading started at — where the <c>/XRefStm</c> names a place
    /// inside its object number, its own header starts before that byte, and where it names a <c>+</c> of the run of signs
    /// the number is written with, the header the walk back from <c>obj</c> finds starts at the digit past the run, that
    /// stream too (<see cref="NumberRunsInto"/>) —: under updates appended to the file, a revision's own stream lies past
    /// every older revision's sections, and a stream before it — a first revision's, which gives no <c>/Prev</c>
    /// (ISO 32000-1, Table 17), or an older revision's — keeps its nearest-first place, whatever its <c>/Prev</c>, the
    /// chain ending there where it gives none. One that starts there or past it, its dictionary read whole, is told by its
    /// own <c>/Prev</c>, read as the chain reads it
    /// (<see cref="StreamPrevious"/>) — a real with no fractional part as its integer, a reference as what a section
    /// already read places there, and one that gives no offset in the file as another offset —: one that gives none, or
    /// gives the offset the table's <c>/Prev</c> names, hands the chain nothing on, or back to that offset, and is
    /// tried once every other place was, nearest first among those, the stream the <c>/XRefStm</c> read among them
    /// where its <c>/Prev</c> names the table's, and never where it gives none, as any section the chain reached is
    /// passed over (#188); one whose <c>/Prev</c> names another offset is tried where it comes nearest, the stream the
    /// <c>/XRefStm</c> read among them. A dictionary a guard cut keeps its nearest-first place, whatever <c>/Prev</c>
    /// the cut may hide: where it declared itself a section before the cut, it is the section found moved, and the
    /// guard's, as any such place is (ADR 34). A stream taken whose <c>/Prev</c> names no older section is read whole
    /// all the same, and its <c>/Prev</c> reported as the chain finds it: a loop, an offset outside the file, or no
    /// offset. To tell, the search reads nothing it would not read trying the place, but the objects a <c>/Prev</c>
    /// written as a reference leads to, as the chain reads them, nothing loaded or reported — what they lead to through
    /// a guard's cut is forgotten where the place is not taken, the stream the <c>/XRefStm</c> read among them, as with
    /// any place dropped (<see cref="ForgetReadThrough"/>): the chain follows that <c>/Prev</c> only from a place it
    /// takes —: the dictionary of the stream the <c>/XRefStm</c> read is the one that reading kept, and another place's
    /// is the one its reading parses first, the reading stopping at the object and keeping nothing where the stream may
    /// be the table's own, a search for its <c>endstream</c> aside. A place so passed over counts once toward
    /// <see cref="MaxRelocationCandidates"/>, at its first reading: read again when the search comes back to it, through
    /// windows that grow from 64 KB up to <see cref="PdfReaderLimits.MaxTrailerLength"/> as a stream's dictionary is read,
    /// it is not counted again, and is tried even once the bound stops new places, so that the places tried stay within
    /// twice the bound; the stream the <c>/XRefStm</c> read, put back unread, is counted where it is read, and the bound
    /// may stop it. Past the bound, the place a hybrid table's <c>/Prev</c> names is still asked whether that
    /// <c>/Prev</c> keeps its place there, where it is the nearest place not yet read, in the look
    /// <see cref="KeepsItsPlace"/> makes once for each hybrid table, and where it does, the section is left to that
    /// <c>/Prev</c>, as with places to spare; where a nearer place is left unread, the bound stops the search before it.
    /// One the search does not come back to, another section taken first, is reported once the chain is read, where it
    /// declares itself a section, as a place a fault of the file keeps from being read is taken only where it does — its
    /// dictionary gives <c>/Type /XRef</c>, and its header starts its line (<see cref="StartsItsLine"/>) —, at its first
    /// sign where its number is written with a run of signs that starts with a <c>+</c>, where the chain has not reached
    /// it, at its header or inside its object number, and where none of the reader's limits left part of the chain
    /// unread, nor may have hidden a link the chain would have followed, as white space past a trailer's window that
    /// turned the chain's reading of a section it names into a search may, or a cut of the reading at an offset the chain
    /// names, whatever it cut (<see cref="ReportStreamsPassedOver"/>): an object that gives only a <c>/W</c> is not,
    /// whether it reads or not. The search is never asked for a section that is there and cannot be read
    /// (<see cref="DeclaresItselfASection"/>): what is near it is another.
    /// </remarks>
    /// <param name="offset">The offset that names the section, as written.</param>
    /// <param name="section">The record of the section, which takes what was read where it was found.</param>
    /// <param name="leaveAt">
    /// Where the first token at the offset a hybrid table's <c>/Prev</c> names starts, -1 for none — no offset in the file,
    /// what the chain has reached, where that <c>/Prev</c> ends the chain in a loop and keeps no place, or white space and
    /// comments running on past a trailer's window —, nor for any other link.
    /// Where the search comes to it, it asks whether that <c>/Prev</c> keeps its place there, as its own reading does
    /// (<see cref="KeepsItsPlace"/>) — told only then, the place it names read only where the search comes to it —: where it
    /// does, the search stops there, as it would take what it found there before any place farther off, and leaves the
    /// section to that link, which reads it whole, its links with it (#156); where it does not, what it names is a place like
    /// any other, dropped where it holds no section, the search going on.
    /// </param>
    /// <param name="leaveIsTable">Whether the first token at <paramref name="leaveAt"/> is the <c>xref</c> keyword.</param>
    /// <param name="readInStreamsPlace">
    /// The record of what the <c>/XRefStm</c> of the table whose <c>/Prev</c> names the section read in the stream's place,
    /// its links left unread; null for none, and for any other link. Where that section starts is a place the search takes,
    /// though the chain reached it: a classic table, which is no stream of the table's revision, where it comes to it, the
    /// nearest; a cross-reference stream, which may be that stream, in the order its own <c>/Prev</c> gives it, as the
    /// remarks say, beside any other stream near the offset, past it, that may be the table's own — its own header too,
    /// where the <c>/XRefStm</c> named a place inside its object number and the header starts before, or named a
    /// <c>+</c> of the run of signs the number is written with and the header the walk back from <c>obj</c> finds starts
    /// at the digit past the run (<see cref="NumberRunsInto"/>).
    /// </param>
    /// <param name="streamNamedAgain">
    /// The record of the cross-reference stream a newer table's <c>/XRefStm</c> read in the stream's place, which the
    /// <c>/XRefStm</c> of the table whose <c>/Prev</c> names the section names again; null for none, and for any other link.
    /// It is not taken, a section the chain reached (#330), and stands for the stream the <c>/XRefStm</c> read in telling
    /// which streams near the offset may be the table's own, as the remarks say.
    /// </param>
    /// <param name="previous">The offset the section found gives through <c>/Prev</c>, or -1.</param>
    /// <param name="hybrid">The offset the section found gives through <c>/XRefStm</c>, or -1.</param>
    /// <param name="left">
    /// Why the search stopped at <paramref name="leaveAt"/>, finding nothing nearer: the <c>/Prev</c> reads a section there,
    /// or what is there declares itself one, or a guard cut what is there before the reading could tell
    /// (<see cref="KeptPlace"/>); <see cref="KeptPlace.None"/> where it did not stop there.
    /// </param>
    private bool TryRelocateXRefSection(
        long offset,
        XRefSectionRecord section,
        long leaveAt,
        bool leaveIsTable,
        XRefSectionRecord? readInStreamsPlace,
        XRefSectionRecord? streamNamedAgain,
        out long previous,
        out long hybrid,
        out KeptPlace left)
    {
        previous = -1;
        hybrid = -1;
        left = KeptPlace.None;

        var absolute = PositionOf(offset);

        if (!IsInFile(absolute))
        {
            return false;
        }

        // The window ends the radius past the position, or at the file's end, however near the file's start the position
        // lies, where a window clamped there once reached up to twice the radius past it (#197): the position is in the
        // file, so the end is too. The byte before the window is read as well, where there is one, so that a place counts
        // only where it starts inside the window, as an object's header does: not the end of a startxref, nor a header whose
        // number the window's start cuts.
        var start = Math.Max(0, absolute - NearbySearchRadius);
        var end = absolute + Math.Min(NearbySearchRadius, _source.Length - absolute);
        var lead = start > 0 ? 1 : 0;
        var candidates = new List<long>();

        // The window stays open while the places are tried: a table that cannot be read is told from a word in a comment or a
        // string by the line it lies in. The byte past it is read as well, where there is one, and seen only by the search for
        // places: it tells a keyword that ends the window from a longer word (#189). A window shorter than asked holds the end
        // of the data, as every window does.
        var trail = end < _source.Length ? 1 : 0;
        using var whole = _source.GetWindow(start - lead, (int)(end - start) + lead + trail);
        var window = whole.Memory[..Math.Min(whole.Length, (int)(end - start) + lead)];
        FindSectionCandidates(whole.Memory.Span, start - lead, lead, window.Length, candidates);

        candidates.Sort((left, right) => Math.Abs(left - absolute).CompareTo(Math.Abs(right - absolute)));

        // What the table's /XRefStm read in the stream's place is taken, though reached. Where it read a cross-reference
        // stream, or names again one a newer table's /XRefStm read, which is not taken (#330), a stream near the offset that
        // starts no sooner than that stream may be the table's own, which the /Prev does not mean: one whose dictionary, read
        // whole, gives no /Prev, or a /Prev naming the offset the table's /Prev names as the chain reads it, hands the chain
        // nothing on, or back to that offset, as a hybrid table's own stream may. Such a stream is tried once every other
        // place was, nearest first among them, but for the stream the /XRefStm read when it gives no /Prev, which is passed
        // over as any section the chain reached is (#188). A stream whose /Prev names another offset, or that starts before
        // that stream, is a section the /Prev can mean, tried where it comes nearest, the stream the /XRefStm read among them
        // (#156). A place passed over is put back at the end of the list, past the places the window holds, and tried there
        // with nothing passed over.
        var takeAt = readInStreamsPlace is { } place ? place.Offset + place.Padding : -1;
        var streamRead = readInStreamsPlace is { Kind: XRefSectionKind.Stream } ? readInStreamsPlace : streamNamedAgain;
        var tablePrevious = streamRead is not null ? offset : -1;
        var streamStart = streamRead is { } read ? read.Offset + read.Padding : -1;
        var streamDigits = streamRead?.DigitsAt ?? -1;
        var inWindow = candidates.Count;

        // Where the stream the /XRefStm read is put back unread, past the places the window holds: the one place put back
        // that was not counted yet.
        var uncountedAt = -1;

        // The places passed over whose dictionary gives /Type /XRef, with what the scan's memo said of each, which may be
        // reported where the search does not come back to them; null while there is none.
        List<(long At, PlaceCheck Check)>? typedPassedOver = null;

        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            var retry = i >= inWindow;

            // What a value is read with leads to through a guard's cut, while the place is told and tried — the /Prev of the
            // stream the /XRefStm read, judged before that stream is tried, among it —, is forgotten where the place is not
            // taken, as a place dropped leaves nothing of its reading: a reading that keeps the place, if the search comes
            // back to it, reads it anew, for its guard to be reached once the chain is read (#263).
            var readThrough = _chainValuesCut?.Count ?? 0;

            // The stream the /XRefStm read, or names again, is told by its data, not by the byte its reading started at: where
            // the /XRefStm names a place inside that stream's object number, the reading starts past the number's first byte
            // — a digit, or the first sign of the run the number is written with, where the line it is on, which an end of
            // line ends before obj, holds a %, the walk back from obj then lexing that line —, at a sign of that run or a
            // digit, and the stream's own header, whose number runs on into that place, starts before it; where it names
            // a + of the run of signs the number is written with, the header the walk back from obj finds starts at the digit
            // past the run, where that reading found the digits, however few of those signs the window holds. What follows
            // that number is the same bytes for both — the dictionary, the data —: the header is that stream's, told as
            // that stream is, from where its reading started, rather than as another stream before it or past it. That
            // start may lie before the bytes the window holds: the look for a section the chain reached takes it as it is,
            // the stream reached there.
            if (NumberRunsInto(window.Span, start - lead, candidate, streamStart, streamDigits))
            {
                candidate = streamStart;
            }

            // Under updates appended to the file, a revision's own stream lies past every older revision's section, and the
            // stream the /XRefStm read, or names again, is the table's own or an older revision's: a stream that starts
            // before it, that stream's own header aside, is an older revision's, and keeps its nearest-first place, whatever
            // its /Prev.
            var passOver = !retry && candidate >= streamStart ? tablePrevious : -1;

            // Where the section was named holds no section that reads, and a place where a section the chain reached starts —
            // read there, found moved there, or there and not read though it declares itself one — holds that section, not the
            // one looked for: both are passed over, but for what the table's /XRefStm read in the stream's place, which is
            // taken (#188, #156). A header whose number is written with a run of signs is the header a reading that started at
            // a + of that run, or at the digit past it, read, the signs before that + aside, as a load reads a run of signs as
            // one, the first deciding. The walk back from obj finds it at that digit — wherever the run starts, before the
            // bytes searched too, and whatever sign starts it, a - among them, from which no reading reads a header —, or,
            // where it lexed the line the number is on, which holds a %, at the run's first sign, a + within the bytes
            // searched: lexed there, a run a - starts reads as a negative number, and one whose first sign lies before the
            // bytes searched starts before them, and the search finds neither. The place it finds is passed over where the
            // digits of a stream the chain reached start past the place's own run of signs, at the place where it has none,
            // as that reading lexed them (DigitsPastSigns, _sectionDigitsAt), a stream the chain could not read among them,
            // which nothing above tells by its data (#327). A section the chain reached that starts at a sign is such a
            // stream, its digits recorded with it (RecordSectionReached). The place is one the search found, within the
            // bytes searched, its run of signs ending within them: where the reading of the stream an /XRefStm read
            // started, which may lie before them and stands for a header found above, is that stream's start, taken or
            // reached, and told before any digits are looked for. A section the chain reached inside the digits of the
            // place's number, past the first, is not looked for, the stream above aside: the header found before it is
            // tried as a place of its own (#332).
            if (candidate == absolute ||
                (candidate != takeAt &&
                    (_sectionsReadAt.Contains(candidate) ||
                        _sectionDigitsAt.Contains(DigitsPastSigns(window.Span, start - lead, candidate)))))
            {
                continue;
            }

            // The stream the /XRefStm read is told by the dictionary that reading kept, and not read again to tell: passed over
            // where it gives no /Prev, as any section the chain reached is, and put back where its /Prev names the table's.
            // What its /Prev read through is forgotten either way: the chain follows that /Prev only where it takes the stream.
            if (candidate == takeAt && passOver >= 0 && MayBeTablesOwn(readInStreamsPlace!.Trailer, passOver))
            {
                if (StreamPrevious(readInStreamsPlace.Trailer) >= 0)
                {
                    uncountedAt = candidates.Count;
                    candidates.Add(candidate);
                }

                ForgetReadThrough(readThrough);
                continue;
            }

            if (candidate == leaveAt)
            {
                // Asked here alone, where the search comes to the place, and nowhere it does not: where the /Prev keeps no
                // place there, it is tried as any other.
                left = KeepsItsPlace(leaveAt, leaveIsTable);

                if (left != KeptPlace.None)
                {
                    break;
                }
            }

            // What the chain names is read as it names it; any other place is asked of the scan's memo first, and one the scan
            // does not take is no section: nothing is read there, nor counted (CheckPlace).
            var check = candidate == takeAt || candidate == leaveAt
                ? PlaceCheck.Named
                : CheckPlace(window.Span, start - lead, lead, candidate);

            if (check == PlaceCheck.Skipped)
            {
                continue;
            }

            // Each place counts once, at its first reading: a place passed over and put back is tried again uncounted, even
            // once the bound stops new places, a place already counted, so that the places tried stay within twice the bound;
            // the stream the /XRefStm read, put back unread, is counted where it is read, and the bound may stop it. Once the
            // bound is reached, the places the window holds that were not read are left, and the search goes on to those put
            // back — but for the place a hybrid table's /Prev names, asked above, before the bound, where it is the nearest
            // place not yet read: whether that /Prev keeps its place there is told as with places to spare, in the look
            // KeepsItsPlace makes once for each hybrid table, and where it does, the section is left to that /Prev there.
            // Where a nearer place is left unread, the bound stops the search before it. What the /Prev of the stream the
            // /XRefStm read led to, judged above, is forgotten with the place the bound leaves.
            if (!retry || i == uncountedAt)
            {
                if (_relocationCandidatesTried >= MaxRelocationCandidates)
                {
                    if (!retry)
                    {
                        i = inWindow - 1;
                    }

                    ForgetReadThrough(readThrough);
                    continue;
                }

                _relocationCandidatesTried++;
            }

            // The memo's bound is reached for a place tried without its word, not for one the bound on places leaves untried.
            if (check == PlaceCheck.PastMemo)
            {
                ReachScanMemoBound();
            }

            var attempt = new XRefSectionRecord(section.NamedBy, candidate, section.NamedFrom);
            var indexMark = MarkIndex();
            XRefSectionState state;

            try
            {
                state = TryReadXRefSection(candidate, attempt, out previous, out hybrid, candidate: true, passOver);
            }
            catch
            {
                // The source failed the reading: what it recorded is kept, as before the place was tried.
                _xref.KeepTentative();
                throw;
            }
            finally
            {
                _refusedOrder = null;
            }

            // A section that cannot be read is the section all the same where what is there declared itself one — the xref
            // keyword, /Type /XRef —, before a guard's cut or a fault (#281), or where a guard cut an object a value it is read
            // with leads to (#263): found where it was moved, it is reported there, as where it is named, rather than passed
            // over for an older section farther off, whose rows would stand for its lost ones in silence. A value cut is a
            // guard's cut. A place that said nothing of what it is — an object that gives only a /W, or a dictionary a guard cut
            // before it said — may be anything, and is dropped as any other is, with what it read through. That the scan took
            // the place does not tell: where it reads the bytes as they come, it takes the word in a comment or a string
            // (StartsItsLine).
            if (state == XRefSectionState.Read ||
                (state == XRefSectionState.Malformed &&
                    (attempt.CutByValue ||
                        ((attempt.TypedXRef || attempt.Kind == XRefSectionKind.Table) &&
                            StartsItsLine(check, window.Span, start - lead, lead, candidate)))))
            {
                _xref.KeepTentative();
                section.RelocateTo(attempt);
                section.Unconfirmed = check is PlaceCheck.Raw or PlaceCheck.PastMemo;
                _diagnostics.Repair(
                    PdfDiagnosticCodes.XRefOffsetAdjusted,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The cross-reference section {section.NamedBy} names at offset {absolute} was found {CountText.Distance(candidate - absolute, "it")}."),
                    candidate);

                // The streams passed over that the search does not come back to, past this place among those put back, are
                // reported once the chain is read, where it has not reached them (ReportStreamsPassedOver) — those that
                // declare themselves a section, as a place the search takes though a fault of the file keeps it from being
                // read does, its line asked only now, so that a stream the search comes back to and takes costs no read for
                // it. The stream the /XRefStm read, put back unread among them, is one the chain read where it starts, and is
                // not recorded. Each is recorded where its header's token starts — at the first sign of the run its number is
                // written with, where that run starts with a +, which the walk back from obj passes over (HeaderTokenStart,
                // #327) —, and where its object number ends from there is kept with it, told in the window: a link of an
                // older section that names a place inside that number reaches the same stream.
                for (var back = Math.Max(i + 1, inWindow); typedPassedOver is not null && back < candidates.Count; back++)
                {
                    var passed = candidates[back];

                    foreach (var (typed, typedCheck) in typedPassedOver)
                    {
                        if (typed == passed && StartsItsLine(typedCheck, window.Span, start - lead, lead, passed))
                        {
                            var headerAt = start - lead + HeaderTokenStart(window.Span, lead, (int)(passed - (start - lead)));
                            (_streamsPassedOver ??= []).Add(
                                (headerAt, NumberEnd(window.Span, start - lead, headerAt), absolute, section.NamedFrom));
                        }
                    }
                }

                if (state == XRefSectionState.Read)
                {
                    return true;
                }

                section.State = XRefSectionState.Malformed;
                break;
            }

            ForgetReadThrough(readThrough);
            RollBackIndex(indexMark);

            // A stream whose filters decode nothing of its data is read that far, and dropped where it declares no section:
            // what its decoding reported goes with it, a reading of that stream meeting it again, and a guard the decoding
            // reached stands. What reading its dictionary reported stands too, as for any place dropped: a reading of that
            // stream meeting it again would not make it again (#221).
            if (attempt.UndecodedReports is { } decoding)
            {
                _diagnostics.RollBackBesideLimits(decoding);
            }

            // A stream that may be the table's own was read no further than its object, and left nothing but a search for its
            // endstream: it is tried again once every other place was, read again, and not counted again toward
            // MaxRelocationCandidates. Whether its dictionary gives /Type /XRef is kept, with what the memo said of the place,
            // for where the search takes another section first.
            if (attempt.MayBeTablesOwn)
            {
                candidates.Add(candidate);

                if (attempt.TypedXRef)
                {
                    (typedPassedOver ??= []).Add((candidate, check));
                }
            }
        }

        previous = -1;
        hybrid = -1;
        return false;
    }

    /// <summary>
    /// Determines whether the header the search near an offset found at <paramref name="candidate"/> is the header of the
    /// stream whose reading started at <paramref name="streamStart"/>, what follows its object number — the generation, the
    /// dictionary, the data — being the same bytes for both (#156): its number runs on into where that reading started,
    /// inside the number, past its first byte; or that reading started at a <c>+</c> of the run of signs the number is
    /// written with, and the header starts at the digits past it, where that reading found them
    /// (<paramref name="streamDigits"/>).
    /// </summary>
    /// <remarks>
    /// No header the search finds starts inside a run of digits. The walk back from <c>obj</c> takes the whole run of the
    /// number's digits, back to the first byte that is not one, and does not look at that byte — a sign, the end of a
    /// keyword such as <c>endobj</c>, any regular byte —: the header <c>+17 0 obj</c> starts at its digit 1, one byte past
    /// the sign a reading of it starts at, and <c>++17 0 obj</c> two bytes past, a load reading a run of signs as it reads
    /// one, the first deciding; a reading that starts at a <c>-</c> reads a negative number, no object's header, and no
    /// stream. The reading of the stream lexed its number, and kept where the digits past the run of signs start, where
    /// the number starts if it has none (<see cref="XRefSectionRecord.DigitsAt"/>): a header found there is that
    /// stream's, whether or not the window holds the signs before it — as where the reading started at a sign before the
    /// byte the window holds ahead of the bytes searched, the digits within them —, told by a comparison, nothing read for
    /// it. A header found before where the reading started is told by a look at the bytes of its number, in the bytes
    /// searched, which the window holds, nothing read for it either: the number runs on from the header's first byte
    /// through the signs and the digits past it (<see cref="NumberEnd"/>) — a header found where the line its number is
    /// on, which an end of line ends before <c>obj</c>, holds a <c>%</c>, a comment's or a string's, is lexed from that
    /// line's start, and starts where the lexer's token does, its run of signs included
    /// (<see cref="TryReadHeaderBackwards"/>) —, and the keyword <c>xref</c>, which holds neither past its first byte,
    /// runs into nothing. Every header found lies whole within the bytes searched (<see cref="FindSectionCandidates"/>),
    /// so that its digits end within them.
    /// </remarks>
    /// <param name="span">The bytes searched.</param>
    /// <param name="spanStart">Where <paramref name="span"/> starts in the file.</param>
    /// <param name="candidate">Where the header starts in the file, within the bytes searched.</param>
    /// <param name="streamStart">Where the reading of the stream started in the file, -1 for none.</param>
    /// <param name="streamDigits">
    /// Where the digits of the stream's object number start in the file, past the run of signs it is written with, if any,
    /// as its reading lexed them; -1 for none.
    /// </param>
    private static bool NumberRunsInto(
        ReadOnlySpan<byte> span, long spanStart, long candidate, long streamStart, long streamDigits) =>
        candidate == streamDigits || (candidate < streamStart && NumberEnd(span, spanStart, candidate) > streamStart);

    /// <summary>
    /// Gives where the object number of the header the search near an offset found at <paramref name="at"/> ends, read as
    /// a load reads a number, a run of signs and then digits: past the signs that follow its first byte, if any, at the
    /// first byte that is not a digit. Its first byte is a digit, or the first sign of the run the number is written with,
    /// where the header's token starts — a header lexed from its line's start, or one the walk back from <c>obj</c> found
    /// at its digit, whose token starts at that sign where the run starts with a <c>+</c> (<see cref="HeaderTokenStart"/>)
    /// — (<see cref="NumberRunsInto"/>, <see cref="ReportStreamsPassedOver"/>).
    /// </summary>
    /// <remarks>
    /// A place the search finds, a header or the keyword <c>xref</c>, lies whole within the bytes searched, and holds more
    /// than signs past its first byte, the keyword <c>obj</c> or the rest of the keyword: the signs, and the digits after
    /// them, end within those bytes, however the file writes them.
    /// </remarks>
    /// <param name="span">The bytes searched.</param>
    /// <param name="spanStart">Where <paramref name="span"/> starts in the file.</param>
    /// <param name="at">Where the header starts in the file, within the bytes searched.</param>
    private static long NumberEnd(ReadOnlySpan<byte> span, long spanStart, long at)
    {
        var rest = span[(int)(at - spanStart + 1)..];
        var signs = rest.IndexOfAnyExcept((byte)'+', (byte)'-');
        return at + 1 + signs + rest[signs..].IndexOfAnyExceptInRange((byte)'0', (byte)'9');
    }

    /// <summary>
    /// Gives where the digits start past the run of signs the place the search near an offset found at <paramref name="at"/>
    /// starts with, as a load reads a number: the first byte at or past the place that is no sign — the place itself where
    /// it is none, a header the walk back from <c>obj</c> found at its digit, or the keyword <c>xref</c> (#327).
    /// </summary>
    /// <remarks>
    /// A header found where the walk lexed the line its number is on, which an end of line ends before <c>obj</c> and which
    /// holds a <c>%</c>, starts where the lexer's token does, at the first sign of the run its number is written with
    /// (<see cref="TryReadHeaderBackwards"/>): a reading that started at a later <c>+</c> of that run, or at the digit past
    /// it, read the same header, and found its digits where this look does, as for a header found at its digit
    /// (<see cref="XRefSectionRecord.DigitsAt"/>). The look ends within the bytes searched, which hold every place found
    /// whole, its number with it; where they held nothing but signs from the place to their end, the place would be given
    /// as it is. No digits of a stream's number start at a sign, nor at the <c>x</c> of <c>xref</c>: the position tells
    /// nothing there. A look at the bytes of the place, in the window: nothing is read for it, nor allocated.
    /// </remarks>
    /// <param name="span">The bytes searched, and the byte before them where the window holds one.</param>
    /// <param name="spanStart">Where <paramref name="span"/> starts in the file.</param>
    /// <param name="at">Where the place starts in the file, within the bytes searched.</param>
    private static long DigitsPastSigns(ReadOnlySpan<byte> span, long spanStart, long at)
    {
        var signs = span[(int)(at - spanStart)..].IndexOfAnyExcept((byte)'+', (byte)'-');
        return signs < 0 ? at : at + signs;
    }

    /// <summary>
    /// Gives where, in <paramref name="span"/>, the token starts that a load reads the place the search near an offset
    /// found at <paramref name="at"/> from: where a run of signs that starts with a <c>+</c> ends at that place, the
    /// run lying within the bytes searched, its first sign, from which a load reads the header's object number; the
    /// place itself otherwise (#327), where the scan for objects places the header (<see cref="CheckPlace"/>). The
    /// search asks it of the scan's memo, of the line test, and for the record of a stream it passes over. Whether the
    /// chain reached the header found at the place is told by where the digits of the streams it reached start, looked
    /// up past the run of signs the place starts with (<see cref="DigitsPastSigns"/>, <see cref="_sectionDigitsAt"/>),
    /// not by this token's start.
    /// </summary>
    /// <remarks>
    /// The walk back from <c>obj</c> takes the digits of a header's number, and stops at the first byte that is not one, a
    /// sign among them (<see cref="TryReadHeaderBackwards"/>): the header <c>+14 0 obj</c> is found at its digit 1, though
    /// a load reads object 14 from the sign, and reads a run of signs as it reads one, the first deciding — <c>++14</c> and
    /// <c>+-14</c> are 14 too. Asked at that digit, the memo and the line test would find no header starting there;
    /// asked here, they find the token a load reads (<see cref="IsTokenOnItsLine"/>), and a stream the search passes
    /// over there is recorded here (<see cref="ReportStreamsPassedOver"/>). A run that starts with a <c>-</c> gives a
    /// negative number, no object's, and its place is given as it is, where no token starts: it declares nothing. So
    /// does a run that does not lie within the bytes searched, which the window holds the byte before to tell, as a
    /// header whose number starts before them counts for nothing (#197). A place where the lexer starts a token — the
    /// keyword <c>xref</c>, or the first sign of a header found where the walk lexed the line its number is on, which
    /// an end of line ends before <c>obj</c> and which holds a <c>%</c> — has no sign before it, and is given as it is.
    /// A reading that started at a <c>+</c> of either run, or at the digit past it, and read the header there, reached
    /// it all the same: the search tells it by where that reading found the digits, past the signs of the place,
    /// wherever the run starts and whatever sign starts it (<see cref="TryRelocateXRefSection"/>). The place may be
    /// where the reading of the stream an <c>/XRefStm</c> read started, which a header found at the digits past a run
    /// of signs stands for (<see cref="NumberRunsInto"/>): where that lies before the window, nothing is looked at, and
    /// it is given as it is. A look at the bytes before the place, in the window: nothing is read for it, nor
    /// allocated.
    /// </remarks>
    /// <param name="span">The bytes searched, and the byte before them where the window holds one.</param>
    /// <param name="from">Where the bytes searched start in <paramref name="span"/>.</param>
    /// <param name="at">
    /// Where the place starts in <paramref name="span"/>, within the bytes searched, or before <paramref name="span"/>, where
    /// it stands for the reading of the stream an <c>/XRefStm</c> read and the window does not hold where it started.
    /// </param>
    private static int HeaderTokenStart(ReadOnlySpan<byte> span, int from, int at)
    {
        var first = at;

        while (first > 0 && span[first - 1] is (byte)'+' or (byte)'-')
        {
            first--;
        }

        return first >= from && span[first] == (byte)'+' ? first : at;
    }

    /// <summary>
    /// Tells whether the search near an offset tries the place it found at <paramref name="candidate"/>, one the chain does
    /// not name, and whether a section found there is confirmed: before where the scan's memo is whole, a place is one the
    /// rebuild's scan for objects takes (<see cref="GetScanMemo"/>), so that the word <c>xref</c> or a header inside
    /// another object — in its stream data, or in a string or a comment —, or in a comment between objects, is no section,
    /// and is neither read nor counted toward <see cref="MaxRelocationCandidates"/>; at or past there, a place is tried
    /// without its word, and a section found there is unconfirmed.
    /// </summary>
    /// <remarks>
    /// The keyword is asked where it starts (<see cref="ScanMemo.TryGetXRefKeywordAt"/>), and a header where the scan
    /// places it, at the first sign of the run of signs its number is written with (<see cref="HeaderTokenStart"/>): a
    /// header counts where that sign lies within the bytes searched, so that one whose run starts before them, or with
    /// a <c>-</c>, which makes no object's number, is not tried (#327); the search places a header it tries at its
    /// digits all the same. A place the scan takes as the bytes give it, in the data of a stream whose length is not
    /// confirmed or in what a string that runs past its object runs into, is tried as any other, and a section found
    /// there is unconfirmed (<see cref="ScannedPlaceKind.Raw"/>). The memo holds every place the scan takes only before
    /// <see cref="ScanMemo.WholeUntil"/> — short of the places it could hold, and of where a window the source gave
    /// shorter than asked ended the scan —: a place at or past there is tried without its word, the guard reached where
    /// the memo left places out once the search tries it, within <see cref="MaxRelocationCandidates"/>, and not for a
    /// place that bound leaves untried (<see cref="ReachScanMemoBound"/>), and a section found there is unconfirmed. A
    /// place the chain names — what a hybrid table's <c>/XRefStm</c> read in the stream's place, which its
    /// <c>/Prev</c>'s search takes, and the place that <c>/Prev</c> names — is not asked: it is read as the chain reads
    /// what it names. Wherever the place is, one that cannot be read declares a section only where its token starts its
    /// line (<see cref="StartsItsLine"/>). The memo is made the first time a search asks it, once a document: a search
    /// whose places are passed over, or named by the chain, scans nothing.
    /// </remarks>
    /// <param name="span">The bytes searched, and the byte before them where the window holds one.</param>
    /// <param name="spanStart">Where <paramref name="span"/> starts in the file.</param>
    /// <param name="from">Where the bytes searched start in <paramref name="span"/>.</param>
    /// <param name="candidate">Where the place starts in the file, within the bytes searched.</param>
    private PlaceCheck CheckPlace(ReadOnlySpan<byte> span, long spanStart, int from, long candidate)
    {
        var at = (int)(candidate - spanStart);
        var keyword = span[at] == (byte)'x';
        var place = keyword ? candidate : spanStart + HeaderTokenStart(span, from, at);
        var memo = GetScanMemo();

        if (place >= memo.WholeUntil)
        {
            return PlaceCheck.PastMemo;
        }

        bool held;
        ScannedPlaceKind kind;

        if (keyword)
        {
            held = memo.TryGetXRefKeywordAt(place, out kind);
        }
        else
        {
            held = memo.TryGetHeaderAt(place, out var header);
            kind = header.Kind;
        }

        return !held ? PlaceCheck.Skipped : kind == ScannedPlaceKind.Raw ? PlaceCheck.Raw : PlaceCheck.Clean;
    }

    /// <summary>
    /// Determines whether the place the search near an offset tried at <paramref name="candidate"/> starts with a token that
    /// declares a section where it cannot be read, the keyword <c>xref</c> or an object's header, and whether a
    /// cross-reference stream it passes over there is reported (<see cref="ReportStreamsPassedOver"/>): one whose token
    /// starts its line as a load lexes it (<see cref="IsTokenOnItsLine"/>), from its first sign where its number is
    /// written with a run of signs that starts with a <c>+</c> (<see cref="HeaderTokenStart"/>), wherever it lies; where
    /// the line starts too far back to tell, one the scan for objects takes between objects
    /// (<see cref="PlaceCheck.Clean"/>).
    /// </summary>
    /// <remarks>
    /// That the scan takes the place does not answer it where the line can be lexed (<see cref="CheckPlace"/>): where the
    /// scan reads the bytes as they come — in the data of a stream whose length it cannot confirm, comments unread there,
    /// or past a string that runs past its object —, it takes the word or a header that a comment or a string on its line
    /// holds, and between objects it takes what follows an end-of-file marker <c>%%EOF</c> on its line, which a load lexes
    /// as part of that comment. Between objects, it sweeps every other comment and string as a load lexes them: its word
    /// stands where the line test cannot tell, for a place it takes there, and for no other.
    /// </remarks>
    /// <param name="check">What the scan's memo said of the place.</param>
    /// <param name="span">The bytes searched, and the byte before them where the window holds one.</param>
    /// <param name="spanStart">Where <paramref name="span"/> starts in the file.</param>
    /// <param name="from">Where the bytes searched start in <paramref name="span"/>.</param>
    /// <param name="candidate">Where the place starts in the file, within the bytes searched.</param>
    private bool StartsItsLine(PlaceCheck check, ReadOnlySpan<byte> span, long spanStart, int from, long candidate) =>
        IsTokenOnItsLine(span, spanStart, HeaderTokenStart(span, from, (int)(candidate - spanStart))) ??
        check == PlaceCheck.Clean;

    /// <summary>
    /// Determines whether a token starts at <paramref name="at"/> in <paramref name="span"/>, the line it lies in lexed from
    /// its start as a load of the line's own tokens reads it: not a word or a header a comment or a string on that line
    /// holds, nor digits a regular byte before them runs into (#281); null where the line starts too far back to tell.
    /// Asked of a place that cannot be read, and of a stream passed over, wherever the scan's memo places it
    /// (<see cref="StartsItsLine"/>).
    /// </summary>
    /// <remarks>
    /// The line starts past the last end of line before the place, or at the file's start. Where it starts before the bytes
    /// searched, the end of line before it is looked for within <see cref="LineStartSearchLength"/> bytes before them, read
    /// again with them as far as the source gives them, and the line is lexed from its start: a comment whose <c>%</c>, or a
    /// string whose <c>(</c>, lies before the bytes searched is seen. Where none lies within them, and the file starts
    /// farther back, the line test cannot tell. A place the bytes given end at or before declares nothing: a source that
    /// gives fewer bytes than asked is at the end of the file there (#52), and a token starts at the place only where its
    /// first byte is given, which, with the bytes before it, tells whether one does.
    /// Stream data, or a string, that an earlier line opened is not seen. The bytes lexed run from the line's start to the
    /// end of the first token that ends past the place — through the comment that holds it and the white space and comments
    /// after it, or to the end of the string that holds it —, within the bytes searched and those read back before them: at
    /// most <see cref="LineStartSearchLength"/> bytes more than the bytes searched, once for each such place, which
    /// <see cref="MaxRelocationCandidates"/> bounds, and one window more read at most for each.
    /// </remarks>
    /// <param name="span">The bytes searched.</param>
    /// <param name="spanStart">Where <paramref name="span"/> starts in the file.</param>
    /// <param name="at">
    /// Where the token a load reads the place from starts in <paramref name="span"/> (<see cref="HeaderTokenStart"/>);
    /// negative where it lies before <paramref name="span"/>, which declares nothing.
    /// </param>
    private bool? IsTokenOnItsLine(ReadOnlySpan<byte> span, long spanStart, int at)
    {
        // A place before the bytes the window holds — the stream an /XRefStm read from a sign, whose header the search
        // found at the digits past a run of signs the window does not hold (NumberRunsInto) — is not lexed: it declares
        // nothing, as a header whose number starts before the bytes searched counts for nothing (#197).
        if (at < 0)
        {
            return false;
        }

        var lineStart = span[..at].LastIndexOfAny((byte)'\r', (byte)'\n') + 1;

        if (lineStart > 0 || spanStart == 0)
        {
            return StartsToken(span, lineStart, at);
        }

        // The bytes searched are read again from where the line may start, to their end, which lies past the place as every
        // token that holds it does — where the source gives every byte asked for. Bytes given that end at or before the
        // place do not hold its first byte, whatever they hold of the stretch the end of line is looked for in: it declares
        // nothing, as past the bound.
        var from = Math.Max(0, spanStart - LineStartSearchLength);
        var before = (int)(spanStart - from);
        using var window = _source.GetWindow(from, before + span.Length);
        var bytes = window.Memory.Span;

        if (bytes.Length <= before + at)
        {
            return false;
        }

        var endOfLine = bytes[..before].LastIndexOfAny((byte)'\r', (byte)'\n');

        if (endOfLine < 0 && from > 0)
        {
            return null;
        }

        return StartsToken(bytes, endOfLine + 1, before + at);
    }

    /// <summary>
    /// Determines whether a token starts at <paramref name="at"/> in <paramref name="span"/>, lexed from
    /// <paramref name="lineStart"/>, where its line starts (<see cref="IsTokenOnItsLine"/>).
    /// </summary>
    private static bool StartsToken(ReadOnlySpan<byte> span, int lineStart, int at)
    {
        // The bytes lexed run past the place, as every token that holds it does; the end of the input stops the lexing all
        // the same, and starts no token, so that no length the bytes come in keeps the loop going (invariant 4).
        var lexer = new PdfLexer(span, lineStart);
        var token = lexer.Read();

        while (token.End <= at && token.Kind != PdfTokenKind.EndOfInput)
        {
            token = lexer.Read();
        }

        return token.Kind != PdfTokenKind.EndOfInput && token.Start == at;
    }

    /// <summary>
    /// Records what the index holds, for a place the search tries and may drop (<see cref="RollBackIndex"/>): the reading of
    /// the place is a tentative one of the index, and the numbers it refuses are kept apart as it refuses them.
    /// </summary>
    private IndexMark MarkIndex()
    {
        _xref.StartTentative();
        _refusedOrder = [];
        return new(_refusedOrder, _indexIncomplete, _cutByGuard);
    }

    /// <summary>
    /// Gives back what the reading of a place the search dropped added to the index: its rows, the numbers it refused, with
    /// the room they took under <see cref="PdfReaderLimits.MaxIndexEntries"/>, and the index marked incomplete for them —
    /// what the place read is no section of the file's, and stands for no row of the one the search takes next (#320). A
    /// guard the reading reached stands, as what a dropped place's decoding reached does (#221): where it cut the chain, the
    /// index stays marked incomplete with it.
    /// </summary>
    private void RollBackIndex(IndexMark mark)
    {
        _xref.DropTentative();

        foreach (var number in mark.Refused)
        {
            _refusedNumbers!.Remove(number);
        }

        if (_cutByGuard == mark.CutByGuard)
        {
            _indexIncomplete = mark.Incomplete;
        }
    }

    /// <summary>
    /// What the reading of a place refused, whether the index was incomplete before it, and whether a guard had cut the chain.
    /// </summary>
    private readonly record struct IndexMark(List<int> Refused, bool Incomplete, bool CutByGuard);

    /// <summary>What the scan's memo says of a place the search near an offset found (<see cref="CheckPlace"/>).</summary>
    private enum PlaceCheck
    {
        /// <summary>The scan does not take it: it is no section, and is not tried.</summary>
        Skipped,

        /// <summary>The scan takes it between objects.</summary>
        Clean,

        /// <summary>The scan takes it as the bytes give it: a section found there is unconfirmed.</summary>
        Raw,

        /// <summary>It lies past the memo's places: tried without the scan's word, a section found there unconfirmed.</summary>
        PastMemo,

        /// <summary>The chain names it: tried without asking the memo.</summary>
        Named,
    }

    /// <summary>
    /// Collects where a cross-reference section could start in <paramref name="span"/>: an <c>xref</c> keyword
    /// standing on its own — not the end of <c>startxref</c> —, and every object header.
    /// </summary>
    /// <param name="span">The bytes searched.</param>
    /// <param name="start">Where <paramref name="span"/> starts in the file.</param>
    /// <param name="from">
    /// Where in <paramref name="span"/> a place may start: the bytes before it are read only to tell a keyword or a header
    /// that starts there from a longer token, or a longer number, that runs into it (#197).
    /// </param>
    /// <param name="to">
    /// Where in <paramref name="span"/> a place's keyword must end: the byte after it, which tells a keyword from a longer
    /// token, is read past it where the file holds one.
    /// </param>
    /// <param name="candidates">Receives where each place starts in the file.</param>
    private static void FindSectionCandidates(ReadOnlySpan<byte> span, long start, int from, int to, List<long> candidates)
    {
        var searchFrom = 0;

        while (searchFrom < to)
        {
            var index = span[searchFrom..to].IndexOf("xref"u8);
            if (index < 0)
            {
                break;
            }

            var position = searchFrom + index;
            var end = position + 4;

            if (position >= from &&
                (position == 0 || !PdfCharacters.IsRegular(span[position - 1])) &&
                (end == span.Length || !PdfCharacters.IsRegular(span[end])))
            {
                candidates.Add(start + position);
            }

            searchFrom = end;
        }

        searchFrom = 0;

        while (searchFrom < to)
        {
            var index = span[searchFrom..to].IndexOf(ObjKeyword);
            if (index < 0)
            {
                break;
            }

            var position = searchFrom + index;

            // A header whose number is written with signs is placed where the walk met its number — at its digits, or at its
            // first sign where the walk lexed its line —, where the search tells it from a section the chain reached
            // (NumberRunsInto, DigitsPastSigns); a load reads it from either byte. A run a minus starts is no header (#327).
            if (ReadHeaderBackwards(span, position, startsData: true, signsBeforeUnknown: true, out _, out _, out _, out var found) == HeaderWalk.Header &&
                found >= from)
            {
                candidates.Add(start + found);
            }

            searchFrom = position + ObjKeyword.Length;
        }
    }
}
