import { useEffect, useRef, useState } from 'react';
import { App, Button, Card, Checkbox, Input, InputNumber, Modal, Select, Space, Steps, Table, Typography } from 'antd';
import { api, ApiError, type PlayerPage } from './api';
import { useGmRead } from './useGmRead';
import { useNotiBar } from './NotiBar';
import { MailAttachments } from './MailAttachments';
import { amplifyTypes } from "./items";
import { emptyMail, type MailDocument, type MailInput, type MailPreview, type MailRecipient, type PendingMail } from "./mail";

export type MailAccess = { onExpired: () => void; onDenied: () => void };
export type SendOutcome = 'committed' | 'rejected' | 'unknown';

export function useMailAction({ onExpired, onDenied }: MailAccess) {
  const noti = useNotiBar();
  const [busy, setBusy] = useState(false);
  const epoch = useRef(0); const alive = useRef(true);
  useEffect(() => { alive.current = true; return () => { alive.current = false; epoch.current++; }; }, []);
  async function run<T>(action: () => Promise<T>, accept: (value: T) => void) {
    const current = ++epoch.current; setBusy(true);
    try { const value = await action(); if (alive.current && current === epoch.current) accept(value); }
    catch (failure) {
      if (!alive.current || current !== epoch.current) return;
      if (failure instanceof ApiError && failure.status === 401) onExpired();
      else if (failure instanceof ApiError && failure.status === 403) onDenied();
      else noti.failure(failure);
    } finally { if (alive.current && current === epoch.current) setBusy(false); }
  }
  return { busy, run };
}

function useCloseEditor(dirty: boolean, onClose: () => void) {
  const { modal } = App.useApp();
  return () => {
    if (!dirty) { onClose(); return; }
    modal.confirm({ title: '放弃未保存的修改？', content: '关闭后将丢弃本窗口的未保存修改。',
      okText: '放弃修改', cancelText: '继续编辑', onOk: onClose });
  };
}

function MailContentEditor({ input, update, locked, prefix, ...access }: MailAccess & {
  input: MailInput; update: (input: MailInput) => void; locked: boolean; prefix: string;
}) {
  return <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
    <Typography.Text type="secondary">支持永久普通装备、永久堆叠物与金币；玩家在邮箱领取，邮件有效期 30 天。</Typography.Text>
    <label htmlFor={`${prefix}-title`}>邮件标题</label><Input id={`${prefix}-title`} value={input.title} disabled={locked} onChange={e => update({ ...input, title: e.target.value })} />
    <label htmlFor={`${prefix}-body`}>邮件正文</label><Input.TextArea id={`${prefix}-body`} placeholder="此为官方邮件，无需回复。" value={input.body} disabled={locked} rows={5} onChange={e => update({ ...input, body: e.target.value })} />
    <Typography.Text type="secondary">正文选填；留空时将使用输入框中的默认文案。</Typography.Text>
    <Typography.Text type="secondary">实际展示含标题和换行，最多 511 个 UTF-8 字节；以发送预览核对结果为准。</Typography.Text>
    <label htmlFor={`${prefix}-gold`}>每人金币</label><InputNumber id={`${prefix}-gold`} min={0} max={2147483647} precision={0} value={input.gold} disabled={locked} onChange={gold => update({ ...input, gold: gold ?? 0 })} />
    <MailAttachments {...access} items={input.items} locked={locked} onChange={items => update({ ...input, items })} />
  </Space>;
}

export function MailComposer({ initial, draftRevision, initialDirty, sending, onSend, onSavedDraft, onClose, ...access }: MailAccess & {
  initial: MailInput; draftRevision?: number; initialDirty: boolean; sending: boolean;
  onSend: (snapshot: PendingMail) => Promise<SendOutcome>;
  onSavedDraft: () => void; onClose: () => void;
}) {
  const noti = useNotiBar(); const { busy, run } = useMailAction(access);
  const [input, setInput] = useState(() => structuredClone(initial));
  const [revision, setRevision] = useState(draftRevision); const [dirty, setDirty] = useState(initialDirty);
  const [step, setStep] = useState(0); const [preview, setPreview] = useState<MailPreview>();
  const requestId = useRef(''); const alive = useRef(true);
  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  useEffect(() => { if (revision === undefined && draftRevision !== undefined) setRevision(draftRevision); }, [draftRevision, revision]);
  const templates = useGmRead<MailDocument[]>('mail/templates', access.onExpired, access.onDenied);
  const close = useCloseEditor(dirty, onClose); const locked = busy || sending;
  function update(value: MailInput) { setInput(value); setDirty(true); setPreview(undefined); }
  function saveDraft() {
    if (revision === undefined) { noti.error('请先重新加载草稿，取得当前版本后再保存。'); return; }
    void run(() => api<MailDocument>('mail/draft', { ...structuredClone(input), expectedRevision: revision }, 'PUT'), value => {
      setRevision(value.revision); setDirty(false); onSavedDraft(); noti.success('草稿已保存。');
    });
  }
  function loadDraft() {
    void run(() => api<MailDocument>('mail/draft'), value => {
      setInput(structuredClone(value.input)); setRevision(value.revision); setDirty(false); setPreview(undefined); setStep(0);
      noti.success('已加载保存的草稿。');
    });
  }
  async function confirm() {
    if (!preview || locked) return;
    const outcome = await onSend({ requestId: requestId.current, input: structuredClone(preview.input) });
    if (alive.current && outcome === 'rejected') { setPreview(undefined); setStep(0); }
  }
  return <Modal title="发送官方邮件" open onCancel={() => { if (!locked) close(); }} width={1040} className="mail-workflow-modal"
    closable={!locked} keyboard={!locked} mask={{ closable: false }} footer={<Space wrap>
      <Button disabled={locked} onClick={close}>取消编辑</Button>
      {step < 2 && <Button disabled={locked || revision === undefined} onClick={saveDraft}>保存草稿</Button>}
      {step > 0 && <Button disabled={locked} onClick={() => { setPreview(undefined); setStep(step - 1); }}>返回上一步</Button>}
      {step === 0 ? <Button type="primary" disabled={locked} onClick={() => {
        setStep(1);
      }}>下一步：选择收件人</Button> : step === 1 ? <Button type="primary" loading={busy} disabled={sending} onClick={() => {
        void run(() => api<MailPreview>('mail/preview', structuredClone(input)), value => { requestId.current = crypto.randomUUID(); setPreview(value); setStep(2); });
      }}>预览并核对</Button> : <Button type="primary" loading={sending} disabled={busy} onClick={() => void confirm()}>确认整批发送</Button>}
    </Space>}>
    <Steps current={step} items={[{ title: '编辑内容' }, { title: '选择收件人' }, { title: '核对发送' }]} />
    <div className="mail-workflow-body">
      {step === 0 && <>
        <Space wrap style={{ marginBottom: 16 }}><Select aria-label="选择内容模板" placeholder="可选：从模板填入" style={{ width: 240 }} disabled={locked}
          value={undefined} options={templates.data?.map(value => ({ value: value.id, label: value.name }))} onChange={id => {
            const value = templates.data?.find(item => item.id === id);
            if (value) { update({ ...structuredClone(value.input), recipientIds: [...input.recipientIds] }); noti.success('已加载模板内容，保留当前收件人。'); }
          }} /><Button disabled={locked} onClick={loadDraft}>重新加载草稿（覆盖当前编辑）</Button></Space>
        <MailContentEditor {...access} prefix="mail" input={input} update={update} locked={locked} />
      </>}
      {step === 1 && <RecipientSelection {...access} input={input} update={update} locked={locked} />}
      {step === 2 && preview && <>
        <Typography.Paragraph type="secondary">系统官方来源 · 30 天有效 · 整批提交，任何目标或附件失败均回滚</Typography.Paragraph>
        <Typography.Paragraph style={{ whiteSpace: 'pre-wrap' }}>{preview.displayText}</Typography.Paragraph>
        <Typography.Paragraph>实际文字 {preview.utf8Bytes} / 511 字节；收件人 {preview.recipients.length}；每人金币 {preview.input.gold}；总金币 {preview.totalGold}；总物品数量 {preview.totalItems}</Typography.Paragraph>
        <Typography.Paragraph>收件角色：{preview.recipients.map(value => `${value.characterId} ${value.name}`).join('、')}</Typography.Paragraph>
        <PreparedAttachments items={preview.items} />
      </>}
    </div>
  </Modal>;
}

export function MailTemplateEditor({ document, onSaved, onClose, ...access }: MailAccess & {
  document?: MailDocument; onSaved: () => void; onClose: () => void;
}) {
  const noti = useNotiBar(); const { busy, run } = useMailAction(access);
  const [input, setInput] = useState<MailInput>(() => ({ ...structuredClone(document?.input ?? emptyMail()), recipientIds: [] }));
  const [name, setName] = useState(document?.name ?? ''); const [dirty, setDirty] = useState(false);
  const close = useCloseEditor(dirty, onClose);
  return <Modal title={document ? '编辑邮件模板' : '创建邮件模板'} open width={1040} className="mail-workflow-modal"
    onCancel={() => { if (!busy) close(); }} closable={!busy} keyboard={!busy} mask={{ closable: false }}
    footer={<Space><Button disabled={busy} onClick={close}>关闭</Button><Button type="primary" loading={busy} onClick={() => {
      if (!name.trim()) { noti.error('请输入模板名称。'); return; }
      void run(() => api<MailDocument>(document ? `mail/templates/${document.id}` : 'mail/templates',
        { ...structuredClone(input), recipientIds: [], name, expectedRevision: document?.revision ?? 0 }, document ? 'PUT' : undefined), () => {
        noti.success('内容模板已保存，不含收件人。'); onSaved();
      });
    }}>保存模板</Button></Space>}>
    <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
      <label htmlFor="template-name">模板名称</label><Input id="template-name" value={name} maxLength={64} disabled={busy} onChange={e => { setName(e.target.value); setDirty(true); }} />
      <MailContentEditor {...access} prefix="template" input={input} locked={busy} update={value => { setInput(value); setDirty(true); }} />
    </Space>
  </Modal>;
}

function RecipientSelection({ input, update, locked, onExpired, onDenied }: MailAccess & { input: MailInput; update: (input: MailInput) => void; locked: boolean }) {
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

export function PreparedAttachments({ items }: { items: MailPreview['items'] }) {
  return <>{items.map((x, index) => <Typography.Paragraph key={index}>{x.name ?? `物品 ${x.itemId}`}（ID {x.itemId}） × {x.count} / 人 · {x.kind === 'equipment' ? '普通装备' : '堆叠物'}
    {x.kind === 'equipment' ? ` · 耐久 ${x.durability} · ${x.sealed ? '封装' : '未封装'}${x.amplifyType ? ` · 增幅 +${x.upgradeLevel ?? 0} · ${amplifyTypes[x.amplifyType]}（基础值 ${x.amplifyValue}）` : x.upgradeLevel ? ` · 强化 +${x.upgradeLevel}` : ' · 无强化 / 增幅'}` : ''}</Typography.Paragraph>)}</>;
}
