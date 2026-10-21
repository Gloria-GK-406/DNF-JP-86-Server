"""(A) list for T-004b: grouped by appendix-A entry family, then by the specific production entry.

usage: alist.py <dir>   (reads <dir>/<Project>.json for the five projects)
The specific entry is the Dfo.Server entry frame nearest to the hook that is not a pipeline behaviour (behaviours sit
inside CommandBus); when the only entry frames are TouchSocket/Kestrel/Host internals, those are named instead.
"""
import collections, json, os, re, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from classify import is_entry, method_of  # noqa: E402

d = sys.argv[1]
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


groups = collections.defaultdict(lambda: collections.defaultdict(lambda: {'hooks': collections.Counter(), 'projects': set(), 'state': collections.Counter()}))
for proj in ['Application', 'Persistence', 'Protocol', 'Host', 'Networking']:
    path = os.path.join(d, proj + '.json')
    if not os.path.exists(path):
        continue
    for r in json.load(open(path)):
        if r['class'] != 'A':
            continue
        ents = entry_frames(r['frames'])
        dfo = [f for f in ents if f.startswith('Dfo.Server') and not re.search(r'Behavior`?\d*[+:]', f) and 'CommandBus' not in f]
        # the most specific production entry: outermost Dfo entry that is not the bus/pipeline
        pick = dfo[-1] if dfo else ents[-1]
        label = name(pick)
        joined = '\n'.join(ents)
        fam = next((f for f, rx in FAMILIES if re.search(rx, label)), None) or next((f for f, rx in FAMILIES if re.search(rx, joined)), '其他')
        g = groups[fam][label]
        g['hooks'][r['hook'].split(':')[0]] += r['count']
        g['projects'].add(proj)
        g['state'][(r['prod_near'] or '?').split('.')[0]] += r['count']
order = [f for f, _ in FAMILIES] + ['其他']
for fam in order:
    if fam not in groups:
        continue
    items = groups[fam]
    print(f'### {fam}（{len(items)} 个入口）')
    for label in sorted(items, key=lambda k: -sum(items[k]['hooks'].values())):
        g = items[label]
        hooks = ', '.join(f'{h} {n}' for h, n in sorted(g['hooks'].items()))
        state = ', '.join(s for s, _ in g['state'].most_common(4))
        print(f'- `{label}`（{",".join(sorted(g["projects"]))}）：{hooks}；首个触达的状态：{state}')
    print()
