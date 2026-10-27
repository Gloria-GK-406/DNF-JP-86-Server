"""Loopback A21 plaintext research relay; injected frames are labelled, never replayed C2S.

Run only with the task-private passthrough client. No protocol hypothesis is handled
as gameplay: backend rejection/unknown requests remain visible and unchanged.
"""
import argparse
import asyncio
import datetime
import json
import os
from pathlib import Path
import struct
import uuid

MAX_FRAME = 0xAF000


class Relay:
    def __init__(self, log, backend, circus_schedule_on_select=False, force_circus_linked=False):
        self.log = log
        self.backend = backend
        self.sessions = {}
        self.serial = 0
        self.circus_schedule_on_select = circus_schedule_on_select
        self.force_circus_linked = force_circus_linked
        self.run_id = uuid.uuid4().hex

    @staticmethod
    def schedule_frame(state):
        if state not in (0, 1, 2):
            raise ValueError("Schedule state must be 0, 1 or 2")
        body = struct.pack("<IIII", 0, state, 4103, 0)
        return struct.pack("<BHIII", 0, 0x033F, 15+len(body), 0, 0)+body

    def record(self, **row):
        row["relay_run"] = self.run_id
        row["relay_pid"] = os.getpid()
        row["utc"] = datetime.datetime.now(datetime.timezone.utc).isoformat()
        with self.log.open("a", encoding="utf-8") as stream:
            stream.write(json.dumps(row, ensure_ascii=False) + "\n")

    async def transfer(self, reader, writer, session, direction):
        header_length = 14 if direction == "c2s" else 15
        while True:
            header = await reader.readexactly(header_length)
            selector, kind, total = struct.unpack_from("<BHI", header)
            if not header_length <= total <= MAX_FRAME:
                raise ValueError(f"Invalid frame length {total}; passthrough client required")
            body = await reader.readexactly(total-header_length)
            if direction == "s2c" and selector == 0 and kind == 0x001C and len(body) >= 2:
                session["dungeon"] = struct.unpack_from("<H", body)[0]
                session["linked_injected"] = False
            # Authentication and USERINFO bodies are withheld even when armed.
            capture = session["armed"] and kind not in (1, 2)
            self.record(session=session["id"], direction=direction, selector=selector,
                        type=kind, length=total, armed=session["armed"],
                        body_hex=body.hex() if capture else None,
                        sequence=struct.unpack_from("<H", header, 12)[0] if direction == "c2s" else None)
            async with session["s2c_lock"] if direction == "s2c" else session["c2s_lock"]:
                writer.write(header+body)
                await writer.drain()
                if (direction == "s2c" and selector == 0 and kind == 0x006C
                        and self.circus_schedule_on_select and not session.get("schedule_injected")):
                    # Explicit research option, after the normal event list and at the
                    # same stage as existing scheduled-dungeon projections. This is
                    # labelled injection, not an original backend notification.
                    session["schedule_injected"] = True
                    session["armed"] = True
                    self.record(event="arm", session=session["id"], enabled=True,
                                reason="explicit-circus-schedule-on-select")
                    injected = self.schedule_frame(1)
                    self.record(event="injected", session=session["id"], selector=0, type=0x033F,
                                state=1, dungeon=4103, body_hex=injected[15:].hex(),
                                reason="balloon-visibility-differential")
                    writer.write(injected)
                    await writer.drain()
                if (direction == "s2c" and selector == 0 and kind == 0x0023
                        and self.force_circus_linked and session.get("dungeon") == 4103
                        and not session.get("linked_injected")):
                    # User-authorized 100% research outcome. Field meanings remain
                    # the upstream hypothesis; no continuation command is assumed.
                    session["linked_injected"] = True
                    injected_body = struct.pack("<II", 4114, 0)
                    injected = struct.pack("<BHIII", 0, 0x0282, 23, 0, 0)+injected_body
                    self.record(event="injected", session=session["id"], selector=0, type=0x0282,
                                body_hex=injected_body.hex(), probability=100, candidate=4114,
                                phase="after-clear-reward", reason="forced-linked-hypothesis")
                    writer.write(injected)
                    await writer.drain()
                if direction == "s2c" and selector == 0 and kind == 0x0017:
                    session["dungeon"] = None

    async def accept(self, reader, writer):
        self.serial += 1
        identifier = self.serial
        session = {"id": identifier, "armed": False, "writer": writer,
                   "s2c_lock": asyncio.Lock(), "c2s_lock": asyncio.Lock()}
        tasks = []
        backend_writer = None
        self.sessions[identifier] = session
        self.record(event="open", session=identifier)
        try:
            backend_reader, backend_writer = await asyncio.open_connection("127.0.0.1", self.backend)
            tasks = [asyncio.create_task(self.transfer(reader, backend_writer, session, "c2s")),
                     asyncio.create_task(self.transfer(backend_reader, writer, session, "s2c"))]
            done, _ = await asyncio.wait(tasks, return_when=asyncio.FIRST_COMPLETED)
            for task in done:
                task.result()
        except (OSError, asyncio.IncompleteReadError, ValueError) as error:
            self.record(event="close", session=identifier, reason=str(error))
        finally:
            self.sessions.pop(identifier, None)
            for task in tasks:
                task.cancel()
            await asyncio.gather(*tasks, return_exceptions=True)
            for connection in (writer, backend_writer):
                if connection:
                    connection.close()
                    await connection.wait_closed()

    async def control(self, reader, writer):
        status = 200
        result = {}
        try:
            raw = await asyncio.wait_for(reader.readuntil(b"\r\n\r\n"), 3)
            if len(raw) > 8192:
                raise ValueError("Header too large")
            lines = raw.decode("ascii").split("\r\n")
            method, path, _ = lines[0].split(" ")
            headers = dict(line.split(":", 1) for line in lines[1:] if ":" in line)
            size = int(next((value for key, value in headers.items() if key.lower() == "content-length"), "0"))
            if not 0 <= size <= 1024:
                raise ValueError("Body too large")
            payload = json.loads(await reader.readexactly(size)) if size else {}
            if method == "GET" and path == "/status":
                result = {"sessions": [{"id": s["id"], "armed": s["armed"], "dungeon": s.get("dungeon"),
                                        "linked_injected": s.get("linked_injected", False)} for s in self.sessions.values()],
                          "backend": self.backend, "log": str(self.log),
                          "force_circus_linked": self.force_circus_linked}
            elif method == "POST" and path == "/scenario":
                enabled = payload["force_circus_linked"]
                if not isinstance(enabled, bool):
                    raise ValueError("force_circus_linked must be a boolean")
                self.force_circus_linked = enabled
                self.record(event="scenario", force_circus_linked=enabled)
                result = {"ok": True, "force_circus_linked": enabled}
            elif method == "POST" and path in ("/arm", "/mark", "/0282", "/schedule"):
                session = self.sessions[int(payload["session"])]
                if path == "/arm":
                    if not isinstance(payload["enabled"], bool):
                        raise ValueError("enabled must be a boolean")
                    session["armed"] = payload["enabled"]
                    self.record(event="arm", session=session["id"], enabled=session["armed"])
                elif path == "/mark":
                    label = str(payload["label"])
                    if len(label) > 200:
                        raise ValueError("Label too long")
                    self.record(event="mark", session=session["id"], label=label)
                else:
                    if not session["armed"]:
                        raise ValueError("Arm and label the experiment before injecting")
                    if path == "/0282":
                        first, second = int(payload["first"]), int(payload["second"])
                        body = struct.pack("<II", first, second)
                        frame = struct.pack("<BHIII", 0, 0x0282, 15+len(body), 0, 0)+body
                    else:
                        frame = self.schedule_frame(int(payload["state"]))
                    async with session["s2c_lock"]:
                        self.record(event="injected", session=session["id"], selector=0,
                                    type=struct.unpack_from("<H", frame, 1)[0], body_hex=frame[15:].hex())
                        session["writer"].write(frame)
                        await session["writer"].drain()
                result = {"ok": True, "session": session["id"]}
            else:
                status, result = 404, {"error": "Unknown control"}
        except (ValueError, KeyError, UnicodeError, struct.error, OSError, asyncio.TimeoutError,
                asyncio.IncompleteReadError, asyncio.LimitOverrunError) as error:
            status, result = 400, {"error": str(error)}
        body = json.dumps(result).encode()
        writer.write(f"HTTP/1.1 {status} Probe\r\nContent-Type: application/json\r\nContent-Length: {len(body)}\r\nConnection: close\r\n\r\n".encode()+body)
        await writer.drain()
        writer.close()
        await writer.wait_closed()


async def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--front", type=int, default=22011)
    parser.add_argument("--backend", type=int, default=22001)
    parser.add_argument("--control", type=int, default=22082)
    parser.add_argument("--allow-default-front", action="store_true")
    parser.add_argument("--circus-schedule-on-select", action="store_true")
    parser.add_argument("--force-circus-linked", action="store_true")
    args = parser.parse_args()
    if (not 22000 <= args.front <= 22999 and not (args.allow_default_front and args.front == 10011)
            or any(not 22000 <= p <= 22999 for p in (args.backend, args.control))):
        parser.error("Ports must remain in 22000-22999")
    root = args.root.resolve()
    workspace = Path(__file__).resolve().parents[3]
    if not root.is_relative_to(workspace / "artifacts"):
        parser.error("Private runtime must remain inside task artifacts")
    root.mkdir(parents=True, exist_ok=True)
    relay = Relay(root / "wire.jsonl", args.backend, args.circus_schedule_on_select, args.force_circus_linked)
    async with await asyncio.start_server(relay.accept, "127.0.0.1", args.front) as game:
        async with await asyncio.start_server(relay.control, "127.0.0.1", args.control) as control:
            print(json.dumps({"front": args.front, "backend": args.backend, "control": args.control}), flush=True)
            await asyncio.gather(game.serve_forever(), control.serve_forever())


if __name__ == "__main__":
    asyncio.run(main())
