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
actor=u(0x3b3dfb8);assert actor
bag=u(0x3a5c9bc);start,end=u(bag+0x58),u(bag+0x5c)
assert 0<end-start<4000
slots=[]
for slot in range((end-start)//4):
 obj=u(start+slot*4)
 if obj and u(obj+0x20)==10149153: slots.append((slot,obj))
assert slots
out=Path(sys.argv[2]);last=None;t=time.monotonic()
try:
 with out.open('w',encoding='utf-8') as f:
  while time.monotonic()-t<180:
   mgr=u(0x3a5c980);view=u(mgr+0x54)
   row=dict(actor=hex(actor),scene=hex(u(actor+0xb8)),view=hex(view),viewActor=hex(u(view+0x474)),effectPending=hex(u(actor+0x61b0)),slots=[dict(slot=s,obj=hex(o),item=u(o+0x20),count=protected(o+0x274)) for s,o in slots])
   key=json.dumps(row,sort_keys=True)
   if key!=last:
    row['utc']=datetime.datetime.now(datetime.timezone.utc).isoformat();line=json.dumps(row);f.write(line+'\n');f.flush();print(line,flush=True);last=key
   time.sleep(.02)
finally:k.CloseHandle(h)
