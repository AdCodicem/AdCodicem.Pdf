import sys
# Applies on top of p197-window.diff: one byte before each nearby window, so that a number the window's start cuts is not
# taken for a header that starts there.
p = sys.argv[1]
s = open(p, encoding='utf-8').read()

old = '''        using var window = source.GetWindow(start, (int)length);
        var span = window.Memory.Span;
        var searchFrom = 0;

        while (searchFrom < span.Length)
        {
            var index = span[searchFrom..].IndexOf(ObjKeyword);
            if (index < 0)
            {
                return false;
            }

            var position = searchFrom + index;

            if (TryReadHeaderBackwards(span, position, out var found, out var foundGeneration, out var headerStart) && found == number)
            {
                actualOffset = start + headerStart;'''
new = '''        // The byte before the window, where it does not start the file, tells a number the window's start cuts - "14 0 obj"
        // read from its "4" - from one that starts there: a header is taken only where it starts in the window.
        var lead = start > 0 ? 1 : 0;
        using var window = source.GetWindow(start - lead, (int)length + lead);
        var span = window.Memory.Span;
        var searchFrom = 0;

        while (searchFrom < span.Length)
        {
            var index = span[searchFrom..].IndexOf(ObjKeyword);
            if (index < 0)
            {
                return false;
            }

            var position = searchFrom + index;

            if (TryReadHeaderBackwards(span, position, out var found, out var foundGeneration, out var headerStart) &&
                headerStart >= lead && found == number)
            {
                actualOffset = start - lead + headerStart;'''
assert s.count(old) == 1, 'object window'
s = s.replace(old, new)

old = '''        var candidates = new List<long>();

        using (var window = _source.GetWindow(start, length))
        {
            FindSectionCandidates(window.Memory.Span, start, candidates);
        }'''
new = '''        var candidates = new List<long>();

        // The byte before the window, as for an object's (#197): a candidate starts in the window.
        var lead = start > 0 ? 1 : 0;

        using (var window = _source.GetWindow(start - lead, length + lead))
        {
            FindSectionCandidates(window.Memory.Span, start - lead, candidates);
        }

        candidates.RemoveAll(candidate => candidate < start);'''
assert s.count(old) == 1, 'section window'
s = s.replace(old, new)
open(p, 'w', encoding='utf-8').write(s)
print('197e applied')
