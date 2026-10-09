import { ApiError } from '../../shared/api/api-error';
import { apiRequest } from '../../shared/api/http';
import { serviceRoutes } from '../../shared/config/api';
import { listProducts } from './catalog-api';
import type { CatalogProduct } from './types';

export type SearchSort = 'relevance' | 'price_asc' | 'price_desc' | 'newest';

export type ProductSearchParams = {
  q?: string;
  categoryId?: string;
  minPrice?: number;
  maxPrice?: number;
  sort?: SearchSort;
  skip?: number;
  take?: number;
};

/** Mirrors SearchService ProductSearchItemDto; shaped like CatalogProduct so cards render it unchanged. */
export type ProductSearchItem = CatalogProduct & {
  nameHighlight?: string | null;
  variantCount?: number;
  image?: string | null;
};

export type ProductSearchResult = {
  items: ProductSearchItem[];
  skip: number;
  take: number;
  total: number;
  /** Over 10 000 hits the engine only reports a lower bound. */
  totalIsLowerBound: boolean;
  facets: {
    categories: { id: string; count: number }[];
    minPrice: number | null;
    maxPrice: number | null;
  } | null;
  /** "database" means the search engine was unavailable and the catalog's plain listing answered instead. */
  engine: 'elasticsearch' | 'database';
};

function fromCatalogProduct(product: CatalogProduct): ProductSearchItem {
  return {
    ...product,
    variantCount: product.variants?.filter((variant) => variant.isActive).length ?? 1,
  };
}

/**
 * Search through the search service; when it is down (503 / 5xx / network) fall back to the catalog's
 * own listing so the page keeps working, just without facets, sorting and price filters.
 * Client errors (4xx) are real errors and are not masked.
 */
export async function searchProducts(params: ProductSearchParams): Promise<ProductSearchResult> {
  const { q, categoryId, minPrice, maxPrice, sort, skip = 0, take = 20 } = params;
  const query = new URLSearchParams({ skip: String(skip), take: String(take) });
  if (q?.trim()) query.set('q', q.trim());
  if (categoryId) query.set('categoryId', categoryId);
  if (minPrice !== undefined) query.set('minPrice', String(minPrice));
  if (maxPrice !== undefined) query.set('maxPrice', String(maxPrice));
  if (sort && sort !== 'relevance') query.set('sort', sort);

  try {
    return await apiRequest<ProductSearchResult>(`${serviceRoutes.search}/products?${query}`);
  } catch (error) {
    if (error instanceof ApiError && error.status > 0 && error.status < 500) throw error;
    const page = await listProducts({ skip, take, search: q, categoryId });
    return {
      items: page.items.map(fromCatalogProduct),
      skip: page.skip,
      take: page.take,
      total: page.total,
      totalIsLowerBound: false,
      facets: null,
      engine: 'database',
    };
  }
}

/** Autocomplete is a nicety: any failure simply means no suggestions. */
export function suggestProducts(prefix: string): Promise<string[]> {
  return apiRequest<string[]>(`${serviceRoutes.search}/suggestions?q=${encodeURIComponent(prefix)}`).catch(() => []);
}
