import { Button, Card, Col, Row, Space, Statistic, Table, Typography } from 'antd';
import type { Overview } from './api';
import { useGmRead } from './useGmRead';

export function SampleTime({ value }: { value?: string }) {
  return <Typography.Text type="secondary">{value ? `采样时间：${new Date(value).toLocaleString('zh-CN')}（可刷新）` : '等待采样'}</Typography.Text>;
}
export function OverviewView({ onExpired, onDenied }: { onExpired: () => void; onDenied: () => void }) {
  const query = useGmRead<Overview>('overview', onExpired, onDenied);
  const data = query.data;
  return <Space vertical style={{ width: '100%' }}>
    <Space><Button onClick={query.refresh} loading={query.busy}>刷新概览</Button><SampleTime value={data?.sampledAt} /></Space>
    <Card loading={query.busy}>
      {data && <><Typography.Paragraph>游戏网络：{data.networkEnabled ? '已启用' : '已关闭'}；游戏服务：{data.gameReady ? '已就绪' : '未就绪'}</Typography.Paragraph>
        <Row gutter={[16, 16]}>{[['账号总数', data.totalAccounts], ['角色总数', data.totalCharacters], ['在线账号', data.onlineAccounts], ['已入场角色', data.onlineCharacters]]
          .map(([title, value]) => <Col xs={12} lg={6} key={title}><Statistic title={title} value={value} /></Col>)}</Row>
        <Typography.Paragraph type="secondary">账号在线包含登录未选角；频道人数只计算已入场角色。未映射频道角色：{data.unmappedOnlineCharacters}</Typography.Paragraph>
        <Table rowKey="channelId" pagination={false} dataSource={data.channels} columns={[
          { title: '频道 ID', dataIndex: 'channelId' }, { title: '频道名称', dataIndex: 'name' }, { title: '已入场角色', dataIndex: 'onlineCharacters' },
        ]} /></>}
    </Card>
  </Space>;
}
