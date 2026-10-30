"""Formal E2E gate. Only human-approved real-client collections may enter this suite."""
import argparse
from datetime import datetime, timezone
from pathlib import Path
import sys

from report import write_report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--out', type=Path, default=Path('artifacts/e2e') / datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ'))
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=False)
    # There are no approved collections yet. Never substitute the synthetic smoke database
    # or server-derived assertions for a real-client fixture and an approved expected result.
    manifest = dict(runId=args.out.name, formalE2E=True, readiness='awaiting-real-client-collection',
                    clientVerified=False, goldenBaseline=False, approvedCases=0, executedCases=0,
                    suiteName='Formal E2E readiness')
    results = [dict(caseId='ENV-REAL-CLIENT-BASELINE', status='blocked',
                    detail='No human-approved real A21 collection: formal E2E was not executed')]
    write_report(args.out, manifest, results)
    print(results[0]['detail'], file=sys.stderr)
    return 1


if __name__ == '__main__':
    sys.exit(main())
