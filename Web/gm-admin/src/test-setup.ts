import { afterEach, vi } from 'vitest';
import { cleanup, configure } from '@testing-library/react';
// CI runners render the whole console several times slower than a workstation; the 1s default leaves first renders no margin.
configure({ asyncUtilTimeout: 3000 });
// Imported lazily: a static import here would load ./api before a test file's vi.mock('./api') applies.
afterEach(async () => { cleanup(); (await import('./itemInfo')).resetItemCaches(); try { localStorage.clear(); } catch { /* jsdom always has storage */ } });
Object.defineProperty(window, 'matchMedia', { value: () => ({ matches: false, addListener() {}, removeListener() {}, addEventListener() {}, removeEventListener() {} }) });
globalThis.ResizeObserver = class { observe() {} unobserve() {} disconnect() {} };
vi.spyOn(window, 'getComputedStyle').mockImplementation(() => ({ getPropertyValue: () => '', overflowX: 'hidden', overflowY: 'hidden' } as unknown as CSSStyleDeclaration));
