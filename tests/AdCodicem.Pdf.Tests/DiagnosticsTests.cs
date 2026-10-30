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
    public void An_enumeration_gives_the_entries_recorded_when_it_began_and_the_next_one_those_recorded_since()
    {
        // A read the enumeration goes on through records into the document's diagnostics (#167): what it records is counted
        // at once, and given by the next enumeration, through every interface, and the enumeration never throws for it.
        var diagnostics = new PdfDiagnostics();
        diagnostics.Warn("first", "One.");
        diagnostics.Repair("second", "Two.");
        var enumerated = new List<string>();

        foreach (var entry in diagnostics)
        {
            enumerated.Add(entry.Code);
            diagnostics.Warn("during", "Recorded while it is enumerated.");
        }

        var generic = ((IEnumerable<PdfDiagnostic>)diagnostics).GetEnumerator();
        var nonGeneric = ((System.Collections.IEnumerable)diagnostics).GetEnumerator();
        diagnostics.Warn("after", "Recorded once both began.");

        enumerated.Should().Equal("first", "second");
        diagnostics.Count.Should().Be(5);
        diagnostics.Select(d => d.Code).Should().Equal("first", "second", "during", "during", "after");
        Drain(generic).Should().Equal("first", "second", "during", "during");
        Drain(nonGeneric).Should().Equal("first", "second", "during", "during");

        static List<string> Drain(System.Collections.IEnumerator enumerator)
        {
            var codes = new List<string>();

            while (enumerator.MoveNext())
            {
                codes.Add(((PdfDiagnostic)enumerator.Current).Code);
            }

            return codes;
        }
    }

    [Fact]
    public void An_enumeration_reset_starts_again_over_the_entries_it_began_with()
    {
        var diagnostics = new PdfDiagnostics();
        diagnostics.Warn("first", "One.");
        using var enumerator = diagnostics.GetEnumerator();

        enumerator.MoveNext().Should().BeTrue();
        diagnostics.Warn("second", "Recorded once it began.");
        enumerator.MoveNext().Should().BeFalse();
        enumerator.Reset();

        enumerator.MoveNext().Should().BeTrue();
        enumerator.Current.Code.Should().Be("first");
        enumerator.MoveNext().Should().BeFalse();
        enumerator.MoveNext().Should().BeFalse();
    }

    [Fact]
    public void An_enumeration_ends_with_the_entries_a_roll_back_left_when_fewer_than_it_began_with()
    {
        // Within one call, a reading may withdraw what it recorded (a rebuild finished again from its scan does), which only
        // a caller's own source, enumerating from inside a read, can see: the enumeration ends with the entries left, and
        // reads none past them.
        var diagnostics = new PdfDiagnostics();
        diagnostics.Warn("kept", "Recorded before the mark.");
        var mark = diagnostics.GetMark();
        diagnostics.Warn("withdrawn", "Recorded after the mark.");
        diagnostics.Warn("withdrawn too", "Recorded after the mark.");
        var enumerator = diagnostics.GetEnumerator();

        enumerator.MoveNext().Should().BeTrue();
        diagnostics.RollBack(mark);

        enumerator.Current.Code.Should().Be("kept");
        enumerator.MoveNext().Should().BeFalse();
        diagnostics.Warn("recorded again", "Recorded after the roll back.");
        enumerator.MoveNext().Should().BeFalse();
    }

    [Fact]
    public void Reports_of_one_text_placed_a_table_s_size_apart_spread_over_its_buckets()
    {
        // A record struct's hash adds the position last: reports alike but for where they lie, a multiple of the table's
        // size apart, would fall in one bucket, or two where the sum wraps. Hashed through the process's seed, they spread
        // as random keys do: about 650 of the 1,103 buckets.
        var size = new HashSet<PdfDiagnostic>(1_000).EnsureCapacity(0);
        var buckets = new HashSet<uint>();

        for (var k = 1; k <= 1_000; k++)
        {
            var diagnostic = new PdfDiagnostic(PdfDiagnosticSeverity.Warning, "syntax.unexpected", "Alike.", (long)k * size);
            buckets.Add((uint)diagnostic.GetHashCode() % (uint)size);
        }

        buckets.Count.Should().BeGreaterThan(500, "{0} reports in {1} buckets spread as random keys do", 1_000, size);
    }

    [Fact]
    public void Reports_whose_positions_fold_alike_do_not_share_a_hash()
    {
        // A long hashes as its halves exclusive-ored: a record struct's own hash would give reports at 0 and at 2^32 + 1 one
        // hash. Their positions' halves are mixed in apart.
        var near = new PdfDiagnostic(PdfDiagnosticSeverity.Warning, "syntax.unexpected", "Alike.", 0);
        var far = near with { Position = (1L << 32) + 1 };

        far.Should().NotBe(near);
        far.GetHashCode().Should().NotBe(near.GetHashCode());
        (far with { }).GetHashCode().Should().Be(far.GetHashCode());
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
