import { useState } from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { ConfigProvider } from 'antd';
import zhCN from 'antd/locale/zh_CN';
import { beforeEach, expect, it, vi } from 'vitest';
import { api, ApiError } from './api';
import { MailAttachments } from './MailAttachments';
import { MailAttachmentPicker } from './MailAttachmentPicker';
import type { MailItem, MailItemDetails } from './mail';

vi.mock('./api', async original => ({ ...await original<typeof import('./api')>(), api: vi.fn() }));
const mocked = vi.mocked(api);
const material = { itemId: 3037, name: '无色小晶块', category: 'material', equipmentType: null, level: null, rarity: 0 };
const bracelet = { itemId: 100310498, name: '测试手镯', category: 'equipment', equipmentType: 'wrist', level: 80, rarity: 2 };
const materialDetails: MailItemDetails = { item: material, kind: 'stackable', defaultSealed: false, durability: 0, equipment: null };
const equipmentDetails: MailItemDetails = { item: bracelet, kind: 'equipment', defaultSealed: true, durability: 0,
  equipment: { maxReinforcementLevel: 31, maxAmplificationLevel: 20, canAmplify: true, reinforcementReason: null, amplificationReason: null, amplifyValues: { 1: 5, 2: 5, 3: 5, 4: 5 } } };
const change = vi.fn(); const expired = vi.fn(); const denied = vi.fn();
const callbacks = { onExpired: expired, onDenied: denied };
function Harness({ initial = [] }: { initial?: MailItem[] }) {
  const [items, setItems] = useState(initial);
  return <MailAttachments {...callbacks} items={items} locked={false} onChange={value => { change(value); setItems(value); }} />;
}
function open(initial: MailItem[] = []) { render(<ConfigProvider locale={zhCN} theme={{ token: { motion: false } }}><Harness initial={initial} /></ConfigProvider>); }
function picker() { render(<ConfigProvider locale={zhCN} theme={{ token: { motion: false } }}><MailAttachmentPicker {...callbacks} onClose={vi.fn()} onChoose={vi.fn()} /></ConfigProvider>); }
async function choose(id: number) {
  fireEvent.click(screen.getByRole('button', { name: '添加物品附件' }));
  fireEvent.click(await screen.findByRole('button', { name: `选择物品 ${id}` }));
  await screen.findByLabelText('每人数量');
}
async function option(label: string, text: string) {
  fireEvent.mouseDown(screen.getByRole('combobox', { name: label }));
  fireEvent.click(await screen.findByText(text, { selector: '.ant-select-item-option-content' }));
}
beforeEach(() => {
  change.mockClear(); expired.mockClear(); denied.mockClear();
  mocked.mockImplementation(async path => {
    if (path.startsWith('mail/items?')) return { items: [material, bracelet], page: 1, pageSize: 15, hasMore: true };
    if (path === 'mail/items/3037') return materialDetails;
    if (path === 'mail/items/100310498') return equipmentDetails;
    throw new Error('Unexpected ' + path);
  });
});

it('材料重复添加会合并数量，编辑和移除保持同一行', async () => {
  open(); await choose(3037);
  fireEvent.change(screen.getByLabelText('每人数量'), { target: { value: '9' } }); fireEvent.click(screen.getByRole('button', { name: '加入邮件' }));
  await waitFor(() => expect(screen.queryByLabelText('每人数量')).toBeNull());
  await choose(3037); fireEvent.click(screen.getByRole('button', { name: '加入邮件' }));
  expect(change).toHaveBeenLastCalledWith([{ itemId: 3037, count: 10 }]);
  await waitFor(() => expect(screen.queryByLabelText('每人数量')).toBeNull());
  fireEvent.click(screen.getByRole('button', { name: '编辑附件 1' }));
  const quantity = await screen.findByLabelText('每人数量'); expect((quantity as HTMLInputElement).value).toBe('10');
  fireEvent.change(quantity, { target: { value: '6' } }); fireEvent.click(screen.getByRole('button', { name: '保存附件配置' }));
  expect(change).toHaveBeenLastCalledWith([{ itemId: 3037, count: 6 }]);
  await waitFor(() => expect(screen.queryByLabelText('每人数量')).toBeNull());
  fireEvent.click(screen.getByRole('button', { name: '移除附件 1' })); expect(change).toHaveBeenLastCalledWith([]);
});

it('同模板不同强化和增幅配置分别保留，展开超限不能加入', async () => {
  open(); await choose(100310498); await option('封装状态', '解除封装'); await option('成长模式', '强化');
  fireEvent.change(screen.getByLabelText('强化等级'), { target: { value: '7' } }); fireEvent.click(screen.getByRole('button', { name: '加入邮件' }));
  await waitFor(() => expect(screen.queryByLabelText('每人数量')).toBeNull());
  await choose(100310498); await option('成长模式', '增幅'); await option('异次元属性', '智力');
  fireEvent.change(screen.getByLabelText('增幅等级'), { target: { value: '10' } });
  fireEvent.change(screen.getByLabelText('每人数量'), { target: { value: '9' } }); fireEvent.click(screen.getByRole('button', { name: '加入邮件' }));
  expect(change).toHaveBeenLastCalledWith([
    { itemId: 100310498, count: 1, equipment: { seal: 'unsealed', growth: 'reinforcement', level: 7, amplifyType: 0 } },
    { itemId: 100310498, count: 9, equipment: { seal: 'default', growth: 'amplification', level: 10, amplifyType: 4 } },
  ]);
  await waitFor(() => expect(screen.queryByLabelText('每人数量')).toBeNull());
  await choose(100310498); fireEvent.click(screen.getByRole('button', { name: '加入邮件' }));
  expect(await screen.findByText('每人最多 10 个展开物品附件；每件装备单独占一个附件位。')).toBeTruthy();
  expect(change).toHaveBeenCalledTimes(2);
});

it('筛选重置分页，旧搜索响应不能覆盖新筛选结果', async () => {
  let resolveOld!: (value: unknown) => void;
  const delayed = new Promise(resolve => { resolveOld = resolve; }); const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async path => path.includes('page=2') ? delayed : original(path));
  picker(); fireEvent.click(await screen.findByRole('button', { name: '下一页物品' }));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith('mail/items?page=2&pageSize=15'));
  fireEvent.change(screen.getByLabelText('附件名称搜索'), { target: { value: '晶块' } }); fireEvent.click(screen.getByRole('button', { name: '搜索物品' }));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith('mail/items?page=1&pageSize=15&q=%E6%99%B6%E5%9D%97'));
  await act(async () => resolveOld({ items: [{ ...material, name: '迟到的旧页面' }], hasMore: false }));
  expect(screen.queryByText('迟到的旧页面')).toBeNull(); expect(screen.getByText('第 1 页')).toBeTruthy();
});

it('旧选中物品的配置响应不能覆盖新选择', async () => {
  let resolveOld!: (value: unknown) => void;
  const delayed = new Promise(resolve => { resolveOld = resolve; }); const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async path => path === 'mail/items/100310498' ? delayed : original(path));
  picker(); fireEvent.click(await screen.findByRole('button', { name: '选择物品 100310498' }));
  fireEvent.click(screen.getByRole('button', { name: '选择物品 3037' })); await screen.findByLabelText('每人数量');
  await act(async () => resolveOld(equipmentDetails));
  expect(screen.queryByRole('combobox', { name: '成长模式' })).toBeNull();
});

it.each([401, 403])('目录访问失败 %s 触发原有身份处理', async status => {
  mocked.mockRejectedValue(new ApiError(status, '访问失效')); picker();
  await screen.findByText('访问失效'); expect(status === 401 ? expired : denied).toHaveBeenCalled();
});
