#!/usr/bin/env python3
"""Transparent A21 proxy that records only the party-directory evidence allowlist.

Channel bytes are forwarded but never persisted. Game bytes are reassembled in
memory and only the exact direction/selector/type combinations required by the
native party-directory run are written as JSONL.
"""

from __future__ import annotations

import argparse
import asyncio
import contextlib
import itertools
import json
import os
import struct
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, BinaryIO


CHANNEL_HEADER_LENGTH = 11
GAME_CLIENT_HEADER_LENGTH = 14
GAME_SERVER_HEADER_LENGTH = 15
MAXIMUM_FRAME_LENGTH = 1024 * 1024

C2S_ALLOWLIST = {0x0004, 0x000C, 0x000D}
S2C_ALLOWLIST = {0x0002, 0x0006, 0x0009}


class FrameReassembler:
    def __init__(self, header_length: int, total_length_offset: int = 3) -> None:
        self.header_length = header_length
        self.total_length_offset = total_length_offset
        self.buffer = bytearray()

    def feed(self, data: bytes) -> list[bytes]:
        self.buffer.extend(data)
        frames: list[bytes] = []
        while len(self.buffer) >= self.header_length:
            total_length = int.from_bytes(
                self.buffer[
                    self.total_length_offset : self.total_length_offset + 4
                ],
                "little",
            )
            if not self.header_length <= total_length <= MAXIMUM_FRAME_LENGTH:
                raise ValueError(
                    f"invalid total length {total_length} for "
                    f"{self.header_length}-byte header"
                )
            if len(self.buffer) < total_length:
                break
            frames.append(bytes(self.buffer[:total_length]))
            del self.buffer[:total_length]
        return frames


@dataclass(frozen=True)
class RecordingPolicy:
    protocol: str
    direction: str

    @property
    def may_record(self) -> bool:
        return self.protocol == "game" and self.direction in {"C2S", "S2C"}


def select_game_record(
    direction: str, frame: bytes, header_length: int
) -> dict[str, Any] | None:
    if len(frame) < header_length:
        raise ValueError("game frame is shorter than its header")

    selector = frame[0]
    message_type = int.from_bytes(frame[1:3], "little")
    accepted = (
        direction == "C2S"
        and selector == 0x01
        and message_type in C2S_ALLOWLIST
    ) or (
        direction == "S2C"
        and selector == 0x00
        and message_type in S2C_ALLOWLIST
    )
    if not accepted:
        return None

    record: dict[str, Any] = {
        "direction": direction,
        "selector": f"0x{selector:02X}",
        "type": f"0x{message_type:04X}",
        "header_length": header_length,
        "total_length": len(frame),
        "body_length": len(frame) - header_length,
        "body_hex": frame[header_length:].hex().upper(),
        "frame_hex": frame.hex().upper(),
    }
    if direction == "C2S" and header_length == GAME_CLIENT_HEADER_LENGTH:
        record["sequence"] = int.from_bytes(frame[12:14], "little")
    return record


@dataclass(frozen=True)
class ProxySpec:
    name: str
    listen_port: int
    backend_port: int
    protocol: str


PROXY_SPECS = (
    ProxySpec("channel", 7001, 17001, "channel"),
    ProxySpec("game-10010", 10010, 20010, "game"),
    ProxySpec("game-10011", 10011, 20011, "game"),
)


class TargetedRecorder:
    def __init__(self, path: Path) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        self._stream: BinaryIO = path.open("ab", buffering=0)
        self._event_numbers = itertools.count(1)

    def write(self, connection: str, listener: int, record: dict[str, Any]) -> None:
        envelope = {
            "timestamp_utc": datetime.now(timezone.utc).isoformat(
                timespec="milliseconds"
            ),
            "event_sequence": next(self._event_numbers),
            "connection": connection,
            "listener": listener,
            **record,
        }
        encoded = json.dumps(envelope, ensure_ascii=True, separators=(",", ":"))
        self._stream.write(encoded.encode("utf-8") + b"\n")

    def close(self) -> None:
        self._stream.close()


class StreamObserver:
    def __init__(
        self,
        protocol: str,
        direction: str,
        connection: str,
        listener: int,
        recorder: TargetedRecorder,
    ) -> None:
        self.protocol = protocol
        self.direction = direction
        self.connection = connection
        self.listener = listener
        self.recorder = recorder
        self.disabled = False
        self.policy = RecordingPolicy(protocol, direction)
        if protocol == "channel":
            self.reassembler = FrameReassembler(
                CHANNEL_HEADER_LENGTH, total_length_offset=2
            )
        else:
            header_length = (
                GAME_CLIENT_HEADER_LENGTH
                if direction == "C2S"
                else GAME_SERVER_HEADER_LENGTH
            )
            self.reassembler = FrameReassembler(header_length)

    def consume(self, data: bytes) -> None:
        if self.disabled:
            return
        try:
            frames = self.reassembler.feed(data)
        except ValueError as error:
            self.disabled = True
            print(
                f"capture-disabled connection={self.connection} "
                f"direction={self.direction} reason={error}",
                flush=True,
            )
            return

        if not self.policy.may_record:
            return
        header_length = self.reassembler.header_length
        for frame in frames:
            record = select_game_record(self.direction, frame, header_length)
            if record is not None:
                self.recorder.write(self.connection, self.listener, record)


async def _close_writer(writer: asyncio.StreamWriter) -> None:
    writer.close()
    with contextlib.suppress(ConnectionError, OSError, asyncio.CancelledError):
        await writer.wait_closed()


async def proxy_connection(
    client_reader: asyncio.StreamReader,
    client_writer: asyncio.StreamWriter,
    spec: ProxySpec,
    backend_host: str,
    recorder: TargetedRecorder,
    connection_numbers: itertools.count,
) -> None:
    connection = f"{spec.name}-{next(connection_numbers):04d}"
    try:
        server_reader, server_writer = await asyncio.open_connection(
            backend_host, spec.backend_port
        )
    except OSError as error:
        print(
            f"backend-connect-failed connection={connection} "
            f"port={spec.backend_port} reason={error}",
            flush=True,
        )
        await _close_writer(client_writer)
        return

    client_peer = client_writer.get_extra_info("peername")
    backend_local = server_writer.get_extra_info("sockname")
    print(
        f"connection-open connection={connection} listener={spec.listen_port} "
        f"client={client_peer} backend_local={backend_local} "
        f"backend={backend_host}:{spec.backend_port}",
        flush=True,
    )

    c2s = StreamObserver(
        spec.protocol, "C2S", connection, spec.listen_port, recorder
    )
    s2c = StreamObserver(
        spec.protocol, "S2C", connection, spec.listen_port, recorder
    )

    async def pump(
        reader: asyncio.StreamReader,
        writer: asyncio.StreamWriter,
        observer: StreamObserver,
    ) -> None:
        while data := await reader.read(64 * 1024):
            observer.consume(data)
            writer.write(data)
            await writer.drain()

    tasks = {
        asyncio.create_task(pump(client_reader, server_writer, c2s)),
        asyncio.create_task(pump(server_reader, client_writer, s2c)),
    }
    try:
        _, pending = await asyncio.wait(tasks, return_when=asyncio.FIRST_COMPLETED)
        for task in pending:
            task.cancel()
        await asyncio.gather(*tasks, return_exceptions=True)
    finally:
        print(f"connection-close connection={connection}", flush=True)
        await _close_writer(server_writer)
        await _close_writer(client_writer)


def _write_ready(path: Path, bind_host: str, backend_host: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "pid": os.getpid(),
        "bind": bind_host,
        "backend": backend_host,
        "listeners": [
            {
                "name": spec.name,
                "listen_port": spec.listen_port,
                "backend_port": spec.backend_port,
                "protocol": spec.protocol,
            }
            for spec in PROXY_SPECS
        ],
        "channel_persisted": False,
        "c2s_allowlist": [f"0x{value:04X}" for value in sorted(C2S_ALLOWLIST)],
        "s2c_allowlist": [f"0x{value:04X}" for value in sorted(S2C_ALLOWLIST)],
    }
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    temporary.replace(path)


async def serve(
    bind_host: str, backend_host: str, log_path: Path, ready_path: Path | None
) -> None:
    recorder = TargetedRecorder(log_path)
    connection_numbers = itertools.count(1)
    servers: list[asyncio.AbstractServer] = []
    try:
        for spec in PROXY_SPECS:
            server = await asyncio.start_server(
                lambda reader, writer, current=spec: proxy_connection(
                    reader,
                    writer,
                    current,
                    backend_host,
                    recorder,
                    connection_numbers,
                ),
                bind_host,
                spec.listen_port,
            )
            servers.append(server)
            print(
                f"ready {spec.name} {bind_host}:{spec.listen_port} -> "
                f"{backend_host}:{spec.backend_port}",
                flush=True,
            )
        if ready_path is not None:
            _write_ready(ready_path, bind_host, backend_host)
        await asyncio.gather(*(server.serve_forever() for server in servers))
    finally:
        if ready_path is not None:
            with contextlib.suppress(FileNotFoundError):
                ready_path.unlink()
        for server in servers:
            server.close()
        await asyncio.gather(
            *(server.wait_closed() for server in servers), return_exceptions=True
        )
        recorder.close()


def build_test_frame(
    selector: int, message_type: int, body: bytes, header_length: int
) -> bytes:
    header = bytearray(header_length)
    header[0] = selector
    struct.pack_into("<H", header, 1, message_type)
    struct.pack_into("<I", header, 3, header_length + len(body))
    if header_length == GAME_CLIENT_HEADER_LENGTH:
        struct.pack_into("<H", header, 12, 7)
    return bytes(header) + body


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bind", default="127.0.0.1")
    parser.add_argument("--backend", default="127.0.0.1")
    parser.add_argument(
        "--log", type=Path, default=Path("directory-targeted-frames.jsonl")
    )
    parser.add_argument("--ready", type=Path)
    return parser.parse_args()


def main() -> int:
    arguments = parse_args()
    try:
        asyncio.run(
            serve(
                arguments.bind,
                arguments.backend,
                arguments.log,
                arguments.ready,
            )
        )
    except KeyboardInterrupt:
        print("capture stopped", flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
