import { useEffect, useRef, useState } from 'react';
import { Button, Card, Descriptions, Modal, Space, Table, Tag, Typography } from 'antd';
import { api, ApiError } from './api';
import { useGmRead } from './useGmRead';
import { NotiHint, useNotiBar } from './NotiBar';
import { currencyNames, readPendingCera, type CeraHistory, type CeraInput, type CeraReceipt, type CeraResult, type PendingCera } from './cera';
import { CeraGrantEditor, Changes, type CeraAccess, type GrantOutcome } from './CeraGrantEditor';

type EditorSession = { id: string; input: CeraInput; dirty: boolean };
export function CeraGrantView({ administratorId, ...access }: CeraAccess & { administratorId: number }) {
  const noti = useNotiBar(); const storageKey = `dfo.gm.cera.pending.${administratorId}`;
  const [editor, setEditor] = useState<EditorSession | null>(null);
  const [pending, setPending] = useState<PendingCera | null>(() => readPendingCera(storageKey));
  const [pendingOpen, setPendingOpen] = useState(false); const [result, setResult] = useState<CeraResult>();
  const [busy, setBusy] = useState(false); const generation = useRef(0); const alive = useRef(true);
  const [historyPage, setHistoryPage] = useState(1);
  const history = useGmRead<CeraHistory>(`cera/operations?page=${historyPage}&pageSize=20`, access.onExpired, access.onDenied);
  useEffect(() => { alive.current = true; return () => { alive.current = false; generation.current++; }; }, []);
  function forgetPending() { setPending(null); try { sessionStorage.removeItem(storageKey); } catch { /* A receipt remains available from the server. */ } }
  function refused(failure: unknown) {
    if (failure instanceof ApiError && failure.status === 401) access.onExpired();
    else if (failure instanceof ApiError && failure.status === 403) access.onDenied();
    else noti.failure(failure);
  }
  async function grant(snapshot: PendingCera): Promise<GrantOutcome> {
    if (busy) return 'unknown';
    const epoch = ++generation.current; setBusy(true);
    try { sessionStorage.setItem(storageKey, JSON.stringify(snapshot)); }
    catch { noti.error('浏览器无法保存待确认请求，请启用会话存储后重试。'); setBusy(false); return 'rejected'; }
    setPending(snapshot);
    try {
      const delivered = await api<CeraResult>('cera/grant', { ...snapshot.input, requestId: snapshot.requestId });
      if (!alive.current || epoch !== generation.current) return 'unknown';
      forgetPending(); setEditor(null); setPendingOpen(false); history.refresh();
      noti.success(delivered.replayed ? '已恢复原发放结果，没有重复到账。' : '整批点券已到账。');
      return 'committed';
    } catch (failure) {
      if (!alive.current || epoch !== generation.current) return 'unknown';
      if (failure instanceof ApiError && (failure.status === 401 || failure.status === 403)) { refused(failure); return 'unknown'; }
      if (failure instanceof ApiError && failure.outcome !== 'unknown' && (failure.status < 500 || failure.outcome === 'rolledBack' || failure.outcome === 'rejected')) {
        forgetPending(); setPendingOpen(false);
        if (!editor) setEditor({ id: crypto.randomUUID(), input: structuredClone(snapshot.input), dirty: true });
        refused(failure); return 'rejected';
      }
      setEditor(null); return 'unknown';
    } finally { if (alive.current && epoch === generation.current) setBusy(false); }
  }
  async function queryPending() {
    if (!pending || busy) return;
    const epoch = ++generation.current; setBusy(true);
    try {
      const receipt = await api<CeraReceipt>(`cera/operations/${pending.requestId}`);
      if (!alive.current || epoch !== generation.current) return;
      setResult({ receipt, replayed: true, reminder: 'unconfirmed' }); forgetPending(); setPendingOpen(false);
      noti.success('已找到持久化发放结果。'); history.refresh();
    } catch (failure) { if (alive.current && epoch === generation.current) refused(failure); }
    finally { if (alive.current && epoch === generation.current) setBusy(false); }
  }
  return <Space orientation="vertical" size="large" style={{ width: '100%' }}>
    {pending && !busy && <NotiHint noticeKey={`cera:${pending.requestId}`} title="结果暂未确认。请查询原编号或用原内容重试，勿新建编号再次发放。"
      action={{ label: '处理待确认发放', onClick: () => setPendingOpen(true) }} />}
    <Space wrap>
      <Button type="primary" disabled={!!pending || busy} onClick={() => setEditor({ id: crypto.randomUUID(), input: { accountIds: [], currency: 'Cera', amount: 1, reason: '' }, dirty: false })}>发放点券</Button>
      {pending && <Button disabled={busy} onClick={() => setPendingOpen(true)}>待确认发放（1）</Button>}
    </Space>
    <Card title="我的点券发放记录" extra={<Button loading={history.busy} onClick={history.refresh}>刷新记录</Button>}>
      <Table<CeraReceipt> rowKey="requestId" dataSource={history.data?.items ?? []} loading={history.busy} scroll={{ x: 950 }} locale={{ emptyText: '尚无发放记录' }}
        pagination={{ current: historyPage, total: history.data?.total ?? 0, pageSize: 20, showSizeChanger: false, onChange: setHistoryPage }} columns={[
          { title: '提交时间', dataIndex: 'committedAt', render: value => new Date(value).toLocaleString('zh-CN') },
          { title: '币种', render: (_, row) => currencyNames[row.input.currency] }, { title: '每账号增量', render: (_, row) => row.input.amount },
          { title: '账号数量', render: (_, row) => row.accounts.length }, { title: '发放原因', render: (_, row) => row.input.reason },
          { title: '结果', render: () => <Tag color="green">已到账</Tag> },
          { title: '详情', render: (_, row) => <Button onClick={() => setResult({ receipt: row, replayed: true, reminder: 'unconfirmed' })}>查看余额详情</Button> },
        ]} />
    </Card>
    {editor && <CeraGrantEditor key={editor.id} {...access} initial={editor.input} initialDirty={editor.dirty} granting={busy} onGrant={grant} onClose={() => setEditor(null)} />}
    <Modal title="发放结果待确认" open={pendingOpen && !!pending} onCancel={() => { if (!busy) setPendingOpen(false); }} width={850} className="cera-workflow-modal" destroyOnHidden
      closable={!busy} keyboard={!busy} mask={{ closable: false }} footer={<Space wrap>
        <Button disabled={busy} onClick={() => setPendingOpen(false)}>关闭</Button><Button loading={busy} onClick={() => void queryPending()}>查询原编号结果</Button>
        <Button disabled={busy} onClick={() => pending && void grant(pending)}>按原内容安全重试</Button>
      </Space>}>
      {pending && <><Typography.Paragraph type="secondary">保留原编号和内容，查询或安全重试不会重复到账。</Typography.Paragraph>
        <Typography.Paragraph copyable>{pending.requestId}</Typography.Paragraph>
        <Typography.Paragraph>账号 ID：{pending.input.accountIds.join('、')}；{currencyNames[pending.input.currency]}每账号增加 {pending.input.amount}；原因：{pending.input.reason}</Typography.Paragraph></>}
    </Modal>
    <Modal title="点券发放记录详情" open={!!result} onCancel={() => setResult(undefined)} footer={<Button onClick={() => setResult(undefined)}>关闭详情</Button>}
      width={1040} className="cera-workflow-modal" destroyOnHidden>
      {result && <Card title="持久化发放结果"><Descriptions column={1} items={[
        { key: 'id', label: '操作编号', children: <Typography.Text copyable>{result.receipt.requestId}</Typography.Text> },
        { key: 'actor', label: '管理员', children: result.receipt.administratorName }, { key: 'time', label: '提交时间', children: new Date(result.receipt.committedAt).toLocaleString('zh-CN') },
        { key: 'reason', label: '发放内容', children: `${currencyNames[result.receipt.input.currency]}，每账号增加 ${result.receipt.input.amount}；${result.receipt.input.reason}` },
      ]} /><Changes accounts={result.receipt.accounts} /><Typography.Paragraph>{result.reminder === 'unconfirmed' ? '已到账，在线提醒未确认。' : '已到账；已尝试在线提醒，离线或无选中角色时无需提醒。'}实际客户端显示仍需玩家核对。</Typography.Paragraph></Card>}
    </Modal>
  </Space>;
}
