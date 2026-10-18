import { Button, Drawer, Spin, Table, Typography } from 'antd';
import { useGmRead } from '../useGmRead';
import { KV, Panel } from '../ui';
type Reward = { attendanceNo: number; itemId: number; itemName: string | null; count: number };
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
    return <Table<Reward> className="ledger" size="small" rowKey={row => `${row.attendanceNo}:${row.itemId}`} dataSource={items}
      pagination={false} locale={{ emptyText: '暂无可用奖励' }} columns={[
        { title: cumulative ? '累计签到次数' : '签到序号', dataIndex: 'attendanceNo', width: 150, render: value => cumulative ? `累计 ${value} 次` : `第 ${value} 次` },
        { title: '奖励物品', render: (_, row) => <span>{row.itemName ?? '名称未找到'}（<Typography.Text copyable>{row.itemId}</Typography.Text>）</span> },
        { title: '数量', dataIndex: 'count', width: 100, align: 'end', render: value => <span className="mono">{value}</span> },
      ]} />;
  }
  return <Drawer open title={`设置活动 · ${title}`} size={680} onClose={onClose} mask={{ closable: false }} destroyOnHidden extra={<span className="chip">PVF · 只读</span>}
    footer={<div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}><Button disabled={current.busy} onClick={current.refresh}>刷新规则</Button><Button type="primary" onClick={onClose}>关闭</Button></div>}>
    <div className="grid">
      <span className="faint small">签到目标与奖励由 PVF 提供，当前仅供查看。</span>
      {current.busy && <Spin />}
      {current.error && <span className="faint small">规则读取失败，请点击“刷新规则”重试。</span>}
      {data && <>
        <Panel title="规则"><KV rows={[
          ['规则来源', <Typography.Text copyable key="s" className="mono small">{data.sourcePath}</Typography.Text>], ['当前期次', data.seasonId], ['配置版本', data.revision],
          ['规则状态', data.available ? '可用' : (data.unavailableReason ?? '活动规则暂不可用')],
          ...(data.available ? [['每周通关目标', `${data.requiredClears} 次推荐地下城`], ['签到次数上限', `${data.totalWeeks} 次`]] as [string, string][] : []),
        ]} />{data.available && <div className="faint small" style={{ marginTop: 8 }}>按账号累计，每个业务周最多签到一次；达标后在游戏内签到，奖励通过邮件发放。</div>}</Panel>
        {data.available && <>
          <Panel title="每周奖励" flush>{rewardTable(data.weeklyRewards, false)}</Panel>
          <Panel title="累计奖励" flush>{rewardTable(data.cumulativeRewards, true)}</Panel>
        </>}
      </>}
    </div>
  </Drawer>;
}
