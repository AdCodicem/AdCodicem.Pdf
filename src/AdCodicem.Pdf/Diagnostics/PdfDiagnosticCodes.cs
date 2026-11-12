namespace AdCodicem.Pdf.Diagnostics;

/// <summary>
/// The diagnostic codes the library emits. They are part of the public contract: renaming one is a
/// breaking change, because callers filter and assert on them.
/// </summary>
public static class PdfDiagnosticCodes
{
    /// <summary>
    /// The file has no <c>%PDF-</c> header within the bytes the reader searches at its start (ISO 32000-1, 7.5.2): the
    /// file was read all the same, its offsets counted from its first byte.
    /// </summary>
    public const string HeaderMissing = "header.missing";

    /// <summary>The cross-reference table was rebuilt by scanning the whole file.</summary>
    public const string XRefRebuilt = "xref.rebuilt";

    /// <summary>
    /// An object, or a cross-reference section the chain names, was not at the offset the file claimed, and was
    /// found nearby — a section never where a section the chain reached starts, nor at a header whose digits, past its
    /// own signs, are where those of a stream the chain reached start (#327), but for what a hybrid table's
    /// <c>/XRefStm</c> read in the stream's place, which that table's <c>/Prev</c> takes, reading it whole (#156); nor near
    /// one that is there and cannot be read. Where the offset holds no section, the nearest place near it that declares
    /// itself one — an <c>xref</c> keyword, a stream whose dictionary gives <c>/Type /XRef</c> — is the section found
    /// moved though it cannot be read, which <see cref="XRefSectionUnreadable"/> then reports where it was found (#281).
    /// </summary>
    public const string XRefOffsetAdjusted = "xref.offset-adjusted";

    /// <summary>
    /// A cross-reference section the chain names, through <c>/Prev</c> or <c>/XRefStm</c>, is neither where
    /// it is named nor near it — nothing there reads as a section, which <see cref="XRefSectionUnreadable"/> reports
    /// otherwise, and an object that gives a <c>/W</c> and another <c>/Type</c>, a CIDFont, holds none unless it reads as
    /// one —, lies outside the file, or is named by a value that is not an offset. The objects only it
    /// indexes are missing from the index, which is rebuilt by scanning the file when one of them is asked for. The report
    /// is placed at the offset the section was named at, or, when that offset lies outside the file or the value is not an
    /// offset, at the section whose trailer named it.
    /// </summary>
    public const string XRefSectionMissing = "xref.section-missing";

    /// <summary>
    /// A cross-reference section the chain names cannot be read whole. Through <c>/Prev</c> or <c>/XRefStm</c>, it is where
    /// it is named but cannot be read — a stray token among a table's rows or in place of its trailer, a subsection
    /// numbering objects past 2,147,483,647, a table the end of the file cuts before its first row, within its
    /// <c>trailer</c> keyword too, a cross-reference stream whose <c>/W</c>, <c>/Index</c>, <c>/Size</c> or data cannot give
    /// rows, data its filters decode nothing of among them (#221), or whose <c>/Length</c> no section read before it places
    /// and whose data no <c>endstream</c> ends before the next object or the end of the file —: an <c>xref</c> keyword, or
    /// a stream whose dictionary gives <c>/Type /XRef</c>, for which no section near it is taken, and which, the nearest
    /// place to an offset that holds no section, is the section found moved, reported where it was found (#281); or a
    /// stream that only gives a <c>/W</c>, when nothing near it can be read in its place. A value that leads to an object
    /// one of the reader's limits cut is the limit's: the chain ends there, and nothing is reported of the section. The
    /// rows read before the fault, if any, were kept, but for those it refused itself and those whose numbers a newer
    /// section refused — none of them, where a newer section refused every number, as the message says —; the objects only
    /// the rest indexes are missing from the index, which is rebuilt by scanning the file when one of them is asked for. A
    /// stream of <c>/Type /XRef</c> whose dictionary was read whole loses its rows alone: no older section's row stands for
    /// an object it numbers — for any, where its numbering cannot be read —, which is missing from the index as the objects
    /// only it indexes are; and one that gives a <c>/Prev</c> hands that <c>/Prev</c> on, the chain going on to the older
    /// sections, as the message says where that <c>/Prev</c> names an offset or leads to what one of the reader's limits
    /// hides (#208). A classic table an <c>/XRefStm</c> names, past which the chain goes on through the table that names
    /// it, leaves no older section's row standing for the rest of the subsection its fault ended, nor, where what follows
    /// the fault cannot be known — a subsection header that is no header, a stray token, the end of the file —, for any
    /// number its rows do not give (#156).
    /// Or the section, the one <c>startxref</c> names among them, was read only in part, and kept, the chain going on from
    /// it where its trailer was read (#207, #210): a row that is no row ended a subsection of a table, a cross-reference
    /// stream holds fewer rows than it declares, the end of the file cuts a table before its trailer or within its
    /// <c>trailer</c> keyword, or a row that gives an object in use what no entry can hold was refused alone, its object
    /// rebuilt where no row read before it — a newer section's, or the section's own for the same number — indexes it. The
    /// rows read were kept, but for those it refused itself, which the message says without naming them beside the fault it
    /// names, and those whose numbers a newer section refused — none of them, where a newer section refused every number —,
    /// and the message says which, and what was lost: the objects the lost rows would index, which no older section's row
    /// stands for — every object neither its rows nor a section read before it indexes, where the section refused every
    /// number: a stream whose numbering is written with references, or more than 4,096 ranges lost, its own or once joined
    /// with those refused before, which the message says, but for a table <c>startxref</c> or a <c>/Prev</c> names whose
    /// rest cannot be known, at which the chain ends, whose message says, however many ranges it lost, that what only the
    /// rest of the table, or the sections its trailer would name, index is looked for, the same objects, unless a newer
    /// section refused every number before the chain came to it, which the message says instead —, and those only the
    /// sections a lost trailer would name index, are missing from the index, rebuilt when one of them is asked for. The
    /// report is placed at the section, once, one fault in its message: the first that lost rows, else the end of the file
    /// before its trailer, else the first row it refused, which is not named beside either of the others.
    /// </summary>
    public const string XRefSectionUnreadable = "xref.section-unreadable";

    /// <summary>
    /// A classic cross-reference table the chain names holds no subsection: its <c>trailer</c> keyword follows its
    /// <c>xref</c> keyword, where ISO 32000-1 (7.5.4) has one or more subsections follow it. It was read as indexing nothing,
    /// as a table whose one subsection is <c>0 0</c> is, and nothing is rebuilt for it (#222). The report is placed at the
    /// table.
    /// </summary>
    public const string XRefSectionEmpty = "xref.section-empty";

    /// <summary>
    /// The chain of previous cross-reference sections looped back on itself: a <c>/Prev</c> names an offset the chain has
    /// already reached — where a section it reached starts, or the white space and comments before it, a section it read
    /// however it was named and one there that it could not read though it declares itself one among them (#188, #208), or
    /// an offset a <c>/Prev</c> or an <c>/XRefStm</c> named where it read none, in the file or outside it. An
    /// <c>/XRefStm</c> ends no chain: one that names what the chain has reached names a hybrid file's stream again, or is
    /// <see cref="XRefHybridStreamLost"/>'s (#156). The report is placed at the offset the <c>/Prev</c> names, or where the
    /// section it reaches past the white space there starts, or at the section whose trailer named it when that offset lies
    /// outside the file.
    /// </summary>
    public const string XRefChainCycle = "xref.chain-cycle";

    /// <summary>
    /// A hybrid file's table names through <c>/XRefStm</c> no cross-reference stream of its revision (ISO 32000-1, Table 19):
    /// a classic table — the table itself, another the chain read, one it reads in the stream's place, its rows taken, or
    /// one that cannot be read, at each table that names it —, a cross-reference stream the chain read through
    /// <c>/Prev</c> or <c>startxref</c>, or what the table's own <c>/Prev</c> reaches too — at the same offset, through the
    /// white space before that section, or where the search near the offset the <c>/XRefStm</c> names comes to it and
    /// that <c>/Prev</c> keeps its place there, as its own reading does —, which the chain reads through that
    /// <c>/Prev</c> rather than in the stream's place, the report saying no more of it than the chain knows before that
    /// <c>/Prev</c> is read (#156). A search that stopped there only because one of the reader's limits cut what the
    /// <c>/Prev</c> names, before it said what it is or in a value its rows are read with, is the limit's, and not
    /// reported (#264). The objects only the table's stream indexes are missing from the index, which is rebuilt by
    /// scanning the file when one of them is asked for. The report is placed at the offset the <c>/XRefStm</c> names, or
    /// at the table when that offset lies outside the file.
    /// </summary>
    public const string XRefHybridStreamLost = "xref.hybrid-stream-lost";

    /// <summary>
    /// A cross-reference stream near the offset a hybrid file's table names through <c>/Prev</c>, where that offset holds
    /// no section, may be that table's own — its dictionary gives no <c>/Prev</c>, or the offset the table's names, and it
    /// starts no sooner than the stream the table's <c>/XRefStm</c> read, or names again —, and was passed over for another
    /// section near that offset, which the search took first; the chain did not reach the stream, at its header or inside
    /// its object number: a reading from inside the number reads the same stream where it reads a header there, and where
    /// it reads none, as from the 0 of a number 20, the search near that place finds the stream moved (#156). Only a stream
    /// that declares itself one is reported: its dictionary gives <c>/Type /XRef</c>, and its header is a token a load
    /// reads from the start of its line, not in a comment or a string there. A header whose object number is written with a
    /// run of signs that starts with a <c>+</c>, which a load reads as its first sign, is such a token from that sign,
    /// where the stream is said to start; one a <c>-</c> starts is no object's header (#327). Nothing is reported where one
    /// of the reader's limits left part of the chain unread — the number of its sections, a section it cut before the
    /// section said what it is, rows, a trailer, an object a value the rows or a link are read with leads to, or an index
    /// it filled —, the limit's report standing for what it left unread. Nor is anything reported anywhere in the document
    /// where <see cref="Documents.PdfReaderLimits.MaxTrailerLength"/> may have hidden a link the chain would have followed,
    /// or cut what would end it: wherever it cut the reading at an offset the chain names, whatever it cut, that the
    /// reading does not take for a section the guard cut — a dictionary, whatever its <c>/Type</c>, the parser keeping it
    /// as far as it read it and the object then read as one of another type (#279), or the object's header, its number, its
    /// generation or its <c>obj</c> keyword running on past the limit or white space or a comment between its tokens, or a
    /// token where no header starts, no object then read (#289); where the reading takes it for the guard's section, the
    /// chain's own cut withholds the reports already. At an offset a <c>/Prev</c> or <c>startxref</c> names, where any
    /// <c>/W</c> makes a stream of any <c>/Type</c> the section (#263), the cut may have hidden one — <c>/XRef</c> parted,
    /// inside the name or inside a <c>#xx</c> escape, a stream of another <c>/Type</c> that gives a <c>/W</c>, a
    /// <c>/Type</c> given twice whose later value is <c>/XRef</c>, the whole dictionary past a header the cut parted —, the
    /// object's <c>/Prev</c> not followed, wherever it lies; at one an <c>/XRefStm</c> names, whose stream's <c>/Prev</c>
    /// the chain never follows, the cut is what would end the chain, as a guard's cut of that stream does (#264). The
    /// reader cannot tell what the cut hid without reading past the guard: a dictionary of another type cut where it gives
    /// no <c>/W</c>, or a token no header starts, withholds the report too. Nor is anything reported anywhere in the
    /// document where white space and comments at an offset the chain names run past that limit, which turned the reading
    /// of the section there into the search near the offset. That search may take a section other than the one past them,
    /// and the reader cannot tell which without reading past the guard: once that guard is reached, no stream passed over
    /// is reported, even where the search finds the section whole past the white space, which only a
    /// <c>MaxTrailerLength</c> under the 512 bytes the search reaches past the offset allows (#289). A cut that leaves
    /// nothing unread — data past a stream's last row, an object past a whole reference it holds — leaves the report made,
    /// and so does the cut of the trailer of a classic table an <c>/XRefStm</c> names, whose links the chain never follows
    /// (#263): it hides no link the chain follows and ends nothing, the chain going on through the table that names it. Nor
    /// is anything reported where the chain indexed nothing: the whole file is rebuilt then, which reads every object. Its
    /// rows, if it is the table's own stream, are not in the index the chain read: the objects only it indexes are missing
    /// from it, and the report leaves the index as it is, rebuilding nothing for them — only a rebuild for another cause,
    /// which scans the file, finds them. The report is placed at the stream, once, however many searches passed over it.
    /// </summary>
    public const string XRefStreamPassedOver = "xref.stream-passed-over";

    /// <summary>
    /// An object's cross-reference entry places it outside the file. The report carries no position, since the index
    /// does not keep where the entry was read; its message gives the offset. A section named outside the file is
    /// <see cref="XRefSectionMissing"/>'s, or the rebuild's when <c>startxref</c> names it.
    /// </summary>
    public const string XRefEntryOutOfRange = "xref.entry-out-of-range";

    /// <summary>
    /// A cross-reference stream the chain reads gives rows of a reserved type: a type past 2, or a type field wider than 8
    /// bytes whose leading bytes are not all zero. ISO 32000-1 (7.5.8.3) defines types 0, 1 and 2, and reads any other as a
    /// reference to the null object, permitting new types to be defined in the future: each such row is read so, its object
    /// indexed as a free one's is, and no older section's row stands for it (#221), until the index is rebuilt for another
    /// fault, which reads the object as the scan finds it; but for one whose number was refused before it was read — by a
    /// newer section, or by a row of the stream's own for the same number —, which stands for nothing, its object rebuilt,
    /// as the message says (#208, #210). Reported once for each section, at the section, the first such row in the message
    /// and the others counted.
    /// </summary>
    public const string XRefEntryReserved = "xref.entry-reserved";

    /// <summary>
    /// A stream's <c>/Length</c> is not where its data ends: its <c>endstream</c> lies elsewhere and ends the data, the
    /// <c>/Length</c> gives no length the reader can take and the <c>endstream</c> ends the data, or no <c>endstream</c>
    /// follows the declared length — none before the next object or the end of the file, or none looked for once the
    /// document's searches read as much of the file as they may — and that length is kept, or the data runs to the next
    /// object, or, taken without a search, to the end of the file. A <c>/Length</c> that leads to an object one of the reader's limits cut, or left out of the index, is not
    /// judged: the limit's report says why.
    /// </summary>
    public const string StreamLengthInvalid = "stream.length-invalid";

    /// <summary>
    /// A stream has no <c>endstream</c> before the next object or the end of the file: its object's <c>endobj</c> ends its
    /// data — after its declared length, or where the object ends —, or its data runs to the end of the file. Data taken
    /// without a search, the document's searches having read as much of the file as they may, is
    /// <see cref="StreamLengthInvalid"/>'s, whose <c>endstream</c> no search said is missing.
    /// </summary>
    public const string StreamTruncated = "stream.truncated";

    /// <summary>
    /// An object stream's dictionary names an object the stream holds — as its <c>/Type</c>, <c>/Length</c>, <c>/N</c>,
    /// <c>/First</c>, a filter or one of its parameters —, which cannot be read before the stream is: the stream was
    /// decoded without it.
    /// </summary>
    public const string StreamSelfReference = "stream.self-reference";

    /// <summary>
    /// An object is in the object stream its entry names, at another index than the entry gives: the object stream's
    /// header lists it elsewhere, and it was read from there. The report is placed where the object stream's data
    /// starts, and names the object, the object stream and both indexes.
    /// </summary>
    public const string ObjectStreamMemberMoved = "object-stream.member-moved";

    /// <summary>
    /// An object an object stream holds is written as a stream — a dictionary followed by <c>stream</c> —, or holds one,
    /// which an object stream cannot hold (ISO 32000-1 7.5.7): it reads as null, and its data is not read. The report is
    /// placed where the object stream's data starts, and names the object, the object stream and the byte the object
    /// starts at; it is made once for each object and each definition of its object stream.
    /// </summary>
    public const string ObjectStreamMemberStream = "object-stream.member-stream";

    /// <summary>
    /// What an object stream — a stream an entry of the index names as one — says of itself cannot be believed: it is not
    /// of <c>/Type /ObjStm</c>, its <c>/N</c> is absent or no non-negative integer, or, when it declares objects, its
    /// <c>/First</c> is, or lies past its decoded data, or its header ends or breaks before listing as many objects as its <c>/N</c> declares —
    /// a pair that is not two integers, that gives a number no object can have or an offset no member can start at, whose
    /// offset is not past the one before it, or whose member would start at or past the end of the decoded data, unless a
    /// guard cut it. The header is read as far as it goes, whatever its <c>/N</c> claims: the objects listed before the
    /// fault, if any, are read; the others cannot be read from it. The report is placed where the object stream's data
    /// starts, and names the object stream and its fault.
    /// </summary>
    public const string ObjectStreamUnreadable = "object-stream.unreadable";

    /// <summary>
    /// A token stood where the syntax does not allow it: where a value or a dictionary key was expected, after a key
    /// that has no value, or closing an array with a dictionary's end. It was read as null, skipped, or it ended what
    /// it stood in.
    /// </summary>
    public const string SyntaxUnexpectedToken = "syntax.unexpected-token";

    /// <summary>
    /// An object ended before it was whole: the file, or an object stream's decoded data, ended in the middle of it — a value
    /// missing, or an array, a dictionary or a string it opened never closed —, or an <c>endobj</c> stood where a value or a
    /// key should be, inside an array or a dictionary it opened. What was read is kept: a string takes the bytes to the end of
    /// the data, and an <c>endobj</c> ends every container it finds open: the object's own, or, when the object lost it too, the
    /// next object's, the container then taking that object. An object an object stream holds is read to the next one's
    /// start, the last to the end of the decoded data: running into either, it is cut short there, and the message says
    /// which.
    /// Reported once, for the innermost construct left open, where it opens — where the value would start, when none is open;
    /// past the depth the reader follows, the container it skipped, which holds what it did not follow —, the constructs
    /// around it counted in the message; inside an object an object stream holds, where the stream's data
    /// starts, the member and the byte in the message. The edge of a window the reader grows is no end of the data, and a cut
    /// a guard made is the guard's: an object an object stream holds read past
    /// <see cref="Documents.PdfReaderLimits.MaxObjectLength"/> is <see cref="LimitObject"/>'s.
    /// </summary>
    public const string SyntaxTruncatedObject = "syntax.truncated-object";

    /// <summary>
    /// Nesting exceeded the depth the reader follows, or the stack the thread leaves the reader, or the validator's walk
    /// of what an object stream's decoding reads: what lies past it was skipped, or read as null.
    /// </summary>
    public const string SyntaxDepthExceeded = "syntax.depth-exceeded";

    /// <summary>
    /// A number is beyond what a real can hold — its magnitude rounds past the largest a double holds, about 1.8 × 10^308
    /// —: it was read as null, so a dictionary holds no entry for its key. The report quotes the number, and is placed
    /// where it starts, or, for a number inside an object an object stream holds, where the stream's data starts, the
    /// member and the byte in its message. An integer past the range of a <see langword="long"/> is no fault of its own:
    /// it reads as a real, the one nearest to it.
    /// </summary>
    public const string SyntaxNumberOutOfRange = "syntax.number-out-of-range";

    /// <summary>
    /// A hexadecimal string holds bytes that are neither hexadecimal digits nor white space, the only bytes ISO 32000-1
    /// (7.3.4.3) lets it hold — a <c>%</c> among them, which is no comment there —: they were skipped, as pdf.js and pdfium
    /// skip them, where PDFBox stops at the first, MuPDF and poppler read each as the digit 0, and qpdf and pypdf lose the
    /// string. Reported once for each string, where
    /// its first such byte lies, how many there are in the message; inside an object an object stream holds, where the
    /// stream's data starts, the member and the byte in the message.
    /// </summary>
    public const string SyntaxHexStringInvalid = "syntax.hex-string-invalid";

    /// <summary>
    /// A name holds a number sign that two hexadecimal digits do not follow: it was kept as the byte it is, as PDF 1.1 read
    /// it and most readers still do, where ISO 32000-1 (7.3.5) has a writer write <c>#23</c>. Reported once for each name,
    /// where its first such number sign lies, the name read quoted in the message; inside an object an object stream holds,
    /// where the stream's data starts, the member and the byte in the message. A <c>#00</c> is no such fault: it is an
    /// escape, of a byte no name may hold, which the validator reports.
    /// </summary>
    public const string SyntaxNameEscapeInvalid = "syntax.name-escape-invalid";

    /// <summary>
    /// A dictionary gives a key more than once, which ISO 32000-1 (7.3.7) forbids: keys compare as they read, so a key written
    /// with <c>#xx</c> escapes repeats the one written without. The last value given is kept, and a null given last removes
    /// the key, as qpdf, pdf.js, PDFBox, MuPDF and pdfium read it; a key given with no value, before <c>&gt;&gt;</c> or
    /// <c>endobj</c>, is one given null, and a value a guard's cut leaves short removes nothing. Reported for each key given
    /// again, where it is given, unless a window's edge reaches the key itself, the
    /// key quoted in the message; inside an object an object stream holds, where the stream's data starts, the member and the
    /// byte in the message.
    /// </summary>
    public const string SyntaxKeyRepeated = "syntax.key-repeated";

    /// <summary>
    /// Rebuilding the index met more than one definition of an object number: the file was updated, or copies an
    /// object. It is reported once for each rebuild, as information with no position: how many definitions met a number
    /// already found, the first ten of those numbers, and which definition was kept: the one the file writes last, a member
    /// of an object stream counting where the stream's definition lies — an update appends what it changes, written
    /// directly or in a new object stream (ISO 32000-1, 7.5.6) —, and, of a number one object stream lists twice, the first
    /// listed (#189). Only the definition kept of an object stream's own number is read for members, but where that number
    /// is listed by a later object stream after the earlier stream was taken in: its members then stay placed in it, and
    /// read as null (#348).
    /// </summary>
    public const string ObjectRedefined = "object.redefined";

    /// <summary>
    /// The trailer's <c>/Root</c> did not lead to a document catalog, and the reader found the catalog among the
    /// file's objects: among those the file indexes when its index is sound, in a rebuilt index otherwise. A rebuild after
    /// opening that takes the same object again reports nothing more; one that takes another object reports it.
    /// </summary>
    public const string TrailerRootRecovered = "trailer.root-recovered";

    /// <summary>
    /// A filter's data is not what the filter says. Flate data that nothing could decode was left as the filters before it
    /// left it, and decoding stopped there; data that decoded in part — a Flate stream that lost its tail or turned corrupt,
    /// an LZW stream that uses a code it has not defined, ASCII85 data a <c>~</c> ends that something other than
    /// <c>&gt;</c> follows — was kept as far as it went; data that may have decoded wrong — a byte passed over that encodes
    /// nothing, an ASCII85 group past four bytes or of a single digit, data that ends inside a group, a pair, a run or an
    /// LZW code without its end marker — is a warning too; data read despite a fault that lost nothing, such as a missing
    /// zlib header or checksum, an end marker missing after a whole unit or ending on a <c>~</c>, or the <c>&lt;~</c>
    /// PostScript writes, is reported as a repair. The message says which; for a Flate stream turned corrupt, a byte that
    /// encodes nothing, an ASCII85 group past four bytes and a <c>~</c> something other than <c>&gt;</c> follows, it says at
    /// which byte of the filter's data the first lies. A cross-reference stream whose Flate data is so left, corrupt before
    /// its first byte decodes, holds no rows: the section cannot be read (#221).
    /// </summary>
    public const string FilterFailed = "filter.failed";

    /// <summary>
    /// A Flate stream's data decoded to its end, but the zlib checksum that follows it disagrees with what it
    /// decoded to: all of it was kept, as other readers keep it, and some of it may be wrong. Of data that decoded to
    /// nothing, the report says only that it decoded to nothing.
    /// </summary>
    public const string FilterChecksumMismatch = "filter.checksum-mismatch";

    /// <summary>
    /// A filter named by the file is not supported, and decoding stopped there: the data is what the filters before
    /// it decoded, or the stream's own when it is the first. A <c>/Filter</c> entry that is neither a name nor an
    /// array leaves the data as the stream holds it. A cross-reference stream whose data is so left, some of it still to
    /// decode, holds no rows: the section cannot be read (#221).
    /// </summary>
    public const string FilterUnsupported = "filter.unsupported";

    /// <summary>
    /// A filter of a stream decodes to more than <see cref="Documents.PdfReaderLimits.MaxDecodedStreamLength"/>, or the
    /// filters before the last pass along more than it together, and decoding stopped there: the data comes back as the
    /// filters after the cut decode what was kept within the bound. Like every <c>limit.*</c> code, the guard is the
    /// reader's, not damage in the file.
    /// </summary>
    public const string LimitDecodedStream = "limit.decoded-stream";

    /// <summary>
    /// A stream's encoded data runs past <see cref="Documents.PdfReaderLimits.MaxStreamLength"/>, and was read up to it:
    /// what lies within it is decoded as data the reader cut, and handed back as it is where decoding hands its data back
    /// undecoded. Reported once for each stream in the document's diagnostics, when its data is first read, and in
    /// diagnostics of your own each time you decode it. Like every <c>limit.*</c> code, the guard is the reader's, not
    /// damage in the file.
    /// </summary>
    public const string LimitStreamLength = "limit.stream-length";

    /// <summary>
    /// An object is longer than <see cref="Documents.PdfReaderLimits.MaxObjectLength"/>, and only the part
    /// within it was parsed. An object an object stream holds is bounded alike, and reported once for each definition of
    /// its object stream, where that stream's data starts, the object, the object stream and the byte the object starts at
    /// in the message.
    /// </summary>
    public const string LimitObject = "limit.object";

    /// <summary>
    /// A classic cross-reference section is longer than
    /// <see cref="Documents.PdfReaderLimits.MaxXRefSectionLength"/>, and only the entries within it were read.
    /// </summary>
    public const string LimitXRefSectionLength = "limit.xref-section-length";

    /// <summary>
    /// The chain of cross-reference sections is longer than
    /// <see cref="Documents.PdfReaderLimits.MaxXRefSectionCount"/>, and the older sections were not read.
    /// </summary>
    public const string LimitXRefSectionCount = "limit.xref-section-count";

    /// <summary>
    /// A trailer, or a cross-reference stream's dictionary, measured from its object's header, is longer than
    /// <see cref="Documents.PdfReaderLimits.MaxTrailerLength"/>, and only the part within it was parsed; or the white space
    /// and comments an offset that names a cross-reference section falls short of it by run past that bound, and the section
    /// was not read from that offset: it is looked for as one that is not there, near the offset, where a search may find it
    /// past them, or, for the one <c>startxref</c> names, by rebuilding the index.
    /// </summary>
    public const string LimitTrailer = "limit.trailer";

    /// <summary>
    /// The index holds <see cref="Documents.PdfReaderLimits.MaxIndexEntries"/> object numbers, and took no more: the
    /// objects past them, in the order the reader met them, were not indexed, and read as null.
    /// </summary>
    public const string LimitIndexEntries = "limit.index-entries";

    /// <summary>
    /// A stream's <c>/Filter</c> array names more filters than <see cref="Documents.PdfReaderLimits.MaxFilterChainLength"/>,
    /// and decoding stopped before the first past them: the data is what the filters within the bound decoded.
    /// </summary>
    public const string LimitFilterChain = "limit.filter-chain";
}
