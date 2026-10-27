"""Within one snapshot: does every worktree blob sha1 equal its index blob sha1? Prints counts."""
import sys
lines = open(sys.argv[1]).read().split('\n')
i1 = lines.index('# git ls-files -s Server/src')
i2 = lines.index('# worktree blob sha1 per tracked file')
i3 = lines.index('# untracked (not ignored) under Server/src')
index = {}
for l in lines[i1 + 1:i2]:
    meta, path = l.split('\t')
    index[path] = meta.split()[1]
work = {}
for l in lines[i2 + 1:i3]:
    sha, path = l.split(' ', 1)
    work[path] = sha
untracked = [l for l in lines[i3 + 1:] if l]
same = sum(1 for p in index if work.get(p) == index[p])
print('index entries', len(index), 'worktree entries', len(work), 'equal', same, 'untracked', len(untracked))
