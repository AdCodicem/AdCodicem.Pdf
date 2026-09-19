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
