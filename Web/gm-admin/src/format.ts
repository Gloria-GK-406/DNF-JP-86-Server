export const nf = (value: number | bigint) => Number(value).toLocaleString('zh-CN');
export const utf8Bytes = (value: string) => new TextEncoder().encode(value).length;
const pad = (value: number) => String(value).padStart(2, '0');

/** Beijing time (UTC+8), the zone the game and event schedules use. */
export function bjTime(value: string | number | Date | null | undefined, withSeconds = false) {
  if (value === null || value === undefined || value === '') return '—';
  const date = new Date(new Date(value).getTime() + 8 * 3600000);
  if (Number.isNaN(date.getTime())) return '—';
  const text = `${date.getUTCFullYear()}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())} ${pad(date.getUTCHours())}:${pad(date.getUTCMinutes())}`;
  return withSeconds ? `${text}:${pad(date.getUTCSeconds())}` : text;
}
export const bjClock = (value: string | number | Date | null | undefined) => bjTime(value).slice(11) || '—';

export function greeting(now = Date.now()) {
  const hour = new Date(now + 8 * 3600000).getUTCHours();
  return hour < 6 ? '夜深了' : hour < 11 ? '早上好' : hour < 14 ? '中午好' : hour < 18 ? '下午好' : '晚上好';
}

export const DEFAULT_MAIL_BODY = '此为官方邮件，无需回复。';
/** Mirrors the server's GmMailRuleService.DisplayText after the blank-body default is applied. */
export function mailDisplayText(title: string, body: string) {
  const text = body.trim() ? body : DEFAULT_MAIL_BODY;
  return !title || title === text ? text : `${title}\n${text}`;
}

/** Job index → [base name, grow type 1..n], read from the server PVF character/*.chr [growtype name]. */
const careers: Record<number, [string, ...string[]]> = {
  0: ['鬼剑士（男）', '剑魂', '鬼泣', '狂战士', '阿修罗'], 1: ['格斗家（女）', '气功师', '散打', '街霸', '柔道家'],
  2: ['神枪手（男）', '漫游枪手', '枪炮师', '机械师', '弹药专家'], 3: ['魔法师（女）', '元素师', '召唤师', '战斗法师', '魔道学者'],
  4: ['圣职者（男）', '圣骑士', '蓝拳圣使', '驱魔师', '复仇者'], 5: ['神枪手（女）', '漫游枪手', '枪炮师', '机械师', '弹药专家'],
  6: ['暗夜使者', '刺客', '死灵术士', '忍者', '影舞者'], 7: ['格斗家（男）', '气功师', '散打', '街霸', '柔道家'],
  8: ['魔法师（男）', '元素爆破师', '冰结师'], 9: ['黑暗武士'], 10: ['缔造者'],
  11: ['鬼剑士（女）', '驭剑士', '暗殿骑士', '契魔者', '流浪武士'], 12: ['守护者', '精灵骑士', '混沌魔灵'], 13: ['魔枪士', '征战者', '决战者'],
};
export function jobName(job: number) { return careers[job]?.[0] ?? `职业 ${job}`; }
/** Grow type keeps the advancement in its low nibble; the high nibble counts awakenings. */
export function careerName(job: number, growType: number) {
  const names = careers[job]; const grow = growType & 0x0f; const awakened = growType >> 4;
  const base = grow === 0 ? names?.[0] ?? `职业 ${job}` : names?.[grow] ?? `转职 ${grow}`;
  return awakened > 0 ? `${base}（觉醒）` : base;
}
export const numberFormat = {
  formatter: (value: number | string | undefined) => `${value ?? ''}`.replace(/\B(?=(\d{3})+(?!\d))/g, ','),
  parser: (value: string | undefined) => Number((value ?? '').replace(/,/g, '')),
};
