import { Button, Card, Descriptions, Modal, Space, Spin, Table, Tag, Typography } from 'antd';
import { useGmRead } from '../useGmRead';

type Reward = { attendanceNo: number; itemId: number; count: number };
export type WeeklyAttendanceSettingsData = {
  eventId: number; revision: number; seasonId: string; sourcePath: string; available: boolean;
  unavailableReason: string | null; requiredClears: number | null; totalWeeks: number | null;
  weeklyRewards: Reward[]; cumulativeRewards: Reward[];
};
export type ActivitySettingsProps = { title: string; onClose: () => void; onExpired: () => void; onDenied: () => void };

export function WeeklyAttendanceSettings({ title, onClose, onExpired, onDenied }: ActivitySettingsProps) {
  const current = useGmRead<WeeklyAttendanceSettingsData>('events/weekly-attendance/settings', onExpired, onDenied);
  const data = current.data;
  function rewardTable(items: Reward[], cumulative: boolean) {
    return <Table<Reward> size="small" rowKey={row => `${row.attendanceNo}:${row.itemId}`} dataSource={items}
      pagination={false} locale={{ emptyText: '暂无可用奖励' }} columns={[
        { title: cumulative ? '累计签到次数' : '签到序号', dataIndex: 'attendanceNo', width: 150,
          render: value => cumulative ? `累计 ${value} 次` : `第 ${value} 次` },
        { title: '物品 ID', dataIndex: 'itemId', render: value => <Typography.Text copyable>{value}</Typography.Text> },
        { title: '数量', dataIndex: 'count', width: 100 },
      ]} />;
  }
  return <Modal open title={`设置活动 · ${title}`} width={860} onCancel={onClose} mask={{ closable: false }} destroyOnHidden
    styles={{ body: { maxHeight: 'calc(100vh - 240px)', overflowY: 'auto', paddingRight: 8 } }}
    footer={<Space><Button disabled={current.busy} onClick={current.refresh}>刷新规则</Button><Button type="primary" onClick={onClose}>关闭</Button></Space>}>
    <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
      <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
        <Tag color="blue">PVF · 只读</Tag>签到目标与奖励由 PVF 提供，当前仅供查看。
      </Typography.Paragraph>
      {current.busy && <Spin />}
      {current.error && <Typography.Text type="secondary">规则读取失败，请点击“刷新规则”重试。</Typography.Text>}
      {data && <>
        <Descriptions size="small" column={1} items={[
          { key: 'source', label: '规则来源', children: <Typography.Text copyable>{data.sourcePath}</Typography.Text> },
          { key: 'season', label: '当前期次', children: data.seasonId },
          { key: 'revision', label: '配置版本', children: data.revision },
          { key: 'availability', label: '规则状态', children: data.available ? '可用' : (data.unavailableReason ?? '活动规则暂不可用') },
        ]} />
        {data.available && <>
          <Card size="small" title="签到规则">
            <Descriptions size="small" column={2} items={[
              { key: 'clears', label: '每周通关目标', children: `${data.requiredClears} 次推荐地下城` },
              { key: 'weeks', label: '签到次数上限', children: `${data.totalWeeks} 次` },
            ]} />
            <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>按账号累计，每个业务周最多签到一次；达标后在游戏内签到，奖励通过邮件发放。</Typography.Paragraph>
          </Card>
          <Card size="small" title="每周奖励">{rewardTable(data.weeklyRewards, false)}</Card>
          <Card size="small" title="累计奖励">{rewardTable(data.cumulativeRewards, true)}</Card>
        </>}
      </>}
    </Space>
  </Modal>;
}
