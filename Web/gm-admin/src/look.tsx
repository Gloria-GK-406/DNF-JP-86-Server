import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { ConfigProvider, theme, type ThemeConfig } from 'antd';
import zhCN from 'antd/locale/zh_CN';

/* Palettes only move the accent, rail and neutral hues; success / warning / danger stay fixed so states read the same everywhere.
   The CSS custom properties in style.css mirror these tokens (data-theme / data-accent on <html>). */
export type Mode = 'system' | 'light' | 'dark';
export type PaletteKey = 'teal' | 'indigo' | 'amber' | 'sakura';
type Light = { primary: string; bg: string; fill: string; border: string; border2: string };
type Dark = Light & { solid: string; container: string; elevated: string };
export const palettes: Record<PaletteKey, { name: string; swatch: string; light: Light; dark: Dark }> = {
  teal: { name: '青绿', swatch: '#0d7a69', light: { primary: '#0d7a69', bg: '#edf0ef', fill: '#f6f8f7', border: '#c7d0cc', border2: '#dde3e0' },
    dark: { primary: '#47c2aa', solid: '#06221c', bg: '#0a100f', fill: '#15201d', container: '#111917', elevated: '#15201d', border: '#31433e', border2: '#21302c' } },
  indigo: { name: '靛蓝', swatch: '#3157a4', light: { primary: '#3157a4', bg: '#edf0f5', fill: '#f6f7fb', border: '#c7cfdd', border2: '#dde2ec' },
    dark: { primary: '#7f9de3', solid: '#0b1430', bg: '#0a0d14', fill: '#161b27', container: '#11151f', elevated: '#161b27', border: '#323c50', border2: '#222a39' } },
  amber: { name: '琥珀铜', swatch: '#8f4f24', light: { primary: '#8f4f24', bg: '#f1eeea', fill: '#f9f6f2', border: '#d1c6b8', border2: '#e4ddd4' },
    dark: { primary: '#d9915a', solid: '#2a1406', bg: '#0f0c09', fill: '#1d1813', container: '#17130f', elevated: '#1d1813', border: '#42372b', border2: '#2e261e' } },
  sakura: { name: '樱绯', swatch: '#b23a62', light: { primary: '#b23a62', bg: '#f3eef0', fill: '#faf6f8', border: '#d4c4cb', border2: '#e6dbe0' },
    dark: { primary: '#ec7fa5', solid: '#2b0a17', bg: '#110b0e', fill: '#20171c', container: '#191216', elevated: '#20171c', border: '#45333b', border2: '#31242a' } },
};
const base = { borderRadius: 4, borderRadiusSM: 3, fontSize: 13.5, controlHeight: 32, controlHeightSM: 26,
  fontFamily: '"Microsoft YaHei UI", "PingFang SC", "Noto Sans SC", system-ui, sans-serif' };

export function themeFor(key: PaletteKey, dark: boolean): ThemeConfig {
  const palette = palettes[key] ?? palettes.teal;
  const token = dark ? { ...base, colorPrimary: palette.dark.primary, colorInfo: palette.dark.primary, colorLink: palette.dark.primary,
    colorTextLightSolid: palette.dark.solid, colorSuccess: '#57c58a', colorWarning: '#e7a947', colorError: '#f0735d',
    colorText: '#e1eae7', colorTextSecondary: '#9caca7', colorTextTertiary: '#66776f', colorBorder: palette.dark.border, colorBorderSecondary: palette.dark.border2,
    colorBgBase: palette.dark.bg, colorBgLayout: palette.dark.bg, colorBgContainer: palette.dark.container, colorBgElevated: palette.dark.elevated, colorFillAlter: palette.dark.fill }
    : { ...base, colorPrimary: palette.light.primary, colorInfo: palette.light.primary, colorLink: palette.light.primary,
      colorSuccess: '#1b7a4c', colorWarning: '#ad6a08', colorError: '#b5372a', colorText: '#17211f', colorTextSecondary: '#51605b', colorTextTertiary: '#87948f',
      colorBorder: palette.light.border, colorBorderSecondary: palette.light.border2, colorBgLayout: palette.light.bg, colorBgContainer: '#ffffff',
      colorBgElevated: '#ffffff', colorFillAlter: palette.light.fill };
  return { algorithm: dark ? theme.darkAlgorithm : theme.defaultAlgorithm, token,
    components: { Table: { cellPaddingBlockSM: 8, rowHoverBg: token.colorFillAlter }, Segmented: { itemSelectedBg: dark ? token.colorBorderSecondary : '#ffffff' },
      Drawer: { colorBgElevated: token.colorBgContainer } } };
}

const store = {
  get: (key: string) => { try { return localStorage.getItem(key); } catch { return null; } },
  set: (key: string, value: string) => { try { localStorage.setItem(key, value); } catch { /* Appearance is a per-browser convenience. */ } },
};
type Look = { mode: Mode; palette: PaletteKey; dark: boolean; setMode: (value: Mode) => void; setPalette: (value: PaletteKey) => void };
const LookContext = createContext<Look>({ mode: 'system', palette: 'teal', dark: false, setMode: () => {}, setPalette: () => {} });
export const useLook = () => useContext(LookContext);

function systemDark() { try { return window.matchMedia('(prefers-color-scheme: dark)').matches; } catch { return false; } }
export function LookProvider({ children }: { children: ReactNode }) {
  const [mode, setMode] = useState<Mode>(() => { const value = store.get('dfo.gm.theme'); return value === 'light' || value === 'dark' ? value : 'system'; });
  const [palette, setPalette] = useState<PaletteKey>(() => { const value = store.get('dfo.gm.palette'); return value && value in palettes ? value as PaletteKey : 'teal'; });
  const [sysDark, setSysDark] = useState(systemDark);
  useEffect(() => {
    let media: MediaQueryList | undefined;
    try { media = window.matchMedia('(prefers-color-scheme: dark)'); } catch { return; }
    const change = (event: MediaQueryListEvent) => setSysDark(event.matches);
    media.addEventListener?.('change', change);
    return () => media?.removeEventListener?.('change', change);
  }, []);
  useEffect(() => { const root = document.documentElement; if (mode === 'system') delete root.dataset.theme; else root.dataset.theme = mode; store.set('dfo.gm.theme', mode); }, [mode]);
  useEffect(() => { const root = document.documentElement; if (palette === 'teal') delete root.dataset.accent; else root.dataset.accent = palette; store.set('dfo.gm.palette', palette); }, [palette]);
  const dark = mode === 'dark' || (mode === 'system' && sysDark);
  const value = useMemo(() => ({ mode, palette, dark, setMode, setPalette }), [mode, palette, dark]);
  const config = useMemo(() => themeFor(palette, dark), [palette, dark]);
  return <LookContext.Provider value={value}><ConfigProvider locale={zhCN} theme={config}>{children}</ConfigProvider></LookContext.Provider>;
}
