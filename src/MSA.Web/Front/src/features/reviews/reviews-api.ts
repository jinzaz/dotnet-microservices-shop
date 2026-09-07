import { apiRequest } from '../../shared/api/http';
import { serviceRoutes } from '../../shared/config/api';
import type { ProductReviewResult } from './types';

export const reviewApiPaths = {
  productReviews: (productId: string) =>
    `${serviceRoutes.review}/api/products/${encodeURIComponent(productId)}/reviews`,
} as const;

export function listProductReviews(productId: string): Promise<ProductReviewResult> {
  return apiRequest<ProductReviewResult>(
    `${reviewApiPaths.productReviews(productId)}?skip=0&take=20&sort=latest`,
    { auth: false },
  );
}
