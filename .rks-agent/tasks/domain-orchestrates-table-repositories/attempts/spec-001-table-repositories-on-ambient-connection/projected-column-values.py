"""Which of the eighty-eight projected columns still share a seeded value."""
import io
import re
from collections import defaultdict

W = r'D:\project\DNF\DNF-JP-86-Server\.worktrees\domain-orchestrates-table-repositories'
REPO = (W + r'\Server\src\Dfo.Server.Persistence\Characters'
        r'\SqliteCharacterEntryStateRepository.cs')
TEST = (W + r'\Server\tests\Dfo.Server.Persistence.Tests\Characters'
        r'\EntryLoadCharacterizationTests.cs')

repo = io.open(REPO, encoding='utf-8', newline='').read().replace('\r\n', '\n')
test = io.open(TEST, encoding='utf-8', newline='').read().replace('\r\n', '\n')

alias = {'c': 'characters', 's0': 'character_subtype0_fields',
         's1': 'character_subtype1_fields', 'f': 'character_init_flags'}

seeded = {}
for table in alias.values():
    match = re.search(
        r'INSERT INTO ' + table + r'\(([^)]*)\)\s*\n\s*VALUES\((.*?)\);', repo + test, re.S)
    names = [n.strip() for n in match.group(1).replace('\n', '').split(',')]
    values = [v.strip() for v in match.group(2).replace('\n', '').split(',')]
    for name, value in zip(names, values):
        seeded[f'{table}.{name}'] = value

body = re.search(r'SELECT c\.account_id,(.*?)FROM characters c', repo, re.S).group(1)
projected = re.findall(r'\b(c|s0|s1|f)\.([a-z0-9_]+)', 'c.account_id,' + body)

groups = defaultdict(list)
for prefix, column in projected:
    key = f'{alias[prefix]}.{column}'
    groups[seeded.get(key, '?')].append(key)

print(f'projected columns: {len(projected)}')
for value, members in sorted(groups.items(), key=lambda kv: -len(kv[1])):
    if len(members) > 1:
        print(f'  value {value} shared by {len(members)}: {", ".join(members)}')
