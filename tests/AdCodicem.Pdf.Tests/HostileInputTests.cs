using System.Diagnostics;
using System.Globalization;
using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// A PDF is an untrusted input. These tests assert the only two acceptable outcomes for a hostile one:
/// a usable result, or a typed failure — never a hang, a crash, or an allocation the file chose.
/// </summary>
public class HostileInputTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    [Fact]
    public void Refuses_an_input_that_contains_no_objects()
    {
        var noise = Encoding.ASCII.GetBytes("%PDF-1.7\n" + new string('x', 5000));

        FluentThrow<PdfFormatException>(() => PdfDocument.Open(noise));
    }

    [Fact]
    public void Ignores_a_stream_length_larger_than_the_file()
    {
        var bytes = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .WithObject(3, "<< /Type /Page /Parent 2 0 R /Contents 4 0 R >>")
            .WithObject(4, "<< /Length 2147483647 >>\nstream\nshort\nendstream")
            .BuildClassic(rootNumber: 1);

        using var document = Measure(() => PdfDocument.Open(bytes));

        var stream = document.GetObject(new PdfObjectId(4)).AsStream().Required();
        stream.GetRawBytes().Length.Should().BeLessThan(bytes.Length);
    }

    [Fact]
    public void Ignores_an_object_stream_that_claims_more_objects_than_it_could_hold()
    {
        var bytes = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(5, "<< /Type /ObjStm /N 1000000000 /First 4 /Length 8 >>\nstream\n1 0 <<>>\nendstream")
            .BuildClassic(rootNumber: 1);

        using var document = Measure(() => PdfDocument.Open(bytes));

        document.Catalog.Required();
    }

    [Fact]
    public void Survives_a_cross_reference_stream_with_impossible_field_widths()
    {
        var bytes = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, "<< /Type /XRef /Size 3 /W [99 99 99] /Root 1 0 R /Length 4 >>\nstream\nAAAA\nendstream")
            .BuildClassic(rootNumber: 1);

        using var document = Measure(() => PdfDocument.Open(bytes));

        document.Catalog.Required();
    }

    [Fact]
    public void Survives_an_object_whose_length_refers_to_itself()
    {
        var bytes = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(3, "<< /Length 3 0 R >>\nstream\nloop\nendstream")
            .BuildClassic(rootNumber: 1);

        using var document = Measure(() => PdfDocument.Open(bytes));

        document.GetObject(new PdfObjectId(3)).Required();
    }

    [Fact]
    public void Survives_a_document_whose_pages_form_a_cycle()
    {
        var bytes = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [2 0 R] /Count 1 >>")
            .BuildClassic(rootNumber: 1);

        using var document = Measure(() => PdfDocument.Open(bytes));

        document.Catalog.GetDictionary(PdfName.Pages).Required();
    }

    [Fact]
    public void Survives_a_file_made_only_of_object_headers()
    {
        var text = new StringBuilder("%PDF-1.7\n");
        for (var i = 1; i <= 2000; i++)
        {
            text.Append(i).Append(" 0 obj\n");
        }

        var bytes = Encoding.ASCII.GetBytes(text.ToString());

        // Objects exist but nothing is well formed: opening must fail cleanly or return an empty document.
        var document = Measure(() =>
        {
            try
            {
                return PdfDocument.Open(bytes);
            }
            catch (PdfFormatException)
            {
                return null;
            }
        });

        document?.Dispose();
    }

    [Fact]
    public void Survives_deeply_nested_containers_inside_an_object()
    {
        var deep = new string('[', 20000) + new string(']', 20000);
        var bytes = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, $"<< /Type /Pages /Kids [] /Count 0 /Deep {deep} >>")
            .BuildClassic(rootNumber: 1);

        using var document = Measure(() => PdfDocument.Open(bytes));

        document.GetObject(new PdfObjectId(2)).Required();
        document.Diagnostics.Contains(PdfDiagnosticCodes.SyntaxDepthExceeded).Should().BeTrue();
    }

    [Fact]
    public void Survives_a_chain_of_lengths_longer_than_the_stack_is_deep()
    {
        // Each stream takes its /Length from the next, so reading the first loads the second while it is
        // being parsed, which loads the third: 50,000 levels took the process down with the stack.
        const int Chain = 50_000;
        var text = new StringBuilder("%PDF-1.7\n");
        var offsets = new long[Chain + 3];

        offsets[1] = text.Length;
        text.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        offsets[2] = text.Length;
        text.Append("2 0 obj\n<< /Type /Pages /Kids [] /Count 0 >>\nendobj\n");

        for (var number = 3; number < Chain + 3; number++)
        {
            offsets[number] = text.Length;
            var length = number + 1 < Chain + 3 ? $"{number + 1} 0 R" : "1";
            text.Append(CultureInfo.InvariantCulture, $"{number} 0 obj\n<< /Length {length} >>\nstream\nx\nendstream\nendobj\n");
        }

        var xref = text.Length;
        text.Append(CultureInfo.InvariantCulture, $"xref\n0 {Chain + 3}\n0000000000 65535 f\r\n");
        for (var number = 1; number < Chain + 3; number++)
        {
            text.Append(CultureInfo.InvariantCulture, $"{offsets[number]:D10} 00000 n\r\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {Chain + 3} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");

        using var document = PdfDocument.Open(Encoding.ASCII.GetBytes(text.ToString()));
        var first = Measure(() => document.GetObject(new PdfObjectId(3)));

        first.AsStream().Required().GetRawBytes().Length.Should().Be(1);
        document.Diagnostics.Where(d => d.Code == PdfDiagnosticCodes.SyntaxDepthExceeded).Should().ContainSingle();
    }

    private static T Measure<T>(Func<T> action)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = action();
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(Budget, "A hostile input must not be allowed to take unbounded time.");
        return result;
    }
}
