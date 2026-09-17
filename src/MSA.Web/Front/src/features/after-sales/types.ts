/**
 * After-sales contracts reserved for the upcoming service. The frontend is built against these shapes;
 * the backend should expose them under /order (see after-sales-api.ts) or update both sides together.
 */
export type AfterSalesType = 'RefundOnly' | 'ReturnAndRefund' | 'Exchange';

export type AfterSalesStatus =
  | 'Requested'
  | 'Approved'
  | 'Rejected'
  | 'AwaitingReturn'
  | 'ReturnInTransit'
  | 'ReturnReceived'
  | 'Refunding'
  | 'Refunded'
  | 'ExchangeShipped'
  | 'Completed'
  | 'Cancelled';

export type AfterSalesReasonCode =
  | 'NoLongerNeeded'
  | 'QualityIssue'
  | 'DamagedInTransit'
  | 'WrongItem'
  | 'NotAsDescribed'
  | 'MissingParts'
  | 'NotReceived'
  | 'Other';

export type AfterSalesLineInput = {
  sku: string;
  quantity: number;
};

export type CreateAfterSalesRequest = {
  type: AfterSalesType;
  reasonCode: AfterSalesReasonCode;
  description: string;
  lines: AfterSalesLineInput[];
  /** Object keys returned by a future evidence upload endpoint. */
  evidenceObjectKeys: string[];
};

export type AfterSalesLine = {
  sku: string;
  productName: string;
  variantName: string;
  quantity: number;
  refundAmount: number;
};

export type AfterSalesHistory = {
  status: AfterSalesStatus;
  note: string | null;
  actor: string;
  occurredOnUtc: string;
};

export type ReturnShipment = {
  carrierName: string;
  trackingNumber: string;
  shippedOnUtc: string;
};

export type AfterSalesRequest = {
  id: string;
  requestNumber: string;
  orderId: string;
  orderNumber: string;
  type: AfterSalesType;
  status: AfterSalesStatus;
  reasonCode: AfterSalesReasonCode;
  description: string;
  lines: AfterSalesLine[];
  refundAmount: number;
  currency: string;
  evidenceUrls: string[];
  returnAddress: string | null;
  returnShipment: ReturnShipment | null;
  rejectionReason: string | null;
  history: AfterSalesHistory[];
  createdOnUtc: string;
  updatedOnUtc: string;
};

export type AfterSalesSummary = Pick<AfterSalesRequest,
  'id' | 'requestNumber' | 'orderId' | 'orderNumber' | 'type' | 'status' | 'refundAmount' | 'currency' | 'createdOnUtc' | 'updatedOnUtc'> & {
  productNames: string[];
};
