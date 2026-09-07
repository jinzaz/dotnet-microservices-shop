import { useMemo, useSyncExternalStore } from 'react';
import type { CartLine } from './types';

const STORAGE_KEY = 'msa-store:basket:v1';
const EMPTY_LINES: readonly CartLine[] = [];

type StoredBasket = {
  version: 1;
  lines: CartLine[];
};

function isCartLine(value: unknown): value is CartLine {
  if (!value || typeof value !== 'object') return false;
  const line = value as Partial<CartLine>;
  return typeof line.productId === 'string'
    && typeof line.sku === 'string'
    && typeof line.name === 'string'
    && typeof line.image === 'string'
    && typeof line.unitPrice === 'number'
    && typeof line.currency === 'string'
    && typeof line.quantity === 'number'
    && line.attributes !== null
    && typeof line.attributes === 'object';
}

function readStoredLines(): readonly CartLine[] {
  if (typeof window === 'undefined') return EMPTY_LINES;
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (!raw) return EMPTY_LINES;
    const parsed = JSON.parse(raw) as Partial<StoredBasket>;
    if (parsed.version !== 1 || !Array.isArray(parsed.lines)) return EMPTY_LINES;
    return parsed.lines.filter(isCartLine).map((line) => ({
      ...line,
      quantity: Math.min(99, Math.max(1, Math.trunc(line.quantity))),
    }));
  } catch {
    return EMPTY_LINES;
  }
}

let linesSnapshot = readStoredLines();
const listeners = new Set<() => void>();

function save(nextLines: readonly CartLine[]) {
  linesSnapshot = nextLines;
  try {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify({ version: 1, lines: nextLines }));
  } catch {
    // The in-memory basket remains usable when storage is unavailable.
  }
  listeners.forEach((listener) => listener());
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

function getSnapshot() {
  return linesSnapshot;
}

export function addCartLine(input: CartLine) {
  const current = linesSnapshot.find((line) => line.sku === input.sku);
  if (!current) {
    save([...linesSnapshot, { ...input, quantity: Math.min(99, Math.max(1, input.quantity)) }]);
    return;
  }
  save(linesSnapshot.map((line) => line.sku === input.sku
    ? { ...line, ...input, quantity: Math.min(99, line.quantity + input.quantity) }
    : line));
}

export function ensureCartLine(input: CartLine) {
  if (linesSnapshot.some((line) => line.sku === input.sku)) return;
  save([...linesSnapshot, { ...input, quantity: Math.min(99, Math.max(1, input.quantity)) }]);
}

export function updateCartLineQuantity(sku: string, quantity: number) {
  const nextQuantity = Math.min(99, Math.max(1, Math.trunc(quantity)));
  save(linesSnapshot.map((line) => line.sku === sku ? { ...line, quantity: nextQuantity } : line));
}

export function removeCartLine(sku: string) {
  save(linesSnapshot.filter((line) => line.sku !== sku));
}

export function clearCart() {
  save(EMPTY_LINES);
}

export function useBasket() {
  const lines = useSyncExternalStore(subscribe, getSnapshot, () => EMPTY_LINES);
  return useMemo(() => ({
    lines,
    itemCount: lines.reduce((total, line) => total + line.quantity, 0),
    subtotal: lines.reduce((total, line) => total + line.unitPrice * line.quantity, 0),
  }), [lines]);
}
