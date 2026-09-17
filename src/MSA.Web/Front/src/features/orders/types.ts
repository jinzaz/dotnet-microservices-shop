export type OrderFulfillmentStatus =
  | 'PendingPayment'
  | 'AwaitingShipment'
  | 'Shipped'
  | 'InTransit'
  | 'Delivered'
  | 'Cancelled';

export type OrderStatus = 'Submitted' | 'AwaitingPayment' | 'Confirmed' | 'Cancelled';

export type OrderFilter = 'all' | OrderFulfillmentStatus;

/** Mirrors OrderService OrderListItemDto. */
export type OrderListItem = {
  id: string;
  orderNumber: string;
  userId: string;
  amount: number;
  currency: string;
  orderStatus: OrderStatus | string;
  fulfillmentStatus: OrderFulfillmentStatus;
  recipientName: string;
  phoneNumber: string;
  carrierName: string | null;
  trackingNumber: string | null;
  createdOnUtc: string;
  updatedOnUtc: string;
};

export type OrderLine = {
  productId?: string | null;
  variantId?: string | null;
  sku: string;
  productName: string;
  variantName?: string;
  variantAttributesJson?: string;
  quantity: number;
  listUnitPrice: number;
  unitPrice: number;
  lineSubtotal: number;
  discountAmount: number;
  lineAmount: number;
};

export type OrderDiscount = {
  promotionId: string;
  couponId: string | null;
  sourceType: string;
  description: string;
  amount: number;
};

export type OrderShippingAddress = {
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

export type OrderFulfillmentHistory = {
  fromStatus: string | null;
  toStatus: OrderFulfillmentStatus;
  carrierCode: string | null;
  carrierName: string | null;
  trackingNumber: string | null;
  note: string | null;
  actor: string;
  occurredOnUtc: string;
};

/** Mirrors OrderService OrderDto; optional fields are absent in demo data. */
export type OrderDetails = {
  id: string;
  orderNumber: string;
  subtotalAmount: number;
  discountAmount: number;
  amount: number;
  currency: string;
  paymentMethod: string;
  status: OrderStatus | string;
  fulfillmentStatus: OrderFulfillmentStatus;
  paymentDueOnUtc: string;
  lines: OrderLine[];
  createdOnUtc: string;
  updatedOnUtc: string;
  paymentId?: string | null;
  carrierCode?: string | null;
  carrierName?: string | null;
  trackingNumber?: string | null;
  cancellationReason?: string | null;
  shippingAddress?: OrderShippingAddress;
  discounts?: OrderDiscount[];
  fulfillmentHistory?: OrderFulfillmentHistory[];
  shippedOnUtc?: string | null;
  inTransitOnUtc?: string | null;
  deliveredOnUtc?: string | null;
};

export type CancelOrderResult = {
  orderId: string;
  status: string;
  fulfillmentStatus: OrderFulfillmentStatus;
  changed: boolean;
};
