import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useMemo, useRef, useSyncExternalStore } from 'react';
import { useAuth } from '../../app/auth/AuthProvider';
import { ApiError } from '../../shared/api/api-error';
import { isDemoMode } from '../../shared/config/api';
import { useToast } from '../../shared/ui/Toast';
import { imageForSku, parseAttributes, rememberSkuImage } from '../catalog/product-view';
import {
  addBasketItem,
  clearBasket,
  getBasket,
  removeBasketItem,
  replaceBasketCoupons,
  setBasketItemQuantity,
} from './basket-api';
import type { Basket, BasketItem, CartLine } from './types';

export const basketQueryKey = ['basket', 'mine'] as const;
export const MAX_LINE_QUANTITY = 99;

const STORAGE_KEY = 'msa-store:basket:v1';
const EMPTY_LINES: readonly CartLine[] = [];

// ---- Guest basket: kept in localStorage until the shopper signs in. ----

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

function clampQuantity(quantity: number): number {
  return Math.min(MAX_LINE_QUANTITY, Math.max(1, Math.trunc(quantity)));
}

function readStoredLines(): readonly CartLine[] {
  if (typeof window === 'undefined') return EMPTY_LINES;
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (!raw) return EMPTY_LINES;
    const parsed = JSON.parse(raw) as { version?: number; lines?: unknown[] };
    if (parsed.version !== 1 || !Array.isArray(parsed.lines)) return EMPTY_LINES;
    return parsed.lines.filter(isCartLine).map((line) => ({ ...line, quantity: clampQuantity(line.quantity) }));
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

export function addCartLine(input: CartLine) {
  const current = linesSnapshot.find((line) => line.sku === input.sku);
  save(current
    ? linesSnapshot.map((line) => line.sku === input.sku ? { ...line, ...input, quantity: clampQuantity(line.quantity + input.quantity) } : line)
    : [...linesSnapshot, { ...input, quantity: clampQuantity(input.quantity) }]);
}

export function ensureCartLine(input: CartLine) {
  if (linesSnapshot.some((line) => line.sku === input.sku)) return;
  save([...linesSnapshot, { ...input, quantity: clampQuantity(input.quantity) }]);
}

export function updateCartLineQuantity(sku: string, quantity: number) {
  save(linesSnapshot.map((line) => line.sku === sku ? { ...line, quantity: clampQuantity(quantity) } : line));
}

export function removeCartLine(sku: string) {
  save(linesSnapshot.filter((line) => line.sku !== sku));
}

export function clearCart() {
  save(EMPTY_LINES);
}

// ---- Signed-in basket: BasketService is the source of truth. ----

function toCartLine(item: BasketItem): CartLine {
  return {
    productId: item.productId,
    sku: item.sku,
    name: item.productName,
    variantName: item.variantName,
    image: imageForSku(item.sku, item.productId),
    unitPrice: item.unitPrice,
    currency: item.currency,
    quantity: item.quantity,
    attributes: parseAttributes(item.attributesJson),
    isAvailable: item.isAvailable,
    priceChanged: item.priceChangedSinceSelection,
    previousUnitPrice: item.unitPriceAtSelection,
  };
}

function useRemoteBasketEnabled(): boolean {
  const { isAuthenticated } = useAuth();
  return isAuthenticated && !isDemoMode();
}

export function useBasket() {
  const remote = useRemoteBasketEnabled();
  const localLines = useSyncExternalStore(subscribe, () => linesSnapshot, () => EMPTY_LINES);
  const query = useQuery({
    queryKey: basketQueryKey,
    queryFn: getBasket,
    enabled: remote,
    staleTime: 15_000,
  });
  const basket = remote ? query.data : undefined;

  return useMemo(() => {
    const lines = remote ? (basket?.items ?? []).map(toCartLine) : localLines;
    return {
      remote,
      basket,
      lines,
      itemCount: lines.reduce((total, line) => total + line.quantity, 0),
      subtotal: lines.reduce((total, line) => total + line.unitPrice * line.quantity, 0),
      currency: lines[0]?.currency ?? 'CNY',
      couponCodes: basket?.couponCodes ?? [],
      hasUnavailableItems: basket?.hasUnavailableItems ?? false,
      hasPriceChanges: basket?.hasPriceChanges ?? false,
      isLoading: remote && query.isPending,
      error: remote ? query.error : null,
      refetch: query.refetch,
    };
  }, [basket, localLines, query.error, query.isPending, query.refetch, remote]);
}

export function useBasketActions() {
  const remote = useRemoteBasketEnabled();
  const queryClient = useQueryClient();

  // Every mutation sends the known version; a 412 means another tab changed the basket,
  // so reload once and replay against the fresh version.
  const mutateRemote = useCallback(async (mutation: (version?: number) => Promise<Basket>) => {
    const versionOf = (basket?: Basket) => basket?.basketId ? basket.version : undefined;
    try {
      const next = await mutation(versionOf(queryClient.getQueryData<Basket>(basketQueryKey)));
      queryClient.setQueryData(basketQueryKey, next);
      return next;
    } catch (error) {
      if (!(error instanceof ApiError && error.status === 412)) throw error;
      const fresh = await queryClient.fetchQuery({ queryKey: basketQueryKey, queryFn: getBasket });
      const next = await mutation(versionOf(fresh));
      queryClient.setQueryData(basketQueryKey, next);
      return next;
    }
  }, [queryClient]);

  return useMemo(() => ({
    remote,
    add: async (line: CartLine) => {
      rememberSkuImage(line.sku, line.image);
      if (!remote) return addCartLine(line);
      await mutateRemote((version) => addBasketItem(line.sku, clampQuantity(line.quantity), version));
    },
    setQuantity: async (sku: string, quantity: number) => {
      if (!remote) return updateCartLineQuantity(sku, quantity);
      await mutateRemote((version) => setBasketItemQuantity(sku, clampQuantity(quantity), version));
    },
    remove: async (sku: string) => {
      if (!remote) return removeCartLine(sku);
      await mutateRemote((version) => removeBasketItem(sku, version));
    },
    clear: async () => {
      if (!remote) return clearCart();
      await mutateRemote((version) => clearBasket(version));
    },
    setCoupons: async (codes: string[]) => {
      if (!remote) throw new Error('登录后才能使用优惠码。');
      await mutateRemote((version) => replaceBasketCoupons(codes, version));
    },
  }), [mutateRemote, remote]);
}

/** Moves items collected while signed out into the server basket once a session exists. */
export function GuestBasketMerger() {
  const remote = useRemoteBasketEnabled();
  const { add } = useBasketActions();
  const toast = useToast();
  const running = useRef(false);

  useEffect(() => {
    if (!remote || running.current || !linesSnapshot.length) return;
    running.current = true;
    const pending = [...linesSnapshot];
    void (async () => {
      let merged = 0;
      for (const line of pending) {
        try {
          await add(line);
          merged += 1;
        } catch {
          // Products that are no longer sellable are dropped rather than blocking the rest.
        }
      }
      clearCart();
      running.current = false;
      if (merged) toast(`已将登录前加入的 ${merged} 件商品合并到购物袋`);
      if (merged < pending.length) toast(`${pending.length - merged} 件商品已下架或暂不可售，未能加入购物袋`, { tone: 'info' });
    })();
  }, [add, remote, toast]);

  return null;
}
