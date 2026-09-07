import { apiRequest } from '../../shared/api/http';
import { serviceRoutes } from '../../shared/config/api';
import type { Basket } from './types';

const basketBase = `${serviceRoutes.basket}/api/basket`;

export function getBasket(): Promise<Basket> {
  return apiRequest<Basket>(`${basketBase}/`);
}

export function addBasketItem(sku: string, quantity: number): Promise<Basket> {
  return apiRequest<Basket>(`${basketBase}/items`, {
    method: 'POST',
    body: { sku, quantity },
  });
}

export function setBasketItemQuantity(sku: string, quantity: number, version?: number): Promise<Basket> {
  return apiRequest<Basket>(`${basketBase}/items/${encodeURIComponent(sku)}`, {
    method: 'PUT',
    headers: version === undefined ? undefined : { 'If-Match': `"${version}"` },
    body: { quantity },
  });
}

export function removeBasketItem(sku: string, version?: number): Promise<Basket> {
  return apiRequest<Basket>(`${basketBase}/items/${encodeURIComponent(sku)}`, {
    method: 'DELETE',
    headers: version === undefined ? undefined : { 'If-Match': `"${version}"` },
  });
}

export function replaceBasketCoupons(couponCodes: string[], version?: number): Promise<Basket> {
  return apiRequest<Basket>(`${basketBase}/coupons`, {
    method: 'PUT',
    headers: version === undefined ? undefined : { 'If-Match': `"${version}"` },
    body: { couponCodes },
  });
}
