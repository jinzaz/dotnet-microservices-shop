import { apiRequest } from '../../shared/api/http';
import { serviceRoutes } from '../../shared/config/api';
import type { PaymentDetails } from './types';

export function getPaymentByOrder(orderId: string): Promise<PaymentDetails> {
  return apiRequest<PaymentDetails>(
    `${serviceRoutes.payment}/orders/${encodeURIComponent(orderId)}`,
  );
}

/** 没收到支付宝异步通知时，让支付服务主动查询一次渠道交易状态。 */
export function syncPayment(orderId: string): Promise<PaymentDetails> {
  return apiRequest<PaymentDetails>(
    `${serviceRoutes.payment}/orders/${encodeURIComponent(orderId)}/sync`,
    { method: 'POST' },
  );
}
