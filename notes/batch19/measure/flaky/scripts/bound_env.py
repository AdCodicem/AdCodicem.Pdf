"""Measurement-only: makes the validator's linear-time test read its bound from ZZ_BOUND, so that a run with a bound of
0.001 always fails and prints both times. Not a prototype.

usage: python3 -I bound_env.py <worktree>
"""
import sys

path = sys.argv[1] + "/tests/AdCodicem.Pdf.Tests/ReaderTimingTests.cs"
s = open(path).read()
start = s.index("public void The_validator_judges_an_object_stream_s_members_in_linear_time")
end = s.index("[Theory]", start)
body = s[start:end]
old = "            smallTime * 8,"
assert body.count(old) == 1
body = body.replace(old, '            smallTime * double.Parse(Environment.GetEnvironmentVariable("ZZ_BOUND") ?? "8", CultureInfo.InvariantCulture),')
open(path, "w").write(s[:start] + body + s[end:])
print("bound from ZZ_BOUND")
