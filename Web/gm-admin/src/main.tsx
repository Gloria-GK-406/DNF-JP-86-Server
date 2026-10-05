import React from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { App, ConfigProvider, theme } from 'antd';
import zhCN from 'antd/locale/zh_CN';
import { AdminApp } from './AdminApp';
import './style.css';

createRoot(document.getElementById('root')!).render(<React.StrictMode>
  <ConfigProvider locale={zhCN} theme={{ algorithm: [theme.defaultAlgorithm, theme.compactAlgorithm], token: { colorPrimary: '#3157a4', borderRadius: 6 } }}>
    <App notification={{ placement: 'topRight', top: 80, maxCount: 3 }}><BrowserRouter basename="/gm"><AdminApp /></BrowserRouter></App>
  </ConfigProvider>
</React.StrictMode>);
