import { apiRequest } from '../../shared/api/http';
import { serviceRoutes } from '../../shared/config/api';
import type { StorefrontPromotion } from './types';

export function listStorefrontPromotions(): Promise<StorefrontPromotion[]> {
  return apiRequest<StorefrontPromotion[]>(`${serviceRoutes.catalog}/promotions`);
}

export const storefrontPromotionsQuery = {
  queryKey: ['catalog', 'storefront-promotions'],
  queryFn: listStorefrontPromotions,
  staleTime: 5 * 60_000,
} as const;
