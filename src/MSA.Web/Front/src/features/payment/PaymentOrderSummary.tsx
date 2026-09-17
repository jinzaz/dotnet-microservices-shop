import { Link } from 'react-router-dom';
import { imageForSku } from '../catalog/product-view';
import type { OrderDetails } from '../orders/types';
import { Icon } from '../../shared/ui/Icon';
import { formatMoney } from '../../shared/utils/format';

export function PaymentOrderSummary({ order, succeeded, detailPath }: { order: OrderDetails; succeeded: boolean; detailPath?: string }) {
  return (
    <aside className="payment-order-summary">
      <h2>订单摘要</h2>
      <div className="payment-order-products">
        {order.lines.slice(0, 3).map((line) => (
          <article key={line.sku}>
            <img src={imageForSku(line.sku, line.productId ?? line.sku)} alt="" width="128" height="104" />
            <div>
              <strong>{line.productName}</strong>
              <span>{line.variantName || line.sku}</span>
            </div>
            <span>× {line.quantity}</span>
          </article>
        ))}
        {order.lines.length > 3 ? <small>另有 {order.lines.length - 3} 件商品</small> : null}
      </div>
      <dl>
        <div><dt>订单号</dt><dd>{detailPath ? <Link to={detailPath}>{order.orderNumber}</Link> : order.orderNumber}</dd></div>
        <div><dt>商品金额</dt><dd>{formatMoney(order.subtotalAmount, order.currency)}</dd></div>
        <div><dt>优惠</dt><dd className="is-discount">- {formatMoney(order.discountAmount, order.currency)}</dd></div>
        <div className="payment-summary-total"><dt>实付</dt><dd>{formatMoney(order.amount, order.currency)}</dd></div>
      </dl>
      <p className="payment-security-note">
        <Icon name="shield" />
        {succeeded ? '支付结果已由支付服务确认。' : '支付安全由支付服务保障，请勿重复付款。'}
      </p>
    </aside>
  );
}
