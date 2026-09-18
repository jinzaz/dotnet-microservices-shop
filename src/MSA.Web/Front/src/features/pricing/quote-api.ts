import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { ApiError } from '../../shared/api/api-error';
import { apiRequest } from '../../shared/api/http';
import { serviceRoutes } from '../../shared/config/api';
import type { PromotionBenefit } from '../promotions/types';

export type QuoteRequest = {
  items: Array<{ sku: string; quantity: number }>;
  couponCodes?: string[];
  walletCouponIds?: string[];
};

export type QuoteCoupon = {
  key: string;
  couponId: string | null;
  maskedCode: string | null;
  applied: boolean;
  discountAmount: number;
  reason: string | null;
};

export type OrderQuote = {
  lines: Array<{ sku: string; productName: string; variantName: string; quantity: number; unitPrice: number; lineSubtotal: number }>;
  unavailableSkus: string[];
  currency: string;
  subtotal: number;
  discountTotal: number;
  total: number;
  discounts: Array<{ promotionId: string; name: string; sourceType: string; couponId: string | null; maskedCode: string | null; amount: number }>;
  coupons: QuoteCoupon[];
  opportunities: Array<{ promotionId: string; name: string; missingAmount: number | null; missingQuantity: number | null; benefits: PromotionBenefit[] }>;
};

export function quoteOrder(request: QuoteRequest): Promise<OrderQuote> {
  return apiRequest<OrderQuote>(`${serviceRoutes.ordering}/orders/quote`, { method: 'POST', body: request });
}

const reasonText: Record<string, string> = {
  Unknown: '优惠码不存在或不属于当前账户',
  Duplicate: '同一张券不能重复使用',
  NotAvailable: '券未生效、已停用或已过期',
  SamePromotion: '同一活动的券只能使用一张',
  PromotionInactive: '券所属活动已结束或暂停',
  TargetMismatch: '当前商品不在券的适用范围内',
  ConditionsNotMet: '未达到券的使用门槛',
  ExclusiveGroup: '与已享受的同类优惠互斥',
  NoDiscount: '当前订单无法再享受该券的优惠',
  NotApplied: '与其他优惠不能同时使用',
  UsageLimitReached: '已达到该券的使用次数上限',
  QuotaExhausted: '券已被领完',
  Conflict: '优惠数量超过限制',
};

export function couponReasonText(reason: string | null | undefined): string {
  return (reason && reasonText[reason]) ?? '当前订单无法使用该券';
}

/** Reads the Ordering 409 payload so checkout failures name the coupon problem instead of a generic conflict. */
export function couponRejectionOf(error: unknown): string | null {
  if (!(error instanceof ApiError) || error.status !== 409) return null;
  const reason = (error.problem as { couponReason?: string } | undefined)?.couponReason;
  return reason ? couponReasonText(reason) : null;
}

/** Live price preview; stale data stays on screen while quantities or coupons change. */
export function useOrderQuote(request: QuoteRequest, enabled: boolean) {
  const normalized = {
    items: [...request.items].sort((left, right) => left.sku.localeCompare(right.sku)),
    couponCodes: [...(request.couponCodes ?? [])].sort(),
    walletCouponIds: [...(request.walletCouponIds ?? [])].sort(),
  };
  return useQuery({
    queryKey: ['ordering', 'quote', normalized],
    queryFn: () => quoteOrder(normalized),
    enabled: enabled && normalized.items.length > 0,
    placeholderData: keepPreviousData,
    staleTime: 10_000,
    retry: false,
  });
}
