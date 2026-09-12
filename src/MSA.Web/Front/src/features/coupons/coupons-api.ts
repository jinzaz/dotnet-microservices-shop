import { apiRequest } from '../../shared/api/http';
import { serviceRoutes } from '../../shared/config/api';
import type { CouponStatus, CustomerCoupon, RedeemCouponResponse } from './types';

export const couponApiPaths = {
  myCoupons: `${serviceRoutes.catalog}/coupons/me`,
  redeem: `${serviceRoutes.catalog}/coupons/me/redeem`,
  claim: (couponId: string) => `${serviceRoutes.catalog}/coupons/${couponId}/claim`,
  availableForProduct: (productId: string) =>
    `${serviceRoutes.catalog}/coupons/available?productId=${encodeURIComponent(productId)}`,
} as const;

export function listMyCoupons(status?: CouponStatus): Promise<CustomerCoupon[]> {
  const parameters = status ? `?status=${encodeURIComponent(status)}` : '';
  return apiRequest<CustomerCoupon[]>(`${couponApiPaths.myCoupons}${parameters}`);
}

export function listProductCoupons(productId: string): Promise<CustomerCoupon[]> {
  return apiRequest<CustomerCoupon[]>(couponApiPaths.availableForProduct(productId));
}

export function redeemCoupon(code: string): Promise<RedeemCouponResponse> {
  return apiRequest<RedeemCouponResponse>(couponApiPaths.redeem, {
    method: 'POST',
    body: { code },
  });
}

export function claimCoupon(couponId: string): Promise<CustomerCoupon> {
  return apiRequest<CustomerCoupon>(couponApiPaths.claim(couponId), { method: 'POST' });
}
