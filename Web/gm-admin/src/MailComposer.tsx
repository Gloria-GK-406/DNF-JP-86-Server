import { useEffect, useRef, useState } from 'react';
import { Button, Input, InputNumber, Popconfirm, Select, Table } from 'antd';
import { CloseOutlined, ExclamationCircleFilled, PlusOutlined, SafetyCertificateOutlined, SearchOutlined, SendOutlined } from '@ant-design/icons';
import { api, type PlayerPage } from './api';
import { useNav } from './AdminApp';
import { AttachmentRows } from './AttachmentRows';
import { careerName, DEFAULT_MAIL_BODY, mailDisplayText, nf, numberFormat, utf8Bytes } from './format';
import { ItemPicker } from './ItemPicker';
import { rememberDetails, useItemDetails } from './itemInfo';
import { amplifyTypes, mergeItems, qualityTiers, usedSlots, type ItemCatalogDetails, type ItemInput } from './items';
import { emptyMail, type MailDocument, type MailInput, type MailPreview, type MailPreviewItem, type MailRecipient } from './mail';
import { MailWindow, type MailSlot } from './MailWindow';
import { useNotiBar } from './NotiBar';
import { useGrants } from './pending';
import { useConfirmLeave, useGmAction } from './useGmAction';
import { useGmRead } from './useGmRead';
import { Banner, Field, KV, PageHead, Panel, Steps } from './ui';

export type MailSeed = { input?: Partial<MailInput>; recipients?: MailRecipient[]; fromDraft?: boolean };
type Access = { onExpired: () => void; onDenied: () => void };

export function MailFields({ input, update, prefix, locked }: { input: Pick<MailInput, 'title' | 'body' | 'gold'>; update: (patch: Partial<MailInput>) => void; prefix: string; locked?: boolean }) {
  return <div className="form">
    <Field label="邮件标题" htmlFor={`${prefix}-title`} hint="选填；和正文一样时游戏里只显示一次。"><Input id={`${prefix}-title`} value={input.title} disabled={locked} placeholder="例如：停服维护补偿"
      onChange={event => update({ title: event.target.value })} /></Field>
    <Field label="邮件正文" htmlFor={`${prefix}-body`} hint="选填；留空时发送默认文案。"><Input.TextArea id={`${prefix}-body`} rows={4} value={input.body} disabled={locked} placeholder={DEFAULT_MAIL_BODY}
      onChange={event => update({ body: event.target.value })} /></Field>
    <Field label="每人金币" htmlFor={`${prefix}-gold`}><InputNumber<number> id={`${prefix}-gold`} min={0} max={2147483647} precision={0} value={input.gold} disabled={locked} style={{ width: 200 }}
      {...numberFormat} onChange={gold => update({ gold: gold ?? 0 })} /></Field>
  </div>;
}
/** Expands attachments into game mail slots: every equipment piece takes its own slot. */
export function mailSlots(items: ItemInput[], details: Record<number, ItemCatalogDetails | undefined>, names: Record<number, string> = {}): MailSlot[] {
  return items.flatMap(item => { const loaded = details[item.itemId];
    const slot: MailSlot = { itemId: item.itemId, count: item.count, name: loaded?.item.name ?? names[item.itemId] ?? `物品 ${item.itemId}`, rarity: loaded?.item.rarity ?? null,
      equipment: item.equipment, details: loaded };
    return loaded?.kind === 'equipment' ? Array.from({ length: Math.min(item.count, 10) }, () => ({ ...slot, count: 1 })) : [slot]; });
}
export function preparedText(item: MailPreviewItem) {
  if (item.kind !== 'equipment') return '堆叠物';
  return ['普通装备', `耐久 ${item.durability}`, item.sealed ? '封装' : '未封装', qualityTiers[item.quality ?? 4]?.label,
    item.amplifyType ? `增幅 +${item.upgradeLevel ?? 0} · ${amplifyTypes[item.amplifyType] ?? ''}（基础值 ${item.amplifyValue}）` : item.upgradeLevel ? `强化 +${item.upgradeLevel}` : '',
    item.forgeLevel ? `锻造 +${item.forgeLevel}` : '', item.socketCount ? `${item.socketCount} 个空徽章孔` : ''].filter(Boolean).join(' · ');
}

export function MailComposer({ seed, onExpired, onDenied }: { seed?: MailSeed } & Access) {
  const access = { onExpired, onDenied };
  const nav = useNav(); const grants = useGrants(); const noti = useNotiBar(); const confirmLeave = useConfirmLeave();
  const { busy, run } = useGmAction(access);
  const [input, setInput] = useState<MailInput>(() => {
    const base = { ...emptyMail(), ...structuredClone(seed?.input ?? {}) };
    return seed?.recipients?.length ? { ...base, recipientIds: [...new Set([...base.recipientIds, ...seed.recipients.map(r => r.characterId)])] } : base;
  });
  const [known, setKnown] = useState<Record<number, MailRecipient>>(() => Object.fromEntries((seed?.recipients ?? []).map(r => [r.characterId, r])));
  const [dirty, setDirty] = useState(!!seed && !seed.fromDraft);
  const draft = useGmRead<MailDocument>('mail/draft', onExpired, onDenied);
  const [revision, setRevision] = useState<number>();
  useEffect(() => { if (revision === undefined && draft.data) setRevision(draft.data.revision); }, [draft.data, revision]);
  const templates = useGmRead<MailDocument[]>('mail/templates', onExpired, onDenied);
  const items = useItemDetails('mail', input.items.map(item => item.itemId), access);
  const [step, setStep] = useState(0); const [preview, setPreview] = useState<MailPreview>(); const requestId = useRef('');
  const [picker, setPicker] = useState(false);
  const alive = useRef(true); useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  const sending = grants.busy.mail; const locked = busy || sending;
  function update(patch: Partial<MailInput>) { setInput(old => ({ ...old, ...patch })); setDirty(true); setPreview(undefined); }
  const text = mailDisplayText(input.title, input.body); const bytes = utf8Bytes(text);
  const used = usedSlots(input.items, items.details);
  const countError = input.items.some(item => item.count < 1 || items.details[item.itemId]?.kind === 'equipment' && item.count > 10);
  const contentOk = bytes <= 511 && items.ready && used <= 10 && input.items.length <= 10 && !countError;
  const leave = () => confirmLeave(dirty, () => nav.go('/grants'), { title: '不写了吗？', content: '没保存的修改会丢掉，已经存过的草稿不受影响。', ok: '不写了', cancel: '继续写' });
  function saveDraft() {
    if (revision === undefined) { noti.error('请先重新加载草稿，取得当前版本后再保存。'); return; }
    void run(() => api<MailDocument>('mail/draft', { ...structuredClone(input), expectedRevision: revision }, 'PUT'), value => {
      setRevision(value.revision); setDirty(false); draft.refresh(); noti.success('草稿已保存。');
    });
  }
  function loadDraft() {
    confirmLeave(dirty, () => void run(() => api<MailDocument>('mail/draft'), value => {
      setInput(structuredClone(value.input)); setRevision(value.revision); setDirty(false); setPreview(undefined); setStep(0); noti.success('已载入保存的草稿。');
    }), { title: '用草稿覆盖当前编辑？', content: '当前未保存的修改会丢掉。', ok: '载入草稿', cancel: '继续编辑' });
  }
  function prepare() {
    void run(() => api<MailPreview>('mail/preview', structuredClone(input)), value => {
      requestId.current = crypto.randomUUID(); setPreview(value); setStep(2);
      setKnown(old => ({ ...old, ...Object.fromEntries(value.recipients.map(r => [r.characterId, r])) }));
    });
  }
  async function send() {
    if (!preview || locked || grants.mail) return;
    const { outcome } = await grants.sendMail({ requestId: requestId.current, input: structuredClone(preview.input) });
    if (!alive.current) return;
    if (outcome === 'committed') { setDirty(false); nav.go('/grants'); }
    else if (outcome === 'rejected') { setPreview(undefined); setStep(0); }
    else { noti.warning('结果暂未确认：请到「发放」页查询原编号或按原内容重试，勿新建编号再次发送。', { key: 'mail-unconfirmed' }); nav.go('/grants'); }
  }
  const slots = step === 2 && preview ? mailSlots(preview.input.items, items.details, Object.fromEntries(preview.items.map(x => [x.itemId, x.name ?? '']))) : mailSlots(input.items, items.details);
  const windowText = step === 2 && preview ? preview.displayText : text;
  const status = step === 0 ? (bytes > 511 ? '标题加正文超过 511 字节了' : !items.ready && input.items.length ? '正在核对附件规则…' : used > 10 ? '附件超过 10 格了' : countError ? '附件数量不对' : dirty ? '有修改还没存成草稿' : '和草稿一致')
    : step === 1 ? (input.recipientIds.length ? `已选 ${input.recipientIds.length} 个角色` : '至少选 1 个收件角色') : '最后看一眼，没问题就发';
  return <>
    <div className="page">
      <PageHead title="写邮件" info="系统官方来源 · 30 天有效 · 整批提交，任何目标或附件失败都会全部回滚。核对之后再改内容需要重新核对。">
        <Steps labels={['写内容', '选收件人', '核对发送']} step={step} extra={[null, `${input.recipientIds.length}/100`, null]} />
        <Button size="small" icon={<CloseOutlined />} disabled={locked} onClick={leave}>关闭</Button>
      </PageHead>
      {grants.mail && <Banner kind="crit" icon={<ExclamationCircleFilled />} title="还有一封邮件结果未确认" actions={<Button size="small" danger onClick={() => nav.go('/grants')}>去处理</Button>}>
        先用原编号查询或按原内容重试，处理完才能发新的。</Banner>}
      <div className="grid g-main-side">
        <div className="grid" style={{ minWidth: 0 }}>
          {step === 0 && <>
            <Panel title="信件内容" tools={<>
              <Select size="small" aria-label="从模板填入" placeholder="从模板填入" style={{ width: 150 }} value={null} disabled={locked}
                options={templates.data?.map(t => ({ value: t.id, label: t.name }))} onChange={id => {
                  const template = templates.data?.find(t => t.id === id);
                  if (template) { update({ ...structuredClone(template.input), recipientIds: [...input.recipientIds] }); noti.success('模板内容填好了，收件人没动。'); }
                }} />
              {draft.data && (draft.data.revision > 0) && <Button size="small" type="text" disabled={locked} onClick={loadDraft}>载入草稿</Button>}</>}>
              <MailFields input={input} update={update} prefix="mail" locked={locked} />
            </Panel>
            <Panel title="附件" count={`${items.ready ? used : '…'}/10 格`} flush tools={<Button size="small" icon={<PlusOutlined />} disabled={locked} onClick={() => setPicker(true)}>添加物品</Button>}>
              <AttachmentRows items={input.items} details={items.details} failed={items.failed} onRetry={items.retry} locked={locked} onChange={rows => update({ items: rows })}
                empty="背包空空如也，点右上角“添加物品”" />
            </Panel>
          </>}
          {step === 1 && <Recipients input={input} known={known} locked={locked} {...access} onSeen={rows => setKnown(old => ({ ...old, ...Object.fromEntries(rows.map(r => [r.characterId, r])) }))}
            onChange={recipientIds => update({ recipientIds })} />}
          {step === 2 && preview && <Panel title="核对">
            <div className="grid" style={{ gap: 12 }}>
              <Banner kind="attn" icon={<SafetyCertificateOutlined />} title="整批提交">要么全成功，要么全回滚。结果不明时，只能用这次的编号查询或按原内容重试。</Banner>
              <KV rows={[['收件角色', <span key="r"><span className="mono">{preview.recipients.length}</span> 个</span>], ['每人金币', <span className="mono" key="g">{nf(preview.input.gold)}</span>],
                ['合计金币', <span className="mono" key="t">{nf(preview.totalGold)}</span>], ['合计物品', <span className="mono" key="i">{nf(preview.totalItems)}</span>],
                ['文字长度', <span key="b"><span className="mono">{preview.utf8Bytes}</span> / 511 字节</span>]]} />
              {preview.items.length > 0 && <div className="tray" style={{ display: 'grid', gap: 4 }}>{preview.items.map((x, index) => <span key={index} className="small">
                <b>{x.name ?? `物品 ${x.itemId}`}</b> <span className="id">{x.itemId}</span> × {x.count} / 人 · <span className="dim">{preparedText(x)}</span></span>)}</div>}
              <div className="tray">{preview.recipients.map(r => <span className="chip" key={r.characterId}>{r.name} <span className="mono">{r.characterId}</span></span>)}</div>
            </div>
          </Panel>}
        </div>
        <Panel title="游戏内预览" count={step === 2 ? '服务端预览结果' : '以服务端预览为准'} flush>
          <MailWindow text={windowText} slots={slots} gold={step === 2 && preview ? preview.input.gold : input.gold} />
          <div className="meter" style={{ margin: '0 14px 14px' }}><div className="meter-row"><span>标题 + 正文</span>
            <span className="mono" style={{ color: bytes > 511 ? 'var(--crit)' : undefined }}>{bytes} / 511 字节</span></div>
            <span className="meter-track"><span className={`meter-fill ${bytes > 511 ? 'over' : ''}`} style={{ width: `${Math.min(100, bytes / 511 * 100)}%` }} /></span></div>
        </Panel>
      </div>
    </div>
    <div className="actionbar">
      <span className="grow">{status}</span>
      {step < 2 && <Button disabled={locked || revision === undefined} onClick={saveDraft}>保存草稿</Button>}
      {step > 0 && <Button disabled={locked} onClick={() => { setPreview(undefined); setStep(step - 1); }}>上一步</Button>}
      {step === 0 && <Button type="primary" disabled={!contentOk || locked} onClick={() => setStep(1)}>下一步：选收件人</Button>}
      {step === 1 && <Button type="primary" loading={busy} disabled={!input.recipientIds.length || sending || !!grants.mail} onClick={prepare}>预览并核对</Button>}
      {step === 2 && <Popconfirm title="确认整批发送？" description={`${preview?.recipients.length ?? 0} 个角色，每人 ${used} 格附件。`} okText="发吧" cancelText="再看看" onConfirm={() => void send()}
        disabled={!preview || busy || !!grants.mail}>
        <Button type="primary" loading={sending} disabled={!preview || busy || !!grants.mail} icon={<SendOutlined />}>确认整批发送</Button></Popconfirm>}
    </div>
    {picker && <ItemPicker purpose="mail" existing={input.items} known={items.details} {...access} onClose={() => setPicker(false)} onConfirm={(adds, loaded) => {
      try {
        const merged = mergeItems(input.items, adds);
        if (merged.length > 10 || usedSlots(merged, loaded) > 10) return '每人最多 10 个展开物品附件；每件装备单独占一个附件位。';
        Object.values(loaded).forEach(value => rememberDetails('mail', value));
        update({ items: merged }); noti.success(`${adds.length} 项附件放进去了。`); return undefined;
      } catch (failure) { return (failure as Error).message; }
    }} />}
  </>;
}

function Recipients({ input, known, locked, onChange, onSeen, onExpired, onDenied }: Access & { input: MailInput; known: Record<number, MailRecipient>; locked: boolean;
  onChange: (ids: number[]) => void; onSeen: (rows: MailRecipient[]) => void }) {
  const [text, setText] = useState(''); const [filter, setFilter] = useState(''); const [page, setPage] = useState(1);
  const recipients = useGmRead<PlayerPage<MailRecipient>>(`mail/recipients?page=${page}&pageSize=20${filter}`, onExpired, onDenied);
  useEffect(() => { if (recipients.data) onSeen(recipients.data.items); }, [recipients.data]);
  function search(value = text) {
    const q = value.trim(); setPage(1);
    setFilter(/^\d{1,10}$/.test(q) && +q > 0 && +q <= 2147483647 ? `&characterId=${q}` : q ? `&q=${encodeURIComponent(q.slice(0, 128))}` : '');
  }
  const selected = input.recipientIds;
  return <section className="panel" aria-label="收件角色">
    <div className="toolbar"><strong style={{ fontSize: 13.5 }}>收件角色</strong><span className="mono faint small">{selected.length}/100</span>
      <Input size="small" allowClear prefix={<SearchOutlined />} aria-label="收件角色搜索" placeholder="角色姓名，或输入数字按角色 ID 查" maxLength={128} value={text} disabled={locked}
        onChange={event => { setText(event.target.value); if (!event.target.value) search(''); }} onPressEnter={() => search()} style={{ width: 260, marginInlineStart: 'auto' }} />
      <Button size="small" disabled={locked} onClick={() => search()}>查找</Button></div>
    {selected.length > 0 && <div className="tray" style={{ padding: '10px 14px', borderBottom: '1px solid var(--line)' }}>{selected.map(id => <span className="chip" key={id}>
      {known[id]?.name ?? '角色'} <span className="mono">{id}</span>
      <button className="linkbtn" aria-label={`取消角色 ${id}`} disabled={locked} style={{ color: 'var(--ink-3)' }} onClick={() => onChange(selected.filter(x => x !== id))}><CloseOutlined /></button></span>)}</div>}
    <Table<MailRecipient> className="ledger" size="small" rowKey="characterId" loading={recipients.busy} dataSource={recipients.data?.items ?? []} scroll={{ x: 520 }}
      locale={{ emptyText: recipients.error ? '查询失败' : '没有符合条件的有效角色' }}
      pagination={{ current: page, pageSize: 20, total: recipients.data?.total ?? 0, size: 'small', showSizeChanger: false, onChange: setPage }}
      rowSelection={{ selectedRowKeys: selected, preserveSelectedRowKeys: true, hideSelectAll: true,
        getCheckboxProps: r => ({ disabled: locked || !selected.includes(r.characterId) && selected.length >= 100, 'aria-label': `选择角色 ${r.characterId}` }),
        onSelect: (r, checked) => onChange(checked ? [...selected, r.characterId].slice(0, 100) : selected.filter(x => x !== r.characterId)) }}
      columns={[
        { title: '角色', key: 'n', render: (_, r) => <span><span className="strong">{r.name}</span> <span className="id">{r.characterId}</span></span> },
        { title: '职业', key: 'j', render: (_, r) => careerName(r.job, r.growType) },
        { title: 'Lv', dataIndex: 'level', render: v => <span className="mono">{v}</span> },
        { title: '姓名诊断', dataIndex: 'nameDecodeStatus', render: v => v === 'valid' ? <span className="faint">正常</span> : v === 'missing' ? '字节缺失' : '编码异常' },
      ]} />
  </section>;
}
