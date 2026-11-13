import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Button, InputNumber, Segmented, Table } from 'antd';
import { jobName, nf, numberFormat } from './format';
import { PendingBanner, usePlayer, useSubmission } from './PlayerApp';
import type { PlayerCeraResult, PlayerCharacter, PlayerCurrency } from './playerApi';
import { Banner, Field, Panel, Pill } from './ui';

const MAX = 2147483647;
const currencyNames: Record<PlayerCurrency, string> = { Cera: '点券', TokenCera: '代币券' };
const quick = [[10000, '+1 万'], [100000, '+10 万'], [1000000, '+100 万']] as const;
type CeraBody = { currency: PlayerCurrency; amount: number };

export function JobTile({ job }: { job: number }) { return <span className="job" aria-hidden="true">{jobName(job).slice(0, 1)}</span>; }

export function PlayerAccountView() {
  const { me, setBalances, refreshMe } = usePlayer(); const navigate = useNavigate();
  const { pending, busy, submit, discard } = useSubmission<CeraBody, PlayerCeraResult>('cera/grant');
  const [currency, setCurrency] = useState<PlayerCurrency>('Cera');
  const [amount, setAmount] = useState<number | null>(null);
  const [done, setDone] = useState<string>();
  if (!me) return null;
  const { portal, balances } = me;
  const shownCurrency = pending?.body.currency ?? currency; const shownAmount = pending?.body.amount ?? amount;
  const balance = shownCurrency === 'Cera' ? balances.cera : balances.tokenCera;
  const perGrant = shownCurrency === 'Cera' ? portal.limits.ceraPerGrant : portal.limits.tokenCeraPerGrant;
  const perDay = shownCurrency === 'Cera' ? portal.limits.ceraPerDay : portal.limits.tokenCeraPerDay;
  const used = shownCurrency === 'Cera' ? portal.today.cera : portal.today.tokenCera;
  const error = shownAmount === null ? undefined : shownAmount > MAX ? '数量超出范围'
    : perGrant > 0 && shownAmount > perGrant ? `单次上限 ${nf(perGrant)}` : perDay > 0 && used + shownAmount > perDay ? `今日剩余 ${nf(Math.max(0, perDay - used))}` : undefined;
  const locked = !portal.ceraEnabled || !!pending || busy;
  function grant(body: CeraBody) {
    setDone(undefined);
    void submit(body, result => {
      setDone(`${currencyNames[result.receipt.currency ?? body.currency]} +${nf(result.receipt.amount ?? body.amount)}`);
      if (result.receipt.after) setBalances(result.receipt.after);
      setAmount(null); void refreshMe();
    });
  }
  return <div className="page">
    <div className="grid g-main-side">
      <div className="grid">
        <section className="panel wallet" aria-label="余额">
          <div><span className="k">点券</span><span className="v accent">{nf(balances.cera)}</span></div>
          <div><span className="k">代币券</span><span className="v">{nf(balances.tokenCera)}</span></div>
        </section>
        <Panel title="角色" count={me.characters.length} flush>
          <div className="tbl-wrap"><Table<PlayerCharacter> size="small" rowKey="characterId" dataSource={me.characters} pagination={false} locale={{ emptyText: '无角色' }} columns={[
            { title: '角色', key: 'name', render: (_, c) => <span className="char-cell"><JobTile job={c.job} /><b>{c.name}</b></span> },
            { title: '职业', key: 'job', className: 'col-job', render: (_, c) => <span className="dim">{jobName(c.job)}</span> },
            { title: '等级', dataIndex: 'level', align: 'right', className: 'col-lv', render: v => <span className="mono">{v}</span> },
            { title: '状态', key: 'online', render: (_, c) => c.isOnline ? <Pill kind="ok">在线</Pill> : <Pill kind="idle">离线</Pill> },
            { title: '', key: 'mail', align: 'right', render: (_, c) => <Button size="small" disabled={!portal.mailEnabled} aria-label={`给 ${c.name} 发邮件`}
              onClick={() => navigate('/player/mail', { state: { characterId: c.characterId } })}>发邮件</Button> },
          ]} /></div>
        </Panel>
      </div>
      <Panel title="加点券" tools={!portal.ceraEnabled && <Pill kind="idle">已停用</Pill>} foot={<><span className="grow" />
        <Button type="primary" disabled={locked || shownAmount === null || shownAmount < 1 || !!error} loading={busy && !pending}
          onClick={() => amount !== null && grant({ currency, amount })}>确认</Button></>}>
        <div className="grid">
          <Segmented block aria-label="币种" disabled={locked} value={shownCurrency} onChange={value => { setCurrency(value as PlayerCurrency); setDone(undefined); }}
            options={[{ label: '点券', value: 'Cera' }, { label: '代币券', value: 'TokenCera' }]} />
          <Field label="数量" htmlFor="cera-amount" err={error}>
            <InputNumber<number> id="cera-amount" size="large" min={1} max={MAX} precision={0} style={{ width: '100%' }} disabled={locked} {...numberFormat}
              value={shownAmount} onChange={value => { setAmount(value); setDone(undefined); }} /></Field>
          <div className="quick">{quick.map(([step, label]) => <Button key={step} size="small" disabled={locked}
            onClick={() => { setAmount(Math.min(MAX, (amount ?? 0) + step)); setDone(undefined); }}>{label}</Button>)}</div>
          <div className="after"><span>加后</span><b aria-label="加后">{shownAmount ? nf(balance + shownAmount) : '—'}</b></div>
          {perDay > 0 && <div className="quota" aria-label="今日用量"><div className="t"><span>今日</span><span>{nf(used)} / {nf(perDay)}</span></div>
            <div className="bar"><i style={{ width: `${Math.min(100, used / perDay * 100)}%` }} /></div></div>}
          <PendingBanner pending={pending} busy={busy} onRetry={() => pending && grant(pending.body)} onDiscard={discard} />
          {done && <Banner title={done} />}
        </div>
      </Panel>
    </div>
  </div>;
}
