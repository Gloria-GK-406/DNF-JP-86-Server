import { useId, useState } from 'react';
import { Button, InputNumber, Select, Space, Tag, Typography } from 'antd';
import { amplifyTypes, defaultEquipment, equipmentTypes, itemCategories, itemRarities, type ItemCatalogDetails, type ItemEquipmentOptions, type ItemInput } from './items';

export type ItemConfigurationValue = { count: number | null; options: ItemEquipmentOptions };
export const initialItemConfiguration = (initial?: ItemInput): ItemConfigurationValue => ({
  count: initial?.count ?? 1, options: { ...(initial?.equipment ?? defaultEquipment()) },
});

export function configuredItem(details: ItemCatalogDetails, value: ItemConfigurationValue, simple = false): ItemInput | undefined {
  const { count, options } = value;
  const capabilities = details.equipment;
  const equipment = details.kind === 'equipment';
  const maximum = options.growth === 'amplification' ? capabilities?.maxAmplificationLevel ?? 0 : capabilities?.maxReinforcementLevel ?? 0;
  if (count === null || !Number.isInteger(count) || count <= 0 || count > (equipment && !simple ? 10 : 2147483647)
    || equipment && !simple && options.growth !== 'none' && (!Number.isInteger(options.level) || options.level < 0 || options.level > maximum
      || (options.growth === 'reinforcement' ? maximum === 0 || options.level < 1 : !capabilities?.canAmplify || !amplifyTypes[options.amplifyType]))) return undefined;
  return { itemId: details.item.itemId, count, ...(equipment && !simple && (options.seal !== 'default' || options.growth !== 'none')
    ? { equipment: { ...options } } : {}) };
}

export function ItemConfigurationFields({ details, value, onChange, simple = false, refill = false, expanded = true }: {
  details: ItemCatalogDetails; value: ItemConfigurationValue; onChange: (value: ItemConfigurationValue) => void;
  simple?: boolean; refill?: boolean; expanded?: boolean;
}) {
  const id = useId();
  const { count, options } = value;
  const capabilities = details.equipment;
  const equipment = details.kind === 'equipment';
  const maximum = options.growth === 'amplification' ? capabilities?.maxAmplificationLevel ?? 0 : capabilities?.maxReinforcementLevel ?? 0;
  function updateOptions(next: ItemEquipmentOptions) { onChange({ ...value, options: next }); }
  return <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
    <div><Typography.Text strong>{details.item.name}</Typography.Text><div className="mail-item-id">ID {details.item.itemId}</div></div>
    <Space wrap><Tag>{itemCategories[details.item.category]}</Tag>{details.item.equipmentType && <Tag>{equipmentTypes[details.item.equipmentType] ?? details.item.equipmentType}</Tag>}
      {details.item.level !== null && <Tag>Lv. {details.item.level}</Tag>}{details.item.rarity !== null && <Tag>{itemRarities[details.item.rarity]}</Tag>}</Space>
    <div><label htmlFor={`${id}-count`}>{refill ? '补给数量' : simple ? '每角色数量' : '每人数量'}</label>
      <InputNumber id={`${id}-count`} style={{ width: '100%', marginTop: 6 }} min={1} max={equipment && !simple ? 10 : 2147483647}
        precision={0} value={count} onChange={next => onChange({ ...value, count: next })} /></div>
    {equipment && !simple && <div hidden={!expanded} className="mail-equipment-fields">
      <div><label htmlFor={`${id}-seal`}>封装状态</label><Select id={`${id}-seal`} aria-label="封装状态" style={{ width: '100%', marginTop: 6 }} value={options.seal}
        options={[{ value: 'default', label: `模板默认（${details.defaultSealed ? '封装' : '未封装'}）` }, { value: 'unsealed', label: '解除封装' }]}
        onChange={seal => updateOptions({ ...options, seal })} /></div>
      <div><label htmlFor={`${id}-growth`}>成长模式</label><Select id={`${id}-growth`} aria-label="成长模式" style={{ width: '100%', marginTop: 6 }} value={options.growth}
        options={[{ value: 'none', label: '无强化 / 增幅' }, { value: 'reinforcement', label: '强化', disabled: !capabilities?.maxReinforcementLevel },
          { value: 'amplification', label: '增幅', disabled: !capabilities?.canAmplify }]}
        onChange={growth => updateOptions({ ...options, growth, level: growth === 'reinforcement' ? 1 : 0, amplifyType: growth === 'amplification' ? 3 : 0 })} /></div>
      {capabilities?.reinforcementReason && <Typography.Text type="secondary">{capabilities.reinforcementReason}</Typography.Text>}
      {capabilities?.amplificationReason && <Typography.Text type="secondary">{capabilities.amplificationReason}</Typography.Text>}
      {options.growth !== 'none' && <div><label htmlFor={`${id}-level`}>{options.growth === 'reinforcement' ? '强化等级' : '增幅等级'}</label>
        <InputNumber id={`${id}-level`} style={{ width: '100%', marginTop: 6 }} min={options.growth === 'reinforcement' ? 1 : 0} max={maximum} precision={0}
          value={options.level} onChange={level => updateOptions({ ...options, level: level ?? 0 })} /><Typography.Text type="secondary">此装备最高 +{maximum}</Typography.Text></div>}
      {options.growth === 'amplification' && <div><label htmlFor={`${id}-amplify`}>异次元属性</label>
        <Select id={`${id}-amplify`} aria-label="异次元属性" style={{ width: '100%', marginTop: 6 }} value={options.amplifyType}
          options={[3, 4, 1, 2].map(amplifyType => ({ value: amplifyType, label: amplifyTypes[amplifyType] }))}
          onChange={amplifyType => updateOptions({ ...options, amplifyType })} />
        <Typography.Text type="secondary">基础异次元属性值 {capabilities?.amplifyValues[options.amplifyType]}，由服务端按模板规则生成。</Typography.Text></div>}
      <Typography.Text type="secondary">每件装备占一个附件位，具有独立身份。</Typography.Text>
    </div>}
  </Space>;
}

export function ItemConfiguration({ details, initial, onChoose, simple = false, refill = false }: {
  details: ItemCatalogDetails; initial?: ItemInput; onChoose: (item: ItemInput) => void; simple?: boolean; refill?: boolean;
}) {
  const [value, setValue] = useState(() => initialItemConfiguration(initial));
  const item = configuredItem(details, value, simple);
  return <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
    <ItemConfigurationFields details={details} value={value} onChange={setValue} simple={simple} refill={refill} />
    <Button type="primary" block disabled={!item} onClick={() => { if (item) onChoose(item); }}>
      {refill ? '选择此补给物品' : simple ? (initial ? '保存物品配置' : '加入初始物品') : (initial ? '保存附件配置' : '加入邮件')}
    </Button>
  </Space>;
}
