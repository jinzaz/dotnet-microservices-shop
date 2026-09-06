import { apiRequest } from '../../shared/api/http';
import { serviceRoutes } from '../../shared/config/api';
import type { CatalogProduct } from './types';

export function listProducts(take = 4): Promise<CatalogProduct[]> {
  return apiRequest<CatalogProduct[]>(
    `${serviceRoutes.catalog}/api/products?skip=0&take=${take}`,
  );
}
