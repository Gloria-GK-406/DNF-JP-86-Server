"""Start one local collection scene from the committed binary dataset; never rebuild fixtures."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import shutil
import socket
import subprocess
import time
import urllib.error
import urllib.request

REPO = Path(__file__).resolve().parents[2]
DEFAULT_DATASET = REPO / 'TestAssets/E2E/a21-initial-v1'


def digest(path):
    with path.open('rb') as source:
        return hashlib.file_digest(source, 'sha256').hexdigest()


def validate(dataset):
    manifest = json.loads((dataset / 'manifest.json').read_text(encoding='utf-8'))
    for name in ('Script.pvf', 'baseline.db'):
        actual = digest(dataset / name)
        if actual != manifest['files'][name]['sha256']:
            raise ValueError(f'Dataset hash mismatch: {name}')
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dataset', type=Path, default=DEFAULT_DATASET)
    parser.add_argument('--host', type=Path, default=REPO / 'Server/src/Dfo.Server.Host/bin/Release/net10.0/Dfo.Server.Host.dll')
    parser.add_argument('--scene', default='login-create-town')
    parser.add_argument('--out', type=Path)
    parser.add_argument('--http-port', type=int, default=18080)
    parser.add_argument('--channel-port', type=int, default=7001)
    parser.add_argument('--verify-only', action='store_true')
    parser.add_argument('--client-root', type=Path, help='Real client directory; require matching fixed PVF before collection')
    args = parser.parse_args()
    dataset = args.dataset.resolve(strict=True)
    manifest = validate(dataset)
    if args.verify_only:
        print(json.dumps(dict(dataset=manifest['datasetId'], files='verified', accounts=len(manifest['accounts']))))
        return
    host = args.host.resolve(strict=True)
    if args.client_root is not None and digest(args.client_root / 'Script.pvf') != manifest['files']['Script.pvf']['sha256']:
        raise ValueError('Client PVF differs from the committed E2E material')
    if digest(host.parent / 'Resources/Channel/channel_info.etc') != manifest['channelScriptSha256']:
        raise ValueError('Host channel script differs from the dataset deployment identity')
    reservations = []
    try:
        for port in [args.http_port, args.channel_port, *manifest['gamePorts']]:
            reservation = socket.socket()
            reservations.append(reservation)
            reservation.bind(('0.0.0.0', port))
    finally:
        for reservation in reservations:
            reservation.close()
    run = (args.out or REPO / 'artifacts/e2e-collections' / datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')).resolve()
    run.mkdir(parents=True, exist_ok=False)
    # All accounts/roles are already in baseline.db. A scene only gets a private copy.
    shutil.copyfile(dataset / 'baseline.db', run / 'run.db')
    base = f'http://127.0.0.1:{args.http_port}'
    options = {
        'Database:Path': run / 'run.db', 'Networking:Enabled': 'true',
        'Networking:UdpDiscoveryEnabled': 'false', 'Networking:ChannelPort': args.channel_port,
        'Networking:FrameCapturePath': run / 'frames.jsonl',
        'Channel:UseScriptCatalog': 'true', 'Channel:IncludePvpChannels': str(manifest['includePvpChannels']).lower(),
        'Channel:PublicServerIp': '127.0.0.1', 'Game:EndpointHost': '127.0.0.1',
        'LauncherAuth:Enabled': 'true', 'LauncherAuth:ListenUrl': base,
        'LauncherAuth:PublicChannel:Host': '127.0.0.1',
        'LauncherAuth:PublicChannel:Port': args.channel_port, 'GmWeb:Enabled': 'false',
    }
    env = {k: v for k, v in os.environ.items() if '__' not in k and k not in ('PVF_ARCHIVE_PATH', 'DOTNET_ENVIRONMENT', 'ASPNETCORE_ENVIRONMENT')}
    env.update(PVF_ARCHIVE_PATH=str(dataset / 'Script.pvf'), DOTNET_ENVIRONMENT='Production')
    command = ['dotnet', str(host), *[f'--{key}={value}' for key, value in options.items()]]
    with (run / 'host.log').open('w', encoding='utf-8') as log:
        process = subprocess.Popen(command, cwd=run, env=env, stdout=log, stderr=subprocess.STDOUT,
                                   stdin=subprocess.DEVNULL,
                                   creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0)
    info = dict(scene=args.scene, dataset=manifest['datasetId'], datasetPath=str(dataset),
                pvfSha256=manifest['files']['Script.pvf']['sha256'],
                baselineSha256=manifest['files']['baseline.db']['sha256'], hostDllSha256=digest(host),
                hostCommit=subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=REPO, text=True).strip(),
                sourceDirty=bool(subprocess.check_output(['git', 'status', '--porcelain'], cwd=REPO, text=True).strip()),
                pid=process.pid, database=str(run / 'run.db'), capture=str(run / 'frames.jsonl'),
                http=base, channelPort=args.channel_port, startedAt=datetime.now(timezone.utc).isoformat(),
                ready=False, clientVerified=False, goldenBaseline=False)
    if args.client_root is not None:
        info['clientRoot'] = str(args.client_root.resolve())
        info['clientExeSha256'] = digest(args.client_root / 'DNF.exe')
    metadata = run / 'collection.json'
    metadata.write_text(json.dumps(info, ensure_ascii=False, indent=2), encoding='utf-8')
    try:
        deadline = time.monotonic() + 180
        while time.monotonic() < deadline:
            if process.poll() is not None:
                raise RuntimeError(f'Host exited with {process.returncode}; see {run / "host.log"}')
            try:
                with urllib.request.urlopen(base + '/api/launcher/v1/bootstrap', timeout=3) as response:
                    if response.status == 200:
                        # HTTP bootstrap alone can be ready when TouchSocket failed to bind.
                        # This unauthed probe is control traffic, not a real-client collection step.
                        probe_port = 10010 if 10010 in manifest['gamePorts'] else manifest['gamePorts'][0]
                        with socket.create_connection(('127.0.0.1', probe_port), timeout=1):
                            pass
                        info['ready'] = True
                        info['readyAt'] = datetime.now(timezone.utc).isoformat()
                        info['readinessProbe'] = dict(port=probe_port, authenticated=False, excludedFromBusinessCases=True)
                        metadata.write_text(json.dumps(info, ensure_ascii=False, indent=2), encoding='utf-8')
                        print(json.dumps(info, ensure_ascii=False, indent=2))
                        return
            except (urllib.error.URLError, OSError):
                time.sleep(0.25)
        raise TimeoutError(f'Host not ready; see {run / "host.log"}')
    except BaseException:
        if process.poll() is None:
            process.terminate()
            process.wait(timeout=30)
        raise


if __name__ == '__main__':
    main()
