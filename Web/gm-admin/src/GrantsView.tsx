import { useEffect, useState } from 'react';
import { Button, Drawer, Popconfirm, Table, Typography } from 'antd';
import { CheckCircleFilled, EditOutlined, ExclamationCircleFilled, FileTextOutlined, PlusOutlined, WalletOutlined } from '@ant-design/icons';
import { api } from './api';
import { useNav } from './AdminApp';
import { currencyNames, type CeraHistory, type CeraReceipt } from './cera';
import { balanceOf } from './CeraComposer';
import { bjTime, nf } from './format';
import { briefOf, useItemDetails } from './itemInfo';
import { equipText, type ItemInput } from './items';
import { emptyMail, type MailDocument, type MailHistory, type MailInput, type MailReceipt } from './mail';
import { preparedText } from './MailComposer';
import { useNotiBar } from './NotiBar';
import { useGrants } from './pending';
import { TemplateEditor } from './TemplateEditor';
import { useGmAction } from './useGmAction';
import { useGmRead } from './useGmRead';
import { Banner, KV, PageHead, Pill } from './ui';

type Receipt = { kind: 'mail'; value: MailReceipt } | { kind: 'cera'; value: CeraReceipt };
const hasContent = (input: MailInput) => !!(input.title || input.body || input.gold || input.items.length || input.recipientIds.length);

/** Item chips for history rows and templates; names come from the shared catalog cache. */
function Bits({ input, onExpired, onDenied }: { input: Pick<MailInput, 'gold' | 'items'>; onExpired: () => void; onDenied: () => void }) {
  const items = useItemDetails('mail', input.items.map(x => x.itemId), { onExpired, onDenied });
  return <span style={{ display: 'inline-flex', gap: 4, flexWrap: 'wrap' }}>{input.gold > 0 && <span className="chip">金币 <span className="mono">{nf(input.gold)}</span></span>}
    {input.items.map((x: ItemInput, index) => { const brief = briefOf(items.details[x.itemId]); const state = equipText(x.equipment);
      return <span className="chip" key={index}><span className={`r${brief?.rarity ?? 0}`}>{brief?.name ?? `物品 ${x.itemId}`}</span>{state && <span className="faint">{state}</span>}<span className="mono">×{x.count}</span></span>; })}
    {input.gold === 0 && input.items.length === 0 && <span className="faint small">无附件</span>}</span>;
}

export function GrantsView({ onExpired, onDeniedMail, onDeniedCera }: { onExpired: () => void; onDeniedMail: () => void; onDeniedCera: () => void }) {
  const nav = useNav(); const grants = useGrants(); const noti = useNotiBar();
  const canMail = nav.can('SendMail'); const canCera = nav.can('GrantCera');
  const mailAccess = { onExpired, onDenied: onDeniedMail };
  const [tab, setTab] = useState(() => grants.cera && !grants.mail && canCera ? 'cera' : canMail ? 'mail' : 'cera');
  const [mailPage, setMailPage] = useState(1); const [ceraPage, setCeraPage] = useState(1);
  const mailHistory = useGmRead<MailHistory>(canMail ? `mail/operations?page=${mailPage}&pageSize=20` : null, onExpired, onDeniedMail);
  const ceraHistory = useGmRead<CeraHistory>(canCera ? `cera/operations?page=${ceraPage}&pageSize=20` : null, onExpired, onDeniedCera);
  const templates = useGmRead<MailDocument[]>(canMail ? 'mail/templates' : null, onExpired, onDeniedMail);
  const draft = useGmRead<MailDocument>(canMail ? 'mail/draft' : null, onExpired, onDeniedMail);
  const [receipt, setReceipt] = useState<Receipt>();
  const [editing, setEditing] = useState<{ document?: MailDocument } | null>(null);
  const templateAction = useGmAction(mailAccess); const draftAction = useGmAction(mailAccess);
  useEffect(() => { if (grants.committed) { mailHistory.refresh(); ceraHistory.refresh(); } }, [grants.committed]);
  async function resolveMail(how: 'query' | 'retry') {
    const snapshot = grants.mail; if (!snapshot) return;
    if (how === 'query') { const value = await grants.queryMail(); if (value) setReceipt({ kind: 'mail', value }); return; }
    const { outcome, result } = await grants.sendMail(snapshot);
    if (outcome === 'committed' && result) setReceipt({ kind: 'mail', value: result.receipt });
    else if (outcome === 'rejected') nav.composeMail({ input: snapshot.input });
  }
  async function resolveCera(how: 'query' | 'retry') {
    const snapshot = grants.cera; if (!snapshot) return;
    if (how === 'query') { const value = await grants.queryCera(); if (value) setReceipt({ kind: 'cera', value }); return; }
    const { outcome, result } = await grants.grantCera(snapshot);
    if (outcome === 'committed' && result) setReceipt({ kind: 'cera', value: result.receipt });
    else if (outcome === 'rejected') nav.composeCera({ ...snapshot.input });
  }
  function discardDraft() {
    if (!draft.data) return;
    void draftAction.run(() => api<MailDocument>('mail/draft', { ...emptyMail(), expectedRevision: draft.data!.revision }, 'PUT'), () => { draft.refresh(); noti.success('草稿已丢弃。'); });
  }
  const tabs: [string, string, string | number | undefined][] = [
    ...(canMail ? [['mail', '邮件记录', mailHistory.data?.total]] as [string, string, number | undefined][] : []),
    ...(canCera ? [['cera', '点券记录', ceraHistory.data?.total]] as [string, string, number | undefined][] : []),
    ...(canMail ? [['templates', '邮件模板', templates.data ? `${templates.data.length}/20` : undefined]] as [string, string, string | undefined][] : []),
  ];
  const pendingBusy = (kind: 'mail' | 'cera') => grants.busy[kind];
  return <div className="page">
    <PageHead title="发放" info="邮件与点券都是整批提交：任一目标失败都会全部回滚。结果不明时只能用原编号查询或按原内容重试。">
      {canCera && <Button size="small" icon={<WalletOutlined />} disabled={!!grants.cera} onClick={() => nav.composeCera()}>发点券</Button>}
      {canMail && <Button size="small" type="primary" icon={<EditOutlined />} disabled={!!grants.mail} onClick={() => nav.composeMail()}>写邮件</Button>}
    </PageHead>
    {grants.mail && <Banner kind="crit" icon={<ExclamationCircleFilled />} title={`邮件发送结果未确认：${grants.mail.input.title || '无标题'}`}
      actions={<><Button size="small" loading={pendingBusy('mail')} onClick={() => void resolveMail('query')}>查询原编号结果</Button>
        <Button size="small" disabled={pendingBusy('mail')} onClick={() => void resolveMail('retry')}>按原内容安全重试</Button></>}>
      保留原编号和内容，查询或安全重试不会重复发放。编号 <Typography.Text copyable={{ text: grants.mail.requestId }} className="mono small">{grants.mail.requestId.slice(0, 8)}</Typography.Text>
      {' '}· {grants.mail.input.recipientIds.length} 个角色</Banner>}
    {grants.cera && <Banner kind="crit" icon={<ExclamationCircleFilled />} title={`点券发放结果未确认：${currencyNames[grants.cera.input.currency]} +${nf(grants.cera.input.amount)}`}
      actions={<><Button size="small" loading={pendingBusy('cera')} onClick={() => void resolveCera('query')}>查询原编号结果</Button>
        <Button size="small" disabled={pendingBusy('cera')} onClick={() => void resolveCera('retry')}>按原内容安全重试</Button></>}>
      保留原编号和内容，查询或安全重试不会重复到账。编号 <Typography.Text copyable={{ text: grants.cera.requestId }} className="mono small">{grants.cera.requestId.slice(0, 8)}</Typography.Text>
      {' '}· 账号 {grants.cera.input.accountIds.join('、')}</Banner>}
    {canMail && draft.data && hasContent(draft.data.input) && <Banner icon={<FileTextOutlined />} title="邮件草稿还在" actions={<>
      <Button size="small" disabled={!!grants.mail} onClick={() => nav.composeMail({ input: draft.data!.input, fromDraft: true })}>接着写</Button>
      <Popconfirm title="丢掉这份草稿？" okText="丢掉" cancelText="留着" onConfirm={discardDraft}><Button size="small" type="text" loading={draftAction.busy}>丢掉</Button></Popconfirm></>}>
      “{draft.data.input.title || '无标题'}” · {draft.data.input.items.length} 项附件 · 版本 {draft.data.revision}</Banner>}
    {draft.error && canMail && <Banner kind="attn" title="草稿读取失败" actions={<Button size="small" onClick={draft.refresh}>重新读取草稿</Button>} />}
    <section className="panel">
      <div className="panel-head">
        <div className="ptabs" role="tablist">{tabs.map(([key, label, count]) => <button key={key} role="tab" aria-selected={tab === key} className={tab === key ? 'on' : ''} onClick={() => setTab(key)}>
          {label}{count !== undefined && <em>{count}</em>}</button>)}</div>
        <div className="tools">
          {tab === 'templates' && <Button size="small" icon={<PlusOutlined />} disabled={(templates.data?.length ?? 0) >= 20} onClick={() => setEditing({})}>新建模板</Button>}
          <Button size="small" type="text" loading={tab === 'cera' ? ceraHistory.busy : tab === 'mail' ? mailHistory.busy : templates.busy}
            onClick={() => (tab === 'cera' ? ceraHistory : tab === 'mail' ? mailHistory : templates).refresh()}>刷新</Button>
        </div>
      </div>
      {tab === 'mail' && canMail && <Table<MailReceipt> className="ledger" size="small" rowKey="requestId" dataSource={mailHistory.data?.items ?? []} loading={mailHistory.busy} scroll={{ x: 900 }}
        locale={{ emptyText: '还没发过邮件' }} pagination={{ current: mailPage, pageSize: 20, total: mailHistory.data?.total ?? 0, size: 'small', showSizeChanger: false, onChange: setMailPage }}
        columns={[
          { title: '提交时间', dataIndex: 'committedAt', width: 140, render: v => <span className="mono small">{bjTime(v)}</span> },
          { title: '标题', key: 't', render: (_, r) => <span className="strong">{r.input.title || '无标题'}</span> },
          { title: '收件', key: 'r', align: 'end', width: 60, render: (_, r) => <span className="mono">{r.recipients.length}</span> },
          { title: '每人发放', key: 'c', render: (_, r) => <Bits input={r.input} {...mailAccess} /> },
          { title: '结果', key: 's', width: 90, render: () => <Pill kind="ok">已提交</Pill> },
          { title: '', key: 'op', align: 'end', width: 130, render: (_, r) => <span style={{ display: 'inline-flex', gap: 10 }}>
            <button className="linkbtn" onClick={() => setReceipt({ kind: 'mail', value: r })}>回执</button>
            <button className="linkbtn" disabled={!!grants.mail} onClick={() => nav.composeMail({ input: { ...structuredClone(r.input), recipientIds: [] } })}>再发一封</button></span> },
        ]} />}
      {tab === 'cera' && canCera && <Table<CeraReceipt> className="ledger" size="small" rowKey="requestId" dataSource={ceraHistory.data?.items ?? []} loading={ceraHistory.busy} scroll={{ x: 800 }}
        locale={{ emptyText: '还没发过点券' }} pagination={{ current: ceraPage, pageSize: 20, total: ceraHistory.data?.total ?? 0, size: 'small', showSizeChanger: false, onChange: setCeraPage }}
        columns={[
          { title: '提交时间', dataIndex: 'committedAt', width: 140, render: v => <span className="mono small">{bjTime(v)}</span> },
          { title: '币种', key: 'c', render: (_, r) => currencyNames[r.input.currency] },
          { title: '每账号增量', key: 'a', align: 'end', render: (_, r) => <span className="mono">+{nf(r.input.amount)}</span> },
          { title: '账号', key: 'n', align: 'end', render: (_, r) => <span className="mono">{r.accounts.length}</span> },
          { title: '原因', key: 'reason', render: (_, r) => r.input.reason || <span className="faint">—</span> },
          { title: '结果', key: 's', width: 90, render: () => <Pill kind="ok">已到账</Pill> },
          { title: '', key: 'op', align: 'end', width: 64, render: (_, r) => <button className="linkbtn" onClick={() => setReceipt({ kind: 'cera', value: r })}>回执</button> },
        ]} />}
      {tab === 'templates' && canMail && (!templates.data?.length ? <div className="empty-line">{templates.busy ? '正在读取…' : '还没有模板，写邮件时常用的内容可以存成模板'}</div> :
        <div>{templates.data.map(t => <div className="tpl" key={t.id}>
          <div style={{ minWidth: 0 }}><span className="strong">{t.name}</span> <span className="faint small">· 标题“{t.input.title || '无'}” · 版本 {t.revision}</span>
            <div className="bits"><Bits input={t.input} {...mailAccess} /></div></div>
          <div style={{ display: 'flex', gap: 4, flexWrap: 'wrap' }}>
            <Button size="small" disabled={!!grants.mail} onClick={() => nav.composeMail({ input: { ...structuredClone(t.input), recipientIds: [] } })}>用它写</Button>
            <Button size="small" type="text" disabled={templateAction.busy} onClick={() => setEditing({ document: t })}>编辑</Button>
            <Popconfirm title="删除这个模板？" description="已发送的邮件不受影响。" okText="删除" cancelText="算了" onConfirm={() => void templateAction.run(
              () => api<MailDocument>(`mail/templates/${t.id}`, { ...emptyMail(), expectedRevision: t.revision }, 'DELETE'), () => { templates.refresh(); noti.success('模板已删除。'); })}>
              <Button size="small" type="text" danger disabled={templateAction.busy}>删除</Button></Popconfirm>
          </div>
        </div>)}</div>)}
    </section>
    <Drawer size={600} open={!!receipt} onClose={() => setReceipt(undefined)} title={receipt?.kind === 'cera' ? '点券发放回执' : '邮件发送回执'} destroyOnHidden>
      {receipt?.kind === 'mail' && <MailReceiptView value={receipt.value} {...mailAccess} />}
      {receipt?.kind === 'cera' && <CeraReceiptView value={receipt.value} />}
    </Drawer>
    {editing && <TemplateEditor document={editing.document} templates={templates.data ?? []} {...mailAccess} onClose={() => setEditing(null)}
      onSaved={() => { setEditing(null); setTab('templates'); templates.refresh(); }} />}
  </div>;
}

function MailReceiptView({ value, onExpired, onDenied }: { value: MailReceipt; onExpired: () => void; onDenied: () => void }) {
  return <div className="grid">
    <Banner icon={<CheckCircleFilled />} title="已提交">邮件已提交，玩家可以去邮箱领附件。在线提醒不代表对方已读或已领取。</Banner>
    <KV rows={[['操作编号', <Typography.Text copyable className="mono small" key="id">{value.requestId}</Typography.Text>], ['管理员', value.administratorName],
      ['标题', value.input.title || '无标题'], ['正文', <span style={{ whiteSpace: 'pre-wrap' }} key="b">{value.input.body}</span>],
      ['每人发放', value.items ? <div key="i" style={{ display: 'grid', gap: 2 }}>{value.input.gold > 0 && <span>金币 <span className="mono">{nf(value.input.gold)}</span></span>}
        {value.items.map((x, index) => <span key={index}>{x.name ?? `物品 ${x.itemId}`} <span className="id">{x.itemId}</span> × {x.count} · <span className="dim small">{preparedText(x)}</span></span>)}</div>
        : <Bits key="i" input={value.input} onExpired={onExpired} onDenied={onDenied} />],
      ['提交', <span className="mono" key="c">{bjTime(value.committedAt, true)}</span>], ['到期', <span className="mono" key="e">{bjTime(value.expiresAt)}</span>]]} />
    <Table className="ledger" size="small" rowKey={x => x.character.characterId} pagination={false} dataSource={value.recipients} columns={[
      { title: '收件角色', key: 'c', render: (_, r) => <span>{r.character.name} <span className="id">{r.character.characterId}</span></span> },
      { title: '邮件 ID', dataIndex: 'mailId', render: v => <span className="mono">{v}</span> },
      { title: '装备 UID', key: 'u', render: (_, r) => r.equipmentUids.length ? <span className="mono small">{r.equipmentUids.join('、')}</span> : <span className="faint">无装备附件</span> }]} />
  </div>;
}
function CeraReceiptView({ value }: { value: CeraReceipt }) {
  const currency = value.input.currency;
  return <div className="grid">
    <Banner icon={<CheckCircleFilled />} title="已到账">余额已经写进账号；实际客户端显示仍需玩家核对。</Banner>
    <KV rows={[['操作编号', <Typography.Text copyable className="mono small" key="id">{value.requestId}</Typography.Text>], ['管理员', value.administratorName],
      ['币种', currencyNames[currency]], ['每账号', <span className="mono" key="a">+{nf(value.input.amount)}</span>], ['原因', value.input.reason || '—'],
      ['提交', <span className="mono" key="c">{bjTime(value.committedAt, true)}</span>]]} />
    <Table className="ledger" size="small" rowKey="accountId" pagination={false} dataSource={value.accounts} columns={[
      { title: '账号', key: 'a', render: (_, a) => <span>{a.loginName} <span className="id">#{a.accountId}</span></span> },
      { title: '发放前', key: 'b', align: 'end', render: (_, a) => <span className="mono">{nf(balanceOf(a.before, currency))}</span> },
      { title: '发放后', key: 'f', align: 'end', render: (_, a) => <span className="mono">{nf(balanceOf(a.after, currency))}</span> }]} />
  </div>;
}
