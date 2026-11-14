import { useState } from 'react';
import { useLocation } from 'react-router-dom';
import { Button, Input, InputNumber } from 'antd';
import { CloseOutlined, PlusOutlined } from '@ant-design/icons';
import { DEFAULT_MAIL_BODY, nf, numberFormat } from './format';
import { ItemIcon, ItemName, useItemDetails } from './itemInfo';
import { ItemSearch } from './ItemSearch';
import { mergeItems, type ItemCatalogEntry, type ItemInput } from './items';
import { JobTile } from './PlayerAccountView';
import { PendingBanner, usePlayer, useSubmission } from './PlayerApp';
import type { PlayerMailResult } from './playerApi';
import { Banner, Field, Panel, Pill } from './ui';

/* 玩家发邮件：物品检索内联在附件面板（与 GM 邮件同一目录接口与件数规则：装备每件一格，共 10 格）。
   物品只带 itemId 与 count；装备选项不可配置（RQ-011）。 */
const MAX = 2147483647;
type MailBody = { characterId: number; title: string; body: string; items: { itemId: number; count: number }[]; gold: number };

export function PlayerMailView() {
  const { me, refreshMe, items: source } = usePlayer(); const location = useLocation();
  const { pending, busy, submit, discard } = useSubmission<MailBody, PlayerMailResult>('mail/send');
  const seeded = (location.state as { characterId?: number } | null)?.characterId;
  const [characterId, setCharacterId] = useState<number | undefined>(() => me?.characters.some(c => c.characterId === seeded) ? seeded : undefined);
  const [attachments, setAttachments] = useState<ItemInput[]>([]);
  const [kinds, setKinds] = useState<Record<number, string>>({});
  const [names, setNames] = useState<Record<number, ItemCatalogEntry>>({});
  const [gold, setGold] = useState<number | null>(0);
  const [title, setTitle] = useState(''); const [body, setBody] = useState('');
  const [tried, setTried] = useState(false); const [done, setDone] = useState<string>();
  const enabled = me?.portal.mailEnabled === true;
  const access = { onExpired: () => {}, onDenied: () => void refreshMe() };
  // After a remount a locked mail shows the pending content, so its items are what the names and slots come from.
  const details = useItemDetails('mail', enabled ? (pending?.body.items ?? attachments).map(item => item.itemId) : [], access, source);
  if (!me) return null;
  const kindOf = (itemId: number) => details.details[itemId]?.kind ?? kinds[itemId];
  const slotsOf = (list: ItemInput[]) => list.reduce((total, item) => total + (kindOf(item.itemId) === 'equipment' ? item.count : 1), 0);
  const shown: MailBody = pending?.body ?? { characterId: characterId ?? 0, title, body, items: attachments.map(({ itemId, count }) => ({ itemId, count })), gold: gold ?? 0 };
  const slots = slotsOf(shown.items);
  const locked = !enabled || !!pending || busy;
  const perMail = me.portal.limits.mailGoldPerMail; const perDay = me.portal.limits.mailsPerDay;
  const goldError = gold === null ? '请输入金币' : perMail > 0 && gold > perMail ? `单封上限 ${nf(perMail)}` : undefined;
  const countError = (item: ItemInput) => item.count < 1 ? '数量至少 1' : slots > 10 ? '超过 10 格' : undefined;
  const empty = !body.trim() && attachments.length === 0 && !(gold && gold > 0);
  const missing = characterId === undefined ? '请选择收件角色' : empty ? '正文、附件、金币至少填一项' : undefined;
  const ready = !goldError && attachments.every(item => !countError(item)) && details.ready;
  function added(entry: ItemCatalogEntry) {
    try { return mergeItems(attachments, [{ itemId: entry.itemId, count: 1 }]); } catch { return null; }
  }
  function add(entry: ItemCatalogEntry) {
    const next = added(entry); if (!next) return;
    setKinds(old => ({ ...old, [entry.itemId]: entry.category })); setNames(old => ({ ...old, [entry.itemId]: entry }));
    setAttachments(next); setDone(undefined);
  }
  const canAdd = (entry: ItemCatalogEntry) => { const next = added(entry);
    return !!next && next.reduce((total, item) => total + ((item.itemId === entry.itemId ? entry.category : kindOf(item.itemId)) === 'equipment' ? item.count : 1), 0) <= 10; };
  function send(mail: MailBody) {
    setDone(undefined);
    void submit(mail, result => {
      setDone(`→ ${result.receipt.characterName ?? ''}`);
      setAttachments([]); setGold(0); setTitle(''); setBody(''); setTried(false); void refreshMe();
    });
  }
  function trySend() { setTried(true); if (missing || !ready || locked) return; send(shown); }
  const recipient = me.characters.find(c => c.characterId === shown.characterId);
  const brief = (itemId: number) => { const loaded = details.details[itemId]; const entry = names[itemId];
    return { name: loaded?.item.name ?? entry?.name, rarity: loaded?.item.rarity ?? entry?.rarity, category: loaded?.item.category ?? entry?.category }; };
  const disabledTag = !enabled && <Pill kind="idle">已停用</Pill>;
  return <div className="page">
    <div className="grid g-main-side">
      <div className="grid">
        <Panel title="收件角色" tools={disabledTag}>
          <div className="rcpt" role="radiogroup" aria-label="收件角色">{me.characters.map(c => <button key={c.characterId} role="radio" aria-checked={shown.characterId === c.characterId}
            className={shown.characterId === c.characterId ? 'on' : ''} disabled={locked} onClick={() => { setCharacterId(c.characterId); setDone(undefined); }}>
            <JobTile job={c.job} /><b>{c.name}</b><span>Lv.{c.level}{c.isOnline ? ' · 在线' : ''}</span></button>)}</div>
        </Panel>
        <Panel title="附件" count={`${slots} / 10`}>
          <div className="grid">
            {enabled && !pending && <div className="item-search"><ItemSearch purpose="mail" requireQuery {...access}
              emptyText={({ busy: reading, error }) => reading ? '' : error ? '目录读取失败' : '没有符合条件的物品'}
              queued={entry => attachments.some(item => item.itemId === entry.itemId)}
              tags={entry => { const count = attachments.find(item => item.itemId === entry.itemId)?.count; return count ? <span className="chip accent">已加 {nf(count)}</span> : null; }}
              action={entry => <Button size="small" icon={<PlusOutlined />} disabled={locked || !canAdd(entry)} aria-label={`添加物品 ${entry.itemId}`} onClick={() => add(entry)}>添加</Button>} /></div>}
            {shown.items.length > 0 && <div className="att-list" aria-label="附件列表">{shown.items.map((item, index) => { const error = countError(item);
              return <div className="p-att" key={item.itemId}>
                <ItemIcon itemId={item.itemId} brief={brief(item.itemId)} size="sm" />
                <div className="nm"><ItemName itemId={item.itemId} brief={brief(item.itemId)} />{error && <span className="err small">{error}</span>}</div>
                <InputNumber<number> aria-label={`数量 ${index + 1}`} size="small" min={1} max={kindOf(item.itemId) === 'equipment' ? 10 : MAX} precision={0} disabled={locked}
                  value={item.count} onChange={value => setAttachments(old => old.map(row => row.itemId === item.itemId ? { ...row, count: value ?? 0 } : row))} />
                <Button size="small" type="text" danger aria-label={`移除附件 ${index + 1}`} icon={<CloseOutlined />} disabled={locked}
                  onClick={() => setAttachments(old => old.filter(row => row.itemId !== item.itemId))} />
              </div>; })}</div>}
            <Field label="金币" htmlFor="mail-gold" err={goldError}>
              <InputNumber<number> id="mail-gold" min={0} max={MAX} precision={0} style={{ width: 220, maxWidth: '100%' }} disabled={locked} {...numberFormat}
                value={pending ? pending.body.gold : gold} onChange={value => { setGold(value); setDone(undefined); }} /></Field>
          </div>
        </Panel>
        <Panel title="内容">
          <div className="grid">
            <Field label="标题" htmlFor="mail-title"><Input id="mail-title" disabled={locked} value={shown.title} onChange={event => { setTitle(event.target.value); setDone(undefined); }} /></Field>
            <Field label="正文" htmlFor="mail-body"><Input.TextArea id="mail-body" rows={4} disabled={locked} value={shown.body} onChange={event => { setBody(event.target.value); setDone(undefined); }} /></Field>
          </div>
        </Panel>
      </div>
      <div className="grid" style={{ alignContent: 'start' }}>
        <div className="letter" aria-label="信件预览">
          <div className="to">{recipient ? `→ ${recipient.name}` : '收件人未选'}</div>
          <h3>{shown.title || '（无标题）'}</h3>
          {shown.body.trim() ? <p>{shown.body}</p> : <p className="faint">{DEFAULT_MAIL_BODY}</p>}
          {(shown.items.length > 0 || shown.gold > 0) && <div className="enc">{shown.items.map(item => <span className="chip" key={item.itemId}>{brief(item.itemId).name ?? `物品 ${item.itemId}`} ×{nf(item.count)}</span>)}
            {shown.gold > 0 && <span className="chip accent">金币 {nf(shown.gold)}</span>}</div>}
        </div>
        {perDay > 0 && <div className="quota" aria-label="今日封数"><div className="t"><span>今日</span><span>{nf(me.portal.today.mails)} / {nf(perDay)}</span></div>
          <div className="bar"><i style={{ width: `${Math.min(100, me.portal.today.mails / perDay * 100)}%` }} /></div></div>}
        {tried && missing && !pending && <span className="err" role="alert">{missing}</span>}
        <Button type="primary" size="large" block disabled={locked || !ready} loading={busy && !pending} onClick={trySend}>发送</Button>
        <PendingBanner pending={pending} busy={busy} onRetry={() => pending && send(pending.body)} onDiscard={discard} />
        {done && <Banner title="已送达">{done}</Banner>}
      </div>
    </div>
  </div>;
}
