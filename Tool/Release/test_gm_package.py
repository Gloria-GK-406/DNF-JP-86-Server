"""A candidate must contain its actual GM assets, not merely publish successfully."""
import json
import os
from pathlib import Path
import shutil
import tempfile
import unittest
from unittest.mock import Mock, patch

import release


class GmPackageTests(unittest.TestCase):
    def test_missing_referenced_asset_rejects_candidate(self):
        with tempfile.TemporaryDirectory(prefix="dfo-gm-package-test-") as temporary:
            root = Path(temporary)
            (root / "Document").mkdir()
            for name in ("server-ci-release.md", "host-component-composition.md", "gm-web-admin.md",
                         "gm-official-mail.md", "gm-cera-grants.md", "gm-acceptance-list.md", "login-session-lifecycle.md"):
                (root / "Document" / name).write_text("Test documentation\n", encoding="utf-8")
            (root / "Web/gm-admin").mkdir(parents=True)

            def publish(args, **kwargs):
                if args[0] != "dotnet":
                    return
                output = Path(args[args.index("-o") + 1])
                (output / "Resources/Channel").mkdir(parents=True)
                config = {key: {"Enabled": False} for key in ("Networking", "LauncherAuth", "GmWeb")}
                (output / "appsettings.json").write_text(json.dumps(config), encoding="utf-8")
                (output / "experience.json").write_text("{}", encoding="utf-8")
                (output / "Resources/Channel/channel_info.etc").write_text("test", encoding="utf-8")
                (output / "wwwroot/gm").mkdir(parents=True)
                (output / "wwwroot/gm/index.html").write_text(
                    '<div id="root"></div><script src="/gm/assets/missing.js"></script>', encoding="utf-8")

            with patch.object(release, "ROOT", root), patch.object(release, "run", return_value="test-identity"), \
                    patch.object(release.subprocess, "run", side_effect=publish), patch.object(release, "smoke"), \
                    patch.dict(os.environ, RELEASE_VERSION="1.2.3", RELEASE_RID="win-x64" if os.name == "nt" else "linux-x64"):
                with self.assertRaises((ValueError, FileNotFoundError)):
                    release.package()
            self.assertFalse(list((root / "artifacts/release").glob("*")))

    def assets(self, root, reference="/gm/assets/app.js"):
        web = root / "wwwroot/gm"
        (web / "assets").mkdir(parents=True)
        (web / "index.html").write_text(
            f'<div id="root"></div><script src="{reference}"></script>'
            '<link rel="stylesheet" href="/gm/assets/app.css">', encoding="utf-8")
        (web / "assets/app.js").write_text("console.log('fixture');", encoding="utf-8")
        (web / "assets/app.css").write_text("body{}", encoding="utf-8")
        return web

    def test_valid_assets_match_fresh_source_but_stale_bytes_are_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            web = self.assets(root)
            source = root / "dist"
            shutil.copytree(web, source)
            self.assertEqual(3, len(release.gm_assets(root, source)))
            (source / "assets/app.js").write_text("changed build", encoding="utf-8")
            with self.assertRaises(ValueError):
                release.gm_assets(root, source)

    def test_external_or_directory_escaping_reference_is_rejected(self):
        for reference in ("https://example.invalid/app.js", "/gm/%2e%2e/outside.js"):
            with self.subTest(reference=reference), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                self.assets(root, reference)
                with self.assertRaises((ValueError, FileNotFoundError)):
                    release.gm_assets(root)

    def test_smoke_overrides_all_inherited_configuration_aliases(self):
        inherited = {
            "Database:Path": "external.db", "database__path": "another.db",
            "Networking:Enabled": "true", "LauncherAuth:Enabled": "true",
            "GmWeb:Enabled": "false", "GmWeb:ListenUrl": "http://external.invalid",
            "Pvf__ArchivePath": "external.pvf", "pvf:archivepath": "another.pvf",
            "PVF_ARCHIVE_PATH": "external.pvf", "gmweb:bootstrap:password": "test-only",
            "GmWeb__Bootstrap__LoginName": "test-only", "DOTNET_ENVIRONMENT": "Staging",
            "ASPNETCORE_ENVIRONMENT": "Staging", "PATH": "preserve-runtime-path",
        }
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.assets(root)
            process = Mock(returncode=1)
            process.poll.return_value = 1
            with patch.object(release.os, "environ", inherited), \
                    patch.object(release.subprocess, "Popen", return_value=process) as launch:
                with self.assertRaisesRegex(RuntimeError, "Published host exited"):
                    release.smoke(root, "win-x64" if os.name == "nt" else "linux-x64")
            spawned = launch.call_args.kwargs["env"]
            normalized = {}
            for key, value in spawned.items():
                normalized.setdefault(key.replace("__", ":").lower(), []).append(value)
            self.assertEqual(["false"], normalized["networking:enabled"])
            self.assertEqual(["false"], normalized["launcherauth:enabled"])
            self.assertEqual(["true"], normalized["gmweb:enabled"])
            self.assertEqual(1, len(normalized["database:path"]))
            self.assertEqual("smoke.db", Path(normalized["database:path"][0]).name)
            self.assertFalse(Path(normalized["database:path"][0]).exists())
            self.assertEqual(1, len(normalized["gmweb:listenurl"]))
            self.assertRegex(normalized["gmweb:listenurl"][0], r"^http://127\.0\.0\.1:\d+$")
            self.assertEqual([""], normalized["pvf:archivepath"])
            self.assertEqual([""], normalized["pvf_archive_path"])
            self.assertFalse(any(key.startswith("gmweb:bootstrap:") for key in normalized))
            self.assertEqual(["Production"], normalized["dotnet_environment"])
            self.assertEqual(["Production"], normalized["aspnetcore_environment"])
            self.assertEqual(["preserve-runtime-path"], normalized["path"])

    def runtime(self, root, settings=None):
        (root / "Resources/Channel").mkdir(parents=True)
        settings = settings or {key: {"Enabled": False} for key in ("Networking", "LauncherAuth", "GmWeb")}
        (root / "appsettings.json").write_text(json.dumps(settings), encoding="utf-8")
        (root / "experience.json").write_text("{}", encoding="utf-8")
        (root / "Resources/Channel/channel_info.etc").write_text("test", encoding="utf-8")

    def test_environment_files_and_database_sidecars_are_rejected(self):
        for name in (".env", ".env.local", "test.db", "test.db-wal", "Script.pvf", "startup.log", "appsettings.Staging.json"):
            with self.subTest(name=name), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                self.runtime(root)
                (root / name).write_text("fixture", encoding="utf-8")
                with self.assertRaises(ValueError):
                    release.verify_runtime_content(root)

    def test_default_disabled_and_case_insensitive_bootstrap_secret_guard(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.runtime(root)
            release.verify_runtime_content(root)
        for mutation in ({"GmWeb": {"Enabled": True}},
                         {"GmWeb": {"Enabled": False, "bootstrap": {"password": "test-only-secret"}}},
                         {"GmWeb:Bootstrap:Password": "test-only-secret"}):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                settings = {key: {"Enabled": False} for key in ("Networking", "LauncherAuth", "GmWeb")}
                settings.update(mutation)
                self.runtime(root, settings)
                with self.assertRaises(ValueError):
                    release.verify_runtime_content(root)


if __name__ == "__main__":
    unittest.main()
