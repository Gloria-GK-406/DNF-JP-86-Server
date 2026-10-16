import { useEffect, useRef, useState } from 'react';
import { Button, Drawer, Input, InputNumber, Popconfirm, Segmented, Table } from 'antd';
import { InfoCircleOutlined, PlusOutlined, ReloadOutlined } from '@ant-design/icons';
import { api, ApiError } from './api';
import { bjTime, nf, numberFormat } from './format';
import { ItemPicker } from './ItemPicker';
import { ItemIcon, ItemName } from './itemInfo';
import { useNotiBar } from './NotiBar';
import { useConfirmLeave } from './useGmAction';
import { useGmRead } from './useGmRead';
import { Banner, Field, LockBanner, PageHead, Pill, type PillKind } from './ui';

export type RefillEntry = { itemId: number; name: string; source: 'pvf' | 'custom'; quantity: number; mode: number;
  expiresAt: string | null; status: string; pvfEnabled: boolean };
export type RefillList = { revision: number; entries: RefillEntry[]; updatedAt: string | null; updatedBy: string | null };
type Access = { onExpired: () => void; onDenied: () => void };
type Change = { action: string; itemId: number; quantity?: number; mode?: number; expiresAt?: string | null; enabled?: boolean };
const statuses: Record<string, [string, PillKind]> = { active: ['生效', 'ok'], overridden: ['已被自定义覆盖', 'idle'], disabled: ['已禁用', 'attn'], expired: ['已过期', 'idle'],
  inactive: ['不发放', 'idle'], unsupported: ['物品不支持', 'attn'] };
type Filter = 'all' | 'active' | 'pvf' | 'custom' | 'off';
/** datetime-local value in Beijing time ↔ ISO instant. */
function localTime(value?: string | null) { return value ? new Date(new Date(value).getTime() + 8 * 3600000).toISOString().slice(0, 16) : ''; }

export function DailyRefillView(access: Access) {
  const current = useGmRead<RefillList>('daily-refill', access.onExpired, access.onDenied); const noti = useNotiBar();
  const [editor, setEditor] = useState<{ revision: number; entry?: RefillEntry }>();
  const [busy, setBusy] = useState(false); const [uncertain, setUncertain] = useState(false);
  const [filter, setFilter] = useState<Filter>('all');
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
      else {
        if (!(failure instanceof ApiError) || failure.status >= 500 || failure.status === 409) { setUncertain(true); setEditor(undefined); }
        noti.failure(failure);
      }
    } finally { saving.current = false; if (alive.current) setBusy(false); }
  }
  function reload() { setEditor(undefined); setUncertain(false); current.refresh(); }
  const locked = busy || uncertain || current.busy || !current.data;
  const entries = current.data?.entries ?? [];
  const counts: Record<Filter, number> = { all: entries.length, active: entries.filter(e => e.status === 'active').length, pvf: entries.filter(e => e.source === 'pvf').length,
    custom: entries.filter(e => e.source === 'custom').length, off: entries.filter(e => e.status !== 'active').length };
  const shown = entries.filter(e => filter === 'all' || (filter === 'active' ? e.status === 'active' : filter === 'off' ? e.status !== 'active' : e.source === filter))
    .sort((a, b) => (a.status === 'active' ? 0 : 1) - (b.status === 'active' ? 0 : 1));
  const days = (value: string) => Math.ceil((new Date(value).getTime() - Date.now()) / 86400000);
  const data = current.data;
  return <div className="page">
    <PageHead title="每日补给" info="角色每天首次入场时尽量补给：先叠堆再用空位，放不下的部分跳过并记为当天已处理，整理背包或重登不会补发。">
      {data && <span className="faint small">版本 {data.revision} · {data.updatedBy ?? '系统默认'}{data.updatedAt && <> · <span className="mono">{bjTime(data.updatedAt)}</span></>}</span>}
      <Button size="small" icon={<ReloadOutlined />} loading={current.busy} disabled={busy} onClick={reload}>{uncertain ? '重新读取生效规则' : '刷新规则'}</Button>
      <Button size="small" type="primary" icon={<PlusOutlined />} disabled={locked} onClick={() => setEditor({ revision: data!.revision })}>添加补给</Button>
    </PageHead>
    {uncertain && <LockBanner onReload={reload} busy={current.busy} what="规则" label="重新读取生效规则" />}
    <section className="panel">
      <div className="toolbar"><Segmented size="small" value={filter} onChange={value => setFilter(value as Filter)}
        options={([['all', '全部'], ['active', '生效'], ['pvf', 'PVF 原生'], ['custom', '自定义'], ['off', '未生效']] as [Filter, string][]).map(([value, label]) => ({ value, label: `${label} ${counts[value]}` }))} />
        <span className="faint small" style={{ marginInlineStart: 'auto' }}>PVF 规则自动识别；同一物品自定义规则优先，每天只处理一次</span></div>
      <Table<RefillEntry> className="ledger" size="small" rowKey={e => `${e.source}:${e.itemId}`} dataSource={shown} loading={current.busy} pagination={false} scroll={{ x: 960 }}
        rowClassName={e => e.status !== 'active' ? 'row-dim' : ''} locale={{ emptyText: current.error ? '读取失败' : '这一类没有规则' }} columns={[
          { title: '物品', key: 'i', render: (_, e) => <span style={{ display: 'flex', gap: 8, alignItems: 'center' }}><ItemIcon itemId={e.itemId} brief={{ name: e.name }} size="sm" /><ItemName itemId={e.itemId} brief={{ name: e.name }} /></span> },
          { title: '来源', dataIndex: 'source', width: 80, render: v => <span className={`chip ${v === 'custom' ? 'accent' : ''}`}>{v === 'pvf' ? 'PVF' : '自定义'}</span> },
          { title: '方式', dataIndex: 'mode', render: v => v === 0 ? '存量补齐' : '增量发放' },
          { title: '数量', dataIndex: 'quantity', align: 'end', render: (v, e) => <span className="mono">{e.mode === 0 ? `补到 ${nf(v)}` : `+${nf(v)}`}</span> },
          { title: '有效期', dataIndex: 'expiresAt', render: v => !v ? <span className="faint">长期</span> : days(v) > 0
            ? <span><span className="mono small">{bjTime(v)}</span> <span className="faint small">还剩 {days(v)} 天</span></span> : <span className="mono small faint">{bjTime(v)}</span> },
          { title: '状态', dataIndex: 'status', render: v => <Pill kind={statuses[v]?.[1] ?? 'idle'}>{statuses[v]?.[0] ?? v}</Pill> },
          { title: '', key: 'op', align: 'end', render: (_, e) => <span style={{ display: 'inline-flex', gap: 2 }}>
            {e.source === 'custom' ? <>
              <Button size="small" type="text" disabled={locked} onClick={() => setEditor({ revision: data!.revision, entry: e })}>编辑</Button>
              <Popconfirm title={`删除 ${e.name} 的自定义补给？`} description="删除后，如存在已启用且有效的同物品 PVF 规则，将恢复执行该规则。当天已处理记录保留。" okText="删除规则" cancelText="取消"
                onConfirm={() => change({ action: 'delete', itemId: e.itemId }, data!.revision)} disabled={locked}><Button size="small" type="text" danger disabled={locked}>删除</Button></Popconfirm>
            </> : <>
              {e.status !== 'overridden' && <Button size="small" type="text" disabled={locked} onClick={() => setEditor({ revision: data!.revision, entry: e })}>自定义覆盖</Button>}
              <Popconfirm title={`${e.pvfEnabled ? '禁用' : '恢复'} ${e.name} 的 PVF 补给？`} description="仅改变服务器补给开关，不修改 PVF 文件，也不重置当天已处理记录。"
                okText={e.pvfEnabled ? '禁用规则' : '恢复规则'} cancelText="取消" onConfirm={() => change({ action: 'pvf', itemId: e.itemId, enabled: !e.pvfEnabled }, data!.revision)} disabled={locked}>
                <Button size="small" type="text" danger={e.pvfEnabled} disabled={locked}>{e.pvfEnabled ? '禁用' : '恢复'}</Button></Popconfirm>
            </>}</span> },
        ]} />
    </section>
    {editor && <RefillEditor key={`${editor.revision}:${editor.entry?.source ?? ''}:${editor.entry?.itemId ?? 'new'}`} entry={editor.entry} entries={entries} busy={busy} {...access}
      onClose={() => setEditor(undefined)} onSave={input => change(input, editor.revision)} />}
  </div>;
}

function RefillEditor({ entry, entries, busy, onSave, onClose, onExpired, onDenied }: Access & { entry?: RefillEntry; entries: RefillEntry[]; busy: boolean;
  onSave: (change: Change) => Promise<void>; onClose: () => void }) {
  const confirmLeave = useConfirmLeave();
  const [item, setItem] = useState(entry ? { itemId: entry.itemId, name: entry.name } : undefined);
  const [mode, setMode] = useState(entry?.mode ?? 0);
  const [quantity, setQuantity] = useState<number | null>(entry?.quantity || 1);
  const [expiry, setExpiry] = useState(entry?.source === 'custom' ? localTime(entry.expiresAt) : '');
  const [picker, setPicker] = useState(false); const [dirty, setDirty] = useState(false);
  const touch = () => setDirty(true);
  const mode_ = entry?.source === 'custom' ? 'edit' : entry ? 'override' : 'add';
  const exists = mode_ === 'add' && !!item && entries.some(x => x.source === 'custom' && x.itemId === item.itemId);
  const expiryTime = expiry ? new Date(`${expiry}:00+08:00`) : null;
  const expiryError = expiryTime && (Number.isNaN(expiryTime.getTime()) || expiryTime.getTime() <= Date.now()) ? '截止时间要晚于现在；留空就是长期有效' : '';
  const ok = !!item && quantity !== null && Number.isInteger(quantity) && quantity >= 1 && !expiryError && !exists;
  const close = () => { if (!busy) confirmLeave(dirty, onClose); };
  const title = mode_ === 'edit' ? '编辑每日补给' : mode_ === 'override' ? '自定义覆盖 PVF 补给' : '添加每日补给';
  return <Drawer open size={560} title={title} onClose={close} mask={{ closable: false }} closable={!busy} keyboard={!busy}
    footer={<div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}><Button disabled={busy} onClick={close}>取消</Button>
      <Button type="primary" loading={busy} disabled={!ok} onClick={() => item && void onSave({ action: mode_ === 'edit' ? 'update' : 'add', itemId: item.itemId, quantity: quantity!, mode,
        expiresAt: expiryTime?.toISOString() ?? null })}>保存并生效</Button></div>}>
    <div className="form">
      {mode_ === 'override' && <Banner icon={<InfoCircleOutlined />} title="覆盖之后以这条为准">PVF 原规则保留，删掉自定义规则就会恢复。</Banner>}
      <Field label="补给物品" err={exists ? '这件已经有自定义规则了，去列表里编辑那一条' : undefined}>
        <div style={{ display: 'flex', gap: 10, alignItems: 'center' }}>{item ? <><ItemIcon itemId={item.itemId} brief={{ name: item.name }} size="sm" /><ItemName itemId={item.itemId} brief={{ name: item.name }} /></>
          : <span className="faint">尚未选择物品</span>}
          {mode_ === 'add' && <Button size="small" disabled={busy} onClick={() => setPicker(true)}>{item ? '换一个' : '选择补给物品'}</Button>}</div>
      </Field>
      <Field label="补给方式">
        <div className="choice" role="radiogroup" aria-label="补给方式">
          <button role="radio" aria-checked={mode === 0} className={mode === 0 ? 'on' : ''} disabled={busy} onClick={() => { setMode(0); touch(); }}><b>存量补齐</b><span>背包里不够目标数量时补上缺口，多出来的不扣</span></button>
          <button role="radio" aria-checked={mode === 1} className={mode === 1 ? 'on' : ''} disabled={busy} onClick={() => { setMode(1); touch(); }}><b>增量发放</b><span>每天额外发固定数量，不看现有多少</span></button>
        </div>
      </Field>
      <Field label={mode === 0 ? '目标数量' : '发放数量'} htmlFor="refill-quantity"><InputNumber<number> id="refill-quantity" min={1} max={2147483647} precision={0} style={{ width: 200 }} disabled={busy}
        value={quantity} onChange={value => { setQuantity(value); touch(); }} {...numberFormat} /></Field>
      <Field label="截止时间（北京时间，留空为长期有效）" htmlFor="refill-expiry" err={expiryError || undefined}><Input id="refill-expiry" type="datetime-local" value={expiry} disabled={busy}
        onChange={event => { setExpiry(event.target.value); touch(); }} style={{ width: 240 }} /></Field>
      <span className="faint small">当天已处理记录保留；空间不足造成的未发放数量不会补发。自定义规则覆盖同物品的 PVF 规则。</span>
    </div>
    {picker && <ItemPicker purpose="refill" onExpired={onExpired} onDenied={onDenied} onClose={() => setPicker(false)} onConfirm={(adds, details) => {
      const chosen = adds[0]; setItem({ itemId: chosen.itemId, name: details[chosen.itemId]?.item.name ?? `物品 ${chosen.itemId}` }); setDirty(true); return undefined;
    }} />}
  </Drawer>;
}
