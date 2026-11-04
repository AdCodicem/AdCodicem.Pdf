import sys


def blocks(path):
    out = {}
    cur = None
    for line in open(path, encoding='utf-8'):
        line = line.rstrip('\n')
        if line.startswith('=== '):
            cur = line
            out[cur] = []
            continue
        if cur is not None and 'object.redefined' not in line:
            out[cur].append(line)
    return out


def main():
    path = sys.argv[1]
    wanted = sys.argv[2:]
    b = blocks(path)
    for key, lines in b.items():
        if any(key.startswith(w) for w in wanted):
            print(key)
            for line in lines:
                print(line)


main()
