"""Classify GameplayProbe dumps into (A) entry-driven, (B) test-driven, (C) truncated.

usage: classify.py <dump-dir> [--json out.json] [--detail]
A record's frames run from the hook outwards.
(A) some frame on the stack is a production entry frame;
(B) no entry frame, and some frame belongs to a test assembly;
(C) neither (the visible stack starts at an async resumption inside production code).
"""
import collections, glob, json, os, re, sys

ENTRY_TYPE = re.compile(
    r'(?:^|\.)(?:CommandBus|\w*Behavior|NetworkMessageDispatcher|DomainEventDispatcher|OnlineUserConnectionObserver'
    r'|LoginSession\w*Runtime\w*|LoginSession\w*Maintenance\w*|ScheduledTask\w*|\w*NetworkPlugin\w*|TouchSocket\w*'
    r'|\w+HostedService|\w+MaintenanceService|\w+ClockService|\w+SweepService|\w+StartupService|\w+BootstrapService|\w+DrainService'
    # TASK-07 extension: the remaining background-service ticks and scheduled jobs (section 2.4 entries).
    r'|\w+RefreshService|\w+Scheduler|\w+(?<!Gameplay)Job|DungeonPartyWipeService|WeaknessRecoveryService|MarriageRelationAudit|EffectOperationLedger)$')
ENTRY_PIPELINE = re.compile(r'\.Commands\.Pipeline\.')
HTTP_ASM = {'Dfo.Server.GmWeb', 'Dfo.Server.HttpApi', 'Dfo.Server.LauncherApi'}


def parse_file(path):
    out = {}
    text = open(path, encoding='utf-8').read()
    for block in text.split('\n\n'):
        block = block.strip('\n')
        if not block.startswith('=== '):
            continue
        head, *frames = block.split('\n')
        _, count, hook = head.split(' ', 2)
        key = (hook, tuple(frames))
        out[key] = out.get(key, 0) + int(count)
    return out


def parse(dirpath):
    """<pid>.txt holds counted snapshots; <pid>.new.txt holds every record the moment it first appeared (count 1),
    so a record survives even when the process ended without a final snapshot."""
    recs = collections.Counter()
    pids = {os.path.basename(p).split('.')[0] for p in glob.glob(os.path.join(dirpath, '*.txt'))}
    for pid in pids:
        snap = os.path.join(dirpath, pid + '.txt')
        new = os.path.join(dirpath, pid + '.new.txt')
        merged = parse_file(new) if os.path.exists(new) else {}
        merged = {k: 1 for k in merged}
        if os.path.exists(snap):
            merged.update(parse_file(snap))
        for k, v in merged.items():
            recs[k] += v
    return recs


def outer_type(full):
    # Ns.Type+<Method>d__3 -> Ns.Type ; Ns.Type+Nested -> Ns.Type+Nested
    t = re.sub(r'\+<[^>]*>[\w`]*$', '', full)
    t = re.sub(r'\+<>c(?:__DisplayClass[\w_]*)?$', '', t)
    return t


def method_of(frame):
    asm, rest = frame.split('!', 1)
    typ, meth = rest.rsplit('::', 1)
    m = re.search(r'\+<([^>]+)>', typ)
    if meth == 'MoveNext' and m:
        meth = m.group(1)
    elif m and re.match(r'<([^>]+)>b__', meth or ''):
        meth = m.group(1) + '/' + meth
    return asm, outer_type(typ), meth


def is_entry(frame):
    asm, typ, meth = method_of(frame)
    if asm in HTTP_ASM or asm.startswith('Microsoft.AspNetCore'):
        return True
    # TASK-07 extension: container disposal and host start/stop are the production shutdown/startup
    # path (T-004b 3i/3j), not a test body touching gameplay state.
    if asm == 'Microsoft.Extensions.DependencyInjection' and meth in ('Dispose', 'DisposeAsync'):
        return True
    if asm == 'Microsoft.Extensions.Hosting' and typ.startswith('Microsoft.Extensions.Hosting.Internal.Host'):
        return True
    # TASK-07 extension: the card presentation delay timers (appendix A, DungeonCardPresentationCoordinator
    # layout and auto-flip) run as their own tasks; a test double may fire them, but the flow is the timer's.
    if typ.endswith('.DungeonCardPresentationCoordinator') and meth in ('RunLayoutAsync', 'RunAutoFlipAsync'):
        return True
    if not asm.startswith('Dfo.Server') and not asm.startswith('TouchSocket'):
        return False
    if asm.startswith('TouchSocket'):
        return True
    base = re.sub(r'`\d+', '', typ.split('+')[0])
    return bool(ENTRY_TYPE.search(base))


def is_test(frame):
    asm = frame.split('!', 1)[0]
    return asm.endswith('.Tests')


def classify(frames):
    entry = next((i for i, f in enumerate(frames) if is_entry(f)), None)
    test = next((i for i, f in enumerate(frames) if is_test(f)), None)
    if entry is not None:
        return 'A', entry, test
    if test is not None:
        return 'B', entry, test
    return 'C', entry, test


def short(frame):
    asm, typ, meth = method_of(frame)
    return f'{typ.split(".")[-1]}.{meth}'


def main():
    d = sys.argv[1]
    recs = parse(d)
    rows = []
    for (hook, frames), count in recs.items():
        cls, e, t = classify(frames)
        dfo = [f for f in frames if f.startswith('Dfo.Server') and not is_test(f)]
        tests = [f for f in frames if is_test(f)]
        rows.append({
            'hook': hook, 'family': hook.split(':')[0], 'count': count, 'class': cls,
            'entry': short(frames[e]) if e is not None else None,
            'entry_outer': short([f for f in frames if is_entry(f)][-1]) if e is not None else None,
            'test_near': short(tests[0]) if tests else None,
            'test_outer': short(tests[-1]) if tests else None,
            'test_outer_full': method_of(tests[-1])[1] + '::' + method_of(tests[-1])[2] if tests else None,
            'prod_near': short(dfo[0]) if dfo else None,
            'prod_outer': short(dfo[-1]) if dfo else None,
            'frames': list(frames),
        })
    fam = collections.Counter((r['family'], r['class']) for r in rows)
    print('records (distinct hook+stack):', len(rows), ' hits:', sum(r['count'] for r in rows))
    for f in ('P1', 'P1b', 'P2', 'P3'):
        print(f, {c: fam[(f, c)] for c in 'ABC'})
    if '--json' in sys.argv:
        json.dump(rows, open(sys.argv[sys.argv.index('--json') + 1], 'w'), ensure_ascii=False, indent=0)
    if '--detail' in sys.argv:
        print('\n## B by test (outer test frame)')
        by = collections.defaultdict(collections.Counter)
        for r in rows:
            if r['class'] == 'B':
                by[r['test_outer_full']][r['hook']] += r['count']
        for k in sorted(by):
            print(k, dict(by[k]))
        print('\n## C by outermost production frame')
        byc = collections.defaultdict(collections.Counter)
        for r in rows:
            if r['class'] == 'C':
                byc[r['prod_outer']][r['hook']] += r['count']
        for k in sorted(byc, key=str):
            print(k, dict(byc[k]))
        print('\n## A by outermost entry frame')
        bya = collections.defaultdict(collections.Counter)
        for r in rows:
            if r['class'] == 'A':
                bya[r['entry_outer']][r['hook']] += r['count']
        for k in sorted(bya, key=str):
            print(k, dict(bya[k]))


main()
