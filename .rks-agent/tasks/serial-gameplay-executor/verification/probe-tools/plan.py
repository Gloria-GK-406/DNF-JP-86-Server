"""From classified probe rows, per test method: (B) hooks, wrap points, (A) entries; plus helper wrap points.

usage: plan.py <rows.json> <project-dir-name> [--helpers] [--tests] [--json out]
The test a record belongs to is the innermost frame that is a known test method (see wrapat.test_of).
"""
import collections, json, os, re, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from wrapat import mo, wrap_at, test_of  # noqa: E402

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), *['..'] * 5)) + '/' + 'Server/tests/'
rows = json.load(open(sys.argv[1]))
proj = sys.argv[2]

ATTR = re.compile(r'^\s*\[(?:[\w.]+\.)?(Fact|Theory|SkippableFact|SkippableTheory|PvfFact|PvfTheory)\b')
DECL = re.compile(r'^\s*public\s+(?:async\s+)?(?:Task|void|ValueTask)\s+(\w+)\s*\(')
CLS = re.compile(r'\bclass\s+(\w+)')

by_name = collections.defaultdict(set)  # method -> files
cls_files = collections.defaultdict(set)
for d, _, fs in os.walk(ROOT + proj):
    if '/obj' in d or '/bin' in d:
        continue
    for f in fs:
        if not f.endswith('.cs'):
            continue
        p = os.path.join(d, f)
        rel = os.path.relpath(p, ROOT)
        lines = open(p, encoding='utf-8').read().split('\n')
        for i, l in enumerate(lines):
            for m in CLS.finditer(l):
                cls_files[m.group(1)].add(rel)
            if ATTR.search(l):
                for j in range(i + 1, min(i + 80, len(lines))):
                    m2 = DECL.search(lines[j])
                    if m2:
                        by_name[m2.group(1)].add(rel)
                        break


class Index:
    def __contains__(self, key):
        return key.split('::')[-1] in by_name


INDEX = Index()


def path_of(key):
    cls, meth = key.split('::')
    files = by_name.get(meth, set())
    if len(files) == 1:
        return next(iter(files))
    both = files & cls_files.get(cls, set())
    if len(both) == 1:
        return next(iter(both))
    cf = cls_files.get(cls, set())
    return next(iter(cf)) if len(cf) == 1 else None


helpers = collections.defaultdict(set)
b_tests = collections.defaultdict(collections.Counter)
a_tests = collections.defaultdict(collections.Counter)
wraps = collections.defaultdict(collections.Counter)
for r in rows:
    t = test_of(r['frames'], INDEX)
    if t is None:
        continue
    if r['class'] == 'A':
        a_tests[t][r['entry_outer']] += r['count']
    elif r['class'] == 'B':
        w = wrap_at(r['frames'])
        if w is None:
            key = 'TRUNC:' + str(r['prod_outer'])
        else:
            _, b, m = mo(w)
            key = b.split('.')[-1] + '::' + m
        b_tests[t][r['hook']] += r['count']
        wraps[t][key] += r['count']
        if key.split('+')[0] != t:
            helpers[key].add(t)

out = {'helpers': {k: sorted(v) for k, v in helpers.items()}, 'tests': {}}
for t in sorted(b_tests):
    out['tests'][t] = {'path': path_of(t), 'B': dict(b_tests[t]), 'A': dict(a_tests.get(t, {})),
                       'wrap': dict(wraps[t])}
if '--json' in sys.argv:
    json.dump(out, open(sys.argv[sys.argv.index('--json') + 1], 'w'), indent=1, ensure_ascii=False)
if '--helpers' in sys.argv:
    for k in sorted(helpers, key=lambda k: (-len(helpers[k]), k)):
        print(len(helpers[k]), k)
if '--tests' in sys.argv:
    for t, v in out['tests'].items():
        print(t, v['path'], 'A' if v['A'] else '-', ','.join(sorted(v['A'])))
print('B tests', len(out['tests']), 'with A', sum(1 for v in out['tests'].values() if v['A']),
      'unresolved path', sum(1 for v in out['tests'].values() if not v['path']), 'helpers', len(helpers))
