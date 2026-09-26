using System.Text;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Validation;
using AdCodicem.Pdf.Validation.Rules;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// <c>file.eof-missing</c>: a file ends with <c>%%EOF</c>, and readers look for it in its last 1,024 bytes.
/// </summary>
public class EndOfFileMarkerRuleTests
{
    /// <summary>
    /// The most bytes that may follow a sound file's own marker line, <c>%%EOF</c> and its line feed — six
    /// bytes —, before the marker leaves the last 1,024 bytes: its five bytes must all lie inside them.
    /// </summary>
    private const int MostTrailingBytes = EndOfFileMarkerRule.SearchLength - 6;

    [Fact]
    public void A_file_that_ends_with_the_marker_is_not_reported()
    {
        Validate(ValidatorTests.SoundFile()).Contains(PdfValidationRuleIds.FileEofMissing).Should().BeFalse();
    }

    [Fact]
    public void A_file_without_the_marker_is_reported_once_as_a_warning_at_its_end()
    {
        var file = WithoutMarker(ValidatorTests.SoundFile());

        var report = Validate(file);

        var finding = report.Findings.Should().ContainSingle().Which;
        finding.RuleId.Should().Be(PdfValidationRuleIds.FileEofMissing);
        finding.Severity.Should().Be(PdfValidationSeverity.Warning);
        finding.Location.Position.Should().Be(file.Length);
        finding.Location.Object.Should().BeNull();
        finding.Message.Should().Be($"The file does not end with an end-of-file marker: no %%EOF in its last {file.Length} bytes.");
        finding.Remedy.Should().NotBeNullOrWhiteSpace();
        report.HasErrors.Should().BeFalse("a file every reader opens is not broken for lacking its marker");
    }

    [Fact]
    public void A_long_file_without_the_marker_is_searched_over_its_last_1024_bytes()
    {
        var padded = WithoutMarker(ValidatorTests.SoundFile()).Concat(Encoding.ASCII.GetBytes(new string(' ', 4000))).ToArray();

        var finding = Validate(padded).Findings.Should().ContainSingle().Which;

        finding.Message.Should().Contain("no %%EOF in its last 1024 bytes");
        finding.Location.Position.Should().Be(padded.Length);
    }

    [Fact]
    public void The_marker_needs_no_end_of_line_after_it()
    {
        var file = ValidatorTests.SoundFile();

        Validate(file.AsSpan(0, file.Length - 1).ToArray()).Findings.Should().BeEmpty();
    }

    [Fact]
    public void Each_incremental_update_ends_with_a_marker_of_its_own_and_that_is_legal()
    {
        var updated = TestPdfBuilder.AppendIncrementalUpdate(
            ValidatorTests.SoundFile(), rootNumber: 1, [(4, "(an update)")]);

        Text(updated).Split("%%EOF").Length.Should().Be(3, "the original and the update each end with a marker");
        Validate(updated).Findings.Should().BeEmpty();
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\r")]
    [InlineData("   \n\n")]
    [InlineData("\0\0\0\0\0\0\0\0")]
    [InlineData("\u001A")]
    [InlineData("junk a transfer appended")]
    public void Bytes_after_the_marker_within_the_last_1024_are_tolerated(string trailing)
    {
        var file = ValidatorTests.SoundFile().Concat(Encoding.Latin1.GetBytes(trailing)).ToArray();

        Validate(file).Findings.Should().BeEmpty();
    }

    [Theory]
    [InlineData(MostTrailingBytes, false)]
    [InlineData(MostTrailingBytes + 1, true)]
    [InlineData(MostTrailingBytes + 5, true)]
    [InlineData(MostTrailingBytes + 6, true)]
    public void The_marker_counts_only_when_all_of_it_lies_in_the_last_1024_bytes(int trailingBytes, bool reported)
    {
        // Past the boundary the search window starts inside the marker, and "%EOF", "EOF" or "F" is not one.
        var file = ValidatorTests.SoundFile().Concat(new byte[trailingBytes].Select(_ => (byte)' ')).ToArray();

        Validate(file).Contains(PdfValidationRuleIds.FileEofMissing).Should().Be(reported);
    }

    [Fact]
    public void Checking_reads_the_last_1024_bytes_and_nothing_else()
    {
        var file = ValidatorTests.SoundFile().Concat(new byte[5000].Select(_ => (byte)'\n')).ToArray();
        var source = new StrictCountingSource(file);
        using var document = PdfDocument.Open(source, options: null, ownsSource: false);
        var before = source.BytesRead;

        new PdfValidator().Validate(document);

        (source.BytesRead - before).Should().Be(EndOfFileMarkerRule.SearchLength);
    }

    [Fact]
    public void A_file_the_reader_rebuilt_is_still_checked()
    {
        // A file cut before its trailer loses its marker too: the reader rebuilds its index and opens it, and
        // the validator says what the reader could only work around.
        var file = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .BuildClassic(rootNumber: 1);
        var cut = file.AsSpan(0, Text(file).IndexOf("xref", StringComparison.Ordinal)).ToArray();
        using var document = PdfDocument.Open(cut);

        document.WasRepaired.Should().BeTrue();
        new PdfValidator().Validate(document).Contains(PdfValidationRuleIds.FileEofMissing).Should().BeTrue();
    }

    private static PdfValidationReport Validate(byte[] file)
    {
        using var document = PdfDocument.Open(file);
        return new PdfValidator().Validate(document);
    }

    /// <summary>The same file with its final <c>%%EOF</c> line taken off, and nothing else changed.</summary>
    private static byte[] WithoutMarker(byte[] file)
    {
        Text(file).EndsWith("%%EOF\n", StringComparison.Ordinal).Should().BeTrue();
        return file.AsSpan(0, file.Length - "%%EOF\n".Length).ToArray();
    }

    private static string Text(byte[] data) => Encoding.Latin1.GetString(data);
}
