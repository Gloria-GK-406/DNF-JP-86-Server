"""First sequential network cases; assertions describe CurrentImplementation, not a client golden baseline."""
import struct

from harness import dstr, require


def authenticate(host):
    client = host.login()
    # A roster request resolves the identity bound by the actual HTTP ticket transaction.
    client.send(1, 8, b'\xff\xff\x02')
    roster = client.until(0, 2)[-1][2]
    require(roster[:1] == b'\x02' and struct.unpack_from('<H', roster, 16)[0] == 0,
            'Fixture account roster is not empty')
    client.command(0x04DD)
    state = host.snapshot()
    require(len(state['accounts']) == 1 and not state['characters'], 'Login changed account/character cardinality')


def invalid_credentials(host):
    before = host.snapshot()
    for credentials in [dict(host.credentials, password='invalid-password'),
                        dict(host.credentials, loginId='does-not-exist')]:
        reply = host.request('login', credentials, expected=401)
        require('ticket' not in reply, 'Rejected login returned a ticket')
    require(host.snapshot() == before, 'Rejected login changed business state')
    # The original authenticated Game connection remains usable.
    host.client.send(1, 8, b'\xff\xff\x02')
    host.client.until(0, 2)


def create_character(host):
    client = host.client
    client.command(5, b'\0' + dstr('E2EProbe') + b'\0\0\0\0')
    client.send(1, 8, b'\xff\xff\x02')
    roster = client.until(0, 2)[-1][2]
    require(roster[:1] == b'\x02' and struct.unpack_from('<H', roster, 16)[0] == 1,
            'Roster does not contain exactly one character')
    client.command(0x04DD)
    state = host.snapshot()
    require(len(state['characters']) == 1, 'Creation did not commit exactly one character')
    character = state['characters'][0]
    require(character['account_id'] == state['accounts'][0]['account_id'], 'Created character belongs to another account')
    require(character['name'] == 'E2EProbe' and character['job'] == 0 and character['level'] == 1,
            'Created profile does not match requested character')
    require(state['items'], 'Starting item instances missing')


def enter_town(host):
    host.client.command(4, struct.pack('<H', 0))
    # The offer list is a production entry projection, after the successful select response.
    host.client.until(0, 0x0015)
    # The next FIFO command completes only after the entire entry job, including refills.
    host.client.command(0x04DD)
    state = host.snapshot()
    require(len(state['characters']) == 1 and state['items'], 'Entry lost character or starting items')


def accept_quest(host):
    host.client.command(0x1F, struct.pack('<HH', 0x1F, 1016))
    host.client.until(0, 0x0015)
    host.client.command(0x04DD)
    rows = [r for r in host.snapshot()['quests'] if r['quest_id'] == 1016]
    require(len(rows) == 1, 'Quest acceptance did not commit exactly one active quest')
    require(rows[0]['character_id'] == host.snapshot()['characters'][0]['character_id'], 'Quest owner is incorrect')


def restart_and_relogin(host):
    before = host.checkpoint('before-restart')
    host.client.exit()
    host.client = None
    host.stop()
    host.start(host.database)
    require(host.snapshot() == before, 'Host restart changed committed profile/items/quest state')
    host.login()
    enter_town(host)
    require(host.snapshot() == before, 'Relogin lost or duplicated committed profile/items/quest state')
    host.client.exit()
    host.client = None


CASES = [
    ('AUTH-001', '已有账号登录、频道握手与身份连续', authenticate),
    ('AUTH-002', '错误密码与不存在账号安全拒绝', invalid_credentials),
    ('ROLE-001', '建角及初始资产落库（正常路径子集）', create_character),
    ('ROLE-004', '选角入城及任务列表投影（子集）', enter_town),
    ('QUEST-001', '接取任务 1016 并核对持久化', accept_quest),
    ('RECOVER-004', '正常重启、重新登录和已提交状态恢复（子集）', restart_and_relogin),
]
