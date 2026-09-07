export type CouponStatus = 'available' | 'used' | 'expired';

export type CustomerCoupon = {
  id: string;
  title: string;
  valueLabel: string;
  conditionLabel: string;
  scopeLabel: string;
  code: string;
  status: CouponStatus;
  validFromUtc: string;
  validUntilUtc: string;
  productId?: string;
  isExpiringSoon?: boolean;
};

export type RedeemCouponResponse = {
  coupon: CustomerCoupon;
};
