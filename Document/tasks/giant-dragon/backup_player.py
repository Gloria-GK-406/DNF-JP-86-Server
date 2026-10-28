from pathlib import Path
import sqlite3,json,sys,datetime,hashlib
source=Path(sys.argv[1]).resolve();target=Path(sys.argv[2]).resolve()
if source==target or target.exists(): raise RuntimeError('Refuse overwriting backup')
with sqlite3.connect(source.as_uri()+'?mode=ro',uri=True,timeout=30) as src,sqlite3.connect(target) as dst: src.backup(dst,pages=256,sleep=0.05)
with sqlite3.connect(target.as_uri()+'?mode=ro',uri=True) as c:
    integrity=c.execute('PRAGMA integrity_check').fetchall()
    tables=[r[0] for r in c.execute("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name")]
    counts={t:c.execute('SELECT COUNT(*) FROM "'+t.replace('"','""')+'"').fetchone()[0] for t in tables}
    result=dict(Source=str(source),Backup=str(target),Integrity=integrity,TableCounts=counts,At=datetime.datetime.now().astimezone().isoformat(),BackupSha256=hashlib.file_digest(target.open('rb'),'sha256').hexdigest())
    target.with_suffix('.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
    print(json.dumps(dict(Backup=str(target),Integrity=integrity,Tables=len(tables),Accounts=counts.get('accounts'),Characters=counts.get('characters'),Items=counts.get('items'),ItemSlots=counts.get('item_slots')),indent=2))
    if integrity != [('ok',)]: raise RuntimeError('Backup integrity failed')
