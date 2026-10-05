import { useEffect, useRef, useState } from 'react';
import { Button, Card, Checkbox, Descriptions, Input, InputNumber, Modal, Select, Space, Table, Tag, Typography } from 'antd';
import { api, ApiError, type PlayerPage } from './api';
import { useGmRead } from './useGmRead';
import { NotiHint, useNotiBar } from './NotiBar';
import { amplifyTypes, emptyMail, readPending, type MailDocument, type MailHistory, type MailInput, type MailPreview, type MailReceipt, type MailRecipient, type MailSendResult, type PendingMail } from './mail';
import { MailAttachments, MailAttachmentSummary } from './MailAttachments';

export function OfficialMailView({ administratorId, onExpired, onDenied }: { administratorId: number; onExpired: () => void; onDenied: () => void }) {
  const noti = useNotiBar();
  const storageKey = `dfo.gm.mail.pending.${administratorId}`;
  const [input, setInput] = useState<MailInput>(emptyMail);
  const [draftRevision, setDraftRevision] = useState(0); const [loaded, setLoaded] = useState(false); const [dirty, setDirty] = useState(false);
  const [templateId, setTemplateId] = useState<string>(); const [templateName, setTemplateName] = useState('');
  const [preview, setPreview] = useState<MailPreview>(); const [pending, setPending] = useState<PendingMail | null>(() => readPending(storageKey));
  const [result, setResult] = useState<MailSendResult>(); const [busy, setBusy] = useState(false);
  const generation = useRef(0); const alive = useRef(true); const edits = useRef(0);
  const previewRequestId = useRef('');
  const draft = useGmRead<MailDocument>('mail/draft', onExpired, onDenied);
  const templates = useGmRead<MailDocument[]>('mail/templates', onExpired, onDenied);
  const [historyPage, setHistoryPage] = useState(1); const history = useGmRead<MailHistory>(`mail/operations?page=${historyPage}&pageSize=20`, onExpired, onDenied);
  useEffect(() => { alive.current = true; return () => { alive.current = false; generation.current++; }; }, []);
  useEffect(() => { if (draft.data && !loaded) { setInput(structuredClone(draft.data.input)); setDraftRevision(draft.data.revision); setLoaded(true); } }, [draft.data, loaded]);
  const locked = busy || !!pending || !!preview || !loaded;
  function update(value: MailInput) { edits.current++; setInput(value); setDirty(true); }
  function forgetPending() { setPending(null); try { sessionStorage.removeItem(storageKey); } catch { /* Current in-memory receipt still remains available. */ } }
  function refused(failure: unknown) {
    if (failure instanceof ApiError && failure.status === 401) onExpired();
    else if (failure instanceof ApiError && failure.status === 403) onDenied();
    else noti.failure(failure);
  }
  async function run<T>(action: () => Promise<T>, accept: (value: T) => void) {
    const epoch = ++generation.current; setBusy(true);
    try { const value = await action(); if (alive.current && epoch === generation.current) accept(value); }
    catch (failure) { if (alive.current && epoch === generation.current) refused(failure); }
    finally { if (alive.current && epoch === generation.current) setBusy(false); }
  }
  function saveDraft() {
    const version = edits.current; const snapshot = structuredClone(input);
    void run(() => api<MailDocument>('mail/draft', { ...snapshot, expectedRevision: draftRevision }, 'PUT'), value => {
      setDraftRevision(value.revision); if (version === edits.current) setDirty(false);
      noti.success(version === edits.current ? '草稿已保存。' : '先前版本已保存，当前更改尚未保存。');
    });
  }
  function loadDraft() {
    void run(() => api<MailDocument>('mail/draft'), value => { edits.current++; setInput(structuredClone(value.input)); setDraftRevision(value.revision); setDirty(false); noti.success('已加载保存的草稿。'); setLoaded(true); });
  }
  function saveTemplate(overwrite: boolean) {
    const selected = templates.data?.find(x => x.id === templateId);
    if (overwrite && !selected) { noti.error('请先选择需要覆盖的模板。'); return; }
    void run(() => api<MailDocument>(overwrite ? `mail/templates/${selected!.id}` : 'mail/templates',
      { ...structuredClone(input), recipientIds: [], name: templateName, expectedRevision: overwrite ? selected!.revision : 0 }, overwrite ? 'PUT' : undefined), value => {
      setTemplateId(value.id); setTemplateName(value.name); noti.success('内容模板已保存，不含收件人。'); templates.refresh();
    });
  }
  function loadTemplate() {
    const selected = templates.data?.find(x => x.id === templateId);
    if (!selected) { noti.error('请选择模板。'); return; }
    update({ ...structuredClone(selected.input), recipientIds: [...input.recipientIds] }); noti.success('已加载模板内容，保留当前收件人。');
  }
  function deleteTemplate() {
    const selected = templates.data?.find(x => x.id === templateId); if (!selected) return;
    void run(() => api<MailDocument>(`mail/templates/${selected.id}`, { ...emptyMail(), expectedRevision: selected.revision }, 'DELETE'), () => {
      setTemplateId(undefined); setTemplateName(''); templates.refresh(); noti.success('模板已删除。');
    });
  }
  async function send(snapshot: PendingMail) {
    const epoch = ++generation.current; setBusy(true);
    setPending(snapshot);
    try {
      sessionStorage.setItem(storageKey, JSON.stringify(snapshot));
    } catch { noti.error('浏览器无法保存待确认请求，请启用会话存储后重试。'); setBusy(false); setPending(null); return; }
    try {
      const delivered = await api<MailSendResult>('mail/send', { ...snapshot.input, requestId: snapshot.requestId });
      if (!alive.current || epoch !== generation.current) return;
      setResult(delivered); forgetPending(); setPreview(undefined); history.refresh(); noti.success(delivered.replayed ? '已恢复原发送结果，没有重复发放。' : '整批邮件已提交。');
    } catch (failure) {
      if (!alive.current || epoch !== generation.current) return;
      if (failure instanceof ApiError && (failure.status === 401 || failure.status === 403)) { refused(failure); }
      else if (failure instanceof ApiError && failure.outcome !== 'unknown' && (failure.status < 500 || failure.outcome === 'rolledBack' || failure.outcome === 'rejected')) {
        forgetPending(); setPreview(undefined); refused(failure);
      } else { setPreview(undefined); }
    } finally { if (alive.current && epoch === generation.current) setBusy(false); }
  }
  function queryPending() {
    if (!pending) return;
    void run(() => api<MailReceipt>(`mail/operations/${pending.requestId}`), value => { setResult({ receipt: value, replayed: true, reminder: 'unconfirmed' }); forgetPending(); noti.success('已找到持久化发送结果。'); history.refresh(); });
  }
  const selectedTemplate = templates.data?.find(x => x.id === templateId);
  return <Space orientation="vertical" size="large" style={{ width: '100%' }}>
    {pending && !busy && <NotiHint noticeKey={`mail:${pending.requestId}`} title="结果暂未确认。请查询原编号或用原内容重试，勿新建编号再次发送。" action={{ label: '查询结果', onClick: queryPending }} />}
    {pending && <Card title="发送结果待确认"><Typography.Paragraph type="secondary">保留原编号和内容，查询或重试不会重复发放。</Typography.Paragraph>
      <Typography.Paragraph copyable>{pending.requestId}</Typography.Paragraph><Typography.Paragraph>收件角色 ID：{pending.input.recipientIds.join('、')}</Typography.Paragraph>
      <Typography.Paragraph style={{ whiteSpace: 'pre-wrap' }}>{pending.input.body}</Typography.Paragraph>
      <MailAttachmentSummary items={pending.input.items} onExpired={onExpired} onDenied={onDenied} />
      <Space><Button disabled={busy} onClick={queryPending}>查询原编号结果</Button><Button disabled={busy} onClick={() => void send(pending)}>按原内容安全重试</Button></Space></Card>}
    <Card title="邮件编辑" extra={<Tag>{dirty ? '尚未保存' : '已加载保存内容'}</Tag>}>
      <Typography.Paragraph type="secondary">支持永久普通装备、永久堆叠物与金币。附件由玩家在邮箱领取，邮件有效期 30 天。</Typography.Paragraph>
      <Space orientation="vertical" style={{ width: '100%', marginTop: 16 }}>
        <label htmlFor="mail-title">邮件标题</label><Input id="mail-title" value={input.title} disabled={locked} onChange={e => update({ ...input, title: e.target.value })} />
        <label htmlFor="mail-body">邮件正文</label><Input.TextArea id="mail-body" value={input.body} disabled={locked} rows={5} onChange={e => update({ ...input, body: e.target.value })} />
        <Typography.Text type="secondary">实际展示含标题和换行，最多 511 个 UTF-8 字节；以预览核对结果为准。</Typography.Text>
        <label htmlFor="mail-gold">每人金币</label><InputNumber id="mail-gold" min={0} max={2147483647} precision={0} value={input.gold} disabled={locked} onChange={value => update({ ...input, gold: value ?? 0 })} />
        <MailAttachments items={input.items} locked={locked} onChange={items => update({ ...input, items })} onExpired={onExpired} onDenied={onDenied} />
        <Space wrap>
          <Button disabled={locked} onClick={saveDraft}>保存草稿</Button><Button disabled={busy || !!preview || !!pending} onClick={loadDraft}>重新加载草稿（覆盖当前编辑）</Button>
          <Button type="primary" disabled={locked} onClick={() => { const version = edits.current; void run(() => api<MailPreview>('mail/preview', structuredClone(input)), value => { if (version === edits.current) { previewRequestId.current = crypto.randomUUID(); setPreview(value); } }); }}>预览并核对</Button></Space>
      </Space>
    </Card>
    <Card title="个人内容模板"><Space wrap>
      <Select aria-label="选择内容模板" style={{ width: 220 }} value={templateId} disabled={locked} placeholder="选择模板" options={templates.data?.map(x => ({ value: x.id, label: x.name }))}
        onChange={value => { setTemplateId(value); setTemplateName(templates.data?.find(x => x.id === value)?.name ?? ''); }} />
      <Button disabled={locked || !selectedTemplate} onClick={loadTemplate}>加载模板（保留收件人）</Button>
      <Input aria-label="模板名称" maxLength={64} style={{ width: 220 }} value={templateName} disabled={locked} placeholder="模板名称" onChange={e => setTemplateName(e.target.value)} />
      <Button disabled={locked} onClick={() => saveTemplate(false)}>新增内容模板</Button><Button disabled={locked || !selectedTemplate} onClick={() => saveTemplate(true)}>覆盖选中模板</Button>
      <Button danger disabled={locked || !selectedTemplate} onClick={deleteTemplate}>删除选中模板</Button><Button disabled={busy} onClick={templates.refresh}>刷新模板</Button>
    </Space></Card>
    <RecipientSelection input={input} update={update} locked={locked} onExpired={onExpired} onDenied={onDenied} />
    <Modal title="发送前核对" open={!!preview} onCancel={() => { if (!busy) setPreview(undefined); }} footer={null} width={800} destroyOnHidden>
      {preview && <><Typography.Paragraph type="secondary">系统官方来源 · 30 天有效 · 整批提交，任何目标或附件失败均回滚</Typography.Paragraph>
        <Typography.Paragraph style={{ whiteSpace: 'pre-wrap', marginTop: 16 }}>{preview.displayText}</Typography.Paragraph>
        <Typography.Paragraph>实际文字 {preview.utf8Bytes} / 511 字节；收件人 {preview.recipients.length}；每人金币 {preview.input.gold}；总金币 {preview.totalGold}；总物品数量 {preview.totalItems}</Typography.Paragraph>
        <Typography.Paragraph>收件角色：{preview.recipients.map(x => `${x.characterId} ${x.name}`).join('、')}</Typography.Paragraph>
        <PreparedAttachments items={preview.items} />
        <Space><Button disabled={busy} onClick={() => setPreview(undefined)}>返回编辑</Button><Button type="primary" loading={busy} onClick={() => void send({ requestId: previewRequestId.current, input: structuredClone(preview.input) })}>确认整批发送</Button></Space>
      </>}
    </Modal>
    {result && <ReceiptCard value={result.receipt} reminder={result.reminder} onExpired={onExpired} onDenied={onDenied} />}
    <Card title="我的发送记录" extra={<Button onClick={history.refresh}>刷新发送记录</Button>}>
      <Table rowKey="requestId" loading={history.busy} dataSource={history.data?.items ?? []} scroll={{ x: 850 }}
        pagination={{ current: historyPage, pageSize: 20, total: history.data?.total ?? 0, onChange: setHistoryPage, showSizeChanger: false }}
        columns={[{ title: '提交时间', dataIndex: 'committedAt', render: value => new Date(value).toLocaleString('zh-CN') }, { title: '操作编号', dataIndex: 'requestId' },
          { title: '收件角色数', render: (_, value) => value.recipients.length }, { title: '结果', render: () => <Tag color="green">已提交</Tag> },
          { title: '详情', render: (_, value) => <Button onClick={() => setResult({ receipt: value, replayed: true, reminder: 'unconfirmed' })}>查看发送结果</Button> }]} />
    </Card>
  </Space>;
}

function RecipientSelection({ input, update, locked, onExpired, onDenied }: { input: MailInput; update: (input: MailInput) => void; locked: boolean; onExpired: () => void; onDenied: () => void }) {
  const noti = useNotiBar();
  const [q, setQ] = useState(''); const [id, setId] = useState(''); const [filter, setFilter] = useState(''); const [page, setPage] = useState(1);
  const recipients = useGmRead<PlayerPage<MailRecipient>>(`mail/recipients?page=${page}&pageSize=20${filter}`, onExpired, onDenied);
  function toggle(characterId: number) { update({ ...input, recipientIds: input.recipientIds.includes(characterId) ? input.recipientIds.filter(x => x !== characterId) : [...input.recipientIds, characterId] }); }
  return <Card title="明确选择收件角色（最多 100 个）">
    <Space wrap><Input aria-label="收件角色姓名搜索" value={q} disabled={locked} maxLength={128} placeholder="字面姓名搜索" onChange={e => setQ(e.target.value)} />
      <Input aria-label="收件角色 ID" value={id} disabled={locked} placeholder="角色 ID" onChange={e => setId(e.target.value)} />
      <Button disabled={locked} onClick={() => { if (id && (!/^\d+$/.test(id) || +id <= 0 || +id > 2147483647)) { noti.error('角色 ID 必须为正整数。'); return; } setPage(1); setFilter(`${q.trim() ? `&q=${encodeURIComponent(q.trim())}` : ''}${id ? `&characterId=${id}` : ''}`); }}>查找收件人</Button>
      <Button disabled={locked} onClick={() => { setQ(''); setId(''); setFilter(''); setPage(1); }}>清空查询</Button></Space>
    <Typography.Paragraph style={{ marginTop: 12 }}>已选择 {input.recipientIds.length} 个角色 ID：{input.recipientIds.length ? input.recipientIds.join('、') : '尚未选择'}</Typography.Paragraph>
    <Space wrap>{input.recipientIds.map(value => <Button key={value} size="small" disabled={locked} onClick={() => toggle(value)}>取消角色 {value}</Button>)}</Space>
    <Table rowKey="characterId" loading={recipients.busy} dataSource={recipients.data?.items ?? []} locale={{ emptyText: '没有符合条件的有效角色' }}
      pagination={{ current: page, pageSize: 20, total: recipients.data?.total ?? 0, onChange: setPage, showSizeChanger: false }}
      columns={[{ title: '选择', render: (_, value) => <Checkbox aria-label={`选择角色 ${value.characterId}`} checked={input.recipientIds.includes(value.characterId)} disabled={locked || !input.recipientIds.includes(value.characterId) && input.recipientIds.length >= 100} onChange={() => toggle(value.characterId)} /> },
        { title: '角色 ID', dataIndex: 'characterId' }, { title: '姓名', dataIndex: 'name' }, { title: '姓名诊断', dataIndex: 'nameDecodeStatus', render: value => value === 'valid' ? '正常' : value === 'missing' ? '字节缺失' : '编码异常' }, { title: '等级', dataIndex: 'level' }]} />
  </Card>;
}
function ReceiptCard({ value, reminder, onExpired, onDenied }: { value: MailReceipt; reminder: string; onExpired: () => void; onDenied: () => void }) {
  return <Card title="持久化发送结果"><Typography.Paragraph>邮件已提交，玩家可在邮箱领取附件。{reminder === 'unconfirmed' ? '在线提醒未确认；不会撤销或重复发放。' : '在线提醒已尝试，不代表客户端已读或附件已领取。'}</Typography.Paragraph>
    <Descriptions column={1} style={{ marginTop: 16 }} items={[{ key: 'id', label: '操作编号', children: <Typography.Text copyable>{value.requestId}</Typography.Text> },
      { key: 'time', label: '提交时间', children: new Date(value.committedAt).toLocaleString('zh-CN') }, { key: 'expiry', label: '到期时间', children: new Date(value.expiresAt).toLocaleString('zh-CN') },
      { key: 'body', label: '发送正文', children: <span style={{ whiteSpace: 'pre-wrap' }}>{value.input.body}</span> }]} />
    {value.items ? <PreparedAttachments items={value.items} /> : <MailAttachmentSummary items={value.input.items} onExpired={onExpired} onDenied={onDenied} />}
    <Table rowKey={x => x.character.characterId} pagination={false} dataSource={value.recipients} columns={[{ title: '角色 ID', render: (_, x) => x.character.characterId },
      { title: '姓名', render: (_, x) => x.character.name }, { title: '邮件 ID', dataIndex: 'mailId' }, { title: '装备 UID', render: (_, x) => x.equipmentUids.join('、') || '无装备附件' }]} />
  </Card>;
}

function PreparedAttachments({ items }: { items: MailPreview['items'] }) {
  return <>{items.map((x, index) => <Typography.Paragraph key={index}>{x.name ?? `物品 ${x.itemId}`}（ID {x.itemId}） × {x.count} / 人 · {x.kind === 'equipment' ? '普通装备' : '堆叠物'}
    {x.kind === 'equipment' ? ` · 耐久 ${x.durability} · ${x.sealed ? '封装' : '未封装'}${x.amplifyType ? ` · 增幅 +${x.upgradeLevel ?? 0} · ${amplifyTypes[x.amplifyType]}（基础值 ${x.amplifyValue}）` : x.upgradeLevel ? ` · 强化 +${x.upgradeLevel}` : ' · 无强化 / 增幅'}` : ''}</Typography.Paragraph>)}</>;
}
