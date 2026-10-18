"""Capture entry and quest acceptance with isolated databases and launcher-issued tickets."""
import argparse
import json
import os
from pathlib import Path
import secrets
import socket
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request

HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent


def free_ports(count):
    sockets = []
    try:
        for _ in range(count):
            sock = socket.socket()
            sock.bind(('127.0.0.1', 0))
            sockets.append(sock)
        return [sock.getsockname()[1] for sock in sockets]
    finally:
        for sock in sockets:
            sock.close()


def request(base, action, credentials=None):
    data = None if credentials is None else json.dumps(credentials).encode()
    req = urllib.request.Request(base + '/api/launcher/v1/' + action, data=data,
                                 headers={'Content-Type': 'application/json'})
    with urllib.request.urlopen(req, timeout=5) as response:
        return json.load(response)


def run_tool(script, arguments, output, env):
    with output.open('w', encoding='utf-8') as log:
        subprocess.run([sys.executable, str(HERE / script), *map(str, arguments)],
                       env=env, stdout=log, stderr=subprocess.STDOUT, check=True, timeout=180)


def capture(args, run, root, out):
    channel, game_a, game_b, http = free_ports(4)
    database = root / f'{run}.db'
    base = f'http://127.0.0.1:{http}'
    options = {
        'Channel:UseScriptCatalog': 'false', 'Logging:EventLog:LogLevel:Default': 'None',
        'Database:Path': database, 'Networking:Enabled': 'true',
        'Networking:UdpDiscoveryEnabled': 'false', 'Networking:ChannelPort': channel,
        'Networking:GamePorts:0': game_a, 'Networking:GamePorts:1': game_b,
        'Channel:Channels:0:Port': game_a, 'Channel:Channels:1:Port': game_b,
        'Channel:Channels:0:Host': '127.0.0.1', 'Channel:Channels:1:Host': '127.0.0.1',
        'Channel:PublicServerIp': '127.0.0.1', 'Pvf:ArchivePath': args.pvf,
        'LauncherAuth:Enabled': 'true', 'LauncherAuth:ListenUrl': base,
        'LauncherAuth:PublicChannel:Host': '127.0.0.1',
        'LauncherAuth:PublicChannel:Port': channel, 'GmWeb:Enabled': 'false',
    }
    # Command-line values override settings without editing the built host's files.
    command = ['dotnet', str(args.host), *[f'--{key}={value}' for key, value in options.items()]]
    env = dict(os.environ, PVF_ARCHIVE_PATH=str(args.pvf), PYTHONDONTWRITEBYTECODE='1')
    flags = subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
    log_path = out / f'host-{run}-{args.label}.log'
    with log_path.open('w', encoding='utf-8') as log:
        host = subprocess.Popen(command, cwd=root, env=env, stdin=subprocess.DEVNULL,
                                stdout=log, stderr=subprocess.STDOUT, creationflags=flags)
        try:
            deadline = time.monotonic() + 90
            while True:
                if host.poll() is not None:
                    raise RuntimeError(f'host exited; see {log_path}')
                try:
                    request(base, 'bootstrap')
                    break
                except (urllib.error.URLError, TimeoutError):
                    if time.monotonic() >= deadline:
                        raise RuntimeError(f'host startup timed out; see {log_path}')
                    time.sleep(0.5)
            credentials = {'loginId': 'wire-smoke', 'password': secrets.token_urlsafe(24)}
            request(base, 'register', credentials)

            def ticket_env():
                login = request(base, 'login', credentials)
                return dict(env, SMOKE_MID=login['mId'], SMOKE_TICKET=login['ticket'])

            run_tool('wireclient.py', ['--port', game_b, '--name', args.name, '--job', args.job],
                     out / f'create-{run}-{args.label}.txt', ticket_env())
            if run == 'entry':
                batch = out / f'entry-batch-{args.label}.txt'
                run_tool('entryprobe.py', ['--port', game_b, '--out', batch],
                         out / f'entry-probe-{args.label}.log', ticket_env())
                if args.reference:
                    compare = [batch, out / f'entry-compare-{args.label}.txt', '--reference', args.reference]
                    if args.enums:
                        compare += ['--enums', args.enums]
                    run_tool('cmpentry2.py', compare, out / f'compare-{args.label}.log', env)
            else:
                run_tool('questprobe.py', ['--port', game_b],
                         out / f'quest-accept-{args.label}.txt', ticket_env())
            run_tool('questrows.py', [database], out / f'quest-rows-after-{run}-{args.label}.txt', env)
        finally:
            if host.poll() is None:
                host.terminate()
                try:
                    host.wait(timeout=15)
                except subprocess.TimeoutExpired:
                    host.kill()
                    host.wait(timeout=10)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('label')
    parser.add_argument('outdir', nargs='?', type=Path)
    parser.add_argument('--host', type=Path, default=REPO / 'Server/src/Dfo.Server.Host/bin/Debug/net10.0/Dfo.Server.Host.dll')
    parser.add_argument('--pvf', type=Path, default=os.environ.get('PVF_ARCHIVE_PATH'))
    parser.add_argument('--reference', type=Path, help='Optional historical 66-frame reference; any difference fails')
    parser.add_argument('--enums', type=Path)
    parser.add_argument('--name', default='Probe')
    parser.add_argument('--job', type=int, default=0)
    args = parser.parse_args()
    if not args.label or any(c not in 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_' for c in args.label):
        parser.error('label must contain only letters, digits, hyphens or underscores')
    for name in ('host', 'pvf', 'reference', 'enums'):
        value = getattr(args, name)
        if value is None:
            if name == 'pvf':
                parser.error('--pvf or PVF_ARCHIVE_PATH is required')
            continue
        value = value.resolve()
        if not value.is_file():
            parser.error(f'{name} file does not exist: {value}')
        setattr(args, name, value)
    out = (args.outdir or Path(f'capture-{args.label}')).resolve()
    out.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='dfo-wiresmoke-') as directory:
        for run in ('entry', 'accept'):
            print(f'{run}: isolated host, database and launcher session', flush=True)
            capture(args, run, Path(directory), out)
    print(f'Captured under {out}. Reference comparison: {"passed" if args.reference else "not requested"}.')


if __name__ == '__main__':
    try:
        main()
    except (RuntimeError, subprocess.SubprocessError, urllib.error.URLError, OSError) as error:
        print(f'WireSmoke failed: {error}', file=sys.stderr)
        sys.exit(1)
