"""Load only reviewed, hash-pinned expectations from a real-client collection."""
import json
from pathlib import Path

from harness import require, sha256
from scene import validate


def verify_document(document, material):
    require(document.get('schemaVersion') == 2, 'Unsupported native baseline schema')
    require(document.get('reviewStatus') == 'approved', 'Native baseline is not approved')
    require(document.get('evidenceSource') == 'RealA21Client' and document.get('humanConfirmed') is True,
            'Baseline lacks human-confirmed real-client evidence')
    require(document.get('datasetId') == material['datasetId'], 'Baseline belongs to another dataset')
    require(document['source']['clientPvfSha256'] == material['files']['Script.pvf']['sha256'],
            'Observed client PVF does not match fixed dataset')
    ids = [case['caseId'] for case in document['cases']]
    require(len(ids) == 2 and set(ids) == {'AUTH-001', 'ROLE-001'}, 'Unsupported or duplicate approved cases')
    require(set(document['flows']) == {'emptyFirst', 'emptyRelogin', 'create'}, 'Unexpected native flows')
    for flow in document['flows'].values():
        require(flow['loginTrailerPatches'] == [dict(offset=23, binding='adapterIPv4')],
                'Unsupported login trailer binding')
        expected = [flow['greeting'], *flow['loginExpected']]
        for name in ('selection', 'finish'):
            require(flow[name]['actions'], 'Empty native action sequence')
            for action in flow[name]['actions']:
                require(action['direction'] in ('C2S', 'S2C'), 'Invalid native action direction')
                if action['direction'] == 'S2C':
                    expected.append(action)
                else:
                    patches = action.get('patches', [])
                    if patches:
                        require(action['opcode'] == 2 and patches == [dict(offset=1, binding='adapterIPv4'),
                            dict(offset=5, binding='adapterIPv4'), dict(offset=19, binding='adapterMacAscii12')],
                            'Unsupported client metadata binding')
        for frame in expected:
            require(frame['selector'] in (0, 1), 'Invalid expected frame family')
            bytes.fromhex(frame['bodyHex'])
            if 'unixTimeOffset' in frame:
                require(frame['selector'] == 0 and frame['opcode'] == 1 and frame['unixTimeOffset'] == 21,
                        'Clock masking is only allowed for the initial notice clock')
    return document


def load(dataset):
    dataset = Path(dataset).resolve()
    material = validate(dataset)
    entries = material.get('nativeBaselines', [])
    require(len(entries) == 1, 'No supported approved real-client baseline in dataset')
    entry = entries[0]
    path = (dataset / entry['file']).resolve()
    require(path.parent == dataset, 'Baseline file must belong to its dataset')
    require(sha256(path) == entry['sha256'], 'Native baseline hash mismatch')
    document = verify_document(json.loads(path.read_text(encoding='utf-8')), material)
    require([case['caseId'] for case in document['cases']] == entry['caseIds'], 'Baseline case registry differs')
    require(document['source']['captureSha256'] == entry['captureSha256'], 'Capture provenance differs')
    return material, document, entry
