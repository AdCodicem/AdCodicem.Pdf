import re, sys
# argv[1]: worktree root; argv[2]: file listing Markdown paths, one per line.
root = sys.argv[1]
for rel in open(sys.argv[2]).read().split():
    text = open(root + '/' + rel, encoding='utf-8').read()
    # strip fenced code
    text_nc = re.sub(r'```.*?```', '', text, flags=re.S)
    used = set(re.findall(r'\[(#\d+)\](?![(:])', text_nc))
    defined = set(re.findall(r'^\[(#\d+)\]:', text, flags=re.M))
    missing = sorted(used - defined, key=lambda s: int(s[1:]))
    unused = sorted(defined - used, key=lambda s: int(s[1:]))
    print(rel, 'used', len(used), 'missing', missing, 'unused', unused)
