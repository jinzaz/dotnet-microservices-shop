import { apiRequest } from '../../shared/api/http';
import { serviceRoutes } from '../../shared/config/api';
import type { AfterSalesRequest, AfterSalesSummary, CreateAfterSalesRequest } from './types';

/**
 * Reserved endpoints. They live under the ordering route so the existing gateway rule
 * (/api/order/**) forwards them without configuration changes once the service ships.
 * Until then these calls return 404, which pages treat as "coming soon".
 */
export const afterSalesApiPaths = {
  list: `${serviceRoutes.ordering}/after-sales`,
  detail: (id: string) => `${serviceRoutes.ordering}/after-sales/${encodeURIComponent(id)}`,
  forOrder: (orderId: string) => `${serviceRoutes.ordering}/orders/${encodeURIComponent(orderId)}/after-sales`,
  cancel: (id: string) => `${serviceRoutes.ordering}/after-sales/${encodeURIComponent(id)}/cancel`,
  returnShipment: (id: string) => `${serviceRoutes.ordering}/after-sales/${encodeURIComponent(id)}/return-shipment`,
} as const;

export function listAfterSales(skip = 0, take = 20): Promise<AfterSalesSummary[]> {
  return apiRequest<AfterSalesSummary[]>(`${afterSalesApiPaths.list}?skip=${skip}&take=${take}`);
}

export function getAfterSales(id: string): Promise<AfterSalesRequest> {
  return apiRequest<AfterSalesRequest>(afterSalesApiPaths.detail(id));
}

export function listOrderAfterSales(orderId: string): Promise<AfterSalesSummary[]> {
  return apiRequest<AfterSalesSummary[]>(afterSalesApiPaths.forOrder(orderId));
}

export function createAfterSales(orderId: string, request: CreateAfterSalesRequest, idempotencyKey: string): Promise<AfterSalesRequest> {
  return apiRequest<AfterSalesRequest>(afterSalesApiPaths.forOrder(orderId), {
    method: 'POST',
    headers: { 'Idempotency-Key': idempotencyKey },
    body: request,
  });
}

export function cancelAfterSales(id: string): Promise<AfterSalesRequest> {
  return apiRequest<AfterSalesRequest>(afterSalesApiPaths.cancel(id), { method: 'POST' });
}

export function submitReturnShipment(id: string, carrierName: string, trackingNumber: string): Promise<AfterSalesRequest> {
  return apiRequest<AfterSalesRequest>(afterSalesApiPaths.returnShipment(id), {
    method: 'POST',
    body: { carrierName, trackingNumber },
  });
}
