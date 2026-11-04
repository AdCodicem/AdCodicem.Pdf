"""Counts, in every corpus PDF, the rows of each type in every cross-reference stream libqpdf indexes (pikepdf), read
independently of our reader: each /Type /XRef stream decoded by libqpdf, its rows split by /W. Prints a histogram of
types, and every document with a type past 2."""
import sys, os, collections
import pikepdf

roots = sys.argv[1:]
files = []
for root in roots:
    for d, _, names in os.walk(root, followlinks=True):
        for n in names:
            if n.lower().endswith('.pdf'):
                files.append(os.path.join(d, n))
files.sort()
types = collections.Counter()
docs_with_streams = 0
streams = 0
reserved_docs = []
errors = []
for f in files:
    try:
        with pikepdf.open(f, attempt_recovery=True) as pdf:
            found = False
            for obj in pdf.objects:
                if not isinstance(obj, pikepdf.Stream):
                    continue
                if obj.get('/Type') != pikepdf.Name.XRef:
                    continue
                found = True
                streams += 1
                w = [int(x) for x in obj.get('/W', [])]
                if len(w) < 3 or sum(w) == 0:
                    continue
                data = obj.read_bytes()
                size = sum(w)
                local = collections.Counter()
                for i in range(0, len(data) - size + 1, size):
                    t = int.from_bytes(data[i:i + w[0]], 'big') if w[0] else 1
                    local[t] += 1
                types.update(local)
                if any(t > 2 for t in local):
                    reserved_docs.append((f, dict(local)))
            docs_with_streams += found
    except Exception as e:
        errors.append((f, type(e).__name__, str(e)[:120]))
print('files', len(files), 'with xref streams', docs_with_streams, 'xref streams', streams)
print('row types', dict(sorted(types.items())))
print('documents with a type past 2:', len(reserved_docs))
for f, local in reserved_docs:
    print('  ', f, local)
print('errors', len(errors))
for e in errors:
    print('  ', e)
