"""CI release tooling. Uses only Python's standard library; never touches a live deployment."""
from contextlib import contextmanager
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tarfile
import tempfile
import time
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


def summary(directory):
    reports = sorted(Path(directory).rglob("*.trx"))
    if not reports:
        raise RuntimeError("No TRX reports were produced; validation did not complete")
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    lines = ["## Test results", "", "| Report | Passed | Failed | Skipped |", "|---|---:|---:|---:|"]
    for report in reports:
        tree = ET.parse(report)
        counters = tree.find(".//t:Counters", ns)
        if counters is None:
            raise ValueError(f"No counters in {report}")
        skipped = sum(result.get("outcome") == "NotExecuted" for result in tree.findall(".//t:UnitTestResult", ns))
        lines.append(f"| {report.name} | {counters.get('passed', '0')} | {counters.get('failed', '0')} | {skipped} |")
    lines += ["", "PVF is not distributed through GitHub Actions. Content-dependent tests explicitly skip without Script.pvf; this is not client/gameplay acceptance."]
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


def smoke(published, rid):
    # Run a copy: migrations, logs and generated state must never enter the release archive.
    with temporary_directory(prefix="dfo-release-smoke-") as temporary:
        temporary = Path(temporary)
        app = temporary / "app"
        shutil.copytree(published, app)
        executable = app / ("Dfo.Server.Host.exe" if rid == "win-x64" else "Dfo.Server.Host")
        env = dict(os.environ, Networking__Enabled="false", LauncherAuth__Enabled="false",
                   Database__Path=str(temporary / "smoke.db"), PVF_ARCHIVE_PATH="",
                   DOTNET_ENVIRONMENT="Production", ASPNETCORE_ENVIRONMENT="Production")
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
                        print("Published self-contained host started successfully (network disabled).")
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


def package():
    version, rid = os.environ["RELEASE_VERSION"], os.environ["RELEASE_RID"]
    if not VERSION.fullmatch(version) or rid not in ("win-x64", "linux-x64"):
        raise ValueError("Invalid release version or runtime")
    if (rid == "win-x64") != (os.name == "nt"):
        raise ValueError("Build on the target operating system so its executable can be smoke-tested")
    with temporary_directory(prefix="dfo-publish-") as temporary:
        published = Path(temporary) / "publish"
        subprocess.run(["dotnet", "publish", "Server/src/Dfo.Server.Host/Dfo.Server.Host.csproj",
                        "-c", "Release", "-r", rid, "--self-contained", "true", "-o", str(published),
                        f"-p:Version={version}", "-p:ContinuousIntegrationBuild=true"], cwd=ROOT, check=True)
        for relative in ("appsettings.json", "experience.json", "Resources/Channel/channel_info.etc"):
            if not (published / relative).is_file():
                raise FileNotFoundError(f"Required runtime content missing: {relative}")
        for file in published.rglob("*"):
            if file.is_file() and (file.suffix.lower() in {".pvf", ".db", ".sqlite", ".log"}
                                   or file.name in {"appsettings.Development.json", "appsettings.Production.json"}):
                raise ValueError(f"Environment-specific data must not ship: {file.relative_to(published)}")
        manifest = {"version": version, "runtime": rid, "commit": run("git", "rev-parse", "HEAD"),
                    "sdk": run("dotnet", "--version"), "selfContained": True}
        (published / "release.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
        shutil.copyfile(ROOT / "Document/server-ci-release.md", published / "README.md")
        shutil.copyfile(ROOT / "Document/host-component-composition.md", published / "host-component-composition.md")
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
        summary(sys.argv[2])
    elif command == "package":
        package()
    else:
        raise ValueError(f"Unknown command: {command}")
