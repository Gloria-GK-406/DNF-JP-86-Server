#!/usr/bin/env python3
"""Normal-party targeted proxy: accepted settings/party notifications plus LEAVE_PARTY."""

from __future__ import annotations

import argparse
import asyncio
import importlib.util
import sys
from pathlib import Path
from typing import Any


BASE_HELPER = (
    Path(__file__).resolve().parents[3]
    / "spec-001-party-settings-foundation"
    / "evidence"
    / "task04"
    / "capture_proxy.py"
)
LEAVE_PARTY_TYPE = 0x000D


def load_base() -> Any:
    spec = importlib.util.spec_from_file_location("party_settings_capture_proxy", BASE_HELPER)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"cannot load accepted capture helper: {BASE_HELPER}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


base = load_base()
base_select_game_record = base.select_game_record


def select_game_record(
    direction: str, frame: bytes, header_length: int
) -> dict[str, Any] | None:
    if len(frame) < header_length:
        raise ValueError("game frame is shorter than its header")
    selector = frame[0]
    message_type = int.from_bytes(frame[1:3], "little")
    is_targeted_client_command = (
        direction == "C2S"
        and selector == 0x01
        and message_type in {base.SET_PARTY_INFO_TYPE, LEAVE_PARTY_TYPE}
    )
    is_targeted_server_notification = (
        direction == "S2C"
        and selector == 0x00
        and message_type in base.TARGETED_SERVER_NOTIFICATIONS
    )
    if not is_targeted_client_command and not is_targeted_server_notification:
        return None
    record = base_select_game_record(direction, frame, header_length)
    if record is not None:
        return record
    result: dict[str, Any] = {
        "direction": direction,
        "selector": f"0x{selector:02X}",
        "type": f"0x{message_type:04X}",
        "header_length": header_length,
        "total_length": len(frame),
        "body_length": len(frame) - header_length,
        "body_hex": frame[header_length:].hex().upper(),
        "frame_hex": frame.hex().upper(),
    }
    if direction == "C2S" and header_length == base.GAME_CLIENT_HEADER_LENGTH:
        result["sequence"] = int.from_bytes(frame[12:14], "little")
    return result


# StreamObserver resolves this symbol through the accepted helper module at runtime.
base.select_game_record = select_game_record


def run_self_test() -> None:
    base.run_self_test()
    leave = base._test_game_frame(
        1, LEAVE_PARTY_TYPE, b"", base.GAME_CLIENT_HEADER_LENGTH
    )
    ignored = base._test_game_frame(
        1, 0x0018, b"ignored", base.GAME_CLIENT_HEADER_LENGTH
    )
    same_type_server_noti = base._test_game_frame(
        0, LEAVE_PARTY_TYPE, b"unrelated", base.GAME_SERVER_HEADER_LENGTH
    )
    same_type_server_response = base._test_game_frame(
        1, base.SET_PARTY_INFO_TYPE, b"unrelated", base.GAME_SERVER_HEADER_LENGTH
    )
    wrong_selector_client_frame = base._test_game_frame(
        0, LEAVE_PARTY_TYPE, b"unrelated", base.GAME_CLIENT_HEADER_LENGTH
    )
    stream = base.FrameReassembler(base.GAME_CLIENT_HEADER_LENGTH)
    assert stream.feed(leave[:9]) == []
    rebuilt = stream.feed(leave[9:] + ignored)
    selected = [
        select_game_record("C2S", frame, base.GAME_CLIENT_HEADER_LENGTH)
        for frame in rebuilt
    ]
    selected = [record for record in selected if record is not None]
    assert len(selected) == 1
    assert selected[0]["type"] == "0x000D"
    assert selected[0]["body_length"] == 0
    assert (
        select_game_record("S2C", same_type_server_noti, base.GAME_SERVER_HEADER_LENGTH)
        is None
    )
    assert (
        select_game_record(
            "S2C", same_type_server_response, base.GAME_SERVER_HEADER_LENGTH
        )
        is None
    )
    assert (
        select_game_record(
            "C2S", wrong_selector_client_frame, base.GAME_CLIENT_HEADER_LENGTH
        )
        is None
    )
    print(
        "NORMAL_SELF_TEST_PASS: split/coalesced C2S/CMD empty-body "
        "0x000D allowlist and same-type S2C rejection"
    )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bind", default="127.0.0.1")
    parser.add_argument("--backend", default="127.0.0.1")
    parser.add_argument("--log", type=Path, default=Path("normal-targeted-frames.jsonl"))
    parser.add_argument("--self-test", action="store_true")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if args.self_test:
        run_self_test()
        return 0
    try:
        asyncio.run(base.serve(args.bind, args.backend, args.log))
    except KeyboardInterrupt:
        print("capture stopped", flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
