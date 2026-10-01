import argparse, hashlib, json, sqlite3, struct
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument("database");p.add_argument("output");p.add_argument("--backup");a=p.parse_args()
with sqlite3.connect(Path(a.database).as_uri()+"?mode=ro", uri=True) as c:
    check=c.execute("pragma quick_check").fetchone()[0]
    if check!="ok": raise RuntimeError(check)
    if a.backup:
        if Path(a.backup).exists(): raise RuntimeError("Backup already exists")
        with sqlite3.connect(a.backup) as dst: c.backup(dst)
    tables={}
    for (name,) in c.execute("select name from sqlite_master where type='table' order by name"):
        rows=c.execute('select * from "'+name.replace('"','""')+'"').fetchall()
        digest=hashlib.sha256("\n".join(sorted(repr(r) for r in rows)).encode()).hexdigest()
        tables[name]={"rows":len(rows),"sha256":digest}
    coupons={}
    for raw,count in c.execute("select i.item_core,s.count from item_slots s join items i on s.core_uid=i.item_uid where s.owner_scope='character' and s.owner_id=2"):
        item=struct.unpack_from('<i',raw,1)[0]
        if item in (7275,7814,10008100,10008107,7804,10007840): coupons[str(item)]=coupons.get(str(item),0)+count
Path(a.output).write_text(json.dumps({"quickCheck":check,"tables":tables,"character2Coupons":coupons},indent=2),encoding='utf-8')
print("quick_check=ok; tables="+str(len(tables))+"; coupons="+json.dumps(coupons))
