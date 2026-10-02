import { afterEach, vi } from 'vitest';
import { cleanup } from '@testing-library/react';
afterEach(() => cleanup());
Object.defineProperty(window, 'matchMedia', { value: () => ({ matches: false, addListener() {}, removeListener() {}, addEventListener() {}, removeEventListener() {} }) });
globalThis.ResizeObserver = class { observe() {} unobserve() {} disconnect() {} };
vi.spyOn(window, 'getComputedStyle').mockImplementation(() => ({ getPropertyValue: () => '', overflowX: 'hidden', overflowY: 'hidden' } as unknown as CSSStyleDeclaration));
