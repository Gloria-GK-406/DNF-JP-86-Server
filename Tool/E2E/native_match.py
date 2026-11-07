"""Compare a captured expectation; dynamic exceptions must be declared in the baseline."""
import struct
import time


def compare_frame(expected, actual, now=None):
    selector, opcode, body = actual
    if (selector, opcode) != (expected['selector'], expected['opcode']):
        raise AssertionError(f"Expected {expected['selector']}:0x{expected['opcode']:04X}, got {selector}:0x{opcode:04X}")
    wanted = bytearray.fromhex(expected['bodyHex'])
    observed = bytearray(body)
    if len(wanted) != len(observed):
        raise AssertionError(f'0x{opcode:04X}: expected {len(wanted)} body bytes, got {len(observed)}')
    if 'unixTimeOffset' in expected:
        offset = expected['unixTimeOffset']
        if not 0 <= offset <= len(observed) - 4:
            raise ValueError('Invalid declared clock field')
        timestamp = struct.unpack_from('<i', observed, offset)[0]
        if abs(timestamp - (time.time() if now is None else now)) > 120:
            raise AssertionError('Initial notice clock differs from current real time by more than 120 seconds')
        wanted[offset:offset + 4] = observed[offset:offset + 4] = bytes(4)
    if wanted != observed:
        first = next(index for index, pair in enumerate(zip(wanted, observed)) if pair[0] != pair[1])
        raise AssertionError(f'0x{opcode:04X}: body differs at byte {first}; baseline was not updated')
