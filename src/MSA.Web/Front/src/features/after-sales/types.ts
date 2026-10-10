/** After-sales contracts served by the ordering service under /order (see after-sales-api.ts). */
export type AfterSalesType = 'RefundOnly' | 'ReturnAndRefund' | 'Exchange' | 'Reship';

export type AfterSalesStatus =
  | 'Requested'
  | 'Approved'
  | 'Rejected'
  | 'Cancelled'
  | 'AwaitingReturn'
  | 'ReturnInTransit'
  | 'ReturnReceived'
  | 'Refunding'
  | 'RefundFailed'
  | 'Refunded'
  | 'ReplacementShipped'
  | 'Completed';

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
  /** Private-bucket object keys returned by uploadEvidence. */
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

export type AfterSalesShipment = {
  carrierCode: string | null;
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
  /** Short-lived signed URLs; refetch the request to renew them. */
  evidenceUrls: string[];
  returnAddress: string | null;
  returnShipment: AfterSalesShipment | null;
  replacementShipment: AfterSalesShipment | null;
  rejectionReason: string | null;
  /** When the current step is handled automatically if nobody acts (auto-approve, auto-close, auto-complete…). */
  deadlineOnUtc: string | null;
  history: AfterSalesHistory[];
  createdOnUtc: string;
  updatedOnUtc: string;
};

export type AfterSalesSummary = Pick<AfterSalesRequest,
  'id' | 'requestNumber' | 'orderId' | 'orderNumber' | 'type' | 'status' | 'refundAmount' | 'currency' | 'createdOnUtc' | 'updatedOnUtc'> & {
  productNames: string[];
};
