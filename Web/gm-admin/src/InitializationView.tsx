import { Fragment, useEffect, useRef, useState } from 'react';
import { Button, InputNumber, Popconfirm, Select } from 'antd';
import { EditOutlined, PlusOutlined, ReloadOutlined } from '@ant-design/icons';
import { api, ApiError } from './api';
import { AttachmentRows } from './AttachmentRows';
import { bjTime, nf, numberFormat } from './format';
import { ItemPicker } from './ItemPicker';
import { rememberDetails, useItemDetails } from './itemInfo';
import { mergeItems, type ItemCatalogDetails } from './items';
import type { AccountInitialization, CharacterInitialization, InitializationDocument, InitializationViewData } from './initialization';
import { useNotiBar } from './NotiBar';
import { useConfirmLeave } from './useGmAction';
import { useGmRead } from './useGmRead';
import { Field, LockBanner, PageHead, Panel } from './ui';

type Access = { onExpired: () => void; onDenied: () => void };
type Kind = 'account' | 'character';
type AccountDraft = Omit<AccountInitialization, 'contracts'> & { contracts: Record<number, number> };
const amountLabels = { cera: '初始点券', tokenCera: '初始代币券', happyTokenCera: '初始欢乐代币券' } as const;

export function InitializationView(access: Access) {
  const noti = useNotiBar();
  const current = useGmRead<InitializationViewData>('initialization', access.onExpired, access.onDenied);
  const [uncertain, setUncertain] = useState(false); const [busy, setBusy] = useState<Kind | null>(null);
  const [editing, setEditing] = useState<Record<Kind, boolean>>({ account: false, character: false });
  const alive = useRef(true); const saving = useRef(false);
  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  async function save(kind: Kind, revision: number, input: AccountInitialization | CharacterInitialization) {
    if (saving.current || uncertain) return false;
    saving.current = true; setBusy(kind);
    try {
      await api(`initialization/${kind}`, { expectedRevision: revision, input });
      if (!alive.current) return false;
      current.refresh(); noti.success('初始化配置已保存并生效，仅影响后续创建。'); return true;
    } catch (failure) {
      if (!alive.current) return false;
      if (failure instanceof ApiError && failure.status === 401) access.onExpired();
      else if (failure instanceof ApiError && failure.status === 403) access.onDenied();
      else {
        if (!(failure instanceof ApiError) || failure.status >= 500 || failure.status === 409) setUncertain(true);
        noti.failure(failure);
      }
      return false;
    } finally { saving.current = false; if (alive.current) setBusy(null); }
  }
  function reload() { setUncertain(false); setEditing({ account: false, character: false }); current.refresh(); }
  const data = current.data;
  return <div className="page">
    <PageHead title="初始化配置" info="账号配置在注册时执行一次；角色配置在每次创角时执行。只影响之后的创建，已有账号和角色不变。">
      <Button size="small" icon={<ReloadOutlined />} loading={current.busy} disabled={!!busy} onClick={reload}>{uncertain ? '重新读取生效配置' : '刷新配置'}</Button>
    </PageHead>
    {uncertain && <LockBanner onReload={reload} busy={current.busy} label="重新读取生效配置" />}
    {!data ? <section className="panel"><div className="empty-line">{current.busy ? '正在读取配置…' : '暂未读取到配置'}</div></section> :
      <div className="grid g-2">
        <InitPanel kind="account" data={data} locked={uncertain || current.busy} busy={busy === 'account'} editing={editing.account} {...access}
          onEdit={value => setEditing(old => ({ ...old, account: value }))} onSave={(revision, input) => save('account', revision, input)} />
        <InitPanel kind="character" data={data} locked={uncertain || current.busy} busy={busy === 'character'} editing={editing.character} {...access}
          onEdit={value => setEditing(old => ({ ...old, character: value }))} onSave={(revision, input) => save('character', revision, input)} />
      </div>}
  </div>;
}

function rowsOf(kind: Kind, data: InitializationViewData, input: AccountDraft | CharacterInitialization, names: Record<number, ItemCatalogDetails | undefined>): [string, string][] {
  if (kind === 'account') {
    const value = input as AccountDraft;
    return [...Object.entries(amountLabels).map(([key, label]) => [label, nf(value[key as keyof typeof amountLabels])] as [string, string]),
      ['初始账号仓库', data.options.accountCargo.find(c => c.value === value.accountCargoLevel)?.label ?? `${value.accountCargoLevel} 格`],
      ...data.options.contracts.map(c => [c.label, value.contracts[c.value] ? `${value.contracts[c.value]} 天` : '不赋予'] as [string, string])];
  }
  const value = input as CharacterInitialization;
  return [['初始角色等级', `Lv.${value.level}`], ['初始个人仓库', data.options.personalCargo.find(c => c.value === value.personalCargoLevel)?.label ?? `${value.personalCargoLevel} 格`],
    ['职业基础赠送', '保留职业初始装备、技能和 10 个复活币'],
    ['额外初始物品', value.items.length ? value.items.map(x => `${names[x.itemId]?.item.name ?? `物品 ${x.itemId}`} ×${nf(x.count)}`).join('、') : '不额外赠送']];
}
function draftOf(kind: Kind, data: InitializationViewData): AccountDraft | CharacterInitialization {
  if (kind === 'character') return structuredClone(data.character.input);
  const input = data.account.input;
  return { ...structuredClone(input), contracts: Object.fromEntries(data.options.contracts.map(c => [c.value, input.contracts.find(v => v.type === c.value)?.days ?? 0])) };
}
function InitPanel({ kind, data, locked, busy, editing, onEdit, onSave, onExpired, onDenied }: Access & { kind: Kind; data: InitializationViewData; locked: boolean; busy: boolean;
  editing: boolean; onEdit: (value: boolean) => void; onSave: (revision: number, input: AccountInitialization | CharacterInitialization) => Promise<boolean> }) {
  const access = { onExpired, onDenied }; const confirmLeave = useConfirmLeave();
  const doc: InitializationDocument<unknown> = data[kind];
  const [draft, setDraft] = useState<AccountDraft | CharacterInitialization | null>(null);
  const [snapshot, setSnapshot] = useState<InitializationViewData | null>(null);
  const [picker, setPicker] = useState(false);
  useEffect(() => { if (!editing) { setDraft(null); setSnapshot(null); } }, [editing]);
  const itemIds = [...data.character.input.items, ...(kind === 'character' && draft ? (draft as CharacterInitialization).items : [])].map(x => x.itemId);
  const items = useItemDetails('initialization', kind === 'character' ? itemIds : [], access);
  const base = snapshot ?? data;
  const before = rowsOf(kind, base, draftOf(kind, base), items.details);
  const after = draft ? rowsOf(kind, base, draft, items.details) : null;
  const diffs = after ? after.map((row, index) => [row[0], before[index][1], row[1]]).filter(([, a, b]) => a !== b) : [];
  const set = (patch: Partial<AccountDraft & CharacterInitialization>) => setDraft(old => old && ({ ...old, ...patch }));
  const valid = !draft || (kind === 'account' ? Object.keys(amountLabels).every(k => { const v = (draft as AccountDraft)[k as keyof typeof amountLabels]; return Number.isInteger(v) && v >= 0 && v <= 2147483647; })
    && Object.values((draft as AccountDraft).contracts).every(v => Number.isInteger(v) && v >= 0 && v <= 3650)
    : Number.isInteger((draft as CharacterInitialization).level) && (draft as CharacterInitialization).level >= 1 && (draft as CharacterInitialization).level <= base.options.maximumLevel
      && (draft as CharacterInitialization).items.length <= 10);
  const title = kind === 'account' ? '账号初始化' : '角色初始化';
  const meta = doc.revision === 0 ? '系统默认配置' : `版本 ${doc.revision} · ${doc.updatedBy ?? '—'} · ${bjTime(doc.updatedAt)}`;
  function start() { setSnapshot(structuredClone(data)); setDraft(draftOf(kind, data)); onEdit(true); }
  function cancel() { confirmLeave(diffs.length > 0, () => onEdit(false), { title: '放弃这次修改？', content: '还没保存的修改会丢掉。', ok: '放弃修改', cancel: '继续编辑' }); }
  async function save() {
    if (!draft || !snapshot) return;
    const input = kind === 'account' ? { ...(draft as AccountDraft), contracts: snapshot.options.contracts.map(c => ({ type: c.value, days: (draft as AccountDraft).contracts[c.value] ?? 0 })) }
      : { ...(draft as CharacterInitialization), items: (draft as CharacterInitialization).items.map(({ itemId, count }) => ({ itemId, count })) };
    if (await onSave(snapshot[kind].revision, input)) onEdit(false);
  }
  return <Panel title={title} count={meta} label={title}
    tools={!draft && <Button size="small" icon={<EditOutlined />} disabled={locked} onClick={start}>{kind === 'account' ? '编辑账号配置' : '编辑角色配置'}</Button>}
    foot={draft && <><span className="grow">{diffs.length ? `改了 ${diffs.length} 项` : '还没有改动'}</span><Button size="small" disabled={busy} onClick={cancel}>取消</Button>
      <Popconfirm title="保存并生效？" description="只影响之后的注册 / 创角。" okText="保存" cancelText="再看看" onConfirm={() => void save()} disabled={!diffs.length || !valid || locked || busy}>
        <Button size="small" type="primary" loading={busy} disabled={!diffs.length || !valid || locked}>保存并生效</Button></Popconfirm></>}>
    {!draft ? <dl className="kv flat">{before.map(([key, value]) => <Fragment key={key}><dt>{key}</dt><dd>{value}</dd></Fragment>)}</dl> :
      <div className="form">
        {kind === 'account' ? <>
          <div className="sub-title">点券</div>
          <div className="fgrid">{Object.entries(amountLabels).map(([key, label]) => <Field key={key} label={label} htmlFor={`init-${key}`}>
            <InputNumber<number> id={`init-${key}`} min={0} max={2147483647} precision={0} style={{ width: '100%' }} {...numberFormat} disabled={locked || busy}
              value={(draft as AccountDraft)[key as keyof typeof amountLabels]} onChange={value => set({ [key]: value ?? 0 })} /></Field>)}</div>
          <div className="sub-title">契约（0 天表示不赋予）</div>
          <div className="fgrid">{base.options.contracts.map(c => <Field key={c.value} label={`${c.label}天数`} htmlFor={`init-contract-${c.value}`}>
            <InputNumber id={`init-contract-${c.value}`} min={0} max={3650} precision={0} suffix="天" style={{ width: '100%' }} disabled={locked || busy}
              value={(draft as AccountDraft).contracts[c.value]} onChange={value => set({ contracts: { ...(draft as AccountDraft).contracts, [c.value]: value ?? 0 } })} /></Field>)}</div>
          <Field label="初始账号仓库" htmlFor="init-account-cargo"><Select id="init-account-cargo" options={base.options.accountCargo} disabled={locked || busy}
            value={(draft as AccountDraft).accountCargoLevel} onChange={value => set({ accountCargoLevel: value })} /></Field>
        </> : <>
          <div className="fgrid">
            <Field label="初始角色等级" htmlFor="init-level" hint={`1—${base.options.maximumLevel}`}><InputNumber id="init-level" min={1} max={base.options.maximumLevel} precision={0} style={{ width: '100%' }}
              disabled={locked || busy} value={(draft as CharacterInitialization).level} onChange={value => set({ level: value ?? 1 })} /></Field>
            <Field label="初始个人仓库" htmlFor="init-personal-cargo"><Select id="init-personal-cargo" options={base.options.personalCargo} disabled={locked || busy}
              value={(draft as CharacterInitialization).personalCargoLevel} onChange={value => set({ personalCargoLevel: value })} /></Field>
          </div>
          <div className="sub-title" style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}><span>额外初始物品（{(draft as CharacterInitialization).items.length}/10 种）</span>
            <Button size="small" icon={<PlusOutlined />} disabled={locked || busy || (draft as CharacterInitialization).items.length >= 10} onClick={() => setPicker(true)}>添加初始物品</Button></div>
          <div className="panel"><AttachmentRows items={(draft as CharacterInitialization).items} details={items.details} failed={items.failed} onRetry={items.retry} equipConfig={false}
            perLabel="/角色" locked={locked || busy} onChange={rows => set({ items: rows.map(({ itemId, count }) => ({ itemId, count })) })} empty="不额外赠送；职业基础赠送照常保留" /></div>
          {picker && <ItemPicker purpose="init" existing={(draft as CharacterInitialization).items} known={items.details} {...access} onClose={() => setPicker(false)} onConfirm={(adds, loaded) => {
            const merged = mergeItems((draft as CharacterInitialization).items, adds);
            if (merged.length > 10) return '最多 10 种额外初始物品。';
            Object.values(loaded).forEach(value => rememberDetails('initialization', value));
            set({ items: merged.map(({ itemId, count }) => ({ itemId, count })) }); return undefined;
          }} />}
        </>}
        {diffs.length > 0 && <div className="diff"><b>保存后会变成：</b>{diffs.map(([key, a, b]) => <span key={key}>{key}：<span className="old">{a}</span> → <b>{b}</b></span>)}</div>}
      </div>}
  </Panel>;
}
