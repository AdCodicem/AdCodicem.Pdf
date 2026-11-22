using System.Globalization;
using AdCodicem.Pdf.IO;
using FsCheck;
using FsCheck.Fluent;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// How a message says how many, and how far (#241): a count agrees with its noun, a count of one included, its digits never
/// grouped (#326), and a distance says which way it runs rather than carrying a sign. Each message that writes a count is held
/// to it with a count of one beside its other tests.
/// </summary>
public class CountTextTests
{
    [Theory]
    [InlineData(0, "0 bytes")]
    [InlineData(1, "1 byte")]
    [InlineData(2, "2 bytes")]
    [InlineData(-1, "-1 bytes")]
    [InlineData(12345, "12345 bytes")]
    public void A_count_takes_the_singular_for_one_alone(long count, string said) =>
        CountText.Bytes(count).Should().Be(said);

    [Fact]
    public void Each_noun_agrees_with_its_count()
    {
        CountText.Objects(1).Should().Be("1 object");
        CountText.Objects(3).Should().Be("3 objects");
        CountText.Pages(1).Should().Be("1 page");
        CountText.Pages(2).Should().Be("2 pages");
        CountText.Of(1, "value", "values").Should().Be("1 value");
        CountText.Of(0, "value", "values").Should().Be("0 values");
    }

    [Theory]
    [InlineData(1, "1 byte", "1 byte")]
    [InlineData(1000, "1000 bytes", "1000 bytes")]
    [InlineData(-1000, "-1000 bytes", "-1000 bytes")]
    [InlineData(1_024_000, "1024000 bytes", "1000 KB")]
    [InlineData(1_048_576_000, "1048576000 bytes", "1000 MB")]
    public void A_count_s_digits_are_never_grouped_and_a_bound_of_whole_kilobytes_or_megabytes_keeps_its_unit(
        int count, string said, string bound)
    {
        CountText.Bytes(count).Should().Be(said);
        PdfLimitGuard.FormatLength(count).Should().Be(bound);
    }

    [Theory]
    [InlineData(-1, "1 byte before it")]
    [InlineData(-12, "12 bytes before it")]
    [InlineData(1, "1 byte after it")]
    [InlineData(3, "3 bytes after it")]
    [InlineData(0, "0 bytes after it")]
    public void A_distance_says_which_way_it_runs(long delta, string said) =>
        CountText.Distance(delta, "it").Should().Be(said);

    [Theory]
    [InlineData("sv-SE")]
    [InlineData("fa-IR")]
    public void A_count_and_a_distance_are_written_the_same_whatever_the_culture(string name)
    {
        // Swedish groups digits with a no-break space and writes U+2212 for a minus sign; Persian writes its own digits'
        // separators and marks a minus sign with U+200E.
        var culture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);

            PdfLimitGuard.FormatLength(1_000_000).Should().Be("1000000 bytes");
            CountText.Of(-5, "value", "values").Should().Be("-5 values");
            CountText.Distance(-3, "where the index said").Should().Be("3 bytes before where the index said");
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void A_distance_names_how_far_and_which_way_and_never_carries_a_sign()
    {
        // Every distance a long holds but the most negative, which no file is long enough to give.
        var deltas = ArbMap.Default.ArbFor<long>().Filter(delta => delta != long.MinValue);

        Check.One(Settings, Prop.ForAll(deltas, delta =>
        {
            var said = CountText.Distance(delta, "it");
            var count = Math.Abs(delta);
            var way = delta < 0 ? "before" : "after";

            said.Should().Be(string.Create(
                CultureInfo.InvariantCulture, $"{count} {(count == 1 ? "byte" : "bytes")} {way} it"));
            said.Should().NotContain("-");
        }));
    }

    private static Config Settings =>
        Config.QuickThrowOnFailure.WithMaxTest(200).WithReplay(0x5EED_1729UL, 0x9E37_79B9_7F4A_7C15UL).WithQuietOnSuccess(true);
}
