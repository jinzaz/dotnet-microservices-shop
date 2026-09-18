import { apiRequest } from '../../shared/api/http';
import { serviceRoutes } from '../../shared/config/api';
import type { WalletCoupon } from './types';

export function listMyCoupons(): Promise<WalletCoupon[]> {
  return apiRequest<WalletCoupon[]>(`${serviceRoutes.ordering}/coupons/mine`);
}

export const myCouponsQueryKey = ['ordering', 'my-coupons'] as const;

/** Wallet coupons that can be applied right now and still have uses left. */
export function usableCoupons(coupons: WalletCoupon[] | undefined): WalletCoupon[] {
  return (coupons ?? []).filter((coupon) => coupon.isAssigned && coupon.status === 'Available');
}
