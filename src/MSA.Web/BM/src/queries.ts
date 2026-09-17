import { queryOptions } from '@tanstack/react-query';
import { api, query, type Paged } from './api';

export type StatusCount = { status: string; count: number };
export type MoneyTotal = { currency: string; amount: number };
export type OrdersSummary = {
  totalOrders: number;
  orderStatuses: StatusCount[];
  fulfillmentStatuses: StatusCount[];
  trend: Array<{ date: string; orderCount: number; paidOrderCount: number; paidAmounts: MoneyTotal[] }>;
  generatedOnUtc: string;
};
export type PaymentSummary = {
  fromUtc: string;
  totalCount: number;
  statuses: Array<{ status: string; currency: string; count: number; amount: number; refundedAmount: number }>;
  generatedOnUtc: string;
};
export type StockRow = { id: string; sku: string; onHand: number; reserved: number; available: number; version: number };

/** Shared with the dashboard, the navigation badge and the order status chips so they reuse one request. */
export const ordersSummaryQuery = (days = 7) => queryOptions({
  queryKey: ['orders', 'summary', days],
  queryFn: () => api<OrdersSummary>(`/api/order/admin/orders/summary?${query({ days, utcOffsetMinutes: -new Date().getTimezoneOffset() })}`),
  staleTime: 30_000,
  refetchInterval: 60_000,
});

export const paymentsSummaryQuery = (days = 7) => queryOptions({
  queryKey: ['payments', 'summary', days],
  queryFn: () => api<PaymentSummary>(`/api/payment/admin/payments/summary?days=${days}`),
  staleTime: 30_000,
  refetchInterval: 60_000,
});

export const LOW_STOCK_THRESHOLD = 10;

export const lowStockQuery = (take = 6) => queryOptions({
  queryKey: ['inventory', 'stock', 'low', take],
  queryFn: () => api<Paged<StockRow>>(`/api/inventory/admin/stock?${query({ lowStockThreshold: LOW_STOCK_THRESHOLD, skip: 0, take })}`),
  staleTime: 30_000,
});

export function countOf(statuses: StatusCount[] | undefined, status: string) {
  return statuses?.find((item) => item.status === status)?.count ?? 0;
}
