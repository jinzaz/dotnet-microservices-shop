import type { CustomerCoupon } from '../coupons/types';
import type { ProductReviewResult } from '../reviews/types';
import type { DisplayProduct } from './types';

export type ProductSku = {
  sku: string;
  variantId?: string;
  displayName?: string;
  image?: string;
  price: number;
  currency: string;
  attributes: Record<string, string>;
  availableQuantity?: number;
};

export type ProductSpecification = {
  label: string;
  value: string;
};

export type ProductDetail = DisplayProduct & {
  images: string[];
  longDescription: string;
  specifications: ProductSpecification[];
  skus: ProductSku[];
  coupons: CustomerCoupon[];
  reviews: ProductReviewResult;
};
