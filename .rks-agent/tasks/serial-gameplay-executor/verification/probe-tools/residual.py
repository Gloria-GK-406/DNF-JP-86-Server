"""TASK-12: classify every probe record that still has a DomainEventDispatcher frame on its stack.

usage: residual.py <round-dir> [baseline-round-dir]
For each record: the frames between the hook and the first DomainEventDispatcher frame. If a test-assembly frame
sits in between and the dispatcher frame is reached only through a completed-task continuation
(SetExistingTaskResult / TrySetResult right below the test frame chain), the access was made by the test body
resuming inline after DrainOnceAsync returned; the record is labelled by the nearest Dfo entry frame above the test
frame (CommandBus.SendAsync -> 3.10) or 'test-direct' when there is none.
"""
import collections, json, os, re, sys

d = sys.argv[1]
base = sys.argv[2] if len(sys.argv) > 2 else None


def load(dirpath):
    rows = []
    for p in ('Application', 'Protocol', 'Host', 'Networking'):
        path = os.path.join(dirpath, p + '.json')
        if os.path.exists(path):
            for r in json.load(open(path)):
                r['project'] = p
                rows.append(r)
    return rows


def judge(r):
    frames = r['frames']
    k = next(i for i, f in enumerate(frames) if 'DomainEventDispatcher' in f)
    above = frames[:k]
    test_idx = [i for i, f in enumerate(above) if f.split('!')[0].endswith('.Tests')]
    if not test_idx:
        return 'dispatcher-originated', None
    t = test_idx[0]
    between = above[t + 1:k]
    continuation = any('SetExistingTaskResult' in f or 'TrySetResult' in f or 'SetResult' in f for f in between)
    nearest = next((f for f in above[:t] if 'CommandBus::SendAsync' in f), None)
    label = '3.10 CommandBus.SendAsync (test calls the bus)' if nearest else 'test-direct (no entry frame above the test frame)'
    return ('test-continuation' if continuation else 'test-frame-no-continuation'), label


rows = load(d)
ev = [r for r in rows if any('DomainEventDispatcher' in f for f in r['frames'])]
print('records with a DomainEventDispatcher frame:', len(ev), 'hits', sum(r['count'] for r in ev), dict(collections.Counter(r['class'] for r in ev)))
c = collections.Counter()
for r in ev:
    kind, label = judge(r)
    c[(kind, label, r['hook'].split(':')[0], r['project'], r['test_outer_full'])] += r['count']
for k, v in sorted(c.items(), key=lambda x: -x[1]):
    print(' ', v, *k)
kinds = collections.Counter()
for r in ev:
    kinds[judge(r)[0]] += 1
print('by kind (records):', dict(kinds))
if base:
    brow = load(base)
    tests = {r['test_outer_full'] for r in ev}
    same = [r for r in brow if r['test_outer_full'] in tests and any('DomainEventDispatcher' in f for f in r['frames'])]
    print('baseline records of the same tests with a dispatcher frame:', len(same), 'hits', sum(r['count'] for r in same))
    bk = collections.Counter(judge(r)[0] for r in same)
    print('  baseline by kind:', dict(bk))
