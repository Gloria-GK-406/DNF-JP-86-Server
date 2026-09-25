import ctypes,struct,json,sys,datetime
from ctypes import wintypes as w
k=ctypes.WinDLL('kernel32',use_last_error=True)
k.OpenProcess.argtypes=[w.DWORD,w.BOOL,w.DWORD];k.OpenProcess.restype=w.HANDLE
k.ReadProcessMemory.argtypes=[w.HANDLE,w.LPCVOID,w.LPVOID,ctypes.c_size_t,ctypes.POINTER(ctypes.c_size_t)];k.ReadProcessMemory.restype=w.BOOL
k.CloseHandle.argtypes=[w.HANDLE]
h=k.OpenProcess(0x410,False,int(sys.argv[1]))
assert h,ctypes.get_last_error()
def read(a,n):
 b=ctypes.create_string_buffer(n);got=ctypes.c_size_t()
 if not k.ReadProcessMemory(h,a,b,n,ctypes.byref(got)) or got.value!=n:raise OSError(ctypes.get_last_error(),hex(a))
 return b.raw

def u(a):return struct.unpack('<I',read(a,4))[0]
def vec(a):
 b,e=u(a),u(a+4)
 if not b and not e:return []
 assert 0<=e-b<=400000 and (e-b)%4==0,(hex(a),hex(b),hex(e))
 return list(struct.unpack('<'+'I'*((e-b)//4),read(b,e-b))) if e>b else []
def protected(a):
 handle,cipher=u(a),u(a+4)
 mgr=u(0x3b89618);page=u(mgr+0x24+(handle>>16)*4);key=u(page+0x2114+(handle&65535)*4)
 value=cipher^((key&65535)*0x10001)
 assert a%16==12, 'snapshot supports aligned object +9C only'
 check=(((value>>16)+(value&65535)+(key&65535))&65535)^(key&65535)
 assert check==key>>16, 'protected value checksum mismatch'
 return value
assert read(0x1185970,4).hex()=='558bec6a'
sys.path.insert(0,r'E:/Project/DNF/Document')
from _a21_static_analysis_lib import PE
pe=PE()
for a,n in [(0x17284b0,0x138),(0x1722cb0,0x16a),(0x1765290,0x7a),(0x27bc240,0x6a),(0x1910510,0x27),(0x1910570,10)]:
 assert read(a,n)==pe.read(a,n), 'live code differs at '+hex(a)
rows=[]
for cell in vec(0x3a8f5f8):
 if not cell:continue
 for obj in vec(cell+0xc):
  if not obj:continue
  vt=u(obj)
  row=dict(cell=hex(cell),obj=hex(obj),vt=hex(vt),getcat=hex(u(vt+0x48)),getnum=hex(u(vt+0x4c)))
  if vt==0x32dc624:
   assert read(u(vt+0x48),7).hex()=='8b8198000000c3'
   row.update(number=protected(obj+0x9c),item=hex(u(obj+0x1134)),inflight=read(obj+0x1139,1)[0],owner=hex(u(obj+0xb8)),raw98=hex(u(obj+0x98)))
  rows.append(row)
actor=u(0x3b3dfb8)
result=dict(actor=hex(actor),actorOwner=hex(u(actor+0xb8)),utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),pid=int(sys.argv[1]),rows=rows)
if len(sys.argv)>2:
 from pathlib import Path
 Path(sys.argv[2]).write_text(json.dumps(result,indent=2),encoding='utf-8')
 print(json.dumps(dict(actor=result['actor'],actorOwner=result['actorOwner'],drops=[r for r in rows if 'number' in r]),indent=2))
else:print(json.dumps(result,indent=2))
k.CloseHandle(h)
