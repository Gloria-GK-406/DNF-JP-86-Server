import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Card, Checkbox, Descriptions, Input, InputNumber, Modal, Space, Table, Typography } from 'antd';
import { api, ApiError, type Balances, type PlayerPage } from './api';
import { useGmRead } from './useGmRead';
import { currencyNames, readPendingCera, type CeraAccount, type CeraChange, type CeraCurrency, type CeraHistory, type CeraInput, type CeraPreview, type CeraReceipt, type CeraResult, type PendingCera } from './cera';

function Wallet({ value }: { value: Balances }) {
  return <span>点券 {value.cera} / 代币点券 {value.tokenCera} / 欢乐代币点券 {value.happyTokenCera}</span>;
}
function Changes({ accounts }: { accounts: CeraChange[] }) {
  return <Table<CeraChange> rowKey="accountId" pagination={false} dataSource={accounts} columns={[
    { title: '账号 ID', dataIndex: 'accountId' }, { title: '登录名', dataIndex: 'loginName' },
    { title: '发放前', render: (_, row) => <Wallet value={row.before} /> }, { title: '发放后', render: (_, row) => <Wallet value={row.after} /> },
  ]} />;
}
export function CeraGrantView({ administratorId, onExpired, onDenied }: { administratorId: number; onExpired: () => void; onDenied: () => void }) {
  const storageKey = `dfo.gm.cera.pending.${administratorId}`;
  const [input, setInput] = useState<CeraInput>({ accountIds: [], currency: 'Cera', amount: 1, reason: '' });
  const [pending, setPending] = useState<PendingCera | null>(() => readPendingCera(storageKey));
  const [preview, setPreview] = useState<CeraPreview>(); const [result, setResult] = useState<CeraResult>();
  const [busy, setBusy] = useState(false); const [error, setError] = useState(''); const [notice, setNotice] = useState('');
  const generation = useRef(0); const alive = useRef(true); const edits = useRef(0); const previewId = useRef('');
  const [page, setPage] = useState(1); const [q, setQ] = useState(''); const [accountId, setAccountId] = useState<number | null>(null);
  const [filter, setFilter] = useState(''); const [historyPage, setHistoryPage] = useState(1);
  const accounts = useGmRead<PlayerPage<CeraAccount>>(`cera/accounts?page=${page}&pageSize=20${filter}`, onExpired, onDenied);
  const history = useGmRead<CeraHistory>(`cera/operations?page=${historyPage}&pageSize=20`, onExpired, onDenied);
  useEffect(() => { alive.current = true; return () => { alive.current = false; generation.current++; }; }, []);
  const locked = busy || !!preview || !!pending;
  function update(value: CeraInput) { edits.current++; setInput(value); setError(''); setNotice(''); }
  function forgetPending() { setPending(null); try { sessionStorage.removeItem(storageKey); } catch { /* A committed receipt stays visible in memory. */ } }
  function refused(failure: unknown) {
    setError((failure as Error).message);
    if (failure instanceof ApiError && failure.status === 401) onExpired();
    if (failure instanceof ApiError && failure.status === 403) onDenied();
  }
  async function run<T>(action: () => Promise<T>, accept: (value: T) => void) {
    const epoch = ++generation.current; setBusy(true); setError(''); setNotice('');
    try { const value = await action(); if (alive.current && epoch === generation.current) accept(value); }
    catch (failure) { if (alive.current && epoch === generation.current) refused(failure); }
    finally { if (alive.current && epoch === generation.current) setBusy(false); }
  }
  async function grant(snapshot: PendingCera) {
    const epoch = ++generation.current; setBusy(true); setError(''); setNotice('');
    try { sessionStorage.setItem(storageKey, JSON.stringify(snapshot)); }
    catch { setError('浏览器无法保存待确认请求，请启用会话存储后重试。'); setBusy(false); return; }
    setPending(snapshot);
    try {
      const delivered = await api<CeraResult>('cera/grant', { ...snapshot.input, requestId: snapshot.requestId });
      if (!alive.current || epoch !== generation.current) return;
      setResult(delivered); forgetPending(); setPreview(undefined); history.refresh(); accounts.refresh();
      setNotice(delivered.replayed ? '已恢复原发放结果，没有重复到账。' : '整批点券已到账。');
    } catch (failure) {
      if (!alive.current || epoch !== generation.current) return;
      if (failure instanceof ApiError && (failure.status === 401 || failure.status === 403)) { refused(failure); }
      else if (failure instanceof ApiError && failure.outcome !== 'unknown' && (failure.status < 500 || failure.outcome === 'rolledBack' || failure.outcome === 'rejected')) {
        forgetPending(); setPreview(undefined); refused(failure);
      } else { setPreview(undefined); setError('结果暂未确认。请查询原编号或用原内容重试，勿新建编号再次发放。'); }
    } finally { if (alive.current && epoch === generation.current) setBusy(false); }
  }
  function queryPending() {
    if (!pending) return;
    void run(() => api<CeraReceipt>(`cera/operations/${pending.requestId}`), value => {
      setResult({ receipt: value, replayed: true, reminder: 'unconfirmed' }); forgetPending(); setPreview(undefined);
      setNotice('已找到持久化发放结果。'); history.refresh(); accounts.refresh();
    });
  }
  return <Space orientation="vertical" size="large" style={{ width: '100%' }}>
    {error && <Alert type="error" title={error} showIcon />}{notice && <Alert type="success" title={notice} showIcon />}
    {(accounts.error || history.error) && <Alert type="error" title={accounts.error ?? history.error} showIcon />}
    {pending && <Card title="发放结果待确认"><Alert type="warning" title="保留原编号和内容，查询或安全重试不会重复到账。" showIcon />
      <Typography.Paragraph copyable>{pending.requestId}</Typography.Paragraph>
      <Typography.Paragraph>账号 ID：{pending.input.accountIds.join('、')}；{currencyNames[pending.input.currency]}每账号增加 {pending.input.amount}；原因：{pending.input.reason}</Typography.Paragraph>
      <Space><Button disabled={busy} onClick={queryPending}>查询原编号结果</Button><Button disabled={busy} onClick={() => void grant(pending)}>按原内容安全重试</Button></Space></Card>}
    <Card title="点券编辑"><Alert type="info" title="按账号发放，每账号只到账一次；支持离线和无角色账号，整批提交。" showIcon />
      <Space orientation="vertical" style={{ width: '100%', marginTop: 16 }}>
        <label htmlFor="cera-currency">币种</label><select id="cera-currency" disabled={locked} value={input.currency} onChange={e => update({ ...input, currency: e.target.value as CeraCurrency })}>
          {Object.entries(currencyNames).map(([key, label]) => <option key={key} value={key}>{label}</option>)}
        </select>
        <label htmlFor="cera-amount">每账号增量</label><InputNumber id="cera-amount" min={1} max={2147483647} precision={0} disabled={locked} value={input.amount} onChange={v => update({ ...input, amount: v ?? 0 })} />
        <label htmlFor="cera-reason">发放原因</label><Input.TextArea id="cera-reason" rows={3} maxLength={256} disabled={locked} value={input.reason} onChange={e => update({ ...input, reason: e.target.value })} />
        <Button type="primary" disabled={locked} onClick={() => { const version = edits.current; void run(() => api<CeraPreview>('cera/preview', structuredClone(input)), value => {
          if (version === edits.current) { previewId.current = crypto.randomUUID(); setPreview(value); }
        }); }}>预览并核对</Button>
      </Space>
    </Card>
    <Card title="选择账号"><Space wrap>
      <Input aria-label="搜索登录名" value={q} onChange={e => setQ(e.target.value)} placeholder="登录名字面搜索" disabled={locked} />
      <InputNumber aria-label="定位账号 ID" value={accountId} min={1} max={2147483647} precision={0} onChange={setAccountId} disabled={locked} />
      <Button disabled={locked} onClick={() => { setPage(1); setFilter(`${q.trim() ? `&q=${encodeURIComponent(q.trim())}` : ''}${accountId ? `&accountId=${accountId}` : ''}`); }}>查找账号</Button>
      <Button disabled={locked} onClick={accounts.refresh}>刷新账号</Button>
    </Space>
      <Typography.Paragraph>已选择 {input.accountIds.length} 个不同账号（最多 100）：{input.accountIds.join('、') || '尚未选择'}</Typography.Paragraph>
      <Space wrap>{input.accountIds.map(id => <Button key={id} disabled={locked} onClick={() => update({ ...input, accountIds: input.accountIds.filter(x => x !== id) })}>取消账号 {id}</Button>)}</Space>
      <Table<CeraAccount> rowKey="accountId" loading={accounts.busy} dataSource={accounts.data?.items ?? []} locale={{ emptyText: '没有匹配的账号' }}
        pagination={{ current: page, total: accounts.data?.total ?? 0, pageSize: 20, showSizeChanger: false, onChange: setPage }} columns={[
          { title: '选择', render: (_, row) => <Checkbox aria-label={`选择账号 ${row.accountId}`} checked={input.accountIds.includes(row.accountId)} disabled={locked || (!input.accountIds.includes(row.accountId) && input.accountIds.length >= 100)} onChange={e => update({ ...input, accountIds: e.target.checked ? [...input.accountIds, row.accountId] : input.accountIds.filter(x => x !== row.accountId) })} /> },
          { title: '账号 ID', dataIndex: 'accountId' }, { title: '登录名', dataIndex: 'loginName' }, { title: '当前余额', render: (_, row) => <Wallet value={row.balances} /> },
        ]} />
    </Card>
    <Modal title="核对点券发放" open={!!preview} onCancel={() => { if (!busy && !pending) setPreview(undefined); }} destroyOnHidden
      footer={<Space><Button disabled={busy || !!pending} onClick={() => setPreview(undefined)}>返回修改</Button><Button type="primary" disabled={busy || !!pending} onClick={() => preview && void grant({ requestId: previewId.current, input: structuredClone(preview.input) })}>确认整批发放</Button></Space>}>
      {preview && <><Alert type="warning" title="提交按当前余额结算，以发放结果为准。" showIcon /><Typography.Paragraph>{currencyNames[preview.input.currency]}：每账号增加 {preview.input.amount}，{preview.accounts.length} 个不同账号，总增量 {preview.totalAmount}；原因：{preview.input.reason}</Typography.Paragraph><Changes accounts={preview.accounts} /></>}
    </Modal>
    {result && <Card title="持久化发放结果"><Descriptions column={1} items={[
      { key: 'id', label: '操作编号', children: <Typography.Text copyable>{result.receipt.requestId}</Typography.Text> },
      { key: 'actor', label: '管理员', children: result.receipt.administratorName }, { key: 'time', label: '提交 UTC 时间', children: result.receipt.committedAt },
      { key: 'reason', label: '发放内容', children: `${currencyNames[result.receipt.input.currency]}，每账号增加 ${result.receipt.input.amount}；${result.receipt.input.reason}` },
    ]} /><Changes accounts={result.receipt.accounts} /><Typography.Paragraph>{result.reminder === 'unconfirmed' ? '已到账，在线提醒未确认。' : '已到账；已尝试在线提醒，离线或无选中角色时无需提醒。'}实际客户端显示仍需玩家核对。</Typography.Paragraph></Card>}
    <Card title="我的点券发放记录" extra={<Button onClick={history.refresh}>刷新记录</Button>}><Table<CeraReceipt> rowKey="requestId" dataSource={history.data?.items ?? []} loading={history.busy} locale={{ emptyText: '尚无发放记录' }}
      pagination={{ current: historyPage, total: history.data?.total ?? 0, pageSize: 20, showSizeChanger: false, onChange: setHistoryPage }} columns={[
        { title: '操作编号', dataIndex: 'requestId' }, { title: '提交 UTC 时间', dataIndex: 'committedAt' },
        { title: '币种/每账号增量', render: (_, row) => `${currencyNames[row.input.currency]} / ${row.input.amount}` },
        { title: '账号数量', render: (_, row) => row.accounts.length },
        { title: '详情', render: (_, row) => <Button onClick={() => setResult({ receipt: row, replayed: true, reminder: 'unconfirmed' })}>查看余额详情</Button> },
      ]} /></Card>
  </Space>;
}
