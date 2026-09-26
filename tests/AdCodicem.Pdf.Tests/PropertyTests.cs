using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.IO;
using AdCodicem.Pdf.Objects;
using FsCheck;
using FsCheck.Fluent;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// Statements that must hold for <em>every</em> input, checked against generated data rather than against
/// a table of examples someone thought of.
/// </summary>
/// <remarks>
/// A different instrument from <see cref="FuzzingTests"/>, not a duplicate of it. Mutation fuzzing starts
/// from real documents and damages them, so it explores the neighbourhood of files that exist. A
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
    public void A_text_string_survives_being_written_and_read_back()
    {
        // An unpaired surrogate is not text — no encoding round-trips one — so it sits outside this
        // property's domain rather than counting as a defect in it.
        var texts = ArbMap.Default.ArbFor<string>()
            .Filter(value => value is not null && IsWellFormedUtf16(value));

        Check.One(Settings, Prop.ForAll(texts, value => PdfString.FromText(value).ToText() == value));
    }

    [Fact]
    public void The_hand_written_integer_parser_agrees_with_the_framework()
    {
        // The range is the one PDF actually admits for an integer, which is also the range the hot-path
        // parser is allowed to accumulate without falling back to floating point.
        Check.One(Settings, Prop.ForAll<int>(value =>
        {
            var text = value.ToString(CultureInfo.InvariantCulture);

            return PdfNumberParser.TryParse(Encoding.ASCII.GetBytes(text), out var integer, out _, out var isReal)
                && !isReal
                && integer == value;
        }));
    }

    [Fact]
    public void The_hand_written_real_parser_agrees_with_the_framework()
    {
        // The domain is what PDF can write: a plain decimal, never exponent notation. Parsing is
        // hand-written because it must accept forms the framework rejects and must not allocate; this
        // pins it against the framework everywhere both are willing to answer.
        Check.One(Settings, Prop.ForAll<int, ushort>((units, fraction) =>
        {
            var text = string.Create(CultureInfo.InvariantCulture, $"{units}.{fraction % 10000:0000}");
            var expected = double.Parse(text, CultureInfo.InvariantCulture);

            return PdfNumberParser.TryParse(Encoding.ASCII.GetBytes(text), out _, out var real, out var isReal)
                && isReal
                && Math.Abs(real - expected) <= 1e-9 * Math.Max(1, Math.Abs(expected));
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
