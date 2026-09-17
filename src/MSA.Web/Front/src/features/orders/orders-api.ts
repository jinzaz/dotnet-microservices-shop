import { apiRequest } from '../../shared/api/http';
import { serviceRoutes } from '../../shared/config/api';
import type { CancelOrderResult, OrderDetails, OrderFulfillmentStatus, OrderListItem } from './types';

export const ORDERS_PAGE_SIZE = 10;

export function listOrders(fulfillmentStatus?: OrderFulfillmentStatus, skip = 0, take = ORDERS_PAGE_SIZE): Promise<OrderListItem[]> {
  const parameters = new URLSearchParams({ skip: String(skip), take: String(take) });
  if (fulfillmentStatus) parameters.set('fulfillmentStatus', fulfillmentStatus);
  return apiRequest<OrderListItem[]>(`${serviceRoutes.ordering}/orders?${parameters}`);
}

export function getOrder(orderId: string): Promise<OrderDetails> {
  return apiRequest<OrderDetails>(`${serviceRoutes.ordering}/orders/${encodeURIComponent(orderId)}`);
}

/** Only unpaid orders can be cancelled; paid orders go through after-sales. */
export function cancelOrder(orderId: string, reason: string | null): Promise<CancelOrderResult> {
  return apiRequest<CancelOrderResult>(`${serviceRoutes.ordering}/orders/${encodeURIComponent(orderId)}/cancel`, {
    method: 'POST',
    body: { reason },
  });
}
