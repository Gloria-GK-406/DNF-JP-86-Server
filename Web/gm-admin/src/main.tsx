import React from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import { App } from 'antd';
import { AdminApp } from './AdminApp';
import { LookProvider } from './look';
import './style.css';

createRoot(document.getElementById('root')!).render(<React.StrictMode>
  <LookProvider>
    <App notification={{ placement: 'topRight', top: 60, maxCount: 3 }}><BrowserRouter basename="/gm"><AdminApp /></BrowserRouter></App>
  </LookProvider>
</React.StrictMode>);
