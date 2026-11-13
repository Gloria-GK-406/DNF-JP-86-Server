import { Button } from 'antd';
import { LockOutlined } from '@ant-design/icons';

export function PortalClosedView({ onBack }: { onBack: () => void }) {
  return <div className="closed" role="status">
    <div className="box"><LockOutlined className="closed-icon" /><h1>玩家后台已关闭</h1><Button onClick={onBack}>返回登录</Button></div>
  </div>;
}
