import { apiRequest } from '../../shared/api/http';
import { serviceRoutes } from '../../shared/config/api';
import type { ProductSku } from './product-detail-types';
import type { CatalogProduct } from './types';

export function listProducts(take = 4): Promise<CatalogProduct[]> {
  return apiRequest<CatalogProduct[]>(
    `${serviceRoutes.catalog}/products?skip=0&take=${take}`,
  );
}

export function getProduct(productId: string): Promise<CatalogProduct> {
  return apiRequest<CatalogProduct>(
    `${serviceRoutes.catalog}/products/${encodeURIComponent(productId)}`,
  );
}

export function listProductSkus(productId: string): Promise<ProductSku[]> {
  return apiRequest<ProductSku[]>(
    `${serviceRoutes.catalog}/products/${encodeURIComponent(productId)}/skus`,
  );
}
