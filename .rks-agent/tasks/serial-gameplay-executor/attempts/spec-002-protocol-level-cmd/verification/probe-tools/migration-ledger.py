#!/usr/bin/env python3
"""Task-local registration capture and open-migration ledger validation. No gameplay is run."""
import argparse
import collections
import copy
import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import resource
import subprocess
import tempfile
import time
import xml.sax.saxutils

B = Path(__file__).resolve().parents[2]
REPO = next(parent for parent in B.parents if (parent / 'Server/86JP.Server.slnx').is_file())
REG_APIS = {'AddCommandHandler', 'RegisterMessageHandler', 'RegisterTelemetryMessageHandler'}
SOURCE_PATTERN = r'IGameplayQueue|GameplayWork|GameplayTimerWork|CoalescingGameplaySource|HttpGameplay|RunsOnGameplay|EnqueuesItself|OutsideGameplay'
TX_PATTERN = r'DomainTransactionScope|DomainTransaction|RequireRollback|\.Begin(?:ReadOnly|Snapshot)?\(|\.CommitAsync\('


def dump(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True) + '\n')


def sha(data):
    return hashlib.sha256(data).hexdigest()


def mask_noncode(text):
    # Preserve offsets/newlines while removing C# comments and ordinary/verbatim/raw literals.
    pattern = re.compile(r'/\*.*?\*/|//[^\n]*|\$?""".*?"""|@"(?:""|[^"])*"|\$?"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'', re.S)
    return pattern.sub(lambda m: ''.join('\n' if c == '\n' else ' ' for c in m[0]), text)


def split_generic(text):
    values, depth, start = [], 0, 0
    for i, char in enumerate(text):
        if char == '<': depth += 1
        elif char == '>': depth -= 1
        elif char == ',' and depth == 0:
            values.append(text[start:i].strip()); start = i + 1
    values.append(text[start:].strip())
    return values


def source_registrations(repo):
    rows = []
    for path in sorted((repo / 'Server/src').rglob('*.cs')):
        text = path.read_text(encoding='utf-8-sig')
        masked = mask_noncode(text)
        usings = re.findall(r'^\s*using\s+([\w.]+)\s*;', masked, re.M)
        aliases = dict(re.findall(r'^\s*using\s+(\w+)\s*=\s*([\w.]+)\s*;', masked, re.M))
        for found in re.finditer(r'\.(' + '|'.join(sorted(REG_APIS)) + r')\s*<', masked):
            start, depth, pos = found.end(), 1, found.end()
            while depth and pos < len(masked):
                depth += (masked[pos] == '<') - (masked[pos] == '>'); pos += 1
            if depth: raise ValueError(f'unclosed generics: {path}:{found.start()}')
            end = pos
            while end < len(masked) and masked[end] != ';': end += 1
            api = found[1]
            arguments = split_generic(text[start:pos-1])
            call = text[found.start():end+1]
            message = re.search(r'ClientMessageType\.(\w+)', masked[pos:end])
            kind = re.search(r'CommandExecutionKind\.(\w+)', masked[pos:end])
            rows.append(dict(api=api, types=arguments, message=message[1] if message else None,
                execution_kind=kind[1] if kind else None, file=path.relative_to(repo).as_posix(),
                line=text.count('\n', 0, found.start()) + 1, text=call.strip(), usings=usings, aliases=aliases,
                source_sha256=sha(path.read_bytes())))
    return rows


def resolve_type(raw, actual_names, row):
    raw = re.sub(r'\s+', '', raw).replace('global::', '')
    raw = row['aliases'].get(raw, raw)
    if raw in actual_names: return raw
    candidates = [name for name in actual_names if name.rsplit('.', 1)[-1] == raw]
    allowed = [name for name in candidates if any(name == namespace + '.' + raw for namespace in row['usings'])]
    if len(allowed) == 1: return allowed[0]
    if len(candidates) == 1: return candidates[0]
    raise ValueError(f"ambiguous/unresolved {raw} at {row['file']}:{row['line']}: {candidates}")


def crosscheck(runtime, source):
    commands = runtime['commands']; messages = runtime['messages']
    by_command = {row['command']: row for row in commands}
    by_message = {row['message']: row for row in messages}
    if len(by_command) != len(commands) or len(by_message) != len(messages): raise ValueError('duplicate runtime registration')
    command_handlers = {row['handler'] for row in commands}; message_handlers = {row['handler'] for row in messages}
    seen_c, seen_m = set(), set()
    for row in source:
        if row['api'] == 'AddCommandHandler':
            name = resolve_type(row['types'][0], by_command, row)
            actual = by_command[name]
            handler = resolve_type(row['types'][2], command_handlers, row)
            assert handler == actual['handler'] and row['execution_kind'] == actual['execution_kind'], row
            assert name not in seen_c, name
            seen_c.add(name)
        else:
            name = row['message']; actual = by_message[name]
            assert resolve_type(row['types'][0], message_handlers, row) == actual['handler'], row
            assert (row['api'] == 'RegisterTelemetryMessageHandler') == actual['telemetry'], row
            assert name not in seen_m, name
            seen_m.add(name)
        actual['registration'] = {key: row[key] for key in ('file','line','text','source_sha256')}
    assert seen_c == set(by_command), set(by_command)-seen_c
    assert seen_m == set(by_message), set(by_message)-seen_m
    report = dict(messages=len(messages), message_handlers=len(message_handlers),
        telemetry_messages=sum(row['telemetry'] for row in messages),
        telemetry_handlers=len({row['handler'] for row in messages if row['telemetry']}),
        business_messages=sum(not row['telemetry'] for row in messages),
        business_handlers=len({row['handler'] for row in messages if not row['telemetry']}),
        commands=len(commands), command_handlers=len(command_handlers),
        execution_kinds=dict(sorted(collections.Counter(row['execution_kind'] for row in commands).items())),
        command_registration_files=dict(sorted(collections.Counter(row['registration']['file'] for row in commands).items())))
    return report


def capture(repo, output):
    assert not output.exists(), output
    output.mkdir(parents=True)
    project = Path(tempfile.mkdtemp(prefix='serial-gameplay-migration-', dir='/private/tmp'))
    refs = ''.join('<ProjectReference Include="' + xml.sax.saxutils.escape(str(repo / f'Server/src/Dfo.Server.{name}/Dfo.Server.{name}.csproj'), {'"':'&quot;'}) + '" />' for name in ('Application','Infrastructure','Persistence','Networking','Protocol'))
    csproj = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup><ItemGroup>' + refs + '</ItemGroup></Project>'
    (project/'capture.csproj').write_text(csproj)
    (project/'Program.cs').write_bytes((B/'verification/probe-tools/migration-export.cs').read_bytes())
    (output/'capture.csproj').write_text(csproj)
    command = ['dotnet','run','--project',str(project/'capture.csproj'),'-c','Release','--',str(output/'runtime.json')]
    started = datetime.datetime.now(datetime.timezone.utc).isoformat(); start = time.monotonic()
    with (output/'console.log').open('w') as log:
        result = subprocess.run(command,cwd=repo,stdout=log,stderr=subprocess.STDOUT,env={**os.environ,'DOTNET_CLI_TELEMETRY_OPTOUT':'1','DOTNET_NOLOGO':'1'})
    manifest = dict(cwd=str(repo),head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=repo,text=True).strip(),command=command,
        started=started,ended=datetime.datetime.now(datetime.timezone.utc).isoformat(),elapsed_seconds=round(time.monotonic()-start,3),
        exit_code=result.returncode,environment={'DOTNET_CLI_TELEMETRY_OPTOUT':'1','DOTNET_NOLOGO':'1'},children_maxrss=resource.getrusage(resource.RUSAGE_CHILDREN).ru_maxrss,
        exporter_sha256=sha((B/'verification/probe-tools/migration-export.cs').read_bytes()))
    dump(output/'manifest.json',manifest)
    if result.returncode: raise RuntimeError(f'capture failed: {output}/console.log')
    runtime = json.loads((output/'runtime.json').read_text()); source = source_registrations(repo)
    report = crosscheck(runtime, source)
    dump(output/'source-registrations.json', source); dump(output/'crosscheck.json',report); dump(output/'payload.json',runtime)
    print(json.dumps(report,ensure_ascii=False,indent=2))




def anchor(repo, relative, needle):
    path=repo/relative; text=path.read_text(encoding='utf-8-sig')
    pos=text.index(needle)
    return dict(file=relative,line=text.count('\n',0,pos)+1,source_sha256=sha(path.read_bytes()))


def wide_scan(repo, roots, pattern):
    result=[]; rx=re.compile(pattern)
    for root in roots:
        for path in sorted((repo/root).rglob('*.cs')):
            text=path.read_text(encoding='utf-8-sig'); code=mask_noncode(text)
            for number,(original,masked) in enumerate(zip(text.splitlines(),code.splitlines()),1):
                if rx.search(masked): result.append(dict(file=path.relative_to(repo).as_posix(),line=number,text=original.strip()))
    return result


def definitions(repo, names):
    short_names={name.rsplit('.',1)[-1] for name in names}; found={}
    rx=re.compile(r'\b(?:class|record|struct)\s+(?:class\s+|struct\s+)?(\w+)')
    for path in sorted((repo/'Server/src').rglob('*.cs')):
        text=path.read_text(encoding='utf-8-sig'); code=mask_noncode(text)
        ns=re.search(r'namespace\s+([\w.]+)',code)
        if not ns: continue
        for match in rx.finditer(code):
            if match[1] not in short_names: continue
            full=ns[1]+'.'+match[1]
            if full in names:
                assert full not in found,full
                found[full]=dict(file=path.relative_to(repo).as_posix(),line=text.count('\n',0,match.start())+1,source_sha256=sha(path.read_bytes()))
    assert set(found)==set(names),set(names)-set(found)
    return found


def http_routes(repo):
    rows=[]
    for folder in ('Dfo.Server.GmWeb','Dfo.Server.HttpApi'):
        for path in sorted((repo/'Server/src'/folder).glob('*.cs')):
            text=path.read_text(); code=mask_noncode(text)
            constants=dict(re.findall(r'const\s+string\s+(\w+)\s*=\s*"([^"]*)"',text))
            relative=path.relative_to(repo).as_posix()
            for match in re.finditer(r'\.Map(Get|Post|Put|Delete|Methods)\s*\(',code):
                start=match.end(); depth=1; pos=start
                while depth and pos<len(code):
                    depth+=(code[pos]=='(')-(code[pos]==')'); pos+=1
                end=pos
                while end<len(code) and code[end]!=';':end+=1
                expression=text[start:pos-1]
                # first argument (all current route expressions are constants/string concatenation)
                route_expr=expression.split(',',1)[0].strip()
                if any(part.strip() == 'path' for part in route_expr.split('+')): continue # local Get/Edit definitions, expanded below
                parts=[part.strip() for part in route_expr.split('+')]
                route=''.join(json.loads(part) if part.startswith('"') else constants[part] for part in parts)
                tail=text[pos:end]
                mode='outside' if 'new OutsideGameplay(' in tail else 'self-enqueued' if 'new EnqueuesItself(' in tail else 'group-enqueued'
                reason=re.search(r'new OutsideGameplay\("([^"]+)"',tail)
                rows.append(dict(id=f'http:{match[1].upper()} {route}',method=match[1].upper(),route=route,mode=mode,
                    reason=reason[1] if reason else None,file=relative,line=text.count('\n',0,match.start())+1,
                    source_sha256=sha(path.read_bytes()),registration_expression=route_expr))
            # Two scoped routing helpers use literal call sites; record each concrete route, not their definition.
            if re.search(r'void Get\(string path',code):
                for match in re.finditer(r'\bGet\("([^"]+)"\s*,',text):
                    route=constants['Prefix']+match[1]
                    rows.append(dict(id=f'http:GET {route}',method='GET',route=route,mode='group-enqueued',reason=None,
                        file=relative,line=text.count('\n',0,match.start())+1,source_sha256=sha(path.read_bytes()),registration_expression='Get literal; expanded local MapGet helper'))
            if re.search(r'void Edit\(string path',code):
                for match in re.finditer(r'\bEdit\("([^"]+)"\s*,\s*"([^"]+)"',text):
                    route=constants['Prefix']+match[1]
                    rows.append(dict(id=f'http:{match[2]} {route}',method=match[2],route=route,mode='group-enqueued',reason=None,
                        file=relative,line=text.count('\n',0,match.start())+1,source_sha256=sha(path.read_bytes()),registration_expression='Edit literals; expanded local MapMethods helper'))
    ids=[row['id'] for row in rows]; assert len(ids)==len(set(ids)),ids
    return sorted(rows,key=lambda row:row['id'])


def source_index(repo, scans, routes):
    app='Server/src/Dfo.Server.Application/'; protocol='Server/src/Dfo.Server.Protocol/Game/'
    sources=[]
    def add(identity,file,needle,kind,body,chain,cancel,coalescing='none',source_connection='none'):
        sources.append(dict(id='source:'+identity,kind=kind,original=anchor(repo,file,needle),body=body,
            chain=chain,source_connection=source_connection,cancellation_shutdown_refusal=cancel,coalescing=coalescing,
            target_input='pending: inspect actual domain arguments at original anchor before migration'))
    add('lifecycle-connected',app+'Dispatching/NetworkConnectionLifecycleDispatcher.cs','public ValueTask DispatchConnectedAsync','lifecycle','connected observer list in original order','new Lifecycle root keyed connection','caller cancellation enters queue; stopped executor warns+throws; lifetime drains before executor',source_connection='exact connecting context')
    add('lifecycle-disconnected',app+'Dispatching/NetworkConnectionLifecycleDispatcher.cs','public async ValueTask DispatchDisconnectedAsync','lifecycle','teardown observers; close-cause child or root; depth refusal re-root teardown plus close refused root','cause.Child(Lifecycle) or new Lifecycle root','caller queue token; depth refusal remedial jobs preserve cleanup; stopped executor warns+throws',source_connection='none (transport already closed); root chain still identifies original connection')
    add('startup-epic-book','Server/src/Dfo.Server.GameServer/GameServerStartupService.cs','startup:GameServerStartupService/epic-book','startup','epic-book legacy migration','new StartupRecovery root','startup token queues/waits; prewarming outside queue; startup failure propagates')
    add('startup-experience-storage','Server/src/Dfo.Server.GameServer/GameServerStartupService.cs','startup:GameServerStartupService/experience-storage','startup','experience storage initialization','new StartupRecovery root','startup token queues/waits; startup failure propagates')
    add('startup-gm-bootstrap','Server/src/Dfo.Server.GmWeb/GmBootstrapService.cs','startup:GmBootstrapService','startup','password preparation off executor; BootstrapGmAdministratorCommand writes once','new StartupRecovery root','startup token queue/body; bool result retained; no secrets in logs')
    add('joust-startup',app+'Events/JoustMaintenanceService.cs','startup:JoustMaintenanceService','startup','same advancement as periodic but silent observed baseline','new StartupRecovery root','GameplayTimerWork None admission/body caller token; Start waits before readiness')
    add('joust-periodic',app+'Events/JoustMaintenanceService.cs','await GameplayTimerWork.TickAsync(periodic','timer','advance lifecycle/commit; publish completed outcomes before current phase; consume observed transition before live publication','new Timer root','None admission/body host token; Tick ended-job Warning continues; original source catches refusal without stopping host',coalescing='periodic CoalescingGameplaySource; manual TickAsync uses separate direct RunAsync')
    add('event-consumer',app+'DomainEvents/DomainEventDispatcher.cs','internal async ValueTask<int> DrainOnceAsync(string','event','due scan outside executor; each delivery one job; Reliable business+completion same transaction; LiveOnly completion committed before projection','local publisher child Event stable across retries; prior process StartupRecovery root; otherwise Event root','None admission/body caller token; loop retry/fail markers; direct subscription remains publisher unit',coalescing='LiveOnly persisted batch/subscriber key; retry identity unchanged')
    add('event-depth-settlement',app+'DomainEvents/DomainEventDispatcher.cs','private async ValueTask<bool> SettleAsync','event','terminal reliable marker or skipped LiveOnly completion then close refused root','new Event root','None admission/body caller token; rejected child not repeatedly retried')
    add('scheduled-task',app+'Scheduling/ScheduledTaskHostedService.cs','private async Task RunAsync(Job job','scheduled','definition.Id callback in scope, due time re-read when starts; bounded retry after failure','new Timer root','None admission/body host token; executions tracked/drained; cancellation stops retry',coalescing='per Job requested latest time while queued; MaximumAttempts/RetryDelay per definition')
    add('card-delay',protocol+'CardReward/DungeonCardPresentationCoordinator.cs','private async Task RunTimerAsync','delay','leave job; wait clock delay; enqueue reveal/expire body; no world read in producer','new Timer root (original delay source does not inherit publisher chain)','linked timer cancellation; Stop cancels pending; Drain waits inflight; exact code pending per timer phase',coalescing='pending per run/phase and generation; no generic tick coalescing')
    # Every remaining timer-owning source file is explicitly indexed; helper/mechanism files are classified separately below.
    excluded={row['original']['file'] for row in sources}
    timer_files=sorted({row['file'] for row in scans if 'GameplayTimerWork' in row['text'] and row['file'] not in excluded
        and row['file']!=app+'Execution/GameplayTimerWork.cs'})
    for file in timer_files:
        text=(repo/file).read_text(); code=mask_noncode(text)
        entry=re.search(r'GameplayTimerWork\.(?:TickAsync|RunAsync)',code)
        # Source identity from the real local command label or BackgroundService type, not just the filename.
        label=re.search(r'(?:private|internal) const string Command\s*=\s*"([^"]+)"',text)
        identity=label[1] if label else (re.search(r'GameplayTimerWork\.TickAsync\([^,]+,\s*"([^"]+)"',text)[1]
            if re.search(r'GameplayTimerWork\.TickAsync\([^,]+,\s*"([^"]+)"',text) else Path(file).stem)
        add(identity,file,code[entry.start():entry.end()],'timer',
            'original timer body and manual entry below; exact domain arguments/commit points pending per cluster',
            'new Timer root', 'GameplayTimerWork: None admission, caller token in body; Tick absorbs ended-job error as Warning and continues; host stops producer before executor drains',
            coalescing='CoalescingGameplaySource periodic ticks; direct manual RunAsync separately admitted (do not infer shared busy round)')
        sources[-1]['entry_points']=[dict(line=i,text=line.strip()) for i,line in enumerate(text.splitlines(),1)
            if 'GameplayTimerWork.' in mask_noncode(line) or re.search(r'\b(?:public|internal)\s+(?:async\s+)?(?:Task|ValueTask)',line)]
        sources[-1]['topic_state']='Premium: Period + normalized sorted Active fingerprint + Crystal/Growth flags and prior presence; other themes inspect original owner; not uniformly period-only' if 'PremiumDaily' in identity else 'pending: preserve topic original watermarks; quota families period-only, do not add balance fingerprint'
    for route in routes:
        sources.append(dict(id=route['id'],kind='http-route',original={key:route[key] for key in ('file','line','source_sha256')},
            route=route,body='registered HTTP endpoint; original bus/domain stages must be inspected at migration',
            chain='new Gm root' if '/gm/' in route['route'] or route['route'].startswith('/gm/') else 'new Http root',
            source_connection='none (target character is not failure source)',coalescing='none',
            cancellation_shutdown_refusal='OutsideGameplay stays off executor' if route['mode']=='outside' else 'HttpGameplay: body buffering before enqueue, None admission, RequestAborted carried into body; producer awaits; original middleware refusal semantics'))
    inputs={
        'source:lifecycle-connected':'exact NetworkConnectionContext; original observer list',
        'source:lifecycle-disconnected':'exact disconnected context + captured close cause or new root; original observers',
        'source:startup-epic-book':'no request payload; current epic-book rules/catalog + legacy storage facts read by owner',
        'source:startup-experience-storage':'no request payload; current experience rule content and storage facts read by owner',
        'source:startup-gm-bootstrap':'configured login name + externally prepared password record + permissions; sensitive values are not exported',
        'source:joust-startup':'caller cancellation; suppressNotifications=true; now and state read inside body',
        'source:joust-periodic':'caller cancellation; suppressNotifications=false; now and state read inside body',
        'source:event-consumer':'PendingDomainEventDelivery read due off executor; event/subscriber identity, persisted origin and attempt count',
        'source:event-depth-settlement':'same delivery + refused chain; body records terminal/skipped status then closes refused root',
        'source:scheduled-task':'definition ID + latest requested scheduled time + attempt; execute context constructed inside body',
        'source:card-delay':'phase command label + wait + body closure over exact session/run/participant cancellation; body rechecks current run',
    }
    for row in sources:
        if row['id'] in inputs:row['target_input']=inputs[row['id']]
        elif row['kind']=='timer':row['target_input']='caller cancellation only at timer/manual entry; original body reads current domain state; typed immutable CMD argument design pending'
        elif row['kind']=='http-route':row['target_input']='HTTP route/query values and buffered body; self-enqueue credentials prepare off executor; concrete normalization/stages remain pending at original route anchor'
    return sources


def classified_scan(scans, sources, repo, transaction=False):
    classified=[]
    source_files={row['original']['file'] for row in sources}
    for row in scans:
        file=row['file']; snippet=row['text']
        if transaction:
            if 'ordering?.Begin(' in snippet: category='nontransaction-outbound-ordering-scope'
            elif 'runtime.Begin(' in snippet: category='nontransaction-runtime-entry'
            elif '/Transactions/' in file: category='transaction-contract-or-mechanism'
            elif '/Execution/Outbox/' in file: category='outbox-unit-mechanism'
            elif '/Database/' in file: category='durable-participant-mechanism'
            elif re.search(r'\.(?:Begin|BeginSnapshot)\(|new DomainTransaction\(',snippet): category='actual-boundary-entry'
            elif 'RequireRollback(' in snippet: category='rollback-only-request'
            elif '.CommitAsync(' in snippet: category='commit-call'
            else: category='ambient-or-participant-consumer/declaration'
            note='scope/index only; inspect full control flow and all participants for each migrated use case; not a mapped unit'
        elif re.search(r'RunsOnGameplay|OutsideGameplay|EnqueuesItself',snippet) and ('/GmWeb/' in file or '/Dfo.Server.GmWeb/' in file or file.endswith('HttpApiApplicationExtensions.cs')):
            category='http-registration-or-explicit-exclusion'; note='actual route table separately indexed; exclusion reasons retained'
        elif file in source_files: category='producer-or-producer-entry'; note='covered by source rows for this owner; raw hit may be shared declaration'
        elif '/Execution/' in file or '/Commands/' in file or '/Dispatching/' in file or file.endswith('HttpGameplay.cs'):
            category='shared-entry-mechanism'; note='shared queue/bus/dispatcher contracts; not a distinct gameplay use case'
        elif file.endswith('GmWebModule.cs') or '/GmWeb/' in file or file.endswith('HttpApiApplicationExtensions.cs'):
            category='http-registration-or-explicit-exclusion'; note='actual route table separately indexed; group default is not proof of final typed internal CMD'
        else: raise ValueError('unclassified source scan '+file)
        classified.append({**row,'category':category,'classification_note':note})
    return classified


def transaction_index(repo):
    root='Server/src/Dfo.Server.Application/'
    notes=[('Commands/Pipeline/UnitOfWorkBehavior.cs','if (context.ExecutionKind','read-only/write declared command boundary; RequireRollback normal refusal rolls back; Freeze then Commit; exception abort; not durable-commit proof via IsSettled'),
        ('DomainEvents/DomainEventDispatcher.cs','private async ValueTask<bool> ReliableAsync','Reliable claim/business/completion one write unit; LiveOnly claim+completion commit first then external projection; retry/depth markers separate units'),
        ('World/TransactionalDungeonVisitDomainService.cs','private async ValueTask<T> Execute','reuse ambient else Begin/action/Commit; dungeon entry and movement share caller unit'),
        ('World/DungeonLifecycleDomainService.cs','public async ValueTask RecordDepartureAsync','reuse ambient else own departure fact write/Commit'),
        ('Inventory/EpicBookDomainService.cs','public async ValueTask InitializeAsync','startup own Begin migration Commit'),
        ('World/BossTower/BossTowerDomainService.cs','transactions.Begin','conditional existing boundary; tower write then memory coordination order must be mapped per caller'),
        ('World/IllusionTower/IllusionTowerDomainService.cs','transactions.Begin','conditional boundary; exact tower participant and phase mapping pending'),
        ('Mail/MailboxDomainService.cs','private async ValueTask<T> RunAsync','ambient reuse or Begin(readOnly:!writing), acquire write when writing; caught refusal aborts current transaction; own lease commit/dispose'),
        ('Inventory/ItemGrantDelivery.cs','var lease = transactions.Current','conditional own write lease; whole-quantity addition commit else rollback/dispose'),
        ('Characters/CharacterStateAccess.cs','internal sealed class AmbientStoreAccess','snapshot reads rollback-only; outside reads standalone DomainTransaction rolled back; durable outside writes refuse; volatile changes standalone commit; selected position persist reuses ambient or owns standalone write'),
        ('Creatures/CreatureRuntimeDomainService.cs','new DomainTransaction','runtime read/write helper own boundary when absent; exact-generation handling and pending caches separately guarded'),
        ('Inventory/ItemContainers.cs','new DomainTransaction','store-backed container boundary access; persistent versus volatile caller obligations, no universal per-command transaction inference')]
    rows=[]
    for file,needle,note in notes:
        path=repo/root/file; text=path.read_text()
        if needle not in text:
            alternatives=['scope.Begin','transactions.Begin','new DomainTransaction','context.ExecutionKind']
            needle=next(value for value in alternatives if value in text)
        rows.append(dict(id='transaction-owner:'+file,original=anchor(repo,root+file,needle),meaning=note,per_use_case_mapping='pending'))
    return rows


def protection_index(repo):
    app='Server/src/Dfo.Server.Application/'; protocol='Server/src/Dfo.Server.Protocol/Game/'
    task=B.relative_to(repo).as_posix()+'/tasks/TASK-02.md'
    rows=[]
    specs=[('F-88-recover',protocol+'RecoverStamina/RecoverStaminaSlice.cs','if (!sessions.IsInWorld(session)) return;',
        'Server/tests/Dfo.Server.Application.Tests/Inventory/WeaknessGoldRecoveryTests.cs','A_selected_session_not_yet_in_world_is_silently_ignored_by_the_recovery_protocol','recover-01','selected-not-world remains silent/no currency or weakness changes'),
        ('F-88-teleport',protocol+'Teleport/TeleportSlice.cs','!session.Owns(previous)',
        'Server/tests/Dfo.Server.Application.Tests/Inventory/TeleportConsumableTests.cs','A_previous_town_member_from_another_generation_cannot_start_a_teleport','teleport-01','wrong-generation prior lease cannot begin settlement; adapt current bus-count probe to new CMD protection owner'),
        ('F-93-discard',app+'Commands/SelectedSessionSafety.cs','DiscardExact(',
        'Server/tests/Dfo.Server.Application.Tests/Creatures/CreatureRuntimeDomainServiceTests.cs','Blocking_uncertain_settlement_discards_only_the_exact_creature_runtime_without_saving_it','discard-02','uncertain settlement clears exact old pending reward/projection without saving stale cache; replacement generation retained'),
        ('F-93-recipient',app+'Effects/EffectOperationLedger.cs','safety.DemandCurrent(recipient)',
        'Server/tests/Dfo.Server.Application.Tests/Sessions/EffectOperationReplayTests.cs','A_recipient_replaced_after_capture_is_rejected_before_the_ordinal_is_consumed','recipient-01','capture-to-registration replacement rejected before sentinel/ordinal; same ordinal accepted after recovery')]
    for identity,file,needle,test_file,test_name,suffix,meaning in specs:
        if identity=='F-88-recover':
            text=(repo/file).read_text(); assert text.index(needle)>text.index('class RecoverStaminaHandler'), 'recover guard must anchor handler, not old CMD'
        rows.append(dict(id='protection:'+identity,original=anchor(repo,file,needle),test=dict(file=test_file,name=test_name),
            evidence=[B.relative_to(repo).as_posix()+f'/verification/logs/task-02-red-{suffix}.log',B.relative_to(repo).as_posix()+f'/verification/logs/task-02-green-{suffix}.log',task],
            invariant=meaning,new_owner='pending',new_test_entry='pending'))
    rows.append(dict(id='protection:F-132',original=anchor(repo,app+'Execution/Outbox/GameplayOutbox.cs','public async ValueTask<OutboxComposition<TRecipient>> ComposeAsync<TRecipient>'),
        test=dict(file='Server/tests/Dfo.Server.Host.Tests/DeferredOutboxCompositionArchitectureTests.cs',name='No_production_method_references_the_deferred_outbox_composition_API'),
        evidence=[task],invariant='mechanism not repaired; actual production IL references forbidden; new typed outbox must not expose API',new_owner='pending',new_test_entry='pending'))
    return rows


def transitions(repo):
    app='Server/src/Dfo.Server.Application/'
    return [dict(id='transition:'+identity,original=[anchor(repo,app+file,needle) for file,needle in locations],
        removal_owner=owner,final_evidence='pending') for identity,locations,owner in [
        ('T1',[('Commands/CommandBus.cs','if (GameplayJob.IsOnExecutor)')],'T-003 dual route then T-011 remove InlineAsync/legacy semantics'),
        ('T2',[('Dispatching/NetworkMessageDispatcher.cs','HandleOnExecutorAsync')],'T-003..010 absorb handler/shared Delivery/Coordinator business; T-011 no fine-grained forwarding'),
        ('T3',[('Commands/CommandDispatchingExtensions.cs','ICommandPipelineBehavior'),('Commands/CommandContracts.cs','public interface ISelectedSessionCommand')],'explicit current-CMD work units/participants replace five pipeline/markers; T-011 zero'),
        ('T4',[('Commands/NetworkCommandAuthorization.cs','AsyncLocal'),('Commands/Pipeline/SelectedSessionExecutionBehavior.cs','AsyncLocal'),('Effects/EffectOperationContext.cs','AsyncLocal'),('Commands/CommandBus.cs','AsyncLocal')],'explicit request/footprint parameters, no fallback; T-011 only Job+transaction AsyncLocal'),
        ('T5',[('Commands/Pipeline/CommandLoggingBehavior.cs','LogError')],'remove duplicate CMD Error with legacy pipeline, executor remains authority'),
        ('T6',[('Dispatching/NetworkMessageDispatcher.cs','_ = outboxes.Current'),('Dispatching/NetworkConnectionLifecycleDispatcher.cs','_ = outboxes.Current')],'bus job entry initializes before any failure; empty-entry failure closes real source'),
        ('planned-legacy-routes',[('Commands/CommandHandlerRegistry.cs','public sealed record'),('Dispatching/MessageHandlerRegistry.cs','public sealed record')],'not yet implemented: count LegacyFineGrained/LegacyQueued routes introduced T003, remove T011'),
        ('planned-LegacyRequestAdapter',[('Effects/EffectOperationContext.cs','internal sealed class')],'not yet implemented: old caller boundary only, explicit core no ambient, remove last consumer same unit')]]


def build_baseline(repo, capture_path):
    payload=json.loads((capture_path/'payload.json').read_text())
    names={row[key] for group,key in [('messages','handler'),('commands','command'),('commands','handler')] for row in payload[group]}
    defs=definitions(repo,names)
    sources_scan=wide_scan(repo,['Server/src'],SOURCE_PATTERN)
    routes=http_routes(repo); sources=source_index(repo,sources_scan,routes)
    txscan=wide_scan(repo,['Server/src'],TX_PATTERN)
    for group in ('messages','commands'):
        for row in payload[group]:
            row['handler_source']=defs[row['handler']]
            if group=='commands':row['command_source']=defs[row['command']]
    baseline=dict(baseline_head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=repo,text=True).strip(),
        registration_payload_sha256=sha((capture_path/'payload.json').read_bytes()),
        exporter_sha256=sha((B/'verification/probe-tools/migration-export.cs').read_bytes()),
        registrations=payload,sources=sources,source_scan=classified_scan(sources_scan,sources,repo),
        transactions=transaction_index(repo),transaction_scan=classified_scan(txscan,sources,repo,transaction=True),
        test_adapter_scan=wide_scan(repo,['Server/tests'],r'TestGameplay|ICommandBus|IGameplayQueue'),
        protections=protection_index(repo),transitions=transitions(repo))
    return baseline


def pending(anchor_value):
    return dict(state='pending',inspect=anchor_value)


def initial_ledger(baseline,repo):
    commands=baseline['registrations']['commands']; names=collections.defaultdict(list)
    for command in commands:names[command['command'].rsplit('.',1)[-1]].append(command['id'])
    rows={}
    for group in ('messages','commands'):
        items=[]
        for row in baseline['registrations'][group]:
            item=dict(id=row['id'],refs=[],state='pending',original=row,
                original_callers=pending(row['handler_source']),
                actual_units=pending(row['handler_source']),participants=pending(row['handler_source']),
                destination=pending(row['handler_source']),protection_locations=pending(row['handler_source']),
                evidence=[],completion_task=None)
            if group=='messages':
                item['classification']='telemetry' if row['telemetry'] else 'protocol'
                if row['telemetry']:
                    item['state']='retained'; item['reason']='pure logging telemetry, not a business CMD; preserve off-executor/fence/supersession behavior'
                else:
                    text=(repo/row['handler_source']['file']).read_text(); code=mask_noncode(text)
                    candidates=sorted({value for name,values in names.items() if re.search(r'\b'+re.escape(name)+r'\b',code) for value in values})
                    item['refs']=candidates
                    item['candidate_commands_scope']='file-level lexical candidates only, not a proven handler call graph; inspect shared helper/caller closure before closing'
                    helpers=sorted(set(re.findall(r'\b\w+(?:Delivery|Coordinator)\b',code)))
                    item['shared_helper_candidates']=helpers
            else:item['classification']='legacy-command'
            items.append(item)
        rows[group]=items
    rows['sources']=[dict(id=row['id'],classification=row['kind'],state='retained' if row['kind']=='http-route' and row['route']['mode']=='outside' else 'pending',
        reason=row['route']['reason'] if row['kind']=='http-route' and row['route']['mode']=='outside' else None,
        original=row,refs=[],actual_units=pending(row['original']),participants=pending(row['original']),
        destination=pending(row['original']),protection_locations=pending(row['original']),evidence=[],completion_task=None) for row in baseline['sources']]
    rows['protections']=[dict(id=row['id'],state='pending',original=row,refs=[],new_owner='pending',new_test_entry='pending',evidence=row['evidence']) for row in baseline['protections']]
    rows['transitions']=[dict(id=row['id'],state='pending',original=row,refs=[],destination='pending',evidence=[]) for row in baseline['transitions']]
    return dict(baseline_head=baseline['baseline_head'],baseline_registration_payload_sha256=baseline['registration_payload_sha256'],
        caution='open migration evidence, not execution status; declaration Kind is not actual unit equivalence',**rows)


def evidence_path(repo,value):
    if not isinstance(value,str) or not value:raise ValueError('evidence path missing')
    path=(repo/value).resolve()
    if not path.is_relative_to(repo.resolve()) or not path.is_file():raise ValueError('evidence path outside workspace or missing: '+value)


def validate(baseline,ledger,repo):
    groups=('messages','commands','sources','protections','transitions')
    baseline_groups={**baseline['registrations'],**{name:baseline[name] for name in groups[2:]}}
    if ledger['baseline_head']!=baseline['baseline_head'] or ledger['baseline_registration_payload_sha256']!=baseline['registration_payload_sha256']:raise ValueError('baseline binding mismatch')
    all_ids=[]; original={}
    for group in groups:
        expected=[row['id'] for row in baseline_groups[group]]; actual=[row['id'] for row in ledger[group]]
        if len(expected)!=len(set(expected)) or len(actual)!=len(set(actual)):raise ValueError('duplicate ID in '+group)
        if set(expected)!=set(actual):raise ValueError('missing/extra baseline IDs in '+group)
        all_ids.extend(actual); original.update({row['id']:row for row in baseline_groups[group]})
    if len(all_ids)!=len(set(all_ids)):raise ValueError('duplicate cross-group ID')
    ids=set(all_ids)
    for group in groups:
        for row in ledger[group]:
            if any(ref not in ids for ref in row.get('refs',[])):raise ValueError('dangling reference '+row['id'])
            old=original[row['id']]
            if row['original']!=old:raise ValueError('original baseline row rewritten '+row['id'])
            state=row['state']
            if group=='messages' and row['classification']!=('telemetry' if old['telemetry'] else 'protocol'):raise ValueError('telemetry classification changed '+row['id'])
            for path in row.get('evidence',[]):evidence_path(repo,path)
            if state=='retained':
                allowed=(group=='messages' and old['telemetry']) or (group=='sources' and old['kind']=='http-route' and old['route']['mode']=='outside')
                if not allowed or not row.get('reason'):raise ValueError('unsupported retained exception '+row['id'])
                continue
            if state not in ('pending','closed'):raise ValueError('invalid migration state '+row['id'])
            if state=='pending':
                required=('actual_units','participants','destination','protection_locations') if group in ('messages','commands','sources') else ('new_owner','new_test_entry') if group=='protections' else ('destination',)
                for key in required:
                    value=row.get(key)
                    if value!='pending' and not (isinstance(value,dict) and value.get('state')=='pending' and value.get('inspect')):
                        raise ValueError('pending field masquerades as settled '+row['id']+':'+key)
            else:
                # Presence/provenance checks cannot approve semantic equivalence; required Task review still owns it.
                if group in ('messages','commands','sources'):
                    destination=row.get('destination')
                    if not isinstance(destination,dict) or destination.get('kind') not in ('protocol-cmd','internal-cmd','application-service','deleted'):
                        raise ValueError('premature closed destination '+row['id'])
                    if destination['kind']=='deleted':
                        if not destination.get('reason'):raise ValueError('deletion lacks reason '+row['id'])
                    else:evidence_path(repo,destination.get('location'))
                    for key in ('actual_units','participants','protection_locations'):
                        value=row.get(key)
                        if isinstance(value,dict) and value.get('none_reason') and value.get('evidence'):
                            for path in value['evidence']:evidence_path(repo,path)
                        elif isinstance(value,list) and value:
                            for mapping in value:
                                if not isinstance(mapping,dict) or not mapping.get('old') or not mapping.get('new') or not mapping.get('evidence'):
                                    raise ValueError('closed without old/new mapping '+row['id']+':'+key)
                                for path in mapping['evidence']:evidence_path(repo,path)
                        else:raise ValueError('premature closed mapping '+row['id']+':'+key)
                    if not row.get('evidence') or not row.get('completion_task'):raise ValueError('closed without evidence/completed Task '+row['id'])
                    evidence_path(repo,row['completion_task'])
                    if '## Result' not in (repo/row['completion_task']).read_text() or 'Outcome: done' not in (repo/row['completion_task']).read_text():raise ValueError('completion Task lacks done result')
                else:
                    keys=('new_owner','new_test_entry') if group=='protections' else ('destination',)
                    if any(not row.get(key) or row[key]=='pending' for key in keys) or not row.get('evidence'):
                        raise ValueError('closed protection/transition lacks destination/evidence '+row['id'])
    return dict(valid=True,counts={group:dict(collections.Counter(row['state'] for row in ledger[group])) for group in groups})


def selftest(baseline,ledger,repo):
    trials={
        'missing-real-message':lambda d:d['messages'].pop(),
        'missing-real-command':lambda d:d['commands'].pop(),
        'duplicate-id':lambda d:d['commands'].append(copy.deepcopy(d['commands'][0])),
        'telemetry-misclassified':lambda d:next(row for row in d['messages'] if row['classification']=='telemetry').update(classification='protocol'),
        'dangling-reference':lambda d:d['messages'][0]['refs'].append('command:does.not.Exist'),
        'premature-closed':lambda d:next(row for row in d['messages'] if row['state']=='pending').update(state='closed'),
        'missing-evidence-path':lambda d:d['commands'][0]['evidence'].append('Server/missing-evidence.txt'),
        'closed-transition-without-destination':lambda d:d['transitions'][0].update(state='closed',evidence=[B.relative_to(repo).as_posix()+'/tasks/TASK-02.md']),
    }
    results={}
    validate(baseline,ledger,repo)
    for name,change in trials.items():
        bad=copy.deepcopy(ledger);change(bad)
        try:validate(baseline,bad,repo)
        except (ValueError,AssertionError) as error:results[name]=dict(rejected=True,reason=str(error))
        else:raise AssertionError('bad data accepted: '+name)
    return results


def main():
    parser=argparse.ArgumentParser(description=__doc__); sub=parser.add_subparsers(dest='action',required=True)
    cap=sub.add_parser('capture'); cap.add_argument('--repo',type=Path,default=REPO); cap.add_argument('--output',type=Path,required=True)
    init=sub.add_parser('initialize');init.add_argument('--capture',type=Path,required=True)
    for name in ('validate','selftest'):
        command=sub.add_parser(name);command.add_argument('--output',type=Path)
    args=parser.parse_args()
    if args.action=='capture':capture(args.repo.resolve(),args.output.resolve());return
    if args.action=='initialize':
        baseline=build_baseline(REPO,args.capture.resolve());ledger=initial_ledger(baseline,REPO)
        validate(baseline,ledger,REPO)
        for path,value in ((B/'verification/migration-baseline.json',baseline),(B/'verification/migration-ledger.json',ledger)):
            if path.exists():raise ValueError('refusing to overwrite frozen/maintained data: '+str(path))
            dump(path,value)
        print('initialized baseline and open ledger');return
    baseline=json.loads((B/'verification/migration-baseline.json').read_text());ledger=json.loads((B/'verification/migration-ledger.json').read_text())
    result=validate(baseline,ledger,REPO) if args.action=='validate' else selftest(baseline,ledger,REPO)
    if args.output:dump(args.output,result)
    print(json.dumps(result,ensure_ascii=False,indent=2))


if __name__=='__main__':main()
