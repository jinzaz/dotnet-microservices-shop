import type { OrderDetails, OrderLine } from '../orders/types';
import { Icon } from '../../shared/ui/Icon';
import { formatMoney } from '../../shared/utils/format';

function productImage(line: OrderLine): string {
  const normalized = line.sku.toUpperCase();
  if (normalized.includes('CHARG')) return '/images/product-charger.jpg';
  if (normalized.includes('BACKPACK') || normalized.includes('TRAVEL')) return '/images/product-backpack.jpg';
  if (normalized.includes('TOOTH')) return '/images/product-toothbrush.jpg';
  return '/images/product-pour-over-bundle.png';
}

function productVariant(line: OrderLine): string {
  if (line.sku.includes('600') && line.sku.toUpperCase().includes('WALNUT')) return '600ml / 胡桃木';
  return line.sku;
}

export function PaymentOrderSummary({ order, succeeded }: { order: OrderDetails; succeeded: boolean }) {
  return (
    <aside className="payment-order-summary">
      <h2>订单摘要</h2>
      <div className="payment-order-products">
        {order.lines.slice(0, 2).map((line) => (
          <article key={line.sku}>
            <img src={productImage(line)} alt="" width="128" height="104" />
            <div>
              <strong>{line.productName}</strong>
              <span>{productVariant(line)}</span>
            </div>
            <span>× {line.quantity}</span>
          </article>
        ))}
        {order.lines.length > 2 ? <small>另有 {order.lines.length - 2} 件商品</small> : null}
      </div>
      <dl>
        <div><dt>订单号</dt><dd>{order.orderNumber}</dd></div>
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
