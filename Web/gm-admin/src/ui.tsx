import { Fragment, useState, type ReactNode } from 'react';
import { Button, Tooltip } from 'antd';
import { DownOutlined, InfoCircleOutlined, LockOutlined, UpOutlined } from '@ant-design/icons';

/* Shared console building blocks. Class names map to style.css. */
export function BrandMark({ className }: { className?: string }) {
  return <svg viewBox="0 0 32 32" aria-hidden="true" className={className}>
    <rect width="32" height="32" rx="7" fill="var(--rail-accent)" />
    <path d="M8 27V18a8 8 0 0 1 16 0v9" fill="none" stroke="var(--rail)" strokeWidth="2.2" strokeLinecap="round" />
    <path d="M15 12.6h2V22l-1 2.4-1-2.4z" fill="var(--rail)" />
    <rect x="12.4" y="10.6" width="7.2" height="2" rx=".8" fill="var(--rail)" />
    <rect x="15.25" y="7.4" width="1.5" height="3.2" fill="var(--rail)" />
    <circle cx="16" cy="6.4" r="1.4" fill="var(--s4)" stroke="var(--rail)" strokeWidth=".6" />
  </svg>;
}
export function PageHead({ title, info, children }: { title: string; info?: ReactNode; children?: ReactNode }) {
  return <div className="ph"><h1>{title}</h1>{info && <Tooltip title={info}><span className="info-i" aria-label="说明"><InfoCircleOutlined /></span></Tooltip>}
    <div className="tools">{children}</div></div>;
}
export function Panel({ title, count, tools, flush, children, foot, label }: {
  title: ReactNode; count?: ReactNode; tools?: ReactNode; flush?: boolean; children?: ReactNode; foot?: ReactNode; label?: string;
}) {
  return <section className="panel" aria-label={label ?? (typeof title === 'string' ? title : undefined)}><div className="panel-head"><h2>{title}</h2>{count !== undefined && count !== null && <span className="count">{count}</span>}
    {tools && <div className="tools">{tools}</div>}</div><div className={flush ? '' : 'panel-body'}>{children}</div>{foot && <div className="panel-foot">{foot}</div>}</section>;
}
export type PillKind = 'ok' | 'attn' | 'crit' | 'idle';
export function Pill({ kind = 'ok', children }: { kind?: PillKind; children: ReactNode }) { return <span className={`pill ${kind}`}><i />{children}</span>; }
export function Banner({ kind = 'info', icon, title, children, actions }: { kind?: 'info' | 'attn' | 'crit'; icon?: ReactNode; title: ReactNode; children?: ReactNode; actions?: ReactNode }) {
  return <div className={`banner ${kind}`} role={kind === 'crit' ? 'alert' : 'status'}>{icon && <span className="b-icon">{icon}</span>}
    <div className="b-text"><b>{title}</b>{children && <span>{children}</span>}</div>{actions && <div className="b-actions">{actions}</div>}</div>;
}
export function Field({ label, htmlFor, hint, err, children }: { label: ReactNode; htmlFor?: string; hint?: ReactNode; err?: ReactNode; children: ReactNode }) {
  return <div className="field">{htmlFor ? <label htmlFor={htmlFor}>{label}</label> : <span className="label">{label}</span>}{children}
    {err ? <span className="err">{err}</span> : hint && <span className="hint">{hint}</span>}</div>;
}
export function Steps({ labels, step, extra }: { labels: string[]; step: number; extra?: (ReactNode | null)[] }) {
  return <div className="steps" aria-label="步骤">{labels.map((text, index) => <Fragment key={text}>{index > 0 && <span className="sep" />}
    <span className={`s ${index === step ? 'cur' : index < step ? 'done' : ''}`} aria-current={index === step ? 'step' : undefined}>
      <i>{index < step ? '✓' : index + 1}</i>{text}{extra?.[index] && <span className="mono faint" style={{ fontSize: 11 }}>{extra[index]}</span>}</span></Fragment>)}</div>;
}
/** Config pages lock writes after a conflict or an unconfirmed result until the operator re-reads the live state. */
export function LockBanner({ onReload, busy, what = '配置', label = '重新读取' }: { onReload: () => void; busy?: boolean; what?: string; label?: string }) {
  return <Banner kind="crit" icon={<LockOutlined />} title={`请先重新读取生效${what}`} actions={<Button size="small" danger loading={busy} onClick={onReload}>{label}</Button>}>
    版本冲突或者结果没确认。为免覆盖别人的改动，写入已锁定。</Banner>;
}
export function Denied({ title = '没有访问此页面的权限', children }: { title?: string; children?: ReactNode }) {
  return <div className="denied" role="status"><b>{title}</b><span>{children ?? '当前页面不可访问。需要权限时请联系服务端管理员。'}</span></div>;
}
export function KV({ rows, flat = true }: { rows: [ReactNode, ReactNode][]; flat?: boolean }) {
  return <dl className={`kv ${flat ? 'flat' : ''}`}>{rows.map(([key, value], index) => <Fragment key={index}><dt>{key}</dt><dd>{value}</dd></Fragment>)}</dl>;
}
/** Expanded / collapsed state of a home-page block, remembered in this browser. */
export function useExpanded(key: string): [boolean, () => void] {
  const storageKey = `dfo.gm.expand.${key}`;
  const [expanded, setExpanded] = useState(() => { try { return localStorage.getItem(storageKey) === '1'; } catch { return false; } });
  return [expanded, () => setExpanded(old => { try { localStorage.setItem(storageKey, old ? '0' : '1'); } catch { /* per-browser convenience */ } return !old; })];
}
/** Footer that reveals the rows beyond the first few; renders nothing when everything already fits. */
export function MoreToggle({ hidden, expanded, onToggle, summary }: { hidden: number; expanded: boolean; onToggle: () => void; summary?: ReactNode }) {
  if (hidden <= 0) return null;
  return <button className="more-toggle" aria-expanded={expanded} onClick={onToggle}>
    {expanded ? <><UpOutlined /> 收起</> : <><DownOutlined /> 展开{summary ? <> · {summary}</> : ` 其余 ${hidden} 项`}</>}</button>;
}
