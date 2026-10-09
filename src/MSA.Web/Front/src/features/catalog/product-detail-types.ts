import type { StorefrontPromotion } from '../promotions/types';
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
  /** Brand name for the breadcrumb and specifications; absent for unbranded products. */
  brandLabel?: string;
  images: string[];
  longDescription: string;
  specifications: ProductSpecification[];
  skus: ProductSku[];
  /** Automatic promotions whose targets include this product. */
  promotions: StorefrontPromotion[];
  reviews: ProductReviewResult;
};
