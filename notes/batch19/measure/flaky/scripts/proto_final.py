"""Applies one prototype of #317, with its comment, to ReaderTimingTests.cs in the worktree.

usage: python3 -I proto_final.py <worktree> <A|B|C|D>
  A  bound 12, five turns as now
  B  allocation held (below 6 times) and time through BestOfEach(collect: true) below 12 times, as the page tree test
  C  BestOfEach(collect: true), bound 8
  D  the probe given the room a validation gives it by default (1,000 findings), five turns, bound 8
"""
import sys

path = sys.argv[1] + "/tests/AdCodicem.Pdf.Tests/ReaderTimingTests.cs"
which = sys.argv[2]
s = open(path).read()
start = s.index("public void The_validator_judges_an_object_stream_s_members_in_linear_time")
end = s.index("[Theory]", start)
body = s[start:end]

comment = """        // One object stream of many members, each placed by the index: the probe finds each in the header through a lookup
        // built once, and each member written as a stream by a search of those it found, not by scanning the header for
        // each — four times the members take about four times as long, where a scan would take sixteen. The probe alone is
        // timed, the documents opened and settled before, and each probed once first; each run starts from a collected heap,
        // so that the findings of a run before it, one for each member, are not collected inside the next.
"""
assert body.count(comment) == 1

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
consts = """        const int Small = 50_000;
        const int Large = 4 * Small;
"""
assert body.count(consts) == 1

if which == "A":
    body = body.replace(comment, comment + """        // The large probe keeps a finding for each member, 60 MB and more, and meets five collections or six that copy them,
        // a full one among them, where the small probe meets one: the ratio is 5 to 8 on a quiet machine, and passed 8 in CI
        // three times in some three hundred runs (#317). A bound of 12 still tells a probe that grows with the members from
        // one that scans the header for each, which took 14 to 19 times as long.
""")
    body = body.replace(loop, loop.replace("smallTime * 8,", "smallTime * 12,"))
elif which == "B":
    body = body.replace(comment, comment + """        // What each probe allocates on the test's thread holds the cost whatever else the process does: the large probe
        // allocates 4.0 times what the small one does, on every run. A scan of the header for each member allocates nothing,
        // so the time is held as well, as the ratio of the best probe of each, run in turn until neither improves, each from a
        // collected heap: the large probe keeps a finding for each member and meets five collections or six that copy them,
        // where the small probe meets one, and the ratio is 5 to 8 on a quiet machine and passed 8 in CI (#317). A bound of
        // 12 still tells a probe that grows with the members from one that scans the header for each, 14 to 19 times as long.
""")
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
    body = body.replace(helpers, """        static long Allocated(Action action)
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            action();
            return GC.GetAllocatedBytesForCurrentThread() - allocated;
        }
""")
elif which == "C":
    body = body.replace(comment, comment + """        // The probes are run in turn until neither improves for three turns (#317).
""")
    body = body.replace(loop, """        var (smallTime, largeTime) = BestOfEach(() => Probe(small), () => Probe(large), collect: true);

        largeTime.Should().BeLessThan(
            smallTime * 8,""")
    body = body.replace(helpers + "\n", "")
elif which == "D":
    body = body.replace(comment, """        // One object stream of many members, each placed by the index: the probe finds each in the header through a lookup
        // built once, and each member written as a stream by a search of those it found, not by scanning the header for
        // each — four times the members take about four times as long, where a scan would take sixteen. The probe alone is
        // timed, the documents opened and settled before, and each probed once first; each run starts from a collected heap.
        // The probe is given the room a validation gives it by default, and writes that many findings, counting the rest
        // (#317). Given room for every finding, it kept one for each member, 63 MB for the large stream, and every large
        // probe met five collections or seven that copied them, a full one among them, where the small probe met one: the
        // ratio was 3.3 to 8.2 alone, up to 11.3 under four busy loops, and passed 8 in CI three times in some three hundred
        // runs. Given 1,000, the probes of a stream whose every index is wrong meet no collection, and the two rows that
        // write findings measured 2.2 to 6.1 in 162 runs, on .NET 10 and 11, alone, under coverage, under load and in the
        // suite; a scan of the header for each member still takes 14 to 20 times as long.
""")
    body = body.replace(consts, consts + """        const int Room = 1000;
""")
    body = body.replace(probe, """        static void Probe(PdfDocument document)
        {
            var probe = CrossReferenceProbe.Run(document, Room);""")
else:
    raise SystemExit("unknown prototype " + which)

open(path, "w").write(s[:start] + body + s[end:])
print("applied", which)
