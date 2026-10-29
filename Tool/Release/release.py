"""CI release tooling. Uses only Python's standard library; never touches a live deployment."""
from contextlib import contextmanager
from html.parser import HTMLParser
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import socket
import sqlite3
import subprocess
import sys
import tarfile
import tempfile
import time
from urllib.error import HTTPError
from urllib.parse import unquote, urlsplit
from urllib.request import ProxyHandler, build_opener
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[2]
VERSION = re.compile(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?")


def run(*args):
    return subprocess.check_output(args, cwd=ROOT, text=True, encoding="utf-8").strip()


def verify_tag():
    tag = os.environ["GITHUB_REF_NAME"]
    version = tag.removeprefix("v")
    if not tag.startswith("v") or not VERSION.fullmatch(version):
        raise ValueError("Expected vMAJOR.MINOR.PATCH or vMAJOR.MINOR.PATCH-prerelease")
    commit = run("git", "rev-parse", f"refs/tags/{tag}^{{commit}}")
    if commit != run("git", "rev-parse", "HEAD"):
        raise ValueError("Checkout and tag do not identify the same commit")
    subprocess.run(["git", "merge-base", "--is-ancestor", commit, "origin/main"], cwd=ROOT, check=True)
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
        output.write(f"version={version}\nprerelease={str('-' in version).lower()}\n")
    print(f"Validated {tag}: {commit} belongs to main history")


def summary(directory, *, unit_only=False):
    reports = sorted(Path(directory).rglob("*.trx"))
    if not reports:
        raise RuntimeError("No TRX reports were produced; validation did not complete")
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    executed = 0
    title = "Linux unit test results" if unit_only else "Test results"
    lines = [f"## {title}", "", "| Report | Passed | Failed | Skipped |", "|---|---:|---:|---:|"]
    for report in reports:
        tree = ET.parse(report)
        counters = tree.find(".//t:Counters", ns)
        if counters is None:
            raise ValueError(f"No counters in {report}")
        executed += int(counters.get("executed", "0"))
        skipped = sum(result.get("outcome") == "NotExecuted" for result in tree.findall(".//t:UnitTestResult", ns))
        lines.append(f"| {report.name} | {counters.get('passed', '0')} | {counters.get('failed', '0')} | {skipped} |")
    if unit_only and executed == 0:
        raise RuntimeError("The Unit filter executed no tests; the PR gate cannot pass empty")
    scope = ("Only Category=Unit tests ran on Linux. Integration, real sockets, SQLite, Host, archive and package validation run in Release."
             if unit_only else "PVF is not distributed through GitHub Actions. Content-dependent tests explicitly skip without Script.pvf; this is not client/gameplay acceptance.")
    lines += ["", scope]
    text = "\n".join(lines) + "\n"
    print(text)
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as output:
            output.write(text)


@contextmanager
def temporary_directory(prefix):
    temporary = tempfile.TemporaryDirectory(prefix=prefix)
    try:
        yield temporary.name
    finally:
        for attempt in range(26):
            try:
                temporary.cleanup()
                break
            except OSError as error:
                if os.name != "nt" or error.winerror not in (5, 32) or attempt == 25:
                    raise
                time.sleep(0.2)


def prepare_frontend():
    npm = shutil.which("npm.cmd" if os.name == "nt" else "npm")
    if npm is None:
        raise FileNotFoundError("Node/npm is required to build the GM frontend, not to run the published host")
    frontend = ROOT / "Web/gm-admin"
    subprocess.run([npm, "ci"], cwd=frontend, check=True)
    subprocess.run([npm, "run", "build"], cwd=frontend, check=True)


class GmReferences(HTMLParser):
    def __init__(self):
        super().__init__()
        self.urls = []

    def handle_starttag(self, tag, attrs):
        values = dict(attrs)
        if tag == "script" and "src" in values:
            self.urls.append(values["src"])
        elif tag == "link" and "stylesheet" in values.get("rel", "").split():
            self.urls.append(values.get("href", ""))


def gm_assets(published, source=None):
    web = Path(published) / "wwwroot/gm"
    index = web / "index.html"
    if not index.is_file():
        raise FileNotFoundError("Required GM entry missing: wwwroot/gm/index.html")
    html = index.read_text(encoding="utf-8")
    if 'id="root"' not in html:
        raise ValueError("GM HTML does not contain its application root")
    references = GmReferences()
    references.feed(html)
    relative_files = {"index.html"}
    for url in references.urls:
        parsed = urlsplit(url)
        path = unquote(parsed.path)
        if parsed.scheme or parsed.netloc or parsed.query or parsed.fragment or not path.startswith("/gm/"):
            raise ValueError(f"Invalid GM local asset reference: {url}")
        relative = path.removeprefix("/gm/")
        asset = (web / relative).resolve()
        if not asset.is_relative_to(web.resolve()) or "\\" in relative or not asset.is_file():
            raise FileNotFoundError(f"GM referenced asset missing or outside its directory: {url}")
        relative_files.add(relative)
    if not any(name.endswith(".js") for name in relative_files) or not any(name.endswith(".css") for name in relative_files):
        raise ValueError("GM HTML must reference JavaScript and CSS")
    # Include all built assets so future code-split chunks are verified and included in HTTP smoke too.
    actual = {p.relative_to(web).as_posix(): p for p in web.rglob("*") if p.is_file()}
    if source is not None:
        expected = {p.relative_to(source).as_posix(): p for p in Path(source).rglob("*") if p.is_file()}
        if actual.keys() != expected.keys() or any(actual[name].read_bytes() != expected[name].read_bytes() for name in actual):
            raise ValueError("Published GM assets differ from the freshly built frontend")
    return {name: hashlib.sha256(file.read_bytes()).hexdigest() for name, file in sorted(actual.items())}


def verify_runtime_content(published):
    published = Path(published)
    for relative in ("appsettings.json", "experience.json", "Resources/Channel/channel_info.etc"):
        if not (published / relative).is_file():
            raise FileNotFoundError(f"Required runtime content missing: {relative}")
    for file in published.rglob("*"):
        name = file.name.lower()
        if file.is_file() and (file.suffix.lower() in {".pvf", ".db", ".sqlite", ".log", ".db-wal", ".db-shm", ".db-journal", ".sqlite-wal", ".sqlite-shm"}
                               or (name.startswith("appsettings.") and name.endswith(".json") and name != "appsettings.json")
                               or name in {".env", "secrets.json"} or name.startswith(".env.")):
            raise ValueError(f"Environment-specific data must not ship: {file.relative_to(published)}")
    settings = json.loads((published / "appsettings.json").read_text(encoding="utf-8"))
    def flatten(values, prefix=""):
        for key, value in values.items():
            path = (prefix + key).lower()
            if isinstance(value, dict):
                yield from flatten(value, path + ":")
            else:
                yield path, value
    configured = dict(flatten(settings))
    for key in ("Networking", "LauncherAuth", "GmWeb"):
        if configured.get(key.lower() + ":enabled") is not False:
            raise ValueError(f"Published {key} must be disabled by default")
    if configured.get("gmweb:bootstrap:loginname") or configured.get("gmweb:bootstrap:password"):
        raise ValueError("Administrator bootstrap credentials must not ship")


def smoke(published, rid):
    # Run a copy: migrations, logs and generated state must never enter the release archive.
    with temporary_directory(prefix="dfo-release-smoke-") as temporary:
        temporary = Path(temporary)
        app = temporary / "app"
        shutil.copytree(published, app)
        executable = app / ("Dfo.Server.Host.exe" if rid == "win-x64" else "Dfo.Server.Host")
        with socket.socket() as reservation:
            reservation.bind(("127.0.0.1", 0))
            port = reservation.getsockname()[1]
        fixed = {
            "Networking__Enabled": "false", "LauncherAuth__Enabled": "false",
            "GmWeb__Enabled": "true", "GmWeb__ListenUrl": f"http://127.0.0.1:{port}",
            "Database__Path": str(temporary / "smoke.db"),
            "Pvf__ArchivePath": "", "PVF_ARCHIVE_PATH": "",
            "DOTNET_ENVIRONMENT": "Production", "ASPNETCORE_ENVIRONMENT": "Production",
        }
        # The Host treats ':'/'__' and case variants as the same configuration key.
        # Remove every inherited alias before writing the isolated values.
        def normalize(key):
            return key.replace("__", ":").lower()
        isolated_keys = {normalize(key) for key in fixed}
        env = {key: value for key, value in os.environ.items()
               if normalize(key) not in isolated_keys
               and not normalize(key).startswith("gmweb:bootstrap:")}
        env.update(fixed)
        opener = build_opener(ProxyHandler({}))
        assets = gm_assets(app)
        base = f"http://127.0.0.1:{port}"
        log = temporary / "startup.log"
        with log.open("wb") as output:
            process = subprocess.Popen([str(executable)], cwd=app, env=env, stdout=output, stderr=subprocess.STDOUT)
            try:
                deadline = time.monotonic() + 45
                while time.monotonic() < deadline:
                    text = log.read_text(encoding="utf-8", errors="replace")
                    if process.poll() is not None:
                        raise RuntimeError(f"Published host exited ({process.returncode}):\n{text}")
                    if "Application started" in text:
                        for name, digest in assets.items():
                            with opener.open(base + "/gm/" + name, timeout=5) as response:
                                body = response.read()
                                if hashlib.sha256(body).hexdigest() != digest:
                                    raise ValueError(f"GM HTTP content mismatch: {name}")
                                expected_type = {".html": "text/html", ".js": "javascript", ".css": "text/css"}.get(Path(name).suffix)
                                if expected_type and expected_type not in response.headers.get("Content-Type", ""):
                                    raise ValueError(f"GM HTTP content type mismatch: {name}")
                        with opener.open(base + "/api/gm/v1/auth/status", timeout=5) as response:
                            if json.load(response).get("initialized") is not False:
                                raise ValueError("Smoke must not inherit or initialize an administrator")
                        try:
                            opener.open(base + "/api/gm/v1/cera/accounts", timeout=5)
                        except HTTPError as error:
                            if error.code != 401:
                                raise ValueError(f"GM private API expected 401, received {error.code}") from error
                        else:
                            raise ValueError("GM private API unexpectedly accessible without administrator")
                        print(f"Published self-contained GM host: {len(assets)} HTTP assets verified; uninitialized and private API 401; Game/Launcher disabled.")
                        return
                    time.sleep(0.25)
                raise RuntimeError(f"Published host startup timed out:\n{log.read_text(encoding='utf-8', errors='replace')}")
            finally:
                if process.poll() is None:
                    process.terminate()
                    try:
                        process.wait(timeout=10)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        process.wait()


MIGRATOR = Path("tools/DbMigrate")


def smoke_migrator(published, rid):
    """Migrates a database from before schema versions with the published tool, on a scratch copy."""
    executable = Path(published) / MIGRATOR / ("DbMigrate.exe" if rid == "win-x64" else "DbMigrate")
    with temporary_directory(prefix="dfo-release-migrate-") as temporary:
        database = Path(temporary) / "legacy.db"
        with sqlite3.connect(database) as connection:
            connection.executescript(
                "CREATE TABLE accounts(account_id INTEGER PRIMARY KEY, m_id TEXT NOT NULL UNIQUE,"
                " cube_red INTEGER NOT NULL DEFAULT 0);"
                "INSERT INTO accounts(account_id, m_id, cube_red) VALUES(1, 'smoke', 3);")
        connection.close()

        def migrate():
            return subprocess.run([str(executable), str(database)], cwd=temporary, capture_output=True,
                                  text=True, encoding="utf-8", timeout=60)

        first = migrate()
        if first.returncode != 0 or "Migrated from schema version 0" not in first.stdout:
            raise RuntimeError(f"Published migration tool failed ({first.returncode}):\n{first.stdout}{first.stderr}")
        connection = sqlite3.connect(database)
        try:
            version = connection.execute("PRAGMA user_version").fetchone()[0]
            kept = connection.execute("SELECT COUNT(*) FROM accounts WHERE m_id='smoke'").fetchone()[0]
            moved = connection.execute(
                "SELECT COALESCE(SUM(count), 0) FROM item_slots WHERE owner_scope='account' AND list_type=240").fetchone()[0]
            retired = connection.execute("SELECT COUNT(*) FROM pragma_table_info('accounts') WHERE name='cube_red'").fetchone()[0]
        finally:
            connection.close()
        if version < 1 or kept != 1 or moved != 3 or retired != 0:
            raise ValueError(f"Published migration left version={version} kept={kept} moved={moved} retired={retired}")
        if not list(Path(temporary).glob("legacy.db.v0-*.bak")):
            raise FileNotFoundError("Published migration tool did not back the database up")
        again = migrate()
        if again.returncode != 0 or "Already at schema version" not in again.stdout:
            raise RuntimeError(f"Published migration tool did not recognize a current database:\n{again.stdout}{again.stderr}")
        print(f"Published migration tool: unversioned database migrated to schema version {version} with a backup; rerun left it alone.")


def package():
    version = os.environ.get("RELEASE_VERSION") or ET.parse(ROOT / "Server/Directory.Build.props").findtext(".//VersionPrefix")
    rid = os.environ["RELEASE_RID"]
    if not VERSION.fullmatch(version) or rid not in ("win-x64", "linux-x64"):
        raise ValueError("Invalid release version or runtime")
    if (rid == "win-x64") != (os.name == "nt"):
        raise ValueError("Build on the target operating system so its executable can be smoke-tested")
    prepare_frontend()
    with temporary_directory(prefix="dfo-publish-") as temporary:
        published = Path(temporary) / "publish"
        subprocess.run(["dotnet", "publish", "Server/src/Dfo.Server.Host/Dfo.Server.Host.csproj",
                        "-c", "Release", "-r", rid, "--self-contained", "true", "-o", str(published),
                        f"-p:Version={version}", "-p:ContinuousIntegrationBuild=true"], cwd=ROOT, check=True)
        # Its own directory: the tool is a separate self-contained program, not part of the host.
        subprocess.run(["dotnet", "publish", "Tool/DbMigrate/DbMigrate.csproj",
                        "-c", "Release", "-r", rid, "--self-contained", "true", "-o", str(published / MIGRATOR),
                        f"-p:Version={version}", "-p:ContinuousIntegrationBuild=true"], cwd=ROOT, check=True)
        verify_runtime_content(published)
        assets = gm_assets(published, ROOT / "Web/gm-admin/dist")
        manifest = {"version": version, "runtime": rid, "commit": run("git", "rev-parse", "HEAD"),
                    "sdk": run("dotnet", "--version"), "selfContained": True, "gmAssets": assets,
                    "migrationTool": MIGRATOR.as_posix()}
        (published / "release.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
        (published / "README.md").write_text(
            "# DNF JP 86 Server\n\n"
            "运行与配置见 [server-ci-release.md](server-ci-release.md)。\n\n"
            "升级已有数据库：停服后运行 `tools/DbMigrate/DbMigrate <数据库路径>`（会先备份），"
            "见 [DatabaseSchemaMigration.md](DatabaseSchemaMigration.md)。\n\n"
            "GM 后台说明见 [gm-web-admin.md](gm-web-admin.md)，"
            "验收按 [gm-acceptance-list.md](gm-acceptance-list.md)。\n", encoding="utf-8")
        shutil.copyfile(ROOT / "Document/server-ci-release.md", published / "server-ci-release.md")
        shutil.copyfile(ROOT / "Document/host-component-composition.md", published / "host-component-composition.md")
        shutil.copyfile(ROOT / "Document/DatabaseSchemaMigration.md", published / "DatabaseSchemaMigration.md")
        for document in ("gm-web-admin.md", "gm-official-mail.md", "gm-cera-grants.md", "gm-acceptance-list.md", "login-session-lifecycle.md"):
            shutil.copyfile(ROOT / "Document" / document, published / document)
        smoke_migrator(published, rid)
        smoke(published, rid)
        output = ROOT / "artifacts/release"
        output.mkdir(parents=True, exist_ok=True)
        name = f"DNF-JP-86-Server-{version}-{rid}"
        archive = output / (name + (".zip" if rid == "win-x64" else ".tar.gz"))
        if rid == "win-x64":
            with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as result:
                for file in sorted(published.rglob("*")):
                    if file.is_file():
                        result.write(file, Path(name) / file.relative_to(published))
        else:
            with tarfile.open(archive, "w:gz") as result:
                result.add(published, arcname=name)
        checksum = hashlib.sha256()
        with archive.open("rb") as stream:
            for block in iter(lambda: stream.read(1024 * 1024), b""):
                checksum.update(block)
        digest = checksum.hexdigest()
        archive.with_name(archive.name + ".sha256").write_text(f"{digest}  {archive.name}\n", encoding="ascii")
        print(f"Release candidate: {archive.name} ({archive.stat().st_size:,} bytes); SHA256={digest}")


if __name__ == "__main__":
    command = sys.argv[1]
    if command == "verify-tag":
        verify_tag()
    elif command == "summary":
        summary(sys.argv[2], unit_only="--unit-only" in sys.argv[3:])
    elif command == "package":
        package()
    else:
        raise ValueError(f"Unknown command: {command}")
