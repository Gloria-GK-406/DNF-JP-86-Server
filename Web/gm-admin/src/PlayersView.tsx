import { useState } from 'react';
import { Button, Checkbox, Input, Segmented, Table, Typography } from 'antd';
import { MailOutlined, ReloadOutlined, SearchOutlined, WalletOutlined } from '@ant-design/icons';
import type { AccountView, CharacterDetail, CharacterView, PlayerPage } from './api';
import { useNav } from './AdminApp';
import { careerName, jobName, nf } from './format';
import { SampleTime } from './OverviewView';
import { useGrants } from './pending';
import { useGmRead } from './useGmRead';
import { KV, PageHead, Pill } from './ui';

type Kind = 'characters' | 'accounts';
type Selected = { kind: Kind; id: number };
/** One search box: a bare number addresses an ID, anything else is a literal name / login search. */
function criteria(kind: Kind, text: string, onlineOnly: boolean): Record<string, string> {
  const value = text.trim(); const next: Record<string, string> = {};
  if (/^\d{1,10}$/.test(value) && +value > 0 && +value <= 2147483647) next[kind === 'accounts' ? 'accountId' : 'characterId'] = value;
  else if (value) next.q = value.slice(0, 128);
  if (onlineOnly) next.onlineOnly = 'true';
  return next;
}

export function PlayersView({ initialQuery, onExpired, onDenied }: { initialQuery?: string; onExpired: () => void; onDenied: () => void }) {
  const nav = useNav(); const grants = useGrants();
  const [kind, setKind] = useState<Kind>('characters');
  const [text, setText] = useState(initialQuery ?? ''); const [onlineOnly, setOnlineOnly] = useState(false);
  const [filter, setFilter] = useState<Record<string, string>>(() => criteria('characters', initialQuery ?? '', false));
  const [page, setPage] = useState(1); const [pageSize, setPageSize] = useState(20);
  const [selected, setSelected] = useState<Selected | null>(null);
  const [picked, setPicked] = useState<number[]>([]);
  const parameters = new URLSearchParams({ page: String(page), pageSize: String(pageSize), ...filter });
  const query = useGmRead<PlayerPage<AccountView | CharacterView>>(`${kind}?${parameters}`, onExpired, onDenied);
  function search(nextText = text, nextOnline = onlineOnly, nextKind = kind) { setFilter(criteria(nextKind, nextText, nextOnline)); setPage(1); }
  function switchKind(value: Kind) { setKind(value); setText(''); setPicked([]); setSelected(null); setFilter(criteria(value, '', onlineOnly)); setPage(1); }
  function accountCharacters(accountId: number) { setKind('characters'); setText(''); setFilter({ accountId: String(accountId) }); setPage(1); setSelected(null); }
  const rows = query.data?.items ?? [];
  const characters = rows as CharacterView[]; const accounts = rows as AccountView[];
  const current = selected ?? (kind === 'characters' && characters[0] ? { kind, id: characters[0].characterId } : null);
  return <div className="page">
    <PageHead title="账号与角色" info="一律以账号 ID / 角色 ID 为准；这里只查询，不修改角色资料。">
      <SampleTime value={query.data?.sampledAt} /><Button size="small" icon={<ReloadOutlined />} loading={query.busy} onClick={query.refresh}>刷新列表</Button>
    </PageHead>
    <div className="grid g-side-main">
      <section className="panel">
        <div className="toolbar">
          <Segmented size="small" aria-label="查询对象" value={kind} onChange={value => switchKind(value as Kind)} options={[{ label: '角色', value: 'characters' }, { label: '账号', value: 'accounts' }]} />
          <Input size="small" allowClear prefix={<SearchOutlined />} aria-label={kind === 'characters' ? '角色搜索' : '账号搜索'} maxLength={128}
            placeholder={kind === 'characters' ? '角色姓名，或输入数字按角色 ID 查' : '登录名，或输入数字按账号 ID 查'} value={text}
            onChange={event => { setText(event.target.value); if (!event.target.value) search('', onlineOnly); }} onPressEnter={() => search()} style={{ width: 260, maxWidth: '100%' }} />
          <Checkbox checked={onlineOnly} onChange={event => { setOnlineOnly(event.target.checked); search(text, event.target.checked); }}>仅在线</Checkbox>
          <Button size="small" type="primary" onClick={() => search()}>查询</Button>
          {filter.accountId && kind === 'characters' && <span className="chip accent">账号 #{filter.accountId} 的角色<button className="linkbtn" aria-label="清除账号条件" onClick={() => search('', onlineOnly)}>×</button></span>}
          <span style={{ marginInlineStart: 'auto', display: 'inline-flex', gap: 8, alignItems: 'center' }}>
            {kind === 'accounts' && picked.length > 0 && nav.can('GrantCera') && <Button size="small" type="primary" icon={<WalletOutlined />} disabled={!!grants.cera}
              onClick={() => nav.composeCera({ accountIds: picked })}>给选中的 {picked.length} 个账号发点券</Button>}
            <span className="faint mono small">{query.data ? `${query.data.total} 条` : ''}</span></span>
        </div>
        {kind === 'characters'
          ? <Table<CharacterView> className="ledger" size="small" rowKey="characterId" dataSource={characters} loading={query.busy} scroll={{ x: 760 }}
              locale={{ emptyText: query.error ? '查询失败' : '没找到，换个关键词试试' }}
              rowClassName={c => c.characterId === current?.id ? 'is-selected' : ''} onRow={c => ({ onClick: () => setSelected({ kind: 'characters', id: c.characterId }), style: { cursor: 'pointer' } })}
              pagination={{ current: page, pageSize, total: query.data?.total ?? 0, size: 'small', showSizeChanger: true, showTotal: total => `共 ${total} 条`,
                onChange: (next, size) => { setPage(size === pageSize ? next : 1); setPageSize(size); } }}
              columns={[
                { title: '角色', key: 'n', render: (_, c) => <span><span className="strong">{c.nameDecodeStatus === 'valid' ? c.name : <Typography.Text type="danger">{c.name}</Typography.Text>}</span> <span className="id">{c.characterId}</span></span> },
                { title: '职业', key: 'j', render: (_, c) => <span>{careerName(c.job, c.growType)} <span className="faint small">{jobName(c.job)}</span></span> },
                { title: 'Lv', dataIndex: 'level', align: 'end', width: 52, render: v => <span className="mono">{v}</span> },
                { title: '账号', key: 'a', render: (_, c) => <span>{c.loginName} <span className="id">#{c.accountId}</span></span> },
                { title: '状态', key: 's', render: (_, c) => c.isDeleted ? <span className="chip">已删除</span> : <span><span className={`dot ${c.isOnline ? 'on' : 'off'}`} />{c.isOnline ? c.channel?.name ?? '未知频道' : '离线'}</span> },
              ]} />
          : <Table<AccountView> className="ledger" size="small" rowKey="accountId" dataSource={accounts} loading={query.busy} scroll={{ x: 680 }}
              locale={{ emptyText: query.error ? '查询失败' : '没找到，换个关键词试试' }}
              rowClassName={a => a.accountId === current?.id && current?.kind === 'accounts' ? 'is-selected' : ''} onRow={a => ({ onClick: () => setSelected({ kind: 'accounts', id: a.accountId }), style: { cursor: 'pointer' } })}
              rowSelection={nav.can('GrantCera') ? { selectedRowKeys: picked, preserveSelectedRowKeys: true, onChange: keys => setPicked(keys.map(Number).slice(0, 100)),
                getCheckboxProps: a => ({ 'aria-label': `选择账号 ${a.accountId}` }) } : undefined}
              pagination={{ current: page, pageSize, total: query.data?.total ?? 0, size: 'small', showSizeChanger: true, showTotal: total => `共 ${total} 条`,
                onChange: (next, size) => { setPage(size === pageSize ? next : 1); setPageSize(size); } }}
              columns={[
                { title: '账号', key: 'l', render: (_, a) => <span><span className="strong">{a.loginName}</span> <span className="id">#{a.accountId}</span></span> },
                { title: '角色', dataIndex: 'characterCount', align: 'end', render: v => <span className="mono">{v}</span> },
                { title: '点券', key: 'c', align: 'end', render: (_, a) => <span className="mono">{nf(a.balances.cera)}</span> },
                { title: '代币点券', key: 't', align: 'end', render: (_, a) => <span className="mono">{nf(a.balances.tokenCera)}</span> },
                { title: '欢乐代币点券', key: 'h', align: 'end', render: (_, a) => <span className="mono">{nf(a.balances.happyTokenCera)}</span> },
                { title: '状态', key: 's', render: (_, a) => <span><span className={`dot ${a.isOnline ? 'on' : 'off'}`} />{a.isOnline ? '在线' : '离线'}</span> },
              ]} />}
      </section>
      {current ? <DetailPanel key={`${current.kind}:${current.id}`} target={current} onExpired={onExpired} onDenied={onDenied}
        onAccount={id => setSelected({ kind: 'accounts', id })} onCharacter={id => setSelected({ kind: 'characters', id })} onAccountCharacters={accountCharacters} />
        : <section className="panel"><div className="empty-line">选一行看看详情</div></section>}
    </div>
  </div>;
}

function Position({ value }: { value: CharacterView['position'] }) {
  if (value.source === 'unavailable') return <span className="faint">不可用</span>;
  return <span className="mono small">城镇 {value.townId} · 区域 {value.areaId} · ({value.x}, {value.y}) · 朝向 {value.direction}</span>;
}
function DetailPanel({ target, onExpired, onDenied, onAccount, onCharacter, onAccountCharacters }: { target: Selected; onExpired: () => void; onDenied: () => void;
  onAccount: (id: number) => void; onCharacter: (id: number) => void; onAccountCharacters: (id: number) => void }) {
  const nav = useNav(); const grants = useGrants();
  const query = useGmRead<AccountView | CharacterDetail>(`${target.kind}/${target.id}`, onExpired, onDenied);
  // A malformed answer (no balances) is treated like no answer rather than crashing the panel.
  const value = query.data && 'balances' in query.data && query.data.balances ? query.data : undefined;
  const head = <div className="panel-head"><h2>{target.kind === 'accounts' ? '账号详情' : '角色详情'}</h2><span className="count">{target.id}</span>
    <div className="tools">{value && ('characterId' in value && value.isDeleted ? <span className="chip">已删除</span> : value.isOnline ? <Pill kind="ok">在线</Pill> : <Pill kind="idle">离线</Pill>)}
      <Button size="small" type="text" aria-label="刷新详情" icon={<ReloadOutlined />} loading={query.busy} onClick={query.refresh} /></div></div>;
  if (!value) return <section className="panel" aria-label="详情">{head}<div className="empty-line">{query.busy ? '正在查询详情…' : query.error ?? '暂无详情'}</div></section>;
  const purse = <div className="purse"><div><span>点券</span><b>{nf(value.balances.cera)}</b></div><div><span>代币点券</span><b>{nf(value.balances.tokenCera)}</b></div>
    <div><span>欢乐代币点券</span><b>{nf(value.balances.happyTokenCera)}</b></div></div>;
  if ('characterId' in value) return <section className="panel" aria-label="角色详情">{head}
    <div className="sheet-top"><span className="name">{value.nameDecodeStatus === 'valid' ? value.name : <Typography.Text type="danger">{value.name}</Typography.Text>}</span>
      <span className="job">{careerName(value.job, value.growType)} · {jobName(value.job)} · Lv.{value.level}</span></div>
    <KV flat={false} rows={[
      ['归属账号', <span key="a">{value.loginName} <span className="id">#{value.accountId}</span></span>],
      ['频道', value.isOnline ? value.channel?.name ?? '未知频道' : <span className="faint" key="c">不在线</span>],
      [value.position.source === 'current' ? '当前位置' : value.position.source === 'saved' ? '保存位置' : '位置', <Position key="p" value={value.position} />],
      ['姓名编码', value.nameDecodeStatus === 'valid' ? '有效 UTF-8' : value.nameDecodeStatus === 'missing' ? '姓名字节缺失' : <Typography.Text type="danger" className="mono" key="n">编码异常：{value.nameBytesHex}</Typography.Text>],
      ['采样', <SampleTime key="s" value={value.sampledAt} />],
    ]} />
    {purse}
    <div className="panel-foot">
      {nav.can('SendMail') && <Button size="small" type="primary" icon={<MailOutlined />} disabled={value.isDeleted || !!grants.mail}
        onClick={() => nav.composeMail({ recipients: [{ characterId: value.characterId, name: value.name, nameDecodeStatus: value.nameDecodeStatus, nameBytesHex: value.nameBytesHex, level: value.level, job: value.job, growType: value.growType }] })}>给 TA 发邮件</Button>}
      {nav.can('GrantCera') && <Button size="small" icon={<WalletOutlined />} disabled={!!grants.cera} onClick={() => nav.composeCera({ accountIds: [value.accountId] })}>给账号发点券</Button>}
      <Button size="small" type="text" onClick={() => onAccount(value.accountId)}>查看归属账号</Button>
    </div>
  </section>;
  return <section className="panel" aria-label="账号详情">{head}
    <div className="sheet-top"><span className="name">{value.loginName}</span><span className="job">账号 #{value.accountId} · {value.characterCount} 个角色</span></div>
    <KV flat={false} rows={[['在线状态', value.isOnline ? '在线' : '离线'], ['已入场角色', value.activeCharacterId ?? <span className="faint" key="n">无</span>], ['采样', <SampleTime key="s" value={value.sampledAt} />]]} />
    {purse}
    <div className="panel-foot">
      <Button size="small" onClick={() => onAccountCharacters(value.accountId)}>查看该账号角色</Button>
      {value.activeCharacterId && <Button size="small" onClick={() => onCharacter(value.activeCharacterId!)}>查看已入场角色</Button>}
      {nav.can('GrantCera') && <Button size="small" icon={<WalletOutlined />} disabled={!!grants.cera} onClick={() => nav.composeCera({ accountIds: [value.accountId] })}>给账号发点券</Button>}
    </div>
  </section>;
}
