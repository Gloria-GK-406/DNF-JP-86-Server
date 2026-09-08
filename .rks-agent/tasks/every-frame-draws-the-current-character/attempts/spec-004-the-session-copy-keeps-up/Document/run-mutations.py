from pathlib import Path
import hashlib, os, subprocess, json, re
root=Path(__file__).resolve().parents[6]
assert (root/'Server').is_dir(), root
out=Path(__file__).resolve().parent
source=root/'Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs'
original=source.read_bytes()
backup=out/'move-mutation-backup.bin'
assert not backup.exists()
backup.write_bytes(original)
initial=hashlib.sha256(original).hexdigest()
line=b'runtimeSessions.ReplaceExact(session, state with { CharacterInventory = reloaded });'
mutations=[
 ('P1',line,b'runtimeSessions.ReplaceExact(session, state with { CharacterInventory = reloaded, CharacterEntry = state.CharacterEntry with { } });'),
 ('P2',line,b'// mutation: inventory refresh omitted'),
 ('P3',b'catch (Exception failure) when (!cancellationToken.IsCancellationRequested)',b'catch (Exception failure) when (cancellationToken.IsCancellationRequested)'),
 ('P4',b'if (result?.Status is ItemMoveStatus.Moved)',b'if (result?.Status is ItemMoveStatus.Moved && ChangesWhatTheCharacterLooksLike(message))'),
]
command=['dotnet','test','Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj','--nologo','--no-restore','--filter','FullyQualifiedName~TheCharacterChangesAsYouDressItTests','--logger','console;verbosity=detailed']
env=dict(os.environ,PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf')
results=[]
try:
 for name,old,new in mutations:
  assert original.count(old)==1,(name,original.count(old))
  source.write_bytes(original.replace(old,new,1))
  try:
   with (out/(name+'.txt')).open('wb') as log:
    result=subprocess.run(command,cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT)
  finally:
   source.write_bytes(backup.read_bytes())
   assert hashlib.sha256(source.read_bytes()).hexdigest()==initial
  body=(out/(name+'.txt')).read_bytes().decode('utf-8',errors='replace')
  failures=[s for s in body.splitlines() if '[FAIL]' in s]
  row=dict(mutation=name,edit_from=old.decode(),edit_to=new.decode(),exit=result.returncode,failures=failures,restored_sha256=initial)
  results.append(row)
  print(json.dumps(row,ensure_ascii=False),flush=True)
  assert result.returncode!=0 and failures and 'error CS' not in body, name
finally:
 source.write_bytes(backup.read_bytes())
 assert hashlib.sha256(source.read_bytes()).hexdigest()==initial
 backup.unlink()
 (out/'mutation-results.json').write_bytes(json.dumps(dict(command=command,source_sha256=initial,results=results),ensure_ascii=False,indent=2).encode())
with (out/'focused-final.txt').open('wb') as log:
 result=subprocess.run(command,cwd=root,env=env,stdout=log,stderr=subprocess.STDOUT)
print('RESTORED_FOCUSED_EXIT='+str(result.returncode),flush=True)
raise SystemExit(result.returncode)
