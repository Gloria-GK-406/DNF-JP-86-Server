import struct, csv, sys, re
from pathlib import Path
DUMP=Path(r"D:\project\DNF\DNFClient-A21-debug\dumps\dnf-dump-20260822-232754-263-26888")
img=DUMP.joinpath("DNF-memory-rva.bin").read_bytes()
BASE=0x400000
def _crypt(src, key):
    out=bytearray(); k=key; ln=len(src); nd=ln//4
    for i in range(nd):
        d=struct.unpack_from('<I',src,i*4)[0]
        v=(d^k)&0xFFFFFFFF
        out+=struct.pack('<I',v)
        k=(k*0x1003F+v)&0xFFFFFFFF
    for i in range(nd*4,ln):
        c=src[i]^(k&0xFF); out.append(c); k=(k*0x107+c)&0xFFFFFFFF
    return bytes(out)
def dec(va):
    o=va-BASE
    if o<0 or o+4>len(img): return None
    b=img[o:o+4]
    magic=struct.unpack_from('<H',b,0)[0]
    if magic==0x2CA1:
        p=o+4
        e=p
        while e+1<len(img) and img[e:e+2]!=b'\0\0': e+=2
        if e-p>4000: return None
        try: return ('P',img[p:e].decode('utf-16le'))
        except: return None
    s=b[1]&0xFE
    w=struct.unpack_from('<H',b,2)[0]
    n=(((s<<7)|s)^w)&0xFFFFFFFF
    best=None
    for mode in (2,1):
        ln=n*mode
        if ln==0 or ln>0x2000 or o+4+ln>len(img): continue
        d=_crypt(img[o+4:o+4+ln], (s|0x9A714CA0)&0xFFFFFFFF)
        if mode==2:
            if len(d)%2: continue
            try: t=d.decode('utf-16le')
            except: continue
        else:
            try: t=d.decode('cp949')
            except:
                try: t=d.decode('latin1')
                except: continue
        t=t.rstrip('\x00')
        if not t: continue
        bad=sum(1 for c in t if ord(c)<0x20 and c not in '\r\n\t')
        if bad*4>len(t): continue
        printable=sum(1 for c in t if 0x20<=ord(c)<0x7f)
        score=printable/max(1,len(t))
        if best is None or score>best[0]: best=(score,('D%d'%mode,t))
    if best and best[0]>0.55: return best[1]
    return None
