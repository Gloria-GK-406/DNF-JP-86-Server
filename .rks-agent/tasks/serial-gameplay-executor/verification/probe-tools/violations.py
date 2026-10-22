"""Summarise the gate-assertion evidence written by tests/Support/GameplayAssertionRecorder.cs.

usage: python3 violations.py <DFO_GAMEPLAY_ASSERTIONS directory> [expected...]
  expected: Class=count pairs for the counterexample classes, e.g. GameplayGateAssertionTests=21

Each record is grouped by its outermost test class: the outermost `Dfo.` frame that has a type
segment ending in "Tests" after the project namespace (`Dfo.Server.<Project>.Tests`). Records with no
such frame are listed on their own: by TASK-17 stop condition 1 they mean a production-path violation.
Exit status 1 when anything outside the expected classes was recorded, or a count differs.
"""
import glob
import json
import os
import sys
from collections import Counter, defaultdict


def test_class(frames):
    for frame in reversed(frames):
        name = frame.split('(')[0]
        parts = name.split('.')
        for index in range(4, len(parts)):
            segment = parts[index].split('+')[0]
            if segment.endswith('Tests'):
                return segment
    return None


def main():
    directory = sys.argv[1]
    expected = dict(arg.split('=') for arg in sys.argv[2:])
    expected = {key: int(value) for key, value in expected.items()}
    groups = defaultdict(Counter)
    orphans = []
    files = sorted(glob.glob(os.path.join(directory, '*.jsonl')))
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
                owner = test_class(record['frames'])
                if owner is None:
                    orphans.append((assembly, record))
                else:
                    groups[(assembly, owner)][record['kind']] += 1
    print(f'directory: {directory}')
    print(f'files: {len(files)} ({", ".join(os.path.basename(p) for p in files) or "none"})')
    print(f'records: {total}')
    failed = False
    outside = 0
    for (assembly, owner), kinds in sorted(groups.items()):
        count = sum(kinds.values())
        mark = 'counterexample' if owner in expected else 'OUTSIDE'
        if owner not in expected:
            outside += count
            failed = True
        print(f'  {assembly:40s} {owner:45s} {count:4d}  {dict(kinds)}  [{mark}]')
    seen = Counter()
    for (assembly, owner), kinds in groups.items():
        seen[owner] += sum(kinds.values())
    for owner, count in sorted(expected.items()):
        status = 'ok' if seen[owner] == count else 'MISMATCH'
        if seen[owner] != count:
            failed = True
        print(f'expected {owner}: {count}, recorded {seen[owner]}  [{status}]')
    print(f'records outside the counterexample classes: {outside}')
    print(f'records without a test frame: {len(orphans)}')
    for assembly, record in orphans:
        failed = True
        print(f'  {assembly}: {record["kind"]} {record["operation"]}')
        for frame in record['frames']:
            print(f'      {frame}')
    sys.exit(1 if failed else 0)


if __name__ == '__main__':
    main()
