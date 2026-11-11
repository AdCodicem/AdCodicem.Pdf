using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO.XRef;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO;

internal sealed partial class PdfFileReader
{
    /// <summary>
    /// Rebuilds the index by scanning the whole file for object headers, keeping the last definition of
    /// each object number under the generation its header gives, and then looking for a trailer and for anything
    /// that can act as one.
    /// </summary>
    private void Repair()
    {
        if (_repaired)
        {
            return;
        }

        _repaired = true;
        _diagnostics.Repair(PdfDiagnosticCodes.XRefRebuilt, "The cross-reference index was rebuilt by scanning the file.");

        PreserveChainIndex();

        // What the rebuild sets in the trailer and merges into it — the file's /Root again for a search's guess, the
        // trailers the scan finds, a catalog it searches for — goes into a copy: a trailer a caller took before is left as
        // it was, and the next one taken is the rebuilt one (#167).
        _xref.RenewTrailer();

        // A catalog a search took is the reader's guess, which the trailer's /Root holds in place of the file's own: the
        // rebuilt index is read with the file's, which it may now serve as a catalog — whole, or cut by a guard and taken
        // as read —, and searched again only when it does not (#175).
        if (_catalogFoundAs != 0 && Trailer.GetRaw(PdfName.Root) is PdfReference guess && guess.Id.Number == _catalogFoundAs)
        {
            if (_structure.RootAsWritten is { } written)
            {
                Trailer.Set(PdfName.Root, written);
            }
            else
            {
                Trailer.Remove(PdfName.Root);
            }

            _catalogFoundAs = 0;
        }

        // What a chain a guard cut kept is kept through a rebuild that damage set off: the scan finds every header the file
        // holds, the objects the guard hid included, but not every entry the chain gave — an object stream's members it has
        // not expanded, a header it cannot read.
        Rebuild(_cutByGuard ? _chainIndex : null);
    }

    /// <summary>
    /// Runs a rebuild from its scan: empties the index, indexes the object headers and the trailers the whole file holds,
    /// and finishes the rebuild from what the scan indexed — at once, or, loads being in flight, once the outermost returns.
    /// </summary>
    /// <remarks>
    /// The source may fail to give its bytes in the scan, or in the finish run here — the one the start of a validation
    /// runs, or the document's opening —: what the rebuild reported since its scan began is withdrawn, the rebuild is left
    /// for the next read or validation to run again from the scan's start, and the failure leaves (#167). Run again from the
    /// scan rather than from a copy of the scan's index, a finish the source fails costs a sound rebuild no copy, which would
    /// take 40 bytes an entry; a finish the outermost load runs starts from the copy its loads in flight keep. A guard
    /// reached is no failure: a document opened to throw on one throws once the index is whole.
    /// </remarks>
    /// <param name="kept">The entries a chain a guard cut gave, kept through the rebuild; null when no guard cut it.</param>
    private void Rebuild(PdfXRefTable? kept)
    {
        // Taken before the scan reports anything, the mark withdraws what a rebuild the source fails reported, for the rebuild
        // run again to report it again; brought past the scan, it marks what the loads in flight report. What was reported
        // is so copied once a rebuild (#273).
        var reports = MarkReports();
        var indexFullAt = _indexFullAt;
        _indexFull = false;
        _xref.Clear();
        ForgetWhatWasRead();
        var redefinitions = new Redefinitions();

        try
        {
            // Indexing and loading objects can reach a guard; the scan for trailers, which loads nothing, cannot. A document
            // opened to throw on one throws once the index is whole, not half-way through rebuilding it: one abandoned mid-way
            // would leave the document unable to find the objects it had not reached. The first guard reached is the one
            // thrown.
            var reached = ScanForObjects(redefinitions);
            ScanForTrailers();

            // A rebuild a load sets off runs with that load, and those it is part of, still in flight: the objects they are
            // loading read as null meanwhile, and so would the values an object stream among them needs, so that expanding
            // the object streams now would expand such a stream without them, or not at all. The loads in flight read
            // through the scan's index, and the rebuild is finished from it once the outermost returns, or throws, as a
            // rebuild run before them would have been: a member is not lost to the load that set the rebuild off, nor read
            // otherwise than had the rebuild run first, and the object streams are expanded once. A guard the scan reached is
            // the first this read reaches: it is thrown now, and the outermost load finishes the index before it lets the
            // exception go.
            if (_loading.Count > 0 || _objectStreamsLoading.Count > 0)
            {
                _unfinishedRebuild = new RebuildAwaitingFinish(
                    _xref.CopyEntries(), redefinitions, kept, _indexFull, _indexFullAt, BringForward(reports));
                reached?.Throw();
                return;
            }

            FinishRebuild(redefinitions, kept, reached);
        }
        catch (Exception exception) when (exception is not PdfLimitExceededException)
        {
            // The rebuild stops where the source failed it: the index holds what it had reached, and the trailer what the scan
            // had merged, until the rebuild is run again from its scan, into a copy of that trailer. Where the index filled is
            // forgotten, for the scan run again to reach it and report it.
            WithdrawReportsSince(reports);
            _indexFullAt = indexFullAt;
            _unfinishedRebuild = new RebuildAwaitingScan(kept);
            throw;
        }
    }

    /// <summary>
    /// Lets go of what the reader kept of the objects it read, and of what it noted of them, for an index a rebuild changes:
    /// each is read again, and noted again, from the rebuilt index.
    /// </summary>
    private void ForgetWhatWasRead()
    {
        _cache.Clear();
        _cacheOrder.Clear();
        _objectStreams.Clear();
        _objectStreamsHiddenByGuard.Clear();
        _objectStreamOrder.Clear();
        _objectStreamBytes = 0;

        // What read as null while loads were in flight was theirs, not the file's. What was noted of each object as it was
        // read is noted again as each is read from the rebuilt index — every object written directly in the file is, as the
        // rebuild is finished —: kept, a note of a definition the rebuilt index no longer serves, or of a number it no longer
        // holds, would be read as the note of the one it serves. What could not be produced is noted again as each number is
        // resolved, which every caller of GetPresence does first: cleared, the note is true before that too.
        _nullWhileDecoding.Clear();
        _unproduced.Clear();
        _cutAtLimit.Clear();
        _endObjMissing.Clear();
        _streamLengthFaults.Clear();
    }

    /// <summary>
    /// Finishes a rebuild from the index its scan made: takes in what the object streams hold, keeps the entries a chain a
    /// guard cut gave that the scan did not find, reports the definitions met again, and looks for a catalog when
    /// <c>/Root</c> leads to none.
    /// </summary>
    /// <exception cref="PdfLimitExceededException">
    /// A guard was reached, by the scan or here, and the document was opened to throw on one: the first reached.
    /// </exception>
    private void FinishRebuild(Redefinitions redefinitions, PdfXRefTable? kept, ExceptionDispatchInfo? reached)
    {
        var expanding = ExpandObjectStreams(redefinitions);
        reached ??= expanding;

        if (kept is not null)
        {
            var merging = MergeChainIndex(kept);
            reached ??= merging;
        }

        var hiding = KeepMembersHiddenByGuard();
        reached ??= hiding;

        if (redefinitions.Count > 0)
        {
            // An updated file defines its changed objects again, so this is what most rebuilds meet: worth knowing,
            // once, rather than a report for each number.
            _diagnostics.Add(PdfDiagnosticSeverity.Information, PdfDiagnosticCodes.ObjectRedefined, redefinitions.Describe());
        }

        // The file's /Root, a search's guess having been set aside as the rebuild began, is read through the rebuilt index;
        // a guard reached reading it is held, as any reached in a rebuild, and it is then a /Root a guard cut.
        bool usable;

        try
        {
            usable = HasUsableRoot();
        }
        catch (PdfLimitExceededException exception)
        {
            reached ??= ExceptionDispatchInfo.Capture(exception);
            usable = true;
        }

        // A /Root a guard cut, or left out of the rebuilt index, is taken as read, and no other is looked for. Searched even
        // when the expansion reached a guard; the first guard reached is the one thrown.
        if (!usable && !RootCutByGuard() && !RootLeftOutByGuard())
        {
            var searching = FindCatalog();
            reached ??= searching;
        }

        reached?.Throw();
    }

    /// <summary>
    /// Finishes the rebuild a load set off, now that no load is in flight, or one the source failed: the index is brought
    /// back to what the scan made, what was read meanwhile let go, and the rebuild finished as one run before the load
    /// would have been; or, the source having failed the scan, or a finish run with no load in flight, the rebuild is run
    /// again from its scan. A finish that fails — the source failing to give its bytes — leaves the rebuild to be finished
    /// again, from the scan's index, by the next read or validation; a rebuild run again from its scan, to be run so again.
    /// </summary>
    /// <exception cref="PdfLimitExceededException">
    /// A guard was reached in finishing, and the document was opened to throw on one; the index is finished.
    /// </exception>
    private void FinishUnfinishedRebuild()
    {
        var unfinished = _unfinishedRebuild!;
        _unfinishedRebuild = null;

        // A rebuild the source failed, but in a finish the outermost load ran, is run again from its scan's start, into a
        // copy of the trailer the scan merged into: a caller may have taken that one meanwhile (#167). The trailers it merged
        // are merged again, each key from the newest trailer that holds it, as they were: what it merged stands as the scan
        // run whole merges it.
        if (unfinished is not RebuildAwaitingFinish finish)
        {
            _xref.RenewTrailer();
            Rebuild(unfinished.Kept);
            return;
        }

        // What the loads in flight reported, they read through the scan's index and with each other's objects reading as
        // null: it is withdrawn, and reported again as far as the finished index's own reading reports it, as a rebuild run
        // first does (#273). Once: what is reported after a finish the source failed, before the next, stays.
        if (finish.Reports is { } reports)
        {
            WithdrawReportsSince(reports);
        }

        // A finish the source fails withdraws what it reported, which the finish run again reports. Withdrawn, the reports
        // are what the in-flight mark holds, which the withdrawal leaves as it was: it marks the finish too.
        var attempt = finish.Reports ?? MarkReports();

        _xref.Clear();

        // A catalog the finish searches for goes into a copy of the trailer too: a caller may have taken it between the
        // read that left the rebuild unfinished, its finish failed by the source, and this one (#167).
        _xref.RenewTrailer();

        foreach (var (number, entry) in finish.Scanned)
        {
            _xref.Set(number, entry);
        }

        _indexFull = finish.IndexFull;
        _indexFullAt = finish.IndexFullAt;
        ForgetWhatWasRead();

        try
        {
            FinishRebuild(finish.Redefinitions.Copy(), finish.Kept, reached: null);
        }
        catch (Exception exception) when (exception is not PdfLimitExceededException)
        {
            WithdrawReportsSince(attempt);
            _unfinishedRebuild = finish with { Reports = null };
            throw;
        }
    }

    /// <summary>
    /// Finishes the rebuild a load set off while that load's exception leaves it, which stands for the read: a guard the
    /// finish reaches afterward is left, the first guard reached being the one thrown, and a finish the source fails,
    /// whatever it throws, leaves the rebuild to the next read or validation.
    /// </summary>
    private void FinishUnfinishedRebuildAfterFailure()
    {
        try
        {
            FinishUnfinishedRebuild();
        }
        catch (PdfLimitExceededException)
        {
            // Reached after the read's own exception, which is the one thrown.
        }
        catch (Exception)
        {
            // The source failed the finish, whatever it threw — an I/O error, or what a caller's own source throws —, as the
            // rebuild's other steps take it: the rebuild is left unfinished, and finished by the next read or validation; the
            // read's own exception is the one thrown.
        }
    }

    /// <summary>
    /// Records where the document's reports stand, and what it holds as reported once, for
    /// <see cref="WithdrawReportsSince"/>. It copies what it records: it is taken as a rebuild's scan begins — brought past
    /// the scan to mark the loads in flight and their finish (<see cref="BringForward"/>) —, and as each finish after one the
    /// source failed begins: once a document, and once more for each rebuild or finish the source fails.
    /// </summary>
    private ReportsMark MarkReports() => new(
        _diagnostics.GetMarkWithDecodingReports(),
        new HashSet<long>(_lengthFaultsReported, SeededComparer.Instance),
        new HashSet<PdfDiagnostic>(_syntaxReported),
        new HashSet<(PdfLimit Limit, long Position)>(_limitsReached, SeededComparer.Instance),
        new HashSet<(long DataStart, int Number)>(_memberLimitsReached, SeededComparer.Instance),
        new HashSet<int>(_objectStreamsNeedingThemselves, SeededComparer.Instance),
        _nestingReported,
        _objectStreamReports.Copy());

    /// <summary>
    /// Brings <paramref name="mark"/> to where the document's reports stand now, for a mark nothing was withdrawn past
    /// since, so that what was reported since was only added: the copies it holds take in what was added, rather than
    /// being taken whole again — a scan reports little, while what the document had reported may be much (#273). The
    /// mark's copies are changed, and the mark given back with where the diagnostics stand now.
    /// </summary>
    private ReportsMark BringForward(ReportsMark mark)
    {
        mark.LengthFaults.UnionWith(_lengthFaultsReported);
        mark.Syntax.UnionWith(_syntaxReported);
        mark.Limits.UnionWith(_limitsReached);
        mark.MemberLimits.UnionWith(_memberLimitsReached);
        mark.NeedingThemselves.UnionWith(_objectStreamsNeedingThemselves);
        mark.ObjectStreams.TakeIn(_objectStreamReports);
        return mark with { Diagnostics = _diagnostics.GetMarkWithDecodingReports(), NestingReported = _nestingReported };
    }

    /// <summary>
    /// Withdraws what the document reported since <paramref name="mark"/>, and forgets that it was reported: what is read
    /// again reports it again.
    /// </summary>
    private void WithdrawReportsSince(ReportsMark mark)
    {
        _diagnostics.RollBack(mark.Diagnostics);
        Restore(_lengthFaultsReported, mark.LengthFaults);
        Restore(_syntaxReported, mark.Syntax);
        Restore(_limitsReached, mark.Limits);
        Restore(_memberLimitsReached, mark.MemberLimits);
        Restore(_objectStreamsNeedingThemselves, mark.NeedingThemselves);
        _nestingReported = mark.NestingReported;
        _objectStreamReports.RestoreFrom(mark.ObjectStreams);
    }

    /// <summary>Brings <paramref name="set"/> back to what <paramref name="copy"/> holds.</summary>
    private static void Restore<T>(HashSet<T> set, HashSet<T> copy)
    {
        set.Clear();
        set.UnionWith(copy);
    }

    /// <summary>
    /// Indexes every object header the file holds, read once each, to its end: the last definition of a number wins, under
    /// the generation its header gives.
    /// </summary>
    /// <remarks>
    /// The index takes object numbers as the file gives them, as many as <see cref="PdfReaderLimits.MaxIndexEntries"/>
    /// lets it; past that, a header for a number it holds still replaces its entry, so that the newest definition wins
    /// whatever came before it, and one for a number it lacks is left out. A number defined again takes no room, so a file
    /// that repeats one header millions of times hides nothing behind them. The scan's time follows the file's length, and
    /// what it keeps the index's bound.
    /// </remarks>
    /// <returns>The exception a document opened to throw asked for, held until the index is whole; or null.</returns>
    private ExceptionDispatchInfo? ScanForObjects(Redefinitions redefinitions)
    {
        var position = 0L;
        ExceptionDispatchInfo? reached = null;

        while (position < _source.Length)
        {
            var length = (int)Math.Min(ScanChunkSize, _source.Length - position);
            using var window = _source.GetWindow(position, length);
            var span = window.Memory.Span;

            // An obj keyword wholly within the overlap was found by the window before, which held it and what precedes
            // it: each header is read once.
            var searchFrom = position == 0 ? 0 : ScanOverlap - ObjKeyword.Length + 1;

            while (searchFrom < span.Length)
            {
                var index = span[searchFrom..].IndexOf(ObjKeyword);
                if (index < 0)
                {
                    break;
                }

                var objPosition = searchFrom + index;

                if (TryReadHeaderBackwards(span, objPosition, out var number, out var generation, out var headerStart))
                {
                    // The last definition wins: that is what an incrementally updated file means. Its entry records
                    // the generation its header gives, whatever an earlier definition gave (#118). A number the index
                    // lacks takes room in it, which it may have no more of.
                    var known = _xref.TryGet(number, out _);

                    if (known)
                    {
                        redefinitions.Add(number);
                    }

                    if (known || HasRoomInRebuild(position + headerStart, ref reached))
                    {
                        _xref.Set(number, XRefEntry.Found(position + headerStart - _headerOffset, generation));
                    }
                }

                searchFrom = objPosition + ObjKeyword.Length;
            }

            if (position + length >= _source.Length)
            {
                break;
            }

            position += length - ScanOverlap;
        }

        return reached;
    }

    /// <summary>
    /// Takes into a rebuilt index the entries in use of <paramref name="chain"/>, the index a chain a guard cut gave, for the
    /// numbers the rebuild did not find: what the file defines last, and the scan found, wins.
    /// </summary>
    /// <returns>The exception a document opened to throw asked for, held until the index is whole; or null.</returns>
    private ExceptionDispatchInfo? MergeChainIndex(PdfXRefTable chain)
    {
        ExceptionDispatchInfo? reached = null;

        // A chain a guard cut is kept only when it was read, its first section at least.
        var position = _structure.Sections[0].Offset;

        foreach (var (number, entry) in chain)
        {
            if (entry.Kind == XRefEntryKind.Free || _xref.TryGet(number, out _))
            {
                continue;
            }

            if (HasRoomInRebuild(position, ref reached))
            {
                _xref.Set(number, entry);
            }
        }

        return reached;
    }

    /// <summary>
    /// Takes into a rebuilt index the members the chain's index places in an object stream a guard keeps from serving them,
    /// for the numbers the rebuild did not find: no header of theirs lies in the file, nor one the stream gives, and they
    /// are unread for the guard's reasons, as before the rebuild — a catalog among them is taken as read (#259).
    /// </summary>
    /// <returns>The exception a document opened to throw asked for, held until the index is whole; or null.</returns>
    private ExceptionDispatchInfo? KeepMembersHiddenByGuard()
    {
        ExceptionDispatchInfo? reached = null;

        if (_objectStreamsHiddenByGuard.Count == 0 || _chainIndex is not { } chain)
        {
            return reached;
        }

        foreach (var (number, entry) in chain)
        {
            if (entry.Kind != XRefEntryKind.Compressed ||
                !_objectStreamsHiddenByGuard.Contains(entry.ObjectStreamNumber) ||
                _xref.TryGet(number, out _))
            {
                continue;
            }

            var position = _xref.TryGet(entry.ObjectStreamNumber, out var stream) ? PositionOf(stream) : 0;

            if (HasRoomInRebuild(position, ref reached))
            {
                _xref.Set(number, entry);
            }
        }

        return reached;
    }

    /// <summary>
    /// Determines whether a rebuilt index has room for one more object number, met at <paramref name="position"/>; when it
    /// has none, reports <see cref="PdfReaderLimits.MaxIndexEntries"/> the first time, and holds the exception a document
    /// opened to throw asked for in <paramref name="reached"/>, unless one is held already: the rebuild finishes first.
    /// </summary>
    private bool HasRoomInRebuild(long position, ref ExceptionDispatchInfo? reached)
    {
        if (IndexHasRoom)
        {
            return true;
        }

        try
        {
            ReachIndexBound(position);
        }
        catch (PdfLimitExceededException exception)
        {
            reached ??= ExceptionDispatchInfo.Capture(exception);
        }

        return false;
    }

    /// <summary>
    /// Merges into the trailer the dictionary that follows each <c>trailer</c> keyword the file holds, the newest first, so
    /// that its keys win.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What follows a keyword is parsed as the dictionary alone, and nothing is loaded (#171): a stream it introduces gives
    /// its dictionary as it was read, neither its data nor what its <c>/Length</c> names being read; a stream nested in the
    /// dictionary, which no file may write there, ends the parse at its <c>stream</c> keyword, the key that holds it and the
    /// keys after it not merged, and this is noted; a reference in the dictionary's place is none, is not followed, and is
    /// noted; any other value there is none too, and what the parse met of it is all that is noted; and the references the
    /// dictionary holds are read only as the trailer's keys that hold them are. The scan so reaches no guard, and merges
    /// the same trailers whatever reads are in flight as it runs — the object one is loading reads as null meanwhile — and
    /// whether the document was opened to throw on a guard or not.
    /// </para>
    /// <para>
    /// A nested stream ends the parse only within the nesting the parser follows: past its 128 levels, or where the stack
    /// probe cuts sooner, the container the stream lies in or follows is skipped as any other is, which is noted as too
    /// deep (#163). A <c>stream</c> keyword left after it is a stray token to the container around it — a key a
    /// dictionary refuses, an element an array reads as null —, and what follows it is read on as that container's keys
    /// or elements, and merged (#171).
    /// </para>
    /// <para>
    /// What the scan notes goes with the faults the parse meets: kept once, though two windows of the scan meet a keyword
    /// that lies where they overlap, and withdrawn with what the rebuild reported when the source fails it, for the rebuild
    /// run again to note it again. The bytes <c>trailer</c> anywhere — a comment, a string, a stream's data, the end of a
    /// name — are still taken for a trailer's keyword, and every key the parse gives of the dictionary after them merged,
    /// not only the keys a trailer defines (#171).
    /// </para>
    /// </remarks>
    private void ScanForTrailers()
    {
        var positions = new List<long>();
        var position = 0L;

        while (position < _source.Length)
        {
            var length = (int)Math.Min(ScanChunkSize, _source.Length - position);
            using var window = _source.GetWindow(position, length);
            var span = window.Memory.Span;
            var searchFrom = 0;

            while (searchFrom < span.Length)
            {
                var index = span[searchFrom..].IndexOf(TrailerKeyword);
                if (index < 0)
                {
                    break;
                }

                positions.Add(position + searchFrom + index + TrailerKeyword.Length);
                searchFrom += index + TrailerKeyword.Length;
            }

            if (position + length >= _source.Length)
            {
                break;
            }

            position += length - ScanOverlap;
        }

        // The newest trailer is the last one in the file, and its keys must win.
        for (var i = positions.Count - 1; i >= 0; i--)
        {
            // A trailer longer than the window is cut at its edge, which is not the file's end (#49): what the edge cuts
            // short is not reported. A window shorter than asked holds the end of the data, as every window does. One the
            // chain read already is not reported again.
            using var window = _source.GetWindow(positions[i], XRefWindow);
            var cut = window.Length == XRefWindow && positions[i] + window.Length < _source.Length;
            var mark = _pending.GetMark();

            try
            {
                // No stream data is read: the file says where a token the window's edge reaches ends, as it does to a load.
                // The white space and comments after the keyword are skipped once, for the parse to start past them and for
                // a note to name where its value starts.
                var parser = new PdfObjectParser(
                    window.Memory, positions[i], this, _pending, endsData: !cut, fileEdge: this, readsStreams: false);
                var keyword = positions[i] - TrailerKeyword.Length;
                var valueStart = ValueStart(window.Memory.Span);
                parser.Position = valueStart;
                var value = parser.ParseObject();

                if (value is PdfDictionary trailer)
                {
                    // A stream inside the dictionary ended the parse at its keyword, the dictionary with it; one after the
                    // dictionary ends nothing of it. Noted with the parse's own faults, the note is kept once, as they are.
                    // Hostile data can hold a keyword every few bytes: the offset is quoted only for a note that is kept.
                    if (parser.StreamStopDepth > 0)
                    {
                        _pending.Warn(
                            PdfDiagnosticCodes.SyntaxUnexpectedToken,
                            _pending.IsFull
                                ? "A stream inside the dictionary after a trailer keyword ends what the rebuild merges of it: the key that holds it and the keys after it are not merged."
                                : string.Create(
                                    CultureInfo.InvariantCulture,
                                    $"A stream inside the dictionary after the trailer keyword at offset {keyword} ends what the rebuild merges of it: the key that holds it and the keys after it are not merged."),
                            positions[i] + parser.StreamStop);
                    }

                    _xref.MergeTrailer(trailer);

                    if (_rebuiltTrailerPosition < 0)
                    {
                        _rebuiltTrailerPosition = keyword;
                    }
                }
                else if (value is PdfReference reference)
                {
                    // The reference is the value the parse began with, past the white space and comments after the keyword.
                    // The offset and the number are quoted only for a note that is kept, as the stream's are.
                    _pending.Warn(
                        PdfDiagnosticCodes.SyntaxUnexpectedToken,
                        _pending.IsFull
                            ? "A trailer keyword is followed by a reference, not by a dictionary; the rebuild does not follow it."
                            : string.Create(
                                CultureInfo.InvariantCulture,
                                $"The trailer keyword at offset {keyword} is followed by a reference to object {reference.Id.Number}, not by a dictionary; the rebuild does not follow it."),
                        positions[i] + valueStart);
                }

                KeepPending(mark);
            }
            finally
            {
                _pending.RollBack(mark);
            }
        }
    }

    /// <summary>
    /// Gets where the value after a trailer keyword starts in <paramref name="data"/>, the bytes that follow the keyword:
    /// past the white space and comments there.
    /// </summary>
    /// <remarks>
    /// Kept out of <see cref="ScanForTrailers"/>, which calls it once a keyword: inlined there, the lexer's loop over a
    /// comment was compiled with the whole scan once tiered PGO recompiled it, and ran about a third slower —
    /// <c>TrailerScanBenchmarks</c>, under a medium job, opened past 12,000 keywords sharing long comments in 344 to 355 ms
    /// over a reference or a null, against 211 to 220 ms with the loop in a frame of its own, and alike with PGO off.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ValueStart(ReadOnlySpan<byte> data)
    {
        var lexer = new PdfLexer(data);
        lexer.SkipWhitespaceAndComments();
        return lexer.Position;
    }

    private ExceptionDispatchInfo? ExpandObjectStreams(Redefinitions redefinitions)
    {
        _expandingObjectStreams = true;

        try
        {
            return ExpandEachObjectStream(redefinitions);
        }
        finally
        {
            _expandingObjectStreams = false;
        }
    }

    private ExceptionDispatchInfo? ExpandEachObjectStream(Redefinitions redefinitions)
    {
        var numbers = _xref.CopyNumbers();
        ExceptionDispatchInfo? reached = null;

        foreach (var number in numbers)
        {
            if (!_xref.TryGet(number, out var entry) || entry.Kind != XRefEntryKind.Regular)
            {
                continue;
            }

            ObjectStreamContents? contents;
            var reentries = _reentries;

            try
            {
                var loaded = GetObject(new PdfObjectId(number));

                if (loaded is not PdfStream stream)
                {
                    // An object a guard cut short of a stream may be an object stream the chain's index places members in.
                    if (IsCutAtLimit(number))
                    {
                        _objectStreamsHiddenByGuard.Add(number);
                    }

                    continue;
                }

                if (!stream.Dictionary.IsOfType(PdfName.ObjStm))
                {
                    continue;
                }

                contents = ObjectStreamContents.TryCreate(number, stream, _diagnostics, _objectStreamReports, out var hidden);

                if (contents is null && hidden)
                {
                    _objectStreamsHiddenByGuard.Add(number);
                }
            }
            catch (PdfLimitExceededException exception)
            {
                // A guard reached loading or decoding the object hides it, as one reached without throwing would.
                reached ??= ExceptionDispatchInfo.Capture(exception);
                _objectStreamsHiddenByGuard.Add(number);
                continue;
            }

            if (contents is null)
            {
                continue;
            }

            // Contents decoded without an object the index did not hold yet are decoded again when first asked for.
            if (reentries == _reentries)
            {
                KeepObjectStream(number, contents);
            }

            for (var index = 0; index < contents.Count; index++)
            {
                // An object written directly in the file wins over a copy inside an object stream, and the first
                // copy listed in the object streams read wins over the others. A member the index lacks takes room in
                // it, which it may have no more of.
                if (_xref.TryGet(contents.NumberAt(index), out _))
                {
                    redefinitions.Add(contents.NumberAt(index));
                }
                else if (HasRoomInRebuild(PositionOf(entry), ref reached))
                {
                    _xref.Set(contents.NumberAt(index), XRefEntry.Compressed(number, index));
                }
            }
        }

        return reached;
    }

    /// <summary>
    /// Looks for an object of <c>/Type /Catalog</c> among the indexed ones, and makes the first it finds the
    /// trailer's <c>/Root</c>.
    /// </summary>
    /// <remarks>
    /// It runs on the index the chain gave, and on a rebuilt one. Loading a candidate from the chain's index can
    /// rebuild it — the candidate's entry being broken —, and the rebuild runs a search of its own: the search it
    /// interrupted stops there, over the numbers it copied before it began.
    /// </remarks>
    private ExceptionDispatchInfo? FindCatalog()
    {
        ExceptionDispatchInfo? reached = null;
        var rebuilt = _repaired;
        var numbers = _xref.CopyNumbers();

        foreach (var number in numbers)
        {
            bool isCatalog;

            // A /Type written as a reference is read as the candidate is, and may reach a guard as its read may: the search
            // goes on, and the first guard reached is the one thrown once it ends.
            try
            {
                isCatalog = GetObject(new PdfObjectId(number)).AsDictionary() is { } dictionary && dictionary.IsOfType(PdfName.Catalog);
            }
            catch (PdfLimitExceededException exception)
            {
                reached ??= ExceptionDispatchInfo.Capture(exception);
                continue;
            }

            if (_repaired != rebuilt)
            {
                break;
            }

            if (isCatalog)
            {
                // The reference names the catalog as its entry does, read once the catalog is — a relocation on the way
                // gives the header's generation —: an entry of 1 1 makes 1 1 R, not 1 0 R (#118).
                var generation = _xref.TryGet(number, out var entry) && entry.Kind == XRefEntryKind.Regular ? entry.Generation : 0;
                Trailer.Set(PdfName.Root, new PdfReference(new PdfObjectId(number, generation), this));
                _catalogFoundAs = number;

                // The file's structure is what the document opened with: the catalog a rebuild run since took is the reader's.
                if (!_opened)
                {
                    _structure.CatalogFoundAs = number;
                }

                // A rebuild after opening sets aside the catalog a search took, and finds the same object again: said once.
                // One that takes another object says so.
                if (number != _catalogReportedAs)
                {
                    _catalogReportedAs = number;
                    _diagnostics.Repair(
                        PdfDiagnosticCodes.TrailerRootRecovered,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"The trailer's /Root does not lead to a document catalog; the catalog was found as object {number}."));
                }

                break;
            }
        }

        return reached;
    }

    /// <summary>
    /// Copies the index the chain gave before the reader changes it, so that the validation rules still judge the
    /// file's own, and the searches for <c>endstream</c> are still bounded by it. Once is enough: the copy is taken
    /// before the first change.
    /// </summary>
    private void PreserveChainIndex()
    {
        if (_structure.ChainRead && _chainIndex is null)
        {
            _chainIndex = _xref.CopyEntries();

            // The copy bounds the searches from now on, and sorts its own offsets when one asks.
            _xref.ForgetSortedOffsets();
        }
    }

    /// <summary>A rebuild left unfinished, which the next read or validation finishes (#167).</summary>
    /// <param name="Kept">The entries a chain a guard cut gave, kept through the rebuild; null when no guard cut it.</param>
    private abstract record UnfinishedRebuild(PdfXRefTable? Kept);

    /// <summary>
    /// What a rebuild a load set off needs to be finished from the index its scan made, once no load is in flight, and
    /// finished again when the source fails that finish.
    /// </summary>
    /// <param name="Scanned">The entries the scan made, before the object streams were expanded.</param>
    /// <param name="Redefinitions">The definitions the scan met again.</param>
    /// <param name="Kept">The entries a chain a guard cut gave, kept through the rebuild; null when no guard cut it.</param>
    /// <param name="IndexFull">Whether the scan filled the index.</param>
    /// <param name="IndexFullAt">Where the index first had no room for a number, or -1.</param>
    /// <param name="Reports">
    /// What the document had reported once the scan was done, which the finish brings its reports back to; null once a
    /// finish has.
    /// </param>
    private sealed record RebuildAwaitingFinish(
        PdfXRefTable Scanned,
        Redefinitions Redefinitions,
        PdfXRefTable? Kept,
        bool IndexFull,
        long IndexFullAt,
        ReportsMark? Reports) : UnfinishedRebuild(Kept);

    /// <summary>
    /// A rebuild the source failed in its scan, or in a finish run with no load in flight, which is run again from the
    /// scan's start.
    /// </summary>
    /// <param name="Kept">The entries a chain a guard cut gave, kept through the rebuild; null when no guard cut it.</param>
    private sealed record RebuildAwaitingScan(PdfXRefTable? Kept) : UnfinishedRebuild(Kept);

    /// <summary>
    /// Where the document's reports stood, and what it held as reported once — a guard, a fault of the syntax, a stream's
    /// length, an object stream's fault —, so that what was reported since can be withdrawn and reported again.
    /// </summary>
    /// <param name="Diagnostics">Where the diagnostics stood, with which of their entries decodings moved in.</param>
    /// <param name="LengthFaults">The streams whose length fault was reported, by where their data starts.</param>
    /// <param name="Syntax">The faults of the syntax reported.</param>
    /// <param name="Limits">The guards reported, and where.</param>
    /// <param name="MemberLimits">The object stream members whose cut was reported.</param>
    /// <param name="NeedingThemselves">The object streams reported as needing an object they hold.</param>
    /// <param name="NestingReported">Whether loads nested too deep were reported.</param>
    /// <param name="ObjectStreams">What the object streams' reports said.</param>
    private sealed record ReportsMark(
        PdfDiagnosticsMark Diagnostics,
        HashSet<long> LengthFaults,
        HashSet<PdfDiagnostic> Syntax,
        HashSet<(PdfLimit Limit, long Position)> Limits,
        HashSet<(long DataStart, int Number)> MemberLimits,
        HashSet<int> NeedingThemselves,
        bool NestingReported,
        ObjectStreamReports ObjectStreams);

    /// <summary>
    /// The definitions a rebuild met of object numbers it had already found: how many, and the first few numbers, kept
    /// without allocating for each.
    /// </summary>
    private sealed class Redefinitions
    {
        /// <summary>How many numbers the report lists at most: enough to look a few up in the file, few enough to read.</summary>
        private const int Listed = 10;

        private readonly int[] _numbers = new int[Listed];
        private int _listed;
        private bool _unlisted;

        /// <summary>
        /// Gets how many definitions met a number already found: a long, since the object streams a rebuild expands can
        /// list more members than an int counts.
        /// </summary>
        public long Count { get; private set; }

        /// <summary>Records a definition of <paramref name="number"/>, which was already found.</summary>
        public void Add(int number)
        {
            Count++;

            if (_numbers.AsSpan(0, _listed).Contains(number))
            {
                return;
            }

            if (_listed < Listed)
            {
                _numbers[_listed++] = number;
            }
            else
            {
                _unlisted = true;
            }
        }

        /// <summary>
        /// Gives a copy of what was met, which a finish adds to: a finish the source fails is run again from what the scan
        /// met, not from what it added.
        /// </summary>
        public Redefinitions Copy()
        {
            var copy = new Redefinitions { Count = Count, _listed = _listed, _unlisted = _unlisted };
            _numbers.CopyTo(copy._numbers, 0);
            return copy;
        }

        /// <summary>Says how many definitions were met, of which numbers, and which definition was kept.</summary>
        public string Describe()
        {
            var numbers = new StringBuilder();

            foreach (var number in _numbers.AsSpan(0, _listed))
            {
                numbers.Append(numbers.Length == 0 ? string.Empty : ", ").Append(number.ToString(CultureInfo.InvariantCulture));
            }

            var met = Count == 1
                ? string.Create(CultureInfo.InvariantCulture, $"Rebuilding the index met a second definition of object {numbers}.")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Rebuilding the index met {Count:N0} definitions of object numbers it had already found, {(_unlisted ? "among them those of objects" : _listed == 1 ? "of object" : "of objects")} {numbers}.");
            return met +
                " Of each number, the definition kept is the last written directly in the file, or, for a number written only inside object streams, the first listed in the object stream read first.";
        }
    }
}
