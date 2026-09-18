import { Link } from 'react-router-dom';
import { describePromotion } from '../promotions/describe';
import { Icon } from '../../shared/ui/Icon';
import { formatMoney } from '../../shared/utils/format';
import type { OrderQuote } from './quote-api';

type PriceBreakdownProps = {
  quote: OrderQuote | undefined;
  fallbackSubtotal: number;
  currency: string;
  loading?: boolean;
  totalLabel?: string;
};

/** Totals block shared by cart and checkout; falls back to the local subtotal until a quote arrives. */
export function PriceBreakdown({ quote, fallbackSubtotal, currency, loading = false, totalLabel = '应付合计' }: PriceBreakdownProps) {
  const subtotal = quote?.subtotal ?? fallbackSubtotal;
  const total = quote?.total ?? fallbackSubtotal;
  return (
    <dl className={`summary-totals${loading ? ' is-stale' : ''}`} aria-busy={loading}>
      <div><dt>商品金额</dt><dd>{formatMoney(subtotal, currency)}</dd></div>
      {quote?.discounts.map((discount) => (
        <div key={`${discount.promotionId}-${discount.couponId ?? 'auto'}`}>
          <dt>
            <span className={`discount-source discount-source--${discount.sourceType === 'Coupon' ? 'coupon' : 'promotion'}`}>
              {discount.sourceType === 'Coupon' ? '券' : '促'}
            </span>
            {discount.name}{discount.maskedCode ? <small> {discount.maskedCode}</small> : null}
          </dt>
          <dd className="is-discount">-{formatMoney(discount.amount, currency)}</dd>
        </div>
      ))}
      <div><dt>运费</dt><dd>{formatMoney(0, currency)}</dd></div>
      <div className="summary-total">
        <dt>{totalLabel}{quote?.discountTotal ? <small>已省 {formatMoney(quote.discountTotal, currency)}</small> : null}</dt>
        <dd>{formatMoney(total, currency)}</dd>
      </div>
    </dl>
  );
}

/** "Buy a little more" nudges for automatic promotions whose threshold is not reached yet. */
export function PromotionOpportunities({ quote, currency, productsPath = '/products' }: { quote: OrderQuote | undefined; currency: string; productsPath?: string }) {
  if (!quote?.opportunities.length) return null;
  return (
    <ul className="promotion-hints" aria-label="凑单优惠">
      {quote.opportunities.slice(0, 2).map((opportunity) => (
        <li key={opportunity.promotionId}>
          <Icon name="gift" />
          <span>
            {opportunity.missingAmount ? `再买 ${formatMoney(opportunity.missingAmount, currency)}` : ''}
            {opportunity.missingAmount && opportunity.missingQuantity ? '、' : ''}
            {opportunity.missingQuantity ? `再买 ${opportunity.missingQuantity} 件` : ''}
            {' '}可享「{opportunity.name}」<small>{describePromotion([], opportunity.benefits, currency)}</small>
          </span>
          <Link to={productsPath}>去凑单<Icon name="chevron-right" /></Link>
        </li>
      ))}
    </ul>
  );
}
