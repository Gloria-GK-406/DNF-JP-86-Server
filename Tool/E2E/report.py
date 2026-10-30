"""Machine-readable results for the dedicated E2E lane."""
import json
from pathlib import Path
import xml.etree.ElementTree as ET


def write_report(directory, manifest, results):
    directory = Path(directory)
    if not results:
        results = [{'caseId': 'ENV-SUITE', 'status': 'failed', 'detail': 'No E2E cases executed'}]
    document = dict(manifest, results=results)
    (directory / 'results.json').write_text(json.dumps(document, ensure_ascii=False, indent=2), encoding='utf-8')
    failures = sum(r['status'] not in ('passed', 'blocked') for r in results)
    skipped = sum(r['status'] == 'blocked' for r in results)
    suite_name = manifest.get('suiteName', 'A21 E2E')
    suite = ET.Element('testsuite', name=suite_name, tests=str(len(results)),
                       failures=str(failures), skipped=str(skipped), errors='0')
    for result in results:
        case = ET.SubElement(suite, 'testcase', classname='A21.E2E', name=result['caseId'])
        if result['status'] == 'blocked':
            ET.SubElement(case, 'skipped', message=result.get('detail', 'Dependency failed'))
        elif result['status'] != 'passed':
            ET.SubElement(case, 'failure', message=result.get('detail', result['status']))
    ET.ElementTree(suite).write(directory / 'junit.xml', encoding='utf-8', xml_declaration=True)
    lines = ['# ' + suite_name, '', f"Run: `{manifest.get('runId', 'unknown')}`", '',
             '| Case | Result | Detail |', '|---|---|---|']
    for result in results:
        detail = result.get('detail', '').replace('|', '\\|').replace('\n', ' ')
        lines.append(f"| {result['caseId']} | {result['status']} | {detail} |")
    (directory / 'summary.md').write_text('\n'.join(lines) + '\n', encoding='utf-8')
    return all(result['status'] == 'passed' for result in results)
