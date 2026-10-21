"""Snapshot of Server/src: index blob hashes plus worktree file sha1 (git blob form) and untracked files.

usage: snapshot.py <out-file>
"""
import hashlib, os, subprocess, sys

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), *['..'] * 5)) + '/'


def git(*a):
    return subprocess.run(['git', '-C', REPO, *a], capture_output=True, check=True).stdout.decode()


index = git('ls-files', '-s', 'Server/src')
lines = ['# git ls-files -s Server/src']
lines += index.rstrip('\n').split('\n')
lines.append('# worktree blob sha1 per tracked file')
for path in git('ls-files', 'Server/src').split('\n'):
    if not path:
        continue
    data = open(REPO + path, 'rb').read()
    blob = hashlib.sha1(b'blob %d\0' % len(data) + data).hexdigest()
    lines.append(f'{blob} {path}')
lines.append('# untracked (not ignored) under Server/src')
lines += [l for l in git('ls-files', '--others', '--exclude-standard', 'Server/src').split('\n') if l]
open(sys.argv[1], 'w').write('\n'.join(lines) + '\n')
print(len(lines), 'lines written to', sys.argv[1])
