"""Lists each failed test of an MTP log with the first lines of its reason."""
import sys, re

lines = open(sys.argv[1], encoding='utf-8', errors='replace').read().split('\n')
out = []
i = 0
while i < len(lines):
    line = lines[i]
    if line.startswith('failed '):
        name = re.sub(r' \(\d+m?s\)$', '', line[len('failed '):])
        reason = []
        j = i + 1
        while j < len(lines) and not lines[j].startswith(('failed ', 'passed ', 'skipped ')) and len(reason) < 400:
            t = lines[j].strip()
            if t and not t.startswith('at ') and 'Stack Trace' not in t and not t.startswith('---'):
                reason.append(t)
            j += 1
        out.append((name, ' '.join(reason)[:600]))
        i = j
    else:
        i += 1
for name, reason in out:
    print('-', name)
    print('   ', reason)
print(len(out), 'failures')
