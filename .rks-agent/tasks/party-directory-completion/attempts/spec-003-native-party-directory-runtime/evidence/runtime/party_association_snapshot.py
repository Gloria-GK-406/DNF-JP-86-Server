#!/usr/bin/env python3
"""Capture a read-only CNUser/CParty association snapshot from one exact client."""

from __future__ import annotations

import argparse
import contextlib
from datetime import datetime, timezone
import json
from pathlib import Path

from directory_state_probe import (
    CNUSER_VTABLE_RVA,
    PARTY_VTABLE_RVA,
    find_exact_process,
    find_module_base,
    open_process_readonly,
    scan_cnuser_objects,
    scan_party_objects,
    _close_handle,
)


def build_snapshot(executable: Path) -> dict[str, object]:
    executable = executable.resolve(strict=True)
    process_id = find_exact_process(executable)
    if process_id is None:
        raise RuntimeError(f"exact executable is not running: {executable}")

    handle = open_process_readonly(process_id)
    try:
        module_base = find_module_base(process_id, executable)
        cnusers = scan_cnuser_objects(handle, module_base + CNUSER_VTABLE_RVA)
        parties = scan_party_objects(handle, module_base + PARTY_VTABLE_RVA)
    finally:
        _close_handle(handle)

    party_ids_by_address = {
        party.object_address: party.party_id for party in parties
    }
    cnuser_records = []
    for cnuser in cnusers:
        record = cnuser.to_json()
        record["associated_party_id"] = party_ids_by_address.get(
            cnuser.party_address
        )
        cnuser_records.append(record)

    return {
        "captured_at_utc": datetime.now(timezone.utc).isoformat(
            timespec="milliseconds"
        ),
        "process_id": process_id,
        "executable": str(executable),
        "module_base": f"0x{module_base:08X}",
        "process_access": "query+read only",
        "write_or_inject_access_requested": False,
        "cnuser_vtable": f"0x{module_base + CNUSER_VTABLE_RVA:08X}",
        "party_vtable": f"0x{module_base + PARTY_VTABLE_RVA:08X}",
        "cnusers": cnuser_records,
        "parties": [party.to_json() for party in parties],
    }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--exe", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    return parser.parse_args()


def main() -> int:
    arguments = parse_args()
    snapshot = build_snapshot(arguments.exe)
    arguments.output.parent.mkdir(parents=True, exist_ok=True)
    temporary = arguments.output.with_suffix(arguments.output.suffix + ".tmp")
    temporary.write_text(
        json.dumps(snapshot, ensure_ascii=True, indent=2) + "\n",
        encoding="utf-8",
    )
    temporary.replace(arguments.output)
    print(json.dumps(snapshot, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
