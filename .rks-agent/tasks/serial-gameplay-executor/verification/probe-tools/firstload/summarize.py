"""TASK-15 first-load probe summary.

usage: summarize.py <probe dir> [<probe dir> ...]
Reads <pid>.counts.tsv written by FirstLoadProbe (columns: hits, ms since process start, CMD type, job id,
innermost Infrastructure.Content frame, innermost Application frame, nth time that content frame was seen,
caller of the Archive getter). One row = one (job, caller) pair; hits = Archive reads by that caller in
that job. Groups by content type (outer type of the content frame) -> CMD type, and prints for each:
rows, distinct jobs, total hits, max hits of one row, the Application frames seen, and the first time.
"""
import collections, glob, os, re, sys

rows = []
for d in sys.argv[1:]:
    for path in sorted(glob.glob(os.path.join(d, '*.counts.tsv'))):
        for line in open(path):
            p = line.rstrip('\n').split('\t')
            if len(p) < 8:
                continue
            rows.append(dict(hits=int(p[0]), ms=int(p[1]), cmd=p[2], job=p[3], content=p[4], app=p[5],
                             nth=int(p[6]), caller=p[7], run=os.path.basename(d.rstrip('/'))))


def owner_type(frame):
    t = frame.split('::')[0]
    t = t.split('+')[0]
    return t.replace('Dfo.Server.Infrastructure.Content.', '')


def app_type(frame):
    t = frame.split('::')[0].split('+')[0]
    return t.replace('Dfo.Server.Application.', '')


by = collections.defaultdict(lambda: collections.defaultdict(list))
for r in rows:
    by[owner_type(r['content'])][r['cmd']].append(r)

total_rows = 0
total_hits = 0
print(f'rows {len(rows)}, content types {len(by)}')
for content in sorted(by, key=lambda c: -sum(len(v) for v in by[c].values())):
    cmds = by[content]
    n = sum(len(v) for v in cmds.values())
    h = sum(r['hits'] for v in cmds.values() for r in v)
    total_rows += n
    total_hits += h
    print(f'\n## {content}: rows {n}, hits {h}')
    for cmd in sorted(cmds, key=lambda c: -len(cmds[c])):
        v = cmds[cmd]
        jobs = len({(r['run'], r['job']) for r in v})
        apps = sorted({app_type(r['app']) for r in v})
        methods = sorted({r['content'].split('::')[-1] for r in v})
        per_job = collections.Counter()
        for r in v:
            per_job[(r['run'], r['job'])] += r['hits']
        print(f'  {cmd}: rows {len(v)}, jobs {jobs}, hits {sum(r["hits"] for r in v)}, '
              f'max hits in one job {max(per_job.values())}, first at {min(r["ms"] for r in v)} ms '
              f'(runs {",".join(sorted({r["run"] for r in v}))})')
        print(f'    app: {", ".join(apps)}')
        print(f'    content methods: {", ".join(methods)}')
print(f'\nsum of rows {total_rows} = {len(rows)}; sum of hits {total_hits}')
