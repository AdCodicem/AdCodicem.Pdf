using System.Globalization;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO;

/// <summary>
/// One read of a search for <c>endstream</c> as the rule for object headers sees it (<see cref="FindHeader"/>): its
/// bytes, after a few that stand for what the search read before them.
/// </summary>
/// <remarks>
/// Whether <c>obj</c> ends an object header depends on the runs of white space and of digits before it, which may run
/// back past the start of a read, as far as the start of the data. What the reads before hold is carried into each one
/// as a few bytes that decide every header as they would: the runs of white space or of digits that end where the read
/// starts, four at most — each run of white space as one space, each run of digits as the digits of its value, or of
/// 2,147,483,648 for any larger one, which no number nor generation reaches however many digits follow —, and the byte
/// before them. A header holds four such runs, and asks of the byte before them only whether it ends a token: no run
/// reads that byte. The start of the data is carried as a space, which ends a token as white space does and holds no
/// digit. The bytes carried have negative indexes, and each records where the run it stands for starts in the file.
/// None of the sizes below bounds what the file holds (ADR 34): a run of any length is carried, as what decides a
/// header — its kind and its value.
/// </remarks>
internal ref struct HeaderText
{
    /// <summary>The most runs of white space or digits before <c>obj</c> that decide a header: those it holds.</summary>
    private const int MaxRuns = 4;

    /// <summary>The most digits a run of digits is carried as: those of <see cref="Beyond"/>.</summary>
    private const int MaxDigits = 10;

    /// <summary>The most bytes carried into a read: the byte before the runs, two runs of white space and two of digits.</summary>
    internal const int CarriedLength = 1 + (2 * (1 + MaxDigits));

    /// <summary>The value a run of digits is taken for when it is larger: past any object number and generation.</summary>
    private const long Beyond = (long)PdfObjectId.MaxNumber + 1;

    private readonly Span<byte> _carried;
    private readonly Span<long> _carriedAt;
    private int _carriedLength;
    private ReadOnlySpan<byte> _bytes;
    private long _at;

    /// <summary>Starts a search at <paramref name="dataStart"/>, before which nothing is read.</summary>
    /// <param name="carried">Room for <see cref="CarriedLength"/> bytes carried into a read.</param>
    /// <param name="carriedAt">Room for where the run each of them stands for starts in the file.</param>
    /// <param name="dataStart">Where the data starts in the file.</param>
    public HeaderText(Span<byte> carried, Span<long> carriedAt, long dataStart)
    {
        _carried = carried;
        _carriedAt = carriedAt;
        _carried[0] = (byte)' ';
        _carriedAt[0] = dataStart;
        _carriedLength = 1;
    }

    /// <summary>Gets the bytes of the read.</summary>
    public readonly ReadOnlySpan<byte> Bytes => _bytes;

    /// <summary>Gets the index of the first byte carried into the read: the byte before the runs, which no run reads.</summary>
    private readonly int FirstCarried => -_carriedLength;

    private readonly byte this[int index] => index >= 0 ? _bytes[index] : _carried[_carriedLength + index];

    /// <summary>Takes <paramref name="bytes"/>, read at <paramref name="at"/> in the file, as the read that follows what is carried.</summary>
    public void Read(ReadOnlySpan<byte> bytes, long at)
    {
        _bytes = bytes;
        _at = at;
    }

    /// <summary>Gives where the byte at <paramref name="index"/> is in the file, or, for a byte carried, where its run starts.</summary>
    public readonly long PositionOf(int index) => index >= 0 ? _at + index : _carriedAt[_carriedLength + index];

    /// <summary>
    /// Carries into the next read, which starts at <paramref name="position"/> in this one, the runs of white space and
    /// of digits that end there, and the byte before them.
    /// </summary>
    public void CarryTo(long position)
    {
        Span<long> runAt = stackalloc long[MaxRuns];
        Span<long> runValue = stackalloc long[MaxRuns];
        var end = (int)(position - _at);
        var runs = 0;

        while (runs < MaxRuns && end > FirstCarried + 1 &&
            (PdfCharacters.IsDigit(this[end - 1]) || PdfCharacters.IsWhitespace(this[end - 1])))
        {
            var digits = PdfCharacters.IsDigit(this[end - 1]);
            var start = RunStart(end, digits);
            runAt[runs] = PositionOf(start);
            runValue[runs] = digits ? Value(start, end) : -1;
            end = start;
            runs++;
        }

        // What is carried is read whole before it is written over.
        var before = this[end - 1];
        _carriedAt[0] = PositionOf(end - 1);
        _carried[0] = before;
        var length = 1;

        for (var run = runs - 1; run >= 0; run--)
        {
            var written = 1;

            if (runValue[run] < 0)
            {
                _carried[length] = (byte)' ';
            }
            else
            {
                // The room carried has ten digits for each run of digits, and no value carried has more.
                _ = runValue[run].TryFormat(_carried[length..], out written, default, CultureInfo.InvariantCulture);
            }

            _carriedAt.Slice(length, written).Fill(runAt[run]);
            length += written;
        }

        _carriedLength = length;
    }

    /// <summary>
    /// Finds the first object header whose <c>obj</c> ends before <paramref name="before"/> in the read, and gives where
    /// it starts in the file; -1 when there is none.
    /// </summary>
    /// <remarks>
    /// A header is <c>N G obj</c> at a token boundary, as the file's bytes hold it: white space, a delimiter or the start
    /// of the data before the number; a run of digits whose value the parser takes for an object number, 1 to
    /// 2,147,483,647, and one whose value it takes for a generation, 0 to 65,535, however many zeros lead them; white
    /// space before each of the generation and <c>obj</c>, as much as the file holds; and white space, a delimiter or the
    /// end of the search after <c>obj</c>. <c>endobj</c> is no header, nor <c>10 0 objx</c>, nor digits a regular
    /// character precedes, nor tokens a comment separates. The scan that rebuilds an index refuses <c>endobj</c> and
    /// tokens a comment separates too, but takes <c>10 0 objx</c> and digits a regular character precedes (#171).
    /// A header lies whole in what the search reads, from the start of the data to the next object the index places: the
    /// stream's own header, and whatever precedes the data, is never one, and one that the next object's offset cuts is
    /// not seen, that offset stopping the search first. The rule reads nothing but the bytes, so where it stops cannot
    /// depend on what was read before; and what a read's start cuts of a header is carried into it, and <c>obj</c> with
    /// the byte after it is whole in the read that decides it, so where the reads start cannot change it either: a
    /// candidate whose <c>obj</c> ends the read, where the search goes on, is left to the next read, which repeats it.
    /// <para>
    /// The rule is no guard (ADR 34): only a stream whose <c>endstream</c> does not follow its declared length is
    /// searched with it, which no valid file holds (ISO 32000-1, 7.3.8.1); a cross-reference stream whose <c>/Length</c>
    /// the chain could not read, which a valid file may hold, is searched without it (#220). A valid stream's data may
    /// hold text that reads as a header — an embedded PDF written uncompressed —: its length, confirmed, is never
    /// searched. An invalid stream whose data holds such text before its <c>endstream</c> stops there, as one whose next
    /// object came first, and its data ends there or before (#174); text that only looks like a header is read past, and
    /// an <c>endstream</c> after it ends the data.
    /// </para>
    /// </remarks>
    /// <param name="before">Where the <c>endstream</c> found in the read starts, or its length.</param>
    /// <param name="endsRange">Whether the read ends where the search does, where <c>obj</c> may end.</param>
    public readonly long FindHeader(int before, bool endsRange)
    {
        var from = 0;

        while (true)
        {
            var index = _bytes[from..before].IndexOf("obj"u8);

            if (index < 0)
            {
                return -1;
            }

            var keyword = from + index;
            var after = keyword + 3;

            // What follows obj past the end of the read is the next read's to see, which repeats it: the end of the read
            // ends the token only where the search ends.
            if ((after < _bytes.Length ? !PdfCharacters.IsRegular(_bytes[after]) : endsRange) &&
                TryFindStart(keyword, out var start))
            {
                return PositionOf(start);
            }

            from = after;
        }
    }

    /// <summary>
    /// Reads back from the <c>obj</c> at <paramref name="keyword"/> for the rest of an object header, as
    /// <see cref="FindHeader"/> takes one, and gives where its number starts.
    /// </summary>
    private readonly bool TryFindStart(int keyword, out int start)
    {
        var generationEnd = RunStart(keyword, digits: false);
        var generationStart = RunStart(generationEnd, digits: true);
        var numberEnd = RunStart(generationStart, digits: false);
        start = RunStart(numberEnd, digits: true);

        // Each run holds a byte at least, what precedes the number ends a token, and the values are an object number's
        // and a generation's.
        return generationEnd < keyword && generationStart < generationEnd && numberEnd < generationStart &&
            start < numberEnd && !PdfCharacters.IsRegular(this[start - 1]) &&
            Value(start, numberEnd) is >= 1 and <= PdfObjectId.MaxNumber &&
            Value(generationStart, generationEnd) <= PdfObjectId.MaxGeneration;
    }

    /// <summary>
    /// Gives where the run of digits, or of white space, that ends at <paramref name="end"/> starts, the first byte
    /// carried left out.
    /// </summary>
    private readonly int RunStart(int end, bool digits)
    {
        var start = end;

        while (start > FirstCarried + 1 &&
            (digits ? PdfCharacters.IsDigit(this[start - 1]) : PdfCharacters.IsWhitespace(this[start - 1])))
        {
            start--;
        }

        return start;
    }

    /// <summary>
    /// Gives the value of the digits from <paramref name="start"/> to <paramref name="end"/>, or <see cref="Beyond"/> when
    /// it is larger.
    /// </summary>
    private readonly long Value(int start, int end)
    {
        var value = 0L;

        for (var index = start; index < end; index++)
        {
            value = Math.Min((value * 10) + (this[index] - '0'), Beyond);
        }

        return value;
    }
}
