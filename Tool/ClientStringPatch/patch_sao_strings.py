"""Patch two confirmed SAO translations in dstr.dat's Chinese FILE/102 resource.

Windows only. Requires pefile for independent readback; writes via Win32 resources.
Never edits the source or accepts an already patched/unfamiliar translation.
"""
import argparse
import ctypes
from ctypes import wintypes
import hashlib
import json
from pathlib import Path
import re
import struct

import pefile

TRANSLATIONS = {38379: '%s 第 %d 层', 38386: '第 %d 层已被通关。'}
TARGET = ('FILE', 102, 1042)


def resources(path):
    with pefile.PE(str(path)) as pe:
        result = {}
        for kind in pe.DIRECTORY_ENTRY_RESOURCE.entries:
            for name in kind.directory.entries:
                for language in name.directory.entries:
                    entry = language.data.struct
                    key = (str(kind.name) if kind.name else kind.id,
                           str(name.name) if name.name else name.id, language.id)
                    result[key] = pe.get_data(entry.OffsetToData, entry.Size)
        return result


def decode(data):
    if data[:4] != b'SC01' or (len(data) - 8) % 4:
        raise ValueError('Unsupported string resource framing')
    size = struct.unpack_from('<I', data, 4)[0]
    plain = bytearray()
    previous = 0x5819AF17
    for offset in range(8, len(data), 4):
        value = struct.unpack_from('<I', data, offset)[0]
        plain.extend(struct.pack('<I', value ^ previous))
        previous = value
    if not 0 < size <= len(plain):
        raise ValueError('Invalid decoded string length')
    return bytes(plain[:size])


def encode(plain):
    padded = plain + b'\0' * (-len(plain) % 4)
    result = bytearray(b'SC01' + struct.pack('<I', len(plain)))
    previous = 0x5819AF17
    for offset in range(0, len(padded), 4):
        previous ^= struct.unpack_from('<I', padded, offset)[0]
        result.extend(struct.pack('<I', previous))
    return bytes(result)


def patch(source, output):
    if source.resolve() == output.resolve() or output.exists() or Path(str(output) + '.json').exists():
        raise ValueError('Refuse in-place editing or overwriting output')
    before = resources(source)
    original = decode(before[TARGET])
    changed = original
    for key, text in TRANSLATIONS.items():
        pattern = rb'(?m)^' + str(key).encode() + rb'>dnf_' + str(key).encode() + rb'(?=\r?$)'
        changed, count = re.subn(pattern, lambda _: f'{key}>{text}'.encode('utf-8'), changed)
        if count != 1:
            raise ValueError(f'Expected exactly one original placeholder for {key}, found {count}')
    payload = encode(changed)
    assert decode(payload) == changed
    output.write_bytes(source.read_bytes())
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    begin = kernel.BeginUpdateResourceW
    begin.argtypes, begin.restype = (wintypes.LPCWSTR, wintypes.BOOL), wintypes.HANDLE
    update = kernel.UpdateResourceW
    update.argtypes = (wintypes.HANDLE, wintypes.LPCWSTR, ctypes.c_void_p,
                       wintypes.WORD, ctypes.c_void_p, wintypes.DWORD)
    update.restype = wintypes.BOOL
    end = kernel.EndUpdateResourceW
    end.argtypes, end.restype = (wintypes.HANDLE, wintypes.BOOL), wintypes.BOOL
    handle = begin(str(output.resolve()), False)
    if not handle:
        raise ctypes.WinError(ctypes.get_last_error())
    buffer = ctypes.create_string_buffer(payload)
    if not update(handle, TARGET[0], TARGET[1], TARGET[2], buffer, len(payload)):
        error = ctypes.get_last_error()
        end(handle, True)
        raise ctypes.WinError(error)
    if not end(handle, False):
        raise ctypes.WinError(ctypes.get_last_error())
    after = resources(output)
    if before.keys() != after.keys() or any(after[key] != value for key, value in before.items() if key != TARGET):
        raise ValueError('Unrelated resources changed')
    if after[TARGET] != payload or decode(after[TARGET]) != changed:
        raise ValueError('Resource readback mismatch')
    # Independent PE code-section guard in addition to the complete resource comparison.
    with pefile.PE(str(source)) as old, pefile.PE(str(output)) as new:
        for section in old.sections:
            if section.Name.rstrip(b'\0') == b'.rsrc':
                continue
            matching = [x for x in new.sections if x.Name == section.Name]
            if len(matching) != 1 or matching[0].get_data() != section.get_data():
                raise ValueError('Non-resource section changed')
    manifest = {'Source': str(source.resolve()), 'Output': str(output.resolve()),
                'SourceSHA256': hashlib.sha256(source.read_bytes()).hexdigest(),
                'OutputSHA256': hashlib.sha256(output.read_bytes()).hexdigest(),
                'Resource': list(TARGET), 'Translations': TRANSLATIONS,
                'OtherResourcesPreserved': True, 'OtherSectionsPreserved': True}
    Path(str(output) + '.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(manifest, ensure_ascii=False))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('output', type=Path)
    arguments = parser.parse_args()
    patch(arguments.source, arguments.output)
