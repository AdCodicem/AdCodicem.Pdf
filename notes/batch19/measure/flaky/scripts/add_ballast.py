import sys

p = sys.argv[1] + '/tests/AdCodicem.Pdf.Tests/ZzFlaky317.cs'
s = open(p).read()
old = '''        var whole = Stopwatch.StartNew();
'''
new = '''        var whole = Stopwatch.StartNew();

        // A heap such as the suite's leaves before the timing collection: that many megabytes of live objects, half in
        // arrays of 64 KB, half in small strings.
        var ballastMb = int.TryParse(Environment.GetEnvironmentVariable("ZZ317_BALLAST_MB"), out var b) ? b : 0;
        var ballast = new List<byte[]>();

        for (var i = 0; i < ballastMb * 8; i++)
        {
            ballast.Add(new byte[(64 * 1024) - 64]);
        }

        var ballastObjects = new List<object>();

        for (var i = 0; i < ballastMb * 2048; i++)
        {
            ballastObjects.Add(new string('x', 100));
        }
'''
assert s.count(old) == 1
s = s.replace(old, new)
old2 = '''        File.AppendAllText(rawPath, raw.ToString());
'''
assert s.count(old2) == 1
s = s.replace(old2, old2 + '''        GC.KeepAlive(ballast);
        GC.KeepAlive(ballastObjects);
''')
old3 = '''\\tnoGcEntered={NoGcEntered}");'''
assert s.count(old3) == 1
s = s.replace(old3, '''\\tnoGcEntered={NoGcEntered}\\tballastMb={ballastMb}");''')
open(p, 'w').write(s)
print('ok')
