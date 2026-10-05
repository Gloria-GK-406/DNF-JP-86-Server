import { useEffect, useRef, useState } from 'react';
import { Button, Card, Descriptions, Modal, Popconfirm, Space, Table, Tabs, Tag, Typography } from 'antd';
import { api, ApiError } from './api';
import { useGmRead } from './useGmRead';
import { NotiHint, useNotiBar } from './NotiBar';
import { emptyMail, readPending, type MailDocument, type MailHistory, type MailInput, type MailReceipt, type MailSendResult, type PendingMail } from './mail';
import { MailAttachmentSummary } from './MailAttachments';
import { MailComposer, MailTemplateEditor, PreparedAttachments, useMailAction, type MailAccess, type SendOutcome } from './MailEditors';

type EditorSession = { id: string; input: MailInput; revision?: number; dirty: boolean };
function mailSummary(input: MailInput) { return `${input.items.length} 项附件 · 每人 ${input.gold} 金币`; }

export function OfficialMailView({ administratorId, ...access }: MailAccess & { administratorId: number }) {
  const noti = useNotiBar();
  const storageKey = `dfo.gm.mail.pending.${administratorId}`;
  const [tab, setTab] = useState('history');
  const [editor, setEditor] = useState<EditorSession | null>(null);
  const [templateEditor, setTemplateEditor] = useState<{ document?: MailDocument } | null>(null);
  const [pending, setPending] = useState<PendingMail | null>(() => readPending(storageKey));
  const [pendingOpen, setPendingOpen] = useState(false);
  const [result, setResult] = useState<MailSendResult>();
  const [busy, setBusy] = useState(false);
  const generation = useRef(0); const alive = useRef(true);
  const draft = useGmRead<MailDocument>('mail/draft', access.onExpired, access.onDenied);
  const templates = useGmRead<MailDocument[]>(tab === 'templates' ? 'mail/templates' : null, access.onExpired, access.onDenied);
  const [historyPage, setHistoryPage] = useState(1);
  const history = useGmRead<MailHistory>(`mail/operations?page=${historyPage}&pageSize=20`, access.onExpired, access.onDenied);
  const templateAction = useMailAction(access);
  const hasDraft = draft.data && (draft.data.revision > 0 || draft.data.input.title || draft.data.input.body || draft.data.input.gold || draft.data.input.items.length || draft.data.input.recipientIds.length);
  useEffect(() => { alive.current = true; return () => { alive.current = false; generation.current++; }; }, []);
  function openEditor(input: MailInput = emptyMail(), dirty = false) {
    if (pending || busy) return;
    setEditor({ id: crypto.randomUUID(), input: structuredClone(input), revision: draft.data?.revision, dirty });
  }
  function forgetPending() { setPending(null); try { sessionStorage.removeItem(storageKey); } catch { /* A receipt remains available from the server. */ } }
  function refused(failure: unknown) {
    if (failure instanceof ApiError && failure.status === 401) access.onExpired();
    else if (failure instanceof ApiError && failure.status === 403) access.onDenied();
    else noti.failure(failure);
  }
  async function send(snapshot: PendingMail): Promise<SendOutcome> {
    const epoch = ++generation.current; setBusy(true);
    try { sessionStorage.setItem(storageKey, JSON.stringify(snapshot)); }
    catch { noti.error('浏览器无法保存待确认请求，请启用会话存储后重试。'); setBusy(false); return 'rejected'; }
    setPending(snapshot);
    try {
      const delivered = await api<MailSendResult>('mail/send', { ...snapshot.input, requestId: snapshot.requestId });
      if (!alive.current || epoch !== generation.current) return 'unknown';
      forgetPending(); setEditor(null); setPendingOpen(false); history.refresh();
      noti.success(delivered.replayed ? '已恢复原发送结果，没有重复发放。' : '整批邮件已提交。');
      return 'committed';
    } catch (failure) {
      if (!alive.current || epoch !== generation.current) return 'unknown';
      if (failure instanceof ApiError && (failure.status === 401 || failure.status === 403)) { refused(failure); return 'unknown'; }
      if (failure instanceof ApiError && failure.outcome !== 'unknown' && (failure.status < 500 || failure.outcome === 'rolledBack' || failure.outcome === 'rejected')) {
        forgetPending(); setPendingOpen(false);
        if (!editor) setEditor({ id: crypto.randomUUID(), input: structuredClone(snapshot.input), revision: draft.data?.revision, dirty: true });
        refused(failure); return 'rejected';
      }
      setEditor(null); return 'unknown';
    } finally { if (alive.current && epoch === generation.current) setBusy(false); }
  }
  async function queryPending() {
    if (!pending || busy) return;
    const epoch = ++generation.current; setBusy(true);
    try {
      const receipt = await api<MailReceipt>(`mail/operations/${pending.requestId}`);
      if (!alive.current || epoch !== generation.current) return;
      setResult({ receipt, replayed: true, reminder: 'unconfirmed' }); forgetPending(); setPendingOpen(false);
      noti.success('已找到持久化发送结果。'); history.refresh();
    } catch (failure) { if (alive.current && epoch === generation.current) refused(failure); }
    finally { if (alive.current && epoch === generation.current) setBusy(false); }
  }
  return <Space orientation="vertical" size="large" style={{ width: '100%' }}>
    {pending && !busy && <NotiHint noticeKey={`mail:${pending.requestId}`} title="结果暂未确认。请查询原编号或用原内容重试，勿新建编号再次发送。"
      action={{ label: '处理待确认发送', onClick: () => setPendingOpen(true) }} />}
    <Space wrap>
      <Button type="primary" disabled={!!pending || busy} onClick={() => openEditor()}>发送邮件</Button>
      <Button onClick={() => setTemplateEditor({})}>创建模板</Button>
      {!!hasDraft && <Button disabled={!!pending || busy} onClick={() => draft.data && openEditor(draft.data.input)}>继续草稿</Button>}
      {draft.error && <Button onClick={draft.refresh}>重新读取草稿</Button>}
      {pending && <Button disabled={busy} onClick={() => setPendingOpen(true)}>待确认发送（1）</Button>}
    </Space>
    <Tabs activeKey={tab} onChange={setTab} items={[
      { key: 'history', label: '发送记录', children: <Card title="我的发送记录" extra={<Button loading={history.busy} onClick={history.refresh}>刷新发送记录</Button>}>
        <Table<MailReceipt> rowKey="requestId" loading={history.busy} dataSource={history.data?.items ?? []} scroll={{ x: 950 }}
          locale={{ emptyText: '尚无发送记录' }} pagination={{ current: historyPage, pageSize: 20, total: history.data?.total ?? 0, onChange: setHistoryPage, showSizeChanger: false }}
          columns={[{ title: '提交时间', dataIndex: 'committedAt', render: value => new Date(value).toLocaleString('zh-CN') },
            { title: '邮件标题', render: (_, value) => value.input.title || '无标题' },
            { title: '收件角色数', render: (_, value) => value.recipients.length }, { title: '发放摘要', render: (_, value) => mailSummary(value.input) },
            { title: '结果', render: () => <Tag color="green">已提交</Tag> },
            { title: '详情', render: (_, value) => <Button onClick={() => setResult({ receipt: value, replayed: true, reminder: 'unconfirmed' })}>查看发送结果</Button> }]} />
      </Card> },
      { key: 'templates', label: '邮件模板', children: <Card title="我的邮件模板" extra={<Button loading={templates.busy} onClick={templates.refresh}>刷新模板</Button>}>
        <Table<MailDocument> rowKey="id" loading={templates.busy} dataSource={templates.data ?? []} pagination={false} scroll={{ x: 850 }} locale={{ emptyText: '尚无邮件模板' }}
          columns={[{ title: '模板名称', dataIndex: 'name' }, { title: '邮件标题', render: (_, value) => value.input.title || '无标题' },
            { title: '内容摘要', render: (_, value) => mailSummary(value.input) },
            { title: '操作', render: (_, value) => <Space wrap>
              <Button disabled={!!pending || busy} onClick={() => openEditor({ ...value.input, recipientIds: [] }, true)}>用此模板发送</Button>
              <Button disabled={templateAction.busy} onClick={() => setTemplateEditor({ document: value })}>编辑模板</Button>
              <Popconfirm title="删除这个邮件模板？" description="已发送的邮件不会受影响。" okText="删除" cancelText="取消" onConfirm={() => templateAction.run(
                () => api<MailDocument>(`mail/templates/${value.id}`, { ...emptyMail(), expectedRevision: value.revision }, 'DELETE'),
                () => { templates.refresh(); noti.success('模板已删除。'); })}>
                <Button danger disabled={templateAction.busy}>删除模板</Button>
              </Popconfirm>
            </Space> }]} />
      </Card> },
    ]} />
    {editor && <MailComposer key={editor.id} {...access} initial={editor.input} draftRevision={editor.revision ?? draft.data?.revision} initialDirty={editor.dirty} sending={busy}
      onSend={send} onSavedDraft={draft.refresh} onClose={() => setEditor(null)} />}
    {templateEditor && <MailTemplateEditor {...access} document={templateEditor.document} onClose={() => setTemplateEditor(null)}
      onSaved={() => { setTemplateEditor(null); setTab('templates'); templates.refresh(); }} />}
    <Modal title="发送结果待确认" open={pendingOpen && !!pending} onCancel={() => { if (!busy) setPendingOpen(false); }} width={850} className="mail-workflow-modal"
      closable={!busy} keyboard={!busy} mask={{ closable: false }} footer={<Space>
        <Button disabled={busy} onClick={() => setPendingOpen(false)}>关闭</Button><Button loading={busy} onClick={() => void queryPending()}>查询原编号结果</Button>
        <Button disabled={busy} onClick={() => pending && void send(pending)}>按原内容安全重试</Button>
      </Space>}>
      {pending && <><Typography.Paragraph type="secondary">保留原编号和内容，查询或安全重试不会重复发放。</Typography.Paragraph>
        <Typography.Paragraph copyable>{pending.requestId}</Typography.Paragraph><Typography.Paragraph>收件角色 ID：{pending.input.recipientIds.join('、')}</Typography.Paragraph>
        <Typography.Paragraph style={{ whiteSpace: 'pre-wrap' }}>{pending.input.title}<br />{pending.input.body}</Typography.Paragraph>
        <Typography.Paragraph>每人金币：{pending.input.gold}</Typography.Paragraph>
        <MailAttachmentSummary {...access} items={pending.input.items} /></>}
    </Modal>
    <Modal title="发送记录详情" open={!!result} onCancel={() => setResult(undefined)} footer={<Button onClick={() => setResult(undefined)}>关闭详情</Button>}
      width={1000} className="mail-workflow-modal" destroyOnHidden>
      {result && <ReceiptCard {...access} value={result.receipt} reminder={result.reminder} />}
    </Modal>
  </Space>;
}

function ReceiptCard({ value, reminder, onExpired, onDenied }: { value: MailReceipt; reminder: string; onExpired: () => void; onDenied: () => void }) {
  return <Card title="持久化发送结果"><Typography.Paragraph>邮件已提交，玩家可在邮箱领取附件。{reminder === 'unconfirmed' ? '在线提醒未确认；不会撤销或重复发放。' : '在线提醒已尝试，不代表客户端已读或附件已领取。'}</Typography.Paragraph>
    <Descriptions column={1} style={{ marginTop: 16 }} items={[{ key: 'id', label: '操作编号', children: <Typography.Text copyable>{value.requestId}</Typography.Text> },
      { key: 'title', label: '邮件标题', children: value.input.title || '无标题' }, { key: 'gold', label: '每人金币', children: value.input.gold },
      { key: 'time', label: '提交时间', children: new Date(value.committedAt).toLocaleString('zh-CN') }, { key: 'expiry', label: '到期时间', children: new Date(value.expiresAt).toLocaleString('zh-CN') },
      { key: 'body', label: '发送正文', children: <span style={{ whiteSpace: 'pre-wrap' }}>{value.input.body}</span> }]} />
    {value.items ? <PreparedAttachments items={value.items} /> : <MailAttachmentSummary items={value.input.items} onExpired={onExpired} onDenied={onDenied} />}
    <Table rowKey={x => x.character.characterId} pagination={false} dataSource={value.recipients} columns={[{ title: '角色 ID', render: (_, x) => x.character.characterId },
      { title: '姓名', render: (_, x) => x.character.name }, { title: '邮件 ID', dataIndex: 'mailId' }, { title: '装备 UID', render: (_, x) => x.equipmentUids.join('、') || '无装备附件' }]} />
  </Card>;
}
