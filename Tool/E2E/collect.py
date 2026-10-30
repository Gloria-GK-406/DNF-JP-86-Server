"""Record real-client collection metadata, operator marks and consistent SQLite backups.

This tool never creates accounts, characters, requests, expected packets or approvals.
"""
import argparse
from contextlib import closing
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import sqlite3


def digest(path):
    with path.open('rb') as source:
        return hashlib.file_digest(source, 'sha256').hexdigest()


def identity(path):
    path = path.resolve(strict=True)
    return dict(path=str(path), bytes=path.stat().st_size, sha256=digest(path))


def now():
    return datetime.now(timezone.utc).isoformat()


def write_new(path, value):
    with path.open('x', encoding='utf-8', newline='\n') as output:
        json.dump(value, output, ensure_ascii=False, indent=2)
        output.write('\n')
    path.chmod(0o600)


def event(directory, value):
    if not (directory / 'collection.json').is_file():
        raise ValueError('Run init first; this is not a collection directory')
    with (directory / 'operator-events.jsonl').open('a', encoding='utf-8') as output:
        output.write(json.dumps(dict(at=now(), **value), ensure_ascii=False) + '\n')


def backup(database, target):
    database = database.resolve(strict=True)
    if target.exists():
        raise FileExistsError(target)
    # mode=ro includes committed WAL pages without allowing writes to the source.
    with closing(sqlite3.connect(database.as_uri() + '?mode=ro', uri=True)) as source:
        with closing(sqlite3.connect(target)) as destination:
            source.backup(destination)
            if destination.execute('PRAGMA integrity_check').fetchone()[0] != 'ok':
                raise ValueError('Snapshot integrity check failed')
    target.chmod(0o600)
    return identity(target)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='action', required=True)
    client = sub.add_parser('client-manifest')
    client.add_argument('--client-root', type=Path, required=True)
    client.add_argument('--out', type=Path, required=True)
    init = sub.add_parser('init')
    init.add_argument('--out', type=Path, required=True)
    init.add_argument('--host', type=Path, required=True)
    init.add_argument('--pvf', type=Path, required=True)
    mark = sub.add_parser('mark')
    mark.add_argument('--out', type=Path, required=True)
    mark.add_argument('--step', required=True)
    mark.add_argument('--phase', choices=['before', 'after'], required=True)
    mark.add_argument('--observation', default='unreviewed')
    snapshot = sub.add_parser('snapshot')
    snapshot.add_argument('--out', type=Path, required=True)
    snapshot.add_argument('--database', type=Path, required=True)
    snapshot.add_argument('--name', required=True)
    args = parser.parse_args()
    if args.action == 'client-manifest':
        root = args.client_root.resolve(strict=True)
        names = ['DNF.exe', 'Dfo.Launcher.exe', 'az.dll', 'ClientHack.ini', 'Launcher.ini', 'Script.pvf']
        files = [identity(root / name) for name in names]
        files.extend(identity(p) for p in sorted((root / 'plugins').glob('*.dll')))
        if (root / 'Dfo.Launcher.dll').is_file():
            files.append(identity(root / 'Dfo.Launcher.dll'))
        write_new(args.out, dict(createdAt=now(), clientRoot=str(root), files=files,
                                 humanVerdict='unreviewed', baselineApproved=False))
    elif args.action == 'init':
        value = dict(createdAt=now(), host=identity(args.host), pvf=identity(args.pvf),
                     status='awaiting-real-client-collection', baselineApproved=False)
        args.out.mkdir(parents=True, exist_ok=False, mode=0o700)
        write_new(args.out / 'collection.json', value)
    elif args.action == 'mark':
        event(args.out, dict(step=args.step, phase=args.phase,
                             observation=args.observation, source='operator-input', baselineApproved=False))
    else:
        if not re.fullmatch(r'[A-Za-z0-9_-]+', args.name):
            parser.error('Snapshot name must contain only letters, digits, hyphens or underscores')
        if not (args.out / 'collection.json').is_file():
            parser.error('Run init first')
        value = backup(args.database, args.out / (args.name + '.db'))
        event(args.out, dict(snapshot=args.name, databaseSource=str(args.database.resolve()), file=value))


if __name__ == '__main__':
    main()
