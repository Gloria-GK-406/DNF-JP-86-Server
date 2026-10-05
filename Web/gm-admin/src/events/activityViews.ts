import type { ComponentType } from 'react';
import { WeeklyAttendanceSettings, type ActivitySettingsProps } from './WeeklyAttendanceSettings';
import { JoustSettings } from './JoustSettings';

type ActivityView = { label: string; Settings: ComponentType<ActivitySettingsProps> };
const views: Record<string, ActivityView> = {
  'weekly-attendance': { label: '累计周签到', Settings: WeeklyAttendanceSettings },
  joust: { label: '骑士马战大竞猜', Settings: JoustSettings },
};
export function activityView(type: string): ActivityView | undefined { return views[type]; }
