"""Infrastructure smoke only. This suite has no approved real-client baseline and is not formal E2E."""
import argparse
from datetime import datetime, timezone
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import time
import traceback

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'E2E'))
from smoke_cases import CASES
from harness import Host, require, sha256
from report import write_report

REPO = Path(__file__).resolve().parents[2]
FIXTURE_VERSION = 'synthetic-smoke-v1'


def execute(args):
    out = args.out.resolve()
    out.mkdir(parents=True, exist_ok=False)
    manifest = dict(runId=out.name, fixtureVersion=FIXTURE_VERSION,
                    startedAt=datetime.now(timezone.utc).isoformat(),
                    evidence='InfrastructureSmoke', formalE2E=False, clientVerified=False, goldenBaseline=False,
                    suiteName='Infrastructure smoke (not formal E2E)')
    results = []
    host = None
    try:
        require(sys.platform.startswith('linux'), 'The production-script E2E lane currently requires Linux')
        require(args.pvf is not None and args.pvf.is_file(), 'PVF_ARCHIVE_PATH / --pvf must point to a local PVF file')
        require(re.fullmatch('[0-9a-fA-F]{64}', args.pvf_sha256 or ''), 'A pinned PVF_SHA256 / --pvf-sha256 is required')
        actual_hash = sha256(args.pvf)
        require(actual_hash.lower() == args.pvf_sha256.lower(), 'PVF SHA256 does not match the pinned fixture')
        require(args.host.is_file(), 'Build the production Host before E2E')
        require(shutil.which('openssl') and shutil.which('dotnet'), 'openssl and dotnet must be on PATH')
        manifest.update(pvfSha256=actual_hash, hostDllSha256=sha256(args.host),
                        hostCommit=subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=REPO, text=True).strip(),
                        sourceDirty=bool(subprocess.check_output(['git', 'status', '--porcelain'], cwd=REPO, text=True).strip()),
                        channelScriptSha256=sha256(args.host.parent / 'Resources/Channel/channel_info.etc'),
                        sdk=subprocess.check_output(['dotnet', '--version'], text=True).strip())
        require(CASES, 'No E2E cases configured')
        host = Host(args.host, args.pvf, out)
        host.prepare()
        manifest.update(baselineSha256=host.baseline_hash,
                        deployment=dict(channelPort=host.channel_port, httpPort=host.http_port,
                                        useScriptCatalog=True, includePvpChannels=True))
        failed = None
        for case_id, name, action in CASES:
            result = dict(caseId=case_id, name=name)
            if failed:
                result.update(status='blocked', detail=f'Dependency {failed} failed')
            else:
                started = time.monotonic()
                try:
                    host.checkpoint(case_id + '-before')
                    action(host)
                    host.checkpoint(case_id + '-after')
                    result.update(status='passed', detail='Synthetic smoke only; excluded from formal E2E')
                except Exception as error:
                    failed = case_id
                    result.update(status='failed', detail=f'{type(error).__name__}: {error}')
                    (out / 'failure.txt').write_text(traceback.format_exc(), encoding='utf-8')
                result['durationSeconds'] = round(time.monotonic() - started, 3)
            results.append(result)
            print(f"{case_id}: {result['status']}", flush=True)
        require(sha256(host.baseline) == host.baseline_hash, 'Read-only baseline was modified')
    except Exception as error:
        results.append(dict(caseId='ENV-STARTUP', status='failed', detail=f'{type(error).__name__}: {error}'))
        (out / 'failure.txt').write_text(traceback.format_exc(), encoding='utf-8')
        recorded = {r['caseId'] for r in results}
        results.extend(dict(caseId=cid, name=name, status='blocked', detail='Environment/setup failed')
                       for cid, name, _ in CASES if cid not in recorded)
    finally:
        if host:
            try:
                host.close()
            except Exception as error:
                results.append(dict(caseId='ENV-CLEANUP', status='failed', detail=str(error)))
        manifest['finishedAt'] = datetime.now(timezone.utc).isoformat()
        success = write_report(out, manifest, results)
    return success


def main():
    signal.signal(signal.SIGTERM, lambda *_: sys.exit(143))
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--pvf', type=Path, default=os.environ.get('PVF_ARCHIVE_PATH'))
    parser.add_argument('--pvf-sha256', default=os.environ.get('PVF_SHA256'))
    parser.add_argument('--host', type=Path, default=REPO / 'Server/src/Dfo.Server.Host/bin/Release/net10.0/Dfo.Server.Host.dll')
    parser.add_argument('--out', type=Path, default=REPO / 'artifacts/smoke' / datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ'))
    return 0 if execute(parser.parse_args()) else 1


if __name__ == '__main__':
    sys.exit(main())
