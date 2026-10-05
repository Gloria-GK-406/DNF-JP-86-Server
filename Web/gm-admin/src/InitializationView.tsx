import { useEffect, useRef, useState } from 'react';
import { Button, Card, Descriptions, Empty, Form, InputNumber, Modal, Select, Space, Tabs, Tag, Typography } from 'antd';
import { api, ApiError } from './api';
import { useGmRead } from './useGmRead';
import { useNotiBar } from './NotiBar';
import { ItemPicker } from './ItemPicker';
import type { ItemCatalogDetails } from './items';
import type { AccountInitialization, CharacterInitialization, InitialItem, InitializationViewData } from './initialization';

type Access = { onExpired: () => void; onDenied: () => void };
type AccountForm = Omit<AccountInitialization, 'contracts'> & { contractDays: Record<string, number> };
const numberRules = [{ required: true, type: 'integer' as const, min: 0, max: 2147483647, message: '请输入 0—2147483647 的整数' }];
const amountLabels = { cera: '初始点券', tokenCera: '初始代币券', happyTokenCera: '初始欢乐代币券' } as const;

export function InitializationView(access: Access) {
  const noti = useNotiBar();
  const current = useGmRead<InitializationViewData>('initialization', access.onExpired, access.onDenied);
  const [tab, setTab] = useState('account');
  const [editing, setEditing] = useState<'account' | 'character' | null>(null);
  const [snapshot, setSnapshot] = useState<InitializationViewData>();
  const [busy, setBusy] = useState(false); const [uncertain, setUncertain] = useState(false);
  const alive = useRef(true); const saving = useRef(false);
  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  function open(kind: 'account' | 'character') {
    if (current.data) { setSnapshot(structuredClone(current.data)); setUncertain(false); setEditing(kind); }
  }
  async function save(kind: 'account' | 'character', input: AccountInitialization | CharacterInitialization) {
    if (!snapshot || saving.current || uncertain) return;
    saving.current = true; setBusy(true);
    try {
      await api(`initialization/${kind}`, { expectedRevision: snapshot[kind].revision, input });
      if (!alive.current) return;
      setEditing(null); current.refresh(); noti.success('初始化配置已保存并生效，仅影响后续创建。');
    } catch (failure) {
      if (!alive.current) return;
      if (failure instanceof ApiError && failure.status === 401) access.onExpired();
      else if (failure instanceof ApiError && failure.status === 403) access.onDenied();
      else {
        if (!(failure instanceof ApiError) || failure.status >= 500 || failure.status === 409) setUncertain(true);
        noti.failure(failure);
      }
    } finally { saving.current = false; if (alive.current) setBusy(false); }
  }
  function reload() { setEditing(null); setSnapshot(undefined); setUncertain(false); current.refresh(); }
  const data = current.data;
  function metadata(kind: 'account' | 'character') {
    const doc = data![kind];
    return <Space wrap style={{ marginBottom: 20 }}><Tag color="green">当前生效</Tag><Typography.Text type="secondary">
      {doc.revision === 0 ? '系统默认配置' : `版本 ${doc.revision} · ${doc.updatedBy} · ${doc.updatedAt ? new Date(doc.updatedAt).toLocaleString('zh-CN') : ''}`}
    </Typography.Text></Space>;
  }
  return <Space orientation="vertical" size="large" style={{ width: '100%' }}>
    <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>账号配置在注册时执行一次；角色配置在每次创角时执行。仅影响后续创建，已有账号与角色不变。</Typography.Paragraph>
    <Tabs activeKey={tab} onChange={setTab} items={[
      { key: 'account', label: '账号初始化', children: <Card title="当前账号初始化配置" loading={current.busy}
        extra={<Space><Button onClick={current.refresh} disabled={current.busy}>刷新配置</Button><Button type="primary" onClick={() => open('account')} disabled={!data}>编辑账号配置</Button></Space>}>
        {data ? <>{metadata('account')}<Descriptions column={2} items={[
          ...Object.entries(amountLabels).map(([key, label]) => ({ key, label, children: data.account.input[key as keyof typeof amountLabels].toLocaleString('zh-CN') })),
          { key: 'cargo', label: '初始账号仓库', children: data.options.accountCargo.find(c => c.value === data.account.input.accountCargoLevel)?.label ?? `${data.account.input.accountCargoLevel} 格` },
          ...data.options.contracts.map(c => { const days = data.account.input.contracts.find(v => v.type === c.value)?.days ?? 0; return { key: String(c.value), label: c.label, children: days ? `${days} 天` : '不赋予' }; }),
        ]} /></> : <Empty description="暂未读取到配置" />}
      </Card> },
      { key: 'character', label: '角色初始化', children: <Card title="当前角色初始化配置" loading={current.busy}
        extra={<Space><Button onClick={current.refresh} disabled={current.busy}>刷新配置</Button><Button type="primary" onClick={() => open('character')} disabled={!data}>编辑角色配置</Button></Space>}>
        {data ? <>{metadata('character')}<Descriptions column={2} items={[
          { key: 'level', label: '初始角色等级', children: `Lv. ${data.character.input.level}` },
          { key: 'cargo', label: '初始个人仓库', children: data.options.personalCargo.find(c => c.value === data.character.input.personalCargoLevel)?.label ?? `${data.character.input.personalCargoLevel} 格` },
          { key: 'base', label: '职业基础赠送', span: 2, children: '保留职业初始装备、技能和 10 个复活币' },
        ]} /><Typography.Title level={5}>额外初始物品</Typography.Title><InitialItems items={data.character.input.items} {...access} /></> : <Empty description="暂未读取到配置" />}
      </Card> },
    ]} />
    {editing && snapshot && <InitializationEditor key={editing} kind={editing} data={snapshot} busy={busy} uncertain={uncertain}
      onClose={() => setEditing(null)} onReload={reload} onSave={input => save(editing, input)} {...access} />}
  </Space>;
}

function InitializationEditor({ kind, data, busy, uncertain, onSave, onClose, onReload, ...access }: Access & {
  kind: 'account' | 'character'; data: InitializationViewData; busy: boolean; uncertain: boolean;
  onSave: (input: AccountInitialization | CharacterInitialization) => Promise<void>; onClose: () => void; onReload: () => void;
}) {
  const noti = useNotiBar(); const [form] = Form.useForm(); const [dirty, setDirty] = useState(false);
  const [items, setItems] = useState<InitialItem[]>(structuredClone(data.character.input.items));
  const [picker, setPicker] = useState<number | 'new' | null>(null);
  const initial = kind === 'account' ? { ...data.account.input,
    contractDays: Object.fromEntries(data.options.contracts.map(c => [c.value, data.account.input.contracts.find(v => v.type === c.value)?.days ?? 0])) } : data.character.input;
  function close() {
    if (busy) return;
    if (!dirty) { onClose(); return; }
    Modal.confirm({ title: '放弃尚未保存的修改？', okText: '放弃修改', cancelText: '继续编辑', onOk: onClose });
  }
  async function finish(values: AccountForm | CharacterInitialization) {
    if (kind === 'account') {
      const v = values as AccountForm;
      await onSave({ cera: v.cera, tokenCera: v.tokenCera, happyTokenCera: v.happyTokenCera, accountCargoLevel: v.accountCargoLevel,
        contracts: data.options.contracts.map(c => ({ type: c.value, days: v.contractDays[c.value] })) });
    } else await onSave({ ...(values as CharacterInitialization), items });
  }
  return <Modal title={kind === 'account' ? '编辑账号初始化配置' : '编辑角色初始化配置'} open width={860} onCancel={close}
    closable={!busy} keyboard={!busy} mask={{ closable: false }} destroyOnHidden footer={<Space>
      <Button disabled={busy} onClick={close}>取消</Button>
      {uncertain ? <Button type="primary" disabled={busy} onClick={onReload}>重新读取生效配置</Button> :
        <Button type="primary" loading={busy} onClick={() => form.submit()}>保存并生效</Button>}
    </Space>}>
    <Typography.Paragraph type="secondary">保存后对后续创建生效，已有账号与角色不变。</Typography.Paragraph>
    <Form form={form} layout="vertical" requiredMark={false} initialValues={initial} onValuesChange={() => setDirty(true)} onFinish={finish}
      onFinishFailed={() => noti.error('请核对配置字段。')} disabled={busy || uncertain}>
      {kind === 'account' ? <>
        <Typography.Title level={5}>点券</Typography.Title><div className="initialization-grid">
          {Object.entries(amountLabels).map(([name, label]) => <Form.Item key={name} name={name} label={label} rules={numberRules}><InputNumber min={0} max={2147483647} precision={0} style={{ width: '100%' }} /></Form.Item>)}
        </div><Typography.Title level={5}>契约</Typography.Title><Typography.Paragraph type="secondary">0 天表示不赋予。</Typography.Paragraph>
        <div className="initialization-grid">{data.options.contracts.map(c => <Form.Item key={c.value} name={['contractDays', String(c.value)]} label={`${c.label}天数`}
          rules={[{ required: true, type: 'integer', min: 0, max: 3650, message: '请输入 0—3650 的整数天数' }]}>
          <InputNumber min={0} max={3650} precision={0} style={{ width: '100%' }} /></Form.Item>)}</div>
        <Form.Item name="accountCargoLevel" label="初始账号仓库" rules={[{ required: true }]}><Select options={data.options.accountCargo} /></Form.Item>
      </> : <>
        <div className="initialization-grid"><Form.Item name="level" label="初始角色等级" rules={[{ required: true, type: 'integer', min: 1, max: data.options.maximumLevel, message: `请输入 1—${data.options.maximumLevel} 的整数等级` }]}>
          <InputNumber min={1} max={data.options.maximumLevel} precision={0} style={{ width: '100%' }} /></Form.Item>
          <Form.Item name="personalCargoLevel" label="初始个人仓库" rules={[{ required: true }]}><Select options={data.options.personalCargo} /></Form.Item>
        </div><Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 12 }}><Typography.Text strong>额外初始物品</Typography.Text>
          <Button disabled={busy || uncertain || items.length >= 10} onClick={() => setPicker('new')}>添加初始物品</Button></Space>
        <Typography.Paragraph type="secondary">职业基础赠送仍会保留。此处额外赠送永久普通背包物品，装备存入背包，最多 10 种。</Typography.Paragraph>
        <InitialItems items={items} {...access} actions={index => <Space>
          <Button size="small" disabled={busy || uncertain} onClick={() => setPicker(index)}>编辑物品 {index + 1}</Button>
          <Button size="small" danger disabled={busy || uncertain} onClick={() => { setItems(items.filter((_, i) => i !== index)); setDirty(true); }}>移除物品 {index + 1}</Button>
        </Space>} />
      </>}
    </Form>
    {picker !== null && <ItemPicker purpose="initialization" initial={picker === 'new' ? undefined : items[picker]} {...access}
      onClose={() => setPicker(null)} onChoose={item => {
        const next = [...items]; const existing = next.findIndex((v, i) => v.itemId === item.itemId && i !== picker);
        if (existing >= 0) return '同种初始物品请合并数量，或直接编辑已有物品。';
        if (picker === 'new') next.push({ itemId: item.itemId, count: item.count }); else next[picker] = { itemId: item.itemId, count: item.count };
        setItems(next); setPicker(null); setDirty(true); return undefined;
      }} />}
  </Modal>;
}

function InitialItems({ items, actions, ...access }: Access & { items: InitialItem[]; actions?: (index: number) => React.ReactNode }) {
  return items.length === 0 ? <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="不额外赠送物品" /> :
    <Space orientation="vertical" style={{ width: '100%' }}>{items.map((item, index) => <InitialItemRow key={`${index}:${item.itemId}`} item={item} actions={actions?.(index)} {...access} />)}</Space>;
}
function InitialItemRow({ item, actions, ...access }: Access & { item: InitialItem; actions?: React.ReactNode }) {
  const details = useGmRead<ItemCatalogDetails>(`initialization/items/${item.itemId}`, access.onExpired, access.onDenied);
  return <div className="mail-attachment-row"><div className="mail-attachment-description"><Typography.Text strong>{details.data?.item.name ?? `物品 ${item.itemId}`}</Typography.Text>
    <div className="mail-item-id">ID {item.itemId} · 每角色 {item.count.toLocaleString('zh-CN')} 个</div></div>{actions}</div>;
}
