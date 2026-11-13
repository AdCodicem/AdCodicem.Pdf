using System.Globalization;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.IO;

/// <summary>
/// One read of a search for <c>endstream</c> as the rule for object headers sees it (<see cref="FindHeader"/>): its
/// bytes, after a few that stand for what the search read before them.
/// </summary>
/// <remarks>
/// <para>
/// Whether <c>obj</c> ends an object header depends on what precedes it — runs of white space and of digits, and lines that
/// end in a comment (#261) —, which may run back past the start of a read, as far as the start of the data. What the reads
/// before hold is carried into each one in three parts, of a few bytes or fields each, which decide every header as those
/// reads would:
/// </para>
/// <list type="bullet">
/// <item>the runs of white space or of digits that end where the read starts, four at most — each run of white space as one
/// space, each run of digits as the digits of its value, or of <see cref="Beyond"/> for any larger one, which no number nor
/// generation reaches however many digits follow —, and the byte before them, which no run reads: what a walk back from an
/// <c>obj</c> reads across the read's start by digits, within the line the read starts in, then, past that line's start, as
/// the next part gives it;</item>
/// <item>what a walk back across the end of line before that line takes of the lines before it: two numbers at most, the
/// nearer last, each as the digits of its value and a space, and before them a byte that says whether the farther starts a
/// token, a space where it does and a regular character where it does not. A walk that lexed the line the read starts in
/// reads on from there;</item>
/// <item>the line the read starts in, lexed from its start as a load reads it, as far as the read's start
/// (<see cref="LineTokens"/>).</item>
/// </list>
/// <para>
/// The start of the data is carried as a space, which ends a token as white space does and holds no digit. The bytes carried
/// have negative indexes, and each records a place in the file: the space for the start of the data, where the data starts;
/// a run of the first part, where its first byte lies, and the byte before the runs, where it lies — or, where that byte was
/// carried into the read, the place it records —; a number of the second part, its digits and the space after them, where
/// the number starts; and the other bytes of that part, which no number places — the byte before its numbers, and both its
/// bytes where the walk took none —, -1, nowhere in the file. Each digit of a run or of a number so records where its number
/// starts; no other place carried gives where a header, or a number carried, starts (<see cref="CarriedStarts"/>). None of
/// the sizes below bounds what the file holds (ADR 34): a run of any length is carried, as what decides a header — its kind
/// and its value —, and a line of any length, as its lexer's state.
/// </para>
/// </remarks>
internal ref struct HeaderText
{
    /// <summary>The value a run of digits is taken for when it is larger: past any object number and generation.</summary>
    internal const long Beyond = (long)PdfObjectId.MaxNumber + 1;

    /// <summary>The most runs of white space or digits before <c>obj</c> that decide a header: those it holds.</summary>
    private const int MaxRuns = 4;

    /// <summary>The most digits a run of digits is carried as: those of <see cref="Beyond"/>.</summary>
    private const int MaxDigits = 10;

    /// <summary>
    /// The most bytes a view carries: the byte before its runs, two runs of white space and two of digits; or the byte before
    /// its numbers, and two numbers with a space after each.
    /// </summary>
    private const int ViewLength = 1 + (2 * (1 + MaxDigits));

    /// <summary>The most bytes carried into a read: those of its two views.</summary>
    internal const int CarriedLength = 2 * ViewLength;

    /// <summary>
    /// The most numbers carried into a read (<see cref="CarriedStarts"/>): two runs of digits, two numbers before the line it
    /// starts in, and that line's last two tokens.
    /// </summary>
    internal const int MostNumbersCarried = 6;

    private readonly Span<byte> _runs;
    private readonly Span<long> _runsAt;
    private readonly Span<byte> _lead;
    private readonly Span<long> _leadAt;
    private int _runsLength;
    private int _leadLength;
    private LineTokens _line;
    private ReadOnlySpan<byte> _bytes;
    private long _at;

    /// <summary>Starts a search at <paramref name="dataStart"/>, before which nothing is read.</summary>
    /// <param name="carried">Room for <see cref="CarriedLength"/> bytes carried into a read.</param>
    /// <param name="carriedAt">Room for the place in the file each of them records.</param>
    /// <param name="dataStart">Where the data starts in the file.</param>
    public HeaderText(Span<byte> carried, Span<long> carriedAt, long dataStart)
    {
        _runs = carried[..ViewLength];
        _runsAt = carriedAt[..ViewLength];
        _lead = carried[ViewLength..CarriedLength];
        _leadAt = carriedAt[ViewLength..CarriedLength];
        _runs[0] = (byte)' ';
        _runsAt[0] = dataStart;
        _runsLength = 1;
        _lead[0] = (byte)' ';
        _leadAt[0] = dataStart;
        _leadLength = 1;
    }

    /// <summary>
    /// What a walk back from an <c>obj</c> reads through, past the start of the read: the runs that end there, or what precedes
    /// the line the read starts in.
    /// </summary>
    private enum View : byte
    {
        /// <summary>The runs of white space and digits that end where the read starts.</summary>
        Runs,

        /// <summary>What precedes the line the read starts in.</summary>
        Lead,
    }

    /// <summary>Gets the bytes of the read.</summary>
    public readonly ReadOnlySpan<byte> Bytes => _bytes;

    /// <summary>Takes <paramref name="bytes"/>, read at <paramref name="at"/> in the file, as the read that follows what is carried.</summary>
    public void Read(ReadOnlySpan<byte> bytes, long at)
    {
        _bytes = bytes;
        _at = at;
    }

    /// <summary>
    /// Gives where the byte at <paramref name="index"/> is in the file, or, for a byte carried, the place it records
    /// (<see cref="HeaderText"/>).
    /// </summary>
    public readonly long PositionOf(int index) => PositionOf(index, 0, View.Runs);

    /// <summary>
    /// Carries into the next read, which starts at <paramref name="position"/> in this one, the runs of white space and
    /// of digits that end there and the byte before them, what precedes the line it starts in, and that line, lexed so far.
    /// </summary>
    public void CarryTo(long position)
    {
        var end = (int)(position - _at);
        var endOfLine = _bytes[..end].LastIndexOfAny((byte)'\r', (byte)'\n');

        if (endOfLine < 0)
        {
            _line.Read(_bytes[..end], _at);
            CarryRuns(end, 0, View.Runs);
            return;
        }

        // A line starts past the end of line: what precedes it is what a walk back across that end of line reads, read whole
        // before the view is written over.
        var numbers = Walk(endOfLine);
        var line = default(LineTokens);
        line.Read(_bytes[(endOfLine + 1)..end], _at + endOfLine + 1);
        _line = line;
        CarryLead(numbers);
        CarryRuns(end, endOfLine + 1, View.Lead);
    }

    /// <summary>
    /// Writes into <paramref name="starts"/> where each number carried into the next read starts in the file, each place
    /// once and the nearest the start of the data first, and gives how many: room for <see cref="MostNumbersCarried"/>.
    /// </summary>
    /// <remarks>
    /// A header the next reads find that starts before them starts at one of these places (#261): its number is a run of
    /// digits a walk reads back through what is carried — a run of the first view, a number of the second, a token of the
    /// line lexed so far —, each recording where it starts; a token the line's bytes so far end in, which the next read may
    /// go on, is the first view's last run. A number carried into a read that starts before it was carried into the read
    /// before, so a place is first given by the read that holds it, and then by each read after it, as long as a header may
    /// still start there.
    /// </remarks>
    public readonly int CarriedStarts(Span<long> starts)
    {
        var count = 0;

        // Each view keeps its first byte for what precedes its numbers, which no number starts at; every digit of a number
        // records where the number starts.
        for (var index = 1; index < _runsLength; index++)
        {
            if (PdfCharacters.IsDigit(_runs[index]))
            {
                count = AddStart(starts, count, _runsAt[index]);
            }
        }

        for (var index = 1; index < _leadLength; index++)
        {
            if (PdfCharacters.IsDigit(_lead[index]))
            {
                count = AddStart(starts, count, _leadAt[index]);
            }
        }

        if (_line.Last.Kind == LineTokens.Kind.Digits)
        {
            count = AddStart(starts, count, _line.Last.Start);
        }

        return _line.Before.Kind == LineTokens.Kind.Digits ? AddStart(starts, count, _line.Before.Start) : count;
    }

    /// <summary>
    /// Finds the first object header whose <c>obj</c> ends before <paramref name="before"/> in the read, and gives where
    /// it starts in the file; -1 when there is none.
    /// </summary>
    /// <remarks>
    /// A header is <c>N G obj</c> at a token boundary, as the file's bytes hold it: white space, a delimiter that ends a
    /// token or the start of the data before the number; a run of digits whose value the parser takes for an object number, 1 to
    /// 2,147,483,647, and one whose value it takes for a generation, 0 to 65,535, however many zeros lead them; white
    /// space before each of the generation and <c>obj</c>, as much as the file holds, and comments between them, as the
    /// scan that rebuilds an index reads them (#261): where the white space crossed holds an end of line and the line it
    /// ends holds a <c>%</c>, that line is lexed from its start as a load reads it, its last tokens are the numbers — digits
    /// alone —, and a line that holds only a comment is passed; and white space, a delimiter or the end of the search after
    /// <c>obj</c>. <c>endobj</c> is no header, nor <c>10 0 objx</c>, nor digits that a regular character, an opening
    /// parenthesis or angle bracket, a solidus or a percent sign precedes — which a load lexes into a longer token, a string,
    /// a name or a comment —, nor a sign before a number, nor digits a comment ends with, which were once taken for a
    /// number. The scan that rebuilds an index refuses the same (#171, #189), but reads a number a run of signs precedes as
    /// the lexer reads it, the first sign deciding (#327), and digits glued to the end-of-file marker <c>%%EOF</c>, a
    /// writer's next update (#171): the rule reads digits alone, after a byte that ends a token, so that a stream whose data
    /// holds <c>+4 0 obj</c> or <c>%%EOF4 0 obj</c> is searched past it, to an <c>endstream</c> or a header after it.
    /// A header lies whole in what the search reads, from the start of the data to the next object the index places: the
    /// stream's own header, and whatever precedes the data, is never one, and one that the next object's offset cuts is
    /// not seen, that offset stopping the search first. The rule reads nothing but the bytes, so where it stops cannot
    /// depend on what was read before; and what a read's start cuts of a header is carried into it, and <c>obj</c> with
    /// the byte after it is whole in the read that decides it, so where the reads start cannot change it either: a
    /// candidate whose <c>obj</c> ends the read, where the search goes on, is left to the next read, which repeats it. A
    /// walk enters a line only through the white space after it, which the walk of one <c>obj</c> at most crosses, besides
    /// the walk that carries the end of the read into the next: a line the read holds is lexed twice at most, and the line
    /// the read starts in once, as it is read, so that what the search lexes is bounded by what it reads, a comment line
    /// however long included. The line the search's first <c>endstream</c> lies on is lexed once more, up to the keyword, to
    /// tell whether a comment holds it (<see cref="PendsHeader"/>): once in a search.
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
                return start;
            }

            from = after;
        }
    }

    /// <summary>
    /// Gives where an object header would start in the file, as <see cref="FindHeader"/> takes one, were the token that
    /// starts at <paramref name="tokenStart"/> in the read <c>obj</c>, ended by white space, a delimiter or the end of the
    /// search; or -1 where the rule takes no header there. Only the bytes before <paramref name="tokenStart"/> are read:
    /// whether the token is <c>obj</c>, and what ends it, is the caller's to tell — <see cref="PendingEndStream"/> reads
    /// this at the token's first byte, while the read still holds the bytes before it, and keeps it only once the token
    /// proves to be <c>obj</c>.
    /// </summary>
    public readonly long HeaderAt(int tokenStart) => TryFindStart(tokenStart, out var start) ? start : -1;

    /// <summary>
    /// Determines whether the byte at <paramref name="index"/> in the read, where an <c>endstream</c> starts, lies inside a
    /// comment of its line, lexed from the line's start as a load reads it, on a line whose tokens before the comment may
    /// begin an object header that ends past it: a run of digits last, or no token, which a walk back from a later
    /// <c>obj</c> reads across (<see cref="PendingEndStream"/>, #261). A line that starts before the read is the one carried
    /// into it.
    /// </summary>
    public readonly bool PendsHeader(int index)
    {
        var lineStart = _bytes[..index].LastIndexOfAny((byte)'\r', (byte)'\n') + 1;
        var tokens = lineStart > 0 ? default : _line;
        tokens.Read(_bytes[lineStart..index], _at + lineStart);
        return tokens.InComment && tokens.Last.Kind != LineTokens.Kind.Other;
    }

    /// <summary>
    /// Reads back from the <c>obj</c> at <paramref name="keyword"/> for the rest of an object header, as
    /// <see cref="FindHeader"/> takes one, and gives where its number starts in the file.
    /// </summary>
    private readonly bool TryFindStart(int keyword, out long start)
    {
        start = -1;

        // The byte before obj is the read's or one carried, the space that stands for the start of the data at worst, from
        // which no number is read.
        if (!PdfCharacters.IsWhitespace(ByteAt(keyword - 1, 0, View.Runs)))
        {
            return false;
        }

        var numbers = Walk(keyword - 1);

        if (numbers.Count < 2 || !numbers.Farther.StartsToken ||
            numbers.Farther.Value is < 1 or > PdfObjectId.MaxNumber || numbers.Nearer.Value > PdfObjectId.MaxGeneration)
        {
            return false;
        }

        start = numbers.Farther.Start;
        return true;
    }

    /// <summary>
    /// Walks back from the white space at <paramref name="index"/>, the nearer number first, and gives the numbers an object
    /// header holds before it, two at most: each a run of digits read back over the white space after it, unless that white
    /// space holds an end of line and the line it ends holds a <c>%</c>, which is lexed from its start, its last tokens
    /// taken, a line that holds only a comment passed. The walk stops at what is no number, however few it took.
    /// </summary>
    private readonly Numbers Walk(int index)
    {
        var numbers = default(Numbers);
        var view = View.Runs;

        while (numbers.Count < 2)
        {
            var first = view == View.Runs ? -_runsLength : -_leadLength;
            var endOfLine = false;

            while (index > first && PdfCharacters.IsWhitespace(ByteAt(index, 0, view)))
            {
                endOfLine |= ByteAt(index, 0, view) is (byte)'\r' or (byte)'\n';
                index--;
            }

            // A line is lexed where the white space crossed ends it: one this read holds from its start, or the line the read
            // starts in, whose start lies before it — whose tokens the walk reads on from with what precedes it.
            if (endOfLine && view == View.Runs)
            {
                var lineEnd = Math.Max(0, index + 1);
                var lineStart = _bytes[..lineEnd].LastIndexOfAny((byte)'\r', (byte)'\n') + 1;

                if (lineStart > 0)
                {
                    if (_bytes[lineStart..lineEnd].Contains((byte)'%'))
                    {
                        var tokens = default(LineTokens);
                        tokens.Read(_bytes[lineStart..lineEnd], _at + lineStart);
                        tokens.Finish();

                        if (!numbers.TakeLine(tokens))
                        {
                            return numbers;
                        }

                        index = lineStart - 1;
                        continue;
                    }
                }
                else if (_line.HoldsPercent || _bytes[..lineEnd].Contains((byte)'%'))
                {
                    var tokens = _line;
                    tokens.Read(_bytes[..lineEnd], _at);
                    tokens.Finish();

                    if (!numbers.TakeLine(tokens))
                    {
                        return numbers;
                    }

                    view = View.Lead;
                    index = -1;
                    continue;
                }
            }

            var runEnd = index + 1;

            while (index > first && PdfCharacters.IsDigit(ByteAt(index, 0, view)))
            {
                index--;
            }

            if (index + 1 == runEnd)
            {
                return numbers;
            }

            numbers.Take(
                Value(index + 1, runEnd, 0, view), PositionOf(index + 1, 0, view), PdfCharacters.EndsToken(ByteAt(index, 0, view)));
        }

        return numbers;
    }

    /// <summary>
    /// Writes what <paramref name="numbers"/> a walk read back across an end of line as the view of what precedes the line
    /// after it: the nearer number's digits, the farther's before them, each followed by a space, and before them a byte
    /// that says whether the farther starts a token — a space where it does, a regular character where it does not —, so
    /// that a walk reading them back by their digits takes what the walk took, and no more.
    /// </summary>
    private void CarryLead(Numbers numbers)
    {
        var length = 1;
        _lead[0] = (byte)' ';
        _leadAt[0] = -1;

        if (numbers.Count == 2)
        {
            _lead[0] = numbers.Farther.StartsToken ? (byte)' ' : (byte)'x';
            length = CarryNumber(numbers.Farther, length);
        }
        else if (numbers.Count == 1)
        {
            _lead[0] = numbers.Nearer.StartsToken ? (byte)' ' : (byte)'x';
        }

        if (numbers.Count > 0)
        {
            length = CarryNumber(numbers.Nearer, length);
        }
        else
        {
            _lead[length] = (byte)' ';
            _leadAt[length] = -1;
            length++;
        }

        _leadLength = length;
    }

    /// <summary>Writes <paramref name="taken"/>'s digits and a space after them at <paramref name="at"/> in the lead, and gives where they end.</summary>
    private readonly int CarryNumber(Taken taken, int at)
    {
        // The view has ten digits for each number, and no value carried has more.
        _ = taken.Value.TryFormat(_lead[at..], out var written, default, CultureInfo.InvariantCulture);
        _lead[at + written] = (byte)' ';
        _leadAt.Slice(at, written + 1).Fill(taken.Start);
        return at + written + 1;
    }

    /// <summary>
    /// Writes the runs of white space and of digits that end at <paramref name="end"/> in the read, from
    /// <paramref name="start"/> on and then through <paramref name="view"/>, four at most, and the byte before them, as the
    /// view of the runs that end where the next read starts.
    /// </summary>
    private void CarryRuns(int end, int start, View view)
    {
        Span<long> runAt = stackalloc long[MaxRuns];
        Span<long> runValue = stackalloc long[MaxRuns];
        var first = start - (view == View.Runs ? _runsLength : _leadLength);
        var runs = 0;

        while (runs < MaxRuns && end > first + 1 &&
            (PdfCharacters.IsDigit(ByteAt(end - 1, start, view)) || PdfCharacters.IsWhitespace(ByteAt(end - 1, start, view))))
        {
            var digits = PdfCharacters.IsDigit(ByteAt(end - 1, start, view));
            var runStart = end;

            while (runStart > first + 1 &&
                (digits ? PdfCharacters.IsDigit(ByteAt(runStart - 1, start, view)) : PdfCharacters.IsWhitespace(ByteAt(runStart - 1, start, view))))
            {
                runStart--;
            }

            runAt[runs] = PositionOf(runStart, start, view);
            runValue[runs] = digits ? Value(runStart, end, start, view) : -1;
            end = runStart;
            runs++;
        }

        // What is carried is read whole before it is written over.
        var before = ByteAt(end - 1, start, view);
        _runsAt[0] = PositionOf(end - 1, start, view);
        _runs[0] = before;
        var length = 1;

        for (var run = runs - 1; run >= 0; run--)
        {
            var written = 1;

            if (runValue[run] < 0)
            {
                _runs[length] = (byte)' ';
            }
            else
            {
                // The room carried has ten digits for each run of digits, and no value carried has more.
                _ = runValue[run].TryFormat(_runs[length..], out written, default, CultureInfo.InvariantCulture);
            }

            _runsAt.Slice(length, written).Fill(runAt[run]);
            length += written;
        }

        _runsLength = length;
    }

    /// <summary>
    /// Gives the byte at <paramref name="index"/>: in the read from <paramref name="start"/> on, or before it, counted back,
    /// in <paramref name="view"/>.
    /// </summary>
    private readonly byte ByteAt(int index, int start, View view) =>
        index >= start ? _bytes[index]
        : view == View.Runs ? _runs[_runsLength + index - start]
        : _lead[_leadLength + index - start];

    /// <summary>
    /// Gives where the byte at <paramref name="index"/> is in the file, or, for a byte carried, the place it records: in the
    /// read from <paramref name="start"/> on, or before it, counted back, in <paramref name="view"/>.
    /// </summary>
    private readonly long PositionOf(int index, int start, View view) =>
        index >= start ? _at + index
        : view == View.Runs ? _runsAt[_runsLength + index - start]
        : _leadAt[_leadLength + index - start];

    /// <summary>
    /// Adds <paramref name="start"/> to the first <paramref name="count"/> places of <paramref name="starts"/>, which run from
    /// the nearest the start of the data, where it is not one of them already, and gives how many there are then.
    /// </summary>
    private static int AddStart(Span<long> starts, int count, long start)
    {
        var at = count;

        while (at > 0 && starts[at - 1] > start)
        {
            at--;
        }

        if (at > 0 && starts[at - 1] == start)
        {
            return count;
        }

        // The views hold four numbers at most and the line two, so the room given is never short of one more.
        starts[at..count].CopyTo(starts[(at + 1)..]);
        starts[at] = start;
        return count + 1;
    }

    /// <summary>
    /// Gives the value of the digits from <paramref name="from"/> to <paramref name="to"/>, or <see cref="Beyond"/> when it is
    /// larger.
    /// </summary>
    private readonly long Value(int from, int to, int start, View view)
    {
        var value = 0L;

        for (var index = from; index < to; index++)
        {
            value = Math.Min((value * 10) + (ByteAt(index, start, view) - '0'), Beyond);
        }

        return value;
    }

    /// <summary>A number a walk took: its value, where it starts in the file, and whether it starts a token there.</summary>
    /// <param name="Value">Its value, at most <see cref="Beyond"/>.</param>
    /// <param name="Start">Where it starts in the file.</param>
    /// <param name="StartsToken">Whether what precedes it ends a token, so that it starts one.</param>
    private readonly record struct Taken(long Value, long Start, bool StartsToken);

    /// <summary>The numbers a walk back from an <c>obj</c> takes, two at most: the generation, nearer, then the number.</summary>
    private struct Numbers
    {
        /// <summary>Gets how many the walk took.</summary>
        public int Count { get; private set; }

        /// <summary>Gets the nearer number: a header's generation.</summary>
        public Taken Nearer { get; private set; }

        /// <summary>Gets the farther number: a header's object number.</summary>
        public Taken Farther { get; private set; }

        /// <summary>Takes the next number back.</summary>
        public void Take(long value, long start, bool startsToken)
        {
            if (Count == 0)
            {
                Nearer = new Taken(value, start, startsToken);
            }
            else
            {
                Farther = new Taken(value, start, startsToken);
            }

            Count++;
        }

        /// <summary>
        /// Takes the last tokens of a line <paramref name="tokens"/> lexed as the numbers still wanted, the nearest first, and
        /// says whether the walk goes on: a line that holds only a comment gives none, and one whose token is no run of digits
        /// alone ends the walk.
        /// </summary>
        public bool TakeLine(LineTokens tokens) =>
            tokens.Last.Kind == LineTokens.Kind.None ||
            (TakeToken(tokens.Last) && (Count == 2 || tokens.Before.Kind == LineTokens.Kind.None || TakeToken(tokens.Before)));

        /// <summary>Takes <paramref name="token"/> where it is a run of digits alone, which starts a token, and says whether it is.</summary>
        private bool TakeToken(LineTokens.Token token)
        {
            if (token.Kind != LineTokens.Kind.Digits)
            {
                return false;
            }

            Take(token.Value, token.Start, startsToken: true);
            return true;
        }
    }
}
