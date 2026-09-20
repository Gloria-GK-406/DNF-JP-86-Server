"""Read narrowly scoped A21 admission state without injecting or writing memory."""
import argparse
import ctypes as c
import datetime
import json
import struct
from pathlib import Path


class Reader:
    def __init__(self, pid):
        self.k = c.WinDLL('kernel32', use_last_error=True)
        self.k.OpenProcess.argtypes = [c.c_uint32, c.c_int, c.c_uint32]
        self.k.OpenProcess.restype = c.c_void_p
        self.k.ReadProcessMemory.argtypes = [c.c_void_p, c.c_void_p, c.c_void_p,
                                            c.c_size_t, c.POINTER(c.c_size_t)]
        self.k.CloseHandle.argtypes = [c.c_void_p]
        self.handle = self.k.OpenProcess(0x10, 0, pid)  # PROCESS_VM_READ only
        if not self.handle:
            raise OSError(c.get_last_error(), 'OpenProcess')

    def read(self, address, size):
        if not 0 <= size <= 16384:
            raise ValueError('Read exceeds the bounded probe size')
        if size == 0:
            return b''
        buffer = c.create_string_buffer(size)
        count = c.c_size_t()
        if not self.k.ReadProcessMemory(self.handle, address, buffer, size, c.byref(count)):
            raise OSError(c.get_last_error(), hex(address))
        if count.value != size:
            raise IOError('Short process read')
        return buffer.raw

    def u32(self, address):
        return struct.unpack('<I', self.read(address, 4))[0]

    def close(self):
        self.k.CloseHandle(self.handle)


def snapshot(reader):
    if reader.read(0x02730A50, 16).hex() != '558bec6aff68704de90264a100000000':
        raise ValueError('A21 code anchor mismatch; do not interpret offsets')
    u = reader.u32
    root = u(0x03A5B348)
    module = u(root + 0xB4)
    start, end = u(module + 0x3B0), u(module + 0x3B4)
    if not 0 <= end - start <= 116 * 128 or (end - start) % 116:
        raise ValueError('Invalid candidate container bounds')
    result = {'root': hex(root), 'select_module': hex(module),
              'fields': {hex(o): u(module + o) for o in (0x8C, 0x3D4, 0x3E0, 0x3E4, 0x44C)},
              'candidate_ids': [u(p) for p in range(start, end, 116)]}
    head = u(0x03A390EC + 12)
    node = u(head)
    definitions = {}
    seen = set()
    while node != head:
        if node in seen or len(seen) >= 2048:
            raise ValueError('Unexpected dungeon cache traversal')
        seen.add(node)
        next_node, _, key, wrapper = struct.unpack('<4I', reader.read(node, 16))
        if key in (150, 151, 152, 153, 154, 155, 8007):
            definition = wrapper + 0x140
            definitions[key] = {'wrapper': hex(wrapper), 'definition': hex(definition),
                                'id_at_34': u(definition + 0x34),
                                'minimum_level_at_88': u(definition + 0x88),
                                'flag_at_44c': reader.read(definition + 0x44C, 1)[0]}
        node = next_node
    result['definitions'] = definitions
    container = u(0x03A41E80 + 0x18)
    events = {}
    for key in (69, 75, 214):
        found = False
        if container:
            sentinel = u(container + 8)
            node = u(sentinel + 4)
            for _ in range(64):
                if reader.read(node + 0x17D, 1)[0]:
                    break
                value = u(node + 12)
                if value == key:
                    found = True
                    break
                node = u(node + (0 if key < value else 8))
            else:
                raise ValueError('Event tree traversal exceeds bound')
        events[key] = found
    result['events_present'] = events
    user = u(u(0x03A5C980) + 0x54)
    result['late_filter_gate'] = {'user': hex(user), 'user_plus_3f4': u(user + 0x3F4),
                                  'active_if_value_equals': 1}
    start, end = u(module + 0x534), u(module + 0x538)
    if not 0 <= end - start <= 4096 or (end - start) % 4:
        raise ValueError('Invalid deduplication bounds')
    result['deduplication_values'] = [u(p) for p in range(start, end, 4)]
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--pid', type=int, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    reader = Reader(args.pid)
    try:
        result = snapshot(reader)
        result.update(pid=args.pid, utc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                      method='PROCESS_VM_READ; non-atomic snapshot; no memory writes')
    finally:
        reader.close()
    args.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(result))
