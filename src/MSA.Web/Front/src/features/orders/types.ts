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

export type OrderLine = {
  sku: string;
  productName: string;
  quantity: number;
  listUnitPrice: number;
  unitPrice: number;
  lineSubtotal: number;
  discountAmount: number;
  lineAmount: number;
};

export type OrderDetails = {
  id: string;
  orderNumber: string;
  subtotalAmount: number;
  discountAmount: number;
  amount: number;
  currency: string;
  paymentMethod: string;
  status: string;
  fulfillmentStatus: OrderFulfillmentStatus;
  paymentDueOnUtc: string;
  lines: OrderLine[];
  createdOnUtc: string;
  updatedOnUtc: string;
};
