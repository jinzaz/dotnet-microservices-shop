import { apiRequest } from '../../shared/api/http';
import { serviceRoutes } from '../../shared/config/api';
import type { Basket } from './types';

const basketBase = serviceRoutes.basket;

function ifMatch(version?: number): HeadersInit | undefined {
  return version === undefined ? undefined : { 'If-Match': `"${version}"` };
}

export function getBasket(): Promise<Basket> {
  return apiRequest<Basket>(`${basketBase}/`);
}

export function addBasketItem(sku: string, quantity: number, version?: number): Promise<Basket> {
  return apiRequest<Basket>(`${basketBase}/items`, {
    method: 'POST',
    headers: ifMatch(version),
    body: { sku, quantity },
  });
}

export function setBasketItemQuantity(sku: string, quantity: number, version?: number): Promise<Basket> {
  return apiRequest<Basket>(`${basketBase}/items/${encodeURIComponent(sku)}`, {
    method: 'PUT',
    headers: ifMatch(version),
    body: { quantity },
  });
}

export function removeBasketItem(sku: string, version?: number): Promise<Basket> {
  return apiRequest<Basket>(`${basketBase}/items/${encodeURIComponent(sku)}`, {
    method: 'DELETE',
    headers: ifMatch(version),
  });
}

export function replaceBasketCoupons(couponCodes: string[], version?: number): Promise<Basket> {
  return apiRequest<Basket>(`${basketBase}/coupons`, {
    method: 'PUT',
    headers: ifMatch(version),
    body: { couponCodes },
  });
}

export function clearBasket(version?: number): Promise<Basket> {
  return apiRequest<Basket>(`${basketBase}/`, {
    method: 'DELETE',
    headers: ifMatch(version),
  });
}
