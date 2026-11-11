import sys

p = sys.argv[1] + '/work/entry.src'
s = open(p, encoding='utf-8').read()
reps = [
    ("the diagnostics counted a guard met again as one the capacity left out, with room to spare;",
     "the diagnostics counted a guard met again as one the capacity left out, with room to spare, since `e157359` and "
     "`68a55fa`;"),
    ("from an older one, with nothing said ([#327]).",
     "from an older one, with nothing said: `2b26d18`'s line test, asked at the digit [#327]'s walk finds, where no "
     "token starts."),
]
for a, b in reps:
    assert s.count(a) == 1, a
    s = s.replace(a, b)
open(p, 'w', encoding='utf-8', newline='\n').write(s)
print('ok')
