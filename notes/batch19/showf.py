import re,sys
t=open(sys.argv[1]).read()
parts=re.split(r'(?=^######## \[)',t,flags=re.M)
d={}
for p in parts:
  m=re.match(r'######## \[(\d+)\]',p)
  if m: d[int(m.group(1))]=p
ev=int(sys.argv[2])
for k in map(int,sys.argv[3:]):
  p=d[k]
  def sec(name,nxt):
    m=re.search(re.escape(name)+r'(.*?)(?='+nxt+r'|\Z)',p,flags=re.S)
    return m.group(1).strip() if m else ''
  print(p.splitlines()[0])
  print('LOC:',sec('LOCATION:','\nCLAIM:'))
  print('CLAIM:',sec('CLAIM:','\nEVIDENCE:'))
  e=sec('EVIDENCE:','\nFIX:'); print('EVID:',e[:ev]+('…' if len(e)>ev else ''))
  print('FIX:',sec('FIX:','\n--- REFUTER:'))
  print('REF:',sec('--- REFUTER:','\nREFUTER EVIDENCE:'))
  e=sec('REFUTER EVIDENCE:','\nREFUTER FIX:'); print('REFEV:',e[:ev]+('…' if len(e)>ev else ''))
  print('REFFIX:',sec('REFUTER FIX:','\Z'))
  print()
