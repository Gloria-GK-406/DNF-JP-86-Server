import { useState } from 'react';
import { Button, Card, Empty, Modal, Spin, Typography } from 'antd';
import { useGmRead } from './useGmRead';
import { useNotiBar } from './NotiBar';
import { ItemCatalogSearch, type ItemAccessCallbacks, type ItemPickerPurpose } from './ItemCatalogSearch';
import { ItemConfiguration } from './ItemConfiguration';
import type { ItemInput, ItemCatalogDetails } from './items';

export function ItemPicker({ initial, onClose, onChoose, onExpired, onDenied, purpose }: ItemAccessCallbacks & {
  purpose: ItemPickerPurpose; initial?: ItemInput; onClose: () => void; onChoose: (item: ItemInput, details: ItemCatalogDetails) => string | undefined;
}) {
  const [selected, setSelected] = useState<number | undefined>(initial?.itemId);
  const noti = useNotiBar();
  const details = useGmRead<ItemCatalogDetails>(selected ? `${purpose}/items/${selected}` : null, onExpired, onDenied);
  return <Modal title={purpose === 'daily-refill' ? '选择每日补给物品' : purpose === 'initialization' ? (initial ? '编辑初始物品' : '添加初始物品') : (initial ? '编辑物品附件' : '添加物品附件')} open onCancel={onClose} footer={null}
    width={1160} className="mail-picker-modal" destroyOnHidden>
    <Typography.Paragraph type="secondary">{purpose === 'daily-refill' ? '选择每日补给的永久普通背包堆叠物品。' : purpose === 'initialization' ? '选择额外赠送的永久普通背包物品，装备使用模板默认状态。' : '从当前游戏物品目录检索，选择物品后配置数量与装备状态。仅显示邮件支持发放的永久物品。'}</Typography.Paragraph>
    <ItemCatalogSearch purpose={purpose} selectedIds={selected ? [selected] : []} onSelect={item => setSelected(item.itemId)}
      onExpired={onExpired} onDenied={onDenied} configuration={
      <Card size="small" title={purpose === 'daily-refill' ? '选择补给数量' : purpose === 'initialization' ? '配置初始物品' : '配置附件'} className="mail-picker-config">
        {!selected && <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="先从左侧选择一个物品" />}
        {details.busy && <div className="mail-picker-loading"><Spin /><Typography.Paragraph>正在读取物品规则…</Typography.Paragraph></div>}
        {details.error && <Button onClick={details.refresh}>重试</Button>}
        {details.data && <ItemConfiguration key={details.data.item.itemId} details={details.data} simple={purpose !== 'mail'} refill={purpose === 'daily-refill'}
          initial={initial?.itemId === details.data.item.itemId ? initial : undefined}
          onChoose={item => { const refusal = onChoose(item, details.data!); if (refusal) noti.error(refusal); }} />}
      </Card>
    } />
  </Modal>;
}
