import { apiRequest } from '../../shared/api/http';
import { serviceRoutes } from '../../shared/config/api';
import type { CatalogCategory, CatalogProduct, Paged } from './types';

export type ProductListParams = {
  skip?: number;
  take?: number;
  search?: string;
  categoryId?: string;
};

export function listProducts({ skip = 0, take = 20, search, categoryId }: ProductListParams = {}): Promise<Paged<CatalogProduct>> {
  const parameters = new URLSearchParams({ skip: String(skip), take: String(take) });
  if (search?.trim()) parameters.set('search', search.trim());
  if (categoryId) parameters.set('categoryId', categoryId);
  return apiRequest<Paged<CatalogProduct>>(`${serviceRoutes.catalog}/products?${parameters}`);
}

export function getProduct(productId: string): Promise<CatalogProduct> {
  return apiRequest<CatalogProduct>(`${serviceRoutes.catalog}/products/${encodeURIComponent(productId)}`);
}

export function listCategories(): Promise<CatalogCategory[]> {
  return apiRequest<CatalogCategory[]>(`${serviceRoutes.catalog}/categories`);
}

export const categoriesQuery = {
  queryKey: ['catalog', 'categories'],
  queryFn: listCategories,
  staleTime: 5 * 60_000,
} as const;
