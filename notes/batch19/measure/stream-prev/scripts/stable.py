import sys

R = '/tmp/claude-0/-home-user-AdCodicem-Pdf/b5ddd9fc-0507-5e11-8a94-c9e1fedd4860/scratchpad/lot19/measure/stream-prev/'
for m in [0, 1, 3, 5, 9, 13, 17, 21]:
    a = open(R + f'shapes-r3-m{m}.txt').read().split('### Q1')[0]
    b = open(R + (f'shapes-r1-m{m}.txt' if m else 'shapes-main-1.txt')).read()
    print(m, 'same as r1' if a == b else 'DIFFERS')
