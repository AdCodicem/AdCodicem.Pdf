using System.Diagnostics;
using AdCodicem.Pdf.Diagnostics;
using AdCodicem.Pdf.Documents;
using AdCodicem.Pdf.Objects;

namespace AdCodicem.Pdf.Tests;

/// <summary>
/// What a hostile input costs in time, measured as how it grows with the input rather than against a budget: run
/// alone, in <see cref="TimingCollection"/>.
/// </summary>
[Collection(TimingCollection.Name)]
public class ReaderTimingTests
{
    [Fact]
    public void An_object_stream_whose_every_index_is_wrong_is_read_in_linear_time()
    {
        // One stream whose every entry in the index names the wrong place in it: each object is found by its
        // number, through a lookup built once, not by a search of the stream's header for each. Four times as many
        // objects then take about four times as long, where a search would take sixteen. The measure is that
        // ratio, the best of five runs of each size, so that it holds on a slow machine and under coverage
        // instrumentation alike, where a fixed time budget did not.
        const int Small = 50_000;
        const int Large = 4 * Small;
        var small = HostileInputTests.ObjectStreams(streams: 1, decodedLength: 0, objectsPerStream: Small, everyIndexWrong: true);
        var large = HostileInputTests.ObjectStreams(streams: 1, decodedLength: 0, objectsPerStream: Large, everyIndexWrong: true);

        ReadEvery(small, Small);
        var smallTime = TimeSpan.MaxValue;
        var largeTime = TimeSpan.MaxValue;

        for (var run = 0; run < 5; run++)
        {
            smallTime = Min(smallTime, Time(() => ReadEvery(small, Small)));
            largeTime = Min(largeTime, Time(() => ReadEvery(large, Large)));
        }

        largeTime.Should().BeLessThan(
            smallTime * 8,
            "four times the objects must cost about four times the time ({0} for {1:N0}, {2} for {3:N0})",
            smallTime,
            Small,
            largeTime,
            Large);

        static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

        static TimeSpan Time(Action action)
        {
            var stopwatch = Stopwatch.StartNew();
            action();
            return stopwatch.Elapsed;
        }

        static void ReadEvery(byte[] file, int count)
        {
            using var document = PdfDocument.Open(file);
            var dictionaries = 0;

            for (var i = 0; i < count; i++)
            {
                dictionaries += document.GetObject(new PdfObjectId(HostileInputTests.PackedNumber(0, i, count))) is PdfDictionary ? 1 : 0;
            }

            dictionaries.Should().Be(count);
            document.Diagnostics.Contains(PdfDiagnosticCodes.XRefOffsetAdjusted).Should().BeTrue();
        }
    }
}
