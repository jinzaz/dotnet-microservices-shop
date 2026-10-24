import type { OrderDetails, OrderFulfillmentStatus, OrderShippingAddress } from './types';

export const statusPresentation: Record<OrderFulfillmentStatus, { label: string; tone: string; hint: string }> = {
  PendingPayment: { label: '待付款', tone: 'warning', hint: '请在支付截止前完成付款，超时订单将自动取消。' },
  AwaitingShipment: { label: '待发货', tone: 'info', hint: '商家正在备货，发货后会更新物流信息。' },
  Shipped: { label: '已发货', tone: 'info', hint: '包裹已交给承运商，正在等待揽收。' },
  InTransit: { label: '运输中', tone: 'info', hint: '包裹正在路上，请保持电话畅通。' },
  Delivered: { label: '已签收', tone: 'success', hint: '订单已完成，签收后 7 天内可申请售后。' },
  Cancelled: { label: '已取消', tone: 'muted', hint: '订单已关闭，如有已付款项将按原路退回。' },
  ReturnedToSender: { label: '已退款', tone: 'muted', hint: '商品已全部退款，包裹将退回商家；如快递员派送，请直接拒收。' },
};

/** Fully refunded orders read as closed even though payment stays confirmed. */
export function isFullyRefunded(order: { amount: number; refundedAmount?: number }): boolean {
  return (order.refundedAmount ?? 0) >= order.amount;
}

/** Status badge for an order: a fully refunded order shows「已退款」whatever its last fulfillment step was. */
export function presentOrderStatus(order: { fulfillmentStatus: string; amount: number; refundedAmount?: number }) {
  const status = presentStatus(order.fulfillmentStatus);
  return isFullyRefunded(order) && order.fulfillmentStatus !== 'ReturnedToSender'
    ? { label: '已退款', tone: 'muted', hint: '订单商品已全部退款，款项已原路退回。' }
    : status;
}

export function presentStatus(status: string) {
  return statusPresentation[status as OrderFulfillmentStatus] ?? { label: status, tone: 'muted', hint: '' };
}

export const fulfillmentSteps: ReadonlyArray<{ status: OrderFulfillmentStatus; label: string }> = [
  { status: 'PendingPayment', label: '提交订单' },
  { status: 'AwaitingShipment', label: '付款成功' },
  { status: 'Shipped', label: '商家发货' },
  { status: 'InTransit', label: '运输中' },
  { status: 'Delivered', label: '确认签收' },
];

export function stepIndexOf(status: OrderFulfillmentStatus): number {
  return fulfillmentSteps.findIndex((step) => step.status === status);
}

export function formatAddress(address: OrderShippingAddress): string {
  return [address.province, address.city, address.district, address.streetLine1, address.streetLine2]
    .filter(Boolean)
    .join(' ');
}

export function canPay(order: Pick<OrderDetails, 'fulfillmentStatus'>): boolean {
  return order.fulfillmentStatus === 'PendingPayment';
}

/** Mirrors the backend rule: only orders that have not been confirmed by payment are cancellable. */
export function canCancel(order: Pick<OrderDetails, 'fulfillmentStatus' | 'status'>): boolean {
  return order.fulfillmentStatus === 'PendingPayment' && order.status !== 'Confirmed' && order.status !== 'Cancelled';
}

export const cancellationReasons = [
  '不想要了',
  '商品信息填写错误',
  '收货地址填写错误',
  '重复下单',
  '想换其他支付方式',
  '其他原因',
] as const;
