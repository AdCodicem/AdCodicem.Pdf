using System.Globalization;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO.XRef;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO;

internal sealed partial class PdfFileReader
{
    private void Initialize()
    {
        DetectHeader();

        var startXref = FindStartXRef();
        bool chainRead;
        _readingChain = true;

        try
        {
            chainRead = startXref >= 0 && TryReadXRefChain(startXref);
        }
        finally
        {
            _readingChain = false;
            _readWhileChainIsRead = null;
            _cutWhileChainIsRead = null;
            _refusedNumbers = null;
            _refusedOrder = null;
            ReleaseRefusedRanges();
            _rowsLost = null;
            _streamsPassedOver = null;
            _sectionsReadAt.Clear();
            _sectionsReadAt.TrimExcess();
            _sectionDigitsAt.Clear();
            _sectionDigitsAt.TrimExcess();
        }

        if (!chainRead)
        {
            // A rebuild reads every object again, and loads none the chain read through.
            _chainValuesCut = null;
            Repair();
            return;
        }

        _structure.ChainRead = true;
        _structure.TrailerRead = Trailer.Count > 0;
        _structure.SizeAsWritten = Trailer.GetRaw(PdfName.Size);
        _structure.RootAsWritten = Trailer.GetRaw(PdfName.Root);
        CheckDeferredLengths();
        ReachChainValuesCut();

        if (!HasUsableRoot())
        {
            _structure.RootUsable = false;
            _structure.RootResolved = _structure.RootAsWritten is PdfReference root ? root.Resolve() : null;
            _structure.RootHiddenByGuard = RootHiddenByGuard();
            _structure.RootCutByGuard = RootCutByGuard();

            // Resolving /Root may itself have rebuilt the index — its object was neither where its entry said nor
            // near it —, and the rebuild looked for the catalog in the rebuilt index. A catalog a guard cut is taken as
            // read: what it lacks may lie past the cut, and another found for it would be the reader's guess; so is one a
            // guard left out of the index, which reads as null until the guard is lifted (#175).
            if (!_repaired && !_structure.RootCutByGuard && !RootLeftOutByGuard())
            {
                RecoverCatalog();
            }
        }
    }

    /// <summary>
    /// Reads, once the chain is read, the <c>/Length</c> of each cross-reference stream the chain read without it, and
    /// reports one that is no length, one the data does not confirm, and one that gives data past the <c>endstream</c> the
    /// chain took the data up to (#182).
    /// </summary>
    /// <remarks>
    /// The <c>/Length</c> is loaded as any object is: what that load finds or changes comes after the chain's index is
    /// copied (<see cref="PreserveChainIndex"/>). A length the <c>endstream</c> the chain stopped at confirms — the white
    /// space before it counted, or not all of it — is the stream's own, as the parser takes it for any stream, and says
    /// nothing; so is one the data was taken again to, the section's own row placing it (<see cref="RetakeDeferred"/>).
    /// One confirmed by an <c>endstream</c> past it gives data the chain did not read, rows included — the section's row
    /// for the <c>/Length</c> among them, or one that does not stand —: they are not read again, and the report says so.
    /// The stream is not parsed again, and each is reported once.
    /// </remarks>
    private void CheckDeferredLengths()
    {
        if (_deferredLengths is null)
        {
            return;
        }

        foreach (var (dataStart, deferred) in _deferredLengths)
        {
            var reentries = _reentries;
            var value = GetObject(deferred.Length);
            var (number, generation) = (deferred.Length.Number, deferred.Length.Generation);
            string? message = null;

            // The chain is resolved as the parse resolves a /Length, one object further than a reference: the object it
            // leads to is read before it is asked whether a guard cut it, and what it holds is what is judged.
            var resolved = value.Resolve();

            // A /Length a guard cut or left out of the index — or the object a chain of references from it leads to — is
            // unknown, not wrong: the guard's report says why, and what the cut left of it, null or a parted integer, is
            // none the file gave (#224).
            if (LeftByGuard(new PdfReference(deferred.Length, this), PdfReference.MaxChainLength))
            {
                continue;
            }

            if (resolved.AsInteger() is not { } length || length is < 0 or > int.MaxValue)
            {
                var presence = value is PdfNull && reentries != _reentries ? ObjectPresence.Unproduced : GetPresence(number);
                var held = presence switch
                {
                    ObjectPresence.Missing => "which the file lacks",
                    ObjectPresence.Unproduced => "which could not be read",
                    _ => "which holds " + DescribeValue(resolved) + ", not a length",
                };
                message = string.Create(
                    CultureInfo.InvariantCulture,
                    $"The /Length of cross-reference stream {deferred.Stream} names object {number} {generation}, {held}; the chain took its data up to its endstream, after {CountText.Bytes(deferred.Taken)}.");
            }
            else if (length != deferred.Taken)
            {
                var stopped = EndStreamAfter(dataStart + deferred.Taken);

                if (((IPdfStreamDataProvider)this).CheckEndStream(dataStart + length) is null)
                {
                    message = string.Create(CultureInfo.InvariantCulture, $"The stream declared {CountText.Bytes(length)} but ended after {deferred.Taken}.");
                }
                else if (dataStart + length > stopped)
                {
                    message = string.Create(
                        CultureInfo.InvariantCulture,
                        $"The /Length of cross-reference stream {deferred.Stream} gives {CountText.Bytes(length)}, which an endstream confirms; the chain, which could not read it, took the data up to an endstream {CountText.Bytes(deferred.Taken)} in, and read no row past it.");
                }
            }

            if (message is not null && _lengthFaultsReported.Add(dataStart))
            {
                _diagnostics.Warn(PdfDiagnosticCodes.StreamLengthInvalid, message, dataStart);
            }
        }
    }

    /// <summary>
    /// Forgets the objects read through since <see cref="_chainValuesCut"/> held <paramref name="mark"/> of them, which a
    /// place the section may have been moved to, and that the search does not take, led to: what the place read through is
    /// not kept, and a value the chain keeps that leads to one of them reads it anew, for it to be loaded once the chain is
    /// read. What the place's reading reported is not this method's to drop (<see cref="TryRelocateXRefSection"/>).
    /// </summary>
    private void ForgetReadThrough(int mark)
    {
        if (_chainValuesCut is not { } cut)
        {
            return;
        }

        for (var i = mark; i < cut.Count; i++)
        {
            _readWhileChainIsRead?.Remove(cut[i]);
        }

        cut.RemoveRange(mark, cut.Count - mark);
    }

    /// <summary>
    /// Loads, once the chain is read, each object a guard cut that a cross-reference stream's value led to, which the chain
    /// ended at or read through: the guard is reached, and reported, where a load reaches it — and thrown, when the caller
    /// asked for it —, as for a <c>/Length</c> read once the chain is (#263).
    /// </summary>
    private void ReachChainValuesCut()
    {
        if (_chainValuesCut is not { } cut)
        {
            return;
        }

        _chainValuesCut = null;

        foreach (var id in cut)
        {
            GetObject(id);
        }
    }

    /// <summary>
    /// Gives where the <c>endstream</c> keyword starts that the white space and comments at <paramref name="position"/>
    /// lead to, or <paramref name="position"/> itself when none follows them.
    /// </summary>
    private long EndStreamAfter(long position)
    {
        var at = NextSignificant(position);
        Span<byte> keyword = stackalloc byte[StreamEnds.EndStreamKeyword.Length];
        var read = at < 0 || at >= _source.Length ? 0 : _source.Read(at, keyword[..(int)Math.Min(keyword.Length, _source.Length - at)]);
        return keyword[..read].SequenceEqual(StreamEnds.EndStreamKeyword) ? at : position;
    }

    /// <summary>
    /// A cross-reference stream's <c>/Length</c> the chain could not read: the object it names, how many bytes of data the
    /// chain took, and the stream's own number.
    /// </summary>
    private readonly record struct DeferredLength(PdfObjectId Length, int Taken, int Stream);

    /// <summary>
    /// Looks for the catalog among the objects the chain indexed, the trailer's <c>/Root</c> leading to none, and
    /// rebuilds the index only when none of them is one.
    /// </summary>
    /// <remarks>
    /// A broken <c>/Root</c> says nothing against the index: the objects are where the chain says. Keeping the index
    /// keeps what an incremental update superseded superseded, which a rebuild — the last definition in the file
    /// winning — does not always manage.
    /// </remarks>
    private void RecoverCatalog()
    {
        var reached = FindCatalog();

        if (!HasUsableRoot() && !_repaired && !RootHiddenByGuard())
        {
            Repair();
        }

        reached?.Throw();
    }

    /// <summary>
    /// Determines whether <c>/Root</c> names an object the index lacks because a guard stopped it short: the catalog lies
    /// past the cut, and reads as null until the guard is lifted, as any object there does. A <c>/Root</c> that names an
    /// object the index holds, or none, says the file is damaged.
    /// </summary>
    /// <remarks>
    /// So is a <c>/Root</c> the trailers lack where a guard kept one of them from being read whole: it may lie past the cut, or
    /// in an older trailer the chain stopped short of.
    /// </remarks>
    private bool RootHiddenByGuard() => Trailer.GetRaw(PdfName.Root) switch
    {
        PdfReference => RootLeftOutByGuard(),
        null => _structure.TrailersHiddenByGuard,
        _ => false,
    };

    /// <summary>
    /// Determines whether <c>/Root</c>, resolved — through a chain of references too —, names an object a guard left out of
    /// the index: the catalog, which reads as null until the guard is lifted, rather than looked for among the objects the
    /// index holds — any found there is another object than the one <c>/Root</c> names, which an update may have superseded
    /// (#175).
    /// </summary>
    private bool RootLeftOutByGuard() => Trailer.GetRaw(PdfName.Root) is PdfReference root && HiddenByGuard(root.Target().Number);

    /// <summary>
    /// Determines whether <c>/Root</c>, resolved, names an object a guard cut: it may be the catalog, its <c>/Type</c> or its
    /// <c>/Pages</c> past the cut, and it is taken for the catalog as read, until the guard is lifted, rather than looked
    /// for elsewhere — a search could take a catalog an update superseded, and a rebuild would scan the file for what the
    /// guard's report already names (ADR 34).
    /// </summary>
    /// <remarks>A catalog written in a trailer a guard cut, rather than referred to, is so cut (#175).</remarks>
    private bool RootCutByGuard() => Trailer.GetRaw(PdfName.Root) is PdfReference root
        ? IsCutAtLimit(root.Target().Number)
        : IsWrittenInCutTrailer(PdfName.Root);

    /// <summary>
    /// Merges the trailer of <paramref name="section"/> into the document's, keeping it apart when one of the reader's limits
    /// cut it: the chain ends there, and a value the document's trailer takes from it may lie past the cut in part.
    /// </summary>
    private void MergeSectionTrailer(PdfDictionary trailer, XRefSectionRecord section)
    {
        if (section.TrailerCutByLimit)
        {
            _cutTrailer = trailer;
        }

        _xref.MergeTrailer(trailer);
    }

    /// <summary>
    /// Determines whether the trailer's <paramref name="key"/> is a value written in a trailer one of the reader's limits
    /// cut — a catalog written there rather than referred to —, which it was read only as far as the limit let it be: what
    /// it lacks may lie past the cut. A reference is whole, and the object it names judged as it was read.
    /// </summary>
    internal bool IsWrittenInCutTrailer(PdfName key) =>
        _cutTrailer?.GetRaw(key) is { } value and not PdfReference && ReferenceEquals(Trailer.GetRaw(key), value);

    /// <summary>
    /// Determines whether <paramref name="trailer"/> names an older section of the chain — through <c>/Prev</c>, or a
    /// hybrid table's stream through <c>/XRefStm</c>, which a cross-reference stream's dictionary does not name —, whose
    /// trailer a chain that ends at it leaves unread: an offset in the file the chain has not reached, by the offsets it
    /// <paramref name="visited"/> or where it read a section (<see cref="ReachedBefore"/>), read as
    /// <see cref="SectionOffset"/> reads it.
    /// </summary>
    private bool NamesOlderSection(PdfDictionary trailer, bool stream, HashSet<long> visited) =>
        NamesSection(trailer.GetRaw(PdfName.Prev), visited) ||
        (!stream && NamesSection(trailer.GetRaw(PdfName.XRefStm), visited));

    /// <summary>
    /// Determines whether <paramref name="value"/>, a <c>/Prev</c> or an <c>/XRefStm</c>, names a section the chain has
    /// not read: an offset in the file it has not reached (<see cref="ReachedBefore"/>) — where it read a section found
    /// moved too, which a section the chain ends at may name in a loop (#264) —; or a reference, or a chain of them, that
    /// leads to what the guard that ended the chain hides (<see cref="LeadsPastIndex"/>). One that leads elsewhere reads as a
    /// whole read reads it, rows read first winning.
    /// </summary>
    private bool NamesSection(PdfObject? value, HashSet<long> visited) =>
        LeadsPastIndex(value, out var read) ||
        (read?.AsInteger() is { } offset and >= 0 && IsInFile(PositionOf(offset)) && !ReachedBefore(offset, visited));

    /// <summary>
    /// Follows <paramref name="value"/> through a chain of references, each read where a section already read places it,
    /// as many as a resolution follows, and determines whether the chain leads to what a guard hides: an object that runs
    /// past <see cref="PdfReaderLimits.MaxObjectLength"/>, or one whose number the index read so far lacks, no section refused
    /// — a row it refused, or the numbers a newer section that lost rows refused to the older sections' rows
    /// (<see cref="RefusedToOlderRows"/>) —, and whose row may be among those a guard left unread. <paramref name="read"/> is
    /// where the chain ends: null at a link that reads as nothing, or past the links a resolution follows, where a whole read
    /// finds no value either.
    /// </summary>
    private bool LeadsPastIndex(PdfObject? value, out PdfObject? read)
    {
        for (var link = 0; value is PdfReference reference && link < PdfReference.MaxChainLength; link++)
        {
            value = ReadWithoutLoading(reference.Id);

            if (value is null)
            {
                // Object 0 is never read, and a number the chain refused is never placed: no row past a guard changes either.
                // Nor does one a newer section that lost rows refused to the older sections' rows — those past the guard
                // among them —, a cross-reference stream it went on past, or one it kept read only in part (#208, #210); the
                // section the guard stopped refuses what it lost once this is judged, its own rows past the guard placing it
                // as read whole. An object a guard cut is the guard's, whatever row placed it.
                var number = reference.Id.Number;
                read = null;
                return number > 0 && _refusedNumbers?.ContainsKey(number) != true &&
                    (_cutWhileChainIsRead?.ContainsKey(reference.Id) == true ||
                        (!_xref.TryGet(number, out _) && !RefusedToOlderRows(number) && MayBeLeftUnread(number)));
            }
        }

        read = value is PdfReference ? null : value;
        return false;
    }

    /// <summary>
    /// Determines whether a row a guard left unread in the section just read may number object <paramref name="number"/>:
    /// any row of a table may, and of a cross-reference stream, only those its <c>/Index</c> or <c>/Size</c> declares past
    /// where the guard stopped it.
    /// </summary>
    private bool MayBeLeftUnread(int number)
    {
        if (_rowsLeftUnread is not { } rows)
        {
            return true;
        }

        for (var i = 0; i + 1 < rows.Length; i += 2)
        {
            if (number >= rows[i] && number - rows[i] < rows[i + 1])
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether a guard keeps object <paramref name="number"/> from the reader: it stopped the index short of
    /// the file, and the index lacks the object, or holds it in an object stream it lacks.
    /// </summary>
    private bool HiddenByGuard(int number) =>
        IndexStoppedByGuard &&
        (!_xref.TryGet(number, out var entry) ||
            (entry.Kind == XRefEntryKind.Compressed && !_xref.TryGet(entry.ObjectStreamNumber, out _)));

    private void DetectHeader()
    {
        using var window = _source.GetWindow(0, (int)Math.Min(HeaderSearchLength, _source.Length));
        var span = window.Memory.Span;
        var index = span.IndexOf(HeaderMarker);

        if (index < 0)
        {
            // Nothing is rebuilt for it: the offsets the file gives are counted from its first byte, as from a header
            // there. An empty source has no first byte to point at.
            _diagnostics.Repair(
                PdfDiagnosticCodes.HeaderMissing,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The file has no PDF header (%PDF-) in its first {HeaderSearchLength:N0} bytes; its offsets were counted from its first byte."),
                _source.Length > 0 ? 0 : -1);
            return;
        }

        _structure.HeaderPosition = index;

        if (index > 0)
        {
            // Bytes before the header shift every offset in the file by the same amount.
            _headerOffset = index;
            _diagnostics.Repair(
                PdfDiagnosticCodes.XRefOffsetAdjusted,
                string.Create(CultureInfo.InvariantCulture, $"The PDF header starts {CountText.Bytes(index)} into the file; offsets were shifted accordingly."),
                index);
        }

        // What follows %PDF- runs to the white space, the comment or the end of the file that ends it, and names a version
        // only when it is one digit, a period and one digit (ISO 32000-1, 7.5.2): 1.4.5.6, 1.7abc and 14 name none. Text
        // the window cuts names none either, what follows it being unknown; the window bounds what is kept of it.
        var versionStart = index + HeaderMarker.Length;
        var versionEnd = versionStart;
        while (versionEnd < span.Length && !PdfCharacters.IsWhitespace(span[versionEnd]) && span[versionEnd] != (byte)'%')
        {
            versionEnd++;
        }

        var written = span[versionStart..versionEnd];
        _structure.HeaderVersion = System.Text.Encoding.Latin1.GetString(written);
        _structure.HeaderVersionCut = versionEnd == span.Length && span.Length < _source.Length;

        if (written is [>= (byte)'0' and <= (byte)'9', (byte)'.', >= (byte)'0' and <= (byte)'9'] &&
            !_structure.HeaderVersionCut)
        {
            _headerVersion = _structure.HeaderVersion;
        }
    }

    /// <summary>
    /// Works out the version the file declares: its header's, or its catalog's <c>/Version</c> when that is later (ISO
    /// 32000-1, 7.5.2 and 7.7.2); none when the header names none, whatever the catalog says.
    /// </summary>
    /// <remarks>
    /// A catalog's <c>/Version</c> is written as the header's is, one digit, a period and one digit; any other value, or
    /// a catalog the reader cannot read — one in an object stream of an encrypted document —, leaves the header's. The
    /// catalog is the one the document opened with: worked out once, the version does not move as more is read.
    /// </remarks>
    private string? DeclaredVersion()
    {
        if (_headerVersion is not { } header)
        {
            return null;
        }

        return Trailer.GetDictionary(PdfName.Root)?.GetName(PdfName.Version)?.Value is
            [>= '0' and <= '9', '.', >= '0' and <= '9'] catalog && string.CompareOrdinal(catalog, header) > 0
            ? catalog
            : header;
    }

    private long FindStartXRef()
    {
        var length = (int)Math.Min(TailSearchLength, _source.Length);
        if (length <= 0)
        {
            return -1;
        }

        using var window = _source.GetWindow(_source.Length - length, length);
        var span = window.Memory.Span;
        var index = span.LastIndexOf(StartXRefKeyword);

        if (index < 0)
        {
            return -1;
        }

        _structure.StartXRefPosition = window.Offset + index;

        var lexer = new PdfLexer(span, index + StartXRefKeyword.Length);
        var token = lexer.Read();

        if (token.Kind == PdfTokenKind.Integer && token.Integer >= 0)
        {
            _structure.StartXRef = token.Integer;
            return token.Integer;
        }

        return -1;
    }
}
