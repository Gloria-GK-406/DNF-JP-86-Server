"""Group (B) records by where a wrap would go: the innermost frame of the outermost run of test frames.

usage: wrapat.py <rows.json> [hooks] [--tests]
"""
import collections, json, re, sys


def mo(frame):
    asm, rest = frame.split('!', 1)
    typ, meth = rest.rsplit('::', 1)
    m = re.search(r'\+<([^>]+)>', typ)
    base = re.sub(r'\+<.*$', '', typ)
    if m and meth == 'MoveNext':
        meth = m.group(1)
    elif m:
        meth = m.group(1) + '/' + meth
    return asm, base, meth


PLUMBING = ('!Dfo.Server.Application.Execution.', '!Dfo.Server.Abstractions.Execution.', '!Dfo.Server.Testing.TestGameplay')


def is_plumbing(frame):
    return any(p in frame for p in PLUMBING)


def wrap_at(frames):
    """Innermost frame of the outermost run of test frames, after dropping executor/helper plumbing
    at the outer end (a continuation resumed synchronously when a wrapped job completed)."""
    dfo = [f for f in frames if f.startswith('Dfo.Server')]
    chain = list(reversed(dfo))  # outer -> inner
    while chain and is_plumbing(chain[0]):
        chain.pop(0)
    last = None
    for f in chain:
        if f.split('!', 1)[0].endswith('.Tests') and not is_plumbing(f):
            last = f
        else:
            break
    return last


def test_of(frames, index):
    """The innermost frame that is a known test method ('Class::Method' in index); else the outermost
    non-plumbing test frame."""
    tests = [f for f in frames if f.split('!', 1)[0].endswith('.Tests') and not is_plumbing(f)]
    for f in tests:  # inner -> outer
        _, b, m = mo(f)
        key = b.split('.')[-1].split('+')[0] + '::' + m
        if key in index:
            return key
    if not tests:
        return None
    _, b, m = mo(tests[-1])
    return b.split('.')[-1].split('+')[0] + '::' + m


def key_of(r):
    w = wrap_at(r['frames'])
    if not w:
        return '?'
    _, b, m = mo(w)
    return b.split('.')[-1] + '::' + m


if __name__ == '__main__':
    rows = json.load(open(sys.argv[1]))
    by = collections.defaultdict(set)
    hooks = collections.defaultdict(collections.Counter)
    for r in rows:
        if r['class'] != 'B':
            continue
        k = key_of(r)
        by[k].add(r['test_outer_full'])
        hooks[k][r['hook']] += r['count']
    for k in sorted(by, key=lambda k: (-len(by[k]), k)):
        print(len(by[k]), k, dict(hooks[k]) if 'hooks' in sys.argv else '')
        if '--tests' in sys.argv:
            for t in sorted(by[k]):
                print('    ', t)
