"""Archive only allowlisted combat bodies and request identities from the two A21 runs."""
import hashlib
import json
import re
from pathlib import Path

source = Path('artifacts/giant-dragon/live-20261027-204411/combat-trace-stdout.log')
raw = source.read_bytes()
assert len(raw) < 50 * 1024 * 1024
# The redirected Windows console contains legacy localized bytes. All extracted
# protocol fields are ASCII; latin1 preserves source bytes without replacement.
lines = raw.decode('latin1').splitlines()
timestamp = ''
frames = []
for number, line in enumerate(lines, 1):
    if re.match(r'^2026-10-27 ', line):
        timestamp = line.split(' +08:00')[0]
    match = re.search(r'Game inbound (Game:02-00-00-00): selector=0x([0-9A-F]+) '
        r'type=0x([0-9A-F]+) seq=(\d+) ordinal=(\d+) body\((\d+)B\) captured=(\d+) hex=(\S+)', line)
    if not match:
        continue
    connection, selector, kind, sequence, ordinal, size, captured, body = match.groups()
    row = dict(time=timestamp, sourceLine=number, connection=connection,
        selector=int(selector, 16), type=f'0x{int(kind, 16):04X}', sequence=int(sequence),
        ordinal=int(ordinal), bodyLength=int(size))
    if int(kind, 16) in (0x27, 0x2e, 0x75) and body != 'withheld':
        assert int(size) == int(captured) == len(bytes.fromhex(body))
        row['bodyHex'] = body
    frames.append(row)

destination = Path('protocol/patterns/data/traces/a21-giant-dragon-completion-20261027.json')
assert not destination.exists(), 'Refuse overwriting captured evidence'
runs = []
for run, boss, sequence, elapsed, feedback in [
    (1, 38864, 206, 66822, 'first: near 5 and 4'),
    (2, 14718, 388, 95997, 'second: near 3'),
]:
    reports = [f for f in frames if f['sequence'] in (sequence - 2, sequence - 1, sequence)]
    assert [f['type'] for f in reports] == ['0x0027', '0x0075', '0x002E']
    runs.append(dict(run=run, bossActor=boss, serverElapsedMs=elapsed,
        userFeedback=feedback, reports=reports))
    for report in reports:
        sample = Path('protocol/game/client-to-server/samples') / f'{report["type"]}-20261027-giant-dragon-{run}.hex'
        assert not sample.exists()
        sample.write_text(report['bodyHex'] + '\n', encoding='ascii')

client_path = Path(r'E:\Project\DNF\DNFClient-A21-protocol2\DNF.exe')
with client_path.open('rb') as executable:
    client_hash = hashlib.file_digest(executable, 'sha256').hexdigest()
assert client_hash == '1f8244d3f7309bbe2fde4299e1863a894f6b28d5707bf01732980585b20b00dd'
result = dict(sourceLogSha256=hashlib.sha256(raw).hexdigest(), sourceLogBytes=len(raw),
    clientExePath=str(client_path),
    clientPvfSha256='d45cf58b1a2b3d16d6b35abe0d4e302e052f0b6bc43e9d3e98d815fed54aeF84'.lower(),
    clientExeSha256=client_hash,
    mappedImageSha256='aaae24216e0e260afa528445775e767d104f658fd68fdad21ec7bb601b9c8a3d',
    evidence='Original A21 C2S through server framing; authentication bodies withheld; synthetic Game:01 excluded',
    runs=runs, requestIdentitySequence=frames)
destination.write_text(json.dumps(result, indent=2), encoding='utf8')
print(json.dumps(dict(archive=str(destination), frames=len(frames), samples=6), indent=2))
