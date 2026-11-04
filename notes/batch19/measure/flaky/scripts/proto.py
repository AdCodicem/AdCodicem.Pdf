"""Applies one prototype of #317 to ReaderTimingTests.cs in the worktree.

usage: python3 -I proto.py <worktree> <A|B|Bnarrow|C|D|DC>
  A        bound 12, five turns as now
  Bnarrow  allocation held as well (one probe of each, below 6 times), time unchanged (five turns, bound 8)
  B        allocation held (below 6 times), time through BestOfEach(collect: true) below 12 times, as the page tree test
  C        BestOfEach(collect: true), bound 8
  D        the probe given the room a validation gives it (1,000), five turns, bound 8
  DC       room 1,000 and BestOfEach(collect: true), bound 8
"""
import sys

path = sys.argv[1] + "/tests/AdCodicem.Pdf.Tests/ReaderTimingTests.cs"
which = sys.argv[2]
s = open(path).read()
start = s.index("public void The_validator_judges_an_object_stream_s_members_in_linear_time")
end = s.index("[Theory]", start)
body = s[start:end]

loop = """        var smallTime = TimeSpan.MaxValue;
        var largeTime = TimeSpan.MaxValue;

        for (var run = 0; run < 5; run++)
        {
            smallTime = Min(smallTime, Time(() => Probe(small)));
            largeTime = Min(largeTime, Time(() => Probe(large)));
        }

        largeTime.Should().BeLessThan(
            smallTime * 8,"""
assert body.count(loop) == 1
helpers = """        static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

        static TimeSpan Time(Action action)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var stopwatch = Stopwatch.StartNew();
            action();
            return stopwatch.Elapsed;
        }
"""
assert body.count(helpers) == 1
probe = """        static void Probe(PdfDocument document)
        {
            var probe = CrossReferenceProbe.Run(document);"""
assert body.count(probe) == 1

allocated = """
        static long Allocated(Action action)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            action();
            return GC.GetAllocatedBytesForCurrentThread() - allocated;
        }
"""

if which == "A":
    body = body.replace(loop, loop.replace("smallTime * 8,", "smallTime * 12,"))
elif which == "Bnarrow":
    body = body.replace(loop, """        var smallAllocated = Allocated(() => Probe(small));
        var largeAllocated = Allocated(() => Probe(large));

        largeAllocated.Should().BeLessThan(
            smallAllocated * 6,
            "four times the members must allocate about four times as much ({0:N0} bytes for {1:N0}, {2:N0} for {3:N0})",
            smallAllocated,
            Small,
            largeAllocated,
            Large);

""" + loop)
    body = body.replace(helpers, helpers + allocated)
elif which == "B":
    body = body.replace(loop, """        var smallAllocated = Allocated(() => Probe(small));
        var largeAllocated = Allocated(() => Probe(large));

        largeAllocated.Should().BeLessThan(
            smallAllocated * 6,
            "four times the members must allocate about four times as much ({0:N0} bytes for {1:N0}, {2:N0} for {3:N0})",
            smallAllocated,
            Small,
            largeAllocated,
            Large);

        var (smallTime, largeTime) = BestOfEach(() => Probe(small), () => Probe(large), collect: true);

        largeTime.Should().BeLessThan(
            smallTime * 12,""")
    body = body.replace(helpers, allocated.lstrip("\n"))
elif which == "C":
    body = body.replace(loop, """        var (smallTime, largeTime) = BestOfEach(() => Probe(small), () => Probe(large), collect: true);

        largeTime.Should().BeLessThan(
            smallTime * 8,""")
    body = body.replace(helpers + "\n", "")
elif which == "D":
    body = body.replace(probe, """        static void Probe(PdfDocument document)
        {
            var probe = CrossReferenceProbe.Run(document, room: 1000);""")
elif which == "DC":
    body = body.replace(loop, """        var (smallTime, largeTime) = BestOfEach(() => Probe(small), () => Probe(large), collect: true);

        largeTime.Should().BeLessThan(
            smallTime * 8,""")
    body = body.replace(helpers + "\n", "")
    body = body.replace(probe, """        static void Probe(PdfDocument document)
        {
            var probe = CrossReferenceProbe.Run(document, room: 1000);""")
else:
    raise SystemExit("unknown prototype " + which)

open(path, "w").write(s[:start] + body + s[end:])
print("applied", which)
