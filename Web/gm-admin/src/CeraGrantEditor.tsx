import { useEffect, useRef, useState } from 'react';
import { App, Button, Checkbox, Input, InputNumber, Modal, Space, Steps, Table, Typography } from 'antd';
import { api, ApiError, type Balances, type PlayerPage } from './api';
import { useGmRead } from './useGmRead';
import { useNotiBar } from './NotiBar';
import { currencyNames, type CeraAccount, type CeraChange, type CeraCurrency, type CeraInput, type CeraPreview, type PendingCera } from './cera';

export type CeraAccess = { onExpired: () => void; onDenied: () => void };
export type GrantOutcome = 'committed' | 'rejected' | 'unknown';
export function Wallet({ value }: { value: Balances }) {
  return <span>点券 {value.cera} / 代币点券 {value.tokenCera} / 欢乐代币点券 {value.happyTokenCera}</span>;
}
export function Changes({ accounts }: { accounts: CeraChange[] }) {
  return <Table<CeraChange> rowKey="accountId" pagination={false} dataSource={accounts} scroll={{ x: 750 }} columns={[
    { title: '账号 ID', dataIndex: 'accountId' }, { title: '登录名', dataIndex: 'loginName' },
    { title: '发放前', render: (_, row) => <Wallet value={row.before} /> }, { title: '发放后', render: (_, row) => <Wallet value={row.after} /> },
  ]} />;
}

export function CeraGrantEditor({ initial, initialDirty, granting, onGrant, onClose, ...access }: CeraAccess & {
  initial: CeraInput; initialDirty: boolean; granting: boolean;
  onGrant: (snapshot: PendingCera) => Promise<GrantOutcome>; onClose: () => void;
}) {
  const { modal } = App.useApp(); const noti = useNotiBar();
  const [input, setInput] = useState(() => structuredClone(initial)); const [dirty, setDirty] = useState(initialDirty);
  const [step, setStep] = useState(0); const [preview, setPreview] = useState<CeraPreview>(); const [busy, setBusy] = useState(false);
  const alive = useRef(true); const generation = useRef(0); const requestId = useRef(''); const locked = busy || granting;
  useEffect(() => { alive.current = true; return () => { alive.current = false; generation.current++; }; }, []);
  function update(value: CeraInput) { setInput(value); setDirty(true); setPreview(undefined); }
  function close() {
    if (locked) return;
    if (!dirty) { onClose(); return; }
    modal.confirm({ title: '放弃未保存的修改？', content: '关闭后将丢弃本窗口的未提交修改。', okText: '放弃修改', cancelText: '继续编辑', onOk: onClose });
  }
  async function prepare() {
    if (locked) return;
    const epoch = ++generation.current; setBusy(true);
    try {
      const value = await api<CeraPreview>('cera/preview', structuredClone(input));
      if (alive.current && epoch === generation.current) { requestId.current = crypto.randomUUID(); setPreview(value); setStep(2); }
    } catch (failure) {
      if (!alive.current || epoch !== generation.current) return;
      if (failure instanceof ApiError && failure.status === 401) access.onExpired();
      else if (failure instanceof ApiError && failure.status === 403) access.onDenied();
      else noti.failure(failure);
    } finally { if (alive.current && epoch === generation.current) setBusy(false); }
  }
  async function confirm() {
    if (!preview || locked) return;
    const outcome = await onGrant({ requestId: requestId.current, input: structuredClone(preview.input) });
    if (alive.current && outcome === 'rejected') { setPreview(undefined); setStep(0); }
  }
  return <Modal title="发放点券" open onCancel={close} width={1040} className="cera-workflow-modal" closable={!locked} keyboard={!locked}
    mask={{ closable: false }} footer={<Space wrap>
      <Button disabled={locked} onClick={close}>取消编辑</Button>
      {step > 0 && <Button disabled={locked} onClick={() => { setPreview(undefined); setStep(step - 1); }}>返回上一步</Button>}
      {step === 0 ? <Button type="primary" disabled={locked} onClick={() => setStep(1)}>下一步：选择账号</Button>
        : step === 1 ? <Button type="primary" loading={busy} disabled={granting} onClick={() => void prepare()}>预览并核对</Button>
          : <Button type="primary" loading={granting} disabled={busy} onClick={() => void confirm()}>确认整批发放</Button>}
    </Space>}>
    <Steps current={step} items={[{ title: '编辑发放内容' }, { title: '选择账号' }, { title: '核对发放' }]} />
    <div className="cera-workflow-body">
      {step === 0 && <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
        <Typography.Text type="secondary">按账号发放，每账号只到账一次；支持离线和无角色账号，整批提交。</Typography.Text>
        <label htmlFor="cera-currency">币种</label><select id="cera-currency" disabled={locked} value={input.currency} onChange={e => update({ ...input, currency: e.target.value as CeraCurrency })}>
          {Object.entries(currencyNames).map(([key, label]) => <option key={key} value={key}>{label}</option>)}
        </select>
        <label htmlFor="cera-amount">每账号增量</label><InputNumber id="cera-amount" min={1} max={2147483647} precision={0} disabled={locked} value={input.amount} onChange={v => update({ ...input, amount: v ?? 0 })} />
        <label htmlFor="cera-reason">发放原因</label><Input.TextArea id="cera-reason" rows={3} maxLength={256} disabled={locked} value={input.reason} onChange={e => update({ ...input, reason: e.target.value })} />
      </Space>}
      {step === 1 && <AccountSelection {...access} input={input} update={update} locked={locked} />}
      {step === 2 && preview && <><Typography.Paragraph type="secondary">提交按当前余额结算，以发放结果为准。</Typography.Paragraph>
        <Typography.Paragraph>{currencyNames[preview.input.currency]}：每账号增加 {preview.input.amount}，{preview.accounts.length} 个不同账号，总增量 {preview.totalAmount}；原因：{preview.input.reason}</Typography.Paragraph>
        <Changes accounts={preview.accounts} /></>}
    </div>
  </Modal>;
}

function AccountSelection({ input, update, locked, ...access }: CeraAccess & { input: CeraInput; update: (value: CeraInput) => void; locked: boolean }) {
  const [page, setPage] = useState(1); const [q, setQ] = useState(''); const [accountId, setAccountId] = useState<number | null>(null); const [filter, setFilter] = useState('');
  const accounts = useGmRead<PlayerPage<CeraAccount>>(`cera/accounts?page=${page}&pageSize=20${filter}`, access.onExpired, access.onDenied);
  return <><Space wrap>
    <Input aria-label="搜索登录名" value={q} onChange={e => setQ(e.target.value)} placeholder="登录名字面搜索" disabled={locked} />
    <InputNumber aria-label="定位账号 ID" value={accountId} min={1} max={2147483647} precision={0} onChange={setAccountId} disabled={locked} />
    <Button disabled={locked} onClick={() => { setPage(1); setFilter(`${q.trim() ? `&q=${encodeURIComponent(q.trim())}` : ''}${accountId ? `&accountId=${accountId}` : ''}`); }}>查找账号</Button>
    <Button disabled={locked} onClick={() => { setPage(1); setQ(''); setAccountId(null); setFilter(''); }}>清空查询</Button>
    <Button disabled={locked} onClick={accounts.refresh}>刷新账号</Button>
  </Space>
    <Typography.Paragraph>已选择 {input.accountIds.length} 个不同账号（最多 100）：{input.accountIds.join('、') || '尚未选择'}</Typography.Paragraph>
    <Space wrap>{input.accountIds.map(id => <Button key={id} disabled={locked} onClick={() => update({ ...input, accountIds: input.accountIds.filter(x => x !== id) })}>取消账号 {id}</Button>)}</Space>
    <Table<CeraAccount> rowKey="accountId" loading={accounts.busy} dataSource={accounts.data?.items ?? []} locale={{ emptyText: '没有匹配的账号' }} scroll={{ x: 750 }}
      pagination={{ current: page, total: accounts.data?.total ?? 0, pageSize: 20, showSizeChanger: false, onChange: setPage }} columns={[
        { title: '选择', render: (_, row) => <Checkbox aria-label={`选择账号 ${row.accountId}`} checked={input.accountIds.includes(row.accountId)} disabled={locked || (!input.accountIds.includes(row.accountId) && input.accountIds.length >= 100)} onChange={e => update({ ...input, accountIds: e.target.checked ? [...input.accountIds, row.accountId] : input.accountIds.filter(x => x !== row.accountId) })} /> },
        { title: '账号 ID', dataIndex: 'accountId' }, { title: '登录名', dataIndex: 'loginName' }, { title: '当前余额', render: (_, row) => <Wallet value={row.balances} /> },
      ]} />
  </>;
}
