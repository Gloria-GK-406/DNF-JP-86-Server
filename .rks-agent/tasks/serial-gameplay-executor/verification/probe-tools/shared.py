"""Helpers (B wrap points that are not test methods) used by tests from more than one file."""
import json, sys, collections
o = json.load(open(sys.argv[1]))
tests = o['tests']
rows = []
for h, ts in o['helpers'].items():
    files = collections.Counter(tests[t]['path'] for t in ts if t in tests)
    rows.append((len(files), len(ts), h, files))
for nf, nt, h, files in sorted(rows, key=lambda r: (-r[0], -r[1])):
    if nf > 1 or '--all' in sys.argv:
        print(nf, nt, h, '|', ', '.join(f'{(k or "?").split("/")[-1]}:{v}' for k, v in files.most_common(6)))
