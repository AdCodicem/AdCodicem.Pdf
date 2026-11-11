"""Writes <name>.diff for a draft against the head's copy, in the form git diff --no-index gives it.

The head's file is a/<repo path>, the draft b/<its absolute path>; three lines of context; each hunk header carries git's
default function context, the last line before the hunk that starts with a letter, '_' or '$', cut to 80 bytes. Written
because the review agent may not run git diff --no-index outside its worktree; GNU diff gives the hunks.

Usage: python -I mkdiff.py <status-draft folder> <name> <repo path>, e.g. status docs/status.md.
"""
import hashlib
import os
import re
import subprocess
import sys

base, name, repo = sys.argv[1], sys.argv[2], sys.argv[3]
head = os.path.join(base, name + '.head.md')
draft = os.path.join(base, name + '.md')
out = os.path.join(base, name + '.diff')
absolute = os.path.abspath(draft).replace('\\', '/')


def blob(path):
    data = open(path, 'rb').read()
    return hashlib.sha1(b'blob %d\0' % len(data) + data).hexdigest()[:7]


old = open(head, 'rb').read()
if old == open(draft, 'rb').read():
    open(out, 'wb').close()
    sys.exit(0)

result = subprocess.run(['diff', '-U3', '--label', 'a/' + repo, '--label', 'b/' + absolute, head, draft],
                        capture_output=True)
if result.returncode != 1:
    raise SystemExit('diff failed: ' + result.stderr.decode('utf-8', 'replace'))

old_lines = old.split(b'\n')
func = re.compile(rb'^[A-Za-z_$]')
lines = result.stdout.split(b'\n')
for i, line in enumerate(lines):
    m = re.match(rb'^@@ -(\d+)(?:,\d+)? \+\d+(?:,\d+)? @@$', line)
    if not m:
        continue
    start = int(m.group(1))
    for k in range(start - 2, -1, -1):
        if func.match(old_lines[k]):
            lines[i] = line + b' ' + old_lines[k][:80].rstrip()
            break

header = ('diff --git a/%s b/%s\nindex %s..%s 100644\n' % (repo, absolute, blob(head), blob(draft))).encode()
open(out, 'wb').write(header + b'\n'.join(lines))
