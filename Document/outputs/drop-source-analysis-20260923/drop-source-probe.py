"""Read-only PVF structure inventory; writes only a JSON evidence file beside this script.

Requires the existing Tool/PvfProbe Debug build and PVF_ARCHIVE_PATH (or --archive).
Field positions are inventoried, not proof of their original server semantics.
"""
import argparse
from collections import Counter
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--archive', default=os.environ.get('PVF_ARCHIVE_PATH'))
    args = parser.parse_args()
    if not args.archive:
        parser.error('set PVF_ARCHIVE_PATH or pass --archive')
    archive = Path(args.archive).resolve(strict=True)
    root = Path(__file__).resolve().parents[3]
    probe = root / 'Tool/PvfProbe/bin/Debug/net10.0/PvfProbe.dll'
    env = dict(os.environ, PVF_ARCHIVE_PATH=str(archive))

    def run(*arguments):
        return subprocess.run(['dotnet', str(probe), *arguments], cwd=root, env=env,
                              capture_output=True, text=True, encoding='utf-8', check=True).stdout

    def dump(path):
        text = run('dump', path, '2000000')
        if '!! not found' in text or '-- stopped at' in text:
            raise ValueError(f'incomplete dump: {path}')
        return text

    text = dump('etc/independent_drop.etc')
    body = text.split('[independent drop]', 1)[1].split('[/independent drop]', 1)[0]
    tokens = re.findall(r'\[[^\]]+\]|-?\d+', body)
    rows = []
    i = 0
    while i < len(tokens):
        row = list(map(int, tokens[i:i + 17]))
        if len(row) != 17:
            raise ValueError(f'incomplete independent row at token {i}')
        i += 17
        pool = []
        if i < len(tokens) and tokens[i] == '[list]':
            end = tokens.index('[/list]', i + 1)
            pool = list(map(int, tokens[i + 1:end]))
            i = end + 1
        if row[16] not in (0, 1) or (row[16] == 0 and pool) or (row[16] == 1 and len(pool) % 2):
            raise ValueError(f'unexpected independent structure: {row}')
        rows.append((row, pool))

    world = dump('etc/worlddrop.etc')
    body = world.split('[world drop]', 1)[1].split('[/world drop]', 1)[0]
    values = list(map(int, re.findall(r'-?\d+', body)))
    i = 0
    groups = []
    while i < len(values):
        level, unknown = values[i:i + 2]
        i += 2
        pairs = []
        while values[i] != -1:
            item, weight = values[i:i + 2]
            pairs.append((item, weight))
            i += 2
        i += 1
        groups.append((level, unknown, pairs))

    files = []
    for line in run('files', 'independent').splitlines():
        parts = line.split('\t')
        if len(parts) == 3:
            files.append({'size': int(parts[1]), 'path': parts[2]})
    with archive.open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest().upper()
    result = {
        'archive': str(archive), 'sha256': digest,
        'interpretation': 'Structure only; mode meanings, count axis and probability bases require separate evidence.',
        'independent': {
            'rows': len(rows), 'mode_counts': dict(Counter(row[16] for row, _ in rows)),
            'variable_count_rows': sum(len(set(row[8:13])) > 1 for row, _ in rows),
            'samples': [{'fields': row, 'list': pool} for row, pool in rows if row[1] in (17, 20, 907)],
            'empty_external_files': [f['path'] for f in files if f['size'] == 0],
        },
        'world': {
            'groups': len(groups), 'second_field_values': dict(Counter(g[1] for g in groups)),
            'samples': [{'level': level, 'second_field': unknown, 'sum': sum(w for _, w in pairs),
                         'positive_examples': [(item, w) for item, w in pairs if w > 0][:10]}
                        for level, unknown, pairs in groups if level in (1, 20, 70, 85)],
        },
        'quest_tag_scan': [line.strip() for line in run('tags', '.qst').splitlines()
                           if 'reward item' in line or 'tag vocabulary' in line],
    }
    output = Path(__file__).with_name('probe-results.json')
    output.write_bytes((json.dumps(result, ensure_ascii=False, indent=2) + '\n').encode('utf-8'))
    print(f'{output}: independent={len(rows)}, world_groups={len(groups)}, sha256={digest}')


if __name__ == '__main__':
    main()
