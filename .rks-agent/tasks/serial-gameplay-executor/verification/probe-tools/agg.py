"""Aggregate classified rows of several projects.

usage: PROBE_WORK=<dir> agg.py <round> <section> [projects...]   (reads $PROBE_WORK/<round>/<Project>.json)
sections:
  counts   per family and project: A/B/C distinct records and hits
  a        (A) grouped by entry (outermost entry frame) -> hooks, distinct tests, sample tests
  c        (C) grouped by outermost production frame, with nearest production frame
  find <s> any record whose frames contain s, by class/hook/test
"""
import collections, json, os, re, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from wrapat import mo  # noqa: E402

HERE = os.environ.get('PROBE_WORK') or sys.exit('set PROBE_WORK to a directory outside the repository')
rnd = sys.argv[1]
sec = sys.argv[2]
rest = sys.argv[3:]
projects = [p for p in (rest[1:] if sec == 'find' else rest) if p[0].isupper()] or ['Application', 'Persistence', 'Protocol', 'Host', 'Networking']


def short_test(full):
    if not full:
        return None
    t, m = full.split('::')
    return t.split('.')[-1] + '::' + m


rows = []
for p in projects:
    path = os.path.join(HERE, rnd, p + '.json')
    if os.path.exists(path):
        for r in json.load(open(path)):
            r['project'] = p
            rows.append(r)

if sec == 'counts':
    c = collections.defaultdict(lambda: [0, 0])
    for r in rows:
        k = (r['project'], r['family'], r['class'])
        c[k][0] += 1
        c[k][1] += r['count']
    print('| 项目 | 族 | A 记录 | A 次数 | B 记录 | B 次数 | C 记录 | C 次数 |')
    print('|---|---|---:|---:|---:|---:|---:|---:|')
    tot = collections.Counter()
    for p in projects:
        for f in ('P1', 'P1b-read', 'P1b-teardown', 'P1b-volatile', 'P1b-creature', 'P2', 'P3'):
            vals = [c[(p, f, x)] for x in 'ABC']
            if not any(v[0] for v in vals):
                continue
            print(f'| {p} | {f} | ' + ' | '.join(f'{v[0]} | {v[1]}' for v in vals) + ' |')
            for x, v in zip('ABC', vals):
                tot[(f, x, 'r')] += v[0]
                tot[(f, x, 'h')] += v[1]
    for f in ('P1', 'P1b', 'P2', 'P3'):
        print(f'| 合计 | {f} | ' + ' | '.join(f"{tot[(f, x, 'r')]} | {tot[(f, x, 'h')]}" for x in 'ABC') + ' |')
elif sec == 'a':
    by = collections.defaultdict(lambda: {'hooks': collections.Counter(), 'tests': set(), 'projects': set(), 'inner': collections.Counter()})
    for r in rows:
        if r['class'] != 'A':
            continue
        e = by[r['entry_outer']]
        e['hooks'][r['hook']] += r['count']
        e['projects'].add(r['project'])
        e['inner'][r['entry']] += r['count']
        t = short_test(r['test_outer_full'])
        if t:
            e['tests'].add(t)
    for k in sorted(by, key=lambda k: (-sum(by[k]['hooks'].values()), str(k))):
        e = by[k]
        print(f'### {k}  (projects: {",".join(sorted(e["projects"]))}; tests: {len(e["tests"])})')
        print('  hooks:', ', '.join(f'{h} x{n}' for h, n in e['hooks'].most_common()))
        if len(e['inner']) > 1 or list(e['inner'])[0] != k:
            print('  nearest entry frames:', ', '.join(f'{h} x{n}' for h, n in e['inner'].most_common(6)))
        print('  sample tests:', '; '.join(sorted(e['tests'])[:4]))
elif sec == 'c':
    by = collections.defaultdict(lambda: {'hooks': collections.Counter(), 'near': collections.Counter(), 'projects': set()})
    for r in rows:
        if r['class'] != 'C':
            continue
        e = by[r['prod_outer']]
        e['hooks'][r['hook']] += r['count']
        e['near'][r['prod_near']] += r['count']
        e['projects'].add(r['project'])
    for k in sorted(by, key=str):
        e = by[k]
        print(f'### {k}  ({",".join(sorted(e["projects"]))})')
        print('  hooks:', ', '.join(f'{h} x{n}' for h, n in e['hooks'].most_common()))
        print('  nearest production frames:', ', '.join(f'{h} x{n}' for h, n in e['near'].most_common(5)))
elif sec == 'find':
    needle = rest[0]
    agg = collections.Counter()
    for r in rows:
        if needle in '\n'.join(r['frames']):
            agg[(r['project'], r['class'], r['hook'], r['entry_outer'], short_test(r['test_outer_full']))] += r['count']
    for k, n in agg.most_common():
        print(n, *k)
