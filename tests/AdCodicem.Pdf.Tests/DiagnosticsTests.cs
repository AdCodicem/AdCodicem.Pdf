using System.Text;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// What the reader notices while parsing through a window is held back until that window is known to have
/// been large enough; these are the rules of that holding back.
/// </summary>
public class DiagnosticsTests
{
    [Fact]
    public void Rolling_back_drops_what_was_recorded_since_the_mark_suppressed_entries_included()
    {
        var diagnostics = new PdfDiagnostics { Capacity = 2 };
        diagnostics.Warn("kept", "Recorded before the mark.");
        var mark = diagnostics.GetMark();

        diagnostics.Warn("dropped", "Recorded after the mark.");
        diagnostics.Warn("suppressed", "Past the capacity.");
        diagnostics.RollBack(mark);

        diagnostics.Select(d => d.Code).Should().Equal("kept");
        diagnostics.SuppressedCount.Should().Be(0);
    }

    [Fact]
    public void Moving_hands_over_entries_and_suppressed_counts_under_the_target_s_capacity()
    {
        var pending = new PdfDiagnostics { Capacity = 3 };
        var target = new PdfDiagnostics { Capacity = 2 };
        target.Warn("earlier", "Already in the report.");
        var mark = pending.GetMark();

        pending.Warn("first", "Moved.");
        pending.Repair("second", "Past the target's capacity.");
        pending.Warn("third", "Past the target's capacity.");
        pending.Warn("fourth", "Past the pending capacity.");
        pending.MoveTo(target, mark);

        target.Select(d => d.Code).Should().Equal("earlier", "first");
        target.SuppressedCount.Should().Be(3);
        pending.Count.Should().Be(0);
        pending.SuppressedCount.Should().Be(0);
    }

    [Fact]
    public void A_nested_mark_keeps_or_drops_only_what_came_after_it()
    {
        // A nested load parses while an outer parse holds entries of its own: it moves or drops its own,
        // and leaves the outer ones to their fate.
        var pending = new PdfDiagnostics();
        var report = new PdfDiagnostics();

        var outer = pending.GetMark();
        pending.Warn("outer", "The outer parse noticed this.");

        var nested = pending.GetMark();
        pending.Warn("nested", "The nested parse noticed this.");
        pending.MoveTo(report, nested);

        var dropped = pending.GetMark();
        pending.Warn("discarded", "A nested attempt that was not kept.");
        pending.RollBack(dropped);

        pending.RollBack(outer);

        report.Select(d => d.Code).Should().Equal("nested");
        pending.Count.Should().Be(0);
    }

    [Theory]
    [InlineData(5000, 1500, 0)]
    [InlineData(100, 100, 1400)]
    public void The_reader_holds_back_as_many_diagnostics_as_the_document_keeps(int capacity, int kept, int suppressed)
    {
        // An object of 1,500 keys that are not names, longer than the first window, so the first attempt
        // is dropped: what the kept one reports is bounded by the document's capacity, not by the buffer's.
        var body = new StringBuilder("<<");
        for (var i = 0; i < 1500; i++)
        {
            body.Append(" 1");
        }

        body.Append(" /Pad (").Append('x', 9 * 1024).Append(") >>");
        var bytes = new TestPdfBuilder()
            .WithObject(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .WithObject(2, "<< /Type /Pages /Kids [] /Count 0 >>")
            .WithObject(5, body.ToString())
            .BuildClassic(rootNumber: 1);

        using var document = PdfDocument.Open(bytes, new PdfReaderOptions { DiagnosticCapacity = capacity });
        document.GetObject(new PdfObjectId(5)).AsDictionary().Required();

        document.Diagnostics.Count.Should().Be(kept);
        document.Diagnostics.SuppressedCount.Should().Be(suppressed);
    }

    [Fact]
    public void Rolling_back_to_the_current_position_changes_nothing()
    {
        var diagnostics = new PdfDiagnostics();
        diagnostics.Warn("kept", "Recorded.");

        diagnostics.RollBack(diagnostics.GetMark());

        diagnostics.Select(d => d.Code).Should().Equal("kept");
    }
}
