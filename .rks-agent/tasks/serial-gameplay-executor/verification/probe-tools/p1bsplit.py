"""Relabel P1b records by the method that constructed the DomainTransaction (from the nearest frame).

usage: p1bsplit.py <rows.json> [--write]
  ReadAsync  -> P1b-read   (a read outside a use case; rolled back, no write unit)
  ChangeAsync -> P1b-volatile (a write to a non-durable store outside a use case)
  AloneAsync -> P1b-teardown (CharacterStateAccess: the teardown position write to SQLite; TASK-03)
  CreatureRuntimeDomainService -> P1b-creature (CreatureRuntimeDomainService.cs:274 fallback; TASK-03)
"""
import collections, json, re, sys

rows = json.load(open(sys.argv[1]))
c = collections.Counter()
for r in rows:
    if not r['hook'].startswith('P1b'):
        continue
    near = r['frames'][0]
    m = re.search(r'<(\w+)>d__', near)
    meth = m.group(1) if m else near.rsplit('::', 1)[-1]
    if 'CreatureRuntimeDomainService' in near:
        label = 'P1b-creature'
    elif meth == 'ReadAsync':
        label = 'P1b-read'
    elif meth == 'ChangeAsync':
        label = 'P1b-volatile'
    elif meth == 'AloneAsync':
        label = 'P1b-teardown'
    else:
        label = 'P1b-other:' + meth
    r['hook'] = label + r['hook'][3:]
    r['family'] = label
    c[(label, r['class'])] += 1
for k in sorted(c):
    print(k, c[k])
if '--write' in sys.argv:
    json.dump(rows, open(sys.argv[1], 'w'), indent=0)
