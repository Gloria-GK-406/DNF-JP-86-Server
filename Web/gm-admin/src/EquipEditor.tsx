import { useState } from 'react';
import { Button, Checkbox, InputNumber, Modal, Segmented } from 'antd';
import { amplifyTypes, equipOptionsOf, equipStateOf, qualityTiers, type EquipState, type ItemCatalogDetails, type ItemEquipmentOptions } from './items';
import { briefOf, ItemIcon } from './itemInfo';

/* Rules (server is authoritative, these mirror it for instant feedback):
   reinforcement and amplification share the level bits, so they are exclusive; an amplified piece is amplify-only.
   Forging is weapon-only and stacks with either. Sockets open the equipment type's empty emblem slots.
   Quality picks a tier; the server writes that tier's fixed seed so the client shows the tier maximum. */
export function equipLimits(details: ItemCatalogDetails) {
  const caps = details.equipment;
  return { maxR: caps?.maxReinforcementLevel ?? 0, maxA: caps?.canAmplify ? caps.maxAmplificationLevel : 0, canAmp: !!caps?.canAmplify,
    weapon: details.item.equipmentType === 'weapon', maxForge: caps?.maxForgeLevel ?? 0, sockets: caps?.socketCount ?? 0, socketClass: caps?.socketClass ?? null };
}
export function equipError(details: ItemCatalogDetails, state: EquipState) {
  const limits = equipLimits(details);
  if (state.growth === 'reinforcement' && !(Number.isInteger(state.level) && state.level >= 1 && state.level <= limits.maxR)) return `强化等级要在 +1 到 +${limits.maxR} 之间`;
  if (state.growth === 'amplification') {
    if (!limits.canAmp) return '这件不能增幅';
    if (!(Number.isInteger(state.level) && state.level >= 0 && state.level <= limits.maxA)) return `增幅等级要在 +0 到 +${limits.maxA} 之间`;
    if (!amplifyTypes[state.amplifyType]) return '请选择异次元属性';
  }
  if (state.forge && !(limits.weapon && Number.isInteger(state.forge) && state.forge >= 0 && state.forge <= limits.maxForge)) return limits.weapon ? `锻造要在 +0 到 +${limits.maxForge} 之间` : '只有武器能锻造';
  if (state.socket && limits.sockets === 0) return '这个部位没有徽章孔';
  if (!qualityTiers.some(tier => tier.value === state.quality)) return '请选择品级';
  return '';
}
export function EquipEditor({ details, value, onChange, idPrefix }: { details: ItemCatalogDetails; value: EquipState; onChange: (value: EquipState) => void; idPrefix: string }) {
  const limits = equipLimits(details); const caps = details.equipment;
  const tier = qualityTiers.find(q => q.value === value.quality) ?? qualityTiers[4];
  const max = value.growth === 'amplification' ? limits.maxA : limits.maxR;
  return <div className="equip-editor">
    <div className="ee-row"><span className="ee-label" id={`${idPrefix}-quality`}>品级</span>
      <Segmented size="small" aria-labelledby={`${idPrefix}-quality`} value={tier.value} onChange={v => onChange({ ...value, quality: Number(v) })} options={qualityTiers.map(q => ({ value: q.value, label: q.label }))} /></div>
    <span className="ee-hint">属性取该档上限 {tier.pct}%（服务端写入该档固定种子，客户端不走随机）</span>
    <div className="ee-row"><span className="ee-label" id={`${idPrefix}-growth`}>成长</span>
      <Segmented size="small" aria-labelledby={`${idPrefix}-growth`} value={value.growth} onChange={v => {
        const growth = v as EquipState['growth'];
        onChange({ ...value, growth, level: growth === 'none' ? 0 : growth === 'reinforcement' ? Math.min(Math.max(1, value.level), limits.maxR) : Math.min(value.level, limits.maxA),
          amplifyType: growth === 'amplification' ? (amplifyTypes[value.amplifyType] ? value.amplifyType : 3) : 0 });
      }} options={[{ label: '原样', value: 'none' }, { label: '强化', value: 'reinforcement', disabled: !limits.maxR }, { label: '增幅', value: 'amplification', disabled: !limits.canAmp }]} /></div>
    {value.growth !== 'none' && <div className="ee-row"><label className="ee-label" htmlFor={`${idPrefix}-level`}>等级</label>
      <InputNumber id={`${idPrefix}-level`} size="small" prefix="+" min={value.growth === 'reinforcement' ? 1 : 0} max={max} precision={0} value={value.level}
        onChange={v => onChange({ ...value, level: v ?? 0 })} style={{ width: 90 }} /><span className="faint small">最高 +{max}</span></div>}
    {value.growth === 'amplification' && <div className="ee-row"><span className="ee-label" id={`${idPrefix}-amp`}>异次元</span>
      <Segmented size="small" aria-labelledby={`${idPrefix}-amp`} value={value.amplifyType} onChange={v => onChange({ ...value, amplifyType: Number(v) })}
        options={[3, 4, 1, 2].map(v => ({ value: v, label: amplifyTypes[v] }))} />
      {caps?.amplifyValues[value.amplifyType] !== undefined && <span className="faint small">基础值 {caps.amplifyValues[value.amplifyType]}</span>}</div>}
    {(limits.maxR > 0 || limits.canAmp) && <span className="ee-hint">强化和增幅共用一个等级，只能二选一；有异次元属性的装备只能增幅。</span>}
    {(caps?.reinforcementReason || caps?.amplificationReason) && <span className="ee-hint">{caps.reinforcementReason ?? caps.amplificationReason}</span>}
    <div className="ee-row"><label className="ee-label" htmlFor={`${idPrefix}-forge`}>锻造</label>
      {limits.weapon && limits.maxForge > 0 ? <><InputNumber id={`${idPrefix}-forge`} size="small" prefix="+" min={0} max={limits.maxForge} precision={0} value={value.forge}
        onChange={v => onChange({ ...value, forge: v ?? 0 })} style={{ width: 90 }} /><span className="faint small">0 为不锻造，最高 +{limits.maxForge}；可以和强化或增幅同时存在</span></>
        : <span className="faint small">{limits.weapon ? '服务端没有提供锻造上限，暂不可锻造' : '只有武器能锻造'}</span>}</div>
    <div className="ee-row"><span className="ee-label">开孔</span>
      {limits.sockets > 0 ? <Checkbox checked={value.socket} onChange={event => onChange({ ...value, socket: event.target.checked })}>开 {limits.sockets} 个{limits.socketClass ? ` ${limits.socketClass} 类` : ''}孔<span className="faint small">（空孔，不预镶徽章）</span></Checkbox>
        : <span className="faint small">{limits.weapon ? '武器没有徽章孔' : '这个部位没有徽章孔'}</span>}</div>
    <div className="ee-row"><span className="ee-label">封装</span><Checkbox checked={value.seal === 'unsealed'} onChange={event => onChange({ ...value, seal: event.target.checked ? 'unsealed' : 'default' })}>
      解除封装<span className="faint small">（模板默认{details.defaultSealed ? '封装' : '不封装'}）</span></Checkbox></div>
    {details.defaultSealed && value.seal !== 'unsealed' && (value.growth !== 'none' || value.forge > 0) && <span className="ee-hint warn">玩家正常流程要先解封才能强化 / 增幅；保留封装会生成“封装且已强化”的状态。</span>}
  </div>;
}
/** Small window behind the attachment row's 配置 button. */
export function EquipModal({ details, value, onClose, onSave }: { details: ItemCatalogDetails; value?: ItemEquipmentOptions | null; onClose: () => void; onSave: (value: ItemEquipmentOptions | null) => void }) {
  const [state, setState] = useState(() => equipStateOf(value));
  const error = equipError(details, state);
  return <Modal open width={500} centered onCancel={onClose} mask={{ closable: false }}
    title={<span style={{ display: 'flex', gap: 8, alignItems: 'center' }}><ItemIcon itemId={details.item.itemId} brief={briefOf(details)} equip={equipOptionsOf(state)} capabilities={details.equipment} size="sm" />配置 · {details.item.name}</span>}
    footer={<div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end', alignItems: 'center' }}>{error && <span className="err small" style={{ marginInlineEnd: 'auto' }}>{error}</span>}
      <Button onClick={onClose}>取消</Button><Button type="primary" disabled={!!error} onClick={() => { onSave(equipOptionsOf(state)); onClose(); }}>保存配置</Button></div>}>
    <div style={{ paddingBlock: 6 }}><EquipEditor details={details} value={state} onChange={setState} idPrefix="equip-modal" /></div>
  </Modal>;
}
