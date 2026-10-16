import { useState } from 'react';
import { Button, InputNumber, Tooltip } from 'antd';
import { DeleteOutlined, ToolOutlined } from '@ant-design/icons';
import { EquipModal } from './EquipEditor';
import { briefOf, ItemIcon, ItemName } from './itemInfo';
import { equipKey, equipText, equipmentTypes, itemCategories, itemRarities, type ItemCatalogDetails, type ItemEquipmentOptions, type ItemInput } from './items';

/* Attachment rows shared by mail / template (equipment configurable) and initialization (count only). */
export function AttachmentRows({ items, details, failed, onRetry, onChange, equipConfig = true, perLabel = '/人', empty, locked = false }: {
  items: ItemInput[]; details: Record<number, ItemCatalogDetails | undefined>; failed?: Record<number, string>; onRetry?: () => void;
  onChange: (items: ItemInput[]) => void; equipConfig?: boolean; perLabel?: string; empty: string; locked?: boolean;
}) {
  const [editing, setEditing] = useState<number | null>(null);
  if (items.length === 0) return <div className="empty-line">{empty}</div>;
  function saveEquip(index: number, equipment: ItemEquipmentOptions | null) {
    const row = items[index]; const key = equipKey(equipment);
    const twin = items.findIndex((other, i) => i !== index && other.itemId === row.itemId && equipKey(other.equipment) === key);
    if (twin >= 0) onChange(items.flatMap((other, i) => i === index ? [] : i === twin ? [{ ...other, count: Math.min(2147483647, other.count + row.count) }] : [other]));
    else onChange(items.map((other, i) => i === index ? { itemId: other.itemId, count: other.count, ...(equipment ? { equipment } : {}) } : other));
  }
  return <>{items.map((item, index) => { const loaded = details[item.itemId]; const eq = loaded?.kind === 'equipment'; const state = equipText(item.equipment);
    const brief = briefOf(loaded); const entry = loaded?.item;
    return <div className="att" key={`${index}:${item.itemId}:${equipKey(item.equipment)}`} aria-label={`附件 ${index + 1}`}>
      <ItemIcon itemId={item.itemId} brief={brief} equip={eq ? item.equipment : null} capabilities={loaded?.equipment} />
      <div className="info">
        <ItemName itemId={item.itemId} brief={brief} />
        <span className="meta">{!loaded ? (failed?.[item.itemId] ? <>规则读取失败 {onRetry && <button className="linkbtn" onClick={onRetry}>重试</button>}</> : '正在读取物品规则…') : <>
          {itemCategories[entry!.category] ?? entry!.category}{entry!.equipmentType ? ` · ${equipmentTypes[entry!.equipmentType] ?? entry!.equipmentType}` : ''}{entry!.level !== null ? ` · Lv.${entry!.level}` : ''}
          {entry!.rarity !== null && <> · <span className={`r${entry!.rarity}`}>{itemRarities[entry!.rarity]}</span></>}
          {eq && equipConfig && <> · <span className={state ? 'state-on' : ''}>{state || '默认状态（最上级）'}</span> · 每件占 1 格</>}</>}</span>
      </div>
      <div className="att-side">
        {eq && equipConfig && <Button size="small" icon={<ToolOutlined />} disabled={locked} aria-label={`配置附件 ${index + 1}`} onClick={() => setEditing(index)}>配置</Button>}
        <InputNumber size="small" aria-label={`附件 ${index + 1} 数量`} min={1} max={eq && equipConfig ? 10 : 2147483647} precision={0} value={item.count} disabled={locked}
          onChange={value => onChange(items.map((other, i) => i === index ? { ...other, count: value ?? 1 } : other))} suffix={perLabel} style={{ width: 124 }} />
        <Tooltip title="移除"><Button size="small" type="text" danger disabled={locked} aria-label={`移除附件 ${index + 1}`} icon={<DeleteOutlined />} onClick={() => onChange(items.filter((_, i) => i !== index))} /></Tooltip>
      </div>
    </div>; })}
    {editing !== null && items[editing] && details[items[editing].itemId] && <EquipModal details={details[items[editing].itemId]!} value={items[editing].equipment}
      onClose={() => setEditing(null)} onSave={equipment => saveEquip(editing, equipment)} />}</>;
}
