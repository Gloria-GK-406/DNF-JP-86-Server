"""Real-client-derived login/relogin and create flows. Never constructs fixture data."""
from concurrent.futures import ThreadPoolExecutor
from contextlib import closing
import json
from pathlib import Path
import shutil
import socket
import sqlite3
import struct
import subprocess
import time
import uuid
import zlib

from harness import GameClient, Host, dstr, require, sha256
from native_match import compare_frame
from scene import validate


class MaterialScene(Host):
    def __init__(self, host, dataset, out):
        self.dataset = Path(dataset).resolve()
        self.material = validate(self.dataset)
        super().__init__(host, self.dataset / 'Script.pvf', out, self.material['includePvpChannels'])

    def prepare(self):
        require(sha256(self.host.parent / 'Resources/Channel/channel_info.etc') == self.material['channelScriptSha256'],
                'Host channel script differs from recorded deployment')
        reservations = []
        try:
            for port in [self.http_port, self.channel_port, *self.material['gamePorts']]:
                probe = socket.socket()
                reservations.append(probe)
                probe.bind(('0.0.0.0', port))
        finally:
            for probe in reservations:
                probe.close()
        shutil.copyfile(self.dataset / 'baseline.db', self.baseline)
        shutil.copyfile(self.baseline, self.database)
        self.baseline_hash = sha256(self.baseline)
        self.start(self.database)
        # Bootstrap may be ready even if a fixed production Game port failed to bind.
        with socket.create_connection(('127.0.0.1', 10010), timeout=10):
            pass

    def read(self, sql, parameters=()):
        with closing(sqlite3.connect(self.database.as_uri() + '?mode=ro', uri=True)) as db:
            return db.execute(sql, parameters).fetchall()


def adapter_bindings():
    # UDP connect selects a local route without transmitting a datagram. No remote service
    # or production identity is used; the client reports this runner's actual interface/MAC.
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as probe:
        probe.connect(('192.0.2.1', 9))
        address = socket.inet_aton(probe.getsockname()[0])
    return dict(adapterIPv4=address, adapterMacAscii12=f'{uuid.getnode():012x}'.encode('ascii'))


def bind_body(spec, bindings):
    body = bytearray.fromhex(spec['bodyHex'])
    for patch in spec.get('patches', []):
        value = bindings[patch['binding']]
        offset = patch['offset']
        require(0 <= offset <= len(body) - len(value), 'Binding exceeds captured body')
        body[offset:offset + len(value)] = value
    return body


def openssl():
    command = shutil.which('openssl')
    if command is None:
        candidate = Path('C:/Program Files/Git/usr/bin/openssl.exe')
        if candidate.is_file():
            command = str(candidate)
    require(command is not None, 'openssl is required for captured Channel AES responses')
    return command


def channel(scene, endpoint, flow):
    require(endpoint == {'host': '127.0.0.1', 'port': scene.channel_port}, 'Unexpected Launcher Channel route')
    key = None
    with socket.create_connection((endpoint['host'], endpoint['port']), timeout=10) as peer:
        def exact(length):
            data = bytearray()
            while len(data) < length:
                part = peer.recv(length - len(data))
                require(part, 'Channel closed before a complete frame')
                data.extend(part)
            return bytes(data)

        for step in flow['channel']:
            body = bytes.fromhex(step['bodyHex'])
            header = bytes([0x7C, step['opcode']]) + struct.pack('<IIB', 11 + len(body), 0, 1)
            peer.sendall(header + body)
            response = exact(11)
            total = struct.unpack_from('<I', response, 2)[0]
            require(11 <= total <= 1048576, 'Invalid Channel frame length')
            require(response[:2] == bytes([0x7C, step['responseOpcode']]), 'Unexpected Channel family/opcode')
            received = exact(total - 11)
            if step['opcode'] == 0x0B:
                require(len(received) == 36 and received[:4] == bytes(4) and received[20:] == bytes(16),
                        'Channel key envelope differs from native sample')
                key = received[4:20]
            else:
                require(key is not None, 'Encrypted Channel response before connect key')
                plaintext = subprocess.run([openssl(), 'enc', '-d', '-aes-128-ecb', '-nopad', '-K', key.hex()],
                    input=zlib.decompress(received), capture_output=True, check=True, timeout=10).stdout
                require(plaintext == bytes.fromhex(step['plaintextHex']),
                        f"Channel 0x{step['responseOpcode']:02X} plaintext differs from recorded script/directory")
    # This is the actual recorded ordinary channel. Its directory address was compared above;
    # never substitute an arbitrary test-only channel catalog.
    return '127.0.0.1', 10010


class NativeFlow:
    def __init__(self, scene, credentials, flow, capture):
        self.flow = flow
        self.bindings = adapter_bindings()
        login = scene.request('login', credentials)
        endpoint = channel(scene, login['channel'], flow)
        self.client = GameClient(endpoint, capture)
        self.client.sequence = -1  # A21 native sample starts with seq 0, not smoke's seq 1.
        try:
            compare_frame(flow['greeting'], self.client.recv_frame())
            trailer = bind_body(dict(bodyHex=flow['loginTrailerHex'], patches=flow['loginTrailerPatches']), self.bindings)
            self.client.send(1, 1, dstr(login['mId']) + dstr(login['ticket']) + trailer)
            for expected in flow['loginExpected']:
                compare_frame(expected, self.client.recv_frame())
            self.phase('selection')
        except BaseException:
            self.client.close()
            raise

    def phase(self, name):
        phase = self.flow[name]
        for action in phase['actions']:
            if action['direction'] == 'C2S':
                self.client.send(action['selector'], action['opcode'], bind_body(action, self.bindings))
            else:
                compare_frame(action, self.client.recv_frame())

    def close(self):
        self.client.close()


def empty_login(scene, baseline, capture):
    account = next(a for a in scene.material['accounts'] if a['loginId'] == 'e2elogin')
    credentials = {key: account[key] for key in ('loginId', 'password')}
    require(scene.read('SELECT count(*) FROM characters WHERE account_id=?', (account['account_id'],))[0][0] == 0,
            'Dedicated empty account was not empty before case')
    for name in ('emptyFirst', 'emptyRelogin'):
        client = NativeFlow(scene, credentials, baseline['flows'][name], capture)
        try:
            require(scene.read('SELECT count(*) FROM characters WHERE account_id=?', (account['account_id'],))[0][0] == 0,
                    'Native empty roster disagrees with stored characters')
            client.phase('finish')
        finally:
            client.close()
    require(scene.read('SELECT count(*) FROM account_settings WHERE account_id=?', (account['account_id'],))[0][0] == 1,
            'Observed account settings persistence is absent')


def create_character(scene, baseline, capture):
    account = next(a for a in scene.material['accounts'] if a['loginId'] == 'e2ecreate')
    credentials = {key: account[key] for key in ('loginId', 'password')}
    require(scene.read('SELECT count(*) FROM characters WHERE account_id=?', (account['account_id'],))[0][0] == 0,
            'Dedicated create account already had a character')
    client = NativeFlow(scene, credentials, baseline['flows']['create'], capture)
    try:
        client.phase('finish')
        rows = scene.read('SELECT character_id,slot_index,name,job,level,exp,tutorial_completed FROM characters WHERE account_id=?',
                          (account['account_id'],))
        require(len(rows) == 1 and rows[0][1:] == (0, 'E2ECreateNat', 0, 1, 0, 0),
                'Created character differs from the native observed snapshot')
        # Bind the ID produced this run; the captured character_id=4 is never replayed.
        return dict(characterId=rows[0][0], characterName=rows[0][2])
    finally:
        client.close()


def execute_cases(scene, baseline, out):
    actions = {'AUTH-001': empty_login, 'ROLE-001': create_character}

    def execute(case):
        started = time.monotonic()
        result = dict(caseId=case['caseId'])
        try:
            with (out / (case['caseId'] + '-frames.jsonl')).open('x', encoding='utf-8') as capture:
                observed = actions[case['caseId']](scene, baseline, capture)
            result.update(status='passed', detail='Matched human-confirmed native flow and persistent state', observations=observed)
        except Exception as error:
            result.update(status='failed', detail=f'{type(error).__name__}: {error}')
        result['durationSeconds'] = round(time.monotonic() - started, 3)
        return result

    # Both accounts share this scene's full Host and run.db. Cases never reset each other.
    with ThreadPoolExecutor(max_workers=2) as workers:
        return list(workers.map(execute, baseline['cases']))
