"""TASK-10: (A) records and hits per entry family, alist.py's grouping, 3.1 split into Received/dispatcher and Connected/Closed.

usage: python3 fam.py <round-dir> [projects...]
Label = outermost Dfo entry frame that is not the bus/pipeline (alist.py); family = first FAMILIES regex matching the
label, else the first matching any entry frame. Within 3.1 the label decides: OnTcpConnected/OnTcpClosed -> Connected/Closed.
"""
import collections, json, os, re, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from classify import is_entry, method_of  # noqa: E402

# copied verbatim from probe-tools/alist.py (importing it runs its report)
FAMILIES = [
    ('入站协议（dispatcher 与传输回调）', r'NetworkMessageDispatcher|NetworkPlugin|TouchSocket|TcpSession|PluginManager|DataHandlingAdapter|PluginEntity|PluginRaise|PluginEventArgs|EasyTask|Method\.Invoke|TcpService'),
    ('效果账本重放', r'EffectOperationLedger'),
    ('连接观察者与关连接清理', r'OnlineUserConnectionObserver|OnTcpClosed|ITcpClosed'),
    ('登录会话运行时与维护', r'LoginSession'),
    ('事件分发（订阅）', r'DomainEventDispatcher'),
    ('卡牌展示计时器', r'DungeonCardPresentationCoordinator'),
    ('GM/HTTP 端点', r'Gm\w*Endpoints|Gm\w*Boundary|HttpApi|Middleware|HttpProtocol|Kestrel|Endpoint'),
    ('后台服务与调度', r'HostedService|RefreshService|Scheduler|Job\b|Job\.|SweepService|MaintenanceService|ClockService|DrainService|PartyWipeService|WeaknessRecoveryService|ScheduledTask'),
    ('启动与停机（宿主、容器释放）', r'Host\.|Host\+|ServiceProvider|StartupService|BootstrapService|MarriageRelationAudit'),
    ('命令总线（经总线进入的命令）', r'CommandBus|Behavior'),
]


def entry_frames(frames):
    return [f for f in frames if is_entry(f)]


def name(frame):
    asm, typ, meth = method_of(frame)
    t = re.sub(r'`\d+', '', typ.split('.')[-1])
    return f'{t}.{meth}'



d = sys.argv[1]
projects = [p for p in sys.argv[2:] if not p.startswith('--')] or ['Application', 'Protocol', 'Host', 'Networking']
rec = collections.Counter()
hit = collections.Counter()
labels = collections.defaultdict(collections.Counter)
for proj in projects:
    path = os.path.join(d, proj + '.json')
    if not os.path.exists(path):
        continue
    for r in json.load(open(path)):
        if r['class'] != 'A':
            continue
        ents = entry_frames(r['frames'])
        dfo = [f for f in ents if f.startswith('Dfo.Server') and not re.search(r'Behavior`?\d*[+:]', f) and 'CommandBus' not in f]
        pick = dfo[-1] if dfo else ents[-1]
        label = name(pick)
        joined = '\n'.join(ents)
        fam = next((f for f, rx in FAMILIES if re.search(rx, label)), None) or next((f for f, rx in FAMILIES if re.search(rx, joined)), '其他')
        if fam.startswith('入站协议'):
            fam = '3.1 Connected/Closed' if re.search(r'OnTcpConnected|OnTcpClosed', label) else '3.1 dispatcher/Received'
        rec[fam] += 1
        hit[fam] += r['count']
        labels[fam][(proj, label, r['hook'], r['test_outer_full'])] += r['count']
order = ['3.1 dispatcher/Received', '3.1 Connected/Closed'] + [f for f, _ in FAMILIES[1:]] + ['其他']
for fam in order:
    print(f'{fam}\trecords {rec[fam]}\thits {hit[fam]}')
if '--detail' in sys.argv or os.environ.get('FAM_DETAIL'):
    for fam in os.environ.get('FAM_DETAIL', '').split(',') or []:
        if not fam:
            continue
        print(f'\n## {fam}')
        for k, n in labels[fam].most_common():
            print(n, *k)
