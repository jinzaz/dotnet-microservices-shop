import { apiRequest } from '../../shared/api/http';
import { serviceRoutes } from '../../shared/config/api';
import type { OrderFulfillmentStatus, OrderListItem } from './types';

export function listOrders(fulfillmentStatus?: OrderFulfillmentStatus): Promise<OrderListItem[]> {
  const parameters = new URLSearchParams({ skip: '0', take: '50' });
  if (fulfillmentStatus) parameters.set('fulfillmentStatus', fulfillmentStatus);

  return apiRequest<OrderListItem[]>(
    `${serviceRoutes.ordering}/api/orders?${parameters.toString()}`,
  );
}
