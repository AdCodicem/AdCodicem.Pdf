namespace AdCodicem.Pdf.Documents;

/// <summary>
/// The reader's guards: bounds on what a file may make the reader hold or read, set against hostile input, which a
/// document valid under the specification can nonetheless exceed.
/// </summary>
/// <remarks>
/// <para>
/// Every guard is active by default, so an application that opens files it did not produce is protected
/// without configuring anything. One that reads a sound but exceptional document — a large-format scan, a
/// file saved incrementally a thousand times — raises the bound it reached, or chooses <see cref="Unbounded"/>.
/// </para>
/// <para>
/// Reaching a guard keeps what fits within it and reports a warning under one of the <c>limit.*</c> codes of
/// <see cref="Diagnostics.PdfDiagnosticCodes"/>, whose message names the property that lifts it. With
/// <see cref="PdfReaderOptions.ThrowOnLimit"/> it throws <see cref="Diagnostics.PdfLimitExceededException"/>
/// instead.
/// </para>
/// <para>
/// A bound that only an invalid file can reach — how far references to references are followed — is not an option:
/// lifting it would let nothing more be read.
/// </para>
/// </remarks>
public sealed record PdfReaderLimits
{
    /// <summary>The guards in force when none are supplied.</summary>
    public static PdfReaderLimits Default { get; } = new();

    /// <summary>
    /// Every guard at the most the implementation can hold: <see cref="Array.MaxLength"/> for a length,
    /// <see cref="int.MaxValue"/> for a count. For documents the application trusts.
    /// </summary>
    public static PdfReaderLimits Unbounded { get; } = new()
    {
        MaxDecodedStreamLength = int.MaxValue,
        MaxStreamLength = int.MaxValue,
        MaxObjectLength = int.MaxValue,
        MaxXRefSectionLength = int.MaxValue,
        MaxXRefSectionCount = int.MaxValue,
        MaxTrailerLength = int.MaxValue,
        MaxIndexEntries = int.MaxValue,
        MaxFilterChainLength = int.MaxValue,
        MaxRebuildReadPasses = int.MaxValue,
        MaxObjectStreamDecodePasses = int.MaxValue,
        MaxNestingDepth = int.MaxValue,
        MaxNestedLoads = int.MaxValue,
    };

    /// <summary>
    /// Gets the most one stream may decode to, in bytes. Defaults to 256 MB: a compressed stream of a few
    /// kilobytes can claim gigabytes, and a scanned map can hold an image of 300 MB.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A decoded stream is held in one piece, so a value above <see cref="Array.MaxLength"/> is taken as that
    /// length. Reaching it reports <see cref="Diagnostics.PdfDiagnosticCodes.LimitDecodedStream"/>.
    /// </para>
    /// <para>
    /// Of a stream whose data passes through several filters, the last filter's output is bounded alone, and the filters
    /// before it share the bound for what they pass along together: a stream of one or two filters decodes as each filter
    /// alone would, and a chain however long holds two bounds at most. A <c>/Crypt</c> filter, which passes its data along
    /// as it is, is neither charged nor the last. Under a bound of <see cref="Array.MaxLength"/>, the filters before the
    /// last share none.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or less.</exception>
    public int MaxDecodedStreamLength { get; init => field = Length(value); } = 256 * 1024 * 1024;

    /// <summary>
    /// Gets the most of one stream's encoded data the reader reads at once, in bytes. Defaults to 256 MB: a <c>/Length</c>
    /// of a few digits can claim gigabytes, and a scanned map can hold an uncompressed image of 300 MB.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The encoded data is read in one piece each time it is asked for — <see cref="Objects.PdfStream.GetRawBytes"/>, or
    /// decoding it —, so a value above <see cref="Array.MaxLength"/> is taken as that length, and a <c>/Length</c> past it
    /// is cut there. Data past the bound is cut at it: what lies within it is read, and decoded as data the reader cut,
    /// whose lost tail is no damage. Reaching it reports <see cref="Diagnostics.PdfDiagnosticCodes.LimitStreamLength"/>,
    /// once for each stream in the document's diagnostics, and in diagnostics of your own each time you decode the stream
    /// (<see cref="Objects.PdfStreamDecodingExtensions.Decode"/>). <see cref="Objects.PdfStreamData.Length"/> stays the
    /// length the file holds.
    /// </para>
    /// <para>
    /// Data that decoding hands back as it is — no filter, an image filter, a filter the library does not know, one that
    /// could not decode it — is bounded by this guard, not by <see cref="MaxDecodedStreamLength"/>, which bounds what the
    /// filters produce: a large uncompressed image needs this bound raised alone (#170).
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or less.</exception>
    public int MaxStreamLength { get; init => field = Length(value); } = 256 * 1024 * 1024;

    /// <summary>
    /// Gets the longest object the reader parses, in bytes, the data of a stream aside, whether the file writes it
    /// directly or an object stream holds it. Defaults to 16 MB, which a direct array of a million references would need.
    /// </summary>
    /// <remarks>
    /// A stream's data never counts toward it, however its end is found, nor the end-of-line after its <c>stream</c>
    /// keyword: a <c>/Length</c> that is wrong, missing or past the file has the data searched for its <c>endstream</c> past
    /// the bytes the object was parsed through, not read through a larger window (#174); what the reader reads of the data at once is
    /// <see cref="MaxStreamLength"/>'s. A value above <see cref="Array.MaxLength"/> is taken as that length. Reaching it reports
    /// <see cref="Diagnostics.PdfDiagnosticCodes.LimitObject"/>, once for each object, and for each object an object
    /// stream holds, once for each definition of that stream.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or less.</exception>
    public int MaxObjectLength { get; init => field = Length(value); } = 16 * 1024 * 1024;

    /// <summary>
    /// Gets the longest classic cross-reference section the reader reads, in bytes. Defaults to 64 MB, about
    /// 3.3 million entries.
    /// </summary>
    /// <remarks>
    /// A value above <see cref="Array.MaxLength"/> is taken as that length. Reaching it keeps the entries read
    /// and reports <see cref="Diagnostics.PdfDiagnosticCodes.LimitXRefSectionLength"/>. A section read where an offset
    /// names it is measured from that offset, so that white space and comments before its <c>xref</c> keyword count toward
    /// the bound (#289).
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or less.</exception>
    public int MaxXRefSectionLength { get; init => field = Length(value); } = 64 * 1024 * 1024;

    /// <summary>
    /// Gets the most cross-reference sections the reader follows through a chain of updates. Defaults to
    /// 1,024; each incremental save adds one.
    /// </summary>
    /// <remarks>
    /// It counts the links of the chain, the sections <c>startxref</c> and each <c>/Prev</c> name. In a hybrid file, the
    /// cross-reference stream a classic table's <c>/XRefStm</c> names is read beside that table and not counted: a chain of
    /// hybrid tables reads up to two sections a link, a table and its stream. Reaching it keeps the newest sections and
    /// reports <see cref="Diagnostics.PdfDiagnosticCodes.LimitXRefSectionCount"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or less.</exception>
    public int MaxXRefSectionCount
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, nameof(MaxXRefSectionCount));
            field = value;
        }
    } = 1024;

    /// <summary>
    /// Gets the longest trailer the reader parses, in bytes — a cross-reference stream's dictionary is its
    /// trailer. Defaults to 64 KB; real ones are a few hundred bytes.
    /// </summary>
    /// <remarks>
    /// A classic trailer that ends within the window its cross-reference table was read through is read whole,
    /// whatever its length: the guard bounds how far the reader follows one past that window. A cross-reference stream's
    /// data is not a trailer: one whose <c>/Length</c> the reader cannot read while it reads the chain is searched for its
    /// <c>endstream</c> in the file, up to the next object or the end of the file. A value above
    /// <see cref="Array.MaxLength"/> is taken as that length. Reaching it reports
    /// <see cref="Diagnostics.PdfDiagnosticCodes.LimitTrailer"/>, and ends the cross-reference chain at the section cut: an
    /// older section, or a key, past the cut is unknown, and what only older sections index reads as null until the limit
    /// is raised — but for the trailer of a classic table an <c>/XRefStm</c> names, whose links the chain never follows
    /// (#263), the chain going on through the table that names it. It bounds, apart, the white space and comments an offset
    /// that names a cross-reference section falls short by: a cross-reference stream is read from its object's header, so
    /// that its dictionary is cut only where it alone runs past the bound, while a classic table's windows open at the
    /// offset named, so that the white space before its <c>xref</c> keyword counts toward
    /// <see cref="MaxXRefSectionLength"/> as well (#289). White space and comments that run on past the bound reach it too,
    /// reported under the same code: the section is not read from that offset, and is looked for as one that is not there —
    /// near the offset, where a search may find it past them, or, for the one <c>startxref</c> names, by rebuilding the
    /// index.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or less.</exception>
    public int MaxTrailerLength { get; init => field = Length(value); } = 64 * 1024;

    /// <summary>
    /// Gets the most object numbers the reader indexes, free ones and those whose row it refused included. Defaults to
    /// 8,388,607, the most ISO 32000-1 advises a document to hold (annex C): a cross-reference stream of a few hundred
    /// bytes can claim hundreds of millions of objects, and the index holds 40 bytes for each it is sized for, up to about
    /// 80 when a rebuild grows it and about 96 for numbers spread far apart.
    /// </summary>
    /// <remarks>
    /// The index takes object numbers in the order the reader meets them — the newest section of the chain first, a
    /// rebuild in the file's order —, and once it holds this many takes no more: an object it holds no entry for reads
    /// as null until the bound is raised, and the index is not rebuilt for it. Reaching it reports
    /// <see cref="Diagnostics.PdfDiagnosticCodes.LimitIndexEntries"/>, once. The bound also sets what validating a
    /// document can cost, which follows its index: about 24 seconds and 3 GB near the default. While the cross-reference
    /// chain is read, the numbers whose rows it refused, which the bound counts with the index, are held apart as well, each
    /// with an int that tells which section refused it: about 60 bytes a number allocated in all as that set grows, let go
    /// once the chain is read. The same bound applies to what the reader keeps of the scan of the file that the
    /// searches near an entry's offset and near an offset the chain names ask for, once a document: the object headers
    /// and keywords it takes, each definition of a number and each keyword counted, 16 bytes at most for a header or an
    /// <c>xref</c> keyword, under 9 for a <c>trailer</c> keyword and under 25 for a cross-reference stream's header —
    /// the lists that hold the last two keep up to a ninth more room than they use —, about 201 MB at most at the
    /// default, and from two to five times what it keeps allocated while it grows. What the scan takes past it is not
    /// kept; what it took from the data of a stream whose length's object it then awaited is kept, and counted toward
    /// it, but not answered for, since that object could show the data to end sooner. A search that has to go without
    /// the scan's word there — one near an entry's offset that finds a header of the object there, or one near an offset
    /// the chain names that tries a place there — takes what it finds as the bytes give it, and reports the code once
    /// more, apart from the index's report, where raising the bound would let it tell
    /// (<see cref="Diagnostics.PdfDiagnosticCodes.LimitIndexEntries"/>).
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or less.</exception>
    public int MaxIndexEntries
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, nameof(MaxIndexEntries));
            field = value;
        }
    } = 8_388_607;

    /// <summary>
    /// Gets the most filters a stream's data passes through. Defaults to 64: real streams name one to three, a
    /// <c>/Crypt</c> filter included, and ISO 32000 sets no bound, while each filter a <c>/Filter</c> array of millions
    /// names costs decoding a few microseconds, and some of them a table of tens of kilobytes, whatever data it is given.
    /// </summary>
    /// <remarks>
    /// The filters are counted as the <c>/Filter</c> array names them, from its first element. Reaching the bound ends
    /// decoding before the element past it, as a filter the library does not know does: the data comes back as the
    /// filters before it decoded it, and a filter past it is not read, an image filter included, so that
    /// <see cref="Objects.PdfStreamDecodingExtensions.HasImageFilter"/> does not see one. Reaching it reports
    /// <see cref="Diagnostics.PdfDiagnosticCodes.LimitFilterChain"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or less.</exception>
    public int MaxFilterChainLength
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, nameof(MaxFilterChainLength));
            field = value;
        }
    } = 64;

    /// <summary>
    /// Gets how many times the length of the file a rebuild of the index may read past streams' direct lengths to confirm
    /// them, as it scans the file for object headers. Defaults to 4: a valid file's own streams take about twice the file
    /// at most, while hostile stream headers by the million, whose lengths all end in one long run of white space, would
    /// make the scan read that run once for each.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scan confirms a stream's direct <c>/Length</c> where an <c>endstream</c> follows it, and reads on through the
    /// white space and comments past the length that a first read of 64 bytes cannot see past, and through the regular
    /// characters glued to such an <c>endstream</c>: those reads count toward the bound. A valid stream's <c>endstream</c>
    /// follows its length, the reads that find it cross the white space before it about twice as their reads double, and
    /// the streams of a valid file lie apart. The scan confirms an indirect <c>/Length</c> where it finds the object a
    /// writer puts near the stream, with reads of its own that do not count toward the bound. It cannot confirm the data
    /// of a stream whose length it does not find, and reads it as objects: the streams an attached file's bytes spell there
    /// count too, and a valid file can reach the bound with them — their lengths may all end in one run of white space,
    /// which each read past them crosses.
    /// </para>
    /// <para>
    /// Once the scan has read as much as the bound lets it, a stream whose direct length a first read cannot confirm is
    /// not confirmed, and the scan reads the data of every stream it cannot confirm from then on as an intact stream's, up
    /// to its first <c>endstream</c>, the object headers there not taken: the headers of an attached file whose data holds an
    /// <c>endstream</c> of its own before the stream's are then taken past that keyword, and a comment there may hide the
    /// objects after the stream. Where the data the bound left so holds an object header the scan would have taken, an
    /// object the rebuilt index lacks reads as null until the bound is raised, and whether the file holds it is unknown, as
    /// for the other guards that leave objects out of the index, whatever the file's own index says of it: the validator
    /// says the index was checked in part (<c>xref.checked-in-part</c>) from the first such stream, rather than judge a
    /// reference to it missing or its entry broken. Reaching it reports
    /// <see cref="Diagnostics.PdfDiagnosticCodes.LimitRebuildReads"/>, once for each rebuild, where the data starts of the
    /// first stream the bound left so; with <see cref="PdfReaderOptions.ThrowOnLimit"/>, the rebuild finishes the index
    /// first and throws after, and, where that data holds such a header, a read of an object the index lacks throws again
    /// each time, at the same position, as where the index filled.
    /// The bound is taken against the file's length as the reader's source gives it, the product held at the most a
    /// position can name.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or less.</exception>
    public int MaxRebuildReadPasses
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, nameof(MaxRebuildReadPasses));
            field = value;
        }
    } = 4;

    /// <summary>
    /// Gets how many times what one of the library's walks of a document first decoded of its object streams it may decode
    /// again of those the reader let go. Defaults to 4: a walk decodes each object stream once while those it reads from in
    /// turn fit the reader's budget for decoded object streams, while two streams that each decode past half that budget,
    /// whose objects the walk reads in turn, would have it decode a whole stream again for each object.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reader keeps the object streams it decoded within a budget of 32 MB, and the one it decoded last whatever its
    /// size: a stream it let go is decoded again when one of its objects is read again. The library's own walks of a
    /// document weigh what they decode: the search for a catalog, which a <c>/Root</c> that leads to none sets off as the
    /// document opens or as a rebuild of its index finishes, and a validation's rules
    /// (<see cref="Validation.PdfValidator.Validate"/>). During one walk, a nested one counted with it, the object streams it
    /// decoded and the reader let go may be decoded again, together, up to this many times what their first decodings in
    /// that walk weighed — their decoded data, and their headers' tables. Past it, every object a stream the walk decoded
    /// and the reader let go holds reads as null until the walk ends, as an object a guard cut, and the walk goes on. A
    /// stream the walk has not decoded yet is decoded, and a rebuild's taking in of the object streams is not weighed. A
    /// search for a catalog that finds none among the objects it read reads those the guard cut again, stream by stream,
    /// each stream decoded once more at most, and takes the first of them, in the index's order, that reads as a catalog —
    /// read whole this time, or cut again, as the first search takes one —, before it would rebuild the index for want of
    /// one; where this reading finds none, the index is rebuilt as it would have been without it. It does not read them
    /// again within another walk, nor once a load rebuilt the index on the way, whose own search for a catalog takes over.
    /// What reads as null for the guard is not kept. An object whose load read through it is kept until the walk ends only,
    /// and an object stream whose decoding did is not kept at all, nor decoded again before the walk ends, so that your own
    /// reads, once the walk is done, read the objects whole; and the budget starts again with the next walk.
    /// </para>
    /// <para>
    /// A valid file can reach it: a page tree whose pages lie in turn in two object streams that each decode past 16 MB.
    /// Validating such a file checks in part what lies past the cut, as for any guard; reading its pages yourself is not
    /// bounded by it, and decodes a stream again for each page. Which objects read as null depends on what the reader kept
    /// before the walk began: two validations of such a file may differ. Reaching it reports
    /// <see cref="Diagnostics.PdfDiagnosticCodes.LimitObjectStreamDecodes"/>, once a document, where the object stream it
    /// first left undecoded lies; with <see cref="PdfReaderOptions.ThrowOnLimit"/>, each read it leaves undecoded throws,
    /// the search for a catalog once it ends, as for the other guards it meets, and the walk reads an object whose read
    /// threw so once, however many of its reads lead to it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or less.</exception>
    public int MaxObjectStreamDecodePasses
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, nameof(MaxObjectStreamDecodePasses));
            field = value;
        }
    } = 4;

    /// <summary>
    /// Gets how deeply one object may nest its arrays and dictionaries. Defaults to 128: real documents rarely pass ten,
    /// while ISO 32000-1 (7.3.6) nests them to any depth — a chain of actions through <c>/Next</c> (12.6.2) written in one
    /// object is as deep as it is long.
    /// </summary>
    /// <remarks>
    /// The bound holds within one object, whether the file writes it directly or an object stream holds it; an object
    /// loaded while another is parsed starts again at zero, and is bounded by <see cref="MaxNestedLoads"/>. The container
    /// past it reads as null — an array's element, a dictionary's key dropped —, what lies inside it unread, and the object
    /// is one a guard cut. Each level costs the parser under a kilobyte of the thread's stack, which is asked before each
    /// one: on a thread that has no room left, the container reads as null too, reported under the same code with the
    /// stack as the reason, however high the bound, and nothing read through it is kept past the read in progress.
    /// Reaching either reports <see cref="Diagnostics.PdfDiagnosticCodes.LimitNestingDepth"/>, once for each place.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or less.</exception>
    public int MaxNestingDepth
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, nameof(MaxNestingDepth));
            field = value;
        }
    } = 128;

    /// <summary>
    /// Gets how many objects may be loaded while loading another, the first load counted. Defaults to 64: real documents
    /// stay within a handful — an object in an object stream whose <c>/Length</c> is indirect is three —, while
    /// ISO 32000-1 (7.5.7) lets an object stream take its <c>/Filter</c> or <c>/DecodeParms</c> from an object another
    /// object stream holds, to any depth.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A load nests another when an object's parse resolves an indirect <c>/Length</c>, when an object an object stream
    /// holds has the stream loaded and decoded, and when that decoding reads its <c>/Type</c>, <c>/N</c>, <c>/First</c>,
    /// <c>/Filter</c> and <c>/DecodeParms</c>. The load past the bound reads as null where it was asked from, and the
    /// object is read whole from a shallower place: how deep a read went is kept with what it gave — an object, an object
    /// stream's contents, or under <see cref="PdfReaderOptions.ThrowOnLimit"/> the exception it threw —, which serves a
    /// later read only where a read made then would go as deep without the bound, or, for what the bound cut, from as many
    /// loads in flight as it was made from. What an object reads as does not depend on what was read before it. Each load
    /// costs the reader a few kilobytes of the thread's stack, which is asked before each one: on a thread that has no room
    /// left, the load reads as null too, reported under the same code with the stack as the reason, however high the
    /// bound, and nothing read through it is kept past the read in progress.
    /// </para>
    /// <para>
    /// The validator's walk of what each object stream's decoding needs nests its loads as the reader does, under the same
    /// bound. Reaching it reports <see cref="Diagnostics.PdfDiagnosticCodes.LimitNestedLoads"/>, once a document.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or less.</exception>
    public int MaxNestedLoads
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, nameof(MaxNestedLoads));
            field = value;
        }
    } = 64;

    private static int Length(int value, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, property);
        return Math.Min(value, Array.MaxLength);
    }
}
