"""Run reviewed real-client cases against one full Host and a copied binary scene database."""
import argparse
from datetime import datetime, timezone
from pathlib import Path
import sys

from report import write_report
from baseline import load
from native_cases import MaterialScene, execute_cases
from harness import require

REPO = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=Path, default=Path('artifacts/e2e') / datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ'))
    parser.add_argument('--dataset', type=Path, default=REPO / 'TestAssets/E2E/a21-initial-v1')
    parser.add_argument('--host', type=Path, default=REPO / 'Server/src/Dfo.Server.Host/bin/Release/net10.0/Dfo.Server.Host.dll')
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=False)
    manifest = dict(runId=args.out.name, formalE2E=True, readiness='awaiting-approved-baseline',
                    clientVerified=False, goldenBaseline=False, approvedCases=0, executedCases=0,
                    suiteName='A21 native login and creation E2E')
    try:
        material, document, entry = load(args.dataset)
        manifest.update(approvedCases=len(document['cases']), datasetId=material['datasetId'],
                        baseline=entry, clientVerified=True, goldenBaseline=True)
        require(args.host.is_file(), 'Production Host is not built')
    except Exception as error:
        results = [dict(caseId='ENV-REAL-CLIENT-BASELINE', status='blocked', detail=f'{type(error).__name__}: {error}')]
        write_report(args.out, manifest, results)
        print(results[0]['detail'], file=sys.stderr)
        return 1
    scene = None
    results = []
    try:
        scene = MaterialScene(args.host, args.dataset, args.out)
        scene.prepare()
        manifest['readiness'] = 'executing'
        results = execute_cases(scene, document, args.out)
        manifest['executedCases'] = len(results)
    except Exception as error:
        results.append(dict(caseId='ENV-SCENE', status='failed', detail=f'{type(error).__name__}: {error}'))
    finally:
        if scene is not None:
            try:
                scene.close()
            except Exception as error:
                results.append(dict(caseId='ENV-SHUTDOWN', status='failed', detail=f'{type(error).__name__}: {error}'))
    manifest['passedCases'] = sum(r['status'] == 'passed' for r in results)
    manifest['readiness'] = 'passed' if results and all(r['status'] == 'passed' for r in results) else 'failed'
    passed = write_report(args.out, manifest, results)
    print(f"Formal E2E: executed={manifest['executedCases']}, passed={manifest['passedCases']}")
    return 0 if passed else 1


if __name__ == '__main__':
    sys.exit(main())
