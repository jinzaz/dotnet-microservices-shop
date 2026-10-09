export type Paged<T> = {
  items: T[];
  skip: number;
  take: number;
  total: number;
};

export type ProductVariant = {
  id: string;
  productId: string;
  sku: string;
  displayName: string;
  attributesJson: string;
  price: number;
  currency: string;
  isActive: boolean;
  isDefault: boolean;
};

export type ProductMedia = {
  id: string;
  productId: string;
  variantId: string | null;
  mediaType: string;
  url: string;
  thumbnailUrl: string | null;
  altText: string | null;
  sortOrder: number;
};

/** Mirrors Catalog ProductDto; list and detail share the same shape. */
export type CatalogProduct = {
  id: string;
  categoryId: string;
  brandId?: string | null;
  sku: string;
  name: string;
  description: string;
  price: number;
  currency: string;
  isActive: boolean;
  updatedOnUtc: string;
  lifecycleStatus?: string;
  variants?: ProductVariant[] | null;
  media?: ProductMedia[] | null;
  minPrice?: number | null;
  maxPrice?: number | null;
};

export type CatalogCategory = {
  id: string;
  code: string;
  name: string;
};

export type CatalogBrand = CatalogCategory;

export type DisplayProduct = CatalogProduct & {
  categoryLabel: string;
  image: string;
  /** Search results only: the name with hit markers, and the variant count (they carry no variant list). */
  nameHighlight?: string | null;
  variantCount?: number;
};
