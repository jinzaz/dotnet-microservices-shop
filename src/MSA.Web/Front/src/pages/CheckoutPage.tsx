import { useQuery } from '@tanstack/react-query';
import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { clearCart, ensureCartLine, updateCartLineQuantity, useBasket } from '../features/basket/basket-store';
import type { CartLine } from '../features/basket/types';
import { createOrder, type PaymentMethod } from '../features/checkout/order-api';
import { listMyCoupons } from '../features/coupons/coupons-api';
import { demoCoupons } from '../features/coupons/demo-coupons';
import { demoAccountSnapshot } from '../features/profile/demo-account';
import { getPoints, listAddresses } from '../features/profile/profile-api';
import type { UserAddress } from '../features/profile/types';
import { getErrorMessage } from '../shared/api/api-error';
import { isDemoMode } from '../shared/config/api';
import { CommerceShell } from '../shared/layout/CommerceShell';
import { Button } from '../shared/ui/Button';
import { Icon } from '../shared/ui/Icon';
import { formatMoney, formatPoints } from '../shared/utils/format';

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

function createDemoOrderId(): string {
  return `DEMO-${new Date().toISOString().slice(0, 10).replaceAll('-', '')}-${Math.floor(1000 + Math.random() * 9000)}`;
}

export default function CheckoutPage() {
  const demoMode = isDemoMode();
  const navigate = useNavigate();
  const { lines } = useBasket();
  const [selectedAddressId, setSelectedAddressId] = useState('');
  const [paymentMethod, setPaymentMethod] = useState<PaymentMethod>('Alipay');
  const [selectedCouponCode, setSelectedCouponCode] = useState('MSA40X299');
  const [usePoints, setUsePoints] = useState(true);
  const [acceptedTerms, setAcceptedTerms] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState('');
  const idempotencyKey = useRef(crypto.randomUUID());
  const addressQuery = useQuery({
    queryKey: ['identity', 'checkout-addresses'],
    queryFn: listAddresses,
    enabled: !demoMode,
    staleTime: 30_000,
  });
  const pointsQuery = useQuery({
    queryKey: ['identity', 'checkout-points'],
    queryFn: getPoints,
    enabled: !demoMode,
    staleTime: 30_000,
  });
  const couponsQuery = useQuery({
    queryKey: ['catalog', 'checkout-coupons'],
    queryFn: () => listMyCoupons('available'),
    enabled: !demoMode,
    staleTime: 30_000,
    retry: 0,
  });

  useEffect(() => {
    if (demoMode && lines.length === 0) ensureCartLine(demoCheckoutLine);
  }, [demoMode, lines.length]);

  const checkoutLines = lines.length ? lines : demoMode ? [demoCheckoutLine] : [];
  const addresses = demoMode ? demoAccountSnapshot.addresses : addressQuery.data ?? [];
  const coupons = demoMode || couponsQuery.isError
    ? demoCoupons.filter((coupon) => coupon.status === 'available')
    : couponsQuery.data ?? [];
  const defaultAddressId = addresses.find((address) => address.isDefault)?.id ?? addresses[0]?.id ?? '';
  const effectiveAddressId = selectedAddressId || defaultAddressId;
  const selectedAddress = addresses.find((address) => address.id === effectiveAddressId);
  const selectedCoupon = coupons.find((coupon) => coupon.code === selectedCouponCode);
  const pointsBalance = demoMode ? demoAccountSnapshot.points.balance : pointsQuery.data?.balance ?? 0;
  const subtotal = useMemo(
    () => checkoutLines.reduce((total, line) => total + line.unitPrice * line.quantity, 0),
    [checkoutLines],
  );
  const couponDiscount = selectedCoupon && selectedCoupon.code === 'MSA40X299' && subtotal >= 299
    ? 40
    : selectedCoupon?.valueLabel === '9折' ? Math.round(subtotal * 0.1 * 100) / 100 : 0;
  const pointsDiscount = usePoints ? Math.min(pointsBalance / 100, Math.max(0, subtotal - couponDiscount)) : 0;
  const total = Math.max(0, subtotal - couponDiscount - pointsDiscount);

  const submit = async () => {
    if (!selectedAddress) {
      setSubmitError('请选择收货地址。');
      return;
    }
    if (!checkoutLines.length) {
      setSubmitError('购物袋中还没有商品。');
      return;
    }
    if (!acceptedTerms) {
      setSubmitError('请先阅读并同意用户服务协议与隐私政策。');
      return;
    }

    setSubmitting(true);
    setSubmitError('');
    try {
      let orderId: string;
      if (demoMode) {
        await new Promise((resolve) => window.setTimeout(resolve, 450));
        orderId = createDemoOrderId();
      } else {
        const response = await createOrder({
          items: checkoutLines.map((line) => ({ sku: line.sku, quantity: line.quantity })),
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
          couponCodes: selectedCoupon ? [selectedCoupon.code] : [],
        }, idempotencyKey.current);
        orderId = response.orderId;
      }
      clearCart();
      navigate(`/payment/${orderId}${demoMode ? `?demo=1&channel=${paymentMethod}` : ''}`);
    } catch (error) {
      setSubmitError(getErrorMessage(error));
      idempotencyKey.current = crypto.randomUUID();
    } finally {
      setSubmitting(false);
    }
  };

  if (!checkoutLines.length) {
    return (
      <CommerceShell>
        <div className="page-state checkout-empty"><Icon name="bag" /><h1>购物袋还是空的</h1><p>先去挑选一件喜欢的商品，再回来确认订单。</p><Link className="button button--primary button--regular" to="/#featured">浏览精选</Link></div>
      </CommerceShell>
    );
  }

  return (
    <CommerceShell>
      <div className="checkout-page">
        <Link className="orders-back" to="/#featured"><Icon name="arrow-right" />返回购物袋</Link>
        <h1>确认订单</h1>
        {demoMode ? <p className="demo-notice">开发预览数据 · 提交订单会进入本地成功状态</p> : null}
        <div className="checkout-layout">
          <div className="checkout-sections">
            <section className="checkout-section">
              <div className="checkout-section__title"><span>1</span><h2>收货地址</h2></div>
              {addressQuery.isError && !demoMode ? <p className="form-message form-message--error">地址加载失败：{getErrorMessage(addressQuery.error)}</p> : null}
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
              <Link className="outline-action" to={`/account${demoMode ? '?demo=1' : ''}#addresses`}><Icon name="plus" />新增地址</Link>
            </section>

            <section className="checkout-section">
              <div className="checkout-section__title"><span>2</span><h2>配送方式</h2></div>
              <div className="delivery-choice"><span className="selection-dot is-selected" /><strong>标准配送</strong><span>预计 3–5 个工作日送达</span><b>¥0.00</b></div>
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
              <div className="checkout-section__title"><span>4</span><h2>优惠与积分</h2></div>
              <label className="checkout-coupon-select">
                <span>优惠券</span>
                <select value={selectedCouponCode} onChange={(event) => setSelectedCouponCode(event.target.value)}>
                  <option value="">不使用优惠券</option>
                  {coupons.map((coupon) => <option key={coupon.id} value={coupon.code}>{coupon.title}</option>)}
                </select>
                <strong>{couponDiscount ? `-${formatMoney(couponDiscount)}` : '未抵扣'}</strong>
              </label>
              <label className="points-choice">
                <input type="checkbox" checked={usePoints} onChange={(event) => setUsePoints(event.target.checked)} />
                <span><Icon name="check" /></span>
                使用 {formatPoints(pointsBalance)} 积分抵扣 {formatMoney(pointsDiscount)}
              </label>
              {couponsQuery.isError && !demoMode ? <small className="checkout-contract-note">用户券包接口待接入，当前优惠为前端预览数据；订单金额以 Ordering 服务确认为准。</small> : null}
            </section>
          </div>

          <aside className="order-summary">
            <h2>订单摘要</h2>
            <div className="summary-products">
              {checkoutLines.map((line) => (
                <article key={line.sku}>
                  <img src={line.image} alt="" width="112" height="112" />
                  <div><strong>{line.name}</strong><span>{Object.values(line.attributes).join(' / ')}</span><b>{formatMoney(line.unitPrice, line.currency)}</b></div>
                  <div className="summary-quantity">
                    <button type="button" aria-label={`减少${line.name}数量`} onClick={() => updateCartLineQuantity(line.sku, Math.max(1, line.quantity - 1))}><Icon name="minus" /></button>
                    <span>{line.quantity}</span>
                    <button type="button" aria-label={`增加${line.name}数量`} onClick={() => updateCartLineQuantity(line.sku, Math.min(99, line.quantity + 1))}><Icon name="plus" /></button>
                  </div>
                </article>
              ))}
            </div>
            <dl className="summary-totals">
              <div><dt>商品金额</dt><dd>{formatMoney(subtotal)}</dd></div>
              <div><dt>运费</dt><dd>{formatMoney(0)}</dd></div>
              <div><dt>优惠券</dt><dd className="is-discount">-{formatMoney(couponDiscount)}</dd></div>
              <div><dt>积分抵扣</dt><dd className="is-discount">-{formatMoney(pointsDiscount)}</dd></div>
              <div className="summary-total"><dt>应付总额</dt><dd>{formatMoney(total)}</dd></div>
            </dl>
            {submitError ? <p className="form-message form-message--error" role="alert">{submitError}</p> : null}
            <Button type="button" className="submit-order" loading={submitting} disabled={!selectedAddress} onClick={() => void submit()}>提交订单</Button>
            <label className="summary-terms">
              <input type="checkbox" checked={acceptedTerms} onChange={(event) => setAcceptedTerms(event.target.checked)} />
              <span><Icon name="check" /></span>
              我已阅读并同意《用户服务协议》和《隐私政策》
            </label>
          </aside>
        </div>
      </div>
      <section className="checkout-progress" aria-label="订单流程">
        <h2>下单后你将获得</h2>
        <div>
          <span><Icon name="receipt" /><strong>订单确认</strong><small>提交后尽快确认订单</small></span>
          <span><Icon name="package" /><strong>商品发货</strong><small>物流信息将及时通知</small></span>
          <span><Icon name="truck" /><strong>到货收货</strong><small>请注意查收包裹</small></span>
          <span><Icon name="circle-check" /><strong>售后无忧</strong><small>7 天无理由退换</small></span>
        </div>
      </section>
    </CommerceShell>
  );
}
