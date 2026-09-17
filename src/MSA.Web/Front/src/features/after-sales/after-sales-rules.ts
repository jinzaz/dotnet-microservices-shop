import type { OrderDetails } from '../orders/types';
import type { AfterSalesReasonCode, AfterSalesStatus, AfterSalesType } from './types';

/** Days after delivery during which returns and exchanges are accepted. The backend stays authoritative. */
export const AFTER_SALES_WINDOW_DAYS = 7;
const DAY_MS = 24 * 60 * 60 * 1_000;

export type AfterSalesEligibility = {
  eligible: boolean;
  types: AfterSalesType[];
  reason: string;
  deadlineUtc: string | null;
};

export function getAfterSalesEligibility(
  order: Pick<OrderDetails, 'status' | 'fulfillmentStatus' | 'deliveredOnUtc' | 'updatedOnUtc'>,
  now = Date.now(),
): AfterSalesEligibility {
  if (order.status !== 'Confirmed') {
    return { eligible: false, types: [], reason: order.fulfillmentStatus === 'PendingPayment' ? '订单尚未付款，可直接取消订单。' : '订单未完成付款，无需售后。', deadlineUtc: null };
  }

  switch (order.fulfillmentStatus) {
    case 'AwaitingShipment':
      return { eligible: true, types: ['RefundOnly'], reason: '商品尚未发货，可申请仅退款。', deadlineUtc: null };
    case 'Shipped':
    case 'InTransit':
      return { eligible: true, types: ['RefundOnly'], reason: '包裹运输中，可申请退款并拒收包裹。', deadlineUtc: null };
    case 'Delivered': {
      // Older orders may miss the delivery timestamp; the last update is the closest proxy.
      const deliveredAt = Date.parse(order.deliveredOnUtc ?? order.updatedOnUtc);
      const deadline = deliveredAt + AFTER_SALES_WINDOW_DAYS * DAY_MS;
      if (Number.isNaN(deliveredAt) || now > deadline) {
        return { eligible: false, types: [], reason: `已超过签收后 ${AFTER_SALES_WINDOW_DAYS} 天的售后期限，如有质量问题请联系客服。`, deadlineUtc: Number.isNaN(deadline) ? null : new Date(deadline).toISOString() };
      }
      return { eligible: true, types: ['ReturnAndRefund', 'Exchange', 'RefundOnly'], reason: '签收后 7 天内可申请退货退款、换货或仅退款。', deadlineUtc: new Date(deadline).toISOString() };
    }
    default:
      return { eligible: false, types: [], reason: '当前订单状态暂不支持售后。', deadlineUtc: null };
  }
}

export const afterSalesTypeLabels: Record<AfterSalesType, { label: string; description: string }> = {
  RefundOnly: { label: '仅退款', description: '未收到货或与商家协商一致，无需退回商品' },
  ReturnAndRefund: { label: '退货退款', description: '寄回商品，商家签收后原路退款' },
  Exchange: { label: '换货', description: '寄回商品，商家重新发出同款商品' },
};

export const afterSalesReasons: ReadonlyArray<{ code: AfterSalesReasonCode; label: string; types: AfterSalesType[] }> = [
  { code: 'NotReceived', label: '未收到货 / 拒收包裹', types: ['RefundOnly'] },
  { code: 'NoLongerNeeded', label: '不想要了（7 天无理由）', types: ['RefundOnly', 'ReturnAndRefund'] },
  { code: 'QualityIssue', label: '商品质量问题', types: ['ReturnAndRefund', 'Exchange', 'RefundOnly'] },
  { code: 'DamagedInTransit', label: '运输途中损坏', types: ['ReturnAndRefund', 'Exchange', 'RefundOnly'] },
  { code: 'WrongItem', label: '商家发错货', types: ['ReturnAndRefund', 'Exchange'] },
  { code: 'NotAsDescribed', label: '与商品描述不符', types: ['ReturnAndRefund', 'Exchange'] },
  { code: 'MissingParts', label: '少件 / 配件缺失', types: ['ReturnAndRefund', 'Exchange', 'RefundOnly'] },
  { code: 'Other', label: '其他原因', types: ['RefundOnly', 'ReturnAndRefund', 'Exchange'] },
];

export const afterSalesStatusLabels: Record<AfterSalesStatus, { label: string; tone: string }> = {
  Requested: { label: '待审核', tone: 'warning' },
  Approved: { label: '已同意', tone: 'info' },
  Rejected: { label: '已拒绝', tone: 'muted' },
  AwaitingReturn: { label: '待寄回商品', tone: 'warning' },
  ReturnInTransit: { label: '退货运输中', tone: 'info' },
  ReturnReceived: { label: '商家已收货', tone: 'info' },
  Refunding: { label: '退款中', tone: 'info' },
  Refunded: { label: '已退款', tone: 'success' },
  ExchangeShipped: { label: '换货已发出', tone: 'info' },
  Completed: { label: '已完成', tone: 'success' },
  Cancelled: { label: '已撤销', tone: 'muted' },
};

/** Refund is proportional to each line's paid amount so order-level discounts are shared fairly. */
export function estimateRefund(lines: ReadonlyArray<{ lineAmount: number; quantity: number; selected: number }>): number {
  const total = lines.reduce((sum, line) => sum + (line.quantity ? line.lineAmount * line.selected / line.quantity : 0), 0);
  return Math.round(total * 100) / 100;
}
