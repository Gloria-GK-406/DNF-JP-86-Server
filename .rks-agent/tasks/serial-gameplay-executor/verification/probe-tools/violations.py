"""Summarise the gameplay-assertion evidence written by tests/Support/GameplayAssertionRecorder.cs.

usage: python3 violations.py <DFO_GAMEPLAY_ASSERTIONS directory> [--assemblies A,B,...] [expected...]
  expected: Assembly:Class=count, or Class=count for the class in any assembly, e.g.
            Dfo.Server.Application.Tests:GameplayWriteAssertionTests=24
  --assemblies: every listed assembly must have its <assembly>.jsonl file and exactly one record of the
                positive control GameplayAssertionRecorderControlTests (TASK-18 section 5.1).

Each record is grouped by its outermost test class: the outermost `Dfo.` frame that has a type segment
ending in "Tests" (the bare namespace segment "Tests" does not count). Records with no such frame are
listed on their own: by the stop conditions they mean a production-path violation. Kinds starting with
"exe:" come from a packaged host's output (Host.Tests fixtures) and always count as outside.
Output is split by kind. Exit status 1 when anything outside the expected classes and the control was
recorded, a count differs, or a listed assembly lacks its file or its single control record.
"""
import glob
import json
import os
import sys
from collections import Counter, defaultdict

CONTROL = 'GameplayAssertionRecorderControlTests'


def test_class(frames):
    for frame in reversed(frames):
        name = frame.split('(')[0]
        parts = name.split('.')
        for index in range(3, len(parts)):
            segment = parts[index].split('+')[0]
            if segment.endswith('Tests') and segment != 'Tests':
                return segment
    return None


def parse_args(argv):
    directory = argv[0]
    assemblies = []
    expected = {}
    rest = argv[1:]
    i = 0
    while i < len(rest):
        arg = rest[i]
        if arg == '--assemblies':
            assemblies = [value for value in rest[i + 1].split(',') if value]
            i += 2
            continue
        key, value = arg.split('=')
        assembly, _, owner = key.rpartition(':')
        expected[(assembly or None, owner)] = int(value)
        i += 1
    return directory, assemblies, expected


def main():
    directory, assemblies, expected = parse_args(sys.argv[1:])
    groups = defaultdict(Counter)
    orphans = []
    exe = []
    files = sorted(glob.glob(os.path.join(directory, '*.jsonl')))
    kinds_total = Counter()
    total = 0
    for path in files:
        assembly = os.path.basename(path)[:-len('.jsonl')]
        with open(path, encoding='utf-8') as handle:
            for line in handle:
                line = line.strip()
                if not line:
                    continue
                record = json.loads(line)
                total += 1
                kinds_total[record['kind']] += 1
                if record['kind'].startswith('exe:'):
                    exe.append((assembly, record))
                    continue
                owner = test_class(record['frames'])
                if owner is None:
                    orphans.append((assembly, record))
                else:
                    groups[(assembly, owner)][record['kind']] += 1
    print(f'directory: {directory}')
    print(f'files: {len(files)} ({", ".join(os.path.basename(p) for p in files) or "none"})')
    print(f'records: {total}')
    print('by kind: ' + ', '.join(f'{kind} {count}' for kind, count in sorted(kinds_total.items())))
    failed = False

    def is_expected(assembly, owner):
        return (assembly, owner) in expected or (None, owner) in expected

    outside = 0
    controls = Counter()
    for (assembly, owner), kinds in sorted(groups.items()):
        count = sum(kinds.values())
        if owner == CONTROL:
            mark = 'control'
            controls[assembly] += count
        elif is_expected(assembly, owner):
            mark = 'counterexample'
        else:
            mark = 'OUTSIDE'
            outside += count
            failed = True
        print(f'  {assembly:40s} {owner:45s} {count:4d}  {dict(sorted(kinds.items()))}  [{mark}]')
    for (assembly, owner), count in sorted(expected.items(), key=lambda item: (item[0][0] or '', item[0][1])):
        recorded = sum(sum(kinds.values()) for (a, o), kinds in groups.items()
                       if o == owner and (assembly is None or a == assembly))
        status = 'ok' if recorded == count else 'MISMATCH'
        if recorded != count:
            failed = True
        print(f'expected {(assembly + ":") if assembly else ""}{owner}: {count}, recorded {recorded}  [{status}]')
    for assembly in assemblies:
        present = os.path.exists(os.path.join(directory, assembly + '.jsonl'))
        status = 'ok' if present and controls[assembly] == 1 else 'MISSING' if not present else 'MISMATCH'
        if status != 'ok':
            failed = True
        print(f'assembly {assembly}: file {"present" if present else "absent"}, control records {controls[assembly]}  [{status}]')
    print(f'records outside the counterexample classes and the control: {outside}')
    print(f'records from packaged hosts (exe:): {len(exe)}')
    for assembly, record in exe:
        failed = True
        print(f'  {assembly}: {record["kind"]} {record["operation"]}')
        for frame in record['frames']:
            print(f'      {frame}')
    print(f'records without a test frame: {len(orphans)}')
    for assembly, record in orphans:
        failed = True
        print(f'  {assembly}: {record["kind"]} {record["operation"]}')
        for frame in record['frames']:
            print(f'      {frame}')
    sys.exit(1 if failed else 0)


if __name__ == '__main__':
    main()
