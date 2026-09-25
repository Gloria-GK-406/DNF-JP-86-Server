import ctypes,struct,json,time,sys,datetime
from ctypes import wintypes as w
from pathlib import Path
k=ctypes.WinDLL('kernel32',use_last_error=True)
k.OpenProcess.argtypes=[w.DWORD,w.BOOL,w.DWORD];k.OpenProcess.restype=w.HANDLE
k.ReadProcessMemory.argtypes=[w.HANDLE,w.LPCVOID,w.LPVOID,ctypes.c_size_t,ctypes.POINTER(ctypes.c_size_t)]
k.CloseHandle.argtypes=[w.HANDLE]
h=k.OpenProcess(0x410,False,int(sys.argv[1]))
assert h,ctypes.get_last_error()
def read(a,n):
 v=ctypes.create_string_buffer(n);got=ctypes.c_size_t()
 if not k.ReadProcessMemory(h,a,v,n,ctypes.byref(got)) or got.value!=n: raise OSError(ctypes.get_last_error(),hex(a))
 return v.raw
def u(a):return struct.unpack('<I',read(a,4))[0]
def protected(a):
 handle,cipher=u(a),u(a+4);mgr=u(0x3b89618)
 page=u(mgr+0x24+(handle>>16)*4);key=u(page+0x2114+(handle&65535)*4)
 value=cipher^((key&65535)*0x10001)
 assert a%16==4
 check=(((value&65535)-(value>>16))&65535)^(key&65535)
 assert check==key>>16
 return value

actor=u(0x3b3dfb8); assert actor
out=Path(sys.argv[2]); last={};t=time.monotonic();prevcount=None
with out.open('w',encoding='utf-8') as f:
 def emit(row):
  row['elapsed']=round(time.monotonic()-t,4);f.write(json.dumps(row)+'\n');f.flush();print(json.dumps(row),flush=True)
 emit(dict(event='ready',actor=hex(actor)))
 while time.monotonic()-t<300:
  try:
   scene=u(actor+0xb8);start,end=u(scene+0xc),u(scene+0x10)
   assert 0<=end-start<100000
   objs=struct.unpack('<'+'I'*((end-start)//4),read(start,end-start));now={}
   for o in objs:
    vt=u(o);now[o]=vt
    if o not in last:
     emit(dict(event='add',obj=hex(o),vt=hex(vt),raw=read(o,0x2cc).hex()))
   for o in last:
    if o not in now: emit(dict(event='remove',obj=hex(o),vt=hex(last[o])))
   last=now
   bag=u(0x3a5c9bc);item=u(u(bag+0x58)+184*4);count=protected(item+0x274)
   if count!=prevcount:emit(dict(event='count',count=count,icon=read(item+0x130,8).hex(),pending=hex(u(actor+0x61b0))));prevcount=count
  except (OSError,AssertionError) as e: emit(dict(event='readerror',error=str(e)));break
  time.sleep(.005)
k.CloseHandle(h)
