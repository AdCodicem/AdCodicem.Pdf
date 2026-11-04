import os, re, sys
# Counts, in every corpus file, object headers with a comment between their tokens, and headers whose tokens an end of
# line separates (the only places the prototypes read differently from main).
root = sys.argv[1]
with_comment = re.compile(rb'(?<![0-9])[0-9]+(?:[ \t\r\n\f\x00]|%[^\r\n]*[\r\n])*?%[^\r\n]*[\r\n](?:[ \t\r\n\f\x00]|%[^\r\n]*[\r\n])*[0-9]+(?:[ \t\r\n\f\x00]|%[^\r\n]*[\r\n])+obj(?![A-Za-z])'
                          rb'|(?<![0-9])[0-9]+[ \t\r\n\f\x00]+[0-9]+(?:[ \t\r\n\f\x00])*%[^\r\n]*[\r\n](?:[ \t\r\n\f\x00]|%[^\r\n]*[\r\n])*obj(?![A-Za-z])')
eol_between = re.compile(rb'(?<![0-9])[0-9]+[ \t\f\x00]*[\r\n][ \t\r\n\f\x00]*[0-9]+[ \t\r\n\f\x00]+obj(?![A-Za-z])|(?<![0-9])[0-9]+[ \t\f\x00]+[0-9]+[ \t\f\x00]*[\r\n][ \t\r\n\f\x00]*obj(?![A-Za-z])')
files = 0
hits = 0
eols = 0
for dirpath, _, names in os.walk(root, followlinks=True):
    for name in names:
        if not name.lower().endswith('.pdf'):
            continue
        path = os.path.join(dirpath, name)
        try:
            data = open(path, 'rb').read()
        except OSError:
            continue
        files += 1
        found = with_comment.findall(data)
        e = eol_between.findall(data)
        if found:
            hits += 1
            print('COMMENT', os.path.relpath(path, root), len(found), [f[:60] for f in found[:3]])
        if e:
            eols += 1
            print('EOL', os.path.relpath(path, root), len(e), [f[:40] for f in e[:3]])
print('files', files, 'with comment headers', hits, 'with an end of line between header tokens', eols)
