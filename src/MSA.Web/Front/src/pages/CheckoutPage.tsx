import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { basketQueryKey, clearCart, ensureCartLine, useBasket } from '../features/basket/basket-store';
import type { CartLine } from '../features/basket/types';
import { createOrder, type PaymentMethod } from '../features/checkout/order-api';
import { demoAccountSnapshot } from '../features/profile/demo-account';
import { listAddresses } from '../features/profile/profile-api';
import type { UserAddress } from '../features/profile/types';
import { ApiError, getErrorMessage } from '../shared/api/api-error';
import { isDemoMode } from '../shared/config/api';
import { CommerceShell } from '../shared/layout/CommerceShell';
import { Button } from '../shared/ui/Button';
import { Icon } from '../shared/ui/Icon';
import { formatMoney } from '../shared/utils/format';

const demoCheckoutLine: CartLine = {
  productId: 'editorial-pour-over',
  sku: 'MSA-HOME-001',
  name: '手冲咖啡套装',
  image: '/images/product-pour-over-bundle.png',
  unitPrice: 299,
  currency: 'CNY',
  quantity: 1,
  attributes: { 容量: '600ml', 滤杯: '胡桃木' },
};

const paymentMethods: ReadonlyArray<{ value: PaymentMethod; label: string; detail: string }> = [
  { value: 'Alipay', label: '支付宝', detail: '推荐' },
  { value: 'WeChatPay', label: '微信支付', detail: '快捷支付' },
];

function fullAddress(address: UserAddress): string {
  return `${address.province} ${address.city} ${address.district} ${address.street}`;
}

function checkoutErrorMessage(error: unknown): string {
  if (error instanceof ApiError && error.status === 429) return '提交过于频繁，请稍候再试。';
  if (error instanceof ApiError && error.status === 409) return `${getErrorMessage(error)} 请返回购物袋确认商品和库存后重试。`;
  return getErrorMessage(error);
}

export default function CheckoutPage() {
  const demoMode = isDemoMode();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const basket = useBasket();
  const [selectedAddressId, setSelectedAddressId] = useState('');
  const [paymentMethod, setPaymentMethod] = useState<PaymentMethod>('Alipay');
  const [acceptedTerms, setAcceptedTerms] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState('');
  // One key per checkout attempt: retries after a network failure must not create a second order.
  const idempotencyKey = useRef(crypto.randomUUID());
  const addressQuery = useQuery({
    queryKey: ['user', 'addresses'],
    queryFn: listAddresses,
    enabled: !demoMode,
    // Always refetch on mount: the shopper may have just added an address on the account page.
    staleTime: 0,
  });

  useEffect(() => {
    if (demoMode && basket.lines.length === 0) ensureCartLine(demoCheckoutLine);
  }, [demoMode, basket.lines.length]);

  const lines = basket.lines;
  const addresses = demoMode ? demoAccountSnapshot.addresses : addressQuery.data ?? [];
  const defaultAddressId = addresses.find((address) => address.isDefault)?.id ?? addresses[0]?.id ?? '';
  const effectiveAddressId = selectedAddressId || defaultAddressId;
  const selectedAddress = addresses.find((address) => address.id === effectiveAddressId);
  const unavailable = lines.filter((line) => line.isAvailable === false);

  const submit = async () => {
    if (!selectedAddress) return setSubmitError('请选择收货地址。');
    if (!lines.length) return setSubmitError('购物袋中还没有商品。');
    if (unavailable.length) return setSubmitError('购物袋中有暂不可售的商品，请返回购物袋移除。');
    if (!acceptedTerms) return setSubmitError('请先阅读并同意用户服务协议与隐私政策。');

    setSubmitting(true);
    setSubmitError('');
    try {
      if (demoMode) {
        await new Promise((resolve) => window.setTimeout(resolve, 450));
        clearCart();
        navigate(`/payment/DEMO-${Date.now()}?demo=1&channel=${paymentMethod}`);
        return;
      }
      const response = await createOrder({
        items: lines.map((line) => ({ sku: line.sku, quantity: line.quantity })),
        paymentMethod,
        shippingAddress: {
          addressId: selectedAddress.id,
          recipientName: selectedAddress.recipientName,
          phoneNumber: selectedAddress.phoneNumber,
          countryCode: selectedAddress.countryCode,
          province: selectedAddress.province,
          city: selectedAddress.city,
          district: selectedAddress.district,
          streetLine1: selectedAddress.street,
          streetLine2: null,
          postalCode: selectedAddress.postalCode,
        },
        couponCodes: basket.couponCodes,
        // Lets BasketService clear exactly this basket version once the order is accepted.
        sourceBasketId: basket.basket?.basketId ?? undefined,
        sourceBasketVersion: basket.basket?.basketId ? basket.basket.version : undefined,
      }, idempotencyKey.current);
      void queryClient.invalidateQueries({ queryKey: ['ordering', 'orders'] });
      // The basket is cleared asynchronously; refresh it after the event has had time to arrive.
      window.setTimeout(() => void queryClient.invalidateQueries({ queryKey: basketQueryKey }), 3_000);
      navigate(`/payment/${response.orderId}`, { replace: true });
    } catch (error) {
      setSubmitError(checkoutErrorMessage(error));
      // Validation failures are final for this key; a fresh key lets the corrected request through.
      if (error instanceof ApiError && error.status < 500) idempotencyKey.current = crypto.randomUUID();
    } finally {
      setSubmitting(false);
    }
  };

  if (basket.isLoading) {
    return <CommerceShell><div className="page-state" role="status"><span className="page-spinner" />正在准备结算…</div></CommerceShell>;
  }

  if (!lines.length) {
    return (
      <CommerceShell>
        <div className="page-state checkout-empty"><Icon name="bag" /><h1>购物袋还是空的</h1><p>先去挑选一件喜欢的商品，再回来确认订单。</p><Link className="button button--primary button--regular" to="/products">浏览商品</Link></div>
      </CommerceShell>
    );
  }

  return (
    <CommerceShell>
      <div className="checkout-page">
        <Link className="orders-back" to={`/cart${demoMode ? '?demo=1' : ''}`}><Icon name="arrow-right" />返回购物袋</Link>
        <h1>确认订单</h1>
        {demoMode ? <p className="demo-notice">开发预览数据 · 提交订单会进入本地支付预览</p> : null}
        <div className="checkout-layout">
          <div className="checkout-sections">
            <section className="checkout-section">
              <div className="checkout-section__title"><span>1</span><h2>收货地址</h2></div>
              {addressQuery.isPending && !demoMode ? <p className="checkout-contract-note">正在加载地址…</p> : null}
              {addressQuery.isError && !demoMode ? (
                <p className="form-message form-message--error">地址加载失败：{getErrorMessage(addressQuery.error)} <button type="button" className="link-button" onClick={() => void addressQuery.refetch()}>重试</button></p>
              ) : null}
              {addressQuery.isSuccess && !addresses.length ? <p className="checkout-contract-note">还没有收货地址，请先新增一个地址。</p> : null}
              <div className="checkout-addresses">
                {addresses.map((address) => (
                  <label key={address.id} className={effectiveAddressId === address.id ? 'is-selected' : undefined}>
                    <input type="radio" name="address" value={address.id} checked={effectiveAddressId === address.id} onChange={() => setSelectedAddressId(address.id)} />
                    <span className="selection-dot" />
                    <strong>{address.recipientName}</strong>
                    <span>{address.phoneNumber}</span>
                    <address>{fullAddress(address)}</address>
                    {address.isDefault ? <em>默认</em> : null}
                  </label>
                ))}
              </div>
              <Link className="outline-action" to={`/account${demoMode ? '?demo=1' : ''}#addresses`}><Icon name="plus" />管理 / 新增地址</Link>
            </section>

            <section className="checkout-section">
              <div className="checkout-section__title"><span>2</span><h2>配送方式</h2></div>
              <div className="delivery-choice"><span className="selection-dot is-selected" /><strong>标准配送</strong><span>付款后 48 小时内发货</span><b>{formatMoney(0)}</b></div>
            </section>

            <section className="checkout-section">
              <div className="checkout-section__title"><span>3</span><h2>支付方式</h2></div>
              <div className="payment-choices">
                {paymentMethods.map((method) => (
                  <label key={method.value} className={paymentMethod === method.value ? 'is-selected' : undefined}>
                    <input type="radio" name="payment" value={method.value} checked={paymentMethod === method.value} onChange={() => setPaymentMethod(method.value)} />
                    <span className="selection-dot" /><Icon name="credit-card" /><strong>{method.label}</strong><small>{method.detail}</small>
                  </label>
                ))}
              </div>
            </section>

            <section className="checkout-section">
              <div className="checkout-section__title"><span>4</span><h2>优惠</h2></div>
              {basket.couponCodes.length ? (
                <ul className="coupon-chips coupon-chips--static">
                  {basket.couponCodes.map((code) => <li key={code}><Icon name="ticket" />{code}</li>)}
                </ul>
              ) : <p className="checkout-contract-note">未使用优惠码。</p>}
              <Link className="outline-action" to={`/cart${demoMode ? '?demo=1' : ''}`}><Icon name="edit" />在购物袋中修改优惠码</Link>
              <small className="checkout-contract-note">满减、折扣等活动会在提交订单时自动计算，实付金额以支付页为准。</small>
            </section>
          </div>

          <aside className="order-summary">
            <h2>订单摘要</h2>
            <div className="summary-products">
              {lines.map((line) => (
                <article key={line.sku}>
                  <img src={line.image} alt="" width="112" height="112" />
                  <div>
                    <strong>{line.name}</strong>
                    <span>{Object.values(line.attributes).join(' / ') || line.variantName}</span>
                    <b>{formatMoney(line.unitPrice, line.currency)} × {line.quantity}</b>
                    {line.isAvailable === false ? <em className="tag tag--muted">暂不可售</em> : null}
                  </div>
                </article>
              ))}
            </div>
            <dl className="summary-totals">
              <div><dt>商品金额</dt><dd>{formatMoney(basket.subtotal, basket.currency)}</dd></div>
              <div><dt>运费</dt><dd>{formatMoney(0, basket.currency)}</dd></div>
              <div><dt>活动与优惠</dt><dd>提交后计算</dd></div>
              <div className="summary-total"><dt>预估应付</dt><dd>{formatMoney(basket.subtotal, basket.currency)}</dd></div>
            </dl>
            {submitError ? <p className="form-message form-message--error" role="alert">{submitError}</p> : null}
            <Button type="button" className="submit-order" loading={submitting} disabled={!selectedAddress || unavailable.length > 0} onClick={() => void submit()}>提交订单</Button>
            <label className="summary-terms">
              <input type="checkbox" checked={acceptedTerms} onChange={(event) => setAcceptedTerms(event.target.checked)} />
              <span><Icon name="check" /></span>
              我已阅读并同意《用户服务协议》《隐私政策》和《售后服务规则》
            </label>
          </aside>
        </div>
      </div>
      <section className="checkout-progress" aria-label="订单流程">
        <h2>下单后你将获得</h2>
        <div>
          <span><Icon name="receipt" /><strong>订单确认</strong><small>提交后锁定库存与优惠</small></span>
          <span><Icon name="package" /><strong>商品发货</strong><small>物流信息实时更新</small></span>
          <span><Icon name="truck" /><strong>到货签收</strong><small>请注意查收包裹</small></span>
          <span><Icon name="circle-check" /><strong>售后无忧</strong><small>签收后 7 天内可申请退换</small></span>
        </div>
      </section>
    </CommerceShell>
  );
}
