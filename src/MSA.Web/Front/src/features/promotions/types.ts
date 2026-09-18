/** Rule, benefit and target shapes shared by Catalog (storefront promotions) and Ordering (wallet, quote). */
export type PromotionRule = { ruleType: string; schemaVersion: number; configurationJson: string };
export type PromotionBenefit = { benefitType: string; schemaVersion: number; configurationJson: string };
export type PromotionTarget = { targetType: string; targetKey: string | null; isExcluded: boolean };

export type StorefrontPromotion = {
  id: string;
  name: string;
  startsOnUtc: string;
  endsOnUtc: string;
  priority: number;
  stackPolicy: string;
  rules: PromotionRule[];
  benefits: PromotionBenefit[];
  targets: PromotionTarget[];
};
