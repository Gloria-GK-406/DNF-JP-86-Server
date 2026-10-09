import { useCallback, useEffect, useRef, useState } from 'react';
import { Button, Card, Empty, Modal, Space, Spin, Typography } from 'antd';
import { ItemCatalogSearch, type ItemAccessCallbacks } from './ItemCatalogSearch';
import { configuredItem, initialItemConfiguration, ItemConfigurationFields, type ItemConfigurationValue } from './ItemConfiguration';
import { equipmentSummary, type ItemCatalogDetails, type ItemCatalogEntry, type ItemInput } from './items';
import { mergeMailItem } from './mail';
import { NotiHint, useNotiBar } from './NotiBar';
import { useGmRead } from './useGmRead';

type Draft = { key: number; item: ItemCatalogEntry; value: ItemConfigurationValue };
type Metadata = Record<number, ItemCatalogDetails>;
export const expandedMailAttachments = (items: ItemInput[], known: Metadata) =>
  items.reduce((total, item) => total + (known[item.itemId]?.kind === 'equipment' ? item.count : 1), 0);

export function MailAttachmentPicker({ existing, known, onClose, onChoose, onExpired, onDenied }: ItemAccessCallbacks & {
  existing: ItemInput[]; known: Metadata; onClose: () => void;
  onChoose: (items: ItemInput[], details: Metadata) => string | undefined;
}) {
  const [drafts, setDrafts] = useState<Draft[]>([]);
  const [loaded, setLoaded] = useState<Metadata>({});
  const nextKey = useRef(0);
  const noti = useNotiBar();
  const metadata = { ...known, ...loaded };
  const remember = useCallback((details: ItemCatalogDetails) => setLoaded(old => old[details.item.itemId] === details
    ? old : { ...old, [details.item.itemId]: details }), []);
  const inputs = drafts.map(draft => metadata[draft.item.itemId] && configuredItem(metadata[draft.item.itemId], draft.value));
  const ready = drafts.length > 0 && inputs.every((item): item is ItemInput => !!item)
    && existing.every(item => !!metadata[item.itemId]);
  let merged: ItemInput[] = [];
  let refusal: string | undefined;
  if (ready) {
    try {
      merged = (inputs as ItemInput[]).reduce((items, item) => mergeMailItem(items, item), existing);
      if (merged.length > 10 || expandedMailAttachments(merged, metadata) > 10)
        refusal = '每人最多 10 个展开物品附件；每件装备单独占一个附件位。';
    } catch (failure) { refusal = (failure as Error).message; }
  }
  function add(item: ItemCatalogEntry) {
    const key = nextKey.current++;
    setDrafts(old => [...old, { key, item, value: initialItemConfiguration() }]);
  }
  return <Modal title="添加物品附件" open width={1280} className="mail-picker-modal mail-batch-picker" onCancel={onClose}
    mask={{ closable: false }} footer={<div className="mail-batch-footer">
      <div><Typography.Text strong>已选 {drafts.length} 项附件</Typography.Text>
        <div><Typography.Text type="secondary">{ready ? `加入后每人占用 ${expandedMailAttachments(merged, metadata)} / 10 个附件位`
          : drafts.length ? '正在核对物品规则，请填写有效数量与装备配置' : '从左侧添加物品，可一次配置多个附件'}</Typography.Text></div></div>
      <Space><Button aria-label="取消批量添加" onClick={onClose}>取消</Button><Button type="primary" disabled={!ready || !!refusal} onClick={() => {
        if (!ready || refusal) return;
        const error = onChoose(inputs as ItemInput[], metadata);
        if (error) noti.error(error);
      }}>加入邮件（{drafts.length}项）</Button></Space>
    </div>}>
    <Typography.Paragraph type="secondary">连续添加物品，在右侧分别配置数量和装备状态，再统一加入邮件。相同物品与配置会合并数量。</Typography.Paragraph>
    <ItemCatalogSearch purpose="mail" multiple selectedIds={drafts.map(draft => draft.item.itemId)} onSelect={add}
      onExpired={onExpired} onDenied={onDenied} configuration={<section className="mail-batch-config" aria-label="待加入的附件">
        <Typography.Text strong>配置附件（{drafts.length}）</Typography.Text>
        <div className="mail-batch-cards">
          {!drafts.length && <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="从左侧添加物品" />}
          {drafts.map((draft, index) => <DraftCard key={draft.key} draft={draft} index={index} details={metadata[draft.item.itemId]}
            remember={remember} onExpired={onExpired} onDenied={onDenied}
            onChange={value => setDrafts(old => old.map(row => row.key === draft.key ? { ...row, value } : row))}
            onRemove={() => setDrafts(old => old.filter(row => row.key !== draft.key))} />)}
        </div>
      </section>} />
    {refusal && <NotiHint type="error" noticeKey="mail-batch-capacity" title={refusal} />}
  </Modal>;
}

function DraftCard({ draft, index, details, remember, onChange, onRemove, onExpired, onDenied }: ItemAccessCallbacks & {
  draft: Draft; index: number; details?: ItemCatalogDetails; remember: (details: ItemCatalogDetails) => void;
  onChange: (value: ItemConfigurationValue) => void; onRemove: () => void;
}) {
  const read = useGmRead<ItemCatalogDetails>(details ? null : `mail/items/${draft.item.itemId}`, onExpired, onDenied);
  const loaded = details ?? read.data;
  const [expanded, setExpanded] = useState(false);
  useEffect(() => { if (read.data) remember(read.data); }, [read.data, remember]);
  return <Card size="small" className="mail-batch-card" title={`附件 ${index + 1}`} aria-label={`待加入附件 ${index + 1}`}
    extra={<Button size="small" type="text" danger aria-label={`移除待加入附件 ${index + 1}`} onClick={onRemove}>移除</Button>}>
    {!loaded && <><Typography.Text strong>{draft.item.name}</Typography.Text><div className="mail-item-id">ID {draft.item.itemId}</div>
      {read.busy && <Spin size="small" />}{read.error && <Button onClick={read.refresh}>重试读取规则</Button>}</>}
    {loaded && <>
      <ItemConfigurationFields details={loaded} value={draft.value} onChange={onChange} expanded={expanded} />
      {loaded.kind === 'equipment' && <div className="mail-batch-equipment-summary">
        <Typography.Text type="secondary">{equipmentSummary(draft.value.options)}</Typography.Text>
        <Button size="small" aria-label={`配置装备附件 ${index + 1}`} aria-expanded={expanded} onClick={() => setExpanded(!expanded)}>
          {expanded ? '收起装备配置' : '配置装备'}</Button>
      </div>}
    </>}
  </Card>;
}
