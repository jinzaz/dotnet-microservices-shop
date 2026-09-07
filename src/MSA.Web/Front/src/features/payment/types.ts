export type PaymentChannel = 'Alipay' | 'WeChatPay';

export type PaymentStatus =
  | 'Pending'
  | 'Succeeded'
  | 'Failed'
  | 'Refunding'
  | 'Refunded'
  | 'Closed'
  | 'Expired'
  | 'Cancelling';

export type PaymentDetails = {
  id: string;
  orderId: string;
  amount: number;
  currency: string;
  channel: PaymentChannel;
  status: PaymentStatus;
  paymentUrl: string | null;
  expiresOnUtc: string;
};
