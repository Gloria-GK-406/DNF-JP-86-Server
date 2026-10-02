import { useState } from 'react';
import { Alert, Button, Card, Checkbox, Descriptions, Drawer, Form, Input, InputNumber, Radio, Space, Spin, Table, Tag, Typography } from 'antd';
import type { AccountView, Balances, CharacterDetail, CharacterView, PlayerPage } from './api';
import { SampleTime } from './OverviewView';
import { useGmRead } from './useGmRead';

type Kind = 'accounts' | 'characters';
type Target = { kind: Kind; id: number };
function Online({ value }: { value: boolean }) { return <Tag color={value ? 'green' : undefined}>{value ? '在线' : '离线'}</Tag>; }
function Position({ value }: { value: CharacterView['position'] }) {
  if (value.source === 'unavailable') return <>当前位置不可用</>;
  return <>{value.source === 'current' ? '当前位置' : '保存位置'}：城镇 {value.townId} / 区域 {value.areaId}，({value.x}, {value.y})，方向 {value.direction}</>;
}
function Wallet({ value }: { value: Balances }) {
  return <Space wrap><span>点券：{value.cera}</span><span>代币点券：{value.tokenCera}</span><span>欢乐代币点券：{value.happyTokenCera}</span></Space>;
}

export function PlayerDirectoryView({ onExpired, onDenied }: { onExpired: () => void; onDenied: () => void }) {
  const [kind, setKind] = useState<Kind>('characters');
  const [page, setPage] = useState(1); const [pageSize, setPageSize] = useState(20);
  const [criteria, setCriteria] = useState<Record<string, string>>({});
  const [target, setTarget] = useState<Target | null>(null);
  const [form] = Form.useForm();
  const parameters = new URLSearchParams({ page: String(page), pageSize: String(pageSize), ...criteria });
  const query = useGmRead<PlayerPage<AccountView | CharacterView>>(`${kind}?${parameters}`, onExpired, onDenied);
  function search(values: { q?: string; id?: number; accountId?: number; onlineOnly?: boolean }) {
    const next: Record<string, string> = {};
    if (values.q?.trim()) next.q = values.q.trim();
    if (values.id) next[kind === 'accounts' ? 'accountId' : 'characterId'] = String(values.id);
    if (kind === 'characters' && values.accountId) next.accountId = String(values.accountId);
    if (values.onlineOnly) next.onlineOnly = 'true';
    setCriteria(next); setPage(1); setTarget(null);
  }
  function switchKind(value: Kind) { setKind(value); setCriteria({}); setPage(1); setTarget(null); form.resetFields(); }
  function relatedCharacters(accountId: number) {
    setKind('characters'); setCriteria({ accountId: String(accountId) }); setPage(1); setTarget(null);
    form.resetFields(); form.setFieldsValue({ accountId });
  }
  const common = [
    { title: '在线状态', key: 'online', render: (_: unknown, row: AccountView | CharacterView) => <Online value={row.isOnline} /> },
    { title: '详情', key: 'detail', render: (_: unknown, row: AccountView | CharacterView) => <Button type="link" onClick={() => setTarget({ kind, id: 'characterId' in row ? row.characterId : row.accountId })}>查看详情</Button> },
  ];
  const columns = kind === 'accounts' ? [
    { title: '账号 ID', dataIndex: 'accountId' }, { title: '登录名', dataIndex: 'loginName' },
    { title: '角色数', dataIndex: 'characterCount' }, { title: '账号余额', key: 'balances', render: (_: unknown, row: AccountView | CharacterView) => 'balances' in row ? <Wallet value={row.balances} /> : null },
    ...common,
  ] : [
    { title: '角色 ID', dataIndex: 'characterId' }, { title: '角色姓名', dataIndex: 'name' },
    { title: '归属账号', key: 'account', render: (_: unknown, row: AccountView | CharacterView) => <Button type="link" onClick={() => setTarget({ kind: 'accounts', id: row.accountId })}>{row.loginName}（{row.accountId}）</Button> },
    { title: '职业 / 成长型', key: 'career', render: (_: unknown, row: AccountView | CharacterView) => 'job' in row ? `${row.job} / ${row.growType}` : '' },
    { title: '等级', dataIndex: 'level' }, { title: '记录状态', key: 'deleted', render: (_: unknown, row: AccountView | CharacterView) => <Tag>{'isDeleted' in row && row.isDeleted ? '已删除' : '正常'}</Tag> },
    { title: '当前频道', key: 'channel', render: (_: unknown, row: AccountView | CharacterView) => 'channel' in row && row.isOnline ? row.channel?.name ?? '未知频道' : '无当前频道' },
    { title: '位置', key: 'position', render: (_: unknown, row: AccountView | CharacterView) => 'position' in row ? <Position value={row.position} /> : null }, ...common,
  ];
  return <Space vertical style={{ width: '100%' }}>
    <Radio.Group aria-label="查询对象" value={kind} onChange={event => switchKind(event.target.value)} options={[{ label: '角色', value: 'characters' }, { label: '账号', value: 'accounts' }]} optionType="button" />
    <Card><Form form={form} layout="inline" onFinish={search}>
      <Form.Item name="q" label={kind === 'characters' ? '角色姓名搜索' : '账号登录名搜索'} rules={[{ max: 128, message: '搜索最多 128 个字符' }]}><Input maxLength={128} /></Form.Item>
      <Form.Item name="id" label={kind === 'characters' ? '角色 ID' : '账号 ID'} rules={[{ type: 'integer', min: 1, max: 2147483647, message: '请输入有效正整数 ID' }]}><InputNumber precision={0} /></Form.Item>
      {kind === 'characters' && <Form.Item name="accountId" label="归属账号 ID" rules={[{ type: 'integer', min: 1, max: 2147483647, message: '请输入有效正整数 ID' }]}><InputNumber precision={0} /></Form.Item>}
      <Form.Item name="onlineOnly" valuePropName="checked"><Checkbox>仅在线</Checkbox></Form.Item>
      <Space><Button type="primary" htmlType="submit">查询</Button><Button onClick={() => { form.resetFields(); search({}); }}>重置</Button></Space>
    </Form></Card>
    <Space><Button onClick={query.refresh} loading={query.busy}>刷新列表</Button><SampleTime value={query.data?.sampledAt} /></Space>
    {query.error && <Alert type="error" title={query.error} showIcon />}
    <Table rowKey={kind === 'characters' ? 'characterId' : 'accountId'} dataSource={query.data?.items ?? []} loading={query.busy} columns={columns} scroll={{ x: 1400 }}
      locale={{ emptyText: `没有符合条件的${kind === 'characters' ? '角色' : '账号'}` }}
      pagination={{ current: page, pageSize, total: query.data?.total ?? 0, showSizeChanger: true, showTotal: total => `共 ${total} 条`, onChange: (next, size) => { setPage(size === pageSize ? next : 1); setPageSize(size); setTarget(null); } }} />
    {target && <PlayerDetail key={`${target.kind}:${target.id}`} target={target} onClose={() => setTarget(null)} onExpired={onExpired} onDenied={onDenied} onAccount={id => setTarget({ kind: 'accounts', id })} onCharacters={relatedCharacters} onCharacter={id => setTarget({ kind: 'characters', id })} />}
  </Space>;
}

function PlayerDetail({ target, onClose, onExpired, onDenied, onAccount, onCharacters, onCharacter }: {
  target: Target; onClose: () => void; onExpired: () => void; onDenied: () => void; onAccount: (id: number) => void; onCharacters: (id: number) => void; onCharacter: (id: number) => void;
}) {
  const query = useGmRead<AccountView | CharacterDetail>(`${target.kind}/${target.id}`, onExpired, onDenied);
  const value = query.data;
  return <Drawer title={target.kind === 'accounts' ? '账号详情' : '角色详情'} open onClose={onClose} size="large">
    <Space vertical style={{ width: '100%' }}><Button onClick={query.refresh} loading={query.busy}>刷新详情</Button>
      {query.error && <Alert type="error" title={query.error} showIcon />}
      {query.busy && <Spin tip="正在查询详情"><div style={{ height: 80 }} /></Spin>}
      {value && <><SampleTime value={value.sampledAt} /><Descriptions column={1} bordered items={[
        { key: 'accountId', label: '账号 ID', children: value.accountId }, { key: 'loginName', label: '登录名', children: value.loginName },
        { key: 'wallet', label: '账号余额', children: <Wallet value={value.balances} /> },
        { key: 'online', label: '在线状态', children: <Online value={value.isOnline} /> },
        ...('characterId' in value ? [
          { key: 'characterId', label: '角色 ID', children: value.characterId }, { key: 'name', label: '角色姓名', children: value.name },
          { key: 'decode', label: '姓名诊断', children: value.nameDecodeStatus === 'valid' ? '有效 UTF-8' : value.nameDecodeStatus === 'missing' ? '姓名字节缺失' : `编码异常：${value.nameBytesHex}` },
          { key: 'career', label: '职业 / 成长型 / 等级', children: `${value.job} / ${value.growType} / ${value.level}` },
          { key: 'deleted', label: '记录状态', children: value.isDeleted ? '已删除' : '正常' },
          { key: 'channel', label: '当前频道', children: value.isOnline ? value.channel?.name ?? '未知频道' : '无当前频道' },
          { key: 'position', label: '位置来源', children: <Position value={value.position} /> },
        ] : [{ key: 'characters', label: '角色数', children: value.characterCount }]),
      ]} />
        {'characterId' in value ? <Button onClick={() => onAccount(value.accountId)}>查看归属账号</Button> : <Space wrap>
          <Button onClick={() => onCharacters(value.accountId)}>查看该账号角色</Button>
          {value.activeCharacterId && <Button onClick={() => onCharacter(value.activeCharacterId!)}>查看已入场角色</Button>}
        </Space>}
        <Typography.Text type="secondary">操作对象以账号 / 角色 ID 为准；当前仅查询资料。</Typography.Text>
      </>}
    </Space>
  </Drawer>;
}
