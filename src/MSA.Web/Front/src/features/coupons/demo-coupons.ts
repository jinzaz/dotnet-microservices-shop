import type { WalletCoupon } from './types';

const fixed = (amount: number) => [{ benefitType: 'FixedAmountOff', schemaVersion: 1, configurationJson: JSON.stringify({ amount }) }];
const percent = (percentage: number, maximumDiscount?: number) => [{
  benefitType: 'PercentageOff',
  schemaVersion: 1,
  configurationJson: JSON.stringify(maximumDiscount ? { percentage, maximumDiscount } : { percentage }),
}];
const minimum = (amount: number) => [{ ruleType: 'MinimumOrderAmount', schemaVersion: 1, configurationJson: JSON.stringify({ amount }) }];
const everything = [{ targetType: 'All', targetKey: null, isExcluded: false }];

/** Development preview wallet (?demo=1); real data comes from Ordering. */
export const demoCoupons: WalletCoupon[] = [
  {
    couponId: 'demo-coupon-40', promotionId: 'demo-promo-40', promotionName: '新人礼 · 满 299 减 40', maskedCode: '****X299',
    isAssigned: true, status: 'Available', validFromUtc: '2026-09-01T00:00:00Z', validUntilUtc: '2026-10-31T15:59:59Z',
    usedCount: 0, perUserLimit: 1, lastUsedOnUtc: null, rules: minimum(299), benefits: fixed(40), targets: everything,
  },
  {
    couponId: 'demo-coupon-90', promotionId: 'demo-promo-90', promotionName: '会员日全场 9 折', maskedCode: '****0OFF',
    isAssigned: true, status: 'Available', validFromUtc: '2026-09-01T00:00:00Z', validUntilUtc: '2026-09-25T15:59:59Z',
    usedCount: 0, perUserLimit: 2, lastUsedOnUtc: null, rules: [], benefits: percent(10, 50), targets: everything,
  },
  {
    couponId: 'demo-coupon-used', promotionId: 'demo-promo-10', promotionName: '满 100 减 10', maskedCode: '****USED',
    isAssigned: true, status: 'Used', validFromUtc: '2026-07-01T00:00:00Z', validUntilUtc: '2026-08-31T15:59:59Z',
    usedCount: 1, perUserLimit: 1, lastUsedOnUtc: '2026-08-12T03:20:00Z', rules: minimum(100), benefits: fixed(10), targets: everything,
  },
  {
    couponId: 'demo-coupon-expired', promotionId: 'demo-promo-30', promotionName: '夏日满 299 减 30', maskedCode: '****3OLD',
    isAssigned: true, status: 'Expired', validFromUtc: '2026-05-01T00:00:00Z', validUntilUtc: '2026-06-30T15:59:59Z',
    usedCount: 0, perUserLimit: 1, lastUsedOnUtc: null, rules: minimum(299), benefits: fixed(30), targets: everything,
  },
];
