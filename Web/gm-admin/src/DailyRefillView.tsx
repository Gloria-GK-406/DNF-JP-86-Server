import { useEffect, useRef, useState } from 'react';
import { App, Button, Card, Form, Input, InputNumber, Modal, Select, Space, Table, Tag, Typography } from 'antd';
import { api, ApiError } from './api';
import { useGmRead } from './useGmRead';
import { useNotiBar } from './NotiBar';
import { ItemPicker } from './ItemPicker';
export type RefillEntry = { itemId: number; name: string; source: 'pvf' | 'custom'; quantity: number; mode: number;
  expiresAt: string | null; status: string; pvfEnabled: boolean };
export type RefillList = { revision: number; entries: RefillEntry[]; updatedAt: string | null; updatedBy: string | null };
type Access = { onExpired: () => void; onDenied: () => void };
type Change = { action: string; itemId: number; quantity?: number; mode?: number; expiresAt?: string | null; enabled?: boolean };
const statuses: Record<string, string> = { active: '生效', overridden: '已被自定义覆盖', disabled: '已禁用', expired: '已过期', inactive: '不发放', unsupported: '物品不支持' };
export function DailyRefillView(access: Access) {
  const current = useGmRead<RefillList>('daily-refill', access.onExpired, access.onDenied); const noti = useNotiBar(); const { modal } = App.useApp();
  const [editor, setEditor] = useState<{ revision: number; entry?: RefillEntry }>();
  const [busy, setBusy] = useState(false); const [uncertain, setUncertain] = useState(false);
  const saving = useRef(false); const alive = useRef(true);
  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  async function change(input: Change, revision: number) {
    if (saving.current || uncertain) return; saving.current = true; setBusy(true);
    try {
      await api('daily-refill', { expectedRevision: revision, ...input });
      if (alive.current) { setEditor(undefined); current.refresh(); noti.success('每日补给配置已生效；当天已完成补给的角色不会再次补给。'); }
    } catch (failure) {
      if (!alive.current) return;
      if (failure instanceof ApiError && failure.status === 401) access.onExpired();
      else if (failure instanceof ApiError && failure.status === 403) access.onDenied();
      else { if (!(failure instanceof ApiError) || failure.status >= 500 || failure.status === 409) setUncertain(true); noti.failure(failure); }
    } finally { saving.current = false; if (alive.current) setBusy(false); }
  }
  function confirm(row: RefillEntry) {
    const revision = current.data!.revision;
    const deleting = row.source === 'custom';
    modal.confirm({ title: deleting ? `删除 ${row.name} 的自定义补给？` : `${row.pvfEnabled ? '禁用' : '恢复'} ${row.name} 的 PVF 补给？`,
      content: deleting ? '删除后，如存在已启用且有效的同物品 PVF 规则，将恢复执行该规则。当天已处理记录保留。' : '仅改变服务器补给开关，不修改 PVF 文件，也不重置当天已处理记录。',
      okText: deleting ? '删除规则' : row.pvfEnabled ? '禁用规则' : '恢复规则', cancelText: '取消',
      onOk: () => change({ action: deleting ? 'delete' : 'pvf', itemId: row.itemId, ...(deleting ? {} : { enabled: !row.pvfEnabled }) }, revision) });
  }
  function reload() { setEditor(undefined); setUncertain(false); current.refresh(); }
  const locked = busy || uncertain || current.busy || !current.data;
  return <Space orientation="vertical" size="large" style={{ width: '100%' }}>
    <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>角色每日首次入场时尽可能补给。优先叠堆，再使用空位；放不下的部分直接跳过并计为当天已处理，整理背包或重登不会补发。</Typography.Paragraph>
    <Card title="每日补给规则" extra={<Space><Button disabled={busy || current.busy} onClick={reload}>{uncertain ? '重新读取生效规则' : '刷新规则'}</Button>
      <Button type="primary" disabled={locked} onClick={() => setEditor({ revision: current.data!.revision })}>添加补给</Button></Space>}>
      <Typography.Paragraph type="secondary">PVF 规则自动识别；同一物品自定义规则优先，每日只处理一次。来源和状态可通过表头筛选。
        {current.data && ` 配置版本 ${current.data.revision}${current.data.updatedBy ? ` · ${current.data.updatedBy}` : ' · 系统默认'}`}</Typography.Paragraph>
      <Table<RefillEntry> rowKey={row => `${row.source}:${row.itemId}`} dataSource={current.data?.entries ?? []} loading={current.busy} scroll={{ x: 1040 }}
        pagination={{ pageSize: 20, hideOnSinglePage: true }} columns={[
          { title: '物品', width: 235, render: (_, row) => <><Typography.Text strong>{row.name}</Typography.Text><div className="mail-item-id">ID {row.itemId}</div></> },
          { title: '来源', width: 95, dataIndex: 'source', filters: [{ text: 'PVF', value: 'pvf' }, { text: '自定义', value: 'custom' }],
            onFilter: (v, row) => row.source === v, render: source => <Tag color={source === 'pvf' ? 'blue' : 'purple'}>{source === 'pvf' ? 'PVF' : '自定义'}</Tag> },
          { title: '补给方式', width: 115, render: (_, row) => row.mode === 0 ? '存量补齐' : '增量发放' },
          { title: '数量', width: 95, dataIndex: 'quantity' },
          { title: '有效期', width: 165, render: (_, row) => row.expiresAt ? new Date(row.expiresAt).toLocaleString('zh-CN') : '长期有效' },
          { title: '状态', width: 150, dataIndex: 'status', filters: Object.entries(statuses).map(([value, text]) => ({ value, text })),
            onFilter: (v, row) => row.status === v, render: status => <Tag color={status === 'active' ? 'green' : undefined}>{statuses[status] ?? status}</Tag> },
          { title: '操作', width: 210, render: (_, row) => <Space>
            <Button size="small" disabled={locked} onClick={() => setEditor({ revision: current.data!.revision, entry: row })}>
              {row.source === 'custom' ? '编辑' : '自定义覆盖'}</Button>
            <Button size="small" danger={row.source === 'custom' || row.pvfEnabled} disabled={locked} onClick={() => confirm(row)}>
              {row.source === 'custom' ? '删除' : row.pvfEnabled ? '禁用' : '恢复'}</Button>
          </Space> },
        ]} />
    </Card>
    {editor && <RefillEditor key={`${editor.revision}:${editor.entry?.itemId ?? 'new'}`} entry={editor.entry} busy={busy} uncertain={uncertain}
      {...access} onClose={() => setEditor(undefined)} onReload={reload} onSave={input => change(input, editor.revision)} />}
  </Space>;
}
function localTime(value?: string | null) { if (!value) return ''; const date = new Date(value); return new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 16); }
function RefillEditor({ entry, busy, uncertain, onSave, onClose, onReload, ...access }: Access & { entry?: RefillEntry; busy: boolean; uncertain: boolean;
  onSave: (change: Change) => Promise<void>; onClose: () => void; onReload: () => void }) {
  const [form] = Form.useForm(); const noti = useNotiBar(); const { modal } = App.useApp(); const [dirty, setDirty] = useState(false); const [picker, setPicker] = useState(false);
  const mode = Form.useWatch('mode', form) ?? entry?.mode ?? 0;
  const [item, setItem] = useState(entry ? { itemId: entry.itemId, name: entry.name } : undefined);
  function close() { if (busy) return; if (!dirty) onClose(); else modal.confirm({ title: '放弃尚未保存的修改？', okText: '放弃修改', cancelText: '继续编辑', onOk: onClose }); }
  return <Modal title={entry?.source === 'custom' ? '编辑每日补给' : entry ? '自定义覆盖 PVF 补给' : '添加每日补给'} open width={680} onCancel={close}
    closable={!busy} keyboard={!busy} mask={{ closable: false }} destroyOnHidden footer={<Space><Button disabled={busy} onClick={close}>取消</Button>
      {uncertain ? <Button type="primary" onClick={onReload}>重新读取生效规则</Button> : <Button type="primary" loading={busy} onClick={() => form.submit()}>保存并生效</Button>}</Space>}>
    <Typography.Paragraph type="secondary">当天已处理记录保留；空间不足造成的未发放数量不会补发。自定义规则覆盖同物品的 PVF 规则。</Typography.Paragraph>
    <Form form={form} layout="vertical" initialValues={{ quantity: entry?.quantity || 1, mode: entry?.mode ?? 0, expiry: localTime(entry?.expiresAt) }}
      disabled={busy || uncertain} onValuesChange={() => setDirty(true)} onFinishFailed={() => noti.error('请核对补给字段。')}
      onFinish={async values => {
        if (!item) { noti.error('请先选择补给物品。'); return; }
        const expiry = values.expiry ? new Date(values.expiry) : null;
        if (expiry && (Number.isNaN(expiry.getTime()) || expiry.getTime() <= Date.now())) { noti.error('截止时间须晚于当前时间，或清空以长期生效。'); return; }
        await onSave({ action: entry?.source === 'custom' ? 'update' : 'add', itemId: item.itemId, quantity: values.quantity, mode: values.mode, expiresAt: expiry?.toISOString() ?? null });
      }}>
      <Form.Item label="补给物品"><Space><Typography.Text>{item ? `${item.name}（ID ${item.itemId}）` : '尚未选择物品'}</Typography.Text>
        {!entry && <Button disabled={busy || uncertain} onClick={() => setPicker(true)}>选择补给物品</Button>}</Space></Form.Item>
      <Form.Item name="mode" label="补给方式" rules={[{ required: true }]}><Select options={[{ value: 0, label: '存量补齐：只补当前缺口，不扣除超出部分' }, { value: 1, label: '增量发放：额外发放指定数量' }]} /></Form.Item>
      <Form.Item name="quantity" label={mode === 0 ? '目标数量' : '发放数量'} rules={[{ required: true, type: 'integer', min: 1, max: 2147483647, message: '请输入正整数数量' }]}><InputNumber min={1} max={2147483647} precision={0} style={{ width: '100%' }} /></Form.Item>
      <Form.Item name="expiry" label="截止时间（留空为长期有效）"><Input type="datetime-local" /></Form.Item>
    </Form>
    {picker && <ItemPicker purpose="daily-refill" {...access} onClose={() => setPicker(false)} onChoose={(selected, details) => {
      setItem({ itemId: selected.itemId, name: details.item.name }); form.setFieldValue('quantity', selected.count); setPicker(false); setDirty(true); return undefined;
    }} />}
  </Modal>;
}
