import { apiRequest } from '../../shared/api/http';
import { serviceRoutes } from '../../shared/config/api';
import type { PaymentDetails } from './types';

export function getPaymentByOrder(orderId: string): Promise<PaymentDetails> {
  return apiRequest<PaymentDetails>(
    `${serviceRoutes.payment}/api/payments/orders/${encodeURIComponent(orderId)}`,
  );
}
