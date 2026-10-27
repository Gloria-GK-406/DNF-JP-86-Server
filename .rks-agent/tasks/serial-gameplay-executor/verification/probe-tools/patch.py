"""TASK-07 probe: apply / revert the temporary Server/src hooks.

usage: PROBE_WORK=<dir> patch.py apply | revert
Backups are byte-exact copies kept in $PROBE_WORK/backup; revert restores them and deletes GameplayProbe.cs.
The repository root is derived from this script's location (.rks-agent/tasks/<task>/verification/probe-tools).
"""
import os, re, shutil, sys

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), *['..'] * 5)) + '/'
SRC = REPO + 'Server/src/'
HERE = os.path.dirname(os.path.abspath(__file__))
BACKUP = os.path.join(os.environ.get('PROBE_WORK') or sys.exit('set PROBE_WORK to a directory outside the repository'), 'backup') + '/'
PROBE_FILE = 'Dfo.Server.Abstractions/Execution/GameplayProbe.cs'
P = 'Dfo.Server.Abstractions.Execution.GameplayProbe'

P2_CLASSES = {
    'Dfo.Server.Application/Users/OnlineStateRegistry.cs': 'OnlineStateRegistry',
    'Dfo.Server.Application/Authentication/LoginSessionRegistry.cs': 'LoginSessionRegistry',
    'Dfo.Server.Application/World/DungeonRunDomainService.cs': 'DungeonRunDomainService',
    'Dfo.Server.Application/World/DungeonRunDomainService.Objects.cs': 'DungeonRunDomainService',
    'Dfo.Server.Application/World/DungeonRunDomainService.Mechanisms.cs': 'DungeonRunDomainService',
    'Dfo.Server.Application/World/DungeonRunDomainService.DynamicActors.cs': 'DungeonRunDomainService',
    'Dfo.Server.Application/World/DungeonRunDomainService.Transitions.cs': 'DungeonRunDomainService',
    'Dfo.Server.Application/Creatures/CreatureRuntimeDomainService.cs': 'CreatureRuntimeDomainService',
    'Dfo.Server.Application/Parties/PartyService.cs': 'PartyService',
    'Dfo.Server.Application/World/TownAreaDomainService.cs': 'TownAreaDomainService',
    'Dfo.Server.Application/World/DungeonParticipationDomainService.cs': 'DungeonParticipationDomainService',
    'Dfo.Server.Protocol/Game/CardReward/DungeonCardPresentationCoordinator.cs': 'DungeonCardPresentationCoordinator',
    'Dfo.Server.Application/World/BossTower/BossTowerDomainService.cs': 'BossTowerDomainService',
    'Dfo.Server.Application/World/IllusionTower/IllusionTowerDomainService.cs': 'IllusionTowerDomainService',
    'Dfo.Server.Application/World/TournamentDungeonDomainService.cs': 'TournamentDungeonDomainService',
    'Dfo.Server.Application/World/AltarDungeonEngine.cs': 'AltarDungeonEngine',
}
RUNTIME = 'Dfo.Server.Application/Characters/OnlineCharacterRuntimeDomainService.cs'
SCOPE = 'Dfo.Server.Application/Transactions/DomainTransactionScope.cs'
USER_GATE = 'Dfo.Server.Application/OnlineUsers/OnlineUserExecutionGate.cs'
TRANSITION = 'Dfo.Server.Application/OnlineUsers/SelectedSessionTransitionGate.cs'
TX_GATE = 'Dfo.Server.Application/Commands/Pipeline/TransactionalExecutionBehavior.cs'
TITLE = 'Dfo.Server.Protocol/Game/TitleBook/TitleBookSlice.cs'
PARTY = 'Dfo.Server.Protocol/Game/Party/PartyNotificationSlice.cs'

# P1b (supplement to P1): write units opened by `new DomainTransaction()` outside DomainTransactionScope.Begin,
# the "self-opened transaction outside the gate" paths TASK-03 recorded (CharacterStateAccess.AloneAsync etc.).
P1B = {
    'Dfo.Server.Application/Inventory/ItemContainers.cs': 1,
    'Dfo.Server.Application/Characters/CharacterStateAccess.cs': 3,
    'Dfo.Server.Application/Creatures/CreatureRuntimeDomainService.cs': 1,
}

ALL = list(P2_CLASSES) + [RUNTIME, SCOPE, USER_GATE, TRANSITION, TX_GATE, TITLE, PARTY] + \
    [k for k in P1B if k not in P2_CLASSES]


def rd(rel):
    with open(SRC + rel, 'rb') as f:
        return f.read().decode('utf-8')


def wr(rel, text):
    with open(SRC + rel, 'wb') as f:
        f.write(text.encode('utf-8'))


def nl(text):
    return '\r\n' if '\r\n' in text else '\n'


def insert_first(text, signature, stmt, expect=1):
    """Insert stmt as the first statement of every method whose signature matches."""
    out, pos, n = [], 0, 0
    for m in re.finditer(signature, text):
        brace = text.index('{', m.end())
        line_start = text.rfind('\n', 0, brace) + 1
        indent = text[line_start:brace] + '    '
        out.append(text[pos:brace + 1])
        out.append(nl(text) + indent + stmt)
        pos = brace + 1
        n += 1
    out.append(text[pos:])
    assert n == expect, (signature, n)
    return ''.join(out)


def sub(text, pattern, repl, expect=None):
    new, n = re.subn(pattern, repl, text)
    assert n > 0 and (expect is None or n == expect), (pattern, n)
    return new, n


def apply():
    if os.path.exists(BACKUP):
        sys.exit('backup exists; revert first')
    os.makedirs(BACKUP)
    for rel in ALL:
        os.makedirs(os.path.dirname(BACKUP + rel), exist_ok=True)
        shutil.copyfile(SRC + rel, BACKUP + rel)
    counts = {}
    for rel, cls in P2_CLASSES.items():
        t = rd(rel)
        t, n = sub(t, r'\block \(((?:this\.|owner\.)?\w+)\)', r'lock (' + P + r'.Touch(\1, "P2:' + cls + r'"))')
        counts[rel] = n
        wr(rel, t)
    t = rd(RUNTIME)
    t, n = sub(t, r'(?<![.\w])entries(?=[.\[])', P + '.Touch(entries, "P2:OnlineCharacterRuntimeDomainService")')
    counts[RUNTIME] = n
    wr(RUNTIME, t)
    t = rd(SCOPE)
    t = insert_first(t, r'public DomainTransactionLease Begin\(bool readOnly = false\)',
                     'if (!readOnly) ' + P + '.Hit("P1:DomainTransactionScope.Begin");')
    wr(SCOPE, t)
    t = rd(USER_GATE)
    t = insert_first(t, r'public virtual ValueTask<IAsyncDisposable> AcquireAsync\(\s*TKey userId,\s*UserAccessMode accessMode,\s*CancellationToken cancellationToken\)',
                     P + '.Hit("P3:" + GetType().Name + ".AcquireAsync");')
    wr(USER_GATE, t)
    t = rd(TRANSITION)
    t = insert_first(t, r'public ValueTask<IAsyncDisposable> AcquireManyExclusiveAsync\(\s*IEnumerable<AccountId> accounts, CancellationToken cancellationToken\)',
                     P + '.Hit("P3:SelectedSessionTransitionGate.AcquireManyExclusiveAsync");')
    t = insert_first(t, r'private ValueTask<IAsyncDisposable> BorrowOrAcquireAsync\(\s*AccountId accountId, UserAccessMode accessMode, CancellationToken cancellationToken\)',
                     P + '.Hit("P3:SelectedSessionTransitionGate.BorrowOrAcquireAsync");')
    wr(TRANSITION, t)
    t = rd(TX_GATE)
    # TASK-17: the gate is taken through GameplayGateAssertion.Enter instead of WaitAsync; same hook site.
    t, _ = sub(t, r'GameplayGateAssertion\.Enter\(this\.gate,', 'GameplayGateAssertion.Enter(' + P + '.Touch(this.gate, "P3:TransactionalExecutionGate.AcquireAsync"),', 1)
    wr(TX_GATE, t)
    t = rd(TITLE)
    t, _ = sub(t, r'GameplayGateAssertion\.Enter\(gate,', 'GameplayGateAssertion.Enter(' + P + '.Touch(gate, "P3:AchievementTitleProjectionGate.EnterAsync"),', 1)
    wr(TITLE, t)
    t = rd(PARTY)
    t, _ = sub(t, r'GameplayGateAssertion\.Enter\(gates\[acquired\],', 'GameplayGateAssertion.Enter(' + P + '.Touch(gates[acquired], "P3:PartyParticipantGate.WaitAsync"),', 1)
    t, _ = sub(t, r'GameplayGateAssertion\.Enter\(stream\.Gate,', 'GameplayGateAssertion.Enter(' + P + '.Touch(stream.Gate, "P3:PartyDeliveryStream.Gate.WaitAsync"),', 2)
    wr(PARTY, t)
    for rel, expect in P1B.items():
        t = rd(rel)
        name = os.path.basename(rel)

        def hook(m, name=name):
            nlc, pad, stmt = m.group(1), m.group(2), m.group(3)
            return f'{nlc}{pad}{P}.Hit("P1b:new DomainTransaction@{name}"){";"}{nlc}{pad}{stmt}'
        t, n = re.subn(r'(\r?\n)([ \t]*)(var (?:alone|transaction) = new DomainTransaction\(\);)', hook, t)
        assert n == expect, (rel, n)
        wr(rel, t)
        print('P1b', rel, n)
    shutil.copyfile(HERE + '/GameplayProbe.cs', SRC + PROBE_FILE)
    for k, v in counts.items():
        print('P2', k, v)
    print('P2 total', sum(counts.values()))


def revert():
    restored = 0
    for d, _, fs in os.walk(BACKUP):
        for f in fs:
            rel = os.path.relpath(os.path.join(d, f), BACKUP)
            shutil.copyfile(BACKUP + rel, SRC + rel)
            restored += 1
    if os.path.exists(SRC + PROBE_FILE):
        os.remove(SRC + PROBE_FILE)
    shutil.rmtree(BACKUP)
    print('reverted', restored, 'files and removed', PROBE_FILE)


{'apply': apply, 'revert': revert}[sys.argv[1]]()
