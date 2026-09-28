"""Extract complete diagnostic bodies; unknown fields keep neutral names.
Usage: python extract_capture.py SERVER_LOG OUTPUT_JSONL
Phase labels assume one test character and first observed use of item 7377.
"""
import json
import re
import struct
import sys
from pathlib import Path

pattern = re.compile(r'(DIE_MONSTER|USE_STACKABLE) capture metadata=(.*?) body\((\d+)B\)=([0-9A-Fa-f]+)')
records = []
stamp = None
phase = 'before-potion'
for line in Path(sys.argv[1]).read_text(encoding='utf-8', errors='replace').splitlines():
    if re.match(r'\d{4}-\d{2}-\d{2} ', line):
        stamp = line[:23]
    match = pattern.search(line)
    if not match:
        continue
    kind, metadata, length, raw = match.groups()
    body = bytes.fromhex(raw)
    if len(body) != int(length):
        raise ValueError('Capture length mismatch')
    row = dict(time=stamp, kind=kind, phase=phase, metadata=metadata, length=len(body), bodyHex=raw)
    if kind == 'USE_STACKABLE' and len(body) >= 11:
        row.update(slot=struct.unpack_from('<h', body)[0], space=body[2],
                   instanceValue=struct.unpack_from('<i', body, 3)[0],
                   itemCode=struct.unpack_from('<i', body, 7)[0])
        if row['itemCode'] == 7377:
            phase = 'after-potion-request'
    elif kind == 'DIE_MONSTER' and len(body) >= 23:
        n = body[22]
        tail = 23 + 10 * n
        row.update(actorOrObjectCode=struct.unpack_from('<I', body)[0],
                   killer=struct.unpack_from('<H', body, 4)[0],
                   unknownU32At6=list(struct.unpack_from('<4I', body, 6)), count=n)
        if len(body) >= tail + 39:
            row.update(records=[list(struct.unpack_from('<HHIH', body, 23 + i * 10)) for i in range(n)],
                       suffixOffset=tail, suffixHex=body[tail:].hex(), passiveObjectFlag=body[tail+6],
                       randomCandidate=struct.unpack_from('<H', body, tail+8)[0])
        if len(body) == 66 + 10 * n:
            row['trailingChecksumHex'] = body[-4:].hex()
    records.append(row)
Path(sys.argv[2]).write_text(''.join(json.dumps(row, ensure_ascii=False) + '\n' for row in records), encoding='utf-8')
print(json.dumps({'records': len(records), 'killsBefore': sum(r['kind']=='DIE_MONSTER' and r['phase']=='before-potion' for r in records), 'killsAfter': sum(r['kind']=='DIE_MONSTER' and r['phase']=='after-potion-request' for r in records)}, ensure_ascii=False))
