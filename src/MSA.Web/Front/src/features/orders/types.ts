export type OrderFulfillmentStatus =
  | 'PendingPayment'
  | 'AwaitingShipment'
  | 'Shipped'
  | 'InTransit'
  | 'Delivered'
  | 'Cancelled';

export type OrderFilter = 'all' | OrderFulfillmentStatus;

export type OrderListItem = {
  id: string;
  orderNumber: string;
  userId: string;
  amount: number;
  currency: string;
  orderStatus: string;
  fulfillmentStatus: OrderFulfillmentStatus;
  recipientName: string;
  phoneNumber: string;
  carrierName: string | null;
  trackingNumber: string | null;
  createdOnUtc: string;
  updatedOnUtc: string;
};
