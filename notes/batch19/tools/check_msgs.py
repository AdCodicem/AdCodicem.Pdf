import re
import sys

data = open(sys.argv[1], encoding='utf-8').read()
types = {'build', 'chore', 'ci', 'docs', 'feat', 'fix', 'perf', 'refactor', 'revert', 'style', 'test'}
exp2 = 'Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>'
exp3 = 'Claude-Session: https://claude.ai/code/session_01PiFf9bygtdCAcN4va1ss8T'
for rec in data.split('\x01'):
    rec = rec.strip('\n')
    if not rec:
        continue
    sha, msg = rec.split('\x00', 1)
    lines = msg.rstrip('\n').split('\n')
    h = lines[0]
    m = re.match(r'^(\w+)(\([^)]*\))?(!)?: (.+)$', h)
    probs = []
    if not m:
        probs.append('header format')
    else:
        t, _, _, s = m.groups()
        if t not in types:
            probs.append('type')
        if s[0].isupper():
            probs.append('subject case')
        if s.endswith('.'):
            probs.append('full stop')
    if len(h) > 100:
        probs.append('header len %d' % len(h))
    if len(lines) > 1 and lines[1] != '':
        probs.append('no blank after header')
    for i, l in enumerate(lines):
        if len(l) > 79 and i > 0:
            probs.append('line %d len %d' % (i + 1, len(l)))
    for i, l in enumerate(lines):
        if re.search(r'#\d+', l) and not l.startswith('Refs '):
            probs.append('issue number in line %d: %s' % (i + 1, l))
    tail = lines[-4:]
    if not (tail[0].startswith('Refs #') and tail[1] == '' and tail[2] == exp2 and tail[3] == exp3):
        probs.append('footer ' + repr(tail))
    if lines[-5] != '':
        probs.append('no blank before Refs')
    print(sha[:7], len(h), probs or 'OK')
