"""Validate preserved runs and compare measurements without overwriting evidence."""
import hashlib
import json
import pathlib
import sys
import xml.etree.ElementTree as ET

root = pathlib.Path.cwd()
evidence = root / '.rks-agent/tasks/change-risk-is-measured-repeatably/verification'
def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

preserved = {}
for filename in ('history-before.json', 'run-1-hashes-before.json'):
    records = read(evidence / filename)
    for item in records:
        path = root / item['Path'].replace('\\', '/')
        assert path.is_file(), path
        assert hashlib.sha256(path.read_bytes()).hexdigest().upper() == item['SHA256'], path
    preserved[filename] = len(records)

first = root / '.crap/2026-09-08_21-10-51'
second = root / '.crap' / sys.argv[1]
assert first != second and first.is_dir() and second.is_dir()
a, b = (read(p / 'manifest.json') for p in (first, second))
assert a['Run']['Status'] == b['Run']['Status'] == 'complete'
assert a['Before']['Files'] == b['Before']['Files']
assert a['Execution']['PvfArchiveSha256'] == b['Execution']['PvfArchiveSha256']
left, right = ({r['id']: r for r in read(p / 'function-details.json')['functions']} for p in (first, second))
assert left.keys() == right.keys()
changes = []
for key in left:
    x, y = left[key], right[key]
    assert x['complexity'] == y['complexity']
    assert x['evidenceState'] == y['evidenceState']
    if x['coverage'] != y['coverage'] or x['crap'] != y['crap']:
        changes.append({'id': key, 'symbol': x['symbol'],
                        'coverage': [x['coverage'], y['coverage']], 'crap': [x['crap'], y['crap']]})

failed = root / '.crap/2026-09-08_21-14-59'
diagnostic = read(failed / 'manifest.json')
assert diagnostic['Run']['Status'] == 'partial'
assert diagnostic['Execution']['TestExitCode'] == 1
assert sum(t['Failed'] for t in diagnostic['Execution']['Tests']) == 1
assert not diagnostic['Execution']['AllTestsPassed']

injected = root / '.crap' / sys.argv[2]
failure = read(injected / 'manifest.json')
assert failure['Run']['Status'] == 'failed'
assert failure['Execution']['BuildExitCode'] == 0
execution = failure['Execution']
assert len(execution['Tests']) == len(execution['ExpectedTestProjects']) == 7
assert {t['TestProject'] for t in execution['Tests']} == set(execution['ExpectedTestProjects'])
ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
for test in execution['Tests']:
    trx = ET.parse(injected / 'raw/tests' / test['TrxFile'])
    counters = trx.find('t:ResultSummary/t:Counters', ns).attrib
    assert test['Total'] == int(counters['total'])
    assert test['Passed'] == int(counters['passed'])
    assert test['Failed'] == int(counters['failed'])
    outcomes = [r.attrib['outcome'] for r in trx.findall('t:Results/t:UnitTestResult', ns)]
    assert len(outcomes) == test['Total']
    assert test['Passed'] == outcomes.count('Passed')
    assert test['Failed'] == outcomes.count('Failed')
    assert test['Skipped'] == outcomes.count('NotExecuted')
    assert test['Skipped'] == int(counters['total']) - int(counters['executed'])
total = sum(t['Total'] for t in execution['Tests'])
failed_tests = sum(t['Failed'] for t in execution['Tests'])
assert total == 2394
assert (execution['TestExitCode'] == 0) == (failed_tests == 0)
assert execution['AllTestsPassed'] == (failed_tests == 0)
assert 'after-tests' in failure['Run']['StatusReason']
assert (injected / 'raw/source-inventory.json').is_file()
assert (injected / 'logs/error.log').stat().st_size > 0
assert 'after-tests' in failure['Run']['Command']
for artifact in failure['Artifacts']:
    path = (injected / artifact['Path']).resolve()
    assert path.is_relative_to(injected.resolve())
    assert path.exists() == artifact['Exists'], artifact
assert not (injected / 'function-details.json').exists()

print(json.dumps({'decision': 'pass', 'preserved_files': preserved,
    'successful_runs': [first.name, second.name], 'same_input_files': len(a['Before']['Files']),
    'same_function_identities': len(left), 'coverage_changes': changes,
    'partial_run': failed.name, 'failure_injection_run': injected.name,
    'failure_injection_test_exit': execution['TestExitCode'],
    'failure_injection_test_failures': failed_tests,
    'failure_artifact_registry_matches_disk': True}, ensure_ascii=False, indent=2))
