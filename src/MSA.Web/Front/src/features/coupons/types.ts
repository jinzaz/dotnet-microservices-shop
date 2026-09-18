import type { PromotionBenefit, PromotionRule, PromotionTarget } from '../promotions/types';

export type WalletCouponStatus = 'Available' | 'NotStarted' | 'Used' | 'Expired' | 'Unavailable';

/**
 * Mirrors Ordering's WalletCouponDto. Issued coupons are applied by couponId because the
 * plaintext code is never stored; public codes the buyer typed before show up once redeemed.
 */
export type WalletCoupon = {
  couponId: string;
  promotionId: string;
  promotionName: string;
  maskedCode: string;
  isAssigned: boolean;
  status: WalletCouponStatus;
  validFromUtc: string;
  validUntilUtc: string;
  usedCount: number;
  perUserLimit: number;
  lastUsedOnUtc: string | null;
  rules: PromotionRule[];
  benefits: PromotionBenefit[];
  targets: PromotionTarget[];
};
