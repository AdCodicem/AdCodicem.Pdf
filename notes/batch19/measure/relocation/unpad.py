# Removes the test files' spacers that keep a newer section out of the relocation search's reach (#188), for the padding
# experiment. Run from the worktree root: python3 -I <path>/unpad.py
def sub(path, old, new, count=None):
    s = open(path, encoding='utf-8').read()
    n = s.count(old)
    assert n > 0, (path, old)
    if count is not None:
        assert n == count, (path, old, n)
    s = s.replace(old, new)
    open(path, 'w', encoding='utf-8').write(s)
    print(path, repr(old[:60]), n)

cf = 'tests/AdCodicem.Pdf.Tests/ChainFiles.cs'
# The ten 1,200-byte comments after an older stream (UnderAnUpdate and the nine builders copied from it).
sub(cf, '        file.Text("%" + new string(\'-\', 1200) + "\\n");\n', '', 10)
# UpdateWithIndirectLength(olderIndirect) and OlderWithIndirectLength.
sub(cf, 'file.Text("%" + new string(\'x\', 2 * AdCodicem.Pdf.IO.PdfFileReader.NearbySearchRadius) + "\\n");', '{ }', 2)
# CrossReferenceChainTests.Updated(padding: 600): the comment after each section the test is about.
sub('tests/AdCodicem.Pdf.Tests/CrossReferenceChainTests.cs',
    '            if (padding > 0)\n            {\n                Append($"%{new string(\'-\', padding)}\\n");',
    '            if (padding < 0)\n            {\n                Append($"%{new string(\'-\', padding)}\\n");', 1)
# DocumentReaderTests.WithUpdatePlacingFourOutside.
sub('tests/AdCodicem.Pdf.Tests/DocumentReaderTests.cs',
    "var update = new StringBuilder(\"%\").Append('x', 2 * PdfFileReader.NearbySearchRadius).Append('\\n');",
    'var update = new StringBuilder();', 1)
# GuardCutValueTests: two builders of an older stream then a newer table.
sub('tests/AdCodicem.Pdf.Tests/GuardCutValueTests.cs', '        file.Raw("%" + new string(\'-\', 1200) + "\\n");\n', '', 2)
