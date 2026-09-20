"""Transparent localhost A21 Game capture; no login/chat/channel bodies persisted.

Run only after arranging an unused frontend/backend port pair. This tool never
changes host configuration, redirects the launcher, or injects/replaces packets.
The patched A21 transport must already use plaintext game frames.
"""
import argparse
import asyncio
import contextlib
import json
import struct
from datetime import datetime, timezone
from pathlib import Path

C2S = {0x000F, 0x0010, 0x0025, 0x002D, 0x0084}
S2C_CMD = {0x000F, 0x0010, 0x0025, 0x0084}
# A21 NOTI 0x0024 handler RVA 0x00D85070; see fatigue/static-evidence.md.
S2C_NOTI = {0x0005, 0x001B, 0x001C, 0x001D, 0x001E, 0x0024, 0x019F, 0x01EB}


class Frames:
    def __init__(self, header):
        self.header = header
        self.pending = bytearray()

    def feed(self, data):
        self.pending.extend(data)
        while len(self.pending) >= self.header:
            size = struct.unpack_from('<I', self.pending, 3)[0]
            if not self.header <= size <= 1024 * 1024:
                raise ValueError('invalid frame length; plaintext framing not established')
            if len(self.pending) < size:
                return
            frame = bytes(self.pending[:size])
            del self.pending[:size]
            yield frame


def select(direction, frame):
    header = 14 if direction == 'C2S' else 15
    if len(frame) < header or struct.unpack_from('<I', frame, 3)[0] != len(frame):
        raise ValueError('invalid complete frame')
    selector, opcode = frame[0], struct.unpack_from('<H', frame, 1)[0]
    # Only the fixed fatigue prefix of successful selection is needed. Do not
    # retain account/character identifiers, entitlements, quests or names.
    if direction == 'S2C' and selector == 1 and opcode == 4:
        body = frame[header:]
        if len(body) >= 17 and body[0] == 1:
            return {'direction': direction, 'selector': '0x01', 'type': '0x0004',
                    'header_length': header, 'body_length': len(body),
                    'capture_kind': 'selected_fields_only',
                    'body_slices': [{'offset': 11, 'length': 6,
                                     'hex': body[11:17].hex().upper()}]}
        return None
    accepted = (direction == 'C2S' and selector == 1 and opcode in C2S) or (
        direction == 'S2C' and ((selector == 1 and opcode in S2C_CMD)
                               or (selector == 0 and opcode in S2C_NOTI)))
    if not accepted:
        return None
    return {'direction': direction, 'selector': f'0x{selector:02X}',
            'type': f'0x{opcode:04X}', 'header_length': header,
            'body_length': len(frame) - header, 'body_hex': frame[header:].hex().upper()}


def status(output, connection, direction, state, reason):
    output.write(json.dumps({'timestamp_utc': datetime.now(timezone.utc).isoformat(),
                             'connection': connection, 'direction': direction,
                             'capture_status': state, 'reason': reason}) + '\n')
    output.flush()


async def relay(reader, writer, direction, connection, output):
    frames = Frames(14 if direction == 'C2S' else 15)
    enabled = True
    while data := await reader.read(65536):
        # Forward the original bytes, including all unrecorded frames.
        writer.write(data)
        await writer.drain()
        if enabled:
            try:
                for frame in frames.feed(data):
                    record = select(direction, frame)
                    if record is not None:
                        output.write(json.dumps({
                            'timestamp_utc': datetime.now(timezone.utc).isoformat(),
                            'connection': connection, **record}) + '\n')
                        output.flush()
            except (ValueError, OSError) as error:
                enabled = False
                frames.pending.clear()
                print(f'CAPTURE_INCOMPLETE {connection} {direction}: {error}', flush=True)
                status(output, connection, direction, 'incomplete', type(error).__name__)
    if frames.pending:
        print(f'CAPTURE_INCOMPLETE {connection} {direction}: trailing partial frame', flush=True)
        status(output, connection, direction, 'incomplete', 'trailing_partial_frame')
    status(output, connection, direction, 'eof', 'peer_closed_write')
    # A peer may half-close after its last request and still expect a response.
    if writer.can_write_eof():
        writer.write_eof()
        await writer.drain()


async def serve(args):
    if args.listen == args.backend:
        raise ValueError('frontend and backend ports must differ')
    args.output.parent.mkdir(parents=True, exist_ok=True)
    # Exclusive creation prevents mixing separate runs or overwriting evidence.
    with args.output.open('x', encoding='utf-8') as output:
        counter = 0

        async def connected(client_reader, client_writer):
            nonlocal counter
            counter += 1
            connection = f'game-{counter:04d}'
            server_writer = None
            tasks = []
            try:
                server_reader, server_writer = await asyncio.open_connection('127.0.0.1', args.backend)
                status(output, connection, 'both', 'opened', 'backend_connected')
                tasks = [asyncio.create_task(relay(client_reader, server_writer, 'C2S', connection, output)),
                         asyncio.create_task(relay(server_reader, client_writer, 'S2C', connection, output))]
                await asyncio.gather(*tasks)
            except OSError as error:
                print(f'CONNECTION_FAILED {connection}: {error}', flush=True)
                status(output, connection, 'both', 'incomplete', type(error).__name__)
            finally:
                for task in tasks:
                    task.cancel()
                results = await asyncio.gather(*tasks, return_exceptions=True)
                for result in results:
                    if isinstance(result, Exception):
                        print(f'CAPTURE_INCOMPLETE {connection}: {result}', flush=True)
                for writer in [client_writer, server_writer]:
                    if writer is not None:
                        writer.close()
                        with contextlib.suppress(OSError):
                            await writer.wait_closed()
                status(output, connection, 'both', 'closed', 'proxy_connection_ended')

        async with await asyncio.start_server(connected, '127.0.0.1', args.listen) as server:
            print(f'READY 127.0.0.1:{args.listen} -> 127.0.0.1:{args.backend}', flush=True)
            await server.serve_forever()


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--listen', type=int, required=True)
    parser.add_argument('--backend', type=int, required=True)
    parser.add_argument('--output', type=Path, required=True)
    try:
        asyncio.run(serve(parser.parse_args()))
    except KeyboardInterrupt:
        pass
