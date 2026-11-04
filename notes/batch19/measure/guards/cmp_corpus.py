import sys, collections
def parse(p):
    docs = collections.OrderedDict(); cur=None
    for line in open(p, encoding='utf-8'):
        line=line.rstrip('\n')
        if line.startswith('== '):
            cur=line[3:]; docs[cur]={'F':[], 'D':[], 'R':[], 'T':[]}
        elif line.startswith('    F '): docs[cur]['F'].append(line[6:])
        elif line.startswith('    D '): docs[cur]['D'].append(line[6:])
        elif line.startswith('  round '): docs[cur]['R'].append(line)
        else: docs[cur]['T'].append(line)
    return docs
a=parse(sys.argv[1]); b=parse(sys.argv[2])
print('docs', len(a), len(b), 'same keys', list(a)==list(b))
nf=nfo=nd=ndo=0
for k in a:
    x,y=a[k],b[k]
    if x['R']!=y['R'] or x['T']!=y['T']: print('ROUNDS/OTHER', k, x['R'], y['R'], x['T'], y['T'])
    if x['F']!=y['F']:
        if sorted(x['F'])==sorted(y['F']): nfo+=1; print('F order only', k)
        else:
            nf+=1; print('F DIFF', k)
            ca,cb=collections.Counter(x['F']),collections.Counter(y['F'])
            for l in (ca-cb): print('   -', l[:230])
            for l in (cb-ca): print('   +', l[:230])
    if x['D']!=y['D']:
        ca,cb=collections.Counter(x['D']),collections.Counter(y['D'])
        if ca==cb: ndo+=1; print('D order only', k, len(x['D']))
        else:
            nd+=1; print('D DIFF', k, len(x['D']), len(y['D']))
            for l in list(ca-cb)[:8]: print('   -', l[:230])
            print('   ... -', sum((ca-cb).values()), '+', sum((cb-ca).values()))
            for l in list(cb-ca)[:8]: print('   +', l[:230])
print('F diff', nf, 'F order', nfo, 'D diff', nd, 'D order', ndo)
print('totals main F', sum(len(v['F']) for v in a.values()), 'D', sum(len(v['D']) for v in a.values()))
print('totals branch F', sum(len(v['F']) for v in b.values()), 'D', sum(len(v['D']) for v in b.values()))
