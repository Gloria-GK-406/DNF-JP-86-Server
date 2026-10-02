import json
import pathlib
import socket
import subprocess
import time
import urllib.error
import urllib.request

root = pathlib.Path(__file__).resolve().parents[6]
runtime = root / 'artifacts/gm-foundation'
with socket.socket() as reservation:
    reservation.bind(('127.0.0.1', 0))
    port = reservation.getsockname()[1]
log_path = root / 'artifacts/gm-tests/spec-001-static-host.log'
with log_path.open('w', encoding='utf-8') as log:
    host = subprocess.Popen(['dotnet', str(runtime / 'Dfo.Server.Host.dll'),
        '--Networking:Enabled=false', '--LauncherAuth:Enabled=false', '--GmWeb:Enabled=true',
        f'--GmWeb:ListenUrl=http://127.0.0.1:{port}', f'--Database:Path={runtime / "Data/smoke.db"}'],
        stdout=log, stderr=log)
    try:
        def get(path):
            try:
                with urllib.request.urlopen(f'http://127.0.0.1:{port}{path}', timeout=2) as response:
                    return response.status, response.read()
            except urllib.error.HTTPError as error:
                return error.code, error.read()
        for attempt in range(100):
            if host.poll() is not None:
                raise RuntimeError(log_path.read_text(encoding='utf-8'))
            try:
                if get('/api/gm/v1/health')[0] == 200:
                    break
            except urllib.error.URLError:
                time.sleep(0.1)
        else:
            raise TimeoutError('GM listener did not become ready')
        results = {}
        for path, expected in [('/gm/', 200), ('/gm/characters', 200),
                ('/gm/assets/not-found.js', 404), ('/api/gm/v1/not-found', 404),
                ('/api/launcher/v1/bootstrap', 404)]:
            status, body = get(path)
            assert status == expected, (path, status, body)
            if expected == 200:
                assert b'<div id="root">' in body
            results[path] = status
        assets = list((runtime / 'wwwroot/gm/assets').glob('*.js'))
        assert assets, 'Published JavaScript missing'
        results['published_js'] = len(assets)
        print(json.dumps(results, ensure_ascii=False))
    finally:
        host.terminate()
        host.wait(timeout=10)
