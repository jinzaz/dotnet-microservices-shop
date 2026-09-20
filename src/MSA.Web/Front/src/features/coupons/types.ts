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

export type ClaimState = 'NotStarted' | 'Claimable' | 'SoldOut' | 'DailySoldOut' | 'LimitReached' | 'DailyLimitReached' | 'Ended';

/** Mirrors Catalog's StorefrontCouponCampaignDto: a coupon shoppers can claim into their wallet. */
export type ClaimableCampaign = {
  id: string;
  title: string;
  description: string | null;
  promotionId: string;
  promotionName: string;
  rules: PromotionRule[];
  benefits: PromotionBenefit[];
  targets: PromotionTarget[];
  claimStartsOnUtc: string;
  claimEndsOnUtc: string;
  totalQuantity: number | null;
  claimedQuantity: number;
  dailyQuantity: number | null;
  claimedToday: number;
  perUserLimit: number;
  perUserDailyLimit: number | null;
  myClaimedCount: number;
  myClaimedToday: number;
  validityMode: 'Fixed' | 'RelativeToClaim';
  validFromUtc: string | null;
  validUntilUtc: string | null;
  validDays: number | null;
  usesPerCoupon: number;
  claimState: ClaimState;
};

export type ClaimResult = {
  couponId: string;
  maskedCode: string;
  validFromUtc: string;
  validUntilUtc: string;
  remainingQuantity: number | null;
  myClaimedCount: number;
  perUserLimit: number;
};
