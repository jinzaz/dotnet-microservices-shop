import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom';
import { listOrderAfterSales } from '../features/after-sales/after-sales-api';
import { afterSalesStatusLabels, afterSalesTypeLabels, getAfterSalesEligibility } from '../features/after-sales/after-sales-rules';
import { useBasketActions } from '../features/basket/basket-store';
import { imageForSku, parseAttributes } from '../features/catalog/product-view';
import { createDemoOrderDetails } from '../features/orders/demo-orders';
import {
  canCancel,
  canPay,
  cancellationReasons,
  formatAddress,
  fulfillmentSteps,
  presentStatus,
  stepIndexOf,
} from '../features/orders/order-presentation';
import { cancelOrder, getOrder } from '../features/orders/orders-api';
import type { OrderDetails, OrderLine } from '../features/orders/types';
import { paymentChannelLabel } from '../features/payment/PaymentProviderSelector';
import { getPaymentByOrder } from '../features/payment/payment-api';
import type { PaymentChannel } from '../features/payment/types';
import { ApiError, getErrorMessage } from '../shared/api/api-error';
import { isDemoMode } from '../shared/config/api';
import { CommerceShell } from '../shared/layout/CommerceShell';
import { Button } from '../shared/ui/Button';
import { ConfirmDialog } from '../shared/ui/ConfirmDialog';
import { Icon } from '../shared/ui/Icon';
import { useToast } from '../shared/ui/Toast';
import { formatDateTime, formatMoney } from '../shared/utils/format';

const paymentStatusLabels: Record<string, string> = {
  Pending: '待支付',
  Succeeded: '支付成功',
  Failed: '支付失败',
  Refunding: '退款中',
  Refunded: '已退款',
  Closed: '已关闭',
  Expired: '已过期',
  Cancelling: '关闭中',
};

function lineVariant(line: OrderLine): string {
  const attributes = Object.values(parseAttributes(line.variantAttributesJson));
  return attributes.length ? attributes.join(' / ') : line.variantName || line.sku;
}

function useCopy() {
  const toast = useToast();
  return async (value: string, label: string) => {
    try {
      await navigator.clipboard.writeText(value);
      toast(`${label}已复制`);
    } catch {
      toast('复制失败，请手动选择文本复制', { tone: 'error' });
    }
  };
}

function OrderProgress({ order }: { order: OrderDetails }) {
  if (order.fulfillmentStatus === 'Cancelled') {
    return (
      <div className="order-cancelled-banner" role="status">
        <Icon name="alert-circle" />
        <div>
          <strong>订单已取消</strong>
          <span>{order.cancellationReason ?? '订单已关闭'}{order.paymentId ? '，已付款项将按原路退回。' : ''}</span>
        </div>
      </div>
    );
  }
  // Delivery is terminal, so every step reads as done rather than leaving the last one "in progress".
  const current = order.fulfillmentStatus === 'Delivered' ? fulfillmentSteps.length : stepIndexOf(order.fulfillmentStatus);
  return (
    <ol className="order-steps" aria-label="订单进度">
      {fulfillmentSteps.map((step, index) => (
        <li key={step.status} className={index < current ? 'is-complete' : index === current ? 'is-current' : undefined} aria-current={index === current ? 'step' : undefined}>
          <span>{index < current ? <Icon name="check" /> : index + 1}</span>
          <strong>{step.label}</strong>
        </li>
      ))}
    </ol>
  );
}

export default function OrderDetailPage() {
  const { orderId = '' } = useParams();
  const location = useLocation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const toast = useToast();
  const copy = useCopy();
  const basket = useBasketActions();
  const demoMode = isDemoMode(location.search);
  const suffix = demoMode ? '?demo=1' : '';
  const [cancelOpen, setCancelOpen] = useState(false);
  const [cancelReason, setCancelReason] = useState<string>(cancellationReasons[0]);
  const [cancelling, setCancelling] = useState(false);
  const [reordering, setReordering] = useState(false);

  const orderQuery = useQuery({
    queryKey: ['ordering', 'order', orderId],
    queryFn: () => getOrder(orderId),
    enabled: Boolean(orderId) && !demoMode,
    staleTime: 15_000,
    retry: (count, error) => !(error instanceof ApiError && error.status === 404) && count < 1,
  });
  const order = demoMode ? createDemoOrderDetails(orderId) : orderQuery.data;
  const paymentQuery = useQuery({
    queryKey: ['payment', 'order', orderId],
    queryFn: () => getPaymentByOrder(orderId),
    enabled: Boolean(order) && !demoMode && order?.status !== 'Submitted',
    retry: false,
    staleTime: 15_000,
  });
  const afterSalesQuery = useQuery({
    queryKey: ['after-sales', 'order', orderId],
    queryFn: () => listOrderAfterSales(orderId),
    enabled: Boolean(order) && !demoMode && order?.status === 'Confirmed',
    retry: false,
    staleTime: 30_000,
  });

  if (!demoMode && orderQuery.isPending) {
    return <CommerceShell><div className="page-state" role="status"><span className="page-spinner" />正在加载订单…</div></CommerceShell>;
  }

  if (!order) {
    const notFound = demoMode || (orderQuery.error instanceof ApiError && orderQuery.error.status === 404);
    return (
      <CommerceShell>
        <div className="page-state">
          <Icon name="receipt" />
          <h1>{notFound ? '没有找到这个订单' : '订单暂时无法打开'}</h1>
          <p>{notFound ? '订单可能不属于当前账户，或链接已失效。' : getErrorMessage(orderQuery.error)}</p>
          <div className="page-state__actions">
            {!notFound ? <Button type="button" onClick={() => void orderQuery.refetch()}>重新加载</Button> : null}
            <Link className="button button--secondary button--regular" to={`/orders${suffix}`}>返回我的订单</Link>
          </div>
        </div>
      </CommerceShell>
    );
  }

  const status = presentStatus(order.fulfillmentStatus);
  const eligibility = getAfterSalesEligibility(order);
  const history = [...(order.fulfillmentHistory ?? [])].sort((left, right) => Date.parse(right.occurredOnUtc) - Date.parse(left.occurredOnUtc));
  const payment = paymentQuery.data;
  const afterSalesRecords = afterSalesQuery.data ?? [];

  const confirmCancel = async () => {
    setCancelling(true);
    try {
      if (demoMode) {
        await new Promise((resolve) => window.setTimeout(resolve, 400));
      } else {
        await cancelOrder(order.id, cancelReason);
        await queryClient.invalidateQueries({ queryKey: ['ordering'] });
      }
      setCancelOpen(false);
      toast('订单已取消，锁定的库存和优惠券会自动释放');
    } catch (error) {
      toast(getErrorMessage(error, '取消订单失败，请刷新后重试。'), { tone: 'error' });
      // A 409 usually means payment landed meanwhile; refresh so the page shows the real state.
      if (error instanceof ApiError && error.status === 409) void orderQuery.refetch();
    } finally {
      setCancelling(false);
    }
  };

  const reorder = async () => {
    setReordering(true);
    let added = 0;
    for (const line of order.lines) {
      try {
        await basket.add({
          productId: line.productId ?? line.sku,
          sku: line.sku,
          name: line.productName,
          variantName: line.variantName,
          image: imageForSku(line.sku, line.productId ?? line.sku),
          unitPrice: line.unitPrice,
          currency: order.currency,
          quantity: line.quantity,
          attributes: parseAttributes(line.variantAttributesJson),
        });
        added += 1;
      } catch {
        // Discontinued products are skipped; the toast reports the shortfall.
      }
    }
    setReordering(false);
    if (added) {
      toast(added === order.lines.length ? '商品已重新加入购物袋' : `${added} 件商品已加入购物袋，其余商品已下架`, {
        action: { label: '去结算', onClick: () => navigate(`/cart${suffix}`) },
      });
    } else {
      toast('订单中的商品均已下架或暂不可售', { tone: 'error' });
    }
  };

  return (
    <CommerceShell>
      <section className="order-detail-page">
        <Link className="orders-back" to={`/orders${suffix}`}><Icon name="arrow-right" />返回我的订单</Link>

        <header className="order-detail-header">
          <div>
            <span className={`order-status order-status--${status.tone}`}>{status.label}</span>
            <h1>订单详情</h1>
            <p>
              订单号 {order.orderNumber}
              <button type="button" className="inline-copy" onClick={() => void copy(order.orderNumber, '订单号')} aria-label="复制订单号"><Icon name="copy" /></button>
              <span aria-hidden="true"> · </span>下单于 {formatDateTime(order.createdOnUtc)}
            </p>
            {status.hint ? <p className="order-detail-header__hint">{canPay(order) ? `请在 ${formatDateTime(order.paymentDueOnUtc)} 前完成支付，超时订单将自动取消。` : status.hint}</p> : null}
          </div>
          <div className="order-detail-actions">
            {canPay(order) ? <Link className="button button--primary button--regular" to={`/payment/${order.id}${suffix}`}>立即支付 {formatMoney(order.amount, order.currency)}</Link> : null}
            {canCancel(order) ? <Button type="button" variant="secondary" onClick={() => setCancelOpen(true)}>取消订单</Button> : null}
            {eligibility.eligible ? (
              <Link className="button button--secondary button--regular" to={`/orders/${order.id}/after-sales/new${suffix}`}><Icon name="refresh" />申请售后</Link>
            ) : null}
            {order.fulfillmentStatus !== 'PendingPayment' ? (
              <Button type="button" variant="ghost" loading={reordering} onClick={() => void reorder()}><Icon name="bag" />再次购买</Button>
            ) : null}
          </div>
        </header>

        <OrderProgress order={order} />

        {afterSalesRecords.length ? (
          <section className="order-card order-card--notice">
            <h2>售后记录</h2>
            {afterSalesRecords.map((record) => (
              <Link key={record.id} className="after-sales-inline" to={`/after-sales/${record.id}${suffix}`}>
                <strong>{afterSalesTypeLabels[record.type]?.label ?? record.type}</strong>
                <span className={`order-status order-status--${afterSalesStatusLabels[record.status]?.tone ?? 'muted'}`}>{afterSalesStatusLabels[record.status]?.label ?? record.status}</span>
                <span>{formatMoney(record.refundAmount, record.currency)}</span>
                <Icon name="chevron-right" />
              </Link>
            ))}
          </section>
        ) : null}

        <div className="order-detail-layout">
          <div className="order-detail-main">
            <section className="order-card">
              <h2>商品清单 <small>共 {order.lines.reduce((total, line) => total + line.quantity, 0)} 件</small></h2>
              <ul className="order-lines">
                {order.lines.map((line) => (
                  <li key={line.sku}>
                    <img src={imageForSku(line.sku, line.productId ?? line.sku)} alt="" width="88" height="88" loading="lazy" />
                    <div className="order-lines__info">
                      {line.productId ? <Link to={`/products/${line.productId}`}>{line.productName}</Link> : <strong>{line.productName}</strong>}
                      <span>{lineVariant(line)}</span>
                      <small>SKU {line.sku}</small>
                    </div>
                    <div className="order-lines__price">
                      <span>{formatMoney(line.unitPrice, order.currency)} × {line.quantity}</span>
                      {line.discountAmount > 0 ? <small>已优惠 {formatMoney(line.discountAmount, order.currency)}</small> : null}
                      <strong>{formatMoney(line.lineAmount, order.currency)}</strong>
                      {eligibility.eligible ? (
                        <Link className="text-link" to={`/orders/${order.id}/after-sales/new?sku=${encodeURIComponent(line.sku)}${demoMode ? '&demo=1' : ''}`}>售后</Link>
                      ) : null}
                    </div>
                  </li>
                ))}
              </ul>
              {!eligibility.eligible && order.status === 'Confirmed' ? <p className="order-card__note"><Icon name="help-circle" />{eligibility.reason}</p> : null}
              {eligibility.eligible && eligibility.deadlineUtc ? <p className="order-card__note"><Icon name="shield" />售后申请截止：{formatDateTime(eligibility.deadlineUtc)}</p> : null}
            </section>

            {order.trackingNumber || history.length ? (
              <section className="order-card">
                <h2>物流信息</h2>
                {order.trackingNumber ? (
                  <p className="tracking-line">
                    <Icon name="truck" />
                    <span>{order.carrierName ?? '承运商'}</span>
                    <strong>{order.trackingNumber}</strong>
                    <button type="button" className="inline-copy" onClick={() => void copy(order.trackingNumber ?? '', '运单号')} aria-label="复制运单号"><Icon name="copy" /></button>
                  </p>
                ) : null}
                {history.length ? (
                  <ol className="timeline">
                    {history.map((entry) => (
                      <li key={`${entry.toStatus}-${entry.occurredOnUtc}`}>
                        <strong>{presentStatus(entry.toStatus).label}</strong>
                        {entry.note ? <span>{entry.note}</span> : null}
                        <time dateTime={entry.occurredOnUtc}>{formatDateTime(entry.occurredOnUtc)}</time>
                      </li>
                    ))}
                  </ol>
                ) : null}
              </section>
            ) : null}
          </div>

          <aside className="order-detail-side">
            {order.shippingAddress ? (
              <section className="order-card">
                <h2>收货信息</h2>
                <p className="address-block">
                  <strong>{order.shippingAddress.recipientName} <span>{order.shippingAddress.phoneNumber}</span></strong>
                  <span>{formatAddress(order.shippingAddress)}</span>
                </p>
              </section>
            ) : null}

            <section className="order-card">
              <h2>金额明细</h2>
              <dl className="summary-totals">
                <div><dt>商品金额</dt><dd>{formatMoney(order.subtotalAmount, order.currency)}</dd></div>
                {(order.discounts ?? []).map((discount) => (
                  <div key={`${discount.promotionId}-${discount.couponId ?? ''}`}><dt>{discount.description}</dt><dd className="is-discount">-{formatMoney(discount.amount, order.currency)}</dd></div>
                ))}
                {!order.discounts?.length && order.discountAmount > 0 ? <div><dt>优惠</dt><dd className="is-discount">-{formatMoney(order.discountAmount, order.currency)}</dd></div> : null}
                <div><dt>运费</dt><dd>{formatMoney(0, order.currency)}</dd></div>
                <div className="summary-total"><dt>{order.status === 'Confirmed' ? '实付款' : '应付款'}</dt><dd>{formatMoney(order.amount, order.currency)}</dd></div>
              </dl>
              <dl className="order-meta">
                <div><dt>支付方式</dt><dd>{paymentChannelLabel(order.paymentMethod as PaymentChannel)}</dd></div>
                {payment ? <div><dt>支付状态</dt><dd>{paymentStatusLabels[payment.status] ?? payment.status}</dd></div> : null}
                <div><dt>最近更新</dt><dd>{formatDateTime(order.updatedOnUtc)}</dd></div>
              </dl>
            </section>

            <section className="order-card order-card--help">
              <Icon name="headset" />
              <div>
                <strong>需要帮助？</strong>
                <span>付款前可直接取消订单；付款后如需退换，请在详情中申请售后。</span>
              </div>
            </section>
          </aside>
        </div>
      </section>

      <ConfirmDialog
        open={cancelOpen}
        title="取消这个订单？"
        description="取消后订单无法恢复，锁定的库存和优惠券会自动释放。"
        confirmLabel="确认取消"
        cancelLabel="暂不取消"
        tone="danger"
        loading={cancelling}
        onConfirm={() => void confirmCancel()}
        onClose={() => setCancelOpen(false)}
      >
        <fieldset className="reason-options">
          <legend>取消原因</legend>
          {cancellationReasons.map((reason) => (
            <label key={reason} className={cancelReason === reason ? 'is-selected' : undefined}>
              <input type="radio" name="cancel-reason" value={reason} checked={cancelReason === reason} onChange={() => setCancelReason(reason)} />
              <span className="selection-dot" />
              {reason}
            </label>
          ))}
        </fieldset>
      </ConfirmDialog>
    </CommerceShell>
  );
}
