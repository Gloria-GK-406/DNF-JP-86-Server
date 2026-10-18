import { useState } from 'react';
import { Button, Drawer, Input } from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import { api } from './api';
import { AttachmentRows } from './AttachmentRows';
import { mailDisplayText, utf8Bytes } from './format';
import { ItemPicker } from './ItemPicker';
import { rememberDetails, useItemDetails } from './itemInfo';
import { mergeItems, usedSlots } from './items';
import { emptyMail, type MailDocument, type MailInput } from './mail';
import { MailFields } from './MailComposer';
import { useNotiBar } from './NotiBar';
import { useConfirmLeave, useGmAction } from './useGmAction';
import { Field, Panel } from './ui';

/** Content templates hold title / body / gold / attachments only — never recipients. */
export function TemplateEditor({ document, templates, onSaved, onClose, onExpired, onDenied }: { document?: MailDocument; templates: MailDocument[];
  onSaved: () => void; onClose: () => void; onExpired: () => void; onDenied: () => void }) {
  const access = { onExpired, onDenied };
  const noti = useNotiBar(); const confirmLeave = useConfirmLeave(); const { busy, run } = useGmAction(access);
  const [name, setName] = useState(document?.name ?? '');
  const [input, setInput] = useState<MailInput>(() => ({ ...structuredClone(document?.input ?? emptyMail()), recipientIds: [] }));
  const [dirty, setDirty] = useState(false); const [picker, setPicker] = useState(false);
  const items = useItemDetails('mail', input.items.map(item => item.itemId), access);
  const update = (patch: Partial<MailInput>) => { setInput(old => ({ ...old, ...patch })); setDirty(true); };
  const used = usedSlots(input.items, items.details);
  const taken = templates.some(t => t.name === name.trim() && t.id !== document?.id);
  const tooMany = !document && templates.length >= 20;
  const bytes = utf8Bytes(mailDisplayText(input.title, input.body));
  const ok = name.trim().length > 0 && !taken && !tooMany && items.ready && used <= 10 && bytes <= 511;
  const close = () => { if (!busy) confirmLeave(dirty, onClose); };
  function save() {
    if (!ok) return;
    void run(() => api<MailDocument>(document ? `mail/templates/${document.id}` : 'mail/templates',
      { ...structuredClone(input), recipientIds: [], name: name.trim(), expectedRevision: document?.revision ?? 0 }, document ? 'PUT' : undefined), () => {
      noti.success('内容模板已保存，不含收件人。'); onSaved();
    });
  }
  return <Drawer open size={720} onClose={close} mask={{ closable: false }} closable={!busy} keyboard={!busy} title={document ? `编辑模板 · ${document.name}` : '新建模板'}
    footer={<div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}><Button disabled={busy} onClick={close}>取消</Button>
      <Button type="primary" loading={busy} disabled={!ok} onClick={save}>保存模板</Button></div>}>
    <div className="grid">
      <Field label="模板名称" htmlFor="template-name" err={taken ? '已经有同名模板了' : tooMany ? '已达到 20 个模板上限' : undefined} hint="最多 20 个模板，只存内容，不存收件人。">
        <Input id="template-name" maxLength={64} value={name} disabled={busy} onChange={event => { setName(event.target.value); setDirty(true); }} /></Field>
      <MailFields input={input} update={update} prefix="template" locked={busy} />
      {bytes > 511 && <span className="err small">标题加正文超过 511 字节了（{bytes}）</span>}
      <Panel title="附件" count={`${items.ready ? used : '…'}/10 格`} flush tools={<Button size="small" icon={<PlusOutlined />} disabled={busy} onClick={() => setPicker(true)}>添加物品</Button>}>
        <AttachmentRows items={input.items} details={items.details} failed={items.failed} onRetry={items.retry} locked={busy} onChange={rows => update({ items: rows })} empty="模板里还没放附件" />
      </Panel>
    </div>
    {picker && <ItemPicker purpose="template" existing={input.items} known={items.details} {...access} onClose={() => setPicker(false)} onConfirm={(adds, loaded) => {
      try {
        const merged = mergeItems(input.items, adds);
        if (merged.length > 10 || usedSlots(merged, loaded) > 10) return '每人最多 10 个展开物品附件；每件装备单独占一个附件位。';
        Object.values(loaded).forEach(value => rememberDetails('mail', value));
        update({ items: merged }); return undefined;
      } catch (failure) { return (failure as Error).message; }
    }} />}
  </Drawer>;
}
