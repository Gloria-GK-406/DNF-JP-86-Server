"""Session-scoped A21 projection fixture. Never writes character/server state.

One client at a time; configuration is frozen per connection. Original and
client-facing allowed frames are separately recorded. Not a production proxy.
"""
import argparse
import asyncio
import contextlib
import json
import struct
from datetime import datetime, timezone
from pathlib import Path

from capture_admission import Frames, select


def encode(selector, opcode, body):
    # Matches GameServerFrameEncoder: zero checksum and reserved fields.
    return struct.pack('<B H I I I', selector, opcode, 15 + len(body), 0, 0) + body


def load_profile(path):
    value = json.loads(path.read_text(encoding='utf-8-sig'))
    mode = value.get('mode')
    if mode not in ('observe', 'permission', 'fatigue'):
        raise ValueError('unknown fixture mode')
    expiry = datetime.fromisoformat(value['expires_utc'])
    if expiry.tzinfo is None or expiry <= datetime.now(timezone.utc):
        raise ValueError('fixture profile expired or lacks timezone')
    if mode == 'permission':
        if type(value.get('dungeon_id')) is not int or value['dungeon_id'] != 150:
            raise ValueError('only reviewed dungeon 150 is permitted')
        if type(value.get('state')) is not int or value['state'] not in (0, 1, 2):
            raise ValueError('only reviewed states 0..2 are permitted')
    if mode == 'fatigue':
        # Explicit wire field; names alone do not establish displayed remaining.
        if value.get('field', 'usedFatigueMax') not in ('Fatigue', 'usedFatigueMax'):
            raise ValueError('unsupported fatigue field')
        if type(value.get('used')) is not int or value['used'] not in (0, 187, 188):
            raise ValueError('only baseline/one-left/zero candidate values permitted')
    return value


class Fixture:
    def __init__(self, profile):
        self.profile = profile
        self.selected = False

    def transform(self, frame):
        selector, opcode = frame[0], struct.unpack_from('<H', frame, 1)[0]
        body = frame[15:]
        mode = self.profile['mode']
        active = datetime.now(timezone.utc) < datetime.fromisoformat(self.profile['expires_utc'])
        if selector == 1 and opcode == 4:
            self.selected = bool(len(body) >= 17 and body[0] == 1)
            if self.selected and active and mode == 'fatigue':
                # Fail closed against a changed server baseline. No assumptions
                # about variable premium/quest/tail offsets are made.
                if body[11:17] != bytes.fromhex('0000BC000000'):
                    raise ValueError('unexpected fatigue baseline')
                patched = bytearray(frame)
                offset = 11 if self.profile.get('field') == 'Fatigue' else 15
                struct.pack_into('<H', patched, 15 + offset, self.profile['used'])
                if patched[7:15] != bytes(8):
                    raise ValueError('unexpected server checksum/reserved')
                return [(bytes(patched), f'replace_selected_fatigue_offset_{offset}')]
        if not self.selected or not active or mode != 'permission':
            return [(frame, None)]
        permission = struct.pack('<H I B', 1, self.profile['dungeon_id'], self.profile['state'])
        if selector == 0 and opcode == 5:
            if body != b'\0\0':
                raise ValueError('nonempty permission baseline requires separate review')
            return [(encode(0, 5, permission), 'replace_empty_permission')]
        if selector == 0 and opcode == 0x1B:
            return [(encode(0, 5, permission), 'permission_before_selection'), (frame, None)]
        return [(frame, None)]


def record(output, connection, stage, **fields):
    output.write(json.dumps({'timestamp_utc': datetime.now(timezone.utc).isoformat(),
                             'connection': connection, 'stage': stage, **fields}) + '\n')
    output.flush()


async def relay(reader, writer, direction, connection, output, fixture):
    parser = Frames(14 if direction == 'C2S' else 15)
    while data := await reader.read(65536):
        for frame in parser.feed(data):
            original = select(direction, frame)
            if original is not None:
                record(output, connection, 'original', **original)
            frames = fixture.transform(frame) if direction == 'S2C' else [(frame, None)]
            for outgoing, action in frames:
                writer.write(outgoing)
                await writer.drain()
                if action:
                    record(output, connection, 'fixture', action=action,
                           **select(direction, outgoing))
    if parser.pending:
        raise ValueError('trailing partial frame')
    if writer.can_write_eof():
        writer.write_eof()
        await writer.drain()


async def serve(args):
    if args.listen == args.backend:
        raise ValueError('frontend equals backend')
    load_profile(args.profile)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    active = False
    counter = 0
    with args.output.open('x', encoding='utf-8') as output:
        async def connected(reader, writer):
            nonlocal active, counter
            if active:
                writer.close()
                await writer.wait_closed()
                return
            active = True
            counter += 1
            connection = f'fixture-{counter:04d}'
            backend = None
            tasks = []
            try:
                profile = load_profile(args.profile)
                fixture = Fixture(profile)
                upstream, backend = await asyncio.open_connection('127.0.0.1', args.backend)
                record(output, connection, 'opened', profile=profile)
                tasks = [asyncio.create_task(relay(reader, backend, 'C2S', connection, output, fixture)),
                         asyncio.create_task(relay(upstream, writer, 'S2C', connection, output, fixture))]
                done, pending = await asyncio.wait(tasks, return_when=asyncio.FIRST_EXCEPTION)
                for task in done:
                    task.result()
                await asyncio.gather(*pending)
            except (ValueError, OSError, KeyError) as error:
                record(output, connection, 'failed_closed', reason=str(error))
            finally:
                for task in tasks:
                    task.cancel()
                await asyncio.gather(*tasks, return_exceptions=True)
                for stream in (writer, backend):
                    if stream is not None:
                        stream.close()
                        with contextlib.suppress(OSError):
                            await stream.wait_closed()
                active = False
                record(output, connection, 'closed')

        async with await asyncio.start_server(connected, '127.0.0.1', args.listen):
            print(f'READY fixture 127.0.0.1:{args.listen} -> 127.0.0.1:{args.backend}', flush=True)
            await asyncio.Future()


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--listen', type=int, required=True)
    parser.add_argument('--backend', type=int, required=True)
    parser.add_argument('--profile', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    try:
        asyncio.run(serve(parser.parse_args()))
    except KeyboardInterrupt:
        pass
