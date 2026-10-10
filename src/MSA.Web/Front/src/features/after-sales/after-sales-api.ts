import { apiRequest } from '../../shared/api/http';
import { serviceRoutes } from '../../shared/config/api';
import type { AfterSalesRequest, AfterSalesSummary, CreateAfterSalesRequest } from './types';

/** Served by the ordering service; the gateway rule /api/order/** forwards them. */
export const afterSalesApiPaths = {
  list: `${serviceRoutes.ordering}/after-sales`,
  detail: (id: string) => `${serviceRoutes.ordering}/after-sales/${encodeURIComponent(id)}`,
  forOrder: (orderId: string) => `${serviceRoutes.ordering}/orders/${encodeURIComponent(orderId)}/after-sales`,
  cancel: (id: string) => `${serviceRoutes.ordering}/after-sales/${encodeURIComponent(id)}/cancel`,
  returnShipment: (id: string) => `${serviceRoutes.ordering}/after-sales/${encodeURIComponent(id)}/return-shipment`,
  confirmReceipt: (id: string) => `${serviceRoutes.ordering}/after-sales/${encodeURIComponent(id)}/confirm-receipt`,
  evidenceUploadUrl: `${serviceRoutes.ordering}/after-sales/evidence/upload-url`,
} as const;

export function listAfterSales(skip = 0, take = 20): Promise<AfterSalesSummary[]> {
  return apiRequest<AfterSalesSummary[]>(`${afterSalesApiPaths.list}?skip=${skip}&take=${take}`);
}

export function getAfterSales(id: string): Promise<AfterSalesRequest> {
  return apiRequest<AfterSalesRequest>(afterSalesApiPaths.detail(id));
}

export function listOrderAfterSales(orderId: string): Promise<AfterSalesSummary[]> {
  return apiRequest<AfterSalesSummary[]>(afterSalesApiPaths.forOrder(orderId));
}

export function createAfterSales(orderId: string, request: CreateAfterSalesRequest, idempotencyKey: string): Promise<AfterSalesRequest> {
  return apiRequest<AfterSalesRequest>(afterSalesApiPaths.forOrder(orderId), {
    method: 'POST',
    headers: { 'Idempotency-Key': idempotencyKey },
    body: request,
  });
}

export function cancelAfterSales(id: string): Promise<AfterSalesRequest> {
  return apiRequest<AfterSalesRequest>(afterSalesApiPaths.cancel(id), { method: 'POST' });
}

export function submitReturnShipment(id: string, carrierName: string, trackingNumber: string): Promise<AfterSalesRequest> {
  return apiRequest<AfterSalesRequest>(afterSalesApiPaths.returnShipment(id), {
    method: 'POST',
    body: { carrierName, trackingNumber },
  });
}

export function confirmAfterSalesReceipt(id: string): Promise<AfterSalesRequest> {
  return apiRequest<AfterSalesRequest>(afterSalesApiPaths.confirmReceipt(id), { method: 'POST' });
}

/** Mirrors the ordering service's evidence policy so bad files fail before a signed URL is requested. */
export const evidencePolicy = {
  accept: 'image/jpeg,image/png,image/webp',
  maxBytes: 10 * 1024 * 1024,
  maxCount: 6,
} as const;

export function validateEvidence(file: File): string | null {
  if (!evidencePolicy.accept.split(',').includes(file.type)) return `“${file.name}” 不是 JPG、PNG 或 WebP 图片。`;
  if (file.size === 0) return `“${file.name}” 是空文件。`;
  if (file.size > evidencePolicy.maxBytes) return `“${file.name}” 超过 10 MB。`;
  return null;
}

type EvidenceUploadTicket = { objectKey: string; uploadUrl: string; headers: Record<string, string> };

/** Uploads one image straight to the private bucket and returns its object key for the create request. */
export async function uploadEvidence(file: File): Promise<string> {
  const invalid = validateEvidence(file);
  if (invalid) throw new Error(invalid);
  const ticket = await apiRequest<EvidenceUploadTicket>(afterSalesApiPaths.evidenceUploadUrl, {
    method: 'POST',
    body: { fileName: file.name, contentType: file.type, contentLength: file.size },
  });
  let response: Response;
  try {
    response = await fetch(ticket.uploadUrl, { method: 'PUT', headers: ticket.headers, body: file });
  } catch {
    throw new Error('图片上传失败，请检查网络后重试。');
  }
  if (!response.ok) throw new Error(`图片上传失败（${response.status}），请重试。`);
  return ticket.objectKey;
}
