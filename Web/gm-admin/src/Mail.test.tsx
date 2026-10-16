import { act, cleanup, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import { api, ApiError } from './api';
import { deferred, logout, renderApp } from './test-utils';
vi.mock('./api', async original => ({ ...await original<typeof import('./api')>(), api: vi.fn() }));
const mocked = vi.mocked(api);
const identity = { id: 1, loginName: '邮件管理员', permissions: ['SendMail'], expiresAt: '2030-01-01T00:00:00Z' };
const empty = { title: '', body: '', recipientIds: [] as number[], items: [] as unknown[], gold: 0 };
const recipient = { characterId: 1, name: '角色甲', nameDecodeStatus: 'valid', nameBytesHex: null, level: 86, job: 1, growType: 2 };
const receipt = (requestId: string, input = { ...empty, body: '测试正文', recipientIds: [1] }) => ({ requestId, administratorId: 1, administratorName: '邮件管理员',
  committedAt: '2026-10-02T00:00:00Z', expiresAt: '2026-11-01T00:00:00Z', input, recipients: [{ character: recipient, accountId: 1, mailId: 37, equipmentUids: [] }] });
const key = 'dfo.gm.mail.pending.1';
const open = (path = '/grants') => renderApp(path);
const sends = () => mocked.mock.calls.filter(([path]) => path === 'mail/send');
async function compose() {
  fireEvent.click(await screen.findByRole('button', { name: /写邮件/ }));
  await screen.findByLabelText('邮件正文');
}
async function recipients() { fireEvent.click(screen.getByRole('button', { name: '下一步：选收件人' })); await screen.findByLabelText('选择角色 1'); }
async function confirmSend() {
  fireEvent.click(screen.getByRole('button', { name: /确认整批发送/ }));
  fireEvent.click(await screen.findByRole('button', { name: /发\s*吧/ }));
}
async function preview() {
  await compose(); fireEvent.change(screen.getByLabelText('邮件正文'), { target: { value: '测试正文' } }); await recipients();
  fireEvent.click(screen.getByLabelText('选择角色 1')); fireEvent.click(screen.getByRole('button', { name: '预览并核对' }));
  await screen.findByRole('button', { name: /确认整批发送/ });
}
const banner = () => screen.findByText(/邮件发送结果未确认/);
beforeEach(() => {
  sessionStorage.clear();
  mocked.mockImplementation(async (path, payload) => {
    if (path === 'auth/status') return { initialized: true };
    if (path === 'auth/session') return identity;
    if (path === 'mail/draft') return { id: '00000000-0000-0000-0000-000000000000', name: '草稿', revision: 0, input: empty };
    if (path === 'mail/templates') return [];
    if (path.startsWith('mail/recipients?')) return { items: [recipient], total: 1, page: 1, pageSize: 20 };
    if (path.startsWith('mail/operations?')) return { items: [], total: 0, page: 1, pageSize: 20 };
    if (path === 'mail/preview') return { input: payload, displayText: (payload as { body: string }).body, utf8Bytes: 6, recipients: [recipient], items: [], totalGold: 0, totalItems: 0 };
    throw new Error('Unexpected path ' + path);
  });
});

it('发放页默认展示记录，收件人只在写邮件的第二步加载', async () => {
  open(); await screen.findByRole('tab', { name: /邮件记录/ });
  expect(screen.queryByLabelText('邮件正文')).toBeNull();
  await compose();
  expect(mocked.mock.calls.some(([path]) => path.startsWith('mail/recipients?'))).toBe(false);
  expect(screen.getByLabelText('游戏内查看邮件窗口预览')).toBeTruthy();
  await recipients(); expect(mocked.mock.calls.some(([path]) => path.startsWith('mail/recipients?'))).toBe(true);
});
it('游戏内预览按服务端规则拼标题与正文，留空正文显示默认文案', async () => {
  open(); await compose();
  const text = () => screen.getByTestId('mail-window-text').textContent;
  expect(text()).toContain('此为官方邮件，无需回复。');
  fireEvent.change(screen.getByLabelText('邮件标题'), { target: { value: '维护补偿' } });
  fireEvent.change(screen.getByLabelText('邮件正文'), { target: { value: '感谢等待' } });
  expect(text()).toContain('维护补偿\n感谢等待');
  fireEvent.change(screen.getByLabelText('邮件标题'), { target: { value: '感谢等待' } });
  expect(text()).not.toContain('感谢等待\n感谢等待');
});
it.each(['', ' \t\n'])('正文选填（场景 %#），保持空草稿并使用后端预览的默认正文发送', async body => {
  const original = mocked.getMockImplementation()!;
  const defaultBody = '此为官方邮件，无需回复。';
  mocked.mockImplementation(async (path, payload, method) => {
    if (path === 'mail/draft' && payload) return { id: '', name: '草稿', revision: 1, input: payload };
    if (path === 'mail/preview') return { input: { ...(payload as typeof empty), body: defaultBody }, displayText: defaultBody, utf8Bytes: 36, recipients: [recipient], items: [], totalGold: 0, totalItems: 0 };
    if (path === 'mail/send') return { receipt: receipt((payload as { requestId: string }).requestId, payload as typeof empty), replayed: false, reminder: 'attempted' };
    return original(path, payload, method);
  });
  open(); await compose();
  const field = screen.getByLabelText('邮件正文') as HTMLTextAreaElement;
  expect(field.placeholder).toBe(defaultBody); expect(field.value).toBe('');
  fireEvent.change(field, { target: { value: body } });
  await waitFor(() => expect((screen.getByRole('button', { name: /保存草稿/ }) as HTMLButtonElement).disabled).toBe(false));
  fireEvent.click(screen.getByRole('button', { name: /保存草稿/ })); await screen.findByText('草稿已保存。');
  expect(mocked).toHaveBeenCalledWith('mail/draft', expect.objectContaining({ body }), 'PUT');
  await recipients(); fireEvent.click(screen.getByLabelText('选择角色 1'));
  fireEvent.click(screen.getByRole('button', { name: '预览并核对' }));
  await screen.findByRole('button', { name: /确认整批发送/ });
  expect(mocked).toHaveBeenCalledWith('mail/preview', expect.objectContaining({ body }));
  await confirmSend(); await screen.findByText('整批邮件已提交。');
  expect(mocked).toHaveBeenCalledWith('mail/send', expect.objectContaining({ body: defaultBody, recipientIds: [1] }));
});
it('确认发送使用预览快照，回到发放页刷新记录，回执独立打开', async () => {
  const original = mocked.getMockImplementation()!; let delivered: ReturnType<typeof receipt> | undefined;
  mocked.mockImplementation(async (path, payload) => {
    if (path === 'mail/send') { delivered = receipt((payload as { requestId: string }).requestId, payload as typeof empty); return { receipt: delivered, replayed: false, reminder: 'attempted' }; }
    if (path.startsWith('mail/operations?')) return { items: delivered ? [delivered] : [], total: delivered ? 1 : 0, page: 1, pageSize: 20 };
    return original(path, payload);
  });
  open(); await preview(); await confirmSend();
  await screen.findByText('整批邮件已提交。');
  await waitFor(() => expect(screen.queryByLabelText('邮件正文')).toBeNull());
  fireEvent.click(await screen.findByRole('button', { name: '回执' }));
  const drawer = await screen.findByRole('dialog', { name: '邮件发送回执' }); expect(within(drawer).getByText('37')).toBeTruthy();
  expect(mocked).toHaveBeenCalledWith('mail/send', expect.objectContaining({ requestId: expect.any(String), body: '测试正文', recipientIds: [1] }));
  expect(sessionStorage.getItem(key)).toBeNull();
});
it('保存草稿使用已加载版本，载入草稿覆盖前先确认', async () => {
  const original = mocked.getMockImplementation()!; let saved = { ...empty, body: '旧草稿', recipientIds: [1] };
  mocked.mockImplementation(async (path, payload) => {
    if (path === 'mail/draft' && payload) { saved = payload as typeof saved; return { id: '', name: '草稿', revision: 8, input: saved }; }
    if (path === 'mail/draft') return { id: '', name: '草稿', revision: 7, input: saved };
    return original(path, payload);
  });
  open(); fireEvent.click(await screen.findByRole('button', { name: '接着写' })); await screen.findByLabelText('邮件正文');
  expect((screen.getByLabelText('邮件正文') as HTMLTextAreaElement).value).toBe('旧草稿');
  fireEvent.change(screen.getByLabelText('邮件正文'), { target: { value: '新草稿' } });
  await waitFor(() => expect((screen.getByRole('button', { name: /保存草稿/ }) as HTMLButtonElement).disabled).toBe(false));
  fireEvent.click(screen.getByRole('button', { name: /保存草稿/ }));
  expect(await screen.findByText('草稿已保存。')).toBeTruthy(); expect(mocked).toHaveBeenCalledWith('mail/draft', expect.objectContaining({ expectedRevision: 7, body: '新草稿' }), 'PUT');
  fireEvent.change(screen.getByLabelText('邮件正文'), { target: { value: '未保存修改' } }); fireEvent.click(screen.getByRole('button', { name: '载入草稿' }));
  await waitFor(() => expect(screen.getAllByRole('button', { name: '载入草稿' }).length).toBe(2)); fireEvent.click(screen.getAllByRole('button', { name: '载入草稿' })[1]);
  await waitFor(() => expect((screen.getByLabelText('邮件正文') as HTMLTextAreaElement).value).toBe('新草稿'));
});
it('丢掉草稿用当前版本写入空内容', async () => {
  const original = mocked.getMockImplementation()!; let draft = { id: '', name: '草稿', revision: 4, input: { ...empty, title: '写了一半' } };
  mocked.mockImplementation(async (path, payload) => {
    if (path === 'mail/draft') { if (payload) draft = { ...draft, revision: 5, input: payload as typeof draft.input }; return draft; }
    return original(path, payload);
  });
  open(); await screen.findByText('邮件草稿还在'); fireEvent.click(screen.getByRole('button', { name: /丢\s*掉/ }));
  await waitFor(() => expect(screen.getAllByRole('button', { name: /^丢\s*掉$/ }).length).toBe(2)); fireEvent.click(screen.getAllByRole('button', { name: /^丢\s*掉$/ })[1]);
  await screen.findByText('草稿已丢弃。');
  expect(mocked).toHaveBeenCalledWith('mail/draft', expect.objectContaining({ expectedRevision: 4, title: '', items: [] }), 'PUT');
  await waitFor(() => expect(screen.queryByText('邮件草稿还在')).toBeNull());
});
it('模板在抽屉里编辑，版本更新后删除携带最新版本，用模板写信不带收件人', async () => {
  const original = mocked.getMockImplementation()!;
  let template = { id: 'template-a', name: '模板甲', revision: 3, input: { ...empty, body: '模板正文', recipientIds: [99] } };
  mocked.mockImplementation(async (path, payload, method) => {
    if (path === 'mail/templates' && !payload) return [template];
    if (path === 'mail/templates/template-a') { if (method === 'PUT') template = { ...template, revision: 4, input: { ...template.input, ...(payload as typeof empty) } }; return template; }
    return original(path, payload);
  });
  open(); fireEvent.click(await screen.findByRole('tab', { name: /邮件模板/ }));
  fireEvent.click(await screen.findByRole('button', { name: /编\s*辑/ }));
  const drawer = await screen.findByRole('dialog', { name: '编辑模板 · 模板甲' });
  expect(within(drawer).queryByLabelText('收件角色搜索')).toBeNull();
  fireEvent.change(within(drawer).getByLabelText('邮件正文'), { target: { value: '新模板正文' } });
  fireEvent.click(within(drawer).getByRole('button', { name: '保存模板' })); await screen.findByText('内容模板已保存，不含收件人。');
  expect(mocked).toHaveBeenCalledWith('mail/templates/template-a', expect.objectContaining({ expectedRevision: 3, body: '新模板正文', recipientIds: [] }), 'PUT');
  await waitFor(() => expect(screen.queryByRole('dialog', { name: '编辑模板 · 模板甲' })).toBeNull());
  await waitFor(() => expect(mocked.mock.calls.filter(([path]) => path === 'mail/templates').length).toBeGreaterThan(1));
  fireEvent.click(screen.getByRole('button', { name: '用它写' })); await screen.findByLabelText('邮件正文');
  expect((screen.getByLabelText('邮件正文') as HTMLTextAreaElement).value).toBe('新模板正文');
  await recipients(); expect((screen.getByLabelText('选择角色 1') as HTMLInputElement).checked).toBe(false);
  fireEvent.click(within(screen.getByRole('heading', { name: '写邮件' }).closest('.ph') as HTMLElement).getByRole('button', { name: /关\s*闭/ }));
  fireEvent.click(await screen.findByRole('button', { name: '不写了' }));
  await screen.findByRole('tab', { name: /邮件模板/ }); fireEvent.click(screen.getByRole('tab', { name: /邮件模板/ }));
  fireEvent.click(await screen.findByRole('button', { name: /^删\s*除$/ })); fireEvent.click((await screen.findAllByRole('button', { name: /^删\s*除$/ })).at(-1)!);
  await screen.findByText('模板已删除。');
  expect(mocked).toHaveBeenCalledWith('mail/templates/template-a', expect.objectContaining({ expectedRevision: 4 }), 'DELETE');
});
it('新建模板从空白开始，不修改已保存草稿', async () => {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async (path, payload) => {
    if (path === 'mail/draft') return { id: '', name: '草稿', revision: 7, input: { ...empty, body: '旧草稿' } };
    if (path === 'mail/templates' && payload) return { id: 'new', name: '新模板', revision: 1, input: payload };
    return original(path, payload);
  });
  open(); fireEvent.click(await screen.findByRole('tab', { name: /邮件模板/ })); fireEvent.click(await screen.findByRole('button', { name: /新建模板/ }));
  const drawer = await screen.findByRole('dialog', { name: '新建模板' });
  expect((within(drawer).getByLabelText('邮件正文') as HTMLTextAreaElement).value).toBe('');
  fireEvent.change(within(drawer).getByLabelText('模板名称'), { target: { value: '新模板' } });
  fireEvent.change(within(drawer).getByLabelText('邮件正文'), { target: { value: '独立模板' } });
  fireEvent.click(within(drawer).getByRole('button', { name: '保存模板' }));
  await screen.findByText('内容模板已保存，不含收件人。');
  expect(mocked).toHaveBeenCalledWith('mail/templates', expect.objectContaining({ name: '新模板', body: '独立模板', recipientIds: [], expectedRevision: 0 }), undefined);
  expect(mocked.mock.calls.some(([path, payload]) => path === 'mail/draft' && !!payload)).toBe(false);
});
it('先写新邮件再收到草稿版本，不覆盖编辑内容且保存使用正确版本', async () => {
  const delayed = deferred<unknown>(); let first = true;
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async (path, payload) => {
    if (path === 'mail/draft') { if (payload) return { id: '', name: '草稿', revision: 8, input: payload }; return first ? { id: '', name: '草稿', revision: 0, input: empty } : delayed.promise; }
    return original(path, payload);
  });
  open(); await screen.findByRole('tab', { name: /邮件记录/ }); first = false; await compose();
  fireEvent.change(screen.getByLabelText('邮件正文'), { target: { value: '新邮件内容' } });
  await act(async () => delayed.resolve({ id: '', name: '草稿', revision: 7, input: { ...empty, body: '旧草稿' } }));
  expect((screen.getByLabelText('邮件正文') as HTMLTextAreaElement).value).toBe('新邮件内容');
  await waitFor(() => expect((screen.getByRole('button', { name: /保存草稿/ }) as HTMLButtonElement).disabled).toBe(false));
  fireEvent.click(screen.getByRole('button', { name: /保存草稿/ })); await screen.findByText('草稿已保存。');
  expect(mocked).toHaveBeenCalledWith('mail/draft', expect.objectContaining({ body: '新邮件内容', expectedRevision: 7 }), 'PUT');
});
it.each([[400, '正文无效'], [409, '版本冲突'], [500, '发送已回滚']])('确定拒绝%s反馈原因并回到编辑', async (status, message) => {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async (path, payload) => { if (path === 'mail/send') throw new ApiError(status, message, status === 500 ? 'rolledBack' : 'rejected'); return original(path, payload); });
  open(); await preview(); await confirmSend();
  expect(await screen.findByText(message)).toBeTruthy(); await screen.findByLabelText('邮件正文');
  expect((screen.getByLabelText('邮件正文') as HTMLTextAreaElement).value).toBe('测试正文');
  expect(sessionStorage.getItem(key)).toBeNull(); expect(screen.queryByText(/件待确认/)).toBeNull();
});
it.each(['unknown', 'timeout'])('结果未知%s保留同一UUID和快照，安全重试不会换编号', async failure => {
  const original = mocked.getMockImplementation()!; let count = 0;
  mocked.mockImplementation(async (path, payload) => {
    if (path === 'mail/send') { if (++count === 1) throw failure === 'unknown' ? new ApiError(500, '提交暂未确认', 'unknown') : new Error('TimeoutError'); return { receipt: receipt((payload as { requestId: string }).requestId), replayed: true, reminder: 'unconfirmed' }; }
    return original(path, payload);
  });
  open(); await preview(); await confirmSend(); await banner();
  const frozen = JSON.parse(sessionStorage.getItem(key)!);
  expect(screen.queryByLabelText('邮件正文')).toBeNull(); expect(screen.getByRole('button', { name: /1 件待确认/ })).toBeTruthy();
  expect((screen.getByRole('button', { name: /写邮件/ }) as HTMLButtonElement).disabled).toBe(true);
  fireEvent.click(screen.getByRole('button', { name: '按原内容安全重试' })); await screen.findByText('已恢复原发送结果，没有重复发放。');
  expect(sends()).toHaveLength(2); expect(sends()[0][1]).toEqual(sends()[1][1]);
  expect((sends()[1][1] as { requestId: string }).requestId).toBe(frozen.requestId);
  expect(await screen.findByRole('dialog', { name: '邮件发送回执' })).toBeTruthy();
});
it('刷新后恢复待确认状态，查询已提交收据只恢复结果，不再次发送', async () => {
  const frozen = { requestId: '7c2af8a7-dfaf-4356-8be5-5db7975bf4c5', input: { ...empty, body: '冻结正文', recipientIds: [1] } };
  sessionStorage.setItem(key, JSON.stringify(frozen)); const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async (path, payload) => path === `mail/operations/${frozen.requestId}` ? receipt(frozen.requestId, frozen.input) : original(path, payload));
  open(); await banner(); fireEvent.click(screen.getByRole('button', { name: '查询原编号结果' }));
  await screen.findByText('已找到持久化发送结果。'); const drawer = await screen.findByRole('dialog', { name: '邮件发送回执' }); expect(within(drawer).getByText('37')).toBeTruthy();
  expect(sessionStorage.getItem(key)).toBeNull(); expect(sends()).toHaveLength(0);
});
it('待确认请求明确拒绝后回到写信页并保留冻结内容', async () => {
  const original = mocked.getMockImplementation()!; let attempts = 0;
  mocked.mockImplementation(async (path, payload) => {
    if (path === 'mail/send') throw ++attempts === 1 ? new ApiError(500, '暂未确认', 'unknown') : new ApiError(400, '内容已拒绝', 'rejected');
    return original(path, payload);
  });
  open(); await preview(); await confirmSend(); await banner();
  fireEvent.click(screen.getByRole('button', { name: '按原内容安全重试' })); await screen.findByText('内容已拒绝');
  expect((await screen.findByLabelText('邮件正文') as HTMLTextAreaElement).value).toBe('测试正文');
  expect(sessionStorage.getItem(key)).toBeNull(); expect(screen.queryByText(/件待确认/)).toBeNull();
  expect(sends()[0][1]).toEqual(sends()[1][1]);
});
it('收件人跨页选择保留明确ID并可取消已不在当前页的角色', async () => {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async (path, payload) => path.startsWith('mail/recipients?') ? { items: [path.includes('page=2&') ? { ...recipient, characterId: 21, name: '角色乙' } : recipient], total: 21, page: path.includes('page=2&') ? 2 : 1, pageSize: 20 } : original(path, payload));
  open(); await compose(); fireEvent.change(screen.getByLabelText('邮件正文'), { target: { value: '正文' } }); await recipients(); fireEvent.click(screen.getByLabelText('选择角色 1'));
  fireEvent.click(screen.getByTitle('2'));
  fireEvent.click(await screen.findByLabelText('选择角色 21'));
  const tray = screen.getByRole('button', { name: '取消角色 1' }).closest('.tray') as HTMLElement;
  expect(within(tray).getByText('角色甲')).toBeTruthy(); expect(within(tray).getByText('角色乙')).toBeTruthy();
  fireEvent.click(screen.getByRole('button', { name: '取消角色 1' }));
  expect(screen.queryByRole('button', { name: '取消角色 1' })).toBeNull(); expect(screen.getByRole('button', { name: '取消角色 21' })).toBeTruthy();
});
it('刷新恢复待确认请求但不会自动发放；404不当作已回滚', async () => {
  const frozen = { requestId: '7c2af8a7-dfaf-4356-8be5-5db7975bf4c5', input: { ...empty, body: '冻结正文', recipientIds: [1] } };
  sessionStorage.setItem(key, JSON.stringify(frozen));
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async (path, payload) => { if (path === `mail/operations/${frozen.requestId}`) throw new ApiError(404, '未找到已提交记录，请保留原编号。'); return original(path, payload); });
  open(); await banner(); expect(sends()).toHaveLength(0);
  fireEvent.click(screen.getByRole('button', { name: '查询原编号结果' })); expect(await screen.findByText('未找到已提交记录，请保留原编号。')).toBeTruthy();
  expect(screen.getByRole('button', { name: '按原内容安全重试' })).toBeTruthy(); expect(sessionStorage.getItem(key)).not.toBeNull();
});
it.each([401, 403])('邮件发送身份失效%s清除私有界面', async status => {
  const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async (path, payload) => { if (path === 'mail/send') throw new ApiError(status, '身份或权限失效'); return original(path, payload); });
  open(); await preview(); await confirmSend();
  if (status === 401) await screen.findByLabelText('管理员账号'); else await screen.findByText('没有访问此页面的权限');
  expect(screen.queryByLabelText('邮件正文')).toBeNull(); expect(screen.queryByText('角色甲')).toBeNull();
});
it('退出登录后迟到的预览响应不能恢复写信界面', async () => {
  const pending = deferred<unknown>(); const original = mocked.getMockImplementation()!;
  mocked.mockImplementation(async (path, payload) => { if (path === 'mail/preview') return pending.promise; if (path === 'auth/logout') return undefined; return original(path, payload); });
  open(); await compose(); fireEvent.change(screen.getByLabelText('邮件正文'), { target: { value: '测试正文' } }); await recipients(); fireEvent.click(screen.getByLabelText('选择角色 1'));
  fireEvent.click(screen.getByRole('button', { name: '预览并核对' })); await logout(); await screen.findByLabelText('管理员账号');
  await act(async () => pending.resolve({ input: { ...empty, body: '测试正文', recipientIds: [1] }, displayText: '测试正文', utf8Bytes: 12, recipients: [recipient], items: [], totalGold: 0, totalItems: 0 }));
  expect(screen.queryByText('确认整批发送')).toBeNull(); expect(screen.queryByLabelText('邮件正文')).toBeNull();
});
it('附件与金币进入预览，收件人查询使用邮件窄投影并编码字面搜索', async () => {
  const original = mocked.getMockImplementation()!;
  const material = { itemId: 3037, name: '无色小晶块', category: 'material', equipmentType: null, level: null, rarity: 1 };
  mocked.mockImplementation(async (path, payload) => {
    if (path.startsWith('mail/items?')) return { items: [material], page: 1, pageSize: 30, hasMore: false };
    if (path === 'mail/items/3037') return { item: material, kind: 'stackable', defaultSealed: false, durability: 0, equipment: null };
    return original(path, payload);
  });
  open(); await compose(); fireEvent.click(screen.getByRole('button', { name: /添加物品/ }));
  fireEvent.click(await screen.findByRole('button', { name: '添加物品 3037' }));
  fireEvent.change(await screen.findByLabelText('数量 1'), { target: { value: '9' } });
  fireEvent.click(screen.getByRole('button', { name: '加入邮件（1 项）' }));
  await waitFor(() => expect(screen.queryByLabelText('数量 1')).toBeNull());
  expect((screen.getByLabelText('附件 1 数量') as HTMLInputElement).value).toBe('9');
  fireEvent.change(screen.getByLabelText('每人金币'), { target: { value: '1000' } });
  fireEvent.change(screen.getByLabelText('邮件正文'), { target: { value: '领取测试' } }); await recipients(); fireEvent.click(screen.getByLabelText('选择角色 1'));
  fireEvent.change(screen.getByLabelText('收件角色搜索'), { target: { value: '中文%_' } }); fireEvent.click(screen.getByRole('button', { name: /查\s*找/ }));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith('mail/recipients?page=1&pageSize=20&q=%E4%B8%AD%E6%96%87%25_'));
  fireEvent.change(screen.getByLabelText('收件角色搜索'), { target: { value: '21' } }); fireEvent.click(screen.getByRole('button', { name: /查\s*找/ }));
  await waitFor(() => expect(mocked).toHaveBeenCalledWith('mail/recipients?page=1&pageSize=20&characterId=21'));
  fireEvent.click(screen.getByRole('button', { name: '预览并核对' })); await screen.findByRole('button', { name: /确认整批发送/ });
  expect(mocked).toHaveBeenCalledWith('mail/preview', expect.objectContaining({ items: [{ itemId: 3037, count: 9 }], gold: 1000, recipientIds: [1] }));
});
it.each([401, 403])('未知发送的失权重试%s保留原UUID，换管理员看不到，恢复身份后仍按同一内容恢复结果', async status => {
  const original = mocked.getMockImplementation()!; let attempts = 0; let currentIdentity = identity;
  mocked.mockImplementation(async (path, payload) => {
    if (path === 'auth/session') return currentIdentity;
    if (path === 'mail/send') {
      attempts++;
      if (attempts === 1) throw new Error('response lost after possible commit');
      if (attempts === 2) throw new ApiError(status, '身份或权限失效');
      return { receipt: receipt((payload as { requestId: string }).requestId), replayed: true, reminder: 'unconfirmed' };
    }
    return original(path, payload);
  });
  open(); await preview(); await confirmSend(); await banner();
  const frozen = sessionStorage.getItem(key); expect(frozen).not.toBeNull();
  fireEvent.click(screen.getByRole('button', { name: '按原内容安全重试' }));
  if (status === 401) await screen.findByLabelText('管理员账号'); else await screen.findByText('没有访问此页面的权限');
  expect(screen.queryByLabelText('邮件正文')).toBeNull(); expect(screen.queryByText(/邮件发送结果未确认/)).toBeNull();
  expect(sessionStorage.getItem(key)).toBe(frozen);
  currentIdentity = { ...identity, id: 2, loginName: '另一管理员' };
  cleanup(); open(); await screen.findByRole('tab', { name: /邮件记录/ });
  expect(screen.queryByText(/邮件发送结果未确认/)).toBeNull(); expect(sessionStorage.getItem(key)).toBe(frozen);
  currentIdentity = identity;
  cleanup(); open(); await banner();
  expect(sends()).toHaveLength(2);
  fireEvent.click(screen.getByRole('button', { name: '按原内容安全重试' }));
  await screen.findByText('已恢复原发送结果，没有重复发放。');
  expect(sends()).toHaveLength(3); expect(sends()[1][1]).toEqual(sends()[0][1]); expect(sends()[2][1]).toEqual(sends()[0][1]);
  expect(sessionStorage.getItem(key)).toBeNull();
});
