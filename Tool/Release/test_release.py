"""Exercise tag ancestry against real Git histories, without touching repository tags."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import release


class TagGuardTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="dfo-tag-test-")
        self.root = Path(self.temporary.name)
        self.root_patch = patch.object(release, "ROOT", self.root)
        self.root_patch.start()
        self.output = self.root / "output.txt"
        self.git("init", "--initial-branch=main")
        self.git("-c", "user.name=CI Test", "-c", "user.email=ci@example.invalid",
                 "commit", "--allow-empty", "-m", "main baseline")
        self.git("update-ref", "refs/remotes/origin/main", "HEAD")

    def tearDown(self):
        self.root_patch.stop()
        self.temporary.cleanup()

    def git(self, *args):
        return release.run("git", *args)

    def verify(self, tag):
        with patch.dict(os.environ, GITHUB_REF_NAME=tag, GITHUB_OUTPUT=str(self.output)):
            release.verify_tag()

    def test_annotated_main_tag_is_accepted(self):
        self.git("-c", "user.name=CI Test", "-c", "user.email=ci@example.invalid",
                 "tag", "-a", "v1.2.3", "-m", "release")
        self.verify("v1.2.3")
        self.assertEqual("version=1.2.3\nprerelease=false\n", self.output.read_text())

    def test_prerelease_on_main_is_accepted(self):
        self.git("tag", "v1.2.3-rc.1")
        self.verify("v1.2.3-rc.1")
        self.assertIn("prerelease=true", self.output.read_text())

    def test_unmerged_feature_tag_is_rejected(self):
        self.git("switch", "-c", "feature")
        self.git("-c", "user.name=CI Test", "-c", "user.email=ci@example.invalid",
                 "commit", "--allow-empty", "-m", "unmerged")
        self.git("tag", "v1.2.3")
        with self.assertRaises(subprocess.CalledProcessError):
            self.verify("v1.2.3")
        self.assertFalse(self.output.exists())

    def test_invalid_version_is_rejected(self):
        with self.assertRaises(ValueError):
            self.verify("vlatest")
        self.assertFalse(self.output.exists())

    def test_checkout_mismatch_is_rejected(self):
        self.git("tag", "v1.2.3")
        self.git("-c", "user.name=CI Test", "-c", "user.email=ci@example.invalid",
                 "commit", "--allow-empty", "-m", "different checkout")
        self.git("update-ref", "refs/remotes/origin/main", "HEAD")
        with self.assertRaises(ValueError):
            self.verify("v1.2.3")
        self.assertFalse(self.output.exists())


if __name__ == "__main__":
    unittest.main()
