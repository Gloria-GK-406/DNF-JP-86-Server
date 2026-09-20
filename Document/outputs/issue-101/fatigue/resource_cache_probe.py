"""Read A21 DGN/WDM resource caches with PROCESS_VM_READ only."""

import argparse
import json
import struct
import sys
from pathlib import Path


HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "client"))
from read_admission_state import Reader  # noqa: E402


CODE_ANCHOR = bytes.fromhex("558bec6aff68704de90264a100000000")


def read_utf16z(reader, address, limit=2048):
    data = reader.read(address, limit)
    end = 0
    while end + 1 < len(data) and data[end:end + 2] != b"\0\0":
        end += 2
    return data[:end].decode("utf-16le", errors="replace")


def enumerate_resource_cache(reader, manager_global):
    manager = reader.u32(manager_global)
    buckets = reader.u32(manager + 0x208)
    bucket_count = reader.u32(manager + 0x20C)
    item_count = reader.u32(manager + 0x210)
    if not 0 < bucket_count <= 4096 or not 0 <= item_count <= 65536:
        raise ValueError("Invalid resource-cache bounds")

    rows = []
    seen_nodes = set()
    for index in range(bucket_count):
        predecessor_link = reader.u32(buckets + 4 * index)
        if not predecessor_link:
            continue
        link = reader.u32(predecessor_link)
        local_links = set()
        while link and link not in local_links:
            local_links.add(link)
            node = link - 0x0C
            path_pointer, _, wrapper, next_link, hash_value = struct.unpack(
                "<5I", reader.read(node, 20)
            )
            if hash_value % bucket_count != index:
                break
            seen_nodes.add(node)
            raw = reader.u32(wrapper)
            rows.append({
                "bucket": index,
                "path": read_utf16z(reader, path_pointer),
                "node": hex(node),
                "wrapper": hex(wrapper),
                "raw": hex(raw),
            })
            link = next_link
            if len(local_links) > item_count + 1:
                raise ValueError("Resource-cache traversal exceeds bound")
    if len(seen_nodes) != item_count:
        raise ValueError(f"Resource-cache count mismatch: {len(seen_nodes)} != {item_count}")
    return {
        "manager_global": hex(manager_global),
        "manager": hex(manager),
        "type": reader.u32(manager + 0x23C),
        "buckets": hex(buckets),
        "bucket_count": bucket_count,
        "item_count": item_count,
        "resources": rows,
    }


def decode_tag_cache(reader, wanted_descriptors):
    cache = reader.u32(0x03B8960C)
    buckets = reader.u32(cache)
    bucket_count = reader.u32(cache + 4)
    item_count = reader.u32(cache + 8)
    found = {}
    for index in range(bucket_count):
        predecessor_link = reader.u32(buckets + 4 * index)
        if not predecessor_link:
            continue
        link = reader.u32(predecessor_link)
        local_links = set()
        while link and link not in local_links:
            local_links.add(link)
            node = link - 8
            descriptor, decoded, next_link, hash_value = struct.unpack(
                "<4I", reader.read(node, 16)
            )
            if hash_value % bucket_count != index:
                break
            if descriptor in wanted_descriptors:
                found[hex(descriptor)] = read_utf16z(reader, decoded, 512)
            link = next_link
            if len(local_links) > item_count + 1:
                raise ValueError("Tag-cache traversal exceeds bound")
    return found


def world_map_entries(reader, raw):
    begin = reader.u32(raw + 0x6C)
    end = reader.u32(raw + 0x70)
    capacity = reader.u32(raw + 0x74)
    if not begin <= end <= capacity or (end - begin) % 12:
        raise ValueError("Invalid WDM dungeon-vector bounds")
    if end - begin > 12 * 4096:
        raise ValueError("WDM dungeon vector exceeds bound")
    entries = []
    for address in range(begin, end, 12):
        dungeon_id, qualifier_quest_id, in_progress_tag = struct.unpack(
            "<iii", reader.read(address, 12)
        )
        entries.append({
            "dungeon_id": dungeon_id,
            "qualifier_quest_id": qualifier_quest_id,
            "in_progress_tag": in_progress_tag,
        })
    return {
        "begin": hex(begin),
        "end": hex(end),
        "capacity": hex(capacity),
        "entries": entries,
    }


def snapshot(reader):
    if reader.read(0x02730A50, len(CODE_ANCHOR)) != CODE_ANCHOR:
        raise ValueError("A21 code anchor mismatch; do not interpret offsets")

    dgn = enumerate_resource_cache(reader, 0x03A5C940)
    wdm = enumerate_resource_cache(reader, 0x03A5C934)
    drift = next((row for row in wdm["resources"] if row["path"].lower() == "driftcave.wdm"), None)
    if drift is None:
        raise ValueError("driftcave.wdm is not present in the loaded WDM cache")
    drift_raw = int(drift["raw"], 16)

    for row in dgn["resources"]:
        raw = int(row["raw"], 16)
        row["added_required_item_at_1bc"] = reader.read(raw + 0x1BC, 1)[0]

    tags = decode_tag_cache(reader, {
        0x03659C78,  # [added required item]
        0x03659C50,  # [event dungeon]
        0x0364D978,  # [in progress]
        0x03122F00,  # [dungeon]
    })
    return {
        "method": "PROCESS_VM_READ only; no client calls; no memory writes",
        "dgn_cache": dgn,
        "wdm_cache": wdm,
        "decoded_tags": tags,
        "driftcave": {
            "raw": hex(drift_raw),
            "dungeon_vector": world_map_entries(reader, drift_raw),
        },
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--pid", type=int, required=True)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    reader = Reader(args.pid)
    try:
        result = snapshot(reader)
    finally:
        reader.close()
    rendered = json.dumps(result, ensure_ascii=False, indent=2) + "\n"
    if args.output:
        args.output.write_text(rendered, encoding="utf-8")
    print(rendered, end="")


if __name__ == "__main__":
    main()
