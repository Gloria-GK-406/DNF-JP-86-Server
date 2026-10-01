import React from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter, useLocation, useNavigate } from 'react-router-dom';
import { App as AntApp, ConfigProvider, Empty, Layout, Menu, theme, Typography } from 'antd';
import zhCN from 'antd/locale/zh_CN';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import './style.css';

const pages = [
  { key: '/', label: '服务概览' }, { key: '/characters', label: '账号与角色' },
  { key: '/mail', label: '官方邮件' }, { key: '/wallet', label: '点券发放' },
  { key: '/operations', label: '操作记录' },
];

function Shell() {
  const location = useLocation();
  const navigate = useNavigate();
  return <Layout className="shell">
    <Layout.Sider theme="light" width={216}><div className="brand">DNF <span>GM 控制台</span></div>
      <Menu items={pages} selectedKeys={[location.pathname]} onClick={({ key }) => navigate(key)} />
    </Layout.Sider>
    <Layout><Layout.Header className="header"><Typography.Text strong>JP Act.86 服务管理</Typography.Text><Typography.Text type="secondary">模块搭建中</Typography.Text></Layout.Header>
      <Layout.Content className="content"><Typography.Title level={3}>{pages.find(p => p.key === location.pathname)?.label ?? '管理后台'}</Typography.Title>
        <Empty description="项目骨架已建立，管理能力将分阶段接入。" /></Layout.Content>
    </Layout>
  </Layout>;
}

createRoot(document.getElementById('root')!).render(<React.StrictMode>
  <ConfigProvider locale={zhCN} theme={{ algorithm: [theme.defaultAlgorithm, theme.compactAlgorithm], token: { colorPrimary: '#3157a4', borderRadius: 6 } }}>
    <AntApp><QueryClientProvider client={new QueryClient()}><BrowserRouter basename="/gm"><Shell /></BrowserRouter></QueryClientProvider></AntApp>
  </ConfigProvider>
</React.StrictMode>);
