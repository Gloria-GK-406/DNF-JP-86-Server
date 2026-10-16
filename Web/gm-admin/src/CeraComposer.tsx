import { useEffect, useRef, useState } from 'react';
import { Button, Input, InputNumber, Popconfirm, Table } from 'antd';
import { CloseOutlined, ExclamationCircleFilled, SafetyCertificateOutlined, SearchOutlined, WalletOutlined } from '@ant-design/icons';
import { api, type Balances, type PlayerPage } from './api';
import { useNav } from './AdminApp';
import { currencyNames, type CeraAccount, type CeraCurrency, type CeraInput, type CeraPreview } from './cera';
import { nf, numberFormat } from './format';
import { useNotiBar } from './NotiBar';
import { useGrants } from './pending';
import { useConfirmLeave, useGmAction } from './useGmAction';
import { useGmRead } from './useGmRead';
import { Banner, Field, KV, PageHead, Panel, Steps } from './ui';

export type CeraSeed = Partial<CeraInput>;
type Access = { onExpired: () => void; onDenied: () => void };
const currencyKeys: CeraCurrency[] = ['Cera', 'TokenCera', 'HappyTokenCera'];
export const balanceOf = (value: Balances, currency: CeraCurrency) => currency === 'Cera' ? value.cera : currency === 'TokenCera' ? value.tokenCera : value.happyTokenCera;

export function CeraComposer({ seed, onExpired, onDenied }: { seed?: CeraSeed } & Access) {
  const access = { onExpired, onDenied };
  const nav = useNav(); const grants = useGrants(); const noti = useNotiBar(); const confirmLeave = useConfirmLeave();
  const { busy, run } = useGmAction(access);
  const [input, setInput] = useState<CeraInput>(() => ({ accountIds: [], currency: 'Cera', amount: 1000, reason: '', ...structuredClone(seed ?? {}) }));
  const [dirty, setDirty] = useState(!!seed?.reason);
  const [known, setKnown] = useState<Record<number, CeraAccount>>({});
  const [step, setStep] = useState(0); const [preview, setPreview] = useState<CeraPreview>(); const requestId = useRef('');
  const alive = useRef(true); useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  // Seeded accounts (from the players page) are looked up by ID so the balance preview can show them.
  useEffect(() => {
    let active = true;
    for (const id of seed?.accountIds ?? [])
      api<PlayerPage<CeraAccount>>(`cera/accounts?page=1&pageSize=1&accountId=${id}`).then(page => { if (active && page.items[0]) setKnown(old => ({ ...old, [id]: page.items[0] })); }).catch(() => {});
    return () => { active = false; };
  }, []);
  const sending = grants.busy.cera; const locked = busy || sending;
  function update(patch: Partial<CeraInput>) { setInput(old => ({ ...old, ...patch })); setDirty(true); setPreview(undefined); }
  const contentOk = Number.isInteger(input.amount) && input.amount > 0 && input.amount <= 2147483647 && input.reason.trim().length > 0 && input.reason.length <= 256;
  const leave = () => confirmLeave(dirty || input.accountIds.length > 0, () => nav.go('/grants'), { title: '不发了吗？', content: '这次填的内容会丢掉。', ok: '不发了', cancel: '继续' });
  function prepare() {
    void run(() => api<CeraPreview>('cera/preview', structuredClone(input)), value => { requestId.current = crypto.randomUUID(); setPreview(value); setStep(2); });
  }
  async function send() {
    if (!preview || locked || grants.cera) return;
    const { outcome } = await grants.grantCera({ requestId: requestId.current, input: structuredClone(preview.input) });
    if (!alive.current) return;
    if (outcome === 'committed') { setDirty(false); nav.go('/grants', { tab: 'cera' }); }
    else if (outcome === 'rejected') { setPreview(undefined); setStep(0); }
    else { noti.warning('结果暂未确认：请到「发放」页查询原编号或按原内容重试，勿新建编号再次发放。', { key: 'cera-unconfirmed' }); nav.go('/grants'); }
  }
  const name = currencyNames[input.currency];
  const status = step === 0 ? (!(input.amount > 0) ? '填一个大于 0 的数量' : !input.reason.trim() ? '写一下发放原因，审计里会用到' : `每个账号 +${nf(input.amount)} ${name}`)
    : step === 1 ? (input.accountIds.length ? `已选 ${input.accountIds.length} 个账号` : '至少选 1 个账号') : '最后看一眼，没问题就发';
  const rows = preview ? preview.accounts.map(a => ({ id: a.accountId, login: a.loginName, before: balanceOf(a.before, input.currency), after: balanceOf(a.after, input.currency) }))
    : input.accountIds.map(id => { const a = known[id]; const before = a ? balanceOf(a.balances, input.currency) : undefined;
      return { id, login: a?.loginName, before, after: before === undefined ? undefined : before + (input.amount || 0) }; });
  return <>
    <div className="page">
      <PageHead title="发点券" info="按账号发放，每账号只到账一次；支持离线和无角色账号，整批提交。结果不明时只能用原编号查询或按原内容重试。">
        <Steps labels={['填写', '选账号', '核对发放']} step={step} extra={[null, `${input.accountIds.length}/100`, null]} />
        <Button size="small" icon={<CloseOutlined />} disabled={locked} onClick={leave}>关闭</Button>
      </PageHead>
      {grants.cera && <Banner kind="crit" icon={<ExclamationCircleFilled />} title="还有一笔点券结果未确认" actions={<Button size="small" danger onClick={() => nav.go('/grants')}>去处理</Button>}>
        先用原编号查询或按原内容重试，处理完才能发新的。</Banner>}
      <div className="grid g-main-side">
        <div className="grid" style={{ minWidth: 0 }}>
          {step === 0 && <Panel title="发放内容">
            <div className="form">
              <Field label="币种"><div className="choice" role="radiogroup" aria-label="币种">{currencyKeys.map(key => <button key={key} role="radio" aria-checked={input.currency === key}
                className={input.currency === key ? 'on' : ''} disabled={locked} onClick={() => update({ currency: key })}><b>{currencyNames[key]}</b><span className="mono">{key}</span></button>)}</div></Field>
              <Field label="每账号增量" htmlFor="cera-amount"><InputNumber<number> id="cera-amount" min={1} max={2147483647} precision={0} value={input.amount} disabled={locked} style={{ width: 200 }}
                {...numberFormat} onChange={amount => update({ amount: amount ?? 0 })} /></Field>
              <Field label="发放原因" htmlFor="cera-reason" hint="会写进回执和操作记录。"><Input.TextArea id="cera-reason" rows={2} value={input.reason} maxLength={256} showCount disabled={locked}
                placeholder="例如：马战活动奖励补发" onChange={event => update({ reason: event.target.value })} /></Field>
            </div>
          </Panel>}
          {step === 1 && <AccountSelection input={input} known={known} locked={locked} {...access} onSeen={list => setKnown(old => ({ ...old, ...Object.fromEntries(list.map(a => [a.accountId, a])) }))}
            onChange={accountIds => update({ accountIds })} />}
          {step === 2 && preview && <Panel title="核对">
            <div className="grid" style={{ gap: 12 }}>
              <Banner kind="attn" icon={<SafetyCertificateOutlined />} title="整批提交">要么全部到账，要么全部回滚。提交按当前余额结算，以发放结果为准。</Banner>
              <KV rows={[['币种', currencyNames[preview.input.currency]], ['每个账号', <span className="mono" key="a">+{nf(preview.input.amount)}</span>],
                ['账号数', <span className="mono" key="n">{preview.accounts.length}</span>], ['合计', <span className="mono" key="t">{nf(preview.totalAmount)}</span>], ['原因', preview.input.reason]]} />
            </div>
          </Panel>}
        </div>
        <Panel title="余额变化" count={name} flush foot={input.accountIds.length > 0 && <><span className="grow">合计发放</span><span className="mono">{nf(preview ? preview.totalAmount : (input.amount || 0) * input.accountIds.length)}</span></>}>
          {rows.length === 0 ? <div className="empty-line">还没选账号</div> : rows.map(row => <div className="delta-row" key={row.id}>
            <span>{row.login ?? '账号'} <span className="id">#{row.id}</span></span>
            <span className="nums">{row.before === undefined ? <span className="faint">余额待预览</span> : <>{nf(row.before)} → <b>{nf(row.after!)}</b></>}</span></div>)}
        </Panel>
      </div>
    </div>
    <div className="actionbar">
      <span className="grow">{status}</span>
      {step > 0 && <Button disabled={locked} onClick={() => { setPreview(undefined); setStep(step - 1); }}>上一步</Button>}
      {step === 0 && <Button type="primary" disabled={!contentOk || locked} onClick={() => setStep(1)}>下一步：选账号</Button>}
      {step === 1 && <Button type="primary" loading={busy} disabled={!input.accountIds.length || sending || !!grants.cera} onClick={prepare}>预览并核对</Button>}
      {step === 2 && <Popconfirm title="确认整批发放？" description={`${preview?.accounts.length ?? 0} 个账号，各 +${nf(preview?.input.amount ?? 0)} ${name}。`} okText="发吧" cancelText="再看看"
        onConfirm={() => void send()} disabled={!preview || busy || !!grants.cera}>
        <Button type="primary" loading={sending} disabled={!preview || busy || !!grants.cera} icon={<WalletOutlined />}>确认整批发放</Button></Popconfirm>}
    </div>
  </>;
}

function AccountSelection({ input, known, locked, onChange, onSeen, onExpired, onDenied }: Access & { input: CeraInput; known: Record<number, CeraAccount>; locked: boolean; onChange: (ids: number[]) => void; onSeen: (rows: CeraAccount[]) => void }) {
  const [text, setText] = useState(''); const [filter, setFilter] = useState(''); const [page, setPage] = useState(1);
  const accounts = useGmRead<PlayerPage<CeraAccount>>(`cera/accounts?page=${page}&pageSize=20${filter}`, onExpired, onDenied);
  useEffect(() => { if (accounts.data) onSeen(accounts.data.items); }, [accounts.data]);
  function search(value = text) {
    const q = value.trim(); setPage(1);
    setFilter(/^\d{1,10}$/.test(q) && +q > 0 && +q <= 2147483647 ? `&accountId=${q}` : q ? `&q=${encodeURIComponent(q.slice(0, 128))}` : '');
  }
  const selected = input.accountIds;
  return <section className="panel" aria-label="选择账号">
    <div className="toolbar"><strong style={{ fontSize: 13.5 }}>选择账号</strong><span className="mono faint small">{selected.length}/100</span>
      <Input size="small" allowClear prefix={<SearchOutlined />} aria-label="账号搜索" placeholder="登录名，或输入数字按账号 ID 查" maxLength={128} value={text} disabled={locked}
        onChange={event => { setText(event.target.value); if (!event.target.value) search(''); }} onPressEnter={() => search()} style={{ width: 240, marginInlineStart: 'auto' }} />
      <Button size="small" disabled={locked} onClick={() => search()}>查找</Button>
      <Button size="small" type="text" disabled={locked} loading={accounts.busy} onClick={accounts.refresh}>刷新</Button></div>
    {selected.length > 0 && <div className="tray" style={{ padding: '10px 14px', borderBottom: '1px solid var(--line)' }}>{selected.map(id => <span className="chip" key={id}>
      {known[id]?.loginName ?? '账号'} <span className="mono">#{id}</span><button className="linkbtn" aria-label={`取消账号 ${id}`} disabled={locked} style={{ color: 'var(--ink-3)' }}
        onClick={() => onChange(selected.filter(x => x !== id))}><CloseOutlined /></button></span>)}</div>}
    <Table<CeraAccount> className="ledger" size="small" rowKey="accountId" loading={accounts.busy} dataSource={accounts.data?.items ?? []} scroll={{ x: 520 }}
      locale={{ emptyText: accounts.error ? '查询失败' : '没有匹配的账号' }}
      pagination={{ current: page, pageSize: 20, total: accounts.data?.total ?? 0, size: 'small', showSizeChanger: false, onChange: setPage }}
      rowSelection={{ selectedRowKeys: selected, preserveSelectedRowKeys: true, hideSelectAll: true,
        getCheckboxProps: a => ({ disabled: locked || !selected.includes(a.accountId) && selected.length >= 100, 'aria-label': `选择账号 ${a.accountId}` }),
        onSelect: (a, checked) => onChange(checked ? [...selected, a.accountId].slice(0, 100) : selected.filter(x => x !== a.accountId)) }}
      columns={[
        { title: '账号', key: 'l', render: (_, a) => <span><span className="strong">{a.loginName}</span> <span className="id">#{a.accountId}</span></span> },
        { title: `当前${currencyNames[input.currency]}`, key: 'b', align: 'end', render: (_, a) => <span className="mono">{nf(balanceOf(a.balances, input.currency))}</span> },
      ]} />
  </section>;
}
