"""TASK-15 first-load probe: apply / revert the temporary hook in a Server/src tree.

usage: patch.py apply|revert <Server/src root> <backup dir outside the repository>
apply copies PvfArchiveProvider.cs byte-exact into the backup dir, inserts FirstLoadProbe.Hit() as the
first statement of the Archive getter and adds Abstractions/Execution/FirstLoadProbe.cs.
revert restores the backup and deletes FirstLoadProbe.cs. The probe writes only when the Host runs with
FIRSTLOAD_PROBE_OUT set, and only for reads made on the gameplay executor.
"""
import os, shutil, sys

HERE = os.path.dirname(os.path.abspath(__file__))
TARGET = 'Dfo.Server.Infrastructure/Content/PvfArchiveProvider.cs'
PROBE = 'Dfo.Server.Abstractions/Execution/FirstLoadProbe.cs'
ANCHOR = '        get\n        {\n'
HOOK = '            Dfo.Server.Abstractions.Execution.FirstLoadProbe.Hit();\n'


def apply(src, backup):
    if os.path.exists(backup):
        sys.exit('backup exists; revert first')
    os.makedirs(backup)
    shutil.copyfile(os.path.join(src, TARGET), os.path.join(backup, os.path.basename(TARGET)))
    data = open(os.path.join(src, TARGET), 'rb').read().decode('utf-8')
    crlf = '\r\n' in data
    text = data.replace('\r\n', '\n')
    assert text.count(ANCHOR) == 1, text.count(ANCHOR)
    text = text.replace(ANCHOR, ANCHOR + HOOK)
    if crlf:
        text = text.replace('\n', '\r\n')
    open(os.path.join(src, TARGET), 'wb').write(text.encode('utf-8'))
    shutil.copyfile(os.path.join(HERE, 'FirstLoadProbe.cs'), os.path.join(src, PROBE))
    print('applied to', src)


def revert(src, backup):
    shutil.copyfile(os.path.join(backup, os.path.basename(TARGET)), os.path.join(src, TARGET))
    if os.path.exists(os.path.join(src, PROBE)):
        os.remove(os.path.join(src, PROBE))
    shutil.rmtree(backup)
    print('reverted', src)


{'apply': apply, 'revert': revert}[sys.argv[1]](sys.argv[2], sys.argv[3])
