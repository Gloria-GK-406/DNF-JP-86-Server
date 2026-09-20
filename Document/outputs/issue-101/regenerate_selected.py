"""Regenerate only #101's two machine zones using the existing generator.

The full inventory/index and human zones are never replaced. Layout data remains
the extractor's bounded result; this does not upgrade it to complete verification.
"""
import csv
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

repo = Path(__file__).resolve().parents[3]
data = repo / 'protocol/patterns/data'
targets = {'game/client-to-server/0x0010.md', 'game/server-to-client/NOTI-0x0005.md'}
with (data / 'protocol-inventory.tsv').open(encoding='utf-8') as stream:
    reader = csv.DictReader(stream, delimiter='\t')
    fields = reader.fieldnames
    rows = [row for row in reader if row['doc'] in targets]
assert {row['doc'] for row in rows} == targets
with tempfile.TemporaryDirectory(prefix='issue101-docs-') as temporary:
    root = Path(temporary)
    inventory = root / 'inventory.tsv'
    with inventory.open('w', encoding='utf-8', newline='') as stream:
        writer = csv.DictWriter(stream, fields, delimiter='\t', lineterminator='\n')
        writer.writeheader()
        writer.writerows(rows)
    for target in targets:
        destination = root / target
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(repo / 'protocol' / target, destination)
    subprocess.run([sys.executable, str(data / 'generate-docs.py'), str(inventory),
                    str(data / 'protocol-layouts.json'), str(root)], check=True)
    for target in sorted(targets):
        shutil.copyfile(root / target, repo / 'protocol' / target)
        print(target)
