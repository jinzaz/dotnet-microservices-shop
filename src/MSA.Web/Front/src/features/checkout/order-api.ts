import { apiRequest } from '../../shared/api/http';
import { serviceRoutes } from '../../shared/config/api';

export type PaymentMethod = 'Alipay' | 'WeChatPay';

export type ShippingAddressInput = {
  addressId: string | null;
  recipientName: string;
  phoneNumber: string;
  countryCode: string;
  province: string;
  city: string;
  district: string;
  streetLine1: string;
  streetLine2: string | null;
  postalCode: string | null;
};

export type CreateOrderRequest = {
  items: Array<{ sku: string; quantity: number }>;
  paymentMethod: PaymentMethod;
  shippingAddress: ShippingAddressInput;
  couponCodes: string[];
  sourceBasketId?: string;
  sourceBasketVersion?: number;
};

export type CreateOrderResponse = {
  orderId: string;
};

export function createOrder(request: CreateOrderRequest, idempotencyKey: string): Promise<CreateOrderResponse> {
  return apiRequest<CreateOrderResponse>(`${serviceRoutes.ordering}/api/orders`, {
    method: 'POST',
    headers: { 'Idempotency-Key': idempotencyKey },
    body: request,
  });
}
