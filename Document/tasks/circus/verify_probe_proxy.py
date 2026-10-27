"""Synthetic relay test: fragmentation, unknown commands and labelled injection."""
import asyncio
import json
from pathlib import Path
import struct
import tempfile

from probe_proxy import Relay


async def main(auto_schedule=False):
    with tempfile.TemporaryDirectory(prefix="circus-relay-fixture-") as directory:
        relay = Relay(Path(directory) / "wire.jsonl", 0, auto_schedule)
        incoming = asyncio.Queue()
        async def backend(reader, writer):
            try:
                frame = await reader.readexactly(16)
                await incoming.put(frame)
                response = struct.pack("<BHIII", 0, 0x006C, 17, 0, 0)+b"AB"
                writer.write(response[:9])
                await writer.drain()
                writer.write(response[9:])
                await writer.drain()
                await reader.read()
            finally:
                writer.close()
                await writer.wait_closed()
        async with await asyncio.start_server(backend, "127.0.0.1", 0) as backend_server:
            relay.backend = backend_server.sockets[0].getsockname()[1]
            async with await asyncio.start_server(relay.accept, "127.0.0.1", 0) as front:
                async with await asyncio.start_server(relay.control, "127.0.0.1", 0) as control:
                    reader, writer = await asyncio.open_connection("127.0.0.1", front.sockets[0].getsockname()[1])
                    await asyncio.sleep(0)
                    while not relay.sessions:
                        await asyncio.sleep(0)
                    async def request(path, payload):
                        r, w = await asyncio.open_connection("127.0.0.1", control.sockets[0].getsockname()[1])
                        body = json.dumps(payload).encode()
                        w.write(f"POST {path} HTTP/1.1\r\nContent-Length: {len(body)}\r\n\r\n".encode()+body)
                        await w.drain()
                        result = await r.read()
                        w.close()
                        await w.wait_closed()
                        return result
                    assert b"400 Probe" in await request("/0282", {"session": 1, "first": 9, "second": 7})
                    assert b"200 Probe" in await request("/arm", {"session": 1, "enabled": True})
                    # Unknown request passes through unchanged; no EPLP semantic handler exists.
                    frame = struct.pack("<BHIIBH", 1, 0x0357, 16, 0, 0, 27)+b"XY"
                    writer.write(frame[:4])
                    await writer.drain()
                    writer.write(frame[4:])
                    await writer.drain()
                    assert await incoming.get() == frame
                    assert (await reader.readexactly(17))[15:] == b"AB"
                    if auto_schedule:
                        assert await reader.readexactly(31) == Relay.schedule_frame(1)
                    assert b"200 Probe" in await request("/schedule", {"session": 1, "state": 0})
                    assert await reader.readexactly(31) == Relay.schedule_frame(0)
                    assert b"400 Probe" in await request("/schedule", {"session": 1, "state": 99})
                    assert b"200 Probe" in await request("/0282", {"session": 1, "first": 9, "second": 7})
                    injection = await reader.readexactly(23)
                    assert injection == struct.pack("<BHIII", 0, 0x0282, 23, 0, 0)+struct.pack("<II", 9, 7)
                    writer.close()
                    await writer.wait_closed()
                    await asyncio.sleep(0.02)
        rows = [json.loads(line) for line in relay.log.read_text().splitlines()]
        assert any(r.get("direction") == "c2s" and r["type"] == 0x0357 and r["body_hex"] == "5859" for r in rows)
        assert sum(r.get("event") == "injected" for r in rows) == 2+int(auto_schedule)
        print(f"Synthetic relay verified (auto_schedule={auto_schedule}): fragmented frames unchanged, unknown request recorded, unarmed injection refused, schedule open/close injected and labelled. No game client ran.")


async def verify_forced_link():
    class Writer:
        def __init__(self):
            self.frames = []
        def write(self, frame):
            self.frames.append(frame)
        async def drain(self):
            pass
    def frame(kind, body=b"", selector=0):
        return struct.pack("<BHIII", selector, kind, 15+len(body), 0, 0)+body
    sequence = [frame(0x001C, struct.pack("<H", 4104)), frame(0x0023),
                frame(0x001C, struct.pack("<H", 4103)), frame(0x0023), frame(0x0023),
                frame(0x0017), frame(0x0023),
                frame(0x001C, struct.pack("<H", 4103)), frame(0x0023)]
    with tempfile.TemporaryDirectory(prefix="circus-forced-link-fixture-") as directory:
        for enabled in (False, True):
            relay = Relay(Path(directory) / f"{enabled}.jsonl", 0, False, enabled)
            reader = asyncio.StreamReader()
            reader.feed_data(b"".join(sequence))
            reader.feed_eof()
            writer = Writer()
            session = {"id": 1, "armed": True, "s2c_lock": asyncio.Lock()}
            try:
                await relay.transfer(reader, writer, session, "s2c")
            except asyncio.IncompleteReadError:
                pass
            injected = [f for f in writer.frames if struct.unpack_from("<H", f, 1)[0] == 0x0282]
            assert len(injected) == (2 if enabled else 0)
            assert all(f[15:] == struct.pack("<II", 4114, 0) for f in injected)
            assert [f for f in writer.frames if struct.unpack_from("<H", f, 1)[0] != 0x0282] == sequence
        print("Synthetic forced-link verified: only 4103, once per run, no replay after town return, original frames preserved, disabled by default. No client semantics proved.")


if __name__ == "__main__":
    asyncio.run(asyncio.wait_for(main(), 5))
    asyncio.run(asyncio.wait_for(main(True), 5))
    asyncio.run(asyncio.wait_for(verify_forced_link(), 5))
