import type { OrderDetails } from '../orders/types';
import type { PaymentChannel, PaymentDetails, PaymentStatus } from './types';

const DEMO_ORDER_NUMBER = 'MSA202609070018';

export function createDemoOrder(orderId: string, channel: PaymentChannel): OrderDetails {
  const now = new Date();
  return {
    id: orderId,
    orderNumber: DEMO_ORDER_NUMBER,
    subtotalAmount: 299,
    discountAmount: 52.8,
    amount: 246.2,
    currency: 'CNY',
    paymentMethod: channel,
    status: 'Submitted',
    fulfillmentStatus: 'PendingPayment',
    paymentDueOnUtc: new Date(now.getTime() + 15 * 60_000).toISOString(),
    lines: [
      {
        sku: 'MSA-HOME-001-600-WALNUT',
        productName: '手冲咖啡套装',
        quantity: 1,
        listUnitPrice: 299,
        unitPrice: 299,
        lineSubtotal: 299,
        discountAmount: 52.8,
        lineAmount: 246.2,
      },
    ],
    createdOnUtc: now.toISOString(),
    updatedOnUtc: now.toISOString(),
  };
}

export function createDemoPayment(
  orderId: string,
  channel: PaymentChannel,
  expiresOnUtc: string,
  status: PaymentStatus = 'Pending',
): PaymentDetails {
  return {
    id: `demo-payment-${orderId}`,
    orderId,
    amount: 246.2,
    currency: 'CNY',
    channel,
    status,
    paymentUrl: `https://example.com/msa-store/pay/${encodeURIComponent(orderId)}?channel=${channel}`,
    expiresOnUtc,
  };
}
