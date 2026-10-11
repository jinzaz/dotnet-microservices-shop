import { useQuery } from '@tanstack/react-query';
import { useEffect, useMemo, useState } from 'react';
import { Link, useLocation, useParams } from 'react-router-dom';
import { createDemoOrder, createDemoPayment } from '../features/payment/demo-payment';
import { getPaymentByOrder, syncPayment } from '../features/payment/payment-api';
import { PaymentOrderSummary } from '../features/payment/PaymentOrderSummary';
import { PaymentProgress } from '../features/payment/PaymentProgress';
import { PaymentProviderMark, PaymentProviderSelector, paymentChannelLabel } from '../features/payment/PaymentProviderSelector';
import { PaymentQrCode, isPagePayForm } from '../features/payment/PaymentQrCode';
import type { PaymentChannel, PaymentStatus } from '../features/payment/types';
import { getOrder } from '../features/orders/orders-api';
import { ApiError, getErrorMessage } from '../shared/api/api-error';
import { isDemoMode } from '../shared/config/api';
import { CommerceShell } from '../shared/layout/CommerceShell';
import { ServicePromises } from '../shared/layout/ServicePromises';
import { Button } from '../shared/ui/Button';
import { Icon } from '../shared/ui/Icon';
import { formatDateTime, formatMoney } from '../shared/utils/format';

function normalizeChannel(value: string | null | undefined): PaymentChannel {
  return value?.toLowerCase() === 'wechatpay' ? 'WeChatPay' : 'Alipay';
}

function initialDemoStatus(search: string): PaymentStatus {
  return new URLSearchParams(search).get('status') === 'success' ? 'Succeeded' : 'Pending';
}

function usePaymentCountdown(expiresOnUtc?: string) {
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    if (!expiresOnUtc) return undefined;
    setNow(Date.now());
    const timer = window.setInterval(() => setNow(Date.now()), 1_000);
    return () => window.clearInterval(timer);
  }, [expiresOnUtc]);

  const seconds = expiresOnUtc
    ? Math.max(0, Math.ceil((new Date(expiresOnUtc).getTime() - now) / 1_000))
    : 0;
  const minutesPart = Math.floor(seconds / 60).toString().padStart(2, '0');
  const secondsPart = (seconds % 60).toString().padStart(2, '0');
  return { seconds, label: `${minutesPart}:${secondsPart}` };
}

function isWaitingForPayment(error: unknown): boolean {
  return error instanceof ApiError && error.status === 404;
}

function paymentStatusCopy(status: PaymentStatus | undefined) {
  switch (status) {
    case 'Expired': return { title: '支付二维码已失效', detail: '本次支付已超过有效期，请返回订单页查看订单状态。' };
    case 'Failed': return { title: '支付未完成', detail: '支付服务没有完成本次收款，你可以刷新状态或返回订单页。' };
    case 'Closed': return { title: '支付已关闭', detail: '本次支付已关闭，请返回订单页查看后续操作。' };
    case 'Refunding': return { title: '退款处理中', detail: '款项正在按原支付渠道退回，请稍后在订单页查看。' };
    case 'Refunded': return { title: '款项已退回', detail: '本次支付已经完成退款，详情可在订单页查看。' };
    case 'Cancelling': return { title: '支付正在关闭', detail: '系统正在关闭本次支付，请稍后刷新状态。' };
    default: return null;
  }
}

export default function PaymentPage() {
  const { orderId = '' } = useParams();
  const location = useLocation();
  const demoMode = isDemoMode(location.search);
  const queryChannel = useMemo(
    () => normalizeChannel(new URLSearchParams(location.search).get('channel')),
    [location.search],
  );
  const [demoChannel, setDemoChannel] = useState<PaymentChannel>(queryChannel);
  const [demoStatus, setDemoStatus] = useState<PaymentStatus>(() => initialDemoStatus(location.search));
  const [demoRevision, setDemoRevision] = useState(1);
  const [checking, setChecking] = useState(false);
  const [feedback, setFeedback] = useState('');
  const [demoOrder] = useState(() => createDemoOrder(orderId || 'demo-order', queryChannel));

  const orderQuery = useQuery({
    queryKey: ['ordering', 'order', orderId],
    queryFn: () => getOrder(orderId),
    enabled: Boolean(orderId && !demoMode),
    staleTime: 15_000,
    retry: 1,
  });
  const paymentQuery = useQuery({
    queryKey: ['payment', 'order', orderId],
    queryFn: () => getPaymentByOrder(orderId),
    enabled: Boolean(orderId && !demoMode),
    retry: false,
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      return !status || status === 'Pending' || status === 'Cancelling' ? 3_000 : false;
    },
  });

  // 从支付宝收银台回跳（return_url 带 out_trade_no）时主动查询一次；回跳参数本身不作为支付成功依据。
  const returnedFromAlipay = new URLSearchParams(location.search).has('out_trade_no');
  const { refetch: refetchPayment } = paymentQuery;
  useEffect(() => {
    if (!orderId || demoMode || !returnedFromAlipay) return;
    void syncPayment(orderId).catch(() => undefined).then(() => refetchPayment());
  }, [orderId, demoMode, returnedFromAlipay, refetchPayment]);

  const order = demoMode ? demoOrder : orderQuery.data;
  const serverChannel = normalizeChannel(paymentQuery.data?.channel ?? order?.paymentMethod);
  const activeChannel = demoMode ? demoChannel : serverChannel;
  const demoPayment = demoMode
    ? {
        ...createDemoPayment(orderId || 'demo-order', demoChannel, demoOrder.paymentDueOnUtc, demoStatus),
        paymentUrl: `${createDemoPayment(orderId || 'demo-order', demoChannel, demoOrder.paymentDueOnUtc, demoStatus).paymentUrl}&revision=${demoRevision}`,
      }
    : undefined;
  const payment = demoPayment ?? paymentQuery.data;
  const pagePay = isPagePayForm(payment?.paymentUrl);
  const countdown = usePaymentCountdown(payment?.expiresOnUtc ?? order?.paymentDueOnUtc);
  const succeeded = payment?.status === 'Succeeded' || (!payment && order?.status === 'Confirmed');
  const expiredByClock = countdown.seconds === 0 && payment?.status === 'Pending';
  const terminal = order?.fulfillmentStatus === 'Cancelled'
    ? { title: '订单已取消', detail: order.cancellationReason ?? '该订单已关闭，无需继续支付。' }
    : paymentStatusCopy(expiredByClock ? 'Expired' : payment?.status);
  const ordersPath = `/orders${demoMode ? '?demo=1' : ''}`;
  const detailPath = demoMode ? ordersPath : `/orders/${orderId}`;

  const selectProvider = (channel: PaymentChannel) => {
    setFeedback('');
    if (demoMode) {
      setDemoChannel(channel);
      setDemoRevision((current) => current + 1);
      return;
    }
    if (channel !== serverChannel) {
      setFeedback('支付方式已在确认订单时锁定，如需更换请重新下单。');
    }
  };

  const refreshPayment = async () => {
    setChecking(true);
    setFeedback('');
    if (demoMode) {
      await new Promise((resolve) => window.setTimeout(resolve, 350));
      setDemoRevision((current) => current + 1);
      setFeedback('支付二维码与状态已刷新。');
    } else {
      const result = await paymentQuery.refetch();
      setFeedback(result.data?.status === 'Succeeded' ? '' : '支付状态已刷新，暂未收到成功通知。');
    }
    setChecking(false);
  };

  const confirmPayment = async () => {
    setChecking(true);
    setFeedback('');
    if (demoMode) {
      await new Promise((resolve) => window.setTimeout(resolve, 650));
      setDemoStatus('Succeeded');
    } else {
      await syncPayment(orderId).catch(() => undefined);
      const result = await paymentQuery.refetch();
      if (result.data?.status !== 'Succeeded') {
        setFeedback('暂未收到支付成功通知，请确认付款后稍候刷新。');
      }
    }
    setChecking(false);
  };

  if (!demoMode && orderQuery.isPending) {
    return <CommerceShell><div className="page-state payment-page-state"><span className="page-spinner" /><h1>正在加载订单…</h1></div></CommerceShell>;
  }

  if (!order) {
    return (
      <CommerceShell>
        <div className="page-state payment-page-state">
          <Icon name="receipt" />
          <h1>订单暂时无法打开</h1>
          <p>{getErrorMessage(orderQuery.error, '请返回订单列表后重试。')}</p>
          <Link className="button button--primary button--regular" to={ordersPath}>返回我的订单</Link>
        </div>
      </CommerceShell>
    );
  }

  return (
    <CommerceShell>
      <section className="payment-page">
        <Link className="orders-back" to={ordersPath}><Icon name="arrow-right" />返回我的订单</Link>
        <div className="payment-heading">
          <div>
            <h1>支付订单</h1>
            <p>订单已提交，请在有效期内完成支付</p>
            {demoMode ? <span className="demo-notice">开发预览模式 · 支付结果仅在本地模拟</span> : null}
          </div>
          <PaymentProgress currentStep={succeeded ? 3 : 2} />
        </div>

        {succeeded ? (
          <>
            <div className="payment-layout">
              <section className="payment-success-panel">
                <span className="payment-success-icon"><Icon name="circle-check" /></span>
                <h2>支付成功</h2>
                <strong>{formatMoney(order.amount, order.currency)}</strong>
                <p>我们已收到款项，订单正在准备中</p>
                <dl>
                  <div><dt>订单号</dt><dd>{order.orderNumber}</dd></div>
                  <div><dt>支付方式</dt><dd><PaymentProviderMark channel={activeChannel} />{paymentChannelLabel(activeChannel)}</dd></div>
                  <div><dt>支付时间</dt><dd>{formatDateTime(order.updatedOnUtc)}</dd></div>
                </dl>
                <div className="payment-success-actions">
                  <Link className="button button--primary button--regular" to={detailPath}>查看订单详情</Link>
                  <Link className="button button--secondary button--regular" to="/">继续购物</Link>
                </div>
              </section>
              <PaymentOrderSummary order={order} succeeded detailPath={detailPath} />
            </div>
            <section className="payment-next-steps">
              <h2>接下来</h2>
              <div>
                <span><Icon name="clipboard" /><strong>商家确认订单</strong><small>我们将尽快确认订单</small></span>
                <span><Icon name="package" /><strong>商品出库</strong><small>商品出库后，物流信息将更新</small></span>
                <span><Icon name="truck" /><strong>配送到家</strong><small>商品将送达您填写的收货地址</small></span>
              </div>
            </section>
          </>
        ) : terminal ? (
          <div className="payment-layout">
            <section className="payment-terminal-panel">
              <span><Icon name="alert-circle" /></span>
              <h2>{terminal.title}</h2>
              <p>{terminal.detail}</p>
              <div>
                <Button type="button" loading={checking} onClick={() => void refreshPayment()}>刷新支付状态</Button>
                <Link className="button button--secondary button--regular" to={detailPath}>查看订单详情</Link>
              </div>
            </section>
            <PaymentOrderSummary order={order} succeeded={false} detailPath={detailPath} />
          </div>
        ) : (
          <>
            <div className="payment-layout">
              <section className="payment-workspace">
                <h2>选择支付方式</h2>
                <PaymentProviderSelector selected={activeChannel} onSelect={selectProvider} />
                <div className="payment-scan-layout">
                  <div className="payment-qr">
                    <PaymentQrCode value={payment?.paymentUrl ?? null} label={`${paymentChannelLabel(activeChannel)}支付二维码`} />
                  </div>
                  <div className="payment-scan-copy">
                    <div className="payment-scan-title"><Icon name="qr-code" /><h3>{pagePay ? '前往支付宝收银台支付' : `使用${paymentChannelLabel(activeChannel)}扫码支付`}</h3></div>
                    <p>{pagePay ? '点击左侧按钮，在新窗口完成付款' : `请使用${paymentChannelLabel(activeChannel)}扫描二维码`}</p>
                    <p>{pagePay ? '付款后回到本页点击“我已完成支付”' : '支付完成后页面将自动更新'}</p>
                    <dl>
                      <div><dt>应付金额</dt><dd>{formatMoney(order.amount, order.currency)}</dd></div>
                      <div><dt>支付剩余</dt><dd className={countdown.seconds <= 180 ? 'is-warning' : undefined}>{countdown.label}</dd></div>
                    </dl>
                    <div className="payment-actions">
                      <Button type="button" loading={checking} disabled={!payment?.paymentUrl} onClick={() => void confirmPayment()}>我已完成支付</Button>
                      <Button type="button" variant="secondary" loading={checking} onClick={() => void refreshPayment()}><Icon name="refresh" />刷新支付状态</Button>
                    </div>
                  </div>
                </div>
                <p className="payment-live-status" role="status">
                  <Icon name="clock" />
                  {feedback || (paymentQuery.isError && !isWaitingForPayment(paymentQuery.error)
                    ? `支付状态加载失败：${getErrorMessage(paymentQuery.error)}`
                    : '等待支付中，请在有效期内完成支付')}
                </p>
              </section>
              <PaymentOrderSummary order={order} succeeded={false} detailPath={detailPath} />
            </div>
            <section className="payment-help-strip">
              <Icon name="help-circle" />
              <strong>支付遇到问题？</strong>
              <span>二维码失效可刷新；不想买了可以在<Link to={detailPath}>订单详情</Link>中取消订单。</span>
            </section>
          </>
        )}
      </section>
      <ServicePromises />
    </CommerceShell>
  );
}
