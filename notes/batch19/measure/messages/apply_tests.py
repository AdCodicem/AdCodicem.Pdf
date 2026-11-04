"""Brings the unit tests the #241 prototype breaks in line with it: each old text must occur exactly once."""
import sys

ROOT = sys.argv[1]

EDITS = {
    "tests/AdCodicem.Pdf.Tests/DocumentReaderTests.cs": [
        ('.And.OnlyContain(entry => entry.Message.EndsWith(" was found -3 bytes from where the index said.", StringComparison.Ordinal));',
         '.And.OnlyContain(entry => entry.Message.EndsWith(" was found 3 bytes before where the index said.", StringComparison.Ordinal));'),
        ('"Object stream 5 has a header that holds something other than an object number and an offset after 0 of the 1 objects its /N declares; none of its objects can be read from it.");',
         '"Object stream 5 has a header that holds something other than an object number and an offset after 0 of the 1 object its /N declares; none of its objects can be read from it.");'),
        ('"Object stream 5 has a header that lists object -5 at offset 0, which no member can be, after 0 of the 1 objects its /N declares; none of its objects can be read from it.",',
         '"Object stream 5 has a header that lists object -5 at offset 0, which no member can be, after 0 of the 1 object its /N declares; none of its objects can be read from it.",'),
        ('"Object stream 7 has a header that lists object -5 at offset 0, which no member can be, after 0 of the 1 objects its /N declares; none of its objects can be read from it.");',
         '"Object stream 7 has a header that lists object -5 at offset 0, which no member can be, after 0 of the 1 object its /N declares; none of its objects can be read from it.");'),
        ('$"was found {stream - named} bytes from there."));',
         '$"was found {stream - named} bytes after it."));'),
    ],
    "tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs": [
        ('    [InlineData(-7)]\n    [InlineData(-2)]\n    [InlineData(1)]\n    [InlineData(3)]\n    [InlineData(12)]\n    public void Reads_a_section_that_prev_names_a_few_bytes_off(int error)',
         '    [InlineData(-7, "7 bytes after")]\n    [InlineData(-2, "2 bytes after")]\n    [InlineData(1, "1 byte before")]\n    [InlineData(3, "3 bytes before")]\n    [InlineData(12, "12 bytes before")]\n    public void Reads_a_section_that_prev_names_a_few_bytes_off(int error, string found)'),
        ('$"The cross-reference section /Prev names at offset {original + error} was found {-error} bytes from there."));',
         '$"The cross-reference section /Prev names at offset {original + error} was found {found} it."));'),
    ],
    "tests/AdCodicem.Pdf.Tests/HeaderOffsetTests.cs": [
        ('$"The cross-reference chain has more than 1 sections; the older ones were not read: the chain goes on at offset {LargestShifted}, outside the file.");',
         '$"The cross-reference chain has more than 1 section; the older ones were not read: the chain goes on at offset {LargestShifted}, outside the file.");'),
    ],
}

for path, edits in EDITS.items():
    full = f"{ROOT}/{path}"
    text = open(full, encoding="utf-8").read()
    for old, new in edits:
        count = text.count(old)
        if count != 1:
            sys.exit(f"{path}: {count} occurrences of {old[:80]!r}")
        text = text.replace(old, new)
    open(full, "w", encoding="utf-8").write(text)
    print(f"{path}: {len(edits)} edits")
