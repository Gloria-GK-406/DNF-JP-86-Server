"""Write logs/task-07-probe-<Project>.txt from a classified round.

usage: PROBE_WORK=<dir> emit.py <round> <outdir> [projects...]   (writes <outdir>/$PROBE_LOG_PREFIX-<Project>.txt)
Every distinct (hook, stack) record appears once. (A) records are one line each (entry frames and test);
(B) and (C) records carry their full Dfo.Server stack, nearest frame first.
"""
import collections, json, os, sys

HERE = os.environ.get('PROBE_WORK') or sys.exit('set PROBE_WORK to a directory outside the repository')
PREFIX = os.environ.get('PROBE_LOG_PREFIX', 'probe')
rnd, outdir = sys.argv[1], sys.argv[2]
projects = sys.argv[3:] or ['Application', 'Persistence', 'Protocol', 'Host', 'Networking']
for p in projects:
    rows = json.load(open(os.path.join(HERE, rnd, p + '.json')))
    rows.sort(key=lambda r: ({'B': 0, 'C': 1, 'A': 2}[r['class']], r['hook'], -r['count']))
    fam = collections.Counter()
    hits = collections.Counter()
    for r in rows:
        fam[(r['family'], r['class'])] += 1
        hits[(r['family'], r['class'])] += r['count']
    with open(os.path.join(outdir, f'{PREFIX}-{p}.txt'), 'w') as out:
        out.write(f'# TASK-07 probe, project {p}, round {rnd}\n')
        out.write('# one record = one distinct (hook, stack); count = hits off the executor\n')
        out.write('# (A) entry-driven, (B) test-driven, (C) neither visible (truncated by an await resumption)\n')
        for f in sorted({r['family'] for r in rows}):
            out.write(f'# {f}: ' + ', '.join(f'{c} records {fam[(f, c)]} hits {hits[(f, c)]}' for c in 'ABC') + '\n')
        out.write('\n')
        for r in rows:
            head = f"[{r['class']}] {r['hook']} x{r['count']}"
            if r['class'] == 'A':
                out.write(f"{head} | entry {r['entry_outer']} (nearest {r['entry']}) | test {r['test_outer_full']} | top {r['prod_near']}\n")
            else:
                out.write(f"{head} | test {r['test_outer_full']} | outer {r['prod_outer']}\n")
                for fr in r['frames']:
                    if fr.startswith('Dfo.Server') or fr.startswith('TouchSocket') or fr.startswith('Microsoft.Extensions'):
                        out.write('    ' + fr + '\n')
    print(p, len(rows), 'records')
