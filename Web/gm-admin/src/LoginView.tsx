import { useState, type FormEvent } from 'react';
import { Button, Input, Spin } from 'antd';
import { NotiHint } from './NotiBar';
import { BrandMark, Field } from './ui';

export function LoginView({ initialized, loading, onLogin, onReconnect }: { initialized: boolean | null; loading: boolean;
  onLogin: (values: { loginName: string; password: string }) => void; onReconnect: () => void }) {
  const [loginName, setLoginName] = useState(''); const [password, setPassword] = useState('');
  const [touched, setTouched] = useState(false);
  function submit(event: FormEvent) {
    event.preventDefault(); setTouched(true);
    if (!loginName.trim() || !password) return;
    onLogin({ loginName: loginName.trim(), password });
  }
  return <div className="login">
    <div className="login-art">
      <div><BrandMark className="big" /><h2>86JP 管理台</h2><p>值班、发放、配置和审计都在这里。今晚的阿拉德交给你了。</p></div>
      <div className="tape"><span>独立管理员账号 · 会话 8 小时</span><span>远程访问需要 HTTPS</span></div>
    </div>
    <div className="login-form">
      {loading && initialized === null ? <Spin description="正在连接管理服务…"><div style={{ width: 200, height: 80 }} /></Spin> :
        <form className="login-card" onSubmit={submit} noValidate>
          <h1>登录</h1>
          {initialized === false ? <>
            <NotiHint noticeKey="not-initialized" title="管理员尚未初始化" description="请在服务端配置 GmWeb:Bootstrap:LoginName 和 Password 后重新启动。私有管理功能当前已关闭。" />
            <span className="dim">管理服务尚未就绪：需要先在服务端配置引导管理员。</span>
          </> : initialized === null ? <>
            <span className="dim">暂时连不上管理服务。</span><Button onClick={onReconnect}>重新连接</Button>
          </> : <>
            <Field label="管理员账号" htmlFor="login-name" err={touched && !loginName.trim() ? '请输入管理员账号' : undefined}>
              <Input id="login-name" size="large" autoComplete="username" maxLength={64} value={loginName} onChange={event => setLoginName(event.target.value)} /></Field>
            <Field label="密码" htmlFor="login-password" err={touched && !password ? '请输入密码' : undefined}>
              <Input.Password id="login-password" size="large" autoComplete="current-password" maxLength={256} value={password} onChange={event => setPassword(event.target.value)} /></Field>
            <Button type="primary" size="large" htmlType="submit" loading={loading} block>登录</Button>
            <span className="faint small">会话固定 8 小时，不自动续期。连续输错会被限流 15 分钟。</span>
          </>}
        </form>}
    </div>
  </div>;
}
