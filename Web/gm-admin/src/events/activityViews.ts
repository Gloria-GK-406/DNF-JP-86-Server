import type { ComponentType } from 'react';
import { WeeklyAttendanceSettings, type ActivitySettingsProps } from './WeeklyAttendanceSettings';

type ActivityView = { label: string; Settings: ComponentType<ActivitySettingsProps> };
const views: Record<string, ActivityView> = {
  'weekly-attendance': { label: '累计周签到', Settings: WeeklyAttendanceSettings },
};
export function activityView(type: string): ActivityView | undefined { return views[type]; }
