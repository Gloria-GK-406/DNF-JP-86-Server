"""The release candidate carries the migration tool and checks it before archiving."""
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import release


class MigrationPackageTests(unittest.TestCase):
    def test_tool_is_published_into_its_own_directory_and_smoked_before_the_archive(self):
        with tempfile.TemporaryDirectory(prefix="dfo-migration-package-test-") as temporary:
            root = Path(temporary)
            (root / "Document").mkdir()
            for name in ("server-ci-release.md", "host-component-composition.md", "DatabaseSchemaMigration.md",
                         "gm-web-admin.md", "gm-official-mail.md", "gm-cera-grants.md", "gm-acceptance-list.md",
                         "login-session-lifecycle.md"):
                (root / "Document" / name).write_text("Test documentation\n", encoding="utf-8")
            published = []
            order = []

            def publish(args, **kwargs):
                if args[0] != "dotnet":
                    return
                output = Path(args[args.index("-o") + 1])
                published.append((args[2], output))
                output.mkdir(parents=True, exist_ok=True)
                if args[2].endswith("Dfo.Server.Host.csproj"):
                    (output / "Resources/Channel").mkdir(parents=True)
                    config = {key: {"Enabled": False} for key in ("Networking", "LauncherAuth", "GmWeb")}
                    (output / "appsettings.json").write_text(json.dumps(config), encoding="utf-8")
                    (output / "experience.json").write_text("{}", encoding="utf-8")
                    (output / "Resources/Channel/channel_info.etc").write_text("test", encoding="utf-8")

            rid = "win-x64" if os.name == "nt" else "linux-x64"
            with patch.object(release, "ROOT", root), patch.object(release, "run", return_value="test-identity"), \
                    patch.object(release, "prepare_frontend"), patch.object(release, "gm_assets", return_value={}), \
                    patch.object(release.subprocess, "run", side_effect=publish), \
                    patch.object(release, "smoke_migrator", side_effect=lambda *_: order.append("migrator")), \
                    patch.object(release, "smoke", side_effect=lambda *_: order.append("host")), \
                    patch.dict(os.environ, RELEASE_VERSION="1.2.3", RELEASE_RID=rid):
                release.package()

            host, tool = published
            self.assertTrue(host[0].endswith("Dfo.Server.Host.csproj"))
            self.assertEqual("Tool/DbMigrate/DbMigrate.csproj", tool[0])
            self.assertEqual(host[1] / "tools/DbMigrate", tool[1])
            self.assertEqual(["migrator", "host"], order)
            archives = list((root / "artifacts/release").glob("DNF-JP-86-Server-1.2.3-*"))
            self.assertTrue(any(archive.suffix in (".zip", ".gz") for archive in archives))


if __name__ == "__main__":
    unittest.main()
