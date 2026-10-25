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
    public void A_guard_reached_is_counted_though_the_capacity_drops_its_report_and_follows_it_when_moved_or_rolled_back()
    {
        // The reader tells a guard's cut from the file's damage by the guard's report: once the capacity was reached, the
        // report was dropped and the cut taken for damage.
        var pending = new PdfDiagnostics { Capacity = 0 };
        var target = new PdfDiagnostics { Capacity = 0 };
        var mark = pending.GetMark();

        pending.ReachLimit(PdfDiagnosticCodes.LimitDecodedStream, "Cut.", 7);
        pending.Warn("other", "No guard.");

        pending.Count.Should().Be(0);
        pending.SuppressedCount.Should().Be(2);
        pending.LimitsReached.Should().Be(1);

        var kept = pending.GetMark();
        pending.ReachLimit(PdfDiagnosticCodes.LimitObject, "Dropped with its parse.", 9);
        pending.RollBack(kept);
        pending.LimitsReached.Should().Be(1);

        pending.MoveTo(target, mark, 0, static (_, _) => false);
        pending.LimitsReached.Should().Be(0);
        target.LimitsReached.Should().Be(1);
        target.SuppressedCount.Should().Be(2);
    }

    [Fact]
    public void A_decoding_moves_in_only_what_no_decoding_moved_in_before_two_alike_of_its_own_included()
    {
        // The whole report is the key: another message under the same code and position is another report, and two filters
        // of one decoding that report alike are each held (#144).
        var target = new PdfDiagnostics();
        var decodings = target.Decodings;

        var first = decodings.GetMark();
        decodings.Warn("filter.failed", "one", 5);
        decodings.Repair("filter.failed", "two", 5);
        decodings.Repair("filter.failed", "two", 5);
        decodings.MoveDecodingReportsTo(target, first);

        var second = decodings.GetMark();
        decodings.Warn("filter.failed", "one", 5);
        decodings.Warn("filter.failed", "three", 5);
        decodings.Repair("filter.failed", "two", 5);
        decodings.MoveDecodingReportsTo(target, second);

        target.Select(d => $"{d.Severity} {d.Message}").Should().Equal("Warning one", "Repair two", "Repair two", "Warning three");
        decodings.Should().BeEmpty();
    }

    [Fact]
    public void A_report_a_decoding_moved_in_before_takes_no_room_once_the_diagnostics_are_full_and_a_guard_stays_counted()
    {
        var target = new PdfDiagnostics { Capacity = 1 };
        var decodings = target.Decodings;

        var first = decodings.GetMark();
        decodings.ReachLimit("limit.decoded-stream", "bound", 5);
        decodings.MoveDecodingReportsTo(target, first);

        var second = decodings.GetMark();
        decodings.ReachLimit("limit.decoded-stream", "bound", 5);
        decodings.MoveDecodingReportsTo(target, second);

        target.Select(d => d.Message).Should().Equal("bound");
        target.SuppressedCount.Should().Be(0);
        target.LimitsReached.Should().Be(2);

        var third = decodings.GetMark();
        decodings.Warn("filter.failed", "new", 5);
        decodings.MoveDecodingReportsTo(target, third);

        target.SuppressedCount.Should().Be(1);
    }

    [Fact]
    public void A_copy_of_a_report_the_capacity_left_out_is_counted_again_each_time_a_decoding_meets_it()
    {
        // The target holds as many copies of a report as it had room for: a copy past them is new to it, whichever decoding
        // meets it, and counted again among those it leaves out, as a repeat of any report it had no room for is.
        var target = new PdfDiagnostics { Capacity = 2 };
        var decodings = target.Decodings;

        for (var decoding = 0; decoding < 2; decoding++)
        {
            var mark = decodings.GetMark();
            decodings.Repair("filter.failed", "alike", 5);
            decodings.Repair("filter.failed", "alike", 5);
            decodings.Repair("filter.failed", "alike", 5);
            decodings.MoveDecodingReportsTo(target, mark);
        }

        target.Select(d => d.Message).Should().Equal("alike", "alike");
        target.SuppressedCount.Should().Be(2);

        var last = decodings.GetMark();
        decodings.Warn("filter.failed", "other", 9);
        decodings.MoveDecodingReportsTo(target, last);

        target.SuppressedCount.Should().Be(3);
    }

    [Fact]
    public void Rolling_back_beside_the_guards_keeps_their_reports_and_counts_those_the_capacity_left_out()
    {
        // A reading met two guards beside faults it is to make again, past a capacity of three: the guards stand, the one
        // the capacity left no room for counted as left out still, and the faults are dropped, the one the capacity left
        // out no longer counted. A mark that records no decoding reports, which the reader never rolls back to beside its
        // guards, leaves which reports decodings moved in as it stands: a fault a decoding moved in before the mark is not
        // moved in again.
        var target = new PdfDiagnostics { Capacity = 3 };
        var decodings = target.Decodings;
        var before = decodings.GetMark();
        decodings.Warn(PdfDiagnosticCodes.FilterFailed, "Moved in before the mark.", 3);
        decodings.MoveDecodingReportsTo(target, before);

        var mark = target.GetMark();
        target.Warn("dropped", "Met beside the guards.");
        target.ReachLimit(PdfDiagnosticCodes.LimitDecodedStream, "Kept.", 7);
        target.Warn("left.out", "Past the capacity.");
        target.ReachLimit(PdfDiagnosticCodes.LimitObject, "Past the capacity.", 9);
        target.RollBackBesideLimits(mark);

        target.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.FilterFailed, PdfDiagnosticCodes.LimitDecodedStream);
        target.LimitsReached.Should().Be(2);
        target.SuppressedCount.Should().Be(1);

        var again = decodings.GetMark();
        decodings.Warn(PdfDiagnosticCodes.FilterFailed, "Moved in before the mark.", 3);
        decodings.MoveDecodingReportsTo(target, again);

        target.Select(d => d.Code).Should().Equal(PdfDiagnosticCodes.FilterFailed, PdfDiagnosticCodes.LimitDecodedStream);
        target.SuppressedCount.Should().Be(1);
    }

    [Fact]
    public void Rolling_back_beside_the_guards_holds_again_as_moved_in_only_the_guard_reports_a_decoding_moved_in()
    {
        // A guard reported outside any decoding before any decoding has reported — which the reader never rolls back beside
        // to such a mark, a decoding having reported by then —, then a guard a decoding reached beside a fault, are each
        // rolled back beside to a mark that records which reports decodings moved in. Every guard's report stands. The
        // decoding's is held again as moved in, so that a decoding that meets it again does not report it twice; the fault
        // dropped with the rollback is forgotten, so that a decoding that meets it again moves it in again (#144, #220).
        var target = new PdfDiagnostics();
        var decodings = target.Decodings;

        var reading = target.GetMarkWithDecodingReports();
        target.ReachLimit(PdfDiagnosticCodes.LimitObject, "The reader's guard.", 9);
        target.Warn("dropped", "Met beside the reader's guard.");
        target.RollBackBesideLimits(reading);

        target.Select(d => d.Message).Should().Equal("The reader's guard.");

        var decoded = target.GetMarkWithDecodingReports();
        var first = decodings.GetMark();
        decodings.ReachLimit(PdfDiagnosticCodes.LimitDecodedStream, "The decoding's guard.", 7);
        decodings.Warn(PdfDiagnosticCodes.FilterFailed, "Met beside the decoding's guard.", 7);
        decodings.MoveDecodingReportsTo(target, first);
        target.RollBackBesideLimits(decoded);

        target.Select(d => d.Message).Should().Equal("The reader's guard.", "The decoding's guard.");

        var again = decodings.GetMark();
        decodings.ReachLimit(PdfDiagnosticCodes.LimitDecodedStream, "The decoding's guard.", 7);
        decodings.Warn(PdfDiagnosticCodes.FilterFailed, "Met beside the decoding's guard.", 7);
        decodings.MoveDecodingReportsTo(target, again);

        target.Select(d => d.Message).Should().Equal("The reader's guard.", "The decoding's guard.", "Met beside the decoding's guard.");
        target.LimitsReached.Should().Be(3);
        target.SuppressedCount.Should().Be(0);
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
    public void Says_which_severities_it_holds()
    {
        var diagnostics = new PdfDiagnostics();
        diagnostics.HasWarnings.Should().BeFalse();
        diagnostics.HasRepairs.Should().BeFalse();
        diagnostics.HasConformanceLoss.Should().BeFalse();

        diagnostics.Warn("a.warning", "Worrying.");
        diagnostics.Add(PdfDiagnosticSeverity.ConformanceLoss, "a.loss", "Lost.");

        diagnostics.HasWarnings.Should().BeTrue();
        diagnostics.HasConformanceLoss.Should().BeTrue();
        diagnostics.HasRepairs.Should().BeFalse();
    }

    [Fact]
    public void Enumerates_its_entries_through_every_interface_it_implements()
    {
        var diagnostics = new PdfDiagnostics();
        diagnostics.Warn("first", "One.");
        diagnostics.Repair("second", "Two.");

        ((IEnumerable<PdfDiagnostic>)diagnostics).Select(d => d.Code).Should().Equal("first", "second");
        ((System.Collections.IEnumerable)diagnostics).Cast<PdfDiagnostic>().Select(d => d.Code).Should().Equal("first", "second");
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
