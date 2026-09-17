import { Link } from 'react-router-dom';
import { Icon } from '../../shared/ui/Icon';
import { formatDateTime, formatMoney } from '../../shared/utils/format';
import { presentStatus } from './order-presentation';
import type { OrderListItem } from './types';

function deliverySummary(order: OrderListItem): string {
  if (order.trackingNumber) {
    return `${order.carrierName ?? '承运商'} · ${order.trackingNumber}`;
  }

  switch (order.fulfillmentStatus) {
    case 'PendingPayment': return '付款后安排配送';
    case 'AwaitingShipment': return '商家正在准备发货';
    case 'Cancelled': return '该订单已取消';
    default: return '物流信息待更新';
  }
}

export function OrderList({ orders, demoMode = false }: { orders: OrderListItem[]; demoMode?: boolean }) {
  const suffix = demoMode ? '?demo=1' : '';

  if (!orders.length) {
    return (
      <div className="orders-empty">
        <Icon name="clipboard" />
        <h2>暂无相关订单</h2>
        <p>当前分类下还没有订单记录。</p>
        <Link className="button button--secondary button--small" to={`/products${suffix}`}>去逛逛</Link>
      </div>
    );
  }

  return (
    <div className="order-list">
      {orders.map((order) => {
        const status = presentStatus(order.fulfillmentStatus);
        const detailPath = `/orders/${order.id}${suffix}`;
        return (
          <article className="order-row" key={order.id}>
            <header className="order-row__header">
              <div>
                <time dateTime={order.createdOnUtc}>{formatDateTime(order.createdOnUtc)}</time>
                <Link to={detailPath}>订单号 {order.orderNumber}</Link>
              </div>
              <span className={`order-status order-status--${status.tone}`}>{status.label}</span>
            </header>
            <div className="order-row__body">
              <div className="order-recipient">
                <span className="order-row__icon"><Icon name="user" /></span>
                <div>
                  <span>收货人</span>
                  <strong>{order.recipientName}</strong>
                  <small>{order.phoneNumber}</small>
                </div>
              </div>
              <div className="order-delivery">
                <span>配送信息</span>
                <strong>{deliverySummary(order)}</strong>
              </div>
              <div className="order-amount">
                <span>订单总额</span>
                <strong>{formatMoney(order.amount, order.currency)}</strong>
                <div className="order-row__actions">
                  {order.fulfillmentStatus === 'PendingPayment' ? (
                    <Link className="button button--primary button--small" to={`/payment/${order.id}${suffix}`}>去支付</Link>
                  ) : null}
                  <Link className="button button--secondary button--small" to={detailPath}>
                    {order.fulfillmentStatus === 'Delivered' ? '详情 / 售后' : '查看详情'}
                  </Link>
                </div>
              </div>
            </div>
          </article>
        );
      })}
    </div>
  );
}
