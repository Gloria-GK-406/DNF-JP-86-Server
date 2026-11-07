import unittest
from unittest.mock import Mock, patch

from harness import Host


class HostShutdownTests(unittest.TestCase):
    def host(self, running, exit_code):
        host = object.__new__(Host)
        host.client = None
        host.log = Mock()
        host.process = Mock(returncode=exit_code)
        host.process.poll.return_value = None if running else exit_code
        return host

    def test_windows_intentional_termination_accepts_popen_terminate_exit_code(self):
        host = self.host(True, 1)
        with patch('harness.os.name', 'nt'):
            host.stop()
        self.assertIsNone(host.process)
        host.log.close.assert_called_once()

    def test_windows_host_that_already_crashed_is_still_rejected(self):
        host = self.host(False, 1)
        with patch('harness.os.name', 'nt'), self.assertRaisesRegex(AssertionError, 'exit code: 1'):
            host.stop()
        host.log.close.assert_called_once()

    def test_posix_nonzero_shutdown_is_still_rejected(self):
        host = self.host(True, 1)
        with patch('harness.os.name', 'posix'), self.assertRaisesRegex(AssertionError, 'exit code: 1'):
            host.stop()


if __name__ == '__main__':
    unittest.main()
