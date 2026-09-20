import type { ClaimableCampaign, WalletCoupon } from './types';

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

const inHours = (hours: number) => new Date(Date.now() + hours * 3_600_000).toISOString();

/** Development preview coupon center (?demo=1); real data comes from Catalog. */
export const demoCampaigns: ClaimableCampaign[] = [
  {
    id: 'demo-campaign-20', title: '新人专享 满 99 减 20', description: '限量发放，先到先得', promotionId: 'demo-promo-20', promotionName: '新人礼',
    rules: minimum(99), benefits: fixed(20), targets: everything, claimStartsOnUtc: inHours(-2), claimEndsOnUtc: inHours(72),
    totalQuantity: 1000, claimedQuantity: 638, dailyQuantity: null, claimedToday: 120, perUserLimit: 1, perUserDailyLimit: null,
    myClaimedCount: 0, myClaimedToday: 0, validityMode: 'RelativeToClaim', validFromUtc: null, validUntilUtc: null, validDays: 7, usesPerCoupon: 1, claimState: 'Claimable',
  },
  {
    id: 'demo-campaign-flash', title: '20 点整点抢 · 全场 8.5 折', description: null, promotionId: 'demo-promo-85', promotionName: '整点秒券',
    rules: [], benefits: percent(15, 80), targets: everything, claimStartsOnUtc: inHours(3), claimEndsOnUtc: inHours(5),
    totalQuantity: 200, claimedQuantity: 0, dailyQuantity: null, claimedToday: 0, perUserLimit: 1, perUserDailyLimit: null,
    myClaimedCount: 0, myClaimedToday: 0, validityMode: 'RelativeToClaim', validFromUtc: null, validUntilUtc: null, validDays: 1, usesPerCoupon: 1, claimState: 'NotStarted',
  },
  {
    id: 'demo-campaign-daily', title: '每日领 满 199 减 30', description: '每天 0 点补货', promotionId: 'demo-promo-30', promotionName: '日日领',
    rules: minimum(199), benefits: fixed(30), targets: everything, claimStartsOnUtc: inHours(-48), claimEndsOnUtc: inHours(24 * 10),
    totalQuantity: null, claimedQuantity: 2140, dailyQuantity: 300, claimedToday: 300, perUserLimit: 5, perUserDailyLimit: 1,
    myClaimedCount: 2, myClaimedToday: 1, validityMode: 'RelativeToClaim', validFromUtc: null, validUntilUtc: null, validDays: 3, usesPerCoupon: 1, claimState: 'DailyLimitReached',
  },
];
