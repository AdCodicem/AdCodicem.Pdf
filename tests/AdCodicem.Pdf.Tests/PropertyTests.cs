using System.Globalization;
using System.Numerics;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.Objects;
using AdCodicem.Pdf.Validation;
using FsCheck;
using FsCheck.Fluent;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// Statements that must hold for <em>every</em> input, checked against generated data rather than against
/// a table of examples someone thought of.
/// </summary>
/// <remarks>
/// A different instrument from <see cref="FuzzingTests"/>, not a duplicate of it. Mutation fuzzing starts
/// from real documents and damages them, so it explores the neighborhood of files that exist. A
/// generator starts from nothing and reaches shapes no producer writes: an empty buffer, a file of
/// nothing but delimiters, a number carrying forty signs. The two find different defects.
///
/// Runs are seeded, so the suite is deterministic and a failure replays exactly — the same reason the
/// mutation campaign prints its seed. Both the seed and the number of cases are overridable through
/// <c>ADCODICEM_PROPERTY_SEED</c> and <c>ADCODICEM_PROPERTY_TESTS</c>, which is what a longer campaign uses.
/// </remarks>
public class PropertyTests
{
    /// <summary>Cases per property when nothing says otherwise: enough for a commit, not for a campaign.</summary>
    private const int DefaultCases = 200;

    private const ulong DefaultSeed = 0x5EED_1729UL;

    /// <summary>
    /// The second half of FsCheck's random state. It must be odd — FsCheck rejects an even gamma outright —
    /// which is why it is a constant here and only the seed is taken from the environment.
    /// </summary>
    private const ulong Gamma = 0x9E37_79B9_7F4A_7C15UL;

    private static int Cases =>
        int.TryParse(Environment.GetEnvironmentVariable("ADCODICEM_PROPERTY_TESTS"), out var value) && value > 0
            ? value
            : DefaultCases;

    private static ulong Seed =>
        ulong.TryParse(Environment.GetEnvironmentVariable("ADCODICEM_PROPERTY_SEED"), out var value)
            ? value
            : DefaultSeed;

    private static Config Settings =>
        Config.QuickThrowOnFailure
            .WithMaxTest(Cases)
            .WithReplay(Seed, Gamma)
            .WithQuietOnSuccess(true);

    /// <summary>Arbitrary byte buffers — the shape every byte that reaches this library arrives in.</summary>
    private static Arbitrary<byte[]> Buffers => ArbMap.Default.ArbFor<byte[]>().Filter(bytes => bytes is not null);

    /// <summary>What a number may start with: no sign, one, or the two some generators write, the first of which decides.</summary>
    private static Gen<string> Signs => Gen.Elements(string.Empty, "-", "+", "--", "+-", "-+");

    [Fact]
    public void The_lexer_terminates_on_any_bytes_and_stays_inside_the_buffer()
    {
        Check.One(Settings, Prop.ForAll(Buffers, bytes =>
        {
            // Every token but the last consumes at least one byte, so a lexer that is making progress
            // cannot produce more tokens than the buffer has bytes. Anything past that bound is a loop,
            // which is the denial of service invariant 4 forbids rather than a wrong answer.
            var budget = bytes.Length + 1;
            var lexer = new PdfLexer(bytes);
            var previousEnd = 0;

            for (var read = 0; read < budget; read++)
            {
                var token = lexer.Read();

                if (token.Kind == PdfTokenKind.EndOfInput)
                {
                    return true;
                }

                if (token.Start < previousEnd || token.End <= token.Start || token.End > bytes.Length)
                {
                    return false;
                }

                previousEnd = token.End;
            }

            return false;
        }));
    }

    [Fact]
    public void A_quote_of_the_file_is_printable_ascii_on_one_line_and_bounded_whatever_the_bytes()
    {
        Check.One(Settings, Prop.ForAll(Buffers, bytes =>
        {
            // A name or keyword holds any byte the file chose; what a message repeats of it holds none a terminal
            // or a log could take for something else, and never more than the bound's worth (#159).
            var keyword = FileQuote.Keyword(bytes);
            var name = FileQuote.Name(PdfName.Get(Encoding.Latin1.GetString(bytes)));
            const int Longest = 1 + (3 * FileQuote.MaxBytes) + 48;

            return keyword.All(c => c is >= ' ' and <= '~') && name.All(c => c is >= ' ' and <= '~') &&
                keyword.Length <= Longest && name.Length <= Longest;
        }));
    }

    [Fact]
    public void A_quote_of_a_name_of_at_most_127_bytes_reads_back_as_that_name()
    {
        Check.One(Settings, Prop.ForAll(Buffers, bytes =>
        {
            // The quote is the name as a writer writes it: lexed and decoded, it gives the bytes it quotes.
            var value = Encoding.Latin1.GetString(bytes.AsSpan(0, Math.Min(bytes.Length, FileQuote.MaxBytes)));
            var quote = Encoding.ASCII.GetBytes(FileQuote.Name(PdfName.Get(value)));
            var lexer = new PdfLexer(quote);
            var token = lexer.Read();

            return token.Kind == PdfTokenKind.Name && token.End == quote.Length &&
                PdfStringDecoder.DecodeName(token.Text, out _) == value;
        }));
    }

    [Fact]
    public void The_parser_answers_for_any_bytes_without_leaving_the_buffer()
    {
        Check.One(Settings, Prop.ForAll(Buffers, bytes =>
        {
            // Below the level where failure is expressed as an exception: whatever the bytes are, the
            // parser reports through diagnostics, returns an object, and leaves its position somewhere
            // inside the input it was given.
            var diagnostics = new PdfDiagnostics();
            var parser = new PdfObjectParser(bytes, diagnostics: diagnostics);
            _ = parser.ParseObject();

            return parser.Position >= 0 && parser.Position <= bytes.Length;
        }));
    }

    [Fact]
    public void A_buffer_that_ends_at_a_window_s_edge_reports_nothing_of_the_end_of_the_data()
    {
        Check.One(Settings, Prop.ForAll(Buffers, bytes =>
        {
            // Whatever the bytes are, a parser told they end at a window's edge, rather than where the data does, reports no
            // construct left open: the reader reads them again through a larger window, or reports the guard instead.
            var diagnostics = new PdfDiagnostics();
            var parser = new PdfObjectParser(bytes, diagnostics: diagnostics, endsData: false);
            _ = parser.ParseObject();

            return !diagnostics.Contains(PdfDiagnosticCodes.SyntaxTruncatedObject);
        }));
    }

    [Fact]
    public void What_a_window_reports_of_its_syntax_the_whole_data_reports_too_in_the_same_order()
    {
        // ADR 34: a window that is not the end of the data reports nothing of the token its edge cuts, nor of what follows
        // once the parse met the edge; what it met before the edge is the data's, and the whole data reports it too, at the
        // same place and in the same order — a key given again among them, whose value the edge cut, which its message says.
        // The syntax is drawn from fragments of PDF's, so that containers, strings, names, references and endobj meet the
        // edge at every byte.
        var fragments = Gen.Elements(
            "[", "]", "<<", ">>", "(", ")", "<", ">", "/A", "/B#2", "#", "1", "0", " ", "R", "endobj", "true", "\\", "%x\n", "\n", "41", "x", "stream\n");
        var cases =
            from count in Gen.Choose(1, 16)
            from parts in Gen.ArrayOf(fragments, count)
            let text = string.Concat(parts)
            from cut in Gen.Choose(0, text.Length)
            select (text, cut);

        // Each case parses a few dozen bytes twice: ten times the cases of a property cost less than one of the others.
        Check.One(Settings.WithMaxTest(10 * Cases), Prop.ForAll(cases.ToArbitrary(), drawn =>
        {
            var bytes = Encoding.ASCII.GetBytes(drawn.text);
            var whole = new PdfDiagnostics();
            var window = new PdfDiagnostics();
            _ = new PdfObjectParser(bytes, diagnostics: whole).ParseObject();
            _ = new PdfObjectParser(bytes.AsMemory(0, drawn.cut), diagnostics: window, endsData: false).ParseObject();

            var made = whole.Where(entry => entry.Code.StartsWith("syntax.", StringComparison.Ordinal)).Select(entry => (entry.Code, entry.Position)).ToList();
            var next = 0;

            foreach (var entry in window.Where(entry => entry.Code.StartsWith("syntax.", StringComparison.Ordinal)))
            {
                next = made.IndexOf((entry.Code, entry.Position), next) + 1;

                if (next == 0)
                {
                    return false;
                }
            }

            return true;
        }));
    }

    [Fact]
    public void A_text_string_survives_being_written_and_read_back()
    {
        // An unpaired surrogate is not text — no encoding round-trips one — so it sits outside this
        // property's domain rather than counting as a defect in it.
        var texts = ArbMap.Default.ArbFor<string>()
            .Filter(value => value is not null && IsWellFormedUtf16(value));

        Check.One(Settings, Prop.ForAll(texts, value => PdfString.FromText(value).ToText() == value));
    }

    [Fact]
    public void The_integer_parser_reads_every_long_and_never_wraps_past_one()
    {
        // Runs of 1 to 25 digits and of 300 to 320, around where a long and a double end, leading zeros included; every
        // value 64 bits hold; and long.MaxValue and long.MinValue, give or take 20. Within a long, the integer written;
        // past one, a real: the double nearest to it, an infinity past double.MaxValue. The signs are the doubled ones
        // some generators emit, the first of which decides.
        var runs = Gen.OneOf(Gen.Choose(1, 25), Gen.Choose(300, 320))
            .SelectMany(length => Gen.ArrayOf(Gen.Choose(0, 9), length))
            .Select(digits => string.Concat(digits));
        var bits = Gen.Zip(Gen.Choose(int.MinValue, int.MaxValue), Gen.Choose(int.MinValue, int.MaxValue))
            .Select(halves => BigInteger.Abs(((long)halves.Item1 << 32) | (uint)halves.Item2).ToString(CultureInfo.InvariantCulture));
        var edges = Gen.Choose(-20, 20)
            .Select(offset => (new BigInteger(long.MaxValue) + offset).ToString(CultureInfo.InvariantCulture));
        var cases = Gen.Zip(Signs, Gen.OneOf(runs, bits, edges));

        Check.One(Settings, Prop.ForAll(cases.ToArbitrary(), written =>
        {
            var (sign, magnitude) = written;
            var negative = sign.StartsWith('-');
            var value = BigInteger.Parse(magnitude, CultureInfo.InvariantCulture) * (negative ? -1 : 1);
            var parsed = PdfNumberParser.TryParse(Encoding.ASCII.GetBytes(sign + magnitude), out var integer, out var real, out var isReal);

            if (value >= long.MinValue && value <= long.MaxValue)
            {
                return parsed && !isReal && integer == (long)value;
            }

            var nearest = double.Parse(magnitude, NumberStyles.None, CultureInfo.InvariantCulture);
            return parsed && isReal && SameBits(real, negative ? -nearest : nearest);
        }));
    }

    [Fact]
    public void The_real_parser_reads_the_double_nearest_to_the_decimal()
    {
        // Doubles from 1e-10 to 1e30, written as M03 writes a real it computes — their shortest decimal, expanded, with
        // a period; the decimals below 1 that matrices, colors and opacities are made of, with 1 to 17 significant digits
        // and up to 9 zeros after the period; and mantissas either side of 2^53, or of a few digits, with 0 to 25
        // decimals, either side of where the parser's exact division gives way to the framework's parse. Equal bit for
        // bit, -0.0 included, to the framework's parse of the same decimal with one sign.
        var drawn = Gen.Choose(0, int.MaxValue).Select(step => Expanded(Math.Pow(10, -10 + (40d * step / int.MaxValue))));
        var belowOne =
            from lead in Gen.Elements("0", string.Empty)
            from zeros in Gen.Choose(0, 9)
            from significant in Gen.Choose(1, 17)
            from digits in Gen.ArrayOf(Gen.Choose(0, 9), significant)
            select lead + "." + new string('0', zeros) + string.Concat(digits);
        var boundary =
            from mantissa in Gen.OneOf(Gen.Choose(-20, 20).Select(offset => (1L << 53) + offset), Gen.Choose(0, 999).Select(small => (long)small))
            from decimals in Gen.Choose(0, 25)
            select WithDecimals(mantissa.ToString(CultureInfo.InvariantCulture), decimals);
        var cases = Gen.Zip(Signs, Gen.OneOf(drawn, belowOne, boundary));

        Check.One(Settings, Prop.ForAll(cases.ToArbitrary(), written =>
        {
            var (sign, magnitude) = written;
            var nearest = double.Parse(magnitude, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

            return PdfNumberParser.TryParse(Encoding.ASCII.GetBytes(sign + magnitude), out _, out var real, out var isReal)
                && isReal
                && SameBits(real, sign.StartsWith('-') ? -nearest : nearest);
        }));
    }

    [Fact]
    public void An_object_reads_the_same_wherever_the_window_edge_falls_in_it()
    {
        // WindowEdgeTests slides a few fixed objects across the reader's 8 KB window one byte at a time.
        // Here the objects are drawn — values nested and escaped, alone or in a dictionary, an array or a
        // stream — and the edge falls anywhere after the filler that pushes them to it: the reader must
        // find what the parser finds when it sees the whole object, and report what that parse reports.
        var cases =
            from shape in ObjectsAround(PdfValues(depth: 3))
            from cut in Gen.Choose(-2, shape.After.Length + "\nendobj\n".Length + 2)
            select (shape.Before, shape.After, cut);

        Check.One(Settings, Prop.ForAll(cases.ToArbitrary(), c =>
        {
            var filler = PdfFileReader.InitialObjectWindow - "5 0 obj\n".Length - c.Before.Length - c.cut;

            return filler < 0 || WindowEdgeTests.ReadAndCompare(c.Before + new string('x', filler) + c.After) is null;
        }));
    }

    [Fact]
    public void A_property_failure_replays_from_its_seed()
    {
        // The guarantee the remarks above claim, asserted rather than assumed: the same seed draws the
        // same cases, so a counter-example found in CI is reproducible on a desk.
        static List<int> Draw(ulong seed)
        {
            var drawn = new List<int>();
            Check.One(
                Config.Quick.WithMaxTest(20).WithReplay(seed, Gamma).WithQuietOnSuccess(true),
                Prop.ForAll<int>(value =>
                {
                    drawn.Add(value);
                    return true;
                }));

            return drawn;
        }

        Draw(DefaultSeed).Should().Equal(Draw(DefaultSeed));
        Draw(DefaultSeed).Should().NotEqual(Draw(DefaultSeed + 1));
    }

    [Fact]
    public void A_reader_limit_is_refused_at_zero_or_less_and_otherwise_held_within_what_the_reader_can_hold()
    {
        // ADR 34: a guard below one lets nothing be read, and one past Array.MaxLength asks for an array no
        // runtime allocates. The count has no such ceiling. Every int, the edges FsCheck favors included.
        Check.One(Settings, Prop.ForAll(ArbMap.Default.ArbFor<int>(), value =>
        {
            if (value <= 0)
            {
                return Refused(() => PdfReaderLimits.Default with { MaxDecodedStreamLength = value })
                    && Refused(() => PdfReaderLimits.Default with { MaxObjectLength = value })
                    && Refused(() => PdfReaderLimits.Default with { MaxXRefSectionLength = value })
                    && Refused(() => PdfReaderLimits.Default with { MaxXRefSectionCount = value })
                    && Refused(() => PdfReaderLimits.Default with { MaxTrailerLength = value });
            }

            var length = Math.Min(value, Array.MaxLength);
            var limits = PdfReaderLimits.Default with
            {
                MaxDecodedStreamLength = value,
                MaxObjectLength = value,
                MaxXRefSectionLength = value,
                MaxXRefSectionCount = value,
                MaxTrailerLength = value,
            };

            return limits.MaxDecodedStreamLength == length && limits.MaxObjectLength == length
                && limits.MaxXRefSectionLength == length && limits.MaxXRefSectionCount == value
                && limits.MaxTrailerLength == length;
        }));

        static bool Refused(Func<PdfReaderLimits> setting)
        {
            try
            {
                setting();
                return false;
            }
            catch (ArgumentOutOfRangeException)
            {
                return true;
            }
        }
    }

    [Fact]
    public void The_end_of_file_rule_reports_exactly_when_the_last_1024_bytes_hold_no_marker()
    {
        // Whatever follows a sound file's own marker — white space, NULs, junk, more markers, pieces of one —
        // the rule answers what a plain search of the file's last 1,024 bytes answers. Half the cases fall
        // around the edge, where the marker leaves that window one byte at a time.
        var sound = ValidatorTests.SoundFile();
        var lengths = Gen.OneOf(Gen.Choose(0, 1100), Gen.Choose(1010, 1026));
        var trailing =
            from length in lengths
            from bytes in Gen.ArrayOf(Gen.Elements((byte)' ', (byte)'\n', (byte)0, (byte)'%', (byte)'E', (byte)'O', (byte)'F', (byte)'x'), length)
            select bytes;

        Check.One(Settings, Prop.ForAll(trailing.ToArbitrary(), bytes =>
        {
            byte[] file = [.. sound, .. bytes];
            using var document = PdfDocument.Open(file);
            var reported = new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.FileEofMissing);

            var searched = Math.Min(1024, file.Length);
            var tail = Encoding.Latin1.GetString(file, file.Length - searched, searched);
            return reported == !tail.Contains("%%EOF", StringComparison.Ordinal);
        }));
    }

    /// <summary>
    /// PDF values as their syntax: every kind of scalar, arrays and dictionaries up to <paramref name="depth"/>
    /// levels deep, with the separators real producers use. Containers hold at most five items, so an
    /// object stays well inside the window before its filler is added.
    /// </summary>
    private static Gen<string> PdfValues(int depth)
    {
        var number = Gen.Choose(-99_999, 99_999);
        var integer = number.Select(value => value.ToString(CultureInfo.InvariantCulture));
        var real = Gen.Zip(number, Gen.Choose(0, 9_999))
            .Select(pair => string.Create(CultureInfo.InvariantCulture, $"{pair.Item1}.{pair.Item2}"));
        var name = Gen.Elements("/Type", "/A", "/LongerName", "/Name#20With#23Escapes", "/x1", "/");
        var literal = Gen.ArrayOf(Gen.Elements('a', 'Z', ' ', '(', ')', '\\', '\n', '\r', '7'))
            .Select(chars => "(" + EscapeLiteral(chars) + ")");
        var hex = Gen.ArrayOf(Gen.Elements('0', '9', 'a', 'F', ' ', '\n')).Select(chars => "<" + new string(chars) + ">");
        var keyword = Gen.Elements("true", "false", "null");
        var reference = Gen.Zip(Gen.Choose(1, 99_999), Gen.Choose(0, 3)).Select(pair => $"{pair.Item1} {pair.Item2} R");
        var scalar = Gen.OneOf(integer, real, name, literal, hex, keyword, reference);

        if (depth == 0)
        {
            return scalar;
        }

        var inner = PdfValues(depth - 1);
        var separator = Gen.Elements(" ", "\n", "\r\n", "  ", "%comment\n");
        var count = Gen.Choose(0, 5);
        var array = count.SelectMany(n => Gen.ArrayOf(Gen.Zip(inner, separator), n))
            .Select(items => "[" + string.Concat(items.Select(item => item.Item1 + item.Item2)) + "]");
        var dictionary = count.SelectMany(n => Gen.ArrayOf(Gen.Zip(name.Where(key => key != "/"), inner), n))
            .Select(entries => "<<" + string.Concat(entries.Select(entry => $" {entry.Item1} {entry.Item2}")) + " >>");

        return Gen.Frequency((4, scalar), (1, array), (1, dictionary));
    }

    /// <summary>
    /// Objects built around a value, as what comes before a filler and what comes after it: the value
    /// alone after a comment, in a dictionary, in an array, or in a stream's dictionary with the stream's
    /// data and the end-of-line forms around it.
    /// </summary>
    private static Gen<(string Before, string After)> ObjectsAround(Gen<string> values)
    {
        var data = Gen.ArrayOf(Gen.Elements('a', 'q', 'z', ' ', '\n', '\r', '0')).Select(chars => new string(chars));
        var afterDictionary = Gen.Elements("\n", "\r\n", " ");
        var afterKeyword = Gen.Elements("\r\n", "\n");
        var beforeEnd = Gen.Elements("\n", "\r\n", "\r", " ", string.Empty);

        var stream =
            from value in values
            from bytes in data
            from first in afterDictionary
            from second in afterKeyword
            from third in beforeEnd
            select ("<< /Pad (", $") /V {value} /Length {bytes.Length} >>{first}stream{second}{bytes}{third}endstream");

        return Gen.OneOf(
            values.Select(value => ("%", "\n" + value)),
            values.Select(value => ("<< /Pad (", ") /V " + value + " >>")),
            values.Select(value => ("[(", ") " + value + "]")),
            stream);
    }

    private static string EscapeLiteral(char[] chars)
    {
        var text = new StringBuilder(chars.Length * 2);

        foreach (var c in chars)
        {
            if (c is '(' or ')' or '\\')
            {
                text.Append('\\');
            }

            text.Append(c);
        }

        return text.ToString();
    }

    private static bool SameBits(double actual, double expected) =>
        BitConverter.DoubleToInt64Bits(actual) == BitConverter.DoubleToInt64Bits(expected);

    /// <summary>Writes a double as its shortest round-trip decimal, without an exponent, always with a period.</summary>
    private static string Expanded(double value)
    {
        var roundTrip = value.ToString("R", CultureInfo.InvariantCulture);
        var e = roundTrip.IndexOf('E', StringComparison.Ordinal);

        if (e < 0)
        {
            return roundTrip.Contains('.', StringComparison.Ordinal) ? roundTrip : roundTrip + ".0";
        }

        var exponent = int.Parse(roundTrip.AsSpan(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var mantissa = roundTrip[..e];
        var point = mantissa.IndexOf('.', StringComparison.Ordinal);
        var digits = point < 0 ? mantissa : mantissa.Remove(point, 1);
        var whole = (point < 0 ? mantissa.Length : point) + exponent;

        return whole <= 0 ? "0." + new string('0', -whole) + digits
            : whole >= digits.Length ? digits + new string('0', whole - digits.Length) + ".0"
            : digits[..whole] + "." + digits[whole..];
    }

    /// <summary>Writes <paramref name="digits"/> with a period before the last <paramref name="decimals"/> of them, zeros added in front as needed.</summary>
    private static string WithDecimals(string digits, int decimals) =>
        decimals < digits.Length
            ? digits[..^decimals] + "." + digits[^decimals..]
            : "0." + new string('0', decimals - digits.Length) + digits;

    private static bool IsWellFormedUtf16(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]))
                {
                    return false;
                }

                index++;
                continue;
            }

            if (char.IsLowSurrogate(value[index]))
            {
                return false;
            }
        }

        return true;
    }
}
