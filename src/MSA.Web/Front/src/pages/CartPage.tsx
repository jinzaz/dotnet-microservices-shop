import { useState, type FormEvent } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';
import { useAuth } from '../app/auth/AuthProvider';
import { MAX_LINE_QUANTITY, useBasket, useBasketActions } from '../features/basket/basket-store';
import type { CartLine } from '../features/basket/types';
import { getErrorMessage } from '../shared/api/api-error';
import { isDemoMode } from '../shared/config/api';
import { CommerceShell } from '../shared/layout/CommerceShell';
import { ServicePromises } from '../shared/layout/ServicePromises';
import { Button } from '../shared/ui/Button';
import { Icon } from '../shared/ui/Icon';
import { useToast } from '../shared/ui/Toast';
import { formatMoney } from '../shared/utils/format';

function variantText(line: CartLine): string {
  const values = Object.values(line.attributes);
  return values.length ? values.join(' / ') : line.variantName ?? '';
}

export default function CartPage() {
  const location = useLocation();
  const navigate = useNavigate();
  const toast = useToast();
  const demoMode = isDemoMode(location.search);
  const suffix = demoMode ? '?demo=1' : '';
  const { isAuthenticated, login } = useAuth();
  const basket = useBasket();
  const actions = useBasketActions();
  const [busySku, setBusySku] = useState<string | null>(null);
  const [couponDraft, setCouponDraft] = useState('');
  const [savingCoupons, setSavingCoupons] = useState(false);

  const run = async (sku: string, action: () => Promise<unknown>, success?: string) => {
    setBusySku(sku);
    try {
      await action();
      if (success) toast(success);
    } catch (error) {
      toast(getErrorMessage(error), { tone: 'error' });
    } finally {
      setBusySku(null);
    }
  };

  const removeLine = (line: CartLine) => run(line.sku, () => actions.remove(line.sku), `已移除 ${line.name}`);

  const updateCoupons = async (codes: string[]) => {
    setSavingCoupons(true);
    try {
      await actions.setCoupons(codes);
      setCouponDraft('');
    } catch (error) {
      toast(getErrorMessage(error), { tone: 'error' });
    } finally {
      setSavingCoupons(false);
    }
  };

  const addCoupon = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const code = couponDraft.trim().toUpperCase();
    if (!code) return;
    if (basket.couponCodes.includes(code)) {
      toast('该优惠码已添加', { tone: 'info' });
      return;
    }
    void updateCoupons([...basket.couponCodes, code]);
  };

  const checkout = () => {
    if (!isAuthenticated && !demoMode) {
      login('/checkout');
      return;
    }
    navigate(`/checkout${suffix}`);
  };

  if (basket.isLoading) {
    return <CommerceShell><div className="page-state" role="status"><span className="page-spinner" />正在加载购物袋…</div></CommerceShell>;
  }

  if (basket.error) {
    return (
      <CommerceShell>
        <div className="page-state" role="alert">
          <Icon name="bag" />
          <h1>购物袋暂时无法加载</h1>
          <p>{getErrorMessage(basket.error)}</p>
          <Button type="button" onClick={() => void basket.refetch()}>重新加载</Button>
        </div>
      </CommerceShell>
    );
  }

  if (!basket.lines.length) {
    return (
      <CommerceShell>
        <div className="page-state checkout-empty">
          <Icon name="bag" />
          <h1>购物袋还是空的</h1>
          <p>去挑选几件喜欢的商品吧{!isAuthenticated && !demoMode ? '，登录后购物袋会在多设备间同步' : ''}。</p>
          <Link className="button button--primary button--regular" to={`/products${suffix}`}>浏览商品</Link>
        </div>
      </CommerceShell>
    );
  }

  const unavailableCount = basket.lines.filter((line) => line.isAvailable === false).length;

  return (
    <CommerceShell>
      <section className="cart-page">
        <div className="orders-title-row">
          <div>
            <h1>购物袋</h1>
            <p>共 {basket.itemCount} 件商品{basket.remote ? '，已同步到你的账户' : '，登录后自动保存到账户'}</p>
          </div>
          <Link className="text-link" to={`/products${suffix}`}>继续购物 <Icon name="arrow-right" /></Link>
        </div>

        {basket.hasPriceChanges ? (
          <p className="inline-alert inline-alert--info" role="status"><Icon name="alert-circle" />部分商品价格已变动，请确认最新价格后再结算。</p>
        ) : null}
        {unavailableCount ? (
          <p className="inline-alert inline-alert--error" role="alert"><Icon name="alert-circle" />{unavailableCount} 件商品已下架或暂不可售，请移除后再结算。</p>
        ) : null}

        <div className="checkout-layout">
          <ul className="cart-lines">
            {basket.lines.map((line) => {
              const busy = busySku === line.sku;
              const unavailable = line.isAvailable === false;
              return (
                <li key={line.sku} className={`${unavailable ? 'is-unavailable' : ''} ${busy ? 'is-busy' : ''}`.trim() || undefined}>
                  <Link to={`/products/${line.productId}${suffix}`} className="cart-lines__image">
                    <img src={line.image} alt="" width="112" height="112" loading="lazy" />
                  </Link>
                  <div className="cart-lines__info">
                    <Link to={`/products/${line.productId}${suffix}`}><strong>{line.name}</strong></Link>
                    {variantText(line) ? <span>{variantText(line)}</span> : null}
                    {unavailable ? <em className="tag tag--muted">暂不可售</em> : null}
                    {line.priceChanged && line.previousUnitPrice !== undefined ? (
                      <em className="tag tag--warning">价格已从 {formatMoney(line.previousUnitPrice, line.currency)} 调整</em>
                    ) : null}
                  </div>
                  <div className="quantity-stepper" aria-label={`${line.name}数量`}>
                    <button type="button" aria-label="减少数量" disabled={busy || unavailable || line.quantity <= 1} onClick={() => void run(line.sku, () => actions.setQuantity(line.sku, line.quantity - 1))}><Icon name="minus" /></button>
                    <output aria-live="polite">{line.quantity}</output>
                    <button type="button" aria-label="增加数量" disabled={busy || unavailable || line.quantity >= MAX_LINE_QUANTITY} onClick={() => void run(line.sku, () => actions.setQuantity(line.sku, line.quantity + 1))}><Icon name="plus" /></button>
                  </div>
                  <strong className="cart-lines__amount">{formatMoney(line.unitPrice * line.quantity, line.currency)}</strong>
                  <button type="button" className="icon-button" aria-label={`移除${line.name}`} disabled={busy} onClick={() => void removeLine(line)}><Icon name="trash" /></button>
                </li>
              );
            })}
          </ul>

          <aside className="order-summary">
            <h2>结算信息</h2>
            {basket.remote ? (
              <form className="coupon-code-form" onSubmit={addCoupon}>
                <label htmlFor="coupon-code">优惠码</label>
                <div>
                  <input id="coupon-code" value={couponDraft} onChange={(event) => setCouponDraft(event.target.value)} placeholder="输入优惠码" maxLength={64} autoComplete="off" />
                  <Button type="submit" variant="secondary" size="small" loading={savingCoupons}>使用</Button>
                </div>
                {basket.couponCodes.length ? (
                  <ul className="coupon-chips">
                    {basket.couponCodes.map((code) => (
                      <li key={code}>
                        <Icon name="ticket" />{code}
                        <button type="button" aria-label={`移除优惠码 ${code}`} disabled={savingCoupons} onClick={() => void updateCoupons(basket.couponCodes.filter((value) => value !== code))}><Icon name="close" /></button>
                      </li>
                    ))}
                  </ul>
                ) : null}
              </form>
            ) : (
              <p className="checkout-contract-note">登录后可使用优惠码，购物袋也会同步到你的账户。</p>
            )}
            <dl className="summary-totals">
              <div><dt>商品金额（预估）</dt><dd>{formatMoney(basket.subtotal, basket.currency)}</dd></div>
              <div><dt>运费</dt><dd>{formatMoney(0, basket.currency)}</dd></div>
              <div className="summary-total"><dt>预估合计</dt><dd>{formatMoney(basket.subtotal, basket.currency)}</dd></div>
            </dl>
            <p className="checkout-contract-note">优惠与最终金额以提交订单时的计算为准。</p>
            <Button type="button" className="submit-order" disabled={unavailableCount > 0} onClick={checkout}>
              {isAuthenticated || demoMode ? `去结算（${basket.itemCount}）` : '登录后结算'}
            </Button>
          </aside>
        </div>
      </section>
      <ServicePromises />
    </CommerceShell>
  );
}
