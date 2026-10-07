import { useState } from 'react';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { App, ConfigProvider } from 'antd';
import { beforeEach, expect, it, vi } from 'vitest';
import { api } from './api';
import { MailAttachments } from './MailAttachments';
import type { ItemCatalogDetails, ItemInput } from './items';

vi.mock('./api', async original => ({ ...await original<typeof import('./api')>(), api: vi.fn() }));
const mocked = vi.mocked(api);
const crystal: ItemCatalogDetails = { item: { itemId: 3037, name: '无色小晶块', category: 'material', equipmentType: null, level: null, rarity: 0 }, kind: 'stackable', defaultSealed: false, durability: 0, equipment: null };
const ticket: ItemCatalogDetails = { ...crystal, item: { ...crystal.item, itemId: 3330, name: '深渊派对邀请函' } };
const weapon: ItemCatalogDetails = { item: { itemId: 100, name: '测试武器', category: 'equipment', equipmentType: 'weapon', level: 80, rarity: 2 }, kind: 'equipment', defaultSealed: true, durability: 1,
  equipment: { maxReinforcementLevel: 31, maxAmplificationLevel: 20, canAmplify: true, reinforcementReason: null, amplificationReason: null, amplifyValues: { 1: 5, 2: 5, 3: 5, 4: 5 } } };
const metadata = [crystal, ticket, weapon];
const change = vi.fn();
const callbacks = { onExpired: vi.fn(), onDenied: vi.fn() };
function Harness({ initial = [] }: { initial?: ItemInput[] }) {
  const [items, setItems] = useState(initial);
  return <MailAttachments {...callbacks} items={items} locked={false} onChange={value => { change(value); setItems(value); }} />;
}
function open(initial?: ItemInput[]) {
  render(<ConfigProvider theme={{ token: { motion: false } }}><App><Harness initial={initial} /></App></ConfigProvider>);
  fireEvent.click(screen.getByRole('button', { name: '添加物品附件' }));
}
async function add(id: number, index: number) {
  fireEvent.click(await screen.findByRole('button', { name: `添加物品 ${id}` }));
  const card = within(screen.getByLabelText(`待加入附件 ${index}`));
  await card.findByLabelText('每人数量');
  return card;
}
beforeEach(() => {
  change.mockClear();
  mocked.mockImplementation(async path => {
    if (path.startsWith('mail/items?')) return { items: metadata.map(row => row.item), page: 1, pageSize: 15, hasMore: true };
    const details = metadata.find(row => path === `mail/items/${row.item.itemId}`);
    if (details) return details;
    throw new Error(`Unexpected ${path}`);
  });
});

it('连续配置多张卡片只在统一提交时更新邮件，翻页搜索保留数量', async () => {
  open(); const first = await add(3037, 1);
  fireEvent.change(first.getByLabelText('每人数量'), { target: { value: '9' } });
  const second = await add(3330, 2);
  fireEvent.change(second.getByLabelText('每人数量'), { target: { value: '20' } });
  fireEvent.click(screen.getByRole('button', { name: '下一页物品' })); await screen.findByText('第 2 页');
  fireEvent.change(screen.getByLabelText('附件名称搜索'), { target: { value: '邀请函' } });
  fireEvent.click(screen.getByRole('button', { name: '搜索物品' })); await screen.findByText('第 1 页');
  expect((first.getByLabelText('每人数量') as HTMLInputElement).value).toBe('9');
  expect((second.getByLabelText('每人数量') as HTMLInputElement).value).toBe('20');
  expect(change).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole('button', { name: '加入邮件（2项）' }));
  expect(change).toHaveBeenCalledExactlyOnceWith([{ itemId: 3037, count: 9 }, { itemId: 3330, count: 20 }]);
  await waitFor(() => expect(screen.queryByLabelText('待加入的附件')).toBeNull());
});

it('移除卡片不会让剩余卡片串值，取消丢弃本次选择且不改已有邮件', async () => {
  open([{ itemId: 3037, count: 5 }]); await add(3037, 1); const second = await add(3330, 2);
  fireEvent.change(second.getByLabelText('每人数量'), { target: { value: '20' } });
  fireEvent.click(screen.getByRole('button', { name: '移除待加入附件 1' }));
  expect((within(screen.getByLabelText('待加入附件 1')).getByLabelText('每人数量') as HTMLInputElement).value).toBe('20');
  fireEvent.click(screen.getByRole('button', { name: '取消批量添加' }));
  expect(change).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole('button', { name: '添加物品附件' }));
  expect(screen.queryByLabelText('待加入附件 1')).toBeNull();
});

it('计入已有附件容量，超限不部分加入，满位仍能合并已有堆叠物', async () => {
  open([{ itemId: 100, count: 9 }, { itemId: 3037, count: 5 }]);
  await add(3037, 1);
  expect(await screen.findByText('加入后每人占用 10 / 10 个附件位')).toBeTruthy();
  await add(3330, 2);
  expect(await screen.findByText('每人最多 10 个展开物品附件；每件装备单独占一个附件位。')).toBeTruthy();
  expect((screen.getByRole('button', { name: '加入邮件（2项）' }) as HTMLButtonElement).disabled).toBe(true);
  expect(change).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole('button', { name: '移除待加入附件 2' }));
  fireEvent.click(screen.getByRole('button', { name: '加入邮件（1项）' }));
  expect(change).toHaveBeenCalledExactlyOnceWith([{ itemId: 100, count: 9 }, { itemId: 3037, count: 6 }]);
});

it('规则未加载和数量为空时阻止整批加入', async () => {
  let resolve!: (value: ItemCatalogDetails) => void;
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async path => path === 'mail/items/3330' ? new Promise<ItemCatalogDetails>(done => { resolve = done; }) : original(path));
  open(); const first = await add(3037, 1);
  fireEvent.click(screen.getByRole('button', { name: '添加物品 3330' }));
  expect((screen.getByRole('button', { name: '加入邮件（2项）' }) as HTMLButtonElement).disabled).toBe(true);
  expect(change).not.toHaveBeenCalled();
  await act(async () => resolve(ticket));
  fireEvent.change(first.getByLabelText('每人数量'), { target: { value: '' } });
  expect((screen.getByRole('button', { name: '加入邮件（2项）' }) as HTMLButtonElement).disabled).toBe(true);
  fireEvent.change(first.getByLabelText('每人数量'), { target: { value: '1' } });
  fireEvent.click(screen.getByRole('button', { name: '加入邮件（2项）' }));
  expect(change).toHaveBeenCalledTimes(1);
});

it('单张卡片读取失败后可重试，其他配置不丢失', async () => {
  const original = mocked.getMockImplementation()!; let failed = false;
  mocked.mockImplementation(async path => {
    if (path === 'mail/items/3330' && !failed) { failed = true; throw new Error('读取失败'); }
    return original(path);
  });
  open(); const first = await add(3037, 1);
  fireEvent.change(first.getByLabelText('每人数量'), { target: { value: '9' } });
  fireEvent.click(screen.getByRole('button', { name: '添加物品 3330' }));
  fireEvent.click(await screen.findByRole('button', { name: '重试读取规则' }));
  await within(screen.getByLabelText('待加入附件 2')).findByLabelText('每人数量');
  fireEvent.click(screen.getByRole('button', { name: '加入邮件（2项）' }));
  expect(change).toHaveBeenCalledExactlyOnceWith([{ itemId: 3037, count: 9 }, { itemId: 3330, count: 1 }]);
});
