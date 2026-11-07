import unittest

from native_match import compare_frame


class NativeBaselineComparisonTests(unittest.TestCase):
    def test_changed_roster_cannot_pass_as_the_same_frame_type(self):
        expected = dict(selector=0, opcode=2, bodyHex='022000200000000000000000000000000000')
        body = bytes.fromhex(expected['bodyHex'])
        compare_frame(expected, (0, 2, body))
        with self.assertRaises(AssertionError):
            compare_frame(expected, (0, 2, body[:-1] + b'\x01'))

    def test_only_registered_clock_field_may_vary_and_it_must_be_current(self):
        expected = dict(selector=0, opcode=1, bodyHex='aa00000000bb', unixTimeOffset=1)
        compare_frame(expected, (0, 1, b'\xaa' + (100).to_bytes(4, 'little') + b'\xbb'), now=100)
        with self.assertRaises(AssertionError):
            compare_frame(expected, (0, 1, b'\xaa' + (1).to_bytes(4, 'little') + b'\xbb'), now=1000)
        with self.assertRaises(AssertionError):
            compare_frame(expected, (0, 1, b'\xaa' + (100).to_bytes(4, 'little') + b'\xbc'), now=100)

    def test_same_opcode_with_wrong_CMD_NOTI_family_is_refused(self):
        with self.assertRaises(AssertionError):
            compare_frame(dict(selector=0, opcode=2, bodyHex='01'), (1, 2, b'\x01'))


if __name__ == '__main__':
    unittest.main()
