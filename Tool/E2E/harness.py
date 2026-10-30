"""Out-of-process production Host; only network mutations and read-only SQL observations."""
import hashlib
from contextlib import closing
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import signal
import socket
import sqlite3
import struct
import subprocess
import sys
import time
import urllib.error
import urllib.request
import zlib

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'WireSmoke'))
from entryprobe import Conn, dstr, frame


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def sha256(path):
    with Path(path).open('rb') as source:
        return hashlib.file_digest(source, 'sha256').hexdigest()


def free_ports(count):
    sockets = []
    try:
        for _ in range(count):
            sock = socket.socket()
            sock.bind(('127.0.0.1', 0))
            sockets.append(sock)
        return [s.getsockname()[1] for s in sockets]
    finally:
        for sock in sockets:
            sock.close()


class GameClient(Conn):
    def __init__(self, endpoint, capture):
        super().__init__(*endpoint)
        self.capture = capture
        self.sequence = 0

    def record(self, direction, selector, opcode, body, sequence=None):
        # Authentication frames contain bearer credentials. Their shape is retained without values.
        record = dict(endpoint='Game', direction=direction, selector=selector,
                      opcode=f'0x{opcode:04X}', length=len(body), seq=sequence)
        record['bodyHex'] = '<redacted>' if direction == 'C2S' and opcode == 1 else body.hex()
        self.capture.write(json.dumps(record) + '\n')
        self.capture.flush()

    def send(self, selector, type_, body=b''):
        self.sequence = (self.sequence + 1) % 65536
        payload = bytearray(frame(selector, type_, body))
        struct.pack_into('<H', payload, 12, self.sequence)
        self.s.sendall(payload)
        self.record('C2S', selector, type_, body, self.sequence)

    def recv_frame(self, timeout=30):
        result = super().recv_frame(timeout)
        if result is None:
            raise TimeoutError('Game peer closed or no complete frame before deadline')
        self.record('S2C', *result)
        return result

    def until(self, selector, opcode, timeout=45):
        received = []
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            result = self.recv_frame(max(0.1, deadline - time.monotonic()))
            received.append(result)
            if result[:2] == (selector, opcode):
                return received
        raise TimeoutError(f'Missing {selector}:0x{opcode:04X}')

    def command(self, opcode, body=b''):
        self.send(1, opcode, body)
        received = self.until(1, opcode)
        require(received[-1][2][:1] == b'\x01', f'CMD 0x{opcode:04X} refused: {received[-1][2].hex()}')
        return received

    def exit(self):
        self.command(3, b'\0')
        self.close()


class Host:
    def __init__(self, host, pvf, out):
        self.host = Path(host).resolve()
        self.pvf = Path(pvf).resolve()
        self.out = Path(out).resolve()
        self.database = self.out / 'run.db'
        self.baseline = self.out / 'baseline.db'
        self.http_port, self.channel_port = free_ports(2)
        self.base = f'http://127.0.0.1:{self.http_port}'
        self.process = None
        self.log = None
        self.starts = 0
        self.client = None
        self.credentials = None
        self.endpoint = None
        self.capture = (self.out / 'frames.jsonl').open('w', encoding='utf-8')

    def request(self, action, data=None, expected=200):
        req = urllib.request.Request(self.base + '/api/launcher/v1/' + action,
                                     data=None if data is None else json.dumps(data).encode(),
                                     headers={'Content-Type': 'application/json'})
        try:
            with urllib.request.urlopen(req, timeout=10) as response:
                status, value = response.status, json.load(response)
        except urllib.error.HTTPError as error:
            status, value = error.code, json.load(error)
        require(status == expected, f'HTTP {action}: expected {expected}, got {status}')
        return value

    def start(self, database):
        require(self.process is None, 'Host already running')
        self.starts += 1
        self.log = (self.out / f'host-{self.starts}.log').open('w', encoding='utf-8')
        # All inherited deployment overrides are excluded; this process owns only its run.db.
        env = {k: v for k, v in os.environ.items() if '__' not in k and k not in ('PVF_ARCHIVE_PATH', 'DOTNET_ENVIRONMENT', 'ASPNETCORE_ENVIRONMENT')}
        env.update(PVF_ARCHIVE_PATH=str(self.pvf), DOTNET_ENVIRONMENT='Production')
        options = {
            'Database:Path': str(database), 'Pvf:ArchivePath': str(self.pvf),
            'Channel:UseScriptCatalog': 'true', 'Channel:IncludePvpChannels': 'true',
            'Channel:PublicServerIp': '127.0.0.1', 'Game:EndpointHost': '127.0.0.1',
            'Networking:Enabled': 'true', 'Networking:UdpDiscoveryEnabled': 'false',
            'Networking:ChannelPort': self.channel_port, 'LauncherAuth:Enabled': 'true',
            'LauncherAuth:ListenUrl': self.base, 'LauncherAuth:PublicChannel:Host': '127.0.0.1',
            'LauncherAuth:PublicChannel:Port': self.channel_port, 'GmWeb:Enabled': 'false',
        }
        command = ['dotnet', str(self.host), *[f'--{k}={v}' for k, v in options.items()]]
        self.process = subprocess.Popen(command, cwd=self.out, env=env, stdout=self.log,
                                        stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL,
                                        start_new_session=os.name != 'nt')
        deadline = time.monotonic() + 180
        while time.monotonic() < deadline:
            require(self.process.poll() is None, f'Host exited: see host-{self.starts}.log')
            try:
                self.request('bootstrap')
                return
            except (urllib.error.URLError, TimeoutError):
                time.sleep(0.25)
        raise TimeoutError('Production Host bootstrap did not become ready')

    def stop(self):
        if self.client:
            self.client.close()
            self.client = None
        if self.process:
            process, self.process = self.process, None
            try:
                if process.poll() is None:
                    process.terminate()
                    try:
                        process.wait(timeout=30)
                    except subprocess.TimeoutExpired:
                        if os.name == 'nt':
                            process.kill()
                        else:
                            os.killpg(process.pid, signal.SIGKILL)
                        process.wait(timeout=10)
                        raise RuntimeError('Host required forced termination')
                require(process.returncode == 0, f'Host shutdown exit code: {process.returncode}')
            finally:
                self.log.close()

    def prepare(self):
        self.start(self.baseline)
        self.credentials = {'loginId': 'e2e-player', 'password': secrets.token_urlsafe(24)}
        self.request('register', self.credentials, expected=201)
        self.stop()
        with closing(sqlite3.connect(self.baseline)) as db:
            require(db.execute('PRAGMA integrity_check').fetchone()[0] == 'ok', 'Baseline integrity failed')
            require(not db.execute('PRAGMA foreign_key_check').fetchall(), 'Baseline foreign key failure')
            require(db.execute('SELECT COUNT(*) FROM accounts').fetchone()[0] == 1, 'Baseline must have one fixture account')
            require(db.execute('SELECT COUNT(*) FROM characters').fetchone()[0] == 0, 'Baseline must have no characters')
        shutil.copy2(self.baseline, self.database)
        self.baseline_hash = sha256(self.baseline)
        self.start(self.database)

    def snapshot(self):
        with closing(sqlite3.connect(self.database.as_uri() + '?mode=ro', uri=True)) as db:
            db.row_factory = sqlite3.Row
            return {name: [dict(row) for row in db.execute(query)] for name, query in (
                ('accounts', 'SELECT account_id,m_id FROM accounts ORDER BY account_id'),
                ('characters', 'SELECT account_id,character_id,name,slot_index,job,level,exp FROM characters ORDER BY character_id'),
                ('quests', 'SELECT * FROM character_active_quests ORDER BY character_id,slot'),
                ('items', 'SELECT item_uid,hex(item_core) AS core FROM items ORDER BY item_uid'))}

    def checkpoint(self, name):
        value = self.snapshot()
        (self.out / f'state-{name}.json').write_text(json.dumps(value, indent=2), encoding='utf-8')
        return value

    def channel(self, endpoint):
        require(endpoint == {'host': '127.0.0.1', 'port': self.channel_port}, 'Unexpected launcher Channel route')
        with socket.create_connection((endpoint['host'], endpoint['port']), timeout=10) as sock:
            def exact(length):
                data = bytearray()
                while len(data) < length:
                    part = sock.recv(length - len(data))
                    require(part, 'Channel closed mid-frame')
                    data.extend(part)
                return bytes(data)

            def exchange(number, response):
                header = bytearray(11)
                header[0:2] = bytes([0x7C, number])
                struct.pack_into('<I', header, 2, 11)
                header[10] = 1
                sock.sendall(header)
                received = exact(11)
                total = struct.unpack_from('<I', received, 2)[0]
                require(11 <= total <= 1048576 and received[1] == response, 'Invalid Channel response')
                body = exact(total - 11)
                for direction, num, payload in [('C2S', number, b''), ('S2C', response, body)]:
                    self.capture.write(json.dumps(dict(endpoint='Channel', direction=direction,
                                                      opcode=f'0x{num:02X}', bodyHex=payload.hex())) + '\n')
                return body

            connected = exchange(0x0B, 0x0C)
            key = connected[4:20]
            require(len(key) == 16, 'Invalid Channel AES key')

            def decrypt(body):
                encrypted = zlib.decompress(body)
                return subprocess.run(['openssl', 'enc', '-d', '-aes-128-ecb', '-nopad', '-K', key.hex()],
                                      input=encrypted, capture_output=True, check=True, timeout=10).stdout

            version = exchange(0x05, 0x06)
            require(version, 'Empty script version')
            script = decrypt(exchange(0x09, 0x0A))
            shipped = (self.host.parent / 'Resources/Channel/channel_info.etc').read_bytes()
            require(script.startswith(shipped), 'Channel did not serve the shipped production script')
            info = decrypt(exchange(0x11, 0x12))
            count = struct.unpack_from('<i', info, 2)[0]
            require(0 < count <= 255 and len(info) >= 6 + count * 48, 'Invalid Channel directory')
            entries = []
            for index in range(count):
                offset = 6 + index * 48
                name = info[offset:offset+20].split(b'\0')[0].decode('ascii')
                host = info[offset+28:offset+44].split(b'\0')[0].decode('ascii')
                port = struct.unpack_from('<i', info, offset+44)[0]
                entries.append(dict(name=name, host=host, port=port))
            selected = next(e for e in entries if e['name'] == '#ch.10')
            require(selected['host'] == '127.0.0.1', 'Game route must stay on loopback')
            self.endpoint = selected['host'], selected['port']
            return entries

    def login(self):
        login = self.request('login', self.credentials)
        require(re.fullmatch(r'[0-9]{6}', login['mId']), 'Invalid launcher mId')
        require(re.fullmatch(r'[0-9A-F]{32}', login['ticket']), 'Invalid launcher ticket')
        self.channel(login['channel'])
        client = GameClient(self.endpoint, self.capture)
        self.client = client
        require(client.recv_frame()[:2] == (0, 1), 'Game initial notice missing')
        client.command(1, dstr(login['mId']) + dstr(login['ticket']))
        return client

    def close(self):
        try:
            self.stop()
        finally:
            self.capture.close()
