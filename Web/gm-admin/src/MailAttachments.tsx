import { useCallback, useEffect, useState } from 'react';
import { Button, Empty, Space, Tag, Typography } from 'antd';
import { MailAttachmentPicker } from './MailAttachmentPicker';
import { equipmentSummary, itemCategories, mergeMailItem, type MailItem, type MailItemDetails } from './mail';
import { useGmRead } from './useGmRead';
import { NotiHint, useNotiBar } from './NotiBar';

type AccessCallbacks = { onExpired: () => void; onDenied: () => void };

export function MailAttachments({ items, onChange, locked, onExpired, onDenied }: AccessCallbacks & {
  items: MailItem[]; onChange: (items: MailItem[]) => void; locked: boolean;
}) {
  const noti = useNotiBar();
  const [editing, setEditing] = useState<number | 'new' | null>(null);
  const [known, setKnown] = useState<Record<number, MailItemDetails>>({});
  const remember = useCallback((details: MailItemDetails) => setKnown(old => old[details.item.itemId] === details ? old : { ...old, [details.item.itemId]: details }), []);
  const expanded = (values: MailItem[], metadata: Record<number, MailItemDetails>) => values.reduce((total, item) => total + (metadata[item.itemId]?.kind === 'equipment' ? item.count : 1), 0);
  const allKnown = items.every(item => known[item.itemId]);
  return <section className="mail-attachments" aria-label="邮件物品附件">
    <div className="mail-attachments-heading"><div><Typography.Text strong>物品附件</Typography.Text>
      <div><Typography.Text type="secondary">{items.length === 0 ? '可按名称、ID 和分类查找物品' : allKnown ? `每人占用 ${expanded(items, known)} / 10 个附件位` : '正在核对附件占用…'}</Typography.Text></div></div>
      <Button disabled={locked || items.length >= 10} onClick={() => setEditing('new')}>添加物品附件</Button></div>
    {items.length === 0 && <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="尚未添加物品附件" />}
    {items.map((item, index) => <AttachmentRow key={`${index}:${item.itemId}`} item={item} index={index} known={known[item.itemId]} remember={remember}
      onExpired={onExpired} onDenied={onDenied} actions={<Space>
        <Button size="small" disabled={locked} onClick={() => setEditing(index)}>编辑附件 {index + 1}</Button>
        <Button size="small" danger disabled={locked} onClick={() => onChange(items.filter((_, row) => row !== index))}>移除附件 {index + 1}</Button>
      </Space>} />)}
    {allKnown && expanded(items, known) > 10 && <NotiHint type="error" noticeKey="attachment-capacity" title="每人最多 10 个展开物品附件，请减少装备数量。" />}
    {editing !== null && !locked && <MailAttachmentPicker initial={editing === 'new' ? undefined : items[editing]}
      onExpired={onExpired} onDenied={onDenied} onClose={() => setEditing(null)} onChoose={(item, details) => {
        try {
          const updated = mergeMailItem(items, item, editing === 'new' ? undefined : editing);
          const metadata = { ...known, [item.itemId]: details };
          if (updated.length > 10 || updated.every(row => metadata[row.itemId]) && expanded(updated, metadata) > 10)
            return '每人最多 10 个展开物品附件；每件装备单独占一个附件位。';
          remember(details); onChange(updated); setEditing(null); noti.success(editing === 'new' ? '物品附件已添加。' : '附件配置已保存。');
          return undefined;
        } catch (failure) { return (failure as Error).message; }
      }} />}
  </section>;
}

function AttachmentRow({ item, index, known, remember, actions, onExpired, onDenied }: AccessCallbacks & {
  item: MailItem; index: number; known?: MailItemDetails; remember?: (details: MailItemDetails) => void; actions?: React.ReactNode;
}) {
  const loaded = useGmRead<MailItemDetails>(known ? null : `mail/items/${item.itemId}`, onExpired, onDenied);
  const details = known ?? loaded.data;
  useEffect(() => { if (loaded.data) remember?.(loaded.data); }, [loaded.data, remember]);
  return <div className="mail-attachment-row">
    <div className="mail-attachment-number">{index + 1}</div><div className="mail-attachment-description">
      <Space wrap><Typography.Text strong>{details?.item.name ?? `物品 ${item.itemId}`}</Typography.Text><Tag>× {item.count} / 人</Tag>
        {details && <Tag>{itemCategories[details.item.category]}</Tag>}</Space>
      <div className="mail-item-id">ID {item.itemId}{details?.kind === 'equipment' || item.equipment ? ` · ${equipmentSummary(item.equipment)}` : ''}</div>
    </div>{actions}
  </div>;
}

export function MailAttachmentSummary({ items, onExpired, onDenied }: AccessCallbacks & { items: MailItem[] }) {
  return <>{items.map((item, index) => <AttachmentRow key={`${index}:${item.itemId}`} item={item} index={index} onExpired={onExpired} onDenied={onDenied} />)}</>;
}
