import { useEffect, useState, type FormEvent } from 'react';
import { Button, Input, Segmented, Spin } from 'antd';
import { NotiHint } from './NotiBar';
import { BrandMark, Field } from './ui';

/** The player side of the shared login page; absent (or disabled) means the page is the administrator login alone. */
export type PlayerLoginOption = { enabled: boolean; busy: boolean; onLogin: (values: { loginName: string; password: string }) => void; onShown: () => void };
type Who = 'player' | 'admin';

export function LoginView({ initialized, loading, onLogin, onReconnect, player }: { initialized: boolean | null; loading: boolean;
  onLogin: (values: { loginName: string; password: string }) => void; onReconnect: () => void; player?: PlayerLoginOption }) {
  const [loginName, setLoginName] = useState(''); const [password, setPassword] = useState('');
  const [touched, setTouched] = useState(false);
  const [chosen, setChosen] = useState<Who>('player');
  const who: Who = player?.enabled ? chosen : 'admin';
  const shown = player?.onShown;
  useEffect(() => { shown?.(); }, []);
  function submit(event: FormEvent) {
    event.preventDefault(); setTouched(true);
    if (!loginName.trim() || !password) return;
    const values = { loginName: loginName.trim(), password };
    if (who === 'player') player!.onLogin(values); else onLogin(values);
  }
  function choose(value: Who) { setChosen(value); setTouched(false); }
  const nameLabel = who === 'player' ? '账号' : '管理员账号';
  const form = <>
    <Field label={nameLabel} htmlFor="login-name" err={touched && !loginName.trim() ? `请输入${nameLabel}` : undefined}>
      <Input id="login-name" size="large" autoComplete="username" maxLength={who === 'player' ? 256 : 64} value={loginName} onChange={event => setLoginName(event.target.value)} /></Field>
    <Field label="密码" htmlFor="login-password" err={touched && !password ? '请输入密码' : undefined}>
      <Input.Password id="login-password" size="large" autoComplete="current-password" maxLength={256} value={password} onChange={event => setPassword(event.target.value)} /></Field>
    <Button type="primary" size="large" htmlType="submit" loading={who === 'player' ? player!.busy : loading} block>登录</Button>
  </>;
  return <div className="login">
    <div className="login-art"><div><BrandMark className="big" /><h2>86JP</h2></div></div>
    <div className="login-form">
      <form className="login-card" onSubmit={submit} noValidate>
        <h1>登录</h1>
        {player?.enabled && <Segmented block aria-label="登录身份" value={who} onChange={value => choose(value as Who)}
          options={[{ label: '玩家', value: 'player' }, { label: '管理员', value: 'admin' }]} />}
        {who === 'player' ? form : loading && initialized === null ? <Spin description="正在连接管理服务…"><div style={{ width: 200, height: 80 }} /></Spin>
          : initialized === false ? <>
          <NotiHint noticeKey="not-initialized" title="管理员尚未初始化" description="请在服务端配置 GmWeb:Bootstrap:LoginName 和 Password 后重新启动。私有管理功能当前已关闭。" />
          <span className="dim">管理服务尚未就绪：需要先在服务端配置引导管理员。</span>
        </> : initialized === null ? <>
          <span className="dim">暂时连不上管理服务。</span><Button onClick={() => { onReconnect(); shown?.(); }}>重新连接</Button>
        </> : form}
      </form>
    </div>
  </div>;
}
