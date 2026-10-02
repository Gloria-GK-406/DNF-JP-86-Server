import { useEffect, useRef, useState } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { Alert, App, Button, Card, Empty, Form, Input, Layout, Menu, Space, Spin, Table, Tag, Typography } from 'antd';
import { api, ApiError, type AuditPage, type Identity, type Permission } from './api';
import { OverviewView } from './OverviewView';
import { PlayerDirectoryView } from './PlayerDirectoryView';
import { OfficialMailView } from './OfficialMailView';
import { CeraGrantView } from './CeraGrantView';

const pages: { key: string; label: string; permission: Permission | null }[] = [
  { key: '/', label: '服务概览', permission: 'QueryPlayers' }, { key: '/characters', label: '账号与角色', permission: 'QueryPlayers' },
  { key: '/mail', label: '官方邮件', permission: 'SendMail' }, { key: '/wallet', label: '点券发放', permission: 'GrantCera' },
  { key: '/operations', label: '操作记录', permission: 'ViewAudit' },
];
const actionLabels: Record<string, string> = { Login: '登录', Logout: '退出', SendOfficialMail: '发送官方邮件', SaveMailDraft: '保存邮件草稿', EditMailTemplate: '修改内容模板' };
const outcomeLabels: Record<string, string> = { Succeeded: '成功', Rejected: '拒绝', RolledBack: '已回滚', Unknown: '结果未确认' };

export function AdminApp() {
  const [identity, setIdentity] = useState<Identity | null>(null);
  const [initialized, setInitialized] = useState<boolean | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const generation = useRef(0);
  const mounted = useRef(true);
  const transitioning = useRef(false);
  const currentIdentity = useRef<Identity | null>(null);
  const location = useLocation(); const navigate = useNavigate(); const { message } = App.useApp();
  function isCurrent(epoch: number) { return mounted.current && generation.current === epoch; }
  function applyIdentity(value: Identity | null) { currentIdentity.current = value; setIdentity(value); }
  function beginTransition() { transitioning.current = true; return ++generation.current; }
  function expire(epoch: number) {
    if (!isCurrent(epoch) || transitioning.current) return;
    generation.current++; applyIdentity(null); setError('会话已失效，请重新登录。');
  }
  function denyPermission(epoch: number, permission: Permission) {
    if (!isCurrent(epoch) || transitioning.current || !currentIdentity.current) return;
    generation.current++;
    applyIdentity({ ...currentIdentity.current, permissions: currentIdentity.current.permissions.filter(value => value !== permission) });
  }
  async function refresh() {
    const epoch = beginTransition();
    setLoading(true); setError('');
    try {
      const status = await api<{ initialized: boolean }>('auth/status');
      if (!isCurrent(epoch)) return;
      setInitialized(status.initialized);
      if (!status.initialized) { applyIdentity(null); return; }
      try { const current = await api<Identity>('auth/session'); if (isCurrent(epoch)) applyIdentity(current); }
      catch (failure) { if (!isCurrent(epoch)) return; if (failure instanceof ApiError && failure.status === 401) applyIdentity(null); else throw failure; }
    } catch (failure) { if (isCurrent(epoch)) setError((failure as Error).message); }
    finally { if (isCurrent(epoch)) { transitioning.current = false; setLoading(false); } }
  }
  useEffect(() => {
    mounted.current = true; void refresh();
    return () => { mounted.current = false; generation.current++; };
  }, []);
  useEffect(() => {
    if (!identity) return;
    let active = true;
    const timer = window.setInterval(() => {
      if (!active || transitioning.current || currentIdentity.current?.id !== identity.id) return;
      const epoch = generation.current;
      api<Identity>('auth/session').then(current => { if (active && isCurrent(epoch)) applyIdentity(current); }).catch(failure => {
        if (active && failure instanceof ApiError && failure.status === 401) expire(epoch);
      });
    }, 60000);
    return () => { active = false; window.clearInterval(timer); };
  }, [identity?.id]);
  async function login(values: { loginName: string; password: string }) {
    const epoch = beginTransition();
    setError(''); setLoading(true);
    try {
      const current = await api<Identity>('auth/login', values);
      if (!isCurrent(epoch)) return;
      applyIdentity(current); navigate(current.permissions.includes('ViewAudit') ? '/operations' : pages.find(page => page.permission !== null && current.permissions.includes(page.permission))?.key ?? '/');
    } catch (failure) { if (isCurrent(epoch)) setError((failure as Error).message); }
    finally { if (isCurrent(epoch)) { transitioning.current = false; setLoading(false); } }
  }
  async function logout() {
    const epoch = beginTransition(); setLoading(true);
    try { await api<void>('auth/logout', {}); if (isCurrent(epoch)) { applyIdentity(null); setError(''); } }
    catch (failure) {
      if (!isCurrent(epoch)) return;
      if (failure instanceof ApiError && failure.status === 401) applyIdentity(null); else void message.error((failure as Error).message);
    } finally { if (isCurrent(epoch)) { transitioning.current = false; setLoading(false); } }
  }
  if (loading && initialized === null) return <div className="login-page"><Spin tip="正在连接管理服务…"><div className="loading-block" /></Spin></div>;
  if (!identity) return <div className="login-page"><Card className="login-card" title="DNF GM 控制台">
    <Typography.Paragraph type="secondary">使用独立管理员账号登录</Typography.Paragraph>
    {error && <Alert type="error" title={error} showIcon />}
    {initialized === false ? <Alert type="warning" title="管理员尚未初始化" description="请在服务端配置 GmWeb:Bootstrap:LoginName 和 Password 后重新启动。私有管理功能当前已关闭。" showIcon /> :
      initialized === null ? <Button onClick={() => void refresh()}>重新连接</Button> :
      <Form layout="vertical" onFinish={login} requiredMark={false}>
        <Form.Item label="管理员账号" name="loginName" rules={[{ required: true, message: '请输入管理员账号' }]}><Input autoComplete="username" maxLength={64} /></Form.Item>
        <Form.Item label="密码" name="password" rules={[{ required: true, message: '请输入密码' }]}><Input.Password autoComplete="current-password" maxLength={256} /></Form.Item>
        <Button htmlType="submit" type="primary" block loading={loading}>登录</Button>
      </Form>}
  </Card></div>;
  const visible = pages.filter(page => page.permission === null || identity.permissions.includes(page.permission));
  const page = pages.find(item => item.key === location.pathname);
  const allowed = page?.permission === null || (page?.permission !== undefined && identity.permissions.includes(page.permission));
  const viewGeneration = generation.current;
  return <Layout className="shell"><Layout.Sider className="sidebar" theme="light" width={216}>
    <div className="brand">DNF <span>GM 控制台</span></div><Menu items={visible} selectedKeys={[location.pathname]} onClick={({ key }) => navigate(key)} />
  </Layout.Sider><Layout className="workspace"><Layout.Header className="header"><Typography.Text strong>JP Act.86 服务管理</Typography.Text>
    <Space><Typography.Text>{identity.loginName}</Typography.Text><Button loading={loading} onClick={() => void logout()}>退出登录</Button></Space></Layout.Header>
    <Layout.Content className="content"><Typography.Title level={3}>{page?.label ?? '管理后台'}</Typography.Title>
      {!allowed ? <Alert type="warning" title="没有访问此页面的权限" showIcon /> : location.pathname === '/operations' ?
        <AuditView key={`${identity.id}:${viewGeneration}`} onExpired={() => expire(viewGeneration)} /> :
        location.pathname === '/' ? <OverviewView key={`${identity.id}:${viewGeneration}`} onExpired={() => expire(viewGeneration)} onDenied={() => denyPermission(viewGeneration, 'QueryPlayers')} /> :
        location.pathname === '/characters' ? <PlayerDirectoryView key={`${identity.id}:${viewGeneration}`} onExpired={() => expire(viewGeneration)} onDenied={() => denyPermission(viewGeneration, 'QueryPlayers')} /> :
        location.pathname === '/mail' ? <OfficialMailView key={`${identity.id}:${viewGeneration}`} administratorId={identity.id} onExpired={() => expire(viewGeneration)} onDenied={() => denyPermission(viewGeneration, 'SendMail')} /> :
        location.pathname === '/wallet' ? <CeraGrantView key={`${identity.id}:${viewGeneration}`} administratorId={identity.id} onExpired={() => expire(viewGeneration)} onDenied={() => denyPermission(viewGeneration, 'GrantCera')} /> :
        <Empty description="本模块将在后续阶段接入。" />}
    </Layout.Content></Layout></Layout>;
}

function AuditView({ onExpired }: { onExpired: () => void }) {
  const [data, setData] = useState<AuditPage>({ items: [], total: 0, page: 1, pageSize: 20 });
  const [page, setPage] = useState(1); const [pageSize, setPageSize] = useState(20); const [busy, setBusy] = useState(false); const [error, setError] = useState('');
  useEffect(() => {
    let active = true; setBusy(true); setError('');
    api<AuditPage>(`audit?page=${page}&pageSize=${pageSize}`).then(result => { if (active) setData(result); }).catch(failure => {
      if (!active) return;
      if (failure instanceof ApiError && failure.status === 401) onExpired();
      else { setData({ items: [], total: 0, page, pageSize }); setError((failure as Error).message); }
    }).finally(() => { if (active) setBusy(false); });
    return () => { active = false; };
  }, [page, pageSize]);
  return <Card>{error && <Alert type="error" title={error} showIcon />}
    <Table rowKey="operationId" dataSource={data.items} loading={busy} scroll={{ x: 1000 }}
      pagination={{ current: page, pageSize, total: data.total, showSizeChanger: true, showTotal: total => `共 ${total} 条`, onChange: (next, size) => { setPage(next); setPageSize(size); } }}
      columns={[
        { title: '时间', dataIndex: 'occurredAt', render: value => new Date(value).toLocaleString('zh-CN') },
        { title: '操作', dataIndex: 'action', render: value => actionLabels[value] ?? value }, { title: '管理员', dataIndex: 'administratorName', render: value => value ?? '未知身份' },
        { title: '对象', dataIndex: 'target' }, { title: '结果', dataIndex: 'outcome', render: value => <Tag color={value === 'Succeeded' ? 'green' : 'orange'}>{outcomeLabels[value] ?? value}</Tag> },
        { title: '说明', dataIndex: 'reason' }, { title: '操作编号', dataIndex: 'operationId', ellipsis: true },
      ]} /></Card>;
}
